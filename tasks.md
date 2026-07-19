# Lyntai (灵台) — Implementation Plan / Task Backlog

> **Status: phases 0–7 + roadmap v0.3–v0.28 implemented** (agentic tool-calling, durable jobs, guards,
> secrets, semantic memory, three storage backends, governance decorators, …). See `CHANGELOG.md` for
> per-release detail and `docs/ROADMAP.md` for the forward sequence.
> **➡ All planned work is DONE, including Part 6 (agent-session primitive).** Parts 1–5
> (T1–T13 · S1–S6 · N1–N4 · C1 · A1–A8) + Part 6 (G1a/G1b · G2a/G2b · G3) landed — bugs fixed + tested,
> refactors behavior-preserving, and the generic self-driving-agent-session primitive shipped
> (`IAgentSession` in Core `Lyntai.Agents`, the `claude` CLI adapter, both consumption doors, resume
> across the gate). Build clean · 725 tests · e2e 3/3 · leak scan clean. This unblocks the adopter's
> two-gate migration (and thereby its cortex migration).

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
- [ ] **R5 · Cross-backend parity is under-verified (Postgres false-green + missing shared contracts)** (sustainable)
  - `tests/…/PostgresStorageTests.cs` gates every test `if (!pg.Available) return` → **silently passes** (not
    skips) when Docker is absent, so Postgres parity is unverified in default CI. Postgres also re-implements
    `JobStoreContract`/`CuratedMemoryStoreContract` ad-hoc (subset of assertions), and 6 of 8 stores (Memory,
    KeyValue, Conversation, Score, Trace, PromptVersion) have NO shared cross-backend contract — each backend
    is tested with different cases, so InMemory (the test double) can green-light semantics the SQL stores
    don't reproduce. Fix: `Assert.Skip` (visible) + run the pg container in CI; run the existing contracts
    against Postgres; extract contracts for the other 6 stores and run all three backends through each.
- [ ] **R6 · SQLite memory dedup is non-atomic (data-integrity divergence)** (sustainable)
  - `SqliteMemoryStore.RememberAsync` (`:32-43`) is UPDATE-then-INSERT with no unique constraint → two
    concurrent Remembers create duplicate `(task,scope,content)` rows; `PostgresMemoryStore` (`:34-38`) uses
    an atomic `ON CONFLICT` on a unique index and can't. Fix: add `UNIQUE(task_key, scope, content)` (or
    hashed) to the SQLite schema + `INSERT … ON CONFLICT DO UPDATE`.

### Med
- [ ] **R7 · README/CHANGELOG version drift (ships in every nupkg)** (sustainable) — `README.md` `## Status`
  is stuck at ~v0.15 (omits governance trio, semantic memory, Postgres, DLQ/cron/cancel jobs, secret vault,
  agent session) while `VersionPrefix`=0.28.5; `CHANGELOG.md` has no entries for 0.28.2–0.28.5 and the
  agent-session work sits under "Unreleased". Reconcile on release; add a `dev.mjs` pack-doctor that fails if
  README status ≠ `VersionPrefix`.
