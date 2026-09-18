using Lyntai.Generation;

namespace Lyntai.Inference;

/// <summary>A backend. THE provider seam — one interface for every domain this library routes over.
///
/// <para><b>What a provider serves is DATA, not a type.</b> <see cref="Capabilities"/> declares what it
/// <c>Accepts</c>, what it <c>Produces</c> and how it delivers, and a router SELECTS on that — a chat model
/// is text → text and a vector backend is text → vector, which is a difference in a LIST
/// (<c>docs/DECISIONS.md</c> D126, D127, D130).
///
/// <para><b>A seam per SIGNATURE is not a seam per kind, and the distinction is the whole of D153.</b>
/// <see cref="IVectorProvider"/> exists because its types differ, not because embedding is a different
/// class of backend — image and video share one seam for exactly the same reason. Which KIND a backend
/// serves stays in <see cref="ProviderCapabilities.Produces"/>, never in the interface it
/// implements.</para></para>
///
/// <para><b>Every operation here is DEFAULTED to <c>Unsupported</c></b>, so a backend implements only what
/// it does — a CLI chat backend overrides <see cref="CompleteAsync"/> and
/// <see cref="StreamAsync(TextRequest,CancellationToken)"/> and nothing else. A backend whose SIGNATURE
/// differs implements its own seam instead: <see cref="IVectorProvider"/> is the worked example
/// (<c>docs/DECISIONS.md</c> D153).</para>
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
    Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        Task.FromResult(new TextResponse("", ProviderVerdict.Unsupported, Detail: ProviderDefaults.NotServed(Id, nameof(CompleteAsync))));

    /// <summary>Text in, text out incrementally. Ends with exactly one terminal chunk either way.</summary>
    IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        ProviderDefaults.One(TextChunk.Error(ProviderVerdict.Unsupported, ProviderDefaults.NotServed(Id, nameof(StreamAsync))));

    /// <summary>Media in, media out — one request, artifacts back.</summary>
    Task<MediaResponse> GenerateAsync(MediaRequest request, CancellationToken ct = default) =>
        Task.FromResult(new MediaResponse(ProviderVerdict.Unsupported, [], Detail: ProviderDefaults.NotServed(Id, nameof(GenerateAsync))));

    /// <summary>Media out incrementally. Ends with exactly one terminal chunk either way.</summary>
    IAsyncEnumerable<MediaChunk> StreamAsync(MediaRequest request, CancellationToken ct = default) =>
        ProviderDefaults.One(MediaChunk.Failure(ProviderVerdict.Unsupported, ProviderDefaults.NotServed(Id, nameof(StreamAsync))));
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
