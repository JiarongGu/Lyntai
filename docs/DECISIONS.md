# Decisions

The load-bearing choices and *why*, so a future session doesn't relitigate them or accidentally revert
an intentional one. The full contract is `2026-07-17-lyntai-design.md`; this is the rationale log.
Newest amendments noted inline.

## D1 — LLM seam is our own `ILlmProvider`, with a `Microsoft.Extensions.AI` bridge
The family is split (Gatherlight/Sonora spawn the `claude` CLI; Vidora/odysseus use API/local). A generic
library must unify all of them *and* own the fallback/verdict/streaming semantics. So the primary seam is
Lyntai's `ILlmProvider` (CLI-first, `LlmVerdict` classification, streaming-aware fallback are
first-class), and `Lyntai.Providers.ExtensionsAi` bridges any MEAI `IChatClient` in — giving the whole
MEAI ecosystem for free without shaping the public API around MEAI's types. **Prefer the bridge; only
write a native provider when MEAI can't reach the backend.**

## D2 — Storage is per-domain interfaces + one SQLite package (composite-ready)
Domain interfaces (`IKeyValueStore`/`IConversationStore`/`IMemoryStore`/`IScoreStore`/`ITraceStore`) live
in Core; `Lyntai.Storage.Sqlite` implements them. They're independent and free of cross-domain coupling
**on purpose** — so a future mastra-style composite store (route each domain to a different backend) can
be added without breaking consumers. Don't couple domains.

## D3 — Adapters depend only on Core; consumers compose via DI
Every provider/storage adapter is its own NuGet-packable package that references `Lyntai.Core` and
**never another adapter**. This is what lets a new backend/provider be a package, not a fork. The public
entry is one `AddLyntai(cfg => …)` call; adapter packages extend `LyntaiBuilder`.

