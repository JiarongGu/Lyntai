// new-migration — the migration scaffolder. See devtools/scripts/new-migration.mjs.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';

import { MIGRATION_DIRS, newMigration, nextNumber } from '../new-migration.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const NOW = new Date(2026, 8, 25, 14, 30);   // 2026-09-25 14:30 local → 202609251430

describe('new-migration — the number', () => {
  it('is the clock\'s yyyyMMddHHmm when nothing is later', () => {
    assert.equal(nextNumber([202607280001], NOW), 202609251430);
  });

  it('is pushed past an existing number rather than reusing it — a reused number is silently skipped', () => {
    assert.equal(nextNumber([202609251430], NOW), 202609251431);
  });

  it('never reuses a number only the POSTGRES backend holds', (t) => {
    const repo = makeTree({
      [`${MIGRATION_DIRS[0]}/M202607280001_KeyValue.cs`]: '',
      [`${MIGRATION_DIRS[1]}/M202609251500_HeadlineSearch.cs`]: '',
    });
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(newMigration({ repo, args: ['add-thing'], now: NOW, log, error: log }), 0, log.text());
    assert.ok(fs.existsSync(path.join(repo, MIGRATION_DIRS[0], 'M202609251501_AddThing.cs')), log.text());
    assert.match(log.text(), /Postgres twin/);
  });
});

describe('new-migration — the scaffold', () => {
  it('refuses a missing or malformed name, and writes nothing', (t) => {
    const repo = makeTree({});
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(newMigration({ repo, args: ['1-bad name'], now: NOW, log, error: log }), 1);
    assert.match(log.text(), /^usage: /m);
    assert.equal(fs.existsSync(path.join(repo, MIGRATION_DIRS[0])), false);
  });

  it('writes a migration carrying its number and the deliberately non-compiling feature tag', (t) => {
    const repo = makeTree({ [`${MIGRATION_DIRS[0]}/M202607280001_KeyValue.cs`]: '' });
    t.after(() => removeTree(repo));
    assert.equal(newMigration({ repo, args: ['add-jobs-table'], now: NOW, log: recorder(), error: recorder() }), 0);
    const text = fs.readFileSync(path.join(repo, MIGRATION_DIRS[0], 'M202609251430_AddJobsTable.cs'), 'utf8');
    assert.match(text, /\[Migration\(202609251430\)\]/);
    assert.match(text, /nameof\(StorageFeature\.Feature\)/);
  });
});
