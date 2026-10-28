using Lyntai.Generation.Providers;

namespace Lyntai.ImagePipeline;

/// <summary>What a host sets to run the pipeline.</summary>
public sealed record PipelineOptions(
    string OutputDirectory,
    string Checkpoint,
    bool Optional = false,
    string? ModelsDirectory = null,
    string? DwposeDirectory = null,
    string? EngineVersion = null);

/// <summary>One step as it ran: where its files are, what it took, and the recipe.</summary>
public sealed record StepReport(string Name, string Directory, Recipe Recipe, double Seconds, GpuUse? Gpu);

/// <summary>A replay's verdict: <c>Identical</c> is decided on the decoded pixels; <c>BytesIdentical</c> on the file.</summary>
public sealed record ReplayReport(string Step, bool Identical, bool BytesIdentical, bool TemplateUnchanged, double Seconds, string Why);

/// <summary>The example pipeline: a setup image, variations of it over several seeds, a compose from a style, a subject
/// and a pose reference plus words, and optionally an inpaint and an upscale. Every step is one template run through
/// <see cref="ComfyUiProvider"/> by <see cref="StepRunner"/>; the references are made by the setup step itself, so no
/// outside image enters, and every step writes <c>recipe.json</c> beside its image.</summary>
public sealed class Pipeline(
    IReadOnlyDictionary<string, Template> templates, StepRunner runner, PipelineOptions options)
{
    public const string Negative = "blurry, low quality, text, watermark, deformed";

    private readonly ModelCatalog _models = new(
        options.ModelsDirectory, options.DwposeDirectory, Path.Combine(options.OutputDirectory, "model-hashes.json"));
    private int _number;

    public List<StepReport> Steps { get; } = [];

    public async Task<IReadOnlyList<StepReport>> RunAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var baseImage = await StepAsync("setup", "base", Setup(
            "a small wooden toy sailboat on a calm pond, soft morning light, simple clean illustration", seed: 101), ct);

        for (var i = 0; i < 3; i++)
            await StepAsync("variation", $"variation-{i + 1}", Variation(
                "a small wooden toy sailboat on a calm pond, soft morning light, simple clean illustration",
                seed: 201 + i, baseImage.Png), ct);

        var style = await StepAsync("setup", "reference-style", Setup(
            "a watercolour painting of a mountain village, soft washes, muted teal and ochre palette, visible paper texture", seed: 301), ct);
        var subject = await StepAsync("setup", "reference-subject", Setup(
            "a ceramic fox figurine standing on a plain light background, studio lighting, product photo", seed: 302), ct);
        var pose = await StepAsync("setup", "reference-pose", Setup(
"a wooden artist's mannequin wearing a plain grey tunic, standing upright with both arms stretched out wide, full body filling the frame, plain light grey background", seed: 303), ct);

        await StepAsync("compose", "compose", new StepSpec
        {
            Prompt = "a small fox figure standing upright like a person with both arms stretched out wide, in front of a mountain village, soft washes",
            NegativePrompt = Negative, Seed = 401, Steps = 25, CfgScale = 7, Sampler = "euler", Scheduler = "normal",
            Checkpoint = options.Checkpoint, Width = 1024, Height = 1024,
            Fields = new Dictionary<string, double>
            {
                ["style-weight"] = 0.8, ["subject-weight"] = 0.8, ["pose-strength"] = 0.8, ["pose-end"] = 0.8,
            },
            Inputs = [("style", style.Png), ("subject", subject.Png), ("pose", pose.Png)],
        }, ct);

        if (options.Optional)
        {
            var (width, height) = Png.Dimensions(baseImage.Png);
            await StepAsync("inpaint", "inpaint", new StepSpec
            {
                Prompt = "a red lighthouse on a small rocky island", NegativePrompt = Negative, Seed = 501, Steps = 25, CfgScale = 7,
                Sampler = "euler", Scheduler = "normal", Checkpoint = options.Checkpoint,
                Fields = new Dictionary<string, double>
                {
                    ["denoise"] = 1, ["image-width"] = width, ["image-height"] = height,
                    ["box-x"] = width / 4, ["box-y"] = height / 4, ["box-width"] = width / 2, ["box-height"] = height / 2,
                },
                Inputs = [("init", baseImage.Png)],
            }, ct);
            await StepAsync("upscale", "upscale", new StepSpec
            {
                Prompt = "a small wooden toy sailboat on a calm pond, soft morning light, simple clean illustration",
                NegativePrompt = Negative, Seed = 601, Steps = 20, CfgScale = 7, Sampler = "euler", Scheduler = "normal",
                Checkpoint = options.Checkpoint, Fields = new Dictionary<string, double> { ["denoise"] = 0.35, ["scale"] = 1.5 },
                Inputs = [("init", baseImage.Png)],
            }, ct);
        }
        return Steps;
    }

    private StepSpec Setup(string prompt, long seed) => new()
    {
        Prompt = prompt, NegativePrompt = Negative, Seed = seed, Steps = 25, CfgScale = 7, Sampler = "euler",
        Scheduler = "normal", Checkpoint = options.Checkpoint, Width = 1024, Height = 1024,
    };

    private StepSpec Variation(string prompt, long seed, byte[] init) => new()
    {
        Prompt = prompt, NegativePrompt = Negative, Seed = seed, Steps = 25, CfgScale = 7, Sampler = "euler",
        Scheduler = "normal", Checkpoint = options.Checkpoint,
        Fields = new Dictionary<string, double> { ["denoise"] = 0.6 }, Inputs = [("init", init)],
    };

    /// <summary>Runs one step and writes <c>NN-name/image.png</c>, its inputs under <c>inputs/</c> and <c>recipe.json</c>.</summary>
    public async Task<(byte[] Png, StepReport Report)> StepAsync(string templateId, string name, StepSpec spec, CancellationToken ct)
    {
        var template = templates[templateId];
        var request = template.Fill(spec);
        var run = await runner.RunAsync(request, ct);

        var directory = Path.Combine(options.OutputDirectory, $"{++_number:00}-{name}");
        Directory.CreateDirectory(Path.Combine(directory, "inputs"));
        var png = run.ImagesByNode.TryGetValue(template.Output, out var main)
            ? main
            : throw new InvalidOperationException($"node {template.Output} of '{templateId}' saved no image (it saved: {string.Join(", ", run.ImagesByNode.Keys)})");
        File.WriteAllBytes(Path.Combine(directory, "image.png"), png);
        foreach (var (extra, node) in template.ExtraOutputs)
            if (run.ImagesByNode.TryGetValue(node, out var extraPng))
                File.WriteAllBytes(Path.Combine(directory, $"{extra}.png"), extraPng);

        var inputs = new List<RecipeInput>();
        foreach (var (role, bytes) in spec.Inputs)
        {
            var file = $"inputs/{role}.png";
            File.WriteAllBytes(Path.Combine(directory, file), bytes);
            inputs.Add(new RecipeInput(role, file, Hashes.Sha256(bytes)));
        }

        var (width, height) = Png.Dimensions(png);
        var recipe = new Recipe
        {
            Step = name, Template = templateId, TemplateSha256 = template.Sha256, EngineVersion = options.EngineVersion,
            Prompt = spec.Prompt, NegativePrompt = spec.NegativePrompt, Seed = spec.Seed, Steps = spec.Steps,
            CfgScale = spec.CfgScale, Sampler = spec.Sampler, Scheduler = spec.Scheduler, Checkpoint = spec.Checkpoint,
            Width = spec.Width, Height = spec.Height, Fields = spec.Fields, Inputs = inputs,
            Models = _models.Of(request.Option("workflow")!, template.Options.GetValueOrDefault("checkpoint-path"), spec.Checkpoint),
            Output = new RecipeOutput("image.png", Hashes.Sha256(png), Png.PixelsSha256(png), width, height),
        };
        File.WriteAllText(Path.Combine(directory, "recipe.json"), recipe.ToJson());

        var report = new StepReport(name, directory, recipe, run.Seconds, run.Gpu);
        Steps.Add(report);
        return (png, report);
    }

    /// <summary>Replays a recipe from its folder: the same template, fields and inputs through the same provider, then a
    /// comparison with the recorded output on its decoded pixels. When the images differ, <c>Why</c> says what is known.</summary>
    public async Task<ReplayReport> ReplayAsync(string recipeDirectory, CancellationToken ct = default)
    {
        var recipe = Recipe.FromJson(File.ReadAllText(Path.Combine(recipeDirectory, "recipe.json")));
        var template = templates[recipe.Template];
        var request = template.Fill(recipe.ToSpec(recipeDirectory));
        var run = await runner.RunAsync(request, ct);

        var png = run.ImagesByNode[template.Output];
        var directory = Path.Combine(recipeDirectory, "replay");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "image.png"), png);

        var identical = Png.PixelsSha256(png) == recipe.Output.PixelsSha256;
        var templateUnchanged = template.Sha256 == recipe.TemplateSha256;
        var why = identical ? "the decoded pixels match the recorded output"
            : !templateUnchanged ? "the template file changed since the recipe was written"
            : "same template, fields and inputs, different pixels: the engine, a node pack, the driver or the GPU is not the one that made the recipe";
        var report = new ReplayReport(recipe.Step, identical, Hashes.Sha256(png) == recipe.Output.FileSha256, templateUnchanged, run.Seconds, why);
        File.WriteAllText(Path.Combine(directory, "replay.txt"),
            $"identical={identical} bytesIdentical={report.BytesIdentical} templateUnchanged={templateUnchanged}\n{why}\n");
        return report;
    }
}
