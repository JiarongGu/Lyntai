# Lyntai (灵台) — Design Spec

> 灵台 (língtái, "the numinous platform") — a classical Chinese name for the seat of the mind.
> Lyntai is the shared **cortex + persistence** substrate the sibling apps plug into.

Status: **approved design, pre-implementation** · Date: 2026-07-17 · Scope: *Brain + persistence core*

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
├─ tasks.md                            # ACTIVE backlog (open tasks only; completed → docs/task-archive.md)
├─ CLAUDE.md + .claude/rules/          # conventions retargeted to Lyntai
└─ .gitignore
```

`Lyntai.Providers.Local` (LLamaSharp, in-process) is a **later** package, not first-cut.

### Dependency graph
```
Lyntai.Core
   ↑           ↑              ↑                    ↑
Storage.Sqlite  Providers.ClaudeCli  Providers.OpenAiCompatible  Providers.ExtensionsAi
```
No adapter references another adapter. Consumers compose via DI.

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
```csharp
services.AddLyntai(cfg => {
    cfg.AddClaudeCliProvider();                          // family default, no API key
    cfg.AddOpenAiCompatibleProvider("ollama", o => o.BaseUrl = "http://localhost:11434");
    cfg.AddExtensionsAiProvider("openai", chatClient);   // bridge any IChatClient
    cfg.UseSqliteStorage(dbPath);
    cfg.AddScorer<MyScorer>();
    cfg.DefaultCandidates("claude-cli", "ollama");       // router fallback order
});
```
Options bind from config + env overrides (`LYNTAI_*`). Sensible defaults so the minimal setup is a
provider + storage.

**Storage feature toggles** — `UseSqliteStorage(path, StorageFeature.Score | …)` (and the Postgres twin)
select which storage domains to wire: a disabled feature registers no store AND lands no table (tag-driven
selective migration; default `All`). Lyntai still OWNS the tables it creates — this just avoids unused
`lyntai_*` tables for domains the app doesn't use.

## 6. Data flow & error handling (the parts that matter)

**Fallback router** (odysseus semantics). *As of v0.3 all of this is the **default** `RoutingPolicy`
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

**Structured output:** schema-constrained call, tolerant JSON extraction from prose/code-fences, one
retry on parse failure, else `Failed` verdict.

## 7. Storage conventions (from the family)

- **Dapper** + hand-written SQL, `snake_case` columns ↔ PascalCase (`MatchNamesWithUnderscores`).
  SQLite integer-affinity trap: wrap 0..1 / double columns in `CAST(x AS REAL)` in SELECTs.
- **`IDbConnectionFactory`** opens pooled connections with `PRAGMA journal_mode=WAL; busy_timeout;
  foreign_keys=ON`.
- **FluentMigrator**, numbered `YYYYMMDDNNNN`, never reuse a number. Composite PKs inline at
  CreateTable (SQLite has no ALTER ADD CONSTRAINT).
- **FTS5 `trigram`** external-content virtual tables kept in sync by AFTER INSERT/DELETE/UPDATE
  triggers, backfilled in the same migration; build the MATCH string via a shared `FtsQuery` helper
  (drop `<3`-char tokens, quote the rest), fall back to LIKE, rank with `bm25()`.
- BOM-less UTF-8 sources + `<CodePage>65001</CodePage>` so csc on a CJK-locale machine doesn't mojibake
  string literals.

## 8. Testing

- **xUnit.** Unit tests: router/fallback/verdict/dedup/cooldown logic, prompt render + placeholder
  guard, `FtsQuery`, scoring aggregation — all pure, no I/O.
- **Integration tests:** SQLite domains against a temp db (created + migrated per test class);
  providers against the **provider-stub** (deterministic, no real tokens, driven by prompt markers —
  the generalized `claude-stub`).
- **`Lyntai.Playground`** console app = live smoke over the full stack (real provider optional, opt-in).
- **devtools e2e harness** boots the Playground (or a tiny sample host) against an isolated fixture
  with `LYNTAI_PROVIDER_CMD` = the stub, asserts end-to-end.

## 9. Explicitly out of scope (deferred to a later "platform kit" cut)

Two-gate chat orchestration · scope-guard/jail hooks · tool/MCP registry · durable jobs (lanes +
checkpoint/resume) · security/access-gate + secret vault · server/host/launcher + auto-update ·
vision/multimodal · `Lyntai.Providers.Local` (LLamaSharp). The domain interfaces are shaped to admit
these later without breaking changes.

> **Amendment (2026-07-18): the platform kit is now SHIPPED** (v0.8–v0.15), exactly as §9 promised —
> additively, no breaking changes to the substrate. `Lyntai.Providers.Local` (v0.8); the tool/MCP
> registry as the agentic tool loop + native tool-calling + an MCP-client tool source + CLI tool-hosting
> (v0.9–v0.13, `Lyntai.Agents` / `Lyntai.Tools.Mcp` / `Lyntai.Providers.ClaudeCli.Mcp`); durable jobs
> (v0.14, `Lyntai.Jobs` + `IJobStore`); then guards (`Lyntai.Guards`), two-gate `IChatOrchestrator`,
> the secret vault (`Lyntai.Secrets`), and vision/multimodal (`LlmMessage.Attachments`) in v0.15. See
> `CHANGELOG.md` / `ROADMAP.md`. The **only** §9 item still deliberately out of scope is the
> **server/host/launcher + auto-update** — that's an application concern, not a library's (Lyntai stays
> host-free; the one scoped exception is the ephemeral, opt-in localhost MCP listener the ClaudeCli.Mcp
> add-on runs during a CLI call).

## 10. Consuming Lyntai (target ergonomics)

A new app adds package references to `Lyntai.Core` + the storage/provider packages it wants, calls
`services.AddLyntai(...)`, and injects `ILlmRouter`, `IScoringService`, `IMemoryStore`, etc. No source
copying, no rebuild of the substrate. Adding a storage backend = a new `Lyntai.Storage.X` package that
implements the domain interfaces. Adding a provider = a new `ILlmProvider` or an MEAI `IChatClient`
through the bridge.
