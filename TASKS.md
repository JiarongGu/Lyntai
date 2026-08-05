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

_**v2.0.1 is released (2026-08-04).** Everything up to and including the generation platform, the package
restructure, the 2.0.1 release hardening, the generation-ergonomics follow-ups and the provider-lifetime seam
has shipped and is archived — see `docs/task-archive.md` Parts 29–37 and `docs/DECISIONS.md` D25–D37. Part 34's
verdict-parity finding closed 2026-08-05 (`LlmVerdict.NotConfigured`), emptying that part. What remains open is
below: the generation follow-ups in Part 33 (**all** now needing a real service, a vendor pick or a design call — none is
codeable from here), the verdict-translation gap in Part 38 (found while closing Part 34), the codex surface
still to MEASURE in Part 39 (opened while closing CLI11 — also not codeable from here), the post-1.0
additive backlog in Part 25, and one conditional item:_

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

## Part 38 — verdict-translation gaps found while closing Part 34 (2026-08-05)

_Found while adding `LlmVerdict.NotConfigured` (`docs/DECISIONS.md` D38). Not fixed there: each changes
RELEASED generation behaviour and deserves its own considered commit, exactly as the Part 34 verdict change
did — not a rider on an unrelated one._

- [ ] **`GenerationVerdictClassifier.Translate` flattens `Unsupported` to `Failed`** —
  `src/Lyntai.Core/Generation/GenerationVerdictClassifier.cs:52`. `LlmVerdict.Unsupported` falls through the
  `_ =>` arm to `GenerationVerdict.Failed`, even though `GenerationVerdict.Unsupported` exists and means the
  same thing ("this backend cannot do THIS request — a capability gap, not a fault"). The method's own doc
  contradicted the code until 2026-08-05, naming `ContextWindowExceeded` as the only intended collapse; the
  doc now records the gap instead of hiding it. **What a consumer observes:** a capability gap arriving
  through the shared corpus — a consumer-registered `AddErrorTextMatcher` returning `Unsupported`, or an
  exception classified into it — is reported as a generic `Failed`, so `GenerationRoutingPolicy` gives it
  `PenalizeAndAdvance` (counts toward the dead-host threshold) instead of `Advance`, and repeated capability
  gaps bench a healthy backend. Same shape as the Part 34 masking bug, one translation layer along. Fix is
  one arm, but it changes a released verdict mapping: needs its own commit, a `CHANGELOG.md` entry, and a
  test that a translated `Unsupported` is not penalised.

---

## Part 39 — CLI backends: the codex surface still to MEASURE (2026-08-05)

_Opened while closing CLI11 (`CodexAgentSession`; see `docs/task-archive.md` Part 39 and
`docs/DECISIONS.md` **D40**). CLI11 shipped the honest subset: the message/usage/terminal half of the codex
mapping is measured, the tool-step half is inferred, and the inference is written shape-driven so a wrong
guess costs fewer events rather than wrong ones. What is left is measurement, and measurement only — nothing
here is codeable without a real codex run._

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
  `CodexAgentReader`'s docblock, the README's codex bullet and `DECISIONS.md` D40 — and extend
  `devtools/scripts/codex-stub.mjs` with the real shapes (its header forbids inventing them, which is why the
  inferred cases are covered only by `FakeProcessRunner` fixtures today). A friendlier `ToolResult.Content`
  projection (the readable output field instead of the raw item JSON) becomes possible at the same time, and
  is additive.
- [ ] **CLI13 — measure codex's resume command, so the session can honour `ResumeToken`.**
  `src/Lyntai.Providers.Default/CodexAgentSession.cs`. Today a non-null `ResumeToken` is REFUSED without
  spawning (a single `SessionEnded` with `LlmVerdict.Unsupported`) — silently ignoring it would start a fresh
  session and lose the conversation, and guessing is unusually expensive on this CLI because
  `codex [OPTIONS] [PROMPT]` reads an unrecognized subcommand as a PROMPT and spends a turn answering the
  thread id. **Needs `codex exec --help` on a real install** to confirm the resume shape (and where the `-`
  stdin marker goes relative to it). Until then the refusal is the correct behaviour, not a placeholder.

  _Decide alongside it — deliberately NOT done in CLI11:_ `IAgentSession` has **no capability query**, so a
  UI written polymorphically over it discovers the refusal only at turn time (a wasted round trip and an error
  state a "Continue" button could have been disabled for). Adding one is a CORE change to a released
  interface, which is why it was not slipped into a provider commit. If codex turns out to support resume,
  the question evaporates; if it does not, the choice is between a capability query on `IAgentSession` and
  leaving the turn-time refusal as the only signal — an owner call, not a mechanical one.

---

## Part 25 — post-1.0 additive backlog (1.0 API review)

_Additive / non-breaking items surfaced by the 1.0 adversarial API review + consumer-usage review (the
working record was `devtools/_review/*`; rejects + rationale are in `docs/DECISIONS.md` D21). None block
1.0 — each is safe to add in a post-1.0 minor._

_The ergonomics batch (verdict helpers, the `AddMcpTools` overload, the agent-event contract, the
curated-metadata accessor, the member/type docs) closed 2026-08-05 — see `docs/task-archive.md` Part 25 and
`docs/DECISIONS.md` **D39**. The storage/wiring pair (async migration entry points, the semantic-memory
wiring helper) closed 2026-08-05 too — `docs/DECISIONS.md` **D40**/**D41**. What is left below is the work
that was never additive or never small._

- [ ] **curated-memory: can `taskKey`/`scope` move in place?** — the half of the old "curated-memory
  ergonomics" item that is NOT additive. The metadata accessor shipped 2026-08-05; re-scoping an entry still
  means delete + re-add. Two reasons it was left rather than done (`docs/DECISIONS.md` D39):
  - **It is a break, not an addition.** `kind` is already updatable (CMEM5); adding `taskKey`/`scope` means
    new parameters on `ICuratedMemoryStore.UpdateAsync` — a signature change on a released interface that
    every BYO implementation must follow. Out of scope for an additive batch; needs the D24 documented-break
    route and its own commit.
  - **It has an unanswered semantics question.** `(kind, content, taskKey, scope)` is the DEDUP IDENTITY of
    `AddAsync(dedup: true)`, so moving one of those fields in place mutates identity and can silently produce
    the duplicate the dedup contract promises not to. `kind` already has this hole. Settle all four together
    — decide whether an identity-mutating update collides, refuses, or merges — rather than widening it by
    two. Contract tests would then need a case per backend (`CuratedMemoryStoreContract`).
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
