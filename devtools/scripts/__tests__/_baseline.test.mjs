// _baseline — the run-derived numbers CLAUDE.md quotes. See devtools/scripts/_baseline.mjs.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { checkQuotedBaseline } from '../_baseline.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

describe('checkQuotedBaseline — a run-derived number is checked by the gate that PRODUCES it', () => {
  const run = { gate: 'check-samples', label: 'doc samples', passed: 78, total: 78 };
  const withClaim = (line) => makeTree({ 'CLAUDE.md': `# repo\n\n${line}\n` });

  it('passes when the quoted pair matches the run', (t) => {
    const repo = withClaim('e2e 3/3, doc samples 78/78.');
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(checkQuotedBaseline(repo, run, log), 0, log.text());
  });

  it('FAILS when the first number differs, naming both', (t) => {
    const repo = withClaim('e2e 3/3, doc samples 78/78.');
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(checkQuotedBaseline(repo, { ...run, passed: 80, total: 80 }, log), 1);
    assert.match(log.text(), /says "doc samples 78\/78" — this run produced 80\/80/);
  });

  it('FAILS when only the DENOMINATOR differs — both numbers are the claim', (t) => {
    const repo = withClaim('guard-script tests 887/890.');
    t.after(() => removeTree(repo));
    const log = recorder();
    const tests = { gate: 'test-devtools', label: 'guard-script tests', passed: 887, total: 887 };
    assert.equal(checkQuotedBaseline(repo, tests, log), 1);
  });

  it('FAILS when CLAUDE.md no longer makes the claim — an absent sentence must not disarm the check', (t) => {
    const repo = withClaim('no counted claim here.');
    t.after(() => removeTree(repo));
    const log = recorder();
    assert.equal(checkQuotedBaseline(repo, run, log), 1);
    assert.match(log.text(), /no longer quotes "doc samples N\/N"/);
  });
});
