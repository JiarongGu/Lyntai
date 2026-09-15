using System.Globalization;
using System.Text;

namespace Lyntai.Text;

/// <summary>One text encoded for a transformer — the three tensors a BERT-family graph takes, in the order
/// it takes them.
///
/// <para><b>All three are load-bearing and none fails loudly.</b> A missing mask attends to padding, a
/// missing segment id costs a cross-encoder the signal that tells its query from its document, and either
/// returns a plausible, wrong vector.</para></summary>
/// <param name="Ids">Vocabulary ids, bracketed by the classification and separator tokens.</param>
/// <param name="AttentionMask">1 for a real token, 0 for padding. All ones until a batch adds padding.</param>
/// <param name="TokenTypeIds">Which segment each token belongs to; all zero for a single text.</param>
public readonly record struct WordPieceEncoding(int[] Ids, int[] AttentionMask, int[] TokenTypeIds);

/// <summary>BERT's WordPiece tokenizer — text to vocabulary ids, owned rather than depended on.
///
/// <para><b>Why the library owns one.</b> Buying this from <c>Microsoft.ML.Tokenizers</c> costs 812 KB of
/// closure (it drags <c>Google.Protobuf</c> for SentencePiece models nothing here loads) and would put a
/// third-party dependency in Core, which may take none (<b>D122</b>).</para>
///
/// <para><b>It reproduces the reference pipeline, not an approximation of it</b>: clean, pad CJK, split on
/// whitespace, lowercase, strip accents, split on punctuation, then greedy longest-match-first with
/// <c>##</c> continuations. <c>WordPieceTokenizerTests</c> asserts id-for-id equality against the
/// implementation this replaced — that test is the contract, the rules below are how it is met.</para>
///
/// <para><b>CONTENT tokens only</b> — no <c>[CLS]</c>/<c>[SEP]</c>, because a <c>model2vec</c> table is a
/// mean over the rows its content tokens select and bracketing would shift every vector. A transformer
/// that needs them must add them itself; that is a deliberate omission, not an oversight.</para></summary>
public sealed class WordPieceTokenizer
{
    /// <summary>BERT's own <c>max_input_chars_per_word</c>. The cap is load-bearing, not cosmetic: longest
    /// match over a pathological token is quadratic in its length.</summary>
    private const int MaxCharactersPerWord = 100;

    private const string ContinuationPrefix = "##";

    private readonly Dictionary<string, int> _vocabulary;
    private readonly int _unknownId;
    private readonly int? _classificationId;
    private readonly int? _separatorId;
    private readonly bool _lowercase;
    private readonly bool _stripAccents;
    private readonly bool _tokenizeChineseCharacters;

    private WordPieceTokenizer(
        Dictionary<string, int> vocabulary, int unknownId, int? classificationId, int? separatorId,
        bool lowercase, bool stripAccents, bool tokenizeChineseCharacters)
    {
        _vocabulary = vocabulary;
        _unknownId = unknownId;
        _classificationId = classificationId;
        _separatorId = separatorId;
        _lowercase = lowercase;
        _stripAccents = stripAccents;
        _tokenizeChineseCharacters = tokenizeChineseCharacters;
    }

    /// <summary>Build from a <c>vocab.txt</c>'s lines, one token per line, id = line number.</summary>
    /// <param name="vocabulary">The lines, in file order.</param>
    /// <param name="unknownToken">The token a word that cannot be pieced falls back to.</param>
    /// <param name="lowercase">Lowercase before matching (<c>do_lower_case</c>).</param>
    /// <param name="stripAccents">Drop combining marks (<c>strip_accents</c>; null follows
    /// <paramref name="lowercase"/>, which is what the reference does).</param>
    /// <param name="tokenizeChineseCharacters">Give each CJK character its own token.</param>
    /// <param name="classificationToken">Opens a sequence in <see cref="Encode(string,int)"/> (<c>cls_token</c>).</param>
    /// <param name="separatorToken">Closes a sequence in <see cref="Encode(string,int)"/> (<c>sep_token</c>).</param>
    /// <exception cref="InvalidDataException"><paramref name="unknownToken"/> is not in the vocabulary —
    /// without it an unmatchable word has no id to fall back to, and every such word would vanish.</exception>
    public static WordPieceTokenizer FromVocabulary(
        IReadOnlyList<string> vocabulary, string unknownToken = "[UNK]",
        bool lowercase = true, bool? stripAccents = null, bool tokenizeChineseCharacters = true,
        string classificationToken = "[CLS]", string separatorToken = "[SEP]")
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        // Last occurrence wins, which is what building the map by assignment in file order does.
        var map = new Dictionary<string, int>(vocabulary.Count, StringComparer.Ordinal);
        for (var i = 0; i < vocabulary.Count; i++) map[vocabulary[i]] = i;

