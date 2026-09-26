using Lyntai.Inference;
using System.Diagnostics.CodeAnalysis;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Guards;
using Lyntai.Jobs;
using Lyntai.Inference.Budgeting;
using Lyntai.Inference.Caching;
using Lyntai.Inference.RateLimiting;
using Lyntai.Memory;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lyntai;

/// <summary>
/// Collects the composition of a Lyntai instance inside <c>services.AddLyntai(cfg => …)</c>.
/// Provider/storage packages extend this with their own <c>Add*</c>/<c>Use*</c> extension methods
/// (e.g. <c>AddClaudeCliProvider()</c>, <c>UseSqliteStorage(path)</c>) — Core knows none of them.
/// </summary>
public sealed class LyntaiBuilder
{
    internal LyntaiBuilder(IServiceCollection services, LyntaiOptions options)
    {
        Services = services;
        Options = options;
    }

    public IServiceCollection Services { get; }

    public LyntaiOptions Options { get; }

    /// <summary>Front-door decorators (response cache, usage budget, …) folded over the base router-backed
    /// client by <c>AddLyntai</c> in ascending <c>Order</c> (lower = innermost, higher = outermost) — so
    /// multiple compose deterministically regardless of the order they were added, instead of clobbering
    /// one another.</summary>
    internal List<(int Order, Func<IServiceProvider, ITextClient, ITextClient> Decorate)> FrontDoorDecorators { get; } = [];

    /// <summary>Who holds each <see cref="FrontDoorDecorators"/> slot — a built-in's name, or a custom
    /// decorator's delegate — so a repeat by the same owner is told apart from a collision.</summary>
    private readonly Dictionary<int, object> _decoratorOwners = [];

    /// <summary>Named LLM clients (<c>AddTextClient</c>) → what each was configured with: the backend ids it
    /// routes over (empty meaning "every registered provider") and any candidate list it stated outright.
    /// Composed by <c>AddLyntai</c> into <see cref="Lyntai.Inference.ITextClientFactory"/> — the chat counterpart of
    /// the memory engine registry.</summary>
    internal Dictionary<string, TextClientBuilder> NamedTextClients { get; } = new(StringComparer.Ordinal);

    /// <summary>Every seam that pinned a MODEL, recorded so composition can check it against the candidates
    /// its client actually routes over. Recorded rather than checked on the spot because the client may be
    /// registered AFTER the seam — composition-root order is deliberately not load-bearing here, which is
    /// the same promise <see cref="TextClientBuilder.UseProviders"/> makes about naming backends.</summary>
    internal List<(string Seam, string? ClientName, string Model)> SeamModelPins { get; } = [];

    // Fold order (higher = outer). The cache is OUTERMOST so a hit returns without touching inner
    // decorators — in particular a cached hit is free and must NOT count toward the usage budget or spend a
    // rate-limit permit. Rate-limit is innermost (closest to the provider — it throttles real calls).
    // Public so a custom decorator (AddFrontDoorDecorator) can position itself relative to the built-ins.
    /// <summary>Fold order of the built-in rate-limit decorator (innermost — closest to the provider).</summary>
    public const int RateLimitDecoratorOrder = 5;
    /// <summary>Fold order of the built-in usage-budget decorator.</summary>
    public const int BudgetDecoratorOrder = 10;
    /// <summary>Fold order of the built-in response-cache decorator (outermost governance layer — a hit
    /// short-circuits without spending budget/rate-limit).</summary>
    public const int CacheDecoratorOrder = 20;

    /// <summary>What a DEFERRED registration said it would produce — the capabilities passed to an
    /// <c>AddProvider</c> overload, for the composition-time questions that cannot wait for a provider to be
    /// built.
    ///
    /// <para><b>A declaration, never a substitute for the real thing.</b> Routing always reads the built
    /// provider's own <see cref="IModelProvider.Capabilities"/>; nothing here reaches a router. This list
    /// answers one narrower question — "will anything in this container be able to do X?" — asked while the
    /// container is still being described.</para></summary>
    internal List<ProviderCapabilities> DeclaredCapabilities { get; } = [];

