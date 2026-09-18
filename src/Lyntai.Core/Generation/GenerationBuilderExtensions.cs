using Lyntai.Inference;
using Lyntai.Inference.Budgeting;
using Lyntai.Inference.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>Platform configuration for the media domain.</summary>
public sealed class MediaOptions
{
    /// <summary>Candidate order used when a caller doesn't name one — the media counterpart of
    /// <c>LyntaiOptions.DefaultCandidates</c>, and mutable for the same reason: the builder sets it at
    /// configure time.</summary>
    public List<ProviderCandidate> DefaultCandidates { get; } = [];

    /// <summary>Throttling for generation, SEPARATE from <c>LyntaiOptions.RateLimit</c> (which governs chat).
    /// A render and a chat turn hit different vendors' limits — often different accounts — so one shared
    /// bucket would have an image render starve the chat that asked for it. Applied by
    /// <c>AddMediaRateLimit()</c>; the machinery is the same token bucket.</summary>
    public RateLimitOptions RateLimit { get; } = new();

    /// <summary>How long <c>generate_backends</c> may take IN TOTAL to probe every registered backend.
    /// Zero or negative means no deadline. Default: 20 seconds.
    /// <para>It is an AGGREGATE because that is the number a caller can act on. Each backend already bounds
    /// its own call, but with its RENDER budget — ten minutes on two of the shipped ones, correctly, since a
    /// render outlives <see cref="HttpClient"/>'s own default — so two backends that accept a connection and
    /// stall made the tool an agent is told to call FIRST block for twenty. Every backend disclosed its
    /// timeout and the composition disclosed nothing.</para>
    /// <para>Short by design: a probe is contractually free and must never generate, so a backend that cannot
    /// answer in seconds is not usable for the render that would follow. A backend that overruns is reported
    /// unusable WITH the reason, never dropped from the listing.</para>
    /// <para><b>To change it, register the options instance before <c>AddLyntai</c></b> —
    /// <c>services.AddSingleton(new MediaOptions { ProbeDeadline = … })</c> — the same DI-registration
    /// path <c>SalienceOptions</c> and <c>DsrOptions</c> take. The builder reuses a pre-registered instance
    /// rather than replacing it, so the candidate order and rate limit configured through
    /// <c>UseDefaultMediaCandidates</c>/<c>AddMediaRateLimit</c> still land on the same
    /// object.</para></summary>
    public TimeSpan ProbeDeadline { get; set; } = TimeSpan.FromSeconds(20);
}

public static class GenerationBuilderExtensions
{
    /// <summary>Register a media backend into the <see cref="IModelProvider"/> collection. Adding a backend is
    /// one registration — never an edit to a branch inside an existing provider (which is exactly the shape a
    /// sibling app grew: one class with an <c>if (provider == "automatic1111")</c> inside).</summary>
    public static LyntaiBuilder AddGenerationProvider(
        this LyntaiBuilder builder, Func<IServiceProvider, IModelProvider> factory)
    {
        builder.Services.AddSingleton(factory);
        EnsureRouter(builder);
        return builder;
    }

    /// <summary>Meter what generation COSTS and refuse renders once a configured cap is reached. Reads the same
    /// <see cref="BudgetOptions"/> as the LLM front door and records into the same
    /// <see cref="IUsageTracker"/>, so "what has this app spent" stays ONE number across chat and media — a
    /// host paying one vendor for both would otherwise have to add up two ledgers.
    ///
    /// <para>Only COST caps bind a render (it spends no tokens and claims none), so set
    /// <see cref="BudgetOptions.MaxCostUsd"/> or a per-consumer cost cap. The cap is checked before a render
    /// and before a SUBMISSION — submitting is what commits the money for a hosted video, whether or not
    /// anyone fetches the result. Register your own <see cref="IUsageTracker"/> before this to share spend
    /// across processes.</para></summary>
    public static LyntaiBuilder AddMediaUsageBudget(this LyntaiBuilder builder, Action<BudgetOptions>? configure = null)
    {
        configure?.Invoke(builder.Options.Budget);
        builder.Services.TryAddSingleton<IUsageTracker, InMemoryUsageTracker>();
        builder.Services.TryAddSingleton<MediaBudgetGovernance>();
        EnsureRouter(builder);
        return builder;
    }

