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

## D9 — Scope: "brain + persistence core" only
Deliberately **out of scope** this cut (deferred to a future platform-kit): two-gate chat orchestration,
scope-guard/jail hooks, tool/MCP registry, durable jobs, security/access-gate, server/host/launcher,
vision, and the LLamaSharp `Local` provider. The interfaces are shaped to admit them later without
breaking changes — don't add them speculatively.
