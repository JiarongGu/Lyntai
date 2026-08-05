---
name: add-storage-backend
description: Use when adding a new storage backend to Lyntai (a new Lyntai.Storage.* package implementing one or more of the TWELVE domain interfaces — IKeyValueStore, IConversationStore, IMemoryStore, IScoreStore, ITraceStore, IPromptVersionStore, IJobStore, ICuratedMemoryStore, IVectorStore, IResponseCache, IUsageTracker, IModelRoutingStore — for Postgres, etc.). Covers the repository pattern, FTS, migrations, and the load-bearing SQLite/SQL traps.
---
<!-- local: never synced; not a daoris artifact -->

# Add a storage backend to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add a storage backend), **all of**
`.claude/knowledge/storage.md` — it lists the invariants that pass tests while being wrong — and
`.claude/knowledge/sql-storage.md`, the canonical statement of the traps themselves (affinity casts,
never reuse a migration number, trigram FTS, explicit pragmas). `storage.md` is this repo's binding of
that canonical set; read both, they are not duplicates.

## Checklist
- [ ] New packable project `src/Lyntai.Storage.<Backend>/`, project-ref `Lyntai.Core` only. A storage
      driver DOES earn its own package — it drags a database dependency a consumer might refuse
      (`docs/DECISIONS.md` D31). Scaffold it with `node devtools/dev.mjs new-package
      Lyntai.Storage.<Backend>`, which writes all NINE registries `check-packages` gates; never register
      them by hand, because the misses are silent (a package absent from `ApiSurfaceTests.Assemblies()`
      has no API gate at all).
- [ ] Implement only the domain interfaces the consumer needs — they're independent, and there are
      **twelve**, not five: `IKeyValueStore`, `IConversationStore`, `IMemoryStore`, `IScoreStore`,
      `ITraceStore`, `IPromptVersionStore`, `IJobStore`, `ICuratedMemoryStore`, `IVectorStore`,
      `IResponseCache`, `IUsageTracker`, `IModelRoutingStore` (`src/Lyntai.Core/Storage/` plus `Memory/`,
      `Llm/Caching/`, `Llm/Budgeting/`, `Llm/Routing/`; mirror `src/Lyntai.Storage.Postgres/`, which
      implements eleven of them). Each one you DO implement owes a `<Domain>StoreContract` fact. No
      cross-domain coupling (a future composite store routes domains to different backends).
- [ ] `IDbConnectionFactory` (or equivalent) applies the backend's concurrency/integrity settings on
      **every** connection. For SQLite that's `WAL; busy_timeout; foreign_keys=ON` — miss `foreign_keys`
      and cascades silently stop.
- [ ] Repositories: parameterize every user value; alias columns explicitly; read every 0..1/double
      column as `CAST(x AS REAL)` (SQLite affinity trap); prefer `INSERT … RETURNING id`.
- [ ] Searchable text → FTS (SQLite: trigram external-content + AFTER INSERT/**DELETE**/**UPDATE**
      triggers emitting the `'delete'` row + in-migration backfill — copy `M202607280003_Memory`).
- [ ] Materialization types (the settable-property Dapper landing type) are named `Row` / `<Thing>Row` —
      **never `*Dto`** (`.claude/rules/repo-mechanics.md` §Naming).
- [ ] Migrations numbered uniquely (`dev.mjs new-migration`); constraints inline at create; each tagged
      `[Tags(nameof(StorageFeature.<Feature>), StorageFeatures.AllTag)]` — an untagged one runs under
      every feature set, so a disabled domain still lands its table.
- [ ] Prefix every object `lyntai_` (Lyntai may share the consumer's database).
- [ ] `Use<Backend>Storage(this LyntaiBuilder, …)` registers the factory + stores + runs migrations.
- [ ] Integration tests against a per-test temp db (create → migrate → delete); prove FTS substring
      recall (incl. a CJK substring); guard the affinity round-trip.
- [ ] `node devtools/dev.mjs verify` green.
