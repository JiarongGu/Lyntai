using Lyntai.Embeddings;
using Lyntai.Embeddings.Static;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>DI entry point for the in-process static embedder. A consumer composes it through the builder
/// (<c>services.AddLyntai(cfg =&gt; cfg.AddStaticEmbedder(…))</c>) and never constructs its types by hand.</summary>
public static class StaticBuilderExtensions
{
    /// <summary>
    /// Embed IN PROCESS from a <c>model2vec</c> static table — no HTTP endpoint, no GPU, no port, and no
    /// second process to ship and supervise.
    ///
    /// <para><b>Reach for it when the deployment cannot run a server</b>, which is the case this package
    /// exists for: a desktop or game application on a stranger's machine cannot spawn one, and that is an
    /// operational constraint no benchmark reports. The quality trade is measured and is a property of the
    /// WORKLOAD — about 0.5 points on the memory default, about 12 on a purely embedding-bound selective
    /// task (<c>docs/memory-measurements.md</c> §5).</para>
    ///
    /// <para><b>Loaded EAGERLY at registration</b>, not on first use: a missing or truncated model is a
    /// composition error worth hearing at startup, and the table is read once and shared. The model is
    /// around 30 MB for `potion-base-8M` and is held for the container's life.</para>
    ///
    /// <para>Registered with <c>TryAdd</c>, so an <see cref="IEmbedder"/> registered before this call wins —
    /// the BYO story every seam here has.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="modelDirectory">A directory holding <c>model.safetensors</c> and <c>vocab.txt</c>.</param>
    /// <param name="configure">Knobs; null takes the model's own configuration.</param>
    public static LyntaiBuilder AddStaticEmbedder(this LyntaiBuilder builder, string modelDirectory,
        Action<StaticEmbedderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        var options = new StaticEmbedderOptions();
        configure?.Invoke(options);

        var embedder = StaticEmbedder.FromDirectory(modelDirectory, options);
        builder.Services.TryAddSingleton<IEmbedder>(embedder);
        return builder;
    }
}
