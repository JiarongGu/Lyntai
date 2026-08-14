using System.Numerics;
using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Salience;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="MemoryProvenance"/> — the one type that owns a provenance column's bit layout
/// (design doc §5.7, Task 4) — plus the two facts C# will not give for free over the two shipped flags
/// enums: uniqueness across every REGISTERED policy, and single-bit-ness of every named member. Neither is
/// enforced by the compiler: <c>HalfLife = 0x1, Dsr = 0x1</c> compiles silently, and so does
/// <c>HalfLife = 0x3</c> (two bits) — both would make a fitness check wrong with nothing reporting it.</summary>
public class MemoryProvenanceTests
{
    [Fact]
    public void Fits_is_the_AND_equality_predicate()
    {
        Assert.True(MemoryProvenance.Fits(0b101, 0b100));
        Assert.True(MemoryProvenance.Fits(0b101, 0b101));
        Assert.False(MemoryProvenance.Fits(0b101, 0b010));
        Assert.True(MemoryProvenance.Fits(0, 0)); // "nothing required" is always fit
        Assert.False(MemoryProvenance.Fits(0, 1)); // required but nothing computed it — the whole point

        // PARTIAL OVERLAP — the case the other five cannot see, and the one this predicate exists for.
        // Every assertion above survives a `(stored & required) != 0` implementation ("any overlap" rather
        // than "full coverage"), because wherever they expect False the AND is zero either way. Here it is
        // NOT: bit 0 is shared, bit 1 is missing, so a partial match must still read UNFIT. This is the
        // motivating scenario itself — a policy needing state nobody computed, alongside state somebody did.
        Assert.False(MemoryProvenance.Fits(0b001, 0b011));
        Assert.False(MemoryProvenance.Fits(0b011, 0b111));
    }

    [Fact]
    public void Pack_combines_every_contribution_with_bitwise_or()
    {
        Assert.Equal(0b111, MemoryProvenance.Pack([0b001, 0b010, 0b100]));
        Assert.Equal(0, MemoryProvenance.Pack([]));
        Assert.Equal(0b101, MemoryProvenance.Pack(0b001, 0b100)); // the params overload
    }

    /// <summary>Step 4 of the brief: structural, not a runtime check that a caller could bypass — the mask
    /// excludes bit 63 unconditionally, so even an input that sets EVERY bit still comes out non-negative.
    /// Both backends' integer columns are signed, so a negative packed value would round-trip wrong: a
    /// fitness check would still look correct (bitwise AND does not care about sign) while ordering, range
    /// queries and indexes would not.</summary>
    [Fact]
    public void Pack_and_unpack_can_never_produce_a_negative_value()
    {
        var packed = MemoryProvenance.Pack([long.MinValue, -1L, 1L << 62, 1L << 63]);
        Assert.True(packed >= 0, $"Pack produced a negative value: {packed}");

        Assert.True(MemoryProvenance.Unpack(-1) >= 0); // -1 sets every one of the 64 bits, including 63
        Assert.Equal(long.MaxValue, MemoryProvenance.Unpack(-1)); // exactly bits 0-62, nothing more
    }

    // ---- registered-policy facts: the two things C# does not enforce ----

    // HalfLifeRetrievability was deleted in 3.0 (docs/DECISIONS.md D49) — DsrRetrievability is the only
    // shipped policy now, but this stays a list rather than a single value so a future SECOND curve joins
    // it here, one line, exactly as this file's own facts below expect.
    private static readonly IMemoryRetrievabilityPolicy[] RetrievabilityPolicies = [new DsrRetrievability()];

    private static readonly IMemorySaliencePolicy[] SaliencePolicies = [new StructuralSaliencePolicy()];

    [Fact]
    public void Every_shipped_retrievability_policy_declares_exactly_one_bit() =>
        Assert.All(RetrievabilityPolicies, p => Assert.True(IsSingleBit((long)p.Provenance)));

    /// <summary>Step 2 of the fsrs-properly plan's Task 1: <see cref="MemoryRetrievabilityProvenance.HalfLife"/>
    /// is RETIRED, not freed for reuse — every row a 2.5.x deployment wrote under the deleted
    /// <c>HalfLifeRetrievability</c> curve still carries that bit, and handing it to a future policy would
    /// silently misattribute those rows' state to whichever policy claimed it next.</summary>
    [Fact]
    public void No_shipped_retrievability_policy_declares_the_retired_HalfLife_bit() =>
        Assert.All(RetrievabilityPolicies,
            p => Assert.NotEqual(MemoryRetrievabilityProvenance.HalfLife, p.Provenance));

    [Fact]
    public void Every_shipped_salience_policy_declares_exactly_one_bit() =>
        Assert.All(SaliencePolicies, p => Assert.True(IsSingleBit((long)p.Provenance)));

    [Fact]
    public void Every_named_retrievability_enum_member_is_a_single_bit()
    {
        foreach (var member in Enum.GetValues<MemoryRetrievabilityProvenance>())
        {
            if (member == MemoryRetrievabilityProvenance.None) continue; // None is the absence of any bit
            Assert.True(IsSingleBit((long)member), $"{member} is not a single bit");
        }
    }

    [Fact]
    public void Every_named_salience_enum_member_is_a_single_bit()
    {
        foreach (var member in Enum.GetValues<MemorySalienceProvenance>())
        {
            if (member == MemorySalienceProvenance.None) continue;
            Assert.True(IsSingleBit((long)member), $"{member} is not a single bit");
        }
    }

