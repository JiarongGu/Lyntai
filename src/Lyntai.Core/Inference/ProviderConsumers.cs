namespace Lyntai.Inference;

/// <summary>The per-feature <see cref="TextRequest.Consumer"/> tags this library itself sends.
/// <para>Declared once rather than spelled at each call site, because a tag is a KEY a host writes its
/// <see cref="LyntaiOptions.DefaultModelByConsumer"/> and <c>Budget.PerConsumer</c> against — so a typo
/// does not fail, it silently opens a second bucket that no cap covers and no report names.</para>
/// <para>A consumer's own tags are free-form and need no entry here; these exist so the library's own
/// traffic is separable from the application's.</para></summary>
public static class ProviderConsumers
{
    /// <summary>Anything untagged, and the fallback every unlisted tag resolves through.</summary>
    public const string Default = "default";

    /// <summary>An LLM judge or comparer in the cortex layer.</summary>
    public const string Scoring = "scoring";

    /// <summary>The memory subsystem's own model calls — annotation, verification, and (since D163) its
    /// embedding and rerank traffic: enrichment on write, semantic seeds and semantic recall, scoring
    /// verification.
    /// <para>Verification fires on EVERY recall, so this is the tag an operator most often wants to cap or
    /// watch on its own, separate from the application's spend.</para></summary>
    public const string Memory = "memory";

    /// <summary>A tool the model drives itself — what <see cref="Lyntai.Generation.Tools"/>' tools bill to
    /// by default, so one <c>Budget.PerConsumer["agent"]</c> entry caps model-driven renders.
    /// <para><b>The TOOL LOOP is not tagged with this, and a host budgeting should know why.</b>
    /// <c>IToolLoop</c> forwards the CALLER's request unchanged, so its iterations — up to
    /// <see cref="LyntaiOptions.ToolLoopMaxIterations"/> model calls — bill to whatever the caller tagged,
    /// which through <c>IChatOrchestrator</c> is <see cref="Chat"/>. That is deliberate: the loop is doing
    /// the caller's work, and re-tagging it would silently move spend out of a cap an existing deployment
    /// already set.</para></summary>
    public const string Agent = "agent";

    /// <summary>A conversational turn through <c>IChatOrchestrator</c>, including any tool-loop iterations
    /// it drives.
    /// <para>Declared because <c>ChatTurn.Consumer</c> defaults to it and the orchestrator puts it straight
    /// onto the request — so it reaches the usage tracker and the budget layer whether or not anyone named
    /// it. Until it was declared it was a library-emitted tag with no constant, which is the exact shape the
    /// remarks above warn about: a bucket no cap covers and no report names.</para></summary>
    public const string Chat = "chat";
}
