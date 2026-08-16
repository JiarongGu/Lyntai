using Lyntai.Memory;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the InMemory backend.</summary>
/// <remarks>
/// Driven from <see cref="MemoryGraphStoreFacts.Names"/> rather than wired by hand, so a fact added to the
/// contract runs here the moment it compiles. See <c>MemoryGraphStoreFacts</c> for what that replaced.
/// </remarks>
public class InMemoryMemoryGraphStoreTests
{
    [Theory]
    [MemberData(nameof(MemoryGraphStoreFacts.Names), MemberType = typeof(MemoryGraphStoreFacts))]
    public Task Contract(string fact) =>
        // A fresh store per case, which is what the hand-wired `New()` gave each [Fact] before. The key is
        // the fact's own name: unique by construction, and it reads in a failure message.
        MemoryGraphStoreFacts.RunAsync(fact, clock => new InMemoryMemoryGraphStore(clock: clock), fact);
}
