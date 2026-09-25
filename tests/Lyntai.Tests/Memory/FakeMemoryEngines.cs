using Lyntai.Memory;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>In-process <see cref="IMemoryStore"/> with the contiguous-substring recall semantics the
/// InMemory backend has, so engine tests need no database.</summary>
internal sealed class FakeMemoryStore : IMemoryStore
{
    private readonly List<MemoryEntry> _entries = [];
    private long _next = 1;

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        if (!_entries.Any(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content))
            _entries.Add(new MemoryEntry(_next++, taskKey, scope, content, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IEnumerable<MemoryEntry> hits = _entries.Where(e => e.TaskKey == taskKey);
        if (scope is not null) hits = hits.Where(e => e.Scope == scope);
        if (!string.IsNullOrWhiteSpace(query))
            hits = hits.Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (limit is int n) hits = hits.Take(n);
        return Task.FromResult<IReadOnlyList<MemoryEntry>>([.. hits]);
    }

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        _entries.RemoveAll(e => e.TaskKey == taskKey && (scope is null || e.Scope == scope));
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
        CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>An engine that returns a fixed set of items, so composition and routing can be tested without
/// any store at all.</summary>
internal sealed class StaticEngine(
    string name,
    IReadOnlyList<MemoryItem> items,
    MemorySources ran = MemorySources.Lexical,
    MemoryGrades grades = MemoryGrades.Associative,
    bool? answered = null) : IMemoryEngine
{
    public string Name { get; } = name;

    public MemoryGrades Supported => grades;

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        throw new NotSupportedException($"'{Name}' is a read-only test engine.");

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count == 0
            ? MemoryRecall.Empty
            : new MemoryRecall(items, ran, answered));
    }
}

/// <summary>An engine that throws from every path — for the fail-open assertions. A BYO engine that
/// ignores the fail-open contract must not sink a caller's prompt.</summary>
internal sealed class FaultingEngine(string name) : IMemoryEngine
{
    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative;

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        throw new InvalidOperationException("boom");

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default) =>
        throw new InvalidOperationException("boom");
}

/// <summary>An engine whose OWN deadline fires — the sibling of <see cref="FaultingEngine"/>, and the case a
/// bare <c>catch (OperationCanceledException)</c> could not tell from a caller's cancel, because an
/// <c>HttpClient</c> timeout arrives as a <see cref="TaskCanceledException"/> and that IS one.
/// <para>Times out on recall, on expansion, or on either, so a walk can be driven to fault at either
/// step. The caller's token is never observed: that is what makes it a FAULT rather than a cancel.</para></summary>
internal sealed class TimingOutEngine(string name, bool onRecall = true, bool onExpand = true)
    : IMemoryEngine, IExpandableMemory
{
    /// <summary>The real message .NET produces, so a test asserting on it is asserting on the real shape.</summary>
    public const string Marker =
        "The request was canceled due to the configured HttpClient.Timeout of 300 seconds elapsing.";

    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative;

    private MemoryItem Hit(string id) =>
        new(new MemoryRef(Name, id), $"hit {id}", $"hit {id}", MemoryGrade.Associative, 1, 1, 1);

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        Task.FromResult(new MemoryWriteResult(new MemoryRef(Name, write.Content), MemorySources.Lexical));

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default) =>
        onRecall
            ? throw new TaskCanceledException(Marker)
            : Task.FromResult(new MemoryRecall([Hit("recalled")], MemorySources.Lexical));

    public Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, CancellationToken ct = default) =>
        onExpand
            ? throw new TaskCanceledException(Marker)
            : Task.FromResult(new MemoryRecall([Hit($"expanded {reference.Id}")], MemorySources.Graph));
}

/// <summary>An engine that records what it was asked to store, and declares which grades it accepts.</summary>
internal sealed class RecordingEngine(string name, MemoryGrades grades) : IMemoryEngine
{

    public List<MemoryWrite> Writes { get; } = [];

    /// <summary>What this member returns from a recall. Empty by default, so every existing test is
    /// unaffected; a blending test seeds it to give the composite something to cut.</summary>
    public List<MemoryItem> Items { get; } = [];

    public string Name { get; } = name;

