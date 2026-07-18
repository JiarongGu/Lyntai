# Lyntai — Roadmap

> The design contract is `2026-07-17-lyntai-design.md`; §9 lists what was deliberately deferred.
> This file sequences how the deferred and newly-identified work lands. Dates are intentions,
> not promises; pre-1.0 minor versions may carry breaking changes (called out in `CHANGELOG.md`).

## Shipped

### v0.1.0 — the substrate (2026-07)
Full `tasks.md` sequence: core abstractions + fallback router, SQLite storage (FTS5-trigram
memory), claude-CLI / OpenAI-compatible / MEAI providers, cortex layer, Playground, e2e, packaging.

### v0.2.0 — production hardening (2026-07)
Multi-agent code review (10 confirmed bugs fixed) + best-practices research pass applied:
`ILlmClient` front door + `AsChatClient()` reverse bridge, shared verdict classifier, finer verdict
taxonomy (context-window / auth), amended RateLimited semantics (cool host, advance), ProcessRunner
lifecycle correctness, OpenTelemetry GenAI spans/metrics, structured output (`CompleteJsonAsync`),
trim/AOT analyzers, symbols + embedded sources. Plus a second adversarial audit pass (streaming
inactivity clocks in every provider, empty-content commit gate, env/telemetry/idempotency fixes).

### v0.3.0 — routing & resilience depth (2026-07)
Configurable `RoutingPolicy` (the §6 switch becomes the default policy): per-verdict action,
retry-then-advance, per-(provider, model) cooldown granularity, sole-candidate exemption — all
tunable via `ConfigureRouting` / `LYNTAI_*` env. Deferred migrations (`migrateOnFirstUse`).
BenchmarkDotNet project (router overhead, FTS recall at scale).

### v0.4.0 — LLM-ops depth (2026-07)
Versioned prompt overrides (`IPromptVersionStore`, history + rollback); judge calibration
(`JudgeAgreement` metrics + position-bias-aware `IPairwiseComparer`); memory lifecycle (dedup,
per-entry TTL, `PruneAsync`); trace↔span bridging (`RunTrace.TraceId`). Remaining v0.4 idea —
LLM summarization/compaction of old memory — deferred as a composition-helper pattern (the
deterministic lifecycle primitives shipped; summarization has no settled recipe yet).

### v0.5.0 — ecosystem & backends (2026-07)
- ✅ **Composite store seam** — `Lyntai.Storage.InMemory` is the second real backend; the mastra
  "one interface per domain, many backends" pattern is expressed through DI (the container is the
  registry): `UseInMemoryStorage()` stands alone or backfills gaps, and mixing is a per-domain
  override (last registration wins). Proven by tests.
- ✅ **AOT story documented** — `docs/AOT.md`: Core + providers + InMemory are AOT-compatible;
  `Lyntai.Storage.Sqlite`/`Postgres` opt out honestly over Dapper reflection, with the Dapper.AOT
  path noted.

### v0.6.0 — Postgres + live-provider validation (2026-07)
- ✅ **`Lyntai.Storage.Postgres`** — the third real backend (Npgsql + Dapper + FluentMigrator, pg_trgm
  memory recall incl. CJK), integration-tested against a real container via Testcontainers.
- ✅ **Live Ollama test** — the OpenAI-compatible provider verified against a real endpoint (opt-in).

### v0.7.0 — bring-your-own resources (2026-07)
IoC seams so the consuming app owns resource lifecycle, Lyntai just provides the interface:
- ✅ **`IProcessRunner`** (own CLI spawning), **BYO HttpClient** (own the client/handlers/lifecycle),
  **BYO `IDbConnectionFactory` + `migrate:false`** (own connection + schema), and **provider presets**
  (`AddOpenAiProvider`/`AddOllamaProvider`/`AddOpenRouterProvider`/`AddAzureOpenAiProvider`) alongside
  the existing BYO `ILlmProvider` path.

### v0.8.0 — in-process local inference (2026-07)
- ✅ **`Lyntai.Providers.Local`** (LLamaSharp / llama.cpp, deferred §9) — runs a local GGUF model
  in-process via `AddLocalProvider(modelPath)`; no network/key/subprocess. Ships **managed-only** so it
  isn't nailed to one runtime — the consuming app picks the `LLamaSharp.Backend.*` for its hardware; a
  missing backend degrades to a `Failed` verdict (router falls over), not a crash. Applies each model's
  own GGUF chat template. Wiring is unit-tested; real inference gated behind opt-in live tests
  (`LYNTAI_LIVE_LLAMA` + `LYNTAI_LLAMA_MODEL`), so the default run stays native-dependency-free.

