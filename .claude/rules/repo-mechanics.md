---
name: repo-mechanics
applies_when: applying a canonical rule in this repository — package names, version authorship, the dev loop, guard scripts, or scratch paths
enforces: contract in Lyntai.Core with adapters, never adapter-to-adapter; zero Dto identifiers; never hand-edit VersionPrefix or the Unreleased heading; scratch under devtools/_*
---
<!-- local: never synced; not a daoris artifact -->

# Repo mechanics — this repository's concrete bindings for the canonical rules

**Local rule.** The canonical rules installed by `daoris` state the principles; this file states how they
are *enforced here* — the commands, paths, and version policy specific to this repository. When a
canonical rule and this file appear to disagree, the canonical rule states the intent and this file states
the mechanism.

> **"Local" is not protection from a sync — measured 2026-08-05.** A `daoris` run **deleted**
> `dev-conventions.md`, which was in no lock entry and had no provenance header, exactly like this file.
> Most of it had been migrated first, but **three rules were lost outright** and survived only because a
> review went looking. Nothing failed and no link dangled: a lost rule is invisible until someone repeats
> the mistake it prevented. **After any sync, diff the deleted and modified files across
> `.claude/rules/`, `.claude/knowledge/` AND `.claude/skills/` before committing** and check every rule,
> knowledge document and skill still has a home. The five local skills (`add-migration`, `add-provider`,
> `add-scorer`, `add-storage-backend`, `archive-task`) and the five local knowledge documents
> (`extending-lyntai`, `generic-library`, `llm-and-router`, `pitfalls`, `storage`) carry no provenance
> header and no `daoris.lock` entry — the same exposure as this file (measured for the rules tier;
> inferred for the other two).

## Sensitive info — the guard that enforces it

- The scan is `devtools/scripts/check-sensitive.mjs`, run by `devtools/hooks/pre-commit` on staged
  changes. It blocks the commit on any hit.
- Install once per clone: `node devtools/dev.mjs install-hooks` (sets `core.hooksPath`).
- Real private tokens go in the gitignored `local/sensitive-patterns.txt`, one JavaScript regex per
  line — never in a tracked file.
