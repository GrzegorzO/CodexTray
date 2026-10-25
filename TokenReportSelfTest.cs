using System.Text.Json;

namespace CodexTray;

internal static class TokenReportSelfTest
{
    private static readonly string[] RootsA = [@"C:\synthetic\a"];
    private static readonly string[] RootsB = [@"C:\synthetic\b"];
    internal static void Run()
    {
        var home = Path.Combine(Path.GetTempPath(), "CodexTray-report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, "sessions"));
        try
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            var reader = new TokenUsageReportReader(home);
            var first = Guid.NewGuid().ToString();
            var second = Guid.NewGuid().ToString();
            var fork = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["local-projects"] = new Dictionary<string, object>
                {
                    ["a"] = new { name = "Same name", rootPaths = RootsA },
                    ["b"] = new { name = "Same name", rootPaths = RootsB }
                },
                ["project-appearances"] = new Dictionary<string, object> { ["a"] = new { color = "green", marker = new { kind = "icon", icon = "terminal" } } },
                ["thread-project-assignments"] = new Dictionary<string, object> { [first] = new { projectId = "a" }, [fork] = new { projectId = "a" }, [second] = new { projectId = "b" } }
            }));
            var path = Path.Combine(home, "sessions", "rollout-test-" + first + ".jsonl");
            File.WriteAllText(path, Meta(first, now.AddHours(-3)) + Usage(now.AddHours(-2), 100, 30, 20, 5, 4) +
                Usage(now.AddHours(-2).AddMinutes(1), 100, 30, 20, 5, 4) + Usage(now.AddHours(-1), 140, 50, 30, 8, 6) + "{\"token_count\":malformed}\n");
            // A fork includes its parent's baseline, but only new tokens belong to it.
            File.WriteAllText(Path.Combine(home, "sessions", "rollout-test-" + fork + ".jsonl"), Meta(fork, now.AddMinutes(-45)) +
                Meta(first, now.AddHours(-3)) + Usage(now.AddHours(-1), 140, 50, 30, 8, 6) + Usage(now.AddMinutes(-30), 160, 60, 40, 10, 7));
            File.WriteAllText(Path.Combine(home, "sessions", "rollout-test-" + second + ".jsonl"), Meta(second, now.AddHours(-3)) + Usage(now.AddHours(-1), 50, 10, 10, 1, 0));
            var report = Read();
            var updates = new List<ReportReadProgress>();
            reader.Read(ReportPreset.Custom, now.AddDays(-1), now, null, now, CancellationToken.None,
                new InlineProgress<ReportReadProgress>(updates.Add));
            Check(updates[0].Stage == ReportReadStage.Discovering && updates[^1].Stage == ReportReadStage.Grouping,
                "history progress includes discovery and aggregation stages");
            Check(updates.Any(update => update is { Stage: ReportReadStage.Reading, CompletedFiles: 3, TotalFiles: 3 } && update.Fraction == 1),
                "file progress reaches the actual discovered-file count, including cached files");
            Check(new ReportReadProgress(ReportReadStage.Reading, 1, 4, 50, 100).Fraction == .375, "large-file byte progress stays within the file's share");
            Check(report.Projects.Count == 2, "identical project names stay distinct");
            var a = report.Projects.Single(p => p.Id == "project:a");
            Check(a.Counts == new TokenCounts(160, 60, 7, 40, 10), "delta totals and inherited fork baseline");
            Check(a.Events == 3, "duplicate cumulative notifications excluded");
            Check(a.Color == ProjectColorMap.ParseColor("green") && a.Marker?.Value == "terminal", "project color and marker");
            Check(report.Notice.Contains("1 malformed usage records", StringComparison.Ordinal), "malformed usage is disclosed");
            Check(report.Projects.Sum(p => p.Counts.Total) == 260, "cache and reasoning subsets not double counted");
            File.AppendAllText(path, Usage(now.AddMinutes(-10), 150, 55, 35, 9, 7).TrimEnd('\n'));
            Check(Read().Projects.Sum(p => p.Counts.Total) == 260, "unfinished JSONL line excluded");
            File.AppendAllText(path, "\n");
            Check(Read().Projects.Sum(p => p.Counts.Total) == 275, "next click observes completed append");
            var narrow = reader.Read(ReportPreset.Custom, now.AddHours(-1), now.AddMinutes(-30), null, now, CancellationToken.None);
            Check(narrow.Projects.Sum(p => p.Counts.Total) == 110, "half-open range boundaries");
            Directory.CreateDirectory(Path.Combine(home, "archived_sessions"));
            File.Copy(path, Path.Combine(home, "archived_sessions", Path.GetFileName(path)));
            Check(Read().Projects.Sum(p => p.Counts.Total) == 275, "archived duplicate is counted once");

            var oldStart = now.AddDays(-3);
            var resetAt = now.AddDays(-1);
            ResetObservation[] resets = [new(oldStart, 10080, oldStart.AddDays(7)), new(resetAt, 10080, resetAt.AddDays(7))];
            var previous = TokenUsageReportReader.ResolvePeriod(resets, 10080, true, now);
            Check(previous?.Start == oldStart && previous.End == resetAt && previous.End - previous.Start == TimeSpan.FromDays(2), "early reset shortens previous weekly window to two days");
            Check(previous!.Note.Contains("approximate", StringComparison.Ordinal), "observed early reset uncertainty disclosed");
            var current = TokenUsageReportReader.ResolvePeriod(resets, 10080, false, now);
            Check(current?.Start == resetAt, "current window starts at observed early reset");
            Check(TokenUsageReportReader.ResolvePeriod(resets.Take(1), 10080, true, now) is null, "unknown previous window is not fabricated");
            ResetObservation[] five = [new(now.AddHours(-6), 300, now.AddHours(-2)), new(now, 300, now.AddHours(3))];
            Check(TokenUsageReportReader.ResolvePeriod(five, 300, true, now)?.End == now.AddHours(-2), "previous 5h ends at reset");
            Check(TokenUsageReportReader.ResolvePeriod([], 300, false, now) is null, "missing reset metadata");
            var weeklyEnd = now.AddDays(5);
            ResetObservation[] corrected = [new(now.AddDays(-2), 10080, weeklyEnd),
                new(now.AddHours(-2), 10080, weeklyEnd.AddSeconds(-1)),
                new(now, 10080, weeklyEnd.AddSeconds(1).ToOffset(TimeSpan.FromHours(1)))];
            var correctedWeek = TokenUsageReportReader.ResolvePeriod(corrected, 10080, false, now);
            Check(correctedWeek?.Start < now.AddHours(-5) && !correctedWeek.Note.Contains("approximate", StringComparison.Ordinal),
                "minor reset corrections and time-zone offsets do not create a new weekly window at generation time");
            Check(TokenUsageReportReader.ResolvePeriod(corrected, 10080, true, now) is null, "reset corrections do not manufacture previous windows");
            var correctedReport = reader.Read(ReportPreset.CurrentWeekly, now, now,
                new UsageSnapshot(null, new UsageWindow(20, 10080, weeklyEnd.AddSeconds(1)), null), now, CancellationToken.None);
            Check(correctedReport.Projects.Count == 2, "current weekly includes both active projects when its range contains the five-hour range");
            var fallback = reader.Read(ReportPreset.CurrentFiveHour, now, now, new UsageSnapshot(new UsageWindow(20, 300, now.AddHours(2)), null, null), now, CancellationToken.None);
            Check(fallback.Period?.Start == now.AddHours(-3), "live snapshot supplies current reset");
            foreach (var project in fallback.Projects)
            {
                Check(correctedReport.Projects.Any(weekly => weekly.Id == project.Id && weekly.Counts.Total >= project.Counts.Total),
                    "weekly coverage contains every project and token total in a contained five-hour range");
            }
            var child = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Combine(home, "sessions", "rollout-test-" + child + ".jsonl"), JsonSerializer.Serialize(new
            {
                timestamp = now.AddMinutes(-20),
                type = "session_meta",
                payload = new
                {
                    id = child,
                    cwd = @"C:\synthetic\worktree",
                    source = new { subagent = new { thread_spawn = new { parent_thread_id = first } } }
                }
            }) + "\n" + Usage(now.AddMinutes(-15), 5, 0, 2, 0, 0));
            Check(Read().Projects.Single(p => p.Id == "project:a").Counts.Total == 222, "subagent worktree follows parent project assignment");
            var resetSession = Guid.NewGuid().ToString();
            File.WriteAllText(Path.Combine(home, "sessions", "rollout-test-" + resetSession + ".jsonl"), Meta(resetSession, oldStart.AddDays(-1)) +
                Reset(oldStart, oldStart.AddDays(7)) + Reset(resetAt, resetAt.AddDays(7)));
            var parsedWindow = reader.Read(ReportPreset.PreviousWeekly, now, now, null, now, CancellationToken.None);
            Check(parsedWindow.Period?.Start == oldStart && parsedWindow.Period.End == resetAt, "reset-only local records establish shortened windows");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { reader.Read(ReportPreset.Custom, now.AddDays(-1), now, null, now, cancellation.Token); throw new InvalidOperationException("Cancellation ignored"); }
            catch (OperationCanceledException) { }

            TokenReport Read() => reader.Read(ReportPreset.Custom, now.AddDays(-1), now, null, now, CancellationToken.None);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    private static string Meta(string id, DateTimeOffset at) => JsonSerializer.Serialize(new
    { timestamp = at, type = "session_meta", payload = new { id, timestamp = at, cwd = @"C:\synthetic\worktree" } }) + "\n";

    private static string Usage(DateTimeOffset at, long input, long cached, long output, long reasoning, long writes) => JsonSerializer.Serialize(new
    {
        timestamp = at,
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            info = new
            {
                total_token_usage = new
                {
                    input_tokens = input,
                    cached_input_tokens = cached,
                    cache_write_input_tokens = writes,
                    output_tokens = output,
                    reasoning_output_tokens = reasoning,
                    total_tokens = input + output
                }
            }
        }
    }) + "\n";

    private static string Reset(DateTimeOffset at, DateTimeOffset reset) => JsonSerializer.Serialize(new
    {
        timestamp = at,
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            rate_limits = new
            {
                limit_id = "codex",
                secondary = new { window_minutes = 10080, resets_at = reset.ToUnixTimeSeconds() }
            }
        }
    }) + "\n";

    private static void Check(bool condition, string name)
    {
        if (!condition) { throw new InvalidOperationException("Token report self-test failed: " + name); }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
