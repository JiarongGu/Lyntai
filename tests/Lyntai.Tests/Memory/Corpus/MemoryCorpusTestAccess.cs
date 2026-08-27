namespace Lyntai.Tests.Memory.Corpus;

/// <summary>The one shared reader, for the whole test assembly, of the convention every
/// <see cref="MemoryCorpus"/> write follows: content is "{leading token} {id} …", so the id is always the
/// second space-delimited part — true in every <see cref="CorpusLanguage"/>, since only the id stays ASCII.
/// <c>MemoryPolicySweep.ExtractCorpusId</c> reads the same convention independently in the bench project,
/// which links this project's source files rather than referencing its assembly and so cannot reach this
/// member — that duplication is deliberate (two independent readers of one convention).
/// A second copy INSIDE this assembly would not carry that justification, so this is the one place a test
/// here parses an id back out of content.</summary>
internal static class MemoryCorpusTestAccess
{
    internal static string IdOf(string content) => content.Split(' ')[1];
}
