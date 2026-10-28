using System.Diagnostics;
using Lyntai.Memory;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;

namespace Lyntai.Benchmarks;

/// <summary>
/// What one linked write and one recall write-back cost on <see cref="PostgresMemoryGraphStore"/> as the store
/// grows — the Postgres half of the commit fix <c>corpus-cost-before-after</c> measured on SQLite.
///
/// <para>Store level, not engine level: it times the two calls the engine makes per write and per recall
/// (five symmetric edges; ten touches, ten edges), over nodes already stored. Needs a migrated-on-demand
/// Postgres with pgvector's image, named by <c>LYNTAI_LIVE_POSTGRES</c>; this sweep owns no container.
/// Run: <c>dotnet run -c Release --project bench/Lyntai.Benchmarks -- --pg-graph-write [--sizes 3000,6000]</c>.</para>
/// </summary>
internal static class PostgresGraphWriteSweep
{
    private const int Timed = 200;
    private const int Warmup = 20;
    private const string Engine = "pgwrite";

    public static async Task<int> RunAsync(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LYNTAI_LIVE_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Set LYNTAI_LIVE_POSTGRES to a Postgres connection string.");
            return 2;
        }

        var sizes = new[] { 3000, 6000 };
        var at = Array.IndexOf(args, "--sizes");
        if (at >= 0 && at + 1 < args.Length) sizes = [.. args[at + 1].Split(',').Select(int.Parse)];

        MigrationRunnerService.MigrateUp(connectionString);
        var store = new PostgresMemoryGraphStore(new PostgresConnectionFactory(connectionString));
        var task = $"t{Guid.NewGuid():N}";
        var ids = new List<long>();
        var rng = new Random(42);

        Console.WriteLine("entries | linked write p50 ms (5 symmetric edges) | write-back p50 ms (10 touches + 10 edges)");
        foreach (var size in sizes)
        {
            while (ids.Count < size)
                ids.Add(await store.UpsertAsync(new GraphNodeWrite(Engine, task, "s", $"h{ids.Count}",
                    $"entry number {ids.Count} about topic {ids.Count % 97}", MemoryGrade.Associative, 10, 1, null)));

            long Pick() => ids[rng.Next(ids.Count)];

            var linked = await TimeAsync(() => store.LinkManyAsync(Engine,
                [.. Enumerable.Range(0, 5).Select(_ => new GraphEdgeWrite(Pick(), Pick(), null, 1, true))]));
            var writeBack = await TimeAsync(() => store.WriteBackAsync(Engine, new GraphWriteBack(
                [.. Enumerable.Range(0, 10).Select(_ => new GraphTouch(Pick(), 12))],
                [.. Enumerable.Range(0, 10).Select(_ => new GraphEdgeWrite(Pick(), Pick(), null, 1, false))],
                [], 0)));
            Console.WriteLine($"{size,7} | {linked,8:F1} | {writeBack,8:F1}");
        }

        await store.ForgetAsync(Engine, task, null);
        return 0;
    }

    private static async Task<double> TimeAsync(Func<Task> op)
    {
        for (var i = 0; i < Warmup; i++) await op();
        var ms = new List<double>(Timed);
        for (var i = 0; i < Timed; i++)
        {
            var t = Stopwatch.GetTimestamp();
            await op();
            ms.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        ms.Sort();
        return ms[ms.Count / 2];
    }
}
