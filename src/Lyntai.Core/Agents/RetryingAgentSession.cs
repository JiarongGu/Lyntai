using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Lyntai.Agents;

/// <summary>Retries a turn that failed transiently, once per attempt the bound allows. Off unless registered
/// (<c>AddAgentRetry</c>): whether a deployment may spend a second try is its own to say.
/// <list type="bullet">
/// <item><description>Only <see cref="AgentFailureKind.Transient"/> is retried. A limit, an access failure, a
/// fatal one and a failure with no <see cref="SessionEnded.Failure"/> pass through untouched.</description></item>
/// <item><description><see cref="AgentDelivery.NeverSent"/>: the same prompt is sent again.
/// <see cref="AgentDelivery.Taken"/>: the conversation is resumed with
/// <see cref="AgentRetryOptions.ContinueWord"/>, from the failed attempt's session id — and with none to resume,
/// the turn is not retried, because sending the prompt again would send it twice.
/// <see cref="AgentDelivery.Uncertain"/>: never retried, as a tool step may already have acted.</description></item>
/// <item><description>The wait is <see cref="AgentRetryOptions.InitialDelay"/> doubled per retry up to
/// <see cref="AgentRetryOptions.MaxDelay"/>, or a stated retry-not-before moment if that is later; it runs on the
/// injected <see cref="TimeProvider"/> and a cancellation cuts it.</description></item>
/// </list>
/// <para><b>What the stream shows.</b> Events are forwarded as they arrive, so a retried turn's stream holds the
/// failed attempt's events and then the next attempt's (a second <see cref="SessionStarted"/>, for one). Only a
/// failed terminal that is about to be retried is withheld: the caller sees exactly one <see cref="SessionEnded"/>,
/// the last attempt's. When a transient failure is NOT retried, that terminal's
/// <see cref="SessionEnded.Diagnostic"/> says why.</para>
/// <para><b>One layer owns the budget.</b> Wrapping a <see cref="RetryingAgentSession"/> in another throws, since
/// two layers would spend <see cref="AgentRetryOptions.MaxRetries"/> twice over.</para></summary>
public sealed class RetryingAgentSession : IAgentSession
{
    private readonly IAgentSession _inner;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly string _continueWord;
    private readonly TimeProvider _time;
    private readonly ILogger<RetryingAgentSession>? _logger;

    /// <param name="inner">The session to retry; not itself a <see cref="RetryingAgentSession"/>.</param>
    /// <param name="options">The bound, read once here.</param>
    /// <param name="time">The clock the waits run on; null = the system clock.</param>
    /// <param name="logger">Where each retry is reported; null = silent.</param>
    /// <exception cref="InvalidOperationException"><paramref name="inner"/> is already a
    /// <see cref="RetryingAgentSession"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A bound is negative, or <c>MaxDelay</c> is under
    /// <c>InitialDelay</c>.</exception>
    public RetryingAgentSession(IAgentSession inner, AgentRetryOptions options, TimeProvider? time = null,
        ILogger<RetryingAgentSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        if (inner is RetryingAgentSession)
            throw new InvalidOperationException(
                $"{nameof(RetryingAgentSession)} was handed another {nameof(RetryingAgentSession)}: one layer owns " +
                "the retry budget, and two would spend it twice. Drop one — AddAgentRetry already wraps every " +
                "registered session, so do not also construct one by hand.");
        options.Validate();
        _inner = inner;
        _maxRetries = options.MaxRetries;
        _initialDelay = options.InitialDelay;
        _maxDelay = options.MaxDelay;
        _continueWord = options.ContinueWord;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var attempt = options;
        var conversation = options.ResumeToken;

        for (var retries = 0; ; retries++)
        {
            AgentSessionOptions? next = null;
            var wait = TimeSpan.Zero;
            var terminalSeen = false;

            await foreach (var e in _inner.StreamAsync(attempt, ct).WithCancellation(ct).ConfigureAwait(false))
            {
                if (e is SessionStarted started) conversation = started.SessionId;
                if (e is SessionEnded ended && !terminalSeen)
                {
                    terminalSeen = true;
                    conversation = ended.SessionId ?? conversation;
                    var (retry, delay, final) = Decide(ended, attempt, conversation, retries);
                    if (retry is null) yield return final;
                    else (next, wait) = (retry, delay);
                    continue;
                }
                yield return e;
            }

            if (next is null) yield break;

            _logger?.LogInformation(
                "Agent turn failed transiently; retry {Retry} of {Max} in {Wait} ({Prompt})",
                retries + 1, _maxRetries, wait, ReferenceEquals(next, attempt) ? "same prompt" : "resumed");
            await Task.Delay(wait, _time, ct).ConfigureAwait(false);
            attempt = next;
        }
    }

    // The disposition table. A null retry means the terminal stands, annotated when it was a transient failure
    // that could have been retried; otherwise the options of the next attempt and the wait before it.
    private (AgentSessionOptions? Retry, TimeSpan Wait, SessionEnded Final) Decide(
        SessionEnded ended, AgentSessionOptions attempt, string? conversation, int retries)
    {
        if (!ended.IsError || ended.Failure is not { Kind: AgentFailureKind.Transient } failure)
            return (null, default, ended);

        // Uncertain is also what an unrecognised delivery reads as: never retry a turn we cannot show was idle.
        AgentSessionOptions next;
        switch (failure.Delivery)
        {
            case AgentDelivery.NeverSent:
                next = attempt;
                break;
            case AgentDelivery.Taken:
                if (conversation is null)
                    return Refuse(ended, "the prompt was taken but the door gave no session to resume");
                next = attempt with { Prompt = _continueWord, ResumeToken = conversation };
                break;
            default:
                return Refuse(ended, "delivery uncertain, so a tool step may already have acted");
        }

        if (retries >= _maxRetries)
            return Refuse(ended, $"the retry budget of {_maxRetries} is spent");

        var wait = TimeSpan.FromMilliseconds(Math.Min(
            _maxDelay.TotalMilliseconds, _initialDelay.TotalMilliseconds * Math.Pow(2, retries)));
        if (failure.Restriction?.Recovery is { Kind: RecoveryKind.RetryNotBefore, At: { } at })
        {
            var asked = at - _time.GetUtcNow();
            if (asked > _maxDelay)
                return Refuse(ended, $"the service asked for no retry for {asked}, past the {_maxDelay} bound");
            if (asked > wait) wait = asked;
        }
        return (next, wait, ended);
    }

    private static (AgentSessionOptions?, TimeSpan, SessionEnded) Refuse(SessionEnded ended, string why)
    {
        var note = $"not retried: {why}";
        return (null, default, ended with
        {
            Diagnostic = string.IsNullOrEmpty(ended.Diagnostic) ? note : $"{ended.Diagnostic} ({note})",
        });
    }
}
