# Long-term memory — how it works, what was measured, how to configure it

> **Maintained state.** This is the guide to the memory subsystem as it is TODAY. The contract (interfaces,
> semantics, objectives) is `docs/2026-07-17-lyntai-design.md` §5.7; the reasoning behind each choice is
> `docs/DECISIONS.md` **D39–D60**; the per-task history is `docs/task-archive.md`. When this page and the
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

## 3. What the engine is FOR

Design §5.7.0 states the objectives **lexicographically** — earlier lines are not traded for later ones:

1. **Never lose an authoritative fact.** The only objective with *no acceptable failure rate*.
2. **Return the relevant material** (miss rate).
3. **Do not return junk** (pollution rate).

Objective (1) is why `MemoryGrade.Authoritative` exists, and why it behaves the way §6 describes.

## 4. How a recall actually works

```
query
  ↓  SeedAsync            lexical candidates from the store (FTS/trigram/substring)
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

| judge | miss | pollution | vs reference |
|---|---|---|---|
| none | 0.5357 | 0.3331 | — |
| `llama3.2:3b` | 0.3643 | 0.176 | 60–69% |
| `qwen2.5-vl:7b` | 0.3071 | 0.1556 | 91.4% |
| `gemma3:4b` | 0.2571 | **0.0492** | 108–111% |
| ground-truth reference | 0.2857 | 0.1549 | 100% |
| Claude Haiku | **0.1857** | 0.1271 | **140%** |

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
novelty matches nothing (`TASKS.md` Part 69).

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

### Learning

| option | default | what it does |
|---|---|---|
| `Reinforcement` | `All` | which EFFECTS apply — `AgeReset`, `StabilityGrowth` |
| `ReinforceOn` | `All` | which CALLS reinforce — `Recall`, `Expansion` |
| `RecallReinforceCap` | `null` | how many of a recall's hits reinforce, from the top |
| `LogReviews` | `true` | record each reinforcement for later fitting |

`StabilityGrowth` without `AgeReset` **throws**: the store resets the age as part of the same write, so that
combination would apply neither effect.

### Model-backed steps (all opt-in, all fail-open)

| call | when | what it costs |
|---|---|---|
| `AddMemoryAnnotation()` | every WRITE | one model call; links entries about the same entity |
| `AddMemoryVerification()` | every RECALL | one model call; promotes buried answers |

Both take `ClientName` to point at a named `AddLlmClient`, so judging runs on a backend you size
deliberately. **Absent, the engine behaves exactly as it always has** — the model-free floor is a supported
configuration, not a degraded one.

### The seams you can replace

Every one is `IMemory<Domain>Policy` (**D47**), registered in DI or passed per engine:

| domain | plural? | default |
|---|---|---|
| age (interference) | plural | `BurstDampenedAgePolicy` |
| retrievability (forgetting) | singular | `DsrRetrievability` |
| retention (modulation) | plural | `SalienceRetentionPolicy` |
| salience | plural | `StructuralSaliencePolicy` |
| ranking | singular | `ReciprocalRankFusionPolicy` |
| verification | singular | none |

Plural domains coexist and are combined by a **composition policy**; the engine composes nothing itself
(**D48**). To turn salience OFF, register `NeutralSaliencePolicy` — **registering nothing takes the shipped
default instead**, which is the one trap in this table.

## 7. Things that will surprise you

Each of these cost a real measurement to find.

- **A small-limit recall can return fewer ordinary hits than you expect.** Authoritative facts take slots
  *within* the limit. That is objective (1) working, not a bug (**D56**).
- **Registering an empty policy collection does not disable a seam** — it takes the shipped default.
- **The review log records every returned entry, not every reinforced one.** A judge-rejected entry is
  logged with `Verified = false` and never touched, which is what lets the log contain failures at all.
  `null` (no verifier ran) and `false` (judged irrelevant) are **not** interchangeable.
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
  corpus, never against this library's own reviews. The review log can now carry real outcomes, so the
  blocker is a deployment's data rather than a design question.
- **Abugida end-to-end recall.** Those scripts are measured for tokenizer discrimination only.

## 9. Where to look next

| you want | read |
|---|---|
| the contract — interfaces, semantics, objectives | `docs/2026-07-17-lyntai-design.md` §5.7 |
| why a choice was made | `docs/DECISIONS.md` D39–D60 (and D13 for the *keyword* store's eviction bound, which is a different surface) |
| upgrading from 2.5 | `docs/migration-2.5-to-3.0.md` |
| the consuming story | `README.md` |
| traps that pass the build while being wrong | `.claude/knowledge/pitfalls.md` |
