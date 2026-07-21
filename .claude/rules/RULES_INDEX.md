# Rules index (dev-facing)

One-line-per-rule registry + router. **Read this first** for the mandatory rules check — this index is
always-loaded, but the on-demand rule *bodies* are not, so this table is how you discover them.

> **Loading model.** Only a tiny **core** of universal-workflow rules auto-loads every session (the
> `## Core rules` table) plus this index. **Every `.claude/knowledge/*.md` deep dive is on-demand — its
> body is NOT in your context until you `Read` it.** So when a `## Knowledge` row's "Read when" matches the
> task, **`Read` the linked file before acting** — otherwise you're flying blind on that contract. This
> split keeps session base-context small.

## Core rules (always on)

| Rule | Applies when | Enforces |
|---|---|---|
| [sensitive-info.md](sensitive-info.md) | EVERY commit / tracked-file edit | No dev-machine paths or private tokens in the repo; pre-commit guard + `local/sensitive-patterns.txt` |
| [dev-conventions.md](dev-conventions.md) | Any code change | Package layout (interface in Core, impl in adapter, never adapter→adapter), async Dapper + FluentMigrator numbering + FTS5-trigram, spawn hygiene + fallback semantics, DI-collection variation points, xUnit + provider-stub testing, devtools loop |
| [task-lifecycle.md](task-lifecycle.md) | Adding / finishing a task; editing `tasks.md` | `tasks.md` = OPEN backlog only; a completed task MOVES to `docs/task-archive.md` (not left checked-off). Use the `archive-task` skill |
| [no-global-memory.md](no-global-memory.md) | Tempted to save a project fact to global auto-memory | Project facts live IN-REPO (`.claude/rules` / `.claude/knowledge` / `docs/DECISIONS.md`); global `~/.claude/*/memory/` is ONLY for user-specific prefs |
| [minimise-bash-prompts.md](minimise-bash-prompts.md) | EVERY task that inspects files; a destructive/irreversible shell command | File ops via `Read`/`Grep`/`Glob` NOT `Bash cat/find/ls/sed -n`; shell (`dev.mjs`/git/dotnet/node) is blanket-allowed so destructive commands need care; NEVER route shell through a side channel to evade the gate |
| [no-tmp-for-repo-files.md](no-tmp-for-repo-files.md) | Composing a repo file; needing a scratch/probe file or a dev helper | Compose finals with `Write`/`Edit`; scratch/dumps → git-ignored `devtools/_*`, reusable tooling → `devtools/`; never OS temp (`/tmp`, `C:\Temp`) for repo content |

## Knowledge (on-demand — `.claude/knowledge/`)

| Doc | Read when |
|---|---|
| [extending-lyntai.md](../knowledge/extending-lyntai.md) | Adding a provider / storage backend / scorer / migration |
| [llm-and-router.md](../knowledge/llm-and-router.md) | Touching `Lyntai.Core/Llm/**` or any provider — verdicts, fallback, streaming, CLI spawn |
| [storage.md](../knowledge/storage.md) | Touching `Lyntai.Storage.*` — Dapper/CAST, FTS5 triggers, migrations, pragmas |
| [pitfalls.md](../knowledge/pitfalls.md) | Before extending anything — traps that pass build+tests while being wrong |

**Skills** live in `.claude/skills/` — extension tasks (`add-provider`, `add-storage-backend`,
`add-scorer`, `add-migration`) and process (`archive-task`). The design contract is
`docs/2026-07-17-lyntai-design.md`; decisions in `docs/DECISIONS.md`. The **active** backlog is `tasks.md`
(open tasks only); **completed** work is archived in `docs/task-archive.md`.

## How to use

1. Before a non-trivial task, scan the **Applies when** / **Read when** columns for matches.
2. A `## Knowledge` row matches → **`Read` its full file** (bodies are NOT in context; a matched deep dive
   you don't read is a contract you don't have).
3. **Evolve the system.** Discovered something durable a future session would otherwise re-discover — a
   multi-file wiring chain, a trap that passes build+tests, a recurring correction? Capture it: copy
   [TEMPLATE.md](TEMPLATE.md) into **`.claude/knowledge/{name}.md`** (the default) and **add a one-line row
   here**. Only add to the always-loaded `.claude/rules/` core if it's a genuinely universal-workflow rule.
   Project facts NEVER go to global auto-memory ([no-global-memory.md](no-global-memory.md)).

## Invariants

- **One rule file = one concern.** No god-files (the exception is the historical `dev-conventions.md`).
- **Core stays tiny.** A new rule defaults to `.claude/knowledge/` (on-demand); the always-loaded core is
  deliberately a handful of universal-workflow files. Don't grow it.
- **One row = one line.** A row is a pointer — the single core invariant + enough to decide "should I open
  this?". The detail lives in the file, not this table.
- Every row points at a file that exists. Rule names are kebab-case and describe **what is enforced**, not
  the incident that caused them.
