# CLAUDE.md — Lyntai (灵台)

> Auto-loaded every session. Keep short — details live in `docs/` and `.claude/rules/`.

## What this is

**Lyntai** (灵台, "the numinous platform" — the seat of the mind) is a reusable **.NET 10 library**: a set
of NuGet-packable projects, **not an app** — no server, no host, no UI. It provides (1) an **LLM provider
abstraction** with routing + **fallback** across CLI / HTTP / lambda-**bridged** providers (**D147** —
`AddBridgeProvider` is a delegate, so bridging costs the library no dependency),
(2) **pluggable storage** behind per-domain interfaces, and (3) the LLM-ops layer — prompt registry,
scoring/eval, run traces, long-term memory — all wired by `AddLyntai(...)`.

## Current state

**Released: v3.1.0 (2026-08-23).** Eleven packages; public API frozen under SemVer 2.0 since 1.0, with no
carve-out (**D70**). The reasoning is `docs/DECISIONS.md`, **D1–D151** — read its generated index table
rather than any list of decisions kept here. **Everything before 3.0 is HISTORY, not context**:
`.claude/rules/repo-mechanics.md` says what that forbids.

**The baseline a green run should match:** `3820 passed / 3853 total, 33 skipped` (the skips are
live-backend only), e2e 3/3, guard-script tests 860/860, doc samples 58/58. **The xUnit trio is held by no
gate** — re-measure those three by hand after `verify` rather than extrapolating them from a diff, and read
a skip count in the low HUNDREDS as "Docker is down and the whole Postgres leg went silently unexercised".
**MEASURED with Docker up, re-attested 2026-09-17 at `355221d8`** — read off that run's own output, never
derived from a diff, which is the discipline the sentence above states and the one an updated number most
easily breaks. **UNCHANGED across a session that deleted a public interface** (**D151**): 78 files, 19 of
them in `src/` and 33 test files, and all three figures held. **No movement is a result, and it is the one
this line is checked against** — a diff that touches `src/` heavily and leaves the trio alone is the normal
case, not a suspicious one, because what moves these numbers is a test being ADDED or REMOVED and a
refactor does neither.
**Two review passes moved this line 3750 → 3780 → 3817 in one day** and the skip count has never changed —
that invariance is the half worth checking, since it is what a Docker-down run would break first.
**The Docker-down run is not hypothetical: the FIRST attempt that day read `3616 / 3853 / 237`** and was
green on all 24 gates. It reconciled to the real numbers by arithmetic — +204 skipped is the Postgres leg
— and the attestation was still withheld until Docker came up, because this line takes a MEASUREMENT and
an arithmetic that happens to work is the most tempting way to break that rule. Every skip is live-backend gated (a live model, embedder, reranker, Ollama, MCP or CLI), so
nothing is skipping for another reason. **The gated-on-a-model-DIRECTORY suites are now four**:
`OnnxProviderLiveTests` is FIVE (**D124**), `OnnxCrossEncoderLiveTests` four, beside
`WordPieceTokenizerLiveTests` (**D122**) and `Model2VecProviderLiveTests` (**D121**).
**Run with `LYNTAI_ONNX_MODEL_DIR`, `LYNTAI_STATIC_MODEL_DIR` and `LYNTAI_ONNX_RERANK_MODEL_DIR` set and
the count reads 22 skipped** — measured at `c1a62871`, where the passing total was 3751; the
SKIP count is the durable half and the total moves with the tree. A
LOWER skip count is the live suites running, which is the one direction that needs no investigation.
**Re-attest the COMMIT alongside the figures whenever they move**: a dated claim left standing over a
changed number cannot be told from an extrapolated one, and that is how it read to a cold reader.
**The TOTAL can now move DOWNWARD, which the previous wording did not anticipate.** `ApiSurfaceTests` and
`ApiSurfaceRendererTests` are theories over the packable roster, so **retiring a package costs two tests**:
**D122** and **D123** took thirteen packages to eleven (−4) while the tokenizer added 18, and 3688 − 4 + 18
= 3702. Everything else reads back identically off each run's own output — the skip roster, e2e 3/3, doc
samples 58/58 — and the guard count is derived from the tree by `check-counts`, so it cannot go stale
unseen. **Movement that reconciles against a NAMED cause on both sides is the only kind needing no
investigation; any other is a finding**, and "+n new tests" is no longer the whole of that rule.
**The Postgres leg is 204 tests**, re-measured 2026-09-16 against the attestation above: Docker down reads
`3616 passed / 237 skipped`, and 237 − 33 = 204 = 3820 − 3616, the same quantity from both sides.
**The LEG GROWS WITH THE TREE, which the previous wording did not anticipate** — it was 195 on an earlier
tree of 3,644 total (`3427 / 217` down against `3622 / 22` up). So what carries forward is the DERIVATION,
never the number: re-derive it from the run in front of you, exactly as the baseline line above is derived.
A skip count in the low hundreds means the leg did not run, whatever it currently counts.
Everything else on that line is gated. `docs/GATES.md` is why each gate exists, what it measured and which
numbers it holds.

