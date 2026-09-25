// _repo-files — repoFiles + twoLineWindows. See devtools/scripts/_repo-files.mjs.
//
// twoLineWindows is the shared window builder check-counts, check-docs and check-links all depend on to
// catch a claim that wraps across a line break. It used to be verbatim copies, and none of them trimmed
// anything — an indented continuation's leading whitespace rode straight into the join, so a claim wrapping
// into a nested or bulleted block acquired extra spaces mid-phrase and a pattern anchored on single-space
// adjacency never matched it. check-counts printed a clean run over a stale claim shaped exactly that way.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { readRepoText, twoLineWindows, windowHits } from '../_repo-files.mjs';
import { makeTree, removeTree } from './_fixtures.mjs';

describe('readRepoText — the one read rule every scanning gate shares', () => {
  it('returns the text, or null for a listed file gone from the working tree (a pending deletion)', (t) => {
    const dir = makeTree({ 'docs/a.md': 'hello\n' });
    t.after(() => removeTree(dir));
    assert.equal(readRepoText(dir, 'docs/a.md'), 'hello\n');
    assert.equal(readRepoText(dir, 'docs/gone.md'), null);
  });

  it('THROWS on any other read error — a file a gate cannot read is one it cannot prove clean', (t) => {
    const dir = makeTree({ 'docs/dir.md/inner.txt': 'x' });   // `docs/dir.md` reads as EISDIR
    t.after(() => removeTree(dir));
    assert.throws(() => readRepoText(dir, 'docs/dir.md'), /docs\/dir\.md: could not be read \(EISDIR\)/);
  });
});

describe('windowHits — the one "line alone, else the two-line window" rule the prose gates share', () => {
  const WIDGET = /\bWidget\b/g;

  it('reports a hit lying wholly on one line ONCE, at that line — never again from the window above it', () => {
    // The double report check-docs shipped: line N-1's window contains a hit lying wholly on line N, so
    // every occurrence was reported twice and half the `file:line` pointers named the wrong line.
    const hits = windowHits(['an intro line', 'the Widget is here', 'a tail'], WIDGET);
    assert.deepEqual(hits.map((h) => h.at), [1]);
    assert.equal(hits[0].straddles, false);
  });

  it('reports a claim broken ACROSS the wrap at the line it begins on', () => {
    const hits = windowHits(['the library ships seven', 'widgets; that is the set'], /seven widgets/g);
    assert.deepEqual(hits.map((h) => [h.at, h.straddles]), [[0, true]]);
  });

  it('takes a match that extends across the join WHOLE — the range whose second end is on the next line', () => {
    const hits = windowHits(['see §7–', '8 for the rest'], /§(\d+(?:\s*–\s*\d+)?)/g);
    assert.equal(hits.length, 1);
    assert.equal(hits[0].match[1], '7– 8');
  });

  it('excuses a hit wholly on line N only by line N\'s OWN escape — never by an unrelated next line', () => {
    const hits = windowHits(['the Widget is here', 'an unrelated line <!-- ok -->'], WIDGET, { escape: 'ok' });
    assert.equal(hits.length, 1);
    assert.equal(hits[0].escaped, false, 'a following line\'s escape is not this line\'s');
  });

  it('excuses a hit straddling the wrap by the escape on EITHER line', () => {
    const lines = ['the library ships seven', 'widgets; the set <!-- ok -->'];
    const [hit] = windowHits(lines, /seven widgets/g, { escape: 'ok' });
    assert.equal(hit.escaped, true);
  });

  it('a match the line alone sees is not excused by the NEXT line even when the window extends it', () => {
    // A named §-citation at a line's end runs greedily into the next line's words; it still lies on line N.
    const lines = ['see `pitfalls.md` §Storage', 'records three <!-- ok -->'];
    const [hit] = windowHits(lines, /§([A-Z]\w*(?:\s+[a-z]\w*)*)/g, { escape: 'ok' });
    assert.equal(hit.match[1], 'Storage records three', 'the window match is the one returned');
    assert.equal(hit.escaped, false);
  });

  it('keeps an escaped hit in the result, flagged, so a caller can still COUNT it', () => {
    const [hit] = windowHits(['the Widget <!-- ok -->'], WIDGET, { escape: 'ok' });
    assert.equal(hit.escaped, true);
  });

  it('reads caller-supplied windows, so a code tier can strip its own comment marker from the continuation', () => {
    const lines = ['// the library ships seven', '// widgets; the set'];
    const windows = [`${lines[0]} widgets; the set`, lines[1]];
    assert.equal(windowHits(lines, /seven widgets/g, { windows }).length, 1);
  });
});

