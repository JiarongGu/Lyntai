# Memory engine seam — named, composable memory systems (Spec A)

> **Status:** design, agreed 2026-08-08. **Not implemented** — no code exists.
> **Task:** `TASKS.md` Part 46 / **MEM1**.
> **Domain:** Core (`Lyntai.Memory`), consumed by `Lyntai.Cortex` (prompt composition) and
> `Lyntai.Agents` (tools). **Compatibility:** purely **additive** — new types, new registration
> extensions, no change to any released signature or semantic. Minor bump; not D24 material.
> **Companion:** `2026-08-08-graph-memory-engine-design.md` (Spec B) is the first new engine built on
> this seam. The two were designed together on purpose, so the seam is shaped by a real second
> implementation rather than only by wrappers over what already exists.

## 1. The problem

Lyntai has three memory systems and no way to have two of anything.

| Today | What it is | Instances |
|---|---|---|
| `IMemoryStore` | lexical remember/recall log, bounded, TTL | **one** singleton |
| `ISemanticMemory` | embedding recall by cosine similarity | **one** singleton |
| `ICuratedMemoryStore` | operator-curated catalog, per-`Kind` composition | **one** singleton |
| `MemoryPromptComposer` | semantic hits lead, lexical fills, 4000-char flat dump | **one** singleton |

Every one is registered as a single unnamed service, so an application that wants a *chat* memory and a
*project* memory — different retention, different content, different purpose — has to wrap all of it
itself. That wrapper is the duplicate code this spec deletes: it is the same wrapper in every consuming
application, and none of them can share it because it lives above the library.

The second problem is that `MemoryPromptComposer` composes by filling a flat character budget in rank
order. A burst of loosely-relevant recalled text can therefore push a hard constraint out of the prompt
entirely, and nothing reports it — the prompt still looks full. That is an *accuracy* regression hiding
inside a convenience feature, and §4 exists to prevent it.

## 2. What this ships

1. Named memory systems that **co-exist** — several live at once in one application, addressed by name.
2. A real **extension point**: a fourth kind of memory is a class plus a registration, never a `switch`
   edit and never a new set of bespoke interfaces.
3. **Mixed mode** — associative and authoritative material in one engine, with the authoritative half
   protected rather than merely preferred.
4. A **builder** so the common shapes are one line, and a **zero-config** entry point so the common case
   is no lines at all.

Explicitly *not* shipped here: any new storage. Spec A works entirely over the three memory systems that
already exist. Spec B adds the first new one.

## 3. Public surface

All of it lives in `Lyntai.Memory`, inside `Lyntai.Core`. No new package: the contracts belong in the
mandatory core package, and every implementation is either in Core (the wrappers, the composite) or in a
storage adapter that already exists.

### 3.1 The engine

```csharp
namespace Lyntai.Memory;

/// <summary>A named memory system: something that can be told a fact and asked for relevant ones.</summary>
public interface IMemoryEngine
{
    /// <summary>This engine's name. Unique within the container; hierarchical for nested members
    /// (see <see cref="MemoryRef"/>).</summary>
    string Name { get; }

    Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default);

    Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default);
}
```

**Where `Id` comes from, because not every store has one.** `IMemoryStore.RememberAsync` and
`ISemanticMemory.RememberAsync` both return `Task` — no identifier — while `ICuratedMemoryStore.AddAsync`
returns a row id. So an engine over a store with no id keys by the SHA-256 hex of the content, which is
also precisely how those stores define identity (re-remembering identical content refreshes rather than
duplicates), and the same value is produced on write and on recall. Nothing dereferences those ids: neither
wrapper implements `IExpandableMemory` or `ILinkableMemory`. An engine with real row ids (curated, and the
graph engine of Spec B) uses them.

Supporting records:

```csharp
/// <summary>An entry's address. <paramref name="Engine"/> is the name of the engine that OWNS the entry
/// — for a member of a composite this is the member's hierarchical name ("project/graph"), never the
/// composite's. That is what makes expansion route unambiguously.</summary>
public readonly record struct MemoryRef(string Engine, string Id);

public sealed record MemoryWrite(
    string TaskKey,
    string Scope,
    string Content,
    string? Headline = null,
    MemoryGrade Grade = MemoryGrade.Inherit,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MemoryQuery(
    string TaskKey,
    string? Scope = null,
    string? Query = null,
    int? Limit = null,
    int? CharBudget = null);

/// <summary>One recalled entry. <paramref name="Content"/> is null for an associative item that has only
/// been recalled, not expanded — that is the point of progressive retrieval. It is ALWAYS populated for
/// an authoritative item, which is never returned truncated (§4.3).</summary>
public sealed record MemoryItem(
    MemoryRef Ref,
    string Headline,
    string? Content,
    MemoryGrade Grade,
    double Relevance,
    double Retrievability,
    int Degree);

public sealed record MemoryRecall(IReadOnlyList<MemoryItem> Items, MemorySources Ran);

/// <summary>Which retrieval tiers actually ran, so a caller can tell "nothing matched" from
/// "that source is not configured" (`model-decoupling`: report which tier ran, on every result).</summary>
[Flags]
public enum MemorySources
{
    None = 0,
    Lexical = 1, Semantic = 2, Curated = 4, Graph = 8,
    /// <summary>Similarity-derived edge enrichment ran (Spec B §4) — distinct from
    /// <see cref="Semantic"/>, which means the semantic-memory MEMBER produced hits. Both need an
    /// embedder, and a caller needs to tell them apart.</summary>
    Similarity = 16,
}
```

### 3.2 Optional capabilities

Not every memory system can do everything, and an engine must not pretend. Extra abilities are separate
interfaces an engine may also implement:

```csharp
public interface IExpandableMemory
{
    Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1,
        int? charBudget = null, CancellationToken ct = default);
}

public interface ILinkableMemory
{
    Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null,
        double weight = 1.0, bool symmetric = false, CancellationToken ct = default);
}

public interface IForgettableMemory
{
    Task<int> PruneAsync(string taskKey, string? scope = null,
        double? minRetrievability = null, TimeSpan? olderThan = null, CancellationToken ct = default);
}
```

The lexical and semantic wrappers implement none of these. The curated wrapper implements none. The graph
engine of Spec B implements all three.

**This is the shape that already bit us once.** `.claude/knowledge/pitfalls.md` records that decorating a
generation provider erased its optional capability interfaces, because `GenerationRouter` type-tests
`IGenerationJobProvider` — so every video render silently stopped routing while every image render kept
working and every inline test stayed green. §3.4 states how the composite avoids repeating it, and §8
pins it with a test.

### 3.3 The factory

Resolution follows `IHttpClientFactory`, which is the pattern .NET developers already know — and which
this repository already cites as a model elsewhere (`pitfalls.md`, on expiry not being disposal).

```csharp
public interface IMemoryEngineFactory
{
    /// <summary>The engine registered under <paramref name="name"/>.
    /// Throws <see cref="KeyNotFoundException"/> naming the registered engines if there is none.</summary>
    IMemoryEngine Get(string name);

    /// <summary>The default engine — the one named "default", or the only one if exactly one is
    /// registered. Throws if neither holds.</summary>
    IMemoryEngine Get();

    bool TryGet(string name, out IMemoryEngine engine);

    IReadOnlyList<string> Names { get; }
}
```

**Where the `IHttpClientFactory` analogy stops, stated so nobody assumes otherwise:**
`CreateClient` returns a *new* client each call over pooled handlers. `Get` returns the *same* singleton
engine each call — engines are stateless over their stores, so there is nothing to pool and nothing to
dispose. The method is named `Get`, not `Create`, for exactly this reason.

Registration is a DI **collection** (`IEnumerable<IMemoryEngine>`), and the factory keys it by `Name`.
This mirrors `ILlmProvider` keyed by `Id` and picked by `ILlmRouter` — the established variation-point
shape in this repository, per `.claude/rules/dotnet-package-layout.md` §Variation points. Adding a fourth
engine kind is a class plus a registration; nothing existing is edited.

