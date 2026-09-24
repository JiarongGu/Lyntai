using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>The vector arithmetic every vector backend and every brute-force store shares, so that two
/// of them rank identically.
///
/// <para><b>`NormalizeInPlace` was two copies in two packages</b> — the ONNX adapter's and the model2vec
/// one's — until D141. Neither could reach the other, Core being their only common dependency, so the
/// zero-length guard was argued in one and merely present in the other.</para></summary>
public class VectorMathTests
{
    [Fact]
    public void Normalizing_scales_to_unit_length_without_changing_direction()
    {
        var v = new[] { 3f, 4f };

        VectorMath.NormalizeInPlace(v);

        Assert.Equal(0.6, v[0], 6);
        Assert.Equal(0.8, v[1], 6);
        Assert.Equal(1.0, Math.Sqrt(v[0] * v[0] + v[1] * v[1]), 6);
    }

    [Fact]
    public void A_ZERO_vector_is_left_alone_rather_than_becoming_NaN()
    {
        // The guard that matters. Dividing by a zero length yields NaN in every component, and NaN compares
        // false against everything — so the vector is stored, never matches anything, and reports no error.
        var v = new float[4];

        VectorMath.NormalizeInPlace(v);

        Assert.All(v, x => Assert.Equal(0f, x));
        Assert.DoesNotContain(v, float.IsNaN);
    }

    [Fact]
    public void An_already_unit_vector_survives_normalization_unchanged()
    {
        var v = new[] { 1f, 0f, 0f };

        VectorMath.NormalizeInPlace(v);

        Assert.Equal([1f, 0f, 0f], v);
    }

    [Fact]
    public void Normalizing_makes_cosine_agree_with_the_plain_dot_product()
    {
        // Why both live on one type: a backend that normalizes and a store that computes cosine must agree,
        // and they only do if the normalization is the same one.
        var a = new[] { 2f, 1f, 0f };
        var b = new[] { 1f, 3f, 0f };
        var expected = VectorMath.Cosine(a, b);

        VectorMath.NormalizeInPlace(a);
        VectorMath.NormalizeInPlace(b);
        var dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        Assert.Equal(expected, dot, 6);
    }

    [Fact]
    public void A_null_vector_is_a_caller_error_rather_than_a_silent_no_op()
    {
        Assert.Throws<ArgumentNullException>(() => VectorMath.NormalizeInPlace(null!));
    }

    // ---- WeightedMeanDirection: several vectors pooled into one unit vector (D177) ---------------------

    [Fact]
    public void The_mean_direction_weights_each_UNIT_vector_and_renormalises_the_sum()
    {
        // [3, 0] and [0, 2] are unit [1, 0] and [0, 1]; weights 1 and 3 give [1, 3]. A raw weighted mean
        // would give [3, 6], an unweighted one [1, 1] — both point elsewhere.
        var pooled = VectorMath.WeightedMeanDirection([[3f, 0f], [0f, 2f]], [1, 3]);

        Assert.Equal(1 / Math.Sqrt(10), pooled[0], 6);
        Assert.Equal(3 / Math.Sqrt(10), pooled[1], 6);
    }

    [Fact]
    public void One_vector_is_its_own_unit_vector()
    {
        Assert.Equal([0.6f, 0.8f], VectorMath.WeightedMeanDirection([[3f, 4f]], [5]));
    }

    [Fact]
    public void A_ZERO_vector_has_no_direction_and_contributes_nothing_whatever_its_weight()
    {
        var pooled = VectorMath.WeightedMeanDirection([[0f, 0f], [2f, 0f]], [100, 1]);

        Assert.Equal([1f, 0f], pooled);
    }

    [Fact]
    public void Directions_that_CANCEL_fall_back_to_the_HEAVIEST_vectors_unit_vector()
    {
        // 1·[1, 0] + 2·[-1, 0] + 1·[1, 0] sums to zero; the heaviest is the second
        var pooled = VectorMath.WeightedMeanDirection([[4f, 0f], [-2f, 0f], [1f, 0f]], [1, 2, 1]);

        Assert.Equal([-1f, 0f], pooled);
    }

    [Fact]
    public void A_TIE_for_heaviest_goes_to_the_first()
    {
        Assert.Equal([1f, 0f], VectorMath.WeightedMeanDirection([[2f, 0f], [-2f, 0f]], [50, 50]));
    }

    [Fact]
    public void Every_vector_ZERO_pools_to_a_zero_vector_rather_than_NaN()
    {
        var pooled = VectorMath.WeightedMeanDirection([[0f, 0f], [0f, 0f]], [1, 1]);

        Assert.Equal([0f, 0f], pooled);
    }

    [Fact]
    public void The_inputs_are_left_as_they_were_and_the_result_is_a_new_array()
    {
        float[] only = [3f, 4f];

        var pooled = VectorMath.WeightedMeanDirection([only], [1]);

        Assert.Equal([3f, 4f], only);
        Assert.NotSame(only, pooled);
    }

    [Fact]
    public void Vectors_of_different_DIMENSIONS_are_refused_rather_than_pooled()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.WeightedMeanDirection([[1f, 0f], [1f, 0f, 0f]], [1, 1]));
    }

    [Fact]
    public void A_weight_count_that_disagrees_with_the_vectors_is_refused()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.WeightedMeanDirection([[1f, 0f], [0f, 1f]], [1]));
    }

    [Fact]
    public void Nothing_to_pool_is_refused()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.WeightedMeanDirection([], []));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_negative_or_non_finite_weight_is_refused(double weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VectorMath.WeightedMeanDirection([[1f, 0f], [0f, 1f]], [1, weight]));
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => VectorMath.WeightedMeanDirection(null!, [1]));
        Assert.Throws<ArgumentNullException>(() => VectorMath.WeightedMeanDirection([[1f]], null!));
    }
}
