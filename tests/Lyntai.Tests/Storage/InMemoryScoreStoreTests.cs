using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="ScoreStoreContract"/> against the InMemory backend.</summary>
public class InMemoryScoreStoreTests
{
    private static InMemoryScoreStore New() => new();

    [Fact] public Task Rescore_replaces() => ScoreStoreContract.Rescore_replaces_not_accumulates(New());
    [Fact] public Task Aggregate() => ScoreStoreContract.Aggregate_is_per_scorer_across_sessions(New());
    [Fact] public Task Export() => ScoreStoreContract.Export_dumps_every_session_scorer_score(New());
}
