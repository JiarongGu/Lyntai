using System.Runtime.CompilerServices;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

/// <summary>The budget doors over a tracker that can RESERVE (<see cref="IReservingUsageTracker"/>): with a hold
/// configured the cap is hard under concurrency, the hold is released however the call ends, and without either
/// half — a tracker that cannot reserve, or no hold set — the soft check is exactly what it was.</summary>
public class UsageReservationBudgetTests
{
    private static TextRequest Ask(string consumer = "default") =>
        new() { Messages = [TextMessage.User("q")], Consumer = consumer };

    private static LyntaiOptions Options(double cap, double? hold)
    {
        var options = new LyntaiOptions();
        options.Budget.MaxCostUsd = cap;
        options.Budget.ReserveCostUsd = hold;
        return options;
    }

    /// <summary>Whether the full cap can still be held — true exactly when no hold leaked.</summary>
    private static async Task<bool> NothingHeld(IReservingUsageTracker tracker, double cap) =>
        (await tracker.ReserveAsync(new("probe", new ProviderUsage(CostUsd: cap - (await tracker.TotalAsync()).CostUsd),
            new UsageCaps(MaxCostUsd: cap), DateTimeOffset.UtcNow.AddMinutes(1)))).Granted;

    /// <summary>Every call waits on one gate, so a test can hold N calls in flight at once.</summary>
    private sealed class GatedTextClient(double costPerCall) : ITextClient
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        public int Entered => Volatile.Read(ref _entered);
        public Exception? Throws { get; init; }
        public void Open() => _open.TrySetResult();

        public ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(TextRequest req, CancellationToken ct = default) =>
            ValueTask.FromResult<ProviderCapabilities?>(null);

