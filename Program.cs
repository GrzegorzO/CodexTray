namespace CodexTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            AppLog.Write($"Fatal application error: {exception}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Contains("--print-project-icons", StringComparer.OrdinalIgnoreCase))
        {
            var argument = Array.FindIndex(args, value => value.Equals("--print-project-icons", StringComparison.OrdinalIgnoreCase));
            var count = argument + 1 < args.Length && !args[argument + 1].StartsWith("--", StringComparison.Ordinal)
                ? DesktopProjectIcons.LoadArchive(args[argument + 1]).Count : DesktopProjectIcons.Count;
            Console.WriteLine($"Desktop project vectors loaded: {count}/30");
            return 0;
        }
        if (args.Contains("--print-activity", StringComparer.OrdinalIgnoreCase))
        {
            var includeColors = args.Contains("--project-colors", StringComparer.OrdinalIgnoreCase);
            var tasks = new AgentActivityMonitor().ReadTasks(CancellationToken.None, includeColors);
            Console.WriteLine($"Running main tasks: {tasks.Count}");
            if (includeColors)
            {
                for (var index = 0; index < tasks.Count; index++)
                {
                    var value = tasks[index].ProjectColor is { } color ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : "default";
                    Console.WriteLine($"Task {index + 1} icon color: {value}");
                }
            }
            return 0;
        }

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

        if (args.Contains("--startup-supervisor", StringComparer.OrdinalIgnoreCase))
        {
            return StartupSupervisor.Run();
        }

        var startedByWindows = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        AppLog.Write($"Launch requested (startup={startedByWindows}, executable={Environment.ProcessPath}, version={typeof(Program).Assembly.GetName().Version}, package={AppLog.PackageContext}).");

        using var mutex = new Mutex(true, @"Local\CodexTray", out var isFirstInstance);
        if (!isFirstInstance)
        {
            AppLog.Write("Launch skipped: another instance already holds Local\\CodexTray.");
            return 0;
        }

        AppLog.Write($"Loading settings (style={WidgetSettings.SelectedStyle}, widgetVisible={WidgetSettings.Visible}).");

        // Explorer and secondary taskbars can still be coming online at logon.
        if (startedByWindows)
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
        }

        // WinForms otherwise displays a thread-error dialog and can leave an
        // unusable process alive. A nonzero exit allows startup-task recovery.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            AppLog.Write($"Unhandled application error: {eventArgs.ExceptionObject}");
        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        AppLog.Write("Tray initialized; entering message loop.");
        Application.Run(context);
        AppLog.Write("Application exited normally.");
        return 0;
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
