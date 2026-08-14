using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Memory;

/// <summary>Collects the members of one named engine inside <c>AddMemoryEngine("name", e =&gt; …)</c>.
/// <c>Use*</c> order is the render order; authoritative material renders first regardless of position.
/// <para>Members are collected as FACTORIES, not instances — nothing is constructed until the container is
/// built, which is what lets a missing backing store surface as a startup failure naming the store rather
/// than as a permanently empty memory section.</para></summary>
public sealed class MemoryEngineBuilder
{
    private readonly List<MemberSpec> _members = [];

    internal MemoryEngineBuilder(string name) => Name = name;

    internal string Name { get; }

    internal MemoryCompositionOptions Composition { get; private set; } = new();

    internal bool HasMembers => _members.Count > 0;

    private sealed record MemberSpec(string Label, Func<IServiceProvider, string, IMemoryEngine> Build);

    /// <summary>Draw on the keyword <see cref="IMemoryStore"/>. Associative.</summary>
    /// <param name="label">Distinguishes several members of the same kind; becomes the member's
    /// hierarchical name.</param>
    public MemoryEngineBuilder UseLexical(string label = "lexical")
    {
        _members.Add(new MemberSpec(label, (sp, full) => new LexicalMemoryEngine(
            full, Required<IMemoryStore>(sp), sp.GetService<ILogger<LexicalMemoryEngine>>())));
        return this;
    }

    /// <summary>Draw on meaning-based <see cref="ISemanticMemory"/>. Associative. Needs an embedder — see
    /// <c>AddSemanticMemory</c>.</summary>
    /// <param name="label">Distinguishes several members of the same kind.</param>
    public MemoryEngineBuilder UseSemantic(string label = "semantic")
    {
        _members.Add(new MemberSpec(label, (sp, full) => new SemanticMemoryEngine(
            full, Required<ISemanticMemory>(sp), logger: sp.GetService<ILogger<SemanticMemoryEngine>>())));
        return this;
    }

    /// <summary>Draw on the operator-curated catalog. AUTHORITATIVE — its entries render as exact facts and
    /// never decay.
    /// <para><paramref name="label"/> defaults to <paramref name="kind"/>, because drawing on two catalog
    /// sections is the ordinary case: <c>UseCurated("glossary").UseCurated("style")</c> yields
    /// <c>engine/glossary</c> and <c>engine/style</c> with nothing further to say. A fixed default would
    /// make those two collide and force a label on the common path.</para></summary>
    /// <param name="kind">The catalog section to read and write.</param>
    /// <param name="label">Overrides the member's name; defaults to <paramref name="kind"/>.</param>
    public MemoryEngineBuilder UseCurated(string kind = "memory", string? label = null)
    {
        _members.Add(new MemberSpec(label ?? kind, (sp, full) => new CuratedMemoryEngine(
            full, Required<ICuratedMemoryStore>(sp), kind, sp.GetService<ILogger<CuratedMemoryEngine>>())));
        return this;
    }

