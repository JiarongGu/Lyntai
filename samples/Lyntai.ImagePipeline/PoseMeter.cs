using System.Text.Json.Nodes;

namespace Lyntai.ImagePipeline;

/// <summary>The body keypoints DWPose read from a picture, in OpenPose's order (<see cref="PoseReference.Joints"/>), as
/// fractions of the frame; a joint it did not find is null.</summary>
public sealed record PoseReading(IReadOnlyList<(double X, double Y)?> Joints, int People)
{
    public (double X, double Y)? this[string joint] => Joints[PoseReference.Joints.ToList().IndexOf(joint)];
}

/// <summary>How far one pose is from another, by two measures that ignore where the figure stands and how big it is.</summary>
/// <param name="ArmAngle">Mean over both arms of the angle, in degrees, between the shoulder-to-wrist directions.</param>
/// <param name="Keypoint">Mean distance between the matching limb joints after each pose is centred on its neck and scaled by
/// its own neck-to-hip-midpoint length, so 0.5 is half a torso.</param>
public sealed record PoseDistance(double ArmAngle, double Keypoint)
{
    private static readonly string[] Limbs =
    [
        "right-shoulder", "right-elbow", "right-wrist", "left-shoulder", "left-elbow", "left-wrist",
        "right-hip", "right-knee", "right-ankle", "left-hip", "left-knee", "left-ankle",
    ];

    /// <summary>The distance of <paramref name="actual"/> from <paramref name="reference"/>, or null when either lacks a
    /// joint a measure needs (a figure DWPose could not read has no pose to compare, which is not a match).</summary>
    public static PoseDistance? Between(PoseReading reference, PoseReading actual)
    {
        var (r, a) = (Normalise(reference), Normalise(actual));
        if (r is null || a is null) return null;

        double arm = 0;
        foreach (var side in new[] { "right", "left" })
        {
            var (rs, rw) = (reference[$"{side}-shoulder"]!.Value, reference[$"{side}-wrist"]!.Value);
            var (s, w) = (actual[$"{side}-shoulder"]!.Value, actual[$"{side}-wrist"]!.Value);
            var turn = Math.Abs(Math.Atan2(rw.Y - rs.Y, rw.X - rs.X) - Math.Atan2(w.Y - s.Y, w.X - s.X)) * 180 / Math.PI;
            arm += turn > 180 ? 360 - turn : turn;
        }
        var keypoint = Limbs.Average(j => Math.Sqrt(Math.Pow(r[j].X - a[j].X, 2) + Math.Pow(r[j].Y - a[j].Y, 2)));
        return new PoseDistance(arm / 2, keypoint);
    }

    private static Dictionary<string, (double X, double Y)>? Normalise(PoseReading reading)
    {
        if (Limbs.Concat(["neck"]).Any(j => reading[j] is null)) return null;
        var neck = reading["neck"]!.Value;
        var (rh, lh) = (reading["right-hip"]!.Value, reading["left-hip"]!.Value);
        var torso = Math.Sqrt(Math.Pow((rh.X + lh.X) / 2 - neck.X, 2) + Math.Pow((rh.Y + lh.Y) / 2 - neck.Y, 2));
        if (torso < 1e-6) return null;
        return Limbs.ToDictionary(j => j, j => ((reading[j]!.Value.X - neck.X) / torso, (reading[j]!.Value.Y - neck.Y) / torso));
    }
}

/// <summary>Reads the pose of a picture the way the compose step is asked to follow one: <c>DWPreprocessor</c> run on the
/// whole frame (detector <c>None</c>, the estimator <c>dw-ll_ucoco_384.onnx</c>) on the same server, its keypoints taken from
/// the history entry the node writes beside its drawn skeleton. A direct call to ComfyUI's HTTP API, because the provider
/// returns images and these are numbers.</summary>
public sealed class PoseMeter(HttpClient http, string baseUrl, TimeSpan? budget = null)
{
    /// <summary>The graph a reading runs, public so a host can hash the models it loads.</summary>
    public const string Graph = """
        {
          "1": {"class_type": "LoadImage", "inputs": {"image": "pose-measure.png"}},
          "2": {"class_type": "DWPreprocessor", "inputs": {"image": ["1", 0], "detect_hand": "disable", "detect_body": "enable",
                "detect_face": "disable", "resolution": 1024, "bbox_detector": "None",
                "pose_estimator": "dw-ll_ucoco_384.onnx", "scale_stick_for_xinsr_cn": "disable"}},
          "3": {"class_type": "SaveImage", "inputs": {"images": ["2", 0], "filename_prefix": "pipeline-pose-reading"}}
        }
        """;

