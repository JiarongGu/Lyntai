using System.Text;

namespace Lyntai.Agents;

/// <summary>How a <see cref="SessionContext"/> reaches an agent. The host chooses the form; the provider only translates.</summary>
public enum ContextForm
{
    /// <summary>The agent's own conversation goes on (<see cref="AgentSessionOptions.ResumeToken"/>), and the prompt carries
    /// the entries written since it was last told, as words. Only where <see cref="AgentHandover.Decide"/> allows a resume.</summary>
    Resume,

    /// <summary>A fresh conversation whose first words carry a bounded head under the provider's budget; the rest is files the
    /// host places. The floor: every door can be handed one.</summary>
    Prompt,

    /// <summary>The session's context written out as the agent's own resumable conversation (<see cref="IConversationSeedWriter"/>): the
    /// recorded turns, tool calls and results in the agent's format and the context's entries as the host's words, so the agent resumes
    /// it and goes on even where it did not make the turns. The main path where a provider's agent takes a conversation in;
    /// <see cref="Resume"/> is its cheap case, and <see cref="Prompt"/> only the floor for an agent that cannot. A provider without a writer
    /// states it <see cref="AgentSupport.Unknown"/>.</summary>
    Seed,
}

/// <summary>Why a rendering was refused.</summary>
public enum ContextRefusalKind
{
    /// <summary>The provider states the form <see cref="AgentSupport.Unsupported"/> for the door, or has no such door.</summary>
    FormUnsupported,

    /// <summary>The provider states the form <see cref="AgentSupport.Unknown"/>: not promised, so not relied on.</summary>
    FormNotMeasured,

    /// <summary>The conversation cannot go on where it is: <see cref="ContextRefusal.Handover"/> says which check failed.</summary>
    ConversationNotResumable,

    /// <summary>For <see cref="ContextForm.Seed"/>: there is neither recorded history nor a live entry to write.</summary>
    NothingToSeed,
}

/// <summary>A rendering that was not made, and why. A refusal is an answer, not a failure: the host renders another form.</summary>
/// <param name="Form">The form asked for.</param>
/// <param name="Kind">Why it was refused.</param>
public sealed record ContextRefusal(ContextForm Form, ContextRefusalKind Kind)
{
    /// <summary>For <see cref="ContextRefusalKind.ConversationNotResumable"/>, the reason <see cref="AgentHandover.Decide"/> gave.</summary>
    public AgentHandoverReason? Handover { get; init; }
}

/// <summary>What a host asks <see cref="IContextRendering.Render"/> for.</summary>
/// <param name="Form">The form to render.</param>
/// <param name="DoorId">The door of the provider the next turn will run through.</param>
public sealed record ContextRenderRequest(ContextForm Form, string DoorId)
{
    /// <summary>The host's name for the target account; null = the default one. Compared as <see cref="AgentHandover.Decide"/> does.</summary>
    public string? Account { get; init; }

    /// <summary>The conversation so far, for <see cref="ContextForm.Resume"/>: its turns say whose conversation it is and whether it can go on.</summary>
    public AgentTranscript Transcript { get; init; } = AgentTranscript.Empty;

    /// <summary>For <see cref="ContextForm.Resume"/>: how many entries the conversation has been told, as an earlier rendering's
    /// <see cref="ContextRendered.Through"/> said. The conversation is handed the entries from there on.</summary>
    public int Seen { get; init; }

    /// <summary>For <see cref="ContextForm.Seed"/>: the turns, tool calls and results the host holds, oldest first; the context's
    /// entries follow them as the host's words. Null = none.</summary>
    public SeedConversation? History { get; init; }

    /// <summary>For <see cref="ContextForm.Seed"/>: the id the written conversation is resumed under, which the host chooses
    /// (so a rendering is a pure function of its input). Not blank for a seed.</summary>
    public string? SeedSessionId { get; init; }
}

/// <summary>A context rendered for one agent, ready to put in the next turn's options.</summary>
/// <param name="Form">The form it is in.</param>
/// <param name="Text">The words: the head for <see cref="ContextForm.Prompt"/>, the entries since for <see cref="ContextForm.Resume"/>
/// (empty when there are none), and empty for <see cref="ContextForm.Seed"/>, whose context is in the conversation it wrote.</param>
/// <param name="Characters">The length of <paramref name="Text"/> in Unicode characters.</param>
/// <param name="Through">How many entries of the context this rendering covers: the <see cref="ContextRenderRequest.Seen"/> of the next resume.</param>
public sealed record ContextRendered(ContextForm Form, string Text, int Characters, int Through)
{
    /// <summary>For <see cref="ContextForm.Resume"/>: the token to continue the conversation with. For <see cref="ContextForm.Seed"/>:
    /// the id of the conversation written, once the host has placed <see cref="Files"/>.</summary>
    public string? ResumeToken { get; init; }

