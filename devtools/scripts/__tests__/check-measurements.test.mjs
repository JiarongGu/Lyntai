// check-measurements' own tests.
//
// The gate's claim is that a reader can learn, from the head of the record alone, which arm SHIPS and
// whether a figure is still CURRENT. Every one of those words is worth only as much as the gate's
// willingness to FAIL, and each check here fails PERMISSIVELY if it is not pinned: the index still renders,
// still looks complete, and quietly reports a superseded number as live — which is the exact defect
// measured on the record this gate was built for.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  BLOCK_BEGIN, BLOCK_END, ESCAPE, RECORD, checkMeasurements, indexFixedPoint, parseResults, resolve,
} from '../check-measurements.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

const VOCAB = ['miss', 'evidence-hit@k'];

const marker = ({
  id = 'a-result', arm = 'shipped defaults', metric = 'miss', n = '200', value = '0.25',
  ships = 'no', status = 'CURRENT', supersedes = null,
} = {}) => `<!-- result: id=${id} arm="${arm}" metric=${metric} n="${n}" value="${value}" `
  + `ships=${ships} status=${status}${supersedes ? ` supersedes="${supersedes}"` : ''} -->`;

/**
 * A measurement record with the shape the gate expects. `current: false` leaves the index empty.
 *
 * The default rows COVER the fixture vocabulary, because an unused metric is a failure — a fixture using
 * one value would fail every test for a reason none of them is about.
 */
function record({
  rows = [{ id: 'first', metric: 'miss' }, { id: 'second', metric: 'evidence-hit@k' }],
  current = true, anchors = true, body = [], sections = null,
} = {}) {
  const raw = ['# Memory measurements', '',
    ...(anchors ? [`${BLOCK_BEGIN} -->`, BLOCK_END, ''] : []),
    '## 5. What was measured', '',
    ...(sections ?? rows.flatMap((r, i) => [`### Section ${i + 1} ${marker(r)}`, '', 'Some prose.', ''])),
    ...body, ''].join('\n');
  return current && anchors ? indexFixedPoint(raw, VOCAB) : raw;
}

function run(text, config = { measurementMetrics: VOCAB }, opts = {}) {
  const dir = makeTree({ [RECORD]: text });
  const log = recorder();
  try {
    return { code: checkMeasurements(dir, config, log, opts), out: log.text(), dir };
  } finally { removeTree(dir); }
}