        if (!map.TryGetValue(unknownToken, out var unknownId))
            throw new InvalidDataException(
                $"The vocabulary has no '{unknownToken}' row. A WordPiece vocabulary must carry one — "
                + "without it a word that cannot be pieced has no id, and the text silently disappears.");

        // NULLABLE rather than required: a model2vec vocabulary is pruned and may carry neither token, and
        // that path never calls Encode. Failing to load over a token the caller will not use would refuse a
        // perfectly good model; Encode itself says what is missing when it is actually needed.
        int? classificationId = map.TryGetValue(classificationToken, out var cls) ? cls : null;
        int? separatorId = map.TryGetValue(separatorToken, out var sep) ? sep : null;

        return new WordPieceTokenizer(
            map, unknownId, classificationId, separatorId,
            lowercase, stripAccents ?? lowercase, tokenizeChineseCharacters);
    }

    /// <summary>Load from a model directory: <c>vocab.txt</c> for the rows, and <c>tokenizer_config.json</c>
    /// — when the model ships one — for the rules it declares.
    ///
    /// <para><b>Prefer this to <see cref="FromVocabulary"/> with hand-picked flags.</b> The ids a model was
    /// built against depend on those rules, so a tokenizer configured by guess returns finite, plausible,
    /// wrong vectors; reading them from the model is the only way that cannot drift.</para></summary>
    /// <param name="directory">A directory holding at least <c>vocab.txt</c>.</param>
    /// <exception cref="FileNotFoundException">No <c>vocab.txt</c> in the directory.</exception>
    public static WordPieceTokenizer FromModelDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var vocabulary = Path.Combine(directory, "vocab.txt");
        if (!File.Exists(vocabulary))
            throw new FileNotFoundException(
                $"'vocab.txt' is missing from '{directory}'.", vocabulary);

        var rules = TokenizerRules.FromDirectory(directory);
        return FromVocabulary(
            File.ReadAllLines(vocabulary), rules.UnknownToken,
            rules.Lowercase, rules.StripAccents, rules.TokenizeChineseCharacters,
            rules.ClassificationToken, rules.SeparatorToken);
    }

    /// <summary>The ids of <paramref name="text"/>'s content tokens, in order. Never null; empty for text
    /// with no usable token.</summary>
    public IReadOnlyList<int> EncodeToIds(string text)
    {
        var ids = new List<int>();
        if (string.IsNullOrEmpty(text)) return ids;

        foreach (var word in Words(text)) AppendPieces(word, ids);
        return ids;
    }

    /// <summary>Encode one text for a TRANSFORMER: <c>[CLS]</c> + content + <c>[SEP]</c>, with the
    /// attention mask and segment ids the graph takes alongside the ids.
    ///
    /// <para><b>Separate from <see cref="EncodeToIds"/> on purpose.</b> A lookup-table model must get
    /// content tokens ONLY — bracketing it folds two rows into every mean — so the two callers genuinely
    /// want different answers and neither default is safe for the other.</para>
    ///
    /// <para><b><paramref name="maxTokens"/> counts the special tokens</b>, so a 512-position model takes
    /// 510 content tokens. Truncation is silent, as every BERT pipeline's is; the caller decides whether a
    /// truncated document is acceptable.</para></summary>
    /// <param name="text">The text. Empty yields the bare <c>[CLS] [SEP]</c> pair, which is what BERT does
    /// and keeps a batch containing a blank document valid.</param>
    /// <param name="maxTokens">Total sequence length INCLUDING both special tokens.</param>
    /// <exception cref="ArgumentOutOfRangeException">Under 3 — no room for content.</exception>
    /// <exception cref="InvalidOperationException">The vocabulary has no classification or separator
    /// token, so this model cannot be encoded for a transformer at all.</exception>
    public WordPieceEncoding Encode(string text, int maxTokens = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 3);
        if (_classificationId is not { } cls || _separatorId is not { } sep)
            throw new InvalidOperationException(
                "This vocabulary carries no classification/separator token, so it cannot be encoded for a "
                + "transformer. A model2vec table is the usual reason — use EncodeToIds for that class.");

        var content = EncodeToIds(text ?? string.Empty);
        var kept = Math.Min(content.Count, maxTokens - 2);

        var ids = new int[kept + 2];
        ids[0] = cls;
        for (var i = 0; i < kept; i++) ids[i + 1] = content[i];
        ids[^1] = sep;

        var mask = new int[ids.Length];
        Array.Fill(mask, 1);
        return new WordPieceEncoding(ids, mask, new int[ids.Length]);
    }

    /// <summary>Encode a PAIR as <c>[CLS] a [SEP] b [SEP]</c> — the shape a cross-encoder scores, where the
    /// whole signal is that the two sides are distinguishable.
    ///
    /// <para><b><see cref="WordPieceEncoding.TokenTypeIds"/> is the point of this overload.</b> Segment 0
    /// covers <c>[CLS] a [SEP]</c> and segment 1 covers <c>b [SEP]</c>, which is what a cross-encoder's
    /// segment embedding reads to tell a query from a document. A runtime that zeroes them scores the pair
    /// as one undifferentiated string, which is how a reranker can return well-formed numbers in the WRONG
    /// order (<c>docs/memory-measurements.md</c> §5).</para>
    ///
    /// <para><b>The BUDGET is spent on the second text, deliberately.</b> Truncation takes from
    /// <paramref name="b"/> first and only shortens <paramref name="a"/> if <paramref name="a"/> alone
    /// cannot fit — because a is the QUERY and b the document: losing the tail of a long document costs
    /// some evidence, while losing the tail of the query changes the question being asked. The reference
    /// implementation offers longest-first truncation as a default; this is the asymmetric rule a reranker
    /// actually wants, and it is stated rather than inherited.</para></summary>
    /// <param name="a">The first segment — the QUERY, for a reranker. Truncated last.</param>
    /// <param name="b">The second segment — the DOCUMENT. Truncated first.</param>
    /// <param name="maxTokens">Total sequence length INCLUDING all three special tokens, so a 512-position
    /// model takes 509 content tokens across both sides.</param>
    /// <exception cref="ArgumentOutOfRangeException">Under 4 — no room for content on either side.</exception>
    /// <exception cref="InvalidOperationException">The vocabulary has no classification or separator token.</exception>
    public WordPieceEncoding Encode(string a, string b, int maxTokens = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 4);
        if (_classificationId is not { } cls || _separatorId is not { } sep)
            throw new InvalidOperationException(
                "This vocabulary carries no classification/separator token, so it cannot be encoded for a "
                + "transformer. A model2vec table is the usual reason — use EncodeToIds for that class.");

        var first = EncodeToIds(a ?? string.Empty);
        var second = EncodeToIds(b ?? string.Empty);
        var budget = maxTokens - 3;                       // [CLS] … [SEP] … [SEP]

        // b yields first; a is shortened only when it cannot fit on its own.
        var keptA = Math.Min(first.Count, budget);
        var keptB = Math.Min(second.Count, budget - keptA);

        var ids = new int[keptA + keptB + 3];
        var types = new int[ids.Length];
        var at = 0;
        ids[at++] = cls;
        for (var i = 0; i < keptA; i++) ids[at++] = first[i];
        ids[at++] = sep;
        var secondStarts = at;                            // segment 1 begins AFTER the first separator
        for (var i = 0; i < keptB; i++) ids[at++] = second[i];
        ids[at] = sep;
        for (var i = secondStarts; i < ids.Length; i++) types[i] = 1;

        var mask = new int[ids.Length];
        Array.Fill(mask, 1);
        return new WordPieceEncoding(ids, mask, types);
    }

    /// <summary>Clean, pad CJK, split on whitespace, normalize each word, then split off punctuation —
    /// the reference's basic-tokenizer half, yielding the words WordPiece then pieces.</summary>
    private IEnumerable<Rune[]> Words(string text)
    {
        var cleaned = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == 0 || rune.Value == 0xFFFD || IsControl(rune)) continue;
            if (IsWhitespace(rune)) { cleaned.Append(' '); continue; }

            // A CJK character is its own word, so it is padded rather than left to join its neighbours.
            if (_tokenizeChineseCharacters && IsChineseCharacter(rune.Value))
                cleaned.Append(' ').Append(rune.ToString()).Append(' ');
            else
                cleaned.Append(rune.ToString());
        }

        foreach (var word in cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = _lowercase ? word.ToLowerInvariant() : word;
            if (_stripAccents) normalized = StripAccents(normalized);

            foreach (var piece in SplitOnPunctuation(normalized)) yield return piece;
        }
    }

    /// <summary>Greedy longest-match-first over one word. <b>All or nothing</b>: if any piece fails to
    /// match, the WHOLE word becomes <c>[UNK]</c> rather than its matched prefix — emitting the prefix
    /// would silently change what the vector means.</summary>
    private void AppendPieces(Rune[] word, List<int> ids)
    {
        if (word.Length == 0) return;
        if (word.Length > MaxCharactersPerWord) { ids.Add(_unknownId); return; }

        var matched = new List<int>();
        var start = 0;
        while (start < word.Length)
        {
            var end = word.Length;
            var found = -1;
            while (start < end)
            {
                var candidate = Text(word, start, end);
                if (start > 0) candidate = ContinuationPrefix + candidate;
                if (_vocabulary.TryGetValue(candidate, out var id)) { found = id; break; }
                end--;
            }

            if (found < 0) { ids.Add(_unknownId); return; }
            matched.Add(found);
            start = end;
        }

        ids.AddRange(matched);
    }

    private static string Text(Rune[] word, int start, int end)
    {
        var builder = new StringBuilder(end - start);
        for (var i = start; i < end; i++) builder.Append(word[i].ToString());
        return builder.ToString();
    }

    /// <summary>Each punctuation character becomes its own word, so <c>"alpha,"</c> cannot match a
    /// vocabulary row that <c>"alpha"</c> would.</summary>
    private static List<Rune[]> SplitOnPunctuation(string word)
    {
        var pieces = new List<Rune[]>();
        var current = new List<Rune>();
        foreach (var rune in word.EnumerateRunes())
        {
            if (IsPunctuation(rune))
            {
                if (current.Count > 0) { pieces.Add([.. current]); current.Clear(); }
                pieces.Add([rune]);
                continue;
            }

            current.Add(rune);
        }

        if (current.Count > 0) pieces.Add([.. current]);
        return pieces;
    }

    private static string StripAccents(string word)
    {
        var decomposed = word.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString();
    }

    private static bool IsWhitespace(Rune rune) =>
        rune.Value is ' ' or '\t' or '\n' or '\r'
        || Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;

    /// <summary>Tab, newline and carriage return are categorized as control characters and are deliberately
    /// treated as WHITESPACE instead — they separate words rather than being dropped.</summary>
    private static bool IsControl(Rune rune)
    {
        if (rune.Value is '\t' or '\n' or '\r') return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned;
    }

    /// <summary>The reference treats every ASCII non-alphanumeric as punctuation, which is WIDER than the
    /// Unicode punctuation categories — <c>$</c>, <c>+</c> and <c>`</c> are symbols to Unicode and splits
    /// here.</summary>
    private static bool IsPunctuation(Rune rune)
    {
        var cp = rune.Value;
        if (cp is (>= 33 and <= 47) or (>= 58 and <= 64) or (>= 91 and <= 96) or (>= 123 and <= 126))
            return true;

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
    }

    /// <summary>The CJK blocks the reference enumerates. Deliberately NOT all of "Chinese": Hiragana,
    /// Katakana and Hangul are excluded there too, because they form words the way Latin script does.</summary>
    private static bool IsChineseCharacter(int cp) =>
        cp is (>= 0x4E00 and <= 0x9FFF) or (>= 0x3400 and <= 0x4DBF) or (>= 0xF900 and <= 0xFAFF)
        or (>= 0x20000 and <= 0x2A6DF) or (>= 0x2A700 and <= 0x2B73F) or (>= 0x2B740 and <= 0x2B81F)
        or (>= 0x2B820 and <= 0x2CEAF) or (>= 0x2F800 and <= 0x2FA1F);
}
