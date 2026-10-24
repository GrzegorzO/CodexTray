using System.Globalization;
using System.Text.Json;

namespace CodexTray;

internal sealed class ProjectColorReader(string home)
{
    private readonly string _path = Path.Combine(home, ".codex-global-state.json");
    private DateTime _lastWrite;
    private long _lastLength = -1;
    private ProjectColorMap _map = new();

    internal void Refresh()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists)
            {
                _map = new ProjectColorMap();
                _lastLength = -1;
                return;
            }
            if (file.LastWriteTimeUtc == _lastWrite && file.Length == _lastLength)
            {
                return;
            }

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            _map = ProjectColorMap.Parse(document.RootElement);
            _lastWrite = file.LastWriteTimeUtc;
            _lastLength = file.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Preserve the last valid mapping during atomic replacement or a
            // partial write. Initially unavailable mappings use the normal icon.
        }
    }

    internal Color? Resolve(string threadId, string? workingDirectory) => _map.Resolve(threadId, workingDirectory);
    internal ProjectMarker? ResolveMarker(string threadId, string? workingDirectory) => _map.ResolveMarker(threadId, workingDirectory);
    internal string? ResolveName(string threadId, string? workingDirectory) => _map.ResolveName(threadId, workingDirectory);
    internal bool IsInProject(string threadId, string? workingDirectory, string projectDirectory) =>
        _map.IsInProject(threadId, workingDirectory, projectDirectory);
}

internal sealed class ProjectColorMap
{
    private readonly Dictionary<string, Color> _colors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectMarker> _markers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _assignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _legacyIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rootHints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _projectless = new(StringComparer.Ordinal);
    private readonly List<(string Directory, string ProjectId)> _roots = [];

