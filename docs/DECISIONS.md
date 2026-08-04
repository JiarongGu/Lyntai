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
**One-time pre-1.0 exception (2026-07-28):** for the 1.0 cut the entire accreted 0.x ledger was collapsed
into **9 per-`StorageFeature` baselines** per backend (SQLite 16→9 byte-identical DDL; Postgres 14→9 via a
normalized-catalog equivalence gate), renumbered `202607280001..0009`. Adopters reset their `lyntai_*`
tables (disposable pre-1.0 data — the user's call). This is THE exception; **post-1.0 the append-only rule
is absolute** (no more squashes — a released baseline is frozen forever). See D22.

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
FIFO cap (`MemoryCapPerScope` now proxies `MaxEntriesPerScope`). `MemoryEviction.Survivors` is the single
PURE reference for *what* survives; *how* it's applied splits by path (both provably match `Survivors` — the
cross-backend contract tests pin the parity). Inspired by LangChain's buffer-window / token-buffer / summary
memories and MemGPT-style eviction. Add a new bound as a knob on the policy + a case in `Survivors` — never a
per-backend branch.
**Two eviction paths:**
- **Count cap (the common case) → ONE ATOMIC statement** per SQL backend: `DELETE … WHERE id NOT IN (SELECT
  … ORDER BY <live-first>, <recency> DESC, id DESC LIMIT @cap)` (`recency` = `created_at` for FIFO,
  `COALESCE(last_accessed_at, created_at)` for LRU). Race-free and without reading the scope; it reproduces
  `Survivors`' count-cap branch exactly. (SQLite/Postgres each hold a byte-identical copy — SQL stays in the
  adapters per the layering rule; the contract tests guard against drift.)
- **Size budget → the compute path** (`MemoryEviction.ApplyAsync`: fetch scoped metadata → `Survivors` →
  delete the rest), because a cumulative-length budget can't be one portable statement. Non-atomic and
  O(scope)-per-write, acceptable because scopes are cap-bounded and single-writer-per-scope is the norm.
  InMemory does the `Survivors` compute under its lock for every bounded policy.

LRU adds a `last_accessed_at` column (migration `202607220002`), refreshed best-effort ONLY on a **queried**
recall (a targeted lookup = "use"; a bare list-all / compose-all is enumeration, not use — so an app that
always composes ALL facts into the prompt should prefer FIFO). The `MemoryCapPerScope` shortcut treats
**0 as uncapped** (proxies `MaxEntriesPerScope`, ≤0 = no count cap) — a change from the pre-policy
"cap 0 = store nothing".
On-write eviction only bounds scopes you keep writing to; a COLD `(taskKey, scope)` accumulates expired
rows. So GC of cold/expired entries is an **opt-in cron job** — `AddMemoryPruneJob(cron, olderThan?)`
registers an `IJobHandler` over `PruneAsync` on the existing durable-jobs + cron machinery. Lyntai owns the
prune work; the **app owns the pump** (no self-run timer — Lyntai is a library, D9/D14).

## D17 — Wire JSON is hand-walked `JsonNode`/`JsonDocument`, not reflection `JsonSerializer` (2026-07-26)
Asked directly ("why can't we use a proper JSON converter?"): reflection-based `System.Text.Json`
serialization is **excluded by the trim/AOT posture (D8)** — the analyzers flag it, and consumers that
Native-AOT/trim their apps would break on runtime property discovery. Beyond that, most of Lyntai's JSON
has **no static type to converge on** (tool-call arguments and parameter schemas are defined by the app's
tools and the model — `JsonNode` IS the right representation there, via the shared `Lyntai.Text.JsonArgs`),
and the wire formats (claude stream-json, OpenAI-compatible dialects) are vendor-drifting, where per-field
`TryGetProperty` reads degrade gracefully instead of failing a whole deserialize on one unexpected field.
**Open option, not taken:** System.Text.Json **source generators** (`JsonSerializerContext`) are AOT-safe
and could type the STABLE response envelopes (OpenAI `choices/message/usage`, SSE delta shell) for
compile-checked field names, at the cost of an envelope-type set per dialect with `JsonElement` interiors anyway. Do
that only if envelope-parsing bugs actually materialize; don't re-litigate the reflection route.

