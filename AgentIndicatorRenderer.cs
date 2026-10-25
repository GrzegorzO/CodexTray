using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CodexTray;

internal enum LoadingIconStyle
{
    Arc,
    Orbit,
    Pulse,
    Bars,
    Spokes,
    BouncingDots,
    Ripples,
    Diamond,
    Radar,
    Hourglass,
    Robot
}

internal enum AgentIndicatorPosition
{
    Right,
    Left
}

internal enum AgentIndicatorMode
{
    IconAndNumber,
    RepeatedIcons
}

internal static class AgentIndicatorRenderer
{
    internal static Color DefaultColor => Color.FromArgb(125, 211, 252);
    internal static string Title(LoadingIconStyle style) => style switch
    {
        LoadingIconStyle.Arc => "Rotating arc",
        LoadingIconStyle.Orbit => "Orbiting dots",
        LoadingIconStyle.Pulse => "Pulsing dot",
        LoadingIconStyle.Bars => "Animated bars",
        LoadingIconStyle.Spokes => "Spinning spokes",
        LoadingIconStyle.BouncingDots => "Bouncing dots",
        LoadingIconStyle.Ripples => "Ripple rings",
        LoadingIconStyle.Diamond => "Rotating diamond",
        LoadingIconStyle.Radar => "Radar sweep",
        LoadingIconStyle.Hourglass => "Hourglass",
        LoadingIconStyle.Robot => "Robot head",
        _ => style.ToString()
    };

    internal const float Scale = 1.3f;

    private static int BaseWidthFor(int count, AgentIndicatorMode mode) => mode == AgentIndicatorMode.RepeatedIcons
        ? 12 + count * 20 + (count - 1) * 6
        : 32 + count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length * 10;

    internal static int WidthFor(int count, AgentIndicatorMode mode = AgentIndicatorMode.IconAndNumber) => count > 0
        ? (int)Math.Ceiling(BaseWidthFor(count, mode) * Scale) : 0;

    // Index zero always sits nearest the other controls; new icons grow outward.
    internal static float IconXFor(int index, int count, AgentIndicatorPosition side) =>
        side == AgentIndicatorPosition.Right ? 8 + index * 26
            : WidthFor(count, AgentIndicatorMode.RepeatedIcons) / Scale - 24 - index * 26;

    internal static int TaskIndexAt(Point point, Rectangle bounds, int count, AgentIndicatorPosition side)
    {
        if (!bounds.Contains(point)) { return -1; }
        var local = new PointF((point.X - bounds.Left) / Scale,
            (point.Y - bounds.Top - bounds.Height / 2f) / Scale);
        for (var index = 0; index < count; index++)
        {
            // Include animated shapes and the project badge in each icon's slot.
            if (new RectangleF(IconXFor(index, count, side) - 1, -16, 26, 32).Contains(local))
            {
                return index;
            }
        }
        return -1;
    }

    internal static int CycleDuration(LoadingIconStyle style) => style == LoadingIconStyle.Robot ? 3600 : 1200;

