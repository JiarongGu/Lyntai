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
- [ ] **S3 — shared cap-evict SQL for the memory stores.** The `DELETE … NOT IN (SELECT … LIMIT @cap)`
  statement is char-identical in both dialects (count-cap semantics now in three places incl.
  `MemoryEviction.Survivors`). A raw-`DbCommand` helper beside `MemoryEviction.ApplyAsync` would
  single-source it without giving Core a Dapper dependency.
- [ ] **S11 — drop the `(object?)x ?? DBNull.Value` dance in the Postgres stores** for typed nullable
  params (Dapper binds C# null as DBNull already); keep `::type` casts only where Npgsql can't infer.
  Do under the Testcontainers suite — Npgsql null-inference has edge cases.
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
- [ ] **T14 — de-flake the two wall-clock-coupled tests** (`Abandoning_the_stream_kills_the_child`'s fixed
  sleeps → bounded polling; `PerRequestTimeoutTests`' real-delay races → a ct-driven `DelayHandler`).
- [ ] **L10/L11 — rate-limiter half-live options claim + `LlmVerdictClassifier` custom-matcher
  lock/copy-per-call** — both small; bundle with the next LLM-area task.
- [ ] **I14 — bound `StreamLinesAsync`'s stderr capture** (it buffers ALL stderr but only ever uses a
  500-char tail) with a ring/tail reader.
- [ ] **JSON source-gen envelopes (optional; see `docs/DECISIONS.md` D17)** — typed
  `JsonSerializerContext` DTOs for the STABLE response envelopes only, if envelope-parsing bugs ever
  materialize. Not a license to reintroduce reflection serialization.

> The pass's REJECTED findings (deliberately not taken) are recorded in `docs/DECISIONS.md` **D18** —
> per the task-lifecycle rule this file holds open tasks only; don't-relitigate rationale lives there.

### Curated-memory as a titled, searchable catalog (requested by the desktop AI-manager integration, 2026-07-26)
The desktop adopter models its agent memory as a **single titled, source-tagged, keyword-searchable,
individually-CRUD-able note catalog** written by BOTH a human (owner) and the agent. Lyntai's
`ICuratedMemoryStore` (`src/Lyntai.Core/Storage/ICuratedMemoryStore.cs`) already covers kind / source /
enable / task / scope / CRUD — but two gaps stop it from BEING that catalog, so the adopter can't retire
its own FTS5 store yet. Both are generic (any curated-catalog UI / agent-recallable operator knowledge
base wants them), app-agnostic, and additive.

- [ ] **CMEM3 — optional `Title` on `CuratedMemory`.** A curated fact commonly has a short label + a longer
  body (a glossary term → definition, a persona trait → detail, a saved note → title). Add an optional
  `Title` to `CuratedMemory` and to `AddAsync`/`UpdateAsync` (COALESCE-update semantics like the existing
  fields), and have `CuratedMemorySections.Compose` render it as the entry's lead (e.g. `- **{Title}**: {Content}`,
  falling back to Content-only when null). Nullable `title` column migration on SQLite + Postgres
  (`ADD COLUMN`, no backfill — null = untitled, existing rows unchanged), mirrored in InMemory. Additive; no
  breaking change. (The desktop's Memory view shows a bold title + body per entry — today packed into
  Content because there's no Title.)
- [ ] **CMEM4 — keyword `SearchAsync` on `ICuratedMemoryStore`.** The curated store is List-by-kind only
  (`ListAsync`/`ForCompositionAsync`); there is no relevance/keyword lookup, so a consumer building a
  searchable curated catalog — or letting an agent `recall` from the curated set — must load-all-and-filter
  in-app. Add `SearchAsync(query, kind?, task?, scope?, enabledOnly?, limit?)` reusing the SAME per-backend
  index machinery `IMemoryStore` already has (SQLite FTS5-trigram + bm25, Postgres pg_trgm, InMemory
  substring/recency) so the semantics + backend-divergence notes match the lexical store's documented
  guarantee. Keeps the catalog "small and deliberate" — search is an added read path, not capping/TTL.
  Together with CMEM3 this makes `ICuratedMemoryStore` a full titled+searchable catalog an app can adopt
  wholesale (owner rows `source="owner"`, agent rows `source="agent"` via the existing `dedup` add).

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
