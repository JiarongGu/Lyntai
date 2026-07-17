namespace Lyntai.Llm.Budgeting;

/// <summary>A process-local <see cref="IUsageTracker"/>: per-consumer running totals plus a global sum,
/// guarded by a single lock (usage recording is low-frequency relative to the calls it meters). For spend
/// that must survive a restart or be shared across processes, register your own <see cref="IUsageTracker"/>
/// instead.</summary>
public sealed class InMemoryUsageTracker : IUsageTracker
{
    private sealed class Totals { public long In, Out, Calls; public double Cost; }

    private readonly object _gate = new();
    private readonly Dictionary<string, Totals> _byConsumer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Totals _global = new();

    public void Record(string consumer, LlmUsage usage)
    {
        lock (_gate)
        {
            var t = Get(consumer);
            t.In += usage.InputTokens; t.Out += usage.OutputTokens; t.Cost += usage.CostUsd ?? 0; t.Calls++;
            _global.In += usage.InputTokens; _global.Out += usage.OutputTokens;
            _global.Cost += usage.CostUsd ?? 0; _global.Calls++;
        }
    }

    public UsageTotals Total(string? consumer = null)
    {
        lock (_gate)
        {
            var t = consumer is null ? _global : (_byConsumer.TryGetValue(consumer, out var c) ? c : null);
            return t is null ? UsageTotals.Empty : new UsageTotals(t.In, t.Out, t.Cost, t.Calls);
        }
    }

    public void Reset(string? consumer = null)
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
    }

    private Totals Get(string consumer)
    {
        if (!_byConsumer.TryGetValue(consumer, out var t)) _byConsumer[consumer] = t = new Totals();
        return t;
    }
}