### v0.9.0 — agentic tool-calling (2026-07) — first platform-kit cut
- ✅ **Tool-calling loop** (`Lyntai.Agents`, deferred §9 "tool/MCP registry") — a provider-agnostic
  ReAct loop over `ILlmClient` (`IToolLoop`), executable-tool seam (`ITool` + `AddTool` DI collection,
  `FunctionTool` for inline tools), name-keyed `IToolRegistry`. Runs over the text contract via
  `CompleteJsonAsync`, so it works with any provider. Unknown/throwing tools become recoverable
  observations; non-Ok verdicts surface; non-convergence returns `Failed`. The primitive the remaining
  agentic §9 items (orchestration, durable jobs) build on.

### v0.10.0 — native tool-calling (2026-07)
- ✅ **Native (structured) tool-calling** — the loop now uses real provider function-calling when
  available, not just the prompt protocol. Contract carries tool calls (`LlmToolCall`,
  `LlmReply.ToolCalls`, `LlmMessage.ToolResult`/`AssistantToolCalls`); `SupportsToolCalls` capability on
  provider/router/client (first-live-candidate) lets `IToolLoop` pick native vs. the prompt fallback
  transparently. `OpenAiCompatibleProvider` parses `tool_calls` (OpenAI + Ollama dialects) and
  serializes tool/assistant turns. Proven end-to-end against a real Ollama.

