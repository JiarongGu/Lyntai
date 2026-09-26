using System.Globalization;
using Lyntai.Inference;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Memory.Seeding;
using Lyntai.Memory.Verification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>
/// Memory that forgets and relinks: entries decay unless reused, connect to whatever was recalled beside
/// them, and open as a cheap index of headlines that expand on demand.
/// <para>Decay is measured in what has HAPPENED in this memory, not in elapsed time — see
/// <see cref="IMemoryAgePolicy"/>. A rarely-used engine therefore keeps everything while a busy one lets old
/// material fall behind, which is the property wall-clock time gets backwards.</para>
/// <para>Recall runs seed → spread → score → filter → touch. It is the one recall path in this library that
/// WRITES — reinforcement and co-activation are recorded for what it returned — so both writes are
/// best-effort: a failure logs and the hits still come back, and a read-only database therefore degrades to
/// "no learning" rather than to "no memory".</para>
/// </summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="store">Node and edge storage.</param>
/// <param name="options">Retrieval knobs; null takes the defaults.</param>
/// <param name="seams">The policies, similarity index and backends this engine is built from; null, or any
/// member left null, takes the engine's own default (see <see cref="GraphMemorySeams"/>).</param>
/// <param name="logger">Optional; reinforcement and recall failures are logged rather than thrown.</param>
/// <exception cref="ArgumentException">Two seed sources share a name, more than one age policy is
/// Accumulating, retention is supplied beside an already-modulated curve, or
/// <see cref="NeutralSaliencePolicy"/> is combined with another salience policy.</exception>
public sealed class GraphMemoryEngine(
    string name,
    IMemoryGraphStore store,
    GraphMemoryOptions? options = null,
    GraphMemorySeams? seams = null,
    ILogger<GraphMemoryEngine>? logger = null)
    : IMemoryEngine, IExpandableMemory, ILinkableMemory, IForgettableMemory, IPrunableMemory, IReindexableMemory
{
    private readonly GraphMemoryOptions _options = options ?? new GraphMemoryOptions();
    private readonly IMemoryRetrievabilityPolicy _policy =
        ValidatedRetrievability(Modulate(seams?.Retrievability ?? new DsrRetrievability(),
            seams?.RetentionPolicies, seams?.RetentionComposition));
    private readonly MemoryAgeResolver _age = new(seams?.AgePolicies, seams?.AgeComposition);
    private readonly ILogger _logger = logger ?? NullLogger<GraphMemoryEngine>.Instance;
    private readonly GraphVectorProjection _vectors = new(name, seams?.Providers, seams?.Vectors, seams?.Routing,
        logger ?? (ILogger)NullLogger<GraphMemoryEngine>.Instance);
    private readonly IReadOnlyList<IMemorySaliencePolicy> _saliencePolicies =
        NormalizeSaliencePolicies(seams?.SaliencePolicies);
    private readonly IMemorySalienceCompositionPolicy _salienceComposition =
        seams?.SalienceComposition ?? new MaximalSalienceCompositionPolicy();
    private readonly IMemoryRankingPolicy _ranking = seams?.Ranking ?? new ReciprocalRankFusionPolicy();
    private readonly IReadOnlyDictionary<string, IMemoryRankingPolicy> _namedRanking =
        NormalizeNamedRanking(seams?.NamedRankingPolicies);
    private readonly Func<DateTimeOffset> _clock = seams?.Clock ?? (() => DateTimeOffset.UtcNow);
    // held by the removal verbs and by each re-embed WRITE step, so a pass never writes a vector back for an entry a
    // forget or prune removed — the content would outlive a consent withdrawal in the index
    private readonly SemaphoreSlim _removals = new(1, 1);
    private readonly IReadOnlyList<IMemorySeedSource> _seedSources = NormalizeSeedSources(seams?.SeedSources);
    private readonly IMemoryAnnotationPolicy? _annotation = seams?.Annotation;
    private readonly IMemoryVerificationPolicy? _verification = seams?.Verification;

    /// <summary>Copies into a fresh, ORDINAL-compared dictionary regardless of what comparer the caller's own
    /// dictionary used — the same comparison <see cref="MemoryEngineFactory"/> uses for engine names, so a
    /// query's <see cref="MemoryQuery.RankingPolicyName"/> is resolved the same way whichever "resolve by
    /// name" seam it names.</summary>
    private static IReadOnlyDictionary<string, IMemoryRankingPolicy> NormalizeNamedRanking(
        IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies) =>
        namedRankingPolicies is null || namedRankingPolicies.Count == 0
            ? EmptyNamedRanking
            : new Dictionary<string, IMemoryRankingPolicy>(namedRankingPolicies, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IMemoryRankingPolicy> EmptyNamedRanking =
        new Dictionary<string, IMemoryRankingPolicy>(StringComparer.Ordinal);

    /// <summary>
    /// Wraps the curve in the engine's own retention modulation — the ENGINE composing a plural domain it
    /// owns, exactly as it does for age and salience.
    ///
    /// <para>Retention arrives as its OWN collection, never pre-wrapped inside <c>retrievability</c>: a plural
    /// domain (<c>docs/DECISIONS.md</c> D48) reaching the engine through another domain's constructor applied
    /// on a DI-built engine and silently not on a hand-built one.</para>
    ///
    /// <para><b>No policies means no wrapper</b> — the wrapper would be exactly <c>inner</c>, but skipping it
    /// keeps that a property of construction rather than of the decorator.</para>
    ///
    /// <para><b><see cref="ModulatedRetrievability"/> stays PUBLIC and composing one yourself stays
    /// supported</b> — it implements a public seam, and a consumer with their own curve, or one not using
    /// this engine at all, has a legitimate reason to build one. What is refused is the single combination
    /// that cannot be intended: an already-modulated curve PLUS retention policies, which would apply
    /// modulation twice and multiply stability twice over. The entry would then outlive what any retention
    /// policy declared, breaking <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/>'s superset
    /// guarantee — and that cutoff's only consumer DELETES. Reported at WIRING time (<b>D85</b>) rather than
    /// silently preferring one, because either silent choice is a stability figure nobody asked for.</para>
    /// </summary>
    /// <exception cref="ArgumentException">An already-modulated curve is supplied alongside retention
    /// policies.</exception>
    private static IMemoryRetrievabilityPolicy Modulate(IMemoryRetrievabilityPolicy inner,
        IEnumerable<IMemoryRetentionPolicy>? retentionPolicies,
        IMemoryRetentionCompositionPolicy? composition)
    {
        var list = retentionPolicies?.ToList() ?? [];
        if (list.Count == 0 && composition is null) return inner;

        if (inner is ModulatedRetrievability)
            throw new ArgumentException(
                $"'{nameof(GraphMemorySeams.Retrievability)}' is already a {nameof(ModulatedRetrievability)} "
                + $"and '{nameof(GraphMemorySeams.RetentionPolicies)}' was also supplied, which would apply "
                + "retention TWICE and multiply stability twice over. Pass the inner curve with the policies, or "
                + "the wrapped curve alone.", nameof(retentionPolicies));

        return new ModulatedRetrievability(inner, list, composition);
    }

    /// <summary>Null or empty takes a single <see cref="StructuralSaliencePolicy"/> — the engine's default.
    /// <para><b>So an empty collection does NOT disable salience</b>, which is the opposite of what
    /// "register nothing" suggests. The supported way off is to register
    /// <see cref="NeutralSaliencePolicy"/>, which judges nothing.</para>
    /// <para><b>Validates provenance across whatever is actually REGISTERED.</b> Salience
    /// is the one PLURAL seam whose provenance bits can genuinely collide — several policies coexist, so a
    /// hand-listed test array cannot see a third one a consumer adds that happens to land on an already-
    /// occupied bit. <see cref="ValidatedRetrievability"/> does the same for the single, singular retrievability
    /// seam.</para></summary>
    private static IReadOnlyList<IMemorySaliencePolicy> NormalizeSaliencePolicies(
        IEnumerable<IMemorySaliencePolicy>? saliencePolicies)
    {
        var list = saliencePolicies?.ToList() ?? [];
        if (list.Count == 0) return [new StructuralSaliencePolicy()];
        // checked BEFORE provenance: Neutral shares Structural's bit, so the collision would name the bit rather
        // than the real mistake — an off switch combined with a policy that is on
        if (list.Count > 1 && list.Any(p => p is NeutralSaliencePolicy))
            throw new ArgumentException(
                $"{nameof(NeutralSaliencePolicy)} turns salience OFF and cannot be combined with " +
                $"{string.Join(", ", list.Where(p => p is not NeutralSaliencePolicy).Select(p => p.GetType().Name))}. " +
                "AddMemoryEngine seeds the default policy unless one is already registered, so register " +
                $"{nameof(NeutralSaliencePolicy)} BEFORE AddLyntai, or remove the others.",
                nameof(saliencePolicies));
        MemoryProvenance.ValidateProvenanceBits(
            [.. list.Select(a => (long)a.Provenance)], i => list[i].GetType().Name);
        return list;
    }

    /// <summary>Null or empty takes the two channels this engine has always run — the unconditional store
    /// read and the handle lookup, on by default at <c>SubjectSeedOptions.K</c>'s own default of 5.
    /// <para><b>So an empty collection does NOT disable seeding</b>, the same rule
    /// <see cref="NormalizeSaliencePolicies"/> states for its own domain. The supported way to narrow is to
    /// pass exactly the sources wanted; the supported way to switch one OFF is its own options record.</para>
    /// <para><b>Two sources under one <see cref="IMemorySeedSource.Name"/> throws.</b>
    /// <see cref="MemorySeedRanks.TryGet"/> keys by name and reports only the first, while rank fusion sums a
    /// term for EVERY entry — so the second is evidence nothing can read that still moves the score. Reported
    /// at wiring time (<b>D85</b>) rather than silently de-duplicated, because either silent choice is a
    /// ranking nobody asked for.</para></summary>
    /// <exception cref="ArgumentException">Two sources share a name.</exception>
    private static IReadOnlyList<IMemorySeedSource> NormalizeSeedSources(
        IEnumerable<IMemorySeedSource>? seedSources)
    {
        var list = seedSources?.ToList() ?? [];
        if (list.Count == 0) return [new LexicalSeedSource(), new SubjectSeedSource()];

        var duplicate = list
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"{duplicate.Count()} seed sources are named '{duplicate.Key}' ({string.Join(", ", duplicate
                    .Select(s => s.GetType().Name))}); a name identifies one retrieval channel. Rank fusion " +
                "sums a term per matched source, so two under one name would count the same evidence twice, " +
                "and MemorySeedRanks reports only the first.", nameof(seedSources));
        return list;
    }

    /// <summary>Whether a channel of this ROLE might be registered — how the two wiring diagnostics below
    /// ask their question.
    /// <para><b>It asks <see cref="IMemorySeedSource.Kind"/>, never the NAME.</b> Matching a name meant a
    /// consumer's BYO semantic channel called anything else produced a FALSE finding on wiring that was
    /// already correct — and under <see cref="MemoryEngineBuilder.StrictWiring"/> that threw at startup.
    /// A rename of a shipped source silently MUTED the finding instead. Both are gone: a channel states its
    /// role and is counted whatever it is called.</para>
    /// <para><b>An UNDECLARED channel makes this abstain</b> — <see cref="MemorySeedKind.Custom"/> is the
    /// interface default, so a source written before the property existed may BE the channel a diagnostic
    /// would report missing, and this engine cannot tell. Returning true keeps the caller quiet, which is
    /// the direction <see cref="MemoryWiring"/>'s own class doc demands: a finding that is usually wrong is
    /// worse than no finding.</para></summary>
    private bool MightSeed(MemorySeedKind kind) =>
        _seedSources.Any(s => s.Kind == kind || s.Kind == MemorySeedKind.Custom);

    /// <summary>Validates a single retrievability policy's own declared provenance bit — real (never
    /// <c>None</c>) and single, the same two facts <see cref="NormalizeSaliencePolicies"/> checks across several
    /// salience policies, applied here to the one policy this seam ever has.</summary>
    private static IMemoryRetrievabilityPolicy ValidatedRetrievability(IMemoryRetrievabilityPolicy policy)
    {
        MemoryProvenance.ValidateProvenanceBits(
            [(long)policy.Provenance], _ => policy.GetType().Name);
        return policy;
    }

    private bool Enriches => _vectors.Enriches;

    /// <summary>What a non-empty recall reports: the graph, plus the WRITE-side tiers as configuration.</summary>
    private MemorySources RecallSources =>
        MemorySources.Graph
        | (Enriches ? MemorySources.Similarity : MemorySources.None)
        | (_annotation is not null ? MemorySources.Annotation : MemorySources.None);

    /// <summary>This engine embeds every write and no recall reads those vectors — a vector backend and a vector
    /// store are wired, so novelty and similarity linking run on the WRITE path, while no
    /// <see cref="SemanticSeedSource"/> is registered so the READ path consults none of it. Still the shipped
    /// default: the vector channel is opt-in (<c>AddMemorySemanticSeeds</c>).
    /// <para>Internal, and read only by <see cref="MemoryWiring"/>: it is a wiring diagnostic, not something
    /// a consumer branches on. Exposed as a property rather than reflected over, so a rename is a compile
    /// error instead of a sweep that throws the next time somebody runs it.</para>
    /// <para>It asks a channel's ROLE (<see cref="IMemorySeedSource.Kind"/>), never its name, so a BYO
    /// vector channel called anything at all answers it — see <see cref="MightSeed"/> for why an
    /// UNDECLARED channel silences this rather than tripping it.</para></summary>
    internal bool EmbedsWithoutSeeding => Enriches && !MightSeed(MemorySeedKind.Semantic);

    /// <summary>This engine records subject handles and no recall can reach one — an annotator is wired, so
    /// every write pays a model call to say what the fact is ABOUT, while no <see cref="SubjectSeedSource"/>
    /// is registered so the only readers left are the write path's own linking and the annotator's reuse
    /// list.
    /// <para><b>It asks about REGISTRATION, not about the source's own knob.</b>
    /// <see cref="SubjectSeedOptions.K"/> of 0 is a documented off-switch a deployment chose deliberately on
    /// the source it registered, so this reports the channel being ABSENT — the gap an adopter actually hit
    /// — and says nothing about one that is present and turned down.</para>
    /// <para>Internal and read only by <see cref="MemoryWiring"/>, exactly like
    /// <see cref="EmbedsWithoutSeeding"/> — the same defect on the other index.</para></summary>
    internal bool RecordsSubjectsWithoutSeeding => _annotation is not null && !MightSeed(MemorySeedKind.Subject);

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    /// <inheritdoc />
    public async Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        // ONE embed, ONE similarity search, shared between salience judgement (which needs the comparison before
        // the node has an id) and EnrichAsync below (linking + indexing, which needs it after). Without a
        // vector store there is nothing to compare against, so nothing is judged or linked — the honest
        // answer, not a degraded one. Before the annotation, so a write that lost its vector can skip it.
        var search = await _vectors.SearchAsync(write, _options.SimilarityK, ct).ConfigureAwait(false);
        var skipAnnotation = _options.SkipAnnotationWithoutVector
            && search is null && _options.SimilarityK > 0 && _vectors.Owed;

        // BEFORE the upsert, because a suggested grade has to reach the row being written — grade is not
        // something a later update can fix up without a second write and a window where the fact is stored
        // at the wrong one.
        var (annotated, answered) = skipAnnotation
            ? (MemoryAnnotation.None, false)
            : await AnnotateAsync(write, ct).ConfigureAwait(false);

        // An explicit grade always wins: a model may advise what matters, never overrule the application.
        // `stated` carries that same rule ACROSS TIME — only a caller-named grade may overwrite what is
        // already stored, so re-remembering a fact without restating its grade no longer demotes it and an
        // annotator's suggestion cannot overrule a decision the application made on an earlier write. The
        // resolved value below is still what a genuine FIRST write is stored with, suggestion included;
        // there is simply nothing to inherit from then.
        var stated = write.Grade != MemoryGrade.Inherit;
        var grade = stated ? write.Grade : annotated.Grade ?? MemoryGrade.Associative;

        // authoritative material is NEVER passed through headline derivation — a truncated exact fact is
        // confidently wrong, which is worse than having no memory at all
        var headline = write.Headline
            ?? (grade == MemoryGrade.Authoritative
                ? write.Content
                : MemoryHeadline.Derive(write.Content, _options.HeadlineChars));

        var tick = _age.Advance(write, Name);

        var (signals, salienceProvenance) = CollectSignals(write, Probe(write, search, _options.MinSimilarity));

        var id = await store.UpsertAsync(
            new GraphNodeWrite(Name, write.TaskKey, write.Scope, headline, write.Content, grade,
                _policy.InitialStability * Ordinary(tick.Encoding), Ordinary(tick.Position),
                write.Metadata, signals,
                ProvenanceRetrievability: (long)_policy.Provenance,
                ProvenanceSalience: salienceProvenance,
                GradeStated: stated, HeadlineStated: write.Headline is not null),
            ct).ConfigureAwait(false);

        var indexed = await EnrichAsync(id, write, search, ct).ConfigureAwait(false);
        var recorded = await LinkBySubjectAsync(id, write, annotated, ct).ConfigureAwait(false);
        return new MemoryWriteResult(new MemoryRef(Name, id.ToString(CultureInfo.InvariantCulture)),
            MemorySources.Graph
            | (indexed ? MemorySources.Similarity : MemorySources.None)
            | (answered && recorded ? MemorySources.Annotation : MemorySources.None));
    }

    /// <summary>Ask the annotator what this fact is about, showing it recent entries so a pronoun is
    /// resolvable. BEST-EFFORT: no annotator, a failing one, or one answering
    /// <see cref="MemoryAnnotation.Answered"/> false yields <see cref="MemoryAnnotation.None"/> and the write
    /// proceeds exactly as it would have — the model-free floor is not negotiable. <c>Answered</c> is false for
    /// all three, which is what <see cref="MemorySources.Annotation"/> reports.</summary>
    private async Task<(MemoryAnnotation Annotation, bool Answered)> AnnotateAsync(MemoryWrite write, CancellationToken ct)
    {
        if (_annotation is null) return (MemoryAnnotation.None, false);
        try
        {
            // Recent entries, newest first — the no-query seed path, which is enumeration rather than
            // retrieval and therefore does not reinforce anything it returns.
            var recent = _options.AnnotationContext <= 0
                ? []
                : (await store.SeedAsync(Name, write.TaskKey, write.Scope, null, _options.AnnotationContext, ct)
                    .ConfigureAwait(false))
                    .Select(n => n.Content)
                    .ToList();

            // The handles already in use, so the annotator REUSES one instead of inventing a new name for the
            // same entity. Measured to matter: without it, real models answered three facts about one person
            // with three different-but-defensible subjects and nothing linked.
            var known = _options.AnnotationKnownSubjects <= 0
                ? []
                : await store.KnownSubjectsAsync(Name, write.TaskKey, write.Scope,
                    _options.AnnotationKnownSubjects, ct).ConfigureAwait(false);

            var annotation = await _annotation.AnnotateAsync(new MemoryAnnotationRequest(write, recent, known), ct)
                .ConfigureAwait(false);
            // what an unanswered annotation carries is not an answer, so none of it is recorded
            return annotation is { Answered: true } ? (annotation, true) : (MemoryAnnotation.None, false);
        }
        // Only the CALLER's cancellation propagates, as on VerifyAsync and for the same reason: an
        // annotator's own timeout arrives as a TaskCanceledException — which IS an
        // OperationCanceledException — so a bare rethrow made this BEST-EFFORT seam fail the WRITE on the
        // likeliest failure a model-backed policy has, losing the fact rather than its subjects.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "memory annotation failed for {Engine}; storing without it", Name);
            return (MemoryAnnotation.None, false);
        }
    }

    /// <summary>
    /// Record what this entry is about, then connect it to the entries already recorded under the same
    /// subjects.
    ///
    /// <para><b>Why a stored index rather than a search.</b> Searching for the subject was tried first and
    /// cannot reach the case that matters: it needs SOME entry to name the subject in its own text, and
    /// three facts about one owner ("the spouse is Alice", "the deploy key is in the vault", "the client is
    /// northern logistics") are all about *me* while none of them contains "me". That is exactly the
    /// corpus's attribute cluster and exactly where the measured no-graph floor comes from, so a
    /// search-based version would have looked right in a unit test and moved no measurement at all — pinned
    /// by <c>MemorySubjectLinkingTests.A_shared_subject_that_no_entry_names_links_nothing</c>.</para>
    ///
    /// <para>Recorded BEFORE the lookup, and the node's own id excluded from the results: recording second
    /// would make the first fact under a subject link to nothing while the second linked to it, so the edge
    /// count would depend on write ORDER. Symmetric edges, so a cluster is traversable from either end, and
    /// self-links are skipped — a node propping up its own Degree and Strength would prop up its own
    /// retrievability forever.</para>
    /// </summary>
    private async Task<bool> LinkBySubjectAsync(long id, MemoryWrite write, MemoryAnnotation annotated,
        CancellationToken ct)
    {
        // canonical first: the store records "Alice" and " alice " as one handle, so looking both up would link
        // the same pair twice — and duplicate links ADD weight
        var subjects = MemorySubject.Canonicalize(annotated.Subjects);
        if (subjects.Count == 0) return true;
        try
        {
            await store.RecordSubjectsAsync(Name, id, subjects, ct).ConfigureAwait(false);
            if (_options.AnnotationLinkK <= 0) return true;

            // one batch for the write: the edges come from one event, so one position stamps them all
            var edges = new List<GraphEdgeWrite>();
            foreach (var subject in subjects)
            {
                // +1 because this node is now recorded under the subject too and is filtered out below —
                // without it a K of 1 would find only itself and link nothing
                var found = await store.NodesBySubjectAsync(Name, write.TaskKey, write.Scope, subject,
                    _options.AnnotationLinkK + 1, ct).ConfigureAwait(false);

                foreach (var other in found)
                    if (other != id) edges.Add(new GraphEdgeWrite(id, other, "subject", 1, Symmetric: true));
            }
            if (edges.Count > 0) await store.LinkManyAsync(Name, edges, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "subject linking failed for {Engine}; the entry is stored unlinked", Name);
            return false;
        }
    }

    /// <summary>What this write can be judged against, from the write's one similarity search —
    /// how unlike its nearest neighbours it is, and how many neighbours there were to compare with, once
    /// this write's own PRIOR vector (identical content, an earlier write) is excluded. Without that
    /// exclusion a re-remember would find itself at cosine ≈ 1, be judged as minimally novel, and (via
    /// <see cref="IMemorySaliencePolicy.Signals"/> reporting the neutral value) silently erase any salience the FIRST write
    /// earned — the inverse of intent, since a re-remembered entry is the one being reinforced.
    /// <para><b>No search, or nothing left after self-exclusion, reports novelty 0 with zero comparables —
    /// not 1.</b> Nothing to compare against is no information, not "unlike everything," which is the
    /// precise failure <see cref="SalienceOptions.MinimumComparables"/> exists to prevent, and it would
    /// arrive by the one route that guard cannot see.</para>
    /// <para>The THIRD member is the same neighbour list counted against a floor: how many actually
    /// resemble this write, where the second is merely how many there were.</para></summary>
    private static (double Novelty, int Comparables, int Similar) Probe(MemoryWrite write,
        (float[] Vector, IReadOnlyList<VectorMatch> Near)? search, double minSimilarity)
    {
        if (search is null) return (0, 0, 0);
        var survivors = search.Value.Near
            .Where(m => !string.Equals(m.Payload, write.Content, StringComparison.Ordinal))
            .ToList();
        return survivors.Count == 0
            ? (0, 0, 0)
            : (Math.Clamp(1 - survivors[0].Score, 0, 1), survivors.Count,
               survivors.Count(m => m.Score >= minSimilarity));
    }

    /// <summary>Collect signals from every registered salience policy, treating each one's own failure as "no
    /// signals from THAT policy" — a broken policy degrades to unmodulated decay, never to a lost write, and
    /// never takes a healthy sibling down with it. The composition then decides how the resulting
    /// bags combine into the ONE bag a write stores — the identity when only one policy is
    /// registered.
    /// <para><b>Provenance records who PRODUCED a signal, not who merely ran.</b> A policy that declined
    /// (too few comparables) or failed contributes <see cref="MemorySignals.Empty"/> to <c>results</c> above
    /// and its own bit is excluded here — crediting it would claim this entry's signals reflect a judgement
    /// that never actually happened (design doc §5.7).</para></summary>
    private (MemorySignals Signals, long Provenance) CollectSignals(MemoryWrite write,
        (double Novelty, int Comparables, int Similar) probe)
    {
        var context = new SalienceContext(Name, probe.Novelty, probe.Comparables, probe.Similar);
        var results = new List<MemorySignals>(_saliencePolicies.Count);
        var contributions = new List<long>(_saliencePolicies.Count);
        foreach (var policy in _saliencePolicies)
        {
            MemorySignals signals;
            try
            {
                signals = policy.Signals(write, context);
            }
            // No cancellation clause: `Signals` is SYNCHRONOUS and takes no token, so nothing here can be
            // relaying a caller's cancel — and this method has no `ct` to test one against. A policy that
            // throws one anyway is a broken policy, which is exactly what the handler below is for.
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "salience policy failed for {Engine}; storing without signals from it",
                    Name);
                signals = MemorySignals.Empty;
            }
            results.Add(signals);
            if (signals.Count > 0) contributions.Add((long)policy.Provenance);
        }
        return (_salienceComposition.Signals(results), MemoryProvenance.Pack(contributions));
    }

    /// <summary>Index a newly stored entry's own vector, then link it to its nearest existing neighbours, from
    /// the similarity search <see cref="RememberAsync"/> already ran to judge it — no second embed or
    /// search. Returns whether the vector was indexed.
    /// <para>BEST-EFFORT, deliberately: enrichment sits on top of a model-free floor, so a failing vector backend
    /// or vector store must not fail the write. The entry is already stored by the time this runs — it
    /// simply has fewer connections than it might have had (none when the shared search failed), and no
    /// vector only when the embed or the upsert itself failed. The index and the links are separate blocks,
    /// so a failed link costs links and never the vector.</para></summary>
    private async Task<bool> EnrichAsync(long id, MemoryWrite write,
        (float[] Vector, IReadOnlyList<VectorMatch> Near)? search, CancellationToken ct)
    {
        if (search is null) return false;
        var (vector, near) = search.Value;
        // the index first, in its own block: a failed link must cost links, never the vector (D175)
        var indexed = await _vectors.IndexAsync(id, write, vector, ct).ConfigureAwait(false);

        try
        {
            var edges = new List<GraphEdgeWrite>(near.Count);
            foreach (var match in near)
            {
                // this write's own PRIOR vector, from an earlier remember of identical content — never a
                // neighbour of itself (the search asked for one extra slot for it)
                if (string.Equals(match.Payload, write.Content, StringComparison.Ordinal)) continue;
                if (match.Score < _options.MinSimilarity) continue;
                if (!long.TryParse(match.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var other))
                    continue;
                // BOTH checks, not either: the content test above is sound for the in-process vector store,
                // but IVectorStore is a BYO seam and a backend that truncates, trims or normalizes the payload
                // it echoes back would slip a self-match past it. A self-edge inflates this node's own Degree
                // and Strength, and both feed retrievability — so the entry would prop itself up forever.
                // The store rejects one too; catching it here also skips a pointless round trip.
                if (other == id) continue;
                edges.Add(new GraphEdgeWrite(id, other, "similar", 1, Symmetric: true));
            }
            if (edges.Count > 0) await store.LinkManyAsync(Name, edges, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "similarity linking failed for {Engine}; the entry is stored with fewer links", Name);
        }

        return indexed;
    }

    /// <inheritdoc />
    /// <remarks><b>A bad <see cref="MemoryQuery.RankingPolicyName"/> is NOT covered by this method's own
    /// fail-open promise.</b> The name is resolved before <see cref="GatherAsync"/> ever runs, and a
    /// <see cref="KeyNotFoundException"/> from an unknown name propagates to the caller like
    /// <see cref="OperationCanceledException"/> does — a caller mistake, not a storage outage, so it must not
    /// be swallowed into an empty result the caller would read as "nothing matched".</remarks>
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        var ranking = ResolveRanking(query.RankingPolicyName);

        var limit = query.Limit ?? _options.DefaultLimit;
        if (limit <= 0) return MemoryRecall.Empty;

        var found = await TryGatherAsync(query, limit, ct).ConfigureAwait(false);
        if (found is null) return MemoryRecall.Empty;

        var candidates = found
            .Select(f => new MemoryCandidate(f.Node, Retrievability(f.Node), f.Hop) { Ranks = f.Ranks })
            .ToList();

        var ranked = ranking.Rank(candidates, new MemoryRankingContext(limit, Name));
        if (ranked.Count == 0 && candidates.Count == 0) return MemoryRecall.Empty;

        var (reserved, ordinary) = ReserveAuthoritative(candidates, ranked, limit);

        // THE CORRECTNESS SIGNAL, APPLIED BEFORE THE CUT — and the position is the whole value of it. A
        // verifier consulted AFTER `Take(limit)` can only observe what the ranking lost below the limit;
        // consulted here it can undo that loss.
        //
        // Bounded by `VerificationDepth`: a judge sees the top slice of the ranking rather than every
        // candidate, because showing a model every candidate's headline per recall is not a shippable cost.
        // Depth is the knob that trades judgement cost against how far down an answer may be rescued from.
        var depth = Math.Max(limit,
            _options.VerificationDepth ?? Saturating(limit, GraphMemoryOptions.DefaultVerificationDepthFactor));
        var verdict = await VerifyAsync(query.Query ?? string.Empty,
            [.. ordinary.Take(depth)], ct).ConfigureAwait(false);

        ordinary = MemoryVerdicts.Apply(ordinary, verdict, _options.VerdictCombination);

        // Ordinary material leads, in the policy's order, then the reserved exact facts — the same
        // "matches lead, exact facts take the low end" shape the store's own merge uses, so the two agree.
        var scored = ordinary
            .Take(Math.Max(0, limit - reserved.Count))
            .Concat(reserved)
            .ToList();
        if (scored.Count == 0) return MemoryRecall.Empty;

        await LogAndReinforceAsync(scored, verdict, ct).ConfigureAwait(false);

        // A judgement never removes an answer unless a consumer asked for that separately: a mistaken
        // verdict should cost a little learning, not a lost result. Authoritative material is exempt
        // whatever the verdict — objective (1) does not defer to a judge (D56).
        if (verdict.Judged && _options.VerificationFilters)
            scored = [.. scored.Where(x =>
                x.Candidate.Node.Grade == MemoryGrade.Authoritative
                || verdict.RelevantIds.Contains(x.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture)))];

        if (scored.Count == 0) return MemoryRecall.Empty;

        var items = ToItems(scored, query);

        // The ABSTENTION signal. `Judged` is the only absolute quality statement available here — ranking is
        // relative by construction, so a page of uniformly-irrelevant material ranks perfectly well among
        // itself. Null when nothing judged, so the shipped default (no verifier) never reports `false` and a
        // consumer abstaining on `false` does not abstain on everything.
        var answered = verdict.Judged ? verdict.RelevantIds.Count > 0 : (bool?)null;

        return new MemoryRecall(items, RecallSources, answered);
    }

    /// <summary>The candidate set, or <c>null</c> where <see cref="RecallAsync"/>'s fail-open promise fired.
    /// <para>The CHOKE POINT of the fail-open chain: <see cref="GatherAsync"/> runs every registered
    /// <c>IMemorySeedSource</c>, so a BYO vector backend or store timing out anywhere below surfaces here. Nothing
    /// between this and the seed source catches, so a bare rethrow would break the promise whatever the
    /// sources did.</para></summary>
    private async Task<List<GatheredCandidate>?> TryGatherAsync(MemoryQuery query, int limit, CancellationToken ct)
    {
        try
        {
            return await GatherAsync(query, limit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "graph recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return null;
        }
    }

    /// <summary>Split the ranking in two: the authoritative material this engine RESERVES slots for, and the
    /// ordinary material competing for what is left. The caller cuts the ordinary half and concatenates the
    /// reserve whole.</summary>
    private (List<RankedMemory> Reserved, List<RankedMemory> Ordinary) ReserveAuthoritative(
        IReadOnlyList<MemoryCandidate> candidates, IReadOnlyList<RankedMemory> ranked, int limit)
    {
        // BURIED, NOT CUT — and trust outranks burial. An entry is hidden because something OUTRANKS it,
        // never because its own retrievability crossed a line. The policy owns the floor; the ENGINE owns
        // this exemption, because "authoritative material is never buried" must hold whatever policy is
        // installed — including a third-party one that never heard of grades.
        //
        // THE PRECISE SHAPE OF THAT PROMISE: re-admission is keyed by `Node.Id`, so it holds against a policy
        // that DROPS an authoritative candidate — even one dropping everything — but NOT against one
        // SUBSTITUTING a fabricated `RankedMemory` under that same id, which would leave the id present and
        // skip re-admission. "May floor, never invent" is what this loop enforces against a FORGETFUL policy,
        // not a fabricating one.
        //
        // RESERVED SLOTS, NOT APPENDED: appended material is cut by the caller's Take, so an exact fact the
        // query did not match — Relevance 0, therefore ranked near the bottom — is lost outright. The reserve
        // covers EVERY authoritative candidate, not only ones a policy dropped (a ranking policy does not
        // omit them, it RANKS them), and it may DISPLACE ordinary material, which is what marking a fact
        // authoritative MEANS; `AuthoritativeReserve` bounds how many slots exact facts take.
        //
        // NOT COSMETIC: this order feeds ReinforceAsync's own `nodes.Take(CoActivationCap)`, so which pairs
        // get a symmetric co-activation edge PERMANENTLY written to the store depends on it too, not just on
        // what a reader sees in the returned list.
        var rankedById = ranked.ToDictionary(r => r.Candidate.Node.Id);
        var authoritative = candidates
            .Where(c => c.Node.Grade == MemoryGrade.Authoritative)
            // keep the policy's own scoring where it produced one, so an exact fact that ranked on merit is
            // not silently re-scored to zero by being reserved
            .Select(c => rankedById.TryGetValue(c.Node.Id, out var r) ? r : new RankedMemory(c, 0))
            .ToList();

        // THE LIMIT BOUNDS THE RESERVE, and that outer Math.Min is not belt-and-braces — the two numbers live
        // on different scopes and nothing else reconciles them. `AuthoritativeReserve` is configured per
        // ENGINE; `limit` arrives per QUERY. Without the cap a reserve larger than a tighter per-call limit
        // returns more items than the caller asked for — the caller's `Take` floors at zero while the reserve
        // is concatenated whole — and "within the caller's Limit" is a promise. The `?? limit` default
        // already carried this cap; only an EXPLICIT value escaped it.
        var reserve = Math.Min(limit,
            Math.Min(authoritative.Count, _options.AuthoritativeReserve ?? limit));
        var reserved = authoritative.Take(reserve).ToList();
        var reservedIds = reserved.Select(r => r.Candidate.Node.Id).ToHashSet();

        return (reserved, ranked.Where(r => !reservedIds.Contains(r.Candidate.Node.Id)).ToList());
    }

    /// <summary>Record what a recall produced — reinforcing what the verdict endorsed, logging everything it
    /// returned.</summary>
    private Task LogAndReinforceAsync(IReadOnlyList<RankedMemory> scored,
        MemoryVerification verdict, CancellationToken ct)
    {
        // WHAT GETS REINFORCED vs WHAT GETS LOGGED are deliberately different sets, and that difference is
        // what makes the review log fittable at all.
        //
        // Reinforcement follows the verdict: only entries a judge said ANSWERED get their age reset and
        // stability grown, so learning follows evidence rather than the ranker's own prior.
        //
        // The LOG, though, records every entry the recall returned, verdict attached. Before this, a row
        // existed only where a touch happened — so the log could contain nothing but successes, which is
        // `docs/DECISIONS.md` D51's second and harder reason parameter fitting was called impossible. An
        // entry the judge REJECTED is the observation that was missing, and it exists only here.
        var relevantIds = verdict.RelevantIds.ToHashSet(StringComparer.Ordinal);
        bool? VerdictFor(GraphNode n) => verdict.Judged
            ? relevantIds.Contains(n.Id.ToString(CultureInfo.InvariantCulture))
            : null;   // no judgement is NOT a failure — see MemoryReviewWrite.Verified

        var returned = scored.Select(x => x.Candidate.Node).ToList();
        var reinforce = verdict.Judged
            ? returned.Where(n => relevantIds.Contains(n.Id.ToString(CultureInfo.InvariantCulture))).ToList()
            : returned;

        return ReinforceAsync(reinforce, MemoryReinforcementActs.Recall, ct, returned, VerdictFor);
    }

    /// <summary>What the caller actually receives: each surviving candidate projected onto a
    /// <see cref="MemoryItem"/>, then trimmed to the query's character budget.</summary>
    private List<MemoryItem> ToItems(IReadOnlyList<RankedMemory> scored, MemoryQuery query)
    {
        var items = scored
            .Select(x => new MemoryItem(
                new MemoryRef(Name, x.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture)),
                x.Candidate.Node.Headline, ProjectContent(x.Candidate.Node, query.Detail),
                x.Candidate.Node.Grade, x.Candidate.Node.Relevance, x.Candidate.Retrievability,
                x.Candidate.Node.Degree, x.Candidate.Node.Metadata))
            .ToList();

        // applied AFTER ranking, so the budget cuts the weakest tail rather than changing what wins
        return query.CharBudget is { } budget && budget > 0 ? MemoryBudget.Cut(items, budget) : items;
    }

    /// <summary>What of a node's content a read returns: associative content is withheld until expansion —
    /// what makes the first load cheap — unless the caller asked for <see cref="MemoryDetail.Full"/>, and
    /// authoritative content is always present, because it is never returned truncated.</summary>
    private static string? ProjectContent(GraphNode node, MemoryDetail detail) =>
        node.Grade == MemoryGrade.Authoritative || detail == MemoryDetail.Full ? node.Content : null;

    /// <inheritdoc />
    public async Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // another engine's reference names another store's node, so a matching id here would be the wrong entry
        if (!Owns(reference) ||
            !long.TryParse(reference.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return MemoryRecall.Empty;

        var node = await store.GetAsync(Name, id, ct).ConfigureAwait(false);
        if (node is null) return MemoryRecall.Empty;

        // `hops` is CLAMPED to the engine's configured ceiling rather than honoured unbounded: this is a
        // model-facing seam (MemoryTools advertises the parameter in its tool schema), so an agent asking for
        // a large number must not be able to walk the whole graph. Zero is legal and means "just this entry".
        var depth = Math.Clamp(hops, 0, _options.Hops);

        // Breadth-first, level by level, each level ordered by effective edge weight. `seen` carries the seed
        // so a symmetric edge cannot walk back to it, and so a diamond yields its far node once.
        var walked = new List<GraphNeighbour>();
        var seen = new HashSet<long> { id };
        var frontier = new List<long> { id };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = await store.NeighboursAsync(Name, node.TaskKey, frontier, _options.DefaultLimit, ct).ConfigureAwait(false);
            frontier = [];
            foreach (var neighbour in next.OrderByDescending(EffectiveEdgeWeight).ThenByDescending(n => n.Node.Id))
            {
                if (!seen.Add(neighbour.Node.Id)) continue;
                // Forgetting gets a vote in TRAVERSAL, not only in recall. Off at the shipped default, so
                // this is the identity filter unless a deployment asks for it — see the option's own doc.
                if (_options.ExpansionRetrievabilityFloor > 0
                    && Retrievability(neighbour.Node) < _options.ExpansionRetrievabilityFloor) continue;
                walked.Add(neighbour);
                frontier.Add(neighbour.Node.Id);
            }
        }

        // expanding a node reinforces it — digging in one direction is exactly what should make that
        // direction more retrievable next time, and it is the one act here a caller PAID for
        await ReinforceAsync([node], MemoryReinforcementActs.Expansion, ct).ConfigureAwait(false);

        var items = new List<MemoryItem>(walked.Count + 1)
        {
            // the expanded node carries its FULL content whatever its grade — that is what expansion IS
            new(reference, node.Headline, node.Content, node.Grade, 1, Retrievability(node), node.Degree,
                node.Metadata),
        };
        // neighbours take the recall projection's rule: the caller's stated detail decides
        items.AddRange(walked.Select(w => new MemoryItem(
            new MemoryRef(Name, w.Node.Id.ToString(CultureInfo.InvariantCulture)),
            w.Node.Headline, ProjectContent(w.Node, detail), w.Node.Grade, w.Node.Relevance,
            Retrievability(w.Node), w.Node.Degree, w.Node.Metadata)));

        // The budget bounds the NEIGHBOURS, never the entry itself — the cut keeps its first item whatever it
        // costs. Null takes the engine's configured budget, itself null (unbounded) unless a host sets one.
        var result = (charBudget ?? _options.ExpandCharBudget) is { } budget ? MemoryBudget.Cut(items, budget) : items;
        return new MemoryRecall(result, RecallSources);
    }

    /// <inheritdoc />
    public async Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default)
    {
        if (!Owns(from) || !Owns(to))
            throw new ArgumentException(
                $"Memory engine '{Name}' links only its own entries; got '{from.Engine}' and '{to.Engine}'. A " +
                "link across engines would connect whichever of this engine's nodes share those ids.");
        if (!long.TryParse(from.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ||
            !long.TryParse(to.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            throw new ArgumentException(
                $"Memory engine '{Name}' addresses nodes by numeric id; got '{from.Id}' and '{to.Id}'.");

        // REFUSED ACROSS TASKS, and refused HERE rather than ignored later. Traversal is task-scoped, so a
        // cross-task edge could be written and would then never be walked — a thing a caller can create that
        // can never work, which is the shape D83–D86 are all about and the worst of the three options. The
        // alternative, letting the walk cross, would make `taskKey` a boundary for every read but one; a
        // half-boundary is worse than none, because consumers reason about it as a whole one.
        var left = await store.GetAsync(Name, a, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"Memory engine '{Name}' has no entry '{from.Id}'.", nameof(from));
        var right = await store.GetAsync(Name, b, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"Memory engine '{Name}' has no entry '{to.Id}'.", nameof(to));

        if (!string.Equals(left.TaskKey, right.TaskKey, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Memory engine '{Name}' cannot link across tasks: '{from.Id}' is in task '{left.TaskKey}' " +
                $"and '{to.Id}' is in task '{right.TaskKey}'. A task is the isolation boundary of every " +
                "read, so an edge across one could never be traversed. Put both facts in one task, or keep " +
                "the association in your own data.", nameof(to));

        // an EXPLICIT link is a write, so it surfaces its failure — unlike the co-activation edges recall
        // records opportunistically, which are best-effort
        await store.LinkAsync(Name, a, b, kind, weight, symmetric, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Two paths, chosen by whether the store's raw accumulator is provably what
    /// <see cref="Retrievability"/> reads.</b> With every registered
    /// <see cref="IMemoryAgePolicy"/> <see cref="MemoryAgeKind.Accumulating"/> (at most one is allowed) —
    /// including the engine's shipped default — the resolved age IS
    /// <see cref="GraphNode.Age"/>, so the store's own cheap, SQL-side
    /// <see cref="IMemoryGraphStore.PruneAsync"/> stays EXACT and is what runs.</para>
    /// <para>With ANY <see cref="MemoryAgeKind.Derivable"/> policy registered, the accumulator can diverge
    /// from the resolved age — most sharply after a policy SWAP over pre-existing data, where the
    /// accumulator still carries residue built up under whichever policy governed each historical write,
    /// while a Derivable policy's own projection re-derives fresh from the swap-safe primitives every time.
    /// The store never evaluates a curve, so this method does what <see cref="RecallAsync"/> already does —
    /// fetch candidates via <see cref="IMemoryGraphStore.SeedAsync"/>, evaluate <see cref="Retrievability"/>
    /// for each — and removes precisely the ones that fail, through
    /// <see cref="IMemoryGraphStore.DeleteAsync"/>. <see cref="IMemoryGraphStore.SeedAsync"/> applies no
    /// faintness bound of its own, so a sufficiently large <c>limit</c> is a full scope scan, oldest entries
    /// included.</para>
    /// <para><b>That path is EXACT on BOTH age axes, not just one.</b> A store tracks the per-edge age
    /// primitives too (<see cref="GraphNode.StrengthAgeSample"/>), so the resolver projects the connection
    /// axis in the installed policy's own unit and no connected entry is exempt from
    /// deletion. Still deliberately more expensive than the cheap path: pruning is periodic maintenance, not
    /// a hot path.</para>
    /// </remarks>
    public async Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var removals = await RemovalsAsync(ct).ConfigureAwait(false);

        // The caller's floor, else the engine's CONFIGURED one — `GraphMemoryOptions.MinRetrievability` is
        // "the retrievability below which PruneAsync may REMOVE an entry", and design §5.7 gives it to this
        // method alone. A floor of 0 is the opt-out: retrievability is never below zero.
        var floorValue = minRetrievability ?? _options.MinRetrievability;

        if (!_age.AnyDerivable)
            return await PruneInStoreAsync(taskKey, scope, floorValue, olderThan, ct).ConfigureAwait(false);

        var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
        var candidates = await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false);

        var doomed = candidates
            .Where(n => n.Grade != MemoryGrade.Authoritative) // never eligible — retrievability fixed at 1
            .Where(n =>
                // both age axes are resolved through the installed policies (ResolvedState), so a
                // connected entry's boost is computed in the same unit as its age and the retrievability
                // criterion is exact for it — no connected-entry carve-out remains
                (floorValue > 0 && Retrievability(n) < floorValue) ||
                (createdBefore is DateTimeOffset before && n.CreatedAt < before))
            .ToList();

        if (doomed.Count == 0) return 0;

        var count = await store.DeleteAsync(Name, [.. doomed.Select(n => n.Id)], ct).ConfigureAwait(false);
        await _vectors.RemoveAsync(doomed, ct).ConfigureAwait(false);
        return count;
    }

    /// <summary>The cheap prune path, plus the census that keeps the similarity index in step with it.
    /// <para>The store decides alone here and reports only a COUNT, so the ids it removed have to be
    /// recovered by comparing the scope before and after. Re-deriving its criterion in C# instead would be a
    /// second copy of one rule, and it would not even agree:
    /// <see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/> is deliberately CONSERVATIVE — widened by
    /// <c>MaxConnectionBoost</c> — so an engine-side evaluation would delete strictly more than this path
    /// does today.</para>
    /// <para>The census is paid only when a vector store is wired, and pruning is periodic maintenance
    /// rather than a hot path — the same trade the derivable path above already makes.</para></summary>
    private async Task<int> PruneInStoreAsync(string taskKey, string? scope, double floorValue,
        TimeSpan? olderThan, CancellationToken ct)
    {
        double? cutoff = floorValue > 0 ? _policy.CandidateCutoff(floorValue) : null;
        if (!_vectors.Wired)
            return await store.PruneAsync(Name, taskKey, scope, cutoff, olderThan, ct).ConfigureAwait(false);

        var before = await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false);

        var count = await store.PruneAsync(Name, taskKey, scope, cutoff, olderThan, ct).ConfigureAwait(false);
        if (count == 0) return 0;

        var surviving = (await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false)).Select(n => n.Id).ToHashSet();

        await _vectors.RemoveAsync([.. before.Where(n => !surviving.Contains(n.Id))], ct).ConfigureAwait(false);
        return count;
    }

    /// <summary>Forget everything under (<paramref name="taskKey"/>, <paramref name="scope"/>) — explicit,
    /// never a side effect of decay, which only ever ranks.
    /// <para><b>The similarity index goes FIRST, and that order is the promise.</b> Enrichment stores each
    /// entry's full content as a vector payload, so erasing the nodes alone would leave it readable in a
    /// projection — and this is the path an application calls when a user withdraws consent, which
    /// <b>D72</b> requires to be COMPLETE. Vectors first means a failure here leaves the nodes intact and the
    /// call retryable; the reverse order would report success over surviving content. <c>PruneAsync</c>
    /// deliberately orders the other way, because there the cheap failure is an orphan rather than a
    /// residue.</para></summary>
    /// <param name="taskKey">The task to clear.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        using var removals = await RemovalsAsync(ct).ConfigureAwait(false);
        await _vectors.ForgetAsync(taskKey, scope, async () =>
                (await store.SeedAsync(Name, taskKey, scope: null, query: null, limit: int.MaxValue, ct)
                    .ConfigureAwait(false)).Select(n => n.Scope).Distinct(StringComparer.Ordinal),
            ct).ConfigureAwait(false);
        await store.ForgetAsync(Name, taskKey, scope, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Enumerates the entries once, then embeds them in batches of
    /// <see cref="GraphMemoryOptions.ReindexBatchSize"/> OUTSIDE the removal lock, so a slow backend holds no removal
    /// up, and writes each batch back UNDER it, re-reading which of its entries still exist first. A batch whose
    /// embed call fails is logged and counted <see cref="MemoryReindexResult.Failed"/>; a write that fails throws, as
    /// the index itself is then broken. A rerun re-embeds the WHOLE task again: nothing records which model wrote a
    /// stored vector.</remarks>
    public async Task<MemoryReindexResult> ReindexAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(taskKey);
        if (!_vectors.Wired)
            throw new InvalidOperationException(
                $"Memory engine '{Name}' has no vector index to re-embed into; register an IVectorStore.");
        if (!_vectors.Enriches) throw new InvalidOperationException(EmbeddingRouting.NothingEmbeds);

        var nodes = await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct).ConfigureAwait(false);
        var (indexed, failed) = (0, 0);
        foreach (var batch in nodes.Chunk(_options.ReindexBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<float[]> vectors;
            try
            {
                vectors = await _vectors.EmbedBatchAsync([.. batch.Select(n => n.Content)], ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "re-embedding a batch of {Count} for {Engine} failed; they keep their old vectors",
                    batch.Length, Name);
                failed += batch.Length;
                continue;
            }

            using var removals = await RemovalsAsync(ct).ConfigureAwait(false);
            for (var i = 0; i < batch.Length; i++)
            {
                if (await store.GetAsync(Name, batch[i].Id, ct).ConfigureAwait(false) is null) continue;   // removed mid-pass
                await _vectors.UpsertAsync(batch[i], vectors[i], ct).ConfigureAwait(false);
                indexed++;
            }
        }
        return new MemoryReindexResult(indexed, failed);
    }

    private async Task<Held> RemovalsAsync(CancellationToken ct)
    {
        await _removals.WaitAsync(ct).ConfigureAwait(false);
        return new Held(_removals);
    }

    private readonly struct Held(SemaphoreSlim held) : IDisposable
    {
        public void Dispose() => held.Release();
    }

    /// <summary>Clamp one component of a composed <see cref="MemoryTick"/> to something a store may keep.
    /// <para>Both components are PERSISTED, and a bare <see cref="Math.Max(double,double)"/> propagates a BYO
    /// <see cref="IMemoryAgePolicy.Advance"/>'s <c>NaN</c> into every later entry's age
    /// (<c>.claude/knowledge/pitfalls.md</c>, "a clamp is not a finiteness guard").</para>
    /// <para>A non-finite component becomes <c>1</c> rather than <c>0</c> because <see cref="MemoryTick.One"/>
    /// already defines that as an ordinary write ("crowds by one and is fully encoded"). <c>0</c> would be a
    /// second, invented meaning — and on the encoding side it multiplies
    /// <see cref="IMemoryRetrievabilityPolicy.InitialStability"/> to zero, storing an entry that is
    /// unretrievable the moment it is written.</para>
    /// <para>This defends the WRITE only. <see cref="IMemoryAgePolicy.Age"/> is deliberately left uncoerced
    /// by its own contract, which is consistent rather than in tension: nothing persists an age.</para>
    /// </summary>
    private static double Ordinary(double component) =>
        double.IsFinite(component) ? Math.Max(0, component) : 1;

    /// <summary>Resolves <see cref="MemoryQuery.RankingPolicyName"/> against this engine's own
    /// <see cref="_namedRanking"/> catalog — null takes <see cref="_ranking"/>, the engine's configured
    /// default; any other name must be IN the catalog or this throws. <b>Never a silent fallback</b>: a name
    /// this engine does not recognize is a caller mistake, and reverting quietly to the default is exactly
    /// the kind of bug that surfaces months later as "ranking seems off" — see
    /// <see cref="MemoryQuery.RankingPolicyName"/>'s own remarks.</summary>
    /// <exception cref="KeyNotFoundException"><paramref name="name"/> is not null and is not registered on
    /// this engine.</exception>
    private IMemoryRankingPolicy ResolveRanking(string? name)
    {
        if (name is null) return _ranking;
        if (_namedRanking.TryGetValue(name, out var found)) return found;
        throw new KeyNotFoundException(
            $"No ranking policy named '{name}' is registered on memory engine '{Name}'. Registered: " +
            (_namedRanking.Count == 0 ? "(none)" : string.Join(", ", _namedRanking.Keys)) + ".");
    }

    private bool Owns(MemoryRef reference) => string.Equals(reference.Engine, Name, StringComparison.Ordinal);

    /// <summary>A product of two non-negative counts, capped at <see cref="int.MaxValue"/> rather than wrapped
    /// negative: a caller's limit is unbounded, and a negative count means "no limit" to SQLite, throws on
    /// Postgres and returns nothing in-process.</summary>
    private static int Saturating(int a, int b) => (int)Math.Min(int.MaxValue, (long)a * b);

    private double Retrievability(GraphNode node) =>
        node.Grade == MemoryGrade.Authoritative ? 1 : _policy.Retrievability(ResolvedState(node));

    /// <summary>This node's decay bookkeeping with BOTH age axes replaced by what every registered
    /// <see cref="IMemoryAgePolicy"/> resolves them to — <see cref="MemoryDecayState.Age"/> from
    /// <see cref="GraphNode.AgeSample"/> and <see cref="MemoryDecayState.StrengthAge"/> from
    /// <see cref="GraphNode.StrengthAgeSample"/>. Everything else
    /// (<c>Stability</c>/<c>Strength</c>/<c>Signals</c>/<c>Difficulty</c>) is untouched.
    /// <para><b>Both axes must be resolved, not just the first.</b> They are consumed together —
    /// <see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/> divides <c>Age</c> by an effective stability
    /// that <c>StrengthAge</c> itself lengthens — so resolving one and leaving the other as the store's raw
    /// accumulator mixes two units inside a single expression.</para></summary>
    private MemoryDecayState ResolvedState(GraphNode node) => node.DecayState with
    {
        Age = _age.Compose(node.AgeSample, node.Age),
        StrengthAge = _age.Compose(node.StrengthAgeSample, node.StrengthAge),
    };

    /// <summary>An edge's weight after decay. The store orders by the RAW value as a cheap pre-sort and the
    /// curve is applied here, so a heavy but stale link falls below a lighter fresh one — which is what
    /// stops a graph that only ever gained edges from saturating until everything reaches everything.
    /// <para><b>The age is projected through the installed policies, like the other two axes</b> — no axis
    /// in this subsystem keeps its own clock, which is what the <see cref="IMemoryAgePolicy"/> seam exists to
    /// prevent. <see cref="GraphMemoryOptions.EdgeHalfLife"/> is therefore denominated in whatever the
    /// installed policies count — with the shipped <see cref="MemoryAgeKind.Accumulating"/> default, the
    /// position accumulator.</para></summary>
    private double EffectiveEdgeWeight(GraphNeighbour neighbour)
    {
        var halfLife = _options.EdgeHalfLife;
        var age = _age.Compose(neighbour.EdgeAgeSample, neighbour.EdgeAge);
        return age <= 0 || halfLife <= 0
            ? neighbour.EdgeWeight
            : neighbour.EdgeWeight * Math.Pow(2, -age / halfLife);
    }

    /// <summary>One gathered candidate: the entry, how far out it was reached, and which sources matched it
    /// at what rank. A hop neighbour carries <see cref="MemorySeedRanks.Empty"/>.</summary>
    private readonly record struct GatheredCandidate(GraphNode Node, int Hop, MemorySeedRanks Ranks);

    /// <summary>
    /// One source's ELIGIBLE nodes, ranked by that source's OWN <see cref="GraphNode.Relevance"/> gradient —
    /// <c>0</c> in a slot means "no rank", which a ranking policy reads as no relevance evidence.
    ///
    /// <para><b>The gradient, not the list POSITION.</b> Position assumes every source returns a
    /// relevance-ordered list, and <see cref="IMemoryGraphStore.SeedAsync"/>'s own contract says the
    /// in-process store has no rank order at all. Treating its <c>grade → salience → recency</c> order as a
    /// relevance ramp is D97 in a new costume: there, a candidate nobody SCORED reported maximum relevance;
    /// here, a candidate nobody ORDERED BY RELEVANCE would report a relevance RANK.</para>
    ///
    /// <para><b>All eligible nodes on ONE value means the source is UNORDERED, and it earns NO ranks.</b> Not
    /// a shared rank 1 — that would hand every one of them this source's BEST term (<c>w/(K+1)</c>),
    /// promoting an uninformative channel instead of silencing it. Silence is also what a uniformly-tied
    /// signal already contributed under the pooled path (<b>D82</b>). A SINGLE eligible node is the
    /// exception: there is no gradient to place it on and it is that source's top hit, so it takes rank
    /// 1.</para>
    ///
    /// <para>Otherwise COMPETITION ranking (<see cref="MemoryRankingContract.CompetitionRanks"/>), the rule
    /// <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/> applies to its other signals.</para>
    ///
    /// <para>Comparing <see cref="GraphNode.Relevance"/> here does NOT put a score across the seam: the
    /// comparison is strictly WITHIN one source, which is the only scope that member's contract claims
    /// ("higher is better, within one seed, from one backend"). Two sources' values are never compared.</para>
    /// </summary>
    private static int[] SourceRanks(IReadOnlyList<GraphNode> eligible)
    {
        var n = eligible.Count;
        if (n == 0) return [];
        if (n == 1) return [1];

        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = eligible[i].Relevance;

        return new HashSet<double>(values).Count == 1
            ? new int[n]   // unordered — every slot stays 0
            : MemoryRankingContract.CompetitionRanks(values, ascending: false);
    }

    /// <summary>Gather the candidate set: every registered <see cref="IMemorySeedSource"/> in turn, then the
    /// hop expansion out from everything they found.
    ///
    /// <para><b>ONLY a <see cref="GraphNode.Matched"/>-<c>true</c> node is eligible for a rank</b>, and the
    /// other two states are refused for the same reason in two costumes. <c>false</c> is the grade carve-out:
    /// store seeds arrive GRADE-FIRST, so an authoritative entry the query never matched sorts to the top and
    /// crediting it would undo <b>D97</b>. <c>null</c> is "the read never asked a relevance question" — a
    /// fetch by id, a handle lookup, a walk — and a RANK is exactly the number nobody measured that D97 says
    /// not to invent. Both still enter the candidate set and compete on their other signals.</para>
    ///
    /// <para><b>That gate is what keeps an UNORDERED channel silent at every cardinality.</b> Inferring
    /// unorderedness from tie-ness alone cannot: one sample carries no tie information, so a
    /// <see cref="SubjectSeedSource"/> resolving exactly one handle would have taken rank 1 — the BEST term —
    /// while its own contract says it contributes none.</para>
    ///
    /// <para>What ranks the eligible is <see cref="SourceRanks"/>, which reads each source's own
    /// <see cref="GraphNode.Relevance"/> gradient rather than its list POSITION.</para>
    ///
    /// <para>Sources are read in their registered order and each candidate's ranks are appended in it, so two
    /// candidates matched by the same sources build IDENTICAL rank lists —
    /// <see cref="MemorySeedRanks"/> compares by sequence.</para></summary>
    private async Task<List<GatheredCandidate>> GatherAsync(MemoryQuery query, int limit,
        CancellationToken ct)
    {
        // no faintness bound: the store returns candidates grade-first, then most-recently-used, and the
        // count is the only limit, so nothing is excluded for having decayed — burial happens by rank, above
        var candidates = Saturating(limit, _options.CandidateMultiplier);
        var request = new MemorySeedRequest(Name, store, query, candidates);

        var found = new List<(GraphNode Node, int Hop)>();
        var seen = new HashSet<long>();
        var ranks = new Dictionary<long, List<MemorySeedRank>>();

        foreach (var source in _seedSources)
        {
            ct.ThrowIfCancellationRequested();
            var produced = await source.SeedAsync(request, ct).ConfigureAwait(false);

            var eligible = new List<GraphNode>(produced.Count);
            var fromThisSource = new HashSet<long>();
            foreach (var node in produced)
            {
                // one rank per source per candidate: a source repeating a node would otherwise buy a second
                // fusion term for the same evidence, and MemorySeedRanks reports only the first
                if (!fromThisSource.Add(node.Id)) continue;
                if (seen.Add(node.Id)) found.Add((node, 0));
                // true ONLY — see this method's own remarks for why null is refused alongside false
                if (node.Matched != true) continue;
                eligible.Add(node);
            }

            var assigned = SourceRanks(eligible);
            for (var i = 0; i < assigned.Length; i++)
            {
                if (assigned[i] == 0) continue;
                var id = eligible[i].Id;
                if (!ranks.TryGetValue(id, out var list)) ranks[id] = list = [];
                list.Add(new MemorySeedRank(source.Name, assigned[i]));
            }
        }

        var frontier = seen.ToList();

        for (var hop = 1; hop <= _options.Hops && frontier.Count > 0; hop++)
        {
            ct.ThrowIfCancellationRequested();
            var neighbours = await store
                .NeighboursAsync(Name, query.TaskKey, frontier, candidates, ct).ConfigureAwait(false);

            frontier = [];
            // re-rank by DECAYED edge weight: the store's raw-weight ordering is a pre-sort, and a link
            // that stopped recurring must stop pulling its neighbour into recall
            foreach (var neighbour in neighbours
                         .OrderByDescending(EffectiveEdgeWeight)
                         .ThenByDescending(n => n.Node.Id)
                         .Select(n => n.Node))
                if (seen.Add(neighbour.Id))
                {
                    found.Add((neighbour, hop));
                    frontier.Add(neighbour.Id);
                }
        }

        return [.. found.Select(f => new GatheredCandidate(f.Node, f.Hop,
            ranks.TryGetValue(f.Node.Id, out var r) ? new MemorySeedRanks(r) : MemorySeedRanks.Empty))];
    }

    /// <summary>Asks the verifier which of these actually answered the query, fail-open in every direction
    /// (<see cref="MemoryVerdicts.AskAsync"/>).</summary>
    private Task<MemoryVerification> VerifyAsync(string queryText, IReadOnlyList<RankedMemory> scored,
        CancellationToken ct) =>
        MemoryVerdicts.AskAsync(_verification, queryText, scored, _logger, Name, ct);

    /// <summary>Record reinforcement and co-activation for what a recall actually returned.
    /// <para>BEST-EFFORT by design: a failure logs and the caller keeps its hits, so a read-only database
    /// degrades to "no learning" rather than to "no memory". Co-activation is capped, or a ten-item recall
    /// would write forty-five edges every turn.</para>
    /// <para>None of this advances the engine's position — a recall is not new material, so it must not age
    /// anything.</para>
    /// <para>Both <see cref="MemoryDecayState.Stability"/> and <see cref="MemoryDecayState.Difficulty"/> reach
    /// <see cref="GraphTouch"/>. <see cref="IMemoryRetrievabilityPolicy.Reinforce"/> returns the FULL state and
    /// requires every field a policy does not own to come back unchanged, so extracting these two is as
    /// complete as persisting the whole thing for every shipped policy — a future policy owning a THIRD field
    /// needs <see cref="GraphTouch"/> widened before this line can reach it.</para>
    /// <para><b>The review log is DATA, never a decision.</b> No line in this class reads
    /// <see cref="IMemoryGraphStore.ReviewsAsync"/>, so nothing logged can feed back into retrievability,
    /// ranking or pruning. <see cref="MemoryReviewWrite.ReviewGrade"/> is
    /// <see cref="IMemoryRetrievabilityPolicy.DerivedGrade"/>'s return on the SAME <c>pre</c> state handed to
    /// <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>, never re-derived afterward.</para>
    /// <para>One <see cref="Guid"/> per call, shared across every node it touches, so a fitter can tell a
    /// group of rows came from the SAME recall.</para>
    /// <para><b>A failed log write cannot cost the touches or the edges</b> — not because of a nested catch,
    /// but because <see cref="IMemoryGraphStore.WriteBackAsync"/> writes the review log LAST (D101).</para></summary>
    /// <param name="nodes">What gets REINFORCED — touched and co-activated.</param>
    /// <param name="act">Which call this is, for <see cref="GraphMemoryOptions.ReinforceOn"/>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="logged">What gets LOGGED, which may be a SUPERSET of <paramref name="nodes"/> — every
    /// entry the recall returned, including ones a verifier rejected. Null logs exactly what is reinforced,
    /// which is what an expansion wants. <b>This split is what lets the review log contain failures</b>: a
    /// rejected entry is never touched, so before it existed no row was ever written for one.</param>
    /// <param name="verdict">What the verifier said about a given node — <c>null</c> for "no judgement",
    /// which must not be recorded as a failure.</param>
    private async Task ReinforceAsync(IReadOnlyList<GraphNode> nodes, MemoryReinforcementActs act,
        CancellationToken ct, IReadOnlyList<GraphNode>? logged = null,
        Func<GraphNode, bool?>? verdict = null)
    {
        // The ACT gate, distinct from the EFFECT gate below: this asks whether THIS CALL reinforces at all,
        // that asks which of the two effects it applies. Co-activation is inside the gate on purpose — an
        // act that does not reinforce should not be writing permanent edges from its own returned set
        // either, which is the same "no permanent change from a retrieval decision" constraint (design
        // §5.7.0) the effect split exists to serve.
        if (!_options.ReinforceOn.HasFlag(act)) return;

        try
        {
            var reinforceable = nodes.Where(n => n.Grade != MemoryGrade.Authoritative).ToList(); // r = 1, nothing to reinforce

            // THE BREADTH GATE, and it is the graded form of the act gate above. A recall hands back a
            // RANKED list of guesses; reinforcing all of them treats the tenth hit as equal evidence to the
            // first, which is where "the loop upvotes its own prior" does its damage. `nodes` arrives in
            // rank order, so taking the head reinforces only what the ranker was most confident about.
            // Applies to RECALL only: an expansion is a single entry a caller explicitly paid for, and
            // there is no ranked tail to trim.
            if (act == MemoryReinforcementActs.Recall && _options.RecallReinforceCap is { } cap)
                reinforceable = [.. reinforceable.Take(cap)];

            var batchId = Guid.NewGuid();
            var touches = new List<GraphTouch>(reinforceable.Count);

            // What gets LOGGED is a superset of what gets touched: every entry the recall returned, so a
            // judge's REJECTION is recorded too. `logged` is null for an expansion (nothing was rejected)
            // and for any call with no verifier, where the two sets are identical anyway. Authoritative
            // entries are excluded on the same grounds they are excluded from reinforcement — r = 1, so
            // there is no prediction to score and a fitted row would be meaningless.
            var loggable = (logged ?? reinforceable)
                .Where(n => n.Grade != MemoryGrade.Authoritative)
                .ToList();
            var reviews = new List<MemoryReviewWrite>(loggable.Count);
            var touched = reinforceable.Select(n => n.Id).ToHashSet();

            // THE TWO EFFECTS, SEPARATED HERE: a touch resets age AND writes back the grown stability, and
            // the two pull in opposite directions — the reset keeps a rarely-queried fact alive, the growth
            // entrenches whatever the ranker returned (docs/DECISIONS.md D57). Which applies is the ENGINE's
            // call, never one curve's private constant a consumer's own policy would not have.
            var grow = _options.Reinforcement.HasFlag(MemoryReinforcementEffects.StabilityGrowth);
            var resetAge = _options.Reinforcement.HasFlag(MemoryReinforcementEffects.AgeReset);

            foreach (var n in loggable)
            {
                // ONE pre-state per node, read exactly once: `pre` is what Reinforce derives its grade from,
                // what DerivedGrade below must read too, and what the review row's own "pre" columns record
                // — three uses of the identical value, never three separate reads that could drift apart.
                var pre = ResolvedState(n);
                var reinforced = _policy.Reinforce(pre);
                var grade = _policy.DerivedGrade(pre);

                // Growth suppressed => write back exactly what was STORED, including its provenance: nothing
                // recomputed this entry's state, so stamping the current policy's bit would claim a
                // computation that did not happen and make "which policy produced this" a lie (design §5.7).
                if (touched.Contains(n.Id))
                    touches.Add(grow
                        ? new GraphTouch(n.Id, reinforced.Stability, (long)_policy.Provenance, reinforced.Difficulty)
                        : new GraphTouch(n.Id, n.Stability, n.ProvenanceRetrievability, n.Difficulty));

                // The review log records what the POLICY computed, not what was persisted — it is an
                // observation of the curve's opinion, and suppressing the write does not unmake the opinion.
                // A fitter reading this log needs the model's prediction whether or not the engine banked it,
                // AND — for a rejected entry — whether the prediction was any good, which is `Verified`.
                reviews.Add(new MemoryReviewWrite(n.Id, batchId, pre.Age, pre.Stability, pre.Difficulty,
                    pre.Strength, pre.StrengthAge, grade, reinforced.Stability, reinforced.Difficulty,
                    (long)_policy.Provenance, verdict?.Invoke(n)));
            }

            // The co-activation set: every pair among the top CoActivationCap hits, C(5,2) = ten edges at
            // the shipped cap.
            var top = nodes.Take(_options.CoActivationCap).Select(n => n.Id).ToList();
            var edges = new List<GraphEdgeWrite>(top.Count * (top.Count - 1) / 2);
            for (var i = 0; i < top.Count; i++)
                for (var j = i + 1; j < top.Count; j++)
                    edges.Add(new GraphEdgeWrite(top[i], top[j], null, 1, Symmetric: true));

            // ONE unit of work for the whole write-back, where the three parts used to be three calls each
            // opening its own connection (`docs/DECISIONS.md` D101). Each switch is applied by handing an
            // EMPTY part, never by reordering: `None` must not write a no-op touch, because the store cannot
            // be asked to hold the age still and a touch with everything else suppressed would STILL reset
            // it. The review log goes last, which is the store contract's own guarantee and is what keeps a
            // broken log from costing the touch or the edges.
            var writeBack = new GraphWriteBack(resetAge ? touches : [], edges,
                _options.LogReviews ? reviews : [], _options.ReviewLogCap);
            if (writeBack.Touches.Count > 0 || writeBack.Edges.Count > 0 || writeBack.Reviews.Count > 0)
                await store.WriteBackAsync(Name, writeBack, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // "partly or wholly" is the honest wording: the write-back's parts commit in order, so a failure
            // in a later one leaves the earlier ones landed.
            _logger.LogWarning(ex,
                "graph write-back failed for {Engine}; returning hits, learning partly or wholly unrecorded",
                Name);
        }
    }
}
