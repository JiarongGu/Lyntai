namespace Lyntai.Memory;

/// <summary>The ONE character-budget cut a recall or an expansion applies to what it returns, so a blend and a
/// bare engine — and a recall and an expansion — cannot answer the same budget differently.</summary>
internal static class MemoryBudget
{
    /// <summary>Keep <paramref name="items"/>, in order, within <paramref name="budget"/> characters, costing
    /// each by its content when present and its headline otherwise.
    /// <para>Three rules. An item that does not fit is SKIPPED, not the end of the walk — one oversized item must
    /// not hide every shorter one behind it. An authoritative item is never dropped (design §5.7.0 objective (1)
    /// has no acceptable failure rate). The first item is always kept, so a budget smaller than one item still
    /// yields that item.</para></summary>
    /// <param name="items">What to cut, best first.</param>
    /// <param name="budget">The characters available.</param>
    internal static List<MemoryItem> Cut(IReadOnlyList<MemoryItem> items, int budget)
    {
        var spent = 0;
        var kept = new List<MemoryItem>(items.Count);
        foreach (var item in items)
        {
            var cost = item.Content?.Length ?? item.Headline.Length;
            if (item.Grade != MemoryGrade.Authoritative && kept.Count > 0 && spent + cost > budget) continue;
            spent += cost;
            kept.Add(item);
        }
        return kept;
    }
}