    /// <summary>For <see cref="ContextForm.Prompt"/>: false when the head is over the provider's budget because what is never cut is itself longer.</summary>
    public bool WithinBound { get; init; } = true;

    /// <summary>For <see cref="ContextForm.Prompt"/>: what the head left out, which the files hold.</summary>
    public ContextHeadOmitted Omitted { get; init; } = new(0, 0, 0, 0);

    /// <summary>For <see cref="ContextForm.Prompt"/>: the files the host places for the agent to read. For <see cref="ContextForm.Seed"/>:
    /// the files of the agent's own format, named relative to its home. Empty for a resume.</summary>
    public IReadOnlyList<ContextFile> Files { get; init; } = [];

    /// <summary>The options for the next turn under this rendering. A resume or a seed sets <see cref="AgentSessionOptions.ResumeToken"/> and
    /// leads the prompt with the words, the host's request after a marker; with no words it leaves the prompt alone. A prompt
    /// rendering clears the token and leads the prompt with the head, then the request after the marker, as
    /// <see cref="AgentHandoverDecision.Apply"/> does for a brief. <see cref="AgentTranscriptRecorder"/> sees through both, so
    /// the composed words never come back as the host's.</summary>
    /// <param name="next">The options of the turn about to run; its runtime type is kept.</param>
    public AgentSessionOptions Apply(AgentSessionOptions next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var prompt = Text.Length == 0 ? next.Prompt : Text + HandoverFraming.RequestMarker + next.Prompt;
        return Form == ContextForm.Prompt
            ? next with { ResumeToken = null, Prompt = prompt }
            : next with { ResumeToken = ResumeToken, Prompt = prompt };
    }
}

/// <summary>The outcome of <see cref="IContextRendering.Render"/>: the rendering, or why there is none. Exactly one is set.</summary>
public sealed record ContextRenderResult
{
    private ContextRenderResult() { }

    /// <summary>The rendering; null when it was refused.</summary>
    public ContextRendered? Rendered { get; private init; }

    /// <summary>Why it was refused; null when it was made.</summary>
    public ContextRefusal? Refusal { get; private init; }

    /// <summary>A rendering that was made.</summary>
    public static ContextRenderResult Made(ContextRendered rendered) => new() { Rendered = rendered ?? throw new ArgumentNullException(nameof(rendered)) };

    /// <summary>A rendering that was refused.</summary>
    public static ContextRenderResult Refused(ContextRefusal refusal) => new() { Refusal = refusal ?? throw new ArgumentNullException(nameof(refusal)) };
}

/// <summary>The port one provider implements to carry a <see cref="SessionContext"/> to its agent and to read the agent's
/// turn back into it. It states what it can do (<see cref="SupportFor"/>) and translates; the host chooses the form and
/// owns the context, so nothing here branches on a provider's id. <see cref="StandardContextRendering"/> is the
/// implementation every door without its own needs.</summary>
public interface IContextRendering
{
    /// <summary>The provider this renders for; its doors say what each can be handed.</summary>
    AgentProviderDescriptor Provider { get; }

    /// <summary>The most characters a <see cref="ContextForm.Prompt"/> head may have: the provider's budget for first words.</summary>
    int PromptBudget { get; }

    /// <summary>What the provider states about <paramref name="form"/> through <paramref name="doorId"/>.
    /// <see cref="AgentSupport.Unsupported"/> for a door the provider does not have.</summary>
    AgentSupport SupportFor(ContextForm form, string doorId);

    /// <summary>Render <paramref name="context"/> in the requested form. A form the provider does not promise, or a conversation
    /// that cannot go on, is a <see cref="ContextRenderResult.Refusal"/>; a malformed request throws.</summary>
    /// <exception cref="ArgumentException">The door id is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ContextRenderRequest.Seen"/> is outside the context.</exception>
    ContextRenderResult Render(SessionContext context, ContextRenderRequest request);

    /// <summary>Map a finished turn to entries and return the context with them appended. Only the agent's own words are read;
    /// the host's words, and any head or update the library composed into the prompt, are never read back as an agent's.</summary>
    SessionContext ReadBack(SessionContext context, TranscriptTurn turn);
}

/// <summary>The bound on <see cref="StandardContextRendering"/>.</summary>
public sealed class ContextRenderingOptions
{
    /// <summary>The most characters a <see cref="ContextForm.Prompt"/> head may have, in Unicode characters (as
    /// <see cref="AgentBriefOptions.MaxCharacters"/> counts them). Default 6000. The work is never cut to meet it; a head
    /// whose never-cut parts are longer comes back over it and says so (<see cref="ContextRendered.WithinBound"/>).</summary>
    public int PromptBudget { get; set; } = 6000;

