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
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>When this fact started holding in the world; null states nothing. Valid time is the writer's
    /// claim about the world, not when the engine heard of it, so a late write can name an instant before
    /// earlier ones. On a re-remember a stated value replaces the stored one and an unstated one keeps it.
    /// <para>An engine that cannot hold valid time THROWS <see cref="NotSupportedException"/> on a write that
    /// states any — storing it as open-ended would return an ended fact as current.</para></summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>When it stopped holding, exclusive: valid at every instant before it, not at it. Null states
    /// nothing. Must not precede <see cref="ValidFrom"/> when both are stated.</summary>
    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>The entries this one SUPERSEDES: each ends where this one starts, and is never deleted — it
    /// still reads in a <see cref="MemoryValidityReading.History"/> or earlier <see cref="MemoryValidityReading.AsOf"/>
    /// reading. <b>Only a caller names a predecessor; nothing is inferred from similar text.</b> A write naming
    /// one with no <see cref="ValidFrom"/> starts when it is written.
    /// <para>Each reference must name this engine's own entry in this write's task, and an entry has one
    /// successor: naming one a different entry already superseded throws <see cref="InvalidOperationException"/>
    /// before anything is written, and a <see cref="ValidFrom"/> earlier than a named entry's own start throws
    /// <see cref="ArgumentException"/>.</para></summary>
    public IReadOnlyList<MemoryRef>? Supersedes { get; init; }

    /// <summary>Live entries of this engine and task this write DISPUTES — its writer says their claim is wrong or in doubt.
    /// Ends nothing: both stay current, and a recall reports the relation as <see cref="MemoryItem.Relations"/> where
    /// both ends are in the reading, as the writer's attributed assertion. <b>Only a caller states one</b>; nothing is
    /// inferred from similar text. Refused, before anything is written, for an entry that is not valid now, another
    /// engine's or task's, the write itself, or a store that cannot read the relation back
    /// (<see cref="ITypedLinkMemoryGraphStore"/>).</summary>
    public IReadOnlyList<MemoryRef>? Disputes { get; init; }

    /// <summary>Live entries this write AMENDS: it corrects or extends one claim of each and leaves the rest of it standing,
    /// which a whole-entry supersession would not. Stated, reported and refused as <see cref="Disputes"/> is.</summary>
    public IReadOnlyList<MemoryRef>? Amends { get; init; }

    /// <summary>Whether this write states valid time at all — what an engine that cannot hold it refuses.</summary>
    public bool StatesValidity => ValidFrom is not null || ValidTo is not null || Supersedes is { Count: > 0 };
}

/// <summary>How much of each matched entry a recall returns.
/// <para>The default withholds associative content until an expansion asks for it, which is what makes a
/// first load cheap (<c>docs/DECISIONS.md</c> D100). Authoritative material is never truncated under either
/// value, because a truncated exact fact is confidently wrong.</para></summary>
public enum MemoryDetail
{
    /// <summary>Content MAY be withheld: <see cref="MemoryItem.Headline"/> is always populated, and
    /// <see cref="MemoryItem.Content"/> is whatever the engine chooses to carry. An engine whose entries have
    /// no separate short form (lexical, semantic, curated) populates both; <c>GraphMemoryEngine</c> returns a
    /// headline alone for associative material.</summary>
    Headline = 0,

