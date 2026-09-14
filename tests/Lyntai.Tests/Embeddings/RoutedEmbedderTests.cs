using Lyntai.Embeddings;
using Lyntai.Lifecycle;

namespace Lyntai.Tests.Embeddings;

/// <summary>The embedding FRONT DOOR — the thing embeddings never had.
///
/// <para>Before D129 the <c>IEmbedder</c> a consumer resolved WAS a backend, so a failing embedder took the
/// whole recall path with it and a second registration silently replaced the first. These tests are the
/// behaviour that split bought; none of them was expressible against the old single slot.</para></summary>
public class RoutedEmbedderTests
{
    private sealed class FakeEmbeddingProvider(string id) : IModelProvider
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

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default)
        {
            Calls++;
            SawRole = role;
            if (Throws is not null) throw Throws;
            return Task.FromResult<IReadOnlyList<float[]>>([[1f, 2f]]);
        }
    }

    /// <summary>A chat-only backend: it PRODUCES text, not vectors.</summary>
    private static FakeEmbeddingProvider ChatOnly(string id) => new(id)
    {
        Capabilities = new ProviderCapabilities
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = [ProviderOperation.Complete],
        },
    };

    private static RoutedEmbedder Router(params IModelProvider[] providers) => new(providers);

    [Fact]
    public async Task Routes_to_the_backend_that_DECLARES_embed_and_never_calls_the_others()
    {
        // The capability filter is the whole point: a chat backend must not be asked to embed and then
        // report Unsupported — it must not be asked at all.
        var chat = ChatOnly("chat");
        var embedder = new FakeEmbeddingProvider("embed");

        var vectors = await Router(chat, embedder).EmbedAsync(["x"]);

        Assert.Single(vectors);
        Assert.Equal(1, embedder.Calls);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task FALLS_OVER_to_the_next_embedder_when_one_fails()
    {
        // This is the capability the single IEmbedder slot could not have — its own doc admitted
        // "there is one embedder slot, so a later registration wins".
        var broken = new FakeEmbeddingProvider("broken") { Throws = new HttpRequestException("socket died") };
        var healthy = new FakeEmbeddingProvider("healthy");

        var vectors = await Router(broken, healthy).EmbedAsync(["x"]);

        Assert.Single(vectors);
        Assert.Equal(1, broken.Calls);
        Assert.Equal(1, healthy.Calls);
    }

    [Fact]
    public async Task Skips_a_backend_reporting_itself_UNAVAILABLE_without_calling_it()
    {
        var down = new FakeEmbeddingProvider("down") { IsAvailable = false };
        var up = new FakeEmbeddingProvider("up");

        await Router(down, up).EmbedAsync(["x"]);

        Assert.Equal(0, down.Calls);
        Assert.Equal(1, up.Calls);
    }

    [Fact]
    public async Task Carries_the_EMBEDDING_ROLE_through_rather_than_flattening_it()
    {
        // Asymmetric models score materially worse when both sides are embedded identically, and the role
        // is the one fact a backend cannot work out for itself — a router that dropped it would be silent.
        var embedder = new FakeEmbeddingProvider("embed");

        await Router(embedder).EmbedAsync(["x"], EmbeddingRole.Query);

        Assert.Equal(EmbeddingRole.Query, embedder.SawRole);
    }

    [Fact]
    public async Task Says_what_is_MISSING_when_nothing_declares_embed()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Router(ChatOnly("chat")).EmbedAsync(["x"]));

        Assert.Contains("ProviderKinds.Vector", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_the_LAST_reason_when_every_backend_failed_rather_than_a_bare_message()
    {
        // A failure list that loses every cause is unactionable; the inner exception is what a developer
        // reads first.
        var a = new FakeEmbeddingProvider("a") { Throws = new HttpRequestException("a died") };
        var b = new FakeEmbeddingProvider("b") { Throws = new TimeoutException("b timed out") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Router(a, b).EmbedAsync(["x"]));

        Assert.Contains("Every embedding backend failed", error.Message, StringComparison.Ordinal);
        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Fact]
    public async Task A_callers_CANCELLATION_propagates_instead_of_advancing_to_the_next_backend()
    {
        // The one throw fallback must not swallow: falling over on the caller's own cancel would call
        // every remaining backend after the caller asked to stop.
        var first = new FakeEmbeddingProvider("a") { Throws = new OperationCanceledException() };
        var second = new FakeEmbeddingProvider("b");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Router(first, second).EmbedAsync(["x"], EmbeddingRole.Document, cts.Token));

        Assert.Equal(0, second.Calls);
    }
}
