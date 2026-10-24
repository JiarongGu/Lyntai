---
name: lyntai-storage-contract
applies_when: another repository's database or folder holds Lyntai's data, or it asks which tables or files Lyntai writes, which of them it may read, or what an upgrade does to them
enforces: Lyntai owns every lyntai_* table and the file-system backend's records; read them through the domain interfaces, never by SQL; a storage or migration break is major-only
---

# The data Lyntai writes, and what of it is promised

**Lyntai owns its data in the consumer's own database or folder: every `lyntai_*` table, its version table,
and the file-system backend's record files. The contract is the twelve domain INTERFACES; the table and
file shapes are what keeps them working across an upgrade, not an API for another program.**

## Why

A store a consumer chose is easy to mistake for the consumer's own schema. A table read by SQL from
outside the library sees a column that a later major release reshapes, and a migration that ran against a
database someone else edited has no shape it expects.

## How to apply

### The contracts

- **The domain interfaces**, in `src/Lyntai.Core/Storage/` (`IKeyValueStore`, `IConversationStore`,
  `IMemoryStore`, `IScoreStore`, `ITraceStore`, `IPromptVersionStore`, `IJobStore`, `ICuratedMemoryStore`)
  plus `IResponseCache` (`src/Lyntai.Core/Inference/Caching/`), `IUsageTracker`
  (`src/Lyntai.Core/Inference/Budgeting/`), `IVectorStore` and `IMemoryGraphStore`
  (`src/Lyntai.Core/Memory/`). These are public surface (`lyntai-surface.md` §What is promised).
- **Which backend serves which**: `UseSqliteStorage` (`src/Lyntai.Storage.Sqlite/SqliteStorageBuilderExtensions.cs`)
  and `UsePostgresStorage` (`src/Lyntai.Storage.Postgres/PostgresStorageBuilderExtensions.cs`) serve all of
  them; `UseFileSystemStorage` (`src/Lyntai.Storage.Basic/FileSystem/FileSystemStorageBuilderExtensions.cs`)
  serves only the domains a person reads, and `UseInMemoryStorage` holds nothing past the process. The
  first registration of a domain wins (`TryAdd`), so backends compose.
- **`StorageFeature`** (`src/Lyntai.Core/Storage/StorageFeature.cs`) selects domains; a disabled one lands
  no store and no table, and `None` creates nothing, not even the version table.

### The data it writes

- **Tables**: every table is prefixed `lyntai_`. The exact fresh-database schema is the golden snapshot —
  `tests/Lyntai.Tests/Storage/Snapshots/sqlite-schema.sql` (`MigrationSchemaSnapshotTests`) and
  `pg-schema.txt` (`PgMigrationSchemaSnapshotTests`) — read those rather than a list kept here.
- **Version table**: `lyntai_version_info` (`Migrations/LyntaiVersionTable.cs` in both SQL packages),
  FluentMigrator's record of applied migrations, run at startup by each package's `MigrationRunnerService`.
- **Files**: the file-system backend writes one Markdown record per file under the consumer's
  `FileSystemStorageOptions.Root`, the memory graph under `graph/<engine>/<task>/<scope>/` and its learning
  journal under `graph/<engine>/state/`.
- How the SQL is written (casts, FTS triggers, migration numbering) is `storage.md`.

### What is promised, and what is not

- **Promised**: the interfaces, and that an upgrade within a major migrates existing data forward without
  loss — a storage or migration break is major-only (`docs/DECISIONS.md` D18).
- **Not promised**: reading or writing a `lyntai_*` table, or a record file, from outside the library. No
  document grants it, and the column set changes with migrations. **Not confirmed** whether any consumer
  does so; Aurelia, the one confirmed consumer, would say.
- **Not confirmed**: a downgrade path. Each migration defines `Down()`, but `MigrationRunnerService`
  exposes only `MigrateUp` / `MigrateUpAsync`, and no document promises a downgrade.
