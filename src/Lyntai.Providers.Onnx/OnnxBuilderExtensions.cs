using Lyntai.Embeddings;
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
    /// at startup rather than on the first recall. Pooling, normalization and the sequence limit come from
    /// the model's own files unless <paramref name="configure"/> overrides them.</para>
    ///
    /// <para>Registered with <c>TryAdd</c>, so an <see cref="IEmbedder"/> registered before this call
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

        var embedder = OnnxProvider.FromDirectory(modelDirectory, options);

        // A PROVIDER declaring ProviderOperation.Embed, so the routing front door can select it by id
        // alongside every other backend (D129) — and a FACTORY returning the already-built instance, which
        // is the half that matters for resources. Building it above is what makes a bad model fail at
        // composition; registering through a factory rather than as an instance is what makes the container
        // OWN it, because `AddSingleton(instance)` does not dispose what it did not create and this holds a
        // native session. Pinned by `OnnxRegistrationTests` — collapsing it to an instance reads as a tidy-up.
        builder.AddEmbeddingProvider(_ => embedder);
        return builder;
    }

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
    /// backend. <b>Loaded EAGERLY</b> too, so a bad model directory is heard at startup.</para>
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

        var reranker = OnnxCrossEncoder.FromDirectory(modelDirectory, options);

        // A FACTORY returning the already-built instance, for the reason AddOnnxProvider states: the
        // container disposes what it CREATED, and this holds a native session.
        builder.AddProvider(_ => reranker);
        return builder;
    }
}
