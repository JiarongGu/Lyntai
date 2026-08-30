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
**v3.1.0 — a hardened, batteries-included cortex substrate, now with a media generation platform.**
Twelve packages, one public front door, and a public API frozen under SemVer 2.0 since 1.0.

What is in it, by domain: **LLM** — routing with streaming-aware fallback across CLI / HTTP / MEAI-bridged
backends, a configurable per-verdict `RoutingPolicy`, dead-host cooldown, native + prompt tool-calling.
**Generation** — one capability-aware seam for image/video/audio/3d with three delivery modes (inline,
submit→poll→fetch, streaming — all three routed, governed and throttled alike), durable renders over
`Lyntai.Jobs`, and five backends.
**Storage** — SQLite / Postgres / InMemory, mixable per domain, with FTS5-trigram recall and feature toggles.
**Agents** — a tool loop, two-gate chat orchestration, guards, and both halves of MCP. **Ops** — prompt
registry, scoring/eval, run traces, task-scoped + semantic + curated memory, **named memory engines** over a
decaying, self-linking graph memory, durable jobs with priorities / DLQ / cron / cancellation, a secret
vault, OTel across all three domains, and front-door governance (cache, budget, rate limit).

Since 1.0 froze the API (2026-07-28): **1.1** generic CLI tool-hosting · **1.2** turn-free backend probe,
auth and pinned self-install · **2.0.1** the generation platform plus a coherent package graph — one rule for
package boundaries, a starting bundle, and four build gates that keep the packaging claims honest ·
**2.1.0** generation ergonomics — named input factories, an `Add*` per media backend, and BYO-`HttpClient`
ownership brought in line with the LLM side · **2.2.0** the provider-lifetime pool for keys owned outside the
deployment, plus a second agent-session backend · **2.3.0** the pre-release whole-library review, with two
documented compile-time breaks · **2.4.0** an agent session that can be given the host's own MCP servers on
either CLI backend · **2.5.0** long-term memory · **3.0.0** the memory retention model, streaming
generation through the router, and the cross-process job cap.

**2.5.0 — long-term memory.** Several memory systems coexist in one app and resolve by name the way
`IHttpClientFactory` resolves clients; entries decay, link to what they were recalled with, and open as a
cheap index you pay to expand. Decay is measured in what has happened in a memory rather than in elapsed
time, and a decayed entry is buried rather than deleted. Purely additive — the three existing memory
surfaces are unchanged, and an app that never calls `AddMemory`/`AddMemoryEngine` sees no difference.

**3.0.0 — the memory retention model, and the platform work around it.** The 2.5 memory subsystem reshaped:
one `IMemory<Domain>Policy` naming shape for every policy seam, FSRS's power-law forgetting curve as the
only shipped default (difficulty axis live, reviews logged for later fitting), rank fusion as the
registered ranking default, model-in-the-loop annotation and verification seams, and authoritative facts
that survive any recall limit. Around it: streaming generation reachable through the router, tool calls on
the LLM streaming contract, a cross-process job concurrency cap, and the withdrawal of the generation
SemVer exemption, so every package carries the full promise. **Breaking, deliberately and with a path** —
a 2.5 consumer starts at `docs/migration-2.5-to-3.0.md`, the ordered upgrade with a worked before/after;
stored data needs nothing (schema changes run automatically).
`CHANGELOG.md` has the per-release detail; `docs/DECISIONS.md` has the reasoning behind the load-bearing calls.
**This file documents the working tree, not only the newest package**: anything that has not shipped yet is
listed under `## Unreleased` in `CHANGELOG.md`, so check there before assuming a member below is in the
version you installed.

> **Versioning.** From **1.0**, Lyntai follows **SemVer 2.0**: no breaking public-API change without a major
> bump (the `ApiSurfaceTests` baseline gates it).
>
> **Every package carries that promise. There is no carve-out** — `Lyntai.Generation` had one from 2.0.1 and
> it was withdrawn in 3.0 (`docs/DECISIONS.md` **D70**). It named three reasons and each is closed: the two
> backends written from vendor documentation now expose every mapping they could have got wrong as a host
> option, so a mismatch is a configuration edit rather than a release (**D69**); the same is true of the
> third's ported argv; and `IGenerationStreamProvider` is reachable through the router (**D67**). What a real
> run can still surprise is a wire format's SHAPE, not a value — and that is now a major-version risk taken
> deliberately rather than a caveat carried indefinitely.
> **The carve-out is the PACKAGE, not the `Lyntai.Generation` NAMESPACE:** the generation *contracts* in that
> namespace (`GenerationResult`, the routing policy, `GenerationVerdictClassifier`, …) ship inside the mandatory
> `Lyntai.Core` and carry the FULL promise — which is why `docs/DECISIONS.md` D36 treated a verdict-translation
> fix in one of them as major-bump material rather than claiming the exemption. Everything
> else (LLM routing, storage, cortex, jobs, guards, secrets, memory, tools) carries the full promise. **Upgrading 0.31 → 1.0:** the 0.x migrations were collapsed
> into per-domain 1.0 baselines — the net schema is identical but the migration ledger is renumbered, so
> **drop your `lyntai_*` tables (including `lyntai_version_info`) or delete the dev database before the first
> 1.0 run**; Lyntai recreates them. One-time; the ledger is append-only thereafter.
>
> **Upgrading 2.5 → 3.0:** see `docs/migration-2.5-to-3.0.md` for the ordered path — every breaking change
> in dependency order, a worked before/after, and why no stored data needs a migration at all.

- `docs/2026-07-17-lyntai-design.md` — the design contract (interfaces, fork decisions, semantics, scope).
- `docs/migration-2.5-to-3.0.md` — the 2.5 → 3.0 upgrade path: what's automatic (schema), what's manual
  (everything else), in the order the fixes depend on.
- `docs/ROADMAP.md` — what's shipped, what's next (generation verification and the open design calls), and
  the standing maintenance policies.
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
| `Lyntai.Generation` | **Experimental.** The media backend set — OpenAI images, Automatic1111, ComfyUI, a local `sd-cli` subprocess, and the fal.ai queue for video, each with an `Add*` of its own. Adds only `Microsoft.Extensions.Http` (its shims register named clients); the generation *contracts* are in Core. Split out so media can iterate without churning the LLM packages (D25). |

Packages are split by **dependency footprint**, never by vendor or by size: every boundary answers "which
dependency does this isolate?" with something concrete. Backends that need nothing extra share
`Providers.Default`; anything dragging a native runtime (LLamaSharp, native SQLite), a platform-specific API
(Windows DPAPI) or a protocol stack of its own (`ModelContextProtocol.Core`, via either MCP package) stays
its own package — and `Lyntai.Core` carries the smallest footprint of all, because it is the one package you
cannot opt out of (`docs/DECISIONS.md` D25).

What goes **in the bundle** is a separate, budgeted decision (D26): a package joins only if it adds no
third-party dependency beyond the `Microsoft.Extensions.*` band, or if it is near-universal and the cost is
accepted explicitly — never if it carries a native payload or a platform-specific API. `node devtools/dev.mjs
check-bundle` fails the build if that closure ever drifts, so the one-line install can't quietly grow heavy.

## Consuming Lyntai

```bash
dotnet add package Lyntai                  # the recommended STARTING set — 6 of the 12 packages
dotnet add package Lyntai.Storage.Sqlite   # persistence (the bundle's storage is IN-MEMORY)
dotnet add package Lyntai.Generation       # image/video/audio backends
```

**`Lyntai` is a starting set, not the whole library.** It gives you Core, the LLM backends, the MEAI bridge,
both halves of MCP, and **in-memory** storage. The two that surprise people: nothing persists until you add
`Lyntai.Storage.Sqlite` (or `.Postgres`), and generation is not included. The five packages left out are left
out for a reason — a native payload (`Storage.Sqlite`, `Providers.Local`), a platform-specific API
(`Secrets.Dpapi`), a server dependency (`Storage.Postgres`), or an unverified surface (`Lyntai.Generation`) —
see `docs/DECISIONS.md` D26.

