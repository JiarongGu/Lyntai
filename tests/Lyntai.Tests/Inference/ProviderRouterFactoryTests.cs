using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

/// <summary>What the factory is FOR: the bookkeeping survives between calls.
///
/// <para>A router is cheap to rebuild and is rebuilt per call on these paths. The tracker is not — a
/// consumer that rebuilds it along with the router can never bench a failing backend, because the knowledge
/// that it is failing is thrown away in between. That was the state of the vector and score kinds after
/// <b>D153</b> gave them the mechanism: a backend answering 429 was asked again on the very next recall.</para>
///
/// <para>The pair of tests below is deliberately a BEFORE and an AFTER over the same scenario, because the
/// only thing that distinguishes them is where the tracker lives.</para></summary>
public class ProviderRouterFactoryTests
{
    private sealed class StubVectorProvider(string id) : IVectorProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public bool Fails { get; init; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Fails
                ? Task.FromResult(VectorResponse.Failure(ProviderVerdict.Failed, "down"))
                : Task.FromResult(VectorResponse.Success([[1f, 2f]]));
        }
    }

    // TWO capable backends on purpose: RoutingPolicy.ExemptSoleCandidate defaults to true, so a lone
    // candidate is asked even while benched — a one-backend scenario would prove nothing either way.
    private static (StubVectorProvider Bad, StubVectorProvider Good, IModelProvider[] All) Backends()
    {
        var bad = new StubVectorProvider("bad") { Fails = true };
        var good = new StubVectorProvider("good");
        return (bad, good, [bad, good]);
    }

    private static Task<VectorResponse> CallAsync(ProviderRouter<VectorRequest, VectorResponse> router) =>
        router.CallAsync(new VectorRequest(["hello"]));

    private static ProviderRouter<VectorRequest, VectorResponse> Bare(IEnumerable<IModelProvider> providers) =>
        new(providers, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));

    [Fact]
    public async Task A_router_rebuilt_per_call_with_its_own_tracker_asks_the_failing_backend_every_time()
    {
        // The defect this closes, pinned so it cannot come back as an "optimisation": rebuilding the
        // bookkeeping with the router is indistinguishable from having none.
        var (bad, good, all) = Backends();

        for (var i = 0; i < 3; i++) Assert.True((await CallAsync(Bare(all))).IsOk);

        Assert.Equal(3, bad.Calls);
        Assert.Equal(3, good.Calls);
    }

    [Fact]
    public async Task The_factory_shares_ONE_tracker_so_a_failing_backend_is_benched_for_the_next_call()
    {
        var (bad, good, all) = Backends();
        var factory = new ProviderRouterFactory(new DeadHostTracker(threshold: 1));

        // a router per call, exactly as the call sites build one — only the tracker is shared
        for (var i = 0; i < 3; i++)
        {
            var router = factory.For<VectorRequest, VectorResponse>(
                all, VectorResponse.Failure,
                c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
            Assert.True((await CallAsync(router)).IsOk);
        }

        Assert.Equal(1, bad.Calls);   // asked once, benched thereafter
        Assert.Equal(3, good.Calls);  // and the healthy one still answers every call
    }

    private sealed class FlakyVectorProvider(string id) : IVectorProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
            Task.FromResult(++Calls == 1
                ? VectorResponse.Failure(ProviderVerdict.Failed, "transient")
                : VectorResponse.Success([[1f, 2f]]));
    }

    private sealed class StubScoreProvider(string id) : IScoreProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Score],
            Operations = [ProviderOperation.Complete],
        };

        public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default) =>
            Task.FromResult(ScoreResponse.Failure(ProviderVerdict.Failed, "down"));
    }

    [Fact]
    public async Task The_CONFIGURED_routing_policy_reaches_a_factory_built_router()
    {
        // ConfigureRouting used to reach chat alone: the factory never passed the configured policy, so
        // vector and score always routed on RoutingPolicy's defaults and an operator's retries were
        // silently ignored for those kinds.
        var flaky = new FlakyVectorProvider("flaky");
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 1);
        var factory = new ProviderRouterFactory(new DeadHostTracker(), options: options);

        var router = factory.For<VectorRequest, VectorResponse>(
            new IModelProvider[] { flaky }, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));

        Assert.True((await CallAsync(router)).IsOk); // failed once, retried per the CONFIGURED policy
        Assert.Equal(2, flaky.Calls);
    }

    [Fact]
    public async Task An_EXPLICIT_policy_still_wins_over_the_configured_one()
    {
        var flaky = new FlakyVectorProvider("flaky");
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 1);
        var factory = new ProviderRouterFactory(new DeadHostTracker(), options: options);

        var router = factory.For<VectorRequest, VectorResponse>(
            new IModelProvider[] { flaky }, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text),
            policy: new RoutingPolicy()); // no retries

        Assert.False((await CallAsync(router)).IsOk);
        Assert.Equal(1, flaky.Calls);
    }

    [Fact]
    public async Task A_score_failure_never_benches_a_vector_backend_sharing_the_same_id()
    {
        // Reachable in a default configuration: AddOnnxProvider defaults Id = "onnx", so an embedder and a
        // reranker both keyed on the bare id shared one bench — a failing reranker silenced recalls. The
        // factory now scopes cooldown keys per closed shape ("vector::onnx" / "score::onnx"), the same rule
        // MediaRouter already applies with its "generation::" prefix.
        var tracker = new DeadHostTracker(threshold: 1);
        var factory = new ProviderRouterFactory(tracker);
        var vector = new StubVectorProvider("onnx");
        var score = new StubScoreProvider("onnx");
        IModelProvider[] all = [vector, score];

        var scoreRouter = factory.For<ScoreRequest, ScoreResponse>(all, ScoreResponse.Failure,
            c => c.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        Assert.False((await scoreRouter.CallAsync(new ScoreRequest("q", ["d"]))).IsOk); // benches score::onnx

        var vectorRouter = factory.For<VectorRequest, VectorResponse>(all, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        var reply = await CallAsync(vectorRouter);

        Assert.True(reply.IsOk);          // the embedder was ASKED — the reranker's bench is not its bench
        Assert.Equal(1, vector.Calls);
    }

    [Fact]
    public async Task A_null_provider_set_routes_to_nothing_rather_than_throwing()
    {
        // A consumer with nothing registered is an ordinary state on these paths — the memory seams pass
        // whatever the container holds, which may be empty.
        var factory = new ProviderRouterFactory(new DeadHostTracker());
        var router = factory.For<VectorRequest, VectorResponse>(null, VectorResponse.Failure);

        Assert.False(router.CanServe());
        Assert.False((await CallAsync(router)).IsOk);
    }

    [Fact]
    public void A_null_synthesize_is_refused_at_the_factory_rather_than_inside_the_router()
    {
        var factory = new ProviderRouterFactory(new DeadHostTracker());
        Assert.Throws<ArgumentNullException>(
            () => factory.For<VectorRequest, VectorResponse>([], null!));
    }
}
