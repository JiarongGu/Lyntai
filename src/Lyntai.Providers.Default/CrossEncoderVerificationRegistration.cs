using Lyntai.Memory.Verification;
using Lyntai.Providers.OpenAiCompatible;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lyntai;

/// <summary>Registers a cross-encoder <see cref="IMemoryVerificationPolicy"/> over a
/// <c>/v1/rerank</c> endpoint.</summary>
public static class CrossEncoderVerificationRegistration
{
    /// <summary>
    /// Fill the memory verification seam with a cross-encoder RERANKER instead of an instruct model.
    ///
    /// <para><b>The alternative to <c>AddMemoryVerification</c>, not a companion to it</b> — both fill the
    /// same singular seam, so register one. This one scores <c>(query, candidate)</c> pairs and never
    /// generates, which is why it runs in a fraction of the memory an instruct model needs and does not pay
    /// generation's latency at all.</para>
    ///
    /// <para><b>Set <see cref="CrossEncoderVerificationOptions.EndorseCount"/> to your recall limit.</b> It
    /// cannot be defaulted for you — the request deliberately does not carry the caller's limit — and
    /// endorsing more than a page turns promotion from a refinement into a replacement.</para>
    ///
    /// <para><b>Opt-in and fail-open</b>, like every model-backed memory seam: an unreachable endpoint
    /// leaves the ranking exactly as it was. Smoke-test the model before trusting a run — a community
    /// conversion of a reranker can be missing its classification head, in which case it still loads and
    /// still returns scores that are simply wrong.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Knobs; null takes the defaults.</param>
    /// <param name="httpClient">BYO client. Supplied, Lyntai NEVER disposes it — the host owns its
    /// lifetime; absent, Lyntai creates and disposes its own.</param>
    public static LyntaiBuilder AddMemoryCrossEncoderVerification(this LyntaiBuilder builder,
        Action<CrossEncoderVerificationOptions>? configure = null,
        Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var config = new CrossEncoderVerificationOptions();
        configure?.Invoke(config);

        Func<IServiceProvider, Func<HttpClient>> resolveClient;
        var byo = httpClient is not null;
        if (byo)
        {
            resolveClient = sp => () => httpClient!(sp); // app-owned client + lifecycle — never disposed by Lyntai
        }
        else
        {
            builder.Services.AddHttpClient(RerankHttpClientName)
                .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
            resolveClient = sp => () =>
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(RerankHttpClientName);
        }

        // TryAdd, so a consumer's own IMemoryVerificationPolicy registered before this call wins outright —
        // the same BYO story every other seam in this subsystem has.
        builder.Services.TryAddSingleton<IMemoryVerificationPolicy>(sp => new CrossEncoderVerificationPolicy(
            config,
            resolveClient(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<CrossEncoderVerificationPolicy>>(),
            disposeHttpClient: !byo));  // dispose only Lyntai-created clients

        return builder;
    }

    internal const string RerankHttpClientName = "lyntai.rerank";
}
