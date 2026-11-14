namespace Lyntai.Agents;

/// <summary>Who said a <see cref="SeedMessage"/>.</summary>
public enum SeedRole
{
    /// <summary>The host: its words, and the session context's entries as its words.</summary>
    Host,

    /// <summary>The agent: its words and the tool calls it made.</summary>
    Agent,

    /// <summary>A tool: the result of a call the agent made.</summary>
    Tool,
}

/// <summary>One part of a <see cref="SeedMessage"/>: text, a tool call or a tool result.</summary>
public abstract record SeedPart;

/// <summary>Words.</summary>
/// <param name="Text">The words, verbatim.</param>
public sealed record SeedText(string Text) : SeedPart;

/// <summary>A tool call the agent made.</summary>
/// <param name="CallId">The call's id, which its <see cref="SeedToolResult"/> names.</param>
/// <param name="Name">The tool's name as the agent called it.</param>
/// <param name="Arguments">The call's arguments, as JSON text.</param>
public sealed record SeedToolCall(string CallId, string Name, string Arguments) : SeedPart;

/// <summary>What a tool call came back with.</summary>
/// <param name="CallId">The <see cref="SeedToolCall.CallId"/> it answers.</param>
/// <param name="Text">The result, verbatim.</param>
/// <param name="IsError">True when the call came back as an error.</param>
public sealed record SeedToolResult(string CallId, string Text, bool IsError = false) : SeedPart;

/// <summary>One message of a conversation in no agent's format: a speaker and what was said or done.</summary>
/// <param name="Role">Who.</param>
/// <param name="Parts">What, in order; at least one.</param>
public sealed record SeedMessage(SeedRole Role, IReadOnlyList<SeedPart> Parts)
{
    /// <summary>The host's words.</summary>
    public static SeedMessage Host(string text) => new(SeedRole.Host, [new SeedText(text)]);

    /// <summary>The agent's words.</summary>
    public static SeedMessage Agent(string text) => new(SeedRole.Agent, [new SeedText(text)]);

    /// <summary>The same message as <paramref name="other"/>: the role and every part equal in value and order.</summary>
    public bool Same(SeedMessage? other) =>
        other is not null && Role == other.Role && Parts.Count == other.Parts.Count
        && Parts.Zip(other.Parts).All(p => p.First.Equals(p.Second));
}

/// <summary>A conversation in no agent's format: what a provider's <see cref="IConversationSeedWriter"/> writes in its own
/// agent's. Its turns, tool calls and results come from whatever the host holds; the library only carries them.</summary>
/// <param name="Messages">The messages, oldest first.</param>
public sealed record SeedConversation(IReadOnlyList<SeedMessage> Messages)
{
    /// <summary>A conversation with no messages.</summary>
    public static SeedConversation Empty { get; } = new([]);
}

/// <summary>A conversation written in an agent's own format.</summary>
/// <param name="SessionId">The id the agent resumes it under (<see cref="AgentSessionOptions.ResumeToken"/>).</param>
/// <param name="Files">The files of the agent's format that hold it, each named relative to the agent's own home (the folder
/// <see cref="AgentAccountMapping.HomeVariable"/> names, or the host's choice where the provider reads none). The library
/// places nothing; the host writes them and then resumes under <paramref name="SessionId"/>.</param>
public sealed record SeededConversation(string SessionId, IReadOnlyList<ContextFile> Files);

/// <summary>What a provider implements to write a conversation in its agent's own format, so the agent resumes and goes on
/// with it as its own even where it did not make the turns. <b>The contract</b> (held by
/// <see cref="SeedWriterConformance"/>, which the provider's own test calls):
/// <list type="bullet">
/// <item><see cref="Write"/> is a pure function of its arguments: the same input gives the same files.</item>
/// <item>The files are named relative to the agent's home — no rooted path, no <c>..</c> segment, no drive or colon — each name once, each text
/// non-empty; the result names the session id it was given.</item>
/// <item><see cref="Read"/> returns, from the files alone, a conversation holding every message written, in order and equal in
/// value. A format that cannot say a thing (it must alternate speakers, say) adds what it needs and states so in its own
/// documentation; it never drops, reorders or rewrites a message.</item>
/// <item>A door whose agent's format will not take a conversation in states <see cref="AgentSupport.Unsupported"/>, and
/// <see cref="Write"/> then throws <see cref="NotSupportedException"/>. A claim of <see cref="AgentSupport.Supported"/> is held by a
/// test that resumes a conversation written this way on the real agent; the library cannot run one.</item>
/// </list></summary>
public interface IConversationSeedWriter
{
    /// <summary>Whether the agent behind <paramref name="doorId"/> can be handed a conversation in its format.
    /// <see cref="AgentSupport.Unknown"/> until one was measured to load.</summary>
    AgentSupport SupportFor(string doorId);

