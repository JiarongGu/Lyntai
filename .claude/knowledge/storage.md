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

## An open bag column, and when a signal instead earns its own column

`lyntai_memory_node.signals` (SQLite `TEXT`, Postgres `JSONB`) carries `MemorySignals` — an open
name→double bag — as one JSON object, via `MemorySignalsJson` (`Lyntai.Core`, hand-walked
`Utf8JsonWriter`/`JsonDocument`, no reflection `JsonSerializer` — D14; mirrors `CuratedMetadataJson`
exactly, including empty-bag → SQL `NULL`, never `"{}"`). **Deserialize defensively**: malformed, null, or
non-object JSON — including a row from before signals existed, where the column is simply absent — reads
back as `MemorySignals.Empty` rather than throwing, and a single non-numeric member is skipped rather than
sinking the whole bag. A row must stay recallable even when one signal cannot be parsed; a lost signal
silently restores pre-signals decay for that entry, which is recoverable, and losing the memory over it
would not be.

**The bag is the default; a signal is promoted to its own column only when the DATABASE itself must sort or
filter on it** — no portable index reaches into a JSON blob. `salience` is the first and, so far, only
promotion, because a salient entry must be admitted as a candidate even when it matches the query, or
recency, poorly.

**The promoted column is the COERCED materialisation of the bag's value, not a copy of it** — so the two are
not byte-for-byte equal, and expecting them to be is the trap. `MemorySignals.Salience(write.Signals)` is the
one shared rule (below 1 → 1, non-finite → 1), and it is what the column gets; the bag is stored verbatim. A
bag holding `{"salience": 0.5}` therefore reads back as `0.5` while the column holds `1`. What they cannot do
is DRIFT: both are written from the same bag, in the same statement, through that one function — which every
other reader of the value also calls (the in-process store's ordering, `GraphMemoryEngine`'s rank boost), so
the same data cannot admit differently on different backends. The bag is read back into the node's `Signals`
on the way out.

**Promotion earns a column; it does not automatically earn an INDEX** — the two legs deliberately differ.
SQLite indexes `(engine, task_key, scope, salience DESC)` because the FTS-merge path's separate exact-facts
sub-query genuinely plans against it: its `WHERE` pins `grade = @authoritative` exactly, so its `ORDER BY`
leads with `salience DESC` and nothing computed sits ahead of it. Postgres has no such sub-query, and both of
its seed paths lead with the COMPUTED `(grade = @authoritative)` boolean, which no such prefix can satisfy —
`ix_lyntai_memory_node_scope` already covers the equality part. An index nothing reads is not free
parallelism; it is write amplification on the hottest table in the schema, paid on every remember. Add one
with the query that needs it.

**The ordering is per BRANCH, not one rule repeated on every backend — read the query, not this
paragraph, before assuming a branch's order.** Where nothing has already ranked the candidates by match
quality — the no-query and LIKE-fallback branches on every backend, and SQLite's separate exact-facts
sub-query inside the FTS-merge path — salience leads recency:
`(grade = authoritative) DESC, salience DESC, last_recalled_position DESC, id DESC` (the exact-facts
sub-query omits the grade term; its `WHERE` already restricts to `grade = authoritative`, so every row
ties on it). **SQLite's bm25-matched branch is the one exception, and deliberately so**: everything there
already matched the query, so match quality leads and salience is only a TIEBREAK —
`ORDER BY bm25(…), salience DESC, id DESC`. Letting salience outrank bm25 would let a salient POOR match
displace a strong one; that distortion is Task 6's engine-side rank contribution to own (bounded and
logarithmic there), not the store's to reproduce unbounded on a query that already discriminates by
relevance.

