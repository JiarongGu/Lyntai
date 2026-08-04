# 2.0.1 Restructure — Implementation Plan

**Goal:** make the package graph obey ONE rule everywhere (D31: split by dependency footprint), then release
it as **2.0.1** — the next major, skipping 2.0.0 because that number is permanently taken (D29).

**Architecture:** `Lyntai.Core` carries every domain's contracts and engines and keeps the smallest dependency
footprint of any package, because it is the one package nobody can opt out of. Adapters exist only where a
real dependency justifies one. Two package boundaries that exist for no dependency reason are removed
(`Lyntai.Generation`, `Lyntai.Generation.Http`), and a `Lyntai` metapackage is added for one-line installs.

**Verification model — read this before starting.** This restructure moves files between assemblies and
changes **no namespace and no type**. So the correctness criterion is not new tests; it is:

1. **all 1254 existing tests pass UNTOUCHED** (any test needing an edit means a namespace moved — stop and
   reconsider), and
2. **the merged API baselines are the exact union of the old ones** (no member added, renamed or lost).

That is a stronger check than a hand-written test would be, and it is why this plan has few new tests.

---

## Target graph (10 packages + 1 metapackage)

| Package | Why it exists separately | External deps |
|---|---|---|
| `Lyntai.Core` | mandatory: contracts + engines for **every** domain (Llm, **Generation**, Jobs, Storage, Cortex, Agents, Guards, Secrets, Prompts, Memory, Embeddings, Processes, Text) | DI.Abstractions, Logging.Abstractions |
| `Lyntai.Providers.Default` | the dep-free backends: claude/codex CLI, OpenAI-compatible chat + embeddings + **images**, **A1111**, **ComfyUI** | Extensions.Http (managed) |
| `Lyntai.Providers.ExtensionsAi` | MEAI bridge — abstractions-only, but MEAI churns fast and this is an *optional* bridge, not a default backend | Extensions.AI.Abstractions |
| `Lyntai.Providers.Local` | LLamaSharp **+ a native backend** (hundreds of MB) | LLamaSharp |
| `Lyntai.Storage.Sqlite` | **native SQLite binary** + Dapper + FluentMigrator | 4 packages |
| `Lyntai.Storage.Postgres` | Npgsql + FluentMigrator | 3 packages |
| `Lyntai.Storage.InMemory` | dep-free, but an **implementation** — Core stays contracts-only | none |
| `Lyntai.Tools.Mcp` | MCP SDK, which drags **MEAI.Abstractions 10.5.2** (a version-conflict surface) | ModelContextProtocol.Core |
| `Lyntai.Tools.Mcp.Hosting` | **framework reference on Microsoft.AspNetCore.App** | ModelContextProtocol.AspNetCore |
| `Lyntai.Secrets.Dpapi` | **Windows-only** ProtectedData | ProtectedData |
| `Lyntai` *(metapackage)* | one-line install for a typical app; references the dep-free set only | — |

### Why Generation folds in, and MCP does not

Both questions have the same answer, applied in opposite directions:

- `Lyntai.Generation` has **zero** external dependencies. A separate package therefore isolates nothing — it
  is a boundary that exists only because it was created before D31 was written. Fold it in.
- `Lyntai.Tools.Mcp*` each drag something real (MEAI abstractions pinned at a *different* version than our
  own bridge; ASP.NET Core). Core is **mandatory**, so its footprint is the one every consumer pays,
  including one that only wanted SQLite storage. Keep them out. "Most consumers use it" is an argument for a
  metapackage, not for a dependency in the mandatory package — and the part that matters most
  (`ITool`/`IToolLoop`/`AddTool`) is **already in Core**.

---

## Task 1: Fold `Lyntai.Generation` into `Lyntai.Core`

**Files:**
- Move: `src/Lyntai.Generation/*.cs` → `src/Lyntai.Core/Generation/`, and
  `src/Lyntai.Generation/Routing/*.cs` → `src/Lyntai.Core/Generation/Routing/`
- Delete: `src/Lyntai.Generation/Lyntai.Generation.csproj`
- Modify: `Lyntai.slnx`, `devtools/project.config.mjs`, `tests/Lyntai.Tests/Lyntai.Tests.csproj`,
  `src/Lyntai.Generation.Http/Lyntai.Generation.Http.csproj`, `tests/Lyntai.Tests/Api/ApiSurfaceTests.cs`
- Delete: `tests/Lyntai.Tests/Api/Baselines/Lyntai.Generation.txt`

