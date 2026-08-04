# Provider pool — instance lifetime as a library seam (GEN12)

> **Status:** design approved 2026-08-05, not yet implemented.
> **Task:** `TASKS.md` GEN12. **Domain:** Core (`Lyntai.Lifecycle`), consumed by both the LLM and
> generation routing domains. **Compatibility:** additive only — no breaking change to the frozen 1.0
> surface.

## 1. The problem

Where configuration is owned by the deployment, `Add*` registration at `AddLyntai(...)` time is right and
none of this applies. Where configuration is owned **externally** — an end user, or a database the process
polls — three things follow: the settings can change at any moment, the *choice of backend* is itself one
of those settings, and several configurations of the same backend are live **at the same time** (different
tenants, different credentials, different endpoints).

Lyntai has no path for that today. `AddGenerationProvider` captures the options object in a closure at
configure time (`src/Lyntai.Core/Generation/GenerationBuilderExtensions.cs:33-39`), so the configuration a
provider uses is fixed when the container is built.

Consumers reach for the obvious workaround — construct the provider per call — and that is where it goes
wrong.

### 1.1 The correction to GEN12 as filed

GEN12 says a provider instance "accumulates" cooldown and dead-host knowledge, and that rebuilding per call
discards it. **Providers hold no such state.** Every generation backend is a constructor and nothing else —
`OpenAiImageProvider(options, httpFactory, dispose)`, `LocalDiffusionProvider(options, runner)` — with no
mutable fields at all.

The state lives in `DeadHostTracker`, owned by the **router**
(`src/Lyntai.Core/Generation/Routing/GenerationRouter.cs:30`), and registered as a singleton.

The real mechanism is one level up, and it is worse than the filed version:

- `GenerationRouter` materializes its provider list in a field initializer —
  `private readonly IReadOnlyList<IGenerationProvider> _providers = [.. providers];`
  (`GenerationRouter.cs:35`).
- `LlmRouter` memoizes a lookup dictionary in a `Lazy<>` built once (`LlmRouter.cs:24-29`), so even a live
  `IEnumerable` would be frozen after the first call.
- **So a consumer wanting a different backend per call cannot keep one router.** They rebuild it, which
  rebuilds the tracker, and *that* is what destroys the cooldown. Pooling provider instances alone would
  never be visible to a long-lived router.

This matters for the design because it relocates the fix: the unit that must be long-lived and correctly
keyed is not the provider instance but the **bookkeeping keyed to a configuration**.

## 2. Scope boundary

**The library never learns about the configuration source.** The pool takes a key and a factory. Reading
the database, caching it, noticing a change, and deciding when to re-resolve stays entirely with the
consumer. Absorbing any of that would make this a framework rather than a seam, and would bind the library
to one shape of configuration store.

**A pool is not a router.** Registering several backends so a router can choose has different semantics:
routing *falls back*, whereas "this configuration was chosen" must **fail** rather than silently succeed
against a different configuration and bill that credential. A pool selects what was chosen; a router
selects what is healthy.

## 3. Decisions

| Decision | Choice | Why |
|---|---|---|
| Breadth | Domain-agnostic, in Core | `LlmRouter` has the identical snapshot defect; a generation-only fix means writing it twice |
| Occupancy | Many live entries, keyed by configuration | A backend id is not single-occupancy — several configurations run concurrently |
| Strategies | `Bounded`, `Transient`, and the interface | Reuse vs never-reuse is a semantic difference, not a setting; unbounded is Bounded with limits unset |
| Front door | Router factory that pulls from the pool | One injected thing, one call; swapping strategy needs no call-site edit |
| Retirement | Retire, then release when unused | Expiry is not disposal — in-flight renders must finish |
| Cooldown key | The pool key, not the provider id | Same config shares a bench; different credentials never bench each other |
| Admission | Keyed and shared, not per-instance | A per-instance semaphore admits everyone once instances are per-call |
| Eviction | `MaxEntries` (LRU) + `IdleTimeout` | Churn must not grow without limit; an idle credential must not sit in memory |
| Keying | Caller-supplied, contributions named | Only the caller knows what its backend resolves at runtime |
| Config source | Outside the library | See §2 |

## 4. Architecture