## D4 — Verdict-driven fallback (design §6), amended 2026-07-17
One `LlmVerdict` enum drives router behavior via one shared `LlmVerdictClassifier` (no per-adapter
heuristics). **Amendment:** `RateLimited` was a hard circuit-break; it now cools the host immediately and
advances (a 429 is terminal for that host's window, transient for the fleet). `AuthFailed` and
`ContextWindowExceeded` were added as a finer taxonomy (auth cools+advances; too-big advances with no
host penalty). See `.claude/knowledge/llm-and-router.md` for the full table.

## D5 — Streaming: no fallback after the first token; timeout is an inactivity clock
Once a real content chunk streams, no fallback (duplicating tokens is worse than surfacing the error).
And a stream's timeout measures provider *inactivity*, not wall-clock — a single `CancelAfter` over the
whole enumeration kills healthy streams under a slow consumer. Both are easy to regress; both are
covered in `pitfalls.md`.

## D6 — `ILlmClient` front door
To a consuming app, Lyntai should look like *one* provider — candidates/fallback/cooldowns are internal.
New consumer-facing surface (structured output, etc.) hangs off `ILlmClient`, not the raw `ILlmRouter`.
`AsChatClient()` is the reverse bridge (Lyntai consumed *as* an MEAI `IChatClient`).

## D7 — `lyntai_`-prefixed SQLite objects
`UseSqliteStorage` may target a database the consumer's own app also uses. Every table/index/trigger/FTS
object (and the FluentMigrator version table) is prefixed `lyntai_` to avoid collisions. Note also that
`MatchNamesWithUnderscores` is a process-global Dapper switch — documented for consumers.

## D8 — Trim/AOT posture
Packable libraries set `IsAotCompatible=true` and carry the trim/AOT analyzers — **except**
`Lyntai.Storage.Sqlite`, which opts out honestly (Dapper/FluentMigrator materialize via reflection; an
AOT-compat claim there would be false).

## D9 — Scope: "brain + persistence core" only (SUPERSEDED by D14)
Deliberately **out of scope** this cut (deferred to a future platform-kit): two-gate chat orchestration,
scope-guard/jail hooks, tool/MCP registry, durable jobs, security/access-gate, server/host/launcher,
vision, and the LLamaSharp `Local` provider. The interfaces are shaped to admit them later without
breaking changes — don't add them speculatively. **Amendment:** the platform kit was subsequently built
out (v0.9–v0.15) and Lyntai is now in capability-expansion — see **D14**. Only the server/host/launcher
stays out of scope (it's a library, not an app).

## D10 — Design §6 fallback is the DEFAULT `RoutingPolicy`, not a hard-coded switch
As of v0.3, the §6 verdict-fallback semantics (D4) are the **default** `RoutingPolicy` (`LyntaiOptions
.Routing`), not a fixed branch. The policy captures per-verdict `FallbackAction`
(Advance / PenalizeAndAdvance / CooldownAndAdvance / Surface), same-candidate retry counts, `CooldownScope`
(Provider vs ProviderAndModel), and `ExemptSoleCandidate`. When changing routing behavior, change the
DEFAULT in `RoutingPolicy` (keep `RoutingPolicyTests.Defaults_reproduce_design_6_exactly` + the §6 doc in
sync) **or add a knob — never re-hard-code a branch in `LlmRouter`.** Consumers tune via
`ConfigureRouting(...)` / `LYNTAI_RETRY_* / LYNTAI_COOLDOWN_SCOPE`. This is the routing-layer expression of
D6 (keep new surface behind the facade/policy, not at call sites).

## D11 — Public-API is snapshot-tested; update the baseline deliberately
`tests/Lyntai.Tests/Api/ApiSurfaceTests.cs` snapshots every packable assembly's public/protected surface
against `tests/Lyntai.Tests/Api/Baselines/<Assembly>.txt`. Any add/remove/rename **fails the test** until
the baseline is updated — so API changes are deliberate (pre-1.0 breaks show in review; post-1.0 they gate
a major bump). After an INTENTIONAL change the test writes `<Assembly>.txt.actual` (gitignored); review the
diff, copy it over the baseline, and note the break in `CHANGELOG.md`. Don't blindly overwrite.

## D12 — Pre-release migration policy: fold into the owning migration; a RELEASED table needs a new one
Pre-release there are no deployed databases, so prefer a **clean consolidated schema** over an accreting
ALTER history: when a change modifies a table an existing (still-unreleased) migration created, fold it INTO
that migration's `CREATE TABLE`/index and delete the redundant ALTER — this RELAXES the standing
`dev-conventions.md` "never reuse a number" rule for the pre-release window only. **But once a migration has
shipped in a RELEASED version, it's frozen** — a schema change to a released table is a NEW numbered
migration (`ALTER TABLE …`), never a fold (adopters already applied the released one). Applied both ways:
`M…0003_JobPriority` folded into `M…0001_Jobs` (unreleased); CM1's `task`/`scope` shipped as a new
`202607220001` migration (the table shipped in 0.28). Editing a migration changes the fresh-db schema —
update the count/version-list guards (`MigrationRunnerTests`, `DeferredMigrationTests`) and keep the
SQLite + Postgres parallels of the same number in sync.

## D13 — Lyntai OWNS its storage schema; apps EXTEND, never fork ("configurable table names" rejected)
Lyntai's `lyntai_*` tables + migrations are Lyntai's, and Lyntai evolves the schema. An adopting app does
**not** point Lyntai's SQL at its own tables (that pushes schema-version management onto the app —
explicitly rejected). It adds its info, in order of preference: (1) the record `metadata` JSON fields
(`ChatThread.Metadata`, `ChatMessage.Metadata`, per-event `Metadata`); (2) the `IConversationEnricher`
DI-collection seam (`AddConversationEnricher<T>`) — a hook invoked after each write to persist into the
app's OWN store; (3) a full **BYO-impl** (register its own `IConversationStore`/… — wins via `TryAdd`) or
`migrate:false` to own the schema entirely. When a task says "let the app use its own table / configurable
table names," push back: extend via metadata/enricher/BYO, never a store fork. The conversation model is a
deliberate superset (GUID `Id`, per-thread `Seq`, `Kind`, JSON `Payload`, per-event `Metadata`) so an
existing event table already conforms.

## D14 — Direction: a LangChain-like platform kit — framework in Lyntai, domain in the app
Lyntai deliberately grows into "more like LangChain, with a lot of pre-defined tuning": **Lyntai owns the
tuned machinery** (routing/fallback, storage, LLM-ops, agentic loop/registry/orchestration, durable jobs,
guards, secrets, semantic memory, governance) and the **consuming app provides its domain tools/logic** via
DI-collection seams (`ITool`+`AddTool`, `IJobHandler`, BYO `IEmbedder`/`IVectorStore`, …). Two reuse shapes
for new features: **(1)** cross-cutting call-path concerns (cache/budget/rate-limit) are **front-door
decorators** (`LyntaiBuilder.FrontDoorDecorators`, ordered fold, cache outermost) so they compose; **(2)**
new capabilities follow "framework in Lyntai, app brings the model/backend via a seam" — interface-in-Core,
impl-via-DI, never a hard-coded branch, all behind the D6 front door. Don't ship opinionated domain tools
inside the library. (Supersedes D9's "out of scope" — the kit is built; only the server/host/launcher stays
out.)

## D15 — `StorageFeature` toggles which domains register + migrate (tag-driven selective migration)
`StorageFeature` (`[Flags]`, default `All`) lets an app enable only the storage domains it uses:
`UseSqliteStorage(path, StorageFeature.Score | …)` registers only those domains' stores AND migrates only
their tables — a disabled feature lands NO `lyntai_*` table and no store. Selective migration is
**FluentMigrator-tag driven**, and the semantics are non-obvious (cost real iterations to find): a
migration runs only when the runner's requested tags are ALL present on it (requested ⊆ migration.tags,
NOT any-match). So `All` = one pass requesting just `[StorageFeatures.AllTag]` (every migration carries
it); a subset = one pass PER selected feature (the version table dedups). Each migration is tagged
`[Tags(nameof(StorageFeature.X), StorageFeatures.AllTag)]`. Full detail in `.claude/knowledge/storage.md`.
Consistent with D13 (Lyntai still owns the tables it creates — this just skips unused domains).

## D16 — Memory retention is an app-configurable, multi-strategy policy
`IMemoryStore` size management is a `MemoryRetentionPolicy` (`LyntaiOptions.MemoryRetention`,
`ConfigureMemory(...)`, `LYNTAI_MEMORY_*`) — mirroring how **D10** makes routing configurable — not a fixed
cap. Composable knobs: a per-scope count cap + `MemoryEvictionMode` (**FIFO** sliding window vs **LRU**
working set), a default TTL, and a per-scope size (character) budget; presets name the shapes
(`CountCap`/`TimeToLive`/`SizeBudget`/`Composite`/`Manual`). The default reproduces the historical 500-entry
FIFO cap (`MemoryCapPerScope` now proxies `MaxEntriesPerScope`). Eviction is a single PURE
`MemoryEviction.Survivors` helper that all three backends share (fetch the scoped group's metadata → compute
survivors → delete the rest) — that's why they can't diverge; LRU adds a `last_accessed_at` column
(migration `202607220002`) refreshed best-effort on recall. Inspired by LangChain's buffer-window /
token-buffer / summary memories and MemGPT-style eviction. Add a new bound as a knob on the policy + a case
in the shared helper — never a per-backend branch.
