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

- **T1 · Denylist jail bypass on the native tool-calling path**
- **T2 · Durable-job poison-pill is unbounded on a worker crash**
- **T3 · Response-cache cross-model collision when per-consumer default models differ**
- **T4 · Streaming `finish_reason=tool_calls` emits a spurious `Refused` after content**
- **T5 · Memory recall matches "any token" (SQLite FTS) vs "contiguous phrase" (Postgres/InMemory)**
- **T6 · Usage-budget consumer key: InMemory case-insensitive vs SQL case-sensitive**
- **T7 · pgvector throws on a dimension-mismatched row; semantic recall isn't fail-open**
- **T8 · Router treats a provider's own `OperationCanceledException` as caller-cancel**
- ...and 5 more closed items (full text in git history).

## Part 2 — Sonora-adoption gaps (features Lyntai lacks)

- **S1 · Portable secret vault: DPAPI protector + recovery-key DEK envelope**
- **S2 · Job admission-control seam + first-class `Paused` state**
- **S3 · Live job progress + step reporting on `JobContext`**
- **S4 · Per-request refusal-pattern seam**
- **S5 · Document the "rate-limit → surface" recipe for single-provider adopters**
- **S6 · (nice-to-have) curated-memory variant of `IMemoryStore`**

## Part 3 — Review round 2 (2026-07-18)

- **N1 · Concurrent step-log reports lose steps on the SQL backends**
- **N2 · Recovery-KDF iteration count is honored from the envelope with no floor**
- **N3 · Envelope `version` is written but never enforced on read**
- **N4 · Nits (batch, non-blocking)**

## Part 4 — Consumer-driven gaps (Sonora integration)

- **C1 · Per-request timeout override (for the CLI-agent / long-tool-loop path)**

## Part 5 — Adoption gaps: cortex + scoring (2026-07-18)

- **A1 · `IScoreStore`: upsert + cross-session aggregate + bulk export — HARD (blocker)**
- **A2 · `IScoringService`: evaluate WITHOUT persisting even when a store is wired — HARD (blocker)**
- **A3 · `LlmScorerBase`: per-scorer model + consumer hook — HARD (blocker)**
- **A4 · `IScorer.Description` (optional) — nice-to-have**
- **A5 · Document the `ScoreContext.Extra` domain-dimension pattern — nice-to-have**
- **A8 · `LlmScorerBase`: an applicability skip hook (don't judge N/A dimensions) — HARD (blocker)**
- **A6 · Live per-consumer model override read into `ResolveModel` — HARD (blocker)**
- **A7 · Surface the placeholder-contract violation to the caller — should-have**

## Part 6 — Agentic self-driving-agent session (generic primitive) (2026-07-19)

Surfaced trying to migrate a real adopter's (Gatherlight's) **interactive two-gate chat** — plan
(read-only) → human approve → execute (write, scope-guarded) → human review diff → commit — off its
hand-rolled native `ClaudeCliRunner` onto Lyntai. This is the ONE remaining Lyntai gap blocking that
adopter, **and the prerequisite for its cortex migration** (Part 5): the app's cortex — prompt/model
tuning — is overwhelmingly consumed by THIS flow, so cortex can't move onto Lyntai until the flow does.

## Part 7 — App-owned storage: use your own table, no duplication (2026-07-19)

- **P1 · Configurable KV key prefix on the cortex stores — should-have (adoption)** — `keyPrefix` ctor arg on both stores + `LyntaiOptions.PromptKeyPrefix`/`ModelKeyPrefix`; `KeyPrefix` const → `DefaultKeyPrefix` + instance property;...
- **P2 · Generic conversation store — a typed event stream, not just role/text chat — should-have (generic capability)** — `ChatMessage` gains `Kind`/`Payload` (Role/Content kept as aliases); `ChatThread` gains opaque `Metadata` + `SetThreadMetadataAsync`; message colum...
- **P3 · App-owned storage — REDESIGNED per the design principle — should-have (adoption)**

## Part 8 — "Generic + sustainable" review sweep (2026-07-19)

