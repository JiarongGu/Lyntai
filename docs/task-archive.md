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
attributable to the ratio rather than to |A| growing. Tables in `docs/memory.md` §5; **D94**'s "honest limit"
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
scope in `docs/memory.md` §5; what is left open is `TASKS.md` Part 109.

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

**Also landed in the same pass:** a literature comparison against the 2026 field (`docs/memory.md` §5) —
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

**Measured on LoCoMo** (`docs/memory.md` §5), evidence-hit@20: defaults **11.0% → 31.0%**, `SemanticSeedK`
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
`--n`/`--seed` sampling built for it went unused. Tables in `docs/memory.md` §5.

**The knowledge-update win survives, at +40.0 against the oracle's +49.8.** What moved is suppression rather
than retrieval: `current@k` fell 2.9 points for this engine and 2.9 for cosine — identically — while
`stale@k` rose 8.6. The distractors cost it the ability to *bury* the superseded fact, not to find the
current one.

**The temporal result REVERSED, and that is the finding.** −4.6 on the oracle becomes **+3.8** on the
haystack. Distractors cost cosine 20.5 points of all-evidence recall and cost this engine 12.1: where there
is finally something to suppress, suppressing it stops being a cost even in the class built to penalise it.
Read as "the cost is gone" rather than "this engine wins temporal" — +3.8 on 132 questions is five questions.
What carries it is that all three columns agree and the other two move further (any-evidence +7.6, per-turn
+4.3).

**So the oracle is not a cheap unbiased proxy — it is biased per class, and the sign is not predictable.**
The caveat that filed this item guessed the bias flattered both classes. It flattered knowledge-update by 9.8
points of arm gap and **penalised** temporal by 8.4, enough to invert the sign. The mechanism is one number
nobody had looked at: at `k = 10` over ~25 turns the oracle returns **40% of the store**, so it barely tests
retrieval at all, where the haystack's ~490 turns make the same `k` a 2% slice.

**A latent harness defect the haystack exposed, and it would have been silent.** The loader took the current
value from the latest-DATED session. Every oracle session is an evidence session, so that was right by
accident; in the haystack the last-dated session is a distractor nearly every time, so the rule found no
current turn and would have dropped the whole class — an empty run rather than a wrong number, which is the
cheap direction only because nothing else read it. It now takes the latest dated session that *carries* a
flagged turn.

**Two controls, because either half could have been the harness.** Re-running both oracle classes under the
new loader reproduces **byte-identically on all fourteen cells**, so the fix moved no published number. And
the variants provably ask the same questions: each preamble prints a fingerprint of its sampled ids, and they
match across variants (`D860F77A3D9E` knowledge-update, `773FB41E0E5A` temporal), which is what lets an
oracle row be read against a haystack row line by line.

**Two documents were stale before this pass and are fixed in it**, both in `docs/memory.md`: §5's preamble
said "everything below is on this repository's own deterministic corpus" while three sections below it are
the field's data, and the literature survey still said running a shared suite was "a piece of work nobody
here has done" after two of them had run. The surviving half of that claim — that a model-free retrieval
metric is not comparable to published QA accuracy — is now stated on its own.

- **Extend `memory-longmemeval` past the knowledge-update class.**

## Part 113 — the twenty slots are spent on the right candidates; the gap is the DESIGN (2026-08-29)

✅ done 2026-08-29 — closed by the second LoCoMo ladder, which shipped in the same commit that reframed the
item and was then left open in `TASKS.md` for a day. Tables in `docs/memory.md` §5. Every arm holds
`RetrievabilityWeight` at its shipped default, so nothing here measures the engine with forgetting switched
off:

| arm | evidence-hit@20 |
|---|---|
| `lyntai` | 31.0% |
| `+sem` (`SemanticSeedK = 20`) | **36.0%** |
| `+sem+hop0` (`HopWeight = 0`) | 11.5% |
| `+sem80` (`SemanticSeedK = 80`) | 30.0% |
| `+sem80+hop0` | 17.5% |
| `vector` | 80.5% |

**Both misallocation hypotheses are refuted, in the opposite direction to the guess.** Graph traversal is
CARRYING the arm rather than stealing slots — `HopWeight = 0` costs 24.5 points. And more semantic seeds make
it WORSE (36.0 → 30.0), which is **D82** behaving as documented: RRF ranks by competition, so widening one
signal re-ranks every candidate within it.

**The pre-committed fallback is refuted too, and by construction rather than by another run.** The item said a
plateau would mean the evidence never enters the candidate pool. It does: `+sem80` seeds the top-80 by
cosine, which CONTAINS cosine's top-20 by construction, and that top-20 holds the evidence 80.5% of the time.
So the pool holds it at least 80.5% of the time while the arm returns it 30.0% — about fifty points lost
ranking candidates that were present. Worth naming precisely: pool membership was settled by a containment
argument over the same embedder and index, not by instrumenting the pool.

**So the boundary is located, and that is the deliverable.** D97 was the defect — philosophy-independent,
worth about twenty points, and fixed. What remains is the DESIGN: the signal demoting those candidates is
retrievability (old, mentioned once, never reinforced), every knob that closes the gap turns forgetting down,
and the two that leave forgetting alone both made things worse. LoCoMo's role here is a **differential
instrument, not a scoreboard**.

- **Spend the twenty slots better, WITHOUT turning off forgetting.**

## Part 114 — the decay model got BETTER under distractors and the fusion threw it away (2026-08-29)

✅ done 2026-08-29 — chasing the one row of Part 112's table that did not fit: `stale@k` ROSE (54.3 → 62.9)
while twenty times more candidates competed for the same ten slots. `memory-longmemeval --ranks` installs a
probe `IMemoryRankingPolicy` that observes the candidate pool and delegates the real ranking untouched.
**No library change** — `MemoryCandidate` already exposes `Retrievability`, `Hop` and the whole pool, which
is the seam doing its job. Tables in `docs/memory.md` §5.

