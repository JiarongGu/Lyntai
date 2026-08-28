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
  <br>**And know what the answer means before repairing anything — which means MEASURING `core.autocrlf`
  rather than assuming it.** Run `git config --show-origin --get-all core.autocrlf`. The plain form gives the
  right effective ANSWER; what it hides is the PROVENANCE — this setting is commonly `true` at system and
  global scope and overridden per-clone, and **the repo-local value wins**. Knowing which scope won is what
  tells you the value is clone-local and unshared, so a teammate's clone may answer differently.
  <br>The two answers mean nearly opposite things:

  | | what the index gets | so a `w/mixed` working tree is |
  |---|---|---|
  | `true`, no `.gitattributes` | LF, for a file not already stored with CRLF | usually local and cosmetic |
  | `false`, no `.gitattributes` | **the tree verbatim** | real, and it commits |

  **Row 1 is not unconditional, and its exceptions are ones you meet.** `core.autocrlf=true` is defined as
  `text=auto`, and gitattributes(5) says of that: *"If it is text and the file was not already in Git with
  CRLF endings, line endings are converted on checkin and checkout … Otherwise, no conversion is done on
  checkin or checkout."* So a blob already stored with CRLF stays CRLF on re-add, and binary-detected content
  (`i/-text`) is never converted at all. Under `true` a `w/mixed` tree can still be real and can still
  commit — which is why the per-file `i/lf` check below is the thing to trust under EITHER setting.

  **This rule asserted row 1 as a fact about this repository and it was row 2**, which is
  exactly the failure the rest of this file is about: a tool wrote CRLF, git faithfully stored CRLF, and
  `git show --stat` read **2055 insertions / 1931 deletions** for a change whose real size was **131 / 7**
  — the first, discarded commit of the guard-script work that landed as `37d15b4`. That commit was reset
  away, so the figures live here rather than anywhere a reader can still run `git show` against.
  `--ignore-cr-at-eol` and `git ls-files --eol` named it in seconds; the sentence above sent the reader the
  other way first.
  <br>**So state the rule, never the value.** `.git/config` is untracked, so no document here can say what a
  given clone holds — which is why this now names the command instead. `git ls-files --eol` reading `i/lf`
  is the property you actually want, and it is true or false per file regardless of how the config got that
  way. **Check it before every commit that touched a file a tool rewrote** — under `false` always, and under
  `true` too, because of row 1's exceptions above.

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
  process and let it take its children — by **PID**, recorded when you started it.
  <br>**This rule was written down and violated anyway, 2026-08-28.** A measurement run finished with
  `taskkill //F //IM llama-server.exe` and took down a *second* instance on another port — a sibling tool's
  embedding server, which nothing in the run had started and nothing in the run was waiting on. It was
  restarted and verified healthy, and the cost was only minutes; the point is that the reach of `//IM` is the
  IMAGE, so it is never scoped to your work. **A local model server is exactly the shared runtime this rule
  is about**, even though it does not look like a browser: one binary, many tenants, one port each.
- **Copy and move preserve the modification time.** A file restored that way can be *older* than the
  artifact built from the version you were replacing, so an incremental build silently keeps using the
  old artifact — a stale PASS, which is the dangerous direction. Undo a change with the same tool that
  made it, and force a full rebuild if unsure.
- **Reverting to the last commit discards uncommitted work** the file already carried. It is not an undo.

### Node

- Some Node versions crash on `fs.cpSync` on Windows with a silent fail-fast. Use an explicit recursive
  copy instead of assuming it works.
