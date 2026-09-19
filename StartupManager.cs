using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace CodexTray;

internal static class StartupManager
{
    private const string ValueName = "CodexTray";
    private const string ShortcutName = "CodexTray.lnk";

    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application path is unavailable.");
            CreateStartupShortcut(executable);
        }
        else
        {
            File.Delete(ShortcutPath);
        }

        RemoveLegacyRunEntry();
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ShortcutName);

    private static void CreateStartupShortcut(string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
        var shellLink = (IShellLinkW)(object)new ShellLink();
        try
        {
            shellLink.SetPath(executable);
            shellLink.SetArguments("--startup");
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            shellLink.SetDescription("Codex usage taskbar widget");
            ((IPersistFile)shellLink).Save(ShortcutPath, remember: true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(StringBuilder file, int maximumPath, IntPtr findData, uint flags);
        void GetIdList(out IntPtr idList);
        void SetIdList(IntPtr idList);
        void GetDescription(StringBuilder description, int maximumName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory(StringBuilder directory, int maximumPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(StringBuilder arguments, int maximumPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCommand(out int showCommand);
        void SetShowCommand(int showCommand);
        void GetIconLocation(StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassId(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurrentFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}

