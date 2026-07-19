using System.Security.Cryptography;
using System.Text;
using Lyntai.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory;

/// <summary>Default <see cref="ISemanticMemory"/>: embeds content/queries with the registered
/// <see cref="IEmbedder"/> and stores/searches vectors through an <see cref="IVectorStore"/> (one
/// collection per task+scope). The embedder is optional at construction so the service always resolves,
/// but a call throws a clear error if none was registered.</summary>
public sealed class SemanticMemory(
    IEmbedder? embedder, IVectorStore vectors, ILogger<SemanticMemory>? logger = null) : ISemanticMemory
{
    // U+001F unit separator between task + scope so ("ab","c") and ("a","bc") can't collide onto one
    // collection. Built from (char)0x1f so the source stays plain-ASCII (no inline control byte / escape).
    private const char CollectionSeparator = (char)0x1f;

    private readonly ILogger _logger = logger ?? NullLogger<SemanticMemory>.Instance;

    private IEmbedder Embedder => embedder ?? throw new InvalidOperationException(
        "Semantic memory requires an IEmbedder — register one with builder.AddEmbeddings(...).");

    public async Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        // NOT fail-open (unlike RecallAsync): a write that faults SURFACES rather than silently losing the
        // fact — see the ISemanticMemory.RememberAsync contract. After an embedding-MODEL swap the stored
        // vectors keep their old dimension; the vector stores degrade gracefully (in-memory/SQLite rank a
        // mismatched row last via Cosine=0; pgvector rejects it), so REINDEX (ForgetAsync + re-Remember).
        if (string.IsNullOrWhiteSpace(content)) return;
        var vector = await Embedder.EmbedAsync(content, ct).ConfigureAwait(false);
        await vectors.UpsertAsync(Collection(taskKey, scope), IdFor(content), vector, content, ct).ConfigureAwait(false);
        _logger.LogDebug("semantic memory: remembered {Chars} chars in {Task}/{Scope}", content.Length, taskKey, scope);
    }

    public async Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string scope, string query,
        int k = 5, double minScore = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || k <= 0) return [];
        try
        {
            var qv = await Embedder.EmbedAsync(query, ct).ConfigureAwait(false);
            var matches = await vectors.SearchAsync(Collection(taskKey, scope), qv, k, ct).ConfigureAwait(false);
            return [.. matches.Where(m => m.Score >= minScore).Select(m => new SemanticHit(m.Payload, m.Score))];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // fail-open (like lexical recall): a vector backend can throw on a dimension-mismatched row (a
            // pgvector `<=>` error after an embedding-model swap, say) — degrade to no hits, don't take down
            // the caller. REINDEX (Forget + re-Remember, or drop the collection) after changing the model.
            _logger.LogWarning(ex, "semantic recall failed for {Task}/{Scope} — returning empty (reindex after a model change)", taskKey, scope);
            return [];
        }
    }

    public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default) =>
        vectors.RemoveCollectionAsync(Collection(taskKey, scope), ct);

    private static string Collection(string taskKey, string scope) => $"{taskKey}{CollectionSeparator}{scope}";

    // stable content hash → the vector id, so re-remembering identical content overwrites (dedup)
    private static string IdFor(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
