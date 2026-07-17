# Dev conventions — Lyntai library

The load-bearing patterns for working on Lyntai. These mirror the sibling projects (Gatherlight/Vidora/
Sonora — same family patterns); deviations need a reason. The design contract is
`docs/2026-07-17-lyntai-design.md`; this file is *how* to build it.

## Package structure

- **Interface in Core, impl in an adapter.** Every abstraction (`ILlmProvider`, the storage domain
  interfaces, `IScorer`, …) lives in `Lyntai.Core`. Concrete implementations live in adapter packages
  (`Lyntai.Storage.Sqlite`, `Lyntai.Providers.ClaudeCli`, `Lyntai.Providers.OpenAiCompatible`,
  `Lyntai.Providers.ExtensionsAi`, `Lyntai.Providers.Local`) that depend **only on Core** —
  **never adapter→adapter**. Consumers
  compose via DI. This is what lets a new backend/provider be a new package, not a fork.
- **Each `src/*` is NuGet-packable** (`IsPackable=true`, `PackageId`, description). `samples/` and `tests/`
  are not. Version comes from `src/Directory.Build.props` (`VersionPrefix`) — the single source.
- **DI-first.** The public entry is `services.AddLyntai(cfg => …)`. Provider/storage packages extend the
  `LyntaiBuilder` with their own `Add*`/`Use*` methods. Nothing is constructed by hand by consumers.

## Variation points = DI collections, never if/else

Pluggable sets (providers, scorers, storage domains) are resolved as `IEnumerable<T>` and iterated —
adding one is a new class + one registration, never a `switch`/`if` edit. `ILlmRouter` selects a provider
by candidate id; `IScoringService` iterates `IEnumerable<IScorer>`; `ILlmProvider`s are keyed by `Id`.

## LLM provider seam

- **Own seam is primary** (`ILlmProvider`): CLI-first, `LlmVerdict` classification (Ok/RateLimited/
  Refused/Failed/Timeout), streaming-aware fallback. The `ExtensionsAi` bridge adapts any
  `Microsoft.Extensions.AI` `IChatClient` into an `ILlmProvider` — that's how OpenAI/Azure/Ollama/etc.
  arrive, not bespoke adapters.
- **Fallback semantics** (see design §6): dedup candidates, try in order, `Failed`/`Timeout` advances,
  `RateLimited` circuit-breaks, `Refused` surfaces (no fallback); **streaming never falls back after the
  first token**; dead-host cooldown instead of exponential backoff; log every attempt.
- **CLI spawn hygiene** (`ProcessRunner`): `UseShellExecute=false`, `ArgumentList` only (prompts carry
  newlines/metacharacters — never a shell), prompt over **stdin**, **BOM-less UTF-8** both directions,
  resolved-path cache (`where.exe`/`which`, prefer `.cmd`/`.exe`), `Kill(entireProcessTree:true)` on
  cancel, per-call timeout. Tests stub the CLI via `LYNTAI_PROVIDER_CMD` (`devtools/scripts/provider-stub.mjs`).

## Storage (Lyntai.Storage.Sqlite)

- **Dapper** + hand-written SQL, `snake_case` columns ↔ PascalCase (`MatchNamesWithUnderscores=true`).
  SQLite integer-affinity trap: wrap 0..1 / double columns in `CAST(x AS REAL)` in SELECTs.
- **`IDbConnectionFactory`** opens with `PRAGMA journal_mode=WAL; busy_timeout=5000; foreign_keys=ON`.
- **FluentMigrator**, numbered `YYYYMMDDNNNN` — **never reuse a number** (unapplied duplicates are skipped
  silently). Composite PKs inline at CreateTable (SQLite has no ALTER ADD CONSTRAINT).
- **FTS5 `trigram`** external-content virtual tables kept in sync by AFTER INSERT/DELETE/UPDATE triggers,
  backfilled in the same migration (indexed CJK *substring* recall — `unicode61` treats a whole CJK phrase
  as one token). Build the MATCH string via `FtsQuery` (drop `<3`-char tokens, quote the rest), fall back
  to LIKE when it returns null, rank with `bm25()`.
- **Sources are BOM-less UTF-8 + `<CodePage>65001</CodePage>`** (in `Directory.Build.props`) — without it,
  csc on a CJK-locale machine reads CJK string literals as ANSI mojibake.

## Scorers / cortex

- Each eval dimension is an `IScorer` registered into the DI collection. Deterministic ones compute in
  code; LLM-judge ones extend `LlmScorerBase` (one-shot call through the router from a neutral context,
  `{score,reason}` verdict). Add a dimension = add a class + one registration, never a switch.

## Testing

- **xUnit.** Pure logic (router/fallback/dedup/cooldown, prompt render + placeholder guard, `FtsQuery`,
  scoring aggregation) is unit-tested with fakes — no I/O. Storage is integration-tested against a per-test
  **temp SQLite db** (created + migrated, deleted after). Providers are tested against the **provider-stub**
  (deterministic, no real tokens). `Lyntai.Playground` + the devtools e2e harness are the full-stack smoke.
- **TDD:** failing test → run it fail → minimal impl → run it pass → commit.

## Dev loop

- `node devtools/dev.mjs <build|test|e2e|playground|pack|install-hooks|check-sensitive>`.
- e2e suites live in `devtools/scripts/e2e/` as `pN.mjs` (discovered by `^p\d+\.mjs$`); the shared harness
  is `_e2e-common.mjs` (leading `_` → not discovered). Each boots the Playground against the stub over an
  isolated `devtools/_e2e-*` data folder.
- Scratch files: `devtools/_*` (gitignored). Never OS temp.
