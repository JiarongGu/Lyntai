# Lyntai (灵台) — Active Task Backlog

> **This file holds OPEN tasks only** — the live backlog. Completed work is not left here: once a task is
> fully done (committed + verified), its entry is **moved to [`docs/task-archive.md`](docs/task-archive.md)**
> (the completed-task record) rather than checked off in place. See the lifecycle rule
> `.claude/rules/task-lifecycle.md`. `CHANGELOG.md` remains the release-facing log; the archive is the
> per-task record (why/how). The design contract is `docs/2026-07-17-lyntai-design.md`; the forward
> sequence is `docs/ROADMAP.md`.

**Goal:** a NuGet-packable, DI-first .NET 10 library — an LLM provider abstraction (routing + fallback
across CLI / HTTP / lambda-bridged providers), pluggable storage (SQLite / InMemory / Postgres), and the
LLM-ops layer (prompt registry, scoring, traces, memory). `AddLyntai(...)` and go.

---

<!-- open-items:begin — GENERATED. Edit the per-item `item:` markers, never this table. -->

## Open items — 16 across 7 Parts: 8 startable, 4 blocked, 2 watch, 2 decision-only

_Generated from the per-item `<!-- item: … -->` markers by `node devtools/dev.mjs check-backlog --write`._
_Edit a marker, never this table — `verify` fails the moment the two disagree._

| line | Part | item | state | waiting on |
| ---: | ---: | --- | --- | --- |
| 117 | 33 | GEN-VERIFY-SD — run one real `sd-cli` render and confirm the argv and the m… | startable |  |
| 134 | 33 | GEN-VERIFY-COMFY — measure ComfyUI's surface against a live local server | startable |  |
| 167 | 33 | GEN-VERIFY-FAL — one submit → poll → fetch against fal.ai with a real key | blocked · env | a fal.ai account and key — nobody here has one, and no download substitutes… |
| 220 | 33 | GEN6 — streaming audio (TTS) | decision-only | a ruling on WHICH backend measures the chunk shape first — a hosted vendor … |
| 239 | 33 | GEN7 — pipelines (3d → image → video) | blocked · tree | a 3D generation backend — the pipeline's first stage has none, and the 3d-t… |
| 296 | 103 | What `AddGenerationProvider` should be called, or whether it should exist | decision-only · decision | a ruling on whether a media registration is named for the ROUTER it wires o… |
| 319 | 102 | REL1 — four surface changes since `v3.1.0` that NO changelog entry announces | startable |  |
| 335 | 102 | REL2 — `### Breaking` carries nine ADDITIVE entries, and one entry describe… | startable |  |
| 344 | 102 | REL3 — the cross-encoder is the one backend the D137→D138 suffix sweep miss… | startable |  |
| 351 | 102 | REL5 — `HttpDialect` is a closed enum plus an if-chain where a DI seam belo… | startable |  |
| 360 | 102 | REL6 — the review's Tier-B list: ~30 internal how-to errors, none consumer-… | startable |  |
| 389 | 41 | CLI12 — measure codex's tool-step items and confirm (or correct) the inferr… | startable |  |
| 449 | 56 | FSRS-B — parameter FITTING, not published defaults | blocked · data | a deployment's own logged reviews; this repository cannot invent them witho… |
| 504 | 75 | Decide what an aggregator's in-band `code` means | blocked · env+data | two or three real aggregators to measure an in-band code against |
| 527 | 99 | `verify`'s test step intermittently fails EXACTLY 9 tests, and once aborted… | watch · data | the same nine tests to recur — the fix is unconfirmed as the cure, and a gr… |
| 584 | 99 | `SqliteCuratedMemoryStoreTests.Dedup_race` disposes a connection another ca… | watch · data | a recurrence with a full stack — the three hypotheses a reading can reach a… |

<!-- open-items:end -->

---

## Active backlog

_**The archive is where closed work lives** — `docs/task-archive.md`, one Part per task, with why and how;
this file does not summarize it. `CHANGELOG.md` is the release-facing log, and everything before 3.0 is
history rather than context (`repo-mechanics.md`)._

