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
            TokenReportSelfTest.Run();
            TokenReportPresentationTests.Run();
            VerifyStartupTask();
            VerifyStartupTaskLookup();
            VerifyStartupRecovery();
            VerifyTaskbarVisibility();
            VerifyAgentActivity();
            VerifyAgentCompletionSounds();
            VerifyQuestionAlerts();
            VerifyCustomCompletionSound();
            VerifyMainTaskCounting();
            VerifyWidgetTextDpi();
            VerifyProjectColors();
            VerifyDesktopProjectIcons();
            VerifyAgentIndicator();
            VerifyAgentPresentationFrame();
            VerifyWorkingTaskNavigation();
            VerifyWorkingTaskTooltip();
            VerifyFinishedTasks();
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
            Assert(WidgetSettings.ParseDisplayStyle("unknown") == DisplayStyle.Neon, "invalid style setting fallback");
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
        Assert(invalidated == widget.ClientRectangle, "animation invalidates a complete alpha frame including batteries");

        static bool Blocks(Rectangle window, Rectangle monitor) =>
            TaskbarVisibility.BlocksTaskbar(window, monitor, true, false, false, false, false);
    }

    private static void VerifyWidgetTextDpi()
    {
        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        var now = DateTimeOffset.Now;
        widget.SetSnapshot(new UsageSnapshot(new UsageWindow(6, 300, now.AddHours(4).AddMinutes(30)),
            new UsageWindow(11, 10080, now.AddDays(5).AddHours(12)), null));
        widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, AgentIndicatorPosition.Right);
        widget.SetWorkingAgentCount(12);
        foreach (var style in Enum.GetValues<DisplayStyle>())
            foreach (var labelSide in Enum.GetValues<LabelPosition>())
                foreach (var percentageSide in Enum.GetValues<PercentagePosition>())
                    foreach (var labelMode in Enum.GetValues<LabelContentMode>())
                    {
                        widget.ApplyDisplaySettings(style, true, true, labelMode, labelSide, percentageSide);
                        using var expected = widget.RenderPreview(Color.Black, scale: 1, dpi: 96);
                        foreach (var dpi in new[] { 120, 144, 192, 288 })
                        {
                            using var actual = widget.RenderPreview(Color.Black, scale: 1, dpi: dpi);
                            for (var x = 0; x < expected.Width; x++)
                                for (var y = 0; y < expected.Height; y++)
                                {
                                    Assert(expected.GetPixel(x, y) == actual.GetPixel(x, y),
                                        $"{style} labels, percentages and agent count keep their size at {dpi} DPI");
                                }
                        }
                    }
    }

    private static void VerifyAgentActivity()
    {
        var state = new AgentActivityMonitor.ActivityState("main");
        Assert(!state.IsWorking, "unknown activity is idle");
        Assert(!state.IsMainTask, "sessions without metadata are not classified as main tasks");
        state.Apply("""{"type":"session_meta","payload":{"source":"vscode"}}""");
        Assert(!state.IsMainTask, "metadata without an owner cannot classify a session");
        state.Apply("""{"type":"session_meta","payload":{"id":"ancestor","source":"vscode"}}""");
        Assert(!state.IsMainTask, "ancestor metadata alone cannot classify a session");
        state.Apply("""{"type":"session_meta","payload":{"id":"main","source":"vscode","parent_thread_id":null}}""");
        Assert(state.IsMainTask, "desktop session is a main task");
        state.Apply("""{"type":"session_meta","payload":{"id":"main","source":{"subagent":{"other":"guardian"}}}}""");
        Assert(!state.IsMainTask, "guardian source is excluded without requiring a parent link");
        state.Apply("""{"type":"session_meta","payload":{"id":"main","source":"vscode","parent_thread_id":"parent"}}""");
        Assert(!state.IsMainTask, "parent-linked sessions are excluded regardless of source");
        state.Apply("""{"type":"session_meta","payload":{"id":"main","source":"subagent"}}""");
        Assert(!state.IsMainTask, "legacy string subagent source is excluded");
        state.Apply("""{"type":"session_meta","payload":{"id":"main","source":"cli","forked_from_id":"original"}}""");
        Assert(state.IsMainTask, "user-created fork is still a main task");
        state.Apply("""{"type":"session_meta","payload":{"id":"main","source":"vscode","cwd":"C:\\Projects\\Main"}}""");
        Assert(state.WorkingDirectory == @"C:\Projects\Main", "session working directory is read for project matching");
        state.Apply("""{"type":"session_meta","payload":{"id":"ancestor","source":"subagent","cwd":"C:\\Other"}}""");
        Assert(state.IsMainTask && state.WorkingDirectory == @"C:\Projects\Main",
            "ancestor metadata cannot replace the owner's classification or project directory");
        state.Apply("""{"type":"turn_context","payload":{"cwd":"C:\\Worktrees\\Main"}}""");
        Assert(state.WorkingDirectory == @"C:\Worktrees\Main" && state.IsMainTask, "turn working directory updates without changing task classification");
        var dispatchState = new AgentActivityMonitor.ActivityState("worker");
        const string dispatch = "Pipeline dispatch id: 11111111-2222-3333-4444-555555555555\nRole: tester; round: 1/3.\nFactory instructions: C:/Projects/Factory/AGENTS.md\n";
        Dispatch("assistant", dispatch);
        Assert(dispatchState.FactoryDirectory is null, "an assistant quoting a dispatch cannot classify the session as a factory worker");
        Dispatch("user", dispatch.Replace("11111111-2222-3333-4444-555555555555", "invalid", StringComparison.Ordinal));
        Assert(dispatchState.FactoryDirectory is null, "an invalid dispatch ID does not identify a factory worker");
        Dispatch("user", dispatch);
        Assert(dispatchState.FactoryDirectory == @"C:\Projects\Factory", "a structured user dispatch identifies the factory independently of its installation path");
        dispatchState.Apply("""{"type":"session_meta","payload":{"id":"worker","source":"vscode","forked_from_id":"original"}}""");
        Dispatch("user", dispatch);
        Assert(dispatchState.IsMainTask && dispatchState.FactoryDirectory is null, "copied dispatch history does not hide a user-created main-chat fork");
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
        var incremental = new AgentActivityMonitor.ActivityState("main");
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
            File.WriteAllText(rollout, """{"type":"session_meta","payload":{"id":"11111111-2222-3333-4444-555555555555","source":"cli"}}""" + "\n" +
                """{"type":"event_msg","payload":{"type":"task_started","turn_id":"live"}}""" + "\n");
            using var writer = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            writer.Lock(0, 1);
            var monitor = new AgentActivityMonitor(home);
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "live writer with active turn counts once");
            Assert(monitor.StoppedLifetimes.Count == 0, "initial activity scan never notifies");
            Assert(monitor.StoppedTasks.Count == 0, "initial history does not create finished icons");
            File.AppendAllText(rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"live"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 0, "loaded idle thread does not count");
            Assert(monitor.StoppedLifetimes.Count == 1, "main-task completion produces one sound candidate");
            Assert(monitor.StoppedTasks.Single() is { ThreadId: id, StartedAt: not null, FinishedAt: not null },
                "observed completion supplies a navigable finished task");
            monitor.ReadCount(CancellationToken.None);
            Assert(monitor.StoppedLifetimes.Count == 0, "completion is not repeated on later polls");
            Assert(monitor.StoppedTasks.Count == 0, "finished transition is emitted once");
            File.AppendAllText(rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"crash"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "next active turn counts");
            writer.Unlock(0, 1);
            Assert(monitor.ReadCount(CancellationToken.None) == 0, "released writer cannot leave a phantom active task");
            Assert(monitor.StoppedLifetimes.Count == 1, "released writer stops its previously active main task");
            monitor.ReadCount(CancellationToken.None);
            Assert(monitor.StoppedLifetimes.Count == 0, "released writer does not notify twice");
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

        void Dispatch(string role, string text) => dispatchState.Apply(JsonSerializer.Serialize(new
        {
            type = "response_item",
            payload = new { type = "message", role, content = new[] { new { type = "input_text", text } } }
        }));
    }

    private static string QuestionEvent(string callId, string tool = "request_user_input_async", string? arguments = null, DateTimeOffset? timestamp = null) =>
        JsonSerializer.Serialize(new
        {
            type = "response_item",
            timestamp,
            payload = new
            {
                type = "function_call",
                name = tool,
                call_id = callId,
                arguments = arguments ?? """{"questions":[{"title":"Choose an option","options":["One","Two"]}]}"""
            }
        });

    private static void VerifyQuestionAlerts()
    {
        Assert(AgentCompletionSound.ParseQuestionSoundId(null) == "None" &&
            AgentCompletionSound.ParseQuestionSoundId("missing") == "None", "question sounds default to silent and reject unknown IDs");
        Assert(AgentCompletionSound.ParseQuestionSoundId("question_ludvig_huh") == "question_ludvig_huh" &&
            AgentCompletionSound.ParseSoundId("question_ludvig_huh") == "None", "question selection is independent of completion selection");
        Assert(AgentCompletionSound.ParseQuestionSoundId("question_robot_unit_ready") == "None" &&
            AgentCompletionSound.ParseQuestionSoundId("question_ludvig_whats_up") == "None", "removed question sounds fall back to None");
        Assert(AgentCompletionSound.ParseQuestionSoundId(AgentCompletionSound.CustomSoundId) == AgentCompletionSound.CustomSoundId,
            "custom question selections remain supported");
        Assert(AgentCompletionSound.CustomQuestionSoundPath != AgentCompletionSound.CustomSoundPath,
            "custom question and completion sounds have separate saved copies");
        Assert(AgentCompletionSound.QuestionChoices.Count == 7 && AgentCompletionSound.QuestionChoices.Select(sound => sound.Id).Distinct().Count() == 7,
            "six distinct Hmm/Huh question sounds plus None");
        Assert(AgentCompletionSound.QuestionChoices.Skip(1).All(sound => sound.Id.EndsWith("_hmm", StringComparison.Ordinal) || sound.Id.EndsWith("_huh", StringComparison.Ordinal)) &&
            typeof(AgentCompletionSound).Assembly.GetManifestResourceNames().Count(name => name.Contains(".question_", StringComparison.Ordinal) && name.EndsWith(".wav", StringComparison.Ordinal)) == 6,
            "only Hmm/Huh question clips are offered and embedded; removed clips are absent");
        foreach (var sound in AgentCompletionSound.QuestionChoices.Skip(1))
        {
            using var audio = AgentCompletionSound.OpenSound(sound.Id);
            Assert(audio is not null, "question audio is embedded");
            using var player = new System.Media.SoundPlayer(audio!);
            player.Load();
            Assert(player.IsLoadCompleted, "question audio is a playable WAV");
        }

        using var stream = new MemoryStream();
        var state = new AgentActivityMonitor.ActivityState("main");
        Append(QuestionEvent("historical") + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 0, "existing questions at startup never replay");
        Append(QuestionEvent("new") + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 1, "one new asynchronous question emits one alert");
        Append(QuestionEvent("new") + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 0, "duplicate call IDs do not repeat the question alert");
        Append(QuestionEvent("partial"));
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 0, "incomplete question lines wait for newline");
        Append("\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 1, "completed question lines alert once");
        Append(QuestionEvent("sync", "request_user_input", """{"questions":[{"question":"What next?"}]}""") + "\n");
        Append(QuestionEvent("namespaced", "functions.request_user_input_async") + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 2, "synchronous and namespaced question calls are supported");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 0, "idle polling never repeats a question");
        Append(QuestionEvent("empty", arguments: """{"questions":[]}""") + "\n");
        Append(QuestionEvent("malformed", arguments: "{") + "\n");
        Append(QuestionEvent("wrong-tool", "other_request_user_input") + "\n");
        Append(QuestionEvent("quoted").Replace("\"function_call\"", "\"function_call_output\"", StringComparison.Ordinal) + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 0, "empty, malformed, unrelated and quoted calls do not alert");
        stream.SetLength(0);
        Append(QuestionEvent("rewritten") + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 0, "truncated history establishes a new silent baseline");
        Append(QuestionEvent("after-truncation") + "\n");
        state.Read(stream, CancellationToken.None);
        Assert(state.NewQuestionCount == 1, "new questions still alert after truncation");

        using var discoveredStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            QuestionEvent("old", timestamp: DateTimeOffset.UnixEpoch.AddSeconds(90)) + "\n" +
            QuestionEvent("fresh", timestamp: DateTimeOffset.UnixEpoch.AddSeconds(110)) + "\n"));
        var discovered = new AgentActivityMonitor.ActivityState("new", DateTimeOffset.UnixEpoch.AddSeconds(100));
        discovered.Read(discoveredStream, CancellationToken.None);
        Assert(discovered.NewQuestionCount == 1, "first discovery alerts for questions recorded after monitoring began, excluding older history");
        discoveredStream.SetLength(0);
        discovered.Read(discoveredStream, CancellationToken.None);
        Assert(discovered.NewQuestionCount == 0, "truncation of a newly discovered session clears the initial notification allowance");

        var customQuestionPath = Path.Combine(Path.GetTempPath(), $"CodexTray-question-test-{Guid.NewGuid():N}.wav");
        try
        {
            using (var audio = AgentCompletionSound.OpenSound("question_ludvig_huh")!)
            using (var file = File.Create(customQuestionPath)) { audio.CopyTo(file); }
            using var custom = AgentCompletionSound.OpenSound(AgentCompletionSound.CustomSoundId, customQuestionPath);
            Assert(custom is not null && custom.Length == new FileInfo(customQuestionPath).Length,
                "question custom playback opens its explicit saved path instead of the completion file");
        }
        finally { File.Delete(customQuestionPath); }

        void Append(string text)
        {
            stream.Position = stream.Length;
            stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
        }
    }

    private static void VerifyAgentCompletionSounds()
    {
        Assert(AgentCompletionSound.ParseSoundId(null) == "None", "completion sound defaults to None");
        Assert(AgentCompletionSound.ParseSoundId("missing.wav") == "None", "unknown sound falls back to None");
        Assert(WidgetSettings.ParseEnum<AgentSoundMinimumLifetime>(null, AgentSoundMinimumLifetime.ThirtySeconds) ==
            AgentSoundMinimumLifetime.ThirtySeconds, "minimum sound runtime defaults to 30 seconds");
        Assert(WidgetSettings.ParseEnum("invalid", AgentSoundMinimumLifetime.ThirtySeconds) ==
            AgentSoundMinimumLifetime.ThirtySeconds, "invalid minimum runtime falls back to 30 seconds");
        int[] expectedLifetimes = [0, 10, 30, 60, 300];
        Assert(Enum.GetValues<AgentSoundMinimumLifetime>().Select(value => (int)value).SequenceEqual(expectedLifetimes),
            "all five requested minimum lifetimes are available");
        foreach (var minimum in Enum.GetValues<AgentSoundMinimumLifetime>())
        {
            var boundary = TimeSpan.FromSeconds((int)minimum);
            Assert(!AgentCompletionSound.Qualifies(boundary - TimeSpan.FromTicks(1), minimum), "runtime below threshold is silent");
            Assert(AgentCompletionSound.Qualifies(boundary, minimum), "runtime equal to threshold qualifies");
            Assert(AgentCompletionSound.Qualifies(boundary + TimeSpan.FromSeconds(1), minimum), "runtime above threshold qualifies");
        }
        Assert(AgentCompletionSound.Choices.Count == 10 && AgentCompletionSound.OpenSound("None") is null,
            "nine bundled sounds plus a silent None choice");
        foreach (var sound in AgentCompletionSound.Choices.Skip(1))
        {
            using var audio = AgentCompletionSound.OpenSound(sound.Id);
            Assert(audio is not null, $"{sound.Title} is embedded for single-file publishing");
            using var player = new System.Media.SoundPlayer(audio!);
            player.Load();
            Assert(player.IsLoadCompleted, $"{sound.Title} loads without playing during self-test");
        }

        using var stream = new MemoryStream();
        var state = new AgentActivityMonitor.ActivityState("main");
        Append("""{"type":"session_meta","payload":{"id":"main","source":"cli"}}""");
        Event("task_started", "historical", 0);
        Event("task_complete", "historical", 60);
        Event("task_started", "active-at-startup", 100);
        state.Read(stream, CancellationToken.None);
        Assert(state.IsWorking && state.StoppedLifetimes.Count == 0, "historical completions are silent while an existing active turn is retained");
        Assert(state.StartedAt == DateTimeOffset.UnixEpoch.AddSeconds(100), "active tooltip runtime retains its recorded start at startup");
        Event("task_started", "active-at-startup", 110);
        Event("task_complete", "older-turn", 120);
        Event("task_complete", "active-at-startup", 130);
        Event("task_complete", "active-at-startup", 131);
        // Both events can arrive between polls, even while this thread stays active.
        Event("task_started", "quick", 140);
        Event("turn_aborted", "quick", 150);
        Event("task_started", "next", 160);
        state.Read(stream, CancellationToken.None);
        Assert(state.IsWorking && state.StoppedLifetimes.SequenceEqual(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10) }),
            "turn timestamps survive startup, duplicate starts, stale/duplicate completions, rapid restarts and interruption");
        Assert(state.StartedAt == DateTimeOffset.UnixEpoch.AddSeconds(160), "tooltip runtime resets with each new turn");
        state.Read(stream, CancellationToken.None);
        Assert(state.StoppedLifetimes.Count == 0, "incremental reads drain sound candidates once");
        stream.SetLength(0);
        Append("""{"type":"session_meta","payload":{"id":"main","source":"cli"}}""");
        Event("task_started", "rewritten", 0);
        Event("task_complete", "rewritten", 60);
        state.Read(stream, CancellationToken.None);
        Assert(state.StoppedLifetimes.Count == 0, "truncated session history is not replayed as new completions");
        Append("""{"type":"session_meta","payload":{"id":"main","source":"subagent"}}""");
        Event("task_started", "worker", 200);
        Event("task_complete", "worker", 300);
        state.Read(stream, CancellationToken.None);
        Assert(state.StoppedLifetimes.Count == 0, "subagent stops do not produce sound candidates");

        void Append(string line)
        {
            stream.Position = stream.Length;
            stream.Write(System.Text.Encoding.UTF8.GetBytes(line + "\n"));
        }

        void Event(string type, string turnId, int seconds) => Append(JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            type = "event_msg",
            payload = new { type, turn_id = turnId }
        }));
    }

    private static void VerifyCustomCompletionSound()
    {
        Assert(AgentCompletionSound.ParseSoundId(AgentCompletionSound.CustomSoundId) == AgentCompletionSound.CustomSoundId,
            "custom sound selection is preserved by settings parsing");
        Assert(AgentCompletionSound.ParseSoundId(@"C:\arbitrary.wav") == "None", "sound IDs cannot become arbitrary file paths");
        var directory = Path.Combine(Path.GetTempPath(), $"CodexTray-custom-sound-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "user sound.wav");
        var destination = Path.Combine(directory, "custom-completion.wav");
        var invalid = Path.Combine(directory, "invalid.wav");
        try
        {
            using (var bundled = AgentCompletionSound.OpenSound("06_confirmation_short")!)
            using (var output = File.Create(source))
            {
                bundled.CopyTo(output);
            }
            var expected = File.ReadAllBytes(source);
            AgentCompletionSound.ImportCustomSound(source, destination);
            File.Delete(source);
            Assert(File.ReadAllBytes(destination).SequenceEqual(expected), "custom sound survives deletion of the original user file");
            using (var player = new System.Media.SoundPlayer(destination))
            {
                player.Load();
                Assert(player.IsLoadCompleted, "imported custom WAV loads without playing during self-test");
            }
            File.WriteAllText(invalid, "This is not WAV audio.");
            RejectInvalidSound();
            File.WriteAllBytes(invalid, expected[..^1]);
            RejectInvalidSound();
            var compressed = (byte[])expected.Clone();
            // The bundled fixture has a standard 16-byte fmt chunk at offset 12.
            compressed[20] = 3; // IEEE float rather than PCM
            File.WriteAllBytes(invalid, compressed);
            RejectInvalidSound();
            Assert(File.ReadAllBytes(destination).SequenceEqual(expected), "invalid selections preserve the last valid custom sound");
            Assert(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "failed custom imports clean their staging files");

            using (var bundled = AgentCompletionSound.OpenSound("04_task_complete_chime")!)
            using (var output = File.Create(source))
            {
                bundled.CopyTo(output);
            }
            using (var previous = AgentCompletionSound.OpenCustomSound(destination))
            {
                AgentCompletionSound.ImportCustomSound(source, destination);
                Assert(File.ReadAllBytes(destination).SequenceEqual(File.ReadAllBytes(source)), "custom sound can be replaced while its previous stream is open");
                Assert(previous.Length == expected.Length, "replacement does not invalidate an existing playback stream");
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(destination);
            File.Delete(invalid);
            Directory.Delete(directory);
        }

        void RejectInvalidSound()
        {
            try
            {
                AgentCompletionSound.ImportCustomSound(invalid, destination);
                throw new InvalidOperationException("Self-test failed: invalid custom WAV was accepted");
            }
            catch (InvalidDataException)
            {
                // Invalid selections must not replace a working custom sound.
            }
        }
    }

    private static void VerifyMainTaskCounting()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CodexTray-main-tasks-test-{Guid.NewGuid():N}");
        var locks = Directory.CreateDirectory(Path.Combine(home, "thread-writer-locks"));
        var sessions = Directory.CreateDirectory(Path.Combine(home, "sessions"));
        var fixtures = new List<(string Lock, string Rollout, FileStream Writer)>();
        var appearancePath = Path.Combine(home, ".codex-global-state.json");
        string[] factoryRoots = [@"C:\Projects\Factory"];
        try
        {
            var main = AddTask("vscode", idOverride: "ffffffff-ffff-ffff-ffff-ffffffffffff");
            AddTask(new { subagent = new { other = "guardian" } }, parent: main.Id);
            var worker = AddTask(new { subagent = new { thread_spawn = new { parent_thread_id = main.Id, depth = 1 } } });
            // Forked worker logs include the parent's history after their own metadata.
            File.AppendAllText(worker.Rollout, File.ReadAllText(main.Rollout));
            AddTask("vscode", parent: main.Id);
            var second = AddTask("cli", acquireLock: false, idOverride: "00000000-0000-0000-0000-000000000001");
            var fork = AddTask("vscode", forkedFrom: main.Id, acquireLock: false);
            var factoryWorker = AddTask("vscode", acquireLock: false);
            File.AppendAllText(factoryWorker.Rollout, JsonSerializer.Serialize(new
            {
                type = "response_item",
                payload = new
                {
                    type = "message",
                    role = "user",
                    content = new[] { new { type = "input_text", text = "Pipeline dispatch id: 11111111-2222-3333-4444-555555555555\nIssue LOCAL-1: Test\nRole: tester; round: 1/3.\nFactory instructions: C:/Projects/Factory/AGENTS.md\n" } }
                }
            }) + "\n");
            var monitor = new AgentActivityMonitor(home);
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "one main task with guardian and workers counts once");
            File.AppendAllText(main.Rollout, QuestionEvent("main-question") + "\n");
            File.AppendAllText(worker.Rollout, QuestionEvent("subagent-question") + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1 && monitor.NewQuestionCount == 1,
                "only the main task's new question is reported, not a subagent's");
            WriteAppearance("green");
            var colored = monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single();
            Assert(colored.ThreadId == main.Id && colored.ProjectColor == ProjectColorMap.ParseColor("green"), "color belongs to the main task, not its helpers");
            Assert(colored.ProjectMarker == new ProjectMarker("plane"), "project marker belongs to the same main task");
            File.AppendAllText(worker.Rollout, File.ReadAllText(main.Rollout));
            Assert(monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single().ThreadId == main.Id,
                "incrementally copied parent metadata cannot promote a worker to a duplicate main task");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ProjectColor is null, "project metadata is optional when colors are off");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ProjectMarker is null, "project markers are not read when both options are off");
            var tooltipTask = monitor.ReadTasks(CancellationToken.None).Single();
            Assert(tooltipTask.ProjectName == "Factory project" && tooltipTask.StartedAt.HasValue,
                "task hover metadata is available with colors and badges off");
            WriteAppearance("red");
            Assert(monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single().ProjectColor == ProjectColorMap.ParseColor("red"),
                "project recoloring updates without restarting a main task");
            File.WriteAllText(appearancePath, "{");
            Assert(monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single().ProjectColor == ProjectColorMap.ParseColor("red"),
                "temporary partial state writes preserve the last valid project color");
            File.Delete(appearancePath);
            Assert(monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single().ProjectColor is null,
                "missing project state falls back without losing the main task");
            second.Writer.Lock(0, 1);
            Assert(monitor.ReadCount(CancellationToken.None) == 2, "two independent main tasks count twice");
            var ordered = monitor.ReadTasks(CancellationToken.None).Select(task => task.ThreadId).ToArray();
            Assert(ordered.SequenceEqual(new[] { main.Id, second.Id }), "new task appends even when its ID sorts before the existing task");
            fork.Writer.Lock(0, 1);
            Assert(monitor.ReadCount(CancellationToken.None) == 3, "manually forked main task counts separately");
            Assert(monitor.ReadTasks(CancellationToken.None).Select(task => task.ThreadId).SequenceEqual(new[] { main.Id, second.Id, fork.Id }),
                "additional work stays at the outer edge without moving existing icons");
            File.AppendAllText(main.Rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"active"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 2, "main completion clears its count even while helpers remain active");
            Assert(monitor.ReadTasks(CancellationToken.None).Select(task => task.ThreadId).SequenceEqual(new[] { second.Id, fork.Id }),
                "completion removes its icon without reordering other tasks");
            File.AppendAllText(main.Rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"resumed"}}""" + "\n");
            Assert(monitor.ReadTasks(CancellationToken.None).Select(task => task.ThreadId).SequenceEqual(new[] { second.Id, fork.Id, main.Id }),
                "a resumed task joins the outer edge as new work");
            File.AppendAllText(main.Rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"resumed"}}""" + "\n");
            second.Writer.Unlock(0, 1);
            fork.Writer.Unlock(0, 1);
            Assert(monitor.ReadCount(CancellationToken.None) == 0, "only running subagents cannot leave a visible main-task count");

            factoryWorker.Writer.Lock(0, 1);
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == factoryWorker.Id,
                "a factory worker without an active controller stays visible");
            File.AppendAllText(factoryWorker.Rollout, QuestionEvent("standalone-question") + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1 && monitor.NewQuestionCount == 1, "a standalone factory worker's question alerts");
            second.Writer.Lock(0, 1);
            Assert(monitor.ReadCount(CancellationToken.None) == 2, "an unrelated active main chat cannot hide a standalone factory worker");
            second.Writer.Unlock(0, 1);
            WriteAppearance("green");
            File.AppendAllText(main.Rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"controller"}}""" + "\n");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == main.Id,
                "a factory worker is hidden behind an active controller matched by project assignment even with colors off");
            File.AppendAllText(factoryWorker.Rollout, QuestionEvent("hidden-question") + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1 && monitor.NewQuestionCount == 0, "hidden factory workers do not produce duplicate question alerts");
            File.Delete(appearancePath);
            Assert(monitor.ReadCount(CancellationToken.None) == 2, "unavailable project matching keeps the worker visible");
            File.AppendAllText(main.Rollout, """{"type":"turn_context","payload":{"cwd":"C:\\Projects\\Factory"}}""" + "\n");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == main.Id,
                "a controller in the factory directory hides its worker without desktop metadata");
            File.AppendAllText(main.Rollout, """{"type":"turn_context","payload":{"cwd":"C:\\Projects\\FactoryElsewhere"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 2, "a directory with a shared prefix does not match the factory");
            WriteAppearance("green");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == main.Id, "desktop assignment matches a controller outside the factory directory");
            File.AppendAllText(main.Rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"controller"}}""" + "\n");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == factoryWorker.Id,
                "an idle loaded controller does not hide its worker");
            File.AppendAllText(main.Rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"controller-again"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "resuming the controller hides the worker again");
            main.Writer.Unlock(0, 1);
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == factoryWorker.Id,
                "a released controller lock reveals a still-running factory worker");
            main.Writer.Lock(0, 1);
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ThreadId == main.Id, "reacquiring the active controller hides its worker");
            File.AppendAllText(factoryWorker.Rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"active"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1 && monitor.StoppedLifetimes.Count == 0,
                "a hidden factory worker completion does not play a sound");
            main.Writer.Unlock(0, 1);
            monitor.ReadCount(CancellationToken.None);
            File.AppendAllText(factoryWorker.Rollout, """{"type":"event_msg","payload":{"type":"task_started","turn_id":"standalone"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "a new standalone factory turn counts");
            File.AppendAllText(factoryWorker.Rollout, """{"type":"event_msg","payload":{"type":"task_complete","turn_id":"standalone"}}""" + "\n");
            Assert(monitor.ReadCount(CancellationToken.None) == 0 && monitor.StoppedLifetimes.Count == 1,
                "a visible standalone worker completion still produces a sound candidate");
        }
        finally
        {
            foreach (var fixture in fixtures)
            {
                fixture.Writer.Dispose();
                File.Delete(fixture.Lock);
                File.Delete(fixture.Rollout);
            }
            Directory.Delete(locks.FullName);
            Directory.Delete(sessions.FullName);
            File.Delete(appearancePath);
            Directory.Delete(home);
        }

        (string Id, string Rollout, FileStream Writer) AddTask(object source, string? parent = null,
            string? forkedFrom = null, bool acquireLock = true, string? idOverride = null)
        {
            var id = idOverride ?? Guid.NewGuid().ToString();
            var lockPath = Path.Combine(locks.FullName, id + ".lock");
            var rollout = Path.Combine(sessions.FullName, $"rollout-2026-10-13T00-00-00-{id}.jsonl");
            var metadata = JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id, source, parent_thread_id = parent, forked_from_id = forkedFrom }
            });
            File.WriteAllText(rollout, metadata + "\n" +
                """{"type":"event_msg","payload":{"type":"task_started","turn_id":"active"}}""" + "\n");
            var writer = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            fixtures.Add((lockPath, rollout, writer));
            if (acquireLock)
            {
                writer.Lock(0, 1);
            }
            return (id, rollout, writer);
        }

        void WriteAppearance(string color)
        {
            var assignments = new Dictionary<string, object>
            {
                [Path.GetFileNameWithoutExtension(fixtures[0].Lock)] = new { projectId = "main-project", projectKind = "local" }
            };
            File.WriteAllText(appearancePath, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["project-appearances"] = new Dictionary<string, object> { ["main-project"] = new { color, marker = new { kind = "icon", icon = "plane" } } },
                ["local-projects"] = new Dictionary<string, object> { ["main-project"] = new { name = "Factory project", rootPaths = factoryRoots } },
                ["thread-project-assignments"] = assignments
            }));
        }
    }

    private static void VerifyProjectColors()
    {
        using var document = JsonDocument.Parse("""
            {
              "project-appearances": {
                "main-project": { "color": "green", "marker": { "kind": "icon", "icon": "plane" } },
                "nested-project": { "color": "yellow" },
                "other-project": { "color": "red" },
                "default-project": { "color": "black" }
              },
              "thread-project-assignments": {
                "assigned": { "projectId": "main-project" },
                "canonical": { "projectId": "canonical-project" },
                "default": { "projectId": "default-project" },
                "projectless": { "projectId": "main-project" }
              },
              "app-server-project-id-by-legacy-project-id-by-host": {
                "local:fixture": { "main-project": "canonical-project" }
              },
              "local-projects": {
                "main-project": { "name": "Main project", "rootPaths": ["C:\\Projects\\Main"] },
                "nested-project": { "rootPaths": ["C:\\Projects\\Main\\Nested"] },
                "other-project": { "rootPaths": ["C:\\Projects\\Other"] }
              },
              "thread-workspace-root-hints": { "worktree": "C:\\Projects\\Main" },
              "projectless-thread-ids": ["projectless"]
            }
            """);
        var map = ProjectColorMap.Parse(document.RootElement);
        var green = ProjectColorMap.ParseColor("green");
        Assert(map.Resolve("assigned", @"C:\Projects\Other") == green, "explicit project assignment wins over working directory");
        Assert(map.Resolve("canonical", null) == green, "migrated project IDs resolve legacy appearance colors");
        Assert(map.ResolveName("assigned", @"C:\Projects\Other") == "Main project", "tooltip name follows explicit project assignment");
        Assert(map.ResolveName("canonical", null) == "Main project", "tooltip names resolve migrated project IDs");
        Assert(map.ResolveName("worktree", @"C:\Worktrees\Detached") == "Main project", "worktree tooltip uses the project name");
        Assert(map.ResolveName("unassigned", @"C:\Projects\Main\Nested\src") == "Nested", "unnamed projects use their root folder name");
        Assert(map.ResolveName("projectless", @"C:\Projects\Main") is null, "projectless tooltip does not borrow a workspace project name");
        Assert(map.ResolveName("cli", @"C:\Work\Example") == "Example", "CLI hover falls back to the workspace folder name");
        Assert(map.ResolveMarker("canonical", null) == new ProjectMarker("plane"), "migrated project IDs resolve their marker");
        Assert(map.ResolveMarker("worktree", @"C:\Worktrees\Detached") == new ProjectMarker("plane"), "worktree marker follows the project assignment");
        Assert(map.ResolveMarker("default", null) == ProjectMarker.Folder, "known projects without marker metadata get a folder badge");
        Assert(map.ResolveMarker("projectless", @"C:\Projects\Main") is null && map.ResolveMarker("missing", null) is null,
            "unmatched and projectless tasks have no misleading project badge");
        using var emojiMarker = JsonDocument.Parse("""{"kind":"emoji","emoji":"🌱"}""");
        Assert(ProjectColorMap.ParseMarker(emojiMarker.RootElement) == new ProjectMarker("🌱", IsEmoji: true), "emoji project markers are retained");
        using var invalidMarker = JsonDocument.Parse("""{"kind":"emoji","emoji":"\n"}""");
        Assert(ProjectColorMap.ParseMarker(invalidMarker.RootElement) == ProjectMarker.Folder, "invalid markers use a safe folder fallback");
        Assert(map.Resolve("unassigned", @"c:\PROJECTS\main\src") == green, "project roots match Windows paths case-insensitively");
        Assert(map.Resolve("unassigned", @"C:\Projects\Main\Nested\src") == ProjectColorMap.ParseColor("yellow"), "longest matching project root wins");
        Assert(map.Resolve("unassigned", @"C:\Projects\Main-other") is null, "sibling directory cannot inherit another project's color");
        Assert(map.Resolve("worktree", @"C:\Worktrees\Detached") == green, "workspace root hints support worktree tasks");
        Assert(map.Resolve("projectless", @"C:\Projects\Main") is null, "explicitly projectless tasks keep the normal color");
        Assert(map.Resolve("default", @"C:\Projects\Main") is null, "default project appearance does not borrow another project's color");
        Assert(map.Resolve("missing", null) is null, "unmatched task uses fallback color");
        Assert(ProjectColorMap.ParseColor(" GREEN ") == Color.FromArgb(64, 201, 119), "named color matches desktop dark-sidebar palette");
        Assert(ProjectColorMap.ParseColor("#ff6764") == ProjectColorMap.ParseColor("red"), "hex project colors are supported");
        Assert(ProjectColorMap.ParseColor("unknown") is null && ProjectColorMap.ParseColor("#invalid") is null,
            "unsupported project colors use the normal icon");
        using var malformed = JsonDocument.Parse("""{"project-appearances":{"bad":null},"local-projects":{"bad":{"rootPaths":[42,null]}},"thread-project-assignments":{"bad":42}}""");
        Assert(ProjectColorMap.Parse(malformed.RootElement).Resolve("bad", null) is null, "unrecognized project metadata is ignored safely");
        using var ambiguous = JsonDocument.Parse("""{"local-projects":{"one":{"rootPaths":["C:\\Same"]},"two":{"rootPaths":["C:\\Same"]}},"project-appearances":{"one":{"color":"red"},"two":{"color":"green"}}}""");
        Assert(ProjectColorMap.Parse(ambiguous.RootElement).Resolve("unassigned", @"C:\Same\src") is null,
            "ambiguous project roots cannot assign an arbitrary project color");

        VerifyProjectColorRendering();
    }

    private static void VerifyProjectColorRendering()
    {
        Color?[] colors = [ProjectColorMap.ParseColor("green"), ProjectColorMap.ParseColor("yellow"), null];
        ProjectMarker?[] markers = [ProjectMarker.Folder, new("plane"), new("wrench")];
        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
            foreach (var style in Enum.GetValues<LoadingIconStyle>())
                foreach (var showMarkers in new[] { false, true })
                    foreach (var colorLoadingIcons in new[] { false, true })
                    {
                        var width = AgentIndicatorRenderer.WidthFor(3, AgentIndicatorMode.RepeatedIcons);
                        using var actual = new Bitmap(width, 42);
                        using var expected = new Bitmap(width, 42);
                        using (var graphics = Graphics.FromImage(actual))
                        {
                            AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, width, 42), 3, style, 0.25f,
                                AgentIndicatorMode.RepeatedIcons, side, synchronizeAnimations: true, projectColors: colors,
                                projectMarkers: showMarkers ? markers : null, colorLoadingIcons: colorLoadingIcons);
                        }
                        using (var graphics = Graphics.FromImage(expected))
                        {
                            for (var index = 0; index < 3; index++)
                            {
                                var state = graphics.Save();
                                var x = (AgentIndicatorRenderer.IconXFor(index, 3, side) - 8) * AgentIndicatorRenderer.Scale;
                                graphics.TranslateTransform(x, 0);
                                AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 32, 42), 1, style, 0.25f,
                                    mode: AgentIndicatorMode.RepeatedIcons, synchronizeAnimations: true, projectColors: [colors[index]],
                                    projectMarkers: showMarkers ? [markers[index]] : null, colorLoadingIcons: colorLoadingIcons);
                                graphics.Restore(state);
                            }
                        }
                        AssertSamePixels(expected, actual, "per-task colors follow their icons on either side in every style");
                    }

        using var neutral = new Bitmap(60, 42);
        using var withColors = new Bitmap(60, 42);
        using (var graphics = Graphics.FromImage(neutral))
        {
            AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 60, 42), 3, LoadingIconStyle.Robot, 0.25f);
        }
        using (var graphics = Graphics.FromImage(withColors))
        {
            AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 60, 42), 3, LoadingIconStyle.Robot, 0.25f,
                projectColors: colors, projectMarkers: markers);
        }
        AssertSamePixels(neutral, withColors, "icon and number mode always keeps its normal color");

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        Assert(!widget.UseProjectColors && !widget.ShowProjectIcons, "project colors and badges are independently opt-in");
        widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, AgentIndicatorPosition.Right,
            mode: AgentIndicatorMode.RepeatedIcons, useProjectColors: true);
        widget.SetWorkingTasks([new WorkingTask("main", null, colors[0])]);
        using var before = widget.RenderPreview(Color.Black);
        widget.SetWorkingTasks([new WorkingTask("main", null, colors[1])]);
        using var after = widget.RenderPreview(Color.Black);
        var changed = false;
        for (var x = 0; x < before.Width; x++)
            for (var y = 0; y < before.Height; y++)
            {
                changed |= before.GetPixel(x, y) != after.GetPixel(x, y);
            }
        Assert(changed, "same-count project recoloring repaints the widget");

        widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, AgentIndicatorPosition.Right,
            mode: AgentIndicatorMode.RepeatedIcons, showProjectIcons: true);
        widget.SetWorkingTasks([new WorkingTask("main", null, colors[0], ProjectMarker.Folder)]);
        using var folderBadge = widget.RenderPreview(Color.Black);
        widget.SetWorkingTasks([new WorkingTask("main", null, colors[0], new ProjectMarker("plane"))]);
        using var planeBadge = widget.RenderPreview(Color.Black);
        Assert(!SamePixels(folderBadge, planeBadge), "marker-only updates repaint with unchanged count and color");
        widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, AgentIndicatorPosition.Right,
            mode: AgentIndicatorMode.RepeatedIcons);
        using var badgesOff = widget.RenderPreview(Color.Black);
        widget.SetWorkingAgentCount(1);
        using var plainIcon = widget.RenderPreview(Color.Black);
        AssertSamePixels(badgesOff, plainIcon, "switching off both project options restores the original loading icon");

        var indicatorWidth = AgentIndicatorRenderer.WidthFor(1, AgentIndicatorMode.RepeatedIcons);
        using var badgeFrame = new Bitmap(indicatorWidth + 8, 42);
        using (var graphics = Graphics.FromImage(badgeFrame))
        {
            AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, indicatorWidth, 42), 1, LoadingIconStyle.Arc, 0.25f,
                mode: AgentIndicatorMode.RepeatedIcons, projectColors: [colors[0]], projectMarkers: [ProjectMarker.Folder], colorLoadingIcons: false);
        }
        var greenBadge = false;
        var blueLoader = false;
        for (var x = 0; x < badgeFrame.Width; x++)
            for (var y = 0; y < badgeFrame.Height; y++)
            {
                var pixel = badgeFrame.GetPixel(x, y);
                if (x >= 26 && y < 19) { greenBadge |= pixel.G > pixel.R * 2 && pixel.G > pixel.B; }
                if (x < 26 && y >= 14) { blueLoader |= pixel.B > pixel.G && pixel.G > pixel.R; }
                Assert(x < indicatorWidth || pixel.A == 0, "project badge fits within the existing indicator width");
            }
        Assert(greenBadge && blueLoader, "badge keeps the project color independently of the loading-icon color switch");

        var namedMarkers = "folder.currency-dollar.book.graduation-cap.edit.writing.function.terminal.music.popcorn.customize.palette.stethoscope.health.lotus.suitcase.bar-chart.kettlebell.dumbbell.logs.scale.desk-globe.plane.globe.wrench.paw.flask.brain.heart.plant.unknown"
            .Split('.').Select(value => new ProjectMarker(value));
        foreach (var marker in namedMarkers.Append(new ProjectMarker("🌱", IsEmoji: true)))
        {
            using var bitmap = new Bitmap(20, 20);
            using var graphics = Graphics.FromImage(bitmap);
            ProjectMarkerRenderer.Draw(graphics, new RectangleF(0, 0, 20, 20), marker, colors[0]!.Value);
            var tinted = false;
            for (var x = 0; x < bitmap.Width; x++)
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    tinted |= pixel.G > pixel.R * 2 && pixel.G > pixel.B;
                }
            Assert(tinted, $"{marker.Value} draws a visible project-colored marker");
        }

        // A glyph can legitimately touch its canvas corner, especially when a
        // Windows Server font substitutes an emoji. Use the known empty interior
        // of the built-in folder to detect an unwanted badge background.
        using var transparentMarker = new Bitmap(28, 28);
        using (var graphics = Graphics.FromImage(transparentMarker))
        {
            ProjectMarkerRenderer.Draw(graphics, new RectangleF(4, 4, 20, 20),
                new ProjectMarker("unknown"), colors[0]!.Value);
        }
        Assert(transparentMarker.GetPixel(14, 16).A == 0, "the folder's empty interior has no badge background");
        for (var x = 0; x < transparentMarker.Width; x++)
            for (var y = 0; y < transparentMarker.Height; y++)
            {
                if (x < 4 || x >= 24 || y < 4 || y >= 24)
                {
                    Assert(transparentMarker.GetPixel(x, y).A == 0, "project symbols leave the surrounding canvas transparent");
                }
            }

        static bool SamePixels(Bitmap first, Bitmap second)
        {
            for (var x = 0; x < first.Width; x++)
                for (var y = 0; y < first.Height; y++)
                {
                    if (first.GetPixel(x, y) != second.GetPixel(x, y)) { return false; }
                }
            return true;
        }

        static void AssertSamePixels(Bitmap expected, Bitmap actual, string message)
        {
            for (var x = 0; x < expected.Width; x++)
                for (var y = 0; y < expected.Height; y++)
                {
                    Assert(expected.GetPixel(x, y) == actual.GetPixel(x, y), message);
                }
        }
    }

    private static void VerifyDesktopProjectIcons()
    {
        const string hole = """<path fill-rule="evenodd" d="M1 1H15V15H1Z M5 5H11V11H5Z"/>""";
        var vector = ProjectVectorIcon.Parse(hole);
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            using var brush = new SolidBrush(Color.Lime);
            vector.Draw(graphics, brush);
        }
        Assert(bitmap.GetPixel(2, 2).A > 0 && bitmap.GetPixel(8, 8).A == 0,
            "desktop filled paths preserve transparent holes rather than drawing an outline approximation");
        using var relativeBitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(relativeBitmap))
        {
            using var brush = new SolidBrush(Color.Lime);
            ProjectVectorIcon.Parse("""<path fill-rule="evenodd" d="m1 1h14v14h-14z m4 4h6v6h-6z"/>""").Draw(graphics, brush);
        }
        for (var x = 0; x < 16; x++)
            for (var y = 0; y < 16; y++)
            {
                Assert(bitmap.GetPixel(x, y) == relativeBitmap.GetPixel(x, y), "absolute and relative vector coordinates render identically");
            }
        var quote = ((char)96).ToString();
        var initial = "folder:{legacy:folderVector},plane:{legacy:planeVector};" +
            Component("folderVector") + Component("planeVector");
        var shared = $"name:{quote}wrench-light-16{quote},body:{quote}{hole}{quote}";
        var icons = DesktopProjectIcons.Extract(initial, shared);
        Assert(icons.Count == 3 && icons.ContainsKey("folder") && icons.ContainsKey("plane") && icons.ContainsKey("wrench"),
            "desktop asset extraction resolves sidebar fallback, legacy, and 16px preset shapes without evaluating JavaScript");
        Assert(DesktopProjectIcons.Extract("", "").Count == 0, "unavailable desktop assets allow built-in fallback");
        var invalid = false;
        try { ProjectVectorIcon.Parse("""<path d="M1 1A2 2 0 0 0 4 4"/>"""); }
        catch (FormatException) { invalid = true; }
        Assert(invalid, "unsupported future SVG commands fail safely instead of drawing a misleading shape");

        var initialBytes = System.Text.Encoding.UTF8.GetBytes(initial);
        var sharedBytes = System.Text.Encoding.UTF8.GetBytes(shared);
        var entries = new Dictionary<string, object>
        {
            ["app-initial-fixture.js"] = new { size = initialBytes.Length, offset = "0" },
            ["app-shared-fixture.js"] = new { size = sharedBytes.Length, offset = initialBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(new { files = new { webview = new { files = new { assets = new { files = entries } } } } });
        var archivePath = Path.Combine(Path.GetTempPath(), "CodexTray-vectors-" + Guid.NewGuid() + ".asar");
        try
        {
            using (var writer = new BinaryWriter(File.Create(archivePath)))
            {
                writer.Write(4);
                writer.Write(json.Length + 8);
                writer.Write(json.Length + 4);
                writer.Write(json.Length);
                writer.Write(json);
                writer.Write(initialBytes);
                writer.Write(sharedBytes);
            }
            Assert(DesktopProjectIcons.LoadArchive(archivePath).Count == 3,
                "ASAR index offsets resolve the desktop assets from a synthetic archive");
            File.WriteAllBytes(archivePath, [4, 0, 0, 0, 255, 255, 255, 127]);
            invalid = false;
            try { DesktopProjectIcons.LoadArchive(archivePath); }
            catch (InvalidDataException) { invalid = true; }
            Assert(invalid, "invalid archive sizes are rejected before allocating asset buffers");
        }
        finally { File.Delete(archivePath); }

        string Component(string id) =>
            $"{id}=e=>({{viewBox:{quote}0 0 16 16{quote},children:jsx({quote}path{quote},{{d:{quote}M1 1H15V15H1Z{quote},fillRule:{quote}evenodd{quote}}})}});function next(){{}}";
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
        VerifyAgentModes();
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

    private static void VerifyAgentPresentationFrame()
    {
        using var menu = new ContextMenuStrip();
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.First();
        using var widget = new TaskbarWidgetForm(menu, screen, loadSettings: false);
        // Exercise a real layered window without putting a test widget on the taskbar.
        widget.Location = new Point(screen.Bounds.Left, screen.Bounds.Top - widget.Height - 100);
        widget.Show();

        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
            foreach (var mode in Enum.GetValues<AgentIndicatorMode>())
                foreach (var animate in new[] { false, true })
                {
                    widget.ApplyAgentSettings(true, animate, LoadingIconStyle.Robot, side, mode);
                    foreach (var count in new[] { 1, 2, 3, 2, 0 })
                    {
                        widget.SetWorkingAgentCount(count);
                        using var surface = widget.RenderSurface();
                        Assert(surface.Size == widget.ClientSize,
                            $"{side} {mode} animation={animate} count={count} renders a complete resized alpha frame");
                        // Uses the actual native presenter; failures throw. Exercise
                        // animation, shrinking/growing, and hide/show without a key.
                        widget.InvalidateAnimationFrame();
                        widget.PresentFrame();
                    }
                }
        widget.Hide();
        widget.SetWorkingAgentCount(2);
        widget.Show();
        widget.PresentFrame();
    }

    private static void VerifyFinishedTasks()
    {
        var start = DateTimeOffset.UnixEpoch;
        var first = new WorkingTask("first", null, ProjectName: "Example", StartedAt: start);
        var second = first with { ThreadId = "second" };
        var finished = first with { FinishedAt = start.AddSeconds(42) };
        var tracker = new FinishedTaskTracker();
        var missing = new Dictionary<string, HashSet<string>>();
        var unread = new Dictionary<string, HashSet<string>> { ["fixture/local:fixture"] = ["first"] };
        var read = new Dictionary<string, HashSet<string>> { ["fixture/local:fixture"] = [] };
        tracker.Update([first, second], [], missing);
        tracker.Update([second], [finished], missing);
        Assert(tracker.Tasks.SequenceEqual([finished, second]), "completion retains the icon's original position");
        Assert(finished.ToTooltip(start.AddHours(1)) == "Example · Finished · 42s", "finished tooltips freeze the turn runtime");
        tracker.Update([second], [], unread);
        tracker.Update([second], [], missing);
        Assert(tracker.Tasks.Count == 2, "missing read state cannot dismiss a finished icon");
        tracker.Update([second], [], read);
        Assert(tracker.Tasks.SequenceEqual([second]), "opening an unread session manually clears its finished icon");
        tracker.Update([first, second], [], unread);
        tracker.Update([second], [finished], unread);
        tracker.Dismiss("second");
        Assert(tracker.Tasks.Count == 2, "clicking an active task does not hide it");
        tracker.Dismiss("FIRST");
        tracker.Update([second], [], unread);
        Assert(tracker.Tasks.SequenceEqual([second]), "click dismissal is shared and cannot resurrect on the next poll");
        tracker.Update([], [finished, second with { FinishedAt = start.AddMinutes(1) }], unread);
        var resumed = first with { StartedAt = start.AddMinutes(2) };
        tracker.Update([resumed], [], unread);
        Assert(tracker.Tasks.Count == 2 && tracker.Tasks[^1] == resumed && tracker.Tasks[0].ThreadId == "second",
            "resuming replaces a finished icon with one active icon at the outer edge");
        tracker.Update([resumed], [finished], unread);
        Assert(tracker.Tasks.Count == 2 && tracker.Tasks[^1] == resumed,
            "a completion and restart in the same poll do not leave a duplicate");
        tracker.Update([resumed], [], read);
        Assert(tracker.Tasks.Any(task => task == resumed), "read-state changes never hide a running task");
        var rapidRestart = resumed with { StartedAt = start.AddMinutes(3) };
        tracker.Update([resumed, second], [], missing);
        tracker.Update([rapidRestart, second], [finished], missing);
        Assert(tracker.Tasks[^1] == rapidRestart && tracker.Tasks.Count(task => task.ThreadId == "first") == 1,
            "a restart between polls still joins the outer edge once");

        using var state = JsonDocument.Parse("""
            {"electron-thread-read-state-v1":{"version":1,"unreadByIdentity":{
              "fixture":{"local:fixture":["first"],"remote:fixture":["second"]}}}}
            """);
        var parsed = FinishedTaskTracker.ParseReadState(state.RootElement);
        Assert(parsed.Count == 1 && parsed.Single().Value.SetEquals(["first"]), "only local unread IDs are interpreted");
        using var malformed = JsonDocument.Parse("""{"electron-thread-read-state-v1":{"version":"1"}}""");
        Assert(FinishedTaskTracker.ParseReadState(malformed.RootElement).Count == 0, "unsupported read-state metadata is ignored");
        var fixtureHome = Path.Combine(Path.GetTempPath(), $"CodexTray-read-state-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureHome);
        var fixturePath = Path.Combine(fixtureHome, ".codex-global-state.json");
        try
        {
            File.WriteAllText(fixturePath, state.RootElement.GetRawText());
            var reader = new ProjectColorReader(fixtureHome);
            reader.Refresh();
            Assert(reader.ReadState.Single().Value.SetEquals(["first"]), "the shared desktop reader loads unread flags");
            File.WriteAllText(fixturePath, "{");
            reader.Refresh();
            Assert(reader.ReadState.Single().Value.SetEquals(["first"]), "partial desktop writes preserve unread state");
            File.WriteAllText(fixturePath, """{"electron-thread-read-state-v1":{"version":1,"unreadByIdentity":{"fixture":{"local:fixture":[]}}}}""");
            reader.Refresh();
            Assert(reader.ReadState.Single().Value.Count == 0, "a later valid write exposes the read transition");
        }
        finally
        {
            File.Delete(fixturePath);
            Directory.Delete(fixtureHome);
        }

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
        {
            widget.ApplyAgentSettings(true, true, LoadingIconStyle.Robot, side, AgentIndicatorMode.RepeatedIcons);
            widget.SetWorkingTasks([finished]);
            Assert(widget.AgentCounterWidth == AgentIndicatorRenderer.WidthFor(1, AgentIndicatorMode.RepeatedIcons),
                "finished-only repeated mode retains its slot");
            var point = new Point(widget.AgentCounterBounds.Left +
                (int)((AgentIndicatorRenderer.IconXFor(0, 1, side) + 8) * AgentIndicatorRenderer.Scale), widget.Height / 2);
            Assert(widget.WorkingThreadAt(point) == "first", "finished icons remain navigable on either side");
            widget.Visible = true;
            var timer = (System.Windows.Forms.Timer)typeof(TaskbarWidgetForm).GetField("_animationTimer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
            Assert(!timer.Enabled, "finished-only widgets stop their animation timer");
            widget.SetWorkingTasks([finished, second]);
            Assert(timer.Enabled, "mixed widgets animate their active task");
            widget.Visible = false;
            widget.SetWorkingTasks([finished]);
            widget.ApplyAgentSettings(true, true, LoadingIconStyle.Robot, side, AgentIndicatorMode.IconAndNumber);
            Assert(widget.AgentCounterWidth == 0, "finished tasks never inflate the aggregate count");
        }
        foreach (var style in Enum.GetValues<LoadingIconStyle>())
            foreach (var dpiScale in new[] { 1f, 1.5f, 2f, 3f })
            {
                using var firstFrame = Render(0.1f);
                using var nextFrame = Render(0.8f);
                var visible = false;
                for (var y = 0; y < firstFrame.Height; y++)
                    for (var x = 0; x < firstFrame.Width; x++)
                    {
                        var pixel = firstFrame.GetPixel(x, y);
                        visible |= pixel.A > 0;
                        Assert(pixel == nextFrame.GetPixel(x, y), "finished icons and badges stay static in every style and scale");
                        Assert(pixel.A <= 105, "finished icons and badges use uniform 40% opacity");
                    }
                Assert(visible, "finished icons remain visible");
                Assert(firstFrame.GetPixel(0, 0).A == 0, "finished slots have no rectangular background");

                Bitmap Render(float phase)
                {
                    var bitmap = new Bitmap((int)(60 * dpiScale), (int)(42 * dpiScale));
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.ScaleTransform(dpiScale, dpiScale);
                    AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 60, 42), 1, style, phase,
                        AgentIndicatorMode.RepeatedIcons, projectMarkers: [new ProjectMarker("code-brackets")], finishedTasks: [true]);
                    return bitmap;
                }
            }
        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
        {
            widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, side, AgentIndicatorMode.RepeatedIcons,
                useProjectColors: true, showProjectIcons: true);
            widget.SetWorkingTasks([finished]);
            using var surface = widget.RenderSurface();
            var facePoint = new Point(widget.AgentCounterBounds.Left +
                (int)Math.Round((AgentIndicatorRenderer.IconXFor(0, 1, side) + 8) * AgentIndicatorRenderer.Scale), 22);
            var fadedFace = surface.GetPixel(facePoint.X, facePoint.Y);
            Assert(fadedFace.A is >= 99 and <= 105, "the native surface preserves finished face alpha");
            widget.SetWorkingTasks([first]);
            using var active = widget.RenderSurface();
            var activeFace = active.GetPixel(facePoint.X, facePoint.Y);
            Assert(Math.Abs(fadedFace.R - activeFace.R) <= 3 && Math.Abs(fadedFace.G - activeFace.G) <= 3 &&
                Math.Abs(fadedFace.B - activeFace.B) <= 3,
                $"finished faces retain their original RGB without whitening ({side}: {activeFace} -> {fadedFace})");
            widget.SetWorkingTasks([finished]);
            foreach (var background in new[] { Color.FromArgb(31, 31, 31), Color.FromArgb(230, 220, 205) })
            {
                using var preview = widget.RenderPreview(background, scale: 1);
                var pixel = preview.GetPixel(facePoint.X, facePoint.Y);
                var alpha = fadedFace.A / 255d;
                Assert(Math.Abs(pixel.R - (fadedFace.R * alpha + background.R * (1 - alpha))) <= 2 &&
                    Math.Abs(pixel.G - (fadedFace.G * alpha + background.G * (1 - alpha))) <= 2 &&
                    Math.Abs(pixel.B - (fadedFace.B * alpha + background.B * (1 - alpha))) <= 2,
                    "finished faces blend all channels with the actual background");
            }
            Assert(widget.TransparencyKey.IsEmpty, "per-pixel alpha widgets never configure a color key");
            widget.PresentFrame();
        }
    }

    private static void VerifyWorkingTaskTooltip()
    {
        var start = DateTimeOffset.UnixEpoch;
        var task = new WorkingTask("main", null, ProjectName: "Example", StartedAt: start);
        foreach (var (seconds, expected) in new (double, string)[]
        {
            (-1, "0s"), (0, "0s"), (59.9, "59s"), (60, "1m 0s"),
            (135, "2m 15s"), (299, "4m 59s"), (300, "5m 0s"),
            (301, "5m"), (360, "6m"), (3661, "61m")
        })
        {
            Assert(task.ToTooltip(start.AddSeconds(seconds)) == $"Example · {expected}", "working-task elapsed time format");
        }
        Assert(new WorkingTask("main", null).ToTooltip(start) == "No project", "missing hover metadata has a clear fallback");

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        var tooltip = (ToolTip)typeof(TaskbarWidgetForm).GetField("_tooltip",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
        {
            widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, side, AgentIndicatorMode.RepeatedIcons);
            widget.SetWorkingTasks([task, task with { ThreadId = "second", ProjectName = "Other" }]);
            for (var index = 0; index < 2; index++)
            {
                var x = widget.AgentCounterBounds.Left + (int)((AgentIndicatorRenderer.IconXFor(index, 2, side) + 8) * AgentIndicatorRenderer.Scale);
                typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(widget, [new MouseEventArgs(MouseButtons.None, 0, x, widget.ClientSize.Height / 2, 0)]);
                Assert(tooltip.GetToolTip(widget)?.StartsWith(index == 0 ? "Example · " : "Other · ", StringComparison.Ordinal) == true,
                    "hover details follow the matching icon on either side with animation off");
            }
            widget.SetWorkingTasks([task, task with { ThreadId = "second", ProjectName = "Renamed", StartedAt = DateTimeOffset.UtcNow.AddSeconds(-20) }]);
            Assert(tooltip.GetToolTip(widget)?.StartsWith("Renamed · 20s", StringComparison.Ordinal) == true, "same-count name and runtime changes refresh a stationary hover");
            typeof(Control).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(widget, [EventArgs.Empty]);
            Assert(tooltip.GetToolTip(widget)?.Contains("Codex usage", StringComparison.Ordinal) == true, "leaving icons restores the usage tooltip");
        }
    }

    private static void VerifyWorkingTaskNavigation()
    {
        const string first = "11111111-2222-3333-4444-555555555555";
        const string second = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var start = CodexSessionLauncher.CreateStartInfo(first);
        Assert(start.FileName == $"codex://threads/{first}" && start.UseShellExecute,
            "task navigation uses the desktop protocol handler with the session ID");
        var escaped = new Uri(CodexSessionLauncher.CreateStartInfo("id/?prompt=unexpected#fragment").FileName);
        Assert(escaped.Query.Length == 0 && escaped.Fragment.Length == 0,
            "session IDs cannot inject deep-link parameters");

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        var opened = new List<string>();
        var details = 0;
        var placements = 0;
        widget.WorkingTaskRequested += opened.Add;
        widget.DetailsRequested += (_, _) => details++;
        widget.PlacementCustomized += (_, _) => placements++;
        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
        {
            widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, side, AgentIndicatorMode.RepeatedIcons);
            widget.SetWorkingTasks([new WorkingTask(first, null), new WorkingTask(second, null)]);
            foreach (var index in new[] { 0, 1 })
            {
                var point = IconPoint(index);
                var expected = index == 0 ? first : second;
                Assert(widget.WorkingThreadAt(point) == expected, "icon hit testing preserves task order on either side");
                var badge = new Point(point.X + (int)(13 * AgentIndicatorRenderer.Scale),
                    point.Y - (int)(10 * AgentIndicatorRenderer.Scale));
                Assert(widget.WorkingThreadAt(badge) == expected, "project badge clicks resolve to their task");
                Mouse("OnMouseDown", point);
                Mouse("OnMouseUp", point);
                Assert(opened[^1] == expected, "a left click requests the matching session");
            }

            opened.Clear();
            var firstPoint = IconPoint(0);
            Mouse("OnMouseDown", firstPoint);
            Mouse("OnMouseUp", IconPoint(1));
            Assert(opened.Count == 0, "releasing over another icon does not open either session");
            Mouse("OnMouseDown", firstPoint, MouseButtons.Right);
            Mouse("OnMouseUp", firstPoint, MouseButtons.Right);
            Assert(opened.Count == 0, "right clicks do not open a session");
            Mouse("OnMouseDoubleClick", firstPoint);
            Assert(details == 0, "double clicking a task icon does not show quota details");

            Mouse("OnMouseDown", firstPoint);
            widget.SetWorkingTasks([new WorkingTask(second, null), new WorkingTask(first, null)]);
            Mouse("OnMouseUp", firstPoint);
            Assert(opened.Count == 0, "a changing task association cannot open the wrong chat during a click");
            Assert(widget.WorkingThreadAt(firstPoint) == second,
                "same-count same-color task replacements refresh navigation identity");
            Mouse("OnMouseDown", firstPoint);
            Mouse("OnMouseUp", firstPoint);
            Assert(opened.Count == 1 && opened[0] == second, "the replacement session is selected on the next click");

            opened.Clear();
            Mouse("OnMouseDown", firstPoint);
            Mouse("OnMouseMove", new Point(firstPoint.X, firstPoint.Y + SystemInformation.DragSize.Height + 2));
            // Cancel capture before release so tests do not save taskbar placement.
            widget.Capture = false;
            Mouse("OnMouseUp", firstPoint);
            Assert(opened.Count == 0, "dragging and lost capture cancel session navigation");

            var batteryPoint = new Point(side == AgentIndicatorPosition.Left ? widget.AgentCounterWidth + 10 : 10, 10);
            Assert(widget.WorkingThreadAt(batteryPoint) is null, "batteries do not resolve to a session");
            Mouse("OnMouseDoubleClick", batteryPoint);
            Assert(details == 1, "battery double clicks still show details");
            details = 0;

            widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, side, AgentIndicatorMode.IconAndNumber);
            Assert(widget.WorkingThreadAt(new Point(widget.AgentCounterBounds.Left + 15, 21)) is null,
                "the aggregate icon and number do not select an arbitrary session");
            widget.ApplyAgentSettings(false, false, LoadingIconStyle.Robot, side, AgentIndicatorMode.RepeatedIcons);
            Assert(widget.WorkingThreadAt(firstPoint) is null, "hidden indicators cannot be clicked");
            widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, side, AgentIndicatorMode.RepeatedIcons);
            widget.SetWorkingAgentCount(2);
            Assert(widget.WorkingThreadAt(IconPoint(0)) is null, "count-only updates clear stale session IDs");
            widget.SetWorkingTasks([]);
            Assert(widget.WorkingThreadAt(firstPoint) is null, "idle indicators cannot be clicked");
        }
        Assert(placements == 0, "clicking task icons does not change taskbar placement");

        Point IconPoint(int index) => new(
            widget.AgentCounterBounds.Left + (int)((AgentIndicatorRenderer.IconXFor(index, 2, widget.AgentCounterSide) + 8) * AgentIndicatorRenderer.Scale),
            widget.ClientSize.Height / 2);

        void Mouse(string method, Point point, MouseButtons button = MouseButtons.Left) =>
            typeof(Control).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(widget, [new MouseEventArgs(button, 1, point.X, point.Y, 0)]);
    }

    private static void VerifyAgentModes()
    {
        Assert(AgentIndicatorRenderer.Scale == 1.3f, "all working-agent icons use the former large size");
        Assert(WidgetSettings.ParseEnum<AgentIndicatorMode>("invalid", AgentIndicatorMode.IconAndNumber) == AgentIndicatorMode.IconAndNumber,
            "existing installations keep icon and number");
        Assert(AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Right) == AgentIndicatorRenderer.IconXFor(0, 1, AgentIndicatorPosition.Right),
            "right-side additional icons grow right");
        Assert(AgentIndicatorRenderer.IconXFor(1, 2, AgentIndicatorPosition.Right) > AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Right),
            "right-side icons have positive spacing");
        Assert(Math.Abs((AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Left) * AgentIndicatorRenderer.Scale - AgentIndicatorRenderer.WidthFor(2, mode: AgentIndicatorMode.RepeatedIcons)) -
            (AgentIndicatorRenderer.IconXFor(0, 1, AgentIndicatorPosition.Left) * AgentIndicatorRenderer.Scale - AgentIndicatorRenderer.WidthFor(1, mode: AgentIndicatorMode.RepeatedIcons))) < 0.001f,
            "left-side nearest icon remains anchored to controls");
        Assert(AgentIndicatorRenderer.IconXFor(1, 2, AgentIndicatorPosition.Left) < AgentIndicatorRenderer.IconXFor(0, 2, AgentIndicatorPosition.Left),
            "left-side additional icons grow left");

        using var menu = new ContextMenuStrip();
        using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
        foreach (var mode in Enum.GetValues<AgentIndicatorMode>())
            foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
                foreach (var style in Enum.GetValues<LoadingIconStyle>())
                {
                    widget.SetWorkingAgentCount(0);
                    var idleWidth = widget.ClientSize.Width;
                    widget.ApplyAgentSettings(true, false, style, side, mode);
                    widget.SetWorkingAgentCount(2);
                    using var preview = widget.RenderPreview(Color.FromArgb(31, 31, 31));
                    Assert(widget.ClientSize.Width == idleWidth + AgentIndicatorRenderer.WidthFor(2, mode), "display mode reserves correct space");
                    widget.SetWorkingAgentCount(0);
                    Assert(widget.ClientSize.Width == idleWidth, "every display mode collapses when idle");
                }

        // A repeated two-icon render must contain exactly those two icon images,
        // including the gap, with no number painted into the remaining space.
        foreach (var side in Enum.GetValues<AgentIndicatorPosition>())
            foreach (var synchronized in new[] { false, true })
            {
                var width = AgentIndicatorRenderer.WidthFor(2, AgentIndicatorMode.RepeatedIcons);
                using var actual = new Bitmap(width, 42);
                using var expected = new Bitmap(width, 42);
                using (var graphics = Graphics.FromImage(actual))
                {
                    AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, width, 42), 2, LoadingIconStyle.Robot, 0.25f,
                        AgentIndicatorMode.RepeatedIcons, side, synchronized);
                }
                using (var graphics = Graphics.FromImage(expected))
                {
                    for (var index = 0; index < 2; index++)
                    {
                        // Draw a normal single icon into a clipped area excluding its count.
                        var state = graphics.Save();
                        var x = (AgentIndicatorRenderer.IconXFor(index, 2, side) - 6) * AgentIndicatorRenderer.Scale;
                        graphics.TranslateTransform(x, 0);
                        graphics.SetClip(new RectangleF(0, 0, 26 * AgentIndicatorRenderer.Scale, 42));
                        AgentIndicatorRenderer.Draw(graphics, new Rectangle(0, 0, 42, 42), 1,
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