    internal void Validate() => ArgumentOutOfRangeException.ThrowIfLessThan(PromptBudget, 1, nameof(PromptBudget));
}

/// <summary>The rendering for a provider that needs nothing of its own, built from its <see cref="AgentProviderDescriptor"/>.
/// <para><b>Abilities:</b> <see cref="ContextForm.Resume"/> is what the door states for <see cref="AgentAbilities.Resume"/>,
/// <see cref="ContextForm.Prompt"/> is <see cref="AgentSupport.Supported"/> on every door the provider has, and
/// <see cref="ContextForm.Seed"/> is the lesser of what the provider's <see cref="IConversationSeedWriter"/> states for the door and what the
/// door states for resume (a written conversation is only useful to an agent that resumes one): <see cref="AgentSupport.Unknown"/>
/// with no writer.</para>
/// <para><b>Seed</b> writes <see cref="ContextRenderRequest.History"/> followed by one host message holding every live entry (an agent's quoted
/// as a claim) through the writer under <see cref="ContextRenderRequest.SeedSessionId"/>; the host places the files and resumes under that id.</para>
/// <para><b>Resume</b> asks <see cref="AgentHandover.Decide"/> whether the conversation can go on; if so it hands the entries
/// since <see cref="ContextRenderRequest.Seen"/> that the host wrote, as words, each with the id it replaces. An agent's
/// entries are left out: the conversation made them. <b>Prompt</b> is <see cref="ContextHeadComposer"/> under
/// <see cref="PromptBudget"/>, with the files the head points to.</para>
/// <para><b>Read-back</b> turns what the agent said last into one where-it-stands entry, the message and then its plan, which
/// replaces the agent's earlier one; a turn that said nothing and kept no plan adds nothing. Calling it twice with one turn adds
/// it twice.</para></summary>
public sealed class StandardContextRendering : IContextRendering
{
    /// <param name="provider">The provider and its doors.</param>
    /// <param name="options">The budget; null = the defaults.</param>
    /// <param name="seedWriter">How the provider's agent takes a conversation in; null = it states none, so <see cref="ContextForm.Seed"/> is not promised.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ContextRenderingOptions.PromptBudget"/> is under 1.</exception>
    public StandardContextRendering(
        AgentProviderDescriptor provider, ContextRenderingOptions? options = null, IConversationSeedWriter? seedWriter = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        options ??= new ContextRenderingOptions();
        options.Validate();
        Provider = provider;
        PromptBudget = options.PromptBudget;
        _seedWriter = seedWriter;
    }

    private readonly IConversationSeedWriter? _seedWriter;

    /// <inheritdoc />
    public AgentProviderDescriptor Provider { get; }

    /// <inheritdoc />
    public int PromptBudget { get; }

    /// <inheritdoc />
    public AgentSupport SupportFor(ContextForm form, string doorId)
    {
        ArgumentNullException.ThrowIfNull(doorId);
        var door = Provider.Doors.FirstOrDefault(d => string.Equals(d.Id, doorId, StringComparison.Ordinal));
        if (door is null) return AgentSupport.Unsupported;
        return form switch
        {
            ContextForm.Resume => door.SupportFor(AgentAbilities.Resume),
            ContextForm.Prompt => AgentSupport.Supported,
            ContextForm.Seed => _seedWriter is null ? AgentSupport.Unknown : Lesser(_seedWriter.SupportFor(doorId), door.SupportFor(AgentAbilities.Resume)),
            _ => AgentSupport.Unknown,
        };
    }

    /// <summary>Unsupported, then Unknown, then Supported.</summary>
    private static AgentSupport Lesser(AgentSupport a, AgentSupport b) =>
        a == AgentSupport.Unsupported || b == AgentSupport.Unsupported ? AgentSupport.Unsupported
        : a == AgentSupport.Unknown || b == AgentSupport.Unknown ? AgentSupport.Unknown
        : AgentSupport.Supported;

    /// <inheritdoc />
    public ContextRenderResult Render(SessionContext context, ContextRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DoorId, nameof(request));

