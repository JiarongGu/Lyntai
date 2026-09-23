using Lyntai.Memory;
using Lyntai.Storage.InMemory;
using Microsoft.Extensions.Logging;

namespace Lyntai.Storage.FileSystem;

/// <summary>
/// <see cref="IMemoryGraphStore"/> as <c>graph/&lt;engine&gt;/&lt;task&gt;/&lt;scope&gt;/000001.md</c> — one memory per
/// file, its content as the body — with the engine's machine state in <c>graph/&lt;engine&gt;/state/</c>:
/// <c>journal.jsonl</c> (totals, decay state, edges, subjects) and <c>reviews.jsonl</c>, both append-only.
/// <para><b>A recall changes no memory file</b>: its write-back is two appends, where rewriting each recalled
/// memory's file costs about ten times as much (<c>docs/DECISIONS.md</c> D174). The semantics are the in-memory
/// store's — both run <see cref="MemoryGraphState"/> — and each change is written to disk before it is applied:
/// the journal first, then the memory file, so a crash between leaves unreachable state, never a memory with none.</para>
/// </summary>
internal sealed class FileSystemMemoryGraphStore(FileSystemRoot root, Func<DateTimeOffset>? clock = null,
    int compactionFloor = 4096) : IMemoryGraphStore
{
    private const string StateDirectory = "state";

    /// <summary>One engine's directory and its two journals, plus the journal lines of memories whose file
    /// exists but did not load — kept through compaction, so repairing the file restores what it learned.</summary>
    private sealed class EngineFiles(string directory, GraphJournal journal, GraphJournal reviews)
    {
        public string Directory { get; } = directory;
        public GraphJournal Journal { get; } = journal;
        public GraphJournal Reviews { get; } = reviews;
        public string? Engine { get; set; }
        public List<(string Line, long[] Ids)> Orphans { get; } = [];
    }

    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("graph");
    private readonly MemoryGraphState _graph = new(clock ?? (() => DateTimeOffset.UtcNow));
    private readonly Dictionary<string, EngineFiles> _byDirectory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EngineFiles> _byEngine = new(StringComparer.Ordinal);
    // every file that loaded as a memory's id — the LAST by name is the one written, as the key-value store does
    private readonly Dictionary<long, List<string>> _files = [];
    private readonly HashSet<long> _unreadable = []; // ids of memory files on disk that did not load
    private bool _loaded;

    // ---- loading --------------------------------------------------------------------------------------------

    private void Load()
    {
        if (_loaded) return;
        var totals = new Dictionary<string, GraphTotals>(StringComparer.Ordinal);
        // keyed by the journal as well: a memory reads its state from its own engine's journal only, so a stray
        // line for its id elsewhere (a failed write's) can never override it
        var states = new Dictionary<(EngineFiles Files, long Id), (NodeLine Line, string Raw)>();
        var edges = new Dictionary<GraphEdgeKey, (EdgeLine Line, string Raw, EngineFiles Files)>();
        var subjects = new Dictionary<(string, long), (SubjectsLine Line, string Raw, EngineFiles Files)>();
        var reviews = new Dictionary<long, MemoryReview>();
        var records = new List<(GraphNodeRecord Record, string File, EngineFiles Files)>();
        var onDisk = new HashSet<long>();
        long highWater = 0, reviewHighWater = 0;

        if (Directory.Exists(_directory))
            foreach (var dir in Directory.EnumerateDirectories(_directory).Order(StringComparer.Ordinal))
            {
                var files = Files(dir);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                files.Journal.Load((line, raw) =>
                {
                    switch (line)
                    {
                        case TotalsLine t: totals[t.Engine] = t.Totals; files.Engine ??= t.Engine; keys.Add("t" + t.Engine); break;
                        case NodeLine n: states[(files, n.Id)] = (n, raw); keys.Add("n" + n.Id); break;
                        case EdgeLine e: edges[e.Key] = (e, raw, files); keys.Add($"e{e.Key}"); break;
                        case SubjectsLine s: subjects[(s.Engine, s.NodeId)] = (s, raw, files); keys.Add($"s{s.Engine}\n{s.NodeId}"); break;
                    }
                });
                files.Journal.Baseline = keys.Count;
                files.Reviews.Load((line, _) =>
                {
                    if (line is ReviewLine r) reviews[r.Review.Id] = r.Review;
                });
                files.Engine ??= files.Journal.Engine ?? files.Reviews.Engine;
                highWater = Math.Max(highWater, files.Journal.HighWater);
                reviewHighWater = Math.Max(reviewHighWater, files.Reviews.HighWater);

                foreach (var scopeDir in Directory.EnumerateDirectories(dir)
                             .Where(d => Path.GetFileName(d) != StateDirectory)
                             .SelectMany(Directory.EnumerateDirectories)
                             .Order(StringComparer.Ordinal))
                {
                    onDisk.UnionWith(FileSystemRoot.NumberedIds(scopeDir));
                    records.AddRange(root.Load(scopeDir, (file, h, body) => (Parse(h, body), file, files)));
                }
            }

        var restore = new GraphChange();
        foreach (var (engine, t) in totals) restore.Totals.Add((engine, t));
        var live = new HashSet<long>();
        foreach (var group in records.GroupBy(r => r.Record.Id))
        {
            var (record, file, files) = group.OrderBy(r => r.File, StringComparer.Ordinal).Last();
            if (!states.TryGetValue((files, record.Id), out var state))
            {
                root.Logger.LogWarning("skipping {File}: its engine's journal holds no state for memory {Id}", file, record.Id);
                continue;
            }
            restore.Nodes.Add((null, new GraphRow(record, state.Line.State)));
            _files[record.Id] = [.. group.Select(r => r.File).Order(StringComparer.Ordinal)];
            files.Engine ??= record.Engine;
            live.Add(record.Id);
        }
        _unreadable.UnionWith(onDisk.Except(records.Select(r => r.Record.Id)));

        foreach (var ((files, id), (_, raw)) in states.Where(s => !live.Contains(s.Key.Id)))
            if (_unreadable.Contains(id)) files.Orphans.Add((raw, [id]));
        foreach (var (key, (line, raw, files)) in edges)
            if (live.Contains(key.From) && live.Contains(key.To)) restore.Edges.Add((key, line.Edge));
            else files.Orphans.Add((raw, [key.From, key.To]));
        foreach (var ((engine, id), (line, raw, files)) in subjects)
            if (live.Contains(id)) restore.Subjects.Add((engine, id, line.Subjects));
            else files.Orphans.Add((raw, [id]));
        restore.Reviews.AddRange(reviews.Values.OrderBy(r => r.Id));

        _graph.Apply(restore);
        _graph.Reserve(
            new[] { highWater }.Concat(states.Keys.Select(k => k.Id)).Concat(onDisk).Concat(records.Select(r => r.Record.Id))
                .Concat(edges.Keys.SelectMany(k => new[] { k.From, k.To })).Max(),
            reviews.Keys.Append(reviewHighWater).Max());
        foreach (var files in _byDirectory.Values)
        {
            files.Orphans.RemoveAll(o => !Keeps(o.Ids));
            if (files.Engine is { } engine) _byEngine.TryAdd(engine, files);
        }
        _loaded = true;
    }

    // An orphan is worth keeping only while a memory file it belongs to exists but cannot be read.
    private bool Keeps(long[] ids) =>
        ids.Any(_unreadable.Contains) && ids.All(id => _unreadable.Contains(id) || _graph.Row(id) is not null);

    private static GraphNodeRecord Parse(RecordFields h, string body) => new(
        h.Long("id") ?? throw new FormatException("'id' is required"), h.RequiredString("engine"),
        h.RequiredString("task"), h.RequiredString("scope"), h.RequiredString("headline"), body,
        h.RequiredString("grade") switch
        {
            "associative" => MemoryGrade.Associative,
            "authoritative" => MemoryGrade.Authoritative,
            var other => throw new FormatException($"'{other}' is not a grade"),
        },
        h.Time("created") ?? throw new FormatException("'created' is required"), h.Map("metadata"));

    private static string MemoryFile(GraphNodeRecord r) => RecordFile.Write(new RecordHeader()
        .Add("id", r.Id).Add("engine", r.Engine).Add("task", r.TaskKey).Add("scope", r.Scope)
        .Add("headline", r.Headline).Add("grade", r.Grade == MemoryGrade.Authoritative ? "authoritative" : "associative")
        .Add("created", r.CreatedAt).Add("metadata", r.Metadata), r.Content);

    // ---- the files an engine writes -------------------------------------------------------------------------

    private EngineFiles Files(string directory)
    {
        if (_byDirectory.TryGetValue(directory, out var files)) return files;
        var state = Path.Combine(directory, StateDirectory);
        files = new EngineFiles(directory,
            new GraphJournal(root, Path.Combine(state, "journal.jsonl"), compactionFloor),
            new GraphJournal(root, Path.Combine(state, "reviews.jsonl"), compactionFloor));
        return _byDirectory[directory] = files;
    }

    private EngineFiles FilesOf(string engine)
    {
        if (_byEngine.TryGetValue(engine, out var files)) return files;
        files = Files(Path.Combine(_directory, RecordName.For(engine)));
        files.Engine = engine;
        return _byEngine[engine] = files;
    }

    private void Commit(GraphChange change, string engine) => ApplyAndCompact(change, Journal(change, engine));

    // Each line goes to the journal of the engine that OWNS the record — a memory and its outgoing edges to the
    // memory's engine, totals and subjects to the calling engine — so compacting one engine re-emits exactly
    // what its journal holds.
    private List<EngineFiles> Journal(GraphChange change, string engine)
    {
        var batches = new Dictionary<EngineFiles, List<string>>();
        void Add(string owner, string line)
        {
            var files = FilesOf(owner);
            if (!batches.TryGetValue(files, out var lines)) batches[files] = lines = [];
            lines.Add(line);
        }

        foreach (var (e, totals) in change.Totals) Add(e, GraphLines.Totals(e, totals));
        foreach (var (_, row) in change.Nodes) Add(row.Record.Engine, GraphLines.Node(row.Id, row.State));
        foreach (var (key, edge) in change.Edges) Add(_graph.Row(key.From)?.Record.Engine ?? engine, GraphLines.Edge(key, edge));
        foreach (var (e, id, subjects) in change.Subjects) Add(e, GraphLines.Subjects(e, id, subjects));
        foreach (var (files, lines) in batches) files.Journal.Append(lines, files.Engine!, _graph.NextId - 1);
        return [.. batches.Keys];
    }

    private void ApplyAndCompact(GraphChange change, List<EngineFiles> journaled)
    {
        _graph.Apply(change);
        foreach (var files in journaled.Where(f => f.Journal.NeedsCompaction)) Compact(files);
    }

    private void Compact(EngineFiles files)
    {
        var engine = files.Engine!;
        var rows = _graph.Rows.Where(r => r.Record.Engine == engine).ToList();
        List<string> lines = [GraphLines.Totals(engine, _graph.Totals(engine))];
        lines.AddRange(rows.Select(r => GraphLines.Node(r.Id, r.State)));
        lines.AddRange(rows.SelectMany(r => _graph.EdgesFrom(r.Id)).Select(e => GraphLines.Edge(e.Key, e.Edge)));
        lines.AddRange(_graph.SubjectSets.Where(s => s.Engine == engine).Select(s => GraphLines.Subjects(s.Engine, s.NodeId, s.Subjects)));
        files.Orphans.RemoveAll(o => !Keeps(o.Ids));
        lines.AddRange(files.Orphans.Select(o => o.Line));
        files.Journal.Rewrite(lines, engine, _graph.NextId - 1);
    }

    // ---- writes ---------------------------------------------------------------------------------------------

    public Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            var change = _graph.PlanUpsert(write);
            var (before, after) = change.Nodes[0];
            var file = before is null
                ? Path.Combine(FilesOf(after.Record.Engine).Directory, RecordName.For(after.Record.TaskKey),
                    RecordName.For(after.Record.Scope), FileSystemRoot.IdFile(after.Id))
                : _files[after.Id][^1];
            List<EngineFiles> journaled;
            try
            {
                // the journal first: a crash before the memory file leaves unreachable state, never a memory with none
                journaled = Journal(change, write.Engine);
                if (before is null || !MemoryGraphState.SameRecord(before.Record, after.Record))
                    root.Write(file, MemoryFile(after.Record));
            }
            // the journal may already hold the new id, and a restart would reserve it — so this process must too
            catch when (before is null)
            {
                _graph.Reserve(after.Id, 0);
                throw;
            }
            if (before is null) _files[after.Id] = [file];
            ApplyAndCompact(change, journaled);
            return Task.FromResult(after.Id);
        }
    }

    public Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            Commit(_graph.PlanTouch(engine, touches), engine);
        }
        return Task.CompletedTask;
    }

    public Task LinkAsync(string engine, long from, long to, string? kind, double weight, bool symmetric,
        CancellationToken ct = default) =>
        LinkManyAsync(engine, [new GraphEdgeWrite(from, to, kind, weight, symmetric)], ct);

    public Task LinkManyAsync(string engine, IReadOnlyList<GraphEdgeWrite> edges, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            Commit(_graph.PlanLink(engine, edges), engine);
        }
        return Task.CompletedTask;
    }

    public Task WriteBackAsync(string engine, GraphWriteBack work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            // the touches and the edges are ONE change and one append
            if (work.Touches.Count > 0 || work.Edges.Count > 0)
                Commit(_graph.PlanLink(engine, work.Edges, _graph.PlanTouch(engine, work.Touches)), engine);
            // LAST by contract (D101): a broken log must cost neither of the above
            RecordReviews(engine, work.Reviews, work.ReviewLogCap);
        }
        return Task.CompletedTask;
    }

    public Task RecordReviewsAsync(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            RecordReviews(engine, reviews, cap);
        }
        return Task.CompletedTask;
    }

    private void RecordReviews(string engine, IReadOnlyCollection<MemoryReviewWrite> reviews, int cap)
    {
        var change = _graph.PlanReviews(engine, reviews, cap);
        if (change.Reviews.Count == 0) return;
        var files = FilesOf(engine);
        files.Reviews.Append([.. change.Reviews.Select(GraphLines.Review)], engine, _graph.NextReviewId - 1);
        _graph.Apply(change);
        if (change.TrimmedReviews.Count > 0)
            files.Reviews.Rewrite([.. _graph.ReviewsOf(engine).Select(GraphLines.Review)], engine, _graph.NextReviewId - 1);
    }

    public Task RecordSubjectsAsync(string engine, long nodeId, IReadOnlyCollection<string> subjects,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            Commit(_graph.PlanSubjects(engine, nodeId, subjects), engine);
        }
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(string engine, string taskKey, string? scope, double? maxAgeOverStability,
        TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(Remove(_graph.PruneIds(engine, taskKey, scope, maxAgeOverStability, olderThan)));
        }
    }

    public Task<int> DeleteAsync(string engine, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(Remove(_graph.DeleteIds(engine, ids)));
        }
    }

    public Task ForgetAsync(string engine, string taskKey, string? scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            Remove(_graph.ForgetIds(engine, taskKey, scope));
        }
        return Task.CompletedTask;
    }

    // The files first, then the view, one memory at a time: a delete that fails leaves that memory visible rather
    // than resurrected by the next start. No journal line is needed — its records become unreachable, and the
    // next compaction drops them. A hand-made copy goes too, or it would come back.
    private int Remove(IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
        {
            foreach (var file in _files.GetValueOrDefault(id) ?? []) FileSystemRoot.Delete(file);
            _files.Remove(id);
            _graph.Apply(_graph.PlanRemove([id]));
        }
        return ids.Count;
    }

    // ---- reads ----------------------------------------------------------------------------------------------

    public Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope, string? query,
        int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(_graph.Seed(engine, taskKey, scope, query, limit));
        }
    }

    public Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine, string taskKey,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(_graph.Neighbours(engine, taskKey, ids, limit));
        }
    }

    public Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(_graph.Get(engine, id));
        }
    }

    public Task<IReadOnlyList<MemoryReview>> ReviewsAsync(string engine, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(_graph.Reviews(engine));
        }
    }

    public Task<IReadOnlyList<long>> NodesBySubjectAsync(string engine, string taskKey, string? scope,
        string subject, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(_graph.NodesBySubject(engine, taskKey, scope, subject, limit));
        }
    }

    public Task<IReadOnlyList<string>> KnownSubjectsAsync(string engine, string taskKey, string? scope,
        int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            Load();
            return Task.FromResult(_graph.KnownSubjects(engine, taskKey, scope, limit));
        }
    }
}
