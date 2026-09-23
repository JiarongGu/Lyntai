# Design records — index

The per-version design records (specs and plans) are **not tracked**. They live in the gitignored
`local/superpowers/{specs,plans}/`, and this file is the tracked list of the ones worth finding again.

**It is NOT a complete inventory, and saying so is the correction.** This line claimed to list "what exists"
until 2026-09-11, when counting found **13 rows against 52 records on disk**. Nothing gates the gap: the
paths are under `local/`, which `check-links` skips by design, so an unindexed record is invisible to every
check here. **A row earns its place by having a CONCLUSION worth reaching** — the rightmost column is the
point of the file, and a record whose conclusions already live in a maintained document needs no row at all.
Do not read a missing row as a missing record.

## Why they are not in the repository

A spec and a plan describe *one version's* work and stop being true the moment it ships. Keeping every one
of them tracked meant the documentation grew by two files per feature forever, and a reader scanning `docs/`
could not tell which files described the library and which described a finished day. The maintained records
are small and fixed in number — the contract, the decisions, the changelog, the backlog and its archive —
and every one of them is *current* by design.

**The trade-off, stated plainly:** a fresh clone does not carry these files, and `check-docs` no longer gates
them (it scans tracked files only). Treat them as a working record, not a contract.

**"Recoverable from git history" is true of exactly one of them, and this line used to say it of all** —
corrected 2026-08-16. A record is only in history if it was once tracked and later moved out of `docs/`, and
just one ever was: the 2026-08-09 measurement below, whose content is at the commit before its removal —

```
git show "$(git rev-list -n1 HEAD -- local/superpowers/records/2026-08-09-memory-policy-measurement.md)^:local/superpowers/records/2026-08-09-memory-policy-measurement.md"
```

Every record written under **Adding one** goes straight into `local/` and is therefore in no history at all:
it exists on the machine that wrote it and nowhere else. That is the intended design — it is also the reason
the **Conclusions live in** column is not paperwork. A conclusion left only in one of these is one disk away
from gone, and no `git show` will bring it back.

