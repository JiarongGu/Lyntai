using Lyntai.Providers.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddLocalProvider` shows up right on the builder.
namespace Lyntai;

public static class LocalBuilderExtensions
{
    /// <summary>Register an in-process local GGUF provider (default id "local") backed by LLamaSharp.
    /// <para>The consuming app must also reference an <c>LLamaSharp.Backend.*</c> package matching its
    /// hardware (e.g. <c>LLamaSharp.Backend.Cpu</c>, <c>.Cuda12</c>, <c>.Vulkan</c>, <c>.Metal</c>) —
    /// this provider ships managed only so it isn't nailed to one runtime. A missing backend surfaces
    /// as a Failed verdict on the first call (the router then falls over), not a startup crash.</para>
    /// <para>The model loads lazily on first use and is reused; generations are serialized (one local
    /// model, one at a time).</para></summary>
    public static LyntaiBuilder AddLocalProvider(this LyntaiBuilder builder, string modelPath,
        Action<LocalModelOptions>? configure = null, string id = "local")
    {
        var config = new LocalModelOptions { ModelPath = modelPath };
        configure?.Invoke(config);

        builder.AddProvider(sp => new LocalProvider(
            id,
            config,
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<LocalProvider>>()));
        return builder;
    }
}
