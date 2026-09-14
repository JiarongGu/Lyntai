using Lyntai.Providers.LlamaSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddLlamaSharpProvider` shows up right on the builder.
namespace Lyntai;

public static class LlamaSharpBuilderExtensions
{
    /// <summary>Register an in-process local GGUF provider (default id "local") backed by LLamaSharp.
    /// <para>The consuming app must also reference an <c>LLamaSharp.Backend.*</c> package matching its
    /// hardware (e.g. <c>LLamaSharp.Backend.Cpu</c>, <c>.Cuda12</c>, <c>.Vulkan</c>, <c>.Metal</c>) —
    /// this provider ships managed only so it isn't nailed to one runtime. A missing backend surfaces
    /// as a Failed verdict on the first call (the router then falls over), not a startup crash.</para>
    /// <para>The model loads lazily on first use and is reused; generations are serialized (one local
    /// model, one at a time).</para></summary>
    public static LyntaiBuilder AddLlamaSharpProvider(this LyntaiBuilder builder, string modelPath,
        Action<LlamaSharpOptions>? configure = null, string id = "local")
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
