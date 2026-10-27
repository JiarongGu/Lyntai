using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>The hand-over decision over every descriptor case: resume only when a session id was recorded, the
/// provider, door and account are the conversation's own, and the door states resume supported — otherwise a
/// fresh session handed the brief. Pure; nothing is spawned.</summary>
public class AgentHandoverTests
{
    private static AgentProviderDescriptor Provider(string id, AgentSupport resume, string door = "cli") => new()
    {
        Id = id,
        Doors =
        [
            new AgentDoor
            {
                Id = door,
                Kind = "cli",
                Abilities = [.. AgentAbilities.All.Select(name => new AgentAbility(
                    name, name == AgentAbilities.Resume ? resume : AgentSupport.Unknown, AgentAbilitySource.Declared))],
            },
        ],
    };

    private static AgentTranscript Ran(string provider = "claude-cli", string door = "cli", string? account = null,
        string? sessionId = "s-1") =>
        new([
            new TranscriptTurn
            {
                Source = new AgentConversationSource(provider, door, account), Prompt = "build it", SessionId = sessionId,
                Verdict = ProviderVerdict.Ok,
            },
        ]);

    [Fact]
    public void The_same_provider_door_and_account_with_resume_supported_resumes_with_the_recorded_token()
    {
        var decision = AgentHandover.Decide(Ran(), Provider("claude-cli", AgentSupport.Supported), "cli");

        Assert.Equal(new AgentHandoverDecision(AgentHandoverKind.Resume, AgentHandoverReason.Allowed, "s-1"), decision);
    }

    [Fact]
    public void The_same_named_account_resumes_and_a_different_one_does_not()
    {
        var target = Provider("claude-cli", AgentSupport.Supported);

        Assert.Equal(AgentHandoverKind.Resume, AgentHandover.Decide(Ran(account: "work"), target, "cli", "work").Kind);
        Assert.Equal(AgentHandoverReason.AccountChanged, AgentHandover.Decide(Ran(account: "work"), target, "cli", "home").Reason);
        Assert.Equal(AgentHandoverReason.AccountChanged, AgentHandover.Decide(Ran(account: "work"), target, "cli").Reason);
        Assert.Equal(AgentHandoverReason.AccountChanged, AgentHandover.Decide(Ran(), target, "cli", "work").Reason);
        Assert.Equal(AgentHandoverReason.AccountChanged, AgentHandover.Decide(Ran(account: "Work"), target, "cli", "work").Reason);
    }

    [Theory]
    [InlineData(AgentSupport.Unsupported, AgentHandoverReason.ResumeUnsupported)]
    [InlineData(AgentSupport.Unknown, AgentHandoverReason.ResumeNotMeasured)]
    public void A_door_that_does_not_state_resume_supported_gets_a_fresh_session(AgentSupport support, AgentHandoverReason reason)
    {
        var decision = AgentHandover.Decide(Ran(), Provider("claude-cli", support), "cli");

        Assert.Equal(new AgentHandoverDecision(AgentHandoverKind.FreshWithBrief, reason, null), decision);
    }

    [Theory]
    [InlineData("codex-cli", "cli", AgentHandoverReason.ProviderChanged)]
    [InlineData("claude-cli", "acp", AgentHandoverReason.DoorChanged)]
    public void Another_provider_or_door_gets_a_fresh_session(string provider, string door, AgentHandoverReason reason)
    {
        var target = Provider(provider, AgentSupport.Supported, door);

        var decision = AgentHandover.Decide(Ran(), target, door);

        Assert.Equal(AgentHandoverKind.FreshWithBrief, decision.Kind);
        Assert.Equal(reason, decision.Reason);
        Assert.Null(decision.ResumeToken);
    }

