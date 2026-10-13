using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CodexTray;

internal static class DesktopProjectIcons
{
    private static readonly Lazy<IReadOnlyDictionary<string, ProjectVectorIcon>> Icons = new(LoadInstalled);
    private static readonly string Quote = ((char)96).ToString();
    private static readonly Dictionary<string, string> Presets = new(StringComparer.Ordinal)
    {
        ["bar-chart"] = "bar-chart",
        ["book"] = "book",
        ["brain"] = "brain",
        ["currency-dollar"] = "dollarsign-circle",
        ["customize"] = "paintbrush-on-pencil",
        ["desk-globe"] = "desk-globe",
        ["dumbbell"] = "dumbbell",
        ["edit"] = "pencil",
        ["flask"] = "flask",
        ["folder"] = "folder",
        ["function"] = "braces",
        ["globe"] = "globe",
        ["graduation-cap"] = "graduation-cap",
        ["health"] = "star-of-life",
        ["heart"] = "heart",
        ["kettlebell"] = "kettlebell",
        ["logs"] = "notebook",
        ["lotus"] = "lotus",
        ["music"] = "music-note",
        ["palette"] = "paint-palette",
        ["paw"] = "paw",
        ["plane"] = "plane",
        ["plant"] = "plant",
        ["popcorn"] = "popcorn",
        ["scale"] = "balance-scale",
        ["stethoscope"] = "stethoscope",
        ["suitcase"] = "suitcase",
        ["terminal"] = "terminal",
        ["wrench"] = "wrench",
        ["writing"] = "pen-tip"
    };

    internal static int Count => Icons.Value.Count;
    internal static bool TryGet(string name, out ProjectVectorIcon? icon) => Icons.Value.TryGetValue(name, out icon);

    private static IReadOnlyDictionary<string, ProjectVectorIcon> LoadInstalled()
    {
        foreach (var archive in FindArchives())
        {
            try
            {
                var icons = LoadArchive(archive);
                if (icons.Count > 0) { return icons; }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                FormatException or System.Xml.XmlException or ArgumentException or OverflowException or
                KeyNotFoundException or InvalidOperationException or RegexMatchTimeoutException)
            {
                // Desktop assets are optional. An incompatible installation uses
                // the built-in glyphs without interrupting activity detection.
            }
        }
        return new Dictionary<string, ProjectVectorIcon>();
    }