**Convenience vs size.** `Lyntai` is a bundle with no code of its own — it just pulls a curated set. A
framework-dependent `dotnet publish` copies the **whole** dependency graph and analyses nothing, so that lands
~3.2 MB of assemblies in your output folder whether or not you call into them — most of it the MCP SDK (1.9 MB
including the `Microsoft.Extensions.AI.Abstractions` it pins). Two levers, either of which removes it:

- **Reference only the packages you use** instead of the bundle. `Lyntai.Core` + one provider is the lean path,
  and the boundaries exist precisely so this is possible (`docs/DECISIONS.md` D25).
- **`PublishTrimmed=true`** (needs a self-contained publish). Unused assemblies are dropped outright *and* the
  used ones are trimmed internally. Measured on a router-only app, Lyntai's whole footprint goes **3.2 MB →
  0.21 MB** (`Lyntai.Core` alone 528 KB → 40 KB), because every compatible package carries honest `IsTrimmable`
  metadata — see [`docs/AOT.md`](docs/AOT.md).

Either way an unused dependency costs **nothing at runtime**: assemblies load on first type reference, so one you
never touch is never opened.

Then compose in DI:

<!-- compile-given: IChatClient myChatClient; -->
```csharp
using Lyntai;                       // the builder + Add*/Use* extensions
using Lyntai.Cortex.Scorers;
using Microsoft.Extensions.DependencyInjection;

services.AddLyntai(cfg =>
{
    cfg.AddClaudeCliProvider();                          // spawns the authenticated `claude` CLI, no API key
    cfg.AddOpenAiCompatibleProvider("ollama", o => o.BaseUrl = "http://localhost:11434");
    cfg.AddExtensionsAiProvider("openai", myChatClient); // bridge any Microsoft.Extensions.AI IChatClient
    cfg.UseSqliteStorage("app.db");                      // all storage domains, migrated on startup
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
        return reply.Verdict.IsOk() ? reply.Text : throw new InvalidOperationException(reply.Detail);
    }
}
```

(`ILlmRouter` stays available for call sites that genuinely need their own candidate list.)

`LlmVerdict` also carries two call-site predicates — `IsOk()` and `IsTransient()` ("may the same request
succeed later?", true for `Failed`/`Timeout`/`RateLimited`). They are categories rather than one method per
verdict, on purpose: the enum grows, and a single member is already best expressed as
`verdict == LlmVerdict.RateLimited`. They hang off the enum, so they read the same off `LlmReply`,
`LlmChunk`, `SessionEnded`, `AgentSessionResult` and `ToolLoopResult`.

And if your app already speaks `Microsoft.Extensions.AI`, consume Lyntai **as** an `IChatClient` —
routing, fallback, and the ops layer come along silently:

```csharp
IChatClient chat = serviceProvider.GetRequiredService<ILlmClient>().AsChatClient();
```

`IChatClient` has no verdict — it returns a response or throws — so the bridge throws
**`LlmVerdictException`** (deriving from `InvalidOperationException`) carrying the `Verdict` that caused it.
That matters for one verdict in particular: `NotConfigured` means *never set up*, so a host can offer setup
instead of reporting an error, and through this bridge that would otherwise be recoverable only by parsing
the message text.

<!-- compile-given: List<Microsoft.Extensions.AI.ChatMessage> messages;
     void ShowSetup(string? detail) { } -->
```csharp
try { var response = await chat.GetResponseAsync(messages, cancellationToken: ct); }
catch (LlmVerdictException ex) when (ex.Verdict == LlmVerdict.NotConfigured) { ShowSetup(ex.Detail); }
```

### The semantics you're getting (design §6)

- **Fallback router:** candidates are deduped and tried in order; `Failed`/`Timeout` advances,
  `RateLimited` puts that host on immediate cooldown and advances to the next candidate (a 429 is
  terminal for the host's window, not for the fleet), `Refused` surfaces with no fallback (content
  policy follows the prompt, not the host).
- **A backend you listed but never configured is skipped, not benched** — when a server answers 401/403 to a
  call that carried no credentials, the verdict is `NotConfigured`, and the router advances with no cooldown
  and no dead-host penalty (`AuthFailed` — a key that WAS supplied and got rejected — still cools the host).
  It isn't "a key is required": a locally-run OpenAI-compatible server legitimately needs none, so only the
  server actually demanding one makes a missing key a configuration gap. Same rule as the generation router.
  A blameless verdict never *masks* a real one either — if one candidate is down and the next is merely
  unconfigured, you're told about the outage, not sent to check a key.
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
- **Memory recall is bounded and fail-open:** FTS5 trigram match, LIKE fallback, capped per (task, scope) —
  and it never throws into your prompt path.
- **Recall is not English-only, and needs no setup to not be.** A query is split into terms the same way on
  every backend: words for a space-separated script, character **trigrams** for one written without spaces
  (Chinese, Japanese, Korean). An entry containing any term is recalled, so `"我的配偶叫什么名字"` finds
  `"我的配偶是爱丽丝"` — no segmenter, no per-language switch, no configuration. Backends still differ in how
  they RANK matches (bm25 on SQLite, matched-term count then recency elsewhere), never in which they find.
- **Memory can learn what a fact is ABOUT** (opt-in). `AddMemoryAnnotation()` labels each written fact with
  its subjects and links entries that share one, so a cue about a person reaches facts that never named
  them — `"my spouse"` finds "she works as an anaesthetist". Nothing lexical can do that: those sentences
  share only pronouns, in any language. Costs one model call per write; point it at a cheap backend with
  `AddLlmClient("memory-fast", …)` and `ClientName`. Absent, memory behaves exactly as it always has.
  <br>Those subjects are **searchable, not just linkable**: a recall matches its query against the handles in
  use and seeds the entries recorded under whichever ones it names, so `"配偶"` reaches the fact whose text
  says `"太太"`. On by default (`GraphMemoryOptions.SubjectSeedK`) once an annotator is wired — a handle
  exists only because you paid for it, so reading it back needs no second opt-in.
- **A model can judge which recalled entries actually ANSWERED the query** (opt-in, and the largest
  recall-quality lever here). `AddMemoryVerification()` shows a judge the query and the candidate headlines
  before the limit is applied, so an answer the ranking buried gets promoted — and reinforcement then follows
  evidence instead of the ranker's own guesses.
  <br>It exists because of a measurement: of the relevant entries a recall failed to return, **100% were
  reachable candidates ranked below the limit** and none were unreachable, and the two shipped model-free
  ranking policies return byte-identical results — so there was no fix inside the library's own arithmetic.
  Measured effect: miss `0.5357 → 0.2571` with `gemma3:4b`, `→ 0.1857` with Claude Haiku; pollution as low as
  `0.0492`. Which judge wins depends on which failure you pay for — the small local model returns the least
  junk, the hosted one finds the most answers.
  <br>Costs one model call per recall, in the latency path of an answer (~1.5 s locally), so point it at a
  small fast backend with `ClientName`. **Avoid a *thinking* model** — one spent ~25 s per judgement against
  another's ~1.5 s; `LlmRequest.Reasoning` asks a backend to skip it where the backend can. A failure leaves
  the ranking untouched, and a judgement never removes a result unless you set `VerificationFilters`.
  Verified to work in English, Chinese, Japanese and Korean.
- **Name an LLM client per use.** `AddLlmClient("memory-fast", c => c.UseProviders("ollama", "openai"))`,
  resolved through `ILlmClientFactory` — the counterpart of named memory engines. A name selects backends,
  never permissions: every named client carries the same cache, budget, rate limit and refusal screening as
  the default one. The ids you name are also its **fallback order**, so a client narrowed onto a local
  backend routes there whatever `UseDefaultCandidates` says globally; `UseCandidates(…)` states the list
  outright when you need two models of one backend.
- **The memory store is bounded out of the box:** the default `MemoryEvictionPolicy` keeps a **500-entry
  per-scope FIFO cap**, so the store does not grow without limit unless you ask it to. Change it with
  `ConfigureMemory(p => …)` — count cap, default TTL, per-scope character budget, FIFO or LRU eviction — or
  the `LYNTAI_MEMORY_*` env family; `MemoryEvictionPolicy.Manual` hands size back to your app. On-write
  eviction never revisits a cold `(task, scope)`, so `AddMemoryPruneJob(cron, …)` is the scheduled form —
  a recurring durable job that removes expired and aged-out entries (your app owns the pump).
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
  `LYNTAI_MEMORY_MAX_ENTRIES`/`_EVICTION` (`Fifo`|`Lru`)/`_TTL_SECONDS`/`_MAX_CHARS`,
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
    .AddOpenAiProvider(apiKey: "…")
    .AddResponseCache(c => c.Ttl = TimeSpan.FromHours(6))); // defaults: 1h TTL, 1000 entries
