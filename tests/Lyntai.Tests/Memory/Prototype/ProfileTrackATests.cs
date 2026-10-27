using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;
using Xunit.Abstractions;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>RV27 Track A</b>, pinned: every case passes on D224's projection over each durable store, each bucket reported
/// apart, and a projection with one deliberate flaw fails the bucket that flaw belongs to. The bench
/// (<c>memory-profile</c>) runs the same cases and writes the result; this is what keeps the gate honest in
/// <c>verify</c>, because a gate no broken projection can fail measures nothing.
/// </summary>
public class ProfileTrackATests(ITestOutputHelper output)
{
    public static TheoryData<string> Backends => ["sqlite", "files"];

    public static TheoryData<string, string> Flaws => new()
    {
        { nameof(ProfileFlaw.InPlaceCorrection), "corrections-history" },
        { nameof(ProfileFlaw.RankedRead), "zero-model-read" },
        { nameof(ProfileFlaw.UncheckedCorrection), "concurrent-writes" },
        { nameof(ProfileFlaw.ReplayWritesAgain), "replayed-event" },
        { nameof(ProfileFlaw.SharedSubjects), "isolation" },
    };

    private static Func<(ICuratedMemoryStore, IDisposable)> Open(string backend) => backend == "sqlite"
        ? () =>
        {
            var db = new TempDb();
            return (new SqliteCuratedMemoryStore(db.Factory), db);
        }
        : () =>
        {
            var root = new TempRoot();
            return (new FileSystemCuratedMemoryStore(root.Root), root);
        };

    [Fact]
    public void Every_case_belongs_to_a_named_bucket_and_every_bucket_has_cases()
    {
        Assert.Equal(ProfileTrackA.Buckets.Order(), ProfileTrackA.Cases.Select(c => c.Bucket).Distinct().Order());
        Assert.Equal(ProfileTrackA.Cases.Count, ProfileTrackA.Cases.Select(c => c.Question).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task The_projection_over_curated_memory_fails_exactly_its_declared_gaps(string backend)
    {
        var outcomes = await ProfileTrackA.RunAsync(Open(backend), s => new ProfileOverCurated(s));

        foreach (var bucket in outcomes.GroupBy(o => o.Bucket))
            output.WriteLine($"{backend}/{bucket.Key}: {bucket.Count(o => o.Passed)}/{bucket.Count()}");
        var failed = outcomes.Where(o => !o.Passed).ToList();
        foreach (var f in failed) output.WriteLine($"{backend}: {f.Question}: {f.Reason}");
        Assert.Empty(failed.Where(f => !ProfileTrackA.IsKnownGap(f.Question, backend)).Select(f => $"{f.Question}: {f.Reason}"));
        Assert.Empty(ProfileTrackA.KnownGaps.Where(g => g.Backend == backend && !g.Intermittent).Select(g => g.Question)
            .Except(failed.Select(f => f.Question)));
    }

    [Theory]
    [MemberData(nameof(Flaws))]
    public async Task A_projection_with_one_flaw_fails_the_bucket_that_flaw_belongs_to(string flawName, string bucket)
    {
        var flaw = Enum.Parse<ProfileFlaw>(flawName);
        var outcomes = await ProfileTrackA.RunAsync(Open("sqlite"), s => new ProfileFlawed(s, flaw));

        var failed = outcomes.Where(o => !o.Passed).ToList();
        foreach (var f in failed) output.WriteLine($"{flaw}: {f.Question}: {f.Reason}");
        Assert.Contains(bucket, failed.Select(o => o.Bucket));
    }
}
