using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Tests.Memory.Corpus;

/// <summary>
/// <b>Byte-exact goldens for the ENGLISH corpus, so a change meant to be additive can be PROVED additive
/// rather than argued to be.</b>
///
/// <para>Every recall-quality figure this repository publishes is a measurement taken over
/// <see cref="MemoryCorpus.Generate"/>. That makes the generator an instrument, and silently moving an
/// instrument invalidates every number previously read off it — including the ones pinned as regression
/// guards elsewhere, which would simply re-baseline to the new reality without anyone noticing the
/// measurement changed rather than the subject.</para>
///
/// <para>Added 2026-08-12 alongside <see cref="CorpusLanguage"/>, whose whole claim is "English is
/// untouched". These hashes are what makes that claim checkable: they were captured BEFORE the language
/// axis existed and did not move when it landed. A future change that alters English output fails here
/// first, which is the point — the failure is not "the hash is stale", it is "you moved the ruler".</para>
///
/// <para>Deliberately hashes the RENDERED timeline (step kind, text, and ground-truth ids in order) rather
/// than object identity: that is exactly the surface a measurement consumes, and nothing else about the
/// generator is a promise.</para>
/// </summary>
public class MemoryCorpusGoldenTests
{
    private const int Seed = 12345;   // the sweep's own seed

    /// <summary>The timeline as a measurement sees it. Any change to a template, an id, an ordering or a
    /// ground-truth set moves this string.</summary>
    private static string Render(MemoryCorpus corpus)
    {
        var sb = new StringBuilder();
        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w: sb.Append("W|").Append(w.Write.Content).Append('\n'); break;
                case CorpusQuery q:
                    sb.Append("Q|").Append(q.Text).Append('|')
                      .Append(string.Join(",", q.RelevantIds));
                    // Conditional: every pre-existing query's SupportNeeded is 0, and an unconditional
                    // append would suffix "|0" onto every line, moving every golden that predates this field.
                    if (q.SupportNeeded != 0) sb.Append('|').Append(q.SupportNeeded);
                    sb.Append('\n');
                    break;
                case CorpusExpand e: sb.Append("E|").Append(e.EntryId).Append('\n'); break;
                default: throw new InvalidOperationException($"unhandled step {step.GetType().Name}");
            }

        return sb.ToString();
    }

    private static string Hash(MemoryCorpus corpus) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Render(corpus)))).ToLowerInvariant();

    /// <summary>Captured 2026-08-12 from the generator as it stood BEFORE <see cref="CorpusLanguage"/>
    /// existed. Six shapes rather than one so the goldens span every opt-in axis — an axis with no golden
    /// is an axis a future change can move silently. The sixth, "routine", was captured 2026-08-27 and
    /// necessarily POSTDATES the language axis, so it pins <see cref="Render"/>'s current shape rather than
    /// the axis's own original baseline.</summary>
    public static TheoryData<string, CorpusShape, string> Goldens() => new()
    {
        { "default", CorpusShape.Default,
            "604b02282745dc56a7d1d1286a47725043a0f694b6099046c3c5224dc5dcc60e" },
        { "attributes", CorpusShape.Default with { AttributeCount = 3 },
            "fd577bf6cd50a7f5fb2e2b2f5f7c6bd78975991e60115fbac204b631ab9a052c" },
        { "attributes-common-cue",
            CorpusShape.Default with { AttributeCount = 3, AttributeCue = AttributeCueKind.SharesCommonTokens },
            "4079879638b7e1c7141723639876ebec132d274929740add2b1c22f5e4c77dc8" },
        { "expand", CorpusShape.Default with { ExpandRatio = 2 },
            "bd0d9dd9f68535ac57dc3fbe9bd1f5d97e5c9aaf5b7e4cac97fe057fa4797099" },
        { "many-candidates", new CorpusShape(4, 8, 6, 40),
            "2f3ef4e9a5b371c1d1e5af7bf448402a794a320da2daf38cd22b727dd491e480" },
        // RoutineCount = 12 (A=8/B=4), not 9 (A=6/B=3): at 9, B's own count EQUALS RoutineSupport (3), so
        // Measure clamps needed = min(3,3) = 3 = the whole relevant set — indistinguishable from the all-of
        // branch every other class exercises. 12 makes B=4 > RoutineSupport, so needed = min(3,4) = 3 < 4
        // and the FINAL query genuinely exercises the n-of-N branch too, not only the phase-A query.
        { "routine", CorpusShape.Default with { RoutineCount = 12 },
            "4790d658ed6376465a3c83ef4cedaba989e9c6319a4927b26ba1cfa2b1d1085b" },
    };

    [Theory]
    [MemberData(nameof(Goldens))]
    public void The_english_corpus_is_byte_identical_to_its_golden(string name, CorpusShape shape, string golden)
    {
        Assert.NotNull(name);
        Assert.Equal(golden, Hash(MemoryCorpus.Generate(shape, Seed)));
    }

    /// <summary>A golden proves English did not move; it says nothing about whether a non-English variant is
    /// DIFFERENT. Without this, a lexicon that silently fell back to English would pass every golden above
    /// and the whole language axis would be a no-op nobody noticed.
    /// <para>Every non-English language, and every language pairwise distinct from every other — the second
    /// half matters as soon as there is more than one: a new lexicon that accidentally inherited another's
    /// templates would differ from English and still be a duplicate arm reported as an independent
    /// measurement.</para></summary>
    [Theory]
    [MemberData(nameof(Goldens))]
    public void Every_language_hashes_differently_for_the_same_shape(
        string name, CorpusShape shape, string golden)
    {
        Assert.NotNull(name);

        var byLanguage = Enum.GetValues<CorpusLanguage>()
            .ToDictionary(l => l, l => Hash(MemoryCorpus.Generate(shape with { Language = l }, Seed)));

        Assert.Equal(golden, byLanguage[CorpusLanguage.English]);
        Assert.Equal(byLanguage.Count, byLanguage.Values.Distinct(StringComparer.Ordinal).Count());
    }
}
