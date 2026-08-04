# Lyntai (灵台) — Active Task Backlog

> **This file holds OPEN tasks only** — the live backlog. Completed work is not left here: once a task is
> fully done (committed + verified), its entry is **moved to [`docs/task-archive.md`](docs/task-archive.md)**
> (the completed-task record) rather than checked off in place. See the lifecycle rule
> `.claude/rules/task-lifecycle.md`. `CHANGELOG.md` remains the release-facing log; the archive is the
> per-task record (why/how). The design contract is `docs/2026-07-17-lyntai-design.md`; the forward
> sequence is `docs/ROADMAP.md`.

**Goal:** a NuGet-packable, DI-first .NET 10 library — an LLM provider abstraction (routing + fallback
across CLI / API / MEAI-bridged providers), pluggable storage (SQLite / InMemory / Postgres), and the
LLM-ops layer (prompt registry, scoring, traces, memory). `AddLyntai(...)` and go.

---

## Active backlog

_The 2026-07-26 hardening-pass deferrals are all closed (implemented or rejected-with-rationale) — see
`docs/task-archive.md` Part 23 and `docs/DECISIONS.md` D18. EMB1 (built-in OpenAI-compatible embedder)
landed 2026-07-27 — see archive Part 24. CLI2 (the Windows npm-shim spawn bug found consuming 1.2.0) landed
2026-07-30 — see archive Part 28. **CLI3 + CLI4** (the turn-free auth seam and the backend's pinned
self-install) landed 2026-08-04 — see archive Part 29 and `docs/DECISIONS.md` D26, which settles where the
"Lyntai never provisions a binary" line now sits. **REL1** (the version-authorship guard) landed 2026-08-04 —
see archive Part 30 and D25. The **CLI provider seam is now generic** (`CliProviderEngine` + a per-CLI
`ICliProviderDialect`) with a second backend (`Lyntai.Providers.CodexCli`) and portable-install support —
landed 2026-08-04, see archive Part 31 and D27/D28. Open work is the post-1.0 backlog in Part 25 below, plus
one CONDITIONAL item:_

- [ ] **JSON source-gen envelopes (optional; see `docs/DECISIONS.md` D17)** — typed
  `JsonSerializerContext` envelope types for the STABLE response envelopes only, **if envelope-parsing bugs ever
  materialize** (none have). Not a license to reintroduce reflection serialization.

## Part 32 — a MEDIA domain (image + video generation)

_Filed 2026-08-04 by a consuming app, on the owner's directive that **everything AI-related belongs in
Lyntai — including whole new domains as new packages** — so an app stays business-central. This is the
largest AI capability currently living in an app rather than the library._

- [ ] **MED1 — `IGenerationProvider` / `IVideoProvider` as a Lyntai domain (new package, e.g.
  `Lyntai.Generation` + per-backend provider packages).**

  **The evidence.** A consuming desktop app currently owns **1,367 lines across 13 files** of image/video
  generation: a provider abstraction, a factory that routes between backends, an OpenAI-compatible images
  client, an Automatic1111 / SD-WebUI client, a local **stable-diffusion.cpp** subprocess backend, a video
  provider seam, and template rendering. None of it is app-specific — it is the same shape as the LLM side
  (abstraction + swappable backends + per-install config), just for a different medium. Every app the owner
  builds that generates an image will otherwise re-write it.

  **Precedent this follows:** Lyntai already does non-chat AI over HTTP (`Lyntai.Embeddings.IEmbedder` /
  `HttpEmbedder`, EMB1), so "generation of a non-text medium" is not a category break — and `IProviderAuth` /
  `IProviderVersionInstaller` just showed the optional-capability pattern working for a second concern.

  **Suggested seam** (shape matters, not the steps):
  - `IGenerationProvider` — text→image and prompt-guided image→image edit, returning bytes plus what the backend
    said about them; `IVideoProvider` for a composition→video render.
  - Backends as separate packages, mirroring the provider layout: an OpenAI-compatible one, an
    Automatic1111 HTTP one, and a **local subprocess** one (stable-diffusion.cpp) — the last of which proves
    the seam isn't HTTP-shaped, exactly as `ClaudeCliProvider` proved the LLM seam isn't.
  - A capability probe in the same spirit as `IProviderInstallation`: "is this backend usable?" answerable
    **without** generating a billable image (the app currently does a tiny generate-and-discard, which is the
    wasteful equivalent of the completion-to-test-auth problem CLI3 removed).
  - Routing/fallback across media backends should reuse the existing router thinking rather than a second one.

  **Explicitly NOT in scope, per D26** — and the app should keep these, so please don't absorb them:
  - **Downloading the local engine or its model weights** (a ~1.7 GB GGUF, an `sd-cli.exe`): the host owns its
    download, storage and trust policy.
  - **Where output bytes land**, the media library, and the UI.
  - **Credentials** for a cloud image backend.
  - A **host-only renderer** (one app renders HTML→MP4 through its own embedded browser) — that is a host
    capability reached over a loopback, not a library concern; the seam just needs to accept such an
    implementation from outside.

  **Done when:** an app can generate and edit an image, and render a video, through Lyntai seams with its own
  backend choice + config, and can ask "is this backend usable?" without paying for a generation — with the
  binary/model download, the output location and the credentials still owned by the app.

  **Status 2026-08-04:** the platform CORE landed — `Lyntai.Generation` (contracts, capability model, the three
  delivery seams, verdicts, capability-aware router, DI). Plan of record:
  `docs/2026-08-04-generation-platform-plan.md`; rationale in `docs/DECISIONS.md` D30. Scope was generalized on the
  owner's direction ("not a generation engine — a media generation **platform**", spanning image/video/audio
  and 3d → image → video chaining), so there is deliberately **no separate `IVideoProvider`**: video is
  `Kind = "video"` plus the async-job delivery mode, because what differs is how a backend DELIVERS, not which
  medium it makes. Remaining: Plan 2 (HTTP image backends), Plan 3 (local subprocess backend), Plan 4 (async
  video + `Lyntai.Jobs` composition), Plan 5 (governance/telemetry parity), Plan 6 (tool/MCP bridge +
  streaming audio), Plan 7 (pipelines). Open questions for the owner are listed at the end of the plan.