**When a design record still matters after
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
| 2026-09-23 | The next release: pre-release review of `v3.2.0..HEAD` and the regular wide check — a session plan | not started | — | ✓ | nothing yet — `TASKS.md` Part 279 holds both items; the plan is the execution route for a fresh session |
| 2026-09-21 | Offline graph consolidation — the measurement (does an offline pass find edges write-time annotation missed?). **Nothing shipped: refuted**, and the plan's own fixture and baseline were wrong — its deviation note says why | not shipped | — | ✓ | **D168** · `docs/task-archive.md` Part 271 · `docs/memory-measurements.md` §5 (`consolidation-offline-pass`) · two `pitfalls.md` instances (a headroom measured beside a PERFECT component; a templated cluster hands a similarity rule its answer) |
| 2026-09-21 | Affect as a memory axis — the measurement (oracle ceiling first, real annotator second). **Nothing shipped: refuted**, and not by the route planned — the oracle stage re-measured `memory-importance`, so the run asked whether arousal PREDICTS relevance instead | not shipped | — | ✓ | **D169** · `docs/task-archive.md` Part 273 · `docs/memory-measurements.md` §5 (`affect-arousal-predicts-evidence`) · `pitfalls.md` (a per-item predictor must be scored within LENGTH) |
| 2026-09-21 | `Lyntai.Storage.FileSystem` — roster-first implementation plan. Shipped five domains; the scan measurement overturned the plan's roster reasoning and its eager guard | unreleased | — | ✓ | **D171** (the shape, the roster, both REJECTED shapes) · `docs/task-archive.md` Part 277 · `docs/memory-measurements.md` §5 (`storage-scan-recall-cost`) · the graph store is `TASKS.md` Part 278 |
| 2026-09-17 | The namespace restructure: one home for calling a backend | unreleased | ✓ | — | **D154** · **D156** · `docs/task-archive.md` Parts 246–250 and 252 · the reusable half is `.claude/knowledge/pitfalls.md` §Refactoring & namespace moves (a prefix says where a type was BORN; check the MIGRATIONS before setting a rename's scope) |
| 2026-09-17 | The provider seam: one interface per SIGNATURE over a generic routed base, so a consuming app can define its own kind | unreleased | ✓ | — | **D153** · **D155**, which closed the half D153 left open · the measured finding it rests on: routing quality varied by KIND — vector had no cooldown or admission, score had no fallback at all · `docs/task-archive.md` Part 251 |
| 2026-09-11 | ONE model serving MANY seams, priced against one per seam (`memory-contention`) | unreleased | ✓ | ✓ | archive Part 190 · `docs/memory-measurements.md` §5 (`contention-mixed-recall-quiet-rerank`) · `pitfalls.md` (router mode is a SUPERVISOR; `--embedding`/`--reranking` are process-wide; a port check must test LISTENING; a bare `HttpClient` leaves proxy resolution on) |
| 2026-09-11 | What `VerdictCombination` costs a READER (`memory-locomo --verdict`) — the backlog predicted a null and the run refuted it | unreleased | ✓ | ✓ | archive Part 191 · `docs/memory-measurements.md` §5 (two rows, partition and `+enginefuse`) · `docs/FIXES.md` (a bench printed a hardcoded caveat about its own output) |
| 2026-08-27 | The gist tier — design, then the support RULE, then a field-research pass. **Nothing shipped: refuted as scoped** | not shipped | 3 records | — | **D94** · **D106** · `docs/memory-measurements.md` §5 (three sweeps + the field survey) · archive Parts 104, 108, 153 |
| 2026-08-26 | The "superhuman memory" proposal — assessment, then Phase 1 and Phase 2 | unreleased | ✓ | ✓ | **D90** · design §5.7.0 · `docs/memory.md` §7 · `docs/FIXES.md` (two entries) · `pitfalls.md` (§Second doors, §Environment, CLI) · `windows-machine.md` · archive Parts 97, 98, 100 · `TASKS.md` Part 99 |
| 2026-08-26 | Graph-engine COST at 1k / 10k / 100k (`memory-scale`, the §7 blind spot) — plus a `--repeat 5` run that settled the read-vs-write-back split the single-cell run could not | unreleased | — | 2 records | `docs/memory.md` §7 · archive Part 97 |
| 2026-08-09 | Ranking × forgetting policy measurement (the D49 falsification pass) | 3.0.0 | — | record | **D49** · `docs/memory-measurements.md` §5 · archive Parts 54–55 (Part 56 is still OPEN) |
| 2026-08-08 | The memory subsystem design PAGE (a published snapshot, not a spec) | 2.5.0 | — | record | design §5.7 · `docs/memory.md` · D39–D42 · archive Parts 46–52 |
| 2026-08-08 | Named memory-engine seam (MEM1) | 2.5.0 | ✓ | ✓ | design §5.7 · D39 · archive Part 46 |
| 2026-08-08 | Graph memory engine (MEM2) | 2.5.0 | ✓ | ✓ | design §5.7 · D40–D41 · archive Parts 47–52 |
| 2026-08-08 | Graph memory on SQLite + Postgres (MEM2b) | 2.5.0 | — | ✓ | `.claude/knowledge/storage.md` · archive Part 48 |
| 2026-08-05 | Provider pool / lifetime seam | 2.2.0 | ✓ | ✓ | D30 · archive Part 37 |
| 2026-08-04 | The media generation platform (Plan 1 of 7) — the last document to leave `docs/` | 2.0.1 | — | ✓ | **D24**/**D25** · design §6 · archive Parts 32–33 · `README.md` §Generation · `TASKS.md` Part 33 (GEN-VERIFY/GEN6/GEN7, which carry the CURRENT framing — see below) |
| 2026-08-04 | The 2.0.1 package restructure | 2.0.1 | — | ✓ | design §3 amendment · D25–D27 · archive Part 32 |
| 2026-07-28 | 1.0 readiness | 1.0.0 | ✓ | ✓ | ROADMAP § v1.0.0 · D16 |
| 2026-07-27 | Curated memory metadata catalog | 0.31.0 | ✓ | — | CHANGELOG 0.31.0 · archive |
| 2026-07-19 | Agent-session surface | 0.28.5 | ✓ | — | D35 · archive · CHANGELOG 0.28.5 |

**Nothing is still tracked but the contract.** `docs/2026-07-17-lyntai-design.md` is maintained state and
never moves; `docs/` otherwise holds only records that are current by design.

**The generation plan's exit is worth reading before you keep a document for its "live half" (D149).** It
sat here for six weeks on the note *"part live — GEN-VERIFY, GEN6 and GEN7 still execute from it"*, and
when that was finally re-read rather than re-asserted, none of the three did: Plan 6 named a streaming
interface **D127** had deleted, Plan 7 predated both the 2026-08-30 3D survey and GEN7a shipping, and
`TASKS.md`'s own item bodies had carried the current framing for weeks. **A "still live" note is a claim
with an expiry date, like a blocker** (`task-lifecycle.md`) — and it expires the same silent way, because
nothing fails when the live half quietly dies. Re-read it; do not re-assert it.

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
