using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Lyntai.Generation.Jobs;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Jobs;
using Lyntai.Storage.InMemory;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The ComfyUI backend against a REAL local server — the measurement behind its own class header:
/// every endpoint path and response field name is documented surface, and only a live run can say whether
/// the documented defaults are the observed ones. One journey exercises the whole queued surface: probe
/// (<c>system_stats</c> + <c>system.comfyui_version</c>), submit (<c>prompt</c> → <c>prompt_id</c>, with the
/// dotted-path prompt substitution), poll (<c>history/{id}</c> keyed by id, <c>status.completed</c>), fetch
/// (the <c>outputs</c> walk to <c>filename</c>/<c>subfolder</c>/<c>type</c>), and the <c>view</c> URI —
/// which this test dereferences, because a URI nobody can GET is not an artifact.
///
/// <para>Skipped without <c>LYNTAI_COMFYUI_URL</c> (e.g. <c>http://127.0.0.1:8188</c>) and
/// <c>LYNTAI_COMFYUI_CHECKPOINT</c> (a checkpoint filename the server's <c>models/checkpoints</c> holds,
/// e.g. an SD 1.5) — except the mesh journey, which needs no model and only the URL, and the Wan journeys, which
/// need <c>LYNTAI_COMFYUI_WAN_MODEL</c> instead. A CPU render is minutes, not seconds — this suite is a
/// measurement, not a regression gate.</para></summary>
public class ComfyUiLiveTests(ITestOutputHelper output)
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

    private static string? WanModel => Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_WAN_MODEL");

    /// <summary>Wan 2.2 TI2V-5B in API format, from ComfyUI's own <c>video_wan2_2_5B_ti2v</c> template — its sampler
    /// (20 <c>uni_pc</c> steps, cfg 5, shift 8) and 24 fps — shrunk to 640×352 and 25 frames (Wan takes 4n + 1) so a
    /// 12 GB laptop GPU answers in minutes. The text encoder and VAE are Comfy-Org's repackaged names. With a start
    /// image, node 56 loads the uploaded frame into the latent's <c>start_image</c>.</summary>
    private static string WanWorkflow(string model, bool startImage) => """
        {
          "37": {"class_type": "UNETLoader", "inputs": {"unet_name": "UNET_NAME", "weight_dtype": "default"}},
          "38": {"class_type": "CLIPLoader", "inputs": {"clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors",
                 "type": "wan", "device": "default"}},
          "39": {"class_type": "VAELoader", "inputs": {"vae_name": "wan2.2_vae.safetensors"}},
          "48": {"class_type": "ModelSamplingSD3", "inputs": {"model": ["37", 0], "shift": 8}},
          "6": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["38", 0], "text": "PLACEHOLDER"}},
          "7": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["38", 0], "text": "static, blurry, low quality"}},
          LOAD_IMAGE
          "55": {"class_type": "Wan22ImageToVideoLatent", "inputs": {"vae": ["39", 0], "width": 640, "height": 352,
                 "length": 25, "batch_size": 1 START_IMAGE}},
          "3": {"class_type": "KSampler", "inputs": {"model": ["48", 0], "positive": ["6", 0], "negative": ["7", 0],
                "latent_image": ["55", 0], "seed": 42, "steps": 20, "cfg": 5, "sampler_name": "uni_pc",
                "scheduler": "simple", "denoise": 1}},
          "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["39", 0]}},
          "57": {"class_type": "CreateVideo", "inputs": {"images": ["8", 0], "fps": 24}},
          "58": {"class_type": "SaveVideo", "inputs": {"video": ["57", 0], "filename_prefix": "video/lyntai-live-wan",
                 "format": "auto", "codec": "auto"}}
        }
        """
        .Replace("UNET_NAME", model)
        .Replace("LOAD_IMAGE", startImage ? "\"56\": {\"class_type\": \"LoadImage\", \"inputs\": {\"image\": \"none\"}}," : "")
        .Replace("START_IMAGE", startImage ? ", \"start_image\": [\"56\", 0]" : "");

    /// <summary>A real video MODEL through the backend, where <see
    /// cref="A_video_producing_workflow_comes_back_as_a_view_URI_with_a_video_media_type"/> needed none: text to
    /// video, then image to video with the start frame uploaded through the <c>first-frame</c> role's
    /// <c>input-path</c>. Skipped without <c>LYNTAI_COMFYUI_WAN_MODEL</c>, the server's Wan 2.2 TI2V-5B file.</summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_video_model_renders_through_the_provider_from_text_and_from_a_first_frame(bool fromImage)
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(WanModel),
            "set LYNTAI_COMFYUI_URL to a running ComfyUI and LYNTAI_COMFYUI_WAN_MODEL to its Wan 2.2 TI2V-5B file");

        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = BaseUrl! }, () => new HttpClient());
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflow"] = WanWorkflow(WanModel!, fromImage),
            ["prompt-path"] = "6.inputs.text",
        };
        if (fromImage) options[$"input-path:{MediaInputRoles.FirstFrame}"] = "56.inputs.image";
        var clock = Stopwatch.StartNew();

        var submitted = await provider.SubmitAsync(new MediaRequest
        {
            Kind = ProviderKinds.Video,
            Prompt = "a red ball rolls slowly across a wooden table, soft daylight",
            Inputs = fromImage ? [new MediaInput("image/png", Data: StartFrame(640, 352), Role: MediaInputRoles.FirstFrame)] : [],
            Options = options,
        });
        Assert.True(submitted.Status == QueuedOperationStatus.Queued, submitted.Detail);

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);   // a cold first run loads ~18 GB of weights
        QueuedOperation polled;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            polled = await provider.PollAsync(submitted.Id);
            Assert.True(polled.Status != QueuedOperationStatus.Failed, polled.Detail);
        }
        while (polled.Status != QueuedOperationStatus.Succeeded && DateTime.UtcNow < deadline);
        Assert.True(polled.Status == QueuedOperationStatus.Succeeded,
            $"the render did not finish inside the budget — last: {polled.Status} {polled.Detail}");
        output.WriteLine($"{(fromImage ? "image" : "text")}-to-video rendered in {clock.Elapsed.TotalSeconds:F0} s");

        var fetched = await provider.FetchAsync(submitted.Id);
        Assert.True(fetched.IsOk, fetched.Detail);
        var artifact = Assert.Single(fetched.Artifacts);
        Assert.Equal("video/mp4", artifact.MediaType);

        using var http = new HttpClient();
        var mp4 = await http.GetByteArrayAsync(artifact.Uri);
        Assert.Equal("ftyp"u8.ToArray(), mp4[4..8]);
        // 25 frames at 24 fps: a one-frame or a wrong-rate fallback would still be a valid MP4
        Assert.InRange(Mp4Seconds(mp4), 25 / 24.0 - 0.2, 25 / 24.0 + 0.2);
    }

    /// <summary>The movie header's duration over its timescale — <c>mvhd</c> version 0 or 1.</summary>
    private static double Mp4Seconds(byte[] mp4)
    {
        var at = mp4.AsSpan().IndexOf("mvhd"u8);
        Assert.True(at > 0, "no mvhd box: not a playable MP4");
        return mp4[at + 4] == 1
            ? (double)ReadBigEndian64(mp4, at + 28) / (uint)ReadBigEndian(mp4, at + 24)
            : (double)(uint)ReadBigEndian(mp4, at + 20) / (uint)ReadBigEndian(mp4, at + 16);
    }

    private static long ReadBigEndian64(byte[] bytes, int at) =>
        ((long)(uint)ReadBigEndian(bytes, at) << 32) | (uint)ReadBigEndian(bytes, at + 4);

    /// <summary>An 8-bit RGB PNG — a red disc on a light gradient — built by hand, as <see cref="Cube"/> is.</summary>
    private static byte[] StartFrame(int width, int height)
    {
        var rows = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);   // filter byte 0: none
            for (var x = 0; x < width; x++)
            {
                var (dx, dy) = (x - width / 4, y - height / 2);
                var disc = dx * dx + dy * dy < 50 * 50;
                rows[row + 1 + x * 3] = disc ? (byte)220 : (byte)(180 + y * 60 / height);
                rows[row + 2 + x * 3] = disc ? (byte)30 : (byte)(150 + y * 60 / height);
                rows[row + 3 + x * 3] = disc ? (byte)30 : (byte)(110 + y * 60 / height);
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest)) z.Write(rows);

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8; header[9] = 2;   // 8-bit RGB; compression, filter and interlace all 0
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();

        static void Chunk(Stream to, string type, byte[] data)
        {
            var body = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, body);
            data.CopyTo(body, 4);
            var word = new byte[4];
            WriteBigEndian(word, 0, data.Length);
            to.Write(word);
            to.Write(body);
            WriteBigEndian(word, 0, (int)Crc32(body));
            to.Write(word);
        }
    }

    // PNG's chunk checksum; the loader ComfyUI uses verifies it
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1)));
        }
        return ~crc;
    }

    private static void WriteBigEndian(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)(value >> 24); bytes[at + 1] = (byte)(value >> 16);
        bytes[at + 2] = (byte)(value >> 8); bytes[at + 3] = (byte)value;
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

    /// <summary>The mesh chain as a DURABLE pipeline job, with no 3D MODEL: stage 1 takes a hand-made GLB as
    /// inline bytes and saves it again, stage 2 chains that mesh's view URI (picked by <c>InputMediaType</c>) and
    /// saves it again, and stage 3 rasterizes it into a PNG. The job runner drives it over an in-memory store, so
    /// every stage is submitted, checkpointed, polled and fetched by the library's own handler — and it measures
    /// the upload binding both ways (bytes, then a fetched URI), the <c>3d</c> output collection and its media
    /// type, and that a mesh chains into an image when the backend RASTERIZES it.</summary>
    [SkippableFact]
    public async Task A_mesh_chain_runs_as_a_durable_pipeline_job_mesh_to_mesh_to_rendered_image()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), "set LYNTAI_COMFYUI_URL to a running ComfyUI");

        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = BaseUrl! }, () => new HttpClient());
        IModelProvider[] providers = [provider];
        var clock = Stopwatch.StartNew();
        var sink = new TimedSink(clock);
        var handler = new GenerationPipelineJobHandler(new MediaRouter(providers), providers, sink,
            new GenerationPipelineJobOptions { PollDelay = TimeSpan.FromSeconds(1) });
        var store = new InMemoryJobStore();
        var options = new LyntaiOptions();
        var runner = new JobRunner(store, new JobHandlerRegistry([handler]), options);
        string[] comfy = [provider.Id];

        var id = await new JobQueue(store, options).EnqueueAsync(new JobSpec("default", GenerationPipelineJobHandler.JobType,
            new GenerationPipelineJob(
            [
                new GenerationPipelineJobStage(comfy, new MediaRequest
                {
                    Kind = ProviderKinds.Model3d,
                    Inputs = [new MediaInput("model/gltf-binary", Data: Cube())],
                    Options = Graph(ResaveWorkflow),
                }),
                new GenerationPipelineJobStage(comfy, new MediaRequest { Kind = ProviderKinds.Model3d, Options = Graph(ResaveWorkflow) })
                {
                    InputMediaType = "model/*",
                },
                new GenerationPipelineJobStage(comfy, new MediaRequest { Kind = ProviderKinds.Image, Options = Graph(RenderWorkflow) }),
            ]).ToJson()));

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
        JobRecord job;
        var passes = 0;
        while (true)
        {
            passes += await runner.RunOnceAsync();
            job = (await store.GetAsync(id))!;
            if (job.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Dead or JobStatus.Cancelled ||
                DateTime.UtcNow > deadline)
                break;
            await Task.Delay(250);
        }
        output.WriteLine($"job {job.Status} after {clock.Elapsed.TotalSeconds:F1} s over {passes} handler runs");
        foreach (var (stage, at, artifact) in sink.Received)
            output.WriteLine($"  stage {stage + 1} delivered at {at.TotalSeconds:F1} s: {artifact.MediaType} {artifact.Uri}");
        Assert.True(job.Status == JobStatus.Succeeded, $"{job.Status}: {job.LastError}");

        Assert.Equal([0, 1, 2], sink.Deliveries.Select(d => d.StageIndex!.Value));
        Assert.Equal([false, false, true], sink.Deliveries.Select(d => d.IsFinal));
        Assert.All(sink.Deliveries, d => Assert.Equal(provider.Id, d.ProviderId));
        using var http = new HttpClient();

        foreach (var delivery in sink.Deliveries.Take(2))
        {
            var mesh = Assert.Single(delivery.Artifacts);
            Assert.Equal("model/gltf-binary", mesh.MediaType);
            Assert.Equal("glTF"u8.ToArray(), (await http.GetByteArrayAsync(mesh.Uri))[..4]);
        }

        var image = Assert.Single(sink.Deliveries[2].Artifacts);
        Assert.Equal("image/png", image.MediaType);
        var png = await http.GetByteArrayAsync(image.Uri);
        Assert.Equal((256, 256), (ReadBigEndian(png, 16), ReadBigEndian(png, 20)));
        Assert.Equal((8, 2), (png[24], png[25]));   // 8-bit RGB, which NotBlank reads
        Assert.True(NotBlank(png), "the render is uniformly background — nothing was rasterized");
    }

    /// <summary>Every delivery, stamped with when it arrived.</summary>
    private sealed class TimedSink(Stopwatch clock) : IGenerationArtifactSink
    {
        public List<GenerationArtifactDelivery> Deliveries { get; } = [];

        public IEnumerable<(int Stage, TimeSpan At, MediaArtifact Artifact)> Received =>
            _arrived.SelectMany(a => a.Delivery.Artifacts.Select(artifact => (a.Delivery.StageIndex ?? -1, a.At, artifact)));

        private readonly List<(GenerationArtifactDelivery Delivery, TimeSpan At)> _arrived = [];

        public Task ReceiveAsync(GenerationArtifactDelivery delivery, CancellationToken ct = default)
        {
            Deliveries.Add(delivery);
            _arrived.Add((delivery, clock.Elapsed));
            return Task.CompletedTask;
        }
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
