---
name: doc-loader
description: Load the documents a task actually needs before touching code — the repository's own doc router plus every on-demand knowledge document whose "applies when" matches. Use at the START of any non-trivial task, because on-demand documents are not auto-loaded and an unread match is a missing contract.
---

# doc-loader

The knowledge tier is deliberately **not** auto-loaded, so the context stays small. The cost of that
choice is this step: if you do not load what the task touches, you will miss an invariant that someone
already paid to learn.

## Steps

1. **The routing table.** Read `.claude/rules/RULES_INDEX.md`'s knowledge table and load every document
   whose *applies when* matches the task — and only those; bulk-loading defeats the purpose of a router.
   The table is hand-maintained, so a document missing from it routes nothing: add its row when you add
   one. The rules tier is already in context.
2. **Private context.** If the task touches machine specifics, real paths, or another repository by
   name, read the untracked local notes rather than guessing.
3. **Report** in two to four lines: what you loaded, and the constraints it imposes here. If nothing
   matched, say so and proceed.

## Why

An unread match is indistinguishable from a rule that does not exist, right up until it is violated.
Reporting what you loaded makes that visible while it is still cheap to correct — and makes a *silent*
miss impossible to mistake for a considered decision.

Load only what the task touches. A step that routinely loads everything will be skipped, and then the
invariants go unread anyway.
