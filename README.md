# Lyntai (灵台)

> 灵台 (língtái) — "the numinous platform," a classical Chinese name for the seat of the mind.

A reusable **.NET 10 library**: the shared **cortex + persistence** substrate for AI apps. Give a new
project an LLM provider abstraction with routing + fallback, pluggable storage, and an LLM-ops layer
(prompt registry, scoring, traces, task-scoped memory) — `AddLyntai(...)` and go, no rebuilding it per app.

Extracted from the good parts of four sibling projects: the storage/scoring/trace patterns of
**Gatherlight**, the provider abstraction of **Vidora**, the verdict-classification + memory of **Sonora**,
mastra's **composable domain storage**, and odysseus's **streaming-aware fallback**.

## Status

<!-- version-indicator: the **vX.Y.Z below is AUTO-SYNCED from src/Directory.Build.props <VersionPrefix> by
     `node devtools/dev.mjs pack` / `doctor --fix` (the release pipeline bumps the version, pack updates this
     headline). Don't hand-edit the version here to release — bump VersionPrefix; the header follows. -->
**v2.1.0 — a hardened, batteries-included cortex substrate, now with a media generation platform.**
Twelve packages, one public front door, and a public API frozen under SemVer 2.0 since 1.0.

What is in it, by domain: **LLM** — routing with streaming-aware fallback across CLI / HTTP / MEAI-bridged
backends, a configurable per-verdict `RoutingPolicy`, dead-host cooldown, native + prompt tool-calling.
**Generation** *(experimental)* — one capability-aware seam for image/video/audio/3d with three delivery modes
(inline, submit→poll→fetch, streaming), durable renders over `Lyntai.Jobs`, and five backends.
**Storage** — SQLite / Postgres / InMemory, mixable per domain, with FTS5-trigram recall and feature toggles.
**Agents** — a tool loop, two-gate chat orchestration, guards, and both halves of MCP. **Ops** — prompt
registry, scoring/eval, run traces, task-scoped + semantic + curated memory, durable jobs with priorities /
DLQ / cron / cancellation, a secret vault, OTel across all three domains, and front-door governance (cache,
budget, rate limit).

Since 1.0 froze the API (2026-07-28): **1.1** generic CLI tool-hosting · **1.2** turn-free backend probe,
auth and pinned self-install · **2.0.1** the generation platform plus a coherent package graph — one rule for
package boundaries, a starting bundle, and four build gates that keep the packaging claims honest ·
**2.1.0** generation ergonomics — named input factories, an `Add*` per media backend, and BYO-`HttpClient`
ownership brought in line with the LLM side.
`CHANGELOG.md` has the per-release detail; `docs/DECISIONS.md` has the reasoning behind the load-bearing calls.

> **Versioning.** From **1.0**, Lyntai follows **SemVer 2.0**: no breaking public-API change without a major
> bump (the `ApiSurfaceTests` baseline gates it).
>
> **One carve-out, stated plainly: `Lyntai.Generation.*` is EXPERIMENTAL as of 2.0.1** and is exempt from that
> promise until its backends have been verified against real services (`TASKS.md` GEN-VERIFY). It is a complete,
> tested platform — but two of its backends were written from vendor documentation with no key to call, a third's
> argv is ported rather than measured, and `IGenerationStreamProvider` has no implementation at all yet. Shapes
> that meet reality tend to change. Freezing that under SemVer would mean either a major bump for a fix we
> already expect, or leaving a known-wrong shape in place — so it is marked instead of pretended. Everything
> else (LLM routing, storage, cortex, jobs, guards, secrets, memory, tools) carries the full promise. **Upgrading 0.31 → 1.0:** the 0.x migrations were collapsed
> into per-domain 1.0 baselines — the net schema is identical but the migration ledger is renumbered, so
> **drop your `lyntai_*` tables (including `lyntai_version_info`) or delete the dev database before the first
> 1.0 run**; Lyntai recreates them. One-time; the ledger is append-only thereafter.

- `docs/2026-07-17-lyntai-design.md` — the design contract (interfaces, fork decisions, semantics, scope).
- `docs/ROADMAP.md` — what's shipped, what's next, and the remaining path to 1.0.
- `docs/AOT.md` — per-package trimming/Native-AOT status.
- `CHANGELOG.md` — per-release detail, breaking changes called out.

## Packages

| Package | What it gives you |
|---|---|
| **`Lyntai`** | **The starting set (6 of 12)** — Core + the dependency-free LLM backends + the MEAI bridge + both halves of MCP + **in-memory** storage. Not the whole library: add `Lyntai.Storage.Sqlite` to persist and `Lyntai.Generation` for media. |
| `Lyntai.Core` | Every domain's contracts and engines: LLM routing/fallback, generation, cortex (prompt/scoring/trace), jobs, guards, secrets, memory, storage interfaces, tools, DI. Deps: DI + Logging abstractions only. |
| `Lyntai.Providers.Default` | The dependency-free **LLM** backends: authenticated `claude` and `codex` CLIs; any OpenAI-compatible endpoint (OpenAI/Ollama/OpenRouter/Azure) for chat and embeddings. Media backends moved to `Lyntai.Generation`. |
| `Lyntai.Providers.ExtensionsAi` | Bridge, both directions: any `Microsoft.Extensions.AI` `IChatClient` → a Lyntai provider, and `AsChatClient()` back. *(In the bundle — MCP already pins the MEAI abstractions, so it costs no new dependency.)* |
| `Lyntai.Providers.Local` | In-process local GGUF inference via LLamaSharp — add an `LLamaSharp.Backend.*` for your hardware. |
| `Lyntai.Storage.Sqlite` | SQLite for every storage domain (Dapper + FluentMigrator + FTS5; ships a native SQLite binary). |
| `Lyntai.Storage.Postgres` | PostgreSQL storage (Npgsql + `pg_trgm` recall) for a server-backed deployment. |
| `Lyntai.Storage.InMemory` | Zero-dependency in-memory storage — tests, ephemeral use, or mixed per-domain. |
| `Lyntai.Tools.Mcp` | Expose an MCP server's tools as Lyntai `ITool`s. (The tool *contract* is in Core; this is the wire adapter.) |
| `Lyntai.Tools.Mcp.Hosting` | The reverse: host your `ITool`s as an ephemeral loopback MCP server for a CLI that runs its own agent loop. Runs on `HttpListener` — **no ASP.NET Core**. |
| `Lyntai.Secrets.Dpapi` | Windows DPAPI + recovery-key envelope for the secret vault. |
| `Lyntai.Generation` | **Experimental.** The media backend set — OpenAI images, Automatic1111, ComfyUI, a local `sd-cli` subprocess, and the fal.ai queue for video, each with an `Add*` of its own. Adds only `Microsoft.Extensions.Http` (its shims register named clients); the generation *contracts* are in Core. Split out so media can iterate without churning the LLM packages (D34). |