### v0.11.0 — native tool-calling through the MEAI bridge (2026-07)
- ✅ **`ExtensionsAiProvider` native tools** — every `Microsoft.Extensions.AI` `IChatClient` (OpenAI,
  Azure, Anthropic API, …) now gets native function-calling. Declaration-only `AIFunctionDeclaration`s
  on `ChatOptions.Tools` (Lyntai's loop still drives execution — no `FunctionInvokingChatClient`),
  `FunctionCallContent`↔`LlmReply.ToolCalls`, `FunctionResultContent` for results; stays trim/AOT-clean
  via `System.Text.Json.Nodes`.

### v0.12.0 — MCP tool source (2026-07)
- ✅ **`Lyntai.Tools.Mcp`** — expose a Model Context Protocol server's tools as Lyntai `ITool`s
  (`McpToolset.FromClientAsync` + `AddMcpTools`), so the loop can drive the whole MCP tool ecosystem.
  App owns the `McpClient` (BYO transport/connection); Lyntai adapts. Proven live against
  `@modelcontextprotocol/server-everything`.

### v0.13.0 — proper tool-calling for the claude CLI (2026-07)
- ✅ **`Lyntai.Providers.ClaudeCli.Mcp`** — the CLI runs its own agent loop and reaches custom tools only
  over MCP, so this hosts the app's `ITool`s as an ephemeral, localhost-only HTTP MCP server (Kestrel)
  and wires `claude -p` to it (`--mcp-config` + `--settings` allow-list). Opt-in `AddClaudeCliMcpTools()`;
  a small Core seam (`ICliToolProvisioner`) keeps the host dependency out of the base provider. A
  deliberate, scoped exception to "no host". **Remaining on the tool-calling track:** streaming
  tool-calls (lower value).

### v0.14.0 — durable jobs (2026-07)
- ✅ **Durable jobs** (`Lyntai.Jobs` + `IJobStore`, design §9 "durable jobs — lanes + checkpoint/resume")
  — enqueue → atomic per-lane claim → app handler → checkpoint → crash-resume, across SQLite/Postgres/
  InMemory. Multi-agent parallelism with control: per-lane + global `MaxConcurrency` limits, all lanes run
  concurrently per pass, multiple runner instances coordinate via the atomic claim. App owns the pump
  (host-free). At-least-once (idempotent-from-checkpoint). **Deferred (noted):** cron/scheduling,
  priorities, dead-letter queue, cross-process global limits, running-job cancellation.

### v0.15.0 — the rest of the platform kit (2026-07)
- ✅ **Scope-guard / jail hooks** (`Lyntai.Guards`) — `IGuard`/`IGuardRail` (Allow/Block/Replace),
  `DenylistGuard`, `GuardedLlmClient`, `AddGuard`.
- ✅ **Two-gate chat orchestration** (`IChatOrchestrator`) — input gate → memory → tool loop → output gate
  → remember, composing the existing primitives.
- ✅ **Secret vault + access gate** (`Lyntai.Secrets`) — `ISecretVault` encrypted at rest (AES-256-GCM,
  BYO key), KV-backed or in-memory, optional `ISecretAccessPolicy`.
- ✅ **Vision / multimodal** — `LlmAttachment` on messages; OpenAI `image_url` parts + MEAI
  `DataContent`/`UriContent`. This completes design §9 (the "platform kit") apart from the
  server/host/launcher, which is intentionally out of scope for a library.

### v0.16.0 — agentic observability (2026-07)
- ✅ **Agentic telemetry** — the v0.2 GenAI telemetry covered the LLM call path; this extends the same
  OpenTelemetry-native surface to the agentic subsystems via a second source/meter `Lyntai.Agents`
  (`AddSource`/`AddMeter`): `tool_loop` + child `execute_tool` spans and a tool-invocations counter; per-job
  `run_job` spans with processed/duration metrics; a guard-decisions counter. An agent run now traces
  end-to-end alongside the `chat` spans. Emits nothing without a listener attached.

### v0.17.0 — response caching (2026-07)
- ✅ **Read-through response cache** (`AddResponseCache`) — an opt-in decorator over the `ILlmClient` front
  door: identical cacheable completions return a stored `Ok` reply instead of hitting a provider. Built-in
  `InMemoryResponseCache` (TTL + size cap) with a swappable `IResponseCache` seam (BYO Redis/distributed);
  stable length-framed SHA-256 keying over output-determining fields (excludes `Consumer`); streaming,
  native-tool, and non-Ok replies are never cached. A `lyntai.cache.requests` hit/miss counter. Because it
  wraps the single front door, the tool loop / orchestrator / scorers all read through it once enabled.

### v0.18.0 — usage budgeting (2026-07)
- ✅ **Usage budget / spend caps** (`AddUsageBudget`) — a front-door decorator that meters token/cost usage
  (`IUsageTracker`, per-consumer + global) and REFUSES further calls once a cap (`BudgetOptions`:
  `MaxCostUsd`/`MaxTokens`, per-consumer overrides) is reached, without hitting a provider. Soft ceiling
  (checked before each call). Front-door decorators now compose deterministically (cache outermost), so a
  cached hit is free and never counts toward the budget. `lyntai.budget.refusals` counter.

### v0.19.0 — semantic memory (2026-07)
- ✅ **Semantic (embedding-based) memory recall** — an app-provided `IEmbedder` (`AddEmbeddings`) + an
  `ISemanticMemory` service that remembers facts by embedding and recalls them by cosine similarity
  (k / minScore), scoped by (task, scope) like the lexical store, dedup by content. Vector persistence is a
  swappable `IVectorStore` seam with a zero-dependency brute-force `InMemoryVectorStore` default (pgvector /
  sqlite-vec can follow as a backend package). First cut: no TTL, composer integration stays opt-in.

### v0.20.0 — semantic memory wired into the chat path (2026-07)
- ✅ **Hybrid recall + dual-write** — the `MemoryPromptComposer` now leads with semantic hits then fills in
  lexical entries (deduped, fail-open across both) when embeddings are registered, and `ChatOrchestrator`
  writes each remembered exchange to both stores. Semantic memory is registered only when an `IEmbedder`
  is, so the chat path skips it cleanly otherwise. Closes the v0.19 "composer integration stays opt-in" note.

### v0.21.0 — client-side rate limiting (2026-07)
- ✅ **Rate limiting** (`AddRateLimit`) — a token-bucket front-door decorator: over the configured rate a
  call waits up to `MaxWait` then is refused (`RateLimited`), without hitting a provider. Global +
  per-consumer rates; swappable `IRateLimiter` seam (in-memory `TokenBucketRateLimiter` default,
  distributed later). Completes the governance trio (cache · budget · rate-limit); folds innermost so a
  cached hit spends no permit. `lyntai.ratelimit.refusals` counter.

### v0.22.0 — persistent SQLite backends for the new seams (2026-07)
- ✅ **SQLite response cache / usage tracker / vector store** — the governance + semantic-memory features
  shipped with in-memory defaults; this backs them with SQLite (`UseSqliteResponseCache` /
  `UseSqliteUsageTracking` / `UseSqliteVectorStore`) so a cache, a spend budget, and semantic memory survive
  restarts, all behind the same interfaces (opt-in, `AddSingleton` over the Core `TryAdd` defaults). One
  migration adds the three `lyntai_*` tables. Vector search is brute-force (pgvector is the path for scale);
  rate limiting stays in-memory by design (distributed-limiter concern). Postgres equivalents can follow the
  same way.

### v0.23.0 — Postgres backends for the new seams, with pgvector (2026-07)
- ✅ **Postgres response cache / usage tracker / vector store** — mirrors the v0.22 SQLite backends
  (`UsePostgresResponseCache` / `UsePostgresUsageTracking` / `UsePostgresVectorStore`). The vector store is
  **pgvector**-backed: the cosine `<=>` operator + SQL `ORDER BY … LIMIT k` do the top-k in the database
  (not brute-force in the app) — the scale path flagged in v0.19/v0.22. Its schema is created lazily on
  first use, so `UsePostgresStorage` doesn't force pgvector on consumers who don't use semantic memory.
  Cache + usage go in migration `M202607180002`. Exact (unindexed) for now; an hnsw/ivfflat ANN index
  (needs a fixed embedding dimension) is a further enhancement.

## Planned

### Blocked on user-provided infrastructure
These need something only the maintainer can provision; the design admits them without breaking changes.
- **Real `PackageProjectUrl`/`RepositoryUrl`** + **SourceLink activation** — gated on the repo being
  hosted. Sources are already embedded in the PDBs via `EmbedAllSources`, so step-into debugging works
  today; SourceLink is a one-package add once there's a remote to resolve. (Docs live in the repo —
  README + `docs/` on GitHub — no separate docs site planned.)

