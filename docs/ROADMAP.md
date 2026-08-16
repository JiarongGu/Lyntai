# Lyntai — Roadmap

> The design contract is `2026-07-17-lyntai-design.md`; §9 lists what was deliberately deferred.
> This file sequences how the deferred and newly-identified work lands. Dates are intentions,
> not promises. **From 1.0 the public API is frozen under SemVer 2.0** — no break without a major bump,
> gated by `ApiSurfaceTests` — **amended by D18 while every consumer is first-party**: a *documented* break
> may ship in a MINOR, but the `ApiSurfaceTests` gate is unchanged and strict D16 resumes the moment a third
> party depends on Lyntai. See `CHANGELOG.md` and `DECISIONS.md` **D16**/**D18**. One carve-out: the
> **`Lyntai.Generation` PACKAGE** (the media backends) ships EXPERIMENTAL until GEN-VERIFY closes — two
> backends were written from vendor docs with no key to call, and nothing implements the stream seam. **The
> carve-out is the PACKAGE, not the `Lyntai.Generation` NAMESPACE:** the generation CONTRACTS live in that
> namespace inside `Lyntai.Core`, which is mandatory for every consumer and carries the FULL promise — D36
> applied the full promise to one deliberately rather than claiming the exemption. Released detail lives in
> `CHANGELOG.md`; the reasoning in `DECISIONS.md`.

## Shipped

One line per release. **The detail is `CHANGELOG.md`** (per-release, breaking changes called out) and the
reasoning is `docs/DECISIONS.md` — this list exists so the sequence is scannable, not to repeat them. A
per-version design record, where one exists, is indexed in `docs/superpowers/INDEX.md`.

| Version | What it added |
|---|---|
| v0.1–v0.30 | **the pre-1.0 line, collapsed to one row on 2026-08-15.** The substrate and fallback router; three storage backends; the §9 platform kit (in-process local inference, the agentic tool loop, native tool-calling across HTTP/MEAI/CLI, MCP, durable jobs with priorities/cron/cancellation, guards); observability, response caching, usage budgeting, rate limiting; semantic + curated memory; recoverable secrets and app-owned storage. **Every 0.x version is unlisted on nuget.org (D44)**, so none is resolvable by a consumer and none carries the SemVer promise, which begins at 1.0 — thirty scannable rows for a line nobody can install was the opposite of what this table is for. Per-release detail is `CHANGELOG.md`. |
| **v1.0.0** (2026-07-28) | **the API freeze** — SemVer 2.0 from here, gated by `ApiSurfaceTests` (D16, amended by D18) |
| v1.1–v1.2.2 | CLI tool-hosting generalized; turn-free backend probe/auth + pinned self-install |
| v2.0.1 (2026-08-04) | the generation platform + a coherent package graph (D24–D25; 2.0.0 is burned — D23) |
| v2.1.0 (2026-08-04) | the generation backends registerable in one line each, and named factories where a constructor could be silently transposed (D28; `task-archive.md` Part 36) |
| v2.2.0 (2026-08-05) | the provider-lifetime seam (D30) and a second agent-session backend (D35) |
| v2.3.0 (2026-08-05) | the pre-release whole-library review — shipped separately only because 2.2.0 was cut from the pushed branch without it (D18, D37; the push-before-release lesson is in pitfalls.md) |
| v2.4.0 (2026-08-05) | app-owned MCP servers on either CLI agent session (D38) |
| **v2.5.0** (2026-08-08) | **long-term memory** — named engines, decay measured in interference, burial rather than deletion (D39–D41) |
| **v3.0.0** (pending) | **the memory retention model** — seven `IMemory*Policy` domains, FSRS as the only shipped curve, RRF the ranking default, a recall that no longer lengthens a half-life, an authoritative fact that takes a slot within the limit, six pre-release migrations folded into one (D45–D66) |

## Planned

### The platform kit (design §9) — SHIPPED pre-1.0, final deferrals closed before the freeze
Delivered additively on the existing seams: `Lyntai.Providers.Local` · the agentic tool loop + native
tool-calling (HTTP/MEAI/CLI) + MCP-client tool source · durable jobs · guards · two-gate chat orchestration
· secret vault · vision/multimodal. The job deferrals subsequently shipped too — priorities + dead-letter
queue, recurring scheduling, cron expressions, running-job cancellation. Still open, each deliberately:
- **Server/host/launcher + auto-update** — permanently out of scope (an application concern; Lyntai is
  host-free — the one standing §9 exclusion).
- **Cross-process GLOBAL concurrency limits** — the last durable-jobs deferral (needs a distributed counter; the
  per-process cap + atomic claim cover most needs).
- **Streaming tool-calls** (the `LlmChunk` contract carries no tool-call payload) and native tool-calling
  for the ClaudeCli/Local providers (both stay on the prompt fallback) — low value, revisit on demand.

### Next — generation, to close the experimental carve-out
In priority order, each needing its own measurement:
1. **GEN-VERIFY** — run `sd-cli` and fal.ai for real, confirm the argv/clamp and the wire format, then drop the
   remaining "documented, not measured" notes. This is what lets `Lyntai.Generation` lose the EXPERIMENTAL
   label. (One of the three ported `sd-cli` details — the binary-directory working dir — was confirmed against
   a real release by a consuming app in 2026-08; argv and the size clamp are what's left.)
2. **Streaming TTS** — the one contract in the platform no real backend exercises
   (`IGenerationStreamProvider`). TTS before music. Needs a vendor pick and a measured wire format.
3. **Pipelines** (3d → image → video) — ordered stages feeding `artifact.ToInput(role)` forward. The original
   "defer until ≥2 real backends exist" test now reads as satisfied and is the wrong one: counted by KIND,
   image has five backends and video two, but **3d has ZERO** — so the pipeline's FIRST stage has no backend
   at all. A 3D-backend survey is the critical path (mesh vs turntable stills — only the latter chains into
   today's video backends), and it decides whether a 3D stage can feed the rest before the runner's shape can
   be designed at all.

_Generation wiring helpers (`AddOpenAiImageProvider()` and friends) were item 4 here and **shipped in 2.1.0**
— see `docs/task-archive.md` Part 36. Every remaining item above needs a real
service or a vendor key, which is why none of them is codeable from the repository alone._

One more item of the same kind — a real run, not a design call — sits outside generation:
- **CLI backends: measure the codex agent-session surface** (`TASKS.md` Part 41, CLI12). The capture
  behind `CodexAgentSession` ran a turn with no tools in it, so the tool-step half of the mapping is INFERRED
  and the reader routes unrecognised item names to the tool arm by elimination — a wrong name is therefore a
  *fabricated* `ToolCall` carrying the model's own reasoning as its arguments, not a missing event
  (`DECISIONS.md` **D35**). No payload is invented or dropped, and the measured half (session id / terminal /
  usage) is unaffected; the KIND of event is what a real run has to confirm.

_This section previously listed three "design calls still open" — blameless-vs-reportable, the curated-memory
`taskKey`/`scope` move, and renaming `OpenAiCompatibleOptions.ContextSize`. **All three were settled and
shipped in 2.3.0** (`docs/DECISIONS.md` D37 for the blameless half; the rename landed as `OllamaContextSize`),
and the section simply outlived them. Removed 2026-08-12 — `docs/task-archive.md` Part 43/44 carries each
one's outcome. The `major-bump-or-never` framing on the third was also wrong on its own terms, as its archive
entry records._

## Standing maintenance policies
- **MEAI churn watch**: Microsoft.Extensions.AI ships roughly monthly with breaks in
  experimental/tool-content surfaces; review release notes on each bump. The bridge references
  only `Microsoft.Extensions.AI.Abstractions` (the stable core) on purpose.
- **OTel GenAI semconv watch**: the conventions are experimental and moved to a standalone repo;
  match whatever MEAI's `OpenTelemetryChatClient` currently emits rather than pinning a version.
- **Dependency refresh**: quarterly `Directory.Packages.props` review; provider-stub keeps every
  test/e2e run at zero real tokens.
