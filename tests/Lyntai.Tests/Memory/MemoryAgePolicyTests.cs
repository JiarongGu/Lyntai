using Lyntai.Memory;
using Lyntai.Memory.Interference;

namespace Lyntai.Tests.Memory;

/// <summary>What one write does to a memory — the scale decay is measured on.</summary>
public class MemoryAgePolicyTests
{
    private static MemoryWrite Write(string content = "a note") => new("t", "s", content);
    private const string Engine = "e";

    [Fact]
    public void Per_write_counts_every_write_as_one()
    {
        var tick = new PerWriteAgePolicy().Advance(Write(), Engine);

        Assert.Equal(1, tick.Position);
        Assert.Equal(1, tick.Encoding);
    }

    [Fact]
    public void Content_size_makes_a_long_document_crowd_harder_than_a_note()
    {
        var policy = new ContentSizeAgePolicy(perUnit: 10);

        Assert.True(policy.Advance(Write(new string('x', 100)), Engine).Position >
                    policy.Advance(Write("short"), Engine).Position);
    }

    [Fact]
    public void Elapsed_charges_nothing_for_the_first_write()
    {
        // there is no previous write to measure from, and inventing one would age a fresh memory by
        // however long the process had been up
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new ElapsedAgePolicy(() => now);

        Assert.Equal(0, policy.Advance(Write(), Engine).Position);
    }

    [Fact]
    public void Elapsed_charges_the_real_time_between_writes()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new ElapsedAgePolicy(() => now);
        policy.Advance(Write(), Engine);

        now = now.AddDays(3);

