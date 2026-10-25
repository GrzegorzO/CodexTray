using System.Globalization;
using System.Text.Json;

namespace CodexTray;

internal sealed record TokenCounts(long Input, long CachedInput, long CacheWrite, long Output, long Reasoning)
{
    internal long Total => Input + Output;
    internal static TokenCounts Zero { get; } = new(0, 0, 0, 0, 0);
    public static TokenCounts operator +(TokenCounts a, TokenCounts b) =>
        new(a.Input + b.Input, a.CachedInput + b.CachedInput, a.CacheWrite + b.CacheWrite, a.Output + b.Output, a.Reasoning + b.Reasoning);
    internal TokenCounts Delta(TokenCounts previous) => new(
        Math.Max(0, Input - previous.Input), Math.Max(0, CachedInput - previous.CachedInput),
        Math.Max(0, CacheWrite - previous.CacheWrite), Math.Max(0, Output - previous.Output), Math.Max(0, Reasoning - previous.Reasoning));
}

internal sealed record TokenEvent(DateTimeOffset At, TokenCounts Counts);
internal sealed record ResetObservation(DateTimeOffset At, int Minutes, DateTimeOffset Reset);
internal sealed record ReportPeriod(DateTimeOffset Start, DateTimeOffset End, string Note);
internal sealed record ProjectUsage(string Id, string Name, Color Color, ProjectMarker? Marker, TokenCounts Counts, int Events);
internal sealed record TokenReport(ReportPeriod? Period, IReadOnlyList<ProjectUsage> Projects, string Notice);
internal enum ReportPreset { CurrentFiveHour, PreviousFiveHour, CurrentWeekly, PreviousWeekly, Custom }
internal enum ReportReadStage { Discovering, Reading, Grouping }
internal sealed record ReportReadProgress(ReportReadStage Stage, int CompletedFiles, int TotalFiles, long FileBytesRead = 0, long FileBytesTotal = 0)
{
    internal double Fraction => TotalFiles == 0 ? 0 : Math.Clamp((CompletedFiles +
        (FileBytesTotal > 0 ? Math.Clamp(FileBytesRead / (double)FileBytesTotal, 0, 1) : 0)) / TotalFiles, 0, 1);
}

// Retains only usage/metadata in memory. Never writes to Codex files or retains messages.
internal sealed class TokenUsageReportReader
{
    private static readonly TimeSpan ResetCorrectionTolerance = TimeSpan.FromMinutes(2);
    private readonly string _home;
    private readonly Dictionary<string, CachedSession> _cache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record CachedSession(long Length, DateTime Modified, string Id, string? Cwd, string? ParentThread,
        IReadOnlyList<TokenEvent> Events, IReadOnlyList<ResetObservation> Resets, int Invalid);

