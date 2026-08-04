using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>Platform configuration for the media domain.</summary>
public sealed class GenerationOptions
{
    /// <summary>Candidate order used when a caller doesn't name one — the media counterpart of
    /// <c>LyntaiOptions.DefaultCandidates</c>, and mutable for the same reason: the builder sets it at
    /// configure time.</summary>
    public List<GenerationCandidate> DefaultCandidates { get; } = [];
}

public static class GenerationBuilderExtensions
{
    /// <summary>Register a media backend into the <see cref="IGenerationProvider"/> collection. Adding a backend is
    /// one registration — never an edit to a branch inside an existing provider (which is exactly the shape a
    /// sibling app grew: one class with an <c>if (provider == "automatic1111")</c> inside).</summary>
    public static LyntaiBuilder AddGenerationProvider(
        this LyntaiBuilder builder, Func<IServiceProvider, IGenerationProvider> factory)
    {
        builder.Services.AddSingleton(factory);
        GenerationOptionsFor(builder);   // ensure the options singleton exists even with no candidates configured
        builder.Services.TryAddSingleton<IGenerationRouter>(sp => new GenerationRouter(
            sp.GetServices<IGenerationProvider>(), sp.GetService<GenerationRoutingPolicy>()));
        return builder;
    }

    /// <summary>Tune per-verdict fallback for generation routing. The defaults reproduce the LLM router's
    /// §6 semantics; the override that matters in practice is
    /// <c>p.On(GenerationVerdict.Refused, GenerationFallbackAction.Advance)</c>, for a host that deliberately
    /// pairs a hosted backend (which refuses some content) with a locally-run one (which doesn't) — that is
    /// the host's policy call, not the library's.</summary>
    public static LyntaiBuilder ConfigureGenerationRouting(
        this LyntaiBuilder builder, Action<GenerationRoutingPolicy> configure)
    {
        configure(RoutingPolicyFor(builder));
        return builder;
    }

    /// <summary>Set the media candidate order used when a caller doesn't pass one. SETS (clears + replaces) —
    /// the last call wins, it does not append — matching <c>LyntaiBuilder.UseDefaultCandidates</c> exactly, so
    /// the two domains behave identically. Each entry is a provider id, optionally <c>"provider:model"</c>.</summary>
    public static LyntaiBuilder UseDefaultGenerationCandidates(this LyntaiBuilder builder, params string[] candidates)
    {
        var options = GenerationOptionsFor(builder);
        options.DefaultCandidates.Clear();
        options.DefaultCandidates.AddRange(candidates.Select(Parse));
        return builder;

        static GenerationCandidate Parse(string spec)
        {
            var at = spec.IndexOf(':');
            return at < 0
                ? new GenerationCandidate(spec.Trim())
                : new GenerationCandidate(spec[..at].Trim(), spec[(at + 1)..].Trim());
        }
    }

    /// <summary>Expose the generation domain to AGENTS as <see cref="Lyntai.Agents.ITool"/>s: <c>generate_backends</c>
    /// (discover what is available), <c>generate</c> (inline), and <c>generate_submit</c> /
    /// <c>generate_status</c> / <c>generate_fetch</c> (the asynchronous path a video render needs).
    ///
    /// This is the whole coupling between the two domains: the LLM side already knows <c>ITool</c>, so these
    /// work in the in-process tool loop and — with <c>AddMcpToolHost(...)</c> from
    /// <c>Lyntai.Tools.Mcp.Hosting</c> — for a CLI agent that runs its own loop over MCP. Neither domain
    /// references the other's concrete types (<c>docs/DECISIONS.md</c> D30).</summary>
    /// <remarks>Bytes are never returned in a tool observation (a base64 image would blow the context window for
    /// no benefit): if an <see cref="Lyntai.Generation.Jobs.IGenerationArtifactSink"/> is registered the artifacts are delivered to
    /// it and the observation says where they went, otherwise it reports their type/size/URI.</remarks>
    public static LyntaiBuilder AddGenerationTools(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationBackendsTool(
            sp.GetServices<IGenerationProvider>()));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationInlineTool(
            sp.GetRequiredService<Lyntai.Generation.Routing.IGenerationRouter>(),
            GenerationOptionsFor(sp),
            sp.GetService<Lyntai.Generation.Jobs.IGenerationArtifactSink>()));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationSubmitTool(
            sp.GetRequiredService<Lyntai.Generation.Routing.IGenerationRouter>(),
            GenerationOptionsFor(sp)));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationStatusTool(
            sp.GetServices<IGenerationProvider>()));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => new Lyntai.Generation.Tools.GenerationFetchTool(
            sp.GetServices<IGenerationProvider>(),
            sp.GetService<Lyntai.Generation.Jobs.IGenerationArtifactSink>()));
        return builder;
    }

    /// <summary>Resolved options, or defaults — the tools work before any candidate order is configured (a model
    /// can always name backends explicitly).</summary>
    private static GenerationOptions GenerationOptionsFor(IServiceProvider sp) =>
        sp.GetService<GenerationOptions>() ?? new GenerationOptions();

    /// <summary>The single <see cref="GenerationOptions"/> instance for this builder, registered as a singleton
    /// INSTANCE. Registering the instance (not a factory) is what lets configure-time mutation be visible to
    /// the resolved service — the same immediate-mutation model the builder's own <c>Options</c> uses.</summary>
    private static GenerationOptions GenerationOptionsFor(LyntaiBuilder builder) =>
        InstanceFor(builder, () => new GenerationOptions());

    /// <summary>The single <see cref="GenerationRoutingPolicy"/> for this builder, so
    /// <see cref="ConfigureGenerationRouting"/> and <see cref="AddGenerationProvider"/> agree on one
    /// instance regardless of call order.</summary>
    private static GenerationRoutingPolicy RoutingPolicyFor(LyntaiBuilder builder) =>
        InstanceFor(builder, () => new GenerationRoutingPolicy());

    /// <summary>Get-or-register a singleton INSTANCE for this builder. Registering the instance (not a
    /// factory) is what lets configure-time mutation be visible to the resolved service — the same
    /// immediate-mutation model the builder's own <c>Options</c> uses.</summary>
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
