using System.Diagnostics;
using System.Text.Json.Nodes;
using Lyntai.Generation.Providers;
using Lyntai.ImagePipeline;
using Xunit.Abstractions;

namespace Lyntai.Tests.Generation;

/// <summary>Each step of the example image pipeline once, through the real templates and <see cref="ComfyUiProvider"/>,
/// against a running ComfyUI with its models — the measurement the unit tests in <see cref="ImagePipelineTests"/> cannot make.
/// Every test skips without <c>LYNTAI_COMFYUI_URL</c> and <c>LYNTAI_COMFYUI_SDXL_CHECKPOINT</c> (the SDXL base file in the
/// server's checkpoints folder); the smoke tier also needs <c>LYNTAI_COMFYUI_CHECKPOINT</c> (an SD 1.5 file), and the compose
/// test skips unless the server lists the two node packs' nodes. A step takes seconds to minutes on a 12 GB card, one at a
/// time. <c>LYNTAI_LIVE_OUTPUT_DIR</c> keeps every step's folder (image, inputs, recipe); without it they are deleted.</summary>
public class ImagePipelineLiveTests(ITestOutputHelper output) : IDisposable
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_URL");
    private static string? Sdxl => Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_SDXL_CHECKPOINT");
    private static string? Sd15 => Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_CHECKPOINT");

    private readonly string _out = Environment.GetEnvironmentVariable("LYNTAI_LIVE_OUTPUT_DIR") is { Length: > 0 } kept
        ? Path.Combine(kept, "image-pipeline", Guid.NewGuid().ToString("N")[..8])
        : Path.Combine(Path.GetTempPath(), $"lyntai-pipeline-live-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("LYNTAI_LIVE_OUTPUT_DIR") is not { Length: > 0 } && Directory.Exists(_out))
            Directory.Delete(_out, recursive: true);
    }

    private Pipeline Start(string checkpoint, bool withMeter = false)
    {
        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = BaseUrl! }, () => new HttpClient());
        var http = new HttpClient();
        return new Pipeline(Templates.Load(Templates.DefaultDirectory()), new StepRunner(provider, http),
            new PipelineOptions(_out, checkpoint), withMeter ? new PoseMeter(http, BaseUrl!) : null);
    }

    private static StepSpec Spec(string checkpoint, string prompt, long seed, int size, int steps) => new()
    {
        Prompt = prompt, NegativePrompt = Pipeline.Negative, Seed = seed, Steps = steps, CfgScale = 7, Sampler = "euler",
        Scheduler = "normal", Checkpoint = checkpoint, Width = size, Height = size,
    };

    private void Report(StepReport step) => output.WriteLine(
        $"{step.Name}: {step.Seconds:F1} s" + (step.Gpu is { } g ? $", VRAM peak {g.Peak} MiB (resident before {g.Resident})" : ""));

    private static void RequireSdxl() => Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Sdxl),
        "set LYNTAI_COMFYUI_URL to a running ComfyUI and LYNTAI_COMFYUI_SDXL_CHECKPOINT to the SDXL base file it holds");

    private const string Boat = "a small wooden toy sailboat on a calm pond, soft morning light, simple clean illustration";

    [SkippableFact]
    public async Task The_setup_step_renders_a_picture_on_the_SDXL_main_tier()
    {
        RequireSdxl();
        var (png, step) = await Start(Sdxl!).StepAsync("setup", "base", Spec(Sdxl!, Boat, 101, 1024, 20), default);
        Report(step);

        Assert.Equal((1024, 1024), Png.Dimensions(png));
        Assert.True(LivePng.Detail(png) > 0.5, "the render is a flat field");
        Assert.Equal("setup", step.Recipe.Template);
        Assert.Equal(Sdxl, step.Recipe.Checkpoint);
    }

    [SkippableFact]
    public async Task The_setup_step_renders_a_picture_on_the_SD15_smoke_tier()
    {
        Skip.If(string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Sd15),
            "set LYNTAI_COMFYUI_URL to a running ComfyUI and LYNTAI_COMFYUI_CHECKPOINT to an SD 1.5 file it holds");
        var (png, step) = await Start(Sd15!).StepAsync("setup", "base", Spec(Sd15!, Boat, 101, 512, 15), default);
        Report(step);

        Assert.Equal((512, 512), Png.Dimensions(png));
        Assert.True(LivePng.Detail(png) > 0.5, "the render is a flat field");
    }

    [SkippableFact]
    public async Task The_variation_step_follows_its_init_image_over_a_seed()
    {
        RequireSdxl();
        var pipeline = Start(Sdxl!);
        var (baseImage, _) = await pipeline.StepAsync("setup", "base", Spec(Sdxl!, Boat, 101, 768, 15), default);
        var (stranger, _) = await pipeline.StepAsync("setup", "stranger", Spec(Sdxl!, "a bowl of oranges on a kitchen table", 7, 768, 15), default);

        var (variation, step) = await pipeline.StepAsync("variation", "variation", new StepSpec
        {
            Prompt = Boat, NegativePrompt = Pipeline.Negative, Seed = 202, Steps = 15, CfgScale = 7, Sampler = "euler",
            Scheduler = "normal", Checkpoint = Sdxl,
            Fields = new Dictionary<string, double> { ["denoise"] = 0.6 }, Inputs = [("init", baseImage)],
        }, default);
        Report(step);

        Assert.Equal((768, 768), Png.Dimensions(variation));
        Assert.NotEqual(Png.PixelsSha256(baseImage), Png.PixelsSha256(variation));
        Assert.True(LivePng.Distance(baseImage, variation) < LivePng.Distance(baseImage, stranger),
            "the variation is no nearer its init image than an unrelated picture is");
    }

    /// <summary>How near the compose's figure must stand to the reference to count as following it: each arm within this many
    /// degrees of the reference's, and the limb joints within this many torso lengths. A figure with its arms down is some 90
    /// degrees from arms out wide, so 20 is a quarter of that gap; a torso length is the neck to the hips, so 0.3 keeps every
    /// limb joint within a third of the body of where the reference put it.</summary>
    private const double ArmTolerance = 20, KeypointTolerance = 0.3;

    private static bool Follows(PoseDistance? distance) => distance is { } d && d.ArmAngle <= ArmTolerance && d.Keypoint <= KeypointTolerance;

    private static string Say(PoseDistance? d) => d is null ? "no figure read" : $"arms {d.ArmAngle:F1} deg, limb joints {d.Keypoint:F2} torso lengths";

    [SkippableFact]
    public async Task The_compose_follows_the_pose_reference_where_the_same_compose_with_the_ControlNet_at_zero_does_not_and_replays_to_the_same_pixels()
    {
        RequireSdxl();
        using (var http = new HttpClient())
        {
            var nodes = JsonNode.Parse(await http.GetStringAsync($"{BaseUrl!.TrimEnd('/')}/object_info"))!.AsObject();
            Skip.IfNot(nodes.ContainsKey("IPAdapterAdvanced") && nodes.ContainsKey("DWPreprocessor"),
                "the ComfyUI at LYNTAI_COMFYUI_URL has not loaded the IPAdapter_plus and controlnet_aux node packs");
        }

        var pipeline = Start(Sdxl!, withMeter: true);
        var (style, _) = await pipeline.StepAsync("setup", "style", Spec(Sdxl!,
            "a watercolour painting of a mountain village, soft washes, muted teal and ochre palette, visible paper texture", 301, 768, 15), default);
        var (subject, _) = await pipeline.StepAsync("setup", "subject", Spec(Sdxl!,
            "a ceramic fox figurine standing on a plain light background, studio lighting, product photo", 302, 768, 15), default);
        var pose = PoseReference.Render(PoseReference.ArmsOutWide, 768);

        StepSpec Compose(double poseStrength) => new()
        {
            Prompt = Pipeline.ComposePrompt, NegativePrompt = Pipeline.Negative, Seed = 401, Steps = 15, CfgScale = 7, Sampler = "euler",
            Scheduler = "normal", Checkpoint = Sdxl, Width = 768, Height = 768, Fields = Pipeline.ComposeFields(poseStrength),
            Inputs = [("style", style), ("subject", subject), ("pose", pose)],
        };

        var (composed, step) = await pipeline.StepAsync("compose", "compose", Compose(1), default);
        Report(step);
        Assert.Equal((768, 768), Png.Dimensions(composed));
        // the recipe replays to the same pixels, before the pose is read (a replay run after the preprocessor had loaded came back different in its last bits, twice)
        var replay = await pipeline.ReplayAsync(step.Directory);
        output.WriteLine($"replay: {replay.Seconds:F1} s, identical={replay.Identical}, bytes identical={replay.BytesIdentical} — {replay.Why}");
        Assert.True(replay.Identical, replay.Why);

        var followed = (await pipeline.MeasurePoseAsync(step))!.Distance;
        output.WriteLine($"compose, ControlNet at 1: {Say(followed)}");
        Assert.True(Follows(followed), $"the compose does not follow the pose reference: {Say(followed)} (tolerance {ArmTolerance} deg, {KeypointTolerance} torso lengths)");

        // the control: the same words, seed, references and weights with the ControlNet at strength 0 is another figure
        var (withoutPose, controlStep) = await pipeline.StepAsync("compose", "compose-no-pose", Compose(0), default);
        var control = (await pipeline.MeasurePoseAsync(controlStep))!.Distance;
        output.WriteLine($"compose, ControlNet at 0: {Say(control)}; the two differ by a mean luma of {LivePng.Distance(composed, withoutPose):F1}");
        Assert.False(Follows(control), $"the pose reference is not what moved the figure: with the ControlNet at 0 it stands as {Say(control)}");
        Assert.True(control is null || control.ArmAngle > followed!.ArmAngle + ArmTolerance, $"the control is only {Say(control)} against {Say(followed)}");
    }

    [SkippableFact]
    public async Task The_inpaint_step_changes_the_masked_box_and_leaves_every_pixel_outside_it_as_it_was()
    {
        RequireSdxl();
        var pipeline = Start(Sdxl!);
        var (baseImage, _) = await pipeline.StepAsync("setup", "base", Spec(Sdxl!, Boat, 101, 768, 15), default);
        const int size = 768, boxX = 192, boxY = 192, box = 384;

        var (inpainted, step) = await pipeline.StepAsync("inpaint", "inpaint", new StepSpec
        {
            Prompt = "a red lighthouse on a small rocky island", NegativePrompt = Pipeline.Negative, Seed = 501, Steps = 15, CfgScale = 7,
            Sampler = "euler", Scheduler = "normal", Checkpoint = Sdxl,
            Fields = new Dictionary<string, double>
            {
                ["denoise"] = 1, ["image-width"] = size, ["image-height"] = size,
                ["box-x"] = boxX, ["box-y"] = boxY, ["box-width"] = box, ["box-height"] = box,
            },
            Inputs = [("init", baseImage)],
        }, default);
        Report(step);

        var (before, after) = (Png.Decode(baseImage), Png.Decode(inpainted));
        Assert.Equal((size, size, before.BytesPerPixel), (after.Width, after.Height, after.BytesPerPixel));
        long inside = 0, outside = 0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var changed = false;
                for (var c = 0; c < 3; c++)
                    changed |= before.Pixels[(y * size + x) * before.BytesPerPixel + c] != after.Pixels[(y * size + x) * after.BytesPerPixel + c];
                if (x >= boxX && x < boxX + box && y >= boxY && y < boxY + box) { if (changed) inside++; }
                else if (changed) outside++;
            }
        Assert.Equal(0, outside);
        Assert.True(inside > box * box / 2, $"only {inside} of {box * box} pixels inside the box changed");
    }

    [SkippableFact]
    public async Task The_upscale_step_returns_the_picture_at_the_scale_it_was_asked()
    {
        RequireSdxl();
        var pipeline = Start(Sdxl!);
        var (baseImage, _) = await pipeline.StepAsync("setup", "base", Spec(Sdxl!, Boat, 101, 768, 15), default);

        var (upscaled, step) = await pipeline.StepAsync("upscale", "upscale", new StepSpec
        {
            Prompt = Boat, NegativePrompt = Pipeline.Negative, Seed = 601, Steps = 15, CfgScale = 7, Sampler = "euler",
            Scheduler = "normal", Checkpoint = Sdxl,
            Fields = new Dictionary<string, double> { ["denoise"] = 0.35, ["scale"] = 1.5 }, Inputs = [("init", baseImage)],
        }, default);
        Report(step);

        Assert.Equal((1152, 1152), Png.Dimensions(upscaled));
    }
}
