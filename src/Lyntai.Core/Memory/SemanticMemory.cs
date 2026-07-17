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
    private readonly ILogger _logger = logger ?? NullLogger<SemanticMemory>.Instance;

    private IEmbedder Embedder => embedder ?? throw new InvalidOperationException(
        "Semantic memory requires an IEmbedder — register one with builder.AddEmbeddings(...).");

    public async Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var vector = await Embedder.EmbedAsync(content, ct).ConfigureAwait(false);
        await vectors.UpsertAsync(Collection(taskKey, scope), IdFor(content), vector, content, ct).ConfigureAwait(false);
        _logger.LogDebug("semantic memory: remembered {Chars} chars in {Task}/{Scope}", content.Length, taskKey, scope);
    }

    public async Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string scope, string query,
        int k = 5, double minScore = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || k <= 0) return [];
        var qv = await Embedder.EmbedAsync(query, ct).ConfigureAwait(false);
        var matches = await vectors.SearchAsync(Collection(taskKey, scope), qv, k, ct).ConfigureAwait(false);
        return [.. matches.Where(m => m.Score >= minScore).Select(m => new SemanticHit(m.Payload, m.Score))];
    }

    public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default) =>
        vectors.RemoveCollectionAsync(Collection(taskKey, scope), ct);

    // one vector collection per (task, scope); the unit separator can't occur in the parts
    private static string Collection(string taskKey, string scope) => $"{taskKey}{scope}";

    // stable content hash → the vector id, so re-remembering identical content overwrites (dedup)
    private static string IdFor(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
