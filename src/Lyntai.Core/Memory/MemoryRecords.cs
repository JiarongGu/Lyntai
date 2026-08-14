namespace Lyntai.Memory;

/// <summary>A fact to remember, scoped by (<paramref name="TaskKey"/>, <paramref name="Scope"/>) like every
/// other memory surface in the library.</summary>
/// <param name="TaskKey">The consumer/purpose this fact belongs to.</param>
/// <param name="Scope">The variant within that task.</param>
/// <param name="Content">The fact itself.</param>
/// <param name="Headline">An authored one-line form. When null, an engine that supports headlines derives
/// one for ASSOCIATIVE material only — authoritative content is never truncated, because a headline that
/// drops a qualifier is worse than no memory at all.</param>
/// <param name="Grade">Explicit grade, which WINS over the engine's own role. The default
/// <see cref="MemoryGrade.Inherit"/> takes the role.</param>
/// <param name="Metadata">Arbitrary app-owned extra data. An engine whose store cannot hold it ignores
/// it.</param>
public sealed record MemoryWrite(
    string TaskKey,
    string Scope,
    string Content,
    string? Headline = null,
    MemoryGrade Grade = MemoryGrade.Inherit,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>What to recall.</summary>
/// <param name="TaskKey">The consumer/purpose to recall within.</param>
/// <param name="Scope">Optional variant filter; null recalls across the task's scopes.</param>
/// <param name="Query">Optional relevance query; null or whitespace recalls the most recent.</param>
/// <param name="Limit">Maximum items; null takes the engine's default.</param>
/// <param name="CharBudget">Maximum characters the caller intends to spend; null takes the engine's
/// configured budget.</param>
/// <param name="RankingPolicyName">Selects a ranking policy BY NAME for this one call, overriding whatever
/// the engine was configured with — null takes the engine's own default.
/// <para><b>A name, never a policy instance.</b> A live <c>IMemoryRankingPolicy</c> is a service, and this
/// record is otherwise plain data — serialized, logged, traced — so it carries the name a policy was
/// registered under (<see cref="Lyntai.Memory.MemoryEngineBuilder.UseGraph"/>'s <c>namedRankingPolicies</c>),
/// never the service itself.</para>
/// <para><b>An unknown name is an error, never a silent fallback.</b> A typo that quietly reverted to the
/// default would surface, if at all, as "ranking seems off" months later rather than as a call that fails
/// where it was made. An engine with no ranking concept (lexical, semantic, curated) simply ignores this
/// field — only <c>GraphMemoryEngine</c> consults it today.</para></param>
public sealed record MemoryQuery(
    string TaskKey,
    string? Scope = null,
    string? Query = null,
    int? Limit = null,
    int? CharBudget = null,
    string? RankingPolicyName = null);

/// <summary>One recalled entry.</summary>
/// <param name="Reference">Its address, carrying the owning engine.</param>
/// <param name="Headline">The one-line form. For associative material this may be derived; for
/// authoritative material it is always the full content.</param>
/// <param name="Content">The full content, or null for an associative item that has been recalled but not
/// expanded — that is what makes the first load cheap. ALWAYS populated for authoritative material.</param>
/// <param name="Grade">Resolved grade; never <see cref="MemoryGrade.Inherit"/>.</param>
/// <param name="Relevance">How well it matched, 0..1. An engine that ranks by recency reports 1. Comparable
/// WITHIN one recall from one engine and no further — a graph engine passes its store's own normalized rank
/// position through, and <see cref="IMemoryGraphStore.SeedAsync"/> makes that position explicitly
/// backend-specific.</param>
/// <param name="Retrievability">How well remembered it is, 0..1. An engine with no decay model reports
/// 1.</param>
/// <param name="Degree">How many other entries it is linked to. An engine with no graph reports 0.</param>
public sealed record MemoryItem(
    MemoryRef Reference,
    string Headline,
    string? Content,
    MemoryGrade Grade,
    double Relevance,
    double Retrievability,
    int Degree);

/// <summary>The result of a recall, with the tiers that actually ran.</summary>
/// <param name="Items">Hits, most relevant first.</param>
/// <param name="Ran">Which tiers produced them — so an empty tier is distinguishable from an absent
/// one.</param>
public sealed record MemoryRecall(IReadOnlyList<MemoryItem> Items, MemorySources Ran)
{
    /// <summary>An empty result from no tier at all.</summary>
    public static MemoryRecall Empty { get; } = new([], MemorySources.None);
}
