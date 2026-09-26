using Lyntai;

namespace Lyntai.Tests.Live;

/// <summary>
/// The gate for a live test that needs A MODEL — any local endpoint, so Ollama and llama.cpp's
/// <c>llama-server</c> are both usable without editing a test.
///
/// <para><b>Deliberately NOT merged with <see cref="OllamaLive"/>, which answers a different question.</b>
/// That one gates suites that are ABOUT Ollama — they exercise <c>OllamaProvider</c>'s NATIVE
/// routes, so "is Ollama up" is exactly the right probe and a llama.cpp endpoint should skip them. This one
/// gates suites that merely need something to embed or judge with, where pinning the vendor would stop
/// the harness running on a different backend at all. Two questions, two gates; collapsing them would either
/// make vendor tests pass against a vendor that is not there, or make model tests skip on a perfectly good
/// endpoint.</para>
///
/// <para><b>Every legacy variable still works, and that is not politeness.</b> A machine already set up with
/// <c>LYNTAI_LIVE_OLLAMA</c> must not start silently SKIPPING because a variable was renamed — a skip reads
/// as a pass in every summary, the failure <see cref="OllamaLive"/>'s own doc describes.
/// The new names are additive.</para>
///
/// <para><b>The trap when pointing this at llama.cpp: two suites need a CHAT model and an EMBEDDING model at
/// the same endpoint simultaneously.</b> Ollama loads on demand, so one URL serves both. A
/// <c>llama-server</c> started with a single <c>-m</c> holds one model, so the judge half or the vector backend
/// half answers and the other 404s — which surfaces as a confusing partial failure rather than as a skip.
/// Start it in ROUTER mode (<c>--models-dir</c> with <c>--models-max</c> above 1) when a suite needs
/// both.</para>
/// </summary>
public static class LiveModel
{
    /// <summary>Opt-in. Set either this or the legacy <c>LYNTAI_LIVE_OLLAMA</c>.</summary>
    public const string EnableVariable = "LYNTAI_LIVE_MODEL";

    /// <summary>Endpoint override; falls back to the legacy <c>LYNTAI_OLLAMA_URL</c>.</summary>
    public const string UrlVariable = "LYNTAI_LIVE_MODEL_URL";

    /// <summary>Which wire the endpoint speaks — <c>ollama</c> (the default) for the native
    /// provider, or <c>openai</c> for anything serving the OpenAI-shaped routes, llama-server included.</summary>
    public const string FlavorVariable = "LYNTAI_LIVE_MODEL_FLAVOR";

    /// <summary>Where the model is.</summary>
    public static string BaseUrl =>
        Read(UrlVariable) ?? Read("LYNTAI_OLLAMA_URL") ?? "http://localhost:11434";

    /// <summary>
    /// Which PROVIDER to register. <b>Declared, never probed</b> — the same stance
    /// <c>LocalDiffusionOptions.Accelerator</c> takes (<c>docs/DECISIONS.md</c> D68): guessing from a port or
    /// a banner is a rule that is right until someone runs llama-server on 11434, and then it is wrong in a
    /// way that presents as a 404 rather than as a bad guess.
    /// <para>Defaults to the Ollama-native provider.</para>
    /// </summary>
    private static bool OpenAiShaped =>
        Read(FlavorVariable)?.ToLowerInvariant() is "openai" or "llamacpp" or "llama.cpp" or "llama-server";

    /// <summary>The message a skip carries — one string, so a reader scanning skips sees one reason.</summary>
    public const string SkipReason =
        "LYNTAI_LIVE_MODEL (or LYNTAI_LIVE_OLLAMA) not set, or no model endpoint is reachable";

    /// <summary>
    /// Whether a live-model test may run: opted in AND something answers.
    ///
    /// <para><b>Probed through <c>/v1/models</c> first, then Ollama's native <c>/api/tags</c>.</b> Both
    /// backends serve the former; only Ollama serves the latter. Probing <c>/api/tags</c> ALONE would skip
    /// every one of these suites silently against llama-server — the endpoint up, the model loaded, and the
    /// gate saying "not available".</para>
    /// </summary>
    public static async Task<bool> IsAvailableAsync()
    {
        if (Read(EnableVariable) is null && Read("LYNTAI_LIVE_OLLAMA") is null) return false;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await AnswersAsync(http, "/v1/models") || await AnswersAsync(http, "/api/tags");

        async Task<bool> AnswersAsync(HttpClient client, string path)
        {
            try { return (await client.GetAsync(new Uri(BaseUrl + path))).IsSuccessStatusCode; }
            catch { return false; }   // unreachable, refused, DNS — none of them mean "broken"
        }
    }

    /// <summary>Register the live endpoint as a provider under <paramref name="id"/>, on whichever wire the
    /// flavor variable declares. The one place a live suite says "give me a model", so adding a backend is
    /// an environment variable rather than an edit to every suite.
    /// <para>The OpenAI-shaped flavor pins the base to its <c>/v1</c> surface so the declaration survives
    /// any URL — an Ollama-port root would otherwise compose the native provider, and both server families
    /// serve the OpenAI-shaped routes under <c>/v1</c> either way.</para></summary>
    /// <param name="builder">The builder to register into.</param>
    /// <param name="model">The model to default to.</param>
    /// <param name="id">The provider id; the default matches what these suites use.</param>
    public static LyntaiBuilder AddLiveProvider(this LyntaiBuilder builder, string model, string id = "ollama") =>
        OpenAiShaped
            ? builder.AddHttpProvider(id, o =>
            {
                var b = BaseUrl.TrimEnd('/');
                o.BaseUrl = b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? b : b + "/v1";
                o.Model = model;
                o.ApiKey = null;
            })
            : builder.AddOllamaProvider(id, o =>
            {
                o.BaseUrl = BaseUrl;
                o.Model = model;
            });

    private static string? Read(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
