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

_**v3.0.0 is released (2026-08-17); `CHANGELOG.md`'s `## 3.0.0` section carries the whole of it** — the
memory retention model AND the pre-freeze sweep that followed it (the generation stream door, the
cross-process job cap, the forget/prune split, the SemVer-exemption withdrawal). Reading it as "the memory
work" is how a session misses that `IJobStore` and `IGenerationRouter` gained required members too.
Everything through the generation platform, the package restructure, the provider-lifetime seam, the codex
agent session, app-owned MCP servers, the long-term memory subsystem and the multilingual measurement has
shipped and is archived. **The archive is where closed work lives** — `docs/task-archive.md`, one Part per
task, with why and how; this file does not summarize it._

**Nothing here is startable right now — every remaining item needs something this repository does not
have** (a key, a model download, a CLI install, a vendor pick, a measurement budget, or a deployment's own
data). That is stated first rather than buried, because it is the answer to the question the file exists to
answer. **Read the caveat two paragraphs down before trusting it**: a banner that over-claims blockage hides
startable work inside, and this one has been wrong that way before.

Startable work has kept arriving in bursts and closing the same day: three items via Parts 85/86
closed in **Part 87**, the 2026-08-17 whole-repo review (nine subsystem reviewers + per-finding
adversarial verification) opened twelve verified findings that all closed the same day in **Part 88** —
which is also where the two finding-halves that measurement refuted are recorded — and the two
adopter-reported Parts that arrived on 3.0.0 closed in **`docs/task-archive.md` Parts 89 and 90**
(the composition renderer, per-entry curated grades, fan-out writes, the wiring check, and cross-scope
semantic recall; `docs/DECISIONS.md` **D83–D86**).

**A caveat this banner earned on 2026-08-16, and it applies to any "blocked" label here.** Part 33 was marked
blocked in full while two startable pieces sat INSIDE it — a settled-by-writing-it-down decision buried in
GEN-VERIFY's notes (the max-dimension question, closed as **D68**) and a platform gap the seam's own remarks
described without ever being filed as work (the unreachable stream door, closed as **D67**). Neither needed
the key the Part is blocked on. **A Part is blocked when its DELIVERABLE is; that does not make every
sentence in it blocked**, and a startable sub-item buried in a blocked Part is invisible in exactly the way
this list exists to prevent. When labelling something blocked, name what the blocker actually gates.

The four older parts are each blocked on something this repository does not have — a key, a model download,
a CLI install, or a deployment's own data. That was briefly true of the WHOLE file (2026-08-15, when Parts
70, 72 and 69 closed), and this banner said so; the pre-3.0 review then opened **Part 75**. Its items
were all startable in a session, each found, verified and deliberately deferred with its reason recorded —
and they have all now closed (archive Parts 76, 78–81 and 84) except one, which needs real aggregators to
measure. It was briefly untrue again when Parts 89 and 90 arrived from adopters (closed 2026-08-21), which
is the pattern worth expecting: **the next startable item will arrive from a consumer, not from this
list.**

**Every blocker below was re-checked against the tree on 2026-08-21** — because a "blocked" label is a claim
development can overtake, and this file has been wrong that way before. Six held (`codex` is still absent
from PATH; `GenerationKinds.Model3d` still has no provider; twelve unverified-surface markers still sit in
the generation backends; `OpenAiHttp.InBandError` is unchanged). **One was stated wrongly** — see Part 65,
where the named obstacle was a missing instrument and the real one is a missing model.

Blocked, and on what:
- **Part 33 / GEN-VERIFY** — a real fal.ai key, and a ~1.7 GB model download for one `sd-cli` render.
- **Part 33 / GEN6 (streaming TTS)** — a vendor pick and a key. Shipping it unmeasured is the exact mistake
  GEN-VERIFY exists to correct.
