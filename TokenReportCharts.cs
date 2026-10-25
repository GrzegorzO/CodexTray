using System.Drawing.Drawing2D;
using System.Globalization;

namespace CodexTray;

internal sealed class ReportHeading : Control
{
    internal ReportTheme Theme = ReportTheme.Light;
    internal bool Synthetic;
    internal ReportHeading() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        ReportDrawing.Prepare(g);
        var width = Width / scale;
        ReportDrawing.Text(g, "CODEXTRAY  /  ANALYTICS", 10, Theme.Muted, new(0, 0, width - 160, 18), FontStyle.Bold);
        ReportDrawing.Text(g, "Usage overview", 28, Theme.Text, new(0, 20, width - 160, 38), FontStyle.Bold);
        ReportDrawing.Text(g, Synthetic ? "Static synthetic preview · not desktop verification" : "See where your tokens go, project by project.",
            12, Theme.Muted, new(0, 58, width - 160, 20));
        using var pill = ReportDrawing.Rounded(new RectangleF(width - 142, 25, 142, 30), 15);
        using var background = new SolidBrush(Theme.Selection);
        g.FillPath(background, pill);
        ReportDrawing.Text(g, Theme.Dark ? "Windows · Dark" : "Windows · Light", 12, Theme.Accent, new(width - 142, 25, 142, 30), alignment: StringAlignment.Center);
    }
}

internal sealed class ReportSummary : Control
{
    internal ReportTheme Theme = ReportTheme.Light;
    private TokenReport? _report;
    private string _period = "Usage period";
    internal ReportSummary() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    internal void SetReport(TokenReport? report, string period) { _report = report; _period = period; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        ReportDrawing.Prepare(g);
        var width = Width / scale;
        var total = _report?.Projects.Sum(p => p.Counts.Total) ?? 0;
        var a = width * .32f;
        var b = width * .51f;
        using var pen = new Pen(Theme.Border);
        g.DrawLine(pen, a - 18, 10, a - 18, 70);
        g.DrawLine(pen, b - 18, 10, b - 18, 70);
        ReportDrawing.Text(g, "TOKENS USED", 10, Theme.Muted, new(0, 0, a - 30, 20), FontStyle.Bold);
        ReportDrawing.Text(g, _report is null ? "—" : ReportDrawing.Compact(total), 30, Theme.Text, new(0, 20, a - 30, 38), FontStyle.Bold);
        ReportDrawing.Text(g, _report is null ? "Choose a period to begin" : $"{total:N0} tokens recorded", 12, Theme.Muted, new(0, 60, a - 30, 20));
        ReportDrawing.Text(g, "PROJECTS", 10, Theme.Muted, new(a, 0, b - a - 30, 20), FontStyle.Bold);
        ReportDrawing.Text(g, _report is null ? "—" : _report.Projects.Count.ToString(CultureInfo.CurrentCulture), 30, Theme.Text, new(a, 20, b - a - 30, 38), FontStyle.Bold);
        ReportDrawing.Text(g, "With recorded usage", 12, Theme.Muted, new(a, 60, b - a - 30, 20));
        ReportDrawing.Text(g, "SELECTED PERIOD", 10, Theme.Muted, new(b, 0, width - b, 20), FontStyle.Bold);
        ReportDrawing.Text(g, _period, 18, Theme.Text, new(b, 22, width - b, 32), FontStyle.Bold);
        var range = _report?.Period is { } period ? $"{period.Start.ToLocalTime():dd MMM, HH:mm} → {period.End.ToLocalTime():dd MMM, HH:mm}" : "Reset history or custom calendar dates";
        ReportDrawing.Text(g, range, 12, Theme.Muted, new(b, 60, width - b, 20));
    }
}

internal sealed class ProjectTokenChart : Control
{
    internal bool Loading;
    internal IReadOnlyList<ProjectUsage> Projects { get; private set; } = [];
    internal ReportTheme Theme = ReportTheme.Light;
    private string? _selected;
    private readonly List<RectangleF> _hits = [];
    private readonly ToolTip _tooltip = new();
    private int _hover = -1;
    private float _renderScale = 1;
    internal event Action<string>? ProjectSelected;
    internal event Action? SizeRequested;

