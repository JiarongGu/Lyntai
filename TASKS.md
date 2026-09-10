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

## Open items — 16 across 11 Parts: 7 startable, 7 blocked, 1 watch, 1 decision-only

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 92 | 33 | GEN-VERIFY — confirm the remaining unmeasured surfaces against reality | blocked · env | a real fal.ai key, and a ~1.7 GB model download for one sd-cli render |
| 136 | 33 | GEN6 — streaming audio (TTS) | blocked · decision+env | a TTS vendor pick, then a key — the wire format must be measured, not infer… |
| 145 | 33 | GEN7 — pipelines (3d → image → video) | blocked · tree | a 3D generation backend — the pipeline's first stage has none, and the 3d-t… |
| 199 | 41 | CLI12 — measure codex's tool-step items and confirm (or correct) the inferr… | blocked · env | a codex-cli reinstall on this machine, then one real turn that runs tools |
| 270 | 65 | Subject drift is bounded but not eliminated, and nothing measures how often… | blocked · env | a second chat model on this machine — a drift RATE across models, not an an… |
| 348 | 56 | FSRS-B — parameter FITTING, not published defaults | blocked · data | a deployment's own logged reviews; this repository cannot invent them witho… |
| 403 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 426 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 497 | 109 | Widen the QA half: the full question set, a second embedder, a second reader | startable |  |
| 559 | 116 | Give LongMemEval's four remaining classes a shot curve | startable |  |
| 678 | 128 | Decide whether a memory seam's `Model` should beat a candidate's — today it… | decision-only | a ruling between three promises — the fix is a decision, not an edit |
| 699 | 128 | Give the fused verdict a READER-facing measurement | startable |  |
| 707 | 128 | Walk the `RetrievabilityWeight` frontier, or decide it is not worth walking | startable |  |
| 761 | 129 | Finish the sweep: `only` (21) and `placement` (12) claims, and the pre-memo… | startable |  |
| 840 | 177 | Does a newer small INSTRUCT model judge better? | startable |  |
| 871 | 177 | Price ONE model serving MANY seams, against one model per seam | startable |  |

<!-- open-items:end -->

---

## Active backlog

_**The archive is where closed work lives** — `docs/task-archive.md`, one Part per task, with why and how;
this file does not summarize it. `CHANGELOG.md` is the release-facing log, and everything before 3.0 is
history rather than context (`repo-mechanics.md`)._

**What is open is the TABLE at the head of this file, and it is GENERATED.** Every checkbox carries an
`<!-- item: state=… kind=… needs="…" -->` marker; `node devtools/dev.mjs check-backlog --write` rebuilds the
table from those markers and `verify` fails while the two disagree, so the roster and the items can no
longer drift apart. Edit the marker, never the table. **The startable set is SEVEN items.** That sentence
is hand-written on purpose and gated by `check-counts`: the banner it replaces advertised finished work
**four** times, and nothing derived it.

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

