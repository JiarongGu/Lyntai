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

**Released: v2.4.0.** Twelve packages; public API frozen under SemVer 2.0 since 1.0 — with ONE
carve-out, the **`Lyntai.Generation` PACKAGE** (the backends), which ships EXPERIMENTAL until `TASKS.md`
GEN-VERIFY closes.

Everything through the roadmap's v0.3–v0.31 shipped (routing depth, LLM-ops, three storage backends, BYO
resource seams, local GGUF, agentic tool-calling native + prompt, MCP both directions, durable jobs, the §9
platform kit, OTel, governance decorators, semantic + curated memory, the agent-session primitive), then
**1.0** froze the API, **1.1** generalized CLI tool-hosting, **1.2** added turn-free backend probe/auth +
pinned self-install, **2.0.1** landed the generation platform and the package graph it needed, **2.1.0**
made the generation backends registerable in one line each, **2.2.0** shipped the provider-lifetime seam
(`Lyntai.Lifecycle`; D37) with `LlmVerdict.NotConfigured` (D38), `AddSemanticMemory` (D41), honest
`MigrateUpAsync` twins (D40) and `CodexAgentSession` (D42), **2.3.0** carried the pre-release whole-library
review that 2.2.0 shipped without (D44–D46), and **2.4.0** gave an agent session the host's own MCP servers
on either CLI backend (D47). Per-release detail is `CHANGELOG.md`; the reasoning is `docs/DECISIONS.md`
(D1–D51 — the memory subsystem is **D48–D51**).

**`## Unreleased` is substantial — read it before assuming a behaviour.** Since 2.4.0 it holds ONE thing,
the **long-term memory subsystem**: named memory engines resolved by name like `IHttpClientFactory`
(`IMemoryEngine` / `IMemoryEngineFactory` / `AddMemoryEngine`; **D48**), a graph engine whose entries decay,
connect and open as a cheap index (`UseGraph()`), decay measured in **interference rather than elapsed
time** with the clock as a seam (**D49**), burial rather than deletion (**D50**), InMemory + SQLite +
Postgres backends under one contract, and `AddMemoryTools` exposing recall/expand to the model. Purely
additive — nothing an existing consumer calls changed — so the next release is a **minor**.

**The packaging rules are now gated, not remembered** — `verify` runs eight checks, four of them added at
2.0.1 and `check-docs` added with the memory work (a doc that uses vocabulary a decision retired fails the
build — the prose counterpart to `check-warnings`; **D51**): `check-warnings` (a warning in a published project fails the build, because an unfailed IL2026 is a
FALSE trim promise), `check-packages` (a package must be registered in all nine registries — a missing
`ApiSurfaceTests` entry means no API gate at all), `check-bundle` (the bundle's dependency closure cannot
grow without a decision), plus `consumer-smoke` outside `verify` (pack, then restore/build/run a fresh app
against the PACKAGES). Adding a package is `node devtools/dev.mjs new-package <Lyntai.X>`.

Tests/e2e green: 1567 tests, e2e 3/3.

**The records, and what each is for:**
- `docs/2026-07-17-lyntai-design.md` — the **contract** (interfaces, fork decisions, semantics —
  note the dated §6 amendments; §6 is now the default `RoutingPolicy`). Read it first.
- `docs/ROADMAP.md` — what is shipped per version, then `## Planned`: the next sequence (generation, to
  close the experimental carve-out) and the standing maintenance policies.
- `CHANGELOG.md` — per-release detail; breaking changes called out.
- `README.md` — the consuming story (install, `AddLyntai`, the add-ons, semantics).
- `TASKS.md` — the **active** backlog (open tasks only); `docs/task-archive.md` — the completed-task
  history (the frozen implementation plan + closed backlogs). See the `task-lifecycle.md` rule.
