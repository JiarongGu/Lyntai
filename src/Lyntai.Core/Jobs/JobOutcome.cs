namespace Lyntai.Jobs;

/// <summary>What an <see cref="IJobHandler"/> returns: finished, wants a retry, or a hard failure.</summary>
public sealed record JobOutcome
{
    public enum Kind
    {
        /// <summary>Done — mark the job Succeeded.</summary>
        Complete,

        /// <summary>Try again later (up to the job's max attempts), optionally after <see cref="RetryDelay"/>.</summary>
        Retry,

        /// <summary>Not finished, and nothing went wrong — come back and look again, WITHOUT spending an
        /// attempt. For a handler that watches a long-running operation somewhere else (a hosted render, a
        /// remote queue) rather than doing the work itself.
        /// <para>This is not <see cref="Retry"/>, and the difference is the whole reason it exists: a retry is
        /// a FAILED attempt worth repeating and is bounded by <c>MaxAttempts</c>, so expressing polling as a
        /// retry dead-letters a healthy operation after a handful of looks. Polling is bounded by the caller's
        /// own patience instead — an operation that never finishes is ended by cancellation, a deadline, or
        /// the handler returning <see cref="Fail"/>, never by the attempt counter.</para></summary>
        Poll,

        /// <summary>Give up now — mark the job Failed regardless of attempts left.</summary>
        Fail,
    }

    private JobOutcome(Kind result, TimeSpan? retryDelay = null, string? error = null)
    {
        Result = result;
        RetryDelay = retryDelay;
        Error = error;
    }

    public Kind Result { get; }
    public TimeSpan? RetryDelay { get; }
    public string? Error { get; }

    /// <summary>The job finished successfully.</summary>
    public static JobOutcome Complete { get; } = new(Kind.Complete);

    /// <summary>Retry later (bounded by the job's max attempts). Null delay uses the configured backoff.</summary>
    public static JobOutcome Retry(TimeSpan? delay = null) => new(Kind.Retry, retryDelay: delay);

    /// <summary>Come back and look again, NOT counted against the job's max attempts. Null delay uses the
    /// configured backoff. Use this — never <see cref="Retry"/> — while waiting on an operation that is
    /// progressing normally somewhere else.</summary>
    public static JobOutcome Poll(TimeSpan? delay = null) => new(Kind.Poll, retryDelay: delay);

    /// <summary>Hard failure — terminal, no more retries.</summary>
    public static JobOutcome Fail(string error) => new(Kind.Fail, error: error);
}
