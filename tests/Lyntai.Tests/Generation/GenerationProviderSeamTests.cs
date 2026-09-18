using Lyntai.Generation;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>What a backend serves is answered TWO ways now, and the split is deliberate (D127).
///
/// <para>Request-to-result operations — complete, stream, embed — are declared in
/// <see cref="ProviderCapabilities"/> and checked as DATA, because they differ by content type rather than
/// by contract shape. The stateful JOB protocol keeps an interface of its own: submit/poll/fetch/cancel is
/// keyed on a handle and is meaningless one method at a time, so "can you do jobs?" stays a type
/// question.</para></summary>
public class GenerationProviderSeamTests
{
    [Fact]
    public void An_inline_backend_declares_inline_and_nothing_more()
    {
        var provider = new FakeGenerationProvider();

        Assert.IsAssignableFrom<IModelProvider>(provider);

        // the JOB protocol is still a type question — it is a contract shape, not a content type
        Assert.IsNotAssignableFrom<IGenerationJobProvider>(provider);

        // …while streaming is now a DATA question. Asserting the absence this way is what the old
        // type-based check bought, and it survives the fold that removed the interface it tested.
        Assert.Contains(ProviderOperation.Complete, provider.Capabilities.Operations);
        Assert.DoesNotContain(ProviderOperation.Stream, provider.Capabilities.Operations);
        Assert.DoesNotContain(ProviderOperation.Queued, provider.Capabilities.Operations);
    }

    [Fact]
    public async Task An_inline_generation_returns_artifacts()
    {
        var provider = new FakeGenerationProvider();

        var result = await provider.GenerateAsync(new GenerationRequest { Kind = ProviderKinds.Image, Prompt = "x" });

        Assert.True(result.IsOk);
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_probe_answers_without_generating()
    {
        var provider = new FakeGenerationProvider();

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal(0, provider.GenerateCalls);   // the point of the probe
    }

    [Fact]
    public async Task A_job_backend_submits_polls_and_fetches()
    {
        var provider = new FakeGenerationJobProvider();

        var submitted = await provider.SubmitAsync(new GenerationRequest { Kind = ProviderKinds.Video, Prompt = "x" });
        var polled = await provider.PollAsync(submitted.Id);
        var fetched = await provider.FetchAsync(submitted.Id);

        Assert.Equal(QueuedOperationStatus.Queued, submitted.Status);
        Assert.False(submitted.IsTerminal);
        Assert.Equal(QueuedOperationStatus.Succeeded, polled.Status);
        Assert.True(polled.IsTerminal);
        Assert.True(fetched.IsOk);
        Assert.Equal("video/mp4", fetched.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_stream_backend_yields_chunks_then_a_final()
    {
        var provider = new FakeGenerationStreamProvider();

        var chunks = new List<GenerationChunk>();
        await foreach (var chunk in provider.StreamAsync(new GenerationRequest { Kind = ProviderKinds.Audio, Prompt = "hi" }))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count(c => c.Data is { Length: > 0 }));
        Assert.True(chunks[^1].Final);
    }
}

/// <summary>The contract fact that asks whether a declared delivery is BACKED, driven both ways.
///
/// <para>It exists because the assertion it replaces could not fail. Until 2026-09-16 the Stream arm read
/// <c>Assert.True(provider is IModelProvider, …)</c> against a parameter already typed
/// <see cref="IModelProvider"/> — always true. It was a real type test before <b>D127</b> collapsed the
/// domain seams and turned <c>StreamAsync(GenerationRequest, …)</c> into a default interface member; the
/// rename made every backend "implement" it, and the fact went vacuous in the same release, silently. A
/// test that cannot fail reports coverage it does not have, which is worse than no test.</para></summary>
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
        // every alternative. This is the direction the old assertion could not see.
        Assert.False(GenerationProviderContract.ServesMediaStream(new LyingStreamProvider()));

        var thrown = Assert.ThrowsAny<Exception>(() =>
            GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements(
                new LyingStreamProvider()));
        Assert.Contains("inherits IModelProvider's default", thrown.Message);
    }

    [Fact]
    public void A_backend_that_does_not_declare_Stream_is_not_asked_about_it()
    {
        // The false-positive direction: no shipped media backend overrides the media stream seam (GEN6 is
        // still open), so a fact that asked this of every backend regardless of what it DECLARES would fail
        // all five. The contract asks only about modes the backend claimed.
        GenerationProviderContract.Its_declared_deliveries_are_backed_by_the_interfaces_it_implements(
            new FakeGenerationProvider());
    }
}