- [ ] **GEN-VERIFY — confirm the remaining unmeasured surfaces against reality.** For `sd-cli`: run one render <!-- item: state=blocked kind=env needs="a real fal.ai key, and a ~1.7 GB model download for one sd-cli render" -->
  and check the argv and the multiple-of-64 clamp. For fal: one submit → poll → fetch with a real key, checking
  the status vocabulary, the result field names and what `cost` reports. Then delete the remaining "unverified"
  notes from the XML docs — or fix the mappings and keep them.

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
  **3d has ZERO**. `GenerationKinds.Model3d` exists in the contract and no provider declares it. **The
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
  reading `request.Inputs`, and `GenerationKinds.Model3d`'s shipped XML doc claiming a chain that does not
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
  `src/Lyntai.Providers.Default/CodexAgentReader.cs`. The capture behind this backend (codex-cli 0.146.0,
  2026-08-04) ran a trivial `--oss` turn with **no tools**, so the entire tool-step half is inferred and
  marked as such in the XML docs.

  **Two blockers, and the second was only found on 2026-08-11 when the owner authorized the first:** it
  spends tokens (the owner's call, and they said yes), **and the codex CLI is not installed on this machine
  at all** — not on PATH, not in the npm global root, not in any usual location. The 2026-08-04 capture came
  from an install that is gone, so this needs a REINSTALL plus a turn, not just a go-ahead.

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

---

## Part 65 — memory optimization: the goal is now stated, so this is optimization rather than exploration (2026-08-12)

_Design §5.7.0 states what the memory engine is FOR — a lexicographic objective, the constraints an
optimization may not spend, the explicit non-goals, and the instrument's three known blind spots. It was
written because five studies in one day produced numbers nobody could act on: every one reported `MissRate`
and `PollutionRate` with no recorded priority between them, so each result had to be argued from first
principles instead of checked against a target. **Read §5.7.0 before starting anything below.**_

_**THE CLUSTER CASE IS CLOSED — see `docs/task-archive.md` Part 67 for the whole sequence.** The owner's
case ("my wife is Alice — even if I don't mention my wife, this entire relationship of mine should stay
relevant") is what `CorpusShape.AttributeCount` encodes, and `miss = 1 − 1/AttributeCount = 0.667` is the
no-graph floor by construction. Subject annotation took Chinese to **0.0000** on three shapes and improved
overall recall at the same time._

_**Two corrections that shaped everything after them, kept because the reasoning was wrong in an instructive
way.** The 2026-08-12 reading of this part — "the graph works, most shapes sit clearly BELOW the floor" —
did not survive the edge census: English had managed **2 of 3** cluster pairs and Chinese **0**, so the
below-floor result rested on a single lucky co-activation edge rather than on the mechanism working. And the
"obvious optimization target" named here (`many-candidates`, swamped spreading activation, salience the only
lever) was the wrong target: co-activation cannot link an entity cluster at all, in either language, so
strengthening it was a trap and annotation replaced it._

_What remains below is genuinely open._

- [ ] **Subject drift is bounded but not eliminated, and nothing measures how often a MODEL drifts.** The <!-- item: state=blocked kind=env needs="a second chat model on this machine — a drift RATE across models, not an anecdote" -->
  live test asserts only that SOME handle is shared by two of three facts, which is the threshold linking
  actually needs. It does not measure how often a model still invents past a perfectly good existing subject.
  That needs many live annotations across models to be worth anything — a rate, not an anecdote.
  <br>**It is blocked on a model DOWNLOAD, not on a budget**, and this line said "a measurement budget"
  until 2026-08-28, which reads as startable: this machine holds exactly one chat model (`gemma3:4b`), so
  "across models" is unreachable without pulling more.
  _**The OVERFLOW half of this item is closed (2026-08-13).** What happens once `AnnotationKnownSubjects`
  is exceeded is now measured and pinned by
  `MemorySubjectLinkingTests.The_reuse_list_evicts_the_least_used_handle_first_so_a_hub_cluster_cannot_be_broken`:
  the list evicts least-used-first, so a hub handle shared by many facts survives while singletons are cut.
  **That ordering is correct, not merely current** — most-recent-first would evict a long-standing hub the
  moment a burst of new subjects arrived, orphaning the largest cluster in the store. The failure is confined
  to the smallest clusters: a singleton may fail to grow, a large cluster cannot be broken. That is the cheap
  direction, which is why this is a bound rather than a defect. (The first draft of that test recorded
  subjects against nonexistent node ids and read back an empty list — `RecordSubjectsAsync` inserts by
  SELECTing from the node table, so it silently drops a subject for a node that is not there.)_

_**Re-measured 2026-08-13 against the 3.0 engine, and it is still there.** Single-seed replay at
`CandidateCount = 40`: miss **+0.0808**, pollution **+0.1532** (334 writes judged salient against 0 in the
control, so the arms are provably distinct). That is a different statistic from the +0.0169 below — a
30-seed mean of paired differences versus one draw — so the larger figure is NOT evidence the cost grew.
What both agree on is the direction and the mechanism, and the pollution column names it: in dense-candidate
conditions salience admits substantially more junk into the same slots.
<br>**A supported lever now exists that did not when this was filed**: `NeutralSaliencePolicy` turns salience
off for a deployment that does not want the trade (registering an empty collection does NOT — that takes the
shipped default). So the item is no longer "a shipped default with a cost and no escape"; it is a shipped
default with a measured cost and a one-line opt-out. Pinned by
`MemorySalienceInversionTests.The_many_candidates_cost_of_salience_is_bounded_on_the_current_engine`, whose
bounds are regression guards at the measured values rather than targets.
<br>**The paired sweep RAN on 2026-08-28, twice, through two real embedders** (`docs/memory-measurements.md` §5). It
settles two things and reframes the item.
<br>**One: the premise of "a bounded-admission RULE" was wrong.** That wording asked for a rule keeping
"salience's gains on the other five shapes". **There are no gains on the other five** — combined Δ miss is
positive and significant on all six shapes under `nomic-embed-text` and on five of six under
`embeddinggemma:300m`. `many-candidates` is the largest cell under the first (+0.0786) and among the largest
under the second (+0.0374), so the cost this item was filed about is real and replicates; what does not
exist is the gain it was supposed to be traded against.
<br>**Two: the rule is a NUMBER that already ships, not a mechanism to design.**
`SalienceOptions.MaxSalience` (default 4) is the ceiling on reported salience and therefore on both
consumers that ship ON — `ModulatedRetrievability` widens `CandidateCutoff` by exactly it. Its own XML doc
says **"Unmeasured — a starting point"**, and so does `NoveltyWeight`'s. At `MaxSalience = 1` the clamp
makes `StructuralSaliencePolicy` return `MemorySignals.Empty`, i.e. an option-level neutral that leaves both
registration sites untouched — the DI collection in `MemoryEngineRegistration` and
`GraphMemoryEngine.NormalizeSaliencePolicies`' "empty does NOT mean off" contract. That is **D89**'s exact
shape: move a documented-but-unmeasured constant, change no registration.
<br>**That sweep RAN the same day (`memory-salience --ceiling`) and refuted the guess.** `MaxSalience` is a
SWITCH, not a dial: `Max2`, `Max3` and `Max4` are identical in every cell, because salience is
`Clamp(1 + NoveltyWeight × novelty, 1, MaxSalience)` with `NoveltyWeight = 1.5` and `novelty ∈ [0,1]`, so the
unclamped value cannot exceed **2.5** and the shipped `MaxSalience = 4` can never bind. The self-check held:
`Max1` is indistinguishable from `Off` on all four cells while still registering a retention policy, which is
the measured form of an option-level neutral.
<br>**The `NoveltyWeight` sweep that this note named as the remaining one RAN the same day** (`memory-salience
--novelty`, 30 seeds × 2 shapes; `docs/memory-measurements.md` §5). It is a real dial where `MaxSalience` is a switch, and
turning it UP makes recall worse monotonically where it matters (`many-candidates` +0.0786 → +0.0954). It
also refuted a shipped XML claim rather than a value: a NEGATIVE weight is inert, not inverting, because the
clamp floors at 1 — corrected in `SalienceOptions` on 2026-08-29. **Whether any default MOVES is still the
owner's call, not a sweep's**: the cost is embedder-dependent by ~2.5× and `high-noise` reverses sign between
the two embedders, so no single figure is *the* cost. **Nothing in this note is startable work any more**,
and the one live thread it left — the defaults question — closed on 2026-08-30 (see below)._

_**The defaults question CLOSED 2026-08-30** as `docs/task-archive.md` **Part 127**: measured on both
embedders at the owner's direction, they pick opposite ends of the ladder (`NW0.5` under `nomic-embed-text`,
`NW3` under `embeddinggemma:300m`), so no best weight exists and neither default moved. Getting there fixed
two instrument defects — a verdict that read miss alone, and an off arm that was never off
(`docs/FIXES.md`). **Every "salience costs recall" figure quoted above was taken through that off arm**, so
it prices RETENTION with admission live in both arms; the between-rung comparisons survive untouched because
every rung shared that same baseline. `docs/memory-measurements.md` §5 carries the correction and the re-measurement._

---

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

- [ ] **Decide what an aggregator's in-band `code` means.** `OpenAiHttp.InBandError` deliberately reports <!-- item: state=blocked kind=env,data needs="two or three real aggregators to measure an in-band code against" -->
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
  AddClaudeCliProviderTests.Registered_provider_serves_through_the_router_by_id
  ClaudeCliProviderTests.Explicit_command_makes_the_provider_available
  CodexCliProviderTests.A_portable_install_is_wired_without_touching_the_process_environment
  ProcessRunnerTests.Resolve_command_path_finds_node_and_caches
  ```

  **Every one of them spawns a process or resolves a command on PATH**, and the whole set is explained by
  the last one failing: the CLI-provider and router-e2e tests all reach the deterministic provider-stub
  through `LYNTAI_PROVIDER_CMD`, which is `node`. If `ProcessRunner` cannot resolve `node`, all nine fall
  together — one cause, nine symptoms, and a constant count is exactly what that predicts.
  <br>**Why only under `verify`** is then the question worth asking, and the shape of an answer is already
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

- [ ] **Widen the QA half: the full question set, a second embedder, a second reader.** 100 of 1540 LoCoMo <!-- item: state=startable -->
  questions ran, on one local reader whose window the `full` arm exceeds. The absolute values are not
  comparable to a published number and only the ARM DIFFERENCE transfers, so what widening buys is
  confidence in the differences rather than a rankable score.
  <br>_**The cross-question contamination this item named as a precondition is FIXED** (2026-08-29,
  `docs/task-archive.md` Part 118): every question now runs against a private byte-copy of the ingested
  store, so a widened run no longer inherits it. **The QA half itself has NOT been re-run** — it needs a
  reader — so its numbers (`docs/task-archive.md` Part 115) were taken under contamination, and widening now
  means re-measuring rather than adding to them._
  <br>_**The FULL QUESTION SET half of this item is DONE (2026-09-01).** All 1,540 questions ran on eight
  arms — `docs/memory-measurements.md` §5's full-sample subsection. It was not bookkeeping: at n = 100 the same instrument
  reported a ranking × walk interaction of +6.7 and category wins of +6.2 / +5.5, and every one of those
  collapsed at full sample (+1.0, −0.6, +0.1). **What is left of this item is the SECOND EMBEDDER and the
  SECOND READER**, which is now the whole of it — restate it that way rather than leaving "the full question
  set" advertised as outstanding._
  <br>_**And a reason the second reader matters more than it did.** The judge column was calibrated against
  that run's `--dump` and is generous by ≈12 points (`docs/memory-measurements.md` §5, finding 7) — same 4B model reading
  and grading. A second reader is no longer only about confidence in the differences; it is the only way to
  separate the reader's ceiling from the memory layer's._
  <br>_**A second embedder ran on 2026-09-04, on the RETRIEVAL half rather than this one**
  (`docs/task-archive.md` **Part 154**): `embeddinggemma:300m` over the judge/fusion ladder, which corrected
  a shipped XML doc. **That does not close the embedder half of THIS item** — token-F1 with a reader is a
  different measurement — but it does answer the cheaper question the item was partly asking, and it is
  evidence the axis is worth the run: the arm ordering held while the size of one effect did not._
  <br>_**`ExpansionRetrievabilityFloor` is no longer part of this item.** It was swept across both workloads
  on 2026-08-30 (`docs/task-archive.md` **Part 123**), and the owner settled it the same day: the default
  stays at `0`. This line said it "needs more than one workload" and quoted a cost of 4 points — the
  25-question figures — after the run that superseded both; at full sample the trade is +2.8 points of
  `clean` for −1.5 of `current@k`, and `GraphMemoryOptions.ExpansionRetrievabilityFloor`'s own XML doc has
  carried the corrected pair since that run. A QA pass may still report the floor as a column; it is not a
  default this item decides._
  <br>*(**LongMemEval is no longer part of this item** — it landed 2026-08-29 in both variants,
  `docs/task-archive.md` Part 112. What it left behind is scope rather than a gap: two of its six classes are
  measured, and `multi-session` (133 questions), the three single-session classes and `BEAM` are untouched.
  None of them is obviously the next one to run, which is why this stays a QA-half item and not a
  class-coverage one.)*
  <br>**Take the haystack finding into the QA half when it runs.** The oracle variant is biased per class and
  the sign is not predictable in advance, so a QA table taken on the oracle would inherit that bias silently.
  Run it on `--haystack`, at ~40× the ingestion cost per question.

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

- [ ] **Give LongMemEval's four remaining classes a shot curve.** `multi-session` (133 questions) and the <!-- item: state=startable -->
  three single-session classes have none. **This is no longer "cheap and model-free" work** — the two classes
  that have curves each needed a metric that matches what the class ASKS (preference for the current fact;
  all-evidence recall), and the remaining four have neither defined nor obviously shared. Deciding what a
  shot BUYS on a single-session question is the task; running it afterwards is the easy half.
  <br>_**The other two thirds of this item closed 2026-08-29** (`docs/task-archive.md` Part 119):
  knowledge-update went from a 25-question sample to all 70, and the temporal class got the first shot curve
  it has ever had, on both variants._
  <br>**Read the reproducibility caveat in `docs/memory-measurements.md` §5 before adding a fifth curve**: a haystack
  figure is reproducible to about ONE question, not to a tenth of a point, and the oracle overstates the
  multi-shot gain by 2.7× on the class where that was checked.
  <br>_**The DECIDING half is done (2026-09-04) and the answer is not what this item assumed** — the classes
  were measured rather than reasoned about from their names, which is what the item was really blocked on._
  <br>**The metric IS shared, and it is temporal's all-evidence recall.** Knowledge-update's preference
  metric needs a current/stale split, which needs evidence spanning two dated sessions — and the three
  single-session classes span more than one session **0%** of the time, so that metric is structurally
  inapplicable and all-evidence recall is what is left. `multi-session` (2.5 flagged turns, 91% spanning
  several sessions) is the same shape as `temporal-reasoning` (1.9, 69%). **So "neither defined nor obviously
  shared" was wrong on both halves.**
  <br>**What actually blocks three of the four is the VARIANT, not the metric.** On the oracle the store is
  comparable to or smaller than the page: `single-session-assistant` has a median of 8 turns and **63% of its
  questions fit entirely inside `k = 10`**, so the first recall returns the whole conversation and a shot
  curve is flat by construction. Those three need `--haystack`, at ~40× the ingestion cost.
  <br>**So the item splits.** `multi-session` is runnable TODAY on the existing loader and temporal's metric
  — it needs a class switch and nothing else. The three single-session classes need the haystack, and
  `single-session-assistant` may be unmeasurable even there.
  <br>_**`multi-session` RAN on 2026-09-04** (`docs/task-archive.md` **Part 155**, `--multi --shots`, both
  variants). Shot 2 is worth **+4.8** on the haystack and shot 3 exactly nothing, so *expand once* holds on a
  fourth class — and the ORACLE said +19.2 and +6.4, a 4× overstatement that was briefly published as
  refuting that rule. **What is left of this item is the three single-session classes**, which need the
  haystack by construction and where `single-session-assistant` is likely unmeasurable at any k._ **Watch the zero-evidence questions**: 8 in
  multi-session and 6 in single-session-user carry no flagged turn, and `Load`'s existing guard drops them
  for the two classes that run today — a new class inherits that only if it takes the same branch.
  Instrument facts: `.claude/knowledge/pitfalls.md`.

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

- [ ] **Decide whether a memory seam's `Model` should beat a candidate's — today it silently loses.** <!-- item: state=decision-only needs="a ruling between three promises — the fix is a decision, not an edit" -->
  `LlmVerificationOptions.Model` and `LlmAnnotationOptions.Model` set `LlmRequest.Model`, and the router
  resolves `candidate.Model ?? req.Model` (`src/Lyntai.Core/Llm/Routing/LlmRouter.cs`), so a candidate that
  pins a model wins. **D87** derives a named client's candidates from `LyntaiOptions.DefaultCandidates` and
  keeps a model pinned there — so on any deployment that pins models globally, setting a seam's `Model` does
  NOTHING. Both seams are fail-open, so the judge or annotator simply runs on another model and nothing
  reports it: D87's own symptom shape, one subsystem over.
  <br>**The XML docs on both properties now state the precedence** (2026-09-03), which is the honest
  minimum and ships to consumers. What is NOT decided is whether the precedence is RIGHT. Three options,
  and each is a different promise: leave it and treat `ClientName` as the only reliable pin; make
  `req.Model` win, which is a routing behaviour change no consumer can detect at compile time (**D18**'s
  major-bump shape); or refuse the ambiguity outright and throw at composition when a seam names a model its
  client's candidates contradict, which is the loudest and the least convenient.
  <br>_Not startable as a code change until that is settled — the fix is a decision, not an edit._

_**The fusion item CLOSED 2026-09-04** as `docs/task-archive.md` **Part 151** / **D105**:
`GraphMemoryOptions.VerdictCombination` ships the choice with `Partition` — today's behaviour — as the
default, so nothing moves for anyone who does not set it. The surface question it was blocked on was decided
additively: changing the default would be a silent reordering no consumer can detect at compile time (D18's
shape), bought on one model and one workload. **A reader-facing check is what it did NOT get** — see below._

- [ ] **Give the fused verdict a READER-facing measurement.** `evidence-hit@k` reads the returned SET, and <!-- item: state=startable -->
  fusion also REORDERS it, so the metric that priced the option is structurally blind to half of what it
  does. Part 151 shipped on set-level evidence plus engine-level facts; what is missing is a QA arm
  (token-F1 with a reader) over `VerdictCombination = Fuse` against the partition on the same judge.
  <br>**Do not expect it to move the score** — fused, the arm lands on its base — so this is a check that
  reordering costs nothing a reader notices, not a hunt for a gain. It belongs with Part 109's QA half,
  which needs a second reader anyway.

- [ ] **Walk the `RetrievabilityWeight` frontier, or decide it is not worth walking.** `docs/memory-measurements.md` §5 <!-- item: state=startable -->
  (2026-09-03) has TWO points on it and two points are not a curve: `+sem` trades +22.0 search for −13.9
  suppression and `+sem+forget2` trades +15.0 for −7.6, so the exchange rate is roughly 1:1 and neither
  dominates. A ladder over the weight is what would locate the knee. **Proposing a default from two points
  is the error `docs/task-archive.md` Part 137 records**, so this is the work that would have to come first
  if any of it is to move a default — and it may simply not be worth it, since the shipped default already
  wins the workload this design claims.

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

## Part 129 — the decisions-vs-implementation audit, and the gate it argues for (2026-08-31)

_Opened at the owner's direction: "there might be some conflict to the actual implementation of the decision
itself". There was. **The pattern is the finding**: the log is accurate about VALUES and drifts on COUNTS and
CLASSIFICATIONS. Every stated constant verified (`ReinforceGain 0`, `SalienceWeight 0`,
`DiagnosticityWeight 0`, `SpacingWeight 1.5`, all of `DsrOptions`); every defect was a number of things or a
category._

_Fixed this session: D46's title said "four DOMAINS" against seven; D46's "the only thing that belongs at the
root" was literally false; `CLAUDE.md` said `IMemoryGraphStore` has five required members against **13**,
having dropped the "in this major" qualifier D67 carries; D47 gained the rule that its naming shape does NOT
mean "this is a domain"; D56 now points at D90 for the invariant it predates. **Two findings were WITHDRAWN
after checking** — retention was reachable via DI all along, and `IMemoryRemovalPolicy` belongs at the root
because it governs blend MEMBERS rather than entries. Both were inferred from a name or a sentence rather
than from what the code does, which is the audit's own subject turned on the auditor._

_The full record, including the findings NOT yet acted on, is the untracked
`local/superpowers/records/2026-08-31-decisions-vs-implementation-audit.md`. **It is on one machine and in no
history** — anything below that must outlive it is here._

- [ ] **Finish the sweep: `only` (21) and `placement` (12) claims, and the pre-memory entries** (everything <!-- item: state=startable -->
  below D39, essentially untouched — the pass concentrated where the churn is).
  The probe is the gitignored
  `devtools/_decisions-audit.mjs` <!-- link-ok: gitignored scratch, named as data; re-creatable from the record -->
  (re-creatable from the record's Method section — it splits on `## D<n> —`
  and extracts count / default / on-off / exclusivity / placement shapes).
  <br>**Verified clean on 2026-08-31 so they are not re-done:** all ten `default`/`onoff` claims; D96's
  counts (past-tense and correctly so); D49's "the ONLY forgetting curve" (two implementors, one a
  decorator — defensible as written); and five `placement` claims — D17 (the dialect SEAM is in
  `Core/Llm/Cli`, the dialects in `Providers.Default`, the hosting package holds neither), D25 (generation
  contracts in `Core/Generation`, backends in `src/Lyntai.Generation`), D79, D85 (`MemoryWiring` is in
  `src`), D95 (`local/sensitive-patterns.txt` is untracked — `git ls-files` returns nothing).
  <br>Sampled rather than exhausted so
  far; **prose claims with no extractable shape are invisible to it**, and that is probably where the
  remaining defects are.

  _**Swept 2026-09-02 and NOTHING was found**, which is worth recording precisely because it is weak
  evidence. The probe was re-created (`devtools/_decisions-audit.mjs` <!-- link-ok: gitignored scratch, re-creatable from this note --> — split on
  `## D<n> —`, extract shapes) and the two remaining populations enumerated: **26 uniqueness claims across
  20 entries** and **23 placement claims**, both wider than the 21/12 the item quotes, because the original
  probe's pattern is gone and mine is not it. Checked by hand: **D88** (only `Grade == Authoritative` reaches
  the reserve — true), **D67** (generation contracts in `Core/Generation`, backends in `src/Lyntai.Generation`
  — true), **D46** (already scoped precisely to a policy DOMAIN's types, so the ~40 contract types at the
  root are not violations), **D15** (`release.yml` is the only workflow — true). Together with the five
  placement and ten default/on-off claims the 2026-08-31 pass verified, the EXTRACTABLE population is now
  largely clean._
  <br>_**D46 was checked because this session might have broken it.** `MemoryDetail` (**D104**) is a new type
  at the root of `Lyntai.Memory`, and D46's claim names `MemoryDecayState` as the only DOMAIN-shaped type
  there. It holds: the entry already scopes "domain-shaped" to a policy domain's seam, implementations or
  options, and lists `MemoryQuery`/`MemoryGrade` as correctly at the root — which is the category
  `MemoryDetail` joins. **A decision worth auditing is one your own change could invalidate**, and that is a
  cheaper trigger than a periodic sweep._
  <br>_What is left is the item's own honest limit: claims with no extractable shape. A regex population is
  not the population._
  <br>_**The PRE-MEMORY entries were opened for the first time on 2026-09-04, and both claims checked came
  back TRUE in substance and got GATED rather than re-audited** — `check-decision-claims` is now eight
  predicates. **D14** (wire JSON is hand-walked) holds: zero `JsonSerializer` uses in the LLM, provider and
  generation paths. **D6** (every SQLite object carries `lyntai_`) holds, version table included.
  <br>**Two process findings came out of gating them, and both are the audit's own subject turned on the
  auditor.** The hand-check of D14 covered THREE of the six wire paths and called it clean; the predicate
  immediately found a fourth. Then the predicate's first run flagged the two files whose comments say
  "JsonDocument.Parse (not JsonSerializer) so the package stays trim/AOT-clean" — the sites most explicitly
  HONOURING D14 — so it now strips comments, the mirror of the trap `check-links` records. **A predicate is
  worth more than an audit precisely because the audit is what missed a path.**
  <br>**Why these two were picked, so the rest can be prioritised the same way:** both are claims whose
  violation would be a shipped DEFECT rather than stale prose — D14 is a trim promise no `check-warnings`
  run can see (a source-generated context is AOT-safe and would still falsify it), and an unprefixed SQLite
  object collides in a consumer's own database. Entries whose claims are merely descriptive can wait._

