using System.Text.Json;

namespace Lyntai.Text;

/// <summary>The parts of a SentencePiece Unigram pipeline, read from a <c>tokenizer.json</c>.
///
/// <para><b>Only what is supported is read, and anything else is refused BY NAME</b> — normalizer
/// <c>Precompiled</c> or none; pre-tokenizer <c>WhitespaceSplit</c>, <c>Metaspace</c>, a <c>Sequence</c> of them,
/// or none; model <c>Unigram</c>; post-processor <c>TemplateProcessing</c>, <c>RobertaProcessing</c>,
/// <c>BertProcessing</c> or none. A tokenizer that approximated a rule it does not run would return finite,
/// plausible, wrong ids. The <c>truncation</c> and <c>padding</c> sections are ignored: the caller owns the
/// window.</para></summary>
internal static class TokenizerJson
{
    /// <summary>What a pipeline is made of. <see cref="Pieces"/> is the vocabulary in id order.</summary>
    internal sealed record Parts(
        PrecompiledCharsMap? Normalizer, SentencePiecePreTokenizer PreTokenizer, UnigramModel Model,
        SequenceTemplate Template, string[] Pieces);

    /// <exception cref="InvalidDataException">Not JSON, not a tokenizer.json's shape, or a component this does not
    /// run — named.</exception>
    public static Parts Read(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The tokenizer.json is not valid JSON: {e.Message}", e);
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException
                                      or IndexOutOfRangeException)
        {
            throw new InvalidDataException($"The tokenizer.json is not shaped as one: {e.Message}", e);
        }
    }

    private static Parts Read(JsonElement root)
    {
        var (model, pieces) = Model(root.GetProperty("model"), Specials(root));
        return new Parts(Normalizer(Section(root, "normalizer")), PreTokenizer(Section(root, "pre_tokenizer")),
            model, Template(Section(root, "post_processor")), pieces);
    }

    /// <summary>A section, or null when it is absent or JSON <c>null</c> — which the reference treats alike.</summary>
    private static JsonElement? Section(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static string TypeOf(JsonElement node) => node.GetProperty("type").GetString()!;

    /// <summary>The ids the added tokens mark <c>special</c> — never matched from text (<b>D191</b>).</summary>
    private static HashSet<int> Specials(JsonElement root)
    {
        var specials = new HashSet<int>();
        if (!root.TryGetProperty("added_tokens", out var added) || added.ValueKind != JsonValueKind.Array) return specials;
        foreach (var token in added.EnumerateArray())
            if (token.TryGetProperty("special", out var special) && special.ValueKind == JsonValueKind.True)
                specials.Add(token.GetProperty("id").GetInt32());
        return specials;
    }

    private static (UnigramModel Model, string[] Pieces) Model(JsonElement node, HashSet<int> specials)
    {
        var type = TypeOf(node);
        if (type != "Unigram")
            throw Unsupported($"a {type} model", "runs Unigram only"
                + (type == "WordPiece" ? " — load a WordPiece model with WordPieceTokenizer from its vocab.txt" : ""));
        if (node.TryGetProperty("byte_fallback", out var fallback) && fallback.ValueKind == JsonValueKind.True)
            throw Unsupported("byte_fallback", "has no byte pieces to fall back to");

        var vocabulary = node.GetProperty("vocab");
        var entries = new List<(string Piece, double Score)>(vocabulary.GetArrayLength());
        foreach (var entry in vocabulary.EnumerateArray())
            entries.Add((entry[0].GetString()!, entry[1].GetDouble()));

        // without one, a character no piece covers has no id, and the text would silently vanish
        if (!node.TryGetProperty("unk_id", out var unknown) || unknown.ValueKind != JsonValueKind.Number
            || !unknown.TryGetInt32(out var unknownId) || unknownId < 0 || unknownId >= entries.Count)
            throw new InvalidDataException(
                "The tokenizer.json's Unigram model has no usable unk_id, so a character no piece covers would vanish.");

        return (new UnigramModel(entries, unknownId, specials), [.. entries.Select(e => e.Piece)]);
    }

    private static PrecompiledCharsMap? Normalizer(JsonElement? node)
    {
        if (node is not { } normalizer) return null;
        var type = TypeOf(normalizer);
        if (type != "Precompiled") throw Unsupported($"a {type} normalizer", "runs Precompiled (SentencePiece's own) or none");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(normalizer.GetProperty("precompiled_charsmap").GetString()!);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException("The tokenizer.json's precompiled_charsmap is not base64.", e);
        }
        return PrecompiledCharsMap.Parse(blob);
    }

    private static SentencePiecePreTokenizer PreTokenizer(JsonElement? node) =>
        node is { } pre ? SentencePiecePreTokenizer.Of(Steps(pre)) : SentencePiecePreTokenizer.None;

    private static IEnumerable<Func<string, IEnumerable<string>>> Steps(JsonElement node)
    {
        switch (TypeOf(node))
        {
            case "Sequence":
                return node.GetProperty("pretokenizers").EnumerateArray().SelectMany(Steps).ToList();
            case "WhitespaceSplit":
                return [SentencePiecePreTokenizer.WhitespaceSplit];
            case "Metaspace":
                return [Metaspace(node)];
            default:
                throw Unsupported($"a {TypeOf(node)} pre-tokenizer", "runs WhitespaceSplit and Metaspace, or none");
        }
    }

    /// <summary>The reference's reading: <c>prepend_scheme</c> defaults to <c>always</c>, a legacy
    /// <c>add_prefix_space: false</c> is valid only beside <c>never</c>, and <c>split</c> defaults to true.</summary>
    private static Func<string, IEnumerable<string>> Metaspace(JsonElement node)
    {
        var replacement = node.GetProperty("replacement").GetString()!;
        if (replacement.Length != 1) throw Unsupported($"a Metaspace replacement '{replacement}'", "needs one UTF-16 character");

        var scheme = node.TryGetProperty("prepend_scheme", out var declared) ? declared.GetString()! : "always";
        if (scheme is not ("always" or "never"))
            throw Unsupported($"Metaspace prepend_scheme '{scheme}'", "runs 'always' and 'never'");
        if (node.TryGetProperty("add_prefix_space", out var legacy) && legacy.ValueKind == JsonValueKind.False && scheme != "never")
            throw Unsupported("Metaspace add_prefix_space: false beside a prepend_scheme other than 'never'",
                "reads the pair as the reference does, which refuses it");

        var split = !node.TryGetProperty("split", out var splits) || splits.ValueKind != JsonValueKind.False;
        return SentencePiecePreTokenizer.Metaspace(replacement[0], scheme == "always", split);
    }

    private static SequenceTemplate Template(JsonElement? node)
    {
        if (node is not { } post) return SequenceTemplate.None;
        return TypeOf(post) switch
        {
            "TemplateProcessing" => SequenceTemplate.FromTemplateProcessing(post),
            "RobertaProcessing" => SequenceTemplate.Roberta(IdOf(post, "cls"), IdOf(post, "sep")),
            "BertProcessing" => SequenceTemplate.Bert(IdOf(post, "cls"), IdOf(post, "sep")),
            var type => throw Unsupported($"a {type} post-processor",
                "runs TemplateProcessing, RobertaProcessing and BertProcessing, or none"),
        };
    }

    /// <summary>The id half of a <c>["&lt;s&gt;", 0]</c> pair.</summary>
    private static int IdOf(JsonElement node, string name) => node.GetProperty(name)[1].GetInt32();

    private static InvalidDataException Unsupported(string what, string instead) =>
        new($"The tokenizer.json declares {what}; SentencePieceTokenizer {instead}.");
}
