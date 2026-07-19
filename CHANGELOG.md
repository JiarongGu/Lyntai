# Changelog

All packages version in lockstep from `src/Directory.Build.props` (`VersionPrefix`).
Pre-1.0: minor bumps may carry breaking changes; each is called out below.

## Unreleased

### Added
- **Agentic self-driving-agent session** — a generic primitive for gating an agent that drives its OWN
  tool loop out-of-process (the `claude` CLI now; a future Codex/Gemini-CLI/OpenAI-Responses adapter
  reuses the surface unchanged), distinct from `IToolLoop` (where Lyntai drives the loop). Neutral surface
  in Core `Lyntai.Agents`: `IAgentSession.StreamAsync` yielding an `AgentStreamEvent` transcript
  (`SessionStarted`/`TextDelta`/`Thinking`/`ToolCall`/`ToolResult`/`UsageLive`/`UsageFinal`/`SessionEnded`),
  an `AgentToolPolicy` (ReadOnly plan gate vs Write execute gate), an opaque `ResumeToken` (resume across a
  human gate), and `AgentSessionOptions`/`AgentSessionResult`. **Both consumption doors:**
  `StreamAsync` (live transcript) and the `RunAsync(onEvent)` extension (folds to `AgentSessionResult`),
  mirroring `ILlmProvider.StreamAsync`/`CompleteAsync`. The `claude` adapter
  (`Lyntai.Providers.ClaudeCli`): `ClaudeAgentSession` + `ClaudeAgentOptions` (`--settings` scope-guard
  hooks, `--mcp-config`/`--allowedTools` for an app-hosted MCP server, read-only/write tool policy,
  `--resume`), `ClaudeAgentArgs`, `ClaudeToolCalls.FilePathOf`, and `AddClaudeCliAgentSession()`. Prompt
  over stdin only; diagnosable termination (a no-output run is never silent). Design:
  `docs/2026-07-19-agent-session-design.md`.
- **`LyntaiOptions.ResolveTimeout(int?)`** — a per-call-seconds timeout overload (value clamped to
  `MaxProviderTimeout`, else the global `ProviderTimeout`), shared by the request path and the agent
  session.

### Changed
- **Generic typed-event conversation store (Part 7 · P2)** — a conversation is now modelled as a typed
  multi-kind event stream (text / tool-call / tool-result / usage / thinking / phase / error), not only
  role/text chat turns — so an agent transcript or a tool-loop run can persist through the same surface,
  and an adopter's typed event log fits without a bespoke schema. `ChatMessage` gains `Kind` (event type;
  a role for a plain chat turn) and `Payload` (event body; text or JSON), keeping `Role`/`Content` as
  read-only aliases for the chat shape. `ChatThread` gains optional opaque `Metadata` (thread-level JSON
  state) with `IConversationStore.SetThreadMetadataAsync` to update it and a `metadata` arg on
  `CreateThreadAsync`. **Breaking (pre-1.0):** the message columns are renamed `role`→`kind`,
  `content`→`payload` and a `metadata` column added to threads (migrations edited in-place, pre-release —
  no data migration); `AppendMessageAsync`'s parameters are renamed `role`/`content`→`kind`/`payload`
  (signature unchanged). Implemented across all three backends (SQLite / InMemory / Postgres).
- **App-owned cortex KV (Part 7 · P1)** — the cortex KV key namespaces are now configurable, so an app can
  point Lyntai's prompt/model overrides straight at its OWN existing keys — no prefix-translating shim, no
  duplicated rows. `PromptRegistry` and `KeyValueModelRoutingStore` take an optional `keyPrefix` ctor
  argument, surfaced on the builder as `LyntaiOptions.PromptKeyPrefix` / `LyntaiOptions.ModelKeyPrefix`.
  Defaults are UNCHANGED (`lyntai.prompt.` / `lyntai.model.`), so existing consumers are unaffected.
  **Breaking (pre-1.0):** the public `KeyPrefix` const on both stores is renamed to `DefaultKeyPrefix`; the
  effective prefix is now the instance `KeyPrefix` property.
