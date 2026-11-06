using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>A host's own program run under the CLI turn's rules, through a plain-line reader with no grammar of the library's:
/// one terminal, the reader told when the output ends and with which code, a process fault made the terminal, and delivery
/// stamped from what the turn showed. <c>CliAgentLoopTests</c> and the two doors' tests hold the same rules for the built-in
/// doors, which run through the same path.</summary>
public class CliAgentTurnTests
{
    private static readonly SessionEnded Success = new(ProviderVerdict.Ok, false, "success", "s-1", "done", null);

    /// <summary>A program that prints plain words: each line is text, and <c>DONE</c> is its own ending.</summary>
    private sealed class PlainReader(Func<int, SessionEnded?>? onEnd = null, string? terminalLine = null) : ICliAgentLineReader
    {
        public List<int> Told { get; } = [];

        public IReadOnlyList<AgentStreamEvent> Read(string line) => line switch
        {
            _ when line == terminalLine => [Success],
            "TOOL" => [new ToolCall("run", "{}", "c1")],
            _ => [new TextDelta(line)],
        };

        public SessionEnded? ReadEnd(int exitCode)
        {
            Told.Add(exitCode);
            return onEnd?.Invoke(exitCode);
        }
    }

    private static Task<List<AgentStreamEvent>> Run(FakeProcessRunner runner, ICliAgentLineReader reader) =>
        CliAgentTurn.RunAsync(runner, "plain", ["--go"], reader, TimeSpan.FromSeconds(30), prompt: "hi").ToListAsync().AsTask();

    [Fact] // a program with no ending of its own that exits 0: the reader's end is the success
    public async Task A_clean_exit_with_no_terminal_ends_as_the_reader_says()
    {
        var reader = new PlainReader(code => code == 0 ? Success : null);

        var events = await Run(new FakeProcessRunner(["hello", "world"]), reader);

        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Equal(["hello", "world"], events.OfType<TextDelta>().Select(t => t.Text));
        Assert.Equal([0], reader.Told);
        Assert.Equal([AgentShownKind.Text], ended.Turn!.Shown);
    }

