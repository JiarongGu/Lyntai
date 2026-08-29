# Long-term memory — how it works, what was measured, how to configure it

> **Maintained state.** This is the guide to the memory subsystem as it is TODAY. The contract (interfaces,
> semantics, objectives) is `docs/2026-07-17-lyntai-design.md` §5.7; the reasoning behind each choice is
> `docs/DECISIONS.md` **D39–D62**; the per-task history is `docs/task-archive.md`. When this page and the
> contract disagree, **the contract wins** and this page is wrong.
>
> It exists because that knowledge was spread across twenty-two decisions and a 400 KB archive, and a
> reader who wanted "how does this thing work and what is it good at" had nowhere to start.

## 1. What it is

A **graph memory engine**: entries are written as nodes, connected by edges, decayed by interference, and
recalled as a cheap index of one-line headlines that a caller can choose to expand.

It is **one of four memory surfaces** and replaces none of them. `IMemoryStore` (keyword), `ICuratedMemoryStore`
(curated facts) and `ISemanticMemory` (vector) are unchanged and co-exist with it.

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("Data Source=app.db")
    .AddMemoryEngine("project", e => e.UseGraph()));
```

## 2. Named engines carry independent configuration

Engines are resolved by name through `IMemoryEngineFactory`, modelled on `IHttpClientFactory` (**D39**).
**Each name carries its own complete configuration set** — options, forgetting curve, ranking policy,
annotator and verifier — so two engines in one application can behave entirely differently:

<!-- compile-given: IMemoryEngineFactory factory = null!; -->
```csharp
services.AddLyntai(cfg => cfg
    .AddMemoryEngine("chat", e => e.UseGraph(
        new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.All }))
    .AddMemoryEngine("archive", e => e.UseGraph(
        new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.None },
        ranking: new MultiplicativeRankingPolicy())));

var chat = factory.Get("chat/graph");
```

A factory that named instances while sharing one option set would be a factory in spelling only, and the
failure would be silent, so it is asserted:
`GraphMemoryWiringTests.Two_named_engines_carry_independent_option_sets`.

Anything not named per engine falls back to the container registration, and anything not registered falls
back to the shipped default. That is the same three-step resolution every seam here uses.

## 2b. Is this part of the LLM cycle, or a generic store? — lifetime and scoping

**It is a generic store, and the LLM-cycle binding is a thin layer at the edge.** That answer is what makes
one registration design serve both uses, and it is worth stating because the two purposes would otherwise
imply different lifetimes.

**Everything is a SINGLETON.** `IMemoryEngine` and `IMemoryEngineFactory` are registered with
`AddSingleton`; so is every policy. Nothing in this subsystem is scoped or transient, and an application
using scoped DI does not need to change that.

**The conversation is a PARAMETER, not construction state.** `MemoryQuery` carries `TaskKey` and `Scope` per
call, so one engine instance serves every conversation in the process. That is the whole reason a singleton
is sufficient: the engine holds no per-conversation state to be scoped.

```
generic use          engine.RecallAsync(new MemoryQuery(taskKey, scope, …))   ← task named at the call site
LLM-cycle use        MemoryToolScope.Use(taskKey)  →  the tools read it        ← task ambient for the turn
```

**The one place the two genuinely differ is TOOLS**, and `MemoryToolScope` is the seam for it. A tool is
registered once and lives as a singleton, but the task a conversation belongs to changes per turn — a chat
application has one task per conversation. `AddMemoryTools` binds a DEFAULT at registration, and
`MemoryToolScope.Use` overrides it for the duration of a turn:

<!-- compile-given: string conversationId = null!; System.Threading.Tasks.Task<string> RunTurnAsync() => null!; -->
```csharp
// the tools registered by AddMemoryTools now read and write this conversation's task,
// for this turn only — restored on dispose, so nesting behaves
using (MemoryToolScope.Use(taskKey: $"chat/{conversationId}"))
{
    var reply = await RunTurnAsync();
}
```

It is backed by `AsyncLocal`, so **concurrent turns in one process cannot read each other's scope** — which
is the property that lets a singleton tool serve a per-conversation task safely. In a request pipeline, set
it once per request; the ambient value flows with the async context and is restored when the handle is
disposed.

**What follows for you:**

| you are doing | what to register | how the task is named |
|---|---|---|
| a store the application reads and writes directly | `AddMemoryEngine(...)` | per call, in `MemoryQuery` |
| memory the MODEL searches during a turn | `AddMemoryEngine(...)` + `AddMemoryTools(...)` | `MemoryToolScope.Use` per turn |
| both, in one process | the same registrations | both, independently |

There is no third design and no scoped variant to choose, which is deliberate: a scoped engine would mean a
per-request store handle, and the store is the thing that must NOT be per-request.

## 3. What the engine is FOR

Design §5.7.0 states the objectives **lexicographically** — earlier lines are not traded for later ones:

1. **Never lose an authoritative fact.** The only objective with *no acceptable failure rate*.
2. **Return the relevant material** (miss rate).
3. **Do not return junk** (pollution rate).
4. **Keep the first load cheap.** Headlines, not content; one bounded query; no background job; everything
   computed at read time. A change that improves (2) by returning MORE, or by scanning more, has not
   improved anything — which is why §4's "headlines, then expand" shape is a constraint and not a style.

Objective (1) is why `MemoryGrade.Authoritative` exists, and why it behaves the way §6 describes.

## 4. How a recall actually works

```
query
  ↓  SeedAsync            lexical candidates from the store (FTS/trigram/substring)
  ↓  subject seeding      entries indexed under a SUBJECT the query names (needs an annotator)
  ↓  traversal            walk edges up to `Hops`, gathering neighbours
  ↓  IMemoryRankingPolicy score and order the candidates
  ↓  verification         (opt-in) a model promotes what actually ANSWERED
  ↓  authoritative reserve exact facts take slots WITHIN the limit
  ↓  Take(limit)
  ↓  reinforcement        age reset and/or stability growth on what survived
