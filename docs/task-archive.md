# Lyntai (灵台) — Completed Task Archive

> **This is the ARCHIVE of finished work — the historical implementation plan + closed backlog, kept for
> the record. The ACTIVE backlog lives in [`../TASKS.md`](../TASKS.md) (open tasks only).** Per the
> task-lifecycle rule (`.claude/rules/task-lifecycle.md`), an entry is moved here from `TASKS.md` once it is
> fully done (committed + verified). `CHANGELOG.md` stays the release-facing log; this file is the
> task-level record (why/how, per-task).
>
> **Everything in this file is COMPLETE, by definition** — an entry arrives here only after it is committed
> and verified, so there is no open work below and no "current" state to summarize. Phases 0–7 built the
> library; the numbered Parts that follow are review/adoption hardening, consumer-driven generic gaps, the
> generation platform, the package restructure and the provider-lifetime seam.
>
> **This banner deliberately carries no counts.** It used to name a Part number and a test total, and both
> rotted — it read "Parts 0–12 · 866 tests" while the archive had reached Part 39 and the suite 1573, which
> is exactly the false "everything below is complete" summary the task-lifecycle rule warns about. For what
> is current: open work is [`../TASKS.md`](../TASKS.md), releases are `../CHANGELOG.md`, and the test/gate
> totals are whatever `node devtools/dev.mjs verify` prints today.
>
> _(Original header preserved below for the record.)_

---

> **Status: phases 0–7 + roadmap v0.3–v0.29 implemented** (agentic tool-calling, durable jobs, guards,
> secrets, semantic memory, three storage backends, governance decorators, storage feature toggles,
> actor/mailbox jobs, …). See `CHANGELOG.md` for per-release detail and `docs/ROADMAP.md` for the forward
> sequence.

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended)
> or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox syntax.
>
> **Read `docs/2026-07-17-lyntai-design.md` first** — it pins every interface, the fork decisions, the
> fallback/CLI semantics, and what's out of scope. This file is the *sequence*; the spec is the *contract*.

**Goal:** a NuGet-packable, DI-first .NET 10 library giving a new app an LLM provider abstraction
(routing + fallback across CLI / API / MEAI-bridged providers) and pluggable SQLite storage plus the
LLM-ops layer (prompt registry, scoring, traces, task-scoped memory) — `AddLyntai(...)` and go.

**Architecture:** `Lyntai.Core` (interfaces + router/fallback + cortex + DI, no heavy deps) with adapter
packages that depend only on Core: `Lyntai.Storage.Sqlite`, `Lyntai.Providers.ClaudeCli`,
`Lyntai.Providers.OpenAiCompatible`, `Lyntai.Providers.ExtensionsAi`. Composed via DI; no adapter
references another. Verified by `tests/Lyntai.Tests` and the `samples/Lyntai.Playground` smoke.

**Tech stack:** net10.0 · C# 13 · Dapper · FluentMigrator · Microsoft.Data.Sqlite · FTS5 (trigram) ·
Microsoft.Extensions.{DependencyInjection,Http,AI} · xUnit · Node-based devtools (`dev.mjs`).

