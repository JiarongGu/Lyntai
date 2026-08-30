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

_**The archive is where closed work lives** — `docs/task-archive.md`, one Part per task, with why and how;
this file does not summarize it. `CHANGELOG.md` is the release-facing log, and everything before 3.0 is
history rather than context (`repo-mechanics.md`)._

**READ `## Part 116` FIRST — it is the handover.** `docs/DECISIONS.md` **D100** changed what this engine is
evaluated as during the 2026-08-29 session: an n-shot WALK, not a single top-k. Every recall-quality number
published before that day scores one shot, which measures a vector index wearing a graph engine's name. Part
116 carries what that opens. **Its biggest item — the library having no n-shot surface — CLOSED on
2026-08-30** as `docs/task-archive.md` **Part 120** / **D102**: `MemoryWalk.WalkAsync` ships the walk as an
extension over the two seams that already existed, and both bench harnesses now drive it. Its naming pass
closed the same day as **Part 121**, so **what is left in the Part is measurement and nothing else.**

**The startable set is TWO items: Part 116's one and Part 109's QA half.** Each is a `- [ ]` you could open
today — which is the test this
banner failed twice on 2026-08-29, so apply it literally: **if the banner names something that is not an
open checkbox below, the banner is wrong.** Both names it carried that day were sweeps that had already run,
with their write-ups sitting in `docs/memory.md` §5 while the banner advertised them.
<br>_It held THREE until 2026-08-30, when GEN7a — the `image → video` pipeline runner Part 124 had just put
here — was built and closed as `docs/task-archive.md` **Part 126**. Both remaining items are measurement._

**HANDOVER (2026-08-30, end of session): the next session's direction is MEMORY OPTIMIZATION**, at the
owner's request. Read this before picking anything up, because the bottleneck is not work — it is three
questions only the owner can answer, and a session that starts measuring will hit all three inside an hour.

1. **`SalienceOptions.MaxSalience` and `NoveltyWeight` — do the two shipped defaults stay?** (Part 65, under
   Blocked.) Everything measurable HAS been measured: `MaxSalience` is a switch rather than a dial and at
   `NoveltyWeight = 1.5` it can never bind, so the shipped `4` is inert on this corpus. Moving it is not a
   no-op in general, which is exactly why it is a decision and not a sweep.
2. **What does the gist tier COMPUTE?** (Part 105, under Blocked.) The measurement came back negative — no θ
   is both pacing- and cardinality-independent, and the one invariant rule is the raw count, which the corpus
   declares wrong for the assistant host. The three candidates are named in the item; none is obviously right.
3. **Does `ExpansionRetrievabilityFloor` move off `0`?** (Inside Part 109's open QA item, so this one rides
   along with startable work rather than blocking it.) **D98** shipped it at `0` on one class of one
   benchmark; `0.8` held the shot curve flat there and cost 4 points of current-fact hit rate. It needs more
   than one workload before anything moves.

