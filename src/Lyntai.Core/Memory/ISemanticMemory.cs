namespace Lyntai.Memory;

/// <summary>
/// Meaning-based task memory: remembers facts by their embedding and recalls them by semantic similarity
/// to a query (not keyword overlap, unlike the lexical <see cref="Lyntai.Storage.IMemoryStore"/>). Composes
/// an app-provided <see cref="Lyntai.Embeddings.IEmbedder"/> with an <see cref="IVectorStore"/>; wired when
/// embeddings are registered (<c>builder.AddEmbeddings(...)</c>). Scoped by (taskKey, scope) like the
/// lexical store; re-remembering identical content overwrites rather than duplicating.
/// </summary>
public interface ISemanticMemory
{
    /// <summary>Embed and store <paramref name="content"/> under (taskKey, scope). Remembering identical
    /// content in the same scope overwrites the existing entry (dedup by content).</summary>
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
