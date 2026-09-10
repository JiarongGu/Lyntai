---
name: windows-machine
description: Windows development-machine traps that succeed WRONGLY — PowerShell round-trips, BOMs, lying exit codes, killing a shared runtime.
applies_when: running any shell command, script, or file write on a Windows development machine
---

# Windows machine traps — the ones that pass silently

Every item here was found the expensive way: it does not fail, it *succeeds wrongly* — it corrupts a file,
reports the wrong exit code, or takes down something that was not yours. The cost is never the incident; it
is the hours spent looking somewhere else.

### Text and encoding

- **Never round-trip a source file through PowerShell 5's `Get-Content` / `Set-Content`.** It mangles
  UTF-8. Use the file-writing tools, or a script in a language that writes bytes as given.
- **`-Encoding utf8` writes a BOM on PowerShell 5.** Harmless to PowerShell, poison to anything
  BOM-sensitive — JSON lines, some compilers, some parsers. Write BOM-less UTF-8 deliberately.
- **On a non-Latin system locale, a compiler may read BOM-less UTF-8 sources as the system codepage** and
  turn every non-ASCII string literal into mojibake. Set the source codepage in the build configuration.
- **A non-UTF-8 console mangles non-ASCII on the way through.** Never build file content by echoing it
  through the shell; write the file directly. `check-encoding` catches mojibake afterwards, with a
  deliberately EMPTY exclusion list — but a detector is not a preventer.
- **`grep $'\r$'` is an ALWAYS-TRUE line-ending check in Git Bash**, so it certifies every file it is
  pointed at: the shell strips the carriage return, leaving the bare anchor `$`, which matches every line.
  **It cannot fail.** Use **`git ls-files --eol`**, which names the state per file — `w/lf` agrees with an
  LF index, `w/crlf` does not, `w/mixed` is the defect. For an untracked file count the bytes, never a
  shell pattern containing a control character.
- **Ask the ATTRIBUTE before the config, because the attribute wins.** `git check-attr text eol -- <path>`
  decides what happens to a file; `core.autocrlf` only decides where no attribute applies. **This
  repository declares one** (`* text=auto eol=lf`, **D95**), so the per-clone investigation is not needed
  here — and why asserting a `core.autocrlf` value as a fact is itself the trap is in
  `.claude/knowledge/pitfalls.md` §Environment / tooling.
- **A tool can still write CRLF into the WORKING TREE.** Freshly written it shows as ` M` and
  `git checkout -- <file>` repairs it; once a `git add` has refreshed the stat cache, status goes clean and
  checkout SKIPS it. It can never reach the index — the clean filter normalizes it — but repair it whenever
  `git ls-files --eol` reports anything but `w/lf`.

### Scripts and exit codes

- **`process.exit()` with a network request in flight aborts the process**, and the abort *replaces* the
  exit code — a script that meant to fail reports success. Set the exit code and let the process end.
- **PowerShell 5 has no `&&` / `||` chaining.** A script written with them fails to parse rather than
  running.
- **Path translation can rewrite arguments** meant for a native tool. Disable it for the call when an
  argument must arrive untouched.
- **Never read an exit code through a pipe.** `cmd | tail` reports *tail's* status, so a failing command
  looks like a clean one. Redirect to a file and echo `$?`, or check `PIPESTATUS`.

### Processes and files

- **Never kill a shared runtime by process name.** `//IM` reaches the IMAGE, so it is never scoped to your
  work — and **a local model server is exactly this kind of shared runtime**: one binary, many tenants, one
  port each. Kill your own process by **PID**, recorded when you started it, and let it take its children.
  <br>**PID is not enough — VERIFY the neighbour afterwards** and restart it if it is gone. *"I only killed
  my own PIDs"* is an argument, not evidence, and `taskkill //F //PID` has reported SUCCESS for a process
  still listening seconds later, so re-read `netstat` rather than trusting its exit code. Both incidents:
  `.claude/knowledge/pitfalls.md` §Environment / tooling.
- **Copy and move preserve the modification time.** A file restored that way can be *older* than the
  artifact built from what it replaced, so an incremental build silently keeps the old artifact — a stale
  PASS, the dangerous direction. Undo a change with the same tool that made it.
- **Reverting to the last commit discards uncommitted work** the file already carried. It is not an undo.

### Node

- Some Node versions crash on `fs.cpSync` on Windows with a silent fail-fast. Use an explicit recursive
  copy instead of assuming it works.
- **`node --test <dir>` does NOT work on Node 24** — a bare directory is loaded as a module; it needs a
  glob.