    internal TokenUsageReportReader(string? home = null) => _home = home ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    internal TokenReport Read(ReportPreset preset, DateTimeOffset from, DateTimeOffset to,
        UsageSnapshot? snapshot, DateTimeOffset now, CancellationToken cancellationToken, IProgress<ReportReadProgress>? progress = null)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessions = new List<CachedSession>();
        var unavailable = 0;
        var lastProgress = Environment.TickCount64;
        Publish(ReportReadStage.Discovering, 0, 0, force: true);
        foreach (var folder in new[] { "sessions", "archived_sessions" })
        {
            var root = Path.Combine(_home, folder);
            if (!Directory.Exists(root)) { continue; }
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "rollout-*.jsonl", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    paths.Add(path);
                    Publish(ReportReadStage.Discovering, 0, paths.Count);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { unavailable++; }
        }
        var completed = 0;
        Publish(ReportReadStage.Reading, 0, paths.Count, force: true);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(path);
                if (!_cache.TryGetValue(path, out var session) || session.Length != file.Length || session.Modified != file.LastWriteTimeUtc)
                {
                    session = ParseFile(path, file.Length, file.LastWriteTimeUtc, cancellationToken,
                        progress is null ? null : (read, length) => Publish(ReportReadStage.Reading, completed, paths.Count, read, length));
                    _cache[path] = session;
                }
                sessions.Add(session);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { unavailable++; }
            completed++;
            Publish(ReportReadStage.Reading, completed, paths.Count);
        }
        Publish(ReportReadStage.Reading, completed, paths.Count, force: true);
        Publish(ReportReadStage.Grouping, completed, paths.Count, force: true);
        foreach (var path in _cache.Keys.Where(path => !paths.Contains(path)).ToArray()) { _cache.Remove(path); }
        // A moved/archive copy of a rollout is still one session.
        sessions = sessions.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.Events.Count).ThenByDescending(s => s.Modified).First()).ToList();
        var observations = sessions.SelectMany(s => s.Resets).ToList();
        AddLive(snapshot?.FiveHour, 300);
        AddLive(snapshot?.Weekly, 10080);
        var period = preset == ReportPreset.Custom ? new ReportPeriod(from, to, "Custom dates; end is exclusive.") :
            ResolvePeriod(observations, preset is ReportPreset.CurrentWeekly or ReportPreset.PreviousWeekly ? 10080 : 300,
                preset is ReportPreset.PreviousFiveHour or ReportPreset.PreviousWeekly, now);
        if (period is null)
        {
            return new TokenReport(null, [], "This window cannot be established from available reset history. Choose custom dates.");
        }
        var projects = new ProjectColorReader(_home);
        projects.Refresh();
        var byId = sessions.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, ProjectUsage>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var events = session.Events.Where(e => e.At >= period.Start && e.At < period.End && e.At <= now).ToArray();
            if (events.Length == 0) { continue; }
            var ownerId = session.Id;
            var ownerCwd = session.Cwd;
            var id = projects.ResolveIdentity(ownerId, ownerCwd);
            var ancestor = session.ParentThread;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { session.Id };
            while (id.StartsWith("directory:", StringComparison.Ordinal) && ancestor is not null && visited.Add(ancestor))
            {
                byId.TryGetValue(ancestor, out var parent);
                var parentId = projects.ResolveIdentity(ancestor, parent?.Cwd);
                if (parentId.StartsWith("project:", StringComparison.Ordinal))
                {
                    ownerId = ancestor;
                    ownerCwd = parent?.Cwd;
                    id = parentId;
                }
                ancestor = parent?.ParentThread;
            }
            var counts = events.Aggregate(TokenCounts.Zero, (sum, e) => sum + e.Counts);
            if (counts.Total == 0) { continue; }
            var usage = totals.GetValueOrDefault(id) ?? new ProjectUsage(id, projects.ResolveName(ownerId, ownerCwd) ?? "No project",
                projects.Resolve(ownerId, ownerCwd) ?? Color.FromArgb(45, 125, 245), projects.ResolveMarker(ownerId, ownerCwd), TokenCounts.Zero, 0);
            totals[id] = usage with { Counts = usage.Counts + counts, Events = usage.Events + events.Length };
        }
        var invalid = sessions.Sum(s => s.Invalid);
        return new TokenReport(period, totals.Values.OrderByDescending(p => p.Counts.Total).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray(),
            $"Local recorded tokens, including subagents; not quota percentages. {unavailable} file read failures; {invalid} malformed usage records. Missing/deleted/remote history is not included.");

        void AddLive(UsageWindow? window, int minutes)
        {
            if (window?.ResetsAt is { } reset && reset > now) { observations.Add(new ResetObservation(now, minutes, reset)); }
        }
        void Publish(ReportReadStage stage, int done, int total, long bytes = 0, long length = 0, bool force = false)
        {
            if (progress is null || (!force && Environment.TickCount64 - lastProgress < 100)) { return; }
            lastProgress = Environment.TickCount64;
            progress.Report(new ReportReadProgress(stage, done, total, bytes, length));
        }
    }

    internal static ReportPeriod? ResolvePeriod(IEnumerable<ResetObservation> observations, int minutes, bool previous, DateTimeOffset now)
    {
        ReportPeriod? completed = null;
        ReportPeriod? active = null;
        DateTimeOffset? identityReset = null;
        foreach (var observation in observations.Where(o => o.Minutes == minutes && o.At <= now && o.Reset > o.At)
                     .Distinct().OrderBy(o => o.At).ThenBy(o => o.Reset))
        {
            if (active is null)
            {
                active = new ReportPeriod(observation.Reset.AddMinutes(-minutes), observation.Reset, "Start inferred from reset time and nominal duration.");
                identityReset = observation.Reset;
            }
            else if ((observation.Reset - identityReset!.Value).Duration() <= ResetCorrectionTolerance)
            {
                // API/log reset estimates can differ by seconds. These corrections are
                // the same window, not an early reset at the time we read the report.
                // Compare with a stable identity so many small changes cannot drift forever.
                var nominalStart = observation.Reset.AddMinutes(-minutes);
                active = active with
                {
                    End = observation.Reset,
                    Start =
                    !active.Note.Contains("approximate", StringComparison.Ordinal) && nominalStart < active.Start ? nominalStart : active.Start
                };
            }
            else
            {
                // A reset that changes before the former expiry shortens the old window.
                // Its exact instant is not recorded; use the first observation and disclose that.
                var early = observation.At < active.End;
                var boundary = early ? observation.At : active.End;
                completed = active with { End = boundary, Note = early ? "Early reset boundary is the first observed change (approximate)." : active.Note };
                var start = early ? boundary : observation.Reset.AddMinutes(-minutes);
                active = new ReportPeriod(start, observation.Reset, early ? completed.Note : "Start inferred from reset time and nominal duration.");
                identityReset = observation.Reset;
            }
        }
        if (active is null) { return null; }
        if (active.End <= now) { return previous ? active : null; }
        return previous ? completed : active;
    }

    private static CachedSession ParseFile(string path, long length, DateTime modified, CancellationToken cancellationToken, Action<long, long>? progress = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var expectedId = name.Length >= 36 ? name[^36..] : name;
        string? cwd = null;
        string? parentThread = null;
        DateTimeOffset? created = null;
        TokenCounts? cumulative = null;
        var events = new List<TokenEvent>();
        var resets = new List<ResetObservation>();
        var invalid = 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // Unfinished final JSONL writes are ignored and revisited after the file changes.
        using var reader = new StreamReader(stream);
        var lineCount = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((++lineCount & 127) == 0) { progress?.Invoke(stream.Position, length); }
            if (reader.EndOfStream && !EndsWithNewline(path, stream.Length)) { break; }
            if (!line.Contains("\"session_meta\"", StringComparison.Ordinal) && !line.Contains("\"token_count\"", StringComparison.Ordinal)) { continue; }
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { invalid++; continue; }
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) { continue; }
                if (Text(root, "type") == "session_meta")
                {
                    if (string.Equals(Text(payload, "id"), expectedId, StringComparison.OrdinalIgnoreCase) && created is null)
                    {
                        cwd = Text(payload, "cwd");
                        created = Timestamp(payload, "timestamp") ?? Timestamp(root, "timestamp");
                        parentThread = Text(payload, "parent_thread_id");
                        if (parentThread is null && payload.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object &&
                            source.TryGetProperty("subagent", out var subagent) && subagent.ValueKind == JsonValueKind.Object &&
                            subagent.TryGetProperty("thread_spawn", out var spawn) && spawn.ValueKind == JsonValueKind.Object)
                        {
                            parentThread = Text(spawn, "parent_thread_id");
                        }
                    }
                    continue;
                }
                if (Text(root, "type") != "event_msg" || Text(payload, "type") != "token_count" || Timestamp(root, "timestamp") is not { } at) { continue; }
                var owned = created is not null && at >= created;
                if (owned && payload.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object &&
                    Text(limits, "limit_id") is null or "codex")
                {
                    foreach (var key in new[] { "primary", "secondary" })
                    {
                        if (limits.TryGetProperty(key, out var window) && window.ValueKind == JsonValueKind.Object &&
                            Number(window, "window_minutes") is 300 or 10080 && Number(window, "resets_at") is > 0 and < 253402300800)
                        {
                            resets.Add(new ResetObservation(at, (int)Number(window, "window_minutes"), DateTimeOffset.FromUnixTimeSeconds(Number(window, "resets_at"))));
                        }
                    }
                }
                if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object ||
                    !info.TryGetProperty("total_token_usage", out var usage) || usage.ValueKind != JsonValueKind.Object) { continue; }
                var next = Counts(usage);
                // Deltas suppress repeated rate-limit-only notifications. Copied fork history
                // establishes a baseline without billing the inherited conversation again.
                var delta = cumulative is null && info.TryGetProperty("last_token_usage", out var last) && last.ValueKind == JsonValueKind.Object
                    ? Counts(last) : cumulative is null || next.Total < cumulative.Total ? next : next.Delta(cumulative);
                cumulative = next;
                if (owned && delta.Total > 0) { events.Add(new TokenEvent(at, delta)); }
            }
            catch (JsonException) { invalid++; }
        }
        return new CachedSession(length, modified, expectedId, cwd, parentThread, events, resets, invalid);
    }

    private static bool EndsWithNewline(string path, long length)
    {
        if (length == 0) { return false; }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = length - 1;
        return stream.ReadByte() == '\n';
    }
    private static TokenCounts Counts(JsonElement value) => new(Number(value, "input_tokens"), Number(value, "cached_input_tokens"),
        Number(value, "cache_write_input_tokens"), Number(value, "output_tokens"), Number(value, "reasoning_output_tokens"));
    private static long Number(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number) ? Math.Clamp(number, 0, 1_000_000_000_000L) : 0;
    private static string? Text(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static DateTimeOffset? Timestamp(JsonElement value, string key) => DateTimeOffset.TryParse(Text(value, key), CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var timestamp) ? timestamp : null;
}
