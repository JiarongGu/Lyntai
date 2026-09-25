namespace Lyntai;

/// <summary>A DI collection indexed by a name: the first registration of a name wins, lookup ignores case,
/// and the survivors keep registration order. What <see cref="Agents.ToolRegistry"/> and
/// <see cref="Jobs.JobHandlerRegistry"/> are built on.</summary>
internal sealed class FirstWinsIndex<T> where T : class
{
    private readonly Dictionary<string, T> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public FirstWinsIndex(IEnumerable<T> items, Func<T, string> key)
    {
        var ordered = new List<T>();
        foreach (var item in items)
            if (_byKey.TryAdd(key(item), item)) ordered.Add(item);
        Items = ordered;
    }

    /// <summary>The surviving items, in registration order.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The item registered under <paramref name="key"/>, ignoring case; null for none or a blank key.</summary>
    public T? Find(string key) => !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out var item) ? item : null;
}
