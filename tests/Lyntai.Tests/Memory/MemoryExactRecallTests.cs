using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>What a history archive asks of a recall: an identifier finds the entry about it, a score says how strong
/// the hit is, a passage replaces the whole entry, and an entry too long to read is refused where it is written.</summary>
public sealed class MemoryExactRecallTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private GraphMemoryEngine Engine(bool identifiers = false, GraphMemoryOptions? options = null) =>
        new("e", new SqliteMemoryGraphStore(_db.Factory), options, new GraphMemorySeams
        {
            SeedSources = identifiers ? [new LexicalSeedSource(), new IdentifierSeedSource()] : [new LexicalSeedSource()],
        });

    private static Task<MemoryWriteResult> Write(GraphMemoryEngine engine, string headline, string content) =>
        engine.RememberAsync(new MemoryWrite("t", "s", content, headline));

    [Theory]
    [InlineData("what did D123 decide?", "D123")]
    [InlineData("row ABC1d moved", "ABC1d")]
    [InlineData("see (ISSUE-42), then", "ISSUE-42")]
    [InlineData("the v3.5.3 release", "v3.5.3")]
    [InlineData("call snake_case now", "snake_case")]
    [InlineData("GraphMemoryEngine recalls", "GraphMemoryEngine")]
    [InlineData("open README.md first", "README.md")]
    public void An_identifier_shaped_word_is_found_with_its_punctuation_trimmed(string query, string expected) =>
        Assert.Equal([expected], MemoryIdentifiers.Extract(query, 8));

    [Theory]
    [InlineData("plain words only")]
    [InlineData("in 1999 it was e.g. fine")]
    [InlineData("12345")]
    [InlineData("")]
    public void A_plain_word_or_a_bare_number_is_no_identifier(string query) =>
        Assert.Empty(MemoryIdentifiers.Extract(query, 8));

    [Fact]
    public void An_identifier_stands_whole_and_in_the_same_case()
    {
        Assert.Equal([0, 18], MemoryIdentifiers.Find("D12 is not D1234; D12.", "D12"));
        Assert.Empty(MemoryIdentifiers.Find("row ABC1D here", "ABC1d"));
        Assert.Empty(MemoryIdentifiers.Find("xD12 D12y _D12", "D12"));
    }

    [Fact]
    public async Task The_identifier_channel_returns_only_whole_matches_and_the_headline_leads()
    {
        var engine = Engine();
        await Write(engine, "cache design", "see D123 for the storage choice, and D123 again");
        await Write(engine, "D123 storage choice", "use sqlite");
        await Write(engine, "D1234 caching", "D1234 only, nothing else");
        var store = new SqliteMemoryGraphStore(_db.Factory);

        var found = await new IdentifierSeedSource().SeedAsync(
            new MemorySeedRequest("e", store, new MemoryQuery("t", "s", "what did D123 decide"), 10), CancellationToken.None);

        Assert.Equal(["D123 storage choice", "cache design"], found.Select(n => n.Headline));
        Assert.All(found, n => Assert.True(n.Matched));
        Assert.True(found[0].Relevance > found[1].Relevance);
    }

    [Fact]
    public async Task A_query_with_no_identifier_gives_the_channel_nothing_to_read()
    {
        var engine = Engine();
        await Write(engine, "D123 storage choice", "use sqlite");
        var store = new SqliteMemoryGraphStore(_db.Factory);

        var found = await new IdentifierSeedSource().SeedAsync(
            new MemorySeedRequest("e", store, new MemoryQuery("t", "s", "storage choice"), 10), CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task The_entry_about_an_identifier_leads_the_one_that_cites_it_and_the_one_that_resembles_it()
    {
        var engine = Engine(identifiers: true);
        await Write(engine, "caching", string.Join(' ', Enumerable.Repeat("what did we decide about storage reads, decide storage", 8)) + " (see D123)");
        await Write(engine, "D1234 decision", "D1234 decided what about storage; did decide about storage");
        var about = (await Write(engine, "D123 storage", "sqlite")).Reference;
        foreach (var filler in new[] { "alpha", "beta", "gamma", "delta", "epsilon" })
            await Write(engine, filler, $"{filler} about nothing in particular");

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "what did D123 decide about storage", Limit: 3));

        Assert.Equal(about, recall.Items[0].Reference);
    }

    [Fact]
    public async Task As_one_more_rank_fusion_vote_the_channel_does_not_put_the_entry_about_an_identifier_first()
    {
        // the reason the channel pins: with Pin off, the long entry that tops the text ranking and cites D123 once
        // stays ahead of the short entry about it
        var engine = new GraphMemoryEngine("e", new SqliteMemoryGraphStore(_db.Factory), null, new GraphMemorySeams
        {
            SeedSources = [new LexicalSeedSource(), new IdentifierSeedSource(new IdentifierSeedOptions { Pin = false })],
        });
        var cites = (await Write(engine, "caching", string.Join(' ', Enumerable.Repeat("what did we decide about storage reads, decide storage", 8)) + " (see D123)")).Reference;
        await Write(engine, "D1234 decision", "D1234 decided what about storage; did decide about storage");
        await Write(engine, "D123 storage", "sqlite");
        foreach (var filler in new[] { "alpha", "beta", "gamma", "delta", "epsilon" })
            await Write(engine, filler, $"{filler} about nothing in particular");

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "what did D123 decide about storage", Limit: 3));

        Assert.Equal(cites, recall.Items[0].Reference);
    }

    [Fact]
    public async Task A_hit_holding_every_distinctive_word_scores_high_and_a_partial_one_lower()
    {
        var engine = Engine();
        await Write(engine, "all", "alpha beta gamma delta");
        await Write(engine, "some", "alpha only here");
        await Write(engine, "other", "beta something else");
        await Write(engine, "none", "nothing relevant at all");

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "alpha beta gamma", Limit: 10));
        var all = recall.Items.Single(i => i.Headline == "all");
        var some = recall.Items.Single(i => i.Headline == "some");

        Assert.True(all.Score > 0.99);
        Assert.InRange(some.Score!.Value, 0.05, 0.8);
        Assert.True(all.Score > some.Score);
    }

    [Fact]
    public async Task A_word_every_candidate_holds_does_not_make_a_weak_hit_strong()
    {
        var engine = Engine();
        await Write(engine, "rare", "the zephyr blew");
        foreach (var n in Enumerable.Range(0, 5)) await Write(engine, $"common {n}", $"the day {n} went by");

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "the zephyr", Limit: 10));

        Assert.True(recall.Items.Single(i => i.Headline == "rare").Score > 0.99);
        Assert.All(recall.Items.Where(i => i.Headline != "rare"), i => Assert.True(i.Score < 0.15));
    }

    [Fact]
    public async Task A_recall_without_a_query_carries_no_score()
    {
        var engine = Engine();
        await Write(engine, "one", "alpha");

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s"));

        Assert.All(recall.Items, i => Assert.Null(i.Score));
    }

    private static readonly string Filler = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet. ", 150));

    [Fact]
    public async Task A_matching_recall_returns_the_passage_and_not_the_entry()
    {
        var engine = Engine();
        var content = Filler + "The row ABC1d was migrated on friday. " + Filler;
        await Write(engine, "migration log", content);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "ABC1d migrated", Limit: 3, Detail: MemoryDetail.Matching) { SpanChars = 200 });
        var item = Assert.Single(recall.Items);

        Assert.Null(item.Content);
        var span = item.Span!;
        Assert.Contains("ABC1d", span.Text);
        Assert.Contains("migrated", span.Text);
        Assert.InRange(span.Text.Length, 1, 200);
        Assert.Equal(content.Length, span.ContentLength);
        Assert.Equal(span.Text, content.Substring(span.Start, span.Text.Length));
        Assert.True(span.Start > 0 && span.Start + span.Text.Length < content.Length);
    }

    [Fact]
    public async Task The_other_details_carry_no_span_and_full_still_returns_the_whole_content()
    {
        var engine = Engine();
        var content = Filler + "The row ABC1d was migrated on friday. ";
        await Write(engine, "migration log", content);

        var headline = Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s", "ABC1d", Detail: MemoryDetail.Headline))).Items);
        var full = Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s", "ABC1d", Detail: MemoryDetail.Full))).Items);

        Assert.Null(headline.Span);
        Assert.Null(full.Span);
        Assert.Equal(content, full.Content);
    }

    [Fact]
    public async Task An_authoritative_entry_stays_whole_beside_its_passage()
    {
        var engine = Engine();
        var content = "The production database is db-prod-2 in eu-west-1, and the failover is db-prod-3.";
        await engine.RememberAsync(new MemoryWrite("t", "s", content, Grade: MemoryGrade.Authoritative));

        var item = Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s", "failover", Detail: MemoryDetail.Matching) { SpanChars = 20 })).Items);

        Assert.Equal(content, item.Content);
        Assert.Contains("failover", item.Span!.Text);
    }

    [Fact]
    public async Task A_passage_is_what_the_character_budget_prices()
    {
        var engine = Engine();
        foreach (var n in Enumerable.Range(0, 4))
            await Write(engine, $"log {n}", Filler + $"marker{n} " + Filler);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "marker0 marker1 marker2 marker3", Limit: 4, CharBudget: 700, Detail: MemoryDetail.Matching) { SpanChars = 300 });

        // two passages of about 300 fit; priced by headline alone, all four would
        Assert.Equal(2, recall.Items.Count);
    }

    [Fact]
    public async Task A_write_past_the_content_bound_is_refused_and_one_at_it_is_kept()
    {
        var engine = Engine(options: new GraphMemoryOptions { MaxContentChars = 20 });

        await engine.RememberAsync(new MemoryWrite("t", "s", new string('a', 20)));
        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.RememberAsync(new MemoryWrite("t", "s", new string('a', 21), Grade: MemoryGrade.Authoritative)));

        Assert.Contains(nameof(MemoryChunks.Split), refused.Message);
        Assert.Single((await engine.RecallAsync(new MemoryQuery("t", "s"))).Items);
    }

    [Fact]
    public void The_content_bound_is_off_by_default_and_refuses_nothing_below_one()
    {
        Assert.Null(new GraphMemoryOptions().MaxContentChars);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphMemoryOptions { MaxContentChars = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryQuery("t") { SpanChars = 0 });
    }

    [Fact]
    public void Chunks_join_back_to_the_text_and_each_fits()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 30).Select(n => $"Paragraph {n} says something. It has two sentences."));

        var parts = MemoryChunks.Split(text, 200);

        Assert.True(parts.Count > 1);
        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, p => Assert.InRange(p.Length, 1, 200));
        Assert.All(parts.Take(parts.Count - 1), p => Assert.EndsWith("\n\n", p));
    }

    [Fact]
    public void A_short_text_is_one_part_and_an_empty_one_none()
    {
        Assert.Equal(["short"], MemoryChunks.Split("short", 10));
        Assert.Empty(MemoryChunks.Split("", 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryChunks.Split("x", 0));
    }

    [Fact]
    public void A_text_with_no_break_is_cut_between_characters_and_never_inside_a_surrogate_pair()
    {
        var text = string.Concat(Enumerable.Repeat("a😀", 20)) + "tail";

        var parts = MemoryChunks.Split(text, 7);

        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, p => Assert.InRange(p.Length, 1, 7));
        Assert.All(parts, p => Assert.False(char.IsLowSurrogate(p[0])));
    }

    [Fact]
    public void A_sentence_end_is_preferred_to_a_space_and_a_space_to_a_cut()
    {
        var parts = MemoryChunks.Split("One two three. Four five six seven eight", 24);

        Assert.Equal("One two three. ", parts[0]);
    }
}
