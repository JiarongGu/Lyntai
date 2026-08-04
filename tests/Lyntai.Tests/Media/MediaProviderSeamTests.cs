using Lyntai.Media;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Media;

/// <summary>The seam is split by DELIVERY MODE, using the optional-capability pattern Core already uses for
/// <c>IProviderAuth</c> / <c>IProviderVersionInstaller</c>: a backend implements the modes it has, and a
/// caller pattern-matches. A backend that can't stream simply isn't an <see cref="IMediaStreamProvider"/> — so
/// "can you stream?" has a real answer rather than a runtime surprise.</summary>
public class MediaProviderSeamTests
{
    [Fact]
    public void An_inline_backend_is_a_media_provider_and_nothing_more()
    {
        var provider = new FakeMediaProvider();

        Assert.IsAssignableFrom<IMediaProvider>(provider);
        Assert.IsNotAssignableFrom<IMediaJobProvider>(provider);
        Assert.IsNotAssignableFrom<IMediaStreamProvider>(provider);
    }

    [Fact]
    public async Task An_inline_generation_returns_artifacts()
    {
        var provider = new FakeMediaProvider();

        var result = await provider.GenerateAsync(new MediaRequest { Kind = MediaKinds.Image, Prompt = "x" });

        Assert.True(result.IsOk);
        Assert.Equal("image/png", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_probe_answers_without_generating()
    {
        var provider = new FakeMediaProvider();

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal(0, provider.GenerateCalls);   // the point of the probe
    }

    [Fact]
    public async Task A_job_backend_submits_polls_and_fetches()
    {
        var provider = new FakeMediaJobProvider();

        var submitted = await provider.SubmitAsync(new MediaRequest { Kind = MediaKinds.Video, Prompt = "x" });
        var polled = await provider.PollAsync(submitted.Id);
        var fetched = await provider.FetchAsync(submitted.Id);

        Assert.Equal(MediaOperationStatus.Queued, submitted.Status);
        Assert.False(submitted.IsTerminal);
        Assert.Equal(MediaOperationStatus.Succeeded, polled.Status);
        Assert.True(polled.IsTerminal);
        Assert.True(fetched.IsOk);
        Assert.Equal("video/mp4", fetched.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_stream_backend_yields_chunks_then_a_final()
    {
        var provider = new FakeMediaStreamProvider();

        var chunks = new List<MediaChunk>();
        await foreach (var chunk in provider.StreamAsync(new MediaRequest { Kind = MediaKinds.Audio, Prompt = "hi" }))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count(c => c.Data is { Length: > 0 }));
        Assert.True(chunks[^1].Final);
    }
}
