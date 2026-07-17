using Lyntai.Llm.Routing;

namespace Lyntai.Tests.Core;

public class DeadHostTrackerTests
{
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private DeadHostTracker Tracker(int threshold = 3, int cooldownSeconds = 30) =>
        new(threshold, TimeSpan.FromSeconds(cooldownSeconds), () => _now);

    [Fact]
    public void Fails_below_threshold_stays_live()
    {
        var t = Tracker(threshold: 3);
        t.RecordFailure("p");
        t.RecordFailure("p");
        Assert.False(t.IsDead("p"));
    }

    [Fact]
    public void Hitting_threshold_goes_dead()
    {
        var t = Tracker(threshold: 3);
        t.RecordFailure("p");
        t.RecordFailure("p");
        t.RecordFailure("p");
        Assert.True(t.IsDead("p"));
        Assert.False(t.IsDead("other")); // isolation per key
    }

    [Fact]
    public void Success_resets_the_failure_count()
    {
        var t = Tracker(threshold: 3);
        t.RecordFailure("p");
        t.RecordFailure("p");
        t.RecordSuccess("p");
        t.RecordFailure("p");
        t.RecordFailure("p");
        Assert.False(t.IsDead("p"));
    }

    [Fact]
    public void Mark_dead_skips_the_threshold_entirely()
    {
        var t = Tracker(threshold: 3, cooldownSeconds: 30);

        t.MarkDead("p"); // one backoff signal (429) → dead immediately
        Assert.True(t.IsDead("p"));

        _now += TimeSpan.FromSeconds(31);
        Assert.False(t.IsDead("p")); // cooldown expires like any other

        t.RecordSuccess("p");
        t.RecordFailure("p");
        Assert.False(t.IsDead("p")); // success fully reset the probation
    }

    [Fact]
    public void Cooldown_expiry_re_lives_the_host()
    {
        var t = Tracker(threshold: 2, cooldownSeconds: 30);
        t.RecordFailure("p");
        t.RecordFailure("p");
        Assert.True(t.IsDead("p"));

        _now += TimeSpan.FromSeconds(31);
        Assert.False(t.IsDead("p"));

        // back in rotation on probation: one more failure re-kills, a success fully resets
        t.RecordFailure("p");
        Assert.True(t.IsDead("p"));
    }
}
