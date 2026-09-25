---
name: add-storage-backend
description: Use when adding a new storage backend to Lyntai (a new Lyntai.Storage.* package implementing one or more of the TWELVE domain interfaces a backend implements — IKeyValueStore, IConversationStore, IMemoryStore, IScoreStore, ITraceStore, IPromptVersionStore, IJobStore, ICuratedMemoryStore, IVectorStore, IResponseCache, IUsageTracker, IMemoryGraphStore — for Postgres, etc.). Covers the repository pattern, FTS, migrations, and the load-bearing SQLite/SQL traps.
---

# Add a storage backend to Lyntai

Read `.claude/knowledge/extending-lyntai.md` §Add a storage backend (which interfaces, and what
`IMemoryGraphStore` costs) and **all of** `.claude/knowledge/storage.md`, the one statement of the traps that
pass tests while being wrong. Each item below is the rule; the why is there.

## Checklist
- [ ] A backend needing nothing beyond Core is a FOLDER in `src/Lyntai.Storage.Basic/`, not a package
      (`docs/DECISIONS.md` D173). One that drags a database driver earns its own package (D25): scaffold it
      with `node devtools/dev.mjs new-package Lyntai.Storage.<Backend>`, never by hand — the misses are silent.
- [ ] Implement only the domain interfaces the consumer needs — they are independent, and there are
      TWELVE (`extending-lyntai.md` §Add a storage backend lists them; `IModelRoutingStore` is served by Core
      over your `IKeyValueStore`). Skip `IMemoryGraphStore` deliberately, never by omission.
- [ ] Each interface you implement owes a `<Domain>StoreContract` fact; no cross-domain coupling.
- [ ] A relational backend links `src/Shared/Relational/*.cs` (`<Compile Include … LinkBase>`, as the SQLite
      and Postgres projects do) rather than copying `DapperConventions` or `GovernanceGuard` (`storage.md`
      §Don't "dedup").
- [ ] Every connection through the factory, with the per-connection pragmas (`storage.md` §Per-connection pragmas).
- [ ] Parameterize every value; alias every SELECTed column; `CAST(x AS REAL)` on every double
      (`storage.md` §The integer-affinity trap); prefer `INSERT … RETURNING id`.
- [ ] Searchable text → FTS with three triggers and a backfill: copy `M202607280003_Memory` (`storage.md` §FTS5).
- [ ] Materialization types are named `Row` / `<Thing>Row` — never `*Dto` (`.claude/rules/repo-mechanics.md` §Naming).
- [ ] Migrations from `dev.mjs new-migration`, carrying both tags (`storage.md` §Migrations; the `add-migration` skill).
- [ ] Prefix every object `lyntai_`.
- [ ] `Use<Backend>Storage(this LyntaiBuilder, …)` registers the factory + stores + runs migrations.
- [ ] Integration tests against a per-test temp db (create → migrate → delete); prove FTS substring
      recall (incl. a CJK substring); guard the affinity round-trip.
- [ ] `node devtools/dev.mjs verify` green.
