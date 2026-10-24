using Lyntai.Diagnostics;
using Lyntai.Guards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Agents;

/// <summary>Runs ONE tool call the way every door onto the app's registered <see cref="ITool"/>s must: the
/// guard rail inspects the arguments before the tool runs and the observation before it goes back, the
/// <see cref="IToolApprover"/>s approve the call in between, a throw becomes an error observation the model
/// can recover from, and the call is recorded as an <c>execute_tool</c> span and on
/// <c>lyntai.tool.invocations</c>. <see cref="ToolLoop"/> and the hosted MCP endpoint both call it, so a guard
/// or an approver enforced on one is enforced on the other.</summary>
public static class ToolInvocation
{
    /// <summary>Gate, run and gate one call of <paramref name="tool"/>, with no approvers.
    /// <para>What a <see cref="GatedToolResult.Blocked"/> result MEANS is the door's to decide: a door that
    /// owns the loop can end the run, and one inside a loop somebody else owns can only report it
    /// (<c>docs/DECISIONS.md</c> D75).</para></summary>
    /// <param name="tool">The tool the model asked for.</param>
    /// <param name="argumentsJson">The arguments it supplied, as a JSON object.</param>
    /// <param name="guards">The rail to inspect both ways; null runs the tool ungated.</param>
    /// <param name="logger">Where a block and a throwing tool are logged; null logs nothing.</param>
    /// <param name="ct">The caller's cancellation, which propagates. The tool's own timeout is a throw like
    /// any other, reported as an error observation.</param>
    public static Task<GatedToolResult> InvokeGatedAsync(
        ITool tool, string argumentsJson, IGuardRail? guards, ILogger? logger = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return InvokeGatedAsync(tool.Name, tool, argumentsJson, guards, approvers: null,
            Inference.ProviderConsumers.Default, callId: null, logger, unknown: null, ct);
    }

    /// <summary>Gate, approve, run and gate one call of <paramref name="tool"/> — the flow a door onto the
    /// app's tools runs when the container registers <see cref="IToolApprover"/>s. A refusal comes back as
    /// <see cref="GatedToolResult.Denied"/>, with an error observation carrying the reason, and the tool does not
    /// run; it is never <see cref="GatedToolResult.Blocked"/>, so no door ends its run on it.</summary>
    /// <param name="tool">The tool the model asked for.</param>
    /// <param name="argumentsJson">The arguments it supplied, as a JSON object.</param>
    /// <param name="guards">The rail to inspect both ways; null runs the tool ungated.</param>
    /// <param name="approvers">Every approver the call must pass, in order; null or empty approves it.</param>
    /// <param name="consumer">The consumer the call belongs to, as the approvers see it.</param>
    /// <param name="callId">The model's id for the call, when the transport carries one.</param>
    /// <param name="logger">Where a block, a refusal and a throwing tool or approver are logged; null logs
    /// nothing.</param>
    /// <param name="ct">The caller's cancellation, which propagates.</param>
    public static Task<GatedToolResult> InvokeGatedAsync(
        ITool tool, string argumentsJson, IGuardRail? guards, IEnumerable<IToolApprover>? approvers,
        string consumer, string? callId = null, ILogger? logger = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return InvokeGatedAsync(tool.Name, tool, argumentsJson, guards, approvers, consumer, callId, logger,
            unknown: null, ct);
    }

