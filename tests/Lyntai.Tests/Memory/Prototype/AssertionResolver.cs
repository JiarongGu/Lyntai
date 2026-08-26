using System.Globalization;
using Lyntai.Memory;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// A TEST-ONLY resolver over <see cref="GraphNodeWrite.Metadata"/>, prototyping bitemporal facts and
/// conflict exposure without changing one line of public surface. The key names are the proposal's own,
/// kept verbatim so this tests its idea rather than a restatement of it.
///
/// <para><b>What it is for.</b> An evidence ledger — facts that supersede rather than overwrite, a query
/// that can ask "what was true THEN", a conflict reported rather than silently resolved — is a large amount
/// of permanent API to add to a frozen library on the strength of an argument. This finds out which FIELDS
/// are load-bearing first.</para>
///
/// <para><b>Why it needs no new API.</b> Metadata is app-owned and open-ended, and
/// <see cref="IMemoryGraphStore"/> hands back <see cref="GraphNode.Metadata"/> on both readers (pinned by
/// <c>MemoryGraphStoreContract</c>). The ENGINE drops it — <c>GraphMemoryEngine</c> builds
/// <c>MemoryItem</c> without it on recall and expansion alike — which is why this reads the STORE, and is
/// the clearest thing a first-class version would change: one nullable property on <c>MemoryItem</c>.</para>
///
/// <para><b><c>ValidTo</c> is DERIVED, forced rather than chosen.</b> Metadata is write-once across a
/// re-remember on all three backends, so an assertion cannot be closed off once a successor arrives: an
/// interval ends where the next assertion for the same <c>canonical_key</c> begins. That constraint and the
/// append-only ledger turn out to be the same thing, which is this prototype's most useful finding.</para>
/// </summary>
internal static class AssertionResolver
{
    /// <summary>The metadata keys this prototype reads. Verbatim from the proposal.</summary>
    internal static class Keys
    {
        internal const string CanonicalKey = "lyntai.canonical_key";
        internal const string ValidFrom = "lyntai.valid_from";
        internal const string RecordedAt = "lyntai.recorded_at";
        internal const string SourceRef = "lyntai.source_ref";
    }

    /// <summary>How the resolver currently regards an assertion. Deliberately three values, not the
    /// proposal's seven: these are the ones this prototype can DERIVE from the fields it reads, and
    /// inventing the rest here would be the design-document-first move the prototype exists to avoid.</summary>
    internal enum Status
    {
        /// <summary>The assertion whose validity interval covers the instant asked about.</summary>
        Active,

        /// <summary>A later assertion for the same key took over. Still readable, still historical truth.</summary>
        Superseded,

        /// <summary>Not in force YET as of the instant asked about — its validity starts later.
        /// <para>Distinct from <see cref="Superseded"/> rather than folded into it, because they are
        /// opposite ends of the same interval and calling a future claim "superseded" is simply false. The
        /// distinction is not cosmetic: it was collapsed in the first version of this resolver and the
        /// generated corpus caught the consequence immediately.</para></summary>
        Pending,

        /// <summary>Two or more assertions claim the same key over the same instant, from different sources.
        /// The resolver reports every one of them and picks none.</summary>
        Disputed,
    }

    /// <param name="Id">The node this came from, so a caller can expand it.</param>
    /// <param name="Content">The assertion itself.</param>
    /// <param name="CanonicalKey">Which fact slot it answers.</param>
    /// <param name="ValidFrom">When it started being true in the world.</param>
    /// <param name="ValidTo">When it stopped — DERIVED from the next assertion's start, null while current.</param>
    /// <param name="RecordedAt">When the system learned it, which is a different question.</param>
    /// <param name="SourceRef">Where it came from, or null.</param>
    /// <param name="State">How the resolver regards it as of the instant asked about.</param>
    internal sealed record Assertion(
        long Id, string Content, string CanonicalKey,
        DateTimeOffset ValidFrom, DateTimeOffset? ValidTo, DateTimeOffset RecordedAt,
        string? SourceRef, Status State);

    /// <summary>
    /// Every assertion for <paramref name="canonicalKey"/>, oldest first, each carrying the validity
    /// interval and the status it has AS OF <paramref name="asOf"/>.
    ///
    /// <para><b>It returns the whole history, always.</b> Filtering to the current answer is the CALLER's
    /// step, because "what is true now" and "what did we believe in July" are different questions over the
    /// same evidence — and a resolver that answered only the first would rebuild, one layer up, the
    /// overwriting this whole idea exists to avoid.</para>
    ///
    /// <para>Reads through <see cref="IMemoryGraphStore.SeedAsync"/> with no query, which enumerates the
    /// scope rather than retrieving from it — no ranking, no reinforcement, and no faintness bound, so a
    /// decayed assertion is still part of its own history. <b>An unbounded scan is the honest cost of a
    /// prototype:</b> nothing indexes metadata on any backend, so there is no narrower query to make, and
    /// that is the second thing a first-class version would have to change.</para>
    /// </summary>
    /// <param name="store">The store to read.</param>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">Consumer/purpose scope.</param>
    /// <param name="scope">Variant scope, or null for every scope of the task.</param>
    /// <param name="canonicalKey">The fact slot to resolve.</param>
    /// <param name="asOf">The instant to judge validity at.</param>
    /// <param name="ct">Cancellation.</param>
    internal static async Task<IReadOnlyList<Assertion>> HistoryAsync(
        IMemoryGraphStore store, string engine, string taskKey, string? scope,
        string canonicalKey, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var nodes = await store.SeedAsync(engine, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false);

        return Resolve(nodes, canonicalKey, asOf);
    }

