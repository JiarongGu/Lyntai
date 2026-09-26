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
/// <param name="MaxTokens">The positions a row may take, including its special tokens: the architectural limit,
/// narrowed to a smaller declared <c>model_max_length</c> or <c>max_seq_length</c>.</param>
internal sealed record SentenceTransformerConfig(OnnxPooling Pooling, bool Normalize, int MaxTokens)
{
    /// <summary>What sentence-transformers does when a file is absent. MEAN because it is the overwhelming
    /// default for the class; NOT normalized because that module is opt-in and its absence is meaningful;
    /// 512 because every BERT-family encoder this package targets has that position limit.</summary>
    private static readonly SentenceTransformerConfig Defaults = new(OnnxPooling.Mean, false, 512);

    public static SentenceTransformerConfig FromDirectory(string directory) => new(
        PoolingFrom(Path.Combine(directory, "1_Pooling", "config.json")) ?? Defaults.Pooling,
        NormalizesFrom(Path.Combine(directory, "modules.json")) ?? Defaults.Normalize,
        WindowFrom(directory));

    /// <summary>The architectural position limit, narrowed to whichever declaration is SMALLER: the tokenizer's
    /// <c>model_max_length</c>, or sentence-transformers' own <c>max_seq_length</c> — where the reference
    /// pipeline truncates, 256 for all-MiniLM-L6-v2 (<c>docs/DECISIONS.md</c> <b>D195</b>). <b>The first is
    /// load-bearing for the RoBERTa family</b>: position ids start after the padding index, so a declared 514
    /// holds 512 tokens and a 514-token row indexes past the table. A larger declaration never widens it —
    /// potion declares 1,000,000.</summary>
    private static int WindowFrom(string directory)
    {
        var window = LimitFrom(Path.Combine(directory, "config.json"), "max_position_embeddings") ?? Defaults.MaxTokens;
        foreach (var (file, property) in Narrowing)
            if (LimitFrom(Path.Combine(directory, file), property) is { } declared && declared < window)
                window = declared;
        return window;
    }

    private static readonly (string File, string Property)[] Narrowing =
        [("tokenizer_config.json", "model_max_length"), ("sentence_bert_config.json", "max_seq_length")];

    // HF writes int(1e30) for "unset", which is no int32 and so declares nothing
    private static int? LimitFrom(string path, string property) => Read<int>(path, root =>
        root.TryGetProperty(property, out var max)
        && max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var value) && value > 2
            ? value
            : (int?)null);

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
