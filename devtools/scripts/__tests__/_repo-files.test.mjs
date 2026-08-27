// _repo-files — repoFiles + twoLineWindows. See devtools/scripts/_repo-files.mjs.
//
// twoLineWindows is the shared window builder check-counts, check-docs and check-links all depend on to
// catch a claim that wraps across a line break. It used to be verbatim copies, and none of them trimmed
// anything — an indented continuation's leading whitespace rode straight into the join, so a claim wrapping
// into a nested or bulleted block acquired extra spaces mid-phrase and a pattern anchored on single-space
// adjacency never matched it. check-counts printed a clean run over a stale claim shaped exactly that way.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { twoLineWindows } from '../_repo-files.mjs';

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
