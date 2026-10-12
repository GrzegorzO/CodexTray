using System.Runtime.InteropServices;

namespace CodexTray;

internal static class AppLog
{
    private const long MaximumLogLength = 128 * 1024;

    public static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTray",
        "startup.log");

    public static unsafe string PackageContext
    {
        get
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, null);
            if (result == 15700) // APPMODEL_ERROR_NO_PACKAGE
            {
                return "unpackaged";
            }

            if (result != 122) // ERROR_INSUFFICIENT_BUFFER
            {
                return $"unknown (Windows error {result})";
            }

            var name = new char[checked((int)length)];
            fixed (char* buffer = name)
            {
                result = GetCurrentPackageFullName(ref length, buffer);
                return result == 0 ? new string(buffer) : $"unknown (Windows error {result})";
            }
        }
    }

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(PathName)!;
            Directory.CreateDirectory(directory);

            if (File.Exists(PathName) && new FileInfo(PathName).Length > MaximumLogLength)
            {
                File.WriteAllText(PathName, string.Empty);
            }

            File.AppendAllText(
                PathName,
                $"{DateTimeOffset.Now:O} [pid={Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Diagnostics must never prevent the widget from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must never prevent the widget from starting.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe int GetCurrentPackageFullName(ref uint packageFullNameLength, char* packageFullName);
}
