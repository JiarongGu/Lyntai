namespace Lyntai.Benchmarks;

/// <summary><c>--layers</c> on both field benches: per question, was the gold evidence a CANDIDATE (an
/// <c>Explain</c> recall's pool), in the RANKED page the reader saw, and was the ANSWER right — so a loss is
/// placed in retrieval, ranking or reading rather than read off one end-to-end score.</summary>
internal sealed class RecallLayers(string bench, IEnumerable<string> args)
{
    private readonly Dictionary<string, List<(bool C, bool R, bool A, double F1)>> _rows = new(StringComparer.Ordinal);
    private readonly BenchResults _results = new(bench, "recall-layers", args);

    /// <summary>Explained recalls whose returned set differed from the one the reader got — must be 0, or
    /// the candidate layer describes a different recall from the ranked one.</summary>
    internal int Mismatched { get; set; }

    internal void Add(string arm, string question, bool candidate, bool ranked, bool answered, double f1)
    {
        if (!_rows.TryGetValue(arm, out var list)) _rows[arm] = list = [];
        list.Add((candidate, ranked, answered, f1));
        _results.Add($"{arm}/candidate", question, candidate);
        _results.Add($"{arm}/ranked", question, ranked);
        _results.Add($"{arm}/answered", question, answered, score: f1);
    }

    internal async Task PrintAsync()
    {
        Console.WriteLine();
        Console.WriteLine("=== Recall in three layers: candidate -> ranked -> answered ===");
        Console.WriteLine($"{"arm",-22} {"n",4} {"candidate",10} {"ranked",8} {"answered",9} {"F1",6}"
            + $" | {"lost: retrieval",15} {"ranking",8} {"reading",8} | {"A w/o R",7}");
        foreach (var (arm, rows) in _rows)
        {
            var n = rows.Count;
            string P(int k) => $"{100.0 * k / n:F1}%";
            int Count(Func<(bool C, bool R, bool A, double F1), bool> f) => rows.Count(f);
            Console.WriteLine($"{arm,-22} {n,4} {P(Count(r => r.C)),10} {P(Count(r => r.R)),8} "
                + $"{P(Count(r => r.A)),9} {rows.Average(r => r.F1),6:F3} | "
                + $"{P(Count(r => !r.C)),15} {P(Count(r => r.C && !r.R)),8} {P(Count(r => r.R && !r.A)),8} | "
                + $"{Count(r => r.A && !r.R),7}");
            var ranked = Count(r => r.R);
            var unranked = n - ranked;
            Console.WriteLine($"{"",-22}      answered | ranked {(ranked == 0 ? "-" : P2(Count(r => r.R && r.A), ranked))}"
                + $"   answered | not ranked {(unranked == 0 ? "-" : P2(Count(r => !r.R && r.A), unranked))}");
        }
        Console.WriteLine("  lost: retrieval = never a candidate; ranking = a candidate the page did not hold;");
        Console.WriteLine("  reading = on the page and still answered wrong. 'A w/o R' answered with no gold turn shown.");
        if (Mismatched > 0)
            Console.WriteLine($"  ! {Mismatched} explained recall(s) returned a different set from the reader's — "
                + "the candidate column is NOT the same recall");
        await _results.WriteAsync();

        static string P2(int k, int of) => $"{100.0 * k / of:F1}% ({k}/{of})";
    }
}
