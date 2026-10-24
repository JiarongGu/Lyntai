// _test-evidence — what the `test` step EXECUTED. See devtools/scripts/_test-evidence.mjs.
//
// The defect it exists for: with Docker down every Postgres fact SKIPS, `dotnet test` prints `Passed!`, and
// every gate in `verify` stays green — the leg goes unexercised behind a green line (`docs/GATES.md`
// §Which numbers a gate holds). These tests hold that a Postgres skip fails unless the run declares it.
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import {
  ALLOW_POSTGRES_SKIP_ENV, POSTGRES_SKIP_MARKER, checkTestEvidence, reduceTrx, testEvidence,
} from '../_test-evidence.mjs';
import { makeTree, recorder, removeTree, repoRoot } from './_fixtures.mjs';

const result = (name, outcome, message) => (message === undefined
  ? `<UnitTestResult testName="${name}" outcome="${outcome}" />`
  : `<UnitTestResult testName="${name}" outcome="${outcome}"><Output><ErrorInfo><Message>${message}</Message>`
    + '</ErrorInfo></Output></UnitTestResult>');

/** A trx shaped like VSTest's, with the Counters element deliberately WRONG, as the real one is for a skip. */
const trx = (...results) => '﻿<?xml version="1.0" encoding="utf-8"?>\n<TestRun><Results>\n'
  + `${results.join('\n')}\n</Results><ResultSummary outcome="Completed">`
  + `<Counters total="${results.length}" executed="0" passed="0" notExecuted="0" /></ResultSummary></TestRun>\n`;

const pgReason = `${POSTGRES_SKIP_MARKER}: DockerUnavailableException: no endpoint`;

describe('reduceTrx — counts every result by OUTCOME, never by the Counters element', () => {
  it('counts passed, failed and skipped from the results themselves', () => {
    // VSTest writes notExecuted="0" for an xUnit skip, so a summary read from Counters would report none.
    const s = reduceTrx(trx(
      result('A.B.Passes', 'Passed'),
      result('A.B.Fails', 'Failed', 'boom'),
      result('A.C.Skips', 'NotExecuted', 'set SOME_DIR'),
    ));
    assert.deepEqual([s.total, s.passed, s.failed, s.skipped], [3, 1, 1, 1]);
  });

  it('buckets each skip by REASON, with its count and the classes it came from', () => {
    const s = reduceTrx(trx(
      result('Ns.Live.One', 'NotExecuted', 'set SOME_DIR'),
      result('Ns.Live.Two(x: &quot;a&gt;b&quot;)', 'NotExecuted', 'set SOME_DIR'),
      result('Ns.Other.Three', 'NotExecuted', 'needs a server'),
    ));
    assert.deepEqual(s.skips, [
      { reason: 'set SOME_DIR', count: 2, postgres: false, classes: ['Ns.Live'] },
      { reason: 'needs a server', count: 1, postgres: false, classes: ['Ns.Other'] },
    ]);
  });

  it('marks a skip whose reason carries the Postgres marker, and totals the leg', () => {
    const s = reduceTrx(trx(
      result('Ns.PostgresStorageTests.Roundtrips', 'NotExecuted', pgReason),
      result('Ns.UsePostgresStorageTests.Wires', 'NotExecuted', `${POSTGRES_SKIP_MARKER}`),
      result('Ns.Live.One', 'NotExecuted', 'set SOME_DIR'),
    ));
    assert.equal(s.postgresSkipped, 2);
    assert.deepEqual(s.skips.filter((b) => b.postgres).map((b) => b.count), [1, 1]);
  });

  it('decodes XML entities in a reason, so a bucket reads as the test wrote it', () => {
    const s = reduceTrx(trx(result('A.B.C', 'NotExecuted', 'needs &lt;dir&gt; &amp; a &quot;model&quot;')));
    assert.equal(s.skips[0].reason, 'needs <dir> & a "model"');
  });

  it('reads an empty run as ZERO results, which the check refuses', () => {
    assert.equal(reduceTrx(trx()).total, 0);
  });
});

