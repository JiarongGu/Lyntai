using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Tests.Memory;

/// <summary>The corpus bench's pure half, on SYNTHETIC records: parsing the three record shapes, holding out what a
/// question quotes, building the questions and scoring a ranked list. The bench itself reads a real record set
/// that never enters the tree; these invented ones pin the shapes it relies on.</summary>
public sealed class RecordCorpusTests
{
    private const string DecisionOne = """
        ## D7 — The ledger rotates nightly (2026-03-01)

        **Decision.** The ledger file rotates at midnight so no reader holds a day-old handle.

        **Why.** A long-lived handle pinned the disk.

        **Rejected.** Rotating by size, because sizes differ per host.

        **Amended 2026-04-02 (ROT2): the rotation also compresses the closed file.** Compression runs after the swap.

        ## Note, 2026-05-01 (ROT3): the compressed file keeps its mtime

        Tools that sort by age keep working.
        """;

    private const string DecisionTwo = """
        ## D9 — Compression is off for small ledgers (2026-06-01)

        **Decision.** A ledger under a megabyte is left uncompressed.

        **This amends:** D7 (the rotation compresses only a ledger over a megabyte).
        """;

    private const string FixLog = """
        # Fix log

        ## 2026-06-10 - The nightly job hung on a locked file (LOCK1)

        - **Symptom:** the nightly rotation never finished and the process sat idle for hours.
        - **Root cause:** the rotate step waited on a handle a reader never released.
        - **Fix:** the reader releases the handle before the swap.
        - **Verify:** the job finished in two seconds.

        ## 2026-06-11 - A second entry (TWO2)

        - **Symptom:** an entirely different failure with enough words to count here.
        - **Fix:** changed it.
        """;

    private const string Archive = """
        # Archive

        ## ROT1 — rotate the ledger at midnight every day (2026-03-02, D7)

        Shipped the nightly rotation.

        ## Retired the old exporter (2026-03-03)

        Removed it.
        """;

    [Fact]
    public void A_decision_parses_into_labelled_sections_with_amendments_kept_apart()
    {
        var d = RecordParser.Decision(DecisionOne)!;

        Assert.Equal("D7", d.Key);
        Assert.Equal("The ledger rotates nightly", d.Title);
        Assert.Equal(new DateOnly(2026, 3, 1), d.Date);
        Assert.Equal(["decision", "why", "rejected", "amended", "note"], d.Sections.Select(s => s.Label));
        var amended = d.Sections[3];
        Assert.Equal("Amended 2026-04-02 (ROT2)", amended.Tag);
        Assert.Equal("the rotation also compresses the closed file", amended.Headline);
        Assert.True(d.Sections[4].IsAmendment);
        Assert.Equal("the compressed file keeps its mtime", d.Sections[4].Headline);
    }

    [Fact]
    public void A_decision_that_amends_another_names_it()
    {
        var d = RecordParser.Decision(DecisionTwo)!;

        Assert.Equal(["D7"], d.Amends);
    }

    [Fact]
    public void A_file_with_no_decision_heading_is_not_a_decision()
    {
        Assert.Null(RecordParser.Decision("just prose\n\n## Not a decision\nbody"));
    }

    [Fact]
    public void The_fix_log_parses_each_entry_with_its_fields()
    {
        var fixes = RecordParser.Fixes(FixLog);

        Assert.Equal(2, fixes.Count);
        Assert.Equal("The nightly job hung on a locked file", fixes[0].Title);
        Assert.Equal(["symptom", "root", "fix", "verify"], fixes[0].Sections.Select(s => s.Label));
    }

    [Fact]
    public void A_fix_heading_in_the_other_two_shapes_still_reads()
    {
        var fixes = RecordParser.Fixes("""
            ## STUCK1 (open): a job that stalls under load (since 2026-09-25)

            - **Symptom:** it stalls.

            ## A retry left a quest parked a second time (2026-10-05)

            - **Symptom:** parked twice.

            ## Undated heading

            - **Symptom:** ignored.
            """);

        Assert.Equal(["STUCK1 (open): a job that stalls under load", "A retry left a quest parked a second time"],
            fixes.Select(f => f.Title));
        Assert.Equal(new DateOnly(2026, 9, 25), fixes[0].Date);
    }

