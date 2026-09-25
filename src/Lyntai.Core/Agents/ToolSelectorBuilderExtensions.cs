using Lyntai.Agents;
using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lyntai;

/// <summary>Registers an <see cref="IToolSelector"/>, which bounds the tool roster before the model sees
/// it.</summary>
public static class ToolSelectorBuilderExtensions
{
    /// <summary>
    /// Narrow the tool roster to the requests each tool is actually plausible for, by embedding the
    /// request against every tool's own name and description.
    ///
    /// <para><b>Worth it when the registry holds a catalogue</b> — why is on <see cref="IToolSelector"/>. On a
    /// handful of tools there is nothing to narrow and this only costs an embedding call.</para>
    ///
    /// <para><b>Needs a backend that produces <see cref="ProviderKinds.Vector"/></b>; see
    /// <see cref="VectorToolSelector"/>.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Knobs; null takes the defaults.</param>
    public static LyntaiBuilder AddVectorToolSelector(this LyntaiBuilder builder,
        Action<ToolSelectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ToolSelectorOptions();
        configure?.Invoke(options);

        // TryAdd, so a consumer's own IToolSelector registered before this call wins outright — the same
        // BYO story every other seam in this library has.
        builder.Services.TryAddSingleton<IToolSelector>(sp =>
            new VectorToolSelector(sp.GetServices<IModelProvider>(), options,
                sp.GetService<IProviderRouterFactory>()));

        return builder;
    }
}
