# devtools — the dev loop, the guard scripts and their tests

## What this folder is

The Node tooling behind `node devtools/dev.mjs <command>`: every gate `verify` runs, the scaffolders, the
benches' drivers and the release helpers. It ships nothing, and it must never need a package the
repository does not already use.

## Conventions here

- **A command is a row in `devtools/commands.mjs`** (plus `devLoopCommands` in `project.config.mjs`); the
  table in `CLAUDE.md` is generated from them, so a new command edits those, never the table.
- **Each guard is tested through a pure function** (`checkDocs(repo, config, log)` and its siblings) with
  the CLI entry a thin wrapper — extract that seam rather than spawning a process.
- **Discovery is by name**: e2e suites are `scripts/e2e/pN.mjs`, guard tests `scripts/__tests__/*.test.mjs`.
  A helper stays out by not matching (`_e2e-common.mjs`, `_fixtures.mjs`); the leading underscore excludes
  nothing on its own.
- **Fixtures live under the gitignored `devtools/_*`, never OS temp**, and never contain a literal the leak
  scanner would flag — synthesize it from parts.
- **Each gate owns its own escape token** (`docs/GATES.md` §Escape tokens and allowances).

## Traps

- Several gates still name `.claude/knowledge/` and treat `.claude/` as the documents' scope
  (`scripts/check-pitfalls.mjs` `RECORD`, `scripts/check-docs.mjs` `IN_SCOPE`, the path pattern in
  `scripts/check-links.mjs`) while the knowledge now lives in `.agents/`: a gate that cannot see a file
  reports exactly like a clean one.
- The script and exit-code traps are `.claude/rules/windows-machine.md` and the shared `windows-machine`
  rule: never read an exit code through a pipe, and never `process.exit()` with a request in flight.

## Checks

- `node devtools/dev.mjs test-devtools` after any change here; it runs first in `verify`.