- **KV backing table renamed `lyntai_app_config` → `lyntai_kv`** — it is Lyntai's own key→value store
  (prompt/model overrides, scheduler next-run, secret vault), never the *application's* config; the old name
  was a carry-over that read backwards for a library table. Applied in-place to the existing migration
  (pre-release — no data migration). **Breaking (pre-1.0)** for anyone reading the raw table.

## 0.28.1 — 2026-07-18

Consumer-driven adoption gaps — makes Lyntai's **cortex + scoring** genuinely adoptable (a real app can
retire its own scoring framework + model tuning with no regression) and adds a **per-request timeout**.
Additive only (new overloads / opt-in / default-interface members) — no breaking change.

### Added
- **Per-request timeout override** — `LlmRequest.TimeoutSeconds` (+ a per-consumer
  `LyntaiOptions.TimeoutByConsumer` map) let one long call — e.g. a CLI-agent run driving many steps —
  carry a bigger budget without inflating every short call. `LyntaiOptions.ResolveTimeout` (request >
  consumer-map > "default" > global `ProviderTimeout`), clamped to `MaxProviderTimeout`
  (env `LYNTAI_MAX_TIMEOUT_SECONDS`); honored by all four providers.
- **Score store: upsert + aggregate + export** (`IScoreStore`) — `SaveAsync` now UPSERTs on
  `(session, scorer)` (re-scoring replaces, not accumulates; new `UNIQUE` merged into the score
  migration), plus `AggregateAsync` (per-scorer AVG+COUNT → `ScorerAggregate`) and `ExportAsync`
  (flat `(session, scorer, score)` dump → `ScoreExportRow`) for the eval dashboard + tuning datasets.
- **Dry-run scoring** — `IScoringService.EvaluateAsync(ctx, persist: false)` scores without writing rows
  even when a store is wired (a preview/tuning path).
- **Per-scorer judge model** — `LlmScorerBase` exposes overridable `Model` + `Consumer`, so a cheap judge
  can route to a cheap model per scorer (was hardcoded to the default + `"scoring"`).
- **Applicability skip on `LlmScorerBase`** — a `protected virtual bool Applies(ScoreContext)` (default
  true) checked before the judge call, so a conditional judge (e.g. "faithfulness" applies to a plan, not a
  code-edit turn) returns null WITHOUT spending tokens instead of scoring every context.
- **Live per-consumer model routing** — opt-in `AddLiveModelRouting()` registers an `IModelRoutingStore`
  (KV-backed) the router + cache read each call, so an admin model retune takes effect WITHOUT a restart
  (`lyntai.model.<consumer>`; precedence: explicit → live → configured default). `ResolveModel` gains a
  `liveOverride` overload.
- **Prompt-override validation** — `IPromptRegistry.ValidateOverride(default, candidate)` returns the
  `{placeholders}` a candidate would drop (empty = valid), so an admin save-flow rejects a bad override up
  front instead of relying on `RenderAsync`'s silent runtime fall-back.
- **`IScorer.Description`** (optional default-interface member) for an admin "list scorers" view; documented
  the `ScoreContext.Extra` domain-dimension pattern.

## 0.28.0 — 2026-07-18

Adds a **portable, recoverable secret vault** and **job admission control / pause**, and lands the
review-follow-up backlog (a second multi-agent code review). New package **`Lyntai.Secrets.Dpapi`**;
public-surface additions to `Lyntai.Core` (envelope vault, `IJobAdmissionController`, `JobStatus.Paused`,
live job progress, curated memory) and the three storage backends (`PauseAsync`/`ResumeAsync`, progress
reporting, `ICuratedMemoryStore`) — see below.

### Added
- **DEK-envelope secret vault** (`Lyntai.Core`) — a Lyntai-managed data-encryption key instead of a BYO
  key. `SecretKeyEnvelope` generates a random 256-bit DEK that all secrets are AES-256-GCM encrypted
  under, and wraps the DEK **two ways**: a *machine wrap* (sealed by an injected `ISecretProtector` — the
  fast path, no passphrase on the same host) and a *recovery wrap* (a KEK derived PBKDF2-SHA256 from a
  one-time recovery key — the portability path). `EnvelopeSecretVault` drives the lifecycle:
  `GenerateMasterKeyAsync()` (once, returns the recovery key to record out-of-band), auto-init via the
  machine wrap, and `RecoverAsync(recoveryKey)` on a new host (re-binds the DEK, so later reads take the
  fast path). A machine that can't unseal the machine wrap throws `SecretRecoveryRequiredException` until
  recovered. Wire with `builder.AddEnvelopeSecretVault(machineProtector)`.
