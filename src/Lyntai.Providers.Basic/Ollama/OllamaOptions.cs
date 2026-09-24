using Lyntai.Inference;

namespace Lyntai.Providers.Ollama;

/// <summary>One Ollama server, spoken to on its NATIVE surface — <c>/api/chat</c> for text,
/// <c>/api/embed</c> for vectors. Ollama also exposes an OpenAI-shaped <c>/v1</c> surface; that one is a
/// different wire and a different provider — pass the <c>/v1</c> base to <c>AddHttpProvider</c> instead.
///
/// <para><b>One registration is one backend</b>: a chat model and an embedding model on the same server are
/// registered twice, under two ids, so a trace can say which one answered (<c>docs/DECISIONS.md</c>
/// D133).</para></summary>
public sealed class OllamaOptions
{
    /// <summary>The server ROOT, e.g. <c>http://localhost:11434</c> — never its <c>/v1</c> OpenAI-shaped
    /// surface, which speaks a different wire (see the class doc).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Bearer token for a proxied or gated server; null (the default) for the usual keyless local
    /// install.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model this backend serves, e.g. <c>llama3.1</c>, <c>nomic-embed-text</c>. Used when
    /// neither the request nor the candidate pins one.</summary>
    public string? Model { get; set; }

    /// <summary>What this backend puts out — <see cref="ProviderKinds.Text"/> (default) posts to
    /// <c>/api/chat</c>, <see cref="ProviderKinds.Vector"/> to <c>/api/embed</c>. Ollama serves no rerank
    /// surface, so <see cref="ProviderKinds.Score"/> is REFUSED at construction rather than guessed into a
    /// route that 404s on the first call.
    /// <para>A single value rather than a list, because one registration is one backend (see the class
    /// doc).</para></summary>
    public string Produces { get; set; } = ProviderKinds.Text;

    /// <summary>The context window (<c>options.num_ctx</c> on the native wire), when configured. Applies to
    /// every completion this registration sends — the knob lives here, on the one backend whose wire can
    /// carry it, so it can no longer be set on a backend that silently ignores it.</summary>
    public int? ContextSize { get; set; }

    /// <summary>Max inputs per <c>/api/embed</c> request when serving <see cref="ProviderKinds.Vector"/>; a
    /// larger call list is split into this many at a time. <c>0</c> (default) sends the whole batch in a
    /// single request.</summary>
    public int BatchSize { get; set; }

    /// <summary>Prepended, VERBATIM, to text embedded as <see cref="Lyntai.Inference.EmbeddingRole.Document"/>
    /// — the storing side. Null or empty (the default) sends the text unchanged. Asymmetric models want a
    /// per-side instruction and score materially worse without it; which spelling, if any, is the model's
    /// own (nomic wants <c>"search_document: "</c>, the E5 family <c>"passage: "</c>) — the library supplies
    /// no default. Include the trailing space if the model wants one, and re-index rather than mixing
    /// corpora embedded under different prefixes.</summary>
    public string? DocumentPrefix { get; set; }

    /// <summary>Prepended, VERBATIM, to text embedded as <see cref="Lyntai.Inference.EmbeddingRole.Query"/>
    /// — the searching side. Null or empty (the default) sends the text unchanged. Set independently of
    /// <see cref="DocumentPrefix"/>: a model may instruct one side only, and an unset side must stay
    /// verbatim rather than inherit the other.</summary>
    public string? QueryPrefix { get; set; }

    /// <summary>The most CHARACTERS one input may carry in an <c>/api/embed</c> request when serving
    /// <see cref="ProviderKinds.Vector"/> — the bound <c>HttpModelOptions.MaxInputChars</c> is, with the same
    /// pieces, pooling and caveat that characters only approximate tokens (see its doc). A role prefix counts
    /// inside it. Ignored for <see cref="ProviderKinds.Text"/>.
    ///
    /// <para><b>Null (the default) sends every input whole and leaves the server's own handling alone</b>:
    /// Ollama cuts an input past the model's context silently. Set, the bound governs instead: unless
    /// <see cref="Segmentation"/> truncates, every request also carries <c>truncate: false</c>, so a piece
    /// that still overflows is reported as a failed call rather than cut behind the bound. Under
    /// <see cref="InputOverflow.Truncate"/> the loss is accepted, and the server may cut further. The provider
    /// throws <see cref="ArgumentOutOfRangeException"/> when it is not positive or leaves a prefix no
    /// room.</para></summary>
    public int? MaxInputChars { get; set; }

    /// <summary>What happens to an input longer than <see cref="MaxInputChars"/>, and ignored without it
    /// (<c>docs/DECISIONS.md</c> <b>D177</b>): null — the default — or <see cref="InputOverflow.Segment"/>
    /// segments it and pools its pieces' vectors; <see cref="InputOverflow.Truncate"/> sends it cut where its
    /// first piece would end. <see cref="InputSegmentation.MaxPiecesPerInput"/> caps one input's pieces, as on
    /// the HTTP provider. <see cref="InputSegmentation.MinDocumentShare"/> does not apply: an embedder
    /// takes no query.</summary>
    public InputSegmentation? Segmentation { get; set; }
}
