using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // TryAdd, unlike the plain AddSingleton below: a consumer's own IMemorySaliencePolicy/
        // IMemoryRetentionPolicy must win, and a second AddMemoryEngine call must not pile a second default
        // retention policy into the collection. Nothing else AddLyntai registers later contends for these two
        // service types, so this doesn't fall into the ordering hazard the comment above warns about.
        //
        // ORDERING NOW CHANGES THE OUTCOME for IMemorySaliencePolicy specifically, since Task 3 made
        // salience plural (GraphMemoryBuilder resolves via GetServices, not GetService) — a DELIBERATE
        // asymmetry, tested by both halves of GraphMemoryWiringTests' own salience-ordering pair. Registered
        // BEFORE this call, a consumer's own policy makes TryAddSingleton a no-op (one already exists), so
        // it REPLACES the default outright — nothing else is ever registered. Registered AFTER, the default
        // is already seeded, so the consumer's own AddSingleton is a genuine SECOND registration: both run,
        // composed by whatever IMemorySalienceCompositionPolicy is registered (MaximalSalienceCompositionPolicy by
        // default). A consumer who wants a pure replacement registers before AddLyntai, whichever direction
        // they call it from. IMemoryRetentionPolicy carries no such asymmetry — SalienceRetentionPolicy is
        // registered via TryAddEnumerable, which is unconditionally additive regardless of ordering, and that
        // was already true before this task.
        //
        // sp.GetService<SalienceOptions>(), never a hardcoded null: SalienceRetentionPolicy below is registered BY
        // TYPE, so the container already injects a registered SalienceOptions into its optional parameter.
        // A salience policy that took the defaults regardless would leave the two types whose whole contract is
        // "the reported ceiling and the declared bound cannot drift apart" (SalienceTests) drifted — through
        // an entirely supported registration, and DI registration is the only configuration path
        // SalienceOptions has.
        builder.Services.TryAddSingleton<IMemorySaliencePolicy>(sp =>
            new StructuralSaliencePolicy(sp.GetService<SalienceOptions>()));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMemoryRetentionPolicy, SalienceRetentionPolicy>());

        // ReciprocalRankFusionPolicy is the REGISTERED default ranking policy as of 3.0 (owner ruling,
        // 2026-08-11 — see docs/DECISIONS.md) — not because rank fusion is universally better, but because
        // this library's own measurement (local/superpowers/records/2026-08-09-memory-policy-measurement.md, fsrs-properly plan
        // Task 4) found RRF beating MultiplicativeRankingPolicy on the corpus's `topical` class in ALL SIX
        // measured shapes, reproduced across two independent runs (+0.238..+0.719 pre-fix, +0.431..+0.746
        // post the difficulty-neutral fix — same direction, same shapes, both clearing the ±0.10 action
        // threshold). That result agrees with the mechanism the same measurement pinned earlier:
        // Multiplicative's product-of-factors formula rewards RAW REINFORCEMENT MAGNITUDE, which is exactly
        // what let an unmeasured flat multiplier out-rank a curve (DsrRetrievability) that correctly declined
        // to over-strengthen — RRF's rank-position fusion does not carry that bias. Multiplicative is NOT
        // the HalfLifeRetrievability case: its formula is not unmeasured-and-wrong, it simply lost a
        // measured comparison on this one dimension, and it remains the better choice on a scale where raw
        // magnitude is meaningful — it stays shipped, registerable in one line
        // (`services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy())` before
        // `AddLyntai`, or after — either direction wins, the same TryAdd ordering below already establishes).
        //
        // The floor ships at RRF's OWN default (0), not the 0.02 the measurement's own confound control
        // equalized both ranking arms at. That is a disclosed gap between what was measured and what ships —
        // stated, not papered over — but it does not weaken the result: a direct instrumentation check
        // (2026-08-11, replaying every corpus shape under RelativeFloor=0.02) found the floor cut ZERO
        // candidates anywhere (995 Rank() calls, 48,120 candidate evaluations, tightest worst/best score
        // ratio observed 0.702 — nowhere near the 0.02 needed to bite). RRF's own compressed score range
        // (forty candidates fused at the default K=60 span only a 100/61 ≈ 1.639× ratio top to bottom) makes
        // a 2% relative floor structurally unable to cut anything at any candidate-set size this library
        // ships with, so 0.02 and 0 are EMPIRICALLY identical on the measured corpus, confirmed rather than
        // assumed — see ReciprocalRankFusionOptions.RelativeFloor's own remarks for what value would actually
        // bite on this policy's range, for a consumer who wants floor-based burial under RRF specifically.
        //
        // Same TryAdd reasoning as the salience policy above: exactly one ranking policy is ever consulted (it is
        // resolved with GetService, not GetServices), so a consumer's own AddSingleton<IMemoryRankingPolicy>
        // — called before OR after this — wins over this default, whether it replaces the whole policy or
        // just registers its own ReciprocalRankFusionOptions for this one to read.
        builder.Services.TryAddSingleton<IMemoryRankingPolicy>(sp =>
            new ReciprocalRankFusionPolicy(sp.GetService<ReciprocalRankFusionOptions>()));

        // DsrRetrievability is the REGISTERED default forgetting curve as of 3.0 (docs/DECISIONS.md D49) —
        // not because a corpus said so, but because FSRS is validated against hundreds of millions of real
        // reviews, unlike the exponential curve this domain used to also ship (HalfLifeRetrievability,
        // reinforcing by a flat factor its own doc called "reasoned, not measured" — DELETED in 3.0, its
        // provenance bit retired rather than reused, see MemoryRetrievabilityProvenance.HalfLife). This
        // library's own falsification pass did not falsify DSR, with one disclosed, known gap: DSR is a
        // PARTIAL, UNFITTED FSRS (no per-review difficulty update, published rather than fitted constants),
        // and that gap was measurably where DSR lost to the now-deleted curve on the corpus's `topical` class
        // under this exact ranking pairing — see D49 and TASKS.md for the prioritized follow-up. Same TryAdd
        // reasoning as the ranking policy above: a consumer's own AddSingleton<IMemoryRetrievabilityPolicy>,
        // before or after this call, wins outright — UseGraph/UseBestAvailable read this with a plain
        // sp.GetService<IMemoryRetrievabilityPolicy>() at container-build time (now GetRequiredService, since
        // this TryAdd guarantees something is always here by the time either reads it — no bare `?? new …()`
        // fallback needed at either call site any more). Before the D49 task neither AddLyntai nor
        // AddMemoryEngine registered ANY default here (both builder call sites fell through to their own bare
        // `?? new HalfLifeRetrievability(...)`); this TryAdd is what made "the default" a single,
        // container-visible fact instead of an unregistered fallback two call sites each restated — and
        // deleting that curve in 3.0 is what let the two call sites' fallbacks go entirely.
        builder.Services.TryAddSingleton<IMemoryRetrievabilityPolicy>(sp =>
            new DsrRetrievability(sp.GetService<DsrOptions>()));

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
internal sealed record MemoryEngineComposition(string Name, MemoryCompositionOptions Options);