_**The gate this Part argued for is BUILT** — `docs/task-archive.md` **Part 130**, 2026-08-31:
`check-decision-claims` is wired into `verify`, and shipped with six predicates, each verified by hand
before registering and each driven RED by a synthesized tree in its own test. **The count is deliberately
not restated here** — it grows whenever the sweep gates another claim (two more on 2026-09-04), so a number
in this sentence would go stale exactly when the Part is making progress. `verify`'s own summary line is
derived and always right. What is left of this Part is the sweep above._

---

## Part 177 — ONE small model doing MANY jobs: the shape a heavy application actually needs (2026-09-10)

_Opened at the owner's direction: "there will be a heavy application using a small model to perform, and it
might need to be multi tasking too". That is a different question from "which model is best at task X", and
the library is already most of the way to answering it — **no new API is needed**. The seams exist:
named `ILlmClient`s (**D87** — "reranking, salience judging … should not silently run on whatever backend
happens to be default"), `LlmAnnotationOptions.ClientName` ("annotation runs on EVERY write, so it belongs
on a small fast backend"), `LlmConsumers` for per-seam cost attribution, and `AddLlamaProvider`. What is
missing is MEASUREMENT._

_**Two findings already constrain this and should be read first.** A task-shape rule: a GENERATIVE task
takes a budget (the extractor went 7.1 → 2.1 facts/turn when told "at most 2") while a SELECTIVE task over
a visible list does not (the judge asked for ≤20 of 80 endorsed MORE, 34.9 against 29.1) — so the fix for a
selective task is a structural constraint, never a prompt. And **D110**: on a judge whose endorsements are
97.7% noise, no promotion rule over them helps, so model choice and calibration are the levers rather than
the plumbing._

_**What is already measured** (`docs/memory-measurements.md` §5, archive Parts 175–176): a 468 MB cross-encoder captures
6.0 of the 7.0 points a perfect judge offers, and a model 28 months newer at the same architecture and size
is IDENTICAL — so in the RERANKER role, recency buys nothing and size can come down 26%._

- [ ] **Does a newer small INSTRUCT model judge better?** The open half of the recency question. Part 176 <!-- item: state=startable -->
  tested recency in the RERANKER role, where the task is "score a pair"; the incumbent `gemma3:4b` fails in
  the JUDGE role, where the task is "decide IF each of 80 answered, and stop" — and it fails at STOPPING,
  not at ranking (39.0% precision at its own top-1 against 2.3% overall, a 17× lift). Instruction-following
  and calibration on a long visible list is the axis; IFEval is the published proxy. Swapping the model is a
  config change (`LYNTAI_LIVE_CHAT_URL` / `_MODEL`), so this costs a download and one ladder.
  <br>**Read `+judge@40` before choosing a target**: the same model at half the shipped depth is already
  level with no judge — 84.0% against the unjudged base's 83.0% — so a newer model has to beat THAT, not
  the **−10.5** the shipped depth produces (72.5%).
  <br>_**This line quoted −14.5 until 2026-09-10 and that figure is in no maintained record**, nor does it
  follow from any published pair: `docs/memory-measurements.md` §5 states the robust result as the
  −10.5 → +1.0 swing across depth, and the shipped depth reads −11.5 against `+judge@40` rather than −14.5.
  Corrected while writing `docs/model-tasks.md`, which cites the same table._
  _**The candidate shortlist, surveyed 2026-09-10 and adversarially re-checked against the model cards and
  the HF API.** A DESK survey — sizes and capabilities read, not called — which is the tier GEN-VERIFY exists
  to distrust, so the SHAPES transfer and nothing here licenses skipping a smoke test. Sizes are exact bytes
  because MiB and MB straddle a 500 threshold (`pitfalls.md`)._
  <br>_**Rerankers under 500 MB, multilingual:** `LAMAR-600m` Q5_K_M **468,393,760 B** — measured, see Part
  176. `xVITA-300M` Q8_0 **332,894,432 B** (2026-08-23, modern-bert) is the untested one and is the smallest
  credible candidate. `Qwen3-Reranker-0.6B` Q6_K **494,879,136 B** is **deprioritised for a Chinese-first
  deployment**: it is 0.85 BEHIND bge on MTEB-zh (71.31 against 72.16) while +8.77 on English, and jina's
  independent table scores it BEIR 56.94 against bge's 56.42 — so the English gain is protocol-dependent. It
  is also `Qwen3ForCausalLM` scoring yes/no logits, not a `*ForSequenceClassification` cross-encoder._
  <br>_**Two dead ends, recorded so they are not re-walked:** `bge-reranker-base`/`-large` are "Chinese and
  English" per their own card — fine for the first phase, a dead end for the JP/KR one. And `gte`'s GGUF
  declares architecture `new`, which llama.cpp does not register, so it cannot load at all._
  <br>_**Provenance matters more than the quant here.** `mradermacher`'s Qwen3-Reranker Q6_K has **310**
  tensors against the working **311** — it is missing `cls.output.weight` and scores silently wrong
  (llama.cpp #16407). `Voodisss` and `zhiqian99` are byte-identical to each other and correct. Prefer an
  official conversion, and smoke-test whatever you pull._

- [ ] **Price ONE model serving MANY seams, against one model per seam.** The deployment shape the owner <!-- item: state=startable -->
  named. A `llama-server` serves ONE model per process, so "multi-tasking" is either several resident
  servers (memory-hungry, never swaps — the 2.6× advantage `repo-mechanics.md` records) or one router
  server (`--models-dir`, which swaps). Measure: throughput and latency when annotation, judging and
  reranking contend for one backend, against dedicated ones. **`memory-scale` is the harness shape to
  mirror** — its subject is COST, it reports latency/throughput/bytes and no recall quality, and it runs
  SEQUENTIALLY because contention biases a latency silently.
  <br>**D107 is the warning**: the last concurrency ceiling found here was a process-global mutex no grep of
  our own code could have found. Expect the answer to be an artefact of the serving layer rather than of the
  model.
_**The taxonomy item CLOSED 2026-09-10** as `docs/task-archive.md` **Part 185**: `docs/model-tasks.md`,
reached from `CLAUDE.md`, `README.md`, `docs/memory.md` and `.claude/knowledge/model-decoupling.md`. **Its
four proposed shapes were not the set** — a sweep found four more the list could not name, `score-a-pair`
turned out to be two unrelated tasks, and `classify` overstated what this library hands a model. **Read
that doc's §3 before scoping either item above**: the only sub-500 MB evidence this repository holds tests
the RERANKER role, is `ships=no`, and the library ships no adapter that can call a rerank endpoint — so
"which shapes survive at <500 MB" is still eight blanks and one qualified cell._

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
