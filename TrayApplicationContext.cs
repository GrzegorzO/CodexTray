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
    private readonly System.Windows.Forms.Timer _visibilityTimer;
    private readonly System.Windows.Forms.Timer _activityTimer;
    private readonly AgentActivityMonitor _activityMonitor = new();
    private readonly ToolStripMenuItem _agentCounterItem;
    private readonly ToolStripMenuItem _animateAgentsItem;
    private readonly ToolStripMenuItem _synchronizeAgentsItem;
    private readonly Dictionary<LoadingIconStyle, ToolStripMenuItem> _agentStyleItems = [];
    private readonly Dictionary<AgentIndicatorPosition, ToolStripMenuItem> _agentPositionItems = [];
    private readonly Dictionary<AgentIndicatorSize, ToolStripMenuItem> _agentSizeItems = [];
    private readonly Dictionary<AgentIndicatorMode, ToolStripMenuItem> _agentModeItems = [];
    private readonly List<Bitmap> _agentMenuImages = [];
    private int _workingAgentCount;
    private bool _readingActivity;
    private int _visibilityUpdatePending;
    private string? _activityError;
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
        _foregroundWindowMonitor = new ForegroundWindowMonitor(QueueWidgetVisibilityUpdate);
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
        var agentMenu = new ToolStripMenuItem("Working agents");
        _agentCounterItem = new ToolStripMenuItem("Show counter when working")
        {
            Checked = WidgetSettings.ShowAgentCounter,
            CheckOnClick = true
        };
        _agentCounterItem.Click += (_, _) =>
        {
            WidgetSettings.ShowAgentCounter = _agentCounterItem.Checked;
            ApplyAgentSettings();
            _ = RefreshActivityAsync();
        };
        _animateAgentsItem = new ToolStripMenuItem("Animate loading icon")
        {
            Checked = WidgetSettings.AnimateAgentCounter,
            CheckOnClick = true
        };
        _animateAgentsItem.Click += (_, _) =>
        {
            WidgetSettings.AnimateAgentCounter = _animateAgentsItem.Checked;
            ApplyAgentSettings();
        };
        agentMenu.DropDownItems.Add(_agentCounterItem);
        agentMenu.DropDownItems.Add(_animateAgentsItem);
        _synchronizeAgentsItem = new ToolStripMenuItem("Synchronize icon animations")
        {
            Checked = WidgetSettings.SynchronizeAgentAnimations,
            CheckOnClick = true,
            ToolTipText = "Animate repeated icons together; turn off for staggered animations."
        };
        _synchronizeAgentsItem.Click += (_, _) =>
        {
            WidgetSettings.SynchronizeAgentAnimations = _synchronizeAgentsItem.Checked;
            ApplyAgentSettings();
        };
        agentMenu.DropDownItems.Add(_synchronizeAgentsItem);
        var loadingStyleMenu = new ToolStripMenuItem("Loading icon style");
        foreach (var style in Enum.GetValues<LoadingIconStyle>())
        {
            var image = new Bitmap(24, 24);
            using (var graphics = Graphics.FromImage(image))
            {
                AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 48, 24), 1, style, 0.25f);
            }
            _agentMenuImages.Add(image);
            var item = new ToolStripMenuItem(AgentIndicatorRenderer.Title(style), image)
            {
                Checked = WidgetSettings.SelectedLoadingIconStyle == style
            };
            item.Click += (_, _) =>
            {
                WidgetSettings.SelectedLoadingIconStyle = style;
                foreach (var pair in _agentStyleItems)
                {
                    pair.Value.Checked = pair.Key == style;
                }
                ApplyAgentSettings();
            };
            _agentStyleItems.Add(style, item);
            loadingStyleMenu.DropDownItems.Add(item);
        }
        agentMenu.DropDownItems.Add(loadingStyleMenu);
        var agentSizeMenu = new ToolStripMenuItem("Indicator size");
        foreach (var size in Enum.GetValues<AgentIndicatorSize>())
        {
            var title = size switch
            {
                AgentIndicatorSize.Small => "Small (70%)",
                AgentIndicatorSize.Large => "Large (130%)",
                _ => "Normal (100%, default)"
            };
            var item = new ToolStripMenuItem(title) { Checked = WidgetSettings.SelectedAgentIndicatorSize == size };
            item.Click += (_, _) =>
            {
                WidgetSettings.SelectedAgentIndicatorSize = size;
                foreach (var pair in _agentSizeItems)
                {
                    pair.Value.Checked = pair.Key == size;
                }
                ApplyAgentSettings();
            };
            _agentSizeItems.Add(size, item);
            agentSizeMenu.DropDownItems.Add(item);
        }
        agentMenu.DropDownItems.Add(agentSizeMenu);
        var agentModeMenu = new ToolStripMenuItem("Display mode");
        foreach (var mode in Enum.GetValues<AgentIndicatorMode>())
        {
            var item = new ToolStripMenuItem(mode == AgentIndicatorMode.IconAndNumber
                ? "Icon and number (default)" : "One icon per agent")
            {
                Checked = WidgetSettings.SelectedAgentIndicatorMode == mode
            };
            item.Click += (_, _) =>
            {
                WidgetSettings.SelectedAgentIndicatorMode = mode;
                foreach (var pair in _agentModeItems)
                {
                    pair.Value.Checked = pair.Key == mode;
                }
                ApplyAgentSettings();
            };
            _agentModeItems.Add(mode, item);
            agentModeMenu.DropDownItems.Add(item);
        }
        agentMenu.DropDownItems.Add(agentModeMenu);
        var agentPositionMenu = new ToolStripMenuItem("Counter position");
        foreach (var position in Enum.GetValues<AgentIndicatorPosition>())
        {
            var item = new ToolStripMenuItem(position == AgentIndicatorPosition.Right ? "Right of all controls (default)" : "Left of all controls")
            {
                Checked = WidgetSettings.SelectedAgentIndicatorPosition == position
            };
            item.Click += (_, _) =>
            {
                WidgetSettings.SelectedAgentIndicatorPosition = position;
                foreach (var pair in _agentPositionItems)
                {
                    pair.Value.Checked = pair.Key == position;
                }
                ApplyAgentSettings();
            };
            _agentPositionItems.Add(position, item);
            agentPositionMenu.DropDownItems.Add(item);
        }
        agentMenu.DropDownItems.Add(agentPositionMenu);
        displayMenu.DropDownItems.Add(agentMenu);
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

        // Taskbar auto-hide and fullscreen z-order changes do not reliably raise a
        // foreground or top-level location event. Polling this inexpensive Win32
        // state keeps each widget's visibility connected to its taskbar.
        _visibilityTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _visibilityTimer.Tick += (_, _) => UpdateWidgetVisibility(restoreZOrder: false);
        _visibilityTimer.Start();

        _activityTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _activityTimer.Tick += async (_, _) => await RefreshActivityAsync();
        _activityTimer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _ = RefreshAsync(showErrors: false);
        _ = RefreshActivityAsync();
    }

    private async Task RefreshActivityAsync()
    {
        if (_readingActivity || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _readingActivity = true;
        try
        {
            var count = await Task.Run(() => _activityMonitor.ReadCount(_shutdown.Token), _shutdown.Token);
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            _activityError = null;
            _workingAgentCount = count;
            foreach (var widget in _widgets)
            {
                widget.SetWorkingAgentCount(count);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // The background read is cancelled before widgets are disposed.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _activityError = exception.Message;
            _workingAgentCount = 0;
            foreach (var widget in _widgets)
            {
                widget.SetWorkingAgentCount(0);
            }
        }
        finally
        {
            _readingActivity = false;
        }
    }

    private void ApplyAgentSettings()
    {
        foreach (var widget in _widgets)
        {
            widget.ApplyAgentSettings(WidgetSettings.ShowAgentCounter, WidgetSettings.AnimateAgentCounter,
                WidgetSettings.SelectedLoadingIconStyle, WidgetSettings.SelectedAgentIndicatorPosition,
                WidgetSettings.SelectedAgentIndicatorSize, WidgetSettings.SelectedAgentIndicatorMode,
                WidgetSettings.SynchronizeAgentAnimations);
        }
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

        text += _activityError is null
            ? $"{Environment.NewLine}{Environment.NewLine}Working local agents: {_workingAgentCount}"
            : $"{Environment.NewLine}{Environment.NewLine}Agent activity unavailable: {_activityError}";

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

    private void QueueWidgetVisibilityUpdate()
    {
        if (Interlocked.Exchange(ref _visibilityUpdatePending, 1) != 0)
        {
            return;
        }
        _uiContext.Post(_ =>
        {
            Interlocked.Exchange(ref _visibilityUpdatePending, 0);
            if (!_shutdown.IsCancellationRequested)
            {
                UpdateWidgetVisibility();
            }
        }, null);
    }

    private void UpdateWidgetVisibility(bool restoreZOrder = true)
    {
        foreach (var widget in _widgets)
        {
            var shouldShow = _widgetItem.Checked && widget.IsTaskbarVisible();
            if (!shouldShow)
            {
                widget.Hide();
                continue;
            }

            if (!widget.Visible)
            {
                widget.PlaceBesideWeather();
                widget.Show();
                widget.RestoreTaskbarZOrder();
            }

            if (restoreZOrder)
            {
                widget.RestoreTaskbarZOrder();
            }
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
            widget.SetWorkingAgentCount(_workingAgentCount);
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
        _visibilityTimer.Stop();
        _activityTimer.Stop();
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
        _activityTimer.Dispose();
        foreach (var image in _agentMenuImages)
        {
            image.Dispose();
        }
        _menu.Dispose();
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
