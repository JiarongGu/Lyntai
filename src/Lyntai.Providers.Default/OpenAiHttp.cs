using System.Text.Json;

namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>
/// The HTTP error-reporting conventions every OpenAI-compatible surface in this package shares — the chat
/// provider and the embedder read and trim a failure body identically, so it lives here once for the same
/// reason <see cref="OpenAiEndpoint"/> does: two copies drift silently, and the drift shows up as a worse
/// diagnostic on one surface only.
/// </summary>
internal static class OpenAiHttp
{
    /// <summary>Read a response body for a DIAGNOSTIC message. Never throws: a body that can't be read is
    /// reported as empty rather than replacing the failure it was meant to describe.</summary>
    internal static async Task<string> SafeRead(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { return ""; }
    }

    /// <summary>Head, not Tail: an HTTP error body leads with the useful part (ClaudeCli's Tail keeps the END
    /// of stderr — same job, opposite slice; the names say which).</summary>
    internal static string Head(string text, int max = 300)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    /// <summary>The error a body reports IN BAND, or null when it reports none.
    ///
    /// <para><b>Why this exists: a backend can answer in TWO channels, and a 2xx status is not a claim of
    /// success.</b> Gateways and aggregators routinely answer <c>200</c> carrying
    /// <c>{"error":{"code":429,…}}</c>. Reading only the status meant such a reply reached the
    /// "malformed or empty body" path, which RE-SENT the identical request to a host that had just said it
    /// was rate-limited and then reported <see cref="Lyntai.Llm.LlmVerdict.Failed"/> — which ADVANCES and
    /// takes a dead-host strike where <c>RateLimited</c> COOLS. This is the same defect class the CLI engine
    /// shipped twice (codex, then claude); the general rule from <c>pitfalls.md</c> is that whenever a
    /// backend can answer in two channels, the precedence is decided explicitly and pinned by a test.</para>
    ///
    /// <para>Deliberately narrow: it reports only that an <c>error</c> member is PRESENT and what it says.
    /// It does not read a <c>code</c> as an HTTP status — that mapping is not measured across the
    /// aggregators this provider serves, and inventing it would be the documented-not-measured trap. The
    /// caller classifies the returned text through the one shared corpus.</para></summary>
    internal static string? InBandError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("error", out var error)) return null;

            var text = error.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Object =>
                    error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString()
                        : error.GetRawText(),
                _ => error.GetRawText(),
            };

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            // not JSON at all — the caller's existing malformed-body path owns that case
            return null;
        }
    }
}
