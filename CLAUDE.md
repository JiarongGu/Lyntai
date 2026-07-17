# CLAUDE.md — Lyntai (灵台)

> Auto-loaded every session. Keep short — details live in `docs/` and `.claude/rules/`.

## What this is

**Lyntai** (灵台, "the numinous platform" — the seat of the mind) is a reusable **.NET 10 library**: the
shared **cortex + persistence** substrate extracted from the sibling apps (Gatherlight, Vidora, Sonora)
and the mastra/odysseus studies, so a new project gets **LLM providers + pluggable storage + LLM-ops**
without rebuilding them. It is a *library* (a set of NuGet-packable projects), not an app — no server,
no host, no UI.

The two things it provides: (1) an **LLM provider abstraction** with routing + **fallback** across CLI /
API / `Microsoft.Extensions.AI`-bridged providers, and (2) **pluggable storage** (SQLite now, interfaces
so other backends follow as separate packages) — plus the LLM-ops layer (prompt registry, scoring/eval,
run traces, task-scoped memory) and DI wiring (`AddLyntai(...)`).

## Current state

**Implemented + hardened (v0.9.0).** All of `tasks.md`, a review/research hardening pass, then roadmap
v0.3–v0.9 (v0.7 = bring-your-own resources: `IProcessRunner`, BYO HttpClient, BYO `IDbConnectionFactory`
+ `migrate:false`, provider presets — the app owns resource lifecycle, Lyntai provides the interface;
v0.8 = `Lyntai.Providers.Local` in-process GGUF inference via LLamaSharp, managed-only so the app picks
the backend; v0.9 = agentic tool-calling `Lyntai.Agents` — provider-agnostic `IToolLoop` over
`ILlmClient`, `ITool`/`AddTool` DI collection, first platform-kit §9 cut): `ILlmClient` front door (to a
consumer, Lyntai behaves like ONE provider — keep new surface
behind it), `AsChatClient()` reverse bridge, shared `LlmVerdictClassifier`, configurable
`RoutingPolicy` (the §6 switch is now its default — tune via `ConfigureRouting`/`LYNTAI_*`), OTel GenAI
telemetry (`LyntaiDiagnostics`) + `RunTrace.TraceId` bridging, structured output (`CompleteJsonAsync`),
versioned prompts (`IPromptVersionStore`), judge calibration (`JudgeAgreement`/`IPairwiseComparer`),
memory lifecycle (dedup/TTL/`PruneAsync`), **three storage backends** (`Sqlite`, `InMemory`, `Postgres`
— pg_trgm recall, Testcontainers-tested) mixable per-domain via DI, deferred migrations,
`lyntai_`-prefixed objects, BenchmarkDotNet, an opt-in live-Ollama test (`LYNTAI_LIVE_OLLAMA`), and a
public-API baseline (`ApiSurfaceTests` — update the baseline deliberately on any public-surface change).
Tests/e2e green.
- `docs/2026-07-17-lyntai-design.md` — the **contract** (interfaces, fork decisions, semantics —
  note the dated §6 amendments; §6 is now the default `RoutingPolicy`). Read it first.
- `docs/ROADMAP.md` — the forward sequence (v0.4+ and standing maintenance policies).
- `CHANGELOG.md` — per-release detail; breaking changes called out.
- `README.md` — the consuming story (install, `AddLyntai`, the add-ons, semantics).

Namespace map (Core): `Lyntai.Llm` (contract types) / `Lyntai.Llm.Routing` (router engine) /
`Lyntai.Prompts` / `Lyntai.Cortex` (+ `.Scorers`) / `Lyntai.Agents` (tool-calling loop) /
`Lyntai.Storage` / `Lyntai.Processes` / `Lyntai.Text`; builder + `Add*`/`Use*` extensions live in the
`Lyntai` namespace.

## Rules, knowledge & skills

- **`.claude/rules/`** (always-on) — `dev-conventions.md` (package layout, async Dapper + `snake_case` +
  `CAST(x AS REAL)`, FluentMigrator numbering, FTS5-trigram, spawn hygiene, DI-collection variation
  points, the devtools loop) and `sensitive-info.md` (no dev-machine paths / private tokens; pre-commit
  guard — install once with `node devtools/dev.mjs install-hooks`). See `.claude/rules/RULES_INDEX.md`.
- **`.claude/knowledge/`** (on-demand deep dives — read the one you're touching):
  `extending-lyntai.md` (the four extension points), `llm-and-router.md` (verdict taxonomy, fallback §6
  amended, streaming-commit + inactivity-clock invariants, CLI hygiene), `storage.md` (Dapper/CAST/FTS5
  trigram triggers/pragmas/`lyntai_` prefix), **`pitfalls.md` (traps that pass the build/tests while
  being wrong — read before extending)**.
- **`.claude/skills/`** — invoke for an extension task: `add-provider`, `add-storage-backend`,
  `add-scorer`, `add-migration`.
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**
- Working files (probes, scratch) go under `devtools/_*` (gitignored), never OS temp.
- **This machine's console is GBK** — write files with the Write/Edit tools or `-Encoding utf8`, never
  `echo`/`Set-Content` UTF-8 through the console (it lossily mangles CJK/em-dashes). See `pitfalls.md`.

## Dev loop

- **`node devtools/dev.mjs verify`** — the "am I done?" gate: build → test → e2e → leak scan. Run before
  claiming a change is complete.
- `node devtools/dev.mjs build` — build the solution.
- `node devtools/dev.mjs test [args]` — run the xUnit tests.
- `node devtools/dev.mjs e2e [pN|all] [--build] [--parallel]` — boot `Lyntai.Playground` against the
  deterministic provider-stub (`LYNTAI_PROVIDER_CMD`) over isolated `devtools/_e2e-*` data folders.
- `node devtools/dev.mjs new-migration <name>` — scaffold the next FluentMigrator migration (unique number).
- `node devtools/dev.mjs playground` — run the sample console app.
- `node devtools/dev.mjs bench [-- --filter *X*]` — BenchmarkDotNet (Release) router/FTS benchmarks.
- `node devtools/dev.mjs pack` — `dotnet pack` the libraries → `publish/packages/`.
- `node devtools/dev.mjs check-sensitive [--tree]` — leak scan.