**What is open is the TABLE at the head of this file, and it is GENERATED.** Every checkbox carries an
`<!-- item: state=… kind=… needs="…" -->` marker; `node devtools/dev.mjs check-backlog --write` rebuilds the
table from those markers and `verify` fails while the two disagree, so the roster and the items can no
longer drift apart. Edit the marker, never the table — the COUNT lives there and nowhere else, so this
sentence names no number — **nor a Part**, the same rot one step out: it named Part 102 as "most of what is
startable" until Part 103 opened bigger. **The accumulation that hid these is GATED, not watched for**:
`check-backlog` fails a `## Part` holding no open checkbox.

**Re-checking an `env` item asks whether the artifact is OBTAINABLE, not whether it is installed** —
`task-lifecycle.md` §A blocked item, which two re-checks here got wrong before a reader did.
**An `env` blocker also EXPIRES
SILENTLY** — Part 65 read "this machine holds exactly one chat model" for two weeks while three sat on
disk, every one already used by other measurements here. Nothing fails when an environment GROWS, so
nothing announces it; **re-check every `env` item before concluding there is no work.**
**A REFUTATION is the fifth route an item arrives by, and the most reliable**: disproving the obvious answer
names the next candidate, which is how Part 217 arrived narrowed to one signal of three. The others are a
ruling, a captured failure, and a design REVIEW — the only one schedulable on purpose, and the only one
that has twice filled a whole Part in one pass (Part 179, then Part 221) where every other route yields
ones and twos. `decision-only` was invented for GEN6 and earns its keep when a SWEEP declines to answer a
question rather than settling it by momentum, which is how `AddGenerationProvider` arrived. That sentence
is hand-written on purpose — the banner it replaces advertised finished work **four** times.

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
`local/superpowers/plans/2026-08-04-generation-platform-plan.md` +
`local/superpowers/plans/2026-08-04-restructure-2.0.1-plan.md`. What remains are
that plan's Plans 3–7, each a separate pass because each needs its own measurement.
<br>**Nothing below EXECUTES from that plan any more, which is why it left `docs/` (D149).** Its Plan 6
still names a streaming interface **D127** deleted and its Plan 7 predates the 2026-08-30 3D survey and
GEN7a shipping, so the three item bodies below are the current framing and the plan is the record of how
the core was built._

_GEN3 (local `sd-cli`), GEN4 (durable renders + the fal.ai queue backend), GEN6's tool/MCP bridge half and
GEN5 (governance + telemetry parity) all landed 2026-08-04 — see `docs/task-archive.md` Part 33._

_**THREE surfaces are unmeasured, not two, and only ONE of them needs a vendor.** This paragraph named the
`sd-cli` argv/clamp (ported-not-measured) and fal's wire format (documented-not-measured) and omitted
**ComfyUI**, whose own class header says no instance was available to measure it — a self-hosted backend
needing no account, declaring both Image and Video. Sorted by what each COSTS: ComfyUI is a local server,
`sd-cli` is two downloads, fal is an account. Naming only the first and the last is how "we are waiting on
fal" came to stand in for "the platform is unverified". (`sd-cli`'s binary-directory working dir was a
fourth such surface — a consuming app measured it 2026-08-04 and it is now confirmed.)_

- [ ] **GEN-VERIFY-SD — run one real `sd-cli` render and confirm the argv and the multiple-of-64 clamp.** <!-- item: state=startable -->
  Then delete that backend's remaining "unverified" notes from the XML docs, or fix the mapping and keep
  them. Two downloads, both named: the CPU build is
  `sd-master-<rev>-bin-win-cpu-x64.zip` from `leejet/stable-diffusion.cpp`'s releases (**17.1 MB**, verified
  2026-09-16), and any SD 1.5 checkpoint (~1.7 GB) satisfies the model. Point
  `LocalDiffusionOptions.BinaryPath` at the extracted `sd-cli.exe` — there is no PATH probe and no prefix
  match, deliberately, because the same zip ships `sd-server.exe` and a loose `sd`-prefix match would
  select the SERVER and present as a HANG.

  _**RE-FILED 2026-09-16 from `blocked · env` to STARTABLE, and the item was never blocked.**
  `task-lifecycle.md` says it in as many words — "a DOWNLOAD is a step, not a blocker, unless you cannot
  name the file" — and both files are nameable, public and small. **This is the SECOND time that exact
  drift has been caught by a reader rather than a re-check**; the rule's own text records the first, where
  "one item sat `startable` needing a model nobody had named, while its neighbour sat `blocked · env` for a
  download". A re-check that confirms the artifact is still absent from the machine is answering the wrong
  question: the test is whether someone could BEGIN today, not whether the work is already done._

