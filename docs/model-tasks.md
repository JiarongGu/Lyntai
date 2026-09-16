# Model tasks — which SHAPE you hand a model decides more than which model

> **What this is.** An inventory of every place this library asks a model to decide something, grouped by
> the SHAPE of the question, with what has actually been measured about each one. It is advice on top of
> seams that already exist — nothing here is new surface, and no default enables any of it.
>
> **What it is not.** It is not a model recommendation. `docs/memory.md` §Choosing the model holds the three
> disqualifiers for the memory judge, and they are properties of the MODEL; this document is about the
> properties of the TASK. The two are orthogonal and you need both.

**Read the first section and the second, then stop unless you are wiring a specific seam.** The taxonomy is
reference; the rule in §2 is the part that changes what you build.

## 1. The shapes, and where each one lives

Every row is a real call this library makes. **"Named client" is whether a deployment can point that seam
at a chosen backend through an option** — §5 is why the answer is mostly no and what to do instead.
**"Measured <500 MB" is the honest column**, and it is still mostly empty: §3 says what each filled cell
does and does not cover, and a blank means *not yet shown to fit the budget*, never *not good enough*.

| shape | the question | where | how often | named client | measured <500 MB |
|---|---|---|---|---|---|
| **extract** | pull structured facts out of free text | `LlmAnnotationOptions` (subject handles) | per WRITE | option | no |
| **select-from-list** | which members of a visible list qualify | `LlmVerificationOptions` (which notes answered) | per RECALL, one call for all candidates | option | no |
| **select-from-list** (short) | pick one of two | `IPairwiseComparer` | 2 calls per pair, by default | composition root | no |
| **select-from-list** (roster) | which tool to call, or none | `IToolLoop`, both paths | per loop iteration, up to `LyntaiOptions.ToolLoopMaxIterations` | composition root | **yes — §3.1** |
| **score-a-pair** (cross-encoder) | how relevant is this document to this query | `ScoringVerificationOptions` | per candidate | endpoint | **yes, one row** |
| **score-a-pair** (generative) | grade this output against this input, 0..1 | `LlmScorerBase`, and `RelevancyScorer` under it | per evaluation, per scorer | composition root | no |
| **classify** | is this fact durable enough to keep verbatim | `LlmAnnotationOptions.SuggestGrade`, off by default | per WRITE, when on | option | no |
| **affordance** | given these tools, what do you want | `MemoryTools`, the generation tools, an MCP-hosted toolset | per model tool call, unbounded by this library | no | **yes — §3.1** |
| **embed** | place this text in a vector space | `IEmbedder` | per WRITE **and** per RECALL | no | **yes — §3.3** |
| **repair** | re-emit that, as JSON this time | the shared JSON completion helper | at most once per call, under three seams | inherited | no |
| **delegate a run** | here is a task, do it | `IAgentSession` | per session, model-driven | n/a — you pick a CLI | out of scope |

**Two rows are not really tasks and are here so nobody looks for them elsewhere.** *Repair* decides nothing
about content — it asks the model to restate its own answer in a format — but it is a real second call, it
is fail-closed where every other seam here is fail-open, and it *"spends a second usage-budget/rate-limit
charge and never returns a cached hit"*. A pair comparison with position-bias mitigation on is therefore
worst-case **four** model calls for one logical decision.

**And it is the one row where reading this table produced a code change (2026-09-15).** Being the shape with
no judgement in it at all, *repair* is the shape code should own — so the read now tolerates a trailing
comma and a stray comment and re-serializes, and those replies cost no call. What still reaches the model is
a TRUNCATED object, which is missing content rather than punctuation. **The negative space in §4 is not
fixed** — a shape can move into it, and this is the exercise that moves one. *Delegate a run* hands a whole task to a model
running its own loop out of process, so **the budget, rate-limit and cache advice in this document does not
reach it**, and the size question is a choice of CLI rather than of a weight file.

**Producing an artifact — an image, a video — is a different currency and is deliberately absent.** Those
backends have no prompt contract this library authors, no parse, and no quality measurement anywhere in
this repository. Treat nothing here as transferring to them.

## 2. Bound the INPUT before buying a bigger model — and the three cases are not one rule

**A model given an unbounded task stops discriminating, and it reads as the model being too small.** This
is the most transferable thing in this document, it was measured on three different seams with the same
model, and it splits:

- **A GENERATIVE task takes a count naturally.** A fact extractor asked for "the facts" with no budget
  produced 7.1 facts per turn; told "at most 2" it produced 2.1 — obeying at 8.3% over.
- **A SELECTIVE task over a list the model can SEE does not.** The same instruction put to the judge did
  not bind at all: asked for **at most 20 of 80 it endorsed 34.9 — MORE than the 29.1 it endorsed
  unbudgeted**; asked for at most 5 it endorsed 27.4. Every candidate looks locally defensible, and a
  stated number reads as an expectation rather than a cap.
- **An AFFORDANCE task — a roster of tools — cannot be bounded by the PROMPT, and this is the case that
  bites hardest.** Given 3-7 tools and a request none of them serves, a 4B invokes one anyway on
  **90-95%** of trials, fabricating arguments to force the fit; a 0.5B on the same protocol reads
  **90-100%**. **Two prompt rewrites in opposite directions moved it by nothing** — so unlike the two cases
  above, no instruction helps, and §1 marks this the one shape the library leaves *unbounded*.
  <br>**But the TRANSPORT bounds it, which this bullet denied until 2026-09-13.** It read *"cannot be
  bounded by the model AT ALL"* and named narrowing the roster as the only lever. Measured on the one model
  here that can take both paths, native function-calling invokes a tool on **20-30%** of those same
  requests against the prompt protocol's 90-100% — and it is discrimination rather than blanket restraint,
  firing on 70-78% of requests a tool DOES serve against 20-30% where none does, about **50 points of
  separation** where the prompt protocol has none. It costs **2.4-9.6 points** of accuracy where a tool
  does fit. §3.1 has the figures; a 1B on the prompt protocol fails the exact mirror way.

