using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;

namespace Lyntai.Tests.Agents;

/// <summary>What a host's own door can get wrong, one flag per stream rule, so a case can be shown failing against
/// a door that breaks exactly it.</summary>
[Flags]
public enum StandInFlaw
{
    None = 0,

    /// <summary>Every ending the agent prints is handed over, not the first.</summary>
    SecondTerminal = 1,

    /// <summary>A failed terminal carries no <see cref="AgentFailure"/> although the door states typed failures.</summary>
    NoFailure = 2,

    /// <summary>Every failed turn reads as one the door had taken, whatever it had shown.</summary>
    AlwaysTaken = 4,

    /// <summary>A limit sentence a tool printed turns the terminal into a limit.</summary>
    ReadsToolText = 8,

    /// <summary>A cancelled turn is ended by a failed terminal of the door's own making.</summary>
    TerminalOnCancel = 16,
}

/// <summary>A host's own provider, written the way <see cref="IAgentProvider"/> says to, over a wire of its own (a
/// word and the rest of the line: <c>start</c>, <c>say</c>, <c>tool</c>, <c>output</c>, <c>end</c>, <c>fail</c>) that no
/// built-in door reads. It states every ability and "spawns" through <see cref="IProcessRunner"/>.</summary>
internal static class DialectStandIn
{
    public const string Id = "stand-in";

    public static AgentProvider Provider(IProcessRunner runner, StandInFlaw flaw = StandInFlaw.None, string id = Id) => new(
        new AgentProviderDescriptor
        {
            Id = id,
            Doors =
            [
                new AgentDoor
                {
                    Id = "local",
                    Kind = "host-own",
                    Abilities =
                    [
                        Claim(AgentAbilities.Resume, AgentSupport.Supported, AgentAbilitySource.Negotiated),
                        Claim(AgentAbilities.ReadOnlyGate, AgentSupport.Unsupported, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.McpServers, AgentSupport.Unknown, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.DisallowedTools, AgentSupport.Unsupported, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.SystemPrompt, AgentSupport.Unknown, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.Model, AgentSupport.Unknown, AgentAbilitySource.Negotiated),
                        Claim(AgentAbilities.Steering, AgentSupport.Unsupported, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.PlanEvents, AgentSupport.Unknown, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.LiveUsage, AgentSupport.Unknown, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.TypedFailures, AgentSupport.Supported, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.Accounts, AgentSupport.Unsupported, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.UsageReading, AgentSupport.Unsupported, AgentAbilitySource.Declared),
                        Claim(AgentAbilities.PermissionRequests, AgentSupport.Unsupported, AgentAbilitySource.Declared),
                    ],
                },
            ],
        },
        _ => new Session(runner, flaw));

    private static AgentAbility Claim(string name, AgentSupport support, AgentAbilitySource source) =>
        new(name, support, source) { Evidence = $"the stand-in states {name} as {support}" };

    private sealed class Session(IProcessRunner runner, StandInFlaw flaw) : IAgentSession
    {
        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
            AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var terminal = false;
            var sawEvent = false;
            var sawTool = false;
            var sawLimit = false;
            string? sessionId = null;

            await using var lines = runner.StreamLinesAsync("stand-in", [], options.Prompt, ct: ct).GetAsyncEnumerator(ct);
            while (true)
            {
                string? line = null;
                Exception? fault = null;
                var cancelled = false;
                try
                {
                    if (!await lines.MoveNextAsync()) break;
                    line = lines.Current;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    fault = ex;
                }

                if (cancelled)
                {
                    if (flaw.HasFlag(StandInFlaw.TerminalOnCancel))
                        yield return Stamp(new SessionEnded(ProviderVerdict.Failed, true, null, sessionId, null, "cancelled"));
                    ct.ThrowIfCancellationRequested();
                }

                if (fault is not null)
                {
                    if (!terminal) yield return Stamp(ReadText(new SessionEnded(ProviderVerdict.Failed, true, null, sessionId, null, fault.Message)));
                    yield break;
                }

                var space = line!.IndexOf(' ');
                var word = space < 0 ? line : line[..space];
                var rest = space < 0 ? "" : line[(space + 1)..];
                sawLimit |= word == "output" && rest.Contains("limit", StringComparison.OrdinalIgnoreCase);

                AgentStreamEvent? evt = word switch
                {
                    "start" => new SessionStarted(sessionId = rest),
                    "say" => new TextDelta(rest),
                    "tool" => new ToolCall(rest, "{}", "c1"),
                    "output" => new ToolResult("c1", rest, false),
                    "end" => new SessionEnded(ProviderVerdict.Ok, false, null, sessionId, rest, null),
                    "fail" => new SessionEnded(ProviderVerdict.Failed, true, null, sessionId, null, rest),
                    _ => null,
                };
                if (evt is null) continue;

                if (evt is SessionEnded ended)
                {
                    if (terminal && !flaw.HasFlag(StandInFlaw.SecondTerminal)) continue;
                    terminal = true;
                    evt = Stamp(ReadText(ended));
                }
                else
                {
                    sawTool |= evt is ToolCall or ToolResult;
                    sawEvent = true;
                }
                yield return evt;
            }

            if (terminal) yield break;
            yield return Stamp(new SessionEnded(ProviderVerdict.Failed, true, null, sessionId, null, "no terminal"));

            // the flaw: a limit sentence in a tool's output becomes the turn's own limit
            SessionEnded ReadText(SessionEnded ended) =>
                sawLimit && flaw.HasFlag(StandInFlaw.ReadsToolText)
                    ? new SessionEnded(ProviderVerdict.RateLimited, true, null, sessionId, null, "limit")
                    {
                        Failure = new AgentFailure(AgentFailureKind.Limit, "stand-in.text"),
                    }
                    : ended;

            // what a failed turn had shown is the one fact its delivery is read from
            SessionEnded Stamp(SessionEnded ended)
            {
                if (!ended.IsError || flaw.HasFlag(StandInFlaw.NoFailure) && ended.Failure is null) return ended;
                var delivery = flaw.HasFlag(StandInFlaw.AlwaysTaken) ? AgentDelivery.Taken
                    : sawTool ? AgentDelivery.Uncertain : sawEvent ? AgentDelivery.Taken : AgentDelivery.NeverSent;
                var failure = ended.Failure ?? new AgentFailure(AgentFailureKind.Fatal, "stand-in.fault");
                return ended with { Failure = failure with { Delivery = delivery } };
            }
        }
    }
}
