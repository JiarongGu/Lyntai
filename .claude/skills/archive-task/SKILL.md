---
name: archive-task
description: Use when a task in tasks.md is complete (implemented, tested, committed, verified) and needs to be moved out of the active backlog. Moves the entry from tasks.md into docs/task-archive.md per the task-lifecycle rule, so tasks.md holds only open work.
---

# Archive a completed task

Read `.claude/rules/task-lifecycle.md` first. `tasks.md` is the ACTIVE backlog (open tasks only); the
completed record lives in `docs/task-archive.md`. Completing a task means MOVING it, not checking it off
in place.

## When

A `tasks.md` entry is fully done: implemented, its tests pass, it's committed, and `node devtools/dev.mjs
verify` is green. (If it's not actually done, leave it `- [ ]` in `tasks.md`.)

## Steps

1. **Confirm done.** The work is committed and `dev.mjs verify` (or at least build + test + relevant e2e)
   is green. Don't archive unverified work.
2. **Cut from `tasks.md`.** Remove the task's entry. If its whole `## Part N` group is now empty, remove the
   group heading too. Then fix the `## Active backlog` section — set it to `_None …_` if nothing is open,
   and make sure no stale banner claims "all done" over remaining open items (or vice-versa).
3. **Paste into `docs/task-archive.md`.** Under the matching `## Part N — <theme>` heading (create it at the
   end if the archive doesn't have that Part yet), append the ORIGINAL task text verbatim, then a line:
   `✅ done <YYYY-MM-DD> — <Outcome>` where Outcome is one line: what shipped + where (files/API/migration).
   Use the real date (today's date from the session context), not a relative one.
4. **Don't duplicate.** The user-facing summary belongs in `CHANGELOG.md` (release log); the archive is the
   per-task why/how. Don't restate release notes — link if useful.
5. **Verify the docs still read straight.** Both files parse as Markdown; `tasks.md` shows only open work;
   the archive entry is under the right Part with a date + Outcome.

## Don't

- Don't leave a completed `[x]` in `tasks.md` — move it.
- Don't delete a completed task without archiving it — the archive is the record.
- Don't rewrite the archived task text — preserve it (add the Outcome line, don't edit the original).
