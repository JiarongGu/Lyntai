namespace Lyntai.Agents;

/// <summary>Approves or refuses ONE tool call before the tool runs, on every door onto the app's registered
/// <see cref="ITool"/>s — the tool loop's native and prompt paths and the hosted MCP endpoint alike, because
/// all of them invoke through <see cref="ToolInvocation"/>. Registered as a DI collection
/// (<c>AddToolApprover</c>); none registered runs every call as before.
/// <para><b>Every registered approver must approve</b>, asked in registration order, and the first refusal
/// decides. A refusal is a tool RESULT the model sees, carrying <see cref="ToolApprovalResult.Reason"/>, and
/// the loop goes on; it never ends the run, which is a guard Block's job. An approver that throws, or answers
/// null, refuses the call: the gate fails closed. The caller's cancellation propagates.</para>
/// <para>Unlike a guard, an approver never rewrites the call: it sees the arguments a guard already
/// rewrote, which are the ones that will run (<c>docs/DECISIONS.md</c> D210).</para></summary>
public interface IToolApprover
{
    /// <summary>Decide one call. Called only for a tool that exists; an unknown name runs nothing and is not
    /// put to the approvers.</summary>
    /// <param name="request">The call: tool name, arguments, consumer and the model's call id.</param>
    /// <param name="ct">The caller's cancellation.</param>
    Task<ToolApprovalResult> ApproveAsync(ToolApprovalRequest request, CancellationToken ct = default);
}

/// <summary>One tool call put to the <see cref="IToolApprover"/>s.</summary>
/// <param name="ToolName">The tool's <see cref="ITool.Name"/>.</param>
/// <param name="ArgumentsJson">The arguments the tool will run with, after any guard rewrite.</param>
/// <param name="Consumer">The consumer of the request the call belongs to — the tool loop's
/// <c>TextRequest.Consumer</c>, or the consumer whose CLI call stood up the hosted MCP endpoint.</param>
/// <param name="CallId">The model's id for the call on the native path; null on the prompt protocol, which
/// has none, and on the hosted MCP endpoint.</param>
public sealed record ToolApprovalRequest(string ToolName, string ArgumentsJson, string Consumer, string? CallId);

/// <summary>An <see cref="IToolApprover"/>'s answer: <see cref="Approve"/>, or <see cref="Deny"/> with the
/// reason the model is told.</summary>
public sealed record ToolApprovalResult
{
    private ToolApprovalResult(bool approved, string? reason)
    {
        Approved = approved;
        Reason = reason;
    }

    /// <summary>Whether the call may run.</summary>
    public bool Approved { get; }

    /// <summary>On a refusal, why — fed back to the model inside the error observation, so write it for the
    /// model to read.</summary>
    public string? Reason { get; }

    /// <summary>Let the call run.</summary>
    public static ToolApprovalResult Approve { get; } = new(true, null);

    /// <summary>Refuse the call, telling the model <paramref name="reason"/>.</summary>
    public static ToolApprovalResult Deny(string reason) => new(false, reason);
}
