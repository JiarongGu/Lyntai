using BenchmarkDotNet.Attributes;
using Lyntai;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Benchmarks;

/// <summary>FTS5-trigram recall latency at scale — how memory recall holds up as the store grows.
/// The db is seeded once per row-count parameter; each benchmark is a single recall query.</summary>
[MemoryDiagnoser]
public class MemoryRecallBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int Rows;

    private SqliteMemoryStore _store = null!;
    private string _dbPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lyntai-bench-{Rows}-{Guid.NewGuid():N}.db");
        MigrationRunnerService.MigrateUp(_dbPath);
        var factory = new SqliteConnectionFactory(_dbPath);
        _store = new SqliteMemoryStore(factory, new LyntaiOptions { MemoryCapPerScope = int.MaxValue });

        // seed Rows entries across 50 tasks; one task carries the needle we recall
        for (var i = 0; i < Rows; i++)
        {
            var task = i % 50 == 0 ? "needle-task" : $"task-{i % 50}";
            var content = i % 50 == 0
                ? $"the quarterly deployment checklist item number {i} requires manual approval"
                : $"unrelated filler content row {i} lorem ipsum dolor sit amet";
            _store.RememberAsync(task, "scope", content).GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Benchmark]
    public async Task<int> RecallByTrigramMatch()
    {
        var hits = await _store.RecallAsync("needle-task", query: "deployment checklist", limit: 20);
        return hits.Count;
    }

    [Benchmark]
    public async Task<int> RecallMostRecent()
    {
        var hits = await _store.RecallAsync("needle-task", limit: 20);
        return hits.Count;
    }
}
