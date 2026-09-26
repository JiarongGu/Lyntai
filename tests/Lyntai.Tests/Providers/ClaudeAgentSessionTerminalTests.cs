using Lyntai.Inference;
using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>The single-terminal rule on the claude agent session — the one its codex twin enforces
/// (<c>CodexAgentSessionTests.A_second_in_band_terminal_never_adds_a_second_ending</c>).
///
/// <para><see cref="SessionEnded"/> is documented as "the single terminal event". A duplicate can come from
/// the PROCESS side (a non-zero exit after the reader has ended the turn) or from the STREAM side: a second
/// <c>result</c> line makes <see cref="StreamJsonAgentReader"/> emit a second <see cref="SessionEnded"/>.
/// The damage is not the extra event — it is that <see cref="AgentSessionExtensions.RunAsync"/>'s fold is
/// last-one-wins, so a finished Ok turn followed by one stray error line would fold to a FAILURE.</para></summary>
public class ClaudeAgentSessionTerminalTests
{
    private const string SystemLine =
        """{"type":"system","session_id":"sess-1","model":"claude-opus-4-5"}""";

    private const string OkResultLine =
        """{"type":"result","result":"Done","session_id":"sess-1","is_error":false,"usage":{"input_tokens":20,"output_tokens":8,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}""";

    // a SECOND terminal on the same stream, failing — what a forwarded duplicate would fold to
    private const string LaterErrorResultLine =
        """{"type":"result","result":"","session_id":"sess-1","is_error":true,"subtype":"error_during_execution"}""";

    private static ClaudeAgentSession Session(FakeProcessRunner runner) =>
        new(runner, new LyntaiOptions(), command: "claude");

    private static AgentSessionOptions Ask() => new() { Prompt = "do the thing" };

    [Fact]
    public async Task A_second_in_band_terminal_never_adds_a_second_ending()
    {
        var runner = new FakeProcessRunner([SystemLine, OkResultLine, LaterErrorResultLine]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);   // the FIRST terminal wins
        Assert.False(ended.IsError);
        Assert.Equal("Done", ended.FinalText);
    }

    [Fact]
    public async Task The_fold_reports_the_first_terminal_not_the_last()
    {
        // the consumer-visible half: RunAsync keeps the LAST SessionEnded it sees, so a forwarded stray
        // trailing result line would turn a completed turn into a reported failure with no final text
        var runner = new FakeProcessRunner([SystemLine, OkResultLine, LaterErrorResultLine]);

        var result = await Session(runner).RunAsync(Ask());

        Assert.Equal(ProviderVerdict.Ok, result.Verdict);
        Assert.False(result.IsError);
        Assert.Equal("Done", result.FinalText);
        Assert.Null(result.Subtype);                  // not "error_during_execution"
    }

    [Fact]
    public async Task Events_after_the_terminal_still_flow_and_only_the_second_ending_is_dropped()
    {
        // the suppression is of the ENDING, not of everything after it — the same rule codex states:
        // anything the CLI prints after the terminal may add events but must never re-end the session
        var runner = new FakeProcessRunner([
            SystemLine,
            OkResultLine,
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"trailing"}}}""",
            LaterErrorResultLine,
        ]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        Assert.Contains(events, e => e is TextDelta { Text: "trailing" });
        Assert.Single(events.OfType<SessionEnded>());
    }

    [Fact]
    public async Task The_ordinary_single_terminal_transcript_is_unchanged()
    {
        // the guard must not eat the FIRST terminal — the case every other test depends on
        var runner = new FakeProcessRunner([SystemLine, OkResultLine]);

        var events = await Session(runner).StreamAsync(Ask()).ToListAsync();

        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal("sess-1", ended.SessionId);
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Single(events.OfType<UsageFinal>());
    }
}