```

Keyed by a stable hash of the output-determining request fields (messages, model, max tokens, temperature,
JSON schema) — `Consumer` is excluded, so two consumers issuing the same request share a hit. Only clean
`Ok`, non-streaming completions are cached; **streaming**, requests carrying **native tools** (the tool
loop is stateful), and **non-Ok** replies never are. The in-memory cache is the default; call `UseSqliteResponseCache()` (or `UsePostgresResponseCache()`) to
persist it so it survives restarts, or register your own `IResponseCache` before `AddResponseCache` to back
it with Redis or another shared store. (Persisting it needs `StorageFeature.Governance` — see the
governance-persistence note at the end of **Rate limiting**.)

### Named memory engines

`IMemoryStore`, `ISemanticMemory` and `ICuratedMemoryStore` are each a single unnamed service, so an app
wanting a *chat* memory **and** a *project* memory used to have to wrap all of it. A memory **engine** is a
named memory system; several coexist and are resolved by name, the way `IHttpClientFactory` resolves
clients.

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("app.db")
    .AddMemory());        // one working engine named "default", wired into ChatOrchestrator's prompt
```

That is the whole of the common case. For more than one, or for a blend, declare members:

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("app.db")
    .AddMemoryEngine("chat",    e => e.UseLexical().UseSemantic().FanOutWrites().Budget(1500))
    .AddMemoryEngine("project", e => e
        .UseCurated("glossary").ReserveCharacters(1200)   // authoritative — exact, never decays
        .UseLexical()                           // associative — recalled context
        .Budget(3000))
    .UseMemoryComposer("chat"));                // which engine backs the chat prompt

var memory = sp.GetRequiredService<IMemoryEngineFactory>();
await memory.Get("project").RememberAsync(new MemoryWrite("proj", "code", "prefers terse commits"));
var recall = await memory.Get("project").RecallAsync(new MemoryQuery("proj", "code", "commits"));
// recall.Ran says which tiers actually ran, so an empty tier differs from an absent one
```

A blend **is** an engine, so members are addressable too (`Get("project/glossary")`) and adding a fourth
kind of memory is a class plus a registration rather than an edit to anything existing.

**`FanOutWrites()` is there because a write goes to ONE member by default** — the first that can hold its
grade. That is right for `project` above (curated takes the exact facts, lexical takes the rest) and wrong
for `chat`, where both members hold associative material and index it differently: without it the lexical
member takes every write and the semantic store stays empty, silently. Two members that share a grade is the
signal to reach for it; the cost is one write per member. A member no write can reach is reported at startup
(`MemoryEngineBuilder.StrictWiring()` makes that a failure rather than a log line).

**Grades are what keep it accurate.** Curated members are *authoritative*: their content never decays, is
never shortened, holds a **reserved** slice of the character budget ahead of associative material, and
renders in its own labelled section — so a burst of loosely-relevant recall cannot quietly push a hard
constraint out of the prompt:

```
## Known facts (authoritative)
- the build gate is `node devtools/dev.mjs verify`

## Recalled context (associative — may be stale or partial)
- user prefers terse commit messages
```

Authoritative material that genuinely will not fit says so (`… 2 further authoritative facts omitted
(budget)`) rather than vanishing, and an authoritative write goes to an engine that can hold one or throws
— it is never silently stored as associative. A duplicate engine name fails at configure time, and naming a
member whose store is not registered fails at startup rather than composing an empty section forever.

Nothing changes for an app that does not call `AddMemory`/`AddMemoryEngine`: the existing stores and
composer behave exactly as before.

### Graph memory — forgetting, relinking, and a cheap first load

`UseGraph()` is a memory engine shaped more like recall than like a log. Entries **decay** unless they get
used, **connect** to whatever was recalled beside them, and come back as one-line **headlines** that expand
on demand — so a session opens on a small index instead of paying for the whole store on every turn.

```csharp
services.AddLyntai(cfg => cfg
    .UseInMemoryStorage()
    .AddMemoryEngine("project", e => e.UseGraph()));

var memory = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
await memory.RememberAsync(new MemoryWrite("proj", "code",
    "The build gate is node devtools/dev.mjs verify, which runs sixteen checks."));

var recall = await memory.RecallAsync(new MemoryQuery("proj", "code", "gate"));
// headlines only — Content is null until you ask for it
foreach (var hit in recall.Items)
    Console.WriteLine($"{hit.Headline}  →{hit.Degree}  r={hit.Retrievability:F2}");

