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

### Deferred from the 2026-07-26 foundation-hardening pass
The whole-library review (6 parallel reviewers, ~80 findings) landed its correctness + dedup clusters
(see `CHANGELOG.md` Unreleased). These remaining findings were TRIAGED AND DEFERRED deliberately — each
with the reason; pick up when the trade-off changes.

- [ ] **P5 — extract the 5×-copied provider streaming read-loop into a Core helper.** Every streaming
  provider (`ExtensionsAiProvider`, `OpenAiCompatibleProvider`, `LocalProvider`, `ClaudeCliProvider`,
  `ClaudeAgentSession`) hand-rolls the same manual-enumerator + inactivity-clock re-arm + OCE-filter +
  map-exception-to-terminal loop — the exact pattern that shipped the wall-clock bug twice. Deferred:
  yield/finally semantics must be preserved exactly; do it TDD against the existing inactivity tests as
  its own focused task, not inside a broad pass. Sketch in the review: `Lyntai.Llm.Streaming.ReadWithInactivityClock<T>`.
- [ ] **I5 — ProcessRunner shared session/reap extraction.** `RunAsync`/`StreamLinesAsync` share ~45 lines
  of spawn/stderr-drain/kill-registration/reap scaffolding. The I2 hang fix already landed; the extraction
  is cleanliness. Keep the two CLOCK topologies separate (buffered dual-clock vs streamed single-clock —
  they are different contracts).
- [ ] **S8 — move the remaining 4 Row-DTO pairs (trace/score/prompt-version/usage) to Core** like
  `JobRow`. Deferred: pure materialization with zero dialect content — inert duplication, no fencing-style
  drift risk; weigh the Core-surface bloat before doing it.
- [ ] **T11 — convert the storage contract classes to abstract-class-with-[Fact]s** so a backend can't
  silently skip a contract method (the mechanism that produced the PG coverage holes). Postgres keeps its
  deliberate Uid-subset delegators.
- [ ] **T4 remnants — Postgres coverage:** `IJobStore.FailAsync` (retry-requeue timestamp math) has no PG
  test; the usage-tracker case-sensitivity test lacks its PG leg; response-cache `MaxEntries` eviction is
  SQLite-only; `Curated_memory_crud_and_filters` (PostgresStorageTests) still hand-copies contract methods.
- [ ] **T5 — mid-stream CALLER-cancellation tests** (router after first committed chunk; agent session
  mid-stream) — OCE must propagate, no fallback, no bogus terminal. **T9** — promote the remaining
  SQLite-only memory-lifecycle semantics (TTL-refresh, recency-refresh, scoped/olderThan prune) into
  `MemoryStoreContract`. **T10** — pin curated-dedup casing + write the parallel dedup race test.
- [ ] **JSON source-gen envelopes (optional; see `docs/DECISIONS.md` D17)** — typed
  `JsonSerializerContext` DTOs for the STABLE response envelopes only, if envelope-parsing bugs ever
  materialize. Not a license to reintroduce reflection serialization.

> The pass's REJECTED findings (deliberately not taken) are recorded in `docs/DECISIONS.md` **D18** —
> per the task-lifecycle rule this file holds open tasks only; don't-relitigate rationale lives there.

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
