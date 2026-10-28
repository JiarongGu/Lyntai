using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.ImagePipeline;

/// <summary>Which model files a filled graph loads, and their sha256 when the host says where its model folders are.
/// The files are the host's: nothing here fetches one, and an unknown folder leaves the hash null.</summary>
public sealed class ModelCatalog(string? modelsDirectory, string? dwposeDirectory, string? cachePath)
{
    // (loader node, the input naming the file) -> the folder kind a ComfyUI install files it under
    private static readonly (string ClassType, string Field, string Kind)[] Loaders =
    [
        ("CheckpointLoaderSimple", "ckpt_name", "checkpoints"),
        ("LoraLoader", "lora_name", "loras"),
        ("VAELoader", "vae_name", "vae"),
        ("CLIPVisionLoader", "clip_name", "clip_vision"),
        ("IPAdapterModelLoader", "ipadapter_file", "ipadapter"),
        ("ControlNetLoader", "control_net_name", "controlnet"),
        ("DWPreprocessor", "bbox_detector", "dwpose"),
        ("DWPreprocessor", "pose_estimator", "dwpose"),
    ];

    private Dictionary<string, string>? _cache;

    /// <summary>The model files <paramref name="workflowJson"/> loads, in node order, each with its hash if resolvable.
    /// The request's checkpoint is written by the provider at submit, so the graph handed over still names the
    /// template's placeholder: <paramref name="checkpointPath"/> and <paramref name="checkpoint"/> say what it will load.</summary>
    public IReadOnlyList<RecipeModel> Of(string workflowJson, string? checkpointPath = null, string? checkpoint = null)
    {
        var graph = JsonNode.Parse(workflowJson)!.AsObject();
        if (checkpointPath is not null && checkpoint is not null)
        {
            var segments = checkpointPath.Split('.');
            JsonNode? owner = graph;
            foreach (var segment in segments[..^1]) owner = owner?[segment];
            if (owner is JsonObject target) target[segments[^1]] = checkpoint;
        }

        var models = new List<RecipeModel>();
        foreach (var (_, node) in graph)
        {
            var classType = node?["class_type"]?.GetValue<string>();
            foreach (var (loader, field, kind) in Loaders)
                if (classType == loader && node!["inputs"]?[field]?.GetValue<string>() is { Length: > 0 } file && file != "None")
                    models.Add(new RecipeModel(kind, file, Hash(kind, file)));
        }
        return models.DistinctBy(m => (m.Kind, m.File)).ToList();
    }

    private string? Hash(string kind, string file)
    {
        var directory = kind == "dwpose" ? dwposeDirectory : modelsDirectory is null ? null : Path.Combine(modelsDirectory, kind);
        var path = directory is null ? null : Path.Combine(directory, file);
        if (path is null || !File.Exists(path)) return null;

        // keyed by size and write time, so a replaced file is hashed again rather than trusted by name
        var info = new FileInfo(path);
        var key = $"{kind}/{file}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        _cache ??= cachePath is not null && File.Exists(cachePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(cachePath)) ?? []
            : [];
        if (_cache.TryGetValue(key, out var known)) return known;

        using var stream = File.OpenRead(path);
        var sha = Convert.ToHexStringLower(SHA256.HashData(stream));
        _cache[key] = sha;
        if (cachePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        return sha;
    }
}