- [ ] **GEN-VERIFY-COMFY — measure ComfyUI's surface against a live local server.** Its class header says <!-- item: state=startable -->
  *"No ComfyUI instance was available to measure when this was written"*, so the endpoint paths and the
  response field names are documented-surface. **Nothing about this needs an account**: ComfyUI is
  self-hosted, its probe is free and exact (system-stats / object-info), and a wrong path fails as a 404
  rather than as a bad render.

  **Two stages, and the first is cheap.** (1) **The HTTP surface** — submit, poll history, fetch — is
  verified by ANY workflow that produces ANY output, so a 512×512 SD 1.5 image settles every path and all
  four response field names in seconds. (2) **The VIDEO kind** then needs a video workflow, and that is the
  only part a model choice touches: it exercises `ProviderKinds.Video` routing and the view-URI rule the
  header states (*"a local video is easily 100 MB, and downloading it uninvited would be the platform
  spending the caller's memory"*) — a rule no image can test, because no image is big enough to make
  returning bytes obviously wrong.

  _**Two open-weight video candidates, checked 2026-09-16, and the LICENCE is the axis that separates
  them** — which is the plan's own Decision 3 test ("open weights … Apache-2.0 and ComfyUI-native" against
  "closed-weight, hosted-only and moderated"). **Wan2.2-TI2V-5B is Apache-2.0**, ComfyUI-native, and
  unifies text-to-video and image-to-video in one model, so it exercises both request shapes the router
  can send; its card states 720P/24fps and **24 GB** VRAM, and reaching a 12 GB card is community GGUF
  quantization at 480p — real, widely reported, and **not measured by us**, which is the caveat this whole
  item exists to stop shipping unstated. **LTX-Video** is far faster and lighter but ships a CUSTOM
  "LTX-Video Open-Weights License", so it is a licence someone must READ rather than a permissive default.
  <br>**Neither is a library recommendation and neither may become a default** — which model serves a
  deployment is the deployment's to answer (`model-decoupling.md`, `generic-library.md`). This names what
  to install to RUN the verification, nothing more._

  _**Why this was not filed until 2026-09-16**, which is the finding rather than the item: GEN-VERIFY was
  written fal-first and split fal-first, so the ONE unverified backend that needs no vendor at all stayed
  invisible inside it through two passes — including the pass that split it. **Three surfaces are
  documented-not-measured, not two**, and they have completely different costs: `sd-cli` needs two
  downloads, ComfyUI needs a local server, and only fal needs an account. Sorting them by what they COST
  is what the single item prevented._

- [ ] **GEN-VERIFY-FAL — one submit → poll → fetch against fal.ai with a real key**, checking the status <!-- item: state=blocked kind=env needs="a fal.ai account and key — nobody here has one, and no download substitutes for it" -->
  vocabulary, the result field names and what `cost` reports. Then delete that backend's "unverified" notes
  or fix the mapping.

  _**This is fal's OWN wire format and nothing else.** It is not the generation platform's verification
  story and must not be treated as one — the two sibling items above cover the other two backends without
  a vendor, and the video DELIVERY path is reachable through ComfyUI. Filed narrowly on purpose, because
  the old bundled item let "we are waiting on fal" stand in for "the platform is unverified"._

  _**This half IS blocked, and it is the rule's own example of one**: a vendor key or an account, which no
  download can supply. Split from the `sd-cli` half on 2026-09-16 because they were always independent —
  the old single item said so itself — and bundling them let the real blocker hide a startable one behind
  it for weeks._

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

