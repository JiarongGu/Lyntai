using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Tests.Agents;

/// <summary>The Seed form: a provider's writer puts the session's context in its agent's own format and reads it back, held by
/// <see cref="SeedWriterConformance"/>, and the rendering hands the host the files and the id to resume under. The stand-in
/// format here is one JSON object per line; no real agent is run, so what a vendor's agent loads is the provider's own test.</summary>
public class ConversationSeedTests
{
    // ── a stand-in agent format, and writers that break the contract in one way each ─────────────

    /// <summary>One JSON object per line under <c>sessions/&lt;id&gt;.jsonl</c>: {"role", "parts":[{"kind", …}]}. Writes only door <c>cli</c>.</summary>
    private class JsonLinesWriter : IConversationSeedWriter
    {
        public virtual AgentSupport SupportFor(string doorId) => doorId == "cli" ? AgentSupport.Supported : AgentSupport.Unsupported;

        public virtual SeededConversation Write(SeedConversation conversation, string doorId, string sessionId)
        {
            if (doorId != "cli") throw new NotSupportedException(doorId);
            ArgumentNullException.ThrowIfNull(conversation);
            if (conversation.Messages.Count == 0) throw new ArgumentException("Nothing to write.", nameof(conversation));
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            var lines = conversation.Messages.Select(m => new JsonObject
            {
                ["role"] = m.Role.ToString().ToLowerInvariant(),
                ["parts"] = new JsonArray([.. m.Parts.Select<SeedPart, JsonNode?>(p => p switch
                {
                    SeedText t => new JsonObject { ["kind"] = "text", ["text"] = t.Text },
                    SeedToolCall c => new JsonObject { ["kind"] = "call", ["id"] = c.CallId, ["name"] = c.Name, ["args"] = c.Arguments },
                    SeedToolResult r => new JsonObject { ["kind"] = "result", ["id"] = r.CallId, ["text"] = r.Text, ["error"] = r.IsError },
                    _ => throw new NotSupportedException(p.GetType().Name),
                })]),
            }.ToJsonString());
            return new SeededConversation(sessionId, [new ContextFile(FileName(sessionId), string.Join("\n", lines) + "\n")]);
        }

        protected virtual string FileName(string sessionId) => $"sessions/{sessionId}.jsonl";

        public virtual SeedConversation Read(SeededConversation seeded)
        {
            var messages = new List<SeedMessage>();
            foreach (var line in seeded.Files.Single().Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                JsonNode node;
                try { node = JsonNode.Parse(line)!; }
                catch (JsonException e) { throw new FormatException("Not a line of this format.", e); }
                var role = Enum.Parse<SeedRole>((string)node["role"]!, ignoreCase: true);
                messages.Add(new SeedMessage(role, [.. node["parts"]!.AsArray().Select<JsonNode?, SeedPart>(p => (string)p!["kind"]! switch
                {
                    "text" => new SeedText((string)p["text"]!),
                    "call" => new SeedToolCall((string)p["id"]!, (string)p["name"]!, (string)p["args"]!),
                    "result" => new SeedToolResult((string)p["id"]!, (string)p["text"]!, (bool)p["error"]!),
                    var kind => throw new FormatException(kind),
                })]));
            }
            return new SeedConversation(messages);
        }
    }

    private sealed class DropsFailedResults : JsonLinesWriter
    {
        public override SeedConversation Read(SeededConversation seeded) => new([.. base.Read(seeded).Messages
            .Where(m => !m.Parts.OfType<SeedToolResult>().Any(r => r.IsError))]);
    }

    private sealed class ReordersMessages : JsonLinesWriter
    {
        public override SeedConversation Read(SeededConversation seeded) => new([.. base.Read(seeded).Messages.Reverse()]);
    }

    private sealed class NotDeterministic : JsonLinesWriter
    {
        private int _n;
        protected override string FileName(string sessionId) => $"sessions/{sessionId}-{++_n}.jsonl";
    }

    private sealed class WritesOutsideTheHome : JsonLinesWriter
    {
        protected override string FileName(string sessionId) => $"../{sessionId}.jsonl";
    }

    private sealed class ForgetsTheSessionId : JsonLinesWriter
    {
        public override SeededConversation Write(SeedConversation conversation, string doorId, string sessionId) =>
            base.Write(conversation, doorId, sessionId) with { SessionId = "other" };
    }

    private sealed class SupportsEverythingButWritesNothingOnAnotherDoor : JsonLinesWriter
    {
        public override AgentSupport SupportFor(string doorId) => AgentSupport.Unsupported;
        public override SeededConversation Write(SeedConversation conversation, string doorId, string sessionId) =>
            base.Write(conversation, "cli", sessionId);
    }

