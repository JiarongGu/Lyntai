namespace Lyntai.Memory;

/// <summary>
/// Meaning-based task memory: remembers facts by their embedding and recalls them by semantic similarity
/// to a query (not keyword overlap, unlike the lexical <see cref="Lyntai.Storage.IMemoryStore"/>). Composes
/// an app-provided <see cref="Lyntai.Embeddings.IEmbedder"/> with an <see cref="IVectorStore"/>; wired when
/// embeddings are registered (<c>builder.AddEmbeddings(...)</c>). Scoped by (taskKey, scope) like the
/// lexical store; re-remembering identical content overwrites rather than duplicating.
/// <para>CHANGING THE EMBEDDING MODEL: stored vectors keep their old dimension. Recall is fail-open (a
/// backend that rejects a dimension-mismatched vector — e.g. pgvector — yields no hits rather than
/// throwing), but you should REINDEX (<see cref="ForgetAsync"/> the scope + re-<see cref="RememberAsync"/>,
/// or drop the vectors) after a model change so recall works again.</para>
/// </summary>
public interface ISemanticMemory
{
    /// <summary>Embed and store <paramref name="content"/> under (taskKey, scope). Remembering identical
    /// content in the same scope overwrites the existing entry (dedup by content).
    /// <para><b>Surfaces failures</b> (throws if the embedder or vector store faults) — deliberately
    /// asymmetric with the fail-open <see cref="RecallAsync"/>: silently losing a WRITE is worse than a
    /// throw the caller can see. The batteries-included <c>ChatOrchestrator</c> already wraps its own
    /// Remember call; a direct consumer that wants best-effort should catch.</para></summary>
    Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default);

    /// <summary>The <paramref name="k"/> most semantically similar remembered facts to <paramref name="query"/>
    /// within (taskKey, scope), highest similarity first. Entries scoring below <paramref name="minScore"/>
    /// (cosine, [-1, 1]) are dropped. An empty/whitespace query returns nothing.</summary>
    Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string scope, string query,
        int k = 5, double minScore = 0, CancellationToken ct = default);

    /// <summary>Forget everything remembered under (taskKey, scope).</summary>
    Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default);
}

/// <summary>A recalled fact and its cosine similarity to the query (higher is more relevant).</summary>
public sealed record SemanticHit(string Content, double Score);
