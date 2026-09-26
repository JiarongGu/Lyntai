namespace Lyntai.Tests.Inference;

/// <summary>A call with no consumer to name still sits under the host's <c>"default"</c> timeout tier, which
/// <see cref="LyntaiOptions.TimeoutByConsumer"/> documents as applying whenever a tag has no entry — the
/// one-argument overload included, or the two CLI agent sessions ignore a host's default timeout.</summary>
public class ConsumerlessTimeoutTests
{
    [Fact]
    public void A_call_with_no_consumer_honours_the_default_tier()
    {
        var options = new LyntaiOptions { ProviderTimeout = TimeSpan.FromMinutes(2) };
        options.TimeoutByConsumer["default"] = TimeSpan.FromSeconds(7);

        Assert.Equal(TimeSpan.FromSeconds(7), options.ResolveTimeout((int?)null));
        Assert.Equal(options.ResolveTimeout(null, consumer: null), options.ResolveTimeout((int?)null));
    }

    [Fact]
    public void Explicit_seconds_still_win_clamped_to_the_ceiling()
    {
        var options = new LyntaiOptions { MaxProviderTimeout = TimeSpan.FromSeconds(30) };
        options.TimeoutByConsumer["default"] = TimeSpan.FromSeconds(7);

        Assert.Equal(TimeSpan.FromSeconds(5), options.ResolveTimeout(5));
        Assert.Equal(TimeSpan.FromSeconds(30), options.ResolveTimeout(600));
    }
}
