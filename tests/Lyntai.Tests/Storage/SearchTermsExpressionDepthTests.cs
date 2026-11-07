using Dapper;
using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>A query's terms size a SQL expression, and SQLite refuses one past a fixed depth: a raw query of
/// a thousand words must still produce a clause the engine runs.</summary>
public class SearchTermsExpressionDepthTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"word{i:D5}"));

    [Fact]
    public void A_query_yields_a_bounded_number_of_terms_and_keeps_the_leading_ones()
    {
        var terms = SearchTerms.Extract(Words(5_000));
        var substring = SearchTerms.SubstringTerms(Words(5_000));

        Assert.Equal(SearchTerms.MaxTerms, terms.Count);
        Assert.Equal(SearchTerms.MaxTerms, substring.Count);
        Assert.Equal("word00000", terms[0]);   // a truncated tail leaves every earlier term matching
    }

    [Theory]
    [InlineData(1_500, "m.content")]
    [InlineData(5_000, "m.content")]
    public async Task The_substring_clause_of_a_very_long_query_runs_on_SQLite(int words, string column)
    {
        await using var conn = await _db.Factory.OpenAsync();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at)
            VALUES ('t', 's', 'the first word00000 and word00001', '2026-07-17T12:00:00+00:00')
            """));

        var clause = SearchTerms.LikeClause(Words(words), column);
        var parameters = new DynamicParameters();
        foreach (var (name, value) in clause.Parameters) parameters.Add(name, value);

        var hits = await conn.QueryAsync<long>(new CommandDefinition($"""
            SELECT m.id FROM lyntai_memory_entry m WHERE {clause.Predicate} ORDER BY {clause.MatchCount} DESC
            """, parameters));

        Assert.Single(hits);
    }
}
