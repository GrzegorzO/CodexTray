using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexTray;

internal enum DisplayStyle
{
    Classic = 0,
    Futuristic = 1,
    Fluent = 2,
    Minimal = 3,
    HighContrast = 4,
    Neon = 5,
    Glass = 6,
    Pixel = 7,
    Carbon = 8,
    Dots = 9,
    Pencil = 10
}

internal enum LabelContentMode
{
    Static,
    TimeToReset
}

internal enum LabelPosition
{
    Left,
    Right
}

internal enum PercentagePosition
{
    Left,
    Center,
    Right
}

internal enum TaskbarPlacement
{
    Custom,
    Left,
    Right
}

internal sealed class TaskbarWidgetForm : Form
{
    // Windows does not expose a supported slot for third-party taskbar widgets,
    // so this compact window is positioned over a user-selected taskbar area.
    private const int DefaultOffset = 320;
    private const int EdgeGap = 8;
    private const int DefaultWidth = 94;
    private const int ExternalPercentageWidth = 38;
    private const int StaticLabelWidth = 25;
    private const int CountdownLabelWidth = 56;
    private static readonly Color TransparentSurface = Color.FromArgb(1, 0, 1);
    private readonly ToolTip _tooltip = new();
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly Screen _targetScreen;
    private bool _dragging;
    private int _dragStartMouseX;
    private int _dragStartWindowX;
    private UsageSnapshot? _snapshot;
    private bool _isError;
    private string? _errorMessage;
    private (string FiveHour, string Weekly)? _lastCountdownLabels;
    private int _workingAgentCount;
    private Color?[] _taskColors = [];
    private ProjectMarker?[] _taskMarkers = [];

    internal bool ShowAgentCounter { get; private set; } = true;
    internal bool AnimateAgentCounter { get; private set; } = true;
    internal LoadingIconStyle AgentIconStyle { get; private set; }
    internal AgentIndicatorPosition AgentCounterSide { get; private set; }
    internal AgentIndicatorSize AgentCounterSize { get; private set; }
    internal AgentIndicatorMode AgentCounterMode { get; private set; }
    internal bool SynchronizeAgentAnimations { get; private set; }
    internal bool UseProjectColors { get; private set; }
    internal bool ShowProjectIcons { get; private set; }
    internal int AgentCounterWidth => ShowAgentCounter
        ? AgentIndicatorRenderer.WidthFor(_workingAgentCount, AgentCounterSize, AgentCounterMode) : 0;
    internal Rectangle AgentCounterBounds => new(
        AgentCounterSide == AgentIndicatorPosition.Left ? 0 : ClientSize.Width - AgentCounterWidth,
        0, AgentCounterWidth, ClientSize.Height);

    public DisplayStyle Style { get; private set; }
    public bool ShowLabels { get; private set; } = true;
    public bool ShowPercentages { get; private set; } = true;
    public LabelContentMode LabelMode { get; private set; }
    public LabelPosition LabelSide { get; private set; }
    public PercentagePosition PercentageSide { get; private set; } = PercentagePosition.Center;

    public event EventHandler? DetailsRequested;
    public event EventHandler? PlacementCustomized;

    public string ScreenDeviceName => _targetScreen.DeviceName;
    public TaskbarWidgetForm(ContextMenuStrip menu, Screen targetScreen, bool loadSettings = true)
    {
        _targetScreen = targetScreen;
        if (loadSettings)
        {
            Style = WidgetSettings.SelectedStyle;
            ShowLabels = WidgetSettings.ShowLabels;
            ShowPercentages = WidgetSettings.ShowPercentages;
            LabelMode = WidgetSettings.SelectedLabelMode;
            LabelSide = WidgetSettings.SelectedLabelPosition;
            PercentageSide = WidgetSettings.SelectedPercentagePosition;
            ShowAgentCounter = WidgetSettings.ShowAgentCounter;
            AnimateAgentCounter = WidgetSettings.AnimateAgentCounter;
            AgentIconStyle = WidgetSettings.SelectedLoadingIconStyle;
            AgentCounterSide = WidgetSettings.SelectedAgentIndicatorPosition;
            AgentCounterSize = WidgetSettings.SelectedAgentIndicatorSize;
            AgentCounterMode = WidgetSettings.SelectedAgentIndicatorMode;
            SynchronizeAgentAnimations = WidgetSettings.SynchronizeAgentAnimations;
            UseProjectColors = WidgetSettings.UseProjectColors;
            ShowProjectIcons = WidgetSettings.ShowProjectIcons;
        }
        _tooltip.InitialDelay = 250;
        _tooltip.ReshowDelay = 100;
        _tooltip.AutoPopDelay = 10_000;
        _tooltip.ShowAlways = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = TransparentSurface;
        TransparencyKey = TransparentSurface;
        ClientSize = new Size(GetDesiredWidth(ShowLabels, ShowPercentages, LabelMode, PercentageSide), 42);
        ContextMenuStrip = menu;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        MouseDown += OnDragStart;
        MouseMove += OnDragMove;
        MouseUp += OnDragEnd;
        MouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                DetailsRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _countdownTimer.Tick += (_, _) => RefreshCountdownIfChanged();
        _countdownTimer.Start();
        _animationTimer = new System.Windows.Forms.Timer { Interval = 50 };
        // Color-key layered windows need a complete buffered frame. Partial
        // presents can expose an empty surface while another monitor plays video.
        _animationTimer.Tick += (_, _) => InvalidateAnimationFrame();
    }

    internal void InvalidateAnimationFrame() => Invalidate();

    internal void SetWorkingAgentCount(int count)
    {
        count = Math.Max(0, count);
        if (_workingAgentCount == count && _taskColors.Length == 0)
        {
            return;
        }

        var previousLeftInset = AgentCounterSide == AgentIndicatorPosition.Left ? AgentCounterWidth : 0;
        _workingAgentCount = count;
        _taskColors = [];
        _taskMarkers = [];
        UpdateAgentPresentation(previousLeftInset);
    }

