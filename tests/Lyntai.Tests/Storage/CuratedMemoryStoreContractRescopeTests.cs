using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>CMEM7 — the in-place re-scope pair (<see cref="CuratedMemoryStoreContract.Update_can_rescope_task_and_scope_in_place"/>
/// and <see cref="CuratedMemoryStoreContract.Update_refuses_an_identity_collision"/>) on every backend.
/// Same no-silent-skips shape as <see cref="CuratedMemoryStoreContractFacts"/>: derive with a store factory
/// and both cases run. Kept in its own file because the pair is ALSO routed to Postgres below — the collision
/// refusal is the load-bearing half and each backend answers it with different SQL (SQLite <c>IS</c>,
/// Postgres <c>IS NOT DISTINCT FROM</c>, InMemory ordinal <c>==</c>), so no backend may sit this one out.</summary>
public abstract class CuratedMemoryRescopeFacts
{
    protected abstract ICuratedMemoryStore NewStore();

    [Fact] public Task Rescope_in_place() => CuratedMemoryStoreContract.Update_can_rescope_task_and_scope_in_place(NewStore());
    [Fact] public Task Refuse_collision() => CuratedMemoryStoreContract.Update_refuses_an_identity_collision(NewStore());
}

/// <summary>CMEM7 against the InMemory backend.</summary>
public class InMemoryCuratedMemoryRescopeTests : CuratedMemoryRescopeFacts
{
    protected override ICuratedMemoryStore NewStore() => new InMemoryCuratedMemoryStore();
}

/// <summary>CMEM7 against SQLite over a per-test temp db.</summary>
public class SqliteCuratedMemoryRescopeTests : CuratedMemoryRescopeFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override ICuratedMemoryStore NewStore() => new SqliteCuratedMemoryStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}

/// <summary>CMEM7 against Postgres on the shared migrated container — the third SQL dialect of the
/// null-safe identity compare. Skips as a whole when Docker is unavailable, like the rest of that
/// collection.</summary>
[Collection("postgres")]
public sealed class PostgresCuratedMemoryRescopeTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");

    [SkippableFact]
    public async Task Curated_memory_rescope_and_collision_refusal()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        // Unique tasks/kinds so the shared container doesn't cross-contaminate the absolute-count asserts.
        await CuratedMemoryStoreContract.Update_can_rescope_task_and_scope_in_place(store, Uid() + "-from", Uid() + "-to");
        await CuratedMemoryStoreContract.Update_refuses_an_identity_collision(store, Uid() + "-cl", Uid() + "-k", Uid() + "-ok");
    }
}
