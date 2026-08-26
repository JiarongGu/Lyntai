using System.Globalization;
using Lyntai.Memory;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// A generated timeline of assertions with KNOWN-CORRECT answers, so temporal resolution can be checked as
/// a property across many seeds rather than against a handful of hand-built fixtures.
///
/// <para><b>Why generated rather than written out.</b> The hand-built facts in
/// <see cref="AssertionResolverTests"/> each encode one situation the author already had in mind, which is
/// exactly the class of bug they cannot find. What breaks a temporal resolver is the interaction — a
/// disputed pair inside a longer chain, a late-arriving version landing between two others, a write order
/// that is not the valid order — and those appear when a generator produces combinations nobody chose.</para>
///
/// <para><b>Three things it varies on purpose, each the source of a real defect class.</b> WRITE ORDER is
/// shuffled, so recorded order never matches valid order and a resolver that quietly sorts by arrival is
/// caught. RECORDED time runs later than VALID time on some versions, which is the late-arriving document
/// case. And some keys carry a DISPUTED pair — two sources claiming the same instant — because a resolver
/// that silently picks one passes every single-answer test ever written.</para>
///
/// <para><b>Content is unique per version by construction.</b> A memory's identity is
/// (engine, task, scope, content), so two versions sharing text would silently become one row and the
/// corpus would quietly hold fewer facts than its ground truth says — the kind of instrument bug that
/// produces a believable number.</para>
/// </summary>
internal static class TemporalCorpus
{
    /// <summary>One assertion to write, and everything the ground truth needs about it.</summary>
    /// <param name="CanonicalKey">The fact slot it answers.</param>
    /// <param name="Content">Its text — unique across the whole corpus.</param>
    /// <param name="ValidFrom">When it starts being true in the world.</param>
    /// <param name="RecordedAt">When the system learned it; never earlier than <paramref name="ValidFrom"/>.</param>
    /// <param name="Source">Which source claimed it — what makes a same-instant pair a DISPUTE.</param>
    internal sealed record Claim(string CanonicalKey, string Content, DateTimeOffset ValidFrom,
        DateTimeOffset RecordedAt, string Source);

    /// <param name="Claims">Every assertion, in the order they should be WRITTEN (shuffled).</param>
    /// <param name="Keys">The canonical keys in play.</param>
    /// <param name="Disputed">The keys carrying a same-instant pair from two sources.</param>
    /// <param name="Epoch">The instant the timeline starts at; probes are offsets from it.</param>
    internal sealed record Corpus(IReadOnlyList<Claim> Claims, IReadOnlyList<string> Keys,
        IReadOnlySet<string> Disputed, DateTimeOffset Epoch);

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Build a corpus. Deterministic in <paramref name="seed"/>: the same seed is the same
    /// timeline, which is what lets a failure be reproduced from the message alone.</summary>
    /// <param name="seed">The generator seed.</param>
    /// <param name="keys">How many canonical keys.</param>
    /// <param name="maxVersions">The most versions any one key gets.</param>
    internal static Corpus Generate(int seed, int keys = 8, int maxVersions = 4)
    {
        var random = new Random(seed);
        var claims = new List<Claim>();
        var names = new List<string>();
        var disputed = new HashSet<string>(StringComparer.Ordinal);

        for (var k = 0; k < keys; k++)
        {
            var key = $"slot:{k}";
            names.Add(key);

            var versions = 1 + random.Next(maxVersions);
            // Distinct months, so every version of a key starts at its own instant and the DISPUTED case
            // below is the only way two can share one. Without that the generator would produce accidental
            // ties and the ground truth would have to guess what they meant.
            var months = Enumerable.Range(0, versions).Select(v => v * 3).ToList();

            for (var v = 0; v < versions; v++)
            {
                var validFrom = Start.AddMonths(months[v]);
                // A late-arriving version is recorded AFTER it took effect -- the case a single timestamp
                // gets wrong. Never before: a system cannot record what has not been asserted to it.
                var lateBy = random.Next(3) == 0 ? random.Next(1, 400) : 0;
                claims.Add(new Claim(key, $"{key} version {v} says value-{seed}-{k}-{v}",
                    validFrom, validFrom.AddDays(lateBy), "primary"));
            }

            // One key in four also carries a rival claim at the SAME instant as its last version, from a
            // different source. Same instant is what makes it a dispute rather than a supersession.
            if (random.Next(4) == 0)
            {
                var contested = Start.AddMonths(months[^1]);
                claims.Add(new Claim(key, $"{key} rival says other-{seed}-{k}",
                    contested, contested.AddDays(random.Next(30)), "rival"));
                disputed.Add(key);
            }
        }

        // WRITE ORDER != VALID ORDER. A resolver that sorts by arrival, or relies on the store's recency,
        // gets the right answer on a corpus written in order and only on that one.
        for (var i = claims.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (claims[i], claims[j]) = (claims[j], claims[i]);
        }

        return new Corpus(claims, names, disputed, Start);
    }

    /// <summary>The write a claim becomes — the proposal's own metadata keys, which is what makes this a
    /// test of its idea rather than of a shape invented here.</summary>
    /// <param name="claim">The claim to write.</param>
    internal static MemoryWrite ToWrite(Claim claim) =>
        new("t", "s", claim.Content, Metadata: new Dictionary<string, string>
        {
            [AssertionResolver.Keys.CanonicalKey] = claim.CanonicalKey,
            [AssertionResolver.Keys.ValidFrom] = Iso(claim.ValidFrom),
            [AssertionResolver.Keys.RecordedAt] = Iso(claim.RecordedAt),
            [AssertionResolver.Keys.SourceRef] = claim.Source,
        });

    /// <summary>
    /// GROUND TRUTH: the content that should be active for <paramref name="key"/> at <paramref name="asOf"/>.
    ///
    /// <para><b>Computed from the claim list independently of the resolver</b>, by the definition rather
    /// than by the algorithm — the greatest <c>ValidFrom</c> at or before the instant, and every claim
    /// sharing it. Deriving it the way the resolver derives it would make the comparison a tautology, which
    /// is the standard way a corpus proves nothing.</para>
    ///
    /// <para>Returns MORE than one entry exactly when the winning instant is contested; empty when the
    /// timeline has not started.</para>
    /// </summary>
    /// <param name="corpus">The corpus.</param>
    /// <param name="key">The canonical key.</param>
    /// <param name="asOf">The instant to judge at.</param>
    internal static IReadOnlyList<string> ExpectedAt(Corpus corpus, string key, DateTimeOffset asOf)
    {
        var eligible = corpus.Claims.Where(c => c.CanonicalKey == key && c.ValidFrom <= asOf).ToList();
        if (eligible.Count == 0) return [];

        var winning = eligible.Max(c => c.ValidFrom);
        return [.. eligible.Where(c => c.ValidFrom == winning).Select(c => c.Content).Order(StringComparer.Ordinal)];
    }

    private static string Iso(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
