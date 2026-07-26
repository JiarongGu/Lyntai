using Lyntai.Storage;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="ScoreStoreContract"/> method as a [Fact] — derive with a store factory and
/// the whole contract runs on that backend automatically (T11: no silent skips). Postgres deliberately
/// does NOT derive: its table-wide Aggregate/Export methods can't run on the shared container, so it
/// keeps a session-scoped subset (see <c>PostgresStorageTests</c>).</summary>
public abstract class ScoreStoreContractFacts
{
    protected abstract IScoreStore NewStore();

    [Fact] public Task Rescore_replaces() => ScoreStoreContract.Rescore_replaces_not_accumulates(NewStore());
    [Fact] public Task Aggregate() => ScoreStoreContract.Aggregate_is_per_scorer_across_sessions(NewStore());
    [Fact] public Task Export() => ScoreStoreContract.Export_dumps_every_session_scorer_score(NewStore());
}

/// <summary>The <see cref="ScoreStoreContract"/> against the InMemory backend.</summary>
public class InMemoryScoreStoreTests : ScoreStoreContractFacts
{
    protected override IScoreStore NewStore() => new InMemoryScoreStore();
}