    /// <summary>Throttle generation with a token-bucket limiter on its OWN rate
    /// (<see cref="MediaOptions.RateLimit"/> — not the chat one; see that property for why). Over the
    /// rate a call waits up to <see cref="RateLimitOptions.MaxWait"/> and is then refused with
    /// <c>RateLimited</c> without hitting a backend.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Tune the generation rate.</param>
    /// <param name="limiter">BYO limiter (a distributed one shared across processes). Null = the built-in
    /// token bucket over <see cref="MediaOptions.RateLimit"/>. Deliberately a PARAMETER rather than an
    /// <see cref="IRateLimiter"/> registration: the LLM front door already owns that service, and two
    /// registrations of it would silently make one domain throttle at the other's rate.</param>
    public static LyntaiBuilder AddMediaRateLimit(
        this LyntaiBuilder builder,
        Action<RateLimitOptions>? configure = null,
        Func<IServiceProvider, IRateLimiter>? limiter = null)
    {
        var options = MediaOptionsFor(builder);
        configure?.Invoke(options.RateLimit);
        builder.Services.TryAddSingleton(sp => new MediaRateLimitGovernance(
            limiter?.Invoke(sp) ?? new TokenBucketRateLimiter(options.RateLimit)));
        EnsureRouter(builder);
        return builder;
    }

    /// <summary>Register the <see cref="IMediaRouterFactory"/> that composes governance, and the
    /// <see cref="IMediaRouter"/> it builds over the REGISTERED backends. One registration for every
    /// entry point (a provider, a budget, a rate limit) because <c>TryAddSingleton</c> keeps the FIRST
    /// factory — so the factory has to be the composing one no matter which <c>Add*</c> ran first, and it
    /// reads the governance markers at RESOLVE time.
    ///
    /// <para>The container's router goes through the same factory as a caller-built one, so there is ONE
    /// composition path rather than two that have to be kept in step. It takes the INSTANCE overload: the
    /// provider set is the DI collection and the cooldown key stays the provider id, which is what keeps an
    /// app that never touches the pool behaving exactly as it did before pooling existed.</para>
    ///
    /// <para>Order mirrors the LLM front door: the rate limiter sits INSIDE the budget, so a call refused for
    /// spend never consumes a permit.</para></summary>
    private static void EnsureRouter(LyntaiBuilder builder)
    {
        MediaOptionsFor(builder);   // ensure the options singleton exists even with nothing configured

        // The pool the factory needs to be constructible. Registered here rather than left to the caller so
        // the pooled overload works out of the box; a host swaps the strategy by registering its own.
        // DELIBERATELY not registering DeadHostTracker: AddLyntai's LLM front door already registers one
        // built from LyntaiOptions, and this method runs BEFORE it — a TryAddSingleton here would win and
        // silently discard the configured threshold, cooldown and logger for BOTH domains.
        builder.Services.TryAddSingleton(typeof(IProviderPool<>), typeof(BoundedProviderPool<>));

        builder.Services.TryAddSingleton<IMediaRouterFactory>(sp =>
        {
            var budgeted = sp.GetService<MediaBudgetGovernance>() is not null;
            return new MediaRouterFactory(
                sp.GetRequiredService<IProviderPool<IModelProvider>>(),
                sp.GetRequiredService<DeadHostTracker>(),
                sp.GetService<MediaRoutingPolicy>(),
                sp.GetService<MediaRateLimitGovernance>()?.Limiter,
                budgeted ? sp.GetRequiredService<IUsageTracker>() : null,
                budgeted ? sp.GetRequiredService<LyntaiOptions>() : null,
                sp.GetService<ILoggerFactory>(),
                sp.GetService<IProviderAdmission>());
        });

        builder.Services.TryAddSingleton<IMediaRouter>(sp =>
            sp.GetRequiredService<IMediaRouterFactory>().For([.. sp.GetServices<IModelProvider>()]));
    }

    /// <summary>Tune per-verdict fallback for generation routing. The defaults follow the SHAPE of the LLM
    /// router's §6 semantics and deliberately differ on <c>Unsupported</c> (which advances here rather than
    /// surfacing — see <see cref="MediaRoutingPolicy"/>); the override that matters in practice is
    /// <c>p.On(ProviderVerdict.Refused, FallbackAction.Advance)</c>, for a host that deliberately
    /// pairs a hosted backend (which refuses some content) with a locally-run one (which doesn't) — that is
    /// the host's policy call, not the library's.</summary>
    public static LyntaiBuilder ConfigureMediaRouting(
        this LyntaiBuilder builder, Action<MediaRoutingPolicy> configure)
    {
        configure(RoutingPolicyFor(builder));
        return builder;
    }

