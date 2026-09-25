using System.Net;
using Lyntai.Inference;
using Lyntai.Providers.Ollama;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>Ollama streams each tool call COMPLETE on its own NDJSON line, and the calls on separate lines
/// must never share a slot. MEASURED on Ollama 0.34.2 (qwen3:4b, 2026-09-25, a two-tool prompt): two lines,
/// one call each, the <c>index</c> nested inside <c>function</c> rather than at the top level — so a slot
/// read from a top-level index (else position within the line) put both calls in slot 0, kept the first
/// name, and appended the arguments into <c>{"city":"Paris"}{"city":"Tokyo"}</c>.</summary>
public class OllamaStreamedToolCallTests
{
    private const string MeasuredTwoToolStream = """
        {"model":"qwen3:4b","created_at":"2026-09-25T07:23:59.7516114Z","message":{"role":"assistant","content":"","tool_calls":[{"id":"call_217hxy2d","function":{"index":0,"name":"get_weather","arguments":{"city":"Paris"}}}]},"done":false}
        {"model":"qwen3:4b","created_at":"2026-09-25T07:23:59.9969816Z","message":{"role":"assistant","content":"","tool_calls":[{"id":"call_e3bxxntc","function":{"index":1,"name":"get_time","arguments":{"city":"Tokyo"}}}]},"done":false}
        {"model":"qwen3:4b","created_at":"2026-09-25T07:24:00.0211469Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":188,"eval_count":472}

        """;

    /// <summary>The defensive half: no index and no id anywhere, which the measured build does not send
    /// but nothing guarantees every build will.</summary>
    private const string UnnumberedTwoToolStream = """
        {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"get_weather","arguments":{"city":"Paris"}}}]},"done":false}
        {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"get_time","arguments":{"city":"Tokyo"}}}]},"done":false}
        {"message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":1,"eval_count":1}

        """;

    [Fact]
    public async Task Two_calls_on_two_lines_stream_as_two_calls()
    {
        var calls = await StreamedCalls(MeasuredTwoToolStream);

        Assert.Equal(2, calls.Count);
        Assert.Equal(("call_217hxy2d", "get_weather", """{"city":"Paris"}"""),
            (calls[0].Id, calls[0].Name, calls[0].ArgumentsJson));
        Assert.Equal(("call_e3bxxntc", "get_time", """{"city":"Tokyo"}"""),
            (calls[1].Id, calls[1].Name, calls[1].ArgumentsJson));
    }

    [Fact]
    public async Task Unnumbered_calls_on_two_lines_still_stream_as_two_calls()
    {
        var calls = await StreamedCalls(UnnumberedTwoToolStream);

        Assert.Equal(["get_weather", "get_time"], calls.Select(c => c.Name));
        Assert.Equal(["""{"city":"Paris"}""", """{"city":"Tokyo"}"""], calls.Select(c => c.ArgumentsJson));
        Assert.Equal(2, calls.Select(c => c.Id).Distinct().Count());
    }

    private static async Task<List<TextToolCall>> StreamedCalls(string ndjson)
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, ndjson, "application/x-ndjson");
        var provider = new OllamaProvider("ollama", new OllamaOptions(),
            () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

        var chunks = await provider.StreamAsync(new TextRequest
        {
            Messages = [TextMessage.User("weather in Paris, time in Tokyo")],
            Model = "qwen3:4b",
        }).ToListAsync();

        return [.. chunks.Where(c => c.Kind == TextChunkKind.ToolCall).Select(c => c.ToolCall!)];
    }
}
