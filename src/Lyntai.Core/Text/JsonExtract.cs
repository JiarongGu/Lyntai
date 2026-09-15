using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Lyntai.Text;

/// <summary>Tolerant JSON extraction from LLM prose (design §6): strips code fences, finds the
/// first balanced <c>{…}</c> object, and parses it — via <see cref="TryParseObject"/> for a document, or
/// <see cref="TryReadObject"/> for strictly valid TEXT.
///
/// <para><b>Three entry points, and the NAMES do not tell you which is which — this paragraph is the only
/// place that does.</b> <see cref="TryParseObject"/> and <see cref="TryReadObject"/> are LENIENT: they
/// tolerate the punctuation a model gets wrong, because repairing it in code is cheaper than a second call.
/// <see cref="IsValid"/> is STRICT, because its caller is grading whether the model got it right. The
/// surface is frozen (<b>D70</b>), so the postures cannot move into the names.</para>
///
/// <para><b>The scan tracks strings but not COMMENTS</b>, so a block comment containing <c>}</c> ends it
/// early and the fragment fails to parse. Leniency and extraction disagree there, and the direction is the
/// safe one — a caller retries rather than receiving a truncated object.</para></summary>
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

    /// <summary>What a READ of a model's own answer tolerates, and what grading it must not.
    ///
    /// <para><b>A trailing comma or a stray comment costs a second model call otherwise</b> — the repair
    /// round trip is fail-CLOSED where every other seam here is fail-open, spends a second
    /// usage-budget/rate-limit charge and never returns a cached hit (<c>docs/model-tasks.md</c> §1). Both
    /// are punctuation the reply already contains all the meaning for, so asking a model to restate it is
    /// paying inference for string surgery.</para>
    ///
    /// <para><b>Deliberately NOT applied by <see cref="IsValid"/></b>, which answers a different question —
    /// <c>StructureScorer</c> uses it to GRADE whether a model emitted well-formed JSON, and a grader that
    /// accepted a trailing comma would silently score malformed output as perfect.</para></summary>
    private static readonly JsonDocumentOptions Lenient =
        new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>Extract the first balanced JSON object from <paramref name="text"/> (via
    /// <see cref="ExtractObject"/>) AND parse it, tolerating the punctuation noted on <see cref="Lenient"/>.
    /// Returns false — with a null <paramref name="doc"/> — when nothing balanced is found or it doesn't
    /// parse. The caller OWNS the returned document and must dispose it (wrap in a <c>using</c>). This is the
    /// shared "tolerantly read a JSON object out of an LLM reply" primitive behind the judge/comparer verdict
    /// parsers.</summary>
    public static bool TryParseObject(string? text, [NotNullWhen(true)] out JsonDocument? doc)
    {
        doc = null;
        var json = ExtractObject(text);
        if (json is null) return false;
        try { doc = JsonDocument.Parse(json, Lenient); return true; }
        catch (JsonException) { return false; }
    }

    /// <summary>The first balanced JSON object in <paramref name="text"/>, as STRICTLY valid JSON — read
    /// leniently and re-serialized when, and only when, that was necessary.
    ///
    /// <para><b>Unchanged byte for byte when the object already parses strictly</b>, so a caller that was
    /// handed the model's own spacing keeps it; re-serializing unconditionally would reformat every reply to
    /// buy nothing.</para>
    ///
    /// <para><b>Re-serializing is what keeps leniency honest.</b> A caller's promise is usually that the text
    /// it returns parses — so handing back a raw trailing comma would break that for every consumer while
    /// saving the round trip, which is the worse trade.</para></summary>
    /// <param name="text">The model's reply, prose and code fences included.</param>
    /// <param name="json">Strictly valid JSON on success; null otherwise.</param>
    /// <returns>False when nothing balanced is present, or when what is there is malformed in a way
    /// punctuation cannot explain — a TRUNCATED object is the case that must still reach a retry, because it
    /// is missing content rather than commas.</returns>
    public static bool TryReadObject(string? text, [NotNullWhen(true)] out string? json)
    {
        json = ExtractObject(text);
        if (json is null) return false;
        if (IsValid(json)) return true;                 // already strict — hand back exactly what was sent

        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer)) doc.RootElement.WriteTo(writer);
            json = Encoding.UTF8.GetString(buffer.ToArray());
            return true;
        }
        catch (JsonException)
        {
            json = null;
            return false;
        }
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
