// _tree-fingerprint — verify's own integrity check. See devtools/scripts/_tree-fingerprint.mjs.
//
// The defect it exists for: editing a file WHILE `verify` runs makes every line it printed describe a tree
// that no longer exists, including `✓ all 16 gates green`. Sixteen green lines and a green summary, over an
// indeterminate mix of before and after. Happened twice in one session (2026-08-30) and both times the only
// thing that caught it was the author remembering — so the point of these tests is that the detection
// cannot itself pass permissively.
import assert from 'node:assert/strict';
import { utimesSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { fingerprintDrift, fingerprintTree } from '../_tree-fingerprint.mjs';
import { makeTree, removeTree } from './_fixtures.mjs';

describe('fingerprintTree', () => {
  it('is STABLE when nothing changes — the green path must not cry wolf', (t) => {
    const dir = makeTree({ 'a.md': 'hello' });
    t.after(() => removeTree(dir));

    assert.deepEqual(fingerprintTree(dir, ['a.md']), fingerprintTree(dir, ['a.md']));
  });

  it('does NOT depend on mtime — a byte-identical rewrite is not drift', (t) => {
    // The whole reason this hashes CONTENT. An mtime-based check would report every touch as a change, and
    // a gate that cries wolf is the one people learn to ignore.
    const dir = makeTree({ 'a.md': 'hello' });
    t.after(() => removeTree(dir));
    const before = fingerprintTree(dir, ['a.md']);

    writeFileSync(join(dir, 'a.md'), 'hello');               // same bytes...
    const later = new Date(Date.now() + 60_000);
    utimesSync(join(dir, 'a.md'), later, later);             // ...deliberately newer mtime

    assert.deepEqual(fingerprintDrift(before, fingerprintTree(dir, ['a.md'])), []);
  });

  it('records an unreadable or absent file as ABSENT rather than skipping it', (t) => {
    const dir = makeTree({});
    t.after(() => removeTree(dir));

    // Skipping would make a file DELETED mid-run compare equal to one that was never there.
    assert.equal(fingerprintTree(dir, ['never-existed.md']).get('never-existed.md'), 'ABSENT');
  });
});

describe('fingerprintDrift', () => {
  it('reports a MODIFIED file — the case that makes a green summary a lie', (t) => {
    const dir = makeTree({ 'a.md': 'before' });
    t.after(() => removeTree(dir));
    const before = fingerprintTree(dir, ['a.md']);

    writeFileSync(join(dir, 'a.md'), 'after');

    assert.deepEqual(fingerprintDrift(before, fingerprintTree(dir, ['a.md'])), ['modified: a.md']);
  });

  it('reports a file ADDED mid-run — the untracked-work case index-only scoping would miss', (t) => {
    const dir = makeTree({ 'a.md': 'x' });
    t.after(() => removeTree(dir));
    const before = fingerprintTree(dir, ['a.md']);

    writeFileSync(join(dir, 'b.md'), 'new');

    assert.deepEqual(fingerprintDrift(before, fingerprintTree(dir, ['a.md', 'b.md'])), ['added:    b.md']);
  });

  it('reports a file REMOVED mid-run', (t) => {
    const dir = makeTree({ 'a.md': 'x', 'b.md': 'y' });
    t.after(() => removeTree(dir));
    const before = fingerprintTree(dir, ['a.md', 'b.md']);

    assert.deepEqual(fingerprintDrift(before, fingerprintTree(dir, ['a.md'])), ['removed:  b.md']);
  });

  it('reports EVERY drifting file, not just the first — a partial list reads as a small problem', (t) => {
    const dir = makeTree({ 'a.md': 'a1', 'b.md': 'b1' });
    t.after(() => removeTree(dir));
    const before = fingerprintTree(dir, ['a.md', 'b.md']);

    writeFileSync(join(dir, 'a.md'), 'a2');
    writeFileSync(join(dir, 'b.md'), 'b2');

    assert.deepEqual(
      fingerprintDrift(before, fingerprintTree(dir, ['a.md', 'b.md'])),
      ['modified: a.md', 'modified: b.md']);
  });

  it('is EMPTY for two identical fingerprints — the positive control', (t) => {
    // Without this the suite would pass on an implementation that reports EVERYTHING as drift. That fails in
    // the safe direction, but it would make `verify` permanently red and be switched off within a day.
    const dir = makeTree({ 'a.md': 'x' });
    t.after(() => removeTree(dir));
    const prints = fingerprintTree(dir, ['a.md']);

    assert.deepEqual(fingerprintDrift(prints, prints), []);
  });
});