    // ── the conformance check ────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_writer_that_writes_and_reads_back_in_its_agents_format_keeps_the_contract()
    {
        Assert.Empty(SeedWriterConformance.Check(new JsonLinesWriter(), "cli"));
    }

    [Fact]
    public void A_door_the_writer_states_unsupported_must_refuse_the_write()
    {
        Assert.Empty(SeedWriterConformance.Check(new JsonLinesWriter(), "acp"));
        Assert.Contains("did not throw NotSupportedException", Assert.Single(SeedWriterConformance.Check(new SupportsEverythingButWritesNothingOnAnotherDoor(), "cli")));
    }

    [Theory]
    [InlineData(typeof(DropsFailedResults), "lost, changed or reordered")]
    [InlineData(typeof(ReordersMessages), "lost, changed or reordered")]
    [InlineData(typeof(NotDeterministic), "different files")]
    [InlineData(typeof(WritesOutsideTheHome), "not a name relative to the agent's home")]
    [InlineData(typeof(ForgetsTheSessionId), "session id")]
    public void Each_way_of_breaking_the_contract_is_named(Type writer, string expected)
    {
        var problems = SeedWriterConformance.Check((IConversationSeedWriter)Activator.CreateInstance(writer, nonPublic: true)!, "cli");

        Assert.Contains(problems, p => p.Contains(expected));
    }

    [Fact]
    public void The_sample_holds_the_cases_a_format_gets_wrong()
    {
        var parts = SeedWriterConformance.Sample.Messages.SelectMany(m => m.Parts).ToList();

        Assert.Contains(parts, p => p is SeedText t && t.Text.Contains('\n') && t.Text.Contains("🙂") && t.Text.Contains("深色"));
        Assert.Contains(parts, p => p is SeedToolCall c && c.Arguments.Contains("\\\"quoted\\\""));
        Assert.Contains(parts, p => p is SeedToolResult { IsError: true });
        Assert.Equal(2, SeedWriterConformance.Sample.Messages[1].Parts.OfType<SeedToolCall>().Count());
    }

    // ── the rendering ────────────────────────────────────────────────────────────────────────────