    public MemoryGrades Supported => grades;

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        Writes.Add(write);
        return Task.FromResult(
            new MemoryWriteResult(new MemoryRef(Name, Writes.Count.ToString()), MemorySources.Lexical));
    }

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Items.Count == 0
            ? MemoryRecall.Empty
            : new MemoryRecall(Items, MemorySources.Lexical));
    }

    /// <summary>Seed one recall item. `relevance` is what the composite's cut orders on below the grade tier.</summary>
    public RecordingEngine Returning(string headline, MemoryGrade grade, double relevance)
    {
        Items.Add(new MemoryItem(new MemoryRef(Name, headline), headline, null, grade, relevance, 1, 0));
        return this;
    }
}

/// <summary>An engine that implements the optional expansion capability — the one a composite must not
/// hide behind itself.</summary>
internal sealed class ExpandableEngine(string name) : IMemoryEngine, IExpandableMemory
{
    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        Task.FromResult(new MemoryWriteResult(new MemoryRef(Name, write.Content), MemorySources.Graph));

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(MemoryRecall.Empty);
    }

    public Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, CancellationToken ct = default)
    {
        var expanded = new MemoryItem(reference, $"expanded {reference.Id}", $"expanded {reference.Id}",
            MemoryGrade.Associative, 1, 1, 1);
        return Task.FromResult(new MemoryRecall([expanded], MemorySources.Graph));
    }
}

/// <summary>An engine that can forget a scope but CANNOT prune a subset — the shape a vector store really
/// has, and the reason 3.0 split <see cref="IForgettableMemory"/> from <see cref="IPrunableMemory"/>. Under
/// one combined interface this engine had to claim both or neither.</summary>
internal sealed class ForgetOnlyEngine(string name) : IMemoryEngine, IForgettableMemory
{
    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative;

    public List<(string TaskKey, string? Scope)> Forgets { get; } = [];

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        Task.FromResult(new MemoryWriteResult(new MemoryRef(Name, write.Content), MemorySources.Semantic));

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(MemoryRecall.Empty);
    }

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        Forgets.Add((taskKey, scope));
        return Task.CompletedTask;
    }
}

/// <summary>An engine that implements the optional REMOVAL capability — the twin of
/// <see cref="ExpandableEngine"/>, and the capability a composite hid behind itself until 3.0. Records what
/// it was asked to remove so a forwarding test can assert the member was actually reached, not merely that the
/// call returned.</summary>
internal sealed class ForgettableEngine(string name, int pruneCount = 0)
    : IMemoryEngine, IForgettableMemory, IPrunableMemory
{
    public string Name { get; } = name;

    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    public List<(string TaskKey, string? Scope)> Prunes { get; } = [];

    public List<(string TaskKey, string? Scope)> Forgets { get; } = [];

    public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
        Task.FromResult(new MemoryWriteResult(new MemoryRef(Name, write.Content), MemorySources.Graph));

    public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(MemoryRecall.Empty);
    }

    public Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Prunes.Add((taskKey, scope));
        return Task.FromResult(pruneCount);
    }

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Forgets.Add((taskKey, scope));
        return Task.CompletedTask;
    }
}

/// <summary>The fault the <c>TimingOut*</c> doubles throw: the store's OWN deadline, spelled the way a
/// network-backed BYO store spells it (<see cref="TaskCanceledException"/>, which IS an
/// <see cref="OperationCanceledException"/> and says nothing about the caller) — or, when the double is handed
/// the caller's source, the CALLER cancelling mid-call, marked with <see cref="CallerMarker"/>.
/// <para>The marker is what lets a caller-cancel test fail: a PRE-cancelled token is rejected by every
/// engine's entry check before any fail-open catch runs, so a bare <c>ThrowsAnyAsync</c> on one passes
/// whatever those catches do (<c>.claude/knowledge/pitfalls.md</c>).</para></summary>
internal static class StoreFault
{
    public const string CallerMarker = "the store saw the caller's cancellation mid-call";

    public static Exception For(CancellationTokenSource? caller)
    {
        if (caller is null) return new TaskCanceledException(TimingOutEngine.Marker);
        caller.Cancel();
        return new OperationCanceledException(CallerMarker, caller.Token);
    }
}

/// <summary>An <see cref="IMemoryGraphStore"/> forwarding every member to a real in-process store, so a
/// double overrides only the members that differ; every forward calls <see cref="OnCall"/> first.
/// <para><b>It deliberately does not declare the two default-body members</b>, <c>LinkManyAsync</c> and
/// <c>WriteBackAsync</c>: the interface's own bodies then route through THIS double's <c>LinkAsync</c>,
/// <c>TouchAsync</c> and <c>RecordReviewsAsync</c>, so a hostile or counting override still sees the
/// write-back. A double that must intercept those two re-lists <see cref="IMemoryGraphStore"/> and declares
/// them itself.</para></summary>
internal class DelegatingGraphStore : IMemoryGraphStore
{
    /// <summary>The real store. Typed as the INTERFACE, because the in-process store takes the two
    /// default-body members from the interface rather than declaring them.</summary>
    protected IMemoryGraphStore Inner { get; } = new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore();

