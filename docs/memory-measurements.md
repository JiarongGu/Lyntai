# Memory measurements — every arm, and whether its number still holds

> **This is the measurement half of [`docs/memory.md`](memory.md), split out on 2026-09-10.** The
> contract — what the engine is, how a recall works, how to configure it — stayed there; this file is
> the evidence. The section keeps its number, and `memory.md`'s §6–§10 were deliberately NOT pulled up
> into the hole: a renumbered `§` resolves **silently to the wrong section**, which is the one
> documentation failure no gate can see (`docs/DECISIONS.md` **D114**).

> **The NAME is narrower than the contents, deliberately.** Every result here was the memory subsystem's
> until 2026-09-12; two now are not — `decision-shape-single-evidence` prices a forced choice and
> `affordance-prompt-protocol-4b` prices `IToolLoop`, which lives in `Lyntai.Agents`. They are here because
> **this is the repository's only gated measurement record** (`check-measurements` reads one path) and
> because both measure a SHAPE the memory seams also hand a model, so a reader comparing them needs one
> index rather than two. **Splitting is the move to avoid, not to plan**: it turned eight bare `§5`
> citations into silent lies in one commit, which is why **D114** exists. Rename the file before splitting
> it, and only when the non-memory half is large enough to mislead.

**Read the index before quoting anything below it.** This record RETRACTS INLINE — a figure published
in one section is corrected three sections down, mid-paragraph — so a section read on its own can be
confidently out of date. The index is generated from a marker on each result and carries the two things
a reader most often gets wrong here: whether the arm measured is the one that **ships**, and whether the
figure is still current.

<!-- results:begin — GENERATED. Edit the per-result `result:` markers, never this index. -->

## Results — 112 measured: 83 current · 2 superseded · 27 retracted

_Generated from the per-result `<!-- result: … -->` markers by
`node devtools/dev.mjs check-measurements --write`. Edit a marker, never this table._
_The two columns on the right are the two questions a cold-start probe could not answer from this
record. `SUPERSEDED` is never written by hand — it is derived from another row's `supersedes=`, so the
losing half of a pair cannot be the half nobody revisits. Line numbers are for JUMPING: the section
itself carries the caveats, and no figure here is quotable without them._

**16 of 112 rows measure the arm that actually SHIPS.** Every other row is a ladder
rung, a ceiling, an oracle or a baseline — reading one as a configuration recommendation is the
mistake this column exists to prevent.

| line | arm | metric | n | value | ships | status |
| ---: | --- | --- | ---: | ---: | :---: | --- |
| 166 | shipped limit 10, model-free ranking (each … | miss-decomposition | 140 relevant entr… | 100.0% | **ships** | CURRENT |
| 182 | `gemma3:4b` judge via `AddMemoryVerificatio… | miss | unstated | 0.2571 | — | CURRENT |
| 231 | `gemma3:4b` on the Japanese hard case (one … | screen-verdict | 1 call per model,… | `[3,4]` — answe… | — | CURRENT |
| 256 | `gemma3:4b` resident as a judge co-tenant o… | vram-resident | unstated | ~4.4 GB residen… | — | CURRENT |
| 335 | `+sem+rel-only+hl512+rerank` — `bge-reranke… | evidence-hit@k | 200 | +5.0 | — | CURRENT |
| 351 | `+sem+rel-only+rerank` at the shipped `Head… | evidence-hit@k | 200 | 78.0% | — | RETRACTED |
| 379 | `lyntai+hl512+rerank` — same `bge-reranker-… | prefers-current | 70 knowledge-upda… | 86.8% (59/68) | — | CURRENT |
| 411 | `+sem+rel-only+judge+top20` — the same 4B j… | evidence-hit@k | 200 | 71.0% | — | CURRENT |
| 446 | `LAMAR-600m` Q8_0 (2026-07) as reranker, ag… | evidence-hit@k | 200 | 91.0% | — | CURRENT |
| 476 | `jina-reranker-v1-tiny-en` Q4_K_M (gpustack… | screen-verdict | 1 query × 4 docum… | 8/8 checks | — | RETRACTED |
| 484 | the same three GGUFs on `cross-encoder/ms-m… | screen-verdict | 1 query × 2 docum… | control 10/10; … | — | CURRENT |
| 575 | `cross-encoder/ms-marco-MiniLM-L6-v2`'s own… | screen-verdict | 1 query × 2 docum… | fp32 reproduces… | — | CURRENT |
| 621 | `ms-marco-MiniLM-L6-v2` int8 and fp32 ONNX … | evidence-hit@k | 200 | +3.0 (both expo… | — | CURRENT <!-- drift-ok: carried from this result's own line --> |
| 667 | `LAMAR-600m` Q5_K_M on one `llama-server --… | screen-verdict | 6 calls of 4 docu… | 9.42e-3 max dri… | — | CURRENT |
| 684 | the shipped `LlmMemoryVerificationPolicy` o… | endorsement-rate | 70 knowledge-upda… | 36.2% | — | CURRENT |
| 777 | `SalienceWeight = 0` (now the shipped defau… | miss | 10 seeds; 10/10 l… | −0.0530 | **ships** | CURRENT |
| 817 | the FIRST VERSION of the same sweep (verdic… | miss | unstated | 5/5 shapes bett… | — | RETRACTED |
| 829 | shipped salience vs a `SalienceOff` control… | miss | 30 seeds × 6 shap… | +0.0384 | — | RETRACTED |
| 934 | `NW1.5` (shipped `NoveltyWeight = 1.5`) vs … | miss | 30 seeds × 6 shap… | +0.0018 | **ships** | CURRENT |
| 972 | `NW0.5` — the best rung under `nomic-embed-… | miss | 30 seeds × 6 shap… | −0.0116 | — | RETRACTED |
| 998 | `lyntai` (shipped defaults) | evidence-hit@k | 200 | 54.5% | **ships** | CURRENT |
| 1067 | `lyntai` (shipped defaults) as FIRST publis… | evidence-hit@k | 200 | 11.0% | — | RETRACTED |
| 1180 | `+sem+rel-only` (semantic seeds on, `Retrie… | evidence-hit@k | 200 | 63.5% | — | SUPERSEDED by L1271 |
| 1261 | `+forget0+oracle` (PERFECT judge) multi-hop… | evidence-hit@k | 200 | 16 points | — | RETRACTED |
| 1271 | `+sem+rel-only` under per-source seed fusio… | evidence-hit@k | unstated | 83.0% | — | CURRENT |
| 1345 | ranking × walk interaction, `lyntai` → `lyn… | token-f1 | 1,540 | +1.0 | — | CURRENT |
| 1347 | the same ranking × walk interaction measure… | token-f1 | 100 | +6.7 / +4.2 / +… | — | RETRACTED |
| 1546 | shipped headline derivation — `MemoryHeadli… | marker-survival | 5,882 LoCoMo turns | 5,882 of 5,882 … | **ships** | CURRENT |
| 1584 | fixed-slot content contrast `lyntai-2shot` … | token-f1 | 1,540 | +1.1 | — | RETRACTED |
| 1630 | `lyntai-fused-full` — `lyntai-fused`'s own … | token-f1 | 200 | +11.7 | — | CURRENT |
| 1680 | `lyntai-fused-full`, multi-hop category cel… | token-f1 | 200 (multi-hop ce… | 38.0 vs 37.8 | — | RETRACTED |
| 1692 | `lyntai-fused-3shot-full` (three-shot walk … | token-f1 | 1,540 | −0.4 | — | CURRENT |
| 1731 | `lyntai-fused-3shot-full` (+ the walk, 39.7… | token-f1 | 200 | +0.9 | — | RETRACTED |
| 1774 | `lyntai-fused-api` (fused ranking + `Memory… | token-f1 | 1,540 | −0.4 | — | CURRENT |
| 1829 | `+sem+rel-only` (best mechanical: semantic … | evidence-hit@k | 1,540 | 82.6% | — | CURRENT |
| 1863 | `+forget0+oracle` (PERFECT judge, no semant… | evidence-hit@k | 1,540 | 74.6% | — | RETRACTED |
| 1896 | `+sem+rel-only` (the arm that wins LoCoMo) … | prefers-current | 70 knowledge-upda… | −37.1 | — | CURRENT |
| 1979 | `+sem+forget2` | evidence-hit@k | 200 | 69.5% | — | CURRENT |
| 2033 | `+sem+forget0.5` — the top rung of a six-po… | evidence-hit@k | 200 | 80.5% | — | CURRENT |
| 2078 | `+sem+rel-only+oracle` (a PERFECT judge — a… | evidence-hit@k | 200 | 92.5% | — | CURRENT |
| 2114 | `+sem+rel-only+judge` (gemma3:4b, shipped `… | evidence-hit@k | 200 | 72.5% | — | SUPERSEDED by L2189 |
| 2189 | `+sem+rel-only+judge@40` (depth 40 = 2×) | evidence-hit@k | 200 | 84.0% | — | CURRENT |
| 2236 | `+sem+rel-only+judge+fuse` (same judge, sam… | evidence-hit@k | 200 | 83.0% | — | CURRENT |
| 2330 | `+sem+rel-only+judge+budget5` | evidence-hit@k | 200 | 76.5% | — | CURRENT |
| 2385 | `extract+forget0` — strong-CLI extracted fa… | prefers-current | 70 knowledge-upda… | 52.9% | — | CURRENT |
| 2466 | `shot-2` on the `--haystack` variant (61,18… | all-evidence-recall | 125 of 133 multi-… | +4.8 | — | CURRENT |
| 2492 | `shot-3` on the ORACLE variant | all-evidence-recall | 125 of 133 multi-… | +6.4 | — | RETRACTED |
| 2506 | `extract+forget0` — `gemma3:4b`-extracted f… | prefers-current | 70 knowledge-upda… | 53.0% | — | CURRENT |
| 2547 | `extract+reconcile` — ADD/UPDATE/DELETE at … | prefers-current | 25 | 75.0% | — | RETRACTED |
| 2622 | `+sem` — decay ON (the one-knob partner of … | prefers-current | 70 questions (68 … | 72.5% | — | CURRENT |
| 2665 | `RetrievabilityWeight` = 1 — shipped | recovery@k | 26 buried entries… | 100.0% | **ships** | CURRENT |
| 2698 | the FIRST `--recover` run — `page@10` repor… | recovery@k | unstated | 'decay deletes … | — | RETRACTED |
| 2718 | `+forget4` — `RetrievabilityWeight` walked … | prefers-current | 70 questions, of … | 100.0% | — | CURRENT |
| 2739 | `lyntai`, haystack variant (model-free, k =… | prefers-current | 70 knowledge-upda… | 86.4% | **ships** | CURRENT |
| 2810 | `lyntai` vs `vector` on LoCoMo, shared-stor… | evidence-hit@k | unstated | −49.5 | — | RETRACTED |
| 2850 | `lyntai` shipped ranking observed by the `-… | rrf-score-separation | 25 questions, sam… | −29% | **ships** | CURRENT |
| 2896 | K = 120 (RRF ladder rung), haystack | current@k | 25 questions | 0.0 points | — | RETRACTED |
| 2922 | K = 60 (shipped) | evidence-hit@k | 200 LoCoMo questi… | 54.5% | **ships** | CURRENT |
| 2969 | `shot-1` (single recall, no expansion), Lon… | clean | all 70 questions | 31.4% | **ships** | CURRENT |
| 3001 | `shot-1` on the 25-question sample of the s… | clean | 25 questions | 40.0% | — | RETRACTED |
| 3007 | `GraphMemoryOptions.ExpansionRetrievability… | clean | 25 questions | +4.0 | — | RETRACTED |
| 3087 | `shot-2` on LoCoMo, shared-store run (pre-i… | evidence-hit@k | 200 questions | +6.0 | — | RETRACTED |
| 3113 | `shot-1` (one-shot recall, shipped `k = 10`… | clean | 70 | 31.4% | — | CURRENT |
| 3178 | `fill` (`k = 80`, engine's own `CharBudget`… | clean | 70 | 57.1% | — | CURRENT |
| 3220 | `fill` (`k = 80`) latency in the run that p… | latency | unstated | 14.7 seconds | — | RETRACTED |
| 3224 | `pool-16` (`GraphMemoryOptions.CandidateMul… | clean | 70 | 58.6% | — | CURRENT |
| 3264 | `CandidateMultiplier` 4 (shipped) → 16, on … | all-evidence-recall | unstated | −28.0 | — | CURRENT |
| 3290 | shipped `CandidateMultiplier = 4` (the `lyn… | evidence-hit@k | 200 | 54.5% | **ships** | CURRENT |
| 3344 | `+sem+rel-only+oracle+pool16` (×16 pool = 3… | evidence-hit@k | unstated | 96.0% | — | CURRENT |
| 3370 | `+oracle+pool32` (640 candidates, larger th… | evidence-hit@k | unstated | 100.0% | — | RETRACTED |
| 3383 | `Partition` (shipped) against `+oracle+fuse… | evidence-hit@k | 200 | +2.0 | — | CURRENT |
| 3409 | `ExpansionRetrievabilityFloor = 0.8`, knowl… | clean | 70q (knowledge-up… | +2.8 | — | CURRENT |
| 3519 | shipped novelty salience policy at the ship… | miss | 10 seeds × 4 shap… | 0.710 | **ships** | CURRENT |
| 3558 | `SalienceContext.SimilarCount` on authored … | separability-auc | 5 recurrence / 5 … | AUC = 1.000 | — | CURRENT |
| 3590 | `recurrence` population mean `SimilarCount`… | similar-count | one probe per pop… | 6.00 | — | RETRACTED |
| 3611 | `sum` — Σ r(m) — over an unbuilt gist tier,… | regime-picks | 600 replays = 60 … | phase A 300/300… | — | CURRENT |
| 3642 | `count@θ`, θ = 0.9 read as one of the two p… | regime-picks | 600 replays = 60 … | phase B 300/300… | — | RETRACTED |
| 3723 | `count@0.9` across `RoutineCount` rungs 3 /… | regime-picks | 2400 replays = 60… | tie 185 → A 175… | — | CURRENT |
| 3763 | `mean` — Σ r(m)/n — under `bulk` with `Corp… | regime-picks | 2400 replays per … | B 300/300 at se… | — | CURRENT |
| 3817 | reinforce on **expansion only** (the shippe… | miss | unstated | 0.4429 | — | CURRENT |
| 3834 | shipped novelty-driven salience policy, iso… | miss | unstated (the `ma… | −0.0786 | **ships** | CURRENT |
| 3877 | `shot-2` on the `--haystack` variant, `sing… | all-evidence-recall | 64 of 70 single-s… | +0.0 | — | CURRENT |
| 3920 | `+sem+rel-only+judge` with `gemma-3-1b-it` … | evidence-hit@k | 200 | 83.0% | — | CURRENT |
| 3964 | `rerank` — the bench-local `CrossEncoderVer… | latency | 50 writes + 50 re… | 94.5 ms recall … | — | CURRENT |
| 4063 | `+sem+rel-only+judge` — the SHIPPED `Memory… | token-f1 | 301 questions sam… | −0.0346, CI [−0… | **ships** | CURRENT |
| 4106 | `+sem+rel-only+judge+enginefuse` — the same… | token-f1 | 301 questions sam… | +0.0295, CI [+0… | — | CURRENT |
| 4177 | the first `memory-decision` grid — gold tak… | forced-choice-accuracy | 200 trials x 5 li… | rerank 53.5% at… | — | RETRACTED |
| 4185 | the same grid's generative tie counts, take… | forced-choice-accuracy | 200 trials x 5 li… | score-4b 100/20… | — | RETRACTED |
| 4198 | `rerank` — a 468,393,760 B cross-encoder ar… | forced-choice-accuracy | 261 trials x 5 li… | 72.0% vs 71.5% … | — | CURRENT |
| 4248 | `score-4b` — how often a generative 0-100 s… | forced-choice-accuracy | 261 trials x 5 li… | score-4b ties o… | — | CURRENT |
| 4301 | `loop-4b` — a 2,489,757,856 B instruct mode… | forced-choice-accuracy | 168 trials x 5 ro… | 86.3% vs 81.0% … | — | CURRENT |
| 4314 | `loop-1b` — a 806,058,240 B instruct model … | forced-choice-accuracy | 168 trials x 5 ro… | 10.1% at N=3 fa… | — | CURRENT |
| 4351 | `loop-4b` on 20 requests NO tool in the ros… | false-call-rate | 20 negative reque… | 90-95% shipped;… | **ships** | CURRENT |
| 4385 | four sub-100 MB GGUF embedders — `all-MiniL… | screen-verdict | 1 known-similar/k… | 4 of 4 HEALTHY;… | — | CURRENT |
| 4415 | `cosine-minilm` — a 25,008,064 B `all-MiniL… | forced-choice-accuracy | 168 trials x 5 ro… | 78.6% at N=3 an… | — | CURRENT |
| 4496 | `potion-base-8M` (30,236,760 B, a CPU looku… | evidence-hit@k | 200 | 54.0% against 5… | — | CURRENT |
| 4541 | the `model2vec`/`potion` static family — a … | forced-choice-accuracy | 168 trials x 5 ro… | potion 52.4-70.… | — | CURRENT |
| 4573 | subtracting the corpus centroid before cosi… | forced-choice-accuracy | 168 trials x 5 ro… | +8/+6/+3 for th… | — | CURRENT |
| 4599 | `lyntai-fused-full` against `lyntai-fused` … | token-f1 | 61 questions x 2 … | completeness +7… | **ships** | CURRENT |
| 4649 | `nomic-embed-text-v1.5` Q8_0 (146,146,432 B… | token-f1 | 61 questions samp… | every arm withi… | — | CURRENT |
| 4698 | `loop-tool-native` — `qwen2.5-0.5b-instruct… | forced-choice-accuracy | 168 trials x 5 ro… | -2.4 to -9.6 po… | — | CURRENT |
| 4706 | `qwen2.5-0.5b-instruct` Q4_K_M against `gem… | screen-verdict | 2 models x 3 tool… | qwen 6/6 emit t… | — | CURRENT |
| 4783 | `loop-tool-native` against `loop-tool`, the… | false-call-rate | 20 negative reque… | 20-30% native a… | — | CURRENT |
| 4820 | `full` — the shipped recall at k = 10 and t… | clean | 70 questions, hay… | -11.4 at 1,200;… | — | CURRENT |
| 4876 | `full` — the shipped recall asking for Memo… | clean | 70 questions, hay… | a monotone rise… | — | CURRENT |
| 4922 | `full` — the shipped recall asking for Memo… | all-evidence-recall | 132 questions, ha… | negative at eve… | — | CURRENT |
| 4965 | `cosine` — a 333,590,944 B embedder scoring… | forced-choice-accuracy | 168 trials at eac… | 96.4 percent at… | — | CURRENT |
| 5002 | the SHIPPED `LlmMemoryAnnotationPolicy` aga… | drift-rate | 24 eligible facts… | 41.7% to 87.5%;… | — | RETRACTED |
| 5044 | a pure-CODE reconciler over the SAME model … | drift-rate | 24 eligible facts… | no change in 5 … | — | CURRENT |
| 5083 | the same three models and fixture through t… | drift-rate | 20-24 eligible fa… | 83.3% to 90.5% … | — | CURRENT |
| 5110 | a pure-CODE recency rule — carry the PREVIO… | drift-rate | 20-24 eligible fa… | gap 0: 87.0% → … | — | CURRENT |
| 5140 | a bench-local selective annotator — number … | drift-rate | 4-24 eligible fac… | 0.0% drift at 1… | — | CURRENT |

<!-- results:end -->

## 5. What was measured, and what it says

Most of what follows is on this repository's own deterministic corpus, replayed against a live engine. It is
a **comparison instrument**, not a claim about your data: relevance in it is defined lexically, the shapes
are synthetic, and no arm exceeds a few hundred entries.

**The exceptions are the three sections measured on the FIELD's data** — LoCoMo and LongMemEval's two
classes. Those carry their own corpus, their own ground truth and their own caveats, which is the whole
reason they are worth running; each says so where it starts.

### The dominant defect is RANKING <!-- result: id=rank-miss-decomp-outranked-n140 arm="shipped limit 10, model-free ranking (each query replayed at the limit and again wide open)" metric=miss-decomposition n="140 relevant entries wanted; 75 missed at limit 10" value="100.0%" ships=yes status=CURRENT -->

Decomposing the misses — replaying each query at the shipped limit and again wide open:

| | count | share |
|---|---|---|
| relevant entries wanted | 140 | |
| missed at limit 10 | 75 | 53.6% |
| …of those, **never a candidate** | **0** | **0.0%** |
| …of those, **reachable but outranked** | **75** | **100.0%** |

**Every miss is a ranking failure.** None is retrieval or tokenization. That rules out — on evidence — the
things one reaches for first: a better tokenizer, more n-gram coverage, a semantic index. The answers were
already in the candidate set. And the two shipped model-free ranking policies return **byte-identical**
results, so there is no fix inside the library's own arithmetic (**D59**).

### A judge is the lever that remains <!-- result: id=judge-gemma3-4b-miss-lever arm="`gemma3:4b` judge via `AddMemoryVerification` (consulted before the limit is applied)" metric=miss n="unstated" value="0.2571" ships=no status=CURRENT -->

`AddMemoryVerification` shows a model the query and the candidate headlines *before* the limit is applied,
so a buried answer is promoted. Depth matters more than the judge: consulted after the cut it can only
observe.

| judge | miss | pollution | vs reference | per judgement | per 1,000 recalls |
|---|---|---|---|---|---|
| none | 0.5357 | 0.3331 | — | — | — |
| `llama3.2:3b` | 0.3643 | 0.176 | 60–69% | local | **$0** |
| `qwen2.5-vl:7b` | 0.3071 | 0.1556 | 91.4% | local | **$0** |
| `gemma3:4b` | 0.2571 | **0.0492** | 108–111% | local, ~1.5 s | **$0** |
| ground-truth reference | 0.2857 | 0.1549 | 100% | — | — |
| Claude Haiku | **0.1857** | 0.1271 | **140%** | $0.0661, 3.0 s | **$66** |
| Claude Sonnet | not measured | | | $0.2670, 2.8 s | **$267** |

**Cost is measured, and it changes the ranking.** The money columns are one `claude -p --output-format json`
call per model (2026-08-15) reporting the CLI's own `total_cost_usd`; the quality columns are full ceiling
runs. Sonnet's quality was not measured — at $267 per thousand recalls the question was answered before it
needed to be.

**A hosted judge through the `claude` CLI costs ~$0.066 per RECALL for Haiku.** This seam fires on every
recall, so a modestly-used store reaches real money quickly: the 140% quality is genuine, and so is being
**four times** the cost of Sonnet-class quality from a local model that already beats the reference. The
likely reason the figure is so high for a ~150-token prompt — inferred, not measured — is that `claude -p`
is a full Claude Code session carrying its own scaffolding, not a bare completion; **an API-backed provider
is the right transport for this seam, and the CLI is the wrong one.** The CLI arm exists to measure a
frontier judge's CAPABILITY on the same corpus, not to be deployed behind it.

**So the cost-effective answer is `gemma3:4b`**: it beats the ground-truth reference on both metrics, admits
the least junk of any judge measured, runs in ~1.5 s, and costs nothing. Haiku buys ~7 points of miss for
$66 per thousand recalls. Whether that trade is worth it is an application question — but it should be made
knowing the local arm already passes the reference.

Three things this table says that a single number would not:

- **The ground-truth arm is a REFERENCE, not a ceiling.** `gemma3:4b` beats it on both metrics. It promotes
  only strictly-relevant entries, leaving the rest of the limit to the noisy ranking, and it reinforces less
  — optimal per recall, not over the trajectory.
- **Newer beats bigger.** `gemma3:4b` (3.3 GB) beats `qwen2.5-vl:7b` (6.0 GB) on both.
- **The ranking is not one-dimensional.** Haiku finds the most answers; `gemma3:4b` admits the least junk.
  Which is "best" depends on which failure your application pays for.

**Avoid a *thinking* model here.** `qwen3:4b` spent ~25 s per judgement against gemma3's ~1.5 s — a seam in
the latency path of every recall makes that disqualifying whatever it scores. `TextRequest.Reasoning` asks a
backend to skip reasoning where it can.

Verified to judge correctly in **English, Chinese, Japanese and Korean**.

#### Screening every local model on hand <!-- result: id=judge-screen-gemma3-4b arm="`gemma3:4b` on the Japanese hard case (one call each, Ollama's `eval_count` / `total_duration`, 2026-08-15)" metric=screen-verdict n="1 call per model, 6 models" value="`[3,4]` — answer + distractor (11 output tokens, ~1.5 s)" ships=no status=CURRENT -->

Before a model earns a corpus run it has to survive one hard case: the Japanese question above, whose note 4
is a lexically adjacent distractor. One call each, Ollama's own `eval_count` and `total_duration`
(2026-08-15). This is a SCREEN, not a quality measurement — a corpus run is what produces the table above.

| local model | output tokens | latency | answer | verdict |
|---|---|---|---|---|
| `llama3.2:3b` | 6 | fast | `[1,2]` | **wrong** — misses the answer entirely |
| `gemma3:4b` | 11 | ~1.5 s | `[3,4]` | answer **+ distractor** |
| `qwen2.5-vl:7b` | 6 | 0.7 s warm | `[]` | **empty** — finds nothing |
| `qwen3:4b` | 2,214 | ~30 s | `[3]` | correct, **disqualified on latency** |
| `qwen3.5-abliterated:4b` | 2,727 | ~45 s | reasoning | **disqualified on latency** |
| `Qwen3.6-35B-A3B` (IQ4_NL) | 648 | **380 s** | reasoning | **spills VRAM — see below** |

**Not one fast local model answers this case exactly.** One is wrong, one over-selects, one finds nothing;
the models that reason are disqualified by the seam's own latency budget. `gemma3:4b` is the best of the
viable arms because it at least *finds* the answer — which is what the corpus table independently confirms.

**The 35B's 380 s is this MACHINE, not that model.** At 20 GB it does not fit the 12 GB of VRAM below, so it
spills to CPU; the figure measures the overflow, not the architecture, and A3B's small active-parameter count
never got a fair test. On a card that holds it the result could be entirely different. Recorded this way
because the opposite reading — "big MoE models are too slow for this seam" — is exactly the kind of
environment-blamed-on-model conclusion the rest of this document exists to avoid.

#### The machine these numbers come from <!-- result: id=judge-cotenancy-gemma3-vram arm="`gemma3:4b` resident as a judge co-tenant on the 12 GB RTX 4080 Laptop card (vs `gemma4:12b`)" metric=vram-resident n="unstated" value="~4.4 GB resident — ~7.8 GB free" ships=no status=CURRENT -->

Latency figures are meaningless without it, and the row above shows why: whether a model fits VRAM decides
its result more than its architecture does.

| | |
|---|---|
| CPU | Intel Core Ultra 9 185H — 16 physical / 22 logical cores |
| RAM | 63.7 GB |
| GPU | NVIDIA RTX 4080 Laptop — **12 GB VRAM** (driver 596.49) |
| OS | Windows 11 Pro 10.0.26200 |
| Runtime | .NET 10; `llama-server` (llama.cpp) is the standard server, Ollama 0.32.7 also present |

**Which server answered is part of the figure, and for everything published before 2026-09-08 the answer is
OLLAMA.** The benches defaulted to `11434` and printed only the model NAME, so no table said which of the
two running servers served it — the attribution was reconstructed afterwards from which processes were up
(`TASKS.md`, 2026-09-04). The default is now llama-server's own `8080` and **every sweep prints its
endpoint**, so a table taken from here on carries its own provenance. `repo-mechanics.md` §Local models.
<br>**It matters most for the MODEL name.** Ollama routes by it; a `llama-server` started with `--model`
serves one model and answers to its `--alias`, so re-running an Ollama-era figure against llama-server with
the same `LYNTAI_LIVE_EMBED_MODEL` string does NOT reproduce the embedder — it silently uses whatever was
loaded.
<br>**And the two servers disagree about an OVER-LONG input, which is how a second silent difference came
out.** Ollama truncates and answers; `llama-server` returns 500. LongMemEval's texts reach **76,560
characters against a median of 429**, so every figure taken here before 2026-09-08 embedded a quietly cut
tail — at whatever limit the answering server happened to be started with, which nothing recorded. The
benches now cut explicitly, shrink and retry on the server's own complaint, and **report the count in the
footer**. The affected tail is small (279 texts of 246,750 exceed 6,000 characters) so no published figure
is withdrawn, but a figure and its truncation regime are one fact, not two.

**The 12 GB ceiling is the load-bearing number.** Every model that fits it (`gemma3:4b` 3.3 GB, `qwen3:4b`
2.5 GB, `qwen2.5-vl:7b` 6.0 GB) was measured on GPU; the 20 GB MoE was not. A cost or latency figure here
transfers to another machine only in so far as the same fit/spill answer holds.

**"Fits" is the wrong test — the judge is a CO-TENANT.** A model may report `100% GPU` and still be the wrong
choice, because it has to share the card with everything else the deployment runs. Measured 2026-08-15:

| candidate | resident | headroom on a 12 GB card |
|---|---|---|
| `gemma3:4b` | ~4.4 GB | **~7.8 GB free** |
| `gemma4:12b` | 8.4 GB | **~0.8 GB free** — 93% of the card |

That matters here specifically rather than in the abstract: **semantic memory needs an embedder resident
too**. A judge holding 11.4 GB forces `nomic-embed-text` to swap in and out on every recall that also embeds
— thrashing that a benchmark of the judge ALONE cannot see, because it never has a second tenant. Read a
model's size against what else must be resident, not against the card.

**`gemma4:12b` was tested and rejected (2026-08-15), and it is the interesting rejection**: it answers the
Japanese screening case CORRECTLY — `[3]`, where `gemma3:4b` takes the distractor — so it is the more capable
judge. It is disqualified anyway, on two independent counts. It is a REASONING model (1,386 characters of
hidden `thinking` for a 29-character answer, ~270 tokens and 40–65 s per judgement), and it takes 93% of the
card. Accuracy is not the only axis when a seam sits in the latency path of every recall and shares a GPU.

#### The research says an LLM judge is the wrong tool for this shape <!-- result-free: Literature survey plus a design lead — the three findings are from the reranking literature and the only numbers restated (`qwen3:4b`'s 2,214 tokens, `bge-reranker-v2-m3`'s ~600–800 ms per ~30 pairs) are re-quoted from the screen above or from published builds; the section says the lead "is now MEASURED — see the next subsection". -->

What this seam actually does — score `(query, candidate)` pairs and reorder — is **reranking**, and the
reranking literature is unambiguous that a purpose-built **cross-encoder** beats an LLM at it on every axis
that matters here. Three findings line up exactly with what was measured above:

- **Cross-encoders match or beat LLM rerankers at far lower latency and cost.** A `SequenceClassification`
  cross-encoder scores a pair in ONE forward pass; an LLM judge decodes autoregressively, which is the whole
  of why `qwen3:4b` costs 2,214 tokens to answer a four-item question.
- **LLM judges are noisy in exactly the ways observed here** — inconsistent scores, missed documents, wrong
  ids, failures to score at all. `llama3.2:3b`'s wrong ids, `qwen2.5-vl:7b`'s empty verdict and `gemma3:4b`'s
  over-selection are three textbook instances, not three unlucky models.
- **Size does not predict reranker quality.** A 149M cross-encoder matches a 1.2B one, while
  `Qwen3-Reranker-4B` places fourth in the same comparison — the mirror of "newer beats bigger" above.

`bge-reranker-v2-m3` (568M) has ONNX builds and reranks ~30 pairs in ~600–800 ms on CPU — the same order as
`gemma3:4b`'s single judgement, for the whole candidate set rather than one verdict, deterministically and
free. At this engine's default `VerificationDepth` of 4× the limit that is one pass over ~40 candidates.

**This was a design lead rather than a shipped claim, and it is now MEASURED — see the next subsection.**
The lead's reasoning held: the seam's SHAPE — score pairs, reorder, never generate — is the shape rerankers
exist for, and the LLM judge is a general tool doing a specialised job. What the lead did not anticipate is
that the seam hands a policy a TRUNCATION, which is most of what a first run measured. ONNX turned out not
to be needed at all: `llama-server --reranking` serves the same model over HTTP.
See `local/superpowers/records/2026-08-15-memory-research-review.md`.

#### A cross-encoder is worth +5.0 — and the seam was starving it (`memory-locomo --retrieval`, 2026-09-08) <!-- result: id=locomo-rerank-hl512-n200 arm="`+sem+rel-only+hl512+rerank` — `bge-reranker-v2-m3` (568M, Q8_0) endorsing its own top-20 of 80 candidates" metric=evidence-hit@k n="200" value="+5.0" ships=no status=CURRENT supersedes="locomo-rerank-headline120-n200" -->

`bge-reranker-v2-m3` (568M, Q8_0) served by `llama-server --reranking`, endorsing its own top-20 of the 80
candidates the engine shows a verifier. n = 200, k = 20, seed 12345, embedder `embeddinggemma-300M-Q8_0`
via llama.cpp. Model-free scoring throughout.

| arm | overall | |
|---|---|---|
| `+sem+rel-only` (base) | 85.5% | |
| `+sem+rel-only+rerank` | **78.0%** | −7.5 |
| `+sem+rel-only+rerank+fuse` | 85.5% | the loss removed, nothing gained |
| `+sem+rel-only+hl512` (base, full turn) | 86.0% | the matched control |
| **`+sem+rel-only+hl512+rerank`** | **91.0%** | **+5.0** |
| `+sem+rel-only+oracle` | 92.5% | the ceiling |
| `vector` | 83.5% | the arm that cannot move |

**The first run refuted a PRE-REGISTERED prediction of 86–90% and the second explains why.** A verifier <!-- result: id=locomo-rerank-headline120-n200 arm="`+sem+rel-only+rerank` at the shipped `HeadlineChars = 120` — the reranker scoring headlines only" metric=evidence-hit@k n="200" value="78.0%" ships=no status=RETRACTED -->
receives `MemoryVerificationCandidate.Headline` and never `Content`, and `GraphMemoryOptions.HeadlineChars`
ships at **120** while LoCoMo's turns have a median of **133** characters and **55.8% exceed 120** — so the
reranker was scoring the first 120 characters of most candidates. Raising headlines to 512 is worth
**+13.0** points to the reranked arm (78.0 → 91.0) and only **+0.5** to the base, which is what makes it the
reranker's handicap rather than a general gain. At full text the cross-encoder captures **5.0 of the 6.5
points** the perfect judge offers, deterministically, locally and free.

**The library fix reproduces it at the SHIPPED headline length** (2026-09-08, **D108**). Once
`MemoryVerificationCandidate` carries `Content`, `+sem+rel-only+rerank` reads **91.0%** at
`HeadlineChars = 120` — up from 78.0% reading the headline, and exactly equal to the `+hl512` arm that
bought the same text with +24% of storage. So the mechanism was the TEXT and nothing else, and the storage
workaround is now only the historical control. Every anchor reproduced across the two runs (base 85.5%,
oracle 92.5%, `vector` 83.5%).

**It is not a flat-signal artifact**: 48,002 pairs scored, 31,340 distinct. And the instrument is intact
across the embedder change — `lyntai` reproduced 54.5% and `+oracle` 92.5% exactly, while the two
embedder-sensitive arms moved together (+2.5 base, +3.0 `vector`).

**Fusing generalises D105 from judges to rerankers, and generalises the disappointment with it.**
`+rerank+fuse` scores exactly the base in every category: letting the reranker COMPETE on rank removes the
partition's whole 7.5-point loss and adds nothing. Insurance, not an improvement — the same shape D105
measured for the 4B judge. **So the partition is not what to fix here; the TEXT is.**

**No default moves on this.** LoCoMo rewards a perfect archive by construction, and this document's own
standing rule is that an arm winning it owes the knowledge-update table a visit first (§5, `+forget0`'s
7:1 collapse). What is settled is the mechanism and its size, not a default.

#### …and the knowledge-update visit it owed (`memory-longmemeval --haystack --rerank`, 2026-09-08) <!-- result: id=longmemeval-rerank-current-n70 arm="`lyntai+hl512+rerank` — same `bge-reranker-v2-m3`, `HeadlineChars = 512`, k = 10, model-free" metric=prefers-current n="70 knowledge-update questions (34,242 turns per arm, 489 per question); 68 decidable" value="86.8% (59/68)" ships=no status=CURRENT -->

All 70 knowledge-update questions, haystack variant (34,242 turns per arm, 489 per question), k = 10,
model-free. The same reranker, the same `HeadlineChars = 512`, and the matched control beside it.

| arm | prefers current | `current@k` | `stale@k` | decidable |
|---|---|---|---|---|
| `lyntai` (shipped) | 90.3% (**56**/62) | 82.9% | 44.3% | 62 |
| `lyntai+hl512` | 88.9% (56/63) | 84.3% | 48.6% | 63 |
| `lyntai+hl512+rerank` | 86.8% (**59**/68) | **91.4%** | **95.7%** | **68** |
| `vector` | 40.0% (28/70) | 92.9% | 94.3% | 70 |

**The RATE falls and the COUNT rises, and the denominator is why.** `prefers current` is scored only over
questions where the arm returned at least one of the two facts, and the reranker makes six more of them
decidable — so 90.3% → 86.8% is 56 of 70 becoming **59 of 70**, which is 80.0% → 84.3% of the whole set.
A reader taking the percentage alone would record a regression where the arm answered three more questions
correctly. **This is the metric's own guard working as designed** (retrieving NEITHER would otherwise score
a vacuous 100%), and it is the trap to carry into any future arm that changes recall breadth.

**It did not cost supersession; it cost PRECISION.** `stale@k` goes 44.3% → **95.7%**, of which the headline
change accounts for 4.3 and the reranker for the rest — the page now almost always contains the superseded
fact as well as the current one. That is the mechanically predicted result: a superseded fact and its
replacement are near-identical text and both score high on query relevance, so a cross-encoder cannot tell
them apart and returns both. The engine's decay ordering still ranks the current one first inside the
promoted set, which is why preference holds at 86.8% where plain cosine — same breadth, no decay vote —
collapses to 40.0%.

**What this does NOT establish.** The bench pairs every arm against `vector`, so there is no paired test of
reranked against shipped: 56 → 59 is three questions and the confidence intervals overlap heavily
([80.5, 95.5] against [76.7, 92.9]). The honest claim is that the cross-encoder is **not** the LoCoMo-shaped
trap `RetrievabilityWeight = 0` was — it does not buy finding with burying — and no more than that.

#### The endorsement COUNT is not the lever: capping the judge changes nothing (`memory-locomo --retrieval`, 2026-09-10) <!-- result: id=locomo-judge-top20-cap-n200 arm="`+sem+rel-only+judge+top20` — the same 4B judge at depth 80, endorsement capped at the page size" metric=evidence-hit@k n="200" value="71.0%" ships=no status=CURRENT -->

The +5.0 cross-encoder against a judge that SPENDS 10.5 has been read as architecture. **It was never a
clean reading, and this file said so at the arm's own construction site**: `CrossEncoderVerifier` endorses a
FIXED top-`limit`, so the failure that cost the judge its points — promoting a set larger than the page —
is unreachable for the reranker BY CONSTRUCTION. The two arms differ in the count rule as well as the
model, and nothing had held the rule fixed. `+top20` does: the same 4B judge, the same depth 80, the same
partition, with its endorsement capped at the page size. The cap keeps the highest-RANKED endorsements, so
it changes the count and nothing else.

| arm | overall | multi-hop | temporal | open-domain | single-hop |
|---|---|---|---|---|---|
| `+sem+rel-only` (base) | 85.5% | 78.4% | 85.7% | 66.7% | 89.9% |
| `+sem+rel-only+oracle` | 92.5% | 86.5% | 95.2% | 66.7% | 96.3% |
| `+sem+rel-only+rerank` | 91.0% | 86.5% | 92.9% | 66.7% | 94.5% |
| `+sem+rel-only+judge` | 71.0% | 70.3% | 73.8% | 50.0% | 72.5% |
| **`+sem+rel-only+judge+top20`** | **71.0%** | 70.3% | 73.8% | 50.0% | 72.5% |
| `+sem+rel-only+judge+top80` | 71.0% | 70.3% | 73.8% | 50.0% | 72.5% |

**The cap moved NOTHING — identical in every cell — and it was not a weak intervention.** It bound on
**138 of 200** calls (69%), dropping **16.1** endorsements per call. `+top80` is the null control and
reports `! INERT` (0/200), which is what licenses reading the rest. n = 200, seed 12345, embedder
`embeddinggemma-300M-Q8_0`; 3382.5s. Raw output, gitignored:
`devtools/_locomo-capcontrol.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table above -->.

**The audit says why, and it bounds every future promotion rule built on this judge.** Rescuable calls —
the page missed it and the evidence sits deeper — are **14 of 200**. The judge endorsed that evidence
anywhere on 6 of them, and in its own top five on **0**. Capping cannot rescue what the model never ranks:
its endorsements are 34.3 per call at **2.3%** precision.

**Its ordering is not noise, which is the surprise.** Precision by the model's own rank runs 39.0% at top-1
against 2.3% overall — a 17× lift — so the model RANKS well and STOPS badly. That is a calibration
property, not a knowledge one. **What is refuted is the count rule as the explanation**; what survives is
that the endorsements themselves are 97.7% noise, so no rule over them helps.

#### Three rerankers, one score: recency and 168 MB both buy nothing (`memory-locomo --retrieval`, 2026-09-10) <!-- result: id=locomo-lamar600m-q8-n200 arm="`LAMAR-600m` Q8_0 (2026-07) as reranker, against incumbent `bge-reranker-v2-m3` Q8_0 (2024-03)" metric=evidence-hit@k n="200" value="91.0%" ships=no status=CURRENT -->

`LAMAR-600m` is as close to a controlled test of MODEL AGE as this axis offers: the same
`XLMRobertaForSequenceClassification` architecture as `bge-reranker-v2-m3`, the same **567,755,777**
parameters, released **2026-07-21** against the incumbent's 2024-03-15 — 28 months apart, MIT-licensed,
51 training languages. Both anchors reproduce EXACTLY across all three runs, which is what makes them
comparable.

| reranker | on disk | overall | vs base |
|---|---|---|---|
| none (`+sem+rel-only`) | — | 85.5% | — |
| `bge-reranker-v2-m3` Q8_0 (2024-03) | 635,676,416 B | 91.0% | +5.5 |
| `LAMAR-600m` Q8_0 (2026-07) | 635,677,824 B | **91.0%** | +5.5 |
| `LAMAR-600m` Q5_K_M (2026-07) | **468,393,760 B** | **91.5%** | +6.0 |
| `+oracle` (ceiling) | — | 92.5% | +7.0 |

**At matched quantisation the newer model is IDENTICAL — 91.0% against 91.0%, cell for cell in all four
categories.** So 28 months of model progress bought nothing measurable in this role. The one arm that
differs is the SMALLER file, by a single question of 200, which is inside the ~1-point near-tie band and is
better read as a demonstration of that band than as a result.

**The useful half is the file size.** 468 MB captures 6.0 of the 7.0 points a perfect judge offers, at 74%
of the incumbent's bytes, with 15,994 distinct scores over 16,002 pairs — discriminating, not flat. A
deployment that wants the reranker's gain under a 500 MB ceiling can have it.

**What this does NOT say.** It tests recency in the RERANKER role. The model this repository measured as
costing 10.5 points is an LLM JUDGE, which faces a different task — decide IF each of 80 candidates
answered, and stop — and a reranker never makes that choice. So this is silent on whether a newer small
INSTRUCT model judges better, which is the open half.

#### RETRACTED the same day: the sub-100 MB screen passed a reranker that ranks BACKWARDS (2026-09-12) <!-- result: id=rerank-screen-jina-tiny-33mb arm="`jina-reranker-v1-tiny-en` Q4_K_M (gpustack) served by `llama-server --reranking`, build 10603 — a four-document ordering screen, NOT a workload" metric=screen-verdict n="1 query × 4 documents, plus one 6,263-character extreme input" value="8/8 checks" ships=no status=RETRACTED -->

**Published, then refuted within the hour, and the refutation is the result.** The claim was that a working
reranker sits at **33,257,824 B**, 14.1× below the 468,393,760 B floor `docs/model-tasks.md` §3 had
asserted. It rested on a four-document fixture — one answer plus three distractors — and **that fixture is
too easy to detect a broken head.** Re-screened against a pair with a PUBLISHED reference score, two of the
three models fail and one of them ranks the pair backwards.

<!-- result: id=rerank-screen-reference-pair arm="the same three GGUFs on `cross-encoder/ms-marco-MiniLM-L6-v2`'s own published example pair — two ON-TOPIC documents, only one of which answers" metric=screen-verdict n="1 query × 2 documents, against a published reference of [8.607138, -4.320078]" value="control 10/10; both sub-100 MB candidates fail" ships=no status=CURRENT supersedes="rerank-screen-jina-tiny-33mb" -->

| model | on disk | arch | `token_type_count` | pooler tensor | reference pair | spread vs **12.9272** | screen |
|---|---|---|---|---|---|---|---|
| `LAMAR-600m` Q5_K_M (**control**) | 468,393,760 B | `bert` (from XLM-R) | **1** | **`cls.weight [1024,1024]`** | 6.161 / −7.700 ✓ | 13.861 (**0.9×**) | **10/10** |
| `jina-reranker-v1-tiny-en` Q4_K_M | 33,257,824 B | `jina-bert-v2` | 2 | `cls.weight [384]` — a VECTOR | 0.123 / 0.029 ✓ | 0.094 (**137.8×**) | 9/10 |
| `ms-marco-MiniLM-L6-v2` Q8_0 | 25,281,216 B | `bert` | 2 | **absent** | **−0.093 / −0.078 ✗** | −0.015 (**−860.9×**) | 7/10 |
| `cstr/ms-marco-MiniLM-L-6-v2` q4_k | 19,394,624 B | `bert` | — | — | will not load | — | — |
| `cstr/mxbai-rerank-xsmall-v1` q4_k | 67,802,304 B | `bert` | — | — | will not load | — | — |

**`ms-marco-MiniLM-L6-v2` scores "Berlin is well known for its museums" ABOVE the population figure**, for
the query *"How many people live in Berlin?"* — its own model card prints `[8.607138, -4.320078]` for those
exact inputs. It is not weak; it is wrong. The retracted screen called it *"ranks correctly"*.

**The cause is upstream and it is two defects, not one.** llama.cpp PR **#21729** — *"Add token_type_ids
input for rerank model with type-embedding"* — is `state: open`, `merged: false`, opened 2026-04-10, and
its body states that *"token_type_ids were hardcoded to zero and pooling layers were discarded during
conversion, limiting BERT functionality to embedding generation only"*. Both halves are visible in the
files above: the conversion leaves `ms-marco-MiniLM-L6-v2` with **no pooler tensor at all**, and every BERT
cross-encoder declares `token_type_count = 2` — it *needs* segment ids to tell the query from the document
— while the runtime feeds zeros.

**Only the RoBERTa/XLM-R family is immune, and that is the whole finding.** `LAMAR-600m` declares
`token_type_count = 1` because XLM-R has no segment embeddings to lose, and its pooler survived as a
`[1024,1024]` matrix. It reproduces the reference pair at **0.9×**. So a *correct* reranker in this seam
must today be RoBERTa-family — and that family's 250k-token vocabulary is exactly what puts it above
100 MB. **Sub-100 MB is blocked by an unmerged upstream patch, not by model availability**, and
**468,393,760 B remains the measured floor.** A reconversion does not fix it; the runtime half is not in
the file.

**The 512-token ceiling still holds and is now the lesser problem.** `ms-marco-MiniLM-L6-v2` also rejects a
1,221-token document with `400 … larger than the max context size (512 tokens)` while the server is up and
healthy — disqualifying for a seam that hands a verifier `Content` (**D108**). Recorded because it is
independent of the head defect: fixing #21729 would leave it true.

**EVERY PUBLISHED ARM IN THIS FILE WAS RE-SCREENED, and all of them are clean — read this before doubting
a cross-encoder figure above.** The defect is architectural, so the first question it raises is whether it
reaches the models this repository actually measured. It does not. Both rerankers behind every published
cross-encoder number — `bge-reranker-v2-m3` Q8_0 (the +5.0 arm, `locomo-rerank-hl512-n200`, and what
`AddMemoryScoringVerification` is documented against) and `LAMAR-600m` Q8_0 (`locomo-lamar600m-q8-n200`)
— declare `token_type_count = 1`, carry an intact `cls.weight [1024,1024]` pooler, and score **10/10**:

| published arm | on disk | `token_type_count` | reference pair | spread |
|---|---|---|---|---|
| `bge-reranker-v2-m3` Q8_0 | 635,676,416 B | 1 | 6.227 / −8.567 ✓ | 14.794 (**0.9×**) |
| `LAMAR-600m` Q8_0 | 635,677,824 B | 1 | 6.267 / −7.607 ✓ | 13.874 (**0.9×**) |
| `LAMAR-600m` Q5_K_M | 468,393,760 B | 1 | 6.161 / −7.700 ✓ | 13.861 (**0.9×**) |

All three are XLM-RoBERTa, which never had segment embeddings to lose — the same property that makes the
family immune is why it is the only family with a measured result here. **So #21729 invalidates nothing in
this record.** It bounds what can be ADDED to it: any future BERT-family reranker rung is degraded until
that patch merges, and the check is one metadata field.

**Sub-100 MB does NOT survive the move to multilingual, and the reason is structural rather than a bad
quant.** The only credible multilingual candidate at this scale is
`cross-encoder/mmarco-mMiniLMv2-L12-H384-v1` (117,641,603 parameters, `XLMRobertaForSequenceClassification`
— the family llama.cpp already serves for `LAMAR-600m` and `bge-reranker-v2-m3`, with `zh` and `ja` among
14 language tags and **no `ko`**). Its smallest published GGUF is **124,925,504 B** (Q4_K_M) against
**132,584,000 B** (Q8_0) — only **6.1%** apart, because XLM-R's 250,002-token vocabulary is **81.6%** of
the parameters. **Quantisation is not a lever on a model whose bulk is its embedding table**, so no quant
of this architecture reaches 104,857,600 B. Sizes read from the HuggingFace tree API, which reports bytes.

**Provenance, measured rather than inferred: one uploader is 2-for-2 defective.** Both `cstr` conversions
tested refuse to load with `error loading model: bert model needs to define token type count` — a missing
metadata key, not a missing tensor. That is the LUCKY direction (a hard failure, not silent garbage), and
it is **not** the defect this repository was watching for: the prediction from the tensor table was that
they would load and return embeddings. `.claude/knowledge/pitfalls.md` carries the two traps this
produced, including why a `cls.output.weight` check cannot condemn a non-BERT architecture.

**What this does NOT say, and the list is longer than the table.**

1. **No quality was measured — none, on any workload.** A screen says a model reproduces one published pair
   and survives a long request. `LAMAR-600m`'s +6.0 came from `memory-locomo --retrieval` at n = 200;
   nothing here is comparable to it, and **no sub-100 MB model has an evidence-hit figure at all**.
2. **A collapsed spread is not a measured quality loss.** `jina-reranker-v1-tiny-en` orders the reference
   pair correctly at 137.8× too little separation. That is consistent with the two upstream defects and is
   the reason not to spend a run on it; it is NOT a number saying how much recall it would cost.
3. **Both candidates are ENGLISH-ONLY**, so neither is a Chinese-first answer whatever it scores.
4. **One published pair is one pair.** It discriminates where the four-document fixture could not — that is
   established — but a model could reproduce it and still be poor, and the reference magnitude belongs to
   `ms-marco-MiniLM-L6-v2` rather than to every family.
5. `ships=no`, and the seam it would fill takes a `/v1/rerank` endpoint through
   `AddMemoryScoringVerification` (**D115**). Nothing here recommends a default.

**The instrument defect this pass created and caught, because it is the reusable half.** The first
reference run FAILED the known-good control, which is implausible — and the cause was the harness sending
`FIXTURE.query` with `REFERENCE.documents`, i.e. scoring the Apollo question against the Berlin passages.
Both scored strongly negative and the ordering was meaningless. **A fixture's query and its documents must
travel together**; the query is now a parameter with no default at the call site. The rule that saved it is
the one this file already relies on: *when a check condemns the control, doubt the check first.*

### The sub-100 MB floor is llama.cpp's, not the model class's: the SAME model reproduces its card exactly through ONNX Runtime (2026-09-14) <!-- result: id=rerank-screen-onnx-runtime arm="`cross-encoder/ms-marco-MiniLM-L6-v2`'s own ONNX exports — fp32 and two int8 quants — scored through onnxruntime 1.30.0 on CPU, against the SAME published reference pair the GGUF screen used" metric=screen-verdict n="1 query × 2 documents, against a published reference of [8.607138, -4.320078]" value="fp32 reproduces the card to 4 decimals (1.00x); int8 at 23,200,716 B reads 1.01x and is correctly ordered" ships=no status=CURRENT -->

**It does not supersede the GGUF screen above — it reframes what that screen measured.** Those numbers are
correct about GGUFs and stay. What changed is the CONCLUSION drawn from them: *"468,393,760 B remains the
measured floor"* is a fact about llama.cpp's converter, and the same model is fine through a runtime that
does not convert.

| variant | on disk | reference pair | spread vs **12.9272** | verdict |
|---|---|---|---|---|
| published (the model card) | — | 8.607138 / −4.320078 | — | — |
| **`onnx/model.onnx`** (fp32) | 91,011,230 B | **8.6071 / −4.3201** ✓ | 12.927 (**1.00×**) | **OK** |
| `onnx/model_qint8_avx512_vnni.onnx` | **23,200,716 B** | 8.4252 / −4.3181 ✓ | 12.743 (**1.01×**) | **OK** |
| `onnx/model_quint8_avx2.onnx` | **23,200,716 B** | 8.3312 / −4.4566 ✓ | 12.788 (**1.01×**) | **OK** |
| *same model, Q8_0 GGUF* (above) | *25,281,216 B* | *−0.093 / −0.078* ✗ | *−0.015 (**−860.9×**)* | *7/10* |

**This is a controlled comparison, which is why one pair is enough to carry it.** Same weights, same two
documents, same query, same published ground truth — only the runtime differs. The GGUF ranks them
backwards at 862× too little spread; the fp32 ONNX reproduces the card to **four decimal places**. That
isolates the defect to conversion, exactly as PR #21729's own body describes, and leaves no room for "the
model is weak".

**Quantisation is nearly free here, and that is the load-bearing half for the sizing question.** int8
costs **1.4%** of spread (12.927 → 12.743) while the ordering and the logit scale survive, so a
**23,200,716 B** correctly-scoring cross-encoder exists — **20.2× below** the 468,393,760 B figure §3 had
recorded as the floor.

**What this does NOT say, and each one is the same shape as the GGUF screen's caveats.**

1. ~~**No quality was measured, again.**~~ **CLOSED 2026-09-15 by the next section**, which is the one this
   whole row was a prerequisite for: the same int8 export reads **+3.0** on LoCoMo evidence-hit where
   `LAMAR-600m` on the same tree reads **+9.0**. It works and it is a third as good.
2. **It is ENGLISH-only.** `ms-marco-MiniLM-L6-v2` is English, so the multilingual finding above is
   untouched — a Chinese-first deployment still has no sub-100 MB answer, and the reason is the vocabulary
   rather than the runtime.
3. **The 512-token ceiling is untouched.** It is a property of the model, not the converter, and D108's
   seam hands a verifier `Content`.
4. ~~**It was measured in Python, not through this library's seam.**~~ **CLOSED 2026-09-15**, and by
   neither route this caveat named: not an in-process `IMemoryVerificationPolicy` and nothing hosting the
   file. `AddOnnxProvider` with `Produces = ProviderKinds.Score` is an `IModelProvider` declaring that kind, which
   `AddMemoryScoringVerification` already selects on (**D139**, not the D115 this caveat read from).
   <br>**The .NET path reproduces the same pair**, so the segment signal survives this library's own
   WordPiece pair encoding and session and not only Python's — asserted against the published figures by
   `OnnxCrossEncoderLiveTests`, gated on `LYNTAI_ONNX_RERANK_MODEL_DIR`. That is the same SCREEN as the
   table above, re-run one layer over; caveat 1 is untouched and is now the only thing left in this row.
5. `ships=no`. Nothing here recommends a default.

#### A sub-100 MB cross-encoder WORKS and captures a THIRD of what 468 MB does — and the 68 MB of extra precision buys nothing (`memory-locomo --retrieval`, 2026-09-15) <!-- result: id=locomo-onnx-sub100mb-n200 arm="`ms-marco-MiniLM-L6-v2` int8 and fp32 ONNX exports scored IN PROCESS through `AddOnnxCrossEncoder`, against `LAMAR-600m` Q5_K_M over `llama-server --reranking` — same tree, same `nomic-embed-text` embedder, byte-identical retrieval" metric=evidence-hit@k n="200" value="+3.0 (both exports) against the 468,393,760 B arm's +9.0, of 9.5 reachable" ships=no status=CURRENT --> <!-- drift-ok: the arm names the registration of its day; D157 folded it into AddOnnxProvider -->

**The first evidence-hit figure any sub-100 MB reranker has, through any runtime.** Three runs, one ladder
(`+sem+rel-only` / `+rerank` / `+oracle`), `--arms`-narrowed, n = 200, k = 20, seed 12345. The retrieval
path is byte-identical across all three — same collections, same 419 vectors, same eight leading cosines to
three decimals — so **the reranker is the only variable.**

| reranker | on disk | overall | vs base | of the 9.5 reachable | multi-hop | temporal | open-domain | single-hop |
|---|---:|---:|---:|---:|---|---|---|---|
| none (`+sem+rel-only`) | — | 83.0% | — | — | 81.1% | 81.0% | 58.3% | 87.2% |
| `ms-marco-MiniLM-L6-v2` **int8** ONNX | **23,200,716 B** | 86.0% | **+3.0** | **31.6%** | 75.7% | 90.5% | 66.7% | 89.9% |
| `ms-marco-MiniLM-L6-v2` **fp32** ONNX | 91,011,230 B | 86.0% | **+3.0** | **31.6%** | 75.7% | 90.5% | 66.7% | 89.9% |
| `LAMAR-600m` Q5_K_M GGUF | 468,393,760 B | **92.0%** | **+9.0** | **94.7%** | 86.5% | 95.2% | 75.0% | 94.5% |
| `+oracle` (perfect judge) | — | 92.5% | +9.5 | 100% | 86.5% | 95.2% | 75.0% | 95.4% |

**It works, and that is not nothing** — a real +3.0 (six questions of 200, outside the ~1-point near-tie
band), from a discriminating scorer: 15,981–15,984 distinct scores over 16,000 pairs, never the flat-scorer
failure. **But 20× the bytes buys 3× the gain**, and `LAMAR-600m` is close enough to the ceiling to be
indistinguishable from a perfect judge in three of four categories.

**QUANTISATION IS NOT THE LEVER, and the evidence is stronger than a near-tie: the two exports are
IDENTICAL IN EVERY CELL** — 28/37, 38/42, 8/12, 98/109, in both. Their scores differ numerically (15,984
distinct against 15,981) and the ORDER within every endorsement set does not. So the extra 67,810,514 B of
precision changes no decision this workload asks for, **the deficit is the model class rather than the
quant**, and a deployment choosing this family should take the 23,200,716 B file.

**The 512-token window NEVER BIT: 0 of 16,000 pairs truncated.** That was pre-registered as the confound
that would make the result unreadable — every arm compared against it has 8192 — and it is ruled out
empirically rather than argued away. The deficit is what the model does with evidence it fully saw.

**It REGRESSES multi-hop, −5.4 (81.1% → 75.7%), where `LAMAR-600m` matches the oracle exactly.** That is
the one cell where the small model is worse than no reranker at all, and it is the category needing several
evidence turns: a fixed top-k endorsement from a weak scorer promotes the single best-matching turn and
displaces the co-evidence. A deployment on multi-hop questions should read this row, not the overall.

**A reranker's delta is a property of the CONFIGURATION, not of the reranker — the published +6.0 did not
reproduce.** `LAMAR-600m` Q5_K_M reads +6.0 in `locomo-lamar600m-q8-n200` and **+9.0** here. The two runs
differ in the embedder (`embeddinggemma-300M`, base 85.5%, against `nomic-embed-text`, base 83.0%) **and**
in three weeks of tree, and **this study did not separate them**, so no cause is claimed. What is
established is the rule: a weaker base leaves more to recover, so quoting a reranker's gain without its
base and its embedder states a number that does not transfer. Both bases reproduce their own recorded
anchors — 83.0% is `locomo-fusion-sem-rel-only` exactly — which is what makes each run internally readable.

**What this does NOT say.** The comparison stacks confounds on purpose, because the QUESTION is whether the
size class works rather than which factor explains the gap: 22M against 568M parameters, English BERT
against multilingual XLM-R, ONNX in process against GGUF over HTTP. Only quantisation and the window are
separated out. It is one workload and one embedder; `ships=no`, and the seam still ships empty. <!-- result: id=rerank-repeatability-drift arm="`LAMAR-600m` Q5_K_M on one `llama-server --reranking` instance — the identical request repeated, and repeated after a differently-shaped batch" metric=screen-verdict n="6 calls of 4 documents on one server instance" value="9.42e-3 max drift" ships=no status=CURRENT -->

Found by a determinism check failing on the **known-good control**, which is the useful way to find it.
Six calls to one `llama-server --reranking` instance: the first differs from the second and third, which
agree with each other; interposing a differently-shaped batch settles the scores on a *new* fixed point
that is then stable. Maximum absolute drift **9.42e-3**; the argmax was unchanged in all six calls.
`rerank-screen` therefore asserts the ORDER and merely REPORTS the drift — demanding byte-equality fails a
model that is working correctly, which is how this was found.

**This does not refute the anchors above it.** *"Both anchors reproduce EXACTLY across all three runs"*
(Part 176) is a claim about a fresh server replaying the same call sequence, and replaying a sequence
replays its drift — so the published cell-for-cell reproduction stands. What is wrong is the **scope a
reader will give it**: repeatability here is a property of the RUN, not of the scorer, and a harness that
changes its batch composition between arms is not covered by it. The practical rule is the one
`CrossEncoderVerifier` already follows — consume the ORDER, never the score value — and `DistinctScores`
is unaffected, since it rounds at 1e-6 and the drift is four orders of magnitude larger.

#### "used", "gone", but not "contradicted" — and RIF is NOT the shape of that gap (analysed 2026-09-08) <!-- result: id=longmemeval-rif-endorsement-n70 arm="the shipped `LlmMemoryVerificationPolicy` over `gemma3:4b` (Q4_K_M), 40 candidates per call — endorsement of the SUPERSEDED fact" metric=endorsement-rate n="70 knowledge-update questions, haystack; 2,800 candidate showings, 69 of them the superseded fact" value="36.2%" ships=no status=CURRENT -->

Filed as a design lead on 2026-08-15 and **analysed rather than built**. The conclusion is not to build it,
and three of the premises it rested on are refuted by the tree.

**There are TWO gaps here and the lead fills the one it is not named after.** The name claims a SEMANTIC
hole — the engine cannot represent *this fact supersedes that one*. The argument beneath it establishes a
MECHANICAL one — an entry can be strengthened or deleted and nothing in between. Retrieval-induced
forgetting supplies a weakening ACT, so it is a candidate for the second; it supplies no supersession
SIGNAL, so it cannot address the first, which is the whole reason it was filed. **D106** already names what
fills the semantic gap and it is not this: **valid-time on the write**, which is where every field system
puts it (Mem0's ADD/UPDATE/DELETE over a similar set, Zep/Graphiti's invalidate-don't-delete). None derives
supersession from retrieval competition.

**The contract does not block a decrement, so "impossible" was never the reason.** The monotonicity clause
is scoped to `Reinforce` — *the state after a successful recall* — and `ModulatedRetrievability` already
does the exact shape a penalty needs: it builds an effective state for the CURVE
(`state with { Stability = state.Stability * factor }`) and forwards `Reinforce` on the RAW state, so it
lowers retrievability while persisting nothing. What actually blocks a factor below 1 is
`IMemoryRetentionPolicy`'s clamp to `[1, declared]`, and that clamp exists because the composed factor
widens `CandidateCutoff`, whose only consumer is `PruneAsync` — a narrowing factor would DELETE, which is
**D41**'s refusal. A penalty computed per call and never persisted sidesteps all of it.

**Three premise errors, each refuted by the tree.**

- *"The signal is already computed and discarded."* It is neither. Under the shipped
  `MemoryVerdictCombination.Partition` the measured judge endorses 29.1 against a limit of 20, so every
  RETURNED node is endorsed and every review row logs `Verified = true`; the unendorsed half is not
  persisted at all on the configuration that ships. And no verifier is registered by default, so the
  trigger does not exist for any deployment that has not paid for a model — invisible to every model-free
  instrument in the roster.
- *"Not being reinforced ALREADY decays an entry relatively"*, offered as the null control. A recall leaves
  an untouched entry bit-identical; only a WRITE advances position, and it advances it as a common addend
  on every untouched entry, which preserves the order and the tie structure exactly. `ReciprocalRankFusionPolicy`
  reads rank POSITIONS, so that decay contributes exactly nothing to ranking. The stated control is the
  wrong control — which cuts in the proposal's favour on separability and against it on the mechanism.
- *"The verdict's unendorsed half is the sharper proxy."* On this workload it is measurably blind to
  supersession: a cross-encoder run through that seam took `stale@k` 44.3% → 95.7%, because a superseded
  fact and its replacement are the two most query-similar entries in the store. A relevance verdict cannot
  separate them, and that is the pair the mechanism exists to separate.

**And the literature predicts the effect is weakest exactly where it is wanted.** RIF is reduced or
eliminated when the competitor is INTEGRATED with the target (Anderson & McCulloch 1999) — and a fact and
its supersessor are maximally integrated. No published work shows that penalising competitors improves
retrieval quality in a machine memory system, as opposed to describing human memory.

**Why not build it anyway.** A competitor penalty writes a QUERY-RELATIVE observation into query-independent
state — the same category error the write-time reconciliation runs paid for by another route (**Part 148**:
`current@k` and `stale@k` identical between `extract` and `extract+reconcile`; only the order moved). And an
arm is under-powered before it is written: **Part 166** measured that no corpus here has dense supersession
(4 replacements, 10 dense pairs of 244 asked, p = 1.000).

**IT WAS MEASURED (2026-09-09) AND THE TRIGGER IS INVERTED — a third answer neither branch anticipated.**
All 70 knowledge-update questions, haystack, the shipped `LlmMemoryVerificationPolicy` over `gemma3:4b`
(Q4_K_M), 40 candidates per call, zero judge failures. **First run judge-served by Ollama, second by llama.cpp**, and both are reported below
rather than one being quietly preferred. The first was a deviation from `repo-mechanics.md` §Local models,
taken because the only chat model here was Ollama's `gemma3:4b`, whose blob is a GGUF that stock
`llama-server` REFUSES (`key not found in model: gemma3.attention.layer_norm_rms_epsilon`). Pulling the
model's own GGUF removed the deviation and the numbers did not move. Embedder on llama.cpp (`repo-mechanics.md` §Local models).

| population | shown | endorsed | UNendorsed |
|---|---|---|---|
| every candidate (base rate) | 2,800 | 9.0% | 91.0% |
| the CURRENT fact | 65 | 10.8% | 89.2% |
| the SUPERSEDED fact | 69 | **36.2%** | 63.8% |

**The judge endorses the SUPERSEDED fact 3.4× more often than the current one**, and at 4× the base rate.
So the unendorsed half is enriched for the CURRENT fact, and a penalty on it fires backwards. Of the 65
calls that showed both, it would demote the superseded fact alone **5** times and the current fact alone
**23** — a 4.6:1 ratio the wrong way, with 35 no-discrimination cases besides.

**REPRODUCED on llama.cpp, and the decisive cells are IDENTICAL** (2026-09-09, second run): base rate
8.9%, current fact 12.3%, superseded fact **37.7%**, and the paired cells **5 correct against 23
backwards** — the same integers, across a different server AND a different embedder (`embeddinggemma` here
against `nomic-embed-text` there, since the judge model is what the measurement is about). Two runs, two
stacks, one answer. **The standard is also 2.6× faster** — 1,183s against 3,094s — most plausibly because
two dedicated resident servers never swap, which is what one model per port buys.

**The mechanism is the one this document already measured from the other side.** A superseded statement is
often the more canonical answer to the question, while its replacement is phrased as a revision — so a
RELEVANCE judgement prefers the stale one. That is the same effect behind the cross-encoder taking
`stale@k` from 44.3% to 95.7%: query relevance is not merely blind to recency here, it is mildly
ANTI-correlated with it. **D109 predicted "at base rate" as the killing result; the measurement is worse
than that, and the direction is closed rather than merely unsupported.**

**What would revive it, and it is one cheap measurement.** `lyntai_memory_review.verified` is already
persisted per (node, batch) as a tri-state, so the trigger's PRECISION is computable from data on disk:
P(entry is the superseded member of the pair | returned AND unendorsed) on the knowledge-update haystack,
against the base rate. Near the base rate, the entry it fires on is second-best rather than wrong and every
objection above follows for free. Well above it, the finding is *a judge can identify superseded material*
— which is a supersession DETECTOR, and belongs on D106's write-time axis as a relation, not as a
competitor penalty. **D109.**

### Salience's RANKING voice is a net cost — measured 2026-08-23 <!-- result: id=salience-rank-weight0-shipped arm="`SalienceWeight = 0` (now the shipped default, D89) vs `1.0`" metric=miss n="10 seeds; 10/10 language × embedder cells (5 languages × 2 embedders); the first run was 10 seeds × 5 shapes × 4 arms" value="−0.0530" ships=yes status=CURRENT supersedes="salience-rank-first-sweep-5of5" -->

`node devtools/dev.mjs memory-salience-weight`, 10 seeds × 5 shapes × 4 arms of
`ReciprocalRankFusionOptions.SalienceWeight`, against a real embedding model. Retention and store admission
are held identical in every arm, so this prices the ranking contribution **alone**.

| `SalienceWeight` | `many-candidates` miss | other shapes miss | pollution (regression / other) |
|---|---|---|---|
| **0** | **−0.0962** | **−0.0570** | +0.0487 / +0.0277 |
| 0.5 | −0.0326 | −0.0264 | +0.0630 / +0.0079 |
| **1.0 (shipped)** | — | — | — |
| 2.0 | +0.0616 | +0.0322 | +0.0062 / +0.0034 |

**Lower is better, monotonically, on every shape in English.** Under §5.7.0 that trade is accepted: miss is
objective (2) and pollution (3) is explicitly not co-equal, so a large miss reduction for a small pollution
rise is the correct direction.

This says nothing bad about salience — it says salience is not a *ranking* signal. **D45** reached the same
conclusion by argument, which is why `MultiplicativeRankingPolicy`'s rank boost defaults OFF: salience means
"does not fade away", and store admission already delivers that.

**Two more runs across five languages and a SECOND embedding model settled it, and `SalienceWeight` now
ships at `0`** (**D89**). Ordinary shapes — the ones a change like this must not cost anything:

| language | `embeddinggemma` (miss / poll) | `nomic-embed-text` (miss / poll) |
|---|---|---|
| English | −0.0570 / +0.0277 | −0.0350 / +0.0456 |
| Chinese | −0.0708 / −0.0071 | −0.0605 / −0.0060 |
| Japanese | −0.0532 / +0.0034 | −0.0306 / −0.0031 |
| Korean | −0.0253 / +0.0474 | −0.0364 / −0.0036 |
| ChineseMixed | −0.0577 / −0.0058 | −0.1039 / −0.0109 |

**Miss is better in 10/10 cells** (mean −0.0530) for a mean pollution rise of **+0.0088** — 6:1, which
§5.7.0 accepts outright. `SalienceWeight = 1` restores the old behaviour in one line.

**The second embedder mattered because it REFUTED the first run's reading.** On `embeddinggemma` alone,
Korean's ordinary shapes traded the wrong way and the conclusion was "four of five writing systems is not a
default". On `nomic-embed-text` Korean accepts and **English** refuses — the refusing row moved, so it was
never a property of a language; it is the noise floor of the pollution column at ten seeds.

**Three instrument lessons, because each nearly published a wrong answer.** A verdict reporting MISS alone <!-- result: id=salience-rank-first-sweep-5of5 arm="the FIRST VERSION of the same sweep (verdict on miss alone, regression shape averaged in, per-cell accepted/REFUSED on an unstable sign)" metric=miss n="unstated" value="5/5 shapes better" ships=no status=RETRACTED -->
cannot evaluate a lexicographic objective. A summary that averages the regression shape together with the
shapes it is traded against destroys the only structure the study has. And a per-cell `accepted`/`REFUSED`
computed from a quantity whose sign is unstable reads as a judgement while being a coin-flip — that verdict
belongs on the aggregate. The first version of this sweep did all three, printed `5/5 shapes better` for
every language, and the shipped default was changed on it before anyone read the pollution column.

**The control worth copying if you build a sweep of your own:** the study reports *distinct salience values*
(352), not how often salience fired (98.9%). Firing is presence; only distinct values are discrimination, and
RRF ranks by competition (**D82**) — so a signal every candidate ties on contributes the same constant at
every weight, and the curve would be flat as an artifact with every ordinary control green.

### And salience's OTHER two consumers cost miss as well (`memory-salience`, 2026-08-28) <!-- result: id=salience-consumers-retention-only arm="shipped salience vs a `SalienceOff` control that was NOT off — labelled 'retention + store admission', actually RETENTION alone (`nomic-embed-text`)" metric=miss n="30 seeds × 6 shapes × 2 arms, paired per (seed, shape), run twice against two real embedders" value="+0.0384" ships=no status=RETRACTED -->

> **CORRECTION 2026-08-30 — every figure in this subsection and the two ladders below measures RETENTION
> ALONE, not "retention and store admission".** The arm labelled `SalienceOff` passed
> `saliencePolicies: null`, and `GraphMemoryEngine.NormalizeSaliencePolicies` substitutes the shipped
> `StructuralSaliencePolicy` for a null or empty collection ("empty does NOT mean off"). So the control arm
> judged every write at the shipped `NoveltyWeight` and wrote the signal **store admission reads** — the
> admission consumer was live in BOTH arms and cancels out of every paired difference here. The numbers are
> real and reproduce; the LABEL was wrong. `docs/FIXES.md` carries the incident and `pitfalls.md` the
> general trap. Read every Δ below as "what registering a retention policy costs", and note that the
> lexicographic verdict was also computed on miss alone until the same day.

D89 measured the RANKING voice and shipped it at 0. This measures what is left — retention and store
admission, the two consumers that actually ship ON (**but see the correction above: it separated only
retention**) — with the rank boost already at its shipped 0, so the
two studies do not overlap. `node devtools/dev.mjs memory-salience`, 30 seeds × 6 shapes × 2 arms, paired
per (seed, shape), run **twice against two real embedders**: positive Δ means salience makes recall worse.

| shape | `nomic-embed-text` | `embeddinggemma:300m` |
|---|---|---|
| baseline | +0.0375 * | +0.0117 * |
| low-reuse | +0.0489 * | +0.0313 * |
| high-reuse | +0.0240 * | +0.0044 * |
| high-noise | +0.0188 * | **−0.0251 \*** |
| many-candidates | **+0.0786 \*** | +0.0374 * |
| rare-critical | +0.0223 * | +0.0321 * |
| **mean combined Δ miss** | **+0.0384** | **+0.0153** |

`*` = the 95% paired interval excludes zero; 15 of 36 cells significant in each run.

**The direction replicates and the magnitude does not.** Five of six shapes are positive under both
embedders, so "salience costs miss through retention and admission" is not one embedder's artefact — but the
means differ by ~2.5× and **`high-noise` reverses sign**, so no single figure should be quoted as *the* cost.
That is D89's own lesson holding: it required a second embedder because the first run's reading did not
survive one. Here the second moderated the reading instead of refuting it.

**The sharpest cell is the class salience exists for.** `attribute (subject cue)` — a cluster stated once and
thereafter referred to obliquely, which is precisely "does not fade away" — is hurt on five of six shapes
under BOTH embedders (up to +0.266 / +0.138). Salience is worst at the case it was built for.

**It is not uniformly harmful**: `critical-rare` improves significantly on the shapes that stress rarity.

**This runs through a REAL embedder and refuses without one, changed 2026-08-28.** It previously built a
`FakeEmbedder` per replay, so "unlike anything already stored" was measured as "shares few words with
anything already stored" — a different quantity, and the one whose numbers Part 69 withdrew. Its two
siblings already refused; this one did not.

**`MaxSalience` is a SWITCH, not a dial, and its shipped default is dead configuration** (`memory-salience
--ceiling`, 30 seeds × 2 shapes × 5 arms, `nomic-embed-text`). The ladder was run because `MaxSalience` looked
like the bounded-admission lever Part 65 wanted. It is not:

| arm vs Off | baseline combined | many-candidates combined |
|---|---|---|
| `Max1` | +0.0016 [−0.0066, 0.0097] | −0.0085 [−0.0253, 0.0083] |
| `Max2` | +0.0375 * | +0.0786 * |
| `Max3` | +0.0375 * | +0.0786 * |
| `Max4` | +0.0375 * | +0.0786 * |

**`Max2`, `Max3` and `Max4` are identical in every cell**, so the ceiling never binds at 2 — and the reason
is arithmetic: salience is `Clamp(1 + NoveltyWeight × novelty, 1, MaxSalience)` with `novelty ∈ [0,1]` and
`NoveltyWeight = 1.5`, so the unclamped value cannot exceed **2.5**. The shipped `MaxSalience = 4` therefore
sits outside the reachable range and can never bind at all; the identity of the three arms further says no
write on this corpus reached even 2, i.e. novelty stayed at or below `(2−1)/1.5 ≈ 0.667`.
<br>**`Max1` is indistinguishable from Off on all four cells** (every interval includes zero) while still
REGISTERING a retention policy — the controls report `Max1 retention policies: 1` against `SalienceOff: 0`.
That is the measured form of an option-level neutral: at that ceiling the clamp makes
`StructuralSaliencePolicy` return `MemorySignals.Empty`, so the DI collection and
`NormalizeSaliencePolicies`' "empty does NOT mean off" contract are both untouched while the effect is gone.
<br>**So bounding salience's magnitude is `NoveltyWeight`'s job, not `MaxSalience`'s.** `MaxSalience` offers
exactly two reachable behaviours, full and off.

**And `NoveltyWeight` IS a real dial, measured the same way** (`memory-salience --novelty`, same 30 seeds ×
2 shapes, `nomic-embed-text`). Every prediction stated before the run held:

| arm vs Off | baseline combined | many-candidates combined | attribute (baseline) |
|---|---|---|---|
| `NW-1.5` | +0.0016 [−0.0066, 0.0097] | −0.0085 [−0.0253, 0.0083] | +0.0104 |
| `NW0` | *identical to `NW-1.5`* | *identical* | *identical* |
| `NW1.5` (shipped) | +0.0375 * | +0.0786 * | +0.2178 * |
| `NW3` | +0.0363 * | **+0.0954 \*** | **+0.2619 \*** |

**Turning the dial UP makes recall worse**, monotonically where it matters: many-candidates +0.0786 → +0.0954
and the attribute class +0.2178 → +0.2619. `NW1.5` reproduces the standalone run's cells exactly, which is
the control that says the ladder measures the same thing.
<br>**`NW-1.5` is byte-identical to `NW0`, and that refuted a shipped DOC rather than a value.**
`SalienceOptions.NoveltyWeight` claimed "a negative weight legitimately inverts the effect"; it cannot,
because `Math.Clamp(1 + w × novelty, 1, MaxSalience)` floors at 1, so any negative weight returns the neutral
value and no signal. The doc was corrected on 2026-08-29. Inverting the preference needs a different policy,
not a negative weight — which matters, because "prefer the FAMILIAR" is exactly the hypothesis the attribute
column above invites, and this knob cannot express it.

**What it does NOT settle, and the caveat did not weaken with a real embedder.** The corpus's noise is
TEMPLATED, so the second noise entry onward reads as familiar under *any* embedder — the novelty-inversion
concern stays unreachable by construction, and `memory-importance`'s `diverse-noise` shape is what reaches
it. `MinimumComparables` is still unswept and still documented "Unmeasured" in its own XML. **`MaxSalience`
and `NoveltyWeight` no longer are** — both ladders above are their measurement, and both XML docs were
corrected on 2026-08-29 to carry what those runs found rather than the "a starting point" they shipped with.
`MaxSalience = 1` makes `StructuralSaliencePolicy` return `MemorySignals.Empty` while remaining registered —
an option-level neutral that changes no DI registration. A bound on what salience may displace is therefore
a VALUE, not a mechanism somebody still has to design.
<br>**What the ceiling ladder leaves for someone to DECIDE rather than measure**: `MaxSalience`'s default of
4 is unreachable at `NoveltyWeight`'s own default, so two shipped defaults make one of them inert. Lowering
it is a no-op *on this corpus* and not in general — a consumer who raises `NoveltyWeight` would feel it —
which is why it is an owner's call and sits in `docs/task-archive.md` Part 216 rather than being quietly changed here.

### The same ladder against an off arm that is actually OFF (`memory-salience --novelty`, 2026-08-30) <!-- result: id=salience-novelty-nw15-true-off arm="`NW1.5` (shipped `NoveltyWeight = 1.5`) vs a genuine `NeutralSaliencePolicy` off arm, `nomic-embed-text`" metric=miss n="30 seeds × 6 shapes × 6 arms, 1080 replays; repeated on a second embedder at the same 30 seeds × 6 shapes × 6 arms" value="+0.0018" ships=yes status=CURRENT supersedes="salience-consumers-retention-only" -->

Everything above compares against a control that still ran the shipped salience policy (the correction at the
head of this section). This is the re-measurement: `NeutralSaliencePolicy` in the off arm, **all six corpus
shapes** rather than two, rungs bracketing the decision, 30 seeds, `nomic-embed-text`, 1080 replays.

**The harness is DETERMINISTIC, and this run proves it rather than assuming it.** `NW0` — the clamp's own
neutral — comes back **exactly `0.0000` against the off arm on every cell of every shape, both metrics, with
zero-width intervals**. Under the old control the same arm read +0.0401 and +0.0493 on two shapes. So the
run-to-run noise floor for an identical configuration is zero, every nonzero figure below is a property of
the configuration, and the confound is fully accounted for.

| arm vs Off | baseline | low-reuse | high-reuse | high-noise | many-candidates | rare-critical | **mean** |
|---|---|---|---|---|---|---|---|
| `NW0` (neutral) | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.0000 | **0.0000** |
| `NW0.5` | −0.0072 | +0.0164 * | **−0.0374 \*** | **−0.0947 \*** | +0.0452 * | +0.0083 | **−0.0116** |
| `NW1` | +0.0065 | +0.0284 * | −0.0341 * | **−0.0973 \*** | +0.0811 * | +0.0212 | **+0.0010** |
| `NW1.5` (shipped) | +0.0089 | +0.0281 * | −0.0235 | **−0.0931 \*** | +0.0751 * | +0.0151 | **+0.0018** |
| `NW3` | +0.0116 | +0.0456 * | −0.0148 | **−0.1213 \*** | +0.1192 * | +0.0315 * | **+0.0120** |

`*` = the 95% paired interval excludes zero. Positive Δ miss means salience makes recall worse.

**The shipped weight is roughly FREE, not a cost.** `NW1.5` reads +0.0018 miss and +0.0012 pollution against
a genuine off arm, where the contaminated pair reported +0.0384. That is not a correction of one number: the
old figure is the cost of the RETENTION consumer alone, and against a real off arm retention's cost is very
nearly cancelled by what store admission buys.

**Where it is bought and where it is paid is the actual finding**, and the mean hides it. Salience helps
enormously on `high-noise` (−0.09 to −0.12, significant at every weight) and on `high-reuse`; it costs on
`many-candidates` (+0.045 to +0.119, monotone in the weight) and on `low-reuse`. `high-noise` reverses sign
against the old table's +0.0188 — that table measured retention, which hurts there, while admission helps
there by more.

**Read the `high-noise` column with the templated-noise caveat, because it now carries the result.** That
shape's noise shares a skeleton with every other class, so "novelty separates junk" is measured under the one
condition this corpus cannot express faithfully. The dominant cell is the one most exposed to the known blind
spot, which is a reason to weight it less, not more.

**The second embedder was run, and the WINNER did not survive it** (`embeddinggemma:300m`, same 30 seeds × <!-- result: id=salience-novelty-nw05-winner arm="`NW0.5` — the best rung under `nomic-embed-text`" metric=miss n="30 seeds × 6 shapes; re-run at the same 30 seeds × 6 shapes × 6 arms on `embeddinggemma:300m`" value="−0.0116" ships=no status=RETRACTED -->
6 shapes × 6 arms, same corrected off arm; `NW0` is exactly `0.0000` there too, so determinism holds on both):

| arm vs Off | `nomic-embed-text` | `embeddinggemma:300m` |
|---|---|---|
| `NW0.5` | **−0.0116** / −0.0042 | −0.0146 / −0.0059 |
| `NW1` | +0.0010 / −0.0005 | −0.0121 / −0.0043 |
| `NW1.5` (shipped) | +0.0018 / +0.0012 | −0.0125 / −0.0040 |
| `NW3` | +0.0120 / −0.0022 | **−0.0303** / **−0.0285** |

(miss / pollution, mean combined.) **The two embedders pick opposite ends of the ladder** — `NW0.5` under one
and `NW3` under the other — so this measurement does not identify a best weight, and D89's precedent applies
exactly as written: it required a second embedder because the first reading did not survive one, and here it
did not either.

**What DOES survive both, and it is the part worth keeping.** `high-noise` is where salience pays, at every
weight, under both embedders, significantly and by a lot (−0.09 to −0.12) — the largest and most consistent
cell in the study. And **the shipped weight is not a net cost under either**: +0.0018 (≈ free) against
−0.0125 (helps). Meanwhile `many-candidates` — the regression `docs/task-archive.md` Part 216 exists for — is significant
under `nomic` (+0.0751) and **not significant** under `embeddinggemma` (+0.0171), which is the ~2.5× embedder
sensitivity the earlier table warned about, now visible on a correct instrument.

**No default moved, and this run is a reason not to move one rather than a failure to decide.** The ladder
was widened and re-based specifically to price a default; what it found is that the price depends on the
embedder more than on the knob.

### The first number against the FIELD's benchmark (`memory-locomo`, 2026-08-29) <!-- result: id=locomo-lyntai-shipped-n200 arm="`lyntai` (shipped defaults)" metric=evidence-hit@k n="200" value="54.5%" ships=yes status=CURRENT supersedes="locomo-lyntai-first-11pct-n200" -->

`node devtools/dev.mjs memory-locomo --retrieval --n 200` — LoCoMo, 10 conversations, 5882 turns ingested
per arm, 200 questions stratified over the four scored categories (5 is the adversarial class the published
protocol excludes). **The metric is MODEL-FREE**: LoCoMo names the evidence turn for each question by
dialogue id, so this scores whether the recalled set CONTAINS it. No reader, no judge, so neither can be
blamed or credited.

| arm | multi-hop | temporal | open-domain | single-hop | overall | items/q |
|---|---|---|---|---|---|---|
| `lyntai` (shipped defaults) | 13.5% | 14.3% | 8.3% | 9.2% | **11.0%** | 20.0 |
| `lyntai+sem` (`SemanticSeedOptions.K = 20`) | 10.8% | 16.7% | 8.3% | 9.2% | **11.0%** | 20.0 |
| `lyntai+rel` (+ `RetrievabilityWeight = 0`) | 13.5% | 23.8% | 16.7% | 25.7% | **22.5%** | 20.0 |
| `vector` (plain cosine, same embedder, same k) | 81.1% | 81.0% | 58.3% | 82.6% | **80.5%** | 20.0 |

**The shipped default retrieves the evidence 11% of the time where plain cosine gets 80%.** Every arm
returned a full 20 items, so this is not a filter — it is ranking the wrong 20. The mechanism is visible in
a single dumped question whose evidence is `D1:4, D6:8`: `lyntai` returns `D19`, `D14`, `D19` — the newest
turns — while `vector` returns `D1:5`, `D1:3`, the oldest session. `RelevanceWeight` and
`RetrievabilityWeight` both ship at **1**, so a recall ranks how-reachable equally with how-relevant. That
is right when recent material is likelier wanted and exactly wrong for a benchmark whose questions are
spread evenly over the whole history.

**This is the blind spot `docs/memory.md` §7 concedes, reached from outside.** The synthetic corpus cannot
see it: its
relevance is recency-correlated by construction, so the two signals never disagree there. LoCoMo makes them
disagree on purpose.

**`SemanticSeedOptions.K` changed NOTHING, and the reason is that it CANNOT — measured, not inferred.** Turning it
to 20 moved the overall figure by 0.0 points. Four plumbing explanations were ruled out by a control that
reads back what actually ran:

```
CONTROL lyntai+sem/conv-30: collections=1 [locomo|conv-30|session] vectors=369 of 369 turns;
                            semantic top-20 returned 20, ids parse as long: 20
```

Every vector is stored, the collection name matches, the search returns a full k, and every id parses — so
the seeds do reach `GatherAsync` and are added at hop 0 with their cosine as `Relevance`. They then lose the
ranking, and the two scales side by side say why:

```
returned Relevance : 1.000, 1.000, 1.000, 1.000, 1.000, 1.000, 1.000, 1.000
semantic  cosines  : 0.785, 0.664, 0.630, 0.622, 0.622, 0.619, 0.618, 0.618
```

**The pool is saturated with flat-1.000 relevance and the best semantic seed in the whole collection enters
at 0.785.** A cosine cannot outrank a flat 1 however much more relevant it is, so the seed is structurally
unable to surface anything — which is why the knob reads as inert rather than weak. **This is exactly the
incommensurability D93 records** — "a lexical rank ramp, a real cosine on a semantic seed, a flat `1` on
graph-walk and subject seeds" — and it is the first measurement of what that costs. D93 drew the conclusion
that no ANSWER may be computed from the value; this adds that the mixed scale also defeats the seeding
feature itself.

**The ROOT CAUSE is a literal, found the same day.** `MemoryNodeRow.ToNode` — and its InMemory twin —
materialize every row with `Relevance = 1`, the maximum. `SeedAsync` overwrites it with a real score;
`NeighboursAsync` and `GetAsync` do not. So every graph-walk candidate and every seed fetched by id claims a
perfect relevance it never earned, and outranks everything that did. That is why the pool reads flat 1.000,
why a 0.785 cosine loses, and why the knob moves the number by exactly zero.

**Setting it to 0 was tried, and it is the strongest single lever measured on this engine:**

| arm | before | after |
|---|---|---|
| `lyntai` (defaults) | 11.0% | **31.0%** |
| `lyntai+sem` | 11.0% | **36.0%** |
| `lyntai+rel` | 22.5% | **63.5%** |
| `vector` (control, engine untouched) | 80.5% | 80.5% |

_**Both columns predate the per-question isolation fix** (Part 118), so read the DELTA and not the levels: <!-- result: id=locomo-lyntai-first-11pct-n200 arm="`lyntai` (shipped defaults) as FIRST published — D97 `Relevance = 1` literal still present and all questions sharing one store" metric=evidence-hit@k n="200" value="11.0%" ships=no status=RETRACTED -->
the isolated `lyntai` is 54.5%, not 31.0%. Every arm here shared a store across questions, so both columns
carry the same offset and the within-regime comparison this table exists to make is unaffected. It is left
as measured rather than restated, because re-running it would need a `RetrievabilityWeight` ladder the
shipped harness no longer has._

The control's being byte-identical is what says the harness did not move underneath. `SemanticSeedOptions.K`
becomes worth **+5.0 points** where it was worth exactly 0.0 — it was unreachable, not weak. **That also revises the
"two separate costs" reading above**: much of what looked like a recency preference was this literal, since
relevance-only ranking still put unscored neighbours first.

**And 0 ALONE is wrong, which is why the shipped fix is not that.** `MultiplicativeRankingPolicy` scores a
PRODUCT of relevance and retrievability, so a 0 annihilates a candidate rather than ranking it low: with it,
`GraphMemoryRankingGoldenTests`' hop-1 and hop-2 entries did not move down the expected order, they VANISHED
from the result. Under a shipped policy that deletes graph traversal.

**So `GraphNode` carries `Matched` (D97), and the numbers above are what shipped.** A read that scores
relevance sets it; a walk, a fetch by id or a query-less enumeration reports `Relevance 0` with `Matched
null`, and a multiplicative policy then omits the relevance factor instead of multiplying by it. The
Matched-aware engine measures **byte-identically** to the table above on all sixteen cells — the arms here
use RRF, where 0 was already safe — while the golden and the "model-free ranking has no headroom" finding
both stay green, which the 0-only version broke. RRF is deliberately unchanged: placing last on one of three
summed signals already means "no relevance evidence".

**A recorded conclusion survives only because the fix is narrow, which is worth knowing.** Under the 0-only
version, `MemoryVerifiedReinforcementTests`' "model-free policy choice has no headroom left on this corpus"
broke — the two rankers diverged (pollution 0.333 against 0.351, RRF ahead), because that indistinguishability
was an artifact of relevance being a constant. Under D97 it holds, since SEEDED nodes are untouched and only
walked ones changed. The finding is therefore intact and newly bounded: it is about the ranking of scored
candidates, and says nothing about unscored ones.

**Two harness defects were caught before publishing, and both would have produced a wrong headline.** The
first run benchmarked `SemanticSeedOptions.K = 0` against a cosine baseline, which is measuring a misconfiguration —
the complaint one vendor levelled at another's published LoCoMo table. The second was worse: the arms shared
a store, and **a recall reinforces what it returns**, so adding a fourth arm moved `lyntai` from 10.0% to
5.5% with the seed and data unchanged. Same-seed drift is the tell. Each arm now ingests into a pristine
store; `MemoryReinforcementEffects.None` would have isolated them more cheaply and was rejected because its
own doc calls it the worst arm for recall quality, which would bias the comparison toward this library.
<br>**The control that says the fix worked is a REPEAT**: two independent runs of the isolated harness are
byte-identical in all sixteen cells. Same-seed reproducibility is exactly the property contamination
destroyed, so it is the property worth checking — and it is cheap, which is the argument for running it
rather than reasoning that the stores are now separate.

**What this is NOT — and the first reading of it here was wrong about this.** It is not a ranking against
Mem0, Zep or Letta: the QA half needs a reader model, the published numbers use a frontier one, and the
reader sets the ceiling far more than the memory layer does. It is one benchmark, one embedder, 200 of 1540
questions.

**More importantly, `vector` is a PERFECT ARCHIVE and this engine is deliberately not one.** Plain cosine
never decays, never buries, and keeps every turn equally retrievable forever. LoCoMo distributes its
questions uniformly over months of history, so it rewards exactly that and penalises forgetting. **A
once-mentioned January turn that was never referred to again is, by design §5.7.0's own objective,
correctly buried** — and §5.7.0 optimises miss and pollution under invariants about authoritative facts and
conflicts, none of which this benchmark measures. Reading 31% against 80.5% as "worse" imports the field's
objective and judges this design by it.

**So `RetrievabilityWeight = 0` is not a target configuration, and an arm using it is not a goal to climb
toward.** That setting measures this engine with its defining feature switched off; whatever score it
reaches says only that a disabled decay model behaves like a vector index. Optimising toward it would end
with a vector store wearing a graph engine's name.

**The narrower claim that DOES survive, and it is the one worth acting on.** "We deliberately do not return
that" is defensible; "we returned twenty items and they were the wrong twenty" is not. The evidence was
stored, embedded and reachable, and the engine spent its slots elsewhere — which is why D97, found through
this benchmark, was a real defect on any philosophy and stands independently of it.

**The residual gap is the DESIGN, and a second ladder proves it rather than assuming it** (`memory-locomo
--retrieval --n 200`, every arm keeping retrievability at its shipped default). **Re-measured 2026-08-29
after the per-question isolation fix** (`docs/task-archive.md` Part 118) — the right-hand column is what this
table said before it, kept because the gap between the columns is itself the finding:

| arm | evidence-hit@20 | as first published (questions shared a store) |
|---|---|---|
| `lyntai` | **54.5%** | 31.0% |
| `+sem` (`SemanticSeedOptions.K = 20`) | **57.5%** | 36.0% |
| `+sem+hop0` (`HopWeight = 0`) | 31.5% | 11.5% |
| `+sem80` (`SemanticSeedOptions.K = 80`) | 55.0% | 30.0% |
| `+sem80+hop0` | 41.0% | 17.5% |
| `vector` | 80.5% | 80.5% |

**`vector` is the control that makes the other five readable.** It never touches the graph store — plain
cosine over the same embedder — so isolation could not move it, and it did not move by a tenth of a point
across a 22-minute re-run. Every engine arm gained 20–25 points; the arm that structurally could not gain,
did not.

Both misallocation hypotheses are still refuted, and in the same direction. **Graph traversal is carrying
the arm, not stealing slots** — `HopWeight = 0` costs **23.0 points** (24.5 before isolation). And MORE
semantic seeds still make it WORSE (57.5 → 55.0), which is **D82** behaving as documented: RRF ranks by
competition, so widening one signal re-ranks every candidate within it. The effect is **−2.5 points** where
the contaminated run read −6.0, so isolation shrank it without reversing it.

**The pool provably contains the evidence, so this is not a seeding problem either.** The `+sem80` arm seeds
the top-80 by cosine, which contains cosine's top-20 by construction, and that top-20 holds the evidence
80.5% of the time. So the candidate pool holds it at least 80.5% of the time and the arm returns it 55.0% of
the time: **roughly twenty-five points are lost ranking candidates that were present** — half what the
contaminated run reported. The signal demoting them is retrievability — old, mentioned once, never
reinforced. That is the decay model doing its job, not misallocating slots.

**So the boundary is located.** D97 was the defect, worth about twenty points and independent of any
philosophy. What remains is the design: every knob that closes it turns forgetting down, and the two that
leave forgetting alone both made things worse.
<br>**A second twenty-odd points turned out to be the INSTRUMENT, not the design.** The isolation fix moved
`lyntai` 31.0 → 54.5 without touching the library at all, so the gap this section set out to explain was
roughly half harness. What survives is every comparison made WITHIN a regime — which is all of the
reasoning above, because each hypothesis was tested by an arm difference rather than by an absolute.

**What LoCoMo is FOR here: a differential instrument, not a scoreboard.** It stresses the archival axis the
synthetic corpus cannot, which is precisely why it exposed a defect 3429 tests were blind to. The benchmark
that would test THIS design's claims is one where forgetting is supposed to HELP — superseded facts,
knowledge updates, distractors that should be suppressed. **LongMemEval's knowledge-update and temporal
categories** are the closer fit, and on those a working decay model should beat a flat archive rather than
apologise to it.

### Where the −26 actually goes, and two hypotheses that died finding out (`memory-locomo --retrieval`, 2026-08-31) <!-- result: id=locomo-sem-rel-only-n200 arm="`+sem+rel-only` (semantic seeds on, `RetrievabilityWeight = 0` and `HopWeight = 0` — every other vote off)" metric=evidence-hit@k n="200" value="63.5%" ships=no status=CURRENT -->

Three ladders, 200 questions, model-free except where a judge is named, all against `vector` at **80.5%**.
Opened because the owner asked why search quality is not good enough; the gap turned out to decompose
cleanly, and neither of the first two explanations survived.

| arm | multi-hop | temporal | open-domain | single-hop | **overall** |
|---|---|---|---|---|---|
| `lyntai` (shipped) | 45.9% | 47.6% | 33.3% | 62.4% | **54.5%** |
| `+sem` (`SemanticSeedOptions.K = 20`) | 51.4% | 50.0% | 33.3% | 65.1% | 57.5% |
| `+sem80` | 51.4% | 47.6% | 33.3% | 61.5% | 55.0% |
| `+forget0` (`RetrievabilityWeight = 0`) | 51.4% | 54.8% | 41.7% | 67.0% | **60.0%** |
| `+rel-only` (also `HopWeight = 0`) | 51.4% | 54.8% | 41.7% | 67.0% | 60.0% |
| `+sem+mult` (magnitude preserved) | **64.9%** | 64.3% | 41.7% | 61.5% | 61.5% |
| `+sem80+mult` | 27.0% | 9.5% | 8.3% | 25.7% | **21.5%** |
| `+forget0+oracle` (PERFECT judge) | 64.9% | **85.7%** | 58.3% | 80.7% | **77.5%** |
| `vector` (plain cosine) | 81.1% | 81.0% | 58.3% | 82.6% | **80.5%** |

`items/q` is 20.0 on every arm, so **nothing is filtered before ranking** — the column exists to catch that
and it is clean.

**Decay costs 5.5 points and no more.** `+forget0` removes forgetting's vote from ranking and improves every
category. LoCoMo rewards a perfect archive by construction, so some of this is the benchmark punishing the
design's premise rather than a defect — but it bounds that share at 5.5 of 26.

**HYPOTHESIS 1, REFUTED: "the edges aren't connecting evidence to queries."** The n-shot walk recovers only
+1.5/+1.0 (Part 118), so a map argument predicts recovery the walk does not deliver — but **D59 already
decomposed this**: 100% of misses are reachable-but-outranked, 0% unreachable. The pool holds the evidence.

**HYPOTHESIS 2, REFUTED: "RRF flattens the cosine magnitude, so preserve it."** `MultiplicativeRankingPolicy`
nets **+1.5** overall. It is not nothing — multi-hop goes 51.4 → 64.9, exactly what a PERFECT judge achieves
there — but single-hop pays it back (67.0 → 61.5). And `+sem80+mult` **collapses to 21.5%**: multiplicative
ranking over a large pool is actively destructive, which is worth knowing before anyone reaches for it.

**What survives is sharper, and it was visible in the first table.** `+sem` (57.5%) is WORSE than `+forget0`
(60.0%). At `SemanticSeedOptions.K = 20` the pool provably contains cosine's entire top-20 — the exact set that
scores 80.5% — and the engine ranks them out. At 80 seeds it holds cosine's top-80 and still scores 55.0%.
**Adding good candidates makes it worse.** `GraphNode.Relevance`'s own contract names the mechanism: for a
lexical hit it is "a backend's own normalized rank POSITION within one seed, not a portable score", while a
semantic seed carries a COSINE. The fusion treats two incomparable scales as one signal.

**The judge recovers what ranking loses, and is still not enough.** A perfect judge promoting before the cut
takes 60.0% → 77.5%, beating cosine on temporal (85.7 vs 81.0) and tying it on open-domain. That is D59
confirmed on this workload. But **cosine at 80.5% beats the engine WITH a perfect judge**, so on a
uniform-history search workload the graph is not paying for itself, and no arm on this ladder makes it.

**Read the oracle as a CEILING.** It endorses exactly the evidence LoCoMo names, agrees with the metric by
construction, and says nothing about any real model's accuracy. Its purpose is to price the mechanism before
spending a model run — the stance `memory-annotation` takes with a perfect annotator.

**CONFIRMED 2026-08-31, on a PRE-REGISTERED prediction.** The arm that isolates it — `+sem+rel-only`,
semantic seeds present and every other vote off, so relevance alone orders a pool provably containing
cosine's entire top-20 — was written with both branches stated in the source before it ran: *reaching ~80%
means the WEIGHTS diluted a good ordering; staying near 60% means the defect is RELEVANCE ITSELF.*

| arm | multi-hop | temporal | open-domain | single-hop | overall |
|---|---|---|---|---|---|
| `+sem+rel-only` | 51.4% | 64.3% | 41.7% | **69.7%** | **63.5%** |

**63.5% is the second branch.** Strip every other vote, hand relevance the right candidates, and it still
loses 17 points to cosine. **The weights were never the problem**, which retires weight-tuning as a direction
— though the arm is now the best mechanical configuration measured, and it is one line.

**The mechanism is visible in the harness's own control output**, and it is why no weighting can fix it:

```
returned Relevance : 0.963, 0.700, 0.738, 0.690, 0.900, …
semantic  cosines  : 0.742, 0.732, 0.726, 0.723, 0.721, …
```

Lexical hits carry a rank POSITION (0.963, 0.900), semantic seeds carry a COSINE (0.742, 0.732), and the two
are compared directly on one field. The lexical values sit systematically higher, so a semantic candidate is
outranked *by construction* however good its similarity — which is also why ADDING semantic seeds makes the
arm worse rather than better.

**What that leaves as a direction** (a design question, not a measured result): relevance has to be
comparable before it is ranked — normalised per SOURCE, or each source ranked within itself and fused
afterwards. Neither is measured, and `GraphNode.Relevance`'s contract would have to change, since it
currently promises only a backend-specific position.

**What is NOT settled.** Whether a real judge approaches 77.5% (the arm exists, `+forget0+judge`, and has
never completed). Whether multi-hop's residual — 64.9% even with a perfect judge, against cosine's 81.1% — is <!-- result: id=locomo-multihop-residual-oracle arm="`+forget0+oracle` (PERFECT judge) multi-hop residual against `vector`" metric=evidence-hit@k n="200" value="16 points" ships=no status=RETRACTED -->
evidence outside `VerificationDepth`, which no judge quality could reach. And every figure here is one
embedder on one workload.

> **Both open questions were answered later and neither survived as posed** (2026-09-02/03, subsections
> below). The multi-hop residual reads **3.2** points at full sample rather than 16, on a pre-fusion premise
> **D103** had already superseded. And `+forget0+judge` no longer exists: the real-judge arm was re-aimed
> onto `+sem+rel-only`, whose oracle reaches 92.5% where `+forget0`'s reaches 74.6% — so "does a model
> approach 77.5%" was a question about a dominated configuration.

### Per-source fusion clears cosine, and the 63.5% bar above is now STALE (`memory-locomo --retrieval`, seed-source fusion, 2026-08-31) <!-- result: id=locomo-fusion-sem-rel-only arm="`+sem+rel-only` under per-source seed fusion (`IMemorySeedSource`, D103); SQLite store, `nomic-embed-text`" metric=evidence-hit@k n="unstated" value="83.0%" ships=no status=CURRENT supersedes="locomo-sem-rel-only-n200" -->

`docs/task-archive.md` Part 235's surviving direction — make `Relevance` comparable before it is ranked —
shipped as `IMemorySeedSource` (**D103**): `ReciprocalRankFusionPolicy` now fuses each source's own ranked
list instead of one pooled `Relevance` field. **This measurement is what the 63.5% figure two sections up
predates.** That number was `+sem+rel-only` BEFORE this shipped; the same arm, same name, now reads 83.0%
below — read the table here, not the one above, for the current best mechanical arm.

**Controls, read before the result** — the same three the harness has reproduced before, run twice on tree
`7e77020`:

| control | why it cannot move | expected | read (both runs) |
|---|---|---|---|
| `vector` | never touches the graph store | 80.5% | **80.5%** |
| `+rel-only` | no semantic seeds, so a single ranked source | 60.0% | **60.0%** |
| `lyntai` (shipped) | semantic seeds not registered | 54.5% | **54.5%** |

All three reproduce to the decimal — the harness did not move under this change.

**One designated control failed, and it was a SPEC error, not a harness defect.** The design spec listed
`+sem+rel-only` as a control expected to *"reproduce 63.5% with the plumbing inert."* It read 83.0% instead:
that arm registers semantic seeds, so it is the arm MOST sensitive to per-source fusion, not the least. It
was never a valid control — `vector` was always the real one.

| arm | before this change | run 1 | run 2 |
|---|---|---|---|
| `+sem+fuse` | — | **76.5%** | **76.5%** |
| `+fuse` (lexical only) | 54.5% (`lyntai`) | **54.5%** | **54.5%** |
| `+sem+rel-only` | 63.5% | **83.0%** | **83.0%** |
| `vector` (plain cosine) | 80.5% | 80.5% | 80.5% |

**The first pre-registered branch fired.** `+sem+fuse` clears the old 63.5% bar; `+fuse` (a single source,
where the weight-shift confound the spec pre-registered cannot occur by construction) does not move at all.
The gain is per-source fusion, not an incidental reweighting.

**`+sem+rel-only` at 83.0% is the actual best mechanical arm, above plain cosine's 80.5%** — the first time
this engine has beaten the formula it was losing to by 26.0 points two sections up. Category detail:
multi-hop 81.1% against cosine's own 81.1% (equal, where `docs/task-archive.md` Part 235 recorded 64.9%
even with a PERFECT judge), single-hop 87.2% against cosine's 82.6%.

**Reproducibility establishes DETERMINISM, not a noise bound.** The two runs' arm tables are byte-identical.
This harness is deterministic by construction — each conversation ingested once, cloned per question,
embeddings cached — so there is no stochastic variation to bound; the repeat can only detect
non-determinism, and found none.

**Two readings that are not independent evidence.** `+sem+fuse` and `+sem` are identical at 76.5% because
`+sem+fuse` IS `+sem` once RRF fuses per source by default — their config tuples match except the name
(`MemoryLocomoBench.cs:247` vs `:358`), so the arm is permanently redundant and will never disagree with its
twin. And `+sem+hop0` still matches `+sem` exactly, as before.

**An unplanned side effect: the Multiplicative arms moved**, `+sem+mult` to 60.5% and `+sem80+mult` from a
recorded 21.5% to **57.0%**. `MultiplicativeRankingPolicy` itself was never touched — `SemanticSeedSource`
now reports `Matched = true` carrying its clamped cosine as `Relevance`, so the policy's own
`Matched is null ? 1 : Relevance` read stops treating a semantic seed as "nobody asked" and starts
multiplying an honest relevance into the product.

**No default moved.** Clearing the bar earns the seam; whether semantic seeding ships ON is a separate
decision on separate evidence, and `SemanticSeedOptions` is still not registered by default.

**83.0% is a SQLite number, and one of three shipped backends never reaches this path.**
`InMemoryMemoryGraphStore` reports a flat `Relevance` for every lexical match and `Matched null` for every
subject fetch, so under the shipped default registration (lexical + subject, no semantic) no candidate
carries a rank and the recall runs the pre-branch pooled fallback — this measurement never touches that
store. `MultiplicativeRankingPolicy` still ranks on the pooled `GraphNode.Relevance` field, so the
mixed-scale defect this fusion removes survives unchanged in the second shipped ranking policy; see
`docs/DECISIONS.md` D103.

**What this does not settle**, same shape as the section above: one workload (LoCoMo rewards a perfect
archive and penalises forgetting by construction; LongMemEval knowledge-update — the shape this design
actually claims — was not run); one embedder (`nomic-embed-text` only; `embeddinggemma:300m` sits on this
machine unused, and earlier sweeps found the two disagree by ~2.5× and reverse sign on one shape); and the
real-judge arm (`--no-judge`), still unrun. `items/q` is 20.0 on every arm, so no arm is filtered before
ranking here either.

### RETRACTED — ranking × the walk are NOT shown to be superadditive: the n = 100 series was underpowered, and the full sample reads +1.0 (`memory-locomo`, QA halves, 2026-09-01) <!-- result: id=locomo-interaction-full-n1540 arm="ranking × walk interaction, `lyntai` → `lyntai-fused-3shot` at `--n 1540 --seeds 16` (gemma3:4b reader/judge, nomic-embed-text, SqliteMemoryGraphStore)" metric=token-f1 n="1,540" value="+1.0" ships=no status=CURRENT supersedes="locomo-interaction-n100-series" -->

This subsection previously reported an interaction between per-source ranking (**D103**) and the n-shot walk <!-- result: id=locomo-interaction-n100-series arm="the same ranking × walk interaction measured at n = 100 (`--seeds 16`) — three successive readings" metric=token-f1 n="100" value="+6.7 / +4.2 / +6.8" ships=no status=RETRACTED -->
(**D100**), then amended it twice — **+6.7**, corrected to **+4.2**, re-corrected to a "ceiling" framing
across three `--seeds` points, **+6.7 / +4.2 / +6.8**. **All three readings were taken at n = 100.** A run
over the COMPLETE published LoCoMo QA set — 1,540 questions, everything else unchanged — puts the interaction
at **+1.0**, indistinguishable from zero at that sample size. **This retracts the superadditivity claim; it
does not add a fourth point to the series.**

**Instrument.** `node devtools/dev.mjs memory-locomo --n 1540 --seeds 16 --dump`, the LoCoMo QA half, ALL
1,540 questions (841 single-hop, 282 multi-hop, 321 temporal, 96 open-domain — the complete set the earlier
n = 100 runs sampled FROM), reader and judge both `gemma3:4b` (local) via Ollama, embedder
`nomic-embed-text`, `SqliteMemoryGraphStore`, seed 12345. Wall clock 18,115.8s. Raw output: the gitignored
`devtools/_locomo-full.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->.
Same conventions as every QA table here: `token-F1` is model-free and PRIMARY, `judge` is the same small
model self-grading (reported beside F1, not instead of it), `unknown` counts a reader admitting the excerpts
held nothing.

| arm | token-F1 | exact | judge | unknown | items/q | chars/q |
|---|---|---|---|---|---|---|
| `lyntai` | 19.6% | 8.7% | 38.3% | 496 | 20.0 | 2273 |
| `lyntai-fused` | 29.1% | 13.1% | 52.8% | 233 | 20.0 | 2275 |
| `lyntai-fused-3shot` | 43.9% | 20.2% | 67.7% | 59 | 40.0 | 6536 |
| `lyntai-2shot` | 32.3% | 16.2% | 52.6% | 261 | 40.0 | 5462 |
| `lyntai-3shot` | 33.4% | 16.6% | 53.6% | 240 | 40.0 | 6285 |
| `vector` | 42.4% | 19.0% | 67.7% | 105 | 20.0 | 3460 |
| `vector-40` | 44.5% | 20.7% | 69.3% | 55 | 40.0 | 6780 |
| `full` | 12.1% | 2.8% | 38.0% | 1 | 601.4 | 101209 |

**`full` is a FLOOR, not a ceiling**, as at every other n in this section — it exceeds this reader's measured
85,000-character window and its head is dropped.

**1. The interaction, decomposed at full sample.** Ranking alone, one shot (`lyntai` → `lyntai-fused`):
**+9.5** (19.6 → 29.1). The walk alone on shipped ranking (`lyntai` → `lyntai-3shot`): **+13.8**
(19.6 → 33.4). Sum of parts: **23.3**. Joint gain (`lyntai` → `lyntai-fused-3shot`): **+24.3**
(19.6 → 43.9). **Interaction: +1.0.** An interaction is a difference of differences, so its sampling error
runs roughly double any one component's — a +1.0 reading here is not a small positive effect, it is a
reading with no sign anyone should trust. **Superadditivity is not supported at power.** Retire the claim
rather than publish a fourth point.

**2. The methodological failure, because it matters more than the number.** The figure was corrected
twice — `+6.7 → +4.2 → +6.8` — while the real defect was never the arithmetic: the quantity was not
MEASURABLE at n = 100 at all. At that sample, multi-hop alone was ~18 questions, so one multi-hop question
moves that stratum by roughly 5.6 points, and a difference-of-differences over the whole set inherits error
from all four terms at once. **Correcting a number that should never have been reported as a point estimate
is not a fix** — it repeats the mistake with a smaller error bar someone can point to. Before publishing a
derived quantity, especially a difference of differences, check whether the sample supports it — not after
the second correction.

**3. RETRACT the category wins too.** At n = 100 (`--seeds 16`), `lyntai-fused-3shot` appeared to beat
`vector-40` by **+6.2** on multi-hop (45.0% vs 38.8%) and **+5.5** on open-domain (33.3% vs 27.8%). At the
full 1,540-question sample:

| arm | multi-hop | temporal | open-domain | single-hop | overall |
|---|---|---|---|---|---|
| `lyntai-fused-3shot` | 38.8% | 32.3% | 13.4% | 53.4% | 43.9% |
| `vector-40` | 39.4% | 34.4% | 13.3% | 53.7% | 44.5% |
| **delta** | **−0.6** | **−2.1** | **+0.1** | **−0.3** | **−0.6** |

Three of four category deltas flip sign and the fourth (open-domain) collapses from +5.5 to +0.1. **Every
category win reported at n = 100 was noise** — retract it rather than re-explain it.

**4. What survives, and it is the better result for being well-powered now.** Both mechanisms are real,
large, and no longer close to their own noise floor. Ranking is worth **+9.5** at one shot (19.6 → 29.1) and
**+10.5** at three shots (33.4 → 43.9). The walk is worth **+13.8** on shipped ranking (19.6 → 33.4) and
**+14.8** on fused ranking (29.1 → 43.9). Together, shipped-defaults-one-shot to fused-three-shot:
**19.6% → 43.9%**, with `unknown` falling from **496 to 59** of 1,540 questions. None of that needed the
interaction to be real — it was never the headline that mattered.

**5. Against the controls, at full-sample resolution.** `lyntai-fused-3shot` beats plain `vector` by **+1.5**
(42.4 → 43.9) and trails the size-matched `vector-40` by **−0.6** (44.5 → 43.9). At n = 1,540, one question
is worth ≈0.065 points (100/1540), so −0.6 points is about **9 questions** — small enough to call the two
arms level on accuracy. **The remaining gap is efficiency, not capability**: `lyntai-fused-3shot` spends
**6,536** chars/q against `vector`'s **3,460** (≈1.9×) for a comparable score, and against `vector-40`'s own
**6,780** chars/q it is now both item-matched (40.0/q) and roughly character-matched while still trailing by
0.6. **Read those two comparisons together, never the first alone** — finding 9 decomposes them, and the
1.9× is an item-COUNT difference rather than a per-item one. This is the same "mostly volume, not form" reading `docs/task-archive.md` **Part 134** reached at
n = 100 — there, the trail against `vector-40` narrowed **11.3 → 5.3 → 2.5** points as `--seeds` rose. That
qualitative conclusion survives the full sample where the interaction claim did not, because it was never a
difference of differences.

**6. `open-domain` is the weakest category for every arm, and now it is a real signal rather than noise.**
13.4% (`lyntai-fused-3shot`) / 13.3% (`vector-40`) / 11.0% (`vector`) — on 96 questions, not 6. It is the
weakest category for the engine AND for plain cosine, which argues the ceiling here is set by the question
class (open-domain LoCoMo questions ask about world knowledge outside any single conversation) rather than
by which retrieval mechanism answers it.

**7. The `judge` column is MEASURABLY generous, by about 12 points — and this is why token-F1 is primary.**
Calibrated 2026-09-01 against the `--dump` record of that same run: 40 `lyntai-fused-3shot` items, 20 the
judge called correct and 20 it called wrong, re-graded by a stronger reader. **Roughly 5 of 20 (~25%) of its
CORRECT verdicts do not survive** — gold *"Restoring cars"* against the answer *"Fixing up things"*, gold
*"to remind you of the good vibes"* against *"like having joy in your pocket"* — and about 3 of 20 (~15%) of
its INCORRECT verdicts are defensible answers. Applied to its 1042 yes / 439 no, the corrected count is
≈848, i.e. **≈55% against the reported 67.7%**.
<br>**The bias is COMMON-MODE**: the same 4B model grades every arm, so it distorts absolutes far more than
differences — which is the measured justification for `docs/task-archive.md` Part 233's "only the arm
difference transfers" rule, previously held on principle alone.
<br>**Stated as a lower bound, because the calibration has a flaw of its own**: the re-grade was NOT blinded
— the grader could see each verdict while judging it, which anchors in the direction that flatters the
judge. 25%/15% are therefore floors on disagreement, not point estimates. A blinded re-grade is the honest
version and has not been run.

**8. LoCoMo's temporal golds are written in RELATIVE phrasing, and that penalises a correct answer.** Found
in the same 40-item sample, and it is a caution about the INSTRUMENT rather than about any arm. Gold *"the
week before 21 January, 2022"* against the answer *"21 January, 2022"*; gold *"The week before March 27,
2023"* against *"27 March, 2023"*; gold *"The sunday before 25 May 2023"* against *"Saturday"*. A reader
that retrieves the right turn and answers with the absolute date scores zero on both token-F1 and the judge.
**That plausibly explains why `temporal` sits at 32–34% for EVERY arm including plain cosine** — the
category may be measuring answer formatting as much as memory. Anyone reading a temporal figure here, ours
or a published one, needs this. Not quantified: what share of the temporal deficit it accounts for.

**9. What the 6,536 chars are MADE of — and the ≈1.9× is an item-COUNT comparison, not a per-item one.**
Most of it derived 2026-09-02 from this table, the harness and the corpus with NO new run, and the upgrade
and duplication counts then measured by `memory-locomo --composition` (`docs/task-archive.md` Part 135). Dividing each
row through by its own `items/q`, net of the `items − 1` join newlines:

| arm | items/q | chars/item |
|---|---|---|
| `lyntai` / `lyntai-fused` | 20.0 | 112.7 / 112.8 |
| `lyntai-2shot` | 40.0 | 135.6 |
| `lyntai-3shot` | 40.0 | 156.2 |
| `lyntai-fused-3shot` | 40.0 | **162.4** |
| `vector` / `vector-40` | 20.0 / 40.0 | 172.1 / 168.5 |
| `full` | 601.4 | 167.3 |

**Metadata is zero, and there is no format to compress.** The prompt context is `string.Join("\n", pieces)`
over `i.Content ?? i.Headline`, and both families draw from the SAME strings — one
`[date] (dia_id) speaker: text` per turn feeds the vector index and `RememberAsync` alike. The graph arms
serialise nothing cosine does not also carry.

**The composition is headline versus content, and nothing else.** A derived headline is capped at
`GraphMemoryOptions.HeadlineChars` = 120 (`MemoryHeadline.Derive`), which puts an all-headline floor at
**113.9** chars/item on this corpus against a full-content ceiling of **168.0**. The one-shot arms sit at
112.7 — on that floor, within 1% — and the three-shot arms at 156.2 / 162.4, near the ceiling.

**Measured, all 1,540 questions.** `node devtools/dev.mjs memory-locomo --composition --seeds 16 --n 1540`,
model-free, 1,269.7s. Raw output: the gitignored
`devtools/_locomo-composition.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

| arm / shot | items/q | new | upgr | headline n / chars / avg | content n / chars / avg | chars/q |
|---|---|---|---|---|---|---|
| `lyntai-fused-3shot` shot-1 | 20.0 | 20.0 | 0.0 | 20.0 / 2,256 / 112.8 | 0.0 / 0 / — | 2,275 |
| `lyntai-fused-3shot` shot-2 | 40.0 | 20.0 | 16.0 | 24.0 / 2,706 / 112.7 | 16.0 / 2,804 / 175.2 | 5,548 |
| `lyntai-fused-3shot` shot-3 | 40.0 | 0.0 | 16.0 | 8.0 / 900 / 112.5 | 32.0 / 5,597 / 174.9 | **6,536** |
| `vector-40` | 40.0 | — | — | 0.0 / 0 / — | 40.0 / 6,741 / 168.5 | **6,780** |

**The control passes EXACTLY, which is what makes the rest readable.** 6,536 and 6,780 reproduce the QA
table's own rows to the character, and shot-1's 2,275 reproduces the `lyntai-fused` row — three independent
exact matches, so this decomposes those arms and not neighbours of them.

**The upgraded share is 80%, and it is STRUCTURAL rather than empirical** — counted directly by
`memory-locomo --composition` (built 2026-09-02, `MemoryWalkStep.UpgradedCount`) rather than inferred.
The walk holds 20 headlines after shot 1, upgrades exactly `MemoryWalkOptions.SeedsPerStep` per expansion
and fills to `MaxItems`: **16 + 16 = 32 of 40**, so 8 items are still a headline at shot 3 because the SEED
BUDGET bounds the raise, not the corpus. The figure is `SeedsPerStep × (shots − 1) / MaxItems` at this
benchmark's `--seeds 16`, which is a harness parameter and not a property of the engine.
<br>**A first reading of this table inverted the mean lengths instead and got ≈90%** — directionally right,
wrong as a point estimate, because it assumed both sub-populations sit at the corpus mean and **neither
does**: the upgraded items measure **174.9** chars against a corpus mean of 168.0, and the 8 survivors
**112.5** against the 113.9 all-headline floor. **It was pre-registered at 88–95% and is recorded as
falsified**, since a structural quantity was never the kind of thing an interval over corpus statistics
could have found.
<br>**The walk's content items are 3.8% LONGER than cosine's** (174.9 against `vector-40`'s 168.5), which is
a small finding in its own right: expansion walks toward richer turns. It is also why the mixture had to be
measured rather than inverted — assuming the corpus mean for both populations is what produced the ≈90%.

**Node duplication across shots cannot happen.** `MemoryWalkState.Hold` keys on `MemoryRef`: re-encountering
a held entry raises it from headline to content and increments `MemoryWalkStep.UpgradedCount`, never taking
a second slot. The obvious compression hypothesis is closed by construction rather than by measurement.
<br>**Near-duplicate text across DISTINCT nodes measures ZERO**, which is the half construction could not
settle: exact pairs, containment pairs and token-Jaccard ≥ 0.8 pairs are all **0.00 per question on BOTH
arms**, over 61,600 items each. So the corpus is not repeating itself into either context, and there is
nothing for a deduplication pass to reclaim.
<br>**A zero from a counter nobody tested would look identical**, so the mode carries a POSITIVE CONTROL over
a known-duplicate set and WITHHOLDS the table when it fails. It reports `PASS - exact 1/1, contained 3/3,
near 3/3` on this run, and it was proven on the red path: neutering the counter to return zeros printed
`FAIL` and refused the table.

**So the walk's cost is in SLOTS, not characters.** At matched item count `lyntai-fused-3shot` spends
**6,536** against `vector-40`'s **6,780** — **3.6% LESS**, because a fifth of its slots are still headlines.
It needs 40 slots to reach what cosine does in 20, while being the cheaper arm per slot. An efficiency claim
taken from the 20-item `vector` row alone inverts which arm is thriftier.

**Control.** The corpus reconstruction reproduces the `full` row EXACTLY — 601.4 items/q and 101209 chars/q,
to the character — which is what licenses reading 113.9 and 168.0 as this table's own floor and ceiling
rather than as two separately-measured numbers.

**Not settled.** The composition is a property of `--seeds 16` on THIS corpus. Every question saturated —
`items/q` is exactly 40.0 and `headline` exactly 8.0 across all 1,540, so no short-recall question fell short
of the seed budget here — but a corpus with sparser recalls would not, and the 80% would fall with it. And
the figures are one embedder (`nomic-embed-text`) on one workload, like everything else in this section.

**What this does not settle.** Absolute values are a property of a 4B local reader and are NOT comparable to
any published figure from any other system — only ARM DIFFERENCES transfer. One workload (LoCoMo), one
embedder (`nomic-embed-text`), one reader tier (`gemma3:4b`). **n = 1,540 now** — the complete published QA
set, not a sample of it — so resolution is ≈0.065 points per question, which is what makes −0.6 readable as
small and +10.5 as large; n = 100's ≈1-point resolution supported neither call, for the interaction or for
the category breakdown. The retraction closes the superadditivity question rather than leaving a fourth
pending number — nothing here licenses reopening it without a reason to expect the earlier reading was
right.

### `evidence-hit@k` cannot see TRUNCATION, and only the graph arms truncate (2026-09-02) <!-- result: id=locomo-truncation-marker-n5882 arm="shipped headline derivation — `MemoryHeadline.Derive` at `GraphMemoryOptions.HeadlineChars` = 120, replayed over every LoCoMo turn (the one-shot graph arms are 100% truncated)" metric=marker-survival n="5,882 LoCoMo turns" value="5,882 of 5,882 — 100.00%" ships=yes status=CURRENT -->

**One arm, two metrics, opposite verdicts against the same control.** `+sem+rel-only` (retrieval ladder) and
`lyntai-fused` (QA table) are the SAME configuration under two names — `ReciprocalRankFusionPolicy` with
`RetrievabilityWeight = 0` and `HopWeight = 0`, semantic seeds at `RecallLimit`, one shot, 20 items. It
scores **83.0% evidence-hit against plain cosine's 80.5%** (n = 200) and **29.1% token-F1 against cosine's
42.4%** (n = 1,540). Retrieval says it wins by 2.5; the reader says it loses by 13.3.

**The mechanism, measured.** A recall returns headlines (**D100**), and `MemoryHeadline.Derive` cuts at
`GraphMemoryOptions.HeadlineChars` = 120 on a word boundary with a `…` marker. Every retrieval-ladder arm is
ONE SHOT — only the two three-shot arms are in `MultiShotArms`, so every other walk breaks at step 1 — so
**100% of its items are truncated**; the cosine arms never truncate.
<br>The four rows below are computed over all 5,882 LoCoMo turns by replaying `MemoryHeadline.Derive`'s own
rule against the dataset (the gitignored `devtools/_part135-truncation.mjs` <!-- link-ok: gitignored scratch, named as provenance; re-creatable from the rule it replays -->), not sampled:

| | |
|---|---|
| turns longer than 120 chars | **72.5%** (4,265 of 5,882) |
| answer text surviving truncation, over those turns | mean **56.2%**, p50 54.6%, p10 30.6% |
| turns keeping under half their text | 30.4% of the corpus |
| **`dia_id` marker surviving truncation** | **5,882 of 5,882 — 100.00%** |

**That last row is the whole finding.** The harness scores a hit with `Contains("(" + dia_id + ")")`, and the
marker sits in the 44-character HEADER every turn carries — `[date] (dia_id) speaker: ` — so it is never the
part that gets cut. Evidence-hit is therefore **structurally incapable** of distinguishing a slot holding a
whole turn from one holding half of it, and the arms it compares differ in exactly that way.

**What this does NOT do is invalidate the retrieval ladder.** Finding the evidence turn is finding it, and
evidence-hit answers that question correctly — **D103**'s per-source fusion really did move retrieval, and
that result stands. What the metric cannot support is the INFERENCE that usually rides on it: *better
retrieval should mean better answers.* Between a graph arm and a cosine arm it does not follow, because the
two deliver different amounts of the turn they both found.

**It is the mechanism, and the subsection below MEASURES it at +11.7 of a 12.0-point gap.**
`docs/task-archive.md` Part 235's pre-registration guessed it in words — *"the engine returns HEADLINES and
the reader cannot answer from a pointer"* — and nothing quantified it. The quantity is 56.2% of the text,
and the reason nobody caught it is that the one metric watching that stage is blind to it by construction.

**A fixed-slot content experiment in the published table said the opposite, and it did NOT generalise.** <!-- result: id=locomo-fixed-slot-content-n1540 arm="fixed-slot content contrast `lyntai-2shot` → `lyntai-3shot` (identical 40 items, 40% vs 80% content share) at `--seeds 16`" metric=token-f1 n="1,540" value="+1.1" ships=no status=RETRACTED -->
At `--seeds 16` shot 3 discovers nothing new (`new 0.0`, measured), so **`lyntai-2shot` and `lyntai-3shot`
hold the IDENTICAL 40 items** and differ only in content share — 40% against 80%. The composition model
predicts both rows to within one character (135.5 against a measured 135.6; 157.2 against 156.2), which is
what licenses reading the pair as a content contrast at all. **token-F1 moves +1.1**, 32.3% to 33.4%.
<br>**That reading was wrong, and the caveat beside it is why.** The contrast does not settle WHICH items get
upgraded: shot 3 seeds shot 2's newly-discovered neighbours, so +1.1 prices the LOW-value half, while
rehydration upgrades the top-ranked items evidence-hit says carry the evidence. The rehydration arm measured
**+11.7**, ten times the fixed-slot figure. **Read this pair as a warning about extrapolating a
natural experiment**: the contrast was real, correctly computed and correctly caveated, and generalising it
past the population it measured would still have been wrong by an order of magnitude.

**Exposure is per-arm and worth stating separately**, because it is not uniform: the retrieval ladder's arms
are 100% headlines, `lyntai-fused-3shot` is 20% (finding 9's 8 of 40), and every `vector*` and `full` arm is
0%. So the truncation penalty is largest exactly where the retrieval win was measured.

**The blast radius stops at the two FIELD benchmarks, and that is checked rather than assumed.** LoCoMo and
LongMemEval score by matching a marker inside the returned TEXT, which is what exposes them. Every sweep over
this repository's own synthetic corpus scores on returned IDS instead (`RecallQuality` intersects id sets),
so miss-rate and pollution-rate are structurally immune — no figure from `memory-sweep`, `memory-salience`,
`memory-support` or their siblings is touched by this.

**LongMemEval carries the same blind spot and carries it HARDER**, which is the cross-workload check. Its
turns are built `$"{t.Tag} {t.Text}"`, so the marker `clean` / `current@k` / `stale@k` all match on sits at
**position 0** — it cannot be truncated away at any cap. And its turns are far longer: on knowledge-update
haystack, `shot-1` spends 1,169 chars over 10.0 items (**116.9**/item, the headline cap) where `vector`
spends 10,387 over the same 10.0 items (**1,038.7**/item). **A LongMemEval headline delivers 11.3% of its
turn**, against LoCoMo's ~56%.

**The composition model predicts that curve independently, which is what makes it a model rather than a
description.** At `chars/item = u × full + (1 − u) × 120`, shot-3's 416 chars/item implies **u = 32%
upgraded** — and a `--seeds 3` budget over ~20 held items gives 3 + 3 = 6 of 19.9 = **30%**. Two corpora,
two seed budgets, one arithmetic.

**What survives untouched is the finding that matters there.** `clean` asks whether the returned set holds
the current fact and NOT the superseded one — a question about WHICH turns came back, which truncation does
not change. **The 31.4%-against-10.0% discrimination result stands.** What does not follow is the efficiency
reading the char column invites: 1,169 characters against 10,387 is not the same information nine times
cheaper, it is ten pointers against ten documents.

**Sized in the subsection below** by an arm that retrieves identically and returns full content
(`docs/task-archive.md` **Part 136**): **+11.7 of a 12.0-point gap**. Note that ingesting the corpus as
`MemoryGrade.Authoritative` would also return full content — a recall projects `Content` only for that grade
— but it engages the grade carve-out and re-admission at the same time, so it moves ranking too and could
not have isolated this.

### TRUNCATION was the gap: rehydrating the same 20 items closes 11.7 of 12.0 points (`memory-locomo`, rehydration arm, 2026-09-02) <!-- result: id=locomo-rehydrate-full-n200 arm="`lyntai-fused-full` — `lyntai-fused`'s own returned set with each headline swapped for the whole turn (harness rehydration, 20 slots)" metric=token-f1 n="200" value="+11.7" ships=no status=CURRENT supersedes="locomo-fixed-slot-content-n1540" -->

The subsection above found the mechanism and could not size it. This sizes it, and the answer is that
headline truncation accounted for essentially the whole QA deficit against plain cosine.

**Instrument.** `node devtools/dev.mjs memory-locomo --n 200 --no-judge`, 200 questions stratified over
categories 1–4, seed 12345, reader `gemma3:4b` local, embedder `nomic-embed-text`, 2,712.4s. Raw output:
the gitignored `devtools/_locomo-rehydrate.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
`lyntai-fused-full` reuses `lyntai-fused`'s OWN returned set and swaps each item's headline for the whole
turn behind it — identical retrieval, identical ranking, identical 20 slots. **Control: 4,000 of 4,000 items
rehydrated, no misses**, so the two arms differ in truncation and nothing else.

| arm | token-F1 | exact | unknown | items/q | chars/q |
|---|---|---|---|---|---|
| `lyntai-fused` | 29.3% | 13.5% | 29 | 20.0 | 2,289 |
| **`lyntai-fused-full`** | **41.0%** | 21.0% | 13 | 20.0 | 3,583 |
| `vector` | 41.3% | 19.0% | 12 | 20.0 | 3,541 |
| `lyntai-fused-3shot` | 38.0% | 17.0% | 10 | 39.7 | 4,968 |
| `vector-40` | 44.5% | 19.0% | 5 | 40.0 | 6,924 |

**1. Rehydration is worth +11.7 points, and the gap it closes was 12.0.** At n = 200 one question is worth
≈0.5 points, so +11.7 is about 23 questions and far outside noise. `lyntai-fused-full` lands at **41.0%
against `vector`'s 41.3%** — a −0.3 difference, under one question — at the same 20 slots and within 1.2% of
the same context. **The engine's ranking is not worse than cosine. The entire measured QA deficit was
headline truncation**, and the retrieval ladder was right about the ordering all along.

**2. `unknown` is the corroborating column.** The reader said "the excerpts contained none" 29 times on the
truncated arm and 13 on the rehydrated one, against `vector`'s 12. That is the same 20 turns in both graph
arms, so nothing was newly FOUND — the reader simply stopped being handed half a sentence.

**3. One shot with full content beats three shots with mixed content, for less context.**
`lyntai-fused-full` scores 41.0% on 3,583 chars where `lyntai-fused-3shot` scores 38.0% on 4,968 — **+3.0
points for 28% less**. Expansion is a more expensive way to obtain content than returning it, when the
material is already in hand. That does not touch **D100**'s claim, which is about DISCOVERING material a
first load did not hold; it does say the walk is the wrong tool for filling in what a recall already found.

**4. The pre-registered prediction, and the revision that made it worse.** The original call was **38–45%**
and the outcome is 41.0%. It was then revised DOWN to 32–40% before the run, on the fixed-slot contrast in
the retraction subsection above (`lyntai-2shot` → `lyntai-3shot`, identical 40 items, content 40% → 80%, for
+1.1). **That contrast did not generalise**, and the pre-registration said why in advance: shot 3 seeds
shot 2's newly-discovered neighbours, so +1.1 prices the LOW-value half of an upgrade while rehydration
upgrades the top-ranked items. The caveat was right and the revision it accompanied was wrong.

**5. The SHIPPED surface reproduces it exactly.** `MemoryQuery.Detail = MemoryDetail.Full` (**D104**) landed
after this run, and `lyntai-fused-api` asks the engine for whole entries instead of having the harness swap
text in afterwards. On the smoke sample the two arms agree in **every column** — token-F1 55.0/55.0, exact
40.0/40.0, unknown 0/0, items 20.0/20.0, chars/q 3,814/3,814, and every category cell — from two
independently-ingested stores by two different mechanisms. So the +11.7 is not a property of a harness
trick: a consumer passing one parameter gets it.

**Not settled.** One reader tier, one embedder, n = 200 rather than the full 1,540. The category splits are <!-- result: id=locomo-fused-full-multihop-n200 arm="`lyntai-fused-full`, multi-hop category cell against `vector`" metric=token-f1 n="200 (multi-hop cell only)" value="38.0 vs 37.8" ships=no status=RETRACTED -->
not resolved at this sample and do not all point one way — against `vector`, `lyntai-fused-full` reads
multi-hop 38.0 vs 37.8 and single-hop 50.8 vs 48.9, but temporal 25.1 vs 29.2 and open-domain 16.7 vs 25.0.
<br>**CONFIRMED at n = 1,540 on 2026-09-02** (the last LoCoMo subsection below): 42.0 against `vector`'s
42.4, a −0.4 where this read −0.3, so **the parity result holds and the sample size is no longer a caveat on
it**. Two of the category readings above did not survive — multi-hop's near-tie is −4.0 at full sample —
which is what "not resolved at this sample" meant. The remaining caveats in this paragraph stand.
**And this measures a HARNESS arm, not a shipped path**: a consumer reaches the same place with N calls of
`ExpandAsync(reference, hops: 0)`, one per returned item, which is supported but is N store round-trips.
Whether a recall should be able to return content directly is a design question this measurement raises and
does not answer.

### RETRACTED in part — at 40 slots the walk is LEVEL with cosine, not ahead: +0.9 at n = 200 reads −0.4 at n = 1,540 (`memory-locomo`, 2026-09-02) <!-- result: id=locomo-3shot-full-40slot-n1540 arm="`lyntai-fused-3shot-full` (three-shot walk asking `MemoryDetail.Full`, 39.7 items/q) against `vector-40`" metric=token-f1 n="1,540" value="−0.4" ships=no status=CURRENT supersedes="locomo-3shot-full-40slot-n200" -->

**The subsection below was written at n = 200 and its headline did not survive the full sample.** Same four
arms, same seed, everything else unchanged, 1,540 questions
(`node devtools/dev.mjs memory-locomo --n 1540 --no-judge --arms …`, 5,707.6s; raw output the gitignored
`devtools/_locomo-40slot-full.txt` <!-- link-ok: gitignored raw sweep output, named as provenance -->):

| arm | token-F1 | unknown | items/q | chars/q |
|---|---|---|---|---|
| `lyntai-fused-full` | 42.2% | 105 | 20.0 | 3,503 |
| `lyntai-fused-api` | 42.2% | 107 | 20.0 | 3,503 |
| `lyntai-fused-3shot-full` | **44.0%** | 58 | 39.7 | 6,941 |
| `vector-40` | **44.4%** | 50 | 40.0 | 6,780 |

**The claim that survives is LEVEL.** 44.0 against 44.4 at 2.4% more context — the sign flipped from +0.9
to −0.4, so "the first time this benchmark has put the engine ahead" is **retracted**. What is not retracted
is the larger movement: this engine was measured 12 points behind plain cosine before headline truncation was
found and fixed, and it is now within half a point of it.

**The noise floor did what was forecast, which is why −0.4 is readable where +0.9 was not.** The two
byte-identical-prompt arms scored 40.7/41.1 at n = 200 and **42.2/42.2** here — reader nondeterminism
collapsed from 0.4 points to 0.0 on token-F1. A difference of ±1 point was never resolvable at n = 200.

**The pre-registration was RIGHT, and the n = 200 adjudication of it was wrong.** It called *"40–45%,
gaining on 38.8 but NOT reaching `vector-40`'s 44.5"*. Full sample: 44.0%, in band, not reaching 44.4 —
**both clauses correct.** At n = 200 the second clause was declared refuted and written up as "the run
refuted my scepticism, not the design". **Adjudicating a ±1-point prediction on a sample that cannot resolve
±1 point is the same error `docs/task-archive.md` Part 134 made with its interaction at n = 100**, recorded
in this very file, and repeated anyway. A prediction is only adjudicated by a run powered to adjudicate it.

**Not confirmed, through a flaw in the run's own design:** the arm set was chosen for the 40-slot question
and dropped `vector` (20 items), so the 20-slot parity of `docs/task-archive.md` **Part 136** — 41.0 against
41.3 at n = 200 — has no full-sample control. `lyntai-fused-full` reads 42.2% here with nothing to compare
it against.
<br>**That gap CLOSED the same day** — the subsection below ran the missing pair at full sample, and the
parity claim survived where this one's did not (−0.4 at n = 1,540 against −0.3 at n = 200). It also gives
this run a cross-run reproducibility check it could not give itself: `lyntai-fused-api` repeats **42.2 → 42.0
at an identical 3,503 `chars/q`**, so the table above reproduces.

### (n = 200, superseded above) At 40 slots of whole entries the walk reaches cosine (`memory-locomo`, 2026-09-02) <!-- result: id=locomo-3shot-full-40slot-n200 arm="`lyntai-fused-3shot-full` (+ the walk, 39.7 items/q) against `vector-40`" metric=token-f1 n="200" value="+0.9" ships=no status=RETRACTED -->

**Instrument.** `node devtools/dev.mjs memory-locomo --n 200 --no-judge`, seed 12345, reader `gemma3:4b`,
embedder `nomic-embed-text`, 3,487s. Raw output: the gitignored
`devtools/_locomo-40slot-v2.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
`lyntai-fused-3shot-full` is the three-shot walk asking for `MemoryDetail.Full` (**D104**) — the only arm
size-matched to `vector-40` on BOTH axes.

| arm | token-F1 | unknown | items/q | chars/q |
|---|---|---|---|---|
| `lyntai` (shipped defaults, 1 shot) | 19.9% | 53 | 20.0 | 2,283 |
| `lyntai-fused` (+ fused ranking) | 28.9% | 30 | 20.0 | 2,289 |
| `lyntai-fused-api` (+ full detail) | 41.1% | 12 | 20.0 | 3,583 |
| **`lyntai-fused-3shot-full`** (+ the walk) | **44.8%** | 6 | 39.7 | 7,084 |
| `vector` | 41.5% | 12 | 20.0 | 3,541 |
| `vector-40` | 43.9% | 5 | 40.0 | 6,924 |

**1. The engine ends AHEAD, and the whole ladder is worth stating in one line.** Shipped defaults at one shot
score 19.9%; ranking, detail and the walk take it to **44.8%** against plain cosine's 43.9% at 40 slots, for
2.3% more context. `unknown` falls from 53 refusals to 6, against cosine's 5.

**2. Read +0.9 as LEVEL-or-slightly-ahead, not as a win.** This run measures its own noise floor: the
harness-rehydrated and API arms send byte-identical prompts and score **40.7 against 41.1** — a 0.4-point
spread from reader nondeterminism alone, replicating the 0.4 seen in the previous run. **+0.9 is about twice
that floor**, which is suggestive and not decisive. The claim that survives is that the engine is no longer
behind, which is a change from every previous reading here.

**3. What the fix was worth, isolated.** The same arm scored 42.0% before expansion honoured the detail
request, when it held 20 whole items and ~20 truncated ones (6,053 chars). Making the discovered items whole
too is **+2.8** and +1,031 chars.

**4. The pre-registration was HALF right, and the wrong half is the interesting one.** It called 40–45%
(44.8 ✓) but said explicitly *"NOT reaching `vector-40`'s 44.5"* (✗), reasoning that the walk's items 21–40
are graph NEIGHBOURS while cosine's are the next-most-similar turns, so the two populations should not be
worth the same. **At this reader tier they are** — the neighbours a walk discovers are as useful to an
answer as reading further down the cosine list, which is the strongest result this benchmark has produced
for the design and the one it was built to test.

**Not settled.** n = 200, one reader tier (`gemma3:4b`), one embedder, one workload. +0.9 needs the full
1,540 to become a claim rather than a direction, and the category splits are not resolved at this sample.
Nothing here licenses a default moving: `MemoryDetail.Full` is opt-in precisely because the cheap first load
is what a different consumer wants (**D100**).

### CONFIRMED at full sample — at 20 slots the walk is LEVEL with cosine: −0.3 at n = 200 reads −0.4 at n = 1,540 (`memory-locomo`, 2026-09-02) <!-- result: id=locomo-fused-api-20slot-n1540 arm="`lyntai-fused-api` (fused ranking + `MemoryDetail.Full` through the shipped API, 20 slots) against `vector`" metric=token-f1 n="1,540" value="−0.4" ships=no status=CURRENT supersedes="locomo-fused-full-multihop-n200" -->

**This is the control the subsection above says it lacked.** The 40-slot confirmation dropped `vector`
(20 items), leaving the 20-slot parity of `docs/task-archive.md` **Part 136** resting on n = 200 — the
sample size that had just flipped the sign of the claim beside it.

**Instrument.** `node devtools/dev.mjs memory-locomo --n 1540 --no-judge --arms vector,lyntai-fused-api`,
all 1,540 questions in categories 1–4, seed 12345, reader `gemma3:4b` local, embedder `nomic-embed-text`,
3,544.9s. Raw output: the gitignored
`devtools/_locomo-20slot-full.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Two arms, one ingestion. Control: all ten conversations' per-question store copies verified turn-for-turn
(419 of 419, 369 of 369, …), and the semantic seed returned its full 20 with ids parsing as node keys.

| arm | token-F1 | exact | unknown | items/q | chars/q |
|---|---|---|---|---|---|
| `lyntai-fused-api` | 42.0% | 19.2% | 101 | 20.0 | 3,503 |
| `vector` | 42.4% | 19.4% | 107 | 20.0 | 3,460 |

**1. The parity claim SURVIVES, unlike the 40-slot claim measured beside it.** −0.3 at n = 200 reads −0.4 at
n = 1,540: same sign, same magnitude, at identical slots and 1.2% more context. Both arms gained about a
point going to full sample (41.0 → 42.0 and 41.3 → 42.4), which is the sample movement this instrument has
now shown three times.

**2. The reproducibility control is what licenses reading the difference at all.** `lyntai-fused-api` scored
**42.2%** in the 40-slot pass and **42.0%** here, at `chars/q` **3,503 in both** — a byte-identical arm
drifting 0.2 points across two independent runs. So −0.4 is about twice the floor, which adjudicates *"no
difference this instrument can resolve"* and **not** *"cosine is ahead"*. The identical char column is the
stronger half of that control: retrieval, ranking and slot count reproduced exactly, so only the reader
moved.

**3. What this run could NOT measure, and it is a flaw in my own run design.** The floor in point 2 is
CROSS-run. `docs/task-archive.md` **Part 138** asked for one measured IN the same run, which needed a second
byte-identical arm (`lyntai-fused-full`) at roughly 30 more minutes. The cross-run figure is stricter on one
axis — it carries ingestion and store-rebuild variation, not only reader sampling — but it is not the
control that was specified, and substituting a stricter-looking measurement for the one asked for is how a
run talks itself out of a gap. Reading it as a floor is a judgement, not a measurement.

**4. The category splits moved, and multi-hop is the one worth keeping.** It read 38.0 against 37.8 at
n = 200 — a tie — and reads **35.8 against 39.8** here, so the engine is 4.0 points behind cosine on the
category `docs/task-archive.md` **Part 235** already carries as the one no judge fixes. single-hop
replicates as a win (**52.1 against 50.5**, +1.6 against +1.9), while temporal (−2.3) and open-domain
(−1.9) stay behind and both narrow by more than half. **None of the n = 200 category cells was resolvable**
— a category holds a few dozen questions at that sample — so what changed is the resolution, not the engine.

**5. The pre-registration held on all three clauses**, registered in `TASKS.md` before the run started:
`lyntai-fused-api` reproduces 42.2% ± 0.3 (42.0 ✓), `vector` lands in 41.0–43.0% (42.4 ✓), and the
difference falls within ±1.0 point with the **sign not predicted** (−0.4 ✓). Declining to predict the sign
is the part that made it a usable prediction: at a 0.2-point floor, a sign call on a 0.3-point gap would
have been adjudicating what the instrument cannot see, which is the error `docs/task-archive.md` **Part
137** made and recorded.

**Not settled.** One reader tier, one embedder, one workload — and the in-run floor above. This says the
engine's ranking is not measurably worse than cosine at 20 slots once entries are whole; it does not say the
walk is better, and no default moves on it (`MemoryDetail.Full` stays opt-in, **D100**/**D104**).

### The retrieval ladder at FULL sample: the best mechanical arm clears cosine, and multi-hop's "16-point" gap is 3.2 (`memory-locomo --retrieval`, 2026-09-02) <!-- result: id=locomo-sem-rel-only-full-n1540 arm="`+sem+rel-only` (best mechanical: semantic seeds, relevance-only ranking) against `vector` (plain cosine)" metric=evidence-hit@k n="1,540" value="82.6%" ships=no status=CURRENT supersedes="locomo-multihop-residual-oracle" -->

**Every retrieval figure above this line is n = 200.** This is the first full-sample run of the ladder, and
it exists because `docs/task-archive.md` Part 235's multi-hop item was reasoning from a 37-question
category cell.

**Instrument.** `node devtools/dev.mjs memory-locomo --retrieval --n 1540 --no-judge --arms
vector,+sem+rel-only,+forget0+oracle`, all 1,540 questions, seed 12345, embedder `nomic-embed-text`, no
reader and no judge model, 9,611.4s. Raw output: the gitignored
`devtools/_locomo-multihop-full.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
It required a harness fix to run at all — `docs/FIXES.md`, 2026-09-02.

| arm | multi-hop | temporal | open-domain | single-hop | **overall** |
|---|---|---|---|---|---|
| `+forget0+oracle` (PERFECT judge) | 65.6% (185/282) | 78.8% (253/321) | 47.8% (44/92) | 79.0% (664/841) | **74.6%** |
| `+sem+rel-only` (best mechanical) | 79.8% (225/282) | 83.5% (268/321) | 57.6% (53/92) | 85.9% (722/841) | **82.6%** |
| `vector` (plain cosine) | **83.0%** (234/282) | 82.2% (264/321) | **58.7%** (54/92) | 82.4% (693/841) | **81.1%** |

**These are not estimates.** The model-free arms are deterministic by construction (one ingestion per
conversation, cloned per question, embeddings cached — the reproducibility subsection above establishes it),
and this is the COMPLETE question set rather than a draw from it. So no noise-floor argument applies: a
3.2-point multi-hop difference is exactly 9 questions of 282, on all of LoCoMo.

**1. `+sem+rel-only` clears plain cosine at full sample — 82.6% against 81.1%.** The n = 200 reading was
83.0 against 80.5; the win survives and is smaller (+1.5 rather than +2.5). This is the first POWERED
confirmation that this engine's best mechanical configuration beats the formula it was 26 points behind in
August. It wins single-hop (+3.5) and temporal (+1.3), and loses multi-hop (−3.2) and open-domain (−1.1).

**2. Multi-hop's gap is 3.2 points, not the 16.2 the backlog was carrying.** `docs/task-archive.md`
Part 235 recorded *"64.9% even with a PERFECT judge, against cosine's 81.1%"* and inferred that multi-hop
evidence sits outside `VerificationDepth`. That figure is a PRE-FUSION arm; per-source fusion (**D103**) had
already superseded it the same day. At full sample the best mechanical arm reads 79.8 against 83.0 — a real
but ordinary category deficit, and **no depth question follows from it.**

**3. The PERFECT-JUDGE arm is the worst of the three, and that is the sharpest result here.** <!-- result: id=locomo-forget0-oracle-n1540 arm="`+forget0+oracle` (PERFECT judge, no semantic seeds)" metric=evidence-hit@k n="1,540" value="74.6%" ships=no status=RETRACTED -->
`+forget0+oracle` scores **74.6%** — 8.0 below the mechanical arm and 6.5 below cosine — with a judge that
endorses exactly the evidence the dataset names. A pure formula beating formula-plus-oracle was the reading
`docs/task-archive.md` Part 235 opened with at n = 200; it now holds on the whole benchmark. **The deficit
was never the model tier**, which is what `model-decoupling.md` asks a design to demonstrate rather than
assume.

**What point 3 does NOT establish, stated because the arms invite the stronger claim.** `+forget0+oracle`
carries no semantic seeds and `+sem+rel-only` carries no judge, so **no measured arm pairs the judge with
the seeding that makes the mechanical arm good.** "A judge adds nothing" is unmeasured; what is measured is
that the best JUDGED arm loses to the best MECHANICAL one. The oracle is firing — 65.6% multi-hop against
`+forget0`'s 51.4% at n = 200 — so this is not a silently inert verifier.

**CORRECTED 2026-09-03, and point 3 does not survive the pairing it was missing.** `+sem+rel-only+oracle`
— seeding held fixed, the ceiling added — reads **92.5% against the same arm's 83.0%** at n = 200, a
**+9.5** gain that improves every category (temporal 81.0 → 95.2, single-hop 87.2 → 95.4, open-domain
58.3 → 75.0, multi-hop 81.1 → 86.5). So *"a pure formula beats formula-plus-oracle"* was an artifact of
comparing across a SEEDING difference rather than across the judge, exactly as the paragraph above warned it
might be. **With the pool right, promotion is worth 9.5 points and takes this engine to 92.5% against plain
cosine's 80.5%** — the highest figure this benchmark has produced here. What survives of point 3 is
narrower and still true: a judge cannot rescue a badly-seeded pool, which is what `+forget0+oracle`'s 74.6%
measures. The subsection below carries the run.

**4. The pre-registration held on all three clauses**, registered in `TASKS.md` before the run: `vector`
overall 78–83% (81.1 ✓), `+sem+rel-only` 78–85% with no prediction on whether it stays ahead (82.6 ✓, ahead),
and **multi-hop within ±5 points, sign not predicted** (−3.2 ✓, where >10 would have restored the struck
premise).

**Not settled.** One embedder, one workload — and LoCoMo rewards a perfect archive and penalises forgetting
by construction, which is why `RetrievabilityWeight = 0` is in two of these three arms and why no default
moves on this table. `open-domain` is 92 questions, the thinnest cell here by a factor of three. `items/q` is
20.0 on every arm, so nothing is filtered before ranking.

### The two workloads disagree by 7×: LoCoMo's winning config costs 37 points where the design makes its claim (`memory-longmemeval --haystack --arms`, 2026-09-02) <!-- result: id=lme-sem-rel-only-haystack-n70 arm="`+sem+rel-only` (the arm that wins LoCoMo) on knowledge-update, against the shipped default `lyntai`" metric=prefers-current n="70 knowledge-update questions among ~490 turns of distractors, 69 decidable" value="−37.1" ships=no status=CURRENT -->

**The question this answers had an empty cell.** Every LongMemEval figure published before this was the
SHIPPED DEFAULT, because that bench had no arm ladder — its arms were hardcoded `["lyntai", "vector"]`. So
the configuration that wins LoCoMo had never been run on the workload this design actually claims, and the
two could not be compared. `FieldArms` now defines an arm once for both benches, so a name means one
configuration on each.

**Instrument.** `node devtools/dev.mjs memory-longmemeval --haystack --arms
lyntai,+sem,+forget0,+sem+rel-only,vector`, all 70 knowledge-update questions among ~490 turns of
distractors (34,242 turns ingested per arm), seed 20260829, embedder `nomic-embed-text`, model-free,
7,245.6s. Raw output: the gitignored
`devtools/_lme-ladder-haystack.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

| arm | prefers current | current@k | stale@k | decidable |
|---|---|---|---|---|
| **`lyntai`** (shipped default) | **86.4%** (57/66) | 87.1% | **62.9%** | 66 |
| `+sem` | 72.5% (50/69) | 90.0% | 90.0% | 69 |
| `+forget0` | 49.3% (33/67) | 87.1% | 87.1% | 67 |
| `+sem+rel-only` (wins LoCoMo) | 49.3% (34/69) | **90.0%** | 92.9% | 69 |
| `vector` (plain cosine) | 46.4% (32/69) | 81.4% | 88.6% | 69 |

**Both in-run controls reproduce their published values exactly** — `lyntai` 86.4% (57/66) and `vector`
46.4% (32/69) — so the arm machinery did not move the bench underneath the new rows.

**1. The configuration that wins LoCoMo lands on plain cosine here.** `+sem+rel-only` scores 82.6% against
cosine's 81.1% on LoCoMo and **49.3% against cosine's 46.4%** on this class — it discards essentially the
whole 40-point advantage the shipped default holds. Stated as the trade it is: adopting the LoCoMo winner
globally buys **+5.5 points of search** and costs **−37.1 points of supersession**, a ratio of about 7 to 1
against.

**2. The mechanism is in the columns, and it is not a recall failure.** `+forget0`'s `current@k` is
**87.1%, identical to `lyntai`'s 87.1%**, while its `stale@k` rises from 62.9% to 87.1%. Removing
forgetting's vote does not change what the engine FINDS — it destroys what the engine BURIES.
`+sem+rel-only` shows the same shape one step further: it finds MORE than the default (90.0%) and suppresses
nothing (92.9%). **Retrieval and suppression are separate capabilities, and only one of them is what LoCoMo
scores.**

**3. Semantic seeding costs suppression too, but not catastrophically.** `+sem` alone runs 86.4 → 72.5 on
preference while raising `current@k` to 90.0 and `stale@k` to 90.0. So registering the vector channel is a
real trade rather than a free win — it surfaces the superseded fact as readily as the current one, which is
what a similarity channel does when the two are near-identical text by construction.

**4. Both pre-registered predictions held**, registered in the design record before the run: `+forget0`
worse than `lyntai` here (predicted; −37.1), and `+sem` raising both `current@k` and `stale@k` with
preference flat or down (predicted; +2.9 / +27.1 / −13.9). The decision rule was also fixed in advance —
within ~1 point makes LoCoMo's gain adoptable, ≥5 points makes the default workload-dependent. It lost 37.

**What follows for defaults.** **The shipped defaults are right, and the LoCoMo ladder was optimising
against that benchmark's own construction** — it asks about months of history uniformly, so it rewards a
perfect archive and penalises decay by design, and its best arm is the one with the engine's distinctive
mechanism switched off. No default moves on this table. A deployment whose workload really is uniform-history
search can set `RetrievabilityWeight = 0` and register semantic seeds; that is a documented profile, not a
new global default.

**5. The same arms on both workloads, which is what the shared registry was built to make sayable.** LoCoMo
column is `evidence-hit@20` at n = 200 (today's control run, reproducing the published table exactly);
knowledge-update is `prefers current` on all 70 haystack questions. Read the two DELTAS against `lyntai`,
not the absolutes — the benchmarks score different things on different scales.

| arm | LoCoMo | Δ vs default | knowledge-update | Δ vs default | verdict |
|---|---|---|---|---|---|
| `lyntai` (shipped) | 54.5% | — | **86.4%** | — | the default |
| `+sem` | 76.5% | **+22.0** | 72.5% | **−13.9** | the only arm that trades FAVOURABLY |
| `+forget0` | 60.0% | +5.5 | 49.3% | **−37.1** | 7:1 against |
| `+sem+rel-only` | **83.0%** | **+28.5** | 49.3% | **−37.1** | buys search, discards the claim |
| `vector` | 80.5% | +26.0 | 46.4% | −40.0 | no mechanism, both ways |

**`+sem` is the finding this table adds**, and it was invisible from either bench alone: registering the
semantic channel buys **+22.0 points of search for −13.9 of supersession**, where the ranking changes buy
+5.5 to +28.5 for a flat −37.1. **The suppression cost is not proportional to the search gain** — it is
carried almost entirely by `RetrievabilityWeight`, and semantic seeding is nearly free of it. That makes
"should `AddMemorySemanticSeeds()` ship ON" a genuine decision with a priced trade, where "should
`RetrievabilityWeight` move" is now simply answered.

**Not settled.** 70 questions — the entire class, model-free and deterministic, so these are exact rather
than estimated, but 70 items is still a thin basis and the gaps that carry the argument (37.1 and 13.9
points, ~25 and ~10 questions) are the ones far outside any plausible noise. One embedder, one benchmark
family. And this prices CONFIGURATIONS, not mechanisms: it shows the suppression advantage travels with
`RetrievabilityWeight`, not that decay is the only thing that could produce it. **The cross-workload table
mixes two metrics and two sample sizes by construction**, so it ranks trades and cannot be read as one
score.

### There is a FRONTIER, not a free configuration — and narrowing a seed source makes its worst candidate louder (both benches, 2026-09-03) <!-- result: id=locomo-frontier-sem-forget2-n200 arm="`+sem+forget2`" metric=evidence-hit@k n="200" value="69.5%" ships=no status=CURRENT -->

**The question this asked.** The 2026-09-02 ladder showed seeding and burial are separable mechanisms —
seeding decides what is in the POOL, `RetrievabilityWeight` decides what gets BURIED — which implies a
combination nobody had run: a wide pool with a STRONGER burying vote. Two arms were added to separate them,
and both were pre-registered before either ran.

**Instrument.** `memory-locomo --retrieval --n 200 --no-judge` (1,476.6s) and `memory-longmemeval --haystack`
(6,918.2s), both `--arms lyntai,+sem,+sem5,+sem+forget2,vector`, embedder `nomic-embed-text`, model-free.
Raw output: the gitignored `devtools/_locomo-both-workloads.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->
and `devtools/_lme-both-workloads.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Three controls reproduce (`lyntai` 54.5/86.4, `+sem` 76.5/72.5, `vector` 80.5/46.4).

| arm | LoCoMo | Δ | knowledge-update | Δ | search per point of suppression |
|---|---|---|---|---|---|
| `lyntai` (shipped) | 54.5% | — | **86.4%** | — | — |
| **`+sem+forget2`** | 69.5% | +15.0 | 78.8% | −7.6 | **2.0** |
| `+sem` | 76.5% | +22.0 | 72.5% | −13.9 | 1.6 |
| `+sem5` | 76.5% | +22.0 | 59.1% | −27.3 | 0.8 |
| `+sem+rel-only` | **83.0%** | +28.5 | 49.3% | −37.1 | 0.8 |
| `+forget0` | 60.0% | +5.5 | 49.3% | −37.1 | 0.1 |
| `vector` | 80.5% | +26.0 | 46.4% | −40.0 | — |

**1. Separability HELD, and it was the clause that could have killed the idea.** The pre-registration said
that if `+sem+forget2` were no better than `+sem` on knowledge-update, the pool itself carries the
suppression cost and the trade is irreducible. It is better — **78.8% against 72.5%** — so doubling the
burying vote does buy suppression back independently of what seeding admits.

**2. But there is no free configuration.** `+sem+forget2` gives up 7.0 points of search to recover 6.3 of
suppression against `+sem`: roughly 1:1, a smooth frontier rather than a cliff. It buys search at the best
exchange rate measured (2.0 points per point of suppression against `+sem`'s 1.6), and it is still a trade.
**The pre-registered decision rule — ≥80% knowledge-update AND ≥70% LoCoMo — was missed on both clauses,
by 1.2 and 0.5 points. No default moves.**

**3. `+sem5` is the result worth keeping, and it is backwards from the prediction.** A narrower semantic
channel retrieves IDENTICALLY to the wide one on LoCoMo (76.5% both) and suppresses far worse (59.1% against
72.5%) — while returning the superseded fact LESS often (`stale@k` 77.1 against 90.0). It holds the stale
fact less and ranks it first more.
<br>**The mechanism is per-source fusion doing exactly what it says.** RRF ranks by position WITHIN each
source's own list (**D82**, **D103**), so cutting the semantic channel from 20 to 5 does not dilute a bad
candidate — it concentrates the source's rank weight onto five, and a near-identical superseded fact is one
of them. **Narrowing a seed source makes its worst candidate LOUDER, not quieter.** Anyone reaching for a
smaller `SemanticSeedOptions.K` to reduce noise should read that sentence first.

**4. What the search column says about `K`.** `+sem5` matches `+sem` overall at a quarter of the width, so
the semantic channel's entire search value on this workload sits in its top ~5 hits. That is a statement
about this corpus and this embedder, not a recommendation to move the default — and point 3 is the reason
moving it would be a mistake anyway.

**Not settled.** Two points on a curve is not a curve: this prices a mechanism, it does not locate an
optimum, and proposing a default from two points is the error `docs/task-archive.md` Part 137 records. A
`RetrievabilityWeight` ladder is what would find the knee, if the frontier is judged worth walking.
<br>**SETTLED 2026-09-11 by the ladder below — there is no knee. The search axis is a straight line.**

### The frontier is STRAIGHT, so there is no knee to find (`memory-locomo --retrieval`, 2026-09-11) <!-- result: id=locomo-frontier-ladder-n200 arm="`+sem+forget0.5` — the top rung of a six-point `RetrievabilityWeight` ladder" metric=evidence-hit@k n="200" value="80.5%" ships=no status=CURRENT -->

**The question, and why it was arithmetic before it was a run.** The section above left two rungs on the
weight and a pre-registered rule its best arm missed on both clauses (**≥80%** knowledge-update AND
**≥70%** LoCoMo, missed by 1.2 and 0.5). Interpolating those two rungs linearly puts the target out of
reach — LoCoMo ≥ 70 needs weight **≤ 1.93**, knowledge-update ≥ 80 needs weight **≥ 2.19**, and the windows
do not overlap. That argument is only as good as its straight-line assumption, and testing that assumption
is the whole of what this run does. It proposes no default.

**Instrument.** `memory-locomo --retrieval --n 200 --no-judge` over seven arms, 1,798.7s, seed 12345.
Raw output, gitignored: `devtools/_frontier-ladder.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Prediction, decision rule and instrument check were fixed in writing before the run.
**Served by `llama-server` on a dedicated port rather than by Ollama** — same `nomic-embed-text` weights,
different server.

| weight | arm | predicted | **measured** | deviation |
|---:|---|---:|---:|---:|
| 0.5 | `+sem+forget0.5` | ~80.0% | **80.5%** | +0.5 |
| 1.0 | `+sem` | — | **76.5%** | anchor |
| 1.5 | `+sem+forget1.5` | ~73.0% | **73.0%** | 0.0 |
| 2.0 | `+sem+forget2` | — | **69.5%** | anchor |
| 2.5 | `+sem+forget2.5` | ~66.0% | **65.5%** | −0.5 |
| 3.0 | `+sem+forget3` | ~62.5% | **63.5%** | +1.0 |

**1. All three anchors reproduce EXACTLY** — `lyntai` 54.5%, `+sem` 76.5%, `+sem+forget2` 69.5%, cell for
cell against 2026-09-03. That is what licenses reading the rest, and it carries a second finding for free:
those figures were Ollama-served and these are `llama-server`-served from the same weights, so **the server
swap is neutral on this workload**. One less thing to control for, measured rather than assumed.

**2. The line is straight across the whole range.** Every rung lands within **±1.0 point** of the
linear prediction — inside the near-tie band of two questions in 200 — and the measured slope over
0.5 → 3.0 is **−6.8 points per unit of weight** against a predicted −7.0. No plateau, no cliff, no knee.

**3. So the interpolation argument holds, and the frontier is NOT worth walking further.** The
pre-registered decision rule said that rungs inside the near-tie band settle it without spending the
suppression half; they are, and it was not spent. `RetrievabilityWeight` stays at 1 for the reason
**Part 140** already gave, and this adds the reason the ladder cannot rescue a move: the exchange rate is
constant, so every weight buys suppression at the same price and none is a bargain.

**What this does NOT say.** **The suppression axis was not re-measured** — the four new rungs have no
knowledge-update figure and none is implied here, because a linear search axis is not evidence of a linear
suppression axis. It also says nothing about a weight above 3 or below 0.5. And `+sem+forget0.5` reading
80.5% is **not** a recommendation: it ties plain cosine on search by giving up more of the burial this
engine exists to do, which is further down the same trade rather than off it.

### The judge on the arm that is actually GOOD is worth +9.5, and it corrects this morning's headline (`memory-locomo --retrieval`, 2026-09-03) <!-- result: id=locomo-oracle-on-sem-rel-only-n200 arm="`+sem+rel-only+oracle` (a PERFECT judge — a ceiling, not a score)" metric=evidence-hit@k n="200" value="92.5%" ships=no status=CURRENT supersedes="locomo-forget0-oracle-n1540" -->

**Every verdict arm on record is built on `+forget0`**, which registers no semantic channel. So the
full-sample reading *"a pure formula beats formula-plus-oracle"* — 74.6% against `+sem+rel-only`'s 82.6% —
compared two arms differing in SEEDING as well as in the judge. This holds seeding fixed and adds the
ceiling.

**Instrument.** `memory-locomo --retrieval --n 200 --no-judge --arms
+sem+rel-only,+sem+rel-only+oracle,vector`, 788.0s. Raw output: the gitignored
`devtools/_locomo-judge-on-good-arm.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Both controls reproduce (`+sem+rel-only` 83.0%, `vector` 80.5%).

| arm | multi-hop | temporal | open-domain | single-hop | **overall** |
|---|---|---|---|---|---|
| `+sem+rel-only` | 81.1% | 81.0% | 58.3% | 87.2% | **83.0%** |
| **`+sem+rel-only+oracle`** | 86.5% | **95.2%** | **75.0%** | **95.4%** | **92.5%** |
| `vector` | 81.1% | 81.0% | 58.3% | 82.6% | 80.5% |

**1. Promotion is worth +9.5 on a well-seeded pool, and it improves every category.** 92.5% is the highest
figure this benchmark has produced from this engine, against plain cosine's 80.5%. The pre-registration
called 88–95% with a smaller absolute gain than the +17.5 the oracle bought on `+forget0`; both clauses hold
(92.5%, +9.5).

**2. It corrects the morning's headline rather than adding to it.** *"A pure formula beats
formula-plus-oracle, so the deficit was never the model tier"* is an artifact of the seeding difference.
The claim that survives is narrower: **a judge cannot rescue a badly-seeded pool** — which is what
`+forget0+oracle`'s 74.6% measures — and it says nothing about a judge on a good one.

**3. So the residual misses ARE reachable-but-outranked**, which is the branch that makes a real model run
worth spending. `docs/task-archive.md` Part 235's judge item was re-aimed onto this base before this ran;
the run confirms the base was the right one.

**A CEILING, not a score.** The oracle endorses exactly the evidence LoCoMo names, so +9.5 is what promotion
could recover at best and no claim about any model's accuracy. n = 200, one embedder; the category cells are
small and only the overall figure carries weight.

### A real 4B judge SPENDS the +9.5 the oracle offers, and the audit says why (`memory-locomo --retrieval`, 2026-09-03) <!-- result: id=locomo-judge-4b-depth80-n200 arm="`+sem+rel-only+judge` (gemma3:4b, shipped `VerificationDepth` 4× = 80)" metric=evidence-hit@k n="200" value="72.5%" ships=no status=CURRENT -->

The subsection above prices what a PERFECT judge could recover on this base. This is the same arm with the
shipped `LlmMemoryVerificationPolicy` behind it — the seam a deployment actually switches on, exercising the
shipped prompt, parsing, depth handling and fail-open behaviour — reading this machine's local `gemma3:4b`.

**Instrument.** `memory-locomo --retrieval --n 200 --arms
+sem+rel-only,+sem+rel-only+oracle,+sem+rel-only+judge,vector`, 2052.1s; then the arm and its base alone,
1340.7s. Raw output, both gitignored:
`devtools/_locomo-real-judge.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->
and `devtools/_locomo-real-judge-repeat.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->.
All three model-free controls reproduce (`+sem+rel-only` 83.0%, `+sem+rel-only+oracle` 92.5%, `vector`
80.5%), and the repeat reproduces the judge arm **cell for cell**.

| arm | multi-hop | temporal | open-domain | single-hop | **overall** |
|---|---|---|---|---|---|
| `+sem+rel-only` | 81.1% | 81.0% | 58.3% | 87.2% | **83.0%** |
| `+sem+rel-only+oracle` | 86.5% | 95.2% | 75.0% | 95.4% | **92.5%** |
| **`+sem+rel-only+judge`** (gemma3:4b) | 70.3% | 78.6% | 41.7% | 74.3% | **72.5%** |

**1. It costs 10.5 points where a perfect judge gains 9.5**, and it loses in every category. This is the
third branch of the prediction registered in the ladder before the run — *below the unjudged base*.

> **CORRECTED the same day by the depth ladder below, and the correction is the more useful half.** This
> subsection read the result as a **capability floor**. It is a **depth×capability interaction**: at half
> this run's `VerificationDepth` the SAME model on the SAME arm is level with no judge, so the 10.5-point
> loss belongs to the shipped depth default at least as much as to the model tier. The caveat this run put
> into `LlmVerificationOptions.ClientName` was corrected with it. The mechanism below — a candidate list so
> long the model stops discriminating — is what the "capability floor" reading was missing, and this
> subsection's own limits section is what named it.

**2. The arm cannot be silently inert, which is the first thing to rule out.** Fail-open returns
`MemoryVerification.NoOpinion`, which leaves the ranking untouched — so a judge that failed, refused, timed
out or emitted unparseable JSON on every call would score the base's **83.0% exactly**. 72.5% is only
reachable by a judge that answered and was wrong. The audit confirms it directly: **0 of 200 calls
declined**.

**3. What the model actually did**, from the `JudgeAudit` decorator (the shipped policy, delegated untouched,
scored against LoCoMo's own evidence labels — the same labels the oracle and the arm's own column use):

| | per call |
|---|---|
| candidates shown | 80.0 — `VerificationDepth`, 4 × the recall's limit of 20 |
| endorsed | **29.1** |
| evidence among those shown | 1.19 |
| **precision** | **2.6%** (151 of 5,829 endorsed were evidence) |
| **recall** | **63.4%** (151 of 238 evidence shown) |
| declined — fail-open | 0 of 200 |
| judged "none relevant" | 0 of 200 |

**4. The endorsement set is BIGGER than the page, so promotion cannot refine — it REPLACES.** 29.1
endorsements against a limit of 20: promotion moves every endorsed candidate ahead of the cut keeping the
policy's order within each group, so the returned page becomes *the 20 best-ranked of the 29 endorsed* and
**everything unendorsed is pushed off entirely, however well the ranking placed it**. That is the whole
mechanism: at 63.4% recall the judge fails to endorse a third of the evidence it was shown, and each of
those is not merely un-promoted but demoted below 29 other candidates.

**5. The judge beats chance, and it does not matter.** Evidence is 1.19 of 80 shown, so it is **1.49%** of
the pool; the model endorses at **2.59%** precision — a **1.74×** lift. Read the other way it recalls 63.4%
while endorsing 36.4% of the pool, which is the same 1.74×. So the endorsements carry real signal. They
still destroy the result, because the ranking they overwrite is far better than 1.74× over chance: it puts
evidence on the page for 83.0% of questions. **A verifier does not need to be good to be consulted; it needs
to be better than what it displaces.**

**What this does NOT say.**
- **One model, one workload, one embedder, n = 200.** It is not a claim that 4B judges are useless, nor that
  the seam is wrong — `+oracle`'s +9.5 on this same base says promotion has genuine work to do here.
- **The judge is shown 120-character DERIVED headlines** (`GraphMemoryOptions.HeadlineChars`, what the
  engine writes when a caller authors none), not full turns. A deployment authoring real headlines is giving
  its judge a different and probably easier task, and that is untested.
- **Temperature is 0** (`SweepDoubles.OpenAiCompatibleChat`), so the exact reproduction bounds HARNESS
  variance and says nothing about sampling variance. A judge run at a real temperature is unmeasured.
- **A `VerificationDepth` of 80 is a choice this run inherited**, not one it tested. A shallower depth
  shows the model fewer chances to be wrong and is the obvious next arm if anyone wants to rescue this.

### It was the DEPTH, not the model: the same judge is level at 2× and catastrophic at 4× (`memory-locomo --retrieval`, 2026-09-03) <!-- result: id=locomo-judge-depth40-n200 arm="`+sem+rel-only+judge@40` (depth 40 = 2×)" metric=evidence-hit@k n="200" value="84.0%" ships=no status=CURRENT supersedes="locomo-judge-4b-depth80-n200" -->

The subsection above called the 10.5-point loss a **capability floor**. Its own limits section named the
untested alternative — *"a `VerificationDepth` of 80 is a choice this run inherited, not one it tested"* —
and this ladder ran it. **The floor is a depth×capability interaction, and the capability half is the
smaller one.** The `DefaultVerificationDepthFactor = 4` those runs inherited was fitted against a PERFECT
judge (**D59**), where depth is free because an oracle never endorses junk.

**Instrument.** `memory-locomo --retrieval --n 200 --arms
+sem+rel-only,+sem+rel-only+judge@40,+sem+rel-only+judge@20`, 1633.7s. Raw output, gitignored:
`devtools/_locomo-judge-depth.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Same model, same prompt, same base arm; **`GraphMemoryOptions.VerificationDepth` is the only thing that
varies**, and the depth-80 row is the shipped default carried over from the run above.

| depth | shown/call | endorsed/call | precision | recall | **evidence-hit@20** |
|---|---|---|---|---|---|
| 20 — the recall's own limit | 20.0 | 3.4 | 16.2% | 55.3% | **83.0%** |
| 40 — 2× | 40.0 | 7.7 | 8.4% | 59.2% | **84.0%** |
| 80 — the shipped 4× | 80.0 | 29.1 | 2.6% | 63.4% | **72.5%** |

**1. The null control held EXACTLY, which is what licenses reading the rest.** At depth 20 the verifier sees
precisely the page being returned, so promotion can only reorder within it and evidence-hit@20 *cannot*
move. It reads 83.0% — the base, **cell for cell in all four categories** — while its audit shows the judge
really did answer and really did promote (3.4 endorsements per call, 0 declines). This is the arm that
structurally cannot move, which the judge ladder had never had.

**2. Selectivity COLLAPSES with list length, and that is the mechanism.** The model endorses ~17% of a
20-item list and ~19% of a 40-item one, then **36% of an 80-item one**. Its discriminative lift over chance
holds at ~3.2× for the two short lists (3.27× and 3.08×) and falls to **1.74×** at 80. So the long list does
not merely dilute a constant judgement — it makes the judgement itself worse, and then promotes the result
over a 20-slot page it now overflows.

**3. Read the +1.0 at depth 40 as LEVEL, not as a win.** It is two questions out of 200, which is inside the
near-tie band `docs/task-archive.md` Part 119 measured; the arms are deterministic (temperature 0), so the
uncertainty is over the QUESTION SAMPLE, not the instrument, and a different 200 could flip its sign. **The
robust result is the −10.5 → +1.0 swing across depth**, which is 21 questions and far outside that band.

**4. No default moved, and none should on this.** One model, one workload, one embedder. What the run
changes is the ADVICE, which now lives on both shipped options: the depth default is right for a strong
judge and can be actively harmful for a weak one, and *"endorses a large fraction of what it sees"* is the
failure signal to watch.

**What it does NOT say.** Depth 20 is not evidence that a judge is useless there — it is invisible to *this
metric* by construction, since reordering a returned page cannot change what is in it. A reader-facing
measurement could still see it, and none has been run. Nor does this locate a knee: three points, one of
them a structural constant, do not make a curve.

### The PARTITION was the harm, and the judge's confidence is anti-correlated with its usefulness (`memory-locomo --retrieval`, 2026-09-03) <!-- result: id=locomo-judge-fuse-depth80-n200 arm="`+sem+rel-only+judge+fuse` (same judge, same shipped depth, FUSED instead of partitioned)" metric=evidence-hit@k n="200" value="83.0%" ships=no status=CURRENT -->

The two subsections above priced a judge that hurts. This asks WHY the engine lets it, and the answer is
the combination rule rather than the model: **the verdict is the only signal this engine combines by a hard
partition** — endorsed ahead of unendorsed, then cut — while every other signal is fused by rank
competition (**D82**, **D103**). So an unendorsed candidate ranked 1st loses to an endorsed one ranked 80th,
and once the endorsed set reaches the limit the ranking is gone.

**Instrument.** `memory-locomo --retrieval --n 200`, four runs over the same base arm. Raw output,
gitignored: `devtools/_locomo-judge-fuse2.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->,
`devtools/_locomo-judge-top.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->
and `devtools/_locomo-judge-headroom.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
`+sem+rel-only` reproduces at 83.0% in every one.

| arm | overall | note |
|---|---|---|
| `+sem+rel-only` | **83.0%** | the base, in all four runs |
| `+sem+rel-only+judge` | 72.5% | partition, shipped depth |
| **`+sem+rel-only+judge+fuse`** | **83.0%** | same judge, same depth, FUSED |
| `+sem+rel-only+judge@40` | 84.0% | partition, depth 40 |
| `+sem+rel-only+judge@40+fuse` | 84.0% | fusion barely differs there — 64% same page |
| `+sem+rel-only+judge+fuse+top5` | 83.0% | **DEGENERATE — 99% same page, no measurement** |
| `+sem+rel-only+oracle` | 92.5% | the ceiling |

**1. Fusing removes the whole 10.5-point loss.** Same model, same prompt, same depth, same 29.1
endorsements — only the combination rule changes, and the catastrophe disappears. That is 21 questions, far
outside the near-tie band. **The partition is the mechanism of harm**, and depth mattered only because it
grew the endorsed set until the partition ate the page.

**2. It removes the harm and adds nothing.** Fused, the arm lands exactly on its base while the oracle shows
+9.5 available. Its categories move (temporal 81.0 → 85.7, open-domain 58.3 → 50.0) and net to zero.

**3. The model's own ordering carries a 23× lift — and the engine discards it.** Asked for its picks
best-first, cumulative precision runs **top1 34.5%, top2 20.7%, top5 12.6%, all 2.6%** against a pool
evidence density of 1.49%. `GraphMemoryEngine` reads `RelevantIds` through `ToHashSet()`, so the order is
dropped: the model is asked a ranked question and one bit per candidate is kept.

**4. And the lift is on the WRONG calls, which is the finding that closes the direction.** A verifier can
only change a call whose page held no evidence while something deeper did — 19 of 200 here, matching the
oracle's headroom exactly. On those the judge endorsed the deep evidence **10 times (53%)** and put it in
its own top five **0 times (0%)**. **Its confident picks are the ones the ranking already found; the useful
ones sit in a tail whose precision is 2.6%** — the same tail that made the partition destructive. So no
truncation, threshold or reweighting of THIS judge's order can win, and `+top5` failing was not a
measurement error but the prediction.

**The ceiling this puts on the seam for this model:** 10 of 19 rescuable calls, so **+5.0 points at most**,
and only for a rule that could pull those tail endorsements out without their neighbours. Everything above
that needs a better judge, which is a deployment choice (`model-decoupling.md`) rather than a library one.

**What it does NOT say.** One model, one workload, one embedder, n = 200; the rescuable cell is 19 calls, so
the 53% and the 0% are small counts and only their contrast is safe to lean on.

**CONFIRMED THROUGH THE SHIPPED ENGINE, 2026-09-04.** The figures above were taken through a bench-local
verifier that emits the fused page AS its verdict — sound for a metric reading the returned SET, and not the
engine's own path, which reorders and then applies its own cut. `GraphMemoryOptions.VerdictCombination`
(**D105**) made the real path measurable, and `+sem+rel-only+judge+enginefuse` reads **83.0% — cell for cell
identical to `+fuse` in all four categories** (81.1 / 85.7 / 50.0 / 86.2). All three controls reproduce:
`+sem+rel-only` 83.0%, `+sem+rel-only+judge` 72.5%, `vector` 80.5%. Raw output, gitignored:
`devtools/_locomo-enginefuse.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the claim above -->.

**The two arms are provably INDEPENDENT, which is what makes the agreement evidence.** They ran separate
model call sequences and the audits differ — 29.2 endorsements per call against 29.1, and 152 evidence
endorsements against 154 — so identical category cells are two implementations of the same rule agreeing,
not one code path measured twice. Both declined 0 of 200, so neither was silently inert. **An arm landing on
72.5% would have meant the shipped option never reached that path**, which is the branch the ladder
pre-registered.

**REPLICATED ON A SECOND EMBEDDER, and it QUALIFIES the claim above** (`embeddinggemma:300m`, 2026-09-04,
`--n 200`, 2286.1s; raw output gitignored:
`devtools/_locomo-embed2.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->).
Every figure here is one model and one workload, so the point of the run was the DIRECTION, and the
prediction was registered before it: levels would move, `+judge` must sit below the base and `+enginefuse`
must land on it.

| arm | `nomic-embed-text` | `embeddinggemma:300m` |
|---|---|---|
| `+sem+rel-only` — the base | 83.0% | **85.0%** |
| `+sem+rel-only+judge` — partition | 72.5% | 73.0% |
| `+sem+rel-only+judge+enginefuse` | **83.0%** | 82.5% |
| `vector` | 80.5% | 83.5% |

**1. The partition's harm replicates, and is slightly larger: −12.0 against −10.5.** That is the load-bearing
half of **D105** and it is not an artefact of one embedder.

**2. Fusion's recovery is INCOMPLETE here, which the first run could not have shown.** It takes back 9.5 of
the 12.0 points and lands **2.5 below** the base, where under `nomic-embed-text` it landed exactly on it.
About five questions at n = 200 — above this instrument's ~1-point near-tie floor, so not noise, and small
enough that it is a qualification rather than a reversal. **"Fuse removes the loss and adds nothing" was one
embedder's phrasing of "Fuse removes MOST of the loss"**, and `GraphMemoryOptions.VerdictCombination`'s
shipped XML doc was corrected with this run.

**3. The controls hold on both arms**: 0 of 200 declined, 30.4 and 30.9 endorsements per call — again
different sequences reaching the same reading — and identical evidence recall at 62.4%.

### A BUDGET in the judge's prompt: the number that helps is not the one the library was going to supply (`--arms …+judge+budget20,…+judge+budget5`, 2026-09-04) <!-- result: id=locomo-judge-budget5-n200 arm="`+sem+rel-only+judge+budget5`" metric=evidence-hit@k n="200" value="76.5%" ships=no status=CURRENT -->

The shipped judge prompt says **"Be selective"** and names no count, which is the tell
`.claude/knowledge/pitfalls.md` records for a model that stops discriminating. The extractor's budget worked
(§5, `--facts`), so the same question was put to this seam. **The budget is injected into the SYSTEM message
at the `ITextClient` boundary**, so the shipped policy still composes, sends, parses and fails open — a
bench-local judge would have measured a prompt invented for the bench.

**Instrument.** `memory-locomo --retrieval --n 200 --arms +sem+rel-only,+sem+rel-only+judge,+sem+rel-only+judge+budget20,+sem+rel-only+judge+budget5,vector`,
3128.6s. Raw output, gitignored:
`devtools/_locomo-judge-budget.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
**All three controls reproduce CELL FOR CELL** — `+sem+rel-only` 83.0%, `vector` 80.5%, and
`+sem+rel-only+judge` 72.5% at 29.1 endorsed/call, 2.6% precision, 0/200 declined, every figure identical to
the table above. That last one is the structural null control for the change itself: an unbudgeted arm
passes `null` and must see a byte-identical prompt.

| arm | overall | endorsed/call | precision | recall | rescued in own top 5 |
|---|---|---|---|---|---|
| `+sem+rel-only` | **83.0%** | — | — | — | — |
| `+sem+rel-only+judge` | 72.5% | 29.1 | 2.6% | 63.4% | 0 of 19 |
| `+sem+rel-only+judge+budget20` | 73.0% | **34.9** | 2.3% | 68.5% | 0 of 19 |
| `+sem+rel-only+judge+budget5` | **76.5%** | 27.4 | 2.8% | 65.5% | 0 of 19 |
| `vector` | 80.5% | — | — | — | — |

**1. The budget does not BIND, and "at most 20" made it endorse MORE.** Asked for at most 20 of 80 the model
returned **34.9**, up from 29.1 unbudgeted; asked for at most 5 it returned **27.4**, 5.5× its budget. The
extractor obeyed the same style of instruction at 8.3% over. **So "shape the input rather than buy a bigger
model" is not one rule but two cases**: a GENERATIVE task takes a count naturally, while a SELECTIVE task
over a list the model can see does not — every one of the 80 looks locally defensible, and a stated number
reads as an expectation rather than a cap. A cap that raises the output is the sharpest form of that.

**2. It moved the score anyway, and `budget5` is worth +4.0 points** — 72.5% → 76.5%, on single-hop
(74.3 → 80.7) and temporal (78.6 → 81.0), with multi-hop and open-domain unmoved. **The pre-registered
prediction was half wrong**: it said both budget arms would land near the unbudgeted judge because 27–28
endorsements still exceed the 20-slot page. The size claim held and the conclusion did not — so **endorsement
set > page is necessary but not sufficient** to explain the loss, and the COMPOSITION of the endorsed set
matters as well as its size. That refines the mechanism recorded above rather than replacing it.

**3. The decision-relevant finding is about the API, and it is negative.** The gap named in the handover was
that `MemoryVerificationRequest` cannot carry the caller's limit, so a policy cannot say "at most 20" for a
page of 20. **Measured, that exact number is worth +0.5 points — nothing.** The number that helps is 5, a
quarter of the page, which the recall limit would never supply. **So the proposed API shape would have
delivered the useless arm**, and anything shipped here would be an endorsement budget in the judge's own
options, defaulting to off — not a `Limit` on the request.

**4. And budgeting is the WEAKEST of the three levers already measured.** Depth 40 reads 84.0% and fusion
reads 83.0%, both removing the whole loss; the best budget recovers 4.0 of the 10.5 points and still lands
**6.5 below the unjudged base**. A judge under every budget tested remains net-negative at the shipped depth,
so nothing here argues for a default moving, and the fusion change already filed stays the right one.

**What it does NOT say.** One model, one workload, n = 200, two budget values — a pair of points is not a
curve, and 5 was not searched for. The structural fact under all of it is unchanged and is why the ceiling
is low: on the 19 rescuable calls the judge put the deep evidence in its own top five **0 times under every
budget**. Its confidence still tracks what the ranking already found, which no prompt bound addresses.

### A write-time baseline built to a GOOD STANDARD still needs decay (`--extract --sessions --writer cli`, 2026-09-04) <!-- result: id=longmemeval-cli-extract-forget0 arm="`extract+forget0` — strong-CLI extracted facts, decay OFF" metric=prefers-current n="70 knowledge-update questions, oracle variant (140 session calls, 921 facts)" value="52.9%" ships=no status=CURRENT -->

The earlier extraction verdict had a weak premise: a 4B model on an unbounded turn-by-turn prompt is not the
field's design done well, so *"write-time consolidation does not substitute for decay"* was partly a
statement about the extractor. **This rebuilds the baseline to a standard worth losing to** — a strong model
through the `claude` CLI, reading a whole SESSION at a time and citing the turn each fact came from, which
is the survey's own named-but-unadopted *"reflection grounding"*.

**Instrument.** `memory-longmemeval --extract --sessions --writer cli`, all 70 knowledge-update questions,
oracle variant, 140 session calls. Raw output, gitignored:
`devtools/_lme-cli-baseline.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

**The baseline is genuinely better, by its own numbers.** It compresses to **0.56×** — 921 facts from 1,640
turns, where the 4B turn-by-turn extractor INFLATED to 7.1× — and mis-cited **1 fact in 921**.

| arm | prefers current | current@k | stale@k | paired vs cosine |
|---|---|---|---|---|
| `lyntai` — raw turns + decay | **96.9%** | 90.0% | 54.3% | +32 −1, **p<0.0001** |
| `extract` — strong facts + decay | 91.4% | **95.7%** | 87.1% | +33 −3, **p<0.0001** |
| `extract+forget0` — same facts, decay OFF | 52.9% | 95.7% | 91.4% | +22 −18, **p = 0.636** |
| `vector` | 47.1% | 84.3% | 95.7% | — |

**1. The two middle rows share an IDENTICAL store**, so this is the cleanest isolation of decay in this
document: same facts, same extractor, same model, and only forgetting's vote differs. **52.9% → 91.4%, a
38.5-point swing**, with no seeding, corpus or model confound to argue about.

**2. A good write-time baseline WITHOUT decay is still indistinguishable from plain cosine** — p = 0.636,
the fifth arm in a row to land there. Strengthening the extractor moved this not at all, which is what makes
the earlier verdict safe to keep: it was not an artefact of a weak model.

**3. The columns say what each mechanism does.** Extraction IMPROVES FINDING — `current@k` 90.0 → 95.7,
better than raw turns — and DESTROYS BURYING, `stale@k` 54.3 → 87.1. **Extraction finds; decay buries.**
They are complementary rather than alternatives, which is the useful form of this result.

**The caveat that bounds it, and it is new:** evidence survival is **133/142 (93.7%)**, not 100%. A
compressing extractor drops evidence — 9 flagged turns lost their fact — where the hoarding one kept
everything. That caps the `extract` arms against `lyntai` and means part of the 96.9 → 91.4 gap is data loss
rather than mechanism. **It does not touch the decay-on/decay-off contrast**, which shares a store and
carries the claim.

**THE RECONCILING HALF RAN TOO, on the same strong model, and it HURTS** (`--reconcile`, all 70; 140
sessions, 898 facts, 0 mis-cited; asked 222 pairs, 83 answered from a shared cache, **replaced 27** — so the
arm is not a duplicate of `extract`). Raw output, gitignored:
`devtools/_lme-cli-reconcile.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

| arm | prefers current | current@k | stale@k | paired vs cosine |
|---|---|---|---|---|
| `lyntai` — raw turns + decay | **96.9%** | 90.0% | 54.3% | +32 −1, **p<0.0001** |
| `extract` — facts + decay | **87.0%** | 91.4% | 87.1% | +28 −2, **p<0.0001** |
| `extract+reconcile` — facts + ADD/UPDATE/DELETE + decay | 68.1% | 91.4% | 87.1% | +18 −4, p = 0.0043 |
| `extract+reconcile+forget0` — the full write-time design, NO decay | 65.2% | 91.4% | 75.7% | +22 −10, **p = 0.050** |
| `vector` | 47.1% | 84.3% | 95.7% | — |

**1. Reconciliation COSTS 18.9 points on top of decay** (87.0 → 68.1), and a strong model rules out the
explanation the first run left open. The 4B pass was read as "it deleted the wrong 122"; this one deletes 27
with a capable model and still hurts, so **the harm is in the mechanism as implemented here, not the model
tier.**

**2. The columns say HOW it hurts, and it is not by losing the answer.** `current@k` and `stale@k` are
IDENTICAL between `extract` and `extract+reconcile` (91.4 / 87.1) — the same facts are found. Only the
ORDER moved. **Deleting 27 entries thinned the corpus and re-ranked what was left against the current
fact**, which is the same mechanism the 4B run reported and is now measured with the sets held fixed.

**3. Decay alone beats the full write-time design without it, by 21.8 points** — 87.0% against 65.2%, on
the same extracted facts. That is the comparison the whole exercise was built for, and it is now against a
baseline with both halves of the mechanism, a strong model, accurate citations and 0.55× compression.

**4. The one row that moved, stated carefully.** `extract+reconcile+forget0` reads **p = 0.050** — exactly
at the conventional line, and the first decay-off arm to approach it. That is expected rather than
surprising: reconciliation IS a supersession mechanism, so an arm carrying one should beat a flat index. It
is borderline, it is one of several arms tested, and it lands 21.8 points below decay. **Read it as "the
field's design buys a little supersession at write time", not as a rival to decay.**

**Also not shown, and the caveats grew.** Evidence survival fell to **128/142 (90.1%)** because
reconciliation DELETES — 5 more flagged turns lost their fact than under extraction alone — so the extract
arms are bounded further against `lyntai`. The reconciler is OURS: top-1 candidate, a 0.80 cosine gate,
one pair per write. A differently-shaped one might not hurt, and that is the live limit rather than the
model. One class, oracle variant. And nothing here ranks against Mem0 or Zep in either direction; §5's
comparability gap applies unchanged. What it licenses is a claim about MECHANISMS — write-time
consolidation against read-time decay — measured on a baseline built to be good.

### The multi-session shot curve, and the ORACLE overstated its headline by 4× (`memory-longmemeval --multi --shots`, 2026-09-04) <!-- result: id=longmemeval-multi-shot2-haystack arm="`shot-2` on the `--haystack` variant (61,182 turns per arm)" metric=all-evidence-recall n="125 of 133 multi-session questions" value="+4.8" ships=no status=CURRENT -->

The third LongMemEval class to get a shot curve, and the first whose metric was settled by measuring the
class rather than reasoning about it: `multi-session` carries 2.5 flagged turns with **91% spanning more than
one session**, and no current/stale split, so knowledge-update's preference metric is structurally
inapplicable and temporal's all-evidence recall is what it asks for (`docs/task-archive.md` Part 234).
125 of 133 questions load — the other 8 carry no flagged turn at all.

**Instrument.** `--multi --shots`, oracle then `--haystack` (61,182 turns per arm, 489 per question), 338.2s
and 4,615.3s. Raw output, gitignored:
`devtools/_lme-multi-oracle.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->
and `devtools/_lme-multi-haystack.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

| arm | oracle | haystack |
|---|---|---|
| shot-1 | 22.4% | 24.0% |
| shot-2 | 41.6% (**+19.2**) | 28.8% (**+4.8**) |
| shot-3 | 48.0% (+6.4) | 28.8% (**+0.0**) |
| `vector` | 47.2% | 38.4% |
| `vector-20` | 80.0% | 63.2% |

**1. The oracle overstated the second shot's gain by 4×**, and it is the fifth question on which that
variant has proved biased in a direction nobody predicted. Part 112 measured 2.7× on the class where it was
first checked; this is worse. **Read the haystack column and treat the oracle one as a cheap upper bound.**

**2. Shot 3 is worth EXACTLY nothing on the haystack** — identical on every column — so *expand once* holds,
now four classes in a row. **The oracle's +6.4 briefly refuted that and was published here for an hour <!-- result: id=longmemeval-multi-shot3-oracle arm="`shot-3` on the ORACLE variant" metric=all-evidence-recall n="125 of 133 multi-session questions" value="+6.4" ships=no status=RETRACTED -->
before the haystack arrived**; the retraction is recorded rather than quietly dropped, because the oracle
number was the one that felt like a discovery.

**3. Plain cosine wins this class outright, on BOTH axes.** `vector` at k = 10 reads 38.4% on 10,382
characters against the walk's 28.8% on 9,053 — better recall AND better recall per character, which no
earlier class showed. All-evidence recall is an ARCHIVE metric that rewards keeping everything, and burying
is what this engine is for; the same shape appears on LoCoMo and in the temporal class, where size-matched
cosine also won the column.

**What it does not say.** One embedder, model-free scoring, no reader. The class is the one where a walk
should look best — evidence spread across sessions the conversation never put side by side — so a +4.8
second shot is the honest size of that effect under distractors, not a floor.

### WRITE-time extraction cannot stand in for READ-time decay, and the half that hurts is the half without reconciliation (`memory-longmemeval --extract`, 2026-09-04) <!-- result: id=longmemeval-extract-forget0-4b arm="`extract+forget0` — `gemma3:4b`-extracted facts, decay OFF" metric=prefers-current n="70 knowledge-update questions, oracle variant" value="53.0%" ships=no status=CURRENT -->

The other design in this field consolidates at WRITE time — a model extracts facts as turns arrive — where
this engine stores raw turns and resolves at READ time with decay. This runs both over the same questions.
**It is measurable at all because the extractor carries each fact's source marker**, so a synthesized fact
keeps the provenance the model-free metric matches on; a real third-party system returns facts that cannot
be scored this way, which is why the comparison is internal.

**Instrument.** `memory-longmemeval --extract`, 70 knowledge-update questions, ORACLE variant (extraction is
1,589 model calls here against ~34,000 on the haystack). Extractor `gemma3:4b`, write-side only — nothing
reads. Raw output, gitignored: `devtools/_lme-extract.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
**Both controls reproduce their published oracle figures** (`lyntai` 96.9%, `vector` 47.1%).

| arm | prefers current | current@k | stale@k | paired vs cosine |
|---|---|---|---|---|
| `lyntai` — raw turns + decay | **96.9%** [89.5, 99.2] | **90.0%** | 54.3% | +32 −1, **p<0.0001** |
| `extract` — facts + decay | 86.0% [74.7, 92.7] | 75.7% | 40.0% | +24 −3, p<0.0001 |
| `extract+forget0` — facts, decay OFF | 53.0% [41.2, 64.6] | 85.7% | 87.1% | +16 −12, **p = 0.572** |
| `vector` | 47.1% [35.7, 58.8] | 84.3% | 95.7% | — |

**1. Extraction does NOT substitute for decay.** With forgetting silent, extracted facts score 53.0% and are
**statistically indistinguishable from plain cosine** (p = 0.572) — the same verdict `+sem+forget0` earned on
raw turns. Removing decay lands at flat-retriever behaviour whatever the store holds.

**2. Extraction HURT alongside decay**: 96.9% → 86.0%, and `current@k` fell 90.0% → 75.7%. On 13 of 70
questions it returned neither fact (`decidable` 57 against 65).

**3. The mechanism is DILUTION, not data loss, and the control is what proves it.** Every one of the 142
flagged turns kept a fact (**142/142**), so nothing was deleted — but 1,589 turns became **11,271 facts**, a
7.1× inflation of near-duplicate one-liners, so the evidence competes against far more lookalikes. Without
that column the drop would have been read as a ranking failure.

**4. What this does NOT show, and it is the load-bearing caveat: reconciliation was not built.** Facts are
extracted, never merged or superseded, so a fact and its replacement both land. **The failure mode measured
here — near-duplicate inflation — is exactly what an ADD/UPDATE/DELETE pass removes.** So this is not a
verdict on write-time consolidation; it isolates its halves and says the value, if any, is in the RECONCILING
half. `IMemoryGraphStore.DeleteAsync` makes that reachable.

**Also not shown.** One extractor at 4B, one class, oracle variant. 7.1 facts per turn is a property of this
model and this prompt as much as of the design; a stronger extractor compresses harder and would dilute less.

**THE RECONCILING HALF RAN (`--reconcile`, n = 25, 2026-09-04) and does not change the verdict.** A new fact <!-- result: id=longmemeval-reconcile-n25 arm="`extract+reconcile` — ADD/UPDATE/DELETE at 4B, n = 25" metric=prefers-current n="25" value="75.0%" ships=no status=RETRACTED -->
that supersedes a stored one now DELETES it through the store instead of leaving it to decay — the mechanism
the field's write-time designs actually claim. It fired: **asked 1,427 times, replaced 122**, so the arm is
not a duplicate of `extract`.

| arm | prefers current | current@k | stale@k | paired vs cosine |
|---|---|---|---|---|
| `lyntai` | **95.8%** [79.8, 99.3] | 92.0% | 44.0% | +11 −0, **p<0.001** |
| `extract` | 80.0% [58.4, 91.9] | 72.0% | 28.0% | +8 −2, p = 0.109 |
| `extract+reconcile` | 75.0% [53.1, 88.8] | 72.0% | **44.0%** | +6 −1, p = 0.125 |
| `extract+reconcile+forget0` | 60.9% [40.8, 77.8] | 84.0% | 80.0% | +6 −3, **p = 0.508** |
| `vector` | 48.0% [30.0, 66.5] | 84.0% | 100.0% | — |

**1. `stale@k` moved the WRONG WAY** — 28.0% → 44.0%. Reconciliation exists to delete superseded facts, so
that column should fall. Removing 122 entries thinned the corpus generally and let stale facts surface more
easily: **it deleted the wrong 122.** Whether that is the 4B model misjudging supersession or the cosine
gate hiding the pairs that mattered is unmeasured — there is no ground truth here for which pairs SHOULD
have been replaced, and building one is the next step if this direction is pursued.

**2. Decay-off still does not reach the decay arm with BOTH halves present.**
`extract+reconcile+forget0` reads p = 0.508 against cosine — the third arm in a row to land
indistinguishable from a flat index once forgetting is silent.

**3. What n = 25 cannot support, stated because the percentages invite it.** Only `lyntai` clears
significance. `extract` (p = 0.109) and `extract+reconcile` (p = 0.125) are not distinguishable from cosine
NOR from each other, so **"reconciliation is worse than extraction" is not a result** — the run supports
only that neither substitutes for decay. A powered comparison needs the full 70 and a stronger model.

**THE INFLATION WAS THE PROMPT, AND FIXING IT DOES NOT CHANGE THE VERDICT** (`--extract --facts 2`,
2026-09-04). The run above closed by naming its own load-bearing caveat — *"7.1 facts per turn is a property
of this model and this prompt as much as of the design"* — so the extractor was given a budget: **at most 2
facts, stated in the prompt and never truncated in code**, because truncating the reply would measure
truncation rather than the model choosing. Same 70 questions, same seed, same oracle variant. Raw output,
gitignored: `devtools/_lme-facts2.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

**The controls reproduce CELL FOR CELL** — `lyntai` 96.9%/90.0%/54.3% at +32 −1, `vector`
47.1%/84.3%/95.7%, every figure identical to the unbounded run including its McNemar counts. That is what
licenses reading the two runs against each other.

| arm | prefers current | current@k | stale@k | decidable | paired vs cosine |
|---|---|---|---|---|---|
| `lyntai` — raw turns + decay | **96.9%** [89.5, 99.2] | **90.0%** | 54.3% | 65 | +32 −1, **p<0.0001** |
| `extract` — bounded facts + decay | 88.1% [78.2, 93.8] | **87.1%** | 57.1% | 67 | +28 −1, **p<0.0001** |
| `extract+forget0` — bounded, decay OFF | 55.1% [43.4, 66.2] | 85.7% | 92.9% | 69 | +16 −10, **p = 0.327** |
| `vector` | 47.1% [35.7, 58.8] | 84.3% | 95.7% | 68 | — |

**1. The budget bound, and it cost no evidence.** 7.1 → **2.1 facts per turn**, 11,271 → **3,308**, with the
model exceeding the budget on 132 of 1,589 turns (8.3%) — so this arm measures a bounded corpus rather than a
budget the model declined, which is the reading a score column cannot separate on its own. **Evidence
survival stayed 142/142**: the compression dropped nothing, so nothing below was bought with data loss.

**2. Dilution is CONFIRMED as the mechanism, and read the count rather than the rate.** `current@k` recovered
**75.7% → 87.1%**, taking back 11.4 of the 14.3 points the unbounded extractor had lost against `lyntai`'s
90.0%. `prefers current` moved only 86.0% → 88.1%, and that understates it: **the rate is conditioned on
`decidable`, which grew 57 → 67**. On the fixed 70-question denominator the arm goes **49 → 59**, so
bounding the extractor recovers **10 of the 14 questions** the unbounded one lost. A denominator that grows
when an arm improves flatters the arm it replaces.

**3. What it did NOT fix is the whole point: `stale@k` ROSE, 40.0% → 57.1%.** A smaller corpus surfaces the
current fact more often *and* the superseded one more often — compression helps both facts compete, and
resolves nothing between them. Extraction reshapes what is FOUND and cannot BURY, which is the same split
the acceptance test states for decay (§5, one knob two workloads). The residual gap to `lyntai` is exactly
the part decay does and extraction does not.

**4. The pre-registered prediction held on the arm that decides it.** `extract+forget0` reads 55.1% against
cosine's 47.1% at **p = 0.327** — the fourth arm in a row to land indistinguishable from a flat index once
forgetting is silent. **So the verdict survives the strongest version of its own counter-arm**: write-time
extraction, properly bounded and losing no evidence, still does not substitute for read-time decay. That is
the caveat the previous run filed, now closed rather than restated.

**What this does not settle.** ONE budget value on ONE model, one class, oracle variant — a value is not a
curve, and nothing here says 2 is the right bound. **And the instrument pairs every arm against `vector`
only**, so the 88.1% against `lyntai`'s 96.9% is a difference in LEVEL and not a tested one; pairing the arms
against `lyntai` as well is a cheap change the next run on this harness should carry.

### ONE KNOB, TWO WORKLOADS: decay off is a flat retriever, decay on is what adds supersession (2026-09-03) <!-- result: id=longmemeval-sem-decay-on arm="`+sem` — decay ON (the one-knob partner of `+sem+forget0`)" metric=prefers-current n="70 questions (68 paired)" value="72.5%" ships=no status=CURRENT -->

The design's own acceptance test, and the arm that makes it possible is new: **`+sem` and `+sem+forget0`
differ in exactly one vote — forgetting's.** Every earlier "decay off" arm confounded it (`+forget0` also
drops the semantic channel, `+sem+rel-only` also drops traversal), so the pair below is the first clean
statement of what decay is worth.

**Instrument.** `memory-locomo --retrieval --n 200 --no-judge` and `memory-longmemeval --haystack`, both
with `--arms +sem,+sem+forget0,vector`; 785.6s and 3564.6s. Raw output, gitignored:
`devtools/_parity-locomo.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->
and `devtools/_parity-lme.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->.

| | LoCoMo evidence-hit@20 | knowledge-update prefers-current | paired vs cosine |
|---|---|---|---|
| `+sem` — decay ON | 76.5% | **72.5%** [61.0, 81.6] | +21 −3, **p<0.001** |
| `+sem+forget0` — decay OFF | **83.0%** | 49.3% [37.8, 60.8] | +8 −6, **p = 0.791** |
| `vector` — plain cosine | 80.5% | 46.4% [35.1, 58.0] | — |

**1. Decay OFF is a flat retriever, and the significance test is the statement.** On the supersession class
it is **indistinguishable from plain cosine** (p = 0.791 over 68 paired questions); on the search workload
it reaches 83.0% against cosine's 80.5%. So with forgetting silent this engine performs like a competent
embedding index and claims nothing extra — which is what "the base logic is correct" should look like.

**2. Decay ON is the entire supersession win, and it is significant.** 49.3% → 72.5%, p < 0.001 against
cosine where decay-off cannot be told apart from it. **`current@k` is IDENTICAL at 90.0% either way**, so
the knob changes what is BURIED and never what is FOUND — Part 140's mechanism, now shown on a one-knob
pair rather than across two differently-seeded arms.

**3. The price is 6.5 points of LoCoMo, and paying it is the design.** 83.0% → 76.5% on the workload that
rewards a perfect archive and penalises forgetting by construction. A decay improvement is SUPPOSED to cost
here; an arm that wins both is not evidence of a better engine, it is evidence that decay stopped working.

**4. The win here is ORDERING, not exclusion — and that differs from the shipped default.** `stale@k` barely
moves between the two arms (90.0% against 92.9%) while preference moves 23 points: the semantic channel
pulls the superseded fact back onto the page, so decay wins by ranking it BELOW the current one. At the
shipped default, which registers no semantic channel, `stale@k` is 62.9% and the same headline is produced
by genuine suppression. **Two mechanisms, one number**, and only this pair separates them.

**What it does NOT say.** `+sem` is not the shipped configuration — the shipped engine reads 86.4% on this
class against this arm's 72.5%, because the semantic channel costs supersession. This pair is internally
valid (one knob) and is not a statement about the default's absolute level. One embedder, 70 questions, and
the CIs overlap between `+sem+forget0` and `vector` precisely because they are the same thing.

### BURIAL, NOT DELETION: D41's invariant, measured for the first time — and the weight at which it stops holding (`memory-longmemeval --recover`, 2026-09-03) <!-- result: id=longmemeval-recover-deep100-w1 arm="`RetrievabilityWeight` = 1 — shipped" metric=recovery@k n="26 buried entries, from 70 knowledge-update questions" value="100.0%" ships=yes status=CURRENT -->

**D41 says a decayed entry is buried rather than deleted, and until now that rested on a store contract
(`IMemoryGraphStore.SeedAsync`'s *faintness never excludes*) rather than on evidence.** No metric on record
could see it: every table here scores what a recall RETURNED, and the invariant is a claim about what it did
not. "Ranked below the cut" and "gone" produce identical rows in all of them.

**Instrument.** `memory-longmemeval --haystack --recover`, 70 knowledge-update questions, model-free. Raw
output, gitignored: `devtools/_lme-recover2.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Scored ONLY over entries the arm actually suppressed — anything else inflates every row toward 100% by
construction. The focused query is the buried entry's own words with its `(sNtM)` marker stripped, so
recovery cannot come from matching a token no real caller would type.

| `RetrievabilityWeight` | buried | page@10 | walk | **deep@100** | mean rank |
|---|---|---|---|---|---|
| **1 — shipped** | 26 | 76.9% | 76.9% | **100.0%** [87%, 100%] | **5.0** |
| 2 | 41 | 26.8% | 87.8% | **100.0%** [91%, 100%] | 41.7 |
| 4 | 69 | 0.0% | 37.7% | 18.8% [11%, 30%] | 76.8 |

**1. The invariant HOLDS at the shipped default: 26 of 26, at mean rank 5.** A more focused query returns
every buried entry, usually near the top of an ordinary page (76.9% inside ten slots) and always within a
hundred. Decay costs an entry its default position, never its existence.

**2. Recoverability is not binary — it DEGRADES continuously and then falls off a cliff between 2 and 4.**
The entry sinks steadily under its own query (mean rank 5.0 → 41.7 → 76.8) while staying fully recoverable
at weights 1 and 2; only past 2 does it become unreachable, at which point 81% of buried entries are gone.
So "ask more precisely" keeps working across the usable range and gets measurably more expensive: at weight
2 a caller needs a page of roughly fifty to see what a page of ten showed at weight 1.

**3. The cliff is where the best-looking suppression figure lives.** `+forget4`'s `stale@k` of 1.4% is the
strongest suppression this benchmark has produced and it was **bought by deletion**. The shipped weight sits
on the safe side of a measured boundary, which is a stronger statement than any ladder alone could make.

**3. `deep@100` is the column that decides this, and `page@10` is the one that misleads.** A first run <!-- result: id=longmemeval-recover-page10-first arm="the FIRST `--recover` run — `page@10` reported alone, no `deep@100`" metric=recovery@k n="unstated" value="'decay deletes one entry in five'" ships=no status=RETRACTED -->
reported only the page and read as *"decay deletes one entry in five"*; absence from a ten-slot page is what
decay is FOR. The two columns are kept side by side so the distinction cannot be lost again.

**4. An exact-content query never returns a buried entry FIRST — 0% at rank 1, mechanically.** RRF gives
retrievability the same weight as relevance, so a buried entry at relevance-rank 1 scores
`1/61 + 1/460` while a fresh entry at relevance-rank 5 scores `1/65 + 1/61` and wins. "Ask more precisely"
recovers the entry; it cannot restore it to the top.

**5. The WALK is an independent route back, and at the shipped default it partly UNDOES suppression.** It
recovers 76.9% at weight 1, **87.8% at weight 2** — rising as suppression deepens, since a more buried set
is still a well-connected one — and 37.7% at weight 4 where a focused query manages 18.8%, so the graph
reaches what a query cannot. The cost side is the same number: expansion does not consult
retrievability, because `GraphMemoryOptions.ExpansionRetrievabilityFloor` ships at `0`. **This is the first
figure D98 has ever had**, and it means an n-shot walk re-surfaces what one-shot decay buried.

**What it does NOT say.** One embedder, one class, 70 questions; the buried counts (26 and 69) are the real
denominators and they are small. The focused query is the entry's own text — deliberately the easiest honest
probe, so this is a floor test for reachability and not a test of how hard recovery is from a paraphrase.

### Louder forgetting is a VOLUME knob, not a discriminator (`memory-longmemeval`, 2026-09-03) <!-- result: id=longmemeval-forget4-vacuous arm="`+forget4` — `RetrievabilityWeight` walked up to 4" metric=prefers-current n="70 questions, of which 23 decidable" value="100.0%" ships=no status=CURRENT -->

Every arm on record moved `RetrievabilityWeight` DOWN. This walks it up, on the class the design claims.

| arm | prefers current | current@k | stale@k | decidable |
|---|---|---|---|---|
| `lyntai` (weight 1) | 86.4% | **87.1%** | 62.9% | 66 |
| `+forget2` | 85.0% | 77.1% | 41.4% | 60 |
| `+forget4` | **100.0%** | 32.9% | 1.4% | **23** |

**`+forget4`'s 100% is the vacuous score the `decidable` control exists to catch**: it returns either fact on
23 of 70 questions, and finds the CURRENT one only 32.9% of the time against the shipped 87.1%. Read the
preference column without that denominator and louder forgetting looks perfect.

**Forgetting's vote suppresses BOTH facts.** `stale@k` falls 62.9 → 41.4 → 1.4 and `current@k` falls
87.1 → 77.1 → 32.9 with it. The vote ranks by AGE, and both facts are old relative to the query — the
current one survives only by being newer. **So the volume knob is not the lever**; a real gain in focus has
to come from the decay SIGNAL (what age means, and the curve's shape), not from how loudly it votes.
Together with the recovery table above, `RetrievabilityWeight = 1` is now a measured choice bounded on both
sides: 0 costs −37.1 points of supersession, 4 deletes.

### The benchmark where forgetting WINS (`memory-longmemeval`, 2026-08-29) <!-- result: id=lme-update-lyntai-haystack-n70 arm="`lyntai`, haystack variant (model-free, k = 10, `nomic-embed-text`)" metric=prefers-current n="70 knowledge-update questions (66 decidable)" value="86.4%" ships=yes status=CURRENT -->

LoCoMo rewards a perfect archive and penalises decay by construction. This is the opposite shape and the
one this design actually claims: **LongMemEval's knowledge-update class**, 70 questions, each carrying an
earlier session stating a fact and a later one REVISING it. Both sit in the store, both are textually
similar, and the flagged turns say which is which. So the score is not "can you find it" but **"do you
prefer the CURRENT value over the superseded one"** — a claim a decay model makes and a flat index has no
mechanism to make. Model-free, `k = 10`, `nomic-embed-text`.

**Two variants, and the pair is the measurement.** `memory-longmemeval` reads the oracle file, whose haystack
holds only the evidence sessions (~25 turns per question); `--haystack` reads `longmemeval_s`, which puts the
same questions among ~490 turns of distractors (34k and 65k turns ingested per arm, per class). Both classes
ran in full — 70 knowledge-update and 132 temporal-reasoning — so nothing here is sampled.

| arm | variant | prefers current | current@k | stale@k | decidable |
|---|---|---|---|---|---|
| `lyntai` | oracle | **96.9%** (63/65) | 90.0% | 54.3% | 65 |
| `lyntai` | haystack | **86.4%** (57/66) | 87.1% | 62.9% | 66 |
| `vector` | oracle | 47.1% (32/68) | 84.3% | 95.7% | 68 |
| `vector` | haystack | 46.4% (32/69) | 81.4% | 88.6% | 69 |

**Plain cosine is at chance in BOTH variants**, which is what "no mechanism" looks like when it is measured
rather than asserted: it returns the superseded fact 95.7% of the time (88.6% with distractors) and picks
between the two about half the time either way. This engine prefers the current one roughly **twice as
often**.

**The haystack rows are the same 70 questions among ~490 turns of distractors** — the run the caveat below
used to be waiting on. The lead narrows from **+49.8 to +40.0** and the finding holds. What moved is
suppression, not retrieval: `current@k` falls 2.9 points for this engine and 2.9 for cosine — identically —
while `stale@k` rises 8.6, so the distractors cost it the ability to *bury* the superseded fact rather than
the ability to find the current one.

**Two controls stop that being an artifact, and both hold in both variants.** `prefers current` is scored
only over questions where the arm returned at least one of the pair, so retrieving NEITHER cannot score a
vacuous 100% — and the `decidable` counts are comparable (65 against 68 on the oracle, 66 against 69 on the
haystack). More decisively, **`current@k` is HIGHER for this engine while `stale@k` is far lower**: it
returns the current fact more often *and* the superseded one less often. That is discrimination, not a
recall collapse dressed as precision.

**The COST side, measured on purpose rather than left to be discovered** (`memory-longmemeval --temporal`,
132 temporal-reasoning questions). That class is not more of the same: *"what was the FIRST issue after the
service"* wants the EARLIER fact, and most questions need BOTH — so the suppression that wins knowledge-update
should hurt here, and the metric is all-evidence recall rather than preference.

| arm | variant | all evidence@k | any evidence@k | evidence turns |
|---|---|---|---|---|
| `lyntai` | oracle | 59.8% | 84.1% | 66.0% |
| `lyntai` | haystack | **47.7%** | **82.6%** | **61.8%** |
| `vector` | oracle | **64.4%** | **90.9%** | **72.2%** |
| `vector` | haystack | 43.9% | 75.0% | 57.5% |

**On the oracle it hurts by 4.6 points, exactly as predicted from the mechanism. On the haystack the sign
REVERSES**, to **+3.8**. Distractors cost cosine 20.5 points of all-evidence recall and cost this engine
12.1 — so where there is finally something to suppress, suppressing it stops being a cost even in the class
built to penalise it.

**Read that as "the cost is gone", not as "this engine wins temporal".** +3.8 on 132 questions is five
questions, which is not a result to lean on by itself. What makes it worth stating is that all three columns
move the same way and the other two move further: any-evidence@k is +7.6 (ten questions) and the per-turn
rate +4.3. A single column flipping would be noise; three agreeing is the same mechanism showing up three
ways.

**The three numbers together are the finding, and no one of them is** (haystack throughout — see below for
why the oracle figures are not the ones to quote):

| workload | what it asks | `lyntai` | `vector` | delta |
|---|---|---|---|---|
| LoCoMo | retrieve arbitrary old material | 54.5% | 80.5% | **−26.0** |
| LongMemEval temporal | need the old fact AND the new | **47.7%** | 43.9% | **+3.8** |
| LongMemEval knowledge-update | prefer the new over the superseded | **86.4%** | 46.4% | **+40.0** |

_The LoCoMo row was **−49.5** until 2026-08-29, on a run whose questions shared a store; it is the isolated <!-- result: id=locomo-delta-shared-store arm="`lyntai` vs `vector` on LoCoMo, shared-store run (pre-isolation)" metric=evidence-hit@k n="unstated" value="−49.5" ships=no status=RETRACTED -->
figure now (Part 118). The two LongMemEval rows are unaffected — that harness already built one store per
question, which is how the LoCoMo defect was recognised as a defect rather than as a property of the data._

**One mechanism produces all three.** Suppressing superseded material is no longer a cost where both facts
are wanted (+3.8, five questions), decisive where the old one is wrong (+40.0), and expensive only where the
workload is to retrieve arbitrary old material nobody has referred to since (−26.0). A library scoring well on all three
would be one that had stopped forgetting. So the honest summary is not "better" or "worse": **decay is a bet
about which of those workloads a deployment has**, and these are its measured odds.

**The caveat that used to bound all three is DISCHARGED, and it was wrong in a way worth keeping.** It said
the oracle variant — whose haystack holds only the evidence sessions, two to six of them — has almost
nothing to bury, so it "flatters the temporal number and may flatter the update one". Measured in the same
unit — the gap between the arms — it flattered the update one by **9.8** points and **penalised** the
temporal one by **8.4**, which is the opposite direction and enough to invert the sign. So the oracle is not
a cheap unbiased proxy for the haystack: it is biased, differently per class, and the bias is not signable
in advance.

**Its mechanism is visible in one number nobody had looked at: at `k = 10` over ~25 turns, the oracle
returns ~40% of the store.** That is barely a retrieval test at all — most of the corpus comes back whatever
the ranking does — whereas the haystack's ~490 turns make the same `k` a 2% slice. So the oracle measures
something closer to *is it in the store* than *did you rank it top-ten*, and which arm that favours depends
on the class rather than on the ranker: it favoured this engine on knowledge-update and cosine on temporal.
That is why the bias could not have been signed in advance, and why it had to be run rather than reasoned
about.

**Two controls, because both halves of this could have been the harness.** The oracle arms were re-run under
the loader that reads the haystack and reproduce **byte-identically** on all fourteen cells, so the change
moved no published number. And each class's two variants ingest the **same question ids**, proven rather than
assumed: the sample fingerprint printed in each preamble matches across variants (`D860F77A3D9E` for
knowledge-update, `773FB41E0E5A` for temporal), which is what makes an oracle row and a haystack row
comparable line by line.

**The harness defect the haystack exposed, recorded because it would have been silent.** The loader took the
current value from the latest-DATED session. In the oracle every session is an evidence session, so that is
right by accident; in the haystack the last-dated session is a distractor nearly every time, so the rule
found no current turn and would have dropped the entire class — reporting an empty run rather than a wrong
number, which is the cheap direction only because nothing else depended on it. It now takes the latest dated
session that *carries* a flagged turn, which is what the oracle numbers above prove is a no-op there.

### Why suppression weakened under distractors — the loss is in the FUSION (`memory-longmemeval --ranks`, 2026-08-29) <!-- result: id=lme-fusion-rrf-gap-haystack-n25 arm="`lyntai` shipped ranking observed by the `--ranks` probe, haystack against oracle" metric=rrf-score-separation n="25 questions, same in both variants (sample digest `9694C9D71534`)" value="−29%" ships=yes status=CURRENT -->

One row of the haystack table does not fit the story it tells: `stale@k` **rose**, 54.3 → 62.9, while twenty
times more candidates competed for the same ten slots. More competition should crowd the superseded fact
out. `--ranks` installs a probe `IMemoryRankingPolicy` that observes the candidate pool and delegates the
real ranking untouched — so it describes the run that produced the table rather than a reconstruction of it.
Same 25 questions in both variants (sample digest `9694C9D71534`).

| | oracle | haystack |
|---|---|---|
| pool size (median) | 24 | 112 |
| relevance rank — current / stale | 2 / 4 | 3 / 4 |
| retrievability rank — current / stale | 10 / 20 | 74 / 102.5 |
| retrievability VALUE — current / stale | 0.9440 / 0.9062 | 0.8556 / 0.7206 |
| RRF contribution gap, from relevance | 0.00024 | 0.00024 |
| RRF contribution gap, from retrievability | **0.00179** | **0.00127** |

**The decay model did not weaken. It improved, and the fusion discarded the improvement.** The value gap
between the two facts grew **3.6×** (0.0347 → 0.1235) and the rank gap grew **2.6×** (10 → 26 positions) —
and the score separation RRF actually sums fell **29%**. The reason is arithmetic: `1/(K + rank)` is convex,
so a gap between ranks 10 and 20 is worth far more than the same gap between 74 and 102. Distractors written
after the current fact push BOTH into the flat region, where the signal that tells them apart stops being
paid for. Relevance is unmoved at 0.00024 either way, because the pair are the two most query-similar entries
in the store whatever its size.

**So the obvious fix is a lower `K` — a steeper curve — and the ladder refutes it.** Scored offline from the
same candidate set, which is not a shortcut: re-running per K would need a fresh store each time, because a
recall reinforces what it returns and would contaminate every later arm (**Part 110**). RRF at another K is a
pure function of ranks already in hand.

| K | oracle current@k | oracle stale@k | haystack current@k | haystack stale@k |
|---|---|---|---|---|
| 1 | 88.0% | 72.0% | 91.7% | 79.2% |
| 3 | 88.0% | 68.0% | 91.7% | 79.2% |
| 10 | 92.0% | 64.0% | 91.7% | 75.0% |
| 30 | 92.0% | 52.0% | 87.5% | 75.0% |
| **60 (shipped)** | 92.0% | 44.0% | 87.5% | 54.2% |
| 120 | 92.0% | **32.0%** | 87.5% | **41.7%** |
| 300 | 92.0% | 32.0% | 70.8% | 16.7% |
| 1000 | 92.0% | 32.0% | 66.7% | 8.3% |

**Lowering K makes suppression worse, monotonically, in both variants** — because `K` selects a REGIME, not a
sharpness. At low K, being top-few on *one* signal outweighs being mediocre on the rest, and the stale fact
is relevance rank 4. At high K the curve flattens toward `(1/K)(1 − r/K)`, so the order tends to the SUM of
ranks — Borda count — which rewards a candidate that is good on every signal.

**The lever therefore runs upward, and the haystack is what bounds it.** K = 120 costs nothing measurable on <!-- result: id=lme-k120-current-free-n25 arm="K = 120 (RRF ladder rung), haystack" metric=current@k n="25 questions" value="0.0 points" ships=no status=RETRACTED -->
`current@k` in either variant and cuts `stale@k` by ~12 points in both. Past that the two variants disagree:
the oracle saturates harmlessly at 32% forever, while the haystack starts paying real recall — **−16.7 points
of `current@k` at K = 300**. Read on the oracle alone, K = 1000 looks free. That is the same bias this
document records one section above, now caught on a second question.
<br>**AMENDED 2026-08-30 — "K = 120 costs nothing measurable" was a 25-QUESTION artifact and is withdrawn.**
Re-run on all 70 (below), the haystack pays **6.0 points** of `current@k` going 60 → 120, where the sample
said 0.0. The direction of the `stale@k` gain survives and is larger; what does not survive is *free*. The
sample was the only thing that changed, which is the sharpest available demonstration that a 25-question
haystack figure is reproducible to about one question and not to a tenth of a point.

**Two controls, and the first caught a real defect.** The ladder's replica of the scoring must reproduce the
SHIPPED policy's own top-10, or it is a table about a formula this library does not run: it agrees on
**25/25** oracle and **24/24** haystack recalls. It did not at first — `MemoryRankingContract.Finish` breaks
score ties by **descending** id, so the newer entry wins a tie, and a replica that broke them ascending moved
the shipped row by 4 points while looking entirely plausible. Second, the ladder's K = 60 row reproduces the
ARM's own measured numbers on the same sample **exactly** (92.0% / 44.0%), which is what makes it comparable
to the published table at all.

**What this does not settle, and it is most of it.** One class of one benchmark, 25 questions, one embedder.
`K` is a GLOBAL ranking constant — every LoCoMo figure in this document was measured at 60, and moving it
would move them all, in a direction this says nothing about. 60 is Cormack, Clarke & Buettcher's published
value for fusing IR result lists, which is a different problem from fusing decay against relevance. **This is
an argument for sweeping `K` properly, not for changing a default**; `docs/task-archive.md` Part 233
carried it, and the section below is that sweep.

### `K` is a COMPROMISE, and the two benchmarks pull opposite ways (`memory-locomo --ranks`, 2026-08-30) <!-- result: id=k-ladder-locomo-shipped-k60-n200 arm="K = 60 (shipped)" metric=evidence-hit@k n="200 LoCoMo questions, seed 12345 (LME ladder: all 70 knowledge-update haystack, seed 20260829)" value="54.5%" ships=yes status=CURRENT supersedes="lme-k120-current-free-n25" -->

The ladder above read as *"the shipped 60 is on the wrong side of free"* — K = 120 bought suppression for
nothing. **That was one workload wide, and both halves of it have now failed.** LoCoMo is a SEARCH
benchmark, which wants old material FOUND rather than suppressed, and raising `K` costs it monotonically;
and on the full 70-question haystack, K = 120 is not free on knowledge-update either.

Both ladders are model-free, scored offline from one ingestion over the pool the shipped arm actually saw.
LoCoMo: 200 questions, seed 12345, the sample every LoCoMo table here uses. LongMemEval: knowledge-update
haystack, all 70, seed 20260829.

| K | LoCoMo evidence-hit ↑ | LME `current@k` ↑ | LME `stale@k` ↓ |
|---|---|---|---|
| 1 | **59.0%** | 92.4% | 81.8% |
| 3 | 59.0% | 92.4% | 81.8% |
| 10 | 59.0% | 92.4% | 80.3% |
| 30 | 57.5% | 90.9% | 80.3% |
| **60 (shipped)** | 54.5% | 92.4% | 65.2% |
| 120 | 50.0% | 86.4% | 48.5% |
| 300 | 40.5% | 80.3% | 22.7% |
| 1000 | 33.0% | 74.2% | **9.1%** |

**Every step that helps one metric hurts another, and the shipped value sits between them.** 60 → 120 buys
16.7 points of suppression and costs **4.5** of LoCoMo evidence-hit *and* **6.0** of `current@k`. 60 → 10
buys 4.5 points of evidence-hit at no `current@k` cost and gives back **15.1** points of suppression. There
is no K that is free on both benchmarks, so 60 is not an unexamined default — it is a **compromise nobody
had priced until now**.

**Two controls on the LoCoMo side, both green.** The replica reproduces the shipped policy's own top-20 on
**200/200** recalls; anything under 100% would make this a table about a formula the library does not run.
And the K = 60 row reads **54.5%**, the `lyntai` arm's own published retrieval figure to the decimal, which
is what makes this an extension of that table rather than a separate measurement. The haystack ladder's
control is **66/66**.

**`K` is not where the LoCoMo gap is, and the pool says so.** 32 of the 200 questions had no evidence in the
candidate pool at all — a SEEDING failure no ranking constant can reach — so the pool's ceiling is **84.0%**
while the shipped ranking returns **54.5%**. The fusion therefore loses **29.5 points of material it already
held**, and the best K on the ladder recovers **4.5** of them, about a sixth. That is the quantified form of
this document's existing claim that the evidence was stored, embedded and reachable while the engine spent
its slots elsewhere. It also frames the cosine comparison: `vector`'s 80.5% sits just under that 84%
ceiling, so cosine is close to saturating what the pool contains.

**What this does not settle.** The three columns come from two instruments and are not commensurable as
LEVELS — only each column's shape transfers. One embedder throughout. The two harnesses also count
differently: LoCoMo keeps a pool-unreachable question in the denominator (so the ladder stays comparable to
its published row), while LongMemEval excludes one whose pair is outside the pool, 4 of 70 here.

### The mode this engine is FOR: shots, not one-shot (2026-08-29) <!-- result: id=lme-shots-clean-shot1-n70 arm="`shot-1` (single recall, no expansion), LongMemEval knowledge-update haystack" metric=clean n="all 70 questions" value="31.4%" ships=yes status=CURRENT supersedes="lme-shots-clean-shot1-n25" -->

Every number above scores a SINGLE top-k, and that is not how this engine is meant to be read. It says so
itself: a recall returns **headlines** because *"associative content is withheld until expansion — that is
what makes the first load cheap"*, and `ExpandAsync` reinforces what it walks because *"digging in one
direction is exactly what should make that direction more retrievable next time"*. A one-shot benchmark is
structurally blind to both. `--shots` measures the walk instead, model-free.

_The LIBRARY surface calls one of these a **step** (`MemoryWalkStep`, reached through `WalkAsync`). The
benchmark arms below keep saying **shot**, deliberately: those strings are the published data every table
here is compared against, and renaming them would break comparison with figures already in circulation.
"Shot" is not wrong for a measurement — it is only wrong for an API._

**On the workload this design claims — LongMemEval knowledge-update, haystack variant, ALL 70 questions**
(2026-08-29; it was a 25-question sample until then, and the right-hand column is what that sample said).
`clean` is the column that matters: the context holds the CURRENT fact and **not** the one it superseded,
which is what a reader's answer actually depends on. A context carrying both hands the model the
contradiction to resolve, which is the work this layer exists to do for it.

| arm | clean | current@k | stale@k | items/q | chars/q | `clean` at n=25 |
|---|---|---|---|---|---|---|
| `shot-1` | **31.4%** | 87.1% | 62.9% | 10.0 | **1,169** | 40.0% |
| `shot-2` | 28.6% | 88.6% | 65.7% | 19.4 | 5,236 | 36.0% |
| `shot-3` | 28.6% | 88.6% | 65.7% | 19.9 | 8,286 | 36.0% |
| `vector` | 10.0% | 81.4% | 88.6% | 10.0 | 10,387 | 16.0% |
| `vector-20` | 4.3% | 95.7% | 95.7% | 20.0 | 21,735 | 4.0% |

**One shot delivers a clean context 3.1× as often as cosine on one-ninth the characters** — 31.4% at 1,169
against 10.0% at 10,387. Cosine at k=20 reaches the current fact 95.7% of the time and reaches the
superseded one just as often, which is the failure mode a decay model exists to prevent, priced: it costs
21,735 characters to hand a reader both answers.

**The full sample moved the LEVEL down and the RATIO up, and both directions are worth stating.** Every <!-- result: id=lme-shots-clean-shot1-n25 arm="`shot-1` on the 25-question sample of the same haystack class" metric=clean n="25 questions" value="40.0%" ships=no status=RETRACTED -->
`clean` figure fell by 6–9 points against the 25-question sample, so that sample was optimistic and any
absolute taken from it was too high. But cosine fell further (16.0 → 10.0), so the multiple this design is
actually claimed on went **2.5× → 3.1×**. The shape is unchanged: shot 1 is the best `clean` context, extra
shots cost it, and shot 3 is indistinguishable from shot 2 on every column while adding 3,050 characters.

**But the shot curve ran the wrong way, and that was a real defect.** `clean` FELL as the walk went deeper <!-- result: id=lme-d98-floor-shots-n25 arm="`GraphMemoryOptions.ExpansionRetrievabilityFloor` (**D98**) on the shot curve, 25-question sample" metric=clean n="25 questions" value="+4.0" ships=no status=RETRACTED -->
while `stale@k` climbed, because `EdgeHalfLife` decays the EDGE and nothing consulted the ENTRY — so
expansion resurrected exactly what recall had buried. **D98** adds
`GraphMemoryOptions.ExpansionRetrievabilityFloor` and holds the curve flat at 40.0% / 40.0% / 40.0% with
`stale@k` back at 56.0%, for 4 points of `current@k`. **An ordering weight was tried first and measured
moving nothing** — ordering only matters when the caller's budget binds, and at 15.9 items against a budget
of 20 it did not.
<br>_Those floor figures were the **25-question** sample. **Re-run at 70 on 2026-08-30** — see the floor
sweep in the section below: the shape holds and both sides shrink, to +2.8 points of `clean` bought for 1.5
of `current@k`, where the sample read +4.0 for −4.0._

**The class where expansion actually PAYS, and it is the one that had no shot curve at all**
(`memory-longmemeval --shots --temporal --haystack`, all 132 questions, 2026-08-29). Temporal reasoning
usually needs EVERY flagged turn — "the first issue after the service" is unanswerable from the later fact
alone — so the metric is all-evidence recall, and the failure mode of a small first load is holding one turn
of two. That is precisely what a second shot is for:

| arm | all evidence@k | any evidence@k | evidence turns | items/q | chars/q |
|---|---|---|---|---|---|
| `shot-1` | 48.5% | 82.6% | 62.2% | 10.0 | **1,173** |
| `shot-2` | **53.0%** | 84.8% | 65.6% | 19.6 | 5,383 |
| `shot-3` | 53.0% | 84.8% | 65.6% | 19.9 | 8,114 |
| `vector` | 43.9% | 75.0% | 57.5% | 10.0 | 10,778 |
| `vector-20` | **65.2%** | 84.1% | 73.7% | 20.0 | 21,759 |

**Shot 2 is worth +4.5 points here, against +1.5 on LoCoMo and −2.8 on knowledge-update** — the only
workload measured where walking clearly buys something, and the mechanism is the one the class is built on.
**Shot 3 is worth exactly nothing**: identical on every column while adding 2,731 characters. That is the
third class in a row where the third shot is inert, so *"expand until the budget runs out"* is not the
lesson — *"expand once"* is.
<br>**Both figures in this paragraph are UNCAPPED, and the +4.5 does not survive an equal character budget**
— it reads 0.0 at 5,400 and −28.1 at 1,200 (the budget section below). Every row in this table lets each arm
spend what its slot count costs, so read them for that regime.

**The honest counterweight, and it is the same shape as LoCoMo's.** Size-matched cosine wins this column
outright: `vector-20` reaches 65.2% where three shots reach 53.0%, at 2.7× the characters. All-evidence
recall is an ARCHIVE metric, so a workload that wants every turn rewards keeping everything — exactly what
§5's LoCoMo discussion says about the archival axis, arriving here from a second direction.
<br>**"Size-matched" here means ITEM-matched, and matching CHARACTERS reverses it** (2026-09-06): at an
equal 5,400-character budget cosine reaches 37.1% where one shot reaches 47.7%. Which axis is the fair one
is the disagreement, not an error in either row — but item-matching is the weaker choice for a design whose
claim is that a headline costs less than a turn.

**The oracle variant overstates the gain by 2.7×**, which is Part 112's finding recurring on a third
question: on the oracle, shot 2 is worth **+12.2** (59.8% → 72.0%) rather than +4.5. Quote the haystack.

**The haystack has a reproducibility floor of ONE QUESTION on the graph arms, and it was measured rather
than assumed.** The identical run repeated reads `shot-1` 47.7% / `shot-2` 52.3% / `shot-3` 52.3% — every
graph arm exactly 0.8 points (one question in 132) below the table above, while **both vector arms are
byte-identical across both runs** and `any evidence@k` does not move at all. The graph arm fuses three
signals over ~490 candidates and so carries far more near-ties than a cosine top-10; the vector arms being
stable is what says this is the ranking's own sensitivity and not the harness, the sample (digest
`773FB41E0E5A`) or the embedder (64,905 misses both times).

**So read the LEVELS to about a point and the DELTAS as they stand.** Shot 2 is worth **+4.5 and +4.6** on
the two runs — six times the floor, and the finding — while the 0.0 between shots 2 and 3 sits inside it and
should be read as "no measurable gain", not as "exactly none". The ORACLE has no such floor: `shot-1` and
the plain `--temporal` arm agree there to the decimal (59.8% / 84.1% / 66.0%), which is what pinned this to
near-ties rather than to a code difference — the same two paths differed by one question on the haystack,
and the repeat then landed on the plain arm's own 47.7%.
<br>**That +4.5 is an UNCAPPED delta**, and it is 0.0 once every arm is held to the same characters (the
budget section below) — so "the finding" is that a second shot buys evidence, not that it buys evidence
worth its cost. **A third and fourth run on 2026-09-06 re-confirmed the floor from the other side**: both
read `shot-1` at 82.6% `any evidence@k` and 1,173 chars against this table's own figures, on the same digest
`773FB41E0E5A` and the same 64,905 embedder misses, while `all evidence@k` read 47.0% and 47.7% — one
question apart, in a pair of runs that differed only in a post-ranking cap that cannot touch `shot-1`.

**On a SEARCH workload the curve runs the other way, which is why the shot count is a question and not a
constant.** LoCoMo, 200 questions, evidence-hit. **Re-measured 2026-08-29 under per-question isolation**
(`docs/task-archive.md` Part 118); the right-hand column is what this table said before it:

| arm | evidence-hit | items/q | chars/q | ms/q | hit / 1k chars | pre-isolation |
|---|---|---|---|---|---|---|
| `shot-1` | 54.5% | 20.0 | 2,264 | 202.9 | **0.241** | 30.0% |
| `shot-2` | 56.0% | 35.9 | 4,246 | 224.3 | 0.132 | 36.0% |
| `shot-3` | **57.0%** | 39.9 | 4,855 | 243.5 | 0.117 | 36.5% |
| `vector` | 80.5% | 20.0 | 3,522 | 1.2 | 0.229 | 80.5% |
| `vector-40` | 86.0% | 40.0 | 6,885 | 1.1 | 0.125 | — |
| `full` | 100% | 590.1 | 98,886 | — | 0.010 | 100% |

**The multi-shot gain was mostly the contamination, and this is the correction that costs a published <!-- result: id=locomo-shots-shot2-shared-store arm="`shot-2` on LoCoMo, shared-store run (pre-isolation)" metric=evidence-hit@k n="200 questions" value="+6.0" ships=no status=RETRACTED -->
claim.** Isolated, shot 2 is worth **+1.5** and shot 3 another **+1.0** — a nearly flat curve — where the
shared-store run read +6.0 and +0.5. The mechanism is visible once stated: a shared store let earlier
questions' expansions reinforce elsewhere, which DEPRESSED shot 1, and later shots then recovered ground
that was never lost in an isolated run. So *"search wants two shots"* is **no longer supported by this
measurement**; see **D100**, amended.
<br>Two internal checks say the re-measurement is sound. `shot-1` reads 54.5%, the same figure the retrieval
ladder's `lyntai` arm reports from a wholly separate run — they are the same operation, so agreeing to the
tenth is the cross-check. And `vector` is byte-identical at 80.5% / 3,522 chars, as it must be.
<br>`ExpandSeeds` was ruled out as the constraint rather than assumed, and that survives isolation: at 20
seeds instead of 3 the three-shot arm finds the same share (65.4% both times on the 25-question control), so
the ceiling is what the graph is connected to rather than how many seeds are expanded.

**Three things in these tables are honest limits rather than results.** The `full` arm exceeds this reader's
window — measured by needle probe, a passcode at the top of the prompt survives 85,508 characters and does
not survive 109,908 — so its QA row is a floor. `ms/q` compares a SQLite-backed store against an in-memory
array with no persistence and no write-back, and it is cold-start dominated at one store per question;
`memory-scale`'s steady-state p50 is 10.4ms at 1k, and ~80% of that was the write-back when it was measured — where **D99** then cut a recall's co-activation from ten store round-trips to one and **D101** cut the whole write-back from three store calls to one, neither of them resolvable by the instrument (its 10k p50 spans 8.9–11.2ms across runs of identical code, so no latency claim is made for either). And the third limit is **FIXED as of
2026-08-29** rather than outstanding: LoCoMo questions within a conversation used to share a store, so a
recall reinforced what the next question read. Each question now runs against a private byte-copy of the
ingested store (`docs/task-archive.md` Part 118), and `vector` — which never touches the graph store — is
byte-identical across the change, which is what says the re-measurement moved the engine and not the
harness. **The effect was five times what filing it had suggested**: `shot-1` was reported moving 30.0% →
28.0%, and the isolated control puts it at 65.4% against 53.8% on the same pair of runs. Every LoCoMo
figure in this document is either re-measured or marked with the regime it was taken in.

### At an EQUAL CHARACTER BUDGET the walk wins both classes — and expansion stops paying entirely (`memory-longmemeval --shots --budget`, 2026-09-06) <!-- result: id=longmemeval-ku-shot1-b1200-n70 arm="`shot-1` (one-shot recall, shipped `k = 10`) capped at 1,200 characters by `MemoryQuery.CharBudget`" metric=clean n="70" value="31.4%" ships=no status=CURRENT -->

Every table above lets each arm spend whatever its slot count costs, so `chars/q` varies ~9× down a single
column. That is the wrong control for this design: all-evidence recall rewards whoever returned MORE, while
the claim being tested is that a recall returns **headlines** and pays for content only when asked (**D100**,
**D102**). `--budget N` caps every arm at the same characters by the engine's own `MemoryQuery.CharBudget`
rule — whole items, an item that does not fit skipped rather than ending the fill, never empty.

| workload / budget | best walk arm | cosine | walk − cosine |
|---|---|---|---|
| knowledge-update, haystack, 70q, 1,200 (`clean`) | **31.4%** (shot-1) | 14.3% | **+17.1** |
| temporal, haystack, 132q, 1,200 (all-evidence) | **47.0%** (shot-1) | 18.2% | **+28.8** |
| temporal, haystack, 132q, 5,400 (all-evidence) | **47.7%** (shot-1) | 37.1% | **+10.6** |

**1. The design's central claim survives its first equal-spend test, on both classes.** Uncapped, the
knowledge-update headline was 31.4% against cosine's 10.0% — a 3.1× ratio bought partly with a 9× character
advantage. Held to 1,200 characters each, cosine improves to 14.3% and the ratio falls to **2.2×**. It
narrows and it does not close, which is the honest version of the claim.

**2. Expansion never pays under a budget — not once, on either class, at either value.**

| arm | ku 1,200 | temporal 1,200 | temporal 5,400 |
|---|---|---|---|
| `shot-1` | **31.4%** | **47.0%** | **47.7%** |
| `shot-2` | 27.1% | 18.9% | 47.7% |
| `shot-3` | 27.1% | 18.9% | 45.5% |

The section above reports shot 2 worth **+4.5** on temporal, *"the only workload measured where walking
clearly buys something"*. At equal spend that is **0.0** at 5,400 and **−28.1** at 1,200. The mechanism is
in `items/q`: `MemoryWalkState.Hold` upgrades a held item from headline to full content **in place**, so
shot 2 swaps ~117-character headlines at the HEAD of the list for ~380–700-character bodies and the budget
drops the tail — 10.0 items become 3.9. Uncapped, the walk gains by holding more; under a fixed context it
is strictly a loss. *"Expand once"* becomes **"under a context budget, do not expand"**.

**3. The "honest counterweight" above flips when the matching axis changes.** It reads *"size-matched cosine
wins this column outright: `vector-20` reaches 65.2% where three shots reach 53.0%, at 2.7× the
characters"* — size-matched by ITEMS. Matched by CHARACTERS, cosine reaches 37.1% where one shot reaches
47.7%. Item-matching is the wrong axis for a design whose claim is that a headline is cheaper than a turn,
and both readings are kept here because the disagreement is the finding.

**Instrument.** `shot-1` on knowledge-update reproduced the published row byte-for-byte — 31.4% / 87.1% /
62.9% / 10.0 items / 1,169 chars — as it must, since 1,169 is under the cap. On temporal it read 47.0% and
47.7% across the two runs with `any evidence@k` identical at 82.6% in both, which is the one-question
reproducibility floor and its documented signature, not a new defect. The cap fired on 210/280, 396/528 and
307/528 bodies, so no table here is the uncapped one under a different heading.

**What this does NOT settle, and the first item is the one that could move the result.** `shot-1` spends
1,173 of the 5,400 it is allowed, so its row is what `k = 10` costs and **not the best one shot could do
with the room** — a `k`-raised arm that actually fills the budget is unmeasured and is the obvious next run.
Beyond that: one embedder (`nomic-embed-text`, Ollama-served), two budget values on one class and one on the
other, and `clean` is a metric that rewards small contexts by construction, which is visible in capped
cosine scoring *better* than uncapped (10.0% → 14.3%) while its `stale@k` falls 88.6% → 55.7%.
<br>**That run happened the next day and the caveat was the whole story** — see the section below. A recall
at `k = 80` trimmed to the SAME characters beats `shot-1` by 25.7 points of `clean` and by 20.5 points of
all-evidence recall. **Read every "the walk wins" sentence above as "the walk beats cosine", never as "the
walk is the best body"**, which is what the next section measures and refutes.

**One library gap, priced rather than assumed.** `MemoryWalkOptions` bounds items and not characters, and
`MemoryWalk.WalkAsync` passes `null` for each expansion's own `charBudget`, so a caller wanting an n-shot
walk inside a context budget cannot express it and must cut the body afterwards — which is what this arm
does. Whether that surface is worth adding is now answerable and the answer is *probably not*, though **not
for the reason given here on 2026-09-06**: this paragraph closed with *"the best body is shot 1, which needs
no walk-level budget at all"*, and the best body is not shot 1. The surface still earns nothing, because the
arm that wins does not walk — it is a deeper first recall, which `MemoryQuery` already expresses.

### The lever is RECALL DEPTH, not the walk — and it moves the two classes in opposite directions (`--shots --budget 1200,5400 --fill-k 80`, 2026-09-07) <!-- result: id=longmemeval-ku-fill-k80-b1200-n70 arm="`fill` (`k = 80`, engine's own `CharBudget` cut) at budget 1,200 — `Limit: 80, CharBudget: 1200`" metric=clean n="70" value="57.1%" ships=no status=CURRENT -->

The section above held every arm to the same characters and concluded the walk beats cosine. It could not
see the arm that was missing: **`shot-1` never spent its allowance** — 1,173 characters of 5,400, because
`k = 10` bound it and not the budget. `fill` is the shipped recall at `k = 80` with the engine's own
`CharBudget` doing the cut, so it spends the whole allowance on a first load. Both classes, haystack, full
samples, one ingestion per ladder.

| | temporal, all-evidence | | knowledge-update, `clean` | |
|---|---|---|---|---|
| **arm** | **@1,200** | **@5,400** | **@1,200** | **@5,400** |
| `shot-1` | 47.7% | 47.7% | 31.4% | 31.4% |
| `shot-2` | 18.9% | 47.7% | 27.1% | 28.6% |
| `fill` (`k = 80`) | 18.2% | **68.2%** | **57.1%** | 5.7% |
| `vector` | 18.2% | 37.1% | 14.3% | 17.1% |

**1. The same configuration is the BEST arm on one class and the WORST on the other, and the budget is what
flips it.** Depth with a wide output wins coverage (68.2%, +20.5 over the best walk arm); depth with a narrow
output wins suppression (57.1%, +25.7); each is catastrophic in the other corner (18.2% and 5.7%). **`Limit`
is doing two jobs** — it sets the candidate POOL and the output SIZE — and the two metrics want them split.

**2. On its flagship metric this is the best figure this repository has measured**: `clean` 31.4% → **57.1%**
at the same 1,175 characters, from a configuration that needs no new surface (`Limit: 80, CharBudget: 1200`).
The mechanism is in the other columns and is not a free lunch: a deeper pool crowds out BOTH facts, just the
superseded one much harder — `stale@k` 62.9% → **11.4%** against `current@k` 87.1% → 65.7%. **So a deployment
that needs the current fact FOUND may still prefer `shot-1`**, which retrieves it 21 points more often; the
one that needs a context it can trust wants the deep recall.

**3. It beats the archive arm on the archive's own metric.** `fill@5400` reaches 68.2% where the UNCAPPED
`vector-20` reaches 65.2% — at 5,358 characters against 21,759. The two sections above both concede that
size-matched cosine wins this class outright; it does not.

**Instrument.** `fill` spent its budget on every recall — `0/132` and `0/70` left unspent, `0` short of `k` —
so no row here is the oracle's degenerate "return the whole store". `shot-1@1200` and `shot-1@5400` are
identical to the decimal on both classes, which is the control on the ladder: they are one walk trimmed
twice. Every pre-existing arm reproduces the 2026-09-06 standalone runs cell for cell.

**What this does NOT settle, and it is the load-bearing caveat.** **`fill` moves two things at once** — at
`k = 80` the engine gathers `k × CandidateMultiplier` = 320 candidates AND returns up to 80, so "depth is the
lever" is a hypothesis, not the measurement. `GraphMemoryOptions.CandidateMultiplier` is the knob that widens
the pool at a FIXED output and it is unmeasured; running `Limit: 10, CandidateMultiplier: 32` against
`fill@1200` is what would separate them, and until it does, what is measured is *"recall at 80 then trim by
characters"*. Also: one embedder, two budget values, and `ms/q` is absent for `fill` in the run that produced <!-- result: id=longmemeval-fill-msq-defective arm="`fill` (`k = 80`) latency in the run that produced this table" metric=latency n="unstated" value="14.7 seconds" ships=no status=RETRACTED -->
this table — it billed the harness's re-ingestion to the arm and read 14.7 seconds in a column of
milliseconds. Fixed after the fact, so no latency claim is made for that arm here.

### It was the POOL, and `CandidateMultiplier = 4` costs 27 points of `clean` (`--pool 4,8,16,32`, 2026-09-07) <!-- result: id=longmemeval-ku-pool16-b1200-n70 arm="`pool-16` (`GraphMemoryOptions.CandidateMultiplier = 16` at a fixed `Limit: 10`), budget 1,200" metric=clean n="70" value="58.6%" ships=no status=CURRENT -->

The section above could not tell a wider POOL from a wider OUTPUT: `fill` raised `Limit` to 80, and the
engine gathers `Limit × CandidateMultiplier`, so it moved both. `GraphMemoryOptions.CandidateMultiplier`
separates them — it widens the pool at a FIXED output. Knowledge-update, haystack, all 70, budget 1,200.

| arm | `clean` | `current@k` | `stale@k` | items/q | ms/q |
|---|---|---|---|---|---|
| `shot-1` / `pool-4` (**shipped**) | 31.4% | 87.1% | 62.9% | 10.0 | 115.8 |
| `pool-8` | 42.9% | 77.1% | 42.9% | 10.0 | 123.8 |
| `pool-16` | **58.6%** | 67.1% | 12.9% | 10.0 | 129.1 |
| `pool-32` | 57.1% | 65.7% | 11.4% | 10.0 | 142.5 |
| `fill` (`k = 80`, mult 4) | 57.1% | 65.7% | 11.4% | 10.1 | 488.1 |

**1. The lever is POOL DEPTH, and the answer is exact rather than approximate.** `pool-32` reproduces `fill`
on every quality column while returning ten items from a `Limit: 10` recall — the two see the same 320
candidates, and the shipped RRF policy never reads `MemoryRankingContext.Limit`, so they rank identically.
Output size contributed nothing.

**2. The curve SATURATES at 16, and on THIS metric the shipped 4 is far below the knee**: +11.5 points to 8,
+15.7 more to 16, then nothing (58.6 → 57.1 is one question, inside this instrument's documented floor).
Cost is 23% of `ms/q`, not a new round trip. **Read "below the knee" as a statement about suppression
only** — the coverage ladder below inverts it, and the shipped 4 turns out to be that axis's other corner
rather than a value nobody checked.

**3. It is a SUPPRESSION dial, not a quality dial** — which is the whole of how to read it. `stale@k`
collapses 62.9% → 12.9% while `current@k` falls 87.1% → 67.1%: a deeper pool buries BOTH facts and the
superseded one much harder. `clean` rewards exactly that. **A deployment that needs the current fact FOUND
loses 20 points by moving this knob.**

**So NO DEFAULT MOVES on this, and the reason is a rule this repository already paid for.** `clean` is a
suppression metric; a coverage metric should punish the same knob. The 2026-09-07 budget run says so
directly — at a 1,200 budget the same depth reads **18.2%** on temporal all-evidence against `shot-1`'s
47.7% — so this is the `RetrievabilityWeight` shape again (**§5**, +5.5 search for −37.1 supersession), and
an arm that wins one workload owes the other a visit before anyone proposes it. **What is measured is a
TRADE with a knee at 16, not an improvement.** One class, one variant, one embedder, n = 70.

**Control.** `pool-4` is the shipped configuration reached by a different route and reproduces `shot-1` to
the decimal on every column — so the arm is measuring the multiplier and nothing else about how it is built.

### …and the coverage ladder INVERTS it, at almost exactly 1:1 (`--pool` on `--temporal`, 2026-09-07) <!-- result: id=longmemeval-temporal-pool4to16-delta arm="`CandidateMultiplier` 4 (shipped) → 16, on temporal" metric=all-evidence-recall n="unstated" value="−28.0" ships=no status=CURRENT -->

The same four rungs on the class that wants every flagged turn, same budget, same controls.

| multiplier | knowledge-update `clean` | temporal all-evidence |
|---|---|---|
| **4 (shipped)** | 31.4% | **47.7%** |
| 8 | 42.9% | 33.3% |
| 16 | **58.6%** | 19.7% |
| 32 | 57.1% | 18.2% |
| **Δ 4 → 16** | **+27.2** | **−28.0** |

**There is no free rung, and the exchange is close to 1:1 at every step** — 4 → 8 buys +11.5 of suppression
for −14.4 of coverage, 4 → 16 buys +27.2 for −28.0. So **the shipped `4` is not an unexamined default; it is
the COVERAGE END of a real axis**, and the section above's "far below the knee" is true of `clean` alone.
A deployment picks a corner here; no value dominates.

**It is a better-behaved trade than `RetrievabilityWeight`, and worth saying so.** That knob runs about 7:1
against (+5.5 search for −37.1 supersession, §5); this one is ~1:1, which makes it the more honest dial to
expose to a deployment that knows which way its own workload leans. Neither default moves on this.

**Both controls held on this class too**: `pool-4` reproduces `shot-1` to the decimal (47.7 / 82.6 / 61.8)
and `pool-32` reproduces `fill` exactly (18.2 / 62.1 / 35.9) — the same two identities as on
knowledge-update, from a wholly separate run. The 18.2% was **pre-registered** from the earlier `fill@1200`
figure and landed on it exactly.

### The SEARCH rung closes it: the shipped `4` wins two of three workloads (`memory-locomo --retrieval --arms +pool8,…`, 2026-09-07) <!-- result: id=locomo-pool4-shipped-n200 arm="shipped `CandidateMultiplier = 4` (the `lyntai` default arm, output fixed at 20)" metric=evidence-hit@k n="200" value="54.5%" ships=yes status=CURRENT -->

The third rung, on the workload the engine already loses. n = 200, evidence-hit@20, the sample every
retrieval ladder here uses.

| multiplier | knowledge-update `clean` | temporal all-evidence | **LoCoMo evidence-hit** |
|---|---|---|---|
| **4 (shipped)** | 31.4% | **47.7%** | **54.5%** |
| 8 | 42.9% | 33.3% | 50.0% |
| 16 | **58.6%** | 19.7% | 44.5% |
| 32 | 57.1% | 18.2% | 43.5% |
| **Δ 4 → 16** | **+27.2** | **−28.0** | **−10.0** |

**1. The shipped default is VINDICATED, which is a stronger result than "no default moves".** Raising the
multiplier buys suppression and costs BOTH coverage workloads; summed over the three at 4 → 16 it is
**−10.8**. Only the metric that rewards burying improves. A deployment whose workload is supersession-shaped
can still take 16 and know what it pays.

**2. It SHARPENS D59 rather than merely agreeing with it.** That entry established every LoCoMo miss as
*reachable-but-outranked* by replaying each query "wide open" — which lifted the **`Limit`**, and the engine
gathers `Limit × CandidateMultiplier`, so the replay widened the pool AND the output together. This ladder
holds the output fixed at 20 and still finds no gain at any rung, so the missed evidence is already **inside
the shipped 80-candidate pool** and widening it only adds competitors. **"Reachable" means reachable at the
SHIPPED pool**, which is the reading that makes D59's "a better formula is not the fix" argument bite.

**3. Three controls, all exact.** `lyntai` reproduces its published 54.5% and `vector` its 80.5% — the arm
that never touches the graph store did not move — and `items/q` is 20.0 on every arm, so no arm was filtered
before ranking rather than losing on it. **Pre-registered and both halves held**: monotone decreasing, and
smaller in magnitude than temporal's −28 because LoCoMo returns 20 slots rather than 10.

**What it does not settle.** One embedder, and the ladder is over the SHIPPED arm — `+sem+rel-only` is the
best mechanical arm on this workload (82.6%) and its pool is unmeasured, so this prices the knob on the
default configuration and not on the best one.

**That gap closed the same day, and it NARROWS the claim above** (n = 200, same controls):

| arm | evidence-hit | vs its own base |
|---|---|---|
| `+sem+rel-only` | **83.0%** | — |
| `+sem+rel-only+pool16` | 80.5% | **−2.5** |
| `+sem+rel-only+pool32` | 81.5% | −1.5 |
| `vector` | 80.5% | — |

**"A wider pool only adds competitors" is a property of the FUSED ranking, not of the pool.** On the shipped
arm — where relevance, retrievability and hop compete — widening costs **−10.0**. On `+sem+rel-only`, which
silences two of those votes and ranks on relevance alone, it costs **−2.5**, and 16 → 32 moves +1.0, which is
two questions and inside this workload's near-tie band. So on the arm a search deployment would actually run,
the knob is close to free rather than expensive.

**The mechanism is visible as an exact coincidence**: `+sem+rel-only+pool16` scores **80.5%**, which is
`vector`'s score to the decimal. A relevance-only ranking over a widening candidate pool converges on what
plain cosine already does — which is the same reading `+sem+rel-only`'s own definition invites, arriving from
the pool axis instead of the weight axis.

### The CEILING rises with the pool — and the gap to it widens faster (`+oracle+pool8/16`, 2026-09-07) <!-- result: id=locomo-oracle-pool16-ceiling arm="`+sem+rel-only+oracle+pool16` (×16 pool = 320 candidates, perfect judge)" metric=evidence-hit@k n="unstated" value="96.0%" ships=no status=CURRENT -->

`+sem+rel-only+oracle` is 92.5%, and at the shipped defaults that number cannot be interrogated: the judge's
`VerificationDepth` is `limit × 4` = 80 and the gathered pool is `limit × CandidateMultiplier` = 80, the
SAME 80, so the oracle already sees every candidate and 92.5% is exactly *"how much evidence reached the
pool"*. Raising the depth alone finds nothing. These arms widen both.

| pool | candidates | oracle ceiling | the real arm at that pool | gap |
|---|---|---|---|---|
| **×4 (shipped)** | 80 | **92.5%** | **83.0%** | 9.5 |
| ×8 | 160 | 94.0% | — | — |
| ×16 | 320 | **96.0%** | 80.5% | **15.5** |

**1. The ceiling IS raisable, and the constraint is pool size rather than retrievability.** It rises +1.5
then +2.0 as the pool doubles, with no knee over the measurable range. So the residual misses are reachable
material that was never gathered — not evidence the seed queries cannot find.

**2. And that is not a fix, because the two move in OPPOSITE directions.** Widening to ×16 raises the
ceiling 3.5 points and costs the real arm 2.5, so the gap between reachable and retrieved grows from **9.5
to 15.5**. Gathering more makes more answers *available* and the ranking *worse* at finding them.

**3. So the lever is RANKING, and the headroom is already there at the shipped pool.** 92.5% reachable
against 83.0% retrieved is 9.5 points sitting inside the candidate set the engine ALREADY gathers, and the
real 4B judge captures at most +1.0 of it (§5). That is **D59**'s conclusion — the defect is ranking, not
retrieval — quantified at every pool size instead of asserted at one.

**The instrument, and a degenerate result caught rather than published.** `+oracle+pool32` gathers 640 <!-- result: id=locomo-oracle-pool32-degenerate arm="`+oracle+pool32` (640 candidates, larger than the whole store for six of ten conversations)" metric=evidence-hit@k n="unstated" value="100.0%" ships=no status=RETRACTED -->
candidates from conversations of 369–689 turns — larger than the whole store for six of the ten — and scored
a flawless **100.0% in every category**, which a perfect judge handed an entire conversation cannot avoid.
It measures the FIXTURE. `WarnIfPoolSwallowsStore` now prints whenever an arm's pool reaches its store, and
reports zero for the ×8 and ×16 rungs above, which is what makes them legitimate. **This was the same defect
the LongMemEval bench had already grown a counter for** — a `fill` arm scoring 90% by returning most of a
25-turn store — and it recurred here because that counter lived in the other bench.

**What it does not say.** One workload, one embedder, and the oracle endorses by matching the same `dia_id`
token the metric scores on, so it is a reachability probe rather than a model of any judge's task. None of
these pools is shippable: the real judge gets LESS selective as depth grows (§5), so a wider pool hands a
weaker verdict a longer list.

### What `Fuse` costs a GOOD judge — the cell D105 decided without (`+oracle+fuse`, 2026-09-07) <!-- result: id=locomo-oracle-partition-vs-fuse arm="`Partition` (shipped) against `+oracle+fuse`, under a perfect (oracle) judge" metric=evidence-hit@k n="200" value="+2.0" ships=no status=CURRENT -->

**D105** kept `Partition` as the default having measured its cost only against a WEAK judge. The partition
promotes every endorsement ahead of the page, so for a judge that is always right it is the maximal rescue,
while `Fuse` only lets an endorsement COMPETE — so the question the decision needed and did not have is what
fusion costs at perfect judgement. LoCoMo, n = 200, same controls.

| judge | `Partition` (shipped) | `Fuse` | Partition − Fuse |
|---|---|---|---|
| **perfect (oracle)** | **92.5%** | 90.5% | **+2.0** |
| **real `gemma3:4b`** | 72.5% | **83.0%** | **−10.5** |

**1. The partition IS right for a good judge, and only just.** It buys 2.0 points at perfect judgement — so
the default is not merely inherited, it has a real (small) justification. Fusion loses them in
`temporal` and `open-domain`, the two categories where the oracle's rescue was largest.

**2. But the shipped default is a BET ON JUDGE QUALITY, and the odds are about 5:1 against it.** +2.0 when
the judge is perfect against −10.5 when it is a 4B local model — and −12.0 on a second embedder (§5). A
library whose own `docs/memory.md` §6 spends three criteria on choosing a small local model is defaulting
to the branch that punishes exactly that choice.

**3. What it does not settle, which is why no default moves here.** The weak-judge side replicates on two
embedders; **the oracle side is one run on one workload**, and D105's own objection stands — flipping it is
a silent reordering no consumer detects at compile time. What would justify the change is the same pair
measured on LongMemEval, where a verdict's effect on supersession is unmeasured entirely.

### The expansion floor, swept across workloads (2026-08-30) <!-- result: id=floor-08-knowledge-update-70q arm="`ExpansionRetrievabilityFloor = 0.8`, knowledge-update haystack (the default stays 0)" metric=clean n="70q (knowledge-update haystack); LoCoMo arm 200q" value="+2.8" ships=no status=CURRENT supersedes="lme-d98-floor-shots-n25" -->

`GraphMemoryOptions.ExpansionRetrievabilityFloor` (**D98**) ships at `0`. It was adopted on one class of one
variant at 25 questions, and its shipped XML doc quoted those figures. **Both workloads were re-measured on
2026-08-30, and the knob is a better deal than its own documentation said.**

It cannot be swept the cheap way. `K` above is scoreable offline from one candidate set because it only
re-ranks a fixed pool; the floor changes which neighbours are FETCHED, and expansion reinforces what it
walks, so every value needs its own run against its own store.

| workload | floor | headline | items/q | chars/q |
|---|---|---|---|---|
| knowledge-update, haystack, 70q | 0 | `clean` 31.4 / 28.6 / 28.6 | 10.0 / 19.4 / 19.9 | 1169 / 5236 / 8286 |
| knowledge-update, haystack, 70q | **0.8** | `clean` 31.4 / **31.4** / **30.0** | 10.0 / 18.4 / 19.0 | 1169 / 5122 / 8022 |
| LoCoMo, 200q | 0 | evidence-hit 54.5 / 56.0 / 57.0 | 20.0 / 35.9 / 39.9 | 2264 / 4246 / 4855 |
| LoCoMo, 200q | 0.5 | evidence-hit 54.5 / 56.0 / 57.0 | 20.0 / 35.9 / 39.9 | 2264 / 4246 / 4855 |
| LoCoMo, 200q | **0.8** | evidence-hit 54.5 / **55.5** / 57.0 | 20.0 / **29.4** / **36.6** | 2264 / **3515** / **4478** |

**The trade is roughly 2:1 in the floor's favour, not the 1:1 the doc sold.** On knowledge-update, 0.8 holds
the shot curve flat where it otherwise falls — **+2.8 points** of `clean` at shot 2 — and gives back **1.5**
points of `current@k`. The 25-question sample read that as +4.0 for −4.0, so BOTH sides shrank at full
sample and the cost shrank further. `stale@k` improves 2.8 points as well.

**On a SEARCH workload it is close to free.** LoCoMo loses one question of 200 at shot 2 and none at shot 3,
while cutting context 17% and 8% — lifting hit per 1k characters from 0.132 to 0.158. That runs against
D98's stated worry that the floor buys precision with recall; on the workload where recall is the whole
metric, it barely charges for it.

**0.5 is inert, and that is the useful part.** It is byte-identical to 0 on every column, so the floor does
not begin to bind until somewhere between 0.5 and 0.8. The reason is the store, not the questions: LoCoMo is
ingested fresh, so nothing has decayed below 0.5. On knowledge-update the `--ranks` diagnostic puts the
current fact at retrievability **0.8556** and the superseded one at **0.7206** — 0.8 sits between them,
which is why it separates the pair and why the value is a property of **how decayed a store is** rather than
of the workload. A deployment picking this number must read its own retrievability distribution; there is no
constant to adopt.

**The default did not move.** A knob that costs recall at all is one a deployment should opt into, and two
benchmarks are not every workload. What changed is the documentation, which overstated the cost by 2.7×.

**What this does not settle.** Two workloads, one embedder, two floor values above zero. The LoCoMo control
is exact — two floor-0 runs reproduced byte-identically, which is what makes a 0.5-point move there one real
question rather than noise — but no such repeat was taken on the haystack arm, whose own reproducibility
this document elsewhere puts at about one question.

### How these choices sit against the published field (surveyed 2026-08-29) <!-- result-free: A literature survey, self-declared: "A literature pass, not a measurement. Every claim below is attributed, because none of it was run here" (line 3206) — every figure cited (0.770 / 0.657 / 0.368) belongs to arXiv:2606.12945, not to this engine, and the section states there is still no comparable number here. -->

*A literature pass, not a measurement. Every claim below is attributed, because none of it was run here —
and no number in this document is comparable to a number in any of those papers, for the reason the last
point gives.*

**Where this engine is an outlier, and it is deliberate.** A 2026 survey of autonomous-agent memory
([arXiv:2603.07670](https://arxiv.org/html/2603.07670v1)) finds decay modelled with a curve at all in only
one surveyed system — MemoryBank, using the **Ebbinghaus exponential** — and reports no system using FSRS or
a power law. `DsrRetrievability` is FSRS's power law (**D49**), so the shipped default here is a form the
survey does not record anyone else shipping. The same survey lists principled forgetting as an open problem.

**Age as INTERFERENCE appears to have no counterpart at all.** That survey describes elapsed wall-clock time
throughout — MemGPT and Generative Agents both decay exponentially over elapsed time — and records nothing
measuring age in intervening writes. **D40** is therefore an unshared bet rather than a variant of a common
one, which cuts both ways: nobody else's results transfer to it, and its own results transfer to nobody.

**On importance scoring the field says yes, and the disagreement is narrower than it looks.** Park et al.'s
Generative Agents score `recency + importance + relevance` with all weights 1, and their ablation degrades
without importance; the survey calls it "a substantial improvement over pure cosine similarity" while noting
it risks "self-reinforcing error". **That is not the same measurement as ours**: they scored believability of
behaviour, this scores recall miss. The newest work is the closer comparison —
[arXiv:2606.12945](https://arxiv.org/abs/2606.12945) (LongMemEval) argues similarity and recency are *"both
mis-specified for the forgetting decision, which is made at consolidation time before the future query is
known"*, and replaces them with **seven** cognitively-grounded factors under learned weights, retaining
**0.770** of critical evidence against **0.657** for uniform weights and **0.368** for recency.
<br>**Read together with the salience result above, the two agree about the diagnosis and differ about the
cure.** A static, write-time, single-signal importance is what both find wanting; that paper's answer is more
factors with learned weights, while this engine currently ships one factor (novelty) at a fixed weight — and
measures it costing miss. **One of their seven factors is usage history, which this engine already records
and salience does not read** (`GraphMemoryOptions.LogReviews`, `Reinforce`, `MemoryReviewWrite.Verified`).
That is the cheapest available improvement and it needs no new seam: `IMemorySaliencePolicy` is where a
different value function plugs in, with no registration change (**D45**, **D47**).

**Burial over deletion aligns, and the forget/prune split is ahead of the surveyed norm.** The survey finds
deprioritization common but eviction-triggered, names "selective forgetting" an open challenge, and reports
only MemoryAgentBench testing forgetting explicitly. **D41** (burial, never deletion), **D72**'s
capability split and **D90**'s completeness invariant are stronger commitments than it records elsewhere.

**The honest gap, and it is the one that matters: there is still no COMPARABLE benchmark number.** The field
publishes against **LoCoMo**, **LongMemEval** and **BEAM**. Two of those have now run here — both above, both
in full — so the "we have never touched a shared suite" version of this gap is closed. What is not closed is
comparability: those figures are **model-free retrieval** metrics against a plain-cosine control on this
machine's embedder, while the field publishes end-to-end QA accuracy read by a frontier model, and the reader
sets that ceiling far more than the memory layer does. So nothing here can be ranked against Mem0, Zep or
Letta in either direction — including favourably — and closing that needs the QA half, which
`docs/task-archive.md` Part 233 carried.

**On CONSOLIDATION the field puts the abstraction at ENCODING, and this engine was looking for it at
retrieval** (surveyed 2026-08-31, for the gist tier). Supersession is not inferred anywhere that solves it:
**Zep/Graphiti** carry bi-temporal edges and *invalidate rather than delete* a superseded fact, and **Mem0**
has a model decide ADD/UPDATE/DELETE/NOOP as the fact arrives. Both use information only the WRITER has. The
survey above reports the problem otherwise unresolved and prescribes *"contradiction detection (flag
conflicts for resolution)"* — flag, not resolve — while naming *"reflection grounding: requiring the agent to
cite specific episodic evidence for each reflection"* as a mitigation with theoretical rather than empirical
adoption. **That is why every read-time rule inverted here**: the information distinguishing the regimes was
never recorded, which design §5.7.0's own invariant 4 already says (vacuous for the base engine, because a
`MemoryWrite` carries no valid-time).
<br>**The cognitive literature the word "gist" is borrowed from says the same thing and adds a warning.**
Fuzzy-trace theory's *parallel storage* principle holds that verbatim and gist are encoded in parallel and
names *"gist is extracted from verbatim memory"* a popular misconception; its *opponent processes* principle
holds that **gist supports false memory while verbatim suppresses it**, gist being the more durable trace. So
an abstraction that outlives its members is the documented mechanism of confabulation — which makes keeping
it tied to reachable members (**D41** buries, never deletes) the countermeasure rather than a nicety. Full
record and sources: the untracked `local/superpowers/specs/2026-08-31-gist-tier-field-research.md`.

### And WHAT salience measures, which is a different question (`memory-importance`, 2026-08-27) <!-- result: id=importance-novelty-critical-rare arm="shipped novelty salience policy at the shipped `SalienceWeight = 0` (vs a salience-off control and a perfect importance ORACLE)" metric=miss n="10 seeds × 4 shapes × 3 arms, `embeddinggemma`" value="0.710" ships=yes status=CURRENT -->

The sweep above prices how LOUD salience is. `node devtools/dev.mjs memory-importance` prices what it
MEASURES: the shipped novelty policy against a perfect importance ORACLE reading ground truth off the corpus,
with salience-off as control, at the shipped `SalienceWeight = 0` — so it is survival (decay resistance and
store admission) being measured, not ranking. 10 seeds × 4 shapes × 3 arms, `embeddinggemma`.

**Novelty is not importance, and on the classes that matter it is worse than nothing.** Critical-rare miss:
salience-off `0.667`, novelty `0.710`, oracle `0.474` on `diverse-noise`; `0.675` / `0.738` / `0.293` on
`rare-critical`. The shipped policy is monotone in "unlike anything already stored", so sustained significance
decays on that axis as it is confirmed while a one-off triviality reads as maximal — measured here as the
novelty arm losing to registering no salience policy at all.

**But the oracle's win is a REDISTRIBUTION, not an improvement** — the same shape `memory-enrichment` found
for similarity linking, running the other way. Against novelty, per shape (miss delta, negative = oracle
better):

| shape | critical-rare | attribute | topical | all (combined) |
|---|---|---|---|---|
| baseline | **−0.14** | −0.12 | +0.14 | −0.03 |
| `diverse-noise` | **−0.24** | −0.28 | +0.28 | −0.07 |
| `templated-noise` | **−0.11** | −0.11 | +0.09 | −0.03 |
| `rare-critical` | **−0.45** | −0.15 | +0.59 | +0.01 |

The aggregate barely moves because the classes cancel. **Store admission is zero-sum**: what importance
promotes displaces what it did not mark, and the displaced material is the frequently-queried working set.

**So no importance policy ships, and the seam already supports one.** Whether "protect the rare marked thing
at the working set's expense" is the right trade is a property of the deployment's corpus and of what its
users would rather lose — `generic-library` rule 7's test, which the library fails by construction. Write an
`IMemorySaliencePolicy`: it receives the whole `MemoryWrite`, so `Content` and a host-declared
`Metadata["importance"]` are both already available, and `SalienceContext` carries the engine's novelty
alongside for a policy that wants both.

**What it does not settle.** The oracle is a CEILING, not an accuracy — no real rater is this good, so a
strong result only says a rater is worth costing. Ranking is unswept. And the corpus has no
low-importance-but-sometimes-relevant class: its noise is never a right answer, so routine material is priced
as junk rather than as background, which is the softer and more common real case.

### Does a CORRECTION separate from a RECURRENCE? (`memory-density`, 2026-08-27) <!-- result: id=density-auc-correction-vs-recurrence arm="`SalienceContext.SimilarCount` on authored correction / recurrence / novel fixtures, `embeddinggemma-300M-Q8_0` (a refutation probe, not a shipped default)" metric=separability-auc n="5 recurrence / 5 correction observations pooled across five scripts (EFFECTIVE n of 1); 120 embed calls" value="AUC = 1.000" ships=no status=CURRENT -->

Not a measurement of a shipped default — the cheapest available refutation of an idea that has not been
built. `SalienceContext.SimilarCount` counts the stored entries that actually RESEMBLE a write: the probe's
neighbours at or above `MinSimilarity`. A design under consideration would promote a write resembling MANY
stored entries and leave alone one resembling exactly one, on the grounds that the first is an instance of an
established pattern and the second is a correction. `node devtools/dev.mjs memory-density` asks only whether
those two are distinguishable on that count at all — if they are not, nothing downstream of it can work.

Authored fixtures rather than `MemoryCorpus`: a separability test needs "which population is this write from"
and never a per-query ground truth, and the corpus has no correction class. Three populations, one probe
each, every store padded to the same 10 entries from one shared distractor pool so the STORE cannot be what
sets the count's ceiling. **It refuses to run without a real embedder** and exits rather than substituting a
double, for the reason `memory-enrichment` established: a correction shares nearly every word with the fact
it corrects, so a bag-of-words fake rates it maximally similar by construction and would print a plausible
table measuring word overlap. Measured against `embeddinggemma-300M-Q8_0` over a local OpenAI-shaped
`/v1/embeddings` endpoint — 120 embed calls, 2.0 s.

| population | mean `SimilarCount` | min | max |
|---|---|---|---|
| `correction` | 1.00 | 1 | 1 |
| `recurrence` | **6.00** | 6 | 6 |
| `novel` | 0.00 | 0 | 0 |

**Identical in all five writing systems** — English, Chinese, Japanese, Korean and mixed-script Chinese return
those same three numbers, with the store-size control reporting `ComparableCount` equal at 6 in every cell.
Pooled: **AUC = 1.000** over 5 recurrence / 5 correction observations, best threshold `SimilarCount >= 6` at
sensitivity 1.00 and specificity 1.00. Those languages are TRANSLATIONS of one fixture pair (**D55**), so read
it as a robustness check across five scripts at an EFFECTIVE n of 1, never as five times the evidence — and
the English-only run withholds the verdict outright, because at one observation per population AUC can only
read 0, 0.5 or 1.

**Two of those numbers are ARTIFACTS of the instrument, and taking either for a finding would set a default <!-- result: id=density-recurrence-count-artifact arm="`recurrence` population mean `SimilarCount` = 6, and the 'best threshold' derived from it" metric=similar-count n="one probe per population, store padded to 10 entries" value="6.00" ships=no status=RETRACTED -->
wrong.**

- **`recurrence` = 6 is the search WINDOW, not a cluster size.** The probe asks for `SimilarityK + 1` = 6
  neighbours, so the count saturates there — the same 6 the control reports for `ComparableCount`. The
  separation is real; its MAGNITUDE is not interpretable, and nothing here can tell "resembles 6" from
  "resembles 300".
- **The best threshold `>= 6` is that saturation point, not a learned boundary.** It is exactly
  `SimilarityK + 1`, so the rule it implies is *promote only when the probe window is ENTIRELY full of similar
  things* — materially stricter than "promote when density is high", and it makes `SimilarityK` the de-facto
  promotion knob rather than any threshold option. **No default is set from this run**;
  `memory-salience-weight`'s precedent is that a second embedder refuted the first run's reading.

**What it does not settle, which is most of it.** AUC 1.000 on authored fixtures with topically distant
distractors is a FLOOR, not evidence about a real corpus — the honest claim is that the mechanism is not
broken and the fixtures are clean. `SimilarityK` (5) and `MinSimilarity` (0.6) bound and define the count and
are both unmeasured — "a starting point, not a tuned value", by their own docs — so a ceiling effect and a
floor effect are both live, and the threshold landing exactly on the window width is what makes sweeping
`SimilarityK` the next question rather than a footnote. The four non-English fixture sets are one author's
best-effort text, unreviewed by a native speaker, and four of the five arms feed the pooled AUC.

### Which regime a generalisation should assert (`memory-support`, 2026-08-28) <!-- result: id=support-sum-pacing-inversion-n600 arm="`sum` — Σ r(m) — over an unbuilt gist tier, under injected `bulk` (100 ms/write) vs `spaced` (10 s/write) clocks" metric=regime-picks n="600 replays = 60 shapes × 5 seeds × 2 injected clocks" value="phase A 300/300 (bulk) vs phase B 300/300 (spaced)" ships=no status=CURRENT -->

`node devtools/dev.mjs memory-support` is the instrument for a tier that is **not built**: a "gist" over
recurring entries has to say which pattern it asserts when the recurring material has two regimes — an older,
larger one and a newer, smaller one. **D94** records what it settled about that tier's shape (support is two
quantities, so no policy seam ships); this is what it measured.

600 replays = 60 shapes × 5 seeds × 2 **injected** clocks. `bulk` steps 100 ms per write, inside
`BurstDampenedAgePolicy`'s own 5-second window, so the whole import arbitrates within one burst; `spaced`
steps 10 s, outside it, so every write starts its own burst. **No wall clock is read anywhere**, which is what
makes bulk ingest a modelled regime rather than a fact about how fast the host ran. Every shape carries
`RoutineCount = 12`, so phase A has 8 members and phase B has 4. The corpus is generated twice — once
declaring the recent regime correct and once the standing one — so a rule that merely tracks recency is
refutable instead of automatically right.

| rule | bulk (100 ms/write) | spaced (10 s/write) |
|---|---|---|
| `sum` — Σ r(m) | phase A, 300/300 | **phase B, 300/300** |
| `mean` — Σ r(m)/n | **NOT A RESULT** — phase B, 300/300 | **NOT A RESULT** — phase B, 300/300 |
| `count@θ`, θ = 0.1 | phase A, 300/300 | phase A, 300/300 |
| `count@θ`, θ = 0.2 | phase A, 300/300 | phase B 250 · A 50 |
| `count@θ`, θ = 0.3 … 0.6 | phase A, 300/300 | phase B, 300/300 |
| `count@θ`, θ = 0.7 | phase A 290 · tie 10 | phase B, 300/300 |
| `count@θ`, θ = 0.8 | **tie 115** · B 125 · A 60 | phase B, 300/300 |
| `count@θ`, θ = 0.9 | phase B, 300/300 | phase B, 300/300 |

**The `mean` row is marked because it is entailed by the fixture rather than measured** — see below before
quoting either cell. Every other row is a measurement.

**`sum` INVERTS with pacing, and that is the headline.** It picks the older regime under bulk and the newer
one under spaced, 300/300 each way — so it cannot ship as *the* rule unless a deployment's write pacing is
part of its contract. **And the only pacing-independent `count@θ` thresholds are the DEGENERATE ones**: <!-- result: id=support-theta09-invariant-n600 arm="`count@θ`, θ = 0.9 read as one of the two pacing-INDEPENDENT (degenerate) thresholds at `RoutineCount = 12`" metric=regime-picks n="600 replays = 60 shapes × 5 seeds × 2 injected clocks, `RoutineCount = 12` only" value="phase B 300/300 on both clocks" ships=no status=RETRACTED -->
θ = 0.1 sits below phase A's floor (min r(A) = 0.602 bulk / 0.102 spaced), while θ = 0.9 sits high INSIDE its
bulk band (max r(A) = 0.942, or 0.903 under `ConnectionBoost = 0` — the table below) — high enough that at
most 3 of phase A's 8 members clear it, against phase B's constant 4. Each therefore answers the same regime
on all 600 replays, and a constant is right on one answer arm and wrong on the other, which is what makes it
useless as a rule (θ = 0.1 scores 0.000 on the recent arm, θ = 0.9 scores 0.000 on the standing arm). **Every θ that could DISCRIMINATE inverts with pacing**: the transition sits between
0.7 and 0.9 under bulk and between 0.1 and 0.3 under spaced.
<br>θ = 0.1 is also the RAW count on this grid — every one of the 12 members clears it on both clocks
(min r(B) = 0.983 / 0.830) — so `count@0.1` returns (8, 4) on every replay, and it inherits raw's
pacing-independence together with raw's wrongness for the assistant host. That equivalence is MEASURED here
rather than structural: the spaced floor sits at 0.102 against a threshold of 0.1. **D94** carries what the
pair of them means for the seam.
<br>**The two degeneracies are not the same KIND of degenerate**, and the difference is what the next sweep
turns on: θ = 0.1 is cardinality-INVARIANT — every member clears it, so `count@0.1` is exactly (|A|, |B|) at
any size — while θ = 0.9 is an ORDER STATISTIC, (≤ 3, 4) here, that flips the moment |A| grows enough for a
4th member to clear it. Cardinality is the axis the gist tier turned on (`docs/task-archive.md` Part 153), so the invariance at 0.1 must
not be read into 0.9.

**`mean` is not tested by this table, and reading its two `phase B` cells as a result would be wrong.** At the
snapshot, phase B has never been recalled and was written immediately before, so it sits at the retrievability
ceiling:

| clock | curve | phase A (2400 members) | phase B (1200 members) | every B ≥ every A |
|---|---|---|---|---|
| bulk | default | min 0.602 · max **0.942** | min **0.983** · max 1.000 | 300/300 |
| bulk | `ConnectionBoost = 0` | min 0.602 · max **0.903** | min **0.983** · max 1.000 | 300/300 |
| spaced | default | min 0.102 · max **0.258** | min **0.830** · max 1.000 | 300/300 |
| spaced | `ConnectionBoost = 0` | min 0.102 · max **0.215** | min **0.830** · max 1.000 | 300/300 |

Retrievability is capped at 1, so `mean(B) ≥ mean(A)` follows **by definition** — a one-line theorem about the
fixture that 600 replays did not test. Testing `mean` needs a corpus where phase B is off the ceiling at the
snapshot. `sum` and `count` are untouched by that argument because they do not normalize by size: eight
members can outweigh four even when every one of them reads lower.

**The `ConnectionBoost = 0` control moved no verdict, and it is not vacuous.** Turning the term off shifts
`max r(A)` on both clocks (the table above; the spaced move, 0.043, is the larger) while `min r(A)` moves on
neither — the boost lifts the most-connected members and leaves the least-connected alone, which is what a
connection term should do. So **none of the finding is the graph's contribution to the rule's INPUT**.
<br>**That is the whole of what the control establishes, and it is narrower than "D54's feedback class is
excluded".** It re-reads the *same stored states* with the term off, so it removes the connection term from
the rule's input and does not re-rank. A member's stability recording that it once landed in a recall's top
five — the ranker's own contribution to stored `Stability`, which is what **D54** names — would need a second
600-replay run with the control curve *inside* `GraphMemoryEngine`. **That run was not done**, so the
engine-side half is untested rather than cleared.

**A model in the loop bought nothing here.** `ggml-org/gemma-3-4b-it-GGUF`, served by `llama-server` on a
local OpenAI-shaped endpoint, answered 300 counterbalanced pairs (600 calls) with **zero order
disagreements** and returned exactly the recency reading, agreeing with `mean` on all 300. Scope that: **the
prompt NAMES the recency ordering** ("more recent" / "older"), so a model obeying the label scores the same
without reading an entry, and counterbalancing rules out position bias, not label-following.

**The ladder that selected that single rung is the more transferable result** — `node devtools/dev.mjs
memory-support --screen`, measured 2026-08-28 through `llama-server` for every rung so the transport is held
constant, on `RoutineCount = 12`, seed 12345, all four rungs run and none skipped. **The command screens ONE
rung per invocation** — it resolves a single model from `LYNTAI_LIVE_CHAT_MODEL` and prints one verdict line
— so the table below is four separate runs against four `llama-server` instances rather than one command's
output:

| rung | A-first | B-first | verdict |
|---|---|---|---|
| gemma-3 270m it | earlier | later | pure "answer option 1" bias — **out** |
| gemma-3 1b it | later | earlier | pure "answer option 2" bias — **out** |
| **gemma-3 4b it** (reference) | later | later | content-driven — **survives** |
| Llama-3.2 1B Instruct Q4_K_M (control, size held) | earlier | later | same direction as the 270m rung — **out** |

**The generation control did not fire.** Both 1B-class rungs fail by pure position bias, each in its own
direction, so **the floor for this shape sits strictly between 1B and 4B parameters rather than at a family
boundary within 1B**. Counterbalancing is what caught it: gemma-3 1b was correct in the A-first order alone.
One shape, one machine, and a 4B ceiling — a flat ladder here would say small is ENOUGH for this task, never
that larger would not have been better.

**The limit that mattered most is now CLOSED — cardinality was swept on 2026-08-28.** See the next
subsection. The rest still stand: English throughout, `RecallLimit = 10` unswept, and the
saturation is structural rather than a sample-size artefact. **THREE cells in the table above are not
unanimous** — bulk θ = 0.7 (A 290 · tie 10), bulk θ = 0.8 (tie 115 · B 125 · A 60) and spaced θ = 0.2
(B 250 · A 50) — and the mechanism is that phase B contributes a constant 4, so a cell is unanimous unless θ
lands inside phase A's narrow band between its 4th- and 5th-largest member. Only the two bulk cells are also
unstable across SEEDS (θ = 0.7 at 55/60 shapes, θ = 0.8 at 40/60); the spaced θ = 0.2 split is seed-stable and
divides the shape grid instead, which is a different failure of unanimity that reads identically in a picks
column.

#### Cardinality: the axis that run held constant (2026-08-28) <!-- result: id=support-cardinality-theta09-n2400 arm="`count@0.9` across `RoutineCount` rungs 3 / 5 / 8 / 12 (bulk, default curve)" metric=regime-picks n="2400 replays = 60 shapes × 4 `RoutineCount` rungs × 5 seeds × 2 injected clocks" value="tie 185 → A 175 → B 160 → B 300 (of 300)" ships=no status=CURRENT supersedes="support-theta09-invariant-n600" -->

`node devtools/dev.mjs memory-support`, **2400 replays** = 60 shapes × **4 `RoutineCount` rungs** × 5 seeds ×
2 injected clocks. All seven controls held on 2400/2400. The rungs are 3, 5, 8 and 12, giving |A|/|B| of
2.00, 4.00, 3.00 and 2.00 — **the two ratio-2.00 rungs sit at different SIZES deliberately**, so a result
that moves across the axis can be attributed to the RATIO rather than to |A| simply growing.

| bulk / default | k=3 (2/1) | k=5 (4/1) | k=8 (6/2) | k=12 (8/4) |
|---|---|---|---|---|
| `sum` | A 300/300 | A 300/300 | A 300/300 | A 300/300 |
| `mean` | *NOT A RESULT* | *NOT A RESULT* | *NOT A RESULT* | *NOT A RESULT* |
| `count@0.1` | A 300/300 | A 300/300 | A 300/300 | A 300/300 |
| `count@0.7` | A 300/300 | A 300/300 | A 300/300 | A 290/300 |
| `count@0.8` | A 295/300 | A 300/300 | A 278/300 | **B 125/300** |
| `count@0.9` | **tie 185/300** | **A 175/300** | **B 160/300** | B 300/300 |

**The headline: θ = 0.9's pacing-independence was an ARTEFACT of `RoutineCount = 12`.** The 600-replay run
named θ = 0.1 and θ = 0.9 as the two degenerate thresholds answering one regime on every replay; cardinality
splits them. **θ = 0.1 is genuinely invariant** — phase A on all 2400 replays, both clocks, both curves —
because every member clears it, so it is exactly (|A|, |B|) and |A| > |B| holds by construction. **θ = 0.9 is
not**: it walks tie → A → B → B across the ratio. That is the order-statistic behaviour the earlier run
predicted and could not test, now measured.

**`count@0.8` flips too, and it re-reads a cell the earlier run called merely non-unanimous.** Its
`tie 115 · B 125 · A 60` at k=12 is not sampling noise: at every smaller rung the same threshold answers A
almost unanimously, so k=12 sits on a boundary this axis walks straight through.

**The mechanism is the connection term, isolated by the control**: under `ConnectionBoost = 0`, θ = 0.9 is
B 300/300 at EVERY rung on both clocks. The flip exists only with the boost on, which lifts phase A's
most-connected members over 0.9 once A is large enough to have any.

**`sum` stays pacing-dependent and is now also mildly cardinality-sensitive** — spaced/default reads
B 280/300 at k=5 against 300/300 at every other rung. **`mean` is still untestable here**, for the ceiling
reason above; its four identical cells are a property of the fixture, not a measurement.

**So no threshold is BOTH pacing-independent and cardinality-independent, and the one invariant threshold is
the raw count.** θ = 0.1 survives both axes and is the audit reading, which this corpus declares wrong for
the assistant host. **D94** refused a support seam on the argument; this closes the measurement question it
left open, and it closes it negatively — there is no constant θ to adopt.

#### `mean` was never a candidate — the FIXTURE was holding it still (2026-08-30) <!-- result: id=support-mean-settle-bulk-n2400 arm="`mean` — Σ r(m)/n — under `bulk` with `CorpusShape.RoutineSettleWrites` (`--settle` 0 … 480)" metric=regime-picks n="2400 replays per settle value (~20 s each)" value="B 300/300 at settle 0 → A 300/300 at settle 240" ships=no status=CURRENT -->

Both runs above marked `mean` *NOT A RESULT*: phase B is judged the instant it stops being written, so it
sits at the retrievability ceiling, every B member dominates every A member on 100% of replays, and
`mean(B) >= mean(A)` follows from that domination rather than from a rule. `CorpusShape.RoutineSettleWrites`
(`--settle N`) interposes filler writes between phase B's last write and the query that judges it, which is
what finally tests it. `node devtools/dev.mjs memory-support --skip-model --settle N`, 2400 replays per
value, ~20s each.

**The gap works on ONE clock, which is itself the first result.** Domination (every B member ≥ every A
member), per clock, default curve:

| settle | 0 | 30 | 60 | 120 | 240 | 480 |
|---|---|---|---|---|---|---|
| `bulk` | 1200/1200 | 106/1200 | **0/1200** | 0/1200 | 0/1200 | 0/1200 |
| `spaced` | 1200/1200 | 1200/1200 | 1200/1200 | 1200/1200 | 1200/1200 | 1200/1200 |

Under `bulk` phase A sits at 0.60–0.94 and phase B decays into that band by 60 writes. Under `spaced` phase A
is already at 0.10–0.26, so no gap tried brings B down to it — **`mean` stays untestable on that clock at
every value swept**, and the tables above remain the last word there.

**Where it IS testable, `mean` inverts with the gap** (`bulk`, default curve, picks per `RoutineCount` rung):

| settle | k=3 | k=5 | k=8 | k=12 |
|---|---|---|---|---|
| 0 | B 300/300 | B 300/300 | B 300/300 | B 300/300 |
| 60 | A 290/300 | A 267/300 | A 174/300 | B 210/300 |
| 120 | A 300/300 | A 300/300 | A 290/300 | A 265/300 |
| 240 | A 300/300 | A 300/300 | A 300/300 | A 300/300 |

It walks from "always B" to "always A" as the gap grows, and passes through a **cardinality-dependent** band
on the way — at settle 60 it answers A/A/A/B across the rungs under the default curve and A/A/B/B under
`ConnectionBoost = 0`, so it is sensitive to both new axes and the transition is not a graph-term artefact.
At settle 120 under `bulk`, **every rule in the table answers phase A**, so all of them are wrong on the
declared `Recent` answer; under `spaced` at the same gap, θ ≥ 0.3 degenerates to `tie 300/300` because both
regimes have decayed below the threshold. All pass/fail controls held on every run, and `--settle 0`
reproduces the published cardinality table cell for cell.

**So the negative result is now COMPLETE rather than partial.** `sum` inverts with pacing, discriminating
`count@θ` inverts with pacing, `count@0.8`/`count@0.9` invert with cardinality, and `mean` — the one arm
never tested — inverts with recency-of-the-newer-regime. Every combining form over member retrievability is
a readout of the corpus's own timing rather than of support, which is what normalizing by size predicts:
mean retrievability is monotone in recency and discards the support count entirely.
<br>**Of the gist tier's three candidates this leaves the third** (`docs/task-archive.md` Part 153). Testing `mean` was the second, and
it killed it; a rule with no constant threshold was the first, and `sum` and `mean` are both exactly that and
both invert. What survives is **a tier that reports N and declines to select a regime** — no combining form
here is invariant to axes a deployment does not control.

**An instrument lesson, because it nearly hid the finding.** The pooled `ConnectionBoost = 0` control reports
*"verdict did NOT move — every rule selects the same regime under both curves"* on both clocks. That is true
of the ARGMAX while the per-rung distributions differ completely (θ = 0.9 default: tie/A/B/B; boost off:
B/B/B/B). **A control comparing pooled verdicts cannot see a split that cancels in the pool** — which is why
the per-rung table is printed per clock AND per curve rather than summarised.

### Reinforcement: the signal, not the quantity <!-- result: id=reinforce-expansion-only arm="reinforce on **expansion only** (the shipped default is `All` — the `both` row)" metric=miss n="unstated" value="0.4429" ships=no status=CURRENT -->

Reinforcement does two separable things — resets the entry's **age**, and grows its **stability** — and they
pull in opposite directions (**D57**). Conditioning it on the act a caller *paid for* beats reinforcing
whatever the ranker returned:

| act | miss | pollution |
|---|---|---|
| both (default) | 0.5786 | 0.1878 |
| recall only | 0.5714 | 0.1878 |
| **expansion only** | **0.4429** | **0.1056** |
| neither | 0.4500 | 0.4118 |

Expansion-only beats reinforcing *nothing* too, which refutes the earlier reading that less reinforcement is
simply better: **the damage was the signal, not the quantity** (**D58**). The default stays `All` because an
application that never expands would otherwise reinforce nothing at all.

### Salience does not preferentially preserve junk <!-- result: id=salience-junk-isolated-effect arm="shipped novelty-driven salience policy, isolated salience effect on junk that can reach a recall (`NeutralSaliencePolicy` is the opt-out)" metric=miss n="unstated (the `many-candidates` cost is a single-seed replay)" value="−0.0786" ships=yes status=CURRENT -->

A standing concern held that a novelty-driven salience policy would preserve random junk. It does not:
isolated salience effect **−0.0786 miss / −0.0924 pollution** on junk that can reach a recall, and **+0.0000**
on textually diverse junk. The reason is the interesting part — **the two properties the concern depends on
are in tension**: pollution requires the junk to be *retrievable*, and junk diverse enough to maximise
novelty matches nothing (`docs/task-archive.md` Part 69).

Its one measured cost is `many-candidates` (40 competitors): miss +0.0808, pollution +0.1532 on a
single-seed replay. `NeutralSaliencePolicy` is the one-line opt-out.

### Multilingual <!-- result-free: Defines the multilingual corpus axis (D55) — five structurally identical arms, the single `SearchTerms` tokenization path, and why the four non-English arms differ. It publishes no recall-quality figure and adopts nothing ("The sweep adopts nothing: the language is the consumer's, not a setting"); the per-language numbers live in the salience/language sections above. -->

Every recall-quality figure published before 2026-08-12 was English. The corpus now replays **structurally
identical** timelines in English, Chinese, Japanese, Korean and mixed-script Chinese — same steps, same ids,
same ground truth, only the text differs — so a gap is the language and not the timeline (**D55**).

Tokenization is one path for every backend (`SearchTerms`): whitespace tokens, then per-script runs, with
spaceless scripts expanded into character n-grams. Thai, Lao, Khmer, Burmese and Tibetan discriminate under
3-grams, measured against Han as the reference.

`node devtools/dev.mjs memory-language` replays that corpus over **FIVE arms — English / Chinese / Japanese
/ Korean / `ChineseMixed`** (the roster is `Enum.GetValues<CorpusLanguage>()`, so it is whatever that enum
declares). `MemoryCorpus` takes `CorpusShape.Language`, default `English` and **byte-identical when unset**
— proved by goldens captured BEFORE the axis existed, which did not move when it landed. The sweep adopts
nothing: the language is the consumer's, not a setting. `MemoryCorpusGoldenTests`
pins **eight** golden shapes in all: the sixth added for the routine class, the seventh for its STANDING
answer arm and the eighth for its SETTLE gap, all captured after the axis and so pinning only their own
shape. (Stated here since 2026-09-15 — the count had lived only in the frozen design record, which became a
HISTORICAL document that no gate scans, so the number it held stopped being checked.)

**The four non-English arms are not interchangeable, and that is the point of having four.** Chinese is a
spaceless run of Han characters. Japanese is a spaceless run MIXING kanji/hiragana/katakana, where kana's
small inventory makes trigram collisions likelier. **Korean WRITES SPACES** and is expanded anyway because
Hangul sits in `SearchTerms`' spaceless range — defensible only because Korean is agglutinative (배우자는 /
배우자의 share the stem), so it is the arm where the expansion would first cost more than it recovers;
`CorpusLexicon.WritesWordSpaces` is what keeps that difference assertable instead of assumed.
**`ChineseMixed` is the fifth and the one closest to real deployment** — Chinese technical prose with
English terms embedded WITHOUT spaces (`部署pipeline`), which is where a Latin word inside a CJK run used to
be shredded into fragments that are words in no language. Every other arm is monolingual prose plus ASCII
ids, so it exercises the script boundary only at a token edge; this one puts it mid-run, where the defect
lived.

### The three SINGLE-SESSION classes: "expand once" holds a sixth time, and one class is flat outright (`memory-longmemeval --class … --shots --haystack`, 2026-09-11) <!-- result: id=longmemeval-single-session-user-shot2-haystack arm="`shot-2` on the `--haystack` variant, `single-session-user` (32,090 turns per arm)" metric=all-evidence-recall n="64 of 70 single-session-user questions" value="+0.0" ships=no status=CURRENT -->

The last three LongMemEval classes to get a curve, and the ones the class list had no switch for at all
until this run (`--class`, added with them). Evidence sits inside ONE session **100%** of the time here,
which is what makes knowledge-update's preference metric structurally inapplicable and leaves temporal's
all-evidence recall — the same reasoning `multi-session` was settled by. All three need `--haystack`: on the
oracle the store is comparable to the page, so a curve is flat by construction rather than by finding.

**Instrument.** Three runs, 3,141.2s total, seed 20260829, embedder `nomic-embed-text` served by
`llama-server` on a dedicated port, model-free scoring. Raw output, gitignored:
`devtools/_lme-single-session.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->.

| class | n | shot-1 | shot-2 | shot-3 | `vector` | `vector-20` |
|---|---:|---:|---:|---:|---:|---:|
| `single-session-user` | 64 of 70 | 82.8% | **82.8%** | 82.8% | 90.6% | 95.3% |
| `single-session-assistant` | 56 | 85.7% | **89.3%** | 89.3% | 98.2% | 100.0% |
| `single-session-preference` | 30 | 23.3% | **30.0%** | 30.0% | 73.3% | 80.0% |

**1. Shot 3 is worth EXACTLY ZERO on all three**, so *expand once* now holds on six classes rather than
four. Whatever a walk buys, it buys at the second step.

**2. `single-session-user` is FLAT — the first class where expansion buys nothing at all.** Not a small
gain: 82.8% at every shot, while `items/q` goes 10.0 → 19.8 and `chars/q` goes 1,163 → 8,075. The walk
returns seven times the material and finds no additional evidence, which is a sharper negative than a
diminishing return and is what a class with all its evidence in one session should do.

**3. The class the item expected to be UNMEASURABLE is the one that moved most cleanly.**
`single-session-assistant` was predicted flat even here, because 63% of its questions fit inside `k = 10`
on the oracle. On the haystack it reads 85.7 → 89.3. The prediction was right about the oracle and the
haystack is what fixed it.

**4. Plain cosine wins all three, and `single-session-preference` is the widest gap this record holds** —
30.0% against 73.3% at the same `k`, a **−43.3** deficit, and 43.3% against 90.0% on `any evidence@k`.
Preference questions are where this engine is furthest behind a flat retriever. The same shape appears on
LoCoMo, in `temporal-reasoning` and in `multi-session`, so it is the rule rather than the exception: an
all-evidence metric rewards keeping everything and burying is what this engine is for.

**What it does not say.** One embedder, model-free scoring, no reader, and no judge or reranker in the
loop — the seam measured elsewhere as worth more than any ranking constant is absent from every cell here.
The `chars/q` columns are not size-matched: the engine returns HEADLINES and `vector` returns whole turns,
so the character columns compare cost and not quality. **The zero-evidence guard fired as predicted** — 6 of
`single-session-user`'s 70 questions carry no flagged turn and were dropped by `Load`, which is why n is 64.

### A 1B judge is INERT — it neither helps nor harms, and it has nothing to promote (`memory-locomo --retrieval`, 2026-09-11) <!-- result: id=locomo-judge-1b-n200 arm="`+sem+rel-only+judge` with `gemma-3-1b-it` Q4_K_M (806,058,240 B) in place of the 4B incumbent" metric=evidence-hit@k n="200" value="83.0%" ships=no status=CURRENT -->

**What varied, and only this:** the judge model. Same seam, same prompt, same corpus, same embedder, same
arms. `gemma-3-1b-it` Q4_K_M at **806,058,240 B** against the incumbent `gemma-3-4b-it` Q4_K_M at
**2,489,757,856 B** — same family and same quantisation on purpose, so this isolates SIZE. It is not the
recency question the item's title asks; Part 176 answered recency in the reranker role.

**Instrument.** `memory-locomo --retrieval --n 200`, 791.0s, both models served by `llama-server` on
dedicated GPU ports. Raw output, gitignored:
`devtools/_judge-1b-ladder.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->.
Controls reproduce: `+sem+rel-only` **83.0%**, `vector` **80.5%**.

| arm | 1B | 4B incumbent |
|---|---:|---:|
| `+sem+rel-only` (no judge) | 83.0% | 83.0% |
| `+sem+rel-only+judge` (shipped depth 80) | **83.0%** | 72.5% |
| `+sem+rel-only+judge@40` | **83.0%** | 84.0% |
| `+sem+rel-only+judge@20` | 83.0% | 83.0% |

**1. It does not move the score at ANY depth, and the arm is not broken.** 200 calls, **0 declined**, 2.4
endorsed per call — the judge answered every time and did endorse. The seam is fail-open, so an arm scoring
its own base has two readings; the audit separates them here and this is the real one.

**2. The two sizes fail in OPPOSITE ways, which is the finding.** The 4B ranks well and stops badly —
29.1 endorsements of 80 at 2.6% precision, a 17× lift at its own top-1 — so it floods a 20-slot page and
destroys 10.5 points. The 1B stops fine and cannot rank: 2.4 endorsements, and cumulative precision by its
own order runs 14.5% at top-1 against 8.7% overall, a **1.7× lift**. The harness's own note applies —
*a flat row means the order is noise and no truncation of it can help.*

**3. The ceiling is ZERO, and that is the number to carry.** On the 19 calls of 200 where a verifier could
possibly help — page missed the evidence, something deeper held it — the 1B endorsed that evidence
**0 times (0%)**. The 4B endorsed it **10 times (53%)** on the same 19, which is what put a **+5.0** ceiling
on the incumbent. So the smaller model is not a cheaper judge with less headroom; it has **no headroom at
all**, and no promotion rule, threshold or combination over it could win a point.

**So a smaller INSTRUCT model is the wrong direction for this seam.** At the shipped depth the 1B is
strictly safer than the 4B (83.0 against 72.5) purely by being inert, which is a real operational fact and
not a gain. **The measured way to spend under a gigabyte here is a cross-encoder, not an instruct model**:
468 MB captures 6.0 of the 7.0 points a perfect judge offers, where 806 MB of instruct model captures 0.0.

**What it does NOT say.** One workload, one embedder, n = 200, one family. The rescuable cell is 19 calls,
so 0 of 19 bounds the ceiling rather than proving the model never could. It says nothing about the 1B in a
different role — extraction, annotation and classification are different tasks and none was measured here.

### ONE model serving MANY seams: the judge pays ~10x for SHARING, on top of being 2x slower (`memory-contention --device both --verifier both`, 2026-09-11) <!-- result: id=contention-mixed-recall-quiet-rerank arm="`rerank` — the bench-local `CrossEncoderVerifier` over `bge-reranker-v2-m3`, alone in the SINGULAR verification slot, mixed write+recall load, quiet device, `dedicated` topology" metric=latency n="50 writes + 50 recalls per cell, 4 workers per loop, repeat 3" value="94.5 ms recall p50, against 2,672.0 ms for a judge sharing the instruct server with annotation" ships=no status=CURRENT -->

**What varied, and only this:** which backend fills the verification slot — `judge` (the shipped
`LlmMemoryVerificationPolicy`, on the same `gemma-3-4b-it` server annotation already writes through) against
`rerank` (the bench-local `CrossEncoderVerifier`, on its own `bge-reranker-v2-m3` server) — crossed with a
quiet and a busy device. Same corpus, same embedder, same engine, same `dedicated` topology, one process per
model in every cell.

**The `rerank` arm ran a bench-local STAND-IN, not the shipped policy, and that is the one naming difference
that matters here.** `CrossEncoderVerifier` is the harness's own class;
`src/Lyntai.Core/Memory/Verification/ScoringVerificationPolicy.cs` is what `AddMemoryScoringVerification`
registers. The two are equivalent FOR A COST MEASUREMENT — the same POST to the same `/v1/rerank`
endpoint against the same model, and the same fixed top-N endorsement rule, differing only in where N comes
from (a constructor argument set to the recall limit, against
`ScoringVerificationOptions.EndorseCount`) — so the latency is dominated by an identical HTTP call and
these figures carry — **except CLIENT LIFETIME.** The bench shares ONE `HttpClient` (`UseProxy = false`)
across every call; the shipped policy's default (`disposeHttpClient = true`) creates and disposes one PER
call via `httpFactory()`, which against a 94.5 ms mixed p50 is not noise. So the absolute `rerank` figures
below are not what the shipped policy's default configuration would cost; the ARM DIFFERENCE (`judge`
against `rerank`, both read off this same bench) is unaffected. **What does NOT carry is anything about the
shipped policy's own configuration surface: this row prices the SHAPE of a cross-encoder in the
verification slot, never the shipped type.**

**THREE model-backed seams, not four** — annotation, verification, embedding. `IMemoryVerificationPolicy` is a
SINGULAR slot (**D115**), so judging and reranking are ALTERNATIVES a deployment chooses between and can never
contend with EACH OTHER; this run fills the slot with one at a time. **Only annotation can contend, and only
with whichever verification backend shares its server.** The plan that scoped this bench said four seams and
was wrong.

**Neither arm SHIPS**, which is why the row reads `ships=no` — and on the `rerank` side for two independent
reasons, the stand-in above being the second. Verification is opt-in in full: a default engine registers no
`IMemoryVerificationPolicy` at all, and both `AddMemoryVerification` and `AddMemoryScoringVerification`
are calls a consumer makes deliberately. Each arm is a rung.

**Instrument.** `node devtools/dev.mjs memory-contention --device both --verifier both --writes 50
--recalls 50 --repeat 3`, exit 0, the `dedicated` arm only. Every cell seeds 50 entries UNTIMED first, then
times 50 writes and 50 recalls; the `mixed` cell runs both loops together at 4 workers each (up to 8 requests
in flight), and every figure below is the median of 3 runs. Models: `gemma-3-4b-it` Q4_K_M (chat — annotation
AND the judge), `embeddinggemma-300M` Q8_0, `bge-reranker-v2-m3` Q8_0, each on its own `llama-server` port.
Raw output, gitignored:
`devtools/_contention/run-grid-2026-09-11.txt` <!-- link-ok: gitignored raw sweep output, named as provenance -->.
**Context and batch were RAISED so the servers accept this harness's longest real input** — embed
2048/2048/2048 (ctx/batch/ubatch), rerank 4096, chat ctx 8192 — because the stock physical batch rejects it at
512 tokens (`.claude/knowledge/pitfalls.md` §Environment / tooling). That raises per-server VRAM, so **these
are not stock-default residency figures**: the sampler saw at most 6,770 MiB in use on the quiet device and
8,069 MiB on the busy one, both including the 2,652 MiB the device already held before the run.

| device | verifier | solo recall p50 | mixed recall p50 | mixed / solo |
|---|---|---:|---:|---:|
| quiet | `judge` | 268.6 ms | **2,672.0 ms** | **9.95x** |
| quiet | `rerank` | 129.8 ms | **94.5 ms** | 0.73x — unresolved, see 2 |
| busy | `judge` | 530.6 ms | 3,744.4 ms | 7.06x |
| busy | `rerank` | 75.3 ms | 129.7 ms | 1.72x |

| device | verifier | solo write p50 | mixed write p50 |
|---|---|---:|---:|
| quiet | `judge` | 299.2 ms | 2,779.1 ms |
| quiet | `rerank` | 350.8 ms | 1,342.6 ms |
| busy | `judge` | 627.9 ms | 3,923.4 ms |
| busy | `rerank` | 632.5 ms | 1,915.0 ms |

**1. TWO separate wins, and the CONTENTION half is the larger — the decomposition is more durable than the
headline.** Before any contention the cross-encoder is already about **2x** faster than the judge (129.8 ms
against 268.6 ms, solo, quiet). The judge then pays a **further ~10x** for SHARING the instruct server with
annotation, where the cross-encoder pays nothing measurable. Moving verification off the shared model is worth
2,577.4 ms of the mixed p50 recall on a quiet device (96% of it) and 3,614.7 ms on a busy one (97%) — but only
the first 2x of that is the model being smaller. Report both halves or a reader attributes the whole gap to
model speed and expects it wherever a small model is used.

**2. Under `rerank` on a quiet device, contention did NOT RESOLVE.** Mixed measured 35.3 ms FASTER than solo,
which contention cannot do, and at 3 runs the two cells' own spreads OVERLAP — solo 69.8–136.7 ms, mixed
86.9–111.3 ms. The honest read is that contention on this arm sits at or below what this instrument resolves
at this repeat count: **not zero, not negative, not any particular size.** The busy device does resolve it, at
**54.3 ms (72% of the p50)**, so the effect is real and small rather than absent. (The raw output's own line
predates a wording fix and tells a `--repeat 3` reader to re-run with `--repeat`; the numbers are unchanged and
reproducible, only the framing was corrected.)

**3. The busy arm is a COMPUTE load, not a graphics one.** A second `llama-server` generating tokens on its own
port, held for the whole cell — NOT the rendering neighbour `.claude/knowledge/pitfalls.md` measured the
12x-faster-to-26x-slower swing against. The DIRECTION should transfer; the MAGNITUDE may not. It roughly
doubles the write path in both arms (299.2 → 627.9 ms judge, 350.8 → 632.5 ms rerank) and leaves the judge's
mixed recall at 3,744.4 ms against the cross-encoder's 129.7 ms.

**4. The controls, so this row can be refuted.** Hit-rate **1.000** in all eight cells and **0 errors**;
subjects/write 1.24–1.30; distinct rerank scores **0** under `judge` — correct, that arm never calls the
reranker — and 250 solo / 130 mixed under `rerank`; the GPU census CLEAN on both devices, only this run's PIDs
and the one known neighbour having touched it. And the two solo WRITE rows are the same workload measured
twice, because no verifier runs on a write: they agree to within 1% on the busy device (627.9 against 632.5)
and differ 17% on the quiet one (299.2 against 350.8), which bounds this instrument's own write-path noise.

**5. Absolute latencies are THIS MACHINE's.** Only the arm differences transfer.

**What it does NOT say.** No recall QUALITY — there is no ground truth in this harness, so nothing here says
whether contention changes WHAT comes back, only what it costs. The `dedicated` topology only: the two router
arms were not run, and the two structural facts that would shape one — llama.cpp's router being a process
SUPERVISOR that spawns a child server per model, so consolidating saves no memory, and `--embedding` /
`--reranking` being PROCESS-WIDE — are already recorded in `.claude/knowledge/pitfalls.md` and are deliberately
not copied here. `--parallel` was fixed. SQLite only, one embedder, one judge model, one cross-encoder.

### The backlog predicted a NULL and the run REFUTES it: the shipped partition costs a READER 3.5 points, and verdict fusion gives 3.0 of them back (`memory-locomo --verdict`, 2026-09-11) <!-- result: id=locomo-verdict-partition-reader-n300 arm="`+sem+rel-only+judge` — the SHIPPED `MemoryVerdictCombination.Partition`, against the no-judge base, paired per question" metric=token-f1 n="301 questions sampled from 1,540, seed 12345, stratified by category" value="−0.0346, CI [−0.0579, −0.0113]" ships=yes status=CURRENT -->

**What varied, and only this:** how an endorsed candidate reaches the page. Three arms, one reader, one
judge, one corpus, one embedder, the same derived depth of 80: the no-judge base `+sem+rel-only`; the
shipped partition `+sem+rel-only+judge`; and `+sem+rel-only+judge+enginefuse`, which is
`GraphMemoryOptions.VerdictCombination` set to `MemoryVerdictCombination.Fuse`.

**THREE different things in this bench are called fusion, so none of them is written bare here.** This
section measures **D105's VERDICT combination**, the `+enginefuse` suffix. It is **not D103's RANKING
fusion**, which the same bench runs as its `lyntai-fused*` arms. And it is **not the published
`+sem+rel-only+judge+fuse` arm** of the 2026-09-03 section above, which proved the same IDEA on
`evidence-hit@k` through the bench-local `FusedVerdictVerifier` — a verifier that emits an already-fused page
as its verdict. `+enginefuse` instead sets the option and lets the ENGINE reorder and apply its own cut, so
it is the control saying the shipped code reproduces that proof; an arm landing on the partition's number
would have meant the option never reached this path. A name meaning three things makes a table silently
wrong, which is why the harness spells the distinction out in its own header too.

**`Partition` is the SHIPPED default and `Fuse` is not**, which is why the row above reads `ships=yes` and
the row below reads `ships=no`. Nothing in this section is a recommendation to change that: **D105** decided
the default on a different metric and is not re-opened by one workload's reader arm.

**Instrument.** `node devtools/dev.mjs memory-locomo -- --verdict --n 300`, exit 0, 2,969.6s, 301 questions
of LoCoMo's published categories 1–4, chat and embedder each on their own `llama-server` port. Raw output,
gitignored:
`devtools/_verdict/run-verdict-n300-2026-09-11.txt` <!-- link-ok: gitignored raw sweep output, named as provenance for the tables below -->.

| arm | token-F1 | judge-graded | exact | unknown | items/q | endorsed/recall |
|---|---:|---:|---:|---:|---:|---:|
| `+sem+rel-only` (base, no judge) | **33.1%** | 59.8% | 15.9% | 23 | 20.0 | — |
| `+sem+rel-only+judge` (`Partition`, shipped) | 29.7% | 57.5% | 14.0% | 23 | 20.0 | 33.6/20 |
| `+sem+rel-only+judge+enginefuse` | **32.6%** | 59.1% | 15.3% | 17 | 20.0 | 33.6/20 |

**1. The prediction that failed is the finding, so it goes first.** The backlog item this closes
(`docs/task-archive.md` **Part 191**) wrote its own expectation down: *"Do not expect it to move the score —
fused, the arm lands on its base — so this is a check that reordering costs nothing a reader notices, not a
hunt for a gain."* **Half of that holds and the half it rests on does not.** Fused, the arm does land just
short of its base (32.6 against 33.1), which is exactly D105's *"removes a loss and never beats the base"* —
and that **0.5-point** gap is the one comparison in this section carrying no interval, so read it against
the floor in finding 4: it is under a QUARTER of the 0.0233 this run could resolve on a difference of that
shape, which is what makes "lands on its base" evidence rather than an eyeball.
But the option is not what fails to move the score — **the PARTITION moves it, downward**, and fusion gives
most of that back. Both paired bounds exclude zero: `partition − base` is **−0.0346 token-F1, 95% CI
[−0.0579, −0.0113]**, and `enginefuse − partition` is **+0.0295, 95% CI [+0.0078, +0.0512]**
<!-- result: id=locomo-verdict-enginefuse-reader-n300 arm="`+sem+rel-only+judge+enginefuse` — the same judge with `VerdictCombination` set to `Fuse`, against the shipped partition, paired per question" metric=token-f1 n="301 questions sampled from 1,540, seed 12345, stratified by category" value="+0.0295, CI [+0.0078, +0.0512]" ships=no status=CURRENT -->.
The two arm columns differ by 3.4 and 2.9 where the paired means read 3.46 and 2.95 — the columns are
rounded to a tenth of a point, the paired means are not, and they are the same quantity. So a session
planning work here should read the option as INSURANCE against a loss the shipped default really does take
on this workload, not as a change that buys nothing.

**2. The mechanism is in the run's own audit: the judge endorses 33.6 candidates for a 20-slot page.** Under
`Partition` an endorsed set larger than the page REPLACES the ranking instead of refining it — everything
unendorsed is pushed off however well it was ranked — which is precisely the harm **D105** names, now
observed on a reader-facing metric rather than on the returned set. The endorsements are almost all noise:
**precision 2.4%** (239 of 10,107 endorsements were evidence), against a judge that does find most of what
is reachable, **recall 65.7%** (239 of the 364 evidence items shown). `unknown` falls 23 → 17 under
`+enginefuse`, which is the same story from the reader's side: the page more often contains something to
answer from.

**3. The controls, so a reader can refute the row.** The two judge arms' audits are **byte-identical** —
calls 301, declined 0, judged 'none relevant' 0, shown/call 80.0, endorsed/call 33.6, evidence shown/call
1.21, precision 2.4%, recall 65.7%, 239 of 10,107, and the same cumulative precision-by-own-rank row. The
harness ASSERTS that equality on calls / shown / endorsed / declined / empty verdicts and exits non-zero
rather than publishing when it breaks, which is what proves both arms saw the same judge output and only the
COMBINATION differed. Beyond that: `items/q` is **20.0** on every arm, so no arm was handed a bigger page;
`unknown` runs 17–23 of 301, far from the count that would make an arm's score a retrieval failure; and
every per-conversation clone control reproduces (419 of 419 turns, and so on for all ten conversations).
**The pairing itself rests on two guarantees of DIFFERENT strength, and they are worth separating.** What
the harness asserts is only that the three per-question score lists are equal in COUNT, refusing rather than
truncating to the shortest — a silently truncated pairing would produce a believable number from mismatched
questions. Question IDENTITY is STRUCTURAL rather than asserted: one pass appends exactly one score per arm
per question, so index *i* is the same question in all three lists by construction. That is the stronger of
the two, and it is why the counts are all that needs checking.

**4. The bound is why a NULL would have been worth stating at all.** This run could not have resolved a
difference smaller than **0.0217 token-F1** on `enginefuse − partition`, or **0.0233** on
`partition − base` — the CI half-widths, printed by the run itself. Each effect clears its own floor by more
than a point. That matters because a bare "we saw no difference" from this instrument is worth nothing: it
is on record manufacturing a **+6.7** at n = 100 that read **+1.0** at full sample
(`locomo-interaction-full-n1540` above, which is the row that withdrew the n = 100 series). So a null here
had to arrive as *"nothing larger than X"* or it would have been unpublishable — which is why the bound was
built before the run rather than reached for after it.

**5. The judge-graded column is generous, and the figure to use is the one derived from these arms.** The
gap is **26.7 / 27.8 / 26.5 points** — judge-graded minus token-F1, base / partition / `+enginefuse`. Read
it as an indicator rather than a bias estimate: the two columns are different scales, a mean overlap against
a share of questions marked correct. **What carries is that the gap is nearly CONSTANT across the three
arms** — it varies **1.3 points** against a largest arm difference of **3.5** — which is what "common-mode"
has to mean for the differences to survive it. The judge-graded column also moves the same direction as
token-F1 on both comparisons (−2.3 then +1.6), as does `exact` (15.9 → 14.0 → 15.3); three columns agreeing
is the reason this is a result rather than the self-grader's opinion.
<br>**The provenance file named above carries one line that is WRONG, and it is this caveat.** It reads
*"generous by roughly 12 points"* — a hardcoded literal the harness printed whatever it had measured, about
15 points off here and the wrong SIGN on the bring-up run. The harness now derives that gap, its spread and
the difference it is judged against from the rows it has just printed (`docs/FIXES.md`, 2026-09-11). Every
other number in that file stands; only that one line does not.

**The harm needs a FLOODING judge, and the verification backend that ships cannot flood — so this result is
CONDITIONAL, not general.** `Partition` only costs anything when the endorsed set is larger than the page:
everything unendorsed is pushed off however well it was ranked, so promotion REPLACES the ranking instead of
refining it. This run's judge endorsed **33.6 for a 20-slot page**, which is the condition met.
`ScoringVerificationOptions.EndorseCount` — the shipped cross-encoder seam (**D115**) — is a **FIXED
count defaulting to 20**, so it endorses at most a page's worth by construction and **cannot reach the
failure measured here at all**. Read the 3.5 points as the price of an instruct judge that floods, never as a
property of `Partition` alone; on the seam a deployment is most likely to register, the two combination rules
have nothing to choose between them.

**What it does NOT say.** **COST** — the judge and the reader share one model, which is the contention
priced in the section above, so nothing here prices the option's latency. **A SECOND READER** — this ran on
one, and the widening across three reader sizes is `docs/task-archive.md` Part 200, not here; the absolute
numbers are a small local model's and only the arm differences transfer. **LoCoMo only**, whose
workload rewards finding where LongMemEval's rewards burying, so this says nothing about the knowledge-update
axis. And the SHIPPED depth only, which is DERIVED (factor 4 × the recall limit = 80) — no depth was swept
here, and the 2026-09-03 sections above are what say depth is the larger lever.

### RETRACTED: the first decision grid let a SECOND correct answer into the options (`memory-decision`, 2026-09-12) <!-- result: id=decision-shape-hard-n200 arm="the first `memory-decision` grid — gold taken as the FIRST evidence id, so a question's other evidence turns stayed eligible as distractors" metric=forced-choice-accuracy n="200 trials x 5 list lengths, run twice" value="rerank 53.5% at N=7, since corrected to 67.4%" ships=no status=RETRACTED -->

Kept rather than deleted, because what it got wrong is instructive and the control that caught it is
reusable. LoCoMo's `evidence` is a LIST: **409 of 1,540 scored questions (26.6%) carry more than one
evidence turn present in their own conversation — 97.9% of the multi-hop class.** The grid took the first
evidence id as gold and left the rest in the distractor pool, where — being highly query-similar by
construction — they were exactly what a cosine-ranked pool selects. So a quarter of the trials had a second
correct option while the prompt asserted *exactly one* answers, and a multi-hop question has no single
answering turn at all. <!-- result: id=decision-generative-score-ties arm="the same grid's generative tie counts, taken under the retracted trial construction AND a tie rule since changed" metric=forced-choice-accuracy n="200 trials x 5 list lengths" value="score-4b 100/200 ties at N=3 rising to 121/200 at N=7" ships=no status=RETRACTED -->

**The bias had a direction, and one arm proves it.** Re-measured clean, every arm that actually reads the
options gained — `rerank` **+13.9 to +15.5**, `select-4b` **+7.1 to +13.0**, `cosine` +8.2 — while
`select-1b` moved **−0.3 to +2.9**, i.e. not at all, because it emits a constant and cannot benefit from
options being cleaner. **The contamination was compressing the gap between judgement and none**, which is
the axis the retracted write-up argued from. It also reversed a headline: the 468 MB cross-encoder read 3.0
points BEHIND the 2.49 GB instruct model and actually sits ahead of it.

**The general trap — a dataset's ground truth is a SET, and taking its first element makes the rest
distractors — is `.claude/knowledge/pitfalls.md`.** Nothing in the run could have shown it: `oracle` was
hardcoded to 100%, so the one control aimed at gold labelling could not fail.

### A decision at 3-7 options: a 468 MB cross-encoder beats a 2.49 GB instruct model (`memory-decision`, 2026-09-12) <!-- result: id=decision-shape-single-evidence arm="`rerank` — a 468,393,760 B cross-encoder argmax-ing N score-a-pair scorings in one round trip — against `select-4b`, one select-from-list call to a 2,489,757,856 B instruct model, on the same forced-choice trials" metric=forced-choice-accuracy n="261 trials x 5 list lengths, run twice" value="72.0% vs 71.5% at N=3; 67.4% vs 63.6% at N=7" ships=no status=CURRENT supersedes="decision-shape-hard-n200" -->

`node devtools/dev.mjs memory-decision --n 350 --difficulty hard`
(`devtools/_decision-fixed-run1.txt`, `devtools/_decision-fixed-run2.txt`). <!-- link-ok: gitignored raw sweep output, named as provenance -->
Every selective figure above stops at 20 candidates and measures an endorse-a-SUBSET task whose base rate
is ~1.5%; a decision is a FORCED CHOICE over a short list, so neither the precision nor the lift column
transfers. `docs/task-archive.md` Part 236 asked which shape wins at N = 3-7 and at what size.

One trial is a LoCoMo question with **exactly one** evidence turn in its conversation, plus N−1 distractors
that are the top NON-evidence turns by cosine — what a retriever actually hands a decision system. 261
trials of 350 sampled survive that filter (multi-hop 2, temporal 64, open-domain 12, single-hop 183).
Option sets are nested, so one per-option scoring serves all five list lengths; gold's slot is randomised
and the distractors are shuffled before it is inserted. Embedder `embeddinggemma-300M-Q8_0`
(333,590,944 B). The pre-registration's scorecard is below.

| arm | bytes | N=3 | N=4 | N=5 | N=6 | N=7 |
|---|---:|---|---|---|---|---|
| chance | — | 33.3% | 25.0% | 20.0% | 16.7% | 14.3% |
| `cosine` | 333,590,944 | 40.2% | 40.2% | 40.2% | 40.2% | 40.2% |
| `select-1b` | 806,058,240 | 33.7% | 24.5% | 20.8% | 18.1% | 13.9% |
| `score-1b` † | 806,058,240 | 58.4% | 53.7% | 49.4% | 46.1% | 43.6% |
| `score-4b` † | 2,489,757,856 | 67.4% | 65.7% | 64.8% | 62.2% | 59.8% |
| `select-4b` | 2,489,757,856 | 71.5% | 69.7% | 69.0% | 66.3% | 63.6% |
| **`rerank`** | **468,393,760** | **72.0%** | 68.6% | 67.8% | **67.8%** | **67.4%** |

† accuracy over the trials the arm actually DECIDED. A tie at the maximum is a decline, counted as
not-fired exactly as a `select` arm replying `0` is — and the generative scorers decline constantly:
`score-4b` fires on **49.4% falling to 33.3%** of trials as N grows, `score-1b` on 70.9% to 57.1%. Their
columns are therefore "how good is it when it commits", never a rate over all trials.

**1. The best arm is the smallest model in the grid.** The 468,393,760-byte cross-encoder matches the
2,489,757,856-byte instruct model at N = 3 (72.0% against 71.5%) and pulls **ahead** as the list grows
(67.4% against 63.6% at N = 7) — at **5.3x fewer bytes**, in ONE round trip, and it is the only arm flat in
N. Both figures reproduce byte-identically across the two runs.

**2. The winning GENERATIVE shape inverts with model size — with a caveat the strict tie rule exposes.**
Paired McNemar over all trials: the 4B reads **+99 / +115 / +121 / +117 / +114** net to `select`
(`p<0.0001` throughout) and the 1B reads **−20 / −24 / −26 / −24 / −29** to `score` (`p=0.082` at N = 3,
then `p` at most 0.024). But most of the 4B's margin is availability rather than judgement: when
`score-4b` commits it scores 59.8-67.4%, near `select-4b` — it simply refuses on half to two-thirds of
trials.

**3. The 1B in the select shape is not choosing badly — it emits a CONSTANT.** At N = 7 it answered slot 1
on **100%** of trials where gold sat there and 0-5% at the six other slots, and its accuracy tracks 1/N at
every length. Accuracy alone reads as "at chance", which sounds like a weak judge; only a position column
separates a constant from one. It is also the arm that did not move when the trial defect was fixed, which
is what made that defect's direction provable.

**4. A generative scorer cannot rank, because the scale it emits is coarse.** `score-4b` tied at the
maximum on **132 of 261** trials at N = 3 rising to **174 of 261** at N = 7; the cross-encoder scored
**1,827 of 1,827 pairs distinct**. <!-- result: id=decision-generative-score-declines arm="`score-4b` — how often a generative 0-100 scorer ties at the maximum and therefore DECLINES, against the cross-encoder on the same options" metric=forced-choice-accuracy n="261 trials x 5 list lengths, run twice" value="score-4b ties on 132/261 at N=3 rising to 174/261 at N=7; rerank 1827/1827 pairs distinct" ships=no status=CURRENT supersedes="decision-generative-score-ties" -->
That is a property of the SCORER, not of the shape: the same shape carried by a cross-encoder is the best
arm here. Guessing among the tied winners would leave `score-4b` at 54.0% falling to 33.0% over all trials,
below every committed figure.

**The 0-100 scale is itself a measured correction.** A first pass asked for `LlmScorerBase`'s own 0..1 and
captured replies show both models returned a score of 0 or 1 and **nothing between**. A binary score cannot
rank 7 options, so the tie rate would have been an artifact of the scale the prompt asked for. Widening it
took the 4B to a genuinely graded 0/10/20/30/60/70/75/90/95/100. **Capture what the model SAID before
pricing what it did.**

**5. The 4B's position effect is an END-OF-LIST penalty, not the first-slot preference the library
mitigates.** At N = 7, `select-4b` reads 68 / 70 / 78 / 68 / 73 / 69 / **21%** by slot (23% on the repeat)
against roughly 40 trials per cell, while none of the argmax controls dips at slot 7 (`rerank` 72%,
`cosine` 46%). `LlmPairwiseComparer` runs both orders and reports `Tie` on disagreement precisely because
judges favour what is shown FIRST; nothing at N > 2 carries that mitigation, and this says the N > 2
failure is a different one.

**Controls, all passed, and two of them are arithmetic.** `random` sat inside its Wilson interval of 1/N at
all five lengths; `oracle` — a synthetic score row pushed through the SAME shown-slot and argmax path every
real arm uses — read 261/261 at all five, so the slot arithmetic is exercised rather than asserted; the
select prompt grew monotonically 1,061 to 1,788 characters, identically for both models; the cross-encoder
passed `rerank-screen`'s published REFERENCE pair. **And `cosine` reads 40.2% at every N, exactly the rate
(105/261) at which gold is its conversation's cosine top-1** — under `hard` the distractors are the top
non-evidence turns, so cosine can win only when gold is rank 1, and must be flat in N. It is.

**The noise floor is MEASURED.** The whole grid ran twice: **max |delta| 1.7 points, mean 0.17 over 40
cells**, with 27 of 40 byte-identical including every `rerank`, `cosine`, `oracle` and `select-1b` cell.
Claim 1's N = 7 gap (3.8 points) is twice the max per-cell delta; claim 2's nets are twenty times it.

**One harness shortcut was PROVEN rather than assumed.** The `rerank` arm scores all seven options in ONE
rerank call and argmaxes each N's subset, which is legitimate only if a cross-encoder's score does not
depend on the other documents in the request. Probed directly: two identical 7-document calls differ by
**9.420e-3** (independently reproducing the drift figure this record already carries), and scoring the same
documents in a 3- or 5-document request moves them by **1.722e-2** and **9.420e-3** — the same order as the
noise, against a spread of about 21 units, with the argmax unchanged in both subsets.

**The pre-registration's scorecard, so this is not read post-hoc.** Predictions were filed before any cell
ran. **HELD:** the 4B select band (55-80% at N = 5; read 69.0%); `rerank` beating `score-1b` and landing
within a few points of the 4B (it beat it); `cosine` far below the model arms. **REFUTED:** the 1B select
band (30-55% predicted, and the prediction explicitly did NOT expect inertness — it read 20.8%, exactly
chance, and is inert in a way nothing anticipated, as a constant rather than as noise); and monotonic
decline in N for every model arm (`rerank` is flat). **HALF:** score beating select for the 1B held, while
"smaller or absent for the 4B" was wrong in direction — it is reversed and large.

**What it does NOT say.** Nothing about `affordance` — no arm hands a model a tool roster, and that is Part
178's remaining item. Nothing about a two-stage shape that resolves a generative scorer's ties with a
second pass; that is an unmeasured lever, not a measured cost. **Multi-hop is excluded by construction**
(2 of 261 trials), so this measures single-fact retrieval decisions and says nothing about a question
needing several turns. One corpus, one language, one distractor policy, and bench-local prompts — the
shipped judge prompt endorses a subset and cannot express a forced choice. And no COST column: the device
carried a ~30% neighbour load throughout, which bounds the wall clock and not the accuracy.

### `affordance`, measured for the first time: a 4B routes tools well and CANNOT DECLINE (`tool-affordance`, 2026-09-12) <!-- result: id=affordance-prompt-protocol-4b arm="`loop-4b` — a 2,489,757,856 B instruct model choosing from a 3-7 tool roster through `ToolLoop`'s own PROMPT protocol — against `cosine`, a 333,590,944 B embedder argmax-ing the same tool declarations" metric=forced-choice-accuracy n="168 trials x 5 roster sizes, run three times" value="86.3% vs 81.0% at N=7; 87.9% vs 81.0% at N=3" ships=no status=CURRENT -->

`node devtools/dev.mjs tool-affordance` (`devtools/_affordance/run1.log`, `run2-preamble.log`,
`run3-escape.log`). <!-- link-ok: gitignored raw sweep output, named as provenance -->
`docs/model-tasks.md` §1 lists `affordance` as the one shape with **no evidence at any size** and the one
this library does not bound. This is that measurement, and it is the PROMPT half only: the native transport
is silently inert on both models here and blocked on a positive control (`docs/task-archive.md` Part 236).

**READ THIS ROW FIRST IF YOU ARE SIZING A DEPLOYMENT.** The headline below compares a 2,489,757,856 B model
against a 333,590,944 B embedder, and that is the WRONG comparison for a project whose stated aim is ~500 MB
(`docs/model-tasks.md` §3). At and under that target **there is no working generative tool-chooser here at
all**: the smallest generative arm is 806,058,240 B — already over — and it reads **10.1% at three options
falling to 3.0% at seven** through the loop, against the embedder's 81.0%. The 4B rows price a ceiling, not
a candidate. <!-- result: id=affordance-target-size-1b arm="`loop-1b` — a 806,058,240 B instruct model choosing from a 3-7 tool roster through `ToolLoop`'s prompt protocol — against `cosine`, a 333,590,944 B embedder over the same declarations" metric=forced-choice-accuracy n="168 trials x 5 roster sizes, run three times" value="10.1% at N=3 falling to 3.0% at N=7, against 81.0% flat" ships=no status=CURRENT -->

**The corpus is SYNTHETIC and is the weakest evidence tier this repository publishes** — 42 authored tools
in 6 families of 7, 4 requests each, distractors drawn from the gold's own family. Requests are written not
to echo their tool's name, but gold is still the embedder's top-1 across all 42 declarations on **71.4%** of
trials, so most of this corpus does not need a model at all. Only arm DIFFERENCES transfer; no absolute
number here does. Every loop arm runs the real `ToolLoop` over a real `ToolRegistry`.

| arm | bytes | N=3 | N=7 | where `cosine` is WRONG (n=32 per N) |
|---|---:|---|---|---|
| chance | — | 33.3% | 14.3% | — |
| `cosine` | 333,590,944 | 81.0% | 81.0% | 0% by construction |
| `rerank` | 468,393,760 | 78.6% | 76.8% | 53-56% |
| `loop-1b` † | 806,058,240 | fires 32/168 | fires 11/168 | 0-9% |
| `select-1b` | 806,058,240 | 36.9% | 19.0% | 9-44% |
| **`loop-4b`** | **2,489,757,856** | **87.9%** | **86.3%** | **72-84%** |
| `select-4b` | 2,489,757,856 | 93.9% | 90.4% | 75-88% |

† accuracy is unreadable where an arm fires on under 90% of trials; the 1B's own column is its fired count.

**1. The 4B earns its bytes, and the free arm is closer than expected.** It clears a 333,590,944-byte
embedder by 5.3 points overall — and on the 28.6% of trials that embedder gets wrong it recovers **72-84%**
where the embedder is 0% by construction. That second column is the one worth reading: an arm strong overall
and weak there is tracking the embedder rather than routing. **The cross-encoder that won the neighbouring
decision grid LOSES here** (76.8% against the bi-encoder's 81.0%), which is a shape result, not a size one.

**2. The transport is nearly free for a 4B and destroys a 1B.** Paired McNemar, loop against the same choice
posed flat: the 4B reads **−7 to −11 net at p=0.099-0.265, significant at no roster size**, while the 1B
reads **−27 to −45 at p<0.0001 at every one**. The mechanism is not bad choices — it is `no tool invoked at
all`, **81.0% at N=3 rising to 93.5% at N=7**, against the 4B's 1.8-4.2%.

**3. …and the 1B is not choosing badly, it emits a CONSTANT** — the same failure the decision grid found, on
a different corpus. At N=7 `select-1b` answers slot 1 on 92% of the trials gold sat there and 0-8% on five of
the six others. A captured reply shows the loop failure is GRAMMAR, not judgement:
`{"final": "…I need to access current weather conditions and sea state data. Please wait a moment while I
retrieve the data."}` — a model that has decided to use a tool and cannot say so.

**4. A 4B given a roster CANNOT DECLINE, and no prompt tested moves it.** <!-- result: id=affordance-false-call-4b arm="`loop-4b` on 20 requests NO tool in the roster serves, under the SHIPPED protocol preamble and two candidate rewrites" metric=false-call-rate n="20 negative requests x 5 roster sizes x 3 preambles" value="90-95% shipped; 100% pushing toward tools; 90-95% escape-first" ships=yes status=CURRENT -->
On 20 requests nothing on the roster serves, it invokes a tool on **90-95%** of them — under the preamble the
library ships today. It is not picking something defensibly adjacent; it fabricates arguments to force a fit:
`restart_service {"service": "sourdough_starter_knowledge_base"}`, `historical_weather {"place": "room"}` for
a paint-quantity question, `translate_text {"to": "en"}` on English. **The 1B is the exact mirror — 0-5%
false calls.** One prompt, two opposite pathologies.

**Two preamble rewrites were measured and BOTH refuted**, which is what makes this a property of the shape
rather than of the wording. Pushing harder toward tool use took false calls to **100%** for a gain
significant in 1 of 10 paired cells; putting the escape first left them at **90-95%** and was significant in
**0 of 10**. `LyntaiOptions.ToolProtocolPreamble` shipped from this work so a deployment can try its own, and
**the default was deliberately left unchanged** — no tested wording earned the change.

**Controls, all passed.** `loop-oracle` — a scripted client driving the REAL loop, not an argmax side path —
read 168/168 at every roster size, so the protocol grammar, registry lookup, argument validator, step
recording and slot arithmetic are exercised rather than asserted; `loop-random` sat inside its Wilson
interval of 1/N at all five; both prompts grew monotonically with N, read off the request the transport
actually sent; **0 of 5,040 and 0 of 6,720 model trials went unanswered**.

**The noise floor is MEASURED**: the grid ran three times, `cosine` byte-identical at 81.0% throughout and
`loop-4b` spanning **1.2 points** at N=3 and 0.7 at N=7. Claim 1's 5.3-point margin is four times that;
claim 2's 1B nets are twenty times it.

**What it does NOT say.** Nothing about the NATIVE transport. Nothing about a roster larger than seven — and
a catalogue is where the bound would matter most. Nothing about argument QUALITY on positive trials, only
that the arguments parsed. `ships=yes` on the false-call row means the PREAMBLE measured is the shipped one,
never that this corpus resembles a deployment's.

> _Corrected 2026-09-12. Claim 1 above read "the **28.6%** of trials that embedder gets wrong". The share is
> **19.0%** — this section's own table puts `cosine` at 81.0% and its own subset header at `n=32 per N`, and
> 32/168 is 19.0%. The 72-84% recovery figure is unaffected: it is a rate over that subset, and the subset
> size was right everywhere it was printed. Recorded rather than silently fixed, because the mechanism is
> the one this file exists to expose — a share RESTATED in prose beside the table it contradicts._

### Sub-100 MB in the EMBEDDER role: it works, and the deficit is a function of ROSTER SIZE (`embed-screen` + `tool-affordance`, 2026-09-12) <!-- result: id=embed-screen-sub100mb arm="four sub-100 MB GGUF embedders — `all-MiniLM-L6-v2` at f16 and Q8_0, `bge-small-en-v1.5` f16, `bge-small-zh-v1.5` f16 — against the 333,590,944 B incumbent as a known-good control" metric=screen-verdict n="1 known-similar/known-unrelated pair plus a 4-topic 28-cosine fixture per model, and a 6,263-character extreme probe" value="4 of 4 HEALTHY; 0 of 4 accept an input over 512 tokens" ships=no status=CURRENT -->

`node devtools/dev.mjs embed-screen --control … --model …` and `node devtools/dev.mjs tool-affordance
--scorers-only --embed-arm …` (`devtools/_embed-survey/screen2.log`, `affordance1.log`,
`affordance2.log`). <!-- link-ok: gitignored raw sweep output, named as provenance -->
`docs/model-tasks.md` §3 re-aimed the sub-100 MB target from the reranker role to the embedder role on the
argument that llama.cpp PR #21729 does not reach it. **That argument had never been tested.** This is the
test, and the routing measurement it unblocks.

**The blocker really does not reach this role, and the mechanism is sharper than "an embedder needs
neither".** All four conversions declare `tokenizer.ggml.token_type_count = 2` — they are BERT models that
HAVE segment embeddings, the exact property that condemns a cross-encoder. It costs an embedder nothing
because **a single-sequence input IS segment 0**: zeroing `token_type_ids` writes the correct value rather
than destroying a signal. And `bert.pooling_type` SURVIVES conversion — `1`/mean for MiniLM, `2`/CLS for
both bge models — so llama.cpp pools as trained, and the right `--pooling` flag is no flag at all.

| model | bytes | dim | ctx | pooling | gap | range | margin | >512 tok | health |
|---|---:|---:|---:|---|---:|---:|---:|:---:|---|
| `embeddinggemma-300M` Q8_0 **(control)** | 333,590,944 | 768 | 2048 | declared | 0.7365 | 0.6431 | **+0.1634** | yes | HEALTHY |
| `all-MiniLM-L6-v2` f16 | 45,949,216 | 384 | 512 | mean | 0.6671 | 0.7640 | −0.0602 | no | HEALTHY |
| `all-MiniLM-L6-v2` Q8_0 | 25,008,064 | 384 | 512 | mean | 0.6672 | 0.7647 | −0.0612 | no | HEALTHY |
| `bge-small-en-v1.5` f16 | 67,308,128 | 384 | 512 | cls | 0.4259 | 0.4366 | −0.0783 | no | HEALTHY |
| `bge-small-zh-v1.5` f16 | 47,886,240 | 512 | 512 | cls | 0.2103 | 0.2837 | −0.0984 | no | HEALTHY |

`gap` is the health ASSERTION — a known-similar text minus a known-unrelated one on an easy pair. `margin`
is a REPORTED sharpness number from a hard fixture whose within-pair sentences share no content word while
two DIFFERENT pairs share vocabulary. It is deliberately not part of the verdict: **every candidate is
blunter than the control and none is broken**, and a screen that failed them would have published *"no
sub-100 MB embedder works"* — the retracted-reranker mistake pointed the other way.

<!-- result: id=affordance-cosine-sub100mb arm="`cosine-minilm` — a 25,008,064 B `all-MiniLM-L6-v2` Q8_0 argmax-ing tool declarations — against `cosine`, the 333,590,944 B incumbent, on trials PINNED to the incumbent so that only the scoring varies" metric=forced-choice-accuracy n="168 trials x 5 roster sizes, run twice" value="78.6% at N=3 and 69.6% at N=7, against 81.0% flat" ships=no status=CURRENT -->

| arm | bytes | N=3 | N=4 | N=5 | N=6 | N=7 | where `cosine` is WRONG (n=32/N) |
|---|---:|---|---|---|---|---|---|
| chance | — | 33.3% | 25.0% | 20.0% | 16.7% | 14.3% | — |
| `cosine` **(control)** | 333,590,944 | **81.0%** | **81.0%** | **81.0%** | **81.0%** | **81.0%** | 0% by construction |
| `cosine-minilm` Q8_0 | 25,008,064 | 78.6% | 76.2% | 73.8% | 72.0% | 69.6% | **56.2-62.5%** |
| `cosine-minilm` f16 | 45,949,216 | 78.6% | 76.8% | 74.4% | 72.6% | 70.8% | — |
| `cosine-bgeen` f16 | 67,308,128 | 76.8% | 73.2% | 71.4% | 71.4% | 69.6% | 40.6-53.1% |
| `cosine-bgezh` f16 † | 47,886,240 | 46.4% | 40.5% | 39.3% | 37.5% | 35.1% | 15.6-21.9% |
| `rerank` (`LAMAR-600m`) | 468,393,760 | 78.6% | 78.0% | 77.4% | 76.8% | 76.8% | 53.1-56.2% |

† an ENGLISH-only corpus against a Chinese vocabulary — finding 4, and it is not a result about the model.

**1. It survives a SHORT roster and decays with a long one.** −2.4 points at three options, −11.4 at seven.
The incumbent is **flat in N** — 81.0% at every roster size — and not one sub-100 MB arm is. So *"does
81.0% survive at a tenth of the bytes"* has no single answer: it nearly survives at N = 3 and does not at
N = 7. That is §2's list-length rule appearing on a **model-free** arm, where it had only been measured on
generative and selective ones.

**2. The 25,008,064 B model is not TRACKING the incumbent — it errs differently.** On the 32 trials per
roster size the incumbent gets wrong, it is right on **56.2-62.5%** — above the 468,393,760 B
cross-encoder's 53.1-56.2%, at **5.3% of the bytes**. An arm strong overall and near zero in that column
would be following the embedder rather than routing, and this one is not. Whether the disagreement can be
exploited by combining them is NOT measured here.

**3. Quantisation is free at this size class, measured two independent ways.** f16 against Q8_0, paired on
identical trials: **0.0 / −0.6 / −0.6 / −0.6 / −1.2** points across N = 3..7 — at most two trials of 168 —
for **45.6% fewer bytes**. The screen agrees without reference to the task: gap 0.6671 against 0.6672,
range 0.7640 against 0.7647. Take the Q8.

**4. `bge-small-zh` is UNMEASURED here rather than bad, and reading it otherwise is the false negative.**
The corpus is English-only and this is a 21,128-token Chinese vocabulary: it spends **2,170 tokens** on the
same 6,263-character English text that costs MiniLM **1,207** — 1.8× — so its 35-46% column prices a
vocabulary mismatch and says nothing about the model. What the row DOES establish is size and health: a
Chinese-capable embedder exists at **47,886,240 B**, loads, and screens HEALTHY.

**5. The multilingual floor is a property of the VOCABULARY, not of the role.** §3 attributed
468,393,760 B to what a cross-encoder structurally needs. Re-measured one role over, the same wall stands
in the same place: `multilingual-e5-small` Q8_0 is **132,439,008 B**, within **0.11%** of the reranker
survey's 132,584,000 B for the same XLM-R architecture. The arithmetic says why, and says quantising cannot
reach it: 250,002 × 384 = **96,000,768** embedding parameters, which at Q8_0's 8.5 bits per weight is
**102,000,816 B — over the target before a single transformer layer**. **The escape is a MONOLINGUAL
vocabulary rather than a smaller quant**, which is exactly what finding 4's 47,886,240 B is.

**6. Every sub-100 MB candidate is a 512-position model, and the size column cannot see it.** All four
reject a 6,263-character input. That disqualifies them from `IEmbedder` — called per WRITE *and* per RECALL
over entries `GraphMemoryOptions` truncates at ~6,000 characters — while leaving a short-input role open: a
tool roster, a query, a headline. It is the reranker survey's `ms-marco` disqualification in a second
costume, and it points the same way, with the SMALLER file the one ruled out.

**7. The serving pooling was measured rather than assumed, and it matters.** `all-MiniLM-L6-v2` forced to
CLS instead of its declared mean loses **45% of its cosine range** (0.7640 → 0.4213) and nearly doubles its
negative margin (−0.0602 → −0.1349). Since the GGUFs carry the trained mode, the correct action is to pass
**no `--pooling` at all** — which is what `tool-affordance`'s extra arms do.

**The STATIC class does not run here at all, and that is a RUNTIME fact rather than a size one.** No GGUF
of any `model2vec` / `potion` / `static-retrieval` model exists — the HuggingFace model API was searched
three ways and returned zero. `potion-retrieval-32M`, the retrieval-tuned member, is **129,210,456 B** of
safetensors and over the target anyway; `static-retrieval-mrl-en-v1` ships an int8 ONNX at **31,259,319 B**.
So that class is blocked on a runtime — ONNX, or a managed implementation whose hard part is tokenization —
and never on availability or size.

**Controls.** `cosine` reproduced **81.0% byte-identically at all five roster sizes in both runs**, matching
the published `affordance-prompt-protocol-4b` figure — which is what makes these cells comparable to that
grid without re-running its model arms. `loop-oracle` read 168/168 at every N through the real loop;
`loop-random` sat inside its Wilson interval of 1/N at all five; **zero ties on any scoring arm**; and
`cosine-minilm` was byte-identical across the two runs at every N.

**Trial construction is PINNED to the incumbent**, so every arm saw the same roster and only the SCORING
varied. That biases AGAINST the control rather than for it — distractors are ordered by cosine using the
incumbent's own vectors, so `cosine` meets the six selected to be hardest for itself. At N = 7 the question
does not arise: all six are shown whatever the order.

**What it does NOT say.** The corpus is SYNTHETIC, English-only and the weakest evidence tier this record
publishes; only arm differences transfer. Nothing about the memory workloads — no sub-100 MB embedder was
run on LoCoMo or LongMemEval, and finding 6 is why one cannot be without shortening what a candidate is
handed. Nothing about INGEST COST, which is where a 13× smaller model should pay most and where this grid
measures nothing. Nothing about a roster past seven. `ships=no` on both rows: no shipped default points at
any of these models.

### A STATIC embedder on the MEMORY workload: it costs nothing on the shipped default and 10 points with semantic seeds (`locomo-pair --retrieval`, 2026-09-13) <!-- result: id=locomo-retrieval-static-embedder arm="`potion-base-8M` (30,236,760 B, a CPU lookup table with no server) against the incumbent `embeddinggemma-300M` Q8_0 (333,590,944 B), same corpus, same seeded questions, every other knob identical" metric=evidence-hit@k n="200" value="54.0% against 54.5% on shipped defaults; 75.5% against 85.5% on the best mechanical arm" ships=no status=CURRENT -->

`node devtools/dev.mjs locomo-pair --embed … | --embed-endpoint … -- --n 200 --retrieval`
(`devtools/_potion/mem-gemma.log`, `mem-potion.log`).
<!-- link-ok: gitignored raw sweep output, named as provenance -->
Model-free, so **no reader is in the loop and nothing here depends on one** — which is the half
`docs/model-tasks.md` §3.3's tool-routing row could not speak to.

| arm | `embeddinggemma-300M` Q8_0 | `potion-base-8M` | Δ |
|---|---|---|---|
| `lyntai` (**the shipped default**) | 54.5% | **54.0%** | **−0.5** |
| `+forget0` | 60.0% | **60.0%** | **0.0** |
| `+forget0+oracle` | 77.5% | **77.5%** | **0.0** |
| `+sem80` | 72.0% | 68.5% | −3.5 |
| `+sem` | 77.0% | 68.5% | −8.5 |
| `+sem+rel-only` (best mechanical) | 85.5% | 75.5% | **−10.0** |
| `+sem+rel-only+oracle` | 92.5% | 88.5% | −4.0 |

**1. The embedder only matters where SEMANTIC SEEDING is on, and three arms prove it by being identical to
the decimal.** `+forget0` and `+forget0+oracle` reproduce exactly, and the shipped `lyntai` default moves by
**one question in two hundred**. Those arms do not seed semantically, so an 11.0× smaller embedder changes
nothing they do. **A deployment on the shipped defaults can swap a 333,590,944 B GPU embedder for a
30,236,760 B CPU lookup table and lose half a point.**

**2. Where it does matter, it costs about ten points** — `+sem+rel-only` is the arm this record calls the
best mechanical one, and it drops 85.5% → 75.5%.

**3. But turning semantic seeds ON is still worth far more than the embedder is worth.** With the static
model, `+sem+rel-only` reads **75.5%** against the shipped default's 54.0% — **+21.5 points** from the arm,
against −10.0 from the embedder swap. So a deployment that cannot afford a GPU embedder should still turn
the arm on rather than conclude the feature needs a bigger model.

**This is a better result for the class than the selective task suggested.** On tool routing — a task that
is PURE embedding, with nothing else contributing — the same model sat ~12 points behind
(`affordance-static-embedders`). Here the engine's other tiers carry most of the arm, so the embedder's
share of the outcome is smaller and the substitution is correspondingly cheaper. **How much an embedder is
worth is a property of the ARM, not of the embedder.**

**What it does NOT say — and the timing column is the trap.** The static run took **8,206 s** against the
incumbent's **7,871 s**, i.e. slightly SLOWER, and that is a fact about the shim rather than the model: a
Python HTTP server per call is not what an in-process lookup table would cost. **No performance claim can
be read off these runs.** Nothing here is a quality claim about the memory workloads at large either — one
corpus, one language, `evidence-hit@k` only, and the reader-facing half is untouched. `ships=no`: the
library still cannot call a static embedder at all.

### STATIC embedders, priced at last: they WORK, and a smaller transformer still beats them (`tool-affordance`, 2026-09-13) <!-- result: id=affordance-static-embedders arm="the `model2vec`/`potion` static family — a token-vector lookup table plus pooling, no transformer at inference — scoring tool routing against `all-MiniLM-L6-v2` Q8_0 and the 333,590,944 B incumbent, trials pinned to the incumbent" metric=forced-choice-accuracy n="168 trials x 5 roster sizes" value="potion 52.4-70.8% against minilm 69.6-78.6% at 19% fewer bytes" ships=no status=CURRENT -->

`node devtools/dev.mjs embed-screen --endpoint …` and `node devtools/dev.mjs tool-affordance
--scorers-only --skip-baseline --embed-endpoint …` (`devtools/_potion/*.log`).
<!-- link-ok: gitignored raw sweep output, named as provenance -->
**§3's static class had never been run at all** — no GGUF of any `model2vec` model exists, so nothing here
could serve one. A small OpenAI-shaped shim over the reference implementation makes it measurable
without a library change, which is what had to happen before anyone argues for an in-process embedder as
public surface.

| embedder | bytes | runtime | N = 3 | N = 7 |
|---|---:|---|---|---|
| `embeddinggemma-300M` Q8_0 (incumbent) | 333,590,944 | GPU server | **81.0%** | **81.0%** |
| `all-MiniLM-L6-v2` Q8_0 | 25,008,064 | GPU server | 78.6% | 69.6% |
| `LAMAR-600m` cross-encoder | 468,393,760 | GPU server | 78.6% | 76.8% |
| `potion-retrieval-32M` | 129,210,456 | **CPU, no server** | 70.8% | 60.1% |
| `potion-base-8M` | 30,236,760 | **CPU, no server** | 69.0% | 64.3% |
| `potion-base-2M` | 7,559,256 | **CPU, no server** | 63.1% | 52.4% |

**1. They work, and they are 10-20 points behind.** Every potion model screens HEALTHY
(`embed-screen --endpoint`), and — unlike EVERY sub-100 MB transformer embedder — **none has a context
limit**, because a lookup table has no positional embeddings: all three swallow the 6,263-character probe
that a 512-position model rejects. On the task, though, a **smaller** transformer beats the biggest static
model tested: 25,008,064 B of MiniLM against 129,210,456 B of `potion-retrieval-32M`, ahead at every
roster size.

**2. RETRIEVAL TUNING does not rescue the class, which is the result worth recording.** The obvious
objection to the row above is that `potion-base-*` are general-purpose distillations. `potion-retrieval-32M`
is the member tuned for this exact job and **4.3× the bytes** of `potion-base-8M` — and it reads 70.8%
against 69.0% at three options and **worse** at seven (60.1% against 64.3%). So the gap is the model class,
not the choice of member.

**3. Mean-CENTERING is REFUTED — it does not replicate on a second fixture.** <!-- result: id=affordance-centering-refuted arm="subtracting the corpus centroid before cosine — a standard anisotropy fix — as a paired variant of every embedder arm, on the HARD fixture and then on the EASY one" metric=forced-choice-accuracy n="168 trials x 5 roster sizes x 2 fixtures" value="+8/+6/+3 for the static model on hard, -1/0/-2 on easy; no cell significant on either" ships=no status=CURRENT -->
Subtracting the corpus centroid before cosine moved paired trials by **+8 / +6 / +3** for
`potion-base-8M` and **−7 / −8 / −8** for the incumbent at N = 3/5/7 on the hard fixture — consistent in
sign within every arm, and **no cell convincingly significant** across twelve comparisons. Re-run on the
`easy` fixture, where distractors come from OTHER families and so the confusable set is genuinely
different, the static model's gain becomes **−1 / 0 / −2** and every other arm moves by ≤ 6 trials at
p ≥ 0.109. **The sign flips and the magnitude vanishes**, so the original direction was a property of that
fixture rather than of the model class.

**The replication had headroom where it mattered**, which is what makes this a refutation rather than a
ceiling artifact: the static arm sits at 75-87% on the easy fixture and had room to gain. The incumbent
and MiniLM sit at 94-97% there, so the claimed HARM to a transformer is compressed and remains untested —
stated because a null with a ceiling under it is not the same null. **Do not ship centering**, and do not
re-derive it: it is cheap to apply, it is not free, and two fixtures disagree about its sign.

**What this changes about the class.** Its appeal was never quality-per-byte and this says so plainly —
**it is that it needs no server, no GPU and no port**. A 30,236,760 B CPU lookup table beside a contended
GPU is a different proposition from a 333,590,944 B resident model, and that is the trade whoever rules on
an in-process embedder is actually buying: roughly 12 points of tool-routing accuracy for zero
infrastructure. `ships=no` — nothing in the library can call one, and this shim is a bench instrument.

**What it does NOT say.** One synthetic English task, and a SELECTIVE one — nothing here prices a static
embedder on the memory workloads, where its lack of a context limit would matter most and where it has
never been run. Nothing about ingest throughput, which is where a CPU table should win hardest. The shim
is the reference implementation over HTTP, so it prices the MODEL and not any managed port of it.

### COMPLETENESS beats DEPTH: a reader wants whole items, not more of them (`memory-locomo`, 2026-09-13) <!-- result: id=locomo-qa-completeness-vs-depth arm="`lyntai-fused-full` against `lyntai-fused` — the SAME 20 items, same ranking, each carrying its whole turn instead of a 120-character headline — against `vector-40` vs `vector`, which doubles the item count instead. Three readers at a constant embedder" metric=token-f1 n="61 questions x 2 levers x 3 readers, paired per question" value="completeness +7.1 to +14.9, every CI excluding zero; depth -5.9 to +1.0, every CI spanning it" ships=yes status=CURRENT -->

`node devtools/dev.mjs locomo-pair --embed embeddinggemma-300M-Q8_0.gguf --chat <reader> -- --n 60
--arms … --dump`, six runs (`devtools/_qa/*.log`).
<!-- link-ok: gitignored raw sweep output, named as provenance -->
That command was scratch when these ran and was promoted the same day, precisely because a published
figure whose command nobody else can run is not sourced.
Two ways of spending a context budget, each isolated against the other.

**The levers are not equivalent, and the cheaper one wins.**

| lever | what changes | chars/q | 4B | 1B | 0.5B |
|---|---|---|---|---|---|
| **completeness** | 120-char headline → whole turn, SAME 20 items | 2,300 → 3,607 | **+14.9** [6.4, 23.5] | **+9.2** [1.0, 17.4] | **+7.1** [1.1, 13.2] |
| depth | 20 items → 40, same method and ranking | 3,562 → 7,028 | −2.0 [−7.6, 3.7] | −5.9 [−14.3, 2.4] | +1.0 [−5.2, 7.1] |

**1. Completeness pays at every reader size and depth pays at none.** All three completeness intervals
exclude zero; all three depth intervals span it. And completeness gets there on **half the context** the
depth arm spends — 3,607 characters against 7,028. **So the budget question is not how much text but
whether each item is intelligible**, which is the opposite of what a character count would suggest.

**2. It pays MORE the larger the reader** — +14.9, +9.2, +7.1 down the size ladder — which is the same
shape as the arm-sensitivity column: the spread across four arms is 14.7 points on the 4B, 8.8 on the 1B
and 4.7 on the 0.5B. **A smaller reader discriminates less between a good page and a bad one**, so memory
quality buys less the smaller the reader gets. That is a real limit on what any memory work can be worth
to a very small consumer, and it is worth knowing before choosing one.

**3. The mechanism is visible in the UNKNOWN column**, which is why this is not a scoring artifact: the
4B declines on 6 of 61 questions with headlines and 3 with whole turns, the 1B on 13 and 7. A truncated
item does not CONTAIN the answer, so the reader says so. (The 0.5B declines on 0 of 61 either way — it
always guesses, which is its own pathology and the reason its absolute score is not comparable to the
other two.)

**4. It already SHIPS, and that is the point.** `MemoryQuery.Detail = MemoryDetail.Full` (**D104**) is the
consumer-reachable form, and the grid runs both it and the harness isolation precisely so their AGREEMENT
is checked — they read **63.1% against 63.1%** at identical `chars/q`, so a consumer can reach what the
isolation measured. `ships=yes` means exactly that and nothing more: the option ships, and
`GraphMemoryOptions.HeadlineChars` still defaults to **120**.

**5. A second seam already said the same thing.** **D108** measured the reranker at **78.0% → 91.0%** on
whole turns against the same 120-character headlines. Different consumer, different metric, same
conclusion — 120 characters is too short for anything that has to READ the item rather than match an id.

**What it does NOT say.** It does not say raise `HeadlineChars`: that costs **+24%** of corpus content
bytes (measured on both field corpora) and changes what every recall returns, where `MemoryDetail.Full` is
per-query and costs storage nothing. It does not price the extra tokens the reader pays — `chars/q` is
reported, latency is not. One workload, one embedder, English. And the DEPTH null is a null at this
power, not a demonstration of no effect: the paired standard deviation is ~25 points, so n = 61 resolves
about 9-10 points and a 2-point depth effect would be invisible.

### A SECOND EMBEDDER on the QA half: no arm moves, and the conclusion is embedder-invariant (`memory-locomo`, 2026-09-13) <!-- result: id=locomo-qa-second-embedder arm="`nomic-embed-text-v1.5` Q8_0 (146,146,432 B) against the incumbent `embeddinggemma-300M` Q8_0 (333,590,944 B), same 4B reader, same seeded questions, four arms, paired per question" metric=token-f1 n="61 questions sampled from 1,540, seed 12345, stratified by category" value="every arm within noise: -5.5 to +2.9, all four CIs spanning zero" ships=no status=CURRENT -->

`node devtools/dev.mjs memory-locomo --n 60 --arms lyntai-fused,lyntai-fused-3shot,vector,vector-40 --dump`,
run twice behind a scratch orchestrator that serves a chosen embedder and reader on its own ports
(946 s and 981 s). This is the EMBEDDER half of `docs/task-archive.md` Part 233; the reader half is not
here and §below says why.

**The arms do not move.** Paired per question — both runs answered the identical seeded sample, so the
comparison is within-question rather than between tables:

| arm | `embeddinggemma-300M` Q8_0 | `nomic-embed-text-v1.5` Q8_0 | paired Δ | 95% CI | sign test |
|---|---|---|---|---|---|
| `lyntai-fused` | 37.2% | 32.8% | −4.4 | [−10.7, +1.9] | p = 0.227 |
| `lyntai-fused-3shot` | 44.5% | 44.4% | −0.2 | [−7.0, +6.7] | p = 0.648 |
| `vector` | 52.0% | 46.5% | −5.5 | [−12.2, +1.2] | p = 0.359 |
| `vector-40` | 50.0% | 52.9% | +2.9 | [−1.8, +7.6] | p = 0.648 |

**1. A 2.28× smaller embedder is indistinguishable here.** 146,146,432 B against 333,590,944 B, and every
CI spans zero. **State the bound with the null**: the intervals are ±7-12 points wide, so this excludes a
LARGE embedder effect and says nothing about a small one — 61 questions is what the budget bought, and the
per-question standard deviation of a token-F1 difference is 19-28 points.

**2. What the widening actually buys is the CONCLUSION, and it survives.** Under both embedders the engine's
arms sit below plain cosine: paired within each run, `vector-40` beats the best engine arm by **+5.4
[−1.3, +12.2]** under the incumbent and **+8.5 [+1.9, +15.1]** under nomic. Same sign, same order of
magnitude, one clearing zero and one not. **That is the uncomfortable summary this record already carries,
replicated on a second embedder and on a reader-facing metric** rather than on evidence-hit alone.

**3. The arm ORDERING is stable where it matters and unstable where it does not.** `lyntai-fused` is last
and `lyntai-fused-3shot` third under both. The top two swap — `vector` 52.0 / `vector-40` 50.0 becomes
46.5 / 52.9 — but they are within 2-3 points of each other in both runs, which is a near-tie either way
rather than a reordering.

**The caveat that cuts both ways: NEITHER model got its prefixes.** `IModelProvider` expresses the
distinction (`EmbeddingRole.Document` / `Query`, whose own doc names the nomic and BGE families) but the
role-aware overload has a DEFAULT BODY forwarding to the role-less one, and the bench's
`OpenAiCompatibleVectorProvider` implements only the role-less overload. Both models here are asymmetric families, so both were embedded
identically on both sides — which the interface's own documentation says costs an asymmetric model
materially. **The comparison between them is fair; both are below their own ceiling**, and lifting either
means implementing the overload in the bench double rather than changing the library.

**What it does NOT say.** Nothing about a second READER — that half ran separately, as
`docs/task-archive.md` Part 200, and it CORRECTED the framing this section was written under: the right
second reader is a SMALLER one, not a stronger one, because `--retrieval` already separates the memory
layer model-free. Seven of the eleven arms were dropped to afford two runs, including
`full`, whose window this reader exceeds (108,428 chars/q, 6 of 6 unknown at n = 6). Absolute token-F1 is
this 4B reader's and transfers nowhere; only the arm differences do. `ships=no`: `nomic-embed-text` is not
a shipped default and this library names no embedder at all.

### The NATIVE tool transport, measured: it buys REFUSAL and pays for it in accuracy (`tool-affordance`, 2026-09-13) <!-- result: id=affordance-native-transport arm="`loop-tool-native` — `qwen2.5-0.5b-instruct` Q4_K_M on NATIVE function-calling — against `loop-tool`, the SAME model on the prompt protocol `ToolLoop` authors, paired trial for trial on an identical roster" metric=forced-choice-accuracy n="168 trials x 5 roster sizes, run three times" value="-2.4 to -9.6 points at N=3..6, level at N=7" ships=no status=CURRENT -->

`node devtools/dev.mjs tool-affordance --skip-baseline --native-model …`
(`devtools/_tooltemplate/native-run1.log`, `native-run2.log`); the positive control below is
`devtools/_tooltemplate/native-probe.mjs`, which serves each model in turn on one port and sends both the <!-- link-ok: gitignored scratch, named as provenance -->
same payload.
`docs/task-archive.md` Part 236 held this blocked on a POSITIVE CONTROL. It is no longer.

**The control, first, because everything below depends on it.** <!-- result: id=native-tool-positive-control arm="`qwen2.5-0.5b-instruct` Q4_K_M against `gemma-3-4b-it` Q4_K_M, byte-identical tools payloads in ONE run on ONE build, over three tool_choice modes and both jinja settings" metric=screen-verdict n="2 models x 3 tool_choice modes x 2 jinja settings" value="qwen 6/6 emit tool_calls; gemma 0/6" ships=no status=CURRENT -->
A model whose chat template has no tool section returns HTTP 200 with `tool_calls: null` and answers from
parametric knowledge, so *"this model will not"* cannot be told from *"this build drops the array"* without
a model known to emit them. Both were sent byte-identical payloads in one run:
**`qwen2.5-0.5b-instruct` Q4_K_M (491,400,032 B) returned `finish_reason=tool_calls` on all six cells**
(default / `tool_choice: "required"` / a named `tool_choice`, each with and without `--jinja`), and
**`gemma-3-4b-it` returned `null` on all six**. The build is fine; the model is the cause. Two things that
corrects: **`--jinja` is byte-irrelevant here for BOTH models**, and **`tool_choice: "required"` does bind**
where the template supports it, so its failure on gemma-3 is that model's property rather than the wire
format's.

**The comparison is PAIRED on one model** — the same weights answer the same trial over the same roster,
once through each transport. The published grid's two models cannot take the native path at all, so a
native-only arm would have confounded the transport with the model.

| | prompt protocol | native function-calling |
|---|---|---|
| accuracy, ALL trials (N = 3 → 7) | 60.7% → 48.8% | 55.4% → 49.4% |
| fired | 93-96% | **69-78%** |
| no tool invoked | 3.0-6.0% | **21.6-30.1%** |
| hallucinated a tool name | 0.6-1.2% | **0.0%** |
| unusable arguments | 0.6-2.4% | **0.0%** |
| **converged** (the loop finished) | **11.3-24.4%** | **99.4-100%** |
| model calls per run | 2.34-2.54 | 1.70-1.79 |

**1. On ACCURACY the native transport COSTS 2.4-9.6 points at N = 3..6, and is level at N = 7.** Three
runs, and the per-cell deltas barely move: **−7.7 / −5.3 / −6.5** at N = 3, **−3.0 / −2.4 / −2.4** at
N = 4, **−8.4 / −9.5 / −9.5** at N = 5, **−9.6 / −7.8 / −8.4** at N = 6, **+1.2 / +0.6 / −0.6** at N = 7.
Per-cell McNemar hovers at p = 0.03-0.70 because ~50 discordant trials cannot resolve a 10-trial net —
**the replication across three runs is the evidence here, not any one p-value.** The cost is entirely the
declines in finding 2; it is not that the native arm chooses worse when it chooses.

> _Corrected 2026-09-13, within hours, by the third run._ This finding first read *"on ACCURACY it is a
> wash, and the run that said otherwise did not replicate"*, on the reasoning that the only cell under
> p = 0.05 was a different cell in each of two runs. **Two runs were not enough to say "nothing there".**
> The per-cell NET was stable across both all along (−14 then −16 at N = 5; −16 then −13 at N = 6) and the
> p-values moved while the effect did not — so the read was taken from the noisiest statistic on the table
> instead of the steadiest one. **A p-value that wanders across runs is not evidence of no effect; it is
> evidence the test is underpowered.** Compare the effect sizes first.

**2. The transports fail in opposite directions, and THAT is the result.** Native declines about five times
as often — and in exchange it never invents a tool name and never emits arguments the registry cannot use,
where the prompt path does both on 1-3% of trials. A deployment choosing between them is not choosing
accuracy; it is choosing which failure it would rather handle.

**3. The prompt protocol's real cost on a 0.5B is CONVERGENCE, and it is the largest effect here.** It
picks a tool on 94-96% of trials and then **finishes the loop on 11-24% of them**, against native's
99.4-100%. The model emits a well-formed `{"tool": …}` and then cannot emit a well-formed `{"final": …}`
after the observation comes back. That also explains the call counts: 2.34-2.54 per run against 1.70-1.79,
the difference being `CompleteJsonAsync`'s repair round, **a real second billed call**. The published grid
could not see this — its models are 1.6× and 5× larger.

**4. The DECLINE rate is real, and bounded rather than assumed.** A turn the output cap cuts off before any
call is emitted returns an empty `tool_calls`, which is byte-identical to a model choosing not to call one.
Counted: **16 of 1,469 native turns (1.09%)**, so at most a point of the 22-30% is the harness. The
declines are also spread evenly over all six tool families (7-11 of 28 each), not clustered on one.

**5. The accuracy over FIRED trials is HIGHER natively — and it is not quotable.** 75.0% against 64.6% at
N = 3. The harness marks it UNREADABLE on its own rule, because an arm firing on 69-78% of trials has
selected which trials it answers. Recorded because the direction is suggestive, and flagged because it is
the exact shape a reader would over-read.

**6. And the finding that reaches furthest is about the OTHER model.** §3.1 of `docs/model-tasks.md`
concluded that nothing at or under the ~500 MB sizing target had been shown to choose a tool generatively,
on a 806,058,240 B `gemma-3-1b-it` reading **10.1% at N = 3 falling to 3.0% at N = 7**. The same arm, same
corpus, same harness, on a **491,400,032 B** `qwen2.5-0.5b-instruct` reads **60.7% → 48.8%** — roughly
**six times** the score at **61% of the bytes**. **That 1B result was a property of that model, not of its
size class**, and §3.1 has been corrected. Nothing here says smaller is better; it says the family and the
tool training dominate the byte count at this scale.

**Controls, all passed, in both runs.** `loop-oracle` 168/168 chosen and converged at every roster size
through the real loop; `loop-random` inside its Wilson interval of 1/N at all five; prompt size rose
monotonically on every arm **including the native one**, whose roster travels in `req.Tools` rather than in
message content and is counted there for exactly that reason; endpoint failures **3 of 4,200 model trials
(0.07%)**. `cosine` read **81.0% at all five sizes for the third independent run**, which is the anchor
that makes these cells comparable to the published grid.

**7. AND THE DECLINES ARE THE POINT — the native transport is the first thing measured here that bounds <!-- result: id=affordance-native-false-calls arm="`loop-tool-native` against `loop-tool`, the SAME `qwen2.5-0.5b-instruct` Q4_K_M, on the 20 requests NO tool in the roster serves" metric=false-call-rate n="20 negative requests x 5 roster sizes, both transports" value="20-30% native against 90-100% prompt" ships=no status=CURRENT -->
an affordance task at all.** On the 20 requests nothing on the roster serves:

| roster size | N=3 | N=4 | N=5 | N=6 | N=7 |
|---|---|---|---|---|---|
| prompt protocol — invoked a tool anyway | 90.0% | 90.0% | **100.0%** | 95.0% | 95.0% |
| **native function-calling** | **20.0%** | **20.0%** | **20.0%** | **25.0%** | **30.0%** |

**And it is DISCRIMINATION, not a constant decline rate** — which is the check that matters, because an
arm that declines everything scores perfectly here and is useless. The same model fires on **70-78%** of
trials where a tool DOES fit and on **20-30%** where none does: about **50 points of separation**. The
prompt protocol fires on 95-98% and 90-100% respectively — **separation of −2 to +6 points, which is
none at all.** It is not deciding; it is calling a tool.

The captured replies say the same thing. Given *"What is a reasonable notice period to give when
resigning?"* with no matching tool, the prompt path called
`list_deployments {"environment": "your_environment_name"}` — a schema placeholder passed through as an
argument — while the native path answered directly. Three of the first four negative requests went the
same way.

**This is the first lever found on §2's hardest case.** `docs/model-tasks.md` §2 records that an affordance
task *"cannot be bounded by the model AT ALL"* and that two prompt rewrites in opposite directions moved a
4B by nothing, leaving *narrow the roster first* as the only remedy. That was measured entirely through the
PROMPT protocol, and it replicates here on a second model and size class (90-100%). **The TRANSPORT is a
second lever, and a large one** — 65-75 points — bought for 2.4-9.6 points of accuracy where a tool does
fit. Whether that trade is worth taking depends on what a false call costs the deployment, which is exactly
the shape §2 says to decide structurally rather than by prompt.

**What it does NOT say.** One model, one family, one quantisation — this is a transport comparison, not a
model comparison, and a second tool-capable model might trade differently. **The negative corpus is 20
requests**, so a 20% cell is 4 trials and its interval spans roughly 6-44%: the 65-75 point GAP is far
outside that, the individual cells are not. The corpus is SYNTHETIC and English-only. Nothing about a
roster past seven, where the declines might behave differently. Nothing about streaming tool calls —
`SupportsStreamingToolCalls` is deliberately false here. And nothing about whether a model that can use
the native transport should also be given the prompt one as a fallback, which is what `ToolLoop` does
today for a provider that declares no support.

### COMPLETENESS is BUDGET-DEPENDENT on the suppression workload, and at a tight budget it COSTS (`memory-longmemeval --shots --budget --detail`, 2026-09-13) <!-- result: id=longmemeval-ku-completeness-budget arm="`full` — the shipped recall at k = 10 and the shipped CandidateMultiplier asking for MemoryDetail.Full — against `shot-1`, the SAME items in the same order rendered as 120-character headlines. Three character budgets, one ingestion" metric=clean n="70 questions, haystack, paired arm-for-arm within one run" value="-11.4 at 1,200; +8.6 at 5,400; EXACTLY 0.0 at 20,000 where the cap binds on neither" ships=no status=CURRENT -->

`node devtools/dev.mjs memory-longmemeval --shots --budget 1200,5400,20000 --detail --haystack`, with
`LYNTAI_LIVE_MODEL_URL` on an embedder this session started and owned. 2,717.5 s, 34,296 embedder calls.
Raw output: the gitignored `devtools/_lme-pair/haystack-detail.log`
<!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.

`locomo-qa-completeness-vs-depth` priced the same lever on a SEARCH workload with a reader and found it
worth **+7.1 to +14.9** token-F1 at every reader size. This is the opposite workload — LongMemEval's
knowledge-update class scores whether the context holds the revised fact and NOT the one it superseded —
and the lever does not behave the same way there.

| budget | `shot-1` `clean` | `full` `clean` | Δ | `full` `current@k` | `full` `stale@k` | `full` items/q |
|---:|---:|---:|---:|---:|---:|---:|
| 1,200 | 44.3% | 32.9% | **−11.4** | 47.1% (−35.8) | 17.1% (−27.2) | 2.9 |
| 5,400 | 44.3% | 52.9% | **+8.6** | 75.7% (−7.2) | 28.6% (−15.7) | 6.3 |
| 20,000 | 44.3% | **44.3%** | **0.0** | 82.9% (0.0) | 44.3% (0.0) | 10.0 |

**1. The 20,000 row is the CONTROL, and it is the most important cell here.** The cap binds on neither arm,
so `full` returns the same ten items as `shot-1` carrying **11,349 characters against 1,168** — 9.7× the
text — and reproduces it **to the decimal on all three quality columns**. That is the confound
`docs/task-archive.md` Part 233 was filed about, shown ABSENT by measurement rather than argued away: this
class is scored by a turn tag at character 0 that survives truncation, so the scorer cannot see the extra
text. **Any movement in the other two rows is therefore the budget admitting fewer items, never the
scorer's eyesight.**

**2. At a tight budget whole items STARVE the page, and that half is not close.** At 1,200 the arm fits
**2.9 items against 10.0**, and `current@k` falls **82.9% → 47.1%** — 25 of 70 questions losing the current
fact outright. `stale@k` falls too, but losing both facts is not suppression; it is retrieving less. The
`clean` column's −11.4 understates what happened, which is why the supporting columns are printed beside it.

**3. The 5,400 row is where the lever could pay, and it is the one this run cannot settle.** `clean` +8.6
is about six questions on a 70-question sample, and this table carries no interval — `RunShotsAsync` keeps
no per-question outcomes, so unlike the one-shot runner it cannot compute a paired test. The supporting
columns point the same way (`stale@k` −15.7 against `current@k` −7.2: it sheds the superseded fact about
twice as fast as the current one), which is a mechanism rather than a confirmation. **Treat +8.6 as
unreplicated and within what this n resolves.**
<br>**SETTLED the same day by a finer ladder** (`longmemeval-ku-completeness-peak`): the cell is not noise,
it sits on a smooth curve that rises over four rungs to a peak of **54.3% at budget 4,200** and falls away
on both sides. The sentence above stands as the honest reading of THIS table — three rungs cannot tell a
curve from a wobble — and the ladder is what could.

**What it means for the recommendation.** *"Completeness, not count"* is not wrong, but it is not
workload-free either: on this class the lever is worth something only in the band where the cap trims the
tail without cutting into the current fact. Too tight and it starves; too loose and it is exactly free.
`docs/deployment-shapes.md` now carries that caveat.

**What this does NOT say.** It does not price completeness for a READER on this class — no model was in the
loop, so this is what the CONTEXT contains, not what an answer scores. It does not touch `fill`, which at
**61.4%** `clean` at budget 1,200 remains the best arm on this workload and beats every `full` cell here.
One embedder (`embeddinggemma-300M` Q8_0, 333,590,944 B), one corpus, one seed, three budget values chosen
around the published pair rather than swept. **No cell here is comparable to the 2026-09-06/07 budget
tables**, which were Ollama-served `nomic-embed-text`: the comparison that carries is `full` against
`shot-1` WITHIN this run, which is paired by construction. 48 inputs were truncated to 6,000 characters by
the harness's own cap, identically for every arm.

### A character cap on WHOLE items is a suppression FILTER with an optimum — `clean` peaks at 4,200 (`memory-longmemeval --shots --budget --detail`, 2026-09-13) <!-- result: id=longmemeval-ku-completeness-peak arm="`full` — the shipped recall asking for MemoryDetail.Full — swept across six character budgets against `shot-1`, which averages 1,168 characters and therefore never binds at any rung" metric=clean n="70 questions, haystack, six budgets over one ingestion" value="a monotone rise 32.9 to 54.3 percent peaking at budget 4,200, then falling back to shot-1's 44.3 percent once the cap stops binding" ships=no status=CURRENT -->

`node devtools/dev.mjs memory-longmemeval --shots --budget 1200,2000,3000,4200,5400,20000 --detail
--haystack`, 2,988.1 s. Raw output: the gitignored `devtools/_lme-pair/ladder.log`
<!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Run because `longmemeval-ku-completeness-budget` left its one POSITIVE cell unreplicated at three rungs,
and a second seed cannot replicate it — all 70 questions run, so the sample is deterministic. A finer
ladder tests the SHAPE instead, which is the available replication.

**Two controls, and both hold exactly.** `shot-1` reads **44.3% / 82.9% / 44.3% / 10.0 items / 1,168 chars
at all six rungs** — identical to the decimal, because its body never reaches even the smallest cap. And
`full@20000` reproduces `shot-1` on every quality column for a second time, independently. Every cell
shared with the three-rung run reproduces byte for byte.

| budget | `full` `clean` | `current@k` | `stale@k` | items/q | vs `shot-1` |
|---:|---:|---:|---:|---:|---:|
| 1,200 | 32.9% | 47.1% | 17.1% | 2.9 | −11.4 |
| 2,000 | 40.0% | 55.7% | 22.9% | 3.6 | −4.3 |
| 3,000 | 48.6% | 62.9% | 20.0% | 4.7 | +4.3 |
| 4,200 | **54.3%** | 75.7% | 27.1% | 5.6 | **+10.0** |
| 5,400 | 52.9% | 75.7% | 28.6% | 6.3 | +8.6 |
| 20,000 | 44.3% | 82.9% | 44.3% | 10.0 | 0.0 |

**1. The positive cell was not noise — it sits on a curve.** Four rungs rise into the peak (+7.1, +8.6,
+5.7) and two fall away from it, crossing `shot-1` between 2,000 and 3,000. That is the pre-registered
*smooth peak* outcome rather than the *rungs jumping around* one, and it is what the earlier table could
not distinguish.

**2. The mechanism is in the two columns beside it, and the cap is doing the work.** `current@k` rises
monotonically with the budget (47.1 → 82.9) and so does `stale@k` (17.1 → 44.3): a bigger allowance simply
finds more of both. `clean` peaks where the GAP between them is widest — **48.6 points at 4,200** against
38.6 at 20,000. So the cap is not paying for completeness; it is acting as a **filter**, and whole items
are what make it selective, because they spend the allowance fast enough to cut the tail the stale fact
lives in.

**3. The honest counterweight, and it is decisive.** `fill` at budget 1,200 reads **61.4%** — better than
`full`'s best cell at **3.5× less context**. Completeness at its own optimum is still not the best lever on
this workload; a deeper first recall trimmed hard remains it, which is what `longmemeval-ku-pool16-b1200-n70`
already said. **Read this as characterising the lever, never as recommending it.**

**What it does NOT say.** The peak's LOCATION is a property of this corpus, not a constant: it sits where
whole-item length meets the cap, and whole items here average ~1,130 characters. Still one embedder
(`embeddinggemma-300M` Q8_0), one corpus, and still no interval per cell — the shape carries the claim, not
any single rung. `fill` above 2,000 collapses (24.3% at 3,000 to 2.9% at 5,400), which is the published
inversion rather than a new finding.

### The COUNTER-TEST: on coverage the same cap is a pure TAX, so completeness is a filter one way and a cost the other (`memory-longmemeval --shots --temporal --budget --detail`, 2026-09-13) <!-- result: id=longmemeval-temporal-completeness-tax arm="`full` — the shipped recall asking for MemoryDetail.Full — against `shot-1` on the TEMPORAL class, the same six character budgets as the knowledge-update ladder" metric=all-evidence-recall n="132 questions, haystack, six budgets over one ingestion" value="negative at every binding rung, -24.3 to -10.6 points, never crossing headline parity and reaching exactly 0.0 only at 20,000 where the cap stops binding" ships=no status=CURRENT -->

`node devtools/dev.mjs memory-longmemeval --shots --temporal --budget 1200,2000,3000,4200,5400,20000
--detail --haystack`, 6,139.5 s, 64,905 embedder calls. Raw output: the gitignored
`devtools/_lme-pair/temporal.log`
<!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
`longmemeval-ku-completeness-peak` found an optimum on the class that scores SUPPRESSION. This is the
class the harness already calls its counter-test: temporal questions usually need every flagged turn, so
the metric is all-evidence recall and a cap can only lose evidence. **The prediction was registered before
the run — monotone worse, no peak — and it held.**

**The controls hold for a third time.** `shot-1` reads **37.9% / 77.3% / 54.1% / 10.0 items / 1,174 chars
at all six rungs**, and `full@20000` reproduces it on every quality column at **12,091 characters**. Three
independent runs across two classes have now put the vacuity control on the table and it has never moved.

| budget | `full` all-evidence@k | vs `shot-1` | items/q | for contrast: `full` on knowledge-update |
|---:|---:|---:|---:|---:|
| 1,200 | 13.6% | −24.3 | 2.4 | −11.4 |
| 2,000 | 23.5% | −14.4 | 3.4 | −4.3 |
| 3,000 | 22.0% | −15.9 | 4.5 | +4.3 |
| 4,200 | 28.0% | −13.9 | 5.3 | **+10.0** |
| 5,400 | 27.3% | −10.6 | 6.2 | +8.6 |
| 20,000 | 37.9% | 0.0 | 10.0 | 0.0 |

**1. The rule is symmetric, and this is the sentence worth carrying.** A character cap on whole items is a
**FILTER where the workload wants suppression and a TAX where it wants coverage**. It never crosses parity
here — the best binding rung is still −10.6 — because trimming the tail can only remove flagged turns when
every flagged turn is wanted. The knowledge-update peak was never about completeness being good; it was
about the cap removing the superseded fact, and this class has nothing it is helpful to remove.

**2. Two independent reproductions arrive with it, neither of them the point of the run.** `fill` INVERTS
direction between the classes exactly as `longmemeval-ku-fill-k80-b1200-n70` reported — rising 22.0 → 72.7
with the budget here, collapsing 61.4 → 2.9 there. And plain cosine, which is close to useless on
suppression (11.4% `clean`), reads **94.7%** all-evidence at 20,000 and beats every engine arm from 2,000
up. Both are the workload inversion this record already carries, reproduced on a different embedder.

**What it does NOT say.** It does not price either class for a READER — no model was in the loop, so this
is what the CONTEXT holds. It does not make `full` uniformly bad: at a non-binding budget it is exactly
free on both classes, which is what the control establishes. One embedder
(`embeddinggemma-300M` Q8_0), one corpus, no interval per cell — the SHAPE carries the claim, and the
shape here is the absence of a crossing rather than the location of one. 82 inputs were truncated to 6,000
characters by the harness's own cap, identically for every arm.

### The embedding tool-selector at CATALOGUE scale: it decays gently and does not collapse (`tool-affordance --roster`, 2026-09-13) <!-- result: id=affordance-roster-catalogue arm="`cosine` — a 333,590,944 B embedder scoring tool descriptions, argmax over the roster — swept from 3 to 35 options on the `easy` fixture, where distractors are drawn from OTHER families" metric=forced-choice-accuracy n="168 trials at each of six roster sizes, nested so the cells share distractors" value="96.4 percent at 3 options falling to 81.5 percent at 35, while chance falls 33 to 3 percent and lift rises 2.89x to 28.54x" ships=no status=CURRENT -->

`node devtools/dev.mjs tool-affordance --scorers-only --difficulty easy --roster 3,7,14,21,28,35`, 6.8 s,
model-free. Raw output: the gitignored `devtools/_lme-pair/roster.log`
<!-- link-ok: gitignored raw sweep output, named as provenance for the table below -->.
Run because `docs/task-archive.md` Part 236 asked for a bound on the tool roster and its grid stopped at
seven, where a bound does not yet matter.

| N | `cosine` | 95% CI | chance | lift |
|---:|---:|---|---:|---:|
| 3 | 96.4% | [92%, 98%] | 33% | 2.89× |
| 7 | 94.0% | [89%, 97%] | 14% | 6.58× |
| 14 | 87.5% | [82%, 92%] | 7% | 12.25× |
| 21 | 85.7% | [80%, 90%] | 5% | 18.00× |
| 28 | 83.9% | [78%, 89%] | 4% | 23.50× |
| 35 | 81.5% | [75%, 87%] | 3% | 28.54× |

**1. A twelvefold roster costs about fifteen points, not the arm.** The free model-free selector still picks
the right tool from thirty-five on four trials in five. Read the LIFT column for the shape: it rises almost
linearly with N, so the arm is losing far less than chance is.

**2. The ceiling is a property of the FIXTURE, and the two difficulties differ structurally.** `hard` draws
distractors from the gold tool's OWN family, which holds seven — so it cannot pose a roster above seven at
all, and that is the design rather than a limit to raise. `easy` draws from every other family, so its
ceiling is the rest of the corpus. The harness now REFUSES an over-large roster instead of silently
building zero trials, which is what it did before the guard existed.

**3. So the number is an optimistic bound, and that is the honest reading.** Beyond the first handful the
added options are cross-family, i.e. semantically distant — a real catalogue is a mix, and no corpus of real
rosters exists here. **Do not compare these cells to the published 81.0%**, which is the `hard` fixture at
3-7 options; the fixtures are different tasks and the numerical coincidence at 35 is exactly that.

**What it does NOT say.** It measures argmax accuracy, never recall@k — a selector narrowing a catalogue to
k would be scored on the latter and would read higher, so this bounds the seam's feasibility from BELOW.
The gold occupies only slots 13-24 at N = 35, so the position table covers part of the roster rather than
all of it. One embedder, one synthetic English fixture, no model in the loop.

### RETRACTED the same day — a real annotator drifts, but this run measured it through a context no deployment has (`memory-annotation-drift`, 2026-09-15) <!-- result: id=annotation-drift-three-models arm="the SHIPPED `LlmMemoryAnnotationPolicy` against three local chat models — `qwen2.5-0.5b-instruct` q4_k_m (491,400,032 B), `gemma-3-1b-it` Q4_K_M (806,058,240 B), `gemma-3-4b-it` Q4_K_M (2,489,757,856 B) — 8 entity clusters x 4 facts, English and Chinese" metric=drift-rate n="24 eligible facts per model per language (32 annotated, 8 anchors)" value="41.7% to 87.5%; no monotone relationship with model size" ships=no status=RETRACTED -->

**The mechanism's CEILING was measured with a perfect annotator and the live test asserts only that SOME
handle is shared by two of three facts. Neither is a rate, and the rate is bad.** Two facts link because
their subjects MATCH, so a model that phrases the same entity differently each write connects nothing —
DRIFT is how often it does that when the anchor handle was in `Known` and reuse was there for the taking.

| model | on disk | lang | drift | collapse | empty | distinct handles |
|---|---:|---|---:|---|---:|---:|
| **PERFECT (self-check)** | — | both | **0.0% (0/24)** | 0 of 8 | 0 | 8 |
| `qwen2.5-0.5b-instruct` | 491,400,032 B | english | 62.5% (15/24) | 1 of 35 | 0 | 35 |
| `qwen2.5-0.5b-instruct` | 491,400,032 B | chinese | 70.8% (17/24) | 0 of 47 | 0 | 47 |
| `gemma-3-1b-it` | 806,058,240 B | english | 76.2% (16/21) | 1 of 22 | 3 | 22 |
| `gemma-3-1b-it` | 806,058,240 B | chinese | 58.3% (14/24) | 1 of 21 | 0 | 21 |
| `gemma-3-4b-it` | 2,489,757,856 B | english | **87.5% (21/24)** | 5 of 14 | 0 | 14 |
| `gemma-3-4b-it` | 2,489,757,856 B | chinese | **41.7% (10/24)** | 2 of 8 | 0 | 8 |

**SIZE IS NOT THE LEVER.** 5x the bytes makes English WORSE (62.5% → 87.5%) and Chinese BETTER
(70.8% → 41.7%). There is no monotone relationship in either direction, and the best English model is the
worst Chinese one — so **a deployment cannot pick an annotator by size, and cannot carry a choice across
languages.** That is the same shape §3 already records for the selective seams: the model, never the size.

**The two failure modes are OPPOSITE, and the handle count names them.** Distinct handles over the same 32
facts run 35/47 → 22/21 → 14/8 as the model grows. The 0.5B invents a near-unique handle per fact — too
SPECIFIC to ever match. The 4B generalises to topic-level handles that span unrelated clusters (5 of 14
collapse in English against 1 of 35 for the 0.5B) — too GENERIC to distinguish. Both fail to link, and a
prompt fix aimed at one would move the other the wrong way.

**Three controls, because drift alone is vacuous.** A model answering one handle for everything drifts 0%
and links everything; one answering nothing drifts 0% and links nothing. So COLLAPSE and EMPTY are reported
beside it, and the **self-check gates the table**: a perfect annotator scores 0.0% drift, 0 collapse, 0
empty and exactly 8 handles for 8 clusters, which is what separates "models drift" from "the scorer is
broken". Every figure above reproduced exactly on a second run of all three models.

**What this does NOT say.** The fixture is SYNTHETIC — the weakest evidence tier here — so read the
directions and not the magnitudes. It measures the annotator alone: no engine, no store, no recall, so it
says nothing about how much recall quality the drift actually costs, which is the question
`memory-annotation`'s ceiling answers from the other end. Three models, all local and quantised, all
small-to-mid; nothing here is evidence about a frontier model. And the drift reference is the cluster's
FIRST labelled fact, which is the item's own definition ("invents past a perfectly good existing subject")
and is stricter than "the cluster is connected at all". `ships=no`.

### REFUTED: code cannot reconcile annotator drift — string matching recovers it in 1 of 6 cells and over-links in another (`memory-annotation-drift`, 2026-09-15) <!-- result: id=annotation-drift-code-reconciler arm="a pure-CODE reconciler over the SAME model answers — Exact (shipped) vs +Containment (the script-aware rule `MemorySubject.Matches` already applies on the recall side) vs +Fragment (shared word, or shared 2-gram in a spaceless script) — three models x two languages" metric=drift-rate n="24 eligible facts per cell, one model pass scored by all three rungs" value="no change in 5 of 6 cells; 70.8% → 58.3% in the sixth; collapse 2 → 3 in another" ships=no status=CURRENT -->

**This is a REFUTATION, and it was run to answer a direct question: can CODE fix what the model does badly,
instead of buying a better model?** For this seam the answer is no. It is filed so nobody spends a second
session building the normalizer.

| model | lang | Exact (shipped) | +Containment | +Fragment | collapse |
|---|---|---:|---:|---:|---|
| `qwen2.5-0.5b-instruct` | english | 62.5% | 62.5% | 62.5% | 1 of 35, unchanged |
| `qwen2.5-0.5b-instruct` | chinese | 70.8% | 66.7% | **58.3%** | 0 of 45, unchanged |
| `gemma-3-1b-it` | english | 76.2% | 76.2% | 76.2% | 1 of 22, unchanged |
| `gemma-3-1b-it` | chinese | 58.3% | 58.3% | 58.3% | 1 of 21, unchanged |
| `gemma-3-4b-it` | english | 87.5% | 87.5% | 87.5% | 5 of 14, unchanged |
| `gemma-3-4b-it` | chinese | 41.7% | 41.7% | 41.7% | **2 → 3 under Fragment** |

**Why it fails, from the dumped handles** (`--dump`): the drift is SEMANTIC, not lexical. Against
`anchor={Mr Chen}` the model answers `{standup}`, `{bank}`, `{email}`; against `{dog | Biscuit}` it answers
`{walk}`, `{veterinarian}`; against `{acoustic guitar}`, `{sixth string}`, `{father}`, `{scratch | body}`.
**Not one English drift is a spelling, plural, article or substring variant** — there is nothing for a
matcher to match. The model is answering *what is this sentence about* locally and naming the most salient
noun present, rather than *which entity does this belong to*.

**The one cell that moved shows the mechanism, and it is a property of the SCRIPT.** Chinese compounds by
concatenation, so the head noun survives as a literal substring — `水文学` inside `水文学论文`, `原声吉他`
inside `父亲的原声吉他` — and containment catches it. English swaps the head noun for a pronoun ("he runs
the standup"), so it is simply absent. **A code-side matcher can only recover drift the script left
recoverable**, which is why this reads as a language finding rather than a technique that needs tuning.

**The aggressive rung is not free.** `+Fragment` raised `gemma-3-4b-it`'s Chinese collapse from 2 to 3 of
10 handles while recovering no drift — the failure mode `docs/memory.md` warns about, bought for nothing.
A larger model answers broad topic-level handles, so a shared fragment ties unrelated clusters together.

**What this does NOT refute.** Only the FIRST of the three signals the field uses for entity resolution
(name similarity; co-occurrence; recency). Co-occurrence cannot bridge these either — the drifted handles
never appear in the same answer as the anchor — but **recency is untested and remains the open candidate**,
and this fixture cannot test it: its clusters are written consecutively by construction, so any
adjacency rule would score brilliantly here and could over-link badly on an interleaved stream. Measuring
it honestly needs an INTERLEAVED fixture, which does not exist yet. `ships=no`, and nothing changed.

### The corrected run: drift is WORSE than reported, and SIZE DOES HELP — the earlier ranking was an artifact of the harness's own context (`memory-annotation-drift`, 2026-09-15) <!-- result: id=annotation-drift-corrected-context arm="the same three models and fixture through the ENGINE's real annotation context — `Recent` global to the task/scope and bounded to `AnnotationContext` = 8, `Known` bounded to `AnnotationKnownSubjects` = 24" metric=drift-rate n="20-24 eligible facts per cell (32 annotated, 8 anchors), consecutive write order" value="83.3% to 90.5% English, 58.3% to 85.0% Chinese; the 2,489,757,856 B model best in BOTH" ships=no supersedes="annotation-drift-three-models" status=CURRENT -->

**The retracted run built the annotator's context itself and built the wrong one.** It scoped `Recent` per
CLUSTER and left it unbounded, where `GraphMemoryEngine.AnnotateAsync` passes the **8 most recent entries
in the task and scope, globally**; and it showed every accumulated handle where the engine shows
`AnnotationKnownSubjects` = 24. Both make the model's job easier than any deployment will.

| model | on disk | english | chinese | was (retracted) |
|---|---:|---:|---:|---|
| `qwen2.5-0.5b-instruct` | 491,400,032 B | 87.0% (20/23) | 85.0% (17/20) | 62.5% / 70.8% |
| `gemma-3-1b-it` | 806,058,240 B | **90.5%** (19/21) | 83.3% (20/24) | 76.2% / 58.3% |
| `gemma-3-4b-it` | 2,489,757,856 B | **83.3%** (20/24) | **58.3%** (14/24) | 87.5% / 41.7% |

**Drift is worse in five of six cells**, and the headline is the opposite of the retracted one. **"Size is
not the lever" and "the best English model is the worst Chinese one" are both REFUTED**: the 2.49 GB model
is now best in BOTH languages, and the two small models sit within a few points of each other above it.

**The distortion was not uniform, which is the part worth carrying.** A cleaner context helped the SMALL
models most — qwen gained 24.5 points of apparent quality from it and the 4B lost 4.2 — so the defect did
not shift a level, it **inverted a ranking**. A harness that assembles a model's input has to assemble the
one the engine assembles; `.claude/knowledge/pitfalls.md` carries it.

**What survives the retraction**: that drift is high everywhere (58.3% at best, and 83-90% in five cells),
that the perfect-annotator ceiling is therefore a figure to read with a discount, and the mechanism the
handle counts name — a small model answers near-unique handles, a large one broad ones. What does not
survive is any claim about size or about a language inversion.

### REFUTED: recency is a FIXTURE ARTIFACT — it looks like a 52-point win when clusters are written consecutively and collapses half the handle space when they are not (`memory-annotation-drift --gap`, 2026-09-15) <!-- result: id=annotation-drift-recency-gap arm="a pure-CODE recency rule — carry the PREVIOUS write's subjects onto a fact that refers by pronoun and ties to nothing known — over an INTERLEAVED fixture at gaps 0, 1 and 7" metric=drift-rate n="20-24 eligible facts per cell, one model pass per gap scored by every reconciler" value="gap 0: 87.0% → 34.8% at no collapse cost; gap 1: 100% → 91.7% with collapse 0 → 24 of 47 handles" ships=no status=CURRENT -->

**The third and last of the cheap entity-resolution signals, and the fixture was built specifically to stop
it lying.** `--gap` is how many other clusters' facts fall between two consecutive facts of one cluster: 0
is the old consecutive order, 7 is a full round-robin over the eight clusters.

| model | lang | gap | Exact | +Recency | collapse under Recency |
|---|---|---:|---:|---:|---|
| `qwen2.5-0.5b-instruct` | english | 0 | 87.0% | **34.8%** | 3 of 40 — unchanged |
| `qwen2.5-0.5b-instruct` | english | 1 | 100.0% | 91.7% | **0 → 24 of 47** |
| `qwen2.5-0.5b-instruct` | english | 7 | 95.8% | 95.8% | **1 → 19 of 42** |
| `gemma-3-1b-it` | english | 0 | 90.5% | 66.7% | 2 of 38 — unchanged |
| `gemma-3-1b-it` | english | 1 | 100.0% | 90.0% | **1 → 10 of 31** |
| `gemma-3-1b-it` | english | 7 | 81.8% | **86.4% — worse** | **0 → 15 of 34** |

**At gap 0 it is the best result in this whole file: 87.0% → 34.8%, a 52-point fall, free.** At gap 1 it
buys 8 points and collapses **half the handle space**. At gap 7 it buys nothing on one model, makes the
other WORSE, and still collapses 15 to 19 handles. The rule is carrying the previous write's subjects
across a cluster boundary, which at gap 0 is always the same entity and never is otherwise.

**So the code-side direction is now closed on all three signals.** Name similarity was refuted by
`annotation-drift-code-reconciler`; co-occurrence dies on the same dumped answers, because a drifted handle
never appears alongside its anchor; recency is this. **Subject linking is model-bound** — the lever is the
annotator's own quality, or asking it a different SHAPE of question, not code over its output.

**The methodological half is the more valuable one.** Had this been measured on the consecutive fixture and
shipped, it would have read as the largest single win in the memory subsystem — and it is an artifact of
the write order. **A harness whose fixture orders writes in a way a real stream does not can manufacture
any adjacency result you like.** `ships=no`; nothing changed.

### REFUTED: reshaping annotation to `select-from-list` trades drift for COLLAPSE — the 4B degenerates to ONE handle for eight entities (`memory-annotation-drift --gap 7`, 2026-09-15) <!-- result: id=annotation-shape-select-from-list arm="a bench-local selective annotator — number the handles already in use, ask for {pick:n} or {new:handle} — against the SHIPPED generative policy on the interleaved fixture, at the shipped list length (24) and a short one (8)" metric=drift-rate n="4-24 eligible facts per cell at gap 7, 32 annotated" value="0.0% drift at 1 of 1 handles on the 4B; 83.3-100% at 3-5 of 4-6 handles on the 0.5B" ships=no status=CURRENT -->

**The fourth and last candidate lever, and the one with the best prior.** The shipped prompt already says
*"REUSE an existing subject … Copy it EXACTLY"* and every model drifts anyway, so the hypothesis was that
reuse should be STRUCTURAL rather than instructed — `docs/model-tasks.md` §1's own prescription for a
generative task that must reuse. It is not.

| model | lang | shape | drift | collapse | empty |
|---|---|---|---:|---|---:|
| `gemma-3-4b-it` | english | extract (shipped) | 91.7% | 7 of 14 handles | 0 |
| `gemma-3-4b-it` | english | **select@24** | **0.0% (0/16)** | **1 of 1 handles** | 8 |
| `gemma-3-4b-it` | chinese | extract (shipped) | 66.7% | 5 of 15 handles | 0 |
| `gemma-3-4b-it` | chinese | **select@24** | **0.0% (0/4)** | **1 of 1 handles** | 22 |
| `qwen2.5-0.5b-instruct` | english | extract (shipped) | 95.8% | 1 of 42 handles | 0 |
| `qwen2.5-0.5b-instruct` | english | select@24 | 83.3% | 3 of 4 handles | 0 |
| `qwen2.5-0.5b-instruct` | english | select@8 | 91.7% | 5 of 6 handles | 0 |
| `qwen2.5-0.5b-instruct` | chinese | select@24 | 100.0% | 3 of 5 handles | 0 |

**A 0.0% drift rate here is the FAILURE, not the result** — one handle for eight unrelated entities links
everything to everything, which is precisely why this sweep never reports drift without collapse beside it.
The 4B produced ONE handle for the whole corpus and declined 8 of 32 English facts outright; the 0.5B
managed four to six handles, of which three to five span clusters, and did not beat the shipped shape on
drift anyway.

**The mechanism is over-picking: selection biases against the escape.** Offered a list and a
*"none of these"* option, both models take the list. That reproduces what §3.1 already records at
806,058,240 B — *the model stops choosing and emits a CONSTANT* — one seam over, and it is the reason a
shorter list does not rescue it: `select@8` is no better than `select@24`.

**A harness bug found on the way, and it is a design rule for any selective seam.** Offering the list when
NOTHING is in use yet is an ABSORBING STATE: the model answers `{"pick": 1}` against an empty list, the
pick is out of range and dropped, so no handle is ever recorded and the list can never grow — 32 of 32
facts unlabelled. **A selective seam has to bootstrap generatively.** The figures above are from after the
fix; `.claude/knowledge/pitfalls.md` carries it.

**What this does NOT say.** It is ONE selective prompt, not a prompt search — but that is the third
wording-shaped hypothesis this subsystem has refuted, and the failure here is structural rather than
phrasal. `ships=no`; the shipped `extract` shape stands.

**So all FOUR candidate levers for annotation drift are now spent**: name similarity and shared fragments
(`annotation-drift-code-reconciler`), co-occurrence (refuted on the same dumped answers), recency
(`annotation-drift-recency-gap`), and the shape. **Annotation quality is bound to the MODEL** — the 2.49 GB
one drifts least — which is the recommendation `docs/memory.md` now carries.

