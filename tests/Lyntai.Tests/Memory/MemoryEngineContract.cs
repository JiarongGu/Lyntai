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

    /// <summary>A recall returns AT MOST <see cref="MemoryQuery.Limit"/> items — the flat property that
    /// nothing anywhere asserted.
    /// <para>Written as a CONTRACT fact rather than fixed per engine on purpose. The composite and the
    /// curated engine were each found ignoring the limit on 2026-08-14 and repaired individually, which is
    /// exactly the shape <c>pitfalls.md</c> records as not working: "a cross-backend invariant enforced on
    /// ONE backend's test class is not enforced". Every engine answers this question, so every engine is
    /// asked it here.</para>
    /// <para>Deliberately writes MORE entries than the limit and uses a query every one of them matches, so
    /// the bound is the only thing that can cut the result. An engine that returns nothing would pass a bare
    /// upper-bound assertion vacuously, so the non-empty check is part of the fact.</para>
    /// <para><b>It writes EVERY grade the engine supports, and that is what makes it discriminating on a
    /// blend.</b> The first version wrote only `Inherit`, which a composite routes entirely to its FIRST
    /// member — so one member held everything, applied the limit itself, and the blend never had more than
    /// the limit to cut. Disabling the composite's cut outright left that version passing: a fixture sitting
    /// in the one regime where the property cannot fail, which is the trap <c>pitfalls.md</c> records for the
    /// AuthoritativeReserve fixtures. Loading every member is what forces the blend past its own bound.</para>
    /// </summary>
    public static async Task A_recall_returns_at_most_the_limit(IMemoryEngine engine, string key)
    {
        for (var i = 0; i < 6; i++)
        {
            if (engine.Supported.HasFlag(MemoryGrades.Associative))
                await engine.RememberAsync(new MemoryWrite(key, "s",
                    $"bounded entry number {i} about deployment", Grade: MemoryGrade.Associative));
            if (engine.Supported.HasFlag(MemoryGrades.Authoritative))
                await engine.RememberAsync(new MemoryWrite(key, "s",
                    $"exact deployment fact number {i}", Grade: MemoryGrade.Authoritative));
        }

        var unbounded = await engine.RecallAsync(new MemoryQuery(key, "s", "deployment"));
        Assert.NotEmpty(unbounded.Items);   // the corpus really is reachable, so the bound below means something

        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "deployment", Limit: 2));

        Assert.NotEmpty(recall.Items);
        Assert.True(recall.Items.Count <= 2,
            $"{engine.Name} returned {recall.Items.Count} items for a Limit of 2");
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
