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

<!-- open-items:begin — GENERATED. Edit the per-item `item:` markers, never this table. -->

## Open items — 8 across 5 Parts: 6 blocked, 2 watch

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 95 | 33 | GEN-VERIFY — confirm the remaining unmeasured surfaces against reality | blocked · env | a real fal.ai key; and for the sd-cli half the BINARY as well as a ~1.7 GB … |
| 146 | 33 | GEN6 — streaming audio (TTS) | blocked · decision+env | a TTS vendor pick, then a key — the wire format must be measured, not infer… |
| 155 | 33 | GEN7 — pipelines (3d → image → video) | blocked · tree | a 3D generation backend — the pipeline's first stage has none, and the 3d-t… |
| 209 | 41 | CLI12 — measure codex's tool-step items and confirm (or correct) the inferr… | blocked · env | a codex-cli reinstall on this machine, then one real turn that runs tools |
| 264 | 56 | FSRS-B — parameter FITTING, not published defaults | blocked · data | a deployment's own logged reviews; this repository cannot invent them witho… |
| 319 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 342 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 399 | 99 | `SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another ca… | watch · data | a recurrence with a full stack — the three hypotheses a reading can reach a… |

<!-- open-items:end -->

---

## Active backlog

_**The archive is where closed work lives** — `docs/task-archive.md`, one Part per task, with why and how;
this file does not summarize it. `CHANGELOG.md` is the release-facing log, and everything before 3.0 is
history rather than context (`repo-mechanics.md`)._

**What is open is the TABLE at the head of this file, and it is GENERATED.** Every checkbox carries an
`<!-- item: state=… kind=… needs="…" -->` marker; `node devtools/dev.mjs check-backlog --write` rebuilds the
table from those markers and `verify` fails while the two disagree, so the roster and the items can no
longer drift apart. Edit the marker, never the table. **The startable set is ZERO items**, which is a
STATE and not an achievement: everything open is blocked or watching, so the next work has to be FOUND
rather than picked up. The review archived as `docs/task-archive.md` Part 221 is how the last seven
arrived, and Parts 223–229 are where they went. **An `env` blocker EXPIRES
SILENTLY** — Part 65 read "this machine holds exactly one chat model" for two weeks while three sat on
disk, every one already used by other measurements here. Nothing fails when an environment GROWS, so
nothing announces it; **re-check every `env` item before concluding there is no work.**
**A REFUTATION is the fifth route an item arrives by, and the most reliable**: disproving the obvious answer
names the next candidate, which is how Part 217 arrived narrowed to one signal of three. The others are a
ruling, a captured failure, and a design REVIEW — the only one schedulable on purpose, and the only one
that has twice filled a whole Part in one pass (Part 179, then Part 221) where every other route yields
ones and twos. `decision-only` is still
EMPTY. That sentence is hand-written on purpose and gated by `check-counts`: the banner it replaces
advertised finished work **four** times, and nothing derived it.

**Where things stand is NOT summarized here, deliberately.** `docs/memory-measurements.md` §5 is the measurement record,
`docs/task-archive.md` holds one Part per closed task, `docs/DECISIONS.md` holds what was decided and why,
and `docs/FIXES.md` holds per-incident fixes. A copy of any of those goes stale the moment the original is
amended, which is why this section stopped carrying one on 2026-09-10.

_**Why this section is short, and the discipline that keeps it short.** It reached 478 lines carrying ZERO
open checkboxes — five stacked `HANDOVER` blocks and a running tally of what had closed, for 17 open items
in a 1308-line file. `task-lifecycle.md` already forbade exactly that ("never let the backlog SUMMARIZE the
archive"), and the section had even documented deleting a 49-line tally for that reason on 2026-09-03 —
then regrew a 19-line one in its place. **A rule that keeps being violated is a missing gate**, so
`check-backlog` now bounds this section; see `dev.mjs`. The content was not lost: the handovers describe
Parts 143–176, which is where they live._

**BLOCKED IS PER ITEM, never per Part** — which is why the state lives on the checkbox rather than in a
roster of Parts. Part 33 was once marked blocked in full while two startable pieces sat inside it (they
closed as **D67** and **D68**). A blocked item names its blocker's KIND as well — `tree`/`env`/`decision`/
`data` — because each is refuted by looking somewhere different, and the rest of that discipline is
`task-lifecycle.md`'s. `watch` and `decision-only` exist because a checkbox can express neither, and both
were being counted as startable work.

## Part 33 — generation platform: remaining backends + composition

_Part 32 (MED1: the generation platform + the 2.0.1 package restructure) landed 2026-08-04 — see
`docs/task-archive.md` Part 32, `docs/DECISIONS.md` D24/D25, and the plans of record
`docs/2026-08-04-generation-platform-plan.md` + `local/superpowers/plans/2026-08-04-restructure-2.0.1-plan.md`. What remains are
that plan's Plans 3–7, each a separate pass because each needs its own measurement._

_GEN3 (local `sd-cli`), GEN4 (durable renders + the fal.ai queue backend), GEN6's tool/MCP bridge half and
GEN5 (governance + telemetry parity) all landed 2026-08-04 — see `docs/task-archive.md` Part 33. GEN3/GEN4 carry
an **unmeasured-surface** caveat to close the first time they run for real: the `sd-cli` argv/size clamping is
ported-not-measured, and fal's wire format is documented-not-measured. (`sd-cli`'s binary-directory working dir
was the third such surface — a consuming app measured it 2026-08-04 and it is now confirmed.)_

- [ ] **GEN-VERIFY — confirm the remaining unmeasured surfaces against reality.** For `sd-cli`: run one render <!-- item: state=blocked kind=env needs="a real fal.ai key; and for the sd-cli half the BINARY as well as a ~1.7 GB model — neither is on this machine" -->
  and check the argv and the multiple-of-64 clamp. For fal: one submit → poll → fetch with a real key, checking
  the status vocabulary, the result field names and what `cost` reports. Then delete the remaining "unverified"
  notes from the XML docs — or fix the mappings and keep them.

  _**RE-CHECKED 2026-09-15, and the `needs` was UNDERSTATING it.** It read "a ~1.7 GB model download",
  which reads as one step; the `sd-cli` BINARY is absent too (`where sd`, `where sd-cli`: nothing), and no
  fal-shaped variable is in the environment. Corrected rather than left, because a `needs` that omits half
  the wall sends someone to do the download and hit the other half — the lifecycle rule's "concretely
  enough to test" is about exactly that. The two halves remain independent: the fal half needs a key nobody
  here has, the `sd-cli` half needs a binary plus a model, and neither blocks the other._

  _**The BLOCKING half is gone (2026-08-16, `docs/DECISIONS.md` D69) — what is left is confirmation, not
  repair.** Every mapping that could be wrong is now a host option: fal's status vocabulary and cost fields,
  ComfyUI's four response field names, and `sd-cli`'s whole argv plus an `ExtraArgs` escape. So an adopting
  application that discovers the real wire format fixes it in `appsettings.json` and keeps going — it no
  longer waits on a Lyntai release, which is what made this item block anything. Reframed deliberately: the
  old wording made a third party's availability a precondition for this backlog being clean, and the library
  cannot promise to have called every vendor._

  _**What a real run is still for**, stated so this is not read as closed: a STRUCTURAL difference — a status
  that is not a string field at all, a history document shaped differently — is not fixable by a per-field
  option. The residual risk is a shape, not a spelling._

  _**The binary-directory working dir is CONFIRMED (2026-08-04)** and no longer part of this task — measured by
  a consuming app against a real downloaded release: the engine ships `ggml*.dll` beside the exe, so spawning
  from anywhere else fails at load time on a perfectly good install. Already implemented (the spawn's working
  directory in `LocalDiffusionProvider.GenerateAsync`) and pinned by a test._

  _**The max-dimension question is SETTLED (2026-08-16, `docs/DECISIONS.md` D68) and is no longer part of this
  task.** It asked whether `LocalDiffusionOptions` should carry a max-dimension, whether a CPU build should cap
  itself, or whether an unbounded size is the caller's problem. The answer: the ceiling is DERIVED from a
  declared `Accelerator` — `Cpu` (the default) derives the consumer's measured 768, `Gpu` derives none, and
  `MaxDimension` overrides either. A declaration, never a probe. What remains below is the ARGV, which still
  needs a real render._

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
  this never blocked a release — 3.0 ships the package under the full SemVer promise (**D70**)._

- [ ] **GEN6 — streaming audio (TTS).** A streaming TTS backend against a real vendor. **The scope shrank on <!-- item: state=blocked kind=decision,env needs="a TTS vendor pick, then a key — the wire format must be measured, not inferred" -->
  2026-08-16** (`docs/DECISIONS.md` **D67**): the PLATFORM half is done and shipped in 3.0 —
  `IGenerationRouter.StreamAsync` selects, falls over, governs and throttles a `Stream`-capable backend, and
  the router guarantees exactly one terminal chunk, so a backend no longer has to be careful about fallback or
  closing its own stream. What is left is a real backend and the thing only it can settle: whether
  data-then-terminal is the decomposition a real TTS wire format wants. So this is no longer "the seam is
  unexercised" — the handling is measured by `GenerationRouterStreamTests`; it is "the chunk SHAPE is still
  inferred". **TTS before music** (owner). Needs a vendor pick and a MEASURED wire format (the GEN-VERIFY
  lesson), so it waits on a key rather than shipping another documented-not-measured surface.
- [ ] **GEN7 — pipelines (3d → image → video)**: ordered stages feeding `artifact.ToInput(role)` forward, with <!-- item: state=blocked kind=tree needs="a 3D generation backend — the pipeline's first stage has none, and the 3d-to-image edge needs a rasterizer that does not belong in this library" -->
  per-stage candidates and per-stage failure semantics.
  **Blocker restated 2026-08-11 — the original "deferred until ≥2 real backends exist" now reads as SATISFIED
  and is the wrong test.** Counted by kind rather than by total: **image has 5** backends (`Automatic1111`,
  `ComfyUi`, `FalQueue`, `LocalDiffusion`, `OpenAiImage`), **video has 2** (`ComfyUi`, `FalQueue`) — and
  **3d has ZERO**. `ProviderKinds.Model3d` exists in the contract and no provider declares it. **The
  pipeline's FIRST STAGE has no backend at all**, which is a harder blocker than any count, and the one the
  original wording hid.
  Both video backends also still carry unverified-surface markers — precisely what GEN-VERIFY covers — so
  "real" in the measured sense is not yet true of the output stage either.
  **The survey RAN on 2026-08-30 (`docs/task-archive.md` Part 124) and its answer is that neither option was
  on the menu.** It asked mesh vs turntable stills; the dominant 3D family returns a **mesh and nothing
  renderable** (Hunyuan3D, Rodin), a minority adds a **single preview thumbnail** (Meshy) which is one fixed
  view rather than a turntable, and **turntable output belongs to a different model family altogether**
  (SV3D-class orbital synthesis), which is **image→views** and so does not occupy a 3D stage's place in the
  chain. **So `3d → image → video` corresponds to no buildable chain today, and the chain that IS buildable
  (`image → orbital views → video`) has no 3D stage in it.** The 3d→image edge is not a generation at all —
  it is a RASTERIZATION, which no vendor on this platform performs.
  <br>**What that unblocked was the runner at `image → video`, and it SHIPPED on 2026-08-30** as GEN7a
  (`docs/task-archive.md` Part 126): `router.RunPipelineAsync(stages)`, ordered stages chaining through
  `GenerationArtifact.ToInput(role)`. A stage is a stage, so **adding a 3D stage later needs no change to the
  runner** — which is what made building it an unblocking rather than a narrowing. What is left of GEN7 is
  therefore the 3D STAGE alone. A rasterizer is the only thing that makes a
  mesh chain and it belongs to an application with a renderer, never inside a library whose core promise is a
  small dependency footprint (`dotnet-package-layout.md` §Package boundaries).
  <br>**Two defects on the OUTPUT stage were found while establishing that, and both are FIXED**
  (`docs/task-archive.md` Part 125) — `ComfyUiProvider` declaring `SupportsInputs = true` while never
  reading `request.Inputs`, and `ProviderKinds.Model3d`'s shipped XML doc claiming a chain that does not
  exist. So they no longer gate the runner below.
  <br>_Desk survey: read from published API pages, never called. That is the tier GEN-VERIFY exists to
  distrust, so the SHAPES transfer and no individual field name is confirmed — nothing in it licenses
  deleting an unverified marker._

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
`docs/DECISIONS.md` **D35**). CLI11 shipped the honest subset: the message/usage/terminal half of the codex
mapping is measured, the tool-step half is inferred, and the inference is written shape-driven — which bounds
what a wrong guess can cost to exactly two things: **no payload is invented or dropped**, and **every
uncertainty stays inside the tool-step half**. It does NOT bound the KIND of event, so a tool step's kind is
provisional and only its payload is reliable (the item below is the consequence). What is left is
measurement, and measurement only — nothing here is codeable without a real codex run._

- [ ] **CLI12 — measure codex's tool-step items and confirm (or correct) the inferred mapping.** <!-- item: state=blocked kind=env needs="a codex-cli reinstall on this machine, then one real turn that runs tools" -->
  `src/Lyntai.Providers.Basic/CodexCli/CodexAgentReader.cs`. The capture behind this backend (codex-cli 0.146.0,
  2026-08-04) ran a trivial `--oss` turn with **no tools**, so the entire tool-step half is inferred and
  marked as such in the XML docs.

  **Two blockers, and the second was only found on 2026-08-11 when the owner authorized the first:** it
  spends tokens (the owner's call, and they said yes), **and the codex CLI is not installed on this machine
  at all** — not on PATH, not in the npm global root, not in any usual location. The 2026-08-04 capture came
  from an install that is gone, so this needs a REINSTALL plus a turn, not just a go-ahead.
  <br>**RE-CHECKED 2026-09-15 and still absent** — `where codex` finds nothing. Recorded because the
  neighbouring `env` item on this sweep had gone stale the other way: an environment blocker expires
  silently when the machine grows, so a negative re-check is worth a date rather than a repeat.

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
  `CodexAgentReader`'s docblock, the README's codex bullet and `DECISIONS.md` D35 — and extend
  `devtools/scripts/codex-stub.mjs` with the real shapes (its header forbids inventing them, which is why the
  inferred cases are covered only by `FakeProcessRunner` fixtures today). A friendlier `ToolResult.Content`
  projection (the readable output field instead of the raw item JSON) becomes possible at the same time, and
  is additive.

_**CLI15** (a measured `turn.failed` shape, filed by `Aurelia` 2026-08-05) closed the same day — see
`docs/task-archive.md` **Part 45**. Three of its four claims were already handled and are now pinned; the
fourth found a real defect in `CliProviderEngine.CompleteAsync` (a non-zero exit masked the backend's own
in-band failure), fixed and recorded in `docs/FIXES.md`._

## Part 56 — complete FSRS: `DsrRetrievability` is a PARTIAL, UNFITTED model, and that gap is measured (2026-08-10)

_Opened by `docs/DECISIONS.md` D49, which shipped `DsrRetrievability` as the 3.0 default on FSRS's own
external validation while disclosing a real, measured gap: this implementation carries FSRS's functional
FORM with none of its calibration. **Completing it is prioritized work — the `topical` regression D49 ships
knowingly is where the gap shows up measurably, not a reason to avoid shipping the default.**_

- [ ] **FSRS-B — parameter FITTING, not published defaults.** Every constant in `DsrOptions` (`Decay = <!-- item: state=blocked kind=data needs="a deployment's own logged reviews; this repository cannot invent them without repeating the mistake D49 refused" -->
  -0.5`, `StabilizationDecay = 0.4`, `SpacingWeight = 1.5`, `DifficultyWeight = 0.08`) is FSRS's own published
  default, fitted by its authors against a huge external review corpus — never fitted against anything this
  library's consumers actually do. Real FSRS fits on the order of 17 parameters per individual's own review
  history.

  **Its blocker was recorded wrongly and is now measured (2026-08-12).** It read "needs a real (or realistic)
  review corpus", which says a corpus is the missing input and implies more/better data would unblock it.
  **No corpus can.** Two structural facts, both read straight out of the shipped code:
  1. **The observed "grade" is a deterministic function of the model's own prediction.**
     `DsrRetrievability.DerivedGrade` is `2 + 2 × Retrievability(state)`, and `Retrievability` is computed
     from the very constants a fit would estimate. Maximising the likelihood of those grades recovers
     whatever produced the log — circular by construction, not merely by choice of corpus.
  2. **The log can only ever contain successes.** The grade scale is restricted to Hard=2..Easy=4 and
     deliberately never reaches FSRS's lapse rating, because — in that member's own words — *an entry that is
     not returned never reaches `Reinforce`*. `GraphMemoryEngine.ReinforceAsync` is called with the nodes a
     recall actually RETURNED. FSRS fits against recall success **and failure**; this library observes only
     the successes, so even breaking (1) would leave the likelihood with nothing to discriminate against.

  **What would actually unblock it**, stated so nobody re-derives it: an outcome signal the model does not
  produce — a consumer-supplied rating, *and* some observation of the entries a consumer expected and did not
  get. The migration guide already lists consumer rating input as deliberately out of scope. Both are
  **additive** API, so neither is gated on the 3.0 window; this is a design question about what the library
  is willing to ask an application for, not a measurement waiting on data.

  _**AMENDED 2026-08-13 — the observable now EXISTS, and it did not come from asking the application for
  it.** `IMemoryVerificationPolicy` (`docs/DECISIONS.md` **D59**) has a judge read the query and the returned
  headlines and say which actually answered. That is external to the curve (defeating blocker 1) and it can
  return a negative (defeating blocker 2), which is precisely the pair recorded above as needing a consumer
  rating. The design question "what is the library willing to ask an application for" turned out to have a
  third answer neither branch anticipated: ask a MODEL, not the application._
  <br>_**The recording landed the same day**: `MemoryReviewWrite.Verified` (nullable — `false` is an observed
  failure, `null` is no judgement, and they are not interchangeable), one column on the unreleased memory
  migration, and the log write decoupled from the touch so a recall logs EVERY entry it returned rather than
  only the ones it reinforced. **So the log can now contain failures**, which was the harder half of D51.
  <br>**What remains is genuinely the fitting itself** — reading the log and estimating `DsrOptions` against
  it. That needs a deployment with real logged reviews, which this repository does not have and cannot
  invent without repeating the mistake D49 refused (tuning against a corpus this library made up). It is no
  longer blocked on a design decision or on a missing observable; it is blocked on data that only a
  consumer can produce._

  **What it is blocked on, stated once so the two paragraphs above are not read as disagreeing:** not the
  environment (nothing here needs a vendor key or a download), and no longer a design decision or a missing
  observable — those were both closed on 2026-08-13. It is blocked on **a deployment's own logged reviews**,
  which only a consumer can produce.

---

## Part 75 — what the pre-3.0 review deferred, and why (2026-08-15)

_Opened by `docs/task-archive.md` **Part 74**. Each of these was found, verified and deliberately NOT fixed
in that pass — and every one was startable, which is why the banner stopped claiming otherwise while they
were open. All have now closed (archive Parts 76, 78–81 and 84) except the one below, whose blocker is not
a design question: it needs two or three real aggregators to measure against._

- [ ] **Decide what an aggregator's in-band `code` means.** `HttpBody.InBandError` deliberately reports <!-- item: state=blocked kind=env,data needs="two or three real aggregators to measure an in-band code against" -->
  only THAT an `error` member is present and what it says; it does not read a numeric `code` as an HTTP
  status, because that mapping is not measured across the gateways this provider serves. A 200 carrying
  `{"error":{"code":429}}` therefore classifies from the message text alone. Measuring two or three real
  aggregators would let the code lead, which is strictly better than text matching — but reading it
  unmeasured is the documented-not-measured trap GEN-VERIFY exists to correct.

## Part 99 — what the memory pass did NOT close (2026-08-26)

_Opened by `docs/task-archive.md` **Part 97**, which closed every other Phase-1 invariant of the memory
proposal. Named here rather than left implied, because a scorecard that reads as complete is how a gap stops
being looked for._

_It opened holding the two Phase-1 gaps. One of those (cross-tenant isolation) closed the same day as
**Part 100** and left a DECISION behind it; the flake below arrived from watching `verify` rather than from
the proposal. **One item is left open here** — the rest closed into the archive, and this line said "all
three are startable" until 2026-08-28, after two of them had gone._

_**It is a WATCH item, not startable work, and the banner counted it as startable until 2026-08-28.** The
suspected cause is fixed AND pinned — `ProcessRunnerTests.A_FAILED_path_lookup_is_not_cached_so_one_transient_locator_failure_is_not_permanent`,
which carries a positive control so it cannot pass on an implementation that simply caches nothing. So
there is nothing here to code: what remains is evidence only recurrence can supply._

- [ ] **`verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted mid-run.** Observed <!-- item: state=watch kind=data needs="the same nine tests to recur — the fix is unconfirmed as the cure, and a green run is not evidence" -->
  twice in roughly ten `verify` runs on 2026-08-26, and **not once in any standalone `node devtools/dev.mjs
  test`**, which passed every time including immediately before and after a failing `verify`. A third run
  ABORTED at 2088/2108 — a crash rather than a failure — and took 6m20s against the usual ~3m.
  <br>**CAPTURED on the third occurrence, and the names refute the obvious hypothesis.** The guess was
  Postgres — 9 is a plausible size for one Testcontainers fixture class, and it is the only part of the
  suite with an external dependency. **Not one of the nine is a Postgres test:**

  ```
  CortexIntegrationTests.Evaluate_persists_results_to_the_score_store
  CortexIntegrationTests.Llm_judge_scorer_returns_the_stub_verdict
  RouterEndToEndTests.Healthy_primary_cli_serves_and_http_is_never_called
  RouterEndToEndTests.Streaming_never_falls_back_after_the_first_token
  RouterEndToEndTests.Dead_host_cooldown_skips_then_retries_after_expiry
  AddClaudeCliTests.Registered_provider_serves_through_the_router_by_id
  ClaudeCliProviderTests.Explicit_command_makes_the_provider_available
  CodexCliProviderTests.A_portable_install_is_wired_without_touching_the_process_environment
  ProcessRunnerTests.Resolve_command_path_finds_node_and_caches
  ```

  **Every one of them spawns a process or resolves a command on PATH**, and the whole set is explained by
  the last one failing: the CLI-provider and router-e2e tests all reach the deterministic provider-stub
  through `LYNTAI_PROVIDER_CMD`, which is `node`. If `ProcessRunner` cannot resolve `node`, all nine fall
  together — one cause, nine symptoms, and a constant count is exactly what that predicts.
  <br>**Why only under `verify`** is then the question worth asking, and the shape of an answer is already
  <br>**RECURRED 2026-09-15, and ALONE — which narrows the hypothesis rather than confirming it.** One of
  the nine failed by itself under `verify`
  (`CodexCliProviderTests.A_portable_install_is_wired_without_touching_the_process_environment`,
  `Assert.True` on `IsAvailable`), passed 3/3 standalone immediately after, and the very next `verify` was
  green with the same tree. So the "one cause, nine symptoms, constant count" reading is too strong: a
  resolution can fail for ONE caller without taking the other eight, which an all-or-nothing PATH outage
  would not do. A per-entry cache race fits; a global `node`-not-on-PATH window does not.
  <br>**The count is therefore NOT the signature** — nine was one observation of it, not its shape, and a
  future single-test failure in this list is the same bug rather than a new one.
  in `.claude/rules/windows-machine.md`: `verify` runs `test-devtools`, `build` and nine gates before the
  test step, `check-samples` spawning Roslyn over ~78 samples, so the test step starts after heavy process
  churn. Look at `ProcessRunner.ResolveLauncher`'s CACHE first — the failing test is named
  `..._finds_node_and_caches`, and a cache that can memoize a transient failure would produce precisely
  this: intermittent, all-or-nothing, and invisible to a standalone run that starts clean.
  <br>**Do not close this by observing a green run.** It was green 11 times out of 14, including twice
  consecutively while trying to reproduce it on purpose.
  <br>**A REAL BUG matching every symptom was found and fixed the same day** (`docs/FIXES.md`,
  `pitfalls.md`): `ProcessRunner.ResolveCommandPath` cached FAILED lookups into a process-wide static, so
  one transient `where.exe` failure made `node` unresolvable for the rest of the run — which fails exactly
  these nine at once and explains the constant count, the all-or-nothing shape, and why a standalone run
  that starts clean never saw it.
  <br>**This item stays OPEN, on purpose.** The mechanism fits every observation and the flake was never
  reproduced on demand, so the fix is unconfirmed as the CURE. **If the nine recur, this was not it** — and
  that is the only evidence that can close this. Watch for it; do not close it by observing green runs,
  which is what the line above already says and is now doubly true.

  <br>**A ONE-test flake was seen 2026-09-09** under `verify`, green on the standalone run immediately after
  and green on the next `verify`. It is recorded here rather than as a new item because it is the same
  SHAPE — intermittent, only under `verify`, unreproducible on demand — but it is NOT the nine, so it does
  not confirm or refute the `ProcessRunner` fix this item watches. The name was not captured; capture it if
  it recurs, which is the only thing that would make it actionable.

