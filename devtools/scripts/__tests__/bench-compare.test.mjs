// bench-compare's own tests.
//
// The command pairs two `devtools/_bench-results/*.jsonl` runs by (arm, question) and reads only the
// questions they DISAGREE on. These drive the pure half: the exact test, the parser's refusals, and the
// pairing's refusals — a comparison over mismatched questions produces a believable wrong number.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { compareResults, formatP, formatReport, mcnemarExact, parseArgs, parseResults } from '../bench-compare.mjs';

const row = (arm, question, hit, extra = {}) => JSON.stringify({
  bench: 'memory-locomo', metric: 'evidence-hit@k', arm, question, hit, configuration: 'cfg1',
  run: '2026-10-24T00:00:00Z', ...extra,
});
const file = (...rows) => rows.join('\n');

describe('mcnemarExact — the same numbers as BenchStats.McNemarExact', () => {
  it('reproduces two figures the C# instrument published (docs/memory-measurements.md, found@8)', () => {
    // "discordant 9/0, p = 0.004" and "discordant 7/2, p = 0.180" came from BenchStats.McNemarExact (the
    // first rounded in prose). Agreeing with them here is what lets a reader quote either tool's p.
    assert.equal(formatP(mcnemarExact(9, 0)), 'p=0.0039');
    assert.equal(formatP(mcnemarExact(7, 2)), 'p=0.180');
  });

  it('is the exact two-sided binomial tail, symmetric in its arguments', () => {
    assert.equal(mcnemarExact(9, 0), 2 / 512);
    assert.ok(Math.abs(mcnemarExact(7, 2) - (2 * (1 + 9 + 36)) / 512) < 1e-15);
    assert.ok(Math.abs(mcnemarExact(8, 2) - (2 * (1 + 10 + 45)) / 1024) < 1e-15);
    assert.equal(mcnemarExact(2, 8), mcnemarExact(8, 2));
  });

  it('is 1 with nothing discordant, and never exceeds 1 on an even split', () => {
    assert.equal(mcnemarExact(0, 0), 1);
    assert.equal(mcnemarExact(5, 5), 1);
  });
});

describe('formatP — BenchStats.Format, never "p = 0.000"', () => {
  it('uses the same four bands', () => {
    assert.equal(formatP(0.00001), 'p<0.0001');
    assert.equal(formatP(0.0005), 'p<0.001');
    assert.equal(formatP(0.0042), 'p=0.0042');
    assert.equal(formatP(0.5), 'p=0.500');
  });
});

describe('parseResults', () => {
  it('reads one row per line and skips blank lines', () => {
    const rows = parseResults(file(row('a', 'q1', true), '', row('a', 'q2', null)), 'x.jsonl');
    assert.equal(rows.length, 2);
    assert.equal(rows[1].hit, null);
  });

  it('refuses a duplicate (arm, question) — a pairing cannot know which one was meant', () => {
    assert.throws(() => parseResults(file(row('a', 'q1', true), row('a', 'q1', false)), 'x.jsonl'),
      /x\.jsonl:2: duplicate \(arm, question\) a \/ q1/);
  });

  it('refuses a row without a boolean-or-null hit, naming the line', () => {
    assert.throws(() => parseResults(row('a', 'q1', 'yes'), 'x.jsonl'), /x\.jsonl:1: .*hit/);
    assert.throws(() => parseResults('{"arm":"a"}', 'x.jsonl'), /x\.jsonl:1: .*question/);
    assert.throws(() => parseResults('not json', 'x.jsonl'), /x\.jsonl:1: not JSON/);
  });
});

describe('compareResults', () => {
  const a = parseResults(file(row('lyntai', 'q1', true), row('lyntai', 'q2', true), row('lyntai', 'q3', false),
    row('lyntai', 'q4', null), row('only-a', 'q1', true)), 'a');
  const b = parseResults(file(row('lyntai', 'q1', true), row('lyntai', 'q2', false), row('lyntai', 'q3', true),
    row('lyntai', 'q4', true)), 'b');

  it('pairs by (arm, question) and counts only the questions both runs DECIDED', () => {
    const r = compareResults(a, b);
    assert.deepEqual(r.refusals, []);
    const [arm] = r.arms;
    assert.equal(arm.arm, 'lyntai');
    // q4 is undecided in `a` (null), so it is absent from the pairing rather than scored as a miss.
    assert.equal(arm.paired, 3);
    assert.equal(arm.hitsA, 2);
    assert.equal(arm.hitsB, 2);
    assert.equal(arm.onlyA, 1);
    assert.equal(arm.onlyB, 1);
    assert.equal(arm.p, mcnemarExact(1, 1));
    assert.deepEqual(r.unpairedArms, { a: ['only-a'], b: [] });
  });

  it('refuses mismatched question sets unless told, then pairs the intersection', () => {
    const short = parseResults(file(row('lyntai', 'q1', false), row('lyntai', 'q9', true)), 'b');
    const refused = compareResults(a, short);
    assert.equal(refused.arms.length, 0);
    assert.match(refused.refusals.join('\n'), /questions differ for arm lyntai: 3 only in A, 1 only in B/);

    const allowed = compareResults(a, short, { allowQuestionMismatch: true });
    assert.deepEqual(allowed.refusals, []);
    assert.equal(allowed.arms[0].paired, 1);
    assert.equal(allowed.arms[0].onlyA, 1);
  });

  it('refuses mismatched configuration hashes unless told', () => {
    const other = parseResults(file(row('lyntai', 'q1', true, { configuration: 'cfg2' }),
      row('lyntai', 'q2', true, { configuration: 'cfg2' }), row('lyntai', 'q3', true, { configuration: 'cfg2' }),
      row('lyntai', 'q4', true, { configuration: 'cfg2' })), 'b');
    assert.match(compareResults(a, other).refusals.join('\n'), /configuration differs: cfg1 vs cfg2/);
    assert.deepEqual(compareResults(a, other, { allowConfigMismatch: true }).refusals, []);
  });

  it('ALWAYS refuses a different metric or bench — a hit would mean two things', () => {
    const judged = parseResults(file(row('lyntai', 'q1', true, { metric: 'judge' })), 'b');
    const r = compareResults(a, judged, { allowConfigMismatch: true, allowQuestionMismatch: true });
    assert.match(r.refusals.join('\n'), /metric differs: evidence-hit@k vs judge/);
    assert.equal(r.arms.length, 0);
  });

  it('formats a report that names the discordant counts and the p-value', () => {
    const text = formatReport(compareResults(a, b)).join('\n');
    assert.match(text, /lyntai\s+3\s+66\.7% → 66\.7%\s+\+1 \/ -1\s+p=1\.000/);
    assert.match(text, /only in A: only-a/);
  });
});

describe('parseArgs', () => {
  it('takes two files and the two override flags', () => {
    assert.deepEqual(parseArgs(['x.jsonl', '--allow-config-mismatch', 'y.jsonl']),
      { files: ['x.jsonl', 'y.jsonl'], allowConfigMismatch: true, allowQuestionMismatch: false });
  });
});
