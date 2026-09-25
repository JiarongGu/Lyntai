namespace Lyntai.Tests.Live;

/// <summary>The one place that decides whether a live-Ollama test may run, and where it points.
///
/// <para>One probe, because these decide whether a test RUNS: a copy with a stricter timeout silently skips
/// on a slow machine while its siblings run.</para>
///
/// <para><b>This is NOT the gate to reach for</b> unless the suite is ABOUT Ollama — it exercises
/// <c>OllamaProvider</c>'s NATIVE routes, so <c>/api/tags</c> is the right probe and a non-Ollama endpoint
/// SHOULD skip it. A suite that merely needs a model to embed or judge with uses <see cref="LiveModel"/>,
/// which probes the OpenAI-shaped routes both Ollama and llama.cpp's <c>llama-server</c> serve.</para></summary>
public static class OllamaLive
{
    /// <summary>Where Ollama is. Overridable so a live run can point at another host without editing tests.</summary>
    public static string BaseUrl =>
        // an EMPTY variable is unset: "" would build a relative URI, fail the probe and skip in silence
        Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL") is { Length: > 0 } url ? url : "http://localhost:11434";

    /// <summary>Whether live-Ollama tests may run: the opt-in variable is set AND the endpoint answers.
    ///
    /// <para><b>Both halves are required, and the second is the point.</b> Checking only the variable turns
    /// "the developer forgot to start Ollama" into a wall of failures that look like defects; checking only
    /// the endpoint would run these on any machine that happens to have it up, spending time and tokens
    /// nobody asked for. Opt in, then verify.</para>
    ///
    /// <para>A failure to reach the endpoint is a SKIP, never an error — the endpoint being down is the
    /// normal state of a machine that has not opted in.</para></summary>
    public static async Task<bool> IsAvailableAsync()
    {
        if (Environment.GetEnvironmentVariable("LYNTAI_LIVE_OLLAMA") is not { Length: > 0 }) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return (await http.GetAsync(new Uri(BaseUrl + "/api/tags"))).IsSuccessStatusCode;
        }
        catch { return false; }   // unreachable, refused, DNS — all mean "not available", none mean "broken"
    }

    /// <summary>Whether the endpoint has <paramref name="model"/> pulled — a name as <c>ollama pull</c> takes it,
    /// with or without its tag. A missing model is a SKIP: an embed call against it FAILS, which reads as a
    /// defect rather than as a machine that has not pulled it.</summary>
    public static async Task<bool> HasModelAsync(string model)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var tags = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(new Uri(BaseUrl + "/api/tags")));
            return tags.RootElement.TryGetProperty("models", out var models)
                && models.EnumerateArray().Any(m => m.TryGetProperty("name", out var name)
                    && name.GetString() is { } pulled
                    && (pulled == model || pulled.StartsWith(model + ":", StringComparison.Ordinal)));
        }
        catch { return false; }
    }

    /// <summary>The message a skip carries. One string, so every live suite explains itself the same way and
    /// a reader scanning skips sees one reason rather than six phrasings of it.</summary>
    public const string SkipReason = "LYNTAI_LIVE_OLLAMA not set, or the Ollama endpoint is unreachable";
}
