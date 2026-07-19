using Lyntai;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Guards;
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

        // a consumer-supplied ILlmClient registered BEFORE AddLyntai would make the base TryAddSingleton
        // below no-op — silently dropping any front-door decorators (cache/budget/rate-limit). Catch that
        // contradiction rather than let governance vanish without a trace.
        var hadPreexistingClient = services.Any(d => d.ServiceType == typeof(ILlmClient));

        var options = new LyntaiOptions();
        var builder = new LyntaiBuilder(services, options);
        configure(builder);
        options.ApplyEnvOverrides();

        if (hadPreexistingClient && builder.FrontDoorDecorators.Count > 0)
            throw new InvalidOperationException(
                "A front-door decorator (AddResponseCache / AddUsageBudget / AddRateLimit) was configured, but an " +
                "ILlmClient is already registered — the decorators would be silently ignored. Either don't pre-register " +
                "ILlmClient, or use the BYO seams (IResponseCache / IUsageTracker / IRateLimiter) instead.");

        // Compose per feature area — each block is self-contained and order-independent across areas (they
        // register distinct service types; the front-door decorators fold at resolution, not registration).
        services.AddSingleton(options);
        RegisterLlmFrontDoor(services, builder, options);
        RegisterCortex(services, options);
        RegisterSemanticMemory(services);
        RegisterAgents(services, options);
        RegisterJobs(services, options);
        RegisterGuardsAndChat(services);

        return services;
    }

    /// <summary>The LLM front door: process runner, dead-host tracker, router, and the consumer
    /// <see cref="ILlmClient"/> — Lyntai behaving like ONE provider, with any front-door decorators folded
    /// over the base client.</summary>
    private static void RegisterLlmFrontDoor(IServiceCollection services, LyntaiBuilder builder, LyntaiOptions options)
    {
        services.TryAddSingleton<IProcessRunner, ProcessRunner>(); // BYO: register your own IProcessRunner first to override spawning
        services.TryAddSingleton(sp => new DeadHostTracker(
            options.DeadHostThreshold, options.DeadHostCooldown, logger: sp.GetService<ILogger<DeadHostTracker>>()));
        services.TryAddSingleton<ILlmRouter>(sp => new LlmRouter(
            sp.GetServices<ILlmProvider>(), sp.GetRequiredService<DeadHostTracker>(), options,
            sp.GetService<ILogger<LlmRouter>>(), modelRouting: sp.GetService<Lyntai.Llm.Routing.IModelRoutingStore>()));
        // Default candidates internal. Any registered front-door decorators (response cache, usage budget, …)
        // are folded over the base client in registration order, so they compose instead of clobbering.
        services.TryAddSingleton<ILlmClient>(sp =>
        {
            ILlmClient client = new LlmClient(sp.GetRequiredService<ILlmRouter>(), options);
            foreach (var (_, decorate) in builder.FrontDoorDecorators.OrderBy(d => d.Order))
                client = decorate(sp, client);
            // per-request refusal screening (LlmRequest.RefusalPattern) is OUTERMOST + always on (no config —
            // it's a request field), so it re-screens even a cached hit. Deliberately NOT in
            // FrontDoorDecorators, so it doesn't trip the "decorators configured but ILlmClient pre-registered"
            // guard above.
            return new RefusalScreeningLlmClient(client, sp.GetService<ILogger<RefusalScreeningLlmClient>>());
        });
    }

    /// <summary>The LLM-ops cortex: prompt registry, scoring, tracing, prompt composition, and the pairwise
    /// judge. All tolerate absent storage (null store → fail-open/no-op), so a provider-only setup resolves.</summary>
    private static void RegisterCortex(IServiceCollection services, LyntaiOptions options)
    {
        services.TryAddSingleton<IPromptRegistry>(sp => new PromptRegistry(
            sp.GetService<IKeyValueStore>(), sp.GetService<IPromptVersionStore>(),
            sp.GetService<ILogger<PromptRegistry>>(), options.PromptKeyPrefix));
        services.TryAddSingleton<IScoringService>(sp => new ScoringService(
            sp.GetServices<IScorer>(), sp.GetService<IScoreStore>(), sp.GetService<ILogger<ScoringService>>()));
        services.TryAddSingleton<ITraceService>(sp => new TraceService(
            sp.GetService<ITraceStore>(), logger: sp.GetService<ILogger<TraceService>>()));
        services.TryAddSingleton<IPromptComposer>(sp => new MemoryPromptComposer(
            sp.GetService<IMemoryStore>(), sp.GetService<Lyntai.Memory.ISemanticMemory>(),
            sp.GetService<ILogger<MemoryPromptComposer>>()));
        services.TryAddSingleton<IPairwiseComparer>(sp => new LlmPairwiseComparer(sp.GetRequiredService<ILlmClient>()));
    }

    /// <summary>Semantic memory — wired ONLY when an embedder is registered (AddEmbeddings). Composes the
    /// app's IEmbedder with a vector store (in-memory default; register your own IVectorStore for pgvector/
    /// etc.). Absent an embedder it isn't registered, so the composer/orchestrator resolve null and skip it —
    /// no accidental throws on every turn.</summary>
    private static void RegisterSemanticMemory(IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(Lyntai.Embeddings.IEmbedder))) return;
        services.TryAddSingleton<Lyntai.Memory.IVectorStore, Lyntai.Memory.InMemoryVectorStore>();
        services.TryAddSingleton<Lyntai.Memory.ISemanticMemory>(sp => new Lyntai.Memory.SemanticMemory(
            sp.GetRequiredService<Lyntai.Embeddings.IEmbedder>(), sp.GetRequiredService<Lyntai.Memory.IVectorStore>(),
            sp.GetService<ILogger<Lyntai.Memory.SemanticMemory>>()));
    }

    /// <summary>Agentic tool-calling: the registry gathers any registered ITools; the loop runs provider-
    /// agnostically over the front door (works with zero tools too — it degenerates to one completion).</summary>
    private static void RegisterAgents(IServiceCollection services, LyntaiOptions options)
    {
        services.TryAddSingleton<IToolRegistry>(sp => new ToolRegistry(sp.GetServices<ITool>()));
        services.TryAddSingleton<IToolLoop>(sp => new ToolLoop(
            sp.GetRequiredService<ILlmClient>(), sp.GetRequiredService<IToolRegistry>(), options, sp.GetService<ILogger<ToolLoop>>()));
    }

    /// <summary>Durable jobs: the handler registry, enqueue queue, admission control, runner, and scheduler.
    /// The queue/runner throw if no IJobStore is wired — durable work must be persisted, not silently lost.</summary>
    private static void RegisterJobs(IServiceCollection services, LyntaiOptions options)
    {
        services.TryAddSingleton<IJobHandlerRegistry>(sp => new JobHandlerRegistry(sp.GetServices<IJobHandler>()));
        services.TryAddSingleton<IJobQueue>(sp => new JobQueue(sp.GetService<IJobStore>(), options));
        // admission control: an app can register its own to throttle lanes by external load; default admits all
        services.TryAddSingleton<IJobAdmissionController, AdmitAllAdmissionController>();
        services.TryAddSingleton<IJobRunner>(sp => new JobRunner(
            sp.GetService<IJobStore>(), sp.GetRequiredService<IJobHandlerRegistry>(), options,
            sp.GetService<ILogger<JobRunner>>(), admission: sp.GetService<IJobAdmissionController>()));
        // recurring schedules: enqueues due JobSchedules; next-run persisted via IKeyValueStore (durable
        // across restart) or in-memory when none is wired. The app drives the pump (host-free).
        services.TryAddSingleton<IJobScheduler>(sp => new JobScheduler(
            sp.GetRequiredService<IJobQueue>(), sp.GetServices<JobSchedule>(), options,
            sp.GetService<IKeyValueStore>(), sp.GetService<ILogger<JobScheduler>>()));
    }

    /// <summary>Scope-guard/jail hooks and the two-gate chat orchestrator that composes guards + memory +
    /// the tool loop into one guarded turn.</summary>
    private static void RegisterGuardsAndChat(IServiceCollection services)
    {
        // the rail gathers any registered IGuards (empty = allow everything)
        services.TryAddSingleton<IGuardRail>(sp => new GuardRail(sp.GetServices<IGuard>(), sp.GetService<ILogger<GuardRail>>()));
        services.TryAddSingleton<IChatOrchestrator>(sp => new ChatOrchestrator(
            sp.GetRequiredService<ILlmClient>(), sp.GetRequiredService<IToolLoop>(), sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IGuardRail>(), sp.GetRequiredService<IPromptComposer>(),
            sp.GetService<IMemoryStore>(), sp.GetService<Lyntai.Memory.ISemanticMemory>(),
            sp.GetService<ILogger<ChatOrchestrator>>()));
    }
}
