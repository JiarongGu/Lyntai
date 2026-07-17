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

    /// <summary>Hard failure — terminal, no more retries.</summary>
    public static JobOutcome Fail(string error) => new(Kind.Fail, error: error);
}
