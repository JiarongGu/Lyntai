namespace Lyntai.Storage;

/// <summary>Builds a SQL <c>LIKE</c>/<c>ILIKE</c> substring pattern from raw user text, escaping the
/// wildcards (<c>%</c>, <c>_</c>) and the escape char itself so the query matches literally. Use with
/// an explicit <c>ESCAPE '\'</c> clause. Shared by the SQLite and Postgres memory stores (both dialects
/// treat <c>\</c> as the escape char), so the escaping lives in one place.</summary>
public static class LikePattern
{
    /// <summary>A <c>%…%</c> "contains" pattern with the term's wildcards escaped.</summary>
    public static string Contains(string term) =>
        "%" + Escape(term.Trim()) + "%";

    /// <summary>Escape <c>\</c>, <c>%</c>, and <c>_</c> so the term is matched literally.</summary>
    public static string Escape(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
