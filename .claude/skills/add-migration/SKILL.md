---
name: add-migration
description: Use when adding or changing a database schema in Lyntai.Storage.Sqlite (a new FluentMigrator migration — new table, column, index, or FTS search table). Covers safe numbering, SQLite constraints, and the FTS trigger pattern.
---

# Add a migration to Lyntai.Storage.Sqlite

Read `.claude/knowledge/storage.md` §Migrations and §FTS5 first — the one statement of the traps; each step
below is the rule and the why is there. Never hand-pick a migration number: a reused one is skipped silently.

## Steps
1. Scaffold: `node devtools/dev.mjs new-migration <name>` → creates
   `src/Lyntai.Storage.Sqlite/Migrations/M<num>_<Name>.cs` with a unique, monotonic `yyyyMMddHHmm`
   number, the `[Migration(<num>)]` class, and a `[Tags(...)]` placeholder that deliberately does not
   compile until you name the feature (step 2). It numbers above both backends and names the Postgres twin.
2. Fill the tag and `Up()`:
   - [ ] Tag it: `[Tags(nameof(StorageFeature.<Feature>), StorageFeatures.AllTag)]` — both, always.
   - [ ] Prefix every object `lyntai_`. snake_case columns.
   - [ ] Composite PK + FK **inline at `Create.Table`**; `ON DELETE CASCADE` via raw `Execute.Sql` if needed.
   - [ ] A 0..1/double column? `CAST(x AS REAL)` wherever a store SELECTs it.
   - [ ] Searchable text? Copy `M202607280003_Memory.cs` exactly (trigram mirror, three triggers, backfill);
         adjust columns only.
3. If it's a new domain: the `I<Domain>Store` interface in Core, then the SQLite impl registered in
   `UseSqliteStorage`, the **Postgres impl plus a Postgres migration carrying the SAME number**
   (`src/Lyntai.Storage.Postgres/Migrations/` mirrors SQLite file-for-file, and NOTHING fails if you forget
   the twin — `docs/DECISIONS.md` D9), and an InMemory implementation in `src/Lyntai.Storage.Basic/InMemory/`
   (a Governance domain's in-memory default lives in Core). Then a `<Domain>StoreContract` fact class in
   `tests/Lyntai.Tests/Storage/`: the contract facts, not a shared base class, keep the backends from
   drifting (`.claude/knowledge/storage.md` §Don't "dedup" the Sqlite/Postgres stores). If it's a change to an
   existing table, update the affected store's SQL.
4. Add/extend the integration test against a temp db (migrate → round-trip; prove FTS recall if you added
   search).
5. `node devtools/dev.mjs verify` green (the runner is idempotent; re-running is a no-op).
