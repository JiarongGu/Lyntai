namespace Lyntai.Llm.Cli;

/// <summary>What one line of a CLI's output turned out to be.</summary>
public enum CliOutputEventKind
{
    /// <summary>Not interesting to the provider (a handshake/init line, tool chatter, a malformed line).
    /// Ignored rather than treated as an error — a CLI's stream carries plenty that isn't the answer.</summary>
    Ignored,

    /// <summary>A piece of the assistant's answer, to be delivered to the caller as it arrives.</summary>
    Content,

    /// <summary>The terminal line: the final text (and usage/cost, where the CLI reports it).</summary>
    Result,

    /// <summary>The backend reported that the turn FAILED, in its own output stream. Needed because a CLI can
    /// report failure in-band and still exit 0 — codex prints
    /// <c>{"type":"turn.failed","error":{"message":"… 401 Unauthorized …"}}</c> and exits cleanly. Without
    /// this the engine would see "no content" and report a bare <see cref="LlmVerdict.Failed"/>, losing both
    /// the reason and the verdict the router acts on.
    ///
    /// Reserve it for a TERMINAL failure. Transient noise a backend logs mid-run (codex's
    /// <c>"Reconnecting… 2/5"</c> lines, which also appear in runs that go on to SUCCEED) must be
    /// <see cref="Ignored"/>, or a healthy call gets failed on a retry it recovered from.</summary>
    Failure,
}

/// <summary>One line of a CLI's output, decoded by an <see cref="ICliProviderDialect"/> into the three
/// things the provider engine acts on. The wire format itself (JSONL, framed text, whatever the backend
/// emits) never leaves the dialect.</summary>
/// <param name="Kind">What this line was.</param>
/// <param name="Text">The content or final text; empty for <see cref="CliOutputEventKind.Ignored"/>.</param>
/// <param name="Usage">Token/cost usage, when the line carries it (typically only the result line).</param>
public sealed record CliOutputEvent(CliOutputEventKind Kind, string Text = "", LlmUsage? Usage = null)
{
    /// <summary>A line the provider should skip.</summary>
    public static readonly CliOutputEvent Ignored = new(CliOutputEventKind.Ignored);

    /// <summary>A piece of the answer. Report a line that carried no content as <see cref="Ignored"/> rather
    /// than as empty content — that is what it is. An empty one is not a defect either way: the engine's
    /// "did anything arrive?" test is the router's commit gate, <c>Kind == Content &amp;&amp; Text.Length &gt; 0</c>,
    /// so empty text is neither delivered nor allowed to commit a stream and disable fallback.</summary>
    public static CliOutputEvent Content(string text) => new(CliOutputEventKind.Content, text);

    /// <summary>The terminal line carrying the final text (+ usage, where reported).</summary>
    public static CliOutputEvent Result(string text, LlmUsage? usage = null) =>
        new(CliOutputEventKind.Result, text, usage);

    /// <summary>The backend reported a TERMINAL turn failure. <paramref name="message"/> is classified by
    /// <see cref="LlmVerdictClassifier"/> and surfaced as the reply's detail — pass the backend's own
    /// wording, not a paraphrase.</summary>
    public static CliOutputEvent Failure(string message) => new(CliOutputEventKind.Failure, message);
}

/// <summary>How a CLI expects the prompt to arrive.</summary>
public enum CliPromptDelivery
{
    /// <summary>On stdin — the safe default: a prompt carries newlines and metacharacters, and argv has
    /// length limits.</summary>
    Stdin,

    /// <summary>As the last positional ARGUMENT, for a CLI that takes its prompt that way. Still never
    /// through a shell (values travel as separate <c>ArgumentList</c> entries).</summary>
    Argument,
}