- **Part 33 / GEN7 (pipelines)** — a 3D backend survey; the pipeline's FIRST stage has no backend at all.
- **Part 41 / CLI12** — codex's tool-step item names need a real turn **that runs tools**. Two blockers, and
  the second was only discovered on 2026-08-11 when the owner authorized the first: it spends tokens (the
  owner's call, and they said yes), **and the codex CLI is not installed on this machine at all** — not on
  PATH, not in the npm global root, not in any usual location. The 2026-08-04 capture (0.146.0) came from an
  install that is gone. So this needs a REINSTALL plus a turn, not just a go-ahead.
- **Part 65 holds TWO items with DIFFERENT blockers, and listing one of them here is how the other read as
  startable** (corrected 2026-08-21 — the caveat above, applied to this file's own banner).
  - *subject drift* — a measurement budget: a RATE across models, not an anecdote.
  - *`many-candidates`* — **a real embedding model.** Salience reads NOVELTY, novelty needs an embedder, and
    without one `StructuralSaliencePolicy` declines on every write — so any sweep of a salience bound is
    flat by construction, and `FakeEmbedder` cannot stand in (Part 69 withdrew the numbers taken through it).
    Same dependency `memory-enrichment` carries, and the same reason it exits rather than faking one. The
    earlier wording ("needs a sweep that varies the admission BOUND, which does not exist") named a missing
    instrument rather than the missing input, and a missing instrument is something this repository builds.
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
  The 3D-backend survey stays a prerequisite and is now the critical path: **mesh vs turntable stills, where
  only the latter chains into today's video backends.** That question decides whether a 3D stage can feed the
  rest at all, so it must be answered before the runner's shape, not alongside it.

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
<br>**What remains genuinely open** is the bounded-admission RULE — a rule that keeps salience's gains on the
other five shapes while capping what it may displace when the candidate set is dense. Designing that off a
single-seed replay would be the over-fitting D49 refused; it needs the paired sweep._

- [ ] **The `many-candidates` regression is the one measured cost of a shipped default.** Salience makes that
  shape's combined MissRate WORSE (+0.0169, significant) while improving every other shape, because with 40
  competitors admitting salient entries displaces relevant ones. Under §5.7.0's priority order this is a real
  regression on line 2 traded for a gain on line 1, which is the correct direction — but it is the sharpest
  known cost and the obvious first target for a bounded-admission rule.
  <br>**The paired sweep this item waits on now EXISTS (2026-08-15)** — `node devtools/dev.mjs
  memory-enrichment`, 10 seeds × 5 shapes against a REAL embedding model. Its `novelty-only` arm is exactly
  this question asked cleanly: novelty is what salience reads, and with `MinSimilarity` above 1 no edge is
  written and `SemanticSeedK` is 0, so nothing but novelty→salience differs from the model-free floor.
  <br>**It confirms the direction and the mechanism, at a far larger magnitude than the figures above.**
  `many-candidates` is the WORST of the five shapes at **+0.2622** mean miss delta, and the per-class rows
  name the displacement outright: `topical` miss goes **0.3406 → 0.9106**. The two figures already recorded
  (+0.0169 as a 30-seed paired mean, +0.0808 as one draw) are not comparable to it — both were taken through
  `FakeEmbedder`, whose "novelty" is word overlap — so treat this as the first real-model measurement of the
  cost rather than as evidence it grew.
  <br>**What is still open is unchanged, and it is the RULE.** A sweep that varies enrichment says where the
  cost lands; designing a bounded-admission rule needs a sweep that varies the BOUND, which does not exist.
  Full output: `local/superpowers/records/2026-08-15-enrichment-attribution.txt`.
  <br>**CORRECTED 2026-08-21 — the blocker above is real but was stated WRONGLY, and the wrong statement made
  this look startable.** "A sweep that varies the BOUND does not exist" reads as *nobody has built the
  instrument*, which in this repository is an invitation. It is not the obstacle. A bound on salience's
  ranking contribution ALREADY exists and always has —
  `ReciprocalRankFusionOptions.SalienceWeight`, a continuous knob defaulting to 1 — and sweeping it on the
  model-free corpus is **guaranteed flat for reasons that have nothing to do with the knob**:
  `GraphMemoryEngine.Probe` returns novelty 0 with zero comparables when no vector search runs, so without an
  embedder `StructuralSaliencePolicy` declines on every write; and RRF ranks by COMPETITION (**D82**), so a
  uniformly-tied signal contributes a constant and cannot move the ordering at any weight. The `0` arm and
  the `4` arm are the same engine.
  <br>**So the real blocker is the one `memory-enrichment` already carries: a REAL embedding model.** Novelty
  is what salience reads, novelty needs an embedder, and `FakeEmbedder`'s "similarity" is word overlap — which
  is why Part 69 withdrew the numbers taken through it and why `memory-enrichment` EXITS rather than
  substituting a double. That is an environment dependency, which is what the blocked list is for. The trap
  itself is recorded in `.claude/knowledge/pitfalls.md`, because the flat curve would have read as a clean
  exoneration with every existing control green.

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
