using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CodexTray;

internal static class ProjectMarkerRenderer
{
    // Original, simplified taskbar glyphs rather than copied desktop assets.
    internal static void Draw(Graphics graphics, RectangleF bounds, ProjectMarker marker, Color color)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.FromArgb(15, 23, 30));
        graphics.FillEllipse(background, bounds);
        graphics.TranslateTransform(bounds.X + 1, bounds.Y + 1);
        graphics.ScaleTransform((bounds.Width - 2) / 16, (bounds.Height - 2) / 16);
        graphics.SetClip(new RectangleF(0, 0, 16, 16), CombineMode.Intersect);
        using var pen = new Pen(color, 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        if (marker.IsEmoji)
        {
            DrawSymbol(graphics, marker.Value, brush);
        }
        else
        {
            switch (marker.Value)
            {
                case "terminal":
                    graphics.DrawRectangle(pen, 1, 2, 14, 12);
                    graphics.DrawLines(pen, [new(4, 5), new(7, 8), new(4, 11)]);
                    graphics.DrawLine(pen, 9, 11, 12, 11);
                    break;
                case "function":
                case "code":
                case "code-brackets":
                    graphics.DrawLines(pen, [new(5, 3), new(1, 8), new(5, 13)]);
                    graphics.DrawLines(pen, [new(11, 3), new(15, 8), new(11, 13)]);
                    graphics.DrawLine(pen, 9, 2, 7, 14);
                    break;
                case "plane":
                    graphics.DrawPolygon(pen, [new PointF(8, 1), new(10, 7), new(15, 10), new(15, 12),
                        new(9, 10), new(9, 13), new(11, 15), new(5, 15), new(7, 13), new(7, 10), new(1, 12), new(1, 10), new(6, 7)]);
                    break;
                case "wrench":
                    graphics.DrawLines(pen, [new(10, 1), new(7, 4), new(8, 8), new(2, 12), new(2, 14), new(4, 14),
                        new(10, 9), new(13, 9), new(15, 6), new(11, 7), new(9, 5), new(10, 1)]);
                    break;
                case "book":
                case "logs":
                case "notebook":
                    graphics.DrawPolygon(pen, [new PointF(1, 2), new(7, 3), new(8, 5), new(9, 3), new(15, 2), new(15, 13),
                        new(10, 13), new(8, 15), new(6, 13), new(1, 13)]);
                    graphics.DrawLine(pen, 8, 5, 8, 15);
                    break;
                case "globe":
                case "desk-globe":
                case "globe-spin":
                    graphics.DrawEllipse(pen, 1, 1, 14, 14);
                    graphics.DrawEllipse(pen, 5, 1, 6, 14);
                    graphics.DrawLine(pen, 1, 8, 15, 8);
                    break;
                case "bar-chart":
                    graphics.DrawLines(pen, [new(1, 1), new(1, 15), new(15, 15)]);
                    graphics.DrawLine(pen, 5, 12, 5, 8);
                    graphics.DrawLine(pen, 9, 12, 9, 3);
                    graphics.DrawLine(pen, 13, 12, 13, 6);
                    break;
                case "flask":
                    graphics.DrawLines(pen, [new(5, 1), new(11, 1)]);
                    graphics.DrawPolygon(pen, [new PointF(6, 1), new(10, 1), new(10, 6), new(15, 14), new(1, 14), new(6, 6)]);
                    graphics.DrawLine(pen, 4, 10, 12, 10);
                    break;
                case "heart":
                case "health":
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(8, 14, -6, 6, 3, -3, 8, 4);
                        path.AddBezier(8, 4, 13, -3, 22, 6, 8, 14);
                        graphics.DrawPath(pen, path);
                    }
                    break;
                case "plant":
                    graphics.DrawLine(pen, 8, 15, 8, 4);
                    graphics.DrawBezier(pen, 8, 9, 1, 10, 1, 5, 1, 3);
                    graphics.DrawBezier(pen, 1, 3, 7, 3, 8, 6, 8, 9);
                    graphics.DrawBezier(pen, 8, 6, 15, 7, 15, 2, 15, 1);
                    graphics.DrawBezier(pen, 15, 1, 9, 1, 8, 3, 8, 6);
                    break;
                case "music":
                    graphics.DrawLines(pen, [new(5, 12), new(5, 3), new(13, 1), new(13, 10)]);
                    graphics.FillEllipse(brush, 1, 10, 5, 4);
                    graphics.FillEllipse(brush, 9, 8, 5, 4);
                    break;
                case "edit":
                case "writing":
                case "pencil":
                case "customize":
                case "pens":
                    graphics.DrawPolygon(pen, [new PointF(2, 10), new(11, 1), new(15, 5), new(6, 14), new(1, 15)]);
                    graphics.DrawLine(pen, 9, 3, 13, 7);
                    break;
                case "suitcase":
                    graphics.DrawRectangle(pen, 1, 5, 14, 10);
                    graphics.DrawRectangle(pen, 5, 1, 6, 4);
                    graphics.DrawLine(pen, 1, 9, 15, 9);
                    break;
                case "dumbbell":
                case "kettlebell":
                    graphics.DrawLine(pen, 1, 8, 15, 8);
                    graphics.DrawRectangle(pen, 2, 3, 3, 10);
                    graphics.DrawRectangle(pen, 11, 3, 3, 10);
                    break;
                case "paw":
                case "cat":
                    graphics.FillEllipse(brush, 4, 8, 8, 7);
                    graphics.FillEllipse(brush, 0, 4, 3, 5);
                    graphics.FillEllipse(brush, 4, 0, 3, 5);
                    graphics.FillEllipse(brush, 9, 0, 3, 5);
                    graphics.FillEllipse(brush, 13, 4, 3, 5);
                    break;
                case "graduation-cap":
                case "graduation":
                    graphics.DrawPolygon(pen, [new PointF(1, 5), new(8, 1), new(15, 5), new(8, 9)]);
                    graphics.DrawLines(pen, [new(4, 7), new(4, 12), new(8, 14), new(12, 12), new(12, 7)]);
                    graphics.DrawLine(pen, 15, 5, 15, 12);
                    break;
                case "scale":
                case "balancing-scale":
                    graphics.DrawLines(pen, [new(3, 15), new(13, 15)]);
                    graphics.DrawLine(pen, 8, 1, 8, 15);
                    graphics.DrawLine(pen, 2, 3, 14, 3);
                    graphics.DrawPolygon(pen, [new PointF(3, 3), new(0, 10), new(6, 10)]);
                    graphics.DrawPolygon(pen, [new PointF(13, 3), new(10, 10), new(16, 10)]);
                    break;
                case "stethoscope":
                    graphics.DrawLines(pen, [new(2, 1), new(2, 6), new(5, 9), new(8, 6), new(8, 1)]);
                    graphics.DrawBezier(pen, 5, 9, 5, 18, 13, 17, 13, 8);
                    graphics.DrawEllipse(pen, 11, 4, 4, 4);
                    break;
                case "brain":
                case "lightbulb":
                case "network":
                    graphics.DrawEllipse(pen, 1, 2, 7, 12);
                    graphics.DrawEllipse(pen, 8, 2, 7, 12);
                    graphics.DrawLine(pen, 8, 1, 8, 15);
                    graphics.DrawLine(pen, 3, 6, 6, 8);
                    graphics.DrawLine(pen, 10, 9, 13, 7);
                    break;
                case "palette":
                    graphics.DrawEllipse(pen, 1, 1, 14, 14);
                    graphics.FillEllipse(brush, 4, 3, 3, 3);
                    graphics.FillEllipse(brush, 9, 3, 3, 3);
                    graphics.FillEllipse(brush, 3, 8, 3, 3);
                    graphics.DrawEllipse(pen, 9, 9, 4, 4);
                    break;
                case "popcorn":
                    graphics.DrawPolygon(pen, [new PointF(2, 6), new(14, 6), new(12, 15), new(4, 15)]);
                    graphics.DrawEllipse(pen, 2, 1, 5, 5);
                    graphics.DrawEllipse(pen, 6, 0, 5, 5);
                    graphics.DrawEllipse(pen, 10, 1, 5, 5);
                    break;
                case "lotus":
                case "lite":
                    graphics.DrawEllipse(pen, 5, 1, 6, 13);
                    graphics.DrawBezier(pen, 8, 14, 1, 14, 1, 8, 1, 5);
                    graphics.DrawBezier(pen, 1, 5, 6, 5, 8, 9, 8, 14);
                    graphics.DrawBezier(pen, 8, 14, 15, 14, 15, 8, 15, 5);
                    graphics.DrawBezier(pen, 15, 5, 10, 5, 8, 9, 8, 14);
                    break;
                case "currency-dollar":
                    DrawSymbol(graphics, "$", brush);
                    break;
                default:
                    graphics.DrawPolygon(pen, [new PointF(1, 3), new(6, 3), new(8, 5), new(15, 5), new(13, 14), new(1, 14)]);
                    graphics.DrawLine(pen, 1, 7, 14, 7);
                    break;
            }
        }
        graphics.Restore(state);
    }

    private static void DrawSymbol(Graphics graphics, string value, Brush brush)
    {
        // GDI+ draws font glyphs in the supplied accent, including emoji outlines.
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI Emoji", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(value, font, brush, new RectangleF(-2, -2, 20, 20), format);
    }
}
