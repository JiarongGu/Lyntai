using Lyntai.Generation;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The contract fact that asks whether a declared delivery is BACKED, driven both ways.
///
/// <para>A type test cannot answer it: <c>StreamAsync(MediaRequest, …)</c> is a default interface member
/// (<b>D127</b>), so every backend "implements" it and <c>provider is IModelProvider</c> is always true. The
/// fact asks whether the declared delivery is SERVED instead, and both tests here drive it.</para></summary>
public class DeclaredDeliveryIsBackedTests
{
    [Fact]
    public void A_backend_that_really_streams_media_PASSES()
    {
        // Both shipped streaming fakes, because one passing could be an accident of its own shape.
        Assert.True(GenerationProviderContract.ServesMediaStream(new FakeGenerationStreamProvider()));
        Assert.True(GenerationProviderContract.ServesMediaStream(new ScriptedStreamProvider()));
    }

    [Fact]
    public void A_backend_that_DECLARES_Stream_and_inherits_the_default_FAILS()
    {
        // `LyingStreamProvider` is the shape a BYO backend can ship: it advertises Stream and never
        // overrides the seam, so every call answers Unsupported after the router has already discarded
        // every alternative. This is the direction a type test cannot see.
        Assert.False(GenerationProviderContract.ServesMediaStream(new LyingStreamProvider()));

        var thrown = Assert.ThrowsAny<Exception>(() =>
            GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements(
                new LyingStreamProvider()));
        Assert.Contains("inherits IModelProvider's default", thrown.Message);
    }

    [Fact]
    public void A_backend_that_does_not_declare_Stream_is_not_asked_about_it()
    {
        // The false-positive direction: an inline-only backend inherits the media stream seam's default, so a
        // fact that asked this of every backend regardless of what it DECLARES would fail every one of them.
        // The contract asks only about modes the backend claimed.
        GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements(
            new FakeGenerationProvider());
    }
}
