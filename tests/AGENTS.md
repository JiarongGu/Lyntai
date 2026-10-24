# tests — the xUnit suite, the API baselines and the schema snapshots

## What this folder is

`Lyntai.Tests`: every package's tests in one project, the frozen public surface
(`Lyntai.Tests/Api/Baselines/<Assembly>.txt`) and the fresh-database schema snapshots
(`Lyntai.Tests/Storage/Snapshots/`). It must never need a real token or a real model to pass.

## Conventions here

- **Pure logic with fakes and no I/O**; storage against a per-test temp SQLite database, created, migrated,
  then deleted; providers against the deterministic provider-stub (`LYNTAI_PROVIDER_CMD`).
- **A live-backend test SKIPS** (`Skip.If` / `Skip.IfNot`) when its backend is absent — never an early
  return, which counts as a pass. Postgres runs in a Docker container (`Storage/PostgresFixture.cs`).
- **A baseline changes only deliberately**: `ApiSurfaceTests` writes `<Assembly>.txt.actual` on a
  mismatch; copy it over the baseline and note the break in `CHANGELOG.md`. The schema snapshots
  (`MigrationSchemaSnapshotTests`, `PgMigrationSchemaSnapshotTests`) work the same way.

## Traps

- **A skip count in the low hundreds means Docker is down**: the Postgres leg went unexercised, and `test`
  fails on it unless the run declares it (`docs/GATES.md` §test). The baseline to compare against, and how
  to read movement in it, is `CLAUDE.md` §Current state.
- A test that wraps a migration in the Docker-down catch turns a broken migration into a skip; the
  fixture's comment says why the catch covers container startup only.

## Checks

- `node devtools/dev.mjs test` after a change here; then `verify`, and re-measure the baseline by hand.
