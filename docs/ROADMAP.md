# Lyntai — Roadmap

> The design contract is `2026-07-17-lyntai-design.md`; §9 lists what was deliberately deferred.
> This file sequences how the deferred and newly-identified work lands. Dates are intentions,
> not promises. **From 1.0 the public API is frozen under SemVer 2.0** — no break without a major bump,
> gated by `ApiSurfaceTests` — with one carve-out: `Lyntai.Generation.*` ships EXPERIMENTAL until
> GEN-VERIFY closes. Released detail lives in `CHANGELOG.md`; the reasoning in `DECISIONS.md`.

## Shipped

### v0.1.0 — the substrate (2026-07)
Full `TASKS.md` sequence: core abstractions + fallback router, SQLite storage (FTS5-trigram
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
tunable via `ConfigureRouting` / `LYNTAI_*` env. Deferred migrations (now `SchemaMigration.OnFirstUse`).
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
- ✅ **`Lyntai.Providers.ClaudeCli.Mcp`** _(package since removed — see the note below)_ — the CLI runs its own agent loop and reaches custom tools only
  over MCP, so this hosts the app's `ITool`s as an ephemeral, localhost-only HTTP MCP server (Kestrel)
  and wires `claude -p` to it (`--mcp-config` + `--settings` allow-list). Opt-in `AddClaudeCliMcpTools()`;
  a small Core seam (`ICliToolProvisioner`) keeps the host dependency out of the base provider. A
  deliberate, scoped exception to "no host". **Remaining on the tool-calling track:** streaming
  tool-calls (lower value).
  _Generalized 2026-07-29 (`docs/DECISIONS.md` D23): the host moved to the provider-neutral
  `Lyntai.Tools.Mcp.Hosting` and the claude flags to a `ClaudeCliMcpDialect` in the claude provider
  package. The `Lyntai.Providers.ClaudeCli.Mcp` package and `AddClaudeCliMcpTools()` were **removed** —
  use `AddMcpToolHost(new ClaudeCliMcpDialect())`._

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

### v0.24.0 — durable-job priorities + dead-letter queue (2026-07)
- ✅ **Priorities + DLQ** — two of the v0.14 deferred job features. `JobSpec.Priority` (claim picks
  `priority DESC, available_at, id`); exhausted retries go to a terminal-but-inspectable/replayable
  `JobStatus.Dead` dead-letter queue (`IJobStore.DeadLetterAsync`/`ReplayAsync`, `IJobQueue.ListDeadAsync`/
  `ReplayAsync`) instead of a silent `Failed`. Across InMemory/SQLite/Postgres, pinned by the shared store
  contract. (The priority column was later folded into the Jobs migration — pre-release consolidation.)

### v0.25.0 — recurring job scheduling (2026-07)
- ✅ **Scheduling** (`AddJobSchedule` + `IJobScheduler`) — the last big v0.14 job deferral. Interval-based
  recurring jobs; `TickAsync`/`RunAsync` (app-owned pump). Next-run persisted via the key-value store
  (durable across restart; in-memory fallback), no new storage domain. Missed slots coalesce; first run
  waits one interval.

### v0.26.0 — cron expressions (2026-07)
- ✅ **Cron schedules** (`AddCronSchedule` + `CronExpression`) — schedules run on a real 5-field cron
  expression (UTC), not just a fixed interval. Dependency-free hand-rolled parser (ranges/steps/lists,
  dom/dow OR, `@daily`-style macros), validated eagerly at composition.

### v0.27.0 — running-job cancellation (2026-07)
- ✅ **Cancel a running job** (`IJobQueue.CancelAsync`, `IJobStore.RequestCancelAsync`/`CancelRunningAsync`,
  `JobRecord.CancelRequested`) — cooperative: a cancel request flags the job, the runner polls and cancels
  the handler's token; a handler honoring it stops and the job becomes Cancelled. Across InMemory/SQLite/
  Postgres. **Still deferred from v0.14 (the last item):** cross-process GLOBAL concurrency limits (the
  per-process cap + atomic claim cover most needs; a shared cap needs a distributed counter).

### v0.28.x — recoverable secrets, job admission, curated memory (2026-07)
- ✅ **DEK-envelope secret vault** — a Lyntai-managed data-encryption key double-wrapped by a machine
  protector (new **`Lyntai.Secrets.Dpapi`** on Windows) + a one-time recovery key (`GenerateMasterKeyAsync`/
  `RecoverAsync` for machine migration), instead of BYO-key-only.
- ✅ **Job admission control + pause + live progress** — `IJobAdmissionController` (transient whole-lane
  hold), `JobStatus.Paused` (persistent single-job hold), `ReportProgressAsync`/`ReportStepAsync` readable
  while running.
- ✅ **Curated memory catalog** (`ICuratedMemoryStore`) — operator-managed, per-kind composable entries,
  distinct from the automatic remember/recall log. Plus **per-request refusal screening**
  (`LlmRequest.RefusalPattern`) in the patch series.

### v0.29.x — app-owned storage adoption (2026-07)
- ✅ **Typed multi-kind conversation event store** — `ChatMessage` = (GUID `Id`, per-thread `Seq`, `Kind`,
  `Payload`, per-message `Metadata`); thread-level metadata; the **`IConversationEnricher`** seam (extend
  writes without forking the store).
- ✅ **`StorageFeature` toggles** — a disabled domain registers no store and lands NO table (tag-driven
  selective migration).
- ✅ **Actor/mailbox durable jobs** — `JobSpec.PartitionKey`: per-partition FIFO one-at-a-time, parallel
  across keys. Plus the typed **`IRefusalMatcher`** seam and a generic-sustainability review sweep.
- ✅ **0.29.1–0.29.3 patches** — consumer-driven generic gaps (curated `task`/`scope`, conversation paging,
  memory retention policy + prune cron) and CLI-runner hardening (large-prompt stream deadlock; buffered
  INACTIVITY dead detection + `maxDuration` backstop).

### v0.30.0 — consumer ergonomics + foundation hardening (2026-07)
- **Part 19 consumer gaps** — headless `SkipAllPermissions` for the claude agent session, `ToolLoopResult.Usage`,
  live **`IToolLoop.StreamAsync`**, `.ps1` launcher-shim hosting, curated-memory dedup-on-add + `scope` filter.
- **Whole-library foundation-hardening pass** — 6 parallel reviews (~80 findings) → correctness fixes
  (router/rate-limiter/guards/orchestrator/prompts/storage/DI), structural dedup (`JobStoreSql`+`JobRow`,
  `DelegatingLlmClient`, `LazyMigratingConnectionFactory`, async `OpenAsync` sweep), and test-suite hygiene —
  then a second **adversarial review of the pass itself** (48-agent workflow) that caught and fixed 5
  regressions round 1 introduced. Carries small pre-1.0 BREAKS (`ChatResult.BlockReason`→`Detail`,
  `IRateLimiter` cancellation semantics, tracker totals now case-insensitive) — minor-bump release.
- The pass's **deferred findings went to the backlog** (`TASKS.md`): P5 streaming-loop extraction,
  remaining Row-DTO/dedup items, PG coverage holes, contract-class mechanism, de-flaking (async
  `IUsageTracker` and the Azure preset closed in the 1.0-prep batch). Rejected findings are recorded
  in `docs/DECISIONS.md` D18.

### v1.0.0 — API freeze (2026-07-28)
The adoption gate is met and **1.0 is cut**. Every technical prerequisite shipped by v0.31.0; the
pre-freeze review + the migration baseline squash landed for 1.0.0. The technical gates (all ✅):
- ✅ **Public-API baseline** — an approval test (`ApiSurfaceTests`) snapshots every packable
  assembly's public/protected surface (incl. sealed/abstract/static/required modifiers); any
  add/remove/rename fails until the baseline is updated deliberately, so pre-1.0 breaks are visible in
  review and post-1.0 gate a major bump.
- ✅ **Semver policy** — stated in `CHANGELOG.md` and here: pre-1.0 minor versions may carry breaking
  changes (each called out in the changelog); 1.0 commits to SemVer 2.0.0 (no breaks without a major bump).
- ✅ **Consolidation reviews** — two adversarial passes over the tool-calling/platform-kit code (v0.10–v0.15),
  and the 2026-07-26 whole-library hardening pass (two rounds, incl. a 48-agent adversarial review of its
  own diff). All confirmed defects fixed.
- ✅ **Repo hosted** — github.com/JiarongGu/Lyntai with release CI (`release.yml`); nuget.org is the
  canonical package feed; real `PackageProjectUrl`/`RepositoryUrl` in `Directory.Build.props`.
- ✅ **Verification stays MANUAL by decision** (`docs/DECISIONS.md` D20) — `node devtools/dev.mjs verify`
  is the gate before any commit/release; no push/PR CI. Releases are manual too (`release.yml`,
  triggered by hand — the only automation).
- ✅ **SourceLink** — `PublishRepositoryUrl` + `ContinuousIntegrationBuild` under the manual release
  pipeline (SDK-included since .NET 8; sources already embedded via `EmbedAllSources`).
- ✅ **Final API sign-off** — an 18-finding surface audit closed by the pre-1.0 breaking batch
  (`UseDefaultCandidates`, `SchemaMigration`, required vault key, `IResponseCache` reshape, async
  `IUsageTracker`, process-runner inactivity/maxDuration reshape, honesty renames, wire internals) —
  decisions in `docs/DECISIONS.md` D19; the surface is now the one 1.0 will freeze.

**The adoption gate is met** — three sibling apps run on 0.31.1, and a pre-freeze adversarial API +
read-only consumer-usage review (`docs/DECISIONS.md` D21) settled the surface (surface-shrink, a few
breaking-if-late interface additions). **1.0.0 is cut (2026-07-28):** the public API is now frozen under
SemVer 2.0 — `ApiSurfaceTests` gates a major bump (D22) — the 0.x SQLite/Postgres migration ledgers are
squashed into 9 per-domain baselines each with the net schema unchanged (D12 one-time exception), and the
release itself is the manual tag + `release.yml` (D20). The `TASKS.md` backlog is now post-1.0 additive work.

### v1.1–v1.2.2 — CLI tool-hosting + turn-free backend maintenance (2026-07-29 → 08-03)
Post-freeze additive work, all behind existing seams: **1.1** generalized CLI tool-hosting into the
provider-neutral `Lyntai.Tools.Mcp.Hosting` + an `IMcpCliDialect` in Core (the per-consumer
`Lyntai.Providers.ClaudeCli.Mcp` package was deleted, D23). **1.2.0** added a turn-free backend probe and a
self-update seam; **1.2.1** a Windows npm-shim spawn fix found while consuming 1.2.0; **1.2.2** turn-free auth
(`IProviderAuth`) and a backend's pinned self-install (`IProviderVersionInstaller`, D26). See `CHANGELOG.md`.

### v2.0.1 — the generation platform + a coherent package graph (2026-08-04)
Two things landed together, and the major was the vehicle for the second.

**The generation platform** — one capability-aware seam for image/video/audio/3d with **three delivery modes**,
because real backends genuinely differ: inline, async job (submit → poll → fetch, universal for video), and
streaming (TTS, no implementation yet). Async operations expose their **operation id**, so a render survives a
restart and composes with `Lyntai.Jobs` — the operation id is checkpointed BEFORE the first poll, so a crash
resumes the render already running instead of paying for a second one. Backends declare
`GenerationCapabilities` and the router pre-filters on them; per-verdict fallback, spend caps, throttling and
dead-host cooldown all reuse the LLM side's machinery rather than copying it. Five backends: OpenAI images,
Automatic1111, ComfyUI, a local `sd-cli` subprocess, and the fal.ai queue. It reaches agents as five `ITool`s —
the *entire* coupling between the two domains (D30). It ships **EXPERIMENTAL**: two backends were written from
vendor docs with no key to call, one argv is ported rather than measured, and nothing implements the streaming
seam yet (`TASKS.md` GEN-VERIFY).

**A package graph with rules, and gates that enforce them.** Boundaries exist where a dependency needs
isolating (D31) — three provider ids merged into `Lyntai.Providers.Default`. Bundle membership is a budget, not
a preference (D32). Many small packages is the intended shape, paid for in tooling rather than merging (D33).
And a package may also be split for release CADENCE when a domain's churn or maturity differs from its host's
(D34) — which is why the media backends ended up in their own `Lyntai.Generation` package after all, with their
namespaces corrected to `Lyntai.Generation.Providers`. Twelve packages: ten libraries, the `Lyntai` starting
bundle, and `Lyntai.Generation`. Four new build gates keep all of it honest — `check-warnings`,
`check-packages`, `check-bundle`, and `consumer-smoke` (which packs and then restores/builds/runs a fresh
consumer app; run by hand before this release it found two defects nothing else could).

**Why 2.0.1 and not 2.0.0:** 2.0.0 is permanently taken on nuget.org (published, then unlisted, on 10 of the
ids), and `--skip-duplicate` would have silently published nothing for those. See D29.

## Planned

### The platform kit (design §9) — SHIPPED (v0.8–v0.15, deferrals closed through v0.27)
Delivered additively on the existing seams: `Lyntai.Providers.Local` · the agentic tool loop + native
tool-calling (HTTP/MEAI/CLI) + MCP-client tool source · durable jobs · guards · two-gate chat orchestration
· secret vault · vision/multimodal. The v0.14 job deferrals subsequently shipped too (priorities + DLQ
v0.24, scheduling v0.25, cron v0.26, running-job cancellation v0.27). Still open, each deliberately:
- **Server/host/launcher + auto-update** — permanently out of scope (an application concern; Lyntai is
  host-free — the one standing §9 exclusion).
- **Cross-process GLOBAL concurrency limits** — the last v0.14 deferral (needs a distributed counter; the
  per-process cap + atomic claim cover most needs).
- **Streaming tool-calls** (the `LlmChunk` contract carries no tool-call payload) and native tool-calling
  for the ClaudeCli/Local providers (both stay on the prompt fallback) — low value, revisit on demand.

### Next — generation, to close the experimental carve-out
In priority order, each needing its own measurement:
1. **GEN-VERIFY** — run `sd-cli` and fal.ai for real, confirm the argv/clamp and the wire format, then drop the
   "documented, not measured" notes. This is what lets `Lyntai.Generation` lose the EXPERIMENTAL label.
2. **Streaming TTS** — the one contract in the platform no real backend exercises
   (`IGenerationStreamProvider`). TTS before music. Needs a vendor pick and a measured wire format.
3. **Pipelines** (3d → image → video) — ordered stages feeding `artifact.ToInput(role)` forward. Deferred until
   ≥2 real backends exist so the runner isn't designed on guesses; needs a 3D-backend survey first (mesh vs
   turntable stills — only the latter chains into today's video backends).
4. **Generation wiring helpers** — `AddOpenAiImageProvider()` and friends, so a consumer stops hand-constructing
   a backend and its `HttpClient` factory. The obvious first addition to the new package.

## Standing maintenance policies
- **MEAI churn watch**: Microsoft.Extensions.AI ships roughly monthly with breaks in
  experimental/tool-content surfaces; review release notes on each bump. The bridge references
  only `Microsoft.Extensions.AI.Abstractions` (the stable core) on purpose.
- **OTel GenAI semconv watch**: the conventions are experimental and moved to a standalone repo;
  match whatever MEAI's `OpenTelemetryChatClient` currently emits rather than pinning a version.
- **Dependency refresh**: quarterly `Directory.Packages.props` review; provider-stub keeps every
  test/e2e run at zero real tokens.
