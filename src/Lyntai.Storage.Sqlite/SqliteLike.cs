using Lyntai.Storage;

namespace Lyntai.Storage.Sqlite;

/// <summary>The substring scan SQLite's stores fall back to, with the case folding SQLite's own <c>LIKE</c> lacks:
/// it folds ASCII alone, so <c>да</c> never matched <c>Да</c> and <c>école</c> never matched <c>ÉCOLE</c>.
/// <para>The column goes through <see cref="FunctionName"/>, which <see cref="SqliteConnectionFactory"/> registers on every
/// connection it opens, and each pattern is folded here in .NET; <c>LIKE</c> then compares two already-folded
/// strings, which its ASCII rule decides correctly (<c>docs/DECISIONS.md</c> D324).</para></summary>
internal static class SqliteLike
{
    /// <summary>The scalar function a connection carries.</summary>
    public const string FunctionName = "lyntai_fold";

    /// <summary>The fold: upper case by the invariant culture, the rule the in-process stores'
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> applies. Null stays null.</summary>
    public static string? Fold(string? text) => text?.ToUpperInvariant();

    /// <summary>The clause <see cref="SearchTerms.LikeClause(string?, IReadOnlyList{string}, string, string, bool)"/> builds, over
    /// the folded columns and with folded patterns.</summary>
    public static LikeTermClause Clause(string? query, params string[] columns)
    {
        var clause = SearchTerms.LikeClause(query, [.. columns.Select(c => $"{FunctionName}({c})")]);
        return clause with
        {
            Parameters = clause.Parameters.ToDictionary(p => p.Key, p => (object?)Fold((string?)p.Value), StringComparer.Ordinal),
        };
    }
}
