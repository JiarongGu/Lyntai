# Lyntai (灵台) — Design Spec

> 灵台 (língtái, "the numinous platform") — a classical Chinese name for the seat of the mind.
> Lyntai is the shared **cortex + persistence** substrate the sibling apps plug into.

Status: **approved design — SHIPPED and amended** (see the dated amendments in §6/§9; the 2026-07-26 one
reconciles the later pre-1.0 line) · Date: 2026-07-17 · Scope: *Brain + persistence core → platform kit (D11)*

> **How to read this doc post-1.0-track:** it governs *semantics* (the rules code must follow); the
> public-API baselines (`tests/Lyntai.Tests/Api/Baselines/`, D8) govern *shape*. Where a §5 snippet
> differs from the baseline, the baseline is current and the snippet is the v0.1 seed kept for its
> semantic commentary.

---

## 1. Why this exists

Four sibling projects (Gatherlight, Vidora, Sonora, Odysseus) each re-implemented the same two
things from scratch: **how to talk to an LLM** and **how to persist agent state**. Each did one
part well and one part poorly:

- **Gatherlight** (net10.0, ASP.NET Core + SQLite) — clean module/Dapper/FluentMigrator/FTS5 storage,
  `IScorer`/`LlmScorerBase` scoring, run traces, MCP tools. **But the LLM layer is hardcoded to the
  `claude` CLI — no provider abstraction.**
- **Vidora** (net10.0) — the **best provider abstraction**: `ICortexClient` (timeout + structured
  output + language + vision), `ILlmProvider` with local (LLamaSharp) + OpenAI-compatible impls,
  `IPromptRegistry`, `LearnedScoring` (EMA).
- **Sonora** (net10.0) — `LlmClient` with **verdict classification** (Ok/RateLimited/Refused/Failed)
  and rate-limit circuit-breaking, task-scoped `IAiMemoryStore` + `IPromptComposer`, and a real
  `Sonora.Plugin.Sdk` project (they already extract contracts).
- **mastra** (TypeScript, the design study) — **composable domain-based storage**: one interface per
  domain, many backend adapters as **separate packages**, wired through a central registry. The
  reference for "one interface, many storage backends, separate packages."
- **odysseus** (Python) — the **best fallback logic**: streaming-aware fallback (only retry *before*
  the first token), dead-host cooldown instead of exponential backoff, candidate dedup.

**Lyntai** extracts the union of the good parts into one reusable .NET library so a new project gets
LLM + storage + LLM-ops for free — `AddLyntai(...)` and go, no rebuild.

## 2. Goal & non-goals

**Goal:** a NuGet-packable, DI-first .NET (net10.0) library providing (a) an LLM provider abstraction
with routing + fallback across CLI / API / bridged providers, (b) pluggable storage (SQLite now,
interfaces so other backends can follow as separate packages), (c) the LLM-ops layer (prompt
registry, scoring/eval, run traces, task-scoped memory), and (d) config + DI wiring — all generic,
no domain assumptions.

**Non-goals (this cut):** two-gate chat orchestration, scope-guard/jail hooks, tool/MCP registry,
durable jobs, security/access-gate, server/host/launcher, vision/multimodal, and the LLamaSharp
`Local` provider. These are the "Full platform kit" and are explicitly deferred (§9).
*(2026-07 note: every one of these except the server/host/launcher has since SHIPPED — see the dated
amendments in §9. This paragraph is the v0.1 scope, kept for the record.)*

## 3. Architecture — packages

Each `src/*` is an independently NuGet-packable project. `Lyntai.Core` has no heavy dependencies;
every provider/storage adapter depends only on `Lyntai.Core`, never on each other.

```
Lyntai/
├─ Lyntai.slnx
├─ src/
│  ├─ Directory.Build.props            # net10.0, nullable, UTF-8 (CodePage 65001), packable, version
│  ├─ Directory.Packages.props         # central package versions
│  ├─ Lyntai.Core/                     # interfaces + router/fallback + cortex + DI. No heavy deps.
│  ├─ Lyntai.Storage.Sqlite/           # Dapper + FluentMigrator + FTS5 impls of every store domain
│  ├─ Lyntai.Providers.ClaudeCli/      # authenticated `claude` CLI spawn (family hygiene)
│  ├─ Lyntai.Providers.OpenAiCompatible/  # HttpClient: OpenAI/Ollama/OpenRouter/…, URL-native detect
│  └─ Lyntai.Providers.ExtensionsAi/   # bridge: Microsoft.Extensions.AI IChatClient → ILlmProvider
├─ samples/
│  └─ Lyntai.Playground/               # console app exercising the full stack (live smoke)
├─ tests/
│  └─ Lyntai.Tests/                    # xUnit: unit + integration (temp SQLite, stubbed provider)
├─ devtools/                           # generalized dev.mjs + e2e harness + check-sensitive + hooks
├─ docs/                               # this spec + implementation plan
├─ TASKS.md                            # ACTIVE backlog (open tasks only; completed → docs/task-archive.md)
├─ CLAUDE.md + .claude/rules/          # conventions retargeted to Lyntai
└─ .gitignore
```

`Lyntai.Providers.Local` (LLamaSharp, in-process) is a **later** package, not first-cut.
*(2026-07 note: shipped in v0.8.0. It earns its own package by the rule below — LLamaSharp drags a native
runtime, which is exactly the footprint a consumer might refuse.)*

> **Amendment (2026-08-05): the tree above is the v0.1 cut; `src/` now holds TWELVE packable projects.**
> The RULE it illustrates is unchanged and still verified — every adapter references `Lyntai.Core` only, never
> another adapter — but two of the names are gone. `Lyntai.Providers.ClaudeCli` and
> `Lyntai.Providers.OpenAiCompatible` merged into **`Lyntai.Providers.Default`** at 2.0.1, because a boundary
> has to answer *which dependency does this isolate?* and those two isolated nothing: process spawn plus
> `HttpClient`, both dependency-free, and the CLIs share one `CliProviderEngine` (`docs/DECISIONS.md` **D25**;
> a new CLI backend is an `ICliProviderDialect` in that package, D21/D22). Today: `Lyntai.Core`,
> `Lyntai.Providers.Default`, `Lyntai.Providers.ExtensionsAi`, `Lyntai.Providers.Local`,
> `Lyntai.Storage.Sqlite`, `Lyntai.Storage.Postgres`, `Lyntai.Storage.InMemory`, `Lyntai.Secrets.Dpapi`,
> `Lyntai.Tools.Mcp`, `Lyntai.Tools.Mcp.Hosting`, `Lyntai.Generation`, and the `Lyntai` starting bundle
> (`src/Lyntai.Bundle/`, which ships no assembly).
> `Lyntai.Generation` — the media BACKENDS — was split from `Providers.Default` for release CADENCE
> (**D25**), and that reason lapsed in 3.0 when the carve-out it depended on was withdrawn (**D70**). The
> boundary stands on dependency isolation after all: the package sits outside the `Lyntai` bundle, so a
> one-line install does not drag the media backends for a feature most applications never use.
> Its contracts stay in `Lyntai.Core` under the `Lyntai.Generation` namespace and carry the full
> promise; see §5.6. Bundle membership is a DEPENDENCY BUDGET, not a preference (**D26**), and many small
> packages is the intended shape, paid for in tooling rather than in merging (**D27** —
> `node devtools/dev.mjs check-packages` gates the nine registries a package must enter).

### Dependency graph
```
Lyntai.Core
   ↑           ↑              ↑                    ↑
Storage.Sqlite  Providers.ClaudeCli  Providers.OpenAiCompatible  Providers.ExtensionsAi
```
No adapter references another adapter. Consumers compose via DI.
*(2026-08-05: same shape, today's names — each of the TEN adapter packages listed in the amendment above
project-references `Lyntai.Core` and nothing else, `Lyntai.Generation` included. The `Lyntai` bundle is the
only project that references several, which is what makes its membership a budget. Verified against the
`src/*/*.csproj` files.)*

## 4. Fork decisions (locked)

**Fork 1 — LLM seam = Hybrid (own seam + MEAI bridge).** Lyntai's own `ILlmProvider` is the primary
seam, so **CLI-first, `LlmVerdict` classification, and streaming-aware fallback are first-class**.
`Lyntai.Providers.ExtensionsAi` ships a thin bridge that turns any `Microsoft.Extensions.AI`
`IChatClient` into an `ILlmProvider`, giving the whole MEAI ecosystem (OpenAI, Azure, Ollama,
Anthropic API, …) for free without shaping the public API around MEAI's types.

**Fork 2 — storage = per-domain interfaces + one SQLite package.** Domain interfaces live in Core;
`Lyntai.Storage.Sqlite` implements all of them. Interfaces are designed so a mastra-style *composite
store* (route each domain to a different backend) can be layered on later without breaking consumers.

## 5. Core surface (interfaces)

### 5.1 LLM
```csharp
public enum LlmVerdict { Ok, RateLimited, Refused, Failed, Timeout }

public sealed record LlmRequest {
    public required IReadOnlyList<LlmMessage> Messages { get; init; }
    public string? Model { get; init; }           // provider resolves null → its default
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public string? JsonSchema { get; init; }       // structured output (optional)
    public IReadOnlyList<LlmTool>? Tools { get; init; }
    public string Consumer { get; init; } = "default";  // per-feature routing/telemetry tag
}

public sealed record LlmReply(string Text, LlmVerdict Verdict, LlmUsage? Usage = null, string? Detail = null);

public interface ILlmProvider {
    string Id { get; }                             // "claude-cli" | "openai" | "ollama" | …
    bool IsAvailable { get; }
    Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default);
    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default);
}

// Ordered candidates → fallback. See §6 for the routing semantics.
public interface ILlmRouter {
    Task<LlmReply> CompleteAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default);
    IAsyncEnumerable<LlmChunk> StreamAsync(IReadOnlyList<LlmCandidate> candidates, LlmRequest req, CancellationToken ct = default);
}
public sealed record LlmCandidate(string ProviderId, string? Model = null);
```
*(2026-08-05: `LlmVerdict` now has **nine** members — the five above plus `ContextWindowExceeded`,
`AuthFailed`, `Unsupported` and `NotConfigured`. **`src/Lyntai.Core/Llm/LlmVerdict.cs` is the canonical
statement**; §9's 2026-07-26 amendment lists the additions and §6 gives each one's routing action. The block
above is the v0.1 seed, kept for its semantic commentary per the reading note at the top of this doc.)*

