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
**"Measured <500 MB" is the honest column**, and it is nearly empty on purpose: §3 says what the one filled
cell does and does not cover.

| shape | the question | where | how often | named client | measured <500 MB |
|---|---|---|---|---|---|
| **extract** | pull structured facts out of free text | `LlmAnnotationOptions` (subject handles) | per WRITE | option | no |
| **select-from-list** | which members of a visible list qualify | `LlmVerificationOptions` (which notes answered) | per RECALL, one call for all candidates | option | no |
| **select-from-list** (short) | pick one of two | `IPairwiseComparer` | 2 calls per pair, by default | composition root | no |
| **select-from-list** (roster) | which tool to call, or none | `IToolLoop`, both paths | per loop iteration, up to `LyntaiOptions.ToolLoopMaxIterations` | composition root | no |
| **score-a-pair** (cross-encoder) | how relevant is this document to this query | `CrossEncoderVerificationOptions` | per candidate | endpoint | **yes, one row** |
| **score-a-pair** (generative) | grade this output against this input, 0..1 | `LlmScorerBase`, and `RelevancyScorer` under it | per evaluation, per scorer | composition root | no |
| **classify** | is this fact durable enough to keep verbatim | `LlmAnnotationOptions.SuggestGrade`, off by default | per WRITE, when on | option | no |
| **affordance** | given these tools, what do you want | `MemoryTools`, the generation tools, an MCP-hosted toolset | per model tool call, unbounded by this library | no | no |
| **embed** | place this text in a vector space | `IEmbedder` | per WRITE **and** per RECALL | no | not treated as a size question |
| **repair** | re-emit that, as JSON this time | the shared JSON completion helper | at most once per call, under three seams | inherited | no |
| **delegate a run** | here is a task, do it | `IAgentSession` | per session, model-driven | n/a — you pick a CLI | out of scope |

**Two rows are not really tasks and are here so nobody looks for them elsewhere.** *Repair* decides nothing
about content — it asks the model to restate its own answer in a format — but it is a real second call, it
is fail-closed where every other seam here is fail-open, and it *"spends a second usage-budget/rate-limit
charge and never returns a cached hit"*. A pair comparison with position-bias mitigation on is therefore
worst-case **four** model calls for one logical decision. *Delegate a run* hands a whole task to a model
running its own loop out of process, so **the budget, rate-limit and cache advice in this document does not
reach it**, and the size question is a choice of CLI rather than of a weight file.

**Producing an artifact — an image, a video — is a different currency and is deliberately absent.** Those
backends have no prompt contract this library authors, no parse, and no quality measurement anywhere in
this repository. Treat nothing here as transferring to them.

## 2. Bound the INPUT before buying a bigger model — and the two cases are not one rule

**A model given an unbounded task stops discriminating, and it reads as the model being too small.** This
is the most transferable thing in this document, it was measured twice on two different seams with the same
model, and it splits:

- **A GENERATIVE task takes a count naturally.** A fact extractor asked for "the facts" with no budget
  produced 7.1 facts per turn; told "at most 2" it produced 2.1 — obeying at 8.3% over.
- **A SELECTIVE task over a list the model can SEE does not.** The same instruction put to the judge did
  not bind at all: asked for **at most 20 of 80 it endorsed 34.9 — MORE than the 29.1 it endorsed
  unbudgeted**; asked for at most 5 it endorsed 27.4. Every candidate looks locally defensible, and a
  stated number reads as an expectation rather than a cap.

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

**What it is NOT is a knowledge failure.** The same run shows the model ranks well and stops badly —
precision at its own top-ranked endorsement is far above its precision overall, a large lift. So the lever
is calibration or the combination rule, never a count. That is **D110**, which refused to build a cap over
the endorsements for exactly this reason.

**The consequence for your own seams:** before concluding a seam needs a bigger model, check what the seam
HANDS it — how many items, how long, and whether the instruction bounds the answer. A knob that shapes the
input is usually cheaper than a bigger model, and it is testable on the model you already have.

**One honest limit on all of this.** Every library-authored prompt named in §1 is a compile-time constant,
and none of them routes through the prompt registry. So on the two memory seams the prompt is not a knob
you have; replacing the whole policy is. What you *can* shape from configuration is the input — depth,
candidate count, how much text each candidate carries.

