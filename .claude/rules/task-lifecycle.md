---
name: task-lifecycle
description: The backlog holds OPEN work only and never summarizes the archive; a finished task MOVES to it; a blocked item names its blocker's KIND.
applies_when: adding or finishing a task, editing the backlog, or labelling something blocked
---

# Task lifecycle — the backlog is open work; finished work moves to the archive

**When a task is genuinely finished its entry is *removed* from the backlog and *appended* to the archive —
not checked off in place.** A checked-off line buries the one thing the file exists to answer: what is
left? The archive gains a per-task history the line never carried anyway.

- **Backlog:** open tasks only, as checklist items grouped by theme, with a `file:line` where known.
- **On completion — move, don't tick.** Cut the entry out, paste it into the archive under the right
  heading, add the completion date plus a one-line outcome, and preserve the original wording.
- **Three records, three jobs, no duplication:** the backlog is what is still TODO, the archive is the
  per-task history, the changelog is the release-facing log.
- **Keep the summary honest.** If the top of the backlog claims everything is done, that must be true.
  Never leave a stale "all done" banner over open items, nor open items under a "done" banner.
- Picking up work you cannot finish now? Leave it open. That is exactly what the backlog is for.
- **Never let an ALWAYS-LOADED file summarize the backlog.** The banner is amended in place every time an
  item moves, so any copy goes stale at exactly that moment — and a copy in `CLAUDE.md` is the worst case,
  re-read at the start of every session while the file it duplicates is not. The auto-loaded file ROUTES
  ("read the banner") and states only what cannot rot; it never carries the count or the list.
- **Never let the backlog SUMMARIZE the archive.** A running tally of what closed is the same accumulation
  one level up: unbounded, answering a question the archive already answers, pushing the open items down.

## An archive entry is an OUTCOME and a POINTER, not a write-up

**What the task DID, what it decided, and where the detail lives.** The archive is reached by `Part N`
lookup and never read end to end, so its LENGTH is cheap — **a second copy of a number is not.** If the
entry and another maintained document both carry the measurement, one of them is wrong as soon as anything
is retracted, and that has already cost a two-place edit on a retraction.

Route by kind, as `persist-working-state.md` §Route by KIND says: a measurement and its caveats go to the
record that owns them and the entry keeps the headline plus a pointer; a trap to `pitfalls.md`; a
per-incident fix to `docs/FIXES.md`; a real choice to `docs/DECISIONS.md`.

**The test: strike every sentence a reader could get from the document that owns it.** What survives is the
entry. Roughly ten lines does that; `check-archive` gates it, and **relocate before deleting** — several
long entries are the only maintained home for a trap.

## A blocked item names its blocker's KIND, and is re-checked against that kind

**"Blocked" is a claim with an expiry date, and the check that refutes it is not always the one you ran
last time.** A blocker is usually the TREE (a member that does not exist yet), the ENVIRONMENT (a key, an
installed tool, a model on disk, a running service), a DECISION nobody has taken, or DATA only a deployment
can produce. Record which, because **each is refuted by looking somewhere different** — a careful re-check
that reads the tree when the blocker is the machine is honest about what it checked and still wrong about
the conclusion (`pitfalls.md` §Environment / tooling).

**Blocked is a property of an ITEM, never of a Part**, so it is recorded on the item: every open `- [ ]` in
`TASKS.md` carries `<!-- item: state=… kind=… needs="…" -->` and the roster at the head of that file is
GENERATED from those markers (`check-backlog`, **D111**). A heading is not a state. `watch` and
`decision-only` exist because neither is startable and neither is blocked on anything a re-check could
clear: one waits on recurrence, the other on a ruling.

- **Say what would unblock it, concretely enough to test.** "Needs a real embedding model" is testable;
  "needs more work" is not, and neither is a missing instrument this repository could simply build.
- **A cleared blocker does not always mean a finished item.** Say which of the two moved, or the next
  reader assumes both did.