var detail = await ((IExpandableMemory)memory).ExpandAsync(recall.Items[0].Reference);
// full content of that entry, plus its neighbours' headlines
```

A **walk** is that pair repeated — recall, then expand what looked worth it, then expand whatever *that*
turned up. It is the mode this engine is built for, since a recall deliberately returns headlines and detail
is bought per entry. Your `break` is the stop condition, because how far it is worth going is a property of
the question rather than a constant:

```csharp
await foreach (var step in engine.WalkAsync(new MemoryQuery("proj", "code", "gate", Limit: 20)))
{
    Console.WriteLine($"step {step.Number}: {step.NewItems.Count} new, {step.UpgradedCount} upgraded");
    if (step.Items.Count >= 30) break;   // you decide how far to go
}
```

`step.Items` is everything held so far, merged: an entry already held as a headline is **upgraded** in place
when a later step expands it, which is the whole payload of expanding something you already have. The walk
composes `RecallAsync` and `ExpandAsync` and adds nothing to either, so an engine that cannot expand yields
exactly one step rather than failing — and the sequence is finite whether or not you break.

**How forgetting works — and it is not the clock.** Decay is measured in *what has happened in that
memory*, not in elapsed time. Each engine keeps a position that advances when something is written to it,
and an entry's age is how far that position has moved since the entry was last used. So **a memory nobody
touches keeps everything, while a busy one lets old material fall behind** — which is the behaviour you
want and the one wall-clock time gets backwards. Reading never ages anything: a recall *refreshes* what it
returned, so material you keep coming back to stays reachable while one-off noise falls behind. It is all
computed at read time — no sweeper, no background job.

**What makes an entry durable is a deliberate design choice, and it is not how often we returned it.** A
recall resets an entry's age — a boost that expires, because the entry then decays again at its own rate.
Lasting durability comes instead from two properties of the material itself: how **novel** it was when
written (salience) and how **connected** it is in the graph. Both measured as improving recall quality
*through retention* — how slowly an entry decays. Salience deliberately does NOT vote on ranking, where it
measured as making recall worse (**D89**); "durable" and "ranked higher" are separate promises here, and
only the first is salience's. Raising an entry's half-life every time the ranker happened to return it
measured as making recall *worse* too, on every corpus shape, because it banks the ranker's own mistakes
permanently. So
`DsrOptions.ReinforceGain` ships at `0` — FSRS's three stability-increase laws are implemented and correct,
and one line switches them back on (`docs/DECISIONS.md` D54).

**Decay buries; it does not cut.** An entry is hidden because something *outranks* it, never because it
crossed a threshold — so a faint memory alone in a quiet engine is still the best thing there and comes
back, while the same memory under fifty fresher ones does not. Either way it stays traceable: ask for it
specifically, or reach it through a neighbour, and it is there, with its `Retrievability` telling you how
faint it has become. Nothing is deleted unless you call `PruneAsync`.

**Where these types live.** The retention seams and their implementations sit in per-domain namespaces —
the age policies below in `Lyntai.Memory.Interference`, the curve and its options in `Lyntai.Memory.Forgetting`,
retention policies (`IMemoryRetentionPolicy`, which lengthens a half-life — unrelated to the
`Lyntai.Storage.MemoryEvictionPolicy` size cap above, which *removes* entries) in
`Lyntai.Memory.Modulation`, salience policies in `Lyntai.Memory.Salience`. They were flat under
`Lyntai.Memory` up to 2.5.

**Upgrading from 2.5 is more than an added `using`, and there is one ordered path through it:
[`docs/migration-2.5-to-3.0.md`](docs/migration-2.5-to-3.0.md).** The namespace move is the easy half — the
seams were also renamed, `Reinforce` returns state rather than a `double`, several records gained members
(so a positional deconstruction no longer binds), two registered defaults changed and one forgetting curve
was deleted outright. **Stored data needs nothing**: every schema change runs automatically through
`MigrateUpAsync`, and `Stability` means the same thing it always did, so 2.5 rows are already correct under
the new curve. It is a compile-and-decide exercise, not a data one.

What the position *counts* is yours to choose, because "how much has happened" is genuinely ambiguous:

<!-- compile-skip: a menu of alternatives, one per line — not a compilable unit -->
```csharp
new PerWriteAgePolicy()        // by count — the default, wrapped in burst damping
new ContentSizeAgePolicy()     // by volume: a long document crowds harder than a note
new ElapsedAgePolicy()         // by real time, for a project memory that should fade on the calendar
new BurstDampenedAgePolicy(inner)   // wraps any of them
```

**Damping is not optional garnish.** Undamped, ingesting a 500-item document advances the position by 500
and erases everything you knew before it — reading a long document must not wipe your memory of the
project. So a burst saturates: 200 rapid writes advance by about 6 rather than 200, and are themselves
weakly encoded, which is why a person can read a book without forgetting their own name and still not
recall most of the book.

**How relinking works.** Entries returned together get linked, and the link strengthens each time it
recurs. A later recall spreads through those links, so a query can reach relevant material it never
literally matched. You can also assert edges yourself with `LinkAsync`.

The curve is swappable, but as of 3.0 there is only one shipped: `DsrRetrievability` (`Lyntai.Memory.Forgetting`)
— FSRS's power-law forgetting curve plus its three stability-increase laws. It is the default everywhere a
graph engine is built, DI or not: `AddMemoryEngine`/`AddMemory` register it for you, and a hand-constructed
`GraphMemoryEngine` (`policy: null`) now defaults to it too, so nothing has to be configured to get it and
there is exactly one behaviour to reason about regardless of how the engine was built.

```csharp
services.AddSingleton<IMemoryRetrievabilityPolicy>(new DsrRetrievability(new DsrOptions { InitialStability = 14 }));
// …or register your own before AddLyntai — a consumer's own registration always wins either direction
```

**The exponential curve this domain shipped beside through 2.5.x, `HalfLifeRetrievability`, is DELETED in
3.0 — there is no restore path.** Its own doc admitted its central `× 1.5` reinforcement constant was
"reasoned, not measured", and it was later measured compounding to 2.1× a correctly-behaving curve's
stability over a four-touch reuse batch — over-crediting massed repetition, the exact behaviour FSRS exists
to correct. A consumer who genuinely needs that shape has to implement `IMemoryRetrievabilityPolicy`
themselves; this library does not ship it any more.

**This needs no data migration.** `Stability` means one thing across every implementation — the position
delta at which retrievability is 0.5 — enforced by a contract fact against every shipped curve, so a 2.5.x
row's stored stability is already valid under DSR with no conversion.

**`GraphMemoryOptions.Decay` (the deleted curve's own `HalfLifeOptions`) is gone too.** The one field on it
that was ever this ENGINE's rather than that curve's — `EdgeHalfLife`, which decays a co-activation edge's
own WEIGHT and is read by `GraphMemoryEngine` itself (`EffectiveEdgeWeight`), independent of whichever
retrievability policy is registered — moved to a new top-level property with the same default (100):

```csharp
new GraphMemoryOptions { EdgeHalfLife = 60 }   // was: new GraphMemoryOptions { Decay = new HalfLifeOptions { EdgeHalfLife = 60 } }
```

**Why DSR is the default, and the one measured, known gap it ships with** (`docs/DECISIONS.md` D49): the
primary evidence is FSRS's own external validation against real review data, outside this repository — never
a claim this library's own corpus decided it. This library's own falsification pass did not falsify DSR, but
it did find one real, reproducible regression under the shipped ranking pairing
(`MultiplicativeRankingPolicy`) against the now-deleted exponential curve: DSR missed more on repeated/reused,
competing material, because the deleted curve's flat, unmeasured `× 1.5` reinforcement outgrew DSR's own
correctly-diminishing response to an immediate re-recall — offset by the opposite pattern on freshly-written
material, so a whole-corpus aggregate would never show either effect. This is a known limitation of shipping
a PARTIAL, unfitted FSRS (no per-review difficulty update, published rather than fitted constants) — the
conclusion and its reasoning are `docs/DECISIONS.md` D49, and `TASKS.md` carries the prioritized work to
close the gap. (The original measurement is an untracked working record, listed in
`docs/superpowers/INDEX.md` — it is not in this repository and does not ship with the package.) Past one
half-life the heavier tail rates a long-untouched entry more retrievable than the deleted exponential curve
did, so `PruneAsync` is markedly less aggressive than it was through 2.5.x.

**Ranking is a swappable seam too** (`Lyntai.Memory.Ranking`), separate from decay: `IMemoryRankingPolicy`
turns a set of recall candidates into a scored, best-first order, and the shipped `ReciprocalRankFusionPolicy`
is the REGISTERED DEFAULT as of 3.0 (owner ruling, 2026-08-11) — `Score = Σₛ wₛ / (K + rankₛ)`, summed over
relevance, retrievability, salience and hop, each contributing its own 1-based RANK POSITION rather than its
raw value, `K` defaulting to `60` (Cormack/Clarke/Buettcher's published value). **`SalienceWeight` ships at
`0`**, so salience does not vote on ranking unless you ask it to — measured across two embedding models and
five writing systems (`docs/DECISIONS.md` **D89**), and matching `MultiplicativeRankingPolicy`, whose own
salience boost has been off since D45. It became the default on the
strength of this library's own measurement (recorded as `docs/DECISIONS.md` D49 and D82; the raw run is an
untracked working record, listed in `docs/superpowers/INDEX.md`): it beat `MultiplicativeRankingPolicy` on
the corpus's `topical` class in all six measured shapes,
across two independent runs — the mechanism being exactly what rank fusion avoids and a product-of-factors
formula does not: rewarding raw reinforcement magnitude, which let an unmeasured flat multiplier out-rank a
curve (`DsrRetrievability`) that correctly declined to over-strengthen.

```csharp
// a lower K steepens the curve (the top few ranks matter more, relative to the rest of the set) and
// HopWeight above 1 pulls nearby material forward more strongly than the other three signals do
services.AddSingleton<IMemoryRankingPolicy>(new ReciprocalRankFusionPolicy(
    new ReciprocalRankFusionOptions { K = 20, HopWeight = 3 }));
```

**Its own `RelativeFloor` ships at `0`, not the `0.02` the measurement's own confound control equalized both
ranking arms at — disclosed, not papered over, and verified rather than assumed to make no difference.** Rank
fusion deliberately compresses its own score range (a `100/61 ≈ 1.639×` ratio top to bottom over forty
candidates at the default `K`), so a `0.02` relative floor over a range that tight would never cross a single
candidate's score — copying `MultiplicativeRankingOptions`'s own default here would not weaken burial, it
would make it PERMANENTLY INERT. A direct instrumentation check (replaying every corpus shape at
`RelativeFloor = 0.02`) found it cutting ZERO candidates across 995 `Rank` calls and 48,120 candidate
evaluations — the tightest worst/best score ratio observed anywhere was `0.702`, nowhere near `0.02` — so
`0.02` and `0` are empirically identical on the measured corpus and the `topical` result transfers cleanly to
what ships. A consumer who wants burial under THIS policy has to choose a floor deliberately, well above
`0.02` (see `ReciprocalRankFusionOptions.RelativeFloor`'s own doc for the formula that tells you where it
actually starts to bite for your own `K` and candidate-set size).

**A first implementation, `MultiplicativeRankingPolicy`, stays shipped and registerable in one line — it is
NOT the case a comparison found it wrong, only that it lost this one measured comparison.** `Score =
Relevance × Retrievability × boost × HopAttenuation^hop`, then a relative floor, given a name and a swap
point rather than changed. It remains the better choice on a scale where raw reinforcement magnitude is
meaningful — reciprocal rank fusion helps precisely when the signals have no shared scale to multiply, which
is not every deployment. Its own constants live on `MultiplicativeRankingOptions`:

```csharp
// the one-line restore for a consumer who wants Multiplicative back
services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy(
    new MultiplicativeRankingOptions { HopAttenuation = 0.7, SalienceRankWeight = 1.0 }));
