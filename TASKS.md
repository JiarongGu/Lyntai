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

_**v2.1.0 is released (2026-08-04).** Everything up to and including the generation platform, the package
restructure, the 2.0.1 release hardening, the generation-ergonomics follow-ups, the provider-lifetime seam and
the codex agent session has shipped and is archived — see `docs/task-archive.md` Parts 29–39 and
`docs/DECISIONS.md` D25–D43.

**The 2.2.0 pre-release sweep (2026-08-05) closed everything that was closeable**, so what is left below is
**not deferred effort** — every remaining item is blocked on something this repository does not have:

| Closed 2026-08-05 | Was blocked by | Now recorded in |
|---|---|---|
| **Part 43** — the behaviour cluster (17 items) | D24's third bullet | **D44** amended it; archive Part 43 |
| **Part 42** — the API-surface gate's blind spots | "a large mechanical diff" | archive; all 11 baselines regenerated |
| **Part 40** — a blameless AND reportable media verdict | a design call | **D45**(1) |
| **Part 25** — curated-memory re-scope, `ContextSize`, `AsChatClient` | a break + a design call | **D24** + **D45**(2)(3) |
| **CLI13** — codex resume | an unmeasured CLI | measured turn-free against the real 0.146.0; **D42** superseded |

What remains, and what each waits on — **none is codeable from here**:
- **Part 33 / GEN-VERIFY** — a real fal.ai key, and a ~1.7 GB model download for one `sd-cli` render.
- **Part 33 / GEN6 (streaming TTS)** — a vendor pick and a key. Shipping it unmeasured is the exact mistake
  GEN-VERIFY exists to correct.
- **Part 33 / GEN7 (pipelines)** — deliberately deferred until ≥2 real backends exist, plus a 3D survey.
- **Part 41 / CLI12** — codex's tool-step item names need a real turn **that runs tools**, which spends
  tokens. It is the one remaining item that needs the owner's go-ahead rather than a probe.
- The conditional JSON item below, which is correctly not done (no envelope-parsing bug has materialized)._

_**Two consumer items were filed on 2026-08-05 and BOTH closed the same day**, so the "none is codeable"
claim above holds again for everything that remains: **Part 44 / CLI14** (an agent session could be given the
app's own tools only on claude) → archive **Part 44**, `docs/DECISIONS.md` **D47**; and **Part 41 / CLI15** (a
measured codex `turn.failed` capture, which also exposed a real exit-code-precedence defect in
`CliProviderEngine.CompleteAsync`) → archive **Part 45**, `docs/FIXES.md`._

- [ ] **JSON source-gen envelopes (optional; see `docs/DECISIONS.md` D17)** — typed
  `JsonSerializerContext` envelope types for the STABLE response envelopes only, **if envelope-parsing bugs ever
  materialize** (none have). Not a license to reintroduce reflection serialization.

## Part 33 — generation platform: remaining backends + composition

_Part 32 (MED1: the generation platform + the 2.0.1 package restructure) landed 2026-08-04 — see
`docs/task-archive.md` Part 32, `docs/DECISIONS.md` D30/D31, and the plans of record
`docs/2026-08-04-generation-platform-plan.md` + `docs/2026-08-04-restructure-2.0.1-plan.md`. What remains are
that plan's Plans 3–7, each a separate pass because each needs its own measurement._

_GEN3 (local `sd-cli`), GEN4 (durable renders + the fal.ai queue backend), GEN6's tool/MCP bridge half and
GEN5 (governance + telemetry parity) all landed 2026-08-04 — see `docs/task-archive.md` Part 33. GEN3/GEN4 carry
an **unmeasured-surface** caveat to close the first time they run for real: the `sd-cli` argv/size clamping is
ported-not-measured, and fal's wire format is documented-not-measured. (`sd-cli`'s binary-directory working dir
was the third such surface — a consuming app measured it 2026-08-04 and it is now confirmed.)_