    /// <summary>Draw on the decaying, linked graph store — the engine that forgets what goes unused,
    /// connects what is recalled together, and returns headlines that expand on demand. Holds BOTH grades,
    /// so it can carry exact facts alongside recalled ones.
    /// <para>The options are taken BY VALUE rather than through a configure callback:
    /// <see cref="GraphMemoryOptions"/> is an init-only record, so a callback could not mutate it and would
    /// silently do nothing. Write <c>UseGraph(new GraphMemoryOptions { Hops = 3 })</c>.</para></summary>
    /// <param name="options">Retrieval knobs and decay constants; null takes the defaults.</param>
    /// <param name="label">Distinguishes several members of the same kind.</param>
    /// <param name="ranking">THIS engine's own ranking policy, overriding the container's registered
    /// <see cref="IMemoryRankingPolicy"/> for this named engine alone; null keeps the container registration
    /// as the default, so an engine that names nothing here behaves exactly as before this parameter existed.
    /// Every other named engine, and the container registration itself, is unaffected.</param>
    /// <param name="namedRankingPolicies">Alternates this engine exposes for a per-call
    /// <see cref="MemoryQuery.RankingPolicyName"/> override; null or empty exposes none. Scoped to THIS
    /// engine alone — a name meaningful on one named engine is simply unknown on another, and each engine's
    /// query throws on a name it does not itself recognize rather than consulting any other engine's
    /// catalog.</param>
    /// <param name="policy">THIS engine's own forgetting curve, overriding the container's registered
    /// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/> for this named engine alone; null
    /// keeps the container registration as the default (or <see cref="DsrRetrievability"/> when nothing is
    /// registered — <c>AddMemoryEngine</c>'s own <c>TryAdd</c>), so an engine that names nothing here behaves
    /// exactly as before this parameter existed. Retention modulation applies either way: whatever curve is
    /// resolved is wrapped in <see cref="ModulatedRetrievability"/> over the registered
    /// <see cref="IMemoryRetentionPolicy"/> collection, so naming a curve here selects the CURVE and changes
    /// nothing else about the engine.
    /// <para>Appended LAST on purpose (<c>docs/DECISIONS.md</c> D50): inserting it beside the other policy
    /// parameters would silently re-bind every positional caller. Ranking was already per-engine and the
    /// curve was not, for no recorded reason — these are the subsystem's only two SINGULAR seams (D48), so
    /// they now have the same selection story.</para></param>
    /// <param name="annotation">THIS engine's own <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/>
    /// — what each written fact is ABOUT, so entries concerning the same entity become connected. Null falls
    /// back to the container registration, and nothing registered means no annotation at all: the model-free
    /// floor every engine has until someone opts in.
    /// <para>Appended LAST for the same reason <c>policy</c> was: inserting it beside the other parameters
    /// would silently re-bind every positional caller.</para></param>
    /// <param name="verification">THIS engine's own
    /// <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> — which of a recall's candidates
    /// actually ANSWERED the query, so a buried answer can be promoted past the limit and reinforcement
    /// follows evidence rather than the ranker's own prior. Null falls back to the container registration,
    /// and nothing registered means the ranking policy's order stands unreviewed: the model-free floor.
    /// <para>Appended LAST for the same reason the two parameters above it were.</para></param>
    public MemoryEngineBuilder UseGraph(GraphMemoryOptions? options = null, string label = "graph",
        IMemoryRankingPolicy? ranking = null,
        IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies = null,
        IMemoryRetrievabilityPolicy? policy = null,
        Lyntai.Memory.Annotation.IMemoryAnnotationPolicy? annotation = null,
        Lyntai.Memory.Verification.IMemoryVerificationPolicy? verification = null)
    {
        var resolved = options ?? new GraphMemoryOptions();
        _members.Add(new MemberSpec(label, (sp, full) => BuildGraph(
            sp, full, Required<IMemoryGraphStore>(sp), resolved,
            ranking, namedRankingPolicies, policy, annotation, verification)));
        return this;
    }

