namespace Lyntai.Tests.Memory.Corpus;

/// <summary>
/// Proves the routine class (<see cref="CorpusShape.RoutineCount"/>) means what Task 2 claims BEFORE any
/// number is measured against it: the final frequency query's correct answer is phase B alone, even though
/// phase A carries more mass, and returning the bigger group scores as pollution rather than as a pass.
/// <para><b>Three of the five facts this plan asked for are already pinned, more strongly, in
/// <c>MemoryCorpusTests</c></b> and are not repeated here: "off by default"
/// (<c>RoutineCount_defaults_to_zero_and_changes_nothing</c>, which also checks byte-identity with the
/// pre-axis shape), "phase A is the larger regime"
/// (<c>Phase_A_is_the_larger_regime_for_every_legal_RoutineCount</c>, property-based over 28 counts rather
/// than one), and "the final query names phase B alone"
/// (<c>The_final_routine_query_names_phase_B_only_and_never_phase_A</c>, which also pins the first query's
/// own answer). This file adds the two facts that were genuinely missing: the SCORED consequence of
/// ignoring recency, and the query's independence from either regime's own wording.</para>
/// </summary>
public class MemoryRoutineClassTests
{
    private static CorpusShape Shape => CorpusShape.Default with { RoutineCount = 12, RoutineSupport = 3 };

    private static IReadOnlyList<CorpusQuery> RoutineQueries(MemoryCorpus corpus) =>
        [.. corpus.Steps.OfType<CorpusQuery>()
            .Where(q => q.RelevantIds.Any(id => id.StartsWith("routine", StringComparison.Ordinal)))];

    /// <summary>The discriminating fact, stated as a SCORE rather than as a property: returning the biggest
    /// group is the wrong answer, and the metric must say so. Built the other way round — the final query's
    /// ground truth naming phase A instead of B — <c>frequencyOnly</c> would score a perfect
    /// <see cref="RecallQuality.MissRate"/> of 0, so this assertion would fail exactly when the class
    /// stopped meaning what it claims.</summary>
    [Fact]
    public void A_frequency_only_answer_scores_as_POLLUTION_after_the_regime_change()
    {
        var corpus = MemoryCorpus.Generate(Shape, seed: 12345);
        var last = RoutineQueries(corpus)[^1];
        var phaseA = corpus.Steps.OfType<CorpusWrite>()
            .Select(w => MemoryCorpusTestAccess.IdOf(w.Write.Content))
            .Where(id => id.StartsWith("routineA", StringComparison.Ordinal))
            .Take(3).ToList();

        var frequencyOnly = RecallQuality.Measure(phaseA, last.RelevantIds, limit: 10, last.SupportNeeded);
        var recencyAware = RecallQuality.Measure(last.RelevantIds, last.RelevantIds, limit: 10, last.SupportNeeded);

        Assert.Equal(1.0, frequencyOnly.MissRate, precision: 9);
        Assert.True(frequencyOnly.PollutionRate > 0, "phase A must be pollution at the final query");
        Assert.Equal(0.0, recencyAware.MissRate, precision: 9);
    }

    /// <summary>Same guarantee <c>AttributeCueKind.Discriminative</c> carries: if the query named either
    /// regime's own distinguishing word, the class would be an ordinary lookup rather than a test of
    /// generalisation. Checked against <see cref="CorpusLexicon.RoutineToken"/> — the token the templates
    /// actually embed — rather than re-deriving one from written content, so the two can never silently
    /// drift apart.</summary>
    [Fact]
    public void The_cue_never_contains_its_own_answer()
    {
        foreach (var language in Enum.GetValues<CorpusLanguage>())
        {
            var lex = CorpusLexicon.For(language);
            var corpus = MemoryCorpus.Generate(Shape with { Language = language }, seed: 12345);
            var queries = RoutineQueries(corpus);
            var routineWrites = corpus.Steps.OfType<CorpusWrite>()
                .Where(w => MemoryCorpusTestAccess.IdOf(w.Write.Content)
                    .StartsWith("routine", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(queries);

            string? DistinguishingToken(string content)
            {
                var id = MemoryCorpusTestAccess.IdOf(content);
                if (id.StartsWith("routineA", StringComparison.Ordinal)) return lex.RoutineToken(0);
                if (id.StartsWith("routineB", StringComparison.Ordinal)) return lex.RoutineToken(1);
                return null;
            }

            foreach (var q in queries)
                Assert.All(routineWrites, w => Assert.False(
                    DistinguishingToken(w.Write.Content) is { } token
                        && q.Text.Contains(token, StringComparison.Ordinal),
                    $"{language}: the routine query contains the routine's own token"));
        }
    }
}
