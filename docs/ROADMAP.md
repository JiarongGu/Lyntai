# Lyntai — Roadmap

> The design contract is `2026-07-17-lyntai-design.md`; §9 lists what was deliberately deferred.
> This file sequences how the deferred and newly-identified work lands. Dates are intentions,
> not promises; pre-1.0 minor versions may carry breaking changes (called out in `CHANGELOG.md`).

## Shipped

### v0.1.0 — the substrate (2026-07)
Full `tasks.md` sequence: core abstractions + fallback router, SQLite storage (FTS5-trigram
memory), claude-CLI / OpenAI-compatible / MEAI providers, cortex layer, Playground, e2e, packaging.

### v0.2.0 — production hardening (2026-07)
Multi-agent code review (10 confirmed bugs fixed) + best-practices research pass applied:
`ILlmClient` front door + `AsChatClient()` reverse bridge, shared verdict classifier, finer verdict
taxonomy (context-window / auth), amended RateLimited semantics (cool host, advance), ProcessRunner
lifecycle correctness, OpenTelemetry GenAI spans/metrics, structured output (`CompleteJsonAsync`),
trim/AOT analyzers, symbols + embedded sources. Plus a second adversarial audit pass (streaming
inactivity clocks in every provider, empty-content commit gate, env/telemetry/idempotency fixes).

### v0.3.0 — routing & resilience depth (2026-07)
Configurable `RoutingPolicy` (the §6 switch becomes the default policy): per-verdict action,
retry-then-advance, per-(provider, model) cooldown granularity, sole-candidate exemption — all
tunable via `ConfigureRouting` / `LYNTAI_*` env. Deferred migrations (`migrateOnFirstUse`).
BenchmarkDotNet project (router overhead, FTS recall at scale).

### v0.4.0 — LLM-ops depth (2026-07)
Versioned prompt overrides (`IPromptVersionStore`, history + rollback); judge calibration
(`JudgeAgreement` metrics + position-bias-aware `IPairwiseComparer`); memory lifecycle (dedup,
per-entry TTL, `PruneAsync`); trace↔span bridging (`RunTrace.TraceId`). Remaining v0.4 idea —
LLM summarization/compaction of old memory — deferred as a composition-helper pattern (the
deterministic lifecycle primitives shipped; summarization has no settled recipe yet).

### v0.5.0 — ecosystem & backends (2026-07)
- ✅ **Composite store seam** — `Lyntai.Storage.InMemory` is the second real backend; the mastra
  "one interface per domain, many backends" pattern is expressed through DI (the container is the
  registry): `UseInMemoryStorage()` stands alone or backfills gaps, and mixing is a per-domain
  override (last registration wins). Proven by tests.
- ✅ **AOT story documented** — `docs/AOT.md`: Core + providers + InMemory are AOT-compatible;
  `Lyntai.Storage.Sqlite`/`Postgres` opt out honestly over Dapper reflection, with the Dapper.AOT
  path noted.

### v0.6.0 — Postgres + live-provider validation (2026-07)
- ✅ **`Lyntai.Storage.Postgres`** — the third real backend (Npgsql + Dapper + FluentMigrator, pg_trgm
  memory recall incl. CJK), integration-tested against a real container via Testcontainers.
- ✅ **Live Ollama test** — the OpenAI-compatible provider verified against a real endpoint (opt-in).

### v0.7.0 — bring-your-own resources (2026-07)
IoC seams so the consuming app owns resource lifecycle, Lyntai just provides the interface:
- ✅ **`IProcessRunner`** (own CLI spawning), **BYO HttpClient** (own the client/handlers/lifecycle),
  **BYO `IDbConnectionFactory` + `migrate:false`** (own connection + schema), and **provider presets**
  (`AddOpenAiProvider`/`AddOllamaProvider`/`AddOpenRouterProvider`/`AddAzureOpenAiProvider`) alongside
  the existing BYO `ILlmProvider` path.

### v0.8.0 — in-process local inference (2026-07)
- ✅ **`Lyntai.Providers.Local`** (LLamaSharp / llama.cpp, deferred §9) — runs a local GGUF model
  in-process via `AddLocalProvider(modelPath)`; no network/key/subprocess. Ships **managed-only** so it
  isn't nailed to one runtime — the consuming app picks the `LLamaSharp.Backend.*` for its hardware; a
  missing backend degrades to a `Failed` verdict (router falls over), not a crash. Applies each model's
  own GGUF chat template. Wiring is unit-tested; real inference gated behind opt-in live tests
  (`LYNTAI_LIVE_LLAMA` + `LYNTAI_LLAMA_MODEL`), so the default run stays native-dependency-free.

## Planned

### Blocked on user-provided infrastructure
These need something only the maintainer can provision; the design admits them without breaking changes.
- **Real `PackageProjectUrl`/`RepositoryUrl`** + **SourceLink activation** — gated on the repo being
  hosted. Sources are already embedded in the PDBs via `EmbedAllSources`, so step-into debugging works
  today; SourceLink is a one-package add once there's a remote to resolve. (Docs live in the repo —
  README + `docs/` on GitHub — no separate docs site planned.)

### v1.0 — API freeze
- ✅ **Public-API baseline** — an approval test (`ApiSurfaceTests`) snapshots every packable
  assembly's public/protected surface; any add/remove/rename fails until the baseline is updated
  deliberately, so pre-1.0 breaks are visible in review and post-1.0 gate a major bump.
- ✅ **Semver policy** — stated in `CHANGELOG.md` and here: pre-1.0 minor versions may carry breaking
  changes (each called out in the changelog); 1.0 commits to SemVer 2.0.0 (no breaks without a major bump).
- Remaining before tagging 1.0: host the repo (unblocks the SourceLink/URL items above), then a docs
  pass and the SourceLink/URL wiring.

### Post-1.0 — the platform kit (design §9)
Each as a separate package on the same seams: two-gate chat orchestration · scope-guard/jail hooks ·
tool/MCP registry · durable jobs (lanes + checkpoint/resume) · security/access-gate + secret vault ·
vision/multimodal.

## Standing maintenance policies
- **MEAI churn watch**: Microsoft.Extensions.AI ships roughly monthly with breaks in
  experimental/tool-content surfaces; review release notes on each bump. The bridge references
  only `Microsoft.Extensions.AI.Abstractions` (the stable core) on purpose.
- **OTel GenAI semconv watch**: the conventions are experimental and moved to a standalone repo;
  match whatever MEAI's `OpenTelemetryChatClient` currently emits rather than pinning a version.
- **Dependency refresh**: quarterly `Directory.Packages.props` review; provider-stub keeps every
  test/e2e run at zero real tokens.
