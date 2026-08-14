namespace Lyntai.Storage;

/// <summary>
/// Splits raw user text into the terms a keyword search matches on — the ONE tokenization every backend
/// shares, so that "does this recall find the entry" has the same answer on SQLite, Postgres and InMemory.
///
/// <para><b>Why this exists (2026-08-12).</b> Only SQLite's FTS path ever split a query: it OR-ed the
/// words, so <c>"deploy pipeline"</c> matched an entry containing either. Every other path —
/// both LIKE fallbacks, all three Postgres queries, both InMemory stores — matched the WHOLE query as one
/// contiguous substring, so the same recall found nothing unless the entry contained that exact phrase.
/// The same <see cref="Lyntai.Memory.IMemoryGraphStore"/> contract therefore had near-zero keyword seeding on one backend
/// and working seeding on another, which is not a "backend-specific ordering" difference; it is a different
/// answer to whether the fact is found at all.</para>
///
/// <para><b>How a token is analysed is decided by its SCRIPT, through <see cref="ScriptProfile"/>.</b> A run
/// in a script that writes no spaces becomes character n-grams, because splitting on whitespace hands back
/// the entire sentence as one token and a whole-sentence term matches almost nothing. N-grams are the right
/// unit because they are what SQLite's index already stores (<c>tokenize='trigram'</c>) and what Postgres's
/// <c>pg_trgm</c> GIN index already accelerates: both backends were built on them, and only the QUERY side
/// was English-shaped. No configuration, no per-language setup, no consumer needing to know.</para>
///
/// <para><b>An ASCII word is NOT expanded</b>, deliberately: a whole word is more precise (<c>"cat"</c>
/// would otherwise match <c>"concatenate"</c>), and whitespace already separated it.</para>
///
/// <para><b>The floor is <see cref="MinimumTermLength"/> characters</b>, because that is what a trigram index
/// can match. A query below it yields no terms at all and callers fall back to a whole-query substring scan,
/// which is exactly right for a two-character CJK word. The floor is a FLOOR, not a ceiling — a script whose
/// 3-grams are not selective can ask for longer ones through its profile.</para>
/// </summary>
public static class SearchTerms
{
    /// <summary>The shortest term a full-text index can match. Three, because both backends index trigrams.
    /// A <see cref="ScriptProfile"/> may ask for MORE (see <see cref="ScriptProfile.IndexGramLength"/>);
    /// none may ask for less.</summary>
    public const int MinimumTermLength = 3;

    /// <summary>The most terms one expanded token contributes. A cap rather than an unbounded expansion:
    /// the input is raw user text and the output sizes a SQL expression. Comfortably above any real query,
    /// and a truncated tail still leaves every earlier n-gram matching.</summary>
    public const int MaxTermsPerToken = 32;

    /// <summary>The terms <paramref name="raw"/> should match on, in order, de-duplicated
    /// (case-insensitively). Empty when nothing reaches the floor — the caller's signal to fall back to a
    /// whole-query substring scan.</summary>
    public static IReadOnlyList<string> Extract(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in raw.Split((char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var (run, profile) in ScriptRuns(token))
                if (profile.ExpandsIntoGrams) AddGrams(run, profile.IndexGramLength, terms, seen);
                else if (run.Length >= MinimumTermLength && seen.Add(run)) terms.Add(run);
        }

        return terms;
    }

