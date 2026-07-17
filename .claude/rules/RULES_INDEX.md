# Rules index (dev-facing)

Core rules auto-apply to any work in this repo. Scan "Applies when" and read what matches.

| Rule | Applies when | Enforces |
|---|---|---|
| [sensitive-info.md](sensitive-info.md) | EVERY commit / tracked-file edit | No dev-machine paths or private tokens in the repo; pre-commit guard + `local/sensitive-patterns.txt` |
| [dev-conventions.md](dev-conventions.md) | Any code change | Package layout (interface in Core, impl in adapter, never adapter→adapter), async Dapper + FluentMigrator numbering + FTS5-trigram, claude-CLI spawn hygiene + fallback semantics, DI-collection variation points, xUnit + provider-stub testing, devtools loop |

The design contract lives in `docs/2026-07-17-lyntai-design.md`; the build sequence in `tasks.md`.