    private static IEnumerable<string> FindArchives()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "ChatGPT", "Codex" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } executable)
                        {
                            candidates.Add(Path.Combine(Path.GetDirectoryName(executable)!, "resources", "app.asar"));
                        }
                    }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException) { }
                }
            }
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Programs", "Codex", "resources", "app.asar"));
        candidates.Add(Path.Combine(local, "OpenAI", "Codex", "resources", "app.asar"));
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            foreach (var name in packages?.GetSubKeyNames().Where(name => name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
                ?? Enumerable.Empty<string>())
            {
                using var package = packages!.OpenSubKey(name);
                if (package?.GetValue("PackageRootFolder") is string root)
                {
                    candidates.Add(Path.Combine(root, "app", "resources", "app.asar"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        try
        {
            var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            foreach (var directory in Directory.GetDirectories(store, "OpenAI.Codex_*").OrderDescending(StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(directory, "app", "resources", "app.asar"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return candidates.Where(File.Exists);
    }

    internal static IReadOnlyDictionary<string, ProjectVectorIcon> LoadArchive(string path)
    {
        using var archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> prefix = stackalloc byte[8];
        archive.ReadExactly(prefix);
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix[4..]);
        if (headerLength is < 8 or > 16 * 1024 * 1024) { throw new InvalidDataException("Invalid desktop archive header."); }
        var header = new byte[headerLength];
        archive.ReadExactly(header);
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (jsonLength <= 0 || jsonLength > headerLength - 8) { throw new InvalidDataException("Invalid desktop archive index."); }
        using var document = JsonDocument.Parse(header.AsMemory(8, jsonLength));
        var assets = document.RootElement.GetProperty("files").GetProperty("webview").GetProperty("files")
            .GetProperty("assets").GetProperty("files");
        string initial = "", shared = "";
        foreach (var asset in assets.EnumerateObject())
        {
            var isInitial = asset.Name.StartsWith("app-initial-", StringComparison.Ordinal);
            if ((!isInitial && !asset.Name.StartsWith("app-shared-", StringComparison.Ordinal)) ||
                !asset.Name.EndsWith(".js", StringComparison.Ordinal) || !asset.Value.TryGetProperty("offset", out var offsetValue))
            {
                continue;
            }
            var size = asset.Value.GetProperty("size").GetInt32();
            var offset = long.Parse(offsetValue.GetString()!, CultureInfo.InvariantCulture);
            var position = checked(8L + headerLength + offset);
            if (size is < 0 or > 32 * 1024 * 1024 || position < 0 || position > archive.Length - size)
            {
                throw new InvalidDataException("Invalid desktop asset bounds.");
            }
            archive.Position = position;
            var bytes = new byte[size];
            archive.ReadExactly(bytes);
            if (isInitial) { initial = Encoding.UTF8.GetString(bytes); }
            else { shared = Encoding.UTF8.GetString(bytes); }
        }
        return Extract(initial, shared);
    }

    internal static IReadOnlyDictionary<string, ProjectVectorIcon> Extract(string initial, string shared)
    {
        var result = new Dictionary<string, ProjectVectorIcon>(StringComparer.Ordinal);
        foreach (var (name, preset) in Presets)
        {
            // The sidebar supplies its own legacy folder fallback. Other named
            // markers use the desktop's optically authored 16px vectors.
            var body = name == "folder" ? LegacyBody(initial, name) : PresetBody(shared, preset);
            body ??= LegacyBody(initial, name);
            if (body is null) { continue; }
            try { result[name] = ProjectVectorIcon.Parse(body); }
            catch (Exception exception) when (exception is FormatException or System.Xml.XmlException or ArgumentException)
            {
                // Keep other supported icons when a single glyph changes format.
            }
        }
        return result;
    }

    private static string? PresetBody(string source, string name)
    {
        var start = source.IndexOf("name:" + Quote + name + "-light-16" + Quote, StringComparison.Ordinal);
        if (start < 0) { return null; }
        var body = source.IndexOf("body:" + Quote, start, StringComparison.Ordinal);
        if (body < 0 || body - start > 3000) { return null; }
        body += 6;
        var end = source.IndexOf(Quote, body, StringComparison.Ordinal);
        return end > body && end - body < 100_000 ? source[body..end] : null;
    }

    private static string? LegacyBody(string source, string name)
    {
        var match = Match(source, $@"(?:^|[,{{])""?{Regex.Escape(name)}""?:\{{legacy:(?<id>[\w$]+)");
        if (!match.Success) { return null; }
        var start = source.IndexOf(match.Groups["id"].Value + "=e=>", StringComparison.Ordinal);
        if (start < 0) { return null; }
        var end = source.IndexOf("function ", start, StringComparison.Ordinal);
        if (end < 0 || end - start > 100_000) { return null; }
        var component = source[start..end];
        if (!component.Contains("viewBox:" + Quote + "0 0 16 16" + Quote, StringComparison.Ordinal)) { return null; }
        var body = new StringBuilder();
        foreach (Match path in Regex.Matches(component, Quote + @"path" + Quote + @",\{(?<attributes>[^{}]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
        {
            var attributes = path.Groups["attributes"].Value;
            var data = Match(attributes, @"(?:^|,)d:" + Quote + "(?<data>[^" + Quote + "]+)" + Quote);
            if (!data.Success) { return null; }
            var element = new XElement("path", new XAttribute("d", data.Groups["data"].Value));
            if (attributes.Contains("fillRule:" + Quote + "evenodd" + Quote, StringComparison.Ordinal))
            {
                element.SetAttributeValue("fill-rule", "evenodd");
            }
            body.Append(element.ToString(SaveOptions.DisableFormatting));
        }
        return body.Length > 0 ? body.ToString() : null;
    }

    private static Match Match(string source, string pattern) =>
        Regex.Match(source, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
}
