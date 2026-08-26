---
name: windows-machine
applies_when: running any shell command, script, or file write on a Windows development machine
enforces: never round-trip text through PowerShell 5; BOM and encoding traps; exit codes that lie; never kill a shared runtime by name
---

# Windows machine traps — the ones that pass silently

Every item here was found the expensive way: it does not fail, it *succeeds wrongly*. Each one either
corrupts a file, reports the wrong exit code, or takes down something that was not yours.

## Why

A tool that errors teaches you something. These do not error — they produce mangled text, a green build
from a stale artifact, or a success exit code on a failed script. The cost is not the incident; it is the
hours spent looking somewhere else.

## How to apply

### Text and encoding

- **Never round-trip a source file through PowerShell 5's `Get-Content` / `Set-Content`.** It mangles
  UTF-8. Use the file-writing tools, or a script in a language that writes bytes as given.
- **`-Encoding utf8` writes a BOM on PowerShell 5.** Harmless to PowerShell, poison to anything
  BOM-sensitive — JSON lines, some compilers, some parsers. Write BOM-less UTF-8 deliberately.
- **On a non-Latin system locale, a compiler may read BOM-less UTF-8 sources as the system codepage** and
  turn every non-ASCII string literal into mojibake. Set the source codepage explicitly in the build
  configuration rather than relying on the default.
- **A non-UTF-8 console mangles non-ASCII on the way through.** Never build file content by echoing it
  through the shell; write the file directly.
- **`grep $'\r$'` is an ALWAYS-TRUE line-ending check in Git Bash, so it certifies every file it is pointed
  at.** The shell strips the carriage return from the pattern argument, leaving the bare anchor `$`, which
  matches every line — a pure-LF three-line file reports three CRLF lines, and its `-cv` twin reports zero
  LF-only lines. **It cannot fail**, so it reads as a clean result on files it never examined. Measured
  2026-08-26, after it was used to certify a dozen files across one session; all of them were LF.
  **Use `git ls-files --eol`**, which is built for this and names the state outright:
  `i/lf w/crlf` is the ordinary checked-out file, and `w/mixed` is the defect. For an untracked file, count
  the bytes (`b.count(b'\r\n')` against `b.count(b'\n')`) — never a shell pattern containing a control
  character.
  <br>**And know what the answer means here before repairing anything.** `core.autocrlf` is `true` with no
  `.gitattributes`, so every blob in the index is LF whatever the working tree looks like: a file that
  splicing left `w/mixed` is committed clean and re-checks-out uniform. The working-tree state is real but
  local, which is exactly why a scary-looking mix is worth *diagnosing* before it is worth *fixing*.

### Scripts and exit codes

- **`process.exit()` with a network request in flight aborts the process**, and the abort *replaces* the
  exit code — a script that meant to fail reports success. Set the exit code and let the process end on
  its own.
- **PowerShell 5 has no `&&` / `||` chaining.** Use explicit conditionals; a script written with them
  fails to parse rather than running.
- **Path translation can rewrite arguments** meant for a native tool. Disable it for the call when an
  argument must arrive untouched.

### Processes and files

- **Never kill a shared runtime by process name.** A browser or framework runtime that your app embeds is
  usually the same one other applications embed; killing it by name takes them with it. Kill your own
  process and let it take its children.
- **Copy and move preserve the modification time.** A file restored that way can be *older* than the
  artifact built from the version you were replacing, so an incremental build silently keeps using the
  old artifact — a stale PASS, which is the dangerous direction. Undo a change with the same tool that
  made it, and force a full rebuild if unsure.
- **Reverting to the last commit discards uncommitted work** the file already carried. It is not an undo.

### Node

- Some Node versions crash on `fs.cpSync` on Windows with a silent fail-fast. Use an explicit recursive
  copy instead of assuming it works.
