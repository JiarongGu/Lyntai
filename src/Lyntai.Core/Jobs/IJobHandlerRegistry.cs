namespace Lyntai.Jobs;

/// <summary>The set of registered <see cref="IJobHandler"/>s, resolved from the DI collection. Lookup is
/// by <see cref="IJobHandler.Type"/> (case-insensitive); first registration wins on a duplicate type.</summary>
public interface IJobHandlerRegistry
{
    IReadOnlyList<IJobHandler> Handlers { get; }

    IJobHandler? Find(string type);
}

/// <inheritdoc/>
public sealed class JobHandlerRegistry : IJobHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IJobHandler> _byType;

    public JobHandlerRegistry(IEnumerable<IJobHandler> handlers)
    {
        var map = new Dictionary<string, IJobHandler>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<IJobHandler>();
        foreach (var h in handlers)
            if (map.TryAdd(h.Type, h)) // first-wins on a duplicate type (mirrors ToolRegistry)
                ordered.Add(h);
        _byType = map;
        Handlers = ordered;
    }

    public IReadOnlyList<IJobHandler> Handlers { get; }

    public IJobHandler? Find(string type) =>
        !string.IsNullOrEmpty(type) && _byType.TryGetValue(type, out var h) ? h : null;
}
