# Changelog

All packages version in lockstep from `src/Directory.Build.props` (`VersionPrefix`).
Pre-1.0: minor bumps may carry breaking changes; each is called out below.

## 0.18.0 — 2026-07-18

Usage budgeting — cost/token governance on the front door, the natural companion to the response cache.
Meters spend and refuses further calls once a cap is reached. Same shape as caching: a decorator over the
single `ILlmClient` front door with a swappable accounting seam.

### Added
- **`AddUsageBudget([configure])`** — registers the built-in `InMemoryUsageTracker` and decorates the
  front door with a `BudgetedLlmClient` that records each call's usage and refuses (a `Refused` reply / an
  Error stream chunk, **without** hitting a provider) once the applicable total reaches a cap. Global caps
  via `BudgetOptions` (`MaxCostUsd` / `MaxTokens`) with optional per-consumer overrides
  (`ConsumerBudget`); also `LYNTAI_BUDGET_MAX_COST_USD` / `LYNTAI_BUDGET_MAX_TOKENS`.
- **`IUsageTracker`** (the seam) — accumulates token/cost totals per consumer + globally; query spend
  (`Total`) or reset at a billing-window boundary (`Reset`) at runtime. Register your own before
  `AddUsageBudget` for persistent/shared accounting. `UsageTotals` is the snapshot record.
- A `lyntai.budget.refusals` counter (tagged by the cap hit) on the `Lyntai.Agents` meter.

### Changed
- **Front-door decorators now compose.** The cache/budget decorators are folded over the base client in a
  deterministic order (cache **outermost**), so enabling both works correctly regardless of the order they
  were added — in particular a **cached hit is free and never counts toward the budget**. (Previously each
  decorator wrapped a fresh base client, so a second one would have clobbered the first.)