    /// <summary>
    /// The ONE place a <see cref="GraphMemoryEngine"/> is constructed from a container.
    ///
    /// <para><b>Why it exists — a shipped defect, not tidiness.</b> <see cref="UseGraph"/> and
    /// <see cref="UseBestAvailable"/> each had their own copy of this argument list, and the copies had
    /// drifted: the one-line path (<c>AddMemory()</c>, which this library documents as "the one-line path,
    /// and deliberately so") passed neither <c>annotation:</c> nor <c>verification:</c>, so both fell to the
    /// engine's model-free floor. A consumer calling <c>AddMemory().AddMemoryVerification()</c> got a
    /// registered policy that never ran — silently, with no throw and no missing result, only worse recall —
    /// while the identical registration behind <c>AddMemoryEngine(…, e =&gt; e.UseGraph())</c> worked. That
    /// mattered most for the seam whose own registration doc calls it <b>the single largest recall-quality
    /// lever the subsystem has</b>.</para>
    ///
    /// <para>Two construction sites for one engine is the defect's actual shape, so the fix is one site
    /// rather than a second copy kept in step by memory: a parameter added to the engine now reaches BOTH
    /// paths by construction. Pinned by
    /// <c>GraphMemoryWiringTests.The_one_line_AddMemory_path_honours_a_registered_annotation_and_verification_policy</c>.</para>
    ///
    /// <para>Every argument that is a DI collection is read here unconditionally — registering a retention,
    /// age or salience dimension is a registration, never an edit to this method. The five OVERRIDE
    /// parameters are the per-engine selections <see cref="UseGraph"/> exposes and
    /// <see cref="UseBestAvailable"/>, having no configuration surface of its own, leaves null: null means
    /// "take the container registration", which is exactly the fallback each one documents.</para>
    /// </summary>
    private static GraphMemoryEngine BuildGraph(IServiceProvider sp, string full, IMemoryGraphStore store,
        GraphMemoryOptions? options = null,
        IMemoryRankingPolicy? ranking = null,
        IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies = null,
        IMemoryRetrievabilityPolicy? policy = null,
        Lyntai.Memory.Annotation.IMemoryAnnotationPolicy? annotation = null,
        Lyntai.Memory.Verification.IMemoryVerificationPolicy? verification = null) =>
        new(
            full, store,
            options,
            // the retention collection is a DI collection: adding a retention dimension is a registration,
            // never an edit here
            //
            // No `?? new …()` fallback here (2026-08-10, fsrs-properly plan Task 1, deleting
            // HalfLifeRetrievability): AddMemoryEngine's own TryAddSingleton<IMemoryRetrievabilityPolicy>
            // always runs before configure(engineBuilder) ever reaches this lambda, so GetRequiredService
            // cannot actually throw here — it documents that guarantee instead of restating a second,
            // now-pointless default that named a curve which no longer exists.
            //
            // PER-ENGINE selection (docs/DECISIONS.md D50), substituted at the INNER resolution exactly the
            // way `ranking` below substitutes at its own: an explicit `policy` argument is THIS engine's own
            // curve and wins outright, and `?? `'s short-circuit means GetRequiredService is not even
            // consulted then. Passing it here rather than as the engine's `policy:` keeps the modulation
            // wrapper on BOTH paths — a consumer selecting a curve is choosing a curve, not opting out of
            // the retention policies every other graph engine gets. `policy: null` therefore resolves
            // exactly as it did before this parameter existed.
            policy: new ModulatedRetrievability(
                policy ?? sp.GetRequiredService<IMemoryRetrievabilityPolicy>(),
                sp.GetServices<IMemoryRetentionPolicy>(),
                sp.GetService<IMemoryRetentionCompositionPolicy>()),
            // age is a DI collection too (2026-08-10 memory-policy-seams plan, Task 3): registering an
            // IMemoryAgePolicy adds a coexisting age dimension, never replaces the engine's own default
            // (a burst-damped per-write age policy) — GetServices returns empty when nothing is registered, and
            // the engine's own normalization falls back to that default exactly as GetService's null used to
            agePolicies: sp.GetServices<IMemoryAgePolicy>(),
            ageComposition: sp.GetService<IMemoryAgeCompositionPolicy>(),
            logger: sp.GetService<ILogger<GraphMemoryEngine>>(),
            // similarity enrichment turns itself on when both are present, and is simply absent otherwise
            embedder: sp.GetService<Lyntai.Embeddings.IEmbedder>(),
            vectors: sp.GetService<IVectorStore>(),
            // salience is a DI collection too, same reasoning as agePolicies above
            saliencePolicies: sp.GetServices<IMemorySaliencePolicy>(),
            salienceComposition: sp.GetService<IMemorySalienceCompositionPolicy>(),
            // PER-ENGINE selection (2026-08-10 memory-policy-seams plan, Task 6): an explicit `ranking`
            // argument here is THIS engine's own choice and wins outright; null falls back to the container
            // registration exactly as before this parameter existed — "container registration stays the
            // default for engines that name nothing".
            ranking: ranking ?? sp.GetService<IMemoryRankingPolicy>(),
            namedRankingPolicies: namedRankingPolicies,
            // PER-ENGINE selection, the same shape as `ranking` and `policy` above. Absent from the
            // container AND unnamed here means no annotation and no subject links — the model-free floor,
            // which is what every engine gets until someone opts in.
            annotation: annotation ?? sp.GetService<Lyntai.Memory.Annotation.IMemoryAnnotationPolicy>(),
            // Same per-engine selection story. Absent from the container AND unnamed here means the ranking
            // policy's order stands unreviewed — the model-free floor.
            verification: verification
                ?? sp.GetService<Lyntai.Memory.Verification.IMemoryVerificationPolicy>());

