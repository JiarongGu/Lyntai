using System.Collections.Concurrent;
using System.Net;
using Lyntai.Tools.Mcp.Hosting;

namespace Lyntai.Tests.Tools;

/// <summary>The hosted endpoint's accept loop, through the seam that takes the listener's accept as a delegate: a transient
/// listener fault (a client's reset surfaces as an <see cref="HttpListenerException"/>) must not end the loop and leave the
/// CLI's client waiting on connect, and a shutdown must still end it.</summary>
public class McpToolHostAcceptLoopTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_listener_fault_while_the_listener_is_up_is_survived_and_the_next_request_is_served()
    {
        using var stop = new CancellationTokenSource();
        var handled = new ConcurrentQueue<int>();
        var served = new TaskCompletionSource();
        var step = 0;
        Task<int> Accept()
        {
            switch (Interlocked.Increment(ref step))
            {
                case 1: throw new HttpListenerException(995);          // a reset client: the listener itself is fine
                case 2: return Task.FromResult(7);
                default: return Task.Delay(Timeout.Infinite, stop.Token).ContinueWith(_ => 0, TaskScheduler.Default);
            }
        }

        var loop = McpToolHost.AcceptLoopAsync(
            Accept, () => true,
            context => { handled.Enqueue(context); served.TrySetResult(); return Task.CompletedTask; },
            _ => { }, TimeSpan.FromMilliseconds(10), stop.Token);

        await served.Task.WaitAsync(Wait);
        Assert.False(loop.IsCompleted);                                // still accepting after the fault
        Assert.Equal([7], handled);

        await stop.CancelAsync();
        await loop.WaitAsync(Wait);                                    // and a shutdown still ends it
    }

    [Fact]
    public async Task A_fault_after_the_listener_stopped_is_a_shutdown_and_ends_the_loop()
    {
        var accepts = 0;
        Task<int> Accept()
        {
            Interlocked.Increment(ref accepts);
            throw new HttpListenerException(995);
        }

        await McpToolHost.AcceptLoopAsync(Accept, () => false, _ => Task.CompletedTask, _ => { },
            TimeSpan.FromMilliseconds(10), CancellationToken.None).WaitAsync(Wait);

        Assert.Equal(1, accepts);
    }

    [Fact]
    public async Task A_request_whose_handler_throws_is_failed_and_the_loop_goes_on()
    {
        using var stop = new CancellationTokenSource();
        var failed = new ConcurrentQueue<int>();
        var second = new TaskCompletionSource();
        var step = 0;
        Task<int> Accept() => Interlocked.Increment(ref step) switch
        {
            1 => Task.FromResult(1),
            2 => Task.FromResult(2),
            _ => Task.Delay(Timeout.Infinite, stop.Token).ContinueWith(_ => 0, TaskScheduler.Default),
        };

        var loop = McpToolHost.AcceptLoopAsync(
            Accept, () => true,
            context => context == 1 ? throw new InvalidOperationException("bad request") : Task.CompletedTask,
            context => { failed.Enqueue(context); if (context == 1) second.TrySetResult(); },
            TimeSpan.FromMilliseconds(10), stop.Token);

        await second.Task.WaitAsync(Wait);
        Assert.Equal([1], failed);
        await stop.CancelAsync();
        await loop.WaitAsync(Wait);
    }
}
