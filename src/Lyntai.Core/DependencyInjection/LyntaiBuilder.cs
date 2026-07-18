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
    internal const int RateLimitDecoratorOrder = 5;
    internal const int BudgetDecoratorOrder = 10;
    internal const int CacheDecoratorOrder = 20;

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
        FrontDoorDecorators.Add((CacheDecoratorOrder, (sp, inner) => new Lyntai.Llm.Caching.CachingLlmClient(
            inner, sp.GetRequiredService<Lyntai.Llm.Caching.IResponseCache>(), Options,
            sp.GetService<ILogger<Lyntai.Llm.Caching.CachingLlmClient>>())));
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
        FrontDoorDecorators.Add((BudgetDecoratorOrder, (sp, inner) => new Lyntai.Llm.Budgeting.BudgetedLlmClient(
            inner, sp.GetRequiredService<Lyntai.Llm.Budgeting.IUsageTracker>(), Options,
            sp.GetService<ILogger<Lyntai.Llm.Budgeting.BudgetedLlmClient>>())));
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
        FrontDoorDecorators.Add((RateLimitDecoratorOrder, (sp, inner) => new Lyntai.Llm.RateLimiting.RateLimitedLlmClient(
            inner, sp.GetRequiredService<Lyntai.Llm.RateLimiting.IRateLimiter>(),
            sp.GetService<ILogger<Lyntai.Llm.RateLimiting.RateLimitedLlmClient>>())));
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
}
