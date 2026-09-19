namespace CodexTray;

internal static class AppLog
{
    private const long MaximumLogLength = 128 * 1024;

    public static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTray",
        "startup.log");

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
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
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
}
