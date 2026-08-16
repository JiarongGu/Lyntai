namespace Lyntai.Memory;

/// <summary>
/// The one type that owns a provenance column's bit layout — pack, unpack, and the fitness predicate — so a
/// stored value is never read several ways with several different rules. That exact defect (one bag value
/// coerced three different ways at four call sites, silently) is what produced <see cref="MemorySignals.Salience"/>;
/// this type exists so provenance cannot repeat it. Every reader or writer of
/// <c>GraphNode.ProvenanceRetrievability</c>/<c>ProvenanceSalience</c> (and the matching members on
/// <see cref="GraphNodeWrite"/>/<see cref="GraphTouch"/>) goes through this type — no inline bit math
/// anywhere else (design doc §5.7).
/// <para><b>Domain-agnostic on purpose.</b> Retrievability and salience each get their own flags enum
/// (<c>Lyntai.Memory.Forgetting.MemoryRetrievabilityProvenance</c>, <c>Lyntai.Memory.Salience.MemorySalienceProvenance</c>)
/// and their own stored column, but both are packed, unpacked and checked for fitness by the SAME rules —
/// the rules, not the vocabulary, are what has to stay identical across domains. Callers cast a policy's own
/// enum member to <see langword="long"/> at the call site; this type never needs to know either enum
/// exists.</para>
/// <para><b>Bit 63 is never part of a valid value.</b> SQLite's <c>INTEGER</c> and Postgres's <c>BIGINT</c>
/// are both signed 64-bit integers, so a value with the top bit set round-trips NEGATIVE: equality still
/// works (a fitness check would look correct) while ordering, range queries and indexes misbehave. Library
/// policies occupy bits 0-31 and a consumer's own policy occupies bits 32-62 — 32 + 31 = 63 bits, exactly
/// the width <see cref="ValidBits"/> masks. Because the two reserved ranges TOGETHER already span every bit
/// this type will ever accept, <see cref="Pack(System.Collections.Generic.IEnumerable{long})"/> cannot
/// produce a negative result — structurally, by the mask, not by a runtime check a caller could bypass.
/// <see langword="ulong"/>, a cast to it, or <c>&gt;&gt;&gt;</c> therefore never appears anywhere in this
/// type: with the top bit always clear, signed and unsigned bit operations are identical.</para>
/// </summary>
public static class MemoryProvenance
{
    /// <summary>Every bit this type accepts: 0 through 62 inclusive. Bit 63 is excluded from both the
    /// library range (0-31) and the consumer range (32-62), so masking against this value is what makes
    /// <see cref="Pack(System.Collections.Generic.IEnumerable{long})"/> incapable of setting it.</summary>
    private const long ValidBits = long.MaxValue; // 0x7FFF_FFFF_FFFF_FFFF - every bit except 63

    /// <summary>Combine several policies' own <c>Provenance</c> contributions — one bit per policy that
    /// actually computed something for this write — into the single value a store persists for one column.
    /// A singular seam (retrievability) passes exactly one contribution; a plural one (salience) passes one
    /// per policy that produced a non-empty result.
    /// <para>A plain bitwise OR, masked against <see cref="ValidBits"/> so the result can never carry bit 63
    /// however many contributions are combined or whatever a rogue contribution sets.</para></summary>
    /// <param name="contributions">Each contributing policy's own provenance value — typically a single
    /// named enum member cast to <see langword="long"/>.</param>
    public static long Pack(IEnumerable<long> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var packed = 0L;
        foreach (var contribution in contributions) packed |= contribution;
        return packed & ValidBits;
    }

    /// <inheritdoc cref="Pack(IEnumerable{long})"/>
    public static long Pack(params long[] contributions) => Pack((IEnumerable<long>)contributions);

    /// <summary>A stored value, defensively re-masked against <see cref="ValidBits"/> — a row this type
    /// never wrote (a hand-edited column, an older library version, a hostile input) cannot make bit 63
    /// reappear on the way back out. <see cref="Fits"/> already applies this; call it directly only when a
    /// caller wants the raw combined value itself rather than a single fitness answer.</summary>
    public static long Unpack(long stored) => stored & ValidBits;

