namespace Lyntai.Inference;

/// <summary>The cooldown-and-admission bookkeeping every router carries — one copy of what the three routers
/// share, which is not the merge <c>docs/DECISIONS.md</c> D153 refused: each keeps its own key SHAPE, and this
/// holds only the identity, the permit and the bench test.
///
/// <para><b>The configuration delegate must return a STABLE key for a given instance.</b> One attempt asks it
/// more than once — the bench check, admission, and the record that follows — so a varying answer records a
/// bench under a key nobody checks, a cooldown that silently never takes effect. Look the key up (as a pool
/// does); never recompute it from live state.</para></summary>
/// <param name="deadHosts">The shared tracker, or null for no cooldown.</param>
/// <param name="admission">Bounds concurrent attempts per configuration, or null for unbounded.</param>
/// <param name="configuration">Which configuration a provider runs under; null (or a null return) keys on its
/// id and applies no admission.</param>
/// <param name="scope">The domain prefix on every cooldown key (<c>generation::</c>, <c>vector::</c>), so one
/// domain's outage never benches another's backend sharing an id; empty for the text router's bare keys.</param>
internal sealed class RouterBookkeeping(
    DeadHostTracker? deadHosts,
    IProviderAdmission? admission,
    Func<IModelProvider, ProviderKey?>? configuration,
    string? scope = null)
{
    // resolved once: the no-delegate case must cost nothing per candidate, and a null-returning delegate must
    // be indistinguishable from no delegate at all
    private readonly Func<IModelProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    /// <summary>The shared tracker, or null.</summary>
    public DeadHostTracker? DeadHosts => deadHosts;

    /// <summary>A provider's cooldown key: its CONFIGURATION when one is known, else its id — so two
    /// configurations of one backend bench independently while two consumers of one downed host share a bench —
    /// behind the domain's prefix.</summary>
    public string Key(IModelProvider provider) =>
        scope + (_configuration(provider)?.ToString() ?? provider.Id);

    /// <summary>Take a concurrency permit for this provider's configuration, or nothing when no admission is
    /// wired or the configuration is unknown. Scope the result with <c>using</c> on EVERY path: a permit that is
    /// not returned pins its gate for the life of the process.</summary>
    public async ValueTask<IDisposable?> EnterAsync(IModelProvider provider, CancellationToken ct) =>
        admission is not null && _configuration(provider) is { } key
            ? await admission.EnterAsync(key, ct).ConfigureAwait(false)
            : null;

    /// <summary>Whether <paramref name="key"/> is benched — never the sole candidate when the policy exempts it,
    /// because benching the only option just guarantees a synthetic failure.</summary>
    public bool IsBenched(string key, bool soleCandidate, bool exemptSoleCandidate) =>
        deadHosts is not null && !(soleCandidate && exemptSoleCandidate) && deadHosts.IsDead(key);

    /// <summary>The host penalty a fallback action carries once a candidate is done with: a bench for
    /// <see cref="FallbackAction.CooldownAndAdvance"/>, one strike for
    /// <see cref="FallbackAction.PenalizeAndAdvance"/>, nothing otherwise.</summary>
    public void Penalize(string key, FallbackAction action)
    {
        if (action == FallbackAction.CooldownAndAdvance) deadHosts?.MarkDead(key);
        else if (action == FallbackAction.PenalizeAndAdvance) deadHosts?.RecordFailure(key);
    }
}
