using Lyntai.Cortex;

namespace Lyntai.Tests.Cortex;

public class JudgeAgreementTests
{
    [Fact]
    public void Identical_series_agree_perfectly_and_correlate()
    {
        var scores = new[] { 0.1, 0.5, 0.9, 0.3 };

        var report = JudgeAgreement.Compare(scores, scores);

        Assert.Equal(4, report.Count);
        Assert.Equal(1.0, report.ExactAgreementRate);
        Assert.Equal(0.0, report.MeanAbsoluteError);
        Assert.Equal(1.0, report.PearsonCorrelation!.Value, precision: 10);
    }

    [Fact]
    public void Opposite_side_of_the_threshold_disagrees()
    {
        // a says low, b says high on every item → 0 exact agreement (2 buckets, split at 0.5)
        var a = new[] { 0.1, 0.2, 0.3 };
        var b = new[] { 0.9, 0.8, 0.7 };

        var report = JudgeAgreement.Compare(a, b);

        Assert.Equal(0.0, report.ExactAgreementRate);
        Assert.Equal(0.6, report.MeanAbsoluteError, precision: 10);
    }

    [Fact]
    public void Mean_absolute_error_is_averaged()
    {
        var report = JudgeAgreement.Compare([0.0, 1.0], [0.5, 0.5]);
        Assert.Equal(0.5, report.MeanAbsoluteError, precision: 10);
    }

    [Fact]
    public void Anti_correlated_series_gives_negative_pearson()
    {
        var report = JudgeAgreement.Compare([0.0, 0.5, 1.0], [1.0, 0.5, 0.0]);
        Assert.Equal(-1.0, report.PearsonCorrelation!.Value, precision: 10);
    }

    [Fact]
    public void Constant_series_has_undefined_correlation()
    {
        var report = JudgeAgreement.Compare([0.5, 0.5, 0.5], [0.1, 0.9, 0.4]);
        Assert.Null(report.PearsonCorrelation);
    }

    [Fact]
    public void Empty_series_is_vacuously_perfect_agreement()
    {
        var report = JudgeAgreement.Compare([], []);
        Assert.Equal(0, report.Count);
        Assert.Equal(1.0, report.ExactAgreementRate);
        Assert.Null(report.PearsonCorrelation);
    }

    [Fact]
    public void Mismatched_lengths_throw()
    {
        Assert.Throws<ArgumentException>(() => JudgeAgreement.Compare([0.1], [0.1, 0.2]));
    }

    [Fact]
    public void Bucket_granularity_is_configurable()
    {
        // 0.2 and 0.3 land in the same 2-bucket bin but different 10-bucket bins
        Assert.Equal(1.0, JudgeAgreement.Compare([0.2], [0.3], buckets: 2).ExactAgreementRate);
        Assert.Equal(0.0, JudgeAgreement.Compare([0.2], [0.3], buckets: 10).ExactAgreementRate);
    }
}
