# Sensitive info — keep dev-machine specifics and private tokens out of tracked files

Lyntai is a reusable library shaped to be published. Keep the repo clean of anything machine- or
person-specific.

## The rules (never in a tracked file or commit message)

- **No dev-machine absolute paths.** No `C:\Users\<name>\…`, no dev-root paths. Use repo-relative paths or
  neutral placeholders.
- **No private tokens / secrets.** API keys, access tokens, credentials — none in tracked files or history.
  Tests use the deterministic provider-stub, never a real key.
- **Commit messages are history too.** Describe changes structurally, never with machine/person specifics.

## How to apply

- **An automated pre-commit guard enforces this** — `devtools/scripts/check-sensitive.mjs` (run by
  `devtools/hooks/pre-commit`) scans staged changes and blocks the commit on any hit. Install once per
  clone: `node devtools/dev.mjs install-hooks` (sets `core.hooksPath`). Any real private tokens go in the
  gitignored `local/sensitive-patterns.txt` (one JS regex per line) — never in a tracked file. Scan the
  whole tree any time: `node devtools/scripts/check-sensitive.mjs --tree`.
- If the guard blocks you: use a repo-relative path / neutral placeholder, or move the value to `local/`.
- A leak already committed is a **history** problem, not a working-tree problem — it needs a history
  rewrite (bundle backup first), not just an edit.
