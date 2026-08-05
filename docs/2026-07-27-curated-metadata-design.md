# Curated memory: an opaque-JSON `Metadata` field + a relational index, folding `Title`/`Source` in

> Design spec (2026-07-27). Status: **SHIPPED 2026-07-27** (`CHANGELOG.md` 0.31.0, `docs/task-archive.md`
> Part 22). Kept as the record of WHY curated metadata is an opaque JSON column plus a relational index
> table rather than `jsonb` — that reasoning still governs the shape. **Not executable:** the
> `M202607270003_CuratedMetadata` migration below was collapsed into the per-domain baselines by the 1.0
> squash (`docs/ROADMAP.md` v1.0.0, `docs/DECISIONS.md` D12's one-time pre-1.0 exception), so neither it
> nor the `202607270001`/`202607270002` numbers it discusses exist in the tree; the live schema is
> `M202607280009_CuratedMemory`. Replaces the per-field accretion on
> `ICuratedMemoryStore` (task/scope/title/kind added one at a time) with one general JSON metadata field,
> stored as an opaque JSON string in every backend and made queryable by a plain relational index table (no
> `jsonb`, no DB-side JSON functions). The contract lives in `docs/2026-07-17-lyntai-design.md`; this is the
> delta.

## Motivation

`CuratedMemory` has grown a new typed column per adopter request — `task`/`scope` (CM1), `title` (CMEM3),
`kind`-update (CMEM5). Each is the same churn: record field, `AddAsync`/`UpdateAsync` params, a migration on
SQLite + Postgres, the InMemory mirror, four ApiSurface baselines. The fields split in two:

- **Filter / composition dimensions** — `kind` (required section grouping + filter), `taskKey`/`scope`
  (drive `ForCompositionAsync`), `enabled` (composition toggle). Load-bearing for querying; **stay** typed.
- **Payload / display** — `Title`, `Source`. Stored and read back, never a first-class filter. Exactly what
  a general metadata field absorbs.

Goal: one generic JSON field for arbitrary extra data — readable **and** queryable — so future payload
fields need no schema/API churn; retire the two columns it subsumes; and give consuming apps a clean,
backend-agnostic way to read and query metadata (a "rich library set", not hand-rolled per-backend SQL).

## Model

`CuratedMemory` gains `IReadOnlyDictionary<string,string>? Metadata` (trailing optional record param) and
**loses** `Source` and `Title`:

```csharp
public sealed record CuratedMemory(
    long Id, string Kind, string Content, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string? TaskKey = null, string? Scope = null,
    IReadOnlyDictionary<string,string>? Metadata = null);
```

**Flat `string→string` map** — the app owns the key namespace (`title`, `source`, `author`, `icon`,
`category`, …). No typed JSON at the API (a number/bool lives as its string form); typed/nested JSON is a
non-goal until a real need appears, and that would be a widening of the value type, not a re-architecture.
Null/empty ⇒ SQL NULL (no empty `{}` rows).

## Storage — opaque JSON column + a relational index table (uniform, no `jsonb`)

Two structures, identical on every SQL backend; InMemory keeps the dict in process.

```
-- on the existing table: the JSON payload (opaque to the DB — parsed only in C#)
ALTER TABLE lyntai_curated_memory ADD COLUMN metadata TEXT NULL;

-- the query index: plain relational, no DB JSON functions
CREATE TABLE lyntai_curated_meta(
  memory_id  → lyntai_curated_memory.id  ON DELETE CASCADE,
  key   TEXT NOT NULL,
  value TEXT NOT NULL,
  PRIMARY KEY (memory_id, key));                 -- one value per key per entry
CREATE INDEX ix_lyntai_curated_meta_kv ON lyntai_curated_meta(key, value);
```

- **Retrieval:** the `metadata` TEXT column holds the canonical JSON; reads deserialize it in C# (the codec
  below) and attach the dict to the returned record. Metadata is on the row — reads are single-row, no
  batch-load.
- **Filtering:** the `lyntai_curated_meta` table is the index — a `metadataMatch` becomes relational
  `EXISTS` clauses (no JSON parsing in SQL, so it's indexed by `(key, value)` and identical on every
  backend). This is the "build the index properly" that makes `jsonb` unnecessary.
- **Why both** (payload column + index): the column is the source of truth and keeps reads a single row;
  the index is derived, for querying only. It's the same shape the FTS already uses (content in the row +
  a search-index mirror), so it's a pattern the codebase already commits to. The write path keeps them
  consistent (below); a bug can't silently desync because a single contract test round-trips add→filter→get.
