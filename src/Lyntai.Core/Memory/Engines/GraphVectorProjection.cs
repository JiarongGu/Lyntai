using System.Globalization;
using Lyntai.Inference;
using Microsoft.Extensions.Logging;

namespace Lyntai.Memory.Engines;

/// <summary>A graph engine's similarity index: a PROJECTION of its nodes the graph store does not hold, kept in
/// an <see cref="IVectorStore"/> under <see cref="MemoryVectorCollection"/>'s address.
/// <para><b>Every removal verb must reach it</b> — the store's own contract cannot see it, so a forget that
/// cleared only the nodes would leave their full content readable here
/// (<c>.claude/knowledge/pitfalls.md</c>, "A projection the OWNING STORE does not hold").</para></summary>
/// <param name="engine">The owning engine's name — the head of every collection address.</param>
/// <param name="providers">The registered backends; one declaring <see cref="ProviderKinds.Vector"/> embeds.</param>
/// <param name="vectors">The index, or null when none is wired.</param>
/// <param name="routing">Shared cooldown and admission for the embedding calls; null routes bare.</param>
/// <param name="logger">The engine's logger, so every message keeps its category.</param>
internal sealed class GraphVectorProjection(
    string engine,
    IEnumerable<IModelProvider>? providers,
    IVectorStore? vectors,
    IProviderRouterFactory? routing,
    ILogger logger)
{
    /// <summary>Whether an index is wired at all — what a REMOVAL asks, since an index an earlier
    /// configuration filled must still be erased after its backend is gone.</summary>
    internal bool Wired => vectors is not null;

    // The VECTOR STORE is checked first deliberately: it is a null test, where `CanEmbed` walks the registered
    // backends and asks each whether it is available — which for a CLI backend resolves a command on PATH. This
    // is read on every write and every recall, so the cheap half leads.
    /// <summary>Whether writes are embedded and indexed — an index AND a backend that embeds.</summary>
    internal bool Enriches => vectors is not null && EmbeddingRouting.CanEmbed(providers);

    /// <summary>Whether a write is OWED a vector: an index and a backend declaring embeds, available or not — so
    /// a write that got none while this holds lost it, rather than never having been due one.</summary>
    internal bool Owed => vectors is not null
        && providers is not null && providers.Any(p => p is IVectorProvider && ProviderShapes.Embeds(p.Capabilities));

    /// <summary>The one embed and similarity search a write needs — a backend billing per call is paid ONCE per
    /// write.
    /// <para>Null when nothing is enriched, <paramref name="k"/> is zero, or the EMBED fails. A failed SEARCH
    /// keeps the vector with no neighbours, so the write is still indexed — after a model swap a rebuild
    /// converges only this way. Both best-effort: a failing vector backend must not fail the write.</para>
    /// <para>Searches <paramref name="k"/> + 1, because on a re-remember this write's own PRIOR vector is
    /// still in the collection and would take a slot a genuine neighbour should have; callers exclude that
    /// self-match by CONTENT, the store's dedup key and the one rule that works before the write has an id.</para></summary>
    internal async Task<(float[] Vector, IReadOnlyList<VectorMatch> Near)?> SearchAsync(MemoryWrite write, int k,
        CancellationToken ct)
    {
        if (!Enriches || k <= 0) return null;
        float[] vector;
        try
        {
            vector = await EmbeddingRouting.EmbedOneAsync(
                providers, write.Content, EmbeddingRole.Document, logger, routing,
                ProviderConsumers.Memory, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "embedding failed for {Engine}; storing without its vector, signals or links", engine);
            return null;
        }

        try
        {
            var near = await vectors!
                .SearchAsync(Collection(write.TaskKey, write.Scope), vector, k + 1, ct)
                .ConfigureAwait(false);
            return (vector, near);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // no neighbours is exactly what the salience probe reads as "nothing to judge against", so salience
            // declines as it would with no search at all — only the index still gets the vector
            logger.LogWarning(ex,
                "similarity search failed for {Engine}; storing without signals or links", engine);
            return (vector, []);
        }
    }

    /// <summary>Index a stored node's vector. Best-effort; returns whether it was indexed.</summary>
    internal async Task<bool> IndexAsync(long id, MemoryWrite write, float[] vector, CancellationToken ct)
    {
        try
        {
            await vectors!
                .UpsertAsync(Collection(write.TaskKey, write.Scope), id.ToString(CultureInfo.InvariantCulture),
                    vector, write.Content, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "similarity enrichment failed for {Engine}; the entry is stored without its vector", engine);
            return false;
        }
    }

    /// <summary>Embed a re-embed's batch as documents. NOT best-effort: the caller counts a failure.</summary>
    internal async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var embedded = await EmbeddingRouting.EmbedAsync(
            providers, texts, EmbeddingRole.Document, logger, routing, ProviderConsumers.Memory, ct).ConfigureAwait(false);
        return embedded.Count == texts.Count
            ? embedded
            : throw new InvalidOperationException(
                $"the embedding backend returned {embedded.Count} vectors for {texts.Count} texts");
    }

    /// <summary>Write a re-embedded vector at the node's existing address. NOT best-effort: a broken index is the
    /// re-embed's failure to report, and a rerun resumes it.</summary>
    internal Task UpsertAsync(GraphNode node, float[] vector, CancellationToken ct) =>
        vectors!.UpsertAsync(Collection(node.TaskKey, node.Scope), node.Id.ToString(CultureInfo.InvariantCulture),
            vector, node.Content, ct);

    /// <summary>Drop the collections a forget of (<paramref name="taskKey"/>, <paramref name="scope"/>) erases.
    /// NOT best-effort: a consent withdrawal must fail loudly rather than leave content readable.
    /// <para>A named scope drops its one collection, orphans included. An unscoped forget drops the collection of
    /// every scope <paramref name="scopes"/> names and — over an <see cref="IListableVectorStore"/> — every
    /// collection under this engine and task's <see cref="MemoryVectorCollection.PrefixFor"/>, which also reaches
    /// an ORPHAN whose nodes an earlier partial failure already removed. The prefix matches no other task.</para></summary>
    /// <param name="taskKey">The task being forgotten.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="scopes">Reads the scopes the task's nodes still name; called only for an unscoped forget.</param>
    /// <param name="ct">Cancellation.</param>
    internal async Task ForgetAsync(string taskKey, string? scope,
        Func<Task<IEnumerable<string>>> scopes, CancellationToken ct)
    {
        if (vectors is null) return;

        if (scope is not null)
        {
            await vectors.RemoveCollectionAsync(Collection(taskKey, scope), ct).ConfigureAwait(false);
            return;
        }

        var collections = new HashSet<string>(
            (await scopes().ConfigureAwait(false)).Select(s => Collection(taskKey, s)), StringComparer.Ordinal);
        if (vectors is IListableVectorStore listable)
            collections.UnionWith(await listable
                .ListCollectionsAsync(MemoryVectorCollection.PrefixFor(engine, taskKey), ct).ConfigureAwait(false));

        foreach (var collection in collections)
        {
            ct.ThrowIfCancellationRequested();
            await vectors.RemoveCollectionAsync(collection, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Delete the vectors of nodes a PRUNE removed, each under its own task and scope.
    /// <para><b>BEST-EFFORT, unlike <see cref="ForgetAsync"/></b>: a prune clears the index AFTER the store, so
    /// throwing here would lose the COUNT of a prune that in fact succeeded. The honest degradation is an orphaned
    /// vector, and pruning is best-effort capacity management by contract.</para></summary>
    internal async Task RemoveAsync(IReadOnlyList<GraphNode> removed, CancellationToken ct)
    {
        if (vectors is null || removed.Count == 0) return;

        try
        {
            foreach (var node in removed)
            {
                ct.ThrowIfCancellationRequested();
                await vectors
                    .DeleteAsync(Collection(node.TaskKey, node.Scope), node.Id.ToString(CultureInfo.InvariantCulture), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "similarity index cleanup failed for {Engine}; {Count} entr(ies) were removed from the store "
                + "and their vectors remain as orphans", engine, removed.Count);
        }
    }

    private string Collection(string taskKey, string scope) => MemoryVectorCollection.For(engine, taskKey, scope);
}
