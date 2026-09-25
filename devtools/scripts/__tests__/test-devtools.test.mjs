// test-devtools — the guards' own suite and its CLAUDE.md baseline. See devtools/scripts/test-devtools.mjs.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { testDevtools, testSummaryCounts } from '../test-devtools.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

describe('testSummaryCounts — how the runner\'s summary is read', () => {
  // The counts are line-anchored on `ℹ`, and the spec reporter puts a colour escape BEFORE that glyph
  // whenever FORCE_COLOR is set — which it honours through the pipe the gate reads. A 871/871 run once
  // parsed as NaN and reported the gate's own scripts as failing.
  it('survives a COLOURED reporter, which is how the gate really runs it', () => {
    const plain = 'ℹ tests 871\nℹ suites 190\nℹ pass 871\nℹ fail 0\n';
    const coloured = '\u001b[34mℹ tests 871\u001b[39m\n\u001b[34mℹ pass 871\u001b[39m\n\u001b[34mℹ fail 0\u001b[39m\n';
    assert.deepEqual(testSummaryCounts(plain), { tests: 871, passed: 871, failed: 0 });
    assert.deepEqual(testSummaryCounts(coloured), { tests: 871, passed: 871, failed: 0 });
  });

  it('an UNREADABLE summary is distinguishable from a real failure', () => {
    const { tests, passed, failed } = testSummaryCounts('the runner crashed before it summarised anything');
    assert.ok(![tests, passed, failed].some(Number.isFinite), 'counts must be NaN, never a plausible zero');
  });
});

describe('testDevtools — the run checks the baseline CLAUDE.md quotes', () => {
  const summary = (n, failed = 0) => ({ status: failed ? 1 : 0, stdout: `ℹ tests ${n}\nℹ pass ${n - failed}\nℹ fail ${failed}\n` });
  const withClaim = (claim) => makeTree({ 'CLAUDE.md': `# repo\n\ne2e 3/3, ${claim}, doc samples 5/5.\n` });

  it('passes a green run whose counts match `guard-script tests N/N`', (t) => {
    const repo = withClaim('guard-script tests 12/12');
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(testDevtools({ repo, log, error: log, run: () => summary(12) }), 0, log.text());
    assert.match(log.text(), /12\/12 guard-script tests pass/);
  });

  it('FAILS a green run whose counts the baseline no longer states — the number is the run\'s own', (t) => {
    const repo = withClaim('guard-script tests 11/11');
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(testDevtools({ repo, log, error: log, run: () => summary(12) }), 1);
    assert.match(log.text(), /says "guard-script tests 11\/11" — this run produced 12\/12/);
  });

  it('reports a failing suite as the gate\'s own scripts failing, naming the count', (t) => {
    const repo = withClaim('guard-script tests 12/12');
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(testDevtools({ repo, log, error: log, run: () => summary(12, 2) }), 1);
    assert.match(log.text(), /themselves failing — 2 test\(s\)/);
  });
});
