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
    private long _nextDiscovery;

    public AgentActivityMonitor(string? home = null)
    {
        _home = home ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _projectColors = new ProjectColorReader(_home);
    }

    public int ReadCount(CancellationToken cancellationToken) => ReadTasks(cancellationToken).Count;

    internal IReadOnlyList<WorkingTask> ReadTasks(CancellationToken cancellationToken, bool includeProjectColors = false)
    {
        var lockDirectory = Path.Combine(_home, "thread-writer-locks");
        if (!Directory.Exists(lockDirectory))
        {
            _states.Clear();
            _workingOrder.Clear();
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

        foreach (var id in _states.Keys.Where(id => !liveThreads.Contains(id)).ToArray())
        {
            _states.Remove(id);
        }

        if (includeProjectColors)
        {
            _projectColors.Refresh();
        }
        var tasks = new List<WorkingTask>();
        foreach (var id in liveThreads.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_rollouts.TryGetValue(id, out var path))
            {
                continue;
            }

            if (!_states.TryGetValue(id, out var state))
            {
                state = new ActivityState(id);
                _states[id] = state;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                state.Read(stream, cancellationToken);
                if (state.IsWorking && state.IsMainTask)
                {
                    tasks.Add(new WorkingTask(id, state.WorkingDirectory,
                        includeProjectColors ? _projectColors.Resolve(id, state.WorkingDirectory) : null,
                        includeProjectColors ? _projectColors.ResolveMarker(id, state.WorkingDirectory) : null));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Deleted, archived, or temporarily inaccessible sessions are not counted.
                _states.Remove(id);
                _rollouts.Remove(id);
            }
        }

        var byId = tasks.ToDictionary(task => task.ThreadId, StringComparer.OrdinalIgnoreCase);
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

    internal sealed class ActivityState(string threadId)
    {
        private long _offset;
        private string? _turnId;
        public bool IsWorking { get; private set; }
        public bool IsMainTask { get; private set; }
        public string? WorkingDirectory { get; private set; }

        public void Read(Stream stream, CancellationToken cancellationToken)
        {
            if (stream.Length < _offset)
            {
                _offset = 0;
                _turnId = null;
                IsWorking = false;
                IsMainTask = false;
                WorkingDirectory = null;
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
        }

        internal void Apply(string line)
        {
            if (!line.Contains("\"session_meta\"", StringComparison.Ordinal) &&
                !line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
                !line.Contains("\"task_started\"", StringComparison.Ordinal) &&
                !line.Contains("\"task_complete\"", StringComparison.Ordinal) &&
                !line.Contains("\"turn_aborted\"", StringComparison.Ordinal))
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
                switch (eventType.GetString())
                {
                    case "task_started":
                        _turnId = turnId;
                        IsWorking = true;
                        break;
                    case "task_complete":
                    case "turn_aborted":
                        if (turnId is null || _turnId is null || turnId == _turnId)
                        {
                            IsWorking = false;
                        }
                        break;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed events without discarding the last valid state.
            }
        }
    }
}
