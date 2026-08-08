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

| Date | Topic | Shipped in | Spec | Plan | Conclusions live in |
|---|---|---|---|---|---|
| 2026-08-08 | Named memory-engine seam (MEM1) | 2.5.0 | ✓ | ✓ | design §5.7 · D48 · archive Part 46 |
| 2026-08-08 | Graph memory engine (MEM2) | 2.5.0 | ✓ | ✓ | design §5.7 · D49–D50 · archive Parts 47–52 |
| 2026-08-08 | Graph memory on SQLite + Postgres (MEM2b) | 2.5.0 | — | ✓ | `.claude/knowledge/storage.md` · archive Part 48 |
| 2026-08-05 | Provider pool / lifetime seam | 2.2.0 | ✓ | ✓ | D37 · archive Part 37 |
| 2026-08-04 | The 2.0.1 package restructure | 2.0.1 | — | ✓ | design §3 amendment · D31–D34 · archive Part 32 |
| 2026-07-28 | 1.0 readiness | 1.0.0 | ✓ | ✓ | ROADMAP § v1.0.0 · D21 |
| 2026-07-27 | Curated memory metadata catalog | 0.31.0 | ✓ | — | CHANGELOG 0.31.0 · archive |
| 2026-07-19 | Agent-session surface | 0.28.5 | ✓ | — | D42 · archive · CHANGELOG 0.28.5 |

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
inbound reference, and check nothing dangles. The reasoning is `docs/DECISIONS.md` **D52**; the operational
rule is `.claude/rules/repo-mechanics.md` § Documents have the same lifecycle as tasks.