- **`Lyntai.Secrets.Dpapi`** (new package) — `DpapiSecretProtector` (Windows DPAPI via
  `System.Security.Cryptography.ProtectedData`, user- or machine-scoped, optional entropy) and
  `builder.AddDpapiSecretVault(...)`, which wires the envelope vault with DPAPI as the machine-binding
  protector: secrets sealed to this Windows host at rest, recoverable off-machine via the recovery key.
  Windows-only at runtime (guarded with a clear `PlatformNotSupportedException`); the envelope crypto
  stays portable in Core, so non-Windows hosts use an AES-GCM protector via `AddEnvelopeSecretVault`.
- **Job admission control + `Paused` state** — `IJobAdmissionController` (default admit-all) is consulted
  by the runner per lane *before* it claims, so an app can throttle lanes by external signals (GPU/CPU
  load, a maintenance window) without Lyntai knowing about them; a held lane's jobs stay Pending (a throw
  is treated as "hold"). Register with `AddJobAdmissionController`. Separately, `JobStatus.Paused` with
  `IJobQueue`/`IJobStore` `PauseAsync`/`ResumeAsync` administratively holds a single Pending job out of the
  claimable set (no schema change — status is TEXT) across all three backends.
- **Live job progress + step reporting** — `JobContext.ReportProgressAsync(done, total, stage)` and
  `ReportStepAsync(message)` let a handler surface live status a UI can read WHILE the job runs (new
  `JobRecord.Progress`/`Total`/`Stage`/`StepLog` + `IJobStore.ReportProgressAsync`/`ReportStepAsync`,
  fenced by the worker id, not lease renewals). The step log is a capped JSON array (`JobStepLog.Parse`/
  `Append` → `JobStep`); `JobContext` also exposes the prior snapshot (`Progress`/`Steps`/…) so a resumed
  handler sees what it already reported. New `lyntai_job` columns folded into the jobs migration
  (pre-release; SQLite + Postgres). InMemory mirrors it.
- **Per-request refusal pattern** — `LlmRequest.RefusalPattern` (a case-insensitive regex) surfaces an
  otherwise-`Ok` reply whose text matches as `Refused` (no fallback) — a caller-supplied check (e.g. a
  per-language "I can't help") on top of the central patterns. Applied by `RefusalScreeningLlmClient`, the
  always-on OUTERMOST front-door layer, so a cached hit is re-screened too; malformed patterns fail open.
- **Curated memory catalog** — `ICuratedMemoryStore` (across InMemory/SQLite/Postgres): a hand-managed
  catalog of `CuratedMemory` entries grouped by `Kind`, each individually enable/disable-able (`Enabled`)
  and editable (`UpdateAsync` with COALESCE semantics) with a `Source` note — distinct from the automatic,
  bounded, dedup/TTL remember/recall *log* (`IMemoryStore`). `CuratedMemorySections.Compose` renders the
  enabled entries into per-kind prompt sections. New `lyntai_curated_memory` table (migration
  `202607180003`, SQLite + Postgres).

### Documented (Sonora-adoption recipes)
- The **"rate-limit → surface"** recipe for single-provider adopters
  (`ConfigureRouting(p => p.On(RateLimited, Surface))` + the `ExemptSoleCandidate` note) — knowledge doc +
  README.

### Fixed
- **Security — denylist guard bypassed via tool calls/attachments** — `DenylistGuard` scanned only message
  text, so a jailed term in a tool-call name/arguments or an attachment URI slipped through. It now scans
  tool-call segments and attachment URIs on the request, and `reply.ToolCalls` on the response.
- **Durable-job poison-pill unbounded on crash-before-run** — a job whose attempts already exceeded
  `MaxAttempts` (e.g. repeatedly claimed then crashed) is now dead-lettered at claim time instead of
  running again.
