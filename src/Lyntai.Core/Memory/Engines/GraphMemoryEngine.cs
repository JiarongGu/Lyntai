using System.Globalization;
using Lyntai.Embeddings;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
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
/// <param name="retrievability">The decay curve; null builds a <see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/>
/// with default options. <b>This bare default and the DI-registered one now agree</b>
/// (<c>AddMemoryEngine</c>/<c>AddMemory</c>/<c>UseGraph</c> — see <c>docs/DECISIONS.md</c> D49) — a
/// two-defaults split existed here through 3.0's own D49 change, when this constructor's own fallback stayed
/// on the deleted exponential curve while the DI path had already moved to DSR, deliberately, so that
/// flipping this fallback would not silently retarget every test in this repository that constructed the
/// engine directly to exercise the exponential curve's own arithmetic without naming it. Deleting that curve
/// removed the second option the split was choosing between, and resolved it on its own: a hand-constructing
/// consumer now gets the same curve DI gives, unless this parameter (or
/// <c>services.AddSingleton&lt;IMemoryRetrievabilityPolicy&gt;</c> ahead of a DI-built engine) says
/// otherwise.</param>
/// <param name="agePolicies">What one write does to this memory — the coexisting age dimensions in play; null
/// or empty takes a single burst-damped per-write age policy. <b>The damping is not optional garnish</b> — an
/// undamped count-based policy lets a bulk ingest wipe everything stored before it. Each policy declares its
/// own <see cref="IMemoryAgePolicy.Kind"/>, and this engine honours it: a <see cref="MemoryAgeKind.Derivable"/>
/// policy's retrievability-facing age is projected from the primitives (<see cref="GraphNode.AgeSample"/>);
/// an <see cref="MemoryAgeKind.Accumulating"/> one's comes from the store's own <c>Advance</c>-driven
/// accumulator (<see cref="GraphNode.Age"/>) — see <see cref="ResolvedAge"/>.</param>
/// <param name="ageComposition">How several coexisting age policies combine into one tick and one age; null
/// takes <see cref="SummedAgeCompositionPolicy"/>. Irrelevant when only one policy is registered — composing a
/// singleton is the identity.</param>
/// <param name="logger">Optional; reinforcement and recall failures are logged rather than thrown.</param>
/// <param name="embedder">Optional. With a <paramref name="vectors"/> store, enables similarity
/// enrichment — a new entry is linked to its nearest existing neighbours. Pure enrichment on top of the
/// model-free floor: without it the graph still forms from co-activation and explicit links.</param>
/// <param name="vectors">Optional; see <paramref name="embedder"/>.</param>
/// <param name="saliencePolicies">Judge how strongly a write is encoded — the coexisting salience dimensions
/// in play; null or empty takes a single <see cref="StructuralSaliencePolicy"/>. Without an embedder there is
/// no novelty to judge and it reports nothing.</param>
/// <param name="salienceComposition">How several coexisting salience policies' bags combine into one; null
/// takes <see cref="MaximalSalienceCompositionPolicy"/>. Irrelevant when only one salience policy is
/// registered.</param>
/// <param name="ranking">Turns seeded, spread candidates into a scored, best-first order; null takes
/// <see cref="ReciprocalRankFusionPolicy"/> — the registered default as of 3.0 (owner ruling, 2026-08-11;
/// this library's own measurement found it beating <see cref="MultiplicativeRankingPolicy"/> on the corpus's
/// `topical` class in every shape tested — see <c>MemoryEngineRegistration.AddMemoryEngine</c>'s own remarks
/// for the full reasoning and the floor's own disclosed measurement gap). <see cref="MultiplicativeRankingPolicy"/>
/// stays shipped and registerable in one line; it is not the case a comparison found it wrong, only that it
/// lost this one measured comparison. Swappable so a
/// consumer can fuse signals differently without editing this engine; see <see cref="IMemoryRankingPolicy"/>
/// for the contract, including what it may NOT do (drop authoritative material — this engine re-admits that
/// itself, so the exemption holds against a policy that DROPS one, though not against one that SUBSTITUTES a
/// fabricated entry under the same id — see <see cref="RecallAsync"/>'s own remarks for the precise
/// shape).</param>
/// <param name="namedRankingPolicies">Alternate ranking policies THIS engine exposes for a per-call override
/// (<see cref="MemoryQuery.RankingPolicyName"/>) — null or empty exposes none, so every call uses
/// <paramref name="ranking"/> (or its own default) unless a name is registered here. Compared by ordinal
/// string equality, the same comparison <see cref="IMemoryEngineFactory"/> uses for engine names. A query
/// naming anything not in this set throws <see cref="KeyNotFoundException"/> rather than silently falling
/// back — see <see cref="MemoryQuery.RankingPolicyName"/>'s own remarks.</param>
/// <param name="clock">Reads "now" for <see cref="PruneAsync"/>'s <c>olderThan</c> criterion, on the
/// derivable path only; null takes <see cref="DateTimeOffset.UtcNow"/>. Mirrors the injectable clock every
/// <see cref="IMemoryGraphStore"/> implementation already takes, so a test that fakes the store's clock can
/// fake this engine's too — an earlier review's minor: before this parameter existed the two disagreed under a
/// test clock, because this engine had no seam of its own and read the wall clock directly.</param>
/// <param name="annotation">Judges what each written fact is ABOUT, so entries concerning the same entity
/// become connected — the only mechanism that reaches a cluster whose members share no distinguishing word
/// (see <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/>). Null is the model-free floor: no
/// annotation, no subject links, and every other behaviour identical.</param>
/// <param name="verification">Judges which of a recall's candidates actually ANSWERED the query, so
/// reinforcement follows evidence rather than the ranker's own prior and an outranked answer can be promoted
/// past the limit (see <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/>). Null is the
/// model-free floor: the ranking policy's order stands and everything a recall returns is reinforced,
/// exactly as before this seam existed.</param>
public sealed class GraphMemoryEngine(
    string name,
    IMemoryGraphStore store,
    GraphMemoryOptions? options = null,
    IMemoryRetrievabilityPolicy? retrievability = null,
    IEnumerable<IMemoryAgePolicy>? agePolicies = null,
    ILogger<GraphMemoryEngine>? logger = null,
    IEmbedder? embedder = null,
    IVectorStore? vectors = null,
    IEnumerable<IMemorySaliencePolicy>? saliencePolicies = null,
    IMemoryRankingPolicy? ranking = null,
    IMemoryAgeCompositionPolicy? ageComposition = null,
    IMemorySalienceCompositionPolicy? salienceComposition = null,
    IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies = null,
    Func<DateTimeOffset>? clock = null,
    Lyntai.Memory.Annotation.IMemoryAnnotationPolicy? annotation = null,
    Lyntai.Memory.Verification.IMemoryVerificationPolicy? verification = null)
    : IMemoryEngine, IExpandableMemory, ILinkableMemory, IForgettableMemory
{
    private readonly GraphMemoryOptions _options = options ?? new GraphMemoryOptions();
    private readonly IMemoryRetrievabilityPolicy _policy =
        ValidatedRetrievability(retrievability ?? new DsrRetrievability());
    private readonly IReadOnlyList<IMemoryAgePolicy> _agePolicies = NormalizeAgePolicies(agePolicies);
    private readonly IMemoryAgeCompositionPolicy _ageComposition = ageComposition ?? new SummedAgeCompositionPolicy();
    private readonly ILogger _logger = logger ?? NullLogger<GraphMemoryEngine>.Instance;
    private readonly IReadOnlyList<IMemorySaliencePolicy> _saliencePolicies =
        NormalizeSaliencePolicies(saliencePolicies);
    private readonly IMemorySalienceCompositionPolicy _salienceComposition =
        salienceComposition ?? new MaximalSalienceCompositionPolicy();
    private readonly IMemoryRankingPolicy _ranking = ranking ?? new ReciprocalRankFusionPolicy();
    private readonly IReadOnlyDictionary<string, IMemoryRankingPolicy> _namedRanking =
        NormalizeNamedRanking(namedRankingPolicies);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

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

    /// <summary>Null or empty takes a single burst-damped per-write age policy — the engine's default.
    /// <para><b>Rejects a second <see cref="MemoryAgeKind.Accumulating"/> policy.</b> The
    /// store's position accumulator is ONE number; two path-dependent quantities cannot share it without one
    /// silently overwriting or blending into the other, and a silent sum is exactly the quiet wrongness this
    /// domain refuses everywhere else (see <see cref="RememberAsync"/>'s own remarks on why only an
    /// Accumulating tick's Position ever reaches the accumulator).</para></summary>
    private static IReadOnlyList<IMemoryAgePolicy> NormalizeAgePolicies(
        IEnumerable<IMemoryAgePolicy>? agePolicies)
    {
        var list = agePolicies?.ToList() ?? [];
        if (list.Count == 0) return [new BurstDampenedAgePolicy(new PerWriteAgePolicy())];

        var accumulating = list.Count(c => c.Kind == MemoryAgeKind.Accumulating);
        if (accumulating > 1)
            throw new ArgumentException(
                $"{accumulating} Accumulating age policies were registered ({string.Join(", ", list
                    .Where(c => c.Kind == MemoryAgeKind.Accumulating).Select(c => c.GetType().Name))}); " +
                "at most one is supported. The store's position accumulator is a single number, and two " +
                "path-dependent (Accumulating) policies cannot share it without silently blending their " +
                "distinct histories together.", nameof(agePolicies));
        return list;
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
        MemoryProvenance.ValidateProvenanceBits(
            [.. list.Select(a => (long)a.Provenance)], i => list[i].GetType().Name);
        return list;
    }

    /// <summary>Validates a single retrievability policy's own declared provenance bit — real (never
    /// <c>None</c>) and single, the same two facts <see cref="NormalizeSaliencePolicies"/> checks across several
    /// salience policies, applied here to the one policy this seam ever has.</summary>
    private static IMemoryRetrievabilityPolicy ValidatedRetrievability(IMemoryRetrievabilityPolicy policy)
    {
        MemoryProvenance.ValidateProvenanceBits(
            [(long)policy.Provenance], _ => policy.GetType().Name);
        return policy;
    }

    private bool Enriches => embedder is not null && vectors is not null;

    /// <summary>This engine embeds every write and no recall reads those vectors — an embedder and a vector
    /// store are wired, so novelty and similarity linking run on the WRITE path, while
    /// <see cref="GraphMemoryOptions.SemanticSeedK"/> is 0 so the READ path consults none of it.
    /// <para>Internal, and read only by <see cref="MemoryWiring"/>: it is a wiring diagnostic, not something
    /// a consumer branches on. Exposed as a property rather than reflected over, so a rename is a compile
    /// error instead of a sweep that throws the next time somebody runs it.</para></summary>
    internal bool EmbedsWithoutSeeding => Enriches && _options.SemanticSeedK <= 0;

    /// <summary>This engine records subject handles and no recall can reach one — an annotator is wired, so
    /// every write pays a model call to say what the fact is ABOUT, while
    /// <see cref="GraphMemoryOptions.SubjectSeedK"/> (or its scan) is 0 so the only readers left are the
    /// write path's own linking and the annotator's reuse list.
    /// <para>Internal and read only by <see cref="MemoryWiring"/>, exactly like
    /// <see cref="EmbedsWithoutSeeding"/> — the same defect on the other index.</para></summary>
    internal bool RecordsSubjectsWithoutSeeding =>
        annotation is not null && (_options.SubjectSeedK <= 0 || _options.SubjectSeedScan <= 0);

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative | MemoryGrades.Authoritative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        // BEFORE the upsert, because a suggested grade has to reach the row being written — grade is not
        // something a later update can fix up without a second write and a window where the fact is stored
        // at the wrong one.
        var annotated = await AnnotateAsync(write, ct).ConfigureAwait(false);

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

        var tick = AdvanceAgePolicies(write);

        // ONE embed, ONE similarity search, shared between salience judgement (which needs the comparison before
        // the node has an id) and EnrichAsync below (linking + indexing, which needs it after). Without a
        // vector store there is nothing to compare against, so nothing is judged or linked — the honest
        // answer, not a degraded one.
        var search = await SearchAsync(write, ct).ConfigureAwait(false);
        var (signals, salienceProvenance) = CollectSignals(write, Probe(write, search));

        var id = await store.UpsertAsync(
            new GraphNodeWrite(Name, write.TaskKey, write.Scope, headline, write.Content, grade,
                _policy.InitialStability * Ordinary(tick.Encoding), Ordinary(tick.Position),
                write.Metadata, signals,
                ProvenanceRetrievability: (long)_policy.Provenance,
                ProvenanceSalience: salienceProvenance,
                GradeStated: stated),
            ct).ConfigureAwait(false);

        await EnrichAsync(id, write, search, ct).ConfigureAwait(false);
        await LinkBySubjectAsync(id, write, annotated, ct).ConfigureAwait(false);
        return new MemoryRef(Name, id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Ask the annotator what this fact is about, showing it recent entries so a pronoun is
    /// resolvable. BEST-EFFORT: no annotator, or a failing one, yields <see cref="MemoryAnnotation.None"/>
    /// and the write proceeds exactly as it would have — the model-free floor is not negotiable.</summary>
    private async Task<MemoryAnnotation> AnnotateAsync(MemoryWrite write, CancellationToken ct)
    {
        if (annotation is null) return MemoryAnnotation.None;
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

            return await annotation.AnnotateAsync(new MemoryAnnotationRequest(write, recent, known), ct)
                .ConfigureAwait(false) ?? MemoryAnnotation.None;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "memory annotation failed for {Engine}; storing without it", Name);
            return MemoryAnnotation.None;
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
    private async Task LinkBySubjectAsync(long id, MemoryWrite write, MemoryAnnotation annotated,
        CancellationToken ct)
    {
        if (annotated.Subjects.Count == 0) return;
        try
        {
            await store.RecordSubjectsAsync(Name, id, [.. annotated.Subjects], ct).ConfigureAwait(false);
            if (_options.AnnotationLinkK <= 0) return;

            foreach (var subject in annotated.Subjects)
            {
                if (string.IsNullOrWhiteSpace(subject)) continue;
                // +1 because this node is now recorded under the subject too and is filtered out below —
                // without it a K of 1 would find only itself and link nothing
                var found = await store.NodesBySubjectAsync(Name, write.TaskKey, write.Scope, subject,
                    _options.AnnotationLinkK + 1, ct).ConfigureAwait(false);

                foreach (var other in found)
                {
                    if (other == id) continue;
                    await store.LinkAsync(Name, id, other, "subject", 1, symmetric: true, ct)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "subject linking failed for {Engine}; the entry is stored unlinked", Name);
        }
    }

    /// <summary>The one embed and similarity search a write needs, shared by <see cref="Probe"/> (novelty,
    /// before the node has an id) and <see cref="EnrichAsync"/> (linking + indexing, after) — an embedder
    /// that bills a network call per invocation is paid ONCE per write, never twice for one.
    /// <para>Null when nothing is enriched (no embedder/vector store wired, or
    /// <see cref="GraphMemoryOptions.SimilarityK"/> is non-positive) or when the search itself fails —
    /// BEST-EFFORT, exactly like the enrichment it now backs: a failing embedder must not fail the
    /// write.</para>
    /// <para>Searches <see cref="GraphMemoryOptions.SimilarityK"/> + 1 because, on a re-remember, this
    /// write's own PRIOR vector — from the earlier write of identical content — is still sitting in the
    /// collection (its replacement is written only once <see cref="EnrichAsync"/> knows the id) and would
    /// otherwise occupy a slot a genuine neighbour should have. <see cref="Probe"/> and
    /// <see cref="EnrichAsync"/> both exclude that self-match by CONTENT — the store's own dedup key —
    /// rather than by id. <see cref="Probe"/> runs before this write has an id at all, so id-equality is
    /// simply unavailable there. <b>On a refresh, the reused id would ALSO identify the self-match inside
    /// <see cref="EnrichAsync"/></b> — that id's own prior vector is already indexed under it from the
    /// earlier write — so id-equality is not wrong there, only insufficient on its own: content is the ONE
    /// rule that works at both call sites, so both use it, rather than each guessing its own definition of
    /// "self".</para></summary>
    private async Task<(float[] Vector, IReadOnlyList<VectorMatch> Near)?> SearchAsync(MemoryWrite write,
        CancellationToken ct)
    {
        if (!Enriches || _options.SimilarityK <= 0) return null;
        try
        {
            var vector = await embedder!.EmbedAsync(write.Content, ct).ConfigureAwait(false);
            var near = await vectors!
                .SearchAsync($"{Name}|{write.TaskKey}|{write.Scope}", vector, _options.SimilarityK + 1, ct)
                .ConfigureAwait(false);
            return (vector, near);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "similarity search failed for {Engine}; storing without signals or links", Name);
            return null;
        }
    }

    /// <summary>What this write can be judged against, from the shared <see cref="SearchAsync"/> result —
    /// how unlike its nearest neighbours it is, and how many neighbours there were to compare with, once
    /// this write's own PRIOR vector (identical content, an earlier write) is excluded. Without that
    /// exclusion a re-remember would find itself at cosine ≈ 1, be judged as minimally novel, and (via
    /// <see cref="IMemorySaliencePolicy.Signals"/> reporting the neutral value) silently erase any salience the FIRST write
    /// earned — the inverse of intent, since a re-remembered entry is the one being reinforced.
    /// <para><b>No search, or nothing left after self-exclusion, reports novelty 0 with zero comparables —
    /// not 1.</b> Nothing to compare against is no information, not "unlike everything," which is the
    /// precise failure <see cref="SalienceOptions.MinimumComparables"/> exists to prevent, and it would
    /// arrive by the one route that guard cannot see.</para></summary>
    private static (double Novelty, int Comparables) Probe(MemoryWrite write,
        (float[] Vector, IReadOnlyList<VectorMatch> Near)? search)
    {
        if (search is null) return (0, 0);
        var survivors = search.Value.Near
            .Where(m => !string.Equals(m.Payload, write.Content, StringComparison.Ordinal))
            .ToList();
        return survivors.Count == 0
            ? (0, 0)
            : (Math.Clamp(1 - survivors[0].Score, 0, 1), survivors.Count);
    }

    /// <summary>Collect signals from every registered salience policy, treating each one's own failure as "no
    /// signals from THAT policy" — a broken policy must degrade to 2.5.0 decay behaviour, never to a lost
    /// write, and never take a healthy sibling down with it. The composition then decides how the resulting
    /// bags combine into the ONE bag a write stores — the identity when only one policy is
    /// registered.
    /// <para><b>Provenance records who PRODUCED a signal, not who merely ran.</b> A policy that declined
    /// (too few comparables) or failed contributes <see cref="MemorySignals.Empty"/> to <c>results</c> above
    /// and its own bit is excluded here — crediting it would claim this entry's signals reflect a judgement
    /// that never actually happened (design doc §5.7).</para></summary>
    private (MemorySignals Signals, long Provenance) CollectSignals(MemoryWrite write,
        (double Novelty, int Comparables) probe)
    {
        var context = new SalienceContext(Name, probe.Novelty, probe.Comparables);
        var results = new List<MemorySignals>(_saliencePolicies.Count);
        var contributions = new List<long>(_saliencePolicies.Count);
        foreach (var policy in _saliencePolicies)
        {
            MemorySignals signals;
            try
            {
                signals = policy.Signals(write, context);
            }
            catch (OperationCanceledException) { throw; }
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

    /// <summary>Link a newly stored entry to its nearest existing neighbours and index its own vector, from
    /// the similarity search <see cref="RememberAsync"/> already ran to judge it — no second embed or
    /// search.
    /// <para>BEST-EFFORT, deliberately: enrichment sits on top of a model-free floor, so a failing embedder
    /// or vector store must not fail the write. The entry is already stored by the time this runs — it
    /// simply has fewer connections than it might have had, or (when the shared search already failed) none
    /// at all, and is not indexed for anyone else's similarity search either.</para></summary>
    private async Task EnrichAsync(long id, MemoryWrite write,
        (float[] Vector, IReadOnlyList<VectorMatch> Near)? search, CancellationToken ct)
    {
        if (search is null) return;
        var (vector, near) = search.Value;
        var collection = VectorCollection(write.TaskKey, write.Scope);
        try
        {
            foreach (var match in near)
            {
                // this write's own PRIOR vector, from an earlier remember of identical content — never a
                // neighbour of itself; see the note on SearchAsync
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
                await store.LinkAsync(Name, id, other, "similar", 1, symmetric: true, ct)
                    .ConfigureAwait(false);
            }

            await vectors!
                .UpsertAsync(collection, id.ToString(CultureInfo.InvariantCulture), vector, write.Content, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "similarity enrichment failed for {Engine}; the entry is stored with fewer links", Name);
        }
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

        List<(GraphNode Node, int Hop)> found;
        try
        {
            found = await GatherAsync(query, limit, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "graph recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }

        var candidates = found
            .Select(f => new MemoryCandidate(f.Node, Retrievability(f.Node), f.Hop))
            .ToList();

        var ranked = ranking.Rank(candidates, new MemoryRankingContext(limit, Name));
        if (ranked.Count == 0 && candidates.Count == 0) return MemoryRecall.Empty;

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
        // RESERVED SLOTS, NOT APPENDED: appended material is cut by the Take below, so an exact fact the
        // query did not match — Relevance 0, therefore ranked near the bottom — is lost outright. The reserve
        // covers EVERY authoritative candidate, not only ones a policy dropped (a ranking policy does not
        // omit them, it RANKS them), and it may DISPLACE ordinary material, which is what marking a fact
        // authoritative MEANS; `AuthoritativeReserve` bounds how many slots exact facts take.
        //
        // NOT COSMETIC: this order feeds ReinforceAsync's own `nodes.Take(CoActivationCap)` below, so which
        // pairs get a symmetric co-activation edge PERMANENTLY written to the store depends on it too, not
        // just on what a reader sees in the returned list.
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
        // returns more items than the caller asked for — the `Take(Math.Max(0, limit - reserve))` below
        // floors at zero while `reserved` is concatenated whole — and "within the caller's Limit" is a
        // promise. The `?? limit` default already carried this cap; only an EXPLICIT value escaped it.
        var reserve = Math.Min(limit,
            Math.Min(authoritative.Count, Math.Max(0, _options.AuthoritativeReserve ?? limit)));
        var reserved = authoritative.Take(reserve).ToList();
        var reservedIds = reserved.Select(r => r.Candidate.Node.Id).ToHashSet();

        var ordinary = ranked.Where(r => !reservedIds.Contains(r.Candidate.Node.Id)).ToList();

        // THE CORRECTNESS SIGNAL, APPLIED BEFORE THE CUT — and the position is the whole value of it. A
        // verifier consulted AFTER `Take(limit)` can only observe what the ranking lost below the limit;
        // consulted here it can undo that loss.
        //
        // Bounded by `VerificationDepth`: a judge sees the top slice of the ranking rather than every
        // candidate, because showing a model every candidate's headline per recall is not a shippable cost.
        // Depth is the knob that trades judgement cost against how far down an answer may be rescued from.
        var depth = Math.Max(limit,
            _options.VerificationDepth ?? limit * GraphMemoryOptions.DefaultVerificationDepthFactor);
        var verdict = await VerifyAsync(query.Query ?? string.Empty,
            [.. ordinary.Take(depth)], ct).ConfigureAwait(false);

        // A judged-relevant candidate is PROMOTED to the front, keeping the policy's relative order among
        // promoted and among demoted alike — the verifier is asked which entries answered, never to invent
        // a total ordering, so its verdict reorders in one bit rather than replacing the ranking wholesale.
        if (verdict.Judged && verdict.RelevantIds.Count > 0)
        {
            var relevant = verdict.RelevantIds.ToHashSet(StringComparer.Ordinal);
            bool IsRelevant(RankedMemory r) =>
                relevant.Contains(r.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture));
            ordinary = [.. ordinary.Where(IsRelevant), .. ordinary.Where(r => !IsRelevant(r))];
        }

        // Ordinary material leads, in the policy's order, then the reserved exact facts — the same
        // "matches lead, exact facts take the low end" shape the store's own merge uses, so the two agree.
        var scored = ordinary
            .Take(Math.Max(0, limit - reserve))
            .Concat(reserved)
            .ToList();
        if (scored.Count == 0) return MemoryRecall.Empty;

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

        await ReinforceAsync(reinforce, MemoryReinforcementActs.Recall, ct, returned, VerdictFor)
            .ConfigureAwait(false);

        // A judgement never removes an answer unless a consumer asked for that separately: a mistaken
        // verdict should cost a little learning, not a lost result. Authoritative material is exempt
        // whatever the verdict — objective (1) does not defer to a judge (D56).
        if (verdict.Judged && _options.VerificationFilters)
            scored = [.. scored.Where(x =>
                x.Candidate.Node.Grade == MemoryGrade.Authoritative
                || verdict.RelevantIds.Contains(x.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture)))];

        if (scored.Count == 0) return MemoryRecall.Empty;

        var items = scored
            .Select(x => new MemoryItem(
                new MemoryRef(Name, x.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture)),
                x.Candidate.Node.Headline,
                // associative content is withheld until expansion — that is what makes the first load
                // cheap; authoritative content is always present, because it is never returned truncated
                x.Candidate.Node.Grade == MemoryGrade.Authoritative ? x.Candidate.Node.Content : null,
                x.Candidate.Node.Grade, x.Candidate.Node.Relevance, x.Candidate.Retrievability,
                x.Candidate.Node.Degree))
            .ToList();

        // MemoryQuery.CharBudget, honoured — it shipped in 2.5.0 documented as "maximum characters the caller
        // intends to spend" and was read by NOTHING, the same class as ExpandAsync's own charBudget and
        // GraphMemoryOptions.MinRetrievability. Applied AFTER ranking so the budget cuts the weakest tail
        // rather than changing what wins, and an authoritative item is never dropped by it: objective (1) has
        // no acceptable failure rate, and its content is the one thing this engine never returns truncated.
        // A budget too small even for the reserved material still yields that material — a caller asking for
        // less than one fact gets one fact, not nothing.
        if (query.CharBudget is { } budget && budget > 0)
        {
            var spent = 0;
            var kept = new List<MemoryItem>(items.Count);
            foreach (var item in items)
            {
                var cost = item.Content?.Length ?? item.Headline.Length;
                if (item.Grade != MemoryGrade.Authoritative && kept.Count > 0 && spent + cost > budget) continue;
                spent += cost;
                kept.Add(item);
            }
            items = kept;
        }

        // The ABSTENTION signal. `Judged` is the only absolute quality statement available here — ranking is
        // relative by construction, so a page of uniformly-irrelevant material ranks perfectly well among
        // itself. Null when nothing judged, so the shipped default (no verifier) never reports `false` and a
        // consumer abstaining on `false` does not abstain on everything.
        var answered = verdict.Judged ? verdict.RelevantIds.Count > 0 : (bool?)null;

        return new MemoryRecall(items, MemorySources.Graph | (Enriches ? MemorySources.Similarity : 0), answered);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!long.TryParse(reference.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return MemoryRecall.Empty;

        var node = await store.GetAsync(Name, id, ct).ConfigureAwait(false);
        if (node is null) return MemoryRecall.Empty;

        // `hops` is CLAMPED to the engine's configured ceiling rather than honoured unbounded: this is a
        // model-facing seam (MemoryTools advertises the parameter in its tool schema), so an agent asking for
        // a large number must not be able to walk the whole graph. Zero is legal and means "just this entry".
        var depth = Math.Clamp(hops, 0, Math.Max(0, _options.Hops));

        // Breadth-first, level by level, each level ordered by effective edge weight. `seen` carries the seed
        // so a symmetric edge cannot walk back to it, and so a diamond yields its far node once.
        var walked = new List<GraphNeighbour>();
        var seen = new HashSet<long> { id };
        var frontier = new List<long> { id };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = await store.NeighboursAsync(Name, frontier, _options.DefaultLimit, ct).ConfigureAwait(false);
            frontier = [];
            foreach (var neighbour in next.OrderByDescending(EffectiveEdgeWeight).ThenByDescending(n => n.Node.Id))
            {
                if (!seen.Add(neighbour.Node.Id)) continue;
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
            new(reference, node.Headline, node.Content, node.Grade, 1, Retrievability(node), node.Degree),
        };

        // The budget bounds the NEIGHBOURS, never the entry itself: returning that entry's full content is
        // what expansion means, so a budget smaller than it trims the walk rather than refusing the request.
        // Null takes the engine's configured budget, which is itself null (unbounded) unless a host sets one.
        var budget = charBudget ?? _options.ExpandCharBudget;
        var spent = node.Content?.Length ?? node.Headline.Length;

        foreach (var neighbour in walked)
        {
            var n = neighbour.Node;
            var content = n.Grade == MemoryGrade.Authoritative ? n.Content : null;
            var cost = content?.Length ?? n.Headline.Length;
            if (budget is { } cap && spent + cost > cap) break;
            spent += cost;
            items.Add(new MemoryItem(
                new MemoryRef(Name, n.Id.ToString(CultureInfo.InvariantCulture)),
                n.Headline, content, n.Grade, n.Relevance, Retrievability(n), n.Degree));
        }

        return new MemoryRecall(items, MemorySources.Graph | (Enriches ? MemorySources.Similarity : 0));
    }

    /// <inheritdoc />
    public async Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default)
    {
        if (!long.TryParse(from.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ||
            !long.TryParse(to.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            throw new ArgumentException(
                $"Memory engine '{Name}' addresses nodes by numeric id; got '{from.Id}' and '{to.Id}'.");

        // an EXPLICIT link is a write, so it surfaces its failure — unlike the co-activation edges recall
        // records opportunistically, which are best-effort
        await store.LinkAsync(Name, a, b, kind, weight, symmetric, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Two paths, chosen by whether the store's raw accumulator is provably what
    /// <see cref="Retrievability"/> reads.</b> With every registered
    /// <see cref="IMemoryAgePolicy"/> <see cref="MemoryAgeKind.Accumulating"/> (at most one, per
    /// <c>NormalizeAgePolicies</c>) — including the engine's shipped default — <see cref="ResolvedAge"/> reads
    /// <see cref="GraphNode.Age"/> directly, so the store's own cheap, SQL-side
    /// <see cref="IMemoryGraphStore.PruneAsync"/> stays EXACT and is what runs.</para>
    /// <para>With ANY <see cref="MemoryAgeKind.Derivable"/> policy registered, the accumulator can diverge
    /// from <see cref="ResolvedAge"/> — most sharply after a policy SWAP over pre-existing data, where the
    /// accumulator still carries residue built up under whichever policy governed each historical write,
    /// while a Derivable policy's own projection re-derives fresh from the swap-safe primitives every time.
    /// The store never evaluates a curve, so this method does what <see cref="RecallAsync"/> already does —
    /// fetch candidates via <see cref="IMemoryGraphStore.SeedAsync"/>, evaluate <see cref="Retrievability"/>
    /// for each — and removes precisely the ones that fail, through
    /// <see cref="IMemoryGraphStore.DeleteAsync"/>. <see cref="IMemoryGraphStore.SeedAsync"/> applies no
    /// faintness bound of its own, so a sufficiently large <c>limit</c> is a full scope scan, oldest entries
    /// included.</para>
    /// <para><b>That path is EXACT on BOTH age axes, not just one.</b> A store tracks the per-edge age
    /// primitives too (<see cref="GraphNode.StrengthAgeSample"/>), so <see cref="ResolvedStrengthAge"/>
    /// projects the connection axis in the installed policy's own unit and no connected entry is exempt from
    /// deletion. Still deliberately more expensive than the cheap path: pruning is periodic maintenance, not
    /// a hot path.</para>
    /// </remarks>
    public async Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The caller's floor, else the engine's CONFIGURED one — `GraphMemoryOptions.MinRetrievability` is
        // "the retrievability below which PruneAsync may REMOVE an entry", and design §5.7 gives it to this
        // method alone. A floor of 0 is the opt-out: retrievability is never below zero.
        var floorValue = minRetrievability ?? _options.MinRetrievability;

        if (!_agePolicies.Any(c => c.Kind == MemoryAgeKind.Derivable))
            return await PruneInStoreAsync(taskKey, scope, floorValue, olderThan, ct).ConfigureAwait(false);

        var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
        var candidates = await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false);

        var doomed = candidates
            .Where(n => n.Grade != MemoryGrade.Authoritative) // never eligible — retrievability fixed at 1
            .Where(n =>
                // both age axes are now resolved through the installed policies (ResolvedState), so a
                // connected entry's boost is computed in the same unit as its age and the retrievability
                // criterion is exact for it — no connected-entry carve-out remains
                (floorValue > 0 && Retrievability(n) < floorValue) ||
                (createdBefore is DateTimeOffset before && n.CreatedAt < before))
            .ToList();

        if (doomed.Count == 0) return 0;

        var count = await store.DeleteAsync(Name, [.. doomed.Select(n => n.Id)], ct).ConfigureAwait(false);
        await RemoveVectorsAsync(doomed, ct).ConfigureAwait(false);
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
        if (vectors is null)
            return await store.PruneAsync(Name, taskKey, scope, cutoff, olderThan, ct).ConfigureAwait(false);

        var before = await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false);

        var count = await store.PruneAsync(Name, taskKey, scope, cutoff, olderThan, ct).ConfigureAwait(false);
        if (count == 0) return 0;

        var surviving = (await store.SeedAsync(Name, taskKey, scope, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false)).Select(n => n.Id).ToHashSet();

        await RemoveVectorsAsync([.. before.Where(n => !surviving.Contains(n.Id))], ct).ConfigureAwait(false);
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
        await ForgetVectorsAsync(taskKey, scope, ct).ConfigureAwait(false);
        await store.ForgetAsync(Name, taskKey, scope, ct).ConfigureAwait(false);
    }

    /// <summary>Drop the similarity-index collections a <see cref="ForgetAsync"/> is erasing.
    /// <para><b>Addresses are DERIVED from the nodes, never matched by prefix.</b> A collection name embeds
    /// the task and the scope with a separator either may legitimately contain, so a prefix sweep for task
    /// <c>"t"</c> also matches task <c>"t|x"</c>'s collections — and over-deleting is the one direction a
    /// removal must never err in (<see cref="IPrunableMemory.PruneAsync"/>'s own remarks). Deriving is also
    /// why this needs no <see cref="IListableVectorStore"/>: that member is optional, and a consent
    /// withdrawal must not degrade to whatever a BYO index happens to be able to enumerate.</para>
    /// <para><b>What deriving does NOT cover</b>, since a defence usually reads as covering the whole
    /// method: a scope whose nodes are ALREADY gone names no collection here, so an orphan left by an
    /// earlier partial failure survives an unscoped forget. Naming the scope clears it — that path drops the
    /// whole collection rather than the ids still present.</para></summary>
    private async Task ForgetVectorsAsync(string taskKey, string? scope, CancellationToken ct)
    {
        // `vectors`, not `Enriches`: an engine whose embedder was removed still has to erase what an earlier
        // configuration indexed.
        if (vectors is null) return;

        if (scope is not null)
        {
            // A named scope is an exact address, so dropping the whole collection also clears any orphan in
            // it — strictly more complete than deleting the ids that happen to still exist.
            await vectors.RemoveCollectionAsync(VectorCollection(taskKey, scope), ct).ConfigureAwait(false);
            return;
        }

        var nodes = await store.SeedAsync(Name, taskKey, scope: null, query: null, limit: int.MaxValue, ct)
            .ConfigureAwait(false);

        foreach (var each in nodes.Select(n => n.Scope).Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            await vectors.RemoveCollectionAsync(VectorCollection(taskKey, each), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Delete the vector payloads of nodes a PRUNE removed, keyed on each node's OWN task and scope
    /// so a scope-spanning call reaches every collection it touched.
    /// <para><b>BEST-EFFORT, unlike <see cref="ForgetVectorsAsync"/>, and the asymmetry runs all the way
    /// through.</b> A prune clears the index AFTER the store, so by the time this can fail the nodes are
    /// already gone: throwing would lose the COUNT and leave the caller unable to tell that the prune had in
    /// fact succeeded. The honest degradation is an orphaned vector — which is precisely the state every
    /// prune left behind before this existed — and <see cref="IPrunableMemory.PruneAsync"/> is best-effort
    /// capacity management by contract, where "removing fewer entries than hoped is a deferred cost rather
    /// than a defect". A FORGET is the opposite on every axis: index first, and a failure must surface.</para>
    /// <para>Cancellation still propagates: that is the caller leaving, not a store fault.</para></summary>
    private async Task RemoveVectorsAsync(IReadOnlyList<GraphNode> removed, CancellationToken ct)
    {
        if (vectors is null || removed.Count == 0) return;

        try
        {
            foreach (var node in removed)
            {
                ct.ThrowIfCancellationRequested();
                await vectors
                    .DeleteAsync(VectorCollection(node.TaskKey, node.Scope),
                        node.Id.ToString(CultureInfo.InvariantCulture), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "similarity index cleanup failed for {Engine}; {Count} entr(ies) were removed from the store "
                + "and their vectors remain as orphans", Name, removed.Count);
        }
    }

    /// <summary>This engine's similarity-index address for one (task, scope).
    /// <para>Spelled once because four sites read it — enrichment's write, the scoped semantic seed, and both
    /// removal verbs — and a fifth (<see cref="AcrossScopesAsync"/>) builds a PREFIX over the same shape. Two
    /// spellings of one address is how a removal quietly misses the collection a write created.</para></summary>
    private string VectorCollection(string taskKey, string scope) => $"{Name}|{taskKey}|{scope}";

    /// <summary>Clamp one component of a composed <see cref="MemoryTick"/> to something a store may keep.
    /// <para><b><see cref="Math.Max(double,double)"/> is not a finiteness guard</b> — it PROPAGATES
    /// <see cref="double.NaN"/> per IEEE 754, the same trap <see cref="MemorySignals.Difficulty"/> records for
    /// <see cref="Math.Clamp(double,double,double)"/>. Both components are PERSISTED, so a bare <c>Max</c> was
    /// the one place a BYO <see cref="IMemoryAgePolicy.Advance"/> could poison the store permanently: on
    /// Postgres a <c>NaN</c> position adds into the engine's running total and every LATER entry then reports
    /// a non-finite age, while SQLite refuses the bind and fails loudly — so the two backends disagreed about
    /// a poisoned write, which is worse than either answer alone.</para>
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

    /// <summary>The ONE tick <see cref="RememberAsync"/> hands the store — <see cref="ResolvedAge"/>'s
    /// write-time counterpart.
    /// <para><b>Encoding composes across EVERY registered policy, always</b> — it is multiplied once into
    /// this write's <c>InitialStability</c> and never re-derived anywhere else, so composing every policy's
    /// own judgment can never double-count, unlike Position.</para>
    /// <para><b>Position composes across the ACCUMULATING policy alone when one is registered — never summed
    /// with a Derivable policy's own tick.</b> A Derivable policy's contribution is already recorded EXACTLY,
    /// unconditionally, in the three primitives (<see cref="GraphNode.AgeSample"/>) — writing its tick into
    /// the accumulator TOO is precisely the double count <see cref="ResolvedAge"/> would then read a second
    /// time. With no Accumulating policy registered (an all-Derivable configuration), Position composes
    /// across every registered tick — <see cref="ResolvedAge"/> never reads <see cref="GraphNode.Age"/> in
    /// that configuration at all, so there is nothing to double-count against.</para>
    /// <para>Every registered policy's <see cref="IMemoryAgePolicy.Advance"/> is still called, unconditionally
    /// — a STATEFUL policy (burst detection, "since this engine's own last write") needs its own per-engine
    /// bookkeeping kept current regardless of whether its tick feeds the accumulator.</para></summary>
    private MemoryTick AdvanceAgePolicies(MemoryWrite write)
    {
        var allTicks = new MemoryTick[_agePolicies.Count];
        for (var i = 0; i < _agePolicies.Count; i++) allTicks[i] = _agePolicies[i].Advance(write, Name);

        var encoding = _ageComposition.Advance(allTicks).Encoding;

        var accumulatingTicks = new List<MemoryTick>(allTicks.Length);
        for (var i = 0; i < _agePolicies.Count; i++)
            if (_agePolicies[i].Kind == MemoryAgeKind.Accumulating) accumulatingTicks.Add(allTicks[i]);

        var position = accumulatingTicks.Count > 0
            ? _ageComposition.Advance(accumulatingTicks).Position
            : _ageComposition.Advance(allTicks).Position;

        return new MemoryTick(position, encoding);
    }

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
    private MemoryDecayState ResolvedState(GraphNode node) =>
        node.DecayState with { Age = ResolvedAge(node), StrengthAge = ResolvedStrengthAge(node) };

    /// <summary>The composed CONNECTION age every registered <see cref="IMemoryAgePolicy"/> resolves
    /// <paramref name="node"/> to — <see cref="ResolvedAge"/>'s exact counterpart, honouring each policy's own
    /// <see cref="IMemoryAgePolicy.Kind"/> the same way: a <see cref="MemoryAgeKind.Derivable"/> policy
    /// projects from <see cref="GraphNode.StrengthAgeSample"/>, an <see cref="MemoryAgeKind.Accumulating"/>
    /// one reads the store's own <see cref="GraphNode.StrengthAge"/> accumulator.
    /// <para><b>The engine's shipped default (one <see cref="BurstDampenedAgePolicy"/>, Accumulating)
    /// therefore composes exactly <see cref="GraphNode.StrengthAge"/>, the store's own accumulator</b> —
    /// the same guarantee <see cref="ResolvedAge"/> makes for the other axis.</para></summary>
    private double ResolvedStrengthAge(GraphNode node) =>
        _ageComposition.Age([.. _agePolicies.Select(c =>
            c.Kind == MemoryAgeKind.Derivable ? c.Age(node.StrengthAgeSample) : node.StrengthAge)]);

    /// <summary>The composed age every registered <see cref="IMemoryAgePolicy"/> resolves <paramref name="node"/>
    /// to, honouring each policy's own declared <see cref="IMemoryAgePolicy.Kind"/> rather than reading
    /// <see cref="GraphNode.Age"/> unconditionally:
    /// <list type="bullet">
    /// <item><see cref="MemoryAgeKind.Derivable"/> projects from the primitives
    /// (<see cref="GraphNode.AgeSample"/>) — a pure function, so this is EXACTLY the age a fresh
    /// <see cref="IMemoryAgePolicy.Advance"/>-replay would have produced.</item>
    /// <item><see cref="MemoryAgeKind.Accumulating"/> reads <see cref="GraphNode.Age"/>, the store's own
    /// <c>Advance</c>-driven accumulator — genuinely non-recomputable from the primitives (see
    /// <see cref="BurstDampenedAgePolicy.Age"/>'s own remarks), so the stored value is read as it
    /// stands.</item>
    /// </list>
    /// <b>The engine's shipped default (one <see cref="BurstDampenedAgePolicy"/>, Accumulating) therefore
    /// composes a one-element list containing exactly <see cref="GraphNode.Age"/> — the store's own
    /// accumulator, unchanged.</b></summary>
    private double ResolvedAge(GraphNode node) =>
        _ageComposition.Age([.. _agePolicies.Select(c =>
            c.Kind == MemoryAgeKind.Derivable ? c.Age(node.AgeSample) : node.Age)]);

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
        var age = ResolvedEdgeAge(neighbour);
        return age <= 0 || halfLife <= 0
            ? neighbour.EdgeWeight
            : neighbour.EdgeWeight * Math.Pow(2, -age / halfLife);
    }

    /// <summary>The composed TRAVERSAL age every registered <see cref="IMemoryAgePolicy"/> resolves this edge
    /// to — <see cref="ResolvedAge"/>'s and <see cref="ResolvedStrengthAge"/>'s counterpart for the edge that
    /// actually reached a neighbour, honouring each policy's <see cref="IMemoryAgePolicy.Kind"/> the same
    /// way.</summary>
    private double ResolvedEdgeAge(GraphNeighbour neighbour) =>
        _ageComposition.Age([.. _agePolicies.Select(c =>
            c.Kind == MemoryAgeKind.Derivable ? c.Age(neighbour.EdgeAgeSample) : neighbour.EdgeAge)]);

    /// <summary>Cosine similarity of the query against the vector store, per node id. Empty unless an
    /// embedder, a vector store and <see cref="GraphMemoryOptions.SemanticSeedK"/> are all present.
    /// <para><b>A null <see cref="MemoryQuery.Scope"/> spans the task's scopes</b>, so this half agrees with
    /// the LEXICAL half about what an unscoped recall means. It did not until an adopter measured it: a write
    /// always names a scope, so the literal collection <c>{Name}|{task}|</c> a null scope produced could
    /// never exist, and an unscoped recall — the common case — was silently unimproved while the same query
    /// answered when a scope was named. Spanning needs <see cref="IListableVectorStore"/>; a store without it
    /// yields nothing here, exactly as before.</para></summary>
    private async Task<Dictionary<long, double>> SemanticScoresAsync(MemoryQuery query, CancellationToken ct)
    {
        if (!Enriches || _options.SemanticSeedK <= 0 || string.IsNullOrWhiteSpace(query.Query)) return [];

        // Best-effort, and the try/catch is the whole point. This runs AFTER store.SeedAsync has already
        // produced the lexical seeds, so letting an embedder or vector-store fault propagate would throw out
        // of GatherAsync into RecallAsync's catch and return MemoryRecall.Empty — discarding good seeds and
        // reporting a transient outage as "nothing matched", which a caller cannot tell apart from a genuine
        // miss. Design §5.7.0: enrichment's failure degrades QUALITY, never CORRECTNESS. The WRITE path's
        // twin (SearchAsync's own guard) has always worked this way; the read path did not.
        // Cancellation is never swallowed — that is the caller leaving, not an enrichment fault.
        IReadOnlyList<VectorMatch> near;
        try
        {
            var vector = await embedder!.EmbedAsync(query.Query, ct).ConfigureAwait(false);
            near = query.Scope is null
                ? await AcrossScopesAsync(vector, query.TaskKey, ct).ConfigureAwait(false)
                : await vectors!
                    .SearchAsync(VectorCollection(query.TaskKey, query.Scope), vector, _options.SemanticSeedK, ct)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "semantic seeding failed for {Engine}; falling back to the lexical seeds", Name);
            return [];
        }

        var scores = new Dictionary<long, double>(near.Count);
        foreach (var match in near)
            if (long.TryParse(match.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                scores[id] = Math.Clamp(match.Score, 0, 1);
        return scores;
    }

    /// <summary>Search every scope this engine holds under <paramref name="taskKey"/> and merge — one search
    /// per scope, each already bounded by <c>SemanticSeedK</c>, so the global top-k is a subset of the
    /// per-scope top-k's and merging afterwards misses nothing.</summary>
    /// <remarks>Ties break by id, ordinally. Without it two equally-scoring nodes in different scopes swap
    /// places between runs and one silently drops out at the k boundary — and here those scores become
    /// RANKING input, so the arbitrariness would land in what a recall returns.</remarks>
    private async Task<IReadOnlyList<VectorMatch>> AcrossScopesAsync(float[] vector, string taskKey,
        CancellationToken ct)
    {
        if (vectors is not IListableVectorStore listable) return [];

        var prefix = $"{Name}|{taskKey}|";
        var merged = new List<VectorMatch>();
        foreach (var collection in await listable.ListCollectionsAsync(prefix, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            merged.AddRange(await vectors!
                .SearchAsync(collection, vector, _options.SemanticSeedK, ct).ConfigureAwait(false));
        }

        return [.. merged
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(_options.SemanticSeedK)];
    }

    private async Task<List<(GraphNode Node, int Hop)>> GatherAsync(MemoryQuery query, int limit,
        CancellationToken ct)
    {
        // no faintness bound: the store returns candidates grade-first, then most-recently-used, and the
        // count is the only limit, so nothing is excluded for having decayed — burial happens by rank, above
        var candidates = limit * Math.Max(1, _options.CandidateMultiplier);

        var seeds = await store.SeedAsync(Name, query.TaskKey, query.Scope, query.Query, candidates, ct)
            .ConfigureAwait(false);

        var found = new List<(GraphNode Node, int Hop)>(seeds.Select(n => (n, 0)));
        var seen = seeds.Select(n => n.Id).ToHashSet();

        var similar = await SemanticScoresAsync(query, ct).ConfigureAwait(false);
        if (similar.Count > 0)
        {
            for (var i = 0; i < found.Count; i++)
                if (similar.TryGetValue(found[i].Node.Id, out var boost) && boost > found[i].Node.Relevance)
                    found[i] = (found[i].Node with { Relevance = boost }, found[i].Hop);

            foreach (var pair in similar)
            {
                if (!seen.Add(pair.Key)) continue;
                var node = await store.GetAsync(Name, pair.Key, ct).ConfigureAwait(false);
                if (node is not null) found.Add((node with { Relevance = pair.Value }, 0));
            }
        }

        foreach (var id in await SubjectSeedsAsync(query, ct).ConfigureAwait(false))
        {
            if (!seen.Add(id)) continue;   // already a lexical or semantic hit — nothing to add
            var node = await store.GetAsync(Name, id, ct).ConfigureAwait(false);
            // No fabricated Relevance: the store reports how well the entry's own TEXT matched, and a subject
            // match says something different — that the entry is ABOUT what was asked. Handing it a score on
            // the text axis would let it displace entries that genuinely matched; entering as a candidate lets
            // it compete on retrievability and degree, which is what it actually has.
            if (node is not null) found.Add((node, 0));
        }

        var frontier = seen.ToList();

        for (var hop = 1; hop <= _options.Hops && frontier.Count > 0; hop++)
        {
            ct.ThrowIfCancellationRequested();
            var neighbours = await store
                .NeighboursAsync(Name, frontier, candidates, ct).ConfigureAwait(false);

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

        return found;
    }

    /// <summary>The entries recorded under whichever SUBJECTS this query names, newest first per subject.
    ///
    /// <para><b>Why the query is matched against the handle list rather than the handles searched for.</b>
    /// A handle is stored as an index, not as text, so there is nothing to search; the only lookup the store
    /// offers is by an exact normalized handle
    /// (<see cref="IMemoryGraphStore.NodesBySubjectAsync"/>). Reading the handles in use and asking which
    /// ones the query names is what turns that exact lookup into something a free-text recall can use, and
    /// <see cref="MemorySubject.Matches"/> owns the per-script rule for "names".</para>
    ///
    /// <para>BEST-EFFORT, like the semantic seed beside it and for the same reason: this runs after the
    /// lexical seeds exist, so letting a fault propagate would discard them and report a storage problem as
    /// "nothing matched". Cancellation is never swallowed.</para></summary>
    private async Task<IReadOnlyList<long>> SubjectSeedsAsync(MemoryQuery query, CancellationToken ct)
    {
        if (_options.SubjectSeedK <= 0 || _options.SubjectSeedScan <= 0 ||
            string.IsNullOrWhiteSpace(query.Query))
            return [];

        try
        {
            var known = await store
                .KnownSubjectsAsync(Name, query.TaskKey, query.Scope, _options.SubjectSeedScan, ct)
                .ConfigureAwait(false);

            var ids = new List<long>();
            var seen = new HashSet<long>();
            foreach (var subject in known)
            {
                if (!MemorySubject.Matches(query.Query, subject)) continue;
                var found = await store.NodesBySubjectAsync(Name, query.TaskKey, query.Scope,
                    MemorySubject.Normalize(subject), _options.SubjectSeedK, ct).ConfigureAwait(false);
                foreach (var id in found)
                    if (seen.Add(id)) ids.Add(id);
            }

            return ids;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "subject seeding failed for {Engine}; recalling on the other seeds", Name);
            return [];
        }
    }

    /// <summary>Asks the verifier which of these actually answered the query, fail-open in every direction.
    /// <para><b>Any failure yields <see cref="Lyntai.Memory.Verification.MemoryVerification.NoOpinion"/>,
    /// never "nothing was relevant".</b> Those two are opposite instructions to the caller above: no opinion
    /// reinforces normally, while an empty judged verdict teaches the engine the recall failed. Collapsing
    /// them would make a model outage silently unlearn the whole corpus — the single most damaging thing a
    /// best-effort seam could do here.</para></summary>
    private async Task<Lyntai.Memory.Verification.MemoryVerification> VerifyAsync(
        string queryText, IReadOnlyList<RankedMemory> scored, CancellationToken ct)
    {
        if (verification is null) return Lyntai.Memory.Verification.MemoryVerification.NoOpinion;

        try
        {
            var request = new Lyntai.Memory.Verification.MemoryVerificationRequest(
                queryText,
                [.. scored.Select(x => new Lyntai.Memory.Verification.MemoryVerificationCandidate(
                    x.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture),
                    x.Candidate.Node.Headline))]);

            return await verification.VerifyAsync(request, ct).ConfigureAwait(false)
                   ?? Lyntai.Memory.Verification.MemoryVerification.NoOpinion;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "memory verification failed for {Engine}; reinforcing what was returned", Name);
            return Lyntai.Memory.Verification.MemoryVerification.NoOpinion;
        }
    }

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
    /// ranking or pruning. <see cref="MemoryReviewWrite.Grade"/> is
    /// <see cref="IMemoryRetrievabilityPolicy.DerivedGrade"/>'s return on the SAME <c>pre</c> state handed to
    /// <see cref="IMemoryRetrievabilityPolicy.Reinforce"/>, never re-derived afterward.</para>
    /// <para>One <see cref="Guid"/> per call, shared across every node it touches, so a fitter can tell a
    /// group of rows came from the SAME recall.</para>
    /// <para><b>Logging gets its OWN try/catch, nested inside the one below</b> — the outer is this method's
    /// best-effort promise, the inner makes logging best-effort at a stricter grain, so a failed log write
    /// does not cost the touches that already succeeded.</para></summary>
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
                reinforceable = [.. reinforceable.Take(Math.Max(0, cap))];

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

            // THE TWO EFFECTS, SEPARATED HERE (`TASKS.md` Part 64). A touch resets the entry's age on every
            // scale AND writes back the grown stability — one store round-trip, two effects that pull in
            // opposite directions. The age reset keeps a rarely-queried critical fact alive; the growth
            // entrenches whatever the ranker already returned, because nothing here observes whether the
            // return was CORRECT. Which of them applies is the ENGINE's call, so it is decided at this line
            // rather than left to one curve's private constant (`DsrOptions.ReinforceGain`), which a
            // consumer's own policy would not have.
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

            // `None` skips the call outright rather than writing a no-op touch: the store cannot be asked to
            // hold the age still, so a touch with everything else suppressed would STILL reset it.
            if (touches.Count > 0 && resetAge)
                await store.TouchAsync(Name, touches, ct).ConfigureAwait(false);

            if (_options.LogReviews && reviews.Count > 0)
            {
                try
                {
                    await store.RecordReviewsAsync(Name, reviews, _options.ReviewLogCap, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "review log write failed for {Engine}; continuing without a logged row", Name);
                }
            }

            var top = nodes.Take(Math.Max(0, _options.CoActivationCap)).Select(n => n.Id).ToList();
            for (var i = 0; i < top.Count; i++)
                for (var j = i + 1; j < top.Count; j++)
                    await store.LinkAsync(Name, top[i], top[j], null, 1, symmetric: true, ct)
                        .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "graph reinforcement failed for {Engine}; returning hits without learning", Name);
        }
    }
}