    /// <summary>Called with the member's name before every forward.</summary>
    protected virtual void OnCall(string member) { }

    public virtual Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    { OnCall(nameof(UpsertAsync)); return Inner.UpsertAsync(write, ct); }

    public virtual Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, int limit, CancellationToken ct = default)
    { OnCall(nameof(SeedAsync)); return Inner.SeedAsync(engine, taskKey, scope, query, limit, ct); }

    public virtual Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    { OnCall(nameof(NeighboursAsync)); return Inner.NeighboursAsync(engine, taskKey, ids, limit, ct); }

    public virtual Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    { OnCall(nameof(GetAsync)); return Inner.GetAsync(engine, id, ct); }

    public virtual Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
        CancellationToken ct = default)
    { OnCall(nameof(TouchAsync)); return Inner.TouchAsync(engine, touches, ct); }

    public virtual Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
        CancellationToken ct = default)
    { OnCall(nameof(LinkAsync)); return Inner.LinkAsync(engine, from, to, kind, weight, symmetric, ct); }

    public virtual Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    { OnCall(nameof(PruneAsync)); return Inner.PruneAsync(engine, taskKey, scope, maxAgeOverStability, olderThan, ct); }

    public virtual Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    { OnCall(nameof(DeleteAsync)); return Inner.DeleteAsync(engine, ids, ct); }

    public virtual Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default)
    { OnCall(nameof(ForgetAsync)); return Inner.ForgetAsync(engine, taskKey, scope, ct); }

    public virtual Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
        CancellationToken ct = default)
    { OnCall(nameof(RecordReviewsAsync)); return Inner.RecordReviewsAsync(engine, reviews, cap, ct); }

    public virtual Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default)
    { OnCall(nameof(ReviewsAsync)); return Inner.ReviewsAsync(engine, ct); }

    public virtual Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    { OnCall(nameof(RecordSubjectsAsync)); return Inner.RecordSubjectsAsync(engine, nodeId, subjects, ct); }

    public virtual Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default)
    { OnCall(nameof(NodesBySubjectAsync)); return Inner.NodesBySubjectAsync(engine, taskKey, scope, subject, limit, ct); }

    public virtual Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    { OnCall(nameof(KnownSubjectsAsync)); return Inner.KnownSubjectsAsync(engine, taskKey, scope, limit, ct); }
}

/// <summary>A graph store whose OWN deadline fires on the members named in <paramref name="timesOutOn"/>
/// (<see cref="StoreFault"/>) — or, given <see cref="Caller"/>, where the CALLER cancels mid-call.
/// <para>Per-member rather than whole-store, because the engine's fail-open promises are per-PATH — a
/// seed that times out must yield an empty recall, while a write-back that times out must still return the
/// hits the recall already found. Everything not named delegates, so a test can assert what still
/// worked.</para></summary>
internal sealed class TimingOutGraphStore(params string[] timesOutOn) : DelegatingGraphStore, IMemoryGraphStore
{
    private readonly HashSet<string> _members = new(timesOutOn, StringComparer.Ordinal);

    /// <summary>How many calls timed out — so a test can show the faulting path was reached, not assume it.</summary>
    public int TimedOut { get; private set; }

    /// <summary>The caller's source: when set, a named member cancels THE CALLER instead of timing out.</summary>
    public CancellationTokenSource? Caller { get; init; }

    protected override void OnCall(string member)
    {
        if (!_members.Contains(member)) return;
        TimedOut++;
        throw StoreFault.For(Caller);
    }

    // Declared, so they can be gated by name too: the interface's bodies would route through the members
    // above and never observe these two names.
    public Task LinkManyAsync(string engine, IReadOnlyList<GraphEdgeWrite> edges, CancellationToken ct = default)
    { OnCall(nameof(LinkManyAsync)); return Inner.LinkManyAsync(engine, edges, ct); }

    public Task WriteBackAsync(string engine, GraphWriteBack work, CancellationToken ct = default)
    { OnCall(nameof(WriteBackAsync)); return Inner.WriteBackAsync(engine, work, ct); }
}

