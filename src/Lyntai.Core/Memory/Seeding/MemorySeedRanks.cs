namespace Lyntai.Memory.Seeding;

/// <summary>One source's verdict on one candidate: which retrieval channel matched it, and where in that
/// channel's own best-first order it landed.</summary>
/// <param name="Source">The <c>IMemorySeedSource.Name</c> that matched it.</param>
/// <param name="Rank">1-based position within that source's own <see cref="GraphNode.Relevance"/> gradient —
/// competition-ranked, so a tied group shares a number and the next distinct value skips its width. 1 is
/// best. Never 0 — a reciprocal-rank term is <c>w / (K + rank)</c> and rank 0 would make one source's best
/// hit score as though the fusion constant alone governed it.</param>
public readonly record struct MemorySeedRank(string Source, int Rank);

/// <summary>
/// Which sources matched one candidate, and at what rank within each — the per-source ordering that
/// <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/> fuses.
///
/// <para><b>Equality is by CONTENT.</b> This wraps an array, and a struct over an array gets REFERENCE
/// equality by default. <see cref="Lyntai.Memory.Ranking.MemoryCandidate"/> is a record struct whose
/// generated <c>Equals</c> would inherit that, so two candidates identical in every value would compare
/// unequal — silently, and visible only as an unrelated existing test starting to fail.</para>
///
/// <para><b><c>default</c> is <see cref="Empty"/></b>, and empty means "no relevance evidence" rather than
/// "ranked worst" — see <c>MemoryCandidate.Ranks</c> for the two cases it covers.</para>
/// </summary>
public readonly struct MemorySeedRanks : IEquatable<MemorySeedRanks>
{
    private readonly MemorySeedRank[]? _ranks;

    /// <summary>No source matched this candidate.</summary>
    public static MemorySeedRanks Empty { get; }

    /// <summary>Wraps a source-rank sequence; null or empty yields <see cref="Empty"/>.</summary>
    /// <param name="ranks">The pairs, in any order.</param>
    public MemorySeedRanks(IEnumerable<MemorySeedRank>? ranks)
    {
        MemorySeedRank[] materialized = ranks is null ? [] : ranks.ToArray();
        _ranks = materialized.Length == 0 ? null : materialized;
    }

    /// <summary>How many sources matched this candidate.</summary>
    public int Count => _ranks?.Length ?? 0;

    /// <summary>Whether no source matched it.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>The pairs, allocation-free — what a ranking policy iterates.</summary>
    public ReadOnlySpan<MemorySeedRank> Span => _ranks;

    /// <summary>This candidate's rank within one named source.</summary>
    /// <param name="source">The source name.</param>
    /// <param name="rank">Its 1-based rank, or 0 when that source did not match it.</param>
    /// <returns>Whether that source matched it.</returns>
    public bool TryGet(string source, out int rank)
    {
        foreach (var entry in Span)
            if (string.Equals(entry.Source, source, StringComparison.Ordinal))
            {
                rank = entry.Rank;
                return true;
            }

        rank = 0;
        return false;
    }

    /// <inheritdoc />
    public bool Equals(MemorySeedRanks other) => Span.SequenceEqual(other.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MemorySeedRanks other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Span) hash.Add(entry);
        return hash.ToHashCode();
    }

    /// <summary>Content equality — see the type's own remarks for why reference equality is a defect here.</summary>
    public static bool operator ==(MemorySeedRanks left, MemorySeedRanks right) => left.Equals(right);

    /// <summary>Content inequality.</summary>
    public static bool operator !=(MemorySeedRanks left, MemorySeedRanks right) => !left.Equals(right);
}
