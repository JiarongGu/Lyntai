# Rules index (dev-facing)

Two tiers. **Core rules** auto-apply to any work in this repo — scan "Applies when" and read what
matches. **Knowledge** is on-demand deep detail — read the file for the area you're touching.

## Core rules (always on)

| Rule | Applies when | Enforces |
|---|---|---|
| [sensitive-info.md](sensitive-info.md) | EVERY commit / tracked-file edit | No dev-machine paths or private tokens in the repo; pre-commit guard + `local/sensitive-patterns.txt` |
| [dev-conventions.md](dev-conventions.md) | Any code change | Package layout (interface in Core, impl in adapter, never adapter→adapter), async Dapper + FluentMigrator numbering + FTS5-trigram, spawn hygiene + fallback semantics, DI-collection variation points, xUnit + provider-stub testing, devtools loop |

## Knowledge (on-demand — `.claude/knowledge/`)

| Doc | Read when |
|---|---|
| [extending-lyntai.md](../knowledge/extending-lyntai.md) | Adding a provider / storage backend / scorer / migration |
| [llm-and-router.md](../knowledge/llm-and-router.md) | Touching `Lyntai.Core/Llm/**` or any provider — verdicts, fallback, streaming, CLI spawn |
| [storage.md](../knowledge/storage.md) | Touching `Lyntai.Storage.*` — Dapper/CAST, FTS5 triggers, migrations, pragmas |
| [pitfalls.md](../knowledge/pitfalls.md) | Before extending anything — traps that pass build+tests while being wrong |

Extension **skills** live in `.claude/skills/` (`add-provider`, `add-storage-backend`, `add-scorer`,
`add-migration`). The design contract is `docs/2026-07-17-lyntai-design.md`; decisions in
`docs/DECISIONS.md`; the build sequence in `tasks.md`.
