using System.Net;
using Lyntai.Agents;
using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

/// <summary>What <see cref="ProviderVerdictClassifier.ReadHttpFailure"/> reads from a response's status, headers and
/// error envelope. Every response is SOURCE-DERIVED: the envelope shapes OpenAI, Anthropic and OpenRouter document
/// (<c>error.code</c>/<c>error.type</c>/<c>error.message</c>), written from those pages and not captured from a call.</summary>
// serialized with the other classifier tests: AddErrorTextMatcher mutates a process-wide list
[Collection("verdict-matchers")]
public class HttpFailureReaderTests
{
    private static readonly TimeProvider Clock = FixedLocalClock.Wednesday;

    private static AgentFailure Read(HttpStatusCode status, string? body, string? retryAfter = null)
    {
        using var response = new HttpResponseMessage(status);
        if (retryAfter is not null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return ProviderVerdictClassifier.ReadHttpFailure(status, response.Headers, body, Clock)
            ?? throw new Xunit.Sdk.XunitException($"no failure read from {(int)status}: {body}");
    }

    private const string OpenAiRateLimit =
        """{"error":{"message":"Rate limit reached for gpt-4o in organization org-x on requests per min (RPM): Limit 3, Used 3, Requested 1.","type":"requests","param":null,"code":"rate_limit_exceeded"}}""";

    private const string OpenAiNoQuota =
        """{"error":{"message":"You exceeded your current quota, please check your plan and billing details.","type":"insufficient_quota","param":null,"code":"insufficient_quota"}}""";

    private const string AnthropicOverloaded =
        """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";

    private const string AnthropicRateLimit =
        """{"type":"error","error":{"type":"rate_limit_error","message":"This request would exceed your organization's rate limit of 50 requests per minute."}}""";

    private const string AnthropicCredit =
        """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."}}""";

    private const string OpenRouterCredit =
        """{"error":{"code":402,"message":"Insufficient credits. Add more using https://openrouter.ai/credits"}}""";

    private const string OpenRouterRateLimit =
        """{"error":{"code":429,"message":"Rate limit exceeded: free-models-per-day. Add 10 credits to unlock 1000 free model requests per day","metadata":{"headers":{"X-RateLimit-Limit":"50","X-RateLimit-Remaining":"0","X-RateLimit-Reset":"1741305600000"}}}}""";

    [Fact]
    public void A_429_with_Retry_After_in_seconds_is_a_rate_limit_not_before_that_moment()
    {
        var failure = Read(HttpStatusCode.TooManyRequests, OpenAiRateLimit, "30");

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RestrictionCause.Rate, failure.Restriction!.Cause);
        Assert.Equal(RecoveryKind.RetryNotBefore, failure.Restriction.Recovery.Kind);
        Assert.Equal(Clock.GetUtcNow().AddSeconds(30), failure.Restriction.Recovery.At);
    }

