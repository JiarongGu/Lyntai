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
