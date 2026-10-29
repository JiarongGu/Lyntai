using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lyntai.Generation.Providers;
using Lyntai.ImagePipeline;
using Lyntai.Inference;

namespace Lyntai.Tests.Generation;

/// <summary>The example image pipeline's templates and recipe, against a scripted ComfyUI: every template is filled
/// through <see cref="ComfyUiProvider"/> and the graph it would queue is read back, the story runs end to end over the
/// fake, and a recipe replays. What the real server and the node packs do with these graphs is
/// <see cref="ImagePipelineLiveTests"/>.</summary>
public class ImagePipelineTests
{
    private static readonly IReadOnlyDictionary<string, Template> Catalog = Templates.Load(Templates.DefaultDirectory());

    /// <summary>The nodes beyond ComfyUI's core that the templates may use: the two allowed packs' only.</summary>
    private static readonly HashSet<string> PackNodes = ["IPAdapterModelLoader", "IPAdapterAdvanced"];

    private static readonly HashSet<string> CoreNodes =
    [
        "CheckpointLoaderSimple", "EmptyLatentImage", "CLIPTextEncode", "KSampler", "VAEDecode", "SaveImage", "LoadImage",
        "VAEEncode", "CLIPVisionLoader", "ControlNetLoader", "ControlNetApplyAdvanced", "SolidMask", "MaskComposite",
        "SetLatentNoiseMask", "ImageCompositeMasked", "ImageScaleBy", "FeatherMask",
    ];

    // ---- the files themselves ---------------------------------------------------------------------------------------

    [Fact]
    public void The_catalog_holds_one_template_per_step()
    {
        Assert.Equal(["compose", "inpaint", "setup", "upscale", "variation"], Catalog.Keys.Order());
    }

    [Fact]
    public void Every_template_uses_core_nodes_and_the_two_allowed_packs_only()
    {
        foreach (var (id, template) in Catalog)
            foreach (var (nodeId, node) in JsonNode.Parse(template.GraphJson)!.AsObject())
            {
                var classType = node!["class_type"]!.GetValue<string>();
                Assert.True(CoreNodes.Contains(classType) || PackNodes.Contains(classType),
                    $"{id} node {nodeId}: {classType} is neither a core node nor one of the allowed packs'");
            }

        // the IP-Adapter nodes are compose's alone; the preprocessor pack belongs to the pose READING (PoseMeter), not to a step
        Assert.Contains("IPAdapterAdvanced", Catalog["compose"].GraphJson);
        Assert.All(Catalog.Where(t => t.Key != "compose"), t =>
            Assert.DoesNotContain(PackNodes, p => t.Value.GraphJson.Contains(p)));
        Assert.All(Catalog, t => Assert.DoesNotContain("DWPreprocessor", t.Value.GraphJson));
    }

    [Fact]
    public void The_pose_reading_graph_uses_the_preprocessor_with_no_detector_and_nothing_else_beyond_core()
    {
        var graph = JsonNode.Parse(PoseMeter.Graph)!.AsObject();
        Assert.Equal(["DWPreprocessor", "LoadImage", "SaveImage"], graph.Select(n => n.Value!["class_type"]!.GetValue<string>()).Order());
        var node = graph.Single(n => n.Value!["class_type"]!.GetValue<string>() == "DWPreprocessor").Value!["inputs"]!;
        Assert.Equal("None", node["bbox_detector"]!.GetValue<string>());   // the person detector finds no figure in a statue
        Assert.Equal("dw-ll_ucoco_384.onnx", node["pose_estimator"]!.GetValue<string>());
    }

    [Fact]
    public void The_compose_template_feeds_the_pose_reference_straight_to_the_ControlNet()
    {
        var graph = JsonNode.Parse(Catalog["compose"].GraphJson)!;
        Assert.Equal("22", graph["43"]!["inputs"]!["image"]![0]!.GetValue<string>());
        Assert.Empty(Catalog["compose"].ExtraOutputs);
    }

