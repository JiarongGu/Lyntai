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

_**v2.5.0 is released (2026-08-08); `CHANGELOG.md`'s `## Unreleased` carries the 3.0 memory work.**
Everything through the generation platform, the package restructure, the provider-lifetime seam, the codex
agent session, app-owned MCP servers, the long-term memory subsystem and the multilingual measurement has
shipped and is archived. **The archive is where closed work lives** — `docs/task-archive.md`, one Part per
task, with why and how; this file does not summarize it._

**Four items are startable today. Everything else is blocked on something this repository does not have.**
That distinction is the point of the list, so it is stated first rather than buried:

| Startable now | Why nobody has |
|---|---|
| **Part 73** — derive or gate the COUNTS in prose | Six incidents in 60 commits, each fixed correctly and none looking like a pattern alone |
| **Part 72** — should `check-links` see the code tiers, and what should it check there? | A scope question about the gate, not a defect: the seven references it missed are already repaired |
| **Part 70** — make per-backend contract coverage structural rather than counted | A scheduling call: it restructures ~200 tests to close a gap with no live instance |
| **Part 69**, first item — should the graph engine seed candidates semantically? | A design question, additive, not gated on the 3.0 window |

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
- **Part 65** — a measurement budget (a drift RATE across models is not an anecdote), and a paired sweep
  before any bounded-admission rule is designed.
- **Part 56 / FSRS-B** — a deployment's own logged reviews. The observable now exists; the data does not,
  and this repository cannot invent it without repeating the mistake D49 refused.

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

## Part 70 — the cross-backend contract guard is blind in one direction (2026-08-14)

_Opened by the 3.0 pre-freeze whole-repo review. **Latent, not live** — a per-backend call census that day
found all 68 `MemoryGraphStoreContract` facts wired on all three backends (68/68/68), so nothing is
currently unexercised. Recorded because the MECHANISM has a hole, and the entry it protects
(`.claude/knowledge/pitfalls.md`, "a cross-backend invariant enforced on ONE backend's test class is not
enforced") is one this repository has already been bitten by._

- [ ] **Make per-backend contract coverage structural rather than counted.** `PostgresStorageTests` asserts
  `Assert.Equal(declared, covered)` where `declared` is reflected over the contract's public statics and
  `covered` is a hand-bumped literal (`68`). That catches a fact wired NOWHERE, and one wired everywhere
  except Postgres — but **not** one wired to Postgres ALONE: the author bumps the literal, it passes, and
  InMemory and Sqlite silently never run it.
  <br>The fix is to drive all three backends from a reflection-fed `[Theory]` over the contract's methods,
  so exhaustiveness holds by construction on every backend and the literal disappears. That keeps
  per-fact test names (the theory argument) while making a missing wiring impossible rather than merely
  counted.
  <br>**Deliberately deferred from the review that found it**: it restructures ~200 tests to close a gap
  with no live instance, days before the 3.0 freeze, and the Postgres leg's single-container sequencing
  needs checking against a theory-per-fact shape first. Not blocked on anything external — this is a
  scheduling call, so it is startable today.

---

## Part 73 — a COUNT in prose is this repository's most-repeated drift, and nothing derives one (2026-08-14)

_Opened by the 2026-08-14 review, from the commit history rather than from the code — the individual
incidents were each fixed correctly and none of them looked like a pattern on its own._

**The measurement.** Six corrections to a counted claim, all within sixty commits, all the same shape —
a number written in prose that nothing computes. **Every hash in this entry is on
`backup/pre-squash-2026-08-14`, not on `master`** (D61 squashed everything after `v2.5.0`), so
`git fetch origin backup/pre-squash-2026-08-14` before trying to `git show` one; the SUBJECTS are quoted
in full precisely so the evidence stands without the hash:

| Commit | The count that was wrong |
|---|---|
| `909a376` | "the list said seven and had eleven; the sub-list said three and had four" |
| `24710f3` | CLAUDE.md "under-reported a BYO-breaking change by two members" |
| `e5807d4` | CLAUDE.md "undercounted its own knowledge tier" |
| `1c55b1d` | "re-measure the four baseline counts CLAUDE.md publishes" |
| `208a7ca` | `dev.mjs`'s own usage list "had drifted to 24 of 30 commands" |
| (this review) | `memory-language` said four languages; `CorpusLanguage` declares five |

**Why it is worth a gate rather than another sweep.** `check-docs` structurally cannot see it: that registry
holds terms a decision RETIRED, and a count going stale retires no vocabulary — the sentence stays
grammatical, plausible and wrong. Every one of the six was caught by a person, and four of them were caught
by a person who happened to be counting something else at the time.

**The repository has already solved this twice, in the same direction both times, which is the argument that
it is solvable here too.** `dev.mjs`'s usage line is DERIVED from the command table (`208a7ca`, after it
drifted), and `verify`'s summary line is derived from its step list precisely so "a gate added without
updating prose still names itself". Neither is gated — both are computed, so they cannot drift.

