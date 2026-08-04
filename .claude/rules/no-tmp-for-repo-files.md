<!-- daoris: core/core/no-tmp-for-repo-files.md @ 0.1.0 — canonical; edit via `daoris upstream` -->
---
name: no-tmp-for-repo-files
applies_when: composing a repository file, or needing a scratch, probe, or dump file
enforces: compose finals with the file-writing tools; scratch goes in a gitignored repo directory, never OS temp
---

# No OS temp for repository files — compose in place; keep scratch in the repository

**Never write repository-related content — final or intermediate — into the operating system's temporary
directory or a session scratch area. Compose final files directly with the file-writing tools, and put
any unavoidable scratch in a gitignored directory inside the repository.**

## Why

A file in OS temp is invisible to the person reviewing the workspace, gets orphaned the moment cleanup is
forgotten, and is not reliably reachable across steps — a different root or permission boundary can make
it vanish between one command and the next. None of that is true of a path inside the repository.

Writing final content through shell redirection has a second failure mode: on a machine whose console
encoding is not UTF-8, non-ASCII characters are silently mangled on the way through. Writing the file
directly never touches the console.

## How to apply

- **Final content → write the file directly.** Read the inputs, build the content, write it once. Most
  work needs no intermediate file at all.
- **Unavoidable scratch — probes, fixtures, multi-stage dumps → a gitignored directory in the
  repository.** Clean it up when the task ends.
- **Reusable tooling → a tracked tools directory**, where it is visible, reviewable, and reusable, rather
  than re-invented next time.
- Never compose a repository file by redirecting into temp and copying back.
- OS temp remains fine for genuinely non-repository content — a tool that legitimately needs it.