- [ ] **GEN6 — streaming audio (TTS).** A streaming TTS backend against a real vendor. **The scope shrank on <!-- item: state=decision-only needs="a ruling on WHICH backend measures the chunk shape first — a hosted vendor (then an account) or a local engine (then only a download)" -->
  2026-08-16** (`docs/DECISIONS.md` **D67**): the PLATFORM half is done and shipped in 3.0 —
  `IMediaRouter.StreamAsync` selects, falls over, governs and throttles a `Stream`-capable backend, and
  the router guarantees exactly one terminal chunk, so a backend no longer has to be careful about fallback or
  closing its own stream. What is left is a real backend and the thing only it can settle: whether
  data-then-terminal is the decomposition a real TTS wire format wants. So this is no longer "the seam is
  unexercised" — the handling is measured by `GenerationRouterStreamTests`; it is "the chunk SHAPE is still
  inferred". **TTS before music** (owner). It needs a MEASURED wire format, never an inferred one — the
  GEN-VERIFY lesson.

  _**RE-FILED 2026-09-16 from `blocked · decision,env` to `decision-only`: the `env` half is CONTINGENT on
  the ruling, not independent of it.** The open question is which backend ships first, and the two branches
  have different costs. A HOSTED vendor needs an account nobody here has — a real blocker. A LOCAL engine
  needs a download: `piper_tts` ships a `win_amd64` wheel (v1.8.0, verified 2026-09-16), streams PCM, and
  would be spawned through `IProcessRunner` exactly as `LocalDiffusionProvider` drives `sd-cli`. So
  "waits on a key" was only true of one branch, and stating it unconditionally made a RULING look like an
  environment wall. **Naming a concrete local candidate is what turns this into a decision someone can
  actually take** — it is not a recommendation, and the owner may well want a hosted format measured
  instead._
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
  `MediaArtifact.ToInput(role)`. A stage is a stage, so **adding a 3D stage later needs no change to the
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

## Part 103 — the namespace restructure, and D153's unfinished wiring (2026-09-17)

_Opened by **D153**, which gave every call family the same SHAPE and left them in three different
namespaces — because that is where each already lived. **The rule that settled it is `docs/DECISIONS.md`
D154**, which is the tracked record; the design that sequenced the work was
`local/superpowers/specs/2026-09-17-namespace-restructure-design.md` and is finished._

_**The RESTRUCTURE is finished, and so is D153's wiring** — Parts 246–251, under **D154** (the namespaces
and names) and **D155** (the router factory that finally gave vector, score and app-defined kinds their
cooldown and admission). What is left below is neither: one question the sweeps deliberately declined to
answer rather than settle by momentum._

_**Two things that carry forward, because a closed Part is not where anyone looks for them.** The `NS-n`
ids here and the design's step numbers **diverged at 5**, so one whole step existed in no tracked file
until the day it closed — write one set of numbers or name the file every time. And only three of the six
steps were BREAKING in the way the Part originally claimed: the last one moved four INTERNAL types and the
API baseline did not move at all._

- [ ] **What `AddGenerationProvider` should be called, or whether it should exist.** <!-- item: state=decision-only kind=decision needs="a ruling on whether a media registration is named for the ROUTER it wires or folds into AddProvider(factory, declares)" -->
  It registers any factory into the media candidate pool AND ensures the media router is wired — two jobs,
  one name, and the name says *"a provider that produces generations"*. That is the shape **D152** retired
  `AddEmbeddingProvider` for, so `AddMediaProvider` would ship the same defect under a new spelling. <!-- drift-ok: the item cites the retired registration it must not repeat -->
  <br>**Left undone by NS-6 deliberately.** Every other registration in that file now reads `Media*`
  (`AddMediaUsageBudget`, `AddMediaRateLimit`, `ConfigureMediaRouting`, `UseDefaultMediaCandidates`,
  `MediaOptions`), so this one name is visibly the odd one out — which is the right way for an undecided
  question to sit, rather than being settled by a sweep that never asked it.
  <br>_The two candidate answers: name it for what it DOES to the router (it is the only registration that
  calls `EnsureRouter`), or delete it and let `AddProvider(factory, declares)` plus explicit router wiring
  do the job. The second is smaller surface and more typing for the consumer._


## Part 102 — the pre-release review's open calls, one decision each (2026-09-17)

_Opened by `docs/task-archive.md` **Part 245**, a five-dimension review run before cutting the major.
Everything that was a DEFECT was fixed in that pass; what is left is below, and each one is a CHOICE
rather than a repair. **Three of them close with this release window** — REL3, REL4 and REL5 change public
names or shapes, so they ship in the major or wait for the next one._

_**Read the evidence before deciding, and re-verify it.** These came out of a review, not out of a gate;
the line numbers and counts were true on 2026-09-17 and rot the way any measurement does._

