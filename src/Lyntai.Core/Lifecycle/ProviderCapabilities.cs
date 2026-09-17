namespace Lyntai.Lifecycle;

/// <summary>What a backend can be ASKED to do, in the vocabulary every domain shares.
///
/// <para>One entry per method a provider can serve, so a declaration maps to a dispatch with nothing in
/// between. Chat completion and an inline image render are both <see cref="Complete"/> — they differ by
/// <see cref="ProviderCapabilities.Produces"/>, which is the CONTENT type, not by the operation.</para></summary>
public enum ProviderOperation
{
    /// <summary>One request, one reply. Chat completion, an inline render, a batch of vectors.</summary>
    Complete = 0,

    /// <summary>Incremental delivery — tokens, frames, audio.</summary>
    Stream = 1,

    /// <summary>Submit now, collect later: the backend returns a handle rather than a result.
    ///
    /// <para><b>Named <c>Queued</c> rather than <c>Job</c>, because <c>Job</c> already means something else
    /// here</b> — <c>Lyntai.Jobs</c> is the durable job queue an APPLICATION runs (<c>IJobStore</c>,
    /// <c>IJobQueue</c>, <c>IJobHandler</c>, <c>IJobRunner</c>). This is a delivery mode of one backend
    /// call, and the two compose rather than being the same thing: an app's job may submit a queued
    /// generation and poll it later.</para></summary>
    Queued = 2,
}

/// <summary>The content types this library routes over. Open by construction —
/// <see cref="ProviderCapabilities.Produces"/> is a string list — so an adapter may declare one that is not
/// here; these are the names the library itself uses.
///
/// <para>Deliberately CONSTANTS rather than an enum, so a backend offering a medium nobody has modelled yet
/// fits without a breaking contract change — the same reasoning as the free-form <c>Method</c>/<c>Version</c>
/// values in <c>docs/DECISIONS.md</c> D20. The media half was a SECOND copy of this list in
/// <c>Lyntai.Generation</c> until <b>D140</b>, with backends setting one in a request and the other in their
/// capabilities, adjacent fields of the same record.</para></summary>
public static class ProviderKinds
{
    public const string Text = "text";

    /// <summary>A still image.</summary>
    public const string Image = "image";

    /// <summary>Moving pictures, with or without an audio track.</summary>
    public const string Video = "video";

    /// <summary>Speech, music or sound effects.</summary>
    public const string Audio = "audio";

    /// <summary>A 3D asset (mesh/scene). <b>No shipped backend declares this kind, and a mesh does NOT chain
    /// into <see cref="Image"/> or <see cref="Video"/>:</b> no image or video backend accepts a mesh, so the
    /// 3d→image edge is a RASTERIZATION rather than a generation, and this platform performs none. A mesh
    /// backend's own <c>image/*</c> artifacts are usually UV texture atlases — a flattened skin, not a view of
    /// the object — so chaining one through <c>GenerationArtifact.ToInput</c> renders fine and is wrong. The
    /// kind is declared so a backend serving it needs no contract change.</summary>
    public const string Model3d = "3d";

    /// <summary>What a RERANKER produces: a relevance score for a (query, document) pair. Unbounded and
    /// comparable only WITHIN one call — a cross-encoder's logit is not a probability and two models do not
    /// share a scale, so a caller ranks by it and must never threshold on it across backends.</summary>
    public const string Score = "score";

    /// <summary>What an EMBEDDER produces. It is a kind rather than an operation, and that is the whole
    /// correction: a vector backend accepts text and produces vectors, exactly as an image backend accepts text
    /// and produces an image. Treating it as its own operation made it the one member that had to be
    /// explained away (<c>docs/DECISIONS.md</c> D130).</summary>
    public const string Vector = "vector";
}

