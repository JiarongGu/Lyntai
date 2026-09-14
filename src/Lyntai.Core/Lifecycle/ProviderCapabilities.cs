namespace Lyntai.Lifecycle;

/// <summary>What a backend can be ASKED to do, in the vocabulary every domain shares.
///
/// <para>One entry per method a provider can serve, so a declaration maps to a dispatch with nothing in
/// between. Chat completion and an inline image render are both <see cref="Complete"/> — they differ by
/// <see cref="ProviderCapabilities.Kinds"/>, which is the CONTENT type, not by the operation.</para></summary>
public enum ProviderOperation
{
    /// <summary>One request, one reply. Chat completion, an inline render.</summary>
    Complete = 0,

    /// <summary>Incremental delivery — tokens, frames, audio.</summary>
    Stream = 1,

    /// <summary>Submit now, collect later: the backend returns a handle rather than a result.</summary>
    Job = 2,

    /// <summary>Content to vector. The one operation that does not produce content of its own
    /// <see cref="ProviderCapabilities.Kinds"/>; it CONSUMES that kind and returns numbers.</summary>
    Embed = 3,
}

/// <summary>The content types this library routes over. Open by construction —
/// <see cref="ProviderCapabilities.Kinds"/> is a string list — so an adapter may declare one that is not
/// here; these are the names the library itself uses.</summary>
public static class ProviderKinds
{
    public const string Text = "text";
    public const string Image = "image";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Model3d = "3d";
}

/// <summary>What a backend DECLARES it serves — read by a router BEFORE anything is spent.
///
/// <para><b>Capability is DATA, not a type hierarchy.</b> A backend that cannot do something declares so
/// and the router never dispatches to it; expressing the same thing as interfaces would need one type per
/// combination and still could not answer "which of your models" (<c>docs/DECISIONS.md</c> D125).</para>
///
/// <para><b>The two defaults point in OPPOSITE directions, on purpose.</b> An empty
/// <see cref="Kinds"/> or <see cref="Operations"/> serves NOTHING — a backend that forgot to declare must
/// be skipped rather than handed every request. An empty <see cref="Models"/> serves ANY — an aggregator
/// fronts hundreds behind one id and cannot enumerate them.</para></summary>
public sealed record ProviderCapabilities
{
    /// <summary>The content types served (<see cref="ProviderKinds"/>). Matched case-insensitively.</summary>
    public IReadOnlyList<string> Kinds { get; init; } = [];

    /// <summary>The operations served. Empty serves nothing.</summary>
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
    /// <param name="kind">The content type wanted (<see cref="ProviderKinds"/>).</param>
    /// <param name="operation">The method that would be called.</param>
    /// <param name="model">The model asked for, or null for the backend's default.</param>
    /// <param name="hasInputs">Whether the request carries input artifacts.</param>
    public bool Supports(
        string kind, ProviderOperation operation, string? model = null, bool hasInputs = false)
    {
        if (!Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) return false;
        if (!Operations.Contains(operation)) return false;
        if (hasInputs && !SupportsInputs) return false;
        if (model is { Length: > 0 } && Models.Count > 0 &&
            !Models.Contains(model, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }
}
