// check-pitfalls' own tests.
//
// The gate's whole claim is that every trap in the record is findable by two facets that are ORTHOGONAL to
// its headings. That claim is worth exactly as much as the gate's willingness to FAIL — an unfiled trap, an
// invented facet value and a hand-edited index each have to be caught, and each of them fails permissively
// if it is not: the index still renders, still looks complete, and is simply missing the trap you needed.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  BLOCK_BEGIN, BLOCK_END, MAX_VALUES, RECORD, checkPitfalls, facetFixedPoint, parseTraps,
} from '../check-pitfalls.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

const VOCAB = { sub: ['gates', 'storage'], shape: ['fail-open', 'vacuous'] };

const marker = ({ sub = 'gates', shape = 'fail-open' } = {}) =>
  `<!-- trap: sub=${sub} shape=${shape} -->`;

/**
 * A traps file with the shape the gate expects. `current: false` leaves the index empty.
 *
 * The default trap set COVERS the fixture vocabulary, because an unused value is a failure — so a fixture
 * that used one value per facet would fail every test for a reason none of them is about.
 */
function pitfalls({
  traps = [{ sub: 'gates', shape: 'fail-open' }, { sub: 'storage', shape: 'vacuous' }],
  current = true, anchors = true, extra = [],
} = {}) {
  const raw = ['# Pitfalls', '',
    ...(anchors ? [`${BLOCK_BEGIN} -->`, BLOCK_END, ''] : []),
    '## Environment / tooling', '',
    ...traps.map((t, i) => `- **trap ${i + 1}.** Some prose. ${marker(t)}`),
    ...extra, ''].join('\n');
  return current && anchors ? facetFixedPoint(raw, VOCAB) : raw;
}

function run(text, config = { pitfallFacets: VOCAB }, opts = {}) {
  const dir = makeTree({ [RECORD]: text });
  const log = recorder();
  try {
    return { code: checkPitfalls(dir, config, log, opts), out: log.text(), dir };
  } finally { removeTree(dir); }
}

describe('check-pitfalls — the per-trap markers', () => {
  it('passes a fully filed record and says what it indexed', () => {
    const { code, out } = run(pitfalls());

    assert.equal(code, 0);
    assert.match(out, /2 trap\(s\) indexed by 2 area\(s\) and 2 shape\(s\)/);
  });

  it('FAILS on a trap carrying no marker, and names its line', () => {
    const text = pitfalls().replace(/ <!-- trap: [^>]*-->/, '');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /1 trap\(s\) carry no `trap:` marker/);
    assert.match(out, new RegExp(`${RECORD.replace(/[./]/g, '\\$&')}:\\d+`));
  });

  it('FAILS on a facet value outside the CLOSED vocabulary', () => {
    const { code, out } = run(pitfalls({ traps: [{ sub: 'vibes' }] }));

    assert.equal(code, 1);
    assert.match(out, /unknown sub `vibes`/);
    assert.match(out, /vocabulary is CLOSED/);
  });

  it('FAILS on a missing facet, because a trap findable by one is unfindable by the other', () => {
    const text = pitfalls({ current: false }).replace(' shape=fail-open', '');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /no `shape=`/);
  });

  it('accepts two values on a facet and REFUSES three', () => {
    assert.equal(run(pitfalls({
      traps: [{ sub: 'gates,storage', shape: 'fail-open' }, { sub: 'gates', shape: 'vacuous' }],
    })).code, 0);

    const { code, out } = run(pitfalls({
      traps: [{ sub: 'gates,storage,gates', shape: 'fail-open' }, { sub: 'gates', shape: 'vacuous' }],
      current: false,
    }));
    assert.equal(code, 1);
    assert.match(out, new RegExp(`at most ${MAX_VALUES}`));
  });

  it('FAILS on an UNQUOTED multi-word value, the residue trap _markers exists for', () => {
    const text = pitfalls({ current: false }).replace('sub=gates', 'sub=gates note=a real reason here');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /unknown facet `note`|stray text/);
  });

  it('FAILS on a vocabulary value NO trap uses, so a category cannot sit there unexpiring', () => {
    // The rule `retiredApiNames` and `check-counts` both carry, applied to a closed vocabulary: an
    // exclusion — or here a category — nobody can see rot is one that silently protects nothing.
    const { code, out } = run(pitfalls({ traps: [{ sub: 'gates', shape: 'fail-open' }] }));

    assert.equal(code, 1);
    assert.match(out, /2 vocabulary value\(s\) no trap uses/);
    assert.match(out, /unused vocabulary: sub=storage/);
    assert.match(out, /unused vocabulary: shape=vacuous/);
  });

  it('FAILS CLOSED when the vocabulary is unconfigured, rather than passing over an empty one', () => {
    const { code, out } = run(pitfalls(), {});

    assert.equal(code, 1);
    assert.match(out, /`pitfallFacets` is missing a vocabulary/);
  });

  it('FAILS CLOSED when the record holds no traps at all', () => {
    const { code, out } = run(`# Pitfalls\n\n${BLOCK_BEGIN} -->\n${BLOCK_END}\n\n## H\n\nprose only.\n`);

    assert.equal(code, 1);
    assert.match(out, /no traps found/);
  });
});

