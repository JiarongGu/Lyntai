using Lyntai.Memory.Engines;
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
    public MemoryEngineBuilder UseGraph(GraphMemoryOptions? options = null, string label = "graph")
    {
        var resolved = options ?? new GraphMemoryOptions();
        _members.Add(new MemberSpec(label, (sp, full) => new GraphMemoryEngine(
            full, Required<IMemoryGraphStore>(sp), resolved, sp.GetService<IRetrievabilityPolicy>(),
            // register an IMemoryClock to change what decay is measured IN; the engine's default is a
            // burst-damped per-write clock
            memoryClock: sp.GetService<IMemoryClock>(),
            logger: sp.GetService<ILogger<GraphMemoryEngine>>(),
            // similarity enrichment turns itself on when both are present, and is simply absent otherwise
            embedder: sp.GetService<Lyntai.Embeddings.IEmbedder>(),
            vectors: sp.GetService<IVectorStore>())));
        return this;
    }

    /// <summary>The zero-configuration member: the graph engine when an <see cref="IMemoryGraphStore"/>
    /// reached the container, the keyword store otherwise. Resolved when the container is BUILT, not when
    /// this is called, because a storage backend may be registered afterwards.</summary>
    internal MemoryEngineBuilder UseBestAvailable()
    {
        _members.Add(new MemberSpec("memory", (sp, full) =>
            sp.GetService<IMemoryGraphStore>() is { } graph
                ? new GraphMemoryEngine(full, graph, policy: sp.GetService<IRetrievabilityPolicy>(),
                    memoryClock: sp.GetService<IMemoryClock>(),
                    logger: sp.GetService<ILogger<GraphMemoryEngine>>(),
                    embedder: sp.GetService<Lyntai.Embeddings.IEmbedder>(),
                    vectors: sp.GetService<IVectorStore>())
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