**A budget that RAISES the output is the tell**, and it is only visible if you count how often the seam
fired. The trap and its full reasoning are `.claude/knowledge/pitfalls.md` §A model given an UNBOUNDED task;
the measurement that owns those figures is `docs/memory-measurements.md` §5.

### For a selective task, LIST LENGTH is the governing variable

**This is the sharpest measured result here, and a taxonomy row that only said "select-from-list is where a
small model fails" would mispredict.** The same model, same prompt, same corpus, with only the number of
candidates shown varying:

| candidates shown | endorsed | precision | lift over chance |
|---|---|---|---|
| 20 | 3.4 | **16.2%** | 3.27× |
| 40 | 7.7 | 8.4% | 3.08× |
| 80 | 29.1 | **2.6%** | **1.74×** |

**The long list does not merely dilute a constant judgement — it makes the judgement itself worse.** At 40
that model is level with using no judge at all; at 80 it is well below. So *"which model"* was the wrong
question and *"how many did you show it"* was the right one. `docs/memory-measurements.md` §5 owns the
table and the arm-level scores.

**Below 20 the governing variable changes, and the table above does not extend down.** Measured 2026-09-12
at 3-7 options in a FORCED choice — exactly one right (`docs/memory-measurements.md` §5): a 4B reads 71.5%
at N = 3 falling only to 63.6% at N = 7, so list length costs it little, while WHICH SHAPE it is asked in
costs it far more and a 1B stops choosing entirely. **Lift over chance is capped at N down here**, so the
3.27x / 1.74x column above cannot be compared against it at all.

**What it is NOT is a knowledge failure.** The same run shows the model ranks well and stops badly —
precision at its own top-ranked endorsement is far above its precision overall, a large lift. So the lever
is calibration or the combination rule, never a count. That is **D110**, which refused to build a cap over
the endorsements for exactly this reason.

**The consequence for your own seams:** before concluding a seam needs a bigger model, check what the seam
HANDS it — how many items, how long, and whether the instruction bounds the answer. A knob that shapes the
input is usually cheaper than a bigger model, and it is testable on the model you already have.

**And check the input bound is one you CONTROL.** The three cases differ in who can enforce it: a generative
task takes the instruction, a selective one ignores it, and an affordance task ignores it *and* acts anyway.
Where the model supplies no bound, the caller must — which is a structural fix, not a prompt.

**One honest limit on all of this.** Every library-authored prompt named in §1 is a compile-time constant,
and none of them routes through the prompt registry. So on the two memory seams the prompt is not a knob
you have; replacing the whole policy is. What you *can* shape from configuration is the input — depth,
candidate count, how much text each candidate carries.

## 3. What survives under 500 MB — one measured row, and the rest blank

> **WHY 500 MB, and the status of that number — a WORKING POSITION, not a rule (2026-09-12).** The owner's
> stated aim: *"we aim to support smaller model (to save resource because this just a memory system)"*, and
> **~500 MB is a SIZING TARGET rather than a cap** — *"not 500mb as a hard cap we can test even smaller ones,
> or around 500mb sizing"*, with **sub-100 MB** named as the stretch.
>
> **The reasoning is a BUDGET one and it is independent of quality.** A memory subsystem is infrastructure
> sitting beside an application's own model, so it should not claim the resources of one. That independence
> is the load-bearing half: **a bigger model winning on quality would not settle it.** The row below is the
> convenient case — 468 MB beat a 2.49 GB instruct model in the same seam — and the aim would hold had it
> lost. The road not taken is to let each seam take the best model available and treat memory's footprint as
> the application's problem. **Reversal cost is low**: no code encodes a threshold, and nothing is gated on
> one. It governs which candidates get surveyed and measured, which is why it is recorded here rather than
> in `docs/DECISIONS.md`.
>
> **What this changes about the blanks below:** a `measured <500 MB: no` cell means *not yet shown to fit the
> budget*, never *not good enough*. Those are different questions and only the first is being asked.
>
> **RULED 2026-09-12: a 4B-class model is not a production candidate, so its QUALITY questions are retired.**
> The owner's words — *"we not really going to use 4B in most of our real production case, so our current 4B
> benchmark should be enough"*. The 4B stays in the grids as a CEILING arm, which is what makes a small
> model's score readable; what stops is spending runs on whether a *better* 4B exists. This retired
> `TASKS.md`'s "Does a NEWER same-size instruct model judge better?" (`docs/task-archive.md` Part 195) —
> **closed by ruling, never measured**, so nothing here says the answer is no.
>
> **It also re-points every reading below.** Where a row compares a 4B against something small, the 4B is
> the ceiling and the small arm is the candidate — §3.1 is the worked example, and it inverts that section's
> own headline.

