---
name: repo-mechanics
description: This repository's concrete bindings for the general rules — package names, version authorship, the dev loop, guard scripts, local model servers, scratch paths.
applies_when: applying a general rule in this repository
---

# Repo mechanics — this repository's concrete bindings for the general rules

**The general rules state the principle; this file states how it is enforced HERE.** Where they appear to
disagree, the general rule states the intent and this file states the mechanism.

> **Every file under `.claude/` is this repository's own** — no sync, no upstream, no provenance tier.
> Editing one is an ordinary change, reviewed like any other.

## Sensitive info — the guard that enforces it

- **Install the hook once per clone: `node devtools/dev.mjs install-hooks`.** Nothing warns you it is
  missing, and a committed leak is a history rewrite, not an edit.
- Real private tokens go in the gitignored `local/sensitive-patterns.txt`, one JavaScript regex per line.
- **A sibling project's on-disk PATH is never acceptable anywhere.** Sibling NAMES appear on purpose only
  in the existing provenance records — design §1, the README credits, `docs/DECISIONS.md` — and nowhere
  new: not in code, tests, tooling, or a commit message.

## Task lifecycle — the three files, and version authorship

`TASKS.md` is the open backlog, `docs/task-archive.md` the per-task history, `CHANGELOG.md` the
release-facing log. Use the `archive-task` skill for the move; the discipline is `task-lifecycle.md`.

### A conditional item is not a task — it belongs in the decision record

**An entry whose trigger has not occurred does not go in `TASKS.md`.** Put the option, its trigger and its
constraint in `docs/DECISIONS.md`, and let the backlog hold only work someone could start today. The
backlog exists to answer "what is still to do", and every permanent resident makes that answer worse — and
**a real failure is a better starting point than a speculative one**, so waiting is not merely cheap, it
produces a better design. The test: *could someone begin this today?*

### Documents have the same lifecycle as tasks (D43)

A finished task moves out of the backlog; a finished **document** moves out of `docs/`. Tracked `docs/` is
maintained state, kept *current* rather than accumulated.

- **Write a new spec or plan straight into the gitignored `local/superpowers/{specs,plans}/`** — the
  brainstorming and writing-plans skills default to `docs/superpowers/`, so redirect them. Only
  `docs/superpowers/INDEX.md` is tracked.
- **Archive when both are true:** nobody needs it to understand the library *today*, and nothing open still
  executes from it. Shipping alone is not sufficient — a part-live plan stays.
- **Before archiving, fill the INDEX's "Conclusions live in" column.** If you cannot, the conclusion was
  never written anywhere durable — put it in a maintained record first. **After archiving, repoint every
  inbound reference** (`check-links` gates it).
- **Never archive maintained state**, and never let `docs/ROADMAP.md` grow a prose section per release — it
  is one line per version, because `CHANGELOG.md` is already the detail. Changelog entries go under
  `## Unreleased`; the release workflow stamps that heading.

### Never hand-edit the version

**`<VersionPrefix>` in `src/Directory.Build.props` is written by the release workflow, never by hand** —
the workflow bumps from whatever that file currently says, so a manual bump silently moves the baseline and
the next release publishes the version *after* the intended one, with the skipped one simply gone
(**D19**). Never hand-stamp the `## Unreleased` heading either; both edits are blocked by the
`check-version-bump` pre-commit guard. **2.0.0 is BURNED on nuget.org and must never be cut** — an unlisted
version's number is never freed, so the release would report success while publishing nothing (**D23**).
Releasing, or repairing a botched release, sets `LYNTAI_RELEASE=1`.

### …and never NAME a version that has not shipped

**No document, doc comment, changelog entry or commit message promises a future version number.** The
pipeline decides what the next release is called, at release time, so a number written before that turns a
guess about somebody else's decision into a promise a consumer can hold the library to.

**Say what SHIPPED, never what WILL.** "Through 3.0.0 this returned nothing" is a fact about a released
artifact and stays true; "fixed in 3.1" was false the moment the release was cut as 3.0.1 — write "in the
next release", or just describe the fix.

**Deliberately not gated**, and the reason is worth keeping so nobody builds it and finds out: a scan for
version-shaped tokens drowns in legitimate ones — `net10.0`, a model tag, a vendor's `0.3.40`, every
historical release heading — the shape `pitfalls.md` records as untightenable. The review question is:
*is this number a record or a prediction?*

### Everything before 3.0 is HISTORY — log it, never analyse it

**Nothing is deployed on a pre-3.0 version.** `CHANGELOG.md` records what those releases did and that is
the only place it needs to exist. **The boundary is deliberately 3.0 and does NOT track the current
release** — do not advance it when the version moves. A session must not:

- **justify a design by what an older release preserved** — that is archaeology; say the same thing about
  the code that exists;
- **pay a real cost for pre-3.0 binary compatibility** — an overload added so a pre-compiled caller of the
  old signature keeps resolving is paying for a caller that does not exist;
- **maintain a pre-3.0 document as if it were current** — and when the last reason to keep one expires,
  UNTRACK it rather than re-banner it (**D149**).

**What survives is the FACT, never the diff.** The narrow exception: a fact about a RELEASED artifact stays
sayable — "2.0.0 is burned on nuget.org" is a fact about the feed, not an analysis of an old release.

## Fix log — where it lives here

The fix log is **`docs/FIXES.md`**, written through the `fix-log` skill, whose template is the file's own
entry shape. Route everything else by kind (`persist-working-state.md` §Route by KIND).