- **Response cache collided across models** — `ResponseCacheKey` now folds the *effective* (resolved)
  model, so the same request routed to different models no longer serves a cross-model cached reply.
- **Usage-tracker consumer key was case-insensitive** — `InMemoryUsageTracker` now keys consumers
  ordinally (case-sensitive), matching the SQL trackers, so `"App"` and `"app"` bill separately.
- **Semantic recall now fails open** — if the vector backend throws mid-recall, `SemanticMemory.RecallAsync`
  logs and returns no hits (caller cancellation still propagates) rather than failing the whole request.
- **Router misclassified a provider's own cancellation** — a provider that throws
  `OperationCanceledException` for its *own* reasons mid-stream now falls over to the next candidate
  instead of aborting the request; only the caller's cancellation still propagates.
- **In-memory job-claim tiebreaker diverged from SQL** — `InMemoryJobStore` breaks a same-priority,
  same-`available_at` tie by the id string (ordinal), matching the SQL stores' `ORDER BY …, id`.

### Documented
- The soft budget cap's **concurrency overshoot bound** (in-flight calls, not "one past"), the job
  scheduler's **at-least-once** enqueue-then-advance window, the required **constant-time compare** for
  secret/token equality in an `ISecretAccessPolicy`, and the cross-backend memory-recall divergence.
  Corrected the 0.27.1 dimension-mismatch note (in-memory scores 0 since 0.27.2, only Postgres throws).

### Hardening (round-2 review of the new surface)
- **Concurrent step-log reports could lose a step on the SQL job stores** — `ReportStepAsync` is a
  read-modify-write on `step_log`; two concurrent reports from one handler could interleave (InMemory was
  already safe under its lock). Serialized with a per-store lock on both SQL backends.
- **Secret-envelope KDF downgrade / non-crypto exception** — the recovery PBKDF2 iteration count was honored
  from the (possibly tampered) envelope unbounded, and `0` threw `ArgumentOutOfRangeException`. Added a hard
  `MinRecoveryIterations = 100_000` floor enforced at load and at the KDF, as a `CryptographicException`.
- **Envelope `version` was written but never enforced** — a future format opened by this build now throws
  instead of silently misparsing; the transient recovery KEK is zeroed after use.
- **Config + polish** — `JobOptions.MaxStepLog` (env `LYNTAI_JOBS_MAX_STEP_LOG`) makes the step-log cap
  configurable; `CompleteJsonAsync` reuses `JsonExtract.IsValid`; documented that `ICuratedMemoryStore.ListAsync`
  kind-ordering isn't ordinal-stable across backends (the composed prompt re-sorts, so it's stable).

### Refactor (behavior-preserving)
- Consolidated the duplicated "extract a JSON object from an LLM reply, then parse it" scaffolding into
  `JsonExtract.TryParseObject`/`IsValid`; split the ~100-line `AddLyntai` composition root into one focused
  helper per feature area. No behavior change (only the two new `JsonExtract` methods touch the surface).

## 0.27.2 — 2026-07-18

Follow-up hardening from a full-codebase multi-agent code review (45 agents; 35 candidates, 19 refuted by
the review's own verifier). No public API change.

### Fixed
- **Streamed native tool call misclassified as a host failure** — a `StreamAsync` response that was a tool
  call (no text) ended as `Failed` in both the OpenAI-compatible provider (`finish_reason=tool_calls`) and
  the MEAI bridge (`FunctionCallContent`), which cooled down a perfectly healthy host. It now surfaces
  `Refused` (no fallback/cooldown) pointing the caller at `CompleteAsync`. (Full streaming tool-call
  *delivery* stays deferred — the `LlmChunk` streaming contract carries no tool-call payload.)
- **Secret vault: a corrupt/truncated at-rest blob now fails as `CryptographicException`** — `Unprotect`
  used to leak a `FormatException`/`ArgumentOutOfRangeException` from base64 parsing or span slicing;
  callers can now catch one exception type for all at-rest corruption (base64, too-short, or tampered).

### Changed
- **`InMemoryVectorStore` tolerates a dimension mismatch (scores it 0) instead of throwing** — so
  `IVectorStore` behaves consistently across the in-memory / SQLite backends (a stray wrong-dimension row,
  e.g. from a prior embedding model, ranks last rather than sinking the whole search).
- **`DenylistGuard` scans each message directly** (short-circuiting on the first hit) instead of
  allocating a whole-transcript join per request — cheaper on long tool-loop transcripts.

### Reviewed, kept by design
- Incrementing a job's attempt count on a stale-lease reclaim is deliberate poison-pill protection (a
  crash-looping job is bounded by `MaxAttempts`); long handlers renew the lease by checkpointing. The
  per-job cancel poll, the tool-loop's defensive message snapshot, and a few small duplicated helpers were
  judged not worth the churn/risk.