// …or register your own IMemoryRankingPolicy before AddLyntai — a consumer's own registration always wins
```

**The forgetting-curve question is settled too** — see the forgetting-curve section above and
`docs/DECISIONS.md` D49: `DsrRetrievability` is the registered default, on FSRS's own external validation.
The full measurement — both domains, both ranking-default rounds — is an untracked working record listed in
`docs/superpowers/INDEX.md`; its conclusions are D49 (the curve) and D82 (why rank fusion won).

**One guarantee the engine keeps against a policy that DROPS a candidate:** an `Authoritative` entry a policy
drops below its own floor is re-admitted afterward — never silently dropped without a trace, even under a
policy that has never heard of grades. **That check is by `Node.Id` alone**, so it is a guarantee against
dropping, not against a policy that substitutes a fabricated entry under the same id instead — nothing
downstream can tell the two apart.
**And authoritative material takes RESERVED slots within your `Limit`**, so an exact fact is displaced only by
another exact fact. It can push ordinary hits out — that is what marking a fact authoritative means — and
`GraphMemoryOptions.AuthoritativeReserve` bounds how many slots it may take if you want the trade the other
way. Until 3.0 this went the other way round (re-admissions were appended last and cut by the limit), and the
first end-to-end measurement of "never lose an authoritative fact" found every one of them lost. **This ordering also decides
which entries get a co-activation edge PERMANENTLY written to the store** — recall's own reinforcement links
whichever entries land inside its co-activation window, and re-admission order decides who that is, not just
what a reader of the returned list sees.

**Ranking is scoped per named engine, and a single call can override it by name.** `UseGraph`'s own
`ranking` parameter is that named engine's choice, ahead of whatever `IMemoryRankingPolicy` is registered in
the container — an engine that passes nothing here still takes the container's registration, so a consumer
who never touches this parameter sees no change. `namedRankingPolicies` exposes alternates a single
`MemoryQuery.RankingPolicyName` can select for one call, resolved BY NAME rather than by passing a live
policy instance into `MemoryQuery` (which is otherwise plain data — serialized, logged, traced). A name
that engine does not recognize is an error (`KeyNotFoundException`), never a silent fallback to the default:

```csharp
services.AddLyntai(b => b.AddMemoryEngine("project", e => e.UseGraph(
    ranking: new MultiplicativeRankingPolicy(),                     // THIS engine's own choice, overriding
                                                                     // the container-registered default (RRF)
    namedRankingPolicies: new Dictionary<string, IMemoryRankingPolicy>
    {
        ["rrf"] = new ReciprocalRankFusionPolicy(),                 // selectable per call, by name
    })));

// ordinary calls use `ranking` above; this one call uses "rrf" instead
await engine.RecallAsync(new MemoryQuery("t", "s", "query", RankingPolicyName: "rrf"));
```

**`CompositeRankingPolicy` fuses two OTHER ranking policies into one order — by rank POSITION, never raw
score.** `MultiplicativeRankingPolicy`'s score is a bounded product roughly in `[0,1]`;
`ReciprocalRankFusionPolicy`'s sums to around `0.06` at its own defaults; `IMemoryRankingPolicy`'s own
contract already says a score means nothing outside the policy that produced it. Averaging the two numbers
directly would be arithmetic over quantities that share no scale, so this class instead re-derives each
member's own competition rank position over the candidate set and fuses THOSE, the same
`score = w / (K + rank)` shape `ReciprocalRankFusionPolicy` already uses for its own four signals:

```csharp
services.AddSingleton<IMemoryRankingPolicy>(new CompositeRankingPolicy(
    new MultiplicativeRankingPolicy(), new ReciprocalRankFusionPolicy(),
    new CompositeRankingOptions { PrimaryWeight = 2, SecondaryWeight = 1 }));
```

A candidate either member's own floor drops is not excluded from the fused result — it is ranked worst for
that member's signal, never fabricated as better or worse than that; a member that drops every candidate
contributes the same constant to everyone rather than distorting the order (the same "buried, not cut"
philosophy every other policy in this domain follows, and the same tie handling `ReciprocalRankFusionPolicy`
already uses internally — a signal that ties every candidate must not smuggle in a full-strength preference
through the id tiebreak alone).

Graph entries carry both grades, so one engine can hold exact facts alongside recalled ones — an
authoritative entry never decays and is never shortened to a headline.

Graph memory persists on **SQLite** and **Postgres** as well as in memory — `UseSqliteStorage()` /
`UsePostgresStorage()` register it under the same `StorageFeature.Memory` flag as the keyword store.

### Semantic memory

The lexical memory store (`IMemoryStore`) recalls by keyword (FTS-trigram). For meaning-based recall, bring
an embedding model and use `ISemanticMemory` — facts are remembered by their embedding and recalled by
cosine similarity, so a query finds relevant memories without sharing keywords.

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(apiKey: "…")
    // built-in embedder over any OpenAI-compatible /v1/embeddings (OpenAI, LM Studio, Ollama, Azure)
    .AddOpenAiCompatibleEmbedder("embeddings", o =>
    {
        o.BaseUrl = "http://localhost:11434";   // e.g. local Ollama
        o.Model = "nomic-embed-text";
    })
    .AddSemanticMemory());                      // states the intent — see below
    // …or bring your own in one call: .AddSemanticMemory(myEmbedder)  // any IEmbedder

var memory = sp.GetRequiredService<ISemanticMemory>();
await memory.RememberAsync(taskKey: "support", scope: "faq", "You can cancel your subscription anytime.");
var hits = await memory.RecallAsync("support", "faq", query: "how do I stop paying?", k: 5);
// hits ranked by similarity, each with a Content + cosine Score
```

`AddSemanticMemory()` is how you **say** you want semantic recall. Registering an embedder is what actually
turns it on, so forgetting one used to be silent — no `ISemanticMemory` at all, and every recall path
skipping it without complaint. Stating the intent turns that into a startup failure instead. Overloads take
the embedder directly (`AddSemanticMemory(myEmbedder)`, a factory, or a type), and the no-argument form is
for when the embedder arrives from elsewhere, as above.

Vectors live in a swappable `IVectorStore` — the built-in `InMemoryVectorStore` (exact brute-force cosine)
is the default; call `UseSqliteVectorStore()` to persist them in SQLite (it needs
`StorageFeature.Governance`, which carries the `lyntai_vector` table — a feature subset that omits it fails
at `AddLyntai` with a message saying so, rather than at the first recall; that check applies only where
Lyntai migrates, so `SchemaMigration.None` and a BYO `IDbConnectionFactory` are left to own their schema),
or `UsePostgresVectorStore()` for
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
    .AddOpenAiProvider(apiKey: "…")
    .AddUsageBudget(b =>
    {
        b.MaxCostUsd = 20.00;                              // global ceiling
        b.PerConsumer["scoring"] = new(MaxCostUsd: 2.00);  // a tighter cap for one consumer
    }));

