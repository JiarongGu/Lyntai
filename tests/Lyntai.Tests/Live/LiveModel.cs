using Lyntai;
using Lyntai.Providers.OpenAiCompatible;

namespace Lyntai.Tests.Live;

/// <summary>
/// The gate for a live test that needs A MODEL — any OpenAI-compatible local endpoint, so Ollama and
/// llama.cpp's <c>llama-server</c> are both usable without editing a test.
///
/// <para><b>Deliberately NOT merged with <see cref="OllamaLive"/>, which answers a different question.</b>
/// That one gates suites that are ABOUT Ollama — they exercise <see cref="OpenAiFlavor.Ollama"/>'s NATIVE
/// routes, so "is Ollama up" is exactly the right probe and a llama.cpp endpoint should skip them. This one
/// gates suites that merely need something to embed or judge with, where pinning the vendor is what stopped
/// the harness running on a different backend at all. Two questions, two gates; collapsing them would either
/// make vendor tests pass against a vendor that is not there, or make model tests skip on a perfectly good
/// endpoint.</para>
///
/// <para><b>Every legacy variable still works, and that is not politeness.</b> A machine already set up with
/// <c>LYNTAI_LIVE_OLLAMA</c> must not start silently SKIPPING because a variable was renamed — a skip reads
/// as a pass in every summary, which is the failure <see cref="OllamaLive"/>'s own doc was written about.
/// The new names are additive.</para>
///
/// <para><b>The trap when pointing this at llama.cpp: two suites need a CHAT model and an EMBEDDING model at
/// the same endpoint simultaneously.</b> Ollama loads on demand, so one URL serves both. A
/// <c>llama-server</c> started with a single <c>-m</c> holds one model, so the judge half or the embedder
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

    /// <summary>Which dialect the endpoint speaks — <c>ollama</c> (the default, unchanged) or
    /// <c>openai</c> for anything serving the OpenAI routes, llama-server included.</summary>
    public const string FlavorVariable = "LYNTAI_LIVE_MODEL_FLAVOR";

    /// <summary>Where the model is.</summary>
    public static string BaseUrl =>
        Read(UrlVariable) ?? Read("LYNTAI_OLLAMA_URL") ?? "http://localhost:11434";

    /// <summary>
    /// Which dialect to speak. <b>Declared, never probed</b> — the same stance
    /// <c>LocalDiffusionOptions.Accelerator</c> takes (<c>docs/DECISIONS.md</c> D68): guessing from a port or
    /// a banner is a rule that is right until someone runs llama-server on 11434, and then it is wrong in a
    /// way that presents as a 404 rather than as a bad guess.
    /// <para>Defaults to <see cref="OpenAiFlavor.Ollama"/>, which is what every one of these suites did
    /// before this type existed, so an unset variable changes nothing.</para>
    /// </summary>
    public static OpenAiFlavor Flavor =>
        Read(FlavorVariable)?.ToLowerInvariant() switch
        {
            "openai" or "llamacpp" or "llama.cpp" or "llama-server" => OpenAiFlavor.OpenAi,
            _ => OpenAiFlavor.Ollama,
        };

    /// <summary>The message a skip carries — one string, so a reader scanning skips sees one reason.</summary>
    public const string SkipReason =
        "LYNTAI_LIVE_MODEL (or LYNTAI_LIVE_OLLAMA) not set, or no model endpoint is reachable";

    /// <summary>
    /// Whether a live-model test may run: opted in AND something answers.
    ///
    /// <para><b>Probed through <c>/v1/models</c> first, then Ollama's native <c>/api/tags</c>.</b> Both
    /// backends serve the former; only Ollama serves the latter. Probing <c>/api/tags</c> ALONE is what made
    /// every one of these suites skip silently against llama-server — the endpoint was up, the model was
    /// loaded, and the gate said "not available".</para>
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

    /// <summary>Register the live endpoint as a provider under <paramref name="id"/>, in whichever dialect
    /// <see cref="Flavor"/> names. The one place a live suite says "give me a model", so adding a backend is
    /// an environment variable rather than an edit to every suite.</summary>
    /// <param name="builder">The builder to register into.</param>
    /// <param name="model">The model to default to.</param>
    /// <param name="id">The provider id; the default matches what these suites already used.</param>
    public static LyntaiBuilder AddLiveProvider(this LyntaiBuilder builder, string model, string id = "ollama") =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = BaseUrl;
            o.DefaultModel = model;
            o.Flavor = Flavor;
        });

    private static string? Read(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
