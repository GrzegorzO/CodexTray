namespace CodexTray;

internal sealed record WorkingTask(string ThreadId, string? WorkingDirectory, Color? ProjectColor = null, ProjectMarker? ProjectMarker = null);

internal sealed record ProjectMarker(string Value, bool IsEmoji = false)
{
    internal static ProjectMarker Folder { get; } = new("folder");
}
