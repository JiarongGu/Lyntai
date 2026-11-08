using Dapper;
using Lyntai.Storage.Postgres;

namespace Lyntai.Tests.Storage;

/// <summary>An unchecked update changes only the fields it names, though another writer commits a different one
/// while it runs. Postgres reads the row first (READ COMMITTED) and writes every column back, so the read must
/// hold the row, or a field the update never named is written back from a stale read.</summary>
[Collection("postgres")]
public sealed class PostgresCuratedUpdateRaceTests(PostgresFixture pg)
{
    [SkippableFact]
    public async Task An_unchecked_update_does_not_write_back_a_field_it_did_not_name()
    {
        var factory = pg.Require();
        var store = new PostgresCuratedMemoryStore(factory);
        var id = await store.AddAsync("race", "old", taskKey: $"race-{Guid.NewGuid():N}");

        // another writer holds an uncommitted content edit while the toggle starts…
        await using var other = await factory.OpenAsync();
        await using var tx = await other.BeginTransactionAsync();
        await other.ExecuteAsync("UPDATE lyntai_curated_memory SET content = 'X', revision = revision + 1 WHERE id = @id",
            new { id }, tx);

        var toggle = Task.Run(() => store.UpdateAsync(id, enabled: false));
        await Task.Delay(750); // long enough for an unlocked toggle to have read 'old'
        await tx.CommitAsync();
        Assert.True(await toggle);

        var after = (await store.GetAsync(id))!;
        Assert.Equal("X", after.Content);
        Assert.False(after.Enabled);
    }
}
