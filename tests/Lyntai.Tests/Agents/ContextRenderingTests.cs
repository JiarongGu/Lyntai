using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Tests.Agents;

/// <summary>The rendering port: what a provider states per form, the Resume form (the conversation's own, plus the host's
/// entries since) and the Prompt form (a bounded head plus files) under <see cref="AgentHandover"/>'s judgement, the Seed form
/// refused where no writer stands behind it (the written one is <c>ConversationSeedTests</c>), and the read-back that maps a turn to the agent's entries and never reads the host's composed words.
/// Pure; nothing is spawned.</summary>
public class ContextRenderingTests
{
    private static readonly AgentConversationSource Claude = new("claude-cli", "cli");

    private static AgentProviderDescriptor Provider(string id = "claude-cli", AgentSupport resume = AgentSupport.Supported) => new()
    {
        Id = id,
        Doors =
        [
            new AgentDoor
            {
                Id = "cli", Kind = "cli",
                Abilities = [.. AgentAbilities.All.Select(name => new AgentAbility(
                    name, name == AgentAbilities.Resume ? resume : AgentSupport.Unknown, AgentAbilitySource.Declared))],
            },
        ],
    };

    private static StandardContextRendering Rendering(AgentSupport resume = AgentSupport.Supported, int budget = 6000) =>
        new(Provider(resume: resume), new ContextRenderingOptions { PromptBudget = budget });

    private static AgentTranscript Ran(string provider = "claude-cli", string? sessionId = "s-1") =>
        new([
            new TranscriptTurn
            {
                Source = new AgentConversationSource(provider, "cli"), Prompt = "go", SessionId = sessionId, Verdict = ProviderVerdict.Ok,
            },
        ]);

    private static SessionContext Add(SessionContext c, SessionContextKind kind, string text, string id,
        SessionContextAuthor author = SessionContextAuthor.Host, string? supersedes = null) =>
        c.Append(new SessionContextNote(kind, text) { Id = id, Supersedes = supersedes }, author);

    private static SessionContext Told()
    {
        var c = Add(SessionContext.Empty, SessionContextKind.Work, "Fix the login test.", "w1");
        c = Add(c, SessionContextKind.Decided, "Use a retry.", "d1");
        return c;
    }

    // ── abilities ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AgentSupport.Supported)]
    [InlineData(AgentSupport.Unsupported)]
    [InlineData(AgentSupport.Unknown)]
    public void Resume_is_what_the_door_states_and_prompt_is_every_doors_floor(AgentSupport resume)
    {
        var r = Rendering(resume);

        Assert.Equal(resume, r.SupportFor(ContextForm.Resume, "cli"));
        Assert.Equal(AgentSupport.Supported, r.SupportFor(ContextForm.Prompt, "cli"));
        Assert.Equal(AgentSupport.Unknown, r.SupportFor(ContextForm.Seed, "cli"));
        Assert.Equal(AgentSupport.Unsupported, r.SupportFor(ContextForm.Prompt, "acp"));
    }

