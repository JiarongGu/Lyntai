# Lyntai (灵台)

> 灵台 (língtái) — "the numinous platform," a classical Chinese name for the seat of the mind.

A reusable **.NET 10 library**: the shared **cortex + persistence** substrate for AI apps. Give a new
project an LLM provider abstraction with routing + fallback, pluggable storage, and an LLM-ops layer
(prompt registry, scoring, traces, task-scoped memory) — `AddLyntai(...)` and go, no rebuilding it per app.

Extracted from the good parts of four sibling projects: the storage/scoring/trace patterns of
**Gatherlight**, the provider abstraction of **Vidora**, the verdict-classification + memory of **Sonora**,
mastra's **composable domain storage**, and odysseus's **streaming-aware fallback**.

## Status

**v0.14.0 — durable jobs (lanes + checkpoint/resume), native tool-calling (HTTP + MEAI bridge), an MCP
tool source, proper CLI tool-calling, in-process local inference, bring-your-own resources, three
storage backends, LLM-ops depth, on a production-hardened base.** The v0.1.0 substrate (all of `tasks.md`), a
multi-agent code-review + best-practices research pass (v0.2), configurable routing (v0.3), LLM-ops
depth (v0.4), public-API baseline + a second storage backend (v0.5), a PostgreSQL backend + live-Ollama
validation (v0.6), IoC seams so the app owns its resource lifecycle — process execution, HttpClient, DB
connection/schema, provider presets (v0.7), a local GGUF provider via LLamaSharp (v0.8), an agentic
tool-calling loop (v0.9), and native (structured) function-calling with a prompt fallback (v0.10).

- `docs/2026-07-17-lyntai-design.md` — the design contract (interfaces, fork decisions, semantics, scope).
- `docs/ROADMAP.md` — what's shipped, what's next, and what's blocked on a hosted repo / DB / native deps.
- `docs/AOT.md` — per-package trimming/Native-AOT status.
- `CHANGELOG.md` — per-release detail, breaking changes called out.

## Packages

| Package | What it gives you |
|---|---|
| `Lyntai.Core` | Interfaces + the fallback router + cortex (prompt/scoring/trace) + DI. No heavy deps. |
| `Lyntai.Storage.Sqlite` | SQLite implementation of every storage domain (Dapper + FluentMigrator + FTS5). |
| `Lyntai.Storage.InMemory` | Zero-dependency in-memory storage — tests, ephemeral use, or mixed per-domain with SQLite. |
| `Lyntai.Storage.Postgres` | PostgreSQL storage (Npgsql + `pg_trgm` memory recall) for a server-backed deployment. |
| `Lyntai.Providers.ClaudeCli` | The authenticated `claude` CLI as a provider (no API key). |
| `Lyntai.Providers.OpenAiCompatible` | OpenAI / Ollama / OpenRouter-style endpoints over HttpClient. |
| `Lyntai.Providers.ExtensionsAi` | Bridge: any `Microsoft.Extensions.AI` `IChatClient` → a Lyntai provider. |
| `Lyntai.Providers.Local` | In-process local GGUF inference via LLamaSharp (llama.cpp) — add an `LLamaSharp.Backend.*`. |
| `Lyntai.Tools.Mcp` | Expose a Model Context Protocol (MCP) server's tools as Lyntai `ITool`s for the tool loop. |
| `Lyntai.Providers.ClaudeCli.Mcp` | Give the claude CLI real tool-calling — hosts your `ITool`s over MCP so the CLI's agent calls them. |

Each `src/*` is an independent NuGet package depending only on `Lyntai.Core` — add just what you need.

## Consuming Lyntai