describe('check-measurements — the per-result markers', () => {
  it('passes a fully marked record and says what it indexed', () => {
    const { code, out } = run(record());

    assert.equal(code, 0);
    assert.match(out, /2 result\(s\) across 2 section\(s\)/);
  });

  it('FAILS on a section that declares neither a result nor a reason', () => {
    const { code, out } = run(record({
      current: false,
      sections: ['### Measured something', '', 'Some prose.', ''],
    }), { measurementMetrics: ['miss'] });

    assert.equal(code, 1);
    assert.match(out, /1 section\(s\) declare neither a result nor a reason/);
    assert.match(out, /no result and no reason — Measured something/);
  });

  it('accepts a `result-free:` section, because not every section measures something', () => {
    const { code, out } = run(record({
      rows: [{ id: 'first', metric: 'miss' }, { id: 'second', metric: 'evidence-hit@k' }],
      body: ['### The machine these numbers come from <!-- result-free: a hardware spec, not a result -->',
        '', 'Some prose.'],
    }));

    assert.equal(code, 0);
    assert.match(out, /3 section\(s\) \(1 result-free\)/);
  });

  it('FAILS a `result-free:` with no reason, so an exemption can always be reviewed', () => {
    const { code, out } = run(record({
      current: false,
      body: ['### Context <!-- result-free: -->', '', 'Some prose.'],
    }));

    assert.equal(code, 1);
    assert.match(out, /needs a REASON/);
  });

  it('FAILS a section carrying a result AND an exemption, which claims both at once', () => {
    const { code, out } = run(record({
      current: false,
      body: [`### Both ${marker({ id: 'third' })} <!-- result-free: also exempt -->`, '', 'Some prose.'],
    }));

    assert.equal(code, 1);
    assert.match(out, /carry a result AND a result-free exemption/);
  });

  it('FAILS on a missing required attribute, naming it', () => {
    const text = record({ current: false }).replace(' n="200"', '');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /no `n=`/);
  });

  it('FAILS on an UNQUOTED multi-word value, the residue trap `_markers` exists for', () => {
    const text = record({ current: false }).replace('arm="shipped defaults"', 'arm=shipped defaults');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /stray text `defaults`/);
  });

  it('FAILS a marker containing `>`, which otherwise matches NOTHING and vanishes in silence', () => {
    // Found on the first real run: an `arm` reading "best threshold `>= 6`" disappeared entirely, and the
    // only thing that noticed was the closed vocabulary reporting its metric as unused. A commoner metric
    // would have hidden it completely — a row simply absent from the index it is the point of.
    const text = record({ current: false }).replace('arm="shipped defaults"', 'arm="threshold >= 6"');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /contains `>` and therefore matches NOTHING/);
  });

  it('FAILS on an unknown attribute', () => {
    const text = record({ current: false }).replace('ships=no', 'ships=no notes=x');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /unknown attribute `notes`/);
  });

  it('FAILS on a metric outside the CLOSED vocabulary', () => {
    const { code, out } = run(record({
      current: false, rows: [{ id: 'first', metric: 'vibes' }, { id: 'second', metric: 'evidence-hit@k' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /unknown metric `vibes`/);
    assert.match(out, /vocabulary is CLOSED/);
  });

  it('FAILS on a vocabulary metric NO result uses, so a slug cannot sit there unexpiring', () => {
    // The rule `retiredApiNames`, `check-counts` and `pitfallFacets` all carry: a registry entry nobody can
    // see rot is one that silently protects nothing.
    const { code, out } = run(record({ rows: [{ id: 'only', metric: 'miss' }] }));

    assert.equal(code, 1);
    assert.match(out, /1 metric\(s\) in the vocabulary no result uses/);
    assert.match(out, /unused metric: evidence-hit@k/);
  });

  it('FAILS a hand-written SUPERSEDED, and says where it is derived from instead', () => {
    const { code, out } = run(record({
      current: false,
      rows: [{ id: 'first', metric: 'miss', status: 'SUPERSEDED' }, { id: 'second', metric: 'evidence-hit@k' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /is never authored/);
    assert.match(out, /supersedes=/);
  });

  it('FAILS on a `ships=` outside yes/no, because the column is read as a recommendation', () => {
    const { code, out } = run(record({
      current: false, rows: [{ id: 'first', metric: 'miss', ships: 'maybe' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /`ships=maybe`/);
  });

  it('FAILS CLOSED when the vocabulary is unconfigured, rather than passing over an empty one', () => {
    const { code, out } = run(record(), {});

    assert.equal(code, 1);
    assert.match(out, /`measurementMetrics` is empty/);
  });

  it('FAILS CLOSED when the record holds no sections at all', () => {
    const { code, out } = run(`# M\n\n${BLOCK_BEGIN} -->\n${BLOCK_END}\n\n## 5. x\n\nprose only.\n`);

    assert.equal(code, 1);
    assert.match(out, /found no result sections/);
  });
});

describe('check-measurements — supersession, and why it is DERIVED', () => {
  it('derives SUPERSEDED from a later row, and points at the row that did it', () => {
    const text = record({
      rows: [{ id: 'old', metric: 'miss' },
        { id: 'new', metric: 'evidence-hit@k', supersedes: 'old' }],
    });
    const { rows } = parseResults(text.split('\n'), VOCAB);
    const { state } = resolve(rows);

    assert.equal(state.get(rows[0]).status, 'SUPERSEDED');
    assert.equal(state.get(rows[0]).by, rows[1]);
    assert.equal(state.get(rows[1]).status, 'CURRENT');
    assert.match(text, new RegExp(`SUPERSEDED by L${rows[1].line}`));
  });

  it('lets an authored RETRACTED win over a derived supersession — the stronger claim', () => {
    const text = record({
      current: false,
      rows: [{ id: 'old', metric: 'miss', status: 'RETRACTED' },
        { id: 'new', metric: 'evidence-hit@k', supersedes: 'old' }],
    });
    const { rows } = parseResults(text.split('\n'), VOCAB);

    assert.equal(resolve(rows).state.get(rows[0]).status, 'RETRACTED');
  });

  it('FAILS on a duplicate id, which would make the handle ambiguous', () => {
    const { code, out } = run(record({
      current: false, rows: [{ id: 'same', metric: 'miss' }, { id: 'same', metric: 'evidence-hit@k' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /duplicate id `same`/);
  });

  it('FAILS on a `supersedes=` that names nothing', () => {
    const { code, out } = run(record({
      current: false,
      rows: [{ id: 'first', metric: 'miss' },
        { id: 'second', metric: 'evidence-hit@k', supersedes: 'ghost' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /`supersedes=ghost` names no result/);
  });

  it('ALLOWS a forward supersedes — a heading announces the correction and quotes the old figure below', () => {
    // The first draft required backwards-only ("chronological, so a replacement is written below"). The
    // real record refutes it in 6 of 76 cases, all this shape, so the constraint is acyclicity instead.
    const { code, out } = run(record({
      rows: [{ id: 'first', metric: 'miss', supersedes: 'second' },
        { id: 'second', metric: 'evidence-hit@k' }],
    }));

    assert.equal(code, 0, out);
  });

  it('FAILS on a CYCLE, which is what backwards-only was really buying', () => {
    const { code, out } = run(record({
      current: false,
      rows: [{ id: 'first', metric: 'miss', supersedes: 'second' },
        { id: 'second', metric: 'evidence-hit@k', supersedes: 'first' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /forms a CYCLE/);
    assert.match(out, /both the live figure and the dead one/);
  });

  it('FAILS on a row that supersedes itself', () => {
    const { code, out } = run(record({
      current: false,
      rows: [{ id: 'first', metric: 'miss' },
        { id: 'second', metric: 'evidence-hit@k', supersedes: 'second' }],
    }));

    assert.equal(code, 1);
    assert.match(out, /names its own id/);
  });
});

describe('check-measurements — the currency check', () => {
  it('FAILS a heading that announces a RETRACTION no result under it records', () => {
    const { code, out } = run(record({
      current: false,
      sections: [`### RETRACTED — the walk is not superadditive ${marker({ id: 'first', metric: 'miss' })}`,
        '', 'Some prose.', '',
        `### Section 2 ${marker({ id: 'second', metric: 'evidence-hit@k' })}`, '', 'Some prose.', ''],
    }));

    assert.equal(code, 1);
    assert.match(out, /1 section heading\(s\) announce a retraction no result under them records/);
    assert.match(out, /this heading says "RETRACTED" and every result under it reads CURRENT/);
  });

  it('clears on `status=RETRACTED` under it', () => {
    const { code, out } = run(record({
      rows: [{ id: 'first', metric: 'miss' }, { id: 'second', metric: 'evidence-hit@k' }],
      sections: [`### Section 1 ${marker({ id: 'first', metric: 'miss' })}`, '', 'Some prose.', '',
        `### (n = 200, superseded above) the old figure `
          + `${marker({ id: 'second', metric: 'evidence-hit@k', status: 'RETRACTED' })}`, '', 'Some prose.', ''],
    }));

    assert.equal(code, 0, out);
  });

  it('clears on a row naming what it supersedes — the bookkeeping the index needs anyway', () => {
    const { code, out } = run(record({
      sections: [`### Section 1 ${marker({ id: 'first', metric: 'miss' })}`, '', 'Some prose.', '',
        `### The bar above is now STALE `
          + `${marker({ id: 'second', metric: 'evidence-hit@k', supersedes: 'first' })}`, '', 'Some prose.', ''],
    }));

    assert.equal(code, 0, out);
  });

  it('IGNORES the same vocabulary in a BODY, which was measured at 63 hits and zero defects', () => {
    // `stale@k` is a METRIC on the real record and "the superseded fact" is the phenomenon it measures, so
    // a body scan is a noise generator. Pinned as behaviour, not left as a comment.
    const { code, out } = run(record({
      sections: [`### Section 1 ${marker({ id: 'first', metric: 'miss' })}`, '',
        'The judge endorses the SUPERSEDED fact 3.4x more often, and `stale@k` goes 44.3% to 95.7%.', '',
        `### Section 2 ${marker({ id: 'second', metric: 'evidence-hit@k' })}`, '', 'A CORRECTION, not a'
          + ' recurrence.', ''],
    }));

    assert.equal(code, 0, out);
  });

  it(`is silenced on the heading by \`${ESCAPE}\`, and by no other gate's token`, () => {
    const heading = (token) => record({
      sections: [`### Does a CORRECTION separate from a RECURRENCE? ${marker({ id: 'first', metric: 'miss' })}`
        + (token ? ` <!-- ${token}: the domain sense, not a retraction -->` : ''), '', 'Some prose.', '',
      `### Section 2 ${marker({ id: 'second', metric: 'evidence-hit@k' })}`, '', 'Some prose.', ''],
    });

    assert.equal(run(heading(ESCAPE)).code, 0, 'its own token must silence it');
    // `drift-ok` is check-docs' and must not reach this gate — one token silencing two is the hole
    // `.claude/rules` names outright.
    assert.equal(run(heading('drift-ok')).code, 1, "another gate's token must NOT");
    assert.equal(run(heading(null)).code, 1);
  });

  it('lets a section carry TWO markers, which is what a mid-section retraction needs', () => {
    // THE LOAD-BEARING CASE for the INDEX: this record retracts inline, so one section holds a live result
    // and a dead one, and a heading-level index cannot show them apart.
    const text = record({
      sections: [
        `### Section 1 ${marker({ id: 'first', metric: 'miss' })}`, '',
        'Some prose about the current figure.', '',
        marker({ id: 'first-old', metric: 'evidence-hit@k', status: 'RETRACTED' }), '',
        'The earlier number here no longer holds.', ''],
    });
    const { rows } = parseResults(text.split('\n'), VOCAB);

    assert.equal(run(text).code, 0);
    assert.equal(rows.length, 2);
    assert.equal(rows[0].section, rows[1].section, 'both rows belong to the one section');
    assert.match(text, /\| RETRACTED \|/);
  });
});

describe('check-measurements — the generated index', () => {
  it('FAILS when the anchors are missing, naming what to add', () => {
    const { code, out } = run(record({ anchors: false }));

    assert.equal(code, 1);
    assert.match(out, /results:begin/);
    assert.match(out, /results:end/);
  });

  it('FAILS when the index disagrees with the markers, and names the command that fixes it', () => {
    const stale = record().replace(/\| 200 \|/, '| 999 |');
    const { code, out } = run(stale);

    assert.equal(code, 1);
    assert.match(out, /index .* is STALE/i);
    assert.match(out, /check-measurements --write/);
  });

  it('--write regenerates the index, and the check then passes', () => {
    const dir = makeTree({ [RECORD]: record({ current: false }) });
    const log = recorder();
    try {
      assert.equal(checkMeasurements(dir, { measurementMetrics: VOCAB }, log, { write: true }), 0, log.text());
      assert.match(log.text(), /regenerated/);
      assert.equal(checkMeasurements(dir, { measurementMetrics: VOCAB }, recorder()), 0);
    } finally { removeTree(dir); }
  });

  it('and --write REFUSES to publish an index built from a broken marker', () => {
    const raw = record({ current: false, rows: [{ id: 'first', metric: 'vibes' }] });
    const dir = makeTree({ [RECORD]: raw });
    const log = recorder();
    try {
      assert.equal(checkMeasurements(dir, { measurementMetrics: VOCAB }, log, { write: true }), 1);
      assert.equal(fs.readFileSync(path.join(dir, RECORD), 'utf8'), raw, 'the file must be untouched');
    } finally { removeTree(dir); }
  });

  it('FAILS on an UNCLOSED fence instead of silently dropping every result below it', () => {
    // Measured on this gate's sibling: one forgotten closing fence took 157 traps to 131, `--write`
    // published the truncation and exited 0. A toggle is only a boundary if something asserts it balanced.
    const { code, out } = run(record({
      current: false,
      body: ['', '```', 'a capture whose fence is never closed', '',
        `### A result in the suppressed tail ${marker({ id: 'third' })}`],
    }));

    assert.equal(code, 1);
    assert.match(out, /fence opened here is never closed/);
  });

  it('FAILS on a DUPLICATE begin anchor, which silently moves the block a write splices into', () => {
    const raw = record({ current: false }).replace('# Memory measurements',
      `# Memory measurements\n${BLOCK_BEGIN} -->`);
    const dir = makeTree({ [RECORD]: raw });
    const log = recorder();
    try {
      assert.equal(checkMeasurements(dir, { measurementMetrics: VOCAB }, log, { write: true }), 1);
      assert.match(log.text(), /anchors — the block is delimited by the FIRST/);
      assert.equal(fs.readFileSync(path.join(dir, RECORD), 'utf8'), raw, 'the file must be untouched');
    } finally { removeTree(dir); }
  });

  it('the index carries every row, with a line number that points at its marker', () => {
    const text = record();
    const lines = text.split('\n');
    const { rows } = parseResults(lines, VOCAB);
    const block = text.slice(text.indexOf(BLOCK_BEGIN), text.indexOf(BLOCK_END));

    assert.equal(rows.length, 2);
    for (const row of rows) {
      assert.match(lines[row.line - 1], /<!-- result: /, `line ${row.line} must be the marker it names`);
      assert.match(block, new RegExp(`^\\| ${row.line} \\|`, 'm'), `the index must list line ${row.line}`);
    }
  });

  it('reports how many rows measure the arm that SHIPS, which is the first question a reader asks', () => {
    const text = record({
      rows: [{ id: 'first', metric: 'miss', ships: 'yes' }, { id: 'second', metric: 'evidence-hit@k' }],
    });

    assert.match(text, /\*\*1 of 2 rows measure the arm that actually SHIPS\.\*\*/);
    assert.match(text, /\*\*ships\*\*/);
  });

  it('the parser reads the real record — its sections and results, not an empty read', () => {
    // The REAL config, not the fixture vocabulary — the subject is the tree, not the fixture.
    return import('../../project.config.mjs').then(({ default: real }) => {
      const { rows, sections } = parseResults(
        fs.readFileSync(path.join(repo, RECORD), 'utf8').split(/\r?\n/), real.measurementMetrics);
      assert.ok(sections.length > 40, `the record must hold its sections; found ${sections.length}`);
      assert.ok(rows.length > 40, `the record must hold its results; found ${rows.length}`);
      assert.ok(rows.some((r) => r.ships === 'yes'), 'some measured arm must be the shipped one');
    });
  });
});