> **WHERE THIS IS HEADING — also a working position (2026-09-12).** The owner names the next goal as **a
> decision system on a small model**, of which this memory work is the first instance. Three findings below
> transfer to it directly and one is a warning: a decision is `select-from-list` or `affordance` in §1, which
> is precisely the shape a small INSTRUCT model failed at (806,058,240 B, inert, ceiling of zero); **list
> length governs it more than model size** (§2's table: 20 shown → 16.2% precision, 80 → 2.6%, below using
> none at all); a stated budget does **not** bind a selective task and made one endorse MORE; and the shape
> that DID work small is `score-a-pair`. `affordance` has no evidence at any size; do not read the above as
> covering it.
>
> **This paragraph used to end "the evidence points at a SCORER over a bounded candidate list, never a <!-- drift-ok: quotes the rule the 3-7 measurement retired -->
> generator asked to choose", and the 3-7 region has since been MEASURED and it is not that simple**
> (2026-09-12, `docs/memory-measurements.md` §5, `decision-shape-single-evidence`). Among the GENERATIVE
> arms **the winning shape inverts with model size**: at 2,489,757,856 B a generator asked to choose beats
> N scorings at every list length from 3 to 7 (`p<0.0001`); at 806,058,240 B it loses at every one, because
> it stops choosing and emits a constant. **Pick the shape from the size, never in advance.**
>
> **`affordance` is no longer blank, and its result is the sharpest warning in this document — §3.1.**
> A 4B routes a 3-7 tool roster well (86.3% at N = 7 against an embedder's 81.0%), and on the same roster it
> invokes a tool for **90-95% of requests nothing on it serves**. The selective half works; the DECLINE does
> not, and two prompt rewrites in opposite directions moved it by nothing.
>
> **What survives intact is the recommendation, and it got stronger.** The best arm measured is a
> **468,393,760-byte cross-encoder** doing the scorer shape in one round trip — it matches the 5.3x larger
> instruct model at three options (72.0% against 71.5%), pulls AHEAD as the list grows (67.4% against
> 63.6% at seven), and is the only arm flat in N.

**FOUR quality measurements of a sub-500 MB model now exist, and the fourth is the first SUB-100 MB one in
any role** (2026-09-15, `locomo-onnx-sub100mb-n200`): a **23,200,716-byte** cross-encoder running in
process captures **+3.0** of the 9.5 evidence-hit points a perfect judge offers, where a 468,393,760-byte
one captures **+9.0**. It works and it is a third as good — and its fp32 sibling at 91,011,230 B scores
**identically in every cell**, so within this family the extra bytes are not a lever. The other three, and
the first is still the narrowest. `docs/memory-measurements.md` §5 owns all of them: the RERANKER
(`locomo-lamar600m-q8-n200`, n = 200, `ships=no`) — a **468,393,760-byte** cross-encoder captures **6.0 of
the 7.0 points** a perfect judge offers on that workload, at 74% of the incumbent's bytes, and a model 28
months newer at the same architecture and size scores identically; the EMBEDDER (§3.3, `ships=no`) — a
**25,008,064-byte** bi-encoder routes tools within 2.4 points of a 13× larger one at a three-option roster;
and TOOL CHOOSING (§3.1, `ships=no`) — a **491,400,032-byte** instruct model picks the right tool on
**60.7%** of trials at three options, six times what a larger 1B managed. **State the byte count whenever
a size decides anything** — the same file is 606 MiB and 636 MB depending on the unit, and a 500 threshold
falls between them.

**Below that, nothing works today — and the blocker is UPSTREAM, not the model shelf** (2026-09-12,
`rerank-screen-reference-pair`). This paragraph briefly claimed a working reranker at 33,257,824 B, *"14.1×
below"* the figure above; that was retracted the same day when the candidates were re-screened on a pair
with a published reference score. `ms-marco-MiniLM-L6-v2` ranks that pair **backwards**, and
`jina-reranker-v1-tiny-en` orders it correctly with **137.8× too little separation**.

**Read `tokenizer.ggml.token_type_count` before anything else.** llama.cpp PR #21729 — token_type_ids
hardcoded to zero, pooling layers dropped in conversion — is **open and unmerged**, so a BERT cross-encoder
loses its pooler in the file and its segment signal at runtime, and a cross-encoder needs segments to tell
the query from the document. `2` means the model wants a signal it will not get; `1` means RoBERTa/XLM-R,
which never had segment embeddings and is immune. **Every model that works here reads 1.**

That collapses the sizing question into one sentence: **a correct reranker must currently be
RoBERTa-family, and that family's 250,002-token vocabulary puts it above 100 MB** — the best multilingual
candidate bottoms out at **124,925,504 B**, only 6.1% below its own Q8_0, because the vocabulary is 81.6%
of the parameters and quantisation is not a lever on an embedding table. So 468,393,760 B was recorded as
the measured floor, with sub-100 MB blocked by an unmerged patch rather than by availability.

> **SCOPED 2026-09-14: every sentence above is about llama.cpp, and the floor went with it.** Read through
> a runtime that does not convert, the same `ms-marco-MiniLM-L6-v2` reproduces its own model card to four
> decimal places — and its int8 ONNX export is **23,200,716 B**, correctly ordered and still logit-scaled,
> **20.2× below** that floor (`docs/memory-measurements.md` §5, `rerank-screen-onnx-runtime`). So the
> constraint was never "a correct sub-100 MB cross-encoder does not exist"; it was "llama.cpp cannot
> convert one". **What survives untouched** is the multilingual half — the 250,002-token vocabulary is a
> property of the model, not the runtime, so a Chinese-first deployment is still above 100 MB — and the
> 512-token ceiling, likewise the model's. **What is NOT established** is any quality figure, and that the
> library can reach an ONNX model at all: `AddMemoryScoringVerification` (**D115**) takes a
> `/v1/rerank` endpoint and an ONNX file has no server.
>
> **BOTH CLOSED 2026-09-15, so the sentence above is fully spent.** Reachability needed no new seam —
> `AddOnnxCrossEncoder` loads a cross-encoder in process and declares `ProviderKinds.Score`, which
> `AddMemoryScoringVerification` already selects on (**D139** replaced D115's endpoint-shaped reading).
> <br>**And the quality figure now exists** (`locomo-onnx-sub100mb-n200`): **+3.0** evidence-hit at
> 23,200,716 B against `LAMAR-600m`'s **+9.0** at 468,393,760 B on the same tree and embedder, of 9.5
> points reachable. **So sub-100 MB WORKS and is a THIRD as good** — the size class is answered rather
> than merely available. Two things that answer settles and one it does not: the fp32 export is
> **identical in every cell**, so the extra 67,810,514 B is not the lever; the 512-token window
> **never bit** (0 of 16,000 pairs truncated), so it is not the cause either; and the comparison stacks
> size against architecture against runtime, which it does not separate.

**RE-AIMED 2026-09-12, and this paragraph read as more final than it is.** Everything above is about the
**cross-encoder** role, and #21729's two defects are role-specific: zeroed `token_type_ids` costs a model
its SEGMENT signal, and a dropped pooler costs it a LEARNED pooling head. A cross-encoder needs both to tell
a query from a document. **A single-sequence embedder needs neither** — and §3.3 has now TESTED that rather
than arguing it, with a mechanism sharper than this paragraph first carried: a single sequence IS segment 0,
so zeroing `token_type_ids` writes the correct value instead of destroying a signal.

**And the floor turned out to belong to the VOCABULARY rather than to the role** (§3.3, finding 5). The
468,393,760 B above reads here as what a cross-encoder structurally costs; measured one role over, the same
wall stands in the same place for the same reason — an XLM-R embedding table is 96,000,768 parameters, which
is **102,000,816 B at Q8_0** and therefore over the target before a single transformer layer. So
**monolingual is the escape and quantising is not**: a Chinese-capable embedder screens healthy at
**47,886,240 B**.

**The STATIC class is now MEASURED, and the answer is that it works and costs ~12 points.** A `model2vec` /
`potion` model is a token→vector table plus pooling with no transformer at inference, which moves the
question off llama.cpp entirely — and that is the appeal, since it is the only candidate class where
"sub-100 MB" and "no server at all" are one sentence. **No GGUF of any such model exists** (the HuggingFace
API searched three ways, zero results), so it was unreachable by every instrument here until a shim made it
measurable. On tool routing (`docs/memory-measurements.md` §5, `affordance-static-embedders`):

- **It works.** All three sizes screen HEALTHY, and **none has a context limit** — a lookup table has no
  positional embeddings, so it swallows an input every sub-100 MB *transformer* embedder rejects at 512.
- **A smaller TRANSFORMER still beats it**: `all-MiniLM-L6-v2` Q8_0 at **25,008,064 B** is ahead of
  `potion-retrieval-32M` at **129,210,456 B** at every roster size.
- **Retrieval tuning does not close it** — the tuned member at 4.3× the bytes of `potion-base-8M` reads no
  better at three options and worse at seven. The gap is the class, not the member.

**So the trade is not quality-per-byte; it is infrastructure.** Roughly 12 points of tool-routing accuracy
for no server, no GPU and no port — which is worth very different amounts to a shared host and to a game
that already owns the device.

**BOTH in-process routes now ship, and the hard part was smaller than this paragraph assumed** — it read
"a new dependency and new public surface". WordPiece over a `vocab.txt` is ~250 lines, so
`Lyntai.Text.WordPieceTokenizer` adds one public type and no dependency at all (**D122**), and the
TRANSFORMER half is `Lyntai.Providers.Onnx` (**D124**), where the runtime genuinely is a native dependency
and is isolated for exactly that reason. The trade this section describes is therefore now a
CONFIGURATION choice rather than a gap: ~12 points against ~16 MB of native code and a 512-token limit.

**But the shipped class is ENGLISH, and the limit is the VOCABULARY rather than the tokenizer.** Counted
2026-09-14 over `potion-base-8M`'s 29,528 rows: **1,900 non-ASCII (6.4%)** — **488 Han**, 188 Kana,
**0 Hangul**, 86 Cyrillic, 88 Arabic. The Han rows are the top-frequency characters, against roughly 3,000
for basic literacy and 7,000 for general text, so Chinese tokenizes STRUCTURALLY correctly (each character
its own token, pinned by a live test against this vocabulary) and then misses the table constantly; Korean
cannot work at all. **This is the same floor §3 records for the reranker role, arriving by the same
route** — a multilingual vocabulary is most of a small model's parameters. A CJK-first deployment needs a
multilingual export, and the caveat in D122 is that those are usually SentencePiece rather than WordPiece.

**Three things that row does not say, and each one matters more than the number.**

1. **It tests the cross-encoder RERANKER role only.** That is *score-a-pair*, a model class trained to emit
   a relevance score. The record is explicitly silent on the JUDGE role — *decide IF each of 80 answered,
   and stop* — which is a different task and the one that failed in §2.
2. **`ships=no`.** It is a ladder rung, not a configuration recommendation. Reading a rung, a ceiling or an
   oracle as a default is the specific mistake `docs/memory-measurements.md` invites and its status index
   exists to prevent.
3. **You can now reach it from configuration** — `AddMemoryScoringVerification` fills the verification
   seam from ANY backend producing `ProviderKinds.Score`: a `/v1/rerank` endpoint through
   `AddHttpProvider`, or an in-process ONNX cross-encoder through `AddOnnxCrossEncoder`, which needs no
   server at all. Until it shipped, the only code that could call one was a bench
   harness, so this row's measurement described something a consumer could not have. **Set
   `ScoringVerificationOptions.EndorseCount` to your recall limit**: it is a fixed count so that
   promotion refines the ranking, and endorsing more than a page replaces it instead — which is exactly how
   an instruct model lost 10.5 points in the neighbouring row.

**ANNOTATION is no longer a blank cell, and its answer is the sharpest warning here after §3.1**
(2026-09-15, `annotation-drift-corrected-context`). The seam links two facts when their subjects MATCH, and
three local models — **491,400,032 B**, **806,058,240 B** and **2,489,757,856 B** — invented a new handle on
**58.3% to 90.5%** of the facts where the right one was already on offer. **This is the one cell where SIZE
buys something**: the 2.49 GB model drifts least in both languages (83.3% English, 58.3% Chinese) and the
two sub-gigabyte ones sit within a few points of each other above it.
<br>**And it is the cell where nothing ELSE buys anything** — four alternatives to a bigger model were
priced and all four failed: name similarity and shared fragments move 1 of 6 cells, co-occurrence cannot
reach a drifted handle at all, recency is a write-order artifact that collapses half the handle space on an
interleaved stream, and **reshaping the seam to `select-from-list` makes the 2.49 GB model answer ONE handle
for eight unrelated entities**. That last one matters most here, because it is this document's own
prescription for a generative task that must reuse — and one seam over it reproduces §3.1's constant-emitter
exactly: offered a list and a "none of these", both models take the list. Read it beside §3's reranker row,
where a 468 MB purpose-built model beat a 5.3× larger instruct one: **the shape decides whether size, code
or re-asking is the lever, and annotation is the cell where only size is.** The practical rule is
`docs/memory.md`'s: screen the annotator you intend to ship.
<br>_An earlier reading of this run said the opposite — "size is not the lever", with the best English
model the worst Chinese one. It was RETRACTED the same day: the harness built the annotator's context
itself and built a cleaner one than the engine passes, which flattered the small models by up to 24.5
points and inverted the ranking._

**Every other shape is unmeasured under 500 MB, and that is a statement about this repository rather than
about the models.** The smallest model called in the JUDGE role here is **806,058,240 B**
(`gemma-3-1b-it` Q4_K_M, `locomo-judge-1b-n200`) — and it was **inert**, with a ceiling of zero, which is a
measured negative rather than a blank. Below that, nothing has been tried in any selective role; the
shipped extract seam has never been quality-measured at any size; classify and the generative graded-quality
scorer have no evidence at any size (**affordance now has some — §3.1**). **Do not read a blank cell as a
negative result** —
and do not read that 1B row as one either, since it prices *instruct models in a selective role*, which is
precisely the shape §1 says to stop reaching for.

> _Corrected 2026-09-12. This paragraph read "the smallest model ever *called* in the judge role here is a
> 4B at roughly 3.3 GB. Nothing under 2 GB has been tried in any selective role" — **both false when
> written**: the 1B judge run landed the day before. Recorded rather than silently fixed because the
> mechanism is the one `pitfalls.md` files under a backlog summary going stale — the sentence was composed
> from the §2 narrative about the 4B, not from the results index, which already carried the row._

**And do not carry any magnitude here into your own deployment.** Direction transfers between corpora and
size does not — the same 4B model reads best-in-class on this repository's own synthetic corpus and
catastrophic on a field benchmark at the shipped depth. One model, two corpora, opposite signs. Price it on
the corpus at hand; `.claude/knowledge/model-decoupling.md` is the standing rule.

### 3.1 `affordance`: the selection works, the REFUSAL does not

**Measured 2026-09-12** (`docs/memory-measurements.md` §5, `affordance-prompt-protocol-4b` and
`affordance-false-call-4b`), on a SYNTHETIC 42-tool fixture through `IToolLoop`'s prompt protocol — the
weakest evidence tier here, so read the directions and none of the magnitudes.

**THAT HEADLINE WAS THE MODEL, NOT THE SIZE CLASS — CORRECTED 2026-09-13.** This section read *"at the
size class this project targets, the prompt-protocol tool transport does not work"*, on a
**806,058,240 B** `gemma-3-1b-it` picking the right tool on **10.1% of trials at three options falling to
3.0% at seven** (36.9% → 19.0% posed as a flat choice). Same arm, same corpus, same harness, a
**491,400,032 B** `qwen2.5-0.5b-instruct` reads **60.7% → 48.8%** — about **six times** the score at
**61% of the bytes**, and INSIDE the sizing target. `docs/memory-measurements.md` §5
(`affordance-native-transport`) owns the table.

**So the transport is not what was broken; that model was.** What survives unchanged is the comparison
against the free arm: **a 333,590,944 B embedder scoring the same tool descriptions reads 81.0%, flat in
roster size**, still ahead of every generative arm at or under the target and still the cheapest. And the
gemma-3-1b row stays as measured — it is a real result about a real model a deployment might pick.

**"Flat in roster size" was measured over 3-7 options; at CATALOGUE scale it decays, gently**
(`affordance-roster-catalogue`). Swept to thirty-five on the `easy` fixture the embedder runs
**96.4% → 81.5%**, losing about fifteen points to a twelvefold roster while chance falls 33% → 3%. **These
are different fixtures and the cells are not comparable** — `hard` draws distractors from the gold tool's
own family and therefore cannot pose a roster above **seven at all**, which is a property of the design.
A catalogue is a mix of both, so 81.5% is an optimistic bound and the argmax metric makes it a low one for
a selector, which would be scored on recall@k instead.

**Nothing here says smaller is better.** It says the model family and its tool training dominate the byte
count at this scale, which is an argument for SCREENING a candidate rather than for choosing one by size.

**The consequence is an architecture, not a model choice.** Let the embedder pick the tool and give the
model only the argument-filling job for the one tool that won: choosing 1-of-7 is what the small model
fails at, while filling `{"place": "Galway"}` for an already-chosen tool is a far smaller task. **That
second half is UNMEASURED here** — this grid scores the choice and only checks that arguments parsed — so
treat it as the shape the evidence points at rather than as a measured recommendation.

**Above the target, choosing is the easy half.** A 2,489,757,856 B model reads 86.3% at a seven-tool roster
against the embedder's 81.0%, and recovers 72-84% of the trials that embedder gets wrong — so the capability
is real, it just costs 5x the sizing target. A 468,393,760 B cross-encoder **loses** to the bi-encoder here
(76.8%), inverting its win in the decision grid — the shapes are not interchangeable across tasks.

**Declining is the half that fails, and it fails silently.** Given 20 requests no tool on the roster serves,
the same 4B invokes one on **90-95%** of them, fabricating arguments to force a fit
(`restart_service {"service": "sourdough_starter_knowledge_base"}`). **Two preamble rewrites in opposite
directions changed nothing** — one reached 100%, the other stayed at 90-95% — so this is a property of
handing a model a roster, not of the wording. `LyntaiOptions.ToolProtocolPreamble` exists so a deployment can
try its own; the shipped default was left alone because no tested wording earned the change.

**Two consequences for anyone wiring a tool loop.** A roster is not a menu the model will decline **on the
prompt protocol** — narrow it before the model sees it, because there the model supplies no bound of its
own, and §1 marks this the one row *unbounded by this library*. And a 806,058,240 B model is the mirror
failure: 0-5% false calls, but it never invokes a tool when one DOES fit (81-93% of the time), emitting
`{"final": "…Please wait a moment while I retrieve the data."}` — the task understood, the grammar
unavailable. **One prompt, two opposite pathologies, decided by size.**

**The third consequence, added 2026-09-13: PREFER THE NATIVE TRANSPORT where the model has a tool
template.** On the one model measured both ways, it cuts false calls from 90-100% to **20-30%** while still
firing on 70-78% of requests a tool serves — roughly 50 points of separation against the prompt
protocol's none — and it never hallucinates a tool name or emits unusable arguments, where the prompt path
does both on 1-3% of trials. It also finishes the loop: **99.4-100% converged against 11.3-24.4%**, and at
1.70-1.79 model calls per run against 2.34-2.54, because the prompt path spends a JSON repair round. It
costs 2.4-9.6 points of choice accuracy. **Check the template before relying on it** — a model without a
tool section returns 200 with `tool_calls: null` and answers anyway, which is why `ToolLoop` keeps the
prompt protocol as its portable fallback.
<br>**And you no longer have to check by hand at runtime**: `ToolLoopResult.Transport` (**D117**) reports
which of the two actually ran, so a deployment that silently landed in the second column above can see it.
It is a fact about the run rather than a warning, so it fires no threshold and says nothing about whether
the fallback was the wrong answer for your model.

### 3.2 Cross-encoder candidates under 500 MB — a DESK survey, not a measurement

**The candidate list for `AddMemoryScoringVerification` (D115).** Moved here from `TASKS.md` on
2026-09-12 when the item holding it was retired — it is the candidate list for a SHIPPED seam, which is
maintained state rather than open work.

**A DESK survey — sizes and capabilities read, not called.** That is the tier GEN-VERIFY exists to distrust,
so the SHAPES transfer and nothing here licenses skipping a smoke test. Sizes are exact bytes because MiB
and MB straddle a 500 threshold (`.claude/knowledge/pitfalls.md`). Surveyed 2026-09-10 and adversarially
re-checked against the model cards and the HF API.

**Rerankers under 500 MB, multilingual:** `LAMAR-600m` Q5_K_M **468,393,760 B** — measured, see
`docs/task-archive.md` Part 176. `xVITA-300M` Q8_0 **332,894,432 B** (2026-08-23, modern-bert) is the
untested one and is the smallest credible MULTILINGUAL candidate — **not the smallest credible one
outright**: an ENGLISH-only reranker screens 8/8 at **33,257,824 B**, and the multilingual floor is a
separate and much higher number for the structural reason §3 records. `Qwen3-Reranker-0.6B` Q6_K
**494,879,136 B** is **deprioritised for a Chinese-first deployment** — 0.85 BEHIND bge on MTEB-zh (71.31
against 72.16) while +8.77 on English, and jina's independent table scores it BEIR 56.94 against bge's
56.42, so the English gain is protocol-dependent. It is also `Qwen3ForCausalLM` scoring yes/no logits, not a
`*ForSequenceClassification` cross-encoder.

**Two dead ends, recorded so they are not re-walked:** `bge-reranker-base`/`-large` are "Chinese and English"
per their own card — fine for a first phase, a dead end for a JP/KR one. And `gte`'s GGUF declares
architecture `new`, which llama.cpp does not register, so it cannot load at all.

**Provenance matters more than the quant here.** `mradermacher`'s Qwen3-Reranker Q6_K has **310** tensors
against the working **311** — it is missing `cls.output.weight` and scores silently wrong (llama.cpp
#16407). `Voodisss` and `zhiqian99` are byte-identical to each other and correct. Prefer an official
conversion, and smoke-test whatever you pull.

### 3.3 `embed`: sub-100 MB WORKS, and the deficit grows with the list

**Measured 2026-09-12** (`docs/memory-measurements.md` §5, `embed-screen-sub100mb` and
`affordance-cosine-sub100mb`), on the same SYNTHETIC tool-routing fixture §3.1 uses — so read the
directions and none of the magnitudes. This is the one filled cell outside the reranker role.

**Four sub-100 MB GGUF embedders load, serve and screen HEALTHY on llama.cpp today**, against the
333,590,944 B incumbent as a known-good control. The re-aiming in §3 is therefore confirmed rather than
merely argued.

| model | bytes | tool routing, N = 3 | N = 7 |
|---|---:|---|---|
| `embeddinggemma-300M` Q8_0 (the incumbent) | 333,590,944 | 81.0% | 81.0% |
| `all-MiniLM-L6-v2` Q8_0 | **25,008,064** | 78.6% | 69.6% |
| `bge-small-en-v1.5` f16 | 67,308,128 | 76.8% | 69.6% |

**The headline is a slope, not a point.** At a 3-tool roster a **13.3× smaller** model costs 2.4 points; at
seven it costs 11.4. The incumbent is FLAT in roster size and none of the small ones is — which is §2's
list-length rule turning up on a **model-free** arm. Size the model to the list you actually show it.

**Three consequences worth carrying off this fixture.**

1. **Quantisation is free down here.** f16 against Q8_0 on identical trials moves at most 1.2 points for
   45.6% fewer bytes, and an independent screen agrees to within 0.001. Take the Q8.
2. **The small model errs DIFFERENTLY from the big one** — on the trials the incumbent gets wrong it is
   right 56-62% of the time, beating a 468,393,760 B cross-encoder at 5.3% of the bytes. It is not a
   degraded copy.
3. **Every one of them is a 512-position model, and the size column cannot see that.** All four reject a
   6,263-character input, so none can be the memory `IEmbedder` — which is called per WRITE *and* per
   RECALL over entries truncated at ~6,000 characters. **Sub-100 MB is a SHORT-INPUT story here**: a tool
   roster, a query, a headline. Ask a candidate's `context_length` before its byte count.

**And check the POOLING before you believe any of it.** `bert.pooling_type` survives conversion — MiniLM
declares mean, both bge models declare CLS — so the correct flag is no `--pooling` flag. Forcing MiniLM to
CLS costs it 45% of its cosine range, which would publish as a property of the model.

### The shape decides how badly a BUSY GPU hurts you

**Give every seam the GPU — and know that a contended one punishes the shapes very differently.** On a free
device offloading wins across the board. What changes under contention is not the size of the win but its
SIGN, and only for one shape.

Measured on one laptop with plenty of VRAM free in every cell, the only difference being whether a game was
rendering. Read the DIRECTION and re-measure on your own hardware:

| shape | busy GPU | free GPU |
|---|---|---|
| generative (a 4B instruct model) | **92× slower** than CPU | **12× faster** than CPU |
| encode-only (an embedder) | 4.8× faster than CPU | 5.4× faster than CPU |

_The busy-GPU generation cell read **26×** until 2026-09-13 and does not divide out of the measurement it
comes from: 0.10 tokens/s offloaded against 9.22 on CPU, both in the busy column, is 92×. The encode row
was correct. `.claude/knowledge/pitfalls.md` carries the raw table and the correction._

**Encode-only work is robust to a busy GPU; generation is not.** That is a second and independent reason to
prefer a cross-encoder over an instruct model where something else owns the device — a game, another
service, anything you do not control. Its quality advantage is in §3; this is its cost advantage, and the
two are unrelated.

**Two practical rules.** Set the offload level explicitly, because the server's default is not neutral and
it logs nothing to say what it chose. And if a generative seam is mysteriously an order of magnitude slow,
suspect the neighbour before the model — the tell is a *non-monotone* curve as you vary the offload level,
since a genuinely wrong setting degrades smoothly and contention does not.

**Where a shortlist of small models exists at all it is a DESK survey** — sizes and capabilities read from
model cards, never called (`docs/task-archive.md` Part 215). Two things make that tier worth distrusting here rather
than merely unconfirmed: a community conversion of a reranker can be missing its classification head, in
which case it still loads and still returns scores that are simply wrong; and one such quant differs from a
working one only by a tensor count. **Smoke-test a reranker before trusting a run** — score a known answer
against known distractors and assert both the ordering and that the scores are distinct.

## 4. The shapes this library deliberately refuses a model

**The negative space is the most transferable part of the taxonomy**, because each of these is a job a
naive design hands to a model and this one does not.

- **Classifying a provider failure** into a verdict is regular expressions in one place, not a judgement.
  It is the largest classify-shaped surface in the library and it is deterministic on purpose; when it
  needed tightening, the fix was narrowing a pattern rather than adding judgement.
- **Refusal screening** is a pattern plus a bring-your-own seam, with no shipped matcher at all.
- **Guards** decide with substring matching. `IGuard` is a seam a consumer *could* back with a model; the
  library does not.
- **Routing** — which provider serves a call, and what to do when one fails — walks the caller's declared
  order and answers each failure from a table. **A model is never consulted about which model to use.**
- **Chaining a generation pipeline** refuses to guess: one artifact chains because it is the only
  candidate, while zero or several are a refusal rather than a model call.
- **Two of the three shipped scorers are deterministic** and declare it, and the judge-agreement instrument
  is pure arithmetic — a way to calibrate a judge rather than a second judge.

**The rule underneath:** a model is not better at exact comparison, at counting, or at anything with a
deterministic answer. Where the library owns the loop it can also enforce a structural constraint instead
of asking — a traversal depth is clamped to a configured ceiling rather than trusted, and one tool argument
is accepted and ignored because a model that could name it could route around a per-consumer cap.

**That lever is not available everywhere.** When tools are exposed to an external client's loop over MCP, a
guard's block is advisory rather than terminal — there is no "abandon the session" response, so a refusal
can be reported and not enforced. **A structural constraint is only available where the library owns the
loop**, which is worth checking before you rely on one.

## 5. Pinning a model per seam — two seams configure it, everything else is composition

**The argument for pinning is the library's own:** a subsystem that calls a model on your behalf should not
silently run on whatever backend happens to be default. The surface does not yet reflect that evenly.

- **`LlmVerificationOptions.ClientName` and `LlmAnnotationOptions.ClientName` are the only two options of
  their kind in the library.** Those two seams are also the only ones that suppress reasoning on the
  request, which matters because a thinking model turns a short answer into a long one — measured at
  roughly 25 s per judgement against 1.5 s.
- **Everything else takes a named client at the composition root instead.** This is not a workaround and it
  does not need a custom type: the shipped scorer, comparer and tool loop each take a client on a public
  constructor, and the container registrations are try-add, so registering your own instance first wins.
  Resolve the factory, ask it for the name you want, and hand it in.
- **`IEmbedder` has no named-client story at all**, and it is the most frequent model contact in the
  library — per write *and* per recall. It is also never batched: every call site goes through the
  single-text path, one text per call.

> **The trap that eats the obvious advice.** Setting a seam's `Model` is inert on any deployment whose
> default candidates pin models, because a candidate's own model wins over the request's. Both memory seams
> are fail-open, so the seam simply runs on another model and **nothing reports it**. If you need a seam
> pinned, pin it with `ClientName` where the option exists and at the composition root where it does not —
> `Model` alone is not a reliable pin. This is **D87**'s shape, and the precedence itself is settled:
> **D119** KEPT it — a candidate IS a provider-and-model pair — and made only the PROVABLY inert case fail,
> at composition, when every candidate a seam's client routes over pins a model and none is the one asked
> for. A partly-pinned list still goes quietly, which is why the advice above stands.

## 6. Can you express a DECISION through what ships? Yes — through the verification seam

**A decision** — given a query and a bounded list of 3-7 options, choose one or none — **is expressible
today, and one shipped implementation already IS the argmax.** Audited 2026-09-12 against the frozen
surface: the thread is `docs/task-archive.md` Part 236 and the audit closed as `docs/task-archive.md`
Part 193.

`MemoryVerificationRequest(Query, Candidates)` is exactly `(query, bounded option list)`; a candidate
carries an `Id`, a `Relevance` and — since **D108** — its whole `Content`. The reply distinguishes the two
answers a decision seam must never conflate: `MemoryVerification.NoOpinion` is *the seam did not answer*
(`Judged: false`), and `NothingRelevant` is a first-class *none of these* (`Judged: true`). And
`ScoringVerificationPolicy` with `ScoringVerificationOptions.EndorseCount = 1` is argmax over N
scorings in ONE round trip, reachable through `AddMemoryScoringVerification` (**D115**).

**What is missing is the MARGIN, not the ability to ask.** `MemoryVerification` is
`(IReadOnlyList<string>, bool)`, so the cross-encoder's real-valued scores are computed and discarded at
the endorsement cut — and a confidence threshold, which is what a decision system is usually built on,
cannot be expressed. **No public type in the library carries a per-option score out of a model-backed
seam.** Whether to add one is open and is a surface question rather than a measurement.

### Why the other seams fit worse

| seam | the obstacle |
|---|---|
| `IPairwiseComparer` | Frozen at **N = 2**, over two `string`s rather than identified options, returning A/B/Tie with **no score**. Costs up to **four** model calls per pair (both orders, each able to trigger one JSON repair). No aggregator ships — `JudgeAgreement` compares two verdict vectors, it does not pick a winner. |
| `IToolLoop` | The right roster shape, the wrong SOURCE: options come from a process-wide DI `IToolRegistry`, and the interface's own doc says the tools come "from the registry, not `LlmRequest.Tools`". A per-call 3-7 option list reaches it only by constructing `ToolLoop` and `ToolRegistry` by hand — both public, so possible, but not configurable. **Check your model emits `tool_calls` before relying on the native path**: a model whose template has no tool section returns 200 with none and answers anyway (`pitfalls.md`), which is why the loop's prompt-protocol fallback is the portable transport. |
| `IScorer` / `LlmScorerBase` | The one-pair PRIMITIVE, not a chooser. N options is N calls plus a caller-side argmax, and `ScoringService` keys results by SCORER rather than by candidate, so N options cannot be told apart inside one evaluation. |
| `IMemoryAnnotationPolicy` | Closer than it looks — its `Known` list IS a bounded candidate set, and the prompt asks the model to reuse one "copied exactly". But the output is free-form strings unconstrained to that list, with no id echo, so it expresses "pick from these" only by asking nicely. |

**And position bias is handled at N = 2 and nowhere else.** `LlmPairwiseComparer` runs both orders and
reports `Tie` when they disagree, because a judge favouring whatever is shown first is a documented failure
mode. Nothing at N > 2 carries that mitigation — and the N > 2 failure is a different one anyway
(`docs/memory-measurements.md` §5): at seven options a 4B model's penalty lands on the LAST slot, and a 1B
stops choosing altogether.

## 7. Where to look next

| you want | read |
|---|---|
| which configuration, for the SHAPE OF DEPLOYMENT you are in | `docs/deployment-shapes.md` |
| which model, rather than which task | `docs/memory.md` §Choosing the model |
| what the memory seams cost, and the judge's field caveat | `docs/memory.md` §Model-backed steps |
| the figures behind every claim here | `docs/memory-measurements.md` §5 |
| why a model is a deployment choice at all | `.claude/knowledge/model-decoupling.md` |
| adding your own scorer, or your own policy | `.claude/knowledge/extending-lyntai.md` |