Install `Lyntai.Core` plus the provider/storage packages you want, then compose in DI:

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
    cfg.DefaultCandidates("claude-cli", "ollama");       // router fallback order
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
- **Dead-host cooldown** instead of exponential backoff; any success resets.
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
- **Env overrides beat code config:** `LYNTAI_TIMEOUT_SECONDS`, `LYNTAI_DEADHOST_THRESHOLD`,
  `LYNTAI_DEADHOST_COOLDOWN_SECONDS`, `LYNTAI_DEFAULT_CANDIDATES` (`providerId[:model],…`),
  `LYNTAI_RETRY_FAILED`/`_TIMEOUT`/`_BACKOFF_SECONDS`, `LYNTAI_COOLDOWN_SCOPE`,
  `LYNTAI_PROVIDER_CMD` (point the CLI provider at a stub — how the tests/e2e spend zero tokens).
- **Shared-database safe:** every SQLite object Lyntai creates is prefixed `lyntai_` (including the
  migration version table), so `UseSqliteStorage` can point at an existing app database.
- **Mix storage backends per domain:** the domain interfaces are independent, so the DI container is
  the registry — `UseSqliteStorage(path)` for most domains, then override one
  (`services.AddSingleton<IMemoryStore>(...)`, last registration wins). `UseInMemoryStorage()` stands
  alone or backfills gaps. `UseSqliteStorage(path, migrateOnFirstUse: true)` defers migration I/O off
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

### Observability

Lyntai emits OpenTelemetry GenAI-convention telemetry from the router — the same schema
`Microsoft.Extensions.AI`'s `OpenTelemetryChatClient` uses, so own-seam and bridged providers land
in one backend. Nothing is emitted unless you subscribe:

```csharp
tracerProviderBuilder.AddSource(LyntaiDiagnostics.ActivitySourceName);   // "Lyntai.Llm" spans
meterProviderBuilder.AddMeter(LyntaiDiagnostics.MeterName);              // duration, token usage,
                                                                          // time_to_first_chunk
```

`chat {model}` client spans carry `gen_ai.system` (provider id), `gen_ai.request.model`, token
usage, and `error.type` (the verdict) on failure. `time_to_first_chunk` marks the streaming
fallback point of no return.

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
    cfg.UsePostgresStorage(connString, migrate: false);    // you own the schema (no Lyntai migrations)
});

// BYO process execution — control how the claude CLI is spawned (sandbox, custom shell, remote):
services.AddSingleton<IProcessRunner>(new MySandboxedProcessRunner());
```

Anything you register wins over Lyntai's default (the defaults use `TryAdd`), and every storage domain
is itself an interface (`IKeyValueStore`, `IMemoryStore`, …) you can implement wholesale.

### Local in-process inference (`Lyntai.Providers.Local`)

Run a GGUF model in-process via LLamaSharp — no network, no key, no subprocess. Reference the
`LLamaSharp.Backend.*` that matches your hardware alongside `Lyntai.Providers.Local`:

```xml
<PackageReference Include="Lyntai.Providers.Local" Version="0.8.0" />
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
    cfg.DefaultCandidates("local");
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
    cfg.AddClaudeCliProvider().DefaultCandidates("claude-cli");

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
services.AddLyntai(b => b.AddClaudeCliProvider().AddMcpTools(mcpTools).DefaultCandidates("claude-cli"));
```

**Tools for the claude CLI** (`Lyntai.Providers.ClaudeCli.Mcp`) — the CLI runs its own agent loop and
reaches custom tools only over MCP, so this add-on hosts your registered `ITool`s as an ephemeral,
localhost-only HTTP MCP server (started/stopped per CLI call) and wires `claude -p` to it. Opt in and a
completion routed to the CLI lets its agent call your tools:

```csharp
services.AddLyntai(b => b
    .AddClaudeCliProvider()
    .AddTool(_ => new FunctionTool("get_weather", (a, ct) => Task.FromResult("""{"tempC":21}"""), "Current weather"))
    .AddClaudeCliMcpTools()        // hosts the tools over MCP for the CLI
    .DefaultCandidates("claude-cli"));
// var reply = await llm.CompleteAsync(...);  → the CLI calls get_weather and answers
```

(This runs an ephemeral Kestrel listener on `127.0.0.1` only during each CLI call — a deliberate, scoped
exception to Lyntai's otherwise host-free design, isolated in this opt-in package.)

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
