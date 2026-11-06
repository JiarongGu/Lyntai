using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyntai.Agents;

namespace Lyntai.Inference;

public static partial class ProviderVerdictClassifier
{
    private static readonly FrozenSet<string> CreditCodes = new[]
    {
        "insufficient_quota", "insufficient_credits", "insufficient_funds", "payment_required",
        "billing_hard_limit_reached", "billing_not_active", "credit_balance_too_low",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> AccessCodes = new[]
    {
        "invalid_api_key", "invalid_authentication", "authentication_error", "permission_error", "permission_denied",
        "insufficient_permissions", "unauthorized", "forbidden",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> RateCodes = new[]
    {
        "rate_limit_exceeded", "rate_limit_error", "rate_limited", "too_many_requests", "throttled",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> OverloadedCodes = new[]
    {
        "overloaded_error", "overloaded", "server_overloaded", "model_overloaded",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TransientCodes = new[]
    {
        "server_error", "api_error", "internal_server_error", "service_unavailable", "timeout", "request_timeout",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] HttpDateFormats =
        ["r", "dddd, dd-MMM-yy HH:mm:ss 'GMT'", "ddd MMM d HH:mm:ss yyyy"];

    /// <summary>Read a failed HTTP response as an <see cref="AgentFailure"/> from its own facts: the status, the
    /// <c>Retry-After</c> header, and the JSON error envelope's <c>code</c> and <c>type</c> before its sentence.
    /// Pure: nothing is sent and nothing is kept.
    /// <list type="bullet">
    /// <item>402, or a credit code or sentence: a <see cref="AgentFailureKind.Limit"/> of
    /// <see cref="RestrictionCause.Credit"/> that needs <see cref="RecoveryKind.OwnerAction"/>.</item>
    /// <item>401 and 403, or an access code: <see cref="AgentFailureKind.Access"/>.</item>
    /// <item>429, or a rate code: a <see cref="AgentFailureKind.Limit"/> whose recovery is
    /// <see cref="RecoveryKind.RetryNotBefore"/> from <c>Retry-After</c> (seconds or an HTTP date), else the
    /// envelope's own stated moment, else <see cref="RecoveryKind.Unknown"/> — never a default.</item>
    /// <item>529 or an overloaded code, 408 and 5xx: <see cref="AgentFailureKind.Transient"/>; the restriction is
    /// <see cref="RestrictionSubject.Endpoint"/>, or <see cref="RestrictionSubject.Model"/> when the envelope names
    /// the model's capacity.</item>
    /// <item>Anything else is read as its sentence through <see cref="ReadAgentFailure"/>, and is
    /// <see cref="AgentFailureKind.Fatal"/> when no entry reads it.</item>
    /// </list>
    /// A 2xx answer is a failure only when its body's top-level <c>error</c> member says so (an in-band error, read
    /// the same way): the model's own text, which may quote any limit, is never read, and the result is null. A
    /// numeric <c>code</c> is never taken for an HTTP status (<c>TASKS.md</c> holds that unmeasured). The result's
    /// <see cref="AgentFailure.Delivery"/> is left to the door that knows what was sent.</summary>
    /// <param name="status">The response's status.</param>
    /// <param name="headers">The response's headers, or null; only <c>Retry-After</c> is read.</param>
    /// <param name="body">The response body, or null.</param>
    /// <param name="time">The clock a <c>Retry-After</c> in seconds counts from; null reads the system's.</param>
    /// <returns>The failure, or null for a 2xx response that carries no error.</returns>
    public static AgentFailure? ReadHttpFailure(HttpStatusCode status, HttpHeaders? headers, string? body, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var code = (int)status;
        var inBand = code is >= 200 and < 300;
        var envelope = HttpEnvelope.Read(body);
        if (inBand && !envelope.HasError) return null;

        var text = envelope.Message ?? (inBand ? "" : Head(body));

        var credit = code == 402 ? "http:402"
            : Named(envelope, CreditCodes) ?? (HttpCreditPattern().IsMatch(text) ? "sentence:http-credit" : null);
        if (credit is not null) return ReadLimit(text, time, credit, credit: true, creditByField: credit != "sentence:http-credit");

        var access = code is 401 or 403 ? $"http:{code}" : Named(envelope, AccessCodes);
        if (access is not null)
        {
            // a 401 and an invalid-key code state a refused credential; a 403 and the other codes state none
            var refused = code == 401 || envelope.Code == "invalid_api_key" || envelope.Type == "invalid_api_key";
            return new AgentFailure(AgentFailureKind.Access, access)
            {
                AccessReadings = new(refused ? AccessReason.CredentialRefused : null, ReadAccessWords(text)),
            };
        }

        var rate = code == 429 ? "http:429" : Named(envelope, RateCodes);
        if (rate is not null) return ReadLimit(text, time, rate, RetryAfter(headers, time));

        var overloaded = code == 529 ? "http:529" : Named(envelope, OverloadedCodes);
        if (overloaded is not null)
        {
            var model = envelope.Code == "model_overloaded" || envelope.Type == "model_overloaded" || CapacityPattern().IsMatch(text);
            return new AgentFailure(AgentFailureKind.Transient, overloaded)
            {
                Restriction = new(RestrictionCause.Capacity, model ? RestrictionSubject.Model : RestrictionSubject.Endpoint, RestrictionRecovery.Unknown),
            };
        }

        // before the 5xx entry: llama.cpp refuses an over-long input as a 500, and a longer wait does not cure it
        if (ContextWindowPattern().IsMatch(text)) return new AgentFailure(AgentFailureKind.Fatal, "sentence:context-window");

        var transient = code is 408 or >= 500 ? $"http:{code}" : Named(envelope, TransientCodes);
        if (transient is not null) return ReadTransient(text) ?? new AgentFailure(AgentFailureKind.Transient, transient);

        return ReadAgentFailure(text, time) ?? new AgentFailure(AgentFailureKind.Fatal, inBand ? "http:in-band-error" : $"http:{code}");
    }

    private static string? Named(HttpEnvelope envelope, FrozenSet<string> codes) =>
        envelope.Code is { } c && codes.Contains(c) ? $"http:code={c}"
        : envelope.Type is { } t && codes.Contains(t) ? $"http:type={t}"
        : null;

    private static string Head(string? body)
    {
        var trimmed = body?.Trim() ?? "";
        return trimmed.Length <= 4096 ? trimmed : trimmed[..4096];
    }

    // Retry-After is delta-seconds or an HTTP-date; a value that is neither states nothing
    private static RestrictionRecovery? RetryAfter(HttpHeaders? headers, TimeProvider time)
    {
        if (headers is null || !headers.TryGetValues("Retry-After", out var values)) return null;
        var value = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return RestrictionRecovery.RetryNotBefore(time.GetUtcNow().AddSeconds(seconds)) with
            {
                Stated = new StatedRecoveryParts(StatedTimeShape.Wait) { Wait = TimeSpan.FromSeconds(seconds) },
            };
        return DateTimeOffset.TryParseExact(value, HttpDateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var at)
            ? RestrictionRecovery.RetryNotBefore(at) with { Stated = new StatedRecoveryParts(StatedTimeShape.Moment) { Moment = at } }
            : null;
    }

    // the credit words a 4xx envelope carries that the limit sentences do not: Anthropic's 400 "credit balance is too
    // low", OpenRouter's "requires more credits"
    [GeneratedRegex(@"credit\s+balance\s+is\s+too\s+low|insufficient\s+(?:credits|funds)|requires\s+more\s+credits", RegexOptions.IgnoreCase)]
    private static partial Regex HttpCreditPattern();

    // the top-level `error` member of a body: OpenAI's and OpenRouter's object, Anthropic's `{"type":"error","error":{…}}`
    // and a bare string. A string code is read; a numeric one is the status's twin and is not
    private readonly record struct HttpEnvelope(bool HasError, string? Code, string? Type, string? Message)
    {
        public static HttpEnvelope Read(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return default;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("error", out var error))
                    return default;

                var envelope = error.ValueKind switch
                {
                    JsonValueKind.Null => default,
                    JsonValueKind.String => new HttpEnvelope(true, null, null, error.GetString()),
                    JsonValueKind.Object => new HttpEnvelope(true, Token(error, "code"), Token(error, "type"),
                        error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : error.GetRawText()),
                    _ => new HttpEnvelope(true, null, null, error.GetRawText()),
                };
                return string.IsNullOrWhiteSpace(envelope.Message) ? default : envelope;
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static string? Token(JsonElement error, string name)
        {
            if (!error.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
            var token = value.GetString()?.Trim().ToLowerInvariant().Replace('-', '_');
            return string.IsNullOrEmpty(token) || token.All(char.IsAsciiDigit) ? null : token;
        }
    }
}
