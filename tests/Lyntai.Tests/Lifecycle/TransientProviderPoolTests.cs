using Lyntai.Lifecycle;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class TransientProviderPoolTests
{
    private static ProviderKey Key(string slot = "fake-generation", string value = "a") =>
        ProviderKey.For(slot).With("v", value).Build();

    [Fact]
    public void Every_call_builds_a_new_instance()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        var key = Key();

        var first = pool.GetOrAdd(key, () => new FakeGenerationProvider());
        var second = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        Assert.NotSame(first, second);
    }

    // Without this, cooldown and admission fall back to the provider id under Transient and the whole
    // configuration-scoped story collapses for the strategy that needs it most.
    [Fact]
    public void The_key_of_a_built_instance_is_recoverable()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        var key = Key(value: "specific");

        var instance = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        Assert.True(pool.TryGetKey(instance, out var found));
        Assert.Equal(key, found);
    }

    [Fact]
    public void An_instance_the_pool_never_built_has_no_key()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();

        Assert.False(pool.TryGetKey(new FakeGenerationProvider(), out _));
    }

    [Fact]
    public void An_id_that_disagrees_with_the_slot_is_rejected()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();

        var ex = Assert.Throws<ArgumentException>(() =>
            pool.GetOrAdd(Key("a1111"), () => new FakeGenerationProvider { Id = "comfyui" }));

        Assert.Contains("comfyui", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a1111", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_retained_so_retiring_reports_nothing_to_retire()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        pool.GetOrAdd(Key(), () => new FakeGenerationProvider());

        Assert.False(pool.Retire(Key()));
        Assert.Equal(0, pool.RetireSlot("fake-generation"));
        Assert.Equal(0, pool.Statistics.Live);
    }

    [Fact]
    public void Statistics_count_every_construction_and_never_a_reuse()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        pool.GetOrAdd(Key(), () => new FakeGenerationProvider());
        pool.GetOrAdd(Key(), () => new FakeGenerationProvider());

        Assert.Equal(2, pool.Statistics.Created);
        Assert.Equal(0, pool.Statistics.Reused);
    }

    [Fact]
    public void A_throwing_factory_propagates()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();

        Assert.Throws<InvalidOperationException>(() =>
            pool.GetOrAdd(Key(), () => throw new InvalidOperationException("bad config")));
    }
}
