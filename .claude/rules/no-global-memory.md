<!-- daoris: core/core/no-global-memory.md @ 0.1.0 — canonical; edit via `daoris upstream` -->
---
name: no-global-memory
applies_when: about to save a project fact to the assistant's global or cross-project memory
enforces: project facts live in the repository, versioned and reviewable; global memory is user preferences only
---

# No global memory for project facts — they belong in the repository

**Never put project-related information into the assistant's global or cross-project memory.** Global
memory is for facts about the *person* — role, communication style, personal preferences — that hold
across every project they touch. Everything about *this* project goes in the repository.

## Why

Four repositories in this family independently wrote a version of this rule, which is the strongest
signal a rule can have.

A fact in global memory is invisible to everyone else who clones the repository, unversioned, unreviewable,
and silently loaded — so it cannot be corrected by review, cannot be traced to the change that motivated
it, and quietly diverges from the code it describes. The same fact in a tracked file is none of those
things. It also survives the assistant: a convention in the repository still works when the tool changes.

## How to apply

| Information | Home |
|---|---|
| The person's role, communication style, personal preferences | Global memory — the only thing that belongs there |
| An always-on convention every task needs | The always-loaded rules directory |
| A deep dive read only when touching that area | The on-demand knowledge directory |
| A load-bearing decision and its reasoning | The decisions record |
| A user-visible change | The changelog |

- **Learned something durable about this project? Write it to the repository**, in the row above that
  fits. If it is a new convention, add the document and its index row; if it is a decision, add it to the
  decisions record with the reason.
- **About to save a `project`- or `feedback`-shaped memory? Stop** — that is a repository document.
- **Keep global memory clean of project facts,** and migrate any that leaked in.
- A product's own memory feature is a different thing entirely; this rule is about the assistant's.
