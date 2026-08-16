using Lyntai.Llm;
using Lyntai.Memory.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lyntai;

/// <summary>Registers the model-backed <see cref="IMemoryVerificationPolicy"/>.</summary>
public static class MemoryVerificationRegistration
{
    /// <summary>
    /// Let a model judge which of a recall's candidates actually ANSWERED the query — so an answer the
    /// ranking buried can be promoted past the limit, and reinforcement follows evidence instead of the
    /// ranker's own prior.
    ///
    /// <para><b>Costs one model call per RECALL, in the latency path of an answer</b> — which is why
    /// <see cref="LlmVerificationOptions.ClientName"/> exists: name a client so judging runs on the smallest
    /// fast backend rather than on whichever model the application made default for chat.</para>
    ///
    /// <para><b>Opt-in, and best-effort.</b> A failing, slow or unparseable reply leaves the ranking
    /// untouched — see <see cref="IMemoryVerificationPolicy"/> for the fail-open rule every implementation
    /// honours. A judgement never removes a result from the caller's answer unless
    /// <see cref="Lyntai.Memory.GraphMemoryOptions.VerificationFilters"/> is set separately.</para>
    ///
    /// <para><b>Use a MULTILINGUAL model if the application stores non-Latin text.</b> The query and the
    /// headlines are passed through verbatim in whatever language they were written; an English-only model
    /// silently becomes the thing that decides which Chinese memories are worth returning.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Knobs; null takes the defaults.</param>
    public static LyntaiBuilder AddMemoryVerification(this LyntaiBuilder builder,
        Action<LlmVerificationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new LlmVerificationOptions();
        configure?.Invoke(options);

        // TryAdd, so a consumer's own IMemoryVerificationPolicy registered before this call wins outright —
        // the same BYO story every other seam in this subsystem has.
        builder.Services.TryAddSingleton<IMemoryVerificationPolicy>(sp => new LlmMemoryVerificationPolicy(
            sp.GetRequiredService<ILlmClientFactory>(), options,
            sp.GetService<ILogger<LlmMemoryVerificationPolicy>>()));

        return builder;
    }
}
