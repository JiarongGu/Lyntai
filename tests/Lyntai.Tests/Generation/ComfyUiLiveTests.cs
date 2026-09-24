using System.IO.Compression;
using System.Text;
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
/// e.g. an SD 1.5) — except the mesh journey, which needs no model and only the URL. A CPU render is
/// minutes, not seconds — this suite is a measurement, not a regression gate.</para></summary>
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

    /// <summary>The same SD graph, batched to 8 frames and encoded to MP4 by the CORE CreateVideo/SaveVideo
    /// nodes. No video MODEL is involved, deliberately: the provider never sees a model — only the history
    /// document — and the video-file SHAPE of that document (which collection a video lands under, its
    /// subfolder, what the view URI serves) is the thing no image run can measure. The prefix routes into a
    /// subfolder on purpose, so the video path exercises that field too.</summary>
    private static string VideoWorkflow(string checkpoint) => """
        {
          "3": {"class_type": "KSampler", "inputs": {"cfg": 7, "denoise": 1, "latent_image": ["5", 0],
                "model": ["4", 0], "negative": ["7", 0], "positive": ["6", 0],
                "sampler_name": "euler", "scheduler": "normal", "seed": 42, "steps": 4}},
          "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "CKPT_NAME"}},
          "5": {"class_type": "EmptyLatentImage", "inputs": {"batch_size": 8, "height": 256, "width": 256}},
          "6": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["4", 1], "text": "PLACEHOLDER"}},
          "7": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["4", 1], "text": "blurry, low quality"}},
          "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
          "10": {"class_type": "CreateVideo", "inputs": {"images": ["8", 0], "fps": 8}},
          "11": {"class_type": "SaveVideo", "inputs": {"video": ["10", 0],
                 "filename_prefix": "video/lyntai-live", "format": "auto", "codec": "auto"}}
        }
        """.Replace("CKPT_NAME", checkpoint);

    [SkippableFact]
    public async Task A_video_producing_workflow_comes_back_as_a_view_URI_with_a_video_media_type()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Checkpoint),
            "set LYNTAI_COMFYUI_URL to a running ComfyUI and LYNTAI_COMFYUI_CHECKPOINT to a checkpoint it holds");

        var provider = new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = BaseUrl! },
            () => new HttpClient());

        var submitted = await provider.SubmitAsync(new MediaRequest
        {
            Kind = ProviderKinds.Video,
            Prompt = "a red square on a white background",
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["workflow"] = VideoWorkflow(Checkpoint!),
                ["prompt-path"] = "6.inputs.text",
            },
        });
        Assert.True(submitted.Status == QueuedOperationStatus.Queued, submitted.Detail);

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
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

        var fetched = await provider.FetchAsync(submitted.Id);
        Assert.True(fetched.IsOk, fetched.Detail);
        var artifact = Assert.Single(fetched.Artifacts);

        // the three claims only a video output can test: the walk finds it under whatever collection the
        // server files a video in, the extension maps to a video/* type, and the bytes stay behind the URI
        Assert.Equal("video/mp4", artifact.MediaType);
        Assert.Null(artifact.Data);
        Assert.False(string.IsNullOrWhiteSpace(artifact.Uri));

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(artifact.Uri);
        Assert.True(bytes.Length > 12 && bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p',
            "the view URI did not serve an MP4 container");
    }

    /// <summary>Stage 1: load the uploaded GLB headlessly (<c>Load3DAdvanced</c>; the browser-bound
    /// <c>Load3D</c> needs a viewer) and save it again as the stage's mesh output.</summary>
    private const string ResaveWorkflow = """
        {
          "1": {"class_type": "Load3DAdvanced",
                "inputs": {"model_file": "none", "viewport_state": {}, "width": 256, "height": 256}},
          "2": {"class_type": "Get3DComponents", "inputs": {"model_3d": ["1", 0]}},
          "3": {"class_type": "SaveGLB", "inputs": {"mesh": ["2", 0], "filename_prefix": "3d/lyntai-live"}}
        }
        """;

    /// <summary>Stage 2: the chained mesh, rasterized server-side from an auto-framed front view.</summary>
    private const string RenderWorkflow = """
        {
          "1": {"class_type": "Load3DAdvanced",
                "inputs": {"model_file": "none", "viewport_state": {}, "width": 256, "height": 256}},
          "2": {"class_type": "Get3DComponents", "inputs": {"model_3d": ["1", 0]}},
          "3": {"class_type": "RenderMesh", "inputs": {"mesh": ["2", 0], "mode": "solid",
                "width": 256, "height": 256, "background": "#000000"}},
          "4": {"class_type": "SaveImage", "inputs": {"images": ["3", 0], "filename_prefix": "lyntai-live-mesh"}}
        }
        """;

    private static Dictionary<string, string> Graph(string workflow) =>
        new(StringComparer.OrdinalIgnoreCase) { ["workflow"] = workflow, ["input-path"] = "1.inputs.model_file" };

    /// <summary>The mesh stage as a two-stage pipeline, with no 3D MODEL: stage 1 takes a hand-made GLB as
    /// inline bytes and yields a <see cref="ProviderKinds.Model3d"/> artifact; stage 2 chains that artifact's
    /// view URI through <c>input-path</c> into a render graph and yields a PNG. So it measures the upload
    /// binding both ways (bytes, then a fetched URI), the <c>3d</c> output collection and its media type, and
    /// that a mesh chains into an image when the backend RASTERIZES it.</summary>
    [SkippableFact]
    public async Task A_mesh_uploads_saves_and_chains_into_a_rendered_image_through_a_pipeline()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), "set LYNTAI_COMFYUI_URL to a running ComfyUI");

        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = BaseUrl! }, () => new HttpClient());
        var router = new SubmitPollFetch(new MediaRouter([provider]), provider);
        ProviderCandidate[] comfy = [new(provider.Id)];

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(new MediaRequest
            {
                Kind = ProviderKinds.Model3d,
                Inputs = [new MediaInput("model/gltf-binary", Data: Cube())],
                Options = Graph(ResaveWorkflow),
            }, comfy),
            new GenerationStage(new MediaRequest { Kind = ProviderKinds.Image, Options = Graph(RenderWorkflow) }, comfy),
        ]);

        Assert.True(result.IsOk, $"stage {result.FailedAt + 1}: {result.Verdict} {result.Detail}");
        using var http = new HttpClient();

        var mesh = Assert.Single(result.Stages[0].Artifacts);
        Assert.Equal("model/gltf-binary", mesh.MediaType);
        Assert.Equal("glTF"u8.ToArray(), (await http.GetByteArrayAsync(mesh.Uri))[..4]);

        var image = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", image.MediaType);
        var png = await http.GetByteArrayAsync(image.Uri);
        Assert.Equal((256, 256), (ReadBigEndian(png, 16), ReadBigEndian(png, 20)));
        Assert.Equal((8, 2), (png[24], png[25]));   // 8-bit RGB, which NotBlank reads
        Assert.True(NotBlank(png), "the render is uniformly background — nothing was rasterized");
    }

    /// <summary><c>RunPipelineAsync</c> drives the INLINE door and this backend is queued-only, so each stage
    /// is bridged here: submitted through a real <see cref="MediaRouter"/>, whose capability filter is part of
    /// what is measured, then polled and fetched.</summary>
    private sealed class SubmitPollFetch(MediaRouter router, IMediaJobProvider job) : IMediaRouter
    {
        public async Task<MediaResponse> GenerateAsync(
            IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
        {
            var submitted = await router.SubmitAsync(candidates, request, ct);
            if (submitted.Operation.Status == QueuedOperationStatus.Failed)
                return MediaResponse.Failure(ProviderVerdict.Failed, $"not accepted: {submitted.Operation.Detail}");

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            QueuedOperation polled;
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                polled = await job.PollAsync(submitted.Operation.Id, ct);
                if (polled.Status == QueuedOperationStatus.Failed)
                    return MediaResponse.Failure(ProviderVerdict.Failed, $"the run failed: {polled.Detail}");
            }
            while (polled.Status != QueuedOperationStatus.Succeeded && DateTime.UtcNow < deadline);

            return polled.Status == QueuedOperationStatus.Succeeded
                ? await job.FetchAsync(submitted.Operation.Id, ct)
                : MediaResponse.Failure(ProviderVerdict.Timeout, $"not finished inside the budget: {polled.Detail}");
        }

        public Task<MediaSubmission> SubmitAsync(
            IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default) =>
            router.SubmitAsync(candidates, request, ct);

        public IAsyncEnumerable<MediaChunk> StreamAsync(
            IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("the pipeline drives the inline door");
    }

    /// <summary>A unit cube as a minimal GLB: 8 positions and 12 triangles, no normals or material.</summary>
    private static byte[] Cube()
    {
        float[] positions = [-.5f, -.5f, -.5f, .5f, -.5f, -.5f, .5f, .5f, -.5f, -.5f, .5f, -.5f,
                             -.5f, -.5f, .5f, .5f, -.5f, .5f, .5f, .5f, .5f, -.5f, .5f, .5f];
        ushort[] indices = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 4, 7, 0, 7, 3,
                            1, 2, 6, 1, 6, 5, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2];
        var bin = new byte[96 + 72];   // both views already end on a 4-byte boundary
        Buffer.BlockCopy(positions, 0, bin, 0, 96);
        Buffer.BlockCopy(indices, 0, bin, 96, 72);

        var json = """
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
             "buffers":[{"byteLength":168}],
             "bufferViews":[{"buffer":0,"byteLength":96,"target":34962},
                            {"buffer":0,"byteOffset":96,"byteLength":72,"target":34963}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":8,"type":"VEC3",
                           "min":[-0.5,-0.5,-0.5],"max":[0.5,0.5,0.5]},
                          {"bufferView":1,"componentType":5123,"count":36,"type":"SCALAR"}]}
            """;
        var chunk = Encoding.ASCII.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));   // space-padded, per spec

        using var glb = new MemoryStream();
        using var w = new BinaryWriter(glb);   // little-endian, as GLB is
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + chunk.Length + 8 + bin.Length));
        w.Write((uint)chunk.Length); w.Write(0x4E4F534Au); w.Write(chunk);   // "JSON"
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin);        // "BIN\0"
        w.Flush();
        return glb.ToArray();
    }

    /// <summary>Whether any pixel of an 8-bit RGB PNG is non-zero. A scanline of zero pixels filters to zero
    /// under every PNG filter, so any non-zero byte past each row's filter byte is a non-zero pixel.</summary>
    private static bool NotBlank(byte[] png)
    {
        using var idat = new MemoryStream();
        for (var at = 8; at + 8 <= png.Length;)
        {
            var length = ReadBigEndian(png, at);
            if (Encoding.ASCII.GetString(png, at + 4, 4) == "IDAT") idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var rows = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(rows);

        var stride = 1 + ReadBigEndian(png, 16) * 3;
        var bytes = rows.ToArray();
        for (var i = 0; i < bytes.Length; i++)
            if (i % stride != 0 && bytes[i] != 0) return true;
        return false;
    }

    private static int ReadBigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