describe('check-pitfalls — what is and is not a trap', () => {
  it('ignores an INDENTED bullet, which is a sub-point of the trap above it', () => {
    const { traps } = parseTraps([
      '## H', '- **a.** x <!-- trap: sub=gates shape=fail-open -->', '  - a sub-point', '   - another',
    ], VOCAB);

    assert.equal(traps.length, 1);
  });

  it('ignores a `- ` line inside a FENCED block, which is captured output and not prose', () => {
    // The gate would otherwise demand a marker on a line it must not touch — a check whose only remedy is
    // to corrupt the thing it guards. This file quotes real captures, so the case is not hypothetical.
    const { traps, unmarked } = parseTraps([
      '## H',
      '- **a.** x <!-- trap: sub=gates shape=fail-open -->',
      '  ```',
      '- this is captured output, not a trap',
      '  ```',
      '- **b.** y <!-- trap: sub=storage shape=vacuous -->',
    ], VOCAB);

    assert.equal(unmarked.length, 0, 'a line inside a fence must not be demanded a marker');
    assert.deepEqual(traps.map((t) => t.line), [2, 6]);
  });

  it('FAILS on an UNCLOSED fence instead of silently dropping every trap below it', () => {
    // Reproduced on the real record by an adversarial review: one forgotten closing fence took 157 traps to
    // 131, `--write` published `131 traps` and exited 0, the next run was GREEN, and an unfiled trap in the
    // suppressed tail was never reported. Three of the four checks voided by a one-line prose edit. A
    // toggle is only a boundary if something asserts it balanced.
    const { code, out } = run(pitfalls({
      extra: ['', '```', 'a capture whose fence is never closed', '',
        '- **a trap in the suppressed tail.** <!-- trap: sub=gates shape=vacuous -->'],
      current: false,
    }));

    assert.equal(code, 1);
    assert.match(out, /fence opened here is never closed/);
  });

  it('and --write REFUSES to publish an index read through an unclosed fence', () => {
    const raw = pitfalls({ extra: ['', '```', 'unclosed'], current: false });
    const dir = makeTree({ [RECORD]: raw });
    const log = recorder();
    try {
      assert.equal(checkPitfalls(dir, { pitfallFacets: VOCAB }, log, { write: true }), 1);
      assert.equal(fs.readFileSync(path.join(dir, RECORD), 'utf8'), raw, 'the file must be untouched');
    } finally { removeTree(dir); }
  });

  it('FAILS on a DUPLICATE begin anchor, which silently moves the block a write splices into', () => {
    // The same review put a second begin anchor five lines above the real one: `--write` deleted the
    // document's intro paragraph and exited 0, reporting nothing. `blockRange` takes the FIRST.
    const raw = pitfalls({ current: false }).replace('# Pitfalls', `# Pitfalls\n${BLOCK_BEGIN} -->`);
    const dir = makeTree({ [RECORD]: raw });
    const log = recorder();
    try {
      assert.equal(checkPitfalls(dir, { pitfallFacets: VOCAB }, log, { write: true }), 1);
      assert.match(log.text(), /anchors — the block is delimited by the FIRST/);
      assert.equal(fs.readFileSync(path.join(dir, RECORD), 'utf8'), raw, 'the file must be untouched');
    } finally { removeTree(dir); }
  });

  it('an anchor QUOTED inside a fence cannot arm the block skip, because the block is located once', () => {
    // The two derivations of one boundary used to disagree: the parser armed on any line carrying the
    // begin prefix while the splicer took the first. Now both call `blockRange`, so a fenced example of
    // the mechanism — the kind of self-documenting prose this record grows — suppresses nothing.
    const { traps, unmarked } = parseTraps([
      '# T', `${BLOCK_BEGIN} -->`, BLOCK_END, '', '## H',
      '- **a.** x <!-- trap: sub=gates shape=fail-open -->',
      '```', `${BLOCK_BEGIN} -->`, '```',
      '- **b.** y <!-- trap: sub=storage shape=vacuous -->',
    ], VOCAB);

    assert.deepEqual(traps.map((t) => t.line), [6, 10]);
    assert.equal(unmarked.length, 0);
  });

  it('reads the heading, the line and the facets off the file', () => {
    const [trap] = parseTraps([
      '## Storage', '- **a thing.** x <!-- trap: sub=storage,gates shape=vacuous -->',
    ], VOCAB).traps;

    assert.equal(trap.line, 2);
    assert.equal(trap.heading, 'Storage');
    assert.equal(trap.lead, 'a thing.');
    assert.deepEqual(trap.sub, ['storage', 'gates']);
    assert.deepEqual(trap.shape, ['vacuous']);
  });
});