/// <summary>An <see cref="IMemoryStore"/> whose own deadline fires on RECALL — the lexical engine's
/// fail-open case — or, given <see cref="Caller"/>, where the caller cancels mid-recall. Everything else
/// delegates, so a test can still write before reading.</summary>
internal sealed class TimingOutMemoryStore : IMemoryStore
{
    private readonly FakeMemoryStore _inner = new();

    /// <summary>The caller's source: when set, a recall cancels THE CALLER instead of timing out.</summary>
    public CancellationTokenSource? Caller { get; init; }

    public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null,
        CancellationToken ct = default) => _inner.RememberAsync(taskKey, scope, content, ttl, ct);

    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default) =>
        throw StoreFault.For(Caller);

    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
        _inner.ForgetAsync(taskKey, scope, ct);

    public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
        CancellationToken ct = default) => _inner.PruneAsync(taskKey, olderThan, ct);
}

/// <summary>An <see cref="ICuratedMemoryStore"/> whose own deadline fires on both READ paths — or, given
/// <see cref="Caller"/>, where the caller cancels mid-read.</summary>
internal sealed class TimingOutCuratedStore : ICuratedMemoryStore
{
    private readonly FakeCuratedStore _inner = new();

    /// <summary>The caller's source: when set, a read cancels THE CALLER instead of timing out.</summary>
    public CancellationTokenSource? Caller { get; init; }

    public Task<long> AddAsync(string kind, string content, bool enabled = true, string? taskKey = null,
        string? scope = null, bool pinned = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        _inner.AddAsync(kind, content, enabled, taskKey, scope, pinned, metadata, ct);

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        _inner.UpdateAsync(id, content, enabled, kind, taskKey, scope, metadata, ct);

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default) => _inner.RemoveAsync(id, ct);

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default) => _inner.GetAsync(id, ct);

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        _inner.ListAsync(kind, enabledOnly, taskKey, scope, limit, metadata, ct);

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null,
        string? taskKey = null, string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        throw StoreFault.For(Caller);

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey,
        IEnumerable<string> scopes, bool enabledOnly = true, CancellationToken ct = default) =>
        throw StoreFault.For(Caller);
}

/// <summary>An <see cref="ISemanticMemory"/> whose own deadline fires on recall — or, given
/// <see cref="Caller"/>, where the caller cancels mid-recall.</summary>
internal sealed class TimingOutSemanticMemory : ISemanticMemory
{
    private readonly FakeSemanticMemory _inner = new();

    /// <summary>The caller's source: when set, a recall cancels THE CALLER instead of timing out.</summary>
    public CancellationTokenSource? Caller { get; init; }

    public Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default) =>
        _inner.RememberAsync(taskKey, scope, content, ct);

    public Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string? scope, string query,
        int k = 10, double minScore = 0, CancellationToken ct = default) =>
        throw StoreFault.For(Caller);

    public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default) =>
        _inner.ForgetAsync(taskKey, scope, ct);
}

/// <summary>A graph store that refuses to LEARN but still remembers — for the read-only-database case,
/// where recall must degrade to "no learning" rather than to "no memory".</summary>
internal sealed class TouchHostileGraphStore : DelegatingGraphStore
{
    // every LEARNING write fails — subjects included, like every other write on a read-only database
    protected override void OnCall(string member)
    {
        if (member is nameof(TouchAsync) or nameof(LinkAsync) or nameof(RecordReviewsAsync)
            or nameof(RecordSubjectsAsync))
            throw new InvalidOperationException("attempt to write to a read-only database");
    }
}

/// <summary>A graph store that fails ONLY when logging a review — best-effort at a STRICTER grain than
/// <see cref="TouchHostileGraphStore"/>: touching, linking, subjects and everything else delegate, so a test
/// can assert that reinforcement still lands and recall still returns its hits although every log write
/// fails.</summary>
internal sealed class ReviewLogHostileGraphStore : DelegatingGraphStore
{
    protected override void OnCall(string member)
    {
        if (member is nameof(RecordReviewsAsync)) throw new InvalidOperationException("the review log is unavailable");
    }
}

/// <summary>A graph store that fails ONLY when recording SUBJECTS — the one that isolates the subject INDEX
/// from the annotator that feeds it.
/// <para>A failing annotator is already covered
/// (<c>MemorySubjectLinkingTests.A_failing_annotator_still_stores_the_fact</c>) and is a different link in
/// the chain: there the model never answers, so nothing reaches the store. Here the model answers perfectly
/// and the projection refuses the write.</para></summary>
internal sealed class SubjectHostileGraphStore : DelegatingGraphStore
{
    protected override void OnCall(string member)
    {
        if (member is nameof(RecordSubjectsAsync)) throw new InvalidOperationException("the subject index is unavailable");
    }
}

