// new-migration — scaffold the next FluentMigrator migration with a unique, monotonic `yyyyMMddHHmm` number.
//
// A reused number is silently SKIPPED by FluentMigrator, so the number is strictly greater than every one
// either SQL backend has — the two sets are kept in step (docs/DECISIONS.md D9), and a number only one of
// them holds must still never be reused. It scaffolds the SQLite file; the Postgres twin is written by hand
// (its DDL differs), and the run says so.
//
// Usage: node devtools/dev.mjs new-migration <name>   (e.g. add-jobs-table)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The migration directories whose numbers must never collide. The first is the one scaffolded into. */
export const MIGRATION_DIRS = ['src/Lyntai.Storage.Sqlite/Migrations', 'src/Lyntai.Storage.Postgres/Migrations'];

export const USAGE = 'usage: node devtools/dev.mjs new-migration <name>   (e.g. add-jobs-table)';

/** The clock's `yyyyMMddHHmm`, pushed past every existing number — two in one minute still differ. */
export function nextNumber(existing, now = new Date()) {
  const pad = (v) => String(v).padStart(2, '0');
  let num = Number(`${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`
    + `${pad(now.getHours())}${pad(now.getMinutes())}`);
  const max = existing.length ? Math.max(...existing) : 0;
  while (num <= max) num++;
  return num;
}

/** The numbers already taken in one migration directory, read from its `M<12 digits>_` file names. */
export const numbersIn = (dir) => (fs.existsSync(dir) ? fs.readdirSync(dir) : [])
  .map((f) => (f.match(/^M(\d{12})_/) ?? [])[1]).filter(Boolean).map(Number);

/** The scaffold. The `Feature` placeholder does not compile ON PURPOSE: an untagged migration runs under
 * every feature set, so a domain the app disabled would still land its table. */
export const migrationSource = (num, cls) => [
  'using FluentMigrator;',
  '',
  'namespace Lyntai.Storage.Sqlite.Migrations;',
  '',
  "// TODO: replace Feature below with THIS migration's StorageFeature (KeyValue, Conversation, Memory,",
  '// Score, Trace, PromptVersion, Jobs, Governance, CuratedMemory) — the placeholder does not compile on',
  '// purpose. Both tags are load-bearing: the feature tag is what a SUBSET pass requests, AllTag is what',
  '// the default StorageFeature.All pass requests, and an UNTAGGED migration runs under EVERY feature',
  '// set — so a domain the app disabled would still land its table.',
  '/// <summary>TODO: what this migration does.</summary>',
  `[Migration(${num})]`,
  '[Tags(nameof(StorageFeature.Feature), StorageFeatures.AllTag)]',
  `public sealed class ${cls} : Migration`,
  '{',
  '    public override void Up()',
  '    {',
  '        // TODO. Prefix every object lyntai_. snake_case columns. Composite PK + FK inline at',
  "        // Create.Table (SQLite can't ALTER ADD CONSTRAINT). Wrap 0..1/double columns in",
  '        // CAST(x AS REAL) when you SELECT them. Searchable text? add an FTS5 trigram',
  "        // external-content mirror + AFTER INSERT/DELETE/UPDATE triggers (emit 'delete' rows on",
  '        // delete AND update) + an in-migration backfill — see M202607280003_Memory and',
  '        // .claude/knowledge/storage.md.',
  '    }',
  '',
  '    public override void Down()',
  '    {',
  '        // TODO: reverse Up.',
  '    }',
  '}',
  '',
].join('\n');

/** @returns {number} the exit code */
export function newMigration({ repo = repoDefault, args = [], now = new Date(), log = console.log, error = console.error } = {}) {
  const raw = args[0];
  if (!raw || !/^[a-z][a-z0-9_-]*$/i.test(raw)) { error(USAGE); return 1; }
  const num = nextNumber(MIGRATION_DIRS.flatMap((d) => numbersIn(path.join(repo, d))), now);
  const pascal = raw.split(/[-_]/).filter(Boolean).map((s) => s[0].toUpperCase() + s.slice(1)).join('');
  const cls = `M${num}_${pascal}`;
  const file = path.join(repo, MIGRATION_DIRS[0], `${cls}.cs`);
  if (fs.existsSync(file)) { error(`already exists: ${file}`); return 1; }
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, migrationSource(num, cls));
  log(`created ${MIGRATION_DIRS[0]}/${cls}.cs — number ${num}, above every number in both backends.`);
  log("Next: name the [Tags] feature (the placeholder doesn't compile — an untagged migration would");
  log('run under every feature set), define the table, then a store + its I*Store impl.');
  log(`Then write its Postgres twin, ${MIGRATION_DIRS[1]}/${cls}.cs, by hand — the two sets stay in step (D9).`);
  log('See .claude/skills/add-migration.');
  return 0;
}

// CLI entry point — a thin wrapper, so importing this module for a test writes nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = newMigration({ args: process.argv.slice(2) });
}
