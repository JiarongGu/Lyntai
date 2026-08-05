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
}
