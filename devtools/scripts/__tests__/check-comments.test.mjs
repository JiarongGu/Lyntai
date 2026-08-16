// check-comments — the comment-length gate. See devtools/scripts/check-comments.mjs.
//
// A guard whose failure mode is a false PASS cannot be validated by running it, so the negative cases here
// matter as much as the positive ones — and the RATCHET has two negative directions, not one: a block over
// its budget must fail, AND an allowance looser than the file needs must fail. The second is the half that
// keeps the debt shrinking; without it the registry becomes a permanent permission slip.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  ESCAPE, MAX_BLOCK, asAllowanceList, blocksIn, checkComments, overLimitBlocks, stackedSummaries, strandedIn,
  trackedFiles, worstBlock,
} from '../check-comments.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** `n` comment lines followed by a line of code. */
const block = (n, first = '// line') =>
  [first, ...Array.from({ length: n - 1 }, (_, i) => `// body ${i}`), 'var x = 1;'].join('\n');

function run(files, cfg) {
  const dir = makeTree(files);
  const log = recorder();
  try {
    return { code: checkComments(dir, cfg, log, Object.keys(files)), out: log.text() };
  } finally {
    removeTree(dir);
  }
}

describe('check-comments — finding the blocks', () => {
  it('a blank line ENDS a block, so two paragraphs are two blocks', () => {
    // The right unit is how much UNINTERRUPTED prose a reader meets before the next line of code. Treating
    // a whole doc comment as one block regardless of paragraphing would measure something else.
    const b = blocksIn('// a\n// b\n\n// c\nvar x = 1;\n');
    assert.deepEqual(b.map((x) => x.length), [2, 1]);
    assert.deepEqual(b.map((x) => x.line), [1, 4]);
  });

  it('counts /// and // alike, and stops at the first line of code', () => {
    assert.deepEqual(blocksIn('/// a\n/// b\nvar x = 1;\n// c\nvar y = 2;\n').map((x) => x.length), [2, 1]);
  });

  it('a doc tag documenting a DIFFERENT subject starts a new block', () => {
    // Otherwise a record with twenty adjacent <param> tags reads as one 40-line wall even at a
    // proportionate two lines each — indistinguishable from a 40-line <remarks> on ONE member, which is the
    // defect this gate is actually for. The unit is prose-per-subject.
    const text = [
      '/// <summary>s1', '/// s2</summary>',
      '/// <param name="a">a1', '/// a2</param>',
      '/// <param name="b">b1</param>',
      '/// <returns>r1', '/// r2</returns>',
      'record X();',
    ].join('\n');
    assert.deepEqual(blocksIn(text).map((x) => x.length), [2, 2, 1, 2]);
  });

  it('but a tag mid-SENTENCE does not — only a tag STARTING a line is a boundary', () => {
    // `<see cref>` and inline `<paramref>` appear constantly inside prose; splitting on those would make
    // every other line its own block and the gate would measure nothing.
    const text = '/// <summary>see <paramref name="a"/> and\n/// <see cref="B"/> too</summary>\nvar x = 1;\n';
    assert.deepEqual(blocksIn(text).map((x) => x.length), [2]);
  });

  it('worstBlock ignores an escaped block entirely', () => {
    const text = `// ${ESCAPE} this one earns it\n// b\n// c\n// d\nvar x = 1;\n`;
    assert.equal(worstBlock(text), 0, 'the escape must remove the block from the measurement');
  });
});

describe('check-comments — the limit', () => {
  it(`fails a block over ${MAX_BLOCK} lines`, () => {
    const { code, out } = run({ 'src/A.cs': block(MAX_BLOCK + 1) }, {});
    assert.equal(code, 1);
    assert.match(out, /src\/A\.cs:1\s+26 lines \(limit 25\)/);
  });

  it(`passes a block of exactly ${MAX_BLOCK} lines — the limit is inclusive`, () => {
    assert.equal(run({ 'src/A.cs': block(MAX_BLOCK) }, {}).code, 0);
  });

  it('an escaped block passes however long it is', () => {
    const { code } = run({ 'src/A.cs': block(60, `// ${ESCAPE} a table nobody can shorten`) }, {});
    assert.equal(code, 0);
  });
});

