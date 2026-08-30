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
