namespace CodexTray;

internal sealed record WorkingTask(string ThreadId, string? WorkingDirectory, Color? ProjectColor = null,
    ProjectMarker? ProjectMarker = null, string? ProjectName = null, DateTimeOffset? StartedAt = null)
{
    internal string ToTooltip(DateTimeOffset now)
    {
        var name = ProjectName ?? "No project";
        if (StartedAt is not { } startedAt) { return name; }
        var seconds = (long)Math.Max(0, (now - startedAt).TotalSeconds);
        var elapsed = seconds < 60 ? $"{seconds}s" :
            now - startedAt <= TimeSpan.FromMinutes(5) ? $"{seconds / 60}m {seconds % 60}s" : $"{seconds / 60}m";
        return $"{name} · {elapsed}";
    }
}

internal sealed record ProjectMarker(string Value, bool IsEmoji = false)
{
    internal static ProjectMarker Folder { get; } = new("folder");
}
