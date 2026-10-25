using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Microsoft.Win32;

namespace CodexTray;

internal sealed record ReportTheme(Color Canvas, Color Surface, Color Text, Color Muted, Color Border,
    Color Accent, Color Selection, Color Warning, bool Dark)
{
    internal static ReportTheme Light { get; } = new(Color.FromArgb(244, 246, 250), Color.White,
        Color.FromArgb(26, 34, 50), Color.FromArgb(96, 109, 130), Color.FromArgb(226, 231, 239),
        Color.FromArgb(37, 99, 235), Color.FromArgb(237, 243, 255), Color.FromArgb(150, 91, 12), false);
    internal static ReportTheme Night { get; } = new(Color.FromArgb(15, 18, 25), Color.FromArgb(23, 28, 38),
        Color.FromArgb(237, 241, 249), Color.FromArgb(153, 166, 188), Color.FromArgb(44, 53, 70),
        Color.FromArgb(125, 170, 255), Color.FromArgb(34, 48, 73), Color.FromArgb(251, 191, 90), true);

    internal static ReportTheme ReadSystem()
    {
        if (SystemInformation.HighContrast)
        {
            return new ReportTheme(SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight,
                SystemColors.Control, SystemColors.WindowText, false);
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? Night : Light;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Light;
        }
    }

    internal static void InitializeNativeMode()
    {
#pragma warning disable WFO5001 // .NET's native system-mode support is intentionally used for this Windows-only UI.
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
    }
}

internal static class ReportDrawing
{
    internal static void Text(Graphics graphics, string text, float size, Color color, RectangleF bounds,
        FontStyle style = FontStyle.Regular, StringAlignment alignment = StringAlignment.Near)
    {
        using var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static string Compact(long count) => count >= 1_000_000_000 ? (count / 1_000_000_000d).ToString("0.##", CultureInfo.CurrentCulture) + "B" :
        count >= 1_000_000 ? (count / 1_000_000d).ToString("0.##", CultureInfo.CurrentCulture) + "M" :
        count >= 1000 ? (count / 1000d).ToString("0.#", CultureInfo.CurrentCulture) + "K" : count.ToString(CultureInfo.CurrentCulture);

    internal static void Prepare(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }
}

internal sealed class ReportCard : Panel
{
    internal ReportTheme Theme = ReportTheme.Light;
    internal ReportCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Canvas);
        ReportDrawing.Prepare(e.Graphics);
        var scale = DeviceDpi / 96f;
        using var path = ReportDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12 * scale);
        using var background = new SolidBrush(Theme.Surface);
        using var border = new Pen(Theme.Border);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal sealed class ReportProgressBar : Control
{
    internal ReportTheme Theme = ReportTheme.Light;
    internal bool Indeterminate = true;
    internal double Fraction;
    internal ReportProgressBar() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ReportDrawing.Prepare(e.Graphics);
        var bounds = new RectangleF(0, 0, Width, Height);
        using var track = ReportDrawing.Rounded(bounds, Height / 2f);
        using var background = new SolidBrush(Theme.Border);
        using var fill = new SolidBrush(Theme.Accent);
        e.Graphics.FillPath(background, track);
        var state = e.Graphics.Save();
        e.Graphics.SetClip(track);
        if (Indeterminate)
        {
            var phase = (Environment.TickCount64 % 1400) / 1400f;
            e.Graphics.FillRectangle(fill, phase * Width * 1.3f - Width * .3f, 0, Width * .3f, Height);
        }
        else { e.Graphics.FillRectangle(fill, 0, 0, (float)Math.Clamp(Fraction, 0, 1) * Width, Height); }
        e.Graphics.Restore(state);
    }
}
