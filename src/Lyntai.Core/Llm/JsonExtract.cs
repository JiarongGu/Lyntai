namespace Lyntai.Llm;

/// <summary>Tolerant JSON extraction from LLM prose (design §6): strips code fences, finds the
/// first balanced <c>{…}</c> object. Parsing/validation stays with the caller.</summary>
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
}