```

Two properties of that pipeline are easy to get wrong and are worth stating:

- **Candidate seeding is LEXICAL by default.** Without `SemanticSeedK` the vector store is consulted at
  *write* time only — novelty for salience, and similarity linking — so an embedder cannot reach a fact
  whose wording shares nothing with the query. Measured with a real embedding model against paraphrase
  cues: **0 of 3**, identical to no embedder. Setting `SemanticSeedK` embeds the query and joins its
  nearest entries to the candidate set, carrying their cosine as `Relevance`; the paraphrases then become
  reachable. **Reachable is not the same as returned** — see below.
- **Registering an embedder changes recall even with `SemanticSeedK = 0`, and it is a TRADE rather than a
  cost.** Both write-time mechanisms were measured separately (`node devtools/dev.mjs memory-enrichment`,
  a real model), and they behave differently enough that a single verdict would mislead:
  **similarity linking is a redistribution** — it roughly halves misses on entries that cluster with
  others (topical material, an attribute cluster reached by its subject) and it badly hurts the
  rare-but-critical entry that clusters with nothing, because the edges it adds pull traversal toward the
  crowd. **Novelty feeding salience is a broad, shallow cost** that only turns positive when there is a lot
  of noise to discriminate against.
  <br>So the question to ask of your own corpus is not "is an embedder worth it" but **"is my important
  material clustered or isolated?"** If the facts that matter most are the ones nothing else resembles, the
  linking half is working against you, and `MinSimilarity` is the knob — it is the link floor, and a value
  above `1` keeps novelty and indexing while writing no similarity edge at all.
  <br>**That recipe also zeroes `SalienceContext.SimilarCount`**, which counts against the same floor: a
  registered `IMemorySaliencePolicy` then reads `0` on every write and cannot tell that from a store nothing
  resembles. Novelty is unaffected — it reads the probe's top score, not this floor.
- **A SUBJECT is readable, not just writable.** `AddMemoryAnnotation` records what each fact is *about*;
  a recall matches its query against the handles in use and seeds the entries recorded under whichever ones
  it names, so a query for `配偶` reaches the fact whose text says `太太`. **On by default** (`SubjectSeedK`),
  unlike `SemanticSeedK` — a subject exists only because an annotator was registered and paid for, so
  reading it back needs no second opt-in. Matching is per-script: a handle in a space-writing script needs a
  word boundary (`pairbond` must not match `repairbonded`), one in a spaceless script matches as a
  substring. Seeded entries are ordinary candidates — ranked, limited, and not appended past the page.
- **Reinforcement follows the verdict, the review log follows the recall.** What gets touched and what gets
  logged are deliberately different sets — see §7.

## 5. What was measured, and what it says

Most of what follows is on this repository's own deterministic corpus, replayed against a live engine. It is
a **comparison instrument**, not a claim about your data: relevance in it is defined lexically, the shapes
are synthetic, and no arm exceeds a few hundred entries.

**The exceptions are the three sections measured on the FIELD's data** — LoCoMo and LongMemEval's two
classes. Those carry their own corpus, their own ground truth and their own caveats, which is the whole
reason they are worth running; each says so where it starts.

### The dominant defect is RANKING

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

### A judge is the lever that remains

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
the latency path of every recall makes that disqualifying whatever it scores. `LlmRequest.Reasoning` asks a
backend to skip reasoning where it can.

Verified to judge correctly in **English, Chinese, Japanese and Korean**.

#### Screening every local model on hand

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

#### The machine these numbers come from

Latency figures are meaningless without it, and the row above shows why: whether a model fits VRAM decides
its result more than its architecture does.

| | |
|---|---|
| CPU | Intel Core Ultra 9 185H — 16 physical / 22 logical cores |
| RAM | 63.7 GB |
| GPU | NVIDIA RTX 4080 Laptop — **12 GB VRAM** (driver 596.49) |
| OS | Windows 11 Pro 10.0.26200 |
| Runtime | Ollama 0.32.7, .NET 10 |

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

#### The research says an LLM judge is the wrong tool for this shape

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

**This is a design lead, not a shipped claim.** Nothing here has been measured on this corpus, ONNX Runtime
needs a C#-side tokenizer, and the literature's numbers come from IR benchmarks rather than from a memory
store. The honest statement is that the seam's SHAPE — score pairs, reorder, never generate — is the shape
rerankers exist for, and the LLM judge is a general tool doing a specialised job. See
`local/superpowers/records/2026-08-15-memory-research-review.md`.

### Salience's RANKING voice is a net cost — measured 2026-08-23

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

**Three instrument lessons, because each nearly published a wrong answer.** A verdict reporting MISS alone
cannot evaluate a lexicographic objective. A summary that averages the regression shape together with the
shapes it is traded against destroys the only structure the study has. And a per-cell `accepted`/`REFUSED`
computed from a quantity whose sign is unstable reads as a judgement while being a coin-flip — that verdict
belongs on the aggregate. The first version of this sweep did all three, printed `5/5 shapes better` for
every language, and the shipped default was changed on it before anyone read the pollution column.

**The control worth copying if you build a sweep of your own:** the study reports *distinct salience values*
(352), not how often salience fired (98.9%). Firing is presence; only distinct values are discrimination, and
RRF ranks by competition (**D82**) — so a signal every candidate ties on contributes the same constant at
every weight, and the curve would be flat as an artifact with every ordinary control green.

### And salience's OTHER two consumers cost miss as well (`memory-salience`, 2026-08-28)

D89 measured the RANKING voice and shipped it at 0. This measures what is left — **retention and store
admission, the two consumers that actually ship ON** — with the rank boost already at its shipped 0, so the
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
which is why it is an owner's call and sits in `TASKS.md` Part 65 rather than being quietly changed here.

### The first number against the FIELD's benchmark (`memory-locomo`, 2026-08-29)

`node devtools/dev.mjs memory-locomo --retrieval --n 200` — LoCoMo, 10 conversations, 5882 turns ingested
per arm, 200 questions stratified over the four scored categories (5 is the adversarial class the published
protocol excludes). **The metric is MODEL-FREE**: LoCoMo names the evidence turn for each question by
dialogue id, so this scores whether the recalled set CONTAINS it. No reader, no judge, so neither can be
blamed or credited.

| arm | multi-hop | temporal | open-domain | single-hop | overall | items/q |
|---|---|---|---|---|---|---|
| `lyntai` (shipped defaults) | 13.5% | 14.3% | 8.3% | 9.2% | **11.0%** | 20.0 |
| `lyntai+sem` (`SemanticSeedK = 20`) | 10.8% | 16.7% | 8.3% | 9.2% | **11.0%** | 20.0 |
| `lyntai+rel` (+ `RetrievabilityWeight = 0`) | 13.5% | 23.8% | 16.7% | 25.7% | **22.5%** | 20.0 |
| `vector` (plain cosine, same embedder, same k) | 81.1% | 81.0% | 58.3% | 82.6% | **80.5%** | 20.0 |

**The shipped default retrieves the evidence 11% of the time where plain cosine gets 80%.** Every arm
returned a full 20 items, so this is not a filter — it is ranking the wrong 20. The mechanism is visible in
a single dumped question whose evidence is `D1:4, D6:8`: `lyntai` returns `D19`, `D14`, `D19` — the newest
turns — while `vector` returns `D1:5`, `D1:3`, the oldest session. `RelevanceWeight` and
`RetrievabilityWeight` both ship at **1**, so a recall ranks how-reachable equally with how-relevant. That
is right when recent material is likelier wanted and exactly wrong for a benchmark whose questions are
spread evenly over the whole history.

**This is the blind spot §7 concedes, reached from outside.** The synthetic corpus cannot see it: its
relevance is recency-correlated by construction, so the two signals never disagree there. LoCoMo makes them
disagree on purpose.

**`SemanticSeedK` changed NOTHING, and the reason is that it CANNOT — measured, not inferred.** Turning it
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

The control's being byte-identical is what says the harness did not move underneath. `SemanticSeedK` becomes
worth **+5.0 points** where it was worth exactly 0.0 — it was unreachable, not weak. **That also revises the
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
first run benchmarked `SemanticSeedK = 0` against a cosine baseline, which is measuring a misconfiguration —
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
--retrieval --n 200`, every arm keeping retrievability at its shipped default):

