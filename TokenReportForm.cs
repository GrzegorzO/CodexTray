using Microsoft.Win32;

namespace CodexTray;

internal sealed class TokenReportForm : Form
{
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 205, AccessibleName = "Report period" };
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 125, AccessibleName = "Start date" };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 125, AccessibleName = "End date, inclusive" };
    private readonly FlowLayoutPanel _dates = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    private readonly Button _refresh = new() { Text = "↻  Refresh report", Width = 152, Height = 32, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(2, 0, 0, 0) };
    private readonly ReportProgressBar _progress = new()
    {
        Dock = DockStyle.Bottom,
        Height = 10,
        AccessibleName = "Usage history reading progress",
        AccessibleRole = AccessibleRole.ProgressBar
    };
    private readonly Label _loadingTitle = new() { Dock = DockStyle.Top, Height = 24, Text = "Loading usage report…" };
    private readonly Label _loadingDetail = new() { Dock = DockStyle.Top, Height = 24, AutoEllipsis = true, Tag = "muted" };
    private readonly Button _cancel = new() { Text = "Cancel", Width = 86, Height = 32, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Right };
    private readonly System.Windows.Forms.Timer _loadingTimer = new() { Interval = 50 };
    private readonly System.Diagnostics.Stopwatch _loadingWatch = new();
    private readonly ReportCard _loadingCard;
    private readonly TableLayoutPanel _layout;
    private ReportReadProgress? _readProgress;
    private string _loadingPhase = "Checking current usage window…";
    private readonly ProjectTokenChart _chart = new();
    private readonly TokenDetailChart _details = new();
    private readonly ReportSummary _summary = new() { Dock = DockStyle.Fill };
    private readonly ReportHeading _heading = new() { Dock = DockStyle.Fill };
    private readonly Panel _chartViewport = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel _detailViewport = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly List<ReportCard> _cards = [];
    private readonly List<Font> _fonts = [];
    private readonly ToolTip _tooltip = new();
    private readonly LinkLabel _about = new() { Text = "About this data", AutoSize = true, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    private readonly TokenUsageReportReader _reader = new();
    private readonly SemaphoreSlim _serial = new(1);
    private readonly Func<UsageSnapshot?> _snapshot;
    private readonly Func<CancellationToken, Task<UsageSnapshot>> _readQuota;
    private readonly ReportTheme? _forcedTheme;
    private readonly bool _preview;
    private CancellationTokenSource? _request;
    private TokenReport? _report;
    private ReportTheme _theme = ReportTheme.Light;
    private string? _selected;
    private string _aboutText = "Reports cover available local token records. Remote and deleted usage is not included.";
    private bool _ready;
    private bool _busy;
    private bool _statusWarning;
    private bool _adjustingDates;

    internal TokenReportForm(Func<UsageSnapshot?> snapshot, bool preview = false, ReportTheme? forcedTheme = null,
        Func<CancellationToken, Task<UsageSnapshot>>? readQuota = null)
    {
        _snapshot = snapshot;
        _readQuota = readQuota ?? CodexRateLimitClient.ReadAsync;
        _preview = preview;
        _forcedTheme = forcedTheme;
        Text = "CodexTray · Usage overview";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(1220, 930);
        MinimumSize = new Size(1080, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = UiFont(10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 12), ColumnCount = 1, RowCount = 6 };
        _layout = root;
        foreach (var height in new[] { 82, 76, 110, 40 }) { root.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.Controls.Add(_heading, 0, 0);
        var toolbar = Card(new Padding(16, 14, 16, 14));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        root.Layout += (_, _) =>
        {
            var availableWidth = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal - toolbar.Padding.Horizontal - toolbar.Margin.Horizontal);
            var rowWidth = 0;
            var rowHeight = 0;
            var contentHeight = 0;
            foreach (Control filter in controls.Controls)
            {
                if (filter == _dates && _preset.SelectedIndex != (int)ReportPreset.Custom) { continue; }
                var size = filter.AutoSize ? filter.GetPreferredSize(Size.Empty) : filter.Size;
                var width = size.Width + filter.Margin.Horizontal;
                if (rowWidth > 0 && rowWidth + width > availableWidth)
                {
                    contentHeight += rowHeight;
                    rowWidth = rowHeight = 0;
                }
                rowWidth += width;
                rowHeight = Math.Max(rowHeight, size.Height + filter.Margin.Vertical);
            }
            var height = Math.Max(LogicalToDeviceUnits(76), contentHeight + rowHeight + LogicalToDeviceUnits(40));
            if (Math.Abs(root.RowStyles[1].Height - height) > .5f) { root.RowStyles[1].Height = height; }
        };
        _preset.Items.AddRange(["Current 5h window", "Previous 5h window", "Current 7d window", "Previous 7d window", "Custom dates"]);
        _from.Value = DateTime.Today.AddDays(-6);
        _to.Value = DateTime.Today;
        _preset.SelectedIndex = 0;
        _from.Enabled = _to.Enabled = false;
        _dates.Visible = false;
        _preset.Margin = new Padding(0, 2, 16, 0);
        _refresh.Margin = new Padding(8, 0, 0, 0);
        _refresh.FlatAppearance.BorderSize = 0;
        _dates.Controls.AddRange([SmallLabel("From"), _from, SmallLabel("Through"), _to]);
        controls.Controls.AddRange([SmallLabel("Period"), _preset, _dates, _refresh]);
        toolbar.Controls.Add(controls);
        root.Controls.Add(toolbar, 0, 1);
        var summary = Card(new Padding(18, 8, 18, 8));
        summary.Controls.Add(_summary);
        root.Controls.Add(summary, 0, 2);
        var statusHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _loadingCard = Card(new Padding(16, 10, 16, 10));
        _loadingCard.Visible = false;
        var loadingLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        loadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        loadingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        loadingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var loadingText = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _loadingTitle.Font = UiFont(11, FontStyle.Bold);
        loadingText.Controls.Add(_progress);
        loadingText.Controls.Add(_loadingDetail);
        loadingText.Controls.Add(_loadingTitle);
        loadingLayout.Controls.Add(loadingText, 0, 0);
        loadingLayout.Controls.Add(_cancel, 1, 0);
        _loadingCard.Controls.Add(loadingLayout);
        statusHost.Controls.Add(_status);
        statusHost.Controls.Add(_loadingCard);
        root.Controls.Add(statusHost, 0, 3);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        var chartCard = Card(new Padding(18, 14, 18, 14));
        chartCard.Margin = new Padding(0, 0, 16, 0);
        var chartHeading = new Label { Text = "Tokens by project", Dock = DockStyle.Top, Height = 28, Font = UiFont(12, FontStyle.Bold) };
        var chartHint = new Label { Text = "Input + output · largest first · click to explore", Dock = DockStyle.Top, Height = 28, Tag = "muted", Font = UiFont(9) };
        _chartViewport.Controls.Add(_chart);
        chartCard.Controls.Add(_chartViewport);
        chartCard.Controls.Add(chartHint);
        chartCard.Controls.Add(chartHeading);
        body.Controls.Add(chartCard, 0, 0);
        var detailCard = Card(new Padding(14));
        detailCard.Margin = Padding.Empty;
        _detailViewport.Controls.Add(_details);
        detailCard.Controls.Add(_detailViewport);
        body.Controls.Add(detailCard, 1, 0);
        root.Controls.Add(body, 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label
        {
            Text = "Local recorded usage · input + output = total tokens",
            Tag = "muted",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiFont(9),
            AutoEllipsis = true
        }, 0, 0);
        footer.Controls.Add(_about, 1, 0);
        root.Controls.Add(footer, 0, 5);
        Controls.Add(root);
        _preset.SelectedIndexChanged += (_, _) =>
        {
            _dates.Visible = _from.Enabled = _to.Enabled = _preset.SelectedIndex == (int)ReportPreset.Custom;
            _selected = null;
            _ = GenerateAsync();
        };
        _from.ValueChanged += (_, _) => CustomDateChanged(startEdited: true);
        _to.ValueChanged += (_, _) => CustomDateChanged(startEdited: false);
        _tooltip.SetToolTip(_from, "Moving the start past the end also moves the end to the selected day.");
        _tooltip.SetToolTip(_to, "Moving the end before the start also moves the start to the selected day.");
        _refresh.Click += (_, _) => _ = GenerateAsync();
        _loadingTimer.Tick += (_, _) => UpdateLoadingText();
        _cancel.Click += (_, _) =>
        {
            _request?.Cancel();
            SetBusy(false);
            _statusWarning = false;
            _status.ForeColor = _theme.Muted;
            _status.Text = _report is null ? "Loading cancelled. Choose Refresh report to try again." : "Loading cancelled. The last completed report is still shown.";
        };
        _chart.ProjectSelected += id =>
        {
            _selected = id;
            if (_report is not null) { PresentSelection(_report); }
            _ = GenerateAsync();
        };
        _about.LinkClicked += (_, _) => MessageBox.Show(this, _aboutText +
            (_report?.Projects.FirstOrDefault(p => p.Id == _selected) is { } selected ?
                $"\n\nSelected project: {selected.Name}\n{selected.Id}\nInput (all): {selected.Counts.Input:N0}\nCached input: {selected.Counts.CachedInput:N0}\nCache writes: {selected.Counts.CacheWrite:N0}\nOutput (all): {selected.Counts.Output:N0}\nReasoning: {selected.Counts.Reasoning:N0}" : ""),
            "About this report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _chartViewport.Resize += (_, _) => SizeCharts();
        _detailViewport.Resize += (_, _) => SizeCharts();
        _chart.SizeRequested += SizeCharts;
        if (!preview)
        {
            Shown += (_, _) => { _ready = true; _ = GenerateAsync(); };
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        ApplyTheme();
    }

    private Font UiFont(float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font("Segoe UI", size, style);
        _fonts.Add(font);
        return font;
    }

    private void CustomDateChanged(bool startEdited)
    {
        if (_adjustingDates || _preset.SelectedIndex != (int)ReportPreset.Custom) { return; }
        _adjustingDates = true;
        try
        {
            // The endpoint the user edits wins; normalize the pair before starting
            // one refresh so no transient inverted range can clear the report.
            if (_from.Value.Date > _to.Value.Date)
            {
                if (startEdited) { _to.Value = _from.Value.Date; }
                else { _from.Value = _to.Value.Date; }
            }
        }
        finally { _adjustingDates = false; }
        _ = GenerateAsync();
    }
    private static Label SmallLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 8, 0), Tag = "muted" };
    private ReportCard Card(Padding padding)
    {
        var card = new ReportCard { Dock = DockStyle.Fill, Padding = padding, Margin = new Padding(0, 0, 0, 12) };
        _cards.Add(card);
        return card;
    }
    private void SizeCharts()
    {
        if (IsDisposed) { return; }
        _chart.Size = new Size(Math.Max(_chartViewport.ClientSize.Width, _chart.Projects.Count * LogicalToDeviceUnits(132) + LogicalToDeviceUnits(82)),
            Math.Max(LogicalToDeviceUnits(310), _chartViewport.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight));
        var detailHeight = Math.Max(LogicalToDeviceUnits(532), _detailViewport.ClientSize.Height);
        _details.Size = new Size(Math.Max(LogicalToDeviceUnits(290), _detailViewport.ClientSize.Width -
            (detailHeight > _detailViewport.ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0)), detailHeight);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) { return; }
        try { BeginInvoke(() => { if (!IsDisposed) { ApplyTheme(); } }); }
        catch (InvalidOperationException) { } // Closing can race the system notification.
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_preview) { return; }
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var available = new Size(Math.Max(1, area.Width - LogicalToDeviceUnits(24)), Math.Max(1, area.Height - LogicalToDeviceUnits(24)));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        StartPosition = FormStartPosition.Manual;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    private void ApplyTheme()
    {
        _theme = _forcedTheme ?? ReportTheme.ReadSystem();
        BackColor = _theme.Canvas;
        ForeColor = _theme.Text;
        ThemeChildren(this, _theme.Canvas);
        foreach (var card in _cards) { card.Theme = _theme; card.Invalidate(); }
        _heading.Theme = _summary.Theme = _chart.Theme = _details.Theme = _theme;
        _progress.Theme = _theme;
        _refresh.BackColor = _theme.Accent;
        _refresh.ForeColor = _theme.Dark ? Color.FromArgb(15, 24, 40) : Color.White;
        _refresh.FlatAppearance.MouseOverBackColor = _theme.Dark ? Color.FromArgb(155, 190, 255) : Color.FromArgb(29, 78, 216);
        _cancel.ForeColor = _theme.Accent;
        _cancel.FlatAppearance.BorderColor = _theme.Border;
        _about.LinkColor = _about.ActiveLinkColor = _about.VisitedLinkColor = _theme.Accent;
        _status.ForeColor = _busy ? _theme.Accent : _statusWarning ? _theme.Warning : _theme.Muted;
        Invalidate(true);
        void ThemeChildren(Control parent, Color background)
        {
            foreach (Control child in parent.Controls)
            {
                var surface = child is ReportCard ? _theme.Surface : background;
                child.BackColor = surface;
                child.ForeColor = child.Tag is "muted" ? _theme.Muted : _theme.Text;
                ThemeChildren(child, surface);
            }
        }
    }

    internal async Task GenerateAsync()
    {
        if (!_ready || IsDisposed) { return; }
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        var token = _request.Token;
        var preset = (ReportPreset)_preset.SelectedIndex;
        var from = new DateTimeOffset(_from.Value.Date);
        var to = new DateTimeOffset(_to.Value.Date.AddDays(1));
        if (preset == ReportPreset.Custom && to <= from)
        {
            SetBusy(false);
            _status.Text = "Choose an end date on or after the start date.";
            _aboutText = "Choose an end date on or after the start date, then refresh the report.";
            _statusWarning = true;
            _status.ForeColor = _theme.Warning;
            _report = null;
            _summary.SetReport(null, "Custom dates");
            _chart.SetProjects([], null);
            _details.SetProject(null, 0);
            return;
        }
        SetBusy(true);
        _loadingPhase = preset == ReportPreset.Custom ? "Preparing usage history…" : "Checking current usage window…";
        UpdateLoadingText();
        try
        {
            var snapshot = _snapshot();
            var quotaNote = "";
            if (preset != ReportPreset.Custom)
            {
                // Locate/start the quota client off the UI thread too: its synchronous
                // discovery used to delay the first paint and freeze the loading UI.
                try { snapshot = await Task.Run(() => _readQuota(token), token); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    quotaNote = "Live quota unavailable; using available reset history.";
                }
            }
            _loadingPhase = "Preparing usage history…";
            UpdateLoadingText();
            var request = _request;
            var progress = new Progress<ReportReadProgress>(update =>
            {
                if (!IsDisposed && _busy && !token.IsCancellationRequested && ReferenceEquals(_request, request))
                {
                    _readProgress = update;
                    UpdateLoadingText();
                }
            });
            var now = DateTimeOffset.UtcNow;
            var report = await Task.Run(async () =>
            {
                await _serial.WaitAsync(token);
                try { return _reader.Read(preset, from, to, snapshot, now, token, progress); }
                finally { _serial.Release(); }
            }, token);
            token.ThrowIfCancellationRequested();
            if (!IsDisposed) { ShowReport(report, quotaNote); }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                SetBusy(false);
                _status.Text = _report is null ? "The report could not be loaded. Try Refresh report." :
                    "Refresh failed. The last completed report is still shown; try Refresh report.";
                _statusWarning = true;
                _status.ForeColor = _theme.Warning;
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loadingCard.Visible = busy;
        _status.Visible = !busy;
        _layout.RowStyles[3].Height = LogicalToDeviceUnits(busy ? 104 : 40);
        _chart.Loading = _details.Loading = busy && _report is null;
        _chart.Invalidate();
        _details.Invalidate();
        _refresh.Text = busy ? "↻  Refreshing…" : "↻  Refresh report";
        if (busy)
        {
            _readProgress = null;
            _loadingWatch.Restart();
            _loadingTitle.Text = _report is null ? "Loading usage report…" : "Updating usage report…";
            UpdateLoadingText();
            _loadingTimer.Start();
            _loadingCard.BringToFront();
        }
        else { _loadingTimer.Stop(); _loadingWatch.Stop(); }
    }

    private void UpdateLoadingText()
    {
        if (!_busy) { return; }
        var reading = _readProgress is { Stage: ReportReadStage.Reading, TotalFiles: > 0 };
        _progress.Indeterminate = !reading;
        _progress.Fraction = reading ? _readProgress!.Fraction : 0;
        _progress.Invalidate();
        var phase = _readProgress switch
        {
            { Stage: ReportReadStage.Discovering } update => $"Finding usage history · {update.TotalFiles:N0} files found",
            { Stage: ReportReadStage.Reading } update => $"Reading usage history · {update.CompletedFiles:N0} of {update.TotalFiles:N0} files · {update.Fraction:P0}",
            { Stage: ReportReadStage.Grouping } => "Calculating project totals and reset windows…",
            _ => _loadingPhase
        };
        _loadingDetail.Text = $"{phase} · {(int)_loadingWatch.Elapsed.TotalSeconds}s";
    }

    private void ShowReport(TokenReport report, string quotaNote)
    {
        SetBusy(false);
        _report = report;
        var period = report.Period;
        // Hidden calendar controls belong exclusively to Custom dates. Quota
        // presets show their range in the summary without overwriting this pair.
        _summary.SetReport(period is null ? null : report, _preset.SelectedItem?.ToString() ?? "Usage period",
            calendarDates: _preset.SelectedIndex == (int)ReportPreset.Custom);
        _aboutText = (period is null ? "" : $"Window: {period.Start.ToLocalTime():g} → {period.End.ToLocalTime():g} (end exclusive).\n{period.Note}\n") +
            quotaNote + "\n" + report.Notice + "\n\nCounts reflect current project assignments. Missing fields can appear as zero. Cached input is part of input; reasoning is part of output.";
        _tooltip.SetToolTip(_status, _aboutText);
        var approximate = period?.Note.Contains("approximate", StringComparison.OrdinalIgnoreCase) == true;
        _status.Text = period is null ? report.Notice : quotaNote.Length > 0 ? quotaNote : approximate ?
            "Approximate reset boundary · see About this data for window details." : "Click a project to explore its token mix. Arrow keys also select projects.";
        if (System.Text.RegularExpressions.Regex.IsMatch(report.Notice, @"\b[1-9]\d* (file read failures|malformed usage records)\b"))
        {
            _statusWarning = true;
            _status.Text = "Some local records could not be read · see About this data for coverage details.";
            _status.ForeColor = _theme.Warning;
        }
        else
        {
            _statusWarning = approximate || quotaNote.Length > 0 || period is null;
            _status.ForeColor = _statusWarning ? _theme.Warning : _theme.Muted;
        }
        if (!report.Projects.Any(p => p.Id == _selected)) { _selected = report.Projects.Count > 0 ? report.Projects[0].Id : null; }
        PresentSelection(report);
    }

    private void PresentSelection(TokenReport report)
    {
        var selected = report.Projects.FirstOrDefault(p => p.Id == _selected);
        _chart.SetProjects(report.Projects, selected?.Id);
        _details.SetProject(selected, report.Projects.Sum(p => p.Counts.Total));
        var index = report.Projects.ToList().FindIndex(p => p.Id == _selected);
        if (index < 0 || _chart.Width <= _chartViewport.Width) { return; }
        var center = LogicalToDeviceUnits(72) + (_chart.Width - LogicalToDeviceUnits(82)) * (index + .5f) / report.Projects.Count;
        var offset = -_chartViewport.AutoScrollPosition.X;
        if (center < offset || center > offset + _chartViewport.ClientSize.Width)
        {
            _chartViewport.AutoScrollPosition = new Point(Math.Max(0, (int)center - _chartViewport.ClientSize.Width / 2), 0);
        }
    }

    internal static void ExportPreview(string path)
    {
        ExportOne(path, ReportTheme.Light);
        ExportOne(Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-dark" + Path.GetExtension(path)), ReportTheme.Night);
        ExportOne(Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-loading" + Path.GetExtension(path)), ReportTheme.Light, loading: true);
        ExportOne(Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-loading-dark" + Path.GetExtension(path)), ReportTheme.Night, loading: true);
        ExportOne(Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-custom" + Path.GetExtension(path)), ReportTheme.Light, custom: true);
        ExportOne(Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-custom-dark" + Path.GetExtension(path)), ReportTheme.Night, custom: true);
    }

    internal static void VerifyPresentation()
    {
        using var form = new TokenReportForm(() => null, preview: true, forcedTheme: ReportTheme.Night);
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Size = form.MinimumSize;
        form._preset.SelectedIndex = 4;
        var start = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        var projects = Enumerable.Range(0, 25).Select(index => new ProjectUsage($"project:synthetic-{index}",
            $"Synthetic project {index} with a long display name", Color.CornflowerBlue, ProjectMarker.Folder,
            new TokenCounts(1000 - index, 500, 0, 50, 10), 1)).ToArray();
        var report = new TokenReport(new ReportPeriod(start, start.AddDays(1), "Synthetic fixture"), projects, "Synthetic fixture");
        form.ShowReport(report, "");
        form.Show();
        form.PerformLayout();
        foreach (var control in new Control[] { form._preset, form._from, form._to, form._refresh })
        {
            if (!control.Visible || !control.Parent!.ClientRectangle.Contains(control.Bounds))
            {
                throw new InvalidOperationException("Report filters are clipped at minimum window size.");
            }
        }
        if (form._selected != projects[0].Id) { throw new InvalidOperationException("The initial project breakdown is empty."); }
        var keyDown = typeof(ProjectTokenChart).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        for (var index = 1; index < projects.Length; index++) { keyDown.Invoke(form._chart, [new KeyEventArgs(Keys.Right)]); }
        if (form._selected != projects[^1].Id || form._chartViewport.AutoScrollPosition.X >= 0 ||
            !form._details.AccessibleDescription!.Contains(projects[^1].Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Keyboard selection did not update and reveal the project breakdown.");
        }
        form.ShowReport(report with { Projects = [] }, "");
        if (form._selected is not null) { throw new InvalidOperationException("Empty reports retained a stale project selection."); }
        form.MinimumSize = new Size(840, 660);
        form.Size = form.MinimumSize;
        form.PerformLayout();
        if (!form._refresh.Parent!.ClientRectangle.Contains(form._refresh.Bounds))
        {
            throw new InvalidOperationException($"Report filters did not wrap in a constrained work area: refresh {form._refresh.Bounds}, available {form._refresh.Parent.ClientRectangle}.");
        }
    }

    private static void ExportOne(string path, ReportTheme theme, bool loading = false, bool custom = false)
    {
#pragma warning disable WFO5001
        Application.SetColorMode(theme.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
        using var form = new TokenReportForm(() => null, preview: true, forcedTheme: theme);
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Text = "CodexTray · Static synthetic report preview (not desktop verification)";
        var start = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        ProjectUsage[] projects = [
            new("project:sample-a", "Sample app", ProjectColorMap.ParseColor("green")!.Value, new ProjectMarker("terminal"), new TokenCounts(700000, 400000, 12000, 80000, 23000), 42),
            new("project:sample-b", "Design tools", ProjectColorMap.ParseColor("purple")!.Value, new ProjectMarker("code-brackets"), new TokenCounts(360000, 170000, 2000, 50000, 15000), 28),
            new("project:sample-c", "Documentation", Color.FromArgb(45, 125, 245), new ProjectMarker("folder"), new TokenCounts(120000, 40000, 0, 20000, 4000), 12)];
        form._preset.SelectedIndex = custom ? 4 : 3;
        if (custom)
        {
            form._from.Value = new DateTime(2026, 10, 25);
            form._to.Value = new DateTime(2026, 10, 14); // Reproduce editing the end first.
            form._from.Value = new DateTime(2026, 10, 12);
            start = new DateTimeOffset(form._from.Value.Date);
        }
        form._heading.Synthetic = true;
        form.ShowReport(new TokenReport(new ReportPeriod(start, custom ? new DateTimeOffset(form._to.Value.Date.AddDays(1)) : start.AddDays(2),
            custom ? "Custom dates; end is exclusive." : "Early reset boundary is the first observed change (approximate)."), projects,
            "Static synthetic example · available local recorded tokens including subagents."), "");
        if (loading)
        {
            form.SetBusy(true);
            form._loadingTimer.Stop();
            form._loadingWatch.Reset();
            form._readProgress = new ReportReadProgress(ReportReadStage.Reading, 128, 320);
            form.UpdateLoadingText();
        }
        form.Show();
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var origin = form.PointToScreen(Point.Empty) - (Size)form.Location;
        using var cropped = bitmap.Clone(new Rectangle(origin, form.ClientSize), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        cropped.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            var request = _request;
            _request = null;
            request?.Cancel();
            request?.Dispose();
            _tooltip.Dispose();
            _loadingTimer.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) { foreach (var font in _fonts) { font.Dispose(); } }
    }

    internal static void VerifyResponsiveLoading()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            using var form = new TokenReportForm(() => null, preview: true, readQuota: token =>
            {
                // Model a quota locator that blocks synchronously before returning a Task.
                using var wait = new ManualResetEventSlim();
                wait.Wait(TimeSpan.FromSeconds(3), token);
                throw new OperationCanceledException(token);
            });
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            using var pulse = new System.Windows.Forms.Timer { Interval = 30 };
            var pulses = 0;
            pulse.Tick += (_, _) => pulses++;
            form.Shown += async (_, _) =>
            {
                try
                {
                    form._ready = true;
                    pulse.Start();
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    var loadingTask = form.GenerateAsync();
                    await Task.Delay(220);
                    if (elapsed.ElapsedMilliseconds > 1500 || pulses == 0 || !form._loadingCard.Visible || !form._cancel.Enabled ||
                        !form._progress.Visible || !form._progress.Parent!.ClientRectangle.Contains(form._progress.Bounds) ||
                        form._progress.Top < form._loadingDetail.Bottom)
                    {
                        throw new InvalidOperationException("Report loading did not leave the UI responsive with a visible progress panel.");
                    }
                    form._cancel.PerformClick();
                    await loadingTask;
                    if (form._busy || form._loadingTimer.Enabled || form._loadingCard.Visible)
                    {
                        throw new InvalidOperationException("Report cancellation left loading UI active.");
                    }
                }
                catch (Exception exception) { failure = exception; }
                finally { form.Close(); }
            };
            Application.Run(form);
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10))) { throw new InvalidOperationException("Responsive-loading test timed out."); }
        if (failure is not null) { throw new InvalidOperationException("Responsive-loading test failed.", failure); }
    }

    internal static void VerifyCustomDates()
    {
        using var form = new TokenReportForm(() => null, preview: true);
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        form._preset.SelectedIndex = (int)ReportPreset.Custom;
        form._from.Value = new DateTime(2026, 10, 25);
        form._to.Value = new DateTime(2026, 10, 14);
        Check(form._from.Value.Date == new DateTime(2026, 10, 14) && form._to.Value.Date == form._from.Value.Date,
            "Editing an earlier end date must move the start to the same day.");
        form._from.Value = new DateTime(2026, 10, 1);
        Check(form._to.Value.Date == new DateTime(2026, 10, 14), "A valid start edit must preserve the end date.");
        form._from.Value = new DateTime(2026, 10, 20);
        Check(form._to.Value.Date == form._from.Value.Date, "Editing a later start date must move the end to the same day.");
        form._to.Value = new DateTime(2026, 10, 25);
        var expectedStart = form._from.Value.Date;
        var expectedEnd = form._to.Value.Date;
        foreach (var preset in new[] { ReportPreset.CurrentWeekly, ReportPreset.PreviousFiveHour, ReportPreset.CurrentFiveHour, ReportPreset.PreviousWeekly })
        {
            form._preset.SelectedIndex = (int)preset;
            var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            form.ShowReport(new TokenReport(new ReportPeriod(start, start.AddDays(7), "Synthetic preset"), [], "Synthetic fixture"), "");
            form._preset.SelectedIndex = (int)ReportPreset.Custom;
            Check(form._from.Value.Date == expectedStart && form._to.Value.Date == expectedEnd,
                "Quota preset results must not overwrite the remembered custom range.");
        }
        form._to.Value = expectedStart;
        Check(form._from.Value.Date == form._to.Value.Date, "A same-day custom range must remain valid.");
        var day = new DateTimeOffset(form._from.Value.Date);
        form.ShowReport(new TokenReport(new ReportPeriod(day, new DateTimeOffset(form._to.Value.Date.AddDays(1)), "Synthetic custom day"), [], "Synthetic fixture"), "");
        Check(form._summary.RangeText == $"{form._from.Value:dd MMM yyyy} → {form._to.Value:dd MMM yyyy} (inclusive)",
            "The custom summary must show the inclusive days selected in the date pickers.");
        static void Check(bool condition, string message)
        {
            if (!condition) { throw new InvalidOperationException(message); }
        }
    }
}
