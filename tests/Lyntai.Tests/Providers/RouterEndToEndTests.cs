using System.Net;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>Spec §6 fallback semantics proven END-TO-END across two heterogeneous provider types:
/// the claude CLI (spawning the deterministic stub) and an OpenAI-compatible HTTP endpoint
/// (stubbed handler). No fakes of Lyntai's own types anywhere in this file.</summary>
[Collection("provider-cmd-env")] // serialized with other tests that set LYNTAI_PROVIDER_CMD
public class RouterEndToEndTests : IDisposable
{
    private readonly StubHttpHandler _http = new();

    public RouterEndToEndTests() =>
        Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", ClaudeCliProviderTests.StubCommand);

    public void Dispose() =>
        Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", null);

    private const string HttpOkBody = """
        {"choices":[{"message":{"content":"served by http"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":5,"completion_tokens":2}}
        """;

    private ServiceProvider BuildStack(Action<LyntaiOptions>? tune = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddClaudeCliProvider();
            b.AddOpenAiCompatibleProvider("openai", c => { c.BaseUrl = "https://api.openai.com"; c.ApiKey = "k"; });
            b.DefaultCandidates("claude-cli", "openai");
            if (tune is not null) b.Configure(tune);
        });
        // reroute the provider's named HttpClient into the scripted handler
        services.AddHttpClient(OpenAiCompatibleBuilderExtensions.HttpClientName("openai"))
            .ConfigurePrimaryHttpMessageHandler(() => _http);
        return services.BuildServiceProvider();
    }

    private static LlmRequest Req(string prompt) => new() { Messages = [LlmMessage.User(prompt)] };

    [Fact]
    public async Task Primary_cli_fails_secondary_http_serves()
    {
        _http.Enqueue(HttpStatusCode.OK, HttpOkBody);
        using var sp = BuildStack();
        var router = sp.GetRequiredService<ILlmRouter>();

        // FORCE_ERROR makes the CLI stub produce no content → Failed → advance
        var reply = await router.CompleteAsync([new("claude-cli"), new("openai")], Req("FORCE_ERROR now"));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("served by http", reply.Text);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Healthy_primary_cli_serves_and_http_is_never_called()
    {
        using var sp = BuildStack();
        var router = sp.GetRequiredService<ILlmRouter>();

        var reply = await router.CompleteAsync([new("claude-cli"), new("openai")], Req("all good"));

        Assert.Equal("stub reply: all good", reply.Text);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Streaming_falls_over_pre_content_then_commits_to_http()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"streamed "}}]}

            data: {"choices":[{"delta":{"content":"by http"}}]}

            data: [DONE]

            """;
        _http.Enqueue(HttpStatusCode.OK, sse, "text/event-stream");
        using var sp = BuildStack();
        var router = sp.GetRequiredService<ILlmRouter>();

        var chunks = new List<LlmChunk>();
        await foreach (var c in router.StreamAsync([new("claude-cli"), new("openai")], Req("FORCE_ERROR stream")))
            chunks.Add(c);

        Assert.Equal("streamed by http",
            string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(LlmChunkKind.Final, chunks[^1].Kind);
        Assert.DoesNotContain(chunks, c => c.Kind == LlmChunkKind.Error); // clean fallover, no leak
    }

    [Fact]
    public async Task Streaming_never_falls_back_after_the_first_token()
    {
        using var sp = BuildStack();
        var router = sp.GetRequiredService<ILlmRouter>();

        // healthy CLI stream commits immediately — the HTTP provider must never be touched
        var chunks = new List<LlmChunk>();
        await foreach (var c in router.StreamAsync([new("claude-cli"), new("openai")], Req("commit to me")))
            chunks.Add(c);

        Assert.Equal("stub reply: commit to me",
            string.Concat(chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text)));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Dead_host_cooldown_skips_then_retries_after_expiry()
    {
        _http.Enqueue(HttpStatusCode.InternalServerError, "boom");   // call 1: kills the host
        _http.Enqueue(HttpStatusCode.OK, HttpOkBody);                // call 3 (after cooldown): recovers
        using var sp = BuildStack(o =>
        {
            o.DeadHostThreshold = 1;
            // generous margin so the test is robust on a saturated parallel runner: call 2 lands
            // immediately (well inside the window), call 3 after a delay that dwarfs the cooldown
            o.DeadHostCooldown = TimeSpan.FromMilliseconds(200);
        });
        var router = sp.GetRequiredService<ILlmRouter>();
        var candidates = new List<LlmCandidate> { new("openai"), new("claude-cli") };

        var r1 = await router.CompleteAsync(candidates, Req("first"));
        Assert.Equal("stub reply: first", r1.Text);   // http 500 → fell to CLI
        Assert.Single(_http.Requests);

        var r2 = await router.CompleteAsync(candidates, Req("second"));
        Assert.Equal("stub reply: second", r2.Text);  // openai dead → skipped without an HTTP call
        Assert.Single(_http.Requests);

        await Task.Delay(600); // let the cooldown (200ms) lapse with a wide margin
        var r3 = await router.CompleteAsync(candidates, Req("third"));
        Assert.Equal("served by http", r3.Text);      // back in rotation and healthy again
        Assert.Equal(2, _http.Requests.Count);
    }
}