**Long-term memory is the newest subsystem and the one most often reasoned about wrongly**, because it is
not the three older surfaces (`IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore`) that co-exist with
it. Named engines resolve by name like `IHttpClientFactory` (`IMemoryEngine` / `AddMemoryEngine`, **D39**);
the graph engine decays in **interference, never a clock** (**D40**) and **buries rather than deletes**
(**D41**), over InMemory / SQLite / Postgres under one contract. A recall is an **n-shot WALK, not a
top-k** (**D100**): it returns HEADLINES, and `MemoryWalk.WalkAsync` (**D102**) expands them a step at a
time, reinforcing what it walks — so a one-shot metric measures the wrong mode. The contract is design
§5.7; every figure lives in `docs/memory-measurements.md` §5 and names the regime it was measured in.

**The memory subsystem's load-bearing invariants** — each is a rule a change can break silently, and none
of them is gated, which is why these five are here and the ones a gate or a test already holds are not.

1. **A seam is SINGULAR or PLURAL by whether its implementations read the same aspect** (**D48**) — age,
   salience and retention are plural and each owns a **composition policy**; the engine composes nothing.
2. **Age is DERIVED, not stored** — nodes carry primitives (encoding ordinal, cumulative characters,
   timestamp) and each policy projects its own view. **Except `BurstDampenedAgePolicy`**
   (`MemoryAgeKind.Accumulating`), the shipped default, which is path-dependent and keeps an accumulator.
3. **Each entry records WHICH policy computed its state** (`MemoryProvenance` flags), so "never computed"
   is distinguishable from "zero".
4. **All three age axes speak one unit** (**D52**) — an edge carries the same primitives a node does, so
   `StrengthAge` is swap-safe and `GraphMemoryOptions.EdgeHalfLife` is denominated in whatever the policies
   count, never in time.
5. **`IMemoryGraphStore` is the largest contract in the library.** A BYO store reads
   `.claude/knowledge/extending-lyntai.md` §Add a storage backend before starting one — which members take
   no default body, which three do and what each default silently costs, and `WriteBackAsync`'s ORDER.