    internal static ProjectColorMap Parse(JsonElement state)
    {
        var map = new ProjectColorMap();
        if (state.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var project in Properties(state, "project-appearances"))
        {
            if (StringProperty(project.Value, "color") is { } token && ParseColor(token) is { } color)
            {
                map._colors[project.Name] = color;
            }
            if (project.Value.ValueKind == JsonValueKind.Object && project.Value.TryGetProperty("marker", out var marker))
            {
                map._markers[project.Name] = ParseMarker(marker);
            }
        }
        foreach (var assignment in Properties(state, "thread-project-assignments"))
        {
            if (StringProperty(assignment.Value, "projectId") is { } projectId)
            {
                map._assignments[assignment.Name] = projectId;
            }
        }
        foreach (var host in Properties(state, "app-server-project-id-by-legacy-project-id-by-host"))
        {
            if (!host.Name.StartsWith("local:", StringComparison.OrdinalIgnoreCase) || host.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            foreach (var alias in host.Value.EnumerateObject())
            {
                if (alias.Value.ValueKind == JsonValueKind.String && alias.Value.GetString() is { } canonicalId)
                {
                    map._legacyIds[canonicalId] = alias.Name;
                }
            }
        }
        foreach (var project in Properties(state, "local-projects"))
        {
            if (StringProperty(project.Value, "name") is { } name && !string.IsNullOrWhiteSpace(name))
            {
                map._names[project.Name] = name.Trim();
            }
            if (project.Value.ValueKind != JsonValueKind.Object ||
                !project.Value.TryGetProperty("rootPaths", out var roots) || roots.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var root in roots.EnumerateArray())
            {
                if (root.ValueKind == JsonValueKind.String && NormalizeDirectory(root.GetString()) is { } directory)
                {
                    map._roots.Add((directory, project.Name));
                    map._names.TryAdd(project.Name, Path.GetFileName(directory) is { Length: > 0 } folder ? folder : directory);
                }
            }
        }
        foreach (var hint in Properties(state, "thread-workspace-root-hints"))
        {
            if (hint.Value.ValueKind == JsonValueKind.String && hint.Value.GetString() is { } directory)
            {
                map._rootHints[hint.Name] = directory;
            }
        }
        if (state.TryGetProperty("projectless-thread-ids", out var projectless) && projectless.ValueKind == JsonValueKind.Array)
        {
            foreach (var thread in projectless.EnumerateArray())
            {
                if (thread.ValueKind == JsonValueKind.String && thread.GetString() is { } id)
                {
                    map._projectless.Add(id);
                }
            }
        }
        map._roots.Sort((left, right) => right.Directory.Length.CompareTo(left.Directory.Length));
        return map;
    }

    internal Color? Resolve(string threadId, string? workingDirectory) =>
        ProjectForTask(threadId, workingDirectory) is { } id ? ColorForProject(id) : null;

    internal string? ResolveName(string threadId, string? workingDirectory)
    {
        if (_projectless.Contains(threadId)) { return null; }
        if (ProjectForTask(threadId, workingDirectory) is { } id)
        {
            if (_names.TryGetValue(id, out var name)) { return name; }
            if (_legacyIds.TryGetValue(id, out var legacy) && _names.TryGetValue(legacy, out name)) { return name; }
        }
        // CLI tasks can have a workspace without any desktop project metadata.
        var directory = NormalizeDirectory(workingDirectory);
        return directory is null ? null : Path.GetFileName(directory) is { Length: > 0 } folder ? folder : directory;
    }

    internal ProjectMarker? ResolveMarker(string threadId, string? workingDirectory)
    {
        if (ProjectForTask(threadId, workingDirectory) is not { } id)
        {
            return null;
        }
        if (_markers.TryGetValue(id, out var marker))
        {
            return marker;
        }
        return _legacyIds.TryGetValue(id, out var legacy) && _markers.TryGetValue(legacy, out marker) ? marker : ProjectMarker.Folder;
    }

    private string? ProjectForTask(string threadId, string? workingDirectory)
    {
        if (_projectless.Contains(threadId))
        {
            return null;
        }
        // An explicit UI assignment wins even when the task moves to a worktree
        // or another directory, or the assigned project has no custom color.
        if (_assignments.TryGetValue(threadId, out var projectId))
        {
            return projectId;
        }
        if (_rootHints.TryGetValue(threadId, out var hint) && ProjectForDirectory(hint) is { } hintedProject)
        {
            return hintedProject;
        }
        return ProjectForDirectory(workingDirectory);
    }

    internal bool IsInProject(string threadId, string? workingDirectory, string projectDirectory)
    {
        if (ProjectForDirectory(projectDirectory) is { } expected)
        {
            var actual = ProjectForTask(threadId, workingDirectory);
            return actual is not null &&
                string.Equals(_legacyIds.GetValueOrDefault(actual, actual), _legacyIds.GetValueOrDefault(expected, expected), StringComparison.Ordinal);
        }
        // CLI installations may have no desktop project metadata.
        var directory = NormalizeDirectory(workingDirectory);
        var root = NormalizeDirectory(projectDirectory);
        return directory is not null && root is not null &&
            (directory.Equals(root, StringComparison.OrdinalIgnoreCase) ||
             directory.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    internal static ProjectMarker ParseMarker(JsonElement marker)
    {
        if (StringProperty(marker, "kind") == "icon" && StringProperty(marker, "icon") is { Length: > 0 and <= 64 } icon)
        {
            return new ProjectMarker(icon);
        }
        if (StringProperty(marker, "kind") == "emoji" && StringProperty(marker, "emoji") is { Length: > 0 and <= 64 } emoji &&
            !emoji.Any(char.IsControl) && !string.IsNullOrWhiteSpace(emoji))
        {
            return new ProjectMarker(StringInfo.GetNextTextElement(emoji.Trim()), IsEmoji: true);
        }
        return ProjectMarker.Folder;
    }

    private Color? ColorForProject(string id)
    {
        if (_colors.TryGetValue(id, out var color))
        {
            return color;
        }
        return _legacyIds.TryGetValue(id, out var legacy) && _colors.TryGetValue(legacy, out color) ? color : null;
    }

    private string? ProjectForDirectory(string? value)
    {
        if (NormalizeDirectory(value) is not { } directory)
        {
            return null;
        }
        string? projectId = null;
        var matchedLength = -1;
        foreach (var root in _roots)
        {
            if (matchedLength >= 0 && root.Directory.Length < matchedLength)
            {
                break;
            }
            var prefix = Path.EndsInDirectorySeparator(root.Directory) ? root.Directory : root.Directory + Path.DirectorySeparatorChar;
            if (directory.Equals(root.Directory, StringComparison.OrdinalIgnoreCase) ||
                directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (projectId is not null && projectId != root.ProjectId)
                {
                    return null;
                }
                projectId = root.ProjectId;
                matchedLength = root.Directory.Length;
            }
        }
        return projectId;
    }

    internal static Color? ParseColor(string token)
    {
        // Codex Desktop's dark-sidebar accent palette, suitable for the taskbar.
        var normalized = token.Trim();
        var hex = normalized.ToLowerInvariant() switch
        {
            "blue" => "339cff",
            "green" => "40c977",
            "orange" => "ff8549",
            "pink" => "ff8cc1",
            "purple" => "ad7bf9",
            "red" => "ff6764",
            "yellow" => "ffd240",
            _ => normalized.StartsWith('#') && normalized.Length == 7 ? normalized[1..] : null
        };
        return hex is not null && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) : null;
    }

    private static IEnumerable<JsonProperty> Properties(JsonElement state, string key) =>
        state.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject() : Enumerable.Empty<JsonProperty>();

    private static string? StringProperty(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static string? NormalizeDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            return null;
        }
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
