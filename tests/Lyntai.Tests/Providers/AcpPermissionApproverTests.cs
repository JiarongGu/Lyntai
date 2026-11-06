using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Providers.Acp;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Inference;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>
/// The seam that hands an ACP agent's permission request to the host. Every capture is SOURCE-DERIVED: the stand-in
/// agent (<c>devtools/scripts/acp-stub.mjs</c>, marker <c>PERMISSION_TURN</c>) asks to edit a file with an
/// <c>allow_always</c>, an <c>allow_once</c> and a <c>reject_once</c> option, in the shape the published protocol
/// schema gives <c>session/request_permission</c>; no turn was taken against a real agent. What a default door does
/// with a request is held by <see cref="AcpAgentSessionTests"/>, unchanged.
/// </summary>
[Collection(AcpStandIn.Collection)]
public class AcpPermissionApproverTests
{
    private sealed class Approver(Func<AcpPermissionRequest, CancellationToken, Task<AcpPermissionAnswer>> decide) : IAcpPermissionApprover
    {
        public List<AcpPermissionRequest> Seen { get; } = [];
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AcpPermissionAnswer> ApproveAsync(AcpPermissionRequest request, CancellationToken ct = default)
        {
            lock (Seen) Seen.Add(request);
            Asked.TrySetResult();
            return decide(request, ct);
        }

        public static Approver Answering(AcpPermissionAnswer answer) => new((_, _) => Task.FromResult(answer));
    }

    private static string StubCommand =>
        $"node \"{Path.Combine(TestPaths.DevtoolsDir("scripts"), "acp-stub.mjs")}\"";

    private static string NewLog() => Path.Combine(TestPaths.TestScratchDir, $"acp-{Guid.NewGuid():N}.jsonl");

    private static AcpAgentSession Stub(IAcpPermissionApprover? approver, string? log = null, TimeSpan? grace = null) =>
        new(new ProcessRunner(), new LyntaiOptions(), StubCommand,
            environment: log is null ? null : new Dictionary<string, string?> { ["LYNTAI_ACP_STUB_LOG"] = log },
            cancelGrace: grace, permissionApprover: approver);

    private static AgentSessionOptions Turn(AgentToolPolicy policy = AgentToolPolicy.Write, int? timeoutSeconds = null) =>
        new() { Prompt = "PERMISSION_TURN", WorkingDirectory = TestPaths.TestScratchDir, ToolPolicy = policy, TimeoutSeconds = timeoutSeconds };

    private static async Task<SessionEnded> Run(IAgentSession session, AgentSessionOptions options) =>
        Assert.IsType<SessionEnded>((await session.StreamAsync(options).ToListAsync()).Last());