    private readonly string _url = baseUrl.TrimEnd('/');
    private readonly TimeSpan _budget = budget ?? TimeSpan.FromMinutes(5);

    /// <summary>The reading and the skeleton image DWPose drew from <paramref name="png"/>.</summary>
    public async Task<(PoseReading Reading, byte[] Skeleton)> ReadAsync(byte[] png, CancellationToken ct = default)
    {
        var name = $"pose-measure-{Guid.NewGuid():N}.png";
        using (var form = new MultipartFormDataContent { { new ByteArrayContent(png), "image", name }, { new StringContent("true"), "overwrite" } })
            (await http.PostAsync($"{_url}/upload/image", form, ct)).EnsureSuccessStatusCode();

        var graph = JsonNode.Parse(Graph)!;
        graph["1"]!["inputs"]!["image"] = name;
        var queued = await http.PostAsync($"{_url}/prompt",
            new StringContent(new JsonObject { ["prompt"] = graph }.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), ct);
        var body = await queued.Content.ReadAsStringAsync(ct);
        if (!queued.IsSuccessStatusCode) throw new InvalidOperationException($"the pose reading was refused: {body}");
        var id = JsonNode.Parse(body)!["prompt_id"]!.GetValue<string>();

        var deadline = DateTime.UtcNow + _budget;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, ct);
            var history = JsonNode.Parse(await http.GetStringAsync($"{_url}/history/{id}", ct))!.AsObject();
            if (!history.TryGetPropertyValue(id, out var entry) || entry is null) continue;
            if (entry["status"]?["status_str"]?.GetValue<string>() == "error")
                throw new InvalidOperationException($"the pose reading failed: {entry["status"]}");
            var outputs = entry["outputs"]?.AsObject();
            if (outputs is null || !outputs.ContainsKey("3")) continue;

            var image = outputs["3"]!["images"]![0]!;
            var skeleton = await http.GetByteArrayAsync(
                $"{_url}/view?filename={Uri.EscapeDataString(image["filename"]!.GetValue<string>())}" +
                $"&subfolder={Uri.EscapeDataString(image["subfolder"]!.GetValue<string>())}&type={image["type"]!.GetValue<string>()}", ct);
            return (Parse(outputs["2"]?["openpose_json"]?[0]?.GetValue<string>()), skeleton);
        }
        throw new TimeoutException($"the pose reading did not finish inside {_budget}");
    }

    /// <summary>The first person of the node's <c>openpose_json</c>. The node reports pixels of its own canvas, with
    /// confidence 0 (and position 0) for a joint it did not find, so each is divided by the canvas into a fraction of the frame.</summary>
    public static PoseReading Parse(string? openposeJson)
    {
        if (string.IsNullOrWhiteSpace(openposeJson)) return new PoseReading(Enumerable.Repeat<(double, double)?>(null, PoseReference.Joints.Count).ToList(), 0);
        var frame = JsonNode.Parse(openposeJson)!;
        var first = frame is JsonArray list ? list[0]! : frame;
        var (canvasWidth, canvasHeight) = (first["canvas_width"]?.GetValue<double>() ?? 1, first["canvas_height"]?.GetValue<double>() ?? 1);
        var people = first["people"]!.AsArray();
        if (people.Count == 0) return new PoseReading(Enumerable.Repeat<(double, double)?>(null, PoseReference.Joints.Count).ToList(), 0);

        var flat = people[0]!["pose_keypoints_2d"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
        var joints = new List<(double X, double Y)?>();
        for (var i = 0; i < PoseReference.Joints.Count; i++)
            joints.Add(flat[i * 3 + 2] > 0 ? (flat[i * 3] / canvasWidth, flat[i * 3 + 1] / canvasHeight) : null);
        return new PoseReading(joints, people.Count);
    }
}