/// <summary>What a backend DECLARES it serves — read by a router BEFORE anything is spent.
///
/// <para><b>Capability is DATA, not a type hierarchy.</b> A backend that cannot do something declares so
/// and the router never dispatches to it; expressing the same thing as interfaces would need one type per
/// combination and still could not answer "which of your models" (<c>docs/DECISIONS.md</c> D125).</para>
///
/// <para><b>The two defaults point in OPPOSITE directions, on purpose.</b> An empty
/// <see cref="Produces"/> or <see cref="Operations"/> serves NOTHING — a backend that forgot to declare
/// must be skipped rather than handed every request. An empty <see cref="Models"/> serves ANY — an
/// aggregator fronts hundreds behind one id and cannot enumerate them.</para>
///
/// <para><b><see cref="Produces"/> is a LIST, and that is load-bearing.</b> One backend can serve several
/// output kinds: an OpenAI-compatible host answers <c>/chat/completions</c> AND <c>/embeddings</c>, so it
/// declares <c>[text, vector]</c> and implements both methods off one configuration. Modelling embedding as
/// its own operation made that inexpressible (<c>docs/DECISIONS.md</c> D130).</para></summary>
public sealed record ProviderCapabilities
{
    /// <summary>The content types this backend takes IN (<see cref="ProviderKinds"/>), matched
    /// case-insensitively. A chat model and a vector backend both accept <c>text</c>; an image-to-video backend
    /// accepts <c>text</c> and <c>image</c>.</summary>
    public IReadOnlyList<string> Accepts { get; init; } = [];

    /// <summary>The content types this backend puts OUT. <b>This is the axis that separates the backend
    /// classes</b>: text out is a chat model, <see cref="ProviderKinds.Image"/> out is a renderer,
    /// <see cref="ProviderKinds.Vector"/> out is a vector backend. Empty serves nothing.</summary>
    public IReadOnlyList<string> Produces { get; init; } = [];

    /// <summary>How a result is DELIVERED — inline, streamed, or as a job. Not what it is; that is
    /// <see cref="Produces"/>. Empty serves nothing.</summary>
    public IReadOnlyList<ProviderOperation> Operations { get; init; } = [];

    /// <summary>The models served, or EMPTY for "any" — see the type's remarks on the opposite default.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>Whether a request may carry input artifacts (an init image, a reference clip). A backend
    /// that accepts the call and ignores them returns a plausible, wrong result.</summary>
    public bool SupportsInputs { get; init; }

    /// <summary>Whether the backend sends tools to the model and surfaces its calls on
    /// <c>LlmReply.ToolCalls</c>. Coarse — provider-level, not per-model: a model that ignores tools just
    /// answers in prose, which the tool loop treats as a final answer.</summary>
    public bool SupportsToolCalls { get; init; }

    /// <summary>Whether the backend's STREAM carries native tool calls.
    ///
    /// <para><b>Separate from <see cref="SupportsToolCalls"/> because the two are genuinely independent</b>:
    /// a backend can surface calls on a buffered reply while its stream drops them, which is what every
    /// provider here did before 3.0. Answering one for the other makes an agentic turn look like a plain
    /// answer — the model asks for a tool, no call chunk arrives, and the loop reports the empty prose as
    /// its final answer. That failure is silent, which is why it is its own flag.</para></summary>
    public bool SupportsStreamingToolCalls { get; init; }

    /// <summary>Free-form declared limits — context window, maximum dimension, rate. Advisory: nothing here
    /// enforces them, they are for a caller or an agent tool deciding what to ask for.</summary>
    public IReadOnlyDictionary<string, string> Limits { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Can this backend serve this request? Every clause fails CLOSED.</summary>
    /// <param name="produces">The content type WANTED back (<see cref="ProviderKinds"/>) — the axis that
    /// picks the backend class.</param>
    /// <param name="operation">How it must be delivered.</param>
    /// <param name="accepts">The content type going IN, when the caller needs to pin it. Null skips the
    /// check, which is right for the common text-in case.</param>
    /// <param name="model">The model asked for, or null for the backend's default.</param>
    /// <param name="hasInputs">Whether the request carries input artifacts.</param>
    public bool Supports(
        string produces, ProviderOperation operation,
        string? accepts = null, string? model = null, bool hasInputs = false)
    {
        if (!Produces.Contains(produces, StringComparer.OrdinalIgnoreCase)) return false;
        if (!Operations.Contains(operation)) return false;
        if (accepts is { Length: > 0 } && !Accepts.Contains(accepts, StringComparer.OrdinalIgnoreCase)) return false;
        if (hasInputs && !SupportsInputs) return false;
        if (model is { Length: > 0 } && Models.Count > 0 &&
            !Models.Contains(model, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }
}