    [Theory] // an HTTP date in each of the three formats HTTP allows, read as UTC
    [InlineData("Wed, 28 Oct 2026 12:00:30 GMT")]
    [InlineData("Wednesday, 28-Oct-26 12:00:30 GMT")]
    [InlineData("Wed Oct 28 12:00:30 2026")]
    public void A_429_with_Retry_After_as_an_HTTP_date_reads_that_date(string date)
    {
        var failure = Read(HttpStatusCode.TooManyRequests, AnthropicRateLimit, date);

        Assert.Equal(RecoveryKind.RetryNotBefore, failure.Restriction!.Recovery.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 12, 0, 30, TimeSpan.Zero), failure.Restriction.Recovery.At);
    }

    [Theory] // absent or unreadable: nothing is stated, and nothing is defaulted
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("99999999999999")]
    [InlineData("Oct 28")]
    public void A_429_without_a_readable_Retry_After_has_an_unknown_recovery(string? header)
    {
        var failure = Read(HttpStatusCode.TooManyRequests, OpenAiRateLimit, header);

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RecoveryKind.Unknown, failure.Restriction!.Recovery.Kind);
        Assert.Null(failure.Restriction.Recovery.At);
    }

    [Fact]
    public void A_429_with_no_headers_object_at_all_reads_the_same()
    {
        var failure = ProviderVerdictClassifier.ReadHttpFailure(HttpStatusCode.TooManyRequests, null, null, Clock)!;

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RestrictionCause.Rate, failure.Restriction!.Cause);
        Assert.Equal(RecoveryKind.Unknown, failure.Restriction.Recovery.Kind);
    }

    [Fact]
    public void A_429_whose_envelope_states_its_own_wait_keeps_it_when_no_header_does()
    {
        var failure = Read(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit exceeded, retry after 60s","type":"requests","code":"rate_limit_exceeded"}}""");

        Assert.Equal(RecoveryKind.RetryNotBefore, failure.Restriction!.Recovery.Kind);
        Assert.Equal(Clock.GetUtcNow().AddSeconds(60), failure.Restriction.Recovery.At);
    }

    [Fact]
    public void The_header_leads_a_wait_the_envelope_also_states()
    {
        var failure = Read(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit exceeded, retry after 60s","code":"rate_limit_exceeded"}}""", "5");

        Assert.Equal(Clock.GetUtcNow().AddSeconds(5), failure.Restriction!.Recovery.At);
    }

    [Theory] // a 402, and every shape of credit code or sentence, waits for a person whatever the status says
    [InlineData(HttpStatusCode.PaymentRequired, OpenRouterCredit)]
    [InlineData(HttpStatusCode.PaymentRequired, "")]
    [InlineData(HttpStatusCode.TooManyRequests, OpenAiNoQuota)]
    [InlineData(HttpStatusCode.BadRequest, AnthropicCredit)]
    [InlineData(HttpStatusCode.Forbidden, OpenAiNoQuota)]
    public void A_credit_failure_needs_an_owner(HttpStatusCode status, string body)
    {
        var failure = Read(status, body, "30");

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RestrictionCause.Credit, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Account, failure.Restriction.Subject);
        Assert.Equal(RecoveryKind.OwnerAction, failure.Restriction.Recovery.Kind);   // Retry-After never turns a balance into a wait
    }

    [Fact] // a 402 is a field that says credit, so the reason is a refill; the same words under a 400 are a sentence and say none
    public void A_402_is_a_refill_and_a_credit_sentence_alone_states_no_reason()
    {
        Assert.Equal(OwnerActionReason.Refill, Read(HttpStatusCode.PaymentRequired, "").Restriction!.Recovery.Reason);
        Assert.Equal(OwnerActionReason.Unknown, Read(HttpStatusCode.BadRequest, AnthropicCredit).Restriction!.Recovery.Reason);
    }

    [Theory] // a 401 states a refused credential; a 403 states no action, and the words read apart
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API key provided\"}}", AccessReason.CredentialRefused, AccessReason.CredentialRefused)]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"Please run /login\"}}", null, AccessReason.SignInAgain)]
    public void An_access_status_states_its_own_reading_and_the_words_theirs(HttpStatusCode status, string body, AccessReason? byField, AccessReason? byWords)
    {
        var failure = Read(status, body);

        Assert.Equal(AgentFailureKind.Access, failure.Kind);
        Assert.Equal(new AgentAccessReadings(byField, byWords), failure.AccessReadings);
    }

    [Fact]
    public void An_OpenRouter_429_reads_as_a_rate_limit_and_its_metadata_header_is_not_read()
    {
        var failure = Read(HttpStatusCode.TooManyRequests, OpenRouterRateLimit);

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(RestrictionCause.Rate, failure.Restriction!.Cause);
        Assert.Equal(RecoveryKind.Unknown, failure.Restriction.Recovery.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided: sk-***.","type":"invalid_request_error","code":"invalid_api_key"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"type":"error","error":{"type":"permission_error","message":"Your API key does not have permission to use the specified resource."}}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":401,"message":"No auth credentials found"}}""")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"type":"authentication_error","message":"x-api-key header is required"}}""")]
    public void An_access_failure_is_access_and_restricts_nothing(HttpStatusCode status, string body)
    {
        var failure = Read(status, body);

        Assert.Equal(AgentFailureKind.Access, failure.Kind);
        Assert.Null(failure.Restriction);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, AnthropicOverloaded)]   // a 429 whose type names overload still reads as the status does: a limit
    public void The_status_names_a_limit_before_the_type_names_overload(HttpStatusCode status, string body) =>
        Assert.Equal(AgentFailureKind.Limit, Read(status, body).Kind);

    [Fact]
    public void A_529_overloaded_is_transient_on_the_endpoint()
    {
        var failure = Read((HttpStatusCode)529, AnthropicOverloaded);

        Assert.Equal(AgentFailureKind.Transient, failure.Kind);
        Assert.Equal(RestrictionCause.Capacity, failure.Restriction!.Cause);
        Assert.Equal(RestrictionSubject.Endpoint, failure.Restriction.Subject);
        Assert.Equal(RecoveryKind.Unknown, failure.Restriction.Recovery.Kind);
    }

    [Theory] // the scope the envelope names: a model's capacity is the model's, not the endpoint's
    [InlineData("""{"error":{"message":"Selected model is at capacity. Please try a different model.","type":"server_error"}}""")]
    [InlineData("""{"error":{"message":"The model is busy","type":"model_overloaded"}}""")]
    public void A_model_at_capacity_restricts_the_model(string body)
    {
        var failure = Read(HttpStatusCode.ServiceUnavailable, body);

        Assert.Equal(AgentFailureKind.Transient, failure.Kind);
        Assert.Equal(RestrictionSubject.Model, failure.Restriction!.Subject);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, "")]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":{"message":"The server had an error while processing your request. Sorry about that!","type":"server_error"}}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>")]
    [InlineData(HttpStatusCode.GatewayTimeout, "")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"The engine is currently overloaded, please try again later","type":"engine_overloaded_error"}}""")]
    public void A_timeout_or_a_server_fault_is_transient(HttpStatusCode status, string body)
    {
        var failure = Read(status, body);

        Assert.Equal(AgentFailureKind.Transient, failure.Kind);
    }

    [Fact]
    public void An_overload_sentence_on_a_503_names_the_endpoint()
    {
        var failure = Read(HttpStatusCode.ServiceUnavailable, "The server is currently overloaded.");

        Assert.Equal(RestrictionSubject.Endpoint, failure.Restriction!.Subject);
    }

    [Fact]
    public void An_over_long_input_refused_as_a_500_is_not_a_fault_to_wait_out()
    {
        var failure = Read(HttpStatusCode.InternalServerError,
            """{"error":{"code":500,"message":"input (4096 tokens) is too large to process. increase the physical batch size (current batch size: 2048)","type":"server_error"}}""");

        Assert.Equal(AgentFailureKind.Fatal, failure.Kind);
        Assert.Null(failure.Restriction);
    }

    [Theory] // anything no entry reads is fatal and says which status decided it
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid value for 'temperature'","type":"invalid_request_error","code":"invalid_value"}}""", "http:400")]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"message":"The model `gpt-9` does not exist","type":"invalid_request_error","code":"model_not_found"}}""", "http:404")]
    [InlineData(HttpStatusCode.NotFound, "", "http:404")]
    [InlineData(HttpStatusCode.Redirect, "", "http:302")]
    public void An_unread_failure_is_fatal(HttpStatusCode status, string body, string by)
    {
        var failure = Read(status, body);

        Assert.Equal(AgentFailureKind.Fatal, failure.Kind);
        Assert.Equal(by, failure.By);
        Assert.Null(failure.Restriction);
    }

    [Theory] // the envelope's code and type are read before its sentence: a code the sentence would not match still decides
    [InlineData("""{"error":{"message":"Slow down.","code":"rate_limit_exceeded"}}""", AgentFailureKind.Limit, "http:code=rate_limit_exceeded")]
    [InlineData("""{"error":{"message":"Please sign in.","type":"authentication_error"}}""", AgentFailureKind.Access, "http:type=authentication_error")]
    [InlineData("""{"error":{"message":"Busy.","type":"overloaded_error"}}""", AgentFailureKind.Transient, "http:type=overloaded_error")]
    [InlineData("""{"error":{"message":"Out of money.","code":"insufficient_quota"}}""", AgentFailureKind.Limit, "http:code=insufficient_quota")]
    public void The_code_and_type_lead_the_sentence_in_band(string body, AgentFailureKind kind, string by)
    {
        var failure = Read(HttpStatusCode.OK, body);

        Assert.Equal(kind, failure.Kind);
        Assert.Equal(by, failure.By);
    }

    [Fact]
    public void A_code_leads_the_sentence_under_a_status_that_does_not_say()
    {
        var failure = Read(HttpStatusCode.BadRequest, """{"error":{"message":"Nope.","code":"Rate-Limit-Exceeded"}}""");

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
    }

    [Theory] // an error under a 2xx: OpenRouter and the gateways answer 200 and put the failure in the body
    [InlineData("""{"error":{"code":429,"message":"Rate limit exceeded: free-models-per-day"}}""", AgentFailureKind.Limit)]
    [InlineData("""{"error":{"code":402,"message":"Insufficient credits. Add more using https://openrouter.ai/credits"}}""", AgentFailureKind.Limit)]
    [InlineData("""{"error":"Incorrect API key provided"}""", AgentFailureKind.Access)]
    [InlineData("""{"error":{"code":502,"message":"Provider returned error"}}""", AgentFailureKind.Fatal)]
    public void An_error_in_a_2xx_body_is_read_as_the_same_failure(string body, AgentFailureKind kind)
    {
        var failure = Read(HttpStatusCode.OK, body);

        Assert.Equal(kind, failure.Kind);
    }

    [Fact]
    public void An_in_band_error_carries_a_credit_restriction_that_needs_an_owner()
    {
        var failure = Read(HttpStatusCode.OK, OpenRouterCredit);

        Assert.Equal(RestrictionCause.Credit, failure.Restriction!.Cause);
        Assert.Equal(RecoveryKind.OwnerAction, failure.Restriction.Recovery.Kind);
    }

    [Fact]
    public void A_numeric_in_band_code_is_not_taken_for_a_status()
    {
        // TASKS.md holds this unmeasured: the words decide here, so a 429 code over words no entry reads is fatal
        var failure = Read(HttpStatusCode.OK, """{"error":{"code":429,"message":"upstream said no"}}""");

        Assert.Equal(AgentFailureKind.Fatal, failure.Kind);
        Assert.Equal("http:in-band-error", failure.By);
    }

    [Theory] // a limit a model's own text merely quotes never classifies: only a top-level `error` member is evidence
    [InlineData("""{"id":"c1","choices":[{"index":0,"message":{"role":"assistant","content":"The API said: 429 Too Many Requests, rate limit exceeded, retry after 60s."},"finish_reason":"stop"}]}""")]
    [InlineData("""{"id":"c1","choices":[{"message":{"role":"assistant","content":"You've hit your usage limit. Incorrect API key provided. {\"error\":{\"code\":\"insufficient_quota\"}}"}}]}""")]
    [InlineData("""{"choices":[{"message":{"role":"tool","content":"{\"error\":{\"type\":\"overloaded_error\"}}"}}],"usage":{}}""")]
    [InlineData("""{"id":"m1","type":"message","content":[{"type":"text","text":"HTTP 429: rate limit exceeded"}],"error":null}""")]
    [InlineData("""{"error":{"message":"   "}}""")]
    [InlineData("{\"error\":")]
    [InlineData("""["error"]""")]
    [InlineData("data: [DONE]")]
    [InlineData("")]
    [InlineData(null)]
    public void A_2xx_that_carries_no_error_member_is_no_failure(string? body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.Null(ProviderVerdictClassifier.ReadHttpFailure(HttpStatusCode.OK, response.Headers, body, Clock));
    }

    [Fact]
    public void A_non_JSON_error_page_is_read_by_its_words_when_the_status_does_not_say()
    {
        var failure = Read(HttpStatusCode.BadRequest, "Too Many Requests: slow down, retry after 2 minutes");

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
        Assert.Equal(Clock.GetUtcNow().AddMinutes(2), failure.Restriction!.Recovery.At);
    }

    [Fact]
    public void The_failure_maps_onto_the_verdict_the_router_acts_on()
    {
        Assert.Equal(ProviderVerdict.RateLimited, ProviderVerdictClassifier.FromAgentFailure(Read(HttpStatusCode.TooManyRequests, OpenAiRateLimit)));
        Assert.Equal(ProviderVerdict.RateLimited, ProviderVerdictClassifier.FromAgentFailure(Read(HttpStatusCode.PaymentRequired, OpenRouterCredit)));
        Assert.Equal(ProviderVerdict.AuthFailed, ProviderVerdictClassifier.FromAgentFailure(Read(HttpStatusCode.Unauthorized, "")));
        Assert.Equal(ProviderVerdict.Failed, ProviderVerdictClassifier.FromAgentFailure(Read((HttpStatusCode)529, AnthropicOverloaded)));
    }

    [Fact]
    public void A_registered_matcher_still_reads_the_words_no_status_or_code_decided()
    {
        using var _ = ProviderVerdictClassifier.AddErrorTextMatcher(t => t.Contains("zu viele Anfragen") ? ProviderVerdict.RateLimited : null);

        var failure = Read(HttpStatusCode.BadRequest, """{"error":{"message":"zu viele Anfragen"}}""");

        Assert.Equal(AgentFailureKind.Limit, failure.Kind);
    }

    [Fact]
    public void The_delivery_is_left_to_the_door()
    {
        Assert.Equal(AgentDelivery.Uncertain, Read(HttpStatusCode.TooManyRequests, OpenAiRateLimit).Delivery);
    }
}