## D18 — Findings deliberately REJECTED in the 2026-07-26 hardening pass (don't re-open without new evidence)
The whole-library review pass triaged ~80 findings into fixed / deferred (the `TASKS.md` backlog) /
**rejected**. The rejected ones, with why — so a future review pass doesn't re-litigate them:
- **Shared clock-default helper** (`clock ?? (() => DateTimeOffset.UtcNow)` in ~15 classes): one line per
  class; a shared helper is either public-surface bloat or unusable from adapters (internal). Revisit only
  as part of a real `TimeProvider` migration at a major bump.
- **AdmitAll admission "duplicate default"** (DI registration + ctor fallback): NOT redundant — the DI
  registration serves container resolution, the ctor fallback serves direct construction (tests/BYO).
- **Builder `Collect` helpers** for the ~18 two-line `Add*` methods: the per-seam XML docs are the value;
  collapsing the bodies saves nothing readers need.
- **Cache-TTL dual default** (decorator passes `options.Cache.Ttl`; `InMemoryResponseCache` re-falls-back
  to the same): harmless, env-governed redundancy; removing either side changes BYO-cache behavior.
- **ClaudeCli `id` ctor parameter**: two same-process `claude` registrations with different commands is
  exotic; additive if a consumer ever asks.
- **S8 — moving the 4 remaining Row-DTO pairs (trace/score/prompt-version/usage) to Core like `JobRow`**
  (rejected 2026-07-27, after deferral): Core internals aren't visible to the adapter packages, so the
  move means 7–8 new PUBLIC materialization types — surface bloat on the API the 1.0 sign-off just
  deliberately shrank (wire-format types went internal), for duplication the review itself rated inert
  (settable-property DTOs with zero dialect content; `JobRow` earned its move because the job stores
  share a whole SQL state machine where drift is dangerous). A mapping drift here fails the
  cross-backend contract tests immediately. Revisit only if a third relational backend materializes.

## D20 — No push/PR CI: verification and releases are MANUAL (2026-07-27)
A push/PR CI workflow (`ci.yml` running `verify` on every push) was added in the 1.0-prep batch and
**removed the same day at the owner's direction** — the project's process is fully manual:
- **Verification is manual**: `node devtools/dev.mjs verify` (build → test → e2e → leak scan) is the
  gate run BEFORE any commit is claimed complete — the standing dev-loop rule, not a server-side check.
- **Releases are manual**: the `release.yml` workflow exists but is only ever triggered by hand
  (Actions → run workflow); it is the ONLY GitHub Actions automation, and the SourceLink/
  `ContinuousIntegrationBuild` props key off it.
- Don't re-add push/PR CI as a "1.0 gap" — it was implemented, considered, and declined. Revisit only
  if the owner asks (e.g. once external contributors send PRs that need a server-side gate).

## D19 — 1.0 API sign-off decisions (2026-07-27; pre-1.0 breaks batched, unreleased)
The final public-surface sign-off pass (18 findings) closed with these calls — recorded so the surface
stays settled and the breaks don't get re-litigated at 1.0:
- **`DefaultCandidates` → `UseDefaultCandidates` is a HARD rename, no obsolete shim.** The method SETS
  (replaces) the fallback chain — "Use" is the builder family for set-semantics (`UseSqliteStorage`,
  `UseInMemoryStorage`), "Add" is for append-semantics collections. Pre-1.0 with zero external adopters,
  a shim would only preserve the misleading name.
- **`AddOpenRouterProvider` + the `OpenRouter` flavor const STAY** even though the flavor currently
  behaves identically to `OpenAi`. The preset is real consumer convenience (endpoint + key wiring); the
  pinned flavor is the seam where OpenRouter-specific behavior (ranking headers) lands later without
  re-detection. Documented as reserved, not dead.
- **Accepted as-is (cosmetic, not worth a break):** `AddMemoryPruneJob`'s argument order differs from
  sibling `AddCronSchedule` (name-first vs cron-first) — both are keyword-argument call sites in
  practice; and `IVectorStore` has no single-vector removal (upsert by id, remove by whole collection) —
  sufficient for `SemanticMemory`'s collection-per-scope model, additive post-1.0 if a BYO consumer asks.