- [ ] **GEN-VERIFY — confirm the remaining unmeasured surfaces against reality.** For `sd-cli`: run one render
  and check the argv and the multiple-of-64 clamp. For fal: one submit → poll → fetch with a real key, checking
  the status vocabulary, the result field names and what `cost` reports. Then delete the remaining "unverified"
  notes from the XML docs — or fix the mappings and keep them.

  _**The binary-directory working dir is CONFIRMED (2026-08-04)** and no longer part of this task — measured by
  a consuming app against a real downloaded release: the engine ships `ggml*.dll` beside the exe, so spawning
  from anywhere else fails at load time on a perfectly good install. Already implemented
  (`src/Lyntai.Generation/LocalDiffusionProvider.cs:145`) and pinned by a test._

  _**A consumer's own clamp did more than round, and that difference is worth a decision (2026-08-04).** The
  app that reported the working-dir finding has now migrated its image generation onto `Lyntai.Generation` and
  deleted its backend. Its `ClampSize` rounded to a multiple of 64 **and capped a CPU render at 768px** — not
  for correctness but for usability: on a laptop with no GPU, an accepted `1024x1792` request means ten
  minutes of grinding, which is a worse experience than a refusal. That guard is now this backend's clamp.
  Worth settling alongside the argv: should `LocalDiffusionOptions` carry a max-dimension (or should a CPU
  build cap itself), or is an unbounded size the caller's problem? Either answer is fine written down; the
  consumer will report what the engine actually does with `1024x1792` when it runs GEN-VERIFY's render._

  _Two more facts from that same measurement, **already true here** — recorded so they aren't re-investigated:
  the binary is `sd-cli.exe` (upstream renamed it from `sd.exe`), and the tree contains zero `sd.exe`
  references while `LocalDiffusionOptions.BinaryPath` has no default at all, so there is nothing to correct;
  and it is a plain CPU x64 build (no GPU, no CUDA), which is what makes it viable as a zero-setup backend.
  **The hazard to respect IF binary resolution is ever added:** the release zip contains `sd-cli.exe` AND
  `sd-server.exe`, so a loose `sd`-prefix match selects the SERVER — presenting as a HANG rather than an error,
  because the server starts and waits. Today `BinaryPath` is an explicit host-supplied path with no PATH probe
  and no prefix match, which is precisely why that hazard doesn't exist — don't introduce one._

  _Expect the argv + clamp half to close **from use, not from a harness here**: that consumer's live test stops
  at `--help` (a render needs a ~1.7 GB model download per run), but it is migrating its media stack onto
  `Lyntai.Generation`, and driving a real render with real weights for a real use case is what that migration
  does. Measuring where there is a real setup and a real use case is the owner's stated preference, and is why
  the experimental marker needn't block anything._

- [ ] **GEN6 — streaming audio (TTS).** A streaming TTS backend to exercise `IGenerationStreamProvider` end to
  end — nothing implements that seam yet, so it is the one contract in the platform no real backend has
  exercised. **TTS before music** (owner). Needs a vendor pick and a MEASURED wire format (the GEN-VERIFY
  lesson), so it waits on a key rather than shipping another documented-not-measured surface.
- [ ] **GEN7 — pipelines (3d → image → video)**: ordered stages feeding `artifact.ToInput(role)` forward, with
  per-stage candidates and per-stage failure semantics. Deferred until ≥2 real backends exist so the runner
  isn't designed on guesses. Needs a 3D-backend survey first (mesh vs turntable stills — only the latter chains
  into today's video backends).

> Add new tasks here as checklist items with an `id` and a short `file:line` where known. Group related
> tasks under a `## Part N — <theme>` heading. Move an item to the archive when it lands — don't leave a
> `[x]` here.

---

## Part 41 — CLI backends: the codex surface still to MEASURE (2026-08-05)

_**Renumbered from Part 39 on 2026-08-05.** `docs/task-archive.md` **Part 39** is the CLI11 entry that OPENED
this one, so "Part 39" named a completed archive entry and an open backlog part at the same time and every
cross-reference to it was ambiguous. The archive keeps 39 — it is history, and history does not get
renumbered; this open part took the next free number instead._

_Opened while closing CLI11 (`CodexAgentSession`; see `docs/task-archive.md` Part 39 and
`docs/DECISIONS.md` **D42**). CLI11 shipped the honest subset: the message/usage/terminal half of the codex
mapping is measured, the tool-step half is inferred, and the inference is written shape-driven — which bounds
what a wrong guess can cost to exactly two things: **no payload is invented or dropped**, and **every
uncertainty stays inside the tool-step half**. It does NOT bound the KIND of event, so a tool step's kind is
provisional and only its payload is reliable (the item below is the consequence). What is left is
measurement, and measurement only — nothing here is codeable without a real codex run._