    internal ProjectTokenChart()
    {
        DoubleBuffered = true;
        TabStop = true;
        AccessibleName = "Project token usage. Use arrow keys to select a project.";
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    internal void SetProjects(IReadOnlyList<ProjectUsage> projects, string? selected)
    {
        Projects = projects;
        _selected = selected;
        _hover = -1;
        _tooltip.SetToolTip(this, null);
        _hits.Clear();
        SizeRequested?.Invoke();
        AccessibleDescription = string.Join("; ", projects.Select(p => $"{p.Name}: {p.Counts.Total:N0} tokens"));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawChart(e.Graphics, ClientRectangle);
    }
    internal void DrawChart(Graphics graphics, Rectangle bounds)
    {
        _hits.Clear();
        var state = graphics.Save();
        _renderScale = graphics.DpiX / 96f;
        graphics.ScaleTransform(_renderScale, _renderScale);
        ReportDrawing.Prepare(graphics);
        var width = bounds.Width / _renderScale;
        var height = bounds.Height / _renderScale;
        if (Projects.Count == 0)
        {
            ReportDrawing.Text(graphics, Loading ? "Loading project usage…" : "No recorded usage in this period", 15, Theme.Muted, new(0, height / 2 - 25, width, 30), alignment: StringAlignment.Center);
            graphics.Restore(state);
            return;
        }
        const float left = 66, top = 36;
        var bottom = height - 100;
        var slot = (width - left - 10) / Projects.Count;
        var max = Math.Max(1, Projects.Max(p => p.Counts.Total));
        var roughStep = max / 4d;
        var power = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var fraction = roughStep / power;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
        var axisMax = nice * power * 4;
        using var grid = new Pen(Theme.Border);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = bottom - (bottom - top) * tick / 4;
            graphics.DrawLine(grid, left, y, width - 10, y);
            ReportDrawing.Text(graphics, ReportDrawing.Compact((long)(axisMax * tick / 4)), 11, Theme.Muted, new(0, y - 10, left - 12, 20), alignment: StringAlignment.Far);
        }
        for (var index = 0; index < Projects.Count; index++)
        {
            var project = Projects[index];
            var x = left + slot * index;
            var selected = project.Id == _selected;
            var barWidth = Math.Min(110, slot * .56f);
            var barHeight = Math.Max(2, (float)((bottom - top) * project.Counts.Total / axisMax));
            var bar = new RectangleF(x + (slot - barWidth) / 2, bottom - barHeight, barWidth, barHeight);
            if (selected || _hover == index)
            {
                using var highlight = ReportDrawing.Rounded(new RectangleF(x + 5, top - 8, slot - 10, height - top + 3), 8);
                using var highlightBrush = new SolidBrush(selected ? Theme.Selection : Color.FromArgb(Theme.Dark ? 22 : 12, project.Color));
                graphics.FillPath(highlightBrush, highlight);
            }
            using var path = ReportDrawing.Rounded(bar, 5);
            using var fill = new LinearGradientBrush(bar, Color.FromArgb(255, project.Color), Color.FromArgb(230, project.Color), LinearGradientMode.Vertical);
            graphics.FillPath(fill, path);
            if (selected)
            {
                using var line = new Pen(project.Color, 2);
                graphics.DrawLine(line, x + slot * .3f, bottom + 82, x + slot * .7f, bottom + 82);
            }
            ReportDrawing.Text(graphics, ReportDrawing.Compact(project.Counts.Total), 13, Theme.Text, new(x, bar.Top - 30, slot, 24), FontStyle.Bold, StringAlignment.Center);
            ProjectMarkerRenderer.Draw(graphics, new RectangleF(x + slot / 2 - 10, bottom + 14, 20, 20), project.Marker ?? ProjectMarker.Folder, project.Color);
            ReportDrawing.Text(graphics, project.Name, 12, selected ? Theme.Text : Theme.Muted, new(x + 5, bottom + 42, slot - 10, 30),
                selected ? FontStyle.Bold : FontStyle.Regular, StringAlignment.Center);
            _hits.Add(new RectangleF(x, top - 10, slot, height - top + 10));
        }
        graphics.Restore(state);
    }
    internal int HitTest(Point point) => _hits.FindIndex(hit => hit.Contains(point.X / _renderScale, point.Y / _renderScale));
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) { return; }
        Focus();
        var index = HitTest(e.Location);
        if (index >= 0) { ProjectSelected?.Invoke(Projects[index].Id); }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = HitTest(e.Location);
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        if (_hover == index) { return; }
        _hover = index;
        _tooltip.SetToolTip(this, index < 0 ? null : $"{Projects[index].Name}\n{Projects[index].Id}\n{Projects[index].Counts.Total:N0} tokens · click for breakdown");
        Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Projects.Count == 0 || e.KeyCode is not (Keys.Left or Keys.Right)) { return; }
        var current = Projects.ToList().FindIndex(p => p.Id == _selected);
        var next = Math.Clamp(current + (e.KeyCode == Keys.Right ? 1 : -1), 0, Projects.Count - 1);
        ProjectSelected?.Invoke(Projects[next].Id);
        e.Handled = true;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tooltip.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed record TokenSlice(string Name, long Count, Color Color);