Namespace map (Core): `Lyntai.Llm` (+ `.Cli` — a new CLI backend is a DIALECT, never a new provider —
`.Routing` / `.Caching` / `.Budgeting` / `.RateLimiting`) / `Lyntai.Generation` (+ `.Routing` / `.Jobs` /
`.Tools`; the CONTRACTS are in Core, the BACKENDS are the separate `Lyntai.Generation` package, split by
dependency footprint) / `Lyntai.Embeddings` / `Lyntai.Memory` (semantic memory + vector store; the
graph-memory DOMAINS are SEVEN: `.Interference` / `.Forgetting` / `.Modulation` / `.Salience` /
`.Ranking` / `.Annotation` / `.Verification`, each an `IMemory*Policy` seam plus its implementations AND its
options — `.Seeding` has that shape and is NOT one, because `IMemorySeedSource` PRODUCES candidates rather
than deciding about them, and the count is derived from the seam's name —
placement is by OWNERSHIP, not consumption, so only a type no domain owns (`MemoryDecayState`) sits at the
root. **`IMemoryRemovalPolicy` is the ONE seam that reads as a missing eighth domain and is not**: it is a
BLEND concern, asked by `CompositeMemoryEngine` which MEMBERS a forget or prune visits (**D75**), never a
stage of the decay pipeline the seven describe — and its namespace is frozen
either way. A root-level `IMemory*Policy` without a recorded reason now RAISES the count and fails
`check-counts`, which it previously could not see at all) / `Lyntai.Prompts` / `Lyntai.Cortex` (+ `.Scorers`) / `Lyntai.Agents` / `Lyntai.Jobs` /
`Lyntai.Guards` / `Lyntai.Secrets` / `Lyntai.Lifecycle` / `Lyntai.Storage` / `Lyntai.Processes` /
`Lyntai.Text`; builder + `Add*`/`Use*` extensions live in the `Lyntai` namespace.
**One namespace above is not Core's alone**: `Lyntai.Providers.Basic` ships `Model2VecProvider` publicly
into `Lyntai.Embeddings.Model2Vec`, while the other in-process embedder sits in `Lyntai.Providers.Onnx`. An
inconsistency, frozen by **D70** — recorded so a reader does not conclude the tree disagrees with this map.

**The records, and what each is for:** `docs/2026-07-17-lyntai-design.md` — the contract (interfaces,
semantics); read it first · `docs/DECISIONS.md` §How to read it — the rationale log, present tense,
contiguous `D1..Dn` · `docs/GATES.md` — what each gate is for and the numbers it holds · `docs/memory.md` —
the memory CONTRACT, and `docs/memory-measurements.md` the EVIDENCE, behind a generated results index whose
`ships` and status columns are the two things read wrongly here (**D114**) · `docs/model-tasks.md` — every
model-backed seam by the SHAPE of the question it asks, and what is measured about each shape ·
`docs/deployment-shapes.md` — the SAME evidence cut by the shape of the deployment (shared server,
contended device, several models in parallel, the size class you can afford) ·
`CHANGELOG.md` — per-release
detail · `README.md` — the consuming story ·
`TASKS.md` — the OPEN backlog, whose own banner is the live work: read it, never a copy kept here ·
`docs/task-archive.md` — the closed one, one Part per task · `docs/FIXES.md` — the fix log
(`.claude/rules/repo-mechanics.md` §Fix log) · `docs/ROADMAP.md` — one line per version ·
`docs/<date>-*.md` — point-in-time, check the status banner before trusting one ·
`docs/superpowers/INDEX.md` — the tracked list; the records themselves are untracked in
`local/superpowers/{specs,plans}/`, so write new ones straight there (the brainstorming and writing-plans
skills default to `docs/`, so redirect them).

## Rules, knowledge & skills

- **`.claude/rules/` is always-on; `.claude/knowledge/` is not.** The roster — what each document applies
  to and what it enforces — is the routing table in `.claude/rules/RULES_INDEX.md`. Read the knowledge
  document your task matches before touching code (`skills-workflow.md`), and add a row when you add one.
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**

## Dev loop

**`verify` is the "am I done?" gate.** Run it before claiming a change is complete. Guard tests run FIRST
on purpose — nothing below them can be trusted if the gates themselves are broken. **Start it and then keep
your hands off the tree**: it content-hashes the tree before and after and voids its own green line if
anything moved, and its exit code must never be read through a pipe (`.claude/knowledge/pitfalls.md`
§Environment / tooling).

Six things no gate can catch, so they live here rather than in `docs/GATES.md`:

- **A rule that is written down and still violated is a missing gate, not a knowledge problem.** That is
  the argument that produced `check-encoding`, `check-links`, `check-archive`, `check-backlog` and
  `check-tautology` — reach for it before writing another paragraph telling the next session to remember.
  **And when a gate's SCOPE rests on a measurement, that measurement expires**: `check-links` skipped the
  code tier on "found none in code" and re-measured at 150, 88 of them dead.
- **Add a `retiredTerms` entry (`devtools/project.config.mjs`) whenever a decision renames or re-dimensions
  something.** `check-docs` has no dead-rule check, so a MISSING entry is invisible to every gate here.
- **Each gate owns its OWN escape token** — `drift-ok` (docs), `link-ok` (links), `count-ok` (counts),
  `comment-ok` (comments), `measure-ok` (measurements), `tautology-ok` (tautology), none at all for the
  length ratchets. Never let one token silence two gates, and write every new allowance so that one LOOSER
  than needed, or one that stops matching, FAILS.
- **Repointing is the fix and renumbering is the trap**: a renumbered `§` resolves to the WRONG section
  without failing anything. Archiving a task breaks every inbound `TASKS.md Part N` the same way — that
  half IS gated now, in prose and in code comments both, so a retirement fails loudly instead of rotting.
- **`check-samples` compiles every fenced `csharp` block in the docs, and the default is ON.** Two
  annotations go before the fence — `<!-- compile-given: <declarations> -->` supplies the context a
  fragment assumes and keeps the block COMPILED, `<!-- compile-skip: <reason> -->` takes it out — and both
  on one block is an ERROR. **Prefer `compile-given`: a skip is unchecked, a given is checked.**
- **Moving prose can turn a gate red.** A `check-counts` claim anchored in exactly one sentence dies when
  that sentence is reworded; move it VERBATIM and run the gate between the copy and the delete. Two
  predicates and the sample-count check read `CLAUDE.md` BY PATH, and `doctor` — which holds the
  `**Released:**` claim above — is not in `verify`, so run it separately after touching this file.

The table below is GENERATED from `devtools/dev.mjs` plus `devLoopCommands` in
`devtools/project.config.mjs`. Edit those, never the table — `node devtools/dev.mjs check-dev-loop --write`
rebuilds it and `verify` fails while the two disagree.

<!-- dev-loop:begin — GENERATED. Edit `devLoopCommands` in devtools/project.config.mjs, never this table. -->

