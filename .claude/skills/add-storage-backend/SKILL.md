---
name: add-storage-backend
description: Use when adding a new storage backend to Lyntai (a new Lyntai.Storage.* package implementing one or more of the domain interfaces — IKeyValueStore, IConversationStore, IMemoryStore, IScoreStore, ITraceStore — for Postgres, etc.). Covers the repository pattern, FTS, migrations, and the load-bearing SQLite/SQL traps.
---

# Add a storage backend to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add a storage backend) and **all of**
`.claude/knowledge/storage.md` first — it lists the invariants that pass tests while being wrong.

## Checklist
- [ ] New packable project `src/Lyntai.Storage.<Backend>/`, project-ref `Lyntai.Core` only.
- [ ] Implement only the domain interfaces the consumer needs — they're independent (`IKeyValueStore`,
      `IConversationStore`, `IMemoryStore`, `IScoreStore`, `ITraceStore`). No cross-domain coupling (a
      future composite store routes domains to different backends).
- [ ] `IDbConnectionFactory` (or equivalent) applies the backend's concurrency/integrity settings on
      **every** connection. For SQLite that's `WAL; busy_timeout; foreign_keys=ON` — miss `foreign_keys`
      and cascades silently stop.
- [ ] Repositories: parameterize every user value; alias columns explicitly; read every 0..1/double
      column as `CAST(x AS REAL)` (SQLite affinity trap); prefer `INSERT … RETURNING id`.
- [ ] Searchable text → FTS (SQLite: trigram external-content + AFTER INSERT/**DELETE**/**UPDATE**
      triggers emitting the `'delete'` row + in-migration backfill — copy `M202607170003_Memory`).
- [ ] Migrations numbered uniquely (`dev.mjs new-migration`); constraints inline at create.
- [ ] Prefix every object `lyntai_` (Lyntai may share the consumer's database).
- [ ] `Use<Backend>Storage(this LyntaiBuilder, …)` registers the factory + stores + runs migrations.
- [ ] Integration tests against a per-test temp db (create → migrate → delete); prove FTS substring
      recall (incl. a CJK substring); guard the affinity round-trip.
- [ ] `node devtools/dev.mjs verify` green.
