using Lyntai.Inference.Budgeting;
using Lyntai.Inference.Caching;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Jobs;
using Lyntai.Tests.Memory;

namespace Lyntai.Tests.Storage;

// Every storage contract against the real Postgres container, one suite per contract, each inheriting its
// contract's theory so a fact added to the contract runs here unasked. The container is ONE database for the
// whole run: each suite SharesItsStore, so every key is unique to its case and an [Unscoped] fact clears the
// suite's tables first. Every case SKIPS when Docker is down (PostgresFixture.Require).

[Collection("postgres")]
public sealed class PostgresKeyValueStoreContractTests(PostgresFixture pg) : KeyValueStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IKeyValueStore NewStore() => new PostgresKeyValueStore(pg.Require());
}

[Collection("postgres")]
public sealed class PostgresConversationStoreContractTests(PostgresFixture pg) : ConversationStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IConversationStore NewStore() => new PostgresConversationStore(pg.Require());
}

[Collection("postgres")]
public sealed class PostgresTraceStoreContractTests(PostgresFixture pg) : TraceStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override ITraceStore NewStore() => new PostgresTraceStore(pg.Require());
}

[Collection("postgres")]
public sealed class PostgresPromptVersionStoreContractTests(PostgresFixture pg) : PromptVersionStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IPromptVersionStore NewStore() => new PostgresPromptVersionStore(pg.Require());
}

[Collection("postgres")]
public sealed class PostgresScoreStoreContractTests(PostgresFixture pg) : ScoreStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IScoreStore NewStore() => new PostgresScoreStore(pg.Require());
    protected override Task ClearAsync() => pg.ClearAsync("lyntai_score_result");
}

[Collection("postgres")]
public sealed class PostgresMemoryStoreContractTests(PostgresFixture pg) : MemoryStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IMemoryStore NewWith(MemoryEvictionPolicy p) =>
        new PostgresMemoryStore(pg.Require(), OptionsWith(p), clock: Clock.Get);
}

[Collection("postgres")]
public sealed class PostgresCuratedMemoryStoreContractTests(PostgresFixture pg) : CuratedMemoryStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override ICuratedMemoryStore NewStore() => new PostgresCuratedMemoryStore(pg.Require());
}

[Collection("postgres")]
public sealed class PostgresJobStoreContractTests(PostgresFixture pg) : JobStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IJobStore New() => new PostgresJobStore(pg.Require(), Clock.Get);
    protected override Task ClearAsync() => pg.ClearAsync("lyntai_job_slot");
}

[Collection("postgres")]
public sealed class PostgresVectorStoreContractTests(PostgresFixture pg) : VectorStoreContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IVectorStore New() => new PostgresVectorStore(pg.Require());
}

[Collection("postgres")]
public sealed class PostgresResponseCacheContractTests(PostgresFixture pg) : ResponseCacheContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IResponseCache NewCache() => new PostgresResponseCache(pg.Require(), new LyntaiOptions(), Clock.Get);
}

[Collection("postgres")]
public sealed class PostgresUsageTrackerContractTests(PostgresFixture pg) : UsageTrackerContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IUsageTracker NewTracker() => new PostgresUsageTracker(pg.Require());
    protected override IUsageTracker Reopen(IUsageTracker tracker) => new PostgresUsageTracker(pg.Factory);
    protected override Task ClearAsync() => pg.ClearAsync("lyntai_usage_reservation", "lyntai_usage");
}

[Collection("postgres")]
public sealed class PostgresUsageReservationContractTests(PostgresFixture pg) : UsageReservationContractFacts
{
    protected override bool SharesItsStore => true;
    protected override IReservingUsageTracker NewTracker() => new PostgresUsageTracker(pg.Require());
    protected override Task ClearAsync() => pg.ClearAsync("lyntai_usage_reservation", "lyntai_usage");
}
