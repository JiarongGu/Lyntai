using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// A run kept across turns on the claude CLI with stdin held open (AG51), through a CLI scripted line by line.
///
/// <para>Every frame is labelled. <b>RECORDED</b>: quoted from a host's record of a real run, its stream-json note (claude 2.1.281:
/// the user line, two turns on one process, the interrupt and its answer) or the shape its own test carries as that probe's (the
/// interrupt marker and the cut turn's result). <b>COMPOSED</b>: built from field names a record or this door's own recorded tests
/// state, with made-up values (an init, a stream delta, an assistant message, a result, a task's end). <b>SYNTHETIC</b>: a frame no
/// record shows, written to pin what the run does with it (a CLI speaking while no turn of the host's runs).</para>
/// </summary>
public class ClaudeCliAgentRunTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    /// <summary>A CLI that answers each line the run writes with what the test scripts, and speaks when the test says.</summary>
    private sealed class KeptCli : IProcessRunner
    {
        private readonly Channel<string> _out = Channel.CreateUnbounded<string>();
        private readonly List<JsonNode> _written = [];
        private int _spawns;
        private int _said;
        private int _consumed;

        public Func<JsonNode, IEnumerable<string>> Reply { get; set; } = _ => [];

        public bool CanPauseInactivity { get; init; } = true;

        /// <summary>A CLI that does not go when its stdin closes, so only a kill ends it.</summary>
        public bool IgnoresStdinClose { get; init; }

        /// <summary>Thrown at the first read, before stdin is asked for: the process never started.</summary>
        public Exception? SpawnFault { get; init; }

        public InactivityPause? Pause { get; private set; }

        public IReadOnlyList<string>? Args { get; private set; }

        public IReadOnlyDictionary<string, string?>? Environment { get; private set; }

        public bool StdinClosed { get; private set; }

        public int Spawns => Volatile.Read(ref _spawns);

        public List<JsonNode> Written
        {
            get
            {
                lock (_written) return [.. _written];
            }
        }

        public void Say(params string[] lines)
        {
            foreach (var line in lines)
            {
                Interlocked.Increment(ref _said);
                _out.Writer.TryWrite(line);
            }
        }

        public void EndOutput() => _out.Writer.TryComplete();

        /// <summary>Every line said has been read and the run came back for the next, so it has acted on each.</summary>
        public Task Settled() => Eventually(() => Volatile.Read(ref _consumed) >= Volatile.Read(ref _said));

        public async Task Wrote(Func<JsonNode, bool> match)
        {
            var until = DateTime.UtcNow + Wait;
            while (!Written.Any(match))
            {
                Assert.True(DateTime.UtcNow < until, "the run never wrote the line");
                await Task.Delay(10);
            }
        }

        public IAsyncEnumerable<string> ExchangeLinesAsync(string command, IReadOnlyList<string> args, IAsyncEnumerable<string> stdinLines,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("a run pauses its window, so it spawns through the pause overload");

        public IAsyncEnumerable<string> ExchangeLinesAsync(string command, IReadOnlyList<string> args, IAsyncEnumerable<string> stdinLines,
            InactivityPause pause, TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default)
        {
            Pause = pause;
            Args = args;
            Environment = environment;
            return Exchange(stdinLines, ct);
        }

        private async IAsyncEnumerable<string> Exchange(IAsyncEnumerable<string> stdinLines, [EnumeratorCancellation] CancellationToken ct)
        {
            if (SpawnFault is { } fault) throw fault;
            Interlocked.Increment(ref _spawns);
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var line in stdinLines)
                    {
                        var node = JsonNode.Parse(line)!;
                        lock (_written) _written.Add(node);
                        Say([.. Reply(node)]);
                    }
                }
                catch (OperationCanceledException)
                {
                    // the process was killed
                }

                StdinClosed = true;
                if (!IgnoresStdinClose) _out.Writer.TryComplete();   // a CLI whose stdin closed ends its session and goes
            });
            await foreach (var line in _out.Reader.ReadAllAsync(ct))
            {
                yield return line;
                Interlocked.Increment(ref _consumed);
            }
        }

        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null, TimeSpan? inactivityTimeout = null,
            TimeSpan? maxDuration = null, string? workingDirectory = null, IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // ── the wire ─────────────────────────────────────────────────────────────

    private static string Type(JsonNode line) => line["type"]?.GetValue<string>() ?? "";

    private static bool IsUser(JsonNode line) => Type(line) == "user";

    private static bool IsInterrupt(JsonNode line) =>
        Type(line) == "control_request" && line["request"]?["subtype"]?.GetValue<string>() == "interrupt";

    private static string RequestId(JsonNode line) => line["request_id"]!.GetValue<string>();

    private static string UserText(JsonNode line) => line["message"]!["content"]![0]!["text"]!.GetValue<string>();

    // COMPOSED: the fields the stream-json note lists for a turn's opening frame
    private static string Init(string session = "s1") =>
        $$"""{"type":"system","subtype":"init","session_id":"{{session}}","model":"claude-test","tools":["Read","Bash"]}""";

    // COMPOSED: the model's stream as stream_event frames (the note's message_start, content_block_delta with text_delta)
    private static string MessageStart(string id) =>
        $$$"""{"type":"stream_event","event":{"type":"message_start","message":{"id":"{{{id}}}"}},"session_id":"s1"}""";

    private static string Delta(string text) =>
        $$$"""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{{{text}}}"}},"session_id":"s1"}""";

    // COMPOSED: the whole assistant message after the stream
    private static string Assistant(string text, string id = "m1") =>
        $$$"""{"type":"assistant","message":{"id":"{{{id}}}","model":"claude-test","content":[{"type":"text","text":"{{{text}}}"}],"usage":{"input_tokens":3,"output_tokens":2,"cache_read_input_tokens":0}},"session_id":"s1"}""";

    private static string ToolUse(string id) =>
        $$$"""{"type":"assistant","message":{"id":"m-tool","content":[{"type":"tool_use","id":"{{{id}}}","name":"Bash","input":{"command":"sleep 30"}}]},"session_id":"s1"}""";

    // COMPOSED: the turn's one result (the note: subtype, is_error, the final text, usage)
    private static string Result(string text) =>
        $$$"""{"type":"result","subtype":"success","is_error":false,"result":"{{{text}}}","session_id":"s1","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}""";

    private static string[] Turn(string text, string id = "m1") => [Init(), MessageStart(id), Delta(text), Assistant(text, id), Result(text)];

    // RECORDED (the stream-json note, CONV4a, 2.1.281): the interrupt's answer
    private static string InterruptAnswered(string id) =>
        $$$$"""{"type":"control_response","response":{"subtype":"success","request_id":"{{{{id}}}}","response":{"still_queued":[]}}}""";

    // RECORDED (the probe's shape, as a host's test of it carries it): the marker an interrupt leaves, and the cut turn's result
    private const string InterruptMarker =
        """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"[Request interrupted by user]"}]},"session_id":"c1"}""";

    private static string CancelledResult(string terminal) =>
        $$"""{"type":"result","subtype":"error_during_execution","is_error":true,"stop_reason":null,"terminal_reason":"{{terminal}}","errors":["[ede_diagnostic] result_type=user"]}""";

    // COMPOSED (CONV4a: the partial assistant message carries aborted at its top level)
    private static string AbortedAssistant(string text) =>
        $$"""{"type":"assistant","aborted":true,"message":{"id":"m1","content":[{"type":"text","text":"{{text}}"}]},"session_id":"s1"}""";

    // COMPOSED (the CONV4a note: a tool cut by the interrupt is answered with the harness's own sentence, is_error true)
    private static string ToolRefused(string id) =>
        $$"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"{{id}}","content":"The user doesn't want to proceed with this tool use.","is_error":true}]},"session_id":"s1"}""";

    // COMPOSED: a task's end, as this door's recorded task tests carry it
    private const string TaskEnded = """{"type":"system","subtype":"task_notification","task_id":"t1","tool_use_id":"toolu_1","status":"completed"}""";

    private const string Allowance = """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1792000000,"rateLimitType":"five_hour"}}""";

    // ── the run ──────────────────────────────────────────────────────────────

    private static AgentSessionOptions Session() => new() { Prompt = "", WorkingDirectory = TestPaths.TestScratchDir };

    private static async Task<IAgentRun> Open(
        KeptCli cli, AgentSessionOptions? options = null, TimeProvider? time = null, AgentRunOptions? run = null) =>
        await new ClaudeAgentSession(cli, new LyntaiOptions(), null, "claude", null, time)
            .OpenRunAsync(options ?? Session(), run, default)
            .WaitAsync(Wait);

    private static async Task<List<AgentStreamEvent>> Read(AgentRunTurn turn) => await turn.Events.ToListAsync().AsTask().WaitAsync(Wait);

    private static SessionEnded Ended(IEnumerable<AgentStreamEvent> events) => Assert.IsType<SessionEnded>(events.Last());

    private static string? ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var i = args.ToList().IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the condition never held");
            await Task.Delay(10);
        }
    }

    // ── turns on one process ─────────────────────────────────────────────────

    [Fact] // RECORDED: two user lines on one process, each its own turn answered in order (the stream-json note)
    public async Task Two_turns_run_on_one_process_each_a_user_line_with_its_own_result()
    {
        var cli = new KeptCli();
        cli.Reply = line => IsUser(line) ? Turn(UserText(line) == "one" ? "first" : "second") : [];
        await using var run = await Open(cli);
        Assert.Null(run.SessionId);   // no init has come: the CLI says it with the first turn

        var first = await run.SendAsync("one");
        var firstEvents = await Read(first);
        var second = await run.SendAsync("two");
        var secondEvents = await Read(second);

        Assert.Equal(1, cli.Spawns);
        Assert.Equal(["one", "two"], cli.Written.Select(UserText));
        Assert.All(cli.Written, line => Assert.Equal(["type", "message"], line.AsObject().Select(p => p.Key)));   // no priority, no id
        Assert.Equal("user", cli.Written[0]["message"]!["role"]!.GetValue<string>());
        Assert.Equal("s1", Assert.IsType<SessionStarted>(firstEvents[0]).SessionId);
        Assert.DoesNotContain(secondEvents, e => e is SessionStarted);   // the second turn's init names the same session
        Assert.Equal("first", Assert.Single(firstEvents.OfType<TextDelta>()).Text);
        Assert.Equal("second", Assert.Single(secondEvents.OfType<TextDelta>()).Text);
        foreach (var (turn, events) in new[] { (first, firstEvents), (second, secondEvents) })
        {
            Assert.Single(events.OfType<SessionEnded>());
            Assert.Equal((ProviderVerdict.Ok, false, "s1"), (Ended(events).Verdict, Ended(events).IsError, Ended(events).SessionId));
            Assert.Equal(AgentRunTurnOrigin.Sent, turn.Origin);
            Assert.Equal(Ended(events), await turn.Ended);
        }

        Assert.Equal("second", Ended(secondEvents).FinalText);
        Assert.Equal("claude-test", secondEvents.OfType<UsageFinal>().Single().Model);   // the model is the run's
        Assert.Equal("s1", run.SessionId);
    }

    [Fact] // the one-turn door's argv, reading its stdin as stream-json; the system prompt rides the launch, so every turn has it
    public async Task The_argv_is_the_one_turn_doors_reading_stream_json_input_and_the_system_prompt_holds_for_the_run()
    {
        var cli = new KeptCli();
        cli.Reply = line => IsUser(line) ? [Result("ok")] : [];
        var options = Session() with { SystemPrompt = "be brief", Model = "claude-x" };
        await using var run = await Open(cli, options);

        await (await run.SendAsync("one")).Ended.WaitAsync(Wait);
        await (await run.SendAsync("two")).Ended.WaitAsync(Wait);

        Assert.True(ClaudeAgentArgs.TryBuild(options, (_, _) => "<unused>", out var oneTurn, out _, out _));
        var args = cli.Args!;
        Assert.Equal("stream-json", ValueAfter(args, "--input-format"));
        Assert.Equal([.. oneTurn], args.Where((_, i) => i != args.ToList().IndexOf("--input-format") && i != args.ToList().IndexOf("--input-format") + 1));
        Assert.DoesNotContain("--replay-user-messages", args);
        Assert.Equal("be brief", ValueAfter(args, "--append-system-prompt"));
        Assert.Equal(["one", "two"], cli.Written.Select(UserText));   // never led by the system prompt
        Assert.DoesNotContain(cli.Written, line => Type(line) == "control_request");   // no initialize: the open is the spawn
    }

    [Fact] // a turn's fallback words are its own: an empty result after an assistant message in an EARLIER turn names nothing
    public async Task An_empty_result_never_takes_the_prior_turns_words()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);

        var first = await run.SendAsync("one");
        cli.Say(Init(), Assistant("first words"), Result(""));
        var firstEnded = Ended(await Read(first));
        var second = await run.SendAsync("two");
        cli.Say(Result(""));
        var secondEnded = Ended(await Read(second));

        Assert.Equal("first words", firstEnded.FinalText);
        Assert.True(string.IsNullOrEmpty(secondEnded.FinalText));
    }

    // ── a stop ───────────────────────────────────────────────────────────────

    [Theory] // RECORDED: the interrupt, its answer, the marker and the cut turn's result; then an ordinary turn (CONV4a)
    [InlineData("aborted_streaming")]
    [InlineData("aborted_tools")]
    public async Task A_stop_interrupts_the_turn_which_ends_cancelled_and_a_turn_after_it_runs(string terminal)
    {
        var cli = new KeptCli();
        cli.Reply = line => IsInterrupt(line)
            ? terminal == "aborted_tools"
                ? [InterruptAnswered(RequestId(line)), ToolRefused("toolu_long"), InterruptMarker.Replace("user]", "user for tool use]"), CancelledResult(terminal)]
                : [InterruptAnswered(RequestId(line)), AbortedAssistant("half an ans"), InterruptMarker, CancelledResult(terminal)]
            : IsUser(line) && UserText(line) == "long"
                ? terminal == "aborted_tools" ? [Init(), ToolUse("toolu_long")] : [Init(), MessageStart("m1"), Delta("half an")]
                : IsUser(line) ? [Result("again")] : [];
        await using var run = await Open(cli);
        var first = await run.SendAsync("long");
        await cli.Settled();

        Assert.True(await run.StopAsync());
        Assert.False(await run.StopAsync());   // one stop is out
        var events = await Read(first);
        var ended = Ended(events);
        var next = await run.SendAsync("again");

        var interrupt = Assert.Single(cli.Written, IsInterrupt);
        Assert.Equal(["type", "request_id", "request"], interrupt.AsObject().Select(p => p.Key));
        Assert.Equal((ProviderVerdict.Failed, true, AgentRunSubtypes.Cancelled), (ended.Verdict, ended.IsError, ended.Subtype));
        Assert.Equal($"claude.terminal_reason={terminal}", ended.Failure!.By);
        Assert.DoesNotContain(events, e => e is TextDelta { Text: "half an ans" } or TextDelta { Text: "[Request interrupted by user]" });
        Assert.Equal(ProviderVerdict.Ok, Ended(await Read(next)).Verdict);
        Assert.Equal(1, cli.Spawns);
        Assert.False(await run.StopAsync());   // nothing runs
    }

    [Fact]
    public async Task A_stop_the_cli_never_answers_ends_the_run_after_the_grace()
    {
        var time = new ManualTimeProvider();
        var cli = new KeptCli();
        await using var run = await Open(cli, time: time);
        var turn = await run.SendAsync("long");

        Assert.True(await run.StopAsync());
        await Eventually(() => time.Pending > 0);
        time.Advance(ClaudeCliAgentRun.DefaultStopGrace + TimeSpan.FromSeconds(1));

        var ended = Ended(await Read(turn));
        Assert.True(ended.IsError);
        Assert.Equal("run-killed", ended.Failure!.By);
        Assert.Equal(AgentRunSubtypes.Over, (await run.Ended.WaitAsync(Wait)).Subtype);
    }

    // ── what the run refuses ─────────────────────────────────────────────────

    [Fact] // STEER3 (2.1.322): a word on a busy CLI folds at a tool step but is the next turn after a streaming one, and nothing tells which
    public async Task A_word_on_a_busy_run_is_refused_busy_and_no_line_is_written()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        var first = await run.SendAsync("one");
        cli.Say(Init(), MessageStart("m1"), Delta("work"));
        await cli.Settled();

        var refused = await run.SendAsync("a word");

        var ended = Ended(await Read(refused));
        Assert.Equal((ProviderVerdict.Unsupported, AgentRunSubtypes.Busy, AgentDelivery.NeverSent), (ended.Verdict, ended.Subtype, ended.Failure!.Delivery));
        Assert.Equal(AgentRunTurnOrigin.Sent, refused.Origin);
        cli.Say(Result("done"));
        Assert.Equal(ProviderVerdict.Ok, (await first.Ended.WaitAsync(Wait)).Verdict);
        Assert.Equal(["one"], cli.Written.Select(UserText));
    }

    [Theory] // the stopped turn's end and the CLI's answer to the stop are both awaited, in either order, before the next line
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_send_after_a_stop_is_refused_until_both_the_answer_and_the_turns_end_are_in(bool answerFirst)
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        var first = await run.SendAsync("long");
        cli.Say(Init(), Delta("half"));
        await cli.Settled();
        Assert.True(await run.StopAsync());
        await cli.Wrote(IsInterrupt);
        var id = RequestId(cli.Written.Single(IsInterrupt));

        Assert.Equal(AgentRunSubtypes.Busy, (await (await run.SendAsync("early")).Ended).Subtype);
        cli.Say(answerFirst ? InterruptAnswered(id) : CancelledResult("aborted_streaming"));
        await cli.Settled();
        Assert.Equal(AgentRunSubtypes.Busy, (await (await run.SendAsync("still early")).Ended).Subtype);
        cli.Say(answerFirst ? CancelledResult("aborted_streaming") : InterruptAnswered(id));
        await cli.Settled();

        var next = await run.SendAsync("again");
        cli.Say(Result("ok"));
        Assert.Equal(AgentRunSubtypes.Cancelled, (await first.Ended.WaitAsync(Wait)).Subtype);
        Assert.Equal(ProviderVerdict.Ok, Ended(await Read(next)).Verdict);
        Assert.Equal(["long", "again"], cli.Written.Where(IsUser).Select(UserText));
    }

    [Fact] // the door takes no word into a running turn, so it offers no steer
    public async Task A_run_on_this_door_is_no_steering_run()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);

        Assert.False(run is IAgentRunSteering);
    }

    // ── what is said while no turn of the host's runs ────────────────────────

    [Fact] // what the CLI says before the first line is the first turn's; a task's end between turns begins the next, an allowance does not
    public async Task Before_the_first_line_the_cli_leads_the_first_turn_and_between_turns_only_a_task_waits_for_the_next()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        cli.Say(Init());
        await cli.Settled();
        Assert.Equal("s1", run.SessionId);

        var first = await run.SendAsync("one");
        cli.Say(Result("one"));
        var firstEvents = await Read(first);
        cli.Say(TaskEnded, Allowance);
        await cli.Settled();
        var second = await run.SendAsync("two");
        cli.Say(Result("two"));
        var secondEvents = await Read(second);

        Assert.IsType<SessionStarted>(firstEvents[0]);
        Assert.IsType<AgentLoaded>(firstEvents[1]);
        var ended = Assert.IsType<AgentTaskEnded>(secondEvents[0]);
        Assert.Equal(("t1", "completed", "toolu_1"), (ended.TaskId, ended.Status, ended.CallId));
        Assert.DoesNotContain(secondEvents, e => e is AgentAllowanceReport);
    }

    [Fact] // the carried ending was seen before the line was written, so a turn that then fails shows nothing of the line being read
    public async Task A_task_event_seen_before_the_line_leaves_a_failed_turn_never_sent()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        var first = await run.SendAsync("one");
        cli.Say(Result("one"));
        await first.Ended.WaitAsync(Wait);
        cli.Say(TaskEnded);
        await cli.Settled();

        var turn = await run.SendAsync("next");
        cli.EndOutput();

        var ended = Ended(await Read(turn));
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        Assert.Contains(AgentShownKind.Task, ended.Turn!.Shown);
    }

    [Fact] // SYNTHETIC: the CLI speaking on its own between turns is a turn no record shows: unread on the next, and no line until it ends
    public async Task The_cli_speaking_with_no_turn_running_refuses_a_send_until_its_result_and_is_unread_on_the_next_turn()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        var first = await run.SendAsync("one");
        cli.Say(Result("one"));
        await first.Ended.WaitAsync(Wait);

        cli.Say(MessageStart("m9"), Delta("unprompted"));
        await cli.Settled();
        var refused = Ended(await Read(await run.SendAsync("two")));
        cli.Say(Assistant("unprompted", "m9"), Result("unprompted"));
        await cli.Settled();
        var next = await run.SendAsync("three");
        cli.Say(Result("three"));
        var events = await Read(next);

        Assert.Equal((ProviderVerdict.Unsupported, AgentRunSubtypes.WokenRunning, AgentDelivery.NeverSent), (refused.Verdict, refused.Subtype, refused.Failure!.Delivery));
        Assert.Equal(["one", "three"], cli.Written.Select(UserText));
        Assert.DoesNotContain(events, e => e is TextDelta);
        var unread = Ended(events).Unread;
        Assert.Contains(unread, u => u is { Type: "stream_event", Subtype: "content_block_delta" });
        Assert.Contains(unread, u => u is { Type: "assistant" });
        Assert.Contains(unread, u => u is { Type: "result" });
        Assert.Equal("three", Ended(events).FinalText);
    }

    [Fact] // no record shows the CLI's own turn on held stdin, so none is offered: the woken turns end empty with the run
    public async Task The_woken_turns_complete_empty_when_the_run_ends()
    {
        var cli = new KeptCli();
        var run = await Open(cli);
        var reading = run.WokenTurns.ToListAsync().AsTask();

        await run.DisposeAsync();

        Assert.Empty(await reading.WaitAsync(Wait));
    }

    // ── the window, the end ──────────────────────────────────────────────────

    [Fact] // the runner's inactivity window is the host's wait while idle, and the CLI's silence while a turn runs
    public async Task The_inactivity_window_counts_only_while_a_sent_turn_runs()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        Assert.True(cli.Pause!.IsPaused);

        var turn = await run.SendAsync("work");
        Assert.False(cli.Pause.IsPaused);
        cli.Say(Result("done"));
        await turn.Ended.WaitAsync(Wait);

        await Eventually(() => cli.Pause.IsPaused);
    }

    [Fact] // the run's MCP document is written once for the run and lives as long as it does
    public async Task Disposing_the_run_closes_stdin_ends_it_disposed_cuts_an_open_turn_and_deletes_its_mcp_document()
    {
        var cli = new KeptCli();
        var run = await Open(cli, Session() with { McpServers = [AgentMcpServer.Http("tools", "http://127.0.0.1:9/mcp")] });
        var document = ValueAfter(cli.Args!, "--mcp-config")!;
        var open = await run.SendAsync("long");
        Assert.True(File.Exists(document));

        await run.DisposeAsync();

        var ended = await run.Ended;
        Assert.Equal((ProviderVerdict.Ok, false, AgentRunSubtypes.Disposed), (ended.Verdict, ended.IsError, ended.Subtype));
        Assert.True(cli.StdinClosed);
        var cut = Ended(await Read(open));
        Assert.Equal((ProviderVerdict.Failed, true, AgentRunSubtypes.Over), (cut.Verdict, cut.IsError, cut.Subtype));
        Assert.False(File.Exists(document));
        await run.DisposeAsync();   // twice is once
    }

    [Fact] // IAgentRun.Ended promises Disposed after a dispose, and a dispose that had to kill the CLI is one
    public async Task A_dispose_that_kills_a_cli_ignoring_stdins_close_still_ends_disposed_with_the_kill_in_its_detail()
    {
        var time = new ManualTimeProvider();
        var cli = new KeptCli { IgnoresStdinClose = true };
        var run = await Open(cli, time: time);

        var disposing = run.DisposeAsync().AsTask();
        await Eventually(() => time.Pending > 0);
        time.Advance(ClaudeCliAgentRun.DefaultStopGrace + TimeSpan.FromSeconds(1));
        await disposing.WaitAsync(Wait);

        var ended = await run.Ended.WaitAsync(Wait);
        Assert.Equal((ProviderVerdict.Ok, AgentRunSubtypes.Disposed), (ended.Verdict, ended.Subtype));
        Assert.Contains("did not leave within its grace", ended.Diagnostic);
    }

    [Fact] // a process that ends under an open turn ends the run with it
    public async Task A_process_that_ends_under_an_open_turn_ends_both()
    {
        var cli = new KeptCli();
        await using var run = await Open(cli);
        var turn = await run.SendAsync("work");

        cli.Say(Init(), Delta("half"));
        cli.EndOutput();

        var ended = Ended(await Read(turn));
        Assert.True(ended.IsError);
        Assert.Equal(AgentDelivery.Taken, ended.Failure!.Delivery);
        Assert.Equal("process-exited", (await run.Ended.WaitAsync(Wait)).Subtype);
        Assert.Equal(AgentRunSubtypes.Over, Ended(await Read(await run.SendAsync("more"))).Subtype);
    }

    [Fact] // the open is the spawn: a CLI that never starts is a run already over, and the host opens another
    public async Task A_spawn_that_fails_returns_the_run_already_over()
    {
        var cli = new KeptCli { SpawnFault = new InvalidOperationException("The system cannot find the file specified.") };

        await using var run = await Open(cli);

        var ended = await run.Ended.WaitAsync(Wait);
        Assert.Equal((ProviderVerdict.Failed, true), (ended.Verdict, ended.IsError));
        Assert.Equal("process.spawn", ended.Failure!.By);
        Assert.Null(run.SessionId);
        Assert.Equal(AgentRunSubtypes.Over, Ended(await Read(await run.SendAsync("anything"))).Subtype);
        Assert.False(await run.StopAsync());
    }

    [Fact] // the one-turn door's own refusals, and a runner that cannot pause its window, each before anything is spawned
    public async Task A_run_refused_before_a_spawn_is_already_over_and_spawns_nothing()
    {
        var cli = new KeptCli();

        await using var blank = await Open(cli, Session() with { ResumeToken = " " });
        await using var mode = await Open(cli, Session() with { PermissionMode = "whatever" });
        await using var mcp = await Open(cli, Session() with { McpServers = [AgentMcpServer.Http("tools", "not a url")] });
        await using var cannotPause = await Open(new KeptCli { CanPauseInactivity = false });

        Assert.Equal("resume-token-invalid", (await blank.Ended).Subtype);
        Assert.Equal(AgentProvider.OptionRefusedSubtype, (await mode.Ended).Subtype);
        Assert.Equal("mcp-server-invalid", (await mcp.Ended).Subtype);
        Assert.Equal("runner-unsupported", (await cannotPause.Ended).Subtype);
        Assert.Equal(AgentDelivery.NeverSent, (await cannotPause.Ended).Failure!.Delivery);
        Assert.Equal(0, cli.Spawns);
        Assert.Equal(AgentRunSubtypes.Over, Ended(await Read(await blank.SendAsync("x"))).Subtype);
        Assert.False(await blank.StopAsync());
    }

    [Theory] // as the protocol door: a quiet is a span of silence a timer can count, whichever door the run is on
    [InlineData(0)]
    [InlineData(50 * 24 * 3600)]
    public async Task A_woken_turn_quiet_outside_what_a_timer_counts_is_refused_at_the_open(int seconds)
    {
        var cli = new KeptCli();

        await Assert.ThrowsAsync<ArgumentException>(() => Open(cli, run: new AgentRunOptions { WokenTurnQuiet = TimeSpan.FromSeconds(seconds) }));
        Assert.Equal(0, cli.Spawns);
    }

    // ── through the provider ─────────────────────────────────────────────────

    private static ServiceProvider Services(KeptCli cli) =>
        new ServiceCollection().AddLyntai(b => b.AddClaudeCliAgentSession(command: "claude"))
            .AddSingleton<IProcessRunner>(cli).BuildServiceProvider();

    [Fact] // the CLI door's abilities, each from the record it rests on; the run is found by type test beside the door's other abilities
    public void The_cli_door_states_a_kept_run_and_a_keeping_stop_supported_from_the_record_and_the_rest_as_the_records_leave_them()
    {
        using var sp = Services(new KeptCli());
        var provider = sp.GetRequiredService<AgentProviderRegistry>().Get("claude-cli");
        var door = provider.Describe().Doors.Single();

        AgentAbility Claim(string ability) => door.Abilities.Single(a => a.Name == ability);

        Assert.Equal(AgentAbilities.All.Order(StringComparer.Ordinal), door.Abilities.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            [AgentSupport.Supported, AgentSupport.Unknown, AgentSupport.Supported, AgentSupport.Unsupported, AgentSupport.Unknown, AgentSupport.Unknown, AgentSupport.Unsupported, AgentSupport.Unsupported],
            new[] { AgentAbilities.KeptRun, AgentAbilities.QueuedWords, AgentAbilities.KeepingStop, AgentAbilities.Steering, AgentAbilities.ModelReask, AgentAbilities.ReplayTold, AgentAbilities.RepeatableOpen, AgentAbilities.PermissionRequests }
                .Select(door.SupportFor));
        Assert.Equal(("claude 2.1.281", "claude 2.1.281"), (Claim(AgentAbilities.KeptRun).AppliesTo, Claim(AgentAbilities.KeepingStop).AppliesTo));
        Assert.Contains("2.1.322", Claim(AgentAbilities.QueuedWords).Evidence);
        Assert.IsAssignableFrom<IAgentRunProvider>(provider);
        Assert.IsAssignableFrom<IAgentUsageSource>(provider);
        Assert.IsAssignableFrom<IAgentAuthSource>(provider);
    }

    [Fact]
    public async Task A_run_opened_through_the_provider_is_refused_what_a_turn_is_and_a_door_it_does_not_have_is_named()
    {
        var cli = new KeptCli();
        using var sp = Services(cli);
        var provider = (IAgentRunProvider)sp.GetRequiredService<AgentProviderRegistry>().Get("claude-cli");

        await using var opened = await provider.OpenRunAsync("cli", Session() with { Account = new AgentAccount { Provider = "claude-cli", Name = "a", Home = "/accounts/a" } });
        await using var wrongAccount = await provider.OpenRunAsync("cli", Session() with { Account = new AgentAccount { Provider = "codex-cli", Name = "a", Home = "x" } });

        Assert.False(opened.Ended.IsCompleted);
        Assert.Equal("/accounts/a", cli.Environment!["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(AgentProvider.AccountRefusedSubtype, (await wrongAccount.Ended).Subtype);
        Assert.Equal(1, cli.Spawns);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.OpenRunAsync("acp", Session()));
    }
}
