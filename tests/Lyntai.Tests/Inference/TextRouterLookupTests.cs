using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

/// <summary>The router's provider table read through a lookup, once per call — the seam a run-time registry swaps
/// snapshots behind, so a call never routes over half an edit.</summary>
public class TextRouterLookupTests
{
    private static TextRequest Req => new() { Messages = [TextMessage.User("hi")] };

    private static IReadOnlyDictionary<string, IModelProvider> Table(params IModelProvider[] providers) =>
        ProviderLookup.ById(providers);

    private static FakeTextProvider Answering(string id)
    {
        var provider = new FakeTextProvider(id);
        for (var i = 0; i < 4; i++) provider.Replies.Enqueue(new TextResponse($"from {id}", ProviderVerdict.Ok));
        return provider;
    }

    [Fact]
    public async Task Each_call_routes_over_the_table_it_read()
    {
        var (a, b) = (Answering("a"), Answering("b"));
        var table = Table(a);
        var router = new TextRouter(() => table, new DeadHostTracker(), new LyntaiOptions());

        var first = await router.CompleteAsync([new ProviderCandidate("b"), new ProviderCandidate("a")], Req);
        table = Table(a, b);
        var second = await router.CompleteAsync([new ProviderCandidate("b"), new ProviderCandidate("a")], Req);

        Assert.Equal("from a", first.Text);                                 // b was not in the first table
        Assert.Equal("from b", second.Text);
    }

    [Fact]
    public async Task A_call_reads_the_table_exactly_once_even_through_a_live_route()
    {
        var reads = 0;
        var a = Answering("a");
        var router = new TextRouter(() => { reads++; return Table(a); }, new DeadHostTracker(), new LyntaiOptions(),
            modelRouting: new FixedRoute([new ProviderCandidate("a")]));

        await router.CompleteAsync([new ProviderCandidate("a")], Req);
        Assert.Equal(1, reads);

        await foreach (var _ in router.StreamAsync([new ProviderCandidate("a")], Req)) { }
        Assert.Equal(2, reads);

        await router.GetCapabilitiesAsync([new ProviderCandidate("a")], Req);
        Assert.Equal(3, reads);
    }

    private sealed class FixedRoute(IReadOnlyList<ProviderCandidate> route) : IModelRoutingStore
    {
        public Task<IReadOnlyList<ProviderCandidate>> GetRouteAsync(string consumer, CancellationToken ct = default) =>
            Task.FromResult(route);
    }
}