- **No cross-backend divergence.** Because filtering is plain relational (not `jsonb` vs `json_extract`
  scans), the equality-match result *and* its index behavior are identical on SQLite/Postgres/InMemory —
  the caveat we kept needing with native JSON types is gone.

### JSON conversion adapter

A Core `CuratedMetadataJson` codec is the single dict ⇄ JSON-string converter: serialize to a **canonical**
object string (`System.Text.Json`, keys sorted for a stable form), parse back. It's uniform because every
backend stores the same `TEXT` — SQLite today, and a future **SQL Server** adapter (`NVARCHAR(MAX)`) slots
in unchanged. (SQL Server is **not** a current Lyntai backend — noted only to show the seam accommodates
it.) Postgres uses the same `TEXT` column too: since the index table does the querying, there's no reason to
special-case `jsonb`. The DB never interprets the JSON.

## Write path & sync

- **Add:** insert the base row, serialize `metadata` into the column, and insert the entry's
  `lyntai_curated_meta(key,value)` rows — one transaction.
- **Update `metadata`:** null = leave unchanged; a non-null dict **replaces the whole set** — rewrite the
  column and `DELETE` + re-`INSERT` the entry's index rows in one transaction; an empty dict clears both to
  NULL/zero rows. `content`/`enabled`/`kind` keep COALESCE semantics.
- **Remove:** the FK `ON DELETE CASCADE` clears the index rows.
- **Dedup identity unchanged:** `(kind, content, taskKey, scope)`. Metadata is payload — OUT of the identity
  (as `source`/`title` were); a dedup hit does not mutate the matched row's metadata.

## Query & access API (baked into the store — the "rich set")

Reading: `Metadata` comes populated on every `CuratedMemory` from `GetAsync`/`ListAsync`/`SearchAsync`/
`ForCompositionAsync` — apps read a plain dictionary, no JSON handling.

Querying: optional `IReadOnlyDictionary<string,string>? metadataMatch` on the **ListAsync family**
(`ListAsync` + `SearchAsync`), trailing param before `ct`. An entry matches when it has **every** (k,v) pair
exactly (AND) — one `AND EXISTS (SELECT 1 FROM lyntai_curated_meta mm WHERE mm.memory_id = <base>.id AND
mm.key = @k_i AND mm.value = @v_i)` per pair (keys/values are bound params — no interpolation); InMemory does
dict lookups. Null/empty ⇒ no metadata filter. Equality-only this pass (matches `kind`/`scope` today).
Richer operators (key-exists, prefix) and a `ForCompositionAsync` filter are deferred.

## `AddAsync` / `UpdateAsync` reshape (breaking, pre-1.0)

```csharp
Task<long> AddAsync(string kind, string content, bool enabled = true,
    string? taskKey = null, string? scope = null, bool dedup = false,
    IReadOnlyDictionary<string,string>? metadata = null, CancellationToken ct = default);

Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
    IReadOnlyDictionary<string,string>? metadata = null, CancellationToken ct = default);
```

`source`/`title` params are gone; `metadata` arrives. `ListAsync`/`SearchAsync` gain the `metadataMatch`
trailing param.

## Migration `M202607270003_CuratedMetadata` (append-only, data-preserving)

One forward migration per SQL backend (number `202607270003`, confirmed next/monotonic); order matters.

**Both backends** `Up`:
1. `ALTER TABLE lyntai_curated_memory ADD COLUMN metadata TEXT NULL`.
2. `CREATE TABLE lyntai_curated_meta(...)` + `CREATE INDEX ix_lyntai_curated_meta_kv`.
3. Backfill from the retiring columns, only non-empty values:
   - set `metadata` to the canonical JSON object of the present `source`/`title` (SQLite `json_object`/
     `json_group_object` over the present keys; Postgres `jsonb_strip_nulls(jsonb_build_object(...))::text`),
     NULL when neither present;
   - insert matching `lyntai_curated_meta` rows: `INSERT … SELECT id,'source',source … WHERE source<>''`,
     same for `'title'`.
4. **SQLite only — rebuild the FTS content-only** (it currently indexes `content, title`): drop the 3
   triggers + the `lyntai_curated_fts` table, recreate `fts5(content, …, tokenize='trigram')` + 3
   content-only triggers, backfill from `content`. Must precede step 5 so no trigger references `title`.
   **Postgres only** — `DROP INDEX ix_lyntai_curated_title_trgm` (content GIN stays).
