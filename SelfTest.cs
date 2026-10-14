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
            VerifyMainTaskCounting();
            VerifyWidgetTextDpi();
            VerifyProjectColors();
            VerifyDesktopProjectIcons();
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

    private static void VerifyMainTaskCounting()
    {
        var home = Path.Combine(Path.GetTempPath(), $"CodexTray-main-tasks-test-{Guid.NewGuid():N}");
        var locks = Directory.CreateDirectory(Path.Combine(home, "thread-writer-locks"));
        var sessions = Directory.CreateDirectory(Path.Combine(home, "sessions"));
        var fixtures = new List<(string Lock, string Rollout, FileStream Writer)>();
        var appearancePath = Path.Combine(home, ".codex-global-state.json");
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
            var monitor = new AgentActivityMonitor(home);
            Assert(monitor.ReadCount(CancellationToken.None) == 1, "one main task with guardian and workers counts once");
            WriteAppearance("green");
            var colored = monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single();
            Assert(colored.ThreadId == main.Id && colored.ProjectColor == ProjectColorMap.ParseColor("green"), "color belongs to the main task, not its helpers");
            Assert(colored.ProjectMarker == new ProjectMarker("plane"), "project marker belongs to the same main task");
            File.AppendAllText(worker.Rollout, File.ReadAllText(main.Rollout));
            Assert(monitor.ReadTasks(CancellationToken.None, includeProjectColors: true).Single().ThreadId == main.Id,
                "incrementally copied parent metadata cannot promote a worker to a duplicate main task");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ProjectColor is null, "project metadata is optional when colors are off");
            Assert(monitor.ReadTasks(CancellationToken.None).Single().ProjectMarker is null, "project markers are not read when both options are off");
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
                "main-project": { "rootPaths": ["C:\\Projects\\Main"] },
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
