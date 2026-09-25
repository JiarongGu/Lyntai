// e2e/run — the runner behind the `e2e` verify gate. See devtools/scripts/e2e/run.mjs.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { discoverSuites, passed, selectSuites } from '../e2e/run.mjs';

describe('e2e runner — which suites run', () => {
  it('discovers `pN.mjs` only, in NUMERIC order, and never a helper', () => {
    assert.deepEqual(discoverSuites(['p10.mjs', 'p2.mjs', '_e2e-common.mjs', 'run.mjs', 'p1.mjs']),
      ['p1', 'p2', 'p10']);
  });

  it('selects all, one suite, or a list — and a typo selects NOTHING rather than something', () => {
    const all = ['p1', 'p2', 'p3'];
    assert.deepEqual(selectSuites(all), all);
    assert.deepEqual(selectSuites(all, 'p2'), ['p2']);
    assert.deepEqual(selectSuites(all, 'p1, p3'), ['p1', 'p3']);
    assert.deepEqual(selectSuites(all, 'p12'), [], 'the caller fails on an empty selection');
  });
});

describe('e2e runner — the verdict', () => {
  it('passes a clean exit, and fails a non-zero one without the PASS line', () => {
    assert.equal(passed('p1', { status: 0, signal: null, out: '' }), true);
    assert.equal(passed('p1', { status: 1, signal: null, out: 'e2e-p1 FAIL (2)' }), false);
  });

  it('trusts the suite\'s own PASS line over a teardown crash code', () => {
    assert.equal(passed('p1', { status: 3221226505, signal: null, out: 'x\ne2e-p1 PASS\n' }), true);
  });

  it('never reads ANOTHER suite\'s PASS line, or a prefix of one, as this suite\'s', () => {
    assert.equal(passed('p1', { status: 1, signal: null, out: '\ne2e-p10 PASS\n' }), false);
    assert.equal(passed('p1', { status: 1, signal: null, out: '\ne2e-p2 PASS\n' }), false);
  });
});