- **Everything else from the scan shipped in the sign-off batch** (see the Unreleased CHANGELOG section):
  wire-format types internal, `TaskKey`/`ContextSize`/`*Tokens` renames, `SchemaMigration` enum replacing
  bool pairs, required `AddSecretVault` key + `AddPlaintextSecretVault`, inactivity-timeout renames +
  `maxDuration`, `ProcessResult.TimeoutKind`, `IKeyValueStore.ListKeysAsync`, `IResponseCache`
  `GetAsync`/`RemoveAsync`, `IJobQueue` read side, fully-async `IUsageTracker`.
- **These breaks are deliberately UNRELEASED.** 1.0 is adoption-gated (the user's call: more applications
  must adopt Lyntai first); this batch rides in whatever release precedes it, versioned then.

## D21 — 1.0 pre-freeze API review: applied, rejected, and two D19 reversals (2026-07-28)
A fresh adversarial public-API review across all packable assemblies (33 findings, 22 survived adversarial
verify) plus a read-only usage review of the three real consumer apps, run before the permanent 1.0 surface
freeze — the adoption gate having been met (three consumers on 0.31.1). Working record was `devtools/_review/*`.
- **Applied (fix-now)** — surface-shrink that can't be undone post-1.0, plus breaking-if-late interface
  fixes: migration classes dropped from the frozen surface (see next bullet); `McpTool` /
  `ProviderDetect.Detect(string)` / `LyntaiChatClient` → `internal`; `LocalModelOptions.AntiPrompts` →
  `StopSequences`; non-obvious-semantics XML docs; `IScoringService` honors `IScorer.Applies()`;
  `IConversationStore.AppendMessageAsync` returns the assigned per-thread `Seq`; `IVectorStore.DeleteAsync`;
  `OpenAiCompatibleOptions.Flavor` string → `OpenAiFlavor` enum. Full applied list in the 1.0 CHANGELOG.
- **Migration types off the frozen surface via SURFACE EXCLUSION, not `internal`.** The ~30 dated
  `*.Migrations.*` classes are impl detail that leaked into the API baseline. A verifier empirically proved
  that making `[Migration]` classes `internal` **breaks FluentMigrator 8.0.1 discovery** (its assembly scan
  finds exported types only) — so they STAY public and are instead excluded from `ApiSurfaceTests`
  (`ApiSurface.Render` filters the `*.Migrations` namespace). Don't re-propose the `internal` route.
