using System.Diagnostics.CodeAnalysis;
using Lyntai.Agents;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Microsoft.Extensions.DependencyInjection;

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
