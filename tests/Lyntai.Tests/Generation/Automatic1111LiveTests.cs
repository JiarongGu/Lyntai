using System.Diagnostics;
using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>The Automatic1111 backend against a REAL server speaking its API — the measurement behind
/// <see cref="Automatic1111ProviderTests"/>, whose stub can only echo the shapes this backend was written to.
///
/// <para>Skipped without <c>LYNTAI_A1111_URL</c> (e.g. <c>http://127.0.0.1:17860</c>). Any server that speaks
/// <c>/sdapi/v1</c> will do: the run recorded in <c>docs/generation.md</c> used stable-diffusion.cpp's
/// <c>sd-server</c>, which implements the WebUI's shapes and is NOT the WebUI. 256×256 at 4 steps, so the
/// suite measures the wire and not image quality; a cold Vulkan server compiles its shaders on the first
/// render and reads over a minute.</para></summary>
public class Automatic1111LiveTests(ITestOutputHelper output)
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_A1111_URL");

    private const string NotRunning = "set LYNTAI_A1111_URL to a running server that speaks /sdapi/v1";

    private static Automatic1111Provider Provider() =>
        new(new Automatic1111Options { BaseUrl = BaseUrl!, Steps = 4 }, () => new HttpClient());

    private static MediaRequest Ask(string prompt, string size, byte[]? png = null) => new()
    {
        Kind = ProviderKinds.Image,
        Prompt = prompt,
        Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["size"] = size },
        Inputs = png is null ? [] : [new MediaInput("image/png", Data: png, Role: MediaInputRoles.Init)],
    };

    [SkippableFact]
    public async Task A_real_server_answers_the_probe_with_its_loaded_checkpoint()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);

        var probe = await Provider().ProbeAsync();

        Assert.True(probe.Available, probe.Detail);
        Assert.False(string.IsNullOrWhiteSpace(probe.Version),
            "sd-models answered but the probe found no model_name or title in it — the probe's fields are wrong");
        output.WriteLine($"probe: {probe.Detail}");
    }

    [SkippableFact]
    public async Task A_real_server_renders_txt2img_at_the_requested_size()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var clock = Stopwatch.StartNew();

        // not square, so a width/height swapped on the way out would show in the dimensions that come back
        var rendered = await Provider().GenerateAsync(Ask("a red square on a white background", "320x192"));

        Assert.True(rendered.IsOk, rendered.Detail);
        var artifact = Assert.Single(rendered.Artifacts);
        Assert.Equal((320, 192), LivePng.Dimensions(artifact.Data!));
        output.WriteLine($"txt2img 320x192 in {clock.Elapsed.TotalSeconds:F1} s");
    }

    [SkippableFact]
    public async Task A_real_server_renders_img2img_from_the_init_image_at_its_size()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var provider = Provider();

        // the source is rendered here, not committed: an img2img the server cannot decode fails the call
        var source = await provider.GenerateAsync(Ask("a red square on a white background", "256x256"));
        Assert.True(source.IsOk, source.Detail);
        var clock = Stopwatch.StartNew();

        var edited = await provider.GenerateAsync(Ask("the same square, blue", "256x256", source.Artifacts[0].Data));

        Assert.True(edited.IsOk, edited.Detail);
        var artifact = Assert.Single(edited.Artifacts);
        Assert.Equal((256, 256), LivePng.Dimensions(artifact.Data!));
        output.WriteLine($"img2img 256x256 in {clock.Elapsed.TotalSeconds:F1} s");
    }

    // ---- a current split model ---------------------------------------------------------------------------

    private static string? ZImageUrl => Environment.GetEnvironmentVariable("LYNTAI_A1111_ZIMAGE_URL");

    /// <summary>Z-Image-Turbo behind the same backend, where SD 1.5 above is the cheap smoke: the server is started on
    /// the three files (<c>--diffusion-model</c>, <c>--llm</c> with the bf16 Qwen3-4B — the engine aborts on the
    /// fp8-scaled one — and <c>--vae</c>), so the provider needs no option beyond the model's own steps and CFG. A
    /// separate server from the SD 1.5 one, hence a variable of its own. The first render of a server loads ~19 GB.
    /// Skipped without <c>LYNTAI_A1111_ZIMAGE_URL</c>.</summary>
    [SkippableFact]
    public async Task Z_Image_Turbo_renders_through_the_server_cold_then_warm()
    {
        Skip.If(string.IsNullOrWhiteSpace(ZImageUrl),
            "set LYNTAI_A1111_ZIMAGE_URL to an sd-server started with --diffusion-model z_image_turbo_bf16, --llm qwen_3_4b (bf16) and --vae z_image_ae");
        var provider = new Automatic1111Provider(
            new Automatic1111Options { BaseUrl = ZImageUrl!, Steps = 8, CfgScale = 1, Timeout = TimeSpan.FromMinutes(30) },
            () => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

        var probe = await provider.ProbeAsync();
        Assert.True(probe.Available, probe.Detail);

        var renders = new List<byte[]>();
        foreach (var (label, seed) in new[] { ("cold", 8), ("warm", 9) })
        {
            var clock = Stopwatch.StartNew();
            var rendered = await provider.GenerateAsync(
                Ask("a ceramic teapot on a wooden table by a window, soft morning light, photograph", "512x512") with { Seed = seed });

            Assert.True(rendered.IsOk, rendered.Detail);
            var png = Assert.Single(rendered.Artifacts).Data!;
            Assert.Equal((512, 512), LivePng.Dimensions(png));
            output.WriteLine($"Z-Image-Turbo {label}: 512x512, 8 steps, CFG 1, seed {seed} in {clock.Elapsed.TotalSeconds:F1} s");
            LivePng.Keep($"sd-server-zimage-seed{seed}.png", png);
            renders.Add(png);
        }

        Assert.NotEqual(renders[0], renders[1]);   // the seed reaches the server
        Assert.True(LivePng.Detail(renders[0]) > 2, "a Z-Image render at 8 steps is a picture, not a flat field");
    }

    // ---- the request's checkpoint, LoRAs and sampling parameters ----------------------------------------

    private static string? Lora => Environment.GetEnvironmentVariable("LYNTAI_A1111_LORA");

    private static MediaRequest Sampled(string prompt = "a red apple on a wooden table") => Ask(prompt, "256x256") with
    {
        Steps = 4,
        CfgScale = 1,
        Seed = 7,
    };

    [SkippableFact]
    public async Task A_seed_a_negative_prompt_a_sampler_and_a_scheduler_reach_the_server()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var provider = Provider();
        var request = Sampled();

        var first = (await provider.GenerateAsync(request)).Artifacts[0].Data!;
        var again = (await provider.GenerateAsync(request)).Artifacts[0].Data!;
        var otherSeed = await provider.GenerateAsync(request with { Seed = 8 });
        var negated = await provider.GenerateAsync(request with { NegativePrompt = "red" });
        var sampled = await provider.GenerateAsync(request with { Sampler = "euler_a" });
        var scheduled = await provider.GenerateAsync(request with { Scheduler = "karras" });

        Assert.All([otherSeed, negated, sampled, scheduled], r => Assert.True(r.IsOk, r.Detail));
        Assert.Equal(first, again);                                    // one seed, one image
        Assert.NotEqual(first, otherSeed.Artifacts[0].Data);
        Assert.NotEqual(first, negated.Artifacts[0].Data);
        Assert.NotEqual(first, sampled.Artifacts[0].Data);
        Assert.NotEqual(first, scheduled.Artifacts[0].Data);
    }

    /// <summary>What the default declaration rests on: the server loads one checkpoint and answers a request's
    /// <c>override_settings</c> with the render it would have made anyway, so a checkpoint is not something this
    /// backend can honestly declare against it — the refusal is the proof that a caller is never handed the wrong model.</summary>
    [SkippableFact]
    public async Task A_server_with_one_loaded_checkpoint_ignores_override_settings_and_the_backend_refuses_it_instead()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl), NotRunning);
        var provider = Provider();
        var plain = (await provider.GenerateAsync(Sampled())).Artifacts[0].Data!;

        // the same payload the WebUI would switch models on, sent to the server directly
        using var http = new HttpClient();
        var body = """
            {"prompt":"a red apple on a wooden table","steps":4,"width":256,"height":256,"cfg_scale":1,"seed":7,
             "override_settings":{"sd_model_checkpoint":"sd_xl_base_1.0"}}
            """;
        using var reply = await http.PostAsync($"{BaseUrl!.TrimEnd('/')}/sdapi/v1/txt2img",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        var image = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync())
            .RootElement.GetProperty("images")[0].GetString()!;
        Assert.Equal(plain, Convert.FromBase64String(image));         // the override changed nothing

        var refused = await provider.GenerateAsync(Sampled() with { Checkpoint = "sd_xl_base_1.0.safetensors" });
        Assert.Equal(ProviderVerdict.Unsupported, refused.Verdict);
        Assert.DoesNotContain(MediaParameters.Checkpoint, provider.Capabilities.Parameters);
    }

    /// <summary>The LCM LoRA on SD 1.5: four steps at CFG 1 are a smear without it and a picture with it, same seed and
    /// sampler. Skipped unless <c>LYNTAI_A1111_LORA</c> names the file the server's <c>/sdapi/v1/loras</c> lists
    /// (<c>lcm-lora-sdv1-5.safetensors</c>), which needs the server started with its LoRA folder.</summary>
    [SkippableFact]
    public async Task The_LCM_LoRA_makes_a_four_step_render_at_CFG_one_a_recognisable_image()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Lora),
            $"{NotRunning}, started with its LoRA folder, and LYNTAI_A1111_LORA to the file name /sdapi/v1/loras lists");
        var provider = new Automatic1111Provider(
            new Automatic1111Options
            {
                BaseUrl = BaseUrl!,
                LoraRequestField = "lora",   // sd-server reads LoRAs from a payload field and ignores prompt tags
                HonouredParameters = [.. new Automatic1111Options().HonouredParameters, MediaParameters.Loras],
            }, () => new HttpClient());
        var request = Sampled() with { Sampler = "lcm", Options = new Dictionary<string, string> { ["size"] = "512x512" } };

        await provider.GenerateAsync(request);   // warms the server, so neither timing below carries it
        var clock = Stopwatch.StartNew();
        var without = await provider.GenerateAsync(request);
        var withoutSeconds = clock.Elapsed.TotalSeconds;
        clock.Restart();
        var with = await provider.GenerateAsync(request with { Loras = [new MediaLora(Lora!, 1.0)] });
        var withSeconds = clock.Elapsed.TotalSeconds;
        Assert.True(without.IsOk && with.IsOk, $"{without.Detail} {with.Detail}");

        var (plain, tuned) = (without.Artifacts[0].Data!, with.Artifacts[0].Data!);
        var (detailWithout, detailWith) = (LivePng.Detail(plain), LivePng.Detail(tuned));
        output.WriteLine($"512x512, 4 steps, CFG 1, seed 7, sampler lcm: without the LoRA {withoutSeconds:F1} s " +
                         $"(detail {detailWithout:F2}); with {Lora} at 1.0 {withSeconds:F1} s (detail {detailWith:F2})");
        LivePng.Keep("sd-server-4steps-without-lora.png", plain);
        LivePng.Keep("sd-server-4steps-with-lora.png", tuned);

        Assert.True(detailWith > 1.5 * detailWithout,
            $"the LoRA should turn a four-step smear into a picture; detail {detailWithout:F2} -> {detailWith:F2}");
    }
}
