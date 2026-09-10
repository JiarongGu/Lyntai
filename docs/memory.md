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

- **Candidate seeding is LEXICAL by default.** Without `AddMemorySemanticSeeds` registered, the vector store
  is consulted at *write* time only — novelty for salience, and similarity linking — so an embedder cannot
  reach a fact whose wording shares nothing with the query. Measured with a real embedding model against
  paraphrase cues: **0 of 3**, identical to no embedder. Registering the semantic seed source
  (`SemanticSeedOptions.K`) embeds the query and joins its nearest entries to the candidate set, carrying
  their cosine as `Relevance`; the paraphrases then become reachable. **Reachable is not the same as
  returned** — see below.
- **Registering an embedder changes recall even with the semantic seed unregistered, and it is a TRADE
  rather than a cost.** Both write-time mechanisms were measured separately (`node devtools/dev.mjs memory-enrichment`,
  a real model), and they behave differently enough that a single verdict would mislead:
  **similarity linking is a redistribution** — it roughly halves misses on entries that cluster with
  others (topical material, an attribute cluster reached by its subject) and it badly hurts the
  rare-but-critical entry that clusters with nothing, because the edges it adds pull traversal toward the
  crowd: `topical` **−0.30**, `attribute` **−0.28**, `critical-rare` **+0.68**, an aggregate that looks
  small only because those cancel. **Novelty feeding salience is a broad, shallow cost** that only turns
  positive when there is a lot of noise to discriminate against.
  <br>So the question to ask of your own corpus is not "is an embedder worth it" but **"is my important
  material clustered or isolated?"** If the facts that matter most are the ones nothing else resembles, the
  linking half is working against you, and `MinSimilarity` is the knob — it is the link floor, and a value
  above `1` keeps novelty and indexing while writing no similarity edge at all.
  <br>**That recipe also zeroes `SalienceContext.SimilarCount`**, which counts against the same floor: a
  registered `IMemorySaliencePolicy` then reads `0` on every write and cannot tell that from a store nothing
  resembles. Novelty is unaffected — it reads the probe's top score, not this floor.
- **A SUBJECT is readable, not just writable.** `AddMemoryAnnotation` records what each fact is *about*;
  a recall matches its query against the handles in use and seeds the entries recorded under whichever ones
  it names, so a query for `配偶` reaches the fact whose text says `太太`. **On by default**
  (`SubjectSeedOptions.K` — `AddMemoryEngine` registers this channel unconditionally, `AddMemorySubjectSeeds`
  only configures it), unlike the semantic seed, which `AddMemorySemanticSeeds` must register — a subject
  exists only because an annotator was registered and paid for, so reading it back needs no second opt-in.
  Matching is per-script: a handle in a space-writing script needs a
  word boundary (`pairbond` must not match `repairbonded`), one in a spaceless script matches as a
  substring. Seeded entries are ordinary candidates — ranked, limited, and not appended past the page.
- **Reinforcement follows the verdict, the review log follows the recall.** What gets touched and what gets
  logged are deliberately different sets — see §7.

## Measurements — moved to `docs/memory-measurements.md`

**Every measured figure now lives in [`docs/memory-measurements.md`](memory-measurements.md), behind a
generated results index.** It was 3,459 of this file's 4,248 lines, which is what made the contract
unreadable end to end. It keeps the heading `## 5.` there, and the sections below keep their numbers:
renumbering makes an existing `§` citation resolve silently to the WRONG section, so the gap at 5 is
deliberate (`docs/DECISIONS.md` **D114**).

**Cite `docs/memory-measurements.md` §5, never a bare `§5`.** A bare one is invisible to `check-links`,
whose pattern needs a filename before the `§` — and this split is what turned eight of them in this
file into silent lies in a single commit (`.claude/knowledge/pitfalls.md` §Refactoring & namespace
moves).

## 6. Configuration reference

### Recall shape

| option | default | what it does |
|---|---|---|
| `DefaultLimit` | 10 | entries a recall returns |
| `Hops` | 2 | edge-traversal depth |
| `CandidateMultiplier` | 4 | candidates fetched per returned slot |
| `AuthoritativeReserve` | `null` (unbounded) | slots exact facts may take **within** the limit |
| `SemanticSeedOptions.K` | not registered by default (`AddMemorySemanticSeeds`; `K` = 20 once added) | entries the query's embedding pulls into the candidate set |
| `SubjectSeedOptions.K` | 5 (channel registered by `AddMemoryEngine`; `AddMemorySubjectSeeds` only configures it) | entries each SUBJECT the query names pulls into the candidate set |
| `SubjectSeedOptions.Scan` | 256 | handles a recall matches its query against |

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

