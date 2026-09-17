using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>The vector arithmetic every vector backend backend and every brute-force store shares, so that two
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
}
