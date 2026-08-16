---
name: archive-task
description: Use when a task in TASKS.md is complete (implemented, tested, committed, verified) and needs to be moved out of the active backlog. Moves the entry from TASKS.md into docs/task-archive.md per the task-lifecycle rule, so TASKS.md holds only open work.
---

# Archive a completed task

Read `.claude/rules/task-lifecycle.md` first. `TASKS.md` is the ACTIVE backlog (open tasks only); the
completed record lives in `docs/task-archive.md`. Completing a task means MOVING it, not checking it off
in place.

## When

A `TASKS.md` entry is fully done: implemented, its tests pass, it's committed, and `node devtools/dev.mjs
verify` is green. (If it's not actually done, leave it `- [ ]` in `TASKS.md`.)

## Steps

1. **Confirm done.** The work is committed and `dev.mjs verify` (or at least build + test + relevant e2e)
   is green. Don't archive unverified work.
2. **Cut from `TASKS.md`.** Remove the task's entry. If its whole `## Part N` group is now empty, remove the
   group heading too. Then fix the `## Active backlog` section — set it to `_None …_` if nothing is open,
   and make sure no stale banner claims "all done" over remaining open items (or vice-versa).
3. **Paste into `docs/task-archive.md`.** Under the matching `## Part N — <theme>` heading, append the
   ORIGINAL task text verbatim, then a line: `✅ done <YYYY-MM-DD> — <Outcome>` where Outcome is one line:
   what shipped + where (files/API/migration). Use the real date (today's date from the session context),
   not a relative one.
   - **Part numbers are allocated across BOTH files.** An open `## Part N` in `TASKS.md` and an archived
     `## Part N` must never be the same N, or every cross-reference to "Part N" is ambiguous. This
     happened on 2026-08-05 and the open part was renumbered 39→41. Take the next free number across both
     files. The ARCHIVE never renumbers — it is history, and history does not get renumbered.
   - **Where a NEW Part goes:** after the last existing `## Part …` heading and **before** the closing
     `## Notes for the implementer` section — Parts are appended in COMPLETION order, not numeric order,
     so "at the end of the file" would land it after the closing notes.
4. **Don't duplicate.** The user-facing summary belongs in `CHANGELOG.md` (release log); the archive is the
   per-task why/how. Don't restate release notes — link if useful.
5. **Verify the docs still read straight.** Both files parse as Markdown; `TASKS.md` shows only open work;
   the archive entry is under the right Part with a date + Outcome.

## Don't

- Don't leave a completed `[x]` in `TASKS.md` — move it.
- Don't delete a completed task without archiving it — the archive is the record.
- Don't rewrite the archived task text — preserve it (add the Outcome line, don't edit the original).