    /// <summary>
    /// Splits one whitespace token into maximal runs of a single script, each carrying its own profile.
    ///
    /// <para><b>Why (2026-08-13).</b> CJK text embeds Latin without spaces around it constantly —
    /// <c>我今天deploy了</c>, <c>部署key</c>. Sliding one window across the whole token shreds the Latin word
    /// into fragments that are words in no language (<c>dep</c>, <c>epl</c>, <c>plo</c>) while never emitting
    /// <c>deploy</c> itself, and those fragments then match arbitrary unrelated text. Segmenting first means
    /// each run is analysed under the rules that fit it: the Han run is expanded, the Latin word is kept
    /// whole.</para>
    ///
    /// <para><b>Digits and punctuation are NEUTRAL and never split a run</b>, which is the refinement that
    /// makes this safe rather than merely tidy. Treating them as their own script would break
    /// <c>第3轮</c> and <c>重复0</c> into single characters and yield NO terms at all — a regression on
    /// perfectly ordinary CJK. They attach to the run in progress, so <c>attribute0</c> stays one Latin token
    /// and <c>重复0</c> stays one Han run.</para>
    ///
    /// <para>This also removes the need for a "most demanding script wins" rule on mixed kanji/kana tokens:
    /// the two are now separate runs and each gets its own treatment, which is strictly better than picking
    /// one set of rules for both. For Japanese it is additionally a cheap approximation of word segmentation
    /// — kanji runs carry the content words, kana runs the grammar.</para>
    /// </summary>
    private static List<(string Run, ScriptProfile Profile)> ScriptRuns(string token)
    {
        var runs = new List<(string, ScriptProfile)>();
        var start = 0;
        ScriptProfile? current = null;

        for (var i = 0; i < token.Length; i++)
        {
            var profile = ProfileOfChar(token[i]);
            if (profile is null) continue;            // neutral — stays in the run in progress
            if (current is null) { current = profile; continue; }
            if (profile == current) continue;

            runs.Add((token[start..i], current));
            start = i;
            current = profile;
        }

        runs.Add((token[start..], current ?? ScriptProfile.Spaced));
        return runs;
    }

    /// <summary>The profile of one character, or null when the character is script-NEUTRAL (a digit, a mark,
    /// punctuation) and must not start or end a run.</summary>
    private static ScriptProfile? ProfileOfChar(char c)
    {
        foreach (var (profile, matches) in Registry)
            if (matches(c))
                return profile;
        return char.IsLetter(c) ? ScriptProfile.Spaced : null;
    }

    /// <summary>Sliding n-grams of one script RUN, de-duplicated and capped.
    /// <para>No boundary check is needed any more: a run is a single script by construction, so a gram
    /// cannot straddle into another one. That check existed only because grams were taken across a whole
    /// mixed token — see <see cref="ScriptRuns"/>.</para></summary>
    private static void AddGrams(string run, int length, List<string> terms, HashSet<string> seen)
    {
        if (length < 1) return;
        var taken = 0;
        for (var i = 0; i + length <= run.Length && taken < MaxTermsPerToken; i++)
        {
            var gram = run.Substring(i, length);
            if (!seen.Add(gram)) continue;
            terms.Add(gram);
            taken++;
        }
    }

