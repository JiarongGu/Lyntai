# CLAUDE.md — Lyntai (灵台)

> The brief every agent reads is `AGENTS.md`, imported at the foot of this file. This file keeps only the
> lines this repository's tooling reads HERE by path, and the agent-specific half: `.claude/rules/` is
> loaded with it.

## Current state

**Released: v3.5.3 (2026-09-30).** Eleven packages. The public API is frozen under
SemVer 2.0 since 1.0 for every package (**D70**).
The reasoning is `docs/DECISIONS.md`, **D1–D233** — read its generated index table rather than any list of
decisions kept here.

**The baseline a green run should match:** `6291 passed / 6376 total, 85 skipped` (every skip is a
live-backend gate), e2e 3/3, guard-script tests 1023/1023, doc samples 78/78 — MEASURED with Docker up at
`eb13c3d5` (2026-10-30). No gate holds the xUnit trio (`docs/GATES.md` §Which numbers a gate holds), so:

- **Re-measure it by hand after `verify`, off that run's own output**, never from a diff, and re-attest the
  COMMIT with the figures whenever they move.
- **Accept movement only against a NAMED cause**: a test added or removed. A refactor moves nothing, and
  retiring a package costs its two `ApiSurface` theories. Any other movement is a finding.
- **A skip count in the low HUNDREDS means Docker is down**: the Postgres leg went unexercised, and `test`
  fails on it unless the run set `LYNTAI_ALLOW_POSTGRES_SKIP=1` (`docs/GATES.md` §test). Start Docker and
  re-run; never attest a figure reconciled by arithmetic. One more skip against a named new live-gated test
  is fine, and a LOWER count is the live suites running.

## Dev loop

**Rewording the one sentence a counted claim is anchored in turns `verify` red**: move it VERBATIM. This
file is read BY PATH by `test-devtools` and `check-samples` (the two run-derived baselines above),
`check-dev-loop` (the table), `check-counts` (the decision range) and `doctor` (the `**Released:**` line) —
and `doctor` is NOT in `verify`, so run it after touching this file.

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
| `test` | ✓ | the xUnit tests — a SKIPPED Postgres leg fails unless the run declares it |
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
| `memory-corpus` |  | recall QUALITY on a real record set far larger than ours, kept untracked; `--model-free` or a local emb… |
| `memory-profile` |  | RV27 Track A: the model-free gate a profile over curated memory must pass, per bucket. No model |
| `memory-contention` |  | judge vs cross-encoder: does moving verification off the shared model pay? |
| `memory-decision` |  | a FORCED CHOICE at 3-7 options: one select call, or N score calls argmax'd? |
| `tool-affordance` |  | given these tools, what do you want — through the PROMPT protocol, on a SYNTHETIC roster |
| `bench-compare` |  | two `_bench-results/` runs, PAIRED by (arm, question): McNemar exact, refuses a mismatch |
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
| `check-lanes` | ✓ | a tracked file in no lane, or a lane glob that matches nothing |
| `check-tautology` | ✓ | a rename that collapsed a CONTRAST — prose naming one thing twice |
| `check-api-vocabulary` | ✓ | a retired name back on the frozen public surface |
| `check-samples` | ✓ | a fenced `csharp` block that does not COMPILE — default ON |
| `verify` |  | the "am I done?" gate. Hands off the tree while it runs |
| `new-migration` |  | scaffold the next migration with a unique number |

<!-- dev-loop:end -->

<!-- daoris:import — generated; edit the canon, not this -->
@AGENTS.md
<!-- /daoris:import -->