**And the standing trap for whoever measures next, because it has now cost three published figure sets:**
every recall-quality number is a property of the INSTRUMENT until proven otherwise. Part 118 (shared stores),
Part 119 (a near-tie noise floor of ~1 point) and D100's own withdrawn *"search wants two shots"* were all
harness, not library. Before believing a delta, run the arm that structurally CANNOT move — `vector` never
touches the graph store — and repeat the after arm rather than reasoning about it.
<br>_Part 116 held five until Parts 117–121 took three of them outright and two thirds of a fourth; the
naming pass it gained on 2026-08-30 was filed BLOCKED and closed the same day, so it never counted toward
the startable set. Both numbers are edited in the same change as the item, which is the habit `pitfalls.md`
prescribes after four stale banners._
<br>**Part 109's `K` sweep CLOSED 2026-08-30** as `docs/task-archive.md` **Part 122**, and it overturned its
own premise: `K` does select a REGIME, but "the shipped 60 is on the wrong side of free" was one workload
wide and one sample thin. LoCoMo pays 4.5 points of evidence-hit going to K = 120, and the full 70-question
haystack pays 6.0 points of `current@k` where the 25-question sample said 0.0. **60 is a priced compromise,
and no default moved** — what is left of that Part is its QA half alone. Its other three items closed
earlier (**D97**, Part 112's haystack run, Part 113's ranking ladder).
<br>**The 3D-backend survey CLOSED 2026-08-30** as `docs/task-archive.md` **Part 124**, and like Part 122 it
overturned its own premise rather than picking one of its two options: a mesh cannot chain into any backend
here, and no 3D backend produces a turntable — so `3d → image → video` is not buildable at all, and the 3D
stage's real blocker is a RASTERIZER that does not belong in this library. **It replaced itself in the
startable set with GEN7a**, the `image → video` runner, which is GEN7's whole design minus the stage that
has no backend — **and GEN7a was built and CLOSED the same day** as `docs/task-archive.md` **Part 126**. It
also found two OUTPUT-stage defects, both FIXED that day as **Part 125** — a
capability ComfyUI declared and never implemented, and a false XML doc that shipped.
<br>**Part 65 was in this list for an hour and is not any more**, which is the second half of the same test:
its remaining half turned out to be a DECISION (`MaxSalience`'s default), and a decision nobody has taken is
not work somebody can start — the same reason Part 105 sits under Blocked. Everything measurable in it has
been measured. The rest of this file needs something this repository does not have (a key, a model download,
a CLI install, a vendor pick, or a deployment's own data). **Part 99 is a WATCH item and not startable work** — its fix is already
pinned by a test with a positive control, so nothing in it is codeable and only RECURRENCE can close it.
That is stated first
rather than buried, because it is the answer to the question the file exists to answer. **Read the caveat
two paragraphs down before trusting any "blocked" label here**: a banner that over-claims blockage hides
startable work inside, and this one has now been wrong that way three times — most recently on 2026-08-28,
when it named Part 99 (which is codeable by nobody) while omitting `many-candidates`, whose own sub-bullet
had read **NO LONGER BLOCKED** for five days, and GEN7's survey.

_This banner named `Part 65 / many-candidates` as the one startable item until 2026-08-26, and that item had
said "CLOSED as **D89**" inside itself since 2026-08-23 — so the file's own summary was steering readers at
work that was finished. It is `docs/task-archive.md` Part 98 now. **A stale banner is worse than a stale
entry**: the entry is one item, the banner is the answer to the question the file exists for._

_**Then twice more on 2026-08-29, and the second one is the instructive half.** The banner still named the
`many-candidates` PAIRED SWEEP, which had run the day before and is written up inside that very item. That
was corrected — to **`NoveltyWeight`**, which had ALSO already run, in the same commit as the sweep it
replaced. So the correction repeated the defect it was fixing, and shipped: the fix was made by re-reading
the item's own prose, and that prose named the next knob without saying it had already been turned._

_**Four instances, one mechanism: an item amended IN PLACE does not amend the banner, and the amendment is
exactly when the banner goes stale.** Re-reading the entry is not enough, because the entry is what went
stale. **Check the instrument instead** — `docs/memory.md` §5 or the archive will say whether the thing you
are about to advertise has already run — and re-read the banner against every item you touch, in the same
change. `.claude/knowledge/pitfalls.md` carries the rest, including why the three obvious gates for this
each catch one instance in four._

**The pattern to expect: the next startable item arrives from a CONSUMER, not from this list.** Every
same-day burst of work since 3.0 came in that way, and the archive has each one. This banner does not
enumerate them — a running tally of closed Parts is the accumulation the lifecycle rule exists to prevent,
and it was allowed to grow here twice.

**Two rules for reading a "blocked" label here**, both earned by this file being wrong:

- **A Part is blocked when its DELIVERABLE is; that does not make every sentence in it blocked.** Part 33 was
  marked blocked in full while two startable pieces sat inside it (closed as **D67** and **D68**), neither
  needing the key the Part waits on. When labelling something blocked, name what the blocker actually gates.
- **An ENVIRONMENT blocker has to be re-checked against the environment, not against the tree.** The
  2026-08-21 re-check read the tree and never asked the machine, so Part 65's `many-candidates` stayed
  labelled blocked on "a real embedding model" while one sat pulled on this machine.

**Every blocker below was re-checked on 2026-08-28, each against its own KIND** — the environment ones by
querying the machine, the tree ones by reading the tree. All held. The ENVIRONMENT: `codex` is absent from
PATH *and* from the npm global root, and **no vendor key is set** in the environment (so GEN-VERIFY, GEN6
and Part 75 all stand). Ollama is up with `nomic-embed-text`, `embeddinggemma:300m` and `gemma3:4b`, which
is what keeps `many-candidates` unblocked. The TREE: `GenerationKinds.Model3d` is still a bare constant in
Core that no provider declares, and `OpenAiHttp.InBandError` is unchanged.
<br>**The re-check changed no blocker and still moved the banner**, which is the point of doing it by kind:
what was wrong was not a blocker but the file's own summary of which items they gate.

Blocked, and on what:
- **Part 33 / GEN-VERIFY** — a real fal.ai key, and a ~1.7 GB model download for one `sd-cli` render.
- **Part 33 / GEN6 (streaming TTS)** — a vendor pick and a key. Shipping it unmeasured is the exact mistake
  GEN-VERIFY exists to correct.
- **Part 33 / GEN7 (pipelines)** — the pipeline's FIRST stage has no backend at all: `GenerationKinds.Model3d`
  is a bare constant no provider declares (re-checked 2026-08-28). **The survey that was the critical path
  here RAN on 2026-08-30** (`docs/task-archive.md` Part 124): a mesh cannot chain and no 3D backend produces
  a turntable, so `3d → image → video` is not buildable and the 3D STAGE is what stays blocked — on a
  RASTERIZER, which is not a generation backend and does not belong in this library. **What that unblocked
  was the runner at `image → video`, and that was built and closed the same day** (GEN7a,
  `docs/task-archive.md` Part 126) — so what remains blocked here is the 3D STAGE alone, not the runner.
  Listing this Part as blocked without saying which half is the Part 33 mistake repeating itself.
- **Part 41 / CLI12** — codex's tool-step item names need a real turn **that runs tools**. Two blockers, and
  the second was only discovered on 2026-08-11 when the owner authorized the first: it spends tokens (the
  owner's call, and they said yes), **and the codex CLI is not installed on this machine at all** — not on
  PATH, not in the npm global root, not in any usual location. The 2026-08-04 capture (0.146.0) came from an
  install that is gone. So this needs a REINSTALL plus a turn, not just a go-ahead.
- **Part 105 / build the gist tier** — a DECISION nobody has taken: which support rule it computes.
  **The DATA half of this blocker is DISCHARGED (2026-08-28)** — the cardinality sweep ran, and it answered
  negatively: no θ is both pacing- and cardinality-independent, and the one invariant threshold is the raw
  count. So the decision is no longer waiting on a measurement; it is waiting on somebody choosing what the
  tier computes when no constant is defensible. **Not an environment blocker.**
- **Part 65 holds TWO items with DIFFERENT blockers, and only ONE of them is blocked** — listing the Part
  here is how the other read as blocked in turn (corrected 2026-08-21, and again 2026-08-28 in the banner).
  - *subject drift* — **still blocked, and the honest kind is a model DOWNLOAD, not a budget.** It needs a
    RATE across models rather than an anecdote, and this machine holds exactly one chat model
    (`gemma3:4b`), so "across models" is unreachable without pulling more. The line below said "a
    measurement budget" until 2026-08-28, which reads as startable.
  - *the `MaxSalience` defaults question* — **blocked on a DECISION, not on an environment.** This slot used
    to hold `many-candidates`, which was blocked on a real embedding model (salience reads NOVELTY, and
    without an embedder `StructuralSaliencePolicy` declines on every write, so any sweep of a salience bound
    is flat by construction — and `FakeEmbedder` cannot stand in, Part 69 having withdrawn the numbers taken
    through one). That cleared when `embeddinggemma` was pulled here; the paired sweep ran on 2026-08-28 and
    the `--ceiling` and `--novelty` ladders on 2026-08-29. Everything measurable has now been measured and
    the stale XML doc is fixed, so what is left is one question for the owner: two shipped defaults make one
    of them inert — keep them?
- **Part 56 / FSRS-B** — a deployment's own logged reviews. The observable now exists; the data does not,
  and this repository cannot invent it without repeating the mistake D49 refused.
- **Part 75** — two or three real aggregators to measure an in-band `code` against. Reading it unmeasured is
  the documented-not-measured trap GEN-VERIFY exists to correct. (The rest of Part 75 has closed; this line
  exists because a Part whose blocker is unlisted reads as startable, which is how Part 33 hid two
  startable items — see the caveat above.)

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

- [ ] **GEN-VERIFY — confirm the remaining unmeasured surfaces against reality.** For `sd-cli`: run one render
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

- [ ] **GEN6 — streaming audio (TTS).** A streaming TTS backend against a real vendor. **The scope shrank on
  2026-08-16** (`docs/DECISIONS.md` **D67**): the PLATFORM half is done and shipped in 3.0 —
  `IGenerationRouter.StreamAsync` selects, falls over, governs and throttles a `Stream`-capable backend, and
  the router guarantees exactly one terminal chunk, so a backend no longer has to be careful about fallback or
  closing its own stream. What is left is a real backend and the thing only it can settle: whether
  data-then-terminal is the decomposition a real TTS wire format wants. So this is no longer "the seam is
  unexercised" — the handling is measured by `GenerationRouterStreamTests`; it is "the chunk SHAPE is still
  inferred". **TTS before music** (owner). Needs a vendor pick and a MEASURED wire format (the GEN-VERIFY
  lesson), so it waits on a key rather than shipping another documented-not-measured surface.
- [ ] **GEN7 — pipelines (3d → image → video)**: ordered stages feeding `artifact.ToInput(role)` forward, with
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

- [ ] **Subject drift is bounded but not eliminated, and nothing measures how often a MODEL drifts.** The
  live test asserts only that SOME handle is shared by two of three facts, which is the threshold linking
  actually needs. It does not measure how often a model still invents past a perfectly good existing subject.
  That needs many live annotations across models to be worth anything — a rate, not an anecdote — so it is
  blocked on a measurement budget rather than on a design question.
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
<br>**The paired sweep RAN on 2026-08-28, twice, through two real embedders** (`docs/memory.md` §5). It
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
--novelty`, 30 seeds × 2 shapes; `docs/memory.md` §5). It is a real dial where `MaxSalience` is a switch, and
turning it UP makes recall worse monotonically where it matters (`many-candidates` +0.0786 → +0.0954). It
also refuted a shipped XML claim rather than a value: a NEGATIVE weight is inert, not inverting, because the
clamp floors at 1 — corrected in `SalienceOptions` on 2026-08-29. **Whether any default MOVES is still the
owner's call, not a sweep's**: the cost is embedder-dependent by ~2.5× and `high-noise` reverses sign between
the two embedders, so no single figure is *the* cost. **Nothing in this note is startable work any more; the
one live thread it left is the item below.**_

- [ ] **Two option defaults, and one makes the other inert.** `SalienceOptions.MaxSalience` defaults to 4,
  and `StructuralSaliencePolicy` computes `Clamp(1 + NoveltyWeight × novelty, 1, MaxSalience)` with
  `NoveltyWeight = 1.5` and `novelty ∈ [0,1]` — so the reachable maximum is **2.5** and the shipped ceiling
  can never bind. Measured, not inferred: `Max2`, `Max3` and `Max4` are byte-identical in every cell of
  `memory-salience --ceiling`.
  <br>_**The DOC half closed 2026-08-29** and is not what remains. `MaxSalience`'s own XML read
  "**Unmeasured** — a starting point" after the ladder that measured it, and never said that at the shipped
  `NoveltyWeight` it cannot bind — so a consumer hovering the member got neither fact, and XML docs SHIP
  (`pitfalls.md` records that family). The containment argument was documented on `NoveltyWeight`, i.e. on
  the wrong member. Both now carry what their runs found._
  <br>**What is left is a DECISION nobody has taken: should those two defaults ship as they are?** Moving
  `MaxSalience` is NOT a no-op in general — it is one on this corpus only because `NoveltyWeight` is 1.5, and
  a consumer who raises that would feel a lowered ceiling. So this is the same shape as Part 105: measured,
  documented, and waiting on somebody choosing. It is listed under Blocked for that reason.

---

## Part 56 — complete FSRS: `DsrRetrievability` is a PARTIAL, UNFITTED model, and that gap is measured (2026-08-10)

_Opened by `docs/DECISIONS.md` D49, which shipped `DsrRetrievability` as the 3.0 default on FSRS's own
external validation while disclosing a real, measured gap: this implementation carries FSRS's functional
FORM with none of its calibration. **Completing it is prioritized work — the `topical` regression D49 ships
knowingly is where the gap shows up measurably, not a reason to avoid shipping the default.**_

- [ ] **FSRS-B — parameter FITTING, not published defaults.** Every constant in `DsrOptions` (`Decay =
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

- [ ] **Decide what an aggregator's in-band `code` means.** `OpenAiHttp.InBandError` deliberately reports
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

- [ ] **`verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted mid-run.** Observed
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

## Part 105 — gist support: the CARDINALITY axis the sweep held constant (2026-08-28)

_What Part 104 left open. The support seam itself is settled — `docs/DECISIONS.md` **D94**, no
`IMemorySupportPolicy` — and the sweep is built (`node devtools/dev.mjs memory-support`, tables in
`docs/memory.md` §5). **The cardinality axis closed 2026-08-28 — `docs/task-archive.md` Part 108.** What is
left is one item, and its blocker is a DECISION rather than the data it used to wait on._

- [ ] **Build the gist tier.** **BLOCKED on a DECISION nobody has taken** — which support rule it computes.
  **D94** settled the tier's SHAPE (no seam), and the measurement half is now DONE rather than pending:
  `docs/memory.md` §5 carries both sweeps.
  <br>**The measurement came back negative, which is what makes this a decision rather than more work.**
  `sum` inverts with pacing. Every DISCRIMINATING `count@θ` inverts with it. Of the two thresholds that
  looked pacing-independent, **θ = 0.9 turned out to be an artefact of `RoutineCount = 12`** — it walks
  tie → A → B → B across |A|/|B| — leaving **θ = 0.1 as the only rule invariant on both axes, and it is the
  raw count**, which this corpus declares wrong for the assistant host. `mean` remains untestable on the
  corpus as it stands (phase B is snapshotted at the retrievability ceiling).
  <br>So there is no constant to adopt, and the open question is what the tier should compute INSTEAD:
  a rule with no constant threshold, a corpus that can test `mean` (phase B off the ceiling at the
  snapshot — a corpus change, not a sweep argument), or a tier that reports N and declines to select.

## Part 109 — LoCoMo says the shipped ranking defaults lose to plain cosine on a uniform-history workload (2026-08-29)

_Opened by `docs/task-archive.md` Part 110; the first item CLOSED as **D97** (`docs/task-archive.md`
Part 111) and the LongMemEval half closed as **Part 112**. `node devtools/dev.mjs memory-locomo --retrieval`
is the first measurement this repository has taken on an instrument it did not build: evidence-hit@20,
model-free, 200 LoCoMo questions. Defaults went **11.0% → 31.0%** on D97 and plain cosine is **80.5%** at the
same k, so a real gap remains. Tables and the two harness defects that had to be fixed first are
`docs/memory.md` §5.
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

- [ ] **Widen the QA half: the full question set, a second embedder, a second reader.** 100 of 1540 LoCoMo
  questions ran, on one local reader whose window the `full` arm exceeds. The absolute values are not
  comparable to a published number and only the ARM DIFFERENCE transfers, so what widening buys is
  confidence in the differences rather than a rankable score.
  <br>_**The cross-question contamination this item named as a precondition is FIXED** (2026-08-29,
  `docs/task-archive.md` Part 118): every question now runs against a private byte-copy of the ingested
  store, so a widened run no longer inherits it. **The QA half itself has NOT been re-run** — it needs a
  reader — so its numbers (`docs/task-archive.md` Part 115) were taken under contamination, and widening now
  means re-measuring rather than adding to them._
  <br>**And sweep `ExpansionRetrievabilityFloor` while doing it** (**D98**). It ships at `0` on one class of
  one benchmark; 0.8 held the shot curve flat there and cost 4 points of current-fact hit rate. Whether any
  default moves is the owner's call, and it needs more than one workload.
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
`memory-longmemeval --shots [--haystack] [--expand-floor w]` — and the tables are `docs/memory.md` §5.
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

- [ ] **Give LongMemEval's four remaining classes a shot curve.** `multi-session` (133 questions) and the
  three single-session classes have none. **This is no longer "cheap and model-free" work** — the two classes
  that have curves each needed a metric that matches what the class ASKS (preference for the current fact;
  all-evidence recall), and the remaining four have neither defined nor obviously shared. Deciding what a
  shot BUYS on a single-session question is the task; running it afterwards is the easy half.
  <br>_**The other two thirds of this item closed 2026-08-29** (`docs/task-archive.md` Part 119):
  knowledge-update went from a 25-question sample to all 70, and the temporal class got the first shot curve
  it has ever had, on both variants._
  <br>**Read the reproducibility caveat in `docs/memory.md` §5 before adding a fifth curve**: a haystack
  figure is reproducible to about ONE question, not to a tenth of a point, and the oracle overstates the
  multi-shot gain by 2.7× on the class where that was checked.

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
