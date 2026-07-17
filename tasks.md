# Lyntai (闁诲繘娼цぐ? 闁?Implementation Plan / Task Backlog

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended)
> or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.
>
> **Read `docs/2026-07-17-lyntai-design.md` first** 闁?it pins every interface, the fork decisions, the
> fallback/CLI semantics, and what's out of scope. This file is the *sequence*; the spec is the *contract*.

**Goal:** a NuGet-packable, DI-first .NET 10 library giving a new app an LLM provider abstraction
(routing + fallback across CLI / API / MEAI-bridged providers) and pluggable SQLite storage plus the
LLM-ops layer (prompt registry, scoring, traces, task-scoped memory) 闁?`AddLyntai(...)` and go.

**Architecture:** `Lyntai.Core` (interfaces + router/fallback + cortex + DI, no heavy deps) with adapter
packages that depend only on Core: `Lyntai.Storage.Sqlite`, `Lyntai.Providers.ClaudeCli`,
`Lyntai.Providers.OpenAiCompatible`, `Lyntai.Providers.ExtensionsAi`. Composed via DI; no adapter
references another. Verified by `tests/Lyntai.Tests` and the `samples/Lyntai.Playground` smoke.

**Tech stack:** net10.0 鐠?C# 13 鐠?Dapper 鐠?FluentMigrator 鐠?Microsoft.Data.Sqlite 鐠?FTS5 (trigram) 鐠?Microsoft.Extensions.{DependencyInjection,Http,AI} 鐠?xUnit 鐠?Node-based devtools (`dev.mjs`).

**Conventions (mirror the family 闁?see `.claude/rules/dev-conventions.md`):** modules = interface in Core
+ impl in adapter; async Dapper + `snake_case` columns + `CAST(x AS REAL)` for doubles; FluentMigrator
numbered `YYYYMMDDNNNN` (never reuse); variation points are DI collections, never if/else; BOM-less UTF-8
sources; TDD (failing test first); commit per task. **Never commit without the user's approval.**

---

## Phase 0 闁?Solution & build scaffolding

