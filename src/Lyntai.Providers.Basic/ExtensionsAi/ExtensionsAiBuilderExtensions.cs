using Lyntai.Providers.ExtensionsAi;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddExtensionsAiProvider` shows up right on the builder.
namespace Lyntai;

public static class ExtensionsAiBuilderExtensions
{
    /// <summary>Bridge an existing <see cref="IChatClient"/> into the router under <paramref name="id"/>.</summary>
    public static LyntaiBuilder AddExtensionsAiProvider(this LyntaiBuilder builder, string id, IChatClient client) =>
        builder.AddExtensionsAiProvider(id, _ => client);

    /// <summary>Bridge an <see cref="IChatClient"/> resolved from the service provider (for clients
    /// that are themselves registered in DI).</summary>
    public static LyntaiBuilder AddExtensionsAiProvider(this LyntaiBuilder builder, string id,
        Func<IServiceProvider, IChatClient> clientFactory)
    {
        builder.AddProvider(sp => new ExtensionsAiProvider(
            id,
            clientFactory(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ExtensionsAiProvider>>()));
        return builder;
    }
}
