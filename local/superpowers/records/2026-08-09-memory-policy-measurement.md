# Memory ranking × forgetting policy measurement (2026-08-09, re-measured 2026-08-10, 2026-08-11)

> **Status: a completed measurement — the falsification pass behind `docs/DECISIONS.md` D49, not itself
> the decision.** D49 made `Lyntai.Memory.Forgetting.DsrRetrievability` the registered 3.0 default forgetting
> curve. Its PRIMARY evidence is FSRS's own external validation (fitted and validated against hundreds of
> millions of real reviews, outside this repository) — never "this corpus said so." What is recorded here is
> this library's own falsification attempt: a corpus deliberately retargeted into the band where the two
> curves that existed at the time disagreed, a 30-seed paired sweep, and a six-item pathology battery
> enumerated before it ran. **It did not falsify DSR, but it did find one real, measured adverse effect — the
> `topical` regression — which D49 ships as a KNOWN, PRIORITIZED DEFECT, not a curiosity explained away.** If
> this document and `docs/DECISIONS.md` disagree about anything, `docs/DECISIONS.md` wins; this page is the
> record of what was measured, not a mirror kept in sync with the decision after the fact.
>
> **2026-08-11 addendum (fsrs-properly plan, Task 4) — the comparison changed because `HalfLifeRetrievability`
> is DELETED (fsrs-properly plan Task 1) and `DsrRetrievability`'s difficulty axis is now LIVE (Task 2).** The
> sections below through "The verdict, per domain" are the ORIGINAL, HISTORICAL record of the curve-vs-curve
> falsification pass exactly as it read on 2026-08-10 — kept verbatim because D49 cites it and because the
> `topical` MECHANISM it documents (a ranking policy rewarding raw reinforcement magnitude) is what motivated
> Task 4's own re-measurement. **"## Task 4 re-measurement" below is the CURRENT, decision-relevant content**:
> a difficulty-live-vs-difficulty-inert × ranking re-measurement on the ONE curve that ships today, run at a
> deliberately smaller `N` than the historical `N=30` (disclosed there, not rounded away) because this
> session's foreground runtime constraint made the full grid unwaitable — see that section for exactly what
> was cut.
>
> **That re-measurement then changed a shipped default, so this page is load-bearing for 3.0, not merely a
> record**: RRF beat `MultiplicativeRankingPolicy` on the corpus's `topical` class in all six shapes, and the
> owner ruled `ReciprocalRankFusionPolicy` the 3.0 ranking default on the strength of it (D49's own "the
> ranking half is REOPENED by measurement" amendment). **"### Ranking: keep `MultiplicativeRankingPolicy`",
> under "The verdict, per domain" below, is therefore SUPERSEDED** — it is kept for the reason the rest of
> the historical record is kept, and carries its own closing amendment saying so.

## What this is

Two domains of the graph memory subsystem are swappable. Ranking still ships two implementations
(`Lyntai.Memory.Ranking.MultiplicativeRankingPolicy`, `Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy`).
Forgetting shipped two at the time the sections below were written
(`Lyntai.Memory.Forgetting.HalfLifeRetrievability`, `Lyntai.Memory.Forgetting.DsrRetrievability`) but ships
only `DsrRetrievability` today — `HalfLifeRetrievability` was deleted 2026-08-10 (fsrs-properly plan Task 1,
`docs/DECISIONS.md`) once its own falsification pass (below) closed the question this document exists to
answer. **The historical sections keep describing both curves in the past tense they earned; "## Task 4
re-measurement" describes today's one-curve reality.**
An earlier measurement (2026-08-09, the corpus-harness plan, `docs/task-archive.md` Part 54 item 1) found a
ranking proposal but left the forgetting-curve question an honest null — the sweep's own corpus tied the two
curves on five of six shapes by construction, discriminating almost nothing. **This document supersedes that
sweep's forgetting-curve verdict.** A follow-on plan (`feat/dsr-default`, 2026-08-10) retargeted the corpus
into the band where the curves actually diverge (Task 1), paired a 30-seed sweep by seed across the same
six-shape grid (Task 2), and ran a six-item falsification battery against both curves, enumerated before any
of it executed (Task 3). Built by `bench/Lyntai.Benchmarks/MemoryPolicySweep.cs` (run with
`node devtools/dev.mjs memory-sweep`), fed by `tests/Lyntai.Tests/Memory/Corpus/MemoryCorpus.cs`, measured by
`tests/Lyntai.Tests/Memory/Corpus/RecallQuality.cs`, falsified against
`tests/Lyntai.Tests/Memory/DsrDefaultFalsificationTests.cs`.

**Reproducing the full grid.** This document keeps the decision-relevant rows, not all 720 cells. It does not
need to: the sweep is **deterministic and seeded**, so `node devtools/dev.mjs memory-sweep` over seeds
`12345`–`12374` regenerates the MissRate/PollutionRate table byte-for-byte (~22 min; it is deliberately
outside `verify`) — that claim is scoped to the table's own numbers, never to the run's wall-clock duration
or the reporting machine's CPU/core count, both of which are properties of whatever hardware happens to run
it, not of the corpus or the curves. Recording the seed family is strictly better than pasting the grid — a
pasted table goes stale silently the moment the corpus or a curve changes, while a regenerated one cannot.
**If a future run disagrees with a MissRate or PollutionRate number cited below, something in the corpus or
the curves moved, and that divergence is itself the finding.**

> **That clause came due on 2026-08-11, and this is the finding.** The corpus's own FILLER was competing with
> the entries it measured: `WriteFiller`'s padding shared the token `"item"` with every real entry and
> `FtsQuery.Build` OR-joins tokens, so a freshly-written filler (retrievability ≈ 1) could outscore an
> already-decayed-but-relevant target **before that target was ever recalled** — `Reinforce` was never called
> for those entries at all (`TASKS.md` Part 56, measured at 9–10 of 55 relevant ids). Filler now writes
> `"padding filler{n} …"`; `FtsQuery.Build` is untouched, only the leading token changed, and every
> non-filler entry's content is byte-identical.
>
> **Numbers moved. No verdict flipped.** RRF still beats `MultiplicativeRankingPolicy` on `topical` in **all
> six shapes**, beyond the ±0.10 action threshold with CI excluding zero, and every `DifficultyEffect` cell is
> still null with a CI spanning zero. **`docs/DECISIONS.md` D49's ranking-default decision is unaffected** —
> the re-run is the same verdict on a cleaner instrument.
>
> What moved, on `topical` `RankingEffect` (published n=8 → re-run n=30):
>
> | Shape | published | after the fix | move |
> |---|---|---|---|
> | baseline | +0.506 ± 0.106 | +0.463 ± 0.064 | −0.043, within noise |
> | low-reuse | +0.431 ± 0.220 | +0.478 ± 0.083 | +0.047, within noise |
> | high-reuse | +0.531 ± 0.063 | +0.513 ± 0.043 | −0.018, within noise |
> | **high-noise** | +0.413 ± 0.144 | **+0.200 ± 0.029** | **−0.213, beyond both CIs** |
> | **many-candidates** | +0.746 ± 0.051 | **+0.533 ± 0.022** | **−0.213, beyond both CIs** |
> | rare-critical | +0.492 ± 0.073 | +0.430 ± 0.056 | −0.062, borderline |
>
> **Read the direction, because it is the honest part: the padding had been INFLATING RRF's measured
> advantage**, and by the most in the two shapes with the densest competition. That is consistent with the
> mechanism this document already names — Multiplicative rewards raw reinforcement magnitude, and fresh
> filler was exactly that. The instrument was flattering the winner.
>
> **Do not read the CI widths as comparable**: the published Task 4 table ran a reduced ceiling (n=8, seeds
> `12345`–`12352`) and the re-run used the full 30. The **pilot SD is** the apples-to-apples comparison — same
> 5 pilot seeds, same worst cell (`low-reuse`/`topical`/`RankingEffect`): **0.2702 → 0.1987**, required
> **N 58 → 32**. On that identical measurement the instrument is **~26% less noisy**.
>
> **`MemoryDefaultRecallQualityTests`'s fixed-corpus pin got WORSE** and was re-pinned: miss `0.179` →
> `0.234`, pollution `0.866` → `0.871`. No policy changed, and the corpus timeline is structurally identical
> (same write count, ordering and interposition; every discriminating-band guard still passes). Only
> *eligibility to be returned* changed. The mechanism is recorded **as a hypothesis, labelled as one**:
> recall reinforces what it returns, filler was absorbing that reinforcement harmlessly, and those slots now
> go to real-but-irrelevant entries that grow more retrievable and compete harder later. **A pin reading
> worse on a less noisy instrument is not a contradiction** — the old number was partly measuring the
> padding.

## The honesty clause — read this before citing a number below

**Nothing here asserts a real deployment behaves like this corpus, and that is sharper now than it was
before the retarget.** `MemoryCorpus` was deliberately retimed (Task 1) so the two curves' `age/S` ranges sit
inside the band where they diverge (`topical` ≥5.0, `hot-ephemeral (in-window)` ≥1.5, `critical-rare` ≥3.0)
— the corpus's own class doc says, in these words, that **it is now an INSTRUMENT chosen for SENSITIVITY to
the curves' disagreement, not a simulation of realistic usage.** That is a deliberate, disclosed choice: a
corpus built to agree with production traffic would mostly measure the band where the curves already tie
(the original sweep's own finding), which cannot falsify anything. It also means a number below says
nothing about how often a real deployment's queries actually land in the discriminating band — only that
when they do, this is what the two curves do differently.

The sweep itself does not run in CI (~1300s for the full paired grid — see "Pilot → scale" below); the
regression guard built from the same corpus machinery,
`tests/Lyntai.Tests/Memory/MemoryDefaultRecallQualityTests.cs`, does, but it pins one fixed corpus point under
the shipped default, not this table. So a number below is **"measured against a stated corpus, in CI, which
a production corpus is not"** — the same sense `tests/Lyntai.Tests/Memory/MemoryDecaySimulationTests.cs`'s
own class doc uses that phrase for the decay constants: it pins dynamics a reader can trust hold *in this
model*, and says so rather than implying more.

## Methodology

**Paired by seed** (2026-08-10, Task 2): every arm at a given `(seed, shape)` replays the byte-identical
corpus, so `CurveEffect = HalfLife-MissRate − Dsr-MissRate` and `RankingMain =
Multiplicative-MissRate − RRF-MissRate` are computed per seed and averaged, rather than comparing two
independently-drawn samples. **Pilot-then-scale:** 5 pilot seeds (`12345`–`12349`) measured the paired
curve-effect's own worst-case standard deviation — **SD = 0.2092**, found on `low-reuse`/`topical` — which
implies **N = 35** for 80% power to detect a 0.10 `MissRate` difference at two-sided α = 0.05. The run's own
30-seed ceiling capped the actual scale-up at **N = 30** (seeds `12345`–`12374`) — **a disclosed shortfall
against the derived N, not rounded away.** Every reported cell below still resolves to a clear verdict at
N = 30 (null, not-actionable, or clearing the ±0.10 threshold with a CI excluding zero), so the missing 5
seeds would not have moved a conclusion, but the shortfall is real and stated rather than absorbed.

Four arms — `Multiplicative+HalfLife`, `Multiplicative+Dsr`, `RRF+HalfLife`, `RRF+Dsr` — replay each corpus's
ordered timeline against a live `Lyntai.Memory.Engines.GraphMemoryEngine` backed by
`Lyntai.Storage.Sqlite.SqliteMemoryGraphStore`, one fresh migrated database per `(seed, shape, arm)` (720
engines total). The same three confound controls as the predecessor sweep apply (no retention policies; each
arm's own options record passed explicitly; an undamped per-write age clock rather than the engine's own
burst-damped default) plus one added this round: `RelativeFloor` is **equalized** at `0.02` on both ranking
arms (derived from `MultiplicativeRankingOptions`, not restated), removing a confound the predecessor sweep
only disclosed. Six corpus shapes, one-factor-at-a-time against `CorpusShape.Default`: `baseline`,
`low-reuse` (`ReuseRatio=1`), `high-reuse` (`ReuseRatio=10`), `high-noise` (`NoiseDensity=40`),
`many-candidates` (`CandidateCount=40`), `rare-critical` (`CriticalRarity=12`).

**Dropped deliberately, same as before:** the full four-parameter factorial (still one-factor-at-a-time); a
second seed FAMILY (all 30 seeds are the sequential family `12345`+i, not independently-drawn samples from a
varied generation strategy); salience as a swept axis; within-arm hyperparameters (every arm uses its
policy's shipped defaults); `CandidateCount` beyond 40.

## The adverse finding: `topical` under the pairing that ships — read the per-class table, not the pooled one

**Under `Multiplicative+Dsr` — the pairing 3.0 actually ships — DSR misses more than HalfLife on the
`topical` query class, in every one of the six shapes, clearing the pilot's own ±0.10 action threshold with a
CI excluding zero in five of six:**

```
Shape            CurveEffect|Mult (HalfLife − Dsr MissRate, + = Dsr better)
baseline         -0.303 ± 0.029
low-reuse        -0.177 ± 0.027
high-reuse       -0.390 ± 0.033
high-noise       -0.013 ± 0.013   (small; CI borderline)
many-candidates  -0.191 ± 0.008
rare-critical    -0.320 ± 0.023
```

**It is not one-sided, and this is the reason the whole-corpus aggregate never flags it.** The identical
pairing shows the *opposite* pattern on `hot-ephemeral (in-window)` — DSR clearly *better* than HalfLife, in
every shape, by a similar or larger margin:

```
Shape            CurveEffect|Mult (HalfLife − Dsr MissRate, + = Dsr better)
baseline         +0.393 ± 0.014
low-reuse        +0.393 ± 0.014
high-reuse       +0.393 ± 0.014
high-noise       +0.200 ± 0.000
many-candidates  +0.373 ± 0.026
rare-critical    +0.400 ± 0.000
```

**These two classes offset in the `all (combined)` row on every shape, which is why the pooled, whole-corpus
`Verdict (curve)` column reports most of these cells as "distinguishable, but < 0.10 (not actionable)" —
`RRF`'s own opposite-signed effect on `topical` (mostly positive, i.e. Dsr better or null under RRF) drags the
50/50 Multiplicative/RRF pooled average back under the action threshold even where the shipping pairing alone
clears it by a wide margin. Read plainly: the pooled number is the one NOT to read for this decision — the
per-ranking, per-class breakdown is.** One cell (`rare-critical`/`topical`) crosses the action threshold in
the pooled column too (`Dsr WORSE (beyond 0.10)`, the one flagged cell in the full sweep), driven almost
entirely by the same `Multiplicative`-paired effect above.

**`critical-rare` — the class the original corpus-harness plan (`docs/task-archive.md` Part 55) nominated as
deciding — decided nothing.** Its curve effect measured `0.000 ± 0.000` in every shape, every seed, despite Task 1 spending the
most retargeting effort on it (independent N ranges 20–240 depending on shape). The two curves' *raw*
retrievability values genuinely diverge there by construction (Task 1 measured `DsrRetrievability` ≈0.106 vs
`HalfLifeRetrievability` ≈1.3e-9 at the deepest ages reached), but whichever curve computes it, the *ranked
outcome* for these queries is apparently identical — consistent with admission being governed by something
other than retrievability magnitude at these depths (grade-based admission is the likeliest candidate; this
sweep does not isolate it). Read as an honest limitation, not a finding for or against DSR: the class the
predecessor plan most wanted to discriminate on carries no discriminating power here at N=30.

## The measured mechanism behind `topical`

Replaying the exact shape of a topical reuse batch in isolation (one entry aged to `age/S = 5`, then four
repeat queries with exactly one filler write interposed between each — mirroring `MemoryCorpus`'s own
`FireReuseBatch` verbatim) and reading back stored stability after every touch, on both curves:

```
Dsr:      45.1048 -> 46.0617 (+2.12%) -> 47.0110 (+2.06%) -> 47.9531 (+2.00%)
HalfLife: 30.0000 -> 45.0000 (+50.0%) -> 67.5000 (+50.0%) -> 101.2500 (+50.0%)
```

`DsrRetrievability`'s first touch (`age/S = 5`, `r` well below 1) grows stability **+125%** — a large,
correct FSRS response to a genuinely faint memory. Every touch after that fires roughly one write later
(`r ≈ 0.97`, not exactly 1 — a single interposed write is a small but nonzero age) and grows only **~2%**,
over an order of magnitude smaller than the first touch's own rate: law 3's spacing term
(`e^(spacing·(1−r)) − 1`) correctly *shrinking*, not vanishing outright and not a bug — a companion fact
confirmed DSR's own contract (stability never shrinks touch-to-touch, retrievability stays a valid
probability) holds throughout this exact pattern. `HalfLifeRetrievability`'s flat `× 1.5` fires identically
on every touch regardless of `r`. By the end of the same four-touch batch, HalfLife has compounded to
**101.25** against DSR's **47.95** — a **2.1×** gap that opens entirely from touches 2–4, where DSR
(correctly, per its own documented model) gains almost nothing and HalfLife (unconditionally) keeps growing.
Under `MultiplicativeRankingPolicy`'s `Score = Relevance × Retrievability`, this is exactly what lets
HalfLife's just-reinforced entries stand out further above template-sharing rivals than DSR's do — a
ranking-competition side effect of what `MultiplicativeRankingPolicy` rewards (raw reinforcement magnitude),
not a violation of anything `DsrRetrievability` promises. A single-seed corpus-scale reproduction
(`CorpusShape.Default`, seed `12345`, `Multiplicative` ranking) confirms the mechanism surfaces at corpus
scale, not just in the isolated micro-replay: `HalfLife` topical `MissRate` = 0.7000, `Dsr` topical
`MissRate` = 0.9000 — the same direction Task 2 found statistically across every shape.

## The falsification battery (Task 3) — enumerated before it ran

Six disqualifying behaviours, written down before any of them were tested, checked against **both** shipped
curves so a pathology that fires on both is reported as a subsystem property, not evidence against DSR
specifically: (1) a memory becoming permanently unreachable; (2) stability collapsing or exploding under a
reachable sequence of writes and recalls; (3) a well-connected entry decaying faster than an isolated one,
over a replayed corpus; (4) `CandidateCutoff` failing its conservative-superset property over real replayed
states; (5) reinforcement that never fires across a realistic session — the exact `r≡1`-always pathology
that made the *predecessor* sweep meaningless; (6) the mechanism behind the `topical` finding itself, and
whether it is DSR correctly declining to over-strengthen or DSR being wrong.

**None of the six disqualified either curve.** Unreachability, stability collapse/explosion, the
`CandidateCutoff` superset property, and connectedness lowering retrievability all held on both curves, over
real replayed states (never a synthetic grid), each guard confirmed live by a deliberately-wrong mutation of
the policy under test. Item 6 is the mechanism recorded above: a real, correctly-attributed loss under a
specific ranking pairing, not a violation of DSR's own contract.

**Two SHARED, curve-symmetric properties surfaced — neither is evidence against DSR specifically, both are
now `TASKS.md` items:**

1. **The already-open `MaxStability`-lowering shortening defect** (`TASKS.md` Part 54, DSR2/DSR3) —
   reconfiguring `MaxStability` lower than an existing stored value shortens a memory on *both* curves
   (confirmed live: a stored `100000` collapses to exactly `2000`). Not new; Task 3 confirmed it recurs on
   the live engine rather than only in the contract test, but it was already tracked before this plan.
2. **A handful of early corpus entries are never recalled at all for any of their own relevant queries** —
   outranked by the corpus's own filler padding (`MemoryCorpus.WriteFiller`), because `FtsQuery.Build`
   OR-joins tokens and every entry shares the token `"item"`, so freshly-written filler (retrievability ≈1)
   can outscore an already-decayed-but-relevant target under `Score = Relevance × Retrievability`.
   Comparable magnitude on both curves — **9 of 55 relevant ids under DSR, 10 of 55 under HalfLife** — new to
   this plan, and now a `TASKS.md` item.

## The verdict, per domain

### Forgetting curve: `DsrRetrievability` ships as the 3.0 default (`docs/DECISIONS.md` D49)

FSRS's own external validation is the primary evidence; this library's falsification pass did not falsify
it. The `topical` regression above is real, measured, and ships as a **known, prioritized defect** — this
library's `DsrRetrievability` is a partial, unfitted FSRS (no per-review difficulty update; published rather
than fitted constants), and completing it is now prioritized work (`TASKS.md`), not a someday item. See D49
for the full reasoning and the caveats. **2026-08-11: the "one-line restore for a consumer who wants the old
curve" no longer exists — `HalfLifeRetrievability` is deleted, not merely superseded (fsrs-properly plan Task
1). The "partial, unfitted FSRS" description is also now half-stale: the per-review difficulty update shipped
(Task 2); see "## Task 4 re-measurement" below for whether it actually helped `topical`.**