| arm | evidence-hit@20 |
|---|---|
| `lyntai` | 31.0% |
| `+sem` (`SemanticSeedK = 20`) | **36.0%** |
| `+sem+hop0` (`HopWeight = 0`) | 11.5% |
| `+sem80` (`SemanticSeedK = 80`) | 30.0% |
| `+sem80+hop0` | 17.5% |
| `vector` | 80.5% |

Both misallocation hypotheses are refuted, and in the opposite direction. **Graph traversal is carrying the
arm, not stealing slots** — `HopWeight = 0` costs 24.5 points. And MORE semantic seeds make it WORSE
(36.0 → 30.0), which is **D82** behaving as documented: RRF ranks by competition, so widening one signal
re-ranks every candidate within it.

**The pool provably contains the evidence, so this is not a seeding problem either.** The `+sem80` arm seeds
the top-80 by cosine, which contains cosine's top-20 by construction, and that top-20 holds the evidence
80.5% of the time. So the candidate pool holds it at least 80.5% of the time and the arm returns it 30.0% of
the time: **roughly fifty points are lost ranking candidates that were present**. The signal demoting them is
retrievability — old, mentioned once, never reinforced. That is the decay model doing its job, not
misallocating slots.

**So the boundary is located.** D97 was the defect, worth about twenty points and independent of any
philosophy. What remains is the design: every knob that closes it turns forgetting down, and the two that
leave forgetting alone both made things worse.

**What LoCoMo is FOR here: a differential instrument, not a scoreboard.** It stresses the archival axis the
synthetic corpus cannot, which is precisely why it exposed a defect 3429 tests were blind to. The benchmark
that would test THIS design's claims is one where forgetting is supposed to HELP — superseded facts,
knowledge updates, distractors that should be suppressed. **LongMemEval's knowledge-update and temporal
categories** are the closer fit, and on those a working decay model should beat a flat archive rather than
apologise to it.

### The benchmark where forgetting WINS (`memory-longmemeval`, 2026-08-29)

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
| LoCoMo | retrieve arbitrary old material | 31.0% | 80.5% | **−49.5** |
| LongMemEval temporal | need the old fact AND the new | **47.7%** | 43.9% | **+3.8** |
| LongMemEval knowledge-update | prefer the new over the superseded | **86.4%** | 46.4% | **+40.0** |

**One mechanism produces all three.** Suppressing superseded material is no longer a cost where both facts
are wanted (+3.8, five questions), decisive where the old one is wrong (+40.0), and expensive only where the
workload is to retrieve arbitrary old material nobody has referred to since (−49.5). A library scoring well on all three
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

### Why suppression weakened under distractors — the loss is in the FUSION (`memory-longmemeval --ranks`, 2026-08-29)

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

**The lever therefore runs upward, and the haystack is what bounds it.** K = 120 costs nothing measurable on
`current@k` in either variant and cuts `stale@k` by ~12 points in both. Past that the two variants disagree:
the oracle saturates harmlessly at 32% forever, while the haystack starts paying real recall — **−16.7 points
of `current@k` at K = 300**. Read on the oracle alone, K = 1000 looks free. That is the same bias this
document records one section above, now caught on a second question.

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
an argument for sweeping `K` properly, not for changing a default**; `TASKS.md` Part 109 carries it.

### The mode this engine is FOR: shots, not one-shot (2026-08-29)

Every number above scores a SINGLE top-k, and that is not how this engine is meant to be read. It says so
itself: a recall returns **headlines** because *"associative content is withheld until expansion — that is
what makes the first load cheap"*, and `ExpandAsync` reinforces what it walks because *"digging in one
direction is exactly what should make that direction more retrievable next time"*. A one-shot benchmark is
structurally blind to both. `--shots` measures the walk instead, model-free.

**On the workload this design claims — LongMemEval knowledge-update, haystack variant, 25 questions.**
`clean` is the column that matters: the context holds the CURRENT fact and **not** the one it superseded,
which is what a reader's answer actually depends on. A context carrying both hands the model the
contradiction to resolve, which is the work this layer exists to do for it.

| arm | clean | current@k | stale@k | items/q | chars/q |
|---|---|---|---|---|---|
| `shot-1` | **40.0%** | 84.0% | 56.0% | 10.0 | **1,165** |
| `shot-2` | 36.0% | 88.0% | 60.0% | 19.4 | 5,208 |
| `shot-3` | 36.0% | 88.0% | 60.0% | 19.9 | 8,122 |
| `vector` | 16.0% | 84.0% | 84.0% | 10.0 | 9,769 |
| `vector-20` | 4.0% | 96.0% | 96.0% | 20.0 | 20,737 |

**One shot delivers a clean context 2.5× as often as cosine on one-eighth the characters** — 40.0% at 1,165
against 16.0% at 9,769. Cosine at k=20 reaches the current fact 96% of the time and reaches the superseded
one just as often, which is the failure mode a decay model exists to prevent, priced: it costs 20,737
characters to hand a reader both answers.

**But the shot curve ran the wrong way, and that was a real defect.** `clean` FELL as the walk went deeper
while `stale@k` climbed, because `EdgeHalfLife` decays the EDGE and nothing consulted the ENTRY — so
expansion resurrected exactly what recall had buried. **D98** adds
`GraphMemoryOptions.ExpansionRetrievabilityFloor` and holds the curve flat at 40.0% / 40.0% / 40.0% with
`stale@k` back at 56.0%, for 4 points of `current@k`. **An ordering weight was tried first and measured
moving nothing** — ordering only matters when the caller's budget binds, and at 15.9 items against a budget
of 20 it did not.

**On a SEARCH workload the curve runs the other way, which is why the shot count is a question and not a
constant.** LoCoMo, 200 questions, evidence-hit:

| arm | evidence-hit | items/q | chars/q | ms/q | hit / 1k chars |
|---|---|---|---|---|---|
| `shot-1` | 30.0% | 20.0 | 2,252 | 186.4 | 0.133 |
| `shot-2` | **36.0%** | 36.4 | 4,273 | 208.1 | 0.084 |
| `shot-3` | 36.5% | 40.0 | 4,860 | 228.7 | 0.075 |
| `vector` | 80.5% | 20.0 | 3,522 | 1.4 | **0.229** |
| `full` | 100% | 590.1 | 98,886 | — | 0.010 |

