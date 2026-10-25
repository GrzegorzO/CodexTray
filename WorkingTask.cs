namespace CodexTray;

internal sealed record WorkingTask(string ThreadId, string? WorkingDirectory, Color? ProjectColor = null,
    ProjectMarker? ProjectMarker = null, string? ProjectName = null, DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null)
{
    internal string ToTooltip(DateTimeOffset now)
    {
        var name = ProjectName ?? "No project";
        if (StartedAt is not { } startedAt) { return FinishedAt.HasValue ? $"{name} · Finished" : name; }
        var end = FinishedAt ?? now;
        var seconds = (long)Math.Max(0, (end - startedAt).TotalSeconds);
        var elapsed = seconds < 60 ? $"{seconds}s" :
            end - startedAt <= TimeSpan.FromMinutes(5) ? $"{seconds / 60}m {seconds % 60}s" : $"{seconds / 60}m";
        return $"{name} · {(FinishedAt.HasValue ? "Finished · " : "")}{elapsed}";
    }
}

internal sealed record ProjectMarker(string Value, bool IsEmoji = false)
{
    internal static ProjectMarker Folder { get; } = new("folder");
}
