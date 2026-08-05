---
name: file-tool-discipline
applies_when: inspecting files, or running a destructive or irreversible command
enforces: use the dedicated read/search/find tools, not shell equivalents; never route a command through a side channel to skip approval
---
<!-- daoris: core/core/rules/file-tool-discipline.md @ 0.0.1 — canonical; edit via `daoris upstream` -->

# Use the dedicated file tools — and never evade the approval gate

**Inspect files with the dedicated read, search, and find tools rather than their shell equivalents.
Reserve the shell for genuine shell work. Never route a command through a side channel to avoid an
approval prompt.**

## Why

The dedicated tools are purpose-built for inspection: clickable file-and-line results, integration with
the approval system, fast indexed search, and no prompt for a read that was never risky. Reaching for a
shell command to do the same job is worse on every axis, and on a stricter policy it also prompts — so
the discipline removes the friction at its source rather than working around it.

The second half matters more. Where the shell is broadly permitted, destructive commands stop prompting —
which makes this a safety rule, not an ergonomics one. And routing a command through some other channel
specifically to skip a prompt is not "reducing friction"; it is circumventing a safety control.

## How to apply

- **Reading a file → the read tool. Searching content → the search tool. Finding files → the find tool.**
- **Genuine shell work → the shell**: builds, tests, version control, package managers, running programs.
- **Destructive commands deserve a pause** precisely when they no longer prompt — recursive deletes,
  hard resets, force pushes, skipping hooks, killing processes, writing to a live datastore. Look before
  you leap, prefer the reversible alternative, and confirm anything irreversible or outward-facing.
- **If something needs approval, let it ask.** Adjust the policy deliberately if the prompt is wrong —
  never hide the command from it.