**Shot 2 is where the value is: +6.0 points against shot 3's +0.5.** So *"find me something"* wants two
shots and *"which value is current"* wants one — the optimum is a property of the question, not a default.
`ExpandSeeds` was ruled out as the constraint rather than assumed: at 20 seeds instead of 3 the arm finds
the same 36.0% for 36% more characters, so the ceiling is what the graph is connected to.

**Three things in these tables are honest limits rather than results.** The `full` arm exceeds this reader's
window — measured by needle probe, a passcode at the top of the prompt survives 85,508 characters and does
not survive 109,908 — so its QA row is a floor. `ms/q` compares a SQLite-backed store against an in-memory
array with no persistence and no write-back, and it is cold-start dominated at one store per question;
`memory-scale`'s steady-state p50 is 10.4ms at 1k, and ~80% of that was the write-back when it was measured — where **D99** then cut a recall's co-activation from ten store round-trips to one and **D101** cut the whole write-back from three store calls to one, neither of them resolvable by the instrument (its 10k p50 spans 8.9–11.2ms across runs of identical code, so no latency claim is made for either). And LoCoMo questions
within a conversation share a store, so a recall reinforces what the next question reads — `shot-1` moved
30.0% → 28.0% between two runs differing only in how much a LATER shot expanded, which is only reachable
that way. It affects every LoCoMo figure in this document.

### How these choices sit against the published field (surveyed 2026-08-29)

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
Letta in either direction — including favourably — and closing that needs the QA half, which `TASKS.md`
Part 109 carries.

### And WHAT salience measures, which is a different question (`memory-importance`, 2026-08-27)

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

### Does a CORRECTION separate from a RECURRENCE? (`memory-density`, 2026-08-27)

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
table measuring word overlap. Measured against `embeddinggemma-300M-Q8_0` over a local OpenAI-compatible
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

**Two of those numbers are ARTIFACTS of the instrument, and taking either for a finding would set a default
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

### Which regime a generalisation should assert (`memory-support`, 2026-08-28)

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
part of its contract. **And the only pacing-independent `count@θ` thresholds are the DEGENERATE ones**:
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
4th member to clear it. Cardinality is the axis `TASKS.md` Part 105 holds open, so the invariance at 0.1 must
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
local OpenAI-compatible endpoint, answered 300 counterbalanced pairs (600 calls) with **zero order
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

#### Cardinality: the axis that run held constant (2026-08-28)

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

**An instrument lesson, because it nearly hid the finding.** The pooled `ConnectionBoost = 0` control reports
*"verdict did NOT move — every rule selects the same regime under both curves"* on both clocks. That is true
of the ARGMAX while the per-rung distributions differ completely (θ = 0.9 default: tie/A/B/B; boost off:
B/B/B/B). **A control comparing pooled verdicts cannot see a split that cancels in the pool** — which is why
the per-rung table is printed per clock AND per curve rather than summarised.

### Reinforcement: the signal, not the quantity

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

### Salience does not preferentially preserve junk

A standing concern held that a novelty-driven salience policy would preserve random junk. It does not:
isolated salience effect **−0.0786 miss / −0.0924 pollution** on junk that can reach a recall, and **+0.0000**
on textually diverse junk. The reason is the interesting part — **the two properties the concern depends on
are in tension**: pollution requires the junk to be *retrievable*, and junk diverse enough to maximise
novelty matches nothing (`docs/task-archive.md` Part 69).

Its one measured cost is `many-candidates` (40 competitors): miss +0.0808, pollution +0.1532 on a
single-seed replay. `NeutralSaliencePolicy` is the one-line opt-out.

### Multilingual

Every recall-quality figure published before 2026-08-12 was English. The corpus now replays **structurally
identical** timelines in English, Chinese, Japanese, Korean and mixed-script Chinese — same steps, same ids,
same ground truth, only the text differs — so a gap is the language and not the timeline (**D55**).

Tokenization is one path for every backend (`SearchTerms`): whitespace tokens, then per-script runs, with
spaceless scripts expanded into character n-grams. Thai, Lao, Khmer, Burmese and Tibetan discriminate under
3-grams, measured against Han as the reference.

## 6. Configuration reference

### Recall shape

| option | default | what it does |
|---|---|---|
| `DefaultLimit` | 10 | entries a recall returns |
| `Hops` | 2 | edge-traversal depth |
| `CandidateMultiplier` | 4 | candidates fetched per returned slot |
| `AuthoritativeReserve` | `null` (unbounded) | slots exact facts may take **within** the limit |
| `SemanticSeedK` | `0` (off) | entries the query's embedding pulls into the candidate set |
| `SubjectSeedK` | 5 | entries each SUBJECT the query names pulls into the candidate set |
| `SubjectSeedScan` | 256 | handles a recall matches its query against |

### Learning

| option | default | what it does |
|---|---|---|
| `Reinforcement` | `All` | which EFFECTS apply — `AgeReset`, `StabilityGrowth` |
| `ReinforceOn` | `All` | which CALLS reinforce — `Recall`, `Expansion` |
| `RecallReinforceCap` | `null` | how many of a recall's hits reinforce, from the top |
| `LogReviews` | `true` | record each reinforcement for later fitting |
| `DsrOptions.ReinforceGain` | **`0`** | how much a recall lengthens the half-life — **zero in 3.0** |

**`Reinforcement = All` does NOT mean a recall lengthens a half-life.** The two rows interact and the
combination is the single biggest behavioural change in 3.0 (**D54**): `StabilityGrowth` is selected, and
the shipped curve's gain is `0`, so the growth arm is a no-op at the defaults. Measured: `miss 0.234 → 0.103`
on the fixed-corpus pin, winning on all six corpus shapes across thirty paired seeds, with every alternative
built and beaten — a capped variant, and one computed from recall COUNT so it cannot compound by
construction.
<br>**What 2.5 did, stated exactly, because it is a different CURVE and not a different setting**: it shipped
`HalfLifeRetrievability`, whose `ReinforceFactor = 0.5` multiplied stability by 1.5 per recall. That curve is
deleted in 3.0 with no restore path (**D49**), so there is no 2.5 value to put back. `ReinforceGain = 2.0`
was a development default inside the unreleased 3.0 window and never shipped.
<br>The age reset is what a recall is worth, and it EXPIRES; permanent growth instead banks the ranker's own
errors, so an entry wrongly returned becomes more likely to be returned wrongly again. To ask the 3.0 curve
for a growth arm anyway: `new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 })`. The two knobs are
separate because the seam is cut at the EFFECTS: `Reinforcement` says which effects a deployment wants at
all, `ReinforceGain` is one curve's magnitude for one of them.

`StabilityGrowth` without `AgeReset` **throws**: the store resets the age as part of the same write, so that
combination would apply neither effect.

