namespace Lyntai.Llm;

/// <summary>
/// A non-<see cref="LlmVerdict.Ok"/> outcome raised as an EXCEPTION — the counterpart of
/// <see cref="LlmReply.Verdict"/> for a seam whose contract is to THROW rather than return a reply.
/// Today that seam is the Microsoft.Extensions.AI reverse bridge (<c>ILlmClient.AsChatClient()</c>): an
/// <c>IChatClient</c> response has nowhere to carry a verdict, so its failure idiom is an exception — and
/// without this type the verdict survived only as text interpolated into the message, which made
/// <see cref="LlmVerdict.NotConfigured"/> ("never set up", so the host should offer SETUP rather than
/// report an error) indistinguishable from a real fault except by string-parsing.
/// <para><b>Why it lives in Core rather than in the adapter that throws it.</b> The verdict taxonomy is
/// Core's, so the exception carrying one is Core's too; defining it in the bridge would guarantee a second
/// copy the first time another seam has to throw instead of returning an <see cref="LlmReply"/>.</para>
/// <para><b>Why it derives from <see cref="InvalidOperationException"/>.</b> That is exactly what the
/// bridge threw before this type existed, so every <c>catch (InvalidOperationException)</c> a consumer has
/// already written keeps working unchanged. For the same reason <b>the message text is part of the
/// contract</b>: it stays byte-identical to the old one (<c>"lyntai: NotConfigured — no api key"</c>),
/// because parsing it was the ONLY way to recover the verdict and is therefore the likeliest thing anyone
/// wrote. The one check that no longer matches is an EXACT type test
/// (<c>ex.GetType() == typeof(InvalidOperationException)</c>) — including xUnit's <c>Assert.Throws&lt;T&gt;</c>,
/// which is an exact match and needs <c>ThrowsAny</c> here.</para>
/// </summary>
public sealed class LlmVerdictException(LlmVerdict verdict, string? detail = null)
    : InvalidOperationException($"lyntai: {verdict} — {detail}")
{
    /// <summary>How the call ended. Read this instead of the message: the branch a host most needs is
    /// <see cref="LlmVerdict.NotConfigured"/>, which is not a fault at all (<c>docs/DECISIONS.md</c> D38) — and
    /// <see cref="LlmVerdictExtensions.IsTransient(LlmVerdict)"/> answers "is a retry worth one attempt?".</summary>
    public LlmVerdict Verdict { get; } = verdict;

    /// <summary>The provider's error context (stderr tail, HTTP status, …) as it arrived on
    /// <see cref="LlmReply.Detail"/> / <see cref="LlmChunk.Detail"/>, so recovering it needs no parsing of
    /// <see cref="System.Exception.Message"/>. Null when the outcome carried none.</summary>
    public string? Detail { get; } = detail;
}