**A re-remember of identical content with an EMPTY incoming bag must not blank an existing one.** A
salience policy may decline to judge a re-remembered write for any reason (too few comparables, a novelty probe
that found only the entry's own prior vector, a caught failure) and reports that as `MemorySignals.Empty` —
which must never be read as "this entry is no longer salient," or the very write meant to REINFORCE an
entry would instead erase an earlier judgement. `InMemoryMemoryGraphStore` has had this since Task 4; the
SQL backends resolve it in the `DO UPDATE SET`/`ON CONFLICT` clause itself:
`signals = COALESCE(@signals, <table>.signals)`, `salience = CASE WHEN @signals IS NULL THEN
<table>.salience ELSE @salience END` — an empty incoming bag serializes to SQL `NULL`, which is also the
"keep what's stored" signal these expressions read. A NON-empty incoming bag still overwrites unconditionally
— re-appraisal must be able to correct a stale value, not just refuse to erase one.

**`NOT NULL DEFAULT 1`, never nullable — an affinity trap of its own.** 1 is salience's neutral value, so a
pre-existing row migrates to "no opinion" and orders exactly as before. A nullable column would be worse
than untidy: `ORDER BY salience DESC` puts NULLs **first** on Postgres, so every legacy row would silently
outrank every appraised one — wrong data, not an error. `MigrationSchemaSnapshotTests` /
`PgMigrationSchemaSnapshotTests` pin the DDL text (`NOT NULL DEFAULT 1`) so a regression here fails on the
golden schema diff, not just on a behavioural test.

**Postgres needs an explicit `::jsonb` cast on the parameter**, unlike the plain-`TEXT` `metadata` column:
Npgsql infers a bound `string` parameter as `text`, and `INSERT`/`ON CONFLICT DO UPDATE SET` against a
`jsonb` column raises `42804` (`column "signals" is of type jsonb but expression is of type text`) without
it — `@signals::jsonb` in both the `VALUES` list and the `DO UPDATE SET` clause, the same `::type` pattern
`PostgresCuratedMemoryStore` already uses to resolve a NULL update parameter's type.

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

Query building: **`SearchTerms.Extract` owns the split, for every backend** — words for a space-separated
script, character **trigrams** for a run written without spaces (CJK), `<3`-char terms dropped (trigram's
minimum). `FtsQuery.Build` then only applies FTS5 *syntax*: double-quote each term (neutralizing FTS
operators — this is also the injection guard), OR-join, and return `null` when nothing usable remains → the
caller **falls back to LIKE** (with `ESCAPE`-guarded `% _ \`). Rank matches with `bm25()`. `match` is only
ever sourced from `FtsQuery.Build`, never raw user text. The LIKE/ILIKE side uses `SearchTerms.LikeClause`,
which returns the OR predicate, a matched-term COUNT expression for ranking, and the parameters.

**WHICH entries a query finds is now the same on all three backends; only RANKING differs** (`D55`). SQLite
ranks by **bm25**; Postgres (pg_trgm) and InMemory by **matched-term count, then recency**.

**The trap this replaced, because it is the shape of trap to watch for.** Only `FtsQuery` knew how to split a
query, so only SQLite's FTS path did — every other path (both LIKE fallbacks, all three Postgres queries,
both InMemory stores) passed the whole query to `LikePattern.Contains` and matched it as one contiguous
substring. `foo bar` recalled `xxfooxx` on SQLite and nothing on the other two, and a realistic cue like
`"what is the spouse called"` is contiguous in no entry at all, so keyword seeding was effectively dead on
two backends. **It was written down as a by-design divergence and defended as one for a year.** The test that
should have caught it asserted the divergence, and the shared contract's every conformance fact used a
one-word query. The rule that falls out: *an ORDERING difference between backends is a divergence; a
different answer to "is the fact found" is a defect* — and a "documented divergence" nobody measured is just
an undiagnosed bug with a citation. Same root cause gave CJK exact-phrase-or-nothing while English got
OR-over-words. Converging ranking is still out of scope (reimplementing bm25 in-app); converging **admission**
was not optional.

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
**D33**, and `AsyncMigrationTests` pins the no-offload property.

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
