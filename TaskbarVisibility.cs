namespace CodexTray;

internal static class TaskbarVisibility
{
    internal static bool CoversMonitor(Rectangle window, Rectangle monitor)
    {
        // Ignore tiny frame rounding differences, but distinguish a maximized
        // work-area window from fullscreen and keep neighboring monitors separate.
        const int tolerance = 2;
        return window.Width > 0 && window.Height > 0 &&
            window.Left <= monitor.Left + tolerance && window.Top <= monitor.Top + tolerance &&
            window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;
    }

    internal static bool BlocksTaskbar(Rectangle window, Rectangle monitor,
        bool visible, bool minimized, bool cloaked, bool ownWindow, bool shellSurface) =>
        visible && !minimized && !cloaked && !ownWindow && !shellSurface && CoversMonitor(window, monitor);
}
