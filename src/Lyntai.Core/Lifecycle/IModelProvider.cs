using Lyntai.Generation;
using Lyntai.Llm;

namespace Lyntai.Lifecycle;

/// <summary>A backend. THE provider seam — one interface for every domain this library routes over.
///
/// <para><b>What a provider serves is DATA, not a type.</b> <see cref="Capabilities"/> declares what it
/// <c>Accepts</c>, what it <c>Produces</c> and how it delivers, and a router checks that BEFORE dispatching
/// — so a method a backend does not serve is never called, and declining one costs it no code. That is why
/// there is no chat-provider and no embedding-provider: a chat model is text → text and an embedder is
/// text → vector, which is a difference in a LIST (<c>docs/DECISIONS.md</c> D126, D127, D130).</para>
///
/// <para><b>Every operation is DEFAULTED to <c>Unsupported</c></b>, so a backend implements only what it
/// does. An embedder overrides
/// <see cref="EmbedAsync(IReadOnlyList{string},Lyntai.Embeddings.EmbeddingRole,CancellationToken)"/> and
/// nothing else; a CLI chat backend overrides <see cref="CompleteAsync"/> and
/// <see cref="StreamAsync(LlmRequest,CancellationToken)"/>.</para>
///
/// <para><b>The stateful JOB protocol is NOT here</b> — <see cref="IGenerationJobProvider"/> keeps
/// submit/poll/fetch/cancel, because that is an operation SHAPE keyed on a handle rather than a content
/// type, and it is meaningless one method at a time.</para></summary>
public interface IModelProvider : IProviderIdentity
{
    /// <summary>Stable id a router matches candidates against ("claude-cli", "openai", "onnx").</summary>
    /// <remarks><b>Declared here as well as on <see cref="IProviderIdentity"/> and <c>new</c> only to
    /// silence CS0108</b>, for the binary-compatibility reason recorded on that type: member resolution
    /// does not walk base INTERFACES, so a caller compiled against this declaration keeps binding if the
    /// base is ever restructured. Pinned by <c>ProviderIdentityTests</c>.</remarks>
    new string Id { get; }

    /// <summary>What this backend declares it can serve. <b>No default</b>: a silent capability is the one
    /// thing a provider may not leave to chance, and an empty <see cref="ProviderCapabilities"/> serves
    /// nothing, so forgetting would make a backend permanently invisible for no stated reason.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>Cheap synchronous probe — configured, binary on PATH, model loaded. Defaults to TRUE: a
    /// backend that was registered is presumed usable, and <see cref="ProbeAsync"/> is where a real check
    /// with a reason lives.</summary>
    bool IsAvailable => true;

    /// <summary>Is this backend usable right now, WITHOUT spending anything? Defaults to reporting
    /// <see cref="IsAvailable"/>, so a backend needs to override only one of the two.</summary>
    Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderProbeResult(IsAvailable));

    /// <summary>Text in, text out — one request, one reply.</summary>
    Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
        Task.FromResult(new LlmReply("", LlmVerdict.Unsupported, Detail: ProviderDefaults.NotServed(Id, nameof(CompleteAsync))));

    /// <summary>Text in, text out incrementally. Ends with exactly one terminal chunk either way.</summary>
    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
        ProviderDefaults.One(LlmChunk.Error(LlmVerdict.Unsupported, ProviderDefaults.NotServed(Id, nameof(StreamAsync))));

    /// <summary>Content in, vectors out — one per input, in order. Served by a backend declaring
    /// <see cref="ProviderKinds.Vector"/> among what it <see cref="ProviderCapabilities.Produces"/>; it is a
    /// separate METHOD only because its return type differs, not because it is a separate kind of call.</summary>
    /// <exception cref="NotSupportedException">This backend does not declare
    /// <see cref="ProviderKinds.Vector"/>. It THROWS where the others return a verdict because there is
    /// no vector that means "I could not": a zero vector compares as real and would poison a store.</exception>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new NotSupportedException(ProviderDefaults.NotServed(Id, nameof(EmbedAsync)));

    /// <summary>Embed for a known <see cref="Lyntai.Embeddings.EmbeddingRole"/>. Defaults to forwarding to
    /// the role-less overload, exactly as <see cref="Lyntai.Embeddings.IEmbedder"/> does.
    ///
    /// <para><b>It exists so the front door cannot silently drop the role.</b> Asymmetric models — E5, BGE,
    /// nomic, Arctic — are trained with a distinct instruction per side and score materially worse when
    /// both sides are embedded identically. A router that only knew the role-less overload would quietly
    /// erase the one fact a backend cannot work out for itself.</para></summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, Lyntai.Embeddings.EmbeddingRole role, CancellationToken ct = default) =>
        EmbedAsync(texts, ct);

    /// <summary>Media in, media out — one request, artifacts back.</summary>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new GenerationResult(GenerationVerdict.Unsupported, [], Detail: ProviderDefaults.NotServed(Id, nameof(GenerateAsync))));

    /// <summary>Media out incrementally. Ends with exactly one terminal chunk either way.</summary>
    IAsyncEnumerable<GenerationChunk> StreamAsync(GenerationRequest request, CancellationToken ct = default) =>
        ProviderDefaults.One(GenerationChunk.Failure(GenerationVerdict.Unsupported, ProviderDefaults.NotServed(Id, nameof(StreamAsync))));
}

/// <summary>The bodies <see cref="IModelProvider"/>'s defaults delegate to — an interface cannot hold an
/// iterator, and one phrasing of "not served" is better than eight.</summary>
internal static class ProviderDefaults
{
    public static string NotServed(string id, string operation) =>
        $"{id} does not serve {operation} — see its ProviderCapabilities.";

    /// <summary>A single-element async sequence, which is what a terminal-only stream is.</summary>
    public static async IAsyncEnumerable<T> One<T>(T value)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return value;
    }
}
