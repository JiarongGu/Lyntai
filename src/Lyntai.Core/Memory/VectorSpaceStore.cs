using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory;

/// <summary>An <see cref="IVectorStore"/> that knows which vector space wrote each collection, so vectors from two
/// embedders are never mixed or compared (<c>docs/DECISIONS.md</c> <b>D199</b>). Composed by
/// <c>UseVectorSpace</c> around the registered store.
/// <para>Each collection carries a STAMP — the space that first wrote it — kept in the same store, in a collection
/// of stamps no listing returns. A write into a collection stamped with another space throws; a search of one
/// finds nothing and warns; a read-back throws. An unstamped collection, written before the space was declared, is
/// read as before and claimed by its first write. Removing a collection removes its stamp.</para>
/// <para>Each guarded call costs the inner store one filtered read of the stamp.</para></summary>
internal abstract class VectorSpaceStore : IVectorStore, IAsyncDisposable, IDisposable
{
    // U+001F leads, so no memory address — which always starts with an engine or task name — can collide with it
    private const string Stamps = "\u001flyntai\u001fvector-space";
    private static readonly float[] StampVector = [1f];

    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);
    private readonly bool _owned;

    private protected VectorSpaceStore(IVectorStore inner, string space, bool owned, ILogger? logger)
    {
        Inner = inner;
        Space = space;
        _owned = owned;
        Logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The wrapped store.</summary>
    internal IVectorStore Inner { get; }

    /// <summary>The declared vector space.</summary>
    internal string Space { get; }

    private protected ILogger Logger { get; }

    /// <summary>Wrap <paramref name="inner"/>, declaring exactly the optional halves it has — claiming one it
    /// lacks would make "cannot list" read as "holds nothing" (<b>D86</b>).</summary>
    /// <param name="inner">The store to guard.</param>
    /// <param name="space">The declared vector space.</param>
    /// <param name="owned">Whether disposing the guard disposes <paramref name="inner"/>.</param>
    /// <param name="logger">Where a refused search is reported.</param>
    internal static IVectorStore Wrap(IVectorStore inner, string space, bool owned, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        return (inner is IListableVectorStore, inner is IReadableVectorStore) switch
        {
            (true, true) => new Full(inner, space, owned, logger),
            (true, false) => new Listable(inner, space, owned, logger),
            (false, true) => new Readable(inner, space, owned, logger),
            _ => new Bare(inner, space, owned, logger),
        };
    }

    /// <summary>The space that stamped <paramref name="collection"/>, or null when nothing has.</summary>
    internal async Task<string?> StampOfAsync(string collection, CancellationToken ct)
    {
        var hit = await Inner.SearchAsync(Stamps, StampVector, 1, new VectorSearchFilter { Ids = [collection] }, ct)
            .ConfigureAwait(false);
        return hit.Count == 0 ? null : hit[0].Payload;
    }

    /// <summary>Stamp <paramref name="collection"/> with the declared space — what a completed re-embed does.</summary>
    internal Task StampAsync(string collection, CancellationToken ct) =>
        Inner.UpsertAsync(Stamps, collection, StampVector, Space, ct);

    /// <summary>Clear <paramref name="collection"/>'s stamp, so a re-embed may write into it.</summary>
    internal Task UnstampAsync(string collection, CancellationToken ct) => Inner.DeleteAsync(Stamps, collection, ct);

    /// <summary>Whether <paramref name="collection"/> may be read under the declared space.</summary>
    private protected async Task<bool> ReadableAsync(string collection, CancellationToken ct)
    {
        var stamp = await StampOfAsync(collection, ct).ConfigureAwait(false);
        if (stamp is null || stamp == Space) return true;
        if (_warned.TryAdd(collection, 0))
            Logger.LogWarning(
                "vector collection {Collection} was written by vector space '{Stamp}' and this deployment declares "
                + "'{Space}'; it is not searched until it is re-embedded or forgotten", collection, stamp, Space);
        return false;
    }

    private protected async Task RequireAsync(string collection, CancellationToken ct)
    {
        var stamp = await StampOfAsync(collection, ct).ConfigureAwait(false);
        if (stamp is not null && stamp != Space) throw Mismatch(collection, stamp);
    }

    private InvalidOperationException Mismatch(string collection, string stamp) => new(
        $"Vector collection '{collection}' was written by vector space '{stamp}', and this deployment declares "
        + $"'{Space}'. Its vectors cannot be mixed or compared with this embedder's: re-embed it "
        + "(IReindexableMemory.ReindexAsync) or forget it first.");

    public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        var stamp = await StampOfAsync(collection, ct).ConfigureAwait(false);
        if (stamp is not null && stamp != Space) throw Mismatch(collection, stamp);
        await Inner.UpsertAsync(collection, id, vector, payload, ct).ConfigureAwait(false);
        if (stamp is null) await StampAsync(collection, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
        CancellationToken ct = default) =>
        await ReadableAsync(collection, ct).ConfigureAwait(false)
            ? await Inner.SearchAsync(collection, query, k, ct).ConfigureAwait(false)
            : [];

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
        VectorSearchFilter filter, CancellationToken ct = default) =>
        await ReadableAsync(collection, ct).ConfigureAwait(false)
            ? await Inner.SearchAsync(collection, query, k, filter, ct).ConfigureAwait(false)
            : [];

    public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
        Inner.DeleteAsync(collection, id, ct);

    public async Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        await Inner.RemoveCollectionAsync(collection, ct).ConfigureAwait(false);
        await UnstampAsync(collection, ct).ConfigureAwait(false);
    }

    private protected async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct) =>
        [.. (await ((IListableVectorStore)Inner).ListCollectionsAsync(prefix, ct).ConfigureAwait(false))
            .Where(c => !string.Equals(c, Stamps, StringComparison.Ordinal))];

    private protected async Task<IReadOnlyList<VectorEntry>> ReadAsync(string collection,
        IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        await RequireAsync(collection, ct).ConfigureAwait(false);
        return await ((IReadableVectorStore)Inner).GetAsync(collection, ids, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (!_owned) return default;
        if (Inner is IAsyncDisposable async) return async.DisposeAsync();
        (Inner as IDisposable)?.Dispose();
        return default;
    }

    public void Dispose()
    {
        if (!_owned) return;
        if (Inner is IDisposable sync) sync.Dispose();
        else if (Inner is IAsyncDisposable async) async.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    internal sealed class Bare(IVectorStore inner, string space, bool owned, ILogger? logger)
        : VectorSpaceStore(inner, space, owned, logger);

    internal sealed class Listable(IVectorStore inner, string space, bool owned, ILogger? logger)
        : VectorSpaceStore(inner, space, owned, logger), IListableVectorStore
    {
        public Task<IReadOnlyList<string>> ListCollectionsAsync(string prefix, CancellationToken ct = default) =>
            ListAsync(prefix, ct);
    }

    internal sealed class Readable(IVectorStore inner, string space, bool owned, ILogger? logger)
        : VectorSpaceStore(inner, space, owned, logger), IReadableVectorStore
    {
        public Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
            CancellationToken ct = default) => ReadAsync(collection, ids, ct);
    }

    internal sealed class Full(IVectorStore inner, string space, bool owned, ILogger? logger)
        : VectorSpaceStore(inner, space, owned, logger), IListableVectorStore, IReadableVectorStore
    {
        public Task<IReadOnlyList<string>> ListCollectionsAsync(string prefix, CancellationToken ct = default) =>
            ListAsync(prefix, ct);

        public Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
            CancellationToken ct = default) => ReadAsync(collection, ids, ct);
    }
}