// query or reset spend at runtime (async by contract — a persistent tracker must not block the front door)
var spent = (await sp.GetRequiredService<IUsageTracker>().TotalAsync()).CostUsd;
await sp.GetRequiredService<IUsageTracker>().ResetAsync();
```

Over a cap, a completion returns `Verdict == Refused` (a stream yields one Error chunk) and no provider is
called. The ceiling is **soft**: the call that crosses a cap still runs (its cost isn't known until it
returns), the next is refused. Compose with the cache and a **cached hit is free** — it never counts toward
the budget (the cache is the outermost decorator). Call `UseSqliteUsageTracking()` (or
`UsePostgresUsageTracking()`) to persist spend across restarts, or register your own `IUsageTracker` for
shared accounting. (Persisting it needs `StorageFeature.Governance` — see the governance-persistence note at
the end of **Rate limiting**.)

### Rate limiting

Throttle throughput with a token bucket. Over the configured rate a call waits briefly for a permit, then
is refused (`Verdict == RateLimited`) rather than hammering the provider.

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(apiKey: "…")
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

**A layer of your own goes on the same chain:** `AddFrontDoorDecorator(order, (sp, inner) => …)` folds PII
redaction, request logging or a bespoke cache in beside the built-ins — higher `order` = further out, with
the built-ins at 5 (rate limit) / 10 (budget) / 20 (cache), so 15 sits between budget and cache and 25
outside the cache. It is what to reach for *instead of* pre-registering an `ILlmClient`, which discards every
front-door decorator with no error at all. One trap: taking a built-in's order silently disables one of the
two — first writer wins per slot — and the loser's options are still applied and its `IResponseCache` /
`IUsageTracker` / `IRateLimiter` still registered, so the wiring reads as complete while that governance
layer is simply not in the chain.

**Persisting a governance store needs `StorageFeature.Governance`.** The default `StorageFeature.All`
already includes it, so this only concerns a deployment that migrates a subset. The three governance-backed
tables — `lyntai_response_cache`, `lyntai_usage` and `lyntai_vector` — all ship in that one migration, so
`UseSqliteResponseCache()`, `UseSqliteUsageTracking()` and `UseSqliteVectorStore()` (plus the Postgres cache
and usage-tracking twins) reject a Governance-less subset at `AddLyntai`, naming the missing feature, rather
than registering a store over a table that was never created and failing at the first cached call, metered
call or recall. Two carve-outs: the check applies only where **Lyntai** migrates (under `SchemaMigration.None`
or a BYO `IDbConnectionFactory` the schema is yours to own), and `UsePostgresVectorStore()` is exempt because
it creates its own `vector` extension and table on first use.

### Observability

Lyntai emits OpenTelemetry GenAI-convention telemetry from the router — the same schema
`Microsoft.Extensions.AI`'s `OpenTelemetryChatClient` uses, so own-seam and bridged providers land
in one backend. Nothing is emitted unless you subscribe:

<!-- compile-skip: the wiring is on OpenTelemetry's own TracerProviderBuilder/MeterProviderBuilder. No
     compile-given can declare them: this library takes no OpenTelemetry dependency (it emits over
     System.Diagnostics), so the types are not in the compilation at all. -->
```csharp
tracerProviderBuilder.AddSource(LyntaiDiagnostics.ActivitySourceName);        // "Lyntai.Llm" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.MeterName);                   // duration, token usage,
                                                                              // time_to_first_chunk

// image/video/audio/3d renders emit on a second source/meter:
tracerProviderBuilder.AddSource(LyntaiDiagnostics.GenerationActivitySourceName);  // "Lyntai.Generation" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.GenerationMeterName);         // render duration + reported cost

// the agentic subsystems (tool loop, durable jobs, guards) emit on a third source/meter:
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

<!-- compile-skip: a tour of BYO seams. compile-given was measured and rejected here: IProcessRunner
     alone is two eight-parameter methods, and with MyCustomProvider (ILlmProvider, four members) and a
     connection factory the context runs to ~26 lines for a 16-line sample — a whole program, not a few
     declarations. -->
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

Owning the schema means running Lyntai's migrations yourself, on your own terms —
`MigrationRunnerService.MigrateUp(path[, features])`, or its awaitable twin `MigrateUpAsync(…, ct)` for an
async startup path. Read the twin's documentation before relying on the token: FluentMigrator's runner is
synchronous, so `MigrateUpAsync` runs **inline on the calling thread** (deliberately not a `Task.Run`) and
the token is honoured only *before* any work and *between* feature passes — a pass in flight cannot be
cancelled, and the default `StorageFeature.All` is a single pass.

Anything you register wins over Lyntai's default (the defaults use `TryAdd`), and every storage domain
is itself an interface (`IKeyValueStore`, `IMemoryStore`, …) you can implement wholesale.

### Backend self-maintenance: version · upgrade · pinned install · auth

Four **optional** provider capabilities (`IProviderProbe`, `IProviderUpdater`,
`IProviderVersionInstaller`, `IProviderAuth`), so a host can show what its backend actually is, whether it
is usable at all, and offer an upgrade — instead of hardcoding a version it will drift away from, or
burning a turn to discover the backend isn't signed in. All are discovered by pattern-matching over the
registered providers, none runs a completion, and all **fail safe**: an absent, stalled or erroring backend
is reported, never thrown.

```csharp
foreach (var provider in serviceProvider.GetServices<ILlmProvider>())
{
    if (provider is not IProviderProbe installation) continue;

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
  (`docs/DECISIONS.md` D20).

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

<!-- compile-given: string portableHome;
     string bundledClaudePath; -->
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

<!-- compile-given: static class MyWireFormat { public static CliOutputEvent Read(string line) => CliOutputEvent.Ignored; } -->
```csharp
public sealed class MyCliDialect : CliProviderDialectBase
{
    public override string Id => "my-cli";
    public override string DefaultCommand => "mycli";
    public override IReadOnlyList<string> CommandEnvironmentVariables => ["LYNTAI_PROVIDER_CMD", "MYCLI_CMD"];
    // `toolHostArgs` point this CLI at Lyntai's own MCP endpoint (empty unless you AddMcpToolHost).
    // WHERE they go is yours to decide, because only you know your CLI's grammar: append when your argv
    // ends in options, place them earlier when it ends in a positional — anything after a positional is
    // read as prompt text by some CLIs, and on codex a swallowed flag costs a turn rather than erroring.
    public override IReadOnlyList<string> BuildCompletionArgs(LlmRequest r, IReadOnlyList<string> toolHostArgs) =>
        ["exec", "--json", .. toolHostArgs];
    public override CliOutputEvent ParseLine(string line) =>   // → Content / Result / Failure / Ignored
        MyWireFormat.Read(line);

    // claim an optional capability ONLY where the real binary has it — the base claims none by default
    public override IReadOnlyList<string>? UpdateArgs => ["update"];
}
```

…plus a provider that forwards to the engine and declares which capability interfaces that backend actually
has (`ClaudeCliProvider` is exactly this, and nothing else):

<!-- compile-given: sealed class MyCliDialect : CliProviderDialectBase
     {
         public override string Id => "my-cli";
         public override string DefaultCommand => "mycli";
         public override IReadOnlyList<string> BuildCompletionArgs(LlmRequest r, IReadOnlyList<string> toolHostArgs) => [];
         public override CliOutputEvent ParseLine(string line) => CliOutputEvent.Ignored;
     } -->
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

<!-- compile-given: string key; -->
```csharp
services.AddLyntai(cfg => cfg
    // hosted: an OpenAI-compatible images API
    .AddOpenAiImageProvider(o => { o.ApiKey = key; o.Model = "gpt-image-1"; })
    // local: a Stable Diffusion WebUI on this machine
    .AddAutomatic1111Provider(o => { })
    .UseDefaultGenerationCandidates("openai-images", "a1111"));
```

Each backend has an `Add*` of its own — `AddOpenAiImageProvider`, `AddAutomatic1111Provider`,
`AddComfyUiProvider`, `AddFalProvider`, `AddLocalDiffusionProvider` — and each takes a **configure
callback**, the same shape as `AddOpenAiCompatibleProvider(id, o => …)` on the LLM side. Every option has a
default (each backend's conventional local URL, or the vendor's API root), so a registration sets only what
differs from it; a blank base URL reports `NotConfigured` rather than failing.
`AddGenerationProvider(sp => …)` remains the BYO seam for a backend of your own.

BYO `HttpClient` is optional on every one of the four HTTP backends — `AddLocalDiffusionProvider` takes a BYO
`IProcessRunner` instead, because it spawns a binary and never makes a request — and Lyntai **never disposes a
client you supply**: it is yours, and it may be carrying a Polly pipeline or an auth handler. Omit it and
Lyntai registers a named client with an *infinite* `HttpClient` timeout, so the per-call deadline owns
cancellation rather than the 100-second default aborting a healthy render. To decorate Lyntai's own client
instead of replacing it, reach it by name:

<!-- compile-given: sealed class MyLoggingHandler : DelegatingHandler { } -->
```csharp
services.AddHttpClient(GenerationProviderBuilderExtensions.HttpClientName("fal"))
        .AddHttpMessageHandler<MyLoggingHandler>();