    /// <summary>Register an <see cref="IModelProvider"/> into the router's provider collection.</summary>
    /// <param name="declares">Optional. What this backend will produce, stated NOW because a type the
    /// container constructs cannot be inspected until it is built — see the
    /// <see cref="AddProvider(Func{IServiceProvider,IModelProvider},ProviderCapabilities)"/> overload, which
    /// documents why and what omitting it costs.</param>
    public LyntaiBuilder AddProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        ProviderCapabilities? declares = null)
        where T : class, IModelProvider
    {
        if (declares is not null) DeclaredCapabilities.Add(declares);
        Services.AddSingleton<IModelProvider, T>();
        return this;
    }

    /// <summary>Register a provider built from the service provider (for id/config-parameterized ones) —
    /// <b>the one door every backend comes through, whatever it produces</b> (<c>docs/DECISIONS.md</c>
    /// <b>D152</b>).
    ///
    /// <para><b>Pass <paramref name="declares"/> when a composition-time decision depends on this
    /// backend.</b> A factory is opaque until it runs, so a seam that must decide while the container is
    /// being described — <see cref="AddSemanticMemory()"/> is the one that ships — cannot see what this will
    /// produce. Stating it here is what lets that seam fail fast, and a backend that declares
    /// <see cref="ProviderKinds.Vector"/> is what <c>AddSemanticMemory</c> looks for.</para>
    ///
    /// <para><b>Omitting it is safe but not free, and the cost is exactly one thing:</b> routing is
    /// unaffected — it reads each built provider's own <see cref="IModelProvider.Capabilities"/> — but a
    /// composition-time question about this backend answers "no". So an undeclared vector backend plus
    /// <c>AddSemanticMemory</c> is a startup failure naming this argument, rather than a recall that quietly
    /// never runs.</para>
    ///
    /// <para><b>A capability interface does NOT make this redundant</b>, though it looks as though it
    /// should: a type test says what a class CAN do, and this says what the REGISTRATION does
    /// (<c>docs/DECISIONS.md</c> <b>D153</b>).</para>
    ///
    /// <para>Registering an INSTANCE on <see cref="Services"/> before <c>AddLyntai</c> needs no declaration:
    /// the object is in the descriptor and states its own capabilities.</para></summary>
    /// <param name="factory">Builds the backend from the container.</param>
    /// <param name="declares">Optional. What the built provider will declare.</param>
    public LyntaiBuilder AddProvider(
        Func<IServiceProvider, IModelProvider> factory, ProviderCapabilities? declares = null)
    {
        if (declares is not null) DeclaredCapabilities.Add(declares);
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register a backend from a FUNCTION — the general form of bridging something that can
    /// already answer into this library.
    ///
    /// <para><b>Use it when a backend is reachable but has no provider here</b>: a vendor SDK, an in-house
    /// service, a <c>Microsoft.Extensions.AI</c> <c>IChatClient</c>. You write the mapping you actually
    /// need — usually a few lines — and routing, fallback, dead-host cooldown, admission and the ops layer
    /// come along. Nothing about the caller's ecosystem reaches this library, which is why a bridge costs no
    /// dependency (<c>docs/DECISIONS.md</c> D147).</para>
    ///
    /// <para><b>For an OpenAI-shaped endpoint, use <c>AddHttpProvider</c> instead</b> — most vendors
    /// ship one, and it already handles verdicts, streaming, usage, tool calls and the per-wire
    /// differences.</para></summary>
    /// <param name="id">The router-facing id, as on any backend: a LABEL for one configured client, so two
    /// configurations of the same vendor are two bridges with two ids.</param>
    /// <param name="complete">Answers one request. Report failure as a non-Ok <see cref="ProviderVerdict"/>
    /// on the reply rather than by throwing, so the router can fall over to the next candidate — a throw is
    /// classified, but a verdict says what happened.</param>
    /// <param name="stream">Optional. Supplied, the bridge declares <see cref="ProviderOperation.Stream"/>;
    /// omitted, it declares only <see cref="ProviderOperation.Complete"/> and a router never asks it to
    /// stream.</param>
    /// <param name="capabilities">Optional. Defaults to text in, text out, with the operations implied by
    /// which delegates were supplied. Pass one to declare something else — tool calls, an input kind such as
    /// an image, a model list, declared limits; an <c>Accepts</c>, <c>Produces</c> or <c>Operations</c> it
    /// leaves empty takes that default. A bridge answers TEXT: an embedder or reranker implements
    /// <see cref="IVectorProvider"/> or <see cref="IScoreProvider"/>, which no delegate here supplies.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is blank, or <paramref name="capabilities"/>
    /// declares a <c>Produces</c> kind other than <see cref="ProviderKinds.Text"/> — no router would ever
    /// select the bridge for it (<c>docs/DECISIONS.md</c> D147).</exception>
    public LyntaiBuilder AddBridgeProvider(
        string id,
        Func<TextRequest, CancellationToken, Task<TextResponse>> complete,
        Func<TextRequest, CancellationToken, IAsyncEnumerable<TextChunk>>? stream = null,
        ProviderCapabilities? capabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(complete);

        var defaults = new ProviderCapabilities
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Text],
            Operations = stream is null
                ? [ProviderOperation.Complete]
                : [ProviderOperation.Complete, ProviderOperation.Stream],
        };
        // the delegates take and return text, so an empty kind means "the default", never "serves nothing"
        var declared = capabilities is null ? defaults : capabilities with
        {
            Accepts = capabilities.Accepts.Count > 0 ? capabilities.Accepts : defaults.Accepts,
            Produces = capabilities.Produces.Count > 0 ? capabilities.Produces : defaults.Produces,
            Operations = capabilities.Operations.Count > 0 ? capabilities.Operations : defaults.Operations,
        };
        // D153's declaration/implementation check, applied here because a factory registration escapes it
        foreach (var kind in declared.Produces)
            if (!string.Equals(kind, ProviderKinds.Text, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"Bridge '{id}' declares ProviderKinds '{kind}', but its delegates answer text, so no router would "
                    + "ever select it for that. An embedder or reranker implements IVectorProvider or IScoreProvider "
                    + "and registers with AddProvider; drop the kind from Produces.", nameof(capabilities));
        return AddProvider(_ => new BridgeProvider(id, declared, complete, stream));
    }

    /// <summary>Register an eval dimension into the scoring collection.</summary>
    public LyntaiBuilder AddScorer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IScorer
    {
        Services.AddSingleton<IScorer, T>();
        return this;
    }

    /// <summary>Register a scorer built from the service provider — for config/dependency-parameterized
    /// scorers the generic overload can't construct.</summary>
    public LyntaiBuilder AddScorer(Func<IServiceProvider, IScorer> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register an <see cref="IConversationEnricher"/> into the enricher collection —
    /// the app's "add additional info" seam. Lyntai owns the conversation store; each registered enricher is
    /// invoked after a thread/message write to persist the app's own info (in its own store), without
    /// replacing the store. Add a class + one registration, never a fork.</summary>
    public LyntaiBuilder AddConversationEnricher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IConversationEnricher
    {
        Services.AddSingleton<IConversationEnricher, T>();
        return this;
    }

    /// <summary>Register a conversation enricher built from the service provider.</summary>
    public LyntaiBuilder AddConversationEnricher(Func<IServiceProvider, IConversationEnricher> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register a typed <see cref="IRefusalMatcher"/> into the refusal-screening front
    /// door — the structured alternative to a per-request <c>RefusalPattern</c> regex. Every registered
    /// matcher runs on an Ok reply's text (after the central patterns + the request pattern); any that
    /// returns true surfaces the reply as <c>Refused</c> (no fallback). Add a class + one registration.</summary>
    public LyntaiBuilder AddRefusalMatcher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IRefusalMatcher
    {
        Services.AddSingleton<IRefusalMatcher, T>();
        return this;
    }

    /// <summary>Register a refusal matcher instance.</summary>
    public LyntaiBuilder AddRefusalMatcher(IRefusalMatcher matcher)
    {
        Services.AddSingleton(matcher);
        return this;
    }

    /// <summary>Register a refusal matcher built from the service provider.</summary>
    public LyntaiBuilder AddRefusalMatcher(Func<IServiceProvider, IRefusalMatcher> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register an <see cref="ITool"/> into the tool-loop's tool collection.</summary>
    public LyntaiBuilder AddTool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, ITool
    {
        Services.AddSingleton<ITool, T>();
        return this;
    }

    /// <summary>Register a tool built from the service provider (for config/dependency-parameterized
    /// ones, or an inline <see cref="FunctionTool"/>).</summary>
    public LyntaiBuilder AddTool(Func<IServiceProvider, ITool> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register an <see cref="IJobHandler"/> into the durable-job handler collection
    /// (keyed by its <c>Type</c>).</summary>
    public LyntaiBuilder AddJobHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IJobHandler
    {
        Services.AddSingleton<IJobHandler, T>();
        return this;
    }

    /// <summary>Register a job handler built from the service provider.</summary>
    public LyntaiBuilder AddJobHandler(Func<IServiceProvider, IJobHandler> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Replace the default admit-all <see cref="IJobAdmissionController"/> with one the
    /// runner consults per lane before claiming — so the app can throttle lanes by external load / a
    /// maintenance window. Registered as the singleton controller (last registration wins).</summary>
    public LyntaiBuilder AddJobAdmissionController<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IJobAdmissionController
    {
        Services.AddSingleton<IJobAdmissionController, T>();
        return this;
    }

    /// <summary>Register a job admission controller built from the service provider.</summary>
    public LyntaiBuilder AddJobAdmissionController(Func<IServiceProvider, IJobAdmissionController> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register a recurring job: every <paramref name="every"/>, the <see cref="IJobScheduler"/>
    /// enqueues a <paramref name="type"/> job on <paramref name="lane"/> with <paramref name="payload"/>.
    /// Validated as <see cref="AddJobSchedule(JobSchedule)"/> says. The app drives the scheduler's pump
    /// (TickAsync/RunAsync).</summary>
    public LyntaiBuilder AddJobSchedule(string name, string lane, string type, string payload, TimeSpan every, int priority = 0) =>
        AddJobSchedule(new JobSchedule(name, lane, type, payload, every, priority));

    /// <summary>Register a recurring job on a <b>cron</b> schedule (5-field <c>min hour dom month dow</c>, or
    /// a macro like <c>@daily</c>; evaluated in UTC). Validated as <see cref="AddJobSchedule(JobSchedule)"/>
    /// says. The app drives the scheduler pump.</summary>
    public LyntaiBuilder AddCronSchedule(string name, string lane, string type, string payload, string cron, int priority = 0) =>
        AddJobSchedule(new JobSchedule(name, lane, type, payload, Cron: cron, Priority: priority));

    /// <summary>Register a recurring <see cref="JobSchedule"/> — the door every schedule registration comes
    /// through, so each is validated HERE, at composition, rather than skipped at tick time.</summary>
    /// <exception cref="ArgumentException">The name is blank, or the schedule sets both triggers or
    /// neither.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="FormatException">The cron does not parse.</exception>
    /// <exception cref="InvalidOperationException">A schedule of the same name is already registered: the
    /// name keys the persisted next-run, so only one of the two would ever fire.</exception>
    public LyntaiBuilder AddJobSchedule(JobSchedule schedule)
    {
        JobScheduleRules.Validate(schedule);
        if (Services.Any(d => d.ServiceType == typeof(JobSchedule) && !d.IsKeyedService
                && d.ImplementationInstance is JobSchedule other
                && string.Equals(other.Name, schedule.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"A job schedule named '{schedule.Name}' is already registered. The name keys the persisted " +
                "next-run, so only one of the two would ever fire; give each schedule its own name.");
        Services.AddSingleton(schedule);
        return this;
    }

    /// <summary>Keep schedules added at run time in the key-value store: registers
    /// <see cref="KeyValueJobScheduleStore"/>, resolvable as itself and as the <see cref="IJobScheduleStore"/> the
    /// <see cref="IJobScheduler"/> lists on every tick. Needs an <see cref="IKeyValueStore"/> — any storage backend
    /// registers one. The first schedule store registered wins.</summary>
    public LyntaiBuilder AddJobScheduleStore()
    {
        Services.TryAddSingleton(sp => new KeyValueJobScheduleStore(
            sp.GetRequiredService<IKeyValueStore>(), sp.GetService<ILogger<KeyValueJobScheduleStore>>()));
        Services.TryAddSingleton<IJobScheduleStore>(sp => sp.GetRequiredService<KeyValueJobScheduleStore>());
        return this;
    }

    /// <summary>Keep schedules added at run time in a store of the app's own — its database, its UI's records. The
    /// first schedule store registered wins.</summary>
    public LyntaiBuilder AddJobScheduleStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>()
        where TStore : class, IJobScheduleStore
    {
        Services.TryAddSingleton<IJobScheduleStore, TStore>();
        return this;
    }

    /// <summary>Opt-in background GC for memory: register a recurring <b>memory-prune</b> job on a
    /// <paramref name="cron"/> schedule that removes expired (and, when <paramref name="olderThan"/> is set,
    /// aged-out) entries — reclaiming storage from cold/expired <c>(taskKey, scope)</c>s that on-write
    /// eviction never revisits. It prunes the keyword <c>IMemoryStore</c> when one is wired and, for a job
    /// naming a <paramref name="taskKey"/>, every registered <c>IPrunableMemory</c> engine too; an engine
    /// prunes within one task, so an all-tasks job (<paramref name="taskKey"/> null) reaches the keyword
    /// store only. Lyntai owns the prune WORK; the APP owns the pump (drive
    /// <c>IJobScheduler.RunAsync</c>/<c>TickAsync</c> + <c>IJobRunner</c>). The schedule is validated now, as
    /// <see cref="AddJobSchedule(JobSchedule)"/> says. Call more than once with distinct
    /// <paramref name="name"/>s for several schedules — a repeated name throws, and the handler is registered
    /// once.</summary>
    public LyntaiBuilder AddMemoryPruneJob(string cron, TimeSpan? olderThan = null, string? taskKey = null,
        string lane = "default", string name = "lyntai-memory-prune", int priority = 0)
    {
        // one handler regardless of how many schedules (TryAddEnumerable dedups by implementation type)
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, MemoryPruneJobHandler>());
        var payload = new MemoryPruneRequest(taskKey, olderThan?.TotalSeconds).ToJson();
        return AddCronSchedule(name, lane, MemoryPruneJobHandler.JobType, payload, cron, priority);
    }

    /// <summary>Register a scope-guard / jail hook into the guard-rail collection (applied at the chat
    /// orchestration's gates, or by a <c>GuardedTextClient</c>).</summary>
    public LyntaiBuilder AddGuard<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IGuard
    {
        Services.AddSingleton<IGuard, T>();
        return this;
    }

    /// <summary>Register a guard built from the service provider (or an inline one, e.g. a
    /// <c>DenylistGuard</c>).</summary>
    public LyntaiBuilder AddGuard(Func<IServiceProvider, IGuard> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Enable read-through response caching on the front door: identical cacheable requests (same
    /// messages / model / params) return a stored Ok completion instead of hitting a provider — cutting
    /// cost + latency and making repeated runs deterministic. Uses the in-process
    /// <see cref="InMemoryResponseCache"/> by default; register your own
    /// <see cref="IResponseCache"/> before this to back it with a persistent/shared
    /// store. Streaming, native tool requests, and non-Ok replies are never cached. Folds at
    /// <see cref="CacheDecoratorOrder"/>.</summary>
    public LyntaiBuilder AddResponseCache(Action<CacheOptions>? configure = null)
    {
        configure?.Invoke(Options.Cache);
        Services.TryAddSingleton<IResponseCache>(_ => new InMemoryResponseCache(Options));
        // Decorate the front door (folded over the base client by AddLyntai, so it composes with any other
        // decorator) — every ITextClient resolution (tool loop, orchestrator, scorers) reads through it.
        return AddFrontDoorDecorator(CacheDecoratorOrder, nameof(AddResponseCache), (sp, inner) => new CachingTextClient(
            inner, sp.GetRequiredService<IResponseCache>(), Options,
            sp.GetService<ILogger<CachingTextClient>>(),
            sp.GetService<IModelRoutingStore>()));
    }

    /// <summary>Enable LIVE per-consumer routing: the router (and the response cache) read the consumer's route —
    /// <c>lyntai.route.&lt;consumer&gt;</c> = <c>provider:model[, provider:model…]</c> — from the key-value store
    /// on each call and, when one is set, route over it IN PLACE of the candidates the call was given, explicit
    /// ones included. So an admin moves a consumer to another backend and model together, WITHOUT a restart (see
    /// <see cref="KeyValueModelRoutingStore"/>; the namespace is <see cref="LyntaiOptions.RouteKeyPrefix"/>).
    /// Text calls only. Needs a registered <see cref="IKeyValueStore"/>; opt-in, so apps that don't want the
    /// per-call lookup pay nothing.</summary>
    public LyntaiBuilder AddLiveModelRouting()
    {
        Services.TryAddSingleton<IModelRoutingStore>(sp =>
            new KeyValueModelRoutingStore(
                sp.GetService<IKeyValueStore>(),
                sp.GetService<ILogger<KeyValueModelRoutingStore>>(),
                Options.RouteKeyPrefix)
            {
                ModelOnlyKeyPrefixes = [.. Options.ModelOnlyKeyPrefixes],
            });
        return this;
    }

    /// <summary>Register a custom cross-cutting front-door decorator (PII redaction, request logging, a
    /// bespoke cache, …) folded over the base router-backed <see cref="ITextClient"/> along the SAME ordered
    /// chain as the built-in governance decorators — so it composes with them instead of forcing the app to
    /// pre-register a whole <see cref="ITextClient"/> (which trips the governance guard). <paramref name="order"/>
    /// positions it: higher = outer; the built-ins are <see cref="RateLimitDecoratorOrder"/> (5) /
    /// <see cref="BudgetDecoratorOrder"/> (10) / <see cref="CacheDecoratorOrder"/> (20). One decorator per
    /// slot, so pick a distinct order (e.g. 15 to sit between budget and cache, 25 to sit outside the cache):
    /// adding the same delegate again is a no-op, and a DIFFERENT decorator on a taken order — a built-in's
    /// included — throws <see cref="InvalidOperationException"/> naming the slot and both registrations.
    /// <para>This is also the supported way to add a layer at all: the governance guard in <c>AddLyntai</c>
    /// observes only an <see cref="ITextClient"/> registered BEFORE the call, so registering one on
    /// <see cref="Services"/> inside the configure callback — or on the collection after <c>AddLyntai</c>
    /// returns — discards every front-door decorator with no error at all.</para></summary>
    public LyntaiBuilder AddFrontDoorDecorator(int order, Func<IServiceProvider, ITextClient, ITextClient> decorate)
    {
        ArgumentNullException.ThrowIfNull(decorate);
        return AddFrontDoorDecorator(order, decorate, decorate);
    }

    // A repeat by the same owner re-applies its options but must NOT stack a second layer (two rate limiters
    // in series would double-charge permits); a different owner would be dropped while its options still
    // read as wired, so it is refused.
    private LyntaiBuilder AddFrontDoorDecorator(int order, object owner, Func<IServiceProvider, ITextClient, ITextClient> decorate)
    {
        if (_decoratorOwners.TryGetValue(order, out var holder))
        {
            if (holder.Equals(owner)) return this;
            throw new InvalidOperationException(
                $"Front-door decorator order {order} is already held by {Describe(holder)}, so {Describe(owner)} " +
                "cannot take it too: a slot holds one decorator. Give the custom decorator a distinct order " +
                $"(the built-ins are {RateLimitDecoratorOrder}, {BudgetDecoratorOrder} and {CacheDecoratorOrder}).");
        }
        _decoratorOwners[order] = owner;
        FrontDoorDecorators.Add((order, decorate));
        return this;

        static string Describe(object who) => who as string ?? $"a custom {nameof(AddFrontDoorDecorator)}";
    }

    /// <summary>Meter token/cost usage across the front door and REFUSE further calls once a configured cap
    /// is reached — cost governance. Global caps via <see cref="BudgetOptions"/>
    /// (<c>MaxCostUsd</c>/<c>MaxTokens</c>) with optional per-consumer overrides; also
    /// <c>LYNTAI_BUDGET_MAX_COST_USD</c> / <c>LYNTAI_BUDGET_MAX_TOKENS</c>. The applicable total is checked
    /// BEFORE each call (a call whose cost isn't yet known can push a total slightly past the cap — a soft
    /// ceiling). Query or reset spend at runtime via the registered
    /// <see cref="IUsageTracker"/>; register your own before this to override the
    /// in-memory default. Folds at <see cref="BudgetDecoratorOrder"/>.</summary>
    public LyntaiBuilder AddUsageBudget(Action<BudgetOptions>? configure = null)
    {
        configure?.Invoke(Options.Budget);
        Services.TryAddSingleton<IUsageTracker, InMemoryUsageTracker>();
        return AddFrontDoorDecorator(BudgetDecoratorOrder, nameof(AddUsageBudget), (sp, inner) => new BudgetedTextClient(
            inner, sp.GetRequiredService<IUsageTracker>(), Options,
            sp.GetService<ILogger<BudgetedTextClient>>()));
    }

    /// <summary>Throttle front-door calls with a token-bucket rate limiter — over the rate a call waits up
    /// to <see cref="RateLimitOptions.MaxWait"/>, then is refused (a <c>RateLimited</c> reply). Global rate
    /// via <see cref="RateLimitOptions"/> (<c>PermitsPerSecond</c>/<c>Burst</c>) with optional per-consumer
    /// rates; also <c>LYNTAI_RATELIMIT_*</c>. Sits inside the response cache, so cached hits don't spend a
    /// permit. Register your own <see cref="IRateLimiter"/> before this for a
    /// distributed/shared limiter. Folds at <see cref="RateLimitDecoratorOrder"/>.</summary>
    public LyntaiBuilder AddRateLimit(Action<RateLimitOptions>? configure = null)
    {
        configure?.Invoke(Options.RateLimit);
        Services.TryAddSingleton<IRateLimiter>(_ => new TokenBucketRateLimiter(Options));
        return AddFrontDoorDecorator(RateLimitDecoratorOrder, nameof(AddRateLimit), (sp, inner) =>
        {
            var limiter = sp.GetRequiredService<IRateLimiter>();
            // the built-in token bucket with no positive global rate and no per-consumer entry throttles
            // NOTHING — warn rather than silently no-op (mirrors the pre-registered-client guard's intent).
            // Evaluated after env overrides; a BYO IRateLimiter owns its own effectiveness, so we only check
            // ours. A per-consumer entry with a ZERO rate is deliberate burst-then-block, i.e. an effective
            // limit — hence "entry", not "rate".
            if (limiter is TokenBucketRateLimiter { HasEffectiveLimit: false })
                sp.GetService<ILogger<RateLimitedTextClient>>()?.LogWarning(
                    "AddRateLimit resolved to no effective limit (RateLimit.PermitsPerSecond=0 and no per-consumer " +
                    "entry) — it will not throttle. Set RateLimit.PermitsPerSecond (or a PerConsumer entry, or the " +
                    "LYNTAI_RATELIMIT_PERMITS_PER_SECOND env var) to enable throttling.");
            return new RateLimitedTextClient(
                inner, limiter, sp.GetService<ILogger<RateLimitedTextClient>>());
        });
    }

    /// <summary>Set by any <c>AddSemanticMemory</c> overload: the app has STATED it wants semantic recall,
    /// so <c>AddLyntai</c> must fail rather than compose a container where <see cref="ISemanticMemory"/> is
    /// silently absent. Intent only — the registrations themselves are unchanged.</summary>
    internal bool SemanticMemoryRequested { get; private set; }

    /// <summary>Turn on semantic (meaning-based) recall — <see cref="ISemanticMemory"/>, composed from any
    /// registered backend that produces <see cref="ProviderKinds.Vector"/> and an
    /// <see cref="IVectorStore"/>. It registers no BACKEND: bring one with
    /// <c>AddModel2VecProvider</c> / <c>AddOnnxProvider</c> / <c>AddHttpProvider</c>, or register an
    /// <see cref="IModelProvider"/> of your own (<c>docs/DECISIONS.md</c> D151).
    /// <para><b>Why state it at all.</b> Semantic memory is otherwise enabled as a SIDE EFFECT of a
    /// backend happening to be registered, which makes its absence silent: <see cref="ISemanticMemory"/>
    /// is simply never registered and every recall path (prompt composer, chat orchestration) skips it
    /// without complaint. Calling this makes the intent explicit, so <c>AddLyntai</c> THROWS at composition
    /// when nothing in the container can embed.</para>
    /// <para><b>Everything stays substitutable.</b> Vectors land in the in-process
    /// <see cref="InMemoryVectorStore"/> unless a persistent store is wired
    /// (<c>UseSqliteVectorStore()</c> / <c>UsePostgresVectorStore()</c>) or you register your own
    /// <see cref="IVectorStore"/>; that store and <see cref="ISemanticMemory"/> itself are
    /// <c>TryAdd</c>-registered, so anything registered before <c>AddLyntai</c> wins. Nothing here needs to
    /// be constructed by hand.</para></summary>
    public LyntaiBuilder AddSemanticMemory()
    {
        SemanticMemoryRequested = true;
        return this;
    }

    /// <summary>Set the router fallback order used when callers don't pass explicit candidates.
    /// SETS (clears + replaces) the default candidate list — the last call wins; it does not append.
    /// Each entry is a candidate spec, as <c>LYNTAI_DEFAULT_CANDIDATES</c> reads one: a provider id, optionally
    /// <c>"provider:model"</c>, split at the FIRST colon (so <c>"ollama:qwen3:4b"</c> is <c>ollama</c> serving
    /// <c>qwen3:4b</c>). A provider id that itself contains a colon takes the <see cref="ProviderCandidate"/>
    /// overload. Naming a registered backend that produces no text throws when the default
    /// <see cref="ITextClient"/> is resolved; a vector or score backend is selected by its kind and needs no
    /// entry here.</summary>
    public LyntaiBuilder UseDefaultCandidates(params string[] providerIds) =>
        UseDefaultCandidates([.. providerIds.Select(ProviderCandidateSpec.Parse)]);

    /// <summary>Set the router fallback order used when callers don't pass explicit candidates.
    /// SETS (clears + replaces) the default candidate list — the last call wins; it does not append. Naming a
    /// registered backend that produces no text throws when the default <see cref="ITextClient"/> is
    /// resolved.</summary>
    public LyntaiBuilder UseDefaultCandidates(params ProviderCandidate[] candidates)
    {
        Options.DefaultCandidates.Clear();
        Options.DefaultCandidates.AddRange(candidates);
        return this;
    }

    /// <summary>Let the app register, replace and unregister text providers while it runs, through
    /// <see cref="ITextProviderRegistry"/> — the endpoints its users add and edit — without rebuilding the container.
    /// The default <see cref="ITextClient"/> serves them with every governance decorator it carries; a named client
    /// (<c>AddTextClient</c>) does not see them.</summary>
    public LyntaiBuilder UseTextProviderRegistry()
    {
        Services.TryAddSingleton(sp => new TextProviderRegistry(
            sp.GetServices<IModelProvider>(), sp.GetRequiredService<IProviderPool<IModelProvider>>()));
        Services.TryAddSingleton<ITextProviderRegistry>(sp => sp.GetRequiredService<TextProviderRegistry>());
        return this;
    }

    /// <summary>Mutate the <see cref="LyntaiOptions"/> in code (timeouts, retries, prompt key prefix,
    /// job/memory knobs, …). Runs immediately, inside the <c>AddLyntai</c> configure callback; the
    /// <c>LYNTAI_*</c> environment overrides are applied AFTER that callback returns, so an env var beats
    /// anything set here.</summary>
    public LyntaiBuilder Configure(Action<LyntaiOptions> configure)
    {
        configure(Options);
        return this;
    }

    /// <summary>Tune the router's fallback policy (per-verdict action, same-candidate retries,
    /// cooldown-key granularity, sole-candidate exemption). The defaults reproduce design §6.</summary>
    public LyntaiBuilder ConfigureRouting(Action<RoutingPolicy> configure)
    {
        configure(Options.Routing);
        return this;
    }

    /// <summary>Tune how <see cref="IMemoryStore"/> bounds its size — count cap + eviction mode (FIFO/LRU),
    /// default TTL, size budget — by setting <see cref="LyntaiOptions.MemoryEviction"/> (e.g.
    /// <c>b.ConfigureMemoryEviction(p => { p.Mode = MemoryEvictionMode.Lru; p.DefaultTtl = TimeSpan.FromDays(7); })</c>).
    /// The graph memory engine is not configured here.</summary>
    public LyntaiBuilder ConfigureMemoryEviction(Action<MemoryEvictionPolicy> configure)
    {
        configure(Options.MemoryEviction);
        return this;
    }
}
