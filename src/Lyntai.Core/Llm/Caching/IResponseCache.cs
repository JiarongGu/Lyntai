using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Llm.Caching;

/// <summary>
/// A cache of completed <see cref="LlmReply"/>s keyed by a stable hash of the output-determining request
/// fields (see <see cref="ResponseCacheKey"/>). The built-in <see cref="InMemoryResponseCache"/> is what
/// <c>AddResponseCache()</c> registers; register your own <see cref="IResponseCache"/> first (Redis, a
/// distributed KV, a persistent store) to override it — the front door then caches through it transparently.
/// </summary>
public interface IResponseCache
{
    /// <summary>The cached reply for <paramref name="key"/>, or null on a miss / expired entry.
    /// (Named like <c>IKeyValueStore.GetAsync</c>/<c>ISecretVault.GetAsync</c> — null-on-miss, no
    /// <c>Try</c> prefix.)</summary>
    Task<LlmReply?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Store <paramref name="reply"/> under <paramref name="key"/> with a freshness window
    /// (<paramref name="ttl"/>; null = the cache's own default).</summary>
    Task SetAsync(string key, LlmReply reply, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>Evict one entry — the escape hatch for a poisoned/stale reply that must not be served
    /// for the rest of its TTL. A missing key is a no-op.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Computes a stable, collision-resistant cache key from the fields of an <see cref="LlmRequest"/> that
/// determine the model's output — messages (role / content / tool calls / tool-result id / attachments),
/// model, max tokens, temperature, JSON schema. Deliberately EXCLUDES <see cref="LlmRequest.Consumer"/>
/// (a telemetry/routing tag, not an output determinant) so two consumers issuing the same request share a
/// hit. Every field is length-framed before hashing, so no field-boundary shifts a value into another and
/// collides (e.g. content "ab"+"c" vs "a"+"bc"). SHA-256 → lowercase hex.
/// </summary>
public static class ResponseCacheKey
{
    /// <summary>Compute the key. Pass <paramref name="effectiveModel"/> (the model the router will actually
    /// use, e.g. <c>options.ResolveModel(req.Consumer, req.Model)</c>) so two consumers whose per-consumer
    /// DEFAULT models differ don't collide on a null <see cref="LlmRequest.Model"/> — the effective model is
    /// an output determinant, the raw one isn't. Consumer itself stays OUT (two consumers resolving to the
    /// same model still share a hit). Falls back to <see cref="LlmRequest.Model"/> when not supplied.</summary>
    public static string For(LlmRequest req, string? effectiveModel = null)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddString(h, "lyntai-cache-v1"); // version prefix — bump to invalidate the whole keyspace on a shape change
        AddString(h, effectiveModel ?? req.Model);
        AddInt(h, req.MaxTokens ?? -1);
        AddString(h, req.Temperature?.ToString("R", CultureInfo.InvariantCulture));
        AddString(h, req.JsonSchema);
        // Reasoning is an OUTPUT DETERMINANT, so it belongs in the key: the same prompt asked with and
        // without intermediate reasoning can come back with different text, and on a thinking model the
        // difference is the whole reply. Serving a suppressed-reasoning caller a cached reasoning-laden hit
        // would be exactly the "cached the wrong shape" bug the excluded-set discipline exists to prevent.
        AddInt(h, (int)req.Reasoning);
        AddInt(h, req.Messages.Count);
        foreach (var m in req.Messages)
        {
            AddString(h, m.Role);
            AddString(h, m.Content);
            AddString(h, m.ToolCallId);
            AddInt(h, m.ToolCalls?.Count ?? 0);
            foreach (var tc in m.ToolCalls ?? [])
            {
                AddString(h, tc.Id);
                AddString(h, tc.Name);
                AddString(h, tc.ArgumentsJson);
            }
            AddInt(h, m.Attachments?.Count ?? 0);
            foreach (var a in m.Attachments ?? [])
            {
                AddString(h, a.MediaType);
                AddString(h, a.Uri);
                AddInt(h, a.Data?.Length ?? -1);
                if (a.Data is { Length: > 0 }) h.AppendData(a.Data);
            }
        }
        return Convert.ToHexStringLower(h.GetHashAndReset());
    }

    private static void AddString(IncrementalHash h, string? s)
    {
        if (s is null) { AddInt(h, -1); return; }        // null length-frames as -1, distinct from "" (0)
        var bytes = Encoding.UTF8.GetBytes(s);
        AddInt(h, bytes.Length);
        h.AppendData(bytes);
    }

    private static void AddInt(IncrementalHash h, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        h.AppendData(b);
    }
}
