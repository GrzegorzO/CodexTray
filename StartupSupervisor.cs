using System.ComponentModel;
using System.Diagnostics;

namespace CodexTray;

internal static class StartupSupervisor
{
    public static int Run()
    {
        AppLog.Write($"Startup supervisor launched (package={AppLog.PackageContext}).");
        using var mutex = new Mutex(true, @"Local\CodexTray.StartupSupervisor", out var isFirstInstance);
        if (!isFirstInstance)
        {
            AppLog.Write("Startup supervisor skipped: another supervisor is running.");
            return 0;
        }

        return RunAttempts(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath
                    ?? throw new InvalidOperationException("The application path is unavailable."),
                Arguments = "--startup",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var child = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The widget process could not be started.");
            AppLog.Write($"Startup child started (pid={child.Id}).");
            child.WaitForExit();
            return child.ExitCode;
        }, () => Thread.Sleep(TimeSpan.FromMinutes(1)));
    }

    internal static int RunAttempts(Func<int> runChild, Action waitBeforeRetry, Action<string>? writeLog = null)
    {
        writeLog ??= AppLog.Write;
        const int maximumAttempts = 4;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            int exitCode;
            try
            {
                exitCode = runChild();
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
            {
                writeLog($"Startup child could not run: {exception}");
                exitCode = 1;
            }

            if (exitCode == 0)
            {
                writeLog("Startup child exited successfully; supervisor is stopping.");
                return 0;
            }

            writeLog($"Startup child failed (attempt={attempt}/{maximumAttempts}, exitCode={exitCode}).");
            if (attempt < maximumAttempts)
            {
                writeLog("Retrying the widget in one minute.");
                waitBeforeRetry();
            }
        }

        writeLog("Startup recovery attempts exhausted.");
        return 1;
    }
}
