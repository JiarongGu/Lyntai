using System.Net;
using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Llm;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Providers.OpenAiCompatible;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>"Read a long off a backend's wire object" had THREE implementations in this package, and two of
/// them ended <c>el.GetInt64()</c> — which throws <see cref="FormatException"/> on a JSON number that is not
/// an integral long (a fractional count, anything past <c>long.MaxValue</c>). Every one of
/// those reads is a <c>usage</c> read on an otherwise GOOD reply, and every guard around them catches only
/// <see cref="JsonException"/>, so the throw escaped: out of <c>CompleteAsync</c>, out of the streaming
/// enumerator, and through both <c>claude</c> stream-json readers whose contract says they never throw.
///
/// <para>These pin the ONE surviving behaviour — the tolerant one the codex reader always had: the field
/// reads as 0, the reply still arrives. A token count is telemetry; losing a budget line beats failing an
/// answer the caller has already paid for.</para></summary>
public class WireNumberToleranceTests
{
    // ── the OpenAI-compatible HTTP provider (buffered) ────────────────────────

    [Fact]
    public async Task A_fractional_token_count_still_returns_the_reply()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":10.5,"completion_tokens":4}}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);   // was: FormatException thrown out of CompleteAsync
        Assert.Equal("hello", reply.Text);
        Assert.Equal(0, reply.Usage!.InputTokens);    // unreadable → 0, never a guess
        Assert.Equal(4, reply.Usage.OutputTokens);    // the sibling field is unaffected
    }

    [Fact]
    public async Task A_token_count_past_long_max_still_returns_the_reply()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, """
            {"choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":99999999999999999999,"completion_tokens":4}}
            """);

        var reply = await Provider(handler).CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal(0, reply.Usage!.InputTokens);
    }

    [Fact]
    public async Task An_ollama_eval_count_that_is_not_an_integer_still_returns_the_reply()
    {
        // the other usage shape the same reader covers: Ollama's root-level counts, no `usage` object
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"from ollama"},"done":true,"prompt_eval_count":7,"eval_count":3.5}""");

        var reply = await Provider(handler, c => { c.BaseUrl = "http://localhost:11434"; c.ApiKey = null; })
            .CompleteAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal(7, reply.Usage!.InputTokens);
        Assert.Equal(0, reply.Usage.OutputTokens);
    }

    // ── the OpenAI-compatible HTTP provider (streamed) ────────────────────────

    [Fact]
    public async Task A_fractional_count_on_the_trailing_usage_chunk_does_not_break_the_stream()
    {
        // the sharpest one: ParseStreamLine runs INSIDE the enumerator body, outside every try, so the
        // throw tore down a stream whose content had already been delivered
        const string sse = """
            data: {"choices":[{"delta":{"content":"hi"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: {"choices":[],"usage":{"prompt_tokens":12.5,"completion_tokens":3}}

            data: [DONE]

            """;
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, sse, "text/event-stream");

        var chunks = new List<LlmChunk>();
        await foreach (var c in Provider(handler).StreamAsync(Req)) chunks.Add(c);

        Assert.Equal(["hi"], chunks.Where(c => c.Kind == LlmChunkKind.Content).Select(c => c.Text));
        var final = chunks[^1];
        Assert.Equal(LlmChunkKind.Final, final.Kind);   // was: FormatException out of the enumerator
        Assert.Equal(0, final.Usage!.InputTokens);
        Assert.Equal(3, final.Usage.OutputTokens);
    }

    // ── the claude stream-json readers (both promise "never throws") ──────────

    [Fact]
    public void The_provider_reader_stays_tolerant_of_a_usage_number_it_cannot_read()
    {
        const string line = """
            {"type":"result","result":"done","usage":{"input_tokens":1.5,"output_tokens":340,"cache_read_input_tokens":800},"total_cost_usd":0.012}
            """;

        var evt = StreamJsonParser.Parse(line);

        Assert.Equal(StreamJsonEventKind.Result, evt.Kind);
        Assert.Equal("done", evt.Text);
        Assert.Equal(0, evt.Usage!.InputTokens);
        Assert.Equal(340, evt.Usage.OutputTokens);
        Assert.Equal(0.012, evt.Usage.CostUsd);
    }

    [Fact]
    public void The_agent_reader_stays_tolerant_on_its_live_usage_tick()
    {
        const string line = """
            {"type":"assistant","message":{"content":[{"type":"text","text":"hi"}],
             "usage":{"input_tokens":1.5,"output_tokens":5,"cache_read_input_tokens":0}}}
            """;

        var events = new StreamJsonAgentReader().Read(line).ToList();

        var usage = Assert.Single(events.OfType<UsageLive>());
        Assert.Equal(0, usage.InputTokens);     // legal JSON, not an integral long
        Assert.Equal(5, usage.OutputTokens);
    }

    [Fact]
    public void The_agent_reader_stays_tolerant_on_its_final_usage()
    {
        const string line = """
            {"type":"result","result":"done","session_id":"s1","is_error":false,
             "usage":{"input_tokens":20,"output_tokens":8.25,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}
            """;

        var events = new StreamJsonAgentReader().Read(line).ToList();

        var usage = Assert.Single(events.OfType<UsageFinal>());
        Assert.Equal(20, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
        Assert.Single(events.OfType<SessionEnded>());   // and the terminal still arrives
    }

    // ── the codex envelope: the copy that was already right, kept right ───────

    [Fact]
    public void The_codex_envelope_still_reads_an_unreadable_count_as_zero()
    {
        using var doc = JsonDocument.Parse(
            """{"type":"turn.completed","usage":{"input_tokens":2.5,"output_tokens":7,"cached_input_tokens":1}}""");

        var usage = CodexEnvelope.ReadUsage(doc.RootElement);

        Assert.True(usage.HasValue, "turn.completed carries a usage object");
        Assert.Equal(0, usage!.Value.Input);
        Assert.Equal(7, usage.Value.Output);
        Assert.Equal(1, usage.Value.CacheRead);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static OpenAiCompatibleProvider Provider(StubHttpHandler handler,
        Action<OpenAiCompatibleOptions>? configure = null)
    {
        var config = new OpenAiCompatibleOptions { BaseUrl = "https://api.openai.com", ApiKey = "test-key" };
        configure?.Invoke(config);
        return new OpenAiCompatibleProvider("openai", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }

    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")], Model = "gpt-x" };
}
