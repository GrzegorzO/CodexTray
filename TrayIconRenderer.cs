using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexTray;

internal static class TrayIconRenderer
{
    public static Icon Render(UsageSnapshot? snapshot, bool isError = false)
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        DrawBattery(graphics, new Rectangle(3, 5, 55, 23), snapshot?.FiveHour?.RemainingPercent);
        DrawBattery(graphics, new Rectangle(3, 36, 55, 23), snapshot?.Weekly?.RemainingPercent);

        if (isError)
        {
            using var errorPen = new Pen(Color.FromArgb(235, 55, 55), 6)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(errorPen, 20, 19, 44, 45);
            graphics.DrawLine(errorPen, 44, 19, 20, 45);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    internal static Color ColorFor(int remainingPercent) => remainingPercent switch
    {
        > 50 => Color.FromArgb(32, 190, 95),
        > 25 => Color.FromArgb(242, 153, 34),
        _ => Color.FromArgb(225, 55, 55)
    };

    private static void DrawBattery(Graphics graphics, Rectangle bounds, int? remainingPercent)
    {
        var body = new Rectangle(bounds.X, bounds.Y, bounds.Width - 5, bounds.Height);
        var terminal = new Rectangle(body.Right, bounds.Y + 6, 5, bounds.Height - 12);
        var outlineColor = remainingPercent is null ? Color.FromArgb(150, 160, 170) : Color.White;

        using var emptyBrush = new SolidBrush(Color.FromArgb(205, 36, 40, 44));
        using var outlinePen = new Pen(outlineColor, 3);
        graphics.FillRectangle(emptyBrush, body.X + 2, body.Y + 2, body.Width - 4, body.Height - 4);

        if (remainingPercent is not null)
        {
            var percent = Math.Clamp(remainingPercent.Value, 0, 100);
            var interior = new Rectangle(body.X + 4, body.Y + 4, body.Width - 8, body.Height - 8);
            var fillWidth = percent == 0 ? 0 : Math.Max(2, (int)Math.Round(interior.Width * percent / 100d));
            if (fillWidth > 0)
            {
                using var fillBrush = new SolidBrush(ColorFor(percent));
                graphics.FillRectangle(fillBrush, interior.X, interior.Y, fillWidth, interior.Height);
            }
        }

        graphics.DrawRectangle(outlinePen, body);
        using var terminalBrush = new SolidBrush(outlineColor);
        graphics.FillRectangle(terminalBrush, terminal);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