    internal void SetWorkingTasks(IReadOnlyList<WorkingTask> tasks)
    {
        var colors = tasks.Select(task => task.ProjectColor).ToArray();
        var markers = tasks.Select(task => task.ProjectMarker).ToArray();
        if (_workingAgentCount == tasks.Count && _taskColors.SequenceEqual(colors) && _taskMarkers.SequenceEqual(markers))
        {
            return;
        }
        var previousLeftInset = AgentCounterSide == AgentIndicatorPosition.Left ? AgentCounterWidth : 0;
        _workingAgentCount = tasks.Count;
        _taskColors = colors;
        _taskMarkers = markers;
        UpdateAgentPresentation(previousLeftInset);
    }

    internal void ApplyAgentSettings(bool show, bool animate, LoadingIconStyle style, AgentIndicatorPosition side,
        AgentIndicatorSize size = AgentIndicatorSize.Normal, AgentIndicatorMode mode = AgentIndicatorMode.IconAndNumber,
        bool synchronizeAnimations = false, bool useProjectColors = false, bool showProjectIcons = false)
    {
        var previousLeftInset = AgentCounterSide == AgentIndicatorPosition.Left ? AgentCounterWidth : 0;
        ShowAgentCounter = show;
        AnimateAgentCounter = animate;
        AgentIconStyle = style;
        AgentCounterSide = side;
        AgentCounterSize = size;
        AgentCounterMode = mode;
        SynchronizeAgentAnimations = synchronizeAnimations;
        UseProjectColors = useProjectColors;
        ShowProjectIcons = showProjectIcons;
        UpdateAgentPresentation(previousLeftInset);
    }

    private void UpdateAgentPresentation(int previousLeftInset)
    {
        UpdateWidth(previousLeftInset);
        UpdateTooltip();
        UpdateAnimation();
        Invalidate();
    }

    private void UpdateWidth(int? previousLeftInset = null)
    {
        var width = GetDesiredWidth(ShowLabels, ShowPercentages, LabelMode, PercentageSide) + AgentCounterWidth;
        if (ClientSize.Width == width)
        {
            return;
        }

        ClientSize = new Size(width, ClientSize.Height);
        if (Visible)
        {
            if (previousLeftInset.HasValue)
            {
                var taskbar = _targetScreen.Bounds;
                var hasTaskbar = TryGetTaskbar(_targetScreen, out var handle, out var actualTaskbar);
                if (hasTaskbar)
                {
                    taskbar = actualTaskbar;
                }
                var gap = ScaleLogical(EdgeGap);
                var maxX = hasTaskbar ? GetRightSideX(handle, taskbar, gap) : taskbar.Right - Width - gap;
                var newLeftInset = AgentCounterSide == AgentIndicatorPosition.Left ? AgentCounterWidth : 0;
                var minX = taskbar.Left + gap;
                Location = new Point(Math.Clamp(Location.X + previousLeftInset.Value - newLeftInset, minX, Math.Max(minX, maxX)), Location.Y);
            }
            else
            {
                PlaceBesideWeather();
            }
        }
    }

    private void UpdateTooltip()
    {
        var text = _isError ? $"Codex usage error: {_errorMessage}" : _snapshot?.ToTooltip() ?? "Codex usage is still loading.";
        if (AgentCounterWidth > 0)
        {
            text += $"\n{_workingAgentCount} running main {(_workingAgentCount == 1 ? "task" : "tasks")}";
        }

        _tooltip.SetToolTip(this, text);
        AccessibleName = text;
    }

    private void UpdateAnimation() => _animationTimer.Enabled = Visible && AgentCounterWidth > 0 && AnimateAgentCounter;

