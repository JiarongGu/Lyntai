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

**Released: v3.2.0 (2026-09-19).** Eleven packages, one of them unreleased. The public API is frozen under
SemVer 2.0 since 1.0 for every package (**D70**), but while every consumer is first-party a documented break
may ship in a minor under `### Breaking` (**D18**, **D161**); storage and migration breaks stay major-only.
The reasoning is `docs/DECISIONS.md`, **D1–D181** — read its generated index table rather than any list of
decisions kept here. **Everything before 3.0 is HISTORY, not context**: `.claude/rules/repo-mechanics.md`
says what that forbids.

**The baseline a green run should match:** `4987 passed / 5028 total, 41 skipped` (every skip is a
live-backend gate), e2e 3/3, guard-script tests 950/950, doc samples 62/62 — MEASURED with Docker up at
`5b033a3e` (2026-09-25). No gate holds the xUnit trio (`docs/GATES.md` §Which numbers a gate holds), so:

- **Re-measure it by hand after `verify`, off that run's own output**, never from a diff, and re-attest the
  COMMIT with the figures whenever they move.
- **Accept movement only against a NAMED cause**: a test added or removed. A refactor moves nothing, and
  retiring a package costs its two `ApiSurface` theories. Any other movement is a finding.
- **A skip count in the low HUNDREDS means Docker is down**: the Postgres leg went unexercised while every
  gate stayed green. Start Docker and re-run; never attest a figure reconciled by arithmetic. One more skip
  against a named new live-gated test is fine, and a LOWER count is the live suites running.

**Long-term memory is the subsystem most often reasoned about wrongly.** `IMemoryEngine` (named engines,
**D39**) is not the three older surfaces (`IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore`): it
decays by INTERFERENCE, never a clock (**D40**), BURIES rather than deletes (**D41**), and a recall is an
n-shot WALK over headlines, not a top-k (**D100**, **D102**), so a one-shot metric measures the wrong mode.
Its contract, headed by the five invariants no gate holds, is `docs/memory.md`; every figure is
`docs/memory-measurements.md` §5.

**Namespace map (Core).** Builder + `Add*`/`Use*` extensions live in `Lyntai`.

