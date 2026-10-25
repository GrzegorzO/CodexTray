using System.Text.Json;

namespace CodexTray;

// Runtime status belongs to the app-server that owns a thread. Observe the local
// writer locks and lifecycle events instead of starting a second app-server.
internal sealed class AgentActivityMonitor
{
    private readonly string _home;
    private readonly ProjectColorReader _projectColors;
    private readonly Dictionary<string, string> _rollouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActivityState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _workingOrder = [];
    private readonly List<TimeSpan> _stoppedLifetimes = [];
    private long _nextDiscovery;
    private readonly DateTimeOffset _monitoringStartedAt = DateTimeOffset.UtcNow;

    internal IReadOnlyList<TimeSpan> StoppedLifetimes => _stoppedLifetimes;
    internal int NewQuestionCount { get; private set; }
    internal IReadOnlyList<WorkingTask> StoppedTasks { get; private set; } = [];
    internal IReadOnlyDictionary<string, HashSet<string>> ReadState => _projectColors.ReadState;

    public AgentActivityMonitor(string? home = null)
    {
        _home = home ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _projectColors = new ProjectColorReader(_home);
    }

    public int ReadCount(CancellationToken cancellationToken) => ReadTasks(cancellationToken).Count;

    internal IReadOnlyList<WorkingTask> ReadTasks(CancellationToken cancellationToken, bool includeProjectColors = false)
    {
        _stoppedLifetimes.Clear();
        StoppedTasks = [];
        NewQuestionCount = 0;
        var lockDirectory = Path.Combine(_home, "thread-writer-locks");
        if (!Directory.Exists(lockDirectory))
        {
            _states.Clear();
            _workingOrder.Clear();
            _projectColors.Refresh();
            return Array.Empty<WorkingTask>();
        }

        var liveThreads = Directory.EnumerateFiles(lockDirectory, "*.lock")
            .Where(path => Guid.TryParse(Path.GetFileNameWithoutExtension(path), out _) && IsWriterAlive(path))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Environment.TickCount64 >= _nextDiscovery && liveThreads.Any(id => !_rollouts.ContainsKey(id)))
        {
            _nextDiscovery = Environment.TickCount64 + 5000;
            var sessions = Path.Combine(_home, "sessions");
            if (Directory.Exists(sessions))
            {
                foreach (var path in Directory.EnumerateFiles(sessions, "rollout-*.jsonl", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length >= 36 && Guid.TryParse(name[^36..], out _))
                    {
                        _rollouts[name[^36..]] = path;
                    }
                }
            }
        }

        var observed = new List<ActivityState>();
        foreach (var id in liveThreads.Union(_states.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_rollouts.TryGetValue(id, out var path))
            {
                continue;
            }

            if (!_states.TryGetValue(id, out var state))
            {
                state = new ActivityState(id, _monitoringStartedAt);
                _states[id] = state;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                state.Read(stream, cancellationToken);
                if (!liveThreads.Contains(id))
                {
                    // Read final events before treating a released lock as an exit.
                    state.Stop(DateTimeOffset.UtcNow);
                    _states.Remove(id);
                }
                observed.Add(state);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Deleted, archived, or temporarily inaccessible sessions are not counted.
                _states.Remove(id);
                _rollouts.Remove(id);
            }
        }

        // Project names are needed for hover details even when icon colors and badges are off.
        _projectColors.Refresh();
        var controllers = observed.Where(state => state.IsWorking && state.IsMainTask && state.FactoryDirectory is null).ToArray();
        var tasks = new List<WorkingTask>();
        var stoppedTasks = new List<WorkingTask>();
        foreach (var state in observed)
        {
            var hidden = state.FactoryDirectory is { } factory && controllers.Any(controller =>
                _projectColors.IsInProject(controller.ThreadId, controller.WorkingDirectory, factory));
            if (!hidden && !state.IsHiddenFactoryWorker)
            {
                _stoppedLifetimes.AddRange(state.StoppedLifetimes);
                if (state.StoppedLifetimes.Count > 0 && state.LastStoppedStart is { } startedAt)
                {
                    stoppedTasks.Add(new WorkingTask(state.ThreadId, state.WorkingDirectory,
                        includeProjectColors ? _projectColors.Resolve(state.ThreadId, state.WorkingDirectory) : null,
                        includeProjectColors ? _projectColors.ResolveMarker(state.ThreadId, state.WorkingDirectory) : null,
                        _projectColors.ResolveName(state.ThreadId, state.WorkingDirectory), startedAt, state.LastStoppedAt));
                }
            }
            state.IsHiddenFactoryWorker = hidden;
            if (state.IsMainTask && !hidden)
            {
                NewQuestionCount += state.NewQuestionCount;
            }
            if (state.IsWorking && state.IsMainTask && !hidden)
            {
                tasks.Add(new WorkingTask(state.ThreadId, state.WorkingDirectory,
                    includeProjectColors ? _projectColors.Resolve(state.ThreadId, state.WorkingDirectory) : null,
                    includeProjectColors ? _projectColors.ResolveMarker(state.ThreadId, state.WorkingDirectory) : null,
                    _projectColors.ResolveName(state.ThreadId, state.WorkingDirectory), state.StartedAt));
            }
        }