    /// <summary>Whether <paramref name="stored"/> covers every bit <paramref name="required"/> asks for —
    /// "this memory's stored state was computed by (at least) these policies." This is the fitness
    /// predicate design doc §5.7 exists for: it turns "is this entry fit for the current policy set" from
    /// guessed into answerable, including the case a unit convention alone cannot cover — a policy needing
    /// state nobody has computed yet, rather than state that merely happens to read as zero.</summary>
    /// <param name="stored">The column's current value.</param>
    /// <param name="required">The bit(s) the caller needs present.</param>
    public static bool Fits(long stored, long required) => (Unpack(stored) & required) == required;

    /// <summary>Validates the two facts a <c>[Flags] : long</c> enum's own compiler never checks — a single,
    /// non-zero bit per policy, unique among DIFFERENT policy TYPES — against whatever is actually
    /// REGISTERED, not a hand-listed test array a third policy could join unnoticed (fix round 2, cheap
    /// minor). <c>HalfLife = 0x1, Dsr = 0x1</c> compiles silently, and so does <c>HalfLife = 0x3</c> (two
    /// bits); either would make <see cref="Fits"/> wrong with nothing reporting it.
    /// <para>Called where policies are actually resolved — a plural seam's whole registered collection at
    /// once (several salience policies can coexist and genuinely collide), or a singular seam's one active
    /// policy (nothing to collide WITH, but still checked for being real and single) — never against a
    /// hand-maintained list of "the shipped ones".</para>
    /// <para><b>Two REGISTERED INSTANCES of the SAME type sharing a bit is not a collision.</b> A plural seam
    /// can legitimately register the identical policy type twice with different construction arguments (two
    /// salience policies tuned differently, say) — for provenance, "did this TYPE of judgement run" is
    /// still perfectly well-defined either way, so this only rejects the SAME bit under DIFFERENT
    /// <paramref name="describe"/> names, the case where a fitness check genuinely could not tell which
    /// ALGORITHM ran.</para></summary>
    /// <param name="bits">Each policy's own declared <c>Provenance</c>, cast to <see langword="long"/>, in
    /// the same order as <paramref name="describe"/> can name them.</param>
    /// <param name="describe">Names the policy at a given index — its type name, typically, since that is
    /// what makes two instances of one type read as the SAME contributor rather than a collision.</param>
    /// <exception cref="ArgumentException">An entry is <c>0</c> (<c>None</c> — reserved for "nothing
    /// computed this," never a running policy's own declared identity), is not a single bit, or shares a bit
    /// with an entry <paramref name="describe"/> names DIFFERENTLY.</exception>
    public static void ValidateProvenanceBits(IReadOnlyList<long> bits, Func<int, string> describe)
    {
        ArgumentNullException.ThrowIfNull(bits);
        ArgumentNullException.ThrowIfNull(describe);
        var seen = new Dictionary<long, string>();
        for (var i = 0; i < bits.Count; i++)
        {
            var bit = bits[i];
            var name = describe(i);
            if (bit == 0)
                throw new ArgumentException(
                    $"{name} declares Provenance = None (0). Every REAL, running policy must declare " +
                    "its own non-zero bit — None is reserved for \"nothing computed this,\" never a policy's " +
                    "own identity.", nameof(bits));
            if (System.Numerics.BitOperations.PopCount((ulong)bit) != 1)
                throw new ArgumentException(
                    $"{name} declares Provenance = 0x{bit:X}, which is not a single bit. A fitness " +
                    "check reading a multi-bit value could not tell one policy's own contribution from " +
                    "several at once.", nameof(bits));
            if (seen.TryGetValue(bit, out var earlierName) &&
                !string.Equals(earlierName, name, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"{name} and {earlierName} both declare Provenance = 0x{bit:X}, but they are DIFFERENT " +
                    "policy types. A fitness check could not tell which of them actually ran. (Two " +
                    "REGISTERED INSTANCES of the SAME type sharing a bit is fine — that is not this.)",
                    nameof(bits));
            seen[bit] = name;
        }
    }
}
