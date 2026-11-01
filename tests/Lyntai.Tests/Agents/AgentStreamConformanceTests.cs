using Lyntai.Agents;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace Lyntai.Tests.Agents;

/// <summary>The stream rules every provider keeps, held by one theory over the same registry as
/// <see cref="AgentProviderConformanceTests"/>: every (provider, door) it holds against every <see cref="StreamCase"/>,
/// at zero tokens. The rules are <see cref="StreamRules"/>; what varies between doors is only the
/// <see cref="DoorScript"/> that says a case in its own words, so a host's provider is held to them by registering and
/// adding its script, and no rule is edited. The captures in the scripts are SOURCE-DERIVED: the lines the doors' own
/// tests already pin, never recorded from a turn run for this theory.</summary>
public class AgentStreamConformanceTests
{
    /// <summary>Every (provider, door, case) — the theory's one source.</summary>
    public static TheoryData<string, string, StreamCase> Turns()
    {
        var data = new TheoryData<string, string, StreamCase>();
        foreach (var turn in AgentProviderConformanceTests.Doors().Select(d => (Provider: (string)d[0], Door: (string)d[1])))
            foreach (var kind in Enum.GetValues<StreamCase>())
                data.Add(turn.Provider, turn.Door, kind);
        return data;
    }

    private static ServiceProvider Compose(TurnScript turn) =>
        AgentProviderConformanceTests.Compose(turn.Runner, turn.Http, StreamRules.LimitTool);

    [Theory, MemberData(nameof(Turns))]
    public Task Each_door_keeps_the_stream_rules(string providerId, string doorId, StreamCase turn) =>
        StreamRules.Hold(Compose, providerId, doorId, DoorScripts.For(providerId), turn);

    [Fact]
    public void Every_registered_provider_has_a_script_and_every_case_is_in_the_theory()
    {
        var providers = AgentProviderConformanceTests.Doors().Select(d => (string)d[0]).Distinct().ToList();

        Assert.All(providers, id => Assert.NotNull(DoorScripts.For(id)));
        Assert.Equal(providers.Count * Enum.GetValues<StreamCase>().Length, Turns().Count());
    }

    // ── the rules bite: each case, shown failing against a door that breaks it ─

    private static ServiceProvider ComposeStandIn(TurnScript turn, StandInFlaw flaw, string id)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddAgentProvider(_ => DialectStandIn.Provider(turn.Runner, flaw, id)));
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(StandInFlaw.SecondTerminal, StreamCase.SecondEnding)]
    [InlineData(StandInFlaw.NoFailure, StreamCase.FailedTerminal)]
    [InlineData(StandInFlaw.NoFailure, StreamCase.DiesAfterAnswer)]
    [InlineData(StandInFlaw.AlwaysTaken, StreamCase.DiesBeforeAnyEvent)]
    [InlineData(StandInFlaw.AlwaysTaken, StreamCase.DiesAfterToolCall)]
    [InlineData(StandInFlaw.ReadsToolText, StreamCase.ToolPrintsLimit)]
    [InlineData(StandInFlaw.ReadsToolText, StreamCase.ToolPrintsLimitThenDies)]
    [InlineData(StandInFlaw.TerminalOnCancel, StreamCase.Cancelled)]
    public async Task A_door_that_breaks_a_rule_fails_the_case_that_holds_it(StandInFlaw flaw, StreamCase turn)
    {
        const string host = "host-own-agent";

        // the same door without the flaw passes, under an id the registry has never seen
        await StreamRules.Hold(t => ComposeStandIn(t, StandInFlaw.None, host), host, "local", DoorScripts.StandIn, turn);

        await Assert.ThrowsAnyAsync<XunitException>(() =>
            StreamRules.Hold(t => ComposeStandIn(t, flaw, host), host, "local", DoorScripts.StandIn, turn));
    }

    [Fact]
    public void A_provider_with_no_script_is_a_failure_and_not_a_pass()
    {
        var ex = Assert.Throws<XunitException>(() => DoorScripts.For("gemini-cli"));

        Assert.Contains("gemini-cli", ex.Message);
    }
}
