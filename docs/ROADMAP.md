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

## Planned

### v0.4 — LLM-ops depth
- **Prompt registry versioning**: history + rollback for `lyntai.prompt.*` overrides (audit who
  changed what; A/B two versions by consumer tag).
- **Judge calibration helpers**: pairwise comparison mode, rubric prompts, agreement metrics
  between judge models (research found no settled practice — design carefully, small surface).
- **Memory lifecycle**: summarization/compaction of old entries, per-entry TTL/decay, dedup on
  remember.
- **Telemetry alignment pass**: track the OTel GenAI semantic-conventions repo as it stabilizes
  (span events, `gen_ai.usage.cached_input_tokens` if/when standardized) and MEAI's convention
  version; add trace-to-`ITraceStore` bridging so spans and run traces cross-reference.

### v0.5 — ecosystem & backends
- **Composite store router** (the mastra pattern the interfaces were shaped for): route each
  storage domain to a different backend package without breaking consumers.
- **`Lyntai.Storage.Postgres`**: second storage backend proving the domain-interface seam.
- **Full AOT story**: evaluate Dapper.AOT for `Lyntai.Storage.Sqlite` (async +
  `MatchNamesWithUnderscores` + FluentMigrator compatibility) or document the non-AOT status.
- **`Lyntai.Providers.Local`** (LLamaSharp, in-process) — deferred §9 item.

### v1.0 — API freeze
- Semver commitment: no breaking changes without a major bump.
- Public-API baseline tests (Microsoft.CodeAnalysis.PublicApiAnalyzers) so breaks are deliberate.
- Docs site (API reference from the shipped XML docs + how-to guides).
- Real `PackageProjectUrl`/`RepositoryUrl` + SourceLink once the repo is hosted.

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