## 0.27.1 — 2026-07-18

Consolidation / hardening pass over v0.16–v0.27 (a three-way adversarial review). No public API change —
all fixes are internal/behavioral.

### Fixed
- **Rate limiter: a cancelled wait now refunds its reserved permit** — a caller that bailed during
  `await Task.Delay(wait)` used to leave the bucket decremented, so a burst of cancellations throttled
  legitimate callers for slots no request ever used.
- **Postgres vector store: a faulted lazy schema no longer bricks the store** — a transient failure on the
  first `CREATE EXTENSION`/`CREATE TABLE` was cached forever (every later call re-threw). It now retries on
  the next call.
- **Cron: inverted/empty ranges are rejected** — `5-3`, `70/5`, `10-40` (out-of-range) parsed cleanly and
  produced a schedule that silently never fired (slipping past `AddCronSchedule`'s eager validation). They
  now throw `FormatException`.
- **Scheduler: an impossible-but-parseable cron (e.g. Feb 30) is quarantined per-schedule** — its
  `Next()` throw no longer aborts the whole tick (skipping later schedules) or spins every poll.
- **Front-door decorators are idempotent** — calling `AddResponseCache`/`AddUsageBudget`/`AddRateLimit`
  twice no longer stacks a second decorator (two rate limiters sharing the singleton would double-charge
  permits).
- **A pre-registered `ILlmClient` + a decorator now throws at composition** instead of silently dropping
  cache/budget/rate-limit governance.
- Postgres response-cache eviction gained a `cache_key` tiebreaker (deterministic trim, matching SQLite);
  `SemanticMemory`'s task+scope separator is now a plain-ASCII `(char)0x1f` constant (was a raw control
  byte in the source); scheduler caches are `ConcurrentDictionary`.

### Known edge cases (documented, not fixed)
- The **pgvector** store can surface a DB error for a **zero-magnitude or non-finite embedding vector**
  (pgvector's `<=>`/parser reject them), where the brute-force in-memory/SQLite stores return a 0 score.
  Real embedders don't emit these for non-empty text. A **dimension mismatch** (e.g. after changing
  embedding models without reindexing) throws in the Postgres store; the in-memory and SQLite stores
  tolerate it (score 0 — in-memory was changed to tolerate in 0.27.2, see above). Either way, reindex on
  a model change.

## 0.27.0 — 2026-07-18

Running-job cancellation — a job that's currently executing can now be stopped (before, only Pending jobs
cancelled). Cooperative: a cancel request sets a flag the runner polls; it cancels the handler's token, and
a handler that honors the token stops. Across all three backends.

### Added
- **`IJobQueue.CancelAsync(id)`** — the single front-door cancel: a Pending job is cancelled outright, a
  Running one has cancellation *requested*.
- **`IJobStore.RequestCancelAsync`** (flag a Running job) + **`CancelRunningAsync`** (the runner marks it
  Cancelled, fenced by worker); **`JobRecord.CancelRequested`**. The runner links a per-job token, polls the
  store (`Jobs.PollInterval`) for the flag, and on seeing it cancels the handler's token → the handler
  stops → the job becomes Cancelled. A cancel already set on a reclaimed (stale-lease) job is honored
  without re-running it. `Replay` clears the flag.