- **R1 · "Plug your own impl" is broken for storage + the README claim is false** — Sqlite+Postgres domain stores now `TryAddSingleton` (match InMemory); pre-registered app impl wins; README claim now true; AddEmbeddings audited (e...
- **R2 · Guards don't cover the agent tool loop** — `IGuardRail.InspectToolCallAsync`/`InspectToolResultAsync` (default methods reusing existing guards); `ToolLoop` gates each call's args + observati...
- **R3 · Response-gate `Replace` only rewrites `Text`, leaving `ToolCalls`/`Detail`** — response Replace now clears `ToolCalls`+`Detail` too (GuardedLlmClient + rail re-threading); replacement is the whole sanitized reply. - `Guards/Gu...
- **R4 · Trace subsystem is orphaned from the agent flows** — chosen: document `ITraceService` as the BYO/app-driven persisted-trace API; OTel Activity spans are the automatic path. Clarified in `ITraceService...
- **R5 · Cross-backend parity is under-verified (Postgres false-green + missing shared contracts)** — `[SkippableFact]`+`Skip.IfNot` (Xunit.SkippableFact) so Postgres tests SKIP visibly, not false-green; extracted KeyValue/Conversation/Memory/Trace/...
- **R6 · SQLite memory dedup is non-atomic (data-integrity divergence)** — added `UNIQUE(task_key, scope, content)` (`ux_lyntai_memory_dedup`, replaces the non-unique prefix index) + `INSERT … ON CONFLICT DO UPDATE` (match...
- **R7 · README/CHANGELOG version drift (ships in every nupkg)** — README Status refreshed to v0.28.5; agent-session moved from Unreleased → `## 0.28.5`; added `dev.mjs doctor` pack-guard (README Status version mus...
- **R8 · Verdict classifier is English/regex-biased + not extensible; `ContextWindowExceeded` unreachable on typed-exception paths** — `FromException` scans the inner-exception chain (typed "too long" → ContextWindowExceeded); added `AddErrorTextMatcher` consumer seam (disposable,...
- ...and 13 more closed items (full text in git history).

## Part 9 — Feature/module toggles: enable only what you use (2026-07-20)

- **F1 · Feature toggle model + gated registration + selective migration — should-have** — `[Flags] StorageFeature` (9 domains + All); `UseSqliteStorage`/`UsePostgresStorage(…, features)` gate registration per feature AND migrate only sel...

## Part 10 — Actor/mailbox model for durable jobs (2026-07-20)

- **A1 · Ordered single-owner-per-key durable jobs (actor mailboxes) — should-have** — `JobSpec`/`IJobQueue.EnqueueAsync` gain an optional `partitionKey`. Jobs sharing a `(lane, partitionKey)` run **one-at-a-time in FIFO (enqueue) ord...

## Part 11 — Consumer-driven gaps: Gatherlight conversation-store adoption (2026-07-20)

- **G1 · `ClaudeToolCalls.FilePathOf` should also read `notebook_path` / `path`, not only `file_path`**
- **G2 · Agent-session `FinalText` should fall back to accumulated assistant text when the terminal `result` is empty**
- **G3 · `IConversationStore` count + filtered/paged list (avoid list-all-then-filter)**

## Part 12 — Consumer-driven gap: curated memory with task + scope (Sonora adoption, 2026-07-22)

- **CM1 · Optional `task` + `scope` on curated memory**

## Part 13 — Assistant coding-system: no-global-memory + sibling coding pattern (2026-07-22)

✅ done 2026-07-22 — Adopted a sibling project's in-repo coding-system discipline: project
facts live in the repo, not in Claude Code's global auto-memory. Added `.claude/rules/no-global-memory.md`
(+ `minimise-bash-prompts.md`, `no-tmp-for-repo-files.md`, `TEMPLATE.md`) and the `RULES_INDEX.md` loading
model / evolve-the-system / invariants; migrated the 9 `lyntai-*` global memories into `docs/DECISIONS.md`
(the `ILlmClient` front door/the `lyntai_` prefix rule already covered two, D3–the `StorageFeature` selective-migration design added the rest).

## Part 14 — App-configurable memory retention policy (multi-strategy) (2026-07-22)

✅ done 2026-07-22 — `IMemoryStore` size management is now an app-configurable `MemoryRetentionPolicy`
(DECISIONS — the configurable memory-retention policy), mirroring the configurable `RoutingPolicy`. Requirement (user): "multi-way / configurable
so the app has control", production-grade, drawing on existing agent-memory systems (LangChain
buffer-window/token-buffer, MemGPT eviction).

## Part 15 — Opt-in memory-prune cron job (2026-07-22)

✅ done 2026-07-22 — Follow-on to Part 14 (DECISIONS — the configurable memory-retention policy). On-write eviction only bounds scopes you keep
writing to; a cold `(taskKey, scope)` accumulates expired rows. `builder.AddMemoryPruneJob(cron,
olderThan?, taskKey?)` registers an internal `MemoryPruneJobHandler` (`IJobHandler` over
`IMemoryStore.PruneAsync`, idempotent) + a cron `JobSchedule` on the EXISTING durable-jobs + cron
machinery — Lyntai owns the prune work, the app owns the pump (no self-run timer; consistent with the "no
host" boundary the platform-kit direction/the platform-kit direction). Handler + payload record are internal (only `AddMemoryPruneJob` is public surface).
TDD'd (handler parses payload / removes; registration wires one handler + N schedules; bad cron throws).
Decided WITH the user: needed because this is a generic library used across many apps (unbounded cold-scope
growth is the real case).

## Part 16 — Code-review follow-ups: close every deferred finding (2026-07-22)

✅ done 2026-07-22 — The workflow code review of Part 14/15 surfaced 10 findings; 4 confirmed bugs + the
dedup landed in `f6fc301`. Per the user's "complete them all", the deferred/refuted rest were closed too:

## Part 17 — CLI runner: `StreamLinesAsync` stdin/stdout pipe deadlock on large prompts (2026-07-23)

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

## Part 18 — CLI completion: inactivity-based dead detection (buffered path) (2026-07-23)

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

## Part 19 — agent-manager desktop adoption + curated-memory papercuts (CM1/CM2/CLI1/TL1/TL2/PR1)

- **CM1 — dedup-on-add for `ICuratedMemoryStore.AddAsync`.** — Added `dedup` (default false) to `ICuratedMemoryStore.AddAsync`. When true the add is idempotent on the (kind, content, task, scope) identity: retu...
- **CM2 — `scope` filter on `ICuratedMemoryStore.ListAsync`.** — Added a strict-equality `scope` filter to `ListAsync` (before `limit`; null = no filter, unchanged). Across all three backends. TDD: `CuratedMemory...
- **CLI1 — headless "skip all permissions" for `ClaudeAgentSession`.** — Added an opt-in `SkipAllPermissions` bool to `ClaudeAgentOptions`. When set, `ClaudeAgentArgs.Build` emits `--dangerously-skip-permissions` and sup...
- **TL1 — surface token usage on `ToolLoopResult`.** — Added nullable `ToolLoopResult.Usage` (init property, like `LlmReply.ToolCalls`). The loop aggregates every front-door reply's `LlmUsage` (summed i...
- **TL2 — live progress from `IToolLoop`.** — Added `IToolLoop.StreamAsync(req, maxIterations?, ct)` yielding `AgentStreamEvent`s (ToolCall/ToolResult per round-trip, assistant TextDelta(s), a...
- **PR1 — default `IProcessRunner`: Windows launcher-shim resolution + forced UTF-8.** — On inspection the default `ProcessRunner` ALREADY forced BOM-less UTF-8 on all three streams and resolved `.cmd`/`.exe` shims; the real remaining g...

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

## Part 21 — 1.0-prep: infrastructure + final API sign-off (2026-07-27; unreleased)

✅ done 2026-07-27 — Also in the part (not `TASKS.md` items): push/PR CI running the full `verify` gate
[later removed the same day at the owner's direction — process is fully manual, see `DECISIONS.md` the manual-verification call];
SourceLink/deterministic CI builds; the design-contract reconciliation amendment; and the **final API
sign-off pass** — an 18-finding audit of the whole public surface, closed by a batch of pre-1.0 breaking
renames/reshapes (`UseDefaultCandidates`, `SchemaMigration` enum, required `AddSecretVault` key +
`AddPlaintextSecretVault`, `IResponseCache.GetAsync`/`RemoveAsync`, `IProcessRunner`
inactivity/maxDuration reshape, `TaskKey`/`ContextSize`/`*Tokens`, wire-format types internal) plus
additive read paths (`IKeyValueStore.ListKeysAsync`, `IJobQueue.GetAsync`/`ListAsync`,
`AddScorer(factory)`/`AddEmbeddings<T>`), an ApiSurface renderer upgrade
(sealed/abstract/static/required) with all 11 baselines regenerated deliberately, and a real

- **P3 — Azure OpenAI preset endpoint shape.** — Outcome: verified against Azure's current `/openai/v1` (v1 GA API) docs; `ProviderDetect.AzureOpenAi` flavor (detects `*.openai.azure.com`), bare-r...
- **L8 — async `IUsageTracker`.** — Outcome: `RecordAsync`/`TotalAsync`/`ResetAsync` (`ValueTask`) across all 3 backends + `BudgetedLlmClient`; per-consumer totals aggregate case-inse...

## Part 22 — Curated-memory as a searchable, metadata-carrying catalog (CMEM3–CMEM6)

- **CMEM3 — optional `Title` on `CuratedMemory`.** — Outcome: `CuratedMemory.Title` (trailing optional record param), `AddAsync` `title` param placed AFTER `dedup` so no pre-existing positional call s...
- **CMEM4 — keyword `SearchAsync` on `ICuratedMemoryStore`.** — Outcome: `SearchAsync(query, kind?, taskKey?, scope?, enabledOnly?, limit?)` matching CONTENT + TITLE with the ListAsync-family strict filters (ena...
- **CMEM5 — `kind` on `ICuratedMemoryStore.UpdateAsync`.** — Outcome: `UpdateAsync` gains a trailing optional `kind` param (placed AFTER `title`, before `ct`, so no pre-existing positional call site silently...
- **CMEM6 — generic `Metadata` field + relational query index; fold & drop `Source`/`Title`.** — Outcome: `CuratedMemory` drops `Source`/`Title`, gains `IReadOnlyDictionary<string,string>? Metadata`; `AddAsync`/`UpdateAsync` drop the `source`/`...

## Part 23 — Deferred-findings burn-down (post-sign-off maintenance)

- **I14 — bound `StreamLinesAsync`'s stderr capture** — Outcome: `ReadTailAsync` (rolling 500-char StringBuilder window, chunked reads, completes on child EOF like `ReadToEndAsync` did) replaces the unbo...
- **L10/L11 — rate-limiter half-live options claim + `LlmVerdictClassifier` custom-matcher lock/copy-per-call** — Outcome: **L10** — `TokenBucketRateLimiter` options are now FULLY live (matching `HasEffectiveLimit`'s documented claim): rate/burst are per-acquir...
- **S3 — shared cap-evict SQL for the memory stores.** — Outcome: `MemoryEviction.CapEvictSql(mode)` in Core returns the one statement; both stores' `CapEvictAsync` collapse to a Dapper one-liner executin...
- **S11 — drop the `(object?)x ?? DBNull.Value` dance in the Postgres stores** — Outcome: all 20 sites (PostgresCuratedMemoryStore + PostgresMemoryStore) now bind typed nullable members directly — Dapper infers the DbType from t...
- **T14 — de-flake the two wall-clock-coupled tests** — Outcome: the abandonment test now POLLS to a 15s deadline for the heartbeat file to hold its size across two consecutive 300ms windows (≥5 missed b...
- **T5 — mid-stream CALLER-cancellation tests** — Outcome: **T5** — router pin (cancel after the first committed chunk → OCE propagates, no fabricated terminal Error, zero fallback calls; `FakeLlmP...
- **T4 remnants — Postgres coverage:** — Outcome: `Fail_with_retry_requeues_available_later` lane-parameterized and routed through the `JobPg` runner (timestamptz retry math now exercised)...
- **S8 — move the remaining 4 Row-DTO pairs (trace/score/prompt-version/usage) to Core**
- ...and 3 more closed items (full text in git history).

## Part 24 — Built-in embedder for the OpenAI-compatible provider (2026-07-27)

- **EMB1 — ship an `IEmbedder` over an OpenAI-compatible `/v1/embeddings` endpoint.** — Outcome: `HttpEmbedder` + `OpenAiCompatibleEmbedderOptions` + `builder.AddOpenAiCompatibleEmbedder(id, cfg, httpClient?)` in `Lyntai.Providers.Open...

## Part 26 — Generalize the MCP tool-hosting seam (2026-07-29)

**Outcome (whole part):** shipped in two commits — the split (additive), then the shim removal
(breaking). Net: a new `Lyntai.Tools.Mcp.Hosting` package (5 files), `ClaudeCliMcpDialect` +
`IMcpCliDialect`/`McpEndpoint`/`McpCliContext`, and `Lyntai.Providers.ClaudeCli.Mcp` gone — 11 packable
projects, down one from 1.0. The relocation itself touched no public surface (every moved type was
`internal`); the only break is the removed package + `AddClaudeCliMcpTools`, migrated by
`AddMcpToolHost(new ClaudeCliMcpDialect())`. Version consequence settled by the deferred-SemVer-strictness rule (documented breaks
may ship in a minor while all consumers are first-party); the bump itself is the release pipeline's job.
Docs: the 1.0 sign-off decisions (git history), CHANGELOG header amendment and an Unreleased
**Breaking** entry with a migration diff, README (generic host + worked custom-dialect example), AOT.md,
ROADMAP, CLAUDE.md, both design docs, and `.claude/knowledge/extending-lyntai.md` gained a fifth

- **MCPH1 — extract `Lyntai.Tools.Mcp.Hosting`** — **Outcome:** `src/Lyntai.Tools.Mcp.Hosting/` with `McpToolHost`, `ToolFunction`, `McpToolHostProvisioner`, `McpToolHostOptions`, `AddMcpToolHost(di...
- **MCPH2 — `IMcpCliDialect` seam** — **Outcome:** `IMcpCliDialect` + `McpEndpoint` + `McpCliContext` in **Core** (`Lyntai.Agents`). Core placement is load-bearing — it lets a provider...
- **MCPH3 — claude dialect into the PROVIDER package; the add-on package removed.** — **Outcome:** `ClaudeCliMcpDialect` ships in `Lyntai.Providers.ClaudeCli` (owner's call) at **zero new dependencies** — it is JSON + strings over Co...
- **MCPH4 — keyed `ICliToolProvisioner` resolution** — **Outcome:** `AddMcpToolHost` registers keyed on `IMcpCliDialect.ProviderId`, with the first registration also taking the unkeyed slot as fallback;...

## Part 27 — Backend version & upgrade awareness (2026-07-30)

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

- **CLI2 — Claude CLI version/model probe**
- **CLI3 — CLI self-update seam**

## Part 28 — Provider probe/update CLI spawn on Windows (2026-07-30)

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

- **CLI2 — probe/update spawn the RAW resolved command → fail on a Windows npm/nvm shim**

## Part 29 — Turn-free backend AUTH + pinned self-install (2026-08-04)

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

- **CLI3 — a turn-free AUTH seam for the CLI provider (`IProviderAuth`), completing the pair CLI1 started.**
- **CLI4 — let `IProviderUpdater` (or `IProviderInstallation`) drive the backend's own PINNED install.**

## Part 30 — Version-authorship guard (2026-08-04)

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

- **REL1 — guard the version against hand-edits (`src/Directory.Build.props`, `devtools/hooks/pre-commit`).**

## Part 31 — Generalize the CLI provider seam + a second CLI backend (2026-08-04)

✅ done 2026-08-04 — **Outcome:** `CliProviderEngine` + `ICliProviderDialect` / `CliProviderDialectBase` /
`CliOutputEvent` / `CliPromptDelivery` / `CliCommand` in Core (`Lyntai.Llm.Cli`), with `ClaudeCliProvider`
refactored into `ClaudeCliDialect` + a dozen forwarding members. **Behaviour preservation was verified, not
asserted:** all ~90 existing claude tests pass UNTOUCHED, and the `ApiSurface` diff showed `ClaudeCliProvider`'s
members byte-identical (the only ClaudeCli addition was the new public dialect). Duplication genuinely
removed: the version-line parser, prompt flattening and command tokenizer are now single copies in Core, and
the claude forwarders for the first two were DELETED (their tests retargeted to the Core primitives with
byte-identical assertions) rather than left as aliases. 21 engine tests drive the generic contract through a
`FakeCliDialect`, so they can't pass by accident via claude's behaviour. Recorded as `docs/DECISIONS.md` — the `CliProviderEngine`-plus-dialect rule.

- **CLI5 — extract the shared spawned-CLI logic behind a per-CLI dialect seam, and prove it with a second backend.**
- **CLI6 — support a PORTABLE CLI (an app-bundled binary), not just a global install.**

## Part 32 — Generation platform + a coherent package graph (2026-08-04)

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

- **MED1 — a generation domain as a Lyntai platform**

## Part 33 — Generation backends: local engine, durable renders, first remote queue (2026-08-04)

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

- **GEN3 — local subprocess backend**
- **GEN4 — async video composed with `Lyntai.Jobs`**
- **GEN6 (tool/MCP bridge half)**
- **GEN5 — governance/telemetry parity for generation**
- **GEN11 — the `Add*` shims' infinite HTTP timeout rests on a per-call deadline that does not exist.**

## Part 35 — the 2.0.1 release hardening + a packaging policy with gates (2026-08-04)

✅ done 2026-08-04 — **Outcome:** the audit found six real defects, and the most serious was self-inflicted:
`Lyntai.Providers.Default` stamped `IsTrimmable` into its assembly — a promise to a consumer's trimmer — while
three generation backends built request bodies by reflection-serializing anonymous types. The warnings had been
there all along; nothing failed on them. Also fixed: docs pointing consumers at three package ids the
restructure had deleted (an install line that cannot restore), `GuardedStream.ReadAll` silently dropping
`WithCancellation` on a public async iterator, an empty symbol package on the new bundle, an unconfigured image
backend reporting `AuthFailed` (so the new cooldown benched it) instead of `NotConfigured`, and two dead package
pins left by the ASP.NET removal.

- **A pre-release audit of the shipped artifact, not the repo.**
- **A bundle membership policy (the bundle dependency budget) + a dependency-budget gate.**
- **Granularity settled (the many-small-packages shape) + an inventory gate + a package scaffolder.**
- **The media backends split out (the release-cadence package split) and generation marked EXPERIMENTAL.**

## Part 36 — generation ergonomics: the misbinding trap and the missing wiring (2026-08-04)

✅ done 2026-08-04 — **Outcome:** ten static factories on `GenerationInput` (`Init`/`FirstFrame`/`Reference`/
`Voice`, each with a bytes and a `System.Uri` overload, plus `From(role, …)` for a role a backend documents
itself), and five `Add*` shims covering every backend the package ships — the fifth, `AddLocalDiffusionProvider`,
takes `IProcessRunner` from DI rather than an `HttpClient`, since it spawns rather than calls. The URI overloads
take `System.Uri` rather than `string` on purpose: two adjacent strings would reintroduce the exact
transposition being fixed, and a wrong type is a compile error where a wrong string is a silent one. Reasoning
in `docs/DECISIONS.md` — the named-factories rule.

- **GEN10 — `GenerationInput`'s ctor order is a silent-misbinding trap; give `Role` a safer path.**
- **generation backend wiring helpers**

## Part 37 — provider lifetime: a pool keyed on the configuration, for externally-owned settings (2026-08-05)

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

- **GEN12 — own the provider POOL: keep one instance per configuration and deprecate it when the configuration changes.**

## Part 34 — findings from the pre-2.0.1 consumer smoke (2026-08-04)

- **LLM-side parity for the no-credentials verdict**

## Part 25 — post-1.0 additive ergonomics from the 1.0 API review (2026-08-05)

- **verdict helpers** — Outcome: shipped as `LlmVerdictExtensions.IsOk()` / `IsTransient()` on the ENUM rather than as per-verdict methods on `LlmReply`. Two reasons, both...
- **`AddMcpTools` convenience overload** — Outcome: both. `AddMcpTools(params ITool[])` sits beside the sequence overload and delegates to it (an array argument binds to the params overload...
- **agent-event contract** — Outcome: **no code change; the item was stale on both halves.** `FilePathOf` already reads `file_path` → `notebook_path` → `path` in that order, wi...
- **curated-memory ergonomics**
- **member/type XML docs** — Outcome: `ExtensionsAiProvider` gained `<param>` docs for all four constructor slots (notably that `id` is a LABEL for one configured client, not a...
- **async migration entry points** — Outcome: shipped on both backends, deliberately **narrow and documented as such** (`docs/DECISIONS.md` — the honest `MigrateUpAsync` scope). Fluent...
- **semantic-memory wiring helper** — Outcome: shipped as `b.AddSemanticMemory(…)` in **Core**, not as a storage-package composite (`docs/DECISIONS.md` — the named semantic-memory regis...

## Part 39 — `CodexAgentSession`: the agent-session shape is not claude-only (2026-08-05)

- **CLI11 — a `CodexAgentSession`, so the agent-session shape isn't claude-only.**

## Part 38 — verdict-translation gaps found while closing Part 34 (2026-08-05)

- **`GenerationVerdictClassifier.Translate` flattens `Unsupported` to `Failed`**

## Part 43 — the deferred behaviour cluster from the pre-2.2.0 review (opened and closed 2026-08-05)

**Outcome (2026-08-05):** all seventeen landed, none skipped, each with a failing test first. Tests
1567 → 1657; `verify` and `consumer-smoke` green.

- **GEN-DEDUP**
- **GEN-SUBMIT-VERDICT**
- **GEN-SUBMIT-DETAIL**
- **ROUTER-ID-CASE**
- **CLI-STREAM-CEILING**
- **CLI-EMPTY-CONTENT**
- **OLLAMA-ATTACHMENTS**
- **OPENAI-LONG-FIELD**
- ...and 12 more closed items (full text in git history).
- **Part 40** — a media verdict that is both BLAMELESS and REPORTABLE. `GenerationRouter` now keeps a
- **Part 42** — the API-surface gate's blind spots. The renderer now emits a method's type parameters,
- **Part 25** — curated-memory `taskKey`/`scope` can now move in place, with the identity question

## Part 45 — a measured `turn.failed` shape, and the exit-code precedence it exposed (2026-08-05)

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

- **CLI15 — a measured `turn.failed` shape, filed as EVIDENCE for the failure half of the mapping.**

## Part 44 — an agent session can only be given the app's own tools if the backend is claude (2026-08-05)

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

- **CLI14 — an `IAgentSession` has no way to be pointed at the app's own MCP servers unless it is `ClaudeAgentSession`.**

## Part 46 — MEM1: a named memory-engine seam (2026-08-08)

- **MEM1 — the memory engine seam (Spec A).**

## Part 47 — MEM2a: the graph memory engine on the InMemory backend (2026-08-08)

- **MEM2a — the decay policy, the graph store contract, the engine, and the InMemory backend.**

## Part 48 — MEM2b: graph memory on SQLite and Postgres (2026-08-08)

- **MEM2b — `IMemoryGraphStore` for SQLite and Postgres**

## Part 49 — MEM2c: the agent-facing half of graph memory (2026-08-08)

- **MEM2c — per-engine agent tools, and similarity enrichment.**

## Part 50 — decay is measured in events, not wall-clock time (2026-08-08)

- **Replace the wall-clock decay dimension with a logical position, and damp bursts.**

## Part 51 — MEM-TUNE: the decay constants, measured (2026-08-08)

- **MEM-TUNE — measure the decay defaults, don't ship them as if tuned.**

## Part 52 — decay buries a memory, it does not cut it (2026-08-08)

- **Replace the absolute recall floor with a relative one.**

## Part 53 (item 1 of 3) — a ranking POLICY seam for salience (2026-08-09)

✅ done 2026-08-09 (memory-ranking-seam plan, Tasks 1–4 — Task 4, reciprocal rank fusion, this domain's
second implementation, shipped the same day as `ReciprocalRankFusionPolicy`/`ReciprocalRankFusionOptions`,
available but not the default) — **Outcome (Tasks 1–3):** `IMemoryRankingPolicy` (`Lyntai.Memory.Ranking`) —
`Rank(candidates, context)`, set-based rather than per-candidate — plus `MultiplicativeRankingPolicy`, today's
formula ported verbatim (`Relevance × Retrievability × boost × HopAttenuation^hop`, then a relative floor) so
nothing about what a candidate scores changed, only where the computation lives and whether it is
swappable. `GraphMemoryEngine.RecallAsync` now resolves the policy from the container (`TryAddSingleton`,
so a consumer's own registration always wins) instead of hardcoding the formula; the three tunables —
`HopAttenuation`, `RelativeFloor`, `SalienceRankWeight` — moved off `GraphMemoryOptions` onto the policy's own
`MultiplicativeRankingOptions`, each now construction-guarded (previously silently accepted any value).

- **A ranking POLICY seam, not a single hardcoded formula constant.**

## Part 54 (item 1 of 5) — measure the two forgetting curves against a real corpus (2026-08-09)

✅ done 2026-08-09 (`feat/memory-corpus-harness`, Tasks 1–4 — the corpus-harness plan) — **Outcome:** a
deterministic corpus (`tests/Lyntai.Tests/Memory/Corpus/MemoryCorpus.cs`), two metrics
(`RecallQuality.MissRate`/`.PollutionRate`), and a four-arm `{MultiplicativeRankingPolicy,
ReciprocalRankFusionPolicy} × {HalfLifeRetrievability, DsrRetrievability}` sweep
(`bench/Lyntai.Benchmarks/MemoryPolicySweep.cs`, `node devtools/dev.mjs memory-sweep`) across a six-shape
grid, replayed against a live SQLite-backed `GraphMemoryEngine`, controlling for both confounds DSR1 named
plus a third found during implementation (the engine's default `BurstDampenedClock` keys off wall-clock time
and would have flattened the whole interference axis for a fast in-process replay — fixed with an undamped
`PerWriteClock`, the same substitution `MemoryDecaySimulationTests` uses for the same reason). **The
measurement is real and controlled, but the RESULT on the curve question is an honest null, not a

- **DSR1 — measure the two curves against a real corpus**

## Part 55 — memory ranking × forgetting policy measurement: findings recorded, no default changed (2026-08-09)

✅ done 2026-08-10 (`feat/dsr-default`, Tasks 1–4 — the DSR-default falsification plan) — **Outcome:** the
owner's decision is `docs/DECISIONS.md` — the `DsrRetrievability` default curve — `DsrRetrievability` ships as the registered 3.0 default
forgetting curve (FSRS's own external validation is the primary evidence, never this corpus); ranking stays
`MultiplicativeRankingPolicy`, unchanged, still on this library's own weak evidence. The curve question got
there by first RETARGETING the corpus into the band the two curves actually diverge in (Task 1 — fixed
delay constants replacing the old shape-scaled formula, a force-drain bug fix, `CriticalBudget` raised
12→240), which is what made a real measurement possible at all: the second-seed, intermediate-shape and
`hot-ephemeral`-delay probes this item asked for were effectively subsumed by that retarget plus Task 2's
30-seed paired sweep (seeds `12345`–`12374`, pilot SD 0.2092, implying N=35 for 80% power at a 0.10
`MissRate` difference — 30 was run, the shortfall disclosed rather than rounded away). **The targeted

- **Owner's decision: does either default change?**

## Part 57 — FSRS-A: per-review difficulty updates (2026-08-10)

✅ done 2026-08-10 (`feat/fsrs-properly`, Task 2, fix round 1) — **Outcome:** `MemoryDecayState`/`GraphNode`/
`GraphTouch` gain a `Difficulty` member (additive-source/binary-breaking); `DsrRetrievability.Reinforce` now
maintains it every review, deriving FSRS's rating from retrievability at recall — restricted to FSRS's
success sub-range, `grade = 2 + 2·r ∈ [2, 4]`, never the lapse rating; computed from the state BEFORE this
reinforcement (pinned and mutation-checked); bypassed on a same-day/zero-elapsed recall so a session burst
cannot pump it (also pinned and mutation-checked) — and adapts FSRS-5's `next_difficulty` law with FSRS-6's
own recalibrated constants, INCLUDING mean reversion (dropping it, as a first draft did, makes
`Difficulty = 10` absorbing; restored and mutation-checked). The three graph stores promote the signal into a
`difficulty` column with its OWN precedence, not salience's: an explicit write-time signal NAMING difficulty
wins (not merely a non-empty bag, which a fix-round review caught resetting a tracked value via an unrelated

- **FSRS-A — per-review DIFFICULTY updates.**

## Part 58 — RELEASE GATE: the memory subsystem's Postgres leg was unexercised for two sessions (2026-08-11) — CLOSED

**Closed 2026-08-11, and it caught a real defect on its first run** — which is the whole argument for having
filed it as a gate rather than a note.

## Part 60 — the guard scripts that gate this repository have no tests of their own (2026-08-11)

✅ done 2026-08-11 (`feat/close-gate-gaps`) — **Outcome:** `node --test` (no dependency, nothing added to any
`package.json`), 62 tests in `devtools/scripts/__tests__/`, wired as `node devtools/dev.mjs test-devtools` and
as the **first** step of `verify` — before the guards it covers, because everything after it is enforced BY
those scripts. Coverage in the priority order this item set: **check-sensitive** (19 — each built-in fires,
both Windows shapes, negative controls, the `local/sensitive-patterns.txt` mechanism incl. a bad regex not
disarming the rest, UTF-16 LE/BE/no-BOM decoding, binary skip, the ENOENT-vs-unreadable split, and end-to-end
over fixture repositories in both tree and staged mode — every value synthesized, never a real credential);
**check-docs** (13 — the three defects above are the first three tests, each **mutation-checked**: revert that
one fix and that one test goes red, confirmed for all three); **check-version-bump** (13 — every rule over
exact diff text plus a real staged fixture, and the `LYNTAI_RELEASE=1` hatch); **check-packages** (17 — one

- **Nothing tests `devtools/scripts/`.**

## Part 61 — no gate can see a stale PARAMETER NAME, and parameter names are frozen public API (2026-08-11)

✅ done 2026-08-11 (`feat/close-gate-gaps`) — **Outcome:** `devtools/scripts/check-api-vocabulary.mjs`, a new
gate scanning the committed API baselines (`tests/Lyntai.Tests/Api/Baselines/*.txt`) against its **own**
registry, `retiredApiNames` in `devtools/project.config.mjs`. Wired as `node devtools/dev.mjs
check-api-vocabulary` and into `verify` directly after `check-docs` — the two vocabulary gates now sit
together, one asking whether the PROSE still says what a decision settled and one asking it of the frozen
public SURFACE.

- **Nothing checks whether a public parameter name still matches the vocabulary its own decision settled.**

## Part 59 — no gate compiles the code samples in our own documentation (2026-08-11)

✅ done 2026-08-11 (`feat/close-gate-gaps`) — **Outcome:** `devtools/scripts/check-samples.mjs`, wired as
`node devtools/dev.mjs check-samples [--list]` and into `verify` directly after `check-api-vocabulary`. The
three documentation gates now sit together: `check-docs` on the prose, `check-api-vocabulary` on the frozen
surface, `check-samples` on the code a consumer copies. **Default-ON** — a block is compiled unless it
opts out; an opt-IN marker would make coverage whatever someone remembered to tag, which is the "checklist
in someone's head" failure `dotnet-package-layout.md` already names.

- **Nothing verifies that a documented code sample compiles.**

## Part 63 — `tests/` still speaks the vocabulary the `IMemory<Domain>Policy` naming shape retired, and one rename made it self-contradictory (2026-08-11)

✅ done 2026-08-11 (`feat/close-gate-gaps`) — **Outcome:** swept across 16 files. **33 test method names**
(the filed estimate of 38 counted the three shared `MemoryGraphStoreContract` facts once per backend caller;
they are three declarations with nine call sites), **all 7 helper types**, and **~100 comment / XML-doc
lines**. `212 insertions, 212 deletions` — exactly balanced, which is the arithmetic signature of a pure
rename: no line was added or removed, only rewritten.

- **Sweep `tests/` for the retired salience/retention vocabulary.**

## Part 54 (items DSR3 and DSR5 of 5) — the unguarded half of `DsrOptions`, and a per-engine forgetting curve (2026-08-11)

✅ done 2026-08-11 (`feat/dsr-guards-and-per-engine-curve`) — **Outcome:** both closed; 38 new test cases,
`verify` green on all eleven gates (2431 passed / 0 failed / 9 skipped, e2e 3/3 — 9 skips, so the Postgres
leg ran for real).

- **DSR3 — `MaxStability` is the one option path to a permanently PERSISTED poisoned stability.**
- **DSR5 — per-engine forgetting-policy selection is impossible.**

## Part 54 (items DSR2 and DSR4 of 5) — a ceiling that CUT instead of capping, and the untested connection axis (2026-08-11)

✅ done 2026-08-11 (`feat/dsr2-and-dsr4`) — **Outcome:** both closed; 7 new facts plus one existing contract
fact widened and one pathology fact inverted, `verify` green on all eleven gates (2438 passed / 0 failed /
**9 skipped** / 2447 total, e2e 3/3 — 9 skips, so the Postgres leg ran for real).

- **DSR2 — `Reinforce` can SHORTEN a memory whose stored stability exceeds `MaxStability`**
- **DSR4 — `Reinforce`'s connection/`Strength` axis is untested.**

## Part 53 (item 3 of 3) — a ranking score could overflow to `+Infinity` from FINITE inputs (2026-08-11)

✅ done 2026-08-11 — **Outcome:** a post-hoc `double.IsFinite(score)` filter, applied where the score is
computed rather than to what went into it, in **three** policies rather than the two the item named: an
overflowed score now drops its own candidate and nobody else's, and `best`/`floor` are computed over a set
that is finite by enforcement.

- **A ranking score can still overflow to `+Infinity` from FINITE inputs, and both policies' docs over-claim that it cannot.**

## Part 63 (residue item 1 of 2) — the 19 fixture STRING LITERALS the vocabulary sweep left behind (2026-08-11)

✅ done 2026-08-11 — **Outcome:** all 19 moved, content and assertion in the same edit, into the vocabulary
the surrounding prose had **already** been swept to: `judgement` / `judged` / `unjudged`. The literals were
the last holdouts of a word their own doc comments had stopped using — `Seeding_treats_a_below_neutral_salience_as_the_neutral_value`
already read *"a half-judged entry must order LEVEL with an unjudged one"* three lines above a fixture called
`"a half-appraised note"`. Mapping: `a non-finite appraisal` → `a non-finite judgement` (9),
`reappraised fact` → `rejudged fact` (6, which also carries the two `a reappraised fact` sites),
`a half-appraised note` → `a half-judged note` (2), `an appraised row` → `a judged row` (2).

- **19 fixture STRING LITERALS in `tests/` still say `appraisal`/`appraised`.**

## Part 63 (residue, `src/` sites) — the 12 `apprais*` comment and XML-doc sites (2026-08-11)

- **`src/` has 12 surviving `apprais*` sites, all in comments and XML docs.**

## Part 63 (residue item 2 of 2) — `clock` still named AGE POLICIES in `tests/` (2026-08-11)

✅ done 2026-08-11 — **Outcome:** **42** `clock` occurrences moved across 7 files (39 changed lines).
`MemoryAgePolicyTests.cs`'s locals became `policy` (every one of them constructs an `IMemoryAgePolicy`, never
a `Func<DateTimeOffset>`), the four class docs' *"an undamped per-write clock"* became *"an undamped per-write
age policy"*, and the scattered prose sites moved with them. One test method renamed:
`Damping_composes_over_whichever_clock_was_chosen` → `..._whichever_age_policy_was_chosen`; checked for
quotes elsewhere first and found none in any tracked file.

- **`clock` still names AGE POLICIES in `tests/` prose and locals**

## Part 56 (item 3 of 3) — the measuring corpus's own filler competed with the entries it was measuring (2026-08-11)

✅ done 2026-08-11 — **Outcome:** taken as the **test-corpus** fix, the second of the two options the item
itself named and the one its own parenthesis preferred. `MemoryCorpus.WriteFiller` now writes
`"padding filler{n} …"` in place of `"item filler{n} …"`. **`FtsQuery.Build` is untouched** — this was a flaw
in the measuring instrument, not in the product, and changing the library's tokenizer to accommodate a test
corpus would have been the tail wagging the dog.

- **A handful of early corpus entries are never recalled at all for any of their own relevant queries — outranked by the corpus's own filler padding, a corpus/ranking interaction sh...**

## Part 67 — the multilingual memory work: four languages measured, the cluster gap closed, and objective (1) found broken (2026-08-13)

- **CLOSED 2026-08-12 — the corpus has a LANGUAGE axis and Chinese is measured, not just pinned.** — `CorpusLanguage` + `CorpusLexicon` (templates AND readers together, so a language cannot be half-added); `memory-language` sweep; goldens prove Eng...
- **CLOSED 2026-08-13 — two-character CJK terms are matched on the substring path, two-phase.** — most Chinese words are two characters, so this is the common case, not a corner. The first two cost measurements were both wrong (a 20k table too s...
- **CLOSED 2026-08-13 — Japanese was fixed by SCRIPT-RUN SEGMENTATION, with no model involved.** — `SearchTerms.ScriptRuns` + `ScriptProfile`; digits and punctuation are neutral so `第3轮` and `重复0` do not shatter. Superseded the two entries below,...
- **Superseded — the record of what was measured before the segmentation fix:** — the options considered at the time (morphological segmentation, a longer kana n-gram, or documenting the loss) were ALL wrong; the cause was upstre...
- **Superseded — JAPANESE IS A RANKING FAILURE, NOT A GATHER FAILURE, and the cluster case is the exact opposite (2026-08-13).** — the cluster half held and drove the annotation work. **The Japanese half was MISLEADING and that is why it is kept**: it was a correct measurement...
- **CLOSED — co-activation cannot link an entity cluster, in either language, and "strengthening" it was a trap (2026-08-13).** — the option the owner had CHOSEN ("strengthen co-activation") was shown unable to work before any of it was built, and that was reported rather than...
- **CLOSED — the graph's contribution does not transfer to Chinese (2026-08-12).** — the leading hypothesis recorded at the time (co-activation windows crowded out by a wider Chinese candidate set) was WRONG: the edge census showed...
- **CLOSED 2026-08-13 — SUBJECT ANNOTATION TAKES CLUSTER RECALL TO ZERO IN CHINESE.** — `IMemoryAnnotationPolicy` + `LlmMemoryAnnotationPolicy` + `AddMemoryAnnotation` + `UseGraph(annotation:)`, over a durable subject index (`RecordSub...
- ...and 4 more closed items (full text in git history).

## Part 68 — the final pre-freeze research sweep: closing what 3.0 must not defer (2026-08-13)

✅ done 2026-08-13 — **both items closed, and the second one REFUTES this Part's own headline.**
`docs/DECISIONS.md` — the effects-not-acts reinforcement seam (the effect seam) and the unverified-signal reinforcement rule (the act seam); `MemoryReinforcementActTests`.
<br>The seam question closed first because it was freeze-gated: `GraphMemoryOptions.Reinforcement` cuts at
the two EFFECTS (age reset vs stability growth), and `GraphMemoryOptions.ReinforceOn` then cut the two ACTS
(recall vs expansion) as a separate, composing type — which is exactly the relationship the effects-not-acts reinforcement seam predicted when it
argued the act question "composes rather than competes".
<br>**The measurement, over `ExpandRatio = 3`, English, seed 909, 20 expansions replayed:** expansion-only
beats the default on BOTH miss and pollution in BOTH growth configurations (0.4429/0.1056 vs 0.5786/0.1878
with growth on; 0.4214/0.2301 vs 0.5357/0.3331 with the shipped growth-free setting). `both` and `recall
only` land in the same place, so essentially all of the shipped configuration's cost comes from reinforcing

- **`consumer-smoke` is still untested**
- **Part 53** — 's remaining items — memory retention (Plan 2): salience's ranking effect is still UNMEASURED (2026-08-09)
- **Part 64** — reinforcement may be NET-HARMFUL to recall quality, and nothing currently rules it out (2026-08-12)
- **Part 66** — what the 3.0 pre-freeze sweep left open (2026-08-12)
- **Part 62** — the guard tests cover four scripts; the rest of `devtools/` is still untested (2026-08-11)

## Part 71 — four closed records that were left in the OPEN backlog (2026-08-14)

_Moved out of `TASKS.md` by the 3.0 pre-freeze documentation sweep. Each was already CLOSED and each was
still sitting under an open Part, which is the defect `.claude/rules/task-lifecycle.md` names: a backlog
that accumulates finished work stops answering the one question it exists for. Nothing here is new — the
text is moved verbatim, so the record of how each was closed is unchanged._
- **Part 69** — ’s two non-embedder items, built the day they were filed (2026-08-13)

## Part 73 — a COUNT in prose is this repository's most-repeated drift, and nothing derives one (2026-08-14)

_**CLOSED 2026-08-15** by building the gate. The open question the entry posed — DERIVE the counts or GATE
them — was decided in favour of GATING: `CLAUDE.md` is hand-written prose a session reads first, and a
generated block inside it would be a new kind of thing this repository does not have, while a curated
registry is a shape the repo already trusts (`retiredTerms`, `retiredApiNames`,
`staleReferenceAllowances`). Shipped as `devtools/dev.mjs check-counts`, the fourteenth `verify` gate,
beside its two twins._

## Part 72 — `check-links` scans markdown only, and the defect it was built for was alive in the code tiers (2026-08-14)

_**CLOSED 2026-08-15.** Decided in favour of widening, with the scope drawn at the TARGET rather than the
comment style: comment lines only, `docs/` targets only. `local/` stays skipped (untracked by design) and
source paths stay unchecked, because `pitfalls.md` records an all-paths existence check over prose
returning ~45 hits and zero defects — source files are renamed for legitimate reasons, documents moving is
the defect._

## Part 70 — the cross-backend contract guard is blind in one direction (2026-08-14)

_**CLOSED 2026-08-15.** All three backends now drive `MemoryGraphStoreContract` from one reflection-fed
theory source (`MemoryGraphStoreFacts.Names`), so exhaustiveness holds BY CONSTRUCTION and the hand-bumped
`covered` literal is gone. The per-fact test name survives as the theory argument._

## Part 69 — the embedder costs recall quality on this corpus, and nothing yet says whether that generalizes (2026-08-13)

_**CLOSED 2026-08-15, both items.**_

## Part 74 — the pre-3.0 whole-library review (2026-08-15)

✅ done 2026-08-15 — Not a `TASKS.md` task; the archive records it as the fourth consolidation review, after
Part 8 ("generic + sustainable"), Part 20 (foundation hardening) and Part 21 (the 1.0 API sign-off). Scope
settled with the owner before any code moved: **whole library, breaks on the table, the Part 20 shape,
stopping at release-ready rather than cutting 3.0.**

## Part 76 — `local/` is now genuinely untracked, and the document describing it overclaimed (2026-08-16)

_Split out of `TASKS.md` Part 75 on closure — that Part stays open for the five items this did not touch, so
this gets its own number rather than colliding with the still-open one (`.claude/skills/archive-task`'s rule:
an open Part N and an archived Part N are never the same N)._

## Part 77 — the two generation items that were never blocked on a key (2026-08-16)

_Not from `TASKS.md`. Both were sitting INSIDE Part 33, which is marked blocked on a fal.ai key and a ~1.7 GB
model download — and neither of these needed either. That is the finding, and it is now a caveat on the
backlog's own banner: **a Part is blocked when its DELIVERABLE is, which does not make every sentence in it
blocked.**_

## Part 78 — memory can be removed by the blend people actually use (2026-08-16)

_Closes `TASKS.md` Part 75's "make the remaining memory engines forgettable". `docs/DECISIONS.md` **D72**._

## Part 79 — the three §9 leftovers, and what re-reading a deferral is worth (2026-08-16)

_Not from `TASKS.md` — from `docs/ROADMAP.md`'s standing §9 list, taken up on the owner's "all three".
`docs/DECISIONS.md` **D71**, **D73**, **D74**._

## Part 80 — the memory corpus can finally SEE headline search (2026-08-16)

_Closes `TASKS.md` Part 75's "widen the memory corpus to author headlines disjoint from content"._

## Part 81 — a guard Block: forced asymmetry, accidental silence (2026-08-16)

_Closes the last startable item in `TASKS.md` Part 75. `docs/DECISIONS.md` **D75**._

## Part 82 — a whole-library review: dedup, then the comment problem it exposed (2026-08-16)

**Outcome.** Storage-pair identical code lines 867 → 730 and private row types 23 → 5 across the two
backends; every public-surface change purely additive (the API baseline gained 157 lines and lost none, so
nothing a consumer compiled against moved). Four measured baselines in `CLAUDE.md` re-measured. `verify`
15/15; suite 3022 passed / 3043 with 21 skipped, which is the count that says Docker was up and the whole
Postgres leg actually ran.

## Part 83 — the adversarial re-check, and the three findings it unblocked (2026-08-16)

_Not from `TASKS.md` — the continuation of Part 82, after its own conclusion was challenged._
`docs/DECISIONS.md` **D82**, D30 and D36 amended, two `FIXES.md` incidents, two `pitfalls.md` entries.

## Part 84 — the daoris references come out, and the stale index they were blocking is rebuilt (2026-08-17)

_Not from `TASKS.md` as an item of its own — the owner asked for it directly. It CLOSES the one open
backlog entry that was waiting on a tool run._

## Part 85 — the subsystems the review had not reached (2026-08-17)

_Not from `TASKS.md` — the tail of the whole-library review, aimed at the areas no earlier pass had
touched: durable jobs, the secret vault, the generation backends and the MCP surface._

## Part 86 — the pre-3.0 release reconcile: what the gates structurally cannot see (2026-08-17)

_Not from `TASKS.md` — asked as "what is left for 3.0, we want a good version with no unfinished dev work".
Every gate was already green (`verify` 15/15, 3057/3078 with 21 skips and Docker up so the Postgres leg
really ran, `consumer-smoke` clean, `decisions-index` current), so this pass is entirely about the class of
defect no gate covers. **v2.5.0 needed exactly the same pass** (commit 7ebbd0e, "reconcile the
state-describing docs with what actually shipped"), for the reason its own message gives: `check-docs` gates
VOCABULARY, not ACCURACY, so a document that quietly stops being true survives everything._

## Part 87 — the last three startable items, and the three defects closing them found (2026-08-17)

_`TASKS.md` §Startable, opened by Parts 85 and 86 and closed here on the owner's instruction that there is no
reason left to defer anything to 3.1. **Each item was coverage work; each uncovered a real defect.** That is
the argument for the whole shape, so it is stated first: a contract is not paperwork over code that already
works — writing three of them found three things that did not._

## Part 88 — the whole-repo review's twelve verified findings, fixed (2026-08-17)

_Opened and closed the same day. The review: nine parallel subsystem reviewers over every `src/` file plus
devtools and the test infrastructure, 13 candidates raised, one retracted by its own finder, and each
survivor independently re-verified against the code with a confidence score (85–55). All twelve fixed
TDD-first — every behavioural fix watched its test fail for the recorded reason before the change landed.
The per-incident records are `docs/FIXES.md` (nine entries dated 2026-08-17); the consumer-facing lines are
`CHANGELOG.md` `## Unreleased`._

## Part 89 — FROM AN ADOPTER: two seams that forced a bespoke `IMemoryEngine` (2026-08-20)

✅ done 2026-08-21 — **Outcome: all three asks answered with the FIRST branch of their two-sided acceptance,
additively.** (A) `MemoryComposition.Render(basePrompt, items, options)` is public and `ComposeAsync` now
calls it, so the two cannot diverge; it takes `IReadOnlyList<MemoryItem>` because composition reads nothing
else off a recall, and the missing sentence about the reserve is on
`MemoryCompositionOptions.AuthoritativeCharacters` (**D83**). (B) `CuratedMemoryEngine.Grade` is an optional
read-path `Func<CuratedMemory, MemoryGrade>` — a delegate rather than a reserved metadata key, because which
key carries provenance is the deployment's convention — and `Supported` deliberately does NOT widen, since
widening would route associative writes to a store with no grade column to mark them with; the adjacent
`kind: null` whole-catalog read shipped with it, read-only and loud about why (**D84**). (C) answered by a
wiring check rather than by teaching a second engine to judge: `MemoryWiring` reports an

## Part 90 — FROM AN ADOPTER: semantic memory is unreachable on a scope-OPTIONAL recall, and a composite hides it both ways (2026-08-21)

✅ done 2026-08-21 — **Outcome: both halves closed, and B got BOTH of its asks because neither alone is
enough.** (A) `ISemanticMemory.RecallAsync`'s `scope` is `string?` and null means "every scope of this task",
bounded by `k`, with `SemanticHit.Scope` naming where each hit came from; `SemanticMemoryEngine` passes a
null `MemoryQuery.Scope` through. Shape 2 from the item's own list, made ADDITIVE by putting the enumeration
on its own optional interface — `IListableVectorStore : IVectorStore`, implemented by all three shipped
stores — so no BYO store gains a required member and one that does not implement it yields nothing, exactly
as before. A default interface implementation was refused: it would have made a store that CANNOT enumerate
indistinguishable from one that holds nothing, which is the silent shape this whole part is about (**D86**).
Forgetting stays scope-mandatory, because an enumeration that misses a scope costs a recall some hits and
costs a consent withdrawal its whole promise.

## Part 91 — FROM AN ADOPTER: 3.0.1 confirmed, and one small thing (2026-08-21)

✅ done 2026-08-21 — **Outcome: took the FIRST branch, not the cheap one.** The ask offered "skip the
separator" or "document that a non-empty prompt is expected", and the second costs nothing — but
`Render` exists *for* callers doing their own retrieval, so documenting the friction would have shipped the
consumer's workaround as the contract. The blank line is a SEPARATOR; with no prompt there is nothing to
separate from, so it is not emitted. Both uses are now stated as first-class on the method's own doc.
<br>**Both heading sites go through one local `Separate()`**, because a fix applied to the authoritative site
alone leaves the associative-only recall — the COMMON case — still leading with the separator. That is pinned
as its own fact rather than folded into the first: two call sites is two chances to fix one of them.
<br>A base prompt is still passed through verbatim, whitespace included. Trimming it would have made this
path disagree with the no-items early return about the same input, which is the kind of inconsistency that

## Part 92 — FROM AN ADOPTER: an embedder is registered, every write pays for it, and no recall reads it (2026-08-21)

✅ done 2026-08-21 — **Outcome: both items taken, and the adopter's own framing of item 1 was right — it is
the first finding that needs no hedge.** The three existing ones go silent the moment a member is an engine
this library does not recognise, because a BYO engine may consult those seams; this one reads two
registrations and one options value **off the engine itself**, via an internal
`GraphMemoryEngine.EmbedsWithoutSeeding`. A property rather than reflection, because `pitfalls.md` records
that a reflected name is a rename site the compiler cannot see, and this lives in `src` rather than in a
bench nobody runs on a schedule (**D85**, amended).
<br>Item 2 SPANNED rather than documented, through the same `IListableVectorStore` Part 90 added, merged and
bounded by `SemanticSeedK` with ties broken by id — those scores become RANKING input, so an untiebroken
merge writes run-to-run arbitrariness into what a recall returns (**D86**, amended). Documenting it would

## Part 93 — FROM AN ADOPTER: a named client cannot route when `DefaultCandidates` names a backend outside its own pool (2026-08-21)

✅ done 2026-08-23 — **Outcome: the adopter's option (1) taken, but on a wider rule than they proposed, plus
their option (2) for the case no rule can derive** (`docs/DECISIONS.md` **D87**).

- **`AddLlmClient(name, c => c.UseProviders("x"))` is unusable whenever `UseDefaultCandidates` pins ids that are not in `x`.**

## Part 94 — FROM AN ADOPTER: subject handles are WRITE-ONLY — recorded, paid for, and reachable by no recall (2026-08-22)

✅ done 2026-08-23 — **Outcome: taken as filed, including the part the report was most careful about — the
default is NOT 0** (`docs/DECISIONS.md` **D88**).

- **Nothing in the engine ever searches a subject.**

## Part 95 — FROM AN ADOPTER: with `VerificationFilters` off, what a verdict DOES to the ranking is undocumented and easy to get backwards (2026-08-22, corrected 2026-08-23)

✅ done 2026-08-23 — **Outcome: both items documented — and the CORRECTED report was still wrong about the
mechanism, which is the finding worth keeping.**

- **Say what a verdict does when `VerificationFilters` is false**
- **A benchmarking note worth shipping, because recall MUTATES.**

## Part 96 — six gates are blind to a file that is not yet committed (2026-08-23)

✅ done 2026-08-23 — **Outcome: closed by hoisting, and the Part's own inventory was wrong in a way worth
recording.**

- **Give the remaining gates the same file list.**

## Part 97 — the memory proposal, assessed then executed: Phase 1 closed, Phase 2 measured, three defects found (2026-08-26)

**Outcome.** Phase 1 closed, Phase 2 measured, three defects found (two of them introduced during this
work), `verify` 15/15 throughout. The prototypes named their own price: `MemoryItem.Metadata`, a metadata
predicate on `SeedAsync`, and the vector collection address — all additive, none shipped, because the
proposal's own PR5 bar is two applications needing the same field and that has not happened.

- **Assess the proposal against the tree before building anything.**
- **A real defect, found by the research rather than by a consumer**
- **…and a second defect inside the fix for the first**
- **Phase 1's invariants, all of them.**
- **`memory-scale`**
- **The harnesses take any OpenAI-compatible endpoint**
- **`Metadata` pinned, and it is WRITE-ONCE**
- **A bitemporal resolver prototype**
- ...and 4 more closed items (full text in git history).

## Part 98 — the `many-candidates` salience regression, closed by moving the shipped default (2026-08-23)

**Outcome (2026-08-23).** `ReciprocalRankFusionOptions.SalienceWeight` ships at `0` — salience does not
vote on ranking — on a third run across two embedding models × five languages × five shapes × 10 seeds.
**D45 had reasoned this without a measurement**, and the item is closed not by finding the bounded-admission
rule it was opened to find but by establishing there was no gain for a bound to protect.

## Part 100 — `taskKey` isolation, asserted as a property instead of assumed (2026-08-26)

- **Cross-tenant leakage had no dedicated surface.**

## Part 101 — `MemoryGrade.Inherit` on a re-remember inherits from the ENTRY (2026-08-26)

- **DECIDE what `MemoryGrade.Inherit` means on a RE-REMEMBER — it downgraded.**

## Part 102 — what a RE-REMEMBER overwrites, all four rules (2026-08-26)

- **`Silent overwrite = 0` was settled for METADATA and not for content.**

## Part 103 — a `taskKey` is a real boundary, not a boundary-except-along-edges (2026-08-26)

- **DECIDE whether `LinkAsync` may assert a link ACROSS tasks.**

## Part 104 — gist support: RAW support is refuted, so the planned two-armed seam does not survive (2026-08-27, closed 2026-08-28)

**Outcome:** re-planned, and the answer was that there is nothing to plan a seam around — support was two
quantities under one name, so `IMemorySupportPolicy` is never written (`docs/DECISIONS.md` **D94**).

- **Re-plan the gist tier's support rule around ONE candidate, not a two-armed seam.**

## Part 107 — a maintained document cites a section that does not exist, and no gate can see it (2026-08-28)

✅ done 2026-08-28 — Both halves closed. All seven citations repointed to §7 (`CLAUDE.md`, `devtools/dev.mjs`,
`docs/task-archive.md`, `docs/superpowers/INDEX.md` ×2, `bench/Lyntai.Benchmarks/{Program,MemoryScaleSweep}.cs`),
and the numbering hole deliberately LEFT — see below. The gate question was **measured, then answered yes**:
`check-links` grew a third half (`ANCHOR_PATTERN`, `declaredAnchors`, `unresolvedAnchor`), 13 new guard-script
tests, 401 → 416.

- **`docs/memory.md` has no `## 8`, and SEVEN citations across six files name one.**

## Part 106 — the working tree is CRLF against an LF index, and nothing in the repo says so (2026-08-28)

✅ done 2026-08-28 — The convention is declared in a tracked `.gitattributes` (`* text=auto eol=lf`,
`docs/DECISIONS.md` **D95**) and the working tree refreshed: `git ls-files --eol` went from 656 `i/lf w/crlf`
/ 171 `w/lf` / 3 `w/mixed` to **830 of 830 `i/lf w/lf`**. **The renormalize commit this entry planned around
does not exist.** Every tracked file already read `i/lf` and none is binary, so with the attributes in place
`git add --renormalize .` staged ZERO files — the commit is one new file, and only the working tree needed
refreshing, which is not a commit. That measurement removed the entry's own stated reason for deferral ("it
wants its own change… that commit buries anything landing beside it"). Three CRLF passages moved from
standing hazard to closed (`pitfalls.md` ×3, plus `windows-machine.md`, `repo-mechanics.md` and
`RULES_INDEX.md` ×2), a fourth `pitfalls.md` entry was added for the trap below, and the `archive-task`
skill was corrected to the compressed-archive convention it still contradicted.

**Four claims about git's behaviour were written into the records from inference and then refuted by
running one command each** — believed first, measured second:

| believed | measured |
|---|---|
| `git checkout-index -a -f` rewrites an up-to-date file | it is a silent **no-op**; delete the file first and it writes |
| a stray CRLF working file is invisible to `git status` | it reports ` M` |
| it never heals on `git checkout --` | it heals **while the stat cache is busted**; after a `git add` it is skipped and persists |
| `eol=lf` is redundant with `text=auto` here | it is not — under `core.autocrlf=true` they check out CRLF and LF respectively, which is the decision's actual justification |

**Every one was plausible, cheap to test, and wrong**, and three of the four were caught only by re-reading
prose that had already been written. D95 carries the measured versions; the reusable form is in
`pitfalls.md`.

- **Decide the line-ending convention and commit it as `.gitattributes`.**

## Part 108 — gist support: the cardinality axis, and the threshold it refuted (2026-08-28)

✅ done 2026-08-28 — `RoutineCount` is now a fourth axis on `MemoryGistSupportSweep`'s grid: **2400 replays**
(60 shapes × 4 rungs × 5 seeds × 2 injected clocks), all seven controls holding 2400/2400. Rungs 3/5/8/12 give
|A|/|B| of 2.00/4.00/3.00/2.00, with **two rungs at ratio 2.00 and different sizes** so a moving result is
attributable to the ratio rather than to |A| growing. Tables in `docs/memory-measurements.md` §5; **D94**'s "honest limit"
paragraph is now a measured result.

**The finding is negative and it removes the last candidate.** D94 named θ = 0.1 and θ = 0.9 as the two
degenerate-but-pacing-independent thresholds; cardinality splits them. **θ = 0.1 is invariant on both axes**
(phase A on all 2400 replays, both clocks, both curves) and is the raw count, which the corpus declares wrong
for the assistant host. **θ = 0.9 is not invariant** — it walks tie → A → B → B across the ratio, the
order-statistic behaviour D94 predicted and could not test. `count@0.8` flips too, which re-reads the
`tie 115` cell the 600-replay run had called merely non-unanimous as a cardinality boundary. `ConnectionBoost
= 0` isolates the mechanism: θ = 0.9 is B 300/300 at every rung with the boost off, so the connection term
lifting phase A's most-connected members over 0.9 is what creates the dependence.

**A control caught the axis before the axis caught anything**, which is the instrument half worth carrying.
C5 asserted "both regimes fully enumerated (8/4)" — the `RoutineCount = 12` split as a LITERAL — so the first
run failed it on 1800 of 2400 cells and refused to publish. It was asserting the very constant the new axis
exists to vary; it is now a function of each cell's own shape (**D60**'s rule). Both instrument defects the
Part named were fixed too: the 60-shape grid is hoisted to `CorpusGrid` in `MemoryCorpus.cs` so the sweep and
`MemoryCorpusTests` share ONE definition, and the `After` snapshot now brackets the final routine query
exactly rather than being taken once the timeline runs out.

**And a pooled control nearly hid the whole finding**: the `ConnectionBoost = 0` comparison reports "verdict
did NOT move" on both clocks, which is true of the ARGMAX while the per-rung distributions differ completely.
A control comparing pooled verdicts cannot see a split that cancels in the pool.

- **Sweep `RoutineCount`, because the question is about cardinality and the run held it at 12.**

## Part 110 — the first measurement on an instrument this repository did not build (2026-08-29)

✅ done 2026-08-29 — `node devtools/dev.mjs memory-locomo` ingests LoCoMo (10 conversations, 5882 turns) and
scores **evidence-hit@k, model-free**: the benchmark names the evidence turn by dialogue id, so it is
checkable with no reader and no judge. Result on 200 stratified questions: shipped defaults **11.0%**,
`SemanticSeedK = 20` **11.0%**, `+ RetrievabilityWeight = 0` **22.5%**, plain cosine at the same k
**80.5%**. Every arm returned a full 20 items, so it is ranking the wrong 20, not filtering. Tables and
scope in `docs/memory-measurements.md` §5; what it left open became `docs/task-archive.md` Part 233.

**The finding is that this engine's ranking defaults are built for a workload LoCoMo deliberately is not.**
`RelevanceWeight` and `RetrievabilityWeight` both ship at 1, so a recall weighs how-reachable equally with
how-relevant; LoCoMo spreads its questions evenly over the whole history, so recency is actively wrong
there. The synthetic corpus cannot see this because its relevance is recency-correlated by construction —
the two signals never disagree in it.

**Two harness defects were caught BEFORE publishing, and each would have produced a wrong headline.** The
first arm ran `SemanticSeedK = 0` — the shipped default, which recalls lexically — against a cosine
baseline, which is benchmarking a misconfiguration rather than a system. The second was sharper: the arms
shared a store, and **a recall reinforces what it returns**, so adding a fourth arm moved `lyntai` from
10.0% to 5.5% with the seed and the data unchanged. Same-seed drift is the tell that arms are not
independent. Each arm now ingests into a pristine store; `MemoryReinforcementEffects.None` would have been
cheaper and was refused because its own doc calls it the worst arm for recall quality, so it would have
biased the comparison toward this library.

**Also landed in the same pass:** a literature comparison against the 2026 field (`docs/memory-measurements.md` §5) —
only MemoryBank models decay with a curve at all and nobody surveyed uses FSRS or a power law; nothing
surveyed measures age in interference rather than elapsed time; and the newest work reaches this session's
own salience conclusion from the other direction, calling static single-signal importance "mis-specified"
and replacing it with seven learned-weight factors, one of which is usage history that this engine already
records and salience does not read.

- **Build a LoCoMo harness so this library has a number against the field's own benchmark.**

## Part 111 — a candidate nobody scored used to outrank every candidate that was (2026-08-29)

✅ done 2026-08-29 — **D97**. Both row projections materialized every node with `Relevance = 1`, the
MAXIMUM, and only `SeedAsync` overwrote it — so every graph-walk candidate, and every semantic or subject
seed fetched by id, outranked everything that had actually been scored. `GraphNode` now carries
`bool? Matched` (default `true`); an unscored read reports `Relevance 0` with `Matched null`, and
`MultiplicativeRankingPolicy` omits the relevance factor rather than multiplying by it.

**Measured on LoCoMo** (`docs/memory-measurements.md` §5), evidence-hit@20: defaults **11.0% → 31.0%**, `SemanticSeedK`
**11.0% → 36.0%**, `+ RetrievabilityWeight = 0` **22.5% → 63.5%**, with the cosine control unmoved at 80.5%.
`SemanticSeedK` becomes worth **+5.0 points** where it was worth exactly 0.0 — a real 0.785 cosine could
never beat a fabricated 1.000, so the option was unreachable rather than weak.

**The obvious fix was tried first and REFUSED, which is the part worth carrying.** Reporting `0` alone gets
byte-identical retrieval numbers and deletes graph traversal: a multiplicative policy scores a product, so a
zero annihilates a candidate instead of ranking it low, and `GraphMemoryRankingGoldenTests`' walked entries
vanished from the result rather than moving down it. It also broke a recorded finding — the "model-free
ranking has no headroom" conclusion, whose indistinguishability turned out to be an artifact of relevance
being a constant. D97 keeps both green because it is narrower: seeded nodes are untouched.

**The default is `true` rather than `null`, and a failing test chose that direction.** `null` as the default
silently stripped the relevance factor from every hand-constructed node, including any BYO store unaware of
the member; two multiplicative tests caught it within one run.

**Five hypotheses were refuted by reading the code before a control found this** — a misconfigured
`SemanticSeedK`, cross-arm contamination, unstored vectors, a wrong collection name, unparseable ids. The
control that settled it printed one line: 369 of 369 vectors stored, collection name right, top-20 returned,
all ids parsing. The seeds arrived and lost. `pitfalls.md` carries the reusable form — an incommensurable
score moves a result by *exactly* zero, where a merely weak one moves it a little.

- **Why does `SemanticSeedK` change nothing, and what should an unmeasured relevance be?**

## Part 112 — the haystack run, and the cheap variant was biased BOTH ways (2026-08-29)

✅ done 2026-08-29 — `node devtools/dev.mjs memory-longmemeval --haystack` reads `longmemeval_s` instead of
the oracle file, putting the same questions among ~490 turns of distractors. Both classes ran in FULL — 70
knowledge-update (34,242 turns per arm) and 132 temporal-reasoning (64,911) — so nothing is sampled and the
`--n`/`--seed` sampling built for it went unused. Tables in `docs/memory-measurements.md` §5.

**The knowledge-update win survives at +40.0**, and what moved is suppression rather than retrieval.
**The temporal result REVERSED** — −4.6 on the oracle becomes +3.8 on the haystack — which is the finding:
where there is finally something to suppress, suppressing it stops being a cost even in the class built to
penalise it. Read it as "the cost is gone" rather than "this engine wins temporal"; +3.8 on 132 questions is
five questions, and what carries it is that all three columns agree.

**So the oracle is not a cheap unbiased proxy — it is biased PER CLASS and the sign is not predictable.**
The caveat that filed this item guessed it flattered both; it flattered one by 9.8 points and penalised the
other by 8.4, enough to invert a sign. The mechanism is one number nobody had looked at: at `k = 10` the
oracle returns 40% of its store, so it barely tests retrieval at all. **This is why `--haystack` is the
variant to run**, at ~40× the ingestion cost.

**A latent loader defect the haystack exposed** — a rule that was right BY ACCIDENT on oracle data and found
nothing on the haystack — is written up in `docs/memory-measurements.md` §5 with its two controls, including the
byte-identical fourteen-cell re-run that proves the fix moved no published number.

- **Extend `memory-longmemeval` past the knowledge-update class.**

## Part 113 — the twenty slots are spent on the right candidates; the gap is the DESIGN (2026-08-29)

✅ done 2026-08-29 — closed by the second LoCoMo ladder. Tables in `docs/memory-measurements.md` §5; every arm holds
`RetrievabilityWeight` at its shipped default, so nothing here measures the engine with forgetting switched
off. **The absolute levels this ran on were superseded by Part 118's contamination fix** — read the ladder
there, not from any figure quoted in this entry's original form.

**Both misallocation hypotheses are refuted, in the opposite direction to the guess.** Graph traversal is
CARRYING the arm rather than stealing slots (`HopWeight = 0` costs 24.5 points), and MORE semantic seeds make
it worse — **D82** behaving as documented, since RRF ranks by competition and widening one signal re-ranks
every candidate within it.

**The pre-committed fallback is refuted by CONSTRUCTION rather than by another run.** The item said a plateau
would mean the evidence never enters the pool; `+sem80` seeds the top-80 by cosine, which contains cosine's
top-20 by construction, and that top-20 holds the evidence 80.5% of the time. So the pool holds it while the
arm loses about fifty points ranking candidates that were present — settled by a containment argument over
the same embedder and index, not by instrumenting the pool.

**So the boundary is located, and that is the deliverable.** What remains is the DESIGN: the signal demoting
those candidates is retrievability, every knob that closes the gap turns forgetting down, and the two that
leave forgetting alone both made things worse. LoCoMo's role here is a **differential instrument, not a
scoreboard**.

- **Spend the twenty slots better, WITHOUT turning off forgetting.**

## Part 114 — the decay model got BETTER under distractors and the fusion threw it away (2026-08-29)

✅ done 2026-08-29 — chasing the one row of Part 112's table that did not fit: `stale@k` ROSE (54.3 → 62.9)
while twenty times more candidates competed for the same ten slots. `memory-longmemeval --ranks` installs a
probe `IMemoryRankingPolicy` that observes the candidate pool and delegates the real ranking untouched.
**No library change** — `MemoryCandidate` already exposes `Retrievability`, `Hop` and the whole pool, which
is the seam doing its job. Tables in `docs/memory-measurements.md` §5.

**The answer inverts the question.** Under distractors the decay model separates the pair **3.6× better** by
value (0.0347 → 0.1235) and **2.6× better** by rank (10 → 26 positions) — and the score separation RRF
actually sums fell **29%** (0.00179 → 0.00127). `1/(K + rank)` is convex, so the same gap is worth far less
at ranks 74/102 than at 10/20, and distractors written after the current fact push both there. **The loss is
in the fusion, not the forgetting**, which is the opposite of what a decay-model regression would look like.

**`K` selects a REGIME, and the intuitive fix is backwards.** A LOWER K makes suppression monotonically
worse in both variants, because at low K being top-few on ONE signal beats being mediocre on the rest, and
the stale fact is relevance rank 4; at high K the order tends to the SUM of ranks (Borda), rewarding being
good on all of them.

**The haystack is what BOUNDS the lever, and the oracle would have hidden it.** Past 120 the variants
disagree — the oracle saturates harmlessly through K = 1000 while the haystack pays −16.7 points of
`current@k` at K = 300. Read on the cheap variant alone, K = 1000 looks free. Part 112's finding recurring on
a second question.

**Two controls, and the first caught a defect that would have shipped a wrong table** — the offline replica
must reproduce the SHIPPED policy's own top-10, and did not at first because it broke score ties the wrong
way round. That is now a rule in `pitfalls.md`. The second control is that the ladder's K = 60 row reproduces
the arm's own published numbers on the same sample exactly.

**Left open as `docs/task-archive.md` Part 233**: `K` is global, every LoCoMo figure was measured at 60, and
one class of one benchmark is not a mandate to move a published constant.

- *(No backlog entry — this came from a measurement, not a plan.)*

## Part 115 — the QA half, the shot curve, and a defect where forgetting had no vote (2026-08-29)

✅ done 2026-08-29 — the QA half of `docs/task-archive.md` Part 233 ran, and it grew a second half nobody
had asked for because the first one measured the wrong mode. Tables in `docs/memory-measurements.md` §5.

**The QA half, on Mem0's own benchmark.** LoCoMo, 100 questions, local reader, token-F1 primary and the LLM
judge beside it: `lyntai` 20.3%, `lyntai-2shot` 22.5%, `vector` 45.8%, `vector-40` 49.8%. Grading is now
MODEL-FREE first — a model is not better at exact comparison, and this judge is the same 4B model that wrote
the answer. The judge is uniformly more generous (36.0% against 20.3%) and preserves the ordering, so it
changes no conclusion.

**The reframing: a single top-k is not the mode this engine is built for.** A recall returns HEADLINES
because associative content is withheld until expansion, and `ExpandAsync` reinforces what it walks. So
`--shots` measures the walk. On LongMemEval knowledge-update (haystack) shot-1 delivers a clean context
**40.0% of the time on 1,165 characters** against cosine's **16.0% on 9,769** — 2.5× the precision at
one-eighth the context, which is the design's own claim measured on the field's data.

**The defect that found (D98).** `clean` FELL as the walk went deeper (40.0 → 36.0) while `stale@k` climbed:
`EdgeHalfLife` decays the EDGE and nothing consulted the ENTRY, so expansion resurrected what recall buried.
`ExpansionRetrievabilityFloor` holds it flat at 40.0% across all three shots. **An ordering weight shipped
first in draft, measured zero, and was withdrawn before commit** — ordering cannot matter unless the
caller's budget binds, and it did not.

**The shot optimum is a property of the QUESTION.** Search wants two shots (LoCoMo +6.0 at shot 2 against
+0.5 at shot 3); resolution wants one. That is the useful half of a negative result.

**Four harness defects found, each of which would have published a wrong number**: the `full` ceiling arm
exceeds the reader's window (needle-probed at 85,508 good / 109,908 bad), the walk deduped by id and threw
away the headline→content upgrade that IS the payload of expanding, `ExpandSeeds` was an unmeasured
arbitrary cap (ruled out — 20 seeds finds the same 36.0%), and LoCoMo questions within a conversation share
a store so a recall reinforces what the next question reads. The last is filed rather than fixed.

- **Run the QA half, and more of it.**

## Part 117 — the write-back is one store call, and the review log moved to the end (2026-08-29)

✅ done 2026-08-29 — `docs/task-archive.md` Part 234's write-back item, opened by **D99**'s own closing
note. The touch, the co-activation edges and the review-log rows were three store calls, and on a relational
store each opened its own connection: `IMemoryGraphStore.WriteBackAsync` takes all three as one, with a
default body running the existing members so a BYO store loses nothing. `docs/DECISIONS.md` **D101**.

**Reported as a COUNT, which is the point.** Connection opens went **3 → 1**, measured by a counting
`IDbConnectionFactory` decorator — the "before" is the same test failing at exactly 3, not an estimate. **No
millisecond is quoted at all**, because this repository has already published a 19% "improvement" from
`memory-scale` that was noise, on this very write-back; the position-totals read also halved but nothing
observes it, so that is a code fact rather than a measured one.

**The ORDERING turned out to be the real finding, and an existing test proved it.** The review log sat
BETWEEN the touch and the co-activation loop, so a log failure skipped the edges — which its own test
already documented from a live mutation check. Moving the log LAST makes that isolation structural rather
than conditional, and the `try/catch` is gone. **That test passed unchanged**, which is what a positive
control is for, and the ORDER is now contract on `WriteBackAsync`.

**Two contract facts hold all three backends to it**, discovered by reflection rather than registered: the
combined path writes what the three separate calls would, and an empty part is SKIPPED rather than written
as a no-op — because the engine turns its own switches off by handing an empty part, and a store that
stamped an age on an empty touch list would reset an age the caller asked to hold still.

**What it does NOT do:** no transaction. "One unit of work" means one connection and one totals snapshot, so
the parts still commit independently — which is precisely what the review log going last relies on.

- **Collapse the recall write-back into ONE store call.**

## Part 118 — LoCoMo's questions shared a store, and it was worth 20-25 points (2026-08-29)

✅ done 2026-08-29 — `docs/task-archive.md` Part 234's contamination item. LoCoMo ran every question of a
conversation against one store, and this engine WRITES on every read: a recall reinforces what it returned and
`ExpandAsync` reinforces what it walks, so question N read a graph questions 1..N−1 had already dug through.
Each question now runs against a private byte-copy of the ingested store — `SweepDb.Clone()`, ingest once
into a template nothing reads. **No library change**; `MemoryLongMemEvalBench` already built one store per
question, which is how this was recognisable as a defect rather than as a property of the data.

**The positive control is the finding, not the fix.** Two runs differing ONLY in how much a LATER shot
expands must leave `shot-1` untouched. Pre-fix it read 65.4% and 53.8%; post-fix, 65.4% both times — and the
fix was stashed so the old code had to FAIL that check, or it would only be evidence the number was stable.

**Every LoCoMo figure moved 20-25 points and `vector` did not move at all** — byte-identical at 80.5%,
because it never touches the graph store. An arm that structurally could not gain did not gain, which is
what makes the other five readable as isolation rather than drift. The gap to cosine is −26.0, not −49.5.
Tables: `docs/memory-measurements.md` §5.

**It cost a published claim, and that is the honest headline.** *"Search wants two shots"* is WITHDRAWN:
**D100** cited shot 2 at +6.0, and isolated the curve is +1.5 on a shot 1 that was 24.5 points too low — a
shared store depressed shot 1 and later shots recovered ground never lost. D100 stands on its other leg,
amended in place.

**The clone control counts rows per conversation** because a lossy copy presents as a recall-quality
regression rather than a broken harness; the WAL trap that makes that possible is in `pitfalls.md` §Testing.
**Not re-measured**, stated rather than implied: D97's before/after tables share the contaminated regime so
their DELTA stands, and the QA half needs a reader (`docs/task-archive.md` Part 233).

- **Fix LoCoMo's cross-question contamination before widening any LoCoMo number.**

## Part 119 — the shot curve, extended: the class where expanding pays, and the one where it never did (2026-08-29)

✅ done 2026-08-29 — two thirds of `docs/task-archive.md` Part 234's shot-curve item: knowledge-update went
from a 25-question sample to all 70, and the temporal class got its first shot curve, on both variants.
Tables in `docs/memory-measurements.md` §5. The remaining third — LongMemEval's four other classes — is
re-scoped rather than closed: each needs a metric matching what that class ASKS, which is design work and not a run.

**The full knowledge-update sample moved the LEVEL down and the RATIO up** — every `clean` figure fell 6–9
points against the 25-question sample, but cosine fell further, so the multiple **D100** argues went
2.5× → 3.1×. The shape is unchanged and the absolutes from that sample were all too high.

**The temporal class is the only workload measured where walking clearly pays** (shot 2 worth +4.5 points of
all-evidence recall, against +1.5 on LoCoMo and −2.8 on knowledge-update), because a temporal question needs
EVERY flagged turn. The counterweight is in the table beside it: size-matched cosine wins that column
outright, since all-evidence recall is an ARCHIVE metric. **Shot 3 is worth nothing — three classes in a
row**, so the lesson is *expand once*, not *expand until the budget runs out*. And the ORACLE overstates the
multi-shot gain by 2.7×, Part 112's bias recurring on a third question.

**A one-question disagreement was chased rather than published and became a measurement**: the haystack has
a reproducibility floor of ONE QUESTION on the graph arms while both vector arms stayed byte-identical, so
deltas are stable and levels are good to about a point. `docs/memory-measurements.md` §5 has it, and `pitfalls.md`
§Testing the general form.

**One refactor, proven neutral before it was trusted.** `WalkAsync` was extracted rather than written a
third time, and equivalence was shown by stash/run/restore/re-run — byte-identical on every column across
all five arms.

## Part 120 — the n-shot walk is a SURFACE now, and both harnesses drive it (2026-08-30)

✅ done 2026-08-30 — `docs/task-archive.md` Part 234's first item, the one its banner called the biggest
thing **D100** opened. `MemoryWalk.WalkAsync` is a static extension on `IMemoryEngine` yielding
`IAsyncEnumerable<MemoryWalkStep>`; **nothing was added to `IMemoryEngine`, `IExpandableMemory`,
`MemoryQuery` or `MemoryItem`**. The reasoning, the three rejected surfaces and what reversing costs are
**D102**.

**The verification was the point, and it is stronger than "the tests pass".** Both harnesses were moved onto
the surface and **every published cell reproduced exactly** — both LongMemEval variants, LoCoMo `--shots`,
and all six arms of the LoCoMo retrieval ladder including every per-category fraction. The oracle class was
additionally run as a true before/after by stashing only the bench file, identical down to the same embedder
call and cache-hit counts, and the after arm was run twice, so the instrument is known deterministic here
rather than merely agreeing once.

**Two rules in the merge are corrections rather than ports**, both mutation-checked: identity is the whole
`MemoryRef` rather than its id (now in `pitfalls.md`, since a composite is what makes it reachable), and the
headline→content upgrade is SEMANTIC rather than the harnesses' `body.Length >` proxy.

**Three defects the plan did not predict**, two of which became reusable traps in `pitfalls.md`: a rule only
the NON-DEFAULT path can break had no coverage at all and its mutation check reported it as dead code; and
the LoCoMo refactor changed a DENOMINATOR rather than a retrieval, which is the one that would have
published a wrong number. The third was `check-samples` catching its own baseline going stale.

**What is deliberately NOT done.** The merge accumulator stays internal (D102 says what would change that).
The public NAMES were provisional when this landed and were settled the same day — Part 121. The working
records are untracked by design (**D43**); everything in them that had to outlive this version is in D102,
design §5.7, `pitfalls.md` and these two Parts.

## Part 121 — the walk's names, passed before the surface shipped (2026-08-30)

✅ done 2026-08-30 — `docs/task-archive.md` Part 234's naming item, filed the previous day and **unblocked
by Part 120 itself**: its blocker was the TREE (`MemoryWalk` did not exist, so there were no names to pass
over), which is the kind of blocker a commit discharges. Five renames, against
`.claude/rules/dotnet-package-layout.md` §Naming.

| was | now | why |
|---|---|---|
| `MemoryWalkStep.Ordinal` | `Number` | `Ordinal` reads as `StringComparison.Ordinal`, which is about comparison rather than position |
| `MemoryWalkStep.Discovered` | `NewItems` | pairs with `Items`; "discovered" is walk-mechanics vocabulary, not a domain noun |
| `MemoryWalkStep.Upgraded` | `UpgradedCount` | a past participle returning an `int` |
| `MemoryWalkOptions.MaxEntries` | `MaxItems` | see below — this is the one with a real argument |
| `MemoryWalkOptions.SelectSeeds` | `SeedSelector` | noun form for a delegate property, matching .NET's `*Selector`; **not** `*Policy`, which here means a DI seam |

**`MaxEntries` is the interesting one, and the argument got STRONGER while checking it.** The rename was
first proposed only for local consistency — it bounds `Items`, and nothing else on the surface said
"entries". Grepping the tree then showed `MaxEntries` is already spent on STORE CAPACITY in five places
(`CacheOptions`, `MemoryEvictionPolicy`, `BoundedProviderPool`, both response caches). A walk's bound is a
per-call context limit, not a store's size, so reusing the word is the `AuthoritativeReserve` shape
`MemoryCompositionOptions` documents having shipped once: one identifier, two meanings, both reachable from
one options chain.

**What did NOT move, each on a precedent rather than a preference:** `Items` and `Ran` mirror
`MemoryRecall`; `Hops` mirrors `ExpandAsync(hops:)`; `SeedsPerStep` keeps "seed", already this library's
word (`SemanticSeedK`, `SubjectSeedK`); `MemoryWalk`/`MemoryWalkOptions` mirror
`MemoryComposition`/`MemoryCompositionOptions`.

**The compiler is the site-list for C# and NOT for prose, which is where the rename could have rotted.**
`StringComparison.Ordinal` appears in three of the four code files touched, so a blanket replace would have
corrupted them — the renames were scoped to `step.Ordinal`/`s.Ordinal` per file. The prose sites (README
sample, `CHANGELOG` Unreleased, D102, design contract §5.7, `pitfalls.md`) were found by grepping the
identifiers, and the same grep correctly left the five pre-existing `MaxEntries` alone.

## Part 122 — the `K` sweep: a compromise, not a default nobody looked at (2026-08-30)

✅ done 2026-08-30 — `docs/task-archive.md` Part 233's `K` sweep. Built
`node devtools/dev.mjs memory-locomo --ranks`, the LoCoMo-side ladder that item asked for, and ran it beside
a re-run of the LongMemEval haystack ladder at full sample. Tables in `docs/memory-measurements.md` §5.
**No default moved, and the item's own premise is what the measurement overturned.**

**The premise was that K = 120 is free, and BOTH halves of it failed.** LoCoMo is a SEARCH workload, and
60 → 120 costs it 4.5 points of evidence-hit monotonically; separately, re-running knowledge-update on all
70 questions rather than 25 shows the same step costing 6.0 points of `current@k` where the small sample
reported 0.0. So *"free"* was one workload wide AND one sample thin. **What replaces it: 60 is a priced
compromise** — every step in either direction helps one metric and hurts another, which is exactly what
"K selects a REGIME" predicts and nobody had measured on more than one regime.

**The sharper result is that `K` is not where the LoCoMo gap is.** 32 of 200 questions had no evidence in
the candidate pool at all, so the fusion loses 29.5 points of material it already HELD and the best K
recovers 4.5 of them. `docs/task-archive.md` Part 233's residual gap is therefore not a fusion constant — it
is seeding for a sixth of it and ranking SHAPE for the rest.

**One replica, not two, proven neutral before it was trusted.** The two ladders share
`bench/Lyntai.Benchmarks/RankLadder.cs`, and the argument is sharper than for `WalkAsync`: a second copy
could get the tiebreak or the competition-rank definition wrong in ONE ladder and still look right.
Equivalence was shown by stash/run/restore/re-run, byte-identical on all eight rows.

**Controls.** LoCoMo's replica reproduces the shipped policy's top-20 on **200/200** recalls and its K = 60
row reads the `lyntai` arm's own published 54.5% to the decimal; the haystack ladder's control is 66/66.
The first attempt at the harness died on a `KeyNotFoundException` rather than scoring nothing quietly — an
early `continue` skipped a dictionary a later scoring path read — which is the loud direction.

## Part 123 — the expansion floor, swept: a better deal than its own doc said (2026-08-30)

✅ done 2026-08-30 — `docs/task-archive.md` Part 234's `ExpansionRetrievabilityFloor` sweep. `--expand-floor`
was added to the LoCoMo harness so both workloads can be priced, and the knowledge-update arm re-run at 70
questions. Tables in `docs/memory-measurements.md` §5. **The default did not move; the DOCUMENTATION did, and that is the finding.**

**It cannot be swept the cheap way, and saying why matters.** The `K` ladder one section earlier scores
offline from a single ingestion because K only re-ranks a fixed pool. The floor changes which neighbours are
FETCHED, and expansion reinforces what it walks, so each value needs its own run against its own store —
three LoCoMo runs and one haystack run rather than one of each.

**The shipped XML doc quoted a superseded sample.** `ExpansionRetrievabilityFloor` documented the trade as
40.0% falling to 36.0% and held flat at 40.0%, costing 4 points of current-fact hit — the 25-question
figures. At 70 it is **+2.8 points of `clean` for −1.5 of `current@k`**: both sides shrank, the cost by
more, so the doc overstated it by **2.7×**. `docs/memory.md` carried the "not re-run at 70" caveat all
along; the XML doc did not, and XML docs SHIP. Same tier as `MaxSalience` keeping *"Unmeasured — a starting
point"* after the ladder that measured it, and as the README's stale gate count fixed the same day.

**On a SEARCH workload the floor is close to free**, which is the opposite of what D98 worried about. LoCoMo
at 0.8 loses one question of 200 at shot 2 and none at shot 3, while cutting context 17% and 8% — hit per 1k
characters goes 0.132 to 0.158. The knob was justified as buying precision with recall; on the workload
where recall IS the metric, it barely charges.

**0.5 is inert, and that is the transferable part.** Byte-identical to 0 on every column, because LoCoMo is
ingested fresh and nothing has decayed that far. On knowledge-update the `--ranks` diagnostic puts the
current fact at retrievability 0.8556 and the superseded one at 0.7206, so 0.8 sits BETWEEN them. The value
that binds is therefore a property of **how decayed a store is**, not of the questions — which is what the
backlog item predicted, and what makes "adopt 0.8" the wrong lesson to draw.

**One control is exact and one is not, stated rather than blurred.** Two LoCoMo floor-0 runs reproduced
byte-identically, which is what licenses reading a 0.5-point move there as one real question. No repeat was
taken on the haystack arm, whose reproducibility this repository elsewhere puts at about one question — so
its +2.8 is a shape, not a decimal.

## Part 124 — the 3D survey: neither option was on the menu (2026-08-30)

✅ done 2026-08-30 — GEN7's survey item asked **mesh vs turntable stills**, and **both options turned out not
to exist as posed** — the second time in two days a backlog item's own premise is what the work overturned
(Part 122 was the first). Working record:
`local/superpowers/specs/2026-08-30-3d-backend-survey.md`.

**The answer.** The dominant 3D family returns a mesh and nothing renderable; a minority adds one fixed
preview thumbnail, not a turntable; and turntable output belongs to a different model family altogether
(SV3D-class orbital synthesis), which is **image→views** and so does not occupy a 3D stage's place in a
chain. **So `3d → image → video` corresponds to no buildable chain, and the chain that IS buildable contains
no 3D stage** — the 3d→image edge is a RASTERIZATION, which no vendor here performs and which belongs to an
application with a renderer rather than to a library promising a small dependency footprint.

**A DESK survey, and the caveat is load-bearing:** every vendor fact was read from a published API page and
never called, so the shapes transfer, no field name is confirmed, and no unverified marker may be deleted.

**What it left elsewhere.** Two chaining traps (a texture atlas is the only `image/*` a mesh backend emits;
`MediaType` is opaque exactly where it would be branched on) went to `.claude/knowledge/pitfalls.md`. Two
OUTPUT-stage defects it found — `ComfyUiProvider` declaring a capability it never implemented, and a false
shipped XML doc on `GenerationKinds.Model3d` — were fixed the same day as **Part 125**, and the capability
one is in `pitfalls.md` and `docs/FIXES.md`.

**What it opened.** **GEN7a**, the pipeline runner at `image → video` — GEN7's design minus the stage with no
backend — built and closed the same day as **Part 126**. So the survey replaced itself in the startable set
rather than shrinking it, and what stays blocked is the 3D STAGE alone, on a rasterizer.

## Part 125 — the capability nothing implemented, and the contract fact that now catches it (2026-08-30)

✅ done 2026-08-30 — the two output-stage defects Part 124 found, both fixed. `docs/FIXES.md` carries the
incident and `.claude/knowledge/pitfalls.md` §Second doors the reusable rule.

**`ComfyUiProvider` declared `SupportsInputs = true` and never read `request.Inputs`.** The flag is an
ADMISSION filter, so declaring it made the router SELECT the backend for input-carrying work it could not
do — the input dropped, the render plausible and billed. Wrong since the commit that added the backend, and
it survived 26 days after the identical bug was fixed in `FalQueueProvider`, because that cure was written
as one provider's comment rather than as a shared fact.

**The guard went into the CONTRACT rather than the provider, which is the transferable part.**
`GenerationProviderContract.A_handed_input_is_consumed_or_refused` hands every HTTP backend an input and
asserts it was consumed or refused, never quietly discarded. **Written against the unfixed tree it failed
for ComfyUI alone and passed for the other three**, which is what makes it a fact rather than a restatement
of the bug. `GenerationKinds.Model3d`'s false XML doc was corrected in the same pass.

**One correction reached two committed records before it was caught**: the filter is
`GenerationCapabilities.Supports`, written throughout as `CanServe` from a grep showing the method BODY
without its signature. A member name quoted in prose had no gate at all — it has one now (`check-links`'
fourth half), and `pitfalls.md` carries the rule.

## Part 126 — GEN7a: the pipeline runner, at the half of GEN7 that is buildable (2026-08-30)

✅ done 2026-08-30 — `router.RunPipelineAsync(stages)` in `Lyntai.Generation.Routing`
(`src/Lyntai.Core/Generation/Routing/GenerationPipeline.cs`): ordered stages, each feeding the next through
`GenerationArtifact.ToInput(role)`, per-stage candidates, per-stage failure semantics. Three public types
(`GenerationStage`, `GenerationPipelineResult`, `GenerationPipeline`), 18 facts, nothing added to
`IGenerationRouter`. Opened by Part 124, which replaced GEN7 in the startable set with this.

**It composes the ROUTER rather than the providers, and that is the load-bearing choice** — every stage is an
ordinary `GenerateAsync`, so spend caps, throttling and dead-host cooldown govern a pipeline exactly as they
govern one render. Pinned as a fact that drives a real `BudgetedGenerationRouter` and fails the day anyone
moves the runner below the seam, mutation-checked by removing the decorator.

**Chaining is one-or-refuse, and the refusal is the design.** Exactly one artifact chains automatically;
zero or several REFUSE without calling a backend. Part 124 ruled out both cleverer rules, and the failure
costs are asymmetric — a refusal is a message, a wrong pick is a plausible billed render of the wrong thing.
`GenerationStage.SelectInput` and `InputRole` are where a caller states their own rule, since the same PNG
is a first frame to one backend and an init image to another. **Nothing is ever re-run**, so a later
stage's failure keeps what earlier stages paid for, and two settings that nothing would read THROW rather
than being ignored — the Part 125 defect in miniature.

**It also found the README asserting the chain Part 124 had just disproved**, surviving two passes that each
fixed the claim only where it was found. That became a rule in `pitfalls.md`: when a decision falsifies a
claim, grep the CLAIM.

- GEN7a — the pipeline runner at `image → video`, the half the survey unblocked

## Part 127 — the salience defaults: measured on both embedders, and neither default moved (2026-08-30)

✅ done 2026-08-30 — `TASKS.md` Part 65's "two option defaults, and one makes the other inert". The owner
was asked whether `SalienceOptions.MaxSalience` (4) and `NoveltyWeight` (1.5) should stay and answered
**measure it**, so the question stopped being a decision and became a ladder. Tables in `docs/memory-measurements.md` §5.
**Outcome: both defaults stay**, and the reason is that the two real embedders pick OPPOSITE ends of the
ladder — `NW0.5` under `nomic-embed-text`, `NW3` under `embeddinggemma:300m` — so no best weight exists to
adopt. That is `docs/DECISIONS.md` D89's own precedent holding a second time: it required a second embedder
because the first reading did not survive one, and this one did not either.

**The instrument had to be repaired twice before it could answer, and the second repair is the finding.**
The verdict reported Δ miss ALONE, which is the defect `pitfalls.md` records this file's sibling paying for
with a reverted default; it now reports pollution beside it and evaluates §5.7.0's lexicographic order. And
**the `SalienceOff` arm was never off** — it passed `saliencePolicies: null`, which
`GraphMemoryEngine.NormalizeSaliencePolicies` turns back into the shipped policy, so every table this sweep
ever published compared retention-on against retention-off with salience's ADMISSION consumer live in both
arms. `docs/FIXES.md` carries the incident, `.claude/knowledge/pitfalls.md` the general rule.

**What the corrected run establishes, beyond the defaults question.** The harness is DETERMINISTIC — the
clamp's own neutral rung reads exactly `0.0000` against the off arm on every cell of every shape, both
metrics, zero-width intervals, on both embedders — so the run-to-run noise floor for an identical
configuration is zero and no separate repeat was needed. `high-noise` is where salience pays (−0.09 to −0.12
at every weight, significant under both), the shipped weight is not a net cost under either (+0.0018 against
−0.0125), and `many-candidates` — the regression Part 65 exists for — is significant under one embedder and
not the other. **Read the `high-noise` column against the templated-noise caveat**: the cell now carrying the
result is the one most exposed to the corpus's known blind spot.

**It was widening the study that exposed the confound, not checking it.** Both earlier ladders ran two corpus
shapes; at six, a provably-silent arm came back significantly worse than "off" on two new shapes, by more
than the spread of the arms being ranked.

- Two option defaults, and one makes the other inert

## Part 130 — `check-decision-claims`: the gate for drift no other gate can see (2026-08-31)

✅ done 2026-08-31 — `TASKS.md` Part 129's gate item. **The FIFTH member of the prose family**, and the first
that gates a decision's TRUTH rather than its form: `check-docs` asks whether a document still SAYS what a
decision settled, `check-links` whether what it POINTS AT exists, `check-counts` whether what it COUNTS is
true, `check-comments` whether a comment still does a comment's job — and none can see a decision that stops
describing the code, because that retires no vocabulary, dangles no path and moves no registered count.

**The measured cost of not having it** is the audit that produced it: the log is accurate about VALUES and
drifts on COUNTS and CLASSIFICATIONS. Every stated constant verified; every defect was a number of things or
a category — D46's own title said "four DOMAINS" against seven (and `decisions-index` rendered it into the
index table on top), and `CLAUDE.md` claimed five required `IMemoryGraphStore` members against **13**.

**Six predicates, each verified BY HAND before being registered** — D54 `ReinforceGain == 0`, D89
`SalienceWeight == 0`, D62 the fan effect present AND off, D88 `SubjectSeedK > 0`, D46's live policy-domain
count, and D47's "every `IMemory*Policy` lives in a domain folder EXCEPT the composite-level removal policy".
That last one earns its place twice over: it is the exception this audit filed as a violation on the strength
of the name alone and nearly moved, breaking the API for nothing. The predicate is what makes the exception
checkable rather than arguable.

**A registry, not a scan**, for the reason `pitfalls.md` gives — an existence check over this repository's
prose returned ~45 hits and zero defects, because naming something absent is frequently correct here. Its
honest limit is stated in its own header: it covers what is registered.

**Two things the build itself taught, both kept.** Its first run went RED on a predicate that was wrong, not
a decision that was stale — `_diagnosticityWeight` ships at 0 by declaring no initializer, and a matcher
requiring `= 0` read that as the field being ABSENT. The helper now returns 0 for a declared-uninitialised
field and NaN only for a missing one, so "ships at 0" and "deleted" stay distinguishable, and a throwing
predicate is reported as a broken GATE rather than as stale prose. And the tests drive it against SYNTHESIZED
trees: asserting only that the real repository is green passes on a predicate that can never go red, which is
the exact failure `test-devtools` exists for.

- Build `check-decision-claims`, the gate the audit argued for

## Part 131 — per-source fusion clears cosine: `+sem+rel-only` at 83.0%, and the 63.5% bar it replaces (2026-08-31)

✅ done 2026-08-31 — `docs/task-archive.md` Part 235's first item, "make `Relevance` comparable before it
is ranked." Shipped as `IMemorySeedSource` (`docs/DECISIONS.md` **D103**): `ReciprocalRankFusionPolicy` now
fuses each source's own ranked list instead of one pooled `Relevance` field. Tables and the full reading
are `docs/memory-measurements.md` §5.

**Outcome, controls beside the result.** Three controls reproduced exactly across two runs — `vector` 80.5%,
`+rel-only` 60.0%, `lyntai` 54.5% — so the harness did not move. `+sem+fuse` clears the old 63.5% bar at
**76.5%**; `+fuse` (lexical only, where the pre-registered weight-shift confound cannot occur by
construction) does not move at all, so the gain is per-source fusion. **`+sem+rel-only` — not `+sem+fuse` —
is the actual best mechanical arm**: the spec's own control assumption was wrong (it called that arm inert
at 63.5%; it is in fact the arm MOST sensitive to fusion), and it now reads **83.0%, above plain cosine's
80.5%** — the first mechanical arm to beat the formula this engine was losing to by 26 points. The two runs'
arm tables are byte-identical, which establishes determinism only (the harness is deterministic by
construction) and bounds no stochastic noise, since there is none to bound. **No default moved** —
`SemanticSeedOptions` still ships unregistered.

**Two findings kept though neither is this item's headline.** `+sem+fuse` and `+sem` are IDENTICAL at
76.5% — their config tuples match except the name (`MemoryLocomoBench.cs:247` vs `:358`) — so the arm is
**permanently redundant** and should be removed or renamed rather than kept as two names that can never
disagree. And the Multiplicative arms moved as a side effect nobody targeted: `+sem80+mult` from a recorded
21.5% to **57.0%**, because `SemanticSeedSource` now reports `Matched = true` carrying an honest cosine and
`MultiplicativeRankingPolicy`'s existing `Matched is null ? 1 : Relevance` read stopped treating a semantic
seed as "nobody asked" — `MultiplicativeRankingPolicy` itself was never touched.

- Make `Relevance` comparable before it is ranked.

## Part 134 — the QA half converts, ~~superadditively~~, but leaves a VOLUME-vs-FORM question open (2026-09-01)

✅ done 2026-09-01 — `TASKS.md` Part 134's one item, separating volume from form in the walk's residual gap
against `vector-40`. A third `--seeds` measurement (16, after 3 and 8) answers it: **mostly volume**. The gap
to `vector-40` narrowed **11.3 → 5.3 → 2.5** points as chars/q rose 5010 → 5727 → 6747 (`vector-40`'s own
7090), and the pre-registered "at least ~4 points is FORM" floor is falsified — a 2.5-point total residual
cannot contain a 4-point floor. Full tables and reading: `docs/memory-measurements.md` §5.

**RETRACTED 2026-09-01, same day — the superadditivity claim.** Every reading here was taken at n = 100; at
the full 1,540 questions the interaction reads **+1.0**, not distinguishable from zero. An interaction is a
difference of differences, so its error runs roughly double any one component's. **The category wins are
retracted with it** — +6.2 and +5.5 became four deltas of −0.6, −2.1, +0.1, −0.3. What survives is the
volume-vs-form reading, which was never a difference of differences: the residual reads −0.6 points at full
sample, still "mostly volume, not form". `docs/memory-measurements.md` §5's retraction subsection.

**A methodological correction rides along and matters more than the number.** The interaction was reported
+6.7, corrected to +4.2, and the third point read +6.8 — **non-monotonic**, so the direction drawn from two
points did not survive a third. That is the second time in this line of work one extra measurement
overturned a published conclusion. What replaces the point estimate is size-free: fused ranking raises the
CEILING on what the walk can use, where the shipped-ranking arm plateaus.

**A pre-registered prediction held on a stated LOW confidence**, which does not vindicate the reasoning
behind it — that model was already known wrong from the previous round.

**What is left is a different question than the one this Part opened**: an EFFICIENCY gap (~1.9× the context
for matching accuracy), not a capability one. Recorded as a finding in `docs/memory-measurements.md` §5, deliberately not
opened as a task.

- Separate volume from form in the walk's residual gap against `vector-40`.

## Part 135 — the context-efficiency gap: the walk's cost is in SLOTS, not characters

✅ done 2026-09-02 — Built `node devtools/dev.mjs memory-locomo --composition` (`MemoryLocomoBench.cs`), a
model-free two-arm mode that decomposes a returned context into headline versus content and counts
duplication within it. Full sample, 1,540 questions, 1,269.7s. Findings and tables:
`docs/memory-measurements.md` §5 finding 9. **Three of the four questions needed no run at all** — they fall out of the
published QA table divided through by its own `items/q`, validated by a corpus reconstruction that
reproduces the `full` row (601.4 items/q, 101209 chars/q) to the character.

**The Part's own premise was half a comparison.** The ≈1.9× that opened it compares 40 items to 20. At
matched count the walk spends **3.6% LESS** than `vector-40`, a row the same table already carried. **The
cost is in SLOTS, not characters** — 40 slots to reach what cosine does in 20, at a cheaper price per slot.

**And the Part named a dump that could not answer it**: it records what the READER answered, for judge
calibration — no context, no pieces, no chars. Censused rather than inferred from the flag's name, and the
~5-hour regeneration cost quoted for it was the QA run's, never this diagnostic's.

**The upgraded share is STRUCTURAL rather than empirical** — `SeedsPerStep × (shots − 1) / MaxItems`, since
the seed budget bounds the raise rather than the corpus. That is why the pre-registered 88–95% was falsified
at 80%: it was reached by inverting mean lengths, which assumes both sub-populations sit at the corpus mean
and neither does. **A structural quantity was never the kind of thing an interval over corpus statistics
could find.** The mode also measured the WRONG ARM on its first run, defaulting to a different `--seeds`
than the table it explains and reporting a plausible number with nothing erroring; `pitfalls.md` has the rule.

**It turned up something bigger than itself** — `evidence-hit@k` matches an id that survives headline
truncation in 5,882 of 5,882 turns, so the metric scores a half-turn and a whole turn identically while the
graph arms return the first and the cosine arms the second. That became Part 136.

- Diagnose what the 40 items behind `lyntai-fused-3shot` actually contain.
- Measure the two things the derivation could not.

## Part 133 — the gate for the withdrawn position rule is narrower than the rule

✅ done 2026-09-02 — Broadened the `retiredTerms` entry that fences D103's withdrawn "position IS the rank":
connectors `is`/`as`/`a`/`the` are now case-covered individually (they were spelt lowercase-only with no `i`
flag while `Position` and `Rank` WERE case-covered, so capitalising one connector defeated the rule) and the
article is OPTIONAL. The article requirement had existed solely to dodge one legitimate line; that line, and
two more this newly reaches, carry a visible `drift-ok` instead. Four regression tests in
`devtools/scripts/__tests__/check-docs.test.mjs`, driven against the REAL registry entry rather than a
fixture rule, with one asserting the entry still exists so the others cannot pass vacuously.
**Mutation-checked**: reverting to the narrow pattern fails exactly the two "bites" tests and nothing else.

**The Part's other half was MEASURED AND REFUSED, which is the finding.** It asked for a
`ranks`/`ranked BY position` branch to catch paraphrases. Compiled and run over the tracked tree — by the
gitignored `devtools/_part133-probe.mjs` <!-- link-ok: gitignored scratch, named as provenance; re-creatable from this paragraph -->, which imports `check-docs`' own scope predicates and two-line
window rather than restating them — that branch
produces **15 hits**, most of them `ReciprocalRankFusionPolicy` and its contract test describing what the
policy legitimately DOES — reciprocal rank fusion ranks by position within each source's own list, which is
the algorithm rather than the withdrawn rule. The two are not separable by pattern, so its false positives
are legitimate authorial choices: the shape `.claude/knowledge/pitfalls.md` records as untightenable, fixable
only by an exclusion list nobody can see rot. The paraphrase stays UNCOVERED, named in the registry comment
and pinned by a test, so the gap is a decision a reader can find rather than an oversight.

**What the Part got wrong, for the next reader.** It said "annotate the ONE legitimate quote"; broadening
reaches **three** — `CompositeRankingPolicy.cs`'s warning plus two lines of the Part's own prose, which quote
the withdrawn rule as their subject. It also asked to "prove it bites on a PARAPHRASE", which the refusal
above makes unsatisfiable; the capitalised-connector and article-less proofs stand.

- Broaden the pattern, and annotate the one legitimate quote instead of dodging it.

## Part 136 — is TRUNCATION the retrieval-win/QA-loss gap? The rehydration arm settles it

✅ done 2026-09-02 — **Yes, almost entirely: +11.7 of a 12.0-point gap.** Built `lyntai-fused-full`
(`MemoryLocomoBench.cs`), which reuses `lyntai-fused`'s OWN returned set and swaps each item's headline for
the whole turn behind it — identical retrieval, ranking and 20 slots, so the arms differ in truncation and
nothing else. It builds no engine and issues no second recall. CONTROL: 4,000 of 4,000 items rehydrated, no
misses. `memory-locomo --n 200 --no-judge`, seed 12345, 2,712.4s. Tables: `docs/memory-measurements.md` §5.

**token-F1 goes 29.3% → 41.0%, landing on plain cosine's 41.3%** at the same 20 slots and within 1.2% of the
same context. **The engine's ranking is not worse than cosine — the entire measured QA deficit was headline
truncation**, and the retrieval ladder was right about the ordering all along. The reader's refusals
corroborate it: 29 truncated against 13 rehydrated, on the SAME 20 turns.

**A design fact falls out, and it is the durable half.** One shot with full content beats three shots with
mixed content for LESS context, so expansion is an expensive way to obtain content already in hand. That
does not touch **D100**, whose claim is about DISCOVERING material a first load did not hold.

**What the Part got wrong, for the next reader.** A pre-registered 38–45% was REVISED DOWN to 32–40% before
the run, on a fixed-slot contrast that prices the low-value half — the pre-registration had already named
that reason. The outcome was 41.0%, so the original was right and the revision wrong by an order of
magnitude. **The lesson is about extrapolating a natural experiment past the population it measured**, not
about the arithmetic, which was correct.

**CONFIRMED at full sample (Part 138)** — 42.0% against `vector`'s 42.4%, a −0.4 where this read −0.3. It
was filed as a Part rather than believed, because the same session's 40-slot claim flipped sign at full
sample. What did NOT survive is the category detail: multi-hop's near-tie reads −4.0.

**What it measured was a HARNESS arm, not a shipped path** — and the decision it left open was taken the
same day as **D104**, so a caller now asks for whole entries with `MemoryQuery.Detail`. Still open: one
reader tier, one embedder, and category splits that do not all point one way.

- Build the REHYDRATION arm and run it.

## Part 137 — does the WALK gain from its extra 20 slots the way deeper cosine does?

✅ done 2026-09-02 — **Yes: LEVEL with plain cosine at 40 slots.** `lyntai-fused-3shot-full` — the three-shot
walk asking for `MemoryDetail.Full` — scores **44.0%** token-F1 against `vector-40`'s **44.4%** at 39.7 slots
and 6,941 chars against 6,780, on all 1,540 questions. Tables: `docs/memory-measurements.md` §5. The engine was measured
12 points behind cosine before headline truncation was found; it is now within half a point.

**An n = 200 pass read this as +0.9 and AHEAD, and the full sample RETRACTED it** — the sign flipped, and
the retraction is in `docs/memory-measurements.md` §5 rather than only here because the "first time the engine is ahead"
claim had already been written into the record. The noise floor is why: two arms sending byte-identical
prompts scored 40.7/41.1 at n = 200 and 42.2/42.2 at full sample, so ±1 point was never resolvable at 200.
**Pre-registration protects nothing if the run that judges it is underpowered** — the pre-registration here
was right and its n = 200 adjudication wrong.

**A flaw in the confirming run's own design** — its arm set dropped `vector`, leaving Part 136's 20-slot
parity without a full-sample control — was filed and CLOSED the same day as **Part 138**, which also gave
this run the cross-run reproducibility check it could not give itself.

**Two reusable traps went to `.claude/knowledge/pitfalls.md`**, both from the first, VOID run of this arm: a
prediction landing where you expected is when you are least inclined to ask what produced it (the accuracy
column could not show the defect and a `chars/q` column did), and a SELF-HEALING mechanism absorbs the bug
you are asserting against, so that fix's test passed with the fix reverted.

- Run `lyntai-fused-3shot-full` and read it against `vector-40`.

## Part 132 — the two seam-name diagnostics key on a hardcoded string, and a BYO channel can trip them

✅ done 2026-09-02 — `IMemorySeedSource.Kind` (`MemorySeedKind`: `Lexical` / `Semantic` / `Subject` /
`Custom`), and `GraphMemoryEngine.EmbedsWithoutSeeding` / `.RecordsSubjectsWithoutSeeding` now ask a
channel's ROLE instead of comparing `Name` by ordinal string. A BYO vector channel called anything at all
answers the question; renaming a shipped source can no longer mute the finding.

**Additive and DEFAULTED, so nothing outside breaks.** `Kind` has an interface default of `Custom`, so a
source written before the property existed compiles unchanged — the API diff is purely new members, with no
signature changed and therefore no binary break (unlike **D104**'s two).

**A `Custom` channel makes the diagnostics ABSTAIN rather than accuse**, which is the design decision inside
this fix. An undeclared source may BE the channel a diagnostic would report missing and the engine cannot
tell, so silence is the safe direction — `MemoryWiring`'s own class doc says a finding that is usually wrong
is worse than no check, and the false-positive case was the one that THREW under `StrictWiring()`.

**Three tests, one of which is the control that matters:** a BYO semantic channel under its own name is not
reported; an undeclared channel silences rather than trips; **and an embedder with no semantic channel at all
is STILL reported** — without that last one the first two pass on a diagnostic that was simply switched off.
Mutation-checked: reverting to name-matching fails exactly the two "must not accuse" tests and leaves the
positive control passing.

The consumer-facing finding text moved with it — it named `IMemorySeedSource named 'semantic'` and now names
the kind, and tells a consumer with their own channel to declare it.

- Give a seed source a way to say WHAT it is, not just its `Name`.

## Part 138 — the 20-slot parity claim had no full-sample control

✅ done 2026-09-02 — **The claim SURVIVED**, unlike the 40-slot claim measured beside it:
**`lyntai-fused-api` 42.0% token-F1 against `vector`'s 42.4%** on all 1,540 questions, a −0.4 where Part 136
read −0.3 at n = 200. Same sign, same magnitude. `docs/memory-measurements.md` §5 has the table and the reading.

**Why it was worth confirming something that did not change.** The claim was measured in the same session as
a 40-slot claim that flipped sign at full sample, and the confirming run had dropped `vector` (20 items) —
so parity rested on exactly the sample size just shown unable to support a difference of that size. The
run's pre-registration declined to predict the sign, because confirming and retracting are the same job.

**A flaw in this run's own design, recorded rather than argued away:** the item asked for a noise floor
measured IN the run and this used a cross-run one, which is stricter on one axis and still not the control
that was specified. That is the third consecutive Part here whose limitation was in the RUN DESIGN rather
than the code. What did not survive is the category detail — multi-hop's n = 200 tie reads −4.0 at full
sample.

- Run the 20-slot pair at full sample.

## Part 181 — the traps record is findable by SHAPE, not only by heading

✅ done 2026-09-10. **D112**; the gate is `node devtools/dev.mjs check-pitfalls [--write]`, and the
vocabularies are `pitfallFacets` in `devtools/project.config.mjs`.

A cold-start probe's nine relevant traps spanned five of nine headings, two of which nobody would have
opened. Retitling was REFUSED — any single hierarchy files a trap in one place and these belong in two —
so every trap now carries `<!-- trap: sub=… shape=… -->` and the index at the head of the file is generated
from them. Closed vocabularies, where an unknown value AND a value no trap uses both fail.

**The subtle half is now shared** (`devtools/scripts/_markers.mjs`, with `check-backlog`): the attribute
RESIDUE check, the line-number fixed point, escape carrying, and anchor uniqueness. `check-backlog`'s tests
staying green is what makes that refactor a claim rather than a hope.

**Four defects, and only two were caught by its own tests** — the generated index's rows start with `- `,
so the parser read them back as unfiled traps; and a `- ` inside a fenced block would have been demanded a
marker, a gate whose only remedy is corrupting what it guards. **The other two came from an adversarial
review after every gate was green, and both made the gate WRITE a truncated read**: an unclosed fence took
158 traps to 131 with `--write` publishing it at exit 0, and the parser located the generated block by its
own scan while the splicer used `blockRange`, so a duplicate anchor moved the block and a write deleted
prose. The general trap is in `.claude/knowledge/pitfalls.md`; the fix deleted the second derivation.

- Give `.claude/knowledge/pitfalls.md` a generated facet index.

## Part 180 — the backlog's roster is GENERATED from a state authored on each item

✅ done 2026-09-10. **D111**; the gate is `node devtools/dev.mjs check-backlog [--write]`, and the rule it
enforces is `.claude/rules/task-lifecycle.md`.

`TASKS.md`'s banner had advertised finished work four times and `.claude/knowledge/pitfalls.md` had refuted
all three obvious gates — each INFERS a state from a `- [ ]`, which cannot say *watch* or *decision-only*.
So the state moved onto the item as `<!-- item: state=… kind=… needs="…" -->` and the table at the head of
the file is derived from those markers. An unmarked item, an unknown state, a blocker with no KIND and a
hand-edited table all FAIL; the startable count is registered in `COUNTED_CLAIMS`.

**Reclassifying was deliberately NOT part of it.** Every marker records what the file already asserted — 12
startable, 7 blocked, 1 watch, 1 decision-only — the last being the one item whose own prose said it was
not startable. The harm `task-lifecycle.md` measures runs the other way: an item left labelled blocked after
its blocker cleared.

**Two defects the build found and review would not have**, both recorded in D111: a generated row
reproduced a `link-ok`-annotated path without the annotation, so `check-links` fired on the copy; and every
row publishes the line number of a checkbox that writing the row moves. **A third came from an adversarial
review after every gate was green** and is in `.claude/knowledge/pitfalls.md`: the marker parser validated
what it matched and was blind to what it skipped, so an unquoted multi-word `needs=` was truncated to its
first word and `--write` published it while reporting every marker sound.

- Generate the open-item manifest at the head of `TASKS.md`, and gate the startable count.

## Part 179 — the rule template left the always-on tier

✅ done 2026-09-10. Moved to `.claude/templates/rule-template.md`.

It auto-loaded into every session (~410 tokens) while `RULES_INDEX.md` called it *"deliberately absent —
the template for writing a new rule, not a rule to follow"* and its own frontmatter agreed. The claim was
true of the index TABLES and false of the directory, which is the only place it cost anything. Its steps
2–3 now say to copy it out and spell the index's path, since it no longer sits beside one.

- Move `.claude/rules/TEMPLATE.md` out of the always-on tier. <!-- link-ok: the original wording, preserved; the file is at .claude/templates/rule-template.md -->

## Part 176 — three rerankers, one score: a 468 MB model matches a 636 MB one, and recency buys nothing

✅ done 2026-09-10, at the owner's direction ("try to improve with a smaller judge < 500mb"). Table:
`docs/memory-measurements.md` §5. Four traps went to `.claude/knowledge/pitfalls.md`.

**`LAMAR-600m` is a near-perfect control for MODEL AGE** — same `XLMRobertaForSequenceClassification`, same
567,755,777 parameters as `bge-reranker-v2-m3`, released 28 months later. **At matched quantisation it is
IDENTICAL**: 91.0% against 91.0%, cell for cell. The one arm that differs is the SMALLER file by a single
question of 200, inside the near-tie band.

**The usable result is the size**: 468,393,760 bytes captures 6.0 of the 7.0 points a perfect judge offers,
at 74% of the incumbent's bytes. Both anchors reproduced exactly across all three runs.

**It is silent on the owner's actual hypothesis**, and says so: it tests recency in the RERANKER role, while
the model measured as costing 10.5 points is an LLM JUDGE facing a different task shape.

- Try to improve the memory result with a smaller judge (< 500 MB).

## Part 175 — the endorsement CAP is not the lever, and the comparison that suggested it was confounded

✅ done 2026-09-10. **D110**; table in `docs/memory-measurements.md` §5.

**The repo's own source named the confound and nobody had read it**: the cross-encoder endorses a FIXED
top-k, so the failure that cost the 4B judge its points is unreachable for it by construction. So
"reranker beats judge" differed in the count rule as well as the model.

**The control was built and refutes the count-rule explanation.** Capped at the page size the judge scores
71.0% against its uncapped 71.0%, identical in every cell, while the cap BOUND on 138/200 calls and dropped
16.1 endorsements per call. The `+top80` null control reported itself INERT.

**The audit bounds every future promotion rule on this judge**: 14 of 200 calls are rescuable, and the judge
put the evidence in its own top five on none of them.

- Give the LLM judge the same fixed-count rule the reranker gets, and see what the model is worth.

## Part 174 — the fail-open CHAIN: the remaining 16 sites answered, and four contracts corrected

✅ done 2026-09-09, closing the item Part 173 opened the same day. Detail: `docs/FIXES.md`; the two reusable
traps are in `.claude/knowledge/pitfalls.md`.

**The filing's premise was wrong.** It said the 16 needed individual answers because "cancellation semantics
differ"; they do not. All 16 sit above the same promise and all wrap a **BYO interface** a consumer may
implement over HTTP. The driver question that framing invited is irrelevant — `Lyntai.Core` references
neither Npgsql nor Microsoft.Data.Sqlite.

**The handlers NEST, which is why a subset fix was worthless**: one embedder timeout crosses four of them
before reaching the caller, and a bare rethrow at any link breaks every link above it.

**Fixed 15, DELETED 1** — `CollectSignals` guards a synchronous policy with no token in scope — and
corrected **four seam contracts** that stated the false premise in shipped XML docs (`IMemoryEngine`:
"because cancellation belongs to the caller"). Each now states the TEST, not the type.

**Mutation-tested twice**, which caught a vacuous test of its own: 12 of 17 facts fail against the unfixed
tree and only the 5 caller-cancel controls pass.

- Answer the 16 remaining bare cancellation sites, or decide they need no answer.

## Part 173 — the annotation seam had the defect too, and so did two more places

✅ done 2026-09-09. Detail: `docs/FIXES.md`; the trap is in `.claude/knowledge/pitfalls.md`.

**Yes, and in two more places than filed** — `LlmMemoryAnnotationPolicy`, `GraphMemoryEngine.AnnotateAsync`,
and `LlmMemoryVerificationPolicy`, which the original fix left because the engine's outer catch masks it on
the shipped path. The write path is the worse half: annotation runs before the upsert, so a slow annotator
lost the FACT. The promise moved onto both seam contracts.

**The reusable finding is that the first fix's own control could not fail.** Both caller-cancel twins passed
under the wrong repair; both now assert on a MARKED exception.

**A count wrong in three documents at once** ("20 other sites", derived by subtracting one fixed site from a
grep total) was corrected and GATED — `check-counts` grew a counter.

- Does the ANNOTATION seam fail closed on its own timeout too?

## Part 172 — the RIF trigger measured: INVERTED, not merely uninformative

✅ done 2026-09-09. The one measurement **D109** named as able to reopen the direction. It closed it
instead. All 70 knowledge-update questions, haystack, shipped judge over `gemma3:4b`, 40 candidates per
call, zero judge failures. Table in `docs/memory-measurements.md` §5.

**The judge endorses the SUPERSEDED fact 3.4× more often than the current one** — 36.2% against 10.8%,
against a 9.0% base rate. So the unendorsed half is enriched for the CURRENT fact. Of the 65 calls showing
both, a penalty would demote the superseded fact alone 5 times and the current fact alone **23**.

**D109 predicted "at base rate" as the killing result and the truth is worse**, which is the rarer and more
useful outcome: query relevance is mildly ANTI-correlated with recency here, the same effect behind the
cross-encoder taking `stale@k` 44.3% → 95.7%.

**REPRODUCED on llama.cpp the same day** with the paired cells identical (5 against 23) across a different
server and embedder, once the model's own GGUF was pulled — the Ollama blob will not load upstream.

**It also found a shipped defect** — a fail-open seam failing CLOSED on its own timeout (`docs/FIXES.md`),
which cost 40 minutes of ingestion before it was read.

- Run the trigger-precision measurement D109 named.

## Part 171 — RIF analysed and REFUSED, and three of its premises were wrong

✅ done 2026-09-08, closing the last of the 2026-09-07 handover's three threads. Nothing built. **D109**;
the corrected reading is `docs/memory-measurements.md` §5.

**It addresses the wrong half of the gap it was filed against**: the name claims a semantic relation
(supersession) and RIF supplies a weakening ACT over competitors. **D106** already put the semantic half at
ENCODING and named valid-time on the write as what fills it.

**"The contract blocks it" was never true.** `Reinforce`'s monotonicity is scoped to a successful recall,
and `ModulatedRetrievability` already lowers retrievability while persisting nothing. The real constraint is
`IMemoryRetentionPolicy`'s `[1, declared]` clamp, which exists because a narrowing factor would make
`PruneAsync` delete — D41's refusal.

**Three premise errors, each refuted by the tree**: the unendorsed signal is not persisted on the shipped
`Partition` (the judge endorses more than the limit) and does not exist by default; the stated null control
contributes exactly zero, because a write advances age as a common addend and RRF reads rank POSITIONS; and
the proxy is measurably blind to supersession (`stale@k` 44.3% → 95.7%).

**One measurement would reopen it** — trigger precision from the review log, which is already persisted.

- Thread 3 of the 2026-09-07 handover: RIF as the shape of the "contradicted" gap.

## Part 170 — a verifier is shown the CONTENT, and the reranker gets its 13 points back for free

✅ done 2026-09-08, taking the decision Part 168 filed. `MemoryVerificationCandidate.Content` (**D108**),
additive, engine-supplied, `null` when nobody built one. `docs/memory-measurements.md` §5.

**Validated rather than argued**: with the field read, `+sem+rel-only+rerank` goes **78.0% → 91.0%** at the
SHIPPED `HeadlineChars = 120` and lands exactly on the `+hl512` arm that bought the same text with **+24%
of content bytes**. Zero storage, zero extra query — `SeedAsync` already selected the column, so the seam
was withholding data it had paid for.

**Why not the other two options.** Raising `HeadlineChars` costs the storage AND changes what every recall
RETURNS to callers, so an internal verifier concern would leak into user-visible output. An option
selecting the text is redundant once a policy holds both and can choose.

**Nothing changes for an existing consumer**: the shipped `LlmMemoryVerificationPolicy` still reads the
headline, because a judge pays by the token and only the policy knows whether it is paying.

- Decide what TEXT a verifier may read.

## Part 169 — the cross-encoder's knowledge-update visit: not the trade LoCoMo winners usually make

✅ done 2026-09-08. The check `docs/memory-measurements.md` §5's standing rule demands of any arm that wins LoCoMo, run
because a reranker reorders by RELEVANCE and a superseded fact reads as relevant as its replacement. Table
is `docs/memory-measurements.md` §5. All 70 knowledge-update questions, haystack, 34,242 turns per arm.

**Predicted badly and it came out mixed.** `prefers current` falls 90.3% → 86.8% while the absolute count
RISES **56 → 59 of 70**, because the metric is scored only over decidable questions and the reranker makes
six more decidable. `current@k` +8.5. **`stale@k` 44.3% → 95.7%**: it returns both facts, which is what a
relevance scorer must do when they are near-identical text.

**So it is not the `RetrievabilityWeight = 0` shape** — it does not buy finding with burying. Decay still
orders the current fact first inside the promoted set, which is why preference holds where plain cosine at
the same breadth collapses to 40.0%.

**No paired test of reranked against shipped exists** — the bench pairs everything against `vector` — so
three questions with overlapping intervals is not a result, and no default moved.

**Two instrument defects fixed to get here**, both silent: an over-long embedding input crashes
`llama-server` where Ollama truncated quietly, and a character budget cannot bound a token limit. The
embedder now shrinks and retries on the server's own complaint and REPORTS the count.

- Take the cross-encoder to the workload this design makes its claim on.

## Part 168 — the cross-encoder is worth +5.0, and the seam was starving it

✅ done 2026-09-08. Closes the ranking lever the 2026-09-07 handover filed as blocked on "a model download
and an ONNX adapter package". Neither was needed: `llama-server --reranking` serves `bge-reranker-v2-m3`
over HTTP, and the arm reaches the engine through the verification seam that already ships. Tables are
`docs/memory-measurements.md` §5.

**The result, on LoCoMo n = 200**: at the shipped `HeadlineChars = 120` a cross-encoder SPENDS 7.5 points
(85.5% → 78.0%), refuting a pre-registered 86–90%. With headlines long enough to hold the turn it reads
**91.0% against a matched base of 86.0%** — **+5.0**, and 1.5 short of a perfect judge's 92.5%.

**The finding is the seam, not the model.** A verifier sees `Headline` and never `Content`, and 55.8% of
LoCoMo turns exceed 120 characters, so the reranker scored a truncation. Its audit was clean throughout
(48,002 pairs, 31,340 distinct scores), which is what makes "the model cannot do this" the wrong reading —
`pitfalls.md` carries that as the reusable half.

**Fusion generalises D105 and disappoints the same way**: `+rerank+fuse` scores exactly the base in every
category, so competing on rank removes the partition's whole loss and adds nothing.

**No default moved**, and none should on one workload — an arm winning LoCoMo owes the knowledge-update
table a visit first. What is open is a library decision, filed in `TASKS.md`.

- Measure the cross-encoder reranker filed as the supported fix for the ranking gap.

## Part 167 — what serialises a pure-read recall: SQLite's memory STATISTICS, not any lock

✅ done 2026-09-08. Closes the open question `docs/memory.md` §7 carried since 2026-09-07. Tables and the
full refutation chain are there; the decision is **D107**.

**The answer**: SQLite collects memory-allocation statistics by default and maintaining them takes a
process-global mutex on every allocation and free, so concurrent readers serialise on a counter rather than
on the database. Eight concurrent read-only recalls go 340/s → **4,665/s** and sixteen 216/s → **6,275/s**,
and the peak at TWO WORKERS becomes monotonic scaling. One thread is unchanged.

**Shipped**: `SqliteRuntime.DisableMemoryStatistics()`, opt-in and never called by Lyntai (D107). The bench
gained the two controls that found it — a CPU-over-wall `cores` column and `--queries`/`--warmup` — plus a
wrap fix its own `hit` control caught, where a widened cell addressed rows that did not exist.

**What the chain cost, and the reusable half is in `pitfalls.md`**: six hypotheses refuted first (the
connection open, GC, exceptions, the WAL, journal mode, the measurement window), then the engine and all
shared state. What located it was scope, not SQLite knowledge — `cores` showed 7.7 busy cores, killing every
lock hypothesis at once, and an isolation ladder ending in separate PROCESSES separated global from shared.

- Explain why `read-only` recalls peak at TWO workers on 22 cores.

## Part 166 — the reconciler fix: built, run, INCONCLUSIVE, and the reason is the finding

✅ done 2026-09-07. `extract+reconcile-fixed` is in the tree and this is its record, so nobody re-derives
why it exists. `docs/memory-measurements.md` §5 carries the diagnosis it tests.

**The hypothesis**: the shipped reconciler gates on top-1 similarity ≥ 0.80, but `memory-density` measured
that a CORRECTION resembles ~1 stored entry and a RECURRENCE ~6 — so a pairwise gate fires on both, and
every wrong deletion removes a CONFIRMATION of a still-true fact. The arm changes both axes: gate on
neighbourhood DENSITY, and REINFORCE the replacement rather than deleting what it supersedes (the
retrievability contract forbids weakening, so raising the winner is the only non-destructive direction).

**It could not be answered.** Against the good-standard session extractor the mechanism barely fires: **4
replacements** in the run and **10 dense pairs of 244 asked** (4%), against a pre-registered "substantially
> 0". `prefers current` and `stale@k` are IDENTICAL across control and fix; `current@k` moved 2 questions on
10 decidable, at p = 1.000.

**The reason is the result worth keeping**: good extraction (0.55× compression) leaves almost no
near-duplicate pairs to supersede — 4 replacements where the unbounded 4B extractor produced 122. **Write-time
reconciliation has little to do when extraction is good**, which weakens it as a lever rather than supporting
the fix. A powered re-run needs a corpus where supersession is dense, and none exists here.

- Test whether the reconciler's gate is what made it delete the wrong entries.

## Part 165 — the performance pass: the write-back share did NOT fall, and concurrency is measured

✅ done 2026-09-07, at the owner's direction to focus on performance. `docs/memory.md` §7.

**The write-back share is unchanged by D99 and D101.** §7 said it was *"expected to have fallen"* after ten
round-trips became one and three write-back calls became one. Re-run at the baseline's own 5 repeats it
reads **76% at 1k and 49% at 10k** against the recorded 75% and 50%. **The COUNT fell and the latency share
did not**, which is consistent with D101 — its claim is a count — and says the write-back's cost is not
dominated by store-call count.

**A single-repeat run said otherwise and was noise** (71% / 45%), which is the `pitfalls.md` trap about a p50
moving less than its own spread, met by the person who had just quoted the warning. The 100k cell is sharper:
35% at one repeat, **7%** at five, with the arms' spreads overlapping outright.

**CONCURRENCY is no longer a blank.** A default recall is **writer-bound**: throughput pinned near 160/s at
every worker count while p99 climbs **23× past a second**, at **zero errors** — the wait is absorbed by a 5s
`busy_timeout` under a 30s command timeout and never reaches a log. `ReinforceOn = None` is a concurrency
knob, not only a latency one.

**Pure reads do not scale either**, peaking at two workers on 22 cores. One explanation — `PRAGMA
journal_mode=WAL` on every connection open taking a lock — was implemented, measured, **refuted** (every cell
inside its spread) and reverted. What serialises a read-only open is still open.

- Re-measure the write-back share after D99/D101, and measure concurrency.

## Part 164 — what `Fuse` costs a GOOD judge, and the shipped default is a bet on judge quality

✅ done 2026-09-07, from "ranking is the lever — what can we do about it". `docs/memory-measurements.md` §5, **D105**
amended. That decision kept `Partition` as the default having measured its cost only against a WEAK judge;
what fusion costs at PERFECT judgement was never run, and it is the whole justification.

**The partition is right for a good judge, and only just: +2.0** (92.5% → 90.5%). So the default is not
merely inherited — but it is a **bet on judge quality**, paying +2.0 when right and costing **−10.5** with a
4B model (−12.0 on a second embedder). About 5:1 against, in a library whose own §6 spends three criteria on
choosing a small local model.

**No default moves**: the weak-judge side replicates on two embedders, the oracle side is one run on one
workload, and D105's objection stands — flipping it is a silent reordering no consumer detects at compile
time. What would justify it is the same pair on LongMemEval, where a verdict's effect on supersession is
unmeasured.

**The ranking lever itself is otherwise exhausted cheaply**, and the answer was already in the repo: a
cross-encoder reranker (`docs/memory-measurements.md` §5, 2026-08-15), blocked on a model download and an ONNX adapter
package rather than on a design question.

- Answer what can be done about ranking, given it is the lever.

## Part 163 — the ceiling rises with the pool, and the gap to it widens faster

✅ done 2026-09-07, from the owner's question "can we make the ceiling higher". `docs/memory-measurements.md` §5.
At shipped defaults the question cannot be asked: `VerificationDepth` (`limit × 4` = 80) and the gathered
pool (`limit × CandidateMultiplier` = 80) are the SAME 80, so the oracle already sees every candidate.

**Yes, it is raisable — 92.5% → 94.0% → 96.0% as the pool doubles, no knee.** So the residual misses are
material that was never GATHERED, not material the seed queries cannot reach.

**And it is not a fix, because ceiling and floor move OPPOSITE ways**: ×16 raises the ceiling 3.5 and costs
the real arm 2.5, widening the reachable-vs-retrieved gap from **9.5 to 15.5**. The lever is RANKING, and
9.5 points of headroom already sit inside the pool the engine gathers today — of which a real 4B judge takes
at most +1.0. **D59 quantified at every pool size rather than asserted at one.**

**A degenerate result was caught rather than published**: `+oracle+pool32` gathers 640 candidates from
369–689-turn conversations and scored 100.0% in every category — the fixture, not retrieval. This is the
SAME defect the LongMemEval bench already had a counter for, recurring here because the counter lived in the
other bench. `WarnIfPoolSwallowsStore` now guards it and reports zero for the rungs above.

- Answer whether the oracle ceiling can be raised.

## Part 162 — the pool knob is nearly FREE on the best arm, and the judge guidance was stale

✅ done 2026-09-07, prompted by the owner asking why the tables read 54.5% when "we hit 93% before".
`docs/memory-measurements.md` §5, §6, §9.

**The 92.5% is `+sem+rel-only+oracle` — a PERFECT judge, a reachability ceiling, not a score.** Answering it
exposed that Part 161 priced `CandidateMultiplier` on the SHIPPED arm (54.5%) rather than the one a search
deployment runs (83.0%).

**Run on the best arm, the knob is nearly free**: −2.5 at ×16 and −1.5 at ×32, against −10.0 on the shipped
arm. So *"a wider pool only adds competitors"* is a property of the FUSED ranking, not of the pool.
`+sem+rel-only+pool16` lands on **80.5%**, `vector`'s score to the decimal — a relevance-only ranking over a
widening pool converges on plain cosine.

**And the consumer-facing guidance was two weeks behind the measurements.** §6 said a judge is worth ~28
points and is *"the only shipped mechanism that repairs"* ranking — true of the synthetic corpus, while the
field runs put a small local judge at **−10.5** on LoCoMo at shipped defaults. §9's recipe handed a consumer
exactly that configuration. Both now carry the field table and the rule: halve `VerificationDepth` or set
`VerdictCombination = Fuse`. **No gate could see this** — no retired vocabulary, no registered count, nothing
dangling.

- Answer why the published tables read 54.5% against a remembered 93%.

## Part 161 — the SEARCH rung closes the pool ladder, and the shipped default is vindicated

✅ done 2026-09-07 — the third and last rung, on the workload the engine loses. `--pool` became shared
`FieldArms` entries (`+pool8/16/32`) rather than a second bench flag, so an arm name means one configuration
on both benches. `docs/memory-measurements.md` §5.

**LoCoMo evidence-hit falls 54.5 → 50.0 → 44.5 → 43.5** across multipliers 4/8/16/32. With
knowledge-update's +27.2 and temporal's −28.0, the three-workload sum at 4 → 16 is **−10.8**: the knob buys
suppression and costs both coverage workloads, so **the shipped `4` is vindicated rather than merely
unmoved.**

**It SHARPENS D59** (amended there). That entry established every miss as *reachable-but-outranked* by
replaying "wide open", which lifted `Limit` and so widened pool AND output together. This ladder holds
output fixed and gains nothing, so the evidence is already inside the shipped 80-candidate pool —
the tighter of the claim's two readings, and the one that makes its "a better formula is not the fix"
argument bite.

**Three exact controls**: `lyntai` reproduces its published 54.5%, `vector` its 80.5%, and `items/q` is 20.0
on every arm. Pre-registered as monotone-decreasing and smaller than temporal's −28; both held.

- Price `CandidateMultiplier` on SEARCH — the third rung, and the one that touches the retrieval gap.

## Part 160 — the coverage ladder INVERTS the pool curve, at almost exactly 1:1

✅ done 2026-09-07 — Part 159's successor, run the same day. `--pool 4,8,16,32` on `--temporal`, the class
that wants every flagged turn. `docs/memory-measurements.md` §5.

**All-evidence recall runs 47.7 → 33.3 → 19.7 → 18.2 as the multiplier rises**, exactly opposite to
knowledge-update's 31.4 → 42.9 → 58.6 → 57.1. **Δ 4 → 16 is +27.2 suppression for −28.0 coverage**, and
every intermediate rung is the same ~1:1 trade.

**So the shipped `4` is not an unexamined default — it is the COVERAGE END of a real axis**, and Part 159's
"27 points below the knee" is true of `clean` alone. Recorded because that framing was published first and
is one-sided; it is corrected in place rather than left to age.

**It is a better-behaved knob than `RetrievabilityWeight`** (~1:1 against that one's ~7:1), which makes it
the more honest dial to expose — but no default moves on either.

**Both controls held from a separate run**: `pool-4` reproduces `shot-1` to the decimal and `pool-32`
reproduces `fill` exactly, the same two identities as on the other class. The 18.2% was pre-registered from
the earlier `fill@1200` figure and landed on it exactly.

- Price `CandidateMultiplier` on a COVERAGE workload before anyone proposes moving it.

## Part 159 — it was the POOL, and the shipped `CandidateMultiplier` sits far below the knee

✅ done 2026-09-07 — Part 158's own load-bearing caveat, closed the next hour. `docs/memory-measurements.md` §5.
`fill` raised `Limit` and the engine gathers `Limit × CandidateMultiplier`, so it moved POOL and OUTPUT
together; `--pool M` varies the multiplier at a fixed output and separates them.

**The lever is pool depth, exactly**: `pool-32` reproduces `fill` on every quality column while returning
ten items from a `Limit: 10` recall. The two see the same 320 candidates and the shipped RRF policy never
reads `MemoryRankingContext.Limit`, so output size contributed nothing.

**`clean` runs 31.4 → 42.9 → 58.6 → 57.1** across multipliers 4/8/16/32: the shipped **4** is 27.2 points
below a knee at **16**, for 23% of `ms/q`.

**No default moves, and that is the finding rather than a hedge.** It is a SUPPRESSION dial — `stale@k`
62.9% → 12.9% while `current@k` falls 87.1% → 67.1% — so it buries both facts and the superseded one harder.
The same depth reads 18.2% on temporal all-evidence against `shot-1`'s 47.7%, which is the
`RetrievabilityWeight` shape again. A coverage ladder is the successor item.

**Control**: `pool-4` is the shipped configuration by a different route and reproduces `shot-1` to the
decimal, so the arm measures the multiplier and nothing about its own construction.

- Separate the POOL from the OUTPUT: `CandidateMultiplier` at a fixed `k`.

## Part 158 — the lever is RECALL DEPTH, and it corrects Part 157's closing claim

✅ done 2026-09-07 — the `k`-raised arm Part 157 filed as its own load-bearing caveat, plus a `--budget A,B`
ladder that shares one ingestion. `docs/memory-measurements.md` §5. Part 157 held every arm to the same characters and
concluded the walk beats cosine; it could not see that **`shot-1` never spent its allowance** (1,173 of
5,400, bound by `k = 10`).

**The same configuration is the best arm on one class and the worst on the other**, and the budget flips it:
depth with a WIDE output wins coverage (temporal 68.2%, +20.5 over the best walk arm), depth with a NARROW
output wins suppression (`clean` **57.1%** against the flagship 31.4%, +25.7), and each is catastrophic in
the other corner. `Limit` sets both the candidate POOL and the output SIZE, and the two metrics want them
split.

**57.1% is the best figure this repository has measured on the metric this design is for**, and it needs no
new surface — `Limit: 80, CharBudget: 1200`. Not a free lunch: a deeper pool buries both facts, the
superseded one harder (`stale@k` 62.9% → 11.4%, `current@k` 87.1% → 65.7%).

**Part 157's closing claim is retracted**: *"the best body is shot 1"* is false. Its conclusion survives on
different grounds — the winning arm does not walk, so a walk-level budget still earns nothing.

**The caveat is load-bearing and named rather than buried**: `fill` moves POOL and OUTPUT together, so
"depth is the lever" is a hypothesis. `CandidateMultiplier` isolates the pool and is the successor item.

- Let one shot FILL the budget: a `k`-raised arm under `--budget`.

## Part 157 — the walk wins at an EQUAL CHARACTER BUDGET, and expansion stops paying

✅ done 2026-09-06 — `memory-longmemeval --shots --budget N`, three haystack runs at full sample.
`docs/memory-measurements.md` §5. Every shot table until now let each arm spend whatever its slot count cost, so
all-evidence recall rewarded whoever returned MORE — the axis **D100**/**D102** say this design does not
optimise. The cap reproduces the engine's own `MemoryQuery.CharBudget` rule rather than inventing one.

**The claim survives its first equal-spend test on both classes** (+17.1, +28.8 and +10.6 points over
cosine), with the knowledge-update ratio narrowing 3.1× → **2.2×** — the part that was bought with a 9×
character advantage, now priced.

**Expansion never pays under a budget**, on either class at either value: shot 1 wins every table. The
published +4.5 for shot 2 on temporal reads **0.0** at 5,400 and **−28.1** at 1,200, because `Hold` upgrades
in place and the budget then drops the tail. *"Expand once"* becomes *"under a budget, do not expand"*.
**And the section's own "size-matched cosine wins" counterweight flips** — it matched ITEMS, and matching
CHARACTERS reverses the sign. Both readings are kept, because the disagreement is the finding.

**The instrument fix was worth 30 points on the smoke sample and is the reusable half**: the first cap
stopped at the first item that did not fit, where the engine SKIPS it and keeps filling
(`.claude/knowledge/pitfalls.md`, the replica bullet's second instance).

- Price the walk against cosine at equal context spend, which no published table had done.

## Part 156 — the write-time baseline, rebuilt to a standard worth losing to

✅ done 2026-09-04, at the owner's direction: *"we're not reproducing mem0, but we need close logic done to a
good standard — then what's left is our own invention."* The earlier extraction verdict rested on a 4B model
with an unbounded turn-by-turn prompt, so *"write-time consolidation does not substitute for decay"* was
partly a statement about the extractor. `docs/memory-measurements.md` §5.

**The baseline is better by its own numbers** — a strong model reading a whole SESSION and citing each
fact's turn, compressing to 0.56× where the old one inflated to 7.1×, mis-citing 1 fact in 921 — **and it
changed nothing about the verdict, which is the point.** Both halves ran.

**Decay alone beats the FULL write-time design without it by 21.8 points** (87.0% against 65.2%) on the same
extracted facts, and the two extract arms share an identical store, so the decay contrast has no seeding,
corpus or model confound. **Reconciliation COSTS 18.9 points on top of decay** and does so without losing
the answer — the found sets are identical and only the ORDER moves — which settles what the 4B run left
open: a capable model deleting 27 entries still hurts, so the harm is the mechanism as implemented rather
than the model tier.

**Two caveats grew.** Evidence survival fell to 90.1% because reconciliation deletes, bounding the extract
arms against `lyntai`; and the reconciler is OURS (top-1 candidate, 0.80 cosine gate), which is now the live
limit rather than the model.

- Build a field-standard write-time baseline with a strong model, so decay and the walk are measured against
  something worth losing to.

## Part 155 — the multi-session shot curve, and a finding that lasted one hour

✅ done 2026-09-04 — `docs/task-archive.md` Part 234's runnable third: `--multi --shots` on both variants,
with the class switch reusing temporal's all-evidence path because Part 234 measured the metric to be shared
rather than assuming it. `docs/memory-measurements.md` §5.

**The oracle overstated the second shot's gain by 4×** — +19.2 against the haystack's **+4.8** — the fifth
question on which that variant has proved biased unpredictably, and worse than the 2.7× Part 112 measured.
**Shot 3 is worth exactly nothing on the haystack**, so *expand once* holds, four classes in a row.

**A retraction, recorded because the wrong number was the exciting one.** The oracle's +6.4 for shot 3 was
read as refuting *expand once* — "a property of the two classes measured, not a rule" — and published for
about an hour before the haystack said otherwise. The pre-registered prediction had called both halves
right; the oracle is what moved me off it. **The lesson is the one already in `CLAUDE.md` — run it with
`--haystack` — and it now has a fifth instance and a retraction attached.**

**Plain cosine wins this class outright on BOTH axes**, which no earlier class showed: better recall AND
better recall per character. All-evidence recall is an archive metric and burying is what this engine does.

- Give LongMemEval's four remaining classes a shot curve (the multi-session third of it).

## Part 154 — the fusion result replicates on a second embedder, and CORRECTS its own shipped doc

✅ done 2026-09-04 — `+sem+rel-only,+judge,+judge+enginefuse,vector` re-run under `embeddinggemma:300m`,
because every figure behind **D105** was one embedder and this repository's standing trap is that a
recall-quality number is a property of the INSTRUMENT until shown otherwise. Prediction registered before
the run: levels move, `+judge` sits below the base, `+enginefuse` lands on it. `docs/memory-measurements.md` §5.

**The harm replicates and is slightly larger — the partition costs 12.0 points against 10.5** — which is
D105's load-bearing half and is not an artefact of one embedder. **The CURE does not fully replicate**:
fusion recovers 9.5 of the 12.0 and lands 2.5 short of the base, where on the first embedder it landed
exactly on it. About five questions at n = 200, above this instrument's ~1-point near-tie floor.

**So "removes a loss and adds nothing" was one embedder's phrasing of "removes MOST of the loss"**, and the
correction went to all four live copies — the shipped XML docs on `VerdictCombination` and
`MemoryVerdictCombination.Fuse`, D105, and the changelog entry. The archive keeps its original wording,
being a record of what that run measured. **The advice a consumer reads did not survive contact with a
second embedder, and that is the whole reason to run one.**

- Check whether the fusion result is a property of the embedder.

## Part 153 — the gist tier is REFUTED AS SCOPED, and closed rather than built

✅ done 2026-09-04 — `TASKS.md` Part 105, closed at the owner's direction after a research pass rather than
by building it. **A read-time tier cannot assert what its name promises**: three sweeps refuted every
combining form over member retrievability, and the field puts the abstraction at ENCODING — Zep/Graphiti
invalidate a superseded edge bi-temporally, Mem0 has a model decide ADD/UPDATE/DELETE/NOOP as the fact
arrives, both using information only the writer has. The read-time rules inverted because what distinguishes
the regimes was never recorded. Reasoning, the rejected alternatives and what would unblock it: **D106**.

**The name was borrowed and meant the opposite of the design** — fuzzy-trace theory encodes verbatim and
gist in PARALLEL and calls "gist is extracted from verbatim" a misconception, while its opponent-processes
principle warns that gist supports false memory where verbatim suppresses it. An abstraction outliving its
members is confabulation by construction, which is what **D41** already guards.

**The three untracked design records now have INDEX rows**, because their conclusions were live on one disk
and in no history — the column that file exists for.

- Build the gist tier.

## Part 152 — the SHIPPED fusion reproduces the bench-local proof, cell for cell

✅ done 2026-09-04 — the loop Part 151 left open. Its 83.0% came from a bench-local verifier emitting a
fused page as its verdict, which is not the engine's path: the engine reorders and then applies its own cut.
`+sem+rel-only+judge+enginefuse` drives the shipped `GraphMemoryOptions.VerdictCombination` and reads
**83.0%, identical to `+fuse` in all four categories**, with all three controls reproducing.
`docs/memory-measurements.md` §5.

**The two arms are provably INDEPENDENT, which is what makes the agreement evidence rather than a
tautology** — separate model call sequences, audits differing at 29.2 endorsements per call against 29.1 —
so identical cells are two implementations of one rule agreeing, not one path measured twice. Both declined
0 of 200, so neither was inert, and an arm landing on the partition's 72.5% was the pre-registered branch
meaning the option never reached that path.

**What it still does not measure is the REORDERING**, and that item stays open: evidence-hit@k reads the
returned set, so a fused page that contains the same 20 entries in a different order scores identically.

- Confirm the shipped option reproduces the fusion measurement.

## Part 151 — the verdict can COMPETE instead of partitioning, and the partition stays the default

✅ done 2026-09-04 — `GraphMemoryOptions.VerdictCombination` (`MemoryVerdictCombination.Partition` /
`.Fuse`), the one library change the judge measurements earned. Partition remains the default, so nothing
moves for anyone who does not set it. Reasoning and the rejected alternatives: `docs/DECISIONS.md` **D105**;
the measurement it rests on is `docs/memory-measurements.md` §5.

**The surface was the decision, not the code** — the item had been blocked on it. Additive won: changing the
default would be a silent reordering no consumer can detect at compile time (D18's major-bump shape), bought
on one model and one workload, which is the same reasoning that kept every other default in this subsystem
still this season.

**One contract fact carries the whole arithmetic: an unendorsed candidate is ranked LAST, never unranked.**
Scoring absence as zero makes the worst endorsement outscore the best non-endorsement at every rank, which
silently reproduces the partition — the error the bench made first. `MemoryVerdictFusionTests` pins it, and
the assertion was mutation-checked against that exact mutant rather than trusted for passing.

- Ship the verdict as a FUSION rather than a partition — the one library change today's runs earned.

## Part 150 — a budget in the JUDGE's prompt, and the API change it argues AGAINST

✅ done 2026-09-04 — **The judge does not obey a budget, and the number the library was going to supply is
the worthless one.** The shipped prompt says "Be selective" and names no count, so both budget arms were run
at the SHIPPED depth, injected into the system message at the `ILlmClient` seam so the shipped policy still
composes, parses and fails open. All three controls reproduced CELL FOR CELL, including the unbudgeted judge
at 72.5%/29.1 endorsed — the structural null control for the change itself. `docs/memory-measurements.md` §5 has the
table.

**It did not bind: asked for at most 20 of 80 the model endorsed 34.9, MORE than the 29.1 it endorsed
unbudgeted**; at most 5 gave 27.4. So input shaping is **two cases, not one rule** — the extractor obeyed the
same instruction at 8.3% over, because a generative task takes a count and a selective task over a visible
list does not. That correction went to `pitfalls.md`, amending the entry written the same day.

**The decision is negative and it is the useful part.** `budget20` — the caller's own limit, the number
`MemoryVerificationRequest` cannot carry — is worth **+0.5 points**, while `budget5` is worth **+4.0**. The
proposed API change would have shipped the useless arm. Budgeting is also the weakest lever measured: depth
40 reads 84.0% and fusion 83.0%, both removing the whole loss, where the best budget still lands 6.5 below
the unjudged base. **No default moved and the fusion item already filed stays the right one.**

**A pre-registered prediction was half wrong**, recorded rather than quietly restated: both arms were
predicted to land near the unbudgeted judge because 27–28 endorsements still exceed the 20-slot page. The
size claim held and `budget5` gained 4.0 anyway, so endorsement-set > page is necessary but not sufficient.

- Give the judge a budget in its prompt, and consider carrying the recall limit on
  `MemoryVerificationRequest`.

## Part 149 — the extractor's inflation was the PROMPT, and bounding it does not change the verdict

✅ done 2026-09-04 — **Part 148 filed its own load-bearing caveat and this closes it.** That run's 7.1
facts/turn was *"a property of this model and this prompt as much as of the design"*, so the extractor was
given a budget: `memory-longmemeval --extract --facts 2`, stated in the prompt and never truncated in code.
Inflation fell to **2.1 facts/turn** (11,271 → 3,308) with **evidence survival still 142/142**, and both
controls reproduced CELL FOR CELL including their McNemar counts, which is what licensed reading the two
runs against each other. Table, findings and limits: `docs/memory-measurements.md` §5.

**The verdict survives the strongest version of its own counter-arm.** Dilution is confirmed — `current@k`
recovered 75.7% → 87.1%, and on the fixed 70-question denominator the arm goes 49 → 59, so bounding recovers
10 of the 14 questions the unbounded extractor lost. But `stale@k` ROSE 40.0% → 57.1%: compression lets both
facts compete and resolves nothing between them, so extraction reshapes what is FOUND and still cannot BURY.
**`extract+forget0` remains indistinguishable from cosine (p = 0.327)** — the pre-registered prediction, and
the fourth arm in a row to land flat once forgetting is silent.

Two reusable rules went to `.claude/knowledge/pitfalls.md`: bound a model in the PROMPT and never truncate
its reply in code (a code truncation measures truncation), and COUNT how often it exceeds the bound, because
"the bound did not help" and "the model ignored it" are the same score. Here 8.3%.

- Give the extractor a budget and re-run `--extract`.

## Part 148 — write-time extraction against read-time decay: extraction is not a substitute

✅ done 2026-09-04 — **Extraction cannot stand in for decay, and without reconciliation it costs.** With
forgetting silent, extracted facts score 53.0% and are **statistically indistinguishable from plain cosine**
(McNemar p = 0.572) — the same verdict `+sem+forget0` earned on raw turns, so removing decay lands at
flat-retriever behaviour whatever the store holds. Alongside decay it *hurt*: 96.9% → 86.0%, `current@k`
90.0% → 75.7%. `docs/memory-measurements.md` §5 has the table; both controls reproduced their published oracle figures.

**The mechanism is dilution, not data loss, and the control is what settled it.** All 142 flagged turns kept
a fact (142/142), so nothing was deleted — but 1,589 turns became **11,271 facts**, a 7.1× inflation of
near-duplicate one-liners. Without that column the loss reads as a ranking failure and the wrong thing gets
fixed.

**It is measurable at all because the extractor carries each fact's source marker**, so a synthesized fact
keeps the provenance the model-free metric matches on. A third-party system's facts cannot be scored this
way, which is why this comparison is internal rather than a head-to-head.

**The load-bearing caveat, and it points at the next build:** reconciliation was NOT implemented — facts are
extracted, never merged or superseded. The failure mode measured is precisely what an ADD/UPDATE/DELETE pass
removes, so this isolates the halves rather than judging write-time consolidation, and says the value would
have to be in the reconciling half. `IMemoryGraphStore.DeleteAsync` makes it reachable.

**The RECONCILING half ran the same day and does not change the verdict.** A superseding fact now deletes
what it replaces, and it fired — **asked 1,427 times, replaced 122** — so the arm is not a duplicate of
`extract`. But `stale@k` moved the WRONG WAY (28.0% → 44.0%): removing 122 entries thinned the corpus and
let stale facts surface, so **it deleted the wrong 122**. And `extract+reconcile+forget0` reads p = 0.508
against cosine, the third arm in a row to land indistinguishable from a flat index once forgetting is
silent. **At n = 25 only `lyntai` clears significance**, so the ordering among the extract arms is not a
result — what the run supports is that neither half substitutes for decay.

- Try adding LLM extraction, and find out whether it replaces decay.

## Part 147 — the acceptance test, on one knob and two workloads

✅ done 2026-09-03 — **Decay off is a flat retriever; decay on is the entire supersession win.**
`+sem` and `+sem+forget0` differ in exactly one vote, which no earlier pair did — `+forget0` also drops the
semantic channel and `+sem+rel-only` also drops traversal. On the supersession class decay-off is
**statistically indistinguishable from plain cosine** (49.3% against 46.4%, McNemar p = 0.791 over 68 paired
questions) while decay-on reads 72.5% at p < 0.001; on LoCoMo decay-off reaches **83.0% against cosine's
80.5%**, so the base claims nothing extra. `docs/memory-measurements.md` §5 has both tables.

**`current@k` is IDENTICAL at 90.0% with the knob either way**, so decay changes what is buried and never
what is found — Part 140's mechanism, now on a one-knob pair instead of across differently-seeded arms. The
price is 6.5 points of LoCoMo, which a decay improvement is SUPPOSED to cost.

**A refinement the pair exposed:** here `stale@k` barely moves (90.0% vs 92.9%) while preference moves 23
points, so the win is ORDERING rather than exclusion — the semantic channel pulls the superseded fact back
onto the page and decay ranks it below. At the shipped default, which registers no semantic channel,
`stale@k` is 62.9% and the same headline comes from genuine suppression. Two mechanisms, one number.

**Not a statement about the default's level:** `+sem` reads 72.5% where the shipped engine reads 86.4%,
because the semantic channel costs supersession. The pair is internally valid, not a benchmark of the ship.

- Prove decay off reaches flat-retriever parity, and that decay is what adds the rest.

## Part 146 — D41's invariant measured at last: decay buries, and the weight at which it starts deleting

✅ done 2026-09-03 — **26 of 26 buried entries recover at the shipped default, mean rank 5.0.** A focused
query in the entry's own words returns every entry decay suppressed — 76.9% inside an ordinary ten-slot
page, 100% within a hundred — so decay costs an entry its position and never its existence. **The invariant
the whole design rests on had never been measured**, because every metric on record scores what a recall
RETURNED and D41 is a claim about what it did not. `docs/memory-measurements.md` §5 has both tables.

**The boundary is real and the shipped weight is inside it.** Recovery is 100% at weights 1 AND 2 and
collapses to 18.8% at 4, while the entry sinks continuously under its own query (mean rank 5.0 → 41.7 →
76.8) — so recoverability degrades gradually and then falls off a cliff between 2 and 4. Weight 4 is the arm
whose `stale@k` of 1.4% is the best suppression figure the benchmark has produced, and it was bought by
deletion. Walking the weight UP also showed the vote is
a volume knob rather than a discriminator: `stale@k` 62.9 → 41.4 → 1.4 and `current@k` 87.1 → 77.1 → 32.9
move together, because the vote ranks by AGE and the current fact survives only by being newer.

**Two controls did real work.** `decidable` caught `+forget4`'s "100% prefers current" for what it is — 23
of 70 questions answered at all. And a first recovery run measured only a ten-slot page, which cannot tell
"the ranking put it below the cut" from "gone"; it read as *"decay deletes one in five"* and was re-run with
a `deep@100` probe before anything was published.

**Two findings this opens.** An exact-content query never returns a buried entry FIRST (0% at rank 1), and
mechanically so: RRF weights retrievability equal to relevance, so recency outranks an exact match. And the
WALK recovers 76.9% using the original question — expansion does not consult retrievability, since
`ExpansionRetrievabilityFloor` ships at 0, so an n-shot walk partly undoes what one-shot decay buried. That
is the first figure **D98** has ever had.

- Prove decay buries rather than deletes, and find where that stops being true.

## Part 145 — the PARTITION was the harm, and the judge's confidence is anti-correlated with its usefulness

✅ done 2026-09-03 — **Fusing the verdict instead of partitioning it removes the entire 10.5-point loss**
(72.5% → 83.0%, same model, same depth, same 29.1 endorsements — only the combination rule changes). The
verdict is the one signal this engine combines by a hard partition while every other is fused by rank
competition (**D82**, **D103**), so an unendorsed candidate ranked 1st loses to an endorsed one ranked 80th.
Depth mattered only because it grew the endorsed set until the partition ate the page. `docs/memory-measurements.md` §5.

**It removes the harm and adds nothing**, and the diagnostic that explains why is the one this Part exists
for. A verifier can only change a call whose page held no evidence while something deeper did — **19 of
200**, matching the oracle's headroom exactly. On those the judge endorsed the deep evidence **10 times
(53%)** and put it in its own top five **0 times (0%)**, while its cumulative precision by its own rank runs
34.5% at rank 1 against a pool density of 1.49%. **Its confident picks are the ones the ranking already
had; the useful ones sit in a 2.6%-precision tail.** So no truncation or reweighting of this judge's order
can win, and the ceiling for this model is +5.0 points.

**Two instrument lessons, both paid for in this session.** A `+top5` arm scored its base and the run said
why — `! DEGENERATE, 99% same page` — because a fused arm is compared against the partition and a fusion
can be arithmetically EQUAL to one. The first fusion attempt was exactly that, silently, by giving
unendorsed candidates a zero judge term rather than a low RANK; `MemoryVerification.RelevantIds`' own doc
says a "no" is a judgement, not an absence. Both controls are now in the harness.

**Nothing shipped.** The fusion is measured through a bench-local verifier, which is sound for a metric that
reads the returned SET and is not a substitute for implementing it.

- Make the judge stop making results worse.

## Part 144 — it was the DEPTH: the same judge is level at 2× and catastrophic at 4×

✅ done 2026-09-03 — **Part 143's "capability floor" is a depth×capability interaction, and this corrects
it.** Varying only `GraphMemoryOptions.VerificationDepth` on one 4B judge: **83.0% at depth 20, 84.0% at 40,
72.5% at the shipped 80.** Same model, same prompt, same base arm. `docs/memory-measurements.md` §5 has the table.

**The mechanism is that selectivity collapses with list length.** The model endorses ~17% of a 20-item list
and ~19% of a 40-item one, then **36% of an 80-item one**, and its lift over chance holds at ~3.2× for the
short lists before falling to 1.74×. So a long candidate list does not merely dilute a fixed judgement — it
degrades the judgement, which then overflows a 20-slot page and displaces a ranking that was better.

**The null control is the half that licenses the rest.** At depth 20 the verifier sees exactly the page
being returned, so promotion can only reorder within it and the metric CANNOT move — it read the base cell
for cell in all four categories while its audit showed 3.4 endorsements per call and zero declines. The
judge ladder had never carried an arm that structurally cannot move.

**+1.0 at depth 40 is reported as LEVEL, not as a win** — two questions out of 200, inside the near-tie band
Part 119 measured. The robust result is the −10.5 → +1.0 swing, which is 21 questions.

**No default moved.** What changed is the ADVICE, on the two shipped options a consumer reads:
`GraphMemoryOptions.VerificationDepth`'s doc had cited D59's saturation sweep as locating the knee without
saying that sweep used a PERFECT judge, for whom depth is free.

- Test the depth the judge inherits before concluding anything about the judge.

## Part 143 — the REAL judge on that arm, and the capability floor it found

✅ done 2026-09-03 — **A 4B judge costs 10.5 points where the perfect one gains 9.5.**
`+sem+rel-only+judge` reads **72.5%** against the same arm's unjudged **83.0%** and the oracle's 92.5% —
the third branch of the prediction registered in the ladder before the run, and the one meaning the seam has
a capability FLOOR rather than a capability curve. Table, audit and caveats: `docs/memory-measurements.md` §5.

**The mechanism was measured, not inferred.** A `JudgeAudit` decorator (added this Part, mirroring the
existing `EvidenceRankProbe`) scores the model's endorsements against LoCoMo's own labels: **29.1
endorsements per recall out of 80 shown, at 2.6% precision and 63.4% recall, with 0 of 200 calls declined.**
The endorsement set is larger than the 20-slot page, so promotion REPLACES the ranking rather than refining
it, and unendorsed evidence leaves the page however well it was ranked. The judge beats chance by 1.74× and
that is far worse than what it displaces.

**Two controls make it a finding rather than a wiring report.** Fail-open scores the base exactly, so 72.5%
is only reachable by a judge that answered and was wrong — and the audit's zero declines confirms it. The
run was repeated cell-for-cell; the bench judge is temperature 0, so that bounds HARNESS variance and not
sampling variance, which the write-up says rather than implies.

Consequence for consumers: `LlmVerificationOptions.ClientName`'s XML doc now carries the floor, since the
sizing advice above it read as a pure cost/quality trade. One header defect fixed on the way past — the
`--retrieval` preamble claimed "no reader and no judge" for a ladder that has had verdict arms since
2026-08-31, in the raw output that gets cited as provenance.

- Finish the REAL judge arm — and BUILD IT ON THE RIGHT BASE, which is the part that changed.

## Part 142 — the judge on the arm that is actually good, and the headline it corrects

✅ done 2026-09-03 — **Worth +9.5 points, so the real-judge run IS worth spending.**
`+sem+rel-only+oracle` reads **92.5% against the same arm's 83.0%** at n = 200, improving every category —
the highest figure this benchmark has produced from this engine, against plain cosine's 80.5%. The
pre-registration called 88–95% with a smaller absolute gain than the +17.5 the oracle bought on `+forget0`;
both clauses held. `docs/memory-measurements.md` §5 has the table.

**It corrects a claim published earlier the same day.** Every verdict arm was built on `+forget0`, which
registers no semantic channel, so *"a pure formula beats formula-plus-oracle, the deficit was never the
model tier"* compared two arms differing in SEEDING as well as in the judge. The limitation was written down
when that claim was made, and measuring it turned the conclusion around. **What survives is narrower: a
judge cannot rescue a badly-seeded pool** — which is what `+forget0+oracle`'s 74.6% measures — and it says
nothing about a judge on a good one.

Consequence for the backlog: `docs/task-archive.md` Part 235's real-judge item builds on `+sem+rel-only`,
not `+forget0`.

- Run the oracle on the best mechanical arm before spending a model run on a dominated base.

## Part 141 — is there a configuration good at BOTH workloads? A frontier, and no free lunch

✅ done 2026-09-03 — **No, and the decision rule was missed by about a point on each clause, so no default
moves.** `+sem+forget2` (semantic seeds at shipped K, `RetrievabilityWeight = 2`) reads **69.5% LoCoMo /
78.8% knowledge-update** against the pre-registered ≥70% / ≥80%. It buys search at the best exchange rate
measured — 2.0 points per point of suppression — but the trade is smooth and irreducible rather than free.
Table and the mechanism: `docs/memory-measurements.md` §5.

**The clause that could have killed the idea did not fire**: `+sem+forget2` beats `+sem` on suppression
(78.8 vs 72.5), so pool and burial are separable and strengthening the burying vote works independently of
seeding. **`+sem5` is the keeper and it is backwards from its prediction** — a narrower semantic channel
retrieves identically (76.5% both) and suppresses far worse (59.1%), because per-source fusion ranks by
position WITHIN a source, so narrowing one concentrates its rank weight on its top hits and a near-identical
superseded fact is one of them.

Also closed here: the LoCoMo retrieval path now honours `--arms` when dropping configs (13 ingested to score
3 became 2; 4,706s → 755s for identical cells), and the ladder's arm names, which lived in three lists that
drifted twice in ten minutes, are asserted equal before a run starts.

- Find whether any configuration is good at both workloads.

## Part 140 — the two field workloads disagree by 7×, and the shipped defaults are the right side of it

✅ done 2026-09-02 — **`RetrievabilityWeight` stays at 1.** The LoCoMo-winning config ran on LongMemEval
knowledge-update for the first time and collapsed: `+forget0` **49.3% against the shipped default's 86.4%**,
a −37.1 where the same change is worth +5.5 on LoCoMo. Roughly 7:1 against moving it, so the question
`docs/task-archive.md` Part 235 filed as "not startable until both workloads are measured" is settled.
Table, mechanism
and the cross-workload trade: `docs/memory-measurements.md` §5.

**Why the cell was empty**: the LongMemEval bench had no arm ladder — arms hardcoded `["lyntai", "vector"]`,
engine taking no ranking policy — so every LongMemEval figure ever published here was the shipped default.
Built `FieldArms`, a registry both field benches read so an arm name denotes one config on each, and gave
LongMemEval `--arms` with per-arm stores and an ingestion filter.

**The durable half is the mechanism**: removing forgetting's vote leaves `current@k` unchanged and destroys
`stale@k`. It changes what the engine BURIES, not what it FINDS — and LoCoMo only scores finding, so its own
winner's cost is invisible there. **Any future arm that wins on LoCoMo owes this table a visit.**

Controls: the LoCoMo retrieval table reproduced all thirteen arms cell for cell at n = 200, and
LongMemEval's oracle variant reproduced its published pair. A third control was WRONG in the spec and caught
by reading — `.claude/knowledge/pitfalls.md` carries the rule.

- Take the arm ladder to LongMemEval, and price the LoCoMo winner where the design makes its claim.

## Part 139 — multi-hop: the item's premise was refuted twice, and the ladder had never run at full sample

✅ done 2026-09-02 — **The gap is 3.2 points, not 16.2, and the "depth or seeding" framing is dead.** The
item's premise fell twice. **By reading**: its "64.9% even with a PERFECT judge" is a PRE-FUSION arm that
**D103** superseded the day the item was written, and `docs/memory-measurements.md` §5 had recorded the post-fusion cell
as level ever since — the doc was updated and the item beneath it was not. **By measurement**: both readings
were n = 200 cells of ~37 questions, and at full sample `+sem+rel-only` reads 79.8% multi-hop against
`vector`'s 83.0%. Ordinary category deficit, no depth question follows. Tables: `docs/memory-measurements.md` §5.

**Two results outrank the one it was filed for.** `+sem+rel-only` clears plain cosine on the whole benchmark
(**82.6 against 81.1**) — the first powered confirmation of that — and the PERFECT-JUDGE arm is the worst of
the three at **74.6%**. A pure formula beats formula-plus-oracle. That does NOT establish that a judge adds
nothing: no arm pairs the judge with semantic seeding, which is what re-aims `docs/task-archive.md`
Part 235's judge item.

**The run needed a harness fix**, and it exposed a class of defect: `+forget0+oracle` could not be
CONSTRUCTED at n = 1,540, so the arm whose ceiling three maintained records quote had never run on the whole
benchmark. `docs/FIXES.md` has the incident; `pitfalls.md` has the rule and the two instrument facts.

- Multi-hop is the one category no judge fixes.

## Part 182 — the always-on tier, paid down 60% behind a generated command table

✅ done 2026-09-10. **D113**; the new gate is `node devtools/dev.mjs check-dev-loop [--write]`.
`CLAUDE.md` + `.claude/rules/*` went **141,330 → 55,828 characters** (~35k tokens to ~14k) against the
item's ~13k target. `## Dev loop` — 65% of `CLAUDE.md`, 44,909 characters documenting 38 of the 51
commands `dev.mjs` declares — became a GENERATED table of 4,172.

**Nothing was deleted without a home.** The per-gate narrative is the new `docs/GATES.md`; eleven traps
went to `.claude/knowledge/pitfalls.md`; the multilingual arms, the enrichment deltas and the
`memory-scale` clauses to `docs/memory-measurements.md` §5; the migration asymmetry to `.claude/knowledge/storage.md`
§Migrations; the `IMemoryGraphStore` default-body roster to `.claude/knowledge/extending-lyntai.md`.

**Six `check-counts` claims were anchored in exactly one sentence each, all in `CLAUDE.md`** — the trap
the item named, now filed. Each moved verbatim with the gate run between the copy and the delete, and the
three mechanisms that read `CLAUDE.md` BY PATH (`claude-doctor`, `check-samples`' baseline, D46's
predicate) all still resolve. Four dead or misleading references were repaired on the way, none of which
any gate could see: D101's "invariant 8", `extending-lyntai.md`'s bare `CLAUDE.md`, and two `§"quoted"`
citations.

**One claimed saving was refuted before it was taken:** deleting the rules tier's frontmatter buys 2,857
bytes on disk and ZERO context, because the harness strips it before injection.

- Pay `CLAUDE.md` and the rules tier down to ~13k tokens.

## Part 183 — the measurement record, split out behind a generated results index

✅ done 2026-09-10. **D114**; the new gate is `node devtools/dev.mjs check-measurements [--write]`.
`docs/memory.md` went **4,248 → 800 lines**: §5 is now `docs/memory-measurements.md`, keeping its number,
with `memory.md`'s §6–§10 deliberately NOT pulled up into the hole. Every result carries a
`<!-- result: … -->` marker and the index is derived from them — 75 results across 57 sections, of which
**13 measure the arm that actually ships**.

**Two of the item's own premises were refuted by the record and are the durable half.** *Backwards-only
`supersedes=`* — "chronological, so a replacement is written below" — is false in 6 of 75 cases, all one
shape: a heading announces the correction and quotes the figure it replaces beneath itself. Acyclicity is
what was actually wanted, so it is checked directly. And the item's proposed BODY scan for
`CORRECT(ED|ION)|RETRACT|STALE|SUPERSED` was built and measured at **63 hits, ZERO defects** — `stale@k` is
a metric here and supersession is the subject matter — so it was refused for a HEADING scan (5 of 57
flagged, 4 genuine). `.claude/knowledge/pitfalls.md` holds both, plus the two traps the split itself paid
for.

**141 `§5` citations repointed across 30 files**, which `check-links` names — plus **ten bare ones it
structurally cannot see**, its pattern needing a filename before the `§`.

- Split `docs/memory.md` §5 into `docs/memory-measurements.md`, with a generated results index.

## Part 184 — a superseded fix entry says what it KEEPS, at its head

✅ done 2026-09-10. Closes Part 178. Two `docs/FIXES.md` entries gained a `<!-- keeps: … -->` marker and a
head blockquote naming what still holds and what does not; the one correction block that sat at an entry's
FOOT moved up, and the two false claims it corrects gained an inline pointer where a grep actually lands.

**Not gated, and the refusal is the finding.** Both candidate signals were measured rather than assumed.
The vocabulary scan is the one `check-measurements` had already refused the same day at 63 hits and zero
defects. The structural one — content after the `**Verify.**` paragraph — is present in **42 of 50**
entries, because `**Introduced by.**` is part of the entry shape; the genuine self-retraction population is
**one**. A gate for a population of one, on a signal measured at 84% noise, is the mistake this Part spent
the day recording. The convention lives in `.claude/rules/repo-mechanics.md` §Fix log with that reason
attached.

- Add a `keeps=` header to superseded `docs/FIXES.md` entries.

## Part 178 — the cold-start measurement, closed in full

✅ done 2026-09-10, six items, `docs/task-archive.md` Parts 179–184. Opened from five instrumented probes,
not an opinion. It bought four gates — `check-backlog` (**D111**), `check-pitfalls` (**D112**),
`check-dev-loop` (**D113**) and `check-measurements` (**D114**) — paid the always-on tier down from
141,330 to 55,828 characters, and cut `docs/memory.md` from 4,248 lines to 800.

**The measurement and the four findings it produced are `docs/GATES.md` §The cold-start measurement**,
which is the record that owns them; this entry does not carry a second copy. The last of those findings —
*measure a candidate signal before shipping a scan over it* — came out of building the gates rather than
out of the probes, and refused two scans on its own terms.

## Part 185 — the task-shape taxonomy, and the four shapes it was scoped around were not the set

✅ done 2026-09-10. **`docs/model-tasks.md`** — every model-backed seam grouped by the SHAPE of the question
it asks. Reached from `CLAUDE.md`, `README.md`, `docs/memory.md` §Model-backed steps and §10, and
`.claude/knowledge/model-decoupling.md` §Give the model only what it is genuinely better at. A new
maintained doc rather than a `docs/memory.md` section, at the owner's call: it spans cortex scorers, the
tool loop, agent sessions and the embedder, and that file's banner scopes it to the memory subsystem.

**The item's own four shapes were not the set, and that is the durable finding.** Sweeping `src/` found
four more it could not name (affordance, embed, repair, delegate-a-run); **`score-a-pair` is two unrelated
tasks** — the cross-encoder carrying the only sub-500 MB evidence, and a generative graded-quality scorer
unmeasured at any size; and **`classify` overstated what this library hands a model**, whose largest
classify surfaces are deliberately deterministic. The doc's §3 carries the size evidence and the eight
blanks.

**A figure was corrected on the way through**, because it sets the bar for a still-open item. `TASKS.md`
Part 177's first item quoted the shipped judge depth as costing **−14.5**, which is in no maintained record
and follows from no published pair; the owning table gives 83.0% unjudged against 72.5%, so the cost is
**−10.5**.

- Write the task-shape taxonomy down as guidance.

## Part 186 — the pre-memory decision sweep: two defects, a hole in an existing gate, one new ratchet

✅ done 2026-09-10, closing Part 129. Swept `docs/DECISIONS.md` **D1–D38**, the range the item called
essentially untouched. `check-decision-claims` goes to ten predicates; guard-script tests 625 → 633.

**Two live defects.** **D2** claimed twelve storage domain interfaces against **thirteen** —
`.claude/knowledge/extending-lyntai.md` and the `add-storage-backend` skill both enumerate thirteen. And
**D6's own predicate had a hole**: it scanned DDL only, while the FluentMigrator version table D6 names
explicitly is declared as metadata PROPERTIES. Probed rather than assumed — renaming it back to the
colliding `VersionInfo` left the gate reporting a CLEAN tree. Both fixed.

**One new predicate, D9**, for what nothing else could see: renumbering a RELEASED migration leaves the
fresh schema byte-identical and the tags untouched, so both migration guards stay green while a consumer's
already-migrated database re-runs it. A ratchet over the numbers `v3.1.0` shipped.

**Two gates were designed and REFUSED, which is the reusable half.** A text predicate for D29 is defeated
by a one-token edit to the flag's definition (`.claude/knowledge/pitfalls.md` §A text predicate that checks
a flag's CALL SITES). A counter for D2 has nothing to derive from — no backend implements all thirteen and
`StorageFeature` groups them — so it would be a fourth hand-written copy of the number.

**Already gated, so deliberately not registered:** D31 (`RoutingPolicyTests`), D12 (`FeatureToggleTests`).
D3, D20, D21 and D29 hold and stay ungated; a claim with no extractable shape stays invisible to a predicate.

- Finish the sweep: `only` (21) and `placement` (12) claims, and the pre-memory entries.

## Part 187 — the RetrievabilityWeight frontier is a STRAIGHT LINE, so there is no knee to find

✅ done 2026-09-11. The item offered two branches and the measurement picked the second: **not worth
walking**. Six rungs over the weight, `docs/memory-measurements.md` §5 (`locomo-frontier-ladder-n200`);
four new arms in `FieldArms`, added to the ladder AND the report list the bench asserts equal.

**The argument was arithmetic before it was a run, and the run tested the arithmetic's assumption.** The
two known rungs interpolate to non-overlapping windows against the pre-registered rule — LoCoMo ≥ 70 needs
weight ≤ 1.93, knowledge-update ≥ 80 needs weight ≥ 2.19 — so on a straight frontier no weight passes.
Every rung then landed within **±1.0 point** of the straight line, slope **−6.8** against a predicted −7.0.
The exchange rate is constant, so no weight is a bargain. `RetrievabilityWeight` stays at 1.

**The suppression half was deliberately not spent**, on a rule fixed before the run: a straight search axis
settles it. The four new rungs therefore carry no knowledge-update figure, stated so nobody infers one.

**Two findings the item did not ask for.** All three published anchors reproduced cell for cell across a
SERVER swap — Ollama then `llama-server`, same weights — so the server is neutral on this workload,
measured rather than assumed. And the environment nearly ate the run: a port believed free was already held
by a neighbour serving a DIFFERENT embedder of the SAME dimension, so the request succeeded, the dimension
check passed, and only reading back the served model caught it (`.claude/knowledge/pitfalls.md`).

- Walk the `RetrievabilityWeight` frontier, or decide it is not worth walking.

## Part 188 — the last three LongMemEval classes, and the one where expansion buys nothing at all

✅ done 2026-09-11, closing `docs/task-archive.md` Part 234. All six classes now have a shot curve; the
three single-session ones had no `--class` switch at all until this run. `docs/memory-measurements.md` §5
(`longmemeval-single-session-user-shot2-haystack`), 3,141.2s over 74,887 ingested turns.

**Shot 3 is worth exactly zero on all three, so *expand once* holds a sixth time.** `single-session-user` is
FLAT outright — 82.8% at every shot while the walk returns 7× the characters — which is a sharper negative
than a diminishing return, and the right answer for a class whose evidence is all in one session.

**The item's own prediction was half right, in the useful direction.** It expected
`single-session-assistant` to be unmeasurable even on the haystack; it moved 85.7 → 89.3. The reasoning was
right about the ORACLE (63% of its questions fit inside `k = 10` there) and `--haystack` is what fixed it.
Its zero-evidence warning also held exactly: 6 of `single-session-user`'s 70 questions carry no flagged turn
and `Load`'s guard dropped them, which is why n is 64.

**The finding worth carrying is not the curve.** Plain cosine wins all three, and
`single-session-preference` is the widest gap this record holds — **30.0% against 73.3%** at the same k.
No judge or reranker was in the loop for any cell.

**Two bench defects fixed to get here**, both of which would have published a wrong table: the three classes
were unreachable, and the all-evidence path printed `multi-session`'s banner and statistics for whatever ran
on it. A third cost an hour — a server whose batch was too small for the harness's own inputs passed every
short probe (`.claude/knowledge/pitfalls.md`).

- Give LongMemEval's four remaining classes a shot curve.

## Part 189 — a 1B judge is INERT, and the two sizes fail in opposite ways

✅ done 2026-09-11. Narrows Part 177's judge item to its RECENCY half; the smaller-model half is answered
and the answer is no. `docs/memory-measurements.md` §5 (`locomo-judge-1b-n200`), 791.0s, n = 200.

**`gemma-3-1b-it` Q4_K_M (806,058,240 B) against the 4B incumbent (2,489,757,856 B)** — same family, same
quantisation, so the run isolates SIZE rather than confounding it with family, recipe and recency at once.
It reads **83.0% at every depth**: on the base, on `@40` and at the shipped depth alike. Not a broken arm —
200 calls, 0 declined, 2.4 endorsed per call.

**The finding is that the two sizes fail in OPPOSITE ways.** The 4B ranks well and stops badly (17× lift at
its own top-1, 29.1 endorsements of 80, so it floods a 20-slot page and destroys 10.5 points). The 1B stops
fine and cannot rank — a 1.7× lift, which the harness reads as noise. **Its ceiling is zero**: on the 19
rescuable calls it endorsed the deep evidence 0 times where the 4B managed 10, so no promotion rule,
threshold or combination over it could win a point. It is safe by being inert, which is an operational fact
rather than a gain.

**So the route to a small-footprint deployment is a cross-encoder, not a small instruct model** — 468 MB
captures 6.0 of 7.0 points where 806 MB of instruct model captured 0.0, and that seam shipped the same day
(**D115**).

**Two probes nearly published a wrong mechanism** — they suggested template-copying, then instability,
while at n = 200 the endorsement count was stable at 2.4. **A probe sets a hypothesis; it does not measure
one**, and the pre-registered instrument check is what separated "inert" from "broken".

- Does a newer small INSTRUCT model judge better? (size half)

## Part 190 — ONE model serving MANY seams, priced: the CONTENTION half is bigger than the model-size half

✅ done 2026-09-11, closing Part 177's contention item. `docs/memory-measurements.md` §5
(`contention-mixed-recall-quiet-rerank`) holds the grid — {quiet, busy} × {judge, rerank} × {solo, mixed},
`dedicated` topology, repeat 3 — and every figure and control stays there rather than being copied here.

**Outcome: moving verification off the shared instruct model is worth most of the mixed-workload recall p50,
and only the first 2x of it is the smaller model.** The cross-encoder is about twice as fast solo; the judge
then pays a further ~10x for SHARING the chat server with annotation, which is the larger and more durable
half. Neither arm ships — verification is opt-in in full, and the `rerank` cells ran the bench-local
`CrossEncoderVerifier` rather than the shipped `CrossEncoderVerificationPolicy` (equivalent for COST, same
endpoint and same fixed top-N rule), so the row prices a cross-encoder's SHAPE in that slot.

**The bench's scope was wrong before it ran, and the correction is the reusable part.** It was planned around
FOUR contending seams. `IMemoryVerificationPolicy` is a SINGULAR slot (**D115**), so judging and reranking are
alternatives that can never contend with EACH OTHER; the seams are three and only annotation can contend.

**Two structural facts came out of the SETUP rather than the measurement**, and live in
`.claude/knowledge/pitfalls.md`: llama.cpp's router is a process SUPERVISOR (one child server per model, so
consolidating saves no memory), and `--embedding` / `--reranking` are PROCESS-WIDE. A quiet-device `rerank`
cell where contention does not resolve at 3 runs also produced a bench wording fix — the NOT-READABLE line
told a `--repeat 3` reader to re-run with `--repeat`.

- Price ONE model serving MANY seams, against one model per seam.

## Part 191 — the fused verdict, measured against a READER, and the item's own prediction fails

✅ done 2026-09-11, closing `docs/task-archive.md` Part 235's reader-facing item. `docs/memory-measurements.md` §5
(`locomo-verdict-partition-reader-n300`, `locomo-verdict-enginefuse-reader-n300`) holds the arms, both paired
bounds, the resolvable floor and every control; nothing is copied here.

**Outcome: the item predicted a null and the run refutes it — the SHIPPED partition costs a reader token-F1
and `+enginefuse` gives most of it back, with both paired 95% bounds excluding zero.** The half that held is
that fused, the arm still lands just short of its base (**D105**'s "removes a loss and never beats the
base"). The half that failed is the premise: it is the PARTITION that moves the score, downward, so "this
option costs nothing a reader notices" was never the question the run was going to answer.

**The mechanism is the one D105 names, now seen on a reader-facing metric.** The judge endorses far more
candidates than the page holds, so under `Partition` an endorsed set larger than the page REPLACES the
ranking rather than refining it. **The default is not re-opened** — one workload, one small local reader,
and D105 decided it on a different metric.

**The reusable half is the BOUND.** This instrument has been caught manufacturing effects at small n that
vanished at full sample, so a bare "we saw no difference" from it is worth nothing; the paired CI half-width
lets a null read as "nothing larger than X", and building it before the run is what turned an expected tie
into a refutable result either way.

**One defect found, fixed as its own concern** (`docs/FIXES.md`): the harness printed a hardcoded caveat
about its own judge-graded column whatever it had measured — wrong in size here, wrong in SIGN on the
bring-up run. It now derives that gap from the rows it just printed.

- Give the fused verdict a READER-facing measurement.

## Part 192 — the selective shape BELOW 20 candidates: a 468 MB cross-encoder wins

✅ done 2026-09-12, closing `docs/task-archive.md` Part 236's first item. `docs/memory-measurements.md` §5
(`decision-shape-single-evidence`) holds the grid, the controls, the noise floor and the prediction
scorecard; nothing is copied here. The instrument is `node devtools/dev.mjs memory-decision`, new.

- **Measure the selective shape BELOW 20 candidates.** One `select-from-list` call showing N options,
  against N `score-a-pair` calls scored independently and argmax'd. Which wins at N = 3..7, and at what
  size.

**Outcome: the best arm is the SMALLEST model in the grid, and the winning generative shape inverts with
model size.** A cross-encoder doing the scorer shape in one round trip matches a 5.3x larger instruct model
at three options and pulls ahead as the list grows. Among the generative arms, the large model wants to be
asked to CHOOSE and the small one wants to be asked to SCORE — but the small one in the select shape is not
judging at all: it emits a constant, which accuracy alone reads as "at chance" and only a position column
can distinguish.

**The first grid was RETRACTED, and the retraction is the more useful half.** It let a second correct
answer into the options — LoCoMo's evidence is a LIST and the harness read its first element — which
flattered the arm that ignores the options and penalised every arm that reads them. Three instrument
defects were found and fixed the same day: that one, a vacuous `oracle` control hardcoded to 100%, and a
position table pooled over list lengths. All three are in `.claude/knowledge/pitfalls.md`; the retraction
and what it cost each arm are in the measurement record.

## Part 193 — a decision IS expressible through a seam that already ships, and what is missing is the margin

✅ done 2026-09-12, closing `docs/task-archive.md` Part 236's second item — a desk audit, no code. Every
claim was read off the tree and spot-verified by hand.

- **Is a decision EXPRESSIBLE through the seams that already ship?** Answer this before proposing any
  surface. Which of `IPairwiseComparer`, `IToolLoop` and the verification seam already expresses it, and
  what is genuinely missing.

**Outcome: `IMemoryVerificationPolicy` already takes `(query, bounded candidate list with ids and full
`Content`)` and already distinguishes the two answers a decision seam must not conflate** —
`MemoryVerification.NoOpinion` (`Judged: false`, the seam did not answer) from `NothingRelevant`
(`Judged: true`, a first-class "none of these"). And `CrossEncoderVerificationPolicy` with
`EndorseCount = 1` **is** argmax over N scorings in one round trip, reachable from configuration via
`AddMemoryCrossEncoderVerification` (**D115**). So Part 177's "no new API is needed" held a second time.

**What is genuinely missing is ONE thing: no score or margin on the way out**, so a confidence threshold
cannot be expressed — left as a `decision-only` backlog item, because it is a surface question and the
owner's call.

**Why the other three seams are worse fits — and the fourth the item's own list missed
(`IMemoryAnnotationPolicy`, whose `Known` IS a bounded candidate set) — is `docs/model-tasks.md` §6**, which
gained the comparison so it sits beside the shape taxonomy rather than in an archive nobody reads end to
end.

## Part 194 — `affordance` measured: a 4B routes a roster well and cannot decline, and no prompt fixes it

✅ done 2026-09-12, closing `docs/task-archive.md` Part 236's third item. New bench `tool-affordance`; one
additive public option; **the shipped default deliberately unchanged.**

- **Measure `affordance` through the PROMPT protocol — the transport this library authors.** Roster size
  3-7, both models, a `random` null and an `oracle` through the real path, and the same trials posed as
  plain `select-from-list`. Count the failure modes a forced choice cannot express.

**Outcome: the selective half works and the REFUSAL half does not.** A 4B routes a seven-tool roster better
than a 333,590,944 B embedder and recovers most of what that embedder gets wrong — then invokes a tool on
**90-95%** of requests nothing on the roster serves, fabricating arguments to force a fit. A 1B fails the
mirror way and will not call a tool when one DOES fit. Figures: `docs/memory-measurements.md` §5; consuming
advice: `docs/model-tasks.md` §3.1; the reusable trap: `.claude/knowledge/pitfalls.md`.

**Two preamble rewrites were measured and BOTH refuted**, so **prompt wording is not the lever** — and that
negative result is the deliverable, because it stops the next session spending a round on it.
`LyntaiOptions.ToolProtocolPreamble` and `ToolLoop.DefaultProtocolPreamble` shipped so a deployment can try
its own wording; the default was left alone because no tested wording earned the change.

**What is NOT closed**: the native transport (blocked on a positive control), any roster larger than seven,
and argument QUALITY. The corpus is synthetic, so only arm differences transfer. The follow-up — bound the
roster before the model sees it — is in `TASKS.md` as decision-only.

## Part 195 — the newer-4B-judge question, retired by ruling rather than answered

✅ closed 2026-09-12 by the owner's ruling, **not by measurement** — no run was spent and nothing here says
a newer 4B judges no better. Moved out of `TASKS.md` Part 177.

- **Does a NEWER same-size instruct model judge better?** What remained open was RECENCY at a comparable
  size — a newer 4B-class instruct model that beats `+judge@40`'s 84.0%.

**Outcome: a 4B-class model is not a production candidate here, so its quality questions do not earn runs.**
The owner's words: *"we not really going to use 4B in most of our real production case, so our current 4B
benchmark should be enough."* That is an application of the ~500 MB sizing position `docs/model-tasks.md` §3
already carried, sharpened into a scope rule; the ruling is recorded there rather than as a `D<n>`, because
that is where the sizing target lives and what it governs is which candidates get surveyed.

**The 4B does NOT leave the benches** — it stays as the CEILING arm that makes a small model's score
readable. What stops is asking whether a better 4B exists.

**Its reranker shortlist survived the retirement** and moved to `docs/model-tasks.md` §3.2: it is the
candidate list for the shipped `AddMemoryCrossEncoderVerification` (**D115**), so it is maintained state
rather than open work, and leaving it inside a retired backlog item would have lost it.

**What this does NOT close**: the sub-100 MB cross-encoder item in the same Part, which is blocked upstream
on llama.cpp PR #21729 and is about the reranker role rather than an instruct model.

## Part 196 — sub-100 MB in the EMBEDDER role: it works, and the floor belongs to the vocabulary

✅ closed 2026-09-12. Moved out of `TASKS.md` Part 196, which opened the same day.

- **Survey and SMOKE-TEST sub-100 MB embedders — the role the sizing target was never aimed at.**

**Outcome: YES — four sub-100 MB embedders screen HEALTHY on llama.cpp today, and a 25,008,064 B one costs
2.4 points of tool routing at three options and 11.4 at seven.** A slope, not a point.
`docs/model-tasks.md` §3.3 is the filled cell; `docs/memory-measurements.md` §5 owns every figure and
caveat (`embed-screen-sub100mb`, `affordance-cosine-sub100mb`).

**Three findings the item did not ask for**, each recorded where it belongs: the multilingual size floor is
the TOKENIZER's rather than the cross-encoder role's, every candidate is a 512-position model and so cannot
be `IEmbedder`, and the STATIC class has no GGUF in existence — which is what the sibling decision-only
item now turns on.

**Instruments:** `embed-screen` is new; `tool-affordance` grew paired embedder arms and `--scorers-only`,
taking a candidate from ~2 hours to 94 seconds, which is what makes this axis a survey. **The screen was
wrong first** — it asserted a HARD fixture and failed four healthy models; `pitfalls.md` carries why a
screen asserts health and reports sharpness.

**Ratio, per `task-lifecycle.md`:** ~430 instrument lines against 0 in `src/`, and no default moved —
correct for a survey whose YES fills a documentation cell. Two runs followed the first actionable finding,
each pricing one decision (quantisation; the pooling mode).

## Part 197 — the NATIVE tool transport, unblocked by a survey and measured

✅ closed 2026-09-13. Moved out of `docs/task-archive.md` Part 236.

- **Measure `affordance` through the NATIVE transport — the positive control is a SURVEY, not a wait.**

**Outcome: the blocker was discharged by looking, and the measurement says the transports tie on accuracy
and differ completely in HOW they fail.** `docs/memory-measurements.md` §5 owns every figure
(`native-tool-positive-control`, `affordance-native-transport`).

**The control took one survey and one probe.** `tokenizer.chat_template` read out of the GGUF header shows
Qwen2.5, Qwen3 and Llama-3.2 all carry a tool section and gemma-3 none; `qwen2.5-0.5b-instruct` Q4_K_M then
emitted `tool_calls` on all six cells where `gemma-3-4b-it` emitted none, same run, same build. The item had
been marked `blocked · env` for a model nobody had gone looking for — which is the case
`task-lifecycle.md`'s *a NAMED download is a step* rule was written for, and it held.

**Three findings beyond the item's question**, each recorded where it belongs: accuracy is a wash while
CONVERGENCE is not (11-24% on the prompt protocol against 99.4-100% native, the largest effect in the
grid); `--jinja` is byte-irrelevant on this build and `tool_choice: "required"` binds only where the
template supports it; and **§3.1's headline was refuted** — a 491,400,032 B model reads six times a
806,058,240 B one on the same arm, so that result was the model's and not the size class's.

**Instruments:** a native `ILlmClient` for the bench, `--skip-baseline`, a paired transport table, and a
truncation counter that bounded the decline rate at 1.09% rather than leaving it arguable.

**Ratio:** ~250 instrument lines against 0 in `src/`. Two runs, the second added only after the first
raised an artifact the first could not rule out.

## Part 198 — the native transport's FALSE-CALL rate: the first lever found on §2's hardest case

✅ closed 2026-09-13. Moved out of `docs/task-archive.md` Part 236, which opened it the same day.

- **Measure the NATIVE transport's FALSE-CALL rate.**

**Outcome: on requests NO tool serves, native function-calling invokes one on 20-30% where the prompt
protocol invokes one on 90-100%.** And it is discrimination rather than blanket restraint — the same model
fires on 70-78% of requests a tool DOES serve, about **50 points of separation** where the prompt protocol
has none at all. `docs/memory-measurements.md` §5 (`affordance-native-false-calls`) owns the figures.

**What that changes is a shipped conclusion.** `docs/model-tasks.md` §2 recorded that an affordance task
*"cannot be bounded by the model AT ALL"*, with narrowing the roster the only remedy — a finding from
**D110** measured entirely through the prompt protocol. It replicates there on a second model and size
class, and the TRANSPORT turns out to be a second lever worth 65-75 points. §2 and §3.1 now say so.

**It also CORRECTED the previous Part's own headline, within hours.** Part 197 published *"on accuracy it
is a wash"* because two runs each had one cell under p = 0.05 and it was a different cell each time. The
third run showed the effect sizes had been stable throughout: native costs **2.4-9.6 points** at N = 3..6.
`pitfalls.md` carries the general form — a wandering p-value is an underpowered test, not a null.

**One instrument defect fixed on the way in:** the negative runner counted a trial the endpoint could not
answer as the model declining, which on an arm whose whole signal is declining would have read as
restraint. Now counted and excluded.

## Part 199 — a SECOND EMBEDDER on the LoCoMo QA half: no arm moves

✅ closed 2026-09-13. The EMBEDDER half of `docs/task-archive.md` Part 233; the reader half stayed open
there and was recorded `blocked · env`.

- **Widen the QA half: a SECOND EMBEDDER and a SECOND READER.**

**Outcome: swapping a 333,590,944 B embedder for a 146,146,432 B one moves no arm** — paired per question,
−5.5 to +2.9 token-F1 with all four CIs spanning zero — **and the conclusion the arms support survives
it**: the engine's arms sit below plain cosine under both. `docs/memory-measurements.md` §5
(`locomo-qa-second-embedder`) owns the figures and states the bound, which matters here because the result
is a null: ±7-12 point intervals exclude a large effect only.

**The reader half stays open, and its scoping was CORRECTED the same day.** This entry first said it needed
a model STRONGER than the 4B, to stop the reader's ceiling confounding the memory layer. The owner rejected
that and was right twice over: `--retrieval` already separates the layer model-free, and a reader above the
4B prices a configuration the sizing position rules out. The question is the deployment's — do the arm
differences survive at the size class that would actually run — so the second reader is a SMALLER one, and
both candidates were already on disk.

**Two caveats worth carrying** rather than rediscovering: the bench's embedder double implements only the
role-less `IEmbedder` overload, so every asymmetric model is measured without the prefixes it was trained
with — fair between models, below ceiling for all of them; and `memory-locomo` has no server orchestrator,
so a run takes whatever is listening.

**Ratio:** zero instrument lines in the tree — both halves were env changes behind a scratch orchestrator —
against three runs. The cost is now measured rather than guessed: four arms at n = 61 is ~16 minutes.

## Part 200 — the QA half widened: a smaller reader registers the memory layer LESS

✅ closed 2026-09-13. `docs/task-archive.md` Part 233's remaining half; the embedder half closed as
Part 199.

- **Widen the QA half: a SECOND EMBEDDER and a SECOND READER.**

**Outcome: the arm ORDERING broadly holds across three readers and the SPREAD collapses with reader size
— 14.7 / 8.8 / 4.7 points across 4B / 1B / 0.5B.** `lyntai-fused` is last under all three and `vector`
first or second; the top two swap at 0.5B within 0.9 points, which is a near-tie rather than a reordering.
So the differences the QA half exists to establish are real at 4B and shrink toward noise below it.
`docs/memory-measurements.md` §5 owns the figures.

**The consequence is a limit, and it is the useful half**: a smaller reader discriminates less between a
good page and a bad one, so memory quality buys less the smaller the consumer. That bounds what any
future memory work can be worth to a very small deployment, and it is now in `docs/deployment-shapes.md`.

**The item's own framing was CORRECTED mid-flight** and the correction is the durable part. It called for
a reader STRONGER than the incumbent, to separate the reader's ceiling from the memory layer's. Two things
were wrong: `--retrieval` already separates them model-free, and a reader above the 4B prices a
configuration the sizing position rules out. The right second reader is a SMALLER one, and both were
already on disk — so what read as a multi-gigabyte download was a re-run.

**Two caveats carried forward** rather than rediscovered: the `unknown` rate differs enormously by reader
(4B 5.3%, 1B 22.1%, 0.5B 0.0%), so token-F1 flatters a model that always guesses and cross-reader
ABSOLUTES are not comparable; and the DEPTH lever is a null at this power, not a demonstration of no
effect.

## Part 201 — mean-centering REFUTED, and a static embedder priced on the memory workload

✅ closed 2026-09-13. Opened the same day by `docs/task-archive.md` Part 233's successor and closed by
replication.

- **Re-test mean-CENTERING at power — the direction is consistent and the grid cannot resolve it.**

**Outcome: REFUTED, which is the outcome the item said would be the more useful one.** Centering looked
like it helped the static embedder (+8 / +6 / +3 paired trials) and hurt every transformer (−7 / −8 / −8)
on the hard fixture. Re-run on the `easy` fixture — a genuinely different confusable set over the same
tools — the static model's gain becomes **−1 / 0 / −2** and nothing moves anywhere. The sign flips; the
direction was the fixture's, not the model class's. `docs/memory-measurements.md` §5
(`affordance-centering-refuted`) owns it, including the one half that stays untested: the claimed harm to
a transformer is compressed by a 94-97% ceiling on that fixture.

**Power could not be bought the obvious way and the item said so.** The corpus is 168 trials by
construction, so `--n` cannot raise it — a second FIXTURE was the available replication, and it was enough
because the arm the claim was about had room to move.

**Alongside it, the static class was priced on the MEMORY workload** (archive entry shared deliberately:
one session, one instrument). `potion-base-8M` costs **0.5 points** on the shipped default and reproduces
`+forget0` exactly, because those arms do not seed semantically — it costs 10 points only where semantic
seeding is on. **How much an embedder is worth is a property of the arm**, which is why a class that looked
~12 points behind on a purely embedding-bound task is nearly free here.

## Part 202 — COMPLETENESS priced on the suppression workload, and the confound it was blocked on refuted

✅ closed 2026-09-13. `docs/task-archive.md` Part 233's last open item, and the last startable item in the
backlog.

- **Price COMPLETENESS on the memory workloads, where the reader half only measured LoCoMo QA.**

**Outcome: the lever is BUDGET-DEPENDENT on this class and at a tight cap it COSTS**, so the search
workload's *"completeness, not count"* is not a general rule about context budgets.
`docs/memory-measurements.md` §5 (`longmemeval-ku-completeness-budget`) owns the figures;
`docs/deployment-shapes.md` now names the workload its recommendation was measured on.

**The item's stated blocker was WRONG in both halves, which is why this took a scoring change to nothing.**
The class is scored by a turn TAG that survives truncation, not by the fact's text — so the confound could
not reach it — and `Detail` is not inert either: it changes item SIZE, which feeds every character cap and
`MemoryWalk`'s termination rule. The durable trap is filed in `.claude/knowledge/pitfalls.md`.

**So neither fix the item offered was the answer.** Identifier scoring was already in place, which makes an
unbudgeted run VACUOUS rather than confounded; a reader was never needed to get a figure. The measurable
arrangement was the third one — a CHARACTER CAP, which is what turns whole items into fewer items.

**The instrument is one arm and one rejection.** `memory-longmemeval --detail` adds `full` and REFUSES to
run without `--budget`, where it would be identical to `shot-1` by construction. Its vacuity control is the
non-binding rung, which reproduced `shot-1` to the decimal at 9.7× the characters — the thing that makes
the other rows readable as retrieval rather than eyesight.

**Unmeasured, named rather than implied:** completeness for a READER on this class, and whether the one
positive cell survives replication — this mode keeps no per-question outcomes, so it reports no interval.

## Part 203 — the tool loop's silent transport fallback becomes visible

✅ closed 2026-09-13. `docs/task-archive.md` Part 236's fallback item, ruled by the owner the same day.

- **Decide whether the PROMPT-protocol fallback should announce itself.**

**Outcome: it announces itself through `ToolLoopResult.Transport`, and NOT through a warning** (**D117**,
`CHANGELOG.md`). The distinction that decided it: reporting which transport ran is a FACT about the run, so
it needs no evidence and cannot age; a warning would have had to ship a roster-size threshold taken from one
model on a synthetic English corpus, which is the genuinely thin part of the evidence.

**Two shape choices worth keeping.** It is an init-only PROPERTY, never a record parameter — widening
`ToolLoopResult`'s primary constructor is a BINARY break, which the backlog item missed when it called the
addition simply "additive". And it is NULLABLE, because `None` (nothing registered, one plain completion)
and "a BYO `IToolLoop` never reported" are different claims that must not collapse.

**What it deliberately does not reach:** the STREAM door. `SessionEnded` is shared with `IAgentSession`,
which has no transport to report, so widening it for one producer was the worse trade.

## Part 204 — the verification seam stops discarding the score it judged on

✅ closed 2026-09-13. `docs/task-archive.md` Part 236's score item, ruled by the owner the same day.

- **Decide whether a verification verdict should carry a per-option SCORE.**

**Outcome: it carries them** (**D118**, `CHANGELOG.md`). `MemoryVerification.Scores` is an init-only
`IReadOnlyDictionary<string, double>?` keyed by candidate id, populated by `CrossEncoderVerificationPolicy`
and left null by the LLM judge, which has no per-candidate number to report.

**Two shape choices carry the reasoning.** EVERY scored candidate, not the endorsed subset — an endorsement
says nothing about how far ahead it was, so returning only winners would have shipped the same gap under a
new name. And NULL is not an empty map: null is a policy that reported nothing, a map of zeros is a
judgement that nothing matched, which is `Judged`'s own distinction one level down.

**The item's framing was wrong in one load-bearing way**, corrected before implementing: it called adding
the scores "additive", which is true of the surface and false of the CONSTRUCTOR. Widening the primary
constructor is a binary break and changes `Deconstruct` arity, which is why this and `Content` (**D108**)
are both properties.

**The vocabulary question is deliberately still open.** A decision is not memory and
`Lyntai.Memory.Verification` is the wrong home for one, but minting a parallel namespace for a single
property would have answered it by accident.

## Part 205 — a seam's `Model` stops losing silently

✅ closed 2026-09-13. `docs/task-archive.md` Part 235's last item, ruled by the owner the same day.

- **Decide whether a memory seam's `Model` should beat a candidate's — today it silently loses.**

**Outcome: neither of the item's three options — the ruling was a FOURTH** (**D119**, `CHANGELOG.md`). The
router's precedence stays, because `candidate.Model ?? request.Model` is correct: a candidate IS a
provider-and-model pair and letting the request win would dissolve its identity. What was wrong was only
that the losing case was SILENT, both seams being fail-open. Composition now throws when every candidate
the seam's client routes over pins a model of its own and none is the one asked for.

**The narrowness is the part that made it shippable.** A single unpinned candidate makes the request
reachable, so a partly-pinned list is not a contradiction and does not throw. The check catches what could
never work, never what is merely fragile — so a deployment that set one value keeps working, and one that
set both meant something and was getting the other.

**Recorded at registration, checked at composition**, so composition-root order stays irrelevant: the
client a seam names may be registered after it. A named client is checked against its OWN resolved
candidates, never the global list, since a name narrows candidates as well as providers.

**Two shipped XML docs said this was "inert, silently" and are now false**; both were corrected with the
code rather than left to rot.

## Part 206 — the tool roster gets a bound, after the measurement that justified one

✅ closed 2026-09-13. `docs/task-archive.md` Part 236's roster item. Ruled *measure first, decide after*,
and it closed in that order.

- **Bound the tool roster BEFORE the model sees it — the model supplies no bound of its own.**

**Outcome: measured, then built** (**D120**, `CHANGELOG.md`). `IToolSelector` with `EmbeddingToolSelector`
shipped and `AddEmbeddingToolSelector` to register it; `ToolLoop` narrows through it and is unchanged when
none is registered. Figures: `docs/memory-measurements.md` §5 (`affordance-roster-catalogue`).

**The item's stated blocker was wrong and cheaply so.** It read *"this grid tops out at seven tools"*; the
FIXTURE holds 42, and the cap was the `hard` difficulty drawing distractors from the gold tool's own
family, which holds seven. `easy` draws from every other family, so catalogue scale was reachable the same
afternoon rather than needing a new corpus.

**What the measurement bought, and why it is a floor.** A model-free embedder reads **81.5%** at 35 options
against 3% chance. It bounds the seam from BELOW twice: argmax, where a selector is scored on recall at a
cut; and the `easy` fixture, whose added options are semantically distant where a real catalogue is a mix.

**Fail-open in three ways** — no selector, a faulting one, an empty result — because the failure that
matters is dropping the tool the request needed. A `Limit` of zero or less narrows nothing, so a
misconfiguration cannot blind the loop.

**One real cost, declared rather than buried:** `ToolLoop`'s constructor gains an optional trailing
parameter, which is binary-breaking. It sits under **Breaking** in the changelog; an overload was refused
because it would pay for a pre-compiled caller that does not exist.

## Part 207 — the STATIC in-process embedder ships; the ONNX half is scoped, not built

✅ closed 2026-09-13 for the static half. `TASKS.md` Part 196's item, ruled the same day. The item stays
open for the TRANSFORMER × CPU package and now carries its scoping.

- **Ship an IN-PROCESS embedder: BOTH CPU cells of the 2×2, as two adapter packages.**

**Outcome: `Lyntai.Embeddings.Static` shipped** (**D121**, `CHANGELOG.md`) — `AddStaticEmbedder(dir)` over a
`model2vec` lookup table, no server, GPU or port. Verified against a real `potion-base-8M`, not only a
fixture.

> **The PACKAGE did not survive the next day** (**D122**): its dependency was priced, the tokenizer written
> instead, and the pieces split by kind — adapter to `Lyntai.Providers.Default`, tokenizer to
> `Lyntai.Core`. `AddStaticEmbedder` and every namespace
> are unchanged. Read the package name above as history; the capability is current.

**The framing improved while building it, and that is the half worth carrying:** the packaging boundary is
**MANAGED against NATIVE**, not static against transformer — `docs/deployment-shapes.md` owns it, and
**D122** owns what it then costs to cross.

**One worry checked rather than assumed.** A `model2vec` export ships a PRUNED vocabulary — 29,528 rows
against the base model's 30,522 — so a table and tokenizer disagreeing about which row an id names would
produce finite, plausible, wrong vectors. `vocab.txt` turned out to be the pruned list, exactly matching the
table, and a live test now pins it because the synthetic fixture structurally cannot.

**A contract pinned that would otherwise be invisible:** CONTENT tokens only, no `[CLS]`/`[SEP]`. Bracketing
would fold two rows into every mean and shift each vector by an amount no smoke test could see.

## Part 208 — the TRANSFORMER x CPU embedder ships, and the CPU column of the 2x2 is complete

✅ closed 2026-09-14. `TASKS.md` Part 196's remaining item, authorised by the 2026-09-13 ruling.

- **Ship the TRANSFORMER × CPU embedder — an ONNX Runtime adapter.**

**Outcome: `Lyntai.Providers.Onnx` shipped** (**D124**, `CHANGELOG.md`) — `AddOnnxEmbedder(dir)`, managed
half only so the app picks CPU / DirectML / CUDA. One package fills BOTH transformer cells of
`docs/deployment-shapes.md`'s 2×2, which had the GPU one down as unbuilt.

**The owner's ruling was MEASURE FIRST, and the measurement changed what the package is for.** Before
building, the reranker hypothesis was tested: ONNX Runtime reproduces `ms-marco-MiniLM-L6-v2`'s published
pair to four decimals where llama.cpp's GGUF ranks it backwards, and an int8 export scores correctly at
23,200,716 B. So the package unblocks `TASKS.md` Part 177 as well as filling this cell
(`docs/memory-measurements.md` §5, `rerank-screen-onnx-runtime`).

**Embedders became providers on the way through.** `IEmbeddingProvider` gives them the `Id`/`IsAvailable`
the LLM and generation seams already had — additive, because adding a base to `IEmbedder` would break every
BYO implementation. **D124** carries why.

**Two things pinned that would otherwise be invisible:** correctness is checked against Python's
`onnxruntime` rather than against plausibility — a wrong pooling mode still returns finite, unit-length,
well-ordered vectors — and the DI registration uses a factory, because `AddSingleton(instance)` does not
dispose a native session.

---

## Part 209 — a vector-collection address could be forgotten ACROSS a task boundary

✅ closed 2026-09-15. `TASKS.md` Part 179's first item, from the D125–D138 design review.

- **Task isolation can be CROSSED: the graph engine's vector-collection address uses a bare `|`.**

**Outcome: `MemoryVectorCollection` now owns the address for every side**, separated by U+001F, so two
different (engine, task, scope) triples cannot name one collection and a task prefix cannot reach a
neighbour. The incident — symptom, the THREE spellings the address actually had, why validating keys was
rejected, and the re-index a deployment owes — is `docs/FIXES.md`, 2026-09-15.

**The reusable half is how the third spelling was found.** Two were obvious (the engine's helper, the seed
source's copy); the third was an inline interpolation on the engine's own similarity-SEARCH path, which
grepping for calls to the helper cannot see, because the sites that BYPASS an owner are the only ones that
can drift. The tests caught it, and only because they were first confirmed to FAIL with the separator
temporarily restored.

---

## Part 210 — the reranker becomes a provider, and the memory policy that uses it moves to Core

✅ closed 2026-09-15. `TASKS.md` Part 179's second item, from the D125–D138 design review.

- **A provider package implements a MEMORY seam, which is the thesis violation itself.**

**Outcome: `ProviderKinds.Score` + `IModelProvider.ScoreAsync` (**D139**).** An HTTP reranker is
`AddHttpProvider(id, o => o.Produces = ProviderKinds.Score)` — no new registration method, no new options
type, no new package, because `Produces` picks the `/v1/rerank` route exactly as it picks `/embeddings`.
The memory half is `ScoringVerificationPolicy` in `Lyntai.Core`, over any backend declaring that kind.

**It collected D130's own prediction verbatim** — *"a reranker is `Produces: [score]` — no new operation,
no new interface, no new family"* — which is the strongest evidence the capability model is right, since
the shape was predicted three days before anything needed it.

**The proof the split worked is in the tests.** The fused class's suite drove every POLICY assertion
through a scripted HTTP handler; the policy's tests now use a fake provider and need no wire at all, and
only the wire-shape tests kept the handler. D115's placement argument was FOOTPRINT ("no new dependency, no
new package"), which answers a packaging question — the layering question was never asked, and its entry
now carries that correction at its head.

---

## Part 211 — the routing action and the media kinds join the taxonomy they were copies of

✅ closed 2026-09-15. `TASKS.md` Part 179's third item, from the D125–D138 design review.

- **Two more instances of the D136 class — one taxonomy under two names.**

**Outcome (**D140**): `GenerationFallbackAction` → `Lyntai.Lifecycle.FallbackAction`, and
`GenerationKinds.Image/Video/Audio/Model3d` → `ProviderKinds.*`.** D136 merged the routing table's KEY and
left its VALUE duplicated; the kinds matched by name AND value, with backends setting one in a request and
the other in their capabilities — adjacent fields of one record.

**The test that separates this from a real distinction: is the difference in the TABLE or the VOCABULARY?**
The two routing policies genuinely differ — `Unsupported` surfaces on one and advances on the other, and
their unmapped defaults differ — and none of that moved. `GenerationInputRoles` is likewise NOT merged: it
says what an input IS TO a generation rather than what a backend produces, and it moved to its own file so
the next reader is not deciding by proximity.

---

## Part 212 — the FuseVerdict constant: a review finding REFUTED, and the comment that invited it

✅ closed 2026-09-15 as a **negative result**. `TASKS.md` Part 179's fourth item.

- **`FuseVerdict` reads a fresh default, not the configured options.**

**Outcome: NOT a defect — the code does exactly what its remark says.** The remark reads *"READ from
`ReciprocalRankFusionOptions` … so the engine and the shipped ranking DEFAULT cannot drift apart"*, and
`new ReciprocalRankFusionOptions().K` is the shipped default, read from the type rather than restated as
`60`. Nothing drifts. No code changed.

**And it should not track a consumer's configured `K` either.** This fuses the RANKING's order with the
VERDICT's — a different pair of signals from the lexical/semantic ones a `ReciprocalRankFusionPolicy`
combines — measured at the shipped constant, and the neighbouring `weight` states the same rule outright
(*"deliberately not a knob, because no run has priced any other value"*).

**What it cost, and what changed instead.** A careful reviewer read "READ from `ReciprocalRankFusionOptions`"
as "read the configured instance", which is a fair reading of an ambiguous sentence sitting two lines above
a `new` expression. The remark now says SHIPPED DEFAULT, says it is deliberately not the consumer's value,
and says why. The finding was worth having: it cost one comment and bought the next reader the answer.

---

## Part 213 — the memory domain roster: a second review finding REFUTED, by the gate that derives it

✅ closed 2026-09-15 as a **negative result**. `TASKS.md` Part 179's fifth item.

- **`CLAUDE.md`'s graph-memory roster says SEVEN domains and the tree has EIGHT.**

**Outcome: SEVEN is correct, and `check-counts` is why we know.** That claim is gated by a counter derived
from the tree whose rule is explicit — *"the seam is what makes a sub-namespace a DOMAIN — `IMemory<X>Policy`
declared in it"* — and `Lyntai.Memory.Seeding`'s seam is `IMemorySeedSource`. Editing the roster to EIGHT
turned the gate RED within a minute, which is the whole argument for having counted claims at all.

**The distinction is real, not bookkeeping.** The seven are pluggable DECISION rules over what the engine
already holds; a seed source PRODUCES the candidates they then decide about. `.Seeding` has the same
file shape — one seam, three implementations, its own options — which is exactly why a careful reader
looking only at the tree concluded it belonged.

**What changed: the sentence, not the number.** It now says the seven are `IMemory*Policy` seams and names
`.Seeding` as deliberately outside, with the reason. Two of Part 179's seven findings were refutations, and
both came from prose that was true and misreadable — the same shape as Part 212.

---

## Part 214 — shared vector arithmetic moves to Core, and a tamper test that tampered with nothing

✅ closed 2026-09-15. `TASKS.md` Part 179's sixth item.

- **Pooling maths is `internal` to the ONNX package and re-implemented by hand next door.**

**Outcome (**D141**): `VectorMath.NormalizeInPlace` in `Lyntai.Core`**, called by both the ONNX adapter and
the model2vec one. The two ship in different packages and adapters never reference each other (**D25**), so
Core was the only home either could reach — the general rule being that an adapter holding
runtime-INDEPENDENT arithmetic has put it one layer too low.

**Half the finding was REFUTED.** The two MEANS are not duplicates: ONNX pools a `[token, width]` tensor
over an attention mask, where including padding shifts every vector by how long the batch's longest text
happened to be; model2vec accumulates lookup-table rows by id, with no mask and no tensor. Merging them
would have needed a flag. Only the L2-normalize was genuinely one function twice, and the split copies had
already cost something measurable: the zero-length NaN guard was ARGUED in one and merely present in the
other.

**A separate defect surfaced on the way and was fixed.** `Tampered_recovery_wrap_throws` replaced the last
two base64 characters of a RANDOM wrap with a constant `"AA"`, so on the runs where the wrap already ended
that way it tampered with nothing and asserted that an untouched envelope throws. It presents as flakiness
and is not — an instance of `TASKS.md` Part 99's watch item with a real root cause. The mutation is now
derived from the input with an `Assert.NotEqual` control; the trap is in `pitfalls.md`.

## Part 215 — ONE small model doing MANY jobs: the sub-100 MB cross-encoder, reached and priced

✅ closed 2026-09-15. `TASKS.md` Part 177, both halves.

- **MEASURE the sub-100 MB cross-encoder that now exists — and give the library a way to reach it.**

**Outcome, reaching it: `AddOnnxCrossEncoder`** (`Lyntai.Providers.Onnx`, commit `c1a62871`) — an
`IModelProvider` producing `ProviderKinds.Score` over `[CLS] q [SEP] d [SEP]`. **It needed no new seam**,
which is the reusable half: `AddMemoryScoringVerification` already selects on that kind (**D139**), so the
bespoke `IMemoryVerificationPolicy` the item assumed was never required. A multi-label head is refused
rather than read at column 0.

**Outcome, pricing it (`docs/memory-measurements.md` §5, `locomo-onnx-sub100mb-n200`): it WORKS and is a
THIRD as good.** +3.0 evidence-hit at 23,200,716 B against `LAMAR-600m`'s +9.0 at 468,393,760 B, of 9.5
reachable, same tree and embedder. The fp32 sibling is identical in every cell, so precision is not the
lever; the 512-token window never bit (0 of 16,000 pairs), so the pre-registered confound is ruled out; it
regresses multi-hop by 5.4 points, which is the row a deployment should read rather than the overall.

**What the item got WRONG**, both already-fixed premises rather than open questions: it assumed a bespoke
policy after opening with "no new API is needed", and its blocker ("no sub-100 MB reranker scores
correctly") was llama.cpp's converter rather than the model class.

**A stale bench control nearly made it unreadable** — the locomo semantic probe still spelled the
pre-U+001F vector-collection address and reported `vectors=0` over a full store. At the head of its own fix
(`docs/FIXES.md`), with the trap that a harness asserting against an internal address IS a spelling of it;
the Windows symlink byte-count trap it also surfaced is in `pitfalls.md`.

## Part 216 — a real annotator DRIFTS, and size is not the lever

✅ closed 2026-09-15. `TASKS.md` Part 65's last item, opened 2026-08-12.

- **Subject drift is bounded but not eliminated, and nothing measures how often a MODEL drifts.**

**Outcome (`docs/memory-measurements.md` §5, `annotation-drift-three-models`): drift is 41.7%-87.5% across
three local models, and it does NOT fall with size.** 5× the bytes made English worse and Chinese better,
with no monotone relationship and the best English model the worst Chinese one. The failure modes are
OPPOSITE — a small model answers a near-unique handle per fact, a larger one topic-level handles spanning
unrelated clusters — so a prompt fix aimed at one moves the other the wrong way. The instrument is
`node devtools/dev.mjs memory-annotation-drift`; the practical rule landed in `docs/memory.md` (screen the
annotator you intend to ship) and `docs/model-tasks.md` §3 (annotation is no longer a blank cell).

**The item was UNBLOCKED by a re-check, not by anyone acting on it.** It had read "this machine holds
exactly one chat model" since 2026-08-28 while three sat on disk, every one of them already quoted by other
measurements here. **An `env` blocker expires silently**, because nothing fails when an environment grows;
that is now the banner's fourth route an item arrives by.

**What the published CEILING now needs.** `memory-annotation` reports the mechanism with a PERFECT
annotator, and this says a real one reaches a fraction of it — so that figure is a ceiling to read with a
discount rather than a result a deployment inherits.

**The instrument gates itself**, which is the reusable half: drift alone is vacuous (a model answering one
handle for everything drifts 0%), so collapse and empty are reported beside it and a PERFECT self-check arm
must score 0/0/0 or the table is declared broken. It needs no engine, store or recall — drift is a property
of `IMemoryAnnotationPolicy` alone.

## Part 218 — the code-side half of subject linking: all three signals refuted, and a retraction

✅ closed 2026-09-15. `TASKS.md` Part 217, opened and closed the same day, at the owner's direction:
"find a way to improve by CODE rather than purely rely on the model".

- **Price a RECENCY/adjacency channel for subject linking.**

**Outcome: subject linking is MODEL-BOUND.** All three signals entity resolution uses are now priced and
all three fail (`docs/memory-measurements.md` §5). Name similarity and shared fragments move 1 of 6 cells
(`annotation-drift-code-reconciler`); co-occurrence cannot reach a drifted handle, which never appears
alongside its anchor; and recency is `annotation-drift-recency-gap`. **Spend on the annotator, not on
reconciling its output** — now in `docs/memory.md` and `docs/model-tasks.md` §3.

**The recency result is the one to remember, because the naive version of it was spectacular.** Scored on
the consecutive fixture it reads 87.0% → 34.8% drift at no collapse cost, the largest single win in the
memory record. Interleaved one fact apart it buys 8 points and collapses HALF the handle space; seven apart
it makes one model worse. The item was opened refusing to measure it without an interleaved fixture, and
that refusal is what stopped an artifact shipping as a finding.

**It also RETRACTED a headline published hours earlier.** Building the interleaved fixture meant reading
the engine's own call site, which showed the harness had been assembling the annotator's context itself and
assembling a cleaner one — `Recent` per-cluster and unbounded against the engine's global 8, `Known`
unbounded against 24. Drift is worse in five of six cells, and because the bias was worth +24.5 points to
the smallest model and −4.2 to the largest it **inverted the ranking**: "size is not the lever" became "the
2.49 GB model is best in both languages". Both traps are in `pitfalls.md`.

## Part 220 — annotation as `select-from-list`: the fourth and last lever, refused

✅ closed 2026-09-15. `TASKS.md` Part 219, opened and closed the same day.

- **Price annotation as `select-from-list` instead of `extract`.**

**Outcome (`docs/memory-measurements.md` §5, `annotation-shape-select-from-list`): REFUTED, and the shipped
generative shape stands.** Making reuse structural rather than instructed does not reduce drift — it trades
drift for COLLAPSE. `gemma-3-4b-it` answered ONE handle for eight unrelated entities (0.0% drift, 1 of 1
handles collapsed, 8 of 32 facts declined); `qwen2.5-0.5b-instruct` managed four to six handles, three to
five of them spanning clusters, and did not beat the shipped shape. A shorter list does not rescue it.

**The mechanism reproduces a finding this repository already had.** Offered a list and a "none of these",
both models take the list — §3.1's constant-emitter, one seam over.

**This closes the question the owner asked** ("improve by CODE rather than purely rely on the model") for
this seam: all four alternatives to a bigger annotator are now measured and refused — name similarity,
co-occurrence, recency, and the shape. `docs/memory.md` and `docs/model-tasks.md` §3 say so.

**One harness bug, and it is a design rule for any selective seam.** Offering the list when nothing is in
use yet is an ABSORBING STATE — an out-of-range pick is dropped, so no handle is recorded, so the list never
grows: 32 of 32 unlabelled, presenting as "this model cannot do the shape". A selective seam must bootstrap
generatively. In `pitfalls.md`; the figures above are from after the fix.

## Part 221 — full code review of the 2026-09-15 line

✅ closed 2026-09-15. `TASKS.md` Part 221, scheduled by the owner and run before anything else.

- **Review `bfd49190..ca670dd4`, the whole 2026-09-15 line.**

**Outcome: seven findings, all startable, filed as `TASKS.md` Part 222.** Two are defects of SILENCE rather
than of logic — a loud refusal placed under a fail-open consumer (RV1), and a seam whose backend is chosen
by DI registration order while both its siblings can be told (RV2). The rest are a shipped internals door
with a cheaper alternative already in the same csproj, a test whose citation overstates it, an undocumented
divergence between three duplicate-name policies, a judging seam with no cheap-backend lever, and five
one-sentence documentation gaps.

**The six judgement calls it was asked to second-read all stand**, one with a correction: the label-count
check must ALSO run at composition, because `ScoringVerificationPolicy` swallows the exception the refusal
raises. The `JsonExtract` split, the ordinal short-circuit, and omitting `BatchSize` were endorsed as
written. The `SelectingAnnotator` prompt needed nothing — `docs/memory-measurements.md` §5 already carries
the "ONE selective prompt, not a prompt search" disclosure, in the record that owns the measurement rather
than in the archive entry.

**What the review did NOT find, which bounds what the findings mean.** No adapter→adapter edge, no
namespace/folder divergence outside the documented exceptions, no public-surface break (the API baseline
delta is purely additive), and no DI captive dependency. The four observations judged not worth an item are
recorded in Part 229 so they are not re-derived.

## Part 223 — a cross-encoder refused a multi-label export where nothing was listening

✅ closed 2026-09-15. `TASKS.md` Part 222, RV1.

- **RV1 — a multi-label head must be refused at COMPOSITION, where the refusal survives.**

**Outcome: the refusal moved to composition and the shape rule now has ONE spelling.**
`CrossEncoderLogits.ShapeProblem` states it; `CrossEncoderLogits.Read` and the new
`CrossEncoderLogits.ScoreOutput` both ask it, so a graph cannot be refused at one time and accepted at the
other. `OnnxCrossEncoder.FromDirectory` resolves its output through `ScoreOutput` against the graph's
DECLARED `OutputMetadata`. A dynamic label axis states too little to refuse on and is still judged at read
time. Root cause and the mutation check are `docs/FIXES.md`; the reusable trap — **a guard's audibility is a
property of its CALLER** — is `.claude/knowledge/pitfalls.md` §Second doors.

**The sub-question it carried was answered by KEEPING fail-open.** A backend declaring `ProviderKinds.Score`
without serving it stays `NoOpinion`, because that is the seam's contract and is pinned separately; only the
LOG LEVEL moved, to Warning, since a mis-declaration is permanent where a transport blip is not. Audibility
was the defect, never the verdict.

**What it got wrong on the first pass, and the reason the fix is shaped as it is.** The composition check was
written against `InferenceSession` directly — unreachable by any test, so deleting it left the suite green.
That is the same defect the review filed as RV4 against `OnnxRegistrationTests`, reproduced by the hand that
filed it. Taking the DECLARATION rather than the session is what made the mutation check possible.

## Part 224 — the scoring verification seam can name its backend

✅ closed 2026-09-15. `TASKS.md` Part 222, RV2.

- **RV2 — the scoring verification seam cannot NAME its backend, and both its siblings can.**

**Outcome: `ScoringVerificationOptions.ProviderId`, and the reasoning is `docs/DECISIONS.md` D148.**
Additive and defaulted to null, which reproduces today's first-registered-wins exactly, so no existing
deployment moves. A name matching no registered backend — or one that does not declare
`ProviderKinds.Score` — throws where the policy is composed, because reporting `NoOpinion` instead is
indistinguishable from a reranker that simply had no opinion.

**A provider id rather than a `ClientName`, which is the part worth carrying.** The two sibling seams name
an `ILlmClientFactory` client because a judge is ROUTED and inherits candidates, fallback and governance. A
scoring backend is not routed at all — it is picked by what it `Produces` and called directly — so the only
thing to name is `IModelProvider.Id`, and reusing `ClientName` would have implied a routing story that does
not exist.

**Why it was reachable at all is D139**, not an oversight: *the declaration is the wiring*, so a
cross-encoder registered for a tool selector or a ranking policy became the memory verifier as a side effect
of existing. The option is the cost of that design, not a correction to it.

## Part 225 — the bench reaches one internal address by a compile-LINK, not an internals door

✅ closed 2026-09-15. `TASKS.md` Part 222, RV3.

- **RV3 — `InternalsVisibleTo(Lyntai.Benchmarks)` ships on a published assembly for two static methods.**

**Outcome: the grant is removed and `bench/Lyntai.Benchmarks` compile-links `MemoryVectorCollection.cs`.**
Same source file, so the one-spelling guarantee that motivated the door is untouched; the door is not. That
csproj already linked three files for this exact reason, so the mechanism was sitting four lines away.
`Lyntai.Tests` keeps its grant — it must reach what it gates, and that trade is worth making once.

**What made it worth undoing rather than tolerating.** `Lyntai.Core` is published and NOT strong-named, so
`InternalsVisibleTo` matches on assembly NAME alone and ships in the nupkg: anything built under that name
reads every internal in Core. Bought for two static string-composing methods. The correction is written at
the HEAD of the `docs/FIXES.md` entry that introduced it, because a reader arrives inside that entry from a
grep and its **Fix.** paragraph now describes a mechanism that is gone.

**No changelog line, deliberately.** It changes no public API and no runtime behaviour — a consumer could
only have depended on it by naming their assembly `Lyntai.Benchmarks`. Recorded so the omission is not read
as one.

## Part 226 — the ONNX registration is ONE call site, and therefore testable

✅ closed 2026-09-15. `TASKS.md` Part 222, RV4.

- **RV4 — `OnnxRegistrationTests` pins the RULE; the comment citing it claims the CALL SITE.**

**Outcome: both builder calls go through `OnnxBuilderExtensions.RegisterOwned`, and `OnnxOwnershipTests`
asserts the registration they really perform.** A tracking fake plus a real container, so it needs no model
on disk; a theory covers both the embedding and the plain path. Mutation-checked — rewriting `RegisterOwned`
to `AddSingleton(instance)`, the "tidy-up" the doc warns about, fails both theory cases.

**The two fixes are the same fix, which is the part worth carrying.** `pitfalls.md` already prescribes ONE
call site for a decision that was copied (the `MemoryEngineBuilder` entry), on the grounds that a second
copy drifts. Collapsing the copies is also what made the decision reachable by a test — the comment had
been citing `OnnxRegistrationTests`, which proves the DI premise against a hand-rolled fake and could not
see either builder call. That claim is now corrected in place rather than deleted, since the premise is
still worth pinning.

**The review filed this and then reproduced it**, hours later, writing the composition check of Part 223
against `InferenceSession`. Recorded because the lesson is not "remember to test the call site" — it is that
a decision welded to a native handle is unreachable by construction, and the tell is that you are about to
assert the rule beside the code instead of on it.

## Part 227 — a duplicate NAME follows two rules, and now says which

✅ closed 2026-09-15. `TASKS.md` Part 222, RV5.

- **RV5 — one hazard, three duplicate-name policies, and the divergence is written down nowhere.**

**Outcome: documentation only — `.claude/knowledge/extending-lyntai.md` opens with the two rules and a
table of which seams are under each.** THROW where the name is an address a caller uses
(`CompositeMemoryEngine` members, `AddLlmClient` names); first-wins where the collection is a fallback list
the router walks (`IModelProvider` ids, `ITool` names, `IJobHandler` types). Both were correct; what was
missing is that all five justified themselves with the SAME sentence and reached opposite conclusions, so a
reader who learned the rule from one seam got the other wrong.

**Nothing was changed in code, deliberately.** `LlmRouter`'s first-wins is load-bearing — the lookup folds
case on purpose, and the pooling and cooldown paths key on the same id — so refusing a duplicate would
reject registrations that are already merged one step earlier. The entry says so, to stop the next reader
"fixing" it.

**The concrete cost is named rather than left general**: give every `*Options.Id` a distinct value per
registration. Two `AddOnnxCrossEncoder` calls on the default id load two models, and the second is invisible
to the router and a coin-flip for `AddMemoryScoringVerification` unless `ProviderId` names one (**D148**).

## Part 228 — REFUTED: the pairwise judge CAN be pointed at a cheap backend, and always could

✅ closed 2026-09-15. `TASKS.md` Part 222, RV6.

- **RV6 — `IPairwiseComparer` is the one judging seam with no way to name a cheap backend.**

**Outcome: the premise was WRONG and no API was added.** `docs/model-tasks.md` §5 already prescribes the
route — resolve `ILlmClientFactory`, ask it for the name you want, hand the client to
`LlmPairwiseComparer`'s public constructor; the container registration is try-add, so yours wins. The
asymmetry with the two `ClientName` seams is deliberate and §5 names the distinguishing property: those two
also suppress reasoning on the request, worth ~25 s against ~1.5 s per judgement.

**What was actually missing was evidence and a pointer, and both shipped.** §5's claim had no test, which is
how a documented path stops being wired (`pitfalls.md`); there is now one driving it end to end through a
real container, with a positive control proving the unconfigured case really does run on the app's default
backend. The type's own doc had no reference to §5, so a reader of `LlmPairwiseComparer` could not find the
answer from where the question arises.

**Worth carrying: the review read the CODE and not the model-task inventory.** A finding of the form "seam X
cannot be configured" is answerable from `docs/model-tasks.md` §1's *named client* column, which had this
one filed as `composition root` the whole time. Check that table before filing another.

## Part 229 — the five documentation gaps, and the four observations the review REFUSED to file

✅ closed 2026-09-15. `TASKS.md` Part 222, RV7 — the last of that Part, which is now empty and removed.

- **RV7 — five documentation gaps the review found, each a sentence or two.**

**Outcome: all five landed.** The batched ceiling on `IModelProvider.ScoreAsync` and `OnnxCrossEncoder`
(the caller owns the list size; no `BatchSize` knob, deliberately); `JsonExtract`'s lenient/strict postures
stated where the frozen NAMES cannot carry them; that scan's comment-blindness; `EndorseCount` trimmed to
the rule with `docs/memory.md` holding the mechanism; and `CLAUDE.md`'s namespace map admitting the one
namespace Core does not own alone.

**One of the five became a test instead of a sentence.** The claim that a block comment containing `}` ends
extraction early was written, then pinned in `JsonExtractTests` with the no-brace control beside it — an
unverified caveat is worth no more than an absent one, and this one was inferred from reading rather than
observed.

**The four observations Part 222 judged NOT worth an item**, relocated here so Part 221's pointer resolves
and nobody re-derives them: `OnnxGraph.Pad` is unreachable without a model but the tokenizer guarantees its
three arrays are one length, so there is no route in; `SentenceTransformerConfig` is a bi-encoder name
serving a cross-encoder for one field, and its call site already says why; `Model2VecProvider` ships publicly
from `Lyntai.Providers.Basic` into Core's `Lyntai.Embeddings`, a real inconsistency frozen by **D70** and
now merely findable; and the eager `InferenceSession` leaks if a LATER composition step throws, which is
named on the builder doc because loading lazily would trade a loud startup failure for a quiet one.

## Part 231 — the two governance domains get the cross-backend contract every other domain had

✅ closed 2026-09-15. `TASKS.md` Part 230, GOV1.

- **GOV1 — the two GOVERNANCE domains have three implementations each and no cross-backend contract.**

**Outcome: `UsageTrackerContract` (9 facts) and `ResponseCacheContract` (5), wired to InMemory, SQLite and
Postgres, and both added to `PostgresContractCoverageTests`'s roster** so the Postgres leg is structural
rather than remembered. Two table-wide usage facts — the global total and reset-everything — are excluded
there by name with the reason, because on a shared container they read and delete other tests' rows.

**The tracker half found a real asymmetry; the cache half found none.** Postgres asserted a strict SUBSET of
SQLite's tracker behaviour: no global total, no unrecorded-consumer read, and — the sharp one — nothing
checking that `ResetAsync(consumer)` leaves the OTHER consumers intact, so a reset that dropped the whole
table would have passed. `PostgresUsageTracker` scopes its `DELETE` correctly, so this was LATENT; what was
missing was the mechanism keeping it that way on a domain where drift means a budget cap stops binding and
`BudgetedLlmClient` stops refusing. The cache, by contrast, already covered the same four behaviours on all
three backends — recorded so this is not read as evidence it had drifted.

**Mutation-checked on the gate, not just the facts**: deleting one Postgres delegator failed
`PostgresContractCoverageTests` naming the exact missing fact.

**What stayed per-backend, and why that is the boundary**: the cache's size-cap trim is set at CONSTRUCTION
and needs a far-future clock on the shared container, and "a fresh handle reads what another wrote" is
meaningless where there is only one handle. Neither is portable, so each suite states its own.

## Part 232 — a policy seam at the memory ROOT is no longer invisible to the gate that counts domains

✅ closed 2026-09-15. `TASKS.md` Part 230, DOM1.

- **DOM1 — the memory-domain RULE and the gate that counts domains disagree about the root.**

**Outcome: `countMemoryDomains` now counts an UNEXEMPTED root-level `IMemory*Policy`**, so one raises the
number and fails `check-counts` against every document saying seven. Proven by probe: a throwaway root seam
took the count to 8 and the gate named the claim. `IMemoryRemovalPolicy` is the one recorded exemption, in
`ROOT_MEMORY_POLICY_EXEMPTIONS` with its reason, and a guard test fails if that entry stops matching.

**The two rules had agreed by accident.** `CLAUDE.md` derives a domain from the seam's NAME; the counter
derived it from a seam in a SUB-namespace, and a policy declared at the root matched neither. This gate's
own reason for existing is that "two public seams sat outside the documented domain list on the eve of the
3.0 freeze" — it was built for this defect class and was blind to this variant.

**No namespace moved, and could not have.** Removal is a BLEND concern — which members a forget or prune
visits (**D75**) — rather than a stage of the decay pipeline the seven describe, and the namespace is public
and frozen (**D70**) either way. `CLAUDE.md` now says that where the SEVEN claim is made, so the seam stops
reading as a missing eighth domain.

## Part 233 — `TASKS.md` Part 109: LoCoMo says the shipped ranking defaults lose to plain cosine

✅ closed 2026-09-16 as part of `TASKS.md` Part 233, BL1. Opened 2026-08-29 by Part 110; retired holding
**zero** open checkboxes, having carried 49 lines of closure notes.

**The thread, and where each half landed.** It asked why defaults read 11.0% evidence-hit@20 against plain
cosine's 80.5% on a uniform-history workload. Ranking closed as Part 113 (traversal carries the arm;
`HopWeight = 0` costs 23 points). The QA half ran as Part 115 and found **D98**. Three harness defects moved
every figure it opened with — question isolation alone took defaults 31.0% → 54.5% (Part 118). The QA
widening closed as Part 200, centering as Part 201 by REFUTATION, completeness as Part 202.

**What it leaves standing**, since an empty Part reads as a live home for a question and that is what kept
it cited: the residual gap is the DESIGN, not a defect, and no arm measured has closed it.
`docs/memory-measurements.md` §5 owns every figure.

**Its "remaining half" had closed and the citations did not notice** — two records still called a second
READER "Part 109's remaining half" after Part 200 ran both halves. That stale pair is what BL1 predicted an
empty Part would produce, and is why the retirement is a repoint rather than a delete.

## Part 234 — `TASKS.md` Part 116: the n-shot WALK, and the surface D100 opened

✅ closed 2026-09-16 as part of `TASKS.md` Part 233, BL1. Opened 2026-08-29; retired holding **zero** open
checkboxes over 36 lines.

**The thread.** **D100** reframed the engine as a WALK rather than a single top-k, and this Part tracked
what that opened. The n-shot SURFACE shipped as Part 120 (**D102**, `MemoryWalk.WalkAsync`), the write-back
collapse as Part 117 (**D101**), LoCoMo contamination as Part 118, the expansion-floor sweep as Part 123,
and the last three LongMemEval shot curves as Part 188.

**The durable finding is not a curve.** *Expand once* holds on all six classes — shot 3 is worth exactly
zero — and `single-session-user` is FLAT outright at 82.8% while the walk returns 7× the characters. Plain
cosine wins all three of the last classes measured, and `single-session-preference` is the widest gap this
record holds: **30.0% against 73.3%** at the same k, with no judge or reranker in the loop for any cell.
`docs/memory-measurements.md` §5 owns the figures.

## Part 235 — `TASKS.md` Part 128: the retrieval gap is RANKING OUT candidates the engine already holds

✅ closed 2026-09-16 as part of `TASKS.md` Part 233, BL1. Opened 2026-08-31; retired holding **zero** open
checkboxes over 138 lines — the largest of the four.

**The thread.** Three LoCoMo ladders run at the owner's direction after "the memory system performance is
not good enough". **D59** decomposed the misses — 100% reachable-but-outranked, 0% unreachable — so the
edges were never the problem. Per-source fusion closed it as Part 131 (**D103**): the same arm reads
**83.0%** fused against cosine's 80.5%. The real judge closed as Part 143 (a 4B judge SPENDS 10.5 points
where a perfect one gains 9.5 — the seam has a capability FLOOR), multi-hop as Part 139 by refuting its own
premise twice, fusion as Part 151 (**D105**), the reader-facing check as Part 191, the frontier as Part 187
(no knee), `HeadlineChars` as Part 170 (**D108**), and seam-`Model` as Part 205 (**D119**).

**Two dead directions, recorded so nobody re-runs them:** weight-tuning is retired (semantic candidates are
outranked by CONSTRUCTION when a rank position and a cosine share one field), and preserving the cosine
MAGNITUDE is not the fix.

**One stale claim closes with it:** `model-tasks.md` called the `Model`-precedence question "still open
here" after **D119** settled it by KEEPING the precedence.

## Part 236 — `TASKS.md` Part 178: a DECISION system on a small model

✅ closed 2026-09-16 as part of `TASKS.md` Part 233, BL1. Opened 2026-09-12 at the owner's direction;
retired holding **zero** open checkboxes over 72 lines.

**The thread.** `docs/model-tasks.md` §1–§3 is its brief. Every item closed inside two days: the shape
comparison as Parts 192–193, per-option scores as Part 204 (**D118**), the tool roster as Part 206
(**D120**, `IToolSelector` + `EmbeddingToolSelector`), native transport as Part 197, false calls as Part
198, and fallback visibility as Part 203 (**D117**).

**The finding that governs the next decision seam.** The winning SHAPE inverts with model size — at
2,489,757,856 B one `select-from-list` call beats N `score-a-pair` calls at every length (`p<0.0001`), and
at 806,058,240 B it loses, because the small model stops choosing and emits a CONSTANT. **Pick the shape
from the size, never in advance.** And the arm to actually reach for is neither: a **468,393,760 B**
cross-encoder doing the scorer shape in ONE round trip matches the 5.3× larger instruct model at three
options and pulls ahead as the list grows — the only arm flat in N.

## Part 237 — a full-tree review: what a nine-decision day left behind, and the two gates that would have caught it

✅ closed 2026-09-16. A code-design + documentation review at the owner's direction, not a filed task.

**The finding that organises the rest: D140–D149 landed in one day, the code sweep was complete and the
PROSE sweep was not.** `src/*.csproj` descriptions were correct while `CLAUDE.md`, the README status
headline, `TASKS.md`'s goal line, `docs/AOT.md` and two shipped XML docs still advertised the
`Microsoft.Extensions.AI` bridge **D146** had deleted. `src/Directory.Packages.props` still declared it,
with a comment claiming NuGet unifies the transitive copy upward — measured against `project.assets.json`,
transitive pinning is off and the restore resolves MCP's own 10.5.2. The README said twelve packages
against eleven, in a phrasing `check-counts` could not see.

**Two gates, both measured before built** (`docs/GATES.md` §Writing a new gate) — details in that file:
`check-tautology` (6 defects / 0 false positives; validated RED against `git show HEAD:` copies, which
caught a duplicate-report bug in its own first version), and `check-backlog`'s empty-`## Part` rule (BL2).
`check-links`' Part half reached the CODE tier, where a stale scope note had excluded it: re-measured at
**150 citations across 73 files, 88 of them dead**.

**Three things the review REFUSED**, a negative result being the deliverable: widening the
`Providers.Default` rule (2 true against 16 false — the two sites fixed by hand), the noun form of the
MEAI rule (it fires on **D123**'s own heading), and any baseline claim from a Docker-down run — discharged
the same day at `3820 / 3853 / 33`, Part 239.

**Detail lives where it belongs**: the rename-collapse incident in `docs/FIXES.md`; the document
retirements in **D149**; five traps in `.claude/knowledge/pitfalls.md`; the gate descriptions in
`docs/GATES.md`; the five refactored methods and the dead `GenerationRouter` branch in `CHANGELOG.md`.

## Part 238 — the backlog's own accumulation, retired and then GATED

✅ closed 2026-09-16. `TASKS.md` Part 233, both items — opened 2026-09-15 by the owner asking why 736
lines held 8 items.

- **BL1 — retire the four Parts that hold no open work, and repoint what cites them.**
- **BL2 — `check-backlog` must fail a `## Part` that holds no open checkbox.**

**Outcome: `TASKS.md` went 760 → ~480 lines**, and the accumulation is now a gate rather than a habit.
Parts 109/116/128/178 are `docs/task-archive.md` Parts 233–236, one per thread. `check-backlog` fails a
`## Part` heading with zero open checkboxes; a heading that is not `## Part <n>` is invisible to it, which
is what lets a retirement leave a pointer behind without tripping the rule it just satisfied.

**BL1 estimated "19+ inbound references" and there were 96.** The gap is the finding: most citations are in
`bench/` and `devtools/` comments naming the thread an instrument belongs to, and **only 20 of the 96 were
ever visible to a gate** — `check-links` skipped the Part half on code entirely, and reads no archive at
all. That measurement is what put the Part half on the code tier (`docs/GATES.md`), where it immediately
found **88 more** dead references that had accumulated over every archiving since.

**BL1's stated premise was wrong and its conclusion survived.** It said all four numbers COLLIDE in the
archive; only 178 does — 109, 116 and 128 are gaps. They could not keep their numbers anyway, because the
archive allocates in LANDING order. **The real trap is the other shape:** `TASKS.md` Part 69's
`NeutralSaliencePolicy` citations belong to archive Part **71**, while archive Part 69 exists and is about
the embedder — a blind renumber would have resolved, gone green, and sent every reader to the wrong entry.

## Part 239 — the follow-on: an attestation withheld, and three items that were never blocked

✅ closed 2026-09-16, at the owner's direction after `docs/task-archive.md` Part 237. Not a filed task —
it began as two loose ends that Part 237 named and the owner asked to close.

**The attestation.** Part 237 refused to re-attest the baseline from a Docker-down run whose arithmetic
reconciled perfectly. Docker up, `verify` green on all 24 gates: **3820 / 3853 / 33** at `a8819277`, +3 on
the previous attestation (`DeclaredDeliveryIsBackedTests`), skip roster unmoved. **The withheld run is
worth as much as the taken one** — it is the concrete case for why that line takes a measurement, and
`CLAUDE.md` now carries it.

**Three items were never blocked, and the owner refuted the framing twice.** The first challenge —
*"verify properly rather than just call it blocked"* — found CLI12 (`@openai/codex` 0.154.0 is on npm with
a `win32-x64` binary) and GEN-VERIFY's `sd-cli` half (a 17.1 MB public zip) were both one download away,
which `task-lifecycle.md` calls a STEP in as many words. GEN6 moved to `decision-only`, the state's first
occupant, because its `env` half was contingent on the ruling rather than independent of it.

**The second challenge — "we should not only focus on fal" — found the bigger one.** THREE surfaces are
documented-not-measured and only fal needs a vendor; ComfyUI is self-hosted, free to probe, declares both
Image and Video, and was named in no summary sentence anywhere. It had survived two passes, **including
the pass that split the item**, because the split followed the axis the NAME suggested.

**What generalises is filed, not summarised here**: the re-check procedure in `.claude/rules/task-lifecycle.md`
(a negative re-check is evidence only of the question it asked), and the framing trap in
`.claude/knowledge/pitfalls.md` (an item named after its most expensive instance hides the cheapest, and
the reader who splits it inherits that framing). Startable set: 3, from 0.

## Part 240 — the nine-decision day's THIRD sweep of residue, in the tiers no prose gate reads

✅ closed 2026-09-16, at the owner's direction. Not a filed task — a documentation-drift pass over the same
D139–D149 window `docs/task-archive.md` Part 237 swept, asked of the tiers that pass did not scan.

**The finding that organises the rest: Part 237 swept `.md` and `.cs`, and the residue that survived is in
neither.** A published package id in `devtools/nuget-unlist.mjs` (`docs/FIXES.md`, and the trap in
`.claude/knowledge/pitfalls.md`) and FOUR `*.csproj` comments — the bundle still advertising the deleted
MEAI bridge, `Providers.Basic` calling itself "the DEFAULT set" with its deleted dependency's
justification orphaned onto the survivor, `Providers.Onnx` naming `Providers.Default`, and a bench note
listing `ExtensionsAi`. Part 237 had recorded *"`src/*.csproj` descriptions were correct"*, which was true
of the `<Description>` elements and false of the comments around them.

**Two gates measured, one widened and one refused.** `retiredTerms`' D70 pattern demanded a VERB and an
uppercase spelling, so the README's package table cell — `**Experimental.**`, in the file a consumer reads
first — matched nothing for a month in a document that gate does scan; widened at 1 true / 1 false (**D67**
takes `drift-ok`) and proven RED against the pre-fix line. Scanning `*.csproj` for retired vocabulary is
REFUSED: 0 hits over 18 files, so the four defects above are pattern misses, not scope misses.

**`CLAUDE.md` contradicted itself about the Postgres leg** — "+204 skipped is the Postgres leg" eleven lines
above "**The Postgres leg is 195 tests**… the quantity that carries forward". Re-measured off this session's
Docker-down run (`3616 / 3853 / 237`): it is 204, and it GROWS with the tree, so the DERIVATION carries
forward and the number does not.

## Part 241 — the duplicated commentary in the two SQL adapters, relocated to its owners

✅ closed 2026-09-16, at the owner's direction after `docs/task-archive.md` Part 240. Not a filed task.

**Measured first, which redirected the pass.** Duplication in `src/` is one axis — **380 of 397 cross-file
duplicated 8-line blocks are the SQLite↔Postgres pair**, and `Lyntai.Core` is effectively clean — but the
CODE half is already answered by `.claude/knowledge/storage.md` §Don't "dedup", and Core takes no database
driver, so the plumbing has nowhere to go. The PROSE half had never been looked at; the measurement and the
rule now live in that same section.

**Outcome over two rounds: 431 → 365 comment lines, identical 119 → 84, near-duplicate 96 → 82**, each
relocated rule landing on the thing that owns it — `MemoryNodeRow`, `MemorySignals.Salience`,
`MemoryEvictionPolicy.TracksAccess`, `MemoryPositionRow`, `MemoryReviewLogPacing`, and the contract members
`IMemoryGraphStore.{UpsertAsync,TouchAsync,LinkManyAsync,SeedAsync}`, `IJobStore.{ListAsync,ReportStepAsync}`,
`ITraceStore`, `IScoreStore`, `ICuratedMemoryStore.UpdateAsync`. **Round two was mostly DELETION**: the owner
already held the rule in most cases, so the second copy went without anything having to be written. Several
were CONTRACT rules living only inside two concrete backends, where a BYO store never looks, so this closed
gaps rather than only shortening files. **D150** takes the Governance-guard argument; the Dapper
process-global type-handler registry is now a trap in `.claude/knowledge/sql-storage.md`.

**§Don't "dedup" was itself stale**: it named `JobStoreSql` as "the one thing that IS shared" after four more
had joined it, so a reader was told to re-derive an extraction that already exists.

**Deliberately NOT deduplicated:** the `Use*Storage` XML docs, ~34 identical lines. Both are public surfaces
and a consumer sees only one, so that is the contract working rather than drift.

## Part 242 — release readiness: the release gate had not run, and it had not worked

✅ closed 2026-09-17, at the owner's direction — the framing that reorganised the session. The review was
not tidying; it was getting the tree ready to cut a release.

**The release gate was broken and nothing could have said so.** `consumer-smoke` — the only check that
compiles a fresh app against the PACKAGES — failed with four compile errors, its consumer fixture never
having been updated through the D125–D147 renames. Fixed against the shipped API baselines and green end to
end. Incident in `docs/FIXES.md`; the general shape in `.claude/knowledge/pitfalls.md`; `docs/GATES.md` now
states the cost of being outside `verify`, which it previously did not.

**A live release note said a package was renamed to itself**, with a refusal sentence naming the name that
shipped — both halves of one rename entry rewritten by the same sweep. `check-tautology` was green: its four
patterns are CONTRAST joiners, and a rename entry is written old-then-new, so the shape most exposed to a
rename campaign was the one it could not see. Two patterns added, measured at 1 true / 0 false over 811
files and driven RED against the real pre-fix file (`docs/GATES.md`).

**Two results that stop the next session re-deriving them.** The next release is a MAJOR — `## Unreleased`
holds 36 Breaking entries — and the deferred unlisting happens after it ships. **A separate migration guide
is REFUSED**: 24 of those entries carry an explicit old → new mapping and the other 12 are source-compatible
widenings that state their own compile-time consequence, so the Breaking section IS the migration path, on
the standard **D149** used to untrack the last guide.

**Baseline re-attested with Docker up**: `3820 / 3853 / 33` at `aae8cbcb`, unchanged across the session.

## Part 243 — `check-decision-claims` widened over the band nothing re-checked

✅ closed 2026-09-17, at the owner's direction, following `docs/task-archive.md` Part 242.

**The gap: 10 machine-checked claims against 150 decisions, and none of them in the D125–D147 band** —
nine decisions that landed in one day and reshaped the provider layer, shipping in the next MAJOR. Three
added, each verified by hand first and each chosen because its violation is SILENT: **D25** (a third-party
dependency in `Lyntai.Core`), **D127** (only `Id` and `Capabilities` required of an `IModelProvider`), and
**D129** (nothing outside `Core/Embeddings/` implements `IEmbedder`). 13 claims, all green.

**Validated RED twice over**: synthesized fixtures for the patterns, and then the REAL files mutated in a
temp copy — one operation's `=>` removed from `IModelProvider`, one off-band `PackageReference` added to
Core — which is the proof a fixture cannot give. `docs/GATES.md` carries what each covers.

**The finding worth carrying is about the predicate, not the decisions.** The first D127 predicate reported
ZERO required members on an interface that has two: it merged each property into the next member's chunk,
so the first `=>` made the whole run look defaulted. It was green, plausible, and would have stayed green
over the defect it exists to catch. A red case proves the pattern; only a POSITIVE CONTROL proves the gate
was looking at anything — and this gate's own header already said a predicate nobody checked is a second
unverified claim.

## Part 244 — `IEmbedder` removed: an embedder is a capability, not a front door

✅ closed 2026-09-17. `TASKS.md` Part 100, opened and closed the same day — **D151** recorded the decision
first at the owner's direction, then the change landed.

**The argument is D151's; what the work cost is this.** 12 entries off the frozen surface (the interface,
two extension helpers, four `Add*` overloads, four public constructors), 19 source files, 33 test files,
the bench doubles and the playground. `RoutedEmbedder` became `EmbeddingRouting` — an internal helper
keeping D129's capability filter and failover, which were the substance; only the type wrapping them went.
The four consumers take `IEnumerable<IModelProvider>`, exactly as `ScoringVerificationPolicy` already did.

**Bring-your-own needed nothing new.** `AddEmbeddingProvider(factory)` was already public, so the route
D151 describes existed before the entry was written — the removal took a seam away and added no
replacement.

**Two gate findings, both about gates this session had just built.** The **D129 claim went vacuous the
moment its subject did**: `check-decision-claims` would have reported it green for ever over a rule with
nothing left to break, which is worse than not having it (`docs/GATES.md`). And a broad `IEmbedder` prose
ban measured **102 hits across ten documents, 47 in `docs/DECISIONS.md`** — entries accurate by naming what
they were about — so the registry took the CALL form instead, at 1 hit against 0 false, and the identifier
half stayed on `retiredApiNames` where it actually stops the type returning.

**No test was lost**: 3853 total before and after, and the four failures the change produced were all
assertions about the deleted seam, each repointed at the question it was really asking.

## Part 245 — a five-dimension release review, and the regression it caught in work committed hours earlier

✅ closed 2026-09-17, at the owner's direction: a full code-and-docs review before cutting the major,
run as five parallel reviews — test integrity, public surface, structure/boundaries, maintained docs,
release mechanics.

**The finding that justified the pass: D151 broke bring-your-own embedding**, and `verify` was green over
it because the one test covering the route had both arms rewritten into the same call. A test that cannot
fail independently is the failure mode this repository builds gates against, and here it hid a shipped
defect for the length of a session. Incident in `docs/FIXES.md`.

**What the review changed immediately**, beyond that fix: the orphaned `Lyntai.Embeddings` namespace root
(`TASKS.md` Part 101), `CLAUDE.md`'s namespace map — which listed a Core namespace that no longer exists
and undercounted shared namespaces by two — and `AddEmbeddingProvider`'s shipped XML doc, which pointed at
an `Add…Embedder` convention D132 retired.

**What it found and did NOT change** is `TASKS.md` Part 102, one decidable item each — filed rather than
fixed because each is a decision rather than a defect: the cross-encoder's names (`AddOnnxCrossEncoder` / `OnnxCrossEncoder`, the one backend D137/D138
missed), `HttpDialect` as a closed enum where a DI seam belongs, `AddProvider` versus
`AddEmbeddingProvider` as a silent mis-wiring trap, four surface changes since v3.1.0 that no changelog
entry announces, and a `### Breaking` section carrying nine additive entries.

**The reusable half is REVIEW SHAPE, not embeddings.** Each finding came from asking a different question
of one tree, and none would have surfaced from the others — the regression was invisible to four of the
five, because only the reviewer told to assume a green suite hides weakened tests looked for arms that had
become identical.

## Part 246 — the role word leaves PROVIDER names, and stops there (2026-09-17)

**Closes `TASKS.md` Part 102's REL4 and all of Part 101 (NS1)** — filed as two unrelated items, resolved as
one decision (**D152**). The backlog framed REL4 as a mis-wiring trap and NS1 as a stray namespace; both are
the same thing, which is that **D151** removed the embedder INTERFACE and left the word on the names around
it. Detail and the alternatives that lost are in D152; the migration table is in `CHANGELOG.md`.

**REL4's four listed options were all worse than a fifth nobody had written down.** Each — a distinct
parameter type, an analyzer, a startup warning, louder docs — accepted that a second registration method had
to exist. `AddProvider(factory, declares)` removed the need. **An options list inherited from a review is a
starting point, not a menu.**

**It took THREE readings to land, and only the last one was derived from the code.** The first retired the
word everywhere; the second pulled back to "the vendors put it on the operation"; the third — prompted by a
reader refusing the appeal to convention — read `Produces` and got the rule that held: the NOUN is the kind
(`Vector`), the VERB is the call (`Embed`). Each earlier reading was right about the cases it had looked at
and silently wrong elsewhere, which is why a review caught residue both times.

**The reusable half is in `.claude/knowledge/pitfalls.md`** §Environment / tooling — one trap for the blind
token sweep that rewrote prose and a user-facing error string, one for taking a rename's SCOPE from
convention instead of from your own model.

**A review also caught new public surface with no test**: `AddProvider<T>(declares)` had none, and the test
claiming to cover it called the factory overload twice — the same collapsed-arms defect the file's own
comment records from D151, reintroduced within the change that quoted it.

## Part 247 — the MEDIA call shape is `Media*`, in `Lyntai.Inference` (D154 NS-3b)

✅ done 2026-09-18 — **Outcome:** `Generation{Request,Result,Chunk,Usage,Artifact,Input,InputRoles}` →
`Media{Request,Response,Chunk,Usage,Artifact,Input,InputRoles}`, moved out of `Lyntai.Generation` into
`Lyntai.Inference` beside the text, vector and score shapes. One rewrite per file: 664 occurrences over 66
files, plus five `git mv`s and two splits (`MediaInput`, `MediaUsage` got their own files, matching the
`Text*` layout). `GenerationResult` → `MediaResponse` closes the last name disagreeing with
`dotnet-package-layout.md` §Naming. Both `retiredTerms` and `retiredApiNames` gained an entry — the SURFACE
half also backfills NS-3a, which had only registered the prose half. Detail in `CHANGELOG.md` §Unreleased.

**The prefix check that NS-3a's trap demands was run and came back clean** — all seven types are read only
by the media domain and by the shared seam (`IModelProvider`, `LyntaiDiagnostics`), which is the argument
for moving them rather than against it. What KEPT the word is the finding: the domain machinery
(`GenerationRouter`, `GenerationPipeline`, `IGenerationJobProvider`, the media tools) and the telemetry
names, which are a consumer's subscription string rather than a type.

**Left for the step that decides `Lyntai.Generation`'s residual membership:** the root namespace now holds
exactly two types, `IGenerationJobProvider` and the internal `GenerationJson`. A seam whose whole signature
is `Lyntai.Inference` types sitting alone under a domain root is the same shape D153 used to justify
`IVectorProvider`'s home.

- **NS-3b — the MEDIA call shape: rename AND move in one pass.**

## Part 248 — the text FRONT DOOR is `ITextClient`, and `Lyntai.Llm` is gone (D154 NS-4, carrying NS-2)

✅ done 2026-09-18 — **Outcome:** `ILlmClient`/`LlmClient`/`ILlmRouter`/`LlmRouter` and their factories,
builder, registration and five decorators became `Text*`; `Lyntai.Llm{,.Routing,.Caching,.Budgeting,
.RateLimiting,.Cli}` folded into `Lyntai.Inference{,.Caching,.Budgeting,.RateLimiting,.Cli}`, with Routing
flattened into the root because `RoutingPolicy` and `DeadHostTracker` were already there. 782 occurrences
over 224 files. NS-2 closed inside it, as its own refutation required. Detail in `CHANGELOG.md` §Unreleased.

**The rename has a BOUNDARY, and it is the deliverable a later sweep needs:** `Llm` is retired as a
call-shape and front-door prefix only. It stays live where it means *"asks a language model"* —
`LlmScorerBase`, `LlmPairwiseComparer`, the two `LlmMemory*Policy` types, and `IScorer.IsLlm`, which is also
the `is_llm` COLUMN in both SQL backends and the `"llm"` score group. Renaming the types alone would split
one vocabulary across two words and strand the persisted half.

**Three defects the move surfaced, none of which any gate could see beforehand:** `check-decision-claims`
had a hardcoded `src/Lyntai.Core/Llm` root that would have scanned NOTHING and reported clean — the SECOND
time that function has been broken this way by a directory move, now commented in place; a `<see cref>`
written `Llm.Cli.…` rather than fully qualified, which a `Lyntai.Llm` pattern cannot match; and three sites
naming `LlmRoutingPolicy`, a type that has never existed — `RoutingPolicy` is the SHARED default table, not
the text one, which is why NS-4 left its name alone.

- **NS-4 — the front door: `ILlmClient` → `ITextClient`.**
- **NS-2 — the governance sub-namespaces move WITH the front door, not before it.**

## Part 249 — the media router joins its peer: `GenerationRouter` → `MediaRouter` (D154, the spec's step 5)

✅ done 2026-09-18 — **Outcome:** the media router family moved to `Lyntai.Inference` as `Media*` —
`IMediaRouter`, `MediaRouter`, `IMediaRouterFactory`/`MediaRouterFactory`, `BudgetedMediaRouter`,
`RateLimitedMediaRouter`, `MediaRoutingPolicy`, `MediaSubmission` — plus `IMediaJobProvider`, a provider
seam typed entirely in Inference types. `GenerationOptions` → `MediaOptions` with the four registrations
that configure the router. 378 occurrences over 69 files. `Lyntai.Generation.Routing` is gone: the pipeline
moved to the `Lyntai.Generation` root rather than being left alone in a namespace that no longer had a
router in it. `TextRouter` and `MediaRouter` are now neighbours, which is what D153 refusing to MERGE them
always implied. Detail in `CHANGELOG.md` §Unreleased.

**What kept the word is the test D154 states:** the pipeline, the render job, the artifact sink and the
`generate_*` tools RUN a generation — the act, not the shape of a call. The tools' wire names and the
`lyntai.generation.*` telemetry are untouchable for the separate reason that a consumer subscribes to them
by string.

**One name was deliberately NOT swept and is now an open item:** `AddGenerationProvider`. It would have
become `AddMediaProvider`, which is the exact shape D152 retired `AddEmbeddingProvider` for — a
registration named for what a provider produces. A sweep that renames it answers a question nobody asked;
leaving it visibly odd among five `Media*` siblings is the honest state for an undecided one.

**The partially-qualified `<see cref>` bit for the THIRD time in this restructure**, and the reusable half
is in `.claude/knowledge/pitfalls.md` §Refactoring & namespace moves.

- **NS-6 — the MEDIA router joins its peer: `GenerationRouter` → `MediaRouter`.**

## Part 250 — `Lyntai.Providers` means adapters and nothing else (D154, the last step)

✅ done 2026-09-18 — **Outcome:** `AgentMcpServers`, `CliAgentTerminal`, `CliTempFile` and `WireJson` moved
from the bare `Lyntai.Providers` root to `Lyntai.Providers.Basic`, the package that owns them, so that
family is now one segment per adapter with no root tenants. Eleven files gained an import; **all four types
are internal, so the API baseline did not move** — the one step of this restructure that broke nothing.

**The item's own description was wrong, and checking it is what picked the home.** It called them "shared
CLI helpers", which is **D144**'s stated invariant — *"a file arriving there is making a claim a reviewer
can check: every CLI backend in this package uses it"*. Nobody checked it: `WireJson` is read by
`HttpModelProvider`. So a CLI-named namespace would have been false for one of the four, and the package
that owns them is the only thing true of all four. D144 now carries the supersession at the paragraph that
made the claim, because a reader arrives inside a record from a grep.

**Nothing was added to `retiredTerms` or `retiredApiNames`, deliberately:** no name was retired here.
`Lyntai.Providers` is still live as the family parent and all four types kept their names — only their
namespace changed, and it was never public.

**With this, D154 is fully executed** and its six steps are Parts 246–250 plus the NS-4 follow-up.

- **NS-5 — `Lyntai.Providers` stops meaning three things.**

## Part 251 — ROUTE-1: the generic router gets a factory, so cooldown and admission reach every kind

✅ done 2026-09-18 — **Outcome:** `IProviderRouterFactory` + `ProviderRouterFactory` in `Lyntai.Inference`,
binding the ONE `DeadHostTracker`, the ONE `IProviderAdmission` and the pool's configuration key.
`EmbeddingRouting` and `ScoringVerificationPolicy` route through it; `SemanticMemory`, `SemanticSeedSource`,
`VectorToolSelector`, `GraphMemoryEngine` and `ScoringVerificationPolicy` each gained ONE optional trailing
parameter, wired by `AddLyntai`. Reasoning and the alternatives that lost are **D155**.

**The item's own plan was not what shipped, and the reason is the deliverable.** It said to change four
public constructors to take a router instead of `IEnumerable<IModelProvider>`. That changes each
parameter's TYPE *and* NAME — and a named argument is source-compatible surface, the bill **D47** already
paid once — for 39 call sites, while still leaving cooldown keyed on the backend id. The library already
had the right answer twice (`ITextRouterFactory`, `IMediaRouterFactory`) and `ITextRouterFactory`'s own doc
already stated the rule both broken call sites violated: *"building a router per call is cheap; what must
NOT be rebuilt is the bookkeeping."* **A backlog item is a plan, not a ruling** — this one was written
before the precedent was looked for.

**Additive, so it is the general fix rather than a patch for two kinds.** Passing no factory still routes,
and an application closing `IProviderCall<,>` over its own kind can now inject the same factory — which is
what D153 promised and could not deliver. Pinned by a BEFORE/AFTER pair: a per-call tracker asks the failing
backend all three times, the shared one asks it once.

- **ROUTE-1 — vector and score have the routing MECHANISM but not the WIRING.**

## Part 252 — `AddGenerationProvider` is deleted, not renamed (D156), closing Part 103

✅ done 2026-09-18 — **Outcome:** the method is gone. A media backend is registered through
`AddProvider(factory, declares)` — the one door every backend comes through — and `AddMediaRouting()` wires
the media router, which was the deleted method's other, unnamed half. The five vendor presets call both, so
`AddOpenAiImageProvider` and its siblings are unchanged. ~33 call sites and 14 test builders updated.
Reasoning in **D156**; this closes the `decision-only` item NS-6 deliberately left open, and with it Part 103.

**The owner's framing is what settled it:** generation and media are not KINDS of provider — each backend is
named for its engine. The evidence was in the same file all along: `AddOpenAiImageProvider`,
`AddAutomatic1111Provider`, `AddComfyUiProvider`, `AddFalProvider` and `AddLocalDiffusionProvider` each name
an ENGINE; the one method naming a domain was the one with no engine to name, because there was no provider
in it to name. So `AddMediaProvider` was never the answer — it would have respelt the defect **D152**
removed from `AddEmbeddingProvider`, in the same builder, four decisions later.

**A `decision-only` item earned its keep.** NS-6 could have renamed this in passing and nobody would have
noticed; it was left visibly odd among five renamed siblings instead, and the ruling that followed deleted
it rather than moving it. That is the state's whole purpose — a sweep that renames a name it never examined
has decided something invisibly.

- **What `AddGenerationProvider` should be called, or whether it should exist.**

## Part 253 — REL1, and the two halves of REL2 that survived re-checking

✅ done 2026-09-18 — **Outcome:** the Unreleased changelog now announces the four surface changes it was
silent on, and its thirteen `###` headings are four. REL1's claims were re-verified against the tree before
acting, as Part 102 requires: all four held.

**The sharp one was sharp.** `ProviderProbeResult`'s entry said the LLM domain had duplicated it *"in a
different field order"* and never said the survivor took GENERATION's. Every member but the first is
`string?`, so a positional call written for `(Available, Version, Model, Detail)` still COMPILES against
`(Available, Detail, Version, Model)` and files the version string into `Detail` — a silent data defect on
upgrade. The entry now states the surviving order and says to check every multi-argument construction.
The other three: `MemoryReview.Grade` → `ReviewGrade` (unannounced, and `MemoryReviewWrite` is constructed
by every BYO graph store), `GraphNode`'s trailing `Matched` (the read-side member of a break class already
listed for four write-side types), and the LLM-side `FallbackAction` namespace move, which D140's entry
described only from the generation side.

**REL2's third half was REFUTED and is now a decision, not work.** It claimed nine `### Breaking` entries
are additive; five genuinely are source-compatible to construct — but the same release files
`GraphNodeWrite gains two trailing flags` under Breaking, which is the identical shape. The repository has
never written down what Breaking MEANS here, so re-filing on a review's assertion would ship a migration
path resting on an unstated rule. The rule goes first; `TASKS.md` Part 102 carries the two candidates.

- **REL1 — four surface changes since `v3.1.0` that NO changelog entry announces.**

## Part 254 — REL3 answered by DELETING the fork: one ONNX provider, `Produces` says the kind (D157)

✅ done 2026-09-18 — **Outcome:** `OnnxCrossEncoder`, `AddOnnxCrossEncoder` and `OnnxCrossEncoderOptions`
are gone. `OnnxProvider` is the engine — session, tokenizer, feed, run — and `OnnxProviderOptions.Produces`
says whether a registration embeds or reranks, exactly as `HttpModelOptions.Produces` does one package
over. Which internal dialect serves it is `internal`. Reasoning, and the EF Core model it follows, in
**D157**; the consumer story is in `CHANGELOG.md` §Unreleased.

**REL3 asked the wrong question and the owner caught it.** The item proposed renaming
`OnnxCrossEncoder` → `OnnxCrossEncoderProvider` for suffix consistency; that shipped and was **reverted the
same day**. The objection: generation and media are not KINDS of provider, each backend is named for its
ENGINE — and both ONNX classes ran the IDENTICAL `session.Run(OnnxGraph.Feed(…))`. They were one backend
split by what they produced, which is the shape **D152** retired `AddEmbeddingProvider` for. A rename would
have made the wrong split permanent and tidier.

**Three passes of naming before it settled, and each was refuted by the library itself:** `Pipeline` (a word
this codebase does not use), then `Dialect` (matching `ICliProviderDialect` — but EF exposes no such option,
and neither should this), and finally `Produces` — which `HttpModelOptions` had been doing all along.
**The answer was already in the sibling provider both times.**

**A gated suite hid a defect mid-refactor:** the live cross-encoder tests loaded a reranker export with the
default vector dialect and would have stayed SKIPPED, green, in `verify`. Caught by reading, not by a gate.

- **REL3 — the cross-encoder is the one backend the D137→D138 suffix sweep missed.**

## Part 255 — REL6: the Tier-B how-to errors, each re-verified before it was touched

✅ done 2026-09-19 — **Outcome:** nine documentation defects fixed across `extending-lyntai.md`,
`pitfalls.md`, `memory.md` and `llm-and-router.md`. Every claim was checked against the tree first, as the
item and Part 102 both require; all nine held, and two were sharper than the item said.

**The two that would have cost a reader real time.** `memory.md`'s "turn the judge on" recipe set
`VerificationDepth = 40` while its comment said it HALVED the depth — but that option is absolute and
defaults to `DefaultVerificationDepthFactor` (4) × `DefaultLimit` (10) = **40**, so the recipe written to
avoid a measured −10.5-point outcome changed nothing. It now sets 20 and shows the arithmetic. And the
salience table's `4.0` column is unreachable: `MaxSalience` defaults to 4, but at the shipped
`NoveltyWeight` of 1.5 the ceiling is `1 + 1.5` = **2.5**, which the option's own XML doc already said.

**`extending-lyntai.md` taught three things that do not compile or do nothing:** `BuildCompletionArgs` with
one parameter where the seam takes two; `public bool SupportsToolCalls => true;` on a provider, which is a
`ProviderCapabilities` FIELD and so compiles as a member nothing reads; and a native-provider sketch
omitting `Capabilities`, the one member with no default body. Its "THREE of the thirteen carry a default
body" was self-contradictory — the thirteen ARE the required ones of sixteen.

**The stale-name half was already discharged** by this session's D154 sweeps — except three **slashed**
paths (`Lifecycle/ProviderVerdict.cs`, `Llm/Caching/`, `Llm/Routing/`), invisible to a dotted-namespace
rewrite for the third time in two days. `pitfalls.md` §Refactoring already records that shape.

- **REL6 — the review's Tier-B list: ~30 internal how-to errors, none consumer-facing.**

## Part 256 — the design-closure review: `TASKS.md` Part 102's three rulings, made and landed in one pass

✅ done 2026-09-19 — **Outcome:** a full pre-release review (five parallel reviewers: post-D152–158 public
surface, four-call-shape symmetry, design-doc coherence, REL2 changelog evidence, the five ungated memory
invariants) put the three open Part 102 calls in front of the owner with costs attached, and all three were
ruled and implemented the same day: **D159** ("dialect" is not public vocabulary; the seam renames, the
`MediaBackendBuilderExtensions` rename, the `McpTransport` clearance and the refused prose gate),
**D160** (`OllamaProvider` + `HttpDialect` deleted; detection moved to composition; internal
`HttpChatEngine`/`IHttpChatWire` hold the shared invariants once), **D161** (the Breaking action rule,
written into the changelog header; nine entries re-filed, three double-filings resolved, action sentences
added), **D162** (shape-neutral `ProviderUsage`; governance slots on the vector/score shapes; the
`TextResponse`-symmetry and duplicate-id-guard refusals). The review's additive findings landed with it:
`ConfigureRouting` now reaches factory-built routers, cooldown keys are namespaced per closed shape
(`vector::`/`score::`), `AddHttpProvider`'s declaration is derived from the built provider, and the design
doc took five dated amendments (§5.1, §5.5, §5.6, §6, §10 — it is exempt from every prose gate, which is
now a filed decision item). Detail: `CHANGELOG.md` Unreleased; incidents: `docs/FIXES.md` 2026-09-19.

- DIALECT-1 — does this library HAVE a "dialect" concept, or only providers? → ruled: providers only (D159)
- REL5 — `HttpDialect` is a closed enum plus an if-chain where a DI seam belongs → provider per wire (D160)
- REL2 — what counts as BREAKING here has never been written down → the action rule (D161)

## Part 258 — vector/score governance WIRING: the one wallet reaches every attributable kind (D163)

✅ done 2026-09-19 — **Outcome:** closes `TASKS.md` Part 257's startable half, opened by Part 256 the same
day. Factory-built `ProviderRouter<,>` routers now budget, rate-limit and record spend for any request they
can attribute (`IConsumerTagged` + the defaulted `IProviderOutcome.Usage`), activated only by the host's
existing `AddUsageBudget()`/`AddRateLimit()` opt-ins; token caps bind embeds/reranks while renders stay
cost-only, enforced by the one shared `BudgetGate` all three doors now use; the library stamps its own
traffic (`"memory"`, `"agent"`); `ResolveTimeout(int?, string?)` gives the transports the text shape's
consumer-tier ladder; and the score kind's compose-it-yourself recipe is in
`.claude/knowledge/llm-and-router.md` §Routing recipes. Reasoning and refusal boundaries: **D163**;
release-facing detail: `CHANGELOG.md` Unreleased.

- Vector/score governance WIRING (the slots froze as D162; this is the behavior)

## Part 259 — the design record's gate exemption, ruled and narrowed to its seeds (D164)

✅ done 2026-09-19 — **Outcome:** closes `TASKS.md` Part 257's decision-only half, and with it the whole
Part (both halves closed the day they were filed). The owner ruled for region-scoped scanning over a
release-checklist read, a document restructure, and accepting the hole; `LIVE_REGIONS` + `liveLineMask`
now serve all three prose gates from `check-docs.mjs`, the design record's eleven inline amendments are
gated (seven announcing lines took `drift-ok`/`link-ok`, the standard pattern), its seven period
blockquotes stay exempt on the 49-hits-in-157-lines measurement, and the reading note states the two-tier
convention so the split is writable, not just inferable. Reasoning and alternatives: **D164**. Guard tests
862/862 (two added for the mask).

- The design contract is exempt from every prose gate — decide the exemption's scope

## Part 260 — CLI12: the codex tool-step mapping, measured and CONFIRMED (2026-09-19)

✅ done 2026-09-19 — **Outcome:** closes `TASKS.md` Part 41 (its only open item; the whole Part retires).
codex-cli 0.155.1, captured on the authenticated ChatGPT path (shell + file edit) and the `--oss` local
path via llama-server (MCP + web search), CONFIRMED every inference in `CodexAgentReader` — nothing needed
correcting, which is the deliverable. The measurement detail and what it settled live in `docs/DECISIONS.md`
D35 (amended); in short: the shell item is `command_execution`, `item.started` fires for every tool item,
and failure is the top-level `status`/`exit_code` the reader already read. Docs flipped INFERRED→MEASURED
(reader docblock, README bullet, D35); the stub gained a `TOOL_TURN` scenario; two tests added.

- **Ratio, per the measurement-task rule:** ~8 capture runs (the actionable finding landed on the first
  authenticated tool run; the rest completed the shape set) against ~15 lines of stub and 2 tests. No `src/`
  behaviour changed — every inference was correct, so this is a confirmation, not a repair.
- CLI12 — measure codex's tool-step items and confirm (or correct) the inferred mapping → confirmed (D35).
  Part 41 also carried the CLI15 pointer (closed 2026-08-05 as archive Part 45); it retires here.
