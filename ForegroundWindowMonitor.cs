using System.Runtime.InteropServices;

namespace CodexTray;

internal sealed class ForegroundWindowMonitor : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjidWindow = 0;
    private const uint DwmwaExtendedFrameBounds = 9;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private readonly WinEventDelegate _callback;
    private readonly Action _foregroundChanged;
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;

    public ForegroundWindowMonitor(Action foregroundChanged)
    {
        _foregroundChanged = foregroundChanged;
        _callback = OnWinEvent;
        _foregroundHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        _locationHook = SetWinEventHook(
            EventObjectLocationChange,
            EventObjectLocationChange,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        if (_foregroundHook == IntPtr.Zero || _locationHook == IntPtr.Zero)
        {
            AppLog.Write("Foreground-window monitoring is partially unavailable; fullscreen visibility may update late.");
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        if (_locationHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
    }

    public static bool IsFullscreenOn(Rectangle screenBounds)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero || foregroundWindow == GetShellWindow() ||
            !IsWindowVisible(foregroundWindow) || IsIconic(foregroundWindow))
        {
            return false;
        }

        NativeRectangle windowBounds;
        var dwmResult = DwmGetWindowAttribute(
            foregroundWindow,
            DwmwaExtendedFrameBounds,
            out windowBounds,
            Marshal.SizeOf<NativeRectangle>());
        if (dwmResult != 0 && !GetWindowRect(foregroundWindow, out windowBounds))
        {
            return false;
        }

        return CoversScreen(
            Rectangle.FromLTRB(windowBounds.Left, windowBounds.Top, windowBounds.Right, windowBounds.Bottom),
            screenBounds);
    }

    internal static bool CoversScreen(Rectangle windowBounds, Rectangle screenBounds)
    {
        const int roundingTolerance = 1;
        return windowBounds.Left <= screenBounds.Left + roundingTolerance &&
               windowBounds.Top <= screenBounds.Top + roundingTolerance &&
               windowBounds.Right >= screenBounds.Right - roundingTolerance &&
               windowBounds.Bottom >= screenBounds.Bottom - roundingTolerance;
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType == EventObjectLocationChange &&
            (objectId != ObjidWindow || childId != 0 || window != GetForegroundWindow()))
        {
            return;
        }

        try
        {
            _foregroundChanged();
        }
        catch (Exception exception)
        {
            // Exceptions must never cross the native WinEvent callback boundary.
            AppLog.Write($"Foreground-window callback failed: {exception.Message}");
        }
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr eventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        uint attribute,
        out NativeRectangle value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
