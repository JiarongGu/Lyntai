using System.Net;
using System.Net.Sockets;
using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

/// <summary>The router's thrown-exception rules, public so a backend that catches its own exceptions reports what
/// the router would have: a submit that may have reached the backend is INCONCLUSIVE (it may already hold a
/// billable render), one that provably never left the process is a plain classified failure, and a throw is never
/// <see cref="ProviderVerdict.Refused"/>.</summary>
public class SubmitThrowClassificationTests
{
    [Fact]
    public void A_throw_after_sending_is_inconclusive()
    {
        var op = QueuedOperation.FromThrownSubmit(
            new HttpRequestException(HttpRequestError.ResponseEnded, "the connection dropped mid-response"));

        Assert.Equal(QueuedOperationStatus.Failed, op.Status);
        Assert.True(op.Inconclusive);
        Assert.Equal("the connection dropped mid-response", op.Detail);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public void A_throw_that_never_reached_the_backend_is_a_plain_classified_failure(HttpRequestError error)
    {
        var op = QueuedOperation.FromThrownSubmit(new HttpRequestException(error, "refused"), detail: "fal: refused");

        Assert.False(op.Inconclusive);
        Assert.Equal(ProviderVerdict.Failed, op.Verdict);
        Assert.Equal("fal: refused", op.Detail);
    }

    [Fact]
    public void A_socket_failure_never_reached_the_backend_either()
    {
        Assert.False(QueuedOperation.FromThrownSubmit(new SocketException()).Inconclusive);
    }

    [Fact]
    public void A_throw_before_sending_began_is_never_inconclusive()
    {
        var op = QueuedOperation.FromThrownSubmit(
            new HttpRequestException("429 Too Many Requests", null, HttpStatusCode.TooManyRequests), sent: false);

        Assert.False(op.Inconclusive);
        Assert.Equal(ProviderVerdict.RateLimited, op.Verdict);
    }

    [Fact]
    public void A_thrown_exception_is_never_Refused()
    {
        var ex = new InvalidOperationException("proxy error page: content policy violation");

        Assert.Equal(ProviderVerdict.Refused, ProviderVerdictClassifier.FromException(ex));
        Assert.Equal(ProviderVerdict.Failed, ProviderVerdictClassifier.FromThrown(ex));
        Assert.Equal(ProviderVerdict.Failed, QueuedOperation.FromThrownSubmit(ex, sent: false).Verdict);
    }

    [Fact]
    public void An_in_band_auth_failure_without_credentials_is_NotConfigured()
    {
        const string text = "error: invalid api key";

        Assert.Equal(ProviderVerdict.NotConfigured, ProviderVerdictClassifier.FromErrorText(text, hasCredentials: false));
        Assert.Equal(ProviderVerdict.AuthFailed, ProviderVerdictClassifier.FromErrorText(text, hasCredentials: true));
        Assert.Equal(ProviderVerdict.RateLimited, ProviderVerdictClassifier.FromErrorText("rate limit", hasCredentials: false));
    }

    [Fact]
    public void A_refused_submission_has_no_provider_and_carries_its_verdict()
    {
        var submission = MediaSubmission.Failure(ProviderVerdict.Refused, "budget reached");

        Assert.Equal("", submission.ProviderId);
        Assert.Equal("", submission.Operation.Id);
        Assert.Equal(QueuedOperationStatus.Failed, submission.Operation.Status);
        Assert.Equal(ProviderVerdict.Refused, submission.Operation.Verdict);
        Assert.Equal("budget reached", submission.Operation.Detail);
        Assert.Null(QueuedOperation.Failure("no reason given").Verdict); // unknown verdict: the router classifies
    }
}