**These two ask the model DIFFERENT SHAPES of question, and the shape predicts more than the size does** —
annotation extracts handles out of free text, verification selects from a visible list, and only the first
of those takes a budget from its prompt. `docs/model-tasks.md` is the inventory: every model-backed seam in
the library by shape, what is measured about each, and why list LENGTH is the variable to watch here.

#### What it costs you NOT to use a judge

The model-free floor is supported, and it is also where the single largest measured gain sits. From the
table in `docs/memory-measurements.md` §5, on this repository's corpus:

| configuration | miss | pollution | what you pay |
|---|---|---|---|
| **no judge** (shipped default) | 0.5357 | 0.3331 | nothing |
| `gemma3:4b` local | **0.2571** | **0.0492** | ~1.5 s/recall, 3.3 GB VRAM, $0 |
| Claude Haiku via CLI | **0.1857** | 0.1271 | 3.0 s/recall, **$66 per 1,000 recalls** |

**Not registering a judge costs ~28 points of miss** — more than every ranking-policy decision in this
library combined, which move recall by hundredths. The reason is the decomposition in
`docs/memory-measurements.md` §5: **0% of misses are retrieval failures**; the answers are already in the
candidate set and merely ranked below the cut. Nothing
inside the library's own arithmetic fixes that, which is why the seam exists.

> **READ THIS BEFORE TURNING IT ON. The table above is this repository's own SYNTHETIC corpus, and on the
> FIELD benchmarks a small local judge at the shipped defaults is net NEGATIVE.** Measured on LoCoMo,
> n = 200, `gemma3:4b` (`docs/memory-measurements.md` §5, 2026-09-03):
>
> | configuration | evidence-hit | against no judge |
> |---|---|---|
> | no judge (`+sem+rel-only`) | **83.0%** | — |
> | judge, **shipped** depth + combination | 72.5% | **−10.5** |
> | judge, `VerdictCombination = Fuse` | 83.0% | 0.0 |
> | judge, `VerificationDepth` halved (40) | **84.0%** | **+1.0** |
> | a PERFECT judge (oracle ceiling) | 92.5% | +9.5 |
>
> **So the seam's VALUE is real and this model does not earn it.** The ceiling says +9.5 is there; the 4B
> model recovers at best +1.0, and at the shipped defaults it destroys 10.5. **The mechanism is measured**:
> on the 19 calls of 200 where a verifier could possibly help, it ranked the deep evidence in its own top
> five **zero** times — its confidence tracks what the ranking already found
> (`docs/memory-measurements.md` §5).
>
> **The actionable rule, if you register one anyway:** halve `VerificationDepth` or set
> `VerdictCombination = Fuse`. The shipped depth factor of 4 was fitted against an ORACLE, for which depth is
> free because it never endorses junk; for a real judge depth is a PRECISION trade and the same model is
> level with no judge at 2×.
>
> **`Fuse` is insurance and it is priced at both ends** (`docs/memory-measurements.md` §5): it costs a
> PERFECT judge 2.0 points and saves a 4B one 10.5. So the shipped `Partition` is a bet that your judge is
> good — take it knowingly, and if you
> are running a small local model the odds are about 5:1 against you.

So the honest framing is not "the judge is an optimisation" but: *this engine's ranking is its weakest part,
and a judge is the only shipped mechanism aimed at repairing it* — with the field caveat above on whether a
given model actually does. Whether ~1.5 s and 3.3 GB is worth it is an application question, and it should
be answered knowing both numbers rather than the encouraging one.

#### Choosing the model — three criteria, in this order

