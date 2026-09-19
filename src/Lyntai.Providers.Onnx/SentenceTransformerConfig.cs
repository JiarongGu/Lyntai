using System.Text.Json;

namespace Lyntai.Providers.Onnx;

/// <summary>How a sentence-transformer export says it wants to be run, read from the files it ships.
///
/// <para><b>Read rather than defaulted, for the reason the whole class exists.</b> Pooling and
/// normalization are not preferences — they are part of how the model was TRAINED, so guessing produces
/// finite, plausible vectors that rank wrongly. A <c>sentence-transformers</c> export states both:
/// <c>1_Pooling/config.json</c> names the pooling mode and <c>modules.json</c> lists whether a
/// <c>Normalize</c> module follows.</para></summary>
/// <param name="Pooling">Mean over attended tokens, or the classification token alone.</param>
/// <param name="Normalize">Whether a <c>Normalize</c> module is in the model's module list.</param>
/// <param name="MaxTokens">The architectural position limit, including both special tokens.</param>
internal sealed record SentenceTransformerConfig(OnnxPooling Pooling, bool Normalize, int MaxTokens)
{
    /// <summary>What sentence-transformers does when a file is absent. MEAN because it is the overwhelming
    /// default for the class; NOT normalized because that module is opt-in and its absence is meaningful;
    /// 512 because every BERT-family encoder this package targets has that position limit.</summary>
    private static readonly SentenceTransformerConfig Defaults = new(OnnxPooling.Mean, false, 512);

    public static SentenceTransformerConfig FromDirectory(string directory) => new(
        PoolingFrom(Path.Combine(directory, "1_Pooling", "config.json")) ?? Defaults.Pooling,
        NormalizesFrom(Path.Combine(directory, "modules.json")) ?? Defaults.Normalize,
        MaxTokensFrom(Path.Combine(directory, "config.json")) ?? Defaults.MaxTokens);

    /// <summary>CLS only when the model says so explicitly — the flags are not mutually exclusive in the
    /// file, and mean is the safe reading when both or neither is set.</summary>
    private static OnnxPooling? PoolingFrom(string path) => Read<OnnxPooling>(path, root =>
        root.TryGetProperty("pooling_mode_cls_token", out var cls) && cls.ValueKind == JsonValueKind.True
        && !(root.TryGetProperty("pooling_mode_mean_tokens", out var mean) && mean.ValueKind == JsonValueKind.True)
            ? OnnxPooling.Cls
            : OnnxPooling.Mean);

    private static bool? NormalizesFrom(string path) => Read<bool>(path, root =>
        root.ValueKind == JsonValueKind.Array
        && root.EnumerateArray().Any(m =>
            m.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            && type.GetString()!.EndsWith("Normalize", StringComparison.Ordinal)));

    private static int? MaxTokensFrom(string path) => Read<int>(path, root =>
        root.TryGetProperty("max_position_embeddings", out var max)
        && max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var value) && value > 2
            ? value
            : (int?)null);

    /// <summary>Parse one file, or null when it is absent or unreadable — a hand-assembled model directory
    /// is a real case and is not worth refusing to load over.</summary>
    private static T? Read<T>(string path, Func<JsonElement, T?> project) where T : struct
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return project(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