    internal static void Draw(Graphics graphics, Rectangle bounds, int count, LoadingIconStyle style, float phase,
        AgentIndicatorMode mode = AgentIndicatorMode.IconAndNumber,
        AgentIndicatorPosition side = AgentIndicatorPosition.Right, bool synchronizeAnimations = false,
        IReadOnlyList<Color?>? projectColors = null, IReadOnlyList<ProjectMarker?>? projectMarkers = null, bool colorLoadingIcons = true,
        IReadOnlyList<bool>? finishedTasks = null)
    {
        if (count <= 0)
        {
            return;
        }

        var state = graphics.Save();
        graphics.TranslateTransform(bounds.Left, bounds.Top + bounds.Height / 2f);
        graphics.ScaleTransform(Scale, Scale);
        if (mode == AgentIndicatorMode.RepeatedIcons)
        {
            for (var index = 0; index < count; index++)
            {
                var icon = new RectangleF(IconXFor(index, count, side), -8, 16, 16);
                var color = projectColors is not null && index < projectColors.Count ? projectColors[index] : null;
                var marker = projectMarkers is not null && index < projectMarkers.Count ? projectMarkers[index] : null;
                if (finishedTasks is not null && index < finishedTasks.Count && finishedTasks[index])
                {
                    DrawFinishedIcon(graphics, icon, style, colorLoadingIcons ? color : null, marker, color ?? DefaultColor);
                    continue;
                }
                DrawIcon(graphics, icon,
                    style, synchronizeAnimations ? phase : (phase + index * 0.08f) % 1,
                    colorLoadingIcons ? color : null);
                if (marker is not null)
                {
                    ProjectMarkerRenderer.Draw(graphics, new RectangleF(icon.Right - 4, icon.Top - 6, 12, 12), marker, color ?? DefaultColor);
                }
            }
        }
        else
        {
            DrawIcon(graphics, new RectangleF(6, -8, 16, 16), style, phase);
            using var font = new Font("Segoe UI", 10f * 96f / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            graphics.DrawString(count.ToString(System.Globalization.CultureInfo.InvariantCulture), font, brush,
                new RectangleF(28, -16, BaseWidthFor(count, mode) - 28, 32), format);
        }
        graphics.Restore(state);
    }

    private static void DrawFinishedIcon(Graphics graphics, RectangleF icon, LoadingIconStyle style,
        Color? color, ProjectMarker? marker, Color markerColor)
    {
        // Fade the whole rendered slot, including white details and the badge.
        // Render at the target scale to retain sharp vectors on high-DPI surfaces.
        using var transform = graphics.Transform;
        var elements = transform.Elements;
        var scale = MathF.Sqrt(elements[0] * elements[0] + elements[1] * elements[1]);
        var size = Math.Max(1, (int)Math.Ceiling(40 * scale));
        using var bitmap = new Bitmap(size, size);
        using (var layer = Graphics.FromImage(bitmap))
        {
            layer.ScaleTransform(size / 40f, size / 40f);
            DrawIcon(layer, new RectangleF(8, 12, 16, 16), style, 0.25f, color);
            if (marker is not null)
            {
                ProjectMarkerRenderer.Draw(layer, new RectangleF(20, 6, 12, 12), marker, markerColor);
            }
        }
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.4f });
        var destination = new PointF[]
        {
            new(icon.X - 8, icon.Y - 12), new(icon.X + 32, icon.Y - 12), new(icon.X - 8, icon.Y + 28)
        };
        graphics.DrawImage(bitmap, destination, new RectangleF(0, 0, size, size), GraphicsUnit.Pixel, attributes);
    }

    private static void DrawIcon(Graphics graphics, RectangleF icon, LoadingIconStyle style, float phase, Color? projectColor = null)
    {
        var color = projectColor ?? DefaultColor;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        switch (style)
        {
            case LoadingIconStyle.Arc:
                using (var track = new Pen(Color.FromArgb(65, color), 2))
                using (var arc = new Pen(color, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    graphics.DrawEllipse(track, icon);
                    graphics.DrawArc(arc, icon, phase * 360 - 90, 100);
                }
                break;
            case LoadingIconStyle.Orbit:
                for (var index = 0; index < 8; index++)
                {
                    var angle = (index / 8f + phase) * Math.PI * 2;
                    using var dot = new SolidBrush(Color.FromArgb(55 + index * 28, color));
                    graphics.FillEllipse(dot, icon.X + 6.5f + (float)Math.Cos(angle) * 6,
                        icon.Y + 6.5f + (float)Math.Sin(angle) * 6, 3, 3);
                }
                break;
            case LoadingIconStyle.Pulse:
                var pulse = (float)((Math.Sin(phase * Math.PI * 2) + 1) / 2);
                using (var halo = new SolidBrush(Color.FromArgb((int)(35 + pulse * 45), color)))
                using (var dot = new SolidBrush(color))
                {
                    graphics.FillEllipse(halo, icon);
                    var diameter = 6 + pulse * 4;
                    graphics.FillEllipse(dot, icon.X + (16 - diameter) / 2, icon.Y + (16 - diameter) / 2, diameter, diameter);
                }
                break;
            case LoadingIconStyle.Bars:
                using (var pen = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    for (var index = 0; index < 4; index++)
                    {
                        var height = 4 + (float)((Math.Sin((phase - index * 0.15f) * Math.PI * 2) + 1) * 5);
                        var x = icon.X + 2 + index * 4;
                        graphics.DrawLine(pen, x, icon.Y + (16 - height) / 2, x, icon.Y + (16 + height) / 2);
                    }
                }
                break;
            case LoadingIconStyle.Spokes:
                DrawSpokes(graphics, icon, color, phase);
                break;
            case LoadingIconStyle.BouncingDots:
                DrawBouncingDots(graphics, icon, color, phase);
                break;
            case LoadingIconStyle.Ripples:
                DrawRipples(graphics, icon, color, phase);
                break;
            case LoadingIconStyle.Diamond:
                DrawDiamond(graphics, icon, color, phase);
                break;
            case LoadingIconStyle.Radar:
                DrawRadar(graphics, icon, color, phase);
                break;
            case LoadingIconStyle.Hourglass:
                DrawHourglass(graphics, icon, color, phase);
                break;
            case LoadingIconStyle.Robot:
                DrawRobot(graphics, icon, color, phase);
                break;
        }

    }

    private static void DrawSpokes(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        for (var index = 0; index < 12; index++)
        {
            var angle = index / 12f * Math.PI * 2;
            var brightness = (index / 12f - phase + 1) % 1;
            using var pen = new Pen(Color.FromArgb(55 + (int)(brightness * 200), color), 1.7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var dx = (float)Math.Cos(angle);
            var dy = (float)Math.Sin(angle);
            graphics.DrawLine(pen, icon.X + 8 + dx * 4, icon.Y + 8 + dy * 4,
                icon.X + 8 + dx * 7, icon.Y + 8 + dy * 7);
        }
    }

    private static void DrawBouncingDots(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        for (var index = 0; index < 3; index++)
        {
            var bounce = (float)Math.Sin((phase - index * 0.18f) * Math.PI * 2);
            using var brush = new SolidBrush(Color.FromArgb(155 + (int)((bounce + 1) * 50), color));
            graphics.FillEllipse(brush, icon.X + index * 6, icon.Y + 6 - bounce * 4, 4, 4);
        }
    }

    private static void DrawRipples(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        for (var index = 0; index < 2; index++)
        {
            var progress = (phase + index * 0.5f) % 1;
            var radius = 2 + progress * 5;
            using var pen = new Pen(Color.FromArgb((int)(230 * (1 - progress)), color), 1.6f);
            graphics.DrawEllipse(pen, icon.X + 8 - radius, icon.Y + 8 - radius, radius * 2, radius * 2);
        }
        using var center = new SolidBrush(color);
        graphics.FillEllipse(center, icon.X + 7, icon.Y + 7, 2, 2);
    }

    private static void DrawDiamond(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(icon.X + 8, icon.Y + 8);
        graphics.RotateTransform(phase * 360);
        PointF[] points = [new(0, -7), new(7, 0), new(0, 7), new(-7, 0)];
        using var fill = new SolidBrush(Color.FromArgb(45, color));
        using var outline = new Pen(color, 1.7f) { LineJoin = LineJoin.Round };
        graphics.FillPolygon(fill, points);
        graphics.DrawPolygon(outline, points);
        using var accent = new SolidBrush(Color.White);
        graphics.FillEllipse(accent, -1.5f, -8.5f, 3, 3);
        graphics.Restore(state);
    }

    private static void DrawRadar(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        var bounds = new RectangleF(icon.X + 1, icon.Y + 1, 14, 14);
        using var track = new Pen(Color.FromArgb(85, color), 1);
        graphics.DrawEllipse(track, bounds);
        graphics.DrawEllipse(track, icon.X + 4, icon.Y + 4, 8, 8);
        graphics.DrawLine(track, icon.X + 1, icon.Y + 8, icon.X + 15, icon.Y + 8);
        graphics.DrawLine(track, icon.X + 8, icon.Y + 1, icon.X + 8, icon.Y + 15);
        using var sweep = new SolidBrush(Color.FromArgb(75, color));
        graphics.FillPie(sweep, bounds.X, bounds.Y, bounds.Width, bounds.Height, phase * 360 - 70, 70);
        var angle = phase * Math.PI * 2;
        using var hand = new Pen(color, 1.7f) { EndCap = LineCap.Round };
        graphics.DrawLine(hand, icon.X + 8, icon.Y + 8,
            icon.X + 8 + (float)Math.Cos(angle) * 7, icon.Y + 8 + (float)Math.Sin(angle) * 7);
    }

    private static void DrawHourglass(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(icon.X + 8, icon.Y + 8);
        if (phase > 0.8f)
        {
            graphics.RotateTransform((phase - 0.8f) / 0.2f * 180);
        }
        var progress = Math.Min(1, phase / 0.8f);
        using var frame = new Pen(color, 1.4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(frame, [new PointF(-6, -7), new(-5, -4), new(0, 0), new(-5, 4), new(-6, 7)]);
        graphics.DrawLines(frame, [new PointF(6, -7), new(5, -4), new(0, 0), new(5, 4), new(6, 7)]);
        graphics.DrawLine(frame, -6, -7, 6, -7);
        graphics.DrawLine(frame, -6, 7, 6, 7);
        using var sand = new SolidBrush(Color.FromArgb(220, color));
        var topWidth = 4 * (1 - progress);
        graphics.FillPolygon(sand, [new PointF(-topWidth, -2 - topWidth), new(topWidth, -2 - topWidth), new(0, -2)]);
        var bottomWidth = 4 * progress;
        graphics.FillPolygon(sand, [new PointF(-bottomWidth, 5.5f), new(bottomWidth, 5.5f), new(0, 5.5f - bottomWidth)]);
        graphics.FillEllipse(sand, -0.7f, 0.5f + phase % 0.2f * 12, 1.4f, 1.4f);
        graphics.Restore(state);
    }

    private static void DrawRobot(Graphics graphics, RectangleF icon, Color color, float phase)
    {
        var state = graphics.Save();
        // A slightly larger face keeps the expression readable at taskbar size.
        graphics.TranslateTransform(icon.X - 2, icon.Y - 2);
        graphics.ScaleTransform(1.25f, 1.25f);
        graphics.TranslateTransform(0, (float)Math.Sin(phase * Math.PI * 2) * 0.45f);

        using var outline = new Pen(color, 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var ears = new SolidBrush(color == DefaultColor ? Color.FromArgb(80, 155, 192) : Color.FromArgb(210, color));
        graphics.FillRectangle(ears, 0.1f, 7, 2, 4);
        graphics.FillRectangle(ears, 13.9f, 7, 2, 4);
        graphics.DrawLine(outline, 8, 2, 8, 4);
        var glowAlpha = 40 + (int)((Math.Sin(phase * Math.PI * 4) + 1) * 30);
        using var glow = new SolidBrush(Color.FromArgb(glowAlpha, color));
        using var light = new SolidBrush(Color.FromArgb(211, 246, 255));
        graphics.FillEllipse(glow, 5.5f, -0.5f, 5, 5);
        graphics.FillEllipse(light, 7, 1, 2, 2);

        using var head = new GraphicsPath();
        head.AddArc(2, 4, 4, 4, 180, 90);
        head.AddArc(10, 4, 4, 4, 270, 90);
        head.AddArc(10, 10, 4, 4, 0, 90);
        head.AddArc(2, 10, 4, 4, 90, 90);
        head.CloseFigure();
        using var shell = new LinearGradientBrush(new RectangleF(2, 4, 12, 10),
            Color.FromArgb(37, 67, 83), Color.FromArgb(17, 32, 43), LinearGradientMode.Vertical);
        graphics.FillPath(shell, head);
        graphics.DrawPath(outline, head);

        using var face = new SolidBrush(Color.FromArgb(10, 23, 32));
        graphics.FillRectangle(face, 3.5f, 6, 9, 4.7f);
        var blink = phase is > 0.72f and < 0.78f;
        var gaze = (float)Math.Sin(phase * Math.PI * 2) * 0.35f;
        if (blink)
        {
            graphics.DrawLine(outline, 4.5f, 8.5f, 6.5f, 8.5f);
            graphics.DrawLine(outline, 9.5f, 8.5f, 11.5f, 8.5f);
        }
        else
        {
            graphics.FillEllipse(light, 4.5f + gaze, 6.8f, 2, 2.7f);
            graphics.FillEllipse(light, 9.5f + gaze, 6.8f, 2, 2.7f);
        }

        using var smile = new Pen(Color.FromArgb(190, color), 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(smile, 5.5f, 9.5f, 5, 2.5f, 20, 140);
        graphics.Restore(state);
    }
}
