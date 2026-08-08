# Graph memory, part B (MEM2b) — SQL backends Implementation Plan

> **Status: EXECUTED 2026-08-08, and its SQL is SUPERSEDED.** Outcome and the three findings in
> `docs/task-archive.md` **Part 48**. The date arithmetic shown here (`julianday`,
> `EXTRACT(EPOCH …)`) was removed by **Part 50**, which made age a subtraction on a logical position; the
> schema also gained `lyntai_memory_position`. Kept as the record of what was planned — read the shipped
> migrations for the current schema.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** `IMemoryGraphStore` on SQLite and Postgres, held to the contract MEM2a already wrote — so graph
memory persists instead of living only in process.

**Architecture:** Two parallel hand-written implementations, deliberately not sharing SQL. Adding a backend
is a new test class deriving `MemoryGraphStoreContract` plus a store; nothing in Core or the engine changes.

**Tech Stack:** .NET 10, Dapper, FluentMigrator, Microsoft.Data.Sqlite, Npgsql, xUnit + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-08-graph-memory-engine-design.md` §8. **Task:** `TASKS.md` MEM2b.
**Depends on:** MEM2a (shipped 2026-08-08, `docs/task-archive.md` Part 47).

**Docker is available on this machine (verified 2026-08-08, server 29.5.3)**, so the Postgres half is
genuinely verified rather than shipped unmeasured. If a later run cannot reach Docker, its tests SKIP — and
a skipped Postgres suite must be reported as skipped, never as a pass.

## Global Constraints

- **No new package.** SQLite work in `src/Lyntai.Storage.Sqlite/`, Postgres in `src/Lyntai.Storage.Postgres/`.
- **Purely additive.** Nothing released changes; the contract and the engine are untouched.
- **`lyntai_` prefix on every table, index and trigger** — Lyntai may share a database with the consumer's
  own schema, and `Every_object_carries_the_lyntai_prefix` fails otherwise.
- **Both migration tags, always:** `[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]`. An
  untagged migration runs under *every* feature set and lands a table for a domain the app disabled.
- **Never reuse a migration number.** `202608081215` is already scaffolded and unique; the Postgres leg
  carries the SAME number as its SQLite twin, matching every existing pair.
- **`CAST(x AS REAL)` on every float read.** SQLite stores `1.0` as INTEGER and `0.5` as REAL in the same
  column; Dapper will hand a `double` property a boxed `long`.
- **Rows land in settable-property row types**, projected to the immutable records. Never a positional
  record constructor, and never named `*Dto`.
- **Do not share SQL between the two backends.** `storage.md`: the contract facts are the deduplication
  mechanism; an extraction needing `bool isSqlite` is the signal to stop.
- **Running tests:** `node devtools/dev.mjs test --filter "FullyQualifiedName~Graph"`. Read the
  matched/total count — a filter matching zero passes vacuously.
- Branch is `feat/memory-engine-seam`.

## Schema

```
lyntai_memory_node                         lyntai_memory_edge
  id               PK                        from_id  FK -> node ON DELETE CASCADE
  engine           TEXT                      to_id    FK -> node ON DELETE CASCADE
  task_key, scope  TEXT                      kind     TEXT NOT NULL DEFAULT ''
  headline         TEXT                      weight   REAL
  content          TEXT                      strengthened_at
  content_hash     TEXT   -- dedup           PRIMARY KEY (from_id, to_id, kind)
  grade            INTEGER -- MemoryGrade: 1 = associative, 2 = authoritative
  metadata         TEXT NULL
  created_at, last_recalled_at
  recall_count     INTEGER
  stability        REAL   -- half-life, days
  UNIQUE (engine, task_key, scope, content_hash)
