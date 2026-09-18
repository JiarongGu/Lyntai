using Lyntai.Inference;
using Lyntai;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Guards;
using Lyntai.Jobs;
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
        // What is OBSERVED is only "a LyntaiOptions descriptor exists", and LyntaiOptions is a normal DI
        // citizen the adapters resolve — so a host that registered one by hand lands here too, and the
        // message names that cause rather than asserting a second call that never happened.
        if (services.Any(d => d.ServiceType == typeof(LyntaiOptions)))
            throw new InvalidOperationException(
                "A LyntaiOptions is already registered on this IServiceCollection. Either AddLyntai has already been " +
                "called — call it once and compose all providers, storage, and scorers in the single configure " +
                "callback — or a LyntaiOptions was registered by hand, which AddLyntai's own registration would " +
                "shadow; configure it inside the callback with builder.Configure(...) instead.");

        // a consumer-supplied ITextClient registered BEFORE AddLyntai would make the base TryAddSingleton
        // below no-op — silently dropping any front-door decorators (cache/budget/rate-limit). Catch that
        // contradiction rather than let governance vanish without a trace. Captured HERE, before the
        // callback runs, so it observes pre-AddLyntai registrations ONLY: a client registered on
        // builder.Services inside the callback, or on the collection after AddLyntai returns, discards the
        // same decorators and no predicate placed in this method could see it.
        var hadPreexistingClient = services.Any(d => d.ServiceType == typeof(ITextClient));

        var options = new LyntaiOptions();
        var builder = new LyntaiBuilder(services, options);
        configure(builder);
        options.ApplyEnvOverrides();

        if (hadPreexistingClient && builder.FrontDoorDecorators.Count > 0)
            throw new InvalidOperationException(
                "A front-door decorator (AddResponseCache / AddUsageBudget / AddRateLimit) was configured, but an " +
                "ITextClient was registered BEFORE AddLyntai — the decorators would be silently ignored. Either don't " +
                "pre-register ITextClient, or use the BYO seams (IResponseCache / IUsageTracker / IRateLimiter) instead. " +
                "This check sees pre-AddLyntai registrations only: registering ITextClient on builder.Services inside " +
                "the configure callback, or on the collection after AddLyntai returns, drops every front-door " +
                "decorator with no error at all — layer your own with AddFrontDoorDecorator instead.");

        // AddSemanticMemory states an intent the wiring below can only honor when something can embed —
        // otherwise ISemanticMemory is never registered and every recall path skips it in silence. The
        // whole point of naming the feature is to turn that quiet degradation into a startup failure.
        //
        // TWO ROUTES, because capability is only knowable once a provider is BUILT and this must decide
        // before that. A factory STATES it through `AddProvider`'s `declares` argument, which is the only
        // thing a deferred registration can do. A host registering an INSTANCE before `AddLyntai` states
        // nothing — but the instance is right there in the descriptor, so its declared `Capabilities` can be
        // read without building anything. Dropping the second route is what made a BYO backend registered
        // outside the builder invisible, while the error text told the consumer to do exactly that.

        // FIRST, so "you declared Vector but did not implement it" is reported as that rather than as the
        // downstream "nothing produces vectors", which names a symptom instead of the defect.
        RefuseCapabilityMismatch(services);

        if (builder.SemanticMemoryRequested && !VectorBackendIsWired(services, builder))
            throw new InvalidOperationException(
                "AddSemanticMemory was called, but no registered backend produces vectors, so "
                + "ISemanticMemory would never be wired and semantic recall would silently do nothing. "
                + Lyntai.Inference.EmbeddingRouting.NothingEmbeds
                + " Or drop the AddSemanticMemory call.");

        // same contradiction for refusal screening: it wraps Lyntai's OWN client inside the factory below,
        // so with a pre-registered ITextClient every AddRefusalMatcher registration would silently do nothing
        if (hadPreexistingClient && services.Any(d => d.ServiceType == typeof(IRefusalMatcher)))
            throw new InvalidOperationException(
                "An IRefusalMatcher was registered (AddRefusalMatcher), but an ITextClient was registered BEFORE " +
                "AddLyntai — refusal screening wraps Lyntai's own front door and would be silently ignored. Either " +
                "don't pre-register ITextClient, or screen replies in your own client. As above, only pre-AddLyntai " +
                "registrations are visible here.");

        // Compose per feature area — each block is self-contained and order-independent across areas (they
        // register distinct service types; the front-door decorators fold at resolution, not registration).
        services.AddSingleton(options);
        RegisterProviderLifetime(services);
        RegisterTextFrontDoor(services, builder, options);
        RegisterCortex(services, options);
        RegisterConversationEnrichment(services);
        RegisterSemanticMemory(services, builder);
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
        // Open generic over IProviderIdentity, not closed over IModelProvider: since D127 collapsed the
        // domain seams there is one to close over, and the open form is what keeps the pool reusable for
        // the next one rather than needing a second registration.
        // Never a concrete backend type — IProviderPool<SomeProvider> would be a different pool that no
        // router consults.
        services.TryAddSingleton(typeof(Lyntai.Inference.IProviderPool<>), typeof(Lyntai.Inference.BoundedProviderPool<>));
        services.TryAddSingleton<Lyntai.Inference.ProviderPoolOptions>();
        services.TryAddSingleton<Lyntai.Inference.ProviderAdmissionOptions>();
        services.TryAddSingleton<Lyntai.Inference.ProviderAdmission>();
        // The routers consume the SEAM, so a host coordinating admission across processes registers its own
        // IProviderAdmission before AddLyntai and this TryAdd stands down. The concrete type stays registered
        // either way — ConfigureProviderAdmission configures THAT one, and resolving it directly must keep
        // working — but the interface is what anything downstream asks for.
        services.TryAddSingleton<Lyntai.Inference.IProviderAdmission>(
            sp => sp.GetRequiredService<Lyntai.Inference.ProviderAdmission>());
    }

    /// <summary>The LLM front door: process runner, dead-host tracker, router, and the consumer
    /// <see cref="ITextClient"/> — Lyntai behaving like ONE provider, with any front-door decorators folded
    /// over the base client.</summary>
    private static void RegisterTextFrontDoor(IServiceCollection services, LyntaiBuilder builder, LyntaiOptions options)
    {
        services.TryAddSingleton<IProcessRunner, ProcessRunner>(); // BYO: register your own IProcessRunner first to override spawning
        services.TryAddSingleton(sp => new DeadHostTracker(
            options.DeadHostThreshold, options.DeadHostCooldown, logger: sp.GetService<ILogger<DeadHostTracker>>()));
        services.TryAddSingleton<ITextRouter>(sp => new TextRouter(
            sp.GetServices<IModelProvider>(), sp.GetRequiredService<DeadHostTracker>(), options,
            sp.GetService<ILogger<TextRouter>>(), modelRouting: sp.GetService<Lyntai.Inference.IModelRoutingStore>()));
        // The chat counterpart of IMediaRouterFactory: a router per CALLER's provider set, over the
        // ONE tracker and the ONE admission table registered above — which is the bookkeeping a consumer
        // hand-building a router per call inevitably rebuilds, and thereby throws away. Registered for the
        // same reason the generation one is: without it half the feature is unreachable through DI.
        // The container's own ITextRouter above is left exactly as it was — no governance composes here (chat
        // spend/caching/throttling live on the ITextClient front door), so routing it through the factory
        // would change the wiring of every existing app to no end.
        services.TryAddSingleton<ITextRouterFactory>(sp => new TextRouterFactory(
            sp.GetRequiredService<Lyntai.Inference.IProviderPool<IModelProvider>>(),
            sp.GetRequiredService<DeadHostTracker>(), options,
            sp.GetService<ILoggerFactory>(), sp.GetService<Lyntai.Inference.IModelRoutingStore>(),
            sp.GetService<Lyntai.Inference.IProviderAdmission>()));
        // Default candidates internal. Any registered front-door decorators (response cache, usage budget, …)
        // are folded over the base client in ascending Order (the decorator's declared position — NOT raw
        // registration order), so they compose predictably instead of clobbering.
        services.TryAddSingleton<ITextClient>(sp => Compose(sp, sp.GetRequiredService<ITextRouter>()));

        // Named clients (AddTextClient) — the chat counterpart of the memory engine registry. Each is the
        // SAME composition as the default client over a narrower provider set: base client, the same
        // front-door decorators in the same order, the same outermost refusal screening. Sharing the fold
        // rather than re-deriving it is what keeps a name from silently meaning "fewer rules"; see
        // ITextClientFactory on why a name selects backends and never permissions.
        //
        // A name narrows the PROVIDER set and its CANDIDATE list together. Narrowing only the first is what
        // made a client pooled outside the global candidate list resolve and then fail every call —
        // ClientCandidates.Resolve is where that pairing lives.
        services.TryAddSingleton<ITextClientFactory>(sp =>
        {
            var named = builder.NamedTextClients.ToDictionary(
                entry => entry.Key,
                entry => ComposeNamed(sp, entry.Key, entry.Value),
                StringComparer.Ordinal);
            VerifySeamModelPins(sp);
            return new TextClientFactory(named, sp.GetRequiredService<ITextClient>());
        });

        // A seam that pinned a MODEL its client can never honour, failed here rather than per call (D119).
        //
        // The router resolves `candidate.Model ?? request.Model`, so a candidate that pins a model outranks
        // a seam's. That precedence is CORRECT — a candidate is a provider-and-model pair, and letting the
        // request win would dissolve its identity — so what is wrong is only that the losing case was
        // SILENT: both memory seams are fail-open, so the judge or annotator ran on another model and
        // nothing reported it. Same argument as the pooled-candidate check above: a setting that can never
        // take effect is worth hearing about at startup rather than never.
        void VerifySeamModelPins(IServiceProvider sp)
        {
            foreach (var (seam, clientName, model) in builder.SeamModelPins)
            {
                // A named client's OWN list, never the global one — a name narrows candidates as well as
                // providers, so checking the global list would both miss a real contradiction and invent a
                // false one.
                var candidates = clientName is { } name && builder.NamedTextClients.TryGetValue(name, out var c)
                    ? ClientCandidates.Resolve(c.ProviderIds, c.Candidates, options.DefaultCandidates)
                    : options.DefaultCandidates;

                if (!ClientCandidates.ModelPinIsInert(model, candidates)) continue;

                var where = clientName is { } n ? $"client '{n}'" : "the default client";
                throw new InvalidOperationException(
                    $"{seam} pins Model '{model}', but every candidate {where} routes over pins a model of " +
                    $"its own and none is '{model}' " +
                    $"({string.Join(", ", candidates.Select(c => $"{c.ProviderId}:{c.Model}"))}). " +
                    "The router resolves `candidate.Model ?? request.Model`, so this setting can never take " +
                    "effect and the seam would silently run on another model. Name a client whose candidates " +
                    "pin the model you want (ClientName), or drop the Model.");
            }
        }

        ITextClient ComposeNamed(IServiceProvider sp, string name, TextClientBuilder client)
        {
            // Checked against the RESOLVED pool, never against the declared ids: naming no provider pools
            // every registered one, so a stated candidate is outside it exactly when nothing registered
            // answers to that id — the same hole one layer along from the one UseProviders already closes.
            var providers = ProvidersFor(sp, name, client.ProviderIds);
            var outside = ClientCandidates.OutsideThePool([.. providers.Select(p => p.Id)], client.Candidates);
            if (outside.Count > 0)
                throw new InvalidOperationException(
                    $"LLM client '{name}' states candidate(s) {string.Join(", ", outside)}, which name no " +
                    "backend it is pooled over " +
                    $"({(providers.Count == 0 ? "(none)" : string.Join(", ", providers.Select(p => p.Id)))}). " +
                    "A candidate the router cannot select fails every call; add it to UseProviders, register " +
                    "it, or drop it.");

            var router = sp.GetRequiredService<ITextRouterFactory>().For(providers);
            return Compose(sp, router,
                ClientCandidates.Resolve(client.ProviderIds, client.Candidates, options.DefaultCandidates));
        }

        // THE FRONT DOOR, built ONCE for every client this container hands out. Only the ROUTER differs
        // between the default client and a named one — the default takes the container's own ITextRouter,
        // a name takes one narrowed to its provider set — and that difference is the whole point of a name,
        // so it is the parameter. Everything outside it is the governance promise and must not vary.
        //
        // It was written twice until 3.0, and the comment above the named registration ASSERTED the parity
        // the two copies were supposed to maintain by hand. Nothing enforced it: deleting the refusal
        // screening from the named copy left the whole suite green, and any new outermost layer added to the
        // default would have been silently absent from every named client — which is what
        // AddMemoryAnnotation and AddMemoryVerification resolve through. Same shape as MemoryEngineBuilder's
        // two construction sites (docs/FIXES.md), where an optional argument added to one path and not the
        // other left a documented knob unwired and the compiler could not see it.
        ITextClient Compose(IServiceProvider sp, ITextRouter router,
            IReadOnlyList<ProviderCandidate>? candidates = null)
        {
            ITextClient client = new TextClient(router, options, candidates);
            foreach (var (_, decorate) in builder.FrontDoorDecorators.OrderBy(d => d.Order))
                client = decorate(sp, client);
            // refusal screening (per-request TextRequest.RefusalPattern + any registered IRefusalMatcher) is
            // OUTERMOST + always on (the pattern is a request field), so it re-screens even a cached hit.
            // Deliberately NOT in FrontDoorDecorators, so it doesn't trip the "decorators configured but
            // ITextClient pre-registered" guard above.
            return new RefusalScreeningTextClient(client, sp.GetServices<IRefusalMatcher>(),
                sp.GetService<ILogger<RefusalScreeningTextClient>>());
        }

        // An id naming no registered provider THROWS rather than narrowing to whatever does exist: a
        // subsystem pointed at a backend this host never registered must fail loudly, because degrading to
        // the app's default is the outcome naming a client exists to prevent.
        static IReadOnlyList<IModelProvider> ProvidersFor(
            IServiceProvider sp, string clientName, IReadOnlyList<string> ids)
        {
            var all = sp.GetServices<IModelProvider>().ToList();
            if (ids.Count == 0) return all;

            var byId = all.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"LLM client '{clientName}' names backend(s) {string.Join(", ", missing)}, which are not " +
                    $"registered. Registered: {(all.Count == 0 ? "(none)" : string.Join(", ", all.Select(p => p.Id)))}.");

            return [.. ids.Select(id => byId[id])];
        }
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
        services.TryAddSingleton<IPairwiseComparer>(sp => new LlmPairwiseComparer(sp.GetRequiredService<ITextClient>()));
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

    /// <summary>Whether anything in the container will be able to embed, decided WITHOUT building a
    /// provider — which is the constraint that shapes this.
    ///
    /// <para>TWO routes, and they are not the same code. A factory cannot be inspected, so it STATES what it
    /// will produce through <c>AddProvider</c>'s <c>declares</c> argument (<c>docs/DECISIONS.md</c>
    /// <b>D152</b>). An instance registration needs no statement: the object is in the descriptor and
    /// declares its own <see cref="Lyntai.Inference.ProviderCapabilities"/>. Reading it is what keeps a host
    /// registration made before <c>AddLyntai</c> working, and reading the CAPABILITY rather than counting
    /// providers is what keeps a chat-only deployment failing fast.</para>
    ///
    /// <para><b>Both arms ask the same question of the same type</b>, so a backend that declares nothing
    /// answers "no" — deliberately, since the alternative is wiring a recall that cannot run.</para></summary>
    private static bool VectorBackendIsWired(IServiceCollection services, LyntaiBuilder builder) =>
        builder.DeclaredCapabilities.Any(Embeds)
        || services.Any(d => !d.IsKeyedService
            && d.ServiceType == typeof(Lyntai.Inference.IModelProvider)
            && d.ImplementationInstance is Lyntai.Inference.IModelProvider p
            && Embeds(p.Capabilities));

    /// <summary>Text in, vectors out — the one shape <c>AddSemanticMemory</c> needs, asked identically of a
    /// declaration and of a built instance so the two arms cannot drift.</summary>
    private static bool Embeds(Lyntai.Inference.ProviderCapabilities capabilities) =>
        capabilities.Supports(
            Lyntai.Inference.ProviderKinds.Vector,
            Lyntai.Inference.ProviderOperation.Complete,
            accepts: Lyntai.Inference.ProviderKinds.Text);

    /// <summary>Refuse a backend whose DECLARATION and IMPLEMENTATION disagree: it says it produces vectors
    /// and does not implement <see cref="Lyntai.Inference.IVectorProvider"/>.
    ///
    /// <para><b>The failure it replaces is silent.</b> Routing selects on the type test, and
    /// <c>AddSemanticMemory</c>'s own check reads the declaration — so such a backend satisfies composition,
    /// is never selected at run time, and semantic recall simply returns nothing. It cost a sample exactly
    /// that on the day the seam landed, which is why the check exists rather than a note
    /// (<c>docs/DECISIONS.md</c> <b>D153</b>).</para>
    ///
    /// <para>Instance registrations only, for the same reason the wiring check has two arms: a factory
    /// cannot be inspected before it runs.</para></summary>
    private static void RefuseCapabilityMismatch(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService
                || descriptor.ServiceType != typeof(Lyntai.Inference.IModelProvider)
                || descriptor.ImplementationInstance is not Lyntai.Inference.IModelProvider provider)
                continue;

            Refuse(provider, Embeds(provider.Capabilities),
                provider is Lyntai.Inference.IVectorProvider,
                nameof(Lyntai.Inference.ProviderKinds.Vector), nameof(Lyntai.Inference.IVectorProvider),
                "semantic recall would return nothing");

            Refuse(provider, Scores(provider.Capabilities),
                provider is Lyntai.Inference.IScoreProvider,
                nameof(Lyntai.Inference.ProviderKinds.Score), nameof(Lyntai.Inference.IScoreProvider),
                "every recall would go unverified");
        }

        static void Refuse(
            Lyntai.Inference.IModelProvider provider, bool declares, bool implements, string kind,
            string seam, string consequence)
        {
            if (!declares || implements) return;
            throw new InvalidOperationException(
                $"Backend '{provider.Id}' ({provider.GetType().Name}) declares ProviderKinds.{kind} but does "
                + $"not implement {seam}, so nothing would ever route that call to it — {consequence} with no "
                + $"error at all. Implement {seam}, or drop {kind} from its ProviderCapabilities.Produces.");
        }
    }

    /// <summary>Text in, scores out — the shape <c>AddMemoryScoringVerification</c> selects on.</summary>
    private static bool Scores(Lyntai.Inference.ProviderCapabilities capabilities) =>
        capabilities.Supports(
            Lyntai.Inference.ProviderKinds.Score,
            Lyntai.Inference.ProviderOperation.Complete,
            accepts: Lyntai.Inference.ProviderKinds.Text);

    /// <summary>Semantic memory — wired ONLY when a backend producing
    /// <see cref="Lyntai.Inference.ProviderKinds.Vector"/> is registered. Composes the registered providers
    /// with a vector store (in-memory default; register your own <c>IVectorStore</c> for pgvector/etc.).
    ///
    /// <para><b>There is no front door to seed any more</b> (<c>docs/DECISIONS.md</c> <b>D151</b>): embedding
    /// is a capability, so <c>SemanticMemory</c> takes the providers themselves and routes over whichever
    /// declare it. What used to be a <c>TryAdd</c> seeding an <c>ProviderKinds.Vector</c> backend is now nothing at all, and
    /// bring-your-own is a provider registration like any other.</para>
    ///
    /// <para>Absent one it is not registered, so the composer/orchestrator resolve null and skip it — no
    /// accidental throws on every turn. An app that MEANT to have it says so with <c>AddSemanticMemory</c>,
    /// and the guard above turns the silent skip into a composition-time throw.</para></summary>
    private static void RegisterSemanticMemory(IServiceCollection services, LyntaiBuilder builder)
    {
        if (!VectorBackendIsWired(services, builder)) return;
        services.TryAddSingleton<Lyntai.Memory.IVectorStore, Lyntai.Memory.InMemoryVectorStore>();
        services.TryAddSingleton<Lyntai.Memory.ISemanticMemory>(sp => new Lyntai.Memory.SemanticMemory(
            sp.GetServices<Lyntai.Inference.IModelProvider>(), sp.GetRequiredService<Lyntai.Memory.IVectorStore>(),
            sp.GetService<ILogger<Lyntai.Memory.SemanticMemory>>()));
    }

    /// <summary>Agentic tool-calling: the registry gathers any registered ITools; the loop runs provider-
    /// agnostically over the front door (works with zero tools too — it degenerates to one completion).</summary>
    private static void RegisterAgents(IServiceCollection services, LyntaiOptions options)
    {
        services.TryAddSingleton<IToolRegistry>(sp => new ToolRegistry(sp.GetServices<ITool>()));
        // GetService for the selector, never GetRequiredService: it is opt-in, and an unregistered one means
        // "show every tool", which is what the loop did before the seam existed.
        services.TryAddSingleton<IToolLoop>(sp => new ToolLoop(
            sp.GetRequiredService<ITextClient>(), sp.GetRequiredService<IToolRegistry>(), options,
            sp.GetService<ILogger<ToolLoop>>(), guards: sp.GetService<Lyntai.Guards.IGuardRail>(),
            selector: sp.GetService<IToolSelector>()));
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
            sp.GetRequiredService<ITextClient>(), sp.GetRequiredService<IToolLoop>(), sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<IGuardRail>(), sp.GetRequiredService<IPromptComposer>(),
            sp.GetService<IMemoryStore>(), sp.GetService<Lyntai.Memory.ISemanticMemory>(),
            sp.GetService<ILogger<ChatOrchestrator>>()));
    }
}