    /// <summary>The same flow for a name that may resolve to no tool, which only a door that looks tools up
    /// by the model's own words has: the guards still see the call, the approvers do not — nothing would
    /// run — and <paramref name="unknown"/> supplies the observation.</summary>
    internal static async Task<GatedToolResult> InvokeGatedAsync(
        string name, ITool? tool, string argumentsJson, IGuardRail? guards, IEnumerable<IToolApprover>? approvers,
        string consumer, string? callId, ILogger? logger, Func<string>? unknown, CancellationToken ct)
    {
        logger ??= NullLogger.Instance;
        if (guards is not null)
        {
            var pre = await guards.InspectToolCallAsync(name, argumentsJson, ct).ConfigureAwait(false);
            if (pre.Result == GuardOutcome.Kind.Block)
            {
                logger.LogInformation("guard blocked tool call {Tool}: {Reason}", name, pre.Reason);
                return GatedToolResult.Block(pre.Reason ?? $"tool call '{name}' blocked by guard",
                    ToolObservations.Error($"tool call blocked by guard: {pre.Reason ?? "no reason given"}"),
                    argumentsJson);
            }
            if (pre.Result == GuardOutcome.Kind.Replace) argumentsJson = pre.Replacement!;
        }

        if (tool is not null && approvers is not null)
        {
            var refusal = await ApproveAsync(
                new ToolApprovalRequest(name, argumentsJson, consumer, callId), approvers, logger, ct).ConfigureAwait(false);
            if (refusal is not null)
                return new GatedToolResult(false, refusal,
                    ToolObservations.Error($"tool call refused by approval: {refusal}"), argumentsJson) { Denied = true };
        }

        var observation = tool is null
            ? unknown?.Invoke() ?? ToolObservations.Error($"unknown tool \"{name}\"")
            : await RunAsync(tool, argumentsJson, logger, ct).ConfigureAwait(false);

        if (guards is not null)
        {
            var post = await guards.InspectToolResultAsync(name, observation, ct).ConfigureAwait(false);
            if (post.Result == GuardOutcome.Kind.Block)
            {
                logger.LogInformation("guard blocked the observation from {Tool}: {Reason}", name, post.Reason);
                return GatedToolResult.Block(post.Reason ?? $"observation from '{name}' blocked by guard",
                    ToolObservations.Error($"observation blocked by guard: {post.Reason ?? "no reason given"}"),
                    argumentsJson);
            }
            if (post.Result == GuardOutcome.Kind.Replace) observation = post.Replacement!;
        }
        return new GatedToolResult(false, null, observation, argumentsJson);
    }

    /// <summary>Put the call to every approver in order; null when all approve, else the first refusal's
    /// reason. A throw or a null answer refuses: an approval gate that fails open is no gate.</summary>
    private static async Task<string?> ApproveAsync(
        ToolApprovalRequest request, IEnumerable<IToolApprover> approvers, ILogger logger, CancellationToken ct)
    {
        foreach (var approver in approvers)
        {
            string? refusal;
            try
            {
                var answer = await approver.ApproveAsync(request, ct).ConfigureAwait(false);
                refusal = answer is null ? "the approver gave no answer"
                    : answer.Approved ? null
                    : string.IsNullOrWhiteSpace(answer.Reason) ? "no reason given" : answer.Reason;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // the exception's text stays in the log: it describes the host, not the call, to the model
                logger.LogWarning(ex, "tool approver {Approver} failed on {Tool}; the call is refused",
                    approver.GetType().Name, request.ToolName);
                refusal = "the approval check failed";
            }
            if (refusal is null) continue;
            logger.LogInformation("tool call {Tool} for {Consumer} refused by approval: {Reason}",
                request.ToolName, request.Consumer, refusal);
            return refusal;
        }
        return null;
    }

    private static async Task<string> RunAsync(ITool tool, string argumentsJson, ILogger logger, CancellationToken ct)
    {
        using var activity = LyntaiDiagnostics.StartToolCall(tool.Name);
        var error = false;
        try
        {
            logger.LogDebug("invoking tool {Tool} with {Args}", tool.Name, argumentsJson);
            return await tool.InvokeAsync(argumentsJson, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller's cancel is not a tool error
        }
        catch (Exception ex)
        {
            error = true;
            logger.LogWarning(ex, "tool {Tool} threw", tool.Name);
            return ToolObservations.Error(ex.Message);
        }
        finally
        {
            LyntaiDiagnostics.EndToolCall(activity, tool.Name, error);
        }
    }
}

/// <summary>What <see cref="ToolInvocation.InvokeGatedAsync(ITool,string,IGuardRail?,ILogger?,CancellationToken)"/>
/// produced.</summary>
/// <param name="Blocked">A guard refused the call, or withheld its observation.</param>
/// <param name="Reason">When blocked, the guard's reason, or one naming the gate when it gave none; when
/// <see cref="Denied"/>, the approver's.</param>
/// <param name="Observation">What to feed back to the model: the tool's result after any guard rewrite, an
/// error observation when the tool threw or an approver refused — or, when blocked, an error observation
/// reporting the refusal, for a door that can only report a block rather than end the run.</param>
/// <param name="ArgumentsJson">The arguments the tool ran with — a guard's rewrite, when one applied.</param>
public sealed record GatedToolResult(bool Blocked, string? Reason, string Observation, string ArgumentsJson)
{
    /// <summary>An <see cref="IToolApprover"/> refused the call, so the tool did not run. Never set with
    /// <see cref="Blocked"/>: a refusal is an ordinary tool result the model sees, and the loop goes on.</summary>
    public bool Denied { get; init; }

    internal static GatedToolResult Block(string reason, string observation, string argumentsJson) =>
        new(true, reason, observation, argumentsJson);
}
