using Lyntai.Lifecycle;
using Lyntai.Memory.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lyntai;

/// <summary>Fills the memory verification seam with a SCORING backend rather than an instruct model.</summary>
public static class ScoringVerificationRegistration
{
    /// <summary>
    /// Verify recalls with whatever backend produces <see cref="ProviderKinds.Score"/> — a cross-encoder
    /// reranker, typically.
    ///
    /// <para><b>Register the backend separately</b>, like any other: an HTTP reranker is
    /// <c>AddHttpProvider("rerank", o =&gt; { o.BaseUrl = …; o.Produces = ProviderKinds.Score; })</c>. This
    /// call only says what MEMORY does with the scores, which is why it takes no endpoint
    /// (<c>docs/DECISIONS.md</c> D139). Any backend declaring that kind serves it, including one that never
    /// speaks HTTP.</para>
    ///
    /// <para><b>The alternative to <c>AddMemoryVerification</c>, not a companion to it</b> — both fill the
    /// same singular seam, so register one. This one scores <c>(query, candidate)</c> pairs and never
    /// generates, which is why it runs in a fraction of the memory an instruct model needs and does not pay
    /// generation's latency at all.</para>
    ///
    /// <para><b>Set <see cref="ScoringVerificationOptions.EndorseCount"/> to your recall limit.</b> It
    /// cannot be defaulted for you — the request deliberately does not carry the caller's limit — and
    /// endorsing more than a page turns promotion from a refinement into a replacement.</para>
    ///
    /// <para><b>Opt-in and fail-open</b>, like every model-backed memory seam: an unreachable backend, or
    /// none registered at all, leaves the ranking exactly as it was. Smoke-test the model before trusting a
    /// run — a community conversion of a reranker can be missing its classification head, in which case it
    /// still loads and still returns scores that are simply wrong.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Knobs; null takes the defaults.</param>
    public static LyntaiBuilder AddMemoryScoringVerification(this LyntaiBuilder builder,
        Action<ScoringVerificationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var config = new ScoringVerificationOptions();
        configure?.Invoke(config);

        // TryAdd, so a consumer's own IMemoryVerificationPolicy registered before this call wins outright —
        // the same BYO story every other seam in this subsystem has.
        builder.Services.TryAddSingleton<IMemoryVerificationPolicy>(sp => new ScoringVerificationPolicy(
            sp.GetServices<IModelProvider>(),
            config,
            sp.GetService<ILogger<ScoringVerificationPolicy>>()));

        return builder;
    }
}
