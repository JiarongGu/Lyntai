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
    /// <para><b>This is the single largest recall-quality lever the subsystem has, and the measurement says
    /// so.</b> Decomposing a full corpus replay: of the relevant entries a recall failed to return,
    /// <b>100% were reachable candidates ranked below the limit</b> and none were unreachable. The miss rate
    /// is a ranking failure end to end — and the two shipped model-free ranking policies return
    /// byte-identical results, so swapping formulas is not an available fix.</para>
    ///
    /// <para><b>What it is worth, measured.</b> A ground-truth REFERENCE judge takes miss
    /// <c>0.5357 → 0.2857</c> and pollution <c>0.3331 → 0.1549</c>. Real models, same corpus and seed:</para>
    /// <list type="table">
    /// <item><term><c>gemma3:4b</c> (3.3 GB)</term><description>miss <c>0.2571</c>/<c>0.2643</c>, pollution
    /// <c>0.0492</c>/<c>0.0571</c> — <b>better than the reference on both, reproducibly</b>.</description></item>
    /// <item><term><c>qwen2.5-vl:7b</c> (6.0 GB)</term><description>miss <c>0.3071</c>, pollution
    /// <c>0.1556</c>.</description></item>
    /// <item><term><c>llama3.2:3b</c> (2.0 GB)</term><description>miss <c>~0.37-0.39</c>, and it VARIES
    /// between runs, because a model is not deterministic.</description></item>
    /// </list>
    ///
    /// <para><b>The reference is not an upper bound.</b> It promotes only strictly-relevant entries, which
    /// leaves the rest of the limit to the noisy ranking and reinforces fewer entries than a broader judge —
    /// so a good model beats it on both counts. Note also that a SMALL CURRENT model beat a larger older
    /// one: judge quality tracks generation at least as much as parameter count.</para>
    ///
    /// <para><b>Which model to use is a DEPLOYMENT choice and this library takes no position on it</b> — the
    /// numbers show the shape of the trade, not a recommended tier. One practical warning: avoid a THINKING
    /// model here. <c>qwen3:4b</c> spent ~25 s of reasoning per judgement against gemma3's ~1.5 s, which is
    /// disqualifying for a seam in the latency path of every recall whatever it would have scored.</para>
    ///
    /// <para>A judge allowed to see only what already won recovers barely a third of the available
    /// improvement whatever its size, which is why
    /// <see cref="Lyntai.Memory.GraphMemoryOptions.VerificationDepth"/> defaults to a multiple of the
    /// recall's limit rather than to the limit itself.</para>
    ///
    /// <para><b>Costs one model call per RECALL, in the latency path of an answer.</b> That is a real price
    /// and it is why <see cref="LlmVerificationOptions.ClientName"/> exists: name a client registered with
    /// <c>AddLlmClient</c> so judging runs on the smallest fast backend rather than on whichever model the
    /// application made default for chat.</para>
    ///
    /// <para><b>Opt-in, and everything degrades to the model-free floor.</b> Without this call an engine
    /// ranks exactly as it always has; with it, a failing, slow or unparseable reply leaves the ranking
    /// untouched — never "nothing was relevant", which would teach the engine that its recalls are failing.
    /// A judgement also never removes a result from the caller's answer unless
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
