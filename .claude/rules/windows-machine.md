---
name: windows-machine
description: Windows development-machine traps that succeed WRONGLY — PowerShell round-trips, BOMs, lying exit codes, killing a shared runtime.
applies_when: running any shell command, script, or file write on a Windows development machine
---

# Windows machine traps — what this repository adds to the shared rule

**The rule is the shared `windows-machine` (`AGENTS.md`). These traps are this machine's beside it, each
found the same expensive way: it does not fail, it succeeds wrongly.**

### Text and encoding

- **`check-encoding` catches mojibake afterwards**, with a deliberately EMPTY exclusion list — but a
  detector is not a preventer.
- **`grep $'\r$'` is an ALWAYS-TRUE line-ending check in Git Bash**, so it certifies every file it is
  pointed at: the shell strips the carriage return, leaving the bare anchor `$`, which matches every line.
  **It cannot fail.** Use **`git ls-files --eol`**, which names the state per file — `w/lf` agrees with an
  LF index, `w/crlf` does not, `w/mixed` is the defect. For an untracked file count the bytes, never a
  shell pattern containing a control character.
- **Ask the ATTRIBUTE before the config, because the attribute wins.** `git check-attr text eol -- <path>`
  decides what happens to a file; `core.autocrlf` only decides where no attribute applies. **This
  repository declares one** (`* text=auto eol=lf`, **D95**), so the per-clone investigation is not needed
  here — and why asserting a `core.autocrlf` value as a fact is itself the trap is in
  `.agents/knowledge/pitfalls.md` §Environment / tooling.
- **A tool can still write CRLF into the WORKING TREE.** Freshly written it shows as ` M` and
  `git checkout -- <file>` repairs it; once a `git add` has refreshed the stat cache, status goes clean and
  checkout SKIPS it. It can never reach the index — the clean filter normalizes it — but repair it whenever
  `git ls-files --eol` reports anything but `w/lf`.

### Scripts and exit codes

- **Never read an exit code through a pipe.** `cmd | tail` reports *tail's* status, so a failing command
  looks like a clean one. Redirect to a file and echo `$?`, or check `PIPESTATUS`.

### Processes and files

- **Never kill a shared runtime by process name.** `//IM` reaches the IMAGE, so it is never scoped to your
  work — and **a local model server is exactly this kind of shared runtime**: one binary, many tenants, one
  port each. Kill your own process by **PID**, recorded when you started it, and let it take its children.
  <br>**PID is not enough — VERIFY the neighbour afterwards** and restart it if it is gone. *"I only killed
  my own PIDs"* is an argument, not evidence, and `taskkill //F //PID` has reported SUCCESS for a process
  still listening seconds later, so re-read `netstat` rather than trusting its exit code. Both incidents:
  `.agents/knowledge/pitfalls.md` §Environment / tooling.

### Node

- **`node --test <dir>` does NOT work on Node 24** — a bare directory is loaded as a module; it needs a
  glob.