    /// <summary>Pins the trap the brief names directly: a member written <c>0x0000_0003</c> compiles, is
    /// unique, and would still make a fitness check report a policy as present when it never
    /// ran.</summary>
    [Fact]
    public void A_two_bit_value_is_not_single_bit() => Assert.False(IsSingleBit(0x0000_0003));

    [Fact]
    public void Every_shipped_retrievability_policys_provenance_is_unique() =>
        Assert.True(IsUnique(RetrievabilityPolicies.Select(p => (long)p.Provenance)));

    [Fact]
    public void Every_shipped_salience_policys_provenance_is_unique() =>
        Assert.True(IsUnique(SaliencePolicies.Select(p => (long)p.Provenance)));

    /// <summary>The other half of the brief's "catches both" claim: a consumer's own policy occupying a bit
    /// a shipped one already uses. <see cref="MemoryRetrievabilityProvenance.Dsr"/> stands in for the
    /// consumer's bit — the point is that TWO policies sharing a value is what breaks, whichever declared it
    /// first. (Not <see cref="MemoryRetrievabilityProvenance.HalfLife"/>: that bit is RETIRED and nothing in
    /// <see cref="RetrievabilityPolicies"/> declares it any more, so appending it would not collide with
    /// anything — the exact fact <c>No_shipped_retrievability_policy_declares_the_retired_HalfLife_bit</c>
    /// pins.)</summary>
    [Fact]
    public void A_consumer_policy_colliding_with_a_shipped_bit_is_caught_by_the_uniqueness_check()
    {
        var colliding = RetrievabilityPolicies.Select(p => (long)p.Provenance)
            .Append((long)MemoryRetrievabilityProvenance.Dsr);

        Assert.False(IsUnique(colliding));
    }

    private static bool IsSingleBit(long value) => BitOperations.PopCount((ulong)value) == 1;

    private static bool IsUnique(IEnumerable<long> values)
    {
        var list = values.ToList();
        return list.Count == list.Distinct().Count();
    }

    // ---- MemoryProvenance.EnsureEachBitIsSingleRealAndUnique (fix round 2, cheap minor) ----
    // The facts above test HAND-LISTED arrays; these test the PRODUCTION validation itself — the one that
    // actually runs where policies are resolved (GraphMemoryEngine's constructor), so a third policy joining
    // the registered set is caught without anyone remembering to grow a test array.

    /// <summary>The shipped bit (<c>Dsr</c>) and the RETIRED one (<c>HalfLife</c>, its declaring policy
    /// deleted in 3.0) must both remain individually valid — single, non-<c>None</c>, and distinct from each
    /// other — forever. This is what would catch someone quietly reassigning <c>HalfLife</c>'s numeric value
    /// (to reuse it, or by an unrelated enum edit that shifted it onto <c>Dsr</c>'s own bit) without anyone
    /// reading the retirement comment on the member itself.</summary>
    [Fact]
    public void Every_shipped_and_retired_bit_remains_individually_valid() =>
        MemoryProvenance.EnsureEachBitIsSingleRealAndUnique(
            [(long)MemoryRetrievabilityProvenance.HalfLife, (long)MemoryRetrievabilityProvenance.Dsr],
            i => i == 0 ? "HalfLifeRetrievability (retired)" : "DsrRetrievability");

    [Fact]
    public void A_bit_of_zero_None_is_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MemoryProvenance.EnsureEachBitIsSingleRealAndUnique([0L], _ => "SomePolicy"));
        Assert.Contains("SomePolicy", ex.Message, StringComparison.Ordinal);
        Assert.Contains("None", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multi_bit_value_is_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MemoryProvenance.EnsureEachBitIsSingleRealAndUnique([0x3L], _ => "SomePolicy"));
        Assert.Contains("SomePolicy", ex.Message, StringComparison.Ordinal);
        Assert.Contains("single bit", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The other half of the brief's "catches both" claim, run against the PRODUCTION check: a
    /// consumer's own policy occupying a bit a shipped one already uses, this time with a name that
    /// genuinely differs from the shipped policy's own.</summary>
    [Fact]
    public void A_consumer_policy_colliding_with_a_shipped_bit_is_rejected_by_the_production_check()
    {
        var ex = Assert.Throws<ArgumentException>(() => MemoryProvenance.EnsureEachBitIsSingleRealAndUnique(
            [(long)MemoryRetrievabilityProvenance.HalfLife, (long)MemoryRetrievabilityProvenance.HalfLife],
            i => i == 0 ? "HalfLifeRetrievability" : "SomeConsumerPolicy"));

        Assert.Contains("HalfLifeRetrievability", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SomeConsumerPolicy", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>THE distinction the production check makes that a bare value-equality check (the hand-rolled
    /// <see cref="IsUnique"/> above) does not: two REGISTERED INSTANCES of the SAME type sharing a bit is
    /// fine, because "did this algorithm run" is unambiguous regardless of how many instances did — only a
    /// bit shared across DIFFERENT type names is a genuine collision.</summary>
    [Fact]
    public void Two_instances_of_the_SAME_policy_type_sharing_a_bit_is_not_a_collision() =>
        MemoryProvenance.EnsureEachBitIsSingleRealAndUnique(
            [(long)MemoryRetrievabilityProvenance.HalfLife, (long)MemoryRetrievabilityProvenance.HalfLife],
            _ => "HalfLifeRetrievability"); // same name both times — no throw
}