        Assert.Equal(3, policy.Advance(Write(), Engine).Position, precision: 6);
    }

    [Fact]
    public void Elapsed_tracks_each_engine_separately_when_one_instance_is_shared()
    {
        // Task 3's settling of the defect Task 2 recorded and deferred: Advance's _previous used to be
        // scoped to the POLICY INSTANCE, so sharing one across engines (the ordinary DI-singleton shape)
        // tracked the last write across ALL of them — one engine's write would reset another engine's own
        // "since last write" reading. Advance now keys its bookkeeping on the engine name, so a shared
        // instance measures each engine independently: writing to "b" between two writes to "a" must not
        // shrink "a"'s own elapsed reading.
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new ElapsedAgePolicy(() => now);

        policy.Advance(Write(), "a");
        now = now.AddHours(1);
        policy.Advance(Write(), "b"); // must not disturb "a"'s own reading
        now = now.AddDays(3);

        var elapsedForA = policy.Advance(Write(), "a").Position;
        Assert.Equal(3 + 1.0 / 24, elapsedForA, precision: 6); // "a"'s own 3-day-plus-1-hour gap, not zero
    }

    [Fact]
    public void A_burst_advances_far_less_than_its_volume()
    {
        // THE catastrophic-forgetting fix. Undamped, 200 writes advance the position by 200 — which at any
        // sane half-life erases everything stored before them. Reading a long document must not erase your
        // memory of the project.
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new BurstDampenedAgePolicy(new PerWriteAgePolicy(), TimeSpan.FromSeconds(5), () => now);

        var total = 0.0;
        for (var i = 0; i < 200; i++) total += policy.Advance(Write(), Engine).Position;

        Assert.InRange(total, 4, 8); // ~ln(200) + γ, not 200
    }

    [Fact]
    public void A_burst_is_itself_weakly_encoded()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new BurstDampenedAgePolicy(new PerWriteAgePolicy(), TimeSpan.FromSeconds(5), () => now);

        var first = policy.Advance(Write(), Engine).Encoding;
        for (var i = 0; i < 98; i++) policy.Advance(Write(), Engine);
        var hundredth = policy.Advance(Write(), Engine).Encoding;

        Assert.Equal(1, first, precision: 6);
        Assert.Equal(0.1, hundredth, precision: 6); // 1/sqrt(100) — you do not remember what you skim
    }

    [Fact]
    public void A_gap_longer_than_the_window_starts_a_new_burst()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new BurstDampenedAgePolicy(new PerWriteAgePolicy(), TimeSpan.FromSeconds(5), () => now);

        for (var i = 0; i < 50; i++) policy.Advance(Write(), Engine);
        now = now.AddMinutes(1);

        var afterTheGap = policy.Advance(Write(), Engine);

        Assert.Equal(1, afterTheGap.Position, precision: 6);
        Assert.Equal(1, afterTheGap.Encoding, precision: 6);
    }

    [Fact]
    public void Damping_composes_over_whichever_age_policy_was_chosen()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new BurstDampenedAgePolicy(new ContentSizeAgePolicy(perUnit: 10), TimeSpan.FromSeconds(5),
            () => now);

        var first = policy.Advance(Write(new string('x', 100)), Engine).Position;   // 10 / 1
        var second = policy.Advance(Write(new string('x', 100)), Engine).Position;  // 10 / 2

        Assert.Equal(10, first, precision: 6);
        Assert.Equal(5, second, precision: 6);
    }

    [Fact]
    public void Burst_damping_tracks_each_engine_separately_when_one_instance_is_shared()
    {
        // The same per-engine fix as ElapsedAgePolicy above, for BurstDampenedAgePolicy's own burst state —
        // freed for the same reason, since threading the engine name through Advance touches every
        // implementation. A write to "b" mid-burst must not extend (or reset) "a"'s own burst count.
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var policy = new BurstDampenedAgePolicy(new PerWriteAgePolicy(), TimeSpan.FromSeconds(5), () => now);

        var firstA = policy.Advance(Write(), "a").Position;   // 1 / 1
        policy.Advance(Write(), "b");                          // "b"'s own burst, must not touch "a"'s
        var secondA = policy.Advance(Write(), "a").Position;  // 1 / 2, not 1 / 3

        Assert.Equal(1, firstA, precision: 6);
        Assert.Equal(0.5, secondA, precision: 6);
    }

    // --- Age(MemoryAgeSample) — the read-time, DERIVED counterpart of Advance (fix round 1, I2: previously
    // only PerWriteAgePolicy's was exercised by any test, via the identity test's own use of it; the other
    // three shipped with zero direct coverage). ---

    [Fact]
    public void Per_write_age_projects_the_ordinal_primitive_unchanged()
    {
        var age = new PerWriteAgePolicy().Age(new MemoryAgeSample(Ordinal: 7, Volume: 999, ElapsedDays: 999));

        Assert.Equal(7, age, precision: 9);
    }

    [Fact]
    public void Content_size_age_scales_the_volume_primitive_by_perUnit()
    {
        var age = new ContentSizeAgePolicy(perUnit: 10).Age(new MemoryAgeSample(Ordinal: 999, Volume: 250, ElapsedDays: 999));

        Assert.Equal(25, age, precision: 9); // 250 / 10
    }

    [Fact]
    public void Elapsed_age_projects_the_elapsed_days_primitive_unchanged()
    {
        var age = new ElapsedAgePolicy().Age(new MemoryAgeSample(Ordinal: 999, Volume: 999, ElapsedDays: 3.5));

        Assert.Equal(3.5, age, precision: 9);
    }

    [Fact]
    public void Burst_dampened_age_delegates_to_the_wrapped_policys_own_projection()
    {
        // NOT an attempt to reconstruct the damping (IMemoryAgePolicy.Age's own remarks explain why that's
        // impossible from two snapshots) — this pins the delegation itself: whatever the inner policy's Age
        // says is exactly what BurstDampenedAgePolicy reports too, for BOTH shipped inner policies.
        var sample = new MemoryAgeSample(Ordinal: 7, Volume: 250, ElapsedDays: 3.5);

        var perWrite = new BurstDampenedAgePolicy(new PerWriteAgePolicy());
        Assert.Equal(new PerWriteAgePolicy().Age(sample), perWrite.Age(sample), precision: 9);

        var contentSize = new BurstDampenedAgePolicy(new ContentSizeAgePolicy(perUnit: 10));
        Assert.Equal(new ContentSizeAgePolicy(perUnit: 10).Age(sample), contentSize.Age(sample), precision: 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Content_size_refuses_a_non_finite_or_non_positive_perUnit(double perUnit)
    {
        // fix round 1, I2: a silent fallback here previously let Advance (1 per write) and Age (raw,
        // unscaled character count) DISAGREE on what perUnit<=0 means — refusing it at construction is
        // safer than trying to keep two independent formulas' degenerate branches in lockstep forever.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentSizeAgePolicy(perUnit));
    }
}
