using System.Runtime.CompilerServices;
using Lyntai.Agents;
using Lyntai.Guards;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.Http;

/// <summary>One turn of an agent whose executor is Lyntai's own <see cref="ToolLoop"/> over a chat-completions
/// endpoint. A turn builds its provider and loop afresh, so the response facts it collects
/// (<see cref="HttpExchanges"/>) belong to that turn alone, and routes to the ONE endpoint the registration names:
/// nothing falls over to another, so the retry budget stays <see cref="RetryingAgentSession"/>'s.
/// <list type="bullet">
/// <item>Events are the loop's own: assistant text per turn, a <see cref="ToolCall"/> and a
/// <see cref="ToolResult"/> per round trip, a <see cref="UsageFinal"/> and exactly one <see cref="SessionEnded"/>.
/// There is no <see cref="SessionStarted"/>: the loop has no session id.</item>
/// <item>A failed terminal carries the failure the response's own facts read as, else a fatal one with no field
/// behind it; its <see cref="AgentFailure.Delivery"/> is <see cref="AgentDelivery.Uncertain"/> once a tool ran,
/// <see cref="AgentDelivery.Taken"/> once anything answered, <see cref="AgentDelivery.NeverSent"/> only when the
/// connection never opened, and <see cref="AgentDelivery.Uncertain"/> for a request that may have left.</item>
/// <item>The caller's cancellation propagates, as on the other doors; it is never read as a failure.</item>
/// <item>Not bound by the front door: no cap, limiter, cache or refusal screen applies, as for every session.</item>
/// </list></summary>
internal sealed class HttpAgentSession(
    string id,
    HttpModelOptions config,
    Func<HttpClient> httpFactory,
    bool disposeHttpClient,
    IToolRegistry registry,
    LyntaiOptions options,
    IReadOnlyList<IToolApprover> approvers,
    IGuardRail? guards = null,
    IToolSelector? selector = null,
    TimeProvider? time = null,
    ILogger<HttpModelProvider>? providerLogger = null,
    ILogger<ToolLoop>? loopLogger = null) : IAgentSession
{
    private static readonly AgentDoor Door = HttpAgentDoor.Describe("http").Doors[0];

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions turn, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        // the keyed session keeps the layer's rule when no provider stands in front of it: refused, never dropped
        var refused = Door.Unhonourable(turn);
        if (refused.Count > 0)
        {
            yield return CliAgentLoop.Refused(AgentProvider.OptionRefusedSubtype,
                $"door '{HttpAgentDoor.DoorId}' cannot honour: {string.Join(", ", refused)}");
            yield break;
        }

        var exchanges = new HttpExchanges(time);
        var provider = new HttpModelProvider(id, config, httpFactory, options, providerLogger, disposeHttpClient, exchanges);
        var loop = new ToolLoop(new PinnedTextClient(provider), Roster(turn), options, loopLogger, guards, selector, approvers);
        var request = new TextRequest
        {
            Messages = Messages(turn),
            Model = turn.Model,
            TimeoutSeconds = turn.TimeoutSeconds,
            Consumer = ProviderConsumers.Agent,
        };

        var sawEvent = false;
        var sawTool = false;
        await foreach (var evt in loop.StreamAsync(request, ct: ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (evt is SessionEnded ended)
            {
                yield return Stamp(ended, exchanges, sawEvent, sawTool);
                yield break;
            }
            sawTool |= evt is ToolCall or ToolResult;
            sawEvent = true;
            yield return evt;
        }
    }

    private static List<TextMessage> Messages(AgentSessionOptions turn)
    {
        var messages = new List<TextMessage>(2);
        if (!string.IsNullOrEmpty(turn.SystemPrompt)) messages.Add(TextMessage.System(turn.SystemPrompt));
        messages.Add(TextMessage.User(turn.Prompt));
        return messages;
    }

    // A read-only turn is offered no tool, so it can change nothing the host's tools would; a write turn is offered
    // the registered ones less what it denies. The registry finds a name case-insensitively, so a denial does too.
    private ToolRegistry Roster(AgentSessionOptions turn)
    {
        if (turn.ToolPolicy == AgentToolPolicy.ReadOnly) return new ToolRegistry([]);
        var denied = new HashSet<string>(turn.DisallowedTools, StringComparer.OrdinalIgnoreCase);
        return new ToolRegistry(registry.Tools.Where(t => !denied.Contains(t.Name)));
    }

    private static SessionEnded Stamp(SessionEnded ended, HttpExchanges exchanges, bool sawEvent, bool sawTool)
    {
        if (!ended.IsError) return ended;
        var failure = exchanges.Failure ?? new AgentFailure(AgentFailureKind.Fatal, "no-failure-fields");
        var delivery = sawTool ? AgentDelivery.Uncertain
            : exchanges.Responded || sawEvent ? AgentDelivery.Taken
            : exchanges.NeverLeft ? AgentDelivery.NeverSent
            : AgentDelivery.Uncertain;
        return ended with { Verdict = VerdictOf(ended, failure), Failure = failure with { Delivery = delivery } };
    }

    // The failure's mapping onto the verdict table, which is what SessionEnded.Verdict promises. The verdicts the
    // loop read with more than the failure knows stay: a key the call never carried, a deadline, a refusal.
    private static ProviderVerdict VerdictOf(SessionEnded ended, AgentFailure failure) =>
        ended.Verdict is ProviderVerdict.NotConfigured or ProviderVerdict.Timeout or ProviderVerdict.Refused
            or ProviderVerdict.Unsupported or ProviderVerdict.ContextWindowExceeded
            ? ended.Verdict
            : ProviderVerdictClassifier.FromAgentFailure(failure, ended.Diagnostic);

    /// <summary>The ONE route a turn takes: a single provider and no candidate list, so no verdict advances to
    /// another endpoint and the capabilities the loop reads are that endpoint's own.</summary>
    private sealed class PinnedTextClient(HttpModelProvider provider) : ITextClient
    {
        public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
            provider.CompleteAsync(req, ct);

        public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
            provider.StreamAsync(req, ct);

        public ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(TextRequest req, CancellationToken ct = default) =>
            ValueTask.FromResult<ProviderCapabilities?>(provider.Capabilities);
    }
}
