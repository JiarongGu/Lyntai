using Lyntai.Memory;
using Lyntai.Memory.Interference;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryAgePolicyContract"/> fact against all FOUR shipped age policies. Derive a
/// class per policy so a new implementation gets the whole contract by adding one file — the same shape
/// <c>JobStoreContractFacts</c> uses for the job stores.</summary>
public abstract class MemoryAgePolicyContractFacts
{
    protected abstract IMemoryAgePolicy New();

    [Fact] public void Finite_non_negative() => MemoryAgePolicyContract.Age_is_finite_and_non_negative(New());
    [Fact] public void Zero_sample_zero_age() => MemoryAgePolicyContract.A_zero_sample_is_zero_age(New());
    [Fact] public void Monotonic() => MemoryAgePolicyContract.Age_never_decreases_as_the_primitives_grow(New());
    [Fact] public void Derivable_is_pure() => MemoryAgePolicyContract.A_derivable_policy_is_a_pure_function_of_the_sample(New());
    [Fact] public void Advance_per_engine() => MemoryAgePolicyContract.Advance_is_keyed_per_engine(New);
}

public class PerWriteAgePolicyContractTests : MemoryAgePolicyContractFacts
{
    protected override IMemoryAgePolicy New() => new PerWriteAgePolicy();
}

public class ContentSizeAgePolicyContractTests : MemoryAgePolicyContractFacts
{
    protected override IMemoryAgePolicy New() => new ContentSizeAgePolicy();
}

/// <summary>The one policy whose <c>Advance</c> reads a real clock, which makes it the one that cannot take
/// the shared <see cref="MemoryAgePolicyContractFacts.Advance_per_engine"/> fact at face value.
///
/// <para><b>Why it is constructed with a FROZEN clock (found flaky 2026-08-15).</b> That fact advances
/// engine-a, then engine-b five times, then engine-a again, and asserts the result equals an isolated
/// engine-a run to nine decimal places. For every other policy that equality IS the keying property. For
/// this one the value is <c>now - last_a</c> in DAYS, so the interleaved arm also absorbs however long the
/// five engine-b calls took: it held only while the machine was fast enough for both arms to round to zero,
/// and it failed inside a full-suite run and passed alone. A test that depends on the machine's load reads
/// as coverage and is not.</para>
///
/// <para>Frozen, the shared fact is deterministic but no longer discriminating HERE — zero equals zero
/// whatever the keying does — so the property it was standing in for is asserted directly below, with a
/// clock the test drives. Stated rather than left implicit, because a fact that cannot fail is the thing
/// this repository treats as worse than no fact at all.</para></summary>
public class ElapsedAgePolicyContractTests : MemoryAgePolicyContractFacts
{
    private static readonly DateTimeOffset Frozen = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    protected override IMemoryAgePolicy New() => new ElapsedAgePolicy(() => Frozen);

    [Fact]
    public void Elapsed_is_measured_from_this_engines_own_last_write_not_from_any_other_engines()
    {
        // The real keying property, on a clock the test moves so no wall-clock noise can reach it. Engine-b
        // writes at every step in between; engine-a must still measure exactly its OWN two-day gap.
        var now = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        var policy = new ElapsedAgePolicy(() => now);
        var write = new MemoryWrite("t", "s", "some content of a stable length");

        policy.Advance(write, "engine-a");                       // day 0
        now = now.AddDays(1);
        for (var i = 0; i < 5; i++) policy.Advance(write, "engine-b");
        now = now.AddDays(1);
        var engineA = policy.Advance(write, "engine-a");          // day 2

        Assert.Equal(2, engineA.Position, precision: 9);          // not 1, which is engine-b's last gap
    }
}

/// <summary>The shipped DEFAULT, and the one policy that declares <see cref="MemoryAgeKind.Accumulating"/> —
/// so the purity fact deliberately does not apply to it, by its own declaration rather than by an exclusion
/// list here.</summary>
public class BurstDampenedAgePolicyContractTests : MemoryAgePolicyContractFacts
{
    protected override IMemoryAgePolicy New() => new BurstDampenedAgePolicy(new PerWriteAgePolicy());
}