    /// <summary>Content is populated wherever the engine holds it, so an item comes back whole rather than
    /// as a pointer to itself. Costs what the extra text costs — <see cref="MemoryQuery.CharBudget"/> prices
    /// it and will therefore admit fewer items for the same budget.</summary>
    Full = 1,
}

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
/// <param name="Detail">How much of each matched entry to return. Defaults to
/// <see cref="MemoryDetail.Headline"/>.
/// <para><b>Per CALL rather than per deployment, because one application wants both.</b> A cheap index of
/// what is related wants headlines; a reader that must answer from the excerpts wants the whole entry, and
/// the same host does both at different call sites. Measured on LoCoMo: returning full content for the SAME
/// twenty items is worth <b>+11.7 token-F1</b> and closes essentially the whole gap to plain cosine
/// (<c>docs/task-archive.md</c> Part 136) — so this is not a preference, it is the difference between a
/// reader answering and refusing.</para>
/// <para>The engines whose entries have no separate short form (lexical, semantic, curated) already return
/// content under either value; only <c>GraphMemoryEngine</c> changes behaviour here.</para></param>
public sealed record MemoryQuery(
    string TaskKey,
    string? Scope = null,
    string? Query = null,
    int? Limit = null,
    int? CharBudget = null,
    string? RankingPolicyName = null,
    MemoryDetail Detail = MemoryDetail.Headline)
{
    /// <summary>Attach <see cref="MemoryRecall.Candidates"/>: every candidate the recall considered and why
    /// each was or was not returned. Off by default, and off costs nothing; on, the recall itself — what it
    /// returns, reinforces and logs — is unchanged. An engine with no candidate stage (lexical, semantic,
    /// curated) ignores it (<c>docs/DECISIONS.md</c> D200).</summary>
    public bool Explain { get; init; }

    /// <summary>Which entries to admit by valid time. The default, <see cref="MemoryValidityReading.Current"/>,
    /// leaves out what has ended or not yet started and admits every untimed entry, so a store that holds no
    /// valid time recalls as it always did. Applied after the candidate set is gathered, so a scope dense in
    /// ended entries can return fewer than <see cref="Limit"/> — never an ended one.
    /// <para>An engine that holds no valid time ignores it: its entries are untimed, valid at every instant.</para></summary>
    public MemoryValidityReading Validity { get; init; }
}

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
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>When the fact held in the world and what superseded it, or null for an untimed entry and for an
    /// engine that holds no valid time. A recall under <see cref="MemoryValidityReading.History"/> or an
    /// earlier reading returns entries that are no longer current; this is how a caller, and
    /// <see cref="MemoryComposition"/>, tell.</summary>
    public MemoryValidity? Validity { get; init; }

    /// <summary>An opaque token for this entry's state, or null where the engine holds none. Pass it back as
    /// <see cref="MemoryEnding.ExpectedRevision"/> so an end applies only to the entry as it was read; compare it
    /// for equality only.</summary>
    public string? Revision { get; init; }

    /// <summary>The relations this entry takes part in whose other end is in the same reading, or null where there are
    /// none or the engine holds no relations. A relation whose other end is not returned, or has ended, is not reported.
    /// Each is the writer's attributed assertion, never an established disagreement.</summary>
    public IReadOnlyList<MemoryItemRelation>? Relations { get; init; }
}

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

    /// <summary>Every candidate the recall considered, and what became of each, in the order the limit was
    /// applied to and the floored last; attached only when <see cref="MemoryQuery.Explain"/> asked.
    /// <para><c>null</c> = not asked, or no engine that answered has a candidate stage; an empty list = asked,
    /// and nothing was a candidate. An entry absent from a non-null list was <b>never a candidate</b>, which
    /// is what tells a retrieval miss from <see cref="MemoryCandidateOutcome.CutByLimit"/>. A blend carries
    /// its members' lists, each entry naming its member, and re-reports what its own cut removed.</para></summary>
    public IReadOnlyList<MemoryRecallCandidate>? Candidates { get; init; }
}

/// <summary>What a remember did: the entry's address and the tiers that took the write.
/// <para><see cref="Ran"/> reports CONTRIBUTION on every flag — a tier of that kind stored the entry, and
/// <see cref="MemorySources.Similarity"/> means THIS write's vector was indexed, so a similarity search (a
/// semantic seed source, or a later write's links) can find it. A vector index that failed or had no backend is
/// absent, which is how a write stored without its vector is told apart from one stored with it; a storage tier
/// that fails THROWS rather than going missing. Nothing else is reported: the graph engine's annotation, subject
/// index, similarity links and salience are best-effort and only logged.</para></summary>
/// <param name="Reference">The entry's address — what expansion and linking take.</param>
/// <param name="Ran">The tiers that took the write. Test one with <see cref="Enum.HasFlag"/>: members may be
/// added.</param>
public sealed record MemoryWriteResult(MemoryRef Reference, MemorySources Ran);
