using Lyntai.Llm;
using Lyntai.Memory.Annotation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lyntai;

/// <summary>Registers the model-backed <see cref="IMemoryAnnotationPolicy"/>.</summary>
public static class MemoryAnnotationRegistration
{
    /// <summary>
    /// Label each remembered fact with what it is ABOUT, using a model, so entries concerning the same
    /// entity become connected.
    ///
    /// <para><b>What it buys, and why nothing cheaper does.</b> A graph engine's edges otherwise come from
    /// vector similarity or from co-activation during recall — and co-activation links whatever a recall
    /// happened to return together, which is not about two facts concerning the same entity. Facts like "my
    /// spouse is Alice", "she works as an anaesthetist" and "we met in Kyoto" share only pronouns in any
    /// language, so no tokenizer or ranking change reaches them.</para>
    ///
    /// <para><b>Opt-in, and everything degrades to the model-free floor.</b> Without this call an engine
    /// annotates nothing and behaves exactly as it always has; with it, a failing or unparseable reply
    /// yields no subjects and the write proceeds regardless.</para>
    ///
    /// <para><b>Costs one model call per WRITE</b> (plus one read for context), which is why
    /// <see cref="LlmAnnotationOptions.ClientName"/> exists: name a client registered with
    /// <c>AddLlmClient</c> and annotation runs on a small fast backend instead of whichever model the
    /// application made default for chat.</para>
    ///
    /// <para><b>Use a MULTILINGUAL model if the application stores non-Latin text.</b> This library detects
    /// no language and passes content through verbatim; the prompt asks for subjects in the language of the
    /// fact precisely so the same entity yields the same handle whatever it is written in. An English-only
    /// model silently becomes the thing that decides whether Chinese facts get linked.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Knobs; null takes the defaults.</param>
    public static LyntaiBuilder AddMemoryAnnotation(this LyntaiBuilder builder,
        Action<LlmAnnotationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new LlmAnnotationOptions();
        configure?.Invoke(options);

        // TryAdd, so a consumer's own IMemoryAnnotationPolicy registered before this call wins outright —
        // the same BYO story every other seam in this subsystem has.
        builder.Services.TryAddSingleton<IMemoryAnnotationPolicy>(sp => new LlmMemoryAnnotationPolicy(
            sp.GetRequiredService<ILlmClientFactory>(), options,
            sp.GetService<ILogger<LlmMemoryAnnotationPolicy>>()));

        return builder;
    }
}
