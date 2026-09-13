using System.Text.Json;

namespace Lyntai.Text;

/// <summary>The tokenization rules a model declares in its own <c>tokenizer_config.json</c>.
///
/// <para><b>Read from the model rather than defaulted</b>: a table or a weight matrix was BUILT against
/// the ids these rules produce, so a disagreement does not fail — it returns plausible vectors that rank
/// wrongly, and nothing downstream can attribute the loss.</para>
///
/// <para>Internal, and reached through <see cref="WordPieceTokenizer.FromModelDirectory"/> rather than
/// exposed: one public entry point is a smaller permanent promise than two.</para></summary>
/// <param name="Lowercase"><c>do_lower_case</c>.</param>
/// <param name="StripAccents"><c>strip_accents</c>. Null means FOLLOW <paramref name="Lowercase"/>, which
/// is what the reference pipeline does and what every shipped BERT config states.</param>
/// <param name="TokenizeChineseCharacters"><c>tokenize_chinese_chars</c>.</param>
/// <param name="UnknownToken"><c>unk_token</c>.</param>
/// <param name="ClassificationToken"><c>cls_token</c>.</param>
/// <param name="SeparatorToken"><c>sep_token</c>.</param>
internal sealed record TokenizerRules(
    bool Lowercase, bool? StripAccents, bool TokenizeChineseCharacters, string UnknownToken,
    string ClassificationToken, string SeparatorToken)
{
    /// <summary>What a BERT tokenizer does when its config says nothing — the reference implementation's
    /// own defaults, so a model shipped without the file behaves as its author assumed.</summary>
    private static readonly TokenizerRules Defaults = new(true, null, true, "[UNK]", "[CLS]", "[SEP]");

    /// <summary>Read <c>tokenizer_config.json</c> from <paramref name="directory"/>, falling back to
    /// <see cref="Defaults"/> when it is absent or unreadable — a missing config is the common case for a
    /// hand-assembled model directory and is not worth refusing to load over.</summary>
    public static TokenizerRules FromDirectory(string directory)
    {
        var path = Path.Combine(directory, "tokenizer_config.json");
        if (!File.Exists(path)) return Defaults;

        try
        {
            using var config = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = config.RootElement;
            return new TokenizerRules(
                Flag(root, "do_lower_case") ?? Defaults.Lowercase,
                Flag(root, "strip_accents"),
                Flag(root, "tokenize_chinese_chars") ?? Defaults.TokenizeChineseCharacters,
                Text(root, "unk_token") ?? Defaults.UnknownToken,
                Text(root, "cls_token") ?? Defaults.ClassificationToken,
                Text(root, "sep_token") ?? Defaults.SeparatorToken);
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    /// <summary>A declared boolean, or null for absent AND for an explicit <c>null</c> — the two mean the
    /// same thing to the reference pipeline, which is why they are not distinguished.</summary>
    private static bool? Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind is JsonValueKind.True
            : null;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
