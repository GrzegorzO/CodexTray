using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CodexTray;

internal sealed class ProjectVectorIcon
{
    private sealed record Shape(PointF[] Points, byte[] Types, FillMode FillMode);
    private readonly List<Shape> _shapes = [];

    internal static ProjectVectorIcon Parse(string body)
    {
        using var text = new StringReader("<svg>" + body + "</svg>");
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        var icon = new ProjectVectorIcon();
        foreach (var element in document.Root!.Elements())
        {
            if (element.Name.LocalName != "path" || element.Attribute("stroke") is not null ||
                element.Attribute("transform") is not null || element.Attribute("fill")?.Value == "none")
            {
                throw new FormatException("Unsupported project vector shape.");
            }
            using var path = ParsePath(element.Attribute("d")?.Value ?? "");
            icon._shapes.Add(new Shape(path.PathPoints, path.PathTypes,
                element.Attribute("fill-rule")?.Value == "evenodd" ? FillMode.Alternate : FillMode.Winding));
        }
        if (icon._shapes.Count == 0) { throw new FormatException("Empty project vector."); }
        return icon;
    }

    internal void Draw(Graphics graphics, Brush brush)
    {
        foreach (var shape in _shapes)
        {
            using var path = new GraphicsPath(shape.Points, shape.Types, shape.FillMode);
            graphics.FillPath(brush, path);
        }
    }

    private static GraphicsPath ParsePath(string data)
    {
        var tokens = Regex.Matches(data, @"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (tokens.Count is < 1 or > 50_000) { throw new FormatException("Invalid project path length."); }
        var consumed = 0;
        foreach (Match token in tokens)
        {
            for (; consumed < token.Index; consumed++)
            {
                if (!char.IsWhiteSpace(data[consumed]) && data[consumed] != ',') { throw new FormatException("Invalid project path token."); }
            }
            consumed = token.Index + token.Length;
        }
        if (!string.IsNullOrWhiteSpace(data[consumed..])) { throw new FormatException("Invalid trailing project path data."); }
        var path = new GraphicsPath();
        var index = 0;
        var current = PointF.Empty;
        var start = PointF.Empty;
        var command = '\0';
        var started = false;
        try
        {
            while (index < tokens.Count)
            {
                if (char.IsLetter(tokens[index].Value[0])) { command = tokens[index++].Value[0]; }
                var relative = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        current = Point(relative);
                        start = current;
                        path.StartFigure();
                        started = true;
                        command = relative ? 'l' : 'L';
                        break;
                    case 'L' when started:
                        Line(Point(relative));
                        break;
                    case 'H' when started:
                        Line(new PointF(Number() + (relative ? current.X : 0), current.Y));
                        break;
                    case 'V' when started:
                        Line(new PointF(current.X, Number() + (relative ? current.Y : 0)));
                        break;
                    case 'C' when started:
                        var first = Point(relative);
                        var second = Point(relative);
                        var last = Point(relative);
                        path.AddBezier(current, first, second, last);
                        current = last;
                        break;
                    case 'Z' when started:
                        path.CloseFigure();
                        current = start;
                        command = '\0';
                        break;
                    default:
                        throw new FormatException("Unsupported project path command.");
                }
            }
            if (path.PointCount == 0) { throw new FormatException("Empty project path."); }
            return path;
        }
        catch
        {
            path.Dispose();
            throw;
        }

        float Number()
        {
            if (index >= tokens.Count || !float.TryParse(tokens[index++].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !float.IsFinite(value) || Math.Abs(value) > 10_000)
            {
                throw new FormatException("Invalid project path coordinate.");
            }
            return value;
        }
        PointF Point(bool relative) => new(Number() + (relative ? current.X : 0), Number() + (relative ? current.Y : 0));
        void Line(PointF point)
        {
            path.AddLine(current, point);
            current = point;
        }
    }
}
