# Design records — index

The per-version design records (specs and plans) are **not tracked**. They live in the gitignored
`local/superpowers/{specs,plans}/`, and this file is the tracked list of what exists.

## Why they are not in the repository

A spec and a plan describe *one version's* work and stop being true the moment it ships. Keeping every one
of them tracked meant the documentation grew by two files per feature forever, and a reader scanning `docs/`
could not tell which files described the library and which described a finished day. The maintained records
are small and fixed in number — the contract, the decisions, the changelog, the backlog and its archive —
and every one of them is *current* by design.

**The trade-off, stated plainly:** a fresh clone does not carry these files, and `check-docs` no longer gates
them (it scans tracked files only). They are recoverable from git history — nothing was destroyed, only
untracked — but treat them as a working record, not a contract. **When a design record still matters after
its version ships, its conclusion belongs in a maintained document**, not in the record: an interface or a
semantic goes in `docs/2026-07-17-lyntai-design.md`, a decision and its reasoning in `docs/DECISIONS.md`, a
reusable trap in `.claude/knowledge/pitfalls.md`, and the per-task history in `docs/task-archive.md`.

## What exists

_A MEASUREMENT RECORD can be listed here too (2026-08-13). The ranking × forgetting measurement was tracked
in `docs/` for four days and then untracked under **D43**'s rule: nothing open executes from it, its
conclusions are in D49, and the current picture of the subsystem is `docs/memory.md`. It sits in
`local/superpowers/records/`. A record of what was measured on one day is the same kind of thing as a spec —
true about that day, not about the library._

| Date | Topic | Shipped in | Spec | Plan | Conclusions live in |
|---|---|---|---|---|---|
| 2026-08-09 | Ranking × forgetting policy measurement (the D49 falsification pass) | 3.0.0 | — | record | **D49** · `docs/memory.md` §5 · archive Parts 54–55 (Part 56 is still OPEN) |
| 2026-08-08 | Named memory-engine seam (MEM1) | 2.5.0 | ✓ | ✓ | design §5.7 · D39 · archive Part 46 |
| 2026-08-08 | Graph memory engine (MEM2) | 2.5.0 | ✓ | ✓ | design §5.7 · D40–D41 · archive Parts 47–52 |
| 2026-08-08 | Graph memory on SQLite + Postgres (MEM2b) | 2.5.0 | — | ✓ | `.claude/knowledge/storage.md` · archive Part 48 |
| 2026-08-05 | Provider pool / lifetime seam | 2.2.0 | ✓ | ✓ | D30 · archive Part 37 |
| 2026-08-04 | The 2.0.1 package restructure | 2.0.1 | — | ✓ | design §3 amendment · D25–D27 · archive Part 32 |
| 2026-07-28 | 1.0 readiness | 1.0.0 | ✓ | ✓ | ROADMAP § v1.0.0 · D16 |
| 2026-07-27 | Curated memory metadata catalog | 0.31.0 | ✓ | — | CHANGELOG 0.31.0 · archive |
| 2026-07-19 | Agent-session surface | 0.28.5 | ✓ | — | D35 · archive · CHANGELOG 0.28.5 |

**Still tracked, deliberately:** `docs/2026-08-04-generation-platform-plan.md` is *part* shipped history and
*part* live — GEN-VERIFY, GEN6 and GEN7 in `TASKS.md` still execute from it. It moves here when its last open
task closes. The contract itself, `docs/2026-07-17-lyntai-design.md`, is maintained state and never moves.

## Adding one

Write the spec and plan straight into `local/superpowers/{specs,plans}/` — the brainstorming and
writing-plans skills default to `docs/superpowers/`, so redirect them. Add a row above when the work ships,
and make sure the **Conclusions live in** column is true before you do: that column is the whole point of
this file, and a row that cannot fill it is a sign the work has not actually been recorded anywhere durable.

## Archiving one that is still in `docs/`

A document leaves `docs/` when **both** are true — shipping alone is not enough:

1. Nobody needs it to understand how the library works **today**, and
2. nothing open still executes from it (a part-live plan stays; see the note above).

Then: fill the **Conclusions live in** column *first*, move the file to `local/superpowers/`, repoint every
inbound reference, and check nothing dangles. The reasoning is `docs/DECISIONS.md` **D43**; the operational
rule is `.claude/rules/repo-mechanics.md` § Documents have the same lifecycle as tasks.

**The last step is now a gate rather than a habit** — `node devtools/dev.mjs check-links`, part of `verify`.
It was added because this very procedure was followed except for that step: untracking the 2026-08-09
measurement record left **six** references to `docs/2026-08-09-…md` alive in maintained state (README ×3,
the design contract, `DECISIONS.md` ×2) plus two in `CHANGELOG.md`'s live `## Unreleased` prefix, and a
reader found them rather than a gate. Repoint to the file's real `local/superpowers/…` path — pointing at
where it actually is beats pointing at where it used to be, even though a fresh clone will not carry it
(the trade-off this document already states above).