Packages are split by **dependency footprint**, never by vendor or by size: every boundary answers "which
dependency does this isolate?" with something concrete. Backends that need nothing extra share
`Providers.Default`; anything dragging a native runtime (LLamaSharp, native SQLite), a platform-specific API
(Windows DPAPI) or a heavy framework (ASP.NET Core, via MCP hosting) stays its own package — and `Lyntai.Core`
carries the smallest footprint of all, because it is the one package you cannot opt out of
(`docs/DECISIONS.md` D31).

What goes **in the bundle** is a separate, budgeted decision (D32): a package joins only if it adds no
third-party dependency beyond the `Microsoft.Extensions.*` band, or if it is near-universal and the cost is
accepted explicitly — never if it carries a native payload or a platform-specific API. `node devtools/dev.mjs
check-bundle` fails the build if that closure ever drifts, so the one-line install can't quietly grow heavy.

## Consuming Lyntai

```bash
dotnet add package Lyntai                  # the recommended STARTING set — 6 of the 12 packages
dotnet add package Lyntai.Storage.Sqlite   # persistence (the bundle's storage is IN-MEMORY)
dotnet add package Lyntai.Generation       # image/video/audio backends (experimental)
```

**`Lyntai` is a starting set, not the whole library.** It gives you Core, the LLM backends, the MEAI bridge,
both halves of MCP, and **in-memory** storage. The two that surprise people: nothing persists until you add
`Lyntai.Storage.Sqlite` (or `.Postgres`), and generation is not included. The five packages left out are left
out for a reason — a native payload (`Storage.Sqlite`, `Providers.Local`), a platform-specific API
(`Secrets.Dpapi`), a server dependency (`Storage.Postgres`), or an unverified surface (`Lyntai.Generation`) —
see `docs/DECISIONS.md` D32.

**Convenience vs size.** `Lyntai` is a bundle with no code of its own — it just pulls a curated set. A
framework-dependent `dotnet publish` copies the **whole** dependency graph and analyses nothing, so that lands
~3.2 MB of assemblies in your output folder whether or not you call into them — most of it the MCP SDK (1.9 MB
including the `Microsoft.Extensions.AI.Abstractions` it pins). Two levers, either of which removes it:

- **Reference only the packages you use** instead of the bundle. `Lyntai.Core` + one provider is the lean path,
  and the boundaries exist precisely so this is possible (`docs/DECISIONS.md` D31).
- **`PublishTrimmed=true`** (needs a self-contained publish). Unused assemblies are dropped outright *and* the
  used ones are trimmed internally. Measured on a router-only app, Lyntai's whole footprint goes **3.2 MB →
  0.21 MB** (`Lyntai.Core` alone 528 KB → 40 KB), because every compatible package carries honest `IsTrimmable`
  metadata — see [`docs/AOT.md`](docs/AOT.md).

Either way an unused dependency costs **nothing at runtime**: assemblies load on first type reference, so one you
never touch is never opened.

Then compose in DI:

```csharp
using Lyntai;                       // the builder + Add*/Use* extensions
using Lyntai.Cortex.Scorers;
using Microsoft.Extensions.DependencyInjection;

services.AddLyntai(cfg =>
{
    cfg.AddClaudeCliProvider();                          // spawns the authenticated `claude` CLI, no API key
    cfg.AddOpenAiCompatibleProvider("ollama", o => o.BaseUrl = "http://localhost:11434");
    cfg.AddExtensionsAiProvider("openai", myChatClient); // bridge any Microsoft.Extensions.AI IChatClient
    cfg.UseSqliteStorage("app.db");                      // all five storage domains, migrated on startup
    cfg.AddScorer<OutcomeScorer>();                      // eval dimensions are DI registrations
    cfg.AddScorer<RelevancyScorer>();                    // (this one is an LLM judge through the router)
    cfg.UseDefaultCandidates("claude-cli", "ollama");       // router fallback order
});
```

Then inject the front door. **To your app, Lyntai behaves like one LLM provider** — `ILlmClient` has
`ILlmProvider`'s shape, and candidate order, fallback, and dead-host handling happen invisibly behind it:

```csharp
public sealed class MyFeature(
    ILlmClient llm,
    IPromptRegistry prompts, IPromptComposer composer,
    IScoringService scoring, ITraceService traces, IMemoryStore memory)
{
    public async Task<string> AskAsync(string question, CancellationToken ct)
    {
        var prompt = await prompts.RenderAsync("myfeature.ask",
            "Answer briefly: {question}", new Dictionary<string, string> { ["question"] = question }, ct);
        prompt = await composer.ComposeAsync(prompt, taskKey: "myfeature", ct: ct); // + learned facts

        var reply = await llm.CompleteAsync(
            new LlmRequest { Messages = [LlmMessage.User(prompt)], Consumer = "myfeature" }, ct);
        return reply.Verdict == LlmVerdict.Ok ? reply.Text : throw new InvalidOperationException(reply.Detail);
    }
}
```

(`ILlmRouter` stays available for call sites that genuinely need their own candidate list.)

And if your app already speaks `Microsoft.Extensions.AI`, consume Lyntai **as** an `IChatClient` —
routing, fallback, and the ops layer come along silently:

```csharp
IChatClient chat = serviceProvider.GetRequiredService<ILlmClient>().AsChatClient();
```

### The semantics you're getting (design §6)

- **Fallback router:** candidates are deduped and tried in order; `Failed`/`Timeout` advances,
  `RateLimited` puts that host on immediate cooldown and advances to the next candidate (a 429 is
  terminal for the host's window, not for the fleet), `Refused` surfaces with no fallback (content
  policy follows the prompt, not the host).
- **Streaming never falls back after the first token** — pre-content failures move to the next
  candidate, mid-stream errors pass through unchanged (your consumer never sees duplicated output).
- **Per-request refusal check** — set `LlmRequest.RefusalPattern` (a regex) and an otherwise-`Ok` reply
  whose text matches surfaces as `Refused` (e.g. a per-language "I can't help with that"). Screened at the
  outermost front-door layer, so even a cached hit is re-checked.
- **Dead-host cooldown** instead of exponential backoff; any success resets.
- **Per-request timeout** — set `LlmRequest.TimeoutSeconds` (or a per-consumer `TimeoutByConsumer` default)
  when one call legitimately runs far longer than the global `ProviderTimeout` (e.g. a CLI-agent run),
  without inflating every short call. Precedence: request → consumer → global; clamped to `MaxProviderTimeout`.
- **All of the above is the default `RoutingPolicy` — tune it without a fork.** Retry a transient
  fault on the same candidate before failing over, override what each verdict does, cool by
  `(provider, model)` instead of whole-host, or keep the sole candidate always live:
  ```csharp
  cfg.ConfigureRouting(r =>
  {
      r.Retry(LlmVerdict.Failed, 1);                     // one retry before advancing
      r.CooldownScope = CooldownScope.ProviderAndModel;  // per-model rate-limit cooldown
      r.On(LlmVerdict.RateLimited, FallbackAction.Surface); // e.g. don't fall back on 429
  });
  ```