> Add new tasks here as checklist items with an `id` and a short `file:line` where known. Group related
> tasks under a `## Part N — <theme>` heading. Move an item to the archive when it lands — don't leave a
> `[x]` here.

---

## Part 25 — post-1.0 backlog (deferred at the 1.0 API-review triage, 2026-07-28)

_Additive / non-breaking items surfaced by the 1.0 adversarial API review + consumer-usage review (the
working record was `devtools/_review/*`; rejects + rationale are in `docs/DECISIONS.md` D21). None block
1.0 — each is safe to add in a post-1.0 minor._

- [ ] **async migration entry points** — `MigrateUpAsync(…, CancellationToken)` twins alongside the sync
  `MigrationRunnerService.MigrateUp` (SQLite + Postgres), for apps owning their schema under
  `SchemaMigration.None`.
- [ ] **semantic-memory wiring helper** — a DI seam / `Use*` helper so an app enabling semantic recall
  doesn't hand-construct `SqliteCuratedMemoryStore` / `SqliteVectorStore` / `MigratingConnectionFactory` /
  `HttpEmbedder` (a consumer does this today). Those concrete types STAY public for 1.0.
- [ ] **`AddMcpTools` convenience overload** — `params ITool[]` and/or document the
  `await McpToolset.FromClientAsync` → `AddMcpTools` two-step as the intended shape.
- [ ] **verdict helpers** — `reply.IsOk()` / `reply.IsRateLimited()` extension(s) to cut the 3-branch
  `LlmVerdict` pattern at call sites.
- [ ] **curated-memory ergonomics** — a `Source`/metadata convenience accessor (apps unpack
  `metadata["source"]` by hand after CMEM6); reconsider the delete+re-add for immutable `kind`/`task`/`scope`.
- [ ] **agent-event contract** — `ClaudeToolCalls.FilePathOf` should also read `notebook_path`/`path`;
  consider a discoverable event-shape contract instead of anonymous objects apps reflect over.
- [ ] **member/type XML docs** — `ExtensionsAiProvider` public ctor, `LyntaiChatClientExtensions` type
  summary, `ClaudeCliProvider` interface members, `AddMcpTools` intended-shape doc.
- [ ] **`OpenAiCompatibleOptions.ContextSize` legibility** — Ollama-only option with a generic name; a
  rename (e.g. `OllamaContextSize`) is BREAKING, so it's a major-bump-or-never item — accepted as-is for
  1.0, revisit only if it causes real confusion.

---

## How to work a task (evergreen)

- **TDD, every task:** failing test → run it fail → minimal impl → run it pass → commit. Read
  `.claude/rules/dev-conventions.md` (package layout, migrations, spawn hygiene) and the relevant
  `.claude/knowledge/*` + `.claude/skills/*` before extending.
- **Commit per task.** **Never commit without the user's approval.** Describe changes structurally in the
  message (no dev-machine paths / private tokens — the pre-commit guard enforces this).
- **This is a generic library** — every task must be a reusable, app-agnostic improvement behind the
  `ILlmClient` front door / a BYO seam, never app-specific code. Update the `ApiSurface` baselines
  deliberately on any public-surface change.
- **Deviate from a task's suggested steps when the code disagrees** — the spec's *contract* (interfaces,
  semantics) is authoritative; a task's step list is a suggestion. Record real deviations in the commit
  message.
- **When a task completes, archive it** (`.claude/rules/task-lifecycle.md`): move its entry (with the
  completion date + a one-line **Outcome**) into `docs/task-archive.md`, and delete it from here.