- [ ] **CLI12 — measure codex's tool-step items and confirm (or correct) the inferred mapping.**
  `src/Lyntai.Providers.Default/CodexAgentReader.cs`. The capture behind this backend (codex-cli 0.146.0,
  2026-08-04) ran a trivial `--oss` turn with **no tools**, so the entire tool-step half is inferred and
  marked as such in the XML docs.

  **Why this is not merely cosmetic.** The reader recognises exactly three item names (`agent_message`,
  `reasoning`, `error`) and routes everything else to the tool arm **by elimination**. So a wrong NAME is not
  a missing event, it is a WRONG one: a renamed `reasoning` (codex's historical `agent_reasoning`) becomes a
  fabricated `ToolCall` carrying the model's thought as its arguments, a `todo_list`-style plan update becomes
  one too, and a rename of `agent_message` would cost the `TextDelta` AND `FinalText` AND emit the answer as a
  tool step. Each contradicts `ToolCall`'s documented meaning ("the agent invoked a tool",
  `src/Lyntai.Core/Agents/AgentStreamEvent.cs:18`). Payload is never invented or lost, and the measured
  half (session id / terminal / usage) is unaffected — that is the whole of what the shape-driven mapping
  buys.

  **Confirm in this order — most costly wrong guess first:**
  1. **`agent_message`** — a rename here is the worst case (loses the answer twice over *and* fabricates a
     tool step). Measured today, so this is a regression check, not a discovery.
  2. **`reasoning`** — INFERRED. Confirm the item-type name (vs `agent_reasoning`) and its text field.
  3. **`todo_list`** (and any other non-tool, non-message item type the run emits) — each one currently
     surfaces as a fabricated tool step. Decide per item: recognise and drop, or accept as a tool step.
  4. **The per-item failure signal** — `IsFailedItem` reads only top-level `status`/`exit_code`, and returns
     `false` as a POSITIVE claim of success, so a nested or differently-named signal makes a failed step look
     successful to a failure-highlighting UI.
  5. Then the cheaper two: whether `item.started` is emitted at all (if not, the synthesised `ToolCall` is a
     degradation, not a break), and whether `item.updated` carries partial text worth showing — deliberately
     IGNORED today, because an unmeasured accumulation rule risks double-counting the answer.

  **Then:** flip the docs from INFERRED to MEASURED where they hold — including the scoped safety claim in
  `CodexAgentReader`'s docblock, the README's codex bullet and `DECISIONS.md` D42 — and extend
  `devtools/scripts/codex-stub.mjs` with the real shapes (its header forbids inventing them, which is why the
  inferred cases are covered only by `FakeProcessRunner` fixtures today). A friendlier `ToolResult.Content`
  projection (the readable output field instead of the raw item JSON) becomes possible at the same time, and
  is additive.

_**CLI15** (a measured `turn.failed` shape, filed by `Aurelia` 2026-08-05) closed the same day — see
`docs/task-archive.md` **Part 45**. Three of its four claims were already handled and are now pinned; the
fourth found a real defect in `CliProviderEngine.CompleteAsync` (a non-zero exit masked the backend's own
in-band failure), fixed and recorded in `docs/FIXES.md`._

---

## Part 46 — memory: a named engine seam, then a graph engine that forgets (2026-08-08)

_Design agreed 2026-08-08, nothing implemented. Two specs, written together so the seam is shaped by a real
second implementation rather than only by wrappers over what exists:
`docs/superpowers/specs/2026-08-08-memory-engine-seam-design.md` (Spec A) and
`docs/superpowers/specs/2026-08-08-graph-memory-engine-design.md` (Spec B). **A lands first** — building the
graph engine without the seam means wiring it bespoke and reworking it afterwards. Both are additive: no
released signature or semantic changes, so this is minor-bump work, not D24 material._

_The motivating gap: all three existing memory systems (`IMemoryStore`, `ISemanticMemory`,
`ICuratedMemoryStore`) are single unnamed singletons, so an application wanting a chat memory AND a project
memory has to wrap all of it — the same wrapper in every consumer, none able to share it. And
`MemoryPromptComposer` fills a flat character budget in rank order, so loosely-relevant recall can push a hard
constraint out of the prompt with nothing reporting it._