/// <summary>Faults on every subject-index READ (<see cref="IMemoryGraphStore.KnownSubjectsAsync"/>) and
/// delegates everything else, so a test can tell "the index read is broken" from "nothing matched" by
/// watching the rest of the store still work.</summary>
internal sealed class SubjectIndexHostileGraphStore : DelegatingGraphStore
{
    protected override void OnCall(string member)
    {
        if (member is nameof(KnownSubjectsAsync)) throw new InvalidOperationException("the subject index is unavailable");
    }
}

/// <summary>Records the <c>limit</c> a caller actually asks <see cref="IMemoryGraphStore.NodesBySubjectAsync"/>
/// for, and COUNTS both subject-index reads — so a test can assert the COST half of a guard (no call at
/// all) rather than only its output, which a downstream guard can satisfy on its own.</summary>
internal sealed class RecordingSubjectGraphStore : DelegatingGraphStore
{
    public int? RequestedNodesLimit { get; private set; }
    public int KnownSubjectsCalls { get; private set; }
    public int NodesBySubjectCalls { get; private set; }

    public override Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    {
        KnownSubjectsCalls++;
        return base.KnownSubjectsAsync(engine, taskKey, scope, limit, ct);
    }

    public override Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default)
    {
        NodesBySubjectCalls++;
        RequestedNodesLimit = limit;
        return base.NodesBySubjectAsync(engine, taskKey, scope, subject, limit, ct);
    }
}

/// <summary>In-process <see cref="ISemanticMemory"/> whose "similarity" is substring containment, so a
/// test needs no vector backend and spends no tokens.</summary>
internal sealed class FakeSemanticMemory : ISemanticMemory
{
    private readonly List<(string Task, string Scope, string Content)> _entries = [];

    public Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        if (!_entries.Any(e => e.Task == taskKey && e.Scope == scope && e.Content == content))
            _entries.Add((taskKey, scope, content));
        return Task.CompletedTask;
    }

    /// <summary>A null scope searches every scope of the task and stamps each hit with the scope it came
    /// from — the contract's cross-scope recall, so an engine test can reach it without a vector store.</summary>
    public Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string? scope, string query,
        int k = 5, double minScore = 0, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var hits = _entries
            .Where(e => e.Task == taskKey && (scope is null || e.Scope == scope) &&
                        e.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(k)
            .Select(e => new SemanticHit(e.Content, 0.9) { Scope = scope is null ? e.Scope : null })
            .ToList();
        return Task.FromResult<IReadOnlyList<SemanticHit>>(hits);
    }

    public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default)
    {
        _entries.RemoveAll(e => e.Task == taskKey && e.Scope == scope);
        return Task.CompletedTask;
    }
}

/// <summary>In-process <see cref="ICuratedMemoryStore"/> covering the read paths the curated engine uses:
/// dedup-aware add, substring search, and the composition read.</summary>
internal sealed class FakeCuratedStore : ICuratedMemoryStore
{
    private readonly List<CuratedMemory> _entries = [];
    private long _next = 1;

    public Task<long> AddAsync(string kind, string content, bool enabled = true, string? taskKey = null,
        string? scope = null, bool dedup = false, IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        if (dedup)
        {
            var existing = _entries.FirstOrDefault(e =>
                e.Kind == kind && e.Content == content && e.TaskKey == taskKey && e.Scope == scope);
            if (existing is not null) return Task.FromResult(existing.Id);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new CuratedMemory(_next++, kind, content, enabled, now, now, taskKey, scope, metadata);
        _entries.Add(entry);
        return Task.FromResult(entry.Id);
    }

    public Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<bool> RemoveAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_entries.RemoveAll(e => e.Id == id) > 0);

