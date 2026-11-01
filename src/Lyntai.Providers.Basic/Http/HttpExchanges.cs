using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>What the HTTP exchanges of ONE agent turn showed, in the response's own facts — the status, the
/// <c>Retry-After</c> header and the error envelope, which the text contract (a verdict and a detail) does not
/// carry. The engine reports each exchange here; the agent door reads the latest failure and whether anything
/// ever answered. One instance per turn: a turn's calls are sequential, so nothing here is locked.</summary>
internal sealed class HttpExchanges(TimeProvider? time = null)
{
    /// <summary>Whether any response arrived this turn, an error status included: the prompt reached a server
    /// that answered.</summary>
    public bool Responded { get; private set; }

    /// <summary>Whether no exchange got past connecting — a refused connection, a name that would not resolve, a
    /// TLS handshake that never completed — so no byte of the prompt left. False once any response arrived or a
    /// failure left it unknown whether one was sent (a timeout, a reset).</summary>
    public bool NeverLeft { get; private set; }

    /// <summary>The latest exchange's failure, or null when it succeeded: an exchange that went well clears an
    /// earlier one, so a failure that is not HTTP's (a tool the model never converged on) is never typed by a
    /// request that was retried.</summary>
    public AgentFailure? Failure { get; private set; }

    /// <summary>A 2xx response began.</summary>
    public void Answered()
    {
        Responded = true;
        NeverLeft = false;
        Failure = null;
    }

    /// <summary>A response said no: an error status, or a 2xx whose body carries an error member.</summary>
    public void Rejected(HttpStatusCode status, HttpHeaders? headers, string? body)
    {
        Responded = true;
        NeverLeft = false;
        Failure = ProviderVerdictClassifier.ReadHttpFailure(status, headers, body, time)
            ?? new AgentFailure(AgentFailureKind.Fatal, "http:in-band-error");
    }

    /// <summary>A 2xx stream said no in a line of its own. Only the sentence survives the stream's parse, so it is
    /// read as the body's error member, which is how a gateway wrote it.</summary>
    public void RejectedInBand(HttpHeaders? headers, string error) =>
        Rejected(HttpStatusCode.OK, headers, new JsonObject { ["error"] = error }.ToJsonString());

    /// <summary>The request threw before any response: a transport fault. Whether a byte left is read from the
    /// exception, never assumed.</summary>
    public void Unreached(Exception ex)
    {
        NeverLeft = !Responded && NeverReached(ex);
        Failure = new AgentFailure(AgentFailureKind.Transient, "http:transport");
    }

    /// <summary>The inactivity window ran out: a request may have been delivered, and nothing says it was not.</summary>
    public void TimedOut()
    {
        NeverLeft = false;
        Failure = new AgentFailure(AgentFailureKind.Transient, "http:inactivity-timeout");
    }

    /// <summary>A stream that had begun broke: the response had arrived, so the prompt was taken.</summary>
    public void Broke(bool timedOut) =>
        Failure = new AgentFailure(AgentFailureKind.Transient, timedOut ? "http:inactivity-timeout" : "http:stream-broke");

    // The same set `ProviderVerdictClassifier.NeverReachedTheBackend` states for a submit (internal to Core): a
    // timeout, a protocol error mid-response or anything unrecognised may have been delivered.
    private static bool NeverReached(Exception ex) => ex switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError
                                              or HttpRequestError.NameResolutionError
                                              or HttpRequestError.SecureConnectionError } => true,
        System.Net.Sockets.SocketException => true,
        _ => false,
    };
}