### 5.2 Prompt registry
```csharp
public interface IPromptRegistry {
    // override-by-key (from IKeyValueStore: "lyntai.prompt.<name>") + {placeholder} fill.
    Task<string> RenderAsync(string name, string defaultTemplate,
        IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default);
}
```
Contract guard: an override that drops a `{placeholder}` present in the default is rejected (silent
content loss otherwise — Gatherlight's placeholder guard).

### 5.3 Cortex / LLM-ops
<!-- compile-skip: a v0.1 seed snippet kept for its semantic commentary — the API baseline is current (see the preamble) -->
```csharp
public interface IScorer {
    string Id { get; } string Name { get; } string Group { get; } bool IsLlm { get; }
    Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct);   // null = not applicable
}
public abstract class LlmScorerBase : IScorer { /* one-shot judge, {score,reason} verdict */ }
public interface IScoringService {                 // iterates IEnumerable<IScorer>, no if/else
    Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, CancellationToken ct = default);
}
public interface ITraceService {                   // run timeline: phase/tool/usage/error steps + tokens + cost
    ITraceRecorder Begin(string sessionId, string mode);
    Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default);
}
```

### 5.4 Storage domains (interfaces in Core; SQLite impl in the storage package)
```csharp
public interface IKeyValueStore {                  // lyntai_kv: prompt overrides, model routing, flags
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
public interface IConversationStore { /* threads + a typed event stream per thread — ChatMessage(GUID Id, per-thread
                                        Seq, Kind, Payload, Metadata); Role/Content aliases for plain chat. Lyntai OWNS
                                        the schema; add app info via thread/message Metadata + IConversationEnricher,
                                        not by owning tables (BYO-impl is the escape hatch). */ }
public interface IMemoryStore { /* task-scoped learned facts, bounded, fail-open, FTS recall */ }
public interface IScoreStore { /* persisted scorer results */ }
public interface ITraceStore { /* run traces + steps */ }
```

### 5.5 Config + DI
<!-- compile-given: IChatClient chatClient;
     string dbPath;
     sealed class MyScorer : IScorer
     {
         public string Id => "my";
         public string Name => "My scorer";
         public string Group => "quality";
         public bool IsLlm => false;
         public Task<ScoreResult?> ScoreAsync(ScoreContext c, CancellationToken t = default)
             => Task.FromResult<ScoreResult?>(null);
     } -->
```csharp
services.AddLyntai(cfg => {
    cfg.AddClaudeCliProvider();                          // family default, no API key
    cfg.AddOpenAiCompatibleProvider("ollama", o => o.BaseUrl = "http://localhost:11434");
    cfg.AddExtensionsAiProvider("openai", chatClient);   // bridge any IChatClient
    cfg.UseSqliteStorage(dbPath);
    cfg.AddScorer<MyScorer>();
    cfg.UseDefaultCandidates("claude-cli", "ollama");       // router fallback order
});
```
Options bind from config + env overrides (`LYNTAI_*`). Sensible defaults so the minimal setup is a
provider + storage.

**Storage feature toggles** — `UseSqliteStorage(path, StorageFeature.Score | …)` (and the Postgres twin)
select which storage domains to wire: a disabled feature registers no store AND lands no table (tag-driven
selective migration; default `All`). Lyntai still OWNS the tables it creates — this just avoids unused
`lyntai_*` tables for domains the app doesn't use.

### 5.6 Generation — a second domain in `Lyntai.Core` (added 2026-08-04, not in the v0.1 design)

*Added here because this document is read first and otherwise never introduces the largest post-1.0 domain —
it only forward-references it in §6. This is the routing entry, not a restatement: the reasoning is
`docs/DECISIONS.md` **D24** (generation is a platform in its own domain, coupled to the LLM side only through
tools), **D31** (a verdict for "never set up", in both domains) and **D36** (the translation between the two
verdict taxonomies). The plan of record is `docs/2026-08-04-generation-platform-plan.md`.*

```csharp
public interface IGenerationProvider : Lyntai.Lifecycle.IProviderIdentity {
    new string Id { get; }                            // "openai-images" | "a1111" | "local-diffusion" | …
    GenerationCapabilities Capabilities { get; }      // read by the router BEFORE spending anything
    Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default);   // no-cost; never generates
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default);
}
```

- **One capability-aware seam for image/video/audio/3d, with THREE delivery modes**, because real backends
  genuinely differ: inline (`IGenerationProvider`), async job (`IGenerationJobProvider` — submit → poll →
  fetch, universal for video), and streaming (`IGenerationStreamProvider`, TTS). A backend that cannot do
  something simply does not implement that interface and callers pattern-match over the registered
  collection — the same optional-capability shape Core already uses for `IProviderAuth` /
  `IProviderVersionInstaller`, rather than one fat interface whose methods throw.
- **`ProbeAsync` never generates.** The generate-and-discard check it replaces bills a real generation to
  answer a setup question. A backend that genuinely cannot be checked without generating reports
  `Available: false` with the reason.
- **A submit whose deadline expires is `GenerationOperation.Inconclusive`, and is never re-submitted** — the
  work may have been accepted, so a retry double-bills the host (`Inconclusive` shipped in 2.2.0). The operation id is checkpointed BEFORE the first poll, so a crash
  resumes the render already running instead of paying for a second one.
- **The CONTRACTS are in `Lyntai.Core`** (namespaces `Lyntai.Generation` + `.Routing`/`.Jobs`/`.Tools`); the
  BACKENDS are the separate `Lyntai.Generation` package (namespace `Lyntai.Generation.Providers`). **Both
  carry the FULL SemVer promise** — the package held an exemption from 2.0.1 and 3.0 withdrew it once each of
  its three named reasons closed (**D70**; **D67**, **D69**). The namespace-vs-package distinction outlives
  the exemption and is still worth knowing, because it is what the exemption was repeatedly mistaken for.
- **The router has a door per delivery mode, and the third one is 3.0** (**D67**). `IGenerationRouter` gained
  `StreamAsync` — a required member, so a hand-written router must add it. Before that the capability
  pre-filter was only ever asked about `Inline` and `Job`, so a backend advertising `GenerationDelivery.Stream`
  was **unreachable through the platform**: `IGenerationStreamProvider` was a `Lyntai.Core` contract about to
  freeze under the full SemVer promise having never been exercised. **Two of its invariants are INHERITED
  from the LLM router rather than invented** (§6, D4): fallback stops at the first chunk carrying real data,
  and only real data commits — a metadata-only opening chunk must not. They transfer because the failure
  they prevent is identical, splicing two responses into one stream, whether the bytes are tokens or audio.
  **One is this door's own: exactly one terminal chunk, guaranteed by the ROUTER**, so a backend whose stream
  simply stops gets it closed here and never has to be careful about closing its own. What stays inferred is
  narrower than a blanket caveat: not the chunk HANDLING, which is measured, but the chunk SHAPE — whether
  data-then-terminal is the decomposition a real TTS wire format wants.
- **The coupling to the LLM side is five `ITool`s** (`AddGenerationTools()`), and that is the *entire* coupling
  (D24) — the tool loop and an MCP-hosted CLI agent both drive media through the same five.

### 5.7.0 What the memory engine is FOR — the objective optimization work is allowed to move (added 2026-08-12)

*Written after five measurement studies in one day produced numbers nobody could act on, because the target
was never stated. Every study reported `MissRate` and `PollutionRate` with no recorded priority between them,
no constraint that must not regress, and no agreed instrument — so each result had to be argued from first
principles instead of checked against a goal. **This section is that goal.** It exists to make the next round
of work optimization rather than exploration; anything below it that a change improves is progress, anything
it forbids is a regression regardless of how good the headline number looks.*

**The behavioural goal, in one sentence.** A session should open on a small, cheap index that contains the
material this task actually needs — so an application pays for headlines rather than for its whole store, and
what it gets back is relevant.

**The objective, in priority order.** These are lexicographic, not weighted: a change that improves a lower
line while breaking a higher one is a regression.

1. **Never lose an authoritative fact.** A `MemoryGrade.Authoritative` entry admitted to a scope must be
   returned for a query it is relevant to, however long it has been quiet. This is the one guarantee with no
   acceptable failure rate, because such an entry is the application asserting something is true.
2. **Minimise `MissRate`** — relevant material that the recall failed to return. This is the primary number.
3. **Do not increase `PollutionRate`** while doing so. Not co-equal with (2): a change that trades a large
   miss reduction for a small pollution rise is accepted, one that trades the reverse is not. Recall returns
   headlines whose cost is bounded by `Limit`, so an irrelevant headline is cheap while a missing fact is not.
4. **Keep the first load cheap.** Headlines, not content; one bounded query; no background job; everything
   computed at read time. A change that improves (2) by returning more, or by scanning more, has not improved
   anything.

**AMENDED 2026-08-26 (`docs/DECISIONS.md` D90): four INVARIANTS sit above these, and line (1) is now one of
them.** The list above says what optimization MOVES; it said nothing about what a change may do to evidence,
to conflicting claims, or to a fact that was true last month — so a temporal or supersession measurement had
no target to be checked against. The additions are deliberately **not** four more lexicographic lines: that
ordering presumes lines you TRADE, and each of these is an absolute with no acceptable failure rate.

> **1.** An explicit deletion COMPLETES, reaching every projection and not only the store that owns the row.
> **2.** No authoritative fact is lost and no canonical evidence is silently lost — decay buries, only an
> explicit caller act deletes. **3.** Nothing is silently overwritten and no conflict is hidden.
> **4.** Current and historical facts resolve correctly by time, *for any feature offering a temporal answer
> at all*.

**Invariants 3 and 4 make no claim about the base engine**, which has no temporal or conflict concept — a
`MemoryWrite` carries no valid-time and nothing supersedes anything, so they are vacuous here by
construction and bind whatever is built on that data instead. Saying so is the point: an objective naming
guarantees the code does not provide misleads everyone who reads it. **Miss and pollution remain the only
two numbers this subsystem optimises**; the invariants are pass/fail conditions on a change, so no figure,
sweep or default on record changes meaning.

**Constraints an optimization may not spend.** Stated because each was a real temptation during the 3.0 work:

- **No sweeper, no background job.** Decay is computed when read.
- **A recall performs at most one embed and one bounded candidate query.** Enrichment is best-effort and its
  failure degrades quality, never correctness.
- **Nothing is deleted except by an explicit caller act.** Decay buries by rank; it never removes (**D41**).
  As of 3.0 there are two such verbs, and they are different capabilities (**D72**): `ForgetAsync`
  (`IForgettableMemory`) is a targeted withdrawal that must be COMPLETE, `PruneAsync` (`IPrunableMemory`) is
  best-effort capacity management. Through 2.5.x this line could name only `PruneAsync`, because that was
  the only one — and the engine's `ForgetAsync` was reachable through no interface at all (**D63**).
- **`Stability` keeps its unit** — the position delta at which retrievability is `0.5`, enforced by a contract
  fact. A change reinterpreting it silently reinterprets every stored row.
- **No mechanism may make a permanent change driven by this engine's own retrieval decisions.** The 3.0
  finding, and the one constraint that is a lesson rather than a principle: it banks the ranker's errors.
  Bounded or expiring effects are fine; salience (write-time content) and the age reset are the shipped
  examples (**D54**).
  <br>**The constraint is now expressible in the API, not only in prose** (**D57**): reinforcement's two
  effects are separated by `GraphMemoryOptions.Reinforcement` (`MemoryReinforcementEffects`), so "reset the
  age, do not grow the stability" — the permanent-change-free configuration this bullet describes — can be
  asked for policy-independently rather than only through one shipped curve's `ReinforceGain`. The seam is
  cut at the EFFECTS; an act-shaped question (recall versus expansion) is separate and composes with it.

**Explicit non-goals.** Optimizing toward any of these is out of scope, not merely deprioritised:

- **Matching FSRS's published numbers.** FSRS is a scheduling model for verified reviews; this is a
  query-driven store that observes no correctness signal. Fidelity to it is a means, and 3.0 measured it
  costing more than it bought.
- **Being a scheduler.** Nothing here decides *when* an application should revisit something.
- **Maximising durability as such.** Durability is instrumental. An engine that retains everything perfectly
  and returns the wrong ten headlines has failed every line above.

**How to read a `PollutionRate` at all — the structural floor (measured 2026-08-15).** `PollutionRate` is the
fraction of the `limit`-sized page occupied by ids outside the relevant set, and a real engine FILLS the page
whenever the store holds at least `limit` candidates. So whenever the relevant set is smaller than the limit,
the surplus slots are unavoidably non-relevant and the metric floors at `(limit − |relevant|) / limit` —
**for `critical-rare`'s 2-target ground truth against a limit of 10 that floor is `0.800`, and the shipped
engine measures `0.805`.** A published pollution number near its floor therefore reports the corpus shape,
not a failing policy; `RecallQuality`'s own remarks carry the same statement for the mirror case, where a
relevant set LARGER than the limit floors `MissRate` instead. **`MissRate` is the number that discriminates**
— which is also the priority order objective (2) and (3) already state.
<br>The only way under that floor is to return FEWER items, which is what `GraphMemoryOptions.VerificationFilters`
does, and it is why the verification seam is the one mechanism aimed at pollution rather than at miss.

**What a MODEL IN THE LOOP is worth — the largest lever measured, and the first measurement of it
(2026-08-15).** Every other recall-quality figure in this repository is **model-free**: `memory-sweep` wires
no annotator and no verifier, so its numbers are the lexical floor rather than what a consumer who
registered an LLM would see. `node devtools/dev.mjs memory-verification` measures the gap with a PERFECT
judge — the mechanism's **ceiling**, never a model's accuracy, the same stance `memory-annotation` takes.

| shape / class | off | judge REORDERS only |
|---|---|---|
| `high-noise` / `topical` | miss `0.690` | miss **`0.000`** |
| `high-noise` / combined | miss `0.281`, pollution `0.874` | miss **`0.000`**, pollution `0.674` |
| `critical-rare` / `critical-rare` | miss `0.275`, pollution `0.707` | miss **`0.000`**, pollution `0.473` |

**The reordering arm is the finding, and it filters nothing.** The relevant material was already inside the
candidate set — within `VerificationDepth` — and merely ranked below the cut, so **the bottleneck this
measures is RANKING, not retrieval.** Every ranking-policy decision on record moves recall by hundredths;
this moves it by an order of magnitude. The `filter` arm's `0.000/0.000` is close to tautological with a
perfect oracle ("only relevant items survive" is what that phrase means) and is reported as an upper bound
rather than a result.
<br>**Two limits that decide how to use it.** The judge's ACCURACY is a separate question, and it is already
measured: **`docs/memory.md` §5 carries the judge ladder** — six judges with miss, pollution and a
share-of-reference column — and is the authority. It is not restated here; a second table would drift from
it. Two of its findings bear directly on the ceiling above: the ground-truth arm is a REFERENCE and not an
upper bound (`gemma3:4b` beats it on both metrics), and **newer beats bigger**. And the judge COSTS a model
call per recall, which is why the seam ships OFF and is a lever a deployment opts into.
<br>**Do not put a REASONING model in this seam** — `docs/memory.md` records ~25s per judgement against
gemma3's ~1.5s, disqualifying whatever it scores when it sits in the latency path of every recall.
Re-confirmed 2026-08-15: qwen3:4b emits ~2,200 output tokens for a four-note question whose answer is about
8, and `LlmRequest.Reasoning` (which `LlmMemoryVerificationPolicy` already sets to `Suppress`) does not stop
Ollama's qwen3 reasoning anyway.

**The difficulty axis is INERT at shipped defaults (measured 2026-08-15).** `DsrRetrievability.Reinforce`'s
growth term is `ReinforceGain × exp(−DifficultyWeight × (difficulty − 1)) × …`, and `ReinforceGain` ships at
`0` (**D54**). Difficulty is the first factor multiplied by that zero, so it cannot move retrievability at
all unless a deployment turns the gain on. `memory-sweep`'s `{difficulty-live, difficulty-inert}` arms are
therefore structurally identical and came back equal to three decimals — **equal arms there are evidence the
run could not SEE the axis, never evidence the axis does nothing.** Difficulty remains LIVE in the sense
**D49** claims — maintained and persisted per review, which is what makes later fitting possible — and that
is the whole of the claim. Pinned by
`DsrRetrievabilityTests.Difficulty_changes_nothing_while_ReinforceGain_is_zero_and_something_once_it_is_not`,
which fails if the default ever moves and forces this paragraph to be revisited with it.

**The instrument, and what it cannot see.** All published numbers come from `MemoryCorpus` replayed against a
real SQLite store. It is an INSTRUMENT built to make policies disagree, not a simulation of usage, and three
blind spots are known and load-bearing — a null result on any of them means *untested*, never *fine*:

- **It is entirely ENGLISH and entirely space-separated**, while this library's FTS is `tokenize='trigram'`
  chosen precisely so non-Latin text works, and its storage suite carries explicit CJK coverage. Under
  trigram matching almost any two texts share trigrams, so **a cue contending with incidental unrelated
  matches is the NORMAL condition for CJK content, not a wording mistake** — there is no stopword to strip.
  Every recall-quality number in this repository is therefore measured under the most favourable
  tokenization the library supports. `CorpusShape.AttributeCue` exposes both conditions for one class, and
  the gap between them is large; nothing else in the corpus varies it at all.
  <br>**This blind spot was hiding a real defect, which is the argument for taking the list seriously.**
  Looking at it directly (2026-08-12) found that a CJK query was not merely measured under favourable
  conditions but *broken*: whitespace splitting handed back a whole sentence as one token, so a Chinese cue
  could only match an entry containing that exact substring. Fixed in **D55** — a spaceless run is expanded
  into character trigrams by default.
  <br>**CLOSED as of 2026-08-12: the corpus now has a LANGUAGE axis**, extended 2026-08-13 to **Chinese,
  Japanese and Korean**. `CorpusShape.Language` selects the `CorpusLexicon` every template and every reader
  comes from; English is the default and is byte-identical when unset, proved by **six** goldens (five
  captured before the axis existed; a sixth, added 2026-08-27 for the routine class, the same way — its own
  default leaves the corpus unchanged). `node devtools/dev.mjs memory-language` measures each arm against English on structurally
  identical timelines — same steps, same ids, same ground truth, only the text differing — so a gap is the
  language rather than the corpus. **The sentence above no longer describes the instrument, only the figures
  published before that date**, which remain English-measured and should be read that way.
  <br>The FOUR non-English arms probe different failure modes rather than repeating one: Chinese is a
  spaceless run of Han characters; Japanese is a spaceless run mixing kanji, hiragana and katakana, where
  kana's small inventory makes trigram collisions likelier; **Korean writes spaces** and is trigram-expanded
  anyway (Hangul sits in `SearchTerms`' spaceless range), which is defensible only because Korean is
  agglutinative — making it the arm where the expansion would first cost more than it recovers; and
  **`ChineseMixed`** embeds English terms in Chinese prose WITHOUT spaces (`部署pipeline`), putting the script
  boundary mid-run rather than at a token edge, which is where a Latin word inside a CJK run used to be
  shredded into fragments that are words in no language. What is still unmeasured is any spaceless script
  outside those ranges (Thai).
  <br>**CLOSED as of 2026-08-14 for mixed script**, which this bullet listed as unmeasured until the
  `ChineseMixed` arm landed and is the case that arm exists for.
- **No authoritative material BY DEFAULT** — `CorpusShape.AuthoritativeCount` is opt-in and `0` unless a
  caller asks, so the SWEEP's arms remain blind to objective (1) and an unchanged sweep number means "not
  exercised" rather than "no regression". **The promise itself is measured**, by
  `MemoryAuthoritativeSurvivalTests` across five languages plus a control (**D56**) — which is what found it
  broken. Partially closed, and the half that remains open is the instrument, not the promise.
- **Noise is TEMPLATED by DEFAULT**, so "novelty" cannot meet textually diverse junk. This was a blind spot
  until 2026-08-13; it is now an axis rather than a limit — `CorpusNoiseKind.Diverse` draws near-skeletonless
  junk from `CorpusLexicon.NoiseVocabulary`, which is the shape a novelty-driven salience policy actually has
  to face. A measurement that leaves the default is still exercising the templated case only.
- **Expansions exist only opt-in** (`CorpusShape.ExpandRatio`, default 0), so a measurement that does not set
  it is exercising recall-driven behaviour only.

**The unlock, named so it is not rediscovered.** Every remaining question in this subsystem — parameter
fitting, whether reinforcement can be made to pay, whether salience inverts — reduces to the same missing
input: **this library observes no signal for whether a recall was CORRECT.** No corpus supplies it. A
consumer-supplied rating, or an observation of material the application expected and did not get, is what
turns the rest of this list from argument into measurement (**D51** amended, **D54**).

### 5.7 Long-term memory — named engines over a decaying graph (added 2026-08-08, not in the v0.1 design)

*Added for the same reason as §5.6: this document is read first, and §5.4 otherwise leaves `IMemoryStore` — a
bounded, task-scoped fact store — looking like the whole of memory. It is not; it is one of four surfaces, and
the newest is a different kind of thing. This is the routing entry, not a restatement: the reasoning is
`docs/DECISIONS.md` **D39**–**D42**, and the specs are `local/superpowers/specs/2026-08-08-memory-engine-seam-design.md`
and `2026-08-08-graph-memory-engine-design.md`.*

```csharp
public interface IMemoryEngine {
    string Name { get; }                              // "chat" | "project" | "chat/lexical" | …
    MemoryGrades Supported { get; }                   // which grades this engine can actually hold
    Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default);
    Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default);
}
```

- **A DI collection keyed by `Name`, resolved through `IMemoryEngineFactory.Get(name)`** — the same
  variation-point shape as `ILlmProvider` keyed by `Id`, and the same shape a consumer already knows from
  `IHttpClientFactory` (`Get`, not `Create`: engines are singletons). One application runs several engines at
  once for different purposes, which is the requirement that rules out a single unnamed singleton (**D39**).
- **A blend IS an engine.** `CompositeMemoryEngine` implements the same interface as its members, so nothing
  branches on whether a caller holds one engine or five. Optional abilities are separate interfaces —
  `IExpandableMemory`, `ILinkableMemory`, `IForgettableMemory` and (3.0, **D72**) `IPrunableMemory` — never
  guessed by type-testing (the same optional-capability shape as §5.6, and the regression it prevents is
  pinned by a test).
  <br>**Two of them ROUTE and two FAN OUT, and the argument is the addressing, not taste** (**D63**, **D72**;
  3.0 — through 2.5.x every one of them routed). `ExpandAsync`/`LinkAsync` take a `MemoryRef`, which names
  exactly one member, so they route by `MemoryRef.Engine`. A removal takes a (task, scope), which every
  member may hold, so it visits each capable one and SUMS what they removed — a consent withdrawal that
  silently cleared one engine of five would be the broken promise the verb exists to prevent. Which members
  a removal visits is the `IMemoryRemovalPolicy` seam, asked per member AND per kind (`Forget` / `Prune`),
  because eligibility is a deployment question the library cannot answer.
  <br>**`Forget` and `Prune` are separate capabilities for the same reason** (**D72**): a forget must be
  COMPLETE, a prune is best-effort capacity management, and one interface forced an engine to claim both or
  neither — which a vector store cannot honestly do, since it can forget a (task, scope) exactly and cannot
  prune by age at all. A blend where NO member can remove throws rather than reporting `0`, because `0`
  already means "nothing matched".
  <br>**A WRITE does either, and the caller chooses** (**D85**, post-3.0). It routes to the first member that
  can hold the write's grade by default; `MemoryWriteRouting.EveryCapable` sends it to all of them, which is
  what makes a blend whose members index the same material differently — a graph member beside a semantic one
  — actually fill both. Opt-in, because N stores is N writes and because an `Inherit` write is stored by each
  member at its OWN role. A member no write can reach under the current routing, and a model-backed policy
  registered where no member consults one, are both reported when the engine is built: a registration that
  resolves and can never run is the failure this whole seam keeps producing.
- **Grades are what let this beat a human memory rather than imitate one.** `MemoryGrade.Authoritative`
  material is allocated from a reserved character budget *before* any associative content is admitted, is
  never truncated to a headline, and renders in its own labelled section. Associative recall must never crowd
  out a fact the application stated as true.
  <br>**The reserve protects exact material from the BUDGET, never from the caller's own retrieval**
  (**D83**): only what reached the composer can be reserved. That is why the rendering half is reachable
  without an engine — `MemoryComposition.Render(basePrompt, items, options)` — so a consumer with its own
  selection offers exact material IN FULL and lets the reserve do the bounding, instead of implementing an
  `IMemoryEngine` whose recall returns material it already chose.
  <br>**A curated engine may derive a grade PER ENTRY** (**D84**) — one catalog mixes the owner's typed facts
  with what an assistant inferred, and the store has no grade column to tell them apart. It is a read-path
  delegate over the entry's app-owned metadata, and it deliberately does not widen the engine's `Supported`:
  reading a grade the deployment encoded is not the same capability as writing one.
- **Decay is measured in interference, not elapsed time** (**D40**). An entry's age is how far its engine's
  monotone position has moved since the entry was last used — a subtraction, not a duration — so a
  rarely-used engine does not forget merely because time passed. What advances the position is an
  `IMemoryAgePolicy` seam with four shipped implementations, because "how much has happened" is genuinely
  ambiguous (writes, volume, or real time). Bursts saturate: advancing linearly would let one bulk ingest age
  every prior entry past its stability and erase the engine's history.
  **A store also tracks the three primitives writes/volume/real-time are each measured against
  UNCONDITIONALLY** — an ordinal, a cumulative character count and a timestamp, none in any one policy's own
  unit — so `IMemoryAgePolicy.Age` lets any of the four project its own view from the SAME stored numbers,
  and swapping the installed policy never reinterprets a row (2026-08-10 memory-policy-seams plan, Task 2).
  **Not every policy CAN project from those primitives, and the engine now honours the difference structurally
  rather than guessing (Task 3):** each `IMemoryAgePolicy` declares its own `MemoryAgeKind` —
  `Derivable` (`PerWriteAgePolicy`, `ContentSizeAgePolicy`, `ElapsedAgePolicy` — a pure function of the
  primitives, so replaying the same writes always reproduces the same age) or `Accumulating`
  (`BurstDampenedAgePolicy`, the engine's shipped DEFAULT — its damping divides by the burst size, which
  depends on the TIMING of every intervening write and cannot be recovered from two snapshots). A `Derivable`
  policy's retrievability-facing age is projected from the primitives; an `Accumulating` one's still comes from
  `GraphNode.Age`, the pre-existing `Advance`-driven subtraction, exactly as before — so the shipped default's
  numbers are unchanged, while every other shipped policy now genuinely derives.
  **Age (and salience) are PLURAL, the same shape `IMemoryRetentionPolicy` already had** — several coexisting
  age dimensions (writes vs characters vs elapsed) or salience dimensions (structural novelty vs semantic
  weight vs explicit marking) may be registered at once, each contributing its own view, combined by a named
  `IMemoryAgeCompositionPolicy` / `IMemorySalienceCompositionPolicy` the engine calls rather than hardcoding.
  `IMemoryRetentionPolicy`'s own combination rule — multiplying every retention policy's clamped factor — is
  similarly named now, `IMemoryRetentionCompositionPolicy`/`MultiplicativeRetentionCompositionPolicy`, and
  `ModulatedRetrievability` takes it as an optional constructor argument instead of hardcoding the multiply.
  Every shipped default composition reduces a one-element (or, for retention, empty) input to exactly the
  pre-existing behaviour, which is what keeps every engine default unchanged by any of this.
- **Decay buries; it does not cut** (**D41**). Recall ranks against a *relative* floor, so a faint memory is
  outranked by stronger hits rather than removed, and still returns via a direct reference or a neighbour's
  link. The absolute `MinRetrievability` governs `PruneAsync` alone, where deletion is the explicit intent.
  Seeding applies no faintness bound at all.
- **No decision is pushed to the consumer.** Every seam ships a registered default and every constant is
  overridable, so `AddMemory()` works with nothing implemented; `IMemoryGraphStore` has InMemory, SQLite
  (FTS5 trigram — a word-boundary tokenizer silently returns nothing for CJK substrings) and Postgres
  (`pg_trgm` GIN) backends held to one contract. No storage backend evaluates the decay curve: the policy
  supplies a conservative `CandidateCutoff` and the store applies it with plain division — in `PruneAsync`
  only, since seeding applies no faintness bound (above), so the cutoff governs DELETION, not admission.
- **Retention is an OPEN model: named signals on a decaying entry, layered by an `IMemoryRetentionPolicy`, and
  the first one is salience** (added 2026-08-09; **D45**, whose ranking default was corrected the same day
  it shipped and which records both readings). `MemoryDecayState`
  carries a `MemorySignals` bag; `ModulatedRetrievability` layers arbitrary retention dimensions over ANY
  `IMemoryRetrievabilityPolicy`, each clamped to its own declared maximum, with `CandidateCutoff` widened by the
  product of every registered maximum so the bound stays a conservative superset. What an under-wide cutoff
  costs is DELETION, not a short recall: its only consumer is `PruneAsync`. Adding a
  dimension is a class plus a registration — never an edit to `MemoryDecayState` or the curve itself.
  `IMemorySaliencePolicy` judges how strongly a write is encoded (a registered, model-free default ships).
  Salience means **"this memory does not fade away"** — decay resistance plus store ADMISSION priority — and
  deliberately does NOT mean "first priority" by default (**D45**, corrected on its own shipping day from an
  initial reading that conflated the two): `SalienceRetentionPolicy` lengthens a half-life (nothing more — its own contract is
  unchanged); the stores order SEED ADMISSION on it wherever nothing has already ranked the candidates by
  match quality — the no-query and substring-fallback paths on all three backends — so a salient candidate
  the recency-only limit would have cut still gets in, which together with the half-life is the whole of
  "does not fade away". On a MATCH-RANKED path (SQLite's FTS branch, taken for any query of three characters
  or more) the match score leads and salience is only a tiebreak behind it, so admission ordering is
  backend-specific in the same way `IMemoryGraphStore.SeedAsync` already makes reported `Relevance`
  backend-specific — whether that branch needs a real salience term is open. And the RANKING seam
  (below) can ALSO lift rank by it, `1 + weight × ln(salience)`
  (`Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight`), bounded logarithmically — the
  same shape and reason as the decay curve's own `DsrOptions.ConnectionBoost` — but that knob
  **defaults to 0 (off)**: reordering a candidate ahead of a better textual match is a stronger, separate
  claim than "does not fade away", and a consumer opts into it explicitly rather than getting it for free. A
  signal earns a promoted, indexed column exactly when the database itself must sort on it (D45's schema
  rule, which the ranking-default correction left untouched); rank needs no such column, since it is
  computed by the ranking policy over
  candidates the store already returned.
- **Where a tuning constant LIVES is settled by ownership; how the constants INTERACT is a separate question,
  and this is its answer** (closing the open half of archive Part 53). Placement follows the domain rule
  above — a domain owns its seam, its implementations *and* its options — which is why a consumer tuning
  ranking touches more than one record. That is correct and is not going to be consolidated; what was missing
  is a single statement of how the records compose, so here it is, in the order the values actually flow:
  1. **`SalienceOptions.MaxSalience` caps the input.** A salience policy may report no higher, so it bounds
     everything downstream — including how much rank movement any weight below can possibly buy.
  2. **`SalienceRetentionPolicy` turns that salience into stability**, which is what "does not fade away"
     means (D45). This is on by default and is the whole of salience's retention effect.
  3. **`MultiplicativeRankingOptions.SalienceRankWeight` turns the same salience into RANK**, as
     `1 + weight × ln(salience)` — a strictly stronger claim, and therefore **off by default**. Because the
     stores normalize `Relevance` by rank POSITION rather than score margin, the movement a given weight buys
     is candidate-count dependent; the property's own doc carries the general form and D45 the worked numbers.
     It is inert under `ReciprocalRankFusionPolicy`, which is the registered default and has its own
     `SalienceWeight` instead.
  4. **`DsrOptions.ConnectionBoost` is a different axis entirely** and is named here only because
     `SalienceRankWeight` copies its `1 + k × ln(x)` shape. It reads connection strength, never salience, and
     the resemblance is formal.
  **The one genuine trap is that `MaxSalience` is shared** between the policy that reports salience and
  everything that consumes it, so lowering it silently shrinks both the retention effect and any rank effect
  at once. Nothing else here is coupled across records: each of the other three is read by exactly one policy.
- **Ranking is its own domain, `IMemoryRankingPolicy` (`Lyntai.Memory.Ranking`) — added when the engine
  stopped hardcoding the formula against its own options (memory-ranking-seam plan).** `Rank(candidates,
  context)` takes the WHOLE candidate set at once, set-based rather than per-candidate, because a fusion
  policy cannot score one candidate without seeing where every other one falls on the same signal. The
  shipped `MultiplicativeRankingPolicy` was this domain's first formula given a name rather than changed —
  no longer the registered default as of 3.0 (owner ruling, 2026-08-11; see below) but unchanged and still
  shipped: `Relevance × Retrievability × boost × HopAttenuation^hop`, then a relative floor, with all three
  constants — `HopAttenuation`, `RelativeFloor`, `SalienceRankWeight` — on its own
  `MultiplicativeRankingOptions` rather than `GraphMemoryOptions`. **The policy owns the floor, never the
  grade exemption**: trust that
  `MemoryGrade.Authoritative` material is never buried must hold against a policy that DROPS one, including a
  third-party one that has never heard of grades, so `GraphMemoryEngine.RecallAsync` re-admits any dropped
  authoritative candidate itself, and **reserves slots for authoritative material within the caller's
  `Limit`** (`GraphMemoryOptions.AuthoritativeReserve`, unbounded by default).
  <br>**This paragraph said the opposite until 2026-08-13, and the correction is the point.** It read
  "still subject to the caller's `Limit`, so a re-admitted entry CAN be cut by a small one; making the
  exemption survive the limit too would let one authoritative entry evict every ordinary hit". The first
  end-to-end measurement of objective (1) below (`MemoryAuthoritativeSurvivalTests`) found **every**
  authoritative fact lost in **all five** corpus languages, from a probe that singled out none of them — so
  the stated objective and the implementation disagreed, and the implementation was the half that had never
  been measured. The eviction objection is answered rather than dismissed: an exact fact CAN now displace
  ordinary material, because that is what marking it authoritative means, and `AuthoritativeReserve` bounds
  how much. The promise degrades to "an exact fact is displaced only by ANOTHER exact fact", never to
  nothing. **Precisely scoped, not "whatever policy"**: re-admission is keyed on `Node.Id` alone, so this is a
  guarantee against a policy that drops a candidate, never against one that substitutes a fabricated entry
  under the same id — that would already violate `IMemoryRankingPolicy`'s own "never invent", but nothing
  downstream independently checks it. **The re-admission order is not merely cosmetic**: `GraphMemoryEngine.ReinforceAsync` writes symmetric
  co-activation edges pairwise across `nodes.Take(CoActivationCap)`, so which re-admitted entries land inside
  that window — and so which edges get PERMANENTLY written to the store — depends on this ordering too.
  **Ships two implementations, `MultiplicativeRankingPolicy` and `ReciprocalRankFusionPolicy` (THE DEFAULT
  as of 3.0)** — `Score = Σₛ wₛ / (K + rankₛ)`, summed over each candidate's 1-based RANK
  POSITION (never its raw value) on relevance, retrievability, salience and hop, so it needs no shared scale
  across signals the way a product does. Fusing by rank rather than value deliberately COMPRESSES the score
  range (forty candidates fused at the default `K = 60` span a `100/61 ≈ 1.639×` ratio top to bottom), which is
  why `ReciprocalRankFusionOptions.RelativeFloor` defaults to `0` rather than
  `MultiplicativeRankingOptions`'s `0.02` — that floor would never cross a single score at this range and
  burial would go silently inert (verified, not assumed: a direct instrumentation check found `0.02` cutting
  zero candidates anywhere on the measurement corpus below). Hop is the one deliberate deviation from the
  three-signal list above: taken literally, ignoring it would let a hop-2 match outrank a direct hit, so it is
  fused as a fourth signal, ranked ASCENDING (nearer is better) where the other three rank descending.
  **Became the registered default 2026-08-11 (owner ruling)** — this library's own measurement
  (`local/superpowers/records/2026-08-09-memory-policy-measurement.md`, fsrs-properly plan Task 4) found it beating
  `MultiplicativeRankingPolicy` on the corpus's `topical` class in all six measured shapes, across two
  independent runs; `MultiplicativeRankingPolicy` stays shipped, unchanged, and registerable in one line.
  (The salience-measurement work is unrelated to which ranking policy is the default, and is tracked
  separately — `TASKS.md` Part 65, and `docs/task-archive.md` Part 69, which closed 2026-08-15.)
- **Ranking is scoped per named engine, and a single call can override it BY NAME (memory-policy-seams
  plan, Task 6).** `MemoryEngineBuilder.UseGraph` takes an explicit `ranking` argument — that named engine's
  own choice, ahead of whatever `IMemoryRankingPolicy` the container has registered; omitted, the container
  registration is still the default, so a consumer who never touches the parameter sees no change. A second
  argument, `namedRankingPolicies`, gives that engine a small catalog of alternates a single
  `MemoryQuery.RankingPolicyName` can select for one call — resolved BY NAME, the same reason
  `IMemoryEngineFactory.Get(name)` resolves engines by name rather than by an instance living in a record
  that is otherwise plain data (serialized, logged, traced). **An unknown name is an error
  (`KeyNotFoundException`), never a silent fallback** — reverting quietly to the default is exactly the kind
  of bug that surfaces months later as "ranking seems off", not at the call that made the mistake.
- **`CompositeRankingPolicy` fuses two OTHER `IMemoryRankingPolicy` members into one order — by rank
  POSITION, never raw score, the only sound way to combine them.** `IMemoryRankingPolicy.Rank`'s own contract
  already says a score means nothing outside the policy that produced it: `MultiplicativeRankingPolicy`'s is
  a bounded product roughly in `[0,1]`, `ReciprocalRankFusionPolicy`'s sums to around `0.06` at its own
  shipped defaults. This class re-derives each member's own COMPETITION rank position over the candidate set
  (grouping by that member's own tied SCORES, never its output list position, which a member's internal id
  tiebreak always makes distinct even when every candidate scored identically) and fuses those the same
  `score = w / (K + rank)` shape `ReciprocalRankFusionPolicy` already uses for its own four raw signals. A
  candidate either member's own floor drops is not excluded — it is ranked one past that member's own worst
  kept rank, tied with anything else that member also dropped, never fabricated as better or worse than
  that; a member that drops every candidate contributes the SAME constant to everyone rather than distorting
  the order — the same reasoning that makes a uniformly tied real signal inside
  `ReciprocalRankFusionPolicy` itself contribute nothing to ordering.
- **Every varying rule here is a named DOMAIN with one policy seam, and implementations accumulate rather
  than replace it — usually.** `IMemoryAgePolicy` (`Lyntai.Memory.Interference`), `IMemoryRetrievabilityPolicy`
  (`Lyntai.Memory.Forgetting`), `IMemoryRetentionPolicy` (`Lyntai.Memory.Modulation`), `IMemorySaliencePolicy`
  (`Lyntai.Memory.Salience`), `IMemoryRankingPolicy` (`Lyntai.Memory.Ranking`), `IMemoryAnnotationPolicy`
  (`Lyntai.Memory.Annotation`) and `IMemoryVerificationPolicy` (`Lyntai.Memory.Verification`) are the seven
  domains so far, each its own sub-namespace holding the seam plus every shipped implementation. `.Ranking` is the
  live proof — `MultiplicativeRankingPolicy` and `ReciprocalRankFusionPolicy` (the default as of 3.0, above)
  genuinely coexist. `.Forgetting` is the counter-example, on purpose: it shipped two curves through 2.5.x
  — `HalfLifeRetrievability`, the exponential curve, alongside `DsrRetrievability` — and 3.0 DELETES the
  first outright rather than keeping it (`docs/DECISIONS.md`: the exponential curve's own central
  reinforcement constant was never more than "reasoned, not measured", and later measurement found it
  compounds to 2.1× a correctly-behaving curve's stability over a four-touch batch). `DsrRetrievability`
  (a power law with a heavier tail plus FSRS's three stability-increase laws in
  `Reinforce`) is therefore the ONLY implementation this domain ships — **the registered default as of 3.0**,
  `docs/DECISIONS.md` D49, on FSRS's own external validation rather than this library's corpus. Accumulation
  is the norm the other six domains demonstrate, not a law this one was ever exempt from breaking when a
  measured defect said to. A domain earns a seam when a plausible alternative algorithm
  genuinely exists, not merely because it is a rule.
  <br>**`.Annotation` and `.Verification` are the two that read like exceptions and are not.** Both are
  MODEL-IN-THE-LOOP seams — the annotator records what a fact is ABOUT, which links entries on write and
  seeds recall from a subject the query names (D88); the verifier judges a recall's candidates — and both are
  **singular** rather than plural (D48) and default to **none**, so the
  whole library runs model-free unless a consumer registers one. That default is why this list said "five"
  until 2026-08-15 while the tree held seven: a domain nothing constructs by default is invisible to
  everything except the namespace map. They are domains by the same test as the other five — one seam, its
  own sub-namespace, implementations that would accumulate — and `docs/memory.md` is where a consumer is
  told what registering one costs and buys. **What lives where is settled by OWNERSHIP, not by
  consumption:** a
  domain owns its seam, its implementations **and its options**, and they stay with it even when a sibling
  domain depends on them — `SalienceOptions` is the salience domain's constants though `SalienceRetentionPolicy`
  (`.Modulation`) is built from them, and `IMemoryRetrievabilityPolicy` is forgetting's seam though
  `ModulatedRetrievability` (`.Modulation`) implements and wraps it. A type that **no single domain owns** —
  shared data every domain reads, like `MemoryDecayState` — stays in the root `Lyntai.Memory` namespace
  (`MemorySignals`, the engine, storage and vector/semantic surfaces do too). Which implementation is the
  DEFAULT is a versioned decision, changed only on measured evidence, never assumed from the seam existing.
- **Provenance tags which policy computed an entry's persisted state, answering "is this entry fit for the
  current policy set" instead of guessing (2026-08-10 memory-policy-seams plan, Task 4).** `MemoryProvenance`
  (root `Lyntai.Memory`) is the one type that owns a provenance column's bit layout — `Pack` (OR several
  policies' contributions into one, masked so bit 63 can never appear: SQLite `INTEGER`/Postgres `BIGINT`
  are both signed 64-bit, so a top-bit value would round-trip negative), `Unpack` (the same mask, applied
  defensively on read) and `Fits(stored, required)` — no inline bit math appears anywhere else, the same
  discipline that keeps `MemorySignals.Salience` a single coercion rather than four independent guesses.
  **Only the two domains that WRITE persisted state a later policy might need and not find get a column**:
  `IMemoryRetrievabilityPolicy` (`GraphNode.ProvenanceRetrievability`, its own `MemoryRetrievabilityProvenance`
  flags — `DsrRetrievability` declares `Dsr`; `HalfLife` is RETIRED, not reused, the one bit surviving the
  policy that declared it, deleted in 3.0 — every row a 2.5.x deployment wrote still carries it, and handing
  it to a future policy would misattribute that row's state) and
  `IMemorySaliencePolicy` (`GraphNode.ProvenanceSalience`, `MemorySalienceProvenance` — `StructuralSaliencePolicy`
  declares `Structural`). Age's three primitives are written UNCONDITIONALLY regardless of which policy is
  installed, so every age policy can always derive its own view — there is nothing to be unfit for; retention
  is read-only and persists nothing; ranking reads no persisted state either — neither of those three gets a
  column, and the absence is a design conclusion, not an oversight. **Provenance records who PRODUCED a
  signal, not who merely ran**: a salience policy that declined (too few comparables) or failed contributes
  nothing to `ProvenanceSalience` — crediting it would claim a judgement that never happened.
  `ModulatedRetrievability` forwards its wrapped policy's own provenance unchanged, because modulation is a
  READ-TIME view: whichever policy actually computed the stored stability is the one provenance must credit,
  never the decorator. Bits 0-31 are reserved for this library; bits 32-62 are open for a consumer's own
  policy; bit 63 is never set. Uniqueness and single-bit-ness are validated at construction, against
  whatever is actually REGISTERED (`MemoryProvenance.ValidateProvenanceBits`,
  `GraphMemoryEngine`'s salience-policy normalization) — a hand-listed test array cannot see a
  third policy that lands on an already-occupied bit; construction-time validation can.
- **`Stability` means exactly one thing across every `IMemoryRetrievabilityPolicy`: the position delta at
  which retrievability is 0.5 — enforced by one contract fact rather than by a convention marker
  (2026-08-10 memory-policy-seams plan, Task 5).** `DsrRetrievability` anchors FSRS's own 90%-retention
  framing back onto this by deriving its curve factor from it (`F = 0.5^(1/decay) - 1`), precisely so the
  fact holds without either curve ever reinterpreting a stored value. This one enforced fact is what let an
  entire first-draft design be deleted: a curve preferring a second convention can never ship without
  failing it on the way in, so no stored `Stability` is ever ambiguous and nothing needs converting between
  two conventions. **This anchor is a claim about the CURVE's own unit, not about a decorator that reads
  other state too** — `ModulatedRetrievability` satisfies it only for a state whose retention policies all
  report their NEUTRAL factor (the model-free default, no signals appraised); a non-neutral policy moving where
  retrievability crosses 0.5 is modulation working as intended, not a violation of the anchor.
  **`IMemoryRetrievabilityPolicy.Reinforce` returns the FULL `MemoryDecayState`, not a scalar** — a scalar
  can only ever persist `Stability`, and `DsrRetrievability` is the first policy to use the wider seam
  (2026-08-10, fsrs-properly plan Task 2, corrected against primary sources in fix round 1): it now
  maintains `MemoryDecayState.Difficulty` too, updating it on every review from a GRADE this library DERIVES
  from retrievability at recall rather than receives from a human (nobody grades a graph-memory recall).
  **The derived grade is restricted to FSRS's SUCCESS sub-range (Hard through Easy) and never emits `Again`,
  a LAPSE** — every graded event here is a success by construction, since an entry that is not returned
  never reaches `Reinforce`: low retrievability derives toward Hard, high toward Easy, and the no-change
  point lands at `r = 0.5`, this curve's own half-life anchor. The grade is computed from the state BEFORE
  that reinforcement, never from a value the same call produces, which is the cheap half of the drift guard
  a self-graded curve needs (the curve's own prediction of retrievability, not a human's judgement, so a
  curve that systematically overestimates could otherwise reinforce its own overestimate forever). **The
  update law includes MEAN REVERSION, FSRS-5/6's own restoring force** — without it, linear damping alone
  makes `Difficulty = 10` an ABSORBING state, since damping's own factor is identically zero there. A policy
  MUST return every field it does not own exactly as given — pinned by a contract fact run against every
  shipped curve — so a caller may persist the whole returned state without special-casing which fields a
  particular policy claimed; `GraphMemoryEngine.ReinforceAsync` extracts `.Stability` and `.Difficulty` today
  because the shipped curve sets nothing else, not because the seam only carries two fields.
- **A store now has TWO writers of one node's difficulty, and the precedence between them is explicit, not
  guessed** (2026-08-10, fsrs-properly plan Task 2; the precedence trigger corrected in fix round 1, C1): a
  write that NAMES a `MemorySignals.WellKnown.Difficulty` signal — a fresh node, or a re-remember whose bag
  carries that specific key — overwrites the promoted `difficulty` column, so an application's stated
  judgement always wins. **This is deliberately NOT `salience`'s own "bag is merely non-empty" trigger**: an
  earlier draft keyed it that way, so a write that appraised salience alone silently reset whatever
  `Reinforce` had tracked — difficulty has a second writer salience does not, so its own precedence must key
  on the specific signal, not on bag emptiness. Between writes, only `Reinforce` (via `TouchAsync`) moves it
  further, and never re-reads the bag, so the policy's own tracking survives an unrelated re-remember whose
  bag either is empty or names something else entirely. A row that predates this column (2.5.x, or any 3.0
  row from before this domain existed) reads the neutral
  default and needs no migration of its own data — `MemoryRetrievabilityProvenance` (already the record of
  which policy computed a row's `Stability`) is what tells "never computed" apart from "computed as
  neutral", exactly as it already did for `Stability` alone.
- **An EDGE carries the same three primitives a node does, so `StrengthAge` is swap-safe too and the
  derivable prune path is EXACT rather than conservative (3.0 pre-freeze).** Through 2.5.x only `Age`
  re-derived from swap-safe primitives; `StrengthAge` stayed `position - strengthened_position` against the
  store's single raw accumulator, in whatever unit was in force when an edge was last strengthened —
  `last_recalled_position` had been retired as an age source, `strengthened_position` had not. That put two
  units inside one retrievability expression: after a policy swap a connected entry's `StrengthAge` could be
  stale by orders of magnitude, collapsing `DsrRetrievability`'s connection boost to `1×` regardless of how
  recently the edge actually strengthened in the new unit. Because deleting a retrievable entry is
  unrecoverable while retaining a prunable one is not, `GraphMemoryEngine.PruneAsync`'s derivable branch then
  refused to delete ANY entry with `Strength > 0 && StrengthAge > 0` on the retrievability floor — safe, but
  it also left a genuinely unretrievable connected entry unremovable forever.
  `lyntai_memory_edge` now stamps `strengthened_ordinal`/`strengthened_chars`/`strengthened_at` beside
  `strengthened_position`, a store reports them as `GraphNode.StrengthOrdinalAge`/`StrengthVolumeAge`/
  `StrengthElapsedAge`, and `GraphNode.StrengthAgeSample` hands them to a policy as the SAME
  `MemoryAgeSample` the encoding side uses — an age policy's projection is a pure function of the three
  primitives and neither can nor need tell which event they were measured from. `GraphMemoryEngine`
  projects that axis exactly as it projects `Age` (`Derivable` → project, `Accumulating` → read the
  accumulator), so the shipped default composes `GraphNode.StrengthAge` unchanged and the guard is gone.
  **The one place this cannot be exact is the backfill**: an existing edge's true strength age was never
  persisted, so every pre-existing edge is treated as strengthened at migration time — the direction that can
  only ever RETAIN, and self-correcting on the edge's first real strengthening.
- **All THREE age axes are projected through the policies, `GraphNeighbour.EdgeAge` included.** It was the
  last one still read as the raw accumulator, and it is the mildest of the three: it feeds
  `GraphMemoryOptions.EdgeHalfLife` — a constant in the same unit — so nothing mixed two units inside one
  expression, and it only ever ORDERS a traversal, never deletes. That makes it a coherence gap rather than a
  data-loss bug, and it was briefly recorded as a deliberate exemption on those grounds. **That reasoning was
  wrong on the point that mattered**: adding a member to the `GraphNeighbour` record is binary-breaking, so it
  was never "additive, therefore free later" — it was free *now* and a whole major afterwards, exactly like
  the other two. It also needed no new schema, the edge primitives already being there. Taken inside the
  window. `EdgeHalfLife` is therefore denominated in whatever the installed policies count, which for the
  shipped `Accumulating` default is the position accumulator it always was.

## 6. Data flow & error handling (the parts that matter)

**Fallback router** (odysseus semantics). *All of this is the **default** `RoutingPolicy`
(`LyntaiOptions.Routing`); each rule below is a per-verdict `FallbackAction`, a same-candidate retry
count, a cooldown-key scope, and a sole-candidate exemption — overridable via `ConfigureRouting` /
`LYNTAI_*` env without changing the documented defaults.*
- Dedup candidates by `(providerId, model)`, first wins — a misconfigured list that re-prepends the
  primary won't retry it. A **sole** candidate is never benched for cooldown (benching the only option
  just yields a synthetic failure).
- **Retry-then-advance** (default 0 retries): the same candidate may be retried on a transient fault
  (Failed/Timeout) before advancing — a single blip shouldn't fail over. Cooled/refused/context-window
  verdicts never retry the same host.
- **Non-streaming:** try candidates in order; on `Failed`/`Timeout` move to the next; log each attempt
  with provider + reason. `RateLimited` = **cool the host immediately and advance** — a 429 is
  terminal for *that host's window* (immediate dead-host cooldown, never re-ask it) but transient for
  the fleet: a different candidate has a different quota. *(Amended 2026-07-17 from "circuit-break,
  hard stop": production routers treat 429 as fallback-eligible — LiteLLM shipped and fixed the
  hard-stop variant as a bug, issue #22296 / PR #22375 — and circuit-break-only fails the whole
  request even when a healthy fallback exists.)* `Refused` = surface, don't fall back (content policy
  follows the prompt, not the host).
- **Streaming:** once the first content token is emitted, **no fallback** — pass errors through
  unchanged (never duplicate output). Only pre-content failures move to the next candidate.
- **Dead-host cooldown** (not exponential backoff): after N consecutive connection failures a
  provider/host is marked dead for a short cooldown; any success resets. One log line per state change.

**CLI hygiene** (Gatherlight/Sonora): `UseShellExecute=false`, `ArgumentList` only (never a shell —
prompts carry newlines + metacharacters), prompt over **stdin**, **BOM-less UTF-8** both directions,
resolved-path cache (`where.exe`/`which`, prefer `.cmd`/`.exe`), `Kill(entireProcessTree:true)` on
cancel, per-call timeout. Cheap utility calls run from a **neutral cwd** (no project config loaded).
*(2026-08-04: these live once in `CliProviderEngine` (`Lyntai.Llm.Cli`); a new CLI backend is an
`ICliProviderDialect`, never a second copy — D21/D22.)*

**Structured output:** schema-constrained call, tolerant JSON extraction from prose/code-fences, one
retry on parse failure, else `Failed` verdict.

> **Amendment (2026-08-04, verdict-translation half 2026-08-05): the generation router (§5.6) has its own
> policy, and it is deliberately NOT identical to the rules above.** `GenerationRoutingPolicy` maps the media
> verdicts onto the same four
> actions — `Refused` → **Surface**; `NotConfigured` and `Unsupported` → **Advance** (no blame);
> `RateLimited` and `AuthFailed` → **CooldownAndAdvance**; `Timeout` and `Failed` →
> **PenalizeAndAdvance**. Two divergences, both deliberate and both easy to "fix" back into a bug:
> - **`Unsupported` ADVANCES here and SURFACES on the LLM side.** A second chat candidate shares the same
>   capability gap, so surfacing is the useful answer; media backends differ widely in what they accept, so
>   advancing is. `GenerationVerdictClassifier` carries the reason.
> - **An UNMAPPED verdict advances here and is PENALIZED there** (`GenerationRoutingPolicy.ActionFor` returns
>   `Advance`; `RoutingPolicy.ActionFor` returns `PenalizeAndAdvance`). A verdict a policy has never heard of
>   should not silently end a run another candidate could serve — but on the LLM side an unclassified fault is
>   more likely to be a real one. The divergence is flagged in the source and, until now, nowhere else.
>
> **A verdict that crosses the boundary keeps its MEANING and changes its ACTION** (**D36**).
> `GenerationVerdictClassifier.Translate` therefore gets one arm per `LlmVerdict` member and no catch-all: a
> discard over a taxonomy expected to GROW converts every future addition into a silent misclassification, and
> that is exactly how `Unsupported` shipped a release reported as `Failed` — benching healthy backends on
> capability gaps. The growth gate is a TEST, not the compiler (C# has no exhaustive switch over an enum, and
> CS8509 is only a warning). `ContextWindowExceeded` is the one member with no media counterpart and collapses
> to `Failed` **deliberately — a TRADE, not a free choice**: `GenerationRouter` never reports a blameless
> verdict over a real failure, so as `Unsupported` it would advance silently and the caller would be told "no
> capable backend" instead of the one actionable answer. That router rule LANDED —
> `docs/task-archive.md` **Part 40** — so the arm is settled: the reporting slot went in first and the
> verdict mapping followed it, which was the load-bearing order.

## 7. Storage conventions (from the family)

- **Dapper** + hand-written SQL, `snake_case` columns ↔ PascalCase (`MatchNamesWithUnderscores`).
  SQLite integer-affinity trap: wrap 0..1 / double columns in `CAST(x AS REAL)` in SELECTs.
- **`IDbConnectionFactory`** opens pooled connections with `PRAGMA journal_mode=WAL; busy_timeout;
  foreign_keys=ON`.
- **FluentMigrator**, numbered `yyyyMMddHHmm` (**amended 2026-08-08** — was `YYYYMMDDNNNN`; <!-- drift-ok --> the nine
  baseline migrations keep their original numbers, which sort below the new form), never reuse a number
  and never renumber one that has shipped. Composite PKs inline at CreateTable (SQLite has no ALTER ADD
  CONSTRAINT).
- **FTS5 `trigram`** external-content virtual tables kept in sync by AFTER INSERT/DELETE/UPDATE
  triggers, backfilled in the same migration; fall back to LIKE, rank with `bm25()`.
  <br>**The ONE tokenization is `Lyntai.Storage.SearchTerms`** (**D55**, 2026-08-12) — words for a
  space-separated script, character trigrams for one written without spaces (Chinese, Japanese, Korean), so
  every backend admits the same entries whatever the language. `FtsQuery` keeps only the FTS5 SYNTAX on top
  of it (quoting, and confining the expression to a named column where the table indexes more than one).
  A backend that splits a query its own way is the divergence D55 exists to prevent.
- BOM-less UTF-8 sources + `<CodePage>65001</CodePage>` so csc on a CJK-locale machine doesn't mojibake
  string literals.

*(2026-08-15: re-checked clause by clause. The FTS clause was restated — it named `FtsQuery` as the owner of
the tokenization, which D55 moved to `SearchTerms` three days after this attestation last said "still
current"; the rest holds. The maintained deep dives cite this section
rather than competing with it — `.claude/knowledge/storage.md` is this repo's binding (the `lyntai_` prefix,
the SQLite/Postgres parallels of the same number, `StorageFeature` tags) and canonical
`.claude/knowledge/sql-storage.md` states the traps themselves.)*

## 8. Testing

- **xUnit.** Unit tests: router/fallback/verdict/dedup/cooldown logic, prompt render + placeholder
  guard, `FtsQuery`, scoring aggregation — all pure, no I/O.
- **Integration tests:** SQLite domains against a temp db (created + migrated per test class);
  providers against the **provider-stub** (deterministic, no real tokens, driven by prompt markers —
  the generalized `claude-stub`).
- **`Lyntai.Playground`** console app = live smoke over the full stack (real provider optional, opt-in).
- **devtools e2e harness** boots the Playground (or a tiny sample host) against an isolated fixture
  with `LYNTAI_PROVIDER_CMD` = the stub, asserts end-to-end.

*(2026-08-05: re-checked and still current. The live mechanics — the `^p\d+\.mjs$` e2e suite-discovery
filter, the leading underscore that keeps `_e2e-common.mjs` from being discovered as a suite, and the stub
path — are `.claude/rules/repo-mechanics.md` §Dev loop.)*

## 9. Explicitly out of scope (deferred to a later "platform kit" cut)

Two-gate chat orchestration · scope-guard/jail hooks · tool/MCP registry · durable jobs (lanes +
checkpoint/resume) · security/access-gate + secret vault · server/host/launcher + auto-update ·
vision/multimodal · `Lyntai.Providers.Local` (LLamaSharp). The domain interfaces are shaped to admit
these later without breaking changes.

> **Amendment (2026-07-18): the platform kit is now SHIPPED** (v0.8–v0.15), exactly as §9 promised —
> additively, no breaking changes to the substrate. `Lyntai.Providers.Local` (v0.8); the tool/MCP
> registry as the agentic tool loop + native tool-calling + an MCP-client tool source + CLI tool-hosting
> (v0.9–v0.13, `Lyntai.Agents` / `Lyntai.Tools.Mcp` / `Lyntai.Tools.Mcp.Hosting`); durable jobs
> (v0.14, `Lyntai.Jobs` + `IJobStore`); then guards (`Lyntai.Guards`), two-gate `IChatOrchestrator`,
> the secret vault (`Lyntai.Secrets`), and vision/multimodal (`LlmMessage.Attachments`) in v0.15. See
> `CHANGELOG.md` / `ROADMAP.md`. The **only** §9 item still deliberately out of scope is the
> **server/host/launcher + auto-update** — that's an application concern, not a library's (Lyntai stays
> host-free; the one scoped exception is the ephemeral, opt-in localhost MCP listener the
> `Lyntai.Tools.Mcp.Hosting` add-on runs during a CLI call).

> **Amendment (2026-07-26): reconciled against v0.30.0** *(and extended in place since — the newest clauses
> in this block are dated 2026-08-05; counts stated inside it are as-of their own date)*. The header's "pre-implementation" status is
> historical; shape is snapshot-tested (D8) and this doc governs semantics. Since the 2026-07-18
> amendment, v0.16–v0.30 added — each per D11's "framework in Lyntai, domain in the app", detail in
> `CHANGELOG.md`/`ROADMAP.md`, rationale in `docs/DECISIONS.md` D5–D14:
> **`ILlmClient` front door + `AsChatClient()`** (inject the front door, not `ILlmRouter` — D5) ·
> **OTel telemetry** (`LyntaiDiagnostics`: `Lyntai.Llm` + `Lyntai.Agents` sources/meters, `RunTrace.TraceId`) ·
> **native tool-calling contract** (`LlmToolCall`, `LlmReply.ToolCalls`, tool/assistant turns,
> `SupportsToolCalls` on provider/router/client) · **governance decorators** (response cache / usage
> budget / rate limit behind `IResponseCache`/`IUsageTracker`/`IRateLimiter`; deterministic fold, cache
> outermost; SQLite/PG persistence) · **semantic memory** (BYO `IEmbedder`, `ISemanticMemory`,
> `IVectorStore` incl. pgvector; hybrid composer + dual-write) · **durable-jobs expansion** (priorities,
> DLQ, interval+cron schedules, cooperative cancellation, admission control, `Paused`, live progress,
> actor/mailbox `PartitionKey`; the last deferral, cross-process global limits, shipped in 3.0 as a slot
> table — see the 3.0 amendment below) · **secrets expansion**
> (DEK-envelope vault + recovery key; `Lyntai.Secrets.Dpapi`) · **refusal screening**
> (`LlmRequest.RefusalPattern` + `IRefusalMatcher`) · **curated memory** (`ICuratedMemoryStore`) ·
> **conversation event store v2** (GUID id + per-thread seq + kind/payload/metadata,
> `IConversationEnricher`, keyset paging — D10; dates the in-body §5.4 edit) · **`StorageFeature`
> toggles** (tag-driven selective registration + migration — D12; dates the in-body §5.5 edit) ·
> **memory eviction policy** (`MemoryEvictionPolicy` FIFO/LRU/TTL/size + opt-in prune cron — D13) ·
> **agent session + streaming loop** (`IAgentSession`/`AgentStreamEvent`, `IToolLoop.StreamAsync`,
> `ToolLoopResult.Usage`) · **BYO resources** (v0.7: `IProcessRunner`, BYO `HttpClient`, BYO
> `IDbConnectionFactory` + `migrate:false`, provider presets).
> **§5 additive shape drift** (current shape = the baselines): `LlmVerdict` +`ContextWindowExceeded`/
> `AuthFailed`/`Unsupported`/`NotConfigured`; `LlmRequest` +`TimeoutSeconds`/`RefusalPattern`; `LlmReply` +`ToolCalls`;
> `LlmMessage` tool turns + `Attachments`; `IPromptRegistry.ValidateOverride`; `IScoringService`
> read/aggregate/export members; new storage domains `IJobStore`/`IPromptVersionStore`/`ICuratedMemoryStore`;
> three storage backends, 11 packages **as of v0.30** (adapter→Core-only rule unchanged and verified; twelve
> today — see the §3 amendment).
> **§6 semantic additions:** AuthFailed = cool + advance; ContextWindowExceeded = advance, no penalty;
> Unsupported = surface (D3); NotConfigured = advance, no penalty (2026-08-05 — a backend that was never set
> up is skipped blamelessly, matching the generation router; the same rule in both domains) · streaming
> timeouts are INACTIVITY clocks + the empty-content commit gate
> (D4) · front-door decorators fold deterministically, cache outermost (D11) · `RefusalPattern` screening
> re-screens even cached hits · usage-tracker totals are async by contract and case-insensitive per
> consumer identity (v0.30).
> **Provider LIFETIME is a seam this design did not have** (2026-08-05, `Lyntai.Lifecycle`, D30). §4 assumed
> configuration is owned by the DEPLOYMENT, so a provider could be registered once at `AddLyntai` time. Where
> it is owned EXTERNALLY — an end user, or a store the process polls — several configurations of one backend
> are live at once and the set changes while the process runs. `IProviderPool<TProvider>` owns those
> instances (`Bounded` reuses, `Transient` never does; pooling is a registered strategy, not a behaviour),
> `ProviderKey` identifies a configuration, and **dead-host cooldown and concurrency admission key on that
> key rather than on the provider id** — otherwise one tenant's rate limit benches another's. Retiring an
> entry never disposes it: without leases a pool cannot know when the last caller finished, and a render
> outlives the configuration that started it. The routers take both as OPTIONAL parameters, so a
> deployment-configured app is unaffected.
> **§7:** pre-release migration changes fold into the owning unreleased migration; released migrations
> are frozen (D9); selective migration is FluentMigrator-tag-driven per `StorageFeature` (D12).
> **v0.30 pre-1.0 breaks:** `ChatResult.BlockReason`→`Detail`; `IRateLimiter` cancellation propagates;
> `IUsageTracker` fully async; tracker totals aggregate across consumer casings.

> **Amendment (2026-08-17): the 3.0 contract changes, and where each is stated in full.** The memory ones are
> in §5.7 and the generation ones in §5.6, both edited in place — this block carries only what those sections
> do not own, plus the index. The ordered upgrade path for a consumer is `docs/migration-2.5-to-3.0.md`; the
> reasoning is `docs/DECISIONS.md`.
>
> **`IJobStore` gains three required members** — `TryAcquireSlotAsync`, `ReleaseSlotAsync`,
> `HeartbeatSlotsAsync` (**D73**) — closing §9's last durable-jobs deferral. `JobOptions.GlobalMaxConcurrency`
> bounds concurrent jobs across every process sharing one store; `0` is the default and is the pre-3.0
> unbounded behaviour with no extra round-trip. **It is a slot TABLE rather than a distributed counter
> because a count cannot gate a claim**: folding a `COUNT` into the claim works on SQLite, whose single
> writer makes one statement the whole exclusion, and fails on Postgres, which claims with `FOR UPDATE SKIP
> LOCKED` precisely so workers do not block each other — two claimers read the same MVCC snapshot and both
> take the same headroom. A slot being a ROW turns `SKIP LOCKED` into the cap's ally: two workers skipping to
> two different slot rows is the correct outcome, so the cap is exact *and* claiming stays parallel, by one
> mechanism on both dialects. A store that declines the cap returns `null` from the acquire, which is a
> visible refusal rather than a silently ignored limit. Only a hand-written store is affected; all three
> shipped stores implement them and the compiler names every site.
>
> **The other three breaks, each stated where it belongs:** `IForgettableMemory` splits and removal fans out
> (§5.7, **D63**/**D72**); `IGenerationRouter` gains `StreamAsync` (§5.6, **D67**); and `Lyntai.Generation`
> comes under the full SemVer promise, its 2.0.1 exemption **withdrawn** rather than merely satisfied (§5.6,
> **D70**). The last is a change to what the library PROMISES and not to any signature — nothing stops
> compiling — so it is the one a consumer can miss: those backends are now frozen, and a wire format that
> differs STRUCTURALLY rather than in a value is a major-version risk taken deliberately (**D69**).
>
> **§6 semantic additions:** a generation backend that THROWS is classified and fallen over rather than
> propagating — the router is a trust boundary, because `AddGenerationProvider` is a documented BYO seam and
> discarding every remaining candidate for a third party's defect is the outcome fallback exists to prevent.
> The two paths differ deliberately and it is about money: an inline generation ADVANCES, a thrown SUBMIT is
> `Inconclusive` and SURFACES, because submitting commits the spend and advancing would buy the same render
> twice (**D64**). The caller's own `OperationCanceledException` still propagates on both paths.

## 10. Consuming Lyntai (target ergonomics)

A new app adds package references to `Lyntai.Core` + the storage/provider packages it wants, calls
`services.AddLyntai(...)`, and injects `ILlmClient` (the front door, D5 — `ILlmRouter` only for a
call-site-specific candidate list), `IScoringService`, `IMemoryStore`, etc. No source
copying, no rebuild of the substrate. Adding a storage backend = a new `Lyntai.Storage.X` package that
implements the domain interfaces. Adding a provider = a new `ILlmProvider` or an MEAI `IChatClient`
through the bridge.