- Scan the whole tree at any time: `node devtools/dev.mjs check-sensitive --tree`.
- Sibling projects are referred to neutrally ("a sibling project"), never by name or on-disk path. The
  one bounded exception, recorded so it is not read as a general blessing: an on-disk PATH to a sibling
  is never acceptable anywhere (the guard's built-ins catch the two Windows shapes), while sibling NAMES
  appear on purpose only in the existing provenance records — design §1, the README credits,
  `docs/DECISIONS.md`. Do not introduce a sibling name anywhere new: not in code, tests, tooling, or a
  commit message. Whether those projects are public under those names is the owner's call to make, not
  something this file settles.

## Task lifecycle — the three files, and version authorship

- The three records are `TASKS.md` (open backlog only), `docs/task-archive.md` (the per-task history),
  and `CHANGELOG.md` (the release-facing log). Use the `archive-task` skill for the mechanical move.

### Documents have the same lifecycle as tasks (D52)

A finished task moves out of the backlog; a finished **document** moves out of `docs/`. Tracked `docs/` is
maintained state — the contract, `DECISIONS.md`, `CHANGELOG.md`, `ROADMAP.md`, `TASKS.md`, the task archive,
`FIXES.md`, `AOT.md`, the design page — and every one of those is kept *current* rather than accumulated.

- **Write a new spec or plan straight into the gitignored `local/superpowers/{specs,plans}/`.** The
  brainstorming and writing-plans skills default to `docs/superpowers/`; redirect them. Only
  `docs/superpowers/INDEX.md` is tracked.
- **Archive when both are true:** nobody needs it to understand how the library works *today*, and nothing
  open still executes from it. Shipping is not sufficient on its own — a part-live plan stays.
- **Before archiving, fill the INDEX's "Conclusions live in" column.** If you cannot, the conclusion was
  never written anywhere durable: put it in the contract / `DECISIONS.md` / `pitfalls.md` / the archive
  first. Keeping the record tracked is not a substitute for recording its conclusion.
- **Never archive maintained state**, and never let the ROADMAP grow a prose section per release — it is one
  line per version, because `CHANGELOG.md` is already the detail.
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

### 2.0.0 is BURNED on nuget.org; never cut it

2.0.0 is permanently taken — published then unlisted on 10 of the 12 package ids, and an unlisted
version's number is never freed. A 2.0.0 release would report success while `--skip-duplicate` silently
published nothing for those 10. The 2.x line resumed at 2.0.1 (tag `v2.0.1`); a future major is 3.0.0.
See `docs/DECISIONS.md` D29.

## Fix log — where it lives here

The canonical `fix-log` skill routes to "the repository's fix log"; here that is **`docs/FIXES.md`**,
newest entry first under a dated heading. It does not exist until the first fix is recorded — create it
with that entry rather than leaving an empty file. Three homes, three jobs, so nothing has to be guessed:

- `docs/DECISIONS.md` — a decision and the reasoning behind it, including one that was rejected.
- `.claude/knowledge/pitfalls.md` — the reusable trap the fix revealed (the skill's own "if the root
  cause is a reusable invariant, also write the rule").
- `docs/FIXES.md` — the per-incident record: symptom, root cause, fix, verification, and the commit that
  introduced the bug.

Do not edit `.claude/skills/fix-log/SKILL.md` to say this — it is canonical and synced, so a local edit
is overwritten on the next `daoris` run. The binding belongs here.

## Package layout — the binding for the canonical rule

Canonical `dotnet-package-layout` states the boundaries; here they resolve to concrete names.

- **Contract in `Lyntai.Core`, implementation in an adapter** (`Lyntai.Storage.Sqlite`,
  `Lyntai.Providers.Default`, `Lyntai.Providers.ExtensionsAi`, `Lyntai.Providers.Local`, …) that
  project-references Core only — or one domain package, such as `Lyntai.Generation` — and **never
  adapter→adapter**. Backends needing nothing extra share `Lyntai.Providers.Default`; one earns its own
  package the moment it drags a native runtime, a platform-specific API, or a dependency a consumer might
  refuse (the footprint test itself is canonical `dotnet-package-layout` §Package boundaries).
  "Most consumers want X" makes it a member of the `Lyntai` metapackage, never a Core dependency.
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
`*Event`, `*Args`, `*Policy` (the full suffix vocabulary with glosses is canonical
`dotnet-package-layout` §Naming).

Do not write "DTO" in prose about these types either — docs and XML comments seed the name back in on
the next change. Say "the row type", "the request record", "the wire type". Entries already in
`docs/task-archive.md` keep their original wording: an archive is a record, not a prescription. And
`dto` is not an abbreviation for `DateTimeOffset` in a local, either.

## Dev loop

- The roster, and what each gate is for, is `CLAUDE.md` §Dev loop; `node devtools/dev.mjs` with no
  argument prints the authoritative list. Do not keep a second copy here — this one went stale by
  omitting `verify`, the gate itself.
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
- **This machine's console is GBK.** Write files with the file-writing tools — in a script,
  `fs.writeFileSync` or an explicit `-Encoding utf8`, which on PowerShell 5 adds a BOM, so write BOM-less
  UTF-8 deliberately wherever the reader is BOM-sensitive. Never build file content by echoing it through
  the console, which lossily mangles CJK and em-dashes. See `.claude/knowledge/pitfalls.md` and canonical
  `windows-machine.md` §Text and encoding.

## Assistant memory

- The 2026-07-22 migration moved the old global memories into `docs/DECISIONS.md` D6–D15 and the
  `.claude/` rules and knowledge. Global memory is kept empty of project facts.
- The library's **own** memory subsystem (`IMemoryStore` / `ICuratedMemoryStore` / `ISemanticMemory`) is a
  separate thing entirely — the canonical rule is about the assistant's memory, not the product's.
