namespace Lyntai.Agents;

/// <summary>The bound on <see cref="RetryingAgentSession"/> (<c>AddAgentRetry</c>): how many more attempts a
/// transient failure may buy, how long to wait before each, and what to say when the turn is resumed. Read once
/// when the session is built, so a change after that does not reach it.</summary>
public sealed class AgentRetryOptions
{
    /// <summary>The most attempts after the first one a single turn may spend. Default 2; 0 retries nothing.
    /// This is the turn's whole budget: nothing else in a retried turn may spend a second one.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The wait before the first retry; each later one doubles it, up to <see cref="MaxDelay"/>.
    /// Default 2 seconds.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest a single wait may be. Default 30 seconds. A door that states a retry-not-before
    /// moment further off than this is NOT waited for: the turn is returned failed and the moment is in its
    /// <see cref="SessionEnded.Failure"/>, for the host to act on.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The prompt that resumes a turn whose original prompt the door had already taken. Default
    /// <c>"continue"</c>; a host whose agent wants other words sets them.</summary>
    public string ContinueWord { get; set; } = "continue";

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetries, nameof(MaxRetries));
        ArgumentOutOfRangeException.ThrowIfLessThan(InitialDelay, TimeSpan.Zero, nameof(InitialDelay));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDelay, InitialDelay, nameof(MaxDelay));
        ArgumentException.ThrowIfNullOrWhiteSpace(ContinueWord, nameof(ContinueWord));
    }
}