Learned by measuring seven models on one machine (`docs/memory-measurements.md` §5). Every one of them is
a *disqualifier*, not a
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
(**fail-open**, `docs/memory-measurements.md` §5). `GraphMemoryOptions.VerificationFilters` then decides
whether a verdict merely reorders or also drops, and `VerificationDepth` how many candidates it sees.

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
  similarity linking run, every write is embedded, and the semantic seed source still is not registered by
  default (`AddMemorySemanticSeeds`), so no recall reads any of it. That gap is now reported at wiring time
  (**D85**) — but the shape is worth knowing, since the bill arrives per write and the benefit does not
  arrive at all until you register the source.
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
- **An embedder alone does not give you semantic recall**, and **registering the semantic seed source alone
  does not either.** It is what consults the vector store at query time, and it is not registered by
  default. Registered, it makes a paraphrase *reachable* — but **no shipped ranking configuration will spend
  a slot on it**: measured against RRF's defaults, an 8× relevance weight, `K = 1`, both together, and
  `MultiplicativeRankingPolicy`, the paraphrase is outranked by recent unrelated material in every one.
  <br>That is a limitation of the source as shipped, not a tuning gap you can close from configuration.
  **`SemanticSeedOptions.K` is useful in combination with `AddMemoryVerification`** — seeding widens the
  candidate set, the judge is what promotes from it. Pinned by `SemanticSeedProbeTests`, which asserts the
  negative for all five configurations so a future ranking change forces this claim to be re-read.
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
  was `1.000` throughout — the latencies are real recalls, not fast misses (a recall matching nothing is
  fast, and a table of fast empty recalls reads as good news).
  <br>**What it covers that `MemoryRecallBenchmarks` did not**, which is why the blank existed at all: that
  benchmark runs 1k/10k/100k against `SqliteMemoryStore`, the KEYWORD store, so the graph engine's own write
  and read paths were unmeasured at any size. The two arms — `shipped` and `read-only` — exist to SPLIT a
  default recall's latency into the read and the write-back it performs afterwards. It runs **sequentially**
  where every other sweep fans out, because contention cannot bias a rate and biases a latency silently.
  <br>**What is still NOT measured, stated separately because the numbers above make it easy to assume
  otherwise:** recall QUALITY at scale (that corpus has no ground truth and none of this speaks to miss or
  pollution), Postgres, and any model in the loop — an embedder, annotator or verifier would dominate every
  number here and none is wired.
  <br>**CONCURRENCY was on that list until 2026-09-07** and is now measured — `memory-scale --concurrency`,
  1k, five repeats per cell:

  | arm | workers | p50 | p99 | recalls/s |
  |---|---|---|---|---|
  | `shipped` | 1 | 4.7ms | 45.9ms | 155 |
  | `shipped` | 8 | 4.4ms | **1061.6ms** | 160 |
  | `read-only` | 2 | 1.8ms | 3.5ms | **1024** |
  | `read-only` | 8 | 19.9ms | 33.1ms | 368 |

  **A default recall is WRITER-BOUND, and concurrency buys nothing while costing the tail everything.**
  Throughput is pinned near 160/s at every worker count — SQLite is single-writer under WAL and a default
  recall ends in a write-back — while p99 climbs **23× past a full second**. **Zero errors at every level**,
  which is the part a deployment feels: a 5s `busy_timeout` under a 30s command timeout turns the lock into
  latency, so nothing reaches an error log. **Read `ReinforceOn = None` as a concurrency knob**, not only a
  latency one.
  <br>**The waiting is now MEASURED rather than inferred, and it holds for only one of the two arms.** The
  sweep reports CPU over wall time, and `shipped` keeps **0.2 cores** busy at every worker count — threads
  genuinely blocked on the writer, which is what the paragraph above claims. `read-only` keeps **1.0 / 1.9 /
  4.1 / 7.7**, so those threads are running flat out while throughput falls. A single "contention here
  waits" reading covers the write-back arm and is wrong about the read one.
  <br>**And pure reads do not scale either, which WAL says they should**: `read-only` peaks at TWO workers
  and falls to 368/s by eight, on a 22-core machine. **One explanation was tested and REFUTED**: every
  connection open issues `PRAGMA journal_mode=WAL` (`SqliteConnectionFactory`), and setting the journal mode
  takes a database lock even when it is a no-op — but making it run once per factory moved every cell inside
  its own spread (`shipped` 8-worker p99 1061.6 → 1115.1; `read-only` 2-worker rate 1024 → 1080). The
  experiment was reverted.

  <br>**ANSWERED 2026-09-08, and it is not a lock at all: it is SQLite's global memory-allocation
  STATISTICS.** Maintaining them takes a process-global mutex on every allocation and free, and SQLite
  allocates heavily inside an FTS5 query, so concurrent readers serialise on the counter. Turning them off
  (`SqliteRuntime.DisableMemoryStatistics`, **D107**) changes nothing else about the run:

  | workers | 1 | 2 | 4 | 8 | 16 |
  |---|---|---|---|---|---|
  | recalls/s, shipped | 744 | 1,060 | 754 | 340 | 216 |
  | recalls/s, statistics off | 858 | 1,712 | 2,842 | 4,665 | **6,275** |
  | scale, statistics off | 1.00× | 1.99× | 3.31× | 5.43× | **7.31×** |

  **The peak at TWO WORKERS disappears** and the curve becomes monotonic; one thread is unaffected, so this
  buys concurrency rather than speed. Reproduced across three interleaved rounds at eight workers with
  non-overlapping spreads (on 330/320/317, off 3,154/4,056/4,060).

  <br>**The chain that got there, because every step was a refutation and each one is worth not repeating.**
  It is NOT the connection open (one connection per worker, zero opens in the timed loop, same collapse),
  not GC (0% pause, allocation flat at 81 KB/op, and Server GC changes nothing), not exceptions (zero
  first-chance), not the WAL (checkpointing 4 MB to 0 changes nothing), not journal mode, not the
  measurement window (a 10× window with a warmup reproduces it at `hit` 1.000), not the engine (raw
  `SeedAsync` with no engine code collapses identically), and **not any shared state** — one engine, one
  store and one database file per worker collapse exactly as the shared ones do. What located it was that
  eight separate PROCESSES deliver 4,422 recalls/s where one process with eight workers delivers 431:
  process-global, which is what a per-database isolation ladder can never reach and what a second process
  gets for free.

  <br>**Lyntai does not turn it off for you**, because `sqlite3_config` is the whole process's and it
  disables `sqlite3_memory_used`, `sqlite3_status` and the heap limits for the host's own SQLite too
  (**D107**). Nothing in this library reads them.
  <br>**What a recall spends on LEARNING, settled with repeats.** The sweep splits a default recall's
  latency into the read and the write-back (reinforcement + co-activation edges + the review-log row) it
  performs afterwards. At 5 runs per cell that write-back is **75% of the p50 at 1k and 50% at 10k** — so
  the read path grows faster than the learning does, and learning's *share* falls as the store grows even
  though its absolute cost barely moves. A deployment that does not need it can turn it off
  (`ReinforceOn = None`, `CoActivationCap = 0`, `LogReviews = false`) and recall roughly halves.
  <br>**RE-RUN 2026-09-07, and the share did NOT fall.** Those percentages predated **D99** and **D101**,
  and this document said the share was *"expected to have fallen"* while conceding a once-taken before/after
  could not settle it. At the baseline's own 5 repeats it reads **76% at 1k and 49% at 10k** — the recorded
  75% and 50%, reproduced. **So the round-trip COUNT fell and the latency share did not**, which is
  consistent with D101 rather than against it: the claim D101 makes is a count, and this says the write-back's
  cost is not dominated by how many store calls it takes.
  <br>**A single-repeat run of the same sweep said otherwise, and it was noise** — 71% / 45%, an apparent
  4–5 point improvement that vanished under repeats. That is the trap `pitfalls.md` records for a p50 moving
  less than its own run-to-run spread, met again by the person who had just quoted the warning. **The 100k
  cell is the sharper case**: 35% at one repeat against **7%** at five, with the two arms' p50 spreads
  (32.9–36.3ms and 29.4–32.8ms) overlapping outright.
  <br>**At 100k the write-back is no longer the story.** It costs 2.5ms of a 33.8ms recall, because the READ
  path is what grows — `read-only` recall p95 runs ×11.02 from 1k→100k against `shipped`'s ×4.58, so the
  share falls by the denominator rising rather than the numerator dropping (4.2 → 4.0 → 2.5ms).
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

