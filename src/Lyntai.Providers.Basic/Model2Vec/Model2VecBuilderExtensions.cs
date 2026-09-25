using Lyntai.Providers.Model2Vec;
using Microsoft.Extensions.DependencyInjection;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>DI entry point for the in-process static vector backend. A consumer composes it through the builder
/// (<c>services.AddLyntai(cfg =&gt; cfg.AddModel2VecProvider(…))</c>) and never constructs its types by hand.</summary>
public static class Model2VecBuilderExtensions
{
    /// <summary>
    /// Embed IN PROCESS from a <c>model2vec</c> static table — no HTTP endpoint, no GPU, no port, and no
    /// second process to ship and supervise.
    ///
    /// <para><b>Reach for it when the deployment cannot run a server</b>, which is the case this backend
    /// exists for: a desktop or game application on a stranger's machine cannot spawn one, and that is an
    /// operational constraint no benchmark reports. The quality trade is measured and is a property of the
    /// WORKLOAD (<c>docs/memory-measurements.md</c> §5).</para>
    ///
    /// <para><b>Loaded EAGERLY at registration</b>, not on first use: a missing or truncated model is a
    /// composition error worth hearing at startup, and the table is read once and held for the container's
    /// life.</para>
    ///
    /// <para>Every call ADDS a provider to the collection the router selects from. Give each its own
    /// <see cref="Model2VecProviderOptions.Id"/>: with a duplicate id the router keeps the first and never
    /// reaches the second.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="modelDirectory">A directory holding <c>model.safetensors</c> and <c>vocab.txt</c>.</param>
    /// <param name="configure">Knobs; null takes the model's own configuration.</param>
    public static LyntaiBuilder AddModel2VecProvider(this LyntaiBuilder builder, string modelDirectory,
        Action<Model2VecProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        var options = new Model2VecProviderOptions();
        configure?.Invoke(options);

        var provider = Model2VecProvider.FromDirectory(modelDirectory, options);

        // A PROVIDER like any other: it goes into the same collection the router selects chat and media
        // from, so several can be registered and told apart by id (D151). Built already, so the declaration
        // handed to composition is its OWN — never a restatement (D152).
        builder.AddProvider(_ => provider, provider.Capabilities);
        return builder;
    }
}
