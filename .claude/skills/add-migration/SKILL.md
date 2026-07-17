---
name: add-migration
description: Use when adding or changing a database schema in Lyntai.Storage.Sqlite (a new FluentMigrator migration — new table, column, index, or FTS search table). Covers safe numbering, SQLite constraints, and the FTS trigger pattern.
---

# Add a migration to Lyntai.Storage.Sqlite

Read `.claude/knowledge/storage.md` first. Never hand-pick a migration number — a reused number is
silently skipped and the migration never runs.

## Steps
1. Scaffold: `node devtools/dev.mjs new-migration <name>` → creates
   `src/Lyntai.Storage.Sqlite/Migrations/M<num>_<Name>.cs` with a unique, monotonic `YYYYMMDDNNNN`
   number and the `[Migration(<num>)]` class.
2. Fill `Up()`:
   - [ ] Prefix every object `lyntai_`. snake_case columns.
   - [ ] Composite PK + FK **inline at `Create.Table`** (SQLite can't `ALTER ADD CONSTRAINT`); `ON DELETE
         CASCADE` via raw `Execute.Sql` if needed.
   - [ ] Store a 0..1/double column? Remember to `CAST(x AS REAL)` wherever a store SELECTs it.
   - [ ] Searchable text? Add an FTS5 **trigram** external-content mirror + **three** triggers (AFTER
         INSERT, DELETE, UPDATE — delete/update emit the `'delete'` command row) + an in-migration
         backfill. Copy `M202607170003_Memory.cs` exactly; adjust columns only.
3. If it's a new domain, add the `I<Domain>Store` interface (in Core) + its SQLite impl + register it in
   `Use SqliteStorage`; if it's a change to an existing table, update the affected store's SQL.
4. Add/extend the integration test against a temp db (migrate → round-trip; prove FTS recall if you added
   search).
5. `node devtools/dev.mjs verify` green (the runner is idempotent; re-running is a no-op).
