# Lyntai — Roadmap

> The design contract is `2026-07-17-lyntai-design.md`; §9 lists what was deliberately deferred.
> This file sequences how the deferred and newly-identified work lands. Dates are intentions,
> not promises. **From 1.0 the public API is frozen under SemVer 2.0** — no break without a major bump,
> gated by `ApiSurfaceTests` — **amended by D24 while every consumer is first-party**: a *documented* break
> may ship in a MINOR, but the `ApiSurfaceTests` gate is unchanged and strict D22 resumes the moment a third
> party depends on Lyntai. See `CHANGELOG.md` and `DECISIONS.md` **D22**/**D24**. One carve-out: the
> **`Lyntai.Generation` PACKAGE** (the media backends) ships EXPERIMENTAL until GEN-VERIFY closes — two
> backends were written from vendor docs with no key to call, and nothing implements the stream seam. **The
> carve-out is the PACKAGE, not the `Lyntai.Generation` NAMESPACE:** the generation CONTRACTS live in that
> namespace inside `Lyntai.Core`, which is mandatory for every consumer and carries the FULL promise — D43
> applied the full promise to one deliberately rather than claiming the exemption. One known hole in the
> gate: it renders a generic method without its type parameters, so some overloads are indistinguishable to
> it (`TASKS.md` Part 42). Released detail lives in `CHANGELOG.md`; the reasoning in `DECISIONS.md`.

## Shipped

One line per release. **The detail is `CHANGELOG.md`** (per-release, breaking changes called out) and the
reasoning is `docs/DECISIONS.md` — this list exists so the sequence is scannable, not to repeat them. A
per-version design record, where one exists, is indexed in `docs/superpowers/INDEX.md`.

| Version | What it added |
|---|---|
| v0.1.0 | the substrate — core abstractions, fallback router, SQLite storage |
| v0.2.0 | production hardening |
| v0.3.0 | routing & resilience depth |
| v0.4.0 | LLM-ops depth |
| v0.5.0 | ecosystem & backends |
| v0.6.0 | Postgres + live-provider validation |
| v0.7.0 | bring-your-own resources |
| v0.8.0 | in-process local inference (`Lyntai.Providers.Local`) |
| v0.9.0 | agentic tool-calling — the first platform-kit cut |
| v0.10.0 | native tool-calling |
| v0.11.0 | native tool-calling through the MEAI bridge |
| v0.12.0 | MCP tool source |
| v0.13.0 | proper tool-calling for the claude CLI |
| v0.14.0 | durable jobs |
| v0.15.0 | the rest of the platform kit |
| v0.16.0 | agentic observability |
| v0.17.0 | response caching |
| v0.18.0 | usage budgeting |
| v0.19.0 | semantic memory |
| v0.20.0 | semantic memory wired into the chat path |
| v0.21.0 | client-side rate limiting |
| v0.22.0 | persistent SQLite backends for the new seams |
| v0.23.0 | Postgres backends for the new seams, with pgvector |
| v0.24.0 | durable-job priorities + dead-letter queue |
| v0.25.0 | recurring job scheduling |
| v0.26.0 | cron expressions |
| v0.27.0 | running-job cancellation |
| v0.28.x | recoverable secrets, job admission, curated memory |
| v0.29.x | app-owned storage adoption |
| v0.30.0 | consumer ergonomics + foundation hardening |
| **v1.0.0** (2026-07-28) | **the API freeze** — SemVer 2.0 from here, gated by `ApiSurfaceTests` (D22, amended by D24) |
| v1.1–v1.2.2 | CLI tool-hosting generalized; turn-free backend probe/auth + pinned self-install |
| v2.0.1 (2026-08-04) | the generation platform + a coherent package graph (D30–D34; 2.0.0 is burned — D29) |
| v2.1.0 (2026-08-04) | the generation backends registerable in one line each (D35/D36) |
| v2.2.0 (2026-08-05) | the provider-lifetime seam (D37) and a second agent-session backend (D42) |
| v2.3.0 (2026-08-05) | the pre-release whole-library review — shipped separately only because 2.2.0 was cut from the pushed branch without it (D44–D46) |
| v2.4.0 (2026-08-05) | app-owned MCP servers on either CLI agent session (D47) |
| **v2.5.0** (2026-08-08) | **long-term memory** — named engines, decay measured in interference, burial rather than deletion (D48–D51) |

## Planned

### The platform kit (design §9) — SHIPPED (v0.8–v0.15, deferrals closed through v0.27)
Delivered additively on the existing seams: `Lyntai.Providers.Local` · the agentic tool loop + native
tool-calling (HTTP/MEAI/CLI) + MCP-client tool source · durable jobs · guards · two-gate chat orchestration
· secret vault · vision/multimodal. The v0.14 job deferrals subsequently shipped too (priorities + DLQ
v0.24, scheduling v0.25, cron v0.26, running-job cancellation v0.27). Still open, each deliberately:
- **Server/host/launcher + auto-update** — permanently out of scope (an application concern; Lyntai is
  host-free — the one standing §9 exclusion).
- **Cross-process GLOBAL concurrency limits** — the last v0.14 deferral (needs a distributed counter; the
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
3. **Pipelines** (3d → image → video) — ordered stages feeding `artifact.ToInput(role)` forward. Deferred until
   ≥2 real backends exist so the runner isn't designed on guesses; needs a 3D-backend survey first (mesh vs
   turntable stills — only the latter chains into today's video backends).

_Generation wiring helpers (`AddOpenAiImageProvider()` and friends) were item 4 here and **shipped in 2.1.0**
— see `docs/task-archive.md` Part 36 and `DECISIONS.md` D35/D36. Every remaining item above needs a real
service or a vendor key, which is why none of them is codeable from the repository alone._

One more item of the same kind — a real run, not a design call — sits outside generation:
- **CLI backends: measure the codex agent-session surface** (`TASKS.md` Part 41, CLI12/CLI13). The capture
  behind `CodexAgentSession` ran a turn with no tools in it, so the tool-step half of the mapping is INFERRED
  and the reader routes unrecognised item names to the tool arm by elimination — a wrong name is therefore a
  *fabricated* `ToolCall` carrying the model's own reasoning as its arguments, not a missing event
  (`DECISIONS.md` **D42**). No payload is invented or dropped, and the measured half (session id / terminal /
  usage) is unaffected; the KIND of event is what a real run has to confirm.

### Also open, and each a DESIGN call rather than a measurement
These came out of closing other work and are in `TASKS.md` with their reasoning. They need a decision, not a
key:
- **Blameless vs reportable** (Part 40) — `GenerationRouter` never reports a blameless verdict over a real
  failure, so a verdict cannot currently be both. That forces `ContextWindowExceeded` to stay `Failed`, which
  benches a healthy backend on repeated oversized prompts. The remedy is the router's `firstFailure` rule.
- **Curated memory: can `taskKey`/`scope` move in place?** — breaking (new parameters on the released
  `ICuratedMemoryStore.UpdateAsync`), and those fields *are* the dedup identity, so an in-place move can mint
  the duplicate `dedup:true` promises against.
- **`OpenAiCompatibleOptions.ContextSize`** — an Ollama-only option with a generic name; renaming is breaking,
  so it is major-bump-or-never.

## Standing maintenance policies
- **MEAI churn watch**: Microsoft.Extensions.AI ships roughly monthly with breaks in
  experimental/tool-content surfaces; review release notes on each bump. The bridge references
  only `Microsoft.Extensions.AI.Abstractions` (the stable core) on purpose.
- **OTel GenAI semconv watch**: the conventions are experimental and moved to a standalone repo;
  match whatever MEAI's `OpenTelemetryChatClient` currently emits rather than pinning a version.
- **Dependency refresh**: quarterly `Directory.Packages.props` review; provider-stub keeps every
  test/e2e run at zero real tokens.