- [ ] **R8 · Verdict classifier is English/regex-biased + not extensible; `ContextWindowExceeded` unreachable
  on typed-exception paths** (generic) — `LlmVerdictClassifier` text patterns are English-only and `static
  partial` (can't extend without editing core); `FromException` has no context-window arm (MEAI "prompt too
  long" typed exceptions → `Failed`, defeating the big-context fallback). Keep typed-status primary; add a
  consumer pattern seam (`Func<string,LlmVerdict?>` / injectable set) + known context-window exception types.
- [ ] **R9 · `Refused` verdict overloaded for capability gaps** (generic) — streaming native tool-calls map to
  `Error(Refused,…)` (`OpenAiCompatibleProvider.cs:206`, `ExtensionsAiProvider.cs:130`); `Refused` means
  "content policy, surface no-fallback", so telemetry/scorers can't tell a policy refusal from a transport
  limitation. Add a distinct verdict (e.g. `Unsupported`) mapped to `Surface`.
- [ ] **R10 · Duplicated stream-json parsing + CLI reconciliation will drift** (sustainable) —
  `StreamJsonParser.cs` and `StreamJsonAgentReader.cs` independently parse the same wire format (usage field
  names, text-block concat, `GetLong`); `ClaudeCliProvider.CompleteAsync` (`:80-102`) hand-rolls a buffered
  assistant-vs-result reconciliation that duplicates the streaming loop (`:152-169`). Extract shared
  field-extraction helpers; consider `CompleteAsync` accumulating over `StreamAsync` like `LocalProvider`.
- [ ] **R11 · No public seam for a custom front-door decorator** (generic) — `FrontDoorDecorators` +
  `AddFrontDoorDecorator` are `internal`; an app's own cross-cutting concern (PII redaction, request logging)
  must pre-register a whole `ILlmClient`, which trips the governance guard. Expose a public
  `AddFrontDoorDecorator(order, factory)` / `ILlmClientDecorator` collection folding on the same ordered chain.
- [ ] **R12 · `IDbConnectionFactory.Open()` is sync-only** (sustainable) — every store blocks a threadpool
  thread on connect (esp. Postgres network+pool). Add `Task<DbConnection> OpenAsync(ct)` to the interface NOW
  (default over `Open()`) — the one interface change that's expensive to make post-publish.
- [ ] **R13 · Unwrapped DEK never zeroized** (sustainable — crypto) — `EnvelopeSecretVault` (`Create`/
  `UnwrapWithMachine`/`UnwrapWithRecoveryKey`) hands the DEK to `AesGcmSecretProtector` (which clones it) and
  never `ZeroMemory`s the original; the transient recovery KEK IS scrubbed but the longer-lived master DEK is
  not. Zero it after building the inner protector; consider making the protector disposable.
- [ ] **R14 · ClaudeCli silently drops `LlmRequest.Tools`** (generic) — `SupportsToolCalls=false` and
  `ClaudeArgs.Build` ignores `req.Tools` (tools reach the CLI only via the separate MCP provisioner). A caller
  putting tools on the request + routing to claude-cli gets them dropped with no diagnostic. Log a warning
  when `req.Tools` is non-empty on the CLI path; document the divergence.
- [ ] **R15 · Process-global Dapper type-handler coupling between the two SQL factories** (generic) — both
  `SqliteConnectionFactory` + `PostgresConnectionFactory` register a `DateTimeOffsetHandler` into Dapper's
  process-global registry in a static ctor ("whichever wins, both must be identical") — a third-party handler
  or a 4th backend can clobber it. Register idempotently/defensively; add a test asserting both are identical.
- [ ] **R16 · Semantic `RememberAsync` not fail-open + no dimension check** (generic) — asymmetric with the
  fail-open `RecallAsync`; a direct `ISemanticMemory` consumer gets an unguarded throw, and a mid-life model
  swap silently poisons a collection (no per-collection dimension stamp). Document the throw contract (or make
  symmetric) + stamp collection dimension at first write.
- [ ] **R17 · `AggregateAsync`/`ExportAsync` live only on `IScoreStore`, bypassing the `IScoringService`
  seam** (generic) — a dashboard must inject the storage interface directly, breaking the "inject the service,
  not the store" layering (`ITraceService.GetAsync` wraps the store correctly). Surface read/aggregate/export
  on `IScoringService`.
- [ ] **R18 · Env-override docs incomplete** (sustainable) — the whole `LYNTAI_JOBS_*` family (6 vars) +
  `LYNTAI_DEFAULT_MODEL` alias are read in `ApplyEnvOverrides` but absent from the `LyntaiOptions` XML-doc
  list + README. Add them; consider one canonical env-var reference table.
- [ ] **R19 · Recall/list ordering diverges across backends beyond what contracts assert** (sustainable) —
  SQLite memory recall `ORDER BY bm25` vs Postgres/InMemory recency; curated `ListAsync`/aggregate `ORDER BY
  <text>` is SQLite BINARY vs Postgres DB-collation. Documented in prose only. Force `COLLATE "C"` (or order
  by recency everywhere) for parity, or assert the divergence in a contract test.
- [ ] **R20 · Job scheduler double-fires under multi-instance** (sustainable) — the runner supports N
  instances but two schedulers read the same due next-run from shared KV and both enqueue before either
  persists → every slot fires per instance. Document "one scheduler process", or make `SetNextAsync` a
  compare-and-swap KV write.

### Low (batch — verify + fix opportunistically)
- [ ] **R21 · Nits**
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

- [ ] **F1 · Feature toggle model + gated registration + selective migration — should-have**
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
