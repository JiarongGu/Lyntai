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
2. **Cut the item from `TASKS.md`.** If its `## Part N` now holds no open `- [ ]`, delete the heading
   (`check-backlog` fails an empty Part), then run `node devtools/dev.mjs check-backlog --write` — the roster
   at the head of the file is GENERATED from the item markers (**D111**), never edited by hand.
3. **Write a COMPRESSED entry into `docs/task-archive.md`** — a `## Part N — <theme> (<date>)` heading, then
   `✅ done <YYYY-MM-DD> — **Outcome:**` what shipped and where (files/API/migration), plus anything the task
   got WRONG that the next reader needs. Then the task's item titles as bullets. Use the real date (today's
   date from the session context), not a relative one.
   - **Keep the heading and the outcome, not the original text**; git history holds it. A conclusion that
     must outlive the Part goes to `docs/DECISIONS.md`, `.claude/knowledge/pitfalls.md` or the design
     contract, and `check-archive` bounds the entry's length.
   - **Take the next number above the highest in EITHER file; the archive never renumbers.** The two files
     number independently, so cite `` `TASKS.md` Part N `` or `` `docs/task-archive.md` Part N ``, never a
     bare Part (`task-lifecycle.md`).
   - **Where a NEW Part goes: at the end of the file.** Parts are appended in COMPLETION order, not numeric
     order, so a lower number arriving after a higher one is correct and must not be re-sorted.
   - **Then check nothing dangles**: `node devtools/dev.mjs check-links` reads a `Part N` reference against
     whichever record actually declares it, and archiving is the move that breaks those.
4. **Don't duplicate.** The user-facing summary belongs in `CHANGELOG.md` (release log); the archive is the
   per-task why/how. Don't restate release notes — link if useful.
5. **Verify the docs still read straight.** `TASKS.md` shows only open work and its generated roster agrees
   (`check-backlog`); the archive entry has a date and an Outcome.

## Don't

- Don't leave a completed `[x]` in `TASKS.md` — move it.
- Don't delete a completed task without archiving it — the archive is the record.
- Don't paste the original entry in full — the archive is compressed, and git history holds the full text.
- Don't re-sort the archive into numeric order, and don't edit an existing entry to match today's
  vocabulary: it is a record, accurate BY using the wording of its day, and `check-docs` exempts it for
  exactly that reason.
