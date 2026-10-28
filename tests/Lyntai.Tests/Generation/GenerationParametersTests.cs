using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>A request naming a checkpoint, LoRAs and sampling parameters: what each local backend does with
/// each — the argv, the payload, the graph fields — and that one it cannot honour is refused BEFORE any call, as
/// an agent door's unsupported ability is. The same mappings run against real engines in the live suites.</summary>
public class GenerationParametersTests : IDisposable
{
    private readonly List<ScratchDir> _scratch = [];

    public void Dispose() => _scratch.ForEach(s => s.Dispose());

    private static MediaRequest Ask(string prompt = "a red square") =>
        new() { Kind = ProviderKinds.Image, Prompt = prompt };

    private static MediaRequest Full() => Ask("a teapot") with
    {
        Checkpoint = "sd15.safetensors",
        Loras = [new MediaLora("lcm-lora", 0.75), new MediaLora("paper", 1.0)],
        NegativePrompt = "blurry",
        Seed = 1234567890123,
        Steps = 4,
        CfgScale = 1.5,
        Sampler = "euler_a",
        Scheduler = "karras",
    };

    // ---- the request and the declaration ----------------------------------------------------------------

    [Fact]
    public void A_request_names_only_the_parameters_it_sets_in_the_declared_order()
    {
        Assert.Empty(MediaParameters.Needed(Ask()));
        Assert.Empty(MediaParameters.Needed(Ask() with { NegativePrompt = " ", Sampler = "", Loras = [] }));
        Assert.Equal(MediaParameters.All, MediaParameters.Needed(Full()));
        Assert.Equal([MediaParameters.Seed], MediaParameters.Needed(Ask() with { Seed = 0 }));   // zero is a seed
    }

    [Fact]
    public void A_backend_that_declares_nothing_honours_none_and_the_unhonoured_are_named()
    {
        var none = new ProviderCapabilities();
        var seedOnly = new ProviderCapabilities { Parameters = [MediaParameters.Seed] };

        Assert.Empty(none.Unhonourable(Ask()));
        Assert.Equal([MediaParameters.Checkpoint, MediaParameters.Steps],
            none.Unhonourable(Ask() with { Checkpoint = "x", Steps = 3 }));
        Assert.Equal([MediaParameters.Steps], seedOnly.Unhonourable(Ask() with { Seed = 1, Steps = 3 }));
    }

    [Fact]
    public async Task The_router_skips_a_backend_that_does_not_declare_what_the_request_names()
    {
        var plain = new FakeGenerationProvider { Id = "plain" };
        var capable = new FakeGenerationProvider
        {
            Id = "capable",
            Capabilities = plain.Capabilities with { Parameters = [MediaParameters.Seed] },
        };
        var router = new MediaRouter([plain, capable]);

        var result = await router.GenerateAsync(
            [new ProviderCandidate("plain"), new ProviderCandidate("capable")], Ask() with { Seed = 7 });
        var nobody = await router.GenerateAsync([new ProviderCandidate("plain")], Ask() with { Seed = 7 });

        Assert.True(result.IsOk);
        Assert.Equal(0, plain.GenerateCalls);
        Assert.Equal(1, capable.GenerateCalls);
        Assert.Equal(ProviderVerdict.Unsupported, nobody.Verdict);
        Assert.Contains("seed", nobody.Detail);
    }

    [Fact]
    public void A_durable_job_carries_every_parameter_through_its_JSON()
    {
        var written = new MemoryStream();
        using (var writer = new Utf8JsonWriter(written))
        {
            writer.WriteStartObject();
            GenerationJson.WriteRequest(writer, Full());
            writer.WriteEndObject();
        }

        var read = GenerationJson.ReadRequest(JsonDocument.Parse(written.ToArray()).RootElement)!;

        var sent = Full();
        Assert.Equal(sent.Loras, read.Loras);
        Assert.Equal(
            (sent.Checkpoint, sent.NegativePrompt, sent.Seed, sent.Steps, sent.CfgScale, sent.Sampler, sent.Scheduler),
            (read.Checkpoint, read.NegativePrompt, read.Seed, read.Steps, read.CfgScale, read.Sampler, read.Scheduler));
    }