```

**That deadline is per backend, and infinite there does not mean unbounded.** Every options object carries a
`Timeout` — 10 minutes for the hosted inline render backends (`OpenAiImageOptions`, `Automatic1111Options`), 2 minutes
for the queue ones (`ComfyUiOptions`, `FalQueueOptions`, whose calls are submit/status/fetch round-trips rather
than renders), and 15 minutes for `LocalDiffusionOptions`, paired there with a 2-minute `InactivityTimeout`:
a CPU render is legitimately slow but never *silent*, so silence rather than elapsed time is what marks it
wedged — and a request's own `TimeoutSeconds` overrides it where a request exists. A fired deadline is a
`GenerationVerdict.Timeout` **result**, not a throw; your own `CancellationToken` keeps its own meaning and
still surfaces as cancellation. Set `Timeout = System.Threading.Timeout.InfiniteTimeSpan` to drop the backend's
own deadline — a request that names its own `TimeoutSeconds` still gets one, since the more specific
instruction wins either way.

For a queue backend the deadline bounds one HTTP call, never the render — the render outlives every call, and
bounding it is the durable job's retry budget to do. One consequence is worth knowing: a **submit** that gets no
answer comes back `Failed` **and `Inconclusive`**, and the router then *surfaces* it rather than trying the next
backend, because a queue that never answered may already hold a billable render and the next candidate would buy
the same generation twice. It is not counted against the backend's cooldown either.

Inputs — an init image, a first frame, a style reference, a voice sample — are built with the **named
factories**, never the positional constructor:

<!-- compile-given: byte[] sourcePng; -->
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
(`docs/DECISIONS.md` D28).

Backends declare what they can do, and the router **skips a candidate that can't serve the request** before
spending anything — media backends differ far more than chat models do (medium, input roles, duration
ceilings, model catalogues):

<!-- compile-given: IReadOnlyList<GenerationCandidate> candidates;
     void Save(byte[] data) { } -->
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
`FetchAsync(operationId)` when it fires.

**Chaining is first-class**, and `RunPipelineAsync` runs a chain for you: ordered stages, each stage's
artifact fed into the next through `artifact.ToInput(role)`. Every stage carries its own candidates and
routes independently, so the image leg and the video leg need not be the same vendor:

<!-- compile-given: GenerationRequest image;
     GenerationRequest video; -->
```csharp
var result = await router.RunPipelineAsync(
[
    new GenerationStage(image, [new GenerationCandidate("openai-images")]),
    new GenerationStage(video, [new GenerationCandidate("fal")])
    {
        InputRole = GenerationInputRoles.FirstFrame,        // what the still IS to the video backend
    },
]);