**The answer inverts the question.** Under distractors the decay model separates the pair **3.6× better** by
value (0.0347 → 0.1235) and **2.6× better** by rank (10 → 26 positions) — and the score separation RRF
actually sums fell **29%** (0.00179 → 0.00127). `1/(K + rank)` is convex, so the same gap is worth far less
at ranks 74/102 than at 10/20, and distractors written after the current fact push both there. **The loss is
in the fusion, not the forgetting**, which is the opposite of what a decay-model regression would look like.

**`K` selects a REGIME, and the intuitive fix is backwards.** A lower K — a steeper curve — makes suppression
monotonically WORSE in both variants, because at low K being top-few on ONE signal beats being mediocre on
the rest, and the stale fact is relevance rank 4. At high K the order tends to the SUM of ranks (Borda), which
rewards being good on all of them. K = 120 costs nothing measurable on `current@k` in either variant and cuts
`stale@k` ~12 points.

**The haystack is what BOUNDS the lever, and the oracle would have hidden it.** Past 120 the variants
disagree: the oracle saturates harmlessly at 32% through K = 1000, while the haystack pays **−16.7 points of
`current@k` at K = 300**. Read on the cheap variant alone, K = 1000 looks free. Part 112's finding, recurring
on a second question.

**Two controls, and the first caught a defect that would have shipped a wrong table.** The offline replica
must reproduce the SHIPPED policy's own top-10 — it agrees 25/25 (oracle) and 24/24 (haystack). It did not at
first: `MemoryRankingContract.Finish` breaks score ties by DESCENDING id, so the newer entry wins, and a
replica breaking them ascending moved the shipped row by 4 points while looking entirely plausible. Second,
the ladder's K = 60 row reproduces the arm's own numbers on the same sample exactly (92.0% / 44.0%), which is
what makes it comparable to the published table.

**Left open as `TASKS.md` Part 109**: `K` is global, every LoCoMo figure was measured at 60, and one class of
one benchmark is not a mandate to move a published constant.

- *(No backlog entry — this came from a measurement, not a plan.)*

## Part 115 — the QA half, the shot curve, and a defect where forgetting had no vote (2026-08-29)

✅ done 2026-08-29 — the QA half of `TASKS.md` Part 109 ran, and it grew a second half nobody had asked for
because the first one measured the wrong mode. Tables in `docs/memory.md` §5.

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

✅ done 2026-08-29 — `TASKS.md` Part 116's write-back item, opened by **D99**'s own closing note. The touch,
the co-activation edges and the review-log rows were three store calls, and on a relational store each
opened its own connection: `IMemoryGraphStore.WriteBackAsync` takes all three as one, with a default body
running the existing members so a BYO store loses nothing. `docs/DECISIONS.md` **D101**.

**Reported as a COUNT, which is the point.** Connection opens went **3 → 1**, measured by a counting
`IDbConnectionFactory` decorator wrapped around the real one — the "before" is not an estimate, it is the
same test failing at exactly 3 before the override existed. The position-totals read went 2 → 1 as well,
but nothing observes that, so it is recorded as a code fact rather than a measured one; conflating the two
is the provenance mistake `pitfalls.md` files under Kind 1. No millisecond is quoted at all: that same
document records this repository publishing a 19% "improvement" from `memory-scale` that was noise, on this
very write-back.

**The ordering turned out to be the real finding, and an existing test is what proved it.** The engine
carried a second `try/catch` around the review-log write, and
`A_broken_review_log_costs_neither_the_hits_the_learning_nor_co_activation` documents — from its own live
mutation check — exactly why: the log sat BETWEEN the touch and the co-activation loop, so a log failure
skipped the edges. Moving the log LAST makes that isolation structural instead of conditional, and the
catch is gone. That test passed unchanged, which is what a positive control is for.

**One behaviour change, deliberate.** The surviving warning said a failed write-back returned hits "without
learning". That was false whenever a later part failed — the earlier parts had already committed — and is
now "partly or wholly unrecorded". Nothing asserted on either string.

**Two contract facts, so all three backends are held to it**, discovered by reflection rather than
registered: the combined path writes what the three separate calls would, and an empty part is skipped
rather than written as a no-op — the second because the engine turns each of its own switches off by
handing an EMPTY part, and a store that stamped an age on an empty touch list would reset an age the caller
asked to hold still.

**What it does NOT do:** no transaction. "One unit of work" here means one connection and one totals
snapshot, exactly as `LinkManyAsync` already meant it — the parts still commit independently, which is
precisely what the review log going last relies on.

- **Collapse the recall write-back into ONE store call.**

## Part 118 — LoCoMo's questions shared a store, and it was worth 20-25 points (2026-08-29)

✅ done 2026-08-29 — `TASKS.md` Part 116's contamination item. LoCoMo ran every question of a conversation
against one store, and this engine WRITES on every read: a recall reinforces what it returned and
`ExpandAsync` reinforces what it walks, so question N read a graph questions 1..N−1 had already dug through.
Each question now runs against a private byte-copy of the ingested store — `SweepDb.Clone()`, ingest once
into a template nothing reads. **No library change**; `MemoryLongMemEvalBench` already built one store per
question, which is how this was recognisable as a defect rather than as a property of the data.

**The positive control is the finding, not the fix.** Two runs differing ONLY in `--seeds` — how much a
LATER shot expands — must leave `shot-1` untouched, because shot 1 is the same query against the same corpus
either way. Pre-fix it read **65.4% at `--seeds 3` and 53.8% at `--seeds 20`**; post-fix, 65.4% both times.
The old code had to fail that check or it would only be evidence the number was stable, so the fix was
stashed and the pair re-run against it.

**Every LoCoMo figure moved 20-25 points, and `vector` did not move at all.** The retrieval ladder went
`lyntai` 31.0 → **54.5**, `+sem` 36.0 → 57.5, `+sem+hop0` 11.5 → 31.5, `+sem80` 30.0 → 55.0,
`+sem80+hop0` 17.5 → 41.0 — while `vector`, which never touches the graph store, was byte-identical at
80.5% across a 22-minute re-run. An arm that structurally could not gain did not gain, which is what makes
the other five readable as isolation rather than drift. The gap to cosine is **−26.0**, not −49.5.

