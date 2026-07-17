namespace Lyntai.Guards;

/// <summary>A guard's verdict on a request or a reply: let it through, block it, or replace its text
/// (e.g. a safe refusal, or redacted content).</summary>
public sealed record GuardOutcome
{
    public enum Kind
    {
        /// <summary>Let it proceed unchanged.</summary>
        Allow,

        /// <summary>Stop here — the request is refused / the reply is withheld (with a <see cref="Reason"/>).</summary>
        Block,

        /// <summary>Substitute <see cref="Replacement"/> for the text (redaction, canned response, …).</summary>
        Replace,
    }

    private GuardOutcome(Kind result, string? reason = null, string? replacement = null)
    {
        Result = result;
        Reason = reason;
        Replacement = replacement;
    }

    public Kind Result { get; }
    public string? Reason { get; }
    public string? Replacement { get; }

    public bool IsAllow => Result == Kind.Allow;

    public static GuardOutcome Allow { get; } = new(Kind.Allow);
    public static GuardOutcome Block(string reason) => new(Kind.Block, reason);
    public static GuardOutcome Replace(string replacement, string? reason = null) => new(Kind.Replace, reason, replacement);
}
