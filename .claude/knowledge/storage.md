---
name: storage
applies_when: writing SQL, adding or changing a migration, or adding/extending a Lyntai storage backend
enforces: alias every SELECT and CAST affinity-typed columns; open connections only through the factory; three FTS trigram triggers plus a backfill; both migration tags; never dedup the Sqlite/Postgres pair — the contract facts are the dedup mechanism
---

# Storage internals

The load-bearing rules for the relational backends (`Lyntai.Storage.Sqlite`, `Lyntai.Storage.Postgres`)
and any future one — the ONE statement of each; `sql-storage.md` only indexes them. Each is a place where
the code passes tests while being subtly wrong. Reference: design §7, and `Lyntai.Storage.Sqlite` as the
worked example.

## Dapper + snake_case

`DapperConventions.Register()` (`src/Shared/Relational/`, called from each connection factory's static
ctor) sets `DefaultTypeMap.MatchNamesWithUnderscores = true` — `snake_case` columns ↔ PascalCase properties
— and the `DateTimeOffset` ↔ UTC type handler. **Both are PROCESS-GLOBAL**: whichever adapter registers last
wins for every connection of every backend, so two adapters with handlers that differed at all would give
one backend the other's round-trip behaviour, with nothing to see at the registration site. That is why the
conventions are ONE source compiled into each adapter, never two copies kept in step by a comment — and
worth saying in any doc aimed at a consumer whose own app also uses Dapper. A column/property name mismatch
yields a **silent null**, not an error — always alias explicitly in SELECTs (`SELECT id AS Id, …`).

## The integer-affinity trap — `CAST(x AS REAL)`

SQLite stores `1.0` as an INTEGER and `0.5` as a REAL in the *same* column, so Dapper's type inference
can hand a `double` property a boxed `long` and throw (or truncate). **Every** 0..1 / floating column
(scores, `cost_usd`) MUST be read as `CAST(col AS REAL)` in the SELECT. Integer columns (token counts,
durations) are fine uncast. `ScoreStoreTests.Doubles_round_trip_exactly_the_affinity_trap` guards this.

**Bool from INTEGER + a positional record:** Dapper will NOT bind a SQLite `INTEGER` (0/1) column to a
`bool` parameter of a **positional record constructor** — it fails with "no matching constructor". Bind
into a settable-property **row type** (Dapper converts INTEGER→bool for a property setter) and project to
the record — see the shared row types in `Core/Storage/StorageRows.cs` (`CuratedMemoryRow`, …) or
`Lyntai.Storage.JobRow`, which both relational backends read through. (The Postgres stores use row types too,
even though native `BOOLEAN` would bind: a property-mapped row sidesteps Dapper's record-ctor **exact-type**
matching regardless of the boolean question — the comment at `PostgresScoreStore.GetAsync` says so.) Name it
`Row` / `<Thing>Row` — **never `*Dto`**, per the naming rule in `.claude/rules/repo-mechanics.md` §Naming.

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
displace a strong one; that distortion is the engine-side rank contribution's to own (bounded and
logarithmic there), not the store's to reproduce unbounded on a query that already discriminates by
relevance.

