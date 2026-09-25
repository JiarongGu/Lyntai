using Lyntai.Inference;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Memory.Seeding;
using Lyntai.Memory.Verification;

namespace Lyntai.Memory.Engines;

/// <summary>The seams one <see cref="GraphMemoryEngine"/> is built from — its policies, its similarity index and
/// the backends that feed it. Every member is optional and null takes the engine's own default, so a record
/// naming nothing builds exactly the model-free engine.
/// <para><b>An init-only record rather than a constructor parameter each</b>, so adding a seam is a new
/// property and never re-binds a positional caller, and the container's one construction site
/// (<c>MemoryEngineBuilder</c>) fills one object rather than mirroring a long argument list.</para></summary>
public sealed record GraphMemorySeams
{
    /// <summary>The decay curve; null builds a <see cref="DsrRetrievability"/> with default options — the same
    /// curve a DI-built engine takes (<c>docs/DECISIONS.md</c> D49).</summary>
    public IMemoryRetrievabilityPolicy? Retrievability { get; init; }

    /// <summary>The coexisting retention dimensions (PLURAL, <c>docs/DECISIONS.md</c> D48), composed by the engine
    /// over <see cref="Retrievability"/>; null or empty leaves the curve as supplied.
    /// <para><b>Supplying these AND an already-modulated curve throws</b>, because it would apply retention
    /// twice. A <see cref="ModulatedRetrievability"/> passed as <see cref="Retrievability"/> ALONE stays
    /// supported.</para></summary>
    public IEnumerable<IMemoryRetentionPolicy>? RetentionPolicies { get; init; }

    /// <summary>How several retention dimensions combine into one factor; null takes the multiplicative
    /// default. Irrelevant when fewer than two are registered.</summary>
    public IMemoryRetentionCompositionPolicy? RetentionComposition { get; init; }

    /// <summary>What one write does to this memory — the coexisting age dimensions; null or empty takes a single
    /// burst-damped per-write policy. <b>The damping is not optional garnish</b>: an undamped count-based policy
    /// lets a bulk ingest wipe everything stored before it. Each policy's <see cref="IMemoryAgePolicy.Kind"/>
    /// decides whether its age is projected from the primitives (<see cref="GraphNode.AgeSample"/>) or read from
    /// the store's accumulator (<see cref="GraphNode.Age"/>); at most one may be Accumulating.</summary>
    public IEnumerable<IMemoryAgePolicy>? AgePolicies { get; init; }

    /// <summary>How several age policies combine into one tick and one age; null takes
    /// <see cref="SummedAgeCompositionPolicy"/>. Irrelevant for a single policy.</summary>
    public IMemoryAgeCompositionPolicy? AgeComposition { get; init; }

    /// <summary>Judge how strongly a write is encoded — the coexisting salience dimensions; null or empty takes a
    /// single <see cref="StructuralSaliencePolicy"/> (an empty collection does NOT turn salience off; register
    /// <see cref="NeutralSaliencePolicy"/> alone for that). Without a vector backend there is no novelty to judge
    /// and it reports nothing.</summary>
    public IEnumerable<IMemorySaliencePolicy>? SaliencePolicies { get; init; }

    /// <summary>How several salience policies' bags combine into one; null takes
    /// <see cref="MaximalSalienceCompositionPolicy"/>.</summary>
    public IMemorySalienceCompositionPolicy? SalienceComposition { get; init; }

    /// <summary>Turns seeded, spread candidates into a scored, best-first order; null takes
    /// <see cref="ReciprocalRankFusionPolicy"/>, the registered default. See <see cref="IMemoryRankingPolicy"/>
    /// for what a policy may NOT do: the engine re-admits authoritative material a policy DROPS, though not one it
    /// SUBSTITUTES under the same id.</summary>
    public IMemoryRankingPolicy? Ranking { get; init; }

    /// <summary>Alternate ranking policies this engine exposes for a per-call
    /// <see cref="MemoryQuery.RankingPolicyName"/>; null or empty exposes none. Compared ordinally; a query naming
    /// anything else throws <see cref="KeyNotFoundException"/> rather than silently falling back.</summary>
    public IReadOnlyDictionary<string, IMemoryRankingPolicy>? NamedRankingPolicies { get; init; }

    /// <summary>Judges what each written fact is ABOUT, so entries concerning one entity connect — the only
    /// mechanism reaching a cluster whose members share no distinguishing word. Null is the model-free floor: no
    /// annotation and no subject links.</summary>
    public IMemoryAnnotationPolicy? Annotation { get; init; }

    /// <summary>Judges which of a recall's candidates ANSWERED the query, so reinforcement follows evidence and an
    /// outranked answer can be promoted past the limit. Null is the model-free floor: the ranking stands and
    /// everything returned is reinforced.</summary>
    public IMemoryVerificationPolicy? Verification { get; init; }

    /// <summary>The retrieval CHANNELS a recall gathers candidates from (PLURAL, D48); null or empty takes
    /// <see cref="LexicalSeedSource"/> and <see cref="SubjectSeedSource"/>, the two a DI-built engine registers.
    /// Two sources sharing a <see cref="IMemorySeedSource.Name"/> throws.</summary>
    public IEnumerable<IMemorySeedSource>? SeedSources { get; init; }

    /// <summary>The backends; with <see cref="Vectors"/>, one declaring <see cref="ProviderKinds.Vector"/> turns
    /// on similarity enrichment — each write is embedded, judged for novelty and linked to its nearest
    /// neighbours. Without them the graph still forms from co-activation and explicit links.</summary>
    public IEnumerable<IModelProvider>? Providers { get; init; }

    /// <summary>The similarity index; see <see cref="Providers"/>. Every removal reaches it too, so wire the same
    /// store an earlier configuration indexed into.</summary>
    public IVectorStore? Vectors { get; init; }

    /// <summary>The SHARED dead-host cooldown and admission for enrichment's embedding calls. Null routes bare, so
    /// a vector backend that rate-limited is asked again on the very next write.</summary>
    public IProviderRouterFactory? Routing { get; init; }

    /// <summary>Reads "now" for <see cref="GraphMemoryEngine.PruneAsync"/>'s <c>olderThan</c> criterion on the
    /// derivable path; null takes <see cref="DateTimeOffset.UtcNow"/>. Mirrors the clock every
    /// <see cref="IMemoryGraphStore"/> takes, so a test faking one can fake both.</summary>
    public Func<DateTimeOffset>? Clock { get; init; }
}