### 3.4 Composites, and hierarchical names

A blend of sources **is** an engine — `CompositeMemoryEngine : IMemoryEngine` over an ordered list of
member engines. There is no second "profile" or "composer" concept, which keeps the public type count
down; every public type is a permanent SemVer promise.

Members are themselves named engines, hierarchically:

```csharp
.AddMemoryEngine("project", e => e
    .UseCurated("glossary")     // engine "project/glossary"
    .UseCurated("style")        // engine "project/style"
    .UseGraph(...))             // engine "project/graph"
```

`label` defaults to the source kind — for a curated member that is the *catalog kind*, not the literal
string "curated", because drawing on two catalog sections is the ordinary case and a fixed default would
make it collide. Two members that would still share a name collide, and that is a **configure-time**
failure naming both, not a runtime surprise. Every member is individually addressable through
the factory, so `Get("project")` gives the blend and `Get("project/glossary")` gives one member.

**The composite's capability rule, which is the anti-repeat of the generation-router trap:** it always
implements `IExpandableMemory` and `ILinkableMemory`, and it never guesses — it routes strictly by
`MemoryRef.Engine` to the member that owns the entry. When that member does not implement the capability:

- `ExpandAsync` fails **open** — returns the entry itself with no neighbours.
- `LinkAsync` **throws** — a silently dropped write is worse than a visible failure, which is the
  asymmetry `ISemanticMemory` already documents.

## 4. Grade — mixed mode, and the accuracy guarantee

A language model system has to beat a human on *precision*, and human-shaped memory is good at
association and bad at precision. If associative recall competes on equal terms with facts that must be
exact, the result is worse than the flat dump it replaced — and it fails plausibly, which is the worst
failure mode available.

```csharp
public enum MemoryGrade { Inherit, Associative, Authoritative }
```

**One precedence rule: an explicit grade on the write wins; the member's role is the default.** A plain
`RememberAsync` never mentions grade, so an application that does not care never sees the concept.

Default roles: curated members are **authoritative**; lexical, semantic and graph members are
**associative**. Both are overridable per member at registration.

### 4.1 What grade actually changes

| Concern | Associative | Authoritative |
|---|---|---|
| Retrievability | decays (Spec B §3) | **fixed 1.0**, never decays |
| Reinforcement on recall | stability grows | no-op — nothing to reinforce |
| Headline | derived when not authored | **never derived**; recall returns full content |
| Budget | spends what is left | reserved pool first, then precedence |
| Edges / co-activation | normal | **normal** — grade affects ranking and rendering, not connectivity |
| `PruneAsync(minRetrievability:)` | eligible | **never eligible** — falls out of retrievability 1.0, no special case |

The "never derived" row is not a preference. A headline reading ``the build gate is `dev.mjs` `` when the
content says ``dev.mjs verify`` is worse than having no memory at all, because it is confidently wrong.

### 4.2 A write is routed, never downgraded

The lexical and semantic wrappers have nowhere to record a grade — their grade is a constant of the
store. So when a composite receives a write graded `Authoritative`, it **routes** the write to the first
member that can hold one. If no member can, it **throws**, naming the engine and the members it
considered.

"Can hold an authoritative write" is a property of the member, exposed on the engine so the composite
never has to type-test its way to an answer:

```csharp
public interface IMemoryEngine
{
    // …
    /// <summary>The grades this engine can store. A store with a grade column reports both; a store
    /// whose grade is a constant of the store reports only that one.</summary>
    MemoryGrades Supported { get; }   // [Flags]: Associative | Authoritative
}
```

The curated wrapper reports `Authoritative`, the lexical and semantic wrappers report `Associative`, and
the graph engine reports both. The composite reports the union of its members'.

Accepting an authoritative write and quietly storing it as associative is precisely the failure this
whole section exists to prevent, and it must never be reachable.

