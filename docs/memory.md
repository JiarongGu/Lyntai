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

Everything below is on this repository's own deterministic corpus, replayed against a live engine. It is a
**comparison instrument**, not a claim about your data: relevance in it is defined lexically, the shapes are
synthetic, and no arm exceeds a few hundred entries.

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

**Lower is better, monotonically, on every shape.** Under §5.7.0 that trade is accepted: miss is objective
(2) and pollution (3) is explicitly not co-equal, so a large miss reduction for a small pollution rise is the
correct direction.

This says nothing bad about salience — it says salience is not a *ranking* signal. **D45** reached the same
conclusion by argument, which is why `MultiplicativeRankingPolicy`'s rank boost defaults OFF: salience means
"does not fade away", and store admission already delivers that. RRF's own `SalienceWeight` shipping at `1.0`
is the inconsistency.

**The default has NOT moved**, because a ranking constant changes on a measurement and this is one run, one
corpus, one embedder, four coarse arms, with relevance defined lexically (D49, D54). Set it to `0` yourself
if this matches your corpus.

**The control worth copying if you build a sweep of your own:** the study reports *distinct salience values*
(352), not how often salience fired (98.9%). Firing is presence; only distinct values are discrimination, and
RRF ranks by competition (**D82**) — so a signal every candidate ties on contributes the same constant at
every weight, and the curve would be flat as an artifact with every ordinary control green.

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

## 8. What is NOT measured

Stated so nobody mistakes silence for a result.

- **Scale.** Nothing exceeds a few hundred entries, single-threaded. Salience's admission priority is inert
  in every test because no arm creates budget pressure.
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
