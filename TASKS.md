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

_The 2026-07-26 hardening-pass deferrals are all closed (implemented or rejected-with-rationale) — see
`docs/task-archive.md` Part 23 and `docs/DECISIONS.md` D18. Two open items remain: one standing CONDITIONAL
item (JSON source-gen envelopes) and one concrete adopter-driven ergonomics gap (CMEM5):_

- [ ] **JSON source-gen envelopes (optional; see `docs/DECISIONS.md` D17)** — typed
  `JsonSerializerContext` DTOs for the STABLE response envelopes only, **if envelope-parsing bugs ever
  materialize** (none have). Not a license to reintroduce reflection serialization.

- [ ] **CMEM5 — `kind` on `ICuratedMemoryStore.UpdateAsync`.** `UpdateAsync(id, content?, enabled?, source?,
  title?)` can change everything about a curated entry EXCEPT its `kind` (the section it belongs to). A
  catalog-editor consumer that lets the user re-categorise an existing note must therefore remove+re-add
  (losing the id + created_at) just to move it between kinds — the desktop AI-manager adopter does exactly
  this as a workaround. Add an optional `kind:` param to `UpdateAsync` (COALESCE semantics like the other
  fields; `null` = leave unchanged) across all three backends so a re-categorise is a true in-place update.
  Small + additive; generic (any curated-catalog UI wants it). `src/Lyntai.Core/Storage/ICuratedMemoryStore.cs`
  + the SQLite/Postgres/InMemory impls.

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
