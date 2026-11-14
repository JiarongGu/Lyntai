namespace Lyntai.Storage;

/// <summary>An optional capability of an <see cref="IKeyValueStore"/>, found by type test: a write that lands only
/// where the key holds nothing, decided by the store itself, so two writers racing for one key agree on one winner.
/// Every shipped store offers it. A store that wraps another must offer it whenever the inner one does, or a consumer
/// testing for it silently falls back to its unguarded path.</summary>
public interface IConditionalKeyValueStore : IKeyValueStore
{
    /// <summary>Write <paramref name="value"/> under <paramref name="key"/> only when the key holds no value. True when
    /// this call wrote it; false, with the stored value untouched, when the key already held one. Atomic against
    /// every writer the store serves: on a database, other processes included.</summary>
    Task<bool> SetIfAbsentAsync(string key, string value, CancellationToken ct = default);
}
