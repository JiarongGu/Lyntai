---
name: add-migration
description: Use when adding or changing a database schema in Lyntai.Storage.Sqlite (a new FluentMigrator migration — new table, column, index, or FTS search table). Covers safe numbering, SQLite constraints, and the FTS trigger pattern.
---

# Add a migration to Lyntai.Storage.Sqlite

Read both first: `.claude/knowledge/sql-storage.md` is the canonical statement of the traps (affinity
casts, never reuse a migration number, trigram FTS, explicit pragmas); `.claude/knowledge/storage.md` is
this repo's binding of them (the `lyntai_` prefix, the SQLite/Postgres parallelism, the `StorageFeature`
tags). Never hand-pick a migration number — a reused number is silently skipped and the migration never
runs.

## Steps
1. Scaffold: `node devtools/dev.mjs new-migration <name>` → creates
   `src/Lyntai.Storage.Sqlite/Migrations/M<num>_<Name>.cs` with a unique, monotonic `yyyyMMddHHmm`
   number, the `[Migration(<num>)]` class, and a `[Tags(...)]` placeholder that deliberately does not
   compile until you name the feature (step 2).
2. Fill the tag and `Up()`:
   - [ ] Tag it: `[Tags(nameof(StorageFeature.<Feature>), StorageFeatures.AllTag)]` — every shipped
         migration carries both. The feature tag is what a SUBSET pass requests; `AllTag` is what the
         default `StorageFeature.All` pass requests. **An untagged migration runs under EVERY feature
         set**, so a domain the app disabled would still land its table, and nothing reports it.
   - [ ] Prefix every object `lyntai_`. snake_case columns.
   - [ ] Composite PK + FK **inline at `Create.Table`** (SQLite can't `ALTER ADD CONSTRAINT`); `ON DELETE
         CASCADE` via raw `Execute.Sql` if needed.
   - [ ] Store a 0..1/double column? Remember to `CAST(x AS REAL)` wherever a store SELECTs it.
   - [ ] Searchable text? Add an FTS5 **trigram** external-content mirror + **three** triggers (AFTER
         INSERT, DELETE, UPDATE — delete/update emit the `'delete'` command row) + an in-migration
         backfill. Copy `M202607280003_Memory.cs` exactly; adjust columns only.
3. If it's a new domain: the `I<Domain>Store` interface in Core, then the SQLite impl registered in
   `UseSqliteStorage`, the **Postgres impl plus a Postgres migration carrying the SAME number**
   (`src/Lyntai.Storage.Postgres/Migrations/` mirrors SQLite file-for-file; `new-migration` scaffolds only
   the SQLite half, so the twin is hand-written and NOTHING fails if you forget it — `docs/DECISIONS.md`
   D9: "keep the SQLite + Postgres parallels of the same number in sync"), and an InMemory impl where the
   domain has one (8 of 12 do; the Governance-backed stores deliberately do not). Then a
   `<Domain>StoreContract` fact class in `tests/Lyntai.Tests/Storage/`: the contract facts, not a shared
   base class, are what keep the backends from drifting (`.claude/knowledge/storage.md` §Don't "dedup" the
   Sqlite/Postgres stores). If it's a change to an existing table, update the affected store's SQL.
4. Add/extend the integration test against a temp db (migrate → round-trip; prove FTS recall if you added
   search).
5. `node devtools/dev.mjs verify` green (the runner is idempotent; re-running is a no-op).
