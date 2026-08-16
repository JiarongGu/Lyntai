---
name: doc-loader
description: Load the documents a task actually needs before touching code — the repository's own doc router plus every on-demand knowledge document whose "applies when" matches. Use at the START of any non-trivial task, because on-demand documents are not auto-loaded and an unread match is a missing contract.
---

# doc-loader

The knowledge tier is deliberately **not** auto-loaded, so the context stays small. The cost of that
choice is this step: if you do not load what the task touches, you will miss an invariant that someone
already paid to learn.

## Steps

1. **The repository's own documents.** Find its documentation router — the table or index that maps a
   task to the one or two documents worth reading — and read only the entries that match. Bulk-loading
   defeats the purpose of a router.
2. **The generated index.** Open the always-loaded rules index. The rules tier is already in context;
   scan the **knowledge** table's *applies when* column against the task and read every matched
   document. This index is generated from what is actually on disk, so it is the exhaustive list — any
   shortcut table elsewhere is a convenience, not the registry.
3. **Private context.** If the task touches machine specifics, real paths, or another repository by
   name, read the untracked local notes rather than guessing.
4. **Report** in two to four lines: what you loaded, and the constraints it imposes here. If nothing
   matched, say so and proceed.

## Why

An unread match is indistinguishable from a rule that does not exist, right up until it is violated.
Reporting what you loaded makes that visible while it is still cheap to correct — and makes a *silent*
miss impossible to mistake for a considered decision.

Load only what the task touches. A step that routinely loads everything will be skipped, and then the
invariants go unread anyway.
