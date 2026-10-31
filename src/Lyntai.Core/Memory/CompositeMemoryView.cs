namespace Lyntai.Memory;

/// <summary>A blend's view of one task: its members' views side by side. A recall asks each and cuts the union as the blend's own
/// recall does (authoritative first, then relevance, then the character budget); an expansion and a revision go to the one view whose
/// engines include the reference's, and a reference no view covers reads as a missing one.</summary>
internal sealed class CompositeMemoryView(string taskKey, IReadOnlyList<IMemoryView> views) : IMemoryView
{
    /// <inheritdoc />
    public string TaskKey => taskKey;

    /// <inheritdoc />
    public IReadOnlyList<string> Engines { get; } = [.. views.SelectMany(v => v.Engines)];

    /// <inheritdoc />
    public async Task<MemoryViewRecall> RecallAsync(MemoryViewQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = new List<MemoryViewItem>();
        var ran = MemorySources.None;
        foreach (var view in views)
        {
            ct.ThrowIfCancellationRequested();
            var recall = await view.RecallAsync(query, ct).ConfigureAwait(false);
            items.AddRange(recall.Items);
            ran |= recall.Ran;
        }
        if (query.Limit is { } limit && limit > 0 && items.Count > limit)
            items = [.. items.OrderByDescending(i => i.Item.Grade == MemoryGrade.Authoritative)
                .ThenByDescending(i => i.Item.Relevance).Take(limit)];
        if (query.CharBudget is { } budget && budget > 0)
        {
            var kept = MemoryBudget.Cut([.. items.Select(i => i.Item)], budget).Select(i => i.Reference).ToHashSet();
            items = [.. items.Where(i => kept.Contains(i.Item.Reference))];
        }
        return items.Count == 0 ? MemoryViewRecall.Empty : new MemoryViewRecall(items, ran);
    }

    /// <inheritdoc />
    public Task<MemoryViewRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, MemoryValidityReading validity = default, CancellationToken ct = default) =>
        Covering(reference) is { } view
            ? view.ExpandAsync(reference, hops, charBudget, detail, validity, ct)
            : Task.FromResult(MemoryViewRecall.Empty);

    /// <inheritdoc />
    public Task<string?> RevisionOfAsync(MemoryRef reference, CancellationToken ct = default) =>
        Covering(reference) is { } view ? view.RevisionOfAsync(reference, ct) : Task.FromResult<string?>(null);

    private IMemoryView? Covering(MemoryRef reference) =>
        views.FirstOrDefault(v => v.Engines.Contains(reference.Engine, StringComparer.Ordinal));
}
