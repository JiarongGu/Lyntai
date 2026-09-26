using static Lyntai.Tests.Text.SentencePieceFixture;
using Reference = Microsoft.ML.Tokenizers.SentencePieceTokenizer;
using SentencePieceTokenizer = Lyntai.Text.SentencePieceTokenizer;

namespace Lyntai.Tests.Text;

/// <summary>The owned tokenizer over a REAL XLM-R vocabulary — 250,002 pieces and the real nmt_nfkc charsmap,
/// which the tiny fixture cannot stand in for — against both references.
///
/// <para>Skipped without <c>LYNTAI_SPM_MODEL_DIR</c>: a directory holding an XLM-R <c>tokenizer.json</c>, and
/// <c>sentencepiece.bpe.model</c> for the second test (a sentence-transformers export such as
/// <c>paraphrase-multilingual-MiniLM-L12-v2</c> ships both).</para></summary>
public class SentencePieceTokenizerLiveTests
{
    private static string? ModelDirectory => Environment.GetEnvironmentVariable("LYNTAI_SPM_MODEL_DIR");

    private static readonly Golden Xlmr = Load("spm-xlmr.golden.json");

    [SkippableFact]
    public void Matches_the_HF_golden_over_the_real_vocabulary()
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory), "set LYNTAI_SPM_MODEL_DIR to an XLM-R export");
        var ours = SentencePieceTokenizer.FromModelDirectory(ModelDirectory!);

        var misses = Xlmr.Cases.Concat(Xlmr.HfOnly)
            .Select(c => (c.Text, c.Ids, Actual: ours.EncodeToIds(c.Text).ToArray()))
            .Where(r => !r.Ids.SequenceEqual(r.Actual))
            .Select(r => $"{Escape(r.Text)} -> expected [{string.Join(',', r.Ids)}], got [{string.Join(',', r.Actual)}]")
            .ToList();

        Assert.True(misses.Count == 0, string.Join(Environment.NewLine, misses));
        foreach (var typed in Xlmr.TypedSpecials) Assert.Equal(typed.Ids, ours.EncodeToIds(typed.Text));
    }

    /// <summary>Inputs where <c>Microsoft.ML.Tokenizers</c>' port departs from C++ <c>sentencepiece</c> itself —
    /// the golden records that C++ and HF AGREE on each — so matching the port would mean reproducing its defect.
    /// Asserted in <see cref="The_Cpp_port_still_departs_where_recorded"/> rather than merely skipped.</summary>
    private static readonly string[] WhereThePortDeparts = ["control\u0001\u007fchars"];

    [SkippableFact]
    public void Matches_the_Cpp_port_id_for_id_through_the_fairseq_offset()
    {
        var (ours, theirs) = BothOrSkip();

        // the inputs HF and C++ agree on, so a miss here is a defect on one side rather than a known split
        var divergences = Xlmr.Cases
            .Where(c => !WhereThePortDeparts.Contains(c.Text))
            .Select(c => (c.Text, Expected: Port(theirs, c.Text), Actual: ours.EncodeToIds(c.Text).ToArray()))
            .Where(r => !r.Expected.SequenceEqual(r.Actual))
            .Select(r => $"{Escape(r.Text)} -> C++ port [{string.Join(',', r.Expected)}], ours [{string.Join(',', r.Actual)}]")
            .ToList();

        Assert.True(divergences.Count == 0, string.Join(Environment.NewLine, divergences));
    }

    /// <summary>The port keeps the C0 and DEL controls that nmt_nfkc deletes, as unfused unknowns. Asserting its
    /// answer as well as ours means that if the port is ever fixed this fails and the exclusion can go.</summary>
    [SkippableFact]
    public void The_Cpp_port_still_departs_where_recorded()
    {
        var (ours, theirs) = BothOrSkip();

        foreach (var text in WhereThePortDeparts)
        {
            var golden = Xlmr.Cases.Single(c => c.Text == text).Ids;
            Assert.Equal(golden, ours.EncodeToIds(text));
            Assert.NotEqual(golden, Port(theirs, text));
        }
    }

    private static (SentencePieceTokenizer Ours, Reference Theirs) BothOrSkip()
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory), "set LYNTAI_SPM_MODEL_DIR to an XLM-R export");
        var model = Path.Combine(ModelDirectory!, "sentencepiece.bpe.model");
        Skip.IfNot(File.Exists(model), $"no sentencepiece.bpe.model in {ModelDirectory}");

        using var stream = File.OpenRead(model);
        return (SentencePieceTokenizer.FromModelDirectory(ModelDirectory!),
            Reference.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false));
    }

    private static int[] Port(Reference theirs, string text) => [.. theirs.EncodeToIds(text).Select(Fairseq)];

    // XLM-R's ids are SentencePiece's shifted by one (fairseq), with SentencePiece's unknown (0) at 3.
    private static int Fairseq(int id) => id == 0 ? 3 : id + 1;
}