    [Fact]
    public async Task A_value_no_engine_could_read_is_refused_before_the_call()
    {
        var (provider, runner, _) = Local(o => o.LoraDirectory = o.WorkDirectory);

        foreach (var bad in new[]
        {
            Ask() with { Steps = 0 },
            Ask() with { CfgScale = double.NaN },
            Ask() with { Loras = [new MediaLora("a:b")] },
            Ask() with { Loras = [new MediaLora("../outside")] },
            Ask() with { Loras = [new MediaLora("a", double.PositiveInfinity)] },
        })
            Assert.Equal(ProviderVerdict.Unsupported, (await provider.GenerateAsync(bad)).Verdict);

        Assert.Empty(runner.Calls);
    }

    // ---- sd-cli -----------------------------------------------------------------------------------------

    private (LocalDiffusionProvider Provider, FakeProcessRunner Runner, ScratchDir Dir) Local(
        Action<LocalDiffusionOptions>? configure = null)
    {
        var scratch = new ScratchDir("sdparams");
        _scratch.Add(scratch);
        var options = new LocalDiffusionOptions
        {
            BinaryPath = scratch.File("sd-cli.exe"),
            ModelPath = scratch.File("default.gguf"),
            WorkDirectory = scratch.Path,
        };
        configure?.Invoke(options);
        var runner = new FakeProcessRunner
        {
            RunHandler = (_, args) =>
            {
                File.WriteAllBytes(args[args.ToList().IndexOf("-o") + 1], [0x89, 0x50]);
                return new ProcessResult(0, "done", "");
            },
        };
        return (new LocalDiffusionProvider(options, runner), runner, scratch);
    }

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public async Task The_engine_gets_the_checkpoint_the_loras_and_the_sampling_parameters()
    {
        var models = new ScratchDir("models");
        var loras = new ScratchDir("loras");
        _scratch.AddRange([models, loras]);
        models.File("sd15.safetensors");
        loras.File("lcm-lora.safetensors");
        loras.File("paper.gguf");
        var (provider, runner, _) = Local(o => { o.CheckpointDirectory = models.Path; o.LoraDirectory = loras.Path; });

        var result = await provider.GenerateAsync(Full());

        Assert.True(result.IsOk, result.Detail);
        var args = runner.LastArgs!;
        Assert.Equal(models.Combine("sd15.safetensors"), After(args, "-m"));
        Assert.Equal("a teapot <lora:lcm-lora:0.75> <lora:paper:1>", After(args, "-p"));
        Assert.Equal(loras.Path, After(args, "--lora-model-dir"));
        Assert.Equal("blurry", After(args, "-n"));
        Assert.Equal("1234567890123", After(args, "-s"));
        Assert.Equal("4", After(args, "--steps"));
        Assert.Equal("1.5", After(args, "--cfg-scale"));
        Assert.Equal("euler_a", After(args, "--sampling-method"));
        Assert.Equal("karras", After(args, "--scheduler"));
    }

    [Fact]
    public async Task A_request_that_names_nothing_extra_leaves_the_argv_as_it_was()
    {
        var (provider, runner, _) = Local();

        await provider.GenerateAsync(Ask());

        Assert.DoesNotContain("-n", runner.LastArgs!);
        Assert.DoesNotContain("-s", runner.LastArgs!);
        Assert.DoesNotContain("--lora-model-dir", runner.LastArgs!);
        Assert.Equal("20", After(runner.LastArgs!, "--steps"));
    }

    [Fact]
    public async Task A_checkpoint_or_lora_resolves_with_or_without_its_extension()
    {
        var models = new ScratchDir("models");
        _scratch.Add(models);
        models.File("sdxl.gguf");
        var (provider, runner, _) = Local(o => o.CheckpointDirectory = models.Path);

        await provider.GenerateAsync(Ask() with { Checkpoint = "sdxl" });

        Assert.Equal(models.Combine("sdxl.gguf"), After(runner.LastArgs!, "-m"));
    }

    [Fact]
    public void It_declares_a_checkpoint_and_loras_only_when_the_host_gave_it_the_folders()
    {
        var (bare, _, dir) = Local();
        var (configured, _, _) = Local(o => { o.CheckpointDirectory = dir.Path; o.LoraDirectory = dir.Path; });

        Assert.Equal(
            [MediaParameters.NegativePrompt, MediaParameters.Seed, MediaParameters.Steps, MediaParameters.CfgScale,
             MediaParameters.Sampler, MediaParameters.Scheduler],
            bare.Capabilities.Parameters);
        Assert.Equal(MediaParameters.All.Order(), configured.Capabilities.Parameters.Order());
    }