    protected override void OnVisibleChanged(EventArgs eventArgs)
    {
        base.OnVisibleChanged(eventArgs);
        if (_animationTimer is not null)
        {
            UpdateAnimation();
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        if (!_isError && Equals(_snapshot, snapshot))
        {
            return;
        }

        _snapshot = snapshot;
        _isError = false;
        _errorMessage = null;
        _lastCountdownLabels = CurrentCountdownLabels();
        UpdateTooltip();
        Invalidate();
    }

    public void SetError(string message)
    {
        if (_isError && string.Equals(_errorMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        _isError = true;
        _errorMessage = message;
        UpdateTooltip();
        Invalidate();
    }

    public void ApplyDisplaySettings(
        DisplayStyle style,
        bool showLabels,
        bool showPercentages,
        LabelContentMode labelMode,
        LabelPosition labelPosition,
        PercentagePosition percentagePosition)
    {
        if (Style == style && ShowLabels == showLabels && ShowPercentages == showPercentages &&
            LabelMode == labelMode && LabelSide == labelPosition && PercentageSide == percentagePosition)
        {
            return;
        }

        Style = style;
        ShowLabels = showLabels;
        ShowPercentages = showPercentages;
        LabelMode = labelMode;
        LabelSide = labelPosition;
        PercentageSide = percentagePosition;
        UpdateWidth();
        _lastCountdownLabels = CurrentCountdownLabels();
        Invalidate();
    }

    public void PlaceBesideWeather()
    {
        if (!TryGetTaskbar(_targetScreen, out var taskbarHandle, out var taskbar))
        {
            var screen = _targetScreen.Bounds;
            taskbar = new Rectangle(screen.Left, screen.Bottom - ScaleLogical(48), screen.Width, ScaleLogical(48));
            var fallbackX = WidgetSettings.SelectedPlacement switch
            {
                TaskbarPlacement.Left => taskbar.Left + ScaleLogical(EdgeGap),
                TaskbarPlacement.Right => taskbar.Right - Width - ScaleLogical(EdgeGap),
                _ => taskbar.Left + (WidgetSettings.GetOffsetX(_targetScreen.DeviceName) ?? ScaleLogical(DefaultOffset))
            };
            fallbackX = Math.Clamp(
                fallbackX,
                taskbar.Left + ScaleLogical(EdgeGap),
                taskbar.Right - Width - ScaleLogical(EdgeGap));
            Location = new Point(fallbackX, taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2));
            return;
        }

        SetTaskbarOwner(taskbarHandle);
        var edgeGap = ScaleLogical(EdgeGap);
        var x = WidgetSettings.SelectedPlacement switch
        {
            TaskbarPlacement.Left => taskbar.Left + edgeGap,
            TaskbarPlacement.Right => GetRightSideX(taskbarHandle, taskbar, edgeGap),
            _ => taskbar.Left + (WidgetSettings.GetOffsetX(_targetScreen.DeviceName) ?? ScaleLogical(DefaultOffset))
        };
        x = Math.Clamp(x, taskbar.Left + edgeGap, taskbar.Right - Width - edgeGap);
        var y = taskbar.Top + Math.Max(0, (taskbar.Height - Height) / 2);
        // Keep the widget in the topmost z-order band. Using HWND_TOP here can
        // demote a topmost window, allowing Windows 11's taskbar surface to
        // cover it the next time the taskbar is activated.
        SetWindowPos(Handle, new IntPtr(-1), x, y, Width, Height, SwpNoActivate | SwpShowWindow);
    }

    public void RestoreTaskbarZOrder()
    {
        if (!Visible || IsDisposed)
        {
            return;
        }

        if (TryGetTaskbar(_targetScreen, out var taskbarHandle, out _))
        {
            SetTaskbarOwner(taskbarHandle);
        }

        SetWindowPos(
            Handle,
            new IntPtr(-1),
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoRedraw);
    }

    public bool IsTaskbarVisible()
    {
        if (!TryGetTaskbar(_targetScreen, out var taskbarHandle, out var taskbar) ||
            !IsWindowVisible(taskbarHandle))
        {
            return false;
        }

        var visibleTaskbar = Rectangle.Intersect(taskbar, _targetScreen.Bounds);
        var isHorizontal = taskbar.Width >= taskbar.Height;
        var visibleThickness = isHorizontal ? visibleTaskbar.Height : visibleTaskbar.Width;
        var taskbarThickness = isHorizontal ? taskbar.Height : taskbar.Width;
        if (visibleThickness < Math.Min(taskbarThickness, ScaleLogical(16)))
        {
            return false;
        }

        // Inspect covering top-level windows above this monitor's taskbar.
        // Pixel hit tests can hit our own animated, color-keyed window and feed
        // its current frame back into the visibility decision.
        var covered = false;
        EnumWindows((window, parameter) =>
        {
            if (window == taskbarHandle)
            {
                return false;
            }
            if (!IsWindowVisible(window) || IsIconic(window))
            {
                return true;
            }
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId || !TryGetVisibleWindowBounds(window, out var bounds) ||
                !TaskbarVisibility.CoversMonitor(bounds, _targetScreen.Bounds))
            {
                return true;
            }
            _ = DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int));
            if (cloaked != 0)
            {
                return true;
            }
            if (IsWindowsShellSurface(window))
            {
                // Start/Search may own a transparent fullscreen input surface
                // while intentionally revealing the taskbar beneath it.
                return false;
            }
            covered = TaskbarVisibility.BlocksTaskbar(bounds, _targetScreen.Bounds,
                visible: true, minimized: false, cloaked: false, ownWindow: false, shellSurface: false);
            return !covered;
        }, IntPtr.Zero);