### Semantics
- The cap is a **soft ceiling**: the applicable total is checked *before* each call, so the call that
  crosses a cap still runs (its cost isn't known until it returns) and the *next* one is refused.

## 0.17.0 — 2026-07-18

Read-through response caching on the front door — an opt-in decorator that turns identical repeated
completions into a stored hit, cutting cost + latency and making repeated runs deterministic. On-brand:
it wraps the single `ILlmClient` front door, so the whole library (tool loop, orchestrator, scorers,
pairwise judge) reads through it once enabled, and the cache backend is a swappable seam.

### Added
- **`AddResponseCache([configure])`** — enables caching. Registers the built-in
  `InMemoryResponseCache` (size-bounded, per-entry TTL) and decorates the front door with a
  `CachingLlmClient`. Tunable via `CacheOptions` (`Ttl` default 1h, `MaxEntries` default 1000) or
  `LYNTAI_CACHE_TTL_SECONDS` / `LYNTAI_CACHE_MAX_ENTRIES`.
- **`IResponseCache`** (the seam) — register your own before `AddResponseCache` for a persistent or
  shared backend (Redis, a distributed KV); the front door caches through it transparently.
- **`ResponseCacheKey.For(req)`** — a stable, length-framed SHA-256 over the output-determining request
  fields (messages incl. tool calls / tool-result ids / attachments, model, max tokens, temperature, JSON
  schema). Deliberately excludes `Consumer` (a routing/telemetry tag) so two consumers share a hit.
- A `lyntai.cache.requests` counter (result `hit`/`miss`) on the `Lyntai.Agents` meter.

### Semantics
- Cached: only clean `Ok` non-streaming completions. **Never** cached: streaming (delivered live),
  requests carrying native `Tools` (the tool loop is stateful and its tools can side-effect), and non-Ok
  replies (a transient failure must not stick). A non-positive `Ttl` disables storing entirely.

## 0.16.0 — 2026-07-18

Observability for the agentic subsystems. The GenAI telemetry (v0.2) covered the LLM call path; this
extends the same OpenTelemetry-native surface to the tool loop, durable jobs, and guards, so an agent run
shows up end-to-end in one trace/metrics backend alongside the `chat` spans.

### Added
- **Agentic telemetry** — a second source/meter, `Lyntai.Agents` (constants
  `LyntaiDiagnostics.AgentActivitySourceName` / `AgentMeterName`), separate from the `Lyntai.Llm` GenAI
  one because these aren't `gen_ai.*` operations. Subscribe with `AddSource("Lyntai.Agents")` /
  `AddMeter("Lyntai.Agents")`. Emits:
  - `tool_loop` spans (tags: consumer, mode = `none`/`native`/`prompt`, step count; Error status on a
    non-Ok verdict) with a child `execute_tool <name>` span per tool call, plus a
    `lyntai.tool.invocations` counter tagged by tool name + error flag.
  - `run_job <type>` spans (tags: lane, type, id, attempt, outcome; Error status on `failed`/`lost_lease`)
    with a `lyntai.jobs.processed` counter (lane + outcome) and a `lyntai.job.duration` histogram (lane).
  - a `lyntai.guard.decisions` counter (gate `input`/`output`, guard name, result `block`/`replace`).
  Nothing is emitted unless a listener is attached — the overhead without observability wiring is a few
  null/`Enabled` checks, matching the GenAI surface.

### Samples / tests
- **Playground now exercises the tool loop** (registers an inline `echo` tool and runs `IToolLoop` over
  the deterministic stub's new `TOOL_DEMO` protocol path) and **subscribes both telemetry surfaces**
  in-process, printing what fired. The `p1` e2e asserts the loop converges via a tool call and that every
  GenAI + agentic span/metric emitted — so the instrumentation is covered end-to-end, not just in units.

## 0.15.1 — 2026-07-18

Correctness + security fixes from a three-pass adversarial review of the v0.14–v0.15 code (the AES-GCM
crypto was reviewed and confirmed correct — fresh per-call nonce, proper layout, tamper detection).

### Fixed
- **Secret vault index collision** — a secret named `__names__` mapped onto the vault's internal index
  key and could corrupt/poison the name list. The index now lives outside the secret-name namespace.
- **Denylist guard bypass** — it scanned only `user` messages, so a denied term in a system/assistant/
  tool message (e.g. a tool result fed back mid-loop) slipped through. It now scans every message and the
  reply's error `Detail` too.
- **Guard rail Replace didn't compose** — a later guard saw the original text, not an earlier guard's
  rewrite. Replacements are now re-threaded so each guard inspects the current effective text.
- **Guarded client skipped non-Ok replies** — an error reply's `Detail` (stderr/HTTP body, which can echo
  content) bypassed the output gate. Every reply is now gated.
- **Job runner lane starvation** — under a global `MaxConcurrency` smaller than the sum of lane limits,
  the first lane monopolized the cap and others starved. Claiming is now round-robin across lanes (with a
  rotating start), and `ActiveLanes` is ordered deterministically.
- **Vision edge cases** — an attachment with neither inline data nor a URL now throws instead of sending
  an empty image URL; attachments on a non-`user` role are dropped (OpenAI rejects them) rather than sent.
- **Orchestrator re-persisted redacted input** — when the input gate rewrote (redacted) the message, the
  memory write stored the *raw* original, re-injecting it on the next recall. It now stores the redacted
  text.

### Docs
- Clarified the deliberate boundaries: the chat orchestrator's two gates cover the turn's entry + final
  answer (tool-loop intermediate turns aren't individually gated — use `GuardedLlmClient` for that);
  `ISecretAccessPolicy` gates reads only; `MaxConcurrency` bounds the per-pass batch; streaming applies
  only the input gate.

## 0.15.0 — 2026-07-18

The rest of the design §9 platform kit, in one release: guards, two-gate chat orchestration, a secret
vault, and vision/multimodal. All additive, all in `Lyntai.Core`.

### Added
- **Scope-guard / jail hooks** (`Lyntai.Guards`) — `IGuard` inspects an outbound request and/or inbound
  reply and can Allow / Block / Replace; `IGuardRail` runs the registered guards (first-non-Allow wins).
  A `DenylistGuard` jails named terms; `GuardedLlmClient` wraps the front door to gate every completion.
  Register via `builder.AddGuard<T>()`.
- **Two-gate chat orchestration** (`IChatOrchestrator` in `Lyntai.Agents`) — one guarded chat turn:
  **input gate** (guards) → memory recall into the prompt → the model *via the tool loop* → **output gate**
  (guards) → remember the exchange. Composes the guard rail, `IPromptComposer`, the tool loop, and memory;
  fail-open around the two gates. Injectable as a batteries-included entry point.
- **Secret vault + access gate** (`Lyntai.Secrets`) — `ISecretVault` (get/set/delete/list), encrypted at
  rest by an `ISecretProtector` (`AesGcmSecretProtector` = AES-256-GCM with a BYO 32-byte key; tamper-
  detecting), backed by the registered `IKeyValueStore` (persistent) or in-memory. An optional
  `ISecretAccessPolicy` gates reads (denied → `UnauthorizedAccessException`). Wire via
  `builder.AddSecretVault(key, policy)`.
- **Vision / multimodal** — `LlmAttachment` (inline bytes or a URL + MIME type) on `LlmMessage.Attachments`,
  with `LlmMessage.UserWithImage(...)` / `UserWithImageUrl(...)`. The OpenAI-compatible provider renders
  them as `image_url` content parts and the MEAI bridge maps them to `DataContent`/`UriContent`; text-only
  providers ignore them.

## 0.14.0 — 2026-07-18

Durable jobs (design §9): lanes + checkpoint/resume, built for running many agents in parallel with
proper concurrency control. New storage domain across all three backends + a runner. Additive.

### Added
- **Durable job store** (`IJobStore` in `Lyntai.Storage`, over a `lyntai_job` table) — enqueue a job
  (lane, type, JSON payload), a runner claims and runs it, the handler checkpoints, and a job whose
  worker crashed is reclaimed and **resumed from its last checkpoint**. The claim is a single atomic
  statement per backend (`UPDATE … RETURNING` on SQLite under the WAL single-writer; `… FOR UPDATE SKIP
  LOCKED …` on Postgres), so multiple workers coordinate without double-claiming. Writes are fenced by
  worker id (a lost lease is abandoned, not clobbered). Backends: SQLite, Postgres, InMemory (the
  Postgres claim proven under real-container concurrency).
- **Runner + handler seam** (`Lyntai.Jobs`) — `IJobHandler` (app work, keyed by type via
  `builder.AddJobHandler<T>()`; the **at-least-once / idempotent-from-checkpoint** contract is in its
  doc), `JobContext` (payload + checkpoint + `SaveCheckpointAsync`, which renews the lease), `JobOutcome`
  (Complete / Retry(delay) / Fail), `IJobQueue`, `IJobRunner`. Retries with backoff up to max attempts; a
  thrown handler is a transient retry.
- **Parallelism + control logic** — one `IJobRunner.RunOnceAsync` claims a bounded set across *all* lanes
  and runs them **concurrently** (true multi-lane parallelism), governed by per-lane limits
  (`JobOptions.LaneConcurrency`) and a global `MaxConcurrency` cap. Scale further by running several
  runner instances (one process or many) — the atomic claim hands each job to exactly one. **The app owns
  the pump** (`RunAsync` from your own `IHostedService`/loop) — Lyntai starts no background threads, so it
  stays host-free. Tuning on `LyntaiOptions.Jobs` + `LYNTAI_JOBS_*` env.
- Demonstrated end-to-end in the Playground (a checkpointing 2-step job over the same SQLite db).

## 0.13.1 — 2026-07-18

Correctness + resource-lifecycle fixes from an adversarial review of the v0.9–v0.13 tool-calling code
(two independent review passes). One small API refinement (`SupportsToolCalls` now takes the request).

### Fixed
- **Kestrel host / `WebApplication` leaks** — the CLI MCP host (`McpToolHost.StartAsync`) and provisioner
  (`McpCliToolProvisioner`) didn't dispose the started host if a later step threw (port-bind failure,
  temp-file write failure, cancellation). Both now dispose on any failure path.
- **`req.Tools` leaked into the prompt-fallback tool loop** — a caller-supplied `LlmRequest.Tools` was
  sent as native declarations *alongside* the JSON protocol prompt, so a partially-tool-aware model could
  emit a native tool-call turn the prompt path never parses. The prompt path now clears `Tools`.
- **Typed tool arguments were stringified** — the MEAI bridge and CLI tool-host serialized boxed CLR
  primitives (`3`, `true`) as JSON strings (`"3"`, `"true"`), so a tool with an `integer`/`boolean`
  schema got the wrong type. Primitives now keep their JSON type (reflection-free).
- **MCP tool results with only non-text blocks** (image/audio/resource) fed an *empty* observation back
  to the model. `McpToolset.ToText` now describes non-text blocks instead of returning "".
- **Capability-vs-routing mismatch** — `SupportsToolCalls` probed a candidate using its raw model while
  `CompleteAsync` resolved a per-consumer/request model, so under `ProviderAndModel` cooldown scope the
  loop could pick the native path while the router served a different (non-native) candidate, silently
  dropping tools. The probe now takes the `LlmRequest` and resolves the identical model/cooldown key
  (minor API change: `ILlmClient.SupportsToolCalls(req)` / `ILlmRouter.SupportsToolCalls(candidates, req)`).

### Security
- **The CLI tool-host now requires a per-call bearer token.** The localhost MCP endpoint *executes* the
  app's tools; a random token is generated per host, passed to the CLI via the `--mcp-config` headers,
  and required on every request (401 otherwise) — so another local process can't invoke the tools during
  the call window. (Loopback-only binding remains the primary mitigation.)

## 0.13.0 — 2026-07-18

Proper tool-calling for the **claude CLI** provider, plus a test-stability fix. Additive.

### Added
- **`Lyntai.Providers.ClaudeCli.Mcp`** — gives the claude CLI provider real tool-calling. The CLI runs
  its own agent loop and reaches custom tools only over MCP, so this hosts the app's registered
  `ITool`s as an **in-process, localhost-only HTTP MCP server** (Kestrel, ephemeral port, started and
  torn down per CLI call) and points `claude -p` at it via a temp `--mcp-config` + a `--settings`
  allow-list (`mcp__lyntai__*`, so only our tools run, non-interactively). Opt-in:
  `builder.AddClaudeCliProvider().AddTool(...).AddClaudeCliMcpTools()`; a completion routed to the CLI
  then lets its agent call the app's tools and returns the tool-informed answer.
  - A small Core seam (`ICliToolProvisioner` / `CliToolSession` in `Lyntai.Agents`) keeps the
    host/ASP.NET dependency out of the base `ClaudeCli` provider — the provider gains an optional
    provisioner and behaves exactly as before when the add-on isn't registered.
  - Each `ITool` is exposed via an invocable `AIFunction` (its own JSON schema, not delegate-inferred).
    Proven end-to-end by hosting the server and connecting with Lyntai's *own* MCP client (the exact
    thing the CLI does) — no real CLI needed for the core test; a gated `LYNTAI_LIVE_CLI_TOOLS` test
    covers the real binary.
  - **Note:** this is a deliberate, scoped exception to the library's "no server/no host" principle —
    an ephemeral localhost listener that exists only during a CLI call, isolated in this opt-in package.

