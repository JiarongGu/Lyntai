using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Seeding;

/// <summary>The handle channel: reads the subjects already recorded under this engine's task and scope, and
/// contributes the entries stored under whichever ones <see cref="MemorySeedRequest.Query"/> NAMES — the
/// only channel that reaches an entry through what it is ABOUT rather than through its own text.
///
/// <para><b>Ownership stays with <see cref="MemorySubject"/>.</b> Whether the query names a handle is
/// <see cref="MemorySubject.Matches"/>'s call, and the handle reaching the store is always
/// <see cref="MemorySubject.Normalize"/>'s form — this source calls both rather than restating either.</para>
///
/// <para><b>Best-effort, per the seam's own contract</b> (<see cref="IMemorySeedSource"/>): a faulting
/// <see cref="IMemoryGraphStore.KnownSubjectsAsync"/> or <see cref="IMemoryGraphStore.NodesBySubjectAsync"/>
/// logs a warning and returns empty rather than throwing; <see cref="OperationCanceledException"/> is never
/// swallowed. De-duplicated across subjects, so two handles naming one node yield it once.</para>
///
/// <para><b>UNORDERED, deliberately — and it says so through <see cref="GraphNode.Matched"/>.</b> Nodes
/// arrive from <see cref="IMemoryGraphStore.GetAsync"/> at <c>Matched null</c>, "nobody asked a relevance
/// question", so this channel earns no rank AT ANY CARDINALITY — where their flat
/// <see cref="GraphNode.Relevance"/> would silence it only from two nodes up, one resolved handle having no
/// tie to read and taking the BEST term of all. A handle match says an entry is ABOUT what was asked, never
/// how WELL it matched. Its candidates still compete on retrievability, hop and degree.</para>
///
/// <para><b>Two bounds on two different things</b>, the shape <see cref="SemanticSeedSource"/> also carries:
/// <see cref="SubjectSeedOptions.K"/> bounds what is FETCHED per matched subject, and
/// <see cref="MemorySeedRequest.Limit"/> caps what is RETURNED, once at the end. Coupling them would search
/// shallower per subject whenever a recall's limit is smaller.</para></summary>
public sealed class SubjectSeedSource(
    SubjectSeedOptions? options = null,
    ILogger<SubjectSeedSource>? logger = null) : IMemorySeedSource
{
    private readonly SubjectSeedOptions _options = options ?? new SubjectSeedOptions();
    private readonly ILogger _logger = logger ?? NullLogger<SubjectSeedSource>.Instance;

    /// <inheritdoc />
    public string Name => "subject";

    /// <inheritdoc />
    public MemorySeedKind Kind => MemorySeedKind.Subject;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct)
    {
        if (_options.K <= 0 || _options.Scan <= 0 || request.Limit <= 0 ||
            string.IsNullOrWhiteSpace(request.Query.Query))
            return [];

        IReadOnlyList<long> ids;
        try
        {
            var known = await request.Store.KnownSubjectsAsync(
                request.Engine, request.Query.TaskKey, request.Query.Scope, _options.Scan, ct)
                .ConfigureAwait(false);

            var found = new List<long>();
            var seen = new HashSet<long>();
            foreach (var subject in known)
            {
                if (!MemorySubject.Matches(request.Query.Query, subject)) continue;
                // K is the per-subject FETCH bound, never narrowed by request.Limit — see this type's own
                // remarks on why the two stay apart.
                var byThisSubject = await request.Store.NodesBySubjectAsync(request.Engine,
                    request.Query.TaskKey, request.Query.Scope, MemorySubject.Normalize(subject), _options.K, ct)
                    .ConfigureAwait(false);
                foreach (var id in byThisSubject)
                    if (seen.Add(id)) found.Add(id);
            }

            ids = found;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "subject seeding failed for {Engine}; returning no subject candidates",
                request.Engine);
            return [];
        }

        // RETURN is capped by request.Limit alone, applied once here — the merge above is already
        // de-duplicated and ordered, so this is a plain truncation rather than a re-sort.
        var results = new List<GraphNode>(Math.Min(ids.Count, request.Limit));
        foreach (var id in ids.Take(request.Limit))
        {
            var node = await request.Store.GetAsync(request.Engine, id, ct).ConfigureAwait(false);
            if (node is not null) results.Add(node);
        }
        return results;
    }
}
