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
/// <param name="Metadata">Whatever the caller put in <see cref="MemoryWrite.Metadata"/>, or null.
/// <para><b>Null means the engine does not carry metadata, never that the caller wrote none</b> — the two are
/// not distinguishable here and a consumer needing them apart should write a sentinel key. Graph and curated
/// engines round-trip it; lexical and semantic return null, because <c>MemoryEntry</c> and a vector hit have
/// nowhere to keep it. A RECALL and an EXPANSION answer alike — the expanded entry carries whatever its
/// neighbours do. Pinned for every engine by
/// <c>MemoryEngineContract.Metadata_written_is_returned_or_explicitly_absent</c> and, for the expansion path
/// the first one does not call, <c>Metadata_survives_an_EXPANSION_not_only_a_recall</c>.</para>
/// <para>It is the same <c>string→string</c> escape hatch the write side takes, kept deliberately opaque:
/// a kind, a source, a caller's own ordering key are all the consumer's vocabulary, and Core stays neutral of
/// it (<c>generic-library</c> rule 3).</para></param>
public sealed record MemoryItem(
    MemoryRef Reference,
    string Headline,
    string? Content,
    MemoryGrade Grade,
    double Relevance,
    double Retrievability,
    int Degree,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>The result of a recall, with the tiers that actually ran.</summary>
/// <param name="Items">Hits, most relevant first.</param>
/// <param name="Ran">Which tiers produced them — so an empty tier is distinguishable from an absent
/// one.</param>
/// <param name="Answered">Whether a judge found anything here that actually answered the query — the
/// ABSTENTION signal, and the only ABSOLUTE quality statement a recall makes.
/// <para><c>null</c> = no judgement happened (no <c>IMemoryVerificationPolicy</c> registered, or it returned
/// no opinion). <c>true</c> = at least one returned item was judged to answer. <c>false</c> = a judge looked
/// at these items and said NONE of them did.</para>
/// <para><b>Nullable for the same reason <c>MemoryReviewWrite.Verified</c> is:</b> <c>false</c> is an
/// observed judgement and <c>null</c> is the absence of one, and they are not interchangeable. With the
/// shipped default (no verifier) this is always <c>null</c> — never <c>false</c> — so a consumer that
/// abstains on <c>false</c> does not abstain on every recall.</para>
/// <para><b>It is ADVISORY, not a filter.</b> The items are still returned; <c>false</c> says the engine
/// does not believe they answer the question. Dropping them is
/// <c>GraphMemoryOptions.VerificationFilters</c>, a separate and deliberate opt-in — a mistaken verdict
/// should cost a little confidence, not a lost result.</para>
/// <para><b>Distinct from <c>RelativeFloor</c>,</b> which is relative to the best candidate present: a page
/// of uniformly-irrelevant material passes that floor with a perfectly good internal ranking. This is the
/// absolute question, and answering it is why the verification seam exists.</para></param>
public sealed record MemoryRecall(IReadOnlyList<MemoryItem> Items, MemorySources Ran, bool? Answered = null)
{
    /// <summary>An empty result from no tier at all.</summary>
    public static MemoryRecall Empty { get; } = new([], MemorySources.None);
}