    /// <summary>The terms a SUBSTRING scan should match on — <see cref="Extract"/>'s terms plus each
    /// expanded script's shorter grams.
    /// <para><b>Two methods because there are two audiences, and conflating them is what the split
    /// prevents.</b> <see cref="Extract"/> is what a full-text INDEX can match, so it stops at the index
    /// floor. A <c>LIKE</c>/<c>ILIKE</c> scan has no such floor and can carry more — and must, because most
    /// Chinese content words are exactly two characters. A backend that matches by substring (both InMemory
    /// stores, every Postgres query, both SQLite fallbacks) uses THIS one; only the FTS query builder uses
    /// <see cref="Extract"/>.</para></summary>
    /// <remarks><b>The two sources are UNIONED unconditionally — never short-circuited on
    /// <see cref="Extract"/> being empty.</b> That short-circuit shipped through 3.0 and silently deleted
    /// every term for a query whose tokens are ALL below the index floor: <c>"配偶 客户"</c>, two ordinary
    /// two-character Chinese words, yielded NOTHING, so every substring backend fell back to matching the
    /// whole trimmed query as one literal (<see cref="LikeClause"/>'s own empty-terms fallback) — i.e.
    /// <c>%配偶 客户%</c>, which demands that exact phrase INCLUDING the space and matches no ordinary prose.
    /// <para><b>The tell was the asymmetry, not the empty list</b>: the identical token survived when a
    /// LONGER word accompanied it (<c>"配偶 叫什么名字"</c>), because that neighbour made <c>Extract</c>
    /// non-empty and the short grams were appended after all. A word kept or dropped according to its
    /// neighbours is no design.</para>
    /// <para>It also defeated the very rescue built for this case: Postgres runs a narrow pass, then checks
    /// <see cref="HasShortSpacelessTerms"/> — which reports <c>true</c> here, because it calls
    /// <see cref="ShortGrams"/> directly — and pays a second round trip to widen, which then returned the
    /// same empty list. The widening pass rescued nothing while costing the scan it was budgeted for.</para>
    /// <para>The SINGLE-token case (<c>"配偶"</c> alone) was never broken, and that coincidence is why this
    /// survived: one token yields no terms and falls through to a whole-query scan for <c>%配偶%</c>, which
    /// is exactly what the term-based clause now produces. Behaviour there is unchanged.</para>
    /// <para>De-duplicated across the union rather than only within each source. With every shipped
    /// <see cref="ScriptProfile"/> the two lengths differ (index 3, substring 2) so no gram can repeat — but
    /// <see cref="ScriptProfile"/> is a public, consumer-constructible type, and a profile declaring the two
    /// lengths equal would otherwise emit each gram twice and double-count it in
    /// <see cref="LikeTermClause.MatchCount"/>, quietly distorting the ordering that expression exists to
    /// provide.</para></remarks>
    public static IReadOnlyList<string> SubstringTerms(string? raw)
    {
        var terms = Extract(raw);
        var shortGrams = ShortGrams(raw);
        if (shortGrams.Count == 0) return terms;
        if (terms.Count == 0) return shortGrams;

        var seen = new HashSet<string>(terms, StringComparer.Ordinal);
        return [.. terms, .. shortGrams.Where(g => seen.Add(g))];
    }

    /// <summary>Whether <paramref name="raw"/> has any short-gram term that <see cref="Extract"/> drops —
    /// i.e. whether widening a substring clause would add anything.
    /// <para>Exists so a backend running the index-friendly pass first can skip the second round trip
    /// entirely when there is nothing to widen with, which is every ASCII query. Cheaper and far clearer
    /// than building both clauses and comparing their term counts.</para></summary>
    public static bool HasShortSpacelessTerms(string? raw) => ShortGrams(raw).Count > 0;

    /// <summary>
    /// The shorter grams of every expanded token — <b>for the SUBSTRING backends only</b>, which is why this
    /// is not part of <see cref="Extract"/>.
    ///
    /// <para><b>Why it exists.</b> Most Chinese content words are exactly two characters (配偶 spouse, 客户
    /// client), and a trigram index cannot match one: glue it to anything and the trigrams straddle the
    /// boundary and appear in no text containing the word. The short-query fallback does not rescue this —
    /// a query that is ONLY <c>配偶</c> yields no term and falls through to the whole-query scan, but a
    /// longer query whose sole overlap is that word DOES yield trigrams, so full-text search runs, matches
    /// nothing, and the fallback then re-tries the entire query, which fails too.</para>
    ///
    /// <para><b>Why only here.</b> <c>LIKE</c>/<c>ILIKE</c> matches a substring with no index-imposed
    /// minimum, so it can carry terms the FTS tokenizer cannot. Emitted ALONGSIDE the longer grams rather
    /// than instead of them: the longer ones are more selective, and the matched-term count these backends
    /// order by still rewards a row matching more of them.</para>
    ///
    /// <para><b>Why not ASCII.</b> A two-letter English fragment ("is", "of", "de") matches almost every
    /// row, which would destroy the precision the whole-word rule exists to protect. A two-character CJK
    /// word is a real word and reasonably selective — the asymmetry is in the writing systems, not a
    /// special case, and it lives in <see cref="ScriptProfile.SubstringGramLength"/> rather than in an
    /// <c>if</c> here.</para>
    ///
    /// <para><b>The cost, stated:</b> <c>pg_trgm</c> cannot accelerate a two-character pattern, so on
    /// Postgres these terms are matched by a sequential scan. Measured before adoption — see
    /// <c>docs/DECISIONS.md</c> D55.</para>
    /// </summary>
    private static IReadOnlyList<string> ShortGrams(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var grams = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in raw.Split((char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var (run, profile) in ScriptRuns(token))
                if (profile.ExpandsIntoGrams && profile.SubstringGramLength >= 1)
                    AddGrams(run, profile.SubstringGramLength, grams, seen);
        }

        return grams;
    }

