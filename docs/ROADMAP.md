# Lyntai — Roadmap

> One line per shipped version, then what is still open. The detail is `CHANGELOG.md`, the reasoning
> `docs/DECISIONS.md`; versioning and its one relaxation are **D16**, **D18** and **D70**. A per-version
> design record, where one exists, is indexed in `docs/superpowers/INDEX.md`.

## Shipped

| Version | What it added |
|---|---|
| v0.1–v0.30 | **the pre-1.0 line.** The substrate and fallback router; three storage backends; the §9 platform kit (in-process local inference, the agentic tool loop, native tool-calling, MCP, durable jobs with priorities/cron/cancellation, guards); observability, response caching, usage budgeting, rate limiting; semantic + curated memory; recoverable secrets and app-owned storage. **Every 0.x version is unlisted on nuget.org (D44)**, so none is resolvable by a consumer and none carries the SemVer promise, which begins at 1.0. |
| **v1.0.0** (2026-07-28) | **the API freeze** — SemVer 2.0 from here, gated by `ApiSurfaceTests` (D16, amended by D18) |
| v1.1–v1.2.2 | CLI tool-hosting generalized; turn-free backend probe/auth + pinned self-install |
| v2.0.1 (2026-08-04) | the generation platform + a coherent package graph (D24–D25; 2.0.0 is burned — D23) |
| v2.1.0 (2026-08-04) | the generation backends registerable in one line each, and named factories where a constructor could be silently transposed (D28; `docs/task-archive.md` Part 36) |
| v2.2.0 (2026-08-05) | the provider-lifetime seam (D30) and a second agent-session backend (D35) |
| v2.3.0 (2026-08-05) | the pre-release whole-library review — shipped separately only because 2.2.0 was cut from the pushed branch without it (D18, D37; the push-before-release lesson is in pitfalls.md) |
| v2.4.0 (2026-08-05) | app-owned MCP servers on either CLI agent session (D38) |
| **v2.5.0** (2026-08-08) | **long-term memory** — named engines, decay measured in interference, burial rather than deletion (D39–D41) |
| **v3.0.0** (2026-08-17) | **the memory retention model, then the pre-freeze sweep that followed it.** Memory (D45–D66): seven `IMemory*Policy` domains, FSRS as the only shipped curve, RRF the ranking default, a recall that no longer lengthens a half-life, an authoritative fact that takes a slot within the limit, six pre-release migrations folded into one. Everything else (D67–D82): the generation stream door, streaming tool calls, the cross-process job cap, the forget/prune split, the generation router as a trust boundary, every generation backend registered by configure callback, the naming sweep — and `Lyntai.Generation`'s SemVer exemption **withdrawn**, so no package is exempt |
| v3.0.1 (2026-08-21) | **five memory seams two adopting applications had to work around**, all one shape — a registration that resolves and can never run (D83–D86): the composition renderer reachable without an engine, per-entry grades for a curated catalog that mixes provenance, fan-out writes so a blend's second member is not silently empty, a wiring check for a member or policy nothing can reach, and a scope-optional semantic recall. Additive throughout |
| v3.0.2 (2026-08-21) | the adopting applications' next round, same shape as 3.0.1 — a seam that resolves and cannot run |
| v3.1.0 (2026-08-23) | three more adopter reports (D87, D88): a named client that states its own candidates, subject seeding readable at recall, and salience no longer voting on ranking by default |
| **v3.2.0** (2026-09-19) | **the design-closure window** (D159–D165): the wire is a provider and "dialect" leaves the vocabulary, one wallet reaching every attributable kind, the design record's gate exemption narrowed to its seeds, and a BINARY process stream. The generation backends stopped being documented-not-measured — `sd-cli`, ComfyUI (image and video) and a streaming piper TTS backend all measured against real engines — leaving fal's wire the only unmeasured one |

## Open

- **fal's wire format** is the one generation mapping never called against the real service (`TASKS.md`).
- **Server/host/launcher + auto-update** stays out of scope for good: a host is an application's concern,
  and the library stays host-free (design §9).

## Standing maintenance policies

- **OTel GenAI semconv watch**: the conventions are experimental and moved to a standalone repo, so pin
  nothing and follow the spec.
- **Dependency refresh**: quarterly `Directory.Packages.props` review; the provider-stub keeps every test and
  e2e run at zero real tokens.
