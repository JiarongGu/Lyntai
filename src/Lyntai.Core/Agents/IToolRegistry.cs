namespace Lyntai.Agents;

/// <summary>The set of executable tools available to an <see cref="IToolLoop"/>, resolved from the DI
/// collection of <see cref="ITool"/>. Lookup is by name (case-insensitive, so a model that varies the
/// casing still resolves); first registration wins on a duplicate name.</summary>
public interface IToolRegistry
{
    IReadOnlyList<ITool> Tools { get; }

    ITool? Find(string name);
}

/// <inheritdoc/>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ITool> _byName;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        var map = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ITool>();
        foreach (var t in tools)
            if (map.TryAdd(t.Name, t)) // first-wins on a duplicate name (mirrors the router)
                ordered.Add(t);
        _byName = map;
        Tools = ordered;
    }

    public IReadOnlyList<ITool> Tools { get; }

    public ITool? Find(string name) =>
        !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var t) ? t : null;
}
