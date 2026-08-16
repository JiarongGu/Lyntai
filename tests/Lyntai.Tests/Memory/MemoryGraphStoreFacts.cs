using System.Reflection;

using Lyntai.Memory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>
/// The theory source and invoker that make <see cref="MemoryGraphStoreContract"/> coverage STRUCTURAL.
/// </summary>
/// <remarks>
/// <para><b>What this replaces, and why it is not merely tidier.</b> Each backend used to wire the contract
/// by hand — one <c>[Fact]</c> per method on InMemory and SQLite, one long sequential method on Postgres —
/// and the only exhaustiveness check was a single <c>Assert.Equal(declared, covered)</c> in
/// <c>PostgresStorageTests</c> against a HAND-BUMPED literal. That catches a fact wired nowhere, and one
/// wired everywhere except Postgres, but it cannot catch a fact wired to Postgres ALONE: the author bumps
/// the literal, it passes, and InMemory and SQLite silently never run it. The invariant it protects —
/// <i>"a cross-backend invariant enforced on ONE backend's test class is not enforced"</i>
/// (<c>.claude/knowledge/pitfalls.md</c>) — is one this repository has already been bitten by.</para>
///
/// <para>Driving every backend from <see cref="Names"/> closes that direction by CONSTRUCTION rather than by
/// counting: a method added to the contract is a new theory case on all three backends the moment it
/// compiles, and there is no literal left to bump. The per-fact test name survives as the theory argument,
/// so a failure still names the fact rather than an index.</para>
///
/// <para><b>This class is deliberately separate from the contract</b> so that "every public static
/// <see cref="Task"/>-returning method on <see cref="MemoryGraphStoreContract"/>" stays an exact description
/// of the fact set. A helper living beside the facts would be reflected as one.</para>
/// </remarks>
public static class MemoryGraphStoreFacts
{
    /// <summary>Every fact the contract declares, in a stable order.</summary>
    /// <remarks>
    /// <see cref="BindingFlags.Static"/> without <see cref="BindingFlags.FlattenHierarchy"/> returns only
    /// the methods declared on this type, so <see cref="object"/>'s own statics never appear. The
    /// <see cref="Task"/> return filter is belt-and-braces for the same reason.
    /// </remarks>
    public static MethodInfo[] Declared() =>
        [.. typeof(MemoryGraphStoreContract)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .OrderBy(m => m.Name, StringComparer.Ordinal)];

    /// <summary>The xUnit theory source every backend's contract suite is fed from.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var m in Declared()) data.Add(m.Name);
        return data;
    }

    /// <summary>
    /// Run one fact against a store the backend supplies.
    /// </summary>
    /// <param name="name">The fact, as handed to the theory.</param>
    /// <param name="store">
    /// Builds the store under test. It is handed a clock accessor for the two facts that assert on ELAPSED
    /// time and <see langword="null"/> for every other, because only those two need a store whose clock the
    /// test can move; a backend that cannot take a clock may ignore the argument.
    /// </param>
    /// <param name="key">A task key unique to this case — the Postgres container is shared across cases.</param>
    public static Task RunAsync(string name, Func<Func<DateTimeOffset>?, IMemoryGraphStore> store, string key)
    {
        var method = Array.Find(Declared(), m => m.Name == name)
            ?? throw new InvalidOperationException(
                $"'{name}' is not a fact on {nameof(MemoryGraphStoreContract)}. The theory source and the "
                + "contract cannot disagree unless one of them was filtered — check Declared().");

        var parameters = method.GetParameters();
        object?[] args;
        if (parameters.Length == 2)
        {
            args = [store(null), key];
        }
        else
        {
            // The elapsed-time pair. The clock has to be built BEFORE the store, since the store captures
            // the accessor, and `Advance` has to be the same instance's — passing a fresh clock to either
            // side would leave the fact asserting against time that never moved, and it would PASS on a
            // backend that ignores the clock entirely.
            var clock = new MutableClock();
            // The delegate type is written out: into an `object?[]` a bare method group has no target type
            // to infer from (CS8974), and the whole point of this argument is that it arrives as the
            // `Action<TimeSpan>` the fact declares.
            args = [store(clock.Get), key, new Action<TimeSpan>(clock.Advance)];
        }

        return (Task)method.Invoke(null, args)!;
    }
}
