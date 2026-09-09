// _markers — the shared half of every index generated from authored markers.
//
// It is tested on its own rather than only through its two callers because both of the pieces it holds are
// defects this repository has already paid for once, and both fail PERMISSIVELY: a scraped attribute whose
// residue nobody checks publishes a truncation that reads like data, and a one-pass generator publishes
// line numbers that were true before it ran. A helper whose failure mode is a plausible wrong answer is
// exactly the one that must not be validated only by "its callers are green".
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  anchorProblems, blockRange, carriedEscapes, cell, escapeComments, fixedPoint, markerPattern,
  parseAttributes, spliceBlock,
} from '../_markers.mjs';

const BEGIN = '<!-- x:begin';
const END = '<!-- x:end -->';

describe('_markers — parseAttributes', () => {
  it('reads quoted and bare values alike', () => {
    const { attrs, residue } = parseAttributes('state=blocked kind=env,data needs="a key, and a download"');

    assert.equal(attrs.get('state'), 'blocked');
    assert.equal(attrs.get('kind'), 'env,data');
    assert.equal(attrs.get('needs'), 'a key, and a download');
    assert.equal(residue, '');
  });

  it('reports the RESIDUE when quotes are omitted, which is the whole reason it exists', () => {
    // Measured 2026-09-10: `needs=a real key` parsed as `needs="a"`, satisfied every rule its caller
    // checked, and published a one-word blocker that read like a complete one.
    const { attrs, residue } = parseAttributes('state=blocked kind=env needs=a real key and a download');

    assert.equal(attrs.get('needs'), 'a', 'the truncation itself is unchanged — it is what must be REPORTED');
    assert.equal(residue, 'real key and a download');
  });

  it('the residue holds no `=`, so an unknown-attribute check could never have caught it', () => {
    // The reason a caller must test the residue rather than the parsed keys: the dropped text is not a
    // malformed attribute, it is not an attribute at all.
    const { residue } = parseAttributes('state=blocked needs=one two three');

    assert.ok(residue.length > 0);
    assert.ok(!residue.includes('='), 'if this ever holds an `=` the unknown-key check would see it');
  });

  it('is blank on an empty body, so an empty marker is not reported as stray text', () => {
    assert.equal(parseAttributes('').residue, '');
    assert.equal(parseAttributes('   ').residue, '');
  });
});

describe('_markers — markerPattern', () => {
  it('matches its own kind and not a sibling kind on the same line', () => {
    const line = '- [ ] **x** <!-- link-ok: a note --> <!-- item: state=startable -->';

    assert.equal(markerPattern('item').exec(line)[1], 'state=startable');
    assert.equal(markerPattern('trap').exec(line), null);
  });

  it('stops at its own terminator rather than running to the last `>` on the line', () => {
    const line = 'a <!-- item: state=startable --> then <!-- item: state=blocked -->';

    assert.equal(markerPattern('item').exec(line)[1], 'state=startable');
  });
});

