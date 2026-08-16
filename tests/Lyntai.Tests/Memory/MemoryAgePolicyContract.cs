using Lyntai.Memory;
using Lyntai.Memory.Interference;

namespace Lyntai.Tests.Memory;

/// <summary>Policy-agnostic facts every <see cref="IMemoryAgePolicy"/> satisfies.
///
/// <para><b>Why this file exists.</b> The age family had FOUR shipped implementations and NO contract — each
/// was covered only by whatever its own tests happened to assert. That is the family <c>CLAUDE.md</c>'s
/// headline claim is about ("all THREE age axes now speak one unit"), and it feeds
/// <c>IMemoryRetrievabilityPolicy</c> directly, so a divergence here changes what every recall ranks and what
/// <c>PruneAsync</c> deletes. Added 2026-08-14 by the whole-codebase review.</para>
///
/// <para>The obligations below are the interface's OWN written promises, not invented ones: a finite,
/// non-negative age; <c>Kind</c> declared rather than inferred; a <c>Derivable</c> policy being a pure
/// function of the sample; and <c>Advance</c> keyed per engine so one registered instance shared by several
/// engines measures each engine's own writes.</para></summary>
public static class MemoryAgePolicyContract
{
    private static readonly MemoryAgeSample[] Samples =
    [
        new(0, 0, 0),
        new(1, 10, 0.5),
        new(1_000, 5_000_000, 3_650),
        new(0.5, 0.25, 0.125),
    ];

    /// <summary>The obligation the interface states in bold: a finite, non-negative number. Written as a
    /// contract fact because it is stated on somebody else's type and there is nowhere in the library to
    /// validate it — the review that added that paragraph found a <c>NaN</c> had already got through once.
    /// </summary>
    public static void Age_is_finite_and_non_negative(IMemoryAgePolicy policy)
    {
        foreach (var sample in Samples)
        {
            var age = policy.Age(sample);
            Assert.True(double.IsFinite(age), $"{policy.GetType().Name} returned a non-finite age for {sample}");
            Assert.True(age >= 0, $"{policy.GetType().Name} returned a negative age ({age}) for {sample}");
        }
    }

    /// <summary>An entry that was just used has no age. Every shipped policy projects one of the three
    /// primitives, all of which are zero at that moment, so a non-zero answer here means the policy is
    /// measuring something other than "since I was last used".</summary>
    public static void A_zero_sample_is_zero_age(IMemoryAgePolicy policy) =>
        Assert.Equal(0, policy.Age(new MemoryAgeSample(0, 0, 0)), precision: 9);

    /// <summary>Age never DECREASES as the primitives grow. A policy reads whichever primitives it likes, so
    /// this grows all three together — the one comparison every implementation must agree on regardless of
    /// which axis it actually counts.</summary>
    public static void Age_never_decreases_as_the_primitives_grow(IMemoryAgePolicy policy)
    {
        var previous = double.NegativeInfinity;
        for (var step = 0; step <= 5; step++)
        {
            var age = policy.Age(new MemoryAgeSample(step, step * 100, step * 1.5));
            Assert.True(age >= previous,
                $"{policy.GetType().Name} went backwards at step {step}: {age} < {previous}");
            previous = age;
        }
    }

    /// <summary><c>Derivable</c> means PURE: the same sample yields the same answer however many times it is
    /// asked, and whatever writes happened in between. That is the property the whole "age is derived, not
    /// stored" design rests on — a store can replay writes and reproduce the value — so a policy declaring
    /// <c>Derivable</c> while keeping hidden state would silently break swap-safety.
    /// <para><c>Accumulating</c> policies are exempt BY DECLARATION, which is the point of the enum: they say
    /// so, and the engine reads the declaration rather than testing the runtime type.</para></summary>
    public static void A_derivable_policy_is_a_pure_function_of_the_sample(IMemoryAgePolicy policy)
    {
        if (policy.Kind != MemoryAgeKind.Derivable) return;

        var sample = new MemoryAgeSample(7, 700, 3.5);
        var first = policy.Age(sample);

        // interleave unrelated writes — a pure projection cannot notice them
        for (var i = 0; i < 5; i++)
            policy.Advance(new MemoryWrite("other-task", "s", $"unrelated write {i}"), "other-engine");

        Assert.Equal(first, policy.Age(sample), precision: 9);
    }

    /// <summary><c>Advance</c> is keyed per ENGINE, which is what lets one DI singleton serve several engines
    /// without one engine's traffic ageing another's memories. Stated explicitly in the member's own summary;
    /// asserted here because a policy that ignored the parameter would look correct in every single-engine
    /// test and corrupt every multi-engine deployment.
    /// <para><b>Compares two INSTANCES rather than asserting a direction on one</b>, and the first draft of
    /// this fact got that wrong in an instructive way. It asserted that engine-a's second tick was not
    /// smaller than its first, and <c>BurstDampenedAgePolicy</c> failed it — correctly. <c>MemoryTick.Position</c>
    /// is the INCREMENT one write contributes, not a running total, and burst damping divides it by the burst
    /// size on purpose so a bulk ingest ages the store less. A shrinking increment is the feature. What the
    /// per-engine promise actually says is that the OTHER engine's writes are invisible here, which is a
    /// comparison between "with interleaving" and "without" — the two runs must agree exactly.</para></summary>
    public static void Advance_is_keyed_per_engine(Func<IMemoryAgePolicy> factory)
    {
        var write = new MemoryWrite("t", "s", "some content of a stable length");

        var interleaved = factory();
        interleaved.Advance(write, "engine-a");
        for (var i = 0; i < 5; i++) interleaved.Advance(write, "engine-b");
        var withOtherTraffic = interleaved.Advance(write, "engine-a");

        var isolated = factory();
        isolated.Advance(write, "engine-a");
        var withoutOtherTraffic = isolated.Advance(write, "engine-a");

        Assert.True(double.IsFinite(withOtherTraffic.Position) && double.IsFinite(withOtherTraffic.Encoding),
            $"{withOtherTraffic} is not finite");
        Assert.Equal(withoutOtherTraffic.Position, withOtherTraffic.Position, precision: 9);
        Assert.Equal(withoutOtherTraffic.Encoding, withOtherTraffic.Encoding, precision: 9);
    }
}
