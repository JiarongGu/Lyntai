// Lyntai.ImagePipeline — the example image pipeline, run against a ComfyUI the host already has: a setup image,
// three variations of it, a compose from a style, a subject and a pose reference plus words, a replay of that compose
// from its recipe, and with --optional an inpaint and an upscale. The workflow templates are tracked beside this
// project (templates/); the models are the host's. See docs/generation.md §12.
//
//   LYNTAI_COMFYUI_URL              the server (default http://127.0.0.1:8188)
//   LYNTAI_COMFYUI_SDXL_CHECKPOINT  the SDXL base checkpoint file name in the server's checkpoints folder
//   LYNTAI_COMFYUI_MODELS           optional: the server's models folder, so each model's sha256 lands in the recipes
//   LYNTAI_COMFYUI_DWPOSE_DIR       optional: the folder holding the DWPose ONNX files, for the same
//
//   --out <folder>  where the steps land    --optional  add the inpaint and the upscale
//   --pose-control  also compose with the pose ControlNet at strength 0, the control the pose is measured against
//   --replay <step folder>  run one recorded step again from its recipe.json and compare the pixels
using Lyntai.Generation.Providers;
using Lyntai.ImagePipeline;

string Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? "";
var url = Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_URL") ?? "http://127.0.0.1:8188";
var checkpoint = Arg("--checkpoint") is { Length: > 0 } c ? c : Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_SDXL_CHECKPOINT");
if (string.IsNullOrWhiteSpace(checkpoint))
{
    Console.Error.WriteLine("set LYNTAI_COMFYUI_SDXL_CHECKPOINT (or --checkpoint) to the SDXL base checkpoint's file name");
    return 2;
}
var output = Arg("--out") is { Length: > 0 } o ? o : Path.Combine("devtools", "_image-pipeline", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

using var http = new HttpClient();
var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = url }, () => new HttpClient());
var probe = await provider.ProbeAsync();
if (!probe.Available) { Console.Error.WriteLine($"ComfyUI at {url}: {probe.Detail}"); return 2; }

var pipeline = new Pipeline(
    Templates.Load(Templates.DefaultDirectory()), new StepRunner(provider, http),
    new PipelineOptions(output, checkpoint!, Optional: args.Contains("--optional"),
        ModelsDirectory: Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_MODELS"),
        DwposeDirectory: Environment.GetEnvironmentVariable("LYNTAI_COMFYUI_DWPOSE_DIR"),
        EngineVersion: probe.Version, PoseControl: args.Contains("--pose-control")),
    new PoseMeter(http, url));

// --replay <folder>: run one recorded step again from its recipe.json and compare the pixels, nothing else
if (Arg("--replay") is { Length: > 0 } recorded)
{
    var again = await pipeline.ReplayAsync(recorded);
    Console.WriteLine($"replay of {again.Step}: {again.Seconds:F1} s, identical={again.Identical} " +
                      $"(bytes identical={again.BytesIdentical}) — {again.Why}");
    return again.Identical ? 0 : 1;
}

Console.WriteLine($"image pipeline: ComfyUI {probe.Version} at {url}, checkpoint {checkpoint}, out {output}");
var steps = await pipeline.RunAsync();
foreach (var step in steps)
    Console.WriteLine($"  {step.Name,-18} {step.Seconds,6:F1} s   " +
        (step.Gpu is { } gpu ? $"VRAM peak {gpu.Peak} MiB (resident before {gpu.Resident})" : "VRAM unread") +
        $"   {step.Recipe.Output.Width}x{step.Recipe.Output.Height}");

foreach (var pose in pipeline.Poses)
    Console.WriteLine($"  pose of {pose.Step,-16} " + (pose.Distance is { } d
        ? $"arms {d.ArmAngle:F1} deg from the reference, limb joints {d.Keypoint:F2} torso lengths away"
        : "no figure read: not following the reference"));

var compose = steps.Single(s => s.Name == "compose");
var replay = await pipeline.ReplayAsync(compose.Directory);
Console.WriteLine($"  replay of compose  {replay.Seconds,6:F1} s   identical={replay.Identical} " +
                  $"(bytes identical={replay.BytesIdentical}) — {replay.Why}");
return replay.Identical ? 0 : 1;
