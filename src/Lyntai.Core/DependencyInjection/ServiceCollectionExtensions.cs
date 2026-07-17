using Lyntai;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Processes;
using Lyntai.Prompts;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// Standard practice: service-collection extensions live in the MS namespace so `AddLyntai` is
// discoverable wherever `IServiceCollection` already is.
namespace Microsoft.Extensions.DependencyInjection;

public static class LyntaiServiceCollectionExtensions
{
    /// <summary>The public entry: compose providers/storage/scorers on the builder, get the router,
    /// prompt registry, scoring and trace services wired. <c>LYNTAI_*</c> environment variables are
    /// applied after the configure callback — env beats code config.</summary>
    public static IServiceCollection AddLyntai(this IServiceCollection services, Action<LyntaiBuilder> configure)
    {
        // idempotency guard: a second AddLyntai would register a second LyntaiOptions (shadowing the
        // first on resolution) while the providers/scorers from both calls pile into the DI collections,
        // configured against the now-orphaned first options. Compose everything in one configure callback.
        if (services.Any(d => d.ServiceType == typeof(LyntaiOptions)))
            throw new InvalidOperationException(
                "AddLyntai has already been called on this IServiceCollection. Call it once and compose all providers, storage, and scorers in the single configure callback.");

        var options = new LyntaiOptions();
        var builder = new LyntaiBuilder(services, options);
        configure(builder);
        options.ApplyEnvOverrides();

        services.AddSingleton(options);
        services.TryAddSingleton<ProcessRunner>();
        services.TryAddSingleton(sp => new DeadHostTracker(
            options.DeadHostThreshold, options.DeadHostCooldown, logger: sp.GetService<ILogger<DeadHostTracker>>()));
        services.TryAddSingleton<ILlmRouter>(sp => new LlmRouter(
            sp.GetServices<ILlmProvider>(), sp.GetRequiredService<DeadHostTracker>(), options, sp.GetService<ILogger<LlmRouter>>()));
        // the consumer front door: Lyntai behaving like ONE provider (default candidates internal)
        services.TryAddSingleton<ILlmClient>(sp => new LlmClient(sp.GetRequiredService<ILlmRouter>(), options));

        // The cortex services tolerate absent storage (null store → fail-open/no-op), so the minimal
        // setup — a provider and nothing else — still resolves everything.
        services.TryAddSingleton<IPromptRegistry>(sp => new PromptRegistry(
            sp.GetService<IKeyValueStore>(), sp.GetService<ILogger<PromptRegistry>>()));
        services.TryAddSingleton<IScoringService>(sp => new ScoringService(
            sp.GetServices<IScorer>(), sp.GetService<IScoreStore>(), sp.GetService<ILogger<ScoringService>>()));
        services.TryAddSingleton<ITraceService>(sp => new TraceService(
            sp.GetService<ITraceStore>(), logger: sp.GetService<ILogger<TraceService>>()));
        services.TryAddSingleton<IPromptComposer>(sp => new MemoryPromptComposer(
            sp.GetService<IMemoryStore>(), sp.GetService<ILogger<MemoryPromptComposer>>()));

        return services;
    }
}