### Fixed
- **Flaky router cooldown test** — the dead-host cooldown integration test depended on wall-clock timing
  (under a saturated parallel runner, call 1's subprocess spawn could outlast the cooldown before call 2
  ran). Rewritten to use `DeadHostTracker`'s injectable clock — fully deterministic. (No library change.)

## 0.12.0 — 2026-07-18

New package **`Lyntai.Tools.Mcp`** — expose a Model Context Protocol (MCP) server's tools to the tool
loop. Additive; new package only.

### Added
- **`Lyntai.Tools.Mcp`** (references `Lyntai.Core` + `ModelContextProtocol.Core`) — adapts each tool on
  a connected MCP server into a Lyntai `ITool`, so the whole MCP tool ecosystem becomes callable from
  `IToolLoop` (native or prompt path, same as any other tool).
  - `McpToolset.FromClientAsync(mcpClient)` lists the server's tools and wraps each as an `McpTool`
    (`ITool`); `builder.AddMcpTools(tools)` registers them into the tool collection. The **app owns the
    `McpClient`** (transport, connection, lifecycle — BYO, consistent with Lyntai's IoC seams); Lyntai
    only adapts. `McpTool` delegates the call through a `Func` seam so the SDK's concrete client stays
    out of the contract and the adapter is unit-testable.
  - Tool results flatten to the observation string the loop feeds back (text blocks joined, or
    structured content as JSON; `error:`-prefixed when the server flags an error).
  - Proven end-to-end against a real `@modelcontextprotocol/server-everything` over stdio (opt-in
    `McpLiveTests`, gated on `LYNTAI_LIVE_MCP`).

## 0.11.0 — 2026-07-18

Native tool-calling through the **MEAI bridge** — the follow-up deferred from v0.10. Now every
`Microsoft.Extensions.AI` `IChatClient` (OpenAI, Azure, Anthropic API, Ollama-via-MEAI, …) gets native
function-calling too, not just the OpenAI-compatible HTTP provider. Additive.

### Added
- **`ExtensionsAiProvider` bridges tools both directions.** `LlmRequest.Tools` map to declaration-only
  `AIFunctionDeclaration`s on `ChatOptions.Tools` (a `LyntaiToolDeclaration` — Lyntai's tool loop drives
  execution, so no invocable `AIFunction`/`FunctionInvokingChatClient` is used); the model's
  `FunctionCallContent` surfaces on `LlmReply.ToolCalls` (empty-content tool-call turn → `Ok` before the
  empty→Failed branch); tool-call/result turns map to `FunctionCallContent`/`FunctionResultContent`.
  `SupportsToolCalls => true`. Proven end-to-end through the tool loop with a scripted `IChatClient`.
- The bridge stays **trim/AOT-clean** (✅): the tool-argument round-trip uses `System.Text.Json.Nodes`
  (no reflection-based `JsonSerializer`).

## 0.10.0 — 2026-07-18

Native (structured) tool-calling — makes the v0.9 tool loop *actually work* over real provider
function-calling instead of only the prompt-based protocol. Additive; the contract additions keep every
existing `new LlmReply`/`LlmMessage` call site source-compatible.

### Added
- **Native tool-calling round-trip.** The model's tool calls now come back as structured data and tool
  results feed back through the contract:
  - `LlmToolCall(Id, Name, ArgumentsJson)`; `LlmReply.ToolCalls`; `LlmMessage.ToolCalls` +
    `LlmMessage.ToolCallId` with factories `AssistantToolCalls(calls)` / `ToolResult(id, content)`.
  - `ILlmProvider.SupportsToolCalls` (default-interface-method, default false),
    `ILlmClient.SupportsToolCalls` / `ILlmRouter.SupportsToolCalls(candidates)` — the loop asks the
    front door whether native tool-calling is available for the default routing (first live candidate)
    without ever seeing the candidate list.
  - **`OpenAiCompatibleProvider`** parses `tool_calls` from the response into `LlmReply.ToolCalls`
    (handling OpenAI's string arguments *and* Ollama's object arguments; synthesizing an id when Ollama
    omits one) and serializes assistant-tool-call turns + `role:"tool"` result turns in both the OpenAI
    and Ollama payloads. `SupportsToolCalls => true`.
- **`IToolLoop` now prefers native, falls back to prompt.** When the routing supports native tool-calling
  the loop sends tool declarations and acts on structured `ToolCalls` (parallel calls in one turn
  supported); otherwise it uses the v0.9 prompt protocol. Both paths execute the same app-registered
  `ITool`s, and unknown/throwing tools stay recoverable. Proven end-to-end against a real local Ollama
  (opt-in `OllamaToolCallLiveTests`, gated on `LYNTAI_LIVE_OLLAMA`).

### Deferred
- Native tool-calling through the **MEAI bridge** (`ExtensionsAiProvider`) — its `SupportsToolCalls`
  stays `false` for now (an argument dict↔JSON serialization spike + a `MapMessages` rewrite); a
  follow-up. Streaming tool-calls and ClaudeCli/Local native tools remain out of scope (they use the
  prompt fallback).

## 0.9.0 — 2026-07-17

First "platform kit" (design §9) capability: agentic tool-calling. Additive, all in `Lyntai.Core`.

### Added
- **Tool-calling loop** (`Lyntai.Agents`) — a provider-agnostic ReAct-style loop over the `ILlmClient`
  front door. `IToolLoop.RunAsync(req)` renders the registered tools into the prompt, asks the model to
  either call a tool or finish (a small JSON protocol: `{"tool":…,"arguments":…}` / `{"final":…}`),
  executes the chosen tool, feeds the observation back, and repeats until it finishes or the iteration
  budget is hit. Because it runs over the text contract (through `CompleteJsonAsync`), it works with
  **any** provider — CLI, HTTP, MEAI bridge, local — with no native tool-calling support required.
  - **`ITool`** — the executable-tool seam (`Name`/`Description`/`ParametersJsonSchema` mirror the
    existing `LlmTool`, plus `InvokeAsync`), registered into a DI collection via `builder.AddTool<T>()`
    / `AddTool(factory)` (the variation point — a tool is a new class + one registration).
  - **`FunctionTool`** — define a tool inline from a delegate, no class needed.
  - **`IToolRegistry`** — name-keyed (case-insensitive, first-wins) resolution over the tool collection.
  - Robust by construction: an unknown tool or a throwing tool becomes an `error: …` observation fed
    back to the model (it can recover) rather than an exception; a non-Ok LLM verdict (refusal, all
    candidates down) is surfaced as-is; a run that doesn't converge returns `Failed` with a reason.
  - Wired by default in `AddLyntai` (resolves with zero tools — it degenerates to one plain
    completion). Budget via `LyntaiOptions.ToolLoopMaxIterations` (default 8) /
    `LYNTAI_TOOL_LOOP_MAX_ITERATIONS`, or a per-call `RunAsync(req, maxIterations)` override.

## 0.8.0 — 2026-07-17

New provider package for in-process local inference. Additive — no changes to existing packages.

### Added
- **`Lyntai.Providers.Local`** — runs a local GGUF model in-process via LLamaSharp (llama.cpp), wired
  with `builder.AddLocalProvider(modelPath, …)`. No network, no API key, no external process; the
  model loads lazily and is reused, and generations are serialized (one local model, one at a time).
  It classifies to the same verdicts the router expects (produced answer → `Ok`; empty generation or
  a load/inference fault → `Failed` so the router falls over; inactivity → `Timeout`).
  - **Managed-only on purpose:** the package references `LLamaSharp` but *not* a backend, so it isn't
    nailed to one runtime — the consuming app adds the `LLamaSharp.Backend.*` (Cpu/Cuda/Vulkan/Metal)
    that matches its hardware. A missing backend surfaces as a `Failed` verdict on the first call
    (the router then falls over), not a startup crash.
  - Applies each model's own chat template (from its GGUF metadata) so instruct-tuned models get the
    prompt format they were trained on. Opt-in live tests gate on `LYNTAI_LIVE_LLAMA` +
    `LYNTAI_LLAMA_MODEL` (like the Ollama live tests), so the default run stays native-dependency-free.

## 0.7.1 — 2026-07-17

Correctness fixes from a high-effort multi-agent code review of the v0.3–v0.7 code. No API break
(one additive optional ctor param).

### Fixed
- **Critical: BYO HttpClient was disposed every call** — an app-supplied shared client threw
  `ObjectDisposedException` on the second request. Lyntai now disposes only clients it created.
- **Memory cap counted expired entries**, evicting live facts while keeping dead ones — the cap now
  evicts expired entries first (all three backends).
- **Memory dedup ranked by a stale id**, so a re-remembered fact recalled as old — recall and the cap
  now order by `created_at` (the refreshed value), honoring the "refreshes recency" contract.
- **Router recorded a dead-host failure per retry attempt** — `Retry(Failed, 2)` benched a host in one
  request; now one failure per request. Router streaming no longer leaks empty content chunks.
- **Dapper `DateTimeOffset` handler collision** between the SQLite and Postgres backends (process-global
  registry) — both handlers are now identical, so loading both is safe.
- **Postgres dedup race** (concurrent Remembers → duplicates) — now an atomic `INSERT … ON CONFLICT`.
- **`MigratingConnectionFactory` cached a transient migration failure forever** — now retries.
- **`ClaudeCliProvider.IsAvailable` bypassed a BYO `IProcessRunner`** — a custom (sandbox/remote) runner
  is now optimistically available so the router reaches it.

## 0.7.0 — 2026-07-17

Bring-your-own resources — inversion of control for the resource-lifecycle concerns. The app owns the
implementation; Lyntai provides the interface. All additive; the `ClaudeCliProvider` ctor now takes
`IProcessRunner` (source-compatible — `ProcessRunner` implements it).

### Added
- **`IProcessRunner`** — the process-spawning seam (default `ProcessRunner`). Register your own to own
  how the `claude` CLI is spawned (sandbox, custom shell, remote/audited execution).
- **BYO HttpClient** — `AddOpenAiCompatibleProvider` (and the presets) accept an optional
  `Func<IServiceProvider, HttpClient>`, so you supply your configured client (Polly, auth handlers,
  proxy, a named `IHttpClientFactory` client) and own its lifecycle.
- **BYO DB connection + schema** — `UseSqliteStorage`/`UsePostgresStorage` gain an
  `IDbConnectionFactory` overload (you own connection creation/pooling/lifecycle) and a `migrate: false`
  flag (you own the schema; Lyntai runs no migrations).
- **Provider presets** — `AddOpenAiProvider`, `AddOllamaProvider`, `AddOpenRouterProvider`,
  `AddAzureOpenAiProvider` — pre-configured defaults over the generic method. The BYO `ILlmProvider`
  path (`AddProvider`) stays open for anything bespoke.

## 0.6.0 — 2026-07-17

Second heavyweight backend + a real-endpoint provider test — the two items previously blocked on
infrastructure, now that a Postgres-capable Docker and a local Ollama are available. Additive.

### Added
- **`Lyntai.Storage.Postgres`** — a full PostgreSQL backend for every storage domain (Npgsql + Dapper
  + FluentMigrator). `lyntai_`-prefixed; memory recall uses `pg_trgm` (GIN trigram index) for ILIKE
  substring search including CJK substrings; `timestamptz` ↔ `DateTimeOffset`; dedup/TTL/cap/prune;
  `UsePostgresStorage(conn, migrateOnFirstUse)`. Integration-tested against a real container via
  Testcontainers (skips when Docker is unavailable). Proves the domain-interface seam holds for a
  heavyweight server DB — three backends now (SQLite, in-memory, Postgres).
- **Opt-in live Ollama test** — validates the OpenAI-compatible provider (Ollama flavor) against a
  real endpoint (completion with real usage, streaming, through the router). Gated on
  `LYNTAI_LIVE_OLLAMA`; the default run stays fast and dependency-free.

## 0.5.0 — 2026-07-17

Ecosystem & backends (roadmap v0.5) + v1.0 API-freeze groundwork. Additive; no behavioral change.

### Added
- **`Lyntai.Storage.InMemory`** — a zero-dependency in-memory backend for every storage domain
  (KV, conversation, memory with dedup/TTL/cap, score, trace, prompt-version). Useful standalone
  (tests, ephemeral/serverless, no file) and as the second real backend proving the domain-interface
  seam. Wired via `builder.UseInMemoryStorage()`.
- **Composite storage** — the mastra "one interface per domain, many backends" pattern expressed
  through DI: mix backends per domain (SQLite for most, in-memory for one) via a per-domain override
  (last registration wins); `UseInMemoryStorage()` uses `TryAdd` so it stands alone or backfills gaps.
- **Public-API baseline** (`ApiSurfaceTests`) — snapshots every packable assembly's public/protected
  surface against a checked-in baseline so API changes are deliberate (pre-1.0 visible in review;
  post-1.0 gate a major bump).
- **`docs/AOT.md`** — per-package trim/AOT status and the Dapper.AOT path for `Lyntai.Storage.Sqlite`.

## 0.4.0 — 2026-07-17

LLM-ops depth (the roadmap's v0.4). No behavioral change to existing paths; all additive except the
`IMemoryStore` signature (a `ttl` param + `PruneAsync`, pre-1.0).

### Added
- **Versioned prompt overrides** — `IPromptVersionStore` + SQLite impl: an audit trail for
  `lyntai.prompt.*` edits (author, monotonic versions, exactly one active) with history and rollback
  that re-activates an earlier revision without rewriting history. The registry renders the active
  versioned override (winning over the plain KV key), placeholder guard still applied.
- **Judge calibration** — `JudgeAgreement` (exact-agreement rate, mean absolute error, Pearson over
  two aligned score series) and `IPairwiseComparer` / `LlmPairwiseComparer` (which-is-better, with
  position-bias mitigation on by default — runs both orders, ties on disagreement).
- **Memory lifecycle** — dedup (remembering an identical fact refreshes rather than duplicates),
  per-entry TTL (expired entries excluded from every recall path), and `PruneAsync(taskKey?, olderThan?)`.
  `SqliteMemoryStore` takes an injectable clock for deterministic TTL tests.
- **Trace ↔ span bridging** — `RunTrace.TraceId` captures the ambient OpenTelemetry W3C trace id at
  `Begin`, persisted and round-tripped, so a stored run trace cross-references the distributed trace.

### Changed
- `IMemoryStore.RememberAsync` gains an optional `ttl`; adds `PruneAsync` (pre-1.0 interface change).
- Migrations 202607170006 (prompt versions), 202607170007 (memory TTL), 202607170008 (trace id).

## 0.3.0 — 2026-07-17

Routing & resilience depth (the roadmap's v0.3), plus a second independent audit pass (four
adversarial reviewers over the router, providers, storage, and cortex) — findings the 0.2.0 review +
221 tests missed.

### Added
- **Configurable `RoutingPolicy`** (`LyntaiOptions.Routing`, `LyntaiBuilder.ConfigureRouting`,
  `LYNTAI_RETRY_*` / `LYNTAI_COOLDOWN_SCOPE` env). The hard-coded §6 router switch becomes the
  *default* policy — every prior router test passes unchanged. Four routing items land at once:
  - **Per-verdict action** (`FallbackAction`: Advance / PenalizeAndAdvance / CooldownAndAdvance /
    Surface), overridable with `.On(verdict, action)`.
  - **Retry-then-advance** — `.Retry(verdict, n)` retries the *same* candidate on a transient fault
    (Failed/Timeout) before falling over; cooled/surfaced/context-window verdicts never retry the
    same host. Optional `RetryBackoff`. Applies to complete + streaming pre-content.
  - **Per-(provider, model) cooldown granularity** (`CooldownScope`) — a rate-limited model no longer
    benches its siblings on the same host. Default `Provider` (unchanged).
  - **Sole-candidate exemption** (default on, LiteLLM parity) — never bench the only candidate.
- **Deferred migrations** — `UseSqliteStorage(path, migrateOnFirstUse: true)` migrates lazily on the
  first store access (thread-safe) so DI composition does no I/O.
- **`bench/Lyntai.Benchmarks`** (BenchmarkDotNet, `dev.mjs bench`) — router overhead per attempt,
  FTS5 recall latency at 1k/10k/100k rows.

### Fixed (audit pass — no API breaks)
- **Streaming timeout is now an inactivity clock in every provider.** The ExtensionsAi and
  OpenAiCompatible streaming paths still armed a single wall-clock `CancelAfter` over the whole stream
  (0.2.0 fixed only the CLI/ProcessRunner path), so a slow-but-healthy stream or a slow consumer got
  killed. Both now re-arm per read and stop the clock while the consumer works.
- **Router won't commit a stream on an empty content chunk** — the commit gate requires non-empty text,
  so a third-party provider yielding an empty/role-only first chunk can't disable fallback.
- **`LYNTAI_MODEL_<CONSUMER>` env override is implemented** (was documented but silently ignored).
- **OTel cost + cache-read tokens are recorded** on the client span (were dropped despite the 0.2.0
  telemetry claim).
- **`CompleteJsonAsync`'s retry now differs from the first attempt** (feeds back the bad reply + a
  JSON-only instruction) instead of re-sending the identical request.
- **`AddLyntai` throws on a second call** instead of shadowing `LyntaiOptions` + duplicating providers.
- **`LlmVerdictClassifier` no longer treats a bare "unauthorized" as `AuthFailed`** (which cools the
  host) — it needs auth context, mirroring the 429 guard.
- **`MigrationRunnerService`** builds its connection string via `SqliteConnectionStringBuilder` (was raw
  interpolation) and sets WAL + `busy_timeout` before migrating.
- **`ConversationStore.ListThreadsAsync`** gets an `id DESC` tiebreaker (deterministic on `created_at`
  ties).
- **`MemoryPromptComposer`** bounds the appended section by a character budget, not just entry count.
- `ILlmRouter` XML doc corrected to the amended fallback semantics.

## 0.2.0 — 2026-07-17

Production-hardening release: everything surfaced by the multi-agent code review and the
2025–2026 best-practices research pass, plus the provider-shaped consumer API.

### Added
- **`ILlmClient`** — the front door: to a consuming app, Lyntai behaves like ONE LLM provider
  (complete/stream over a request; candidates, fallback, and cooldowns stay internal).
- **`AsChatClient()`** — the reverse MEAI bridge: consume a whole Lyntai composition as a
  `Microsoft.Extensions.AI.IChatClient`.
- **`CompleteJsonAsync`** — structured output per design §6: schema-constrained call, tolerant JSON
  extraction from prose/fences, one retry, else `Failed`. `LlmScorerBase` now builds on it.
- **`LlmVerdictClassifier`** — the one shared failure classifier (typed HTTP status first, then
  conservative text heuristics); replaces three drifting per-adapter copies.
- **`LlmVerdict.ContextWindowExceeded`** (advance without penalizing the host — the remedy is a
  larger-context candidate) and **`LlmVerdict.AuthFailed`** (immediate host cooldown + advance).
- **OpenTelemetry GenAI telemetry** — `ActivitySource`/`Meter` "Lyntai.Llm": `chat {model}` client
  spans with `gen_ai.*` attributes, `gen_ai.client.operation.duration`, `gen_ai.client.token.usage`,
  and `gen_ai.client.operation.time_to_first_chunk` (the streaming fallback point of no return).
- Packaging: XML docs, snupkg symbols, embedded sources, package tags, trim/AOT analyzers
  (`IsAotCompatible` everywhere except `Lyntai.Storage.Sqlite`, which opts out honestly over Dapper).
- Playground/e2e exercise streaming end-to-end.

### Changed — breaking
- **`RateLimited` semantics (design §6 amendment):** was circuit-break-hard-stop; now cools the
  host immediately (`DeadHostTracker.MarkDead`) and advances to the next candidate — a 429 is
  terminal for the host's window, not for the fleet. Surfaced only when every candidate is exhausted.
- `LlmScorerBase`/`RelevancyScorer` constructors take `ILlmClient` (was `ILlmRouter` + options).
- SQLite objects are prefixed `lyntai_` (tables, FTS, triggers, indexes, and the FluentMigrator
  version table) so `UseSqliteStorage` can safely target an existing application database.

### Fixed
- `ProcessRunner`: stdin written before the timeout was armed (unbounded hang); streaming timeout
  counted consumer dwell time (healthy streams killed); abandoned enumerators orphaned live CLI
  processes (now killed via try/finally).
- Content-filter verdicts: HTTP-200-with-empty-content and streamed `content_filter` both surfaced
  as `Refused` (never retried, never fallen back) instead of `Failed`/silent-`Final`.
- Zero-content HTTP/MEAI streams now yield `Error(Failed)` so the router can fall over, matching
  the non-streaming path and the CLI provider.
- Claude CLI: content without a terminal result event ends `Final`, not a spurious error; spawns
  from a neutral cwd (no host-project CLAUDE.md/hooks loaded into library calls).
- `http://localhost:11434/v1` (Ollama's OpenAI-compatible surface) detects the OpenAI flavor.
- SQLite `CommandTimeout` set deliberately (the driver's busy-retry loop is independent of
  `PRAGMA busy_timeout`).

## 0.1.0 — 2026-07-17

Initial implementation: the full `tasks.md` sequence (phases 0–7). Core abstractions + fallback
router, SQLite storage with FTS5-trigram memory, claude-CLI / OpenAI-compatible / MEAI providers,
cortex layer (prompt registry, scorers incl. LLM judge, traces, memory composition), Playground,
devtools e2e harness, NuGet packaging.
