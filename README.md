# Lyntai (灵台)

> 灵台 (língtái) — "the numinous platform," a classical Chinese name for the seat of the mind.

A reusable **.NET 10 library**: the shared **cortex + persistence** substrate for AI apps. Give a new
project an LLM provider abstraction with routing + fallback, pluggable storage, and an LLM-ops layer
(prompt registry, scoring, traces, task-scoped memory) — `AddLyntai(...)` and go, no rebuilding it per app.

Extracted from the good parts of four sibling projects: the storage/scoring/trace patterns of
**Gatherlight**, the provider abstraction of **Vidora**, the verdict-classification + memory of **Sonora**,
mastra's **composable domain storage**, and odysseus's **streaming-aware fallback**.

## Status

**v0.7.0 — bring-your-own resources, three storage backends, LLM-ops depth, on a production-hardened
base.** The v0.1.0 substrate (all of `tasks.md`), a multi-agent code-review + best-practices research
pass (v0.2), configurable routing (v0.3), LLM-ops depth (v0.4), public-API baseline + a second storage
backend (v0.5), a PostgreSQL backend + live-Ollama validation (v0.6), and IoC seams so the app owns its
resource lifecycle — process execution, HttpClient, DB connection/schema, provider presets (v0.7).

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