        var byId = tasks.ToDictionary(task => task.ThreadId, StringComparer.OrdinalIgnoreCase);
        StoppedTasks = stoppedTasks;
        _workingOrder.RemoveAll(id => !byId.ContainsKey(id));
        var known = _workingOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            if (known.Add(task.ThreadId)) { _workingOrder.Add(task.ThreadId); }
        }
        return _workingOrder.Select(id => byId[id]).ToArray();
    }

    internal static bool IsWriterAlive(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Lock(0, 1);
            stream.Unlock(0, 1);
            return false;
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) == 33)
        {
            // ERROR_LOCK_VIOLATION: Codex still owns this thread. A crash releases it.
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal sealed class ActivityState(string threadId, DateTimeOffset? questionBaseline = null)
    {
        internal string ThreadId => threadId;
        internal DateTimeOffset? StartedAt => _startedAt;
        internal DateTimeOffset? LastStoppedStart { get; private set; }
        internal DateTimeOffset? LastStoppedAt { get; private set; }
        private long _offset;
        private string? _turnId;
        private DateTimeOffset? _startedAt;
        private bool _hasRead;
        private bool _isUserFork;
        private readonly HashSet<string> _questionCalls = new(StringComparer.Ordinal);
        private bool _canNotifyOnDiscovery = questionBaseline.HasValue;
        internal List<TimeSpan> StoppedLifetimes { get; } = [];
        internal int NewQuestionCount { get; private set; }
        public bool IsWorking { get; private set; }
        public bool IsMainTask { get; private set; }
        public string? WorkingDirectory { get; private set; }
        internal string? FactoryDirectory { get; private set; }
        internal bool IsHiddenFactoryWorker { get; set; }

        public void Read(Stream stream, CancellationToken cancellationToken)
        {
            StoppedLifetimes.Clear();
            NewQuestionCount = 0;
            if (stream.Length < _offset)
            {
                _offset = 0;
                _turnId = null;
                _startedAt = null;
                _hasRead = false;
                _isUserFork = false;
                _questionCalls.Clear();
                _canNotifyOnDiscovery = false;
                IsWorking = false;
                IsMainTask = false;
                WorkingDirectory = null;
                FactoryDirectory = null;
                IsHiddenFactoryWorker = false;
            }

            stream.Position = _offset;
            // Only commit complete lines: a writer may be halfway through an event.
            using var line = new MemoryStream();
            var buffer = new byte[32 * 1024];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segmentStart = 0;
                for (var index = 0; index < bytesRead; index++)
                {
                    if (buffer[index] != '\n')
                    {
                        continue;
                    }

                    line.Write(buffer.AsSpan(segmentStart, index - segmentStart));
                    Apply(System.Text.Encoding.UTF8.GetString(line.GetBuffer(), 0, checked((int)line.Length)).TrimStart('\ufeff'));
                    _offset += line.Length + 1;
                    line.SetLength(0);
                    line.Position = 0;
                    segmentStart = index + 1;
                }
                line.Write(buffer.AsSpan(segmentStart, bytesRead - segmentStart));
            }
            if (!_hasRead)
            {
                // Old history is silent; a newly discovered session may already
                // contain a question recorded after this monitor started.
                StoppedLifetimes.Clear();
                if (!_canNotifyOnDiscovery) { NewQuestionCount = 0; }
                _hasRead = true;
            }
            _canNotifyOnDiscovery = false;
        }

        internal void Stop(DateTimeOffset stoppedAt)
        {
            if (IsWorking && IsMainTask && _startedAt is { } startedAt)
            {
                StoppedLifetimes.Add(stoppedAt >= startedAt ? stoppedAt - startedAt : TimeSpan.Zero);
                LastStoppedStart = startedAt;
                LastStoppedAt = stoppedAt;
            }
            IsWorking = false;
            _startedAt = null;
        }

        internal void Apply(string line)
        {
            if (!line.Contains("\"session_meta\"", StringComparison.Ordinal) &&
                !line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
                !line.Contains("\"task_started\"", StringComparison.Ordinal) &&
                !line.Contains("\"task_complete\"", StringComparison.Ordinal) &&
                !line.Contains("\"turn_aborted\"", StringComparison.Ordinal) &&
                !line.Contains("Pipeline dispatch id: ", StringComparison.Ordinal) &&
                !line.Contains("request_user_input", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                if (type.GetString() == "response_item" &&
                    payload.TryGetProperty("type", out var callType) && callType.ValueKind == JsonValueKind.String && callType.GetString() == "function_call" &&
                    payload.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                    name.GetString() is "request_user_input" or "request_user_input_async" or "functions.request_user_input" or "functions.request_user_input_async" &&
                    payload.TryGetProperty("call_id", out var callId) && callId.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(callId.GetString()) &&
                    payload.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String)
                {
                    using var request = JsonDocument.Parse(arguments.GetString()!);
                    if (request.RootElement.ValueKind == JsonValueKind.Object &&
                        request.RootElement.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array &&
                        questions.EnumerateArray().Any(question => question.ValueKind == JsonValueKind.Object &&
                            (HasQuestionText(question, "title") || HasQuestionText(question, "question"))) &&
                        _questionCalls.Add(callId.GetString()!))
                    {
                        if (_hasRead || (_canNotifyOnDiscovery && questionBaseline is { } baseline &&
                            root.TryGetProperty("timestamp", out var questionTime) && questionTime.ValueKind == JsonValueKind.String &&
                            questionTime.TryGetDateTimeOffset(out var recordedAt) && recordedAt >= baseline))
                        {
                            NewQuestionCount++;
                        }
                    }
                    return;
                }

                if (!_isUserFork && type.GetString() == "response_item" &&
                    payload.TryGetProperty("type", out var itemType) && itemType.ValueKind == JsonValueKind.String && itemType.GetString() == "message" &&
                    payload.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String && role.GetString() == "user" &&
                    payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) &&
                            text.ValueKind == JsonValueKind.String && FactoryForDispatch(text.GetString()!) is { } factory)
                        {
                            FactoryDirectory = factory;
                        }
                    }
                    return;
                }

                if (type.GetString() == "session_meta")
                {
                    // Forks copy ancestor metadata after the session's own record.
                    // Only the writer-lock owner's metadata can classify this task.
                    if (!payload.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                        !string.Equals(id.GetString(), threadId, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    WorkingDirectory = payload.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String ? cwd.GetString() : null;
                    var hasParent = payload.TryGetProperty("parent_thread_id", out var parent) &&
                        parent.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(parent.GetString());
                    var isSubagent = payload.TryGetProperty("source", out var source) &&
                        ((source.ValueKind == JsonValueKind.Object && source.TryGetProperty("subagent", out _)) ||
                        (source.ValueKind == JsonValueKind.String && string.Equals(source.GetString(), "subagent", StringComparison.OrdinalIgnoreCase)));
                    // User-created forks use forked_from_id and remain main tasks.
                    // Parent links and subagent sources identify workers/guardians.
                    IsMainTask = !hasParent && !isSubagent;
                    _isUserFork = IsMainTask && payload.TryGetProperty("forked_from_id", out var fork) &&
                        fork.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(fork.GetString());
                    if (_isUserFork) { FactoryDirectory = null; }
                    return;
                }

                if (type.GetString() == "turn_context")
                {
                    if (payload.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
                    {
                        WorkingDirectory = cwd.GetString();
                    }
                    return;
                }

                if (type.GetString() != "event_msg" || !payload.TryGetProperty("type", out var eventType) ||
                    eventType.ValueKind != JsonValueKind.String)
                {
                    return;
                }

                var turnId = payload.TryGetProperty("turn_id", out var turn) && turn.ValueKind == JsonValueKind.String
                    ? turn.GetString() : null;
                var timestamp = root.TryGetProperty("timestamp", out var time) && time.ValueKind == JsonValueKind.String &&
                    time.TryGetDateTimeOffset(out var parsedTime) ? parsedTime : DateTimeOffset.UtcNow;
                switch (eventType.GetString())
                {
                    case "task_started":
                        if (!IsWorking || turnId != _turnId)
                        {
                            _startedAt = timestamp;
                        }
                        _turnId = turnId;
                        IsWorking = true;
                        break;
                    case "task_complete":
                    case "turn_aborted":
                        if (turnId is null || _turnId is null || turnId == _turnId)
                        {
                            Stop(timestamp);
                        }
                        break;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed events without discarding the last valid state.
            }
        }

        private static string? FactoryForDispatch(string text)
        {
            const string prefix = "Pipeline dispatch id: ";
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) { return null; }
            var lines = text.Split('\n');
            if (!Guid.TryParse(lines[0][prefix.Length..].Trim(), out _) ||
                !lines.Any(line => line.StartsWith("Role: ", StringComparison.Ordinal) && line.Contains("; round: ", StringComparison.Ordinal)))
            {
                return null;
            }
            const string instructions = "Factory instructions: ";
            foreach (var line in lines)
            {
                if (!line.StartsWith(instructions, StringComparison.Ordinal)) { continue; }
                var path = line[instructions.Length..].Trim().Replace('/', Path.DirectorySeparatorChar);
                if (!path.Contains('\0') && Path.IsPathFullyQualified(path) &&
                    string.Equals(Path.GetFileName(path), "AGENTS.md", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(path);
                }
            }
            return null;
        }

        private static bool HasQuestionText(JsonElement question, string property) =>
            question.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString());
    }
}
