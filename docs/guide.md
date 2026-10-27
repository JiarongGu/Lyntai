# Lyntai guide

The consuming reference for [Lyntai](../README.md): every feature, the semantics it guarantees, and a sample
for each. The [README](../README.md) is the overview, the quickstart and the package list. Long-term memory and
media generation have guides of their own, `docs/memory.md` and `docs/generation.md`.

**This guide documents the working tree, not only the newest package**: anything that has not shipped yet is
listed under `## Unreleased` in `CHANGELOG.md`, so check there before assuming a member below is in the
version you installed.

## Contents

- [When to use Lyntai, and when not to](#when-to-use-lyntai-and-when-not-to)
- [Start from a scenario](#start-from-a-scenario)
- [Install and compose](#install-and-compose)
- [The semantics you're getting (design §6)](#the-semantics-youre-getting-design-6)
- [Structured output](#structured-output)
- [Governance: response cache, usage budget, rate limit](#governance-response-cache-usage-budget-rate-limit)
- [Named memory engines](#named-memory-engines)
- [Graph memory — forgetting, relinking, and a cheap first load](#graph-memory--forgetting-relinking-and-a-cheap-first-load)
- [Semantic memory](#semantic-memory)
- [Observability](#observability)
- [Bring your own resources](#bring-your-own-resources)
- [Backend self-maintenance: version · upgrade · pinned install · auth](#backend-self-maintenance-version--upgrade--pinned-install--auth)
- [CLI backends: `claude`, `codex`, or your own (`CliProviderEngine` + an `ICliBackend`)](#cli-backends-claude-codex-or-your-own-cliproviderengine--an-iclibackend)
- [Generation: image · video · audio · 3d (`Lyntai.Generation`)](#generation-image--video--audio--3d-lyntaigeneration)
- [When your users own the backend configuration (`Lyntai.Inference`)](#when-your-users-own-the-backend-configuration-lyntaiinference)
- [Bridging a backend Lyntai has no provider for (`AddBridgeProvider`)](#bridging-a-backend-lyntai-has-no-provider-for-addbridgeprovider)
- [Local in-process inference (`Lyntai.Providers.LlamaSharp`)](#local-in-process-inference-lyntaiprovidersllamasharp)
- [Tool-calling (`Lyntai.Agents`)](#tool-calling-lyntaiagents)
- [CLI-agent session vs `IToolLoop` (`IAgentSession`)](#cli-agent-session-vs-itoolloop-iagentsession)
- [Durable jobs (`Lyntai.Jobs`)](#durable-jobs-lyntaijobs)
- [Guards, orchestration, secrets, vision](#guards-orchestration-secrets-vision)

## When to use Lyntai, and when not to

Lyntai is a library a .NET application links in. It has no server, no UI and no language other than C#.

**It fits when** the model calls, the memory and the governance belong inside your own process:

- **One application, several backends.** A hosted model first, a local `llama-server` or Ollama as the
  offline backstop, the signed-in `claude` or `codex` CLI with no API key: all candidates behind one
  `ITextClient`, with failover, retries and dead-host cooldown, and no proxy to run.
- **Memory that lives in your database.** The memory engines store on SQLite, PostgreSQL, files or in memory
  beside your own tables. The graph engine decays by interference rather than the clock, buries rather than
  deletes, and removes only when asked: `ForgetAsync` for a user's withdrawal, `PruneAsync` for capacity.
- **One set of controls on every call.** The response cache, usage budget and rate limit sit on the front
  door, so the tool loop, the scorers and memory's own model calls pass through them; a tool approver sees
  every tool call, whichever path runs it.

**Reach for something else when:**

- **You are building on Microsoft Agent Framework or `Microsoft.Extensions.AI`.** Lyntai ships no adapter
  package for either (`docs/DECISIONS.md` D146), and its public seams cover both directions: an `IChatClient`
  enters through [`AddBridgeProvider`](#bridging-a-backend-lyntai-has-no-provider-for-addbridgeprovider), and
  Lyntai leaves as one through a small class you own — both tested, and copied from the tested files, in
  [`docs/extensions-ai.md`](extensions-ai.md). Lyntai has a tool loop and durable jobs, not a multi-agent
  workflow graph. If those abstractions are what you want, build the agents on Agent Framework and keep Lyntai
  for what sits under them: routing, fallback, budget and memory.
- **You need a gateway many services share.** LiteLLM is a proxy: one endpoint for every language and
  team, virtual keys, central spend tracking and a provider catalogue far wider than Lyntai's. Lyntai's
  budget and rate limit are per process unless you back them with shared state (`UsePostgresUsageTracking`
  for the budget, an `IRateLimiter` of your own for the rate). The two compose: LiteLLM speaks OpenAI's
  schema, so `AddHttpProvider` can point at it.
- **You want a memory service that extracts facts for you.** Mem0 and Zep turn conversations into facts
  and user profiles with a model, and Zep tracks when a fact stopped being true. Lyntai stores what you write
  (subject annotation is opt-in, `AddMemoryAnnotation()`); it does not extract facts, and a `MemoryWrite`
  carries no valid time, so nothing supersedes anything. An old fact fades as newer material is written and
  recalled; it is never marked false. They also serve many languages over HTTP, which Lyntai does not.

## Start from a scenario

Three minimal programs, each compiled against the library on every `verify`. The sections below hold the
semantics.

**Routed text with a fallback and a hard token cap.** The hosted model answers when its key is set. Without
one it answers `NotConfigured`, and the router moves on to the local server with no cooldown:

```csharp
using Lyntai;
using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "", model: "gpt-4o-mini")
    .AddLlamaProvider()                                 // llama-server on localhost:8080, the offline backstop
    .UseDefaultCandidates("openai", "llama")
    .ConfigureRouting(r => r.Retry(ProviderVerdict.Failed, 1))
    .AddUsageBudget(b =>
    {
        b.MaxTokens = 2_000_000;                        // for the whole process
        b.ReserveTokens = 8_000;                        // held per call while it runs, which makes the cap hard
    }));

await using var sp = services.BuildServiceProvider();
var llm = sp.GetRequiredService<ITextClient>();
var reply = await llm.CompleteAsync(new TextRequest { Messages = [TextMessage.User("Summarize RFC 9110 in one line.")] });
Console.WriteLine(reply.Verdict.IsOk() ? reply.Text : $"{reply.Verdict}: {reply.Detail}");
```

**Long-term memory for an assistant.** Remember under a task (here, one user) and a scope. A later turn
recalls what is relevant into its prompt, and a withdrawal removes the user's material:

```csharp
using Lyntai;
using Lyntai.Cortex;
using Lyntai.Inference;
using Lyntai.Memory;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLyntai(cfg => cfg
    .AddClaudeCliProvider()
    .UseDefaultCandidates("claude-cli")
    .UseSqliteStorage("assistant.db")                   // the graph engine persists here
    .AddMemory());                                      // one engine, "default", behind IPromptComposer

await using var sp = services.BuildServiceProvider();
var memory = sp.GetRequiredService<IMemoryEngineFactory>().Get();
await memory.RememberAsync(new MemoryWrite("user-42", "profile", "Prefers metric units."));
await memory.RememberAsync(new MemoryWrite("user-42", "travel", "Is going to Kyoto in April."));

var question = "What should I pack?";
var prompt = await sp.GetRequiredService<IPromptComposer>()
    .ComposeAsync(question, taskKey: "user-42", query: question);
var reply = await sp.GetRequiredService<ITextClient>()
    .CompleteAsync(new TextRequest { Messages = [TextMessage.User(prompt)], Consumer = "assistant" });

if (memory is IForgettableMemory forgettable)
    await forgettable.ForgetAsync("user-42");           // every scope of the task
```

**Tool calling with approval.** Every tool call passes the registered approvers before it runs. A refusal
reaches the model as the tool's result, and the run goes on:

```csharp
using Lyntai;
using Lyntai.Agents;
using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

public sealed class ConfirmSends : IToolApprover
{
    public Task<ToolApprovalResult> ApproveAsync(ToolApprovalRequest call, CancellationToken ct = default)
    {
        if (!call.ToolName.StartsWith("send_", StringComparison.Ordinal))
            return Task.FromResult(ToolApprovalResult.Approve);
        Console.Write($"Allow {call.ToolName}({call.ArgumentsJson})? [y/N] ");
        return Task.FromResult(Console.ReadLine() == "y"
            ? ToolApprovalResult.Approve
            : ToolApprovalResult.Deny("the user declined to send this"));
    }
}

public static class SupportAgent
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddOllamaProvider(model: "qwen3")
            .UseDefaultCandidates("ollama")
            .AddTool(_ => new FunctionTool("lookup_order",
                (args, ct) => Task.FromResult("""{"status":"shipped"}"""), "Look up an order by id"))
            .AddTool(_ => new FunctionTool("send_email",
                (args, ct) => Task.FromResult("sent"), "Email the customer"))
            .AddToolApprover<ConfirmSends>());

        await using var sp = services.BuildServiceProvider();
        var result = await sp.GetRequiredService<IToolLoop>().RunAsync(new TextRequest
        {
            Messages = [TextMessage.User("Where is order 1187? Email the customer the answer.")],
            Consumer = "support",
        });
        Console.WriteLine(result.Verdict.IsOk() ? result.Answer : $"{result.Verdict}: {result.Detail}");
    }
}
```

## Install and compose

```bash
dotnet add package Lyntai                  # the recommended STARTING set — 5 of the 11 packages
dotnet add package Lyntai.Storage.Sqlite   # a database (the bundle persists only as files, once you name a root)
dotnet add package Lyntai.Generation       # image/video/audio backends
```

**`Lyntai` is a starting set, not the whole library.** Nothing persists until you either name a root with
`UseFileSystemStorage` (the six domains a person reads — keys, prompts, conversations, task and curated
memory, the memory engine's graph — not jobs or the cache) or add `Lyntai.Storage.Sqlite` or `.Postgres`; and
generation is not included. The six packages left out carry a native payload, a platform-specific API, a
server dependency, or a surface most applications never call (`docs/DECISIONS.md` D26).

**Size.** An unused dependency costs nothing at runtime, because an assembly loads on first type reference;
to keep one out of your publish output, reference only the packages you use or publish trimmed
(`PublishTrimmed=true`). `docs/AOT.md` has the measured footprint.

Then compose in DI:

```csharp
using Lyntai;                       // the builder + Add*/Use* extensions
using Lyntai.Cortex.Scorers;
using Microsoft.Extensions.DependencyInjection;

services.AddLyntai(cfg =>
{
    cfg.AddClaudeCliProvider();                          // spawns the authenticated `claude` CLI, no API key
    cfg.AddHttpProvider("ollama", o => o.BaseUrl = "http://localhost:11434");
    cfg.UseSqliteStorage("app.db");                      // all storage domains, migrated on startup
    cfg.AddScorer<OutcomeScorer>();                      // eval dimensions are DI registrations
    cfg.AddScorer<RelevancyScorer>();                    // (this one is an LLM judge through the router)
    cfg.UseDefaultCandidates("claude-cli", "ollama");       // router fallback order
});
```

Then inject the front door. **To your app, Lyntai behaves like one LLM provider** — `ITextClient` has
`IModelProvider`'s shape, and candidate order, fallback, and dead-host handling happen invisibly behind it:

```csharp
public sealed class MyFeature(
    ITextClient llm,
    IPromptRegistry prompts, IPromptComposer composer,
    IScoringService scoring, ITraceService traces, IMemoryStore memory)
{
    public async Task<string> AskAsync(string question, CancellationToken ct)
    {
        var prompt = await prompts.RenderAsync("myfeature.ask",
            "Answer briefly: {question}", new Dictionary<string, string> { ["question"] = question }, ct);
        prompt = await composer.ComposeAsync(prompt, taskKey: "myfeature", ct: ct); // + recalled context

        var reply = await llm.CompleteAsync(
            new TextRequest { Messages = [TextMessage.User(prompt)], Consumer = "myfeature" }, ct);
        return reply.Verdict.IsOk() ? reply.Text : throw new InvalidOperationException(reply.Detail);
    }
}
```

(`ITextRouter` stays available for call sites that genuinely need their own candidate list.)

`ProviderVerdict` carries three call-site predicates — `IsOk()`, `IsTransient()` ("may the same request
succeed later?", true for `Failed`/`Timeout`/`RateLimited`) and `IsBlameless()` (the backend declined without
anything being wrong with it). They hang off the enum, so they read the same off `TextResponse`, `TextChunk`,
`SessionEnded`, `AgentSessionResult` and `ToolLoopResult`.

## The semantics you're getting (design §6)

- **Fallback router:** candidates are deduped and tried in order; `Failed`/`Timeout` advances,
  `RateLimited` puts that host on immediate cooldown and advances to the next candidate (a 429 is
  terminal for the host's window, not for the fleet), `Refused` surfaces with no fallback (content
  policy follows the prompt, not the host).
- **A backend you listed but never configured is skipped, not benched** — a 401/403 to a call that carried
  no credentials is `NotConfigured`, and the router advances with no cooldown and no dead-host penalty
  (`AuthFailed`, a supplied key that got rejected, still cools the host). A blameless verdict never *masks* a
  real one: if one candidate is down and the next is unconfigured, you're told about the outage.
- **Streaming never falls back after the first token** — pre-content failures move to the next
  candidate, mid-stream errors pass through unchanged (your consumer never sees duplicated output).
- **Per-request refusal check** — set `TextRequest.RefusalPattern` (a regex) and an otherwise-`Ok` reply
  whose text matches surfaces as `Refused`. Screened at the outermost front-door layer, so even a cached hit
  is re-checked.
- **Dead-host cooldown** instead of exponential backoff; any success resets.
- **Per-request timeout** — set `TextRequest.TimeoutSeconds` (or a per-consumer `TimeoutByConsumer` default)
  when one call legitimately runs far longer than the global `ProviderTimeout`, without inflating every short
  call. Precedence: request → consumer → global; only the request's own value is clamped, to
  `MaxProviderTimeout`.
- **All of the above is the default `RoutingPolicy` — tune it without a fork.** Retry a transient
  fault on the same candidate before failing over, override what each verdict does, cool by
  `(provider, model)` instead of whole-host, or keep the sole candidate always live:
  ```csharp
  cfg.ConfigureRouting(r =>
  {
      r.Retry(ProviderVerdict.Failed, 1);                     // one retry before advancing
      r.CooldownScope = CooldownScope.ProviderAndModel;  // per-model rate-limit cooldown
      r.On(ProviderVerdict.RateLimited, FallbackAction.Surface); // e.g. don't fall back on 429
  });
  ```
- **Prompt overrides** live in the key-value store under `lyntai.prompt.<name>`; an override that
  drops a `{placeholder}` present in the default is rejected (falls back to the default, with a warning).
- **Memory recall is bounded and fail-open:** FTS5 trigram match, LIKE fallback, capped per (task, scope) —
  and it never throws into your prompt path.
- **Recall is not English-only, and needs no setup to not be.** A query splits into words for a spaced
  script and character **trigrams** for one without spaces, the same way on every backend, so
  `"我的配偶叫什么名字"` finds `"我的配偶是爱丽丝"`. Backends differ in how they RANK matches, never in which they find.
- **Memory can learn what a fact is ABOUT** (opt-in, one model call per write). `AddMemoryAnnotation()` links
  entries that share a subject, so `"my spouse"` finds "she works as an anaesthetist", and a recall seeds the
  entries filed under any subject its query names, so `"配偶"` reaches the fact whose text says `"太太"`.
- **A model can judge which recalled entries actually ANSWERED the query** (opt-in, one model call per
  recall). `AddMemoryVerification()` shows a judge the candidate headlines before the limit is applied, so an
  answer the ranking buried gets promoted; a failure leaves the ranking untouched. The measured effect, and
  how to choose a judge: `docs/memory.md`.
- **Name an LLM client per use.** `AddTextClient("memory-fast", c => c.UseProviders("ollama", "openai"))`,
  resolved through `ITextClientFactory`. A name selects backends — its **fallback order** — never
  permissions: every named client carries the same cache, budget, rate limit and refusal screening.
- **The memory store is bounded out of the box:** the default `MemoryEvictionPolicy` keeps a **500-entry
  per-scope FIFO cap**. Change it with `ConfigureMemoryEviction(p => …)` (count cap, default TTL, character
  budget, FIFO or LRU) or the `LYNTAI_MEMORY_*` env family; `MemoryEvictionPolicy.Manual` hands size back to
  your app. `AddMemoryPruneJob(cron, …)` is the scheduled form, a recurring durable job that removes expired
  and aged-out entries from the keyword store and, for a job naming a `taskKey`, prunes every memory engine
  that supports pruning too (your app owns the pump).
- **Curated memory catalog** (`ICuratedMemoryStore`) for hand-managed context: entries grouped by `Kind`,
  each enabled, disabled and edited individually, an app-owned `Metadata` map queryable by exact key/value,
  keyword `SearchAsync`, and per-kind prompt sections from `CuratedMemorySections.Compose`. Every entry
  carries a `Revision`, and when two writers may edit one entry, the `IRevisionedCuratedMemoryStore` overload
  `UpdateAsync(id, expectedRevision: entry.Revision, …)` refuses a stale edit as `CuratedUpdateOutcome.Stale`
  rather than overwriting the other writer's; every shipped store implements it.
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
- **Mix storage backends per domain:** the DI container is the registry — `UseSqliteStorage(path)` for most
  domains, then override one (`services.AddSingleton<IMemoryStore>(...)`, last registration wins). Among the
  `Use*Storage` helpers the FIRST wins: `UseFileSystemStorage(o => o.Root = …)` before `UseSqliteStorage(path)`
  puts its six domains in files and leaves the rest to SQLite.

## Structured output

```csharp
var reply = await llm.CompleteJsonAsync(new TextRequest
{
    Messages = [TextMessage.User("Summarize as JSON.")],
    JsonSchema = """{"type":"object","properties":{"summary":{"type":"string"}}}""",
});
// reply.Verdict == Ok guarantees reply.Text parses as a single JSON object
// (tolerant extraction from prose/fences; a trailing comma or stray comment is repaired in CODE,
//  so only what code cannot fix costs a retry — a truncated object — else Failed. Design §6.)
```

**Not every backend holds the model to the schema.** The HTTP and Ollama backends send it on the wire; a CLI
receives it as prompt text and LLamaSharp drops it, so a fallback onto one still parses but was never
constrained. Set `RequireNativeSupport = true` on the request and the router skips any candidate whose
`ProviderCapabilities` do not declare `SupportsStructuredOutput` — or, for a request carrying `Tools`, native
tool calls — and answers `Unsupported`, naming each skipped candidate, when none can (**D205**). Unset, every
candidate is asked, as before.

## Governance: response cache, usage budget, rate limit

Three opt-in layers on the single front door, so the tool loop, orchestrator and scorers all pass through
them:

```csharp
services.AddLyntai(cfg => cfg
    .AddOpenAiProvider(apiKey: "…")
    .AddResponseCache(c => c.Ttl = TimeSpan.FromHours(6))   // defaults: 1h TTL, 1000 entries
    .AddUsageBudget(b =>
    {
        b.MaxCostUsd = 20.00;                              // global ceiling
        b.PerConsumer["scoring"] = new(MaxCostUsd: 2.00);  // a tighter cap for one consumer
    })
    .AddRateLimit(r =>
    {
        r.PermitsPerSecond = 10;
        r.Burst = 20;                                      // allow a burst after idle
        r.PerConsumer["scoring"] = new(PermitsPerSecond: 2);
    }));
```

- **The cache** is keyed by a stable hash of the output-determining request fields and the model the call
  resolves to — `Consumer` itself excluded, so two consumers issuing the same request to the same model share a
  hit; a live route, or a client's candidate list when a candidate pins a model, joins the key — and holds only
  clean `Ok`, non-streaming completions without native tools. **When consumers are tenants, set
  `c.PartitionByConsumer = true`**: each consumer then gets its own entries, on every backend (`docs/DECISIONS.md`
  **D208**).
- **The budget** refuses a completion over a cap with `Verdict == Refused`, calling no provider. The ceiling
  is **soft**: the call that crosses a cap still runs, the next is refused. `IUsageTracker.TotalAsync()` and
  `ResetAsync()` read and reset spend at runtime. **For a hard cap, set `b.ReserveCostUsd`** (or `ReserveTokens`)
  to the most one call can cost: each call then holds that much before it runs, admitted only if spend plus every
  call in flight still fits, and settles to what it reported afterwards, whether it succeeds, fails or is
  cancelled (`docs/DECISIONS.md` **D206**). Every shipped tracker can reserve (`IReservingUsageTracker`); a custom
  one that does not keeps the soft check.
- **The rate limit** is a token bucket: over the rate a call waits briefly for a permit, then is refused with
  `RateLimited`. Register your own `IRateLimiter` for a limiter shared across processes.

They compose **cache outermost, rate-limit innermost**, so a cached hit spends nothing. `UseSqliteResponseCache()`
and `UseSqliteUsageTracking()` (or their Postgres twins) persist the cache and spend across restarts, or register
your own `IResponseCache` / `IUsageTracker` for a shared store. **The same wallet reaches embeds and reranks**
(`docs/DECISIONS.md` **D163**), and the library stamps its own traffic: memory's embeds, semantic recalls and
scoring verification bill to `"memory"`, the tool selector's embeds to `"agent"`, so `b.PerConsumer["memory"]`
fences memory spend, and a reached cap degrades a recall through its fail-open paths rather than failing it.

**A layer of your own goes on the same chain:** `AddFrontDoorDecorator(order, (sp, inner) => …)` folds PII
redaction, request logging or a bespoke cache in beside the built-ins — higher `order` = further out, with
the built-ins at 5 (rate limit) / 10 (budget) / 20 (cache) / 30 (call tracing). Reach for it *instead of* pre-registering an
`ITextClient`, which discards every front-door decorator with no error at all. Taking an order another
decorator already holds — a built-in's included — throws at composition, naming the slot and both
registrations. **Derive the layer from `DelegatingTextClient`**, which forwards `GetCapabilitiesAsync`; a
layer answering null there puts every tool loop above it on the prompt path.

**Persisting a governance store needs `StorageFeature.Governance`** (the default `StorageFeature.All`
includes it): the SQL response cache, usage tracking and SQLite vector store reject a feature subset without
it at `AddLyntai`, wherever Lyntai runs the migrations.

## Named memory engines

`IMemoryStore`, `ISemanticMemory` and `ICuratedMemoryStore` are each a single unnamed service. A memory
**engine** is a named memory system; several coexist and resolve by name, the way `IHttpClientFactory`
resolves clients (**D39**).

```csharp
services.AddLyntai(cfg => cfg
    .UseSqliteStorage("app.db")
    .AddMemory());        // one working engine named "default": the chat reads from it and remembers into it
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
    .UseMemoryComposer("chat"));                // the engine the chat reads from and remembers into

var memory = sp.GetRequiredService<IMemoryEngineFactory>();
await memory.Get("project").RememberAsync(new MemoryWrite("proj", "code", "prefers terse commits"));
var recall = await memory.Get("project").RecallAsync(new MemoryQuery("proj", "code", "commits"));
// recall.Ran says which tiers actually ran, so an empty tier differs from an absent one
```

A blend **is** an engine, and its members are addressable too (`Get("project/glossary")`). **A write goes to
ONE member by default**, the first that can hold its grade — right for `project` above, wrong for `chat`, whose
members both hold associative material, hence `FanOutWrites()` (**D85**). Curated members are
*authoritative*: never decayed or shortened, holding a **reserved** slice of the budget in their own labelled
prompt section. A member no write can reach is reported at startup (`MemoryEngineBuilder.StrictWiring()`
makes that a failure).

## Graph memory — forgetting, relinking, and a cheap first load

`UseGraph()` is a memory engine shaped more like recall than like a log. Entries **decay** unless they get
used, **connect** to whatever was recalled beside them, and come back as one-line **headlines** that expand
on demand — so a session opens on a small index instead of paying for the whole store on every turn.

```csharp
services.AddLyntai(cfg => cfg
    .UseInMemoryStorage()
    .AddMemoryEngine("project", e => e.UseGraph()));

var memory = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
await memory.RememberAsync(new MemoryWrite("proj", "code",
    "The build gate is node devtools/dev.mjs verify."));

var recall = await memory.RecallAsync(new MemoryQuery("proj", "code", "gate"));
// headlines only — Content is null until you ask for it
foreach (var hit in recall.Items)
    Console.WriteLine($"{hit.Headline}  →{hit.Degree}  r={hit.Retrievability:F2}");

var detail = await ((IExpandableMemory)memory).ExpandAsync(recall.Items[0].Reference);
// full content of that entry, plus its neighbours' headlines
```

A **walk** repeats that pair — recall, expand what looked worth it, then whatever *that* turned up (**D100**,
**D102**) — and your `break` is the stop condition:

```csharp
await foreach (var step in engine.WalkAsync(new MemoryQuery("proj", "code", "gate", Limit: 20)))
{
    Console.WriteLine($"step {step.Number}: {step.NewItems.Count} new, {step.UpgradedCount} upgraded");
    if (step.Items.Count >= 30) break;   // you decide how far to go
}
```

`step.Items` is everything held so far; an entry held as a headline is **upgraded** in place when a later step
expands it. Four rules decide how the engine behaves:

- **Decay is interference, not the clock** (**D40**). An entry ages as the engine takes writes after it was
  last used — counted per write by default, damped so a burst cannot erase what came before
  (`ContentSizeAgePolicy` counts volume, `ElapsedAgePolicy` real time). A memory nobody touches keeps
  everything, a recall refreshes what it returned, and it is all computed at read time: no background job.
- **Decay buries; it does not cut** (**D41**). An entry is hidden because something outranks it, never because
  it crossed a threshold, and it stays reachable directly or through a neighbour, its `Retrievability` saying
  how faint it has become. Nothing is deleted unless you call `PruneAsync`.
- **Grades reserve slots** (**D56**). An `Authoritative` entry never decays, is never shortened, and takes a
  reserved slot within your `Limit`, so only another exact fact displaces it
  (`GraphMemoryOptions.AuthoritativeReserve` bounds how many slots).
- **Ranking and forgetting are seams.** `ReciprocalRankFusionPolicy` ranks by default (**D82**), beside
  `MultiplicativeRankingPolicy` and `CompositeRankingPolicy`; `UseGraph(ranking: …)` picks one per engine and
  `MemoryQuery.RankingPolicyName` one per call. `DsrRetrievability` (FSRS's curve) is the only shipped
  forgetting curve (**D49**); at the defaults a recall resets an entry's age without lengthening its
  half-life (`DsrOptions.ReinforceGain = 0`, **D54**).

Entries returned together get linked, so a later recall spreads through those links to material it never
literally matched. Graph memory persists in memory, on SQLite and Postgres, and in files. `docs/memory.md` is
the full guide: configuration, the model-backed steps with their measured costs, and what surprises.

## Semantic memory

For meaning-based recall, bring an embedding model and use `ISemanticMemory`: facts are recalled by cosine
similarity. An embedding model is its own backend — `Produces` says so, and that field picks the route — so a
server hosting a chat model too is registered twice, under two ids:

```csharp
services.AddLyntai(cfg => cfg
    .AddHttpProvider("local-chat", o =>
    {
        o.BaseUrl = "http://localhost:11434";
        o.Model = "llama3.1";
    })
    .AddHttpProvider("local-embed", o =>
    {
        o.BaseUrl = "http://localhost:11434";     // same server, different backend
        o.Model = "nomic-embed-text";
        o.Produces = ProviderKinds.Vector;        // an embedder: an Ollama root embeds on /api/embed
    })
    .AddSemanticMemory());                        // states the intent — see below
    // …or bring your own: .AddProvider(_ => myBackend, declares)  // an IModelProvider that implements IVectorProvider

var memory = sp.GetRequiredService<ISemanticMemory>();
await memory.RememberAsync(taskKey: "support", scope: "faq", "You can cancel your subscription anytime.");
var hits = await memory.RecallAsync("support", "faq", query: "how do I stop paying?", k: 5);
// hits ranked by similarity, each with a Content + cosine Score
```

Vector work ROUTES over every backend that produces vectors, so two of them are failover (**D151**/**D152**).
`AddSemanticMemory()` states the intent, turning a missing backend into a startup failure; a backend added
through `AddProvider(_ => backend, declares)` must **pass that second argument**, because a factory cannot be
inspected before it runs. Vectors live in a swappable `IVectorStore`: in memory by default,
`UseSqliteVectorStore()` in SQLite, `UsePostgresVectorStore()` in **pgvector** (SQL-side top-k), or your own.
All three shipped stores read a stored vector back by id (`IReadableVectorStore.GetAsync`, bit-identical), and a
search narrows to ids you keep yourself with `SearchAsync(collection, query, k, new VectorSearchFilter { Ids = … })`
(`docs/DECISIONS.md` **D194**). After changing the embedding model, a graph memory engine re-embeds in place with
`ReindexAsync` (`docs/memory.md` §9).

Registering an embedder also upgrades the **chat orchestration**: the default composer blends the keyword store
and semantic memory — keyword hits first, then semantic ones, deduped — and writes each exchange to both, so a
later turn recalls earlier ones by meaning as well as by keyword.

## Observability

Lyntai emits OpenTelemetry GenAI-convention telemetry — the schema `Microsoft.Extensions.AI`'s
`OpenTelemetryChatClient` uses — and nothing is emitted unless you subscribe:

<!-- compile-skip: the wiring is on OpenTelemetry's own TracerProviderBuilder/MeterProviderBuilder. No
     compile-given can declare them: this library takes no OpenTelemetry dependency (it emits over
     System.Diagnostics), so the types are not in the compilation at all. -->
```csharp
tracerProviderBuilder.AddSource(LyntaiDiagnostics.ActivitySourceName);        // "Lyntai.Inference" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.MeterName);                   // duration, token usage,
                                                                              // time_to_first_chunk

// image/video/audio/3d renders emit on a second source/meter:
tracerProviderBuilder.AddSource(LyntaiDiagnostics.GenerationActivitySourceName);  // "Lyntai.Generation" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.GenerationMeterName);         // render duration + reported cost

// the agentic subsystems (tool loop, durable jobs, guards) emit on a third source/meter:
tracerProviderBuilder.AddSource(LyntaiDiagnostics.AgentActivitySourceName);   // "Lyntai.Agents" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.AgentMeterName);              // tool/job/guard metrics
```

`chat {model}` spans carry `gen_ai.system` (provider id), `gen_ai.request.model`, token usage and
`error.type` (the verdict), one per provider attempt. Each sits under the call's own `lyntai.call` span — opened
by every text front door and by the generic router, tagged `lyntai.consumer`, `lyntai.call.operation` and
`error.type` when the caller got a failure — so a fallback reads as one call with two attempts, and a cache hit as a
call with none (`docs/DECISIONS.md` **D207**). No span records the request or reply text. A `tool_loop` span nests
one `execute_tool {name}` span per call. `ITraceService`
is separate and **app-driven**: a durable, queryable run history you record yourself (`Begin(sessionId, mode)`,
`recorder.Record(step)`) into the wired `ITraceStore`.

**`AddTextCallTracing()` records one for you**: a step per front-door call — consumer, usage, duration, verdict,
model — then the registered scorers `TextCallTracingOptions.Scorers` selects, the deterministic ones by default,
saved under the trace's session id (`docs/DECISIONS.md` **D193**). Each call is a trace of its own unless it runs
inside `using (TextCallTracing.Into(recorder))`, which puts a run of calls on your recorder and saves each call's
scores under `{SessionId}#{n}`. Cached and streamed calls are traced, a scorer's own model call never is, and the
reply's text is stored only with `RecordText`. Tracing never fails a call, but it runs on the call's path: the
reply waits for the step and the scores, so a slow store or an opted-in LLM scorer adds to every traced call.

## Bring your own resources

Lyntai defines the interfaces; your app owns the resource lifecycle wherever that matters.

<!-- compile-skip: a tour of BYO seams. compile-given was measured and rejected here: IProcessRunner
     alone is three eight-parameter methods, and with MyCustomProvider (IModelProvider, four members) and a
     connection factory the context runs past 26 lines for a 16-line sample — a whole program, not a few
     declarations. -->
```csharp
services.AddLyntai(cfg =>
{
    // Provider presets (or the generic AddHttpProvider, or your own IModelProvider):
    cfg.AddOpenAiProvider(apiKey, model: "gpt-4o-mini");
    cfg.AddLlamaProvider(model: "gemma-3-4b");      // llama.cpp llama-server, :8080
    cfg.AddOllamaProvider(model: "llama3.2:3b");
    cfg.AddProvider(_ => new MyCustomProvider());          // BYO IModelProvider

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

Owning the schema means running Lyntai's migrations yourself (`MigrationRunnerService.MigrateUp` /
`MigrateUpAsync`). Anything you register wins over Lyntai's default (the defaults use `TryAdd`), and every
storage domain is an interface you can implement wholesale. A BYO `IProcessRunner` owes `StreamBytesAsync`
only if a byte-streaming backend (piper) routes through it; its default refuses rather than decoding binary
as text (**D165**).

**The interfaces the library itself decorates.** A BYO implementation of one of these can end up wrapped by
a class the library ships, and a BYO decorator joins a chain beside them. A decorator forwards every member
to the instance it wraps; where a member can go unanswered, its own documentation says what to return, under
**Implementing it**. A member added to one of these takes no default body (**D67**), so a decorator that
misses it fails to compile instead of silently running a default. The one older default body named below
falls back correctly, only more slowly. `DecoratedInterfaceTests` holds this table to the tree: a newly
decorated interface fails the tests until it is listed here, and so does a row nothing decorates any more.

<!-- decorated-interfaces:begin -->
| interface | decorated by | what a decorator owes it |
| --- | --- | --- |
| `ITextClient` | `DelegatingTextClient` (derive from it), `BudgetedTextClient`, `CachingTextClient`, `RateLimitedTextClient`, `GuardedTextClient`, `RefusalScreeningTextClient` | every member, `GetCapabilitiesAsync` included |
| `IMediaRouter` | `BudgetedMediaRouter`, `RateLimitedMediaRouter` | all three doors, each one governed: the compiler cannot tell a governed door from a pass-through |
| `IAgentSession` | `RetryingAgentSession` (`AddAgentRetry`), `RecordingAgentSession` | `StreamAsync`, forwarding the options as given or with a resumed turn's prompt and token |
| `IConversationStore` | `EnrichingConversationStore` | every member |
| `IDbConnectionFactory` | `LazyMigratingConnectionFactory` | `Open`, and `OpenAsync`, whose default loses the inner factory's async open |
| `IMemoryEngine` | `CompositeMemoryEngine`, over its members | every member |
| `IMemoryAgePolicy` | `BurstDampenedAgePolicy` | every member; `Kind` says when not to forward |
| `IMemoryRankingPolicy` | `CompositeRankingPolicy`, over two | `Rank` |
| `IMemoryRetrievabilityPolicy` | `ModulatedRetrievability` | every member; `Provenance`, `CandidateCutoff` and `DerivedGrade` say how |
| `IVectorStore` | the vector-space guard `UseVectorSpace` composes (**D199**) | every member, the filtered `SearchAsync` included, whose default ranks the whole collection; and the optional halves the store has, never one it lacks |
<!-- decorated-interfaces:end -->

## Backend self-maintenance: version · upgrade · pinned install · auth

`ProbeAsync` is on `IModelProvider` itself (**D127**), so *every* backend answers "is this usable right now?"
without running a completion. Three **optional** capabilities — `IProviderUpdater`,
`IProviderVersionInstaller`, `IProviderAuth` — are discovered by pattern-matching, and all **fail safe**: an
absent, stalled or erroring backend is reported, never thrown. An HTTP backend answers by ASKING its server
— one GET of its model listing (`/v1/models`, Ollama's `/api/tags`), which generates nothing — so a setup
screen's "test connection" learns whether the server is reachable, whether it accepts the key, and what it
serves (`probe.Models`). A server with no listing route is reachable, and a configured model it does not list
is reported in `probe.Detail` rather than as down, since llama-server serves whatever is loaded.

```csharp
foreach (var provider in serviceProvider.GetServices<IModelProvider>())
{
    var probe = await provider.ProbeAsync(ct);   // NO completion is run: no tokens, no model call
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

`ClaudeCliProvider` implements all three: `IProviderAuth.StatusAsync` answers "signed in, and as whom?" as a
VALUE, `LoginAsync` drives the backend's own login, and `IProviderVersionInstaller.InstallAsync` pins a
known-good version. `codex` cannot install a *named* version, so `CodexCliProvider` does not claim to.
`Model` is null against today's claude CLI, which has no turn-free way to report it; read the model a turn
used from `AgentStreamEvent.UsageFinal.Model`. **Nothing is guessed**: a CLI reads an unknown token as a
*prompt* and spends a turn on it, so every maintenance question is a flag or a documented subcommand, and a
free-form value the backend does not recognize is refused. Lyntai drives the tooling a backend ships and
never stores credentials or downloads a backend (`docs/DECISIONS.md` D20).

## CLI backends: `claude`, `codex`, or your own (`CliProviderEngine` + an `ICliBackend`)

```csharp
services.AddLyntai(cfg => cfg
    .AddClaudeCliProvider()                       // the authenticated `claude` CLI
    .AddCodexCliProvider()                        // the authenticated OpenAI `codex` CLI
    .UseDefaultCandidates("claude-cli", "codex-cli"));   // one falls over to the other
```

**Portable installs.** If your app ships its own copy of a CLI, pass the path — and, where the backend has
one, that install's own home directory, so it never touches the machine-wide install's state:

<!-- compile-given: string portableHome;
     string bundledClaudePath; -->
```csharp
cfg.AddCodexCliProvider(
    command: Path.Combine(AppContext.BaseDirectory, "tools", "codex.exe"),
    environment: new Dictionary<string, string?> { ["CODEX_HOME"] = portableHome });

cfg.AddClaudeCliProvider(command: bundledClaudePath);   // …and the same value for AddClaudeCliAgentSession
```

`IsAvailable` then checks the file is actually *there*, so a missing copy is skipped by the router rather than
discovered as a failed turn.

**A one-shot claude call's own flags, by consumer.** The library's own model seams call under their consumers —
`"memory"` for annotation and verification, `"scoring"` for the LLM scorers — so a configured backend says how each
consumer's completions are spawned: the request's `Consumer` entry, then `"default"`, then the plain argv.
`AskUserQuestion` is always denied, and a `DisallowedTools` list adds to it. `Tools` is the allow-list instead: the
only built-in tools the call has, so a tool a later CLI adds is never granted by omission, and `[]` leaves none.
Deny by default and list the consumers that need tools, as the tool host's map does below:

```csharp
cfg.AddClaudeCliProvider(new ClaudeCliBackend
{
    CompletionByConsumer = new Dictionary<string, ClaudeCompletionOptions>
    {
        ["default"] = new() { Tools = [] },   // memory, scoring and every unlisted consumer: no built-in tool
        ["study"] = new() { DisallowedTools = ["PowerShell", "Monitor"] },   // the CLI's set, less a shell
    },
});
```

`Tools` governs the CLI's BUILT-IN tools only: a hosted call's MCP tools still reach it, and which of yours a consumer
is handed is `McpToolHostOptions.ToolsByConsumer`'s to say, so denying by default takes `[]` in both maps.

`SettingSources` and `StrictMcpConfig` scope what a call LOADS: `[]` loads no settings source — no project
`CLAUDE.md`, even one in a parent directory, which the CLI walks up to — and strict MCP config starts no server from a
project `.mcp.json`. `ClaudeAgentOptions` takes the same pair for an agent run. `SettingsPath` hands a call a command-line settings file, which
outranks the project and local scopes — the way to put a setting on every call, one-shot and agent alike. The CLI
applies only the last `--settings` it is given, so a call a tool host serves gets this file merged into the host's
allow-list rather than a second flag.

**Writing your own.** The rules every CLI-agent backend must get right — no shell, a neutral working
directory, an *inactivity* clock, verdicts from the shared classifier, empty output as a failure, exactly one
terminal stream chunk — live once, in `CliProviderEngine`, and a new CLI supplies only its vocabulary:

<!-- compile-given: static class MyWireFormat { public static CliOutputEvent Read(string line) => CliOutputEvent.Ignored; } -->
```csharp
public sealed class MyCliBackend : CliBackendBase
{
    public override string Id => "my-cli";
    public override string DefaultCommand => "mycli";
    public override IReadOnlyList<string> CommandEnvironmentVariables => ["LYNTAI_PROVIDER_CMD", "MYCLI_CMD"];
    // `toolHostArgs` point the CLI at Lyntai's MCP host: place them where it reads options, never after a positional
    public override IReadOnlyList<string> BuildCompletionArgs(TextRequest r, IReadOnlyList<string> toolHostArgs) =>
        ["exec", "--json", .. toolHostArgs];
    public override CliOutputEvent ParseLine(string line) =>   // → Content / Result / Failure / Ignored
        MyWireFormat.Read(line);

    // claim an optional capability ONLY where the real binary has it — the base claims none by default
    public override IReadOnlyList<string>? UpdateArgs => ["update"];
}
```

…plus a provider of forwarding members that composes the engine with it (`ClaudeCliProvider` is exactly that);
the rules are `.agents/knowledge/extending-lyntai.md` §Add an LLM provider.

## Generation: image · video · audio · 3d (`Lyntai.Generation`)

The LLM front door's idea, for generated artifacts: you register backends, Lyntai routes across them. It is
a **platform, not an engine** — a backend you choose produces every pixel and sample — and `Kind` is an open
string. `docs/generation.md` is the full guide.

<!-- compile-given: string key; -->
```csharp
services.AddLyntai(cfg => cfg
    // hosted: an OpenAI-shaped images API
    .AddOpenAiImageProvider(o => { o.ApiKey = key; o.Model = "gpt-image-1"; })
    // local: a Stable Diffusion WebUI on this machine
    .AddAutomatic1111Provider(o => { })
    .UseDefaultMediaCandidates("openai-images", "a1111"));
```

Every option has a default, so a registration sets only what differs; a blank base URL reports
`NotConfigured`. A render backend of your own is `AddProvider(sp => …, declares: …)` plus `AddMediaRouting()`.

<!-- compile-given: IReadOnlyList<ProviderCandidate> candidates;
     byte[] sourcePng;
     void Save(byte[] data) { } -->
```csharp
var result = await router.GenerateAsync(candidates, new MediaRequest
{
    Kind = ProviderKinds.Image,                 // open string: image / video / audio / 3d / whatever's next
    Prompt = "the same room, at night",
    Inputs = [MediaInput.Init(sourcePng, "image/png")],   // a named factory: the role cannot be omitted
    Options = new Dictionary<string, string> { ["size"] = "1024x1024" },
});
if (result.IsOk) Save(result.Artifacts[0].Data!);
```

Build inputs with the **named factories** (`Init` / `FirstFrame` / `Reference` / `Voice`, or `From(role, …)`),
never the positional constructor, whose role comes last and binds silently to the wrong slot (**D28**). The
router **skips a candidate that can't serve the request** before spending anything, across **three delivery
modes** declared in `ProviderCapabilities.Operations`: inline, an async job (`IMediaJobProvider`: submit →
poll → fetch) and streaming. **Chaining is first-class**: `RunPipelineAsync` feeds each stage's artifact into
the next over the inline door, and a pipeline with a QUEUED stage runs as a durable job
(`GenerationPipelineJobHandler`) that checkpoints each submission before its first poll and each result before
delivery, so a restart or a throwing sink does not pay for a render twice — bar a crash in the instant before a
checkpoint, or a result over `GenerationPipelineJobOptions.MaxCheckpointBytes` (`docs/generation.md` §7 and §8).

| Backend | Delivery | Standing |
|---|---|---|
| `OpenAiImageProvider` | Inline | Ported from a production implementation; not yet measured against OpenAI's current GPT-image models |
| `Automatic1111Provider` | Inline | Ported from a production implementation. Not running (nothing listening) is **Failed**, a down host like any other, and so is a WebUI that drops a render mid-response |
| `ComfyUiProvider` | **Job** | Measured against a live server: image, video and mesh workflows |
| `LocalDiffusionProvider` | Inline | A local `sd-cli` subprocess, measured end to end (txt2img and img2img) |
| `FalProvider` | **Job** | *Never called: written from fal.ai's public docs; no maintainer holds an account* |
| `PiperProvider` | Inline + **Stream** | Local piper TTS; streamed PCM measured against a real engine |

**`FalProvider` was written from fal.ai's public queue documentation and has never been called, because no
maintainer holds a fal account.** Its URL segments, status vocabulary, cost fields, `ErrorField`, `AuthScheme`
and `QueryParameters` are options on `FalOptions`, so a host that finds a different wire corrects it in
configuration. A free Hugging Face account can verify the wire through the Hugging Face router, which proxies
fal's queue: `BaseUrl = "https://router.huggingface.co/fal-ai"`, `AuthScheme = "Bearer"`, an `hf_` token as the
key and `QueryParameters["_subdomain"] = "queue"` — a route Hugging Face documents and nobody has yet run from
this library. fal's documented results carry no cost field, so a spend cap never sees a fal render unless
`CostFields` names one that its results do carry.

**Hosted fallbacks beside fal.** A survey of hosted vendors' public documentation found three candidates —
WaveSpeedAI, OpenRouter (video) and Replicate — and the library ships none of them, nor has it called any.
Its recommendation is to extract a shared internal queue engine from `FalProvider` only when a second vendor is
actually written; until then a fallback is a backend of your own (`docs/generation.md` §4 says what it must
implement).

`AddGenerationTools(consumer)` exposes the platform to an agent as tools (`generate_backends`, `generate`,
`generate_submit` / `generate_status` / `generate_fetch`); every render they start bills to `consumer`
(`"agent"` by default), and a model says what a source image is to the render with `imageRole` (`init`,
`first-frame` or `reference`). **Not in scope, by design:** generation itself, downloading engines or model
weights, hosting a webhook endpoint, storing artifacts, or holding your credentials (`docs/DECISIONS.md` D20
and D24).

## When your users own the backend configuration (`Lyntai.Inference`)

If an **end user** owns backend configuration — settings that change at any moment, several configurations
of one backend live at once — hand `ITextRouterFactory` or `IMediaRouterFactory` a `ProviderKey` per
configuration and a way to build each backend. `UseProviderPool()` (the default) reuses a backend while its
key is unchanged, `UseTransientProviders()` builds one per call, and **cooldown and admission are keyed on the
configuration, not the backend id**, so one tenant's rate limit never benches another's (`docs/DECISIONS.md`
D30). A worked example: `docs/generation.md` §10.

**Those routers carry no governance** — the budget, cache, rate limit and refusal screening live on the composed
`ITextClient`. When the text providers themselves are what users edit, `UseTextProviderRegistry()` instead lets
the DEFAULT client route over them: `ITextProviderRegistry.Register(new(key, create))`, `Unregister(id)` and
`SetDefaultCandidates(…)` take effect on the next call, and everything folded onto that client governs them. The
provider is built at `Register`, so a mistake fails there. A named client keeps the providers it was composed
with (`docs/DECISIONS.md` **D193**).

## Bridging a backend Lyntai has no provider for (`AddBridgeProvider`)

`AddHttpProvider` reaches any endpoint speaking OpenAI's schema, and `AddOllamaProvider` Ollama's native one.
For anything else — its own wire format, an in-house service, an SDK you already use — **a bridge is a
lambda**, and the library takes no dependency on whatever you wrapped:

<!-- compile-given: static class Vendor { public static System.Threading.Tasks.Task<string> AskAsync(string prompt, System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.FromResult(""); } -->
```csharp
services.AddLyntai(cfg => cfg
    .AddBridgeProvider("vendor", async (req, ct) =>
    {
        try
        {
            var text = await Vendor.AskAsync(req.Messages[^1].Content, ct);
            return new TextResponse(text, ProviderVerdict.Ok);
        }
        catch (HttpRequestException ex)
        {
            // a VERDICT, not a throw — it is what lets the router advance to the next candidate
            return new TextResponse("", ProviderVerdict.Failed, Detail: ex.Message);
        }
    })
    .UseDefaultCandidates("vendor"));
```

From there it is a backend like any other, **the provider timeout included**: `LyntaiOptions.ProviderTimeout`
(or the request's own) bounds a completion as a whole and a stream by inactivity, answers `Timeout` so the
router advances, and abandons a delegate that ignores its token. **Return a verdict rather than throwing**: a
throw is classified conservatively, while a verdict says what happened
(`ProviderVerdictClassifier.FromHttpFailure(status, body, hasCredentials)` maps a response for you). For an
`IChatClient` in particular, [`docs/extensions-ai.md`](extensions-ai.md) has the whole mapping. **A bridge declares only what you hand it a delegate for**: omit
`stream` and no router asks it to stream, and pass `capabilities` to declare more than the defaults — tool
calls, a model list, declared limits. **A bridge answers text only**, and refuses any other `Produces` at the
call: an embedder or reranker of your own is an `IModelProvider` that implements `IVectorProvider` or
`IScoreProvider`, registered with `AddProvider(_ => backend, declares)`.

## Local in-process inference (`Lyntai.Providers.LlamaSharp`)

Run a GGUF model in-process via LLamaSharp — no network, no key, no subprocess. Reference the
`LLamaSharp.Backend.*` that matches your hardware alongside `Lyntai.Providers.LlamaSharp`:

```xml
<PackageReference Include="Lyntai.Providers.LlamaSharp" />             <!-- version: the current release -->
<PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.27.0" />  <!-- or .Cuda12 / .Vulkan / .Metal -->
```

```csharp
services.AddLyntai(cfg =>
{
    cfg.AddLlamaSharpProvider("models/Phi-3-mini-4k-instruct-q4.gguf", o =>
    {
        o.GpuLayerCount = 0;      // 0 = CPU; raise to offload layers to the GPU
        o.ContextSize = 4096;     // null = the model's own trained maximum
    });
    cfg.UseDefaultCandidates("llamasharp");   // the registration default id
});
```

The model loads lazily and generations are serialized; as just another `IModelProvider` it fits anywhere in
a candidate list — a hosted model first, `"llamasharp"` as an offline backstop.

## Tool-calling (`Lyntai.Agents`)

Give the model tools and let it work in a loop. `IToolLoop` runs over the `ITextClient` front door, so
it works with **any** provider (CLI, HTTP, bridged, local) — no native tool-calling required.

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
var result = await toolLoop.RunAsync(new TextRequest
{
    Messages = [TextMessage.User("What should I wear in Paris today?")],
});
Console.WriteLine(result.Answer);          // the model's final answer after any tool round-trips
foreach (var step in result.Steps)         // every tool call it made, for tracing
    Console.WriteLine($"{step.Tool}({step.ArgumentsJson}) -> {step.Result}");
```

The loop runs up to `ToolLoopMaxIterations` (default 8), using **native** function-calling where the backend
that would serve has it and a **prompt protocol** over the text contract elsewhere — same `ITool`s either way.
An unknown or throwing tool becomes a recoverable `error: …` observation; a refusal or all-providers-down
verdict surfaces on `result.Verdict`.

**Approving a call before it runs.** An `IToolApprover` sees every tool call — its tool name, its arguments,
the request's `Consumer` and, on the native path, the model's call id — before the tool runs, on the native
path, the prompt protocol and the hosted MCP endpoint below alike. Register one with
`cfg.AddToolApprover<WritesOnlyForStudy>()`; every registered approver must approve, in registration order.
A refusal does not end the run: the model receives `error: tool call refused by approval: <reason>` as the
call's result and carries on. An approver that throws refuses the call (`docs/DECISIONS.md` D210).

```csharp
public sealed class WritesOnlyForStudy : IToolApprover
{
    public Task<ToolApprovalResult> ApproveAsync(ToolApprovalRequest call, CancellationToken ct = default) =>
        Task.FromResult(call.ToolName.StartsWith("write_", StringComparison.Ordinal) && call.Consumer != "study"
            ? ToolApprovalResult.Deny("writing files is open only to the study assistant")
            : ToolApprovalResult.Approve);
}
```

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

**Hosting your tools for a CLI agent** — the reverse direction. A CLI running its own agent loop reaches
custom tools only over MCP, so this hosts your `ITool`s as a localhost-only MCP server for the length of each
CLI call and passes the CLI the flags that point it there:

```csharp
services.AddLyntai(b => b
    .AddClaudeCliProvider()
    .AddTool(_ => new FunctionTool("get_weather", (a, ct) => Task.FromResult("""{"tempC":21}"""), "Current weather"))
    .AddMcpToolHost(new ClaudeCliMcpConnector())   // hosts the tools over MCP for the CLI
    .UseDefaultCandidates("claude-cli"));
// var reply = await llm.CompleteAsync(...);  → the CLI calls get_weather and answers
```

**Per call, by consumer.** `McpToolHostOptions.ToolsByConsumer` says which tools a spawn hosts, resolved as
`TimeoutByConsumer` is: the request's `Consumer` entry, then the `"default"` entry, then every registered tool. An
empty list hosts none and starts no host — the fast plain call — and names host only those tools. **Set a map
even if only your own calls use tools:** the library's model seams spawn the CLI under THEIR consumers —
`"memory"` for memory annotation and verification, `"scoring"` for the LLM scorers — so with no map each of those
calls hosts every registered tool and starts a host for it, while an annotation prompt carries stored content
your users wrote. Deny by default, and list the consumers that need tools; a claude backend's own `"default"` entry
with `Tools = []` (above) keeps the CLI's built-in tools from those calls too:

```csharp
services.AddLyntai(b => b
    .AddClaudeCliProvider()
    .AddTool(_ => new FunctionTool("read_file", (a, ct) => Task.FromResult("…"), "Read a project file"))
    .AddMcpToolHost(new ClaudeCliMcpConnector(), o =>
    {
        o.ToolsByConsumer["default"] = [];            // memory, scoring and every unlisted consumer: no tools, no host
        o.ToolsByConsumer["study"] = ["read_file"];   // only this consumer's calls host read_file
    }));
```

A provisioner of your own sees the call through `ProvisionAsync(CliToolRequest, ct)` (`docs/DECISIONS.md` D190).

**Which** CLI connects and **how** it is told to is an `IMcpCliConnector` (flag names plus config-file
shapes), so another CLI is one small class (`.agents/knowledge/extending-lyntai.md` §Add a CLI tool-hosting
connector), registered keyed on its `ProviderId` beside the others. The loopback `HttpListener` it runs
during each call is a deliberate, scoped exception to Lyntai's otherwise host-free design.

## CLI-agent session vs `IToolLoop` (`IAgentSession`)

When the external agent drives its OWN tool loop out-of-process (the `claude` or `codex` CLI), use
`IAgentSession`: observe its transcript (`AgentStreamEvent`), gate it read-only (plan) or write (execute) with
`AgentToolPolicy`, and resume it across a human confirmation gate with its `ResumeToken` — live through
`StreamAsync`, or folded to a result through `RunAsync(onEvent)`. Choose `IToolLoop` instead when you supply
the tools and want Lyntai to call them.

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

**Give the agent your app's own tools, on either backend**: `AgentSessionOptions.McpServers` takes
`AgentMcpServer.Stdio(name, command, args, env)` for a tool server shipped as a child process, or
`AgentMcpServer.Http(name, url, authToken)` for one already running, and each adapter renders the list in its
own CLI's vocabulary. Naming a server makes its tools reachable, not approved — claude still needs
`ClaudeAgentOptions.AllowedTools` for a headless run, and codex gates on `--sandbox`. A server that cannot be
rendered refuses the turn (`ProviderVerdict.Unsupported`, no process spawned) rather than being dropped, and
an `AuthToken` never reaches the command line.

**The codex session** (`AddCodexCliAgentSession()`) sits behind the same `IAgentSession`, and both
`Add*CliAgentSession` extensions also register keyed by their `id` (`"claude-cli"`, `"codex-cli"` by
default). What the codex mapping cannot do (`docs/DECISIONS.md` **D35**): a tool step arrives under codex's
own item type with codex's own payload, so switch on `ToolCall.Name`; `UsageLive`, `SessionEnded.Subtype`,
`UsageFinal.Model` and token-level deltas are never emitted; `DisallowedTools` is logged as unhonoured (codex
gates on `--sandbox`, from `ToolPolicy` or `CodexAgentOptions.SandboxMode`); and `SystemPrompt` travels as a
leading block of the prompt.

**A turn's stream, completed.** One `StreamAsync` call is one turn, ended by its one `SessionEnded`; the
conversation is the resume token that event carries. Two events sit beside the older usage ones. `PlanUpdated`
is the agent's WHOLE plan as it now stands (claude's `TodoWrite` list, codex's `todo_list`), each replacing the
last; a turn with none means the door sent none. `UsageReading` holds nullable counts, a unit, a scope
(`Turn` or `Cumulative`) and the time the library read it, so a count a door never sends is `null`, which
`UsageFinal`'s `0` cannot say. Counts are the door's own and are not normalised across doors, so compare a
reading only with one of the same door and scope. Both doors read `Turn` from their terminal usage line;
claude's is the turn's total over every API call, and codex's scope is taken from its usage record and has not
been re-measured across a resumed conversation.

**Why a turn failed** (`SessionEnded.Failure`, set on every failed terminal of both CLI sessions, null on a good
one). `AgentFailure.Kind` is `Limit`, `Access`, `Transient` or `Fatal`, read from the door's OWN failure — a
rejected `rate_limit_event`, a result's `api_error_status`, a failed turn's message — and never from what the agent
said or a tool printed, so a model quoting "529 Overloaded" cannot stop its own turn. The kinds map onto the
existing verdicts and add none (`Limit` is `RateLimited`, `Access` is `AuthFailed`, `Transient` is `Failed`).
`Restriction` says what ran out (`Cause`), what it holds back (`Subject`) and how it ends (`Recovery`: a reset
moment, a retry-not-before, an owner action, or `Unknown`). Lyntai states a recovery only where the words or a
field do and never invents a deadline: a default cool-off is yours to choose. `Delivery` says whether the prompt
was read — `NeverSent` (nothing came back from the door), `Taken` (it answered) or `Uncertain` (a tool step had
begun) — which is what a retry has to know. A process fault is the harness's, not the door's: only its stderr may
name an `Access` failure, and the verdict of such a fault is still its stderr's read.

**What an agent can do** (`AgentProviderRegistry`). Both `Add*CliAgentSession` extensions also register an
`IAgentProvider` under the same `id`, and `AddLyntai` registers the `AgentProviderRegistry` that resolves one by id;
an unknown id throws `KeyNotFoundException` naming the ids that exist. `Describe()` returns an
`AgentProviderDescriptor`: its doors (`Id`, `Kind`) and, per door, one claim for EACH ability in `AgentAbilities.All`
(resume, read-only gate, MCP servers, disallowed tools, system prompt, steering, plan events, live usage, typed
failures, accounts). A claim is `Supported`, `Unsupported` or `Unknown`, with a `Source` (`Declared`, `Negotiated`,
`Measured`) and its `Evidence`; an ability nobody measured is `Unknown`, which a host reads as not promised, never
as not possible. Ask the descriptor rather than branching on an id. `Open(doorId)` returns the door's session, and
that session refuses an option its door states `Unsupported` before anything is spawned — one `SessionEnded` with
`ProviderVerdict.Unsupported`, subtype `option-unsupported` and `NeverSent` — where the keyed `IAgentSession` keeps
its own handling (codex's logs `DisallowedTools` and spawns). An `Unknown` ability is not refused: the option reaches
the door. Another agent is one `IAgentProvider` registered with `AddAgentProvider`; build it with `AgentProvider`
over a descriptor and a session per door and the refusal comes with it. Today's claims on the two CLI doors are
declared from the adapters, except codex's resume, which was measured; codex's plan events are `Unknown`.

**Running a turn under an account** (`AgentSessionOptions.Account`). An `AgentAccount` is a `Provider` (the id the
agent is registered under, `"claude-cli"` by default), a `Name` of your choosing and a `Home`: a configuration
directory you already set up and signed in. The door starts its child with the home in its agent's own variable, for
that turn only and over the same variable in the session's `environment`: `CLAUDE_CONFIG_DIR` for claude and
`CODEX_HOME` for codex (`Describe().Accounts.HomeVariable` names it). Lyntai creates no profile, signs in to none and
stores no credential (**D20**): it never opens the home, and nothing in the account is a secret. Pass the home
absolute and existing, since the agent creates a missing one signed out and resolves a relative one against the
turn's working directory. A turn through `Open(doorId)` is refused, with no process spawned, when the account belongs
to another provider (subtype `account-other-provider`), when the door states `accounts` `Unsupported`
(`option-unsupported`), or when the name is blank or the home is blank or holds a control character
(`account-invalid`, which the door's own session raises too); each is one `SessionEnded` with
`ProviderVerdict.Unsupported` and `NeverSent`. The keyed `IAgentSession` checks only the account's own shape, not whose
it is, so go through the provider when the account may be the wrong one.

**Retrying a transient failure** (`AddAgentRetry`, off unless you call it). Every registered `IAgentSession` —
unkeyed and keyed, whichever order you register them in — is wrapped in a `RetryingAgentSession` when `AddLyntai`
finishes composing. It acts on `Failure.Kind == Transient` and nothing else, at most `MaxRetries` times (default
2), waiting `InitialDelay` doubled per retry up to `MaxDelay` — or a stated retry-not-before moment, if that is
later and within `MaxDelay`. The wait runs on the container's `TimeProvider` and a cancellation cuts it. What it
sends depends on `Delivery`: `NeverSent` sends the same prompt again; `Taken` resumes the conversation from the
failed attempt's session id with `ContinueWord` (default `"continue"`), and with no session to resume does not
retry, since resending would send the prompt twice; `Uncertain` is never retried, as a tool step may have acted.
Events are forwarded live, so a retried turn's stream holds the failed attempt's events and then the next
attempt's, and only a failed terminal that is about to be retried is withheld: you see one `SessionEnded`, the
last attempt's, and a transient failure that was not retried says why in its `Diagnostic`. One layer owns the
budget: wrapping a `RetryingAgentSession` in another, or registering one by hand beside `AddAgentRetry`, throws.

**Carrying a conversation to another agent** (a transcript and a brief; no model, no memory engine). Wrap a
session in `new RecordingAgentSession(session, recorder, new AgentConversationSource(providerId, doorId, account))`
— or call `recorder.BeginTurn(source, options)` and `recorder.Add(event)` yourself — and `recorder.Transcript`
holds each turn: your prompt and system prompt verbatim, the agent's last message (the terminal's `FinalText`, else
the text after its last tool step), a count per tool, its last plan, the session id and the end. Thinking and
diagnostics are not kept, and where the transcript is stored is yours. A recorder is one conversation, one turn at
a time, so build it per conversation rather than registering one.

`AgentBriefComposer` (registered by `AddLyntai`; `AddAgentBrief(o => o.MaxCharacters = …)` sets the bound,
default 6000) turns a transcript and your own facts into the text a fresh agent is handed: a preamble saying it is a
record and not the agent's memory, **your words verbatim and first** (the request as first written, later prompts,
standing instructions), your facts, a block per turn, the last plan, and a count of what was left out. Everything an
agent said is a claim: its lines are quoted behind `> `, so it cannot pose as yours or open a section. The bound is
in Unicode characters and **cuts in a fixed order, first to go first**: tool lines (oldest turn first), agent
messages (oldest first, the newest turn's kept longest), the plan, the newest message, then your facts
(last-listed first). Your words, the turn headers and the count are never cut; a brief they alone outgrow is
returned with `WithinBound` false, and so is one no cut shortens, since the count is itself text.

`AgentHandover.Decide(transcript, targetDescriptor, doorId, account)` says what to do next: **resume** with the
recorded session id when it is the same provider, door and account and the door states `resume` `Supported`
(`Unknown` is not promised); otherwise a **fresh session handed the brief**, with the reason (`ProviderChanged`,
`DoorChanged`, `AccountChanged`, `ResumeUnsupported`, `ResumeNotMeasured`, `NoSuchDoor`, `NoConversation`).
`decision.Apply(nextOptions, brief)` returns the options to run: the token set, or the token cleared and the
prompt led by the brief with your new request after a marker. When to hand over stays yours.

**Running turns across several accounts** (an account pool; `AddAgentAccountPool`, off unless you call it). List your
`AgentChoice`s in the order you prefer them — a provider, optionally a model and an effort word, and the accounts to
try for it — and the pool answers which account the next turn runs on, walking a choice's accounts before the next
choice. **Ask once per turn**, run the turn on the pick, and give the pick back with the turn's terminal event; a
rotation therefore lands on the next pick, never inside a turn:

<!-- compile-given: AgentAccountPool pool; IAgentSession session; AgentSessionOptions turn; -->
```csharp
var asked = await pool.PickAsync();
if (asked.Pick is { } pick)
{
    SessionEnded? ended = null;
    await foreach (var e in session.StreamAsync(pick.Apply(turn)))
        if (e is SessionEnded end) ended = end;

    await pool.ReportAsync(pick, ended!);          // a failure's restriction is held; a success clears the attempt's
}
else
{
    // every seat is restricted: asked.ReadyAt is the earliest moment time alone could free one (null = only you can)
}
```

`pick.Apply(options)` sets the options' `Account` and, when the choice names one, the `Model`. `Effort` is yours to
read off `pick.Seat.Choice`: no neutral option carries it. The pool never sleeps and never decides to wait.

**What a failure holds.** The door's own `Restriction` (never the agent's words) is held on what it covers, and a seat
runs only if EVERY restriction covering it permits: `Account` holds the account, `Endpoint` the provider (all its
accounts), `Model` and an unstated subject the account and model that met it. `Pool` holds the allowance an
`IAgentAllowanceSource` showed several accounts share — a door implements it beside `IAgentProvider` and gives its
evidence — and with none, only the account it was seen on, because another profile name proves nothing. A limit or an
access failure holds something even when the door stated nothing more; a transient or fatal failure only when it
carries a restriction. Two limits on one account are two holds, with their own moments.

**A hold ends, and then proves nothing.** A stated reset or retry-not-before moment passing releases the hold; so does
`ClearAsync(id, revision)`. A released hold permits ONE attempt at a time, leased for `AttemptLease` (default 30
minutes, which should outlast your longest turn) — a second ask is turned to the next seat. That attempt's success
removes the hold; its failure restates it at the next revision, so a clear you made from an older reading cannot lift
it (`ClearAsync` returns false). An **owner action** (an empty balance, a signed-out account) never expires: only your
clear releases it. A recovery the door did not state holds until you clear it, because Lyntai invents no deadline;
set `UnknownProbeAfter` to let a probe through after a cool-off of your choosing — a bare 429 reads this way, and
without it holds its seat until you act. `HoldsAsync()` lists every hold with its id, revision, state and the moment
time alone releases it.

`ReportReadingAsync(account, measuredAt, hasRoom)` takes a reading of an account's allowance, the input an optional
usage source will feed. It lifts a hold with a stated moment only when the reading shows room and was measured after
the hold was taken; an older reading, one that shows nothing (`null`: absent is unknown, never zero) and an owner
action or unknown recovery are left alone. Which permitted seat serves beyond the first is an `IAgentAccountPolicy`
you register yourself; only the ordered walk ships.

Holds are written through the registered `IKeyValueStore`, one key per hold under `lyntai:agent-pool:<name>:hold:`
(`AgentPoolOptions.Name`), and read back on first use, so a restart keeps them and an attempt that was in flight when
the process died bars the seat until its lease runs out; with no store the pool is process-only. **Two processes
sharing one pool are not supported**: the store has no compare-and-set, so each would hold its own picture and the
last writer would win. Give each process its own pool name, or one owner.

## Durable jobs (`Lyntai.Jobs`)

Long, multi-step work that survives restarts: a runner claims a job, your handler checkpoints, and a job whose
worker crashed is reclaimed and **resumed from its checkpoint**. Your app owns the pump:

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

Run several `IJobRunner` instances, in one process or many, and the atomic claim gives each job to exactly one.
Delivery is at-least-once, so a handler must be idempotent from its checkpoint. Higher priority runs first
within a lane, and a job that exhausts its retries lands in the dead-letter queue (`JobStatus.Dead`):

<!-- compile-given: string payloadJson;
     Guid jobId; -->
```csharp
await queue.EnqueueAsync("summarize", "summarize", payloadJson, priority: 10); // jumps the lane
foreach (var dead in await queue.ListDeadAsync())    // inspect what gave up
    await queue.ReplayAsync(dead.Id);                // requeue it (attempts reset)
await queue.CancelAsync(jobId);   // cancels a Pending or Paused job; requests cancellation of a Running one
```

Cancelling a running job is cooperative, through the handler's `CancellationToken`. **Recurring schedules**
enqueue on an interval or a cron, the next run persisted so the cadence survives restarts:

```csharp
cfg.AddJobSchedule("nightly-report", lane: "reports", type: "report", payload: "{}", every: TimeSpan.FromHours(24));
cfg.AddCronSchedule("weekday-9am", lane: "reports", type: "report", payload: "{}", cron: "0 9 * * 1-5"); // or a cron (UTC)
await scheduler.RunAsync(ct);   // in your IHostedService, alongside runner.RunAsync
```

**Schedules your users author** go in an `IJobScheduleStore`, which the scheduler lists on every tick after the
registered ones; a changed cron or interval re-anchors rather than firing at the old slot. **Status a UI
localizes** is a `JobMessage`: its `Text` is what any reader shows, and its `Code` and `Arguments` are what a
translation looks up and fills in — the generation job engine reports its stages this way
(`GenerationJobMessages`):

<!-- compile-given: IJobScheduleStore schedules;
     JobContext ctx; -->
```csharp
cfg.AddJobScheduleStore();   // kept in the key-value store — any storage backend
await schedules.SetAsync(new JobSchedule("user-42-digest", "reports", "digest", "{}", Cron: "0 18 * * *"));
await schedules.RemoveAsync("user-42-digest");

await ctx.ReportStageAsync(3, 10, new JobMessage("Copying 3 of 10")
{
    Code = "copy.progress",
    Arguments = new Dictionary<string, string> { ["done"] = "3", ["total"] = "10" },
});
```

## Guards, orchestration, secrets, vision

- **Guards** (`Lyntai.Guards`) — `IGuard`s inspect requests/replies and Allow/Block/Replace; `AddGuard<T>()`
  registers them, `GuardedTextClient` gates any completion, and the chat orchestrator applies them as gates.
- **Two-gate chat** (`IChatOrchestrator`) — one call runs: input gate → memory recall → model (via the
  tool loop) → output gate → remember. A batteries-included, guarded chat entry point.
- **Secret vault** (`Lyntai.Secrets`) — `AddSecretVault(key)` gives an `ISecretVault` encrypted at rest
  (AES-256-GCM, your key) over your storage backend. `AddEnvelopeSecretVault(machineProtector)` instead uses a
  Lyntai-generated key sealed to the host and backed by a one-time recovery key (`GenerateMasterKeyAsync()`,
  then `RecoverAsync(key)` on migration); on Windows, `AddDpapiSecretVault()` (`Lyntai.Secrets.Dpapi`) seals
  it with DPAPI.
- **Vision** — `TextMessage.UserWithImage(text, bytes, "image/png")` (or `UserWithImageUrl`) goes to the
  OpenAI-shaped backends as image content and to Ollama-native (`AddOllamaProvider`) as `/api/chat`'s `images`
  array. A URL-only attachment cannot travel on the Ollama-native path, which has no URL form: it is logged
  as undeliverable, never fetched for you — send bytes, or use Ollama's `/v1` surface via `AddHttpProvider`.
