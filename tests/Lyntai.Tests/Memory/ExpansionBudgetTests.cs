using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>An expansion's budget follows the recall's rule: a neighbour that does not fit is SKIPPED, so one
/// long neighbour cannot hide every shorter one behind it.</summary>
public class ExpansionBudgetTests
{
    [Fact]
    public async Task A_long_neighbour_that_does_not_fit_does_not_hide_a_short_one_behind_it()
    {
        var engine = new GraphMemoryEngine("g", new InMemoryMemoryGraphStore());
        var named = await engine.RememberAsync(new MemoryWrite("t", "s", "the ferry"));
        var longOne = await engine.RememberAsync(new MemoryWrite("t", "s", new string('x', 400)));
        var shortOne = await engine.RememberAsync(new MemoryWrite("t", "s", "pier north"));
        // the long neighbour is linked MORE strongly, so it is walked first
        await engine.LinkAsync(named.Reference, longOne.Reference, weight: 5, symmetric: true);
        await engine.LinkAsync(named.Reference, shortOne.Reference, weight: 1, symmetric: true);

        var expanded = await engine.ExpandAsync(named.Reference, charBudget: "the ferry".Length + 20);

        Assert.Contains(expanded.Items, i => i.Reference == shortOne.Reference);
        Assert.DoesNotContain(expanded.Items, i => i.Reference == longOne.Reference);
    }
}