### 4.3 Budget: reserved, not first-come

`Reserve(n)` on a member allocates `n` characters to it **before** any associative content is admitted.
Associative content then spends what remains of the engine's `Budget`.

If reserve plus precedence still cannot fit every authoritative item, the composed text carries an
explicit line:

```
… 3 further authoritative facts omitted (budget)
```

The model is told and so is the log. A prompt that looks full while silently missing a hard constraint is
not acceptable.

### 4.4 Rendering

Composition emits labelled sections rather than one undifferentiated list, so the model is told which
material is exact and which is recalled:

```
## Known facts (authoritative)
- the build gate is `node devtools/dev.mjs verify`

## Recalled context (associative — may be stale or partial)
- user prefers terse commit messages
- GBK console mangles UTF-8 writes
```

The labelling is free, needs no model, and is the same "report which tier ran" discipline applied to the
prompt instead of to the return value. The authoritative section is **deterministic**: same task, same
scope, same bytes, no decay and no ranking — which is the property that makes it trustworthy for concrete
logic, and which §8 pins with a test.

## 5. Registration

### 5.1 Zero configuration

A seam is an escape hatch, never the answer to "how does this work". One line must produce a working
memory system with nothing to implement:

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage(...)
    .AddMemory());
```

`AddMemory()` registers one engine named `default` — graph if a graph store is available, otherwise
lexical + semantic — wires it as `ChatOrchestrator`'s `IPromptComposer`, and registers its tools.
`AddMemoryEngine("name")` with an empty callback does the same under another name.

### 5.2 The builder, for applications that want to differ

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage(...)
    .AddEmbeddings(...)                              // optional — enables similarity enrichment

    .AddMemoryEngine("chat", e => e
        .UseLexical()
        .UseSemantic()
        .Budget(1500))

    .AddMemoryEngine("project", e => e
        .UseCurated(kind: "glossary").Reserve(1200)
        .UseGraph(g => { g.Hops = 2; g.Reinforce = 0.5; })
        .Budget(3000))

    .AddMemoryTools("project")
    .UseMemoryComposer("chat"));
```

`Use*` order is the render order of the composed sections; authoritative members render first regardless
of position.

**There is no separate composer type.** Composing a prompt is something an engine does, exposed as an
extension method over `IMemoryEngine`, so "several composers" is already "several engines".
`UseMemoryComposer(name)` only decides which engine backs `ChatOrchestrator`'s default `IPromptComposer`;
everything else an application composes by hand from any engine, anywhere.

### 5.3 The whole knob budget

Composition: `Budget`, `Reserve`, per-member grade default. Retrieval: `Hops`, `MinRetrievability`,
`HeadlineChars`, `SimilarityK`. The **decay constants are not loose knobs** — they are `HalfLifeOptions`
behind the `IRetrievabilityPolicy` seam (Spec B §3.1), so an application can tune the numbers *or*
replace the curve, and neither choice freezes the other.

That distinction is the rule, not a special case. A **flag** that selects between two behaviours is two
consumers disagreeing, and belongs behind a seam. A **magic value** a caller might reasonably want
different is a property with a documented default — `library-api-design` is explicit about it, and an
*unmeasured* constant is the strongest case there is for exposing one. Every seam ships with a
registered default implementation, so implementing an interface is never on the path to *using* the
feature.

### 5.4 Two registration traps this design must not step in

**No `TryAdd` inside the builder callback.** `pitfalls.md` records that a `TryAddSingleton` reached during
`configure(builder)` beats `AddLyntai`'s own later registration, silently substituting parameterless
defaults for configured values — a real bug that 1427 tests missed and that mutation testing found. So
`AddMemoryEngine` records a factory into an options list, and engines are built in the `Register*` block
that owns them, resolving dependencies with `GetRequiredService<T>()`.

