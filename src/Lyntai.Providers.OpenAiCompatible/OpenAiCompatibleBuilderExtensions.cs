using Lyntai.Providers.OpenAiCompatible;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddOpenAiCompatibleProvider` shows up right on the builder.
namespace Lyntai;

public static class OpenAiCompatibleBuilderExtensions
{
    /// <summary>Register an OpenAI-compatible HTTP provider under <paramref name="id"/> (also usable
    /// multiple times with different ids — e.g. one "openai" and one "ollama").</summary>
    public static LyntaiBuilder AddOpenAiCompatibleProvider(this LyntaiBuilder builder, string id,
        Action<OpenAiCompatibleOptions> configure)
    {
        var config = new OpenAiCompatibleOptions();
        configure(config);

        // per-call deadline (LyntaiOptions.ProviderTimeout) owns timeouts — not HttpClient's default 100s
        builder.Services.AddHttpClient(HttpClientName(id))
            .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
        builder.AddProvider(sp => new OpenAiCompatibleProvider(
            id,
            config,
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName(id)),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<OpenAiCompatibleProvider>>()));
        return builder;
    }

    internal static string HttpClientName(string id) => $"lyntai.provider.{id}";
}