- [ ] **`SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another caller is still using.** <!-- item: state=watch kind=data needs="a recurrence with a full stack — the three hypotheses a reading can reach are refuted, so the next move needs the frame that raised it" -->
  **Captured 2026-09-14 — a name, an exception, and a reproduction, which is what the note above asked for
  and did not get.** `System.ObjectDisposedException: Cannot access a disposed object. Object name:
  'SQLitePCL.sqlite3'`, thrown inside `SqliteConnectionFactory.OpenAsync`
  (`src/Lyntai.Storage.Sqlite/SqliteConnectionFactory.cs`) while the test exercises concurrent dedup.
  <br>**It is NOT the flake above, and the difference is the whole point:** that one is *only* under
  `verify` and has never reproduced standalone. This one failed **1 of 3 standalone runs** of its own
  class — so it is not `verify`-specific, not process-churn, and not the nine. Nothing here touches
  `ProcessRunner`.
  <br>**Startable, and the reproduction is the cheap part** — loop the single class until it fails. What
  makes it worth doing rather than muting: the exception says a connection was DISPOSED while in use, and
  the same factory serves every SQLite domain. A test that fails a third of the time is also a gate that
  passes two thirds of the time for the wrong reason.
  <br>**2026-09-15: did not reproduce in 8 consecutive runs, and the "shipped storage code" reading is
  REFUTED — three hypotheses, each checked by reading rather than by re-running.** (1) A double dispose in
  `SqliteCuratedMemoryStore.AddAsync`: no — connection and transaction are both `await using`, disposed in
  the right order, and the factory builds a fresh `SqliteConnection` per call and disposes it on failure.
  (2) `SqliteConnection.ClearAllPools()` evicting a concurrent test's handle: already fixed —
  `TempDbPath.Dispose` clears only its OWN db's pool and `TempDb` delegates to it. The trap is now in
  `pitfalls.md`, where it was not. (3) Work outliving the test method: no — every task in
  `Dedup_add_race_settles_to_a_stable_id` is awaited through `Task.WhenAll`.
  <br>**So it is `watch · data` rather than startable**: what it needs is a recurrence carrying the frame
  BELOW `OpenAsync`, because the three causes a reading can reach are gone and the remaining ones are all
  in the runner's resource behaviour — the same shape as Part 99 above, by a different mechanism.

## Part 109 — LoCoMo says the shipped ranking defaults lose to plain cosine on a uniform-history workload (2026-08-29)

_Opened by `docs/task-archive.md` Part 110; the first item CLOSED as **D97** (`docs/task-archive.md`
Part 111) and the LongMemEval half closed as **Part 112**. `node devtools/dev.mjs memory-locomo --retrieval`
is the first measurement this repository has taken on an instrument it did not build: evidence-hit@20,
model-free, 200 LoCoMo questions. Defaults went **11.0% → 31.0%** on D97 and plain cosine is **80.5%** at the
same k, so a real gap remains. Tables and the two harness defects that had to be fixed first are
`docs/memory-measurements.md` §5.
<br>**A THIRD harness defect landed 2026-08-29 (`docs/task-archive.md` Part 118) and moved every figure in
this paragraph**: questions shared a store, and isolating them puts defaults at **54.5%**, not 31.0%. Cosine
is unchanged at 80.5% — it never touches the graph store — so the gap is **−26.0** rather than −49.5._

_**The ranking half of this Part is CLOSED — `docs/task-archive.md` Part 113.** The ladder it asked for ran
in the same commit that reframed the item, and refuted both misallocation hypotheses plus the pre-committed
seeding fallback: `HopWeight = 0` costs 23.0 points on the isolated re-run and 24.5 before it (either way,
traversal carries the arm), more semantic seeds make it worse, and the pool provably contains the evidence.
The residual gap is the design, not a defect._

_**The QA half RAN — `docs/task-archive.md` Part 115.** LoCoMo, 100 questions, token-F1 primary with the LLM
judge beside it, plus a `--shots` diagnostic for the multi-shot mode the one-shot tables could not see. It
found **D98** and four harness defects. What is left of the item is below: more of it, not the first of
it._

_**The QA-widening item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 200**. Both halves ran: a
second EMBEDDER moved no arm (Part 199), and a second and third READER — `gemma-3-1b-it` 806,058,240 B and
`qwen2.5-0.5b-instruct` 491,400,032 B, both already on disk — were measured on the same four arms and the
same seeded questions. **The arm ORDERING broadly holds and the SPREAD collapses**: `lyntai-fused` is last
under all three readers and `vector` first or second, while the spread across arms falls 14.7 → 8.8 → 4.7
points down the size ladder. So a smaller reader registers the memory layer less, which bounds what memory
work can be worth to a small consumer. `docs/memory-measurements.md` §5 owns the figures._

_**The centering item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 201**, by REFUTATION — the
outcome the item itself named as the more useful one. The direction that looked consistent on the hard
fixture (**+8 / +6 / +3** for the static model) becomes **−1 / 0 / −2** on the easy one, where that arm had
room to move, so it was a property of the FIXTURE rather than of the model class. **Do not re-derive it.**
`docs/memory-measurements.md` §5 (`affordance-centering-refuted`) owns the figures and the one half that
stays untested — the claimed harm to a transformer sits under a 94-97% ceiling there._

_**The completeness item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 202**, and it closed by
refuting the blocker it was filed with. The class is scored by a turn TAG, not by the fact's text, so the
confound named here could not reach it — and an unbudgeted run is therefore VACUOUS rather than confounded.
The measurable arrangement is a CHARACTER CAP, where whole items become fewer items: **−11.4** points of
`clean` at 1,200, **+8.6** at 5,400, **exactly 0.0** at 20,000 where the cap binds on neither arm and the
`full` arm reproduces `shot-1` to the decimal. `docs/memory-measurements.md` §5
(`longmemeval-ku-completeness-budget`) owns the figures; `docs/deployment-shapes.md` carries the caveat.
**Still unmeasured and named rather than implied**: completeness for a READER on this class._

---

## Part 116 — the n-shot WALK: what D100 opens, and the surface it does not have yet (2026-08-29)

_**Start here.** `docs/DECISIONS.md` **D100** changed what this engine is evaluated as: a walk, not a single
top-k. The instruments exist — `node devtools/dev.mjs memory-locomo --shots` and
`memory-longmemeval --shots [--haystack] [--expand-floor w]` — and the tables are `docs/memory-measurements.md` §5.
Closed alongside it: **D98** (expansion had no vote from forgetting) and **D99** (co-activation is one store
call). The session that produced all three is `docs/task-archive.md` Parts 112–115.
<br>**This Part held five items and now holds ONE** — the shot curves for LongMemEval's four unmeasured
classes, whose hard half is deciding what a shot BUYS on a single-session question rather than running it.
The expansion-floor sweep closed 2026-08-30 as `docs/task-archive.md` **Part 123**. The write-back
collapse closed as `docs/task-archive.md` **Part 117** (**D101** — the whole write-back is one store call,
not just the co-activation half D99 did), the LoCoMo contamination as **Part 118**, two thirds of the
shot-curve item as **Part 119**, **the n-shot SURFACE itself as Part 120** (**D102** —
`MemoryWalk.WalkAsync`, an extension over the two seams that already existed, with both harnesses moved onto
it and every published table reproduced cell for cell), and **its naming pass as Part 121** — filed blocked
on the TREE and unblocked by Part 120's own commit, which is the kind of blocker a commit discharges.
<br>**Read this before trusting any number below.** Those two measurement passes moved published figures
three times: every LoCoMo arm by 20–25 points, D100's *"search wants two shots"* withdrawn outright, and
knowledge-update's level down 6–9 points off a small sample. **None was a library defect — all three were
the instrument.** So treat the remaining measurement items as RE-measurements: they were scoped against
figures that have since moved._

_**The last three classes RAN 2026-09-11** and this item CLOSED as `docs/task-archive.md` **Part 188**.
All six LongMemEval classes now have a shot curve. **Shot 3 is worth exactly zero on all three**, so
*expand once* holds a sixth time; `single-session-user` is FLAT outright (82.8% at every shot while the walk
returns 7× the characters), which is a sharper negative than a diminishing return. The class this item
predicted unmeasurable — `single-session-assistant` — moved cleanly on the haystack (85.7 → 89.3), so the
prediction was right about the ORACLE and the haystack is what fixed it.
<br>**The finding worth carrying forward is not the curve.** Plain cosine wins all three, and
`single-session-preference` is the widest gap this record holds: **30.0% against 73.3%** at the same k.
Preference questions are where this engine is furthest behind a flat retriever, and no judge or reranker
was in the loop for any cell — the seam measured as worth more than any ranking constant is absent from the
whole table. `docs/memory-measurements.md` §5._

---

## Part 128 — the retrieval gap is RANKING OUT candidates the engine already holds (2026-08-31)

_Opened by three LoCoMo ladders run at the owner's direction after "the memory system performance is not
good enough". Tables and the full reading are `docs/memory-measurements.md` §5; this Part carries only what is still to
do. **Two hypotheses died in those runs and are recorded so nobody re-runs them**: the edges are not the
problem (**D59** decomposed it — 100% of misses reachable-but-outranked, 0% unreachable), and preserving the
cosine MAGNITUDE is not the fix (`MultiplicativeRankingPolicy` nets +1.5 overall, and `+sem80+mult`
collapsed to 21.5% at the time)._

_**`+sem80+mult` is now 57.0%, not 21.5%** — a direct side effect of per-source fusion: `SemanticSeedSource`
now sets `Matched = true` carrying an honest cosine, and `MultiplicativeRankingPolicy` reads
`Matched is null ? 1 : Relevance`, so a semantic seed stopped acting as an implicit neutral multiplier
(`docs/memory-measurements.md` §5, `docs/task-archive.md` **Part 131**). **The conclusion above is UNCHANGED, only its
supporting figure moved**: 57.0% is still far below `+sem+rel-only`'s 83.0%, so magnitude preservation is
still not the fix._

_**The uncomfortable summary, stated once so it is not softened later:** plain cosine scores **80.5%**, this
engine's best mechanical arm **61.5%**, and the engine WITH A PERFECT JUDGE **77.5%**. On a uniform-history
search workload the graph is not paying for itself, and no arm measured so far makes it._

_**61.5% above is superseded, not retracted — it is quoted as written on 2026-08-31.** Per-source fusion
later moved the best mechanical arm to **83.0%** (`+sem+rel-only`; see the closure note below and
`docs/memory-measurements.md` §5). The rest of that paragraph's claim — cosine beating the engine even WITH a perfect
judge — is untouched by that later change: neither `vector` (80.5%) nor `+forget0+oracle` (77.5%) uses
semantic seeds._

_**The mixed-scale hypothesis was TESTED on 2026-08-31 and CONFIRMED, on a prediction registered in the
source before the run.** `+sem+rel-only` — semantic seeds present, every other vote off, so relevance alone
orders a pool provably holding cosine's entire top-20 — scored **63.5%**, not the ~80% that would have meant
the weights were diluting a good ordering. **Weight-tuning is therefore retired as a direction.** The
mechanism is in the harness's own control output: lexical hits carry a rank POSITION (0.963, 0.900) and
semantic seeds a COSINE (0.742, 0.732), compared on one field, so a semantic candidate is outranked by
construction however similar it is — which is also why ADDING semantic seeds makes the arm worse.
`docs/memory-measurements.md` §5 carries the table._

_**This item CLOSED 2026-08-31 as `docs/task-archive.md` Part 131.** Per-source fusion —
`IMemorySeedSource` plus `ReciprocalRankFusionPolicy` fusing each source's own ranked list instead of one
pooled `Relevance` field (`docs/DECISIONS.md` **D103**) — is what the 63.5% two paragraphs up now
predates: the SAME arm, same name, reads **83.0%** under the fused engine, above plain cosine's 80.5%. That
makes `+sem+rel-only`, not `+sem+fuse`, the current best mechanical arm. `docs/memory-measurements.md` §5 carries the
current table; the 61.5%/63.5% figures above are the PRE-fusion measurement that opened this Part and stay
for that reason._

_**The real-judge item CLOSED 2026-09-03** as `docs/task-archive.md` **Part 143**, and the answer is the
branch nobody wanted: `+sem+rel-only+judge` reads **72.5%** against the arm's unjudged 83.0% and the oracle's
92.5%, so a 4B judge SPENDS 10.5 points where a perfect one gains 9.5. The audit says why — 29.1
endorsements per recall out of 80 shown, at 2.6% precision, which is an endorsement set larger than the
20-slot page, so promotion replaces the ranking instead of refining it. **The seam has a capability FLOOR**,
now stated in `LlmVerificationOptions.ClientName`'s shipped XML doc. `docs/memory-measurements.md` §5 carries the table
and the four things it does not say._

- ~~**Decide what TEXT a verifier may read — today it is a 120-character truncation, and that costs a
  cross-encoder 13 points.**~~ **DECIDED AND SHIPPED 2026-09-08** (`docs/task-archive.md` **Part 170**,
  **D108**): the candidate carries `Content`. Additive, engine-supplied, no extra query — `SeedAsync`
  already read the column. Validated rather than argued: the reranked arm goes **78.0% → 91.0%** at the
  SHIPPED `HeadlineChars`, landing exactly on the arm that bought the same text with +24% of storage.
  The shipped judge still reads the headline, so nothing changes for an existing consumer. _Kept here with
  its three options rather than deleted, because the two that lost are the reasons the winner is right._ `MemoryVerificationCandidate` carries `Headline` and never `Content`, and
  `GraphMemoryOptions.HeadlineChars` ships at 120. Measured 2026-09-08 (`docs/task-archive.md` **Part
  168**): the same reranker on the same arm reads **78.0%** on truncated headlines and **91.0%** on whole
  turns. The headline-only contract is not an oversight — it keeps an LLM judge cheap, and it is what a
  caller would see without paying to expand — so this is a real trade rather than a defect.
  <br>**Three options, each a different promise, and the storage one is now PRICED.** Leave it and document
  that a reranking deployment raises `HeadlineChars`: it works today, but 120 → 512 costs **+24% of the
  corpus's content bytes** — measured on both field corpora independently (LoCoMo +24.2%, LongMemEval
  +23.8%), taking the headline column from 11.5% of content to 35% — and it changes what every recall
  returns to callers, not just what the verifier sees. Add `Content` to the candidate record, which is
  additive, costs no storage and leaves recall untouched — but hands an LLM judge a far bigger prompt
  unless it is opt-in, which is the cost that made the seam headline-only in the first place. Or an option
  selecting which text the seam passes, the explicit form of the same choice.
  <br>_The knowledge-update run (`docs/task-archive.md` **Part 169**) says what the reranker does with the
  fuller text once it has it: finds more (`current@k` +8.5) and discriminates no better between a fact and
  its replacement (`stale@k` +51.4). So this decision buys RECALL, and whoever takes it should want that._
  <br>_Not startable as a code change until that is settled — the fix is a decision, not an edit._

_**The cancellation thread is CLOSED** — `docs/task-archive.md` **Parts 173–174**, `docs/FIXES.md`. Every
fail-open handler in `Lyntai.Core/Memory` now distinguishes the caller's cancel from a component's own
timeout, and four seam contracts that stated the false premise were corrected with the code. **One shape
survives OUTSIDE memory and is deliberately not swept**: `JobRunner`'s heartbeat loop
(`catch (OperationCanceledException) { return; }` over `_store.HeartbeatSlotsAsync`), where per **D73** a
lost heartbeat is a lost cross-process job slot. Different subsystem, different promise, its own answer._

_**The seam-`Model` item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 205** / **D119**: the
router's precedence STAYS (a candidate is a provider-and-model pair), and the provably-inert case now
throws at composition instead of running silently on another model. **Narrow on purpose** — every candidate
pinned AND none matching — so a partly-pinned list still composes and a deployment that set only one of the
two values is untouched._

_**The fusion item CLOSED 2026-09-04** as `docs/task-archive.md` **Part 151** / **D105**:
`GraphMemoryOptions.VerdictCombination` ships the choice with `Partition` — today's behaviour — as the
default, so nothing moves for anyone who does not set it. The surface question it was blocked on was decided
additively: changing the default would be a silent reordering no consumer can detect at compile time (D18's
shape), bought on one model and one workload. **The reader-facing check it did NOT get has since run** — the
note below._

_**The reader-facing item CLOSED 2026-09-11** as `docs/task-archive.md` **Part 191**, and it closed by
REFUTING its own prediction. It expected the option not to move the score; both paired bounds exclude zero —
the shipped **partition** costs a reader real token-F1 and `+enginefuse` gives most of it back
(`docs/memory-measurements.md` §5, which owns every figure). **The default is not re-opened**: one
workload, one reader, and **D105** decided it on a different metric. What must not be carried forward is
"fusing costs nothing a reader notices", which is the sentence the run killed._

_**The frontier item CLOSED 2026-09-11** as `docs/task-archive.md` **Part 187**, and the answer is the
second branch it offered: **there is no knee, so it is not worth walking**. A six-point ladder
(`docs/memory-measurements.md` §5) lands every rung within ±1.0 point of a straight line — measured slope
**−6.8** per unit of weight against a predicted −7.0 — with all three published anchors reproducing cell for
cell. So the exchange rate is CONSTANT: every weight buys suppression at the same price and none is a
bargain, which is what a knee would have been. `RetrievabilityWeight` stays at 1.
<br>**The suppression half was deliberately NOT spent**, per a decision rule fixed before the run: a
straight search axis settles the question without it. So the four new rungs have no knowledge-update figure
and none is implied — that is a live gap if anyone reopens this, not an oversight._

_**The ingestion-cost item CLOSED 2026-09-03** (`docs/task-archive.md` **Part 141**): the retrieval path
honours `--arms` when dropping configs, so a three-arm ladder ingests 2 rather than 13 and n = 200 runs in
755s rather than 4,706s, on byte-identical cells. Its follow-on was a defect the fix exposed rather than
caused: the ladder's arm names lived in THREE lists, adding an arm to two of them failed two runs ten
minutes apart, and the report list and the ladder are now asserted equal before a run starts._

_**The multi-hop item CLOSED 2026-09-02** as `docs/task-archive.md` **Part 139**, and it closed by REFUTING
its own premise twice over. It read *"64.9% even with a PERFECT judge against cosine's 81.1%, so multi-hop
evidence sits outside `VerificationDepth` — a depth or seeding question"*. That 64.9% is a PRE-FUSION arm
which **D103** had already superseded the same day, and at full sample the best mechanical arm reads
**79.8% against cosine's 83.0%** — a 3.2-point category deficit, not a 16-point one, and no depth question
follows from it. Nobody should sweep `VerificationDepth` on the struck premise._

_~~**Not startable and deliberately not listed above:** whether `RetrievabilityWeight` should move off 1…
it needs both workloads measured, which is the `+forget0` arm re-run on LongMemEval.~~_
<br>_**MEASURED AND SETTLED 2026-09-02** (`docs/task-archive.md` **Part 140**, `docs/memory-measurements.md` §5). The
`+forget0` arm ran on LongMemEval knowledge-update and the answer is emphatic: **49.3% against the shipped
default's 86.4%**, a −37.1 collapse, against the +5.5 it is worth on LoCoMo — **about 7 to 1 against
moving it.** `RetrievabilityWeight` stays at 1, and this is no longer an open question.
<br>The columns say WHY, which is the durable half: `+forget0`'s `current@k` is **identical** to the
default's (87.1%) while its `stale@k` rises 62.9 → 87.1. Removing forgetting's vote does not change what
the engine FINDS, it destroys what it BURIES — so LoCoMo, which only scores finding, cannot see the cost.
**Any future arm that wins on LoCoMo owes this table a visit before it is proposed as a default.**_

---

## Part 178 — a DECISION system on a small model: the shape is already constrained, and the region that matters is unmeasured (2026-09-12)

_Opened at the owner's direction, who names this the next goal and the memory work its first instance.
**`docs/model-tasks.md` §1–§3 is the brief** — read it before anything here, because four findings already
constrain the design and one is a warning._

_**What is already settled, so nobody re-derives it.** A decision is `select-from-list` or `affordance` in
§1's taxonomy. **List length governs the selective shape** — 20 shown gives 16.2% precision, 40 gives 8.4%
(level with using no judge), 80 gives 2.6% (below it). A stated budget does not bind a selective task and
made one model endorse MORE; **D110** refused a cap over endorsements for that reason, because the lever is
calibration or the combination rule, never a count._

_**The first two items CLOSED 2026-09-12** as `docs/task-archive.md` **Parts 192 and 193**, and between
them they replace the reading this Part opened with. The measurement is
`node devtools/dev.mjs memory-decision`; every figure is `docs/memory-measurements.md` §5._

_**This Part opened saying the evidence points at "a SCORER over a bounded candidate list, never a <!-- drift-ok: quotes the rule this Part's own measurement retired -->
generator asked to choose". Measured at 3-7 options, that is HALF true and the half it gets wrong is the
large model.** Among the GENERATIVE arms the winning shape **inverts with model size**: at 2,489,757,856 B
one `select-from-list` call beats N `score-a-pair` calls at every length (`p<0.0001`), and at 806,058,240 B
it loses — because the small model stops choosing and emits a CONSTANT, answering slot 1 on 100% of the
trials where gold sat there. **Pick the shape from the size, never in advance.**_

_**But the arm to actually reach for is neither, and it is the smallest model in the grid**: a
**468,393,760 B** cross-encoder doing the scorer shape in ONE round trip matches the 5.3x larger instruct
model at three options and pulls AHEAD as the list grows. It is the only arm flat in N._

_**And a decision IS expressible through what ships** — `IMemoryVerificationPolicy` takes
`(query, bounded candidate list)` and already separates *the seam did not answer* from *none of these*,
while `ScoringVerificationPolicy` with `EndorseCount = 1` IS the argmax. `docs/model-tasks.md` §6
carries the comparison against the other four seams, so nobody re-walks them._

_**The per-option-score item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 204** / **D118**:
`MemoryVerification.Scores` carries the score for EVERY candidate a policy scored, not the endorsed subset,
because the rejected scores are the half a margin needs. An init-only property, since widening the record's
primary constructor is a BINARY break — which the item's own "additive" framing got wrong. The vocabulary
question it raised is deliberately still open: a decision is not memory, but minting a parallel namespace
for one property is the worse trade._

_**The tool-roster item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 206** / **D120**, in the
order the ruling set: MEASURE, then decide. The ladder ran to 35 options
(`affordance-roster-catalogue`) and a model-free embedder still reads **81.5%** against 3% chance, so
`IToolSelector` + `EmbeddingToolSelector` shipped. **The figure bounds the seam from BELOW twice over** —
argmax where a selector is scored on recall@k, and the `easy` fixture, whose distractors past the first
handful are semantically distant. The `hard` fixture cannot pose the question at all: its distractors come
from the gold tool's own family, which holds seven._

_**The native-transport item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 197**. The survey did not
come back empty — Qwen2.5, Qwen3 and Llama-3.2 all carry a tool section in their own
`tokenizer.chat_template` — and the paired measurement says the two transports TIE on accuracy while
failing in opposite directions, with convergence (11-24% prompt against 99.4-100% native) the largest
effect in the grid. It also refuted `docs/model-tasks.md` §3.1's headline: a 491,400,032 B model reads six
times a 806,058,240 B one on the same arm, so that result was the MODEL's rather than the size class's.
`docs/memory-measurements.md` §5 owns the figures._

_**The false-call item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 198**, and the hypothesis held:
native function-calling invokes a tool on **20-30%** of requests nothing serves against the prompt
protocol's **90-100%**, while still firing on 70-78% where a tool does fit — about 50 points of separation
where the prompt protocol has none. So the TRANSPORT is a second lever on §2's hardest case, and
`docs/model-tasks.md` §2 and §3.1 now say so. It also corrected Part 197's own "accuracy is a wash"
headline: the cost is **2.4-9.6 points** at N = 3..6. `docs/memory-measurements.md` §5 owns the figures._

_**The fallback-visibility item CLOSED 2026-09-13** as `docs/task-archive.md` **Part 203** / **D117**:
`ToolLoopResult.Transport` reports which transport ran, as a nullable init-only property. A result property
rather than a warning, because reporting which transport ran is a FACT about the run and needs no evidence,
where a warning would have shipped a roster-size threshold taken from one model on a synthetic corpus.
Nullable because `None` (no tools registered) and "a BYO loop never said" are different claims._

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