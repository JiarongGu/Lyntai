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

### Part 14 — App-configurable memory retention policy (multi-strategy) (2026-07-22)

Give the consuming app CONTROL over how `IMemoryStore` bounds its size — multiple retention STRATEGIES
selected via configuration, mirroring how `RoutingPolicy` (DECISIONS D10) makes fallback configurable.
Today there's ONE fixed approach: a per-`(taskKey, scope)` count cap (`LyntaiOptions.MemoryCapPerScope`,
default 500, oldest-trimmed FIFO on write) + an optional per-call `ttl` + manual `PruneAsync`. The app can
tune the cap NUMBER but not the STRATEGY. (This is the "auto-manage memory size" ask, made app-controlled.)

- [ ] **R1 · `MemoryRetentionPolicy` on `LyntaiOptions.Memory`** — configurable via `AddLyntai` /
  `ConfigureMemory(...)` + `LYNTAI_MEMORY_*` env; a multi-strategy retention seam the app selects:
  - **CountCap** (default — reproduces today): cap N per `(taskKey, scope)`, evict on write; add an
    eviction mode `Fifo` (oldest-created, current) vs `Lru` (least-recently-recalled).
  - **Ttl**: a default max-age applied to all entries (dropped from recall + reaped), beyond the per-call `ttl`.
  - **Manual**: no built-in cap/TTL — the app fully owns size via `PruneAsync`/`ForgetAsync` (opt out of bounding).
  - (optional) **Composite**: cap + ttl together.
  - Default MUST reproduce current behavior (CountCap/Fifo/500). Thread the policy through all three
    backends (Sqlite/InMemory/Postgres) consistently with cross-backend contract tests; keep it behind the
    existing seam (no call-site changes); update the `ApiSurface` baselines deliberately.
  - Auto-prune: either an app-owned hook/schedule that runs `PruneAsync` (app owns the pump, per the jobs
    pattern) OR document that pruning stays app-driven. **Confirm the strategy set + auto-prune scope with
    the user before building.**

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