### Turn the judge on — and the two options to set with it

**See §6 before copying this.** On this repository's synthetic corpus a judge is worth ~28 points of miss;
on the FIELD benchmarks a small local model at the SHIPPED defaults measured **−10.5 points**, and the two
options below are what separates those outcomes. ~1.5 s and 3.3 GB of VRAM per recall either way.

```csharp
services.AddLyntai(cfg => cfg
    .AddOllamaProvider(baseUrl: "http://localhost:11434", defaultModel: "gemma3:4b")
    .UseDefaultCandidates("ollama")
    .AddMemoryEngine("project", e => e.UseGraph(new GraphMemoryOptions
    {
        // The shipped depth factor of 4 was fitted against a PERFECT judge, for which depth is free
        // because it never endorses junk. A real small model loses precision on a long list: halving
        // the depth took the same model from -10.5 points to +1.0 (docs/memory-measurements.md).
        VerificationDepth = 40,

        // ...or leave the depth alone and stop the verdict PARTITIONING the page, which removes the
        // same loss. Either one; both together is untested.
        VerdictCombination = MemoryVerdictCombination.Fuse,
    }))
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
| which SHAPE of question each model-backed seam asks, and what is measured about each | `docs/model-tasks.md` |
| traps that pass the build while being wrong | `.claude/knowledge/pitfalls.md` |