5. `ALTER TABLE lyntai_curated_memory DROP COLUMN title; … DROP COLUMN source` (SQLite one-per-statement;
   Postgres `DROP COLUMN title, DROP COLUMN source`).

`Down` reverses best-effort (re-add nullable `source`/`title`, restore from `metadata`, rebuild the
title-aware FTS/GIN, drop the metadata column + index table). InMemory needs no migration.

`title`'s add-migration (`202607270001`) and the search migration (`202607270002`) are **unreleased**, so we
*could* edit them in place; we keep append-only (never edit past migrations). The add-then-drop of `title`
inside the unreleased batch is cosmetically redundant but harmless.

## Composition & search consequences (intended)

- `CuratedMemorySections.Compose` (`CuratedMemorySections.cs:45`) drops the bold `**{Title}**:` lead →
  content-only. A consumer wanting a titled lead reads its own `title` metadata key app-side.
- `SearchAsync` matches **content only** (title leaves the FTS/GIN). Finding a row by label is now a
  `metadataMatch` on a known key value.

## Touch-points

- `src/Lyntai.Core/Storage/ICuratedMemoryStore.cs` — record reshape; `AddAsync`/`UpdateAsync` signatures +
  docs; `metadataMatch` on `ListAsync`/`SearchAsync`; document the storage model.
- `src/Lyntai.Core/…/CuratedMetadataJson.cs` — the new codec (dict ⇄ canonical JSON string).
- `src/Lyntai.Core/Cortex/CuratedMemorySections.cs` — drop the Title lead.
- `src/Lyntai.Storage.{Sqlite,Postgres}/*CuratedMemoryStore.cs` — remove `source`/`title` from Cols/INSERT/
  UPDATE/Row; serialize/parse `metadata` column; write/replace index rows in the Add/Update transaction;
  `metadataMatch` EXISTS on reads; content-only search.
- `src/Lyntai.Storage.InMemory/InMemoryCuratedMemoryStore.cs` — `Metadata` on the entry, replace-on-update,
  dict-lookup filter, content-only search.
- `src/Lyntai.Storage.{Sqlite,Postgres}/Migrations/M202607270003_CuratedMetadata.cs`.
- `tests/Lyntai.Tests/Storage/CuratedMemoryStoreContract.cs` + `CuratedMemoryStoreTests.cs` +
  `PostgresStorageTests.cs` — retire `Title_*`; move `source:`/`title:` usages onto `metadata:`; new
  contract methods (below).
- `tests/Lyntai.Tests/Api/Baselines/*.txt` (4) — regenerate.
- `CHANGELOG.md` (Unreleased) — replace the CMEM3 `Title` entry; add the metadata entry; **Breaking**:
  `AddAsync`/`UpdateAsync` drop `source`/`title`, `CuratedMemory` drops `Source`/`Title`.
- `docs/task-archive.md` Part 22 + `TASKS.md` — reconcile the CMEM3/CMEM5 records with the reshape.
- migration-count pins in tests (bump for the new migration).

## Testing (TDD, all three backends)

Contract methods via `CuratedMemoryStoreContractFacts` (InMemory + SQLite) + the Postgres shared container
(Uid-namespaced): metadata add/get round-trip (incl. keys once served by `source`/`title`); update replaces
the whole map; empty map clears; a value with quotes/unicode (CJK) proves the codec; `metadataMatch`
requires all pairs (AND) and composes with `kind`/`taskKey`/`scope`/`enabledOnly` on `ListAsync` **and**
`SearchAsync`; `RemoveAsync` cascades the index rows; dedup ignores metadata. `verify` green
(build · test · e2e · check-sensitive) is the gate.

**Backfill coverage.** The per-test `TempDb` migrates to HEAD, where `source`/`title` are gone — so the
column→metadata copy can't be exercised through the store. Plan: a version-targeted migration test (migrate
to `202607270002`, insert rows with `source`/`title`, run `202607270003`, assert both surface in `Metadata`
and in the index) if the harness can target a version; else cover the backfill SQL with a standalone fixture.

## Out of scope / deferred

- Typed/nested JSON values.
- Richer query operators (key-exists, prefix, subtree) and a `ForCompositionAsync` metadata filter.
- Rendering metadata into composed prompts (it's app payload).
- Any column→metadata folds beyond `Source`/`Title` (`kind`/`task`/`scope`/`enabled` stay typed).
