using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Engine-agnostic facts every <see cref="IMemoryEngine"/> satisfies, run by each engine's test
/// class the way <c>MemoryStoreContract</c> holds three storage backends to one contract. Every method is
/// namespaced by a caller-supplied key so implementations sharing state stay isolated.</summary>
public static class MemoryEngineContract
{
    public static async Task Remember_then_recall_finds_it(IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "the deploy pipeline needs approval"));

        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "pipeline"));

        Assert.Contains(recall.Items, i => i.Headline.Contains("approval", StringComparison.Ordinal));
    }

    public static async Task Every_item_carries_this_engines_name(IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "ownership is recorded on the reference"));

        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "ownership"));

        Assert.NotEmpty(recall.Items);
        // a member of a composite owns its own entries, so the name is the OWNER's, not the blend's
        Assert.All(recall.Items, i => Assert.StartsWith(engine.Name.Split('/')[0], i.Reference.Engine,
            StringComparison.Ordinal));
    }

    public static async Task Recall_reports_the_tier_that_ran(IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "tiers are reported"));

        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "tiers"));

        Assert.NotEqual(MemorySources.None, recall.Ran);
    }

    public static async Task An_unsupported_grade_throws_rather_than_downgrading(IMemoryEngine engine, string key)
    {
        if (engine.Supported.HasFlag(MemoryGrades.Associative) &&
            engine.Supported.HasFlag(MemoryGrades.Authoritative))
            return; // an engine that holds both has nothing to refuse

        var unsupported = engine.Supported.HasFlag(MemoryGrades.Authoritative)
            ? MemoryGrade.Associative
            : MemoryGrade.Authoritative;

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.RememberAsync(new MemoryWrite(key, "s", "graded write", Grade: unsupported)));
    }

    public static async Task An_inherited_grade_resolves_and_is_never_returned_as_Inherit(
        IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "grade resolves on read"));

        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "resolves"));

        Assert.NotEmpty(recall.Items);
        Assert.All(recall.Items, i => Assert.NotEqual(MemoryGrade.Inherit, i.Grade));
    }

    public static async Task Authoritative_items_always_carry_full_content(IMemoryEngine engine, string key)
    {
        if (!engine.Supported.HasFlag(MemoryGrades.Authoritative)) return;

        await engine.RememberAsync(new MemoryWrite(key, "s", "the build gate is dev.mjs verify",
            Grade: MemoryGrade.Authoritative));

        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "gate"));

        Assert.All(recall.Items.Where(i => i.Grade == MemoryGrade.Authoritative),
            i => Assert.Equal("the build gate is dev.mjs verify", i.Content));
    }

    public static async Task An_empty_query_does_not_throw(IMemoryEngine engine, string key)
    {
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "   "));

        Assert.NotNull(recall.Items);
    }

    public static async Task Cancellation_propagates(IMemoryEngine engine, string key)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.RecallAsync(new MemoryQuery(key, "s", "anything"), cts.Token));
    }
}