**A later fix that corrects an earlier entry writes the correction at that entry's HEAD, never its foot**,
as a blockquote plus a `<!-- keeps: … -->` on the heading saying what still holds — because a reader
arrives INSIDE an entry from a grep, and a superseded entry is usually still the ONLY home of its reusable
half. Deliberately not gated: both candidate signals were measured as noise (`docs/GATES.md` §The
cold-start measurement).

## Package layout — the binding for the general rule

`dotnet-package-layout.md` states the boundaries; here they resolve to concrete names.

- **Contract in `Lyntai.Core`, implementation in an adapter** (`Lyntai.Storage.Sqlite`,
  `Lyntai.Providers.Basic`, `Lyntai.Providers.LlamaSharp`, …) that project-references Core only — or one
  domain package such as `Lyntai.Generation` — and **never adapter→adapter**. "Most consumers want X"
  makes it a member of the `Lyntai` metapackage, never a Core dependency (**D25**/**D26**/**D27**).
- **Every `src/*` is packable**; `samples/` and `tests/` are not. `node devtools/dev.mjs new-package`
  scaffolds into every registry `check-packages` gates — never hand-roll the csproj, the misses are silent.
- **When a package is removed or folded, add its id to `devtools/nuget-unlist.mjs`'s `RETIRED` array.** The
  live roster is derived from `src/*/*.csproj`, so retired ids are the one thing the tree stops
  remembering, and a stale roster skips a live package while reporting a clean run (**D44**).
- **DI-first.** The public entry is `services.AddLyntai(cfg => …)`; adapter packages extend `LyntaiBuilder`
  with `Add*`/`Use*`, and a consumer constructs nothing by hand.

## Naming — the binding for the general rule

**The tree contains zero `Dto` identifiers, and both gates hold it** — `check-api-vocabulary` on the frozen
surface, `check-docs` on prose. The residue worth remembering is what neither sees: **`dto` is not an
abbreviation for `DateTimeOffset` in a local, either.** Do not write "DTO" in prose about these types
(say "the row type", "the request record"), or the name seeds itself back in on the next change; entries
already in `docs/task-archive.md` keep their original wording, because an archive is a record rather than a
prescription. Suffix vocabulary: `dotnet-package-layout.md` §Naming.

## Dev loop

- **The command roster is the generated table in `CLAUDE.md` §Dev loop**; `node devtools/dev.mjs` with no
  argument prints the same list, and `docs/GATES.md` is what each gate is FOR. Do not keep a second copy
  here.
- e2e suites live in `devtools/scripts/e2e/` as `pN.mjs`, discovered by `^p\d+\.mjs$`; the guards' own tests are
  `devtools/scripts/__tests__/*.test.mjs`. **A leading underscore keeps a helper out of a runner's
  discovery** — `_e2e-common.mjs` and `_fixtures.mjs` both rely on it.
- **Each guard is tested through a pure function** (`checkDocs(repo, config, log)` and its siblings) with
  the CLI entry point a thin wrapper — when adding a guard, extract that seam rather than spawning a
  process. A fixture must never contain a literal the leak scanner would flag; synthesize it from parts.
- **Tests:** xUnit. Pure logic is unit-tested with fakes and no I/O; storage against a per-test temp SQLite
  database, created and migrated then deleted; providers against the deterministic provider-stub
  (`LYNTAI_PROVIDER_CMD`), so no test needs a real token.

## Local models — llama.cpp is the standard, Ollama is merely supported

Everything here speaks HTTP on a wire the library ships and does not care which server answers, so this is a
convention about what to REACH FOR, not a constraint the code enforces.

- **The benches default to `http://localhost:8080`** — llama-server's own port. Override with
  `LYNTAI_LIVE_MODEL_URL`; name the models with `LYNTAI_LIVE_EMBED_MODEL` / `LYNTAI_LIVE_CHAT_MODEL`. The
  legacy `LYNTAI_OLLAMA_*` names still work, and are legacy because a variable named after one vendor is a
  claim about the host that the code never makes. Every sweep prints the endpoint it used.
- **The library supports both and prefers neither**: `AddLlamaProvider` beside `AddOllamaProvider`. Which
  one a deployment uses is its own choice (`.claude/knowledge/model-decoupling.md`).
- **Run your OWN server on its own port; never borrow one that happens to be up, never kill one by image
  name, and never infer from a green run that the model you named is the model that answered.** The three
  traps behind that sentence — plus the two servers' silent disagreement about an over-long input — are in
  `.claude/knowledge/pitfalls.md` §Environment / tooling.

## Scratch and working files

- Scratch, probes and dumps go under the gitignored `devtools/_*`; reusable tooling goes in `devtools/`,
  tracked.
- **Line endings are LF, declared in the tracked `.gitattributes`** (`* text=auto eol=lf`, **D95**) — in
  the index AND the working tree, so `git ls-files --eol` reads `i/lf w/lf` on every tracked file and any
  other line is a finding. The attribute **overrides** `core.autocrlf`, which is the point of having one.
  Repair a stray CRLF with a bytes replace, never a PowerShell round-trip.
- **This machine's console is GBK.** Write files with the file-writing tools — in a script,
  `fs.writeFileSync` or an explicit `-Encoding utf8`, which on PowerShell 5 adds a BOM, so write BOM-less
  UTF-8 deliberately where the reader is BOM-sensitive. Never build file content by echoing it through the
  console (`windows-machine.md` §Text and encoding).
