using Lyntai.Providers.LlamaSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddLlamaSharpProvider` shows up right on the builder.
namespace Lyntai;

/// <summary>DI entry point for <c>Lyntai.Providers.LlamaSharp</c>: an in-process GGUF model. A consumer
/// composes it through the builder and never constructs its types by hand.</summary>
public static class LlamaSharpBuilderExtensions
{
    /// <summary>The id <see cref="AddLlamaSharpProvider"/> registers under unless given another — the
    /// backend's name.</summary>
    public const string DefaultId = "llamasharp";

    /// <summary>Register an in-process local GGUF provider (id <see cref="DefaultId"/> unless
    /// <paramref name="id"/> names another) backed by LLamaSharp.
    /// <para>The consuming app must also reference an <c>LLamaSharp.Backend.*</c> package matching its
    /// hardware (e.g. <c>LLamaSharp.Backend.Cpu</c>, <c>.Cuda12</c>, <c>.Vulkan</c>, <c>.Metal</c>) —
    /// this provider ships managed only so it isn't nailed to one runtime. A missing backend surfaces
    /// as a Failed verdict on the first call (the router then falls over), not a startup crash.</para>
    /// <para>The model loads lazily on first use and is reused; generations are serialized (one local
    /// model, one at a time).</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="modelPath">The GGUF file.</param>
    /// <param name="configure">Knobs; see <see cref="LlamaSharpOptions"/>.</param>
    /// <param name="id">The router-facing id; a second model needs its own.</param>
    public static LyntaiBuilder AddLlamaSharpProvider(this LyntaiBuilder builder, string modelPath,
        Action<LlamaSharpOptions>? configure = null, string id = DefaultId)
    {
        var config = new LlamaSharpOptions { ModelPath = modelPath };
        configure?.Invoke(config);

        builder.AddProvider(sp => new LlamaSharpProvider(
            id,
            config,
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<LlamaSharpProvider>>()));
        return builder;
    }
}