internal static class TokenBreakdown
{
    // These four slices partition input + output. Cache writes remain a separately
    // reported input subset so they cannot be counted again in the donut.
    internal static IReadOnlyList<TokenSlice> Create(TokenCounts counts)
    {
        var cached = Math.Clamp(counts.CachedInput, 0, Math.Max(0, counts.Input));
        var reasoning = Math.Clamp(counts.Reasoning, 0, Math.Max(0, counts.Output));
        return [
            new("Other input", counts.Input - cached, Color.FromArgb(96, 165, 250)),
            new("Cached input", cached, Color.FromArgb(52, 211, 153)),
            new("Other output", counts.Output - reasoning, Color.FromArgb(251, 146, 60)),
            new("Reasoning", reasoning, Color.FromArgb(192, 132, 252))];
    }
}

internal sealed class TokenDetailChart : Control
{
    internal bool Loading;
    internal ReportTheme Theme = ReportTheme.Light;
    private ProjectUsage? _project;
    private long _periodTotal;
    private readonly ToolTip _tooltip = new();
    private readonly List<RectangleF> _legendHits = [];
    private IReadOnlyList<TokenSlice> _slices = [];
    private float _renderScale = 1;
    private RectangleF _donut;
    private string? _hoverText;

    internal TokenDetailChart()
    {
        DoubleBuffered = true;
        AccessibleName = "Selected project token breakdown";
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    internal void SetProject(ProjectUsage? project, long periodTotal)
    {
        _project = project;
        _periodTotal = periodTotal;
        _slices = project is null ? [] : TokenBreakdown.Create(project.Counts);
        AccessibleDescription = project is null ? "No recorded usage" :
            $"{project.Name}. Total {project.Counts.Total:N0}. Input {project.Counts.Input:N0}. Cached input {project.Counts.CachedInput:N0}. Cache writes {project.Counts.CacheWrite:N0}. Output {project.Counts.Output:N0}. Reasoning {project.Counts.Reasoning:N0}.";
        _tooltip.SetToolTip(this, null);
        _hoverText = null;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawDetails(e.Graphics, ClientRectangle);
    }
    internal void DrawDetails(Graphics graphics, Rectangle bounds)
    {
        _legendHits.Clear();
        var state = graphics.Save();
        _renderScale = graphics.DpiX / 96f;
        graphics.ScaleTransform(_renderScale, _renderScale);
        ReportDrawing.Prepare(graphics);
        var width = bounds.Width / _renderScale;
        if (_project is not { } project)
        {
            ReportDrawing.Text(graphics, "Project breakdown", 18, Theme.Text, new(4, 8, width - 8, 30), FontStyle.Bold);
            ReportDrawing.Text(graphics, Loading ? "Preparing token breakdown…" : "No recorded usage in this period", 13, Theme.Muted, new(4, 70, width - 8, 30));
            graphics.Restore(state);
            return;
        }
        ProjectMarkerRenderer.Draw(graphics, new RectangleF(4, 8, 26, 26), project.Marker ?? ProjectMarker.Folder, project.Color);
        ReportDrawing.Text(graphics, project.Name, 17, Theme.Text, new(42, 1, width - 46, 32), FontStyle.Bold);
        var share = _periodTotal == 0 ? 0 : (double)project.Counts.Total / _periodTotal;
        ReportDrawing.Text(graphics, $"{share:P1} of period · {project.Events:N0} usage updates", 11, Theme.Muted, new(42, 34, width - 46, 24));
        _donut = new RectangleF(width / 2 - 79, 68, 158, 158);
        var angle = -90f;
        using (var empty = new SolidBrush(Theme.Border)) { graphics.FillEllipse(empty, _donut); }
        foreach (var slice in _slices)
        {
            var sweep = project.Counts.Total <= 0 ? 0 : (float)(slice.Count * 360d / project.Counts.Total);
            if (sweep > 0)
            {
                using var fill = new SolidBrush(slice.Color);
                graphics.FillPie(fill, _donut.X, _donut.Y, _donut.Width, _donut.Height, angle, sweep);
                angle += sweep;
            }
        }
        using (var center = new SolidBrush(Theme.Surface)) { graphics.FillEllipse(center, _donut.X + 24, _donut.Y + 24, _donut.Width - 48, _donut.Height - 48); }
        ReportDrawing.Text(graphics, ReportDrawing.Compact(project.Counts.Total), 26, Theme.Text, new(_donut.X + 18, 119, _donut.Width - 36, 36), FontStyle.Bold, StringAlignment.Center);
        ReportDrawing.Text(graphics, "TOTAL TOKENS", 9, Theme.Muted, new(_donut.X + 18, 158, _donut.Width - 36, 20), FontStyle.Bold, StringAlignment.Center);
        ReportDrawing.Text(graphics, $"{project.Counts.Total:N0} tokens", 12, Theme.Text, new(0, 236, width, 22), alignment: StringAlignment.Center);
        ReportDrawing.Text(graphics, "TOKEN BREAKDOWN", 10, Theme.Muted, new(4, 266, width - 8, 20), FontStyle.Bold);
        for (var index = 0; index < _slices.Count; index++)
        {
            var slice = _slices[index];
            var y = 296 + 30 * index;
            using var dot = new SolidBrush(slice.Color);
            graphics.FillEllipse(dot, 5, y + 9, 8, 8);
            ReportDrawing.Text(graphics, slice.Name, 12, Theme.Text, new(22, y, width - 184, 26));
            ReportDrawing.Text(graphics, slice.Count.ToString("N0", CultureInfo.CurrentCulture), 13, Theme.Text, new(width - 161, y, 98, 26), FontStyle.Bold, StringAlignment.Far);
            ReportDrawing.Text(graphics, (project.Counts.Total == 0 ? 0 : slice.Count / (double)project.Counts.Total).ToString("P1", CultureInfo.CurrentCulture),
                11, Theme.Muted, new(width - 61, y, 59, 26), alignment: StringAlignment.Far);
            _legendHits.Add(new RectangleF(0, y, width, 30));
        }
        using var divider = new Pen(Theme.Border);
        graphics.DrawLine(divider, 4, 424, width - 4, 424);
        Row("Input (all)", project.Counts.Input, 432);
        Row("Output (all)", project.Counts.Output, 457);
        Row("Cache writes · input subset", project.Counts.CacheWrite, 482);
        var inconsistent = project.Counts.CachedInput > project.Counts.Input || project.Counts.Reasoning > project.Counts.Output;
        ReportDrawing.Text(graphics, inconsistent ? "Chart capped to totals; see recorded counts in About this data." : "Other input includes cache writes. Other output excludes reasoning.",
            10, inconsistent ? Theme.Warning : Theme.Muted, new(4, 512, width - 8, 18));
        graphics.Restore(state);

        void Row(string name, long value, float y)
        {
            ReportDrawing.Text(graphics, name, 11, Theme.Muted, new(4, y, width - 115, 24));
            ReportDrawing.Text(graphics, value.ToString("N0", CultureInfo.CurrentCulture), 12, Theme.Text, new(width - 108, y, 104, 24), alignment: StringAlignment.Far);
        }
    }
    internal int SliceAt(Point point)
    {
        var x = point.X / _renderScale;
        var y = point.Y / _renderScale;
        var legend = _legendHits.FindIndex(hit => hit.Contains(x, y));
        if (legend >= 0) { return legend; }
        if (_project is null || _project.Counts.Total <= 0) { return -1; }
        var dx = x - (_donut.X + _donut.Width / 2);
        var dy = y - (_donut.Y + _donut.Height / 2);
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < _donut.Width / 2 - 24 || radius > _donut.Width / 2) { return -1; }
        var angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 450) % 360;
        var end = 0d;
        for (var index = 0; index < _slices.Count; index++)
        {
            end += _slices[index].Count * 360d / _project.Counts.Total;
            if (angle < end) { return index; }
        }
        return -1;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = SliceAt(e.Location);
        var text = index < 0 ? _project?.Name : $"{_slices[index].Name}: {_slices[index].Count:N0} tokens";
        if (_hoverText == text) { return; }
        _hoverText = text;
        _tooltip.SetToolTip(this, text);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tooltip.Dispose(); }
        base.Dispose(disposing);
    }
}