- `docs/FIXES.md` — the fix log: per-incident symptom, root cause, fix and verification (the `fix-log`
  skill's target; created with the first entry — see `repo-mechanics.md` §Fix log).
- `docs/<date>-*.md` — point-in-time designs, not maintained state: check the status banner (or the date
  against `CHANGELOG.md`) before treating one as executable.
- `docs/superpowers/INDEX.md` — the tracked LIST of per-version design records; the records themselves are
  **untracked**, in `local/superpowers/{specs,plans}/`. They describe one version's work and stop being
  true once it ships, so they are a working record, not a contract — **anything in one that must outlive
  its version belongs in a maintained document** (the contract, `DECISIONS.md`, `pitfalls.md`, the
  archive). Write new ones straight into `local/`; the brainstorming/writing-plans skills default to
  `docs/superpowers/`, so redirect them.

**The `Lyntai.Generation` PACKAGE is EXPERIMENTAL as of 2.0.1** — exempt from the SemVer promise until
GEN-VERIFY closes (unmeasured backends + an unimplemented stream seam), so it may be reshaped in a minor. Say
so in the docs before changing it; every other domain needs a major.
**The carve-out is the PACKAGE, not the `Lyntai.Generation` NAMESPACE** — the generation CONTRACTS
(`GenerationResult`, the routing policy, `GenerationVerdictClassifier`, …) live in that namespace *inside
`Lyntai.Core`*, which is mandatory for every consumer and carries the FULL promise. Read the reason clause
before claiming the exemption: it is about backends written from vendor docs with no key to call and a stream
seam nothing implements — all of which are in the package. When in doubt, apply the full promise
(`docs/DECISIONS.md` D43 did).

Namespace map (Core): `Lyntai.Llm` (contract types) / `Lyntai.Llm.Cli` (the shared spawned-CLI engine +
per-CLI `ICliProviderDialect` — a new CLI backend is a dialect, never a new provider; see `DECISIONS.md`
D27/D28) / `Lyntai.Generation` (+ `.Routing`/`.Jobs`/`.Tools`) (the generation platform — image/video/audio/3d behind one
capability-aware seam; the CONTRACTS are in Core, the BACKENDS are the `Lyntai.Generation` package under
`Lyntai.Generation.Providers` — split for release cadence, `DECISIONS.md` D30/D31/D34) /
`Lyntai.Llm.Routing` (router engine) /
`Lyntai.Llm.Caching` (response cache) / `Lyntai.Llm.Budgeting` (usage budget) /
`Lyntai.Llm.RateLimiting` (rate limiter) /
`Lyntai.Embeddings` (embedder seam) / `Lyntai.Memory` (semantic memory + vector store) /
`Lyntai.Prompts` / `Lyntai.Cortex` (+ `.Scorers`) / `Lyntai.Agents` (tool loop + chat orchestration) /
`Lyntai.Jobs` (durable jobs) / `Lyntai.Guards` (guard rail) / `Lyntai.Secrets` (secret vault: AES-GCM/BYO
+ recovery-key envelope; DPAPI binding in the `Lyntai.Secrets.Dpapi` adapter) /
`Lyntai.Lifecycle` (provider POOL + `ProviderKey` + admission — for an app whose backend configuration is
owned outside the deployment; `DECISIONS.md` D37) /
`Lyntai.Storage` / `Lyntai.Processes` / `Lyntai.Text`; builder + `Add*`/`Use*` extensions live in the
`Lyntai` namespace.

## Rules, knowledge & skills

- **`.claude/rules/`** (always-on) — `dotnet-package-layout.md` (contract in Core, impl in an adapter,
  never adapter→adapter; split by dependency footprint; DI-collection variation points; naming),
  `skills-workflow.md` (start a non-trivial task through the discovery skills — and READ what they route
  you to), `sensitive-info.md` (no dev-machine paths / private tokens; pre-commit guard — install once
  with `node devtools/dev.mjs install-hooks`), `task-lifecycle.md` (`TASKS.md` = OPEN backlog only; a
  completed task MOVES to `docs/task-archive.md`), `persist-working-state.md` (checkpoint a decision or
  finding to its in-repo home WHEN it happens, not at the end), `no-global-memory.md` (project facts live
  IN-REPO — `.claude/**` / `docs/DECISIONS.md` — global memory is user-prefs only),
  `file-tool-discipline.md` (inspect files with `Read`/`Grep`/`Glob` not `Bash cat/ls/find`; never evade
  the permission gate), `no-tmp-for-repo-files.md` (compose with `Write`; scratch → `devtools/_*`, never
  OS temp), and `windows-machine.md` (the traps that succeed WRONGLY — PowerShell 5 round-trips, BOMs,
  lying exit codes). Those are canonical (synced by `daoris`) and state the PRINCIPLE; this repo's
  concrete bindings — package names and the packable/version layout, the `Dto`-free naming invariant,
  guard scripts, version-authorship policy, the dev loop and test conventions, scratch paths — live in
  the local, never-synced `repo-mechanics.md`. See `.claude/rules/RULES_INDEX.md` (generated).
- **`.claude/knowledge/`** (on-demand deep dives — read the one you're touching):
  `extending-lyntai.md` (the five extension points — provider, storage backend, scorer, CLI tool-hosting
  dialect, migration), `llm-and-router.md` (verdict taxonomy, fallback §6 amended, streaming-commit +
  inactivity-clock invariants, CLI hygiene), `storage.md` (Dapper/CAST/FTS5
  trigram triggers/pragmas/`lyntai_` prefix), **`pitfalls.md` (traps that pass the build/tests while
  being wrong — read before extending)**, `generic-library.md` (turning a consumer ask into app-agnostic
  surface) — plus the canonical `library-api-design.md` (generalize the ask, never ship its shape),
  `sql-storage.md` (the SQL traps that return wrong data rather than failing), and `model-decoupling.md`
  (which model is a DEPLOYMENT choice, never part of a feature's definition).
- **`.claude/skills/`** — extension tasks (`add-provider`, `add-storage-backend`, `add-scorer`,
  `add-migration`), process (`archive-task` — move a finished task from `TASKS.md` to the archive), and
  the canonical set (`doc-loader`, `pattern-finder`, `post-feature`, `fix-log`, `caveman`).
- **TDD** (failing test first) and **commit per task**. **Never commit without explicit user approval.**
- **Backlog vs archive:** `TASKS.md` holds only OPEN tasks; completed work is moved to
  `docs/task-archive.md` (see `task-lifecycle.md`), and `CHANGELOG.md` is the release-facing log.
- Working files (probes, scratch) go under `devtools/_*` (gitignored), never OS temp.
- **This machine's console is GBK** — write files with the Write/Edit tools (in a script,
  `fs.writeFileSync` or `-Encoding utf8`, which adds a BOM on PowerShell 5); never `echo`/`Set-Content`
  UTF-8 through the console (it lossily mangles CJK/em-dashes). See `pitfalls.md` / `windows-machine.md`.

## Dev loop

- **`node devtools/dev.mjs verify`** — the "am I done?" gate, eight checks stopping at the first failure:
  build → warnings → packages → bundle → **docs** → test → e2e → leak scan. Run before claiming a change is
  complete.
- `node devtools/dev.mjs build` — build the solution.
- `node devtools/dev.mjs check-packages` — **fail if a package is missing from any registry it needs** (part of
  `verify`): `packableProjects`, the solution, `ApiSurfaceTests` (list + anchor map), the test project's
  references, a baseline, the `docs/AOT.md` table, the README table — plus the reverse, so a deleted package
  leaves nothing stale behind. Shipping a package touches NINE registries and the misses are silent (no
  `ApiSurfaceTests` entry = no API gate at all). Many small packages is the intended shape — `DECISIONS.md` D33.
- `node devtools/dev.mjs check-bundle` — **fail if the `Lyntai` bundle's dependency closure drifted** (part of
  `verify`). The bundle forces every dependency on every one-line-install consumer (an untrimmed publish copies
  the whole graph), so membership is a budget: see `docs/DECISIONS.md` **D32** for the rule and
  `bundle.allowedThirdParty` in `devtools/project.config.mjs` for the approved list.
- `node devtools/dev.mjs check-warnings [--list]` — **fail if any `src/` project compiles with a warning** (part
  of `verify`). Not style policing: `IsAotCompatible=true` stamps `IsTrimmable` into the assembly, so an
  unfailed IL2026/IL3050 is a FALSE trim promise shipping to consumers (four did), and an unresolved doc cref
  ships inside the XML docs consumers read.
- `node devtools/dev.mjs check-docs` — **fail if a doc uses vocabulary a decision retired** (part of
  `verify`). The prose counterpart to `check-warnings`: the CODE is gated from every side while the DOCS are
  gated from none, so a spec paragraph that quietly stops being true survives everything and the next
  session reads it and implements the wrong thing — which happened twice on 2026-08-08, caught both times
  only by a human reading it. The registry is `retiredTerms` in `devtools/project.config.mjs`: a term, what
  to say instead, and why. **Add an entry whenever a decision renames or re-dimensions something.**
  Historical records (`CHANGELOG.md`, `docs/task-archive.md`, `docs/superpowers/plans/`) are exempt because
  they are accurate BY using the vocabulary of their day — **specs are not**, since a spec is read as the
  contract. Put `drift-ok` on a line that deliberately names the retired thing.
  Unlike `decisions-index` this IS in `verify`: a stale index costs a reader one `Ctrl-F`, a stale spec
  costs an implementation.
- `node devtools/dev.mjs test [args]` — run the xUnit tests.
- `node devtools/dev.mjs e2e [pN|all] [--build] [--parallel]` — boot `Lyntai.Playground` against the
  deterministic provider-stub (`LYNTAI_PROVIDER_CMD`) over isolated `devtools/_e2e-*` data folders.
- `node devtools/dev.mjs new-migration <name>` — scaffold the next FluentMigrator migration (unique number).
- `node devtools/dev.mjs new-package <Lyntai.X> [--description "…"]` — scaffold an adapter package (csproj +
  its `Add*` entry point) and register it in all **nine** registries `check-packages` gates. Bundle membership
  is deliberately NOT automatic (D32). See `DECISIONS.md` D33.
- `node devtools/dev.mjs playground` — run the sample console app.
- `node devtools/dev.mjs bench [-- --filter *X*]` — BenchmarkDotNet (Release) router/FTS benchmarks.
- `node devtools/dev.mjs pack` — `dotnet pack` the libraries → `publish/packages/`.
- `node devtools/dev.mjs consumer-smoke` — **the release gate**: packs every package to a scratch feed under a
  throwaway version, then restores + builds + runs a fresh console app against the PACKAGES (not project
  references). The only check that exercises what actually ships — nuspecs, dependency groups, symbol packages,
  the bundle restore. Minutes, so deliberately NOT in `verify`; run before a release or after touching packaging.
- `node devtools/dev.mjs check-sensitive [--tree]` — leak scan.
- `node devtools/dev.mjs doctor [--fix]` — README `## Status` version ↔ `VersionPrefix`, and `VersionPrefix`
  ↔ the newest `v*` tag (**never hand-edit the version** — see `repo-mechanics.md` §Never hand-edit the
  version / `DECISIONS.md` D25).
- `node devtools/dev.mjs check-version` — the pre-commit version-authorship guard, run by hand.
- `node devtools/dev.mjs decisions-index [--check]` — regenerate the index table at the top of
  `docs/DECISIONS.md` from its own headings. **Run it after adding a `D<n>` entry** (`--check` reports
  staleness without writing). Deliberately NOT in `verify`: a stale index costs a reader one `Ctrl-F`, and
  `verify` stays the build/test gate rather than growing a documentation check.
