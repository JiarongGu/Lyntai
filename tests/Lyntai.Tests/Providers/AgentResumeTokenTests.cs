using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>Both CLI agent sessions refuse the SAME resume tokens, before spawning anything. The token is
/// free-form data in a data slot, and <c>ArgumentList</c> stops shell injection, not the CLI's own parser
/// reading the value as an option. On claude that is worse than a wrong thread: <c>--resume [value]</c>
/// takes an OPTIONAL value (measured, <c>claude --help</c>, 2.1.281), so a token such as
/// <c>--dangerously-skip-permissions</c> is parsed as that flag and bypasses every permission prompt.</summary>
public class AgentResumeTokenTests
{
    private static readonly string[] ClaudeTranscript =
    [
        """{"type":"system","session_id":"sess-abc123","model":"claude-opus-4-5"}""",
        """{"type":"result","result":"Done","session_id":"sess-abc123","is_error":false,"subtype":null}""",
    ];

    private static readonly string[] CodexTranscript =
    [
        """{"type":"thread.started","thread_id":"t-1"}""",
        """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"ok"}}""",
        """{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}""",
    ];

    public static TheoryData<string, string> FlagShapedTokens() => new()
    {
        { "claude", "--dangerously-skip-permissions" },
        { "claude", "-c" },
        { "claude", "   " },
        { "claude", " --model" },
        { "codex", "--last" },
        { "codex", "-i" },
        { "codex", "   " },
        { "codex", " --last" },
    };

    private static IAgentSession Session(string backend, FakeProcessRunner runner) => backend switch
    {
        "claude" => new ClaudeAgentSession(runner, new LyntaiOptions(), command: "claude"),
        _ => new CodexAgentSession(runner, new LyntaiOptions(), command: "codex"),
    };

    private static FakeProcessRunner Runner(string backend) =>
        new(backend == "claude" ? ClaudeTranscript : CodexTranscript);

    [Theory]
    [MemberData(nameof(FlagShapedTokens))]
    public async Task A_resume_token_the_cli_would_read_as_an_option_is_refused_without_spawning(
        string backend, string token)
    {
        var runner = Runner(backend);

        var events = await Session(backend, runner)
            .StreamAsync(new AgentSessionOptions { Prompt = "hi", ResumeToken = token }).ToListAsync();

        var ended = Assert.IsType<SessionEnded>(Assert.Single(events));
        Assert.True(ended.IsError);
        Assert.Equal(ProviderVerdict.Unsupported, ended.Verdict);
        Assert.Equal("resume-token-invalid", ended.Subtype);
        Assert.Contains("session id", ended.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    [Theory] // the positive control: a real id still resumes on both backends
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task A_session_id_is_forwarded_as_the_resume_value(string backend)
    {
        var runner = Runner(backend);

        var events = await Session(backend, runner)
            .StreamAsync(new AgentSessionOptions { Prompt = "hi", ResumeToken = "sess-xyz" }).ToListAsync();

        Assert.Equal(ProviderVerdict.Ok, events.OfType<SessionEnded>().Single().Verdict);
        Assert.Contains("sess-xyz", runner.LastArgs!);
    }

    [Theory] // an empty token means "start fresh" on both, never a refusal
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task An_empty_token_starts_a_fresh_session(string backend)
    {
        var runner = Runner(backend);

        var events = await Session(backend, runner)
            .StreamAsync(new AgentSessionOptions { Prompt = "hi", ResumeToken = "" }).ToListAsync();

        Assert.Equal(ProviderVerdict.Ok, events.OfType<SessionEnded>().Single().Verdict);
        Assert.DoesNotContain("--resume", runner.LastArgs!);
        Assert.DoesNotContain("resume", runner.LastArgs!);
    }
}