    [Fact]
    public void Every_link_in_every_template_points_at_a_node_that_exists_and_every_template_saves_its_output_node()
    {
        foreach (var (id, template) in Catalog)
        {
            var graph = JsonNode.Parse(template.GraphJson)!.AsObject();
            foreach (var (nodeId, node) in graph)
                foreach (var (input, value) in node!["inputs"]!.AsObject())
                    if (value is JsonArray { Count: 2 } link && link[0]!.GetValueKind() == System.Text.Json.JsonValueKind.String)
                        Assert.True(graph.ContainsKey(link[0]!.GetValue<string>()), $"{id} node {nodeId}.{input} links to a node that is not there");

            Assert.Equal("SaveImage", graph[template.Output]!["class_type"]!.GetValue<string>());
            Assert.All(template.ExtraOutputs.Values, n => Assert.Equal("SaveImage", graph[n]!["class_type"]!.GetValue<string>()));
        }
    }

    [Fact]
    public void Every_path_in_every_manifest_entry_is_a_field_of_its_graph()
    {
        foreach (var (id, template) in Catalog)
        {
            var graph = JsonNode.Parse(template.GraphJson)!;
            var paths = template.Options.Where(o => o.Key.EndsWith("-path") || o.Key.StartsWith("input-path:"))
                .Select(o => (o.Key, o.Value)).Concat(template.Fields.Select(f => (f.Key, f.Value)));
            foreach (var (key, path) in paths)
            {
                JsonNode? node = graph;
                foreach (var segment in path.Split('.')) node = node?[segment];
                Assert.True(node is not null, $"{id}: {key} -> {path} is not a field of the graph");
            }
        }
    }

    [Fact]
    public void A_template_accepts_exactly_the_roles_it_maps()
    {
        Assert.Empty(Catalog["setup"].Roles);
        Assert.Equal(["init"], Catalog["variation"].Roles);
        Assert.Equal(["init"], Catalog["inpaint"].Roles);
        Assert.Equal(["init"], Catalog["upscale"].Roles);
        Assert.Equal(["style", "subject", "pose"], Catalog["compose"].Roles);
    }

    // ---- each template, filled and queued ---------------------------------------------------------------------------

    private static StepSpec Typed(IReadOnlyDictionary<string, double>? fields = null, params (string, byte[])[] inputs) => new()
    {
        Prompt = "a wooden toy boat", NegativePrompt = "blurry", Seed = 77, Steps = 12, CfgScale = 5.5,
        Sampler = "dpmpp_2m", Scheduler = "karras", Checkpoint = "model-x.safetensors", Width = 832, Height = 640,
        Fields = fields ?? new Dictionary<string, double>(), Inputs = inputs,
    };

