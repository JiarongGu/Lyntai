using Lyntai.Tests.Fakes;
using Lyntai.Inference;

namespace Lyntai.Tests.Vectors;

/// <summary>Routing for the Vector capability: the capability filter, and the failover.
///
/// <para>Routing, not one resolved backend, is what keeps a failing backend from taking the whole recall
/// path with it and a second registration from silently replacing the first (D129). The routing is a
/// helper over the providers (D151): the behaviour belongs to routing, not to a type.</para>
/// </summary>
public class EmbeddingRoutingTests
{
    private sealed class StubVectorProvider(string id) : IVectorProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable { get; set; } = true;
        public int Calls { get; private set; }
        public Exception? Throws { get; set; }
        public EmbeddingRole? SawRole { get; private set; }

        public ProviderCapabilities Capabilities { get; set; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        /// <summary>Throws rather than returning a verdict on purpose: these tests are about what routing
        /// does with a backend that FAILS, and an escaping exception is the harsher of the two paths — the
        /// router has to classify it rather than being handed a verdict.</summary>
        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
        {
            Calls++;
            SawRole = request.Role;
            if (Throws is not null) throw Throws;
            return Task.FromResult(VectorResponse.Success([[1f, 2f]]));
        }
    }

    /// <summary>A chat-only backend: it PRODUCES text, not vectors.</summary>
    private static StubVectorProvider ChatOnly(string id) => new(id)
    {
        Capabilities = new ProviderCapabilities
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = [ProviderOperation.Complete],
        },
    };

    private static IModelProvider[] Router(params IModelProvider[] providers) => providers;

    [Fact]
    public async Task Routes_to_the_backend_that_DECLARES_embed_and_never_calls_the_others()
    {
        // The capability filter is the whole point: a chat backend must not be asked to embed and then
        // report Unsupported — it must not be asked at all.
        var chat = ChatOnly("chat");
        var vectorProvider = new StubVectorProvider("embed");

        var vectors = await EmbeddingRouting.EmbedAsync(Router(chat, vectorProvider), ["x"]);

        Assert.Single(vectors);
        Assert.Equal(1, vectorProvider.Calls);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task FALLS_OVER_to_the_next_vector_backend_when_one_fails()
    {
        // A single vector backend slot cannot do this: a later registration would simply win.
        var broken = new StubVectorProvider("broken") { Throws = new HttpRequestException("socket died") };
        var healthy = new StubVectorProvider("healthy");

        var vectors = await EmbeddingRouting.EmbedAsync(Router(broken, healthy), ["x"]);

        Assert.Single(vectors);
        Assert.Equal(1, broken.Calls);
        Assert.Equal(1, healthy.Calls);
    }

    [Fact]
    public async Task Skips_a_backend_reporting_itself_UNAVAILABLE_without_calling_it()
    {
        var down = new StubVectorProvider("down") { IsAvailable = false };
        var up = new StubVectorProvider("up");

        await EmbeddingRouting.EmbedAsync(Router(down, up), ["x"]);

        Assert.Equal(0, down.Calls);
        Assert.Equal(1, up.Calls);
    }

    [Fact]
    public async Task Carries_the_EMBEDDING_ROLE_through_rather_than_flattening_it()
    {
        // Asymmetric models score materially worse when both sides are embedded identically, and the role
        // is the one fact a backend cannot work out for itself — a router that dropped it would be silent.
        var vectorProvider = new StubVectorProvider("embed");

        await EmbeddingRouting.EmbedAsync(Router(vectorProvider), ["x"], EmbeddingRole.Query);

        Assert.Equal(EmbeddingRole.Query, vectorProvider.SawRole);
    }

    [Fact]
    public async Task Says_what_is_MISSING_when_nothing_declares_embed()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EmbeddingRouting.EmbedAsync(Router(ChatOnly("chat")), ["x"]));

        Assert.Contains("ProviderKinds.Vector", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_the_LAST_reason_when_every_backend_failed_rather_than_a_bare_message()
    {
        // A failure list that loses every cause is unactionable. The cause survives as a CLASSIFIED
        // VERDICT plus the backend's own words rather than as an inner exception (D153) — which is
        // strictly more usable, because a verdict is what routing and a host can act on where an exception
        // type is only something to read.
        var a = new StubVectorProvider("a") { Throws = new HttpRequestException("a died") };
        var b = new StubVectorProvider("b") { Throws = new TimeoutException("b timed out") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => EmbeddingRouting.EmbedAsync(Router(a, b), ["x"]));

        Assert.Contains("Every embedding backend failed", error.Message, StringComparison.Ordinal);
        Assert.Contains("b timed out", error.Message, StringComparison.Ordinal);   // the LAST backend's words
        Assert.DoesNotContain("a died", error.Message, StringComparison.Ordinal);  // …not the first one's
    }

    [Fact]
    public async Task A_callers_CANCELLATION_propagates_instead_of_advancing_to_the_next_backend()
    {
        // The one throw fallback must not swallow: falling over on the caller's own cancel would call
        // every remaining backend after the caller asked to stop.
        var first = new StubVectorProvider("a") { Throws = new OperationCanceledException() };
        var second = new StubVectorProvider("b");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EmbeddingRouting.EmbedAsync(Router(first, second), ["x"], EmbeddingRole.Document, ct: cts.Token));

        Assert.Equal(0, second.Calls);
    }
}