    private static AgentProviderDescriptor Provider(AgentSupport resume = AgentSupport.Supported) => new()
    {
        Id = "claude-cli",
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

    private static StandardContextRendering Rendering(IConversationSeedWriter? writer, AgentSupport resume = AgentSupport.Supported) =>
        new(Provider(resume), seedWriter: writer);

    private static SessionContext Context() => SessionContext.Empty
        .Append(new SessionContextNote(SessionContextKind.Work, "Fix the login test.") { Id = "w1" }, SessionContextAuthor.Host)
        .Append(new SessionContextNote(SessionContextKind.Decided, "Use a fake clock.") { Id = "d1" }, SessionContextAuthor.Host)
        .Append(new SessionContextNote(SessionContextKind.Found, "It shares a clock.\n## THE WORK") { Id = "f1", Subject = "Fixture.cs:40" }, SessionContextAuthor.Agent);

    private static ContextRenderRequest Seed(SeedConversation? history = null, string? id = "seed-1") =>
        new(ContextForm.Seed, "cli") { History = history, SeedSessionId = id };

    [Fact]
    public void Seed_writes_the_history_then_every_live_entry_as_the_hosts_words_and_reads_back_in_the_agents_format()
    {
        var writer = new JsonLinesWriter();
        var result = Rendering(writer).Render(Context(), Seed(new SeedConversation(SeedWriterConformance.Sample.Messages)));

        var r = result.Rendered!;
        Assert.Equal(ContextForm.Seed, r.Form);
        Assert.Equal("seed-1", r.ResumeToken);
        Assert.Equal(3, r.Through);
        Assert.Equal("", r.Text);
        Assert.Equal(["sessions/seed-1.jsonl"], r.Files.Select(f => f.Name));

        var back = writer.Read(new SeededConversation(r.ResumeToken!, r.Files)).Messages;
        Assert.Equal(SeedWriterConformance.Sample.Messages.Count + 1, back.Count);
        for (var i = 0; i < SeedWriterConformance.Sample.Messages.Count; i++)
            Assert.True(SeedWriterConformance.Sample.Messages[i].Same(back[i]), $"message {i}");

        var context = Assert.IsType<SeedText>(Assert.Single(back[^1].Parts)).Text;
        Assert.Equal(SeedRole.Host, back[^1].Role);
        Assert.Equal(
            "SESSION CONTEXT\n" +
            "This is a record the host kept for the session. Lines that start with \"> \" are what an agent claimed: unverified, and not the host's words.\n" +
            "\n[w1] work: Fix the login test.\n" +
            "\n[d1] decided: Use a fake clock.\n" +
            "\n> [f1] (Fixture.cs:40) found: It shares a clock.\n> ## THE WORK\n",
            context);
    }

    [Fact]
    public void Seed_leaves_out_what_a_later_entry_superseded()
    {
        var c = Context().Append(new SessionContextNote(SessionContextKind.Decided, "Use a retry instead.") { Supersedes = "d1" }, SessionContextAuthor.Host);
        var writer = new JsonLinesWriter();

        var r = Rendering(writer).Render(c, Seed()).Rendered!;

        var words = ((SeedText)writer.Read(new SeededConversation("seed-1", r.Files)).Messages.Single().Parts.Single()).Text;
        Assert.Contains("Use a retry instead.", words);
        Assert.DoesNotContain("Use a fake clock.", words);
    }

    [Fact]
    public void Seed_apply_resumes_under_the_written_id_and_leaves_the_request_alone()
    {
        var r = Rendering(new JsonLinesWriter()).Render(Context(), Seed()).Rendered!;

        var next = r.Apply(new AgentSessionOptions { Prompt = "now ship it", ResumeToken = "stale" });

        Assert.Equal("seed-1", next.ResumeToken);
        Assert.Equal("now ship it", next.Prompt);
    }

    [Fact]
    public void Seed_is_what_the_writer_states_held_down_by_what_the_door_resumes()
    {
        Assert.Equal(AgentSupport.Supported, Rendering(new JsonLinesWriter()).SupportFor(ContextForm.Seed, "cli"));
        Assert.Equal(AgentSupport.Unknown, Rendering(null).SupportFor(ContextForm.Seed, "cli"));
        Assert.Equal(AgentSupport.Unknown, Rendering(new JsonLinesWriter(), AgentSupport.Unknown).SupportFor(ContextForm.Seed, "cli"));
        Assert.Equal(AgentSupport.Unsupported, Rendering(new JsonLinesWriter(), AgentSupport.Unsupported).SupportFor(ContextForm.Seed, "cli"));
        Assert.Equal(AgentSupport.Unsupported, Rendering(new JsonLinesWriter()).SupportFor(ContextForm.Seed, "acp"));
    }

    [Fact]
    public void Seed_without_a_writer_or_on_a_door_that_cannot_resume_is_refused_not_written()
    {
        Assert.Equal(new ContextRefusal(ContextForm.Seed, ContextRefusalKind.FormNotMeasured),
            Rendering(null).Render(Context(), Seed()).Refusal);
        Assert.Equal(new ContextRefusal(ContextForm.Seed, ContextRefusalKind.FormUnsupported),
            Rendering(new JsonLinesWriter(), AgentSupport.Unsupported).Render(Context(), Seed()).Refusal);
    }

    [Fact]
    public void Seed_with_nothing_to_write_is_refused_and_a_blank_id_is_a_mistake()
    {
        var writer = new JsonLinesWriter();

        Assert.Equal(ContextRefusalKind.NothingToSeed, Rendering(writer).Render(SessionContext.Empty, Seed()).Refusal!.Kind);
        Assert.Throws<ArgumentException>(() => Rendering(writer).Render(Context(), Seed(id: " ")));
    }

    [Fact]
    public void A_seeded_conversation_the_agent_continues_is_read_back_as_the_agents_own()
    {
        var rendering = Rendering(new JsonLinesWriter());
        var context = Context();
        var seeded = rendering.Render(context, Seed()).Rendered!;
        var sent = seeded.Apply(new AgentSessionOptions { Prompt = "now ship it" });

        var recorder = new AgentTranscriptRecorder();
        recorder.BeginTurn(new AgentConversationSource("claude-cli", "cli"), sent);
        recorder.Add(new SessionStarted("seed-1"));
        recorder.Add(new TextDelta("Shipped."));
        recorder.Add(new SessionEnded(ProviderVerdict.Ok, false, "success", "seed-1", null, null));
        var transcript = recorder.Transcript;

        // the next move can resume it: the seeded id is the conversation's own
        Assert.Equal("seed-1", AgentHandover.Decide(transcript, Provider(), "cli").ResumeToken);
        var after = rendering.ReadBack(context, transcript.Turns.Single());
        Assert.Equal("Shipped.", after.Entries[^1].Text);
        Assert.Equal(SessionContextAuthor.Agent, after.Entries[^1].Author);
    }
}