    /// <summary>The frames the client sent back to the agent's request, by the request's id.</summary>
    private static List<JsonNode> Answers(string log) =>
        File.ReadAllLines(log).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!)
            .Where(f => f["method"] is null && f["id"]?.GetValue<string>() == "agent-1").ToList();

    private static string Outcome(JsonNode answer) => answer["result"]!["outcome"]!["outcome"]!.GetValue<string>();

    // ── answered ─────────────────────────────────────────────────────────────

    [Fact] // SOURCE-DERIVED
    public async Task An_approval_is_written_as_the_allow_once_option_and_the_request_carries_what_the_agent_asked()
    {
        var approver = Approver.Answering(AcpPermissionAnswer.Approve);

        var ended = await Run(Stub(approver), Turn());

        Assert.Equal("permission: allow-once", ended.FinalText);
        var request = Assert.Single(approver.Seen);
        Assert.Equal("acp-stub-session-1", request.SessionId);
        Assert.Equal("Edit hello.txt", JsonNode.Parse(request.ToolCallJson)!["title"]!.GetValue<string>());
        Assert.Equal(
            [("allow-always", "Always allow", "allow_always"), ("allow-once", "Allow", "allow_once"), ("reject-once", "Reject", "reject_once")],
            request.Options.Select(o => (o.OptionId, o.Name, o.Kind)));
    }

    [Fact] // SOURCE-DERIVED
    public async Task A_denial_is_written_as_the_reject_once_option()
    {
        var ended = await Run(Stub(Approver.Answering(AcpPermissionAnswer.Deny)), Turn());

        Assert.Equal("permission: reject-once", ended.FinalText);
    }

    [Fact] // SOURCE-DERIVED: the host's own choice among the agent's options, an allow_always included
    public async Task A_named_option_the_agent_offered_is_written_and_one_it_did_not_is_a_refusal()
    {
        var chosen = await Run(Stub(Approver.Answering(AcpPermissionAnswer.Choose("allow-always"))), Turn());
        var invented = await Run(Stub(Approver.Answering(AcpPermissionAnswer.Choose("grant-everything"))), Turn());

        Assert.Equal("permission: allow-always", chosen.FinalText);
        Assert.Equal("permission: reject-once", invented.FinalText);
    }

    [Fact] // SOURCE-DERIVED: the gate fails closed, as IToolApprover's does
    public async Task An_approver_that_throws_or_answers_null_refuses()
    {
        var threw = await Run(Stub(new Approver((_, _) => throw new InvalidOperationException("the dialog broke"))), Turn());
        var nothing = await Run(Stub(new Approver((_, _) => Task.FromResult<AcpPermissionAnswer>(null!))), Turn());

        Assert.Equal("permission: reject-once", threw.FinalText);
        Assert.Equal("permission: reject-once", nothing.FinalText);
    }

    [Fact] // SOURCE-DERIVED: a request is asked for as it arrives, so the host can hold one while a person decides
    public async Task A_person_who_takes_a_moment_is_waited_for()
    {
        var person = new TaskCompletionSource<AcpPermissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approver = new Approver((_, _) => person.Task);
        var turn = Run(Stub(approver), Turn());

        await approver.Asked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(300);
        Assert.False(turn.IsCompleted);
        person.SetResult(AcpPermissionAnswer.Approve);

        Assert.Equal("permission: allow-once", (await turn.WaitAsync(TimeSpan.FromSeconds(20))).FinalText);
    }

    // ── never answered ───────────────────────────────────────────────────────

    [Fact] // SOURCE-DERIVED: half of a 4 s inactivity window is 2 s; the agent then has the other half to act on it
    public async Task A_request_nobody_answers_is_refused_inside_the_window_and_a_late_answer_is_dropped()
    {
        var log = NewLog();
        CancellationToken seen = default;
        var late = new TaskCompletionSource<AcpPermissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approver = new Approver((_, ct) => { seen = ct; return late.Task; });   // ignores its token, as a careless host may

        var ended = await Run(Stub(approver, log), Turn(timeoutSeconds: 4));

        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Equal("permission: reject-once", ended.FinalText);
        Assert.True(seen.IsCancellationRequested);

        late.SetResult(AcpPermissionAnswer.Approve);
        await Task.Delay(300);
        var answer = Assert.Single(Answers(log));
        Assert.Equal("reject-once", answer["result"]!["outcome"]!["optionId"]!.GetValue<string>());
    }

    [Fact] // SOURCE-DERIVED: the protocol answers each pending request cancelled when its turn is cancelled
    public async Task A_cancelled_turn_answers_the_pending_request_cancelled_once()
    {
        var log = NewLog();
        var approver = new Approver((_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => AcpPermissionAnswer.Approve, TaskScheduler.Default));
        using var cts = new CancellationTokenSource();

        var turn = Stub(approver, log).StreamAsync(Turn(), cts.Token).ToListAsync().AsTask();
        await approver.Asked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn.WaitAsync(TimeSpan.FromSeconds(20)));
        var answer = Assert.Single(Answers(log));
        Assert.Equal("cancelled", Outcome(answer));
    }

    // ── a wait that is the approver's ────────────────────────────────────────

    private static AcpAgentSession Holding(IAcpPermissionApprover approver, string? log = null) =>
        new(new ProcessRunner(), new LyntaiOptions(), StubCommand,
            environment: log is null ? null : new Dictionary<string, string?> { ["LYNTAI_ACP_STUB_LOG"] = log },
            permissionApprover: approver, permissionWait: AcpPermissionWait.AsLongAsApproverHolds);

    [Fact] // SOURCE-DERIVED: a 3 s window, and an approver that holds the request for 5 s; the shipped runner is told
    public async Task A_request_the_approver_holds_past_the_whole_window_is_waited_for_and_the_turn_goes_on()
    {
        var approver = new Approver(async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return AcpPermissionAnswer.Approve;
        });

        var ended = await Run(Holding(approver), Turn(timeoutSeconds: 3));

        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.Equal("permission: allow-once", ended.FinalText);
    }

    [Fact] // the same hold without the option is the default: refused at half the window, and a late answer is dropped
    public async Task Without_the_option_the_same_hold_is_refused_at_half_the_window()
    {
        var approver = new Approver(async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return AcpPermissionAnswer.Approve;
        });

        var ended = await Run(Stub(approver), Turn(timeoutSeconds: 3));

        Assert.Equal("permission: reject-once", ended.FinalText);
    }

    [Fact] // SOURCE-DERIVED: no window answers it, so only the caller's cancellation does, with the protocol's cancelled outcome
    public async Task A_cancelled_turn_answers_a_request_the_approver_still_holds_cancelled()
    {
        var log = NewLog();
        var approver = new Approver((_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => AcpPermissionAnswer.Approve, TaskScheduler.Default));
        using var cts = new CancellationTokenSource();

        var turn = Holding(approver, log).StreamAsync(Turn(timeoutSeconds: 2), cts.Token).ToListAsync().AsTask();
        await approver.Asked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(TimeSpan.FromSeconds(3));   // past the window the default would have refused at
        Assert.False(turn.IsCompleted);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal("cancelled", Outcome(Assert.Single(Answers(log))));
    }

    [Fact] // a runner that cannot be told would end the turn the person is deciding, so the option is refused where it is registered
    public void A_runner_that_cannot_be_told_refuses_the_option_when_the_session_is_built()
    {
        var approver = Approver.Answering(AcpPermissionAnswer.Deny);

        var direct = Assert.Throws<InvalidOperationException>(() => new AcpAgentSession(new FakeProcessRunner(), new LyntaiOptions(), "agent",
            permissionApprover: approver, permissionWait: AcpPermissionWait.AsLongAsApproverHolds));
        Assert.Contains("CanPauseInactivity", direct.Message);

        using var sp = new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("held-acp", "agent", permissionApprover: approver, permissionWait: AcpPermissionWait.AsLongAsApproverHolds))
            .AddSingleton<IProcessRunner>(new FakeProcessRunner()).BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredKeyedService<IAgentSession>("held-acp"));
    }

    [Fact] // the approver's wait is a wait for an approver
    public void The_approvers_wait_needs_an_approver()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("held-acp", "agent", permissionWait: AcpPermissionWait.AsLongAsApproverHolds)));
        Assert.Throws<ArgumentException>(() => new AcpAgentSession(new ProcessRunner(), new LyntaiOptions(), "agent",
            permissionWait: AcpPermissionWait.AsLongAsApproverHolds));
    }

    [Fact] // the door states what a request does under the option
    public void The_door_says_a_request_waits_as_long_as_the_approver_holds_it_only_under_the_option()
    {
        var approver = Approver.Answering(AcpPermissionAnswer.Deny);
        using var sp = new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("held-acp", "agent", permissionApprover: approver, permissionWait: AcpPermissionWait.AsLongAsApproverHolds)
            .AddAcpAgentSession("timed-acp", "agent", permissionApprover: approver)).BuildServiceProvider();
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        string Evidence(string id) =>
            registry.Get(id).Describe().Doors.Single().Abilities.Single(a => a.Name == AgentAbilities.PermissionRequests).Evidence!;

        Assert.Contains("as long as the approver holds it", Evidence("held-acp"));
        Assert.Contains("half the inactivity window", Evidence("timed-acp"));
    }

    // ── what does not change ─────────────────────────────────────────────────

    [Fact] // SOURCE-DERIVED: a read-only turn is a gate the approver cannot weaken, and is never asked
    public async Task A_read_only_turn_rejects_the_request_without_asking()
    {
        var approver = Approver.Answering(AcpPermissionAnswer.Approve);

        var ended = await Run(Stub(approver), Turn(AgentToolPolicy.ReadOnly));

        Assert.Equal("permission: reject-once", ended.FinalText);
        Assert.Empty(approver.Seen);
    }

    [Fact] // SOURCE-DERIVED
    public async Task With_no_approver_a_write_turn_allows_the_request_once_as_it_always_did()
    {
        var ended = await Run(Stub(null), Turn());

        Assert.Equal("permission: allow-once", ended.FinalText);
    }

    // ── the ability ──────────────────────────────────────────────────────────

    [Fact]
    public void The_door_states_permission_requests_supported_only_where_an_approver_is_registered()
    {
        var approver = Approver.Answering(AcpPermissionAnswer.Deny);
        using var sp = new ServiceCollection().AddLyntai(b => b
            .AddAcpAgentSession("held-acp", "agent", permissionApprover: approver)
            .AddAcpAgentSession("plain-acp", "agent")).BuildServiceProvider();
        var registry = sp.GetRequiredService<AgentProviderRegistry>();

        AgentSupport Stated(string id) =>
            registry.Get(id).Describe().Doors.Single().SupportFor(AgentAbilities.PermissionRequests);

        Assert.Equal(AgentSupport.Supported, Stated("held-acp"));
        Assert.Equal(AgentSupport.Unsupported, Stated("plain-acp"));
    }

    [Fact]
    public void An_answer_names_an_option_only_when_it_chooses_one()
    {
        Assert.Null(AcpPermissionAnswer.Approve.OptionId);
        Assert.Null(AcpPermissionAnswer.Deny.OptionId);
        Assert.Equal("allow-always", AcpPermissionAnswer.Choose("allow-always").OptionId);
        Assert.Throws<ArgumentException>(() => AcpPermissionAnswer.Choose(" "));
    }
}
