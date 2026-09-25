namespace Lyntai.Inference;

/// <summary>What ROUTING needs from a response, and the whole of it.
///
/// <para><b>This is the contract that makes ONE router possible for every kind.</b> Fallback needs to know
/// whether an attempt succeeded, whether the failure belongs to this host, and what to tell a caller when
/// every candidate is exhausted — and nothing else about the payload. A response carrying these two members
/// can be routed without the router knowing what it holds (<c>docs/DECISIONS.md</c> <b>D153</b>).</para>
///
/// <para><b>The cost, stated so it is chosen rather than discovered:</b> every response type is committed to
/// carrying a verdict. A backend that would rather throw has to decide what a failed call RETURNS instead —
/// which is the discipline the LLM side has had since 1.0, and the reason a zero-content reply is
/// <see cref="ProviderVerdict.Failed"/> rather than an empty success.</para></summary>
/// <remarks><b>Implement it on a REFERENCE type.</b> <see cref="ProviderRouter{TRequest,TResponse}"/>
/// constrains its response to a class, because it distinguishes "no candidate answered" from "one answered
/// badly" by holding a null — and a struct's default is not null, so a value-type response would report a
/// zeroed answer as a real one. Every response here is a record.</remarks>
public interface IProviderOutcome
{
    /// <summary>How the attempt ended. The router acts on this alone.</summary>
    ProviderVerdict Verdict { get; }

    /// <summary>The backend's own words, or the failure reason. Surfaced verbatim, never parsed.</summary>
    string? Detail { get; }

    /// <summary>What the call spent, in the ledger's currency — how a governed router records a response
    /// without knowing its shape (<c>docs/DECISIONS.md</c> <b>D163</b>). DEFAULTED to null, so a response
    /// type says nothing unless it chooses to: a type declaring its own <c>ProviderUsage? Usage</c>
    /// implements this implicitly (<see cref="VectorResponse"/>, <see cref="ScoreResponse"/>), while one
    /// whose usage is a richer shape (<see cref="TextResponse"/>, <see cref="MediaResponse"/>) reports null
    /// here and is recorded by its own door instead — never twice.</summary>
    ProviderUsage? Usage => null;
}

/// <summary>A backend that answers one request shape with one response shape — the single call seam every
/// KIND is an instance of.
///
/// <para><b>One interface per SIGNATURE, not per kind.</b> Image and video share this contract because they
/// share a shape; which of them a backend serves is a <see cref="ProviderCapabilities.Produces"/> value,
/// which is the axis a router selects on. An interface exists here only where the types genuinely
/// differ.</para>
///
/// <para><b>An application may close this over its OWN types</b> and get candidate selection, dead-host
/// cooldown, admission and fallback from the shared router without this library knowing its kind exists.
/// That is the point of the generic parameter rather than a fixed set of methods.</para></summary>
/// <typeparam name="TRequest">What the backend is asked for.</typeparam>
/// <typeparam name="TResponse">What it answers with — see <see cref="IProviderOutcome"/> for why it must
/// carry a verdict.</typeparam>
public interface IProviderCall<in TRequest, TResponse> : IModelProvider
    where TResponse : IProviderOutcome
{
    /// <summary>Answer one request. <b>Report failure as a non-Ok verdict rather than by throwing</b>, so
    /// the router can advance to the next candidate — a throw is classified, but a verdict says what
    /// happened. Cancellation belongs to the caller and propagates.</summary>
    Task<TResponse> CallAsync(TRequest request, CancellationToken ct = default);
}

// No generic streaming or queued counterpart beside this seam, deliberately — docs/DECISIONS.md D155.