### Ranking: keep `MultiplicativeRankingPolicy` — SUPERSEDED 2026-08-11, see the amendment closing this section

This measurement did not re-open the ranking question, and the predecessor sweep's verdict stands exactly as
it was: `MultiplicativeRankingPolicy`'s own `critical-rare` `MissRate` was `≤` `ReciprocalRankFusionPolicy`'s
on every shape measured, with no exceptions — a defensible default *proposal*, not a transferable prior.
**RRF's own validation is for fusing independent ranked lists from separate retrieval systems, where scores
are incomparable and rank position is the only shared currency; here every signal comes from one system and
the magnitudes are meaningful, so rank fusion discards information this library holds.** That is off-label
use of RRF's own evidence, and it is why this default stays on this library's own measurement, stated as
weak, rather than on an external prior the way the curve default now is. See `docs/task-archive.md` Part 54
item 1 for the original measurement and Part 55 for how this plan closed the owner's decision on both
domains; `TASKS.md` Part 53 carries the one still-open ranking-adjacent item (measuring salience's own effect
now that the ranking seam exists). **2026-08-11: "this measurement did not re-open the ranking question" is
no longer true — Task 4 did, deliberately, per the design's own instruction that the `topical` loss "was
measured to be a ranking-competition artifact... asking costs nothing." See "## Task 4 re-measurement" below
for what asking it found: `ReciprocalRankFusionPolicy` measurably outperforms `MultiplicativeRankingPolicy` on
`topical` under the ONE curve that ships today, in every shape tested.**