    [Fact]
    public void A_door_the_provider_does_not_have_gets_a_fresh_session()
    {
        var decision = AgentHandover.Decide(Ran(), Provider("claude-cli", AgentSupport.Supported, door: "other"), "cli");

        Assert.Equal(AgentHandoverReason.NoSuchDoor, decision.Reason);
        Assert.Equal(AgentHandoverKind.FreshWithBrief, decision.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_conversation_with_no_session_id_has_nothing_to_resume(string? sessionId)
    {
        var decision = AgentHandover.Decide(Ran(sessionId: sessionId), Provider("claude-cli", AgentSupport.Supported), "cli");

        Assert.Equal(AgentHandoverReason.NoConversation, decision.Reason);
        Assert.Equal(AgentHandoverKind.FreshWithBrief, decision.Kind);
        Assert.Equal(AgentHandoverReason.NoConversation,
            AgentHandover.Decide(AgentTranscript.Empty, Provider("claude-cli", AgentSupport.Supported), "cli").Reason);
    }

    [Fact]
    public void The_conversation_is_the_latest_turn_that_gave_a_session_id()
    {
        var src = new AgentConversationSource("claude-cli", "cli");
        var transcript = new AgentTranscript([
            new TranscriptTurn { Source = src, Prompt = "a", SessionId = "s-old" },
            new TranscriptTurn { Source = src, Prompt = "b", SessionId = "s-new" },
            new TranscriptTurn { Source = src, Prompt = "c", Verdict = ProviderVerdict.RateLimited },
        ]);

        var decision = AgentHandover.Decide(transcript, Provider("claude-cli", AgentSupport.Supported), "cli");

        Assert.Equal("s-new", decision.ResumeToken);
    }

    // ── over the registered providers: one source, the registry ─────────────────────────────────────

    public static TheoryData<string, string> Doors()
    {
        using var sp = Compose();
        var data = new TheoryData<string, string>();
        foreach (var provider in sp.GetRequiredService<AgentProviderRegistry>().All)
            foreach (var door in provider.Describe().Doors) data.Add(provider.Describe().Id, door.Id);
        return data;
    }

    private static ServiceProvider Compose()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner>(new FakeProcessRunner());
        services.AddLyntai(b => b.AddClaudeCliAgentSession(command: "claude").AddCodexCliAgentSession(command: "codex"));
        return services.BuildServiceProvider();
    }

    [Theory]
    [MemberData(nameof(Doors))]
    public void Each_registered_door_resumes_exactly_when_it_states_resume_supported(string providerId, string doorId)
    {
        using var sp = Compose();
        var descriptor = sp.GetRequiredService<AgentProviderRegistry>().Get(providerId).Describe();
        var transcript = Ran(providerId, doorId);

        var decision = AgentHandover.Decide(transcript, descriptor, doorId);

        var supported = descriptor.Doors.Single(d => d.Id == doorId).SupportFor(AgentAbilities.Resume) == AgentSupport.Supported;
        Assert.Equal(supported ? AgentHandoverKind.Resume : AgentHandoverKind.FreshWithBrief, decision.Kind);
        Assert.Equal(supported ? "s-1" : null, decision.ResumeToken);
    }

    // ── applying it ─────────────────────────────────────────────────────────────────────────────────

    private sealed record DoorOptions : AgentSessionOptions { public string? Extra { get; init; } }

    [Fact]
    public void Resuming_sets_the_token_and_leaves_the_prompt_and_the_options_type_alone()
    {
        var decision = new AgentHandoverDecision(AgentHandoverKind.Resume, AgentHandoverReason.Allowed, "s-1");
        var next = new DoorOptions { Prompt = "go on", Extra = "keep" };

        var applied = decision.Apply(next, brief: null);

        var door = Assert.IsType<DoorOptions>(applied);
        Assert.Equal("go on", door.Prompt);
        Assert.Equal("s-1", door.ResumeToken);
        Assert.Equal("keep", door.Extra);
    }

    [Fact]
    public void A_fresh_session_clears_the_token_and_leads_its_prompt_with_the_brief()
    {
        var decision = new AgentHandoverDecision(AgentHandoverKind.FreshWithBrief, AgentHandoverReason.ProviderChanged, null);
        var brief = new AgentBriefComposer().Brief(Ran());
        var next = new DoorOptions { Prompt = "go on", ResumeToken = "stale", Extra = "keep" };

        var applied = decision.Apply(next, brief);

        var door = Assert.IsType<DoorOptions>(applied);
        Assert.Null(door.ResumeToken);
        Assert.Equal("keep", door.Extra);
        Assert.StartsWith(brief.Text, door.Prompt);
        Assert.EndsWith("\nNEW REQUEST FROM THE HOST\ngo on", door.Prompt);
        Assert.Throws<ArgumentNullException>(() => decision.Apply(next, brief: null));
    }
}
