namespace CodexTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--print", StringComparer.OrdinalIgnoreCase))
        {
            return PrintUsageAsync().GetAwaiter().GetResult();
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        var screenshotArgument = Array.FindIndex(
            args,
            argument => string.Equals(argument, "--export-screenshots", StringComparison.OrdinalIgnoreCase));
        if (screenshotArgument >= 0)
        {
            var outputDirectory = screenshotArgument + 1 < args.Length
                ? args[screenshotArgument + 1]
                : Path.Combine(AppContext.BaseDirectory, "screenshots");
            ApplicationConfiguration.Initialize();
            return ScreenshotExporter.Run(outputDirectory);
        }

        if (args.Contains("--enable-startup", StringComparer.OrdinalIgnoreCase))
        {
            StartupManager.SetEnabled(enabled: true);
            return 0;
        }

        if (args.Contains("--disable-startup", StringComparer.OrdinalIgnoreCase))
        {
            StartupManager.SetEnabled(enabled: false);
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\CodexTray", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        var startedByWindows = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        AppLog.Write($"Launch requested (startup={startedByWindows}, style={WidgetSettings.SelectedStyle}).");

        try
        {
            // Explorer and secondary taskbars can still be coming online when a
            // Startup shortcut is invoked. Give the shell a moment before attaching the
            // taskbar-owned windows; interactive launches remain immediate.
            if (startedByWindows)
            {
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
            AppLog.Write("Application exited normally.");
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Write($"Fatal startup error: {exception}");
            return 1;
        }
    }

    private static async Task<int> PrintUsageAsync()
    {
        try
        {
            var snapshot = await CodexRateLimitClient.ReadAsync(CancellationToken.None);
            Console.WriteLine(snapshot.ToDisplayText());
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
