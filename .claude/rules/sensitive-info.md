---
name: sensitive-info
description: No dev-machine paths, no private repo names, no credentials in tracked files or commit messages; a committed leak is a history problem.
applies_when: writing any tracked file or commit message, or rewriting history
---

# Sensitive info — keep machine specifics and private names out of tracked files

**Never put a developer-machine absolute path, a private repository's name, or any credential into a
tracked file or a commit message.**

A repository shaped to be published carries its history with it. A machine path or a private project name
is invisible to the person who wrote it and obvious to everyone who reads it afterwards — and **once
committed it lives in the history, where deleting the line does not remove it.** Commit messages are
history too, and they are the easiest place to forget this: the change itself gets reviewed, the message
rarely does.

## How to apply

- Use repository-relative paths, or a neutral placeholder, in every tracked file.
- Refer to sibling projects neutrally unless the project is genuinely public under that name.
- Keep private context — real names, machine paths, tokens — in an untracked directory.
- Credentials belong in the environment or a secret store, never in a file the repository tracks. Tests use
  a deterministic stub, never a real key.
- **A leak that is already committed is a HISTORY problem, not a working-tree problem.** It needs a history
  rewrite, and a backup before you start — an edit only hides it from the current checkout.

The guard that enforces this here, and the one exception for sibling NAMES, is `repo-mechanics.md`
§Sensitive info.
