---
name: task-lifecycle
applies_when: adding or finishing a task, or editing the backlog
enforces: the backlog holds OPEN work only; a finished task MOVES to the archive; three records, three jobs
---
<!-- daoris: core/core/rules/task-lifecycle.md @ 0.0.1 — canonical; edit via `daoris upstream` -->

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
