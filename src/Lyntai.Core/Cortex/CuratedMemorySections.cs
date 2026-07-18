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
    /// entry (default <c>"- "</c>).</summary>
    public static string Compose(IEnumerable<CuratedMemory> entries,
        Func<string, string>? header = null, string bullet = "- ")
    {
        header ??= k => $"## {k}";
        var byKind = entries
            .Where(e => e.Enabled)
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
}