    /// <summary>Write <paramref name="conversation"/> in the agent's format under <paramref name="sessionId"/>.</summary>
    /// <exception cref="ArgumentException">The conversation has no message, or the session id is blank.</exception>
    /// <exception cref="NotSupportedException">The door's agent takes no conversation in (<see cref="SupportFor"/> is not
    /// <see cref="AgentSupport.Supported"/>).</exception>
    SeededConversation Write(SeedConversation conversation, string doorId, string sessionId);

    /// <summary>Read a conversation this writer wrote back from its files, in the neutral form.</summary>
    /// <exception cref="FormatException">The files are not this agent's format.</exception>
    SeedConversation Read(SeededConversation seeded);
}

/// <summary>Holds a provider's <see cref="IConversationSeedWriter"/> to its contract: a provider's own test calls
/// <see cref="Check"/> and asserts nothing is returned. It writes a conversation that has the cases a format gets wrong
/// (multi-line and non-Latin words, an emoji, a call whose arguments hold quotes and a line break, a failed result, two calls in
/// one message) and reads it back.</summary>
public static class SeedWriterConformance
{
    /// <summary>The conversation <see cref="Check"/> writes.</summary>
    public static SeedConversation Sample { get; } = new([
        SeedMessage.Host("Fix the flaky login test.\nIt fails on CI only. 把登录页面改成深色主题 🙂"),
        new SeedMessage(SeedRole.Agent, [
            new SeedText("I will read the fixture."),
            new SeedToolCall("call-1", "Read", "{\"path\":\"tests/Fixture.cs\",\"note\":\"a \\\"quoted\\\" word\\nnext line\"}"),
            new SeedToolCall("call-2", "Grep", "{\"pattern\":\"Clock\"}"),
        ]),
        new SeedMessage(SeedRole.Tool, [new SeedToolResult("call-1", "line 1\nline 2 \"x\"")]),
        new SeedMessage(SeedRole.Tool, [new SeedToolResult("call-2", "no such file", IsError: true)]),
        SeedMessage.Agent("The fixture shares a clock."),
        SeedMessage.Host("Decided: use a fake clock."),
    ]);

    /// <summary>The ways <paramref name="writer"/> breaks its contract through <paramref name="doorId"/>; empty when it keeps it.</summary>
    /// <param name="writer">The writer to hold.</param>
    /// <param name="doorId">The door whose agent's format it writes.</param>
    public static IReadOnlyList<string> Check(IConversationSeedWriter writer, string doorId)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(doorId);
        var problems = new List<string>();

        if (writer.SupportFor(doorId) != AgentSupport.Supported)
        {
            try { writer.Write(Sample, doorId, "seed-1"); problems.Add("The door is not stated Supported, and Write did not throw NotSupportedException."); }
            catch (NotSupportedException) { }
            return problems;
        }

        SeededConversation seeded;
        try { seeded = writer.Write(Sample, doorId, "seed-1"); }
        catch (Exception e) { return [$"Write threw {e.GetType().Name}: {e.Message}"]; }

        if (seeded.SessionId != "seed-1") problems.Add($"The result's session id is '{seeded.SessionId}', not the one it was given.");
        if (seeded.Files.Count == 0) problems.Add("No file was written.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in seeded.Files)
        {
            if (string.IsNullOrWhiteSpace(f.Name) || Path.IsPathRooted(f.Name) || f.Name.Contains(':')
                || f.Name.Replace('\\', '/').Split('/').Contains(".."))
                problems.Add($"'{f.Name}' is not a name relative to the agent's home.");
            if (!names.Add(f.Name)) problems.Add($"The file '{f.Name}' is written twice.");
            if (string.IsNullOrEmpty(f.Text)) problems.Add($"The file '{f.Name}' is empty.");
        }

        try
        {
            var again = writer.Write(Sample, doorId, "seed-1");
            if (!seeded.Files.Select(f => (f.Name, f.Text)).SequenceEqual(again.Files.Select(f => (f.Name, f.Text))))
                problems.Add("Two writes of the same conversation gave different files.");
        }
        catch (Exception e) { problems.Add($"A second Write threw {e.GetType().Name}."); }

        try
        {
            var read = writer.Read(seeded).Messages;
            var at = 0;
            foreach (var message in Sample.Messages)
            {
                while (at < read.Count && !message.Same(read[at])) at++;
                if (at == read.Count) { problems.Add($"Read lost, changed or reordered the message '{Describe(message)}'."); break; }
                at++;
            }
        }
        catch (Exception e) { problems.Add($"Read threw {e.GetType().Name}: {e.Message}"); }

        try { writer.Write(SeedConversation.Empty, doorId, "seed-1"); problems.Add("Write of an empty conversation did not throw ArgumentException."); }
        catch (ArgumentException) { }
        try { writer.Write(Sample, doorId, " "); problems.Add("Write with a blank session id did not throw ArgumentException."); }
        catch (ArgumentException) { }
        return problems;
    }

    private static string Describe(SeedMessage m) =>
        m.Role + ": " + string.Join(" ", m.Parts.Select(p => p switch
        {
            SeedText t => t.Text,
            SeedToolCall c => $"call {c.CallId}",
            SeedToolResult r => $"result {r.CallId}",
            _ => p.GetType().Name,
        }));
}