**It cost a published claim, and that is the honest headline.** **D100** argued the useful shot count is a
property of the question, citing LoCoMo shot 2 at +6.0 against shot 3's +0.5. Isolated, the curve is
**+1.5 and +1.0** on a shot 1 that was 24.5 points too low: a shared store DEPRESSED shot 1, and later
shots recovered ground that was never lost. *"Search wants two shots"* is withdrawn; D100 stands on its
other leg (a one-shot metric cannot see a mode that withholds content until asked), amended in place.

**Two cross-checks that the re-measurement is sound.** `shot-1` reads 54.5% and the retrieval ladder's
`lyntai` arm reads 54.5% from a wholly separate run — they are the same operation. And the clone control
counts rows in the copy per conversation (`419 of 419`), because a lossy clone presents as a recall-quality
regression rather than as a broken harness. The WAL is the trap there: a byte copy taken while the
write-ahead log still holds committed rows is a silently partial database, so `Clone()` checkpoints first
and copies a surviving `-wal` too. `.claude/knowledge/pitfalls.md` §Testing carries the general form.

**What is NOT re-measured, stated rather than implied.** The D97 before/after tables in `docs/memory.md` and
`DECISIONS.md` D97 need a `RetrievabilityWeight` ladder the shipped harness no longer has; both columns
share the contaminated regime, so their DELTA stands and both now say so. The QA half was not re-run at all
— it needs a reader, and widening it is `TASKS.md` Part 109.

- **Fix LoCoMo's cross-question contamination before widening any LoCoMo number.**

## Part 119 — the shot curve, extended: the class where expanding pays, and the one where it never did (2026-08-29)

✅ done 2026-08-29 — two thirds of `TASKS.md` Part 116's shot-curve item. **Knowledge-update went from a
25-question sample to all 70**, and **the temporal class got the first shot curve it has ever had**, on both
variants. Tables in `docs/memory.md` §5. The remaining third — LongMemEval's four other classes — is
re-scoped rather than closed: each needs a metric matching what that class ASKS, which is design work and
not a run.

**The full knowledge-update sample moved the LEVEL down and the RATIO up.** Every `clean` figure fell 6–9
points against the 25-question sample, so that sample was optimistic and any absolute from it was too high;
but cosine fell further (16.0 → 10.0), so the multiple **D100** actually argues went **2.5× → 3.1×** —
31.4% clean on 1,169 characters against cosine's 10.0% on 10,387. The shape is unchanged.

**The temporal class is the only workload measured where walking clearly pays: shot 2 is worth +4.5 points**
of all-evidence recall, against +1.5 on LoCoMo and −2.8 on knowledge-update. The mechanism is the one the
class is built on — a temporal question usually needs EVERY flagged turn, so the failure mode of a small
first load is holding one of two. **The honest counterweight is in the table beside it**: size-matched
cosine wins that column outright (65.2% against 53.0%) at 2.7× the characters, because all-evidence recall
is an ARCHIVE metric and rewards keeping everything — LoCoMo's axis reached from a second direction.

**Shot 3 is worth nothing, and that is now three classes in a row.** Identical to shot 2 on every column
while adding 2,731 characters. *"Expand until the budget runs out"* is not the lesson; *"expand once"* is.
**And the ORACLE overstates the multi-shot gain by 2.7×** (+12.2 against +4.5) — Part 112's finding that the
cheap variant is biased in unpredictable directions, recurring on a third question.

**A one-question disagreement was chased rather than published, and became a measurement.** The new
`shot-1` read 48.5% where the existing `lyntai` arm read 47.7% on the same sample. The code diff said
equivalent (the one difference, an explicit options object, is inert under `options ?? new
GraphMemoryOptions()`), the ORACLE agreed to the decimal, and repeating the identical haystack run moved
every graph arm exactly 0.8 points onto the other arm's own 47.7% — while **both vector arms stayed
byte-identical throughout**. So the haystack carries a reproducibility floor of ONE QUESTION on the graph
arms, the deltas are stable across runs (+4.5 / +4.6), and the levels are good to about a point.
`.claude/knowledge/pitfalls.md` §Testing carries the general form.

**One refactor, proven neutral before it was trusted.** Both curves score the SAME walk by opposite metrics,
so `WalkAsync` was extracted rather than written a third time — Part 116 cites that duplication as the tell
for the library having no n-shot surface. Equivalence was shown the way the LoCoMo control was: stash, run,
restore, re-run — byte-identical on every quality and size column across all five arms. Reading the
extracted code back also caught a compile error before any build, `ShotBudget`/`ExpandSeeds` having been
method-locals invisible to the shared walk.

## Part 120 — the n-shot walk is a SURFACE now, and both harnesses drive it (2026-08-30)

✅ done 2026-08-30 — `TASKS.md` Part 116's first item, the one its banner called the biggest thing **D100**
opened. `MemoryWalk.WalkAsync` is a static extension on `IMemoryEngine` yielding
`IAsyncEnumerable<MemoryWalkStep>`; **nothing was added to `IMemoryEngine`, `IExpandableMemory`,
`MemoryQuery` or `MemoryItem`**. The reasoning, the three rejected surfaces and what reversing costs are
**D102**.

**The verification was the point, and it is stronger than "the tests pass".** Both harnesses were moved onto
the surface and every published table re-run. **Every cell reproduced exactly** — LongMemEval haystack
knowledge-update (31.4 / 28.6 / 28.6 `clean`, 1,169 / 5,236 / 8,286 chars), LoCoMo `--shots` (54.5 / 56.0 /
57.0, and every `hit / 1k chars` ratio), and all six arms of the LoCoMo retrieval ladder (54.5 / 57.5 / 31.5
/ 55.0 / 41.0 / 80.5) including every per-category fraction. The oracle class was additionally run as a true
before/after by stashing only the bench file — identical down to the same 1,662 embedder calls and 1,828
cache hits — and the after arm was run twice, so the instrument is known deterministic here rather than
merely agreeing once.

