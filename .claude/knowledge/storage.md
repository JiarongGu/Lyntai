# Storage internals

The load-bearing rules for `Lyntai.Storage.Sqlite` (and any future backend). Each is a place where the
code passes tests while being subtly wrong. Reference: design §7, and `Lyntai.Storage.Sqlite` as the
worked example.

## Dapper + snake_case

`Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` maps `snake_case` columns ↔ PascalCase
properties. It's a **process-global** switch (set once in `SqliteConnectionFactory`'s static ctor) —
note this in any doc aimed at a consumer whose own app also uses Dapper. A column/property name mismatch
yields a **silent null**, not an error — always alias explicitly in SELECTs (`SELECT id AS Id, …`).

## The integer-affinity trap — `CAST(x AS REAL)`

SQLite stores `1.0` as an INTEGER and `0.5` as a REAL in the *same* column, so Dapper's type inference
can hand a `double` property a boxed `long` and throw (or truncate). **Every** 0..1 / floating column
(scores, `cost_usd`) MUST be read as `CAST(col AS REAL)` in the SELECT. Integer columns (token counts,
durations) are fine uncast. `ScoreStoreTests.Doubles_round_trip_exactly_the_affinity_trap` guards this.

## Per-connection pragmas

`foreign_keys`, `busy_timeout`, and `journal_mode` are **per-connection** in SQLite (except WAL, which
is a persistent header setting). Every `IDbConnectionFactory.Open()` applies
`PRAGMA journal_mode=WAL; busy_timeout=5000; foreign_keys=ON`. A store that opens a connection any other
way silently **loses FK enforcement** (cascades stop working). The migrator sets the same pragmas up
front (WAL persists; a fresh connection would otherwise migrate without a busy-wait). Always go through
the factory.

## FTS5 trigram external-content — the #1 botched thing

Searchable text tables use an external-content FTS5 virtual table with the **`trigram`** tokenizer
(`unicode61` treats a whole CJK phrase as one token — trigram gives substring recall, incl. CJK). It is
kept in sync by triggers, and this is where bugs hide:

- **Three triggers, not one:** AFTER INSERT, AFTER DELETE, AFTER UPDATE.
- On DELETE **and** UPDATE you must emit the special FTS `'delete'` command row
  (`INSERT INTO x_fts(x_fts, rowid, col) VALUES('delete', old.id, old.content)`) before re-inserting the
  new row — miss it and the index silently corrupts (stale rows match forever).
- **Backfill in the same migration** so existing rows are indexed.
- Copy `M202607170003_Memory.cs` verbatim; adjust columns only.

Query building: `FtsQuery.Build` drops `<3`-char tokens (trigram's minimum), double-quotes the rest
(neutralizing FTS operators — this is also the injection guard), OR-joins them, and returns `null` when
nothing usable remains → the caller **falls back to LIKE** (with `ESCAPE`-guarded `% _ \`). Rank matches
with `bm25()`. `match` is only ever sourced from `FtsQuery.Build`, never raw user text.

**Cross-backend recall DIVERGENCE (documented on `IMemoryStore.RecallAsync`, not a bug):** the three
backends use three different index engines, so multi-word recall + ranking differ *by design*. SQLite:
ANY token (the OR-join above) via the trigram index, ranked by **bm25 relevance**. Postgres (pg_trgm) +
InMemory: the query as a **contiguous substring**, ranked by **recency**. Consistent guarantee: an entry
whose content contains a ≥3-char query token as a substring is recalled on every backend. A multi-word
query whose words appear *separately* can hit on SQLite but miss on Postgres/InMemory. Don't "fix" one
backend to match another without deciding the semantic — reimplementing bm25 in-app to converge ranking
is out of scope; single salient query terms are portable.

## Migrations

FluentMigrator, numbered `YYYYMMDDNNNN`, **never reused** (an unapplied duplicate number is silently
skipped). Use `dev.mjs new-migration` to get a unique monotonic number. Composite PKs and FKs go
**inline at `Create.Table`** (SQLite has no `ALTER ADD CONSTRAINT`). Raw SQL (`Execute.Sql`) is fine for
the things FluentMigrator's fluent API can't express (FTS virtual tables, triggers, `ON DELETE CASCADE`).
The runner is idempotent.

## Conventions

- **`lyntai_` prefix on every table/index/trigger/FTS object** — Lyntai may share a database with the
  consumer's own schema; the prefix keeps them from colliding.
- Prefer `INSERT … RETURNING id` over `last_insert_rowid()` (the latter is per-connection and returns 0
  on a different pooled connection).
- Parameterize every user value (`@param`); the only safe interpolation is a compile-time column-list
  constant.
- Deterministic ordering: any `ORDER BY` on a non-unique column needs a unique tiebreaker (e.g.
  `ORDER BY created_at DESC, id DESC`) or results wobble on ties.
- Stores are **fail-open** where the interface says so (memory recall degrades FTS→LIKE→recent→empty,
  never throws on a short/unmatchable query; re-throw only `OperationCanceledException`).
