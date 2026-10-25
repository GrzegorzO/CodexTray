using System.Text.Json;

namespace CodexTray;

// UI-thread state shared by all widgets. Nothing is persisted or marked read in Codex.
internal sealed class FinishedTaskTracker
{
    private readonly List<WorkingTask> _tasks = [];
    private readonly Dictionary<string, HashSet<string>> _unreadScopes = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<WorkingTask> Tasks => _tasks.ToArray();

    internal void Update(IReadOnlyList<WorkingTask> running, IReadOnlyList<WorkingTask> stopped,
        IReadOnlyDictionary<string, HashSet<string>> readState)
    {
        var activeIds = running.Select(task => task.ThreadId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runningById = running.ToDictionary(task => task.ThreadId, StringComparer.OrdinalIgnoreCase);
        var stoppedById = stopped.Where(task => !activeIds.Contains(task.ThreadId))
            .ToDictionary(task => task.ThreadId, StringComparer.OrdinalIgnoreCase);
        for (var index = _tasks.Count - 1; index >= 0; index--)
        {
            var task = _tasks[index];
            if (activeIds.Contains(task.ThreadId))
            {
                if (task.FinishedAt.HasValue || task.StartedAt != runningById[task.ThreadId].StartedAt) { RemoveAt(index); }
            }
            else if (stoppedById.Remove(task.ThreadId, out var finished))
            {
                _tasks[index] = finished;
                _unreadScopes.Remove(task.ThreadId);
            }
            else if (!task.FinishedAt.HasValue) { RemoveAt(index); }
        }
        _tasks.AddRange(stoppedById.Values);
        foreach (var task in running)
        {
            var index = _tasks.FindIndex(existing => string.Equals(existing.ThreadId, task.ThreadId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) { _tasks[index] = task; }
            else { _tasks.Add(task); }
        }
        for (var index = _tasks.Count - 1; index >= 0; index--)
        {
            var task = _tasks[index];
            if (!task.FinishedAt.HasValue) { continue; }
            if (!_unreadScopes.TryGetValue(task.ThreadId, out var scopes))
            {
                scopes = new HashSet<string>(StringComparer.Ordinal);
                _unreadScopes[task.ThreadId] = scopes;
            }
            // Only an observed unread -> read transition is an acknowledgement.
            // Missing metadata, logout, and partial writes must not dismiss icons.
            if (scopes.Any(scope => readState.TryGetValue(scope, out var unread) && !unread.Contains(task.ThreadId)))
            {
                RemoveAt(index);
                continue;
            }
            foreach (var (scope, unread) in readState)
            {
                if (unread.Contains(task.ThreadId)) { scopes.Add(scope); }
            }
        }
    }

    internal void Dismiss(string threadId)
    {
        var index = _tasks.FindIndex(task => task.FinishedAt.HasValue &&
            string.Equals(task.ThreadId, threadId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) { RemoveAt(index); }
    }

    private void RemoveAt(int index)
    {
        _unreadScopes.Remove(_tasks[index].ThreadId);
        _tasks.RemoveAt(index);
    }

    internal static IReadOnlyDictionary<string, HashSet<string>> ParseReadState(JsonElement root)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("electron-thread-read-state-v1", out var state) || state.ValueKind != JsonValueKind.Object ||
            !state.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number != 1 ||
            !state.TryGetProperty("unreadByIdentity", out var identities) || identities.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        foreach (var identity in identities.EnumerateObject())
        {
            if (identity.Value.ValueKind != JsonValueKind.Object) { continue; }
            foreach (var host in identity.Value.EnumerateObject())
            {
                if (!host.Name.StartsWith("local:", StringComparison.Ordinal) || host.Value.ValueKind != JsonValueKind.Array ||
                    host.Value.EnumerateArray().Any(id => id.ValueKind != JsonValueKind.String)) { continue; }
                result[identity.Name + "/" + host.Name] = host.Value.EnumerateArray()
                    .Select(id => id.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        return result;
    }
}