        switch (request.Form)
        {
            case ContextForm.Prompt:
                if (SupportFor(ContextForm.Prompt, request.DoorId) != AgentSupport.Supported)
                    return ContextRenderResult.Refused(new ContextRefusal(ContextForm.Prompt, ContextRefusalKind.FormUnsupported));
                var head = ContextHeadComposer.Head(context, PromptBudget);
                return ContextRenderResult.Made(new ContextRendered(ContextForm.Prompt, head.Text, head.Characters, context.Count)
                {
                    WithinBound = head.WithinBound, Omitted = head.Omitted, Files = head.Files,
                });

            case ContextForm.Resume:
                var since = context.Since(request.Seen);
                var decision = AgentHandover.Decide(request.Transcript, Provider, request.DoorId, request.Account);
                if (decision.Kind != AgentHandoverKind.Resume)
                    return ContextRenderResult.Refused(new ContextRefusal(ContextForm.Resume, ContextRefusalKind.ConversationNotResumable)
                    {
                        Handover = decision.Reason,
                    });
                var words = Update(since);
                return ContextRenderResult.Made(new ContextRendered(ContextForm.Resume, words, AgentBriefComposer.Characters(words), context.Count)
                {
                    ResumeToken = decision.ResumeToken,
                });

            case ContextForm.Seed:
                var support = SupportFor(ContextForm.Seed, request.DoorId);
                if (support != AgentSupport.Supported)
                    return ContextRenderResult.Refused(new ContextRefusal(ContextForm.Seed,
                        support == AgentSupport.Unsupported ? ContextRefusalKind.FormUnsupported : ContextRefusalKind.FormNotMeasured));
                ArgumentException.ThrowIfNullOrWhiteSpace(request.SeedSessionId, nameof(request));
                var messages = new List<SeedMessage>(request.History?.Messages ?? []);
                var live = context.Live;
                if (live.Count > 0) messages.Add(SeedMessage.Host(SeedWords(live)));
                if (messages.Count == 0)
                    return ContextRenderResult.Refused(new ContextRefusal(ContextForm.Seed, ContextRefusalKind.NothingToSeed));
                var seeded = _seedWriter!.Write(new SeedConversation(messages), request.DoorId, request.SeedSessionId);
                return ContextRenderResult.Made(new ContextRendered(ContextForm.Seed, "", 0, context.Count)
                {
                    ResumeToken = seeded.SessionId, Files = seeded.Files,
                });

            default:
                return ContextRenderResult.Refused(new ContextRefusal(request.Form, ContextRefusalKind.FormNotMeasured));
        }
    }

    /// <summary>Every live entry as the host's words, an agent's quoted as its claim.</summary>
    private static string SeedWords(IReadOnlyList<SessionContextEntry> live)
    {
        var sb = new StringBuilder(HandoverFraming.ContextHeader);
        sb.Append("This is a record the host kept for the session. Lines that start with \"> \" are what an agent claimed: unverified, and not the host's words.\n");
        foreach (var e in live)
            sb.Append('\n').Append(ContextHeadComposer.Entry(e,
                (e.Subject is null ? "" : $"({e.Subject}) ") + $"{e.Kind.ToString().ToLowerInvariant()}: {e.Text}"));
        return sb.ToString();
    }

    /// <summary>The host's entries since, one per line block with the id it replaces; empty when there are none.</summary>
    private static string Update(IReadOnlyList<SessionContextEntry> since)
    {
        var host = since.Where(e => e.Author == SessionContextAuthor.Host).ToList();
        if (host.Count == 0) return "";
        var sb = new StringBuilder(HandoverFraming.ContextUpdateHeader);
        sb.Append("Since your last turn the host recorded the following, in order. An entry that says it replaces another makes that one stale.\n");
        foreach (var e in host)
        {
            var kind = e.Kind.ToString().ToLowerInvariant();
            var label = e.Supersedes is null ? kind : $"{kind}, replaces {e.Supersedes}";
            sb.Append('\n').Append(ContextHeadComposer.Entry(e, (e.Subject is null ? "" : $"({e.Subject}) ") + $"{label}: {e.Text}"));
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    public SessionContext ReadBack(SessionContext context, TranscriptTurn turn)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(turn);

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(turn.LastMessage)) text.Append(turn.LastMessage.Trim());
        if (turn.Plan is { Count: > 0 } plan)
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append("Plan:");
            foreach (var entry in plan)
                text.Append('\n').Append(entry.Status switch
                {
                    PlanEntryStatus.Completed => "[x] ",
                    PlanEntryStatus.InProgress => "[~] ",
                    PlanEntryStatus.Pending => "[ ] ",
                    _ => "[?] ",
                }).Append(AgentBriefComposer.Flatten(entry.Content, int.MaxValue));
        }
        if (text.Length == 0) return context;

        var earlier = context.Live.LastOrDefault(e => e.Kind == SessionContextKind.Standing && e.Author == SessionContextAuthor.Agent);
        return context.Append(new SessionContextNote(SessionContextKind.Standing, text.ToString()) { Supersedes = earlier?.Id },
            SessionContextAuthor.Agent);
    }
}