- `Lyntai.Inference`: everything about CALLING a backend, flat (**D154**) — the provider seam, the verdict
  taxonomy, the four call shapes, the text front door and both routers (`TextRouter`, `MediaRouter`: peers
  **D153** refused to merge). Plus `.Cli` (ONE engine; a new CLI is an `ICliBackend`, **D21**/**D159**) and
  the front-door decorators `.Caching` / `.Budgeting` / `.RateLimiting` / `.Streaming`.
- `Lyntai.Generation` (+ `.Jobs` / `.Tools`): what RUNS a generation. The media backends are the separate
  `Lyntai.Generation` package, namespace `Lyntai.Generation.Providers`.
- `Lyntai.Memory`: the engines (`.Engines`), semantic memory, the vector store.
  The graph-memory DOMAINS are SEVEN: `.Interference` / `.Forgetting` / `.Modulation` / `.Salience` /
  `.Ranking` / `.Annotation` / `.Verification`, each an `IMemory*Policy` seam with its implementations and
  options, placed by OWNERSHIP.
  `.Seeding` (it produces candidates) and `IMemoryRemovalPolicy` (a blend concern, **D72**) are not domains,
  and a root-level `IMemory*Policy` without a recorded reason fails `check-counts`.
- `Lyntai.Prompts` · `Lyntai.Cortex` (+ `.Scorers`) · `Lyntai.Agents` · `Lyntai.Jobs` · `Lyntai.Guards` ·
  `Lyntai.Secrets` (shared with `Lyntai.Secrets.Dpapi`) · `Lyntai.Storage` · `Lyntai.Processes` ·
  `Lyntai.Text` · `Lyntai.Diagnostics`. Adapters are `Lyntai.Providers.<Backend>` /
  `Lyntai.Storage.<Backend>`, one segment per adapter.

**Vocabulary** — what a rename sweep breaks without failing anything:

- A CALL SHAPE is named for what it PRODUCES: `Text*`, `Vector*`, `Score*`, `Media*` (`ITextClient`,
  `TextRouter`).
- `Llm` stays wherever it means "this asks a language model": `LlmScorerBase`, `LlmMemoryVerificationPolicy`,
  `IScorer.IsLlm`, the `is_llm` column in both SQL backends and the `"llm"` score group.
- A PROVIDER is named for its BACKEND, and what it produces is DATA (`ProviderCapabilities.Produces`,
  **D152**): one `Add<Backend>Provider(…)` per package, EF Core-style, never an `Add<Kind>Provider`. A kind
  never forks a provider class; an options field selects it (`HttpModelOptions.Produces`,
  `OnnxProviderOptions.Produces`, **D157**), and is a list only where one backend really serves several
  (`ComfyUiOptions`, `FalOptions`).
- The noun is `Vector` (what `Produces` selects on: `ProviderKinds.Vector`, `IVectorStore`) and the verb is
  `Embed` (`EmbedAsync`, `CanEmbed`).

**The records, and what each is for:** `docs/2026-07-17-lyntai-design.md` — the design contract (semantics;
the API baselines own shape): read the section you touch · `docs/memory.md` — the memory CONTRACT, and
`docs/memory-measurements.md` the EVIDENCE, behind a generated results index whose `ships` and status
columns are the two things read wrongly here (**D114**) · `docs/DECISIONS.md` §How to read it — the
rationale log, present tense, contiguous `D1..Dn` · `docs/GATES.md` — what each gate is for and the numbers
it holds · `docs/model-tasks.md` — every model-backed seam by the SHAPE of the question it asks ·
`docs/deployment-shapes.md` — the same evidence cut by the shape of the deployment · `README.md` — the
consuming story · `CHANGELOG.md` — per-release detail · `docs/ROADMAP.md` — one line per version ·
`TASKS.md` — the OPEN backlog, whose generated roster is the live work: read it, never a copy kept here ·
`docs/task-archive.md` — the closed one, one Part per task · `docs/FIXES.md` — the fix log ·
`docs/superpowers/INDEX.md` — the tracked list of specs and plans, whose records live untracked in
`local/superpowers/`.

## Rules, knowledge & skills

- **`.claude/rules/` is always-on; `.claude/knowledge/` is not.** What each document applies to is the
  routing table in `.claude/rules/RULES_INDEX.md`; each knowledge document's front matter says what it
  enforces. Read the document your task matches before touching code (`skills-workflow.md`), and add a row
  when you add one.
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**

## Dev loop

**`verify` is the "am I done?" gate.** Run it before claiming a change is complete, keep your hands off the
tree while it runs (it voids its own green line if a file moved), and never read its exit code through a
pipe (`.claude/knowledge/pitfalls.md` §Environment / tooling). `docs/GATES.md` is why each gate exists and
what it holds. The six rules that bite when editing prose:

- **A rule written down and still violated is a missing gate**: build the gate, not another paragraph. A
  gate whose SCOPE rests on a measurement needs the measurement re-run as the tree grows.
- **A decision that renames or re-dimensions something adds a `retiredTerms` entry**
  (`devtools/project.config.mjs`); a rename in neither registry is invisible to every gate.
- **Each gate owns its OWN escape token** (`docs/GATES.md` §Escape tokens and allowances).
- **Repoint a citation, never renumber its target**: a renumbered `§` resolves to the WRONG section
  silently.
- **Every fenced `csharp` block compiles**: prefer `compile-given` to `compile-skip`, and never put a
  compiled sample under `## Unreleased` (`docs/GATES.md` §check-samples).
- **Rewording the one sentence a counted claim is anchored in turns `verify` red**: move it VERBATIM. This
  file is read BY PATH by `test-devtools` and `check-samples` (the two run-derived baselines above),
  `check-dev-loop` (the table) and `doctor` (the `**Released:**` line) — and `doctor` is NOT in `verify`, so
  run it after touching this file.

The table below is GENERATED from `devtools/commands.mjs` plus `devLoopCommands` in
`devtools/project.config.mjs`. Edit those, never the table — `node devtools/dev.mjs check-dev-loop --write`
rebuilds it and `verify` fails while the two disagree.

<!-- dev-loop:begin — GENERATED. Edit `devLoopCommands` in devtools/project.config.mjs, never this table. -->

| command | `verify` | what it does |
| --- | :---: | --- |
| `build` |  | build the solution |
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
| `memory-bounded` |  | the FORM of the growth rule, not its constants — set `ReinforceGain` |
| `memory-salience` |  | enrichment held constant so only salience varies |
| `memory-longmemeval` |  | prefer a revised fact over the superseded one — **run `--haystack`** |
| `memory-locomo` |  | the FIELD's benchmark; rewards a perfect archive, so read it differentially |
| `memory-language` |  | one factor: `CorpusLanguage`, structurally identical corpora |
| `memory-annotation` |  | subject linking with a PERFECT annotator — the CEILING, not a model |
| `memory-annotation-drift` |  | how much of that ceiling a REAL annotator reaches, and whether CODE can close it |
| `memory-consolidation` |  | does an OFFLINE pass find links the write path missed? Needs a real embedder |
| `memory-affect` |  | does AROUSAL predict what gets asked about later? LoCoMo, a chat model + a lexicon |
| `storage-scan` |  | can a file-per-record store SCAN for recall, or must it keep an index? `--writes`: what a recall writes… |
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
| `nuget-unlist` |  | hide superseded versions on nuget.org — a DRY RUN unless `--apply` |
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
| `check-dev-loop` | ✓ | this table drifting from `devtools/commands.mjs`; `--write` rebuilds it |
| `check-decision-claims` | ✓ | a DECISION that stopped describing the code it governs |
| `check-encoding` | ✓ | MOJIBAKE in tracked text — no other gate can see it |
| `check-tautology` | ✓ | a rename that collapsed a CONTRAST — prose naming one thing twice |
| `check-api-vocabulary` | ✓ | a retired name back on the frozen public surface |
| `check-samples` | ✓ | a fenced `csharp` block that does not COMPILE — default ON |
| `verify` |  | the "am I done?" gate. Hands off the tree while it runs |
| `new-migration` |  | scaffold the next migration with a unique number |

<!-- dev-loop:end -->
