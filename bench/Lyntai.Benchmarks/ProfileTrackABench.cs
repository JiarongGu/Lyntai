using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Prototype;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Benchmarks;

/// <summary>
/// RV27 Track A (<c>memory-profile</c>): the model-free gate a profile must pass, run on the projection over curated
/// memory (D224) over SQLite and over files, and on five deliberately broken projections over SQLite. The cases are
/// <see cref="ProfileTrackA"/>'s, linked from the test project, so this runs the code the tests pin. No model, judge
/// or embedder runs: every case asks the store and nothing else.
///
/// <para><b>Scored per bucket, never as one figure.</b> One RV8 row per case, the arm being
/// <c>&lt;projection&gt;/&lt;bucket&gt;</c> and the question the case id, so <c>bench-compare</c> pairs two runs bucket by
/// bucket. A pass is <c>hit: true</c>. The exit code is 0 when the projection fails only its
/// <see cref="ProfileTrackA.KnownGaps"/> and each broken projection fails the bucket it belongs to.</para>
/// </summary>
internal static class ProfileTrackABench
{
    private const string Metric = "case-pass";

    private static readonly (ProfileFlaw Flaw, string Bucket)[] Flaws =
    [
        (ProfileFlaw.InPlaceCorrection, "corrections-history"),
        (ProfileFlaw.RankedRead, "zero-model-read"),
        (ProfileFlaw.UncheckedCorrection, "concurrent-writes"),
        (ProfileFlaw.ReplayWritesAgain, "replayed-event"),
        (ProfileFlaw.SharedSubjects, "isolation"),
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        var results = new BenchResults("memory-profile", Metric, args);
        var runs = new List<(string Arm, string Backend, ProfileFlaw? Flaw, IReadOnlyList<CaseOutcome> Outcomes)>();

        foreach (var backend in new[] { "sqlite", "files" })
        {
            Console.WriteLine($"-- curated-projection/{backend}: {ProfileTrackA.Cases.Count} cases…");
            runs.Add(($"curated-projection/{backend}", backend, null,
                await ProfileTrackA.RunAsync(Open(backend), store => new ProfileOverCurated(store))));
        }

        foreach (var (flaw, _) in Flaws)
        {
            Console.WriteLine($"-- flawed:{flaw}/sqlite…");
            runs.Add(($"flawed:{flaw}/sqlite", "sqlite", flaw,
                await ProfileTrackA.RunAsync(Open("sqlite"), store => new ProfileFlawed(store, flaw))));
        }

        foreach (var (arm, _, _, outcomes) in runs)
            foreach (var o in outcomes)
                results.Add($"{arm}/{o.Bucket}", o.Id, o.Passed);

        Print(runs);
        await results.WriteAsync();
        return Verdict(runs);
    }

    private static void Print(List<(string Arm, string Backend, ProfileFlaw? Flaw, IReadOnlyList<CaseOutcome> Outcomes)> runs)
    {
        Console.WriteLine();
        Console.WriteLine($"{"bucket (cases passed)",-24}" + string.Concat(runs.Select(r => $"{Short(r.Arm),-30}")));
        foreach (var bucket in ProfileTrackA.Buckets)
            Console.WriteLine($"{bucket,-24}" + string.Concat(runs.Select(r =>
            {
                var inBucket = r.Outcomes.Where(o => o.Bucket == bucket).ToList();
                return $"{inBucket.Count(o => o.Passed) + "/" + inBucket.Count,-30}";
            })));

        Console.WriteLine();
        foreach (var r in runs.Where(r => r.Flaw is null))
            foreach (var f in r.Outcomes.Where(o => !o.Passed))
                Console.WriteLine($"  {r.Arm}: {f.Question}{(ProfileTrackA.IsKnownGap(f.Question, r.Backend) ? " [known gap]" : "")}: {f.Reason}");
        foreach (var r in runs.Where(r => r.Flaw is not null))
            Console.WriteLine($"  {r.Arm} fails: {string.Join(", ", r.Outcomes.Where(o => !o.Passed).GroupBy(o => o.Bucket).Select(g => $"{g.Key} ({g.Count()})"))}");
        Console.WriteLine("  a bucket is a gate, not a score: every case in it must pass, apart from the declared known gaps");
    }

    private static int Verdict(List<(string Arm, string Backend, ProfileFlaw? Flaw, IReadOnlyList<CaseOutcome> Outcomes)> runs)
    {
        var bad = new List<string>();
        foreach (var r in runs)
            if (r.Flaw is null)
            {
                bad.AddRange(r.Outcomes.Where(o => !o.Passed && !ProfileTrackA.IsKnownGap(o.Question, r.Backend))
                    .Select(o => $"{r.Arm}: {o.Question} failed and is no known gap"));
                bad.AddRange(ProfileTrackA.KnownGaps.Where(g => g.Backend == r.Backend && !g.Intermittent
                        && r.Outcomes.Any(o => o.Question == g.Question && o.Passed))
                    .Select(g => $"{r.Arm}: {g.Question} is a known gap and passed, so the allowance is stale"));
            }
            else if (!r.Outcomes.Any(o => !o.Passed && o.Bucket == Flaws.Single(f => f.Flaw == r.Flaw).Bucket))
                bad.Add($"{r.Arm}: the broken projection passed the bucket it should fail");

        foreach (var line in bad) Console.WriteLine($"  GATE: {line}");
        return bad.Count == 0 ? 0 : 1;
    }

    private static string Short(string arm) => arm.Replace("curated-projection", "projection").Replace("flawed:", "");

    private static Func<(ICuratedMemoryStore, IDisposable)> Open(string backend) => backend == "sqlite"
        ? () =>
        {
            var db = new MemoryPolicySweep.SweepDb();
            return (new SqliteCuratedMemoryStore(db.Factory), db);
        }
        : () =>
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools",
                "_profile-track-a-files", Guid.NewGuid().ToString("N")));
            var services = new ServiceCollection();
            services.AddLyntai(b => b.UseFileSystemStorage(o => o.Root = dir));
            var provider = services.BuildServiceProvider();
            return (provider.GetRequiredService<ICuratedMemoryStore>(), new FileLease(provider, dir));
        };

    private sealed class FileLease(ServiceProvider provider, string dir) : IDisposable
    {
        public void Dispose()
        {
            provider.Dispose();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { /* gitignored scratch */ }
        }
    }
}
