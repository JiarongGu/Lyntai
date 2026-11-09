using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage.FileSystem;

namespace Lyntai.Tests.Storage;

/// <summary>A write never evicts the row it stored. A scope that is already over its cap (the cap was lowered
/// after it filled) re-remembers its OLDEST fact on a fixed clock: every row ties on recency, and the id
/// tie-break used to take the row just written.</summary>
public static class MemoryWrittenRowEviction
{
    public static IEnumerable<object[]> Policies() =>
    [
        [MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Fifo)],
        [MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru)],
        [MemoryEvictionPolicy.SizeBudget(10)],
    ];

    public static readonly DateTimeOffset Tick = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    public static async Task Run(IMemoryStore store, LyntaiOptions options, string key, MemoryEvictionPolicy lowered)
    {
        options.MemoryEviction = MemoryEvictionPolicy.Manual;
        foreach (var i in Enumerable.Range(1, 4)) await store.RememberAsync(key, "s", $"fact-{i}");

        options.MemoryEviction = lowered;
        await store.RememberAsync(key, "s", "fact-1"); // the lowest id, tied with every other row on the fixed clock

        var kept = (await store.RecallAsync(key)).Select(e => e.Content).ToHashSet();
        Assert.Contains("fact-1", kept);
        Assert.True(kept.Count <= 2, $"the scope was trimmed to its bound, not left at {kept.Count}");
    }

    public static LyntaiOptions Options() => new() { MemoryRecallLimit = 100 };
}

public sealed class InMemoryMemoryWrittenRowEvictionTests
{
    [Theory, MemberData(nameof(Policies))]
    public Task The_row_being_written_survives(MemoryEvictionPolicy lowered)
    {
        var options = MemoryWrittenRowEviction.Options();
        return MemoryWrittenRowEviction.Run(
            new InMemoryMemoryStore(options, () => MemoryWrittenRowEviction.Tick), options, "k", lowered);
    }

    public static IEnumerable<object[]> Policies() => MemoryWrittenRowEviction.Policies();
}

public sealed class FileSystemMemoryWrittenRowEvictionTests : IDisposable
{
    private readonly TempRoot _root = new();
    public void Dispose() => _root.Dispose();

    [Theory, MemberData(nameof(Policies))]
    public Task The_row_being_written_survives(MemoryEvictionPolicy lowered)
    {
        var options = MemoryWrittenRowEviction.Options();
        return MemoryWrittenRowEviction.Run(
            new FileSystemMemoryStore(_root.Root, options, () => MemoryWrittenRowEviction.Tick), options, "k", lowered);
    }

    public static IEnumerable<object[]> Policies() => MemoryWrittenRowEviction.Policies();
}

public sealed class SqliteMemoryWrittenRowEvictionTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    [Theory, MemberData(nameof(Policies))]
    public Task The_row_being_written_survives(MemoryEvictionPolicy lowered)
    {
        var options = MemoryWrittenRowEviction.Options();
        return MemoryWrittenRowEviction.Run(
            new SqliteMemoryStore(_db.Factory, options, clock: () => MemoryWrittenRowEviction.Tick), options, "k", lowered);
    }

    public static IEnumerable<object[]> Policies() => MemoryWrittenRowEviction.Policies();
}

[Collection("postgres")]
public sealed class PostgresMemoryWrittenRowEvictionTests(PostgresFixture pg)
{
    [SkippableTheory, MemberData(nameof(Policies))]
    public Task The_row_being_written_survives(MemoryEvictionPolicy lowered)
    {
        var options = MemoryWrittenRowEviction.Options();
        return MemoryWrittenRowEviction.Run(
            new PostgresMemoryStore(pg.Require(), options, clock: () => MemoryWrittenRowEviction.Tick),
            options, "written-" + Guid.NewGuid().ToString("N"), lowered);
    }

    public static IEnumerable<object[]> Policies() => MemoryWrittenRowEviction.Policies();
}
