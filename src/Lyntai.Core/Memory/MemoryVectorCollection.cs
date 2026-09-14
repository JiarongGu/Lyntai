namespace Lyntai.Memory;

/// <summary>The similarity-index address a graph engine stores enrichment vectors under, spelled ONCE.
///
/// <para><b>The separator is U+001F, not a printable character, and that is the whole point.</b> A printable
/// one makes two different addresses collide: with <c>|</c>, engine <c>E</c> + task <c>a</c> + scope
/// <c>b|c</c> and engine <c>E</c> + task <c>a|b</c> + scope <c>c</c> both compose to <c>E|a|b|c</c>, so a
/// forget for either erases the other's vectors — across a TASK boundary that <c>docs/memory.md</c> §7 holds
/// absolute (<b>D92</b>). The same collision made <see cref="PrefixFor"/> sweep a neighbouring task's
/// collections. <see cref="SemanticMemory"/> has used U+001F for this since it shipped; the graph engine did
/// not, and this type is why there is now one answer rather than two
/// (<c>docs/FIXES.md</c>, 2026-09-15).</para>
///
/// <para><b>Why one type rather than a private method each side.</b> The address is composed by the engine
/// that WRITES and rebuilt by the seed source that READS, and two spellings of one address is how a removal
/// quietly misses the collection a write created — a hazard the engine's own doc named while carrying a
/// second spelling next door.</para></summary>
internal static class MemoryVectorCollection
{
    /// <summary>U+001F UNIT SEPARATOR. Built from the code point so the source stays plain ASCII — an inline
    /// control byte is invisible in a diff and does not survive every editor.</summary>
    private const char Separator = (char)0x1f;

    /// <summary>The collection holding one (engine, task, scope)'s vectors.</summary>
    public static string For(string engine, string taskKey, string scope) =>
        $"{engine}{Separator}{taskKey}{Separator}{scope}";

    /// <summary>The prefix matching every scope under one (engine, task) — and NOTHING under another task,
    /// which a printable separator could not promise.</summary>
    public static string PrefixFor(string engine, string taskKey) =>
        $"{engine}{Separator}{taskKey}{Separator}";
}
