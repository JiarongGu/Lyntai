using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The agent-turn loop is ONE loop for both CLI sessions, so they end a turn the same way: one
/// terminal, the first wins, a fault classified the same, and the two no-terminal cases told apart — "printed
/// nothing" versus "streamed, then died before its terminal", which send whoever is debugging to different
/// places.</summary>
public class CliAgentLoopTests
{
    private static readonly Dictionary<string, string[]> ContentWithoutTerminal = new()
    {
        ["claude"] =
        [
            """{"type":"system","session_id":"s-1","model":"m"}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"half"}}}""",
        ],
        ["codex"] =
        [
            """{"type":"thread.started","thread_id":"s-1"}""",
            """{"type":"item.completed","item":{"id":"a","type":"agent_message","text":"half"}}""",
        ],
    };

    private static IAgentSession Session(string backend, FakeProcessRunner runner) => backend switch
    {
        "claude" => new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude"),
        _ => new CodexAgentSession(runner, new LyntaiOptions(), command: "codex"),
    };

    private static Task<List<AgentStreamEvent>> Run(string backend, FakeProcessRunner runner) =>
        Session(backend, runner).StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync().AsTask();

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task Content_without_a_terminal_is_an_unterminated_turn_not_no_output(string backend)
    {
        var events = await Run(backend, new FakeProcessRunner(ContentWithoutTerminal[backend]));

        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        Assert.True(ended.IsError);
        Assert.Contains("never terminated", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("s-1", ended.SessionId);
        Assert.Null(ended.FinalText);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task Nothing_printed_is_no_output(string backend)
    {
        var ended = Assert.Single((await Run(backend, new FakeProcessRunner([]))).OfType<SessionEnded>());

        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        Assert.Contains("no output", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_fault_after_content_is_classified_and_carries_the_session_id(string backend)
    {
        var runner = new FakeProcessRunner(ContentWithoutTerminal[backend],
            new ProcessRunException(backend, 1, "429 Too Many Requests"));

        var ended = Assert.Single((await Run(backend, runner)).OfType<SessionEnded>());

        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        Assert.Equal("exit 1: 429 Too Many Requests", ended.Diagnostic);
        Assert.Equal("s-1", ended.SessionId);
    }
}
