namespace Lyntai.Memory;

/// <summary>
/// Resolves memory engines by name, the way <c>IHttpClientFactory</c> resolves clients — the pattern .NET
/// consumers already know.
/// <para><b>Where the analogy stops, stated so nobody assumes otherwise:</b> <c>CreateClient</c> returns a
/// NEW client per call over pooled handlers, whereas an engine is a stateless singleton over its stores, so
/// the same instance comes back every time. Hence <see cref="Get(string)"/> rather than <c>Create</c>.</para>
/// </summary>
public interface IMemoryEngineFactory
{
    /// <summary>The engine registered under <paramref name="name"/>, including a member's hierarchical name
    /// ("project/glossary").</summary>
    /// <param name="name">The engine's name.</param>
    /// <exception cref="KeyNotFoundException">No engine has that name; the message lists the registered
    /// ones.</exception>
    IMemoryEngine Get(string name);

    /// <summary>The default engine — the one named "default", or the only one when exactly one is
    /// registered.</summary>
    /// <exception cref="KeyNotFoundException">Neither holds; the message says which case it was.</exception>
    IMemoryEngine Get();

    /// <summary>Look one up without throwing.</summary>
    /// <param name="name">The engine's name.</param>
    /// <param name="engine">The engine, when found.</param>
    bool TryGet(string name, out IMemoryEngine engine);

    /// <summary>Every registered name, including hierarchical member names.</summary>
    IReadOnlyList<string> Names { get; }
}

/// <summary>The default <see cref="IMemoryEngineFactory"/> over the registered engine collection. A
/// composite's members are indexed alongside it, so a blend and any one of its parts are both addressable
/// without either being constructed twice.</summary>
public sealed class MemoryEngineFactory : IMemoryEngineFactory
{
    private readonly Dictionary<string, IMemoryEngine> _byName;

    /// <summary>Index <paramref name="engines"/> and their members by name.</summary>
    /// <param name="engines">The registered engines.</param>
    /// <exception cref="InvalidOperationException">Two engines share a name, which would make one of them
    /// unreachable.</exception>
    public MemoryEngineFactory(IEnumerable<IMemoryEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _byName = new Dictionary<string, IMemoryEngine>(StringComparer.Ordinal);
        foreach (var engine in engines) Index(engine);
    }

    private void Index(IMemoryEngine engine)
    {
        if (!_byName.TryAdd(engine.Name, engine))
            throw new InvalidOperationException(
                $"Two memory engines are named '{engine.Name}'. Names must be unique — an engine is " +
                "addressed by name, so a duplicate makes one of them unreachable.");

        if (engine is CompositeMemoryEngine composite)
            foreach (var member in composite.Members) Index(member);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names => [.. _byName.Keys];

    /// <inheritdoc />
    public IMemoryEngine Get(string name) =>
        _byName.TryGetValue(name, out var engine)
            ? engine
            : throw new KeyNotFoundException(
                $"No memory engine named '{name}'. Registered: {string.Join(", ", _byName.Keys)}.");

    /// <inheritdoc />
    public IMemoryEngine Get() =>
        _byName.TryGetValue("default", out var byDefault) ? byDefault
        : _byName.Count == 1 ? _byName.Values.First()
        : throw new KeyNotFoundException(
            _byName.Count == 0
                ? "No memory engine is registered. Call AddMemory() or AddMemoryEngine(name, …)."
                : $"No engine named 'default', and {_byName.Count} are registered — ask for one by name. " +
                  $"Registered: {string.Join(", ", _byName.Keys)}.");

    /// <inheritdoc />
    public bool TryGet(string name, out IMemoryEngine engine) => _byName.TryGetValue(name, out engine!);
}