if (!result.IsOk)
{
    // nothing is ever re-run, so the still stage 1 already paid for is still here
    var stills = result.Stages[0].Artifacts;
}
```

Every stage is an ordinary routed call, so spend caps, throttling and dead-host cooldown govern a pipeline
exactly as they govern one render. A stage that cannot identify a single artifact to chain **refuses**
(`Unsupported`) rather than guessing — a media type cannot be branched on, and "the first `image/*`" picks a
texture atlas on a mesh backend; `GenerationStage.SelectInput` is where you state your own rule.

**`3d → image → video` is not one of the chains you can build**, and the reason is worth knowing before you
try: no image or video backend accepts a mesh, so that first edge is a *rasterization* rather than a
generation and this platform performs none. `GenerationKinds.Model3d` exists so a backend serving it needs no
contract change.

Every backend answers **"are you usable?"** without generating anything (`ProbeAsync`), so a setup screen
never has to pay for a test image. The `generate_backends` tool asks all of them **concurrently, under one
aggregate `GenerationOptions.ProbeDeadline`** (20s) — a backend that overruns it or throws is listed
`usable: false` with the reason rather than dropped, because telling a model a configured backend does not
exist is worse than telling it one is not answering.

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
| `Automatic1111Provider` | Inline | A locally-run SD WebUI: `txt2img` / `img2img`. Not running reports **NotConfigured** (skipped, not blamed), and its probe checks a checkpoint is *loaded* — "up" isn't "usable". The WebUI's currently-loaded checkpoint decides the model: `GenerationRequest.Model`, including a candidate's `a1111:sd_xl_base` pin, is **not** sent |
| `ComfyUiProvider` | **Job** | *Documented, not measured.* Local and workflow-driven: you supply the graph in `Options["workflow"]` (+ optional `Options["prompt-path"]` to place the prompt), and outputs come back as view URIs. A transport failure while polling reports **Running, not Failed** — an unanswered status call says nothing about a run still going — while a 4xx or an unconfigured base URL stays terminal, so a bad id never polls forever |
| `LocalDiffusionProvider` | Inline | A local `sd-cli` / stable-diffusion.cpp subprocess through `IProcessRunner` — no key, no network, no content policy in the path. Argv and the multiple-of-64 size clamp are ported from a working implementation rather than measured here |
| `FalQueueProvider` | **Job** | *Documented, not measured.* One aggregator queue reaching the Wan/Kling/Veo-class video models. The operation id **carries its model** (`"model#requestId"`) because a resumed job has only the id, and a transport failure while polling reports **Running, not Failed** — a 500 says nothing about a paid render still in flight |

**Not in scope, by design:** generation itself, downloading engines or model weights, hosting a webhook
endpoint, storing artifacts, or holding your credentials — see `docs/DECISIONS.md` D20 and D24.

### When your users own the backend configuration (`Lyntai.Lifecycle`)

Everything above assumes the *deployment* configures the backends: you call `Add*` once and the container
holds them. If instead an **end user** — or a store your process polls — owns that configuration, the
settings change at any moment, the choice of backend is itself one of those settings, and several
configurations of one backend are live at the same time. Hand the router factory a key and a way to build
each backend, and it does the rest:

<!-- compile-skip: per-tenant pseudo-code over the reader's own settings service and router cache -->
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
in-flight calls finish normally (`docs/DECISIONS.md` D30).

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
package (it costs that package no extra dependencies; the host — and the `ModelContextProtocol.Core`
reference it needs — stays here, so apps using the plain CLI provider carry neither). Supporting a different
CLI is one small class, no new package and no change to the host:

<!-- compile-skip: a type declaration and its registration statement in one fence — a block is wrapped at one scope -->
```csharp
public sealed class MyCliMcpDialect : IMcpCliDialect
{
    public string ProviderId => "my-cli";

    public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext ctx, CancellationToken ct = default)
    {
        // write whatever config file the CLI reads (JSON, TOML, …) — the host deletes it for you
        var path = ctx.WriteTempFile("mcp", $$"""{"servers": {"{{ctx.Endpoint.ServerName}}": {"url": "{{ctx.Endpoint.Url}}"} } }""");
        return ValueTask.FromResult<IReadOnlyList<string>>(["--mcp-config", path]);
    }
}

services.AddLyntai(b => b.AddMcpToolHost(new MyCliMcpDialect()));
```

The provisioner is registered keyed on `ProviderId`, so several CLI providers can host tools side by side
with different dialects. (This runs an ephemeral `HttpListener` on loopback only during each CLI call —
a deliberate, scoped exception to Lyntai's otherwise host-free design, isolated in this opt-in package.)

### CLI-agent session vs `IToolLoop` (`IAgentSession`)

When the external agent drives its OWN tool loop out-of-process (the `claude` or `codex` CLI running
autonomously), `IAgentSession` is the right primitive — not `IToolLoop`. You observe a streamed
transcript of what the agent did (`AgentStreamEvent`), gate it read-only (plan) vs write (execute) via
`AgentToolPolicy`, and resume it across a human confirmation gate using the session's `ResumeToken`.
Two consumption doors: `StreamAsync` (live event-by-event, for progress UI or structured logging) and
`RunAsync(onEvent)` (fold to a result for callers that only need the outcome).

The `IAgentSession` interface is neutral Core (`Lyntai.Agents`); all claude-specific flags
(`--settings`, `AllowedTools`) live in the `Lyntai.Providers.Default` package (namespace
`Lyntai.Providers.ClaudeCli`)
(`ClaudeAgentSession` / `ClaudeAgentOptions`, registered via `AddClaudeCliAgentSession()`).

**Give the agent your app's own tools — on either backend** (`AgentSessionOptions.McpServers`). An agent
embedded in an app is usually there to act on *that app's* domain, which it reaches through the app's MCP
servers. This is neutral Core, so the same options object drives both CLIs; each adapter renders it in its
own vocabulary (claude: an owner-only `--mcp-config` document, deleted when the turn ends; codex: repeated
`-c mcp_servers.<name>.…` TOML overrides). Both shapes were measured against the real CLIs.

<!-- compile-given: string cwd;
     string appToolsExePath;
     string workspacePath;
     string token; -->
```csharp
var options = new AgentSessionOptions
{
    Prompt = "Rename the selected clip and re-export it.",
    WorkingDirectory = cwd,
    McpServers =
    [
        // the app's own tools, shipped as a child process — no port, nothing to authenticate
        AgentMcpServer.Stdio("app-tools", appToolsExePath, ["--serve"],
            new Dictionary<string, string> { ["APP_WORKSPACE"] = workspacePath }),
        // …or something already running over HTTP
        AgentMcpServer.Http("remote", "https://tools.example.com/mcp", authToken: token),
    ],
};
```

Three things worth knowing before you rely on it:
- **Naming a server makes its tools reachable, not approved.** claude still needs
  `ClaudeAgentOptions.AllowedTools` (or `SkipAllPermissions`) for a headless run; codex still gates on
  `--sandbox`. Auto-approving your servers would be a silent change of security posture, so it stays yours.
- **A server that cannot be rendered refuses the turn** — one `SessionEnded` with `LlmVerdict.Unsupported`
  and no process spawned — rather than being dropped. A dropped server is an agent that runs and silently
  cannot do its job. Names are letters/digits/`_`/`-` (they become configuration keys), stdio needs a
  `Command`, HTTP needs an absolute `Url`, and duplicate names are rejected.
- **An `AuthToken` never reaches the command line**, which is readable machine-wide: claude gets it in the
  owner-only temp document, codex gets an environment variable it names (`bearer_token_env_var`, the only
  shape that CLI accepts).
- Your own `ClaudeAgentOptions.McpConfigPath` still works and is **kept alongside** the rendered one — the
  flag takes a list, so nothing you already wired up is displaced.

<!-- compile-given: string cwd; -->
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

#### The codex agent session — same shape, and what it honestly cannot do

`AddCodexCliAgentSession()` registers a `CodexAgentSession` (`Lyntai.Providers.CodexCli`) behind the same
`IAgentSession`, so an app can offer both CLI backends without hand-parsing `codex exec --json`. Both
`Add*CliAgentSession` extensions also register **keyed by provider id**, so registering both resolves
deterministically:

```csharp
services.AddLyntai(b => b.AddClaudeCliAgentSession().AddCodexCliAgentSession());

var codex = sp.GetRequiredKeyedService<IAgentSession>("codex-cli");
var claude = sp.GetRequiredKeyedService<IAgentSession>("claude-cli");
```

**Read this before adopting it** — the two halves of the codex mapping have different standing, and
`docs/DECISIONS.md` **D35** has the full account:

- **Measured** against codex-cli 0.146.0: session id, assistant text, final usage, and the terminal —
  including the rule that only `turn.failed` fails a turn (a bare `error` line and an `error` item both
  appear in runs that succeed).
- **Inferred**: every **tool step**. The measured run used no tools. The mapping is therefore shape-driven —
  a tool step arrives under codex's *own* item-type name with codex's *own* item object as
  `ToolCall.ArgumentsJson` / `ToolResult.Content` (no normalised schema, and deliberately no `CodexToolCalls`
  helper). **What that guarantees, precisely:** no payload is ever invented or dropped, and every uncertainty
  stays inside the tool-step half — the session id, terminal and usage are measured and unaffected. **What it
  does not guarantee is the KIND of event.** The tool arm is reached by *elimination* against three
  recognised names (`agent_message`, `reasoning`, `error`), so an item that is not one of them and not a tool
  — a renamed `reasoning`, a `todo_list`-style plan update — arrives as a fabricated `ToolCall`, which is not
  what `ToolCall` means. Treat a tool step's **kind as provisional and its payload as reliable**, and switch
  on `ToolCall.Name` rather than assuming every one is a tool. Likewise `ToolResult.IsError` is a *positive*
  claim of success when no top-level `status`/`exit_code` says otherwise — a nested failure signal would read
  as a successful step.
- **Not emitted**, because codex has no analogue: `UsageLive`, `SessionEnded.Subtype`, `UsageFinal.Model`,
  and token-level deltas — a codex `TextDelta` is one whole assistant message, not a token.
- **`ResumeToken` is honoured** (measured 2026-08-05 via `codex exec resume --help`, a flag and therefore
  turn-free): the argv becomes `codex exec resume … <SESSION_ID> -`. The one shape still **refused** without
  spawning is a token the CLI would read as an OPTION rather than an id — blank, or starting with `-` such as
  codex's own `--last`, which would quietly resume the *wrong* thread. Note that whether codex re-announces
  `thread.started` on a resumed turn is not measured, so a resumed `SessionEnded.SessionId` may be null where
  a fresh one's is set; nothing is fabricated to cover that.
- **`DisallowedTools` is logged as unhonoured** — codex's tool gate is `--sandbox`, driven by `ToolPolicy`
  (`ReadOnly` → `read-only`, `Write` → `workspace-write`) or set outright via `CodexAgentOptions.SandboxMode`.
  **`SystemPrompt`** travels as a leading block of the prompt (codex `exec` has no flag for one).

### Durable jobs (`Lyntai.Jobs`)

Run long, multi-step work (e.g. many agents) that survives restarts, with lanes for concurrency control.
Enqueue a job, a runner claims and runs it, your handler checkpoints — and a job whose worker crashed is
reclaimed and **resumed from its checkpoint**. Your app owns the pump (no background threads are started
for you):

<!-- compile-skip: a handler declaration and its wiring statements in one fence — a block is wrapped at one scope -->
```csharp
sealed class SummarizeHandler : IJobHandler
{
    public string Type => "summarize";
    public async Task<JobOutcome> HandleAsync(JobContext ctx, CancellationToken ct)
    {
        if (ctx.Checkpoint is null) { /* step 1 … */ await ctx.SaveCheckpointAsync("fetched", ct); }
        /* step 2 (skipped-ahead on resume) … */
        return JobOutcome.Complete;   // or Retry(delay) / Fail(reason) / Poll(delay)
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

<!-- compile-given: string payloadJson;
     Guid jobId; -->
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
  OpenAI-compatible and MEAI-bridged providers send it as image content, and the **Ollama-native** flavour
  (`AddOllamaProvider`, or any base URL detected as Ollama) sends it as `/api/chat`'s own `images` array.
  Pair it with a vision model (`llava` and friends). **One shape does not travel on the Ollama-native path:**
  an attachment carrying only a remote URL, because `/api/chat` has no URL form and Lyntai will not fetch
  the bytes for you — it is logged as undeliverable rather than dropped silently. Send bytes, or use
  Ollama's `/v1` surface through `AddOpenAiCompatibleProvider`, which takes a URL.

## Dev loop

```
node devtools/dev.mjs verify           # THE "am I done?" gate — sixteen checks, stopping at the first
node devtools/dev.mjs build            # build the solution
node devtools/dev.mjs test             # xUnit tests (unit + integration, zero real tokens)
node devtools/dev.mjs e2e --build      # Playground full-stack smoke against the provider-stub
node devtools/dev.mjs playground       # run the sample console app yourself
node devtools/dev.mjs pack             # dotnet pack → publish/packages/
node devtools/dev.mjs install-hooks    # enable the pre-commit guards (sensitive info, encoding, version)
```

`verify` is first because three of the commands under it are steps INSIDE it; running them individually is
for a fast loop, not for deciding you are done. `node devtools/dev.mjs` with no argument prints the
authoritative command list — it is derived, so it cannot go stale the way a copy here can.

See `.claude/rules/dotnet-package-layout.md` (package boundaries, naming, variation points) and
`.claude/rules/repo-mechanics.md` (this repo's concrete bindings), with `CLAUDE.md` §Dev loop for what each
gate is for.

## License

[MIT](LICENSE) © Jiarong Gu — the same `MIT` SPDX expression every NuGet package carries.
