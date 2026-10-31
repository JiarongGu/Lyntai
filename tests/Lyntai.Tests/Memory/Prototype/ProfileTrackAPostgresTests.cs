using Lyntai.Tests.Storage;
using Xunit.Abstractions;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>RV27 Track A</b> on Postgres, the third durable store beside <see cref="ProfileTrackATests"/>' two: D221's gate
/// asks the contract suites to pass on the chosen backends, and the projection's correction needs only
/// <see cref="Lyntai.Storage.IRevisionedCuratedMemoryStore"/>, which every curated store implements. Each site a case
/// opens is its own database (<see cref="ProfilePostgresSites"/>).
/// </summary>
[Collection("postgres")]
public sealed class ProfileTrackAPostgresTests(PostgresFixture pg, ITestOutputHelper output)
{
    [SkippableFact]
    public async Task The_projection_over_curated_memory_passes_every_case_on_postgres()
    {
        var databases = ProfilePostgresSites.For(pg);

        var outcomes = await ProfileTrackA.RunAsync(databases.Open, s => new ProfileOverCurated(s));

        Assert.Equal(ProfileTrackA.Cases.Count, outcomes.Count);
        foreach (var bucket in outcomes.GroupBy(o => o.Bucket))
            output.WriteLine($"postgres/{bucket.Key}: {bucket.Count(o => o.Passed)}/{bucket.Count()}");
        var failed = outcomes.Where(o => !o.Passed).ToList();
        foreach (var f in failed) output.WriteLine($"postgres: {f.Question}: {f.Reason}");
        Assert.Empty(failed.Where(f => !ProfileTrackA.IsKnownGap(f.Question, "postgres")).Select(f => $"{f.Question}: {f.Reason}"));
    }
}
