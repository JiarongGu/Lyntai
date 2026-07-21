# Minimise Bash friction — prefer the dedicated file tools; never evade the gate

**Inspect files with `Read` / `Grep` / `Glob`, NOT `Bash` `cat`/`find`/`ls`/`head`/`tail`/`sed -n`/`grep`.
Reserve `Bash`/`PowerShell` for genuine shell (the `dev.mjs` loop, git, dotnet, node). NEVER route a shell
command through a side channel (the Chrome `javascript_tool`, a spawned server, …) to dodge the permission
system — that circumvents a safety control, it does not "reduce friction".**

## Why

- `Read`/`Grep`/`Glob` are purpose-built for inspection: clickable `file:line`, permission-UI integration,
  ripgrep speed, and they never prompt. `Bash cat/find/ls/grep` for the same job is worse UX and, on a
  stricter allowlist, also prompts — the dedicated tools remove the friction at the source.
- Shell here is broadly allowed (`Bash` + `PowerShell` are blanket-allowed in the git-ignored
  `.claude/settings.local.json`, per the user's request), so **destructive commands no longer prompt** —
  which makes the discipline below a SAFETY matter, not just a UX one.

## How to apply

- **File reads → `Read`. Content search → `Grep`. File discovery → `Glob`.** Don't reach for
  `Bash cat/find/ls/head/tail/sed -n/grep` to inspect files.
- **Genuine shell → `Bash`/`PowerShell`** — the `node devtools/dev.mjs <build|test|e2e|verify|…>` loop,
  `git`, `dotnet`, `node`. That's what the shell tools are for.
- **Destructive commands need care** (blanket-allow = no prompt to stop you): `rm -rf`, `git reset --hard`,
  `git push --force`, `git commit --no-verify`, `Remove-Item -Recurse`, killing processes, DB writes. Look
  before you leap, prefer a reversible alternative, and confirm irreversible/outward-facing actions first.
- **NEVER evade the permission gate.** Don't route shell through `javascript_tool` / a spawned localhost
  server / any side channel to skip a prompt. If something needs approval, run it through `Bash`/`PowerShell`
  and let the gate handle it, or adjust the allowlist via `/update-config` — never hide it.

## Related

- `.claude/settings.local.json` — the (git-ignored) allowlist. Skills: `/update-config`, `/fewer-permission-prompts`.
- `sensitive-info.md` — the pre-commit guard; never `--no-verify` past it without cause.