describe('check-comments — the ratchet', () => {
  it('an allowance permits exactly its recorded size and no more', () => {
    assert.equal(run({ 'src/A.cs': block(40) }, { commentBlockAllowances: { 'src/A.cs': 40 } }).code, 0);
    const worse = run({ 'src/A.cs': block(41) }, { commentBlockAllowances: { 'src/A.cs': 40 } });
    assert.equal(worse.code, 1);
    assert.match(worse.out, /41 lines \(limit 40\)/);
  });

  it('an allowance LOOSER than the file needs FAILS, so improving a file forces its number down', () => {
    // The half that makes this a debt rather than a permission. Without it, a file cleaned from 40 to 30
    // keeps a 40 allowance, and a later regression back to 40 passes unnoticed.
    const { code, out } = run({ 'src/A.cs': block(30) }, { commentBlockAllowances: { 'src/A.cs': 40 } });
    assert.equal(code, 1);
    assert.match(out, /allowed 40, the matching block is now 30/);
  });

  it('an allowance naming a file that is not scanned FAILS, so the registry cannot rot', () => {
    const { code, out } = run({ 'src/A.cs': block(3) }, { commentBlockAllowances: { 'src/Gone.cs': 40 } });
    assert.equal(code, 1);
    assert.match(out, /name a file that is not scanned/);
  });

  it('an allowance below the limit is itself slack — the limit is the floor, not the allowance', () => {
    // A file at 10 with an allowance of 20 is reported, because the allowance is doing nothing except
    // hiding a future regression up to 20. Only an allowance ABOVE MAX_BLOCK is ever load-bearing.
    //
    // This assertion used to say `code === 0` — the opposite of the sentence above it, which is the shape a
    // test acquires when it is written to describe behaviour and then adjusted to match it. The multiset
    // ledger made the comment true; the assertion now agrees with it.
    const { code, out } = run({ 'src/A.cs': block(10) }, { commentBlockAllowances: { 'src/A.cs': 20 } });
    assert.equal(code, 1, out);
    assert.match(out, /the matching block is now gone/);
  });

  it('a SECOND over-limit block in an already-budgeted file FAILS — the hole the ledger had', () => {
    // The defect that forced the multiset. With one number per file, an allowance of 40 covered the file's
    // worst block and said nothing about any other, so a budgeted file could grow unboundedly many new
    // 40-line blocks and stay green. Measured when this changed: 28 over-limit blocks in the tree against
    // 19 recorded numbers — 279 lines of debt outside the budget.
    const two = `${block(40)}\nvoid A() {}\n\n${block(30)}\nvoid B() {}\n`;
    const { code, out } = run({ 'src/A.cs': two }, { commentBlockAllowances: { 'src/A.cs': 40 } });
    assert.equal(code, 1, out);
    assert.match(out, /30 lines/);
  });

  it('an allowance ARRAY budgets each block separately, and its order does not matter', () => {
    const two = `${block(40)}\nvoid A() {}\n\n${block(30)}\nvoid B() {}\n`;
    assert.equal(run({ 'src/A.cs': two }, { commentBlockAllowances: { 'src/A.cs': [40, 30] } }).code, 0);
    assert.equal(run({ 'src/A.cs': two }, { commentBlockAllowances: { 'src/A.cs': [30, 40] } }).code, 0);
    // …and it is a BUDGET per block, not a total: 35+35 does not cover 40+30.
    assert.equal(run({ 'src/A.cs': two }, { commentBlockAllowances: { 'src/A.cs': [35, 35] } }).code, 1);
  });
});