- [ ] **Step 1: Move the sources, preserving namespaces**

```bash
mkdir -p src/Lyntai.Core/Generation/Routing
for f in src/Lyntai.Generation/*.cs; do git mv "$f" "src/Lyntai.Core/Generation/$(basename "$f")"; done
for f in src/Lyntai.Generation/Routing/*.cs; do git mv "$f" "src/Lyntai.Core/Generation/Routing/$(basename "$f")"; done
git rm -q src/Lyntai.Generation/Lyntai.Generation.csproj
rm -rf src/Lyntai.Generation
```

The files keep `namespace Lyntai.Generation` / `Lyntai.Generation.Routing`. **Do not** rename them to
`Lyntai.Core.Generation`: a folder is not a namespace here, and renaming would break every consumer's `using`
for no benefit.

- [ ] **Step 2: Drop the now-redundant project reference**

In `src/Lyntai.Generation.Http/Lyntai.Generation.Http.csproj`, remove:

```xml
    <ProjectReference Include="..\Lyntai.Generation\Lyntai.Generation.csproj" />
```

(Core is already referenced, and Core now contains those types.)

- [ ] **Step 3: Deregister the package** — remove the `src/Lyntai.Generation/Lyntai.Generation.csproj` line
from `Lyntai.slnx`, the `'src/Lyntai.Generation',` entry from `packableProjects` in
`devtools/project.config.mjs`, and the `<ProjectReference … Lyntai.Generation.csproj />` from
`tests/Lyntai.Tests/Lyntai.Tests.csproj`.

- [ ] **Step 4: Fold the baseline** — in `ApiSurfaceTests.cs` remove `"Lyntai.Generation",` from
`Assemblies()` and the `["Lyntai.Generation"] = …` line from `Loaded`, then
`git rm tests/Lyntai.Tests/Api/Baselines/Lyntai.Generation.txt`.

- [ ] **Step 5: Verify the union, not just "it compiles"**

Run: `node devtools/dev.mjs test --filter "FullyQualifiedName~ApiSurfaceTests"`
Expected: the `Lyntai.Core` baseline test FAILS with a `.actual` written. Then diff it and confirm **every**
added line is a `Lyntai.Generation*` type that previously lived in the deleted baseline, and that **nothing was
removed**:

```bash
cd tests/Lyntai.Tests/Api/Baselines
git --no-pager diff --no-index --unified=0 Lyntai.Core.txt Lyntai.Core.txt.actual | grep -E "^-" | grep -v "^---"
# ^ MUST be empty. Additions must all be Lyntai.Generation.*
mv -f Lyntai.Core.txt.actual Lyntai.Core.txt
```

- [ ] **Step 6: Full gate**