- [ ] **MEM1 — the memory engine seam (Spec A).** `IMemoryEngine` + `MemoryRef`/`MemoryWrite`/`MemoryQuery`/
  `MemoryItem`/`MemoryRecall`, the optional capabilities (`IExpandableMemory`, `ILinkableMemory`,
  `IForgettableMemory`), `IMemoryEngineFactory` (named lookup, `IHttpClientFactory`-shaped but returning the
  same singleton — hence `Get`, not `Create`), `CompositeMemoryEngine`, wrappers over the three existing
  stores, the fluent builder and the zero-config `AddMemory()`. New surface in `Lyntai.Memory` inside
  `Lyntai.Core`; **no new package**, so no registry dance — but the `ApiSurface` baseline moves and must be
  regenerated deliberately.

  **The two guards that matter, both from measured history:** the composite must forward optional
  capabilities by routing on `MemoryRef.Engine` (decorating a generation provider erased them once, and every
  video render stopped routing while every image render kept working — `.claude/knowledge/pitfalls.md`), and
  `AddMemoryEngine` must not `TryAdd` anything `AddLyntai` registers later (the `DeadHostTracker` shadowing
  bug that 1427 tests missed).

- [ ] **MEM2 — the graph memory engine (Spec B).** `IMemoryGraphStore` in Core; implementations in the three
  EXISTING storage adapters (no new package). Two tables + one FluentMigrator migration
  (`dev.mjs new-migration`, **both** tags), trigram FTS over headline+content with all three triggers and a
  same-migration backfill, `CAST(x AS REAL)` on `stability`/`weight`, settable-property row types. Then the
  composer, the per-engine agent tools, and similarity edges as opt-in enrichment.

  **Two defects designed out before they could ship, worth keeping across a re-read:** (1) the database never
  evaluates the decay curve — `POWER(2, …)` needs `SQLITE_ENABLE_MATH_FUNCTIONS`, and a pluggable curve
  cannot be an SQL expression anyway, so SQL bounds a candidate set via
  `IRetrievabilityPolicy.CandidateCutoff` (a CONSERVATIVE superset) and the policy ranks in app code;
  (2) `stability *= 1 + Reinforce` is unbounded, and ~20 recalls turn a 7-day half-life into 64 years — a hot
  ASSOCIATIVE node silently acquiring authoritative durability without its guarantees. `MaxStability` caps it.

- [ ] **MEM-TUNE — measure the decay defaults, don't ship them as if tuned.** Five constants are guesses
  (`HeadlineChars`, `ReinforceFactor`, initial `stability`, `MinRetrievability`, `MaxStability`,
  `SimilarityK`) and are marked unmeasured in the XML docs. Close it with `MemoryDecaySimulation`: a corpus
  with a KNOWN reuse/noise split, driven over simulated weeks against an injected clock, asserting ≥90% of the
  reused set still above `MinRetrievability` at week 8, ≤10% of the noise, full rank separation, and all of it
  still true at week 16 so the numbers aren't fitted to one point. The constants become whatever satisfies
  those assertions and the test then guards them.

  _**Be precise about what that closes.** A synthetic corpus measures the DYNAMICS and runs in CI, which a
  production corpus cannot; it does NOT establish that real usage has the reuse-to-noise ratio it assumes. So
  it moves the constants from "guess" to "measured against a stated model", and the XML docs must say that
  rather than dropping the caveat. Replacing the model with a real corpus later is a strict improvement, not a
  prerequisite — the same shape as GEN-VERIFY._

---

## How to work a task (evergreen)

- **TDD, every task:** failing test → run it fail → minimal impl → run it pass → commit. Read
  `.claude/rules/dotnet-package-layout.md` (package layout) + `.claude/rules/repo-mechanics.md` (this
  repo's bindings — package names, naming invariant, dev loop, test conventions), then the relevant
  `.claude/knowledge/*` (migrations → `storage.md` / `sql-storage.md`; spawn hygiene →
  `llm-and-router.md`) + `.claude/skills/*` before extending.
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

## Quests from other repositories

_Posted by other repositories in this family, which do not edit this one. Take one with
`daoris quest take <id>`, finish it with `done`, or turn it down with `decline` — declining is a
real answer, and the reason is what the asker can actually act on._

_None open._