### v1.0 — API freeze
- ✅ **Public-API baseline** — an approval test (`ApiSurfaceTests`) snapshots every packable
  assembly's public/protected surface; any add/remove/rename fails until the baseline is updated
  deliberately, so pre-1.0 breaks are visible in review and post-1.0 gate a major bump.
- ✅ **Semver policy** — stated in `CHANGELOG.md` and here: pre-1.0 minor versions may carry breaking
  changes (each called out in the changelog); 1.0 commits to SemVer 2.0.0 (no breaks without a major bump).
- ✅ **Consolidation review** — two adversarial-review passes over the tool-calling and platform-kit code
  (v0.10–v0.15), all confirmed defects fixed (the AES-GCM crypto reviewed and confirmed correct).
- ✅ **Docs sweep** — README/CHANGELOG/ROADMAP/CLAUDE + the design §9 amendment reconciled to the shipped
  surface.
- Remaining before tagging 1.0: **host the repo** (the one blocker — unblocks SourceLink + the real
  package/repo URLs, a one-package add once there's a remote), then tag 1.0 to freeze the public API
  (`ApiSurfaceTests` already guards it).

### The platform kit (design §9) — SHIPPED (v0.8–v0.15)
Delivered additively on the existing seams, no breaking changes to the substrate: `Lyntai.Providers.Local`
· the agentic tool loop + native tool-calling (HTTP/MEAI/CLI) + MCP-client tool source · durable jobs ·
guards · two-gate chat orchestration · secret vault · vision/multimodal. The only §9 item still out of
scope is the **server/host/launcher + auto-update** (an application concern — Lyntai stays host-free).
Smaller per-feature deferrals remain open (each low priority): streaming tool-calls; native tool-calling
through the MEAI bridge is done, but ClaudeCli/Local stay on the prompt fallback; durable-job cron/
priorities/dead-letter-queue/cross-process-global-limits/running-job-cancellation.

## Standing maintenance policies
- **MEAI churn watch**: Microsoft.Extensions.AI ships roughly monthly with breaks in
  experimental/tool-content surfaces; review release notes on each bump. The bridge references
  only `Microsoft.Extensions.AI.Abstractions` (the stable core) on purpose.
- **OTel GenAI semconv watch**: the conventions are experimental and moved to a standalone repo;
  match whatever MEAI's `OpenTelemetryChatClient` currently emits rather than pinning a version.
- **Dependency refresh**: quarterly `Directory.Packages.props` review; provider-stub keeps every
  test/e2e run at zero real tokens.