**Two rules in the merge are corrections rather than ports.** Identity is the whole `MemoryRef`, where both
harnesses keyed on `Reference.Id` alone and got away with it only by running one engine — on a composite,
two members may each own id `"1"`. And the headline→content upgrade is SEMANTIC (`Content` arrived where
none was held) rather than the harnesses' `body.Length >` proxy, which differs wherever an authored headline
outruns its content. Both are pinned by facts that were **mutation-checked**: keying on the id alone, and
restoring the length proxy, each fail exactly one fact and nothing else.

**Three defects found that the plan did not predict, and the middle one is the instructive one.**
<br>**(1) The planned termination mutation check was untestable as written.** Deleting the "a step that
moved nothing ends the walk" guard failed NO test — the `seeds.Count == 0` guard carries termination for the
DEFAULT selector, so the rule that actually makes the sequence finite had no coverage at all. A caller-
supplied selector can hand back seeds forever, which is now `A_selector_that_never_returns_empty_still_
terminates`, bounded inside the TEST so a regression fails instead of hanging.
<br>**(2) The LoCoMo refactor changed a DENOMINATOR, not a retrieval.** The old loop ran shots 2 and 3
unconditionally and re-snapshotted an unchanged context; a walk that ends early would have silently dropped
those rows, moving every rate for a harness reason wearing a result's clothes. Filled explicitly. This is
the one that would have published a wrong number.
<br>**(3) `check-samples` caught its own baseline going stale** (`CLAUDE.md` 78 → 79 doc samples), and the
first README annotation was redundant: `check-samples.mjs` already pre-declares `IMemoryEngine engine`.

**What is deliberately NOT done.** The merge accumulator stays internal (D102 says what would change that).
The public NAMES were provisional when this landed and were settled the same day — Part 121.

**The working records** are `local/superpowers/specs/2026-08-30-memory-walk-design.md` and
`local/superpowers/plans/2026-08-30-memory-walk.md` — untracked by design (**D43**), so they exist on one
machine and in no history. Everything in them that had to outlive this version is already in D102, design
§5.7, `pitfalls.md` and these two Parts; they get their `docs/superpowers/INDEX.md` row when the work
SHIPS, which is that file's own rule.

## Part 121 — the walk's names, passed before the surface shipped (2026-08-30)

✅ done 2026-08-30 — `TASKS.md` Part 116's naming item, filed the previous day and **unblocked by Part 120
itself**: its blocker was the TREE (`MemoryWalk` did not exist, so there were no names to pass over), which
is the kind of blocker a commit discharges. Five renames, against
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

✅ done 2026-08-30 — `TASKS.md` Part 109's `K` sweep. Built `node devtools/dev.mjs memory-locomo --ranks`,
the LoCoMo-side ladder that item asked for, and ran it beside a re-run of the LongMemEval haystack ladder at
full sample. Tables in `docs/memory.md` §5. **No default moved, and the item's own premise is what the
measurement overturned.**

**The premise was that K = 120 is free, and BOTH halves of it failed.** LoCoMo is a search workload — it
wants old material found rather than suppressed — and 60 → 120 costs it **4.5 points** of evidence-hit,
monotonically, with no threshold effect. Separately, re-running the knowledge-update ladder on all 70
questions rather than the 25 it was first measured on shows 60 → 120 also costing **6.0 points** of
`current@k`, where the small sample reported 0.0. So *"free"* was one workload wide AND one sample thin, and
`docs/memory.md`'s claim to that effect is amended in place rather than deleted, because the amendment is
the finding.

**What replaces it: 60 is a priced compromise.** Every step in either direction helps one metric and hurts
another — up buys suppression and pays in recall on both benchmarks, down buys recall and gives the
suppression back. There is no K that is free on both, which is exactly what "K selects a REGIME" predicts
and nobody had measured on more than one regime.

**The sharper result is that `K` is not where the LoCoMo gap is.** 32 of 200 questions had no evidence in
the candidate pool at all, so the pool's ceiling is 84.0% against a shipped 54.5% — the fusion loses **29.5
points of material it already held**, and the best K recovers **4.5** of them. Part 109's residual gap is
therefore not a fusion constant; it is seeding for a sixth of it and ranking SHAPE for the rest.

**One replica, not two, and proven neutral before it was trusted.** The RRF scoring the two ladders share is
now `bench/Lyntai.Benchmarks/RankLadder.cs` — the same argument Part 119 made for `WalkAsync`, and a
sharper one here, because a second copy could get the descending-id tiebreak or the competition-rank
definition wrong in only one ladder and still look right. Equivalence was shown the way every refactor in
this sequence has been: stash, run, restore, re-run — the LongMemEval ladder reproduced **byte-identically
on all eight rows**, both controls included.

**Controls.** LoCoMo's replica reproduces the shipped policy's top-20 on **200/200** recalls and its K = 60
row reads the `lyntai` arm's own published 54.5% to the decimal; the haystack ladder's control is 66/66.
The first attempt at the harness died on a `KeyNotFoundException` rather than scoring nothing quietly — an
early `continue` skipped a dictionary a later scoring path read — which is the loud direction.

## Part 123 — the expansion floor, swept: a better deal than its own doc said (2026-08-30)

✅ done 2026-08-30 — `TASKS.md` Part 116's `ExpansionRetrievabilityFloor` sweep. `--expand-floor` was added
to the LoCoMo harness so both workloads can be priced, and the knowledge-update arm re-run at 70 questions.
Tables in `docs/memory.md` §5. **The default did not move; the DOCUMENTATION did, and that is the finding.**

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

✅ done 2026-08-30 — `TASKS.md` Part 33 / GEN7's survey item, the one the banner had carried as a startable
critical path. It asked **mesh vs turntable stills, since only the latter chains into today's video
backends**. **Both options turned out not to exist as posed**, which is the second time in two days a
backlog item's own premise is what the work overturned (Part 122 was the first). Working record:
`local/superpowers/specs/2026-08-30-3d-backend-survey.md`.