- **Rejected (don't re-litigate without new evidence, cf. D18):**
  - `IScoringService.EvaluateAsync(bool persist)` → options-object reshape — the flag is a niche dry-run
    escape hatch, always called `persist:`-named and documented in XML; reshaping for one flag is over-eng.
  - Narrowing `SqliteMemoryStore` / `SqliteResponseCache` ctors off `LyntaiOptions` — the concrete SQLite
    stores stay PUBLIC (a consumer news them up for semantic-memory wiring), so their ctors stay too.
  - `ClaudeCliProvider.ProviderId` dual const/`Id`, and `McpToolset` → `McpTools` rename — cosmetic; frozen.
- **Two D19 calls reversed by real consumer contact** (D19 had accepted both as-is, "additive post-1.0 if a
  BYO consumer asks" — one did): `IVectorStore` single-item removal is now added (`DeleteAsync`), and
  `Flavor` becomes a typed enum (the old string consts become enum members; the reserved `OpenRouter` /
  `AzureOpenAi` values are preserved). These supersede the corresponding D19 "accepted as-is" notes.

## D22 — 1.0 API freeze: the surface is now frozen under SemVer 2.0 (2026-07-28)
The adoption gate stated in the ROADMAP ("1.0 tags only after applications adopt Lyntai in anger and the
surface survives that contact") is **met**: three sibling apps run on 0.31.1, and a pre-freeze adversarial
API review + read-only consumer-usage review (D21) confirmed/settled the surface. So 1.0 is cut:
- **The public API is frozen.** `ApiSurfaceTests` (D11) now gates a MAJOR bump — post-1.0, any add/remove/
  rename of public/protected surface requires a major version. Pre-1.0's "minor may break" (stated in
  `CHANGELOG.md`) ends here; **SemVer 2.0 is in force**.
- **What shipped in the freeze batch** (see the 1.0.0 CHANGELOG): the D21 surface-shrink + interface
  additions, and the D12 one-time baseline squash. The migration reset is the only consumer-visible break.
- **Verification + releases stay MANUAL** (D20) — the 1.0 tag + `release.yml` are triggered by hand.
- This is a policy line, not a code change: the value of 1.0 is the *commitment*. Don't reopen a settled
  surface item without a major-bump rationale.
- **AMENDED 2026-07-29 by D24** — while every consumer is one of the owner's own apps, a documented break
  may ship in a MINOR. The `ApiSurfaceTests` gate and the documentation duty are unchanged; only the
  version-number consequence is relaxed, and it expires the moment a third party depends on Lyntai.

## D23 — MCP tool hosting is generic; the CLI dialect lives in the provider package (2026-07-29)
`Lyntai.Providers.ClaudeCli.Mcp` was named for one consumer but was ~85% provider-neutral machinery — its
csproj referenced only `Lyntai.Core`, so there was never any code coupling to the claude adapter at all.
It is now split along the seam that actually exists:
- **`Lyntai.Tools.Mcp.Hosting`** (new package) owns everything neutral: the ephemeral loopback Kestrel MCP
  server, the `ITool` → `AIFunction` bridge, bearer-token minting, owner-only temp-file writing, teardown
  ordering, and the no-tools-registered short-circuit. It is the OUTBOUND twin of `Lyntai.Tools.Mcp`
  (inbound: an MCP server's tools → `ITool`), and it is named for the concept, not a consumer.
- **`IMcpCliDialect` + `McpEndpoint` + `McpCliContext` live in CORE**, not in the host package. That
  placement is load-bearing: it lets a provider package ship its own dialect **without** referencing the
  host. An `IMcpCliDialect` is an INTERFACE, not a format string, because the variation across CLIs is
  structural — flag names differ and the config file is JSON for some CLIs, TOML for others.
- **`ClaudeCliMcpDialect` lives in `Lyntai.Providers.ClaudeCli`** — knowledge about `claude` belongs with
  the claude provider, and it costs that package **no new dependencies** (JSON + strings over Core types).
- **What must NOT happen: `Lyntai.Providers.ClaudeCli` must never reference the hosting package.** Doing so
  would drag `Microsoft.AspNetCore.App` + `ModelContextProtocol.AspNetCore` into every app that uses the
  plain CLI provider. Keeping host/transport dependencies out of the base provider is the entire reason
  `ICliToolProvisioner` exists as a seam — so only the *dialect* moved into the provider package, never the
  host. This is why the split isn't simply "fold the add-on into the provider".
- **`Lyntai.Providers.ClaudeCli.Mcp` is DELETED, not kept as a shim.** It briefly survived as a
  composition package (`AddClaudeCliMcpTools()` → `AddMcpToolHost(new ClaudeCliMcpDialect())`) and was
  removed the same day: a whole NuGet package whose only value was saving the caller `new
  ClaudeCliMcpDialect()` is not worth its own id, versioning surface and doc footprint. Apps compose the
  two halves themselves, which is the normal DI story. **Consequence worth keeping:** D3's "never
  adapter→adapter" now has ZERO exceptions in the tree again — don't reintroduce a glue package.
- **The provisioner is resolved KEYED by `IMcpCliDialect.ProviderId`**, with the first registration also
  taking the unkeyed slot as a fallback. The old unkeyed-only `TryAddSingleton` meant two CLI providers
  that each run their own agent loop would collide — first registration won and the wrong dialect was
  injected into both. That was a real defect, not a naming issue.
- **Cost: a MINOR bump, by the D24 amendment.** The relocation itself was additive (all the moved
  machinery was `internal`), but deleting the shim removes `AddClaudeCliMcpTools` and a published package
  id — a break under a strict reading of D22. See **D24** for why that is allowed right now.

## D24 — SemVer strictness is deferred while every consumer is first-party (2026-07-29)
D22 froze the public surface under SemVer 2.0 at 1.0. **Amendment, at the owner's direction:** while
**every** Lyntai consumer is one of the owner's own applications, a breaking surface change may ship in a
MINOR release, provided it is documented. The commitment D22 encodes is to *external* adopters, and there
are none yet; paying a major bump to protect callers you control yourself buys nothing and inflates the
version number past any useful meaning.

What this does and does not license:
- **Does:** removing/renaming public surface in a minor, when the change is a genuine simplification and
  the CHANGELOG entry says plainly what broke and what replaces it. The first application of this is the
  D23 shim deletion.
- **Does NOT:** skipping the documentation. The `ApiSurfaceTests` baselines still gate every surface
  change, breaks are still called out under a **Breaking** heading in `CHANGELOG.md`, and the rationale
  still lands in this file. The gate is unchanged; only the *version-number consequence* is relaxed.
- **Does NOT:** silent behavior changes, storage/migration breaks, or anything a consumer can't detect at
  compile time. Those stay major-bump material regardless — a compile error is recoverable, a corrupted
  database is not.

**Reinstate strict D22 SemVer the moment a third party depends on Lyntai** — a public NuGet consumer
outside the owner's apps, or any external contributor. At that point this amendment expires and the next
breaking change takes a major bump. Anyone reading D22 and finding a 1.x release with a `Breaking`
section should land here, not assume the policy was violated.

## D25 — the version and the CHANGELOG heading are authored by the RELEASE PIPELINE only (2026-08-04)
`<VersionPrefix>` in `src/Directory.Build.props` is the single version source, and `release.yml` bumps it
**from its current value** when no explicit version input is given. That makes the file's current value a
*baseline*, not a note — so a session that bumps it by hand ("ready for the next release", which looks
helpful) silently moves the baseline and the next release publishes the version AFTER the intended one.

**This is measured, not theoretical:** in a sibling repo a hand-edited `0.1.2 → 0.2.0` published `0.3.0`,
and `0.2.0` went from unreleased to *skipped* without anyone deciding to skip it. The same slip on a
post-1.0 repo lands on a MAJOR. The second half of the same failure: the workflow **stamps** the
CHANGELOG's `## Unreleased` heading with the version + date, so a commit that stamps or deletes that
heading by hand leaves nothing to stamp and the release ships with the wrong section title.

**Why nothing caught it:** `doctor` verified the version was *consistent* across props/README — and a
hand-bump keeps them consistent. Consistency was never the property at risk; **authorship** was.

Two layers guard it, both sabotage-verified here:
- **State** — `node devtools/dev.mjs doctor` compares `VersionPrefix` to the newest `v*` tag. Between
  releases they are equal by construction, so any difference means a hand-edit — and being state-based it
  also catches a bad merge/rebase. Silent when there are no tags (a shallow CI checkout or fresh clone).
  Deliberately NOT in `verify`/`pack`: during a real release the version is *supposed* to be ahead of the
  newest tag.
- **Act** — `devtools/scripts/check-version-bump.mjs` (pre-commit) blocks a staged change to
  `<VersionPrefix>` or a removal of the `## Unreleased` heading. A newly ADDED props file has no removal
  line, so seeding a repo is never blocked.

`LYNTAI_RELEASE=1` is the escape hatch for both — set by the workflow's commit step, and by a human
deliberately repairing a botched release (where the version being written is a considered choice).
**Write release notes under `## Unreleased` and leave the heading alone; cut releases from the Actions tab.**

## D26 — Lyntai DRIVES a backend's own self-maintenance (probe · update · pinned install · auth); it never OWNS credentials or binaries (2026-08-04)
The line for "what a provider capability may do to its own backend" kept getting drawn ad hoc, so here it
is once. Lyntai may **drive tooling the backend already ships**, without spending a turn:
`IProviderInstallation` (what is it), `IProviderUpdater` (update itself), `IProviderVersionInstaller`
(install a NAMED version of itself), `IProviderAuth` (is it signed in, and drive sign-in/out). Each is an
OPTIONAL interface discovered by pattern-matching, never a member on `ILlmProvider` — a backend that can't
answer simply doesn't implement it.

**The pinning question, settled** (it read as a contradiction of "Lyntai never provisions or pins a
binary"): that rule is about **fetching a binary from nowhere**, where a host owns its own download,
storage and trust policy. Driving an already-present backend's own installer is the *update path with an
argument* — `claude install 2.1.220` is the same class of act as `claude update` — and pinning a known-good
version is exactly what a host needs that `update` cannot express. So it is IN, reporting the same
`ProviderUpdateResult` (where `Updated` also covers a deliberate downgrade).

Still OUT, and not by accident:
- **Credential storage.** The backend owns its credentials; `IProviderAuth` asks and drives, never stores.
- **Binary provisioning.** Downloading/placing a backend that isn't there stays the host's concern.
- **Guessing.** A capability reports what the backend SAID (`Detail` verbatim) or nothing. Unreadable
  output is "unknown", never an invented signed-in state or version.
- **Forwarding a free-form value as a flag.** `Mode`/`Version` are free-form so other backends fit the
  contract; an adapter that doesn't recognize one must REFUSE rather than synthesize `--<whatever>`, and a
  flag-shaped `Email`/`Version` is refused without spawning.

**Corollary for the CLI:** every maintenance question must be **flag-shaped or a documented subcommand** —
the claude CLI treats an unrecognized token as a PROMPT and spends a turn answering it. `auth status` is
sent with an explicit `--json` (its default) precisely so an older build rejects an unknown *flag* instead
of billing a turn for the sentence "auth status".

## D27 — a CLI-backed provider is `CliProviderEngine` + a DIALECT, never a second copy of the rules (2026-08-04)
Driving a command-line agent involves a dozen invariants that have nothing to do with *which* CLI it is: no
shell, `ArgumentList` only, a neutral working directory, prompt over stdin (or as a trailing argument),
timeouts as a per-chunk **inactivity** clock with an absolute backstop, verdicts through
`LlmVerdictClassifier`, empty output as `Failed`, exactly one terminal stream chunk, and probe → run →
re-probe for self-maintenance. Every one of those has been gotten wrong at least once in this repo's
history (`pitfalls.md` is largely a list of them), and the reason a fix didn't stick was that the logic
existed per provider.

**So: those invariants live once, in `CliProviderEngine` (Core, `Lyntai.Llm.Cli`), and a CLI backend
contributes only its VOCABULARY** through an `ICliProviderDialect` — command name + env vars, completion
argv, prompt delivery, line parsing, and the maintenance commands it has. A provider package is then a
dialect plus a forwarding `ILlmProvider` that declares which optional capability interfaces the backend
supports. This is the same split D23 chose for MCP tool-hosting (generic host in Core, per-CLI flags in an
`IMcpCliDialect`), applied to the provider itself.

Two properties of the split are deliberate, not incidental:
- **`CliProviderDialectBase` claims NOTHING optional by default** — no updater, no pinned install, no auth,
  and its only default maintenance command is the flag `--version`. A capability appears only when a dialect
  names the command for it, so a backend is never credited with something nobody measured. (For a CLI that
  answers unrecognized tokens as prompts, a guessed subcommand costs tokens on every call while the build
  stays green — D26's corollary.)
- **The engine is composed, not inherited.** `ClaudeCliProvider` keeps its name, its public surface and its
  ~90 tests unchanged, and holds an engine; it does not derive from a base provider. A second CLI package
  therefore adds nothing to Core and breaks nothing existing.

Consequence for anyone adding a backend: **if it is a spawned CLI, write a dialect** (`.claude/skills/
add-provider` → the CLI checklist). Reaching for a fresh `ILlmProvider` for a CLI is the thing this
decision exists to prevent.

**Validated by a second implementer, immediately.** `Lyntai.Providers.CodexCli` was built on the seam the
same day, and the differences it surfaced are why one implementer is never enough: codex takes its prompt on
stdin but needs `--skip-git-repo-check` (it refuses to run outside a git repo, and the engine's cwd is a
neutral temp dir); its auth readout is **prose**, with no `--json` at all; its logout is a TOP-LEVEL command
(`codex logout`, not `auth logout`); it cannot pin a version, so it doesn't implement
`IProviderVersionInstaller`; and — the one that changed the interface — it reports a failed turn **in band and
exits 0**, which the dialect vocabulary had no way to express. That became
`CliOutputEventKind.Failure`, whose message is classified (a 401 → `AuthFailed` cools the host) instead of
being flattened into "no output produced". A seam validated by one CLI is that CLI's shape wearing a generic
name.

## D28 — a CLI backend may be PORTABLE (app-bundled), not just a global install (2026-08-04)
A host may ship, unpack or side-load its own copy of a CLI rather than depend on a machine-wide install
(offline/air-gapped deployment, a pinned known-good build, a desktop app that shouldn't require the user to
`npm i -g` anything). Lyntai supports that as a first-class wiring, not a workaround:

- **The path is a parameter, not an environment variable.** `AddClaudeCliProvider(command, environment)` /
  `AddClaudeCliAgentSession(command)` / `AddCodexCliProvider(command, environment, dialect)` take it, so a host
  reads it from its own configuration. The env seams (`LYNTAI_PROVIDER_CMD`, `CLAUDE_CMD`, `CODEX_CMD`) remain
  for tests/e2e and ad-hoc overrides — they were never a good place for an app's own deployment layout.
- **Per-spawn environment comes with it.** A bundled CLI usually needs its own home/config dir (`CODEX_HOME`,
  `CLAUDE_CONFIG_DIR`) so it neither reads nor mutates the machine-wide install's credentials and settings. The
  engine applies it to the MAINTENANCE spawns too — otherwise a probe or auth check would report the global
  install's state while completions used the portable one, which is worse than not supporting portability.
- **Presence is verified, not assumed.** `IsAvailable` checks that an explicitly-supplied command actually
  exists (`ProcessRunner.CommandExists`, which also accepts an extensionless launcher with a spawnable
  sibling — the CLI2 shim shape). Previously any explicit command was trusted; for a portable install that
  turns "this candidate isn't deployed" into a failed turn instead of a skipped candidate. A BYO
  `IProcessRunner` is still trusted optimistically, because it resolves commands in its own environment.

**Still NOT in scope:** downloading, unpacking or updating a portable copy. That is provisioning, and it stays
the host's concern (D26) — Lyntai points at what the host deployed and reports honestly whether it's there.

## D30 — generation is a PLATFORM in its own domain, coupled to the LLM side only through tools (2026-08-04)
`Lyntai.Generation` is a separate domain package with its own contracts and its own `GenerationVerdict`, not an extension
of the LLM stack. At the owner's direction: **"our goal is not to make a generation engine, it is to make a
media generation platform"** — so the value is contracts, capability-aware routing, delivery-mode handling and
lifecycle, with every pixel and sample produced by a backend the host chooses.

**Named `Generation`, not `Media`** (owner's call, decided while the core was still uncommitted-to-consumers):
the workflow is "generate an artifact of kind X", and `Kind` is an open string — so a kind that isn't media
(a document, a dataset, something bespoke) fits the same submit/poll/stream, capability and routing machinery
without the package name lying about it. No `Custom` constant is needed or wanted: an open string already
accepts any value, and a well-known `"custom"` would mean nothing to a backend that matches on the kinds it
serves.

Three researched findings forced the shape, and a future session must not "simplify" them away:

- **Three delivery modes, not one.** Image is inline (request → bytes); video is **universally** an async job
  (submit → poll/webhook → fetch — WAN documents 1–5 minute renders as create-task-then-poll; Kling is
  `POST /v1/videos/generations` then `GET /v1/tasks/{id}`); audio splits (TTS streams, playback starting before
  generation ends; music/dubbing are batch jobs). One `GenerateAsync` can only express image — so
  `IGenerationJobProvider` and `IGenerationStreamProvider` are separate optional capabilities, and the **operation id is
  exposed** so a render survives a process restart, composes with `Lyntai.Jobs`, and works with a
  webhook-delivering backend.
- **A backend is not a model.** Aggregators serve 1,000+ models across image/video/audio/**3D** behind one
  queue endpoint, and the same model (WAN) is reachable through several backends. Routing selects
  backend **+** model (`GenerationCandidate`), or every aggregator becomes N fake providers.
- **Capability declaration is load-bearing**, unlike LLM routing. Chat models all take text; media backends
  differ by medium, delivery, input role (text→video vs first-frame→video vs reference→video), duration
  ceiling and model catalogue. The router **pre-filters** on `GenerationCapabilities.Supports` before spending
  anything, and "nothing here can do that" is `Unsupported` — a configuration answer, not a runtime fault.

**Media CHAINS.** `3d → image → video` is a first-class use case, so `GenerationArtifact.ToInput(role)` carries
bytes-or-URI into the next stage and `Kind` is an open string (3D already ships on real aggregators). The
pipeline RUNNER is deferred until ≥2 real backends exist, but nothing in the core may make it impossible.

**Fallback is a POLICY, not a law.** `GenerationRoutingPolicy` makes the per-verdict action configurable
(defaults reproduce §6: `Refused` surfaces, everything else advances) because one real setup breaks the
default: a host that deliberately lists a **hosted** backend and a **locally-run** one, where the hosted one
refuses content the local one has no policy against. `On(Refused, Advance)` is then correct, and it is the
HOST's decision — same reasoning and shape as D10 on the LLM side. The three backend shapes this implies
(local / spawned-CLI / remote) need no contract support: they are implementation shapes the seam already
spans, and locality preference is expressed through candidate ORDER, exactly as it is for LLM providers.

**Coupling, at the owner's direction:** the LLM stack gains **ZERO** dependency on media. The bridge is
`ITool` (and therefore MCP, via `Lyntai.Tools.Mcp.Hosting`), so an agent can generate media without either
domain referencing the other's concrete types. Where media wants an LLM (prompt rewriting), it takes
`ILlmClient` — handed in, never owned. Failure PATTERNS are still shared: `GenerationVerdictClassifier` delegates to
`LlmVerdictClassifier` so there is one corpus of "what does a 429 mean", per D27's rule against a second copy
of the rules.

**Not a generation engine (extends D26):** no inference, no ffmpeg pipeline authoring, no engine/weights
provisioning, **no webhook hosting** (Lyntai is a library — the app owns its endpoint and calls `FetchAsync`),
no artifact storage, no credential storage. A backend that cannot be checked without generating reports
"unavailable" rather than billing a probe — replacing the generate-and-discard test that pattern otherwise
requires.

**Packaging:** stays in the SHARED release pipeline while it is cheap — measured at 48 KB / 9 s, the same
class as `Lyntai.Providers.ClaudeCli` (50 KB), and every planned backend is thin (HTTP clients, a subprocess
shell-out). The split trigger is a **dependency**, not the domain: the first backend that needs a native
runtime (ONNX/TensorRT/CUDA) or ships model files gets its own release action, so its build time and failure
surface never bleed into an LLM release.

Plan of record: `docs/2026-08-04-generation-platform-plan.md` (Plan 1 = this core; Plans 2–7 = HTTP backends, local
subprocess, async video + Jobs, governance parity, tool bridge + audio, pipelines). Backend order set by the
owner: **TTS before music** for audio; video backend choice (WAN direct vs. an aggregator) still open.

## D29 — the next major release is 2.0.1; 2.0.0 is BURNED on nuget.org (2026-08-04)
**2.0.0 was published and then unlisted** for 10 of the 12 package ids (all except
`Lyntai.Providers.CodexCli` and `Lyntai.Tools.Mcp.Hosting`, which have no 2.x at all). Unlisting hides a
version from search and resolution but **never frees its number** — nuget.org will not accept that
id+version again, ever.

**Why this matters more than a cosmetic gap:** the release workflow pushes with
`dotnet nuget push --skip-duplicate`, so cutting a 2.0.0 would **succeed loudly and publish nothing** for
those 10 packages, while the 2 without a 2.0.0 *would* publish — a partial release that reports green and
leaves the feed at 1.2.x for most of the library. That is the same silent-skip family as D25: the pipeline's
"success" doesn't mean what it looks like.

**The decision (owner's, 2026-08-04):** when the next breaking/major batch lands, go straight to **2.0.1**.
Verified the same day: 2.0.1 is free on **all 12** ids. Skipping 2.0.0 is not a SemVer violation — SemVer
requires that versions ORDER correctly, not that they be contiguous — and because all packages version in
lockstep from `VersionPrefix`, the skip applies uniformly with no per-package divergence.

**How to apply it:** run the release workflow with an explicit `version: 2.0.1` and `bump: none`. Do NOT
reach 2.0.1 by hand-editing `<VersionPrefix>` (D25 — the pipeline authors the version), and do not rely on a
bump input, which would produce 1.2.x or 2.0.0 depending on the baseline.

**Generalization worth remembering:** before publishing any version you have reason to think was used before
(a botched release, an unlisted push, a renamed package), check the feed *including unlisted* —
`https://api.nuget.org/v3-flatcontainer/<id-lowercase>/index.json` lists every version that exists, listed or
not. `--skip-duplicate` is the right default for re-runnability, and precisely because of that it cannot tell
you that a publish was skipped.