### Breaking (pre-1.0)
- `JobRecord` gains a trailing optional `CancelRequested`; `IJobStore` gains `RequestCancelAsync` /
  `CancelRunningAsync` (custom implementers must add them). The `cancel_requested` column was folded into
  the Jobs migration (pre-release consolidation) — no new migration.

### Notes
- Cancellation is cooperative — a handler must honor its `CancellationToken` to actually stop. Latency is
  up to one `Jobs.PollInterval`.

## 0.26.0 — 2026-07-18

Cron expressions for job schedules — recurring jobs can now run on a real cron schedule, not just a fixed
interval. Dependency-free (a hand-rolled 5-field parser; no cron NuGet pulled into Core).

### Added
- **`AddCronSchedule(name, lane, type, payload, cron, priority)`** — schedule a job on a cron expression.
  The expression is validated at composition (a bad one throws in `AddLyntai`, not silently at tick time).
- **`CronExpression`** (`Parse` / `Next`) — a 5-field UTC cron: `*`, values, ranges `a-b`, steps `*/n` /
  `a-b/n` / `n/step`, comma lists, day-of-week 0–6 (Sunday=0 or 7), the standard day-of-month/day-of-week
  OR rule, and the macros `@hourly @daily @midnight @weekly @monthly @yearly/@annually`.
