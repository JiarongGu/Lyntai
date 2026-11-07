namespace Lyntai.Memory.Engines;

/// <summary>A <see cref="GraphMemoryEngine"/>'s read-only view of one task. Holds no store and no writer of its own: every read is the
/// engine's own recall or expansion with its write-back switched off, so what it ranks and what it returns are what the engine would.
/// The engine it wraps is a reader's instance over the owner's store and the owner's engine name; the owner's instance is untouched.</summary>
internal sealed class GraphMemoryView(GraphMemoryEngine engine, string taskKey, IMemoryViewCheck? check) : IMemoryView
{
    /// <inheritdoc />
    public string TaskKey => taskKey;

    /// <inheritdoc />
    public IReadOnlyList<string> Engines { get; } = [engine.Name];

    /// <inheritdoc />
    public async Task<MemoryViewRecall> RecallAsync(MemoryViewQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var recall = await engine.ReadAsync(
            new MemoryQuery(taskKey, query.Scope, query.Query, query.Limit, query.CharBudget, query.RankingPolicyName, query.Detail)
            { Validity = query.Validity }, ct).ConfigureAwait(false);
        return Wrap(recall);
    }

    /// <inheritdoc />
    public async Task<MemoryViewRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, MemoryValidityReading validity = default, CancellationToken ct = default)
    {
        // asked BEFORE the store is touched: a refusal reads nothing, and a reference the host will not name is not looked up
        if (check is not null && !await check.PermitsAsync(reference, ct).ConfigureAwait(false))
            return MemoryViewRecall.Empty;
        return Wrap(await engine.ReadExpansionAsync(reference, taskKey, hops, charBudget, detail, validity, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<string?> RevisionOfAsync(MemoryRef reference, CancellationToken ct = default)
    {
        // a reference the host will not name reads as a missing one, as an expansion's does: a revision moving says the entry was written
        if (check is not null && !await check.PermitsAsync(reference, ct).ConfigureAwait(false)) return null;
        return await engine.ReadRevisionAsync(reference, taskKey, ct).ConfigureAwait(false);
    }

    // every read of the engine is bounded by the task it was asked for, so each item's source task is this view's
    private MemoryViewRecall Wrap(MemoryRecall recall)
    {
        if (recall.Items.Count == 0) return MemoryViewRecall.Empty;
        var now = engine.Now;
        return new MemoryViewRecall(
            [.. recall.Items.Select(i => new MemoryViewItem(i, taskKey, StandingOf(i.Validity, now)))], recall.Ran);
    }

    private static MemoryStanding StandingOf(MemoryValidity? validity, DateTimeOffset now) =>
        validity is null || validity.IsValidAt(now) ? MemoryStanding.Current
        : validity.From is { } from && from > now ? MemoryStanding.NotYetValid
        : MemoryStanding.Ended;
}