describe('check-comments — punctuation an edit left behind', () => {
  // The two fixtures below are the ACTUAL text that shipped in public XML docs, from MemorySignals.cs, and
  // each is mutation-checked against the repaired version so the rule is pinned to the defect rather than to
  // the neighbourhood it lived in.
  const SHIPPED_A = '/// calls this: it reads and writes the LIVE field instead\n///. This exists so the store';
  const SHIPPED_B = '/// an ABSENT signal or a non-finite value reads as the neutral <c>5</c> instead (\n'
    + '/// was the floor <c>1</c> — see the remarks';

  it('catches a clause deleted out from under its punctuation', () => {
    const { code, out } = run({ 'src/A.cs': `${SHIPPED_A}\nvoid A() {}\n` }, {});
    assert.equal(code, 1, out);
    assert.match(out, /a clause was deleted before it/);
  });

  it('catches a parenthesis left dangling at end of line', () => {
    const { code, out } = run({ 'src/A.cs': `${SHIPPED_B}\nvoid A() {}\n` }, {});
    assert.equal(code, 1, out);
    assert.match(out, /dangling/);
  });

  it('passes the REPAIRED text — mutation check, or the rule is matching the neighbourhood', () => {
    const repairedA = '/// calls this: it reads and writes the LIVE field instead. This exists so the store';
    const repairedB = '/// an ABSENT signal or a non-finite value reads as the neutral <c>5</c> instead — see\n'
      + '/// the remarks for why';
    assert.equal(run({ 'src/A.cs': `${repairedA}\nvoid A() {}\n` }, {}).code, 0);
    assert.equal(run({ 'src/B.cs': `${repairedB}\nvoid B() {}\n` }, {}).code, 0);
  });

  it('does NOT fire on the legitimate shapes the broad rule caught', () => {
    // Measured before the rule was narrowed: these four are every hit the obvious version produced across
    // src+tests+devtools+bench, and all four are correct prose. A gate that failed them would be switched off.
    const legit = [
      '/// .cmd, then .exe, then .ps1 (an extensionless shim cannot be spawned directly)',
      '/// without replacing the wiring: <c>services.AddHttpClient(</c>',
      '// ...and the same question asked of the DERIVED variable',
      '/// : 1e-6</c>) where SQL FLOORS',
    ].join('\n');
    const { code, out } = run({ 'src/A.cs': `${legit}\nvoid A() {}\n` }, {});
    assert.equal(code, 0, out);
  });

  it('catches two summaries fused into one doc run', () => {
    const fused = '/// <summary>Belongs to the method 40 lines below.</summary>\n'
      + '/// <summary>Belongs to this one.</summary>';
    const { code, out } = run({ 'src/A.cs': `${fused}\nvoid A() {}\n` }, {});
    assert.equal(code, 1, out);
    assert.match(out, /2 summaries in one run/);
  });

  it('does NOT fire on a summary and a remarks, nor on two summaries separated by a member', () => {
    const ok = '/// <summary>One.</summary>\n/// <remarks>Its detail.</remarks>\nvoid A() {}\n\n'
      + '/// <summary>Two.</summary>\nvoid B() {}\n';
    assert.equal(run({ 'src/A.cs': ok }, {}).code, 0);
  });

  it('the real tree is clean of stacked summaries', () => {
    const fused = trackedFiles(repo).filter((f) => (f.endsWith('.cs') || f.endsWith('.mjs')))
      .flatMap((f) => stackedSummaries(fs.readFileSync(path.join(repo, f), 'utf8')).map((s) => `${f}:${s.line}`));
    assert.deepEqual(fused, []);
  });

  it('the real tree is clean of both shapes', () => {
    const dirty = trackedFiles(repo).filter((f) => (f.endsWith('.cs') || f.endsWith('.mjs')))
      .flatMap((f) => strandedIn(fs.readFileSync(path.join(repo, f), 'utf8')).map((s) => `${f}:${s.line}`));
    assert.deepEqual(dirty, []);
  });
});

describe('check-comments — fail-closed', () => {
  it('an EMPTY source list is a broken listing, not a clean tree', () => {
    const dir = makeTree({});
    const log = recorder();
    try {
      assert.equal(checkComments(dir, {}, log, []), 1);
      assert.match(log.text(), /the file list is empty/);
    } finally { removeTree(dir); }
  });

  it('a list with no .cs survivors is legitimate — the caller chose that scope', () => {
    assert.equal(run({ 'src/notes.md': '# hi\n' }, {}).code, 0);
  });
});

describe('check-comments — against the real tree', () => {
  it('every recorded allowance names a file that still exists and is still over the limit', () => {
    // Pins the registry against the tree rather than against a fixture: an entry for a deleted or
    // already-cleaned file is exactly the rot the slack/stale rules exist to catch, and this fact fails
    // the moment one appears.
    const cfgPromise = import('../../project.config.mjs');
    return cfgPromise.then(({ default: config }) => {
      const allowances = config.commentBlockAllowances ?? {};
      assert.ok(Object.keys(allowances).length > 0, 'the registry should not be silently empty');
      const scanned = new Set(trackedFiles(repo).filter((f) => (f.endsWith('.cs') || f.endsWith('.mjs'))).map((f) => f.split('\\').join('/')));
      for (const [file, budget] of Object.entries(allowances)) {
        assert.ok(scanned.has(file), `${file} is in the registry but is not scanned`);
        const allowed = asAllowanceList(budget);
        for (const n of allowed)
          assert.ok(n > MAX_BLOCK, `${file}: an allowance at or below ${MAX_BLOCK} does nothing — delete it`);
        // The registry records EVERY over-limit block, not just the worst: that is what stops a budgeted
        // file growing new long blocks behind its recorded number.
        const actual = overLimitBlocks(fs.readFileSync(path.join(repo, file), 'utf8')).map((b) => b.length);
        assert.deepEqual(allowed, actual,
          `${file}: recorded ${JSON.stringify(allowed)} but the tree has ${JSON.stringify(actual)}`);
      }
    });
  });

  it('and the gate is green on this tree', async () => {
    const { default: config } = await import('../../project.config.mjs');
    const log = recorder();
    assert.equal(checkComments(repo, config, log), 0, log.text());
  });
});
