namespace Lyntai.Storage;

/// <summary>
/// How one writing system has to be turned into search terms — <b>the seam every language-dependent
/// decision in <see cref="SearchTerms"/> goes through, so there is exactly one place to look and exactly one
/// place to change.</b>
///
/// <para><b>A profile rather than a boolean, because two questions are involved</b>
/// (<c>docs/DECISIONS.md</c> D55): whether whitespace already separates this script's words, and how long an
/// n-gram must be before it is SELECTIVE here. Neither answers the other, which is why Hangul writes spaces
/// and is expanded anyway.</para>
///
/// <para><b>Changing a gram length needs NO migration, which is what makes this seam worth having.</b> The
/// FTS5 <c>trigram</c> tokenizer indexes three-character sequences, but a QUERY may be longer and is matched
/// as consecutive trigrams — so a script whose 3-grams collide can be queried with 4-grams against the same
/// index. The index's floor is a floor on the INDEX, never a ceiling on the query.</para>
///
/// <para><b>What this is not:</b> not a language detector and not a segmenter. It answers "what shape of
/// term does this script need", a stable property of the writing system; finding word boundaries needs a
/// morphological analyser, which would plug in beside this rather than replace it.</para>
/// </summary>
/// <param name="Name">Human-readable, for diagnostics and for <see cref="SearchTerms.Profiles"/>.</param>
/// <param name="ExpandsIntoGrams">Whether tokens of this script are expanded into character n-grams instead
/// of matched whole. False for space-separated alphabetic scripts, where the whole word is more precise.</param>
/// <param name="IndexGramLength">The n-gram length used for terms that must survive a full-text INDEX. Never
/// below <see cref="SearchTerms.MinimumTermLength"/> — the index cannot match shorter — but deliberately
/// allowed above it, which is the whole point.</param>
/// <param name="SubstringGramLength">The n-gram length a SUBSTRING scan may additionally use. Lower than
/// <paramref name="IndexGramLength"/> because <c>LIKE</c>/<c>ILIKE</c> has no index-imposed minimum, and
/// necessary because most Chinese content words are exactly two characters.</param>
public sealed record ScriptProfile(
    string Name,
    bool ExpandsIntoGrams,
    int IndexGramLength,
    int SubstringGramLength)
{
    /// <summary>Anything whose words whitespace already separates — Latin, Cyrillic, Greek, digits. Matched
    /// as whole words, never expanded.</summary>
    public static ScriptProfile Spaced { get; } = new("spaced", false, SearchTerms.MinimumTermLength, 0);

    /// <summary>Han ideographs (Chinese, and the kanji in Japanese). Three characters identify a phrase
    /// well: the inventory is in the thousands, so a 3-gram is already selective. Two-character words are
    /// the norm (配偶, 客户), which is what <see cref="SubstringGramLength"/> is for.</summary>
    public static ScriptProfile Han { get; } = new("han", true, 3, 2);

    /// <summary>Hiragana and katakana. <b>The measured problem case</b>: the inventory is roughly fifty
    /// characters per syllabary, so a three-kana sequence recurs across unrelated sentences by chance far
    /// more readily than three Han characters do, and a Japanese query drags in material that then displaces
    /// the relevant entries under the recall limit.
    /// <para>The gram length here is the knob that answers it, and it is deliberately <b>not</b> tuned by
    /// assumption — see <c>TASKS.md</c>. Raising it costs no migration (a longer query phrase still matches a
    /// trigram index) and must be MEASURED, because a longer gram is more selective and also matches less:
    /// the failure mode of over-correcting is a miss rate that rises for the opposite reason.</para></summary>
    public static ScriptProfile Kana { get; } = new("kana", true, 3, 2);

    /// <summary>Lao. Spaceless, no case — the same shape as <see cref="Thai"/>, whose remarks apply
    /// unchanged including the unmeasured caveat.</summary>
    public static ScriptProfile Lao { get; } = new("lao", true, 3, 2);

    /// <summary>Khmer. Spaceless (spaces separate phrases, not words), no case — same shape as
    /// <see cref="Thai"/>.</summary>
    public static ScriptProfile Khmer { get; } = new("khmer", true, 3, 2);

    /// <summary>Burmese and the other Myanmar-script languages. Spaceless, no case — same shape as
    /// <see cref="Thai"/>.</summary>
    public static ScriptProfile Myanmar { get; } = new("myanmar", true, 3, 2);

    /// <summary>Tibetan. Written without word spaces — the tsheg (U+0F0B) separates SYLLABLES, not words, so
    /// whitespace splitting separates nothing here either. Same shape as <see cref="Thai"/>.</summary>
    public static ScriptProfile Tibetan { get; } = new("tibetan", true, 3, 2);

    /// <summary>Thai. Spaceless like Han, no case, and its words are frequently two or three characters, so
    /// it takes the same shape.
    /// <para><b>Unmeasured, and that is said rather than glossed.</b> There is no Thai corpus, so this
    /// claims only that Thai tokenizes like a spaceless script instead of like one long word — a strict
    /// improvement over matching nothing, and no more than that.</para></summary>
    public static ScriptProfile Thai { get; } = new("thai", true, 3, 2);

    /// <summary>Hangul. Korean WRITES SPACES and is expanded anyway, because the deciding
    /// question here is morphological: particles attach to the stem, so whole-token
    /// matching misses 배우자 whenever it appears as 배우자는.</summary>
    public static ScriptProfile Hangul { get; } = new("hangul", true, 3, 2);
}
