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
public sealed class ToolRegistry(IEnumerable<ITool> tools) : IToolRegistry
{
    private readonly FirstWinsIndex<ITool> _index = new(tools, t => t.Name);

    public IReadOnlyList<ITool> Tools => _index.Items;

    public ITool? Find(string name) => _index.Find(name);
}
