---
name: lyntai-computations
applies_when: another repository or a person reads a figure or status Lyntai produces — a call's verdict, whether it fell back, its cost, a budget refusal, a cache hit, a score aggregate, a job's state, a memory recall — and asks how it is derived
enforces: read each figure from the symbol that derives it; Lyntai reports a backend's cost, it never prices one
---

# The figures and statuses Lyntai derives, and where

**Each figure a consumer reads has one derivation in `Lyntai.Core`. Read it there — the XML doc on the
named symbol is the contract — rather than inferring it from behaviour.**

## Why

These are the numbers a consumer builds a dashboard, a bill or a retry on. A guess about them is wrong in
exactly the case that matters: a verdict read as "will retry", a cost read as Lyntai's own pricing, a
budget read as a hard ceiling.

## How to apply

| Figure or status | Derived from | Where |
|---|---|---|
| **Why a call ended** (`Ok`, `RateLimited`, `Refused`, `Failed`, `Timeout`, `ContextWindowExceeded`, `AuthFailed`, `Unsupported`, `NotConfigured`) | the backend's response, classified once | `ProviderVerdict` (`src/Lyntai.Core/Inference/ProviderVerdict.cs`); transport failures through `ProviderVerdictClassifier.FromHttpFailure` |
| **What the router does about a verdict** — advance, cool down, penalise, surface | a per-verdict action table; a verdict is never itself a promise of fallback | `RoutingPolicy` (`src/Lyntai.Core/Inference/RoutingPolicy.cs`) for text, `MediaRoutingPolicy` for media; consumed by `TextRouter` / `MediaRouter` |
| **A host benched** (dead-host cooldown) | repeated `Failed`/`Timeout`, or one `RateLimited`/`AuthFailed`, keyed per provider (or provider + model under `CooldownScope.ProviderAndModel`) | `TextRouter`'s `deadHosts` parameter and `RoutingPolicy.CooldownScope` |
| **A call's cost** (`CostUsd`) | **what the backend reported**, when it reported anything; Lyntai holds no price table | `ProviderUsage` (`src/Lyntai.Core/Inference/ProviderUsage.cs`); per-shape `TextUsage` / `MediaUsage` project into it |
| **A budget refusal** | the accumulated `CostUsd` and input + output tokens, global and per consumer, checked BEFORE each call — a SOFT ceiling: the crossing call runs, the next is refused; consumer ids fold case. With `BudgetOptions.ReserveCostUsd`/`ReserveTokens` set over a tracker that can reserve, the check also counts every call in flight at its held amount (**D206**) | `BudgetOptions` (`src/Lyntai.Core/BudgetOptions.cs`), `BudgetGate` (`Inference/Budgeting/`), `IUsageTracker` |
| **A cache hit** | SHA-256 over the length-framed output-determining request fields (messages, model, max tokens, temperature, JSON schema); `Consumer` is excluded, so two consumers share a hit | `ResponseCacheKey` (`src/Lyntai.Core/Inference/Caching/IResponseCache.cs`) |
| **A run's total cost** | the sum of its trace steps' `CostUsd` | `TotalCostUsd` (`src/Lyntai.Core/Cortex/TraceModels.cs`) |
| **A scorer aggregate** | mean score and count per scorer across sessions; a re-score of a session replaces that scorer's row | `IScoreStore.AggregateAsync` / `SaveAsync` (`src/Lyntai.Core/Storage/IScoreStore.cs`); a score is `ScoreResult` (`Cortex/ScoreModels.cs`) |
| **A job's state** (`Pending`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `Dead`, `Paused`) | the lease and the handler's outcome; `Dead` is retries exhausted and replayable, `Failed` is a declared permanent failure | `JobStatus` (`src/Lyntai.Core/Jobs/JobModels.cs`), `IJobStore` |
| **What a memory recall returns** | an n-shot walk over headlines, decayed by interference rather than a clock, burying rather than deleting | `docs/memory.md` (the contract, §4 How a recall actually works); figures in `docs/memory-measurements.md` §5 |

**Not confirmed**: which providers report `CostUsd` at all. `ProviderUsage` documents it as "what the
backend said it cost, when it said anything", so a budget capped on cost is only as complete as the
providers in use; each provider's own source under `src/Lyntai.Providers.*` would settle it per backend.
