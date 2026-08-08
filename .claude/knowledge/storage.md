---
name: storage
applies_when: writing SQL, adding or changing a migration, or adding/extending a Lyntai storage backend
enforces: alias every SELECT and CAST affinity-typed columns; open connections only through the factory; three FTS trigram triggers plus a backfill; both migration tags; never dedup the Sqlite/Postgres pair — the contract facts are the dedup mechanism
---

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

**Bool from INTEGER + a positional record:** Dapper will NOT bind a SQLite `INTEGER` (0/1) column to a
`bool` parameter of a **positional record constructor** — it fails with "no matching constructor". Bind
into a settable-property **row type** (Dapper converts INTEGER→bool for a property setter) and project to
the record — see `SqliteCuratedMemoryStore.Row`, or the shared `Lyntai.Storage.JobRow` that both
relational job stores read through. (The Postgres stores use row types too, even though native `BOOLEAN`
would bind: a property-mapped row sidesteps Dapper's record-ctor **exact-type** matching regardless of the
boolean question — the comment at `PostgresScoreStore.GetAsync` says so.) Name it `Row` / `<Thing>Row` — **never
`*Dto`**, per the naming rule in `.claude/rules/repo-mechanics.md` §Naming.

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
- Copy `M202607280003_Memory.cs` verbatim; adjust columns only.

