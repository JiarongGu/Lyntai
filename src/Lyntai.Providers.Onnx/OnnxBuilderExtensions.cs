using Lyntai.Lifecycle;
using Lyntai.Providers.Onnx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>DI entry point for <c>Lyntai.Providers.Onnx</c>. A consumer composes this adapter through the
/// builder (<c>services.AddLyntai(cfg =&gt; cfg.AddOnnxProvider(…))</c>) and never constructs its types by
/// hand.</summary>
public static class OnnxBuilderExtensions
{
    /// <summary>
    /// Embed IN PROCESS with a transformer through ONNX Runtime — no HTTP endpoint, no server, no port.
    ///
    /// <para><b>This package references the MANAGED half of ONNX Runtime only, so the consuming application
    /// MUST add exactly one native backend</b> — <c>Microsoft.ML.OnnxRuntime</c> (CPU),
    /// <c>Microsoft.ML.OnnxRuntime.DirectML</c> (any DX12 GPU) or <c>Microsoft.ML.OnnxRuntime.Gpu</c>
    /// (CUDA). Without one the load throws at this call. That is deliberate: nailing the package to a
    /// backend would ship ~16 MB of the wrong native code to everyone and choose the user's hardware for
    /// them, which is what <c>docs/DECISIONS.md</c> D68 refuses.</para>
    ///
    /// <para><b>Loaded EAGERLY</b>, so a missing, truncated or non-ONNX model is a composition error heard
    /// at startup rather than on the first recall. The native session therefore exists before the container
    /// does, and a composition step that throws AFTER this call strands one — kept deliberately, because
    /// loading lazily trades a loud startup failure for a quiet first-recall one. Pooling, normalization and
    /// the sequence limit come from the model's own files unless <paramref name="configure"/> overrides
    /// them.</para>
    ///
    /// <para>Registered with <c>TryAdd</c>, so an an embedding backend registered before this call
    /// wins — the BYO story every seam here has.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="modelDirectory">A directory holding an ONNX graph and <c>vocab.txt</c>.</param>
    /// <param name="configure">Knobs; null takes the model's own configuration.</param>
    public static LyntaiBuilder AddOnnxProvider(this LyntaiBuilder builder, string modelDirectory,
        Action<OnnxProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        var options = new OnnxProviderOptions();
        configure?.Invoke(options);

        return RegisterOwned(builder, OnnxProvider.FromDirectory(modelDirectory, options), embeds: true);
    }

    /// <summary>Hands the container a built backend it will DISPOSE — the one registration site both calls
    /// above share.
    ///
    /// <para><b>A FACTORY returning the already-built instance, and the distinction is the whole point.</b>
    /// Building eagerly is what makes a bad model directory fail at composition; registering through a
    /// factory rather than as an instance is what makes the container OWN the result, because
    /// <c>AddSingleton(instance)</c> does not dispose what it did not create and these hold a native session.
    /// Collapsing it reads as a tidy-up and leaks one per container.</para>
    ///
    /// <para><b>One site rather than a copy in each call</b>, so the rule cannot be applied on one path and
    /// missed on the other — and so `OnnxOwnershipTests` can assert the registration these methods really
    /// perform instead of restating the DI rule beside them.</para>
    ///
    /// <para><paramref name="embeds"/> picks the collection: an embedding provider sets a composition-time
    /// flag that decides whether <c>AddSemanticMemory</c> can be honoured, and a cross-encoder must not set
    /// it — it produces scores, and claiming otherwise turns a clean composition failure into a runtime
    /// one.</para></summary>
    internal static LyntaiBuilder RegisterOwned(LyntaiBuilder builder, IModelProvider provider, bool embeds) =>
        embeds ? builder.AddEmbeddingProvider(_ => provider) : builder.AddProvider(_ => provider);

    /// <summary>
    /// Score <c>(query, document)</c> pairs IN PROCESS with a cross-encoder through ONNX Runtime — the
    /// reranker half of this package, and a different backend from
    /// <see cref="AddOnnxProvider"/> rather than a mode of it.
    ///
    /// <para><b>Registering it is all that reaching it takes.</b> It declares
    /// <see cref="Lyntai.Lifecycle.ProviderKinds.Score"/>, so
    /// <c>AddMemoryScoringVerification()</c> selects it with no endpoint and no second seam
    /// (<c>docs/DECISIONS.md</c> D139) — that call decides what memory DOES with the scores, this one says
    /// what produces them.</para>
    ///
    /// <para><b>The same native-backend requirement as <see cref="AddOnnxProvider"/> applies</b>: this
    /// package references the MANAGED half of ONNX Runtime only, so the application adds exactly one native
    /// backend. <b>Loaded EAGERLY</b> too, with the same trade recorded there.</para>
    ///
    /// <para><b>An export whose head cannot carry one score per pair is refused HERE</b>, not on the first
    /// recall — a multi-label (NLI) model above all, which otherwise loads, scores, and ranks backwards.
    /// It has to be composition: the seam below is fail-open, so the same refusal raised later arrives as a
    /// recall that is silently never verified.</para>
    ///
    /// <para><b>Registered as a plain provider, not an embedding one</b> — it produces scores, so it must not
    /// be what makes a deployment think it can embed.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="modelDirectory">A directory holding a cross-encoder ONNX graph and <c>vocab.txt</c>.</param>
    /// <param name="configure">Knobs; null takes the model's own configuration.</param>
    public static LyntaiBuilder AddOnnxCrossEncoder(this LyntaiBuilder builder, string modelDirectory,
        Action<OnnxCrossEncoderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        var options = new OnnxCrossEncoderOptions();
        configure?.Invoke(options);

        return RegisterOwned(
            builder, OnnxCrossEncoder.FromDirectory(modelDirectory, options), embeds: false);
    }
}
