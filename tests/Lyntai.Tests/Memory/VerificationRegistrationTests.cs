using Lyntai.Memory.Verification;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>The verifier seam is SINGULAR, so a second shipped registration is refused: dropped silently by
/// <c>TryAdd</c>, whichever came first would verify memory and nothing would say so.</summary>
public class VerificationRegistrationTests
{
    [Fact]
    public void Registering_both_shipped_verifiers_is_refused_naming_both()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddMemoryVerification()
            .AddMemoryScoringVerification()));

        Assert.Contains("AddMemoryVerification", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddMemoryScoringVerification", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_call_of_the_same_verifier_is_refused_rather_than_keeping_the_first_options()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddMemoryVerification(o => o.Model = "a")
            .AddMemoryVerification(o => o.Model = "b")));
    }

    [Fact]
    public void A_BYO_verifier_registered_first_still_wins_silently()
    {
        var services = new ServiceCollection();
        var mine = new ByoVerifier();
        services.AddSingleton<IMemoryVerificationPolicy>(mine);
        services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("p")).AddMemoryVerification());
        using var sp = services.BuildServiceProvider();

        Assert.Same(mine, sp.GetRequiredService<IMemoryVerificationPolicy>());
    }

    private sealed class ByoVerifier : IMemoryVerificationPolicy
    {
        public Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request, CancellationToken ct = default) =>
            Task.FromResult(MemoryVerification.NoOpinion);
    }
}