## 3. What survives under 500 MB — one measured row, and the rest blank

**There is exactly one measurement in this repository of a sub-500 MB model doing any of these jobs**, and
it is narrower than the question. `docs/memory-measurements.md` §5 owns it (`locomo-lamar600m-q8-n200`,
n = 200, `ships=no`): a **468,393,760-byte** cross-encoder captures **6.0 of the 7.0 points** a perfect
judge offers on that workload, at 74% of the incumbent's bytes, and a model 28 months newer at the same
architecture and size scores identically. **State the byte count whenever a size decides anything** — the
same file is 606 MiB and 636 MB depending on the unit, and a 500 threshold falls between them.

**Three things that row does not say, and each one matters more than the number.**

1. **It tests the cross-encoder RERANKER role only.** That is *score-a-pair*, a model class trained to emit
   a relevance score. The record is explicitly silent on the JUDGE role — *decide IF each of 80 answered,
   and stop* — which is a different task and the one that failed in §2.
2. **`ships=no`.** It is a ladder rung, not a configuration recommendation. Reading a rung, a ceiling or an
   oracle as a default is the specific mistake `docs/memory-measurements.md` invites and its status index
   exists to prevent.
3. **You can now reach it from configuration** — `AddMemoryCrossEncoderVerification` fills the verification
   seam from a `/v1/rerank` endpoint. Until it shipped, the only code that could call one was a bench
   harness, so this row's measurement described something a consumer could not have. **Set
   `CrossEncoderVerificationOptions.EndorseCount` to your recall limit**: it is a fixed count so that
   promotion refines the ranking, and endorsing more than a page replaces it instead — which is exactly how
   an instruct model lost 10.5 points in the neighbouring row.

**Every other shape is unmeasured under 500 MB, and that is a statement about this repository rather than
about the models.** The smallest model ever *called* in the judge role here is a 4B at roughly 3.3 GB.
Nothing under 2 GB has been tried in any selective role; the shipped extract seam has never been
quality-measured at any size; classify, affordance and the generative graded-quality scorer have no
evidence at any size. **Do not read a blank cell as a negative result.**

**And do not carry any magnitude here into your own deployment.** Direction transfers between corpora and
size does not — the same 4B model reads best-in-class on this repository's own synthetic corpus and
catastrophic on a field benchmark at the shipped depth. One model, two corpora, opposite signs. Price it on
the corpus at hand; `.claude/knowledge/model-decoupling.md` is the standing rule.

### The shape decides how you SERVE it, too

**An encode-only shape and a generative one want opposite serving configurations, and the gap is large
enough to mistake for a hardware limit.** *Embed* and *score-a-pair* never generate a token — they are all
prompt processing, so they take GPU offload well. The generative shapes spend most of their wall clock
producing tokens, which is a different kernel entirely and can be dramatically worse on a *contended*
device even when prompt processing on that same device is faster.

Measured here on one contended laptop, so read the DIRECTION and re-measure the size yourself: an embedder
ran **4.8× faster** fully offloaded, while a 4B instruct model ran an order of magnitude *slower* offloaded
than on CPU. **Set the offload level per seam and per machine, and never infer one from the other** — the
same box gave both results within the hour. The vectors were identical across devices, so for the
encode-only shapes this is a free speed choice rather than a trade.

**Where a shortlist of small models exists at all it is a DESK survey** — sizes and capabilities read from
model cards, never called (`TASKS.md` Part 177). Two things make that tier worth distrusting here rather
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
> `Model` alone is not a reliable pin. This is **D87**'s shape, and whether that precedence is right is
> still open (`TASKS.md` Part 128).

## 6. Where to look next

| you want | read |
|---|---|
| which model, rather than which task | `docs/memory.md` §Choosing the model |
| what the memory seams cost, and the judge's field caveat | `docs/memory.md` §Model-backed steps |
| the figures behind every claim here | `docs/memory-measurements.md` §5 |
| why a model is a deployment choice at all | `.claude/knowledge/model-decoupling.md` |
| adding your own scorer, or your own policy | `.claude/knowledge/extending-lyntai.md` |
