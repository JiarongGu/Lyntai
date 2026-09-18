using Lyntai;
using Lyntai.Inference.Caching;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="ResponseCacheContract"/> method as a [Fact] — derive with a cache factory and
/// the WHOLE contract runs. Postgres does not derive; it delegates the same facts against the shared
/// container, and <see cref="PostgresContractCoverageTests"/> is what makes that structural.</summary>
public abstract class ResponseCacheContractFacts
{
    /// <summary>A cache plus the clock it reads, since two of the facts are about TIME passing and the
    /// clock is a constructor parameter on every shipped implementation.</summary>
    protected abstract (IResponseCache Cache, MutableClock Clock) NewCache();

    private Task Run(Func<IResponseCache, string, Action<TimeSpan>, Task> fact)
    {
        var (cache, clock) = NewCache();
        return fact(cache, "k", clock.Advance);
    }

    [Fact] public Task Round_trip() => Run(ResponseCacheContract.A_stored_reply_round_trips_with_its_usage);
    [Fact] public Task Miss() => Run(ResponseCacheContract.A_key_that_was_never_set_is_a_MISS);
    [Fact] public Task Ttl() => Run(ResponseCacheContract.An_entry_past_its_TTL_is_a_MISS);
    [Fact] public Task Remove_one() => Run(ResponseCacheContract.Remove_evicts_ONE_entry_and_leaves_the_others);
    [Fact] public Task Remove_absent() => Run(ResponseCacheContract.Removing_a_key_that_is_not_there_is_a_NO_OP);
}

/// <summary>The <see cref="ResponseCacheContract"/> against the in-process default.</summary>
public class InMemoryResponseCacheContractTests : ResponseCacheContractFacts
{
    protected override (IResponseCache, MutableClock) NewCache()
    {
        var clock = new MutableClock();
        return (new InMemoryResponseCache(new LyntaiOptions(), clock.Get), clock);
    }
}

/// <summary>The <see cref="ResponseCacheContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteResponseCacheContractTests : ResponseCacheContractFacts, IDisposable
{
    private readonly TempDb _db = new();

    protected override (IResponseCache, MutableClock) NewCache()
    {
        var clock = new MutableClock();
        return (new SqliteResponseCache(_db.Factory, new LyntaiOptions(), clock.Get), clock);
    }

    public void Dispose() => _db.Dispose();
}