    [Fact] // …and a reader with nothing to say leaves the turn failed, as for a door that never printed its ending
    public async Task A_clean_exit_the_reader_does_not_end_is_a_failed_turn()
    {
        var reader = new PlainReader();

        var ended = Assert.Single((await Run(new FakeProcessRunner(["hello"]), reader)).OfType<SessionEnded>());

        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        Assert.Equal("no-terminal-event", ended.Failure!.By);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure.Kind);
        Assert.Contains("never terminated", ended.Diagnostic);
    }

    [Fact] // a non-zero exit: the reader is told the code and its terminal outranks the fault's default
    public async Task A_non_zero_exit_is_ended_by_the_readers_terminal_when_it_has_one()
    {
        var failed = new SessionEnded(ProviderVerdict.Failed, true, "bad-usage", null, null, "the program said usage")
            { Failure = new AgentFailure(AgentFailureKind.Fatal, "plain.exit=2") };
        var reader = new PlainReader(code => code == 2 ? failed : null);
        var runner = new FakeProcessRunner(["hello"], new ProcessRunException("plain", 2, "usage: plain"));

        var ended = Assert.Single((await Run(runner, reader)).OfType<SessionEnded>());

        Assert.Equal([2], reader.Told);
        Assert.Equal("bad-usage", ended.Subtype);
        Assert.Equal("plain.exit=2", ended.Failure!.By);
        Assert.Equal(AgentDelivery.Taken, ended.Failure.Delivery);   // the program answered before it failed
    }

    [Fact] // …and a reader with no terminal for it leaves the fatal default, which is the process fault
    public async Task A_non_zero_exit_the_reader_does_not_end_is_the_process_fault()
    {
        var reader = new PlainReader();
        var runner = new FakeProcessRunner([], new ProcessRunException("plain", 2, "usage: plain"));

        var ended = Assert.Single((await Run(runner, reader)).OfType<SessionEnded>());

        Assert.Equal([2], reader.Told);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal("process.exit", ended.Failure.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);   // nothing came back, so nothing shows it was read
    }

    [Fact] // the first terminal wins: what the program prints after it is events, the end-of-output terminal is dropped
    public async Task A_program_that_prints_more_after_its_terminal_still_ends_once()
    {
        var late = new SessionEnded(ProviderVerdict.Failed, true, "late", null, null, "too late");
        var reader = new PlainReader(_ => late, terminalLine: "DONE");

        var events = await Run(new FakeProcessRunner(["one", "DONE", "two"]), reader);

        var ended = Assert.Single(events.OfType<SessionEnded>());
        Assert.Equal("success", ended.Subtype);
        Assert.Equal(["one", "two"], events.OfType<TextDelta>().Select(t => t.Text));   // the later line is still an event
        Assert.Equal([0], reader.Told);   // told that the output ended, with nothing left to decide
    }

    [Fact] // a non-zero exit after the reader's own terminal leaves that terminal standing, and the reader is still told
    public async Task A_non_zero_exit_after_the_terminal_does_not_replace_it()
    {
        var reader = new PlainReader(terminalLine: "DONE");
        var runner = new FakeProcessRunner(["DONE"], new ProcessRunException("plain", 3, "late failure"));

        var ended = Assert.Single((await Run(runner, reader)).OfType<SessionEnded>());

        Assert.Equal("success", ended.Subtype);
        Assert.Equal([3], reader.Told);
    }

    [Fact] // delivery is the turn's: a tool step that had begun is uncertain, an answer is taken
    public async Task A_failed_terminal_is_stamped_from_what_the_turn_showed()
    {
        var failing = new SessionEnded(ProviderVerdict.Failed, true, null, null, null, "boom");

        var afterTool = await Run(new FakeProcessRunner(["TOOL"]), new PlainReader(_ => failing));
        var afterText = await Run(new FakeProcessRunner(["hello"]), new PlainReader(_ => failing));
        var afterNothing = await Run(new FakeProcessRunner([]), new PlainReader(_ => failing));

        Assert.Equal(AgentDelivery.Uncertain, afterTool.OfType<SessionEnded>().Single().Failure!.Delivery);
        Assert.Equal(AgentDelivery.Taken, afterText.OfType<SessionEnded>().Single().Failure!.Delivery);
        Assert.Equal(AgentDelivery.NeverSent, afterNothing.OfType<SessionEnded>().Single().Failure!.Delivery);
    }

    [Fact] // a stall is the runner's, and the reader is not asked to end it
    public async Task A_stalled_program_is_a_transient_fault_the_reader_is_not_told_of()
    {
        var reader = new PlainReader(_ => Success);
        var runner = new FakeProcessRunner(["hello"], new ProcessTimeoutException("plain", TimeSpan.FromSeconds(30)));

        var ended = Assert.Single((await Run(runner, reader)).OfType<SessionEnded>());

        Assert.Empty(reader.Told);
        Assert.Equal("timeout", ended.Subtype);
        Assert.Equal(AgentFailureKind.Transient, ended.Failure!.Kind);
    }

    [Fact] // the program is spawned by the runner the way the library spawns its own: argv, the prompt on stdin, the window
    public async Task The_program_is_started_through_the_runner_with_the_prompt_on_stdin()
    {
        var runner = new FakeProcessRunner(["hello"]);

        await Run(runner, new PlainReader(_ => Success));

        var call = Assert.Single(runner.Calls);
        Assert.Equal(("plain", "hi", TimeSpan.FromSeconds(30)), (call.Command, call.Stdin, call.InactivityTimeout));
        Assert.Equal(["--go"], call.Args);
    }

    [Fact]
    public void A_turn_needs_a_program_a_reader_and_a_positive_window()
    {
        var runner = new FakeProcessRunner();
        var reader = new PlainReader();

        Assert.Throws<ArgumentException>(() => CliAgentTurn.RunAsync(runner, " ", [], reader, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentNullException>(() => CliAgentTurn.RunAsync(runner, "plain", [], null!, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CliAgentTurn.RunAsync(runner, "plain", [], reader, TimeSpan.Zero));
    }
}
