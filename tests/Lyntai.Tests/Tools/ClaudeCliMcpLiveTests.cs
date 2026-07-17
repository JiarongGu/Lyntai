using Lyntai;
using Lyntai.Agents;
using Lyntai.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Tools;

/// <summary>
/// OPT-IN end-to-end proof with the REAL <c>claude</c> binary: registers a tool whose answer the model
/// can't know, hosts it (AddClaudeCliMcpTools), and asserts the CLI actually called it and surfaced the
/// value. Runs only when <c>LYNTAI_LIVE_CLI_TOOLS</c> is set AND an authenticated <c>claude</c> is on
/// PATH; otherwise a no-op pass (it consumes real tokens, so it's never in the default suite).
///
/// Enable:  set LYNTAI_LIVE_CLI_TOOLS=1   (authenticated `claude` CLI required)
/// </summary>
public class ClaudeCliMcpLiveTests
{
    private static bool Live => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LYNTAI_LIVE_CLI_TOOLS"));

    [Fact]
    public async Task Real_claude_cli_calls_a_hosted_tool_and_surfaces_its_result()
    {
        if (!Live) return;

        var called = 0;
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddClaudeCliProvider()
            .AddClaudeCliMcpTools()
            .AddTool(_ => new FunctionTool("get_secret_word",
                (_, _) => { Interlocked.Increment(ref called); return Task.FromResult("banana"); },
                "Returns today's secret word. Call this to learn the secret word.",
                """{"type":"object","properties":{}}"""))
            .Configure(o => o.ProviderTimeout = TimeSpan.FromMinutes(3))
            .DefaultCandidates("claude-cli"));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ILlmClient>().CompleteAsync(new LlmRequest
        {
            Messages = [LlmMessage.User("Call the get_secret_word tool, then tell me the secret word.")],
        });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.True(called > 0, "the CLI's agent should have called the hosted tool");
        Assert.Contains("banana", reply.Text, StringComparison.OrdinalIgnoreCase);
    }
}
