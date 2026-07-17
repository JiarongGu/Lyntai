# Changelog

All packages version in lockstep from `src/Directory.Build.props` (`VersionPrefix`).
Pre-1.0: minor bumps may carry breaking changes; each is called out below.

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