        public async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _entered);
            await _open.Task.WaitAsync(ct);
            if (Throws is not null) throw Throws;
            return new TextResponse("ok", ProviderVerdict.Ok, new TextUsage(1, 1, CostUsd: costPerCall));
        }

        public async IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Interlocked.Increment(ref _entered);
            await _open.Task.WaitAsync(ct);
            yield return TextChunk.Content("a");
            yield return TextChunk.Content("b");
            yield return TextChunk.Final(new TextUsage(1, 1, CostUsd: costPerCall));
        }
    }

    private static async Task<TextResponse[]> InFlight(ITextClient client, GatedTextClient inner, int n, int expectEntered)
    {
        var calls = Enumerable.Range(0, n).Select(_ => client.CompleteAsync(Ask())).ToArray();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (calls.Count(c => c.IsCompleted) + inner.Entered < n && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.Equal(expectEntered, inner.Entered);
        inner.Open();
        return await Task.WhenAll(calls);
    }

    [Fact]
    public async Task With_a_hold_set_the_cap_is_HARD_under_concurrency()
    {
        var tracker = new InMemoryUsageTracker();
        var inner = new GatedTextClient(costPerCall: 0.2);
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 0.25));

        var replies = await InFlight(client, inner, n: 10, expectEntered: 4); // 4 × 0.25 fits; the fifth does not

        Assert.Equal(4, replies.Count(r => r.Verdict == ProviderVerdict.Ok));
        Assert.Equal(6, replies.Count(r => r.Verdict == ProviderVerdict.Refused));
        Assert.Equal(0.8, (await tracker.TotalAsync()).CostUsd, 5); // reconciled to what each actually cost
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task Without_a_hold_set_a_reserving_tracker_keeps_the_soft_check()
    {
        var tracker = new InMemoryUsageTracker();
        var inner = new GatedTextClient(costPerCall: 0.2);
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: null));

        var replies = await InFlight(client, inner, n: 10, expectEntered: 10); // every call passed the pre-call read

        Assert.All(replies, r => Assert.Equal(ProviderVerdict.Ok, r.Verdict));
    }

    /// <summary>A tracker that implements only <see cref="IUsageTracker"/>, so the doors cannot reserve.</summary>
    private sealed class PlainTracker : IUsageTracker
    {
        private readonly InMemoryUsageTracker _inner = new();
        public ValueTask RecordAsync(string consumer, ProviderUsage usage, CancellationToken ct = default) => _inner.RecordAsync(consumer, usage, ct);
        public ValueTask<UsageTotals> TotalAsync(string? consumer = null, CancellationToken ct = default) => _inner.TotalAsync(consumer, ct);
        public ValueTask ResetAsync(string? consumer = null, CancellationToken ct = default) => _inner.ResetAsync(consumer, ct);
    }

    [Fact]
    public async Task A_tracker_that_cannot_reserve_keeps_the_soft_check_even_with_a_hold_set()
    {
        var tracker = new PlainTracker();
        var inner = new GatedTextClient(costPerCall: 0.2);
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 0.25));

        var replies = await InFlight(client, inner, n: 10, expectEntered: 10);

        Assert.All(replies, r => Assert.Equal(ProviderVerdict.Ok, r.Verdict));
        Assert.Equal(2.0, (await tracker.TotalAsync()).CostUsd, 5);
    }

    [Fact]
    public async Task A_call_that_THROWS_releases_its_hold_and_records_nothing()
    {
        var tracker = new InMemoryUsageTracker();
        var inner = new GatedTextClient(0.2) { Throws = new InvalidOperationException("boom") };
        inner.Open();
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 1.0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(Ask()));

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync());
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task A_CANCELLED_call_releases_its_hold()
    {
        var tracker = new InMemoryUsageTracker();
        var inner = new GatedTextClient(0.2); // never opened: the call waits until cancelled
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 1.0));
        using var cts = new CancellationTokenSource();

        var call = client.CompleteAsync(Ask(), cts.Token);
        while (inner.Entered == 0) await Task.Delay(5);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task A_stream_reconciles_with_its_final_usage()
    {
        var tracker = new InMemoryUsageTracker();
        var inner = new GatedTextClient(0.3);
        inner.Open();
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 1.0));

        await foreach (var _ in client.StreamAsync(Ask())) { }

        Assert.Equal(0.3, (await tracker.TotalAsync()).CostUsd, 5);
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task A_stream_abandoned_before_its_final_chunk_releases_its_hold()
    {
        var tracker = new InMemoryUsageTracker();
        var inner = new GatedTextClient(0.3);
        inner.Open();
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 1.0));

        await foreach (var _ in client.StreamAsync(Ask())) break;

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync());
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task A_refused_stream_says_so_in_one_error_chunk()
    {
        var tracker = new InMemoryUsageTracker();
        await tracker.RecordAsync("default", new ProviderUsage(CostUsd: 0.9));
        var inner = new GatedTextClient(0.3);
        var client = new BudgetedTextClient(inner, tracker, Options(cap: 1.0, hold: 0.5));

        var chunks = new List<TextChunk>();
        await foreach (var c in client.StreamAsync(Ask())) chunks.Add(c);

        var only = Assert.Single(chunks);
        Assert.Equal(ProviderVerdict.Refused, only.Verdict);
        Assert.Contains("cost budget", only.Detail);
        Assert.Equal(0, inner.Entered);
    }

    // ---- the generic router's governance and the media router share the gate --------------------------

    private sealed class ReportingVectorProvider : IVectorProvider
    {
        public string Id => "v";
        public bool IsAvailable => true;
        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
            Task.FromResult(VectorResponse.Success([[1f]], usage: new ProviderUsage(7, CostUsd: 0.1)));
    }

    private sealed class RefusingLimiter : Lyntai.Inference.RateLimiting.IRateLimiter
    {
        public Task<bool> AcquireAsync(string consumer, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static ProviderRouter<VectorRequest, VectorResponse> VectorRouter(
        IUsageTracker tracker, LyntaiOptions options, Lyntai.Inference.RateLimiting.IRateLimiter? limiter = null) =>
        new ProviderRouterFactory(new DeadHostTracker(), options: options, tracker: tracker, limiter: limiter)
            .For<VectorRequest, VectorResponse>([new ReportingVectorProvider()], VectorResponse.Failure,
                c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));

    [Fact]
    public async Task The_router_reconciles_a_held_call_with_the_usage_the_wire_reported()
    {
        var tracker = new InMemoryUsageTracker();

        var reply = await VectorRouter(tracker, Options(cap: 1.0, hold: 0.5)).CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.True(reply.IsOk);
        Assert.Equal(7, (await tracker.TotalAsync("memory")).InputTokens);
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task A_rate_refusal_AFTER_the_hold_was_granted_releases_it()
    {
        var tracker = new InMemoryUsageTracker();

        var reply = await VectorRouter(tracker, Options(cap: 1.0, hold: 1.0), new RefusingLimiter())
            .CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.Equal(ProviderVerdict.RateLimited, reply.Verdict);
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task The_router_refuses_a_hold_that_does_not_fit_without_asking_a_backend()
    {
        var tracker = new InMemoryUsageTracker();
        await tracker.RecordAsync("memory", new ProviderUsage(CostUsd: 0.6));

        var reply = await VectorRouter(tracker, Options(cap: 1.0, hold: 0.5)).CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
        Assert.Equal(0, (await tracker.TotalAsync("memory")).InputTokens);
    }

    private static MediaRequest Image() => new() { Kind = ProviderKinds.Image, Prompt = "a red square" };

    [Fact]
    public async Task A_render_reconciles_its_hold_with_what_it_cost()
    {
        var tracker = new InMemoryUsageTracker();
        var backend = new FakeGenerationProvider { Id = "sd", CostUsd = 0.4 };
        var router = new BudgetedMediaRouter(new MediaRouter([backend]), tracker, Options(cap: 1.0, hold: 1.0));

        var result = await router.GenerateAsync([new("sd")], Image());

        Assert.Equal("sd", result.ProviderId);
        Assert.Equal(0.4, (await tracker.TotalAsync()).CostUsd, 5);
        Assert.True(await NothingHeld(tracker, 1.0));
    }

    [Fact]
    public async Task A_render_that_failed_releases_its_hold()
    {
        var tracker = new InMemoryUsageTracker();
        var backend = new FakeGenerationProvider { Id = "sd", Throws = new InvalidOperationException("down") };
        var router = new BudgetedMediaRouter(new MediaRouter([backend]), tracker, Options(cap: 1.0, hold: 1.0));

        await router.GenerateAsync([new("sd")], Image());

        Assert.Equal(UsageTotals.Empty, await tracker.TotalAsync());
        Assert.True(await NothingHeld(tracker, 1.0));
    }
}