    /// <summary>The zero-configuration member: the graph engine when an <see cref="IMemoryGraphStore"/>
    /// reached the container, the keyword store otherwise. Resolved when the container is BUILT, not when
    /// this is called, because a storage backend may be registered afterwards.
    /// <para>Names no override at all — it has no configuration surface to name one with — so every seam
    /// resolves from the container, which is what <see cref="BuildGraph"/>'s null arguments mean. Before
    /// that method existed this carried its own copy of the argument list and had silently fallen two
    /// parameters behind; see its remarks.</para></summary>
    internal MemoryEngineBuilder UseBestAvailable()
    {
        _members.Add(new MemberSpec("memory", (sp, full) =>
            sp.GetService<IMemoryGraphStore>() is { } graph
                ? BuildGraph(sp, full, graph)
                : new LexicalMemoryEngine(full, Required<IMemoryStore>(sp),
                    sp.GetService<ILogger<LexicalMemoryEngine>>())));
        return this;
    }

    /// <summary>Total characters this engine's composed sections may use.</summary>
    /// <param name="characters">The budget.</param>
    public MemoryEngineBuilder Budget(int characters)
    {
        Composition = Composition with { Budget = characters };
        return this;
    }

    /// <summary>Characters reserved for AUTHORITATIVE material, allocated before any associative content is
    /// admitted.
    /// <para><b>This is an engine-level allocation, not a per-member one</b>, however it reads in the
    /// chain: it covers every authoritative member of the blend together. Writing it after the member it is
    /// meant for is a readability convention only.</para></summary>
    /// <param name="characters">The reserve.</param>
    public MemoryEngineBuilder Reserve(int characters)
    {
        Composition = Composition with { AuthoritativeReserve = characters };
        return this;
    }

    /// <summary>Fail on two members that would share a hierarchical name. Called at CONFIGURE time, so the
    /// mistake surfaces where it was made rather than at the first recall.</summary>
    internal void Validate()
    {
        var duplicate = _members
            .GroupBy(m => m.Label, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Memory engine '{Name}' has two members labelled '{Name}/{duplicate.Key}'. Pass an " +
                "explicit label (e.g. UseCurated(kind: \"style\", label: \"style\")) so every entry's " +
                "reference names exactly one owner.");
    }

    /// <summary>Materialize the engine. Called when the container is built, never at configure time.
    /// <para>ALWAYS a composite, even for one member: returning the bare member would name the engine after
    /// the member ("chat/lexical" rather than "chat") and make it unreachable by the name it was
    /// registered under. One indirection buys uniform naming, routing and <c>Supported</c>.</para></summary>
    internal IMemoryEngine Build(IServiceProvider sp) =>
        new CompositeMemoryEngine(Name, [.. _members.Select(m => m.Build(sp, $"{Name}/{m.Label}"))],
            sp.GetService<ILogger<CompositeMemoryEngine>>());

    private static T Required<T>(IServiceProvider sp) where T : class =>
        sp.GetService<T>() ?? throw new InvalidOperationException(
            $"A memory engine member needs {typeof(T).Name}, which is not registered. Wire a storage " +
            $"backend (e.g. UseSqliteStorage(...)) or register your own {typeof(T).Name} before AddLyntai.");
}
