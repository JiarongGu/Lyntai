using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>A front-door slot holds ONE decorator. The same registration repeated is a no-op (so a repeated
/// <c>AddResponseCache()</c> does not stack two layers); a DIFFERENT one on a taken slot is a composition
/// error, because the loser would otherwise be dropped while its options still read as wired.</summary>
public sealed class FrontDoorDecoratorSlotTests
{
    private static readonly Func<IServiceProvider, ITextClient, ITextClient> PassThrough = (_, inner) => inner;

    private static void Compose(Action<LyntaiBuilder> configure) =>
        new ServiceCollection().AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeTextProvider("p")).UseInMemoryStorage();
            configure(b);
        });

    [Fact]
    public void A_custom_decorator_taking_a_built_ins_slot_throws_naming_both()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Compose(b => b
            .AddFrontDoorDecorator(LyntaiBuilder.BudgetDecoratorOrder, PassThrough)
            .AddUsageBudget(o => o.MaxTokens = 100)));

        Assert.Contains($"{LyntaiBuilder.BudgetDecoratorOrder}", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LyntaiBuilder.AddUsageBudget), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LyntaiBuilder.AddFrontDoorDecorator), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_built_in_already_holding_the_slot_refuses_a_custom_decorator_too()
    {
        Assert.Throws<InvalidOperationException>(() => Compose(b => b
            .AddResponseCache()
            .AddFrontDoorDecorator(LyntaiBuilder.CacheDecoratorOrder, PassThrough)));
    }

    [Fact]
    public void Two_different_custom_decorators_on_one_order_throw()
    {
        Assert.Throws<InvalidOperationException>(() => Compose(b => b
            .AddFrontDoorDecorator(15, PassThrough)
            .AddFrontDoorDecorator(15, (_, inner) => inner)));
    }

    [Fact]
    public void Repeating_the_same_registration_is_a_no_op()
    {
        Compose(b => b
            .AddResponseCache().AddResponseCache()
            .AddUsageBudget().AddUsageBudget()
            .AddRateLimit().AddRateLimit()
            .AddFrontDoorDecorator(15, PassThrough).AddFrontDoorDecorator(15, PassThrough));
    }
}