describe('twoLineWindows', () => {
  it('joins a line to an UNINDENTED continuation with a single space (unchanged behaviour)', () => {
    const [w0] = twoLineWindows(['The library ships seven', 'widgets; that is the set.']);
    assert.equal(w0, 'The library ships seven widgets; that is the set.');
  });

  it('joins a line to an INDENTED continuation, stripping ITS leading whitespace before the join', () => {
    // The demonstrated defect: without the strip this reads "…proved by" + " " + "      seven goldens",
    // and a pattern like /proved by (\w+) goldens/ (single literal space) never crosses the extra spaces.
    const [w0] = twoLineWindows(['byte-identical when unset, proved by', '      seven goldens']);
    assert.equal(w0, 'byte-identical when unset, proved by seven goldens');
  });

  it('strips a BLOCKQUOTE marker off the continuation — markdown\'s version of the same defect', () => {
    // Measured 2026-09-12: a retired claim wrapping inside a `>` block joined as "…never a" + " " +
    // "> generator asked to choose", putting the marker exactly where the pattern wanted a space. This
    // repository keeps its standing working positions in blockquotes, so that was the one place a wrapped
    // claim could not be read — and check-docs reported the file clean while the claim was false.
    const [w0] = twoLineWindows(['> the evidence points at a scorer, never a', '> generator asked to choose']);
    assert.equal(w0, '> the evidence points at a scorer, never a generator asked to choose');
  });

  it('strips an INDENTED and NESTED blockquote marker, in that order', () => {
    const [w0] = twoLineWindows(['a claim that wraps', '  > > deep inside a quote']);
    assert.equal(w0, 'a claim that wraps deep inside a quote');
  });

  it('leaves a `>` that is not a line-leading marker alone', () => {
    // `>` is only a blockquote at the START of a line; mid-line it is content (a comparison, an arrow).
    const [w0] = twoLineWindows(['the threshold is', 'x > 6 for every rung']);
    assert.equal(w0, 'the threshold is x > 6 for every rung');
  });

  it('never touches the FIRST line — only the continuation is trimmed', () => {
    // Load-bearing: check-counts's duplicate-report guard (`m.index >= line.length + 1`) anchors the join
    // boundary at the RAW first line's own length. Trimming the first line's tail would move that boundary
    // and mis-fire the guard silently. Pinned here as a property of the builder itself, not just of the
    // gate that consumes it.
    const first = '   leading space that must survive   ';
    const [w0] = twoLineWindows([first, 'next line']);
    assert.ok(w0.startsWith(first), 'the first line must appear byte-for-byte, untrimmed, at the window start');
    assert.equal(w0, `${first} next line`);
  });

  it('keeps the join boundary at exactly line.length + 1 — the offset check-counts guards against', () => {
    const lines = ['abc', '   def'];
    const [w0] = twoLineWindows(lines);
    // The continuation's content (post-trim) must start exactly where the RAW first line's length says it
    // does — one join space past lines[0].length — regardless of how much indentation was stripped.
    assert.equal(w0.slice(lines[0].length + 1), 'def');
  });

  it('the LAST line has no successor and is returned unchanged', () => {
    const windows = twoLineWindows(['first', 'second']);
    assert.equal(windows[windows.length - 1], 'second');
    assert.equal(windows.length, 2);
  });

  it('an EMPTY continuation joins as the line plus a trailing space, and matches nothing extra', () => {
    const [w0] = twoLineWindows(['some prose', '']);
    assert.equal(w0, 'some prose ');
  });

  it('a WHITESPACE-ONLY continuation trims to empty, same as an empty one', () => {
    const [w0] = twoLineWindows(['some prose', '   \t  ']);
    assert.equal(w0, 'some prose ');
  });

  it('an empty input array produces no windows', () => {
    assert.deepEqual(twoLineWindows([]), []);
  });
});