    /// <summary>The script ranges, MOST DEMANDING FIRST. Order is load-bearing: <see cref="ProfileOf"/>
    /// returns the profile of the first matching script, so a mixed kanji/kana token — which is what ordinary
    /// Japanese looks like — is analysed under kana's rules rather than Han's. Taking the laxer half of a
    /// mixed token is how a script's own weakness gets hidden behind its neighbour.
    ///
    /// <para><b>Numeric code points, never character literals.</b> The literal form is how the previous
    /// version was wrong: it wrote CJK Compatibility Ideographs as <c>'豈'..'﫿'</c>, but the ordinary 豈 is
    /// U+8C48 rather than the U+F900 compatibility character it meant to name — so the range ran
    /// U+8C48..U+FBFF and swallowed the entire Hangul block. That was invisible while the answer was one
    /// boolean ("spaceless?", true either way) and became a MISROUTED PROFILE the moment scripts stopped
    /// being interchangeable. On a machine whose console is not UTF-8 a literal is also the thing most
    /// likely to be silently transcoded. Caught by
    /// <c>SearchTermsTests.ProfileOf_returns_the_most_demanding_script_present</c>.</para></summary>
    private static readonly (ScriptProfile Profile, Func<char, bool> Matches)[] Registry =
    [
        (ScriptProfile.Kana, c => In(c, 0x3040, 0x30FF)),       // Hiragana + Katakana
        (ScriptProfile.Hangul, c => In(c, 0xAC00, 0xD7AF)),     // Hangul Syllables
        // The spaceless scripts that were missing entirely — each was handed back as ONE whitespace token and
        // could only match an exact substring, the pre-3.0 defect D55 fixed for CJK. Added because they were
        // ABSENT, not because they were measured; see ScriptProfile.Thai for the caveat that governs all of
        // them. Tibetan belongs here despite its tsheg: that mark separates syllables, not words.
        (ScriptProfile.Thai, c => In(c, 0x0E00, 0x0E7F)),
        (ScriptProfile.Lao, c => In(c, 0x0E80, 0x0EFF)),
        (ScriptProfile.Khmer, c => In(c, 0x1780, 0x17FF)),
        (ScriptProfile.Myanmar, c => In(c, 0x1000, 0x109F)),
        (ScriptProfile.Tibetan, c => In(c, 0x0F00, 0x0FFF)),
        (ScriptProfile.Han, c => In(c, 0x3400, 0x4DBF)          // CJK Unified Ideographs Extension A
                                 || In(c, 0x4E00, 0x9FFF)       // CJK Unified Ideographs
                                 || In(c, 0xF900, 0xFAFF)),     // CJK Compatibility Ideographs
    ];

    private static bool In(char c, int low, int high) => c >= low && c <= high;

    /// <summary>Every profile this library ships, including <see cref="ScriptProfile.Spaced"/>. Public so a
    /// consumer can SEE what their language is being analysed as — "why did my recall behave like that"
    /// should have an answer that does not require reading this file.</summary>
    public static IReadOnlyList<ScriptProfile> Profiles =>
        [ScriptProfile.Spaced, .. Registry.Select(r => r.Profile)];

    /// <summary>The profile of <paramref name="token"/>'s FIRST script run — for diagnostics and for the
    /// common case of a token that is all one script.
    /// <para><b>A mixed token has more than one profile</b>, and this returns only the first: analysis
    /// segments into runs and treats each under its own rules (<see cref="ScriptRuns"/>), so no single
    /// profile describes <c>我今天deploy了</c>. Reading one is still the right question for "what is this
    /// text being analysed as" when the text is one script, which is the case a consumer asks about.</para>
    /// </summary>
    public static ScriptProfile ProfileOf(string token) =>
        string.IsNullOrEmpty(token) ? ScriptProfile.Spaced : ScriptRuns(token)[0].Profile;

