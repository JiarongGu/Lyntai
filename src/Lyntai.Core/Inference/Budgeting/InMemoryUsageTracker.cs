using Lyntai.Inference;
namespace Lyntai.Inference.Budgeting;

/// <summary>A process-local <see cref="IUsageTracker"/>: per-consumer running totals plus a global sum,
/// guarded by a single lock (usage recording is low-frequency relative to the calls it meters); every
/// member completes synchronously. It can reserve (<see cref="IReservingUsageTracker"/>): the check and the hold
/// happen under that same lock, so a hard cap holds across every caller in this process. For spend that must survive a restart or be shared across processes,
/// register your own <see cref="IUsageTracker"/> instead.</summary>
public sealed class InMemoryUsageTracker : IReservingUsageTracker
{
    private sealed class Totals { public long In, Out, Calls; public double Cost; }

    private readonly object _gate = new();
    // case-INSENSITIVE, matching every consumer-keyed options map (Budget/RateLimit PerConsumer,
    // DefaultModelByConsumer, …): "App" and "app" are ONE consumer identity, so a mixed-casing app can't
    // silently accrue two separate totals against one cap. The SQL trackers keep per-casing ROWS (their
    // TEXT PK) but AGGREGATE reads case-insensitively — totals agree across backends.
    private readonly Dictionary<string, Totals> _byConsumer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Totals _global = new();
    private readonly Dictionary<string, UsageReservation> _holds = new(StringComparer.Ordinal);

    public ValueTask RecordAsync(string consumer, ProviderUsage usage, CancellationToken ct = default)
    {
        lock (_gate) RecordLocked(consumer, usage);
        return ValueTask.CompletedTask;
    }

    private void RecordLocked(string consumer, ProviderUsage usage)
    {
        var t = Get(consumer);
        t.In += usage.InputTokens; t.Out += usage.OutputTokens; t.Cost += usage.CostUsd ?? 0; t.Calls++;
        _global.In += usage.InputTokens; _global.Out += usage.OutputTokens;
        _global.Cost += usage.CostUsd ?? 0; _global.Calls++;
    }

    public ValueTask<UsageTotals> TotalAsync(string? consumer = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var t = consumer is null ? _global : (_byConsumer.TryGetValue(consumer, out var c) ? c : null);
            return ValueTask.FromResult(t is null ? UsageTotals.Empty : new UsageTotals(t.In, t.Out, t.Cost, t.Calls));
        }
    }

    public ValueTask ResetAsync(string? consumer = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (consumer is null)
            {
                _byConsumer.Clear();
                _global.In = _global.Out = _global.Calls = 0; _global.Cost = 0;
            }
            else if (_byConsumer.Remove(consumer, out var t))
            {
                // subtract the consumer's contribution from the global running total
                _global.In -= t.In; _global.Out -= t.Out; _global.Cost -= t.Cost; _global.Calls -= t.Calls;
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<UsageReservationResult> ReserveAsync(UsageReservationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            foreach (var expired in _holds.Where(h => h.Value.ExpiresAt <= now).Select(h => h.Key).ToList())
                _holds.Remove(expired);

            var global = Load(null);
            var mine = Load(request.Consumer);
            if (request.Caps.ExceededBy(global, mine, request.Amount) is { } cap)
                return ValueTask.FromResult(new UsageReservationResult(null, cap));

            var held = new UsageReservation(Guid.NewGuid().ToString("N"), request.Consumer, request.Amount, request.ExpiresAt);
            _holds[held.Id] = held;
            return ValueTask.FromResult(new UsageReservationResult(held));
        }
    }

    public ValueTask ReconcileAsync(UsageReservation reservation, ProviderUsage? actual, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (_gate)
        {
            _holds.Remove(reservation.Id);
            if (actual is not null) RecordLocked(reservation.Consumer, actual);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Committed spend plus live holds, for one consumer or (null) everyone. Caller holds the lock.</summary>
    private UsageTotals Load(string? consumer)
    {
        var spent = consumer is null ? _global : (_byConsumer.TryGetValue(consumer, out var c) ? c : null);
        long tokensIn = spent?.In ?? 0, tokensOut = spent?.Out ?? 0;
        var cost = spent?.Cost ?? 0;
        foreach (var hold in _holds.Values)
        {
            if (consumer is not null && !string.Equals(hold.Consumer, consumer, StringComparison.OrdinalIgnoreCase)) continue;
            tokensIn += hold.Amount.InputTokens; tokensOut += hold.Amount.OutputTokens; cost += hold.Amount.CostUsd ?? 0;
        }
        return new UsageTotals(tokensIn, tokensOut, cost, 0);
    }

    private Totals Get(string consumer)
    {
        if (!_byConsumer.TryGetValue(consumer, out var t)) _byConsumer[consumer] = t = new Totals();
        return t;
    }
}
