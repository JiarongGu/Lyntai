using Dapper;
using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>The count-cap statement (<see cref="MemoryEviction.CapEvictSql"/>) run on SQLite against rows with
/// chosen timestamps, because the store's own clock cannot produce the ties, the null last-access and the
/// expiry-at-now boundary that decide which rows go. The statement text is shared with Postgres, whose leg
/// of the memory-store contract runs it; these cases hold the SQLite side to <see cref="MemoryEviction.Survivors"/>.</summary>
public sealed class MemoryCapEvictionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    private sealed record Seed(DateTimeOffset CreatedAt, DateTimeOffset? LastAccessedAt, DateTimeOffset? ExpiresAt);

    [Fact]
    public async Task The_survivors_are_exactly_those_of_the_shared_computation()
    {
        var rng = new Random(20261028);
        // few distinct ticks, so recency ties are common and the id tie-break is what decides
        DateTimeOffset Tick() => Now.AddMinutes(-rng.Next(0, 6));
        for (var run = 0; run < 400; run++)
        {
            var mode = run % 2 == 0 ? MemoryEvictionMode.Fifo : MemoryEvictionMode.Lru;
            var seeds = Enumerable.Range(0, rng.Next(1, 31)).Select(_ => new Seed(
                Tick(),
                rng.Next(3) == 0 ? null : Tick(),
                rng.Next(4) switch
                {
                    0 => Now,                        // expires AT now: expired, as `expires_at > @now` says
                    1 => Now.AddMinutes(-rng.Next(1, 6)),
                    2 => Now.AddMinutes(rng.Next(1, 6)),
                    _ => (DateTimeOffset?)null,
                })).ToList();
            var cap = rng.Next(1, 36);
            var task = $"t{run}";

            var ids = await InsertAsync(task, "s", seeds);
            var bystander = await InsertAsync(task, "other", seeds); // another scope of the same task must not move
            await InsertAsync(task + "x", "s", seeds);               // nor the same scope of another task

            await EvictAsync(task, "s", cap, mode);

            var rows = ids.Zip(seeds, (id, s) => new MemoryEviction.Row(id, s.CreatedAt, s.LastAccessedAt ?? s.CreatedAt, s.ExpiresAt, 1));
            var policy = new MemoryEvictionPolicy { MaxEntriesPerScope = cap, Mode = mode };
            var expected = MemoryEviction.Survivors(policy, rows, Now);

            Assert.True(expected.SetEquals(await IdsAsync(task, "s")), $"run {run}: {mode}, cap {cap}, {seeds.Count} rows");
            Assert.Equal(bystander.Count, (await IdsAsync(task, "other")).Count);
            Assert.Equal(seeds.Count, (await IdsAsync(task + "x", "s")).Count);
        }
    }

    [Theory]
    [InlineData(MemoryEvictionMode.Fifo)]
    [InlineData(MemoryEvictionMode.Lru)]
    public async Task A_scope_at_or_under_its_cap_deletes_nothing_even_when_some_entries_are_expired(MemoryEvictionMode mode)
    {
        var seeds = new[]
        {
            new Seed(Now.AddMinutes(-3), null, Now.AddMinutes(-1)), // expired, but a scope under its cap keeps it
            new Seed(Now.AddMinutes(-2), Now.AddMinutes(-1), null),
            new Seed(Now.AddMinutes(-1), null, Now.AddMinutes(5)),
        };
        var ids = await InsertAsync("under", "s", seeds);

        await EvictAsync("under", "s", cap: 4, mode);
        Assert.Equal(ids, await IdsAsync("under", "s"));

        await EvictAsync("under", "s", cap: 3, mode); // exactly at the cap
        Assert.Equal(ids, await IdsAsync("under", "s"));

        await EvictAsync("under", "s", cap: int.MaxValue, mode);
        Assert.Equal(ids, await IdsAsync("under", "s"));
    }

    [Fact]
    public async Task A_scope_far_over_its_cap_is_trimmed_to_it_in_one_statement()
    {
        // cap lowered after the scope filled: the excess is not one row
        var seeds = Enumerable.Range(0, 20).Select(i => new Seed(Now.AddMinutes(-i), null, null)).ToList();
        await InsertAsync("over", "s", seeds);

        await EvictAsync("over", "s", cap: 5, MemoryEvictionMode.Fifo);

        var left = await IdsAsync("over", "s");
        Assert.Equal(5, left.Count);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => (long)i), left.Order()); // the five newest were written first
    }

    private async Task<List<long>> InsertAsync(string task, string scope, IEnumerable<Seed> seeds)
    {
        await using var conn = await _db.Factory.OpenAsync();
        var ids = new List<long>();
        foreach (var (s, i) in seeds.Select((s, i) => (s, i)))
            ids.Add(await conn.ExecuteScalarAsync<long>("""
                INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at, last_accessed_at, expires_at)
                VALUES (@task, @scope, @content, @createdAt, @lastAccessedAt, @expiresAt) RETURNING id
                """, new
            {
                task, scope, content = $"fact {i}",
                createdAt = s.CreatedAt, lastAccessedAt = s.LastAccessedAt, expiresAt = s.ExpiresAt,
            }));
        return ids;
    }

    private async Task EvictAsync(string task, string scope, int cap, MemoryEvictionMode mode)
    {
        await using var conn = await _db.Factory.OpenAsync();
        await conn.ExecuteAsync(MemoryEviction.CapEvictSql(mode), new { taskKey = task, scope, cap, now = Now });
    }

    private async Task<List<long>> IdsAsync(string task, string scope)
    {
        await using var conn = await _db.Factory.OpenAsync();
        return [.. await conn.QueryAsync<long>(
            "SELECT id FROM lyntai_memory_entry WHERE task_key = @task AND scope = @scope ORDER BY id", new { task, scope })];
    }
}
