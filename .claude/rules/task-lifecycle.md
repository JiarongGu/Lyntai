---
name: task-lifecycle
applies_when: adding or finishing a task, or editing the backlog
enforces: the backlog holds OPEN work only; a finished task MOVES to the archive; three records, three jobs
---

# Task lifecycle — the backlog is open work; finished work moves to the archive

**The backlog file is a living list of what is still to do, not a growing checklist of everything ever
done.** When a task is genuinely finished, its entry is *removed* from the backlog and *appended* to the
archive — not checked off in place.

## Why

A backlog that accumulates completed items stops being scannable, and the next open task stops being
obvious. Checking items off in place feels like record-keeping, but it buries the one thing the file
exists to answer: what is left?

Keeping the record in a separate archive loses nothing. It gains a per-task history — why and how each
item was closed — that a checked-off line never carried anyway.

## How to apply

- **Backlog:** open tasks only, as checklist items grouped by theme, with a `file:line` where known.
- **On completion — move, don't tick.** In the same change or its follow-up: cut the entry out of the
  backlog, paste it into the archive under the right heading, and add the completion date plus a one-line
  outcome. Preserve the original wording so the archive stays a faithful record.
- **Three records, three jobs, no duplication:** the backlog is what is still TODO; the archive is the
  per-task history; the changelog is the release-facing, user-visible log.
- **Keep the summary honest.** If the top of the backlog claims everything is done, that must be true.
  Never leave a stale "all done" banner over open items, nor open items under a "done" banner.
- Picking up work you cannot finish now? Leave it open in the backlog. That is exactly what it is for.
- **And never let an ALWAYS-LOADED file summarize the backlog.** The banner is amended in place every time
  an item moves, so any copy of it goes stale at exactly that moment — and a copy in `CLAUDE.md` is the
  worst case, because it is re-read at the start of every session while the file it duplicates is not. The
  auto-loaded file should ROUTE ("read the banner") and state only what cannot rot; it must not carry the
  count of open items or the list of them. Caught 2026-08-30 while fixing a stale banner reference — the fix
  itself enumerated the three startable items into `CLAUDE.md`, reproducing the defect one level up.

- **Never let the backlog SUMMARIZE the archive.** A running tally of what closed is the same accumulation
  as ticking items in place, one level up: it grows without bound, it answers a question the archive already
  answers, and it pushes the open items further down the file. Keep only what a reader needs to judge what
  is LEFT.

## An archive entry is an OUTCOME and a POINTER, not a write-up

**What the task DID, what it decided, and where the detail lives. If the entry and another maintained
document both carry the measurement, one of them is wrong as soon as anything is retracted.**

Measured 2026-09-02, because the drift is the same one the decision log already grew a ratchet for. Mean
non-blank lines per archive entry, across the file in thirds: **8.0 → 6.1 → 22.2** — a 2.8× growth, and
every one of the ten longest entries is recent. The entry count is not the problem; 132 entries is what 132
finished tasks look like.

**The cost is duplication, not size, and it was paid the same day the growth was measured.** A retraction
landed on a measurement whose narrative sat in BOTH the archive entry and `docs/memory.md` §5, so both had
to be edited; had only one been, the record would now hold two disagreeing accounts of the same run. The
archive is reached by `Part N` lookup and never read end to end, so its length is cheap — but a second copy
of a number is not.

**So route by kind, the same way `persist-working-state.md` already says:**

| what you have | where it goes | what the ARCHIVE entry says |
|---|---|---|
| a measurement, its table, its caveats | the measurement record (`docs/memory.md` §5 here) | the headline figure and a pointer |
| a reusable trap the task revealed | `.claude/knowledge/pitfalls.md` | one clause naming it |
| a per-incident fix | `docs/FIXES.md` | that it happened, and the pointer |
| a choice between real alternatives | `docs/DECISIONS.md` | the outcome |

**The test: strike every sentence a reader could get from the document that owns it.** What survives is the
entry — what the task did, what changed because of it, and what a future reader needs to find the rest.
Roughly ten lines does that; the long entries in this file are not richer, they are copies.

**This is GATED as of 2026-09-04 — `node devtools/dev.mjs check-archive`, part of `verify`** — because the
paragraph above did not hold. It was written from a measurement and the file kept growing: **7.5 → 6.7 →
23.2** two days later, against the 8.0 → 6.1 → 22.2 recorded here. A written-down rule that is still
violated is a missing gate, the same reasoning that produced `check-encoding` and `check-links`. The limit
is **20** non-blank lines — deliberately loose against the "roughly ten" above, since the median entry
already complies and the whole weight is in the tail — and it is a RATCHET: over-limit entries record their
current length in `archiveEntryLengthAllowances`, an allowance looser than the entry needs FAILS, and there
is no escape token. **Relocate before deleting**: several long entries are the only maintained home for a
trap, and cutting one without moving it first loses it.

## A blocked item names its blocker's KIND, and is re-checked against that kind

**"Blocked" is a claim with an expiry date, and the check that refutes it is not always the one you ran
last time.** A blocker is usually one of: the TREE (a member that does not exist yet, a provider nobody
wrote), the ENVIRONMENT (a key, an installed tool, a model on disk, a running service), a DECISION nobody
has taken, or DATA only a deployment can produce. Record which, because each is refuted by looking
somewhere different.

Measured 2026-08-23. A backlog item sat labelled blocked on "a real embedding model" while one was pulled
on the machine the whole time. The previous re-check had been careful and thorough — and it read the
**tree**, because that is what the other five blockers needed, and never asked the **machine**. The
sweep was honest about what it checked and still wrong about the conclusion, which is why the fix is
procedural rather than "look harder".

- **Say what would unblock it, concretely enough to test.** "Needs a real embedding model" is testable;
  "needs more work" is not, and neither is a missing instrument this repository could simply build.
- **Re-check against the blocker's own kind.** An environment blocker needs the environment queried, not a
  `grep`. A tree blocker needs the tree.
- **A cleared blocker does not always mean a finished item** — here what remained was a measurement budget.
  Say which of the two moved, or the next reader assumes both did.
