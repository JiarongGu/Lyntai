namespace Lyntai.Inference;

/// <summary>Text providers registered, replaced and unregistered while the app runs, beside the container's own —
/// the endpoints an app's users add and edit. Unlike the library's other registries, which are read-only lookups,
/// this one is edited at run time. Opted into with <c>UseTextProviderRegistry()</c>.
///
/// <para><b>The DEFAULT text client serves them</b>: its router reads one snapshot of this registry per call, so
/// everything folded onto that client — the usage budget, the response cache, the rate limit, refusal screening —
/// applies to a registered provider as to a container one, and so does every library caller that uses it. A call
/// already routing when an edit lands finishes on the snapshot it started with.</para>
///
/// <para><b>Two limits, deliberately.</b> A named client (<c>AddTextClient</c>) is bound to its providers at
/// composition and never sees this registry. And the response cache keys on the model, not the backend, so an
/// endpoint edited under the same id and model may serve replies cached before the edit until they expire.</para></summary>
public interface ITextProviderRegistry
{
    /// <summary>The configuration of every registered provider, in registration order.</summary>
    IReadOnlyList<ProviderKey> Registered { get; }

    /// <summary>Register a provider, or replace the one registered under its id. It is built at once through the
    /// provider pool, so a mistake fails here rather than at a later call; a replaced configuration is retired from
    /// the pool without being disposed, so a call already running on it finishes.</summary>
    /// <exception cref="InvalidOperationException">A container provider already has that id.</exception>
    /// <exception cref="ArgumentException">The built provider's id is not the key's slot, or it produces no text —
    /// either way it could never be routed to.</exception>
    void Register(ProviderRegistration<IModelProvider> registration);

    /// <summary>Unregister the provider of that id; true when there was one.</summary>
    bool Unregister(string providerId);

    /// <summary>The fallback order the default text client uses when neither the request nor a live route names
    /// candidates; null means <c>LyntaiOptions.DefaultCandidates</c>.</summary>
    IReadOnlyList<ProviderCandidate>? DefaultCandidates { get; }

    /// <summary>Set the default client's fallback order — a registered id, a container one, or both. Null restores
    /// <c>LyntaiOptions.DefaultCandidates</c>.</summary>
    void SetDefaultCandidates(IReadOnlyList<ProviderCandidate>? candidates);
}

/// <summary>The shipped <see cref="ITextProviderRegistry"/>: every edit publishes an immutable snapshot — the merged id
/// table the default router reads, the registered keys, the default candidates — so a reader never sees half an
/// edit. Edits serialize on one lock; reads take none.</summary>
internal sealed class TextProviderRegistry(IEnumerable<IModelProvider> container, IProviderPool<IModelProvider> pool)
    : ITextProviderRegistry
{
    private readonly IReadOnlyList<IModelProvider> _container = [.. container];
    private readonly Lock _gate = new();
    private readonly List<(ProviderKey Key, IModelProvider Provider)> _registered = [];
    private Snapshot? _snapshot;

    private sealed record Snapshot(
        IReadOnlyDictionary<string, IModelProvider> ById, IReadOnlyList<ProviderKey> Registered,
        IReadOnlyList<ProviderCandidate>? DefaultCandidates);

    private Snapshot Current => Volatile.Read(ref _snapshot) ?? Initialize();

    /// <summary>The id → provider table the default router reads, container and registered providers together.</summary>
    public IReadOnlyDictionary<string, IModelProvider> Lookup() => Current.ById;

    public IReadOnlyList<ProviderKey> Registered => Current.Registered;

    public IReadOnlyList<ProviderCandidate>? DefaultCandidates => Current.DefaultCandidates;

    public void Register(ProviderRegistration<IModelProvider> registration)
    {
        ArgumentNullException.ThrowIfNull(registration.Create);
        lock (_gate)
        {
            var slot = registration.Key.Slot;
            if (_container.Any(p => string.Equals(p.Id, slot, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"A provider registered at composition already has the id '{slot}'; a run-time registration "
                    + "cannot share it, because a router matches candidates on the id.");

            var provider = pool.GetOrAdd(registration.Key, registration.Create);
            try
            {
                ProviderPoolGuard.EnsureIdMatchesSlot(provider, registration.Key);
                if (!ClientCandidates.ServesText(provider))
                    throw new ArgumentException(
                        $"The provider built for '{slot}' produces no text, so a text router could never select it.",
                        nameof(registration));
            }
            catch
            {
                pool.Retire(registration.Key);
                throw;
            }

            var at = _registered.FindIndex(r => string.Equals(r.Key.Slot, slot, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                if (_registered[at].Key != registration.Key) pool.Retire(_registered[at].Key);
                _registered[at] = (registration.Key, provider);
            }
            else
            {
                _registered.Add((registration.Key, provider));
            }
            Publish(_snapshot?.DefaultCandidates);
        }
    }

    public bool Unregister(string providerId)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        lock (_gate)
        {
            var at = _registered.FindIndex(r => string.Equals(r.Key.Slot, providerId, StringComparison.OrdinalIgnoreCase));
            if (at < 0) return false;
            pool.Retire(_registered[at].Key);
            _registered.RemoveAt(at);
            Publish(_snapshot?.DefaultCandidates);
            return true;
        }
    }

    public void SetDefaultCandidates(IReadOnlyList<ProviderCandidate>? candidates)
    {
        lock (_gate) Publish(candidates is null ? null : [.. candidates]);
    }

    private Snapshot Initialize()
    {
        lock (_gate) return _snapshot ?? Publish(null);
    }

    // the caller holds _gate: the lock is never re-entered
    private Snapshot Publish(IReadOnlyList<ProviderCandidate>? defaults)
    {
        var snapshot = new Snapshot(
            ProviderLookup.ById([.. _container, .. _registered.Select(r => r.Provider)]),
            [.. _registered.Select(r => r.Key)], defaults);
        Volatile.Write(ref _snapshot, snapshot);
        return snapshot;
    }
}
