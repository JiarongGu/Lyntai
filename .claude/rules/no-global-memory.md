---
name: no-global-memory
applies_when: about to save a project fact to the assistant's global or cross-project memory
enforces: project facts live in the repository, versioned and reviewable; global memory is user preferences only
---
<!-- daoris: core/core/rules/no-global-memory.md @ 0.0.1 — canonical; edit via `daoris upstream` -->

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
| The person's role, tastes, and preferences — facts about *them* | Global memory — the only thing that belongs there |
| An always-on convention every task needs | The always-loaded rules directory |
| A deep dive read only when touching that area | The on-demand knowledge directory |
| A load-bearing decision and its reasoning | The decisions record |
| A repeatable procedure, including one governing how the assistant responds | A skill in the repository |
| A user-visible change | The changelog |

**The test is not the topic — it is whether a fresh clone would be defective without it.** "They prefer
terse answers" is a taste, and it travels with the person. A *protocol* for terse answers — one with
carve-outs saying never to compress a destructive-action warning, and never to abbreviate an identifier
or a commit message — is doctrine, however much it looks like a preference. Losing it on a fresh clone
loses the carve-outs, and the carve-outs are the part that was learned the hard way.

Apply the same test to anything the tooling offers to remember automatically. A per-project memory the
assistant maintains outside the repository is convenient and still fails every clause above: a teammate
never sees it, review never touches it, and moving the project loses it.

- **Learned something durable about this project? Write it to the repository**, in the row above that
  fits. If it is a new convention, add the document and its index row; if it is a decision, add it to the
  decisions record with the reason.
- **About to save a `project`- or `feedback`-shaped memory? Stop** — that is a repository document.
- **Keep global memory clean of project facts,** and migrate any that leaked in.
- A product's own memory feature is a different thing entirely; this rule is about the assistant's.