| command | `verify` | what it does |
| --- | :---: | --- |
| `build` | ✓ | build the solution |
| `check-warnings` | ✓ | a warning in `src/` — an unfailed IL2026 is a FALSE trim promise |
| `check-bundle` | ✓ | the bundle's dependency closure — membership is a budget |
| `check-packages` | ✓ | a package missing from any registry; the misses are silent |
| `new-package` |  | scaffold an adapter package into every registry |
| `consumer-smoke` |  | the release gate — a fresh app against the PACKAGES. Minutes |
| `test` | ✓ | the xUnit tests |
| `test-devtools` | ✓ | the guards' own tests — FIRST in `verify` |
| `playground` |  | the sample console app |
| `bench` |  | BenchmarkDotNet router/FTS benchmarks |
| `memory-sweep` |  | the {ranking × forgetting} 2×2 — miss and pollution rates |
| `memory-spacing` |  | is `topical` responsive to `DsrOptions.SpacingWeight`? |
| `memory-reinforcement` |  | law 3's `r`-dependence, isolated from reinforcement MAGNITUDE |
| `memory-bounded` |  | the FORM of the growth rule, not its constants — set `ReinforceGain` |
| `memory-salience` |  | enrichment held constant so only salience varies |
| `memory-longmemeval` |  | prefer a revised fact over the superseded one — **run `--haystack`** |
| `memory-locomo` |  | the FIELD's benchmark; rewards a perfect archive, so read it differentially |
| `memory-language` |  | one factor: `CorpusLanguage`, structurally identical corpora |
| `memory-annotation` |  | subject linking with a PERFECT annotator — the CEILING, not a model |
| `memory-annotation-drift` |  | how much of that ceiling a REAL annotator reaches, and whether CODE can close it |
| `memory-verification` |  | the judge seam — what a model in the loop is worth |
| `memory-fan` |  | ACT-R's fan effect, measured and REFUSED (D62) |
| `memory-enrichment` |  | why an embedder costs recall quality. Calls a REAL model |
| `memory-salience-weight` |  | how LOUD salience is. Needs a real embedder or the curve is an ARTIFACT |
| `memory-importance` |  | WHAT salience measures — novelty against a perfect ORACLE |
| `memory-density` |  | a REFUTATION: does a CORRECTION separate from a RECURRENCE? |
| `memory-support` |  | rule × θ × clock × `ConnectionBoost`; `--screen` = the model ladder |
| `memory-scale` |  | COST, not quality — latency, throughput, bytes; no ground truth |
| `memory-contention` |  | judge vs cross-encoder: does moving verification off the shared model pay? |
| `memory-decision` |  | a FORCED CHOICE at 3-7 options: one select call, or N score calls argmax'd? |
| `tool-affordance` |  | given these tools, what do you want — through the PROMPT protocol, on a SYNTHETIC roster |
| `install-hooks` |  | set `core.hooksPath` — once per clone, nothing warns you |
| `check-sensitive` | ✓ | leak scan; `--tree` for everything, not just staged |
| `decisions-index` |  | rebuild `DECISIONS.md`'s index after adding a `D<n>` |
| `rerank-screen` |  | does a candidate GGUF rerank AT ALL? `--inspect` needs no download |
| `embed-screen` |  | does a candidate GGUF EMBED at all? Sweeps `--pooling`, reads the range not the order |
| `locomo-pair` |  | `memory-locomo` against a CHOSEN embedder + reader, owning both servers |
| `doctor` |  | three version checks. NOT in `verify` — run before a release |
| `check-version` |  | the pre-commit version-authorship guard, by hand |
| `changelog` |  | stamp `## Unreleased` at release time — never by hand |
| `release-notes` |  | render the notes for a tagged version |
| `pack` |  | → `publish/packages/` |
| `e2e` | ✓ | the Playground against the deterministic provider-stub |
| `check-docs` | ✓ | vocabulary a decision retired (`retiredTerms`) |
| `check-links` | ✓ | a dead path, `Part N`, `§section` or `Type.Member` |
| `check-counts` | ✓ | a COUNT in prose that disagrees with the tree |
| `check-comments` | ✓ | a comment block that outgrew what it explains |
| `check-decisions` | ✓ | a `DECISIONS.md` entry that outgrew the decision |
| `check-archive` | ✓ | an archive entry that outgrew the OUTCOME it records |
| `check-backlog` | ✓ | a backlog summarizing the archive; `--write` rebuilds its roster |
| `check-pitfalls` | ✓ | an unfiled trap or stale facet index; `--write` rebuilds it |
| `check-options` | ✓ | a shipped option a consumer sets with NO xml doc to explain it |
| `check-measurements` | ✓ | a result reading CURRENT that its own body retracts; `--write` rebuilds the index |
| `check-dev-loop` | ✓ | this table drifting from `dev.mjs`; `--write` rebuilds it |
| `check-decision-claims` | ✓ | a DECISION that stopped describing the code it governs |
| `check-encoding` | ✓ | MOJIBAKE in tracked text — no other gate can see it |
| `check-tautology` | ✓ | a rename that collapsed a CONTRAST — prose naming one thing twice |
| `check-api-vocabulary` | ✓ | a retired name back on the frozen public surface |
| `check-samples` | ✓ | a fenced `csharp` block that does not COMPILE — default ON |
| `verify` |  | the "am I done?" gate. Hands off the tree while it runs |
| `new-migration` |  | scaffold the next migration with a unique number |

<!-- dev-loop:end -->