- **Prompt overrides** live in the key-value store under `lyntai.prompt.<name>`; an override that
  drops a `{placeholder}` present in the default is rejected (falls back to the default, with a warning).
- **Memory recall is bounded and fail-open:** FTS5 trigram match (works for CJK substrings), LIKE
  fallback, capped per (task, scope) — and it never throws into your prompt path.
- **Curated memory catalog** (`ICuratedMemoryStore`) sits beside the recall log for hand-managed context:
  entries grouped by `Kind`, each individually enable/disable-able and editable (`UpdateAsync`, incl.
  re-categorising `kind` in place), with an arbitrary app-owned `string→string` `Metadata` map (title,
  source, author, …) that is both stored (as one opaque JSON field per backend) and queryable by exact
  key/value (`metadataMatch` on `ListAsync`/`SearchAsync`, backed by a plain relational index — identical
  across backends), plus keyword `SearchAsync` over content (same index machinery and fail-open semantics as
  memory recall), rendered into per-kind prompt sections by `CuratedMemorySections.Compose` — across all
  three backends.
- **Env overrides beat code config:** `LYNTAI_TIMEOUT_SECONDS`, `LYNTAI_MAX_TIMEOUT_SECONDS`,
  `LYNTAI_DEADHOST_THRESHOLD`, `LYNTAI_DEADHOST_COOLDOWN_SECONDS`, `LYNTAI_DEFAULT_CANDIDATES`
  (`providerId[:model],…`), `LYNTAI_MODEL_<CONSUMER>` (+ `LYNTAI_DEFAULT_MODEL` alias),
  `LYNTAI_RETRY_FAILED`/`_TIMEOUT`/`_BACKOFF_SECONDS`, `LYNTAI_COOLDOWN_SCOPE`,
  `LYNTAI_TOOL_LOOP_MAX_ITERATIONS`, `LYNTAI_CACHE_TTL_SECONDS`/`_MAX_ENTRIES`,
  `LYNTAI_BUDGET_MAX_COST_USD`/`_MAX_TOKENS`, `LYNTAI_RATELIMIT_PERMITS_PER_SECOND`/`_BURST`/`_MAX_WAIT_SECONDS`,
  the durable-jobs family `LYNTAI_JOBS_LEASE_SECONDS`/`_POLL_SECONDS`/`_MAX_ATTEMPTS`/`_BACKOFF_SECONDS`/`_DEFAULT_CONCURRENCY`/`_MAX_STEP_LOG`,
  and `LYNTAI_PROVIDER_CMD` (point the CLI provider at a stub — how the tests/e2e spend zero tokens).
- **Shared-database safe:** every SQLite object Lyntai creates is prefixed `lyntai_` (including the
  migration version table), so `UseSqliteStorage` can point at an existing app database.
- **Mix storage backends per domain:** the domain interfaces are independent, so the DI container is
  the registry — `UseSqliteStorage(path)` for most domains, then override one
  (`services.AddSingleton<IMemoryStore>(...)`, last registration wins). `UseInMemoryStorage()` stands
  alone or backfills gaps. `UseSqliteStorage(path, SchemaMigration.OnFirstUse)` defers migration I/O off
  DI composition.

### Structured output

```csharp
var reply = await llm.CompleteJsonAsync(new LlmRequest
{
    Messages = [LlmMessage.User("Summarize as JSON.")],
    JsonSchema = """{"type":"object","properties":{"summary":{"type":"string"}}}""",
});
// reply.Verdict == Ok guarantees reply.Text parses as a single JSON object
// (tolerant extraction from prose/fences, one retry, else Failed — design §6)
```

### Response caching

Opt in and identical repeated completions come back from a cache instead of a provider — cutting cost and
latency, and making repeated runs deterministic. It wraps the single front door, so the tool loop,
orchestrator, and scorers all read through it once enabled.

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(/* … */)
    .AddResponseCache(c => c.Ttl = TimeSpan.FromHours(6))); // defaults: 1h TTL, 1000 entries
```

Keyed by a stable hash of the output-determining request fields (messages, model, max tokens, temperature,
JSON schema) — `Consumer` is excluded, so two consumers issuing the same request share a hit. Only clean
`Ok`, non-streaming completions are cached; **streaming**, requests carrying **native tools** (the tool
loop is stateful), and **non-Ok** replies never are. The in-memory cache is the default; call `UseSqliteResponseCache()` (or `UsePostgresResponseCache()`) to
persist it so it survives restarts, or register your own `IResponseCache` before `AddResponseCache` to back
it with Redis or another shared store.

### Semantic memory

The lexical memory store (`IMemoryStore`) recalls by keyword (FTS-trigram). For meaning-based recall, bring
an embedding model and use `ISemanticMemory` — facts are remembered by their embedding and recalled by
cosine similarity, so a query finds relevant memories without sharing keywords.

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(/* … */)
    // built-in embedder over any OpenAI-compatible /v1/embeddings (OpenAI, LM Studio, Ollama, Azure)
    .AddOpenAiCompatibleEmbedder("embeddings", o =>
    {
        o.BaseUrl = "http://localhost:11434";   // e.g. local Ollama
        o.Model = "nomic-embed-text";
    }));
    // …or bring your own: .AddEmbeddings(myEmbedder)  // any IEmbedder — a hosted endpoint or local model

var memory = sp.GetRequiredService<ISemanticMemory>();
await memory.RememberAsync(task: "support", scope: "faq", "You can cancel your subscription anytime.");
var hits = await memory.RecallAsync("support", "faq", query: "how do I stop paying?", k: 5);
// hits ranked by similarity, each with a Content + cosine Score
```

Vectors live in a swappable `IVectorStore` — the built-in `InMemoryVectorStore` (exact brute-force cosine)
is the default; call `UseSqliteVectorStore()` to persist them in SQLite, or `UsePostgresVectorStore()` for
**pgvector** (the cosine search runs in the database — SQL-side top-k, not brute-force in the app). Or
register your own before `AddLyntai` for another vector DB — the recall code is unchanged. Scoped by (task,
scope) like the lexical store; re-remembering identical content dedups.

Registering an embedder also upgrades the **chat orchestration** automatically: `IChatOrchestrator`'s
memory injection becomes **hybrid** (semantic hits lead, then lexical entries fill in, deduped) and each
remembered exchange is written to both stores — so a later turn recalls earlier ones by meaning, not just
keywords. With no embedder, the chat path stays purely lexical.

### Usage budgeting

Cap spend. The budget meters token/cost usage across the front door and refuses further calls once a cap is
reached — without hitting a provider.

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(/* … */)
    .AddUsageBudget(b =>
    {
        b.MaxCostUsd = 20.00;                              // global ceiling
        b.PerConsumer["scoring"] = new(MaxCostUsd: 2.00);  // a tighter cap for one consumer
    }));