describe('_markers — escapes and cells', () => {
  it('carries only the escapes the line actually holds', () => {
    assert.deepEqual(carriedEscapes('- x <!-- link-ok: why -->'), ['link-ok']);
    assert.deepEqual(carriedEscapes('- x'), []);
    assert.match(escapeComments(['link-ok'], 'trap'), /link-ok: carried from this trap's own line/);
    assert.equal(escapeComments([], 'trap'), '');
  });

  it('escapes a pipe so one cell cannot become two', () => {
    assert.equal(cell('a | b'), 'a \\| b');
  });

  it('flattens whitespace and cuts long text, so a row cannot break the table', () => {
    assert.equal(cell('a\n  b'), 'a b');
    assert.equal(cell('x'.repeat(100)).length, 76);
    assert.match(cell('x'.repeat(100)), /…$/);
  });
});

describe('_markers — the block', () => {
  const doc = (body = []) => ['# T', '', BEGIN + ' -->', ...body, END, '', 'tail'].join('\n');

  it('finds the anchors, and returns null when either is missing', () => {
    assert.ok(blockRange(doc().split('\n'), BEGIN, END));
    assert.equal(blockRange('# T\n\ntail'.split('\n'), BEGIN, END), null);
    assert.equal(blockRange(`# T\n${BEGIN} -->\ntail`.split('\n'), BEGIN, END), null);
  });

  it('will not close a block on a line that merely QUOTES the end anchor mid-sentence', () => {
    // The end anchor is matched exactly and the begin anchor by prefix, so prose about the mechanism —
    // which both callers' own documents carry — cannot terminate a block early.
    const lines = ['# T', `${BEGIN} -->`, `prose naming ${END} inside a sentence`, END];
    const range = blockRange(lines, BEGIN, END);

    assert.equal(range.end, 3, 'the quoting line must not be taken for the terminator');
  });

  it('reports a DUPLICATE anchor, which silently moves the block a write splices into', () => {
    // Measured 2026-09-10: a second begin anchor five lines above the real one made `--write` delete a
    // document's intro paragraph and exit 0. `blockRange` takes the FIRST begin, so uniqueness is a
    // precondition rather than an assumption — and nothing downstream can notice it being violated.
    const two = ['# T', `${BEGIN} -->`, 'prose', `${BEGIN} -->`, END];

    assert.match(anchorProblems(two, BEGIN, END)[0], /delimited by the FIRST/);
    assert.deepEqual(anchorProblems(doc().split('\n'), BEGIN, END), []);
  });

  it('reports an unterminated or orphaned pair rather than treating it as absent', () => {
    assert.match(anchorProblems([`${BEGIN} -->`], BEGIN, END)[0], /never closed/);
    assert.match(anchorProblems([END], BEGIN, END)[0], /with no/);
    assert.match(anchorProblems([`${BEGIN} -->`, END, END], BEGIN, END)[0],
      /exactly one may close/);
  });

  it('does not fire on an anchor NAMED in prose, only on one that starts a line', () => {
    // The record this guards documents its own mechanism, so the anchor appears in sentences. Starting a
    // line with it is the thing that is forbidden — the same rule that stops a quoted end anchor closing
    // a block early.
    const prose = ['# T', `${BEGIN} -->`, `a duplicate \`${BEGIN}\` anchor moves it`, END];

    assert.deepEqual(anchorProblems(prose, BEGIN, END), []);
  });

  it('splices between the anchors and leaves everything else alone', () => {
    const out = spliceBlock(doc(['old']), ['new'], BEGIN, END);

    assert.match(out, /new/);
    assert.doesNotMatch(out, /old/);
    assert.match(out, /^# T/);
    assert.match(out, /tail$/);
  });
});

describe('_markers — fixedPoint', () => {
  it('converges when the body publishes the line numbers the body itself moves', () => {
    // The real case: every row carries the line number of something BELOW the block, so a one-pass
    // generator emits positions that were true of the file before it existed.
    const text = ['# T', '', BEGIN + ' -->', END, '', 'a', 'b', 'c'].join('\n');
    const render = (t) => t.split('\n')
      .map((l, i) => [l, i + 1])
      .filter(([l]) => /^[abc]$/.test(l))
      .map(([l, n]) => `- ${l} at ${n}`);

    const out = fixedPoint(text, render, BEGIN, END);
    const lines = out.split('\n');

    for (const row of lines.filter((l) => l.startsWith('- '))) {
      const [, label, n] = /^- (\w) at (\d+)$/.exec(row);
      assert.equal(lines[Number(n) - 1], label, `row "${row}" must point at the line it names`);
    }
    assert.equal(fixedPoint(out, render, BEGIN, END), out, 'a written block is already at its fixed point');
  });

  it('returns null rather than looping forever when the body never settles', () => {
    // A renderer that cannot settle is a BROKEN generator, and the dangerous outcome would be publishing
    // whichever pass happened to be last. Reported as a failure instead.
    let n = 0;
    const text = ['# T', BEGIN + ' -->', END].join('\n');

    assert.equal(fixedPoint(text, () => [`- ${n++}`], BEGIN, END), null);
  });

  it('returns null when the anchors are missing, which is a structure problem not a stale block', () => {
    assert.equal(fixedPoint('# T\n\ntail', () => ['- x'], BEGIN, END), null);
  });

  it('normalises CRLF to LF, so an EOL problem is never reported as a stale block', () => {
    const out = fixedPoint(['# T', BEGIN + ' -->', END, 'tail'].join('\r\n'), () => ['- x'], BEGIN, END);

    assert.ok(!out.includes('\r'), '.gitattributes declares LF (D95)');
  });
});