**A missing backing store fails at startup.** Naming `.UseGraph()` with no `IMemoryGraphStore` registered
throws at wiring time with a message naming the missing store, in the same spirit as the existing
`RequireGovernance` check. The alternative — a permanently empty memory section indistinguishable from
"nothing matched" — is the documented-but-unwired failure `pitfalls.md` already calls out.

## 6. Error handling

Reads fail **open**, writes fail **loud**.

- Recall degrades FTS → LIKE → most-recent → empty; only `OperationCanceledException` propagates, because
  cancellation belongs to the caller.
- One member faulting never sinks the engine: the composite logs and returns what the others produced,
  matching today's `MemoryPromptComposer`.
- An authoritative write is routed or throws (§4.2). Never downgraded.
- A member with no backing store throws at startup (§5.4).
- Authoritative content that cannot fit emits the omission line (§4.3).
- Every recall reports `MemorySources`, so an empty semantic tier is distinguishable from an absent one.

## 7. Compatibility and packaging

Additive at source and binary level: new interfaces, new records, new extension methods. `IMemoryStore`,
`ISemanticMemory`, `ICuratedMemoryStore` and `MemoryPromptComposer` are untouched and keep working exactly
as they do; an application that never calls `AddMemory`/`AddMemoryEngine` observes no difference.

No new package. Contracts and implementations both land in `Lyntai.Core`, so `check-packages` and
`check-bundle` have nothing new to register and the bundle's dependency closure does not move. The API
surface baseline changes and must be regenerated deliberately, which is the review gate that would surface
an application-specific leak.

## 8. Testing

- **`MemoryEngineContract`** — one set of facts run against every engine implementation (the three
  wrappers, the composite, and Spec B's graph engine), the way `CuratedMemoryStoreContract` already holds
  three backends to one contract. Per `storage.md`, contract facts are the deduplication mechanism here;
  a shared base class is not.
- **The accuracy test.** Flood the associative side with high-relevance noise and assert the authoritative
  fact is still present in the composed output, verbatim, in its own section. This is the test the whole
  of §4 exists for.
- **The capability-forwarding test.** Expand *through a composite* must reach the underlying graph. This
  is the direct analogue of the trap that shipped in the generation router; without it the same regression
  ships invisibly.
- **Determinism.** Repeated recall produces a byte-identical authoritative section.
- **Routing.** An authoritative write to a composite whose only capable member is third in the list lands
  in that member; a composite with no capable member throws.
- Budget allocation and section rendering as pure unit tests with fakes — no I/O.
- Deterministic fake embedder; no test spends a token. Every await that can block is bounded, so a
  regression arrives as an assertion rather than a hung `verify` (`pitfalls.md`).
- Beware the filter traps when running these: `dotnet test --filter` passes vacuously on a name that
  matches nothing, and `dev.mjs test -- --filter X` silently runs the whole suite. Read the matched/total
  count.

## 9. Deferred, with reasons

- **Per-engine databases.** Engines share the application's registered stores and separate by
  `(taskKey, scope)`, which is what that scoping is for. Physically separate stores is a *storage
  lifetime* problem that applies equally to all twelve store interfaces — the provider-side analogue was
  solved once, generally, by `Lyntai.Lifecycle` (`DECISIONS.md` D37), and the storage side deserves the
  same treatment rather than a memory-specific special case.
- **Ephemeral vs durable engine lifetimes.** Cheap to add later (a flag plus a clear-on-start step); no
  demand yet.
- **Per-entry confidence scores.** Nothing in the library can produce that number honestly, so it would
  be a knob every caller sets to 1.0.

## 10. Open questions

- **Tool naming.** `AddMemoryTools("project")` yields `project_recall` / `project_expand`. Chosen over one
  multiplexed `memory_recall(engine:…)` tool because the multiplexed form lets the model consult the wrong
  memory, which is the same accuracy failure §4 exists to prevent. Revisit if tool-count pressure is real
  in practice.
- **Whether `MemoryQuery` should carry a grade filter** ("authoritative only"). Probably yes for an agent
  that wants only hard facts; deferred until a caller asks, since every public member is permanent.
