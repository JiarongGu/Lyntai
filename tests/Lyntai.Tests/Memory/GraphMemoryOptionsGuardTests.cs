using Lyntai.Memory;
using Lyntai.Memory.Salience;

namespace Lyntai.Tests.Memory;

/// <summary>Every integer knob of <see cref="GraphMemoryOptions"/> is validated where it is SET, through the one
/// guard every memory options record uses — so no use site has to clamp it, and a nonsensical value fails at the
/// line that configured it rather than being silently reinterpreted.</summary>
public class GraphMemoryOptionsGuardTests
{
    public static TheoryData<string, Func<GraphMemoryOptions>> BelowItsFloor => new()
    {
        { nameof(GraphMemoryOptions.Hops), () => new GraphMemoryOptions { Hops = -1 } },
        { nameof(GraphMemoryOptions.HeadlineChars), () => new GraphMemoryOptions { HeadlineChars = 0 } },
        { nameof(GraphMemoryOptions.CoActivationCap), () => new GraphMemoryOptions { CoActivationCap = -1 } },
        { nameof(GraphMemoryOptions.AuthoritativeReserve), () => new GraphMemoryOptions { AuthoritativeReserve = -1 } },
        { nameof(GraphMemoryOptions.AnnotationContext), () => new GraphMemoryOptions { AnnotationContext = -1 } },
        { nameof(GraphMemoryOptions.AnnotationLinkK), () => new GraphMemoryOptions { AnnotationLinkK = -1 } },
        { nameof(GraphMemoryOptions.AnnotationKnownSubjects), () => new GraphMemoryOptions { AnnotationKnownSubjects = -1 } },
        { nameof(GraphMemoryOptions.CandidateMultiplier), () => new GraphMemoryOptions { CandidateMultiplier = 0 } },
        { nameof(GraphMemoryOptions.DefaultLimit), () => new GraphMemoryOptions { DefaultLimit = 0 } },
        { nameof(GraphMemoryOptions.SimilarityK), () => new GraphMemoryOptions { SimilarityK = -1 } },
        { nameof(GraphMemoryOptions.RecallReinforceCap), () => new GraphMemoryOptions { RecallReinforceCap = -1 } },
        { nameof(GraphMemoryOptions.VerificationDepth), () => new GraphMemoryOptions { VerificationDepth = -1 } },
        { nameof(GraphMemoryOptions.ReviewLogCap), () => new GraphMemoryOptions { ReviewLogCap = -1 } },
        { nameof(GraphMemoryOptions.ExpandCharBudget), () => new GraphMemoryOptions { ExpandCharBudget = -1 } },
    };

    [Theory]
    [MemberData(nameof(BelowItsFloor))]
    public void An_integer_knob_below_its_floor_is_refused_naming_itself(string property, Func<GraphMemoryOptions> build)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(build);

        Assert.Equal(property, ex.ParamName);
        Assert.Contains($"{nameof(GraphMemoryOptions)}.{property}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documented_zero_switches_and_nulls_stay_legal()
    {
        _ = new GraphMemoryOptions
        {
            Hops = 0, CoActivationCap = 0, AuthoritativeReserve = 0, AnnotationContext = 0, AnnotationLinkK = 0,
            AnnotationKnownSubjects = 0, SimilarityK = 0, RecallReinforceCap = 0, ReviewLogCap = 0,
            ExpandCharBudget = 0, VerificationDepth = null,
        };
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    [InlineData(0.5)]
    public void A_salience_ceiling_that_is_not_a_finite_number_at_or_above_one_is_refused(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalienceOptions { MaxSalience = value });
    }
}