    /// <summary>
    /// The resolution itself, over a candidate set someone else chose — the half of
    /// <see cref="HistoryAsync"/> that does no I/O.
    ///
    /// <para><b>Separated so a BOUNDED candidate set can be resolved too.</b> The method above scans the
    /// whole scope, which no production caller can afford; what a real one has is whatever a limited read
    /// returned. Resolving those through the identical code is the only way to attribute a wrong answer to
    /// the RETRIEVAL rather than to the resolver — and that attribution is the whole point of measuring
    /// it.</para>
    /// </summary>
    /// <param name="nodes">The candidates to resolve over.</param>
    /// <param name="canonicalKey">The fact slot to resolve.</param>
    /// <param name="asOf">The instant to judge validity at.</param>
    internal static IReadOnlyList<Assertion> Resolve(
        IReadOnlyList<GraphNode> nodes, string canonicalKey, DateTimeOffset asOf)
    {
        var claims = nodes
            .Where(n => Read(n, Keys.CanonicalKey) == canonicalKey)
            .Select(n => (Node: n, From: Instant(n, Keys.ValidFrom), At: Instant(n, Keys.RecordedAt)))
            .Where(x => x.From is not null)
            // ValidFrom orders the timeline; the node id breaks a tie so two assertions claiming the same
            // instant have a stable order rather than the store's enumeration order (`storage.md`: an
            // ORDER BY on a non-unique column is nondeterministic on ties).
            .OrderBy(x => x.From!.Value).ThenBy(x => x.Node.Id)
            .ToList();

        var resolved = new List<Assertion>(claims.Count);
        for (var i = 0; i < claims.Count; i++)
        {
            var (node, from, at) = claims[i];

            // DERIVED, not stored: the interval ends where the next DISTINCT start begins. Skipping equal
            // starts is what keeps a disputed pair from closing each other off at zero width — they claim
            // the same instant, so neither supersedes the other.
            var next = claims.Skip(i + 1).FirstOrDefault(c => c.From!.Value > from!.Value);
            DateTimeOffset? until = next.Node is null ? null : next.From!.Value;

            var contested = claims.Any(c => c.Node.Id != node.Id
                && c.From!.Value == from!.Value
                && Read(c.Node, Keys.SourceRef) != Read(node, Keys.SourceRef));

            // IN FORCE FIRST, CONTESTED SECOND — and that order is the whole of this expression.
            // The first version asked `contested` first and unconditionally, so a disputed pair was
            // reported as the current answer at EVERY instant, including before it had taken effect and
            // after a later version superseded it. Every hand-written fact probed an instant where the pair
            // happened to be live, so all of them passed; the generated corpus failed on its first run.
            // A conflict is a property OF an interval, never a property that outranks one.
            var inForce = from!.Value <= asOf && (until is null || asOf < until.Value);

            var state = !inForce ? (from.Value > asOf ? Status.Pending : Status.Superseded)
                : contested ? Status.Disputed
                : Status.Active;

            resolved.Add(new Assertion(node.Id, node.Content, canonicalKey, from!.Value, until,
                at ?? node.CreatedAt, Read(node, Keys.SourceRef), state));
        }

        return resolved;
    }

    /// <summary>What the resolver believes as of <paramref name="asOf"/> — one assertion, or SEVERAL when
    /// they are disputed, or none.
    /// <para><b>Returning a list rather than a single value is the whole point.</b> A conflict that collapses
    /// to one answer is a conflict the caller cannot see, and picking silently is what this is built to
    /// refuse. Two returned assertions means "the evidence disagrees", which is a legitimate thing for a
    /// memory to say and something a single return type cannot express.</para></summary>
    /// <param name="history">A <see cref="HistoryAsync"/> result.</param>
    internal static IReadOnlyList<Assertion> CurrentOf(IReadOnlyList<Assertion> history) =>
        [.. history.Where(a => a.State is Status.Active or Status.Disputed)];

    private static string? Read(GraphNode node, string key) =>
        node.Metadata is not null && node.Metadata.TryGetValue(key, out var value) ? value : null;

    /// <summary>Parse an ISO-8601 instant, or null when absent or unparseable.
    /// <para>Unparseable is treated as ABSENT rather than thrown on: metadata is app-owned free text, so a
    /// malformed value is a caller's data problem, and a resolver that throws would take out the whole
    /// history for one bad row.</para></summary>
    private static DateTimeOffset? Instant(GraphNode node, string key) =>
        Read(node, key) is { } raw
        && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