**A DESK survey, and the caveat is load-bearing.** Every vendor fact was read from a published API page,
never called — the tier GEN-VERIFY exists to distrust. The item was scoped that way deliberately ("no key,
no install, no download"), so the SHAPES transfer and no individual field name is confirmed. Nothing here
licenses deleting an unverified marker.

**The answer.** The dominant 3D family returns a mesh and nothing renderable (Hunyuan3D v2: `model_mesh`
alone; Hyper3D Rodin: `model_mesh.url` plus `textures[]`). A minority adds a **single preview `thumbnail`**
(Meshy v6) — one fixed view, not a turntable. Turntable output belongs to a **different model family**
(SV3D-class orbital synthesis), which is **image→views** and therefore does not occupy a 3D stage's place in
a chain. So `3d → image → video` corresponds to no buildable chain, and the chain that IS buildable
(`image → orbital views → video`) contains no 3D stage. **The 3d→image edge is not a generation at all — it
is a RASTERIZATION**, which no vendor on this platform performs, and a rasterizer belongs to an application
with a renderer rather than to a library whose core promise is a small dependency footprint.

**Two traps a runner would have walked into, and neither is visible from the contract.** A mesh backend's
only `image/*` artifacts are **UV texture atlases** (Rodin's `textures[]`, `content_type: image/png`) — a
flattened skin, not a view — so "pick the first `image/*` artifact" chains something that renders fine and
is completely wrong, the same silent-plausible class `FalQueueProvider` carries a comment about having
shipped once. And **MediaType cannot be branched on**: Hunyuan3D reports its GLB as
`application/octet-stream`, so the type is opaque exactly where the decision matters. A stage that cannot
identify a chainable artifact must REFUSE rather than fall back.

**Two defects found on the OUTPUT stage, recorded here and FIXED the same day as Part 125.**

- **`ComfyUiProvider` declares `SupportsInputs = true` and never reads `request.Inputs`** — the identifier
  occurs once in the file, in the declaration. `GenerationCapabilities.Supports` uses the flag as an
  ADMISSION filter (`request.Inputs.Count > 0 && !SupportsInputs`), so it is a promise to the router that
  the backend consumes inputs: the router will select ComfyUI for an image→video request carrying a first
  frame, and the frame is dropped in silence. fal fixed this exact bug on its own side and left the
  reasoning in a comment. The flag buys ComfyUI nothing even charitably — a caller who bakes the image into
  the workflow graph sends no `Inputs`, and `Supports` only filters when there are some.
- **`GenerationKinds.Model3d`'s XML doc is false and it SHIPS** — *"Chains into `Image` and then `Video`"*
  is untrue for the entire mesh family. Same tier as `MaxSalience` keeping *"Unmeasured"* after the ladder
  that measured it (Part 123, the same day).

**What it opened.** **GEN7a**, the pipeline runner at `image → video` — GEN7's whole design minus the stage
that has no backend, startable with no key or download. The survey replaced itself in the startable set
rather than shrinking it, and GEN7's own blocker was restated: what stays blocked is the 3D STAGE, on a
rasterizer, not the runner.

## Part 125 — the capability nothing implemented, and the contract fact that now catches it (2026-08-30)

✅ done 2026-08-30 — the two output-stage defects Part 124 found, both fixed. `docs/FIXES.md` carries the
incident and `.claude/knowledge/pitfalls.md` §Second doors the reusable rule.

**`ComfyUiProvider` declared `SupportsInputs = true` and never read `request.Inputs`** — one occurrence of
the identifier in the file, the declaration. The flag is an ADMISSION filter
(`GenerationCapabilities.Supports`), so declaring it does not describe the backend, it makes the router
SELECT it for input-carrying work it cannot do: the input was dropped, the graph ran as authored, and the
render came back plausible and billed. Wrong since `a0efbe6` (2026-08-04), the commit that added the
backend — never a regression. **It survived 26 days after the identical bug was found and fixed in
`FalQueueProvider`**, whose comment records the same billed-and-plausible outcome, because that cure was
written as one provider's comment rather than as a shared fact.

**The fix is the honest capability plus a refusal, and the flag bought nothing even charitably.**
`SupportsInputs` is no longer declared, so `Supports` filters the backend out and the router goes
elsewhere; `SubmitCoreAsync` refuses an input that arrives anyway, which is reachable by a caller holding
the provider directly, and posts nothing so bills nothing. A caller who references the image from the graph
— every correct ComfyUI caller — sends no `Inputs` at all, and the filter only applies when there are some,
so `true` was unnecessary for the path that works and harmful on the path that did not.

**The guard went into the CONTRACT rather than the provider, which is the transferable part.** Four
backends honoured this and one did not: precisely the shape a shared contract fact catches and a
per-backend test never will. `GenerationProviderContract.A_handed_input_is_consumed_or_refused` hands every
HTTP backend an input carrying a marker and asserts it was either consumed or refused before the call,
never quietly discarded — sending nothing is honest, sending a request without the input is the defect.
**Written against the unfixed tree it failed for ComfyUI alone and passed for the other three**, which is
what makes it a fact rather than a restatement of the bug. It sits beside
`Its_declared_deliveries_are_backed_by_the_interfaces_it_implements`, which was already doing this for
delivery modes — and whose existence suggested the inputs axis to nobody for 26 days.

**`GenerationKinds.Model3d`'s XML doc claimed a chain that does not exist** and shipped. Corrected to state
what Part 124 established, including the texture-atlas trap. No code depended on it.

**One correction worth recording, because it reached two committed records before it was caught.** The
admission filter is `GenerationCapabilities.Supports`; this session wrote it as `CanServe` throughout —
inferred from a grep that showed the method BODY without its signature — and that name reached Part 124 and
`TASKS.md` before the compiler rejected it in a test. `check-links` cannot see it: a C# member named in
prose is not a path, a Part or a `§`. **A member name quoted in prose has no gate at all**, so read the
declaration rather than the body before citing one.

## Part 126 — GEN7a: the pipeline runner, at the half of GEN7 that is buildable (2026-08-30)

✅ done 2026-08-30 — `router.RunPipelineAsync(stages)` in `Lyntai.Generation.Routing`
(`src/Lyntai.Core/Generation/Routing/GenerationPipeline.cs`): ordered stages, each feeding the next through
`GenerationArtifact.ToInput(role)`, per-stage candidates, per-stage failure semantics. Three public types
(`GenerationStage`, `GenerationPipelineResult`, `GenerationPipeline`), 18 facts, nothing added to
`IGenerationRouter`. Opened by Part 124, which replaced GEN7 in the startable set with this.

**It composes the router rather than the providers, and that is the load-bearing choice.** Every stage is an
ordinary `GenerateAsync`, so spend caps, throttling and dead-host cooldown govern a pipeline exactly as they
govern one render — `pitfalls.md` §Second doors is the reason it is a FACT and not a sentence:
`A_spend_cap_binds_BETWEEN_stages_because_the_runner_drives_the_ROUTER_not_a_backend` drives a real
`BudgetedGenerationRouter` and fails the day anyone moves the runner below the seam. Mutation-checked by
removing the decorator; it failed, and alone.

**Chaining is one-or-refuse, and the refusal is the design.** Exactly one artifact chains automatically;
zero or several REFUSE with `Unsupported` **without calling a backend**, naming the count and the fix.
Part 124 ruled out both cleverer rules — a media type cannot be branched on (a GLB reports
`application/octet-stream`) and "the first `image/*`" picks a UV texture atlas — and the failure costs are
asymmetric: a refusal is a message, a wrong pick is a plausible billed render of the wrong thing, which is
the Part 125 shape. `GenerationStage.SelectInput` is where a caller states their own rule, and a delegate
that throws PROPAGATES rather than being reported as a capability gap. `InputRole` is the caller's too — the
same PNG is a first frame to one backend and an init image to another.

**Nothing is ever re-run**, so a later stage's failure keeps what earlier stages paid for:
`GenerationPipelineResult.Stages` holds every stage that ran including the failed one, and the fact asserts
a CALL COUNT rather than describing the intent. Chained inputs are APPENDED, so a caller's own style
reference survives, and `with` leaves their `GenerationRequest` unmutated.

**Two settings throw rather than being ignored** — `InputRole`/`SelectInput` on the first stage, which
chains from nothing, and an empty stage list. A setting nothing reads is the Part 125 defect in miniature.

**It also found the README asserting a chain that does not exist.** *"`artifact.ToInput(role)` feeds one
stage's output into the next (3d → image → video)"* survived Part 124 and Part 125 — which had corrected the
identical claim in `GenerationKinds.Model3d`'s shipped XML doc — because the fix was made where the defect
was FOUND rather than everywhere the claim lived. `check-docs` cannot see it: no decision retired a word
here, so the sentence stays grammatical, plausible and wrong. **When a decision falsifies a claim, grep the
claim, not the file you were reading.**

- GEN7a — the pipeline runner at `image → video`, the half the survey unblocked

## Part 127 — the salience defaults: measured on both embedders, and neither default moved (2026-08-30)

✅ done 2026-08-30 — `TASKS.md` Part 65's "two option defaults, and one makes the other inert". The owner
was asked whether `SalienceOptions.MaxSalience` (4) and `NoveltyWeight` (1.5) should stay and answered
**measure it**, so the question stopped being a decision and became a ladder. Tables in `docs/memory.md` §5.
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

✅ done 2026-08-31 — `TASKS.md` Part 128's first item, "make `Relevance` comparable before it is ranked."
Shipped as `IMemorySeedSource` (`docs/DECISIONS.md` **D103**): `ReciprocalRankFusionPolicy` now fuses each
source's own ranked list instead of one pooled `Relevance` field. Tables and the full reading are
`docs/memory.md` §5.

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
cannot contain a 4-point floor. Full tables and reading: `docs/memory.md` §5.

**RETRACTED 2026-09-01, same day — the superadditivity claim, at the full 1,540-question sample.** Every
reading below (+6.7 / +4.2 / +6.8) was taken at n = 100. A run over the COMPLETE published LoCoMo QA set —
all 1,540 questions, `--seeds 16` — puts the interaction at **+1.0**, not distinguishable from zero at that
sample: an interaction is a difference of differences, so its error runs roughly double any one component's,
and +1.0 with that error is a reading with no sign anyone should trust. This is a retraction, not a fourth
point for the series below. **The category wins this run's underlying data also implied are retracted with
it**: `lyntai-fused-3shot` appeared to beat `vector-40` by +6.2 on multi-hop and +5.5 on open-domain at
n = 100; at full sample the four category deltas are −0.6, −2.1, +0.1 and −0.3 — three sign flips and a
collapse. **What survives is the volume-vs-form reading immediately below**, restated at full-sample
resolution: the residual against `vector-40` reads **−0.6** points (≈9 of 1,540 questions) rather than this
entry's 2.5-point trail, still readable as "mostly volume, not form" — that qualitative conclusion transfers
because it was never a difference of differences. Full reading: `docs/memory.md` §5's retraction subsection.

**A methodological correction rides along, and it matters more than the number.** The interaction between
per-source ranking (**D103**) and the n-shot walk (**D100**), reported here as +6.7 and then corrected to
+4.2, is **non-monotonic**: the third point reads +6.8, back near the original. The "seed starvation explains
the shrink" direction drawn from two points did not survive a third — the second time in this line of work
that one extra measurement overturned a published conclusion. What replaces the point estimate is a
size-free claim: fused ranking raises the CEILING on what the walk can use (the shipped-ranking three-shot
arm plateaus at 33.4–33.6%; the fused one keeps climbing, 38.5 → 44.5 → 47.3%, decelerating but not flat).

**The pre-registered `--seeds 16` prediction held, on a stated LOW confidence.** It called a gain under +6.0,
landing in 46–49%, still short of `vector-40`'s 49.8 at ~7000 chars/q; the run read +2.8, landed at 47.3%,
short by 2.5, at 6747 chars. A correct call on a self-described "thin" prior does not vindicate the reasoning
behind it — that model was already known wrong from the `--seeds 8` round.

**What is left is a different question than the one this Part opened.** At 47.3%/6747 chars the engine
roughly matches `vector`'s 45.7%/3634 chars — an EFFICIENCY gap (~1.9× the context for matching accuracy),
not a capability one. No follow-up item is opened for it here; it is recorded as a finding in
`docs/memory.md` §5, not as a startable task.

- Separate volume from form in the walk's residual gap against `vector-40`.

## Part 135 — the context-efficiency gap: the walk's cost is in SLOTS, not characters

✅ done 2026-09-02 — Built `node devtools/dev.mjs memory-locomo --composition` (`MemoryLocomoBench.cs`), a
model-free two-arm mode that decomposes a returned context into headline versus content and counts
duplication within it. Full sample, 1,540 questions, 1,269.7s. Findings and tables:
`docs/memory.md` §5 finding 9. **Three of the four questions needed no run at all** — they fall out of the
published QA table divided through by its own `items/q`, validated by a corpus reconstruction that
reproduces the `full` row (601.4 items/q, 101209 chars/q) to the character.

**The Part's own premise was half a comparison.** The ≈1.9× that opened it (6,536 against `vector`'s 3,460)
compares 40 items to 20. At matched count the walk spends **6,536 against `vector-40`'s 6,780 — 3.6% LESS**,
a row the same table already carried. The cost is in SLOTS, not characters: 40 slots to reach what cosine
does in 20, at a cheaper price per slot.

**And the Part named a dump that could not answer it.** `devtools/_locomo-dump-181506.jsonl` holds 12,320
lines with exactly ONE key set — `arm, question, gold, answer, unknown, f1, exact, judge`. It records what
the READER answered, for judge calibration; no context, no pieces, no chars. Censused, not inferred from the
flag's name. The ~5-hour cost quoted for regenerating it was the QA run's and never applied here — the
diagnostic needs no reader.

**Measured:** upgraded share **80.0%** (32 of 40) and it is STRUCTURAL, not empirical —
`SeedsPerStep × (shots − 1) / MaxItems`, since the seed budget bounds the raise rather than the corpus.
Headline items average **112.5** chars, content items **174.9** (against `vector-40`'s 168.5, so expansion
walks toward richer turns). Duplication is **0.00** pairs per question on both arms over 61,600 items each,
under a positive control proven on the red path.

**What it got wrong, for the next reader.** The pre-registered upgrade prediction (88–95%) was FALSIFIED at
80%: it was reached by inverting mean lengths, which assumes both sub-populations sit at the corpus mean and
neither does. A structural quantity was never the kind of thing an interval over corpus statistics could
find. **And the mode measured the wrong arm on its first run** — it defaults to `--seeds 3` where the table
it explains was taken at `--seeds 16`, and reported a plausible 4,950 chars/q with nothing erroring; the
reusable rule is in `.claude/knowledge/pitfalls.md`. Its control now reproduces the published rows exactly
(6,536 / 6,780, and shot-1's 2,275 against the `lyntai-fused` row).

**It also turned up something bigger than itself**, which is now `TASKS.md` **Part 136**: `evidence-hit@k`
matches a `(dia_id)` that survives headline truncation in **5,882 of 5,882** turns, so the metric scores a
half-turn and a whole turn identically while the graph arms return the first and the cosine arms the second.

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
misses. `memory-locomo --n 200 --no-judge`, seed 12345, 2,712.4s. Tables: `docs/memory.md` §5.

**token-F1 goes 29.3% → 41.0%, landing on plain cosine's 41.3%** at the same 20 slots and within 1.2% of the
same context (3,583 against 3,541 chars). At n = 200 one question is ≈0.5 points, so +11.7 is ~23 questions
and −0.3 is under one. **The engine's ranking is not worse than cosine — the entire measured QA deficit was
headline truncation**, and the retrieval ladder was right about the ordering all along. `unknown` corroborates
it: the reader refused 29 times truncated and 13 rehydrated, against cosine's 12, on the SAME 20 turns.

**A design fact that falls out, and it is the durable half.** One shot with full content beats three shots
with mixed content for less context — 41.0% on 3,583 chars against `lyntai-fused-3shot`'s 38.0% on 4,968.
Expansion is an expensive way to obtain content already in hand. That does not touch **D100**, whose claim is
about DISCOVERING material a first load did not hold.

**What the Part got wrong, for the next reader.** Its prediction was pre-registered at 38–45%, then REVISED
DOWN to 32–40% before the run on a fixed-slot contrast (`lyntai-2shot` → `lyntai-3shot`: identical 40 items,
content 40% → 80%, +1.1 token-F1). The outcome is 41.0%, so **the original was right and the revision was
wrong by an order of magnitude**. The pre-registration had already named the reason — that contrast upgrades
shot 2's newly-discovered NEIGHBOURS, so it prices the low-value half — and the revision was made anyway.
The lesson is about extrapolating a natural experiment past the population it measured, not about the
arithmetic, which was correct.

**CONFIRMED at full sample 2026-09-02 (Part 138).** The parity claim was an n = 200 reading when this was
written, and the same session's 40-slot claim flipped sign at full sample — so it was filed as a Part rather
than believed. It held: **42.0% against `vector`'s 42.4%** on all 1,540 questions, a −0.4 where this read
−0.3, at the same 20 slots. What did NOT survive is the category detail below — multi-hop's 38.0-vs-37.8
near-tie reads −4.0 at full sample. `docs/memory.md` §5 carries the run.

**Not settled, and deliberately not opened as a new Part.** This measures a HARNESS arm, not a shipped path:
a consumer reaches the same place with N calls of `ExpandAsync(reference, hops: 0)`, which is supported and
is N store round-trips. Whether a recall should be able to return content directly is a DECISION nobody has
taken, so it belongs in the decision record rather than the backlog (`repo-mechanics.md` §"A conditional item
is not a task"). Also open: one reader tier, one embedder, n = 200, and category splits that do not all point
one way (temporal and open-domain still trail cosine).

- Build the REHYDRATION arm and run it.

## Part 137 — does the WALK gain from its extra 20 slots the way deeper cosine does?

✅ done 2026-09-02 — **Yes: LEVEL with plain cosine at 40 slots.** `lyntai-fused-3shot-full` — the three-shot
walk asking for `MemoryDetail.Full` — scores **44.0%** token-F1 against `vector-40`'s **44.4%** at 39.7 slots
and 6,941 chars against 6,780, on all 1,540 questions. Tables: `docs/memory.md` §5. The engine was measured
12 points behind cosine before headline truncation was found; it is now within half a point.

**An n = 200 pass read this as +0.9 and AHEAD, and that was retracted by the full sample.** Same arms, same
seed: 44.8/43.9 at 200 questions, 44.0/44.4 at 1,540 — the sign flipped. The retraction is in
`docs/memory.md` §5 rather than only here, because the "first time the engine is ahead" claim had already
been written into the record.

**The noise floor is why one reading was believable and the other was not.** Two arms sending byte-identical
prompts (`lyntai-fused-full`, `lyntai-fused-api`) scored 40.7/41.1 at n = 200 and **42.2/42.2** at n = 1,540:
reader nondeterminism collapsed from 0.4 points to 0.0. A ±1-point difference was never resolvable at 200.

**The pre-registration was RIGHT and its n = 200 adjudication was WRONG — the more useful of the two
findings.** It called "40–45%, gaining on 38.8 but NOT reaching 44.5". Full sample: 44.0%, in band, not
reaching 44.4 — both clauses correct. At n = 200 the second was declared refuted and written up as "the run
refuted my scepticism, not the design". **Adjudicating a ±1-point prediction on a sample that cannot resolve
±1 point is exactly what Part 134 did with its interaction at n = 100**, a lesson already recorded in
`docs/memory.md`, quoted during this session, and repeated anyway. Pre-registration protects nothing if the
run that judges it is underpowered.

**Not confirmed, by a flaw in the confirming run's own design:** its arm set was chosen for the 40-slot
question and dropped `vector` (20 items), so **Part 136's 20-slot parity has no full-sample control** —
`lyntai-fused-full` reads 42.2% there against nothing. **Filed as Part 138 and CLOSED the same day**: the
missing pair ran at full sample, Part 136's claim survived (−0.4 against its −0.3), and `lyntai-fused-api`
repeating **42.2 → 42.0 at an identical 3,503 `chars/q`** gives this run the cross-run reproducibility check
it could not give itself.

**The FIRST run of this arm was void and is recorded because of how it was caught.** It scored 42.0% —
inside the predicted band AND branch — while measuring 20 whole items plus ~20 truncated ones, because
`ExpandAsync` projected discovered neighbours exactly as a recall does and D104 had only covered the recall.
`chars/q` gave it away (6,053 where 40 whole turns cost ~7,000), not the accuracy column. **A prediction
landing where you expected is when you are least inclined to ask what produced it**, so the re-run stated
its instrument check in advance: chars/q must rise to ~6,600–6,900 or the fix had not reached the arm. It
read 7,084.

**And the test for that fix passed for the wrong reason first.** The walk SELF-HEALS — an entry discovered as
a headline is upgraded once a later step seeds it — so asserting on the last step's held set passes whether
or not expansion honoured anything; it passed with the fix reverted, and only a mutation check said so. The
assertion belongs on `NewItems` at the step that discovered them.

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
read −0.3 at n = 200. Same sign, same magnitude. `docs/memory.md` §5 has the table and the reading.

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

## Part 144 — it was the DEPTH: the same judge is level at 2× and catastrophic at 4×

✅ done 2026-09-03 — **Part 143's "capability floor" is a depth×capability interaction, and this corrects
it.** Varying only `GraphMemoryOptions.VerificationDepth` on one 4B judge: **83.0% at depth 20, 84.0% at 40,
72.5% at the shipped 80.** Same model, same prompt, same base arm. `docs/memory.md` §5 has the table.

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
a capability FLOOR rather than a capability curve. Table, audit and caveats: `docs/memory.md` §5.

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
both clauses held. `docs/memory.md` §5 has the table.

**It corrects a claim published earlier the same day.** Every verdict arm was built on `+forget0`, which
registers no semantic channel, so *"a pure formula beats formula-plus-oracle, the deficit was never the
model tier"* compared two arms differing in SEEDING as well as in the judge. The limitation was written down
when that claim was made, and measuring it turned the conclusion around. **What survives is narrower: a
judge cannot rescue a badly-seeded pool** — which is what `+forget0+oracle`'s 74.6% measures — and it says
nothing about a judge on a good one.

Consequence for the backlog: Part 128's real-judge item builds on `+sem+rel-only`, not `+forget0`.

- Run the oracle on the best mechanical arm before spending a model run on a dominated base.

## Part 141 — is there a configuration good at BOTH workloads? A frontier, and no free lunch

✅ done 2026-09-03 — **No, and the decision rule was missed by about a point on each clause, so no default
moves.** `+sem+forget2` (semantic seeds at shipped K, `RetrievabilityWeight = 2`) reads **69.5% LoCoMo /
78.8% knowledge-update** against the pre-registered ≥70% / ≥80%. It buys search at the best exchange rate
measured — 2.0 points per point of suppression — but the trade is smooth and irreducible rather than free.
Table and the mechanism: `docs/memory.md` §5.

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
`TASKS.md` Part 128 filed as "not startable until both workloads are measured" is settled. Table, mechanism
and the cross-workload trade: `docs/memory.md` §5.

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
**D103** superseded the day the item was written, and `docs/memory.md` §5 had recorded the post-fusion cell
as level ever since — the doc was updated and the item beneath it was not. **By measurement**: both readings
were n = 200 cells of ~37 questions, and at full sample `+sem+rel-only` reads 79.8% multi-hop against
`vector`'s 83.0%. Ordinary category deficit, no depth question follows. Tables: `docs/memory.md` §5.

**Two results outrank the one it was filed for.** `+sem+rel-only` clears plain cosine on the whole benchmark
(**82.6 against 81.1**) — the first powered confirmation of that — and the PERFECT-JUDGE arm is the worst of
the three at **74.6%**. A pure formula beats formula-plus-oracle. That does NOT establish that a judge adds
nothing: no arm pairs the judge with semantic seeding, which is what re-aims Part 128's judge item.

**The run needed a harness fix**, and it exposed a class of defect: `+forget0+oracle` could not be
CONSTRUCTED at n = 1,540, so the arm whose ceiling three maintained records quote had never run on the whole
benchmark. `docs/FIXES.md` has the incident; `pitfalls.md` has the rule and the two instrument facts.

- Multi-hop is the one category no judge fixes.
