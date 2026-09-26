namespace Lyntai.Text;

/// <summary>SentencePiece Unigram tokenization — text to vocabulary ids — read from a model's
/// <c>tokenizer.json</c>, owned rather than depended on (<c>docs/DECISIONS.md</c> <b>D191</b>). It is what the
/// XLM-R family of multilingual embedders and rerankers ships.
///
/// <para><b>It runs the pipeline the file declares, as HF <c>tokenizers</c> runs it</b>: SentencePiece's
/// precompiled normalizer, <c>WhitespaceSplit</c> and <c>Metaspace</c>, best-scoring Unigram segmentation, and
/// the post-processor's layout of special tokens. Any other component — a BPE model, a byte-level
/// pre-tokenizer, byte fallback — is refused at load, naming it, never approximated.
/// <c>SentencePieceTokenizerTests</c> asserts id-for-id equality with HF <c>tokenizers</c> and C++
/// <c>sentencepiece</c> on a committed fixture; that test is the contract.</para>
///
/// <para><b>Special tokens typed in text stay TEXT.</b> <c>&lt;s&gt;</c> or <c>&lt;mask&gt;</c> in a document is
/// its characters, as C++ SentencePiece and <see cref="WordPieceTokenizer"/> treat it, so content cannot change
/// the structure of the sequence the model sees. HF parses them as control tokens; this is the one place the two
/// part, deliberately.</para></summary>
public sealed class SentencePieceTokenizer
{
    private readonly PrecompiledCharsMap? _normalizer;
    private readonly SentencePiecePreTokenizer _preTokenizer;
    private readonly UnigramModel _model;
    private readonly SequenceTemplate _template;

    private SentencePieceTokenizer(TokenizerJson.Parts parts)
    {
        _normalizer = parts.Normalizer;
        _preTokenizer = parts.PreTokenizer;
        _model = parts.Model;
        _template = parts.Template;
        Pieces = Array.AsReadOnly(parts.Pieces);
    }

    /// <summary>The piece each id stands for, in id order — the model's whole vocabulary.</summary>
    public IReadOnlyList<string> Pieces { get; }

    /// <summary>Load the <c>tokenizer.json</c> in a model directory.</summary>
    /// <param name="directory">A directory holding <c>tokenizer.json</c>.</param>
    /// <exception cref="FileNotFoundException">No <c>tokenizer.json</c> in the directory.</exception>
    /// <exception cref="InvalidDataException">The file is not a SentencePiece Unigram pipeline this runs.</exception>
    public static SentencePieceTokenizer FromModelDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.Combine(directory, "tokenizer.json");
        if (!File.Exists(path)) throw new FileNotFoundException($"'tokenizer.json' is missing from '{directory}'.", path);

        using var stream = File.OpenRead(path);
        return FromTokenizerJson(stream);
    }

    /// <summary>Load a <c>tokenizer.json</c> from a stream, which is read to its end and left open.</summary>
    /// <exception cref="InvalidDataException">Not JSON, not a tokenizer.json's shape, or a component this does not
    /// run — the message names it.</exception>
    public static SentencePieceTokenizer FromTokenizerJson(Stream json) => new(TokenizerJson.Read(json));

    /// <summary>The ids of <paramref name="text"/>'s content tokens, in order. Never null; empty for text with
    /// no usable token.</summary>
    public IReadOnlyList<int> EncodeToIds(string text)
    {
        var ids = new List<int>();
        if (string.IsNullOrEmpty(text)) return ids;

        var normalized = _normalizer?.Normalize(text) ?? text;
        foreach (var word in _preTokenizer.Words(normalized)) _model.Segment(word, ids);
        return ids;
    }

    /// <summary>Encode one text for a transformer, in the model's layout of special tokens.
    /// <b><paramref name="maxTokens"/> counts the special tokens</b>; longer content is cut silently.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No room for one content token beside the specials.</exception>
    public TokenEncoding Encode(string text, int maxTokens = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, _template.SingleOverhead + 1);
        return Frame(Prefix(EncodeToIds(text ?? string.Empty), maxTokens - _template.SingleOverhead));
    }

    /// <summary>Encode a PAIR — a query and a document — in the model's pair layout. The budget is spent on
    /// <paramref name="b"/> first: <paramref name="a"/>, the query, is shortened only when it cannot fit alone,
    /// as <see cref="WordPieceTokenizer.Encode(string,string,int)"/> does.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No room for one content token beside the specials.</exception>
    public TokenEncoding Encode(string a, string b, int maxTokens = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, _template.PairOverhead + 1);

        var first = EncodeToIds(a ?? string.Empty);
        var second = EncodeToIds(b ?? string.Empty);
        var budget = maxTokens - _template.PairOverhead;
        var keptA = Math.Min(first.Count, budget);
        var keptB = Math.Min(second.Count, budget - keptA);
        return Frame(Prefix(first, keptA), Prefix(second, keptB));
    }

    /// <summary>Frame content ids that are already tokenized in the model's special tokens. <b>No
    /// truncation</b>: the caller has already fit the ids to its window.</summary>
    public TokenEncoding Frame(IReadOnlyList<int> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return _template.Frame(content);
    }

    /// <summary>Frame a pair of already-tokenized sides in the model's pair layout. <b>No truncation</b>.</summary>
    public TokenEncoding Frame(IReadOnlyList<int> first, IReadOnlyList<int> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return _template.Frame(first, second);
    }

    private static IReadOnlyList<int> Prefix(IReadOnlyList<int> ids, int count) =>
        count >= ids.Count ? ids : [.. ids.Take(count)];
}