- [ ] **Decide between DERIVING the counts and GATING them, and do one.** `CLAUDE.md` alone publishes
  roughly fifteen: twelve packages, thirteen `verify` checks, nine registries, eleven fresh-session facts,
  five required `IMemoryGraphStore` members, eleven migrations, four test/e2e baselines, `D1–D60`, five local
  skills, six local knowledge documents, five language arms, six extension points, four memory domains.
  At least eight are computable from the tree today.
  <br>**Deriving** is the stronger fix and the harder sell: `CLAUDE.md` is hand-written prose that a session
  reads first, and a generated block inside it is a new kind of thing this repository does not have.
  <br>**Gating** fits the shape the repo already trusts — a curated registry, one entry per count, mapping a
  phrase to a function that computes the number, so a hit is a defect BY CONSTRUCTION exactly as
  `retiredTerms` intends. Same honest limit, too, and it should be stated in the header rather than
  discovered: it only ever covers counts somebody registered.
  <br>**Cheapest useful subset if the whole thing is too much**: the four test/e2e baselines, which `verify`
  already computes and prints on every run, and which `CLAUDE.md` itself instructs the reader to compare
  against — a stale baseline there actively teaches the next session to stop comparing.
  <br>**A caveat measured while writing this entry, because it is the thing that would sink a naive
  implementation.** Every count `CLAUDE.md` publishes was re-checked here and **all of them are correct
  today** — so this is a gate against RECURRENCE, not a repair. And getting each one right is fiddlier than
  it looks: a first probe counted `Migrations/M*.cs` and got **12**, because `MigrationRunnerService.cs`
  matches that glob and is not a migration; "five local skills" and "six local knowledge documents" are
  correct but the DIRECTORIES hold 10 and 9, since both tiers mix local files with synced canonical ones.
  A counter that is subtly wrong is worse than none — it fails a clean tree and the fix is to edit the
  counter, which trains exactly the "ignore this gate" reflex `check-warnings`' own ENOBUFS incident
  records. **Every entry needs a test pinning the counter against the tree as it stands.**
  <br>Not blocked on anything; this is a design call about how much of an auto-loaded file may be generated.

---

## Part 72 — `check-links` scans markdown only, and the defect it was built for was alive in the code tiers (2026-08-14)

_Opened by the 2026-08-14 whole-repo review. **The instances are already fixed** — seven dead references
repaired in `src/` and `tests/`, and the gate's own `.csproj`-blind `PATH_PATTERN` corrected and pinned. What
is open is the SCOPE question, which the owner asked to decide separately rather than have widened by the
same pass that found it._

- [ ] **Decide whether `check-links` should read the code tiers, and what it may check there.** It scans
  `.md` under the `check-docs` scope predicates; `PATH_PATTERN` itself already anchors on `src|tests|devtools|
  bench|samples`, so the pattern was always able to see them and only the file filter stops it.
  <br>**What the miss cost, measured**: on the day the gate went green, seven references to archived documents
  were alive in `src/` and `tests/` — two inside XML documentation that ships to consumers — plus a second
  archived spec (`2026-07-19-agent-session-design.md`) nobody had swept at all, and two paths that never
  existed in the repository. `bench/Lyntai.Benchmarks/MemoryLanguageSweep.cs` separately cited a test name
  that had been renamed away, twice.
  <br>**The argument FOR**: the gate's own header excludes `src/` because "the compiler already gates their
  crefs", and that covers `<see cref>` and nothing else — a path or a type in a `<c>` tag is prose the
  compiler never resolves, and this repository's XML docs put load-bearing references there constantly.
  <br>**The argument AGAINST, and why this is a real question rather than a formality**: a code comment is
  the one place a reference to something that no longer exists is often CORRECT — `pitfalls.md` already
  records that an existence-check over prose produced ~45 hits and zero defects for exactly that reason, and
  a `//` comment describing a defect as it was is the same shape. Widening the file filter without deciding
  what counts would import that false-positive problem into `verify`.
  <br>**A narrower option worth costing first**: check only paths under `docs/` and `local/` (documents move;
  source files get renamed for legitimate reasons and `pitfalls.md` already forbids gating line numbers), and
  only in `///` XML docs rather than `//` comments — the tier that actually ships. That may be most of the
  value for none of the noise.
  <br>**MEASURED once the repairs landed, which is what makes this decidable rather than arguable.** Re-running
  the pattern over all 677 code files (excluding `__tests__`, whose fixture paths are synthetic by design)
  returns **seven hits, and all seven are correct prose**: five name `docs/灵台.md` / `docs/plain.md`, guard <!-- link-ok: the gate's own fixture names, quoted as data -->
  FIXTURE names that have never existed in this repository, and two are `check-links.mjs`'s own incident
  narrative naming the dead path it was built for and the truncated `.cs` example from its `PATH_PATTERN`
  fix. So a naive widening would ship a gate whose entire output is false positives — exactly the
  ~45-hits-zero-defects shape `pitfalls.md` records for existence checks over prose. **The `link-ok` escape
  already handles all seven**, so the real question is only whether five annotations on guard-script comments
  are a fair price for covering the tier where seven genuine dead references actually lived.
  <br>Not blocked on anything external; this is a scope call.

