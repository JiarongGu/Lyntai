# Lyntai (灵台) — Active Task Backlog

> **This file holds OPEN tasks only** — the live backlog. Completed work is not left here: once a task is
> fully done (committed + verified), its entry is **moved to [`docs/task-archive.md`](docs/task-archive.md)**
> (the completed-task record) rather than checked off in place. See the lifecycle rule
> `.claude/rules/task-lifecycle.md`. `CHANGELOG.md` remains the release-facing log; the archive is the
> per-task record (why/how). The design contract is `docs/2026-07-17-lyntai-design.md`; the forward
> sequence is `docs/ROADMAP.md`.

**Goal:** a NuGet-packable, DI-first .NET 10 library — an LLM provider abstraction (routing + fallback
across CLI / API / MEAI-bridged providers), pluggable storage (SQLite / InMemory / Postgres), and the
LLM-ops layer (prompt registry, scoring, traces, memory). `AddLyntai(...)` and go.

---

## Active backlog

### Part 13 — Assistant coding-system: adopt a sibling project's no-global-memory pattern (2026-07-22)

Modeling this project's Claude Code setup on a sibling project (`report-ui`): project facts belong
in-repo, not in global auto-memory. The convention is DONE (`.claude/rules/no-global-memory.md` + wired
into `RULES_INDEX.md`/`CLAUDE.md`); the content is DONE (the 9 `lyntai-*` memories are migrated into
`docs/DECISIONS.md` — D6/D7 already covered two, D10–D15 added the rest). Only the irreversible cleanup
remains, deferred here at the user's request.

- [ ] **M1 · Clear global auto-memory of project facts** — delete the 9 migrated `lyntai-*` files from
  this project's global auto-memory dir and reset its `MEMORY.md` so global memory holds ONLY
  user-specific prefs (currently none). Verify (per `no-global-memory.md`) that nothing project-specific
  remains in global memory. **Irreversible deletion — confirm before running**; the content is already
  preserved in `docs/DECISIONS.md`, so no information is lost.

> Add new tasks here as checklist items with an `id` and a short `file:line` where known. Group related
> tasks under a `## Part N — <theme>` heading. Move an item to the archive when it lands — don't leave a
> `[x]` here.

---

## How to work a task (evergreen)

- **TDD, every task:** failing test → run it fail → minimal impl → run it pass → commit. Read
  `.claude/rules/dev-conventions.md` (package layout, migrations, spawn hygiene) and the relevant
  `.claude/knowledge/*` + `.claude/skills/*` before extending.
- **Commit per task.** **Never commit without the user's approval.** Describe changes structurally in the
  message (no dev-machine paths / private tokens — the pre-commit guard enforces this).
- **This is a generic library** — every task must be a reusable, app-agnostic improvement behind the
  `ILlmClient` front door / a BYO seam, never app-specific code. Update the `ApiSurface` baselines
  deliberately on any public-surface change.
- **Deviate from a task's suggested steps when the code disagrees** — the spec's *contract* (interfaces,
  semantics) is authoritative; a task's step list is a suggestion. Record real deviations in the commit
  message.
- **When a task completes, archive it** (`.claude/rules/task-lifecycle.md`): move its entry (with the
  completion date + a one-line **Outcome**) into `docs/task-archive.md`, and delete it from here.
