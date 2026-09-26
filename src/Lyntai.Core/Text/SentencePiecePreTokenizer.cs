using System.Text;

namespace Lyntai.Text;

/// <summary>Splits normalized text into the words a Unigram model segments one at a time: the
/// <c>WhitespaceSplit</c> and <c>Metaspace</c> steps a <c>tokenizer.json</c> declares, run in its order.
///
/// <para><c>WhitespaceSplit</c> cuts on Unicode <c>White_Space</c> and drops it. <c>Metaspace</c> writes each
/// space as the replacement (<c>▁</c>), prepends one to a word not already starting with it unless its scheme
/// is <c>never</c>, and — when it splits — cuts before every replacement, keeping it with what follows. No step
/// at all leaves the text one word.</para></summary>
internal sealed class SentencePiecePreTokenizer
{
    private readonly Func<string, IEnumerable<string>>[] _steps;

    private SentencePiecePreTokenizer(Func<string, IEnumerable<string>>[] steps) => _steps = steps;

    public static SentencePiecePreTokenizer None { get; } = new([]);

    public static Func<string, IEnumerable<string>> WhitespaceSplit => SplitOnWhitespace;

    public static Func<string, IEnumerable<string>> Metaspace(char replacement, bool prepend, bool split) =>
        word => MetaspaceStep(word, replacement, prepend, split);

    public static SentencePiecePreTokenizer Of(IEnumerable<Func<string, IEnumerable<string>>> steps) => new([.. steps]);

    /// <summary>The words of <paramref name="text"/>, never an empty one. An empty word reaches no step — so an
    /// empty text is no words, not a lone <c>▁</c>, as in the reference.</summary>
    public IEnumerable<string> Words(string text)
    {
        IEnumerable<string> words = [text];
        foreach (var step in _steps) words = words.Where(w => w.Length > 0).SelectMany(step);
        return words.Where(w => w.Length > 0);
    }

    private static IEnumerable<string> SplitOnWhitespace(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length;)
        {
            // a lone surrogate decodes as U+FFFD: not whitespace, so it stays in its word as written
            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed);
            if (Rune.IsWhiteSpace(rune))
            {
                if (start >= 0) yield return text[start..i];
                start = -1;
            }
            else if (start < 0)
            {
                start = i;
            }
            i += consumed;
        }
        if (start >= 0) yield return text[start..];
    }

    private static IEnumerable<string> MetaspaceStep(string word, char replacement, bool prepend, bool split)
    {
        var marked = word.Replace(' ', replacement);
        if (prepend && !marked.StartsWith(replacement)) marked = replacement + marked;
        if (!split)
        {
            yield return marked;
            yield break;
        }

        var start = 0;
        for (var i = 1; i < marked.Length; i++)
        {
            if (marked[i] != replacement) continue;
            yield return marked[start..i];
            start = i;
        }
        yield return marked[start..];
    }
}