#### The difficulty law, and how it is adapted from FSRS

Here rather than in the decision log or a method's `<remarks>`, because it is REFERENCE — what you need to
check the implementation against the published form, or to judge a `DsrOptions` value before changing it.
The decision to site it here is **D79**; the rule behind that is `.claude/rules/code-commentary.md`.

Adapted from FSRS-5's `next_difficulty` with FSRS-6's constants, checked against `py-fsrs/scheduler.py`,
`fsrs-rs/model.rs`, fsrs4anki v4.7.2 and the Anki manual. FSRS-4/4.5 has NO damping term; FSRS-5 introduced
the linear damping and moved the reversion target from `D0(3)` to `D0(4)`; FSRS-6 kept that SHAPE and
recalibrated `w6`/`w7`/its own `D0` sub-formula. The form:

    ΔD  = -w6 · (G - 3)
    D'  = D + ΔD · (10 - D) / 9        (linear damping toward the ceiling)
    D'' = w7 · target + (1 - w7) · D'  (mean reversion)                then clamp to [1, 10]

**Four adaptations, so the implementation can be checked against that form:**

1. **The discrete rating `G ∈ {1,2,3,4}` becomes a continuous derived grade** restricted to the success
   range, `g = 2 + 2·retrievability ∈ [2, 4]` — exact at both ends (`r=0 → g=2` "Hard", `r=1 → g=4`
   "Easy"), with `g=3` ("Good", FSRS's own no-change reference) landing at `r = 0.5`, this library's OWN
   half-life anchor rather than an arbitrary point.
   <br>**The FLOOR is a constraint, not a tuning choice: the mapping may never reach `g=1`** — `Again`, a
   LAPSE, the one rating a purely-successful recall must never emit. A linear `1 + 3r` would emit it at
   `r=0` while growing stability maximally in the same call.
2. `w6` is `DsrOptions.DifficultyChangeWeight`, `w7` is `DifficultyReversionWeight`, and the target is
   `DifficultyReversionTarget` — all three adopt FSRS-6's OWN published defaults, not invented numbers.
3. The linear damping term is kept verbatim.
4. **The reversion target is a directly-settable NUMBER** rather than FSRS's per-grade `D0` sub-formula:
   this library has no `w4`/`w5` pair to compute one from, and the target is a plain constant once
   `w4`/`w5` and the grade (always `4`, Easy) are fixed. Exposing the result changes where one
   sub-computation's output comes from, and nothing about the SHAPE of the law.
   <br>**Reversion is not optional**: linear damping's own factor is identically zero at `D = 10`, so
   dropping it leaves that ceiling ABSORBING.

#### Salience inflates stability growth — measured, shipped, deliberately left

`ModulatedRetrievability` calls `Reinforce` with the RAW stored state, because `Reinforce`'s return is what
gets STORED and compounding a modulated figure would bake the modulation in permanently. The consequence is
not neutral: a modulated (for instance salient) entry has a raised effective retrievability the curve never
sees, so `r` reads LOW, the spacing term `e^(spacing·(1−r)) − 1` reads HIGH, and the entry gains more
stability per recall than an equally-aged unmodulated one. **The same signal both slows decay and speeds
growth.**

Measured at the defaults, on the increase term, with a stored stability of 100:

| age/S | salience 1.5 | salience 2.5 | salience 4.0 |
|---|---|---|---|
| 0.5 | 1.33× | 1.99× | 2.98× |
| 1 | 1.26× | 1.77× | 2.53× |
| 5 | 1.12× | 1.35× | 1.66× |

`SalienceRetentionPolicy` is registered for every graph engine, so a consumer who never mentions salience
still gets this; `4.0` is `SalienceOptions.MaxSalience`'s own default, so the right-hand column is the most
a shipped policy can report rather than a corner case. The inflation is LARGEST for the FRESHEST recalls —
the opposite of the intuition that a retention signal matters most on rarely-touched entries — and it
COMPOUNDS, because each inflated gain raises the base of the next.

**Left in place, and the alternatives are why.** Removing it means either compounding the modulated figure
into stored stability — exactly what `ModulatedRetrievability` refuses to do, since that bakes a signal's
effect in where no later change to the signal could undo it — or giving that wrapper a second,
modulation-aware seam only one shipped curve would use. Both are changes to the modulation CONTRACT rather
than fixes to the curve, and the direction is safe (more stability → a wider `CandidateCutoff` → fewer
deletions).

**It does confound a curve-vs-curve measurement**, so any such comparison must register no retention
policies or control for salience explicitly (`docs/task-archive.md` Part 54).

### Model-backed steps (all opt-in, all fail-open)

| call | when | what it costs |
|---|---|---|
| `AddMemoryAnnotation()` | every WRITE | one model call; links entries about the same entity |
| `AddMemoryVerification()` | every RECALL | one model call; promotes buried answers |

Both take `ClientName` to point at a named `AddLlmClient`, so judging runs on a backend you size
deliberately. **Absent, the engine behaves exactly as it always has** — the model-free floor is a supported
configuration, not a degraded one.

#### What it costs you NOT to use a judge

The model-free floor is supported, and it is also where the single largest measured gain sits. From the
table in §5, on this repository's corpus:

| configuration | miss | pollution | what you pay |
|---|---|---|---|
| **no judge** (shipped default) | 0.5357 | 0.3331 | nothing |
| `gemma3:4b` local | **0.2571** | **0.0492** | ~1.5 s/recall, 3.3 GB VRAM, $0 |
| Claude Haiku via CLI | **0.1857** | 0.1271 | 3.0 s/recall, **$66 per 1,000 recalls** |

**Not registering a judge costs ~28 points of miss** — more than every ranking-policy decision in this
library combined, which move recall by hundredths. The reason is §5's decomposition: **0% of misses are
retrieval failures**; the answers are already in the candidate set and merely ranked below the cut. Nothing
inside the library's own arithmetic fixes that, which is why the seam exists.

So the honest framing is not "the judge is an optimisation" but: *this engine's ranking is its weakest part,
and a judge is the only shipped mechanism that repairs it.* Whether ~1.5 s and 3.3 GB is worth 28 points is
an application question — but it should be answered knowing the size of the number.

#### Choosing the model — three criteria, in this order

Learned by measuring seven models on one machine (§5). Every one of them is a *disqualifier*, not a
preference: a model failing any of them is unsuitable however well it scores.

1. **NOT a reasoning model.** This seam fires once per recall, and chain-of-thought turns a ~10-token answer
   into hundreds. Measured: `qwen3:4b` 2,214 tokens/~30 s, `gemma4:12b` ~270 tokens/40–65 s, against
   `gemma3:4b`'s 11 tokens/~1.5 s. **It is not visible from the model card** — check for a `thinking` field
   in the response, because `gemma4:12b` reasons and does not say so anywhere obvious.
2. **Leaves the card room for its neighbours.** `nomic-embed-text` must stay resident if you use semantic
   memory, and your application has its own needs. A judge at 93% of VRAM makes every embedding recall
   thrash. Judge *headroom*, not size.
3. **Answers in the languages you store.** Three of the models tested pass English and fail elsewhere —
   `llama3.2:3b` misses the answer entirely in Japanese, `qwen2.5-vl:7b` returns an empty verdict.

`LlmRequest.Reasoning = Suppress` is set by both policies already, so the library asks. Ollama's qwen-family
models reason regardless — asking is not the same as being obeyed.

#### It is a policy, so switching is one line

Everything above is a REGISTRATION, not a rebuild — the point of the seam table below. The judge is
`IMemoryVerificationPolicy`, singular, defaulting to none:

```csharp
// the model-free floor — the shipped default, nothing to write
services.AddLyntai(b => b.AddMemoryEngine("project", e => e.UseGraph()));

// a local judge: one call, one line
services.AddLyntai(b => b
    .AddOllamaProvider(baseUrl: "http://localhost:11434", defaultModel: "gemma3:4b")
    .UseDefaultCandidates("ollama")
    .AddMemoryEngine("project", e => e.UseGraph())
    .AddMemoryVerification(o => o.Model = "gemma3:4b"));
```

Because it is a policy rather than a mode, a consumer who disagrees with every judgement above implements
`IMemoryVerificationPolicy` themselves — a cross-encoder reranker, a hosted model, a hand-written rule — and
registers it. The engine consults whatever is there and behaves identically when nothing is
(**fail-open**, §5). `GraphMemoryOptions.VerificationFilters` then decides whether a verdict merely reorders
or also drops, and `VerificationDepth` how many candidates it sees.

### The seams you can replace

Every one is `IMemory<Domain>Policy` (**D47**), registered in DI or passed per engine:

| domain | plural? | default |
|---|---|---|
| age (interference) | plural | `BurstDampenedAgePolicy` |
| retrievability (forgetting) | singular | `DsrRetrievability` |
| retention (modulation) | plural | `SalienceRetentionPolicy` |
| salience | plural | `StructuralSaliencePolicy` |
| ranking | singular | `ReciprocalRankFusionPolicy` |
| annotation | singular | none |
| verification | singular | none |

Plural domains coexist and are combined by a **composition policy**; the engine composes nothing itself
(**D48**). To turn salience OFF, register `NeutralSaliencePolicy` — **registering nothing takes the shipped
default instead**, which is the one trap in this table.

## 7. Things that will surprise you

Each of these cost a real measurement to find.

- **A small-limit recall can return fewer ordinary hits than you expect.** Authoritative facts take slots
  *within* the limit. That is objective (1) working, not a bug (**D56**).
- **Registering an empty policy collection does not disable a seam** — it takes the shipped default.
- **A write goes to ONE member of a blend** — the first that can hold its grade. So `UseGraph().UseSemantic()`
  leaves the semantic store permanently empty, because the graph supports both grades and takes everything.
  `FanOutWrites()` sends the write to every capable member; a member no write can reach is reported at
  startup, and `StrictWiring()` makes that a failure rather than a log line (**D85**).
- **`IMemoryVerificationPolicy` and `IMemoryAnnotationPolicy` are consulted by a GRAPH member only.**
  Registered onto a blend with none, they never run — and a recall then reports `Answered = null`, which is
  exactly what it reports with nothing registered at all. That is the second thing the wiring check reports.
- **A recall with no scope searches every scope of the task** (**D86**), for semantic memory as much as for
  the rest. Through 3.0.0 the semantic member returned nothing there, so a consumer treating scope as an
  optional filter got no semantic recall on its ordinary path. It needs an `IListableVectorStore` underneath;
  all three shipped stores are one. The graph engine's own semantic SEEDING had the identical defect one
  layer down and was fixed after an adopter measured it — one engine had held two answers to "unscoped",
  because its lexical half spanned scopes and its semantic half did not.
- **Registering an embedder does not switch semantic RECALL on.** It switches semantic WRITES on: novelty and
  similarity linking run, every write is embedded, and `SemanticSeedK` still defaults to `0`, so no recall
  reads any of it. That gap is now reported at wiring time (**D85**) — but the shape is worth knowing, since
  the bill arrives per write and the benefit does not arrive at all until you set the knob.
- **The review log records every returned entry, not every reinforced one.** A judge-rejected entry is
  logged with `Verified = false` and never touched, which is what lets the log contain failures at all.
  `null` (no verifier ran) and `false` (judged irrelevant) are **not** interchangeable.
- **`VerificationFilters: false` does NOT mean the verdict leaves the results alone.** Off — the default,
  and what the option's own docs recommend — a verdict still **promotes** every endorsed candidate to the
  front, before the caller's limit is applied and over a candidate set `VerificationDepth` deep, so it
  reorders the page and can pull onto it an answer that never fitted. `VerificationFilters` adds
  *removal*; it is not the switch that makes a judge visible.
  <br>Corollary, because an adopter reached the opposite conclusion from a real measurement: **a judge that
  changes nothing is a fact about your corpus, not about the wiring.** Endorsing the entries that already
  lead *is* agreement with the ranker, and the page is then identical. Both wrong readings — "a verdict
  never reaches the ranking" and "verification always reorders" — are reachable from the code, so all three
  behaviours are pinned in `MemoryVerificationOrderingTests`.
- **A recall MUTATES, so an A/B over it must be paired and counterbalanced.** It reinforces what it returns
  and links those entries together; run one arm to completion and then the other and you have compared a
  cold graph against one the first arm warmed. Ask each query under both arms back to back with the order
  alternating. The bias is silent and lands on whichever arm ran second.
- **An embedder alone does not give you semantic recall**, and **`SemanticSeedK` alone does not either.**
  It is what consults the vector store at query time, and it is off by default. Switched on, it makes a
  paraphrase *reachable* — but **no shipped ranking configuration will spend a slot on it**: measured
  against RRF's defaults, an 8× relevance weight, `K = 1`, both together, and
  `MultiplicativeRankingPolicy`, the paraphrase is outranked by recent unrelated material in every one.
  <br>That is a limitation of the option as shipped, not a tuning gap you can close from configuration.
  **`SemanticSeedK` is useful in combination with `AddMemoryVerification`** — seeding widens the candidate
  set, the judge is what promotes from it. Pinned by `SemanticSeedProbeTests`, which asserts the negative
  for all five configurations so a future ranking change forces this claim to be re-read.
- **`Stability` has exactly one meaning**: the position delta at which retrievability is `0.5`. FSRS anchors
  at 90%; adopting that convention would silently reinterpret every stored value, so a contract fact makes
  it unshippable.
- **Two-character CJK words produce no index terms** — below the trigram floor, which is the deliberate
  signal to fall back to a substring scan.
- **`MemoryGrade.Inherit` on a re-remember inherits from the ENTRY, not from the engine's role.** Writing a
  fact again without naming a grade keeps whatever grade it already had. Naming one — including naming
  `Associative` — applies it, so promotion and deliberate demotion both still work.
  <br>**Through 3.1.0 this demoted instead**, silently: `Inherit` resolved to the engine's role before the
  store saw it, so "said nothing" and "said Associative" were the same value, and refreshing an
  authoritative fact lost decay-immunity, prune-immunity, its reserved recall slot and untruncated content.
  If you added a defensive "always restate the grade" to work around it, it is now belt-and-braces rather
  than load-bearing — and harmless to keep.
- **A re-remember applies several update rules to the fields around the content**, and they are worth
  knowing together. **The rule for everything you supply is the same: say it and it lands, leave it out and
  what is stored survives.** `Headline` and `Grade` are overwritten only when you NAME one; `Metadata` and
  `Signals` (and salience) keep what is stored when you supply nothing and REPLACE it wholesale when you do;
  `Difficulty` changes only when the signals bag names one; and `Stability`, `provenance_retrievability` and
  `CreatedAt` are never revisited — stability is what the retention policy has learned, and a re-remember is
  not a review.
  <br>**Replace, not merge**, for both bags: keys you do not restate are gone. Merging would make removing a
  key impossible. Through 3.1.0 `Metadata` was write-once instead — a correction was silently ignored
  (**D91**).
- **`Metadata` comes BACK on `MemoryItem`, and `null` is a statement about the ENGINE.** Graph and curated
  engines round-trip whatever you wrote; lexical and semantic return `null`, because `MemoryEntry` and a
  vector hit have nowhere to keep it. So `null` means *this engine does not carry metadata*, never *the caller
  wrote none* — the two are not distinguishable here, and if you need them apart, write a sentinel key.
  **A recall and an expansion answer alike**, including the entry an expansion was asked for.
  Through 3.1.0 it was write-only: stored, returned by the store, and dropped at all three of the projections
  onto `MemoryItem` (**D93**).
  <br>It stays a `string→string` bag deliberately rather than becoming a typed kind. A kind is your
  vocabulary, and Core stays neutral of it.
- **`taskKey` isolates every READ, and `LinkAsync` is the one way across.** No recall, expansion, subject
  seed, semantic seed, prune or forget crosses a task — pinned on all three backends
  (`MemoryGraphStoreContract.No_read_crosses_a_task_key`, `No_removal_crosses_a_task_key`) and end to end
  (`MemoryTaskIsolationTests`). The engine never links across tasks by itself either: co-activation links
  what one task-scoped recall returned, similarity links inside a per-task-and-scope vector collection, and
  subject linking looks up the write's own task.
  <br>**A cross-task link is REFUSED** (**D92**): `ILinkableMemory.LinkAsync` throws rather than writing
  an edge between two tasks, and traversal is scoped to the task besides — so an edge a pre-D92
  database already holds is never walked either. If you were relying on cross-task links, keep the
  association in your own data; two facts that belong together belong in one task.
- **Scale — MEASURED 2026-08-26, and no longer the blank it was.** `node devtools/dev.mjs memory-scale`
  runs the graph engine at 1k / 10k / 100k entries on SQLite. The headline: **write throughput does not
  degrade** — 210–260 entries/s at every size, unchanged across 100× the store — and **recall grows
  sub-linearly**, p50 `10.4ms → 18.5ms → 42.0ms` with p99 `77ms` at 100k. Storage is ~1 KB per entry
  (100 MiB at 100k) and a cold first recall costs 21 → 49ms. Every cell reports a hit-rate control, and it
  was `1.000` throughout — the latencies are real recalls, not fast misses.
  <br>**What is still NOT measured, stated separately because the numbers above make it easy to assume
  otherwise:** recall QUALITY at scale (that corpus has no ground truth and none of this speaks to miss or
  pollution), concurrency (single-threaded throughout), Postgres, and any model in the loop — an embedder,
  annotator or verifier would dominate every number here and none is wired.
  <br>**What a recall spends on LEARNING, settled with repeats.** The sweep splits a default recall's
  latency into the read and the write-back (reinforcement + co-activation edges + the review-log row) it
  performs afterwards. At 5 runs per cell that write-back is **75% of the p50 at 1k and 50% at 10k** — so
  the read path grows faster than the learning does, and learning's *share* falls as the store grows even
  though its absolute cost barely moves. A deployment that does not need it can turn it off
  (`ReinforceOn = None`, `CoActivationCap = 0`, `LogReviews = false`) and recall roughly halves.
  <br>**Those two percentages PREDATE `docs/DECISIONS.md` D101** and are left as measured rather than
  restated. D101 collapsed the write-back's three store calls into one — 3 connection opens and 2
  position-totals reads became 1 and 1 — so the share is expected to have fallen, and *expected* is as far
  as this document goes: nothing re-ran the sweep, and this instrument's own noise (below) is why a
  before/after pair taken once would not have settled it either. The claim D101 does make is a COUNT.
  <br>**The first run could not support that claim and said so**, which is the half worth keeping: at one
  cell per arm the 100k comparison came out NEGATIVE — `read-only` measured slower, which it cannot be —
  so the sweep printed "not readable" rather than an impossible percentage. Repeats fixed it. **The same
  run also showed absolute latencies moving ~2.5× between a busy machine and a quiet one**, which is why
  the growth factors are the thing to compare and the milliseconds are not.
- **Salience's admission priority** is inert in every test because no arm creates budget pressure.
- **Real-world recall quality.** The corpus defines relevance lexically and is synthetic throughout.
- **Parameter fitting.** Every `DsrOptions` constant is FSRS's published default, fitted against an external
  corpus, never against this library's own reviews — **with one exception, `ReinforceGain`, which 3.0 moved
  to `0` on a measurement taken here** (§6's Learning table, **D54**). The review log can now carry real
  outcomes, so the blocker is a deployment's data rather than a design question.
- **Abugida end-to-end recall.** Those scripts are measured for tokenizer discrimination only.

## 9. Recipes

Task-first, in the order you meet them. Every block here is compiled by `node devtools/dev.mjs
check-samples`, so a signature that drifts fails the build rather than misleading a reader.

### Store something and get it back

`RememberAsync` returns a `MemoryRef` — the handle you use to expand or link later. Recall returns
**headlines**, not full text; that is what makes the first load cheap.

```csharp
await engine.RememberAsync(new MemoryWrite("project", "backend", "the deploy gate is dev.mjs verify"));

var recall = await engine.RecallAsync(new MemoryQuery("project", "backend", "deploy", Limit: 5));
foreach (var item in recall.Items)
    Console.WriteLine($"{item.Headline}  (r={item.Retrievability:F2}, ref={item.Reference.Id})");
```

`TaskKey` and `Scope` are the two-level namespace: everything is stored and recalled within a
`(taskKey, scope)` pair, and a `null` scope means "the task's default".

### Keep a fact exactly, forever

An `Authoritative` entry never decays, is never truncated, is never removed by `PruneAsync` at any floor, and
takes a reserved slot inside a recall's limit. Use it for things that would be wrong to forget — identifiers,
rules, stable preferences.

```csharp
await engine.RememberAsync(new MemoryWrite("project", "backend",
    "the production database is db-prod-2 in eu-west-1",
    Grade: MemoryGrade.Authoritative));
```

Everything else defaults to `Associative`: it decays, competes, and can be buried. **That two-tier split is
the design** — §3 objective (1) applies only to the first tier.

### Read the full text of one entry, and what it is linked to

<!-- compile-given: IExpandableMemory expandable = null!; MemoryRef reference = default!; -->
```csharp
var expanded = await expandable.ExpandAsync(reference, hops: 2, charBudget: 4000);

var entry = expanded.Items[0];                    // the entry itself, full Content
var neighbours = expanded.Items.Skip(1);          // what it is connected to, as headlines
```

`hops` is clamped to the engine's configured `Hops`; `charBudget` bounds the neighbours and never the entry.

### Let a model search its own memory

`AddMemoryTools` exposes `<engine>_recall` and `<engine>_expand` to a tool loop, so the model decides when to
look things up instead of you pre-loading context.

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("Data Source=app.db")
    .AddMemoryEngine("project", e => e.UseGraph())
    .AddMemoryTools("project", taskKey: "project", scope: "backend"));
```

### Two engines that behave differently

Named engines carry independent configuration, so a fast-moving chat memory and a stable archive can share
one application and one database.

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("Data Source=app.db")
    // chat: reinforce on everything, forget quickly
    .AddMemoryEngine("chat", e => e.UseGraph(
        new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.All }))
    // archive: never reinforce, so nothing a query touches becomes more durable
    .AddMemoryEngine("archive", e => e.UseGraph(
        new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.None })));
```

### Turn the biggest quality lever on

See §6 for what this costs and what it buys — ~28 points of miss, ~1.5 s and 3.3 GB of VRAM.

```csharp
services.AddLyntai(cfg => cfg
    .AddOllamaProvider(baseUrl: "http://localhost:11434", defaultModel: "gemma3:4b")
    .UseDefaultCandidates("ollama")
    .AddMemoryEngine("project", e => e.UseGraph())
    .AddMemoryVerification(o => o.Model = "gemma3:4b"));
```

### Know when the memory has nothing useful

`Answered` is the abstention signal: `true` a judge found an answer, `false` a judge looked and found none,
`null` nothing judged. **Only meaningful with a verifier registered** — without one it is always `null`.

```csharp
var recall = await engine.RecallAsync(new MemoryQuery("project", "backend", "deploy"));

if (recall.Answered == false)
    Console.WriteLine("memory has nothing that answers this — don't put it in the prompt");
```

### Delete things, deliberately

Decay never deletes; it only buries. Removal is always an explicit call.

<!-- compile-given: GraphMemoryEngine graph = null!; -->
```csharp
// remove entries that have faded past the configured MinRetrievability
var removed = await graph.PruneAsync("project", "backend");

// erase a whole scope — the user-facing "forget this"
await graph.ForgetAsync("project", "backend");
```

`PruneAsync` never removes authoritative material at any floor. Set `MinRetrievability = 0` to make it remove
nothing on that criterion.

**Both verbs also clear the similarity index.** With an embedder and a vector store wired, each write is
indexed with its **full content as the payload**, so a removal that stopped at the graph store would leave
that content readable — `ForgetAsync` is the consent-withdrawal path and has to be complete. Nothing extra
to configure, and it needs no `IListableVectorStore`. Two consequences worth knowing: `ForgetAsync` clears
the index *before* the nodes, so a vector-store outage fails the call with the nodes intact rather than
half-forgetting; and pruning through the store's own path pays one extra scope read to learn which ids it
removed, which a deployment with no vector store does not pay.

### Blend two members that index the same material

A graph member for decay and links, a semantic member for meaning — over the same facts. Both hold
associative material, so by default the graph takes every write and the semantic store stays empty.

<!-- compile-given: class MyEmbedder : Lyntai.Embeddings.IEmbedder { public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<float[]>>([]); } -->
```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("Data Source=app.db")
    .UseSqliteVectorStore()
    .AddSemanticMemory(new MyEmbedder())
    .AddMemoryEngine("project", e => e.UseGraph().UseSemantic().FanOutWrites()));
```

Two members sharing a grade is the signal to reach for `FanOutWrites()`; the cost is one write (and one
embedding) per member. Leave it off when the members hold DIFFERENT grades — a curated catalog beside a
graph — because there the routing is already right and fanning out would store one fact at two grades.

### Compose a prompt from your own retrieval

`ComposeAsync` recalls and renders. If your application already selects its own material — a fused semantic
and keyword search of your own — `Render` is the second half on its own, with no engine involved.

<!-- compile-given: string basePrompt = ""; IReadOnlyList<MemoryItem> myItems = []; -->
```csharp
var prompt = MemoryComposition.Render(basePrompt, myItems, new MemoryCompositionOptions
{
    Budget = 4000,
    AuthoritativeCharacters = 1200,
});
```

Grades come off the items, so set `MemoryItem.Grade` yourself: authoritative material renders first, in its
own section, verbatim. **The reserve protects exact material from the BUDGET, not from your retrieval** — a
fact your own selection dropped cannot be rescued here, and the section still looks full.

**Pass `""` to get a standalone block** rather than an appended one — the blank line between prompt and
sections is a separator, so with no prompt it is not emitted and there is nothing to trim. Both uses are
first-class; a formatting-only entry point is as often used to build a block you place yourself as to append
to a prompt.

### Replace a policy with your own

Every seam is an interface plus a registration. Nothing here is a mode or a flag.

<!-- compile-given: class MyRanking : IMemoryRankingPolicy { public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates, in MemoryRankingContext context) => []; } -->
```csharp
services.AddSingleton<IMemoryRankingPolicy, MyRanking>();
```

Registered before or after `AddLyntai`, a container registration wins over the shipped default; an argument
passed to `UseGraph(...)` wins over both, for that engine only.

## 10. Where to look next

| you want | read |
|---|---|
| the contract — interfaces, semantics, objectives | `docs/2026-07-17-lyntai-design.md` §5.7 |
| why a choice was made | `docs/DECISIONS.md` D39–D62 and D83–D86 (and D13 for the *keyword* store's eviction bound, which is a different surface) |
| upgrading from 2.5 | `docs/migration-2.5-to-3.0.md` |
| the consuming story | `README.md` |
| traps that pass the build while being wrong | `.claude/knowledge/pitfalls.md` |
