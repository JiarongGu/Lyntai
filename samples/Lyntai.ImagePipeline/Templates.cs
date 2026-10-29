using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Inference;

namespace Lyntai.ImagePipeline;

/// <summary>What a step asks of its template: the request's typed fields, the template's own numeric fields, and the
/// inputs by role. A field left null is not sent, so the template's own value stands.</summary>
public sealed record StepSpec
{
    public string? Prompt { get; init; }
    public string? NegativePrompt { get; init; }
    public long? Seed { get; init; }
    public int? Steps { get; init; }
    public double? CfgScale { get; init; }
    public string? Sampler { get; init; }
    public string? Scheduler { get; init; }
    public string? Checkpoint { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }

    /// <summary>Values for the fields the template names under <c>fields</c> in the manifest — a denoise strength, an
    /// IP-Adapter weight: the request has no typed field for them, so the sample writes them into the graph itself.</summary>
    public IReadOnlyDictionary<string, double> Fields { get; init; } = new Dictionary<string, double>();

    /// <summary>Each input's role (a name the template maps with <c>input-path:&lt;role&gt;</c>) and its PNG bytes.</summary>
    public IReadOnlyList<(string Role, byte[] Png)> Inputs { get; init; } = [];
}

/// <summary>One tracked API-format workflow plus where each of a request's fields lands in it. The graph file is
/// ComfyUI's own format and nothing else; the paths live in <c>manifest.json</c> beside it, because an extra key in the
/// graph would be a node ComfyUI refuses.</summary>
public sealed class Template(
    string id, string file, string graphJson, string sha256, string output,
    IReadOnlyDictionary<string, string> extraOutputs,
    IReadOnlyDictionary<string, string> options, IReadOnlyDictionary<string, string> fields)
{
    public string Id { get; } = id;
    public string File { get; } = file;
    public string GraphJson { get; } = graphJson;

    /// <summary>The graph file's sha256, which a recipe records so a replay can say the template changed.</summary>
    public string Sha256 { get; } = sha256;

    /// <summary>The graph node whose image is the step's result.</summary>
    public string Output { get; } = output;

    /// <summary>Other saved nodes worth keeping, by a name (a mask a graph drew, say).</summary>
    public IReadOnlyDictionary<string, string> ExtraOutputs { get; } = extraOutputs;

    /// <summary>The provider's own option keys (<c>prompt-path</c>, <c>input-path:style</c>…) to graph paths.</summary>
    public IReadOnlyDictionary<string, string> Options { get; } = options;

    /// <summary>The template's own numeric fields: a name to a graph path.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; } = fields;

    /// <summary>The roles this template accepts: exactly the ones it maps an <c>input-path:&lt;role&gt;</c> for, since the
    /// provider refuses an input whose role has nowhere to go.</summary>
    public IReadOnlyList<string> Roles { get; } =
        options.Keys.Where(k => k.StartsWith("input-path:", StringComparison.Ordinal)).Select(k => k["input-path:".Length..]).ToList();

    /// <summary>The request that runs this template for <paramref name="spec"/>: the typed fields set, the inputs
    /// attached by role, the graph (with the template's own fields written) in <c>Options["workflow"]</c> and the
    /// paths beside it. Throws on a field the manifest does not name, or whose path the graph lacks.</summary>
    public MediaRequest Fill(StepSpec spec)
    {
        var graph = JsonNode.Parse(GraphJson)!;
        foreach (var (name, value) in spec.Fields)
        {
            if (!Fields.TryGetValue(name, out var path))
                throw new ArgumentException($"template '{Id}' has no field '{name}' (it names: {string.Join(", ", Fields.Keys)})");
            Write(graph, path, JsonValue.Create(value));
        }

        var options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase)
        {
            ["workflow"] = graph.ToJsonString(),
        };
        if (spec.Width is { } width && spec.Height is { } height)
            options["size"] = string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");

        return new MediaRequest
        {
            Kind = ProviderKinds.Image,
            Prompt = spec.Prompt,
            NegativePrompt = spec.NegativePrompt,
            Seed = spec.Seed,
            Steps = spec.Steps,
            CfgScale = spec.CfgScale,
            Sampler = spec.Sampler,
            Scheduler = spec.Scheduler,
            Checkpoint = spec.Checkpoint,
            Inputs = spec.Inputs.Select(i => MediaInput.From(i.Role, i.Png, "image/png")).ToList(),
            Options = options,
        };
    }

    private static void Write(JsonNode graph, string dottedPath, JsonNode value)
    {
        var segments = dottedPath.Split('.');
        var node = graph;
        foreach (var segment in segments[..^1])
            node = node?[segment] ?? throw new ArgumentException($"'{dottedPath}' is not in the graph");
        if (node is not JsonObject owner || !owner.ContainsKey(segments[^1]))
            throw new ArgumentException($"'{dottedPath}' is not a field of the graph");
        owner[segments[^1]] = value;
    }
}

/// <summary>The template folder: <c>manifest.json</c> and the graph files it names.</summary>
public static class Templates
{
    public static IReadOnlyDictionary<string, Template> Load(string directory)
    {
        var manifest = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(directory, "manifest.json")))!.AsObject();
        var templates = new Dictionary<string, Template>();
        foreach (var (id, entry) in manifest)
        {
            var file = entry!["file"]!.GetValue<string>();
            var path = Path.Combine(directory, file);
            var bytes = System.IO.File.ReadAllBytes(path);
            templates[id] = new Template(
                id, file, System.Text.Encoding.UTF8.GetString(bytes),
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                entry["output"]!.GetValue<string>(),
                Map(entry["extraOutputs"]), Map(entry["options"]), Map(entry["fields"]));
        }
        return templates;
    }

    /// <summary>The template folder beside the running binary, else the sample's own source folder when run from the tree.</summary>
    public static string DefaultDirectory()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "templates");
        if (Directory.Exists(beside)) return beside;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, "samples", "Lyntai.ImagePipeline", "templates");
            if (Directory.Exists(source)) return source;
        }
        throw new DirectoryNotFoundException("no templates folder beside the binary or in samples/Lyntai.ImagePipeline");
    }

    private static IReadOnlyDictionary<string, string> Map(JsonNode? node) =>
        node is JsonObject obj
            ? obj.ToDictionary(p => p.Key, p => p.Value!.GetValue<string>())
            : new Dictionary<string, string>();
}
