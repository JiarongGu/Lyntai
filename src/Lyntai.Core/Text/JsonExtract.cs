using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Lyntai.Text;

/// <summary>Tolerant JSON extraction from LLM prose (design §6): strips code fences, finds the
/// first balanced <c>{…}</c> object, and (via <see cref="TryParseObject"/>) parses it.</summary>
public static class JsonExtract
{
    public static string? ExtractObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;               // skip the escaped char
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return text[start..(i + 1)];
                    break;
            }
        }
        return null; // unbalanced
    }

    /// <summary>Extract the first balanced JSON object from <paramref name="text"/> (via
    /// <see cref="ExtractObject"/>) AND parse it. Returns false — with a null <paramref name="doc"/> — when
    /// nothing balanced is found or it doesn't parse. The caller OWNS the returned document and must dispose
    /// it (wrap in a <c>using</c>). This is the shared "tolerantly read a JSON object out of an LLM reply"
    /// primitive behind the judge/comparer verdict parsers.</summary>
    public static bool TryParseObject(string? text, [NotNullWhen(true)] out JsonDocument? doc)
    {
        doc = null;
        var json = ExtractObject(text);
        if (json is null) return false;
        try { doc = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
    }

    /// <summary>Whether <paramref name="text"/> parses as JSON in its entirety (any JSON value, not just an
    /// object). Null/blank → false.</summary>
    public static bool IsValid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { using var _ = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }
}
