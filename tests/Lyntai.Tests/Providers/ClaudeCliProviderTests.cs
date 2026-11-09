using Lyntai.Inference;
using Lyntai;
using Lyntai.Inference.Cli;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>Integration tests against the deterministic provider stub — zero network, zero real tokens.</summary>
public class ClaudeCliProviderTests
{
    public static string StubCommand
    {
        get
        {
            var stub = Path.Combine(TestPaths.DevtoolsDir("scripts"), "provider-stub.mjs");
            return $"node \"{stub}\"";
        }
    }

    private static ClaudeCliProvider Provider(TimeSpan? timeout = null) =>
        new(new ProcessRunner(),
            new LyntaiOptions { ProviderTimeout = timeout ?? TimeSpan.FromSeconds(60) },
            command: StubCommand);

    private static TextRequest Req(string prompt) => new() { Messages = [TextMessage.User(prompt)] };

    [Fact]
    public async Task Completion_returns_stub_text_with_ok_verdict_and_usage()
    {
        var reply = await Provider().CompleteAsync(Req("hello from the tests"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("stub reply: hello from the tests", reply.Text);
        Assert.NotNull(reply.Usage);
        Assert.Equal(1200, reply.Usage.InputTokens);
        Assert.Equal(0.012, reply.Usage.CostUsd);
    }

    [Fact]
    public async Task Empty_output_maps_to_failed()
    {
        var reply = await Provider().CompleteAsync(Req("FORCE_ERROR please"));

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
        Assert.Equal("", reply.Text);
    }

    [Fact]
    public async Task Timeout_kills_and_maps_to_timeout_verdict()
    {
        var reply = await Provider(timeout: TimeSpan.FromSeconds(2)).CompleteAsync(Req("SLOW down"));

        Assert.Equal(ProviderVerdict.Timeout, reply.Verdict);
    }

    [Fact]
    public async Task Streaming_yields_content_then_final_with_usage()
    {
        var chunks = new List<TextChunk>();
        await foreach (var c in Provider().StreamAsync(Req("stream me")))
            chunks.Add(c);

        Assert.True(chunks.Count >= 2);
        Assert.Equal(TextChunkKind.Content, chunks[0].Kind);
        Assert.Equal("stub reply: stream me", string.Concat(chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
        Assert.NotNull(chunks[^1].Usage);
        Assert.Equal(340, chunks[^1].Usage!.OutputTokens);
    }

    /// <summary>A multi-turn call speaks in several assistant messages, and the streamed answer is all of them: the second
    /// and later messages start after a blank line, so they do not run into the first ("I'll search.Answer"). The
    /// buffered answer is the <c>result</c> line's text (the steward's ruling, <c>docs/DECISIONS.md</c> D324).</summary>
    [Fact]
    public async Task A_streamed_multi_turn_answer_separates_its_assistant_messages_with_a_blank_line()
    {
        var runner = new FakeProcessRunner([
            """{"type":"assistant","message":{"content":[{"type":"text","text":"I'll search."}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Grep","input":{}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Answer"}]}}""",
            """{"type":"result","result":"Answer"}""",
        ]);
        var provider = new ClaudeCliProvider(runner, new LyntaiOptions(), command: "claude");

        var chunks = await provider.StreamAsync(Req("search")).ToListAsync();

        Assert.Equal("I'll search.\n\nAnswer", string.Concat(chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text)));
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
    }

    [Fact]
    public async Task Content_without_a_result_line_still_ends_with_final()
    {
        // a CLI/wrapper that exits 0 after assistant text but without the terminal result event
        // delivered a full answer — ending it with Error would record a success as a failed run
        var chunks = new List<TextChunk>();
        await foreach (var c in Provider().StreamAsync(Req("NO_RESULT please")))
            chunks.Add(c);

        Assert.Contains(chunks, c => c.Kind == TextChunkKind.Content);
        Assert.Equal(TextChunkKind.Final, chunks[^1].Kind);
        Assert.DoesNotContain(chunks, c => c.Kind == TextChunkKind.Error);
    }

    [Fact]
    public async Task Streaming_timeout_surfaces_an_error_chunk()
    {
        var chunks = new List<TextChunk>();
        await foreach (var c in Provider(timeout: TimeSpan.FromSeconds(2)).StreamAsync(Req("SLOW stream")))
            chunks.Add(c);

        Assert.Equal(TextChunkKind.Error, chunks[^1].Kind);
        Assert.Equal(ProviderVerdict.Timeout, chunks[^1].Verdict);
    }

    [Fact]
    public void Command_tokenizer_handles_quoted_paths()
    {
        var tokens = CliCommand.Tokenize("node \"C:\\some dir\\stub.mjs\" --flag");

        Assert.Equal(["node", @"C:\some dir\stub.mjs", "--flag"], tokens);
    }

    [Fact]
    public void Explicit_command_makes_the_provider_available()
    {
        Assert.True(Provider().IsAvailable);
    }
}
