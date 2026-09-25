using Lyntai.Diagnostics;
using Lyntai.Guards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Agents;

/// <summary>Runs ONE tool call the way every door onto the app's registered <see cref="ITool"/>s must: the
/// guard rail inspects the arguments before the tool runs and the observation before it goes back, a throw
/// becomes an error observation the model can recover from, and the call is recorded as an
/// <c>execute_tool</c> span and on <c>lyntai.tool.invocations</c>. <see cref="ToolLoop"/> and the hosted MCP
/// endpoint both call it, so a guard enforced on one is enforced on the other.</summary>
public static class ToolInvocation
{
    /// <summary>Gate, run and gate one call of <paramref name="tool"/>.
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
        return InvokeGatedAsync(tool.Name, tool, argumentsJson, guards, logger, unknown: null, ct);
    }

    /// <summary>The same flow for a name that may resolve to no tool, which only a door that looks tools up
    /// by the model's own words has: the guards still see the call, and <paramref name="unknown"/> supplies
    /// the observation.</summary>
    internal static async Task<GatedToolResult> InvokeGatedAsync(
        string name, ITool? tool, string argumentsJson, IGuardRail? guards, ILogger? logger,
        Func<string>? unknown, CancellationToken ct)
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
/// <param name="Reason">When blocked, the guard's reason, or one naming the gate when it gave none.</param>
/// <param name="Observation">What to feed back to the model: the tool's result after any guard rewrite, an
/// error observation when the tool threw — or, when blocked, an error observation reporting the refusal,
/// for a door that can only report a block rather than end the run.</param>
/// <param name="ArgumentsJson">The arguments the tool ran with — a guard's rewrite, when one applied.</param>
public sealed record GatedToolResult(bool Blocked, string? Reason, string Observation, string ArgumentsJson)
{
    internal static GatedToolResult Block(string reason, string observation, string argumentsJson) =>
        new(true, reason, observation, argumentsJson);
}
