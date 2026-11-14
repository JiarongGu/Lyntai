using System.Threading.Channels;
using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Providers.Basic;

/// <summary>What every door's kept run shares: the refusals a provider makes before a spawn, a refusal as a turn, the end a run's own
/// end gives a turn still open, and the run that never started.</summary>
internal static class KeptRuns
{
    // a timer's due time is a uint of milliseconds (about 49.7 days)
    private static readonly TimeSpan MaxWokenTurnQuiet = TimeSpan.FromDays(49);

    /// <summary>What a host set for the run, refused before anything is spawned, whichever door opens it.</summary>
    /// <exception cref="ArgumentException">A woken turn's quiet that is not positive, or longer than a timer counts.</exception>
    public static void Check(AgentRunOptions run)
    {
        if (run.WokenTurnQuiet is { } quiet && quiet <= TimeSpan.Zero)
            throw new ArgumentException("A woken turn's quiet is a span of silence, so it must be positive.", nameof(run));
        if (run.WokenTurnQuiet > MaxWokenTurnQuiet)
            throw new ArgumentException("A woken turn's quiet is counted by a timer, which holds at most 49 days.", nameof(run));
    }

    /// <summary>The refusals <see cref="AgentProvider"/> makes of a turn, made of a run's open: an account of another provider, or an
    /// option the door states <see cref="AgentSupport.Unsupported"/>. Null when the door may open; nothing is spawned for either.</summary>
    /// <exception cref="KeyNotFoundException">The provider has no such door.</exception>
    public static SessionEnded? Admit(AgentProviderDescriptor descriptor, string doorId, AgentSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(doorId);
        ArgumentNullException.ThrowIfNull(options);
        var door = descriptor.Doors.FirstOrDefault(d => string.Equals(d.Id, doorId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Agent provider '{descriptor.Id}' has no door '{doorId}'. Doors: {string.Join(", ", descriptor.Doors.Select(d => d.Id))}.");

        if (options.Account is { } account
            && !string.Equals(account.Provider, descriptor.Id, StringComparison.Ordinal)
            && !string.Equals(account.Provider, descriptor.Agent, StringComparison.Ordinal))
            return CliAgentLoop.Refused(AgentProvider.AccountRefusedSubtype,
                $"account '{account.Name}' belongs to '{account.Provider}', not to provider '{descriptor.Id}'"
                + (descriptor.Agent is null ? "" : $" or its agent '{descriptor.Agent}'"));

        var refused = door.Unhonourable(options);
        return refused.Count > 0
            ? CliAgentLoop.Refused(AgentProvider.OptionRefusedSubtype, $"door '{door.Id}' cannot honour: {string.Join(", ", refused)}")
            : null;
    }

    /// <summary>A <see cref="IAgentRun.SendAsync"/> the run refused, as the turn <see cref="IAgentRun"/> promises: one
    /// <see cref="SessionEnded"/>, <see cref="ProviderVerdict.Unsupported"/>, never sent.</summary>
    public static AgentRunTurn Refused(string subtype, string why, string? sessionId) =>
        RefusedTurn(new SessionEnded(ProviderVerdict.Unsupported, true, subtype, sessionId, null, why)
        {
            Failure = new AgentFailure(AgentFailureKind.Fatal, "refused-by-run") { Delivery = AgentDelivery.NeverSent },
        });

    public static AgentRunTurn RefusedTurn(SessionEnded ended)
    {
        var channel = Channel.CreateBounded<AgentStreamEvent>(1);
        channel.Writer.TryWrite(ended);
        channel.Writer.TryComplete();
        return new AgentRunTurn(AgentRunTurnOrigin.Sent, channel.Reader.ReadAllAsync(), Task.FromResult(ended));
    }

    /// <summary>A turn the run's end finds open ends with the same cause, failed, whatever the run's own verdict: its work never
    /// finished.</summary>
    public static SessionEnded Cut(SessionEnded cause, CliAgentLoop.TurnWatch watch) =>
        CliAgentLoop.Stamp(cause.IsError
            ? cause
            : cause with
            {
                Verdict = ProviderVerdict.Failed, IsError = true, Subtype = AgentRunSubtypes.Over,
                Diagnostic = "the run was disposed while the turn was open.",
                Failure = new AgentFailure(AgentFailureKind.Fatal, "run-disposed"),
            }, watch);

    /// <summary>A run that never started: the refusal or fault that kept it from opening, as the run's own end.</summary>
    internal class OverRun(SessionEnded refusal) : IAgentRun
    {
        protected SessionEnded Refusal { get; } = refusal;

        public string? SessionId => null;

        public Task<SessionEnded> Ended { get; } = Task.FromResult(refusal);

        public IAsyncEnumerable<AgentRunTurn> WokenTurns { get; } = None();

        private static IAsyncEnumerable<AgentRunTurn> None()
        {
            var none = Channel.CreateBounded<AgentRunTurn>(1);
            none.Writer.TryComplete();
            return none.Reader.ReadAllAsync();
        }

        public Task<AgentRunTurn> SendAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult(RefusedTurn(Refusal with { Subtype = AgentRunSubtypes.Over, Diagnostic = "the run is over: " + Refusal.Diagnostic }));

        public Task<bool> StopAsync(CancellationToken ct = default) => Task.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
