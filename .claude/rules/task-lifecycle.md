# Task lifecycle — `tasks.md` is the ACTIVE backlog; completed work moves to the archive

`tasks.md` is a *living backlog of open work*, not a growing checklist of everything ever done. It stays
small and scannable so the next open task is obvious. The completed record lives elsewhere.

## The rule

- **`tasks.md` holds OPEN tasks only.** Add new work here as checklist items (`- [ ] **id** …`), grouped
  under a `## Part N — <theme>` heading, with a `file:line` where known.
- **On completion, MOVE — don't just check off.** When a task is fully done (implemented, tested,
  committed, and verified), **remove its entry from `tasks.md`** and **append it to
  `docs/task-archive.md`** with the completion date and a one-line **Outcome** (what shipped + where).
  Don't accumulate `[x]` items in `tasks.md`.
- **Three records, three jobs, no duplication:**
  - `tasks.md` — what's still TODO (open backlog).
  - `docs/task-archive.md` — the per-task history (why/how each closed item was done; the frozen plan).
  - `CHANGELOG.md` — the release-facing, user-visible log (per `VersionPrefix` release).
- **Keep the top banner honest.** The `## Active backlog` section reflects reality — `_None …_` when empty;
  never a stale "all done" banner over open items, nor open items under a "done" banner.

## How to apply

- Finishing a task? In the SAME change (or its follow-up doc commit): cut the entry out of `tasks.md`,
  paste it under the right Part heading in `docs/task-archive.md`, and add `✅ done <YYYY-MM-DD> — <Outcome>`.
  Preserve the original task text so the archive stays a faithful record.
- Use the **`archive-task`** skill for the mechanical move.
- Never delete a completed task outright (the archive is the record) and never leave a completed task in
  `tasks.md` (the backlog must show only open work).
- Adding a task mid-work you can't finish now? Leave it `- [ ]` in `tasks.md` — that's exactly what the
  backlog is for.