    /// <summary>Set the media candidate order used when a caller doesn't pass one. SETS (clears + replaces) —
    /// the last call wins, it does not append — matching <c>LyntaiBuilder.UseDefaultCandidates</c> exactly, so
    /// the two domains behave identically. Each entry is a provider id, optionally <c>"provider:model"</c>.</summary>
    public static LyntaiBuilder UseDefaultMediaCandidates(this LyntaiBuilder builder, params string[] providerIds)
    {
        var options = MediaOptionsFor(builder);
        options.DefaultCandidates.Clear();
        options.DefaultCandidates.AddRange(providerIds.Select(ProviderCandidateSpec.Parse));
        return builder;
    }

    /// <summary>Expose the generation domain to AGENTS as <see cref="Lyntai.Agents.ITool"/>s: <c>generate_backends</c>
    /// (discover what is available), <c>generate</c> (inline), and <c>generate_submit</c> /
    /// <c>generate_status</c> / <c>generate_fetch</c> (the asynchronous path a video render needs).
    ///
    /// This is the whole coupling between the two domains: the LLM side already knows <see cref="Lyntai.Agents.ITool"/>, so these
    /// work in the in-process tool loop and — with <c>AddMcpToolHost(...)</c> from
    /// <c>Lyntai.Tools.Mcp</c> — for a CLI agent that runs its own loop over MCP. Neither domain
    /// references the other's concrete types (<c>docs/DECISIONS.md</c> D24).</summary>
    /// <remarks>Bytes are never returned in a tool observation (a base64 image would blow the context window for
    /// no benefit): if an <see cref="Lyntai.Generation.Jobs.IGenerationArtifactSink"/> is registered the artifacts are delivered to
    /// it and the observation says where they went, otherwise it reports their type/size/URI.</remarks>
    public static LyntaiBuilder AddGenerationTools(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationBackendsTool(
            sp.GetServices<IModelProvider>(),
            MediaOptionsFor(sp)));   // the listing's aggregate ProbeDeadline lives here
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationInlineTool(
            sp.GetRequiredService<Lyntai.Inference.IMediaRouter>(),
            MediaOptionsFor(sp),
            sp.GetService<Lyntai.Generation.Jobs.IGenerationArtifactSink>()));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationSubmitTool(
            sp.GetRequiredService<Lyntai.Inference.IMediaRouter>(),
            MediaOptionsFor(sp)));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationStatusTool(
            sp.GetServices<IModelProvider>()));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationFetchTool(
            sp.GetServices<IModelProvider>(),
            sp.GetService<Lyntai.Generation.Jobs.IGenerationArtifactSink>(),
            sp.GetService<Lyntai.Inference.Budgeting.IUsageTracker>()));
        return builder;
    }

    /// <summary>Resolved options, or defaults — the tools work before any candidate order is configured (a model
    /// can always name backends explicitly).</summary>
    private static MediaOptions MediaOptionsFor(IServiceProvider sp) =>
        sp.GetService<MediaOptions>() ?? new MediaOptions();

    /// <summary>The single <see cref="MediaOptions"/> instance for this builder, registered as a singleton
    /// INSTANCE. Registering the instance (not a factory) is what lets configure-time mutation be visible to
    /// the resolved service — the same immediate-mutation model the builder's own <c>Options</c> uses.</summary>
    private static MediaOptions MediaOptionsFor(LyntaiBuilder builder) =>
        InstanceFor(builder, () => new MediaOptions());

    /// <summary>The single <see cref="MediaRoutingPolicy"/> for this builder, so
    /// <see cref="ConfigureMediaRouting"/> and <see cref="AddGenerationProvider"/> agree on one
    /// instance regardless of call order.</summary>
    private static MediaRoutingPolicy RoutingPolicyFor(LyntaiBuilder builder) =>
        InstanceFor(builder, () => new MediaRoutingPolicy());

    /// <summary>Marker: spend governance is configured. INTERNAL — it is wiring state, not a knob, and the
    /// public surface should not grow a type whose only job is to exist.</summary>
    internal sealed class MediaBudgetGovernance;

    /// <summary>Marker carrying the generation limiter — carried rather than registered as
    /// <see cref="IRateLimiter"/> so it can never be mistaken for (or overwrite) the chat limiter.</summary>
    internal sealed class MediaRateLimitGovernance(IRateLimiter limiter)
    {
        public IRateLimiter Limiter { get; } = limiter;
    }

    /// <summary>Get-or-register a singleton INSTANCE for this builder — an instance rather than a factory, for
    /// the reason <see cref="MediaOptionsFor(LyntaiBuilder)"/> gives.</summary>
    private static T InstanceFor<T>(LyntaiBuilder builder, Func<T> create) where T : class
    {
        foreach (var descriptor in builder.Services)
            if (descriptor.ServiceType == typeof(T) && descriptor.ImplementationInstance is T existing)
                return existing;

        var created = create();
        builder.Services.AddSingleton(created);
        return created;
    }
}