    /// <summary>Submits <paramref name="request"/> to a provider over a scripted server and answers the graph it queued
    /// and the uploads it made, each name the server stored it under in order.</summary>
    private static async Task<(JsonNode Graph, List<string> Uploads)> Queue(MediaRequest request)
    {
        var server = new FakeComfy();
        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = FakeComfy.Url }, () => new HttpClient(server, disposeHandler: false));
        var operation = await provider.SubmitAsync(request);
        Assert.True(operation.Status == QueuedOperationStatus.Queued, operation.Detail);
        return (JsonNode.Parse(server.Prompts.Single())!["prompt"]!, server.Uploads);
    }

    private static string At(JsonNode graph, string path)
    {
        JsonNode? node = graph;
        foreach (var segment in path.Split('.')) node = node![segment];
        return node!.ToJsonString().Trim('"');
    }

    [Fact]
    public async Task The_setup_template_places_every_typed_field_and_the_size()
    {
        var (graph, uploads) = await Queue(Catalog["setup"].Fill(Typed()));

        Assert.Empty(uploads);
        Assert.Equal("a wooden toy boat", At(graph, "6.inputs.text"));
        Assert.Equal("blurry", At(graph, "7.inputs.text"));
        Assert.Equal("77", At(graph, "3.inputs.seed"));
        Assert.Equal("12", At(graph, "3.inputs.steps"));
        Assert.Equal("5.5", At(graph, "3.inputs.cfg"));
        Assert.Equal("dpmpp_2m", At(graph, "3.inputs.sampler_name"));
        Assert.Equal("karras", At(graph, "3.inputs.scheduler"));
        Assert.Equal("model-x.safetensors", At(graph, "4.inputs.ckpt_name"));
        Assert.Equal(("832", "640"), (At(graph, "5.inputs.width"), At(graph, "5.inputs.height")));
    }

    [Fact]
    public async Task The_variation_template_uploads_the_init_image_and_writes_the_denoise_strength()
    {
        var init = TinyPng.Make(8, 8, 1);
        var (graph, uploads) = await Queue(Catalog["variation"].Fill(
            Typed(new Dictionary<string, double> { ["denoise"] = 0.45 }, ("init", init))));

        Assert.Equal("stored-1.png", At(graph, "10.inputs.image"));
        Assert.Single(uploads);
        Assert.Equal("0.45", At(graph, "3.inputs.denoise"));
        Assert.Equal("77", At(graph, "3.inputs.seed"));
    }

    [Fact]
    public async Task The_compose_template_binds_each_reference_to_its_own_loader_and_writes_every_weight()
    {
        var (graph, uploads) = await Queue(Catalog["compose"].Fill(Typed(
            new Dictionary<string, double>
            {
                ["style-weight"] = 0.6, ["subject-weight"] = 0.9, ["pose-strength"] = 0.7, ["pose-start"] = 0.1, ["pose-end"] = 0.75,
                ["style-start"] = 0.05, ["style-end"] = 0.95, ["subject-start"] = 0.0, ["subject-end"] = 1,
            },
            ("style", TinyPng.Make(8, 8, 1)), ("subject", TinyPng.Make(8, 8, 2)), ("pose", TinyPng.Make(8, 8, 3)))));

        Assert.Equal(3, uploads.Count);
        Assert.Equal("stored-1.png", At(graph, "20.inputs.image"));   // style
        Assert.Equal("stored-2.png", At(graph, "21.inputs.image"));   // subject
        Assert.Equal("stored-3.png", At(graph, "22.inputs.image"));   // pose
        Assert.Equal(("0.6", "0.05", "0.95"), (At(graph, "32.inputs.weight"), At(graph, "32.inputs.start_at"), At(graph, "32.inputs.end_at")));
        Assert.Equal(("0.9", "0", "1"), (At(graph, "34.inputs.weight"), At(graph, "34.inputs.start_at"), At(graph, "34.inputs.end_at")));
        Assert.Equal(("0.7", "0.1", "0.75"), (At(graph, "43.inputs.strength"), At(graph, "43.inputs.start_percent"), At(graph, "43.inputs.end_percent")));
        Assert.Equal("a wooden toy boat", At(graph, "6.inputs.text"));
        Assert.Equal("832", At(graph, "5.inputs.width"));
    }

    [Fact]
    public async Task The_inpaint_template_writes_the_mask_rectangle_and_the_image_size()
    {
        var (graph, uploads) = await Queue(Catalog["inpaint"].Fill(Typed(
            new Dictionary<string, double>
            {
                ["denoise"] = 1, ["image-width"] = 1024, ["image-height"] = 768,
                ["box-x"] = 256, ["box-y"] = 192, ["box-width"] = 512, ["box-height"] = 384,
            },
            ("init", TinyPng.Make(8, 8, 1)))));

        Assert.Single(uploads);
        Assert.Equal(("1024", "768"), (At(graph, "12.inputs.width"), At(graph, "12.inputs.height")));
        Assert.Equal(("512", "384"), (At(graph, "13.inputs.width"), At(graph, "13.inputs.height")));
        Assert.Equal(("256", "192"), (At(graph, "14.inputs.x"), At(graph, "14.inputs.y")));
    }

    [Fact]
    public async Task The_upscale_template_writes_the_scale_and_the_second_pass_denoise()
    {
        var (graph, _) = await Queue(Catalog["upscale"].Fill(Typed(
            new Dictionary<string, double> { ["denoise"] = 0.3, ["scale"] = 2 }, ("init", TinyPng.Make(8, 8, 1)))));

        Assert.Equal("2", At(graph, "12.inputs.scale_by"));
        Assert.Equal("0.3", At(graph, "3.inputs.denoise"));
    }

    [Fact]
    public void A_field_the_template_does_not_name_is_refused_by_the_fill()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Catalog["setup"].Fill(Typed(new Dictionary<string, double> { ["denoise"] = 0.5 })));
        Assert.Contains("has no field 'denoise'", ex.Message);
    }

    // ---- a role the template does not accept ------------------------------------------------------------------------

    [Theory]
    [InlineData("variation", "style")]
    [InlineData("setup", "init")]
    [InlineData("compose", "init")]
    public async Task A_role_the_template_does_not_map_is_refused_before_anything_is_uploaded_or_queued(string template, string role)
    {
        var server = new FakeComfy();
        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = FakeComfy.Url }, () => new HttpClient(server, disposeHandler: false));

        var operation = await provider.SubmitAsync(Catalog[template].Fill(Typed(null, (role, TinyPng.Make(8, 8, 1)))));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Contains($"input-path:{role}", operation.Detail);
        Assert.Empty(server.Uploads);
        Assert.Empty(server.Prompts);
    }

    // ---- the pixel hash a replay is compared on ----------------------------------------------------------------------

    [Fact]
    public void Pixel_hashes_ignore_the_row_filter_and_the_ancillary_chunks_that_make_two_files_differ()
    {
        var plain = TinyPng.Make(16, 12, 4);
        var sub = TinyPng.Make(16, 12, 4, filter: 1);
        var up = TinyPng.Make(16, 12, 4, filter: 2, textChunk: "a different workflow text");

        Assert.NotEqual(Hashes.Sha256(plain), Hashes.Sha256(up));
        Assert.Equal(Png.PixelsSha256(plain), Png.PixelsSha256(sub));
        Assert.Equal(Png.PixelsSha256(plain), Png.PixelsSha256(up));
        Assert.NotEqual(Png.PixelsSha256(plain), Png.PixelsSha256(TinyPng.Make(16, 12, 5)));
        Assert.Equal((16, 12), Png.Dimensions(plain));
    }

    // ---- the story and its recipes, over a scripted server -----------------------------------------------------------

    private static (Pipeline Pipeline, FakeComfy Server, string Out) Story(bool optional = false)
    {
        var server = new FakeComfy();
        var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = FakeComfy.Url }, () => new HttpClient(server, disposeHandler: false));
        var output = Path.Combine(Path.GetTempPath(), $"lyntai-pipeline-{Guid.NewGuid():N}");
        var runner = new StepRunner(provider, new HttpClient(server, disposeHandler: false), pollEvery: TimeSpan.FromMilliseconds(1), measureGpu: false);
        return (new Pipeline(Catalog, runner, new PipelineOptions(output, "model-x.safetensors", optional), new PoseMeter(new HttpClient(server, disposeHandler: false), FakeComfy.Url)), server, output);
    }

    [Fact]
    public async Task The_story_runs_a_base_three_variations_two_references_a_drawn_pose_and_a_compose_each_with_a_recipe()
    {
        var (pipeline, server, output) = Story();
        try
        {
            var steps = await pipeline.RunAsync();

            Assert.Equal(["base", "variation-1", "variation-2", "variation-3", "reference-style", "reference-subject", "compose"],
                steps.Select(s => s.Name));
            Assert.Equal(["setup", "variation", "variation", "variation", "setup", "setup", "compose"],
                steps.Select(s => s.Recipe.Template));
            Assert.Equal(7 + 1, server.Prompts.Count);   // seven steps, and the one graph that read the compose's pose

            // the variations start FROM the base image, over three seeds; the compose carries three roles
            var baseHash = steps[0].Recipe.Output.FileSha256;
            Assert.All(steps.Skip(1).Take(3), v => Assert.Equal(baseHash, Assert.Single(v.Recipe.Inputs).Sha256));
            Assert.Equal([201L, 202L, 203L], steps.Skip(1).Take(3).Select(s => s.Recipe.Seed!.Value));
            Assert.Equal(["style", "subject", "pose"], steps[6].Recipe.Inputs.Select(i => i.Role));

            // the style and subject references are the two reference steps' own outputs, byte for byte; the pose is the
            // drawn skeleton the pose folder holds, with the keypoints it was drawn from
            var compose = steps[6].Recipe.Inputs;
            Assert.Equal(steps.Skip(4).Take(2).Select(s => s.Recipe.Output.FileSha256), compose.Take(2).Select(i => i.Sha256));
            var poseDirectory = Path.Combine(output, "07-reference-pose");
            Assert.Equal(Hashes.Sha256(File.ReadAllBytes(Path.Combine(poseDirectory, "image.png"))), compose[2].Sha256);
            Assert.Equal(PoseReference.Joints.Count, JsonNode.Parse(File.ReadAllText(Path.Combine(poseDirectory, "keypoints.json")))!["joints"]!.AsArray().Count);
            Assert.Equal("08-compose", Path.GetFileName(steps[6].Directory));

            foreach (var step in steps)
            {
                Assert.True(File.Exists(Path.Combine(step.Directory, "image.png")));
                var written = Recipe.FromJson(File.ReadAllText(Path.Combine(step.Directory, "recipe.json")));
                Assert.Equal(step.Recipe.Output, written.Output);
                Assert.Equal(Catalog[written.Template].Sha256, written.TemplateSha256);
            }
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public async Task The_compose_runs_the_recorded_pose_settings_and_the_control_runs_the_same_with_the_ControlNet_at_zero()
    {
        var (_, server, output) = Story();
        try
        {
            var control = new Pipeline(Catalog, new StepRunner(
                    new ComfyUiProvider(new ComfyUiOptions { BaseUrl = FakeComfy.Url }, () => new HttpClient(server, disposeHandler: false)),
                    new HttpClient(server, disposeHandler: false), pollEvery: TimeSpan.FromMilliseconds(1), measureGpu: false),
                new PipelineOptions(output, "model-x.safetensors", PoseControl: true), new PoseMeter(new HttpClient(server, disposeHandler: false), FakeComfy.Url));
            var steps = await control.RunAsync();

            var (compose, none) = (steps.Single(s => s.Name == "compose"), steps.Single(s => s.Name == "compose-no-pose"));
            Assert.Equal(1, compose.Recipe.Fields["pose-strength"]);
            Assert.Equal(0, none.Recipe.Fields["pose-strength"]);
            Assert.Equal(Pipeline.ComposePrompt, compose.Recipe.Prompt);
            Assert.Equal(
                compose.Recipe.Fields.Where(f => f.Key != "pose-strength").OrderBy(f => f.Key),
                none.Recipe.Fields.Where(f => f.Key != "pose-strength").OrderBy(f => f.Key));
            Assert.Equal(compose.Recipe.Seed, none.Recipe.Seed);
            Assert.Equal(compose.Recipe.Inputs.Select(i => i.Sha256), none.Recipe.Inputs.Select(i => i.Sha256));
            Assert.Equal(["compose", "compose-no-pose"], control.Poses.Select(p => p.Step));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public async Task A_pose_read_back_as_the_reference_is_recorded_as_followed_and_an_unreadable_one_as_not()
    {
        var (pipeline, server, output) = Story();
        try
        {
            server.OpenPose = OpenPoseJson(PoseReference.ArmsOutWide.Select(k => (k.X, k.Y, 0.9)));
            var compose = (await pipeline.RunAsync()).Single(s => s.Name == "compose");
            var followed = Assert.Single(pipeline.Poses);

            Assert.NotNull(followed.Distance);
            Assert.True(followed.Distance!.ArmAngle < 0.01 && followed.Distance.Keypoint < 0.01);
            var written = JsonNode.Parse(File.ReadAllText(Path.Combine(compose.Directory, "pose-reading.json")))!;
            Assert.True(written["followed"]!.GetValue<bool>());
            Assert.True(File.Exists(Path.Combine(compose.Directory, "pose-reading.png")));

            server.OpenPose = OpenPoseJson(PoseReference.ArmsOutWide.Select(k => (0.0, 0.0, 0.0)));   // DWPose found nothing
            var lost = (await pipeline.MeasurePoseAsync(compose))!;
            Assert.Null(lost.Distance);
            Assert.False(JsonNode.Parse(File.ReadAllText(lost.File))!["followed"]!.GetValue<bool>());
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    private static string OpenPoseJson(IEnumerable<(double X, double Y, double Confidence)> joints) =>
        new JsonArray(new JsonObject
        {
            ["people"] = new JsonArray(new JsonObject
            {
                ["pose_keypoints_2d"] = new JsonArray(joints.SelectMany(j => new[] { j.X * 1024, j.Y * 1024, j.Confidence }).Select(v => (JsonNode)v).ToArray()),
            }),
            ["canvas_height"] = 1024, ["canvas_width"] = 1024,
        }).ToJsonString();

    // ---- the pose reference and its measure --------------------------------------------------------------------------

    [Fact]
    public void The_pose_reference_is_a_black_frame_with_a_coloured_limb_between_each_pair_of_joints()
    {
        var (width, height, bytesPerPixel, pixels) = Png.Decode(PoseReference.Render(PoseReference.ArmsOutWide, 512));
        Assert.Equal((512, 512, 3), (width, height, bytesPerPixel));

        bool Lit(double x, double y) { var at = ((int)(y * 512) * 512 + (int)(x * 512)) * 3; return pixels[at] + pixels[at + 1] + pixels[at + 2] > 0; }
        Assert.All(PoseReference.ArmsOutWide, k => Assert.True(Lit(k.X, k.Y), $"no mark at ({k.X}, {k.Y})"));
        Assert.True(Lit(0.33, 0.31) && Lit(0.80, 0.31));   // mid-forearm on both sides: the arms are drawn out wide
        Assert.False(Lit(0.30, 0.50) || Lit(0.70, 0.50) || Lit(0.5, 0.02));   // and nothing else is on the frame
        Assert.Equal(PoseReference.Joints.Count, PoseReference.ArmsOutWide.Count);
        Assert.Equal(Hashes.Sha256(PoseReference.Render(PoseReference.ArmsOutWide, 64)), Hashes.Sha256(PoseReference.Render(PoseReference.ArmsOutWide, 64)));
    }

    [Fact]
    public void A_pose_measures_zero_from_itself_however_it_is_moved_or_scaled_and_far_from_a_figure_with_its_arms_down()
    {
        var reference = PoseReference.Reading(PoseReference.ArmsOutWide);
        var moved = PoseReference.Reading(PoseReference.ArmsOutWide.Select(k => (k.X * 0.6 + 0.2, k.Y * 0.6 + 0.3)).ToList());
        var armsDown = PoseReference.ArmsOutWide.ToList();
        armsDown[3] = (0.40, 0.46); armsDown[4] = (0.40, 0.60);   // right elbow and wrist hang
        armsDown[6] = (0.60, 0.46); armsDown[7] = (0.60, 0.60);   // and the left

        var same = PoseDistance.Between(reference, moved)!;
        Assert.True(same.ArmAngle < 0.01 && same.Keypoint < 0.01, $"{same}");
        var down = PoseDistance.Between(reference, PoseReference.Reading(armsDown))!;
        Assert.True(down.ArmAngle > 60, $"{down}");
        Assert.True(down.Keypoint > 0.2, $"{down}");
    }

    [Fact]
    public void A_figure_with_a_joint_missing_has_no_pose_to_compare_and_the_distance_says_so()
    {
        var joints = PoseReference.ArmsOutWide.Select(k => ((double, double)?)k).ToList();
        joints[4] = null;   // the right wrist
        Assert.Null(PoseDistance.Between(PoseReference.Reading(PoseReference.ArmsOutWide), new PoseReading(joints, 1)));
        Assert.Null(PoseDistance.Between(PoseReference.Reading(PoseReference.ArmsOutWide), PoseMeter.Parse(null)));
    }

    [Fact]
    public void The_keypoints_a_node_reports_are_read_as_fractions_of_its_canvas_and_a_joint_it_missed_is_null()
    {
        var reading = PoseMeter.Parse(OpenPoseJson(PoseReference.ArmsOutWide.Select((k, i) => (k.X, k.Y, i == 4 ? 0.0 : 1.0))));
        Assert.Equal(1, reading.People);
        Assert.Null(reading["right-wrist"]);
        Assert.Equal(0.86, reading["left-wrist"]!.Value.X, 6);   // pixels of a 1024 canvas, read back as a fraction of it
        Assert.Equal(0.31, reading["left-wrist"]!.Value.Y, 6);
        Assert.Equal(0, PoseMeter.Parse("""[{"people": []}]""").People);
    }

    [Fact]
    public async Task The_optional_steps_add_an_inpaint_and_an_upscale_that_start_from_the_base_image()
    {
        var (pipeline, _, output) = Story(optional: true);
        try
        {
            var steps = await pipeline.RunAsync();

            Assert.Equal(["inpaint", "upscale"], steps.Skip(7).Select(s => s.Name));
            Assert.All(steps.Skip(7), s => Assert.Equal(steps[0].Recipe.Output.FileSha256, Assert.Single(s.Recipe.Inputs).Sha256));
            Assert.Equal(1.5, steps[8].Recipe.Fields["scale"]);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public async Task A_recipe_replays_to_the_same_pixels_even_though_the_file_bytes_differ()
    {
        var (pipeline, server, output) = Story();
        try
        {
            var compose = (await pipeline.RunAsync()).Single(s => s.Name == "compose");
            server.StampFilesWithTheRun = true;   // a real server embeds the run's own workflow text in every PNG

            var replay = await pipeline.ReplayAsync(compose.Directory);

            Assert.True(replay.Identical, replay.Why);
            Assert.False(replay.BytesIdentical);
            Assert.True(replay.TemplateUnchanged);
            Assert.Equal(7 + 1 + 1, server.Prompts.Count);   // the steps, the pose reading, the replay
            // the replay queued the recorded graph: same seed, same words, same fields
            var (first, again) = (JsonNode.Parse(server.Prompts[6])!["prompt"]!, JsonNode.Parse(server.Prompts[8])!["prompt"]!);
            Assert.Equal(At(first, "3.inputs.seed"), At(again, "3.inputs.seed"));
            Assert.Equal(At(first, "6.inputs.text"), At(again, "6.inputs.text"));
            Assert.Equal(At(first, "32.inputs.weight"), At(again, "32.inputs.weight"));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public async Task A_replay_that_comes_back_different_says_so_and_names_what_it_cannot_tell()
    {
        var (pipeline, server, output) = Story();
        try
        {
            var compose = (await pipeline.RunAsync()).Single(s => s.Name == "compose");
            server.PixelsShift = 1;

            var replay = await pipeline.ReplayAsync(compose.Directory);

            Assert.False(replay.Identical);
            Assert.True(replay.TemplateUnchanged);
            Assert.Contains("different pixels", replay.Why);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public async Task A_replay_refuses_an_input_that_no_longer_matches_its_recorded_hash()
    {
        var (pipeline, _, output) = Story();
        try
        {
            var compose = (await pipeline.RunAsync()).Single(s => s.Name == "compose");
            File.WriteAllBytes(Path.Combine(compose.Directory, "inputs", "style.png"), TinyPng.Make(8, 8, 99));

            await Assert.ThrowsAsync<InvalidDataException>(() => pipeline.ReplayAsync(compose.Directory));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public async Task A_recipe_names_each_model_the_graph_loads_and_hashes_those_the_host_points_at()
    {
        var models = Path.Combine(Path.GetTempPath(), $"lyntai-models-{Guid.NewGuid():N}");
        var (_, server, output) = Story();
        try
        {
            Directory.CreateDirectory(Path.Combine(models, "checkpoints"));
            File.WriteAllText(Path.Combine(models, "checkpoints", "model-x.safetensors"), "weights");
            var provider = new ComfyUiProvider(new ComfyUiOptions { BaseUrl = FakeComfy.Url }, () => new HttpClient(server, disposeHandler: false));
            var pipeline = new Pipeline(Catalog, new StepRunner(provider, new HttpClient(server, disposeHandler: false), pollEvery: TimeSpan.FromMilliseconds(1), measureGpu: false),
                new PipelineOptions(output, "model-x.safetensors", ModelsDirectory: models));

            var (_, report) = await pipeline.StepAsync("compose", "compose", Typed(null,
                ("style", TinyPng.Make(8, 8, 1)), ("subject", TinyPng.Make(8, 8, 2)), ("pose", TinyPng.Make(8, 8, 3))), default);

            var byFile = report.Recipe.Models.ToDictionary(m => m.File);
            Assert.Equal(Hashes.Sha256(Encoding.UTF8.GetBytes("weights")), byFile["model-x.safetensors"].Sha256);
            Assert.Equal("checkpoints", byFile["model-x.safetensors"].Kind);
            // the files the host did not point at are named, with no hash invented
            Assert.Null(byFile["ip-adapter-plus_sdxl_vit-h.safetensors"].Sha256);
            Assert.Equal(new[] { "controlnet", "ipadapter", "clip_vision", "checkpoints" }.Order(),
                report.Recipe.Models.Select(m => m.Kind).Distinct().Order());
        }
        finally
        {
            if (Directory.Exists(models)) Directory.Delete(models, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    /// <summary>A ComfyUI that answers every endpoint the provider and the runner use, deterministically: an upload is
    /// stored as <c>stored-n.png</c>, a queued graph finishes at once, its history names each of its SaveImage nodes, and
    /// a view serves a picture that depends on the graph's seed alone.</summary>
    private sealed class FakeComfy : HttpMessageHandler
    {
        public const string Url = "http://127.0.0.1:8188";
        public List<string> Prompts { get; } = [];
        public List<string> Uploads { get; } = [];
        public bool StampFilesWithTheRun { get; set; }
        public int PixelsShift { get; set; }

        /// <summary>What a DWPreprocessor node reports as <c>openpose_json</c>; the default is a pose with nothing found.</summary>
        public string OpenPose { get; set; } = "[{\"people\": []}]";

        private readonly Dictionary<string, JsonNode> _graphs = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/upload/image")
            {
                Uploads.Add($"stored-{Uploads.Count + 1}.png");
                return Json($$"""{"name":"{{Uploads[^1]}}","subfolder":"","type":"input"}""");
            }
            if (path == "/prompt")
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                Prompts.Add(body);
                var id = $"run-{Prompts.Count}";
                _graphs[id] = JsonNode.Parse(body)!["prompt"]!;
                return Json($$"""{"prompt_id":"{{id}}"}""");
            }
            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                var id = path["/history/".Length..];
                var saves = _graphs[id].AsObject().Where(n => n.Value!["class_type"]!.GetValue<string>() == "SaveImage")
                    .Select(n => $$"""
                        "{{n.Key}}":{"images":[{"filename":"{{id}}-{{n.Key}}.png","subfolder":"","type":"output"}]}
                        """);
                // a DWPreprocessor reports its keypoints as text beside the image it draws
                var poses = _graphs[id].AsObject().Where(n => n.Value!["class_type"]!.GetValue<string>() == "DWPreprocessor")
                    .Select(n => $"\"{n.Key}\":{{\"openpose_json\":[{JsonValue.Create(OpenPose)!.ToJsonString()}]}}");
                var outputs = string.Join(",", saves.Concat(poses));
                return Json("{\"" + id + "\":{\"outputs\":{" + outputs + "},\"status\":{\"status_str\":\"success\",\"completed\":true,\"messages\":[]}}}");
            }
            if (path == "/view")
            {
                var name = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["filename"]!;
                var graph = _graphs[name[..name.IndexOf('-', 4)]];
                var seed = (graph["3"]!["inputs"]!["seed"] is { } s ? int.Parse(s.ToJsonString()) : 0) + PixelsShift;
                var node = name[(name.IndexOf('-', 4) + 1)..^4];
                var png = TinyPng.Make(16, 16, seed + node.Length, textChunk: StampFilesWithTheRun ? name : null);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
