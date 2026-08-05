using Lyntai.Generation;
using Lyntai.Lifecycle;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class BoundedProviderPoolTests
{
    private static ProviderKey Key(string value, string slot = "fake-generation") =>
        ProviderKey.For(slot).With("v", value).Build();

    private static BoundedProviderPool<FakeGenerationProvider> Pool(
        int? maxEntries = null, TimeSpan? idleTimeout = null, MutableClock? clock = null) =>
        new(new ProviderPoolOptions { MaxEntries = maxEntries, IdleTimeout = idleTimeout },
            clock is null ? null : clock.Get);

    [Fact]
    public void The_same_key_returns_the_same_instance()
    {
        var pool = Pool();
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());
        var second = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        Assert.Same(first, second);
        Assert.Equal(1, pool.Statistics.Created);
        Assert.Equal(1, pool.Statistics.Reused);
    }

    [Fact]
    public void A_changed_key_builds_a_new_instance_and_replaces_the_old()
    {
        var pool = Pool();
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());
        var second = pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider());

        Assert.NotSame(first, second);
    }

    // The multi-configuration requirement: one backend id, several credentials, all live at once.
    [Fact]
    public void Several_configurations_of_one_backend_are_live_simultaneously()
    {
        var pool = Pool();
        var tenantA = pool.GetOrAdd(Key("tenant-a"), () => new FakeGenerationProvider());
        var tenantB = pool.GetOrAdd(Key("tenant-b"), () => new FakeGenerationProvider());

        Assert.NotSame(tenantA, tenantB);
        Assert.Same(tenantA, pool.GetOrAdd(Key("tenant-a"), () => new FakeGenerationProvider()));
        Assert.Same(tenantB, pool.GetOrAdd(Key("tenant-b"), () => new FakeGenerationProvider()));
        Assert.Equal(2, pool.Statistics.Live);
    }

    [Fact]
    public void Retire_drops_the_entry_so_the_next_call_rebuilds()
    {
        var pool = Pool();
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        Assert.True(pool.Retire(Key("a")));
        Assert.False(pool.Retire(Key("a")));

        Assert.NotSame(first, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void RetireSlot_drops_every_configuration_of_that_backend_only()
    {
        var pool = Pool();
        pool.GetOrAdd(Key("a", "a1111"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("b", "a1111"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("c", "comfyui"), () => new FakeGenerationProvider { Id = "comfyui" });

        Assert.Equal(2, pool.RetireSlot("a1111"));
        Assert.Equal(1, pool.Statistics.Live);
    }

    // Trap 7.1: a configuration change must never abort work already running on the old instance.
    [Fact]
    public async Task A_retired_instance_is_still_usable_by_whoever_holds_it()
    {
        var pool = Pool();
        var inFlight = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        pool.Retire(Key("a"));

        var result = await inFlight.GenerateAsync(new GenerationRequest { Kind = "image", Prompt = "a cat" });
        Assert.True(result.IsOk);
    }

    // The pool disposes nothing, ever — see the spec's 4.5. Pinned so a later "helpful" disposal fails here.
    [Fact]
    public void Retiring_never_disposes()
    {
        var pool = new BoundedProviderPool<DisposableProvider>();
        var instance = pool.GetOrAdd(ProviderKey.For("disposable").With("v", "a").Build(),
            () => new DisposableProvider());

        pool.Retire(ProviderKey.For("disposable").With("v", "a").Build());

        Assert.False(instance.Disposed);
    }

    [Fact]
    public void Least_recently_used_is_retired_at_the_entry_cap()
    {
        var pool = Pool(maxEntries: 2);
        var a = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());
        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider());
        pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());   // touch a, so b is now least recent
        pool.GetOrAdd(Key("c"), () => new FakeGenerationProvider());   // evicts b

        Assert.Equal(2, pool.Statistics.Live);
        Assert.Same(a, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void An_entry_idle_past_the_timeout_is_retired_on_the_next_access()
    {
        var clock = new MutableClock();
        var pool = Pool(idleTimeout: TimeSpan.FromMinutes(10), clock: clock);
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.NotSame(first, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void An_entry_used_within_the_timeout_survives()
    {
        var clock = new MutableClock();
        var pool = Pool(idleTimeout: TimeSpan.FromMinutes(10), clock: clock);
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        clock.Advance(TimeSpan.FromMinutes(9));

        Assert.Same(first, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void Both_bounds_unset_means_nothing_is_ever_evicted()
    {
        var clock = new MutableClock();
        var pool = Pool(clock: clock);
        for (var i = 0; i < 200; i++)
            pool.GetOrAdd(Key($"k{i}"), () => new FakeGenerationProvider());

        clock.Advance(TimeSpan.FromDays(30));

        Assert.Equal(200, pool.Statistics.Live);
    }

    // 0 reads like "hold nothing" and means the OPPOSITE: it is the same "no count bound" as null (so is any
    // negative), the convention ProviderAdmissionOptions.Default already documents. Pinned so the reading is
    // a decision rather than a fall-through nobody noticed.
    [Fact]
    public void A_zero_entry_cap_is_no_count_bound_not_an_empty_pool()
    {
        var pool = Pool(maxEntries: 0);
        for (var i = 0; i < 10; i++)
            pool.GetOrAdd(Key($"k{i}"), () => new FakeGenerationProvider());

        Assert.Equal(10, pool.Statistics.Live);
    }

    // Two requests for a newly-configured tenant arriving together must not each get their own instance,
    // each accumulating half the history.
    [Fact]
    public void Concurrent_calls_on_one_key_build_exactly_once()
    {
        var pool = Pool();
        var built = 0;
        var instances = new FakeGenerationProvider[32];

        Parallel.For(0, instances.Length, i =>
            instances[i] = pool.GetOrAdd(Key("a"), () =>
            {
                Interlocked.Increment(ref built);
                return new FakeGenerationProvider();
            }));

        Assert.Equal(1, built);
        Assert.All(instances, x => Assert.Same(instances[0], x));
    }

    [Fact]
    public void A_throwing_factory_leaves_the_existing_entry_intact()
    {
        var pool = Pool();
        var existing = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        Assert.Throws<InvalidOperationException>(() =>
            pool.GetOrAdd(Key("b"), () => throw new InvalidOperationException("bad config")));

        Assert.Same(existing, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
        Assert.Equal(1, pool.Statistics.Live);
    }

    [Fact]
    public void An_id_that_disagrees_with_the_slot_is_rejected_and_stores_nothing()
    {
        var pool = Pool();

        Assert.Throws<ArgumentException>(() =>
            pool.GetOrAdd(Key("a", "a1111"), () => new FakeGenerationProvider { Id = "comfyui" }));

        Assert.Equal(0, pool.Statistics.Live);
    }

    [Fact]
    public void TryGetKey_answers_for_a_live_entry()
    {
        var pool = Pool();
        var key = Key("a");
        var instance = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        Assert.True(pool.TryGetKey(instance, out var found));
        Assert.Equal(key, found);
    }

    // Load-bearing per IProviderPool<TProvider>'s own remarks on TryGetKey: a router attributes dead-host
    // cooldown to the CONFIGURATION of a call that is still in flight on a provider the pool already
    // dropped. If this answered false the moment Retire ran, cooldown would silently fall back to the
    // backend id and bench every other tenant sharing it.
    [Fact]
    public void TryGetKey_still_answers_after_the_entry_is_retired()
    {
        var pool = Pool();
        var key = Key("a");
        var instance = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        pool.Retire(key);

        Assert.True(pool.TryGetKey(instance, out var found));
        Assert.Equal(key, found);
    }

    // Same load-bearing case as retirement, but via LRU eviction rather than an explicit Retire call.
    [Fact]
    public void TryGetKey_still_answers_after_the_entry_is_evicted()
    {
        var pool = Pool(maxEntries: 1);
        var key = Key("a");
        var instance = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider());   // evicts "a" at the cap of 1

        Assert.True(pool.TryGetKey(instance, out var found));
        Assert.Equal(key, found);
    }

    [Fact]
    public void TryGetKey_is_false_for_an_instance_the_pool_never_built()
    {
        var pool = Pool();

        Assert.False(pool.TryGetKey(new FakeGenerationProvider(), out _));
    }

    private sealed class DisposableProvider : Lyntai.Lifecycle.IProviderIdentity, IDisposable
    {
        public string Id => "disposable";
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
