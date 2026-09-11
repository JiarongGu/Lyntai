using System.Linq;

namespace Lyntai.Benchmarks;

/// <summary>The two statistics a benchmark table needs before a percentage can be argued from.
///
/// <para><b>Why this exists.</b> Every recall-quality figure this repository publishes is a proportion over
/// a fixed question set, and a bare proportion says nothing about how far it could move on a different
/// draw. <c>docs/task-archive.md</c> Part 119 measured a near-tie band of about one point the expensive
/// way, and Part 137 retracted a category claim that a full sample reversed — both are what an interval
/// states up front.</para>
///
/// <para><b>The comparison is PAIRED, and that is not a refinement.</b> Arms answer the SAME questions, so
/// a two-sample test throws away the pairing and answers a question nobody asked. What matters is the
/// questions the two arms DISAGREE on, which is exactly what <see cref="McNemarExact"/> reads — and on a
/// 70-question class the discordant count is often small enough that the distinction decides the
/// verdict.</para></summary>
internal static class BenchStats
{
    /// <summary>The 95% two-sided normal quantile.</summary>
    private const double Z = 1.959964;

    /// <summary>A Wilson score interval for a proportion — chosen over the textbook normal interval because
    /// it stays inside [0,1] and behaves at the extremes, which is where these tables live (a 0/19 cell is a
    /// real result here, and the normal interval reports zero width for it).</summary>
    /// <param name="hits">Successes.</param>
    /// <param name="total">Trials; zero yields the full interval rather than a division by zero.</param>
    internal static (double Low, double High) Wilson(int hits, int total)
    {
        if (total <= 0) return (0, 1);

        var p = (double)hits / total;
        var z2 = Z * Z;
        var denominator = 1 + (z2 / total);
        var centre = (p + (z2 / (2.0 * total))) / denominator;
        var half = Z * Math.Sqrt((p * (1 - p) / total) + (z2 / (4.0 * total * total))) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>McNemar's EXACT test over the discordant pairs of two paired binary outcomes — the two-sided
    /// probability that a coin explains the split.
    ///
    /// <para>Exact rather than the chi-square approximation because that approximation needs roughly 25
    /// discordant pairs and these classes routinely produce fewer; reporting a chi-square p on eight
    /// discordant questions would be a fabricated precision.</para></summary>
    /// <param name="onlyFirst">Questions the first arm got and the second did not.</param>
    /// <param name="onlySecond">Questions the second arm got and the first did not.</param>
    /// <returns>The two-sided p-value; <c>1</c> when nothing is discordant.</returns>
    internal static double McNemarExact(int onlyFirst, int onlySecond)
    {
        var n = onlyFirst + onlySecond;
        if (n == 0) return 1;

        // Iterative terms rather than factorials: C(n,i)/2^n overflows nothing this way, and the running
        // term is exact enough at the sizes here.
        var k = Math.Min(onlyFirst, onlySecond);
        var term = Math.Pow(0.5, n);
        var tail = term;
        for (var i = 0; i < k; i++)
        {
            term *= (double)(n - i) / (i + 1);
            tail += term;
        }

        return Math.Min(1, 2 * tail);
    }

    /// <summary>A 95% interval for the MEAN of paired per-question differences — the instrument for a NULL.
    ///
    /// <para><b>Why neither existing member fits.</b> <see cref="Wilson"/> bounds a PROPORTION and
    /// <see cref="McNemarExact"/> tests paired BINARY outcomes; token-F1 is continuous and paired, so a
    /// difference here is a real number per question rather than a win or a loss.</para>
    ///
    /// <para><b>The half-width is the finding when nothing moves.</b> A bare "we saw no difference" is not a
    /// result — this reports "nothing larger than X", which is what makes a null refutable. A single
    /// observation yields an infinite interval rather than a zero-width one, because one pair bounds
    /// nothing.</para></summary>
    /// <param name="differences">One signed difference per question, arm A minus arm B, SAME questions.</param>
    internal static (double Mean, double Low, double High) PairedMeanInterval(
        IReadOnlyList<double> differences)
    {
        if (differences.Count == 0) return (0, double.NegativeInfinity, double.PositiveInfinity);

        var n = differences.Count;
        var mean = differences.Sum() / n;
        if (n == 1) return (mean, double.NegativeInfinity, double.PositiveInfinity);

        // Sample standard deviation (n-1): these questions are a SAMPLE of LoCoMo's 1,540, not the whole of
        // it, and the population form would understate the interval on exactly the small n this exists for.
        var variance = differences.Sum(d => (d - mean) * (d - mean)) / (n - 1);
        var half = Z * Math.Sqrt(variance / n);
        return (mean, mean - half, mean + half);
    }

    /// <summary>A p-value as a table cell, said the way a reader can act on: never "p = 0.000".</summary>
    internal static string Format(double p) => p switch
    {
        < 0.0001 => "p<0.0001",
        < 0.001 => "p<0.001",
        < 0.01 => $"p={p:F4}",
        _ => $"p={p:F3}",
    };

    /// <summary>Nearest-rank, on a copy — <see cref="List{T}.Sort"/> mutates, and a caller taking several
    /// percentiles off the same sample reads it again for the next one.
    ///
    /// <para><b>Hoisted here 2026-09-11</b>, when <c>MemoryContentionSweep</c> needed the identical helper
    /// <c>MemoryScaleSweep</c> already carried privately. A private second copy is the duplication this
    /// class exists to prevent — two percentile functions that drift produce two believable tables, the same
    /// reasoning <see cref="SweepDoubles"/>'s own hoisting note gives for a shared judge client.</para>
    /// </summary>
    /// <param name="values">The sample; never mutated by this call.</param>
    /// <param name="q">The quantile in [0, 1].</param>
    internal static double Percentile(IReadOnlyList<double> values, double q)
    {
        if (values.Count == 0) return 0;
        var sorted = new List<double>(values);
        sorted.Sort();
        var rank = (int)Math.Ceiling(q * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
