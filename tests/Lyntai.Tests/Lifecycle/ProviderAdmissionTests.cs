using Lyntai.Lifecycle;

namespace Lyntai.Tests.Lifecycle;

public class ProviderAdmissionTests
{
    private static ProviderKey Key(string value, string slot = "local-diffusion") =>
        ProviderKey.For(slot).With("v", value).Build();

    [Fact]
    public async Task With_no_limit_configured_everything_is_admitted_immediately()
    {
        var admission = new ProviderAdmission();
        var held = new List<IDisposable>();

        for (var i = 0; i < 50; i++) held.Add(await admission.EnterAsync(Key("a")));

        Assert.Equal(50, held.Count);
        foreach (var h in held) h.Dispose();
    }

    [Fact]
    public async Task A_slot_limit_bounds_concurrent_entries_for_that_configuration()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var first = await admission.EnterAsync(Key("a"));
        var second = admission.EnterAsync(Key("a"), CancellationToken.None);

        Assert.False(second.IsCompleted);       // blocked behind the first
        first.Dispose();
        (await second).Dispose();               // released, so it completes
    }

    // The whole reason admission is keyed rather than carried on an instance: one tenant must not throttle
    // another, while two consumers of the SAME engine must share its capacity.
    [Fact]
    public async Task Different_configurations_of_one_backend_do_not_block_each_other()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var tenantA = await admission.EnterAsync(Key("tenant-a"));
        var tenantB = admission.EnterAsync(Key("tenant-b"), CancellationToken.None);

        Assert.True(tenantB.IsCompleted);
        tenantA.Dispose();
        (await tenantB).Dispose();
    }

    [Fact]
    public async Task The_same_configuration_shares_capacity_across_callers()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var one = await admission.EnterAsync(Key("shared"));
        var two = admission.EnterAsync(Key("shared"), CancellationToken.None);

        Assert.False(two.IsCompleted);
        one.Dispose();
        (await two).Dispose();
    }

    [Fact]
    public async Task The_default_limit_applies_to_a_slot_with_no_entry()
    {
        var admission = new ProviderAdmission(new ProviderAdmissionOptions { Default = 1 });

        var first = await admission.EnterAsync(Key("a", "unlisted"));
        var second = admission.EnterAsync(Key("a", "unlisted"), CancellationToken.None);

        Assert.False(second.IsCompleted);
        first.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public async Task Releasing_twice_is_harmless()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var handle = await admission.EnterAsync(Key("a"));
        handle.Dispose();
        handle.Dispose();               // must not over-release the semaphore

        var next = admission.EnterAsync(Key("a"), CancellationToken.None);
        Assert.True(next.IsCompleted);
        (await next).Dispose();

        var third = admission.EnterAsync(Key("a"), CancellationToken.None);
        Assert.True(third.IsCompleted);   // still exactly one permit, not two
        (await third).Dispose();
    }

    [Fact]
    public async Task A_waiting_caller_observes_cancellation()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);
        using var cts = new CancellationTokenSource();

        var held = await admission.EnterAsync(Key("a"));
        var waiting = admission.EnterAsync(Key("a"), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        held.Dispose();
    }

    // Coverage added in fix round 1: the table must be bounded by calls in flight, not by every
    // configuration ever seen (a ConcurrentDictionary that never removed entries was the finding).

    [Fact]
    public async Task A_gate_is_removed_once_its_last_holder_disposes()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var handle = await admission.EnterAsync(Key("a"));
        Assert.Equal(1, admission.GateCount);

        handle.Dispose();

        Assert.Equal(0, admission.GateCount);   // nothing left to hold it alive — no eviction policy needed
    }

    // The path this rewrite most easily gets wrong: a cancelled waiter never took a permit, so it must
    // still decrement the holder count it added before waiting, or the gate is pinned forever.
    [Fact]
    public async Task A_cancelled_waiter_does_not_pin_a_gate()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);
        using var cts = new CancellationTokenSource();

        var held = await admission.EnterAsync(Key("a"));
        var waiting = admission.EnterAsync(Key("a"), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);

        held.Dispose();

        Assert.Equal(0, admission.GateCount);   // the cancelled waiter's holder count was released too
    }

    [Fact]
    public async Task Concurrent_callers_on_one_key_share_one_gate()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var first = await admission.EnterAsync(Key("a"));
        var second = admission.EnterAsync(Key("a"), CancellationToken.None);
        var third = admission.EnterAsync(Key("a"), CancellationToken.None);

        Assert.Equal(1, admission.GateCount);   // one key, one gate, however many callers are queued on it

        first.Dispose();
        (await second).Dispose();
        (await third).Dispose();

        Assert.Equal(0, admission.GateCount);
    }
}
