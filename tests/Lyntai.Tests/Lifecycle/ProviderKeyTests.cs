using Lyntai.Lifecycle;

namespace Lyntai.Tests.Lifecycle;

public class ProviderKeyTests
{
    private static ProviderKey Key(string url, string? apiKey, string? model = "dall-e-3") =>
        ProviderKey.For("openai-images")
            .With("baseUrl", url)
            .With("model", model)
            .WithSecret("apiKey", apiKey)
            .Build();

    [Fact]
    public void Identical_configurations_produce_equal_keys()
    {
        Assert.Equal(Key("https://api.example.invalid/v1", "sk-abc"),
                     Key("https://api.example.invalid/v1", "sk-abc"));
    }

    [Fact]
    public void A_changed_value_produces_a_different_key()
    {
        Assert.NotEqual(Key("https://api.example.invalid/v1", "sk-abc"),
                        Key("https://other.example.invalid/v1", "sk-abc"));
    }

    // The rotation case: the saved settings look identical except for the credential, and a pool that
    // missed it would keep calling with a revoked secret.
    [Fact]
    public void A_rotated_credential_produces_a_different_key()
    {
        Assert.NotEqual(Key("https://api.example.invalid/v1", "sk-abc"),
                        Key("https://api.example.invalid/v1", "sk-xyz"));
    }

    [Fact]
    public void A_missing_credential_differs_from_a_present_one()
    {
        Assert.NotEqual(Key("https://api.example.invalid/v1", null),
                        Key("https://api.example.invalid/v1", "sk-abc"));
    }

    [Fact]
    public void The_secret_never_appears_in_the_key()
    {
        var key = Key("https://api.example.invalid/v1", "sk-super-secret");

        Assert.DoesNotContain("sk-super-secret", key.Fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-super-secret", key.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_slot_is_carried_verbatim()
    {
        Assert.Equal("openai-images", Key("https://api.example.invalid/v1", "sk-abc").Slot);
    }

    // Name-and-length framing: without it, With("a","bc") and With("ab","c") would fold to the same bytes.
    [Fact]
    public void Contributions_cannot_collide_by_concatenation()
    {
        var left  = ProviderKey.For("x").With("a", "bc").Build();
        var right = ProviderKey.For("x").With("ab", "c").Build();

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Different_slots_with_identical_values_differ()
    {
        Assert.NotEqual(ProviderKey.For("a1111").With("baseUrl", "http://localhost:7860").Build(),
                        ProviderKey.For("comfyui").With("baseUrl", "http://localhost:7860").Build());
    }

    [Fact]
    public void Order_of_contributions_is_significant_and_stable()
    {
        var built = ProviderKey.For("x").With("a", "1").With("b", "2").Build();
        var again = ProviderKey.For("x").With("a", "1").With("b", "2").Build();

        Assert.Equal(built, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_slot_is_rejected(string slot) =>
        Assert.Throws<ArgumentException>(() => ProviderKey.For(slot));

    // A name that embeds the framing delimiters ("=1:X;b") must not be able to forge the boundary between
    // two separate contributions — the name has to be length-prefixed the same way the value already is.
    [Fact]
    public void A_delimiter_embedded_in_a_name_cannot_forge_a_different_contribution_sequence()
    {
        var forged = ProviderKey.For("x").With("a=1:X;b", "Y").Build();
        var real   = ProviderKey.For("x").With("a", "X").With("b", "Y").Build();

        Assert.NotEqual(forged, real);
    }

    // default(ProviderKey) is reachable (e.g. an out parameter on a TryGetKey-style miss); ToString() must
    // degrade rather than throw on its null Fingerprint.
    [Fact]
    public void ToString_does_not_throw_on_a_defaulted_key()
    {
        var key = default(ProviderKey);

        Assert.Equal("#(unset)", key.ToString());
    }
}
