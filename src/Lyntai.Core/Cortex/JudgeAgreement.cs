namespace Lyntai.Cortex;

/// <summary>How well two judges (or a judge and a gold label) agree over the same items. Pure code,
/// no LLM — feed it paired scores/labels to calibrate whether an LLM judge is trustworthy before you
/// rely on it. Research found no settled practice here, so the surface is deliberately small and the
/// metrics are the well-understood ones.</summary>
/// <param name="Count">Number of paired items compared.</param>
/// <param name="ExactAgreementRate">Fraction of items where both sides gave the same discretized
/// verdict (score rounded to <c>buckets</c> bins). 1.0 = identical, 0.0 = never agree.</param>
/// <param name="MeanAbsoluteError">Average |a − b| over the raw scores — how far apart, on average.</param>
/// <param name="PearsonCorrelation">Linear correlation of the two score series in [−1, 1]; null when
/// undefined (fewer than 2 items, or either side is constant).</param>
public sealed record AgreementReport(int Count, double ExactAgreementRate, double MeanAbsoluteError, double? PearsonCorrelation);

public static class JudgeAgreement
{
    /// <summary>Compare two aligned score series (index i is the same item). Scores are assumed to be
    /// comparable (e.g. both 0..1). <paramref name="buckets"/> discretizes for the exact-agreement rate
    /// (default 2 → agree iff both land on the same side of 0.5).</summary>
    public static AgreementReport Compare(IReadOnlyList<double> a, IReadOnlyList<double> b, int buckets = 2)
    {
        if (a.Count != b.Count)
            throw new ArgumentException($"score series must be the same length ({a.Count} vs {b.Count})");
        if (buckets < 1) buckets = 1;
        var n = a.Count;
        if (n == 0) return new AgreementReport(0, 1.0, 0.0, null);

        var agree = 0;
        var absErrSum = 0.0;
        for (var i = 0; i < n; i++)
        {
            if (Bucket(a[i], buckets) == Bucket(b[i], buckets)) agree++;
            absErrSum += Math.Abs(a[i] - b[i]);
        }

        return new AgreementReport(n, (double)agree / n, absErrSum / n, Pearson(a, b));
    }

    private static int Bucket(double score, int buckets)
    {
        var clamped = Math.Clamp(score, 0.0, 1.0);
        var idx = (int)(clamped * buckets);
        return idx == buckets ? buckets - 1 : idx; // 1.0 lands in the top bucket, not one past it
    }

    private static double? Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var n = a.Count;
        if (n < 2) return null;

        double meanA = 0, meanB = 0;
        for (var i = 0; i < n; i++) { meanA += a[i]; meanB += b[i]; }
        meanA /= n; meanB /= n;

        double cov = 0, varA = 0, varB = 0;
        for (var i = 0; i < n; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            cov += da * db;
            varA += da * da;
            varB += db * db;
        }
        if (varA == 0 || varB == 0) return null; // a constant series has no linear correlation
        return cov / Math.Sqrt(varA * varB);
    }
}