```

`content_hash` is the SHA-256 of the content, computed in app code — a unique index over full content text
would be large and, on SQLite, capped.

## Three things the SQL must get right

**1. No exponent, ever.** The candidate filter is plain division against the cutoff the policy supplies:

```sql
-- SQLite
(julianday(@now) - julianday(n.last_recalled_at)) / MAX(CAST(n.stability AS REAL), 0.000001) <= @cut
-- Postgres
EXTRACT(EPOCH FROM (@now - n.last_recalled_at)) / 86400.0 / GREATEST(n.stability, 0.000001) <= @cut
```

The `MAX`/`GREATEST` guard matters: a zero stability divides by zero, which SQLite evaluates to NULL — and a
NULL predicate **excludes the row silently**, losing a memory rather than erroring.

**2. Authoritative nodes bypass both filters.** `grade = 1 OR <query match>` and `grade = 1 OR <cutoff>`.
An exact fact is never excluded by a query it does not match or by age.

**3. `Strength` and `StrengthAsOf` are plain aggregates.** `SUM(weight)` and `MAX(strengthened_at)` over the
node's outgoing edges, computed with correlated subqueries. The store applies no decay — the policy decays
the aggregate, which is the documented over-estimate (spec §3.0).

**`Relevance`, and why it is rank-derived.** `GraphNode.Relevance` is contractually 0..1, but SQLite's
`bm25()` returns an unbounded negative score and Postgres ranks by recency. Rather than inventing a
normalization, both SQL backends report **the backend's own rank position, normalized**:
`Relevance = 1 - i / count` for the *i*-th row of their own ordering. That is a monotone transform of the
backend's ranking, so multiplying it into the engine's `Rank` preserves that ranking exactly, and it stays
inside the contract. Document it on both stores. InMemory reports 1 for every row, as it already does.

---

### Task 1: SQLite — migration and store

**Files:**
- Modify: `src/Lyntai.Storage.Sqlite/Migrations/M202608081215_MemoryGraph.cs` (scaffolded; the placeholder
  tag deliberately does not compile)
- Create: `src/Lyntai.Storage.Sqlite/SqliteMemoryGraphStore.cs`
- Modify: `src/Lyntai.Storage.Sqlite/SqliteStorageBuilderExtensions.cs`
- Test: `tests/Lyntai.Tests/Memory/SqliteMemoryGraphStoreTests.cs`

**Interfaces:**
- Consumes: `IMemoryGraphStore`, `GraphNode`, `GraphNodeWrite`, `GraphTouch`, `GraphNeighbour`,
  `MemoryGrade` (Core); `IDbConnectionFactory`, `FtsQuery.Build`, `LikePattern.Contains` (existing).
- Produces: `SqliteMemoryGraphStore(IDbConnectionFactory factory, ILogger<SqliteMemoryGraphStore>? logger)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against SQLite over a per-test temp db.</summary>
public class SqliteMemoryGraphStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    private IMemoryGraphStore New() => new SqliteMemoryGraphStore(_db.Factory);

    [Fact] public Task Seed() => MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(New(), "k1");
    [Fact] public Task Dedup() => MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(New(), "k2");
    [Fact] public Task Engine_isolation() => MemoryGraphStoreContract.Engines_are_isolated_from_one_another(New(), "k3");
    [Fact] public Task Cutoff_excludes() => MemoryGraphStoreContract.The_candidate_cutoff_excludes_stale_associative_nodes(New(), "k4");
    [Fact] public Task Cutoff_spares_exact() => MemoryGraphStoreContract.The_candidate_cutoff_never_excludes_authoritative_nodes(New(), "k5");
    [Fact] public Task Touch() => MemoryGraphStoreContract.Touch_records_reinforcement(New(), "k6");
    [Fact] public Task Neighbours() => MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(New(), "k7");
    [Fact] public Task Relink() => MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(New(), "k8");
    [Fact] public Task Degree() => MemoryGraphStoreContract.Degree_counts_connections(New(), "k9");
    [Fact] public Task Prune() => MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(New(), "k10");
    [Fact] public Task Forget() => MemoryGraphStoreContract.Forget_clears_a_scope(New(), "k11");
    [Fact] public Task Cascade() => MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(New(), "k12");
    [Fact] public Task Cancellation() => MemoryGraphStoreContract.Cancellation_propagates(New(), "k13");
    [Fact] public Task Edge_freshness() => MemoryGraphStoreContract.An_edge_records_when_it_was_last_strengthened(New(), "k14");
    [Fact] public Task Strength() => MemoryGraphStoreContract.A_node_reports_its_connection_strength_and_freshness(New(), "k15");
    [Fact] public Task No_strength() => MemoryGraphStoreContract.An_unconnected_node_reports_no_strength(New(), "k16");

    /// <summary>SQLite-specific, because the other backends match a contiguous substring: the trigram index
    /// gives CJK substring recall, which unicode61 would silently return nothing for.</summary>
    [Fact]
    public async Task Cjk_substring_recall()
    {
        var store = New();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertAsync(new GraphNodeWrite("e", "cjk", "s", "灵台平台负责智能代理的记忆存储",
            "灵台平台负责智能代理的记忆存储", MemoryGrade.Associative, 7, null), now);
        await store.UpsertAsync(new GraphNodeWrite("e", "cjk", "s", "另一条无关的记录",
            "另一条无关的记录", MemoryGrade.Associative, 7, null), now);

        var hits = await store.SeedAsync("e", "cjk", "s", "智能代理", null, 10, now);

        Assert.Single(hits);
    }

    /// <summary>The FTS index must not keep matching text that was deleted — the single most botched thing
    /// in this repository's storage layer, and silent when it goes wrong.</summary>
    [Fact]
    public async Task Fts_stays_in_sync_after_a_delete()
    {
        var store = New();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertAsync(new GraphNodeWrite("e", "sync", "s", "distinctive phrase here",
            "distinctive phrase here", MemoryGrade.Associative, 7, null), now);

        await store.ForgetAsync("e", "sync", "s");

        Assert.Empty(await store.SeedAsync("e", "sync", "s", "distinctive", null, 10, now));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~SqliteMemoryGraphStore"`
Expected: compile failure — `SqliteMemoryGraphStore` does not exist. (The scaffolded migration also does not
compile until its tag placeholder is replaced; that is deliberate.)

- [ ] **Step 3: Write the migration**

Replace the scaffold's body. Copy the trigger shape from `M202607280003_Memory.cs` and adjust for TWO
indexed columns — the `'delete'` command row must supply both.

```csharp
[Migration(202608081215)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608081215_MemoryGraph : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE lyntai_memory_node (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                engine TEXT NOT NULL,
                task_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                headline TEXT NOT NULL,
                content TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                grade INTEGER NOT NULL,
                metadata TEXT NULL,
                created_at TEXT NOT NULL,
                last_recalled_at TEXT NOT NULL,
                recall_count INTEGER NOT NULL DEFAULT 0,
                stability REAL NOT NULL
            )
            """);
        Execute.Sql("""
            CREATE UNIQUE INDEX ux_lyntai_memory_node_dedup
            ON lyntai_memory_node(engine, task_key, scope, content_hash)
            """);
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_node_scope ON lyntai_memory_node(engine, task_key, scope)
            """);

        // composite PK and both FKs INLINE — SQLite has no ALTER ADD CONSTRAINT
        Execute.Sql("""
            CREATE TABLE lyntai_memory_edge (
                from_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                to_id INTEGER NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                kind TEXT NOT NULL DEFAULT '',
                weight REAL NOT NULL,
                strengthened_at TEXT NOT NULL,
                PRIMARY KEY (from_id, to_id, kind)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_edge_to ON lyntai_memory_edge(to_id)");

        Execute.Sql("""
            CREATE VIRTUAL TABLE lyntai_memory_node_fts USING fts5(
                headline, content, content='lyntai_memory_node', content_rowid='id', tokenize='trigram')
            """);

        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_node_ai AFTER INSERT ON lyntai_memory_node BEGIN
                INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
                VALUES (new.id, new.headline, new.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_node_ad AFTER DELETE ON lyntai_memory_node BEGIN
                INSERT INTO lyntai_memory_node_fts(lyntai_memory_node_fts, rowid, headline, content)
                VALUES ('delete', old.id, old.headline, old.content);
            END
            """);
        Execute.Sql("""
            CREATE TRIGGER lyntai_memory_node_au AFTER UPDATE OF headline, content ON lyntai_memory_node BEGIN
                INSERT INTO lyntai_memory_node_fts(lyntai_memory_node_fts, rowid, headline, content)
                VALUES ('delete', old.id, old.headline, old.content);
                INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
                VALUES (new.id, new.headline, new.content);
            END
            """);

        Execute.Sql("""
            INSERT INTO lyntai_memory_node_fts(rowid, headline, content)
            SELECT id, headline, content FROM lyntai_memory_node
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_node_au");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_node_ad");
        Execute.Sql("DROP TRIGGER IF EXISTS lyntai_memory_node_ai");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_node_fts");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_edge_to");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_edge");
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_node_scope");
        Execute.Sql("DROP INDEX IF EXISTS ux_lyntai_memory_node_dedup");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_node");
    }
}
```

- [ ] **Step 4: Write the store**

Key shapes, in full, so nothing is left to invention:

- **Column list**, aliased explicitly (a name mismatch is a silent null, not an error), with the aggregates
  as correlated subqueries and `CAST` on every float:

```csharp
private const string NodeColumns = """
    n.id AS Id, n.engine AS Engine, n.task_key AS TaskKey, n.scope AS Scope,
    n.headline AS Headline, n.content AS Content, n.grade AS Grade,
    n.created_at AS CreatedAt, n.last_recalled_at AS LastRecalledAt, n.recall_count AS RecallCount,
    CAST(n.stability AS REAL) AS Stability, n.metadata AS Metadata,
    (SELECT COUNT(*) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Degree,
    (SELECT CAST(COALESCE(SUM(e.weight), 0) AS REAL) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Strength,
    (SELECT MAX(e.strengthened_at) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthAsOf
    """;
```

- **A settable-property row type**, never a positional record:

```csharp
private sealed class NodeRow
{
    public long Id { get; set; }
    public string Engine { get; set; } = "";
    public string TaskKey { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Content { get; set; } = "";
    public int Grade { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastRecalledAt { get; set; }
    public int RecallCount { get; set; }
    public double Stability { get; set; }
    public string? Metadata { get; set; }
    public int Degree { get; set; }
    public double Strength { get; set; }
    public DateTimeOffset? StrengthAsOf { get; set; }
}
```

- **The age predicate**, reused by `SeedAsync` and `PruneAsync`. The `MAX(...)` guard is load-bearing: a
  zero stability divides by zero, SQLite evaluates that to NULL, and a NULL predicate excludes the row
  **silently**:

```csharp
private const string AgeOverStability =
    "(julianday(@now) - julianday(n.last_recalled_at)) / MAX(CAST(n.stability AS REAL), 0.000001)";
```

- **`UpsertAsync`** — atomic on the dedup index, refreshing rather than duplicating:

```sql
INSERT INTO lyntai_memory_node
    (engine, task_key, scope, headline, content, content_hash, grade, metadata,
     created_at, last_recalled_at, recall_count, stability)
VALUES (@engine, @taskKey, @scope, @headline, @content, @hash, @grade, @metadata,
        @now, @now, 0, @stability)
ON CONFLICT(engine, task_key, scope, content_hash)
    DO UPDATE SET last_recalled_at = @now, grade = @grade, headline = @headline
RETURNING id
```

`RETURNING id` rather than `last_insert_rowid()`, which is per-connection and returns 0 on a different
pooled connection.

- **`SeedAsync`** — FTS arm first, LIKE fallback, then most-recent, mirroring `SqliteMemoryStore`. Every arm
  carries `(n.grade = 1 OR <its own match>)` and `(n.grade = 1 OR @cut IS NULL OR {AgeOverStability} <= @cut)`,
  and every `ORDER BY` ends `, n.id DESC`.

- **`NeighboursAsync`** — the strongest edge per neighbour plus its freshness, raw:

```sql
SELECT {NodeColumns}, CAST(x.w AS REAL) AS EdgeWeight, x.at AS EdgeStrengthenedAt
FROM (SELECT e.to_id AS id, MAX(e.weight) AS w, MAX(e.strengthened_at) AS at
      FROM lyntai_memory_edge e
      WHERE e.from_id IN @ids AND e.to_id NOT IN @ids
      GROUP BY e.to_id) x
JOIN lyntai_memory_node n ON n.id = x.id
WHERE n.engine = @engine
ORDER BY x.w DESC, n.id DESC
LIMIT @limit
```

- **`LinkAsync`** — strengthen, recording freshness; skip a self-edge:

```sql
INSERT INTO lyntai_memory_edge (from_id, to_id, kind, weight, strengthened_at)
VALUES (@from, @to, @kind, @weight, @now)
ON CONFLICT(from_id, to_id, kind)
    DO UPDATE SET weight = weight + @weight, strengthened_at = @now
```

- **`TouchAsync`** — one statement per touch inside a transaction; `recall_count = recall_count + 1`.

- **`PruneAsync` / `ForgetAsync`** — `DELETE FROM lyntai_memory_node WHERE …`; edges go with them through
  the FK cascade, which only holds because connections come from `IDbConnectionFactory` (it applies
  `foreign_keys=ON` per connection). Prune excludes `grade = 1`.

- [ ] **Step 5: Register it**

In `SqliteStorageBuilderExtensions`, beside the other `StorageFeature.Memory` stores:

```csharp
services.TryAddSingleton<Lyntai.Memory.IMemoryGraphStore>(sp => new SqliteMemoryGraphStore(
    sp.GetRequiredService<IDbConnectionFactory>(),
    sp.GetService<ILogger<SqliteMemoryGraphStore>>()));
```

- [ ] **Step 6: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~SqliteMemoryGraphStore"`
Expected: PASS, 18 matched.

- [ ] **Step 7: Commit**

```bash
git add src/Lyntai.Storage.Sqlite tests/Lyntai.Tests/Memory
git commit -m "feat(storage): persist graph memory on SQLite"
```

---

### Task 2: Postgres

**Files:**
- Create: `src/Lyntai.Storage.Postgres/Migrations/M202608081215_MemoryGraph.cs`
- Create: `src/Lyntai.Storage.Postgres/PostgresMemoryGraphStore.cs`
- Modify: `src/Lyntai.Storage.Postgres/`'s storage builder extensions
- Test: add graph facts to `tests/Lyntai.Tests/Storage/PostgresStorageTests.cs`

**Interfaces:**
- Consumes: the same Core types; `PostgresFixture` (shared container, `[Collection("postgres")]`).
- Produces: `PostgresMemoryGraphStore(IDbConnectionFactory factory, ILogger<PostgresMemoryGraphStore>? logger)`.

- [ ] **Step 1: Write the failing test**

Add to `PostgresStorageTests`, one `[SkippableFact]` per contract fact, each namespaced by `Uid()` because
the container is shared:

```csharp
    [SkippableFact]
    public async Task Graph_store_satisfies_the_contract()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var key = Uid();

        await MemoryGraphStoreContract.Upsert_then_seed_by_single_token_substring(store, key + "a");
        await MemoryGraphStoreContract.Upserting_identical_content_refreshes_rather_than_duplicating(store, key + "b");
        await MemoryGraphStoreContract.Engines_are_isolated_from_one_another(store, key + "c");
        await MemoryGraphStoreContract.The_candidate_cutoff_excludes_stale_associative_nodes(store, key + "d");
        await MemoryGraphStoreContract.The_candidate_cutoff_never_excludes_authoritative_nodes(store, key + "e");
        await MemoryGraphStoreContract.Touch_records_reinforcement(store, key + "f");
        await MemoryGraphStoreContract.Linked_nodes_are_reachable_as_neighbours(store, key + "g");
        await MemoryGraphStoreContract.Linking_the_same_pair_again_strengthens_it(store, key + "h");
        await MemoryGraphStoreContract.Degree_counts_connections(store, key + "i");
        await MemoryGraphStoreContract.Prune_removes_only_what_it_is_told_to(store, key + "j");
        await MemoryGraphStoreContract.Forget_clears_a_scope(store, key + "k");
        await MemoryGraphStoreContract.Deleting_a_node_takes_its_edges_with_it(store, key + "l");
        await MemoryGraphStoreContract.Cancellation_propagates(store, key + "m");
        await MemoryGraphStoreContract.An_edge_records_when_it_was_last_strengthened(store, key + "n");
        await MemoryGraphStoreContract.A_node_reports_its_connection_strength_and_freshness(store, key + "o");
        await MemoryGraphStoreContract.An_unconnected_node_reports_no_strength(store, key + "p");
    }
```

One test running every fact matches how the other Postgres suites keep container startup cost down.

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~PostgresStorageTests"`
Expected: compile failure — `PostgresMemoryGraphStore` does not exist. **If the suite reports SKIPPED,
Docker is not reachable — say so and stop; a skipped Postgres suite is not a pass.**

- [ ] **Step 3: Write the migration**

Same number as its SQLite twin, matching every existing pair. Postgres needs no FTS mirror: `pg_trgm` is a
GIN index on the column, and `ILIKE` uses it.

```csharp
[Migration(202608081215)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608081215_MemoryGraph : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        Execute.Sql("""
            CREATE TABLE lyntai_memory_node (
                id               BIGSERIAL PRIMARY KEY,
                engine           TEXT NOT NULL,
                task_key         TEXT NOT NULL,
                scope            TEXT NOT NULL,
                headline         TEXT NOT NULL,
                content          TEXT NOT NULL,
                content_hash     TEXT NOT NULL,
                grade            INTEGER NOT NULL,
                metadata         TEXT NULL,
                created_at       TIMESTAMPTZ NOT NULL,
                last_recalled_at TIMESTAMPTZ NOT NULL,
                recall_count     INTEGER NOT NULL DEFAULT 0,
                stability        DOUBLE PRECISION NOT NULL
            )
            """);
        Execute.Sql("""
            CREATE UNIQUE INDEX ux_lyntai_memory_node_dedup
            ON lyntai_memory_node(engine, task_key, scope, content_hash)
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_node_scope ON lyntai_memory_node(engine, task_key, scope)");
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_node_trgm ON lyntai_memory_node USING gin (content gin_trgm_ops)
            """);
        Execute.Sql("""
            CREATE TABLE lyntai_memory_edge (
                from_id         BIGINT NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                to_id           BIGINT NOT NULL REFERENCES lyntai_memory_node(id) ON DELETE CASCADE,
                kind            TEXT NOT NULL DEFAULT '',
                weight          DOUBLE PRECISION NOT NULL,
                strengthened_at TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (from_id, to_id, kind)
            )
            """);
        Execute.Sql("CREATE INDEX ix_lyntai_memory_edge_to ON lyntai_memory_edge(to_id)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_edge");
        Execute.Sql("DROP TABLE IF EXISTS lyntai_memory_node");
    }
}
```

- [ ] **Step 4: Write the store**

Mirror the SQLite store file-for-file, with the dialect differences and nothing more:

| Concern | SQLite | Postgres |
|---|---|---|
| Age in days | `julianday(@now) - julianday(n.last_recalled_at)` | `EXTRACT(EPOCH FROM (@now - n.last_recalled_at)) / 86400.0` |
| Divide-by-zero guard | `MAX(CAST(n.stability AS REAL), 0.000001)` | `GREATEST(n.stability, 0.000001)` |
| Float read | `CAST(x AS REAL)` | bare column (`DOUBLE PRECISION` binds directly) |
| Query match | FTS5 trigram `MATCH` + bm25, LIKE fallback | `content ILIKE @pattern` over the pg_trgm GIN index |
| Limit | `LIMIT @take` | `LIMIT @take` |

Postgres still uses a settable-property row type — `PostgresScoreStore` explains why: it sidesteps Dapper's
record-constructor exact-type matching regardless of the boolean question.

- [ ] **Step 5: Register it**, mirroring the SQLite registration.

- [ ] **Step 6: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~PostgresStorageTests"`
Expected: PASS with the graph test RUN, not skipped. Confirm it ran.

- [ ] **Step 7: Commit**

```bash
git add src/Lyntai.Storage.Postgres tests/Lyntai.Tests/Storage
git commit -m "feat(storage): persist graph memory on Postgres"
```

---

### Task 3: Close out

- [ ] **Step 1:** Regenerate the API baselines (`Lyntai.Storage.Sqlite`, `Lyntai.Storage.Postgres`), read
  the diff with `git diff --ignore-cr-at-eol` — CRLF baselines versus LF `.actual` make a plain diff
  useless — and confirm insertions only.
- [ ] **Step 2:** `node devtools/dev.mjs verify` — all seven gates.
- [ ] **Step 3:** `node devtools/dev.mjs consumer-smoke` — this touched packaging-visible surface in two
  adapter packages, and it is the only check that exercises what actually ships.
- [ ] **Step 4:** CHANGELOG under `## Unreleased`; README's graph-memory section drops "This release ships
  the InMemory backend; SQLite and Postgres follow."
- [ ] **Step 5:** Archive MEM2b; leave MEM2c and MEM-TUNE open, and keep the Part 46/47 summaries honest.
- [ ] **Step 6:** Commit.

## Self-Review

**Spec coverage (§8).** 8.1 tables → Task 1 Step 3 and Task 2 Step 3. 8.2 FTS (trigram, three triggers,
`'delete'` row on update *and* delete, same-migration backfill) → Task 1 Step 3, pinned by the CJK and
delete-sync tests. 8.3 migration (unique number, both tags) → both. 8.4 reading rows (`CAST`, row types,
explicit aliases, unique tiebreakers) → Task 1 Step 4. 8.5 three backends one contract, no shared SQL →
Tasks 1–2, with the divergence table naming exactly what differs.

**Not covered, and why:** `MemorySources.Similarity`, the agent tools and MEM-TUNE are MEM2c.

**Type consistency.** `NodeRow` members match `GraphNode`'s aliases exactly; `EdgeWeight` /
`EdgeStrengthenedAt` match `GraphNeighbour`. `AgeOverStability` is defined once per backend and reused by
`SeedAsync` and `PruneAsync`, so the two cannot disagree about what "stale" means.

**One judgement call recorded:** `Relevance` is rank-derived on both SQL backends (see "Three things the SQL
must get right"). It is a monotone transform of each backend's own ranking, which is all the engine's `Rank`
multiplication needs, and it keeps the contractual 0..1 without inventing a bm25 normalization.
