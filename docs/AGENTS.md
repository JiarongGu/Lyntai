# docs — the contract, the guides and the records

## What this folder is

The design contract, the consuming guides, the memory contract and its evidence, and the records
(decisions, archive, fixes). `docs/documents.md` is the router: every document, its kind and its standing.
Every document here is maintained state that the prose gates read, except what they list as historical.

## Conventions here

- **Every fenced `csharp` block compiles** (`check-samples`); prefer `compile-given` to `compile-skip`, and
  never put a compiled sample under `## Unreleased`.
- **Repoint a citation, never renumber its target**: a renumbered `§` resolves to the wrong section
  silently. Write `` `TASKS.md` Part N `` or `` `docs/task-archive.md` Part N ``, never a bare Part.
- **A counted claim moves VERBATIM**, and only into a file the gates scan (`docs/GATES.md` §Editing prose
  without turning `verify` red).
- **`docs/DECISIONS.md`'s index is generated** (`decisions-index`), and an entry is a choice between real
  alternatives (`.claude/rules/persist-working-state.md` §Route by KIND).
- **`docs/task-archive.md` and released `CHANGELOG.md` sections are history**: they keep their wording.
- A new spec or plan goes in the gitignored `local/superpowers/`; only `docs/superpowers/INDEX.md` is
  tracked (`.claude/rules/repo-mechanics.md` §Documents have the same lifecycle as tasks).

## Traps

- A sentence rewritten here can turn `verify` red through `check-counts`, `check-links` or `check-docs`
  without touching code; `docs/GATES.md` names which gate reads what.

## Checks

- `node devtools/dev.mjs check-links`, `check-docs`, `check-counts` and `check-samples` after a change
  here; `verify` before calling it done.
