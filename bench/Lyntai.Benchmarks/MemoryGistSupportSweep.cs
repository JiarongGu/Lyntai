using System.Text;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// Which rule selects the CURRENT regime of a recurring cluster — the gist tier's support question.
/// <para><b><c>--screen</c> is the ladder alone, on one shape.</b> It asks whether a generative model can
/// answer a BINARY regime question at all, and at what size. The measured 3-4B judge floor is a fact about
/// 40-way verification, not about this; the ladder is what finds this task's own floor.</para>
/// <para><b>Position is COUNTERBALANCED and a rung must win both orders.</b> A small model asked to pick
/// between two labelled options has a position bias, and a rung that always answers "2" scores 50% while
/// looking like a partial success.</para>
/// <para><b>Measured 2026-08-28, llama-server, one shape (<c>RoutineCount=12</c>), seed 12345 — all four
/// rungs run, none NOT TESTED.</b> gemma-3 270m it: A-first EARLIER, B-first LATER — a pure "always answer
/// option 1" position bias, does NOT survive. gemma-3 1b it: A-first LATER, B-first EARLIER — a pure
/// "always answer option 2" bias, does NOT survive. gemma-3 4b it (ref): A-first LATER, B-first LATER —
/// content-driven in both orders, SURVIVES. Llama-3.2 1B Instruct Q4_K_M (ctrl, size held = rung 2):
/// A-first EARLIER, B-first LATER — a pure "always answer option 1" bias (the SAME direction as the 270m
/// rung and the OPPOSITE of gemma-3 1b's), does NOT survive.</para>
/// <para><b>The control did not separate generation from size.</b> Both 1B-class rungs fail by pure
/// position bias, each in its own direction, so a small model's family does not rescue this task at 1B —
/// this shape's floor sits strictly between 1B and 4B parameters, not at a family boundary within 1B.</para>
/// </summary>
internal static class MemoryGistSupportSweep
{
    private const int Seed = 12345;
    private static readonly CorpusShape ScreenShape = CorpusShape.Default with { RoutineCount = 12 };

    internal static async Task<int> RunAsync(string[] args)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var chat = await SweepDoubles.TryRealChatAsync(http, "memory-support");
        if (chat is null) return 1;

        var corpus = MemoryCorpus.Generate(ScreenShape, Seed);
        var (query, phaseA, phaseB) = RoutineMaterial(corpus);

        Console.WriteLine($"memory-support --screen · model={chat.Model} · shape=RoutineCount=12 · seed={Seed}");
        Console.WriteLine($"  phase A: {phaseA.Count} entries (earlier)   phase B: {phaseB.Count} entries (later)");
        Console.WriteLine();

        // A position-biased rung emits the SAME digit in both orders. The digit-to-regime mapping flips
        // between orders, so that digit cannot be correct twice - which is what counterbalancing catches.
        var warm = await AskAsync(chat, query, phaseA, phaseB, bFirst: false);
        var swapped = await AskAsync(chat, query, phaseA, phaseB, bFirst: true);

        Console.WriteLine($"  A-first  → answered {Describe(warm)}   (correct: the LATER option)");
        Console.WriteLine($"  B-first  → answered {Describe(swapped)}   (correct: the LATER option)");
        Console.WriteLine();

        var passed = warm is Verdict.Later && swapped is Verdict.Later;
        Console.WriteLine(passed
            ? "  VERDICT: survives - both orders correct. Eligible for the full sweep."
            : "  VERDICT: does NOT survive. Record it as dropped; do not spend the grid on it.");
        return 0;
    }

    private enum Verdict { Later, Earlier, Unparsed }

    private static string Describe(Verdict v) => v switch
    {
        Verdict.Later => "the LATER regime",
        Verdict.Earlier => "the EARLIER regime",
        _ => "UNPARSED",
    };

    private static async Task<Verdict> AskAsync(SweepDoubles.OpenAiCompatibleChat chat, string query,
        IReadOnlyList<string> phaseA, IReadOnlyList<string> phaseB, bool bFirst)
    {
        var first = bFirst ? phaseB : phaseA;
        var second = bFirst ? phaseA : phaseB;
        var firstIsLater = bFirst;

        var answer = await chat.AskAsync(Prompt(query, first, second, firstIsLater));
        var trimmed = answer?.Trim();
        if (trimmed is null || trimmed.Length == 0) return Verdict.Unparsed;

        return trimmed[0] switch
        {
            '1' => firstIsLater ? Verdict.Later : Verdict.Earlier,
            '2' => firstIsLater ? Verdict.Earlier : Verdict.Later,
            _ => Verdict.Unparsed,
        };
    }

    // Recency is STATED rather than implied. The mechanical arms read it from retrievability, so withholding
    // it here would compare a model without the signal against rules that have it.
    private static string Prompt(string query, IReadOnlyList<string> first, IReadOnlyList<string> second,
        bool firstIsLater)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Question: {query}");
        sb.AppendLine();
        sb.AppendLine($"Option 1 ({(firstIsLater ? "more recent" : "older")}, {first.Count} entries):");
        foreach (var text in first) sb.AppendLine($"  - {text}");
        sb.AppendLine();
        sb.AppendLine($"Option 2 ({(firstIsLater ? "older" : "more recent")}, {second.Count} entries):");
        foreach (var text in second) sb.AppendLine($"  - {text}");
        sb.AppendLine();
        sb.Append("Which option describes what is true NOW? Reply with only the digit 1 or 2.");
        return sb.ToString();
    }

    /// <summary>The routine query and each regime's entry texts, read straight off the corpus timeline.</summary>
    private static (string Query, IReadOnlyList<string> PhaseA, IReadOnlyList<string> PhaseB) RoutineMaterial(
        MemoryCorpus corpus)
    {
        var phaseA = new List<string>();
        var phaseB = new List<string>();
        foreach (var write in corpus.Steps.OfType<CorpusWrite>())
        {
            var id = MemoryPolicySweep.ExtractCorpusId(write.Write.Content);
            if (id.StartsWith("routineA", StringComparison.Ordinal)) phaseA.Add(write.Write.Content);
            else if (id.StartsWith("routineB", StringComparison.Ordinal)) phaseB.Add(write.Write.Content);
        }

        var query = corpus.Steps.OfType<CorpusQuery>()
            .Last(q => q.RelevantIds.Any(id => id.StartsWith("routine", StringComparison.Ordinal)));
        return (query.Text, phaseA, phaseB);
    }
}