    [Fact]
    public void The_archive_reads_the_decisions_a_heading_cites_and_drops_its_id()
    {
        var entries = RecordParser.Archive(Archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal("rotate the ledger at midnight every day", entries[0].Title);
        Assert.Equal(["D7"], entries[0].Cites);
        Assert.Empty(entries[1].Cites);
    }

    [Fact]
    public void The_index_holds_out_what_a_question_quotes()
    {
        var fixes = RecordParser.Fixes(FixLog);
        var chunks = RecordChunker.Chunk(fixes);

        Assert.All(chunks, c => Assert.DoesNotContain("sat idle for hours", c.Text));
        Assert.All(chunks, c => Assert.DoesNotContain("hung on a locked file", c.Text));
        Assert.Contains(chunks, c => c.Text.Contains("never released"));

        var d = RecordParser.Decision(DecisionOne)!;
        Assert.All(RecordChunker.Chunk([d]), c => Assert.DoesNotContain("also compresses the closed file", c.Text));
    }

    [Fact]
    public void Chunks_stay_inside_their_record_and_under_the_limit_and_run_oldest_first()
    {
        var long1 = string.Join(" ", Enumerable.Repeat("alpha beta gamma delta", 120));
        var a = new CorpusRecord("A0001", RecordKind.Archive, "newer", new DateOnly(2026, 2, 1), [], [],
            [new RecordSection("body", "", "", long1)]);
        var b = new CorpusRecord("A0002", RecordKind.Archive, "older", new DateOnly(2026, 1, 1), [], [],
            [new RecordSection("body", "", "", "short")]);

        var chunks = RecordChunker.Chunk([a, b], maxChars: 300, minChars: 100);

        Assert.Equal("A0002", chunks[0].RecordKey);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 300));
        Assert.True(chunks.Count(c => c.RecordKey == "A0001") > 1);
    }

    [Fact]
    public void Questions_are_built_from_structure_and_are_repeatable()
    {
        var records = new List<CorpusRecord>
        {
            RecordParser.Decision(DecisionOne)!, RecordParser.Decision(DecisionTwo)!,
        };
        records.AddRange(RecordParser.Fixes(FixLog));
        records.AddRange(RecordParser.Archive(Archive));

        var questions = QuestionBuilder.Build(records, perKind: 10, seed: 1);

        Assert.Equal(questions.Select(q => q.Text), QuestionBuilder.Build(records, 10, 1).Select(q => q.Text));
        Assert.Contains(questions, q => q.Kind == QuestionKind.FixSymptom && q.Text.StartsWith("the nightly rotation never finished"));
        Assert.Contains(questions, q => q.Kind == QuestionKind.DecisionRejected && q.Gold[0].Label == "rejected");
        Assert.Contains(questions, q => q.Kind == QuestionKind.AmendmentChange
            && q.Text == "the rotation also compresses the closed file");
        var amender = Assert.Single(questions, q => q.Kind == QuestionKind.AmenderOfDecision);
        Assert.Equal(["D9", "D7"], amender.Gold.Select(g => g.RecordKey));
        Assert.Contains(questions, q => q.Kind == QuestionKind.ArchiveDelivered && q.Gold[0].RecordKey == "A0001");
    }

    [Fact]
    public void Scoring_reads_rank_precision_recall_and_a_stale_answer()
    {
        var d = RecordParser.Decision(DecisionOne)!;
        var chunks = RecordChunker.Chunk([d], maxChars: 120, minChars: 1);
        var amendment = Array.FindIndex([.. d.Sections], s => s.Label == "amended");
        var q = new CorpusQuestion(0, QuestionKind.AmendmentChange, "x",
            [new GoldRef("amendment", "D7", Section: amendment), new GoldRef("stale", "D7", Label: "amended", Not: true)]);

        var original = chunks.First(c => c.Labels.Contains("decision"));
        var amended = chunks.First(c => c.Labels.Contains("amended"));
        var staleFirst = RecallScoring.Score(q, [original, amended]);
        var amendedFirst = RecallScoring.Score(q, [amended, original]);

        Assert.Equal(2, staleFirst.FirstRank["amendment"]);
        Assert.Equal(1, staleFirst.FirstRank["stale"]);
        Assert.False(staleFirst.Hit(1));
        Assert.True(staleFirst.Hit(2));
        Assert.Equal(0.5, staleFirst.Reciprocal);
        Assert.Equal(0.5, staleFirst.Precision(2));
        Assert.Equal(1, amendedFirst.FirstRank["amendment"]);
        Assert.Equal(1.0, amendedFirst.Recall(1, 1));
        Assert.Empty(RecallScoring.Score(q, []).PrimaryAtRank);
    }

    [Fact]
    public void Overlap_is_the_share_of_the_question_a_word_match_could_use()
    {
        Assert.Equal(1.0, RecallScoring.Overlap("rotation compresses ledger", ["The ledger rotation compresses files"]));
        Assert.Equal(0.0, RecallScoring.Overlap("handle pinned disk", ["completely unrelated words here"]));
        Assert.Equal(0.0, RecallScoring.Overlap("the and for", ["the and for"]));
    }
}
