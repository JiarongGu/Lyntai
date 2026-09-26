using System.Text.Json;

namespace Lyntai.Tests.Text;

/// <summary>The committed SentencePiece fixture and the golden answers <c>devtools/onnx/spm-fixture.py</c>
/// generated for it: <c>spm-tiny.*</c> is a tiny XLM-R-shaped model, <c>spm-xlmr.golden.json</c> the answers for
/// the real XLM-R tokenizer, whose files are too large to commit.</summary>
internal static class SentencePieceFixture
{
    /// <summary>The fixture directory, beside this file in the source tree.</summary>
    public static string Directory => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Text", "Fixtures"));

    /// <summary>The tiny model's <c>tokenizer.json</c>.</summary>
    public static string TokenizerPath => Path.Combine(Directory, "spm-tiny.tokenizer.json");

    public static Golden Load(string name) =>
        JsonSerializer.Deserialize<Golden>(File.ReadAllText(Path.Combine(Directory, name)), Options)!;

    /// <summary>The tiny model's precompiled charsmap, decoded.</summary>
    public static byte[] Charsmap()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TokenizerPath));
        return Convert.FromBase64String(
            document.RootElement.GetProperty("normalizer").GetProperty("precompiled_charsmap").GetString()!);
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <param name="Cases">Inputs HF <c>tokenizers</c> and C++ <c>sentencepiece</c> agree on.</param>
    /// <param name="HfOnly">Inputs they disagree on for a stated reason; the golden is HF's.</param>
    /// <param name="TypedSpecials">Special tokens typed in text: <c>Ids</c> keeps them text, <c>HfIds</c> is HF's parse.</param>
    /// <param name="Pairs">Pairs framed by the model's template.</param>
    internal sealed record Golden(Case[] Cases, Case[] HfOnly, TypedSpecial[] TypedSpecials, Pair[] Pairs);

    internal sealed record Case(string Text, string? Normalized, int[] Ids, int[]? SpmIds, string? Why);

    internal sealed record TypedSpecial(string Text, int[] Ids, int[] HfIds);

    internal sealed record Pair(string A, string B, int[] Ids, int[] TypeIds);

    /// <summary>Non-printable-ASCII as <c>\uXXXX</c>, so a failure message shows what the text really held.</summary>
    public static string Escape(string text) =>
        string.Concat(text.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:x4}"));
}