describe('checkTestEvidence — a Postgres skip fails unless the run DECLARES it', () => {
  const summary = (postgresSkipped, total = 10) => ({ total, passed: total - postgresSkipped, failed: 0,
    skipped: postgresSkipped, other: 0, postgresSkipped, skips: [] });

  it('passes a run whose Postgres leg executed', () => {
    const log = recorder();
    assert.equal(checkTestEvidence(summary(0), { allowPostgresSkip: false }, log), 0, log.text());
  });

  it('FAILS when a Postgres fact skipped, and names the declaration that would allow it', () => {
    const log = recorder();
    assert.equal(checkTestEvidence(summary(3), { allowPostgresSkip: false }, log), 1);
    assert.match(log.text(), /3 Postgres fact/);
    assert.match(log.text(), new RegExp(ALLOW_POSTGRES_SKIP_ENV));
  });

  it('passes a Postgres skip the run DECLARED, and still says so', () => {
    const log = recorder();
    assert.equal(checkTestEvidence(summary(3), { allowPostgresSkip: true }, log), 0);
    assert.match(log.text(), /DECLARED/);
  });

  it('FAILS a run that executed nothing — an empty trx is not evidence', () => {
    const log = recorder();
    assert.equal(checkTestEvidence(summary(0, 0), { allowPostgresSkip: true }, log), 1);
  });
});

describe('testEvidence — reads the trx, writes the summary, returns the verdict', () => {
  it('writes the JSON summary, with the declaration recorded', (t) => {
    const dir = makeTree({ 'r/run.trx': trx(result('Ns.PostgresScoreOrderTests.X', 'NotExecuted', pgReason)) });
    t.after(() => removeTree(dir));
    const log = recorder();
    const code = testEvidence({ trxPath: join(dir, 'r/run.trx'), summaryPath: join(dir, 'r/summary.json'),
      env: { [ALLOW_POSTGRES_SKIP_ENV]: '1' }, log });
    assert.equal(code, 0, log.text());
    const written = JSON.parse(readFileSync(join(dir, 'r/summary.json'), 'utf8'));
    assert.equal(written.postgresSkipped, 1);
    assert.equal(written.postgresSkipDeclared, true);
  });

  it('FAILS when there is no trx — a missing record never reads as a clean one', (t) => {
    const dir = makeTree({});
    t.after(() => removeTree(dir));
    const log = recorder();
    assert.equal(testEvidence({ trxPath: join(dir, 'none.trx'), summaryPath: join(dir, 's.json'), env: {}, log }), 1);
  });

  it('treats only the value 1 as a declaration — an unset or empty variable declares nothing', (t) => {
    const dir = makeTree({ 'run.trx': trx(result('Ns.PgX.Y', 'NotExecuted', pgReason)) });
    t.after(() => removeTree(dir));
    for (const value of [undefined, '', '0']) {
      const code = testEvidence({ trxPath: join(dir, 'run.trx'), summaryPath: join(dir, 's.json'),
        env: { [ALLOW_POSTGRES_SKIP_ENV]: value }, log: recorder() });
      assert.equal(code, 1, `value ${JSON.stringify(value)}`);
    }
  });
});

describe('the marker is what the Postgres tests actually SAY', () => {
  // The check identifies the leg by its skip reason, so a reworded reason would disarm it silently.
  const csFiles = (dir) => readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory()
    ? (['bin', 'obj'].includes(e.name) ? [] : csFiles(join(dir, e.name)))
    : e.name.endsWith('.cs') ? [join(dir, e.name)] : []));
  const sources = csFiles(join(repoRoot, 'tests')).map((f) => readFileSync(f, 'utf8'));

  it('the fixture prefixes its InitError with the marker', () => {
    const fixture = readFileSync(join(repoRoot, 'tests/Lyntai.Tests/Storage/PostgresFixture.cs'), 'utf8');
    assert.ok(fixture.includes(`InitError = $"${POSTGRES_SKIP_MARKER}: `), 'PostgresFixture.InitError lost the marker');
  });

  it('every Postgres skip site falls back to the marker', () => {
    const sites = sources.flatMap((s) => s.match(/Skip\.IfNot\(pg\.Available,[^;]*;/g) ?? []);
    assert.ok(sites.length > 0, 'found no Postgres skip site at all — the scan read the wrong files');
    for (const site of sites) assert.ok(site.includes(`?? "${POSTGRES_SKIP_MARKER}"`), site);
  });
});
