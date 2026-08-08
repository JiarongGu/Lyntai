using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>What one write does to a memory — the scale decay is measured on.</summary>
public class MemoryClockTests
{
    private static MemoryWrite Write(string content = "a note") => new("t", "s", content);

    [Fact]
    public void Per_write_counts_every_write_as_one()
    {
        var tick = new PerWriteClock().Advance(Write());

        Assert.Equal(1, tick.Position);
        Assert.Equal(1, tick.Encoding);
    }

    [Fact]
    public void Content_size_makes_a_long_document_crowd_harder_than_a_note()
    {
        var clock = new ContentSizeClock(perUnit: 10);

        Assert.True(clock.Advance(Write(new string('x', 100))).Position >
                    clock.Advance(Write("short")).Position);
    }

    [Fact]
    public void Elapsed_charges_nothing_for_the_first_write()
    {
        // there is no previous write to measure from, and inventing one would age a fresh memory by
        // however long the process had been up
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ElapsedClock(() => now);

        Assert.Equal(0, clock.Advance(Write()).Position);
    }

    [Fact]
    public void Elapsed_charges_the_real_time_between_writes()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ElapsedClock(() => now);
        clock.Advance(Write());

        now = now.AddDays(3);

        Assert.Equal(3, clock.Advance(Write()).Position, precision: 6);
    }

    [Fact]
    public void A_burst_advances_far_less_than_its_volume()
    {
        // THE catastrophic-forgetting fix. Undamped, 200 writes advance the position by 200 — which at any
        // sane half-life erases everything stored before them. Reading a long document must not erase your
        // memory of the project.
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new BurstDampenedClock(new PerWriteClock(), TimeSpan.FromSeconds(5), () => now);

        var total = 0.0;
        for (var i = 0; i < 200; i++) total += clock.Advance(Write()).Position;

        Assert.InRange(total, 4, 8); // ~ln(200) + γ, not 200
    }

    [Fact]
    public void A_burst_is_itself_weakly_encoded()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new BurstDampenedClock(new PerWriteClock(), TimeSpan.FromSeconds(5), () => now);

        var first = clock.Advance(Write()).Encoding;
        for (var i = 0; i < 98; i++) clock.Advance(Write());
        var hundredth = clock.Advance(Write()).Encoding;

        Assert.Equal(1, first, precision: 6);
        Assert.Equal(0.1, hundredth, precision: 6); // 1/sqrt(100) — you do not remember what you skim
    }

    [Fact]
    public void A_gap_longer_than_the_window_starts_a_new_burst()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new BurstDampenedClock(new PerWriteClock(), TimeSpan.FromSeconds(5), () => now);

        for (var i = 0; i < 50; i++) clock.Advance(Write());
        now = now.AddMinutes(1);

        var afterTheGap = clock.Advance(Write());

        Assert.Equal(1, afterTheGap.Position, precision: 6);
        Assert.Equal(1, afterTheGap.Encoding, precision: 6);
    }

    [Fact]
    public void Damping_composes_over_whichever_clock_was_chosen()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new BurstDampenedClock(new ContentSizeClock(perUnit: 10), TimeSpan.FromSeconds(5),
            () => now);

        var first = clock.Advance(Write(new string('x', 100))).Position;   // 10 / 1
        var second = clock.Advance(Write(new string('x', 100))).Position;  // 10 / 2

        Assert.Equal(10, first, precision: 6);
        Assert.Equal(5, second, precision: 6);
    }
}
