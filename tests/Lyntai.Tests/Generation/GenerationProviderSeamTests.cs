using Lyntai.Generation;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The seam is split by DELIVERY MODE, using the optional-capability pattern Core already uses for
/// <c>IProviderAuth</c> / <c>IProviderVersionInstaller</c>: a backend implements the modes it has, and a
/// caller pattern-matches. A backend that can't stream simply isn't an <see cref="IGenerationStreamProvider"/> — so
/// "can you stream?" has a real answer rather than a runtime surprise.</summary>
public class GenerationProviderSeamTests
{
    [Fact]
    public void An_inline_backend_is_a_media_provider_and_nothing_more()
    {
        var provider = new FakeGenerationProvider();

        Assert.IsAssignableFrom<IGenerationProvider>(provider);
        Assert.IsNotAssignableFrom<IGenerationJobProvider>(provider);
        Assert.IsNotAssignableFrom<IGenerationStreamProvider>(provider);
    }

    [Fact]
    public async Task An_inline_generation_returns_artifacts()
    {
        var provider = new FakeGenerationProvider();

        var result = await provider.GenerateAsync(new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "x" });

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

        var submitted = await provider.SubmitAsync(new GenerationRequest { Kind = GenerationKinds.Video, Prompt = "x" });
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
        await foreach (var chunk in provider.StreamAsync(new GenerationRequest { Kind = GenerationKinds.Audio, Prompt = "hi" }))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count(c => c.Data is { Length: > 0 }));
        Assert.True(chunks[^1].Final);
    }
}