// query or reset spend at runtime
var spent = sp.GetRequiredService<IUsageTracker>().Total().CostUsd;
```

Over a cap, a completion returns `Verdict == Refused` (a stream yields one Error chunk) and no provider is
called. The ceiling is **soft**: the call that crosses a cap still runs (its cost isn't known until it
returns), the next is refused. Compose with the cache and a **cached hit is free** — it never counts toward
the budget (the cache is the outermost decorator). Call `UseSqliteUsageTracking()` (or
`UsePostgresUsageTracking()`) to persist spend across restarts, or register your own `IUsageTracker` for
shared accounting.

### Rate limiting

Throttle throughput with a token bucket. Over the configured rate a call waits briefly for a permit, then
is refused (`Verdict == RateLimited`) rather than hammering the provider.

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(/* … */)
    .AddRateLimit(r =>
    {
        r.PermitsPerSecond = 10;
        r.Burst = 20;                                 // allow a burst after idle
        r.PerConsumer["scoring"] = new(PermitsPerSecond: 2);
    }));
```

Together, caching, budgeting, and rate limiting are the front-door **governance trio** (cost/latency,
spend, throughput) and compose on one chain — **cache outermost, rate-limit innermost** — so a cached hit
spends nothing: no budget accounting and no rate-limit permit. Register your own `IRateLimiter` for a
limiter shared across processes.

### Observability

Lyntai emits OpenTelemetry GenAI-convention telemetry from the router — the same schema
`Microsoft.Extensions.AI`'s `OpenTelemetryChatClient` uses, so own-seam and bridged providers land
in one backend. Nothing is emitted unless you subscribe:

```csharp
tracerProviderBuilder.AddSource(LyntaiDiagnostics.ActivitySourceName);        // "Lyntai.Llm" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.MeterName);                   // duration, token usage,
                                                                              // time_to_first_chunk
// the agentic subsystems (tool loop, durable jobs, guards) emit on a second source/meter:
tracerProviderBuilder.AddSource(LyntaiDiagnostics.GenerationActivitySourceName);  // "Lyntai.Generation" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.GenerationMeterName);         // render duration + reported cost

tracerProviderBuilder.AddSource(LyntaiDiagnostics.AgentActivitySourceName);   // "Lyntai.Agents" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.AgentMeterName);              // tool/job/guard metrics
```

`chat {model}` client spans carry `gen_ai.system` (provider id), `gen_ai.request.model`, token
usage, and `error.type` (the verdict) on failure. `time_to_first_chunk` marks the streaming
fallback point of no return. On the `Lyntai.Agents` side, a `tool_loop` span nests one
`execute_tool {name}` span per call, `run_job {type}` spans carry the lane/outcome (with
processed/duration metrics), and a guard-decisions counter tags each block/replace by gate — so an
agent run traces end-to-end next to its LLM calls.

OpenTelemetry is the **automatic** observability path. `ITraceService` is a separate, **app-driven**
API for a durable, step-shaped run history you query later: call `Begin(sessionId, mode)` and
`recorder.Record(step)` yourself, and it persists a `RunTrace` to the wired `ITraceStore`
(SQLite/Postgres/InMemory). The batteries-included flows don't auto-populate it — reach for it when you
want your own queryable trace timeline; reach for OTel for live tracing/metrics.

### Bring your own resources

Lyntai defines the interfaces; your app owns the resource lifecycle wherever that matters.

```csharp
services.AddLyntai(cfg =>
{
    // Provider presets (or the generic AddOpenAiCompatibleProvider, or your own ILlmProvider):
    cfg.AddOpenAiProvider(apiKey, defaultModel: "gpt-4o-mini");
    cfg.AddOllamaProvider(defaultModel: "llama3.2:3b");
    cfg.AddProvider(_ => new MyCustomProvider());          // BYO ILlmProvider

    // BYO HttpClient — your configured client (Polly, auth handlers, proxy, a named client):
    cfg.AddOpenRouterProvider(apiKey,
        httpClient: sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("resilient"));

    // BYO DB connection + schema ownership:
    cfg.UseSqliteStorage(myConnectionFactory);             // you own connection lifecycle
    cfg.UsePostgresStorage(connString, SchemaMigration.None);  // you own the schema (no Lyntai migrations)
});

// BYO process execution — control how the claude CLI is spawned (sandbox, custom shell, remote):
services.AddSingleton<IProcessRunner>(new MySandboxedProcessRunner());
```

Anything you register wins over Lyntai's default (the defaults use `TryAdd`), and every storage domain
is itself an interface (`IKeyValueStore`, `IMemoryStore`, …) you can implement wholesale.

### Backend self-maintenance: version · upgrade · pinned install · auth

Four **optional** provider capabilities (`IProviderInstallation`, `IProviderUpdater`,
`IProviderVersionInstaller`, `IProviderAuth`), so a host can show what its backend actually is, whether it
is usable at all, and offer an upgrade — instead of hardcoding a version it will drift away from, or
burning a turn to discover the backend isn't signed in. All are discovered by pattern-matching over the
registered providers, none runs a completion, and all **fail safe**: an absent, stalled or erroring backend
is reported, never thrown.

```csharp
foreach (var provider in serviceProvider.GetServices<ILlmProvider>())
{
    if (provider is not IProviderInstallation installation) continue;

    var probe = await installation.ProbeAsync(ct);   // NO completion is run: no tokens, no model call
    Console.WriteLine(probe.Available
        ? $"{provider.Id} {probe.Version} {probe.Model ?? "(model unknown until a turn runs)"}"
        : $"{provider.Id} unavailable — {probe.Detail}");

    // the backend's OWN updater, when it ships one — gate it behind a user action, it installs software
    if (probe.Available && provider is IProviderUpdater updater)
    {
        var result = await updater.UpdateAsync(ct);
        Console.WriteLine(result.Updated
            ? $"updated {result.FromVersion} → {result.ToVersion}"
            : result.Detail);                        // "up to date", or why it failed
    }
}
```

`ClaudeCliProvider` implements all four through the same BYO `IProcessRunner` and command seams as a
completion. Two notes on what the probe will and won't tell you:

- **`Version` is exact; `Model` is null against today's claude CLI** — it has no turn-free way to report
  its resolved model, and the probe never guesses one. Read the model actually used from
  `AgentStreamEvent.UsageFinal.Model` after a turn (see the agent-session section). The field is populated
  by backends that *can* answer cheaply — a local runtime naming its loaded weights, a build that labels a
  model on its version line.
- **Nothing is guessed.** The CLI treats an unrecognized token as a *prompt* and spends a turn answering
  it, so every maintenance question is flag-shaped or a documented subcommand. Lyntai drives the tooling the
  backend already ships — it never downloads a backend that isn't there; provisioning stays yours
  (`docs/DECISIONS.md` D26).

Two more capabilities in the same family, discovered the same way:

```csharp
// "Is this backend signed in, and as whom?" — NO completion is run, so no turn is spent finding out
if (provider is IProviderAuth auth)
{
    var status = await auth.StatusAsync(ct);
    if (!status.Authenticated)
    {
        // "not signed in" is a VALUE, not an exception. LoginAsync BLOCKS while the browser flow runs
        // (bounded, cancellable) — show a spinner; you don't need to poll StatusAsync afterwards.
        var result = await auth.LoginAsync(new ProviderLoginRequest(Mode: "console"), ct);
        Console.WriteLine(result.Status?.Authenticated == true
            ? $"signed in as {result.Status.Account}"
            : $"sign-in did not complete — {result.Detail}");
    }
    else Console.WriteLine($"{status.Method}: {status.Account}");   // e.g. "claude.ai: you@example.com"
}

// PIN a known-good backend version, instead of taking whatever `update` gives you
if (provider is IProviderVersionInstaller installer)
{
    var pinned = await installer.InstallAsync(new ProviderInstallRequest("2.1.220"), ct);
    Console.WriteLine($"{pinned.FromVersion} → {pinned.ToVersion}");   // Updated covers a downgrade too
}
```

`Method` and `ProviderLoginRequest.Mode` are free-form strings (`"console"`/`"api"`,
`"claudeai"`/`"subscription"` for the claude CLI) so another backend's account kinds fit without an enum
change — an adapter **refuses** a value it doesn't recognize rather than inventing a flag from it. **Lyntai
never stores credentials**: the backend owns its own, and this seam only asks and drives. Because
`Authenticated: false` covers both "signed out" and "couldn't be asked", call `ProbeAsync` first when you
need to tell those apart.

### CLI backends: `claude`, `codex`, or your own (`CliProviderEngine` + a dialect)

```csharp
services.AddLyntai(cfg => cfg
    .AddClaudeCliProvider()                       // the authenticated `claude` CLI
    .AddCodexCliProvider()                        // the authenticated OpenAI `codex` CLI
    .UseDefaultCandidates("claude-cli", "codex-cli"));   // one falls over to the other
```

Both are the same composition — a shared engine plus a per-CLI dialect — and each advertises only the
capabilities its backend really has. `codex` has no way to install a *named* version of itself, so
`CodexCliProvider` doesn't implement `IProviderVersionInstaller` at all; pattern-matching a capability is
therefore a real answer, not a maybe.

**Portable (non-global) installs.** If your app ships or unpacks its own copy of a CLI, pass the path — and,
where the backend has one, that install's own home directory so it neither reads nor mutates the machine-wide
install's state:

```csharp
cfg.AddCodexCliProvider(
    command: Path.Combine(AppContext.BaseDirectory, "tools", "codex.exe"),
    environment: new Dictionary<string, string> { ["CODEX_HOME"] = portableHome });

cfg.AddClaudeCliProvider(command: bundledClaudePath);   // …and the same value for AddClaudeCliAgentSession
```

`IsAvailable` then checks that the file is actually *there* (including an extensionless launcher rescued by
its `.cmd` sibling), so a missing portable copy makes the router skip that candidate instead of discovering
the absence as a failed turn. The maintenance seams (`ProbeAsync`, auth, update) honour the same command and
environment, so they report the portable install's state rather than a global one's.

**Writing your own.** Every CLI-agent backend needs the same things done right — no shell, a neutral working
directory, prompt over stdin (or as an argument), timeouts as an *inactivity* clock, verdicts from the shared
classifier, empty output as a failure, an in-band `turn failed` event classified rather than swallowed,
exactly one terminal stream chunk, and probe → run → re-probe for self-maintenance. Those live once, in
`CliProviderEngine` (`Lyntai.Llm.Cli`). A new CLI supplies only its own vocabulary:

```csharp
public sealed class MyCliDialect : CliProviderDialectBase
{
    public override string Id => "my-cli";
    public override string DefaultCommand => "mycli";
    public override IReadOnlyList<string> CommandEnvironmentVariables => ["LYNTAI_PROVIDER_CMD", "MYCLI_CMD"];
    public override IReadOnlyList<string> BuildCompletionArgs(LlmRequest r) => ["exec", "--json"];
    public override CliOutputEvent ParseLine(string line) =>   // → Content / Result / Failure / Ignored
        MyWireFormat.Read(line);

    // claim an optional capability ONLY where the real binary has it — the base claims none by default
    public override IReadOnlyList<string>? UpdateArgs => ["update"];
}
```

…plus a provider that forwards to the engine and declares which capability interfaces that backend actually
has (`ClaudeCliProvider` is exactly this, and nothing else):

```csharp
public sealed class MyCliProvider(IProcessRunner runner, LyntaiOptions options) : ILlmProvider, IProviderUpdater
{
    private readonly CliProviderEngine _engine = new(new MyCliDialect(), runner, options);
    public string Id => "my-cli";
    public bool IsAvailable => _engine.IsAvailable;
    public Task<LlmReply> CompleteAsync(LlmRequest r, CancellationToken ct = default) => _engine.CompleteAsync(r, ct);
    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest r, CancellationToken ct = default) => _engine.StreamAsync(r, ct);
    public Task<ProviderUpdateResult> UpdateAsync(CancellationToken ct = default) => _engine.UpdateAsync(ct);
}
```

If your CLI takes the prompt positionally rather than on stdin, set `PromptDelivery = CliPromptDelivery.Argument`
— the engine appends it last. Free-form values (`ProviderLoginRequest.Mode`, `ProviderInstallRequest.Version`)
must be *refused* by the dialect when it doesn't recognize them, never turned into an invented flag.

### Generation: image · video · audio · 3d (`Lyntai.Generation`)

The same idea as the LLM front door, for generated artifacts: you register backends, Lyntai routes across
them. It is a **platform, not an engine** — every pixel and sample is produced by a backend you choose.
`Kind` is an open string, so a medium (or a non-media artifact) nobody has modelled yet uses the same
submit/poll/stream, capability and routing machinery.

```csharp
services.AddLyntai(cfg => cfg
    // hosted: an OpenAI-compatible images API
    .AddOpenAiImageProvider(new OpenAiImageOptions
        { BaseUrl = "https://api.openai.com/v1", ApiKey = key, Model = "gpt-image-1" })
    // local: a Stable Diffusion WebUI on this machine
    .AddAutomatic1111Provider(new Automatic1111Options { BaseUrl = "http://127.0.0.1:7860" })
    .UseDefaultGenerationCandidates("openai-images", "a1111"));
```

Each backend has an `Add*` of its own — `AddOpenAiImageProvider`, `AddAutomatic1111Provider`,
`AddComfyUiProvider`, `AddFalProvider`, `AddLocalDiffusionProvider` — and each takes an **options object**
rather than a configure callback, because these options are records with `required` members: passing the
instance is what keeps `required BaseUrl` compiler-enforced. `AddGenerationProvider(sp => …)` remains the BYO
seam for a backend of your own.

BYO `HttpClient` is optional on every one of them, and Lyntai **never disposes a client you supply** — it is
yours, and it may be carrying a Polly pipeline or an auth handler. Omit it and Lyntai registers a named client
with an *infinite* `HttpClient` timeout, so the per-call deadline owns cancellation rather than the 100-second
default aborting a healthy render. To decorate Lyntai's own client instead of replacing it, reach it by name:

```csharp
services.AddHttpClient(GenerationProviderBuilderExtensions.HttpClientName("fal"))
        .AddHttpMessageHandler<MyLoggingHandler>();
```

Inputs — an init image, a first frame, a style reference, a voice sample — are built with the **named
factories**, never the positional constructor:

```csharp
new GenerationRequest
{
    Kind = GenerationKinds.Image,
    Prompt = "the same room, at night",
    Inputs = [GenerationInput.Init(sourcePng, "image/png")],   // role baked in; it cannot be omitted
}
```

The constructor takes `(MediaType, Data, Uri, Role)` with `Role` **last**, so a plausible positional call
compiles clean, binds the role string to the media type and leaves the role null — and then nothing fails: the
backend gets a well-formed roleless input and your img2img request quietly becomes text-to-image. Use
`Init` / `FirstFrame` / `Reference` / `Voice`, or `From(role, …)` for a role a backend documents itself
(`docs/DECISIONS.md` D35).

Backends declare what they can do, and the router **skips a candidate that can't serve the request** before
spending anything — media backends differ far more than chat models do (medium, input roles, duration
ceilings, model catalogues):

```csharp
var result = await router.GenerateAsync(candidates, new GenerationRequest
{
    Kind = GenerationKinds.Image,                 // open string: image / video / audio / 3d / whatever's next
    Prompt = "a red square on white",
    Options = new Dictionary<string, string> { ["size"] = "1024x1024" },
});
if (result.IsOk) Save(result.Artifacts[0].Data!);
```

**Three delivery modes**, because real backends genuinely differ — and a seam that modelled only one would
force the others to lie:

| Mode | Interface | Typical of |
|---|---|---|
| Inline | `IGenerationProvider.GenerateAsync` | image generation |
| Async job | `IGenerationJobProvider` (submit → poll → fetch) | video, batch music — renders take minutes |
| Streaming | `IGenerationStreamProvider` | text-to-speech, where playback starts before generation ends |

An async render exposes its **operation id**, so it survives a process restart and composes with
`Lyntai.Jobs`; if your backend delivers by webhook, your app owns the endpoint and calls
`FetchAsync(operationId)` when it fires. Chaining is first-class — `artifact.ToInput(role)` feeds one stage's
output into the next (3d → image → video).

Every backend answers **"are you usable?"** without generating anything (`ProbeAsync`), so a setup screen
never has to pay for a test image.

Fallback is a **policy**, not a law. The default matches the LLM router (a content `Refused` surfaces rather
than being re-submitted elsewhere), but if you deliberately pair a hosted backend with a locally-run one, that
is your call to change:

```csharp
cfg.ConfigureGenerationRouting(p =>
    p.On(GenerationVerdict.Refused, GenerationFallbackAction.Advance));   // local backend picks it up
```

Backends come in the same three shapes as LLM providers — **remote** (HTTP), **spawned CLI**, and **local
in-process** — and which one handles a given request is expressed by candidate order, not by a flag.

`Lyntai.Generation` ships five of them (`dotnet add package Lyntai.Generation` — it pulls Core with it). **Measured vs documented matters here** — the two marked
*documented* were written from vendor docs without a key or an engine to call, so treat the first run as the
verification: every endpoint path and field name is an option, and an unrecognised response degrades to a
failure rather than inventing an artifact.


| Backend | Delivery | Notes |
|---|---|---|
| `OpenAiImageProvider` | Inline | `/images/generations`, or `/images/edits` when the request carries an input image. A `url` response comes back as a URI artifact — never downloaded for you |
| `Automatic1111Provider` | Inline | A locally-run SD WebUI: `txt2img` / `img2img`. Not running reports **NotConfigured** (skipped, not blamed), and its probe checks a checkpoint is *loaded* — "up" isn't "usable" |
| `ComfyUiProvider` | **Job** | *Documented, not measured.* Local and workflow-driven: you supply the graph in `Options["workflow"]` (+ optional `Options["prompt-path"]` to place the prompt), and outputs come back as view URIs |
| `LocalDiffusionProvider` | Inline | A local `sd-cli` / stable-diffusion.cpp subprocess through `IProcessRunner` — no key, no network, no content policy in the path. Argv and the multiple-of-64 size clamp are ported from a working implementation rather than measured here |
| `FalQueueProvider` | **Job** | *Documented, not measured.* One aggregator queue reaching the Wan/Kling/Veo-class video models. The operation id **carries its model** (`"model#requestId"`) because a resumed job has only the id, and a transport failure while polling reports **Running, not Failed** — a 500 says nothing about a paid render still in flight |

**Not in scope, by design:** generation itself, downloading engines or model weights, hosting a webhook
endpoint, storing artifacts, or holding your credentials — see `docs/DECISIONS.md` D26 and D30.

### When your users own the backend configuration (`Lyntai.Lifecycle`)

Everything above assumes the *deployment* configures the backends: you call `Add*` once and the container
holds them. If instead an **end user** — or a store your process polls — owns that configuration, the
settings change at any moment, the choice of backend is itself one of those settings, and several
configurations of one backend are live at the same time. Hand the router factory a key and a way to build
each backend, and it does the rest:

```csharp
var cfg = await _settings.ForTenantAsync(tenantId, ct);   // your source; Lyntai never asks where it lives

var openAiKey = ProviderKey.For(cfg.OpenAi.Id)
    .With("baseUrl", cfg.OpenAi.BaseUrl).With("model", cfg.OpenAi.Model)
    .WithSecret("apiKey", cfg.OpenAi.ApiKey)               // hashed into the key, never retained
    .Build();

var localKey = ProviderKey.For(cfg.Local.Id)
    .With("binary", cfg.Local.BinaryPath).With("model", cfg.Local.ModelPath)
    .With("steps", cfg.Local.Steps)
    .Build();

var router = _routers.For([                                // IGenerationRouterFactory, injected
    new(openAiKey, () => new OpenAiImageProvider(cfg.OpenAi, _httpFactory, disposeHttpClient: false)),
    new(localKey,  () => new LocalDiffusionProvider(cfg.Local, _runner)),
]);

var result = await router.GenerateAsync(candidates, request, ct);
```

Name every contribution to the key, and include the values the backend resolves at **runtime** as well as
the ones the user typed — a locally-provisioned engine's binary and model paths appear when a download
finishes, at which point the saved settings have not changed at all and an instance holding empty paths
would keep failing forever.

Whether that rebuilds the backend or reuses it is decided **at startup, not at the call site** — the code
above is byte-for-byte the same under either:

```csharp
services.AddLyntai(b => b.UseProviderPool());        // reuse while the key is unchanged (the default)
services.AddLyntai(b => b.UseTransientProviders());  // a fresh instance every call
```

That is the point of the seam: choosing wrong is a one-line change at startup rather than a rewrite, and
`IProviderPool<TProvider>` is there for a strategy of your own. The same factory exists for chat
(`ILlmRouterFactory`).

