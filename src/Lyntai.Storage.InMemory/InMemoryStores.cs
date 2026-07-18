using System.Collections.Concurrent;
using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Storage.InMemory;

/// <summary>In-memory <see cref="IKeyValueStore"/>.</summary>
public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly ConcurrentDictionary<string, string> _data = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        _data[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _data.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IConversationStore"/> (delete-thread cascades to its messages).</summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ChatThread> _threads = [];
    private readonly List<ChatMessage> _messages = [];
    private long _nextId = 1;

    public Task<ChatThread> CreateThreadAsync(string id, string? title = null, CancellationToken ct = default)
    {
        var thread = new ChatThread(id, title, DateTimeOffset.UtcNow);
        lock (_lock) _threads[id] = thread;
        return Task.FromResult(thread);
    }

    public Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_threads.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ChatThread> result =
            [
                .. _threads.Values
                    .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id, StringComparer.Ordinal)
                    .Take(limit)
            ];
            return Task.FromResult(result);
        }
    }

    public Task<ChatMessage> AppendMessageAsync(string threadId, string role, string content, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var msg = new ChatMessage(_nextId++, threadId, role, content, DateTimeOffset.UtcNow);
            _messages.Add(msg);
            return Task.FromResult(msg);
        }
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ChatMessage> result = [.. _messages.Where(m => m.ThreadId == threadId).OrderBy(m => m.Id)];
            return Task.FromResult(result);
        }
    }

    public Task DeleteThreadAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _threads.Remove(id);
            _messages.RemoveAll(m => m.ThreadId == id); // cascade
        }
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IScoreStore"/> (saving a session again appends; GetAsync returns all
/// for the session in save order).</summary>
public sealed class InMemoryScoreStore : IScoreStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<ScoredResult>> _bySession = [];

    public Task SaveAsync(string sessionId, IReadOnlyList<ScoredResult> results, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_bySession.TryGetValue(sessionId, out var list)) _bySession[sessionId] = list = [];
            // upsert on (session, scorer): re-scoring REPLACES that scorer's row (matches the SQL stores)
            foreach (var r in results)
            {
                var i = list.FindIndex(e => e.ScorerId == r.ScorerId);
                if (i >= 0) list[i] = r; else list.Add(r);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredResult>> GetAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ScoredResult> result = _bySession.TryGetValue(sessionId, out var list) ? [.. list] : [];
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<ScorerAggregate>> AggregateAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ScorerAggregate> agg =
            [
                .. _bySession.Values.SelectMany(l => l)
                    .GroupBy(r => r.ScorerId, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new ScorerAggregate(g.Key, g.First().ScorerName, g.Average(r => r.Score), g.Count())),
            ];
            return Task.FromResult(agg);
        }
    }

    public Task<IReadOnlyList<ScoreExportRow>> ExportAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ScoreExportRow> rows =
            [
                .. _bySession
                    .SelectMany(kv => kv.Value.Select(r => new ScoreExportRow(kv.Key, r.ScorerId, r.Score)))
                    .OrderBy(r => r.SessionId, StringComparer.Ordinal).ThenBy(r => r.ScorerId, StringComparer.Ordinal),
            ];
            return Task.FromResult(rows);
        }
    }
}

/// <summary>In-memory <see cref="ITraceStore"/> (saving a session replaces its trace).</summary>
public sealed class InMemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<string, RunTrace> _bySession = new();

    public Task SaveAsync(RunTrace trace, CancellationToken ct = default)
    {
        _bySession[trace.SessionId] = trace;
        return Task.CompletedTask;
    }

    public Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult(_bySession.GetValueOrDefault(sessionId));
}

/// <summary>In-memory <see cref="IPromptVersionStore"/> (history + rollback, monotonic versions).</summary>
public sealed class InMemoryPromptVersionStore : IPromptVersionStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<PromptVersion>> _byName = [];

    public Task<PromptVersion?> GetActiveAsync(string name, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_byName.TryGetValue(name, out var list) ? list.FirstOrDefault(v => v.IsActive) : null);
    }

    public Task<PromptVersion> SaveAsync(string name, string template, string? author = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = [];
            for (var i = 0; i < list.Count; i++) list[i] = list[i] with { IsActive = false };
            var next = (list.Count == 0 ? 0 : list.Max(v => v.Version)) + 1;
            var version = new PromptVersion(name, next, template, author, DateTimeOffset.UtcNow, IsActive: true);
            list.Add(version);
            return Task.FromResult(version);
        }
    }

    public Task<IReadOnlyList<PromptVersion>> HistoryAsync(string name, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<PromptVersion> result = _byName.TryGetValue(name, out var list)
                ? [.. list.OrderByDescending(v => v.Version)]
                : [];
            return Task.FromResult(result);
        }
    }

    public Task<PromptVersion?> RollbackAsync(string name, int version, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_byName.TryGetValue(name, out var list)) return Task.FromResult<PromptVersion?>(null);
            var idx = list.FindIndex(v => v.Version == version);
            if (idx < 0) return Task.FromResult<PromptVersion?>(null);
            for (var i = 0; i < list.Count; i++) list[i] = list[i] with { IsActive = i == idx };
            return Task.FromResult<PromptVersion?>(list[idx]);
        }
    }
}
