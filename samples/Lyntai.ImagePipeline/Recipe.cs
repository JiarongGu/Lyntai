using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lyntai.ImagePipeline;

/// <summary>What a step needs to be run again and what it made: the template and its hash, every filled field, the seed,
/// the models by file name and sha256 where the host's model folder is known, the inputs by hash (their bytes are kept
/// beside the recipe), and the output's hashes.</summary>
public sealed record Recipe
{
    public int RecipeVersion { get; init; } = 1;
    public required string Step { get; init; }
    public required string Template { get; init; }
    public required string TemplateSha256 { get; init; }
    public string? EngineVersion { get; init; }

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
    public IReadOnlyDictionary<string, double> Fields { get; init; } = new Dictionary<string, double>();

    public IReadOnlyList<RecipeInput> Inputs { get; init; } = [];
    public IReadOnlyList<RecipeModel> Models { get; init; } = [];
    public required RecipeOutput Output { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Recipe FromJson(string json) => JsonSerializer.Deserialize<Recipe>(json, Json)!;

    /// <summary>The spec this recipe replays: every typed field and template field as it was, the inputs read from
    /// the files kept beside <paramref name="directory"/> (each checked against its recorded hash).</summary>
    public StepSpec ToSpec(string directory)
    {
        var inputs = new List<(string, byte[])>();
        foreach (var input in Inputs)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, input.File));
            if (Hashes.Sha256(bytes) != input.Sha256)
                throw new InvalidDataException($"input {input.File} no longer matches its recorded sha256");
            inputs.Add((input.Role, bytes));
        }
        return new StepSpec
        {
            Prompt = Prompt, NegativePrompt = NegativePrompt, Seed = Seed, Steps = Steps, CfgScale = CfgScale,
            Sampler = Sampler, Scheduler = Scheduler, Checkpoint = Checkpoint, Width = Width, Height = Height,
            Fields = Fields, Inputs = inputs,
        };
    }
}

public sealed record RecipeInput(string Role, string File, string Sha256);

/// <summary>A model file a template names, with the folder kind a host files it under; the hash is null when the host
/// did not say where its model folders are.</summary>
public sealed record RecipeModel(string Kind, string File, string? Sha256);

/// <summary><c>PixelsSha256</c> is what a replay is compared on; the file's own bytes also carry ComfyUI's embedded
/// workflow text, which names the run's uploaded files.</summary>
public sealed record RecipeOutput(string File, string FileSha256, string PixelsSha256, int Width, int Height);

public static class Hashes
{
    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}
