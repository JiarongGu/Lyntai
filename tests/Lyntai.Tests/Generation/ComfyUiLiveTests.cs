using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Inference;

namespace Lyntai.Tests.Generation;

/// <summary>The ComfyUI backend against a REAL local server — the measurement its own class header demands:
/// every endpoint path and response field name was documented-surface, and only a live run can say whether
/// the documented defaults are the observed ones. One journey exercises the whole queued surface: probe
/// (<c>system_stats</c> + <c>system.comfyui_version</c>), submit (<c>prompt</c> → <c>prompt_id</c>, with the
/// dotted-path prompt substitution), poll (<c>history/{id}</c> keyed by id, <c>status.completed</c>), fetch
/// (the <c>outputs</c> walk to <c>filename</c>/<c>subfolder</c>/<c>type</c>), and the <c>view</c> URI —
/// which this test dereferences, because a URI nobody can GET is not an artifact.
///
/// <para>Skipped without <c>LYNTAI_COMFYUI_URL</c> (e.g. <c>http://127.0.0.1:8188</c>) and
/// <c>LYNTAI_COMFYUI_CHECKPOINT</c> (a checkpoint filename the server's <c>models/checkpoints</c> holds,
/// e.g. an SD 1.5). A CPU render is minutes, not seconds — this suite is a measurement, not a regression
/// gate.</para></summary>
public class ComfyUiLiveTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_URL");
    private static string? Checkpoint => Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_CHECKPOINT");

    /// <summary>The classic SD txt2img graph in API format, small and fast: the checkpoint is the host's,
    /// the prompt node is "6" (which is what <c>prompt-path</c> points at), 4 steps at 256×256.</summary>
    private static string Workflow(string checkpoint) => """
        {
          "3": {"class_type": "KSampler", "inputs": {"cfg": 7, "denoise": 1, "latent_image": ["5", 0],
                "model": ["4", 0], "negative": ["7", 0], "positive": ["6", 0],
                "sampler_name": "euler", "scheduler": "normal", "seed": 42, "steps": 4}},
          "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "CKPT_NAME"}},
          "5": {"class_type": "EmptyLatentImage", "inputs": {"batch_size": 1, "height": 256, "width": 256}},
          "6": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["4", 1], "text": "PLACEHOLDER"}},
          "7": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["4", 1], "text": "blurry, low quality"}},
          "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
          "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": "lyntai-live", "images": ["8", 0]}}
        }
        """.Replace("CKPT_NAME", checkpoint);

    [SkippableFact]
    public async Task A_real_server_answers_the_whole_queued_surface_probe_submit_poll_fetch_view()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Checkpoint),
            "set LYNTAI_COMFYUI_URL to a running ComfyUI and LYNTAI_COMFYUI_CHECKPOINT to a checkpoint it holds");

        var provider = new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = BaseUrl! },
            () => new HttpClient());

        // system_stats, and the version field the probe reads from it
        var probe = await provider.ProbeAsync();
        Assert.True(probe.Available, probe.Detail);
        Assert.False(string.IsNullOrWhiteSpace(probe.Version),
            "system_stats answered but carried no system.comfyui_version — the probe's field is wrong");

        // submit: the prompt lands in the graph via the dotted path, and the reply carries prompt_id
        var submitted = await provider.SubmitAsync(new MediaRequest
        {
            Kind = ProviderKinds.Image,
            Prompt = "a red square on a white background",
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["workflow"] = Workflow(Checkpoint!),
                ["prompt-path"] = "6.inputs.text",
            },
        });
        Assert.True(submitted.Status == QueuedOperationStatus.Queued, submitted.Detail);
        Assert.False(string.IsNullOrWhiteSpace(submitted.Id), "no prompt_id came back");

        // poll: history/{id} keyed by the id, finished when status.completed reads true
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);   // first run also loads the checkpoint
        QueuedOperation polled;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            polled = await provider.PollAsync(submitted.Id);
            Assert.True(polled.Status != QueuedOperationStatus.Failed, polled.Detail);
        }
        while (polled.Status != QueuedOperationStatus.Succeeded && DateTime.UtcNow < deadline);
        Assert.True(polled.Status == QueuedOperationStatus.Succeeded,
            $"the render did not finish inside the budget — last: {polled.Status} {polled.Detail}");

        // fetch: the outputs walk yields view URIs, never bytes (the backend's own rule)
        var fetched = await provider.FetchAsync(submitted.Id);
        Assert.True(fetched.IsOk, fetched.Detail);
        var artifact = Assert.Single(fetched.Artifacts);
        Assert.Null(artifact.Data);
        Assert.False(string.IsNullOrWhiteSpace(artifact.Uri));

        // ...and the view URI actually serves the PNG at the graph's size, or it was no artifact at all
        using var http = new HttpClient();
        var png = await http.GetByteArrayAsync(artifact.Uri);
        Assert.True(png.Length > 24 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "view did not serve a PNG");
        Assert.Equal((256, 256), (ReadBigEndian(png, 16), ReadBigEndian(png, 20)));
    }

    private static int ReadBigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