Run: `node devtools/dev.mjs verify`
Expected: green, **1254 tests, no test file edited**.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "refactor(packaging)!: fold Lyntai.Generation into Core (no dependency to isolate)"
```

---

## Task 2: Fold `Lyntai.Generation.Http` into `Lyntai.Providers.Default`

**Files:**
- Move: `src/Lyntai.Generation.Http/*.cs` → `src/Lyntai.Providers.Default/Generation/`
- Delete: `src/Lyntai.Generation.Http/Lyntai.Generation.Http.csproj`
- Modify: `Lyntai.slnx`, `devtools/project.config.mjs`, `tests/Lyntai.Tests/Lyntai.Tests.csproj`,
  `tests/Lyntai.Tests/Api/ApiSurfaceTests.cs`, `src/Lyntai.Providers.Default/Lyntai.Providers.Default.csproj`
  (description)
- Delete: `tests/Lyntai.Tests/Api/Baselines/Lyntai.Generation.Http.txt`

- [ ] **Step 1: Move the sources** (namespace `Lyntai.Generation.Http` unchanged)

```bash
mkdir -p src/Lyntai.Providers.Default/Generation
for f in src/Lyntai.Generation.Http/*.cs; do git mv "$f" "src/Lyntai.Providers.Default/Generation/$(basename "$f")"; done
git rm -q src/Lyntai.Generation.Http/Lyntai.Generation.Http.csproj
rm -rf src/Lyntai.Generation.Http
```

- [ ] **Step 2: Widen the package description** in `Lyntai.Providers.Default.csproj` so the id still describes
its contents:

```xml
    <Description>The default Lyntai backend set: the authenticated `claude` and `codex` CLIs, any OpenAI-compatible endpoint (OpenAI/Ollama/OpenRouter/Azure) for chat + embeddings + images, a Stable Diffusion WebUI (Automatic1111) backend and a local ComfyUI generation backend — bundled because they share one dependency footprint and ship no native payload. Heavier backends (local GGUF, the MEAI bridge) remain separate packages.</Description>
```

- [ ] **Step 3: Deregister** the package from `Lyntai.slnx`, `packableProjects`, the test project's
`ProjectReference`s, and `ApiSurfaceTests` (`Assemblies()` + `Loaded`); `git rm` its baseline.

- [ ] **Step 4: Verify the union** exactly as in Task 1 Step 5, against
`Lyntai.Providers.Default.txt` — every addition must be a `Lyntai.Generation.Http.*` type, nothing removed.

- [ ] **Step 5: Full gate + commit**

Run: `node devtools/dev.mjs verify` (expect green, 1254 tests, no test edited)

```bash
git add -A && git commit -m "refactor(packaging)!: fold the HTTP generation backends into Providers.Default"
```

---

## Task 3: The `Lyntai` metapackage

**Files:**
- Create: `src/Lyntai.Meta/Lyntai.Meta.csproj`
- Modify: `Lyntai.slnx`, `devtools/project.config.mjs`

- [ ] **Step 1: Create it** — a package with no code, only references, so one install gets a working default
setup. Deliberately excludes anything with a native, platform-specific or fast-churning dependency:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    The one-line install: `dotnet add package Lyntai` gets Core plus the dependency-free default backends and
    in-memory storage. Everything with a real dependency stays an explicit opt-in (Storage.Sqlite's native
    binary, Providers.Local's LLamaSharp, Tools.Mcp*'s MEAI/ASP.NET, Secrets.Dpapi's Windows-only API) — see
    docs/DECISIONS.md D31. A metapackage is how "most people want X" is served WITHOUT putting X's
    dependencies in front of someone who only wanted storage.
  -->
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <PackageId>Lyntai</PackageId>
    <AssemblyName>Lyntai.Meta</AssemblyName>
    <RootNamespace>Lyntai.Meta</RootNamespace>
    <Description>One-line install for Lyntai: the core library plus the dependency-free default backends (claude/codex CLI, OpenAI-compatible chat/embeddings/images, Automatic1111, ComfyUI) and in-memory storage. Add Lyntai.Storage.Sqlite, Lyntai.Providers.Local, Lyntai.Tools.Mcp or Lyntai.Secrets.Dpapi when you need them.</Description>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>false</SuppressDependenciesWhenPacking>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Lyntai.Core\Lyntai.Core.csproj" />
    <ProjectReference Include="..\Lyntai.Providers.Default\Lyntai.Providers.Default.csproj" />
    <ProjectReference Include="..\Lyntai.Storage.InMemory\Lyntai.Storage.InMemory.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register** in `Lyntai.slnx` and `packableProjects`. Do **not** add it to `ApiSurfaceTests` —
it has no public surface of its own (`IncludeBuildOutput=false` means no assembly ships).

- [ ] **Step 3: Prove it packs as references-only**

Run: `node devtools/dev.mjs pack`
Expected: `Lyntai.<version>.nupkg` exists and is tiny (no `lib/` assembly). Confirm:

```bash
unzip -l publish/packages/Lyntai.*.nupkg | grep -E "lib/|\.nuspec"
# expect the .nuspec and NO lib/**/Lyntai.Meta.dll
```

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(packaging): add the Lyntai metapackage for one-line installs"
```

---

## Task 4: Documentation, and the rule that produced this

**Files:** `README.md`, `CLAUDE.md`, `docs/DECISIONS.md`, `.claude/rules/dev-conventions.md`,
`docs/ROADMAP.md`, `CHANGELOG.md`, `TASKS.md`

- [ ] **Step 1: Amend D31** with the principle that decided both open questions — it is the reusable part:

```markdown
**Amendment (2026-08-04): Core is MANDATORY, so it carries the smallest footprint of any package.**
Two questions resolved by this, in opposite directions:
- `Lyntai.Generation` had **no** external dependency, so its package boundary isolated nothing — folded into
  Core. So was `Lyntai.Generation.Http`, folded into `Providers.Default`.
- `Lyntai.Tools.Mcp` was proposed for Core on the grounds that most consumers use it. Rejected on measurement:
  `ModelContextProtocol.Core` drags `Microsoft.Extensions.AI.Abstractions` **10.5.2** — a *different* version
  than our own MEAI bridge pins — and `ModelContextProtocol.AspNetCore` carries a framework reference on
  `Microsoft.AspNetCore.App`. Putting either in Core would hand an ASP.NET/MEAI dependency to a consumer who
  only wanted SQLite storage. The tool CONTRACT (`ITool`, `IToolLoop`, `IToolRegistry`, `AddTool`) is already
  in Core; only the wire adapter sits outside.
**"Most consumers want X" is an argument for a metapackage, never for a dependency in the mandatory package.**
```

