using System.Runtime.InteropServices;
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
            VerifyStartupTaskLookup();
            VerifyStartupRecovery();
            VerifyTaskbarVisibility();
            VerifyAgentActivity();
            VerifyAgentIndicator();
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

    private static void VerifyTaskbarVisibility()
    {
        var primary = new Rectangle(0, 0, 1920, 1080);
        var videoMonitor = new Rectangle(-2560, -574, 2560, 1440);
        Assert(!Blocks(videoMonitor, primary), "fullscreen video on another monitor does not hide this widget");
        Assert(Blocks(videoMonitor, videoMonitor), "fullscreen video covers its own monitor");
        Assert(!Blocks(new Rectangle(0, 0, 1920, 1032), primary), "maximized work-area browser leaves taskbar visible");
        Assert(!Blocks(Rectangle.Empty, primary), "missing window bounds do not hide widget");
        Assert(TaskbarVisibility.CoversMonitor(new Rectangle(1, 1, 1918, 1078), primary), "fullscreen frame rounding tolerance");
        Assert(!TaskbarVisibility.BlocksTaskbar(primary, primary, true, false, false, true, false), "own animated surface cannot change taskbar visibility");
        Assert(!TaskbarVisibility.BlocksTaskbar(primary, primary, true, false, false, false, true), "Start and Search surfaces preserve taskbar visibility");
        Assert(!TaskbarVisibility.BlocksTaskbar(primary, primary, false, false, false, false, false), "hidden fullscreen window does not block taskbar");
        Assert(!TaskbarVisibility.BlocksTaskbar(primary, primary, true, true, false, false, false), "minimized fullscreen window does not block taskbar");
        Assert(!TaskbarVisibility.BlocksTaskbar(primary, primary, true, false, true, false, false), "cloaked window on another virtual desktop does not block taskbar");
        var spanning = Rectangle.Union(primary, videoMonitor);
        Assert(Blocks(spanning, primary) && Blocks(spanning, videoMonitor), "window spanning both monitors covers both taskbars");

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        widget.ApplyAgentSettings(true, true, LoadingIconStyle.Robot, AgentIndicatorPosition.Right);
        widget.SetWorkingAgentCount(2);
        _ = widget.Handle;
        Rectangle? invalidated = null;
        widget.Invalidated += (_, eventArgs) => invalidated = eventArgs.InvalidRect;
        widget.InvalidateAnimationFrame();
        Assert(invalidated == widget.ClientRectangle, "animation invalidates a complete buffered frame including batteries");

        static bool Blocks(Rectangle window, Rectangle monitor) =>
            TaskbarVisibility.BlocksTaskbar(window, monitor, true, false, false, false, false);
    }

    private static void VerifyAgentActivity()
    {
        var state = new AgentActivityMonitor.ActivityState();
        Assert(!state.IsWorking, "unknown activity is idle");
        state.Apply("""{"type":"event_msg","payload":{"type":"task_started","turn_id":"one"}}""");
        Assert(state.IsWorking, "started turn is working");
        state.Apply("""{"type":"event_msg","payload":{"type":"task_complete","turn_id":"previous"}}""");
        Assert(state.IsWorking, "late completion cannot stop a newer turn");
        state.Apply("""{"type":"response_item","payload":{"type":"task_complete","turn_id":"one"}}""");
        Assert(state.IsWorking, "content cannot masquerade as a lifecycle event");
        state.Apply("""{"type":"event_msg","payload":{"type":"task_complete","turn_id":"one"}}""");
        Assert(!state.IsWorking, "completed turn is idle");
        state.Apply("""{"type":"event_msg","payload":{"type":"task_started","turn_id":"two"}}""");
        state.Apply("""{"type":"event_msg","payload":{"type":"turn_aborted","turn_id":"two"}}""");
        Assert(!state.IsWorking, "interrupted turn is idle");

        using var stream = new MemoryStream();
        var incremental = new AgentActivityMonitor.ActivityState();
        Append("""{"type":"event_msg","payload":{"type":"task_started","turn_id":"żółć"}}""" + "\n");
        incremental.Read(stream, CancellationToken.None);
        Assert(incremental.IsWorking, "UTF-8 lifecycle event");
        Append("""{"type":"event_msg","payload":{"type":"task_complete","turn_id":"żółć"}}""");
        incremental.Read(stream, CancellationToken.None);
        Assert(incremental.IsWorking, "partially written events wait for newline");
        Append("\n");
        incremental.Read(stream, CancellationToken.None);
        Assert(!incremental.IsWorking, "appended completion is read once complete");
        stream.SetLength(0);
        incremental.Read(stream, CancellationToken.None);
        Assert(!incremental.IsWorking, "truncated rollout resets activity");

        var home = Path.Combine(Path.GetTempPath(), $"CodexTray-activity-test-{Guid.NewGuid():N}");
        var locks = Directory.CreateDirectory(Path.Combine(home, "thread-writer-locks"));
        var sessions = Directory.CreateDirectory(Path.Combine(home, "sessions"));
        const string id = "11111111-2222-3333-4444-555555555555";
        var lockPath = Path.Combine(locks.FullName, id + ".lock");
        var rollout = Path.Combine(sessions.FullName, $"rollout-2026-10-13T00-00-00-{id}.jsonl");
        try
        {
            File.WriteAllText(rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"live"}}""" + "\n");
            using var writer = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            writer.Lock(0, 1);
            var monitor = new AgentActivityMonitor(home);
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "live writer with active turn counts once");
            File.AppendAllText(rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"live"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 0, "loaded idle thread does not count");
            File.AppendAllText(rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"crash"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "next active turn counts");
            writer.Unlock(0, 1);
            Assert(monitor.ReadCount(CancellationToken.None) == 0, "released writer cannot leave a phantom active task");
        }
        finally
        {
            // Remove only the specifically named fixtures; no recursive filesystem cleanup.
            File.Delete(lockPath);
            File.Delete(rollout);
            Directory.Delete(locks.FullName);
            Directory.Delete(sessions.FullName);
            Directory.Delete(home);
        }

        void Append(string text)
        {
            stream.Position = stream.Length;
            stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
        }
    }

    private static void VerifyAgentIndicator()
    {
        var loadingStyles = Enum.GetValues<LoadingIconStyle>();
        Assert(loadingStyles.Length == 11, "eleven loading icon choices including robot head");
        foreach (var style in loadingStyles)
        {
            using var firstFrame = new Bitmap(60, 32);
            using var nextFrame = new Bitmap(60, 32);
            using (var graphics = Graphics.FromImage(firstFrame))
            {
                AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 60, 32), 1, style, 0);
            }
            using (var graphics = Graphics.FromImage(nextFrame))
            {
                AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 60, 32), 1, style, 0.37f);
            }
            var visible = false;
            var changed = false;
            for (var x = 0; x < 26; x++)
                for (var y = 0; y < 32; y++)
                {
                    visible |= firstFrame.GetPixel(x, y).A > 0;
                    changed |= firstFrame.GetPixel(x, y) != nextFrame.GetPixel(x, y);
                }
            Assert(visible && changed, $"{style} draws a visible, animated icon");
        }

        Assert(WidgetSettings.ParseEnum<LoadingIconStyle>("invalid", LoadingIconStyle.Arc) == LoadingIconStyle.Arc,
            "invalid loading style falls back to arc");
        Assert(WidgetSettings.ParseEnum<AgentIndicatorPosition>(null, AgentIndicatorPosition.Right) == AgentIndicatorPosition.Right,
            "counter defaults to right");
        VerifyAgentSizesAndModes();
        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        foreach (var labelPosition in Enum.GetValues<LabelPosition>())
            foreach (var percentagePosition in Enum.GetValues<PercentagePosition>())
                foreach (var labelMode in Enum.GetValues<LabelContentMode>())
                {
                    widget.ApplyDisplaySettings(DisplayStyle.Classic, true, true, labelMode, labelPosition, percentagePosition);
                    widget.SetWorkingAgentCount(0);
                    var idleWidth = widget.ClientSize.Width;
                    foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
                        foreach (var style in Enum.GetValues<LoadingIconStyle>())
                        {
                            widget.ApplyAgentSettings(true, false, style, side);
                            widget.SetWorkingAgentCount(123);
                            Assert(widget.ClientSize.Width == idleWidth + widget.AgentCounterWidth, "counter has separate layout space");
                            Assert(widget.AgentCounterBounds.X == (side == AgentIndicatorPosition.Left ? 0 : idleWidth), "counter stays outside every other control");
                            using var preview = widget.RenderPreview(Color.FromArgb(31, 31, 31));
                            Assert(preview.Width > 0, "all counter styles render");
                            widget.SetWorkingAgentCount(0);
                            Assert(widget.ClientSize.Width == idleWidth && widget.AgentCounterWidth == 0, "idle counter leaves no gap");
                        }
                }

        widget.ApplyAgentSettings(false, true, LoadingIconStyle.Arc, AgentIndicatorPosition.Right);
        var disabledWidth = widget.ClientSize.Width;
        widget.SetWorkingAgentCount(4);
        Assert(widget.AgentCounterWidth == 0 && widget.ClientSize.Width == disabledWidth, "disabled counter stays hidden during activity");
    }

    private static void VerifyAgentSizesAndModes()
    {
        Assert(AgentIndicatorRenderer.ScaleFor(AgentIndicatorSize.Small) == 0.7f &&
            AgentIndicatorRenderer.ScaleFor(AgentIndicatorSize.Normal) == 1f &&
            AgentIndicatorRenderer.ScaleFor(AgentIndicatorSize.Large) == 1.3f, "requested size ratios");
        Assert(WidgetSettings.ParseEnum<AgentIndicatorSize>(null, AgentIndicatorSize.Normal) == AgentIndicatorSize.Normal,
            "existing installations default to normal size");
        Assert(WidgetSettings.ParseEnum<AgentIndicatorMode>("invalid", AgentIndicatorMode.IconAndNumber) == AgentIndicatorMode.IconAndNumber,
            "existing installations keep icon and number");
        Assert(AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Right) == AgentIndicatorRenderer.IconXFor(0, 1, AgentIndicatorPosition.Right),
            "right-side additional icons grow right");
        Assert(AgentIndicatorRenderer.IconXFor(1, 2, AgentIndicatorPosition.Right) > AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Right),
            "right-side icons have positive spacing");
        Assert(AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Left) - AgentIndicatorRenderer.WidthFor(2, mode: AgentIndicatorMode.RepeatedIcons) ==
            AgentIndicatorRenderer.IconXFor(0, 1, AgentIndicatorPosition.Left) - AgentIndicatorRenderer.WidthFor(1, mode: AgentIndicatorMode.RepeatedIcons),
            "left-side nearest icon remains anchored to controls");
        Assert(AgentIndicatorRenderer.IconXFor(1, 2, AgentIndicatorPosition.Left) < AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Left),
            "left-side additional icons grow left");

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        foreach (var size in Enum.GetValues<AgentIndicatorSize>())
            foreach (var mode in Enum.GetValues<AgentIndicatorMode>())
                foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
                    foreach (var style in Enum.GetValues<LoadingIconStyle>())
                    {
                        widget.SetWorkingAgentCount(0);
                        var idleWidth = widget.ClientSize.Width;
                        widget.ApplyAgentSettings(true, false, style, side, size, mode);
                        widget.SetWorkingAgentCount(2);
                        using var preview = widget.RenderPreview(Color.FromArgb(31, 31, 31));
                        Assert(widget.ClientSize.Width == idleWidth + AgentIndicatorRenderer.WidthFor(2, size, mode), "size and mode reserve correct space");
                        widget.SetWorkingAgentCount(0);
                        Assert(widget.ClientSize.Width == idleWidth, "every size and mode collapses when idle");
                    }

        // A repeated two-icon render must contain exactly those two icon images,
        // including the gap, with no number painted into the remaining space.
        foreach (var size in Enum.GetValues<AgentIndicatorSize>())
            foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
                foreach (var synchronized in new[] { false, true })
                {
                    var width = AgentIndicatorRenderer.WidthFor(2, size, AgentIndicatorMode.RepeatedIcons);
                    using var actual = new Bitmap(width, 42);
                    using var expected = new Bitmap(width, 42);
                    using (var graphics = Graphics.FromImage(actual))
                    {
                        AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, width, 42), 2, LoadingIconStyle.Robot, 0.25f,
                            size, AgentIndicatorMode.RepeatedIcons, side, synchronized);
                    }
                    using (var graphics = Graphics.FromImage(expected))
                    {
                        graphics.TranslateTransform(0, 21);
                        graphics.ScaleTransform(AgentIndicatorRenderer.ScaleFor(size), AgentIndicatorRenderer.ScaleFor(size));
                        for (var index = 0; index < 2; index++)
                        {
                            // Draw a normal single icon into a clipped area excluding its count.
                            var state = graphics.Save();
                            var x = AgentIndicatorRenderer.IconXFor(index, 2, side) - 6;
                            graphics.SetClip(new RectangleF(x, -16, 26, 32));
                            AgentIndicatorRenderer.Draw(graphics, new Rectangle((int)x, -21, 42, 42), 1,
                                LoadingIconStyle.Robot, synchronized ? 0.25f : 0.25f + index * 0.08f);
                            graphics.Restore(state);
                        }
                    }
                    for (var x = 0; x < width; x++)
                        for (var y = 0; y < 42; y++)
                        {
                            Assert(actual.GetPixel(x, y) == expected.GetPixel(x, y),
                                "repeated mode draws the selected synchronized or staggered icon phases without a number");
                        }
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Simulates the Task Scheduler COM failures handled by the production lookup.")]
    private static void VerifyStartupTaskLookup()
    {
        const int fileNotFound = unchecked((int)0x80070002);
        const int accessDenied = unchecked((int)0x80070005);
        var task = new object();
        Assert(ReferenceEquals(StartupManager.FindTask(new TaskFolderStub(task)), task), "existing startup task is returned");
        Assert(StartupManager.FindTask(new TaskFolderStub(exception: new COMException("Missing task", fileNotFound))) is null,
            "missing startup task reported by COM is absent");
        Assert(StartupManager.FindTask(new TaskFolderStub(exception: new FileNotFoundException("Missing task"))) is null,
            "missing startup task reported by the dynamic COM binder is absent");

        VerifyLookupFailure(new COMException("Access denied", accessDenied));
        VerifyLookupFailure(new FileNotFoundException("Unexpected HRESULT") { HResult = accessDenied });
        VerifyLookupFailure(new IOException("Unrelated I/O failure"));

        static void VerifyLookupFailure(Exception failure)
        {
            try
            {
                StartupManager.FindTask(new TaskFolderStub(exception: failure));
            }
            catch (Exception exception) when (ReferenceEquals(exception, failure))
            {
                return;
            }

            Assert(false, "unrelated startup task lookup errors propagate");
        }
    }

    internal sealed class TaskFolderStub(object? task = null, Exception? exception = null)
    {
        public object? GetTask(string name)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return task;
        }
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