    /// <summary>Builds the SQL for matching <paramref name="column"/> against every term of
    /// <paramref name="raw"/>, for the backends that search with <c>LIKE</c>/<c>ILIKE</c> rather than a
    /// full-text index.
    /// <para><b>The term VALUES are parameters; <paramref name="column"/>, <paramref name="op"/> and
    /// <paramref name="parameterPrefix"/> are interpolated and must be caller-controlled literals</b> — they
    /// name a column and an operator, never user input.</para></summary>
    /// <param name="raw">The user's query. When it yields no terms, the clause falls back to matching the
    /// whole trimmed query as one substring, preserving the behaviour short queries always had.</param>
    /// <param name="column">The column expression to match, e.g. <c>n.content</c>.</param>
    /// <param name="op"><c>LIKE</c> (SQLite) or <c>ILIKE</c> (Postgres).</param>
    /// <param name="parameterPrefix">Prefix for the generated parameter names, unique within the statement.</param>
    /// <param name="includeShortTerms">Whether to carry the shorter grams of an expanded script (see
    /// <see cref="SubstringTerms"/>). <b>Pass false for a first pass that must stay index-friendly.</b>
    /// MEASURED on Postgres at 300k rows (2026-08-13): a two-character <c>ILIKE</c> pattern cannot use the
    /// <c>pg_trgm</c> GIN index and degrades to a parallel sequential scan — <b>96.6 ms against 0.90 ms</b>
    /// for the three-character pattern on the identical, equally selective data. So a backend that has an
    /// index to lose should run the narrow clause FIRST and widen only when it returns nothing, which is the
    /// same zero-rows-then-fall-back shape SQLite already uses between FTS and LIKE: the scan is then paid
    /// only in the case the short terms exist to rescue. A backend that scans anyway (in-process, or
    /// SQLite's own LIKE fallback) can widen immediately at no cost.</param>
    public static LikeTermClause LikeClause(string? raw, string column,
        string op = "LIKE", string parameterPrefix = "kw", bool includeShortTerms = true)
    {
        var terms = includeShortTerms ? SubstringTerms(raw) : Extract(raw);
        if (terms.Count == 0) terms = [raw?.Trim() ?? string.Empty];

        var parameters = new Dictionary<string, object?>(terms.Count, StringComparer.Ordinal);
        var predicates = new List<string>(terms.Count);
        var scores = new List<string>(terms.Count);
        for (var i = 0; i < terms.Count; i++)
        {
            var name = parameterPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            parameters[name] = LikePattern.Contains(terms[i]);
            var predicate = $"{column} {op} @{name} ESCAPE '\\'";
            predicates.Add(predicate);
            scores.Add($"CASE WHEN {predicate} THEN 1 ELSE 0 END");
        }

        return new LikeTermClause(
            "(" + string.Join(" OR ", predicates) + ")",
            "(" + string.Join(" + ", scores) + ")",
            terms.Count,
            parameters);
    }
}

/// <summary>The SQL produced by <see cref="SearchTerms.LikeClause"/>: a predicate that is true when ANY
/// term matches, and an expression counting HOW MANY did.</summary>
/// <param name="Predicate">Parenthesized <c>OR</c> of one substring test per term — use in <c>WHERE</c>.</param>
/// <param name="MatchCount">Parenthesized sum, <c>0</c>..<see cref="TermCount"/> — use to lead an
/// <c>ORDER BY</c> so a row matching more of the query outranks one matching less. This is the coarse
/// stand-in for the relevance ranking SQLite gets from <c>bm25</c>; without it an <c>OR</c> match is
/// unranked and a one-term brush-past can displace a near-exact hit.</param>
/// <param name="TermCount">How many terms the query produced.</param>
/// <param name="Parameters">Parameter name → pattern value, to merge into the command's parameters.</param>
public sealed record LikeTermClause(
    string Predicate,
    string MatchCount,
    int TermCount,
    IReadOnlyDictionary<string, object?> Parameters);