All new types live in `src/Lyntai.Core` under namespace **`Lyntai.Lifecycle`** — neutral because it serves
both domains, and deliberately *not* `Lyntai.Providers`, which is already a package prefix
(`Lyntai.Providers.Default`, `.CodexCli`) and would read as an adapter. No new package, so `check-packages`
and `check-bundle` are unaffected.

### 4.1 `IProviderIdentity`

```csharp
public interface IProviderIdentity
{
    string Id { get; }
}
```

`ILlmProvider` and `IGenerationProvider` gain it as a base interface. Both already declare exactly
`string Id { get; }` (as does `IScorer`), so every existing implementor satisfies it unchanged. Adding a
base interface is source- and binary-compatible, which is what makes this legal on the frozen surface.

### 4.2 `ProviderKey` and its builder

The pool's connection string, and the unit *everything* is keyed on — reuse, cooldown, and admission.

```csharp
public readonly record struct ProviderKey(string Slot, string Fingerprint);

public static ProviderKeyBuilder ProviderKey.For(string slot);

// builder
public ProviderKeyBuilder With(string name, string? value);
public ProviderKeyBuilder With(string name, int value);        // + the numeric/bool overloads
public ProviderKeyBuilder WithSecret(string name, string? value);
public ProviderKey Build();
```

Usage:

```csharp
var key = ProviderKey.For(options.Id)
    .With("baseUrl", options.BaseUrl)
    .With("model", options.Model)
    .WithSecret("apiKey", options.ApiKey)   // hashed, never retained
    .Build();
```

Every contribution is **named**, so a forgotten member is visible in review rather than inferred. Values
are folded into the fingerprint in call order with the name included, so `With("a", "bc")` and
`With("ab", "c")` do not collide. `WithSecret` folds in a SHA-256 of the value and never keeps the value —
a rotated credential must change the key without the revoked secret staying reachable behind it.

**The member that is easy to miss**, and the one GEN12 explicitly calls out: values the backend resolves at
**runtime** belong in the key too. A locally-provisioned engine's binary and model paths appear when a
download completes — at which point the saved configuration has not changed at all, yet an instance holding
empty paths keeps failing forever. Keying only on "the configuration the user typed" looks correct and is
not.

**Why not derive the key from the options object automatically.** Four of the five generation options types
are records with `init` members and would work; `LocalDiffusionOptions` is a plain class with settable
properties (`src/Lyntai.Generation/LocalDiffusionProvider.cs:8`) and compares by reference, and
`ComfyUiOptions.Kinds` is an `IReadOnlyList<string>` that record equality compares by reference. So
automatic derivation is correct for three backends and silently wrong for two — the failure class
`pitfalls.md` exists for. Deriving by reflection or serialization is worse still: it breaks the
`IsAotCompatible` / `IsTrimmable` promise that `check-warnings` gates, and D17 already rejected reflection
serialization.

### 4.3 `IProviderPool<TProvider>` — the seam

```csharp
public interface IProviderPool<TProvider> where TProvider : class, IProviderIdentity
{
    /// The instance for this configuration. May or may not be reused — that is the strategy's choice.
    TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory);

    /// Which configuration produced an instance. The cooldown and admission key.
    bool TryGetKey(TProvider instance, out ProviderKey key);

    /// Remove from the lookup. In-flight callers finish on it.
    bool Retire(ProviderKey key);

    /// Every configuration of one backend — the backend the user removed entirely.
    int RetireSlot(string slot);

    ProviderPoolStatistics Statistics { get; }
}

public readonly record struct ProviderPoolStatistics(
    int Live, int Draining, long Created, long Reused, long Evicted);
```

`TryGetKey` is backed by a `ConditionalWeakTable<TProvider, object>` populated at construction, so it
answers correctly under **every** strategy — including `Transient`, where the caller may hold the only
reference to the instance it is asking about, and where a strong-keyed dictionary would leak one entry per
call.

### 4.4 The two shipped strategies

**`BoundedProviderPool<TProvider>`** — the default.

- Reuses while the key is unchanged.
- Bounded by `MaxEntries` (least-recently-used eviction) and `IdleTimeout`.
- Both evictions take the **retirement** path, so hitting a pool limit can never abort a running call.
- Leaving both limits unset gives an unbounded pool — which is why "unbounded" needs no type of its own.

