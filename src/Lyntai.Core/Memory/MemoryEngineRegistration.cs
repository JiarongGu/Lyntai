using Lyntai.Cortex;
using Lyntai.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai;

/// <summary>Registers named memory engines inside <c>services.AddLyntai(cfg =&gt; …)</c>. Several coexist;
/// address them through <see cref="IMemoryEngineFactory"/>.</summary>
public static class MemoryEngineRegistration
{
    /// <summary>Register one working engine with no further configuration, and back
    /// <see cref="IPromptComposer"/> with it.
    /// <para>The one-line path, and deliberately so: a seam is an escape hatch, never the answer to "how
    /// does this work". Nothing has to be implemented to get working memory.</para>
    /// <para>Uses the decaying graph engine when an <see cref="IMemoryGraphStore"/> reached the container
    /// and the keyword store otherwise — decided when the container is BUILT, not here, because a storage
    /// backend may be registered after this call.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="name">The engine's name.</param>
    public static LyntaiBuilder AddMemory(this LyntaiBuilder builder, string name = "default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddMemoryEngine(name, e => e.UseBestAvailable()).UseMemoryComposer(name);
    }

    /// <summary>Register a named engine composed of the members <paramref name="configure"/> declares. An
    /// empty callback yields a lexical engine, so the name alone is enough to get something that
    /// works.</summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="name">The engine's name; must be unique in the container.</param>
    /// <param name="configure">Declares its members and budget.</param>
    /// <exception cref="InvalidOperationException">The name is already registered, or two members would
    /// share a hierarchical name.</exception>
    public static LyntaiBuilder AddMemoryEngine(this LyntaiBuilder builder, string name,
        Action<MemoryEngineBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // A duplicate name is caught HERE rather than at first resolve, so it surfaces at the line that
        // made the mistake. The already-registered set is read back off the service collection — the same
        // sentinel-descriptor technique the storage governance guard uses, and it needs no extra state on
        // LyntaiBuilder.
        if (builder.Services.Any(d =>
                d.ServiceType == typeof(MemoryEngineComposition) &&
                d.ImplementationInstance is MemoryEngineComposition existing &&
                string.Equals(existing.Name, name, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"A memory engine named '{name}' is already registered. Names must be unique — an engine " +
                "is addressed by name, so a duplicate makes one of them unreachable.");

        var engineBuilder = new MemoryEngineBuilder(name);
        configure?.Invoke(engineBuilder);
        if (!engineBuilder.HasMembers) engineBuilder.UseLexical();
        engineBuilder.Validate();

        builder.Services.AddSingleton(new MemoryEngineComposition(name, engineBuilder.Composition));

        // NOT TryAdd. A TryAddSingleton reached during configure(builder) BEATS AddLyntai's own later
        // registration, which once silently swapped a configured DeadHostTracker for parameterless
        // defaults and was missed by 1427 tests. A plain AddSingleton into the collection, read back by
        // the factory, has no such ordering hazard.
        builder.Services.AddSingleton<IMemoryEngine>(sp => engineBuilder.Build(sp));
        builder.Services.AddSingleton<IMemoryEngineFactory>(sp =>
            new MemoryEngineFactory(sp.GetServices<IMemoryEngine>()));

        return builder;
    }

    /// <summary>Expose a named engine to the model as a pair of tools — <c>{prefix}_recall</c> returns
    /// headlines, <c>{prefix}_expand</c> returns one item's full text and what it is linked to. Registered
    /// as ordinary <see cref="Lyntai.Agents.ITool"/>s, so they reach the tool loop and the MCP bridge alike.
    /// <para>Names are prefixed per engine rather than one multiplexed tool taking an engine argument:
    /// fewer tools would read better, but it would let the model consult the WRONG memory, and a wrong
    /// memory is worse than a missing one. The prefix defaults to the engine's name with anything a tool
    /// name cannot carry replaced — a hierarchical member name like <c>project/graph</c> becomes
    /// <c>project_graph</c>.</para>
    /// <para><paramref name="taskKey"/> is the DEFAULT the tools read and write under; override it per turn
    /// with <see cref="MemoryToolScope.Use"/>, which a chat application needs since its task is
    /// per-conversation.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="name">The engine to expose.</param>
    /// <param name="taskKey">Default task key.</param>
    /// <param name="scope">Default scope, or null for every scope of the task.</param>
    /// <param name="prefix">Overrides the derived tool-name prefix.</param>
    public static LyntaiBuilder AddMemoryTools(this LyntaiBuilder builder, string name, string taskKey,
        string? scope = null, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);

        var resolved = prefix ?? MemoryTools.ToolPrefix(name);
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => MemoryTools.Recall(
            sp.GetRequiredService<IMemoryEngineFactory>().Get(name), resolved, taskKey, scope));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => MemoryTools.Expand(
            sp.GetRequiredService<IMemoryEngineFactory>().Get(name), resolved));
        return builder;
    }

    /// <summary>Back <see cref="IPromptComposer"/> — what <c>ChatOrchestrator</c> composes with — using the
    /// named engine.
    /// <para>Without this call the existing flat composer stays in place, so adding an engine never changes
    /// an application's prompts by itself. This registers plainly rather than with <c>TryAdd</c>, which is
    /// what lets it win over the <c>TryAdd</c>-registered default that <c>AddLyntai</c> applies
    /// afterwards.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="name">The engine to compose from.</param>
    public static LyntaiBuilder UseMemoryComposer(this LyntaiBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.Services.AddSingleton<IPromptComposer>(sp => new EngineBackedPromptComposer(
            sp.GetRequiredService<IMemoryEngineFactory>().Get(name), CompositionOf(sp, name)));
        return builder;
    }

    private static MemoryCompositionOptions CompositionOf(IServiceProvider sp, string name) =>
        sp.GetServices<MemoryEngineComposition>()
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))?.Options
        ?? new MemoryCompositionOptions();
}

/// <summary>Carries a named engine's composition options into the container, so
/// <see cref="MemoryEngineRegistration.UseMemoryComposer"/> can find them without a second builder pass —
/// and so <see cref="MemoryEngineRegistration.AddMemoryEngine"/> can detect a duplicate name at configure
/// time.</summary>
/// <param name="Name">The engine's name.</param>
/// <param name="Options">Its composition options.</param>
public sealed record MemoryEngineComposition(string Name, MemoryCompositionOptions Options);
