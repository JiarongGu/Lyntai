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
> `add-scorer`, `add-storage-backend`, `archive-task`) and the six local knowledge documents
> (`extending-lyntai`, `generic-library`, `input-is-thinking-not-doctrine`, `llm-and-router`, `pitfalls`,
> `storage`) carry no provenance header and no `daoris.lock` entry — the same exposure as this file
> (measured for the rules tier; inferred for the other two).

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

### A conditional item is not a task — it belongs in the decision record

**An entry whose trigger has not occurred does not go in `TASKS.md`.** Put the option, its trigger and its
constraint in `docs/DECISIONS.md`, and let the backlog hold only work someone could start today.

The measured case is the JSON source-gen envelopes item (D14), which sat in the open backlog for the
library's entire life waiting on an envelope-parsing bug that never materialized — a duplicate of D14's own
closing sentence. Removed 2026-08-11.

Two reasons, and the second is the better one:

- The backlog exists to answer **"what is still to do"**, and every permanent resident makes that answer
  worse. `task-lifecycle.md` says the same thing about *completed* items; a never-startable item is the same
  defect from the other end.
- **A real failure is a better starting point than a speculative one.** Built on a guess, the envelope types
  would be shaped against vendor formats documented as drifting; built against an actual break, they are
  shaped by the field that actually moved. Waiting is not merely cheap — it produces a better design.

The test: *could someone begin this today?* If the honest answer is "only once X happens", X is the record's
concern, not the backlog's.

### Documents have the same lifecycle as tasks (D43)

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
- **After archiving, repoint every inbound reference — `node devtools/dev.mjs check-links` is the gate.**
  Added 2026-08-14 because this step was skipped on the one archive that has happened: six references to
  `docs/2026-08-09-…md` stayed alive in maintained state (README, the design contract, `DECISIONS.md`) and a
  reader, not a gate, caught them. Point at the file's real `local/superpowers/…` path.
- **Never archive maintained state**, and never let the ROADMAP grow a prose section per release — it is one
  line per version, because `CHANGELOG.md` is already the detail.
- Write changelog entries under `## Unreleased`. The release workflow stamps that heading with the
  version and date (`node devtools/dev.mjs changelog --fix`) — never hand-stamp it. A titled release is
  pre-titled as `## Unreleased — <title>`.

### Never hand-edit the version

**`<VersionPrefix>` in `src/Directory.Build.props` is written by the release workflow, never by hand.**
The workflow bumps from whatever that file currently says, so a manual bump silently moves the baseline
and the next release publishes the version *after* the intended one — the skipped version is simply gone.
This happened in a sibling repository; see `docs/DECISIONS.md` D19.

- Both that edit and a hand-stamped `## Unreleased` heading are blocked by the `check-version-bump`
  pre-commit guard.
- `node devtools/dev.mjs doctor` fails when `VersionPrefix` no longer matches the newest `v*` tag.
- Releasing, or repairing a botched release, sets `LYNTAI_RELEASE=1`.

### 2.0.0 is BURNED on nuget.org; never cut it

2.0.0 is permanently taken — published then unlisted on 10 of the 12 package ids, and an unlisted
version's number is never freed. A 2.0.0 release would report success while `--skip-duplicate` silently
published nothing for those 10. The 2.x line resumed at 2.0.1 (tag `v2.0.1`); a future major is 3.0.0.
See `docs/DECISIONS.md` D23.

### The feed shows 2.0.1+ only — unlisting is `nuget-unlist.mjs`, and its roster is derived

Everything below 2.0.1 is unlisted on nuget.org (D44), so `Lyntai.Providers.ClaudeCli`, `.CodexCli` and
`.OpenAiCompatible` have no listed version at all — they were folded into `Lyntai.Providers.Default` at
2.0.1. Unlisting hides a version from search and from *range* resolution but never breaks a pinned
consumer, and never frees the number.

- The tool is `node devtools/nuget-unlist.mjs [--below <version>] [--only <id>]` — **dry run by default**;
  add `--apply` to act. Key from `NUGET_API_KEY` or `--api-key <key>`, minted on nuget.org scoped `Unlist`
  + glob `Lyntai.*`. Prefer the environment variable — `--api-key` puts the key in shell history — and
  never commit one. The tool redacts the key from its own error output.
- **The roster is derived from `src/*/*.csproj`**, not hand-listed — only retired ids are hand-kept, in the
  script's `RETIRED` array. Add an id there whenever a package is removed or folded, because that is the
  one thing the tree stops remembering. A hand-maintained roster already went stale once and would have
  skipped a live package while reporting a clean run (D44).
- Deprecation (as opposed to unlisting) is **web-UI only** — no API, so it is not scriptable.

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
  See `docs/DECISIONS.md` D25 (the split), D26 (the bundle budget), D27 (many small packages).
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
- **The guard scripts have their own tests**, in `devtools/scripts/__tests__/*.test.mjs`, run by
  `node --test` via `node devtools/dev.mjs test-devtools` and FIRST in `verify`. The shared fixture helper
  is `_fixtures.mjs` — the leading underscore keeps it out of the runner's discovery, the same trick
  `e2e/_e2e-common.mjs` uses. `devtools/scripts/` stays a roster of executable gates, which is why the
  tests sit in a subdirectory rather than beside them. **Each guard is tested through a pure function**
  (`checkDocs(repo, config, log)` and its siblings), with the CLI entry point a thin wrapper — when adding
  a guard, extract that seam rather than testing by spawning a process.
  Two traps already paid for: `node --test <dir>` does NOT work on Node 24 (a bare directory is loaded as a
  module — it needs a glob), and a fixture must never contain a literal the leak scanner would flag, so
  synthesize values from concatenated parts.
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

- The 2026-07-22 migration moved the old global memories into `docs/DECISIONS.md` D5–D12 and the
  `.claude/` rules and knowledge. Global memory is kept empty of project facts.
- The library's **own** memory subsystem (`IMemoryStore` / `ICuratedMemoryStore` / `ISemanticMemory`) is a
  separate thing entirely — the canonical rule is about the assistant's memory, not the product's.
