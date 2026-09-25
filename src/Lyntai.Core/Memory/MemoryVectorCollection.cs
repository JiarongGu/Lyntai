namespace Lyntai.Memory;

/// <summary>The vector-collection addresses memory writes under — a graph engine's enrichment vectors and
/// <see cref="SemanticMemory"/>'s — spelled ONCE.
///
/// <para><b>The separator is U+001F, not a printable character, and that is the whole point.</b> A printable
/// one makes two different addresses collide: with <c>|</c>, engine <c>E</c> + task <c>a</c> + scope
/// <c>b|c</c> and engine <c>E</c> + task <c>a|b</c> + scope <c>c</c> both compose to <c>E|a|b|c</c>, so a
/// forget for either erases the other's vectors — across a TASK boundary that <c>docs/memory.md</c> §7 holds
/// absolute (<b>D92</b>), and <see cref="PrefixFor"/> would sweep a neighbouring task's collections.</para>
///
/// <para><b>Why one type rather than a private method each side.</b> An address is composed by whoever
/// WRITES and rebuilt by whoever READS or removes, and two spellings of one address is how a removal quietly
/// misses the collection a write created.</para></summary>
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

    /// <summary>The collection <see cref="SemanticMemory"/> stores one (task, scope) under — two parts where a
    /// graph engine's address has three, and the spelling every stored semantic collection already carries.</summary>
    public static string ForSemantic(string taskKey, string scope) => $"{taskKey}{Separator}{scope}";

    /// <summary>The prefix matching every scope of one semantic task.
    /// <para><b>It also matches a graph engine's collections</b> whenever an engine is named like the task, since
    /// both share one vector store — read the scope with <see cref="SemanticScopeOf"/>, which refuses them.</para></summary>
    public static string SemanticPrefixFor(string taskKey) => $"{taskKey}{Separator}";

    /// <summary>The scope a listed collection under <see cref="SemanticPrefixFor"/> addresses, or null when the
    /// remainder carries a second separator — a graph engine's (engine, task, scope) address, which belongs to
    /// another task and must never answer a semantic recall.</summary>
    public static string? SemanticScopeOf(string collection, string prefix)
    {
        var scope = collection[prefix.Length..];
        return scope.Contains(Separator) ? null : scope;
    }
}
