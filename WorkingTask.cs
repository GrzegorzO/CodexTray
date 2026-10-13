namespace CodexTray;

internal sealed record WorkingTask(string ThreadId, string? WorkingDirectory, Color? ProjectColor = null);