What you get either way, and what a hand-rolled per-call cache gets wrong: **dead-host cooldown and
concurrency admission are keyed on the configuration, not on the backend id** — so one tenant's rate limit
never benches another's, while two consumers pointing at the same self-hosted host do share a bench. And a
configuration that changes mid-render never aborts it: a replaced entry is **retired**, not disposed, so
in-flight calls finish normally (`docs/DECISIONS.md` D37).

```csharp
services.AddLyntai(b => b.ConfigureProviderAdmission(a => a.BySlot["sd-local"] = 1));  // one render at a time
```

### Local in-process inference (`Lyntai.Providers.Local`)

Run a GGUF model in-process via LLamaSharp — no network, no key, no subprocess. Reference the
`LLamaSharp.Backend.*` that matches your hardware alongside `Lyntai.Providers.Local`:

```xml
<PackageReference Include="Lyntai.Providers.Local" />                  <!-- version: the current release -->
<PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.27.0" />  <!-- or .Cuda12 / .Vulkan / .Metal -->
```

```csharp
services.AddLyntai(cfg =>
{
    cfg.AddLocalProvider("models/Phi-3-mini-4k-instruct-q4.gguf", o =>
    {
        o.GpuLayerCount = 0;      // 0 = CPU; raise to offload layers to the GPU
        o.ContextSize = 4096;     // null = the model's own trained maximum
    });
    cfg.UseDefaultCandidates("local");
});
```

The model loads lazily on first use and generations are serialized (one local model, one at a time).
It's just another `ILlmProvider`, so it fits anywhere in a fallback candidate list — e.g. a hosted
model first, `"local"` as an offline backstop.

### Tool-calling (`Lyntai.Agents`)

Give the model tools and let it work in a loop. `IToolLoop` runs over the `ILlmClient` front door, so
it works with **any** provider (CLI, HTTP, MEAI bridge, local) — no native tool-calling required.

```csharp
services.AddLyntai(cfg =>
{
    cfg.AddClaudeCliProvider().UseDefaultCandidates("claude-cli");

    // a tool from a class (DI-injectable) or inline from a delegate:
    cfg.AddTool(_ => new FunctionTool(
        name: "get_weather",
        invoke: (argsJson, ct) => Task.FromResult("""{"tempC":21,"sky":"clear"}"""),
        description: "Current weather for a city",
        parametersJsonSchema: """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"""));
});

// inject IToolLoop:
var result = await toolLoop.RunAsync(new LlmRequest
{
    Messages = [LlmMessage.User("What should I wear in Paris today?")],
});
Console.WriteLine(result.Answer);          // the model's final answer after any tool round-trips
foreach (var step in result.Steps)         // every tool call it made, for tracing
    Console.WriteLine($"{step.Tool}({step.ArgumentsJson}) -> {step.Result}");
```

The loop executes the tool the model chooses, feeds the result back, and repeats up to
`ToolLoopMaxIterations` (default 8). It uses **native** provider function-calling when available
(OpenAI-compatible / Ollama and any `Microsoft.Extensions.AI` `IChatClient` via the bridge — structured
`tool_calls`, parallel calls supported) and falls back to a **prompt protocol** over the text contract
for providers without it (CLI, basic local models) — same `ITool`s either way, chosen transparently
behind the front door (`ILlmClient.SupportsToolCalls`). An
unknown or throwing tool becomes a recoverable `error: …` observation rather than a crash; a refusal or
all-providers-down verdict surfaces on `result.Verdict`.

**MCP tools** (`Lyntai.Tools.Mcp`) — point the loop at a Model Context Protocol server and its tools
become `ITool`s. Your app owns the MCP connection; Lyntai adapts:

```csharp
await using var mcp = await McpClient.CreateAsync(new StdioClientTransport(new()
{
    Command = "npx", Arguments = ["-y", "@modelcontextprotocol/server-everything"], Name = "everything",
}));
var mcpTools = await McpToolset.FromClientAsync(mcp);   // list + adapt the server's tools
services.AddLyntai(b => b.AddClaudeCliProvider().AddMcpTools(mcpTools).UseDefaultCandidates("claude-cli"));
```

**Hosting your tools for a CLI agent** (`Lyntai.Tools.Mcp.Hosting`) — the reverse direction. A CLI that
runs its own agent loop reaches custom tools only over MCP, so this package hosts your registered
`ITool`s as an ephemeral, localhost-only HTTP MCP server (started/stopped per CLI call) and passes the
CLI whatever flags point it there. Opt in and a completion routed to that CLI lets its agent call your
tools:

```csharp
services.AddLyntai(b => b
    .AddClaudeCliProvider()
    .AddTool(_ => new FunctionTool("get_weather", (a, ct) => Task.FromResult("""{"tempC":21}"""), "Current weather"))
    .AddMcpToolHost(new ClaudeCliMcpDialect())   // hosts the tools over MCP for the CLI
    .UseDefaultCandidates("claude-cli"));
// var reply = await llm.CompleteAsync(...);  → the CLI calls get_weather and answers
```

The host is provider-neutral: **which** CLI connects and **how** it's told to is the `IMcpCliDialect` —
flag names plus config-file shapes, and nothing else. `ClaudeCliMcpDialect` ships with the claude provider
package (it costs that package no extra dependencies; the Kestrel host stays here, so apps using the plain
CLI provider stay ASP.NET-free and AOT-compatible). Supporting a different CLI is one small class, no new
package and no change to the host:

```csharp
public sealed class MyCliMcpDialect : IMcpCliDialect
{
    public string ProviderId => "my-cli";

    public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext ctx, CancellationToken ct = default)
    {
        // write whatever config file the CLI reads (JSON, TOML, …) — the host deletes it for you
        var path = ctx.WriteTempFile("mcp", $$"""{"servers":{"{{ctx.Endpoint.ServerName}}":{"url":"{{ctx.Endpoint.Url}}"}}}""");
        return ValueTask.FromResult<IReadOnlyList<string>>(["--mcp-config", path]);
    }
}

services.AddLyntai(b => b.AddMcpToolHost(new MyCliMcpDialect()));
```

