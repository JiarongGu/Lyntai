---
name: lyntai-surface
applies_when: another repository consumes Lyntai, or asks which package owns a capability, where its entry point is, or which part of the surface is promised and which is incidental
enforces: a consumer composes through AddLyntai and the public surface the ApiSurface baselines hold; a package id or a member outside them is not a promise
---

# What Lyntai owns, and what of it is promised

**Lyntai is a set of NuGet packages, not a running service: a consumer takes it by `PackageReference` and
composes it with `services.AddLyntai(cfg => …)`. What is promised is exactly the public surface the
`ApiSurface` baselines hold; storage shape is promised more strictly still.**

## Why

A neighbour that reads the source rather than the baselines builds against an internal, and one that pins
a package id from an older release can be holding a package that no longer ships. Both look fine until
the next upgrade.

## How to apply

### What it owns, and where

| Area | Package (`src/<id>/<id>.csproj`) | Where to start |
|---|---|---|
| The contracts and engines of every domain: inference, routing, prompts, scoring, traces, memory, jobs, guards, secrets, storage interfaces | `Lyntai.Core` | `src/Lyntai.Core/DependencyInjection/ServiceCollectionExtensions.cs` (`AddLyntai`); the namespace map is the brief |
| The metapackage most consumers take | `Lyntai` (`src/Lyntai.Bundle`) | its csproj's references are the membership |
| Backends needing nothing beyond Core: the `claude`/`codex` CLIs, OpenAI-compatible HTTP and friends | `Lyntai.Providers.Basic` | `docs/guide.md` §Install and compose |
| In-process GGUF inference | `Lyntai.Providers.LlamaSharp` | `docs/guide.md` §Local in-process inference |
| In-process ONNX embeddings / reranking | `Lyntai.Providers.Onnx` | its csproj `Description` |
| Media generation backends (image, video, audio, 3D) | `Lyntai.Generation`, namespace `Lyntai.Generation.Providers` | `docs/generation.md` |
| Storage backends | `Lyntai.Storage.Sqlite`, `Lyntai.Storage.Postgres`, `Lyntai.Storage.Basic` (in-memory + file system) | `lyntai-storage-contract.md` |
| MCP tools, both directions | `Lyntai.Tools.Mcp` | `docs/guide.md` §Tool-calling |
| Windows DPAPI binding for the secret vault | `Lyntai.Secrets.Dpapi` | its csproj `Description` |

The live roster is `src/*/*.csproj` (`src/Shared` is linked source, not a package). Retired ids are the
`RETIRED` array in `devtools/nuget-unlist.mjs` — among them `Lyntai.Providers.Default`, <!-- drift-ok: names a retired package id on purpose -->
`Lyntai.Storage.InMemory` and `Lyntai.Providers.ExtensionsAi`. A consumer still referencing one is on a <!-- drift-ok: names a retired package id on purpose -->
release before its retirement and moves to the package that absorbed it (`CHANGELOG.md` names which).

### What is promised

- **The public surface**: every public or protected member of each packable assembly is rendered into
  `tests/Lyntai.Tests/Api/Baselines/<Assembly>.txt` and `ApiSurfaceTests.Public_surface_matches_the_baseline`
  fails on any difference. A member absent from those files is not a promise.
- **SemVer 2.0 since 1.0, relaxed while every consumer is first-party**: a disclosed break may ship in a
  minor under `### Breaking` in `CHANGELOG.md` (`docs/DECISIONS.md` D18, D161). **A storage or migration
  break is major-only, unconditionally** (D18).
- **Behaviour**: `docs/2026-07-17-lyntai-design.md` is the semantic contract (§6 for data flow and errors);
  `docs/guide.md` is the consuming story. Memory semantics are `docs/memory.md`.
- **Versions**: `<VersionPrefix>` in `src/Directory.Build.props`, written only by the release workflow
  (`.github/workflows/release.yml`); packages publish to nuget.org.

### What is incidental

`samples/`, `bench/`, `devtools/` and `tests/` ship nothing. `internal` types, a type's XML-doc wording
and log text are not surface. The benches' figures are evidence for this repository's decisions, not an
interface.

### What it takes from whom

Lyntai's code references no other repository of this workspace (searched `src/` for every sibling name;
none). Its third-party dependencies are pinned centrally in `src/Directory.Packages.props`.

### Who consumes it

Confirmed: **Aurelia**, whose desktop projects (`apps/desktop/Aurelia.Manager*/*.csproj`) reference
`Lyntai.Core`, `Lyntai.Storage.Sqlite`, `Lyntai.Tools.Mcp`, `Lyntai.Generation` and the retired
`Lyntai.Providers.Default`, all at `3.0.2`. **Not confirmed**: any other consumer — a search of the <!-- drift-ok: names a retired package id on purpose -->
workspace's checkouts for `Lyntai` in project and package files found only Aurelia, and a consumer outside
these checkouts would not appear.
