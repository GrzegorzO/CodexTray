using System.Diagnostics;

namespace CodexTray;

internal static class CodexSessionLauncher
{
    internal static ProcessStartInfo CreateStartInfo(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return new ProcessStartInfo($"codex://threads/{Uri.EscapeDataString(threadId)}")
        {
            UseShellExecute = true
        };
    }

    internal static void Open(string threadId)
    {
        using var process = Process.Start(CreateStartInfo(threadId));
    }
}
