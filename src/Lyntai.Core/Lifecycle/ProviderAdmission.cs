using System.Collections.Concurrent;

namespace Lyntai.Lifecycle;

/// <summary>How many calls may be in flight against one CONFIGURATION at a time.
///
/// <para>Limits are declared per SLOT because capacity is a property of the backend kind ("a local engine
/// renders one image at a time"), and enforced per KEY because the contended resource belongs to a
/// configuration. That split is what produces the behaviour worth having: two consumers pointing at the
/// same self-hosted engine share its capacity, while two tenants on different hosted endpoints never
/// throttle each other.</para>
///
/// <para><b>Why this is not a field on the provider.</b> A semaphore carried by an instance bounds nothing
/// once instances are per-call: under <see cref="TransientProviderPool{TProvider}"/> every call builds its
/// own limiter and every caller is admitted at once, so the engine the limit exists to protect thrashes
/// exactly as it would with no limit configured. Keyed and shared is the only shape that survives both
/// pooling strategies.</para>
///
/// <para>Distinct from rate limiting: a rate limiter bounds calls per unit TIME, this bounds calls at
/// once.</para></summary>
/// <param name="options">Limits. Null = unlimited everywhere.</param>
public sealed class ProviderAdmission(ProviderAdmissionOptions? options = null)
{
    private static readonly IDisposable Unlimited = new NoopHandle();

    private readonly ProviderAdmissionOptions _options = options ?? new ProviderAdmissionOptions();
    private readonly ConcurrentDictionary<ProviderKey, SemaphoreSlim> _gates = new();

    /// <summary>Wait for a permit for this configuration, then return the handle that releases it.
    ///
    /// <para>When the slot's limit is 0 (unlimited) this completes synchronously and returns a no-op
    /// handle — never null, so a call site is one <c>using</c> with no branch.</para></summary>
    /// <param name="key">The configuration being called.</param>
    /// <param name="ct">Cancels the wait.</param>
    public ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct = default)
    {
        var limit = LimitFor(key.Slot);
        if (limit <= 0) return ValueTask.FromResult(Unlimited);

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(limit, limit));
        var wait = gate.WaitAsync(ct);
        return wait.IsCompletedSuccessfully
            ? ValueTask.FromResult<IDisposable>(new Handle(gate))
            : Awaited(wait, gate);

        static async ValueTask<IDisposable> Awaited(Task wait, SemaphoreSlim gate)
        {
            await wait.ConfigureAwait(false);
            return new Handle(gate);
        }
    }

    private int LimitFor(string slot) =>
        _options.BySlot.TryGetValue(slot, out var limit) ? limit : _options.Default;

    /// <summary>Releases exactly once, however many times it is disposed — a double dispose would otherwise
    /// hand out a permit that was never taken, quietly raising the limit for the life of the process.</summary>
    private sealed class Handle(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }

    private sealed class NoopHandle : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Concurrency limits per backend. See <see cref="ProviderAdmission"/> for the slot/key split.</summary>
public sealed class ProviderAdmissionOptions
{
    /// <summary>Concurrent calls allowed per configuration when the slot has no entry in
    /// <see cref="BySlot"/>. 0 = unlimited, which is the default: the HTTP backends are stateless and a
    /// limit would only ever slow them down.</summary>
    public int Default { get; set; }

    /// <summary>Per-slot limits — the one that matters in practice is a locally-run engine, where several
    /// simultaneous renders contend for one CPU or GPU.</summary>
    public IDictionary<string, int> BySlot { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
