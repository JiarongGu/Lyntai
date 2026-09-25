namespace Lyntai.Jobs;

/// <summary>The set of registered <see cref="IJobHandler"/>s, resolved from the DI collection. Lookup is
/// by <see cref="IJobHandler.Type"/> (case-insensitive); first registration wins on a duplicate type.</summary>
public interface IJobHandlerRegistry
{
    IJobHandler? Find(string type);
}

/// <inheritdoc/>
public sealed class JobHandlerRegistry(IEnumerable<IJobHandler> handlers) : IJobHandlerRegistry
{
    private readonly FirstWinsIndex<IJobHandler> _index = new(handlers, h => h.Type);

    public IJobHandler? Find(string type) => _index.Find(type);
}
