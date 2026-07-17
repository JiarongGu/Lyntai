using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// OPT-IN end-to-end proof of NATIVE tool-calling against a real local Ollama: registers a real tool,
/// runs the full <see cref="IToolLoop"/> through the OpenAI-compatible (Ollama) provider, and asserts
/// the model actually called the tool and the loop returned a tool-informed answer — not just the stub.
/// Runs only when <c>LYNTAI_LIVE_OLLAMA</c> is set AND the endpoint is reachable; otherwise a no-op pass
/// (xUnit v2 has no dynamic <c>Assert.Skip</c>, hence the early return).
///
/// Enable:  set LYNTAI_LIVE_OLLAMA=1  (optionally LYNTAI_OLLAMA_TOOL_MODEL, default "llama3.1" — pick a
/// tool-capable model you have pulled; llama3.1 / qwen2.5 / llama3.2 support tools).
/// </summary>
public class OllamaToolCallLiveTests
{
    private static string BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL") ?? "http://localhost:11434";
    private static string Model => Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_TOOL_MODEL") ?? "llama3.1";

    private static async Task<bool> LiveAsync()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_OLLAMA"))) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"{BaseUrl}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Model_calls_a_registered_tool_and_the_loop_returns_a_tool_informed_answer()
    {
        if (!await LiveAsync()) return;

        var addCalls = 0;
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOllamaProvider(baseUrl: BaseUrl, defaultModel: Model)
            .AddTool(_ => new FunctionTool(
                name: "add",
                invoke: (argsJson, _) =>
                {
                    Interlocked.Increment(ref addCalls);
                    using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
                    var a = doc.RootElement.GetProperty("a").GetDouble();
                    var b2 = doc.RootElement.GetProperty("b").GetDouble();
                    return Task.FromResult((a + b2).ToString(System.Globalization.CultureInfo.InvariantCulture));
                },
                description: "Add two numbers a and b and return the sum.",
                parametersJsonSchema: """{"type":"object","properties":{"a":{"type":"number"},"b":{"type":"number"}},"required":["a","b"]}"""))
            .Configure(o => o.ProviderTimeout = TimeSpan.FromMinutes(3)) // cold model load can be slow
            .DefaultCandidates("ollama"));
        using var sp = services.BuildServiceProvider();

        // the Ollama provider advertises native tool-calling, so the loop takes the native path
        Assert.True(sp.GetRequiredService<ILlmClient>().SupportsToolCalls(new LlmRequest { Messages = [LlmMessage.User("x")] }));

        var result = await sp.GetRequiredService<IToolLoop>().RunAsync(new LlmRequest
        {
            Messages = [LlmMessage.User("What is 17 plus 25? Use the add tool, then state the result.")],
            Temperature = 0,
        });

        Assert.Equal(LlmVerdict.Ok, result.Verdict);
        Assert.True(addCalls > 0, "the model should have called the add tool");
        Assert.Contains("add", result.Steps.Select(s => s.Tool));
        Assert.Contains("42", result.Answer); // 17 + 25, surfaced back through the tool result
    }
}
