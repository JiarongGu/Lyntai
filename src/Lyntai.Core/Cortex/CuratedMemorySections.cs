using System.Text;
using Lyntai.Storage;

namespace Lyntai.Cortex;

/// <summary>
/// Composes a curated memory catalog into a prompt fragment as per-<c>Kind</c> sections — the read side of
/// <see cref="ICuratedMemoryStore"/>. Disabled entries are dropped; kinds are ordered (ordinal) and each
/// becomes a headed section of bulleted entries. Pure/no-I/O (fetch with the store's <c>ListAsync</c>,
/// then compose), so it's trivially testable and AOT-clean.
/// </summary>
public static class CuratedMemorySections
{
    /// <summary>Render <paramref name="entries"/> as per-kind sections. Only <c>Enabled</c> entries are
    /// included; empty input (or all-disabled) yields an empty string. <paramref name="header"/> formats a
    /// kind into its section heading (default <c>"## {kind}"</c>); <paramref name="bullet"/> prefixes each
    /// entry (default <c>"- "</c>). When <paramref name="task"/> is non-null the entries are additionally
    /// filtered by <see cref="AppliesTo"/> (task + scope), mirroring <c>ICuratedMemoryStore.ForCompositionAsync</c>
    /// — for callers that fetch a broad list and filter in-app; pass an empty <paramref name="scopes"/> to
    /// disable scope filtering.</summary>
    public static string Compose(IEnumerable<CuratedMemory> entries,
        Func<string, string>? header = null, string bullet = "- ",
        string? task = null, IEnumerable<string>? scopes = null)
    {
        header ??= k => $"## {k}";
        IEnumerable<CuratedMemory> enabled = entries.Where(e => e.Enabled);
        if (task is not null)
        {
            var scopeSet = scopes as IReadOnlyCollection<string> ?? scopes?.ToArray() ?? [];
            enabled = enabled.Where(e => AppliesTo(e, task, scopeSet));
        }
        var byKind = enabled
            .GroupBy(e => e.Kind, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var group in byKind)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(header(group.Key)).Append('\n');
            foreach (var entry in group.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id))
                sb.Append(bullet).Append(entry.Content).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The canonical curated-composition predicate (shared by the store backends' <c>ForCompositionAsync</c>
    /// and <see cref="Compose"/>): the entry's <see cref="CuratedMemory.Task"/> is null (applies to every task)
    /// or equals <paramref name="task"/>, AND either <paramref name="scopes"/> is empty (scope filter disabled),
    /// or the entry's <see cref="CuratedMemory.Scope"/> is null/empty (applies to every scope), or the scope is
    /// one of <paramref name="scopes"/>. <see cref="CuratedMemory.Enabled"/> is checked separately.</summary>
    public static bool AppliesTo(CuratedMemory entry, string task, IReadOnlyCollection<string> scopes)
        => (entry.Task is null || entry.Task == task)
           && (scopes.Count == 0 || string.IsNullOrEmpty(entry.Scope) || scopes.Contains(entry.Scope));
}