describe('check-pitfalls — the generated index', () => {
  it('FAILS when the anchors are missing, naming what to add', () => {
    const { code, out } = run(pitfalls({ anchors: false }));

    assert.equal(code, 1);
    assert.match(out, /facets:begin/);
    assert.match(out, /facets:end/);
  });

  it('FAILS when the index disagrees with the markers, and names the command that fixes it', () => {
    const stale = pitfalls().replace(/^- \*\*`gates`\*\* \(1\) — \d+$/m, '- **`gates`** (1) — 999');
    const { code, out } = run(stale);

    assert.equal(code, 1);
    assert.match(out, /index .* is STALE/i);
    assert.match(out, /check-pitfalls --write/);
  });

  it('--write regenerates the index, and the check then passes', () => {
    const dir = makeTree({ [RECORD]: pitfalls({ current: false }) });
    const log = recorder();
    try {
      assert.equal(checkPitfalls(dir, { pitfallFacets: VOCAB }, log, { write: true }), 0, log.text());
      assert.match(log.text(), /regenerated/);
      assert.equal(checkPitfalls(dir, { pitfallFacets: VOCAB }, recorder()), 0);
    } finally { removeTree(dir); }
  });

  it('and --write REFUSES to publish an index built from a broken marker', () => {
    const raw = pitfalls({ traps: [{ sub: 'vibes' }], current: false });
    const dir = makeTree({ [RECORD]: raw });
    const log = recorder();
    try {
      assert.equal(checkPitfalls(dir, { pitfallFacets: VOCAB }, log, { write: true }), 1);
      assert.equal(fs.readFileSync(path.join(dir, RECORD), 'utf8'), raw, 'the file must be untouched');
    } finally { removeTree(dir); }
  });

  it('the index carries every trap under BOTH of its facets, with line numbers that point at it', () => {
    const text = pitfalls();
    const lines = text.split('\n');
    const { traps } = parseTraps(lines, VOCAB);
    const block = text.slice(text.indexOf(BLOCK_BEGIN), text.indexOf(BLOCK_END));

    assert.equal(traps.length, 2);
    for (const trap of traps) {
      assert.match(lines[trap.line - 1], /^- \*\*trap /, `line ${trap.line} must be the trap it names`);
      for (const facet of ['sub', 'shape'])
        for (const value of trap[facet])
          assert.match(block, new RegExp(`\\*\\*\`${value}\`\\*\\*[^\\n]*\\b${trap.line}\\b`),
            `${facet}=${value} must list line ${trap.line}`);
    }
  });

  it('the real record is fully filed and its index is current', () => {
    const log = recorder();
    // Read the REAL config rather than the fixture vocabulary — the point is the tree, not the fixture.
    return import('../../project.config.mjs').then(({ default: real }) => {
      assert.equal(checkPitfalls(repo, real, log), 0, log.text());
      const { traps, unmarked } = parseTraps(
        fs.readFileSync(path.join(repo, RECORD), 'utf8').split(/\r?\n/), real.pitfallFacets);
      assert.equal(unmarked.length, 0, 'every trap in the record must carry a marker');
      assert.ok(traps.length > 100, `the record must hold its traps; found ${traps.length}`);
    });
  });
});
