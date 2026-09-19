using Microsoft.Win32;

namespace CodexTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int RefreshIntervalMinutes = 2;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _widgetItem;
    private readonly ToolStripMenuItem _classicStyleItem;
    private readonly ToolStripMenuItem _futuristicStyleItem;
    private readonly ToolStripMenuItem _fluentStyleItem;
    private readonly ToolStripMenuItem _minimalStyleItem;
    private readonly ToolStripMenuItem _highContrastStyleItem;
    private readonly ToolStripMenuItem _neonStyleItem;
    private readonly ToolStripMenuItem _glassStyleItem;
    private readonly ToolStripMenuItem _pixelStyleItem;
    private readonly ToolStripMenuItem _carbonStyleItem;
    private readonly ToolStripMenuItem _dotsStyleItem;
    private readonly ToolStripMenuItem _pencilStyleItem;
    private readonly ToolStripMenuItem _labelsItem;
    private readonly ToolStripMenuItem _percentagesItem;
    private readonly ToolStripMenuItem _staticLabelsItem;
    private readonly ToolStripMenuItem _resetCountdownLabelsItem;
    private readonly ToolStripMenuItem _leftLabelPositionItem;
    private readonly ToolStripMenuItem _rightLabelPositionItem;
    private readonly ToolStripMenuItem _leftPercentagePositionItem;
    private readonly ToolStripMenuItem _centerPercentagePositionItem;
    private readonly ToolStripMenuItem _rightPercentagePositionItem;
    private readonly ToolStripMenuItem _leftPlacementItem;
    private readonly ToolStripMenuItem _rightPlacementItem;
    private readonly ToolStripMenuItem _customPlacementItem;
    private readonly List<TaskbarWidgetForm> _widgets = [];
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ContextMenuStrip _menu;
    private readonly SynchronizationContext _uiContext;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ForegroundWindowMonitor _foregroundWindowMonitor;
    private UsageSnapshot? _snapshot;
    private string? _lastError;
    private Icon? _currentIcon;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _foregroundWindowMonitor = new ForegroundWindowMonitor(
            () => _uiContext.Post(_ => UpdateWidgetVisibility(), null));
        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupManager.IsEnabled,
            CheckOnClick = true
        };
        _startupItem.Click += ToggleStartup;

        _widgetItem = new ToolStripMenuItem("Show this widget in taskbar")
        {
            Checked = WidgetSettings.Visible,
            CheckOnClick = true
        };
        _widgetItem.Click += ToggleWidget;

        _classicStyleItem = new ToolStripMenuItem("Classic")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Classic
        };
        _futuristicStyleItem = new ToolStripMenuItem("Futuristic")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Futuristic
        };
        _fluentStyleItem = new ToolStripMenuItem("Fluent")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Fluent
        };
        _minimalStyleItem = new ToolStripMenuItem("Minimal")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Minimal
        };
        _highContrastStyleItem = new ToolStripMenuItem("High contrast")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.HighContrast
        };
        _neonStyleItem = new ToolStripMenuItem("Neon")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Neon
        };
        _glassStyleItem = new ToolStripMenuItem("Glass")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Glass
        };
        _pixelStyleItem = new ToolStripMenuItem("Pixel")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Pixel
        };
        _carbonStyleItem = new ToolStripMenuItem("Carbon")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Carbon
        };
        _dotsStyleItem = new ToolStripMenuItem("Dots")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Dots
        };
        _pencilStyleItem = new ToolStripMenuItem("Pencil")
        {
            Checked = WidgetSettings.SelectedStyle == DisplayStyle.Pencil
        };
        _classicStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Classic);
        _futuristicStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Futuristic);
        _fluentStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Fluent);
        _minimalStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Minimal);
        _highContrastStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.HighContrast);
        _neonStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Neon);
        _glassStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Glass);
        _pixelStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Pixel);
        _carbonStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Carbon);
        _dotsStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Dots);
        _pencilStyleItem.Click += (_, _) => SetDisplayStyle(DisplayStyle.Pencil);

        _labelsItem = new ToolStripMenuItem("Show labels")
        {
            Checked = WidgetSettings.ShowLabels,
            CheckOnClick = true
        };
        _labelsItem.Click += ToggleLabels;

        _staticLabelsItem = new ToolStripMenuItem("Static (5h / 7d)")
        {
            Checked = WidgetSettings.SelectedLabelMode == LabelContentMode.Static
        };
        _resetCountdownLabelsItem = new ToolStripMenuItem("Time to reset")
        {
            Checked = WidgetSettings.SelectedLabelMode == LabelContentMode.TimeToReset
        };
        _staticLabelsItem.Click += (_, _) => SetLabelMode(LabelContentMode.Static);
        _resetCountdownLabelsItem.Click += (_, _) => SetLabelMode(LabelContentMode.TimeToReset);

        var labelContentMenu = new ToolStripMenuItem("Label content");
        labelContentMenu.DropDownItems.Add(_staticLabelsItem);
        labelContentMenu.DropDownItems.Add(_resetCountdownLabelsItem);

        _leftLabelPositionItem = new ToolStripMenuItem("Left")
        {
            Checked = WidgetSettings.SelectedLabelPosition == LabelPosition.Left
        };
        _rightLabelPositionItem = new ToolStripMenuItem("Right")
        {
            Checked = WidgetSettings.SelectedLabelPosition == LabelPosition.Right
        };
        _leftLabelPositionItem.Click += (_, _) => SetLabelPosition(LabelPosition.Left);
        _rightLabelPositionItem.Click += (_, _) => SetLabelPosition(LabelPosition.Right);

        var labelPositionMenu = new ToolStripMenuItem("Label position");
        labelPositionMenu.DropDownItems.Add(_leftLabelPositionItem);
        labelPositionMenu.DropDownItems.Add(_rightLabelPositionItem);

        _percentagesItem = new ToolStripMenuItem("Show percentages")
        {
            Checked = WidgetSettings.ShowPercentages,
            CheckOnClick = true
        };
        _percentagesItem.Click += TogglePercentages;

        _leftPercentagePositionItem = new ToolStripMenuItem("Left of batteries")
        {
            Checked = WidgetSettings.SelectedPercentagePosition == PercentagePosition.Left
        };
        _centerPercentagePositionItem = new ToolStripMenuItem("Centered in batteries")
        {
            Checked = WidgetSettings.SelectedPercentagePosition == PercentagePosition.Center
        };
        _rightPercentagePositionItem = new ToolStripMenuItem("Right of batteries")
        {
            Checked = WidgetSettings.SelectedPercentagePosition == PercentagePosition.Right
        };
        _leftPercentagePositionItem.Click += (_, _) => SetPercentagePosition(PercentagePosition.Left);
        _centerPercentagePositionItem.Click += (_, _) => SetPercentagePosition(PercentagePosition.Center);
        _rightPercentagePositionItem.Click += (_, _) => SetPercentagePosition(PercentagePosition.Right);

        var percentagePositionMenu = new ToolStripMenuItem("Percentage position");
        percentagePositionMenu.DropDownItems.Add(_leftPercentagePositionItem);
        percentagePositionMenu.DropDownItems.Add(_centerPercentagePositionItem);
        percentagePositionMenu.DropDownItems.Add(_rightPercentagePositionItem);

        _leftPlacementItem = new ToolStripMenuItem("Left side")
        {
            Checked = WidgetSettings.SelectedPlacement == TaskbarPlacement.Left
        };
        _rightPlacementItem = new ToolStripMenuItem("Right side")
        {
            Checked = WidgetSettings.SelectedPlacement == TaskbarPlacement.Right
        };
        _customPlacementItem = new ToolStripMenuItem("Custom (dragged)")
        {
            Checked = WidgetSettings.SelectedPlacement == TaskbarPlacement.Custom,
            Enabled = false
        };
        _leftPlacementItem.Click += (_, _) => SetTaskbarPlacement(TaskbarPlacement.Left);
        _rightPlacementItem.Click += (_, _) => SetTaskbarPlacement(TaskbarPlacement.Right);

        var positionMenu = new ToolStripMenuItem("Position");
        positionMenu.DropDownItems.Add(_leftPlacementItem);
        positionMenu.DropDownItems.Add(_rightPlacementItem);
        positionMenu.DropDownItems.Add(_customPlacementItem);

        var styleMenu = new ToolStripMenuItem("Style");
        styleMenu.DropDownItems.Add(_classicStyleItem);
        styleMenu.DropDownItems.Add(_futuristicStyleItem);
        styleMenu.DropDownItems.Add(_fluentStyleItem);
        styleMenu.DropDownItems.Add(_minimalStyleItem);
        styleMenu.DropDownItems.Add(_highContrastStyleItem);
        styleMenu.DropDownItems.Add(new ToolStripSeparator());
        styleMenu.DropDownItems.Add(_neonStyleItem);
        styleMenu.DropDownItems.Add(_glassStyleItem);
        styleMenu.DropDownItems.Add(_pixelStyleItem);
        styleMenu.DropDownItems.Add(_carbonStyleItem);
        styleMenu.DropDownItems.Add(_dotsStyleItem);
        styleMenu.DropDownItems.Add(_pencilStyleItem);

        var displayMenu = new ToolStripMenuItem("Display");
        displayMenu.DropDownItems.Add(styleMenu);
        displayMenu.DropDownItems.Add(positionMenu);
        displayMenu.DropDownItems.Add(_labelsItem);
        displayMenu.DropDownItems.Add(labelContentMenu);
        displayMenu.DropDownItems.Add(labelPositionMenu);
        displayMenu.DropDownItems.Add(_percentagesItem);
        displayMenu.DropDownItems.Add(percentagePositionMenu);
        displayMenu.DropDownItems.Add(new ToolStripSeparator());
        displayMenu.DropDownItems.Add(new ToolStripMenuItem($"Refresh interval: {RefreshIntervalMinutes} minutes")
        {
            Enabled = false
        });

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Show details", null, ShowDetails);
        _menu.Items.Add("Refresh", null, async (_, _) => await RefreshAsync(showErrors: true));
        _menu.Items.Add(displayMenu);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_widgetItem);
        _menu.Items.Add("Exit", null, Exit);

        _currentIcon = TrayIconRenderer.Render(null);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "Codex usage: loading...",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ShowDetails(this, EventArgs.Empty);
            }
        };

        RebuildWidgets(_menu);

        _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMinutes * 60 * 1000 };
        _timer.Tick += async (_, _) => await RefreshAsync(showErrors: false);
        _timer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _ = RefreshAsync(showErrors: false);
    }

    private async Task RefreshAsync(bool showErrors)
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var newSnapshot = await CodexRateLimitClient.ReadAsync(_shutdown.Token);
            var visualChanged = _lastError is not null || !Equals(_snapshot, newSnapshot);
            _snapshot = newSnapshot;
            _lastError = null;
            if (visualChanged)
            {
                SetIcon(TrayIconRenderer.Render(_snapshot));
            }
            foreach (var widget in _widgets)
            {
                widget.SetSnapshot(_snapshot);
            }
            _notifyIcon.Text = TruncateTooltip(_snapshot.ToTooltip());
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            var visualChanged = !string.Equals(_lastError, exception.Message, StringComparison.Ordinal);
            _lastError = exception.Message;
            if (visualChanged)
            {
                SetIcon(TrayIconRenderer.Render(_snapshot, isError: true));
            }
            foreach (var widget in _widgets)
            {
                widget.SetError(exception.Message);
            }
            _notifyIcon.Text = TruncateTooltip($"Codex usage error: {exception.Message}");
            if (showErrors)
            {
                ShowDetails(this, EventArgs.Empty);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void ShowDetails(object? sender, EventArgs eventArgs)
    {
        var text = _lastError is not null
            ? $"Could not read Codex usage.{Environment.NewLine}{Environment.NewLine}{_lastError}"
            : _snapshot?.ToDisplayText() ?? "Codex usage is still loading.";

        _notifyIcon.BalloonTipTitle = "Codex usage";
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = _lastError is null ? ToolTipIcon.Info : ToolTipIcon.Error;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void SetDisplayStyle(DisplayStyle style)
    {
        WidgetSettings.SelectedStyle = style;
        _classicStyleItem.Checked = style == DisplayStyle.Classic;
        _futuristicStyleItem.Checked = style == DisplayStyle.Futuristic;
        _fluentStyleItem.Checked = style == DisplayStyle.Fluent;
        _minimalStyleItem.Checked = style == DisplayStyle.Minimal;
        _highContrastStyleItem.Checked = style == DisplayStyle.HighContrast;
        _neonStyleItem.Checked = style == DisplayStyle.Neon;
        _glassStyleItem.Checked = style == DisplayStyle.Glass;
        _pixelStyleItem.Checked = style == DisplayStyle.Pixel;
        _carbonStyleItem.Checked = style == DisplayStyle.Carbon;
        _dotsStyleItem.Checked = style == DisplayStyle.Dots;
        _pencilStyleItem.Checked = style == DisplayStyle.Pencil;
        ApplyDisplaySettings();
    }

    private void ToggleLabels(object? sender, EventArgs eventArgs)
    {
        WidgetSettings.ShowLabels = _labelsItem.Checked;
        ApplyDisplaySettings();
    }

    private void SetLabelMode(LabelContentMode mode)
    {
        WidgetSettings.SelectedLabelMode = mode;
        _staticLabelsItem.Checked = mode == LabelContentMode.Static;
        _resetCountdownLabelsItem.Checked = mode == LabelContentMode.TimeToReset;
        ApplyDisplaySettings();
    }

    private void TogglePercentages(object? sender, EventArgs eventArgs)
    {
        WidgetSettings.ShowPercentages = _percentagesItem.Checked;
        ApplyDisplaySettings();
    }

    private void SetLabelPosition(LabelPosition position)
    {
        WidgetSettings.SelectedLabelPosition = position;
        _leftLabelPositionItem.Checked = position == LabelPosition.Left;
        _rightLabelPositionItem.Checked = position == LabelPosition.Right;
        ApplyDisplaySettings();
    }

    private void SetPercentagePosition(PercentagePosition position)
    {
        WidgetSettings.SelectedPercentagePosition = position;
        _leftPercentagePositionItem.Checked = position == PercentagePosition.Left;
        _centerPercentagePositionItem.Checked = position == PercentagePosition.Center;
        _rightPercentagePositionItem.Checked = position == PercentagePosition.Right;
        ApplyDisplaySettings();
    }

    private void SetTaskbarPlacement(TaskbarPlacement placement)
    {
        WidgetSettings.SelectedPlacement = placement;
        UpdatePlacementChecks();
        foreach (var widget in _widgets)
        {
            widget.PlaceBesideWeather();
        }
    }

    private void UpdatePlacementChecks()
    {
        var placement = WidgetSettings.SelectedPlacement;
        _leftPlacementItem.Checked = placement == TaskbarPlacement.Left;
        _rightPlacementItem.Checked = placement == TaskbarPlacement.Right;
        _customPlacementItem.Checked = placement == TaskbarPlacement.Custom;
    }

    private void ApplyDisplaySettings()
    {
        foreach (var widget in _widgets)
        {
            widget.ApplyDisplaySettings(
                WidgetSettings.SelectedStyle,
                WidgetSettings.ShowLabels,
                WidgetSettings.ShowPercentages,
                WidgetSettings.SelectedLabelMode,
                WidgetSettings.SelectedLabelPosition,
                WidgetSettings.SelectedPercentagePosition);
        }
    }

    private void ToggleStartup(object? sender, EventArgs eventArgs)
    {
        try
        {
            StartupManager.SetEnabled(_startupItem.Checked);
        }
        catch (Exception exception)
        {
            _startupItem.Checked = StartupManager.IsEnabled;
            MessageBox.Show(exception.Message, "CodexTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleWidget(object? sender, EventArgs eventArgs)
    {
        WidgetSettings.Visible = _widgetItem.Checked;
        UpdateWidgetVisibility();
    }

    private void UpdateWidgetVisibility()
    {
        foreach (var widget in _widgets)
        {
            var shouldShow = _widgetItem.Checked &&
                             !ForegroundWindowMonitor.IsFullscreenOn(widget.ScreenBounds);
            if (!shouldShow)
            {
                widget.Hide();
                continue;
            }

            if (!widget.Visible)
            {
                widget.PlaceBesideWeather();
                widget.Show();
            }

            widget.RestoreTaskbarZOrder();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs)
    {
        _uiContext.Post(_ => RebuildWidgets(_menu), null);
    }

    private void RebuildWidgets(ContextMenuStrip menu)
    {
        foreach (var widget in _widgets)
        {
            widget.Hide();
            widget.Dispose();
        }

        _widgets.Clear();
        foreach (var screen in Screen.AllScreens)
        {
            var widget = new TaskbarWidgetForm(menu, screen);
            widget.DetailsRequested += ShowDetails;
            widget.PlacementCustomized += (_, _) => UpdatePlacementChecks();
            widget.ApplyDisplaySettings(
                WidgetSettings.SelectedStyle,
                WidgetSettings.ShowLabels,
                WidgetSettings.ShowPercentages,
                WidgetSettings.SelectedLabelMode,
                WidgetSettings.SelectedLabelPosition,
                WidgetSettings.SelectedPercentagePosition);
            if (_snapshot is not null)
            {
                widget.SetSnapshot(_snapshot);
            }
            else if (_lastError is not null)
            {
                widget.SetError(_lastError);
            }

            _widgets.Add(widget);
        }

        UpdateWidgetVisibility();
    }

    private void Exit(object? sender, EventArgs eventArgs)
    {
        _timer.Stop();
        _shutdown.Cancel();
        _foregroundWindowMonitor.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var widget in _widgets)
        {
            widget.Hide();
            widget.Dispose();
        }

        _widgets.Clear();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        ExitThread();
    }

    private void SetIcon(Icon icon)
    {
        var previous = _currentIcon;
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
        previous?.Dispose();
    }

    private static string TruncateTooltip(string value) => value.Length <= 63 ? value : value[..63];
}