- [ ] **REL1 — four surface changes since `v3.1.0` that NO changelog entry announces.** <!-- item: state=startable -->
  The changelog IS the migration path for this major (**D149** — there is deliberately no separate guide),
  so an unannounced break is a consumer hitting it with nothing to read.
  1. **`MemoryReview.Grade` → `ReviewGrade`** on `MemoryReview`, `MemoryReviewWrite` and `MemoryReviewRow`
     (commit `94f89b2c`, marked `!`). `MemoryReviewWrite` is constructed by every BYO `IMemoryGraphStore`.
  2. **`ProviderProbeResult`'s positional parameter ORDER changed** — `(Available, Version, Model, Detail)`
     became `(Available, Detail, Version, Model)`. **The sharp one**: D127's entry mentions the two domains
     had different field orders but never says the survivor took generation's, so a consumer who follows
     its stated migration recompiles CLEAN and files their version string into `Detail`. A silent data
     defect on upgrade, not a compile break.
  3. **`GraphNode` gained a trailing `Matched` member** — the Breaking section lists this exact break class
     for four sibling types and omits the one a BYO graph store RETURNS.
  4. **`Lyntai.Llm.Routing.FallbackAction` → `Lyntai.Inference.FallbackAction`.** D140's entry names only <!-- drift-ok: the item is ABOUT the move, so it must name where the type came from · tautology-ok: the two sides are the two namespaces -->
     the generation-side type as moving. (The source namespace has since been retired outright by D154
     NS-4, which does not change what D140's entry failed to say.)

- [ ] **REL2 — `### Breaking` carries nine ADDITIVE entries, and one entry describes a dead migration.** <!-- item: state=startable -->
  `RunPipelineAsync`, `WalkAsync`, `WriteBackAsync`, `LinkManyAsync`, `ExpansionRetrievabilityFloor`,
  `SalienceContext.SimilarCount`, `MemoryItem.Metadata`, `MemoryVerificationCandidate.Relevance` and
  `IMemorySeedSource` all sit under a `### Breaking` heading. For a release whose changelog is the
  migration path, a consumer reading that heading gets nine non-breaks mixed into the list. Separately,
  **D145's entry instructs a migration D146 deleted fifteen lines above it** — D123 and D128 both carry a
  supersession note and D145 does not, so it reads as live guidance. The Unreleased section also has six
  `### Added` headings and three `### Breaking`; worth collapsing before the cut.

- [ ] **REL3 — the cross-encoder is the one backend the D137→D138 suffix sweep missed.** <!-- item: state=startable -->
  `AddOnnxCrossEncoder` / `OnnxCrossEncoder` / `OnnxCrossEncoderOptions`, beside `AddOnnxProvider` /
  `OnnxProvider` / `OnnxProviderOptions` in the same file and the same baseline. It is the ONLY shipped
  `IModelProvider` whose type name lacks `Provider` and the only named-backend registration without the
  suffix. **D137** restored the suffix "on all seventeen" and **D138** renamed the sibling in this very
  file; both passed over it. A rename now is mechanical; after the major it costs another one.

- [ ] **REL5 — `HttpDialect` is a closed enum plus an if-chain where a DI seam belongs.** <!-- item: state=startable -->
  `src/Lyntai.Providers.Basic/Http/HttpDialect.cs:11`, with the conditionals at `Http/ProviderDetect.cs`,
  `Http/HttpEndpoint.cs` and `Http/HttpModelProvider.cs`. `dotnet-package-layout.md` §Variation points:
  *"If adding a backend requires editing existing code, the seam is in the wrong place."* Adding
  Ollama-native required edits at five sites, so the test is met by history rather than hypothesis — and
  one directory up, `ICliProviderDialect` is the same idea done as an interface. The enum is PUBLIC, so
  shipping again freezes it for the major. The counter-argument worth weighing: the family's membership
  rule is "OpenAI-compatible", so a foreign wire schema arguably belongs in its own provider class.

- [ ] **REL6 — the review's Tier-B list: ~30 internal how-to errors, none consumer-facing.** <!-- item: state=startable -->
  Worst first, and **each needs re-verifying before acting** — this list is a review's output, not a
  gate's. `extending-lyntai.md`: a dialect sketch with the wrong `BuildCompletionArgs` arity, a
  `SupportsToolCalls` member that does not exist on `IModelProvider` (writing it compiles and is silently
  ignored), a native-provider sketch omitting `Capabilities` — the one member with no default — and
  "THREE of the thirteen carry a default body" where the thirteen ARE the required ones of sixteen.
  `pitfalls.md`: an entry whose premise **D108** removed (`IMemoryVerificationPolicy` does receive
  `Content`), and one instructing a bump to a literal that no longer exists. `memory.md`: a "turn the judge
  on" recipe that is a no-op at shipped defaults, and an unreachable salience column. `GATES.md`,
  `AOT.md`, `llm-and-router.md`, `storage.md`: assorted stale names and counts.
  <br>_Why no gate sees these: `check-links`' member tier asks whether a name exists ANYWHERE in the tree,
  not whether it is on the named type; `check-tautology` and `check-docs` do not read
  `.claude/knowledge/**`; and 11 of the 58 doc samples carry `compile-skip`._

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

- [ ] **CLI12 — measure codex's tool-step items and confirm (or correct) the inferred mapping.** <!-- item: state=startable -->
  `src/Lyntai.Providers.Basic/CodexCli/CodexAgentReader.cs`. The capture behind this backend (codex-cli 0.146.0,
  2026-08-04) ran a trivial `--oss` turn with **no tools**, so the entire tool-step half is inferred and
  marked as such in the XML docs.

  **First step: `npm i -g @openai/codex`.** Verified on the registry 2026-09-16 — `0.154.0`, with a
  `win32-x64` platform binary among its optional dependencies. The 2026-08-04 capture used `--oss`, and
  this machine holds 16 GGUF models, so the turn itself needs **no vendor account** — which also settles
  the token question the owner already answered yes to.

  _**RE-FILED 2026-09-16 from `blocked · env`, and the re-checks that kept it blocked were asking the wrong
  question.** Twice — 2026-09-15 and again this morning — `where codex` returned nothing and the item was
  left blocked on the strength of it. That confirms the binary is ABSENT; it says nothing about whether it
  is OBTAINABLE, which is the test `task-lifecycle.md` actually sets ("could someone begin this today?").
  A named package on a public registry is a step. **The shape to carry: for an `env` blocker, "still not
  installed" is not a re-check — it is the same observation that filed the item.** Re-check the artifact's
  AVAILABILITY, not the machine's inventory._

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

## Retired — five Parts that outlived their open work (2026-09-16)

_Parts 109, 116, 128 and 178 held **295 lines and zero open checkboxes** between them: every line was a
"CLOSED as archive Part N" note, which is what `.claude/rules/task-lifecycle.md` forbids in as many words.
They are `docs/task-archive.md` **Parts 233–236** respectively, one per thread, each naming where its
halves landed and what it leaves standing. **Part 233, which filed the retirement, went with them** —
`docs/task-archive.md` **Part 238**, both items._

_**The rule is now a gate**: `check-backlog` fails a `## Part` heading holding no open checkbox, so this
cannot regrow unnoticed a third time. A heading that is not `## Part <n>` — this one — is invisible to it,
which is what lets a retirement leave a pointer behind without tripping the rule it just satisfied._

_**They are numbered 233–236 in the ARCHIVE and were 109/116/128/178 HERE** — the two files number
independently, so a Part keeps its backlog number until it closes and the archive allocates in LANDING
order. Name the file in every citation._

_**BL1 was filed saying all four numbers COLLIDE, and only one does.** `docs/task-archive.md` Part 178 is
an unrelated task (the cold-start measurement); 109, 116 and 128 are simply gaps there. The item's
conclusion survives its wrong premise — they could not keep their numbers anyway, because the archive
allocates in landing order rather than reserving a backlog number — but the reason is the ORDER, not a
clash. Recorded because the wrong version was repeated from the item into this note before anyone checked
it, which is the whole failure mode `check-links` cannot see: a `Part N` claim about a Part that does not
exist reads exactly like one about a Part that does._

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
  `ITextClient` front door / a BYO seam, never app-specific code. Update the `ApiSurface` baselines
  deliberately on any public-surface change.
- **Deviate from a task's suggested steps when the code disagrees** — the spec's *contract* (interfaces,
  semantics) is authoritative; a task's step list is a suggestion. Record real deviations in the commit
  message.
- **When a task completes, archive it** (`.claude/rules/task-lifecycle.md`): move its entry (with the
  completion date + a one-line **Outcome**) into `docs/task-archive.md`, and delete it from here.