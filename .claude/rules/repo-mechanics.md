# Repo mechanics — this repository's concrete bindings for the canonical rules

**Local rule.** The canonical rules installed by `daoris` state the principles; this file states how they
are *enforced here* — the commands, paths, and version policy specific to this repository. When a
canonical rule and this file appear to disagree, the canonical rule states the intent and this file states
the mechanism.

> **"Local" is not protection from a sync — measured 2026-08-05.** A `daoris` run **deleted**
> `dev-conventions.md`, which was in no lock entry and had no provenance header, exactly like this file.
> Most of it had been migrated first, but **three rules were lost outright** and survived only because a
> review went looking. Nothing failed and no link dangled: a lost rule is invisible until someone repeats
> the mistake it prevented. **After any sync, diff the deleted and modified rule files before committing**
> and check every rule still has a home.

## Sensitive info — the guard that enforces it

- The scan is `devtools/scripts/check-sensitive.mjs`, run by `devtools/hooks/pre-commit` on staged
  changes. It blocks the commit on any hit.
- Install once per clone: `node devtools/dev.mjs install-hooks` (sets `core.hooksPath`).
- Real private tokens go in the gitignored `local/sensitive-patterns.txt`, one JavaScript regex per
  line — never in a tracked file.
- Scan the whole tree at any time: `node devtools/dev.mjs check-sensitive --tree`.
- Sibling projects are referred to neutrally ("a sibling project"), never by name or on-disk path.

## Task lifecycle — the three files, and version authorship

- The three records are `TASKS.md` (open backlog only), `docs/task-archive.md` (the per-task history),
  and `CHANGELOG.md` (the release-facing log). Use the `archive-task` skill for the mechanical move.
- Write changelog entries under `## Unreleased`. The release workflow stamps that heading with the
  version and date (`node devtools/dev.mjs changelog --fix`) — never hand-stamp it. A titled release is
  pre-titled as `## Unreleased — <title>`.

### Never hand-edit the version

**`<VersionPrefix>` in `src/Directory.Build.props` is written by the release workflow, never by hand.**
The workflow bumps from whatever that file currently says, so a manual bump silently moves the baseline
and the next release publishes the version *after* the intended one — the skipped version is simply gone.
This happened in a sibling repository; see `docs/DECISIONS.md` D25.

- Both that edit and a hand-stamped `## Unreleased` heading are blocked by the `check-version-bump`
  pre-commit guard.
- `node devtools/dev.mjs doctor` fails when `VersionPrefix` no longer matches the newest `v*` tag.
- Releasing, or repairing a botched release, sets `LYNTAI_RELEASE=1`.

### The next major is 2.0.1, not 2.0.0

2.0.0 is permanently taken — published then unlisted on 10 of the 12 package ids, and an unlisted
version's number is never freed. A 2.0.0 release would report success while `--skip-duplicate` silently
published nothing for those 10. Cut it with an explicit `version: 2.0.1` and `bump: none`. See
`docs/DECISIONS.md` D29.

## Package layout — the binding for the canonical rule

Canonical `dotnet-package-layout` states the boundaries; here they resolve to concrete names.

- **Contract in `Lyntai.Core`, implementation in an adapter** (`Lyntai.Storage.Sqlite`,
  `Lyntai.Providers.Default`, `Lyntai.Providers.ExtensionsAi`, `Lyntai.Providers.Local`, …) that
  project-references Core only — or one domain package, such as `Lyntai.Generation` — and **never
  adapter→adapter**. Backends needing nothing extra share `Lyntai.Providers.Default`; one earns its own
  package the moment it drags a native runtime, a platform-specific API, or a dependency a consumer might
  refuse. "Most consumers want X" makes it a member of the `Lyntai` metapackage, never a Core dependency.
  See `docs/DECISIONS.md` D31 (the split), D32 (the bundle budget), D33 (many small packages).
- **Every `src/*` is packable** (`IsPackable=true`, `PackageId`, description); `samples/` and `tests/`
  are not. The version comes from `VersionPrefix` in `src/Directory.Build.props` — the single source, and
  never hand-edited (above). `node devtools/dev.mjs new-package` scaffolds a package into the nine
  registries `check-packages` gates.
- **DI-first.** The public entry is `services.AddLyntai(cfg => …)`; adapter packages extend
  `LyntaiBuilder` with their own `Add*`/`Use*` methods, and a consumer constructs nothing by hand.
  Variation points are DI collections: `ILlmProvider`s are keyed by `Id` and picked by `ILlmRouter`,
  `IScoringService` iterates `IEnumerable<IScorer>` — adding one is a class plus a registration, never a
  `switch` edit.

## Naming — the binding for the canonical rule

Canonical `dotnet-package-layout` says never to name a type for the layer it crosses. Here that has a
measurable invariant: **the tree contains zero `Dto` identifiers, and that is worth keeping.** Reach for
the established suffixes — `*Options`, `*Request`/`*Reply`, `*Result`, `*Entry`/`*Record`, `*Row`,
`*Event`, `*Args`, `*Policy`.

Do not write "DTO" in prose about these types either — docs and XML comments seed the name back in on
the next change. Say "the row type", "the request record", "the wire type". Entries already in
`docs/task-archive.md` keep their original wording: an archive is a record, not a prescription. And
`dto` is not an abbreviation for `DateTimeOffset` in a local, either.

## Dev loop

- `node devtools/dev.mjs <build|test|e2e|playground|pack|install-hooks|check-sensitive>`.
- e2e suites live in `devtools/scripts/e2e/` as `pN.mjs`, discovered by `^p\d+\.mjs$`; the shared
  harness is `_e2e-common.mjs` — the leading underscore is what keeps it from being discovered as a
  suite. Each boots the Playground against the stub over an isolated `devtools/_e2e-*` data folder.
- **Tests:** xUnit. Pure logic (router, fallback, dedup, cooldown, prompt render, `FtsQuery`, scoring
  aggregation) is unit-tested with fakes and no I/O. Storage is integration-tested against a per-test
  temp SQLite database, created and migrated then deleted. Providers run against the deterministic
  provider-stub (`devtools/scripts/provider-stub.mjs`, selected by `LYNTAI_PROVIDER_CMD`) so no test
  needs a real token.

## Scratch and working files

- Scratch, probes, and dumps go under the gitignored `devtools/_*` (for example the e2e harness's
  `devtools/_e2e-*` data directories). Reusable tooling goes in `devtools/`, tracked.
- **This machine's console is GBK.** Write files with the file-writing tools or an explicit UTF-8
  encoding — never build file content by echoing it through the console, which lossily mangles CJK and
  em-dashes. See `.claude/knowledge/pitfalls.md`.

## Assistant memory

- The 2026-07-22 migration moved the old global memories into `docs/DECISIONS.md` D6–D15 and the
  `.claude/` rules and knowledge. Global memory is kept empty of project facts.
- The library's **own** memory subsystem (`IMemoryStore` / `ICuratedMemoryStore` / `ISemanticMemory`) is a
  separate thing entirely — the canonical rule is about the assistant's memory, not the product's.
