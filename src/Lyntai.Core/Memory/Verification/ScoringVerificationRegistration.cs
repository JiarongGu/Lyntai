using Lyntai.Inference;
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
    /// <para><b>Register the backend separately</b>, like any other:
    /// <c>AddHttpProvider("rerank", o =&gt; o.Produces = ProviderKinds.Score)</c>, or
    /// <c>AddOnnxCrossEncoder</c> for one that never speaks HTTP. This call says only what MEMORY does with
    /// the scores, which is why it takes no endpoint (<c>docs/DECISIONS.md</c> D139).</para>
    ///
    /// <para><b>The alternative to <c>AddMemoryVerification</c>, not a companion to it</b> — both fill the
    /// same singular seam, so register one. This scores pairs and never generates, so it costs a fraction
    /// of an instruct model's memory and none of generation's latency.</para>
    ///
    /// <para><b>Neither setting can be defaulted for you.</b>
    /// <see cref="ScoringVerificationOptions.ProviderId"/> says WHICH backend scores — name it once a second
    /// produces this kind, or registration order decides silently (D148);
    /// <see cref="ScoringVerificationOptions.EndorseCount"/> is your recall limit, and endorsing past a page
    /// replaces the ranking rather than refining it.</para>
    ///
    /// <para><b>Opt-in and fail-open</b>: an unreachable backend, or none at all, leaves the ranking exactly
    /// as it was. Smoke-test the model first — a community conversion can be missing its classification
    /// head, still load, and still return scores that are simply wrong.</para>
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
            sp.GetService<ILogger<ScoringVerificationPolicy>>(),
            sp.GetService<IProviderRouterFactory>()));

        return builder;
    }
}
