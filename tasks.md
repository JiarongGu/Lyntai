# Lyntai (灵台) — Implementation Plan / Task Backlog

> **Status: phases 0–7 implemented, then roadmap v0.3–v0.27.2 landed on top** (agentic tool-calling,
> durable jobs, guards, secrets, semantic memory, three storage backends, governance decorators, …).
> See `CHANGELOG.md` for per-release detail and `docs/ROADMAP.md` for the forward sequence.
> **➡ Active work: the "Review follow-up (2026-07-18)" section at the bottom of this file** — confirmed
> defects + Sonora-adoption gaps from an independent review of v0.27.2. Build clean · 571 tests · e2e
> green · leak scan clean.

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

**Conventions (mirror the family — see `.claude/rules/dev-conventions.md`):** modules = interface in Core
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

- [ ] **T4 · Streaming `finish_reason=tool_calls` emits a spurious `Refused` after content**
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

- [ ] **T5 · Memory recall matches "any token" (SQLite FTS) vs "contiguous phrase" (Postgres/InMemory)**
  - Files: `src/Lyntai.Core/Storage/FtsQuery.cs`, `SqliteMemoryStore.cs`, `PostgresMemoryStore.cs`,
    `InMemoryMemoryStore.cs`. A multi-word query → SQLite `"a" OR "b"` (either-token hits) while Postgres
    `ILIKE %a b%` / InMemory `Contains("a b")` match only the contiguous substring. Same query, different results.
  - Fix (pick one, document it): AND-join FTS tokens for phrase parity, OR document the per-backend recall/ranking
    difference on `IMemoryStore` XML doc + `.claude/knowledge/storage.md`. Test: a shared cross-backend two-word
    recall test asserting the documented behavior.

- [ ] **T6 · Usage-budget consumer key: InMemory case-insensitive vs SQL case-sensitive**
  - Files: `InMemoryUsageTracker.cs` (keys `OrdinalIgnoreCase`), `SqliteUsageTracker.cs`, `PostgresUsageTracker.cs`
    (case-sensitive PK). Budget totals diverge by backend for `App` vs `app`. Fix: converge — cheapest is
    `StringComparer.Ordinal` in the in-memory tracker. Test: `Record("App")` + `Record("app")` → consistent totals
    across every backend (parametrized).

- [ ] **T7 · pgvector throws on a dimension-mismatched row; semantic recall isn't fail-open**
  - Files: `PostgresVectorStore.cs` (`SearchAsync`), `src/Lyntai.Core/Memory/SemanticMemory.cs` (`RecallAsync`).
    pgvector's `<=>` errors on a differing-dimension row (InMemory/SQLite score 0), and `RecallAsync` has no
    try/catch → all semantic recall breaks on Postgres after a model swap. Fix: make `RecallAsync` fail-open
    (catch/log/return `[]`, rethrow only OCE); document reindex-on-model-change on `IEmbedder`/`ISemanticMemory`.
    Test: a wrong-dimension row → recall returns `[]`, doesn't throw.

### Lower (risk / nit)

- [ ] **T8 · Router treats a provider's own `OperationCanceledException` as caller-cancel** —
  `src/Lyntai.Core/Llm/Routing/LlmRouter.cs` (streaming catch): narrow to
  `when (ex is not OperationCanceledException || !ct.IsCancellationRequested)` so only the caller's cancel aborts;
  a provider-side OCE becomes a fall-over-able Error chunk (the router is the trust boundary). Test: a fake
  provider throwing a bare OCE (ct not cancelled) pre-content → falls over to the next candidate.
- [ ] **T9 · Budget cap not atomic under concurrency** — `BudgetedLlmClient.cs`: check-then-act lets N concurrent
  calls all pass the cap (overshoot = in-flight N, not "one past"). Reserve-then-reconcile, or tighten the doc to
  state the overshoot bound.
- [ ] **T10 · Scheduler enqueues before persisting the advance** — `JobScheduler.cs`: a crash between enqueue and
  `SetNextAsync` re-runs the slot. Persist-then-enqueue, or document the at-least-once semantics on `IJobScheduler`.
