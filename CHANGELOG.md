# Changelog

All packages version in lockstep from `src/Directory.Build.props` (`VersionPrefix`).
Pre-1.0: minor bumps may carry breaking changes; each is called out below.

## Unreleased

A second independent audit pass (four adversarial reviewers over the router, providers, storage, and
cortex) — findings the 0.2.0 review + 221 tests missed. No API breaks.

### Fixed
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
