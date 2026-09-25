using System.Net;
using Lyntai.Inference;

namespace Lyntai.Generation.Providers;

/// <summary>The scaffolding a queue backend's calls share: the deadline each runs under and what a fired one
/// reports, the GET that tells "the queue did not answer" from "the queue answered no", and how a failed poll,
/// fetch or cancel reads. Which statuses count as TRANSPORT stays each backend's own rule — a loopback server and
/// a paid, rate-limiting API do not agree on it, and a copy of one backend's rule is how a single 429
/// dead-lettered a render that was already billed.</summary>
internal static class QueueCalls
{
    /// <summary>A status call under <paramref name="budget"/>. A fired deadline reports the operation still
    /// RUNNING: no answer is not a failed render, and reading it as terminal abandons one already paid for.</summary>
    public static Task<QueuedOperation> PollGuard(TimeSpan budget, string operationId, CancellationToken ct,
        Func<CancellationToken, Task<QueuedOperation>> call) =>
        GenerationDeadline.GuardAsync(budget, ct, call,
            reason => new QueuedOperation(operationId, QueuedOperationStatus.Running,
                Detail: $"the status call {reason} — the render is still assumed to be running"));

    /// <summary>A result fetch under <paramref name="budget"/>. A fired deadline is a
    /// <see cref="ProviderVerdict.Timeout"/> result, and the operation can simply be fetched again.</summary>
    public static Task<MediaResponse> FetchGuard(TimeSpan budget, CancellationToken ct,
        Func<CancellationToken, Task<MediaResponse>> call) =>
        GenerationDeadline.GuardAsync(budget, ct, call,
            reason => MediaResponse.Failure(ProviderVerdict.Timeout, $"the result fetch {reason}"));

    /// <summary>A cancel under <paramref name="budget"/>. One that timed out may or may not have landed, so the
    /// render is reported still running rather than assumed stopped.</summary>
    public static Task<QueuedOperation> CancelGuard(TimeSpan budget, string operationId, CancellationToken ct,
        Func<CancellationToken, Task<QueuedOperation>> call) =>
        GenerationDeadline.GuardAsync(budget, ct, call,
            reason => new QueuedOperation(operationId, QueuedOperationStatus.Running, Detail: $"the cancel {reason}"));

    /// <summary>Send <paramref name="message"/> and read the answer. <c>Transport</c> marks a failure that says
    /// NOTHING about the operation — the queue unreachable, or a status <paramref name="isTransport"/> accepts —
    /// as against one saying it will never resolve. <c>Status</c> carries the typed status back, which
    /// <see cref="ProviderVerdictClassifier"/> weighs above body text.</summary>
    /// <param name="http">The call's client.</param>
    /// <param name="message">The request, authorized.</param>
    /// <param name="isTransport">Which failing statuses leave the operation alive — the backend's own rule.</param>
    /// <param name="unreachable">The failure text for a request that never reached the queue.</param>
    /// <param name="ct">The caller's token, composed with the call's deadline.</param>
    public static async Task<(string? Body, string? Failure, bool Transport, HttpStatusCode? Status)> SendAsync(
        HttpClient http, HttpRequestMessage message, Func<HttpStatusCode, bool> isTransport,
        Func<HttpRequestException, string> unreachable, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (body, null, false, response.StatusCode)
                : (null, $"{(int)response.StatusCode}: {HttpArtifacts.FailureDetail(body)}",
                   isTransport(response.StatusCode), response.StatusCode);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return (null, unreachable(ex), true, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false, null);
        }
    }

    /// <summary>A failed status read: still RUNNING when it was transport, FAILED when the queue answered that
    /// the operation will never resolve.</summary>
    public static QueuedOperation PollFailure(string operationId, string failure, bool transport) =>
        new(operationId, transport ? QueuedOperationStatus.Running : QueuedOperationStatus.Failed, Detail: failure);

    /// <summary>A failed fetch, CLASSIFIED — by the typed status where there is one, since a status line is not
    /// the vocabulary <see cref="ProviderVerdictClassifier.FromErrorText"/> matches on.</summary>
    public static MediaResponse FetchFailure(HttpStatusCode? status, string failure, bool hasCredentials) =>
        MediaResponse.Failure(
            status is { } code
                ? ProviderVerdictClassifier.FromHttpFailure(code, failure, hasCredentials)
                : ProviderVerdictClassifier.FromErrorText(failure),
            failure);

    /// <summary>What a cancel the queue answered reports: <see cref="QueuedOperationStatus.Cancelled"/> on success,
    /// still RUNNING when rejected — a render already running may not be cancellable.</summary>
    public static QueuedOperation CancelAnswered(string operationId, HttpStatusCode status, string? detail = null) =>
        (int)status is >= 200 and < 300
            ? new QueuedOperation(operationId, QueuedOperationStatus.Cancelled, Detail: detail)
            : new QueuedOperation(operationId, QueuedOperationStatus.Running, Detail: $"cancel rejected: {(int)status}");

    /// <summary>A cancel that never got an answer: it may or may not have landed, so the render is still RUNNING.</summary>
    public static QueuedOperation CancelUnanswered(string operationId, Exception ex) =>
        new(operationId, QueuedOperationStatus.Running, Detail: ex.Message);

    /// <summary>A submission that failed, with no operation to name.</summary>
    public static QueuedOperation Failed(string detail) => new("", QueuedOperationStatus.Failed, Detail: detail);
}
