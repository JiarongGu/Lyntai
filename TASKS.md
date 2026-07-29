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
`docs/task-archive.md` Part 23 and `docs/DECISIONS.md` D18. EMB1 (built-in OpenAI-compatible embedder)
landed 2026-07-27 — see archive Part 24. CLI2 (the Windows npm-shim spawn bug found consuming 1.2.0) landed
2026-07-30 — see archive Part 28. Open work is the post-1.0 backlog in Part 25 below, plus one CONDITIONAL
item:_

- [ ] **JSON source-gen envelopes (optional; see `docs/DECISIONS.md` D17)** — typed
  `JsonSerializerContext` envelope types for the STABLE response envelopes only, **if envelope-parsing bugs ever
  materialize** (none have). Not a license to reintroduce reflection serialization.

> Add new tasks here as checklist items with an `id` and a short `file:line` where known. Group related
> tasks under a `## Part N — <theme>` heading. Move an item to the archive when it lands — don't leave a
> `[x]` here.

---

## Part 25 — post-1.0 backlog (deferred at the 1.0 API-review triage, 2026-07-28)

_Additive / non-breaking items surfaced by the 1.0 adversarial API review + consumer-usage review (the
working record was `devtools/_review/*`; rejects + rationale are in `docs/DECISIONS.md` D21). None block
1.0 — each is safe to add in a post-1.0 minor._

- [ ] **async migration entry points** — `MigrateUpAsync(…, CancellationToken)` twins alongside the sync
  `MigrationRunnerService.MigrateUp` (SQLite + Postgres), for apps owning their schema under
  `SchemaMigration.None`.
- [ ] **semantic-memory wiring helper** — a DI seam / `Use*` helper so an app enabling semantic recall
  doesn't hand-construct `SqliteCuratedMemoryStore` / `SqliteVectorStore` / `MigratingConnectionFactory` /
  `HttpEmbedder` (a consumer does this today). Those concrete types STAY public for 1.0.
- [ ] **`AddMcpTools` convenience overload** — `params ITool[]` and/or document the
  `await McpToolset.FromClientAsync` → `AddMcpTools` two-step as the intended shape.
- [ ] **verdict helpers** — `reply.IsOk()` / `reply.IsRateLimited()` extension(s) to cut the 3-branch
  `LlmVerdict` pattern at call sites.
- [ ] **curated-memory ergonomics** — a `Source`/metadata convenience accessor (apps unpack
  `metadata["source"]` by hand after CMEM6); reconsider the delete+re-add for immutable `kind`/`task`/`scope`.
- [ ] **agent-event contract** — `ClaudeToolCalls.FilePathOf` should also read `notebook_path`/`path`;
  consider a discoverable event-shape contract instead of anonymous objects apps reflect over.
- [ ] **member/type XML docs** — `ExtensionsAiProvider` public ctor, `LyntaiChatClientExtensions` type
  summary, `ClaudeCliProvider` interface members, `AddMcpTools` intended-shape doc.
- [ ] **`OpenAiCompatibleOptions.ContextSize` legibility** — Ollama-only option with a generic name; a
  rename (e.g. `OllamaContextSize`) is BREAKING, so it's a major-bump-or-never item — accepted as-is for
  1.0, revisit only if it causes real confusion.

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
