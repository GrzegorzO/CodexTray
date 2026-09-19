using System.Text.Json;

namespace CodexTray;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
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
}
