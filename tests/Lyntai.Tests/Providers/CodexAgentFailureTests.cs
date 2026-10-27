using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.CodexCli;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Providers;

/// <summary>What the codex door says about a failed turn: its own <c>turn.failed</c> and nothing the agent
/// printed. The sentences are recorded from codex-acp 2.1.1 (Codex 0.159.3), the same two
/// <c>CodexAgentSessionTests</c> pins for their verdict; the exec JSONL carries a message and no typed
/// field, so the sentence is the door's own failure here.</summary>
public class CodexAgentFailureTests
{
    private const string OutOfCredits =
        "Internal error: Your workspace is out of credits. Ask your workspace owner to refill in order to continue.";

    private static string[] FailedTurn(string message, params string[] before) =>
    [
        """{"type":"thread.started","thread_id":"thread-fail"}""",
        """{"type":"turn.started"}""",
        .. before,
        $$$"""{"type":"turn.failed","error":{"message":"{{{message}}}"}}""",
    ];

    private static async Task<SessionEnded> Run(params string[] lines)
    {
        var session = new CodexAgentSession(new FakeProcessRunner(lines), new LyntaiOptions(), command: "codex");
        var events = await session.StreamAsync(new AgentSessionOptions { Prompt = "hi" }).ToListAsync();
        return events.OfType<SessionEnded>().Single();
    }

    [Fact] // the recorded exhaustion sentence: an allowance spent until a person refills it
    public async Task An_exhausted_workspace_is_a_limit_that_waits_for_its_owner()
    {
        var ended = await Run(FailedTurn(OutOfCredits));

        Assert.Equal(ProviderVerdict.RateLimited, ended.Verdict);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RestrictionCause.Credit, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Account, failure.Restriction.Subject);
        Assert.Equal(RecoveryKind.OwnerAction, failure.Restriction.Recovery.Kind);
        Assert.Null(failure.Restriction.Recovery.At);
        Assert.StartsWith("codex.turn.failed", failure.By);
        // the thread started, so the door had the prompt: failure class and delivery are independent
        Assert.Equal(AgentDelivery.Taken, failure.Delivery);
    }

    [Fact] // the recorded capacity sentence: the MODEL's, so transient and the account is not blamed
    public async Task A_model_at_capacity_is_transient_and_names_the_model_not_the_account()
    {
        var ended = await Run(FailedTurn("Selected model is at capacity. Please try a different model."));

        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        var failure = ended.Failure!;
        Assert.Equal(AgentFailureKind.Transient, failure.Kind);
        Assert.Equal(RestrictionCause.Capacity, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Model, failure.Restriction.Subject);
        Assert.Equal(RecoveryKind.Unknown, failure.Restriction.Recovery.Kind);
    }

    [Fact] // an unrecognised failure is fatal and says what decided it; the verdict stays the classifier's
    public async Task A_failure_no_table_reads_is_fatal()
    {
        var ended = await Run(FailedTurn("stub turn failure"));

        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Null(ended.Failure.Restriction);
        Assert.Equal("codex.turn.failed", ended.Failure.By);
    }

    [Fact] // a turn that ended well carries no failure, whatever the agent and its tools printed
    public async Task Agent_prose_and_tool_output_never_make_a_failure_of_a_good_turn()
    {
        var ended = await Run(
            """{"type":"thread.started","thread_id":"t"}""",
            """{"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"You've hit your usage limit. 529 Overloaded. Your workspace is out of credits."}}""",
            """{"type":"item.completed","item":{"id":"i2","type":"command_execution","command":"curl x","aggregated_output":"HTTP 429 Too Many Requests","exit_code":22,"status":"failed"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":1,"cached_input_tokens":0,"cache_write_input_tokens":0,"output_tokens":1}}""");

        Assert.Equal(ProviderVerdict.Ok, ended.Verdict);
        Assert.False(ended.IsError);
        Assert.Null(ended.Failure);
    }

    [Fact] // …and on a failed turn the failure is the door's sentence, not the agent's words before it
    public async Task Agent_prose_before_a_generic_failure_does_not_decide_its_kind()
    {
        var ended = await Run(FailedTurn("stub turn failure",
            """{"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"Rate limit exceeded: you are out of credits, 529 Overloaded"}}""",
            """{"type":"item.completed","item":{"id":"i2","type":"command_execution","command":"x","aggregated_output":"401 Unauthorized","exit_code":1,"status":"failed"}}"""));

        Assert.Equal(AgentFailureKind.Fatal, ended.Failure!.Kind);
        Assert.Equal(ProviderVerdict.Failed, ended.Verdict);
    }
}