    [Fact]
    public async Task A_checkpoint_or_lora_without_a_folder_is_refused_and_nothing_is_spawned()
    {
        var (provider, runner, _) = Local();

        var checkpoint = await provider.GenerateAsync(Ask() with { Checkpoint = "sd15.safetensors" });
        var lora = await provider.GenerateAsync(Ask() with { Loras = [new MediaLora("lcm-lora")] });

        Assert.Equal(ProviderVerdict.Unsupported, checkpoint.Verdict);
        Assert.Contains("checkpoint", checkpoint.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, lora.Verdict);
        Assert.Contains("loras", lora.Detail);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_name_the_folder_does_not_hold_is_refused_and_nothing_is_spawned()
    {
        var models = new ScratchDir("models");
        _scratch.Add(models);
        var (provider, runner, _) = Local(o => { o.CheckpointDirectory = models.Path; o.LoraDirectory = models.Path; });

        var missingCheckpoint = await provider.GenerateAsync(Ask() with { Checkpoint = "absent" });
        var missingLora = await provider.GenerateAsync(Ask() with { Loras = [new MediaLora("absent")] });
        var escaping = await provider.GenerateAsync(Ask() with { Checkpoint = Path.Combine("..", "default.gguf") });

        Assert.All([missingCheckpoint, missingLora, escaping], r => Assert.Equal(ProviderVerdict.Unsupported, r.Verdict));
        Assert.Contains("CheckpointDirectory", missingCheckpoint.Detail);
        Assert.Contains("LoraDirectory", missingLora.Detail);
        Assert.Empty(runner.Calls);
    }

    // ---- the Automatic1111 API --------------------------------------------------------------------------

    private static (Automatic1111Provider Provider, StubHttpHandler Http) WebUi(
        Action<Automatic1111Options>? configure = null)
    {
        var handler = new StubHttpHandler();
        var options = new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" };
        configure?.Invoke(options);
        handler.Enqueue(HttpStatusCode.OK, """{"images":["iVBORw=="]}""");
        return (new Automatic1111Provider(options, () => new HttpClient(handler, disposeHandler: false)), handler);
    }

    [Fact]
    public async Task A_server_declared_to_take_everything_gets_every_parameter_in_the_WebUIs_own_fields()
    {
        var (provider, http) = WebUi(o => o.HonouredParameters = [.. MediaParameters.All]);

        var result = await provider.GenerateAsync(Full());

        Assert.True(result.IsOk, result.Detail);
        var body = JsonNode.Parse(http.Requests[0].Body)!;
        Assert.Equal("a teapot <lora:lcm-lora:0.75> <lora:paper:1>", (string?)body["prompt"]);
        Assert.Equal("blurry", (string?)body["negative_prompt"]);
        Assert.Equal(1234567890123, (long?)body["seed"]);
        Assert.Equal(4, (int?)body["steps"]);
        Assert.Equal(1.5, (double?)body["cfg_scale"]);
        Assert.Equal("euler_a", (string?)body["sampler_name"]);
        Assert.Equal("karras", (string?)body["scheduler"]);
        Assert.Equal("sd15.safetensors", (string?)body["override_settings"]!["sd_model_checkpoint"]);
    }

    [Fact]
    public async Task A_WebUI_tag_names_the_lora_without_its_extension()
    {
        var (provider, http) = WebUi(o => o.HonouredParameters.Add(MediaParameters.Loras));

        await provider.GenerateAsync(Ask("a teapot") with { Loras = [new MediaLora("lcm-lora.safetensors", 0.5)] });

        Assert.Equal("a teapot <lora:lcm-lora:0.5>", (string?)JsonNode.Parse(http.Requests[0].Body)!["prompt"]);
    }

    [Fact]
    public async Task A_server_that_reads_loras_from_a_payload_field_gets_them_there_and_not_as_tags()
    {
        var (provider, http) = WebUi(o =>
        {
            o.HonouredParameters.Add(MediaParameters.Loras);
            o.LoraRequestField = "lora";
        });

        await provider.GenerateAsync(Ask("a teapot") with { Loras = [new MediaLora("lcm-lora.safetensors", 0.5)] });

        var body = JsonNode.Parse(http.Requests[0].Body)!;
        Assert.Equal("a teapot", (string?)body["prompt"]);
        var entry = Assert.Single(body["lora"]!.AsArray());
        Assert.Equal(("lcm-lora.safetensors", 0.5, false),
            ((string?)entry!["path"], (double?)entry["multiplier"], (bool?)entry["is_high_noise"]));
    }

    [Fact]
    public async Task A_request_naming_nothing_extra_posts_the_payload_it_always_did()
    {
        var (provider, http) = WebUi();

        await provider.GenerateAsync(Ask());

        var body = (JsonObject)JsonNode.Parse(http.Requests[0].Body)!;
        Assert.Equal(["prompt", "steps", "width", "height", "cfg_scale"], body.Select(p => p.Key));
    }

    [Fact]
    public async Task What_the_server_was_not_declared_to_take_is_refused_before_any_request()
    {
        // the default is the set measured on stable-diffusion.cpp's sd-server, which has one loaded model
        var (provider, http) = WebUi();

        var checkpoint = await provider.GenerateAsync(Ask() with { Checkpoint = "other.safetensors" });
        var lora = await provider.GenerateAsync(Ask() with { Loras = [new MediaLora("lcm-lora")] });

        Assert.Equal(ProviderVerdict.Unsupported, checkpoint.Verdict);
        Assert.Contains("checkpoint", checkpoint.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, lora.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void What_it_declares_follows_the_host_s_setting_after_construction()
    {
        var options = new Automatic1111Options();
        var provider = new Automatic1111Provider(options, () => new HttpClient());

        options.HonouredParameters.Add(MediaParameters.Checkpoint);

        Assert.Contains(MediaParameters.Checkpoint, provider.Capabilities.Parameters);
    }

    // ---- ComfyUI: the host's template says where each field goes ----------------------------------------

    private const string Graph = """
        {"3":{"class_type":"KSampler","inputs":{"seed":0,"steps":20,"cfg":7,"sampler_name":"euler","scheduler":"normal"}},
         "4":{"class_type":"CheckpointLoaderSimple","inputs":{"ckpt_name":"none"}},
         "5":{"class_type":"EmptyLatentImage","inputs":{"width":512,"height":512}},
         "6":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}},
         "7":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}},
         "10":{"class_type":"LoraLoader","inputs":{"lora_name":"none","strength_model":1,"strength_clip":1}},
         "11":{"class_type":"LoraLoader","inputs":{"lora_name":"none","strength_model":1,"strength_clip":1}}}
        """;

    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prompt-path"] = "6.inputs.text",
        ["negative-prompt-path"] = "7.inputs.text",
        ["seed-path"] = "3.inputs.seed",
        ["steps-path"] = "3.inputs.steps",
        ["cfg-scale-path"] = "3.inputs.cfg",
        ["sampler-path"] = "3.inputs.sampler_name",
        ["scheduler-path"] = "3.inputs.scheduler",
        ["checkpoint-path"] = "4.inputs.ckpt_name",
        ["width-path"] = "5.inputs.width",
        ["height-path"] = "5.inputs.height",
        ["lora-path:1"] = "10.inputs.lora_name",
        ["lora-weight-path:1"] = "10.inputs.strength_model, 10.inputs.strength_clip",
        ["lora-path:2"] = "11.inputs.lora_name",
        ["lora-weight-path:2"] = "11.inputs.strength_model",
    };

    private static MediaRequest GraphAsk(MediaRequest request, params (string Key, string? Value)[] edits)
    {
        var options = new Dictionary<string, string>(Paths, StringComparer.OrdinalIgnoreCase) { ["workflow"] = Graph };
        foreach (var (key, value) in edits)
            if (value is null) options.Remove(key); else options[key] = value;
        return request with { Options = options };
    }

    private static (ComfyUiProvider Provider, StubHttpHandler Http) Comfy()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"prompt_id":"abc"}""");
        return (new ComfyUiProvider(new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(handler, disposeHandler: false)), handler);
    }

    [Fact]
    public async Task Every_parameter_lands_in_the_graph_field_its_path_names()
    {
        var (provider, http) = Comfy();

        var operation = await provider.SubmitAsync(GraphAsk(
            Full() with { Options = new Dictionary<string, string> { ["size"] = "768x640" } }, ("size", "768x640")));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        var graph = JsonNode.Parse(http.Requests[0].Body)!["prompt"]!;
        Assert.Equal("a teapot", (string?)graph["6"]!["inputs"]!["text"]);
        Assert.Equal("blurry", (string?)graph["7"]!["inputs"]!["text"]);
        Assert.Equal(1234567890123, (long?)graph["3"]!["inputs"]!["seed"]);
        Assert.Equal(4, (int?)graph["3"]!["inputs"]!["steps"]);
        Assert.Equal(1.5, (double?)graph["3"]!["inputs"]!["cfg"]);
        Assert.Equal("euler_a", (string?)graph["3"]!["inputs"]!["sampler_name"]);
        Assert.Equal("karras", (string?)graph["3"]!["inputs"]!["scheduler"]);
        Assert.Equal("sd15.safetensors", (string?)graph["4"]!["inputs"]!["ckpt_name"]);
        Assert.Equal((768, 640), ((int?)graph["5"]!["inputs"]!["width"], (int?)graph["5"]!["inputs"]!["height"]));
        Assert.Equal("lcm-lora", (string?)graph["10"]!["inputs"]!["lora_name"]);
        Assert.Equal((0.75, 0.75), ((double?)graph["10"]!["inputs"]!["strength_model"], (double?)graph["10"]!["inputs"]!["strength_clip"]));
        Assert.Equal(("paper", 1.0), ((string?)graph["11"]!["inputs"]!["lora_name"], (double?)graph["11"]!["inputs"]!["strength_model"]));
    }

    [Fact]
    public async Task A_parameter_whose_path_is_not_set_is_refused_and_nothing_is_sent()
    {
        foreach (var (named, key) in new (MediaRequest, string)[]
        {
            (Ask() with { Seed = 1 }, "seed-path"),
            (Ask() with { NegativePrompt = "x" }, "negative-prompt-path"),
            (Ask() with { Checkpoint = "x" }, "checkpoint-path"),
            (Ask() with { Loras = [new MediaLora("x")] }, "lora-path:1"),
            (Ask() with { Loras = [new MediaLora("x")] }, "lora-weight-path:1"),
        })
        {
            var (provider, http) = Comfy();

            var operation = await provider.SubmitAsync(GraphAsk(named, (key, null)));

            Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
            Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
            Assert.Contains(key, operation.Detail);
            Assert.Empty(http.Requests);
        }
    }

    [Fact]
    public async Task A_path_the_graph_lacks_or_a_second_lora_with_no_slot_is_refused()
    {
        var (provider, http) = Comfy();

        var wrongPath = await provider.SubmitAsync(GraphAsk(Ask() with { Seed = 1 }, ("seed-path", "99.inputs.seed")));
        var noSlot = await provider.SubmitAsync(GraphAsk(
            Ask() with { Loras = [new MediaLora("a"), new MediaLora("b"), new MediaLora("c")] }));
        var collision = await provider.SubmitAsync(GraphAsk(
            Ask() with { Seed = 1, Steps = 2 }, ("steps-path", "3.inputs.seed")));

        Assert.Contains("not a field of the workflow", wrongPath.Detail);
        Assert.Contains("lora-path:3", noSlot.Detail);
        Assert.Contains("already taken", collision.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_graph_that_names_no_size_paths_keeps_the_size_it_was_written_with()
    {
        var (provider, http) = Comfy();

        await provider.SubmitAsync(GraphAsk(
            Ask() with { Options = new Dictionary<string, string> { ["size"] = "768x640" } },
            ("size", "768x640"), ("width-path", null), ("height-path", null)));

        var latent = JsonNode.Parse(http.Requests[0].Body)!["prompt"]!["5"]!["inputs"]!;
        Assert.Equal((512, 512), ((int?)latent["width"], (int?)latent["height"]));
    }

    [Fact]
    public void It_declares_every_parameter_because_the_template_decides_which_land()
    {
        var (provider, _) = Comfy();

        Assert.Equal(MediaParameters.All, provider.Capabilities.Parameters);
    }
}
