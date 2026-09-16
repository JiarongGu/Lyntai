using Lyntai.Agents;
using Lyntai.Embeddings;
using Lyntai.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lyntai;

/// <summary>Registers an <see cref="IToolSelector"/>, which bounds the tool roster before the model sees
/// it.</summary>
public static class ToolSelectorRegistration
{
    /// <summary>
    /// Narrow the tool roster to the requests each tool is actually plausible for, by embedding the
    /// request against every tool's own name and description.
    ///
    /// <para><b>Worth it when the registry holds a catalogue.</b> The loop otherwise shows every registered
    /// tool on every iteration and the model supplies no bound of its own — measured, a 4B invokes a tool on
    /// <b>90-95%</b> of requests nothing on the roster serves, and rewording the preamble moved that by
    /// nothing (<c>docs/memory-measurements.md</c> §5). On a handful of tools there is nothing to narrow and
    /// this only costs an embedding call.</para>
    ///
    /// <para><b>Needs a backend that produces <see cref="ProviderKinds.Vector"/></b>, and it is the
    /// cheapest thing in the loop:
    /// model-free, and the arm measured furthest ahead of any generative one at this size class.</para>
    ///
    /// <para><b>Fail-open.</b> A selector that faults or returns nothing leaves the roster whole — dropping
    /// the tool a request needed is the failure that matters, so it is the one the loop refuses to risk.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Knobs; null takes the defaults.</param>
    public static LyntaiBuilder AddEmbeddingToolSelector(this LyntaiBuilder builder,
        Action<ToolSelectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ToolSelectorOptions();
        configure?.Invoke(options);

        // TryAdd, so a consumer's own IToolSelector registered before this call wins outright — the same
        // BYO story every other seam in this library has.
        builder.Services.TryAddSingleton<IToolSelector>(sp =>
            new EmbeddingToolSelector(sp.GetServices<IModelProvider>(), options));

        return builder;
    }
}