- `JobSchedule` now carries `Cron` (alongside `Interval`); the scheduler uses the cron's next occurrence
  when set — missed slots still coalesce (the cron's next-after-now skips them).

### Breaking (pre-1.0)
- `JobSchedule.Interval` is now `TimeSpan?` (was `TimeSpan`) and a trailing `Cron` field was added — set
  exactly one of interval/cron. Source-compatible for the existing interval overloads; the positional
  ctor/deconstruct arity changed. Both are normally built via the builder methods.

## 0.25.0 — 2026-07-18

Recurring job scheduling — the last big v0.14-deferred job feature. Register an interval schedule and the
scheduler enqueues a job every interval, durably.

### Added
- **`AddJobSchedule(name, lane, type, payload, every, priority)`** (and a `JobSchedule` overload) — a
  recurring job. **`IJobScheduler`** drives it: `TickAsync` enqueues the due schedules and advances them;
  `RunAsync` loops on `Jobs.PollInterval`. The app owns the pump (host-free), same as the runner.
- Next-run time is **persisted via the key-value store** (keyed by schedule name), so a restart resumes the
  cadence instead of re-anchoring; with no `IKeyValueStore` wired it falls back to in-memory. No new storage
  domain / migration — it reuses `IKeyValueStore`.
- **Missed slots coalesce** into a single enqueue (a ticker that was down doesn't replay a burst); the first
  run waits one interval (no fire-on-startup); a non-positive interval is skipped, not spun.

### Notes
- Interval-based for now (cron expressions are a future enhancement — they'd need a cron parser). Scheduling
  requires durable jobs (the queue throws without a storage backend).

## 0.24.0 — 2026-07-18

Durable-job priorities + a dead-letter queue — two of the deferred v0.14 job features. Across all three
backends (InMemory / SQLite / Postgres), pinned by the shared store contract.

### Added
- **Priorities** — `JobSpec.Priority` (and `IJobQueue.EnqueueAsync(lane, type, payload, priority)`). The
  claim now picks by `priority DESC, available_at, id` — higher runs first within a lane. The claim index
  is recreated to lead with priority (migration `M202607180003`).
- **Dead-letter queue** — exhausted transient retries now go to a new terminal `JobStatus.Dead` (instead of
  a silent `Failed`), which is **inspectable and replayable**: `IJobStore.DeadLetterAsync` /
  `ReplayAsync`, surfaced on the front door as `IJobQueue.ListDeadAsync` / `ReplayAsync`. `Replay` requeues
  a Dead (or Failed) job — Pending, attempts reset, error cleared, available now. The runner dead-letters
  on exhaustion (telemetry outcome `dead`, Error span status); an explicit `JobOutcome.Fail` still → `Failed`.

### Breaking (pre-1.0)
- `JobStatus` gains `Dead`; retries-exhausted jobs are now `Dead`, not `Failed` (an explicit `Fail`
  outcome and the no-handler path stay `Failed`). `JobSpec`/`JobRecord` gain a trailing optional
  `Priority` (source-compatible; positional deconstruct/ctor arity changed). `IJobStore` gains
  `DeadLetterAsync`/`ReplayAsync` (custom implementers must add them).

## 0.23.0 — 2026-07-18

Postgres backends for the governance + semantic-memory seams, mirroring v0.22's SQLite ones — and the
vector store uses **pgvector** so similarity search runs in the database, not brute-force in the app.

### Added (`Lyntai.Storage.Postgres`)
- **`UsePostgresResponseCache()`** — `PostgresResponseCache` (`IResponseCache`): reply JSON + `timestamptz`
  expiry, eviction on write. Persistent and shareable across processes on the same db.
- **`UsePostgresUsageTracking()`** — `PostgresUsageTracker` (`IUsageTracker`): per-consumer rows,
  incremented in place; global total is a `SUM`.
- **`UsePostgresVectorStore()`** — `PostgresVectorStore` (`IVectorStore`) over **pgvector**: the cosine
  `<=>` operator + SQL `ORDER BY … LIMIT k` do the top-k in the database (only the k nearest rows come
  back, vs. loading a whole collection into the app). `ISemanticMemory` persists unchanged.
- Migration `M202607180002_Governance` adds `lyntai_response_cache` / `lyntai_usage`.

### Notes
- **`UsePostgresStorage` does NOT require pgvector.** The vector store creates its `vector` extension +
  `lyntai_vector` table LAZILY on first use (needs rights to `CREATE EXTENSION vector`, or a DBA enabling
  it once) — so only `UsePostgresVectorStore` pulls in pgvector, not the whole storage layer.
- The pgvector column is an unbounded `vector` (dimension-agnostic) and unindexed — the search is exact (a
  sequential scan with pgvector's operator, SQL-side top-k). An ANN index (hnsw/ivfflat, needs a fixed
  embedding dimension) is a future enhancement.
- The Postgres test container image is now `pgvector/pgvector:pg16` (a superset of postgres:16) so the
  vector store's live tests run; all other Postgres tests are unchanged. Tests skip without Docker.

## 0.22.0 — 2026-07-18

Persistent SQLite backends for the governance + semantic-memory seams. The cache, usage tracker, and
vector store shipped with in-memory defaults (in Core); this backs them with SQLite so they survive a
restart — all behind the same interfaces, opt-in, no change to the decorators or `ISemanticMemory`.

### Added (`Lyntai.Storage.Sqlite`)
- **`UseSqliteResponseCache()`** — `SqliteResponseCache` (`IResponseCache`): reply JSON + expiry, eviction
  on write (prune expired, then trim oldest beyond `MaxEntries`). The cache survives restarts.
- **`UseSqliteUsageTracking()`** — `SqliteUsageTracker` (`IUsageTracker`): one row per consumer,
  incremented in place; the global total is a `SUM` across rows — so a usage budget isn't reset every
  deploy.
- **`UseSqliteVectorStore()`** — `SqliteVectorStore` (`IVectorStore`): persistent semantic-memory vectors
  (JSON float arrays), brute-force exact cosine loaded per collection. Plug it in and `ISemanticMemory`
  persists unchanged.
- Migration `M202607180002_Governance` adds `lyntai_response_cache` / `lyntai_usage` / `lyntai_vector`.

### Notes
- These `AddSingleton` over the Core in-memory `TryAdd` defaults (win regardless of call order). Each needs
  the connection factory + schema from `UseSqliteStorage`, so call that first.
- The SQLite vector store is brute-force (not indexed) — persistent and fine to some thousands of vectors
  per collection; a dedicated vector backend (pgvector) is the path for larger corpora. Rate limiting stays
  in-memory by design (a shared limiter is a distributed-cache concern, not SQLite) — its `IRateLimiter`
  seam remains the extension point.

## 0.21.0 — 2026-07-18

Client-side rate limiting — the third front-door governance decorator, completing the trio with response
caching (cost/latency) and usage budgeting (spend): cache · budget · **rate limit** (throughput). All
compose on the same ordered decorator chain.

### Added
- **`AddRateLimit([configure])`** — throttles front-door calls with a token bucket. Over the rate a call
  waits up to `MaxWait`, then is refused (a `RateLimited` reply / an Error stream chunk) without hitting a
  provider. Global rate via `RateLimitOptions` (`PermitsPerSecond` / `Burst` / `MaxWait`) with optional
  per-consumer rates (`ConsumerRate`); also `LYNTAI_RATELIMIT_PERMITS_PER_SECOND` / `_BURST` /
  `_MAX_WAIT_SECONDS`.
- **`IRateLimiter`** (the seam) with the built-in **`TokenBucketRateLimiter`** (continuous refill,
  reservation-based waits, injectable clock). Register your own before `AddRateLimit` for a
  distributed/shared limiter.
- A `lyntai.ratelimit.refusals` counter (tagged by consumer) on the `Lyntai.Agents` meter.

### Composition
- Fold order is now **cache (outer) → budget → rate-limit (inner) → client**, so a **cached hit spends
  nothing** — no budget accounting and no rate-limit permit; the rate limiter throttles only real provider
  calls. Order is deterministic regardless of the order the decorators were added.

## 0.20.0 — 2026-07-18

Semantic memory is now wired into the chat path — the composer and orchestrator use it automatically when
embeddings are registered, closing the "opt-in only" gap from v0.19.

### Changed
- **Hybrid memory recall in `MemoryPromptComposer`** — when an `ISemanticMemory` is present (embeddings
  registered), the composer leads the "Learned facts" section with meaning-based hits, then fills in
  lexical `IMemoryStore` entries, deduped by content and bounded by the same char budget. Fail-open across
  both sources: an outage in either yields whatever the other returned. Lexical-only behavior is unchanged
  when no embedder is wired.
- **`ChatOrchestrator` dual-writes memory** — a remembered exchange is written to both the lexical store
  and semantic memory (when wired), so the next turn's hybrid recall can find it by meaning. Both writes
  are fail-open.
- **Semantic memory is registered only when an embedder is** (`AddEmbeddings`) — absent one, `ISemanticMemory`
  isn't in the container, so the composer/orchestrator resolve null and skip it cleanly (no per-turn throws).

### Breaking (pre-1.0)
- `MemoryPromptComposer` and `ChatOrchestrator` constructors take an added optional `ISemanticMemory?`
  parameter (source-compatible for named/DI use; binary signature changed). Both are normally DI-resolved.

## 0.19.0 — 2026-07-18

Semantic memory — meaning-based recall to complement the lexical memory store. Facts are remembered by
their embedding and recalled by cosine similarity to a query, so retrieval finds relevant memories even
without keyword overlap. Consistent with Lyntai's shape: the app brings the embedding model, Lyntai owns
the recall machinery, and the vector backend is a swappable seam.

### Added
- **`IEmbedder`** (`Lyntai.Embeddings`) — the app-provided embedding model (BYO: an OpenAI/Ollama
  embeddings endpoint, a local model, …), a batch `EmbedAsync` primitive + a single-text convenience.
  Registered with **`builder.AddEmbeddings(...)`**.
- **`ISemanticMemory`** (`Lyntai.Memory`) — `RememberAsync` / `RecallAsync(…, k, minScore)` / `ForgetAsync`,
  scoped by (taskKey, scope) like the lexical store; re-remembering identical content dedups. Auto-wired
  when an embedder is registered; a call throws a clear error if none is.
- **`IVectorStore`** (the vector-persistence seam) with the built-in brute-force **`InMemoryVectorStore`**
  (exact cosine, zero-dependency). Register your own before `AddLyntai` to back recall with pgvector /
  sqlite-vec / a vector DB — the recall logic is unchanged.

### Notes
- The in-memory vector store is exact (brute-force) — fine for up to some thousands of entries per scope;
  for larger corpora or persistence across restarts, plug in a real vector backend via `IVectorStore`.
- First cut: no per-entry TTL on semantic memory (the lexical store keeps that); `ForgetAsync` clears a
  whole scope. Composer/orchestrator integration stays opt-in (call `ISemanticMemory` directly) for now.

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