- [ ] **Step 2: README** — rewrite the packages table to the 10 + metapackage shape, and lead the install
section with `dotnet add package Lyntai`.

- [ ] **Step 3: CLAUDE.md** — update the namespace map: `Lyntai.Generation` (+ `.Routing`) now lives in Core;
drop the `Lyntai.Generation` / `Lyntai.Generation.Http` packages from any package list.

- [ ] **Step 4: `dev-conventions.md`** — state the amended rule in one line so the next backend lands right.

- [ ] **Step 5: ROADMAP** — a short `## 2.x` section: what 2.0.1 contains (the restructure + the generation
platform), and what is next (generation Plans 3–7).

- [ ] **Step 6: CHANGELOG** — retitle the `## Unreleased` heading to
`## Unreleased — the generation platform + a coherent package graph`, and add the package moves under the
existing `### Breaking` heading with the one-line migration for each. **Never stamp a version here** (D25).

- [ ] **Step 7: Verify + commit**

```bash
node devtools/dev.mjs verify && node devtools/dev.mjs doctor
git add -A && git commit -m "docs: record the 2.0.1 package graph and the mandatory-Core rule (D31)"
```

---

## Task 5: Release 2.0.1

- [ ] **Step 1: Pre-flight** — confirm the working tree is clean, `doctor` is green, and the CHANGELOG's
`## Unreleased` heading carries the title you want stamped.

- [ ] **Step 2: Confirm 2.0.1 is still free** on every id (it was on 2026-08-04, but re-check — this is the
one irreversible step):

```bash
for pkg in core providers.default providers.extensionsai providers.local \
           storage.sqlite storage.postgres storage.inmemory \
           tools.mcp tools.mcp.hosting secrets.dpapi lyntai; do
  printf "%-32s " "$pkg"
  curl -s "https://api.nuget.org/v3-flatcontainer/lyntai${pkg:+.$pkg}/index.json" \
    | grep -c '"2.0.1"' || true
done
# every count MUST be 0
```

(`Lyntai` itself is a NEW id — expect a 404, which is fine and means it's free.)

- [ ] **Step 3: Cut it from the Actions tab** with **`version: 2.0.1`** and **`bump: none`**.
Both matter: an empty version input would bump from 1.2.2 to 1.2.3, and 2.0.0 is permanently taken (D29). The
workflow bumps `VersionPrefix`, stamps the CHANGELOG heading, runs the full gate, publishes, then commits and
tags — do **not** hand-edit either file (D25).

- [ ] **Step 4: Verify what actually published** — index propagation lags, so re-check before concluding
anything is missing:

```bash
for pkg in core providers.default providers.extensionsai providers.local \
           storage.sqlite storage.postgres storage.inmemory \
           tools.mcp tools.mcp.hosting secrets.dpapi; do
  printf "%-32s " "lyntai.$pkg"
  curl -s "https://api.nuget.org/v3-flatcontainer/lyntai.$pkg/index.json" | grep -o '"2.0.1"' || echo MISSING
done
curl -s "https://api.nuget.org/v3-flatcontainer/lyntai/index.json" | grep -o '"2.0.1"' || echo "metapackage MISSING"
```

- [ ] **Step 5: Archive the work** — move Part 32 / MED1 and this restructure into `docs/task-archive.md` with
outcomes, per `task-lifecycle.md`, and trim `TASKS.md` to what is still open (generation Plans 3–7).

---

## Consumer migration (one line each, no code changes)

| Was | Now |
|---|---|
| `Lyntai.Providers.ClaudeCli` / `.CodexCli` / `.OpenAiCompatible` | `Lyntai.Providers.Default` |
| `Lyntai.Generation` | `Lyntai.Core` (already referenced — just drop it) |
| `Lyntai.Generation.Http` | `Lyntai.Providers.Default` |
| a typical app's whole set | `Lyntai` (metapackage) |

**No `using` changes, no type renames, no `Add*` renames.** Every namespace survives the move — that was the
constraint the whole restructure was designed around.
