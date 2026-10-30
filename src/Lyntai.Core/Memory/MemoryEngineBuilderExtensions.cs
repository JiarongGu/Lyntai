using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Memory.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lyntai;

/// <summary>Registers named memory engines inside <c>services.AddLyntai(cfg =&gt; …)</c>. Several coexist;
/// address them through <see cref="IMemoryEngineFactory"/>.</summary>
public static class MemoryEngineBuilderExtensions
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

        AddContainerDefaults(builder.Services);

        var engineBuilder = new MemoryEngineBuilder(name);
        configure?.Invoke(engineBuilder);
        if (!engineBuilder.HasMembers) engineBuilder.UseLexical();
        engineBuilder.Validate();

        builder.Services.AddSingleton(
            new MemoryEngineComposition(name, engineBuilder.Composition, engineBuilder.Strict));

        // NOT TryAdd. A TryAddSingleton reached during configure(builder) BEATS AddLyntai's own later
        // registration, which once silently swapped a configured DeadHostTracker for parameterless
        // defaults and was missed by 1427 tests. A plain AddSingleton into the collection, read back by
        // the factory, has no such ordering hazard.
        builder.Services.AddSingleton<IMemoryEngine>(sp => engineBuilder.Build(sp));

        return builder;
    }

    /// <summary>What every <see cref="AddMemoryEngine"/> call needs once per CONTAINER. All <c>TryAdd</c>, so
    /// a second call adds nothing and a consumer's own registration wins — which for a PLURAL seam depends on
    /// ORDER, as each block says.</summary>
    private static void AddContainerDefaults(IServiceCollection services)
    {
        // Salience is PLURAL: a consumer's policy registered BEFORE this call REPLACES the default; registered
        // after, both run, composed by IMemorySalienceCompositionPolicy (GraphMemoryWiringTests' ordering
        // pair). SalienceOptions comes from the container, so the policy's ceiling and
        // SalienceRetentionPolicy's declared bound cannot drift apart.
        services.TryAddSingleton<IMemorySaliencePolicy>(sp =>
            new StructuralSaliencePolicy(sp.GetService<SalienceOptions>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemoryRetentionPolicy, SalienceRetentionPolicy>());

        // The default ranking policy and forgetting curve (docs/DECISIONS.md D49). Each is SINGULAR, so a
        // consumer's own registration, before or after, wins outright.
        services.TryAddSingleton<IMemoryRankingPolicy>(sp =>
            new ReciprocalRankFusionPolicy(sp.GetService<ReciprocalRankFusionOptions>()));
        services.TryAddSingleton<IMemoryRetrievabilityPolicy>(sp =>
            new DsrRetrievability(sp.GetService<DsrOptions>()));

        // The two default seed channels: the store's text read and the subject lookup. The VECTOR channel is
        // opt-in (AddMemorySemanticSeeds) — a vector backend registered for other reasons must not start
        // steering recall. TryAddEnumerable refuses a repeat of one implementation type, which is what keeps
        // two same-named sources (refused by the engine) from ever being registered.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemorySeedSource, LexicalSeedSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMemorySeedSource, SubjectSeedSource>());

        // The wiring check runs over the CONTAINER rather than in Build, because two of its findings are about
        // it — a policy registered and consulted by no engine. The report is its own service so a host reads it
        // without the factory; building the factory logs it and, under StrictWiring, throws.
        services.TryAddSingleton<MemoryWiringReport>(MemoryWiring.Inspect);
        services.TryAddSingleton<IMemoryEngineFactory>(sp =>
        {
            var engines = sp.GetServices<IMemoryEngine>().ToList();
            MemoryWiring.Report(sp.GetRequiredService<MemoryWiringReport>(), sp,
                sp.GetServices<MemoryEngineComposition>().Any(c => c.Strict));
            return new MemoryEngineFactory(engines);
        });
    }

    /// <summary>Register an <see cref="IMemoryChangeApprover"/>: a proposed supersession or end, from an agent or any caller of
    /// <see cref="MemoryChangeGate"/>, is applied only if every registered approver approves it. With none registered
    /// nothing is ended. <b>The library ships no approver that approves by itself</b> (<c>docs/DECISIONS.md</c> D241).</summary>
    /// <typeparam name="T">The approver, constructed by the container.</typeparam>
    /// <param name="builder">The Lyntai builder.</param>
    public static LyntaiBuilder AddMemoryChangeApprover<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this LyntaiBuilder builder)
        where T : class, IMemoryChangeApprover
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IMemoryChangeApprover, T>();
        builder.Services.TryAddSingleton<MemoryChangeGate>();
        return builder;
    }

    /// <summary>Register a memory-change approver built from the service provider.</summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="factory">Builds the approver.</param>
    public static LyntaiBuilder AddMemoryChangeApprover(this LyntaiBuilder builder, Func<IServiceProvider, IMemoryChangeApprover> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.Services.AddSingleton(factory);
        builder.Services.TryAddSingleton<MemoryChangeGate>();
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
    /// <para><paramref name="taskKey"/> is the DEFAULT the tools read under; override it per turn
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
            sp.GetRequiredService<IMemoryEngineFactory>().Get(name), resolved, taskKey, scope));
        return builder;
    }

    /// <summary>Let a model write and end what it remembers in a named engine, as two more tools: <c>{prefix}_remember</c> (content,
    /// headline, validity, <c>supersedes</c>, <c>disputes</c>, <c>amends</c>) and <c>{prefix}_end</c> (ref, revision, reason).
    /// <see cref="AddMemoryTools"/> never registers these, so no existing host's agent gains a write it did not choose
    /// (<c>docs/DECISIONS.md</c> D239).
    /// <para>The HOST owns the task (from <see cref="MemoryToolScope"/>, never an argument), the grade (always associative, with
    /// no argument to change it) and the metadata stamp. <c>disputes</c> and <c>amends</c> end nothing and apply alone;
    /// <c>supersedes</c> and the end tool go through the registered <see cref="IMemoryChangeApprover"/>s and apply only if all
    /// approve, bound to the revision the model read — with none registered nothing ends (D241). A write returns
    /// <c>mayUpdate</c>, the live entries it may update, and applies none of them.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="name">The engine to expose.</param>
    /// <param name="taskKey">Default task key; override per turn with <see cref="MemoryToolScope.Use"/>.</param>
    /// <param name="scope">The scope a write lands in. Required: an entry has one.</param>
    /// <param name="prefix">Overrides the derived tool-name prefix, which is the same one <see cref="AddMemoryTools"/> derives.</param>
    /// <param name="options">The stamp, the content bound and whether a model may name a scope; null takes the defaults.</param>
    public static LyntaiBuilder AddMemoryWriteTools(this LyntaiBuilder builder, string name, string taskKey, string scope,
        string? prefix = null, MemoryWriteToolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var resolved = prefix ?? MemoryTools.ToolPrefix(name);
        // the gate with no approver is the gate that ends nothing, so a host that registered none still gets a safe tool
        builder.Services.TryAddSingleton<MemoryChangeGate>(sp => new MemoryChangeGate(sp.GetServices<IMemoryChangeApprover>()));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => MemoryTools.Remember(
            sp.GetRequiredService<IMemoryEngineFactory>().Get(name), sp.GetRequiredService<MemoryChangeGate>(),
            resolved, taskKey, scope, options));
        builder.Services.AddSingleton<Lyntai.Agents.ITool>(sp => MemoryTools.End(
            sp.GetRequiredService<IMemoryEngineFactory>().Get(name), sp.GetRequiredService<MemoryChangeGate>(),
            resolved, taskKey, scope));
        return builder;
    }

    /// <summary>Back <see cref="IPromptComposer"/> — what <c>ChatOrchestrator</c> composes with AND remembers
    /// each exchange through — using the named engine, so the chat writes into the engine it reads from.
    /// <para>Without this call the default composer (over the keyword and semantic stores) stays in place, so
    /// adding an engine never changes an application's prompts by itself. This registers plainly rather than
    /// with <c>TryAdd</c>, which is what lets it win over the <c>TryAdd</c>-registered default that
    /// <c>AddLyntai</c> applies afterwards.</para></summary>
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
/// <see cref="MemoryEngineBuilderExtensions.UseMemoryComposer"/> can find them without a second builder pass —
/// and so <see cref="MemoryEngineBuilderExtensions.AddMemoryEngine"/> can detect a duplicate name at configure
/// time.</summary>
/// <param name="Name">The engine's name.</param>
/// <param name="Options">Its composition options.</param>
/// <param name="Strict">Whether this engine asked for wiring warnings to fail startup.</param>
internal sealed record MemoryEngineComposition(string Name, MemoryCompositionOptions Options,
    bool Strict = false);