**A re-remember of identical content with an EMPTY incoming bag must not blank an existing one.** A
salience policy may decline to judge a re-remembered write for any reason (too few comparables, a novelty probe
that found only the entry's own prior vector, a caught failure) and reports that as `MemorySignals.Empty` —
which must never be read as "this entry is no longer salient," or the very write meant to REINFORCE an
entry would instead erase an earlier judgement. `InMemoryMemoryGraphStore` already honours this; the
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

**WHICH entries a query finds is the same on every backend; only RANKING differs.** SQLite ranks by
**bm25**; Postgres (pg_trgm), InMemory and FileSystem by **matched-term count, then recency**. *An ORDERING
difference between backends is a divergence; a different answer to "is the fact found" is a defect*
(**D55**): every backend splits a query through `SearchTerms`, and a conformance fact that uses only a
one-word query cannot tell the two apart.

## Don't "dedup" the Sqlite/Postgres stores — the parallelism is intentional

The two relational backends mirror each other file-for-file, and what still differs between a pair is
*dialect necessity*, not drift:

| Pair | What stays per backend, and why it cannot be shared |
|---|---|
| `ConversationStore` | Postgres needs a **bounded retry loop** around the `MAX(seq)+1` insert; SQLite serializes writers and doesn't. A concurrency strategy, not a spelling. |
| `KeyValueStore` | the prefix listing: SQLite's `LIKE` is case-INsensitive → `substr()` prefix match; Postgres's `LIKE` is case-sensitive but its ORDER BY is locale-dependent → `LIKE … ESCAPE` + `COLLATE "C"`. Opposite problems, opposite fixes, same contract. |
| `UsageTracker` | the totals read and the per-consumer delete: `COLLATE NOCASE` vs `lower()`. |
| `PromptVersionStore` | SQLite has no boolean type: `is_active = 1` vs `is_active` / `TRUE` / `FALSE`. |
| `ResponseCache` | the size-cap trim: SQLite requires `LIMIT -1 OFFSET @max`; Postgres takes a bare `OFFSET @max`. |
| `ScoreStore` | SQLite's `CAST(score AS REAL)` (the affinity trap above) against Postgres's `COLLATE "C"` ordering. |

Sharing these would mean parameterizing booleans, case-collation, LIMIT/OFFSET **and** the concurrency
strategy. That isn't a dialect seam, it's a small ORM — and it would make both backends harder to read and to
fork. The `*StoreContract` facts run every domain against InMemory + Sqlite + Postgres and hold them to one
contract: **the contract tests are the dedup mechanism here, not a shared base class.**

**What IS shared: every statement with a PORTABLE spelling.** The engine-independent statements live in Core
as text — `JobStoreSql` (the job state machine: transition statements, the `claimed_by` write fence, the
claim-candidate predicate, bound booleans `@t`/`@f`), `ConversationStoreSql`, `TraceStoreSql`,
`KeyValueStoreSql`, `UsageTrackerSql`, `ResponseCacheSql`, `MemoryGraphSql` (**D77**) and
`MemoryEviction.CapEvictSql` — with their row types (`StorageRows.cs`, `MemoryGraphRows.cs`). **Try the
portable spelling before writing a second copy**: `CAST(… AS DOUBLE PRECISION)`, a table-qualified
`DO UPDATE SET`, `ON CONFLICT … DO NOTHING`, unquoted aliases all run on both. A second copy is for genuine
dialect only — FTS5, `IN @ids` vs `= ANY`, `MAX` vs `GREATEST`, `::jsonb`/`::text`, `COLLATE "C"`,
`LIMIT -1 OFFSET`. **Never share a dialect EXPRESSION**: an extraction that needs a `bool isSqlite` or a
`Real(col)` helper to work is the signal to stop — Core carries no database driver, so shared SQL there is
text and nothing more.

**Dialect-free CODE is one linked source, not a package.** `src/Shared/Relational/*.cs` (`DapperConventions`,
`GovernanceGuard`, `StoreWiring`) is compiled into each relational adapter through
`<Compile Include="..\Shared\Relational\*.cs" LinkBase="Shared\Relational" />`, never referenced: an
adapter→adapter reference breaks the package rule, Core has no Dapper, and a package would be a published id
and nine registries for under two hundred internal lines.
Everything there is `internal`, so each adapter gets its own copy of every type — the SQLite and Postgres
`FeatureSelection` services are distinct for exactly that reason — and nothing public may go there, since two
adapters in one app would then collide on it. `check-packages` counts only `src/*/*.csproj`.

**The rule reaches the PROSE.** An engine-independent RULE gets stated once, next to the shared thing it
governs (`MemoryNodeRow` for what an age mark means, `MemoryEviction` for the eviction statement) or in the
record that owns it; each backend keeps its DIALECT note and a pointer. Two wordings of one rule in the two
adapters are drift by the definition this section uses for code, and `check-comments` bounds only a
block's length.

## Migrations

FluentMigrator, numbered `yyyyMMddHHmm`, **never reused** (an unapplied duplicate number is silently
skipped). Use `dev.mjs new-migration` to get a unique monotonic number. **Backfill in the same migration that
adds a structure** — a structure correct only for rows written after it shipped is a bug waiting for the
first old row.

**A fresh database applies 12 migrations on SQLite and 13 on POSTGRES, and the asymmetry is deliberate:**
`M202608152310_MemoryHeadlineSearch` adds a trigram index on `headline` so a recall can match an authored
one without a sequential scan, and SQLite needs no counterpart because its FTS5 mirror has indexed
`headline, content` since the graph store shipped. Migrations are per-backend projects; forcing the numbers
to match would mean shipping a SQLite migration that does nothing. `check-counts` holds the first number.

**Never renumber a shipped migration**: the number is recorded in `lyntai_version_info`, so a new one re-runs
it against tables that already exist. Composite PKs and FKs go **inline at `Create.Table`** (SQLite has no
`ALTER ADD CONSTRAINT`); raw `Execute.Sql` covers what the fluent API cannot (FTS tables, triggers,
`ON DELETE CASCADE`). The runner applies migrations under WAL + `busy_timeout` (`MigrationRunnerService`) and
is idempotent.

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
fourth Governance-backed helper and it must call `GovernanceGuard.Require` (`src/Shared/Relational/`), and
**D150** is why the check is eager and scoped this way. `UsePostgresVectorStore` is **exempt**:
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
  never throws on a short/unmatchable query). **Re-throw only the CALLER's cancellation, tested as
  `ct.IsCancellationRequested` — never by the exception's TYPE.** A bare
  `catch (OperationCanceledException) { throw; }` makes a fail-open seam fail CLOSED, because a network
  deadline arrives as `TaskCanceledException` and that IS an `OperationCanceledException`.
