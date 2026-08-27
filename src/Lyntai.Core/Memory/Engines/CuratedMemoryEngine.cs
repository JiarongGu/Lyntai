using System.Globalization;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts the operator-curated <see cref="ICuratedMemoryStore"/> to <see cref="IMemoryEngine"/>.
/// AUTHORITATIVE by default — the catalog is deliberately managed, has no cap and no TTL, so its entries are
/// exact facts unless <see cref="Grade"/> says otherwise for a given one.
/// <para>Writes go in under <paramref name="kind"/> with <c>dedup: true</c>, so remembering the same fact
/// twice is idempotent rather than minting a duplicate catalog row.</para></summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="store">The catalog to draw on.</param>
/// <param name="kind">The catalog section this engine reads and writes; <b>null reads EVERY section</b>
/// (still bounded by <see cref="MemoryQuery.Limit"/>) and makes this engine read-only, because a write has
/// no section to go in and picking one would be the engine inventing the catalog's shape.</param>
/// <param name="logger">Optional; recall failures are logged rather than thrown.</param>
public sealed class CuratedMemoryEngine(
    string name,
    ICuratedMemoryStore store,
    string? kind = "memory",
    ILogger<CuratedMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<CuratedMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <summary>Derives an entry's <see cref="MemoryGrade"/> from the entry itself, so ONE catalog can mix
    /// provenance — the owner's typed facts against what an assistant inferred while working. Null grades
    /// every entry <see cref="MemoryGrade.Authoritative"/>, which is what this engine did before this
    /// property existed; <see cref="MemoryGrade.Inherit"/> from the delegate means the same thing.
    /// <para>A delegate rather than a reserved metadata key: which key carries provenance, and which of its
    /// values are exact, is the deployment's own convention — <see cref="CuratedMemory.Metadata"/> is
    /// app-owned and this library has no business naming a key inside it.</para>
    /// <para>It runs on the READ path only, and <see cref="Supported"/> deliberately does not widen: the
    /// store has no grade column, so the engine cannot mark a write, and claiming Associative would make a
    /// composite route associative writes HERE instead of to the engine that can decay them. Carry
    /// provenance in <see cref="MemoryWrite.Metadata"/> on an <see cref="MemoryGrade.Inherit"/> write and
    /// read it back here.</para>
    /// <para>A throwing delegate fails the recall the way a faulting store does — logged, empty — rather
    /// than guessing: an inferred fact shown as exact is the confusion the split exists to prevent, and an
    /// exact fact shown as inferred loses objective (1)'s protection, so there is no safe direction.</para>
    /// <para>An <c>init</c> property rather than a constructor parameter, for the reason
    /// <see cref="CompositeMemoryEngine.WriteRouting"/> gives: an appended optional parameter breaks a
    /// pre-compiled caller of the old signature, and a new member breaks nobody.</para></summary>
    public Func<CuratedMemory, MemoryGrade>? Grade { get; init; }

    /// <inheritdoc />
    /// <remarks>Authoritative, whatever <see cref="Grade"/> reports on the read path — see that property for
    /// why this does not widen.</remarks>
    public MemoryGrades Supported => MemoryGrades.Authoritative;


    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (kind is null)
            throw new NotSupportedException(
                $"Memory engine '{Name}' reads every section of the catalog (kind: null) and cannot write: " +
                "there is no section to put the entry in. Bind this engine to one kind, or add through " +
                "ICuratedMemoryStore directly.");
        if (write.Grade == MemoryGrade.Associative)
            throw new NotSupportedException(
                $"Memory engine '{Name}' is a curated catalog and stores every write as an exact fact. Route " +
                "associative material to an engine whose Supported includes Associative — or, to keep it in " +
                "this catalog, write it with Grade.Inherit carrying your provenance in Metadata and set the " +
                "Grade delegate to read it back.");

        var id = await store.AddAsync(kind, write.Content, enabled: true, taskKey: write.TaskKey,
            scope: write.Scope, dedup: true, metadata: write.Metadata, ct: ct).ConfigureAwait(false);
        return new MemoryRef(Name, id.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        try
        {
            // no query = "everything that applies to this task", which is the catalog's composition read.
            //
            // ForCompositionAsync takes NEITHER kind NOR limit — it is the whole-catalog read that
            // CuratedMemorySections renders per section — so both are applied HERE, matching what the
            // SearchAsync branch below already passes down. Without that, an engine bound to one section
            // returned every section of the catalog, unbounded, all graded Authoritative; a blend of two
            // curated engines over one catalog therefore returned each fact once per member, and the
            // duplicates consumed the authoritative reserve objective (1) exists to protect. Filtering in the
            // engine rather than widening the store contract keeps this a two-line fix and leaves
            // ForCompositionAsync doing the one job its other caller needs (found 2026-08-14).
            //
            // `kind: null` skips the section filter on both branches — the whole-catalog read the engine
            // otherwise narrows, which is what makes a catalog of several sections readable through ONE
            // engine instead of a composite of N.
            var entries = string.IsNullOrWhiteSpace(query.Query)
                ? (await store.ForCompositionAsync(query.TaskKey,
                            query.Scope is null ? [] : [query.Scope], enabledOnly: true, ct)
                        .ConfigureAwait(false))
                    .Where(e => kind is null || string.Equals(e.Kind, kind, StringComparison.Ordinal))
                    .Take(query.Limit is { } cap && cap > 0 ? cap : int.MaxValue)
                    .ToList()
                : await store.SearchAsync(query.Query, kind, query.TaskKey, query.Scope,
                    enabledOnly: true, query.Limit, ct: ct).ConfigureAwait(false);
            if (entries.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(entries.Count);
            foreach (var entry in entries)
            {
                // Inherit means "this engine's role", which is Authoritative — the same resolution
                // MemoryWrite.Grade gets, so a delegate returning it is not a third state.
                var resolved = Grade?.Invoke(entry) ?? MemoryGrade.Authoritative;
                if (resolved == MemoryGrade.Inherit) resolved = MemoryGrade.Authoritative;
                items.Add(new MemoryItem(
                    new MemoryRef(Name, entry.Id.ToString(CultureInfo.InvariantCulture)),
                    entry.Content, entry.Content, resolved, 1, 1, 0, entry.Metadata));
            }

            return new MemoryRecall(items, MemorySources.Curated);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "curated recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
