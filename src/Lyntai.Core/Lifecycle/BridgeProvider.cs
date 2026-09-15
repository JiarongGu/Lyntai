using Lyntai.Llm;

namespace Lyntai.Lifecycle;

/// <summary>A backend built from a FUNCTION rather than a class — the general shape of bridging something
/// that can already answer into this library.
///
/// <para><b>It exists because the specific bridge was the wrong unit.</b> A 473-line adapter for
/// <c>Microsoft.Extensions.AI</c> shipped for a year, cost every consumer a 654 KB dependency, and was used
/// by nothing (<c>docs/DECISIONS.md</c> D146). What a caller actually needs is not a bridge to one
/// ecosystem: it is a way to say "here is a function that answers; route it like any other backend". That
/// needs no third-party type, so it belongs in Core, where a dependency-bearing adapter never could
/// (**D147**).</para>
///
/// <para><b>Bridging any client is now a lambda.</b> Whatever the SDK — an <c>IChatClient</c>, a vendor's
/// own client, an in-house HTTP service — the consumer owns the mapping they actually need and the library
/// owns routing, fallback, cooldown, admission and the ops layer around it.</para>
///
/// <para><b>What it does NOT do, deliberately:</b> nothing is inferred. It declares exactly the operations
/// it was given a function for, so a bridge with no stream delegate reports no
/// <see cref="ProviderOperation.Stream"/> and a router never offers it one — the same rule every other
/// backend follows. A caller wanting tool calls, embeddings or scores declares them the same way, by
/// supplying the delegate and the capability that matches.</para></summary>
internal sealed class BridgeProvider(
    string id,
    ProviderCapabilities capabilities,
    Func<LlmRequest, CancellationToken, Task<LlmReply>> complete,
    Func<LlmRequest, CancellationToken, IAsyncEnumerable<LlmChunk>>? stream) : IModelProvider
{
    public string Id => id;

    public ProviderCapabilities Capabilities => capabilities;

    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        complete(req, ct);

    /// <summary>Falls back to <see cref="IModelProvider"/>'s own Unsupported default when no stream
    /// delegate was supplied — which the declared capabilities already tell a router, so this is the
    /// belt-and-braces half rather than the gate.</summary>
    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        stream is null
            ? ((IModelProvider)this).StreamUnsupported()
            : stream(req, ct);
}

/// <summary>Reaches <see cref="IModelProvider"/>'s default body for a stream nobody supplied, which a
/// class implementing the member cannot otherwise call.</summary>
internal static class BridgeProviderDefaults
{
    public static IAsyncEnumerable<LlmChunk> StreamUnsupported(this IModelProvider provider) =>
        One(LlmChunk.Error(ProviderVerdict.Unsupported,
            $"{provider.Id} does not serve StreamAsync — no stream delegate was supplied to AddBridgeProvider."));

    private static async IAsyncEnumerable<LlmChunk> One(LlmChunk chunk)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return chunk;
    }
}
