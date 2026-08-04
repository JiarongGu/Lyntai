# Task lifecycle — `TASKS.md` is the ACTIVE backlog; completed work moves to the archive

`TASKS.md` is a *living backlog of open work*, not a growing checklist of everything ever done. It stays
small and scannable so the next open task is obvious. The completed record lives elsewhere.

## The rule

- **`TASKS.md` holds OPEN tasks only.** Add new work here as checklist items (`- [ ] **id** …`), grouped
  under a `## Part N — <theme>` heading, with a `file:line` where known.
- **On completion, MOVE — don't just check off.** When a task is fully done (implemented, tested,
  committed, and verified), **remove its entry from `TASKS.md`** and **append it to
  `docs/task-archive.md`** with the completion date and a one-line **Outcome** (what shipped + where).
  Don't accumulate `[x]` items in `TASKS.md`.
- **Three records, three jobs, no duplication:**
  - `TASKS.md` — what's still TODO (open backlog).
  - `docs/task-archive.md` — the per-task history (why/how each closed item was done; the frozen plan).
  - `CHANGELOG.md` — the release-facing, user-visible log (per `VersionPrefix` release). Write under
    `## Unreleased`; the release workflow **stamps that heading** with the version + date (`node
    devtools/dev.mjs changelog --fix`), so never hand-stamp it. Want a titled release? Pre-title the
    heading — `## Unreleased — <title>` becomes `## X.Y.Z — <title> (<date>)`.
- **NEVER hand-edit `<VersionPrefix>`** (`src/Directory.Build.props`) or the `## Unreleased` heading. The
  release workflow bumps the version **from whatever that file currently says**, so a manual bump silently
  moves the baseline and the next release publishes the version AFTER the intended one — the skipped version
  is simply gone (this happened in a sibling repo; `docs/DECISIONS.md` D25). Both edits are blocked by the
  `check-version-bump` pre-commit guard, and `node devtools/dev.mjs doctor` fails when `VersionPrefix` no
  longer matches the newest `v*` tag. Releasing, or repairing a botched release? `LYNTAI_RELEASE=1`.
- **The next MAJOR is `2.0.1`, not `2.0.0`** — 2.0.0 is already taken (published then unlisted) on 10 of the
  12 package ids, and an unlisted version's number is never freed. A 2.0.0 release would report success while
  `--skip-duplicate` silently published nothing for those 10. Cut it with an explicit `version: 2.0.1` +
  `bump: none` on the release workflow. See `docs/DECISIONS.md` D29.
- **Keep the top banner honest.** The `## Active backlog` section reflects reality — `_None …_` when empty;
  never a stale "all done" banner over open items, nor open items under a "done" banner.

## How to apply

- Finishing a task? In the SAME change (or its follow-up doc commit): cut the entry out of `TASKS.md`,
  paste it under the right Part heading in `docs/task-archive.md`, and add `✅ done <YYYY-MM-DD> — <Outcome>`.
  Preserve the original task text so the archive stays a faithful record.
- Use the **`archive-task`** skill for the mechanical move.
- Never delete a completed task outright (the archive is the record) and never leave a completed task in
  `TASKS.md` (the backlog must show only open work).
- Adding a task mid-work you can't finish now? Leave it `- [ ]` in `TASKS.md` — that's exactly what the
  backlog is for.