    public Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        IEnumerable<CuratedMemory> hits = _entries;
        if (kind is not null) hits = hits.Where(e => e.Kind == kind);
        if (taskKey is not null) hits = hits.Where(e => e.TaskKey == taskKey);
        if (scope is not null) hits = hits.Where(e => e.Scope == scope);
        if (enabledOnly) hits = hits.Where(e => e.Enabled);
        if (limit is int n) hits = hits.Take(n);
        return Task.FromResult<IReadOnlyList<CuratedMemory>>([.. hits]);
    }

    public Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null,
        string? taskKey = null, string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<CuratedMemory>>([]);

        IEnumerable<CuratedMemory> hits =
            _entries.Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (kind is not null) hits = hits.Where(e => e.Kind == kind);
        if (taskKey is not null) hits = hits.Where(e => e.TaskKey == taskKey);
        if (scope is not null) hits = hits.Where(e => e.Scope == scope);
        if (enabledOnly) hits = hits.Where(e => e.Enabled);
        if (limit is int n) hits = hits.Take(n);
        return Task.FromResult<IReadOnlyList<CuratedMemory>>([.. hits]);
    }

    public Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var wanted = scopes.ToList();
        IEnumerable<CuratedMemory> hits = _entries.Where(e => e.TaskKey is null || e.TaskKey == taskKey);
        if (wanted.Count > 0)
            hits = hits.Where(e => string.IsNullOrEmpty(e.Scope) || wanted.Contains(e.Scope));
        if (enabledOnly) hits = hits.Where(e => e.Enabled);
        return Task.FromResult<IReadOnlyList<CuratedMemory>>([.. hits]);
    }
}

/// <summary>Counts how a recall's whole write-back reaches the store: as ONE combined call, or as the three
/// separate ones it replaced. The same countable-claim reasoning as <see cref="LinkCountingGraphStore"/> one
/// level up — a round-trip count is checkable where the latency it buys sits inside the instrument's noise.
/// <para>It records the ORDER too, because the review log going LAST is contract (a broken log must cost
/// neither the touch nor the edges) and nothing else would catch a reordering.</para></summary>
internal sealed class WriteBackCountingGraphStore : DelegatingGraphStore, IMemoryGraphStore
{
    public int WriteBacks { get; private set; }
    public int DirectTouches { get; private set; }
    public int DirectBatchedLinks { get; private set; }
    public int DirectReviewWrites { get; private set; }
    public List<string> Order { get; } = [];

    // Declared rather than left to the interface's default body, which would call the three members below
    // and make the combined path indistinguishable from the three calls it replaced — so it reaches the
    // INNER store directly.
    public async Task WriteBackAsync(string engine, GraphWriteBack work, CancellationToken ct = default)
    {
        WriteBacks++;
        if (work.Touches.Count > 0)
        {
            Order.Add("touch");
            await Inner.TouchAsync(engine, work.Touches, ct);
        }

        if (work.Edges.Count > 0)
        {
            Order.Add("edges");
            await Inner.LinkManyAsync(engine, work.Edges, ct);
        }

        if (work.Reviews.Count > 0)
        {
            Order.Add("reviews");
            await Inner.RecordReviewsAsync(engine, work.Reviews, work.ReviewLogCap, ct);
        }
    }

    public Task LinkManyAsync(string engine, IReadOnlyList<GraphEdgeWrite> edges,
        CancellationToken ct = default)
    {
        DirectBatchedLinks++;
        return Inner.LinkManyAsync(engine, edges, ct);
    }

    protected override void OnCall(string member)
    {
        if (member is nameof(TouchAsync)) DirectTouches++;
        else if (member is nameof(RecordReviewsAsync)) DirectReviewWrites++;
    }
}

/// <summary>Counts how a recall's co-activation reaches the store: as ONE batched call or as N single ones.
/// <para>It exists because the change it guards is a ROUND-TRIP count, and the repository's own latency
/// instrument could not resolve that change above its run-to-run noise — <c>memory-scale</c>'s 10k p50 spans
/// 8.9–11.2ms across runs of identical code. A countable claim is checkable where a timing one is not.</para>
/// </summary>
internal sealed class LinkCountingGraphStore : DelegatingGraphStore, IMemoryGraphStore
{
    public int SingleLinks { get; private set; }
    public int BatchedLinks { get; private set; }
    public int EdgesWritten { get; private set; }

    protected override void OnCall(string member)
    {
        if (member is not nameof(LinkAsync)) return;
        SingleLinks++;
        EdgesWritten++;
    }

    // Declared rather than left to the interface's default body, which would loop LinkAsync and make the
    // two counters indistinguishable — the whole point is telling one call from ten.
    public async Task LinkManyAsync(string engine, IReadOnlyList<GraphEdgeWrite> edges,
        CancellationToken ct = default)
    {
        BatchedLinks++;
        EdgesWritten += edges.Count;
        foreach (var e in edges)
            await Inner.LinkAsync(engine, e.From, e.To, e.Kind, e.Weight, e.Symmetric, ct);
    }
}