The provisioner is registered keyed on `ProviderId`, so several CLI providers can host tools side by side
with different dialects. (This runs an ephemeral Kestrel listener on loopback only during each CLI call —
a deliberate, scoped exception to Lyntai's otherwise host-free design, isolated in this opt-in package.)

### CLI-agent session vs `IToolLoop` (`IAgentSession`)

When the external agent drives its OWN tool loop out-of-process (e.g. the `claude` CLI running
autonomously), `IAgentSession` is the right primitive — not `IToolLoop`. You observe a streamed
transcript of what the agent did (`AgentStreamEvent`), gate it read-only (plan) vs write (execute) via
`AgentToolPolicy`, and resume it across a human confirmation gate using the session's `ResumeToken`.
Two consumption doors: `StreamAsync` (live event-by-event, for progress UI or structured logging) and
`RunAsync(onEvent)` (fold to a result for callers that only need the outcome).

The `IAgentSession` interface is neutral Core (`Lyntai.Agents`); all claude-specific flags
(`--settings`, `--mcp-config`, `AllowedTools`) live in the `Lyntai.Providers.Default` package (namespace
`Lyntai.Providers.ClaudeCli`)
(`ClaudeAgentSession` / `ClaudeAgentOptions`, registered via `AddClaudeCliAgentSession()`).

```csharp
services.AddLyntai(b => b
    .AddClaudeCliProvider()
    .AddClaudeCliAgentSession()          // registers IAgentSession → ClaudeAgentSession
    .UseDefaultCandidates("claude-cli"));

var session = sp.GetRequiredService<IAgentSession>();

// Session 1 — read-only PLAN gate, streaming door (observe live tool calls):
string? resumeToken = null;
await foreach (var e in session.StreamAsync(new ClaudeAgentOptions
    { Prompt = "Plan the refactor.", ToolPolicy = AgentToolPolicy.ReadOnly, WorkingDirectory = cwd }))
{
    if (e is SessionStarted s) resumeToken = s.SessionId;
    else if (e is ToolCall tc) Console.WriteLine($"tool: {tc.Name} → {ClaudeToolCalls.FilePathOf(tc)}");
    else if (e is SessionEnded se) Console.WriteLine($"plan verdict: {se.Verdict}");
}

// Human review / approval gate here …

// Session 2 — WRITE execute gate, resumed from session 1, result door:
var result = await session.RunAsync(new ClaudeAgentOptions
    { Prompt = "Apply the refactor.", ToolPolicy = AgentToolPolicy.Write, ResumeToken = resumeToken,
      WorkingDirectory = cwd });
Console.WriteLine($"done: {result.Verdict} — {result.FinalText}");
```

**`IToolLoop`** (the other shape) — Lyntai drives the ReAct loop in-process over registered `ITool`s.
Choose `IToolLoop` when you supply the tools and want Lyntai to call them; choose `IAgentSession` when
the external agent drives its own loop and you want to observe, gate, and resume it.

### Durable jobs (`Lyntai.Jobs`)

Run long, multi-step work (e.g. many agents) that survives restarts, with lanes for concurrency control.
Enqueue a job, a runner claims and runs it, your handler checkpoints — and a job whose worker crashed is
reclaimed and **resumed from its checkpoint**. Your app owns the pump (no background threads are started
for you):

```csharp
sealed class SummarizeHandler : IJobHandler
{
    public string Type => "summarize";
    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct)
    {
        if (ctx.Checkpoint is null) { /* step 1 … */ await ctx.SaveCheckpointAsync("fetched", ct); }
        /* step 2 (skipped-ahead on resume) … */
        return JobOutcome.Complete;   // or JobOutcome.Retry(delay) / JobOutcome.Fail(reason)
    }
}

services.AddLyntai(cfg => cfg
    .UseSqliteStorage("jobs.db")                 // durable — Postgres/InMemory also supported
    .AddJobHandler<SummarizeHandler>()
    .Configure(o => { o.Jobs.LaneConcurrency["summarize"] = 4; o.Jobs.MaxConcurrency = 8; }));

await queue.EnqueueAsync("summarize", "summarize", payloadJson);
await runner.RunAsync(ct);   // in your IHostedService — claims across lanes and runs them in parallel
```

Per-lane limits + a global `MaxConcurrency` cap are the control knobs; run several `IJobRunner` instances
(one process or many) and the atomic claim gives each job to exactly one. At-least-once semantics —
handlers must be idempotent from their checkpoint.

**Priorities + dead-letter queue.** Enqueue with a priority (higher runs first within a lane), and a job
that exhausts its retries lands in the dead-letter queue (`JobStatus.Dead`) — inspectable and replayable
rather than a silent failure:

```csharp
await queue.EnqueueAsync("summarize", "summarize", payloadJson, priority: 10); // jumps the lane
foreach (var dead in await queue.ListDeadAsync())    // inspect what gave up
    await queue.ReplayAsync(dead.Id);                // requeue it (attempts reset)
await queue.CancelAsync(jobId);   // cancels a Pending job; requests cancellation of a Running one
```

`CancelAsync` on a running job is cooperative — the runner cancels the handler's `CancellationToken`, so a
handler that honors it stops (and the job becomes `Cancelled`).

**Recurring schedules.** Register an interval schedule and `IJobScheduler` enqueues the job every interval;
the next-run time is persisted (in the key-value store) so the cadence survives restarts. The app owns the
scheduler pump too:

```csharp
cfg.AddJobSchedule("nightly-report", lane: "reports", type: "report", payload: "{}", every: TimeSpan.FromHours(24));
cfg.AddCronSchedule("weekday-9am", lane: "reports", type: "report", payload: "{}", cron: "0 9 * * 1-5"); // or a cron (UTC)
await scheduler.RunAsync(ct);   // in your IHostedService, alongside runner.RunAsync
```

### Guards, orchestration, secrets, vision

- **Guards** (`Lyntai.Guards`) — `IGuard`s inspect requests/replies and Allow/Block/Replace; `AddGuard<T>()`
  registers them, `GuardedLlmClient` gates any completion, and the chat orchestrator applies them as gates.
- **Two-gate chat** (`IChatOrchestrator`) — one call runs: input gate → memory recall → model (via the
  tool loop) → output gate → remember. A batteries-included, guarded chat entry point.
- **Secret vault** (`Lyntai.Secrets`) — `AddSecretVault(key)` gives an `ISecretVault` encrypted at rest
  (AES-256-GCM, your key), persistent over your storage backend, with an optional read access policy.
  Prefer no key to manage? `AddEnvelopeSecretVault(machineProtector)` (Core) uses a Lyntai-generated key
  sealed to the host *and* backed by a one-time recovery key for off-machine recovery; on Windows,
  `AddDpapiSecretVault()` (`Lyntai.Secrets.Dpapi`) binds it with DPAPI. Call `GenerateMasterKeyAsync()`
  once (record the recovery key), `RecoverAsync(key)` on migration.
- **Vision** — `LlmMessage.UserWithImage(text, bytes, "image/png")` (or `UserWithImageUrl`); the
  OpenAI-compatible and MEAI-bridged providers send it as image content.

## Dev loop

```
node devtools/dev.mjs build            # build the solution
node devtools/dev.mjs test             # xUnit tests (unit + integration, zero real tokens)
node devtools/dev.mjs e2e --build      # Playground full-stack smoke against the provider-stub
node devtools/dev.mjs playground       # run the sample console app yourself
node devtools/dev.mjs pack             # dotnet pack → publish/packages/
node devtools/dev.mjs install-hooks    # enable the pre-commit sensitive-info guard
```

See `.claude/rules/dev-conventions.md` for the load-bearing patterns.

## License

[MIT](LICENSE) © Jiarong Gu — the same `MIT` SPDX expression every NuGet package carries.
