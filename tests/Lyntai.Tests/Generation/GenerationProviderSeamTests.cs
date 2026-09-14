using Lyntai.Generation;
using Lyntai.Lifecycle;
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
        Assert.DoesNotContain(ProviderOperation.Job, provider.Capabilities.Operations);
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

        Assert.Equal(GenerationOperationStatus.Queued, submitted.Status);
        Assert.False(submitted.IsTerminal);
        Assert.Equal(GenerationOperationStatus.Succeeded, polled.Status);
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
