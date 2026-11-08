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

    /// <summary>Read the export's own configuration. A file that is absent or unreadable keeps its default;
    /// one that DECLARES a pooling or a module this reader does not run is refused when
    /// <paramref name="refuseUnsupported"/> is set, rather than read as mean pooling.</summary>
    /// <param name="directory">The model directory.</param>
    /// <param name="refuseUnsupported">False when the caller states the pooling itself, or runs no pooling head.</param>
    /// <exception cref="InvalidDataException">An unsupported pooling mode or module is declared.</exception>
    public static SentenceTransformerConfig FromDirectory(string directory, bool refuseUnsupported = true)
    {
        var pooling = Path.Combine(directory, "1_Pooling", "config.json");
        var modules = Path.Combine(directory, "modules.json");
        if (refuseUnsupported && (UnsupportedPooling(pooling) ?? UnsupportedModule(modules)) is { } declared)
            throw new InvalidDataException(
                $"{declared} This reader runs cls and mean pooling and no module past the pooling but Normalize; "
                + "the export would be mean-pooled and rank wrongly. Set OnnxProviderOptions.Pooling to run it anyway.");

        return new(
            PoolingFrom(pooling) ?? Defaults.Pooling,
            NormalizesFrom(modules) ?? Defaults.Normalize,
            WindowFrom(directory));
    }

    private static readonly string[] UnsupportedPoolingFlags =
        ["pooling_mode_lasttoken", "pooling_mode_max_tokens", "pooling_mode_weightedmean_tokens"];

    private static readonly string[] RunPoolingModes = ["cls", "mean", "mean_sqrt_len_tokens"];

    private static readonly string[] RunModules = ["Transformer", "Pooling", "Normalize"];

    private static string? UnsupportedPooling(string path) => ReadReference(path, root =>
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var flag in UnsupportedPoolingFlags)
            if (root.TryGetProperty(flag, out var set) && set.ValueKind == JsonValueKind.True)
                return $"'1_Pooling/config.json' declares {flag}.";
        return root.TryGetProperty("pooling_mode", out var mode) && mode.ValueKind == JsonValueKind.String
            && !RunPoolingModes.Contains(mode.GetString(), StringComparer.OrdinalIgnoreCase)
                ? $"'1_Pooling/config.json' declares pooling_mode {mode.GetString()}."
                : null;
    });

    private static string? UnsupportedModule(string path) => ReadReference(path, root =>
    {
        if (root.ValueKind != JsonValueKind.Array) return null;
        foreach (var module in root.EnumerateArray())
        {
            if (module.ValueKind != JsonValueKind.Object || !module.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String) continue;
            var name = type.GetString()![(type.GetString()!.LastIndexOf('.') + 1)..];
            if (!RunModules.Contains(name, StringComparer.Ordinal))
                return $"'modules.json' lists a {name} module.";
        }
        return null;
    });

    private static string? ReadReference(string path, Func<JsonElement, string?> project)
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

    /// <summary>The model types whose position ids start after the padding index, so the table holds
    /// <c>pad_token_id + 1</c> positions the tokens never take.</summary>
    private static readonly string[] PaddedPositionModels = ["roberta", "xlm-roberta", "camembert", "mpnet"];

    /// <summary>The architectural position limit, narrowed to whichever declaration is SMALLER: the tokenizer's
    /// <c>model_max_length</c>, or sentence-transformers' own <c>max_seq_length</c> — where the reference
    /// pipeline truncates, 256 for all-MiniLM-L6-v2 (<c>docs/DECISIONS.md</c> <b>D195</b>). <b>The RoBERTa
    /// family is load-bearing here</b>: position ids start after the padding index, so a declared 514 holds 512
    /// tokens and a 514-token row indexes past the table — read from <c>model_type</c> and <c>pad_token_id</c>
    /// where no narrower declaration ships. A larger declaration never widens it — potion declares
    /// 1,000,000.</summary>
    private static int WindowFrom(string directory)
    {
        var config = Path.Combine(directory, "config.json");
        var window = LimitFrom(config, "max_position_embeddings") ?? Defaults.MaxTokens;
        if (Read<int>(config, root => root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("model_type", out var type) && type.ValueKind == JsonValueKind.String
                && PaddedPositionModels.Contains(type.GetString(), StringComparer.OrdinalIgnoreCase)
                    ? (root.TryGetProperty("pad_token_id", out var pad) && pad.ValueKind == JsonValueKind.Number
                        && pad.TryGetInt32(out var index) && index >= 0 ? index : 1) + 1
                    : (int?)null) is { } skipped && window - skipped > 2)
            window -= skipped;
        foreach (var (file, property) in Narrowing)
            if (LimitFrom(Path.Combine(directory, file), property) is { } declared && declared < window)
                window = declared;
        return window;
    }

    private static readonly (string File, string Property)[] Narrowing =
        [("tokenizer_config.json", "model_max_length"), ("sentence_bert_config.json", "max_seq_length")];

    // HF writes int(1e30) for "unset", which is no int32 and so declares nothing
    private static int? LimitFrom(string path, string property) => Read<int>(path, root =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var max)
        && max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var value) && value > 2
            ? value
            : (int?)null);

    /// <summary>CLS only when the model says so explicitly — the flags are not mutually exclusive in the
    /// file, and mean is the safe reading when both or neither is set.</summary>
    private static OnnxPooling? PoolingFrom(string path) => Read<OnnxPooling>(path, root =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("pooling_mode_cls_token", out var cls) && cls.ValueKind == JsonValueKind.True
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
