using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>The per-key async lock both relational job stores hold around their step-log
/// read-modify-write. Overlap is asserted as a PROPERTY (peak concurrent holders), never as elapsed
/// time — serial execution cannot push a peak above 1 at any speed.</summary>
public class KeyedLockTests
{
    [Fact]
    public async Task The_same_key_serializes_its_holders()
    {
        var keyed = new KeyedLock<string>();
        var inside = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using (await keyed.AcquireAsync("k"))
            {
                var now = Interlocked.Increment(ref inside);
                int seen;
                while (now > (seen = Volatile.Read(ref peak)))
                    Interlocked.CompareExchange(ref peak, now, seen);
                await Task.Yield();
                Interlocked.Decrement(ref inside);
            }
        })));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task Different_keys_hold_at_the_same_time()
    {
        // Deterministic overlap: "a" refuses to leave until "b" is inside. One gate for all keys would
        // deadlock here, so the bounded waits are what turn that regression into a failure, not a hang.
        var keyed = new KeyedLock<string>();
        var aInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = Task.Run(async () =>
        {
            using (await keyed.AcquireAsync("a"))
            {
                aInside.SetResult();
                await bInside.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        });
        var b = Task.Run(async () =>
        {
            await aInside.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (await keyed.AcquireAsync("b")) bInside.SetResult();
        });

        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_table_is_bounded_by_keys_in_flight()
    {
        // The same shape ProviderAdmission's gate table carries: an entry exists only while somebody
        // holds or waits, so an idle process holds nothing and there is nothing to cap or expire.
        var keyed = new KeyedLock<string>();

        var held = await keyed.AcquireAsync("x");
        Assert.Equal(1, keyed.EntryCount);

        held.Dispose();
        Assert.Equal(0, keyed.EntryCount);

        held.Dispose(); // double-dispose must not corrupt the table or release somebody else's gate
        Assert.Equal(0, keyed.EntryCount);
    }

    [Fact]
    public async Task A_cancelled_wait_leaves_the_gate_releasable_and_the_table_clean()
    {
        var keyed = new KeyedLock<string>();
        var held = await keyed.AcquireAsync("x");

        using var cts = new CancellationTokenSource();
        var waiting = keyed.AcquireAsync("x", cts.Token).AsTask();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        held.Dispose();
        Assert.Equal(0, keyed.EntryCount);   // the cancelled waiter did not strand its refcount

        using (await keyed.AcquireAsync("x")) { }   // and the key is acquirable again
    }
}
