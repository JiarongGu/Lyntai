using Lyntai.Inference;
using Lyntai.Providers.Onnx;
using Microsoft.Extensions.DependencyInjection;

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
    /// loading lazily trades a loud startup failure for a quiet first-recall one. Every knob defaults to
    /// the model's own files; see <see cref="OnnxProviderOptions"/>.</para>
    ///
    /// <para>Every call ADDS a provider to the collection the router selects from. Give each its own
    /// <see cref="OnnxProviderOptions.Id"/>: with a duplicate id the router keeps the first and never reaches
    /// the second.</para>
    ///
    /// <para><b>The SAME call registers a reranker</b>: set <see cref="OnnxProviderOptions.Produces"/> to
    /// <see cref="Lyntai.Inference.ProviderKinds.Score"/>, with its own <see cref="OnnxProviderOptions.Id"/>
    /// because one session holds one graph (<b>D157</b>). <c>AddMemoryScoringVerification()</c> selects it
    /// on that alone (<b>D139</b>), and a head that cannot carry one score per pair is refused HERE — that
    /// seam is fail-open, so a later refusal is a recall silently never verified.</para>
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

        return RegisterOwned(builder, OnnxProvider.FromDirectory(modelDirectory, options));
    }

    /// <summary>Hands the container a built backend it will DISPOSE — kept as its own seam so
    /// `OnnxOwnershipTests` can assert the registration the method above really performs.
    ///
    /// <para><b>A FACTORY returning the already-built instance, and the distinction is the whole point.</b>
    /// Building eagerly is what makes a bad model directory fail at composition; registering through a
    /// factory rather than as an instance is what makes the container OWN the result, because
    /// <c>AddSingleton(instance)</c> does not dispose what it did not create and these hold a native session.
    /// Collapsing it reads as a tidy-up and leaks one per container.</para>
    ///
    /// <para><b>The capability is READ, never restated.</b> The backend is built before this runs,
    /// so the declaration handed to composition is the provider's own — there is no second place to get it
    /// wrong, and no parameter saying which kind this is (<c>docs/DECISIONS.md</c> <b>D152</b>).</para></summary>
    internal static LyntaiBuilder RegisterOwned(LyntaiBuilder builder, IModelProvider provider) =>
        builder.AddProvider(_ => provider, provider.Capabilities);
}