- [ ] **T11 · InMemory job-claim tiebreaker diverges from SQL** — `InMemoryJobStore.cs`: add `.ThenBy(j => j.Id)`
  after `AvailableAt` to match SQLite/Postgres `…, id` (deterministic same-tick same-priority order).
- [ ] **T12 · Access-gate constant-time compare guidance** — `src/Lyntai.Core/Secrets/ISecretVault.cs`: XML-doc
  warning that any token/secret equality inside an `ISecretAccessPolicy` must use
  `CryptographicOperations.FixedTimeEquals`. Doc-only.
- [ ] **T13 · Stale changelog** — `CHANGELOG.md` v0.27.1 "Known edge cases" still says the in-memory store throws
  on a dimension mismatch; v0.27.2 changed it to score 0. Correct the note.

## Part 2 — Sonora-adoption gaps (features Lyntai lacks)

Sonora (the sibling Sonora repo) can adopt Lyntai for its LLM client, providers, CLI spawn, structured
output, jobs core, and storage — and would *upgrade* several (multi-worker jobs, cron, DLQ, priority, model
routing). These are the pieces it still needs before dropping its own code. Priority order; fold into
`docs/ROADMAP.md` as next versions if Sonora adoption is a goal.

- [ ] **S1 · Portable secret vault: DPAPI protector + recovery-key DEK envelope** (highest value)
  - `Lyntai.Secrets` is AES-GCM with a BYO key only — no DPAPI, no recovery key, no `Recover()`, no
    machine-portability. Add a `DpapiSecretProtector` (Windows `ProtectedData`, guarded by `OperatingSystem.IsWindows()`)
    and a DEK-envelope vault mode (random 256-bit DEK encrypts secrets, double-wrapped by DPAPI + a PBKDF2 recovery
    key; `GenerateMasterKey`/`Recover`/machine-fingerprint). Keep AES-GCM/BYO as the portable default. (Mirror
    Sonora `…/Modules/Core/Services/SecretVault.cs`.) Tests: DPAPI round-trip; recover via key on a "different
    machine"; tamper → `CryptographicException`.
- [ ] **S2 · Job admission-control seam + first-class `Paused` state**
  - Sonora's `CapacityGovernor` (external GPU/CPU-load-aware lane throttling + schedule window) has no Lyntai hook,
    and there's no `Paused` status. Add `IJobAdmissionController` the runner consults per lane before
    `ClaimNextAsync` (allow / hold-lane) + `JobStatus.Paused` with pause/resume on `IJobQueue`/`IJobStore` across
    all three backends. App owns the load sampling. Tests: a controller that holds a lane → no claims for it;
    pause/resume round-trips on every backend.
- [ ] **S3 · Live job progress + step reporting on `JobContext`**
  - Lyntai exposes only `SaveCheckpointAsync`; Sonora's UI needs `ReportProgressAsync(done,total,stage)` +
    `ReportStepAsync(msg)`. Add them (new `JobRecord` `Progress`/`Total`/`Stage`/`StepLog` fields + a migration
    across backends, or an event stream). Tests: progress/steps round-trip and are readable while the job runs.
- [ ] **S4 · Per-request refusal-pattern seam** — add optional `LlmRequest.RefusalPattern` (or a classifier hook)
  so a caller can supply an extra refusal check on the reply text (Sonora passes a per-language regex per call).
  Keep the central patterns as default. Test: a reply matching a per-request pattern → `Refused`.
- [ ] **S5 · Document the "rate-limit → surface" recipe for single-provider adopters** — Sonora wants a 429 to
  hard-stop (protect the quota window), not cool-and-advance; with a sole candidate, `ExemptSoleCandidate` would
  even retry it. README/knowledge recipe: `ConfigureRouting(p => p.On(RateLimited, Surface))` (+ note
  `ExemptSoleCandidate`). Doc-only (capability exists).
- [ ] **S6 · (nice-to-have) curated-memory variant of `IMemoryStore`** — Sonora's is a curated catalog
  (`Kind`/`Enabled`/`Source` + `UpdateAsync` + per-kind prompt sections); Lyntai's is a remember/recall log.
  Optionally add a curated-entry model + `UpdateAsync` + per-kind composition. Otherwise Sonora keeps its own
  memory module — acceptable; deprioritize.

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