**Conventions (mirror the family — see `.claude/rules/dev-conventions.md`):** _[2026-08-05: that rule file
was retired in the canonical-rule sync; its content now lives in `.claude/rules/dotnet-package-layout.md`
(boundaries, naming, variation points) and `.claude/rules/repo-mechanics.md` (this repo's bindings). The
original wording is kept below because an archive is a record.]_ modules = interface in Core
+ impl in adapter; async Dapper + `snake_case` columns + `CAST(x AS REAL)` for doubles; FluentMigrator
numbered `YYYYMMDDNNNN` (never reuse); variation points are DI collections, never if/else; BOM-less UTF-8
sources; TDD (failing test first); commit per task. **Never commit without the user's approval.**

---

## Phase 0 — Solution & build scaffolding

Goal: `node devtools/dev.mjs build` and `node devtools/dev.mjs test` both green with an empty-but-real
solution. (devtools, `.gitignore`, `Directory.Build.props`, `.claude/`, `CLAUDE.md` are pre-seeded by the
planning session — verify, don't recreate.)

- [x] **0.1** `git init` (Lyntai gets its own repo, sibling to the others). Then `node devtools/dev.mjs install-hooks`.
- [x] **0.2** Create `Lyntai.slnx` referencing the projects created below.
- [x] **0.3** Create `src/Directory.Packages.props` (central package management) — pin: Dapper, FluentMigrator.Runner.SQLite, Microsoft.Data.Sqlite, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Http, Microsoft.Extensions.AI (+ .Abstractions), Microsoft.Extensions.Logging.Abstractions, xUnit, xunit.runner.visualstudio, SQLitePCLRaw.bundle_e_sqlite3.
- [x] **0.4** Create empty projects + one `Class1`-free placeholder each so the solution builds:
      `src/Lyntai.Core`, `src/Lyntai.Storage.Sqlite`, `src/Lyntai.Providers.ClaudeCli`,
      `src/Lyntai.Providers.OpenAiCompatible`, `src/Lyntai.Providers.ExtensionsAi`,
      `samples/Lyntai.Playground` (Exe), `tests/Lyntai.Tests` (xUnit). Each `src/*` is `<IsPackable>true</IsPackable>`
      with package metadata; Playground/Tests are not packable.
- [x] **0.5** Add project references: every adapter + Playground + Tests → `Lyntai.Core`; Tests → all adapters.
- [x] **0.6** One trivial passing xUnit test (`SmokeTests.SolutionBuilds`) so `dev.mjs test` has something green.
- [x] **0.7** Commit: `chore: solution scaffolding + central package management`.

**Acceptance:** `dev.mjs build` restores + builds all 7 projects; `dev.mjs test` runs 1 passing test;
`dev.mjs check-sensitive --tree` is clean.

---

## Phase 1 — Core abstractions (`Lyntai.Core`)

Goal: every interface/type from spec §5 exists; the pure logic (router fallback, dedup, cooldown, prompt
render + placeholder guard, scoring aggregation, `FtsQuery`, `ProcessRunner`) is unit-tested. No provider
or DB yet — router is tested against fake in-memory `ILlmProvider`s.

- [x] **1.1 LLM value types** — `Llm/LlmMessage.cs`, `LlmRequest.cs`, `LlmReply.cs`, `LlmChunk.cs`, `LlmUsage.cs`, `LlmTool.cs`, `LlmVerdict.cs`, `LlmCandidate.cs`. Records exactly as spec §5.1. Test: construction + record equality. Commit.
- [x] **1.2 `ILlmProvider` / `ILlmRouter` interfaces** — `Llm/ILlmProvider.cs`, `Llm/ILlmRouter.cs`. No impl yet. Commit.
- [x] **1.3 `DeadHostTracker`** — `Llm/DeadHostTracker.cs`: N consecutive fails → cooldown window; any success resets; thread-safe (lock). **Inject a clock** (`Func<DateTimeOffset>` / `TimeProvider`) — no `DateTime.Now` in logic, so tests are deterministic. Tests: fails-below-threshold stays live; hits-threshold goes dead; success resets; cooldown expiry re-lives. Commit.
- [x] **1.4 Candidate dedup** — `Llm/CandidateDedup.cs`: drop repeat `(providerId, model)`, first wins, preserve order. Tests: dup primary stripped; order preserved; empty → empty. Commit.
- [x] **1.5 `LlmRouter` (non-streaming fallback)** — `Llm/LlmRouter.cs` implementing `ILlmRouter.CompleteAsync`. Semantics from spec §6: dedup → try in order → `Failed`/`Timeout` advances, `RateLimited` circuit-breaks (stop, surface), `Refused` surfaces (no fallback), skip dead hosts, log each attempt (`ILogger`). Tests (fake providers returning scripted verdicts): first-ok returns it; first-failed→second-ok; all-failed→last error; rate-limited stops immediately; refused stops immediately; dead provider skipped. Commit.
- [x] **1.6 `LlmRouter` (streaming, no-fallback-after-token)** — `StreamAsync`: pre-content error advances to next candidate; **once any content chunk is yielded, errors pass through unchanged**. Tests: pre-content failure falls over; mid-stream error after a token is passed through (no second candidate invoked); success streams straight through. Commit.
- [x] **1.7 `ProcessRunner`** — `Process/ProcessRunner.cs`: `UseShellExecute=false`, `ArgumentList` only, stdin write (BOM-less UTF-8), stdout/stderr capture (BOM-less UTF-8), per-call timeout, `Kill(entireProcessTree:true)` on cancel/timeout, resolved-path cache (`where.exe`/`which`, prefer `.cmd`/`.exe`). Tests (spawn `dotnet --version` or a tiny node script): captures stdout; honors timeout→kill; passes stdin through. Commit.
- [x] **1.8 `IPromptRegistry` + `PromptRegistry`** — `Prompt/IPromptRegistry.cs`, `Prompt/PromptRegistry.cs`: override key `lyntai.prompt.<name>` from `IKeyValueStore`, `{placeholder}` fill, **reject an override that drops a placeholder present in the default**. Tests: no-override renders default+vars; override wins; missing-placeholder override rejected (throws/falls back to default — pick one, document it); unknown `{var}` left literal or errors (document). Commit.
- [x] **1.9 Cortex interfaces** — `Cortex/IScorer.cs`, `Cortex/LlmScorerBase.cs` (abstract; one-shot judge via `ILlmRouter`, parses `{score,reason}`), `Cortex/IScoringService.cs`, `Cortex/ScoreModels.cs` (`ScoreContext`, `ScoreResult`, `ScoredResult`), `Cortex/ITraceService.cs` + `TraceModels.cs` (`RunTrace`, `TraceStep` — kind/label/tokens/cost/durationMs). Interfaces + models only. Commit.
- [x] **1.10 `ScoringService`** — `Cortex/ScoringService.cs`: iterate `IEnumerable<IScorer>`, skip `null` results, aggregate. Tests: two fake scorers both run; a scorer returning null is omitted; grouping preserved. Commit.
- [x] **1.11 Storage domain interfaces** — `Storage/IKeyValueStore.cs`, `Storage/IConversationStore.cs`, `Storage/IMemoryStore.cs`, `Storage/IScoreStore.cs`, `Storage/ITraceStore.cs`, `Storage/IDbConnectionFactory.cs`, plus DTOs (`Thread`, `ChatMessage`, `MemoryEntry`, etc.). Interfaces + DTOs only; impls in Phase 2. Commit.
- [x] **1.12 `FtsQuery`** — `Storage/FtsQuery.cs`: drop `<3`-char tokens, quote the rest, OR-join; return null when nothing usable (caller falls back to LIKE). Tests: short tokens dropped; special chars quoted; all-short → null. Commit.
- [x] **1.13 DI builder** — `DependencyInjection/LyntaiBuilder.cs` + `ServiceCollectionExtensions.AddLyntai(...)`. `LyntaiBuilder` collects provider registrations, storage registration, scorer registrations, default candidate order; `AddLyntai` wires `ILlmRouter`, `IPromptRegistry`, `IScoringService`, `ITraceService` into DI. Provider/storage-specific `Add*`/`Use*` extension methods live in their adapter packages but extend `LyntaiBuilder`. Test: `AddLyntai` with a fake provider + in-memory KV resolves `ILlmRouter` and round-trips a completion. Commit.
- [x] **1.14 Options + env overrides** — `LyntaiOptions.cs`: timeouts, cooldown threshold/window, default model per consumer; bind from config + `LYNTAI_*` env. Tests: env override beats config; defaults applied. Commit.

**Acceptance:** `dev.mjs test` green; router fallback + streaming semantics + prompt guard + dead-host + dedup all covered by passing unit tests; no I/O in Core tests.

---

## Phase 2 — SQLite storage (`Lyntai.Storage.Sqlite`)

Goal: every storage domain interface has a working SQLite implementation, migrated + FTS-indexed, verified
by integration tests against a temp db. `builder.UseSqliteStorage(path)` wires them all.

- [x] **2.1 `SqliteConnectionFactory`** — `IDbConnectionFactory` impl: `MatchNamesWithUnderscores=true` (static ctor), pooled `Open()` with `PRAGMA journal_mode=WAL; busy_timeout=5000; foreign_keys=ON`. Test: opens, pragmas applied, round-trips a scalar. Commit.
- [x] **2.2 Migration runner + base** — `Migrations/` with FluentMigrator wiring; `MigrationRunnerService` discovers + applies on `UseSqliteStorage`. Test: fresh temp db → runner applies → `VersionInfo` populated. Commit.
- [x] **2.3 Migration `202607170001_KeyValue`** + `KeyValueStore` — `app_config(key PK, value, updated_at)`. Tests: set/get/delete; overwrite updates `updated_at`; missing key → null. Commit.
- [x] **2.4 Migration `202607170002_Conversation`** + `ConversationStore` — `thread`, `message` tables (FK message→thread, `foreign_keys=ON`). Tests: create thread, append messages, list by thread ordered, delete cascades. Commit.
- [x] **2.5 Migration `202607170003_Memory` (+ FTS5 trigram)** + `MemoryStore` — `memory_entry` external-content `memory_fts` (trigram) kept in sync by AFTER INSERT/DELETE/UPDATE triggers, backfilled in-migration. Recall via `FtsQuery` MATCH + `bm25()`, LIKE fallback; task/scope filter; bounded (cap entries), fail-open (recall never throws on empty/short query). Tests: remember→recall by substring (incl. a CJK substring, proving trigram); scope filter; cap enforced; short-query LIKE fallback. Commit.
- [x] **2.6 Migration `202607170004_Score`** + `ScoreStore` — persist `ScoredResult`s per session (`CAST(score AS REAL)` in SELECTs). Tests: save+load; double round-trips exactly (guards the affinity trap). Commit.
- [x] **2.7 Migration `202607170005_Trace`** + `TraceStore` — `run_trace` + `trace_step`. Tests: save trace with steps, load by session, token/cost totals preserved. Commit.
- [x] **2.8 `UseSqliteStorage` extension** on `LyntaiBuilder` — registers factory + all five stores + runs migrations. Test: `AddLyntai(b => b.UseSqliteStorage(tempDb))` resolves every store interface and each round-trips. Commit.

**Acceptance:** integration tests green against a per-test temp db (created, migrated, deleted); FTS CJK-substring recall proven; no double-affinity regressions.

---

## Phase 3 — Claude CLI provider (`Lyntai.Providers.ClaudeCli`)

Goal: `builder.AddClaudeCliProvider()` yields an `ILlmProvider` (`Id="claude-cli"`) that spawns the
authenticated `claude` CLI via `ProcessRunner`, parses stream-json, maps to `LlmReply`/`LlmChunk` +
verdict. Tested entirely against the **provider-stub** (no real tokens).

- [x] **3.1 Arg builder** — `ClaudeArgs.cs`: static argv (`--output-format stream-json`, `--verbose`, model, disallowed UI tools); dynamic content (the prompt) goes via stdin, never argv. Unit test: argv shape; prompt never in argv. Commit.
- [x] **3.2 stream-json parser** — `StreamJsonParser.cs`: translate `{system,assistant,user,result}` lines → `LlmChunk`/text + `LlmUsage` + cost. Unit test against captured fixture lines (incl. the stub's output). Commit.
- [x] **3.3 `ClaudeCliProvider`** — `CompleteAsync` + `StreamAsync` over `ProcessRunner`; `CLAUDE_CMD` / `LYNTAI_PROVIDER_CMD` env override (points at the stub in tests); no-output → `Failed` with stderr tail in `Detail`; timeout → `Timeout`. Integration tests (env → provider-stub): completion returns stub text + `Ok`; empty-output → `Failed`; streaming yields chunks then done. Commit.
- [x] **3.4 `AddClaudeCliProvider`** extension on `LyntaiBuilder`. Test: registered, resolvable via router by id. Commit.

**Acceptance:** provider tests green against the stub with zero network/real-CLI dependency; spawn hygiene (ArgumentList, stdin, kill-tree) exercised.

---

## Phase 4 — OpenAI-compatible provider + router end-to-end (`Lyntai.Providers.OpenAiCompatible`)

Goal: an HttpClient-based provider covering OpenAI/Ollama/OpenRouter-style endpoints, with URL-native
detection + payload normalization, wired so the **router fallback works across a CLI provider and an HTTP
provider**.

- [x] **4.1 Provider detection** — `ProviderDetect.cs`: hostname/path shape → `openai` | `ollama` | …, fail-open to OpenAI-compat. Host-match must be exact/subdomain (not substring — guard `anthropic.com.evil.com`). Tests table-driven. Commit.
- [x] **4.2 Payload builders** — `Payloads/OpenAiPayload.cs`, `Payloads/OllamaPayload.cs`: canonical `LlmRequest` → provider schema (Ollama tool `arguments` as object vs OpenAI string; `num_ctx`; `response_format` for structured output). Unit tests: message mapping; tool-arg normalization; schema round-trip. Commit.
- [x] **4.3 `OpenAiCompatibleProvider.CompleteAsync`** — `HttpClient` (from `IHttpClientFactory`), map HTTP status → verdict (429→`RateLimited`, 5xx/timeout→`Failed`/`Timeout`, content-filter→`Refused`), tolerant JSON extraction. Tests against a stubbed `HttpMessageHandler`: 200→`Ok`+text; 429→`RateLimited`; 500→`Failed`; malformed→one-retry→`Failed`. Commit.
- [x] **4.4 `OpenAiCompatibleProvider.StreamAsync`** — SSE parse (`data:` lines, `[DONE]`), first-token marks committed. Tests: chunks parsed in order; `[DONE]` terminates; pre-content 500 surfaces as error chunk (lets router fall over). Commit.
- [x] **4.5 `AddOpenAiCompatibleProvider(id, cfg)`** extension (BaseUrl, apiKey, default model, dead-host wired to `DeadHostTracker`). Commit.
- [x] **4.6 Router end-to-end integration** — in Tests: `AddLyntai` with claude-cli (stub) + openai-compatible (stubbed handler) + `DefaultCandidates`. Tests: primary-fails→secondary-serves; streaming never falls back after a token across the two real provider types; dead-host cooldown skips a downed provider then re-tries after expiry. Commit.

**Acceptance:** two heterogeneous providers behind one router; all §6 fallback semantics proven end-to-end.

---

## Phase 5 — Cortex layer implementations

Goal: prompt registry, scoring (incl. an LLM judge), traces, and task-scoped memory work end-to-end over
the stores + router.

- [x] **5.1 Wire `PromptRegistry` to `IKeyValueStore`** (Phase 1.8 used a fake) — integration test: override persisted in SQLite KV changes the rendered prompt. Commit.
- [x] **5.2 Two built-in deterministic scorers** — e.g. `OutcomeScorer`, `StructureScorer` in `Cortex/Scorers/` (generic, no domain assumptions; document what each checks). Tests. Commit.
- [x] **5.3 One `LlmScorerBase` judge scorer** (e.g. `RelevancyScorer`) — runs through the router; against the provider-stub's `SCORING TASK` path returns a deterministic `{score,reason}`. Integration test. Commit.
- [x] **5.4 `ScoringService` → `IScoreStore`** — evaluate persists results. Integration test: evaluate a context, results readable from the store. Commit.
- [x] **5.5 `TraceService` → `ITraceStore`** — `Begin`/record steps/token+cost totals persisted; `GetAsync` reads back. Integration test. Commit.
- [x] **5.6 `MemoryStore` composition helper** — task-scoped recall bounded + appended to a prompt (the `IPromptComposer`-style helper from Sonora, fail-open). Integration test: remembered facts surface in a composed prompt; outage → prompt still renders. Commit.

**Acceptance:** the LLM-ops loop (prompt override → run → score → trace → remember) works against SQLite + the stubbed router.

---

## Phase 6 — MEAI bridge, sample, e2e

Goal: any `Microsoft.Extensions.AI` `IChatClient` becomes a Lyntai provider; the Playground exercises the
full stack; the devtools e2e harness is green.

- [x] **6.1 `ExtensionsAiProvider`** — `Providers/ExtensionsAiProvider.cs`: adapt `IChatClient` → `ILlmProvider` (map `LlmRequest`↔`ChatMessage`/`ChatOptions`, streaming via `GetStreamingResponseAsync`, usage, verdict from exceptions). Tests against a fake `IChatClient`. Commit.
- [x] **6.2 `AddExtensionsAiProvider(id, IChatClient)`** extension. Test: a fake `IChatClient` serves through the router by id. Commit.
- [x] **6.3 `Lyntai.Playground`** — console app: `AddLyntai` with SQLite + claude-cli + an openai-compatible endpoint + default candidates; run a completion, score it, persist a trace, recall memory; print results. Honors `LYNTAI_PROVIDER_CMD` so it runs against the stub with no real tokens. Commit.
- [x] **6.4 devtools e2e** — `devtools/scripts/e2e/p1.mjs`: boot the Playground against a temp data dir with `LYNTAI_PROVIDER_CMD` = provider-stub, assert it completes + wrote a trace + a memory row. Wire into `dev.mjs e2e`. Commit.

**Acceptance:** `dev.mjs e2e` green (Playground full-stack smoke against the stub); MEAI bridge round-trips a fake `IChatClient`.

---

## Phase 7 — Packaging & docs

Goal: the library is consumable as NuGet packages with a clean README.

- [x] **7.1 Package metadata** — per `src/*` csproj: `PackageId` (`Lyntai.Core`, …), description, authors, license, repo url, `PackageReadmeFile`. Version from `src/Directory.Build.props` (`VersionPrefix`).
- [x] **7.2 `dev.mjs pack`** — `dotnet pack` all packable projects → `publish/packages/*.nupkg`; print ids + sha256. Commit.
- [x] **7.3 README** — the §10 "consuming Lyntai" story: install, `AddLyntai(...)`, the four provider/storage add-ons, a minimal working snippet. Commit.
- [x] **7.4 Final self-review** — `dev.mjs test` + `dev.mjs e2e` + `dev.mjs check-sensitive --tree` all green; spec §5 interfaces all implemented; out-of-scope items (§9) genuinely absent.

**Acceptance:** `dev.mjs pack` produces restorable packages a throwaway consumer project can `AddLyntai` against and run a stubbed completion.

---

# Review follow-up (2026-07-18) — active backlog

Backlog from an independent review of v0.27.2 (four adversarial reviewers over LLM core/governance,
jobs/agents/guards/secrets, storage/memory, plus a Sonora-adoption gap analysis). Findings with a
`file:line` were verified in code. **Part 1** = confirmed defects the 571 tests + prior review passes
missed. **Part 2** = capabilities Lyntai still lacks for the Sonora app to adopt it. Read
`.claude/knowledge/pitfalls.md` first; several of these are "passing tests ≠ correct" — add the test that
would have caught the bug.

## Part 1 — Review fixes

### Bugs (correctness / security) — do these first

- [x] **T1 · Denylist jail bypass on the native tool-calling path** (security)
  - Files: `src/Lyntai.Core/Guards/DenylistGuard.cs`, models `src/Lyntai.Core/Llm/LlmMessage.cs` (`ToolCalls`,
    `Attachments`), `src/Lyntai.Core/Llm/LlmReply.cs` (`ToolCalls`).
  - Defect: `InspectRequestAsync` scans only `req.Messages.Select(m => m.Content)`. An assistant tool-call
    turn carries `Content=""` with the payload on `.ToolCalls` (name + `ArgumentsJson`); image `.Attachments`
    (`Uri`) and `reply.ToolCalls` are never scanned. A denied term in tool arguments/attachments slips the jail.
  - Fix: in `Check`, also project each message's `ToolCalls?.Select(c => c.Name + " " + c.ArgumentsJson)` and
    `Attachments?.Select(a => a.Uri ?? "")`; in `InspectResponseAsync` also scan `reply.ToolCalls`.
  - Test: a request whose only occurrence of a denied term is inside an assistant tool-call's `ArgumentsJson`
    (and one in an attachment `Uri`) → `GuardOutcome.Block`; a clean tool-call turn → `Allow`.

- [x] **T2 · Durable-job poison-pill is unbounded on a worker crash**
  - Files: `src/Lyntai.Core/Jobs/JobRunner.cs` (`RunJobAsync`), and the three stores' `ClaimNextAsync`
    (`SqliteJobStore.cs`, `PostgresJobStore.cs`, `InMemoryJobStore.cs`).
  - Defect: `MaxAttempts` is enforced only in `ApplyAsync` (runs when a handler throws/returns). A worker that
    dies before the handler returns leaves the job `Running`; the stale-lease reclaim increments `attempts` and
    re-runs it forever — no `attempts > MaxAttempts` check at claim/run. Contradicts the v0.27.2 changelog.
  - Fix: at the top of `RunJobAsync` (after the `CancelRequested` short-circuit), if `job.Attempts > job.MaxAttempts`
    dead-letter it and return WITHOUT invoking the handler. Confirm `attempts` is incremented on claim in all
    three stores so the bound trips.
  - Test: reclaim a stale `Running` job whose `attempts` already exceeds `MaxAttempts` → the runner dead-letters
    it and the handler is NEVER invoked (fake handler asserts it wasn't called).

- [x] **T3 · Response-cache cross-model collision when per-consumer default models differ**
  - Files: `src/Lyntai.Core/Llm/Caching/IResponseCache.cs` (`ResponseCacheKey.For`),
    `src/Lyntai.Core/Llm/Caching/CachingLlmClient.cs`, `src/Lyntai.Core/LyntaiOptions.cs` (`ResolveModel`).
  - Defect: the cache is the outermost decorator and keys on the RAW `req.Model`, excluding `req.Consumer`. But
    the router resolves the effective model via `options.ResolveModel(req.Consumer, …)` (`DefaultModelByConsumer` /
    `LYNTAI_MODEL_<CONSUMER>`). Two consumers with different per-consumer defaults, both sending `req.Model=null` +
    identical messages, share a key → the first's answer is served to the second (wrong model).
  - Fix: fold the effective model into the key — in `CachingLlmClient` compute
    `options.ResolveModel(req.Consumer, req.Model)` and pass it to a `ResponseCacheKey.For(req, effectiveModel)`
    overload that hashes it (keep `req.Consumer` OUT so two consumers resolving to the same model still share).
  - Test: two consumers, distinct `DefaultModelByConsumer` entries, `req.Model=null`, identical messages →
    DISTINCT keys / no shared hit; two consumers resolving to the same model → shared hit.

- [x] **T4 · Streaming `finish_reason=tool_calls` emits a spurious `Refused` after content**
  - File: `src/Lyntai.Providers.OpenAiCompatible/OpenAiCompatibleProvider.cs` (the `finishReason == "tool_calls"`
    branch, ~203).
  - Defect: the branch is NOT gated on `!sawContent`, so a stream that interleaves content deltas then ends
    `finish_reason:tool_calls` yields a trailing `Error(Refused)` after valid content. The MEAI twin
    (`ExtensionsAiProvider`) already gates on `!sawContent`.
  - Fix: `if (finishReason == "tool_calls" && !sawContent)`. When content already streamed, fall through to the
    normal `Final(usage)` (streaming tool-call delivery is deferred by design).
  - Test: SSE with `finish_reason:tool_calls` and NO content → a single `Error/Refused` chunk (not `Failed`); SSE
    with content deltas THEN `finish_reason:tool_calls` → content chunks + a benign `Final`, no trailing `Error`.

### Cross-backend divergences (the three storage backends disagree)

- [x] **T5 · Memory recall matches "any token" (SQLite FTS) vs "contiguous phrase" (Postgres/InMemory)** — documented (ranking can't converge without in-app bm25) + cross-backend guarantee test
  - Files: `src/Lyntai.Core/Storage/FtsQuery.cs`, `SqliteMemoryStore.cs`, `PostgresMemoryStore.cs`,
    `InMemoryMemoryStore.cs`. A multi-word query → SQLite `"a" OR "b"` (either-token hits) while Postgres
    `ILIKE %a b%` / InMemory `Contains("a b")` match only the contiguous substring. Same query, different results.
  - Fix (pick one, document it): AND-join FTS tokens for phrase parity, OR document the per-backend recall/ranking
    difference on `IMemoryStore` XML doc + `.claude/knowledge/storage.md`. Test: a shared cross-backend two-word
    recall test asserting the documented behavior.

- [x] **T6 · Usage-budget consumer key: InMemory case-insensitive vs SQL case-sensitive**
  - Files: `InMemoryUsageTracker.cs` (keys `OrdinalIgnoreCase`), `SqliteUsageTracker.cs`, `PostgresUsageTracker.cs`
    (case-sensitive PK). Budget totals diverge by backend for `App` vs `app`. Fix: converge — cheapest is
    `StringComparer.Ordinal` in the in-memory tracker. Test: `Record("App")` + `Record("app")` → consistent totals
    across every backend (parametrized).

- [x] **T7 · pgvector throws on a dimension-mismatched row; semantic recall isn't fail-open**
  - Files: `PostgresVectorStore.cs` (`SearchAsync`), `src/Lyntai.Core/Memory/SemanticMemory.cs` (`RecallAsync`).
    pgvector's `<=>` errors on a differing-dimension row (InMemory/SQLite score 0), and `RecallAsync` has no
    try/catch → all semantic recall breaks on Postgres after a model swap. Fix: make `RecallAsync` fail-open
    (catch/log/return `[]`, rethrow only OCE); document reindex-on-model-change on `IEmbedder`/`ISemanticMemory`.
    Test: a wrong-dimension row → recall returns `[]`, doesn't throw.

### Lower (risk / nit)

- [x] **T8 · Router treats a provider's own `OperationCanceledException` as caller-cancel** —
  `src/Lyntai.Core/Llm/Routing/LlmRouter.cs` (streaming catch): narrow to
  `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)` so only the caller's cancel aborts;
  a provider-side OCE becomes a fall-over-able Error chunk (the router is the trust boundary). Test: a fake
  provider throwing a bare OCE (ct not cancelled) pre-content → falls over to the next candidate.
- [x] **T9 · Budget cap not atomic under concurrency** — `BudgetedLlmClient.cs`: check-then-act lets N concurrent
  calls all pass the cap (overshoot = in-flight N, not "one past"). Reserve-then-reconcile, or tighten the doc to
  state the overshoot bound.
- [x] **T10 · Scheduler enqueues before persisting the advance** — `JobScheduler.cs`: a crash between enqueue and
  `SetNextAsync` re-runs the slot. Persist-then-enqueue, or document the at-least-once semantics on `IJobScheduler`.
- [x] **T11 · InMemory job-claim tiebreaker diverges from SQL** — `InMemoryJobStore.cs`: add `.ThenBy(j => j.Id)`
  after `AvailableAt` to match SQLite/Postgres `…, id` (deterministic same-tick same-priority order).
- [x] **T12 · Access-gate constant-time compare guidance** — `src/Lyntai.Core/Secrets/ISecretVault.cs`: XML-doc
  warning that any token/secret equality inside an `ISecretAccessPolicy` must use
  `CryptographicOperations.FixedTimeEquals`. Doc-only.
- [x] **T13 · Stale changelog** — `CHANGELOG.md` v0.27.1 "Known edge cases" still says the in-memory store throws
  on a dimension mismatch; v0.27.2 changed it to score 0. Correct the note.

## Part 2 — Sonora-adoption gaps (features Lyntai lacks)

Sonora (the sibling Sonora repo) can adopt Lyntai for its LLM client, providers, CLI spawn, structured
output, jobs core, and storage — and would *upgrade* several (multi-worker jobs, cron, DLQ, priority, model
routing). These are the pieces it still needs before dropping its own code. Priority order; fold into
`docs/ROADMAP.md` as next versions if Sonora adoption is a goal.

- [x] **S1 · Portable secret vault: DPAPI protector + recovery-key DEK envelope** (highest value)
  - `Lyntai.Secrets` is AES-GCM with a BYO key only — no DPAPI, no recovery key, no `Recover()`, no
    machine-portability. Add a `DpapiSecretProtector` (Windows `ProtectedData`, guarded by `OperatingSystem.IsWindows()`)
    and a DEK-envelope vault mode (random 256-bit DEK encrypts secrets, double-wrapped by DPAPI + a PBKDF2 recovery
    key; `GenerateMasterKey`/`Recover`/machine-fingerprint). Keep AES-GCM/BYO as the portable default. (Mirror
    Sonora `…/Modules/Core/Services/SecretVault.cs`.) Tests: DPAPI round-trip; recover via key on a "different
    machine"; tamper → `CryptographicException`.
- [x] **S2 · Job admission-control seam + first-class `Paused` state**
  - Sonora's `CapacityGovernor` (external GPU/CPU-load-aware lane throttling + schedule window) has no Lyntai hook,
    and there's no `Paused` status. Add `IJobAdmissionController` the runner consults per lane before
    `ClaimNextAsync` (allow / hold-lane) + `JobStatus.Paused` with pause/resume on `IJobQueue`/`IJobStore` across
    all three backends. App owns the load sampling. Tests: a controller that holds a lane → no claims for it;
    pause/resume round-trips on every backend.
- [x] **S3 · Live job progress + step reporting on `JobContext`**
  - Lyntai exposes only `SaveCheckpointAsync`; Sonora's UI needs `ReportProgressAsync(done,total,stage)` +
    `ReportStepAsync(msg)`. Add them (new `JobRecord` `Progress`/`Total`/`Stage`/`StepLog` fields + a migration
    across backends, or an event stream). Tests: progress/steps round-trip and are readable while the job runs.
- [x] **S4 · Per-request refusal-pattern seam** — add optional `LlmRequest.RefusalPattern` (or a classifier hook)
  so a caller can supply an extra refusal check on the reply text (Sonora passes a per-language regex per call).
  Keep the central patterns as default. Test: a reply matching a per-request pattern → `Refused`.
- [x] **S5 · Document the "rate-limit → surface" recipe for single-provider adopters** — Sonora wants a 429 to
  hard-stop (protect the quota window), not cool-and-advance; with a sole candidate, `ExemptSoleCandidate` would
  even retry it. README/knowledge recipe: `ConfigureRouting(p => p.On(RateLimited, Surface))` (+ note
  `ExemptSoleCandidate`). Doc-only (capability exists).
- [x] **S6 · (nice-to-have) curated-memory variant of `IMemoryStore`** — Sonora's is a curated catalog
  (`Kind`/`Enabled`/`Source` + `UpdateAsync` + per-kind prompt sections); Lyntai's is a remember/recall log.
  Optionally add a curated-entry model + `UpdateAsync` + per-kind composition. Otherwise Sonora keeps its own
  memory module — acceptable; deprioritize.

---

## Part 3 — Review round 2 (2026-07-18)

Findings from reviewing the Part 1/2 work (T1–T13 + S1–S6, all confirmed COMPLETE — the four bugs fixed
correctly + tested, the refactor behavior-preserving, S1 crypto core sound: fresh per-op nonces, PBKDF2
@210k, authenticated tag, no plaintext DEK on disk). Three real issues remain on the newly-added surface,
plus nits. All verified in code; none catastrophic.

- [x] **N1 · Concurrent step-log reports lose steps on the SQL backends** (BUG)
  - Files: `src/Lyntai.Core/Jobs/JobContext.cs` (`ReportStepAsync`/`ReportProgressAsync`),
    `src/Lyntai.Storage.Sqlite/SqliteJobStore.cs` (~64-71), `src/Lyntai.Storage.Postgres/PostgresJobStore.cs`.
  - Defect: `ReportStepAsync` is a read-modify-write across two round-trips (`GetAsync` → `JobStepLog.Append`
    → fenced `UPDATE`) with no serialization. Two concurrent (or un-awaited) reports from ONE handler interleave
    → a step is lost. `InMemoryJobStore` appends under its lock, so it's safe there — a cross-backend divergence
    too.
  - Fix: serialize the per-job reporters — they all target the one running row. Cheapest: a `SemaphoreSlim` (or
    lock) in `JobContext` guarding `ReportStepAsync`/`ReportProgressAsync`/`SaveCheckpointAsync` for that job.
    (Or an atomic SQL append — harder on SQLite.)
  - Test: fire N concurrent `ReportStepAsync` from a handler; assert all N steps land, on every backend (add to
    `JobStoreContract`).

- [x] **N2 · Recovery-KDF iteration count is honored from the envelope with no floor** (crypto hardening)
  - File: `src/Lyntai.Core/Secrets/SecretKeyEnvelope.cs` (`FromJson` ~138; `UnwrapWithRecoveryKey` /
    `DeriveRecoveryKek` ~90/169).
  - Defect: `RecoveryIterations` is read straight from JSON and fed to `Pbkdf2` unbounded. A tampered/portable
    envelope can downgrade the KDF (e.g. `1`), and `iterations <= 0` throws `ArgumentOutOfRangeException` — a
    leaked non-`CryptographicException` on the corrupt-envelope path. Practical brute-force risk is low (192-bit
    random recovery key), but the iteration count is meant to be a code-owned invariant.
  - Fix: in `FromJson`/unwrap, reject `RecoveryIterations < DefaultRecoveryIterations` (or a hard floor like
    100_000) as a `CryptographicException`.
  - Test: an envelope with `recoveryIterations` of `1` (and `0`) → `CryptographicException`.

- [x] **N3 · Envelope `version` is written but never enforced on read** (risk)
  - File: `src/Lyntai.Core/Secrets/SecretKeyEnvelope.cs` (`FromJson` ~134).
  - Defect: `version` is parsed as `?? 1` then discarded — a future v2 envelope opened by v1 code silently
    misparses as v1 instead of being rejected.
  - Fix: after parsing, `if (version > CurrentVersion) throw new CryptographicException(...)`; carry the parsed
    version onto the record. Test: a version-bumped envelope → `CryptographicException`.

- [x] **N4 · Nits (batch, non-blocking)**
  - Step-log cap is hard-wired to `JobStepLog.DefaultCap = 200` — add `JobOptions.MaxStepLog` and thread it into
    the three stores' `Append` calls.
  - Zero the transient recovery KEK after use — `try/finally { CryptographicOperations.ZeroMemory(kek); }` in
    `WrapWithRecovery`/`UnwrapWithRecoveryKey` (`SecretKeyEnvelope.cs`). Defense-in-depth (the long-lived DEK is
    effectively unscrubbable — don't chase it).
  - `CompleteJsonAsync` (`src/Lyntai.Core/Llm/LlmStructuredExtensions.cs`) still hand-rolls `IsParseable` —
    replace with `JsonExtract.IsValid(json)` (finish the round-1 refactor's dedup).
  - Document that `ICuratedMemoryStore.ListAsync` order isn't ordinal-stable across backends (Postgres uses DB
    collation; `Compose` re-sorts ordinal, so the composed path is fine) — one line on the interface.
  - Add the two missing tests: an `IJobAdmissionController` that THROWS → treated as hold, pump survives; and
    `UpdateAsync(source: "")` clears the source on every backend.

---

## Part 4 — Consumer-driven gaps (Sonora integration)

Surfaced while evaluating **Sonora** (a real consumer) adopting Lyntai for its LLM + agentic site-study
layer. Sonora is a strong fit — drop-in `AddClaudeCliProvider`, the `LlmVerdictClassifier`, routing/fallback
(which would fix its `CLI_TIMEOUT`), and the prompt/memory cortex all map cleanly; the only friction on
Sonora's side (re-authoring its `[McpServerTool]` study tools as `ITool`s) is Sonora's own migration, not a
Lyntai gap. One genuine Lyntai gap:

- [x] **C1 · Per-request timeout override (for the CLI-agent / long-tool-loop path)**
  - Files: `src/Lyntai.Core/Llm/LlmRequest.cs` (add the field); the providers that honor it (esp. the spawn
    timeout in `src/Lyntai.Providers.ClaudeCli`); wherever the global `LyntaiOptions.ProviderTimeout` is applied.
  - Need: the timeout today is a single global option. Sonora's site-study drives the **claude CLI's own agent
    loop** via `AddClaudeCliMcpTools()` — ONE `claude -p` call that fetches/renders/test-renders many pages over
    10+ minutes — while its other calls (translation, one-shot) are short. Raising the global timeout to fit the
    agentic run over-waits every short call; keeping it short kills the study (that's Sonora's current
    `CLI_TIMEOUT`). Routing/fallback only helps if a second provider is configured.
  - Fix: add `LlmRequest.TimeoutSeconds? { get; init; }` (and/or a per-`Consumer` timeout map in options),
    honored by providers over the global default; clamp to a sane ceiling; null = the global.
  - Test: a request whose `TimeoutSeconds` exceeds the global still completes when the provider runs longer than
    the global; a short-timeout request cancels at its own value.
  - Note: the alternative — a consumer adopts Lyntai's `IToolLoop` (Lyntai-orchestrated, short per-turn calls) —
    sidesteps this, but the CLI-agent path (`ClaudeCli.Mcp`) that Lyntai ships should still support a per-call
    budget larger than the global.

---

## Part 5 — Adoption gaps: cortex + scoring (2026-07-18)

Trying to replace a real adopting app's (Gatherlight's) hand-rolled **cortex** (prompt/model tuning) and
**scoring** with Lyntai's surfaced where Lyntai is the runtime CORE but not yet the full adoptable surface —
a wholesale swap today would *regress* the app. Goal: make Lyntai's cortex+scoring genuinely adoptable so a
real app can retire its own `ScoringService`/`ScoreRepository` + cortex model-routing with **no regression**.

**HARD requirements (a migration REGRESSES the product without these — do first): A1, A2, A3, A6, A8.**
Should-have: A7. Nice-to-have / polish: A4, A5. Each is a generic library improvement, not app-specific.
**Definition of done:** after A1–A3 + A6, the adopting app's scoring framework + cortex model tuning move
onto Lyntai losing nothing (no duplicate score rows, the eval aggregate UI still works, dry runs don't
persist, per-scorer judge model preserved, model retuning takes effect live).

### Scoring
- [x] **A1 · `IScoreStore`: upsert + cross-session aggregate + bulk export — HARD (blocker)**
  - Today it's `SaveAsync` (append-only INSERT) + `GetAsync(session)`. Without this the eval UI breaks and
    re-scoring corrupts data. Needs: (a) **upsert** on `(session_id, scorer_id)` (re-scoring REPLACES, not
    accumulates — add the unique/PK + ON CONFLICT); (b) a cross-session per-scorer **aggregate**
    (`AVG(score), COUNT` grouped by scorer); (c) a **bulk/all-sessions read/export** (`session_id, scorer_id,
    score` dump for a tuning dataset). Files: `src/Lyntai.Core/Storage/IScoreStore.cs`, the three
    `*ScoreStore.cs` impls + the score migration on each backend + `JobStoreContract`-style cross-backend tests.
- [x] **A2 · `IScoringService`: evaluate WITHOUT persisting even when a store is wired — HARD (blocker)**
  - `ScoringService.EvaluateAsync` auto-saves whenever an `IScoreStore` is registered, so a dry/preview path
    can't score without writing rows. Add an overload/flag (`EvaluateAsync(ctx, persist: false)`) or split
    evaluate-vs-persist. Files: `src/Lyntai.Core/Cortex/{IScoringService,ScoringService}.cs`.
- [x] **A3 · `LlmScorerBase`: per-scorer model + consumer hook — HARD (blocker)**
  - It hardcodes the default candidates + `Consumer="scoring"`, so every judge runs on the default model — a
    real app routes cheap judges to a cheap model (e.g. haiku) per scorer. Let the subclass/ctor set a `Model`
    + `Consumer` threaded into the `CompleteJsonAsync` request. File: `src/Lyntai.Core/Cortex/LlmScorerBase.cs`.
- [x] **A4 · `IScorer.Description` (optional) — nice-to-have** — an admin "list scorers" view wants a human
    description beyond `Name`. Add an optional `Description` (default "") or document it as app-owned.
    File: `src/Lyntai.Core/Cortex/IScorer.cs`.
- [x] **A5 · Document the `ScoreContext.Extra` domain-dimension pattern — nice-to-have** — domain scorers put
    their dimensions (phase/mode/changed-files) into `Extra` (stringly-typed; list values must be serialized).
    Document this as the intended extension pattern on `ScoreContext`, or add a typed-context helper.
    Files: `src/Lyntai.Core/Cortex/ScoreModels.cs` + `.claude/knowledge/`.
- [x] **A8 · `LlmScorerBase`: an applicability skip hook (don't judge N/A dimensions) — HARD (blocker)**
  - `LlmScorerBase.ScoreAsync` ALWAYS calls the judge — a subclass can't say "this dimension doesn't apply
    to this context" before spending tokens (`BuildJudgePrompt` returns a non-null `string`; `ScoreAsync`
    isn't virtual). Real judge scorers are conditional (a "faithfulness" dimension applies to a plan, not a
    code-edit turn); without a skip they call the LLM for every context and record a score where there
    should be none. Fix: make `BuildJudgePrompt` return `string?` (null → skip, `ScoreAsync` returns null),
    OR add `protected virtual bool Applies(ScoreContext ctx) => true` checked before the judge call.
    (Without it, an adopting app must re-implement `LlmScorerBase` locally just to get the skip — defeating
    the point of the base.) File: `src/Lyntai.Core/Cortex/LlmScorerBase.cs`.
  - Test: a scorer whose `Applies`/`BuildJudgePrompt` says "no" returns null WITHOUT calling the client
    (assert the fake client saw no calls).

### Cortex
- [x] **A6 · Live per-consumer model override read into `ResolveModel` — HARD (blocker)**
  - Model routing (`DefaultModelByConsumer` + `LYNTAI_MODEL_<CONSUMER>`) resolves from code config + env at
    STARTUP, so an admin-set model retune never takes effect without a restart. Add an optional KV-backed
    `IModelRoutingStore` that `ResolveModel`/`LlmRouter` consults LIVE (KV override → per-consumer default →
    "default" → provider default), mirroring how `IPromptRegistry` reads a live prompt override. Files:
    `src/Lyntai.Core/LyntaiOptions.cs` (ResolveModel), `src/Lyntai.Core/Llm/Routing/LlmRouter.cs`.
- [x] **A7 · Surface the placeholder-contract violation to the caller — should-have**
  - `PromptRegistry.RenderAsync` enforces "an override must keep the default's `{placeholders}`" but only
    LOGS + silently falls back. An admin save-flow needs to REJECT with the exact missing tokens (the app can
    pre-validate today, but the library should own it). Add a `TryValidateOverride(name, defaultTemplate,
    candidate) → missing[]` (or a strict render mode) the app calls before persisting.
    File: `src/Lyntai.Core/Prompts/PromptRegistry.cs`.

### Not gaps (app-owned — recorded so they aren't re-raised)
The prompt catalog (names/labels/descriptions/groups/placeholder lists), the model-consumer catalog +
suggestions, the `/api/manage/*` controllers + client panels, the domain scorers, `BuildContext` (rebuild
a score context from the app's own session/event tables), and backlog orchestration all correctly stay in
the adopting app. Lyntai renders / stores / versions / scores; the app owns its domain metadata + UI.

---

## Part 6 — Agentic self-driving-agent session (generic primitive) (2026-07-19)

> **✅ DONE (G1a · G2a · G1b · G2b · G3).** Shipped as designed (one as-built deviation: the adapter
> parser is a new stateful `StreamJsonAgentReader`, not an edit to the static `StreamJsonParser`).
> Build clean · 725 tests · e2e 3/3 · leak scan clean.
>
> **Design contract:** `docs/2026-07-19-agent-session-design.md` — the neutral surface, the OpenAI
> Responses API neutrality stress-test that shaped it, the `IAgentSession`-vs-`IToolLoop` boundary, and
> the three resolved decisions.

Surfaced trying to migrate a real adopter's (Gatherlight's) **interactive two-gate chat** — plan
(read-only) → human approve → execute (write, scope-guarded) → human review diff → commit — off its
hand-rolled native `ClaudeCliRunner` onto Lyntai. This is the ONE remaining Lyntai gap blocking that
adopter, **and the prerequisite for its cortex migration** (Part 5): the app's cortex — prompt/model
tuning — is overwhelmingly consumed by THIS flow, so cortex can't move onto Lyntai until the flow does.

Lyntai already drives an LLM two ways — `ILlmProvider` (`ClaudeCliProvider`: one-shot text reply, a
neutral cwd) and `IToolLoop`/`ChatOrchestrator` (**Lyntai** orchestrates a ReAct loop over registered
`ITool`s, in-proc — the tool calls come back to us). **Neither fits the two-gate**, which lets **the
agent drive its OWN loop** (many tool turns inside one `claude -p`, executing its own tools) against the
**app's own out-of-process MCP server + a scope-guard hooks file**, and needs a **rich streamed event
surface + session resume across the human gate**. That is a genuinely new capability, and — per the
design decision — a **generic primitive**: the interface + event model live in **Core (`Lyntai.Agents`)**,
the `claude` CLI is **adapter #1** (`Lyntai.Providers.ClaudeCli`). No `claude` flag leaks into Core; a
future Codex/Gemini-CLI/OpenAI-Responses adapter reuses the surface unchanged. `IAgentSession` sits
**beside** `IToolLoop`, never folded into it (the boundary is: a tool call handed back to the caller to
execute is `IToolLoop`'s job, not this).

Reference implementation to generalize (a straight port of the *adapter half*): the adopter's native
runner — Gatherlight `src/server/Gatherlight.Server/Modules/Llm/Services/ClaudeCliRunner.cs` +
`ClaudeRunOptions` / `AgentEvent` / `EditTracker`.

**HARD (a two-gate adopter can't migrate without these): G1a+G1b, G2a+G2b.** Should-have: G3. Each is a
generic library primitive (Core) or its first adapter, not app-specific.

### G1a · `AgentStreamEvent` event model — Core (`Lyntai.Agents`) — HARD (blocker)
- The neutral, reusable event vocabulary — the self-driving-agent counterpart to `Agents.ToolStep`, so it
  belongs in Core next to it (**not** in the adapter). A sealed hierarchy (consumers `switch` on type),
  in `src/Lyntai.Core/Agents/`:
  - `abstract record AgentStreamEvent;`
  - `SessionStarted(string SessionId)`; `TextDelta(string Text)`; `Thinking(string Text)`;
  - `ToolCall(string Name, string ArgumentsJson, string? CallId = null)` — **no `filePath`** (that is
    claude's Edit/Write tool schema, not universal; the adapter ships a helper, see G2b);
  - `ToolResult(string? CallId, string Content, bool IsError)` — `CallId` correlates to its `ToolCall`;
  - `UsageLive(long Input, long Output, long CacheRead)` (per assistant turn);
  - `UsageFinal(long Input, long Output, long CacheRead, long CacheCreate, string? Model)` (per run — RAW
    counts + the ACTUAL model id, so an app prices from its own table; deliberately NOT `LlmUsage`, which
    lacks `CacheCreate`/model and is the priced path);
  - `SessionEnded(LlmVerdict Verdict, bool IsError, string? Subtype, string? SessionId, string? FinalText,
    string? Diagnostic)` — the **single** terminal event (folds the old separate `Done`+`Error`); a
    no-output run is diagnosable via `Verdict`/`Subtype`/`Diagnostic`, never silent. `Diagnostic` (neutral)
    is where the CLI adapter packs its stderr tail.
- Also in Core: `enum AgentToolPolicy { ReadOnly, Write }`.
- Test (Core, fakes, no I/O): pattern-match exhaustiveness over the hierarchy; `AgentSessionResult` (G2a)
  folds correctly from a synthetic event stream.

### G1b · `StreamJsonAgentReader` emits the events — adapter (`Lyntai.Providers.ClaudeCli`) — HARD (blocker)
- Today `StreamJsonParser` (`src/Lyntai.Providers.ClaudeCli/StreamJsonParser.cs`) recognizes only
  `assistant` text blocks → `AssistantText` and `result` → `Result`; `system/init`, `stream_event`
  partial deltas, `assistant` tool_use, and `user` tool_result all fall to `Other` and are dropped.
- Fix: add a new stateful, per-run `StreamJsonAgentReader` (NOT an edit to the static `StreamJsonParser`)
  to emit the G1a events from the fuller stream-json — `system`/init → `SessionStarted`;
  `stream_event` `content_block_delta` → `TextDelta`/`Thinking` (needs `--include-partial-messages`, set
  in G2b); `assistant` tool_use → `ToolCall`, `message.model` + per-turn usage → `UsageLive`; `user`
  tool_result → `ToolResult`; `result` `subtype`/`is_error` → `SessionEnded`. With partial messages on,
  text comes from the deltas; the consolidated `assistant` block is **not** re-emitted as text (drives
  `UsageLive` only) — avoid double-counting. **Leave the existing `StreamJsonEvent` → text path and the
  `LlmChunk`/`ILlmProvider` mapping unchanged** — no provider regression; factor the extraction so the
  provider still collapses to text.
- Test (captured fixture lines + the stub): `system/init` → `SessionStarted` with the id; an `assistant`
  tool_use block → `ToolCall` with name (+ args); partial `text_delta`/`thinking_delta` →
  `TextDelta`/`Thinking`; `result` with `is_error:true, subtype:"error_max_turns"` → `SessionEnded`
  carrying both; raw per-run token counts + model id on `UsageFinal`; a malformed line still ignored (no
  throw); **the existing provider text path still collapses correctly** (regression guard).

### G2a · `IAgentSession` + options + result — Core (`Lyntai.Agents`) — HARD (blocker)
- The neutral session contract, in `src/Lyntai.Core/Agents/`:
  - `interface IAgentSession { IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentSessionOptions options,
    CancellationToken ct = default); }` — the streaming door (adapters implement only this). Plus a
    `AgentSessionExtensions.RunAsync(this IAgentSession, options, Action<AgentStreamEvent>? onEvent = null,
    ct)` extension that folds the stream to `AgentSessionResult` — the result door, written ONCE (DRY),
    mirroring `ILlmProvider.StreamAsync`/`CompleteAsync`. Both consumption doors first-class.
  - `record AgentSessionOptions` — the neutral per-call inputs: `Prompt` (required; travels over stdin,
    never argv), `SystemPrompt?`, `ToolPolicy` (default `ReadOnly`), `ResumeToken?` (**opaque string** —
    claude session id / OpenAI `previous_response_id`; the resume-across-the-gate mechanism),
    `Model?`, `TimeoutSeconds?` (null = the global; reuse C1), `DisallowedTools`, and `WorkingDirectory?`
    (**on the base** by resolved decision 1 — documented "CLI-agent adapters run the loop here; adapters
    without a filesystem context ignore it").
  - `record AgentSessionResult(string? SessionId, string FinalText, LlmVerdict Verdict, bool IsError,
    string? Subtype, string? Diagnostic, UsageFinal? Usage)` — the caller-facing outcome.
- `IAgentSession` is a **second, sanctioned front door** (distinct from the `ILlmClient` completion door)
  and sits **outside** the router: no cross-provider fallback mid-agent-loop.
- Test (Core, fakes): a fake `IAgentSession` whose `StreamAsync` yields a hand-driven event sequence →
  `RunAsync` folds to the right `AgentSessionResult` (SessionId from `SessionStarted`;
  Verdict/FinalText/Subtype/Diagnostic from `SessionEnded`; Usage from the last `UsageFinal`); `onEvent`
  fires once per streamed event in order.

### G2b · `ClaudeAgentSession` + args + DI — adapter (`Lyntai.Providers.ClaudeCli`) — HARD (blocker)
- `record ClaudeAgentOptions : AgentSessionOptions` — adds the claude-specific flags (all
  adapter-confined): `SettingsPath?` (`--settings`, the scope-guard **hooks file** / PreToolUse jail —
  the adopter's security boundary, forwarded verbatim), `McpConfigPath?` + `AllowedTools` (`--mcp-config`
  /`--allowedTools` — the app's **externally-hosted** out-of-process MCP server, distinct from and
  composing with the in-proc `ICliToolProvisioner`/`AddClaudeCliMcpTools`).
- `ClaudeAgentArgs.Build(ClaudeAgentOptions)` (generalize the reference `ClaudeCliRunner.BuildArgs`):
  `-p --output-format stream-json --verbose --include-partial-messages`; `ReadOnly` ⇒ `--disallowed-tools
  Edit,Write,NotebookEdit` (+ the caller set, default also `AskUserQuestion`/`ExitPlanMode`/`EnterPlanMode`
  — flow tools that hang a headless run); `Write` ⇒ `--permission-mode acceptEdits`; `--settings`,
  `--mcp-config`/`--allowedTools`, `--resume` (from `ResumeToken`), `--model` forwarded. Prompt via stdin
  only, never argv.
- `ClaudeAgentSession : IAgentSession` — runs ONE `claude -p` turn over `ProcessRunner`, cwd =
  `WorkingDirectory` (the deliberate inverse of `ClaudeCliProvider.NeutralWorkingDirectory` — the
  interactive gate loads the app's `CLAUDE.md`/knowledge; must be per-call, never the neutral cwd),
  kill-tree on cancel, the `CLAUDE_CMD`/`LYNTAI_PROVIDER_CMD` stub seam (token-free e2e); stream-json →
  G1a events; prompt written on a **background task** so a large prompt can't deadlock the stdout drain;
  bounded stderr tail → `SessionEnded.Diagnostic`; final result classified via `LlmVerdictClassifier`.
- `ClaudeToolCalls.FilePathOf(evt)` — adapter convenience (resolved decision 2): extracts `file_path` from
  a `ToolCall`'s `ArgumentsJson` for the app's `EditTracker`, keeping claude's tool schema out of Core.
- `AddClaudeCliAgentSession()` builder extension (resolve `IProcessRunner` + `LyntaiOptions` + logger;
  honor the stub env), mirroring `AddClaudeCliProvider`.
- Test (against the stub): read-only argv denies the write tools and omits `acceptEdits`; write argv adds
  `--permission-mode acceptEdits`; `SettingsPath`/`McpConfigPath`/`AllowedTools`/`ResumeToken` all land in
  argv; the prompt never appears in argv; a stubbed `system/init` → the result `SessionId`; a stubbed
  tool_use transcript → ordered `ToolCall`/`ToolResult` events then `SessionEnded`; an empty stub run →
  `SessionEnded` carrying subtype + `Diagnostic` (stderr tail); cancel mid-stream kills the process tree;
  `FilePathOf` pulls the path from an Edit tool_use's args.

### G3 · docs + stub transcript + e2e — should-have
- Extend the provider-stub (`devtools/scripts/provider-stub.mjs`) with a marker that emits a deterministic
  multi-tool agentic transcript (system/init → assistant text + tool_use → user tool_result → result), so
  G1/G2 tests + an e2e stay token-free. Add a `devtools/scripts/e2e/pN.mjs` that runs a read-only then a
  resumed write session against the stub and asserts the event sequence + the session resume. README: a
  "**CLI-agent session vs `IToolLoop`**" section — when the agent drives its own loop against the app's
  tools/gates (this) vs when Lyntai orchestrates the loop over registered `ITool`s (that) — plus the
  Core/adapter split. Update the `Lyntai.Core` **and** `Lyntai.Providers.ClaudeCli` API baselines
  deliberately (new public surface in both).

### Not gaps (app-owned — recorded so they aren't re-raised)
The two-gate **orchestration** (plan→approve→execute→review→commit state machine), the scope-guard hook
**content** (the jail policy/script + its `GUARD_VERSION` re-issue), the app's **MCP server + tool
registry**, **edit-tracking → git stage/diff/commit** (the adopter's `EditTracker` just consumes a
`ToolCall`'s args via `ClaudeToolCalls.FilePathOf`), the **SSE bridge + the app's own event wire shape**,
and **model-pricing tables** all stay in the adopting app. Lyntai ships the session **primitive** — spawn
+ gate flags + rich streamed events + resume + diagnosable termination; the app builds the gated
review/commit product on top. Completing G1+G2 unblocks the adopter's two-gate migration, which unblocks
its cortex migration (Part 5).

---

## Part 7 — App-owned storage: use your own table, no duplication (2026-07-19)

Surfaced adopting Lyntai's **cortex + conversation storage** in Gatherlight. The design goal (the user's,
verbatim): *Lyntai is the cortex library with the render/validate/routing/MCP/tuning logic built-in; the app
plugs its OWN table behind Lyntai's interfaces — single source of truth, nothing stored twice, and no unused
Lyntai tables for domains the app owns.* Three gaps block that: the hardcoded cortex KV key prefix (**P1**),
a conversation model too chat-specific to adopt an app's typed event stream (**P2**), and all-or-nothing
storage wiring + hardcoded table names (**P3**). The adopter already has `app_config` (cortex) and
`chat_session`/`chat_event` (conversations) and does NOT want them duplicated into `lyntai_kv` /
`lyntai_thread` / `lyntai_message`.

**The KV seam already exists and is the right one:** `IPromptRegistry`/`KeyValueModelRoutingStore` both take a
nullable `IKeyValueStore` (`TryAdd`), so an app can register its own `IKeyValueStore` (over its existing
config table) and Lyntai's cortex operates on it — no `lyntai_kv` copy. This is good; keep it.

**The one gap that forces a shim:** the key prefixes are HARDCODED — `PromptRegistry.KeyPrefix =
"lyntai.prompt."` and `KeyValueModelRoutingStore.KeyPrefix = "lyntai.model."`. An app whose existing keys
are `cortex.prompt.*` / `llm.model.*` must wrap its store in a **prefix-translating adapter** just to reuse
its own rows — awkward, and easy to get wrong. To truly "expose an interface to use your own table," let the
app tell Lyntai its prefix.

**The seam already exists and is the right one:** `IPromptRegistry`/`KeyValueModelRoutingStore` both take a
nullable `IKeyValueStore` (`TryAdd`), so an app can register its own `IKeyValueStore` (over its existing
config table) and Lyntai's cortex operates on it — no `lyntai_kv` copy. This is good; keep it.

**The one gap that forces a shim:** the key prefixes are HARDCODED — `PromptRegistry.KeyPrefix =
"lyntai.prompt."` and `KeyValueModelRoutingStore.KeyPrefix = "lyntai.model."`. An app whose existing keys
are `cortex.prompt.*` / `llm.model.*` must wrap its store in a **prefix-translating adapter** just to reuse
its own rows — awkward, and easy to get wrong. To truly "expose an interface to use your own table," let the
app tell Lyntai its prefix.

- [x] **P1 · Configurable KV key prefix on the cortex stores — should-have (adoption)** ✅ done 2026-07-19
      — `keyPrefix` ctor arg on both stores + `LyntaiOptions.PromptKeyPrefix`/`ModelKeyPrefix`; `KeyPrefix`
      const → `DefaultKeyPrefix` + instance property; KV table renamed `lyntai_app_config` → `lyntai_kv`.
  - Files: `src/Lyntai.Core/Prompts/PromptRegistry.cs` (`KeyPrefix` → a ctor-injected value, default
    `"lyntai.prompt."`), `src/Lyntai.Core/Llm/Routing/IModelRoutingStore.cs` (`KeyValueModelRoutingStore.KeyPrefix`
    → ctor-injected, default `"lyntai.model."`), and the `AddLiveModelRouting()` / prompt-registry DI
    registrations (`ServiceCollectionExtensions` / `LyntaiBuilder`) to thread an optional override
    (e.g. `LyntaiOptions.PromptKeyPrefix` / `ModelKeyPrefix`, or params on the Add* methods).
  - Behavior: default UNCHANGED (`lyntai.prompt.` / `lyntai.model.`), so existing consumers are unaffected;
    an app can set `cortex.prompt.` / `llm.model.` to point Lyntai straight at its own keys — no translating
    shim, no duplication, existing overrides honored as-is.
  - Test: a `PromptRegistry` built with prefix `cortex.prompt.` reads an override stored under
    `cortex.prompt.plan` in a fake KV; `KeyValueModelRoutingStore` with prefix `llm.model.` reads
    `llm.model.chat`; defaults still read the `lyntai.*` keys.
  - Note: `IKeyValueStore` itself needs no change — it's already the app-owned-storage seam. This is purely
    making the key NAMESPACE the app's, so Lyntai's logic sits over the app's single table cleanly.

- [x] **P2 · Generic conversation store — a typed event stream, not just role/text chat — should-have (generic capability)** ✅ done 2026-07-20
      — `ChatMessage` gains `Kind`/`Payload` (Role/Content kept as aliases); `ChatThread` gains opaque
      `Metadata` + `SetThreadMetadataAsync`; message columns renamed role→kind/content→payload, thread
      `metadata` column added (migrations in-place); all 3 backends + contract tests. (Agent-session
      dogfood wiring deferred — see R4.)
  - A conversation is, in general, a **typed multi-kind event stream** — text, tool-call, tool-result,
    usage, thinking, phase/status, error — not only user/assistant chat turns. **Lyntai already produces
    exactly this shape natively**: the Part 6 agent session's `AgentStreamEvent` and `IToolLoop`'s
    `ToolStep` are typed events. So a store that can persist a typed event stream is a *first-party* Lyntai
    capability (persist an agent transcript / tool-loop run), not just an adopter concern — today there's
    nowhere to durably record those runs. `ChatMessage(Id, ThreadId, **Role**, **Content**, CreatedAt)` only
    models a chat turn, so neither Lyntai's own transcripts nor an adopter's event log fit it.
  - Motivating adopter (one example of the general shape): Gatherlight's `chat_event` is
    `(thread_id, **seq**, **kind**, **payload_json**, created_at)` with `kind` ∈ {phase, text, tool,
    tool-result, usage, error, done}; its session-level `phase`/`plan_text`/`commit_sha` are just
    PROJECTIONS of typed events in that stream. This is representative, not special.
  - Fix (design it generic): give `ChatMessage` a generic **`Kind`/`Type`** (superset of role) and a
    structured **`Payload`** (JSON string, superset of `Content`), keep `Id` as the store-assigned **seq**;
    give `ChatThread` optional **metadata** (small key→value / JSON) for thread-level state without a bespoke
    per-app column. Backward-compatible: role→kind, content→payload with chat as the default shape. Ideally
    wire the agent session / tool loop to be able to persist their event stream through it (dogfood the
    generality).
  - Files: `src/Lyntai.Core/Storage/IConversationStore.cs` (records + interface),
    `src/Lyntai.Storage.Sqlite/SqliteConversationStore.cs` + its migration (add `kind`/`payload`/thread
    metadata columns; keep role/content as views or the default kind), the InMemory/Postgres twins + the
    `ConversationStore` contract tests.
  - Test: append messages of mixed `Kind` (phase/text/tool) with JSON payloads, read back in seq order;
    thread metadata round-trips; the plain role/content chat path still works.

- [x] **P3 · App-owned storage — REDESIGNED per the design principle — should-have (adoption)** ✅ done 2026-07-20
  - **Design correction (user, 2026-07-20):** the original P3 premise (point Lyntai's SQL at the app's OWN
    `chat_session`/`chat_event` tables via configurable table names) is *wrong* — it makes the ADOPTER manage
    Lyntai's schema version. The design is: **Lyntai OWNS and manages the LLM storage** (its `lyntai_*` tables
    + migrations); the app adds ADDITIONAL INFO on top. So:
    - Delivered the **enrichment** (P2): `ChatMessage` is a superset matching complex event-stream systems
      (GUID `Id`, per-thread `Seq`, `Kind`, `Payload`, per-message `Metadata`) + thread `Metadata` — so an
      adopter's existing event table already conforms and there's little schema drift to manage.
    - Delivered the **`IConversationEnricher`** DI-collection seam (`AddConversationEnricher<T>`): add your
      own info via a focused interface, invoked after each write — NOT by forking the store.
    - Kept the **BYO-impl** escape hatch: an app can register its own `IConversationStore`/`IKeyValueStore`
      impl for a genuinely custom backend (wins via `TryAdd` — see R1). `migrate:false` still lets an app own
      the schema entirely.
    - **Dropped** the configurable-table-names work (contrary to the design) and the opt-in store-selection /
      selective-migration idea (unused empty `lyntai_*` tables are cheap; app-owned tables are the BYO path).

### Not a gap (recorded)
`IKeyValueStore` (logic-backed) and the pure-storage interfaces (`IConversationStore`, `IMemoryStore`, …)
are the correct app-owned-storage seams — an app supplies its own impl to use its own table. Prompt VERSION
history in `lyntai_prompt_version` is Lyntai's own domain table (not a duplicate of app data), consistent
with "Lyntai manages its own tables." The gaps: the hardcoded KV key prefix (P1) forces a shim over a
logic-backed store; the conversation model is too chat-specific to adopt an event stream (P2); and
all-or-nothing wiring + hardcoded table names (P3) create/duplicate tables for domains the app owns.

---

## Part 8 — "Generic + sustainable" review sweep (2026-07-19)

A 6-agent parallel read of the whole codebase (LLM core · agents/cortex/prompts · storage ×3 backends ·
providers · jobs/secrets/memory/guards · DI/options/docs) against two axes: **generic** (no single-consumer
leakage, configurable seams, DI-strategy variation not if/else) and **sustainable** (cross-backend parity,
tested, documented, safe to evolve). `file:line` noted — **verify each in code before fixing** (a couple may
be intentional/by-design). Excludes items already filed in Parts 5–7. Overall verdict: the library is
genuinely strong (policy-driven fallback, clean provider/decorator seams, honest fail-open docs, real
crypto discipline) — these are refinements + a few real correctness/consistency gaps, not structural rot.

### High
- [x] **R1 · "Plug your own impl" is broken for storage + the README claim is false** (generic/sustainable) ✅ done 2026-07-20 — Sqlite+Postgres domain stores now `TryAddSingleton` (match InMemory); pre-registered app impl wins; README claim now true; AddEmbeddings audited (explicit registration, plain Add correct).
  - `README.md:~295` says "anything you register wins (defaults use `TryAdd`)", but
    `src/Lyntai.Storage.Sqlite/SqliteStorageBuilderExtensions.cs:46-62` registers every domain store with
    plain `AddSingleton` — so a pre-registered app impl does NOT win; last-registration-wins by ORDER. This
    directly undercuts the "use your own table/impl" goal (Part 7). Fix: make the storage-domain
    registrations `TryAdd` (matching the InMemory/secrets packages + `IPromptRegistry`), OR correct the
    README + document "register after `Use*Storage`". Audit `AddEmbeddings` (`LyntaiBuilder.cs:236`) for the
    same Add-vs-TryAdd inconsistency.
- [x] **R2 · Guards don't cover the agent tool loop** (sustainable — security) ✅ done 2026-07-20 — `IGuardRail.InspectToolCallAsync`/`InspectToolResultAsync` (default methods reusing existing guards); `ToolLoop` gates each call's args + observation, Block→abort(Refused), Replace→rewrite; DI-wired.
  - `ChatOrchestrator`/`ToolLoop` gate only the initial user message + final answer; when `UseTools` is on,
    model-emitted tool-call `ArgumentsJson` and tool observations flow UN-guarded (`Agents/ToolLoop.cs:91-96`,
    `Agents/ChatOrchestrator.cs:54-57`). `DenylistGuard` was deliberately extended to scan `ArgumentsJson` +
    attachment URIs, but nothing invokes the rail inside the loop — a denied term in a tool call or an exfil
    via a tool observation bypasses the jail. Fix: give `ToolLoop` an `IGuardRail`/per-tool-call hook (gate
    each call's args + observation), or document loudly that guards are a chat-gate boundary only.
- [x] **R3 · Response-gate `Replace` only rewrites `Text`, leaving `ToolCalls`/`Detail`** (sustainable — security) ✅ done 2026-07-20 — response Replace now clears `ToolCalls`+`Detail` too (GuardedLlmClient + rail re-threading); replacement is the whole sanitized reply.
  - `Guards/GuardRail.cs:69` + `GuardedLlmClient.cs:29`: `InspectResponseAsync` scans Text+Detail+ToolCalls
    but a `Replace` outcome does `reply with { Text = … }`, so denied content in `ToolCalls`/`Detail` passes
    through un-redacted. Fix: on response `Replace` also clear/rewrite `ToolCalls`+`Detail`, or treat a hit
    outside `Text` as `Block`-only.
- [x] **R4 · Trace subsystem is orphaned from the agent flows** (sustainable) ✅ done 2026-07-20 — chosen: document `ITraceService` as the BYO/app-driven persisted-trace API; OTel Activity spans are the automatic path. Clarified in `ITraceService` XML-doc + README Observability. (No auto-wiring — ChatTurn has no session id, and OTel already covers auto-observability.)
  - `ITraceService.Record` is called nowhere in `src/` except tests. `ToolLoop`/`ChatOrchestrator` emit OTel
    `Activity` spans (`LyntaiDiagnostics`) but never a `TraceStep`, and the batteries-included orchestrator
    persists no trace — though "run traces" is a headline cortex feature. Fix: wire `ITraceService` into the
    orchestrator/loop (phase/llm/tool steps), or document `ITraceService` as BYO + the auto path is OTel-only.
- [x] **R5 · Cross-backend parity is under-verified (Postgres false-green + missing shared contracts)** (sustainable) ✅ done 2026-07-20 — `[SkippableFact]`+`Skip.IfNot` (Xunit.SkippableFact) so Postgres tests SKIP visibly, not false-green; extracted KeyValue/Conversation/Memory/Trace/PromptVersion contracts run across all 3 backends (+ existing Score/Job/CuratedMemory routed through Postgres where session-scoped); divergent memory ordering/matching kept as backend-specific tests (→ R19). 752 pass / 49 skip (Docker down). CI pg run left as an infra follow-up (no CI config in repo yet).
  - `tests/…/PostgresStorageTests.cs` gates every test `if (!pg.Available) return` → **silently passes** (not
    skips) when Docker is absent, so Postgres parity is unverified in default CI. Postgres also re-implements
    `JobStoreContract`/`CuratedMemoryStoreContract` ad-hoc (subset of assertions), and 6 of 8 stores (Memory,
    KeyValue, Conversation, Score, Trace, PromptVersion) have NO shared cross-backend contract — each backend
    is tested with different cases, so InMemory (the test double) can green-light semantics the SQL stores
    don't reproduce. Fix: `Assert.Skip` (visible) + run the pg container in CI; run the existing contracts
    against Postgres; extract contracts for the other 6 stores and run all three backends through each.
- [x] **R6 · SQLite memory dedup is non-atomic (data-integrity divergence)** (sustainable) ✅ done 2026-07-20 — added `UNIQUE(task_key, scope, content)` (`ux_lyntai_memory_dedup`, replaces the non-unique prefix index) + `INSERT … ON CONFLICT DO UPDATE` (matches Postgres); FTS stays synced via the AFTER UPDATE trigger.
  - `SqliteMemoryStore.RememberAsync` (`:32-43`) is UPDATE-then-INSERT with no unique constraint → two
    concurrent Remembers create duplicate `(task,scope,content)` rows; `PostgresMemoryStore` (`:34-38`) uses
    an atomic `ON CONFLICT` on a unique index and can't. Fix: add `UNIQUE(task_key, scope, content)` (or
    hashed) to the SQLite schema + `INSERT … ON CONFLICT DO UPDATE`.

### Med
- [x] **R7 · README/CHANGELOG version drift (ships in every nupkg)** (sustainable) ✅ done 2026-07-20 — README Status refreshed to v0.28.5; agent-session moved from Unreleased → `## 0.28.5`; added `dev.mjs doctor` pack-guard (README Status version must == VersionPrefix). Original: — `README.md` `## Status`
  is stuck at ~v0.15 (omits governance trio, semantic memory, Postgres, DLQ/cron/cancel jobs, secret vault,
  agent session) while `VersionPrefix`=0.28.5; `CHANGELOG.md` has no entries for 0.28.2–0.28.5 and the
  agent-session work sits under "Unreleased". Reconcile on release; add a `dev.mjs` pack-doctor that fails if
  README status ≠ `VersionPrefix`.
- [x] **R8 · Verdict classifier is English/regex-biased + not extensible; `ContextWindowExceeded` unreachable
  on typed-exception paths** (generic) ✅ done 2026-07-20 — `FromException` scans the inner-exception chain (typed "too long" → ContextWindowExceeded); added `AddErrorTextMatcher` consumer seam (disposable, consulted before built-ins). — `LlmVerdictClassifier` text patterns are English-only and `static
  partial` (can't extend without editing core); `FromException` has no context-window arm (MEAI "prompt too
  long" typed exceptions → `Failed`, defeating the big-context fallback). Keep typed-status primary; add a
  consumer pattern seam (`Func<string,LlmVerdict?>` / injectable set) + known context-window exception types.
- [x] **R9 · `Refused` verdict overloaded for capability gaps** (generic) ✅ done 2026-07-20 — added `LlmVerdict.Unsupported` (→ `Surface`); OpenAI-compat + MEAI streaming providers emit it for stream-native-tool-calls instead of `Refused`, so telemetry distinguishes capability gap from policy refusal. — streaming native tool-calls map to
  `Error(Refused,…)` (`OpenAiCompatibleProvider.cs:206`, `ExtensionsAiProvider.cs:130`); `Refused` means
  "content policy, surface no-fallback", so telemetry/scorers can't tell a policy refusal from a transport
  limitation. Add a distinct verdict (e.g. `Unsupported`) mapped to `Surface`.
- [x] **R10 · Duplicated stream-json parsing + CLI reconciliation will drift** (sustainable) ✅ done 2026-07-20 — extracted `StreamJsonFields` (shared `GetLong` + `ConcatTextBlocks`) so `StreamJsonParser` + `StreamJsonAgentReader` can't drift on usage/text-block reads. (CompleteAsync-over-StreamAsync accumulation deferred — larger behavioral refactor, noted below.) —
  `StreamJsonParser.cs` and `StreamJsonAgentReader.cs` independently parse the same wire format (usage field
  names, text-block concat, `GetLong`); `ClaudeCliProvider.CompleteAsync` (`:80-102`) hand-rolls a buffered
  assistant-vs-result reconciliation that duplicates the streaming loop (`:152-169`). Extract shared
  field-extraction helpers; consider `CompleteAsync` accumulating over `StreamAsync` like `LocalProvider`.
- [x] **R11 · No public seam for a custom front-door decorator** (generic) ✅ done 2026-07-20 — `AddFrontDoorDecorator(order, factory)` public + built-in fold-order consts public, so an app folds its own decorator on the same ordered chain without pre-registering a whole ILlmClient. Original: — `FrontDoorDecorators` +
  `AddFrontDoorDecorator` are `internal`; an app's own cross-cutting concern (PII redaction, request logging)
  must pre-register a whole `ILlmClient`, which trips the governance guard. Expose a public
  `AddFrontDoorDecorator(order, factory)` / `ILlmClientDecorator` collection folding on the same ordered chain.
- [x] **R12 · `IDbConnectionFactory.Open()` is sync-only** (sustainable) ✅ done 2026-07-20 — added `OpenAsync(ct)` as a default-interface method (delegates to `Open()`, non-breaking) with genuine async overrides in the SQLite + Postgres factories. Original: — every store blocks a threadpool
  thread on connect (esp. Postgres network+pool). Add `Task<DbConnection> OpenAsync(ct)` to the interface NOW
  (default over `Open()`) — the one interface change that's expensive to make post-publish.
- [x] **R13 · Unwrapped DEK never zeroized** (sustainable — crypto) ✅ done 2026-07-20 — `EnvelopeSecretVault.BuildInner` now `CryptographicOperations.ZeroMemory`s the unwrapped DEK after the protector clones it (single choke point covering Generate/Recover/Initialize). Original: — `EnvelopeSecretVault` (`Create`/
  `UnwrapWithMachine`/`UnwrapWithRecoveryKey`) hands the DEK to `AesGcmSecretProtector` (which clones it) and
  never `ZeroMemory`s the original; the transient recovery KEK IS scrubbed but the longer-lived master DEK is
  not. Zero it after building the inner protector; consider making the protector disposable.
- [x] **R14 · ClaudeCli silently drops `LlmRequest.Tools`** (generic) ✅ done 2026-07-20 — logs a warning (count + "use the ClaudeCli.Mcp provisioner") when `req.Tools` is non-empty on the CLI path; documented on the `WarnIfRequestToolsIgnored` helper. Original: — `SupportsToolCalls=false` and
  `ClaudeArgs.Build` ignores `req.Tools` (tools reach the CLI only via the separate MCP provisioner). A caller
  putting tools on the request + routing to claude-cli gets them dropped with no diagnostic. Log a warning
  when `req.Tools` is non-empty on the CLI path; document the divergence.
- [x] **R15 · Process-global Dapper type-handler coupling between the two SQL factories** (generic) ✅ done 2026-07-20 — added a Docker-free parity test asserting the two `DateTimeOffsetHandler`s `Parse`/`SetValue` identically (handlers now `internal` + `InternalsVisibleTo`), catching drift immediately. (Registration is already replace-idempotent; guarding against a 3rd-party clobber isn't feasible with Dapper's API — drift protection is the real fix.) Original: — both
  `SqliteConnectionFactory` + `PostgresConnectionFactory` register a `DateTimeOffsetHandler` into Dapper's
  process-global registry in a static ctor ("whichever wins, both must be identical") — a third-party handler
  or a 4th backend can clobber it. Register idempotently/defensively; add a test asserting both are identical.
- [x] **R16 · Semantic `RememberAsync` not fail-open + no dimension check** (generic) ✅ done 2026-07-20 — documented the throw contract (RememberAsync surfaces write failures by design, asymmetric with fail-open Recall) + the per-backend model-swap behavior. Persistent dimension-stamp NOT added: contradicts the intentional graceful-degradation design (in-mem/sqlite rank mismatch last; pgvector rejects) + needs an IVectorStore change. Original: — asymmetric with the
  fail-open `RecallAsync`; a direct `ISemanticMemory` consumer gets an unguarded throw, and a mid-life model
  swap silently poisons a collection (no per-collection dimension stamp). Document the throw contract (or make
  symmetric) + stamp collection dimension at first write.
- [x] **R17 · `AggregateAsync`/`ExportAsync` live only on `IScoreStore`, bypassing the `IScoringService`
  seam** (generic) ✅ done 2026-07-20 — surfaced `GetAsync`/`AggregateAsync`/`ExportAsync` on `IScoringService` (delegates to the store, empty when none), so a dashboard injects the service not the store. Original: — a dashboard must inject the storage interface directly, breaking the "inject the service,
  not the store" layering (`ITraceService.GetAsync` wraps the store correctly). Surface read/aggregate/export
  on `IScoringService`.
- [x] **R18 · Env-override docs incomplete** (sustainable) ✅ done 2026-07-20 — added the `LYNTAI_JOBS_*` family (6) + `LYNTAI_DEFAULT_MODEL` alias (and the cache/budget/ratelimit/tool-loop vars) to the `LyntaiOptions` XML-doc + README env list. Original: — the whole `LYNTAI_JOBS_*` family (6 vars) +
  `LYNTAI_DEFAULT_MODEL` alias are read in `ApplyEnvOverrides` but absent from the `LyntaiOptions` XML-doc
  list + README. Add them; consider one canonical env-var reference table.
- [x] **R19 · Recall/list ordering diverges across backends beyond what contracts assert** (sustainable) ✅ done 2026-07-20 — Postgres curated `ListAsync` now `ORDER BY kind COLLATE "C"` (byte-ordinal, matches SQLite BINARY). Memory-recall bm25-vs-recency is an INHERENT divergence (FTS relevance vs recency — can't converge without dropping a feature): kept documented + asserted-divergent via R5's backend-specific tests. Original: —
  SQLite memory recall `ORDER BY bm25` vs Postgres/InMemory recency; curated `ListAsync`/aggregate `ORDER BY
  <text>` is SQLite BINARY vs Postgres DB-collation. Documented in prose only. Force `COLLATE "C"` (or order
  by recency everywhere) for parity, or assert the divergence in a contract test.
- [x] **R20 · Job scheduler double-fires under multi-instance** (sustainable) ✅ done 2026-07-20 — documented the "run ONE scheduler process" constraint on `IJobScheduler` (the read→enqueue→persist sequence isn't a CAS; the runner fleet can still be N). CAS on `IKeyValueStore` deferred (interface change across all backends). Original: — the runner supports N
  instances but two schedulers read the same due next-run from shared KV and both enqueue before either
  persists → every slot fires per instance. Document "one scheduler process", or make `SetNextAsync` a
  compare-and-swap KV write.

### Low (batch — verify + fix opportunistically)
- [x] **R21 · Nits** — DONE (2026-07-20). **Done in R21:** OutcomeScorer magic key → `ErrorKey` const;
  `LlmScorerBase` judge SYSTEM preamble now `virtual JudgeSystemPrompt`; `InMemoryJobStore.ListAsync` ordinal
  Id tiebreak (SQL parity); `CompleteJsonAsync` retry double-charge/no-cache DOCUMENTED; `ClaudeCommand.Tokenize`
  double-quotes-only DOCUMENTED; SQLite late-ADD-COLUMN `Down()` best-effort already documented (verified);
  `PackageProjectUrl`/`RepositoryUrl` set (verify repo hosted + add SourceLink at the 1.0 hosting step).
  **R21b (2026-07-20) — first cut:** ToolLoop native path now preserves assistant prose (`AssistantToolCalls`
  carries content); `ResponseCacheKey` reflection guard test (every `LlmRequest` field hashed or excluded);
  vault read-only policy asymmetry documented on `ISecretAccessPolicy`. **R21b — final cut (2026-07-20), all
  cleared:** `TraceStep.Sequence`/`OffsetMs` stamped by the recorder + persisted/ordered on all 3 backends;
  reverse-MEAI-bridge parity (`AsChatClient` maps tools/JSON-schema/attachments/tool-turns + surfaces reply
  tool calls); JsonNode-switch hoisted to public `Lyntai.Text.JsonArgs` (`ToNode`/`Serialize`/`Parse`), both
  adapters delegate; typed `IRefusalMatcher` seam (`AddRefusalMatcher`, fail-open) alongside `RefusalPattern`;
  `AddRateLimit` warns (still serves) when it resolves to no effective limit. Each TDD'd + committed; 837 green.
  - `ToolLoop` native path drops assistant prose that accompanies tool calls (`ToolLoop.cs:82-86`) — capture
    it into a step/thinking channel.
  - `OutcomeScorer.cs:22` bakes in a magic `Extra["error"]` key though `Extra` is "app-owned" — expose as a
    documented `const`/configurable key name.
  - `LlmScorerBase` judge SYSTEM prompt is hardcoded English + un-overridable (only `BuildJudgePrompt` = the
    user turn is virtual) — make the system preamble virtual.
  - `TraceStep` has no explicit `Sequence`/`OffsetMs` — timeline relies on store insertion order; add one.
  - `ClaudeArgs.Build` hardcodes `--disallowed-tools AskUserQuestion` with no override seam — move to config.
  - Reverse MEAI bridge `LyntaiChatClient.MapRequest` drops tools/JsonSchema/attachments (forward bridge maps
    them) — restore parity.
  - The boxed-primitive→`JsonNode` switch is copied in `ToolFunction.ToNode` + `ExtensionsAiProvider
    .SerializeArgs` (+2 more) — hoist to one internal helper.
  - `CompleteJsonAsync` structured retry double-charges budget/rate-limit + never cache-hits — document.
  - `InMemoryJobStore.ListAsync` tiebreak (`Guid` byte order) differs from SQL (`TEXT` ordinal) — apply the
    `Id.ToString()` ordinal tiebreak like `ClaimNextAsync` already does.
  - `ResponseCacheKey.For` manual `"lyntai-cache-v1"` prefix + hand-listed fields can rot when a new
    output-determining `LlmRequest` field is added un-hashed — add a reflection guard test.
  - Secret vault access policy gates READS only; `Set`/`Delete`/`ListNames` are ungated by contract — surface
    an optional write/enumerate policy hook, or document the asymmetry in the builder doc.
  - `LlmRequest.RefusalPattern` is a stringly-typed .NET regex on the canonical DTO (a consumer concern) —
    consider a typed `IRefusalMatcher` seam long-term.
  - `PackageProjectUrl`/`RepositoryUrl` point at a repo that may not be hosted yet → dead link + no SourceLink
    in shipped packages — verify before the next pack (ROADMAP lists hosting as the 1.0 blocker).
  - `AddRateLimit()` with all-default options throttles nothing (global `PermitsPerSecond=0`) — warn/no-op
    when it resolves to no effective limit, mirroring the pre-registered-client guard.
  - `ClaudeCommand.Tokenize` handles double quotes only (not escaped/single) — document the env-var limitation.
  - SQLite `Down()` for late ADD-COLUMN migrations is a no-op (pre-3.35 no DROP COLUMN) → down-migrations
    asymmetric with Postgres — document as best-effort, or recreate-table.

---

## Part 9 — Feature/module toggles: enable only what you use (2026-07-20)

Requirement (user, 2026-07-20): every **side feature** (scoring, conversation/message, memory, traces,
jobs, curated memory, prompt-versions, governance cache/budget, semantic memory) should be individually
**enable/disable-able by the app**. When a feature is DISABLED: its stores are NOT registered, and its
table(s) are **NOT migrated** — no unused `lyntai_*` tables land. Backed by (a) **app-startup verification**
(only the enabled features' schema exists / using a disabled feature fails fast with a clear message) and
(b) **per-module/feature migration logic** (migrate only the selected features' tables).

Consistent with the [[lyntai-owns-storage-extend-not-fork]] design: Lyntai still OWNS the tables it creates —
this only stops it creating tables for features the app opted out of. This is the opt-in store-selection +
selective-migration deferred from P3, now a first-class requirement (NOT the rejected "app owns its own
tables" direction).

- [x] **F1 · Feature toggle model + gated registration + selective migration — should-have** ✅ done 2026-07-20 — `[Flags] StorageFeature` (9 domains + All); `UseSqliteStorage`/`UsePostgresStorage(…, features)` gate registration per feature AND migrate only selected tables (tag-driven: each migration `[Tags(nameof(StorageFeature.X), AllTag)]`; All=one pass, subset=one pass/feature). Postgres monolith split into per-feature migrations for parity. Startup signal = a disabled store isn't registered (GetRequiredService throws). Verified both backends against a live Postgres container (819 pass). Details:
  - A `[Flags] enum LyntaiFeatures` (Scoring, Conversation, Memory, Traces, Jobs, CuratedMemory,
    PromptVersions, Governance, SemanticMemory, …; `All` default) — or per-feature options. Surface on the
    storage builders (e.g. `UseSqliteStorage(dbPath, features: LyntaiFeatures.Scoring | …)`) and/or a builder
    `EnableFeatures(...)`. Default = All (unchanged behavior).
  - **Registration:** register only the selected features' stores (conditional on the flag set).
  - **Migration:** per-feature migration gating — map each migration (or a FluentMigrator tag) to its feature;
    `MigrationRunnerService.MigrateUp` runs ONLY the selected features' migrations (both SQLite + Postgres).
    Update the "N migrations applied" invariant test to be feature-set-aware. **This is the risky part**
    (FluentMigrator tag/selective-run semantics + version-table interplay) — TDD it carefully.
  - **Startup verification:** a check (opt-in) that the enabled features' tables exist and that a disabled
    feature isn't silently used — fail fast with an actionable message rather than a raw SQL "no such table".
  - Files: `SqliteStorageBuilderExtensions` / `PostgresStorageBuilderExtensions`, both `MigrationRunnerService`s,
    the migrations (feature tagging), `ServiceCollectionExtensions` (conditional registration), a new
    `LyntaiFeatures` enum/options; contract test asserting a disabled feature lands no table + its store isn't
    registered, while an enabled one works.

---

## Part 10 — Actor/mailbox model for durable jobs (2026-07-20)

- [x] **A1 · Ordered single-owner-per-key durable jobs (actor mailboxes) — should-have** ✅ done 2026-07-20 —
  `JobSpec`/`IJobQueue.EnqueueAsync` gain an optional `partitionKey`. Jobs sharing a `(lane, partitionKey)`
  run **one-at-a-time in FIFO (enqueue) order** — an actor mailbox — while distinct keys run in parallel up
  to the lane's `MaxConcurrency`. Built on the EXISTING durable persistence + atomic claim + crash-resume
  (no new pump, no new state machine): the actor guarantee is a claim-time predicate, so a crashed owner's
  stale lease is *reclaimed* (keeps its slot) rather than skipped, and priority still orders *across*
  partitions but is ignored *within* one (FIFO wins). `partitionKey = null` = unchanged behavior.
  - **Claim guard (all 3 backends):** a candidate with a non-null key is claimable only if no OTHER live-leased
    `Running` sibling of the partition exists (one-at-a-time); a `Pending` candidate additionally requires NO
    `Running` sibling at all (a stale Running must be reclaimed first) AND must be the earliest available
    `Pending` of the partition (FIFO, `available_at` then ordinal id). SQLite/Postgres express this as
    `NOT EXISTS` subqueries inside the atomic claim (`FOR UPDATE SKIP LOCKED` on pg); InMemory mirrors it in
    `PartitionClaimable`. New index `ix_lyntai_job_partition(lane, partition_key)`.
  - **Schema:** `partition_key TEXT NULL` folded into the existing `M202607180001_Jobs` migration (pre-release
    merge policy — no new numbered migration).
  - Verified on InMemory, SQLite, and **live Postgres** (Docker): 4 new `JobStoreContract` tests run across all
    three backends — same-partition serialize+FIFO, different-partitions parallel, priority-across/FIFO-within,
    stale-Running reclaimed-before-later-Pending. 832 pass / 0 fail / 0 skip.

---

## Part 11 — Consumer-driven gaps: Gatherlight conversation-store adoption (2026-07-20)

> **✅ DONE (G1 · G2 · G3), 2026-07-22.** All three shipped generic + TDD'd. G3 added `CountThreadsAsync`
> + keyset-cursor `ListThreadsPageAsync` as default-interface methods (BYO impls keep working) with
> efficient overrides on all three backends; cross-backend contract tests pass (incl. live Postgres).

Surfaced adopting the generic conversation store (Part 7 · P2) in Gatherlight (its two-gate `chat_session`/
`chat_event` moved onto `IConversationStore`, accessed ONLY through the API — no raw SQL on `lyntai_*`).
Three small, generic library improvements the adopter had to work around app-side. Each is a general
capability, Gatherlight is just the example.

- [x] **G1 · `ClaudeToolCalls.FilePathOf` should also read `notebook_path` / `path`, not only `file_path`** (generic)
  - `src/Lyntai.Providers.ClaudeCli/ClaudeToolCalls.cs` — `FilePathOf` reads only `file_path`, so a
    `NotebookEdit` tool call (arg `notebook_path`) returns null. An app building an edit-tracker / commit-set
    from the agent stream (Gatherlight does) then silently misses `NotebookEdit` (and any `path`-arg tool)
    writes. Gatherlight had to re-implement the parse app-side (`file_path` → `notebook_path` → `path`).
    Fix: check `file_path`, then `notebook_path`, then `path` (the three write-tool path args). Test:
    a `NotebookEdit` call's `notebook_path` is returned; `file_path` still wins when both present.

- [x] **G2 · Agent-session `FinalText` should fall back to accumulated assistant text when the terminal
    `result` is empty** (generic — robustness)
  - `src/Lyntai.Providers.ClaudeCli/StreamJsonAgentReader.cs` + the `RunAsync` fold (`AgentSessionResult
    .FinalText`) populate final text ONLY from the terminal `result` message's `result` string; assistant
    text blocks are intentionally skipped (streamed as deltas). If a run ends with assistant text but an
    empty/absent `result.result` (truncation, an older CLI, a provider variant), `FinalText` is `""` — where
    the pre-Lyntai native runner fell back to the last assistant text block. Consumers that treat empty as
    failure (one-shot extract, kb-merge, validate, a dry-plan preview) then spuriously fail. Fix: have the
    reader/fold retain the last assistant text (or the accumulated `TextDelta`s) and use it as the fallback
    when the terminal result text is empty. Test: a stubbed stream with assistant text + an empty `result`
    string → `AgentSessionResult.FinalText` is the assistant text, not `""`.

- [x] **G3 · `IConversationStore` count + filtered/paged list (avoid list-all-then-filter)** (generic — nice-to-have)
  - `src/Lyntai.Core/Storage/IConversationStore.cs` exposes only `ListThreadsAsync(limit)` — no count and no
    server-side filter. An adopter that needs "how many conversations" or "the unscored terminal ones"
    resorts to `ListThreadsAsync(100_000)` + in-memory filter (Gatherlight's eval-console stats + score
    backlog do exactly this). Fine at family scale, ugly + O(n) at any real size. Consider a
    `CountThreadsAsync()` and/or a filtered/paged list (by a metadata predicate or a created-at cursor).
    Keep the simple `ListThreadsAsync` as the default. Test: count matches inserted rows; a paged cursor
    walks all threads without loading them at once.

---

## Part 12 — Consumer-driven gap: curated memory with task + scope (Sonora adoption, 2026-07-22)

> **✅ DONE (CM1), 2026-07-22.** Shipped generic + TDD'd across all three backends. `CuratedMemory` gained
> nullable `Task`/`Scope`; `ICuratedMemoryStore` gained `ForCompositionAsync(task, scopes, enabledOnly)` +
> a `task` filter on `ListAsync`/`AddAsync`; `CuratedMemorySections` gained the shared `AppliesTo` predicate
> + a `(task, scopes)` `Compose` filter. Because the `lyntai_curated_memory` table shipped RELEASED (v0.28),
> the columns are a NEW numbered migration (`202607220001`, `ADD COLUMN`), not a fold — no backfill (null =
> global). Verified on InMemory, SQLite, and live Postgres.

Sonora is adopting Lyntai as its LLM + cortex substrate (retiring its own `Modules/Llm` + `Modules/Ai`). Its
`ai_memory` is a HUMAN-CURATED store — an editor adds entries carrying a `task` ("translation" / "metadata"),
an optional `scope` ("lang:zh"), a `kind` (grouping), an `enabled` toggle, and a `source`; a composer folds the
*enabled* entries for a given (task, scope-set) into that call's system prompt as a kind-grouped block (bounded,
fail-open). This is a legitimate, generic pattern — curated context that is PER-CONSUMER and PER-VARIANT, not
only global-per-kind — but Lyntai's `ICuratedMemoryStore` today is `kind` + `enabled` ONLY (no task/scope), so a
clean migration would lose the task/scope filtering. (`IMemoryStore` / `ISemanticMemory` have task/scope but are
auto-learned — no curation, no editor.)

- [x] **CM1 · Optional `task` + `scope` on curated memory** (generic — an adopter with per-consumer curated context)
  - Files: `src/Lyntai.Core/Storage/ICuratedMemoryStore.cs` (+ the `CuratedMemory` record), the three backends
    (`Lyntai.Storage.{Sqlite,Postgres,InMemory}` — the `lyntai_curated_memory` table + a migration adding
    nullable `task` + `scope`), and the compose helper `Cortex/CuratedMemorySections.cs`.
  - Spec: `AddAsync(kind, content, source?, enabled?, task?, scope?)`; `ListAsync(kind?, enabledOnly?, task?)`;
    and a filtered read `ForCompositionAsync(task, IEnumerable<string> scopes, enabledOnly = true)` returning
    enabled entries whose `task` matches AND (`scope` is null/empty OR `scope` ∈ scopes) — task-less/scope-less
    rows apply everywhere (backward-compatible: existing rows have null task/scope, so they behave exactly as
    today). `CuratedMemorySections.Compose` gains an optional (task, scopes) filter.
  - Test: an entry (task="translation", scope="lang:zh") is returned for ("translation", ["lang:zh"]) and for
    ("translation", []) via the null-scope rule, but NOT for ("metadata", …) nor ("translation", ["lang:ja"]);
    a null-task/null-scope entry is returned for every (task, scopes); the existing kind+enabled behavior is
    unchanged when task/scope are omitted.
  - Migration: `YYYYMMDDNNNN_CuratedMemoryTaskScope` adds nullable `task` + `scope` (tagged
    `StorageFeature.CuratedMemory`); no backfill (null = global — the historical behavior).

Once CM1 ships (a release Sonora bumps to), Sonora migrates `ai_memory` → `lyntai_curated_memory`
(task/scope/kind/enabled/source map 1:1) and retires `Modules/Ai`. Until then Sonora keeps its curated store as
is and wires only Lyntai's LEARNED memory (the auto-improvement layer) alongside it.

---

## Part 13 — Assistant coding-system: no-global-memory + sibling coding pattern (2026-07-22)

✅ done 2026-07-22 — Adopted a sibling project's in-repo coding-system discipline: project
facts live in the repo, not in Claude Code's global auto-memory. Added `.claude/rules/no-global-memory.md`
(+ `minimise-bash-prompts.md`, `no-tmp-for-repo-files.md`, `TEMPLATE.md`) and the `RULES_INDEX.md` loading
model / evolve-the-system / invariants; migrated the 9 `lyntai-*` global memories into `docs/DECISIONS.md`
(D6/D7 already covered two, D10–D15 added the rest).

- **M1 · Clear global auto-memory of project facts** — ✅ done 2026-07-22. Deleted the 9 migrated
  `lyntai-*` files from this project's global auto-memory dir and reset its `MEMORY.md` to a redirect note
  (global memory now holds only user-specific prefs — currently none). Information-lossless — all content
  is preserved in `docs/DECISIONS.md`. First live use of the `archive-task` flow.

---

## Part 14 — App-configurable memory retention policy (multi-strategy) (2026-07-22)

✅ done 2026-07-22 — `IMemoryStore` size management is now an app-configurable `MemoryRetentionPolicy`
(DECISIONS D16), mirroring the configurable `RoutingPolicy`. Requirement (user): "multi-way / configurable
so the app has control", production-grade, drawing on existing agent-memory systems (LangChain
buffer-window/token-buffer, MemGPT eviction).

- **R1 · `MemoryRetentionPolicy`** — ✅ done. Composable knobs (`LyntaiOptions.MemoryRetention` /
  `ConfigureMemory(...)` / `LYNTAI_MEMORY_*`): count cap + `MemoryEvictionMode` (FIFO/LRU), default TTL,
  per-scope size (character) budget; presets `CountCap`/`TimeToLive`/`SizeBudget`/`Composite`/`Manual`.
  Eviction is a single pure `MemoryEviction.Survivors` helper shared by all three backends (InMemory /
  SQLite / Postgres) so they can't diverge; LRU adds `last_accessed_at` (migration `202607220002`,
  refreshed best-effort on recall; SQLite FTS update-trigger scoped to `content` to avoid churn). Default
  reproduces the historical 500-entry FIFO cap (`MemoryCapPerScope` proxies it). Cross-backend contract
  tests for LRU / default-TTL / size-budget / manual; verified on InMemory, SQLite, and live Postgres.
  Landed in 3 commits (Core+InMemory, SQLite, Postgres+docs).

---

## Part 15 — Opt-in memory-prune cron job (2026-07-22)

✅ done 2026-07-22 — Follow-on to Part 14 (DECISIONS D16). On-write eviction only bounds scopes you keep
writing to; a cold `(taskKey, scope)` accumulates expired rows. `builder.AddMemoryPruneJob(cron,
olderThan?, taskKey?)` registers an internal `MemoryPruneJobHandler` (`IJobHandler` over
`IMemoryStore.PruneAsync`, idempotent) + a cron `JobSchedule` on the EXISTING durable-jobs + cron
machinery — Lyntai owns the prune work, the app owns the pump (no self-run timer; consistent with the "no
host" boundary D9/D14). Handler + payload record are internal (only `AddMemoryPruneJob` is public surface).
TDD'd (handler parses payload / reaps; registration wires one handler + N schedules; bad cron throws).
Decided WITH the user: needed because this is a generic library used across many apps (unbounded cold-scope
growth is the real case).

---

## Part 16 — Code-review follow-ups: close every deferred finding (2026-07-22)

✅ done 2026-07-22 — The workflow code review of Part 14/15 surfaced 10 findings; 4 confirmed bugs + the
dedup landed in `f6fc301`. Per the user's "complete them all", the deferred/refuted rest were closed too:

- **R1 · Atomic count-cap eviction (SQL)** — ✅ done. Restored a single-statement atomic
  `DELETE … WHERE id NOT IN (SELECT … ORDER BY <recency> DESC LIMIT @cap)` for the count-cap case (FIFO via
  `created_at`, LRU via `COALESCE(last_accessed_at, created_at)`) in both SQL stores; `MemoryEviction.ApplyAsync`
  now runs ONLY for the size-budget case (needs the windowed compute). Race-free + O(1)-ish for the common
  path, fixing review #5/#9. Cap/Lru/Lru_bare contract tests stay green (incl. live Postgres).
- **R2 · InMemory `Seq` = MAX+1** — ✅ done. `InMemoryConversationStore.AppendMessageAsync` now uses
  `MAX(seq)+1` (mirrors the SQL `COALESCE(MAX(seq),0)+1`), so a future per-message deletion can't reuse a seq.
- **R3 · BYO paging caveat** — ✅ done. The `CountThreadsAsync`/`ListThreadsPageAsync` XML docs now WARN that
  the default materializes the whole table (a naive fallback) and a BYO store must override for cheapness.
- **R4 · `MemoryCapPerScope = 0`** — ✅ resolved-as-documented. 0 = uncapped (≤0 = no cap is the policy's
  stated semantics); a fringe change from the old "cap 0 = store nothing", noted in DECISIONS D16.
- **R5 · Refuted findings** — ✅ recorded, no change. StreamJsonAgentReader text-buffering (needed for the
  empty-terminal fallback) and MemoryPruneRequest-vs-JsonArgs (JsonArgs is shaped for tool args) were refuted
  by the review's own verifier.

---

## Part 17 — CLI runner: `StreamLinesAsync` stdin/stdout pipe deadlock on large prompts (2026-07-23)

- [ ] **`id: procrunner-streamlines-stdin-deadlock`** — `src/Lyntai.Core/Processes/ProcessRunner.cs` (`StreamLinesAsync`).
  **Bug.** `StreamLinesAsync` `await`s the FULL stdin write (`WriteStdinAsync`) and closes stdin **before** the
  stdout read loop begins. On a prompt larger than the OS pipe buffer this **deadlocks**: the parent blocks filling
  the stdin pipe while the child — already emitting stdout (`--output-format stream-json` startup, MCP
  `initialize`/`tools/list` events) — blocks filling the stdout pipe the parent hasn't begun draining. The child's
  turn never starts (0 tool calls, no output); the caller's timeout is what finally kills it. `RunAsync` doesn't
  hit this because it starts the stdout/stderr `ReadToEndAsync` **first**, then writes stdin — `StreamLinesAsync`
  must not serialize write-then-read.
  **Fix.** Fire the stdin write **concurrently** with the read loop (don't `await` it before the first read);
  observe its outcome after the loop (a broken pipe on early child exit is already swallowed in `WriteStdinAsync`).
  **TDD (must FAIL before the fix).** Stream a prompt bigger than the pipe buffer (≥ ~128 KB) through a child that
  interleaves reading stdin with writing to stdout — it must complete and yield the child's lines; today it hangs
  to the timeout. Keep a small-prompt case green (regression guard).
  **Impact.** Every large self-driving/agentic CLI turn (big system prompt or user prompt) stalls to the caller's
  timeout — the model never runs — so any consumer's agent loop silently times out / falls back to a non-LLM path.
  (Found from Sonora: its site-study synth hung on every large prompt → deterministic fallback → wrong result on
  pages the deterministic path can't handle, e.g. forums. The agentic `claude` call itself completes fine when
  spawned directly; only this write-then-read path hangs.)
  **NB.** A candidate minimal fix is drafted in the working tree (write stdin concurrently in `StreamLinesAsync`);
  formalize it test-first, then bump + republish `Lyntai.Core` so consumers pick it up (Sonora pins `0.29.1`).

✅ done 2026-07-23 — `StreamLinesAsync` now fires the stdin write **concurrently** with the stdout read loop
(was: `await` the full write + close stdin BEFORE the first read) and observes the write's outcome after the loop
(a broken pipe / cancel is already swallowed in `WriteStdinAsync`; the real signal stays exit-code / stderr /
`timedOut`). The old write-then-read serialization deadlocked on a prompt > the OS pipe buffer against a child
that emits stdout before draining stdin (parent filling the stdin pipe ⟂ child filling the un-drained stdout
pipe) and hung to the caller's timeout; now stdout drains as stdin is fed, matching `RunAsync`'s read-first
ordering. TDD: `Stream_lines_does_not_deadlock_on_large_stdin_with_interleaved_stdout` (512 KB stdin + a node
child that writes ~256 KB stdout BEFORE reading stdin) FAILED with `ProcessTimeoutException` on the pre-fix code
and passes now; `Stream_lines_passes_small_stdin_through` guards the small-prompt path. Windows-deterministic
(node's pipe writes are synchronous, so the child's up-front stdout burst blocks its event loop before it reads
stdin). No public-surface change (internal behavior only — `ApiSurface` unchanged); `dev.mjs verify` green
(898 tests · e2e 3/3 · leak scan). Republish is the manual **Release** workflow (patch bump → `0.29.2`) so
consumers (Sonora, pinned `0.29.1`) pick it up. Files: `src/Lyntai.Core/Processes/ProcessRunner.cs`,
`tests/Lyntai.Tests/Core/ProcessRunnerTests.cs`.

---

## Part 18 — CLI completion: inactivity-based dead detection (buffered path) (2026-07-23)

- [ ] **`id: cli-completion-inactivity-dead-detection`** — `src/Lyntai.Core/Processes/ProcessRunner.cs` (`RunAsync`) + `src/Lyntai.Providers.ClaudeCli/ClaudeCliProvider.cs` (buffered `CompleteAsync`).
  **Problem.** The buffered completion path (`ProcessRunner.RunAsync`, used by `ClaudeCliProvider.CompleteAsync`) applies a **wall-clock** timeout that "covers the WHOLE call" + reads with `ReadToEndAsync`. So a model turn that is SLOW-but-ALIVE (a big prompt, a long tool loop) is killed exactly like a truly DEAD/stalled one — the caller can't tell "working" from "hung," and raising the wall-clock just delays the false kill. `StreamLinesAsync` already does the right thing (an INACTIVITY window re-armed on each stdout read, with the clock stopped while the consumer works), but the buffered path can't — it awaits the entire stream at once.
  **Fix.** Give the buffered path INACTIVITY-based DEAD DETECTION: read stdout in chunks and RE-ARM the timeout on each chunk received, so the child is killed only after N seconds of TRUE SILENCE (no output) — a streaming / tool-looping child keeps resetting the clock. Options: (a) add an inactivity mode to `RunAsync` (chunked read + per-chunk re-arm), or (b) implement `CompleteAsync` on top of `StreamLinesAsync` (accumulate lines; its inactivity window already applies). Keep an absolute MAX cap as a backstop, and treat "dead" (inactivity) distinctly from "exceeded max."
  **TDD (must FAIL before the fix).** A buffered completion whose child stays SILENT for the inactivity window is killed with a Timeout; a child that keeps emitting output well past the old wall-clock (but never stalls) COMPLETES — today the wall-clock kills it.
  **Impact.** Consumers (Sonora's site-study synth) intermittently lose a working-but-slow LLM turn to the wall-clock and fall back to a worse non-LLM path (e.g. a forum def the deterministic path gets wrong). Dead detection lets a slow turn finish while cutting a genuinely stuck one fast.

✅ done 2026-07-23 — Took option (a): `ProcessRunner.RunAsync` now measures child **inactivity**, not wall
clock — it reads stdout in chunks and re-arms `timeout` on each read (stdin written concurrently, its clock
re-armed too), so a slow-but-ALIVE turn (big prompt / long tool loop) runs to completion while a child gone
SILENT for the window is killed — the same discipline `StreamLinesAsync` already used. A new absolute
`maxDuration` backstop bounds a child that never stalls but never finishes, and `ProcessResult.TimeoutKind`
(`Inactivity` vs `MaxDuration`) reports which clock fired. `ClaudeCliProvider.CompleteAsync` passes the
resolved timeout as the inactivity window and `MaxProviderTimeout` (raised to the window if a consumer budget
exceeds the ceiling, never below it) as the backstop, surfacing the distinction in the timeout `Detail`.
**Seam note:** `IProcessRunner.RunAsync` gained an optional `maxDuration` parameter — callers stay
source-compatible; a BYO `IProcessRunner` must add it to its override (the three test fakes did). TDD:
`Run_async_a_streaming_child_past_the_inactivity_window_completes` FAILED on the pre-fix wall-clock code
(killed the healthy child at ~4s → `ExitCode -1`) and passes now;
`Run_async_kills_a_child_gone_silent_and_reports_inactivity`,
`Run_async_absolute_max_caps_a_chatty_child_that_never_stalls`, and
`ClaudeCli_passes_the_absolute_max_duration_backstop_to_the_runner` cover dead detection, the backstop, and
the provider wiring. Public surface grew (`ApiSurface` Lyntai.Core baseline updated: the `maxDuration` param,
`ProcessResult.TimeoutKind`, the `ProcessTimeoutKind` enum). `dev.mjs verify` green (902 tests · e2e 3/3 ·
leak scan). Files: `src/Lyntai.Core/Processes/ProcessRunner.cs`, `src/Lyntai.Core/Processes/IProcessRunner.cs`,
`src/Lyntai.Providers.ClaudeCli/ClaudeCliProvider.cs`, `tests/Lyntai.Tests/Core/ProcessRunnerTests.cs`,
`tests/Lyntai.Tests/Providers/PerRequestTimeoutTests.cs`, the two provider/agent-session test fakes,
`.claude/knowledge/{llm-and-router,pitfalls}.md`, `CHANGELOG.md`. **Shipped in 0.29.3** (fix commit
`f33669d`, release `5414cd7` — patch bump so consumers, e.g. Sonora, pick it up).

---

## Part 19 — agent-manager desktop adoption + curated-memory papercuts (CM1/CM2/CLI1/TL1/TL2/PR1)

Consumer-driven generic gaps: a WinForms desktop AI-manager adopting `ClaudeAgentSession` + `IToolLoop`
(2026-07-26) and the Sonora source-study curated-memory ergonomics (2026-07-24). All generalized to
app-agnostic library surface (see `.claude/knowledge/generic-library.md`, created in this pass).
**Shipped in 0.30.0.** (Archived initially under a duplicate "Part 13" heading — renumbered.)

### Curated-memory ergonomics

- [x] **CM1 — dedup-on-add for `ICuratedMemoryStore.AddAsync`.** `IMemoryStore.RememberAsync` dedups identical
  content per (task, scope); `AddAsync` did not. A `dedup: bool = false` flag lets a consumer write a
  `confirmed` note idempotently without a pre-`ListAsync`+compare.

  ✅ done 2026-07-26 — Added `dedup` (default false) to `ICuratedMemoryStore.AddAsync`. When true the add is
  idempotent on the (kind, content, task, scope) identity: returns the existing row's id and writes no second
  row; default keeps the deliberate-catalog always-insert. Across InMemory (LINQ match), SQLite (`… task IS
  @task AND scope IS @scope` null-safe compare), Postgres (`IS NOT DISTINCT FROM`). TDD:
  `CuratedMemoryStoreContract.Dedup_add_is_idempotent` (run by InMemory/SQLite/Postgres). Files:
  `src/Lyntai.Core/Storage/ICuratedMemoryStore.cs`, the three `*CuratedMemoryStore.cs`, the contract +
  wirings, `ApiSurface` baselines (Core + 3 storage), `CHANGELOG.md`.

- [x] **CM2 — `scope` filter on `ICuratedMemoryStore.ListAsync`.** The optimize/admin pass wants "all notes
  (incl. disabled) for ONE scope."

  ✅ done 2026-07-26 — Added a strict-equality `scope` filter to `ListAsync` (before `limit`; null = no filter,
  unchanged). Across all three backends. TDD: `CuratedMemoryStoreContract.List_filters_by_scope`. Files: same
  set as CM1.

### Agent-session & tool-loop ergonomics

- [x] **CLI1 — headless "skip all permissions" for `ClaudeAgentSession`.** `--permission-mode acceptEdits`
  auto-accepts edits only; Read/Grep/Bash and every `mcp__*` tool still prompt, hanging a headless `-p` run
  with no responder. **This was the one blocking that consumer.**

  ✅ done 2026-07-26 — Added an opt-in `SkipAllPermissions` bool to `ClaudeAgentOptions`. When set,
  `ClaudeAgentArgs.Build` emits `--dangerously-skip-permissions` and suppresses the conflicting
  `--permission-mode` / `--allowedTools` (the CLI rejects combining them); the always-denied flow tools and the
  caller's `DisallowedTools` (+ ReadOnly write denial) still stand. Documented opt-in/dangerous. TDD: 6
  arg-build matrix tests in `ClaudeAgentSessionTests`. Files:
  `src/Lyntai.Providers.ClaudeCli/{ClaudeAgentOptions,ClaudeAgentArgs}.cs`, tests, `ApiSurface`
  Lyntai.Providers.ClaudeCli baseline, `CHANGELOG.md`.

- [x] **TL1 — surface token usage on `ToolLoopResult`.** A consumer wanting per-run token accounting had to
  wrap `ILlmClient` in its own front-door decorator.

  ✅ done 2026-07-26 — Added nullable `ToolLoopResult.Usage` (init property, like `LlmReply.ToolCalls`). The
  loop aggregates every front-door reply's `LlmUsage` (summed input/output/cache-read; cost summed when any
  reported one, else null; null overall when none did). TDD: 4 tests in `ToolLoopTests` (prompt/native/no-tools
  aggregation + null-when-none). Files: `src/Lyntai.Core/Agents/{ToolModels,ToolLoop}.cs`, tests, `ApiSurface`
  Core baseline, `CHANGELOG.md`.

- [x] **TL2 — live progress from `IToolLoop`.** `RunAsync` returned the answer whole; an interactive UI
  couldn't show tool calls as they happened. Chosen shape: a full `StreamAsync` event overload mirroring
  `IAgentSession`.

  ✅ done 2026-07-26 — Added `IToolLoop.StreamAsync(req, maxIterations?, ct)` yielding `AgentStreamEvent`s
  (ToolCall/ToolResult per round-trip, assistant TextDelta(s), a UsageFinal when usage was reported, one
  terminal SessionEnded; no SessionStarted — a Lyntai-driven loop has no external session id). Refactored the
  native/prompt/no-tools loops into one shared event-producing `RunCoreAsync`; `RunAsync` now folds it (steps +
  usage as side outputs) so both doors stay in lockstep. `StreamAsync` is a DEFAULT interface method (a BYO
  `IToolLoop` that only implements `RunAsync` gets a functional post-hoc stream for free; `ToolLoop` overrides
  it live) — additive, not a break. Events are TURN-granular (the native path needs the whole reply to read its
  structured tool calls). **Gotcha fixed:** an async iterator resets `Activity.Current` (AsyncLocal) across
  every `yield return`, which broke tool-span nesting under the loop span — re-asserted the loop activity
  before each child-span-creating await (`Enter()` helper). TDD: 6 StreamAsync tests + the default-method test
  in `ToolLoopTests`, and the existing 22 RunAsync + `AgentDiagnosticsTests` nesting tests validate the fold.
  Files: `src/Lyntai.Core/Agents/{IToolLoop,ToolLoop}.cs`, tests, `ApiSurface` Core baseline, `CHANGELOG.md`.

- [x] **PR1 — default `IProcessRunner`: Windows launcher-shim resolution + forced UTF-8.**

  ✅ done 2026-07-26 — On inspection the default `ProcessRunner` ALREADY forced BOM-less UTF-8 on all three
  streams and resolved `.cmd`/`.exe` shims; the real remaining gap was `.ps1` launcher shims (can't be exec'd
  directly by CreateProcess). Added `.ps1` to the resolution preference (`.cmd` → `.exe` → `.ps1`) and hosting
  via `powershell -NoProfile -ExecutionPolicy Bypass -File` (new private `ResolveLauncher`). TDD:
  `ProcessRunnerTests.Runs_a_powershell_ps1_launcher_shim` (Windows-gated); the UTF-8-no-BOM round-trip stays
  locked by the existing CJK stdin tests. Files: `src/Lyntai.Core/Processes/ProcessRunner.cs`,
  `tests/Lyntai.Tests/Core/ProcessRunnerTests.cs`, `CHANGELOG.md`.

---

## Part 20 — Whole-library foundation-hardening pass (2026-07-26)

✅ done 2026-07-26 — Not a `TASKS.md` task but the archive records it as the third consolidation review:
six parallel subsystem reviews (~80 findings) → verified per finding → three correctness clusters
(router/rate-limiter/cache · infra/DI/scheduler/vault · provider bridges/guards/orchestrator/prompts), a
storage refactor (async `OpenAsync` sweep, `JobStoreSql`+`JobRow` shared state machine,
`LazyMigratingConnectionFactory`, `VectorMath`, `TagPasses`, cross-backend divergence fixes), an
LLM/agents dedup cluster (`DelegatingLlmClient` base, router `LiveCandidates`, `GuardRail` shared gate
loop, API-honesty renames), and test-suite hygiene (shared fakes, per-db pool clears, coverage pins).
Then a **round-2 adversarial review of the pass's own diff** (48-agent workflow) confirmed 5 regressions
round 1 introduced — composed-prompt gating, placeholder key-grammar narrowing, stdin drain-liveness,
thrown-Refused terminality, inert streamed-usage fix — all fixed with regression tests, plus
case-insensitive consumer identity end-to-end and the shared claim predicate. Deferred findings → the
`TASKS.md` backlog (with reasons); rejected findings → `docs/DECISIONS.md` D18; the JSON-approach
rationale → D17. Nine commits, `verify` green throughout (954 tests · e2e 3/3 · leak scan).
**Shipped in 0.30.0.**

---

## Part 21 — 1.0-prep: infrastructure + final API sign-off (2026-07-27; unreleased)

Everything technically gating 1.0, implemented now but deliberately NOT released — 1.0 itself is
adoption-gated (more applications must adopt Lyntai first; see ROADMAP). Two `TASKS.md` items closed as
part of it:

- [x] **P3 — Azure OpenAI preset endpoint shape.** `AddAzureOpenAiProvider`'s documented endpoint example
  likely 404s (`/v1/...` vs `/openai/v1/...`) and Azure key auth conventionally uses the `api-key` header.
  Needs verification against a real Azure resource before changing `Endpoint()` — don't fix blind.
  ✅ done 2026-07-26 — Outcome: verified against Azure's current `/openai/v1` (v1 GA API) docs;
  `ProviderDetect.AzureOpenAi` flavor (detects `*.openai.azure.com`), bare-resource endpoint completion
  to `/openai/v1/chat/completions`, `api-key` header alongside Bearer. Commit `ac519fa`.
- [x] **L8 — async `IUsageTracker`.** The sync `Total()` is a pre-call read on EVERY budgeted request — a
  blocking network round-trip with the Postgres tracker. Breaking interface change (3 impls + baseline);
  do as its own task.
  ✅ done 2026-07-26 — Outcome: `RecordAsync`/`TotalAsync`/`ResetAsync` (`ValueTask`) across all 3
  backends + `BudgetedLlmClient`; per-consumer totals aggregate case-insensitively (COLLATE NOCASE /
  `lower()`). Commit `ac519fa`.

✅ done 2026-07-27 — Also in the part (not `TASKS.md` items): push/PR CI running the full `verify` gate
[later removed the same day at the owner's direction — process is fully manual, see `DECISIONS.md` D20];
SourceLink/deterministic CI builds; the design-contract reconciliation amendment; and the **final API
sign-off pass** — an 18-finding audit of the whole public surface, closed by a batch of pre-1.0 breaking
renames/reshapes (`UseDefaultCandidates`, `SchemaMigration` enum, required `AddSecretVault` key +
`AddPlaintextSecretVault`, `IResponseCache.GetAsync`/`RemoveAsync`, `IProcessRunner`
inactivity/maxDuration reshape, `TaskKey`/`ContextSize`/`*Tokens`, wire-format types internal) plus
additive read paths (`IKeyValueStore.ListKeysAsync`, `IJobQueue.GetAsync`/`ListAsync`,
`AddScorer(factory)`/`AddEmbeddings<T>`), an ApiSurface renderer upgrade
(sealed/abstract/static/required) with all 11 baselines regenerated deliberately, and a real
cross-backend fix caught by the new contract tests (SQLite `ListKeysAsync` used case-insensitive `LIKE`).
Decisions → `docs/DECISIONS.md` D19; user-visible detail → CHANGELOG Unreleased.

---

## Part 22 — Curated-memory as a searchable, metadata-carrying catalog (CMEM3–CMEM6)

Requested by the desktop AI-manager integration (2026-07-26): its agent memory is a single titled,
source-tagged, keyword-searchable, individually-CRUD-able note catalog written by both a human (owner)
and the agent; two generic gaps stopped `ICuratedMemoryStore` from being that catalog.

- [x] **CMEM3 — optional `Title` on `CuratedMemory`.** A curated fact commonly has a short label + a longer
  body (a glossary term → definition, a persona trait → detail, a saved note → title). Add an optional
  `Title` to `CuratedMemory` and to `AddAsync`/`UpdateAsync` (COALESCE-update semantics like the existing
  fields), and have `CuratedMemorySections.Compose` render it as the entry's lead (e.g. `- **{Title}**: {Content}`,
  falling back to Content-only when null). Nullable `title` column migration on SQLite + Postgres
  (`ADD COLUMN`, no backfill — null = untitled, existing rows unchanged), mirrored in InMemory. Additive; no
  breaking change. (The desktop's Memory view shows a bold title + body per entry — today packed into
  Content because there's no Title.)
  ✅ done 2026-07-27 — Outcome: `CuratedMemory.Title` (trailing optional record param), `AddAsync`
  `title` param placed AFTER `dedup` so no pre-existing positional call site silently re-binds
  (source/title are both `string?`), `UpdateAsync` COALESCE title ("" clears, null unchanged), title
  stays OUT of the dedup identity (display metadata, matched row untouched), Compose renders
  `- **{Title}**: {Content}` (null/empty = content-only), migration `M202607270001_CuratedMemoryTitle`
  ×2 backends (`ADD COLUMN title TEXT NULL`, CuratedMemory-tagged), contract test
  `Title_round_trips_updates_and_clears` wired to all 3 backends + a Compose rendering test; 4 ApiSurface
  baselines regenerated; migration-count pins bumped to 14. `verify` green (976 tests).

- [x] **CMEM4 — keyword `SearchAsync` on `ICuratedMemoryStore`.** The curated store is List-by-kind only
  (`ListAsync`/`ForCompositionAsync`); there is no relevance/keyword lookup, so a consumer building a
  searchable curated catalog — or letting an agent `recall` from the curated set — must load-all-and-filter
  in-app. Add `SearchAsync(query, kind?, task?, scope?, enabledOnly?, limit?)` reusing the SAME per-backend
  index machinery `IMemoryStore` already has (SQLite FTS5-trigram + bm25, Postgres pg_trgm, InMemory
  substring/recency) so the semantics + backend-divergence notes match the lexical store's documented
  guarantee. Keeps the catalog "small and deliberate" — search is an added read path, not capping/TTL.
  Together with CMEM3 this makes `ICuratedMemoryStore` a full titled+searchable catalog an app can adopt
  wholesale (owner rows `source="owner"`, agent rows `source="agent"` via the existing `dedup` add).
  ✅ done 2026-07-27 — Outcome: `SearchAsync(query, kind?, taskKey?, scope?, enabledOnly?, limit?)` matching
  CONTENT + TITLE with the ListAsync-family strict filters (enabledOnly default false; whitespace query →
  empty — ListAsync is the enumeration path). Backends mirror `IMemoryStore.RecallAsync` exactly, incl.
  the documented divergence + fail-open: SQLite any-token FTS5-trigram bm25 over a new
  `lyntai_curated_fts` (content+title) external-content table with 3 sync triggers + backfill
  (`M202607270002`), LIKE fallback; Postgres pg_trgm GIN indexes on content/title + ILIKE, recency-ranked
  (`M202607270002`, extension create repeated so CuratedMemory-only builds don't depend on Memory);
  InMemory OrdinalIgnoreCase substring, recency-ranked. SQL curated stores gained the family's optional
  `ILogger` ctor param (fail-open logging), wired in DI. Contract tests
  `Search_matches_content_and_title_with_filters` + `Search_recalls_cjk_substrings` (the portable
  ≥3-char-substring guarantee + 2-char CJK via the substring fallbacks) wired to all 3 backends;
  4 ApiSurface baselines regenerated; migration pins bumped to 15. `verify` green (981 tests).

A later follow-up from the same adopter (2026-07-27) closed a third gap — this time in the catalog's EDIT
path rather than its read path:

- [x] **CMEM5 — `kind` on `ICuratedMemoryStore.UpdateAsync`.** `UpdateAsync(id, content?, enabled?, source?,
  title?)` can change everything about a curated entry EXCEPT its `kind` (the section it belongs to). A
  catalog-editor consumer that lets the user re-categorise an existing note must therefore remove+re-add
  (losing the id + created_at) just to move it between kinds — the desktop AI-manager adopter does exactly
  this as a workaround. Add an optional `kind:` param to `UpdateAsync` (COALESCE semantics like the other
  fields; `null` = leave unchanged) across all three backends so a re-categorise is a true in-place update.
  Small + additive; generic (any curated-catalog UI wants it). `src/Lyntai.Core/Storage/ICuratedMemoryStore.cs`
  + the SQLite/Postgres/InMemory impls.
  ✅ done 2026-07-27 — Outcome: `UpdateAsync` gains a trailing optional `kind` param (placed AFTER `title`,
  before `ct`, so no pre-existing positional call site silently re-binds) with COALESCE semantics (null =
  leave unchanged) across all three backends — SQLite/Postgres `kind = COALESCE(@kind, kind)` in the UPDATE
  (`kind` isn't in the FTS, and the SQLite `_au` trigger just re-syncs the unchanged content/title as a
  no-op → NO migration, the column already exists), InMemory `Kind = kind ?? e.Kind`. A re-categorise now
  keeps the id + `created_at`. Contract test `Update_can_recategorise_kind_in_place` wired to all 3 backends
  (InMemory/SQLite facts + the Postgres shared-container CRUD suite); 4 ApiSurface baselines updated.
  `verify` green (build · test · e2e · check-sensitive).

A design pass (2026-07-27) then generalised the whole payload story — and in doing so REVERTED the
unreleased CMEM3 `Title` and folded the released `Source` into the new field:

- [x] **CMEM6 — generic `Metadata` field + relational query index; fold & drop `Source`/`Title`.** Rather
  than keep adding a typed column per payload field (task/scope/title/kind), `CuratedMemory` gains one
  arbitrary app-owned `string→string` `Metadata` map, stored as an opaque JSON `metadata` column per backend
  (via the new Core `CuratedMetadataJson` codec — hand-written `Utf8JsonWriter`/`JsonDocument`, AOT-clean) and
  made QUERYABLE by a plain relational `lyntai_curated_meta(memory_id, key, value)` index — chosen over
  Postgres `jsonb` so filtering is IDENTICAL across all backends (no JSON-function divergence). The
  purpose-built `Source` (released) and `Title` (unreleased CMEM3) columns are RETIRED into `Metadata`.
  Breaking, inside the pre-1.0 D19 window; design in `docs/2026-07-27-curated-metadata-design.md`.
  ✅ done 2026-07-27 — Outcome: `CuratedMemory` drops `Source`/`Title`, gains
  `IReadOnlyDictionary<string,string>? Metadata`; `AddAsync`/`UpdateAsync` drop the `source`/`title` params
  and gain `metadata` (Update REPLACES the whole map; null = unchanged, empty = clear); `ListAsync`/
  `SearchAsync` gain a `metadataMatch` AND-of-pairs filter; search is now content-only and
  `CuratedMemorySections.Compose` drops the bold Title lead. Storage: opaque `metadata` TEXT column +
  `lyntai_curated_meta` index (FK `ON DELETE CASCADE`; written in the Add/Update transaction; `metadataMatch`
  → `EXISTS` per pair). Append-only, data-preserving migration `M202607270003_CuratedMetadata` ×2 backends —
  add column+index, backfill `source`/`title` into metadata, rebuild the SQLite FTS content-only / drop the
  Postgres title trigram index, then `DROP COLUMN`. Contract tests `Metadata_round_trips_updates_and_clears`
  + `Metadata_filter_matches_all_pairs` (+ reworked search/CRUD; the `Title_*` test retired) on all 3
  backends; 4 ApiSurface baselines regenerated; migration-count pins 15 → 16.
  `verify` green (build · test · e2e · check-sensitive).

---

## Part 23 — Deferred-findings burn-down (post-sign-off maintenance)

The remaining 2026-07-26 hardening-pass deferrals, taken up one by one after the 1.0-prep batch.

- [x] **I14 — bound `StreamLinesAsync`'s stderr capture** (it buffers ALL stderr but only ever uses a
  500-char tail) with a ring/tail reader.
  ✅ done 2026-07-27 — Outcome: `ReadTailAsync` (rolling 500-char StringBuilder window, chunked reads,
  completes on child EOF like `ReadToEndAsync` did) replaces the unbounded read on the STREAMED path only
  (`RunAsync`'s full stderr is contract). Test pins tail semantics across chunk boundaries on a 600 KB
  stderr spew (ends-with marker, ≤500 chars, start dropped). `verify` green (982 tests).

- [x] **L10/L11 — rate-limiter half-live options claim + `LlmVerdictClassifier` custom-matcher
  lock/copy-per-call** — both small; bundle with the next LLM-area task.
  ✅ done 2026-07-27 — Outcome: **L10** — `TokenBucketRateLimiter` options are now FULLY live (matching
  `HasEffectiveLimit`'s documented claim): rate/burst are per-acquire parameters resolved from live
  options, buckets hold only token state (a global limit enabled after construction gets a lazy bucket;
  a per-consumer retune applies to the existing bucket immediately), and an explicit zero-rate consumer
  now REFUSES after its burst instead of throwing (`TimeSpan.FromSeconds(Infinity)`). **L11** —
  `FromErrorText` reads a copy-on-write matcher snapshot (volatile array rebuilt on register/unregister)
  instead of lock+copy per classification. 3 new tests (live global enable, per-consumer retune,
  zero-rate refusal). 985 tests green.

- [x] **S3 — shared cap-evict SQL for the memory stores.** The `DELETE … NOT IN (SELECT … LIMIT @cap)`
  statement is char-identical in both dialects (count-cap semantics now in three places incl.
  `MemoryEviction.Survivors`). A raw-`DbCommand` helper beside `MemoryEviction.ApplyAsync` would
  single-source it without giving Core a Dapper dependency.
  ✅ done 2026-07-27 — Outcome: `MemoryEviction.CapEvictSql(mode)` in Core returns the one statement;
  both stores' `CapEvictAsync` collapse to a Dapper one-liner executing it (binding stays per-driver —
  a raw-`DbCommand` helper was deliberately NOT taken: a raw SQLite parameter bind would bypass the
  Dapper `DateTimeOffset` handler and risk a TEXT-format mismatch in `expires_at > @now`; sharing the
  STATEMENT is what kills the drift risk). Existing eviction tests are the net; Core baseline +1 line.

- [x] **S11 — drop the `(object?)x ?? DBNull.Value` dance in the Postgres stores** for typed nullable
  params (Dapper binds C# null as DBNull already); keep `::type` casts only where Npgsql can't infer.
  Do under the Testcontainers suite — Npgsql null-inference has edge cases.
  ✅ done 2026-07-27 — Outcome: all 20 sites (PostgresCuratedMemoryStore + PostgresMemoryStore) now bind
  typed nullable members directly — Dapper infers the DbType from the anonymous-member's STATIC type
  (`string?`→text, `int?`→int, `bool?`→boolean, `DateTimeOffset?`→timestamptz), so a null arrives typed.
  The SQL-side `::type` casts were deliberately KEPT (harmless, and they document the type at the
  `IS NULL` sites). Verified under the live Testcontainers suite (985 green, 0 skipped).

- [x] **T14 — de-flake the two wall-clock-coupled tests** (`Abandoning_the_stream_kills_the_child`'s fixed
  sleeps → bounded polling; `PerRequestTimeoutTests`' real-delay races → a ct-driven `DelayHandler`).
  ✅ done 2026-07-27 — Outcome: the abandonment test now POLLS to a 15s deadline for the heartbeat file to
  hold its size across two consecutive 300ms windows (≥5 missed beats = dead) instead of two fixed
  sleeps — load-independent, and it fails HARD if the child outlives the deadline. The timeout tests are
  now race-free by construction: the must-time-out calls use an infinite ct-driven delay (their own
  timeout is the only possible exit) and the must-complete call pairs a 1s response with a 60s override
  (a timer can never fire EARLY, so only the global leaking through — the regression under test — can
  fail it). The `DelayHandler` was already ct-driven; the margins were the residual flake.

- [x] **T5 — mid-stream CALLER-cancellation tests** (router after first committed chunk; agent session
  mid-stream) — OCE must propagate, no fallback, no bogus terminal. **T9** — promote the remaining
  SQLite-only memory-lifecycle semantics (TTL-refresh, recency-refresh, scoped/olderThan prune) into
  `MemoryStoreContract`. **T10** — pin curated-dedup casing + write the parallel dedup race test.
  ✅ done 2026-07-27 — Outcome: **T5** — router pin (cancel after the first committed chunk → OCE
  propagates, no fabricated terminal Error, zero fallback calls; `FakeLlmProvider` now observes ct
  between chunks like a real provider) + agent-session pin (cancel after the first event → OCE, no
  fabricated `SessionEnded`); both pass against existing code — the hardening-pass semantics hold, now
  pinned. **T9** — four contract methods (`Refreshing_a_fact_extends_its_ttl`,
  `Re_remembering_refreshes_recall_recency`, `Prune_older_than_removes_by_age_within_a_task` —
  task-scoped so the shared PG container is safe — and `Prune_scoped_to_one_task_leaves_the_sibling`,
  proving survival via a second scoped prune instead of a SQLite table peek) wired to all 3 backends;
  the SQLite-only originals removed from `MemoryLifecycleTests` (now genuinely unique regressions only).
  **T10** — dedup identity pinned case-SENSITIVE ×3 backends (kind/content/task casing variants each
  insert); the 8-writer race test pins the documented best-effort contract (post-race dedup adds return
  ONE stable id = the first row's; row count bounded by racer count). 999 tests green.

- [x] **T4 remnants — Postgres coverage:** `IJobStore.FailAsync` (retry-requeue timestamp math) has no PG
  test; the usage-tracker case-sensitivity test lacks its PG leg; response-cache `MaxEntries` eviction is
  SQLite-only; `Curated_memory_crud_and_filters` (PostgresStorageTests) still hand-copies contract methods.
  ✅ done 2026-07-27 — Outcome: `Fail_with_retry_requeues_available_later` lane-parameterized and routed
  through the `JobPg` runner (timestamptz retry math now exercised); `UsageTracker_consumer_totals_
  aggregate_across_casings` PG leg (Uid-seeded casing variants); PG `MaxEntries` eviction test using a
  FAR-FUTURE clock so its entries strictly outrank other tests' rows in the shared table (trim is
  table-wide; serial collection makes leftover eviction harmless; rows removed after via `RemoveAsync`);
  the hand-copied `Curated_memory_crud_and_filters` replaced with the real contract methods, which were
  made shared-container-safe (kind-parameterized, `GetAsync(-1)`/`UpdateAsync(-1)` for guaranteed-missing
  ids instead of `id+9999`, cross-kind enabled filter asserted by containment not table-wide count).
  1002 tests green.

- [x] **S8 — move the remaining 4 Row-DTO pairs (trace/score/prompt-version/usage) to Core** like
  `JobRow`. Deferred: pure materialization with zero dialect content — inert duplication, no fencing-style
  drift risk; weigh the Core-surface bloat before doing it.
  ✅ closed 2026-07-27 — **REJECTED after weighing** (the weighing was the open question): the move
  requires making 7–8 materialization DTOs PUBLIC (adapters can't see Core internals) — surface bloat
  against the direction of the 1.0 sign-off — for duplication rated inert; drift fails the cross-backend
  contract tests immediately. Rationale recorded in `docs/DECISIONS.md` D18; revisit only if a third
  relational backend materializes.

- [x] **T11 — convert the storage contract classes to abstract-class-with-[Fact]s** so a backend can't
  silently skip a contract method (the mechanism that produced the PG coverage holes). Postgres keeps its
  deliberate Uid-subset delegators.
  ✅ done 2026-07-27 — Outcome: eight `*ContractFacts` abstract bases (KeyValue, Conversation,
  PromptVersion, Trace, CuratedMemory, Memory — with the two-factory + mutable-clock shape — Score, and
  Jobs — with the store+clock factory), each deriving InMemory + SQLite classes that supply only the
  factory; every contract [Fact] is inherited, so a NEW contract method wired once runs on every derived
  backend automatically. Backend-specific tests (SQLite concurrency/affinity/lease-boundary) stay on the
  derived classes. Postgres deliberately does not derive (documented on each base: shared container →
  Uid-namespaced subset). Fact count identical before/after (1002) — nothing lost in the conversion.

- [x] **I5 — ProcessRunner shared session/reap extraction.** `RunAsync`/`StreamLinesAsync` share ~45 lines
  of spawn/stderr-drain/kill-registration/reap scaffolding. The I2 hang fix already landed; the extraction
  is cleanliness. Keep the two CLOCK topologies separate (buffered dual-clock vs streamed single-clock —
  they are different contracts).
  ✅ done 2026-07-27 — Outcome: the genuinely shared piece post-hardening is the post-read-loop tail —
  the I2 fresh-window discipline (fresh window for the stdin observe → observe the writer → fresh window
  for the reap → reap → drained stderr) — now `ObserveStdinAndReapAsync(process, stdinTask, stderrTask,
  reArm, killed)`, with each path passing its OWN clock re-arm (the buffered dual-clock/tagged-reason and
  streamed single-clock topologies stay separate exactly as the deferral demanded; `Start`/
  `WriteStdinAsync`/`KillTree` were already shared, and the stderr readers differ deliberately —
  full-buffer contract vs bounded tail). Pure refactor; the ProcessRunner suite (timeout/drain-liveness/
  stdin-coverage/kill/maxDuration) is the net — 1002 green.

- [x] **P5 — extract the 5×-copied provider streaming read-loop into a Core helper.** Every streaming
  provider (`ExtensionsAiProvider`, `OpenAiCompatibleProvider`, `LocalProvider`, `ClaudeCliProvider`,
  `ClaudeAgentSession`) hand-rolls the same manual-enumerator + inactivity-clock re-arm + OCE-filter +
  map-exception-to-terminal loop — the exact pattern that shipped the wall-clock bug twice. Deferred:
  yield/finally semantics must be preserved exactly; do it TDD against the existing inactivity tests as
  its own focused task, not inside a broad pass. Sketch in the review: `Lyntai.Llm.Streaming.ReadWithInactivityClock<T>`.
  ✅ done 2026-07-27 — Outcome: `Lyntai.Llm.Streaming.GuardedStream.ReadAll<TItem, TTerminal>` +
  `InactivityClock` (Arm/Stop over the provider's linked CTS) own the guarded loop once: arm → read →
  stop, caller-cancel ALWAYS rethrows, any other fault maps via the provider's `onFault` (null =
  propagate unchanged — how the CLI paths keep ALL OCE flowing to the router per T8). All five providers
  now iterate it; the three clock-owning providers pass an `InactivityClock`, the two CLI paths pass none
  (their window is `ProcessRunner`'s, arriving as `ProcessTimeoutException`); `TTerminal` is generic so
  the agent session yields `SessionEnded` terminals (closing over the loop-mutated `lastSessionId`).
  Yield/finally preserved: each provider keeps its own enumerator `await using`, source disposal, and
  post-loop semantics. Full `verify` green incl. e2e (the streaming smoke); Core baseline +2 types;
  `llm-and-router.md` updated to name the helper as the canonical provider-side shape.

---

## Part 24 — Built-in embedder for the OpenAI-compatible provider (2026-07-27)

Requested by the desktop AI-manager integration: an app already on an OpenAI-compatible chat endpoint had
no way to turn on semantic memory without writing its own `IEmbedder`.

- [x] **EMB1 — ship an `IEmbedder` over an OpenAI-compatible `/v1/embeddings` endpoint.** Today Lyntai
  defines the `IEmbedder` interface (`src/Lyntai.Core/Embeddings/IEmbedder.cs`) + `AddEmbeddings(...)`, but
  ships NO implementation — every consumer of `ISemanticMemory` must BYO one, which is the sole blocker to
  turning on semantic recall for an app that ALREADY talks to an OpenAI-compatible chat endpoint via
  `Lyntai.Providers.OpenAiCompatible` (the desktop hits Ollama / LM Studio / OpenAI). Add a built-in
  `HttpEmbedder` in the `Lyntai.Providers.OpenAiCompatible` package (POST `/v1/embeddings`, `{model, input[]}`
  → `data[].embedding`, batched) + an `AddOpenAiCompatibleEmbedder(id, o => { o.BaseUrl; o.Model; o.ApiKey; })`
  builder method, reusing the same BYO-`HttpClient` seam + endpoint-flavor detection the chat provider has.
  Ollama (`/api/embeddings` / its OpenAI-compat `/v1/embeddings`) + OpenAI + LM Studio all speak this, so one
  impl unlocks local **and** hosted embeddings. Generic + app-agnostic; pairs with the existing
  `UseSqliteVectorStore` / pgvector path. Live-gate against a real endpoint like the chat provider's Ollama test.
  ✅ done 2026-07-27 — Outcome: `HttpEmbedder` + `OpenAiCompatibleEmbedderOptions` +
  `builder.AddOpenAiCompatibleEmbedder(id, cfg, httpClient?)` in `Lyntai.Providers.OpenAiCompatible`
  (`HttpEmbedder.cs`, `OpenAiCompatibleEmbedderOptions.cs`, `OpenAiCompatibleBuilderExtensions.cs`). One
  `{model, input[]}` body serves every flavor; `TryExtractVectors` reads BOTH the OpenAI/LM-Studio
  `data[].embedding` shape (re-ordered by the authoritative `index`) and Ollama's `embeddings[[…]]` shape
  (plus the legacy single `embedding[]`). Endpoint/flavor reuse the chat provider's `ProviderDetect`
  (Ollama → native `/api/embed`; bare Azure resource → `/openai/v1/embeddings`; else `/v1/embeddings`,
  not double-prefixing a `/v1` base); same BYO-`HttpClient` seam + `lyntai.embedder.{id}` named client;
  per-call deadline is `LyntaiOptions.ProviderTimeout`; failures THROW (Recall is fail-open, Remember
  surfaces). `BatchSize` splits an over-cap input list. 11 unit tests (StubHttpHandler) + a live-gated
  Ollama embedding test (`LYNTAI_LIVE_OLLAMA` / `LYNTAI_OLLAMA_EMBED_MODEL`). Core untouched; OpenAiCompatible
  API baseline +3 types (additive, no break). `verify` green (build · test · e2e · leak scan).

---

## Part 26 — Generalize the MCP tool-hosting seam (2026-07-29)

_Raised in review: `Lyntai.Providers.ClaudeCli.Mcp` was named for one consumer but its csproj referenced
**only `Lyntai.Core`** — ~85% of it was provider-neutral machinery with no code coupling to the claude
adapter at all. The genuinely vendor-specific slice was ~35 lines of strings (the
`--mcp-config`/`--settings`/`--allowedTools` flags, the `mcpServers` and `permissions.allow` JSON shapes,
the `mcp__<server>__*` pattern). It also read as an asymmetry: `Lyntai.Tools.Mcp` is the INBOUND direction
and is named for the concept; this was the OUTBOUND direction and was named for a consumer. Rationale +
the constraints that shaped the split are `docs/DECISIONS.md` D23._

- [x] **MCPH1 — extract `Lyntai.Tools.Mcp.Hosting`** — a new Core-only package carrying the Kestrel +
  `ModelContextProtocol.AspNetCore` weight, owning the neutral lifecycle (bearer token, host start/stop,
  owner-only temp files, teardown, no-tools short-circuit) plus `McpToolHostOptions` (server name, bind
  address — previously a hard-coded const).
  ✅ done 2026-07-29 — **Outcome:** `src/Lyntai.Tools.Mcp.Hosting/` with `McpToolHost`, `ToolFunction`,
  `McpToolHostProvisioner`, `McpToolHostOptions`, `AddMcpToolHost(dialect, configure?)`. Temp-file paths
  are tracked by the PROVISIONER (handed to the dialect as a `WriteTempFile` callback), so a dialect
  cannot leak a token-bearing file. Covered by `McpToolHostTests` (7 tests incl. dialect-throws teardown).
- [x] **MCPH2 — `IMcpCliDialect` seam** — an interface, not a format string: the variation across CLIs is
  structural (JSON vs TOML config, different flags and allow-list conventions).
  ✅ done 2026-07-29 — **Outcome:** `IMcpCliDialect` + `McpEndpoint` + `McpCliContext` in **Core**
  (`Lyntai.Agents`). Core placement is load-bearing — it lets a provider package ship a dialect without
  referencing the host package. Deviation from the planned shape: `McpCliLaunch(ExtraArgs, TempFiles)` was
  dropped in favor of `McpCliContext.WriteTempFile` + returning args only; cleanup then lives in one place
  and can't be forgotten by a dialect.
- [x] **MCPH3 — claude dialect into the PROVIDER package; the add-on package removed.**
  ✅ done 2026-07-29 — **Outcome:** `ClaudeCliMcpDialect` ships in `Lyntai.Providers.ClaudeCli` (owner's
  call) at **zero new dependencies** — it is JSON + strings over Core types.
  **Constraint that shaped this:** the provider package must NOT reference the hosting package, or
  `Microsoft.AspNetCore.App` lands in every app using the plain CLI provider (a WinForms/console consumer
  would then need the ASP.NET Core runtime) *and* the provider loses `IsAotCompatible`. That is exactly
  what the `ICliToolProvisioner` seam exists to prevent, so only the dialect moved, never the host.
  `Lyntai.Providers.ClaudeCli.Mcp` first shrank to a one-line composition shim, then was **deleted
  outright in a follow-up** — a package id worth only `new ClaudeCliMcpDialect()` of typing doesn't earn
  its versioning + doc footprint, and removing it restores D3's never-adapter→adapter to zero exceptions.
  Callers use `AddMcpToolHost(new ClaudeCliMcpDialect())`. Breaking, shipped as a MINOR under the new
  **D24** amendment (all consumers are first-party today; strict SemVer resumes on the first third-party
  dependant).
- [x] **MCPH4 — keyed `ICliToolProvisioner` resolution** — the real defect behind the naming issue.
  ✅ done 2026-07-29 — **Outcome:** `AddMcpToolHost` registers keyed on `IMcpCliDialect.ProviderId`, with
  the first registration also taking the unkeyed slot as fallback; `AddClaudeCliProvider` prefers the
  keyed lookup. Two CLI providers with different dialects no longer collide (previously first
  `TryAddSingleton` won and the wrong dialect was injected into both). Covered by
  `CliToolProvisionerResolutionTests`.

**Outcome (whole part):** shipped in two commits — the split (additive), then the shim removal
(breaking). Net: a new `Lyntai.Tools.Mcp.Hosting` package (5 files), `ClaudeCliMcpDialect` +
`IMcpCliDialect`/`McpEndpoint`/`McpCliContext`, and `Lyntai.Providers.ClaudeCli.Mcp` gone — 11 packable
projects, down one from 1.0. The relocation itself touched no public surface (every moved type was
`internal`); the only break is the removed package + `AddClaudeCliMcpTools`, migrated by
`AddMcpToolHost(new ClaudeCliMcpDialect())`. Version consequence settled by **D24** (documented breaks
may ship in a minor while all consumers are first-party); the bump itself is the release pipeline's job.
Docs: DECISIONS D23 + D24 (+ a forward pointer on D22), CHANGELOG header amendment and an Unreleased
**Breaking** entry with a migration diff, README (generic host + worked custom-dialect example), AOT.md,
ROADMAP, CLAUDE.md, both design docs, and `.claude/knowledge/extending-lyntai.md` gained a fifth
extension point. `verify` green on both commits.

_Out of scope (unchanged): the `ClaudeAgentSession` path — its `ClaudeAgentOptions.McpConfigPath` is an
app-hosted, out-of-process MCP server and does not go through `ICliToolProvisioner`._

---

## Part 27 — Backend version & upgrade awareness (2026-07-30)

_Filed in `TASKS.md` as "Part 26 — Claude CLI version & upgrade awareness (CLI2/CLI3)"; renumbered here
because the archive's Part 26 was already taken by the MCP-hosting split. Net-new capability (CLI1 is
archived, unrelated). The ClaudeCli provider reported only binary PRESENCE (`ClaudeCliProvider.IsAvailable`)
and the ACTUAL resolved model only as post-run telemetry (`AgentStreamEvent.UsageFinal.Model`, scraped
per-turn from the CLI stream-json). A consumer desktop showing a stale hardcoded model needed the installed
CLI **version** + the **resolved default model** WITHOUT running a turn, plus a seam to drive the CLI's own
self-update — a real version on a fresh install, and a guided upgrade._

- [x] **CLI2 — Claude CLI version/model probe** — an app-agnostic probe on the ClaudeCli provider that,
  without running an agent turn, reports the installed CLI **version** (`claude --version`) and — where the
  CLI can report it cheaply — the **resolved default model**. Behind the SAME BYO `IProcessRunner` /
  `ClaudeCommand.Resolve` env seams; fails safe (CLI absent/unreachable → null, like `IsAvailable`).
- [x] **CLI3 — CLI self-update seam** — an app-agnostic way to run the CLI's OWN updater (`claude update`)
  and/or report "update available", so a host offers one-click upgrade instead of hand-shelling a terminal.
  Managed/pinned-binary PROVISIONING stays OUT of scope (the host's concern).

✅ done 2026-07-30 — **Outcome:** shipped as a **Core capability pair with the claude CLI as first
implementer**, not as adapter-only methods (mid-task steer: "building for one provider often means adding
the interface in Core so other providers implement their own logic"). `IProviderInstallation.ProbeAsync` →
`ProviderProbeResult { Available, Version, Model?, Detail }` and `IProviderUpdater.UpdateAsync` →
`ProviderUpdateResult { Succeeded, Updated, FromVersion, ToVersion, Detail }` (Core, `Lyntai.Llm`) — two
OPTIONAL interfaces rather than members on `ILlmProvider`, so a backend that can't answer cheaply just
doesn't implement one and callers pattern-match over the registered provider collection. The capability
generalizes beyond CLIs (a server version endpoint, a local runtime naming its loaded weights).
`ClaudeCliProvider` implements both via `--version` / `update` through the existing `IProcessRunner` +
`ClaudeCommand` seams, neutral cwd, no stdin; parsing is `ClaudeVersionLine` (internal, source-gen regex).
Probe: 30s stall detector (a version readout is sub-second, and a probe must not hang a settings screen for
the 2-minute provider timeout); update: the configured provider clocks, since it downloads. `Updated` is a
before/after version comparison — the CLI has NO check-only mode, so "was an update available?" can only be
answered after the fact. **Live-probe finding that shaped the design:** `--version` is the only turn-free
question the CLI can be asked — an unrecognized token is treated as a PROMPT and spends a turn (`claude
zzznotacommand` answered in prose), and `config`/`models` hang; so `Model` is null against today's CLI
rather than invented (documented, with `UsageFinal.Model` as the caller's fallback), and the field fills in
only from an explicitly labelled `model:` on a version line. 20 tests (`ClaudeCliProbeTests`: fake-runner
unit coverage of both happy paths + missing binary / nonzero exit / stall, the version-line parser, a
capability-discoverability assertion pinning the Core seam, and two real-spawn tests against the stub);
`FakeProcessRunner` gained a `RunHandler` selector so a test can script a probe → update → re-probe
sequence; the provider stub answers `--version` / `update|upgrade` before reading stdin. Both API baselines
updated (purely additive: Core +2 interfaces +2 records, ClaudeCli +2 methods). Docs: CHANGELOG Unreleased,
README "Backend version + guided upgrade", `.claude/knowledge/pitfalls.md` (the unrecognized-token trap).
`verify` green (build · 1053 tests · e2e 3/3 · leak scan).

---

## Part 28 — Provider probe/update CLI spawn on Windows (2026-07-30)

_Filed in `TASKS.md` as "Part 26 — provider probe/update CLI spawn on Windows (CLI2)"; the `CLI2` id is
reused (Part 27's CLI2 shipped the probe itself — this is a spawn bug found while CONSUMING it). Bug found
consuming 1.2.0 on Windows: the turn-free `IProviderInstallation.ProbeAsync` + `IProviderUpdater.UpdateAsync`
appeared not to resolve/spawn the backend command the way the COMPLETION path does, so a Windows npm/nvm CLI
shim broke. App-agnostic — hits any host on Windows whose `claude` is an npm-installed shim rather than a
real `.exe`._

- [x] **CLI2 — probe/update spawn the RAW resolved command → fail on a Windows npm/nvm shim** —
  `ClaudeCliProvider`'s `ProbeAsync` (`claude --version`) and `UpdateAsync` (`claude update`) spawn the
  resolved command directly, so when `claude` resolves to an EXTENSIONLESS npm/nvm shim (a `claude` launcher
  script with no `.cmd`/`.exe` on the nodejs bin dir) the spawn throws **"The specified executable is not a
  valid application for this OS platform"** → `ProviderProbeResult.Available=false` /
  `ProviderUpdateResult.Succeeded=false` (observed via the consumer's host stderr). The COMPLETION path
  (`StreamAsync`) runs the SAME `claude` fine on that machine, so probe/update should resolve + spawn via the
  SAME Windows shim handling (`.cmd`/`.exe` resolution, or a `cmd.exe /c` wrapper) as a completion — not a
  bare spawn of the resolved path. Impl: `src/Lyntai.Providers.ClaudeCli/ClaudeCliProvider.cs` (the
  `IProviderInstallation`/`IProviderUpdater` members). Fail-safe is intact (false, not a throw) — the gap is
  that it should SUCCEED for a shimmed install. (Consumer workaround already in place — a host-side shell
  `cmd.exe /c claude --version` / terminal `claude update` fallback — to be removed once this lands.)

✅ done 2026-07-30 — **Outcome:** fixed **one layer below where it was filed**. The report's premise (probe/
update spawn differently from a completion) doesn't hold in the code: `ProbeAsync`/`UpdateAsync` already go
through the same `ClaudeCommand.Resolve` + `IProcessRunner` seams as `CompleteAsync`/`StreamAsync`. The real
defect was in the SHARED spawn path — `ProcessRunner.ResolveLauncher` special-cased only `.ps1`, so any
resolved launcher CreateProcess can't exec was spawned raw. An npm/nvm global install writes three launchers
side by side (`claude` = a POSIX `sh` script, `claude.cmd`, `claude.ps1`); whenever resolution landed on the
extensionless one — a `where.exe` hit list without the `.cmd` (`Locate`'s `hits[0]` fallback), or a
caller-supplied/`CLAUDE_CMD` path pointing straight at the shim, which bypasses `where.exe` entirely — the
spawn threw Win32 193. Fix: `ResolveLauncher` now swaps a non-exec'able launcher for its spawnable **sibling**
(`.cmd`/`.bat`/`.exe`/`.com`, then `.ps1` via the existing PowerShell host), probing siblings only for paths
with a directory component so a bare name can never be answered by a same-named file in the current
directory. Deliberately NOT a `cmd.exe /c` wrapper (that would re-introduce a shell into the argv path, and
`cmd /c <extensionless sh script>` fails too) and deliberately NOT a fix at the provider call site — the
runner is the single place that knows how to launch things, so completions, the agent session and the
maintenance seams are all fixed at once. Repro'd first: 3 tests failing with the exact field error
("The specified executable is not a valid application for this OS platform") — two in `ProcessRunnerTests`
(extensionless shim rescued by a `.cmd` sibling; by a `.ps1`-only sibling) and one in `ClaudeCliProbeTests`
(`Probe_and_update_work_against_a_windows_npm_shim_install` — probe + update over a REAL spawn of an
npm-shaped shim wrapping the provider stub). No public API change (all-private), so no baseline update.
Docs: CHANGELOG (plus stamping the never-stamped `1.2.0` heading, which the automated release commit left as
"Unreleased"), `dev-conventions.md` + `llm-and-router.md` (shim handling belongs in the runner, never at a
call site), `pitfalls.md`. `verify` green (build · 1056 tests · e2e 3/3 · leak scan).

---

## Part 29 — Turn-free backend AUTH + pinned self-install (2026-08-04)

_Filed in `TASKS.md` as CLI3 + CLI4. Completes the "ask and drive the backend about itself without spending
a turn" family Part 27 started: Lyntai could ask what the backend IS and drive its updater, but the one
thing every consumer needs settled before a turn can possibly succeed — is it authenticated at all? — had no
seam, and `update` could not express "install THIS version"._

- [x] **CLI3 — a turn-free AUTH seam for the CLI provider (`IProviderAuth`), completing the pair CLI1 started.**
  Lyntai already owns *asking/driving the backend about itself* without spending a turn:
  `IProviderInstallation.ProbeAsync` (what is it, which version) and `IProviderUpdater.UpdateAsync` (drive the
  backend's own updater). The one thing every consumer still needs before a turn can possibly succeed —
  **is the backend authenticated at all?** — has no seam. Today a consumer either runs a completion and
  pattern-matches the failure string (a wasted, possibly billed turn) or shells out to the CLI itself, which
  is precisely the bespoke provider handling Lyntai exists to remove.

  **The backend supports it — measured, not assumed** (claude CLI v2.1.220, 2026-08-03):
  - `claude auth status --json` — and **`--json` is the DEFAULT**, so state is machine-readable with no prose
    parsing. This is the turn-free primitive, exactly as `claude --version` was for the probe.
  - `claude auth login [--claudeai | --console] [--email <e>] [--sso]` — two distinct account kinds
    (subscription vs Console/API billing) plus an SSO variant.
  - `claude auth logout`.

  **Suggested shape** (the contract is what matters; the steps are a suggestion):
  ```csharp
  public interface IProviderAuth   // OPTIONAL capability, pattern-matched like the others
  {
      Task<ProviderAuthStatus> StatusAsync(CancellationToken ct = default);            // MUST NOT run a completion
      Task<ProviderAuthResult> LoginAsync(ProviderLoginRequest req, CancellationToken ct = default);
      Task<ProviderAuthResult> LogoutAsync(CancellationToken ct = default);
  }
  public sealed record ProviderAuthStatus(bool Authenticated, string? Method, string? Account, string? Detail);
  public sealed record ProviderLoginRequest(string? Mode = null, string? Email = null, bool Sso = false);
  public sealed record ProviderAuthResult(bool Succeeded, ProviderAuthStatus? Status, string? Detail);
  ```
  - "Not signed in" is a **value, not an exception** — `StatusAsync` shouldn't throw for the normal case a
    caller is asking about.
  - `LoginAsync` is **interactive by nature** (it opens a browser), so the honest contract is "start it and
    report what happened", not "guarantee a signed-in state". Please state in the XML docs whether it blocks
    until the browser flow completes — a UI has to choose between a spinner and a "finish in your browser"
    instruction, and can't tell from the signature.
  - Reuse the **CLI2** launcher fix: on Windows the resolved `claude` may be an extensionless npm/nvm shim,
    which is exactly what broke probe/update in 1.2.0.
  - **Keep it provider-NEUTRAL, not Claude-shaped.** `Method` and `ProviderLoginRequest.Mode` are free-form
    strings on purpose: another backend's account kinds (Codex, a gateway, an enterprise SSO tenant) must fit
    without an enum change or a breaking bump. A provider that has no login story simply doesn't implement
    the interface, exactly as with the existing optional capabilities.
  - **Cancellation matters more than usual here.** A browser flow can be abandoned by the human, so a caller
    needs `ct` to actually abort the wait and needs a bounded default rather than a hang. Please say which.
  - **The testable part is the parsing, and it should be reachable without a process.** Splitting "run the
    command" from "interpret its output" lets the `auth status` JSON shapes (signed-in, signed-out, malformed,
    empty) be unit-tested through the existing fake-runner seam; only the live round-trip needs a real CLI.
  - **Non-goals:** no credential storage in Lyntai (the CLI owns its own credentials), and no binary
    provisioning — that stays the host's concern.
  - **Where:** beside `IProviderInstallation` / `IProviderUpdater` in `Lyntai.Llm`, implemented by
    `ClaudeCliProvider` (which already implements both). Public surface changes, so the `ApiSurface`
    baselines need a deliberate update.
  - **Done when:** a consumer can ask "is this backend signed in, and as whom?" without running a completion;
    "not signed in" comes back as a value; login/logout are drivable; and the XML docs state the blocking and
    cancellation behaviour of `LoginAsync` so a UI can be written against it without experimenting.

- [x] **CLI4 — let `IProviderUpdater` (or `IProviderInstallation`) drive the backend's own PINNED install.**
  Smaller, and separable from CLI3. `claude install [stable|latest|<version>] [--force]` exists (same CLI
  version as above), so the backend can install a SPECIFIC version of itself. That is the same class of thing
  as `claude update` — the backend's own tooling, which Lyntai already drives — and it is the difference
  between a consumer *pinning* a known-good version and merely taking whatever `update` gives it.

  The existing split says Lyntai "never provisions/pins a binary — that's the host's concern", and that
  remains right for *fetching a binary from nowhere* (a host owns its own download/storage policy). But
  driving an already-present backend's own installer is not provisioning; it is the update path with an
  argument. Suggested: `UpdateAsync(string? targetVersion)` overload, or
  `InstallAsync(ProviderInstallRequest)` on `IProviderInstallation`, returning the existing
  `ProviderUpdateResult` shape (`Succeeded`/`Updated`/`FromVersion`/`ToVersion`/`Detail`) so callers keep one
  result type. If you'd rather keep pinning entirely host-side, say so in `docs/DECISIONS.md` and we'll do it
  in the host — the point is to decide it once, in one place, rather than each consumer guessing.

  **Done when:** either the seam exists (a consumer can ask the backend to install a NAMED version and learn
  what it moved from/to), or `docs/DECISIONS.md` records that pinning is host-only and why — a decision
  either way closes this. Public surface changes if built, so `ApiSurface` baselines need a deliberate update.

✅ done 2026-08-04 — **Outcome:** two more Core capabilities with the claude CLI as first implementer,
following Part 27's pattern exactly. **CLI3:** `IProviderAuth` (`StatusAsync`/`LoginAsync`/`LogoutAsync`) +
`ProviderAuthStatus { Authenticated, Method?, Account?, Detail? }` / `ProviderLoginRequest { Mode?, Email?,
Sso }` / `ProviderAuthResult { Succeeded, Status?, Detail? }` in `Lyntai.Llm`; `ClaudeCliProvider` implements
it over `claude auth {status,login,logout}`. **Deviations from the suggested shape, both deliberate:**
`LoginAsync(ProviderLoginRequest? request = null, …)` — a UI that just wants "sign in" shouldn't have to
construct an all-defaults record; and `Succeeded` is defined as "the command reported success" with `Status`
(re-read AFTER the command, as `UpdateAsync` re-probes) as the authority on the resulting state, which is the
same split `ProviderUpdateResult` already uses. Answers to the task's two explicit questions, now in the XML
docs: `LoginAsync` **blocks** until the flow finishes/fails, bounded by a 10-minute budget applied to BOTH
runner clocks (a login prints a URL then goes silent while a human clicks, so the per-chunk inactivity window
a probe uses would kill a live flow), and `ct` abandons the wait. **Measured the CLI before designing**
(`claude auth --help`, `auth status --help`, `auth login --help`, and a shape-only dump of the real
`auth status --json`): the document is `{loggedIn, authMethod, apiProvider, email, orgId, orgName,
subscriptionType}`, so `Method`←`authMethod`/`apiProvider` and `Account`←`email`/`orgName`. Parsing is
`ClaudeAuthStatusJson` (internal, hand-walked over the shared `JsonExtract.TryParseObject` primitive per D17,
tolerant of a few key spellings) — split from the spawn exactly as the task asked, so every shape is
unit-tested without a process. Three semantics worth keeping: the parsed state **wins over the exit code** (a
signed-out CLI may report state and exit non-zero — an answer, not a broken backend); the whole body is
parsed, not `Tail()`'d (which keeps the LAST 500 chars and would decapitate a document); and `--json` is sent
**explicitly** despite being the default, so an older CLI rejects an unknown flag instead of billing a turn
for the sentence "auth status". **CLI4:** `IProviderVersionInstaller.InstallAsync(ProviderInstallRequest?)`
→ the existing `ProviderUpdateResult`, over `claude install [target] [--force]`. Chose a THIRD optional
interface over a member on `IProviderUpdater`/`IProviderInstallation`: the repo's capability idiom is
"doesn't implement it" rather than "returns not-supported", a backend can be self-updating without being
able to pin, and it keeps the change purely additive. `UpdateAsync` and `InstallAsync` now share one
`SelfMaintainAsync` (probe → run → re-probe), so the fail-safe paths can't drift apart. Both free-form values
are validated rather than forwarded: an unrecognized login `Mode` and a flag-shaped `Email`/`Version` are
refused **without spawning** (`ArgumentList` stops shell injection, not the backend's own option parser). 38
new tests (`ClaudeCliAuthTests` + a CLI4 section in `ClaudeCliProbeTests`), including real-spawn and Windows
npm-shim coverage; the provider stub answers `install` / `auth *` before reading stdin
(`LYNTAI_STUB_AUTH=out` for a signed-out state). Both API baselines updated (purely additive: Core +2
interfaces +4 records, ClaudeCli +4 methods). Docs: CHANGELOG Unreleased, README (section renamed "Backend
self-maintenance: version · upgrade · pinned install · auth"), **`docs/DECISIONS.md` D26** — which settles
CLI4's open question in one place: driving an already-present backend's own installer is IN (it is the update
path with an argument), while credential storage, binary provisioning and guessing stay OUT. `pitfalls.md`
gained four entries (pin a flag so an old CLI fails loudly; exit code vs. machine-readable answer; `Tail()`
decapitation; never forward a free-form value into argv). `verify` green (build · 1097 tests · e2e 3/3 ·
leak scan).

---

## Part 30 — Version-authorship guard (2026-08-04)

_Filed in `TASKS.md` as REL1, reported from a sibling repo where the failure actually fired on 2026-08-01 and
cost a whole version number. Lyntai had the identical exposure — verified in the code, not assumed._

- [x] **REL1 — guard the version against hand-edits (`src/Directory.Build.props`, `devtools/hooks/pre-commit`).**
  Reported from the sibling kit repo, where this failure **actually fired on 2026-08-01 and cost a whole
  version number**. Lyntai has the identical exposure — verified, not assumed: `release.yml`'s "Determine
  version" step reads `current` from `devtools/project.config.mjs` (i.e. `<VersionPrefix>`) and bumps from
  it, and nothing in `devtools/hooks/` or `devtools/scripts/` guards that file.

  **The failure mode.** A session hand-edits `<VersionPrefix>` (bumping "ready for the next release", which
  looks helpful). The workflow's empty `version` input means *bump from whatever VersionPrefix says*, so the
  baseline has silently moved: the run bumps again and publishes the version AFTER the intended one. Over
  there `0.1.2 → 0.2.0` by hand became a published `0.3.0`, and 0.2.0 went from unreleased to skipped
  without anyone deciding to skip it. The same slip on a post-1.0 repo lands on a MAJOR.

  **Why nothing catches it today.** `doctor` verifies the version is *consistent* across
  props/npm/README/LICENSE — and a hand-bump keeps all four consistent, so doctor stays green. Consistency
  was never the property at risk; **authorship** was. There is a second half: the workflow stamps the
  CHANGELOG's `## Unreleased` heading, so a session that also hand-stamps it leaves nothing to stamp, and
  the release ships with the wrong section title (that happened too).

  **The invariant, which holds in this repo right now** (`VersionPrefix` 1.2.1 = newest tag `v1.2.1`):
  between releases `VersionPrefix` equals the LAST RELEASED tag, because the workflow bumps it as part of
  releasing. So `VersionPrefix != newest tag` means a hand-edit, whatever the reason.

  **The fix, as implemented in the sibling — two layers, both sabotage-verified there:**
  1. a `doctor` check comparing `VersionPrefix` to the newest `v*` tag (state-based, so it catches a bad
     merge or rebase too; silent when no tags exist, so a shallow CI checkout still passes);
  2. a `check-version-bump.mjs` pre-commit guard that blocks a staged change to `<VersionPrefix>` or a
     removal of the `## Unreleased` heading, with a `SHENORA_RELEASE=1`-style env escape for the pipeline
     and for a human genuinely repairing a botched release.

  Port both, renaming the env var. Layer 1 alone is most of the value and is ~20 lines in `dev.mjs`.

✅ done 2026-08-04 — **Outcome:** both layers ported, env var renamed to **`LYNTAI_RELEASE=1`**, and both
sabotage-verified HERE (hand-bumped `VersionPrefix` → `doctor` exits 1 and the staged edit is blocked;
`LYNTAI_RELEASE=1` clears both; the version was restored and `git status` re-checked). Layer 1 is
`versionDoctor()` in `dev.mjs`, wired into `doctor` alongside the existing README check (both always run, no
short-circuit, so one pass reports all drift); `--fix` deliberately does NOT "fix" the version — a
hand-authored version is the problem, not the symptom. Kept OUT of `verify`/`pack` for a load-bearing reason:
the release workflow writes the new version *before* running both, so during a real release `VersionPrefix`
is *supposed* to be ahead of the newest tag; wiring it into `verify` would have failed every release. Layer 2
is `devtools/scripts/check-version-bump.mjs` + a second line in `devtools/hooks/pre-commit` (now `|| exit 1`
per guard so the first failure stops the commit), also exposed as `dev.mjs check-version`. **One deviation
from the ported design, found by sabotage-testing rather than by reading:** the sibling's CHANGELOG check
only looks for a REMOVED `## Unreleased` line, which silently misses the case where a session adds its notes
and titles them `## 1.3.0` in the same change — nothing is removed, yet the pipeline still has nothing to
stamp, and if the release turns out to be a different number the log ships a heading for a version that was
never released. A third check blocks INTRODUCING a `## X.Y.Z` heading (ignoring one merely re-added after
removal, so a whole-file rewrap is fine). A newly ADDED props file has no removal line, so seeding a repo is
never blocked. `release.yml`'s commit step now sets `LYNTAI_RELEASE: '1'` (belt-and-braces — `core.hooksPath`
is local config, so hooks aren't installed on a fresh runner). Docs: CHANGELOG Unreleased,
**`docs/DECISIONS.md` D25**, `.claude/rules/task-lifecycle.md` (never hand-edit either), `CLAUDE.md` dev-loop
list.

---

## Part 31 — Generalize the CLI provider seam + a second CLI backend (2026-08-04)

_Not a filed `TASKS.md` item — requested directly while CLI3/CLI4 were being reviewed: "for cli type of
provider the logic probably similar so you can create another one like codex, so we can have a more general
design for those", clarified with "for cli they got similar logic, a sub processor, a feedback loop, some kind
of io format and etc, so their interface or common implementation can be shared" and "we can have portable cli
too (not a global installation)". Recorded here because it changed load-bearing structure._

- [x] **CLI5 — extract the shared spawned-CLI logic behind a per-CLI dialect seam, and prove it with a second
  backend.** The claude provider had accumulated every CLI invariant (command resolution, spawn hygiene,
  inactivity clocks, verdict classification, empty-output handling, streaming order, probe → run → re-probe
  maintenance) as its own code, so a second CLI would have been a second copy — the exact shape of the drift
  `pitfalls.md` documents. Chosen approach (offered as an option, user picked it): a shared engine in Core with
  BOTH provider types kept public and delegating, so nothing existing breaks.
- [x] **CLI6 — support a PORTABLE CLI (an app-bundled binary), not just a global install.**

✅ done 2026-08-04 — **Outcome:** `CliProviderEngine` + `ICliProviderDialect` / `CliProviderDialectBase` /
`CliOutputEvent` / `CliPromptDelivery` / `CliCommand` in Core (`Lyntai.Llm.Cli`), with `ClaudeCliProvider`
refactored into `ClaudeCliDialect` + a dozen forwarding members. **Behaviour preservation was verified, not
asserted:** all ~90 existing claude tests pass UNTOUCHED, and the `ApiSurface` diff showed `ClaudeCliProvider`'s
members byte-identical (the only ClaudeCli addition was the new public dialect). Duplication genuinely
removed: the version-line parser, prompt flattening and command tokenizer are now single copies in Core, and
the claude forwarders for the first two were DELETED (their tests retargeted to the Core primitives with
byte-identical assertions) rather than left as aliases. 21 engine tests drive the generic contract through a
`FakeCliDialect`, so they can't pass by accident via claude's behaviour. Recorded as `docs/DECISIONS.md` **D27**.

**The second implementer immediately earned its keep — `Lyntai.Providers.CodexCli`** (new package, id
`codex-cli`), written against codex-cli 0.146.0 **measured** with `--help` plus one real successful turn (via
`--oss` + a local ollama model, so zero tokens) and one real failed turn. It surfaced FOUR things a
claude-only seam had wrong or missing: (1) **in-band failure** — codex prints `turn.failed` and exits 0, which
the dialect vocabulary couldn't express, so it was flattened to "no output produced" with no reason; that
became `CliOutputEventKind.Failure`, classified (401 → `AuthFailed`, which cools the host) and, mid-stream, a
terminal `Error` chunk instead of a `Final`; (2) the **mirror-image trap** — a bare `error` line and an
`item.completed` whose item type is `error` both appeared in the run that SUCCEEDED, so only the terminal event
may fail a call; (3) **neutral-cwd interaction** — codex refuses to run outside a git repo, so
`--skip-git-repo-check` is mandatory given the engine's temp-dir cwd; (4) **prose auth** — `codex login status`
has no `--json` at all, vindicating `ParseAuthStatus(string)` over a JSON-shaped contract, and its parser
returns "unknown" for an unrecognized wording rather than guessing signed-in (only the signed-OUT line could be
measured — signing in needs a real account, so that corner is documented as unverified in the source).
`CodexCliProvider` deliberately does NOT implement `IProviderVersionInstaller` (`codex update` takes no
target), which is the capability model paying off: pattern-matching gives a real answer, not a maybe. Also
measured-and-honoured: `logout` is top-level here, not `auth logout`; completions default to
`--sandbox read-only` (a text completion must not edit the caller's disk), raisable via
`CodexCliDialect.SandboxMode`; and codex's credential-reading login modes (`--with-api-key`,
`--with-access-token`, which read a SECRET from stdin) are refused by design — Lyntai never carries a
credential (D26). A separate `devtools/scripts/codex-stub.mjs` speaks codex's JSONL (a stub faking both CLIs
would stop being a faithful model of either), and the 37 codex tests were **mutation-checked**: sabotaging
`turn.failed` → Ignored and dropping `--skip-git-repo-check` failed exactly the 5 tests that should fail
(they were written after the implementation, so passing on the first run proved little on its own).

**Portable installs (D28):** `command` + `environment` are now parameters on `AddClaudeCliProvider` /
`AddClaudeCliAgentSession` / `AddCodexCliProvider` (previously the ONLY way to point at a non-PATH binary was a
process-wide env var — a real gap for an app shipping its own copy), the environment reaches MAINTENANCE spawns
too (else a probe/auth check reports the global install's state while completions use the portable one), and
`IsAvailable` now VERIFIES an explicit command exists via the new `ProcessRunner.CommandExists` (accepting an
extensionless launcher with a spawnable sibling — the CLI2 shim shape) instead of trusting it, so an
undeployed portable copy makes the router skip the candidate rather than surface as a failed turn.

Surface: Core + ClaudeCli baselines updated (additions plus optional-parameter extensions — source-compatible,
NOT binary-compatible, flagged in the CHANGELOG under D24), CodexCli baseline seeded; package registered in
`Lyntai.slnx`, `project.config.mjs`, `ApiSurfaceTests` and the test project. Docs: CHANGELOG, README (packages
table + a rewritten "CLI backends: claude, codex, or your own" section incl. the portable wiring),
`DECISIONS.md` D27/D28, `extending-lyntai.md` (path A2 = write a dialect), the `add-provider` skill (a
CLI-backend checklist that starts with "measure the CLI first"), `pitfalls.md` (+5 entries), `dev-conventions.md`,
`CLAUDE.md`. `verify` green (build · 1163 tests · e2e 3/3 · leak scan).

---

## Part 32 — Generation platform + a coherent package graph (2026-08-04)

_Filed in `TASKS.md` as MED1 (Part 32) by a consuming app, then widened twice by the owner: from "a media
domain" to **"not a generation engine — a media generation PLATFORM"**, and from media to **any generated kind**
(hence `Lyntai.Generation`, not `Lyntai.Media`). The 2.0.1 package restructure is recorded here too because it
was driven by the same work._

- [x] **MED1 — a generation domain as a Lyntai platform** (filed as `IMediaProvider`/`IVideoProvider`; see the
  deviation below). Plans of record: `docs/2026-08-04-generation-platform-plan.md` (the platform, Plans 1–7)
  and `docs/2026-08-04-restructure-2.0.1-plan.md` (the package graph).

✅ done 2026-08-04 (Plans 1–2 + the restructure; Plans 3–7 remain open) — **Outcome:** research changed the
contract before any code was written, and that is the part worth keeping. Measured across the August-2026
provider landscape: image generation is **inline**, video is **universally an async job** (WAN documents 1–5
minute renders as create-task-then-poll; Kling is `POST /v1/videos/generations` then `GET /v1/tasks/{id}`), and
audio **splits** — TTS streams (playback before generation ends) while music is a batch job. A single
`GenerateAsync` can therefore only express image, so the seam is three OPTIONAL capabilities
(`IGenerationProvider` inline, `IGenerationJobProvider` submit→poll→fetch, `IGenerationStreamProvider`), with
the **operation id exposed** so a paid render survives a process restart and composes with `Lyntai.Jobs`.
Two further findings: aggregators serve 1,000+ models across image/video/audio/**3D** behind ONE queue API, so
routing selects **backend + model** (`GenerationCandidate`) rather than one-provider-per-model; and capability
declaration is load-bearing here unlike LLM routing (chat models all take text, whereas a video backend simply
cannot serve an image request), so the router **pre-filters** on `GenerationCapabilities` and "nothing here can
do that" is `Unsupported` — a configuration answer, not a runtime fault.

**Deviations from the filed task, all deliberate:** (1) **no separate `IVideoProvider`** — video is
`Kind = "video"` plus a delivery mode, because what differs between media is how a backend DELIVERS, not which
medium it makes; a per-medium interface would need a third for audio and a fourth for 3D. (2) **`Kind` is an
open string** (`GenerationKinds.Image/Video/Audio/Model3d`), so a kind nobody has modelled is not a breaking
change, and no `Custom` constant was added because an open string already accepts any value. (3) **Chaining is
first-class** — `artifact.ToInput(role)` carries bytes-or-URI into the next stage (3d → image → video), tested
rather than assumed, though the pipeline RUNNER is deferred to Plan 7 until ≥2 real backends exist. (4) Media
keeps its own `GenerationVerdict` but **shares the failure corpus** (`GenerationVerdictClassifier` delegates to
`LlmVerdictClassifier`), so there is one definition of what a 429 means (D27's rule against a second copy).
(5) Fallback is a **policy**, not a law — `GenerationRoutingPolicy` defaults to §6 semantics but a host pairing
a hosted backend with a permissive local one can set `On(Refused, Advance)`; that was the owner's R18
requirement, and it is the host's call, not the library's.

**Shipped:** Plan 1 (contracts, capability model, three delivery seams, verdicts, capability-aware router, DI,
routing policy) and Plan 2 (three HTTP backends: OpenAI-compatible images with both `b64_json` and `url`
responses; a Stable Diffusion WebUI reporting `NotConfigured` when not running and probing for a LOADED
checkpoint; and a local **ComfyUI** job backend that is graph-shaped — the caller supplies the workflow, so
`Prompt` may be null and no default graph is invented). Artifacts are returned as **URIs where the backend
gave one** — never downloaded uninvited, on either the input or output side. ComfyUI's surface is documented
rather than measured (no instance available), so every endpoint path is a settable option and its 15 tests were
**mutation-checked**; the same discipline applied to the codex dialect earlier. Backend order set by the owner
after research: an aggregator (fal.ai) first for remote video — ~985 endpoints, one queue shape, 30–50%
cheaper than the nearest comparable — with a direct single-vendor integration rejected as the first backend
since hosted models are moderated at the API layer either way; TTS before music for audio; and a local
ComfyUI, because every hosted model enforces its provider's policy and a host cannot opt out
(`local/r18-backend-notes.md`, gitignored, holds those specifics — R18 support is a requirement of the
platform, not a goal of it).

**The 2.0.1 restructure** (`docs/DECISIONS.md` D31 + amendment): packages are split by **dependency
footprint**, never by vendor or size. `Lyntai.Providers.ClaudeCli`/`.CodexCli`/`.OpenAiCompatible` merged into
`Lyntai.Providers.Default`; `Lyntai.Generation` + `.Http` folded into Core and `Providers.Default` (both had
zero dependencies, so their boundaries isolated nothing); a `Lyntai` metapackage added for one-line installs;
**ASP.NET Core removed from MCP hosting** by moving `McpToolHost` onto `System.Net.HttpListener` +
`StreamableHttpServerTransport` (the ASP.NET package only supplied Kestrel routing glue), which is what let MCP
join the metapackage while Core stays free of the MCP SDK's pinned MEAI abstraction. 14 ids → 10 packages + a
metapackage. **No namespace, type or API changed** — every consumer edit is one `PackageReference` line — and
each fold was verified as an exact baseline UNION (byte-identical added lines, zero removals) with **0 test
files edited** across 1252 tests, which is what proves nothing moved. Two MCP findings recorded in D31 because
they are easy to re-break: the Streamable HTTP client needs the `Mcp-Session-Id` response header to pass
`initialize`, and requests must be handled CONCURRENTLY (the client holds a long-lived GET SSE stream open while
POSTing, so a sequential accept loop deadlocks — it presented as a 60s "Initialization timed out" while a raw
single POST answered in milliseconds).

**Still open:** generation Plans 3–7 (local subprocess backend; async video composed with `Lyntai.Jobs`;
governance/telemetry parity; the tool/MCP bridge + streaming TTS; pipelines). `verify` green throughout
(build · 1252 tests · e2e 3/3 · leak scan).

---

## Part 33 — Generation backends: local engine, durable renders, first remote queue (2026-08-04)

_The remaining slices of the generation plan, filed in `TASKS.md` as GEN3–GEN7. Three landed; GEN5–GEN7 and a
verification task stay open._

- [x] **GEN3 — local subprocess backend** (`sd-cli` / stable-diffusion.cpp) through `IProcessRunner`.
- [x] **GEN4 — async video composed with `Lyntai.Jobs`** (the durable half + the fal.ai queue backend).
- [x] **GEN6 (tool/MCP bridge half)** — `AddGenerationTools()`: generation reachable from an agent as `ITool`s.
- [x] **GEN5 — governance/telemetry parity for generation** (cost/budget, rate limiting, cooldown, OTel).

✅ done 2026-08-04 — **Outcome:** `LocalDiffusionProvider` runs stable-diffusion.cpp locally — no key, no
network, no content policy in the path, which is what makes it the local half of the pair
`GenerationRoutingPolicy` exists for. Its argv and size clamping are PORTED from a sibling app's working
implementation (no engine on the dev machine) and pinned by exact-argv tests, because that failure would
otherwise land on a user's render rather than on CI. Two ported details that look incidental and are not, both
asserted: the spawn's working directory is the BINARY's directory (the engine loads `ggml*.dll` from beside
itself) and sizes clamp to multiples of 64 within 256–768 (an engine requirement, and above that a CPU render is
minutes of waiting). It improves on the source implementation by going through `IProcessRunner` rather than
`Process`: BYO-runner seam, kill-tree cancellation, and an INACTIVITY clock with an absolute backstop instead of
one wall clock that would kill a healthy slow render.

`GenerationRenderJobHandler` + `IGenerationArtifactSink` make an asynchronous generation a durable job, and this
is where the platform earns its keep over a thin client: the operation id is **checkpointed before the first
poll**, so a crash, deploy or restart resumes polling the render already in flight instead of paying for a
second one. Each poll re-checkpoints to renew the lease; a LOST lease stops the handler outright (operation id
in the error, for manual recovery) rather than letting two workers drive one paid render. A submission no
candidate accepts FAILS (config, not transient) while a still-working backend retries, and a throwing sink
leaves the job to retry so a momentarily unavailable store cannot lose a finished render. Payload and checkpoint
JSON are hand-written per the `MemoryPruneJobHandler` precedent, keeping Core's trim/AOT claim honest.

`FalQueueProvider` is the first remote backend, chosen after research: one aggregator integration reaches the
Wan/Kling/Veo-class models behind a single queue shape, versus another auth and envelope per vendor for models
that are moderated at the API layer either way. The **operation id carries its model** (`"model#requestId"`)
because the queue's URLs need the model while a resumed job only has an operation id. A transport failure while
polling reports **Running, not Failed** — a 500 says nothing about a paid render still in progress — and an
unknown status is likewise non-terminal.

**Two unmeasured surfaces, deliberately shipped and flagged** rather than blocked: `sd-cli`'s argv (ported) and
fal's wire format (documented, no API key). Both make every path/field an option, degrade to "no artifacts"
rather than inventing one, and say so in their XML docs. GEN-VERIFY in `TASKS.md` closes them the first time each
runs for real — at the owner's direction ("okay to not test fully today; we are building the foundation").

`verify` green (build · 1296 tests · e2e 3/3 · leak scan).

✅ **GEN6 (tool/MCP bridge half)** done 2026-08-04 — **Outcome:** `AddGenerationTools()` registers five
`ITool`s, and that registration is the ENTIRE coupling between the generation and LLM domains: neither
references the other's concrete types, and because the LLM side already knows `ITool`, they work in the
in-process tool loop *and* — with `AddMcpToolHost(...)` — for a CLI agent running its own loop over MCP (the
owner's loose-coupling requirement, D30). Five tools rather than one because a model must DISCOVER before it can
choose: `generate_backends` reports what exists and what each backend supports, `generate` is inline, and
`generate_submit` → `generate_status` → `generate_fetch` is the asynchronous path a video render actually needs
(submitting to an inline-only backend returns one clear sentence, not a crash). **Bytes never enter an
observation** — a base64 image would blow the context window for nothing: artifacts go to the
`IGenerationArtifactSink` when one is registered, otherwise the observation reports type/size/URI. Bad arguments
come back readable so a model can correct itself, and unknown arguments pass through as backend options so a
model can use a backend's own knobs without Lyntai enumerating them. Lives in Core (needs only the `ITool`
contract already there, D31). One trap recorded: inside `namespace Lyntai`, a relative `Tools.X` binds to
`Lyntai.Tools` (the MCP package), so the registrations are fully qualified. 12 tests; `verify` green
(1308 tests).

✅ **GEN5** done 2026-08-04 — **Outcome:** the generation domain now has the LLM front door's governance, and
every piece REUSES that machinery rather than growing a second copy (the task's own constraint):
`DeadHostTracker` for cooldown, `IUsageTracker` for spend, `IRateLimiter`/`TokenBucketRateLimiter` for
throttling. What the reuse forced into the open were the two places the domains must NOT bleed together, both
pinned by tests: cooldown keys are **domain-prefixed** (`generation::<id>`), so a host whose chat provider and
image backend share an id — plausible, e.g. both "openai" — never has a chat outage bench its image renders;
and generation **throttles on its own `RateLimitOptions`** (`GenerationOptions.RateLimit`), because a render and
a chat turn hit different vendors' limits and one shared bucket would let a render starve the chat that asked
for it. The limiter is passed to the decorator rather than registered as `IRateLimiter`, so it can never
overwrite the chat one; `TokenBucketRateLimiter` gained a `RateLimitOptions` ctor for it (the `LyntaiOptions` one
now delegates — a pure addition).

Spend, by contrast, is deliberately SHARED: renders record into the same `IUsageTracker` as chat, because "what
has this app spent" has to be one number. Only COST caps bind a render — it spends no tokens and claims none, so
refusing one because chat exhausted a token budget would be governance by coincidence. The cap is checked before
a render AND before a **submission**, since submitting is what commits the money for a hosted video whether or
not anyone fetches it; the completed cost is recorded by `GenerationRenderJobHandler`, the only place that still
exists when a durable render finishes (billed BEFORE delivery, so a retrying sink cannot lose the charge). Two
supporting additions fell out of this: `GenerationRequest.Consumer` (round-tripped through the durable-job
payload, so a render resumed in another process still bills to whoever asked), and the agent tools defaulting to
consumer `"agent"` — a tool loop is the runaway-spend case, so `Budget.PerConsumer["agent"]` fences it off
without touching what a user pressing a button may spend. A model cannot name its own consumer (the key is
reserved and ignored in the tool schema) or it could route around its own cap.

The router's policy gained the two actions its own doc-comment had promised (`PenalizeAndAdvance`,
`CooldownAndAdvance`) with defaults mirroring design §6, plus `ExemptSoleCandidate` — benching the only capable
backend converts an actionable "rate limited, try in a minute" into a useless "no capable backend". Telemetry is
a **third** source/meter (`Lyntai.Generation`), not folded into the GenAI one: a render is not a `gen_ai.*` chat
operation and mixing them would corrupt token/duration aggregates a dashboard computes over the LLM source. The
two tags that do carry over keep their GenAI names (`gen_ai.system`, `gen_ai.request.model`) so spend can still
be grouped by vendor across both domains, and cost is a first-class metric (`lyntai.generation.cost`) because
there is no token proxy for it. Spans are per ATTEMPT, so a trace of a fallback run shows the backend that
failed as well as the one that worked. 24 tests; `verify` green (build · 1332 tests · e2e 3/3 · leak scan).

- [x] **GEN11 — the `Add*` shims' infinite HTTP timeout rests on a per-call deadline that does not exist.**
  Found 2026-08-04 by the consuming app adopting 2.1.0, from reading the release note rather than from a hang.

  `GenerationProviderBuilderExtensions` configures the named client with `Timeout.InfiniteTimeSpan`, and the
  changelog gives the reason: *"the per-call deadline owns cancellation (a render routinely outlives the
  100-second default)"*. The first half is right — 100s is genuinely too short for a render. But **there is no
  per-call deadline for the HTTP generation backends.** `GenerationRequest.TimeoutSeconds` exists on the
  contract and, as of 2.1.0, **no source file reads it** (grep of `src/` finds it only in XML docs and the
  compiled DLL); only `LocalDiffusionProvider` — the subprocess one — has its own timeout.

  So a consumer using `AddOpenAiImageProvider` / `AddAutomatic1111Provider` gets: infinite HttpClient timeout,
  no request deadline, no options deadline. A backend that accepts the connection and then stalls hangs the
  render until the caller's `ct` fires — and a UI that offers no cancel (a background job, a scheduled task)
  waits forever. That is a worse failure than the 100s cut-off it replaced, because it is unbounded and silent.

  **Suggested:** honour `GenerationRequest.TimeoutSeconds` in the HTTP backends (it already exists, so this is
  wiring not API), and/or give the options a `Timeout` like `LocalDiffusionOptions` has, defaulted generously.
  Then the shim's infinite client timeout becomes true rather than aspirational.

  **What the consumer did meanwhile**, in case it is useful as a data point: it constructs its own client with
  an explicit **180s** timeout — deliberately NOT infinite — because that restores the ceiling its pre-migration
  code had and keeps a bounded failure. It will switch to the shims once a deadline exists.

✅ done 2026-08-05 — **Outcome:** the premise was verified before it was fixed (`TimeoutSeconds` was read by
nothing but the durable-job payload's own serializer). The deadline is now real: an internal `GenerationDeadline`
(`src/Lyntai.Generation/GenerationDeadline.cs`) wraps every HTTP-calling entry point of all four backends, and
each options record carries a `Timeout` — 10 minutes for the inline render backends, 2 minutes for the queue ones
— overridden by `GenerationRequest.TimeoutSeconds` wherever a request is in hand.

**The load-bearing part is telling a fired deadline from the caller's cancellation**, since both arrive as
`OperationCanceledException` through the same linked token. The discriminator is the caller's own token: if `ct`
is cancelled the exception is theirs and is rethrown; otherwise the only clocks left are ours and the client's,
and both mean timeout — so a deadline yields a `GenerationVerdict.Timeout` RESULT (the seam is contractually
fail-safe) while cancellation still propagates. That is the LLM side's existing idiom
(`OpenAiCompatibleProvider.CompleteAsync`), reused rather than reinvented, and it is pinned by mutation: dropping
the `when (!ct.IsCancellationRequested)` filter fails exactly the two cancellation tests and no others. A bonus
fix falls out — a BYO client's own `HttpClient.Timeout` (what the consumer above is using, at 180s) previously
escaped as a raw `TaskCanceledException` and is now that same verdict.

**For the queue backends the deadline bounds ONE HTTP call, never the render** — recorded because the choice is
invisible in the code. The render outlives every individual call: `GenerationRenderJobHandler` polls it across
job re-dispatches and process restarts, so poll and fetch arrive with no memory of the submit and no request in
hand, and a whole-operation deadline could only live in the job's retry budget. Two consequences are deliberate:
a timed-out **status poll reports Running**, not Failed (no answer is not a failed render — reading it as
terminal would abandon a submitted, billed generation; this also aligns ComfyUI's poll with fal's existing
transport-failure treatment), and a timed-out **submit** says the request may still have been enqueued.

**Review round 1 found one Important defect in the above, now fixed.** Mapping a timed-out submit to `Failed`
was correct as far as it went, but `GenerationRouter.SubmitAsync` advances to the next candidate on exactly that
status — so the detail string said "the request may still have been enqueued" and the router then enqueued it
somewhere else, buying the same render twice, and benched the first backend on a single timeout. `Failed` cannot
express the difference between *the queue answered "no"* (retry elsewhere, free) and *the queue never answered*
(may already be billable). So `GenerationOperation` gained an additive **`Inconclusive`** flag: the status stays
`Failed`, every existing status check behaves exactly as before, and only the router opts in — it SURFACES an
inconclusive submission carrying the provider id (so the caller learns who might hold it) instead of advancing,
and skips `RecordFailure`, since no answer is no evidence of ill health. `GenerationRenderJobHandler` fails such
a job with the backend NAMED and states it is deliberately not retried, mirroring the lost-lease path's
manual-recovery message. Pinned by mutation again: stubbing the router's guard to `false` fails exactly the two
new router tests. This is the same reasoning already applied to polls, applied to the call that commits money.

Three minor review items landed in the same commit: the `Timeout.InfiniteTimeSpan` docs said "opts out
entirely" when a positive `GenerationRequest.TimeoutSeconds` still re-imposes a deadline through `Resolve` (the
precedence is right, the sentence was not); the BYO-`HttpClient.Timeout` claim rested on reasoning and now has a
test; and `OperationCanceledException.CancellationToken` carries the LINKED token, not the caller's, so a
consumer filtering on `e.CancellationToken == myToken` will not match — noted where the discriminator is
explained.

**Round 2 closed the same bug on the one path with no human in it.** `GenerationSubmitTool` returned the
operation detail alone and dropped the provider id — so the caller most likely to re-submit (a model, whose
default reaction to a tool error is to call the tool again) was handed "may still have been enqueued" without
the backend name or any instruction, walking straight around the router's refusal to try a second candidate. The
observation now names the backend, forbids the retry and says why, points at `generate_status` instead, and
carries an `inconclusive` flag so a host can branch without parsing prose. `RecordSubmission` likewise tags such
a submit `error.type = "Inconclusive"` rather than `"Failed"`: same status, different incident, and an operator
chasing a possible double charge needs something to search on.

Public surface additive only — four `Timeout : TimeSpan` lines plus `GenerationOperation.Inconclusive`, nothing
removed or re-signed. 16 tests in `tests/Lyntai.Tests/Generation/GenerationTimeoutTests.cs`; `verify` green
(build · warnings · packages · bundle · 1470 tests · e2e 3/3 · leak scan).

## Part 35 — the 2.0.1 release hardening + a packaging policy with gates (2026-08-04)

_Not a planned backlog item: this came out of the owner asking, before cutting 2.0.1, whether the library was
"good enough for a production grade library" — and then, as the package count grew, how bundling should be
decided at all. Recorded here because the answers became load-bearing rules (D32–D34) and four build gates._

- [x] **A pre-release audit of the shipped artifact, not the repo.**
- [x] **A bundle membership policy (D32) + a dependency-budget gate.**
- [x] **Granularity settled (D33) + an inventory gate + a package scaffolder.**
- [x] **The media backends split out (D34) and generation marked EXPERIMENTAL.**

✅ done 2026-08-04 — **Outcome:** the audit found six real defects, and the most serious was self-inflicted:
`Lyntai.Providers.Default` stamped `IsTrimmable` into its assembly — a promise to a consumer's trimmer — while
three generation backends built request bodies by reflection-serializing anonymous types. The warnings had been
there all along; nothing failed on them. Also fixed: docs pointing consumers at three package ids the
restructure had deleted (an install line that cannot restore), `GuardedStream.ReadAll` silently dropping
`WithCancellation` on a public async iterator, an empty symbol package on the new bundle, an unconfigured image
backend reporting `AuthFailed` (so the new cooldown benched it) instead of `NotConfigured`, and two dead package
pins left by the ASP.NET removal.

The systemic half mattered more than any single fix. **`check-warnings`**: a warning in a published project now
fails `verify`, because an unfailed IL2026 is a false trim claim shipping to consumers. **`check-bundle`**: the
bundle's third-party closure cannot grow without a recorded decision (D32 — membership is a budget, since an
untrimmed publish copies the whole graph and analyses nothing; measured at 3.2 MB for an app that calls only
`AddLyntai`). **`check-packages`**: a package must appear in all nine registries, because the dangerous misses
are silent — no `ApiSurfaceTests` entry means no API gate at all (D33). **`consumer-smoke`**: packs every
package and then restores, builds and runs a fresh app against them, the only check that exercises what ships;
run by hand it found two of the six defects. Plus **`new-package`**, so the granularity D33 settles on is paid
for in tooling rather than in merging.

Then the media backends left `Lyntai.Providers.Default` for their own `Lyntai.Generation` package (D34), a split
justified by release CADENCE rather than dependency isolation — media is where the growth is, and every new
backend would otherwise churn the package every chat consumer installs. Their namespaces were corrected in the
same move (`Lyntai.Generation.Http` had contained a *subprocess* backend), which was only free because
generation had never shipped. And generation itself ships EXPERIMENTAL, exempt from the SemVer promise until
GEN-VERIFY closes — marked rather than pretended, since two of its backends have never been run against a real
service.

**Two lessons worth keeping, both about the tooling itself:** `check-packages` PASSED on its first run with two
registries deliberately broken (a presence check against a big file almost always passes), and `new-package`
reported "already present" for five registries it had never written (its guard tested the anchor line, not the
inserted line). Both were caught the same way — run the thing against a tree you broke on purpose and read the
output instead of trusting the exit code. `verify` green at 7 gates, 1337 tests; `consumer-smoke` green across
12 packages.

## Part 36 — generation ergonomics: the misbinding trap and the missing wiring (2026-08-04)

_Both items were filed by consumers rather than found here — GEN10 by an app writing an img2img adapter, the
wiring gap by the pre-2.0.1 consumer smoke. Taken together because they are the same complaint from two
directions: the generation domain was correct but unpleasant to reach._

- [x] **GEN10 — `GenerationInput`'s ctor order is a silent-misbinding trap; give `Role` a safer path.**
  Filed 2026-08-04 by a consuming app that hit it while writing an img2img adapter. Real signature:
  `GenerationInput(string MediaType, byte[]? Data = null, string? Uri = null, string? Role = null)` — three of
  the four slots are strings and `Role` is LAST, so a plausible positional call
  `new GenerationInput(GenerationInputRoles.Init, bytes, "image/png")` **compiles clean** and binds `"init"` to
  `MediaType`, the bytes to `Data`, `"image/png"` to `Uri`, and leaves `Role` **null**. Suggested, cheapest
  first: named factories, one per `GenerationInputRoles` constant. The same shape check was asked for
  `GenerationArtifact`.
- [x] **generation backend wiring helpers** — the new `Lyntai.Generation` package shipped five backends and no
  `Add*` methods, while the LLM side has `AddOllamaProvider()` / `AddOpenAiProvider()` /
  `AddAzureOpenAiProvider()`; every generation backend had to be hand-constructed WITH its `Func<HttpClient>`.

✅ done 2026-08-04 — **Outcome:** ten static factories on `GenerationInput` (`Init`/`FirstFrame`/`Reference`/
`Voice`, each with a bytes and a `System.Uri` overload, plus `From(role, …)` for a role a backend documents
itself), and five `Add*` shims covering every backend the package ships — the fifth, `AddLocalDiffusionProvider`,
takes `IProcessRunner` from DI rather than an `HttpClient`, since it spawns rather than calls. The URI overloads
take `System.Uri` rather than `string` on purpose: two adjacent strings would reintroduce the exact
transposition being fixed, and a wrong type is a compile error where a wrong string is a silent one. Reasoning
in `docs/DECISIONS.md` **D35**.

**`GenerationArtifact` was checked and deliberately left alone** — the task asked, so the answer is recorded
rather than left to a second round of guessing. Its `(MediaType, Data, Uri, Metadata)` layout has only one
same-typed pair, and transposing it requires explicitly passing `null` for the `byte[]` slot in between; there
is also no role-shaped silent degradation, since a wrong media type surfaces immediately and nothing branches on
it. Factories there would be surface that doesn't earn its keep.

**Writing the shims surfaced a real defect the task hadn't asked about** (`docs/DECISIONS.md` **D36**): every
HTTP generation backend did `using var http = httpFactory()` — it **disposed the client it was handed**. Correct
for a factory that MAKES a client per call, which is what the backends' own tests pass, and wrong for the natural
BYO lambda `_ => _myClient`: the first render succeeds and the **second** throws `ObjectDisposedException`. The
LLM side had always had this right (`disposeHttpClient: !byo`), so the fix states one rule for both domains — a
host-supplied client is never disposed by Lyntai; a client Lyntai created is. The four backends gained
`bool disposeHttpClient = true`, defaulted to the previous behaviour so no existing construction changed. It is
pinned by a test that renders TWICE, because a one-render test passes either way — which is precisely why the
defect survived the release.

Also public: `GenerationProviderBuilderExtensions.HttpClientName(id)`, so a host can decorate the same named
client the backend uses (a Polly policy, a logging handler) instead of abandoning the shim to hand-construct.
The options are passed as an OBJECT rather than an `Action<T>` configure callback — unlike the LLM presets —
because these options are records with `required`/`init` members: there is nothing to mutate after construction,
and passing the instance is what keeps `required BaseUrl` compiler-enforced instead of discovered at the first
render. `Lyntai.Generation` picked up `Microsoft.Extensions.Http`; it is not in the bundle, so the one-line
install's closure is unchanged (`check-bundle` green). 21 new tests; `verify` green at 7 gates, 1358 tests,
e2e 3/3.

**And the release gate itself turned out to be lying** — the third tooling defect this project has found by
pointing a check at something it should have failed on. `consumer-smoke` packs under the FIXED throwaway version
`9.9.9-smoke`, and NuGet never re-extracts a version already in its global cache: after the first run ever, every
later run restored that run's packages and reported success about code it had never compiled against. It
surfaced only because the smoke app was extended to call `GenerationInput.Init`, which the "restored" Core did
not contain — a package that demonstrably did contain it. The script now evicts
`<global-packages>/lyntai.*/<version>` after packing (**9** stale packages on the first eviction), which beats
isolating `NUGET_PACKAGES` because third-party dependencies stay cached and the smoke stays minutes. Recorded in
`.claude/knowledge/pitfalls.md`; the general shape is *a distinct version does not defeat a cache if the version
is a constant*. The smoke app also now references `Lyntai.Generation` and asserts on it — it is the package a
consumer is most likely to meet on its own (not in the bundle) and the only one carrying a dependency the bundle
never resolves, so a wrong dependency group in its nuspec would show up nowhere else.

## Part 37 — provider lifetime: a pool keyed on the configuration, for externally-owned settings (2026-08-05)

_Filed as GEN12 by a consuming app whose backend configuration is owned by its end users, reframed twice, and
finally built against a written design (`docs/superpowers/specs/2026-08-05-provider-pool-design.md`, approved
2026-08-05) rather than against the task text — because the task text turned out to be wrong about the
mechanism. Nine tasks, each committed separately; the reasoning is `docs/DECISIONS.md` **D37**._

- [x] **GEN12 — own the provider POOL: keep one instance per configuration and deprecate it when the
  configuration changes.** Raised 2026-08-04, reframed twice; this is the version to build against.

  _**Design of record: `docs/superpowers/specs/2026-08-05-provider-pool-design.md`** (approved 2026-08-05).
  It supersedes the surface sketched below in three ways worth knowing before reading further: the pool holds
  MANY live entries (several configurations of one backend run concurrently, from a source that keeps
  changing), pooling itself is a registered STRATEGY (`Bounded` / `Transient` / BYO) so a consumer wanting a
  fresh instance per call is served by the same call site, and a replaced entry is RETIRED rather than
  disposed — disposing it would abort renders still running on it. The spec also corrects this entry's
  premise: providers hold no cooldown state; `DeadHostTracker` does, owned by the router._ Earlier
  drafts asked for per-call options resolution and then for a documentation note. Both were treating the
  symptom — the underlying need is provider **lifecycle**, and it belongs in the library rather than in each
  consumer.

  **The situation.** Where configuration is owned by the deployment, `Add*` registration is right and nothing
  here applies. Where configuration is owned by an END USER, three things follow: the settings can change at
  any moment, the *choice of backend* is itself one of those settings, and (commonly) the process reading the
  configuration is not the one that wrote it — so the configuration must be resolved per call.

  **The trap that follows, which is the actual point.** "Resolve the config per call" quietly becomes
  "construct the provider per call", and that is wrong twice over. It allocates a backend per request, and
  more importantly it **discards everything a provider instance accumulates**. That was harmless when a
  provider was a thin wrapper; it stopped being harmless in 2.1.0, when the generation domain gained cooldown
  and dead-host tracking. A consumer that rebuilds per render can never bench a failing backend, because the
  instance holding that knowledge is thrown away before it can be consulted twice. The library added the
  state, so the library has the strongest interest in the instances being long-lived and correctly keyed.

  **What the pool must do** — small, and the semantics matter more than the surface:
  - **Reuse** while the resolved configuration is unchanged.
  - **Deprecate** the entry the moment ANY part of that configuration changes — including a credential
    rotation, which is easy to leave out of a key and means a pooled provider keeps using a revoked secret.
  - Be **thread-safe** (a UI request and a background job can arrive together).
  - Be explicitly **NOT routing.** Registering several backends so a router can choose has different
    semantics: routing falls back, whereas "the user chose this backend" must FAIL rather than silently
    succeed against a different one and bill that credential. A pool selects what the user chose; a router
    selects what is healthy.
  - Say what happens on eviction if a provider ever becomes `IDisposable` (today none are, so a dropped
    reference suffices — but a consumer cannot rely on that staying true without being told).

  **A key member that is easy to miss**, from building one: the key must include values the backend resolves
  at RUNTIME, not just the saved settings. A locally-provisioned engine's binary and model paths appear when a
  download completes — at which point the saved configuration has not changed at all, yet the pooled provider
  is holding empty paths and will keep failing. Keying only on "the config the user typed" looks correct and
  is not.

  **Suggested surface:** something a consumer can hand a key and a factory to
  (`IGenerationProviderPool.GetOrCreate(key, factory)`), usable WITHOUT a container so it serves the
  direct-construction path too; or, for container users, `Add*` overloads that resolve options per resolution
  and key the instance on the result. Either way the win is that consumers stop each writing the same cache
  and getting the key subtly wrong.

  _Reference implementation available: the reporting consumer now runs exactly this (config re-read per call,
  provider rebuilt only on change), with the reuse and every deprecation path sabotage-verified — disabling
  reuse fails the reuse test, ignoring the key fails all five deprecation tests. Happy to hand over the shape
  if useful._

✅ done 2026-08-05 — **Outcome:** `Lyntai.Lifecycle` in Core — `IProviderIdentity` (now the shared base of both
provider seams), `ProviderKey` + its named-contribution builder, `IProviderPool<TProvider>` with two shipped
strategies (`BoundedProviderPool` reuses within LRU + idle bounds, `TransientProviderPool` never reuses),
`ProviderAdmission`, `ProviderRegistration`, and `IGenerationRouterFactory` / `ILlmRouterFactory` composing a
governed router over a caller-chosen provider set; wired by `UseProviderPool` / `UseTransientProviders` /
`ConfigureProviderAdmission`. **The task's premise was corrected during design and that correction is the
reason the fix works:** providers hold no cooldown state at all — every backend is a constructor and immutable
fields — so pooling instances alone would have changed nothing. `DeadHostTracker` holds it and the **router**
owns it, and both routers snapshot their provider set at construction, so a consumer wanting a different
backend per call rebuilds the router and *that* is what destroys the cooldown. The unit that has to be
long-lived is the bookkeeping, which is why the deliverable is a router **factory** over a shared tracker as
much as it is a pool. Additive throughout: an app that configures its backends at startup resolves the same
routers over the same DI collection, keyed on the provider id exactly as before.

**Two decisions the task asked for, answered as entailments rather than preferences.** Retirement **never
disposes** — you cannot both refuse to break in-flight work and dispose deterministically without leases, and
a lease-based `IProviderPool` is therefore the documented escape hatch rather than an omission (pinned by a
disposable fake whose flag stays false through `Retire`, `RetireSlot` and both evictions). And cooldown and
admission are keyed on the **configuration**, not the provider id: two configurations of one backend under
different credentials must bench independently, while two consumers of one downed self-hosted host should
share a bench. Admission is applied by the **router**, never by wrapping a provider, because a wrapper
implementing only `IGenerationProvider` erases `IGenerationJobProvider` — every queued video render would stop
routing while every image render and every inline-only test stayed green.

**Three defects were found during execution that no plan could have anticipated, and all three were silent.**
(1) The plan mandated a `TryAddSingleton<DeadHostTracker>()` inside the generation `EnsureRouter`; that runs
during `configure(builder)`, which precedes the front-door registration that builds the tracker from
`LyntaiOptions` — so the `TryAdd` would have won and silently discarded the configured threshold, cooldown and
logger **for both domains**. Confirmed twice by mutation, and nothing in 1427 tests noticed; it was omitted and
a guard test now catches reintroduction. (2) Two registrations sharing a backend id in one factory call routed
silently to the first, leaving the second built, pooled and unreachable — now an `ArgumentException` naming the
slot. (3) `ProviderAdmission`'s gate table, specified as unbounded, would have accumulated one permanent
semaphore per configuration ever presented in a process-lifetime singleton; it now bounds itself by **calls in
flight** (a holder count per gate, removed at zero), which needs no cap and no timeout — the spec's §4.7 was
amended to match. Two more traps came from the *tooling*: `dotnet test --filter` reports success when it
matches zero tests (a filter named a class that does not exist), and `node devtools/dev.mjs test -- --filter`
does not forward the filter at all — two independent ways for a targeted run to verify nothing. All of it is
in `.claude/knowledge/pitfalls.md`.

`verify` green at 7 gates, 1451 tests, e2e 3/3, 0 warnings. Public surface: additive only (a base interface on
two existing interfaces, plus optional trailing constructor parameters on both routers — source-compatible,
**not** binary-compatible, called out in `CHANGELOG.md`).

---

## Part 34 — findings from the pre-2.0.1 consumer smoke (2026-08-04)

_Restoring the packed bundle into a fresh app and compiling against the 2.0.1 surface (rather than project
references) proved the install story works, and exposed two asymmetries between the domains. Neither blocks the
release; both are additive._

_The generation wiring helpers landed 2026-08-04 — see Part 36 above and `docs/DECISIONS.md` D35/D36. The
verdict half closed 2026-08-05, emptying the part._

- [x] **LLM-side parity for the no-credentials verdict** — `GenerationVerdictClassifier.FromHttpFailure(status,
  body, hasCredentials)` now reports a 401 with NO key supplied as `NotConfigured` rather than `AuthFailed`, so
  routing skips an unconfigured backend blamelessly instead of benching it. `OpenAiCompatibleProvider` /
  `HttpEmbedder` have the same shape and still report `AuthFailed`. Deliberately NOT changed here: that is
  released behaviour, and a verdict change belongs in its own considered commit, not a pre-release sweep.

  **Closed 2026-08-05. Outcome:** added `LlmVerdict.NotConfigured` (appended last — existing members keep their
  numeric values, so it is binary-compatible) mapped to `FallbackAction.Advance` in the default `RoutingPolicy`,
  plus `LlmVerdictClassifier.FromHttpFailure(status, body, hasCredentials)`; `OpenAiCompatibleProvider` now
  passes whether it carried a key, so a 401 to an unconfigured backend advances with no cooldown and no
  dead-host penalty while a REJECTED key still cools the host.

  Four findings worth keeping (the fourth came out of review, in a follow-up commit):

  - **The enum member was the whole question.** There was no `NotConfigured`-equivalent on the LLM side and no
    member that both meant the right thing and produced the right action — `Unsupported` maps to `Surface`
    (which would STOP the run at the unconfigured candidate, strictly worse than benching it) and
    `ContextWindowExceeded` has the right action but the wrong meaning. Escalated as a design call rather than
    invented; approved as an additive minor. The cost is CS8509 in a consumer's non-exhaustive `switch`
    expression — a warning in their build, called out in `CHANGELOG.md`.
  - **`RoutingPolicy.ActionFor` falls back to `PenalizeAndAdvance`**, so adding the member WITHOUT the policy
    entry would have produced a different wrong outcome (still counting toward the dead-host threshold) rather
    than a fix. The two must always move together — noted in the policy source.
  - **The rule is stated twice, deliberately, not shared.** `docs/DECISIONS.md` D30 keeps the pattern CORPUS
    single-sourced in `LlmVerdictClassifier` (there is one answer to "what does a 429 look like"), but this
    two-term promotion reaches different populations: every LLM backend that authenticates by login SESSION
    rather than a supplied key — the CLI dialects, the primary seam — classifies from error text and has no
    `hasCredentials` fact at all. A shared helper for `authFailure && !hasCredentials` would be indirection
    without drift protection, so each site states it and cross-references the other. `GenerationVerdictClassifier`
    also stopped flattening a `NotConfigured` from the shared corpus to `Failed` (reachable via a
    consumer-registered `AddErrorTextMatcher`).
  - **A blameless verdict could MASK a real one** (caught in review). `LlmRouter` remembered the last failure
    unconditionally, so `[downHost → Failed, neverConfigured → NotConfigured]` reported "not configured" and
    sent the caller to set up a key while the backend they HAD configured was down. `GenerationRouter` already
    guarded exactly this; introducing a blameless verdict is what made the LLM side need it. Blameless
    verdicts are now remembered separately on both routing paths and reported only when there was no real
    failure. Deliberately unchanged: WHICH substantive failure wins (this router keeps the last, generation
    the first), and the test is keyed on the verdict rather than on `FallbackAction.Advance` — that would
    also swallow `ContextWindowExceeded`, which is a real, actionable answer. **Generalisation: adding a
    verdict that advances without blame obliges you to check every "remember the failure" accumulator, not
    just the policy table.**

  **`HttpEmbedder` was deliberately left alone**, contrary to the task's premise: it reports no verdict at all,
  it THROWS (its type doc states that contract), and there is no embedder router — so there is no "advance
  without blame" for it to reach and nothing to route around. The only thing a host can act on is the wording,
  so its 401 now says `(not configured: no ApiKey)` when no key was supplied. Message only.

  `verify` green at 7 gates, 1486 tests, e2e 3/3, 0 warnings. Public surface: additive only. Reasoning
  recorded as `docs/DECISIONS.md` **D38**; the `Unsupported` translation gap found alongside is filed as
  Part 38 in `TASKS.md`, deliberately not fixed here.

---

## Part 25 — post-1.0 additive ergonomics from the 1.0 API review (2026-08-05)

_Additive / non-breaking items surfaced by the 1.0 adversarial API review + consumer-usage review (the
working record was `devtools/_review/*`; rejects + rationale are in `docs/DECISIONS.md` D21). Worked as one
pass because they were all small and all found by the same review. Shape decisions →
`docs/DECISIONS.md` **D39**. The non-additive remainder of the curated-memory item stays OPEN in `TASKS.md`._

- [x] **verdict helpers** — `reply.IsOk()` / `reply.IsRateLimited()` extension(s) to cut the 3-branch
  `LlmVerdict` pattern at call sites.
  ✅ done 2026-08-05 — Outcome: shipped as `LlmVerdictExtensions.IsOk()` / `IsTransient()` on the ENUM rather
  than as per-verdict methods on `LlmReply`. Two reasons, both about growth (D39): the enum grows —
  `NotConfigured` was appended after the freeze — so a helper per member would owe a public addition every
  time while leaving the newest verdict the only one without one, and five released types carry a verdict, so
  an extension on the enum is one definition instead of five. `IsTransient()` answers "may the SAME request
  succeed later?" (true for `Failed`/`Timeout`/`RateLimited`; false for everything terminal as sent, and for
  an unknown value, so an unrecognized verdict can never provoke a retry loop). Deliberately not derived from
  `RoutingPolicy` — that table answers what the ROUTER does and gives `RateLimited`/`AuthFailed` the same
  action, which is the one distinction that matters here. Pinned by
  `LlmVerdictExtensionsTests.Every_verdict_states_whether_it_is_transient`, which asserts the CLASSIFICATION
  rather than membership in a list, so a new verdict cannot be greened by appending a name — the D38 "the
  enum and the policy move together" obligation, now covering a third thing.
  **Review follow-up (same day):** `Failed` is also `FromErrorText`'s catch-all, so an unrecognized PERMANENT
  error reads transient. Reviewed and KEPT — `RoutingPolicy.Retry` already re-sends only for
  `Failed`/`Timeout`, so narrowing the predicate would have put it at odds with the router's own retry rule —
  with the false positive named in the XML doc and pinned by its own test, and the gate above strengthened
  from a membership check to a classification check in the same pass.

- [x] **`AddMcpTools` convenience overload** — `params ITool[]` and/or document the
  `await McpToolset.FromClientAsync` → `AddMcpTools` two-step as the intended shape.
  ✅ done 2026-08-05 — Outcome: both. `AddMcpTools(params ITool[])` sits beside the sequence overload and
  delegates to it (an array argument binds to the params overload — same behavior, existing call sites
  unaffected), and the sequence overload's doc now states WHY the two-step is intentional rather than
  incidental: connecting an MCP client is async and its lifetime outlives registration, so the app owns the
  client and Lyntai only adapts its tools. `McpBuilderExtensions` also gained the type summary naming it the
  INBOUND half of the MCP story (`Lyntai.Tools.Mcp.Hosting` being the outbound twin).

- [x] **agent-event contract** — `ClaudeToolCalls.FilePathOf` should also read `notebook_path`/`path`;
  consider a discoverable event-shape contract instead of anonymous objects apps reflect over.
  ✅ done 2026-08-05 — Outcome: **no code change; the item was stale on both halves.** `FilePathOf` already
  reads `file_path` → `notebook_path` → `path` in that order, with six tests, landed pre-1.0. The
  "discoverable event-shape contract" is already `AgentStreamEvent` — a sealed abstract record with eight
  concrete cases, yielded by both `IAgentSession.StreamAsync` and `IToolLoop.StreamAsync` — and Lyntai's
  public surface contains zero anonymous objects (the only `new { }` in the tree are Dapper parameter objects
  inside the storage adapters, which never leave the assembly). The reflection the review saw was
  consumer-side code over its OWN hand-parsed CLI JSON; the gap there is a missing ADAPTER, not a missing
  contract, and is already filed as Part 33's CLI11 (`CodexAgentSession`). Recorded in D39 so it is not
  re-proposed as new surface.

- [x] **curated-memory ergonomics** — a `Source`/metadata convenience accessor (apps unpack
  `metadata["source"]` by hand after CMEM6); reconsider the delete+re-add for immutable `kind`/`task`/`scope`.
  ✅ **accessor half** done 2026-08-05 — Outcome: `CuratedMemoryExtensions.MetadataValue(key)`, the null-safe
  read of a map that is null on every entry written without metadata (so `entry.Metadata!["source"]` both
  throws on a missing key and NREs on the common case). Deliberately generic: CMEM6 retired the purpose-built
  `Source`/`Title` COLUMNS into one arbitrary map so a new payload field needs no schema or API change, and a
  `Source()` accessor would re-privilege that name one layer up, where the storage layer can no longer see the
  decision. Conventional key names stay documentation. **Mutability half NOT done and left open in `TASKS.md`**:
  `kind` is already updatable (CMEM5), and making `taskKey`/`scope` updatable is a signature change on a
  released interface (not additive) with an unanswered semantics question attached — those fields are the
  DEDUP IDENTITY, so an in-place move can silently produce the duplicate `AddAsync(dedup: true)` promises not
  to. See D39.

- [x] **member/type XML docs** — `ExtensionsAiProvider` public ctor, `LyntaiChatClientExtensions` type
  summary, `ClaudeCliProvider` interface members, `AddMcpTools` intended-shape doc.
  ✅ done 2026-08-05 — Outcome: `ExtensionsAiProvider` gained `<param>` docs for all four constructor slots
  (notably that `id` is a LABEL for one configured client, not a vendor name, and that the client is BYO and
  never disposed here) plus `<inheritdoc/>` on the interface members and real summaries on
  `IsAvailable`/`SupportsToolCalls`, which are both unconditionally `true` for reasons worth stating.
  `LyntaiChatClientExtensions` gained a type summary saying which DIRECTION of the MEAI bridge it is.
  `ClaudeCliProvider`'s interface members were already documented (`<inheritdoc/>` + per-capability summaries);
  only its `ProviderId` const was bare, and now says how a candidate list uses it. No test — a documentation
  change has nothing to assert beyond the build gate that every `<see cref/>` resolves, which `verify` runs.

- [x] **async migration entry points** — `MigrateUpAsync(…, CancellationToken)` twins alongside the sync
  `MigrationRunnerService.MigrateUp` (SQLite + Postgres), for apps owning their schema under
  `SchemaMigration.None`.
  ✅ done 2026-08-05 — Outcome: shipped on both backends, deliberately **narrow and documented as such**
  (`docs/DECISIONS.md` **D40**). FluentMigrator's runner is synchronous and takes no token, so the honest
  promise is exactly two things: the migration runs INLINE on the calling thread (never `Task.Run` — that
  would occupy a pool thread for the whole migration *and* still be uncancellable, i.e. worse than the sync
  call), and the token is honoured before any work (a cancelled token leaves the SQLite file uncreated /
  never dials the Postgres connection string) and between feature passes, each of which the version table has
  already committed. Under the default `StorageFeature.All` there is one pass, so there it degenerates to
  "before starting" — the XML docs say so under explicit *what it can do* / *what it cannot do* headings, and
  the README repeats it where `SchemaMigration.None` is described. SQLite's twin is genuinely `async` (its
  pragma seed is real ADO.NET); Postgres has no await point and returns a completed task with faults funnelled
  through `Task.FromException`/`Task.FromCanceled` so a `Task`-returning method never throws synchronously.
  Seven tests in `AsyncMigrationTests` plus a Postgres idempotence leg; one pins the no-offload property so
  nobody "improves" it into a `Task.Run`.

- [x] **semantic-memory wiring helper** — a DI seam / `Use*` helper so an app enabling semantic recall
  doesn't hand-construct `SqliteCuratedMemoryStore` / `SqliteVectorStore` / `MigratingConnectionFactory` /
  `HttpEmbedder` (a consumer does this today). Those concrete types STAY public for 1.0.
  ✅ done 2026-08-05 — Outcome: shipped as `b.AddSemanticMemory(…)` in **Core**, not as a storage-package
  composite (`docs/DECISIONS.md` **D41**). Auditing the four named types showed each already had a builder
  call — `UseSqliteStorage` (curated store; migrating factory via `SchemaMigration.OnFirstUse`),
  `UseSqliteVectorStore`, and `AddOpenAiCompatibleEmbedder`, the last two post-dating the consumer code the
  review looked at — so the hand-construction was stale rather than unavoidable. The real defect was that
  semantic memory had no NAME: it was enabled purely as a side effect of an `IEmbedder` being registered, so
  forgetting one registered no `ISemanticMemory` at all and the prompt composer and chat orchestrator skipped
  semantic recall on every turn, silently. `AddSemanticMemory` states the intent and `AddLyntai` now throws at
  composition when no embedder reached the container. Overloads mirror `AddEmbeddings` (instance / factory /
  by type) plus a no-argument one for a host-supplied embedder; it constructs no embedder (BYO by design — a
  defaulted `HttpEmbedder` would point nowhere). Everything stays substitutable: the vector store and
  `ISemanticMemory` are still `TryAdd`-registered, the concrete stores stay public, and `AddEmbeddings` alone
  behaves exactly as before. A `UseSqliteSemanticMemory()` composite was REJECTED — a one-line alias, and the
  version that also covered the embedder would have forced adapter-to-adapter (`Lyntai.Storage.Sqlite` →
  `Lyntai.Providers.Default`). Six tests in `SemanticMemoryWiringTests`, including the persistent path end to
  end over a temp SQLite db with zero hand-construction. Found on the way: `lyntai_vector` ships under
  `StorageFeature.Governance`, so a subset omitting it registers the store over a missing table. First cut
  only documented that; **review corrected it to a guard** — documenting a silent late failure while throwing
  for its twin in the same commit is inconsistent by the change's own standard, and `UseSqliteStorage`'s doc
  already states the rule the helper was breaking (a disabled domain is unresolvable, and that is the startup
  signal). All five Governance-backed helpers now fail at wiring time naming the call and the feature
  (`UsePostgresVectorStore` exempt — it creates its own schema lazily), order-independently via sentinel
  descriptors, with five tests in `FeatureToggleTests` including the reverse call order and a narrow-but-valid
  subset that still works. See the D41 amendment.
  **Whole-branch review (2026-08-05) — the guard was too broad and is now scoped to schema OWNERSHIP.** It
  checked the feature flags only, so it also fired under `SchemaMigration.None` and over an app-supplied
  `IDbConnectionFactory`, where Lyntai runs no migrations and the feature set therefore decides nothing —
  a regression on a documented, previously-working path whose offered remedy (add `StorageFeature.Governance`)
  would have created no table anyway. `SqliteFeatureSelection`/`PostgresFeatureSelection` now carry a
  `LyntaiMigrates` flag alongside the features and the verification returns early when it is false. Both
  Postgres helpers also gained the theory cases the "all five check" claim had been asserting without
  covering, and the two no-fire directions are pinned in both call orders.

---

## Part 39 — `CodexAgentSession`: the agent-session shape is not claude-only (2026-08-05)

_Closes CLI11, filed 2026-08-04 by a consuming app that wanted to delete its hand-rolled codex integration
and could not. The reasoning — and specifically WHICH half was built and why the other is marked rather than
faked or withheld — is `docs/DECISIONS.md` **D42**. The measurement that remains is filed as **Part 41**
(CLI12/CLI13) in `TASKS.md` — renumbered from 39 on 2026-08-05, because THIS archive entry is Part 39 and the
two collided._

- [x] **CLI11 — a `CodexAgentSession`, so the agent-session shape isn't claude-only.** Filed 2026-08-04 by a
  consuming app that wanted to delete its hand-rolled codex integration and could not.

  **The gap.** `Lyntai.Providers.CodexCli.CodexCliProvider` gives `CompleteAsync`/`StreamAsync(LlmRequest)`
  plus the maintenance capabilities — everything a ROUTER needs. But a desktop chat UI needs the other shape:
  the streamed **tool steps** an agent takes, which is what `AgentStreamEvent` carries and what
  `ClaudeAgentSession.StreamAsync(AgentSessionOptions)` produces. `LlmChunk` is `{ Kind, Text, Usage,
  Verdict, Detail }` — there is nowhere for "the agent called tool X with these arguments" to go.

  So a consumer that shows tool activity can adopt the codex provider for probe/update/auth, but must keep
  hand-parsing `codex exec --json` for the chat path — which is precisely the bespoke provider handling
  Lyntai exists to remove, and which has already cost that app two real defects (a bare `error` line failing a
  turn that SUCCEEDED, and a missing `--skip-git-repo-check` that works in a dev git repo and breaks in a
  shipped bundle — both of which YOUR measured codex work got right and theirs did not).

  **Why this looks cheap now and wasn't before:** 2.0.1 extracted `CliProviderEngine` + `ICliProviderDialect`
  specifically so a second CLI could reuse the first's machinery, and `CodexCliDialect` already knows codex's
  JSONL vocabulary (`item.started`/`item.completed`, `turn.failed`, the terminal-event rule). The agent
  session is the same events mapped to `AgentStreamEvent` instead of `LlmChunk`.

  **Done when:** a consumer can drive codex through the same `IAgentSession` shape as claude — streamed text,
  tool steps and usage — and delete its own JSONL parsing. If the answer is "the agent-session shape stays
  claude-only by design", that is a fine outcome too; say so in `docs/DECISIONS.md` and the consumer will stop
  waiting and own its codex parsing deliberately rather than provisionally.

**Closed 2026-08-05 — built, as the honest subset.** The answer was neither "they correspond" nor
"claude-only by design": they correspond only PARTIALLY, and the split is measured-vs-unmeasured rather than
conceptual. `AgentStreamEvent` needed no new case and lost none, so nothing about the shape is claude-specific
— but the filing's premise that `CodexCliDialect` "already knows `item.started`/`item.completed`" was
half-right. `CodexJsonlParser` handles only `item.completed`, and only two item types (`agent_message`,
`error`); the measured capture ran a trivial `--oss` turn with **no tools**, so every tool-step shape — the
whole reason the agent-session shape exists — was unmeasured.

Shipped: `CodexAgentSession` + `CodexAgentOptions` + `AddCodexCliAgentSession()` in `Lyntai.Providers.Default`
(the layout rule's answer — a codex agent session drags no dependency the codex provider does not already
have, so it shares that package rather than earning one). Measured half → measured events (session id,
assistant text, final usage incl. both cache fields, the classified terminal, the non-terminal-`error` rule).
Inferred half → tool steps, mapped **shape-driven, not name-driven**: any unknown item type becomes a
`ToolCall`/`ToolResult` under codex's OWN item-type name carrying codex's OWN item object, nothing renamed or
normalised, and no `CodexToolCalls` helper (inventing one would mean guessing field names). Where
`item.started` is absent the `ToolCall` is synthesised from the completion, correlated by item id and never
duplicated — so the unmeasured detail costs fewer events, never wrong ones.
  **[Editorial pointer, added later — the claim in the previous sentence was RETRACTED the same day; see
  "Review round 1" below. It holds for payload, not for the tool arm's item KIND, which is reached by
  elimination and can fabricate a `ToolCall`. The original wording is kept as the record of what was
  believed at the time; the scoped claim is the one that survived.]**
  Not emitted, because codex has no
analogue: `UsageLive`, `SessionEnded.Subtype`, `UsageFinal.Model`, and token-level deltas. `ResumeToken` is
REFUSED without spawning (`LlmVerdict.Unsupported`) rather than guessed or ignored; `DisallowedTools` is
logged as unhonoured; `SystemPrompt` travels as a leading block of the prompt.

**Both of the consumer's defects are now structurally shared rather than re-implemented**, which is the
durable part: `CodexExecArgs` is the single source of the `exec` argv for BOTH codex paths (so
`--skip-git-repo-check` cannot be present on one and missing from the other — and the agent path runs in the
CALLER's directory, where "obviously a repo" is most tempting), and `CodexEnvelope` is the single source of
the envelope vocabulary, the usage fields and the failure-message read (so the non-terminal-`error` rule
cannot drift between the two readers). Both were **mutation-checked**: removing `--skip-git-repo-check` failed
4 tests across both paths, treating a bare `error` line as terminal failed 2, and dropping the synthesised
`ToolCall` failed 2. A terminal-dedup guard was added to the session while mutation-checking (the first
`SessionEnded` wins; a later one can never add a second ending).

Surface: additive only — `AddCodexCliAgentSession`, `CodexAgentSession`, `CodexAgentOptions`; baseline
reviewed and updated. Both `Add*CliAgentSession` extensions now ALSO register keyed by provider id, so an app
registering both no longer has the unkeyed resolve depend on registration order. Docs: `CHANGELOG.md`,
`README.md` (a codex subsection stating plainly what it cannot do), `DECISIONS.md` **D42**, `pitfalls.md`
(+3 entries: the agent path's cwd trap, two seams over one wire format, and how to map a format you have not
measured). Tests: 40, all labelled MEASURED or INFERRED in the source.

**Review round 1 (2026-08-05) — the safety CLAIM was overbroad and was scoped down.** Spec passed and both
defect fixes were verified genuinely shared, but the review found that "a wrong guess costs fewer events,
never wrong ones" is falsified by the reader's own inferred set: the tool arm is reached by ELIMINATION
against three names, one of which (`reasoning`) is itself a guess — so a renamed `reasoning`, a `todo_list`
plan update, or a renamed `agent_message` each produce a *fabricated* `ToolCall`, contradicting that type's
documented meaning rather than merely missing an event; and `IsFailedItem` returning `false` is a positive
claim of success, not "unknown". Nothing loses payload and all of it sits inside the region already marked
INFERRED, but the docs are what a consumer reads. The claim is now scoped everywhere it appeared
(`CodexAgentReader`, `CodexAgentSession`, README, `CHANGELOG.md`, D42) to what the code actually guarantees —
no payload invented or dropped, uncertainty confined to the tool-step half, **kind provisional / payload
reliable** — and CLI12 now names the four items to confirm first, worst-case first. Also from that round:
the public remarks no longer `<see cref>` an internal type; the no-terminal fallback distinguishes "printed
nothing" from "answered then never terminated" (two different bugs, two diagnostics, one new test); the
in-band double-terminal dedup (`turn.completed` then `turn.failed`) gained the test it was missing and was
mutation-checked (removing the guard fails it); and CLI13 gained a note that `IAgentSession` has no
capability query, so the resume refusal is discoverable only at turn time — a Core change, left as an
owner call.

---

## Part 38 — verdict-translation gaps found while closing Part 34 (2026-08-05)

_Found while adding `LlmVerdict.NotConfigured` (`docs/DECISIONS.md` D38). Not fixed there: each changes
RELEASED generation behaviour and deserves its own considered commit, exactly as the Part 34 verdict change
did — not a rider on an unrelated one._

- [x] **`GenerationVerdictClassifier.Translate` flattens `Unsupported` to `Failed`** —
  `src/Lyntai.Core/Generation/GenerationVerdictClassifier.cs:71`. `LlmVerdict.Unsupported` falls through the
  `_ =>` arm to `GenerationVerdict.Failed`, even though `GenerationVerdict.Unsupported` exists and means the
  same thing ("this backend cannot do THIS request — a capability gap, not a fault"). The method's own doc
  contradicted the code until 2026-08-05, naming `ContextWindowExceeded` as the only intended collapse; the
  doc now records the gap instead of hiding it. **What a consumer observes:** a capability gap arriving
  through the shared corpus — a consumer-registered `AddErrorTextMatcher` returning `Unsupported`, or an
  exception classified into it — is reported as a generic `Failed`, so `GenerationRoutingPolicy` gives it
  `PenalizeAndAdvance` (counts toward the dead-host threshold) instead of `Advance`, and repeated capability
  gaps bench a healthy backend. Same shape as the Part 34 masking bug, one translation layer along. Fix is
  one arm, but it changes a released verdict mapping: needs its own commit, a `CHANGELOG.md` entry, and a
  test that a translated `Unsupported` is not penalised.

  **Closed 2026-08-05. Outcome:** `LlmVerdict.Unsupported` now translates to `GenerationVerdict.Unsupported`,
  so a translated capability gap takes `Advance` and no longer counts toward the dead-host threshold — pinned
  by an end-to-end router test (a `DeadHostTracker(threshold: 1)` that would bench the backend after ONE
  penalised failure still has it in rotation on the second run). Reasoning: `docs/DECISIONS.md` **D43**.

  Three findings beyond the filed arm:

  - **The catch-all was hiding three members, not one**, and nothing distinguished them: `Failed` (right
    answer, wrong reason), `ContextWindowExceeded` (deliberate) and `Unsupported` (the defect). Every one of
    the nine `LlmVerdict` members now has its own arm, so the discard holds nothing but undefined numeric
    values. **No other arm was mis-mapped** — the `NotConfigured` pairing checked specifically was already
    correct (it landed with D38).
  - **The compiler cannot be the growth gate for an enum switch.** C# treats any switch over an enum as
    non-exhaustive (`(LlmVerdict)99` is legal), so removing the discard buys CS8509 on the code as it stands —
    and since `TreatWarningsAsErrors` is false, what fails is the `check-warnings` GATE, not the compiler.
    The gate is a test, `Every_llm_verdict_states_its_media_translation`, which demands a row naming a media
    verdict per member **and an arm**: `Translate` was split over an `internal TryTranslate` returning null for
    an unhandled member, because the discard's own answer was `Failed` and a new member registered as `Failed`
    would otherwise have passed on the discard alone. Mutation-checked — deleting the `ContextWindowExceeded`
    arm changes no observable value and now fails the gate. Third instance of the same mechanism
    (`Every_verdict_states_whether_it_is_transient`, D38's obligation on the routing policy).
  - **`ContextWindowExceeded` stays at `Failed` on purpose**, and the reason is now written down rather than
    assumed. `Unsupported` would describe and route it better, but `GenerationRouter` never reports a
    blameless verdict over a real failure (`:92`, `:117`), so as `Unsupported` the one actionable answer in the
    set ("your prompt is too long for this backend") would be swallowed when it was the only thing that went
    wrong. D38 resolved the analogous LLM-side question the same way. **The price is stated, not implied:**
    `Failed` is `PenalizeAndAdvance`, so repeated oversized prompts can still bench a healthy backend — the
    same harm this task fixed, one member along — and the LLM domain maps that verdict to `Advance`
    (`RoutingPolicy.cs:20`), so the two domains now disagree about it. The remedy is a ROUTER change, filed as
    Part 40; moving the arm on its own just swaps one cost for the other.
  - **A shared meaning is not a shared action.** `LlmVerdict.Unsupported` routes to `Surface`,
    `GenerationVerdict.Unsupported` to `Advance` — both deliberate, each right for its domain, but the
    translation therefore changes fallback semantics silently. Now stated on the method and in D43.

  **Which promise binds, decided rather than assumed:** the type ships in `Lyntai.Core`, which carries the
  FULL SemVer promise — the `Lyntai.Generation` experimental carve-out is package- and reason-scoped
  (unmeasured backends, the unimplemented stream seam) and does not cover it. The conservative reading was
  applied instead of claiming the exemption, and `CLAUDE.md`'s wording was tightened to say "package" so the
  next reader does not have to re-derive it. No public API member changed, so the `ApiSurfaceTests` baselines
  are untouched.

  **Called out as MAJOR-BUMP MATERIAL rather than shipped quietly in a minor** (review finding). D24 relaxes
  the version consequence for documented breaks, but its third bullet excludes exactly this shape — "silent
  behavior changes … or anything a consumer can't detect at compile time" — and a consumer whose `switch`
  catches `Failed` still compiles and simply stops matching. The fix is still right; the description is the
  thing that had to be honest. The entry sits under `## Unreleased`, which fixes no number, and says plainly
  that it is major-bump material so whoever cuts the release decides deliberately.

  **Known and accepted, recorded so it is not rediscovered:** blameless verdicts are excluded from
  `GenerationRouter`'s `firstFailure`, so a run where EVERY candidate reports `Unsupported` returns
  `NotConfigured` / "every capable backend reported it is not configured". Unchanged by this fix and reachable
  today from any backend returning `Unsupported` directly; correcting it means changing the router's reporting
  rule — now filed as Part 40 together with the `ContextWindowExceeded` half, since it is the same rule.

---

## Part 43 — the deferred behaviour cluster from the pre-2.2.0 review (opened and closed 2026-08-05)

_Opened by the whole-library review that preceded 2.2.0 and closed the same day, once **`docs/DECISIONS.md`
D44** removed the only thing blocking it. (The review's working notes lived under the gitignored
`devtools/_review/`, so they are not in a fresh clone by design — everything durable from that pass is here,
in `CHANGELOG.md`, and in D44.)_

**Why it existed at all, which is the part worth keeping.** The review produced these eleven fixes and every
one was verified, small, and obviously right — and all of them were held back, because D24's third bullet made
a behaviour change no consumer can detect at compile time major-bump material *unconditionally*. The backlog
entry was written to hold them together for a future major.

That turned out to be the wrong deferral, for a reason no single finding could show: **the bullet did not
decide cases.** D38 and D43 are the same shape of change and read it oppositely, in the same release. And the
cost was one-sided — the owner's own applications had not fully adopted the library, so there was not even a
first-party deployment depending on the old behaviour. D44 amended the bullet (keeping storage/migration
breaks major-bump material, unconditionally), and the cluster landed in one pass.

- [x] **GEN-DEDUP** — `GenerationRouter` deduplicates candidates on the resolved (backend, effective model)
  pair before the count `ExemptSoleCandidate` reads. Generalized `CandidateDedup` rather than copying it.
- [x] **GEN-SUBMIT-VERDICT** — the submit path classifies the backend's rejection and consults the routing
  policy, so a blameless rejection no longer takes a dead-host strike.
- [x] **GEN-SUBMIT-DETAIL** — a failed submission carries the first backend's own reason.
- [x] **ROUTER-ID-CASE** — `LlmRouter` matches candidate ids case-insensitively, like every other id lookup.
- [x] **CLI-STREAM-CEILING** — the streamed CLI path gained the absolute backstop; deliberately NOT extended
  to the agent sessions, whose long turns a wall clock would kill.
- [x] **CLI-EMPTY-CONTENT** — an empty content event is no longer "delivered content"; the invariant moved
  from the two shipped dialects into the engine.
- [x] **OLLAMA-ATTACHMENTS** — images ride `/api/chat`'s `images` array; a URL-only attachment is reported.
- [x] **OPENAI-LONG-FIELD** — a non-integral usage count reads as 0 instead of throwing mid-stream.
- [x] **CLAUDE-TERMINAL** — first terminal wins, matching the codex twin.
- [x] **SCORER-APPLIES** — `IScorer.Applies` reaches an LLM judge's own predicate.
- [x] **VECTOR-TIEBREAK** — `InMemoryVectorStore` top-k is deterministic on tied scores.
- [x] **JOB-PAUSED-CANCEL** — a paused job cancels in one call, across all three backends.
- [x] **A1111-SIZE** — a non-positive size hint falls back to the configured default.
- [x] Surface additions: `ChatResult.Usage`, `StructureScorer.FormatKey`, `JobSpec.DefaultMaxAttempts`,
  `FalQueueOptions.CancelSegment`.

**Outcome (2026-08-05):** all seventeen landed, none skipped, each with a failing test first. Tests
1567 → 1657; `verify` and `consumer-smoke` green.

**Which release carries it: NOT 2.2.0.** The work was finished before 2.2.0 was cut but had not been pushed,
and the release workflow runs against the pushed branch — so 2.2.0 shipped without it and it reships in the
next minor. See `docs/DECISIONS.md` **D46**. The dates above are the dates the work was done and are left
as-is. The API baselines gained exactly four lines and lost none —
checked, because a removal on a frozen surface is what that gate exists to catch. Every observable delta is
disclosed in `CHANGELOG.md` under **Changed — behaviour fixes that a consumer cannot detect at compile time**,
which is the whole price D44 charges for shipping them in a minor.

**Two deltas can change working behaviour rather than only fix broken behaviour**, and are called out in the
changelog for that reason: a media submission rejected on content policy now *surfaces* instead of being
re-submitted to the next queue (restore with `policy.On(GenerationVerdict.Refused, …Advance)`), and a
consumer who adapted to the Ollama attachment drop now really sends images.

---

## Parts 40, 42, 25 and CLI13 — the rest of the 2.2.0 sweep (closed 2026-08-05)

_Closed in one pass after Part 43, on the principle that a release should carry no known issue that is
solvable without an external dependency. Each of these had been open for a while, and **none was open because
it was hard** — three needed a DECISION the backlog deliberately refused to guess (recorded as
`docs/DECISIONS.md` **D45**), one was filed under a premise that D24 had already invalidated, and one was
waiting on a measurement that turned out to be free._

- [x] **Part 40 — a media verdict that is both BLAMELESS and REPORTABLE.** `GenerationRouter` now keeps a
  second reporting slot: `firstFailure ?? firstBlameless ?? <synthetic>`. D38's rule is untouched — a real
  failure still always wins — but when nothing substantive failed, the caller gets the backend's own words
  instead of a synthetic "every capable backend reported it is not configured". **Then** (order was
  load-bearing, and Part 40 said so) `GenerationVerdictClassifier` began mapping `ContextWindowExceeded` to
  `Unsupported`, so repeated oversized prompts stop benching a healthy backend. Doing the mapping first would
  merely have traded a benched-backend cost for a lost-reason cost.
  _A second-order catch worth recording: the classifier change would have silently DELETED the
  "— 'x' said: …" clause from failed submissions, because those rejections became blameless. `SubmitAsync`'s
  reporting was mirrored in the same pass so the fix did not introduce the regression it was fixing._
- [x] **Part 42 — the API-surface gate's blind spots.** The renderer now emits a method's **type parameters**,
  its **parameter names**, and each optional parameter's **actual default value**. Before, a generic overload
  and its non-generic sibling produced the identical baseline line — the baseline literally contained that
  line twice — so **deleting either was invisible to the gate**, which is the single thing the gate exists to
  prevent. A parameter rename (a source break for named-argument callers, which the README teaches) and a
  flipped default were invisible too. All eleven baselines were regenerated.
  _Verified as a no-op rather than trusted: a member-set audit across all eleven baselines showed nine
  **identical**, `Lyntai.Core` gaining only `LlmVerdictException`, and `Lyntai.Providers.Default` showing only
  the deliberate `ContextSize` → `OllamaContextSize` rename. A formatting change that silently dropped a
  member would have been the worst possible outcome here, so it was checked directly._
- [x] **Part 25 — curated-memory `taskKey`/`scope` can now move in place**, with the identity question
  settled: an update that would collide with another entry's `(kind, content, taskKey, scope)` **refuses**
  (D45(2)). All four identity fields closed together, including the `kind` hole that already existed. No
  schema change, no migration — existing columns only.
- [x] **Part 25 — `OpenAiCompatibleOptions.ContextSize` → `OllamaContextSize`.** Filed as
  "major-bump-or-never"; that premise was simply wrong — D24 has always allowed a documented compile-time
  break in a minor, and a rename is the friendliest kind (every caller gets a compile error naming the fix).
  Verified first that the option really is Ollama-native-only before renaming.
- [x] **Part 25 — `AsChatClient` no longer erases the verdict.** New `Lyntai.Llm.LlmVerdictException` in
  **Core** (D45(3)), deriving from `InvalidOperationException` so every existing `catch` keeps working, with
  the message text preserved verbatim because parsing `.Message` was until now the only way to recover the
  verdict.
- [x] **CLI13 — codex resume.** `CodexAgentSession` honours `ResumeToken` instead of refusing it. **The
  measurement was free all along:** `codex exec resume --help` on the real installed 0.146.0 is a `--help`
  flag, so it costs no turn — the very escape the refusal was waiting for. It reports
  `codex exec resume [OPTIONS] [SESSION_ID] [PROMPT]` with `-` reading stdin. `docs/DECISIONS.md` D42's
  refusal paragraph is marked superseded rather than deleted, because the reasoning is why replacing it with a
  *measurement* rather than a *guess* was the right sequence.
  _Also measured, and recorded because it is the repo's own trap: that probe printed correct help and exited
  **255**. A non-zero exit from a `--help` probe does not mean the subcommand is absent._

**Outcome (2026-08-05):** all seven closed, none skipped, each with a failing test first. Tests
1657 → 1697; `verify` green. Two compile-time breaks (`ICuratedMemoryStore.UpdateAsync`, the `ContextSize`
rename) and one additive type, all disclosed in `CHANGELOG.md`. **What is left in `TASKS.md` is now exactly
the set that needs a key, a vendor, a second backend, or a paid turn** — not effort.

---

## Part 45 — a measured `turn.failed` shape, and the exit-code precedence it exposed (2026-08-05)

_Filed as CLI15 under the open Part 41 and closed the same day. It takes its own Part number because Part 41
stays OPEN (CLI12 is still unmeasurable from here), and an open Part and an archived Part must never share
a number._

- [x] **CLI15 — a measured `turn.failed` shape, filed as EVIDENCE for the failure half of the mapping.**
  Filed by a consuming app on 2026-08-05 after its own hand-rolled codex reader crashed on it. No claim is
  made about this repository's reader — the consumer treats this library as a black box and has not read it.
  What is offered is the capture, because CLI12's premise is that the codex surface needs measuring and this
  is a piece of it that a no-tools run does not reach.

  **Measured** against the codex CLI on Windows, 2026-08-05, by driving `codex exec --json
  --skip-git-repo-check` against an account whose login had expired (a 401 — cheap to reproduce, needs no
  quota, and is a realistic owner state rather than a synthetic fault):

  ```jsonc
  {"type":"error","message":"Reconnecting... 2/5 (unexpected status 401 …)"}   // string
  {"type":"turn.failed","error":{"message":"unexpected status 401 …"}}          // OBJECT, nested message
  ```

  Two things in that pair are worth having on the record:
  1. **`turn.failed.error` is an OBJECT wrapping `message`, not a string** — the two error-ish events on one
     stream do not share a shape. Reading it as a string throws `InvalidOperationException`, which is *not* a
     `JsonException`, so a defensive `catch (JsonException)` around a per-line parse does not hold it. In the
     consumer that turned a recoverable "your codex login expired" into a killed turn reported as "is the CLI
     installed and on PATH?" — the wrong remedy for the actual problem.
  2. **A `turn.failed` is followed by a non-zero exit**, so a reader that reports both the terminal event and
     the exit/stderr emits the failure twice. The second one carried codex's ordinary stderr chatter
     (`"Reading prompt from stdin..."`), which reads as a second, unrelated failure underneath the real one.

  **Acceptance (two-sided, either is a complete answer):** the failure half of the mapping is confirmed
  against these shapes and the stub grows the `turn.failed` object form — *or* a note recording that this
  repository already handles both and the capture is only corroboration, which is equally useful to the
  consumer since it is the version they will adopt when CLI14 lands.

  _Quest from `Aurelia` · filed 2026-08-05 · **open**._

✅ done 2026-08-05 — **Answered on the first arm, because the second turned out to be false.** Three of the
four claims were already handled and are now pinned rather than assumed: `turn.failed.error` as an OBJECT is
read by `CodexEnvelope.FailureMessage` (nested `error.message`, then a flat `message`, then a generic line);
the bare `error` line stays non-terminal; and the double-report is prevented in `StreamAsync` (the in-band
`Failure` ends the stream before the runner's non-zero-exit exception surfaces) and in both agent sessions
(the `sawTerminal` guard). **The fourth found a real defect the consumer could not have seen from outside:**
`CliProviderEngine.CompleteAsync` returned on a non-zero exit *before* parsing stdout, so this exact pair
classified the stderr chatter — `Failed` / "exit 1: Reading prompt from stdin..." — and lost both the reason
and the `AuthFailed` verdict that benches the host. The engine now parses first and lets the backend's own
account win, with the exit code kept as context; a non-zero exit with no in-band failure is unchanged. Same
ordering `StatusAsync` always had, which is why it is recorded in `.claude/knowledge/pitfalls.md` as a
generalisation (two answer channels ⇒ decide precedence explicitly and pin it) rather than as one more CLI
quirk. `docs/FIXES.md` opens with the incident; `codex-stub.mjs` grew the measured `AUTH_ERROR_EXIT` shape
and stopped `process.exit()`-ing mid-write. Four tests (three red first), `verify` green.

---

## Part 44 — an agent session can only be given the app's own tools if the backend is claude (2026-08-05)

_Filed by a consuming app after adopting 2.3.0 and finding it still cannot delete its hand-rolled codex
provider. Everything else CLI11 promised landed and is better than the hand-rolled version — argv built once,
resume measured, `--skip-git-repo-check` no longer possible to omit from one path. This is the single seam
that remains._

- [x] **CLI14 — an `IAgentSession` has no way to be pointed at the app's own MCP servers unless it is
  `ClaudeAgentSession`.** `src/Lyntai.Core/Agents/AgentSessionOptions.cs`,
  `src/Lyntai.Providers.Default/Codex*`.

  **The need.** `CodexAgentSession`'s own docblock states the value of the abstraction: "a chat UI that shows
  tool activity can drive either backend through one `IAgentSession`". But an agent embedded in an app is
  usually there to act on *that app's* domain, and it reaches that domain through the app's own MCP tools.
  Today the only way to point a session at an app-hosted MCP server is `ClaudeAgentOptions.McpConfigPath`.
  `CodexAgentOptions` carries `SandboxMode` and nothing else. So the backends are interchangeable only for an
  agent that needs no app tools — arguably the case the abstraction is least often reached for.

  **Why this is general rather than one consumer's shape.** Both shipped backends already accept app-provided
  MCP servers natively, and both were measured to do so: claude through a `--mcp-config` JSON file, codex
  through repeatable `-c mcp_servers.<name>.command|args|env` overrides (the filing consumer drives codex that
  way in production today). The *vocabulary* differs; the *need* is identical — which is the shape a dialect
  layer normally absorbs, and `ICliProviderDialect`/`CodexCliDialect` already exist. The seam is there; the
  agent-session path just does not reach it.

  **A second gap in the same area, which is why reusing the existing type would not close this.**
  `Lyntai.Agents.McpEndpoint` is `(Url, AuthToken, ServerName)` — **HTTP only**. It cannot express a **stdio**
  server (command + args + env). Stdio is how an application ships its own tools as a child process without
  opening a localhost port or having to authenticate one, and it is the shape MCP's own reference servers
  ship in. That is also why the claude path here hand-writes its `--mcp-config` JSON instead of using a typed
  surface: the typed surface cannot say `command`.

  **Evidence.** The consumer's desktop app spawns its own MCP server as a stdio child (an exe plus
  environment) and passes codex three `-c mcp_servers.aurelia.*` overrides per turn. Adopting
  `CodexAgentSession` as shipped would spawn codex with **no MCP servers at all**, so the agent would lose
  every tool it exists to use — a silent capability loss, not a compile error. The claude path is unaffected
  only because it writes that JSON itself.

  **Acceptance — either is a complete answer:**
  - **Neutral.** `AgentSessionOptions` carries the app's MCP servers in a form that can express **stdio
    (command/args/env)** as well as HTTP, and each CLI dialect renders it in its own vocabulary (claude: a
    temp `--mcp-config` file; codex: repeated `-c`). Either `McpEndpoint` grows a stdio form or a sibling
    type appears beside it.
  - **Or per-backend, recorded.** A decision that agent-session MCP wiring stays backend-specific — in which
    case `CodexAgentOptions` needs its own seam. Codex's native shape is a list of config overrides, so an
    `IReadOnlyList<string> ConfigOverrides` rendered as repeated `-c` closes it, and is at least honest about
    being codex-shaped.

  **Why not route it through the ctor's `env` (the obvious workaround, which the consumer rejected).**
  Pointing `CODEX_HOME` at a temp directory holding a generated `config.toml` would carry the MCP servers —
  but that same directory holds codex's **auth**, so it would cost the owner their login. Under **D26** the
  host keeps credentials and the library never touches them; a workaround that trades an app's tools for the
  user's session is not one.

✅ done 2026-08-05 — **The NEUTRAL arm, at the owner's direction**, and a sibling type rather than growing
`McpEndpoint` (which describes the loopback host *Lyntai* stands up for in-proc `ITool`s and would then mean
two things). New in Core: `AgentMcpServer` (stdio: command/args/env · http: url/token, with `Stdio`/`Http`
factories), `McpTransport`, and `AgentSessionOptions.McpServers` — additive, three `ApiSurface` additions and
no removals. `ClaudeAgentArgs` renders an owner-only `--mcp-config` document deleted when the turn ends and
**kept alongside** a caller's own `McpConfigPath` (the flag takes a list); `CodexMcpConfig` renders repeated
`-c mcp_servers.<n>.…` TOML overrides passed THROUGH `CodexExecArgs`, so a resumed turn carries them
identically and none can land past the `-` where codex would read it as prompt text.
**Every backend detail was measured turn-free before it was written** — `codex mcp list`/`get` with the
overrides applied (both CLIs are installed here), and claude's document shape read back through
`claude mcp list`. Three decisions recorded in `docs/DECISIONS.md` **D47**: a bearer token never reaches argv
(codex takes only `bearer_token_env_var`, so the value goes to the child's environment); an unrenderable
entry REFUSES the turn rather than being dropped, because a dropped server is the silent capability loss this
whole item is about; and naming a server does not pre-approve its tools. 22 tests, mutation-checked; `verify`
green. Also fixed a stale README bullet found next door — it still said codex `ResumeToken` was refused,
which CLI13 changed.

---

## Part 46 — MEM1: a named memory-engine seam (2026-08-08)

_Design: `docs/superpowers/specs/2026-08-08-memory-engine-seam-design.md`. Plan:
`docs/superpowers/plans/2026-08-08-memory-engine-seam.md`. MEM2 (the graph engine) and MEM-TUNE remain
OPEN in `TASKS.md`._

- [x] **MEM1 — the memory engine seam (Spec A).** `IMemoryEngine` + `MemoryRef`/`MemoryWrite`/`MemoryQuery`/
  `MemoryItem`/`MemoryRecall`, the optional capabilities (`IExpandableMemory`, `ILinkableMemory`,
  `IForgettableMemory`), `IMemoryEngineFactory`, `CompositeMemoryEngine`, engines over the three existing
  stores, the fluent builder and the zero-config `AddMemory()`.

  **Outcome (2026-08-08):** shipped in six commits, 68 new tests, no new package, 24 new public types in
  `Lyntai.Core` with **zero removals** from the API baseline. The two named guards both hold: the composite
  forwards optional capabilities by routing on `MemoryRef.Engine` (pinned by an expand-through-a-composite
  test, the analogue of the generation-router regression), and registration uses plain `AddSingleton` rather
  than a `TryAdd` reached during `configure(builder)`. `verify` green.

  _Four things the code disagreed with the design about, all corrected rather than worked around:_
  1. **`MemoryRef.Id` cannot always come from the store.** `IMemoryStore` and `ISemanticMemory` both return
     `Task` from `RememberAsync`; only `ICuratedMemoryStore.AddAsync` returns an id. Engines over id-less
     stores key by a length-framed SHA-256 of `(taskKey, scope, content)` — which is how those stores define
     identity anyway, and which makes the reference a write returns equal to the one recall reports.
  2. **A one-member engine still needs the composite wrapper.** Returning the bare member named the engine
     after the member (`chat/lexical`, not `chat`) and made it unreachable by its registered name.
  3. **`UseCurated`'s label defaults to the catalog KIND**, not the literal `"curated"`. The design said
     "label defaults to the source kind" and the first implementation read that as the source *type*, so the
     design's own motivating example — `UseCurated("glossary")` beside `UseCurated("style")` — would have
     collided and thrown.
  4. **The plan's test filter was invalid.** A bare `--filter "~Name"` is not VSTest syntax; it needs
     `FullyQualifiedName~Name`. It errored rather than passing vacuously, but see the `pitfalls.md` entry —
     the vacuous-pass direction is the dangerous one.

---

## Part 47 — MEM2a: the graph memory engine on the InMemory backend (2026-08-08)

_Design: `docs/superpowers/specs/2026-08-08-graph-memory-engine-design.md`. Plan:
`docs/superpowers/plans/2026-08-08-graph-memory-engine.md`. **MEM2b** (SQLite + Postgres), **MEM2c** (agent
tools, similarity enrichment) and **MEM-TUNE** remain OPEN in `TASKS.md`._

- [x] **MEM2a — the decay policy, the graph store contract, the engine, and the InMemory backend.**

  **Outcome (2026-08-08):** shipped in four commits, 39 new tests, no new package, 11 new public types
  across `Lyntai.Core` and `Lyntai.Storage.InMemory` with **zero removals** from the API baselines.
  `verify` green on all seven gates. The engine satisfies MEM1's shared engine contract alongside the three
  store wrappers and the composite — 40 facts across 5 engines.

  _Spec B was split into three plans rather than one, because it is three independently shippable pieces
  and a single plan's first review gate would not have arrived until the end. Splitting also means the SQL
  shape in MEM2b is settled by a working reference implementation instead of guessed alongside one._

  _Three design corrections made before they could ship:_
  1. **`UseGraph` takes options BY VALUE, not an `Action<GraphMemoryOptions>`.** The record is init-only, so
     a configure callback cannot mutate it and would silently do nothing — the "documented option that
     isn't wired" failure `pitfalls.md` records.
  2. **Headline derivation does not split on sentences.** "The build gate is dev.mjs verify" cut at the
     first period reads "The build gate is dev." — confidently wrong, and worse than no memory. It cuts on
     a word boundary and marks the truncation; authoritative material never passes through derivation.
  3. **`HalfLifeOptions.MaxStability` closes a defect, not a gap in the options.** Unbounded
     `stability *= 1 + Reinforce` compounds, so ~20 recalls turn a 7-day half-life into 64 years and a hot
     ASSOCIATIVE node silently acquires authoritative durability with none of its guarantees.

  _**Amended the same day — connectedness feeds decay, and edges decay too.** The first cut had the graph
  affecting only REACHABILITY: a node recalled once and connected to twenty things decayed exactly like one
  connected to nothing, which is backwards from the whole reason edges exist. The mirror defect surfaced
  at the same time — edges only ever GREW, so over a long run every pair that had co-occurred stayed
  linked at a rising weight and spreading stopped discriminating. Fixing only the first half would have
  been worse than neither: stale links would prop memories up forever. Both are now read-time, so there is
  still no sweeper. Three things worth keeping:_
  - _**`MaxConnectionBoost` exists for correctness, not tidiness.** A store filters candidates against the
    STORED stability, so `CandidateCutoff` widens by exactly that factor. An unbounded boost has no valid
    finite cutoff, and a well-connected node would be excluded while still perfectly retrievable — silently
    losing exactly what connectedness was meant to protect._
  - _**The effective-stability clamp is `max(stability, min(stability × boost, MaxStability))`.** A bare
    `min` would SHORTEN the half-life of an entry whose stored stability already exceeds the ceiling,
    lowering retrievability and breaking the superset guarantee. Caught by reasoning, then pinned by the
    contract's strength sweep._
  - _**`Strength` is a raw `SUM` decayed once by `MAX(strengthened_at)`** — an over-estimate, deliberately.
    Decaying each edge inside the aggregate needs a per-edge exponent no backend does portably, and
    over-estimating raises `r`, which is the only direction that keeps the cutoff conservative._

  _One test had to be corrected rather than the code: a hub asserted to outlive an isolated node at 60 days
  actually lands at r=0.049 against a 0.05 floor, because edge decay has eroded the boost by then. The
  window connectedness buys is FINITE on purpose; the test now uses 45 days and says so._

  _One thing left as-is and recorded rather than smoothed over: `GraphMemoryEngine.ForgetAsync` is a
  concrete-type convenience, not an interface member — `IForgettableMemory` declares only `PruneAsync`, and
  adding to an interface MEM1 already shipped is a break that needs its own decision. Promote it when a
  caller actually needs it through the interface._

---

## Part 48 — MEM2b: graph memory on SQLite and Postgres (2026-08-08)

_Plan: `docs/superpowers/plans/2026-08-08-graph-memory-sql-backends.md`. **MEM2c** and **MEM-TUNE** remain
OPEN in `TASKS.md`._

- [x] **MEM2b — `IMemoryGraphStore` for SQLite and Postgres**, both held to the `MemoryGraphStoreContract`
  MEM2a wrote. Two migrations (one per backend, same number), two hand-written stores, no shared SQL.

  **Outcome (2026-08-08):** `verify` green on all seven gates, plus `consumer-smoke` (12 packages restore,
  compile and run for a fresh consumer). **Postgres was genuinely verified, not shipped unmeasured** — 91
  tests against a live container, zero skipped.

  _Three findings, each of which would have failed silently:_
  1. **The grade encoding was inverted.** The SQL was written as `grade = 1` for authoritative, following
     the spec's own schema comment. `MemoryGrade` is `Inherit=0, Associative=1, Authoritative=2`, so the
     predicate meant the OPPOSITE: stale associative nodes bypassed the cutoff and exact facts were
     excluded by it. Now bound as a parameter derived from the enum; the spec comment is corrected. The
     InMemory backend never hit this because it holds the enum directly — the SQL backends are the first
     place a numeric encoding exists.
  2. **A gap in the contract itself:** nothing asserted a FRESH node SURVIVES a cutoff. Had `julianday`
     failed to parse the stored timestamp it would return NULL, excluding every row — and every existing
     cutoff fact would still have passed. `The_candidate_cutoff_keeps_fresh_associative_nodes` closes it,
     on every backend. The `MAX`/`GREATEST` divide-by-zero guard exists for the same reason.
  3. **`Relevance` is rank-derived on both SQL backends.** `bm25()` is unbounded and negative, so rather
     than invent a normalization each store reports a monotone transform of its own ordering, which is all
     the engine's rank multiplication needs and keeps the contractual 0..1.

  _**Migration numbering changed to `yyyyMMddHHmm` (owner's call, same day).** The generator implemented the
  documented `YYYYMMDDNNNN`, so this was a convention change, not a fix. Done immediately because it was
  free: a number that has been applied is recorded in `lyntai_version_info`, so renumbering a SHIPPED
  migration re-runs it against a database that already has its tables. `dev.mjs`, `storage.md`,
  `extending-lyntai.md`, the `add-migration` skill and design §7 all updated; the nine baseline migrations
  keep their original numbers, which sort below the new form._

  _Seven guard tests had to move deliberately — migration counts, migration-id lists, and both golden
  schema snapshots. All were pure additions; the goldens carry no removals._

---

## Part 49 — MEM2c: the agent-facing half of graph memory (2026-08-08)

_**MEM-TUNE** remains OPEN in `TASKS.md` — it is the only piece of the memory work left._

- [x] **MEM2c — per-engine agent tools, and similarity enrichment.**

  **Outcome (2026-08-08):** `verify` green on all seven gates. 13 new tests. The tools are ordinary
  `ITool`s, so they reach the tool loop and the MCP bridge with no extra wiring.

  _Four things worth keeping:_
  1. **`MemoryToolScope` exists because a singleton tool cannot bind a per-conversation task.**
     Registration binds a default; `Use(taskKey)` overrides it for the current async flow, backed by
     `AsyncLocal` so concurrent turns cannot read each other's scope. The alternative was "write your own
     `ITool`", which is the responsibility-shifting this design has avoided throughout.
  2. **`content` is always present in the tool's JSON, null when withheld.** Omitting the key would be
     cheaper and silent; the explicit null is what tells the model there is more text to fetch, which is
     the affordance that makes it call expand at all.
  3. **`MemorySources.Similarity` reports CONFIGURATION, not contribution** — unlike every sibling flag,
     and the enum now says so. Enrichment is a write-side tier: by the time a recall traverses them, its
     edges are indistinguishable from co-activation's. What the flag still buys is the distinction the enum
     exists for — "nothing similar was found" versus "similarity is not configured here".
  4. **`MinSimilarity` has a floor for a reason.** Without one a new entry links to its `SimilarityK`
     nearest neighbours however unrelated they are, which in a young graph means linking to nearly
     everything. Pinned by a test that raises the floor and asserts an unrelated entry stays unlinked.

  _Enrichment is best-effort by design (`model-decoupling`: a failure in the model half must not fail the
  whole). A broken embedding endpoint costs an entry some links, never the entry — pinned by a throwing
  embedder._

  _**A process slip, recorded rather than hidden:** the tools were committed after their own tests and
  `check-warnings` but BEFORE the API-surface gate, so that intermediate commit was not `verify`-green. It
  was caught and fixed in the next commit — which also found that `MemoryTools` was rendering as a public
  type with zero public members. Made internal (tests reach it via the existing `InternalsVisibleTo`),
  which is exactly the "make it internal before the release, not after" rule in `library-api-design`._

---

## Part 50 — decay is measured in events, not wall-clock time (2026-08-08)

_Owner-driven, mid-MEM2c: "since we are building the logic but not 100% same as human, the day might be
wrong term". It was — and the objection went deeper than the unit. **MEM-TUNE remains OPEN** and is now
the only memory work left._

- [x] **Replace the wall-clock decay dimension with a logical position, and damp bursts.**

  **Outcome (2026-08-08):** `verify` green on all seven gates; 199 memory tests; Postgres re-verified live
  (91 tests, zero skipped). Refactor across the policy, the node records, the store contract and all three
  backends.

  **The defect.** The model was borrowed wholesale from human memory research, where decay is measured in
  real time because that is what a person experiences between encounters. An agent does not work that way:
  one eight-hour session with 500 writes decayed almost nothing, while two three-write sessions a month
  apart decayed everything. The second experienced almost nothing and forgot everything. Backwards, and
  exactly the burst-shaped usage this library targets.

  **The model is interference** — a trace fades because newer material competes, not because seconds
  elapsed. Each engine keeps a monotone position advanced by writes; age is a subtraction. **The property
  this buys is the point:** a rarely-used memory decays slowly and a busy one decays fast, automatically,
  and a quiet engine is not aged by a busy sibling because the position is per engine.

  _Four things worth keeping:_
  1. **What the position COUNTS is a seam, not a decision.** "How much has happened" is ambiguous —
     writes, volume, real time — so `IMemoryClock` supplies it, with four shipped implementations. A
     project engine can decay on the calendar while a chat engine decays by volume, in one application.
  2. **`BurstDampenedClock` fixes a catastrophic-forgetting bug, not a rough edge.** Linear advance means a
     500-item ingest ages every prior entry by 500 and erases everything known before it. Damped, a burst
     advances by about `ln n` and is itself weakly encoded — which is why a person can read a book without
     forgetting their own name and still not recall most of the book.
  3. **All date arithmetic left the SQL.** `julianday` / `EXTRACT(EPOCH …)` are gone, taking with them the
     hazard where an unparseable timestamp yields NULL and silently excludes every row. Timestamps remain
     only for `PruneAsync(olderThan:)` and auditing — the one genuinely calendar concern.
  4. **`TimeSpan` left the options.** It asserted wall-clock in the type signature, which is a dimension
     the application had not chosen yet.

  _Two test calibrations, and one property they exposed: **prune under-reaps by design.** It reaps by the
  policy's candidate cutoff, which is a conservative superset widened by the connection-boost ceiling, so
  an entry can be below the recall floor and still not be deleted. That is the right direction for a
  destructive operation, and it is now stated in the test rather than left to be rediscovered._

  _The migration was EDITED IN PLACE rather than superseded — it is unreleased and has never been applied
  outside test databases, so there was nothing to preserve. Editing an applied migration would be the
  opposite call entirely._

---

## Part 51 — MEM-TUNE: the decay constants, measured (2026-08-08)

_Closes the memory sequence. `TASKS.md` Part 46 is now empty of memory work._

- [x] **MEM-TUNE — measure the decay defaults, don't ship them as if tuned.** _(Original wording preserved
  below; an archive is a record.)_ "Five constants are guesses … Close it with `MemoryDecaySimulation`: a
  corpus with a KNOWN reuse/noise split, driven over simulated weeks against an injected clock, asserting
  ≥90% of the reused set still above `MinRetrievability` at week 8, ≤10% of the noise, full rank
  separation, and all of it still true at week 16 so the numbers aren't fitted to one point."

  **Outcome (2026-08-08):** `MemoryDecaySimulationTests`, six facts, `verify` green on all seven gates.

  _**The criteria had to change with the dimension.** "Week 8" was calendar language written before Part 50
  replaced wall-clock decay with interference; the runs are now measured in rounds of writes. Two of the
  assertions also became sharper in the rewrite:_
  - _**Decay is measured over the FIRST HALF of the run only.** Material written moments ago should still
    be recallable — it is recent, and asserting otherwise would have been asserting a bug. What must fade
    is what was mentioned once, long ago._
  - _**Burst survival gained a control.** A 500-item ingest must not erase prior memory, AND the same
    ingest undamped must erase all of it. Without the control, a regression that silently disabled damping
    would pass the first assertion until the corpus happened to grow._

  _**What this closes, precisely** — and the XML docs now say exactly this rather than dropping the
  caveat: it measures the DYNAMICS and runs in CI, which a production corpus cannot. It does NOT establish
  that real usage has the reuse-to-noise ratio modelled. The constants move from "guess" to "measured
  against a stated model" — a starting point, not a tuned value. Replacing the model with a real corpus is
  a strict improvement, not a prerequisite. Same shape as GEN-VERIFY._

---

## Part 52 — decay buries a memory, it does not cut it (2026-08-08)

_Owner-driven, after the merge: "we don't really cut a memory, we decay it so it can be traced back, just
buried under other links of nodes."_

- [x] **Replace the absolute recall floor with a relative one.**

  **Outcome (2026-08-08):** `verify` green on all eight gates; 204 memory tests; Postgres re-verified live
  (91 tests, zero skipped).

  **The gap.** The docs said "decay only ranks, it never deletes" — true of storage, false of experience.
  Recall applied an ABSOLUTE floor, so an entry below `MinRetrievability` vanished from recall *and* from
  spreading, and the store's candidate query excluded it before ranking could even see it. It survived only
  via `ExpandAsync` with a reference you already held, which is not "traceable" in any useful sense. A test
  asserted the behaviour outright: `Assert.Empty(recall.Items)`. A cliff wearing the word gradient.

  **The model now.** An entry is hidden because something OUTRANKS it: recall ranks everything and drops
  only what falls more than `RelativeFloor` (default 0.02, so ~50× weaker than the best hit) below the
  strongest. A faint memory alone in a quiet engine is still the best thing there and surfaces; under fifty
  fresher ones it does not. `MinRetrievability` now governs `PruneAsync` alone — deleting is the only thing
  that removes a memory, and it is always explicit.

  _**Seeding lost the ability to exclude for faintness entirely**, rather than keeping the parameter and
  passing null. That capability existing is what let the mistake happen; the only bound left is the
  candidate count._

  _**Four tests had encoded the old semantics and had to be reformulated, not patched** — which is the
  clearest evidence the change was real:_
  - _`..._falls_below_the_floor` → a faint memory ALONE still surfaces, plus a sibling proving it is buried
    once something stronger exists, plus one proving it is still reachable by reference._
  - _`A_connected_memory_outlives_an_isolated_one` → `..._outranks_...`. Connectedness determines RANK, not
    existence, now that nothing is cut._
  - _The simulation's "old noise is gone" measured a TARGETED recall, which under burial always returns the
    entry. It now measures absence from a BROAD recall the entry has to compete in — which is the only
    place burial is observable — with a companion asserting the buried item is still there when asked for
    directly._
  - _Burst survival stopped measuring presence and started measuring RETRIEVABILITY: 500 fresher
    paragraphs legitimately outrank everything in a top-30, and that is not forgetting. Damped, prior
    material holds r > 0.25; undamped, r < 0.001. The control still proves the damping does the work._

---

## Notes for the implementer

- **TDD, every task:** failing test → run it fail → minimal impl → run it pass → commit. The acceptance
  lines are your definition of done per phase.
- **Deviate from the plan when the code disagrees with it** — the spec's *contract* (interfaces,
  semantics) is authoritative; this file's task ordering is a suggestion. Record real deviations in the
  commit message.
- **Ask before committing** if running non-autonomously. Never `--no-verify` past the sensitive-info hook
  without cause.
- The provider-stub (`devtools/scripts/provider-stub.mjs`) is the seam that keeps every provider/e2e test
  free of real tokens — extend its prompt-marker behavior as new tests need deterministic outputs.