> **RESOLVED the same day — the owner ruled `ReciprocalRankFusionPolicy` the 3.0 ranking default**
> (*"lets use the best so RRF for ranking"*). When the amendment above was written the finding was recorded
> as "on the table, but the default stays the owner's call"; the call was then made, in favour of the
> measurement. So **this section's own heading, and everything under it above this amendment, describes a
> default that no longer ships.** It is kept, unedited, because it is the
> honest record of what this library believed on the strength of the `critical-rare` proposal, and because
> the reason it gives for distrusting RRF's external evidence — that RRF is validated for fusing
> *independent ranked lists*, whereas here every signal comes from one system with comparable magnitudes —
> is **still correct and still the reason `MultiplicativeRankingPolicy` stays shipped**. What changed is
> that this library then produced its own direct measurement on its own corpus, which is not off-label use
> of anyone's prior. `MultiplicativeRankingPolicy` remains one line to restore and remains the better
> choice on a scale where raw magnitude carries meaning.

### The interaction is the reason neither verdict above can be read alone

The curve and ranking defaults are not independent questions. `topical`'s loss is a ranking-competition
artifact of what `MultiplicativeRankingPolicy` rewards; `RRF+Dsr` shows the opposite pattern on the same
class in most shapes. A future change to either default has to re-check the other's own measurement under
the new pairing — this is the same coupling the predecessor sweep found on `many-candidates`/`topical`,
reproduced here across every shape rather than one.