**`TransientProviderPool<TProvider>`** — never reuses. Every `GetOrAdd` constructs, and the instance enters
the drain list immediately. This is the "new options every time" path, and it is a **registration** rather
than a different call site. It still records the key, which is why cooldown and admission keep working
under it.

Both strategies compose the same internal drain component (§4.5) — the weak-reference list, the sweep
timer, and the release rules — so retirement behaves identically whichever is registered, and both pools
are `IDisposable` because they own that timer. Only the *reuse* decision differs between them, which is
what makes the pair a strategy rather than two implementations of overlapping behaviour.

Anything else is BYO through the interface: a pool sharing state across processes, one reporting to the
host's telemetry, one with a different eviction policy.

```csharp
public sealed class ProviderPoolOptions
{
    /// Live entries before least-recently-used eviction. Null = unbounded.
    public int? MaxEntries { get; set; } = 64;

    /// Retire an entry unused for this long. Null = never on idle.
    public TimeSpan? IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// How often the sweep runs. Also drives release of drained entries.
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}
```

Defaults ship rather than forcing a choice: an unconfigured pool that grows without limit is a worse
default than one with generous bounds, and a host that wants unbounded can say so explicitly.

### 4.5 Retirement and release

A retired entry moves to a drain list holding a **weak reference**. A single periodic sweep — one timer,
shared with idle eviction — releases entries whose last user has gone, disposing them only if they
implement `IDisposable` or `IAsyncDisposable`. Nothing implements either today, so the sweep is inert;
writing the contract now means a future warm-subprocess backend does not reopen it.

No lease is taken and none returned, so there is no forgotten-return failure mode. The price is that
release timing is not deterministic — acceptable precisely because the resources involved are, today,
nothing.

The clock and the sweep are injected/exposed for deterministic tests, matching `DeadHostTracker`'s existing
`Func<DateTimeOffset>? clock` convention: `BoundedProviderPool` takes a clock and exposes `Sweep()`, which
the timer calls and a test can call directly.

### 4.6 Router factories — the one injected thing

Only the caller knows which configurations are its own, so it hands the factory its keys and their
construction delegates. The factory resolves each through the pool and returns a **fully-governed** router.
Every long-lived piece stays a singleton: the tracker, the limiter, the usage ledger, the admission table.

```csharp
public readonly record struct ProviderRegistration<TProvider>(ProviderKey Key, Func<TProvider> Create);

public interface IGenerationRouterFactory
{
    /// Pooled: each registration is resolved through the pool, and the cooldown key is its ProviderKey.
    IGenerationRouter For(IReadOnlyList<ProviderRegistration<IGenerationProvider>> providers);

    /// Already-constructed instances — the container-composed path. No pool involvement;
    /// the cooldown key stays `p => p.Id`, exactly as today.
    IGenerationRouter For(IReadOnlyList<IGenerationProvider> providers);
}

public interface ILlmRouterFactory
{
    ILlmRouter For(IReadOnlyList<ProviderRegistration<ILlmProvider>> providers);
    ILlmRouter For(IReadOnlyList<ILlmProvider> providers);
}
```

Composition (rate limit **inside** budget, matching the LLM front door) moves out of
`GenerationBuilderExtensions.EnsureRouter` and into the factory — one composition path rather than two that
must be kept in step. The existing singleton registration then becomes a call to the **instance** overload
over `sp.GetServices<IGenerationProvider>()`, which is why an app that never touches the pool keeps its
current behaviour bit for bit: same provider set, same `p => p.Id` cooldown key, same decorator order.

The routers themselves change in exactly one way: an **optional** `Func<TProvider, string>? cooldownKey`
constructor parameter, defaulting to today's `p => p.Id`. The factory binds it to `pool.TryGetKey`.
Untouched wiring behaves exactly as it does now.

### 4.7 Admission control, keyed like the tracker

A shared table of semaphores indexed by `ProviderKey`, resolved **per attempt** rather than captured per
instance:

```csharp
public sealed class ProviderAdmission
{
    /// Waits for a permit, then returns the release handle. When the slot's limit is 0
    /// (unlimited) this completes synchronously and returns a no-op handle — never null,
    /// so the call site is one `using` with no branch.
    public ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct);
}

public sealed class ProviderAdmissionOptions
{
    /// Concurrent calls allowed per configuration. 0 = unlimited. Declared per slot,
    /// enforced per key — see below.
    public int Default { get; set; }
    public IDictionary<string, int> BySlot { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
```

Limits are **declared per slot** (a backend type has a natural capacity: "a local engine takes one render
at a time") but **enforced per key** (the contended resource belongs to a configuration). That split is
deliberate and gives the behaviour that matters: two consumers pointing at the *same* self-hosted engine
share its capacity, while two tenants on different hosted endpoints do not throttle each other.

## 5. The consuming shape

```csharp
// per request — one injected factory
var cfg = await _settings.ForTenantAsync(tenantId, ct);   // your source; the library never asks

var router = _routers.For([
    new(KeyFor(cfg.OpenAi), () => new OpenAiImageProvider(cfg.OpenAi, _http)),
    new(KeyFor(cfg.Local),  () => new LocalDiffusionProvider(cfg.Local, _runner)),
]);

var result = await router.GenerateAsync(candidates, request, ct);
```

```csharp
// at startup — the strategy, and nothing else, decides reuse
services.AddLyntai(b => b.UseProviderPool(new ProviderPoolOptions {
    MaxEntries = 64, IdleTimeout = TimeSpan.FromMinutes(10),
}));

// a fresh instance every call — the call site above does not change
services.AddLyntai(b => b.UseTransientProviders());
```

The pool registers as an open generic (`TryAddSingleton(typeof(IProviderPool<>), typeof(BoundedProviderPool<>))`),
so a host replaces it by registering first — the convention already used for `IUsageTracker` and
`IRateLimiter`. The pool stays injectable directly for anyone who wants instances without a router.

## 6. Semantics

- **A throwing factory adds nothing and propagates.** Because entries are keyed rather than replaced in
  place, a failed build cannot damage the entry already there.
- **`instance.Id != key.Slot` throws at registration** (`ArgumentException`). This is what the generic
  constraint buys; without it the mismatch stays invisible until routing quietly cannot find the candidate,
  because the router matches candidates on `p.Id`.
- **Release failures are swallowed and logged.** A misbehaving `Dispose` must not stall the sweep or break
  a reconfiguration.
- **Eviction retires, never tears down.** Both bounds go through the drain path.
- **Concurrent `GetOrAdd` on one key builds once** under `Bounded` — two requests for a newly-configured
  tenant arriving together must not produce two instances each holding half the history. Under `Transient`
  it builds twice by definition: that is the contract, not a race.
- **Thread safety** is the pool's responsibility, not the caller's. A UI request and a background job
  arrive together by default.
- **A host-supplied `HttpClient` is never disposed by the pool.** The ownership rule already centralised in
  `GenerationProviderBuilderExtensions.HttpBackend` is carried forward, not re-decided. Under `Transient`
  this stops being theoretical — instances retire constantly, so a wrong answer here fails on the second
  call rather than eventually.

## 7. Traps this design exists to avoid

Each of these passes a build and a plausible test suite while being wrong. They belong in
`.claude/knowledge/pitfalls.md` when this lands.

1. **Disposing a replaced instance aborts in-flight work.** With configuration arriving from a source that
   changes at any moment, and renders that legitimately take minutes, a routine configuration poll kills a
   healthy in-flight render — a failure caused entirely by the library's own bookkeeping.
   `IHttpClientFactory` is the model: an expired handler leaves the lookup so new callers get the fresh
   one, while existing users keep the old one until they finish. **Expiry is not disposal.**

2. **Cooldown keyed on the provider id becomes cross-tenant benching.** The key is
   `generation::{providerId}` (`GenerationRouter.cs:152`). Once two configurations of `openai-images` are
   live with different credentials, one exhausting its quota benches the other — whose key was fine.
   Conversely two consumers pointing at the same self-hosted URL *should* share a bench when that host is
   down. Keying on the pool key gets both right; keying on the id gets one wrong, and only in production.

3. **A per-instance concurrency semaphore admits everyone under a no-reuse strategy.** A `SemaphoreSlim`
   field on a per-instance decorator bounds anything only while the instance is shared. Register
   `Transient` and every call constructs its own decorator with its own fresh semaphore, so the local
   engine the limit exists to protect thrashes exactly as it would with no limit configured.

4. **A key derived from the options object is right for some backends and silently wrong for others** — see
   §4.2.

## 8. Testing

TDD per the repo convention: failing test first. Every behavioural test runs against **both** shipped
strategies. Plus the sabotage standard: disabling reuse must fail the reuse test, and ignoring the key must
fail every replacement test.

| Area | What is pinned |
|---|---|
| Occupancy | Several configurations of one backend id live at once without collision; reuse on an identical key under `Bounded`; a distinct instance per distinct key, including a key differing only in the credential |
| Strategy | `Bounded` reuses and `Transient` does not, from the same call site with no code change. **Cooldown accumulates correctly under `Transient`** |
| Retirement | **A call in flight completes normally after its instance is retired.** A retired entry leaves the lookup immediately; a new call gets the new instance; release happens only once nothing references the old one |
| Admission | The concurrency limit holds under **both** strategies; two consumers sharing one configuration share its capacity |
| Eviction | LRU eviction at `MaxEntries`; retirement after `IdleTimeout` against an injected clock; neither aborts an in-flight call; both limits unset means no eviction |
| Cooldown | Two configurations of one backend bench independently; two consumers sharing a configuration share the bench; history survives a configuration change |
| Concurrency | Parallel `GetOrAdd` on one key invokes the factory exactly once under `Bounded` |
| Keys | Two logically identical configurations produce equal keys; a changed secret produces a different key; the secret never appears in the key's string form; name/value framing prevents concatenation collisions |
| Release | Disposal invoked for a disposable provider; a disposal exception is swallowed and does not stall the sweep; a host-supplied `HttpClient` is never disposed |
| Compatibility | An existing `AddLyntai` + `Add*Provider` app routes identically with no pool configured |

## 9. Surfaces touched

- `ApiSurfaceTests` baselines — the new public types, and the two provider interfaces gaining a base.
- `docs/DECISIONS.md` — a new entry: pooling as a registered strategy; a pool selects what was chosen while
  a router selects what is healthy; configuration-scoped cooldown and admission.
- `.claude/knowledge/llm-and-router.md` (the cooldown key is no longer the provider id),
  `.claude/knowledge/pitfalls.md` (§7), and the README.
- `TASKS.md` — GEN12 moves to `docs/task-archive.md` on completion, per `task-lifecycle.md`.
- Frozen LLM surface: additive only — a base interface on an existing interface, and optional constructor
  parameters.

## 10. Naming, settled and open

- **`IProviderPool`, not `IProviderRegistry`.** An earlier revision argued for "registry" on the grounds
  that nothing pooled multiple instances of anything. Under a continuously-updated, multi-configuration
  source that objection is wrong: many entries are live at once, keyed by configuration, bounded, evicted
  and drained. GEN12's original word was right.
- **`TransientProviderPool`** — a pool that never pools. Accurate about lifetime (matching .NET's
  "transient" vocabulary) and odd as a word. `NoReuseProviderPool` is uglier and clearer; keeping
  `Transient` for the DI-lifetime echo.
- **Namespace `Lyntai.Lifecycle`** — accepted. Not `Lyntai.Providers` (a package prefix), not
  `Lyntai.Pooling` (the namespace also holds the identity interface and the router-factory contracts).

## 11. Deliberately not doing

- **Leases / reference counting.** Deterministic release and an answer to "how many calls are running on
  this configuration", at the price of a `using` block at every call site and a forgotten return pinning an
  instance forever. Revisit only if a provider ever holds something that must be released promptly.
- **A max instance lifetime** (`IHttpClientFactory`'s two-minute handler rotation). That exists to pick up
  DNS changes; here the `HttpClient` and its handler are owned by `IHttpClientFactory` already, so the
  provider has no such staleness to rotate away.
- **Multiple instances per configuration.** The backends are stateless and thread-safe, so parallelism is
  gated by admission control, not by instance count. Adding instances would buy nothing and cost lease
  semantics.
- **Automatic key derivation** — §4.2.
- **A configuration-source abstraction** — §2.
