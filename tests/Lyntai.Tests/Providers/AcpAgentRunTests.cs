using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Acp;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Inference;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// A run kept across turns on the Claude protocol door (AG46 to AG48, D251), through an agent scripted frame by frame.
///
/// <para>Every capture is labelled. <b>SOURCE-DERIVED</b> from Daoris's recorded notes, which no run of ours repeated: the
/// second prompt answered at the fold (its steer note: the earlier prompt <c>end_turn</c> with every count 0, <c>initialize</c>
/// stating <c>_meta.claudeCode.promptQueueing</c>), the task frames and the agent speaking again after its response (its
/// console-streams note: <c>async_task_spawned</c>, a stopped-then-completed pair, chunks 1.6 and 2.4 s later) and a prompt sent
/// into a woken turn that is never answered (its BGWAIT2 entry). A real capture replaces one when it arrives.</para>
/// </summary>
[Collection(AcpStandIn.Collection)]
public class AcpAgentRunTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    /// <summary>An agent that answers each frame the client writes with what the test scripts, and speaks when the test says.</summary>
    private sealed class KeptAgent : IProcessRunner
    {
        private readonly Channel<string> _out = Channel.CreateUnbounded<string>();
        private readonly List<JsonNode> _written = [];
        private int _spawns;

        public Func<JsonNode, IEnumerable<string>> Reply { get; set; } = Handshake;

        public bool CanPauseInactivity { get; init; } = true;

        public InactivityPause? Pause { get; private set; }

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
            foreach (var line in lines) _out.Writer.TryWrite(line);
        }

        public void EndOutput() => _out.Writer.TryComplete();

        public async Task Wrote(Func<JsonNode, bool> match)
        {
            var until = DateTime.UtcNow + Wait;
            while (!Written.Any(match))
            {
                Assert.True(DateTime.UtcNow < until, "the client never wrote the frame");
                await Task.Delay(10);
            }
        }

        public IAsyncEnumerable<string> ExchangeLinesAsync(string command, IReadOnlyList<string> args, IAsyncEnumerable<string> stdinLines,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) =>
            Exchange(stdinLines, ct);

        public IAsyncEnumerable<string> ExchangeLinesAsync(string command, IReadOnlyList<string> args, IAsyncEnumerable<string> stdinLines,
            InactivityPause pause, TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default)
        {
            Pause = pause;
            return Exchange(stdinLines, ct);
        }

        private async IAsyncEnumerable<string> Exchange(IAsyncEnumerable<string> stdinLines, [EnumeratorCancellation] CancellationToken ct)
        {
            Interlocked.Increment(ref _spawns);
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var frame in stdinLines)
                    {
                        var node = JsonNode.Parse(frame)!;
                        lock (_written) _written.Add(node);
                        Say([.. Reply(node)]);
                    }
                }
                catch (OperationCanceledException)
                {
                    // the process was killed
                }

                StdinClosed = true;
                _out.Writer.TryComplete();   // an agent whose stdin closed goes
            });
            await foreach (var line in _out.Reader.ReadAllAsync(ct)) yield return line;
        }

        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null, TimeSpan? inactivityTimeout = null,
            TimeSpan? maxDuration = null, string? workingDirectory = null, IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? inactivityTimeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // ── the wire ─────────────────────────────────────────────────────────────

    /// <summary>SOURCE-DERIVED: <c>initialize</c> as claude-agent-acp 0.84.0 answers it (steer note §1).</summary>
    private static IEnumerable<string> Handshake(JsonNode frame) => Method(frame) switch
    {
        "initialize" => ["""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"agentCapabilities":{"_meta":{"claudeCode":{"promptQueueing":true}}},"authMethods":[]}}"""],
        "session/new" => ["""{"jsonrpc":"2.0","id":2,"result":{"sessionId":"s1"}}"""],
        _ => [],
    };

    private static string Method(JsonNode frame) => frame["method"]?.GetValue<string>() ?? "";

    private static string? IdOf(JsonNode frame) => frame["id"]?.ToString();

    private static List<string> Methods(KeptAgent agent) => [.. agent.Written.Select(Method)];

    private static string Update(string body) =>
        $$$"""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"s1","update":{{{body}}}}}""";

    private static string Chunk(string text) => Update($$$"""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"{{{text}}}"}}""");

    private static string Call(string id, string status) =>
        Update($$$"""{"sessionUpdate":"tool_call","toolCallId":"{{{id}}}","kind":"read","title":"Read","status":"{{{status}}}"}""");

    private static string CallDone(string id) => Update($$$"""{"sessionUpdate":"tool_call_update","toolCallId":"{{{id}}}","status":"completed"}""");

    private static string Answer(int id, string stop = "end_turn", string usage = """{"inputTokens":10,"outputTokens":5,"cachedReadTokens":0,"cachedWriteTokens":0}""") =>
        $$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{"stopReason":"{{{stop}}}","usage":{{{usage}}}}}""";

    private const string NoUsage = """{"inputTokens":0,"outputTokens":0,"cachedReadTokens":0,"cachedWriteTokens":0}""";

    /// <summary>SOURCE-DERIVED: the frames of a backgrounded command (console-streams note, REAL field names), here with ids made up.</summary>
    private static string Spawned(string task) => Update($$"""{"sessionUpdate":"async_task_spawned","asyncTaskId":"{{task}}","name":"tick","taskType":"shell","description":"ticks for twenty seconds","showInTranscript":false,"canStop":true}""");

    private static string TaskProgress(string call) => Update($$"""{"sessionUpdate":"async_task_progress","asyncTaskId":"t1","toolCallId":"{{call}}"}""");

    private static string TaskState(string task, string state) => Update($$"""{"sessionUpdate":"async_task_state_update","asyncTaskId":"{{task}}","state":"{{state}}"}""");

    private static string AutonomousUsage() =>
        Update("""{"sessionUpdate":"usage_update","used":100,"size":1000,"_meta":{"_claude/origin":{"kind":"task-notification"}}}""");

    private static string PermissionRequest() =>
        """{"jsonrpc":"2.0","id":"agent-1","method":"session/request_permission","params":{"sessionId":"s1","toolCall":{"toolCallId":"c1","title":"Edit"},"options":[{"optionId":"allow-once","name":"Allow","kind":"allow_once"},{"optionId":"reject-once","name":"Reject","kind":"reject_once"}]}}""";

    // ── the run ──────────────────────────────────────────────────────────────

    private static AgentSessionOptions Session(AgentToolPolicy policy = AgentToolPolicy.ReadOnly) =>
        new() { Prompt = "", WorkingDirectory = TestPaths.TestScratchDir, ToolPolicy = policy };

    private static async Task<IAgentRun> Open(
        KeptAgent agent, AgentRunOptions? run = null, TimeProvider? time = null, bool queuedWords = true,
        IAcpPermissionApprover? approver = null, AgentSessionOptions? options = null) =>
        await new AcpAgentSession(agent, new LyntaiOptions(), "agent", time: time, permissionApprover: approver)
            .OpenRunAsync(options ?? Session(approver is null ? AgentToolPolicy.ReadOnly : AgentToolPolicy.Write), run, queuedWords, default)
            .WaitAsync(Wait);

    private static async Task<List<AgentStreamEvent>> Read(AgentRunTurn turn) => await turn.Events.ToListAsync().AsTask().WaitAsync(Wait);

    private static SessionEnded Ended(IEnumerable<AgentStreamEvent> events) => Assert.IsType<SessionEnded>(events.Last());

    private static async Task Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the condition never held");
            await Task.Delay(10);
        }
    }

    // ── AG46: the open, a turn, and a turn after it ──────────────────────────

    [Fact] // SOURCE-DERIVED
    public async Task Two_turns_run_on_one_process_with_one_initialize_and_one_session_new()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        Assert.Equal("s1", run.SessionId);

        var first = await run.SendAsync("one");
        agent.Say(Chunk("first"), Answer(100));
        var firstEvents = await Read(first);
        var second = await run.SendAsync("two");
        agent.Say(Chunk("second"), Answer(101));
        var secondEvents = await Read(second);

        Assert.Equal(["initialize", "session/new", "session/prompt", "session/prompt"], Methods(agent));
        Assert.Equal(1, agent.Spawns);
        Assert.Equal("s1", Assert.IsType<SessionStarted>(firstEvents[0]).SessionId);
        Assert.Equal("first", Assert.IsType<TextDelta>(firstEvents[1]).Text);
        Assert.DoesNotContain(secondEvents, e => e is SessionStarted);
        Assert.Equal("second", Assert.IsType<TextDelta>(secondEvents[0]).Text);
        foreach (var events in new[] { firstEvents, secondEvents })
        {
            Assert.Single(events.OfType<SessionEnded>());
            var ended = Ended(events);
            Assert.Equal((ProviderVerdict.Ok, false, "s1"), (ended.Verdict, ended.IsError, ended.SessionId));
            Assert.Equal(AgentRunTurnOrigin.Sent, first.Origin);
        }

        Assert.Equal("first", Ended(firstEvents).FinalText);
        var usage = secondEvents.OfType<UsageReading>().Single();
        Assert.Equal(15L, usage.InputTokens + usage.OutputTokens);
        Assert.Equal(Ended(secondEvents), await second.Ended);
    }

    [Fact] // SOURCE-DERIVED: the system prompt leads the first prompt only, and the first prompt's id is the run's own
    public async Task The_system_prompt_leads_the_first_prompt_alone()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent, options: Session() with { SystemPrompt = "be brief" });

        await run.SendAsync("one");
        agent.Say(Answer(100));
        await agent.Wrote(f => IdOf(f) == "100");
        var second = await run.SendAsync("two");
        agent.Say(Answer(101));
        await second.Ended.WaitAsync(Wait);

        var prompts = agent.Written.Where(f => Method(f) == "session/prompt").ToList();
        Assert.Equal(["be brief", "one"], prompts[0]["params"]!["prompt"]!.AsArray().Select(b => b!["text"]!.GetValue<string>()));
        Assert.Equal(["two"], prompts[1]["params"]!["prompt"]!.AsArray().Select(b => b!["text"]!.GetValue<string>()));
    }

    [Fact] // the protocol's own request: only a run asks for the frames of the work it backgrounds
    public async Task Only_a_run_declares_asyncTasks_at_initialize()
    {
        var run = new KeptAgent();
        await using var kept = await Open(run);
        var oneTurn = new KeptAgent();
        oneTurn.Reply = frame => Method(frame) == "session/prompt" ? [Answer(3)] : Handshake(frame);
        await new AcpAgentSession(oneTurn, new LyntaiOptions(), "agent").StreamAsync(Session() with { Prompt = "hi" }).ToListAsync();

        string[] Declared(KeptAgent a) => [.. a.Written[0]["params"]!["clientCapabilities"]!["_meta"]!["jetbrains"]!["air"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>())];
        Assert.Equal(["sessionFailure", "asyncTasks"], Declared(run));
        Assert.Equal(["sessionFailure"], Declared(oneTurn));
    }

    [Fact] // SOURCE-DERIVED: a stop is the one-turn door's cancel, and the next turn runs on the same process
    public async Task A_stop_cancels_the_turn_and_a_turn_after_it_runs()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var first = await run.SendAsync("long");

        Assert.True(await run.StopAsync());
        Assert.False(await run.StopAsync());   // one stop is out
        await agent.Wrote(f => Method(f) == "session/cancel");
        agent.Say(Answer(100, "cancelled", NoUsage));
        var ended = Ended(await Read(first));
        var next = await run.SendAsync("again");
        agent.Say(Answer(101));

        Assert.Equal(("cancelled", true, ProviderVerdict.Failed), (ended.Subtype, ended.IsError, ended.Verdict));
        Assert.Equal(ProviderVerdict.Ok, Ended(await Read(next)).Verdict);
        Assert.Equal(1, agent.Spawns);
        Assert.False(await run.StopAsync());   // nothing runs
    }

    [Fact]
    public async Task A_stop_from_a_cancel_nobody_answers_ends_the_run_after_the_grace()
    {
        var time = new ManualTimeProvider();
        var agent = new KeptAgent();
        await using var run = await Open(agent, time: time);
        var turn = await run.SendAsync("long");

        Assert.True(await run.StopAsync());
        await Eventually(() => time.Pending > 0);
        time.Advance(AcpAgentSession.DefaultCancelGrace + TimeSpan.FromSeconds(1));

        var ended = Ended(await Read(turn));
        Assert.True(ended.IsError);
        Assert.Equal("run-killed", ended.Failure!.By);
        Assert.Equal(AgentRunSubtypes.Over, (await run.Ended.WaitAsync(Wait)).Subtype);
    }

    [Fact] // a door that states no queued-words refuses the word, not the turn
    public async Task A_busy_run_refuses_a_word_where_the_door_states_none_queued()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent, queuedWords: false);
        var first = await run.SendAsync("one");

        var refused = await run.SendAsync("a word");

        var ended = Ended(await Read(refused));
        Assert.Equal((ProviderVerdict.Unsupported, AgentRunSubtypes.Busy, AgentDelivery.NeverSent), (ended.Verdict, ended.Subtype, ended.Failure!.Delivery));
        await Task.Delay(100);
        Assert.Single(agent.Written, f => Method(f) == "session/prompt");
        agent.Say(Answer(100));
        Assert.Equal(ProviderVerdict.Ok, (await first.Ended.WaitAsync(Wait)).Verdict);
    }

    [Fact]
    public async Task An_open_the_agent_fails_ends_the_run_with_the_typed_failure_and_the_host_opens_another()
    {
        var agent = new KeptAgent { Reply = f => Method(f) == "session/new" ? ["""{"jsonrpc":"2.0","id":2,"error":{"code":-32002,"message":"nope"}}"""] : Handshake(f) };

        await using var run = await Open(agent);

        var ended = await run.Ended.WaitAsync(Wait);
        Assert.Null(run.SessionId);
        Assert.True(ended.IsError);
        Assert.Equal("acp.error.code=-32002", ended.Failure!.By);
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure.Delivery);
        var refused = Ended(await Read(await run.SendAsync("anything")));
        Assert.Equal(AgentRunSubtypes.Over, refused.Subtype);
        await Eventually(() => agent.StdinClosed);   // the library never repeats an open: one session/new was sent
        Assert.Single(agent.Written, f => Method(f) == "session/new");
    }

    [Fact]
    public async Task A_run_refused_before_a_spawn_is_already_over_and_spawns_nothing()
    {
        var agent = new KeptAgent();

        await using var blank = await Open(agent, options: Session() with { ResumeToken = " " });
        await using var cannotPause = await Open(new KeptAgent { CanPauseInactivity = false });

        Assert.Equal("resume-token-invalid", (await blank.Ended).Subtype);
        Assert.Equal(0, agent.Spawns);
        Assert.Equal("runner-unsupported", (await cannotPause.Ended).Subtype);
        Assert.Equal(AgentDelivery.NeverSent, (await cannotPause.Ended).Failure!.Delivery);
        Assert.Equal(AgentRunSubtypes.Over, Ended(await Read(await blank.SendAsync("x"))).Subtype);
        Assert.False(await blank.StopAsync());
    }

    [Fact] // the runner's inactivity window is the host's wait while idle, and the agent's silence while a turn runs
    public async Task The_inactivity_window_counts_only_while_a_sent_turn_runs()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        Assert.True(agent.Pause!.IsPaused);

        var turn = await run.SendAsync("work");
        Assert.False(agent.Pause.IsPaused);
        agent.Say(Answer(100));
        await turn.Ended.WaitAsync(Wait);

        await Eventually(() => agent.Pause.IsPaused);
    }

    [Fact]
    public async Task Disposing_the_run_closes_stdin_and_ends_it_ok_and_a_turn_still_open_ends_failed()
    {
        var agent = new KeptAgent();
        var run = await Open(agent);
        var open = await run.SendAsync("long");

        await run.DisposeAsync();

        var ended = await run.Ended;
        Assert.Equal((ProviderVerdict.Ok, false, AgentRunSubtypes.Disposed), (ended.Verdict, ended.IsError, ended.Subtype));
        Assert.True(agent.StdinClosed);
        var cut = Ended(await Read(open));
        Assert.Equal((ProviderVerdict.Failed, true, AgentRunSubtypes.Over), (cut.Verdict, cut.IsError, cut.Subtype));
        await run.DisposeAsync();   // twice is once
    }

    [Fact] // a process that dies under an open turn ends the run with its fault
    public async Task A_process_that_ends_under_an_open_turn_ends_both()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var turn = await run.SendAsync("work");

        agent.Say(Chunk("half"));
        agent.EndOutput();

        var ended = Ended(await Read(turn));
        Assert.True(ended.IsError);
        Assert.Equal("process-exited", (await run.Ended.WaitAsync(Wait)).Subtype);
        Assert.Equal(AgentDelivery.Taken, ended.Failure!.Delivery);
    }

    // ── what the door states, and finding it by type ─────────────────────────

    [Fact]
    public void The_claude_door_states_a_kept_run_and_queued_words_supported_and_the_rest_unknown()
    {
        var agent = new KeptAgent();
        using var sp = new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("claude-acp", "agent", keptRun: AcpKeptRun.ClaudeAgentAcp)
            .AddAcpAgentSession("plain-acp", "agent")).AddSingleton<IProcessRunner>(agent).BuildServiceProvider();
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        AgentSupport Stated(string id, string ability) => registry.Get(id).Describe().Doors.Single().SupportFor(ability);

        Assert.Equal(AgentAbilities.All.Order(StringComparer.Ordinal), registry.Get("claude-acp").Describe().Doors.Single().Abilities.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal([AgentSupport.Supported, AgentSupport.Supported],
            [Stated("claude-acp", AgentAbilities.KeptRun), Stated("claude-acp", AgentAbilities.QueuedWords)]);
        foreach (var ability in new[] { AgentAbilities.KeepingStop, AgentAbilities.ModelReask, AgentAbilities.ReplayTold, AgentAbilities.RepeatableOpen })
            Assert.Equal(AgentSupport.Unknown, Stated("claude-acp", ability));
        foreach (var ability in new[] { AgentAbilities.KeptRun, AgentAbilities.QueuedWords })
            Assert.Equal(AgentSupport.Unknown, Stated("plain-acp", ability));
        Assert.IsAssignableFrom<IAgentRunProvider>(registry.Get("claude-acp"));
        Assert.False(registry.Get("plain-acp") is IAgentRunProvider);
        Assert.Equal("claude-agent-acp 0.84.0", registry.Get("claude-acp").Describe().Doors.Single().Abilities.Single(a => a.Name == AgentAbilities.KeptRun).AppliesTo);
    }

    [Fact]
    public async Task A_run_opened_through_the_provider_is_refused_what_a_turn_is_and_a_door_it_does_not_have_is_named()
    {
        var agent = new KeptAgent();
        using var sp = new ServiceCollection().AddLyntai(b => b.AddAcpAgentSession("claude-acp", "agent", keptRun: AcpKeptRun.ClaudeAgentAcp))
            .AddSingleton<IProcessRunner>(agent).BuildServiceProvider();
        var provider = (IAgentRunProvider)sp.GetRequiredService<AgentProviderRegistry>().Get("claude-acp");

        await using var opened = await provider.OpenRunAsync("acp", Session());
        await using var wrongAccount = await provider.OpenRunAsync("acp", Session() with { Account = new AgentAccount { Provider = "codex-cli", Name = "a", Home = "x" } });
        await using var unhonourable = await provider.OpenRunAsync("acp", Session() with { DisallowedTools = ["Bash"] });

        Assert.Equal("s1", opened.SessionId);
        Assert.Equal(AgentProvider.AccountRefusedSubtype, (await wrongAccount.Ended).Subtype);
        Assert.Equal(AgentProvider.OptionRefusedSubtype, (await unhonourable.Ended).Subtype);
        Assert.Equal(1, agent.Spawns);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.OpenRunAsync("cli", Session()));
    }

    // ── AG47: a word taken mid-turn ──────────────────────────────────────────

    [Fact] // SOURCE-DERIVED: steer note §6 turn A, the earlier prompt answered end_turn with every count 0 at the next step
    public async Task A_word_folds_the_earlier_turn_and_the_later_one_owns_the_rest()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var task = await run.SendAsync("read five files");
        agent.Say(Call("r1", "in_progress"));

        var note = await run.SendAsync("put PINEAPPLE after the words");   // a word, on a busy run
        agent.Say(CallDone("r1"), Answer(100, "end_turn", NoUsage), Chunk("alpha PINEAPPLE"), Answer(101));
        var taskEvents = await Read(task);
        var noteEvents = await Read(note);

        var folded = Ended(taskEvents);
        Assert.Equal((ProviderVerdict.Ok, false, AgentRunSubtypes.Folded), (folded.Verdict, folded.IsError, folded.Subtype));
        var zero = taskEvents.OfType<UsageFinal>().Single();
        Assert.Equal((0L, 0L, 0L, 0L), (zero.InputTokens, zero.OutputTokens, zero.CacheReadTokens, zero.CacheCreateTokens));
        Assert.Contains(taskEvents, e => e is ToolResult);
        Assert.Equal("alpha PINEAPPLE", Assert.IsType<TextDelta>(noteEvents[0]).Text);
        var later = Ended(noteEvents);
        Assert.Equal((ProviderVerdict.Ok, (string?)null, "alpha PINEAPPLE"), (later.Verdict, later.Subtype, later.FinalText));
        var counted = noteEvents.OfType<UsageFinal>().Single();
        Assert.Equal(15L, counted.InputTokens + counted.OutputTokens);
        Assert.DoesNotContain(agent.Written, f => Method(f) == "session/cancel");
        Assert.Equal(2, agent.Written.Count(f => Method(f) == "session/prompt"));
    }

    [Fact] // an earlier prompt that did real work and ended before the word was taken is a finished turn, not a fold
    public async Task An_earlier_turn_that_ended_with_work_done_is_not_folded()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var first = await run.SendAsync("one");
        var word = await run.SendAsync("two");

        agent.Say(Chunk("done"), Answer(100), Chunk("then"), Answer(101));

        var ended = Ended(await Read(first));
        Assert.Equal((ProviderVerdict.Ok, (string?)null, "done"), (ended.Verdict, ended.Subtype, ended.FinalText));
        Assert.Equal("then", Ended(await Read(word)).FinalText);
    }

    [Fact] // SOURCE-DERIVED: a cancel with a word on its way loses its answer (steer note §3), so the run sends none
    public async Task A_stop_with_a_word_on_its_way_sends_nothing()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var first = await run.SendAsync("one");
        await run.SendAsync("two");

        Assert.False(await run.StopAsync());

        await Task.Delay(100);
        Assert.DoesNotContain(agent.Written, f => Method(f) == "session/cancel");
        agent.Say(Answer(100, "end_turn", NoUsage), Answer(101));
        Assert.Equal(AgentRunSubtypes.Folded, (await first.Ended.WaitAsync(Wait)).Subtype);
        Assert.False(await run.StopAsync());   // the word was taken, and nothing runs now
    }

    [Fact] // no record shows a folded word with a request held, so the run does not offer an unknown
    public async Task A_prompt_is_refused_while_a_permission_request_is_held()
    {
        var release = new TaskCompletionSource<AcpPermissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approver = new HoldingApprover(release.Task, asked);
        var agent = new KeptAgent();
        await using var run = await Open(agent, approver: approver);
        var turn = await run.SendAsync("edit it");

        agent.Say(PermissionRequest());
        await asked.Task.WaitAsync(Wait);
        var refused = Ended(await Read(await run.SendAsync("a word")));
        release.SetResult(AcpPermissionAnswer.Approve);
        await agent.Wrote(f => IdOf(f) == "agent-1");
        var taken = await run.SendAsync("a word, now");

        Assert.Equal((ProviderVerdict.Unsupported, AgentRunSubtypes.RequestHeld, AgentDelivery.NeverSent), (refused.Verdict, refused.Subtype, refused.Failure!.Delivery));
        Assert.Equal(1, agent.Written.Count(f => Method(f) == "session/prompt" && IdOf(f) == "100"));
        agent.Say(Answer(100, "end_turn", NoUsage), Answer(101));
        Assert.Equal(AgentRunSubtypes.Folded, (await turn.Ended.WaitAsync(Wait)).Subtype);
        Assert.Equal(ProviderVerdict.Ok, (await taken.Ended.WaitAsync(Wait)).Verdict);
    }

    private sealed class HoldingApprover(Task<AcpPermissionAnswer> answer, TaskCompletionSource asked) : IAcpPermissionApprover
    {
        public Task<AcpPermissionAnswer> ApproveAsync(AcpPermissionRequest request, CancellationToken ct = default)
        {
            asked.TrySetResult();
            return answer;
        }
    }

    // ── AG48: background work, and the turn its end wakes ────────────────────

    [Fact] // SOURCE-DERIVED: console-streams note, a task spawned, the response, the agent speaking again after its end
    public async Task A_woken_turn_carries_the_endings_that_woke_it_and_a_task_is_read_from_its_start()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var turn = await run.SendAsync("start a ticker and delegate");

        agent.Say(Call("bash-1", "in_progress"), Spawned("t1"), TaskProgress("bash-1"), CallDone("bash-1"), Chunk("DONE"), Answer(100));
        var events = await Read(turn);
        agent.Say(TaskState("t1", "stopped"), TaskState("t1", "completed"), TaskState("t1", "completed"), Chunk("The background ticker finished"));
        await using var woken = run.WokenTurns.GetAsyncEnumerator();
        Assert.True(await woken.MoveNextAsync().AsTask().WaitAsync(Wait));
        await using var wokenEvents = woken.Current.Events.GetAsyncEnumerator();
        var read = new List<AgentStreamEvent>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await wokenEvents.MoveNextAsync().AsTask().WaitAsync(Wait));
            read.Add(wokenEvents.Current);
        }

        var started = Assert.IsType<AgentTaskStarted>(Assert.Single(events.OfType<AgentTaskStarted>()));
        Assert.Equal(("t1", "shell", "ticks for twenty seconds", (string?)null), (started.TaskId, started.Kind, started.Description, started.CallId));
        Assert.Equal(AgentRunTurnOrigin.Woken, woken.Current.Origin);
        Assert.Equal([new AgentTaskEnded("t1", "stopped"), new AgentTaskEnded("t1", "completed")], read.Take(2));   // a repeat of a state is one ending
        Assert.Equal("The background ticker finished", Assert.IsType<TextDelta>(read[2]).Text);
        Assert.Contains(AgentShownKind.Task, Ended(events).Turn!.Shown);
        Assert.DoesNotContain(events, e => e is AgentTaskEnded);
    }

    [Fact] // the file a task's output goes to is named by a progress frame (the console-streams note), once, and the end keys by the spawn's call
    public async Task A_task_names_its_output_file_once_and_its_end_carries_the_call_that_spawned_it()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var turn = await run.SendAsync("start a ticker");

        agent.Say(
            Update("""{"sessionUpdate":"async_task_spawned","asyncTaskId":"t1","name":"tick","toolCallId":"bash-1"}"""),
            Update("""{"sessionUpdate":"async_task_progress","asyncTaskId":"t1","outputFilePath":"/tasks/t1.output"}"""),
            Update("""{"sessionUpdate":"async_task_progress","asyncTaskId":"t1","outputFilePath":"/tasks/t1.output"}"""),
            Update("""{"sessionUpdate":"async_task_state_update","asyncTaskId":"t1","state":"completed","outputFilePath":"/tasks/t1.output"}"""),
            Answer(100));
        var events = await Read(turn);

        Assert.Equal(new AgentTaskOutputFile("t1", "/tasks/t1.output"), events.OfType<AgentTaskOutputFile>().Single());
        Assert.Equal(new AgentTaskEnded("t1", "completed") { CallId = "bash-1" }, events.OfType<AgentTaskEnded>().Single());
        Assert.True(events.FindIndex(e => e is AgentTaskStarted) < events.FindIndex(e => e is AgentTaskOutputFile));
        Assert.True(events.FindIndex(e => e is AgentTaskOutputFile) < events.FindIndex(e => e is AgentTaskEnded));
    }

    [Fact] // D251: a woken turn ends only by the host's quiet, and the agent's autonomous usage frame is not its end
    public async Task A_woken_turn_with_no_quiet_bound_never_ends_itself_and_a_prompt_into_it_is_refused()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        await using var woken = run.WokenTurns.GetAsyncEnumerator();

        agent.Say(Chunk("I woke"), AutonomousUsage(), AutonomousUsage());
        Assert.True(await woken.MoveNextAsync().AsTask().WaitAsync(Wait));
        await Task.Delay(300);
        var refused = Ended(await Read(await run.SendAsync("go on")));

        Assert.False(woken.Current.Ended.IsCompleted);
        Assert.Equal((ProviderVerdict.Unsupported, AgentRunSubtypes.WokenRunning, AgentDelivery.NeverSent), (refused.Verdict, refused.Subtype, refused.Failure!.Delivery));
        Assert.DoesNotContain(agent.Written, f => Method(f) == "session/prompt");
        await run.DisposeAsync();
        var cut = await woken.Current.Ended.WaitAsync(Wait);
        Assert.Equal((ProviderVerdict.Failed, AgentRunSubtypes.Over), (cut.Verdict, cut.Subtype));   // one terminal even then
    }

    [Fact]
    public async Task A_woken_turn_ends_after_the_hosts_quiet_with_what_it_said_and_a_word_moves_the_clock()
    {
        var time = new ManualTimeProvider();
        var agent = new KeptAgent();
        await using var run = await Open(agent, new AgentRunOptions { WokenTurnQuiet = TimeSpan.FromSeconds(5) }, time);
        await using var woken = run.WokenTurns.GetAsyncEnumerator();

        agent.Say(Chunk("one "));
        Assert.True(await woken.MoveNextAsync().AsTask().WaitAsync(Wait));
        await Eventually(() => time.Pending > 0);
        time.Advance(TimeSpan.FromSeconds(4));
        agent.Say(Chunk("two"));
        await Eventually(() => time.NextDue() > TimeSpan.FromSeconds(4));   // rescheduled from the last word
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(woken.Current.Ended.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));

        var ended = await woken.Current.Ended.WaitAsync(Wait);
        Assert.Equal((ProviderVerdict.Ok, false, AgentRunSubtypes.Quiet, "one two"), (ended.Verdict, ended.IsError, ended.Subtype, ended.FinalText));
        Assert.Null(ended.Turn!.Written);
        Assert.Equal(ProviderVerdict.Ok, Ended(await Read(woken.Current)).Verdict);
        // the run is idle again, and a prompt is a turn
        var next = await run.SendAsync("go on");
        agent.Say(Answer(100));
        Assert.Equal(ProviderVerdict.Ok, (await next.Ended.WaitAsync(Wait)).Verdict);
    }

    [Fact] // a turn that has a call open is working, not quiet
    public async Task A_woken_turn_is_not_quiet_while_a_call_it_made_is_open()
    {
        var time = new ManualTimeProvider();
        var agent = new KeptAgent();
        await using var run = await Open(agent, new AgentRunOptions { WokenTurnQuiet = TimeSpan.FromSeconds(5) }, time);
        await using var woken = run.WokenTurns.GetAsyncEnumerator();

        agent.Say(Call("long", "in_progress"));
        Assert.True(await woken.MoveNextAsync().AsTask().WaitAsync(Wait));
        await Task.Delay(100);
        time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(100);
        Assert.False(woken.Current.Ended.IsCompleted);

        agent.Say(CallDone("long"));
        await Eventually(() => time.Pending > 0);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(AgentRunSubtypes.Quiet, (await woken.Current.Ended.WaitAsync(Wait)).Subtype);
    }

    [Fact] // a request with no prompt open is the agent's turn beginning, and the run holds it for the approver
    public async Task A_request_with_no_turn_running_begins_a_woken_turn()
    {
        var release = new TaskCompletionSource<AcpPermissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new KeptAgent();
        await using var run = await Open(agent, approver: new HoldingApprover(release.Task, asked));
        await using var woken = run.WokenTurns.GetAsyncEnumerator();

        agent.Say(PermissionRequest());

        Assert.True(await woken.MoveNextAsync().AsTask().WaitAsync(Wait));
        await asked.Task.WaitAsync(Wait);
        release.SetResult(AcpPermissionAnswer.Deny);
        await agent.Wrote(f => IdOf(f) == "agent-1");
    }

    [Fact] // the one-turn door's background reading is unchanged: it never asked for the frames, and one that arrives is unread
    public async Task The_one_turn_door_reads_no_task_event_from_task_frames()
    {
        var agent = new KeptAgent();
        agent.Reply = frame => Method(frame) == "session/prompt" ? [Spawned("t1"), TaskState("t1", "completed"), Answer(3)] : Handshake(frame);

        var events = await new AcpAgentSession(agent, new LyntaiOptions(), "agent").StreamAsync(Session() with { Prompt = "hi" }).ToListAsync();

        Assert.DoesNotContain(events, e => e is AgentTaskStarted or AgentTaskEnded);
        var unread = Ended(events).Unread;
        Assert.Contains(unread, u => u is { Type: "session/update", Subtype: "async_task_spawned" });
        Assert.Contains(unread, u => u is { Type: "session/update", Subtype: "async_task_state_update" });
    }

    [Fact] // a task event seen while no turn runs begins the next turn, so a host reading turns never misses an ending
    public async Task A_task_ending_between_turns_begins_the_next_turn()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);

        agent.Say(TaskState("t1", "completed"));
        await Task.Delay(100);
        var turn = await run.SendAsync("next");
        agent.Say(Answer(100));

        var events = await Read(turn);
        Assert.Equal(new AgentTaskEnded("t1", "completed"), events.OfType<AgentTaskEnded>().Single());
    }

    [Fact] // the carried ending was seen before the prompt, so it shows nothing of the prompt being read
    public async Task A_task_event_seen_before_the_prompt_leaves_a_failed_turn_never_sent()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);

        agent.Say(TaskState("t1", "completed"));
        await Task.Delay(100);
        var turn = await run.SendAsync("next");
        agent.EndOutput();

        var ended = Ended(await Read(turn));
        Assert.Equal(AgentDelivery.NeverSent, ended.Failure!.Delivery);
        Assert.Contains(AgentShownKind.Task, ended.Turn!.Shown);
    }

    // ── what a kept run cannot place ─────────────────────────────────────────

    [Theory] // a quiet is a span of silence a timer can count: not zero, and not past what its due time holds
    [InlineData(0)]
    [InlineData(50 * 24 * 3600)]
    public async Task A_woken_turn_quiet_outside_what_a_timer_counts_is_refused_at_the_open(int seconds)
    {
        var agent = new KeptAgent();

        await Assert.ThrowsAsync<ArgumentException>(() => Open(agent, new AgentRunOptions { WokenTurnQuiet = TimeSpan.FromSeconds(seconds) }));
        Assert.Equal(0, agent.Spawns);
    }

    private static string UnknownRequest() => """{"jsonrpc":"2.0","id":"u1","method":"fs/read_text_file","params":{"path":"a"}}""";

    private static string UnknownNotification() => """{"jsonrpc":"2.0","method":"telemetry/ping","params":{}}""";

    [Fact] // the turn that runs owns what the agent says that no case takes
    public async Task A_request_and_a_notification_no_case_takes_are_unread_on_the_turn_that_runs()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var turn = await run.SendAsync("work");

        agent.Say(UnknownRequest(), UnknownNotification(), Answer(100));

        var unread = Ended(await Read(turn)).Unread;
        Assert.Contains(unread, u => u is { Type: "request", Subtype: "fs/read_text_file" });
        Assert.Contains(unread, u => u is { Type: "notification", Subtype: "telemetry/ping" });
        await agent.Wrote(f => IdOf(f) == "u1");   // and the request is still answered
    }

    [Fact] // between turns nothing runs, so what is unread waits for the next turn to begin
    public async Task An_update_no_case_takes_between_turns_is_unread_on_the_next_turn()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);

        agent.Say(Update("""{"sessionUpdate":"telemetry_update"}"""), CallDone("late"));
        await Task.Delay(100);
        var turn = await run.SendAsync("next");
        agent.Say(Answer(100));

        var events = await Read(turn);
        Assert.Contains(Ended(events).Unread, u => u is { Type: "session/update", Subtype: "telemetry_update" });
        Assert.DoesNotContain(events, e => e is ToolResult);
    }

    [Fact] // a line nested deeper than the parser's default is still read, so a request on it is answered
    public async Task A_request_on_a_deeply_nested_line_is_answered()
    {
        var agent = new KeptAgent();
        await using var run = await Open(agent);
        var nest = new string('[', 100) + new string(']', 100);

        agent.Say($$$"""{"jsonrpc":"2.0","id":"agent-1","method":"session/request_permission","params":{"sessionId":"s1","toolCall":{"toolCallId":"c1","rawInput":{{{nest}}}},"options":[{"optionId":"reject-once","name":"Reject","kind":"reject_once"}]}}""");

        await agent.Wrote(f => IdOf(f) == "agent-1");
    }

    // ── a run's end releases a held request ──────────────────────────────────

    private sealed class TokenApprover : IAcpPermissionApprover
    {
        public TaskCompletionSource<CancellationToken> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AcpPermissionAnswer> ApproveAsync(AcpPermissionRequest request, CancellationToken ct = default)
        {
            Asked.TrySetResult(ct);
            return new TaskCompletionSource<AcpPermissionAnswer>().Task;   // the host's dialog stays open until the token says otherwise
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_run_that_ends_cancels_the_token_of_a_request_held_for_the_approver(bool disposed)
    {
        var approver = new TokenApprover();
        var agent = new KeptAgent();
        await using var run = await Open(agent, approver: approver);
        await run.SendAsync("edit it");
        agent.Say(PermissionRequest());
        var token = await approver.Asked.Task.WaitAsync(Wait);
        Assert.False(token.IsCancellationRequested);

        if (disposed) await run.DisposeAsync();
        else agent.EndOutput();

        await run.Ended.WaitAsync(Wait);
        await Eventually(() => token.IsCancellationRequested);
    }
}