        return !covered;
    }

    private static bool TryGetVisibleWindowBounds(IntPtr window, out Rectangle bounds)
    {
        // DWM excludes invisible resize borders, so a maximized browser is not
        // mistaken for fullscreen. GetWindowRect covers non-DWM windows.
        if (DwmGetWindowAttribute(window, 9, out NativeRectangle native, Marshal.SizeOf<NativeRectangle>()) != 0 &&
            !GetWindowRect(window, out native))
        {
            bounds = Rectangle.Empty;
            return false;
        }
        bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return true;
    }

    private static bool IsWindowsShellSurface(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return IsShellProcessName(process.ProcessName);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool IsShellProcessName(string processName) => processName is
        "explorer" or
        "MicrosoftStartFeedProvider" or
        "SearchApp" or
        "SearchHost" or
        "ShellExperienceHost" or
        "ShellHost" or
        "StartMenuExperienceHost" or
        "TextInputHost";

    private void SetTaskbarOwner(IntPtr taskbarHandle)
    {
        if (GetWindowOwner(Handle) == taskbarHandle)
        {
            return;
        }

        SetWindowOwner(Handle, taskbarHandle);
    }

    private void RefreshCountdownIfChanged()
    {
        if (!ShowLabels || LabelMode != LabelContentMode.TimeToReset)
        {
            return;
        }

        var labels = CurrentCountdownLabels();
        if (_lastCountdownLabels == labels)
        {
            return;
        }

        _lastCountdownLabels = labels;
        Invalidate();
    }

    private (string FiveHour, string Weekly) CurrentCountdownLabels()
    {
        var now = DateTimeOffset.Now;
        return (FormatResetLabel(_snapshot?.FiveHour, now), FormatResetLabel(_snapshot?.Weekly, now));
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        DrawWidget(eventArgs.Graphics);
    }

    internal Bitmap RenderPreview(Color background, float scale = 2f)
    {
        var bitmap = new Bitmap(
            (int)Math.Ceiling(Width * scale),
            (int)Math.Ceiling(Height * scale));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(background);
        graphics.ScaleTransform(scale, scale);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        DrawWidget(graphics);
        return bitmap;
    }

    private void DrawWidget(Graphics graphics)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var state = graphics.Save();
        if (AgentCounterSide == AgentIndicatorPosition.Left)
        {
            graphics.TranslateTransform(AgentCounterWidth, 0);
        }

        DrawRow(graphics, "5h", 3, _snapshot?.FiveHour);
        DrawRow(graphics, "7d", 22, _snapshot?.Weekly);

        if (_isError)
        {
            using var pen = new Pen(Color.FromArgb(235, 65, 65), 3);
            var contentWidth = ClientSize.Width - AgentCounterWidth;
            graphics.DrawLine(pen, contentWidth - 15, 4, contentWidth - 4, 15);
            graphics.DrawLine(pen, contentWidth - 4, 4, contentWidth - 15, 15);
        }
        graphics.Restore(state);

        if (AgentCounterWidth > 0)
        {
            var duration = AgentIndicatorRenderer.CycleDuration(AgentIconStyle);
            var phase = AnimateAgentCounter ? (Environment.TickCount64 % duration) / (float)duration : 0.25f;
            AgentIndicatorRenderer.Draw(graphics, AgentCounterBounds, _workingAgentCount, AgentIconStyle, phase,
                AgentCounterSize, AgentCounterMode, AgentCounterSide, SynchronizeAgentAnimations,
                (UseProjectColors || ShowProjectIcons) && AgentCounterMode == AgentIndicatorMode.RepeatedIcons ? _taskColors : null,
                ShowProjectIcons && AgentCounterMode == AgentIndicatorMode.RepeatedIcons ? _taskMarkers : null,
                colorLoadingIcons: UseProjectColors);
        }
    }

    private void DrawRow(Graphics graphics, string staticLabel, int y, UsageWindow? window)
    {
        var remainingPercent = window?.RemainingPercent;
        var labelOnLeft = ShowLabels && LabelSide == LabelPosition.Left;
        var labelOnRight = ShowLabels && LabelSide == LabelPosition.Right;
        var percentageOnLeft = ShowPercentages && PercentageSide == PercentagePosition.Left;
        var percentageOnRight = ShowPercentages && PercentageSide == PercentagePosition.Right;
        var labelWidth = LabelMode == LabelContentMode.TimeToReset ? CountdownLabelWidth : StaticLabelWidth;
        var contentLeft = 3;
        var contentRight = ClientSize.Width - AgentCounterWidth - 7;
        Rectangle? externalPercentageBounds = null;

        if (percentageOnLeft)
        {
            externalPercentageBounds = new Rectangle(contentLeft, y, ExternalPercentageWidth - 2, 16);
            contentLeft += ExternalPercentageWidth;
        }

        var labelX = 0;
        if (labelOnLeft)
        {
            labelX = contentLeft;
            contentLeft += labelWidth;
        }

        if (percentageOnRight)
        {
            contentRight -= ExternalPercentageWidth;
            externalPercentageBounds = new Rectangle(contentRight + 2, y, ExternalPercentageWidth - 2, 16);
        }

        if (labelOnRight)
        {
            contentRight -= labelWidth;
            labelX = contentRight + 2;
        }

        var batteryBounds = new Rectangle(contentLeft, y, Math.Max(12, contentRight - contentLeft), 16);

        if (ShowLabels)
        {
            var label = LabelMode == LabelContentMode.TimeToReset
                ? FormatResetLabel(window, DateTimeOffset.Now)
                : staticLabel;
            using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
            using var labelBrush = new SolidBrush(Style switch
            {
                DisplayStyle.Futuristic => Color.FromArgb(125, 225, 255),
                DisplayStyle.Minimal => Color.FromArgb(218, 220, 224),
                _ => Color.White
            });
            graphics.DrawString(label, font, labelBrush, new PointF(labelX, y + 1));
        }

        switch (Style)
        {
            case DisplayStyle.Futuristic:
                DrawFuturisticBattery(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Fluent:
                DrawFluentBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Minimal:
                DrawMinimalBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.HighContrast:
                DrawHighContrastBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Neon:
                DrawNeonBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Glass:
                DrawGlassBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Pixel:
                DrawPixelBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Carbon:
                DrawCarbonBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Dots:
                DrawDotsBar(graphics, batteryBounds, remainingPercent);
                break;
            case DisplayStyle.Pencil:
                DrawPencilBar(graphics, batteryBounds, remainingPercent);
                break;
            default:
                DrawClassicBattery(graphics, batteryBounds, remainingPercent);
                break;
        }

        if (externalPercentageBounds is { } percentageBounds)
        {
            DrawPercentageText(graphics, percentageBounds, remainingPercent, Color.White);
        }
    }

    internal static string FormatResetLabel(UsageWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is null)
        {
            return "--";
        }

        var remaining = window.ResetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)remaining.TotalMinutes)}m";
        }

        if (remaining < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)remaining.TotalHours)}h {remaining.Minutes}m";
        }

        return $"{Math.Max(1, (int)remaining.TotalDays)}d {remaining.Hours}h";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Stop();
            _countdownTimer.Dispose();
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _tooltip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawClassicBattery(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = new Rectangle(bounds.X, bounds.Y, bounds.Width - 4, bounds.Height);
        var terminal = new Rectangle(body.Right, body.Y + 4, 4, 8);
        var outlineColor = remainingPercent is null ? Color.FromArgb(140, 150, 160) : Color.White;
        using var emptyBrush = new SolidBrush(Color.FromArgb(46, 50, 54));
        using var outlinePen = new Pen(outlineColor, 2);
        graphics.FillRectangle(emptyBrush, body.X + 2, body.Y + 2, body.Width - 4, body.Height - 4);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var interior = new Rectangle(body.X + 3, body.Y + 3, body.Width - 6, body.Height - 6);
            var fillWidth = percent == 0 ? 0 : Math.Max(1, (int)Math.Round(interior.Width * percent / 100d));
            if (fillWidth > 0)
            {
                using var fillBrush = new SolidBrush(TrayIconRenderer.ColorFor(percent));
                graphics.FillRectangle(fillBrush, interior.X, interior.Y, fillWidth, interior.Height);
            }
        }

        graphics.DrawRectangle(outlinePen, body);
        using var terminalBrush = new SolidBrush(outlineColor);
        graphics.FillRectangle(terminalBrush, terminal);

        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawFuturisticBattery(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        using var path = new GraphicsPath();
        path.AddRectangle(body);
        using var emptyBrush = new SolidBrush(Color.FromArgb(18, 35, 43));
        graphics.FillPath(emptyBrush, path);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var fillWidth = (int)Math.Round(body.Width * percent / 100d);
            if (fillWidth > 0)
            {
                var color = TrayIconRenderer.ColorFor(percent);
                var state = graphics.Save();
                graphics.SetClip(path);
                using var glow = new LinearGradientBrush(
                    body,
                    Color.FromArgb(120, color),
                    Color.FromArgb(255, color),
                    LinearGradientMode.Horizontal);
                graphics.FillRectangle(glow, body.X, body.Y + 2, fillWidth, body.Height - 3);
                using var segmentPen = new Pen(Color.FromArgb(110, 8, 18, 22), 1);
                for (var x = body.X + 9; x < body.X + fillWidth; x += 9)
                {
                    graphics.DrawLine(segmentPen, x, body.Top + 2, x, body.Bottom - 2);
                }
                graphics.Restore(state);
            }
        }

        using var outlinePen = new Pen(
            remainingPercent is null ? Color.FromArgb(90, 130, 145) : Color.FromArgb(125, 225, 255),
            1.6f);
        graphics.DrawPath(outlinePen, path);
        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawFluentBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = Rectangle.Inflate(bounds, -1, -1);
        using var track = CreateRoundedRectangle(body, 4);
        using var trackBrush = new SolidBrush(Color.FromArgb(58, 60, 66));
        graphics.FillPath(trackBrush, track);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var fillWidth = percent == 0 ? 0 : Math.Max(2, (int)Math.Round(body.Width * percent / 100d));
            if (fillWidth > 0)
            {
                var fillBounds = new Rectangle(body.X, body.Y, Math.Min(fillWidth, body.Width), body.Height);
                using var fill = CreateRoundedRectangle(fillBounds, Math.Min(4, fillBounds.Width / 2f));
                var color = TrayIconRenderer.ColorFor(percent);
                using var fillBrush = new LinearGradientBrush(
                    fillBounds,
                    ControlPaint.Light(color, 0.25f),
                    color,
                    LinearGradientMode.Vertical);
                graphics.FillPath(fillBrush, fill);
            }
        }

        using var outline = new Pen(Color.FromArgb(105, 255, 255, 255), 1);
        graphics.DrawPath(outline, track);
        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawMinimalBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        const int height = 5;
        var body = new Rectangle(bounds.X, bounds.Y + (bounds.Height - height) / 2, bounds.Width, height);
        using var track = CreateRoundedRectangle(body, height / 2f);
        using var trackBrush = new SolidBrush(Color.FromArgb(78, 80, 86));
        graphics.FillPath(trackBrush, track);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var fillWidth = percent == 0 ? 0 : Math.Max(2, (int)Math.Round(body.Width * percent / 100d));
            if (fillWidth > 0)
            {
                var fillBounds = new Rectangle(body.X, body.Y, Math.Min(fillWidth, body.Width), body.Height);
                using var fill = CreateRoundedRectangle(fillBounds, Math.Min(height / 2f, fillBounds.Width / 2f));
                using var fillBrush = new SolidBrush(TrayIconRenderer.ColorFor(percent));
                graphics.FillPath(fillBrush, fill);
            }
        }

        DrawPercentage(graphics, bounds, remainingPercent, Color.White);
    }

    private void DrawHighContrastBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = Rectangle.Inflate(bounds, -1, -1);
        using var emptyBrush = new SolidBrush(Color.Black);
        graphics.FillRectangle(emptyBrush, body);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var interior = Rectangle.Inflate(body, -2, -2);
            var fillWidth = percent == 0 ? 0 : Math.Max(1, (int)Math.Round(interior.Width * percent / 100d));
            if (fillWidth > 0)
            {
                var fillBounds = new Rectangle(interior.X, interior.Y, fillWidth, interior.Height);
                var color = TrayIconRenderer.ColorFor(percent);
                using var fillBrush = new SolidBrush(color);
                using var patternBrush = new HatchBrush(
                    HatchStyle.ForwardDiagonal,
                    Color.FromArgb(150, Color.Black),
                    Color.Transparent);
                graphics.FillRectangle(fillBrush, fillBounds);
                graphics.FillRectangle(patternBrush, fillBounds);
            }
        }

        using var outline = new Pen(Color.White, 2);
        graphics.DrawRectangle(outline, body);
        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawNeonBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = Rectangle.Inflate(bounds, -2, -2);
        var percent = Math.Clamp(remainingPercent ?? 0, 0, 100);
        var color = remainingPercent is null ? Color.FromArgb(100, 115, 125) : TrayIconRenderer.ColorFor(percent);
        using var track = CreateRoundedRectangle(body, 3);
        using var trackBrush = new SolidBrush(Color.FromArgb(8, 12, 16));
        using var glowPen = new Pen(Color.FromArgb(85, color), 4);
        using var edgePen = new Pen(color, 1);
        graphics.FillPath(trackBrush, track);
        graphics.DrawPath(glowPen, track);
        graphics.DrawPath(edgePen, track);

        if (remainingPercent is not null && percent > 0)
        {
            var interior = Rectangle.Inflate(body, -2, -2);
            var fillWidth = Math.Max(1, (int)Math.Round(interior.Width * percent / 100d));
            using var fillBrush = new SolidBrush(Color.FromArgb(220, color));
            graphics.FillRectangle(fillBrush, interior.X, interior.Y, fillWidth, interior.Height);
        }

        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawGlassBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = Rectangle.Inflate(bounds, -1, -1);
        using var track = CreateRoundedRectangle(body, 5);
        using var trackBrush = new LinearGradientBrush(
            body,
            Color.FromArgb(82, 88, 96),
            Color.FromArgb(24, 28, 34),
            LinearGradientMode.Vertical);
        graphics.FillPath(trackBrush, track);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var fillWidth = percent == 0 ? 0 : Math.Max(2, (int)Math.Round(body.Width * percent / 100d));
            if (fillWidth > 0)
            {
                var fillBounds = new Rectangle(body.X, body.Y, Math.Min(fillWidth, body.Width), body.Height);
                using var fill = CreateRoundedRectangle(fillBounds, Math.Min(5, fillBounds.Width / 2f));
                var color = TrayIconRenderer.ColorFor(percent);
                using var fillBrush = new LinearGradientBrush(
                    fillBounds,
                    ControlPaint.LightLight(color),
                    ControlPaint.Dark(color, 0.25f),
                    LinearGradientMode.Vertical);
                graphics.FillPath(fillBrush, fill);
            }
        }

        using var shinePen = new Pen(Color.FromArgb(150, Color.White), 1);
        graphics.DrawArc(shinePen, body.X + 3, body.Y + 2, Math.Max(1, body.Width - 6), Math.Max(1, body.Height - 5), 190, 160);
        using var outline = new Pen(Color.FromArgb(150, 235, 240, 245), 1);
        graphics.DrawPath(outline, track);
        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawPixelBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        const int cellCount = 10;
        const int gap = 1;
        var percent = Math.Clamp(remainingPercent ?? 0, 0, 100);
        var litCells = remainingPercent is null ? 0 : (int)Math.Ceiling(percent / 10d);
        var cellWidth = Math.Max(2, (bounds.Width - ((cellCount - 1) * gap)) / cellCount);
        var totalWidth = (cellWidth * cellCount) + ((cellCount - 1) * gap);
        var startX = bounds.X + Math.Max(0, (bounds.Width - totalWidth) / 2);
        var color = TrayIconRenderer.ColorFor(percent);

        for (var index = 0; index < cellCount; index++)
        {
            var cell = new Rectangle(startX + (index * (cellWidth + gap)), bounds.Y + 2, cellWidth, bounds.Height - 4);
            using var brush = new SolidBrush(index < litCells ? color : Color.FromArgb(55, 59, 64));
            graphics.FillRectangle(brush, cell);
        }

        DrawPercentage(graphics, bounds, remainingPercent, Color.White);
    }

    private void DrawCarbonBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = Rectangle.Inflate(bounds, -1, -1);
        using var track = CreateRoundedRectangle(body, 2);
        using var trackBrush = new HatchBrush(
            HatchStyle.DiagonalCross,
            Color.FromArgb(48, 52, 58),
            Color.FromArgb(16, 18, 21));
        graphics.FillPath(trackBrush, track);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var fillWidth = percent == 0 ? 0 : Math.Max(2, (int)Math.Round(body.Width * percent / 100d));
            if (fillWidth > 0)
            {
                var fillBounds = new Rectangle(body.X, body.Y, Math.Min(fillWidth, body.Width), body.Height);
                using var fill = CreateRoundedRectangle(fillBounds, Math.Min(2, fillBounds.Width / 2f));
                var color = TrayIconRenderer.ColorFor(percent);
                using var fillBrush = new LinearGradientBrush(
                    fillBounds,
                    ControlPaint.Light(color, 0.2f),
                    ControlPaint.Dark(color, 0.25f),
                    LinearGradientMode.Vertical);
                graphics.FillPath(fillBrush, fill);

                var state = graphics.Save();
                graphics.SetClip(fill);
                using var weavePen = new Pen(Color.FromArgb(70, Color.Black), 1);
                for (var x = fillBounds.Left - fillBounds.Height; x < fillBounds.Right; x += 5)
                {
                    graphics.DrawLine(weavePen, x, fillBounds.Bottom, x + fillBounds.Height, fillBounds.Top);
                }
                graphics.Restore(state);
            }
        }

        using var topEdge = new Pen(Color.FromArgb(115, 235, 238, 242), 1);
        graphics.DrawLine(topEdge, body.Left + 2, body.Top + 1, body.Right - 2, body.Top + 1);
        using var outline = new Pen(Color.FromArgb(145, 118, 124, 132), 1);
        graphics.DrawPath(outline, track);
        DrawPercentage(graphics, body, remainingPercent, Color.White);
    }

    private void DrawDotsBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        const int dotCount = 10;
        const int gap = 2;
        var diameter = Math.Max(3, Math.Min(bounds.Height - 6, (bounds.Width - ((dotCount - 1) * gap)) / dotCount));
        var totalWidth = (diameter * dotCount) + ((dotCount - 1) * gap);
        var startX = bounds.X + Math.Max(0, (bounds.Width - totalWidth) / 2);
        var y = bounds.Y + (bounds.Height - diameter) / 2;
        var percent = Math.Clamp(remainingPercent ?? 0, 0, 100);
        var litDots = remainingPercent is null ? 0 : (int)Math.Ceiling(percent / 10d);
        var color = TrayIconRenderer.ColorFor(percent);

        for (var index = 0; index < dotCount; index++)
        {
            var dot = new Rectangle(startX + (index * (diameter + gap)), y, diameter, diameter);
            using var brush = new SolidBrush(index < litDots ? color : Color.FromArgb(65, 68, 74));
            graphics.FillEllipse(brush, dot);
        }

        DrawPercentage(graphics, bounds, remainingPercent, Color.White);
    }

    private void DrawPencilBar(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = new Rectangle(bounds.X, bounds.Y + 1, Math.Max(8, bounds.Width - 4), bounds.Height - 2);
        var terminal = new Rectangle(body.Right, body.Y + 4, 4, Math.Max(3, body.Height - 8));
        var interior = Rectangle.Inflate(body, -2, -2);
        var paper = Color.FromArgb(235, 232, 224);
        var graphite = Color.FromArgb(42, 40, 38);
        using var paperBrush = new SolidBrush(paper);
        graphics.FillRectangle(paperBrush, body);
        graphics.FillRectangle(paperBrush, terminal);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var fillWidth = percent == 0 ? 0 : Math.Max(1, (int)Math.Round(interior.Width * percent / 100d));
            if (fillWidth > 0)
            {
                var fill = new Rectangle(interior.X, interior.Y, fillWidth, interior.Height);
                graphics.FillRectangle(paperBrush, fill);

                var state = graphics.Save();
                graphics.SetClip(fill);
                using var hatchPen = new Pen(Color.FromArgb(225, graphite), 1);
                for (var x = fill.Left - fill.Height; x < fill.Right; x += 3)
                {
                    graphics.DrawLine(hatchPen, x, fill.Bottom, x + fill.Height, fill.Top);
                }
                graphics.Restore(state);

                using var fillEdge = new Pen(Color.FromArgb(180, graphite), 1);
                graphics.DrawLine(fillEdge, fill.Right, fill.Top, fill.Right - 1, fill.Bottom);
            }
        }

        using var graphitePen = new Pen(graphite, 1.15f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var left = body.Left;
        var right = body.Right;
        var top = body.Top;
        var bottom = body.Bottom;
        graphics.DrawLines(graphitePen,
        [
            new Point(left, top + 1),
            new Point(left + (body.Width / 3), top),
            new Point(left + ((body.Width * 2) / 3), top + 1),
            new Point(right, top)
        ]);
        graphics.DrawLines(graphitePen,
        [
            new Point(left, bottom - 1),
            new Point(left + (body.Width / 3), bottom),
            new Point(left + ((body.Width * 2) / 3), bottom - 1),
            new Point(right, bottom)
        ]);
        graphics.DrawLine(graphitePen, left + 1, top, left, bottom);
        graphics.DrawLine(graphitePen, right, top, right - 1, bottom);
        graphics.DrawLine(graphitePen, terminal.Left, terminal.Top, terminal.Right, terminal.Top + 1);
        graphics.DrawLine(graphitePen, terminal.Right, terminal.Top + 1, terminal.Right - 1, terminal.Bottom);
        graphics.DrawLine(graphitePen, terminal.Right - 1, terminal.Bottom, terminal.Left, terminal.Bottom - 1);

        using var secondStroke = new Pen(Color.FromArgb(135, graphite), 1);
        graphics.DrawLine(secondStroke, left + 1, top + 2, right - 2, top + 1);
        graphics.DrawLine(secondStroke, left + 2, bottom - 2, right - 1, bottom - 1);
        graphics.DrawLine(secondStroke, left + 2, top + 1, left + 1, bottom - 2);

        DrawPercentage(graphics, body, remainingPercent, graphite);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DrawPercentage(Graphics graphics, Rectangle bounds, int? remainingPercent, Color color)
    {
        if (!ShowPercentages || PercentageSide != PercentagePosition.Center)
        {
            return;
        }

        DrawPercentageText(graphics, bounds, remainingPercent, color);
    }

    private static void DrawPercentageText(Graphics graphics, Rectangle bounds, int? remainingPercent, Color color)
    {
        var text = remainingPercent is null ? "--" : $"{remainingPercent}%";
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        var shadowColor = color.GetBrightness() < 0.45f
            ? Color.FromArgb(190, 255, 255, 255)
            : Color.FromArgb(190, 0, 0, 0);
        using var shadowBrush = new SolidBrush(shadowColor);
        using var textBrush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var textBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        graphics.DrawString(text, font, shadowBrush, new RectangleF(textBounds.X + 1, textBounds.Y + 1, textBounds.Width, textBounds.Height), format);
        graphics.DrawString(text, font, textBrush, textBounds, format);
    }

    private void OnDragStart(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragStartMouseX = Cursor.Position.X;
        _dragStartWindowX = Left;
        Capture = true;
    }

    private void OnDragMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }

        if (!TryGetTaskbar(_targetScreen, out _, out var taskbar))
        {
            return;
        }

        var desiredX = _dragStartWindowX + Cursor.Position.X - _dragStartMouseX;
        var edgeGap = ScaleLogical(EdgeGap);
        Left = Math.Clamp(desiredX, taskbar.Left + edgeGap, taskbar.Right - Width - edgeGap);
    }

    private void OnDragEnd(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging || eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        if (TryGetTaskbar(_targetScreen, out _, out var taskbar))
        {
            WidgetSettings.SetOffsetX(_targetScreen.DeviceName, Left - taskbar.Left);
            WidgetSettings.SelectedPlacement = TaskbarPlacement.Custom;
            PlacementCustomized?.Invoke(this, EventArgs.Empty);
        }
    }

    private int ScaleLogical(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    private static int GetDesiredWidth(
        bool showLabels,
        bool showPercentages,
        LabelContentMode labelMode,
        PercentagePosition position) =>
        DefaultWidth +
        (showLabels && labelMode == LabelContentMode.TimeToReset ? CountdownLabelWidth - StaticLabelWidth : 0) +
        (showPercentages && position != PercentagePosition.Center ? ExternalPercentageWidth : 0);

    private static bool TryGetTaskbar(Screen targetScreen, out IntPtr taskbarHandle, out Rectangle rectangle)
    {
        Rectangle? match = null;
        var matchHandle = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            var className = GetWindowClassName(handle);
            if (className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd") ||
                !GetWindowRect(handle, out var native))
            {
                return true;
            }

            var candidate = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            if (candidate.IntersectsWith(targetScreen.Bounds))
            {
                match = candidate;
                matchHandle = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (match is { } taskbar)
        {
            taskbarHandle = matchHandle;
            rectangle = taskbar;
            return true;
        }

        taskbarHandle = IntPtr.Zero;
        rectangle = Rectangle.Empty;
        return false;
    }

    private int GetRightSideX(IntPtr taskbarHandle, Rectangle taskbar, int edgeGap)
    {
        int? notificationAreaLeft = null;
        EnumChildWindows(taskbarHandle, (handle, _) =>
        {
            if (GetWindowClassName(handle) == "TrayNotifyWnd" && GetWindowRect(handle, out var native))
            {
                notificationAreaLeft = native.Left;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return (notificationAreaLeft ?? taskbar.Right) - Width - edgeGap;
    }

    private static IntPtr GetWindowOwner(IntPtr handle) => IntPtr.Size == 8
        ? GetWindowLongPtr64(handle, -8)
        : new IntPtr(GetWindowLong32(handle, -8));

    private static void SetWindowOwner(IntPtr handle, IntPtr owner)
    {
        if (IntPtr.Size == 8)
        {
            _ = SetWindowLongPtr64(handle, -8, owner);
        }
        else
        {
            _ = SetWindowLong32(handle, -8, owner.ToInt32());
        }
    }

    private static unsafe string GetWindowClassName(IntPtr handle)
    {
        Span<char> buffer = stackalloc char[64];
        fixed (char* pointer = buffer)
        {
            var length = GetClassName(handle, pointer, buffer.Length);
            return length > 0 ? new string(buffer[..length]) : string.Empty;
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parentHandle,
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe int GetClassName(IntPtr handle, char* className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out NativeRectangle value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr handle, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoRedraw = 0x0008;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

}

internal static class WidgetSettings
{
    private const string KeyPath = @"Software\CodexTray";

    public static int? OffsetX
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("WidgetOffsetX") is int value ? value : null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (value is null)
            {
                key.DeleteValue("WidgetOffsetX", throwOnMissingValue: false);
            }
            else
            {
                key.SetValue("WidgetOffsetX", value.Value, RegistryValueKind.DWord);
            }
        }
    }

    public static int? GetOffsetX(string screenDeviceName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(OffsetValueName(screenDeviceName)) is int value ? value : OffsetX;
    }

    public static void SetOffsetX(string screenDeviceName, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(OffsetValueName(screenDeviceName), value, RegistryValueKind.DWord);
    }

    public static bool Visible
    {
        get => ReadBoolean("WidgetVisible", defaultValue: true);
        set => WriteBoolean("WidgetVisible", value);
    }

    public static DisplayStyle SelectedStyle
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return ParseDisplayStyle(key?.GetValue("DisplayStyle"));
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue("DisplayStyle", value.ToString(), RegistryValueKind.String);
        }
    }

    internal static DisplayStyle ParseDisplayStyle(object? value)
    {
        if (value is string name &&
            Enum.TryParse<DisplayStyle>(name, ignoreCase: true, out var namedStyle) &&
            Enum.IsDefined(namedStyle))
        {
            return namedStyle;
        }

        if (value is int legacyValue && Enum.IsDefined((DisplayStyle)legacyValue))
        {
            return (DisplayStyle)legacyValue;
        }

        return DisplayStyle.Classic;
    }

    public static bool ShowLabels
    {
        get => ReadBoolean("ShowLabels", defaultValue: true);
        set => WriteBoolean("ShowLabels", value);
    }

    public static bool ShowPercentages
    {
        get => ReadBoolean("ShowPercentages", defaultValue: true);
        set => WriteBoolean("ShowPercentages", value);
    }

    public static bool ShowAgentCounter
    {
        get => ReadBoolean("ShowAgentCounter", defaultValue: true);
        set => WriteBoolean("ShowAgentCounter", value);
    }

    public static bool AnimateAgentCounter
    {
        get => ReadBoolean("AnimateAgentCounter", defaultValue: true);
        set => WriteBoolean("AnimateAgentCounter", value);
    }

    public static LoadingIconStyle SelectedLoadingIconStyle
    {
        get => ReadEnum("LoadingIconStyle", LoadingIconStyle.Arc);
        set => WriteEnum("LoadingIconStyle", value);
    }

    public static AgentIndicatorPosition SelectedAgentIndicatorPosition
    {
        get => ReadEnum("AgentIndicatorPosition", AgentIndicatorPosition.Right);
        set => WriteEnum("AgentIndicatorPosition", value);
    }

    public static AgentIndicatorSize SelectedAgentIndicatorSize
    {
        get => ReadEnum("AgentIndicatorSize", AgentIndicatorSize.Normal);
        set => WriteEnum("AgentIndicatorSize", value);
    }

    public static AgentIndicatorMode SelectedAgentIndicatorMode
    {
        get => ReadEnum("AgentIndicatorMode", AgentIndicatorMode.IconAndNumber);
        set => WriteEnum("AgentIndicatorMode", value);
    }

    public static bool SynchronizeAgentAnimations
    {
        get => ReadBoolean("SynchronizeAgentAnimations", defaultValue: false);
        set => WriteBoolean("SynchronizeAgentAnimations", value);
    }

    public static bool UseProjectColors
    {
        get => ReadBoolean("UseProjectColors", defaultValue: false);
        set => WriteBoolean("UseProjectColors", value);
    }

    public static bool ShowProjectIcons
    {
        get => ReadBoolean("ShowProjectIcons", defaultValue: false);
        set => WriteBoolean("ShowProjectIcons", value);
    }

    internal static T ParseEnum<T>(object? value, T fallback) where T : struct, Enum =>
        value is string name && Enum.TryParse<T>(name, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static T ReadEnum<T>(string name, T fallback) where T : struct, Enum
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return ParseEnum(key?.GetValue(name), fallback);
    }

    private static void WriteEnum<T>(string name, T value) where T : struct, Enum
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value.ToString(), RegistryValueKind.String);
    }

    public static LabelContentMode SelectedLabelMode
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("LabelContentMode") is int value && value == (int)LabelContentMode.TimeToReset
                ? LabelContentMode.TimeToReset
                : LabelContentMode.Static;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue("LabelContentMode", (int)value, RegistryValueKind.DWord);
        }
    }

    public static LabelPosition SelectedLabelPosition
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("LabelPosition") is int value && value == (int)LabelPosition.Right
                ? LabelPosition.Right
                : LabelPosition.Left;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue("LabelPosition", (int)value, RegistryValueKind.DWord);
        }
    }

    public static PercentagePosition SelectedPercentagePosition
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("PercentagePosition") is int value && Enum.IsDefined((PercentagePosition)value)
                ? (PercentagePosition)value
                : PercentagePosition.Center;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue("PercentagePosition", (int)value, RegistryValueKind.DWord);
        }
    }

    public static TaskbarPlacement SelectedPlacement
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("TaskbarPlacement") is int value && Enum.IsDefined((TaskbarPlacement)value)
                ? (TaskbarPlacement)value
                : TaskbarPlacement.Custom;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue("TaskbarPlacement", (int)value, RegistryValueKind.DWord);
        }
    }

    private static bool ReadBoolean(string name, bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) is int value ? value != 0 : defaultValue;
    }

    private static void WriteBoolean(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    private static string OffsetValueName(string screenDeviceName)
    {
        var safeName = new string(screenDeviceName.Where(char.IsLetterOrDigit).ToArray());
        return $"WidgetOffsetX_{safeName}";
    }
}
