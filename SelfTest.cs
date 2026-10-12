using System.Text.Json;
using System.Xml.Linq;

namespace CodexTray;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            VerifyStartupTask();
            VerifyStartupRecovery();
            using var document = JsonDocument.Parse("""
                {
                  "rateLimits": {},
                  "rateLimitsByLimitId": {
                    "codex": {
                      "planType": "plus",
                      "primary": { "usedPercent": 77, "windowDurationMins": 300, "resetsAt": 1788575616 },
                      "secondary": { "usedPercent": 28, "windowDurationMins": 10080, "resetsAt": 1788973842 }
                    }
                  }
                }
                """);
            var snapshot = CodexRateLimitClient.Parse(document.RootElement);
            Assert(snapshot.FiveHour?.RemainingPercent == 23, "5-hour percentage");
            Assert(snapshot.Weekly?.RemainingPercent == 72, "weekly percentage");
            Assert(snapshot.TrayPercent == 72, "tray percentage");
            Assert(snapshot.PlanType == "plus", "plan type");
            Assert(snapshot.ToTooltip().Contains("5h 23%", StringComparison.Ordinal), "tooltip");
            Assert(TrayIconRenderer.ColorFor(100) == Color.FromArgb(45, 125, 245), "blue upper bound");
            Assert(TrayIconRenderer.ColorFor(90) == Color.FromArgb(45, 125, 245), "blue threshold");
            Assert(TrayIconRenderer.ColorFor(89).G == 190, "green upper bound");
            Assert(TrayIconRenderer.ColorFor(51).G == 190, "green threshold");
            Assert(TrayIconRenderer.ColorFor(50).R == 242, "orange threshold");
            Assert(TrayIconRenderer.ColorFor(26).R == 242, "orange lower bound");
            Assert(TrayIconRenderer.ColorFor(25).R == 225, "red threshold");
            Assert(TrayIconRenderer.ColorFor(6).R == 225, "red lower bound");
            Assert(TrayIconRenderer.ColorFor(5) == Color.FromArgb(168, 85, 247), "purple threshold");
            Assert(TrayIconRenderer.ColorFor(0) == Color.FromArgb(168, 85, 247), "purple lower bound");
            Assert(WidgetSettings.ParseDisplayStyle("Neon") == DisplayStyle.Neon, "named Neon style setting");
            Assert(WidgetSettings.ParseDisplayStyle(5) == DisplayStyle.Neon, "legacy Neon style setting");
            Assert(WidgetSettings.ParseDisplayStyle("Carbon") == DisplayStyle.Carbon, "named Carbon style setting");
            Assert(WidgetSettings.ParseDisplayStyle(8) == DisplayStyle.Carbon, "legacy Carbon style setting");
            Assert(WidgetSettings.ParseDisplayStyle("unknown") == DisplayStyle.Classic, "invalid style setting fallback");
            using var icon = TrayIconRenderer.Render(snapshot);
            Assert(icon.Width > 0 && icon.Height > 0, "battery icon rendering");
            Assert(TaskbarWidgetForm.IsShellProcessName("StartMenuExperienceHost"), "Start menu shell surface");
            Assert(TaskbarWidgetForm.IsShellProcessName("SearchHost"), "Search shell surface");
            Assert(!TaskbarWidgetForm.IsShellProcessName("chrome"), "non-shell fullscreen surface");

            var now = DateTimeOffset.UnixEpoch;
            Assert(TaskbarWidgetForm.FormatResetLabel(new UsageWindow(0, null, now.AddDays(2).AddHours(3)), now) == "2d 3h", "reset label days and hours");
            Assert(TaskbarWidgetForm.FormatResetLabel(new UsageWindow(0, null, now.AddHours(4).AddMinutes(59)), now) == "4h 59m", "reset label hours and minutes");
            Assert(TaskbarWidgetForm.FormatResetLabel(new UsageWindow(0, null, now.AddHours(23).AddMinutes(59)), now) == "23h 59m", "reset label day boundary");
            Assert(TaskbarWidgetForm.FormatResetLabel(new UsageWindow(0, null, now.AddDays(1)), now) == "1d 0h", "reset label whole day");
            Assert(TaskbarWidgetForm.FormatResetLabel(new UsageWindow(0, null, now.AddMinutes(42).AddSeconds(59)), now) == "42m", "reset label minutes");

            using var weeklyOnlyDocument = JsonDocument.Parse("""
                { "rateLimits": { "primary": { "usedPercent": 9, "windowDurationMins": 10080 } } }
                """);
            var weeklyOnly = CodexRateLimitClient.Parse(weeklyOnlyDocument.RootElement);
            Assert(weeklyOnly.FiveHour is null, "weekly-only 5-hour absence");
            Assert(weeklyOnly.Weekly?.RemainingPercent == 91, "weekly-only percentage");

            using var incompleteWindowDocument = JsonDocument.Parse("""
                { "rateLimits": { "primary": { "windowDurationMins": 300 } } }
                """);
            var incompleteWindow = CodexRateLimitClient.Parse(incompleteWindowDocument.RootElement);
            Assert(incompleteWindow.FiveHour is null, "incomplete window is ignored");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test failed: {name}");
        }
    }

    private static void VerifyStartupTask()
    {
        const string executable = @"C:\Apps\Usage & tools\CodexTray.exe";
        const string userSid = "S-1-5-21-123-456-789-1001";
        var task = XElement.Parse(StartupManager.CreateTaskXml(executable, userSid));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var principal = task.Element(ns + "Principals")!.Element(ns + "Principal")!;
        var trigger = task.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!;
        var settings = task.Element(ns + "Settings")!;
        var action = task.Element(ns + "Actions")!.Element(ns + "Exec")!;
        Assert((string?)principal.Element(ns + "UserId") == userSid &&
            (string?)trigger.Element(ns + "UserId") == userSid, "startup is limited to the current user");
        Assert((string?)principal.Element(ns + "LogonType") == "InteractiveToken" &&
            (string?)principal.Element(ns + "RunLevel") == "LeastPrivilege", "startup uses the interactive desktop without elevation");
        Assert((string?)action.Element(ns + "Command") == executable &&
            (string?)action.Element(ns + "WorkingDirectory") == Path.GetDirectoryName(executable), "startup preserves paths with spaces and XML characters");
        Assert((string?)action.Element(ns + "Arguments") == "--startup-supervisor", "startup launches the recovery supervisor");
        Assert((string?)settings.Element(ns + "ExecutionTimeLimit") == "PT0S", "startup never stops a long-running widget at the scheduler's default time limit");
        Assert((bool?)settings.Element(ns + "DisallowStartIfOnBatteries") == false &&
            (bool?)settings.Element(ns + "StopIfGoingOnBatteries") == false, "startup works on battery power");
        Assert((int?)settings.Element(ns + "RestartOnFailure")!.Element(ns + "Count") == 3 &&
            (string?)settings.Element(ns + "RestartOnFailure")!.Element(ns + "Interval") == "PT1M", "startup failures receive bounded retries");
    }

    private static void VerifyStartupRecovery()
    {
        var attempts = 0;
        var waits = 0;
        var result = StartupSupervisor.RunAttempts(() => ++attempts < 3 ? -1 : 0, () => waits++, _ => { });
        Assert(result == 0 && attempts == 3 && waits == 2, "recovery observes child failures including forced termination");

        attempts = waits = 0;
        result = StartupSupervisor.RunAttempts(() => { attempts++; return 0; }, () => waits++, _ => { });
        Assert(result == 0 && attempts == 1 && waits == 0, "normal Exit does not restart the widget");

        attempts = waits = 0;
        result = StartupSupervisor.RunAttempts(() => { attempts++; return 1; }, () => waits++, _ => { });
        Assert(result == 1 && attempts == 4 && waits == 3, "persistent failures exhaust a bounded recovery budget");

        attempts = waits = 0;
        result = StartupSupervisor.RunAttempts(() =>
        {
            if (++attempts == 1)
            {
                throw new System.ComponentModel.Win32Exception(2);
            }

            return 0;
        }, () => waits++, _ => { });
        Assert(result == 0 && attempts == 2 && waits == 1, "a failure before child startup is retried");
    }
}
