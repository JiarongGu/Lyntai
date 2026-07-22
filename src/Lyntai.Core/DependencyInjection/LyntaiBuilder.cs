using System.Diagnostics.CodeAnalysis;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
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
    internal List<(int Order, Func<IServiceProvider, ILlmClient, ILlmClient> Decorate)> FrontDoorDecorators { get; } = [];

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

    /// <summary>Register an <see cref="ILlmProvider"/> into the router's provider collection.</summary>
    public LyntaiBuilder AddProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, ILlmProvider
    {
        Services.AddSingleton<ILlmProvider, T>();
        return this;
    }

    /// <summary>Register a provider built from the service provider (for id/config-parameterized ones).</summary>
    public LyntaiBuilder AddProvider(Func<IServiceProvider, ILlmProvider> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register an eval dimension into the scoring collection.</summary>
    public LyntaiBuilder AddScorer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IScorer
    {
        Services.AddSingleton<IScorer, T>();
        return this;
    }

    /// <summary>Register an <see cref="Lyntai.Storage.IConversationEnricher"/> into the enricher collection —
    /// the app's "add additional info" seam. Lyntai owns the conversation store; each registered enricher is
    /// invoked after a thread/message write to persist the app's own info (in its own store), without
    /// replacing the store. Add a class + one registration, never a fork.</summary>
    public LyntaiBuilder AddConversationEnricher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Lyntai.Storage.IConversationEnricher
    {
        Services.AddSingleton<Lyntai.Storage.IConversationEnricher, T>();
        return this;
    }

    /// <summary>Register a conversation enricher built from the service provider.</summary>
    public LyntaiBuilder AddConversationEnricher(Func<IServiceProvider, Lyntai.Storage.IConversationEnricher> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register a typed <see cref="Lyntai.Llm.IRefusalMatcher"/> into the refusal-screening front
    /// door — the structured alternative to a per-request <c>RefusalPattern</c> regex. Every registered
    /// matcher runs on an Ok reply's text (after the central patterns + the request pattern); any that
    /// returns true surfaces the reply as <c>Refused</c> (no fallback). Add a class + one registration.</summary>
    public LyntaiBuilder AddRefusalMatcher<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Lyntai.Llm.IRefusalMatcher
    {
        Services.AddSingleton<Lyntai.Llm.IRefusalMatcher, T>();
        return this;
    }

    /// <summary>Register a refusal matcher instance.</summary>
    public LyntaiBuilder AddRefusalMatcher(Lyntai.Llm.IRefusalMatcher matcher)
    {
        Services.AddSingleton(matcher);
        return this;
    }

    /// <summary>Register a refusal matcher built from the service provider.</summary>
    public LyntaiBuilder AddRefusalMatcher(Func<IServiceProvider, Lyntai.Llm.IRefusalMatcher> factory)
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

    /// <summary>Register an <see cref="Lyntai.Jobs.IJobHandler"/> into the durable-job handler collection
    /// (keyed by its <c>Type</c>).</summary>
    public LyntaiBuilder AddJobHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Lyntai.Jobs.IJobHandler
    {
        Services.AddSingleton<Lyntai.Jobs.IJobHandler, T>();
        return this;
    }

    /// <summary>Register a job handler built from the service provider.</summary>
    public LyntaiBuilder AddJobHandler(Func<IServiceProvider, Lyntai.Jobs.IJobHandler> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Replace the default admit-all <see cref="Lyntai.Jobs.IJobAdmissionController"/> with one the
    /// runner consults per lane before claiming — so the app can throttle lanes by external load / a
    /// maintenance window. Registered as the singleton controller (last registration wins).</summary>
    public LyntaiBuilder AddJobAdmissionController<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Lyntai.Jobs.IJobAdmissionController
    {
        Services.AddSingleton<Lyntai.Jobs.IJobAdmissionController, T>();
        return this;
    }

    /// <summary>Register a job admission controller instance (or one built from the provider).</summary>
    public LyntaiBuilder AddJobAdmissionController(Func<IServiceProvider, Lyntai.Jobs.IJobAdmissionController> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register a recurring job: every <paramref name="every"/>, the <see cref="Lyntai.Jobs.IJobScheduler"/>
    /// enqueues a <paramref name="type"/> job on <paramref name="lane"/> with <paramref name="payload"/>.
    /// <paramref name="name"/> must be stable + unique (it keys the persisted next-run). The app drives the
    /// scheduler's pump (TickAsync/RunAsync).</summary>
    public LyntaiBuilder AddJobSchedule(string name, string lane, string type, string payload, TimeSpan every, int priority = 0) =>
        AddJobSchedule(new Lyntai.Jobs.JobSchedule(name, lane, type, payload, every, priority));

    /// <summary>Register a recurring job on a <b>cron</b> schedule (5-field <c>min hour dom month dow</c>, or
    /// a macro like <c>@daily</c>; evaluated in UTC). The expression is validated now — a bad one throws
    /// here rather than being silently skipped at tick time. The app drives the scheduler pump.</summary>
    public LyntaiBuilder AddCronSchedule(string name, string lane, string type, string payload, string cron, int priority = 0)
    {
        _ = Lyntai.Jobs.CronExpression.Parse(cron); // fail fast on a malformed expression
        return AddJobSchedule(new Lyntai.Jobs.JobSchedule(name, lane, type, payload, Cron: cron, Priority: priority));
    }

    /// <summary>Register a recurring <see cref="Lyntai.Jobs.JobSchedule"/>.</summary>
    public LyntaiBuilder AddJobSchedule(Lyntai.Jobs.JobSchedule schedule)
    {
        Services.AddSingleton(schedule);
        return this;
    }

    /// <summary>Register a scope-guard / jail hook into the guard-rail collection (applied at the chat
    /// orchestration's gates, or by a <c>GuardedLlmClient</c>).</summary>
    public LyntaiBuilder AddGuard<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Lyntai.Guards.IGuard
    {
        Services.AddSingleton<Lyntai.Guards.IGuard, T>();
        return this;
    }

    /// <summary>Register a guard built from the service provider (or an inline one, e.g. a
    /// <c>DenylistGuard</c>).</summary>
    public LyntaiBuilder AddGuard(Func<IServiceProvider, Lyntai.Guards.IGuard> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Enable read-through response caching on the front door: identical cacheable requests (same
    /// messages / model / params) return a stored Ok completion instead of hitting a provider — cutting
    /// cost + latency and making repeated runs deterministic. Uses the in-process
    /// <see cref="Lyntai.Llm.Caching.InMemoryResponseCache"/> by default; register your own
    /// <see cref="Lyntai.Llm.Caching.IResponseCache"/> before this to back it with a persistent/shared
    /// store. Streaming, native tool requests, and non-Ok replies are never cached.</summary>
    public LyntaiBuilder AddResponseCache(Action<CacheOptions>? configure = null)
    {
        configure?.Invoke(Options.Cache);
        Services.TryAddSingleton<Lyntai.Llm.Caching.IResponseCache>(_ => new Lyntai.Llm.Caching.InMemoryResponseCache(Options));
        // Decorate the front door (folded over the base client by AddLyntai, so it composes with any other
        // decorator) — every ILlmClient resolution (tool loop, orchestrator, scorers) reads through it.
        AddFrontDoorDecorator(CacheDecoratorOrder, (sp, inner) => new Lyntai.Llm.Caching.CachingLlmClient(
            inner, sp.GetRequiredService<Lyntai.Llm.Caching.IResponseCache>(), Options,
            sp.GetService<ILogger<Lyntai.Llm.Caching.CachingLlmClient>>(),
            sp.GetService<Lyntai.Llm.Routing.IModelRoutingStore>()));
        return this;
    }

    /// <summary>Enable LIVE per-consumer model routing: the router (and response cache) read a
    /// <c>lyntai.model.&lt;consumer&gt;</c> override from the key-value store on each call, so an admin retune
    /// of a consumer's model takes effect WITHOUT a restart (the model analogue of a prompt override). Needs a
    /// registered <see cref="Lyntai.Storage.IKeyValueStore"/>; opt-in, so apps that don't want the per-call
    /// lookup pay nothing. Precedence: explicit request/candidate model → live override → configured
    /// per-consumer default → provider default.</summary>
    public LyntaiBuilder AddLiveModelRouting()
    {
        Services.TryAddSingleton<Lyntai.Llm.Routing.IModelRoutingStore>(sp =>
            new Lyntai.Llm.Routing.KeyValueModelRoutingStore(
                sp.GetService<Lyntai.Storage.IKeyValueStore>(),
                sp.GetService<ILogger<Lyntai.Llm.Routing.KeyValueModelRoutingStore>>(),
                Options.ModelKeyPrefix));
        return this;
    }

    /// <summary>Register a custom cross-cutting front-door decorator (PII redaction, request logging, a
    /// bespoke cache, …) folded over the base router-backed <see cref="ILlmClient"/> along the SAME ordered
    /// chain as the built-in governance decorators — so it composes with them instead of forcing the app to
    /// pre-register a whole <see cref="ILlmClient"/> (which trips the governance guard). <paramref name="order"/>
    /// positions it: higher = outer; the built-ins are <see cref="RateLimitDecoratorOrder"/> (5) /
    /// <see cref="BudgetDecoratorOrder"/> (10) / <see cref="CacheDecoratorOrder"/> (20). Idempotent per
    /// order — one decorator per slot (a repeated Add of the same order is ignored), so pick a distinct
    /// order (e.g. 15 to sit between budget and cache, 25 to sit outside the cache).</summary>
    public LyntaiBuilder AddFrontDoorDecorator(int order, Func<IServiceProvider, ILlmClient, ILlmClient> decorate)
    {
        // idempotent per order: a repeated Add* still re-applies its options but must NOT stack a second
        // decorator in the same slot — two rate limiters in series would double-charge permits.
        if (FrontDoorDecorators.All(d => d.Order != order))
            FrontDoorDecorators.Add((order, decorate));
        return this;
    }

    /// <summary>Meter token/cost usage across the front door and REFUSE further calls once a configured cap
    /// is reached — cost governance. Global caps via <see cref="BudgetOptions"/>
    /// (<c>MaxCostUsd</c>/<c>MaxTokens</c>) with optional per-consumer overrides; also
    /// <c>LYNTAI_BUDGET_MAX_COST_USD</c> / <c>LYNTAI_BUDGET_MAX_TOKENS</c>. The applicable total is checked
    /// BEFORE each call (a call whose cost isn't yet known can push a total slightly past the cap — a soft
    /// ceiling). Query or reset spend at runtime via the registered
    /// <see cref="Lyntai.Llm.Budgeting.IUsageTracker"/>; register your own before this to override the
    /// in-memory default.</summary>
    public LyntaiBuilder AddUsageBudget(Action<BudgetOptions>? configure = null)
    {
        configure?.Invoke(Options.Budget);
        Services.TryAddSingleton<Lyntai.Llm.Budgeting.IUsageTracker, Lyntai.Llm.Budgeting.InMemoryUsageTracker>();
        AddFrontDoorDecorator(BudgetDecoratorOrder, (sp, inner) => new Lyntai.Llm.Budgeting.BudgetedLlmClient(
            inner, sp.GetRequiredService<Lyntai.Llm.Budgeting.IUsageTracker>(), Options,
            sp.GetService<ILogger<Lyntai.Llm.Budgeting.BudgetedLlmClient>>()));
        return this;
    }

    /// <summary>Throttle front-door calls with a token-bucket rate limiter — over the rate a call waits up
    /// to <see cref="RateLimitOptions.MaxWait"/>, then is refused (a <c>RateLimited</c> reply). Global rate
    /// via <see cref="RateLimitOptions"/> (<c>PermitsPerSecond</c>/<c>Burst</c>) with optional per-consumer
    /// rates; also <c>LYNTAI_RATELIMIT_*</c>. Sits inside the response cache, so cached hits don't spend a
    /// permit. Register your own <see cref="Lyntai.Llm.RateLimiting.IRateLimiter"/> before this for a
    /// distributed/shared limiter.</summary>
    public LyntaiBuilder AddRateLimit(Action<RateLimitOptions>? configure = null)
    {
        configure?.Invoke(Options.RateLimit);
        Services.TryAddSingleton<Lyntai.Llm.RateLimiting.IRateLimiter>(_ => new Lyntai.Llm.RateLimiting.TokenBucketRateLimiter(Options));
        AddFrontDoorDecorator(RateLimitDecoratorOrder, (sp, inner) =>
        {
            var limiter = sp.GetRequiredService<Lyntai.Llm.RateLimiting.IRateLimiter>();
            // the built-in token bucket with no positive global/per-consumer rate throttles NOTHING — warn
            // rather than silently no-op (mirrors the pre-registered-client guard's intent). Evaluated after
            // env overrides; a BYO IRateLimiter owns its own effectiveness, so we only check ours.
            if (limiter is Lyntai.Llm.RateLimiting.TokenBucketRateLimiter { HasEffectiveLimit: false })
                sp.GetService<ILogger<Lyntai.Llm.RateLimiting.RateLimitedLlmClient>>()?.LogWarning(
                    "AddRateLimit resolved to no effective limit (RateLimit.PermitsPerSecond=0 and no per-consumer " +
                    "rate) — it will not throttle. Set RateLimit.PermitsPerSecond (or a PerConsumer rate, or the " +
                    "LYNTAI_RATELIMIT_PERMITS_PER_SECOND env var) to enable throttling.");
            return new Lyntai.Llm.RateLimiting.RateLimitedLlmClient(
                inner, limiter, sp.GetService<ILogger<Lyntai.Llm.RateLimiting.RateLimitedLlmClient>>());
        });
        return this;
    }

    /// <summary>Register the app's embedding model, enabling semantic memory
    /// (<see cref="Lyntai.Memory.ISemanticMemory"/>). BYO — an OpenAI/Ollama embeddings endpoint, a local
    /// model, etc.; Lyntai owns the recall machinery. Pair with your own
    /// <see cref="Lyntai.Memory.IVectorStore"/> (registered before <c>AddLyntai</c>) for a persistent/scaled
    /// vector backend, or take the in-memory default.</summary>
    public LyntaiBuilder AddEmbeddings(Lyntai.Embeddings.IEmbedder embedder)
    {
        Services.AddSingleton(embedder);
        return this;
    }

    /// <summary>Register the embedder from the service provider (for config/dependency-parameterized ones).</summary>
    public LyntaiBuilder AddEmbeddings(Func<IServiceProvider, Lyntai.Embeddings.IEmbedder> factory)
    {
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Set the router fallback order used when callers don't pass explicit candidates.</summary>
    public LyntaiBuilder DefaultCandidates(params string[] providerIds) =>
        DefaultCandidates([.. providerIds.Select(id => new LlmCandidate(id))]);

    public LyntaiBuilder DefaultCandidates(params LlmCandidate[] candidates)
    {
        Options.DefaultCandidates.Clear();
        Options.DefaultCandidates.AddRange(candidates);
        return this;
    }

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

    /// <summary>Tune how <c>IMemoryStore</c> bounds its size — count cap + eviction mode (FIFO/LRU), default
    /// TTL, size budget. The defaults reproduce the historical 500-entry FIFO cap; use a
    /// <see cref="Lyntai.Storage.MemoryRetentionPolicy"/> preset (e.g.
    /// <c>b.ConfigureMemory(p => { p.Eviction = MemoryEvictionMode.Lru; p.DefaultTtl = TimeSpan.FromDays(7); })</c>).</summary>
    public LyntaiBuilder ConfigureMemory(Action<Lyntai.Storage.MemoryRetentionPolicy> configure)
    {
        configure(Options.MemoryRetention);
        return this;
    }
}
