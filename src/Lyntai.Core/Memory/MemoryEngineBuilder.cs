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

    internal MemoryWriteRouting Routing { get; private set; } = MemoryWriteRouting.FirstCapable;

    internal bool Strict { get; private set; }

    private sealed record MemberSpec(string Label, Func<IServiceProvider, string, IMemoryEngine> Build);

    /// <summary>Draw on the keyword <see cref="IMemoryStore"/>. Associative.</summary>
    /// <param name="label">Distinguishes several members of the same kind; becomes the member's
    /// hierarchical name.</param>
    public MemoryEngineBuilder UseLexical(string label = "lexical")
    {
        _members.Add(new MemberSpec(label, (sp, full) => BuildLexical(sp, full)));
        return this;
    }

    /// <summary>The ONE place a <see cref="LexicalMemoryEngine"/> is constructed, for the reason
    /// <see cref="BuildGraph"/> exists.</summary>
    private static LexicalMemoryEngine BuildLexical(IServiceProvider sp, string full) =>
        new(full, Required<IMemoryStore>(sp), sp.GetService<ILogger<LexicalMemoryEngine>>());

    /// <summary>Draw on meaning-based <see cref="ISemanticMemory"/>. Associative. Needs a vector backend — see
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
    /// <param name="kind">The catalog section to read and write; null reads EVERY section through one
    /// member — still bounded by the query's limit — and makes that member read-only. That is the whole of
    /// what a composite of N curated members bought, for a catalog whose sections need no separate
    /// budgets.</param>
    /// <param name="label">Overrides the member's name; defaults to <paramref name="kind"/>.</param>
    public MemoryEngineBuilder UseCurated(string? kind = "memory", string? label = null) =>
        UseCurated(kind, grade: null, label);

    /// <summary>Draw on the operator-curated catalog, grading each entry from the entry itself — so one
    /// catalog can mix the owner's typed facts with what an assistant inferred while working.
    /// <para>A SEPARATE overload rather than a third optional parameter on the one above: appending a
    /// parameter is source-compatible and NOT binary-compatible, so it breaks a pre-compiled caller of the
    /// old signature while an overload breaks nobody. That is also why <paramref name="grade"/> comes
    /// second — it is the argument this overload exists for, and it is what distinguishes the two.</para></summary>
    /// <param name="kind">The catalog section, or null to read every one (read-only; see the overload
    /// above).</param>
    /// <param name="grade">Grades each entry. See <see cref="CuratedMemoryEngine.Grade"/> for why a
    /// delegate rather than a reserved metadata key, and why the member's
    /// <see cref="IMemoryEngine.Supported"/> does not widen.</param>
    /// <param name="label">Overrides the member's name; defaults to <paramref name="kind"/>.</param>
    public MemoryEngineBuilder UseCurated(string? kind, Func<CuratedMemory, MemoryGrade>? grade,
        string? label = null)
    {
        _members.Add(new MemberSpec(label ?? kind ?? "curated", (sp, full) => new CuratedMemoryEngine(
            full, Required<ICuratedMemoryStore>(sp), kind, sp.GetService<ILogger<CuratedMemoryEngine>>())
        {
            Grade = grade,
        }));
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
    /// <see cref="IMemoryRankingPolicy"/> for this named engine alone; null keeps the container registration.
    /// Every other named engine, and the container registration itself, is unaffected.</param>
    /// <param name="namedRankingPolicies">Alternates this engine exposes for a per-call
    /// <see cref="MemoryQuery.RankingPolicyName"/> override; null or empty exposes none. Scoped to THIS
    /// engine alone — a name meaningful on one named engine is simply unknown on another, and each engine's
    /// query throws on a name it does not itself recognize rather than consulting any other engine's
    /// catalog.</param>
    /// <param name="retrievability">THIS engine's own forgetting curve, overriding the container's registered
    /// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/> for this named engine alone; null
    /// keeps the container registration (<see cref="DsrRetrievability"/> unless a consumer registered
    /// another). Retention modulation applies either way: whatever curve is resolved is wrapped over the
    /// registered <see cref="IMemoryRetentionPolicy"/> collection, so naming a curve here selects the CURVE and
    /// changes nothing else about the engine.</param>
    /// <param name="annotation">THIS engine's own <see cref="Lyntai.Memory.Annotation.IMemoryAnnotationPolicy"/>
    /// — what each written fact is ABOUT, so entries concerning the same entity become connected. Null falls
    /// back to the container registration, and nothing registered means no annotation at all: the model-free
    /// floor every engine has until someone opts in.</param>
    /// <param name="verification">THIS engine's own
    /// <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> — which of a recall's candidates
    /// actually ANSWERED the query, so a buried answer can be promoted past the limit and reinforcement
    /// follows evidence rather than the ranker's own prior. Null falls back to the container registration,
    /// and nothing registered means the ranking policy's order stands unreviewed: the model-free floor.</param>
    /// <param name="seedSources">THIS engine's own retrieval CHANNELS — which
    /// <see cref="Lyntai.Memory.Seeding.IMemorySeedSource"/>s a recall gathers candidates from. Null falls
    /// back to the container registration, which <c>AddMemoryEngine</c> seeds with the lexical and subject
    /// channels and <c>AddMemorySemanticSeeds</c> adds the vector one to. A COLLECTION, so naming it
    /// replaces the container's whole set for this engine: an engine that must not pay for a channel lists
    /// the ones it wants.</param>
    public MemoryEngineBuilder UseGraph(GraphMemoryOptions? options = null, string label = "graph",
        IMemoryRankingPolicy? ranking = null,
        IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies = null,
        IMemoryRetrievabilityPolicy? retrievability = null,
        Lyntai.Memory.Annotation.IMemoryAnnotationPolicy? annotation = null,
        Lyntai.Memory.Verification.IMemoryVerificationPolicy? verification = null,
        IEnumerable<Lyntai.Memory.Seeding.IMemorySeedSource>? seedSources = null)
    {
        var resolved = options ?? new GraphMemoryOptions();
        _members.Add(new MemberSpec(label, (sp, full) => BuildGraph(
            sp, full, Required<IMemoryGraphStore>(sp), resolved,
            ranking, namedRankingPolicies, retrievability, annotation, verification, seedSources)));
        return this;
    }

    /// <summary>
    /// The ONE place a <see cref="GraphMemoryEngine"/> is constructed from a container, so a parameter added
    /// to the engine reaches <see cref="UseGraph"/> and <see cref="UseBestAvailable"/> alike — two copies of
    /// this list drifted once (<c>.claude/knowledge/pitfalls.md</c>, "TWO construction sites"; pinned by
    /// <c>GraphMemoryWiringTests.The_one_line_AddMemory_path_honours_a_registered_annotation_and_verification_policy</c>).
    /// <para>Every DI collection is read unconditionally; the override parameters are the per-engine
    /// selections <see cref="UseGraph"/> exposes, and null means "take the container registration".</para>
    /// </summary>
    private static GraphMemoryEngine BuildGraph(IServiceProvider sp, string full, IMemoryGraphStore store,
        GraphMemoryOptions? options = null,
        IMemoryRankingPolicy? ranking = null,
        IReadOnlyDictionary<string, IMemoryRankingPolicy>? namedRankingPolicies = null,
        IMemoryRetrievabilityPolicy? retrievability = null,
        Lyntai.Memory.Annotation.IMemoryAnnotationPolicy? annotation = null,
        Lyntai.Memory.Verification.IMemoryVerificationPolicy? verification = null,
        IEnumerable<Lyntai.Memory.Seeding.IMemorySeedSource>? seedSources = null) =>
        new(
            full, store,
            options,
            // Required, not defaulted: AddMemoryEngine TryAdds a curve before any engine is built. An
            // explicit per-engine curve (D50) wins, and retention still applies over it.
            retrievability: retrievability ?? sp.GetRequiredService<IMemoryRetrievabilityPolicy>(),
            retentionPolicies: sp.GetServices<IMemoryRetentionPolicy>(),
            retentionComposition: sp.GetService<IMemoryRetentionCompositionPolicy>(),
            // empty when nothing is registered, which the engine reads as its own burst-damped default
            agePolicies: sp.GetServices<IMemoryAgePolicy>(),
            ageComposition: sp.GetService<IMemoryAgeCompositionPolicy>(),
            logger: sp.GetService<ILogger<GraphMemoryEngine>>(),
            // similarity enrichment turns itself on when both are present, and is simply absent otherwise
            providers: sp.GetServices<Lyntai.Inference.IModelProvider>(),
            vectors: sp.GetService<IVectorStore>(),
            saliencePolicies: sp.GetServices<IMemorySaliencePolicy>(),
            salienceComposition: sp.GetService<IMemorySalienceCompositionPolicy>(),
            ranking: ranking ?? sp.GetService<IMemoryRankingPolicy>(),
            namedRankingPolicies: namedRankingPolicies,
            // absent from the container AND unnamed here: no annotation, no subject links (the model-free floor)
            annotation: annotation ?? sp.GetService<Lyntai.Memory.Annotation.IMemoryAnnotationPolicy>(),
            verification: verification
                ?? sp.GetService<Lyntai.Memory.Verification.IMemoryVerificationPolicy>(),
            // the one collection with a per-engine override: which channels a recall pays for is a property
            // of the ENGINE, where a retention or age dimension is one of the deployment
            seedSources: seedSources ?? sp.GetServices<Lyntai.Memory.Seeding.IMemorySeedSource>(),
            // the shared cooldown and admission for enrichment's embedding calls; absent means bare routing
            routing: sp.GetService<Lyntai.Inference.IProviderRouterFactory>());

    /// <summary>The zero-configuration member: the graph engine when an <see cref="IMemoryGraphStore"/>
    /// reached the container, the keyword store otherwise. Resolved when the container is BUILT, not when
    /// this is called, because a storage backend may be registered afterwards.
    /// <para>Names no override, so every seam resolves from the container.</para></summary>
    internal MemoryEngineBuilder UseBestAvailable()
    {
        _members.Add(new MemberSpec("memory", (sp, full) =>
            sp.GetService<IMemoryGraphStore>() is { } graph
                ? BuildGraph(sp, full, graph)
                : BuildLexical(sp, full)));
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
    /// meant for is a readability convention only.</para>
    /// <para>Named for its UNIT, because the neighbouring <see cref="GraphMemoryOptions.AuthoritativeReserve"/>
    /// reserves recall SLOTS and both are reachable from this one chain — a bare <c>Reserve(2)</c> read as
    /// slots would silently mean two characters.</para></summary>
    /// <param name="characters">The reserve, in characters.</param>
    public MemoryEngineBuilder ReserveCharacters(int characters)
    {
        Composition = Composition with { AuthoritativeCharacters = characters };
        return this;
    }

    /// <summary>Send every write to EVERY member that can hold its grade, rather than to the first one.
    /// <para>What it is for: a blend whose members index the same material differently — <c>UseGraph()</c> for
    /// decay and links beside <c>UseSemantic()</c> for meaning. Routing to the first capable member leaves the
    /// second one's store permanently empty, and nothing but a recall-quality measurement can find that.</para>
    /// <para>Costs one write per member (N stores, and an embedding per semantic member), which is why it is
    /// opt-in — and read <see cref="MemoryWriteRouting.EveryCapable"/> before turning it on in a blend that
    /// mixes an authoritative member with an associative one.</para></summary>
    public MemoryEngineBuilder FanOutWrites()
    {
        Routing = MemoryWriteRouting.EveryCapable;
        return this;
    }

    /// <summary>Turn this engine's wiring warnings into a startup failure.
    /// <para>The checks run either way and are logged at Warning; this makes them impossible to miss on a
    /// deployment that has no log to read, or where a seam silently not running is not survivable. It is
    /// per-engine to declare, and container-wide in effect: one strict engine fails the build of the memory
    /// engine factory, so a registered policy nothing consults is reported once rather than per engine.</para>
    /// <para>Off by default: a blend that never receives a write through the engine is a legitimate shape (an
    /// operator-curated catalog is filled through its own store), and this library will not fail an
    /// application's startup over a configuration it cannot be sure is wrong.</para></summary>
    public MemoryEngineBuilder StrictWiring()
    {
        Strict = true;
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
    /// <remarks>The remove policy is resolved from DI when the host registered one, so
    /// <c>services.AddSingleton&lt;IMemoryRemovalPolicy, MyPolicy&gt;()</c> is the whole opt-in. Unregistered, the
    /// composite falls back to <see cref="DefaultMemoryRemovalPolicy"/> — eligibility is a DEPLOYMENT question
    /// (see <see cref="IMemoryRemovalPolicy"/>), so it has to be reachable without editing an engine.</remarks>
    internal IMemoryEngine Build(IServiceProvider sp) =>
        new CompositeMemoryEngine(Name, [.. _members.Select(m => m.Build(sp, $"{Name}/{m.Label}"))],
            sp.GetService<ILogger<CompositeMemoryEngine>>(),
            sp.GetService<IMemoryRemovalPolicy>())
        {
            WriteRouting = Routing,
        };

    private static T Required<T>(IServiceProvider sp) where T : class =>
        sp.GetService<T>() ?? throw new InvalidOperationException(
            $"A memory engine member needs {typeof(T).Name}, which is not registered. Wire a storage " +
            $"backend (e.g. UseSqliteStorage(...)) or register your own {typeof(T).Name} before AddLyntai.");
}
