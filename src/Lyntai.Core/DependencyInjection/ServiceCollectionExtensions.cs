using Lyntai;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Jobs;
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
        services.TryAddSingleton<IProcessRunner, ProcessRunner>(); // BYO: register your own IProcessRunner first to override spawning
        services.TryAddSingleton(sp => new DeadHostTracker(
            options.DeadHostThreshold, options.DeadHostCooldown, logger: sp.GetService<ILogger<DeadHostTracker>>()));
        services.TryAddSingleton<ILlmRouter>(sp => new LlmRouter(
            sp.GetServices<ILlmProvider>(), sp.GetRequiredService<DeadHostTracker>(), options, sp.GetService<ILogger<LlmRouter>>()));
        // the consumer front door: Lyntai behaving like ONE provider (default candidates internal)
        services.TryAddSingleton<ILlmClient>(sp => new LlmClient(sp.GetRequiredService<ILlmRouter>(), options));

        // The cortex services tolerate absent storage (null store → fail-open/no-op), so the minimal
        // setup — a provider and nothing else — still resolves everything.
        services.TryAddSingleton<IPromptRegistry>(sp => new PromptRegistry(
            sp.GetService<IKeyValueStore>(), sp.GetService<IPromptVersionStore>(), sp.GetService<ILogger<PromptRegistry>>()));
        services.TryAddSingleton<IScoringService>(sp => new ScoringService(
            sp.GetServices<IScorer>(), sp.GetService<IScoreStore>(), sp.GetService<ILogger<ScoringService>>()));
        services.TryAddSingleton<ITraceService>(sp => new TraceService(
            sp.GetService<ITraceStore>(), logger: sp.GetService<ILogger<TraceService>>()));
        services.TryAddSingleton<IPromptComposer>(sp => new MemoryPromptComposer(
            sp.GetService<IMemoryStore>(), sp.GetService<ILogger<MemoryPromptComposer>>()));
        services.TryAddSingleton<IPairwiseComparer>(sp => new LlmPairwiseComparer(sp.GetRequiredService<ILlmClient>()));

        // agentic tool-calling: the registry gathers any registered ITools; the loop runs provider-
        // agnostically over the front door (works with zero tools too — it degenerates to one completion)
        services.TryAddSingleton<IToolRegistry>(sp => new ToolRegistry(sp.GetServices<ITool>()));
        services.TryAddSingleton<IToolLoop>(sp => new ToolLoop(
            sp.GetRequiredService<ILlmClient>(), sp.GetRequiredService<IToolRegistry>(), options, sp.GetService<ILogger<ToolLoop>>()));

        // durable jobs: registry gathers registered IJobHandlers; queue/runner drive them over IJobStore
        // (they throw if no storage backend is wired — durable work must be persisted, not silently lost)
        services.TryAddSingleton<IJobHandlerRegistry>(sp => new JobHandlerRegistry(sp.GetServices<IJobHandler>()));
        services.TryAddSingleton<IJobQueue>(sp => new JobQueue(sp.GetService<IJobStore>(), options));
        services.TryAddSingleton<IJobRunner>(sp => new JobRunner(
            sp.GetService<IJobStore>(), sp.GetRequiredService<IJobHandlerRegistry>(), options, sp.GetService<ILogger<JobRunner>>()));

        return services;
    }
}
