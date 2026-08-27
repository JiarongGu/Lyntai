namespace Lyntai.Tests.Memory.Corpus;

/// <summary>The shared reader, for this whole test assembly, of the convention every TEMPLATED
/// <see cref="MemoryCorpus"/> write follows: content is "{leading token} {id} …", so the id is the second
/// space-delimited part. One reader is enough for every <see cref="CorpusLanguage"/> because the id alone
/// stays ASCII.
/// <para><b>The convention does NOT cover <see cref="CorpusNoiseKind.Diverse"/>.</b>
/// <c>CorpusLexicon.DiverseNoise</c> is deliberately near-skeletonless: it writes the id FIRST, and with no
/// separator at all in a spaceless language. So this reader is defined over templated content only and
/// THROWS rather than handing back a junk word. A test whose corpus can contain diverse noise needs a
/// position-independent rule instead — <c>MemorySalienceInversionTests</c> carries one.</para>
/// <para>Two readers elsewhere are deliberately not routed here.
/// <c>MemoryPolicySweep.ExtractCorpusId</c> reads the same convention independently in the bench project,
/// which links this project's source files rather than referencing its assembly and so cannot reach this
/// member at all. <c>DsrPathologyTests</c> reads an id back out of a recalled HEADLINE, which is a
/// different string with a different failure mode. Every other test here that wants a written entry's id
/// calls this.</para></summary>
internal static class MemoryCorpusTestAccess
{
    /// <summary>The corpus id embedded in <paramref name="content"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="content"/> does not follow the templated
    /// convention, so no id can be read from it.</exception>
    internal static string IdOf(string content)
    {
        var parts = content.Split(' ');
        if (parts.Length >= 2 && IsCorpusId(parts[1])) return parts[1];

        throw new ArgumentException(
            $"'{content}' does not follow MemoryCorpus's templated \"{{token}} {{id}} …\" convention, so the "
            + "second token is not an id. Diverse noise is the case this catches: it writes the id first, "
            + "and unseparated in a spaceless language.", nameof(content));
    }

    // Shape-checked rather than taken on trust, so content this reader cannot parse fails loudly instead of
    // yielding a plausible-looking word. Every corpus id is ASCII letters followed by an index digit
    // (`topic1`, `routineA0`); no filler or noise-vocabulary word in any lexicon has that form.
    private static bool IsCorpusId(string token) =>
        token.Length >= 2
        && char.IsAsciiLetter(token[0])
        && char.IsAsciiDigit(token[^1])
        && token.All(char.IsAsciiLetterOrDigit);
}
