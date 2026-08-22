namespace Lyntai.Memory;

/// <summary>
/// The ONE normalization every <see cref="IMemoryGraphStore"/> applies to a subject handle, on the way in
/// and on the way out.
///
/// <para><b>Why this is a function rather than a sentence in the contract.</b>
/// <see cref="IMemoryGraphStore.RecordSubjectsAsync"/> and
/// <see cref="IMemoryGraphStore.NodesBySubjectAsync"/> promise a subject is "compared case-insensitively",
/// and each of the three shipped backends implemented that promise with its own inline copy of
/// <c>Trim().ToLowerInvariant()</c> — six copies of a rule that has to be IDENTICAL across backends or the
/// same annotation links a different cluster depending on where it was stored. That is the shape
/// <c>.claude/knowledge/pitfalls.md</c> §Storage records for <c>salience</c>: <i>one stored value read at N
/// sites grows N coercion rules, and the divergence is silent</i>. <see cref="MemorySignals.Salience"/> is
/// the same fix for that one.</para>
///
/// <para><b>Invariant casing is load-bearing, not a style choice.</b> A backend reaching for
/// <c>ToLower()</c> (or a database collation) instead would fold <c>"I"</c> to <c>"ı"</c> under a Turkish
/// culture, so an annotator's handle recorded on one machine would not match the same handle looked up on
/// another — a linking failure that reproduces only under a locale nobody tests with. Naming the rule once
/// is what makes it checkable.</para>
///
/// <para><b>Public because a BYO store needs it.</b> <see cref="IMemoryGraphStore"/> is an extension point,
/// and a store outside this repository has to normalize exactly as the shipped ones do or its subjects
/// silently stop linking. Handing it the rule is cheaper than documenting it and hoping.</para>
/// </summary>
public static class MemorySubject
{
    /// <summary>This subject handle in its canonical form: trimmed, lowercased invariantly.</summary>
    /// <param name="subject">The handle as an annotator produced it.</param>
    /// <returns>The canonical form; the empty string for a null, empty or whitespace-only handle, which
    /// <see cref="IsUsable"/> is the test for.</returns>
    public static string Normalize(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? string.Empty : subject.Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="subject"/> is worth recording or looking up at all — false for
    /// null, empty and whitespace-only, which every backend drops rather than storing as a handle nothing
    /// can ever match.</summary>
    /// <param name="subject">The handle to test.</param>
    public static bool IsUsable(string? subject) => !string.IsNullOrWhiteSpace(subject);

    /// <summary>Every usable handle in <paramref name="subjects"/>, normalized and de-duplicated, in first-
    /// seen order — what a store actually persists for one node.
    /// <para>De-duplicated ORDINALLY, after normalizing: two handles that differed only in case or padding
    /// have already become one string by then, so an ordinal comparison is exact rather than merely
    /// approximate, and no second casing rule enters the picture.</para></summary>
    /// <param name="subjects">The handles as an annotator produced them; null yields nothing.</param>
    public static IReadOnlyList<string> Canonicalize(IEnumerable<string>? subjects) =>
        subjects is null
            ? []
            : [.. subjects.Where(IsUsable).Select(s => Normalize(s)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Whether <paramref name="query"/> names <paramref name="subject"/> — the read-side counterpart of
    /// <see cref="Canonicalize"/>, and what lets a recall reach an entry through the handle it was indexed
    /// under rather than only through its own words.
    ///
    /// <para><b>Both sides are folded by <see cref="Normalize"/></b>, so the comparison below is ordinal and
    /// exact rather than approximately case-insensitive. One rule, applied twice.</para>
    ///
    /// <para><b>How closely it must match depends on the SCRIPT, through
    /// <see cref="Lyntai.Storage.ScriptProfile.ExpandsIntoGrams"/>.</b> A handle in a script that writes word
    /// spaces matches only on a word BOUNDARY — <c>pairbond</c> sits inside <c>repairbonded</c>, and matching
    /// it there would link a fact to a query that never mentioned it. A handle in a spaceless script matches
    /// as a plain SUBSTRING, because there is no boundary to anchor to: Chinese writes <c>配偶</c> straight
    /// against whatever precedes and follows it, so demanding one would reject every real query. That is the
    /// same asymmetry <see cref="Lyntai.Storage.SearchTerms"/> already draws for search terms, read from the
    /// same seam rather than restated as an is-this-ASCII test — which would get Cyrillic and Thai backwards
    /// in opposite directions.</para>
    ///
    /// <para>Public for the same reason the rest of this type is: a BYO engine that seeds recall from
    /// subjects has to ask the question the shipped one asks.</para>
    /// </summary>
    /// <param name="query">The recall's query text.</param>
    /// <param name="subject">The handle, as stored or as an annotator produced it.</param>
    /// <returns>False for an unusable handle or an empty query — neither can match anything.</returns>
    public static bool Matches(string? query, string? subject)
    {
        var handle = Normalize(subject);
        var text = Normalize(query);
        if (handle.Length == 0 || text.Length == 0) return false;

        if (Lyntai.Storage.SearchTerms.ProfileOf(handle).ExpandsIntoGrams)
            return text.Contains(handle, StringComparison.Ordinal);

        for (var i = text.IndexOf(handle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(handle, i + 1, StringComparison.Ordinal))
        {
            var end = i + handle.Length;
            if ((i == 0 || !char.IsLetterOrDigit(text[i - 1])) &&
                (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
        }

        return false;
    }
}