Goal: `node devtools/dev.mjs build` and `node devtools/dev.mjs test` both green with an empty-but-real
solution. (devtools, `.gitignore`, `Directory.Build.props`, `.claude/`, `CLAUDE.md` are pre-seeded by the
planning session 闁?verify, don't recreate.)

- [x] **0.1** `git init` (Lyntai gets its own repo, sibling to the others). Then `node devtools/dev.mjs install-hooks`.
- [x] **0.2** Create `Lyntai.slnx` referencing the projects created below.
- [x] **0.3** Create `src/Directory.Packages.props` (central package management) 闁?pin: Dapper, FluentMigrator.Runner.SQLite, Microsoft.Data.Sqlite, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Http, Microsoft.Extensions.AI (+ .Abstractions), Microsoft.Extensions.Logging.Abstractions, xUnit, xunit.runner.visualstudio, SQLitePCLRaw.bundle_e_sqlite3.
- [x] **0.4** Create empty projects + one `Class1`-free placeholder each so the solution builds:
      `src/Lyntai.Core`, `src/Lyntai.Storage.Sqlite`, `src/Lyntai.Providers.ClaudeCli`,
      `src/Lyntai.Providers.OpenAiCompatible`, `src/Lyntai.Providers.ExtensionsAi`,
      `samples/Lyntai.Playground` (Exe), `tests/Lyntai.Tests` (xUnit). Each `src/*` is `<IsPackable>true</IsPackable>`
      with package metadata; Playground/Tests are not packable.
- [x] **0.5** Add project references: every adapter + Playground + Tests 闁?`Lyntai.Core`; Tests 闁?all adapters.
- [x] **0.6** One trivial passing xUnit test (`SmokeTests.SolutionBuilds`) so `dev.mjs test` has something green.
- [x] **0.7** Commit: `chore: solution scaffolding + central package management`.

**Acceptance:** `dev.mjs build` restores + builds all 7 projects; `dev.mjs test` runs 1 passing test;
`dev.mjs check-sensitive --tree` is clean.

---

## Phase 1 闁?Core abstractions (`Lyntai.Core`)

Goal: every interface/type from spec 閹? exists; the pure logic (router fallback, dedup, cooldown, prompt
render + placeholder guard, scoring aggregation, `FtsQuery`, `ProcessRunner`) is unit-tested. No provider
or DB yet 闁?router is tested against fake in-memory `ILlmProvider`s.

- [x] **1.1 LLM value types** 闁?`Llm/LlmMessage.cs`, `LlmRequest.cs`, `LlmReply.cs`, `LlmChunk.cs`, `LlmUsage.cs`, `LlmTool.cs`, `LlmVerdict.cs`, `LlmCandidate.cs`. Records exactly as spec 閹?.1. Test: construction + record equality. Commit.
- [x] **1.2 `ILlmProvider` / `ILlmRouter` interfaces** 闁?`Llm/ILlmProvider.cs`, `Llm/ILlmRouter.cs`. No impl yet. Commit.
- [x] **1.3 `DeadHostTracker`** 闁?`Llm/DeadHostTracker.cs`: N consecutive fails 闁?cooldown window; any success resets; thread-safe (lock). **Inject a clock** (`Func<DateTimeOffset>` / `TimeProvider`) 闁?no `DateTime.Now` in logic, so tests are deterministic. Tests: fails-below-threshold stays live; hits-threshold goes dead; success resets; cooldown expiry re-lives. Commit.
- [x] **1.4 Candidate dedup** 闁?`Llm/CandidateDedup.cs`: drop repeat `(providerId, model)`, first wins, preserve order. Tests: dup primary stripped; order preserved; empty 闁?empty. Commit.
- [x] **1.5 `LlmRouter` (non-streaming fallback)** 闁?`Llm/LlmRouter.cs` implementing `ILlmRouter.CompleteAsync`. Semantics from spec 閹?: dedup 闁?try in order 闁?`Failed`/`Timeout` advances, `RateLimited` circuit-breaks (stop, surface), `Refused` surfaces (no fallback), skip dead hosts, log each attempt (`ILogger`). Tests (fake providers returning scripted verdicts): first-ok returns it; first-failed闁愁偅濮眅cond-ok; all-failed闁愁偅濮st error; rate-limited stops immediately; refused stops immediately; dead provider skipped. Commit.
- [x] **1.6 `LlmRouter` (streaming, no-fallback-after-token)** 闁?`StreamAsync`: pre-content error advances to next candidate; **once any content chunk is yielded, errors pass through unchanged**. Tests: pre-content failure falls over; mid-stream error after a token is passed through (no second candidate invoked); success streams straight through. Commit.
- [x] **1.7 `ProcessRunner`** 闁?`Process/ProcessRunner.cs`: `UseShellExecute=false`, `ArgumentList` only, stdin write (BOM-less UTF-8), stdout/stderr capture (BOM-less UTF-8), per-call timeout, `Kill(entireProcessTree:true)` on cancel/timeout, resolved-path cache (`where.exe`/`which`, prefer `.cmd`/`.exe`). Tests (spawn `dotnet --version` or a tiny node script): captures stdout; honors timeout闁愁偅濮檌ll; passes stdin through. Commit.
- [x] **1.8 `IPromptRegistry` + `PromptRegistry`** 闁?`Prompt/IPromptRegistry.cs`, `Prompt/PromptRegistry.cs`: override key `lyntai.prompt.<name>` from `IKeyValueStore`, `{placeholder}` fill, **reject an override that drops a placeholder present in the default**. Tests: no-override renders default+vars; override wins; missing-placeholder override rejected (throws/falls back to default 闁?pick one, document it); unknown `{var}` left literal or errors (document). Commit.
- [x] **1.9 Cortex interfaces** 闁?`Cortex/IScorer.cs`, `Cortex/LlmScorerBase.cs` (abstract; one-shot judge via `ILlmRouter`, parses `{score,reason}`), `Cortex/IScoringService.cs`, `Cortex/ScoreModels.cs` (`ScoreContext`, `ScoreResult`, `ScoredResult`), `Cortex/ITraceService.cs` + `TraceModels.cs` (`RunTrace`, `TraceStep` 闁?kind/label/tokens/cost/durationMs). Interfaces + models only. Commit.
- [x] **1.10 `ScoringService`** 闁?`Cortex/ScoringService.cs`: iterate `IEnumerable<IScorer>`, skip `null` results, aggregate. Tests: two fake scorers both run; a scorer returning null is omitted; grouping preserved. Commit.
- [x] **1.11 Storage domain interfaces** 闁?`Storage/IKeyValueStore.cs`, `Storage/IConversationStore.cs`, `Storage/IMemoryStore.cs`, `Storage/IScoreStore.cs`, `Storage/ITraceStore.cs`, `Storage/IDbConnectionFactory.cs`, plus DTOs (`Thread`, `ChatMessage`, `MemoryEntry`, etc.). Interfaces + DTOs only; impls in Phase 2. Commit.
- [x] **1.12 `FtsQuery`** 闁?`Storage/FtsQuery.cs`: drop `<3`-char tokens, quote the rest, OR-join; return null when nothing usable (caller falls back to LIKE). Tests: short tokens dropped; special chars quoted; all-short 闁?null. Commit.
- [x] **1.13 DI builder** 闁?`DependencyInjection/LyntaiBuilder.cs` + `ServiceCollectionExtensions.AddLyntai(...)`. `LyntaiBuilder` collects provider registrations, storage registration, scorer registrations, default candidate order; `AddLyntai` wires `ILlmRouter`, `IPromptRegistry`, `IScoringService`, `ITraceService` into DI. Provider/storage-specific `Add*`/`Use*` extension methods live in their adapter packages but extend `LyntaiBuilder`. Test: `AddLyntai` with a fake provider + in-memory KV resolves `ILlmRouter` and round-trips a completion. Commit.
- [x] **1.14 Options + env overrides** 闁?`LyntaiOptions.cs`: timeouts, cooldown threshold/window, default model per consumer; bind from config + `LYNTAI_*` env. Tests: env override beats config; defaults applied. Commit.

**Acceptance:** `dev.mjs test` green; router fallback + streaming semantics + prompt guard + dead-host + dedup all covered by passing unit tests; no I/O in Core tests.

---

## Phase 2 闁?SQLite storage (`Lyntai.Storage.Sqlite`)

Goal: every storage domain interface has a working SQLite implementation, migrated + FTS-indexed, verified
by integration tests against a temp db. `builder.UseSqliteStorage(path)` wires them all.

- [x] **2.1 `SqliteConnectionFactory`** 闁?`IDbConnectionFactory` impl: `MatchNamesWithUnderscores=true` (static ctor), pooled `Open()` with `PRAGMA journal_mode=WAL; busy_timeout=5000; foreign_keys=ON`. Test: opens, pragmas applied, round-trips a scalar. Commit.
- [x] **2.2 Migration runner + base** 闁?`Migrations/` with FluentMigrator wiring; `MigrationRunnerService` discovers + applies on `UseSqliteStorage`. Test: fresh temp db 闁?runner applies 闁?`VersionInfo` populated. Commit.
- [x] **2.3 Migration `202607170001_KeyValue`** + `KeyValueStore` 闁?`app_config(key PK, value, updated_at)`. Tests: set/get/delete; overwrite updates `updated_at`; missing key 闁?null. Commit.
- [x] **2.4 Migration `202607170002_Conversation`** + `ConversationStore` 闁?`thread`, `message` tables (FK message闁愁偅濮瞙read, `foreign_keys=ON`). Tests: create thread, append messages, list by thread ordered, delete cascades. Commit.
- [x] **2.5 Migration `202607170003_Memory` (+ FTS5 trigram)** + `MemoryStore` 闁?`memory_entry` external-content `memory_fts` (trigram) kept in sync by AFTER INSERT/DELETE/UPDATE triggers, backfilled in-migration. Recall via `FtsQuery` MATCH + `bm25()`, LIKE fallback; task/scope filter; bounded (cap entries), fail-open (recall never throws on empty/short query). Tests: remember闁愁偅濮癳call by substring (incl. a CJK substring, proving trigram); scope filter; cap enforced; short-query LIKE fallback. Commit.
- [x] **2.6 Migration `202607170004_Score`** + `ScoreStore` 闁?persist `ScoredResult`s per session (`CAST(score AS REAL)` in SELECTs). Tests: save+load; double round-trips exactly (guards the affinity trap). Commit.
- [x] **2.7 Migration `202607170005_Trace`** + `TraceStore` 闁?`run_trace` + `trace_step`. Tests: save trace with steps, load by session, token/cost totals preserved. Commit.
- [x] **2.8 `UseSqliteStorage` extension** on `LyntaiBuilder` 闁?registers factory + all five stores + runs migrations. Test: `AddLyntai(b => b.UseSqliteStorage(tempDb))` resolves every store interface and each round-trips. Commit.

**Acceptance:** integration tests green against a per-test temp db (created, migrated, deleted); FTS CJK-substring recall proven; no double-affinity regressions.

---

## Phase 3 闁?Claude CLI provider (`Lyntai.Providers.ClaudeCli`)

Goal: `builder.AddClaudeCliProvider()` yields an `ILlmProvider` (`Id="claude-cli"`) that spawns the
authenticated `claude` CLI via `ProcessRunner`, parses stream-json, maps to `LlmReply`/`LlmChunk` +
verdict. Tested entirely against the **provider-stub** (no real tokens).

- [x] **3.1 Arg builder** 闁?`ClaudeArgs.cs`: static argv (`--output-format stream-json`, `--verbose`, model, disallowed UI tools); dynamic content (the prompt) goes via stdin, never argv. Unit test: argv shape; prompt never in argv. Commit.
- [x] **3.2 stream-json parser** 闁?`StreamJsonParser.cs`: translate `{system,assistant,user,result}` lines 闁?`LlmChunk`/text + `LlmUsage` + cost. Unit test against captured fixture lines (incl. the stub's output). Commit.
- [x] **3.3 `ClaudeCliProvider`** 闁?`CompleteAsync` + `StreamAsync` over `ProcessRunner`; `CLAUDE_CMD` / `LYNTAI_PROVIDER_CMD` env override (points at the stub in tests); no-output 闁?`Failed` with stderr tail in `Detail`; timeout 闁?`Timeout`. Integration tests (env 闁?provider-stub): completion returns stub text + `Ok`; empty-output 闁?`Failed`; streaming yields chunks then done. Commit.
- [x] **3.4 `AddClaudeCliProvider`** extension on `LyntaiBuilder`. Test: registered, resolvable via router by id. Commit.

**Acceptance:** provider tests green against the stub with zero network/real-CLI dependency; spawn hygiene (ArgumentList, stdin, kill-tree) exercised.

---

## Phase 4 闁?OpenAI-compatible provider + router end-to-end (`Lyntai.Providers.OpenAiCompatible`)

Goal: an HttpClient-based provider covering OpenAI/Ollama/OpenRouter-style endpoints, with URL-native
detection + payload normalization, wired so the **router fallback works across a CLI provider and an HTTP
provider**.

- [ ] **4.1 Provider detection** 闁?`ProviderDetect.cs`: hostname/path shape 闁?`openai` | `ollama` | 闁? fail-open to OpenAI-compat. Host-match must be exact/subdomain (not substring 闁?guard `anthropic.com.evil.com`). Tests table-driven. Commit.
- [ ] **4.2 Payload builders** 闁?`Payloads/OpenAiPayload.cs`, `Payloads/OllamaPayload.cs`: canonical `LlmRequest` 闁?provider schema (Ollama tool `arguments` as object vs OpenAI string; `num_ctx`; `response_format` for structured output). Unit tests: message mapping; tool-arg normalization; schema round-trip. Commit.
- [ ] **4.3 `OpenAiCompatibleProvider.CompleteAsync`** 闁?`HttpClient` (from `IHttpClientFactory`), map HTTP status 闁?verdict (429闁愁偅濡禦ateLimited`, 5xx/timeout闁愁偅濡禙ailed`/`Timeout`, content-filter闁愁偅濡禦efused`), tolerant JSON extraction. Tests against a stubbed `HttpMessageHandler`: 200闁愁偅濡禣k`+text; 429闁愁偅濡禦ateLimited`; 500闁愁偅濡禙ailed`; malformed闁愁偅濮猲e-retry闁愁偅濡禙ailed`. Commit.
- [ ] **4.4 `OpenAiCompatibleProvider.StreamAsync`** 闁?SSE parse (`data:` lines, `[DONE]`), first-token marks committed. Tests: chunks parsed in order; `[DONE]` terminates; pre-content 500 surfaces as error chunk (lets router fall over). Commit.
- [ ] **4.5 `AddOpenAiCompatibleProvider(id, cfg)`** extension (BaseUrl, apiKey, default model, dead-host wired to `DeadHostTracker`). Commit.
- [ ] **4.6 Router end-to-end integration** 闁?in Tests: `AddLyntai` with claude-cli (stub) + openai-compatible (stubbed handler) + `DefaultCandidates`. Tests: primary-fails闁愁偅濮眅condary-serves; streaming never falls back after a token across the two real provider types; dead-host cooldown skips a downed provider then re-tries after expiry. Commit.

**Acceptance:** two heterogeneous providers behind one router; all 閹? fallback semantics proven end-to-end.

---

## Phase 5 闁?Cortex layer implementations

Goal: prompt registry, scoring (incl. an LLM judge), traces, and task-scoped memory work end-to-end over
the stores + router.

- [ ] **5.1 Wire `PromptRegistry` to `IKeyValueStore`** (Phase 1.8 used a fake) 闁?integration test: override persisted in SQLite KV changes the rendered prompt. Commit.
- [ ] **5.2 Two built-in deterministic scorers** 闁?e.g. `OutcomeScorer`, `StructureScorer` in `Cortex/Scorers/` (generic, no domain assumptions; document what each checks). Tests. Commit.
- [ ] **5.3 One `LlmScorerBase` judge scorer** (e.g. `RelevancyScorer`) 闁?runs through the router; against the provider-stub's `SCORING TASK` path returns a deterministic `{score,reason}`. Integration test. Commit.
- [ ] **5.4 `ScoringService` 闁?`IScoreStore`** 闁?evaluate persists results. Integration test: evaluate a context, results readable from the store. Commit.
- [ ] **5.5 `TraceService` 闁?`ITraceStore`** 闁?`Begin`/record steps/token+cost totals persisted; `GetAsync` reads back. Integration test. Commit.
- [ ] **5.6 `MemoryStore` composition helper** 闁?task-scoped recall bounded + appended to a prompt (the `IPromptComposer`-style helper from Sonora, fail-open). Integration test: remembered facts surface in a composed prompt; outage 闁?prompt still renders. Commit.

**Acceptance:** the LLM-ops loop (prompt override 闁?run 闁?score 闁?trace 闁?remember) works against SQLite + the stubbed router.

---

## Phase 6 闁?MEAI bridge, sample, e2e

Goal: any `Microsoft.Extensions.AI` `IChatClient` becomes a Lyntai provider; the Playground exercises the
full stack; the devtools e2e harness is green.

- [ ] **6.1 `ExtensionsAiProvider`** 闁?`Providers/ExtensionsAiProvider.cs`: adapt `IChatClient` 闁?`ILlmProvider` (map `LlmRequest`闁愁偅鎽紺hatMessage`/`ChatOptions`, streaming via `GetStreamingResponseAsync`, usage, verdict from exceptions). Tests against a fake `IChatClient`. Commit.
- [ ] **6.2 `AddExtensionsAiProvider(id, IChatClient)`** extension. Test: a fake `IChatClient` serves through the router by id. Commit.
- [ ] **6.3 `Lyntai.Playground`** 闁?console app: `AddLyntai` with SQLite + claude-cli + an openai-compatible endpoint + default candidates; run a completion, score it, persist a trace, recall memory; print results. Honors `LYNTAI_PROVIDER_CMD` so it runs against the stub with no real tokens. Commit.
- [ ] **6.4 devtools e2e** 闁?`devtools/scripts/e2e/p1.mjs`: boot the Playground against a temp data dir with `LYNTAI_PROVIDER_CMD` = provider-stub, assert it completes + wrote a trace + a memory row. Wire into `dev.mjs e2e`. Commit.

**Acceptance:** `dev.mjs e2e` green (Playground full-stack smoke against the stub); MEAI bridge round-trips a fake `IChatClient`.

---

## Phase 7 闁?Packaging & docs

Goal: the library is consumable as NuGet packages with a clean README.

- [ ] **7.1 Package metadata** 闁?per `src/*` csproj: `PackageId` (`Lyntai.Core`, 闁?, description, authors, license, repo url, `PackageReadmeFile`. Version from `src/Directory.Build.props` (`VersionPrefix`).
- [ ] **7.2 `dev.mjs pack`** 闁?`dotnet pack` all packable projects 闁?`publish/packages/*.nupkg`; print ids + sha256. Commit.
- [ ] **7.3 README** 闁?the 閹?0 "consuming Lyntai" story: install, `AddLyntai(...)`, the four provider/storage add-ons, a minimal working snippet. Commit.
- [ ] **7.4 Final self-review** 闁?`dev.mjs test` + `dev.mjs e2e` + `dev.mjs check-sensitive --tree` all green; spec 閹? interfaces all implemented; out-of-scope items (閹?) genuinely absent.

**Acceptance:** `dev.mjs pack` produces restorable packages a throwaway consumer project can `AddLyntai` against and run a stubbed completion.

---

## Notes for the implementer

- **TDD, every task:** failing test 闁?run it fail 闁?minimal impl 闁?run it pass 闁?commit. The acceptance
  lines are your definition of done per phase.
- **Deviate from the plan when the code disagrees with it** 闁?the spec's *contract* (interfaces,
  semantics) is authoritative; this file's task ordering is a suggestion. Record real deviations in the
  commit message.
- **Ask before committing** if running non-autonomously. Never `--no-verify` past the sensitive-info hook
  without cause.
- The provider-stub (`devtools/scripts/provider-stub.mjs`) is the seam that keeps every provider/e2e test
  free of real tokens 闁?extend its prompt-marker behavior as new tests need deterministic outputs.
