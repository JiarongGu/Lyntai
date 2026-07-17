# CLAUDE.md — Lyntai (灵台)

> Auto-loaded every session. Keep short — details live in `docs/` and `.claude/rules/`.

## What this is

**Lyntai** (灵台, "the numinous platform" — the seat of the mind) is a reusable **.NET 10 library**: the
shared **cortex + persistence** substrate extracted from the sibling apps (Gatherlight, Vidora, Sonora)
and the mastra/odysseus studies, so a new project gets **LLM providers + pluggable storage + LLM-ops**
without rebuilding them. It is a *library* (a set of NuGet-packable projects), not an app — no server,
no host, no UI.

The two things it provides: (1) an **LLM provider abstraction** with routing + **fallback** across CLI /
API / `Microsoft.Extensions.AI`-bridged providers, and (2) **pluggable storage** (SQLite now, interfaces
so other backends follow as separate packages) — plus the LLM-ops layer (prompt registry, scoring/eval,
run traces, task-scoped memory) and DI wiring (`AddLyntai(...)`).

## Current state

**Pre-implementation.** The design is locked and the build sequence is written:
- `docs/2026-07-17-lyntai-design.md` — the **contract** (every interface, the two fork decisions, the
  fallback + CLI-hygiene semantics, and what's explicitly out of scope). Read it first.
- `tasks.md` — the **sequence** (Phases 0–7, checkbox tasks, TDD, commit points).
- `devtools/` — the build/test/e2e/pack toolkit, ready to use.

The `src/` projects don't exist yet — Phase 0 in `tasks.md` scaffolds them.

## Rules

- **`.claude/rules/dev-conventions.md`** — the load-bearing patterns: package layout (interface in
  `Lyntai.Core`, impl in an adapter package that depends only on Core; never adapter→adapter), async Dapper
  + `snake_case` + `CAST(x AS REAL)`, FluentMigrator numbering, FTS5-trigram search, claude-CLI spawn
  hygiene, DI-collection variation points (never if/else), the devtools loop.
- **`.claude/rules/sensitive-info.md`** — no dev-machine absolute paths or private tokens in tracked files;
  pre-commit guard (`devtools/scripts/check-sensitive.mjs`). Install once: `node devtools/dev.mjs install-hooks`.
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**
- Working files (probes, scratch) go under `devtools/_*` (gitignored), never OS temp.

## Dev loop

- `node devtools/dev.mjs build` — build the solution.
- `node devtools/dev.mjs test [args]` — run the xUnit tests.
- `node devtools/dev.mjs e2e [pN|all] [--build] [--parallel]` — boot `Lyntai.Playground` against the
  deterministic provider-stub (`LYNTAI_PROVIDER_CMD`) over isolated `devtools/_e2e-*` data folders.
- `node devtools/dev.mjs playground` — run the sample console app.
- `node devtools/dev.mjs pack` — `dotnet pack` the libraries → `publish/packages/`.
- `node devtools/dev.mjs check-sensitive [--tree]` — leak scan.
