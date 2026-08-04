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

        // same contradiction for refusal screening: it wraps Lyntai's OWN client inside the factory below,
        // so with a pre-registered ILlmClient every AddRefusalMatcher registration would silently do nothing
        if (hadPreexistingClient && services.Any(d => d.ServiceType == typeof(IRefusalMatcher)))
            throw new InvalidOperationException(
                "An IRefusalMatcher was registered (AddRefusalMatcher), but an ILlmClient is already registered — " +
                "refusal screening wraps Lyntai's own front door and would be silently ignored. Either don't " +
                "pre-register ILlmClient, or screen replies in your own client.");

        // Compose per feature area — each block is self-contained and order-independent across areas (they
        // register distinct service types; the front-door decorators fold at resolution, not registration).
        services.AddSingleton(options);
        RegisterProviderLifetime(services);
        RegisterLlmFrontDoor(services, builder, options);
        RegisterCortex(services, options);
        RegisterConversationEnrichment(services);
        RegisterSemanticMemory(services);
        RegisterAgents(services, options);
        RegisterJobs(services, options);
        RegisterGuardsAndChat(services);

        return services;
    }

    /// <summary>Provider LIFETIME, for the app whose backend configuration is owned outside the deployment:
    /// the pooling strategy and the concurrency-admission table, both shared by the two router factories.
    ///
    /// <para>Registered unconditionally, so the pooled overloads work with no opt-in — and entirely with
    /// <c>TryAdd</c>, so a <c>Use*</c>/<c>Configure*</c> call inside the configure callback (which runs
    /// BEFORE this) or a host registration made before <c>AddLyntai</c> always wins.</para>
    ///
    /// <para>Additive by construction: an app that touches none of this resolves the container-composed
    /// routers exactly as before, over the DI provider collection and keyed on the provider id. The pool is
    /// only ever consulted through a router factory's POOLED overload, and unconfigured admission admits
    /// everyone. DELIBERATELY not registering <see cref="DeadHostTracker"/> here — the LLM front door below
    /// builds it from <see cref="LyntaiOptions"/>, and a <c>TryAdd</c> that reached the collection first
    /// would silently discard the configured threshold, cooldown and logger for BOTH domains.</para></summary>
    private static void RegisterProviderLifetime(IServiceCollection services)
    {
        // Open generic: ONE registration serves every provider seam (ILlmProvider, IGenerationProvider).
        // Never a concrete backend type — IProviderPool<SomeProvider> would be a different pool that no
        // router consults.
        services.TryAddSingleton(typeof(Lyntai.Lifecycle.IProviderPool<>), typeof(Lyntai.Lifecycle.BoundedProviderPool<>));
        services.TryAddSingleton<Lyntai.Lifecycle.ProviderPoolOptions>();
        services.TryAddSingleton<Lyntai.Lifecycle.ProviderAdmissionOptions>();
        services.TryAddSingleton<Lyntai.Lifecycle.ProviderAdmission>();
        // The routers consume the SEAM, so a host coordinating admission across processes registers its own
        // IProviderAdmission before AddLyntai and this TryAdd stands down. The concrete type stays registered
        // either way — ConfigureProviderAdmission configures THAT one, and resolving it directly must keep
        // working — but the interface is what anything downstream asks for.
        services.TryAddSingleton<Lyntai.Lifecycle.IProviderAdmission>(
            sp => sp.GetRequiredService<Lyntai.Lifecycle.ProviderAdmission>());
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
        // The chat counterpart of IGenerationRouterFactory: a router per CALLER's provider set, over the
        // ONE tracker and the ONE admission table registered above — which is the bookkeeping a consumer
        // hand-building a router per call inevitably rebuilds, and thereby throws away. Registered for the
        // same reason the generation one is: without it half the feature is unreachable through DI.
        // The container's own ILlmRouter above is left exactly as it was — no governance composes here (chat
        // spend/caching/throttling live on the ILlmClient front door), so routing it through the factory
        // would change the wiring of every existing app to no end.
        services.TryAddSingleton<ILlmRouterFactory>(sp => new LlmRouterFactory(
            sp.GetRequiredService<Lyntai.Lifecycle.IProviderPool<ILlmProvider>>(),
            sp.GetRequiredService<DeadHostTracker>(), options,
            sp.GetService<ILoggerFactory>(), sp.GetService<Lyntai.Llm.Routing.IModelRoutingStore>(),
            sp.GetService<Lyntai.Lifecycle.IProviderAdmission>()));
        // Default candidates internal. Any registered front-door decorators (response cache, usage budget, …)
        // are folded over the base client in ascending Order (the decorator's declared position — NOT raw
        // registration order), so they compose predictably instead of clobbering.
        services.TryAddSingleton<ILlmClient>(sp =>
        {
            ILlmClient client = new LlmClient(sp.GetRequiredService<ILlmRouter>(), options);
            foreach (var (_, decorate) in builder.FrontDoorDecorators.OrderBy(d => d.Order))
                client = decorate(sp, client);
            // refusal screening (per-request LlmRequest.RefusalPattern + any registered IRefusalMatcher) is
            // OUTERMOST + always on (the pattern is a request field), so it re-screens even a cached hit.
            // Deliberately NOT in FrontDoorDecorators, so it doesn't trip the "decorators configured but
            // ILlmClient pre-registered" guard above.
            return new RefusalScreeningLlmClient(client, sp.GetServices<IRefusalMatcher>(),
                sp.GetService<ILogger<RefusalScreeningLlmClient>>());
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

    /// <summary>When any <see cref="IConversationEnricher"/> is registered, decorate the resolved
    /// conversation store with <see cref="EnrichingConversationStore"/> so the app's enrichers fire after
    /// each write — composing over whatever backend (or BYO impl) is registered, without replacing it. No
    /// enrichers → the plain backend store resolves unwrapped (zero overhead).</summary>
    private static void RegisterConversationEnrichment(IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IConversationEnricher))) return;
        // wrap the LAST-registered IConversationStore (the effective backend / BYO impl); keyed
        // registrations are skipped — accessing a keyed descriptor's implementation members throws
        var backend = services.LastOrDefault(d => d.ServiceType == typeof(IConversationStore) && !d.IsKeyedService);
        if (backend is null) return; // no conversation store wired → nothing to enrich

        services.Remove(backend);
        // PRESERVE the original lifetime — a BYO store registered scoped/transient must not be silently
        // promoted to singleton (one cached instance + potential captive dependencies)
        services.Add(ServiceDescriptor.Describe(typeof(IConversationStore), sp =>
        {
            var inner = (IConversationStore)(backend.ImplementationInstance
                ?? backend.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.GetServiceOrCreateInstance(sp, backend.ImplementationType!));
            return new EnrichingConversationStore(inner, sp.GetServices<IConversationEnricher>());
        }, backend.Lifetime));
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
            sp.GetRequiredService<ILlmClient>(), sp.GetRequiredService<IToolRegistry>(), options,
            sp.GetService<ILogger<ToolLoop>>(), guards: sp.GetService<Lyntai.Guards.IGuardRail>()));
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