---


## Part 69 — the embedder costs recall quality on this corpus, and nothing yet says whether that generalizes (2026-08-13)

_Opened by the salience study of archive Part 53, which found it while controlling for something else.
**Not a regression and not new** — it has presumably always been true; what is new is that anyone measured
it separately from the policies that ride on top of it._

_**The MECHANISM in the item below is wrong, found 2026-08-13 while building the instrument to test it.**
It explains the embedder's cost as "semantic neighbours and lexical hits compete for the same bounded
slots". **There are no semantic neighbours at recall.** `GraphMemoryEngine.GatherAsync` seeds candidates
only from `IMemoryGraphStore.SeedAsync` — a LEXICAL query — and then walks edges. The vector store is
consulted at WRITE time (novelty for salience, and similarity linking) and never at query time. The graph
engine has **no semantic retrieval path at all**._

_**Measured with a REAL embedding model** (`nomic-embed-text`, `LlmSemanticRecallLiveTests`) over new
`CorpusLexicon.ParaphrasePairs` — statement/cue pairs that mean the same and share NO index term, asserted
disjoint in all five languages. A real model recovers **0 of 3**, identical to no embedder. The cost is
real; its cause is write-time linking and salience changing what traversal reaches, not slot competition._

_**A second instrument defect, and it invalidated the original arm.** That measurement used `FakeEmbedder`
— a feature-hashed bag of WORDS, in which "semantic similarity" IS word overlap. A double that cannot
represent meaning cannot show meaning-based retrieval helping, so the embedder was only ever measurable
costing slots it could never be seen earning back._

- [ ] **Whether the graph engine SHOULD have a query-time semantic seed is now the real question**, and it
  is a design decision rather than a measurement. `ISemanticMemory` is a separate shipped seam, so a
  consumer wanting meaning-based retrieval has one — the question is whether the GRAPH engine should also
  embed the query and union vector hits into `GatherAsync`'s seeds. The corpus axis to judge it now exists
  (`ParaphrasePairs`), and `LlmSemanticRecallLiveTests` pins the current answer at 0/3 so the assertion
  flips the moment someone adds it. Additive, and NOT gated on the 3.0 window.
- [ ] **Registering an `IEmbedder` + `IVectorStore` costs recall quality on this corpus, and WHY is now the
  open question.** The effect is much larger than anything salience produces in either direction, and it is
  reproducible; the explanation this item shipped with is not. Two candidate mechanisms remain, both
  write-time, because recall has no semantic path to blame (the part header above):
  **(a)** similarity linking adds edges that change what traversal reaches, and
  **(b)** novelty feeds salience, which changes what is admitted and how it decays.
  Nothing separates them yet — the write-time seams would have to be varied independently.
  <br>**The published numbers were measured through an instrument that could not answer the question**, so
  they are not restated here: that arm used `FakeEmbedder`, a feature-hashed bag of WORDS in which "semantic
  similarity" IS word overlap. A double that cannot represent meaning can only ever be seen paying a cost it
  could never be seen earning back. Re-measure with a real model before quoting a figure.
  <br>**Why this is not simply a defect to fix.** This corpus defines relevance LEXICALLY — ground truth is
  "the entry whose id the query names" — so a semantic neighbour is *by construction* wrong here. The honest
  statement is that the instrument cannot say which resembles a real consumer, not that enrichment is
  harmful. `CorpusLexicon.ParaphrasePairs` now supplies the missing axis (queries answerable only
  semantically, asserted term-disjoint in all five languages) and is what a re-measurement should use.
  <br>Guarded meanwhile by `MemorySalienceInversionTests.The_embedder_not_salience_is_what_moves_recall_quality_on_this_corpus`,
  which asserts salience stays the SMALLER effect — so if that ordering changes, the attribution is
  re-measured rather than quietly re-worded.

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