Query building: `FtsQuery.Build` drops `<3`-char tokens (trigram's minimum), double-quotes the rest
(neutralizing FTS operators — this is also the injection guard), OR-joins them, and returns `null` when
nothing usable remains → the caller **falls back to LIKE** (with `ESCAPE`-guarded `% _ \`). Rank matches
with `bm25()`. `match` is only ever sourced from `FtsQuery.Build`, never raw user text.

**Cross-backend recall DIVERGENCE (documented on `IMemoryStore.RecallAsync`, not a bug):** the three
backends use three different index engines, so multi-word recall + ranking differ *by design*. SQLite:
ANY token (the OR-join above) via the trigram index, ranked by **bm25 relevance**. Postgres (pg_trgm) +
InMemory: the query as a **contiguous substring**, ranked by **recency**. Consistent guarantee, and it is a
**single-token** one: an entry whose content contains a ≥3-char SINGLE-token query as a substring is
recalled on every backend. A MULTI-token query is per-token on SQLite (any one token matches) and
contiguous-substring on Postgres/InMemory — so `foo bar` recalls `xxfooxx` on SQLite and nothing on the
other two. Don't "fix" one backend to match another without deciding the semantic — reimplementing bm25
in-app to converge ranking is out of scope; single salient query terms are portable.

## Don't "dedup" the Sqlite/Postgres stores — the parallelism is intentional

The two relational backends mirror each other file-for-file, and a normalized diff makes most pairs look
90%+ identical (`TraceStore` differs by 3 lines of 199). **This is not duplication waiting to be
extracted.** A 2026-07-29 review checked every pair and found the divergence is *dialect necessity* in
every case, not drift:

| Pair | Why it differs — and can't be shared |
|---|---|
| `ConversationStore` | Postgres needs a **bounded retry loop** around the `MAX(seq)+1` insert; SQLite serializes writers and doesn't. A concurrency strategy, not a spelling. |
| `KeyValueStore` | SQLite's `LIKE` is case-INsensitive → `substr()` prefix match; Postgres's `LIKE` is case-sensitive but its ORDER BY is locale-dependent → `LIKE … ESCAPE` + `COLLATE "C"`. Opposite problems, opposite fixes, same contract. |
| `UsageTracker` | `COLLATE NOCASE` vs `lower()`; SQLite's bare `col` in `ON CONFLICT DO UPDATE` vs Postgres's required `lyntai_usage.col`. |
| `PromptVersionStore` | SQLite has no boolean type: `is_active = 1` vs `is_active` / `TRUE` / `FALSE`. |
| `ResponseCache` | SQLite requires `LIMIT -1 OFFSET @max`; Postgres takes a bare `OFFSET @max`. |
| `Score`/`Trace` | Only the `CAST(x AS REAL)` affinity trap above — genuinely near-identical, but see below. |

Sharing these would mean parameterizing booleans, case-collation, LIMIT/OFFSET, upsert-reference syntax
**and** the concurrency strategy. That isn't a dialect seam, it's a small ORM — and it would make both
backends harder to read and to fork. They have also **not drifted** across 30+ releases, because the
`*StoreContract` facts run every domain against InMemory + Sqlite + Postgres and hold them to one contract.
**The contract tests are the dedup mechanism here, not a shared base class.**

**The one thing that IS shared, and the rule it sets:** `Core/Storage/JobStoreSql.cs` hoists the job
**state machine** (transition statements, the `claimed_by` write fence, the claim-candidate predicate) plus
the `JobRow` mapping. That was right because drift there is a *correctness* bug — two backends disagreeing
on fencing corrupts jobs — and because the text is genuinely engine-independent (booleans are bound as
`@t`/`@f` parameters precisely so the statements stay identical). Only the locking frame stays per-dialect.

So the rule: **share engine-independent, correctness-critical logic; never share dialect expressions.** If
an extraction needs a `bool isSqlite` or a `Real(col)` helper to work, that's the signal to stop — Core
carries no database driver (`Lyntai.Core.csproj` has only DI + Logging abstractions, and "no heavy
dependencies" is a stated selling point), so shared SQL there can never be more than text anyway.

## Migrations

The canonical statement of the traps behind this section is `.claude/knowledge/sql-storage.md` — never reuse a
number, declare constraints inline, backfill in the same migration, trigram FTS with insert/delete/update
triggers, explicit per-connection pragmas. This section is the Lyntai BINDING of those rules (the `lyntai_`
prefix, the `StorageFeature` tags, the Sqlite/Postgres parallelism); read both.

FluentMigrator, numbered `yyyyMMddHHmm`, **never reused** (an unapplied duplicate number is silently
skipped). Use `dev.mjs new-migration` to get a unique monotonic number.

> **Convention changed 2026-08-08, from `YYYYMMDDNNNN` to `yyyyMMddHHmm`.** <!-- drift-ok --> The timestamp is
> self-describing where a per-day `NNNN` sequence is not, and two people adding a migration on the same day
> without coordinating now collide only within the same MINUTE — still resolved by the generator's
> strictly-greater-than-max loop. Both forms are 12 digits, so they sort together and the nine baseline
> migrations keep their original numbers. **Never renumber an applied migration**: the number is recorded
> in `lyntai_version_info`, so changing it re-runs the migration against a database that already has its
> tables. Renumbering is free only before a migration has shipped. Composite PKs and FKs go
**inline at `Create.Table`** (SQLite has no `ALTER ADD CONSTRAINT`). Raw SQL (`Execute.Sql`) is fine for
the things FluentMigrator's fluent API can't express (FTS virtual tables, triggers, `ON DELETE CASCADE`).
The runner is idempotent.

**Every migration carries `[Tags(nameof(StorageFeature.<Feature>), StorageFeatures.AllTag)]` — both tags,
always.** The feature tag is what a SUBSET pass requests; `AllTag` is what the default `StorageFeature.All`
pass requests (one pass, and it works only because every migration carries it). The trap is that an
UNTAGGED migration is run by FluentMigrator under *every* feature set, so a domain the app disabled would
still land its table and nothing would report it — which is why the scaffold's tag placeholder deliberately
doesn't compile.

**`MigrateUpAsync` cannot be more than it is.** FluentMigrator's runner is synchronous and takes no
`CancellationToken`, so the awaitable twins run the migration **inline on the calling thread** — never
`Task.Run`, which would burn a pool thread for the whole migration and *still* be uncancellable — and honour
the token only *before* any work and *between* feature passes. `StorageFeature.All` is a single pass, so
there it means "before starting" only. Say exactly that in any doc you write about it; see `DECISIONS.md`
**D40**, and `AsyncMigrationTests` pins the no-offload property.

**`lyntai_vector` ships under `StorageFeature.Governance`,** alongside the response cache and usage ledger —
not under `Memory`, and there is no `Vector` feature. A subset omitting `Governance` would otherwise let
`UseSqliteVectorStore()` register a store over a table that was never created, failing at the first recall
rather than at startup, so **the three Governance-backed helpers now throw at wiring time**
(`UseSqlite{ResponseCache,UsageTracking,VectorStore}` + the two Postgres equivalents). The check is
order-independent — each side records a sentinel `ServiceDescriptor` and verifies whatever the other side
already recorded — because a guard you can defeat by swapping two builder lines is not a guard. It is also
scoped to **schema OWNERSHIP**: the selection carries a `LyntaiMigrates` flag and the check returns early
under `SchemaMigration.None` or an app-supplied `IDbConnectionFactory`, because there Lyntai runs no
migration, the feature set decides nothing, and "add `StorageFeature.Governance`" would create no table —
the guard's whole premise is that Lyntai was going to create the table and the feature set stopped it. Add a
fourth Governance-backed helper and it must call `RequireGovernance`. `UsePostgresVectorStore` is **exempt**:
`PostgresVectorStore` creates its `vector` extension and table lazily, deliberately outside the migration, so
pgvector is not forced on consumers who never use semantic memory.

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