    [Fact]
    public void A_budget_under_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rendering(budget: 0));
    }

    // ── Prompt ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_is_the_head_under_the_providers_budget_with_the_files_it_points_to()
    {
        var c = Told();
        for (var i = 1; i <= 6; i++) c = Add(c, SessionContextKind.Found, $"finding {i} " + new string('x', 80), $"f{i}");
        var full = Rendering().Render(c, new ContextRenderRequest(ContextForm.Prompt, "cli")).Rendered!;
        var small = Rendering(budget: full.Characters - 200).Render(c, new ContextRenderRequest(ContextForm.Prompt, "cli")).Rendered!;

        Assert.True(full.Omitted.IsNone);
        Assert.True(small.WithinBound);
        Assert.True(small.Characters <= full.Characters - 200);
        Assert.True(small.Omitted.Found > 0);
        Assert.Equal(ContextForm.Prompt, small.Form);
        Assert.Equal(c.Count, small.Through);
        Assert.Null(small.ResumeToken);
        // every finding is in the file though the head dropped some
        var found = small.Files.Single(f => f.Name == "found.md").Text;
        for (var i = 1; i <= 6; i++) Assert.Contains($"[f{i}] finding {i} ", found);
    }

    [Fact]
    public void Prompt_clears_the_token_and_leads_the_prompt_with_the_head_then_the_request_after_the_marker()
    {
        var rendered = Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Prompt, "cli")).Rendered!;

        var next = rendered.Apply(new AgentSessionOptions { Prompt = "now ship it", ResumeToken = "stale" });

        Assert.Null(next.ResumeToken);
        Assert.Equal(rendered.Text + "\nNEW REQUEST FROM THE HOST\nnow ship it", next.Prompt);
    }

    [Fact]
    public void Prompt_on_a_door_the_provider_does_not_have_is_refused()
    {
        var result = Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Prompt, "acp"));

        Assert.Null(result.Rendered);
        Assert.Equal(new ContextRefusal(ContextForm.Prompt, ContextRefusalKind.FormUnsupported), result.Refusal);
    }

    // ── Resume ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resume_hands_the_conversations_token_and_only_the_hosts_entries_since_it_was_told()
    {
        var c = Told();                                  // told 2 entries
        var seen = c.Count;
        c = Add(c, SessionContextKind.Standing, "I fixed the race.", "s1", SessionContextAuthor.Agent);   // the conversation's own
        c = Add(c, SessionContextKind.Decided, "Fix the race instead.", "d2", supersedes: "d1");
        c = Add(c, SessionContextKind.Found, "CI runs on one core.", "f1");

        var result = Rendering().Render(c, new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(), Seen = seen });

        var r = result.Rendered!;
        Assert.Equal(ContextForm.Resume, r.Form);
        Assert.Equal("s-1", r.ResumeToken);
        Assert.Equal(c.Count, r.Through);
        Assert.Equal(
            "SESSION CONTEXT UPDATE\n" +
            "Since your last turn the host recorded the following, in order. An entry that says it replaces another makes that one stale.\n" +
            "\n[d2] decided, replaces d1: Fix the race instead.\n" +
            "\n[f1] found: CI runs on one core.\n",
            r.Text);
        Assert.DoesNotContain("I fixed the race", r.Text);   // its own words are not said back to it
        Assert.DoesNotContain("Fix the login test", r.Text); // what it already heard is not said again
        Assert.Empty(r.Files);
    }

    [Fact]
    public void Resume_apply_sets_the_token_and_leads_the_prompt_with_the_words()
    {
        var c = Add(Told(), SessionContextKind.Found, "CI runs on one core.", "f1");
        var r = Rendering().Render(c, new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(), Seen = 2 }).Rendered!;

        var next = r.Apply(new AgentSessionOptions { Prompt = "continue" });

        Assert.Equal("s-1", next.ResumeToken);
        Assert.Equal(r.Text + "\nNEW REQUEST FROM THE HOST\ncontinue", next.Prompt);
    }

    [Fact]
    public void Resume_with_nothing_new_hands_no_words_and_leaves_the_prompt_alone()
    {
        var c = Told();
        var r = Rendering().Render(c, new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(), Seen = c.Count }).Rendered!;

        Assert.Equal("", r.Text);
        Assert.Equal(0, r.Characters);
        Assert.Equal("continue", r.Apply(new AgentSessionOptions { Prompt = "continue" }).Prompt);
        Assert.Equal("s-1", r.Apply(new AgentSessionOptions { Prompt = "continue" }).ResumeToken);
    }

    [Fact]
    public void A_re_wake_after_a_check_hands_only_the_verdict()
    {
        var c = Told();
        c = Add(c, SessionContextKind.Tree, "3 files changed.", "t1");
        var seen = c.Count;
        c = Add(c, SessionContextKind.Standing, "The check passed: 120 of 120.", "s1");

        var r = Rendering().Render(c, new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(), Seen = seen }).Rendered!;

        Assert.Contains("The check passed: 120 of 120.", r.Text);
        Assert.DoesNotContain("3 files changed", r.Text);
        Assert.DoesNotContain("Use a retry", r.Text);
    }

    [Theory]
    [InlineData("codex-cli", AgentHandoverReason.ProviderChanged)]
    public void Resume_over_another_providers_conversation_is_refused_with_the_handover_reason(string ranOn, AgentHandoverReason reason)
    {
        var result = Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(ranOn) });

        Assert.Null(result.Rendered);
        Assert.Equal(new ContextRefusal(ContextForm.Resume, ContextRefusalKind.ConversationNotResumable) { Handover = reason }, result.Refusal);
    }

    [Theory]
    [InlineData(AgentSupport.Unsupported, AgentHandoverReason.ResumeUnsupported)]
    [InlineData(AgentSupport.Unknown, AgentHandoverReason.ResumeNotMeasured)]
    public void Resume_on_a_door_that_does_not_state_it_is_refused(AgentSupport support, AgentHandoverReason reason)
    {
        var result = Rendering(support).Render(Told(), new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran() });

        Assert.Equal(reason, result.Refusal!.Handover);
    }

    [Fact]
    public void Resume_with_no_conversation_recorded_is_refused()
    {
        var result = Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Resume, "cli"));

        Assert.Equal(AgentHandoverReason.NoConversation, result.Refusal!.Handover);
    }

    [Fact]
    public void A_position_outside_the_context_is_refused_not_rendered()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(), Seen = 3 }));
        Assert.Throws<ArgumentException>(() => Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Prompt, " ")));
    }

    // ── Seed ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Seed_with_no_writer_behind_it_is_refused_as_not_measured()
    {
        var result = Rendering().Render(Told(), new ContextRenderRequest(ContextForm.Seed, "cli"));

        Assert.Null(result.Rendered);
        Assert.Equal(new ContextRefusal(ContextForm.Seed, ContextRefusalKind.FormNotMeasured), result.Refusal);
    }

    // ── read-back ────────────────────────────────────────────────────────────────────────────────

    private static TranscriptTurn Turn(string? message, params PlanEntry[] plan) => new()
    {
        Source = Claude, Prompt = "go", LastMessage = message, Plan = plan.Length == 0 ? null : plan, SessionId = "s-1", Verdict = ProviderVerdict.Ok,
    };

    [Fact]
    public void Read_back_adds_the_agents_last_words_and_plan_as_one_claimed_standing_entry()
    {
        var c = Rendering().ReadBack(Told(), Turn("Found the race.", new PlanEntry("Fix it", PlanEntryStatus.InProgress), new PlanEntry("Add a test", PlanEntryStatus.Pending)));

        var e = c.Entries[^1];
        Assert.Equal((SessionContextKind.Standing, SessionContextAuthor.Agent), (e.Kind, e.Author));
        Assert.Equal("Found the race.\n\nPlan:\n[~] Fix it\n[ ] Add a test", e.Text);
        Assert.Equal(3, c.Count);
    }

    [Fact]
    public void Each_turns_read_back_replaces_the_agents_earlier_one()
    {
        var r = Rendering();
        var c = r.ReadBack(Told(), Turn("Halfway."));
        c = r.ReadBack(c, Turn("Done."));

        var standing = c.Live.Where(e => e.Kind == SessionContextKind.Standing).Single();
        Assert.Equal("Done.", standing.Text);
        Assert.Equal(4, c.Count);                                   // nothing deleted
        Assert.Equal("Halfway.", c.Replaced.Single().Text);
    }

    [Fact]
    public void A_turn_that_said_nothing_and_kept_no_plan_adds_nothing()
    {
        var c = Told();

        Assert.Same(c, Rendering().ReadBack(c, Turn(null)));
        Assert.Same(c, Rendering().ReadBack(c, Turn("   \n")));
    }

    [Fact]
    public void Read_back_never_replaces_the_hosts_standing_entry()
    {
        var c = Add(Told(), SessionContextKind.Standing, "Host: waiting on CI.", "s0");

        c = Rendering().ReadBack(c, Turn("Working."));

        Assert.Equal(["s0", "e4"], c.Live.Where(e => e.Kind == SessionContextKind.Standing).Select(e => e.Id));
    }

    [Fact]
    public void The_hosts_composed_words_never_come_back_as_the_hosts_or_the_agents()
    {
        var context = Told();
        var asked = new AgentSessionOptions { Prompt = "now ship it" };
        var head = Rendering().Render(context, new ContextRenderRequest(ContextForm.Prompt, "cli")).Rendered!;
        var update = Rendering().Render(Add(context, SessionContextKind.Found, "CI runs on one core.", "f1"),
            new ContextRenderRequest(ContextForm.Resume, "cli") { Transcript = Ran(), Seen = 2 }).Rendered!;

        foreach (var sent in new[] { head.Apply(asked), update.Apply(asked) })
        {
            var recorder = new AgentTranscriptRecorder();
            recorder.BeginTurn(Claude, sent);
            recorder.Add(new TextDelta("Shipped."));
            recorder.Add(new SessionEnded(ProviderVerdict.Ok, false, "success", "s-2", null, null));

            var turn = recorder.Transcript.Turns.Single();
            Assert.Equal("now ship it", turn.Prompt);
            var read = Rendering().ReadBack(context, turn);
            Assert.All(read.Since(context.Count), e => Assert.Equal(SessionContextAuthor.Agent, e.Author));
            Assert.DoesNotContain("SESSION CONTEXT", string.Join("\n", read.Entries.Select(e => e.Text)));
        }
    }

    [Fact]
    public void A_prompt_that_opens_like_a_context_but_has_no_marker_is_the_hosts_and_kept_whole()
    {
        var recorder = new AgentTranscriptRecorder();
        recorder.BeginTurn(Claude, new AgentSessionOptions { Prompt = "SESSION CONTEXT\nthe host wrote this itself" });

        Assert.Equal("SESSION CONTEXT\nthe host wrote this itself", recorder.Transcript.Turns.Single().Prompt);
    }
}
