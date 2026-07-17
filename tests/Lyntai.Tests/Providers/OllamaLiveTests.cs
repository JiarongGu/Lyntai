using Lyntai;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// OPT-IN live integration against a real local Ollama — proves the OpenAI-compatible provider
/// (Ollama flavor) works end-to-end against a real endpoint, not just a stubbed HttpMessageHandler.
/// Runs only when <c>LYNTAI_LIVE_OLLAMA</c> is set AND the endpoint is reachable; otherwise it returns
/// early (a no-op pass), so the default test run stays fast, deterministic, and dependency-free
/// (CI never runs the live path). xUnit v2 has no dynamic <c>Assert.Skip</c>, hence the early-return.
///
/// Enable:  set LYNTAI_LIVE_OLLAMA=1   (optionally LYNTAI_OLLAMA_MODEL, default llama3.2:3b)
/// </summary>
public class OllamaLiveTests
{
    private const string DefaultModel = "llama3.2:3b";
    private static string BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL") ?? "http://localhost:11434";
    private static string Model => Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_MODEL") ?? DefaultModel;

    private static OpenAiCompatibleProvider Provider() =>
        new("ollama",
            new OpenAiCompatibleOptions { BaseUrl = BaseUrl, DefaultModel = Model },
            () => new HttpClient(),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromMinutes(3) }); // cold model load can be slow

    /// <summary>True only when the live path is opted in AND the endpoint answers; otherwise the
    /// caller returns early (the test is a no-op pass).</summary>
    private static async Task<bool> LiveAsync()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_OLLAMA"))) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"{BaseUrl}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static LlmRequest Ask(string prompt) => new()
    {
        Messages = [LlmMessage.User(prompt)],
        MaxTokens = 32,
        Temperature = 0,
    };

    [Fact]
    public async Task Completion_against_real_ollama_returns_ok_with_text_and_usage()
    {
        if (!await LiveAsync()) return;

        var reply = await Provider().CompleteAsync(Ask("Reply with exactly one word: pong"));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
        Assert.NotNull(reply.Usage);
        Assert.True(reply.Usage!.InputTokens > 0, "expected a prompt-eval token count from Ollama");
        Assert.True(reply.Usage.OutputTokens > 0, "expected an eval token count from Ollama");
    }

    [Fact]
    public async Task Streaming_against_real_ollama_yields_content_then_final()
    {
        if (!await LiveAsync()) return;

        var content = new System.Text.StringBuilder();
        LlmChunk? last = null;
        await foreach (var chunk in Provider().StreamAsync(Ask("Count from 1 to 3.")))
        {
            if (chunk.Kind == LlmChunkKind.Content) content.Append(chunk.Text);
            last = chunk;
        }

        Assert.False(string.IsNullOrWhiteSpace(content.ToString()), "expected streamed content");
        Assert.NotNull(last);
        Assert.Equal(LlmChunkKind.Final, last!.Kind); // clean termination, not an error
    }

    [Fact]
    public async Task Router_treats_real_ollama_like_any_provider()
    {
        if (!await LiveAsync()) return;

        // the whole point of the abstraction: a real HTTP provider behind the router/front door
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOpenAiCompatibleProvider("ollama", c => { c.BaseUrl = BaseUrl; c.DefaultModel = Model; })
            .DefaultCandidates("ollama")
            .Configure(o => o.ProviderTimeout = TimeSpan.FromMinutes(3)));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>().CompleteAsync(Ask("Say hi in one word."));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }
}
