namespace Lyntai.Tests.Live;

/// <summary>The one place that decides whether a live-Ollama test may run, and where it points.
///
/// <para><b>Extracted 2026-08-13 from SIX copies.</b> Every live suite had grown its own
/// <c>BaseUrl</c> + <c>LiveAsync</c> pair — same intent, drifted in every detail: a 3-second timeout in two
/// files and 5 in the others, <c>string.IsNullOrEmpty</c> against a list pattern, a string concat against
/// an interpolation. None of that variation was a decision; it was three separate authors of the same idea,
/// and each new live suite copied whichever one it happened to sit next to.</para>
///
/// <para><b>Why the duplication mattered rather than merely being untidy.</b> These probes decide whether a
/// test RUNS. A copy with a subtly stricter timeout silently skips on a slow machine while its siblings run,
/// so the suite's coverage varies by which file you look at — and a skip reads as a pass in every summary.
/// One implementation means one answer to "is the backend up".</para></summary>
public static class OllamaLive
{
    /// <summary>Where Ollama is. Overridable so a live run can point at another host without editing tests.</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL") ?? "http://localhost:11434";

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

    /// <summary>The message a skip carries. One string, so every live suite explains itself the same way and
    /// a reader scanning skips sees one reason rather than six phrasings of it.</summary>
    public const string SkipReason = "LYNTAI_LIVE_OLLAMA not set, or the Ollama endpoint is unreachable";
}