## Task 4 re-measurement (2026-08-11): difficulty-live vs difficulty-inert, and the ranking default

**The question changed because the comparison changed.** `HalfLifeRetrievability` is deleted (fsrs-properly
plan Task 1) — there is one curve now, so this is no longer "does DSR beat HalfLife." Task 2 made
`DsrRetrievability`'s difficulty axis LIVE (`Reinforce` now maintains `MemoryDecayState.Difficulty` every
review, deriving a grade from retrievability at recall). The arms this section measures are
**`{difficulty-live, difficulty-inert} × {MultiplicativeRankingPolicy, ReciprocalRankFusionPolicy}`** — four
arms, fully crossed (not one-factor-at-a-time), replaying the byte-identical corpus per seed exactly as
before. **Difficulty-inert is `DsrOptions` with `DifficultyChangeWeight = 0` AND
`DifficultyReversionWeight = 0` together** — a fix-round correction inside Task 2 found that
`DifficultyChangeWeight = 0` ALONE is no longer sufficient once `DifficultyReversionWeight` exists as a
separate restoring force (FSRS-6's own `w7`, default `0.001`, never exactly zero); the inert options record
is derived from the live one via a record `with` expression, so every OTHER constant is identical by
construction, confirmed below.

**This section now records TWO runs, not one — the first found an apparent exact-zero difficulty effect
that turned out to be a defect masquerading as a null, and a fix landed between them.** Read in order: the
cut (applies to both runs identically), the confound controls (both runs), the first run's exact-zero
finding and the diagnostic that showed it was not a real null, the fix, the second diagnostic that confirmed
the fix worked, and the re-measured result. The ranking finding was never in question and reproduces
unchanged across both runs.

### The cut, disclosed plainly — identical for both runs

The design's own worked example sizes this sweep at up to **`N = 30`** seeds over 6 shapes — with four arms
now instead of two, a full run is on the order of 45 minutes end to end, which does not fit inside a single
foreground command in this session. **The seed ceiling was cut to `N = 8` for both runs below — nothing
else.** All 6 corpus shapes ran, all 4 arms ran, the pilot (5 seeds) is the real, unmodified design
(`PilotSeedCount = 5`, unchanged), and the shipped tool's own `MaxSeedCount` constant is `30` again as
committed — the cut was to each run's own ceiling, not to what the tool ships. Both runs used the identical
seed set: `12345, 12346, 12347, 12348, 12349, 12350, 12351, 12352`.

- **First run (pre-fix):** pilot 218.9s (120 combinations, 22-way parallel) + scale-up 174.0s (72
  combinations) = **393.0s total**. Pilot SD (worst case across all three paired effects) **0.2608**, found
  on `low-reuse`/`topical`/`RankingEffect`, implying a derived **`N = 54`** — an even larger disclosed
  shortfall against the achieved `N = 8` than the historical `35`-derived/`30`-run precedent.
- **Second run (post-fix, re-measurement):** pilot 306.9s + scale-up 195.4s = **502.4s total**. Pilot SD
  **0.2702**, same worst cell, implying **`N = 58`** — consistent with the first run's sizing; the fix did
  not change how noisy the ranking effect is.

**How the shortfall should be read, per finding.** The ranking effect survives it: an effect of `+0.4` to
`+0.75` with intervals of `±0.05` to `±0.22` is not marginal at any achievable `N` — a larger `N` would
tighten the interval, not change the verdict. The difficulty effect is the one the shortfall genuinely
limits **in what it can prove statistically** — "no detectable effect at `N=8`" can only ever rule out a
LARGE effect, never confirm a true zero — which is exactly why the diagnostic below, not the seed count, is
what makes the difficulty finding decisive rather than merely underpowered.

### The four confound controls — still reading real fields, on both runs

All confirmed from what was ACTUALLY constructed (192 engines: 8 seeds × 6 shapes × 4 arms, per run), never
from an ingredient variable, exactly as every prior round of this measurement required — Plan 5's renames and
Tasks 1–3's changes to the curve, the state record and the store did not silently degrade any of them, and
neither did the neutral-difficulty fix between the two runs:

- **C0** (declared vs observed write/query order): **192/192** replays matched, both runs.
- **C1** (no retention policies) / **C3** (undamped `PerWriteAgePolicy` clock): confirmed uniform on all
  192 engines, both runs.
- **C2 (ranking)** — `RelativeFloor` equalized at `0.020` on both ranking arms, confirmed on all 192 engines,
  both runs.
- **C2 (difficulty)** — two checks, both read back from the ACTUAL `DsrOptions` each constructed engine's
  curve held, not from the two records `RunAsync` built: (1) every Live-arm engine (96 of them) carried
  `DifficultyChangeWeight = 3.0194` / `DifficultyReversionWeight = 0.001` (FSRS-6's own defaults) and every
  Inert-arm engine (96) carried `0`/`0`; (2) every OTHER `DsrOptions` constant — including the new
  `NeutralDifficulty = 5` after the fix, identical on both curve instances since it is not one of the two
  axes under comparison — read IDENTICAL between the Live and Inert curve on every engine. The difficulty
  axis is confirmed to be the ONLY thing that differs between the two curve instances, on both runs; the
  process exited `0` both times.

### First run: an exact-zero result that needed checking, not accepting

**`DifficultyEffect` (mean over ranking arms of Inert-`MissRate` minus Live-`MissRate`; positive = Live
better) measured EXACTLY `0.000 ± 0.000` on every single `(shape, class)` cell in the grid — all 6 shapes,
all 5 classes, `n=8` each, zero seed-to-seed variance, not merely a wide CI spanning zero.** Exact equality
with zero variance is not the signature of "a small real effect" — it is the signature of two arms
**computing identically**. The C2 confound control already proved the two arms are **configured**
differently; it does not prove they **behave** differently, and that gap needed a direct check.

**Diagnostic (2026-08-11, one shape — `baseline`, seed `12345`, difficulty-live only, otherwise identical
construction to the sweep's own Live arm — 263 writes, 145 queries, 1360 logged review rows via
`IMemoryGraphStore.ReviewsAsync`, before the fix):**

- **The derived grade is overwhelmingly Easy.** Of the 615 rows with a non-null grade (the other 745 hit the
  Δt=0 bypass, Task 2's own I1 fix — same-position touches within a burst, 54.8% of ALL touches), the mean
  grade was **3.8079** (range `[2,4]`, neutral `3`) and **89.6% fell in `[3.5, 4.0]`** (Easy-leaning); only
  6.5% Hard-leaning. This is exactly what the corpus's own reuse-batch structure produces: a successful
  recall tends to be a FRESH one, so retrievability at recall tends to be high.
- **Difficulty was pinned at the neutral floor (`1.0`, the OLD neutral) for the population the metric
  actually measures.** The 5 most-reinforced nodes in this one shape (132, 132, 128, 123, 118 touches each —
  exactly the reuse-heavy entries `topical`/`hot-ephemeral (in-window)` sample from) sat at
  `PreDifficulty = PostDifficulty = 1.000000` for essentially their entire trajectory. Two of the five showed
  one brief excursion (a Hard-leaning first touch nudging difficulty to `1.365` or `1.438`) that snapped
  straight back to exactly `1.0` on the very next (Easy-graded) touch.
- **Not a corpus-wide pin — `PostDifficulty` reached `3.060304` SOMEWHERE in the 1360 rows** — so difficulty
  could move for a lightly-touched entry. The defect was specifically that it did not move, in practice, for
  the HEAVILY-REUSED entries the measured classes sample from.

**Diagnosis: Case 1 of three possible readings — difficulty never meaningfully moved for the population this
measurement's own metric discriminates on, and that was a defect in the adaptation, not a null about the
model.** The root cause: `MemoryDecayState.Difficulty` defaulted to `1` for an unjudged entry — but `1` is
FSRS's EASIEST value, not "no information". Starting there, and with grades overwhelmingly Easy-leaning
(above), the update law's own linear damping computed a value below the floor almost immediately, and
`Math.Clamp(_, 1, 10)` floored it right back — the axis was structurally incapable of moving away from the
floor for an entry whose recalls keep landing Easy, which a fresh/reused entry's recalls generally do.

### The fix (owner-directed): the neutral value moves from the floor to the mid-point

**Initialise neutral difficulty at the SCALE'S MID-POINT (`5`), not its floor (`1`) — a stated choice, not a
derivation, since this library has no first rating to derive FSRS's own `D0` from** (the same honesty the
`r = 0.5` half-life anchor's own divergence from FSRS's 90% convention already discloses). Changed together,
so the signal layer and the state layer cannot drift apart again the way they did unnoticed the first time:
`MemoryDecayState.Difficulty`'s own default, `MemorySignals.Difficulty`'s fallback AND its non-finite
coercion, `GraphNode.Difficulty`/`GraphTouch.Difficulty`'s own defaults, and a new
`DsrOptions.NeutralDifficulty` (default `5`, guarded to `[1, 10]`) that `DsrRetrievability.Reinforce`
substitutes for a non-finite incoming `Difficulty` — the same substitution pattern `InitialStability`
already uses for a non-positive `Stability`. **The clamp bounds (`[1, 10]`) and the `g = 2 + 2·r` grade
mapping are UNCHANGED** — verified against FSRS's primary sources the round before this one; the defect was
the starting point, not the transfer function. An EXPLICIT out-of-range judgement still clamps to its
nearest bound exactly as before (`0`/`-5` still read as the floor `1`, `1e9` still reads as the ceiling
`10`) — only "no information at all" moved. A row written under the OLD neutral keeps its stored `1` as a
historical fact; it is not migrated or reinterpreted.

**Re-confirmation diagnostic (same shape/seed/construction as the first, after the fix):** difficulty now
starts at `5` and genuinely moves through a real range. `NodeId=44`'s own trajectory: touch 1
`5.000000 → 5.196252` (Hard-leaning, moves UP), touch 2 `5.196252 → 3.719134` (Easy, falls), touch 3
`3.719134 → 1.709881` (Easy, falls further), touch 4 `1.709881 → 1.000000` (Easy, reaches the genuine FSRS
floor), then stays at the floor for the remaining ~125 touches (grades stay overwhelmingly Easy, and once
genuinely AT the floor an Easy grade cannot push it lower). `PostDifficulty` across all 1360 rows now ranges
`[1.000000, 6.138049]` (was `[1.000000, 3.060304]`) — wider in both directions, confirming real movement.
**Landing at the floor after a few touches is now a legitimate outcome of the model** (an entry that keeps
getting recalled fresh really is "easy" under this corpus's own recall pattern), not an artifact of having
started there — the grade distribution is unchanged by the fix (mean `3.806`, `89.1%` Easy-leaning, matching
the first diagnostic within noise), confirming `DerivedGrade` itself was never touched.

### Second run: difficulty now varies — and still shows no detectable effect on recall quality

**`DifficultyEffect` no longer reads exactly zero anywhere — small, seed-varying point estimates appear in
every shape (`baseline`/`topical`: `+0.013 ± 0.032`; `low-reuse`/`topical`: `+0.006 ± 0.035`; `many-candidates`/
`topical`: `-0.003 ± 0.006`; and so on) — but EVERY cell's 95% CI still spans zero.** This is the qualitatively
different result the diagnosis above predicted: not "two arms computing identically" (zero variance, as
before) but "two arms computing similarly, with real per-seed variation neither large nor consistent enough
to distinguish from noise at `N=8`." **This is Case 2 of the three possible readings: the axis genuinely
varies (confirmed directly, not inferred) and, on this corpus, that variation does not produce a detectable
change in recall quality.** That is a real and legitimate finding, and a different one from Case 1 — an axis
that cannot vary is a defect; an axis that varies and does not matter here is a property of this corpus (or
of how small the compounding effect stays once the entry reaches the floor and only a fraction of its own
touches occur before that point), reported exactly as measured rather than pushed toward either a positive or
a null conclusion.

### Ranking: `topical` favors RRF strongly and consistently, on the ONE curve that ships today — reproduces after the fix

**`RankingEffect` (mean over difficulty arms of Multiplicative-`MissRate` minus RRF-`MissRate`; positive = RRF
better) on `topical` — the class the previous round flagged as the known defect, first run then second run:**

```
Shape            First run (pre-fix)              Second run (post-fix)            n
baseline         +0.509 ± 0.097  RRF better        +0.506 ± 0.106  RRF better       8
low-reuse        +0.238 ± 0.236  RRF better        +0.431 ± 0.220  RRF better       8
high-reuse       +0.536 ± 0.088  RRF better        +0.531 ± 0.063  RRF better       8
high-noise       +0.413 ± 0.144  RRF better        +0.413 ± 0.144  RRF better       8
many-candidates  +0.719 ± 0.055  RRF better        +0.746 ± 0.051  RRF better       8
rare-critical    +0.503 ± 0.069  RRF better        +0.492 ± 0.073  RRF better       8
```

RRF beats Multiplicative on `topical` in **every one of the six shapes, in BOTH runs**, clearing the ±0.10
action threshold with a CI excluding zero every time. The small point-estimate movement between runs (same
seeds, same shapes) is exactly what a now-genuinely-varying difficulty axis should produce — MissRate itself
shifts by a few points once Live and Inert stop computing byte-identical trajectories — and it changes no
verdict anywhere. This is a DIFFERENT comparison from the historical `topical` finding earlier in this
document (that one held the ranking policy fixed at `Multiplicative` and varied the CURVE; this one holds
the curve fixed at `Dsr` and varies the RANKING policy) but it is convergent evidence for the SAME mechanism
the design spec named before this measurement ran: `MultiplicativeRankingPolicy` rewards raw reinforcement
magnitude, and `topical`'s own reuse-batch entries are exactly where that reward is largest. Under
Multiplicative, DSR's own `topical` `MissRate` runs `0.950`–`1.000` across every shape; under RRF, the same
curve's `topical` `MissRate` runs `0.207`–`0.588` — still often poor in absolute terms, but a large,
consistent, same-direction improvement everywhere, unaffected by the difficulty fix.

### Every class, not only `topical` — read this before citing the number above alone

**`all (combined)` also favors RRF in every shape** (`+0.11` to `+0.45` across the two runs, all "RRF
better") — unlike the historical curve-vs-curve comparison, this pooled number is NOT hiding an
equal-and-opposite effect on `hot-ephemeral (in-window)` the way the old one was: that class's own
`RankingEffect` is mostly a NULL (CI spans zero) across shapes, with one exception (`low-reuse`,
Multiplicative better, both runs) that does not generalize to the other five shapes. `critical-rare` and
`hot-ephemeral (stale)` measured **exactly `0.000 ± 0.000` on the ranking axis too**, in every shape, both
runs — the same structural insensitivity the historical measurement found on the curve axis. **The pooled
`all (combined)` row here is a diluted, not a cancelled, echo of `topical`'s own effect** — safer to read
alone than the historical pooled row was, but still not a substitute for the per-class breakdown.

### The bottom line this section adds to the record

- **Difficulty now genuinely varies, confirmed directly** — the mapping/clamp defect that pinned it at the
  floor for the reused population is fixed (neutral moved from `1` to `5`, a stated choice). **It still shows
  no detectable effect on this corpus's recall quality at `N=8`** — a real, legitimate Case-2 finding
  (varies, does not detectably matter here), reported exactly as measured, and a materially different claim
  from the first run's Case-1 finding (could not vary at all). Neither result argues for reverting the
  difficulty-live change; the fix corrects a genuine defect in the adaptation regardless of whether difficulty
  ever moves `topical`'s own number.
- **The ranking default is a live question again, not a settled one, and this holds regardless of the
  difficulty fix.** `ReciprocalRankFusionPolicy` measurably and consistently outperforms
  `MultiplicativeRankingPolicy` on `topical` — the very class D49 ships as a known, prioritized defect —
  under the curve that actually ships today, in every shape tested, in both runs. This does not by itself
  justify changing the default (`critical-rare`'s own zero-effect floor, and `hot-ephemeral (in-window)`'s
  one reversed shape, mean the trade is not uniformly one-sided), but it is a materially stronger signal than
  the `critical-rare` proposal the predecessor measurement offered, and it belongs on the table the next time
  the ranking default is reconsidered.
- **Neither finding changes a shipped default.** Both are reported for the owner's own decision, per this
  plan's own scope (`local/superpowers/specs/2026-08-10-fsrs-properly-design.md` §8).

## Where the SQLite requirement, and the regression guard, fit

The sweep is deliberately SQLite-backed (`Lyntai.Storage.Sqlite.SqliteMemoryGraphStore`) rather than
in-process: `MemoryCorpus`'s own query text is built to be found by TOKEN matching, and
`Lyntai.Storage.InMemory.InMemoryMemoryGraphStore` matches only by contiguous substring — tried against this
corpus it structurally fails to gather almost every candidate, a store-matching mismatch, not a policy
signal. `tests/Lyntai.Tests/Memory/MemoryDefaultRecallQualityTests.cs` is the regression guard: it now pins
*today's shipped defaults* (`MultiplicativeRankingPolicy` + `DsrRetrievability`, re-baselined 2026-08-10
alongside the default change itself — see the test's own class doc for the old HalfLife-paired numbers kept
alongside the new ones) against one fixed corpus point, using the same SQLite-backed construction. **It
answers "did we break the default," never "which default is best"** — that is this document's question, not
the guard's.

## Whether the SQLite FTS seed branch needs a real salience term

Unchanged from the predecessor measurement, and still unanswered by this one: salience was held OFF
throughout (no retention policies registered), which is exactly the FTS seed branch's own salience
tiebreak (`ORDER BY bm25(…), salience DESC, id DESC`) — neither sweep ever exercises that ordering with a
non-neutral salience value, so neither has anything to say about whether the tiebreak needs a richer term.
Closing this needs its own measurement with salience varied deliberately.
