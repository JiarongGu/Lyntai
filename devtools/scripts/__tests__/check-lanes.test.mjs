import assert from 'node:assert/strict';
import { test } from 'node:test';

import { checkLanes, expandBraces, globToRegExp } from '../check-lanes.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const lane = (id, paths, extra = {}) => ({ id, title: id.toUpperCase(), summary: `the ${id} lane`, paths, ...extra });

/** Run the gate over a fixture map (an object, or raw text) and an explicit file list. */
function run(map, files) {
  const dir = makeTree({ 'daoris.lanes.json': typeof map === 'string' ? map : JSON.stringify(map) });
  try {
    const log = recorder();
    return { code: checkLanes(dir, log, files), out: log.text() };
  } finally { removeTree(dir); }
}

const ok = (r) => assert.equal(r.code, 0, r.out);
const fails = (r, re) => { assert.equal(r.code, 1, r.out); assert.match(r.out, re); };

// ---- glob semantics ------------------------------------------------------------------------------------

test('`*` stays inside one segment', () => {
  const re = globToRegExp('src/*.cs');
  assert.ok(re.test('src/a.cs'));
  assert.ok(!re.test('src/x/a.cs'));
});

test('`**` crosses segments: `a/**` is everything under a, and not a sibling sharing its prefix', () => {
  const re = globToRegExp('src/a/**');
  assert.ok(re.test('src/a/b.cs'));
  assert.ok(re.test('src/a/x/y/b.cs'));
  assert.ok(!re.test('src/ab/b.cs'));
  assert.ok(globToRegExp('**/x.md').test('x.md'), 'a leading `**/` includes depth zero');
  assert.ok(globToRegExp('**/x.md').test('a/b/x.md'));
});

test('`{a,b}` is either branch, inside a segment and around one', () => {
  const re = globToRegExp('src/{Jobs,Guards}/**');
  assert.ok(re.test('src/Jobs/a.cs'));
  assert.ok(re.test('src/Guards/a.cs'));
  assert.ok(!re.test('src/Agents/a.cs'));
  assert.deepEqual(expandBraces('a/{b,c}/{d,e}'), ['a/b/d', 'a/b/e', 'a/c/d', 'a/c/e']);
});

test('regex metacharacters in a glob are literal', () => {
  assert.ok(globToRegExp('Lyntai.slnx').test('Lyntai.slnx'));
  assert.ok(!globToRegExp('Lyntai.slnx').test('LyntaiXslnx'));
});

// ---- a clean map ---------------------------------------------------------------------------------------

test('a map that places every file PASSES and reports each lane\'s count plus laneless', () => {
  const r = run({
    lanes: [lane('core', ['src/**']), lane('tools', ['devtools/**'])],
    laneless: [{ what: 'docs', paths: ['docs/**'] }],
  }, ['src/a.cs', 'src/b/c.cs', 'devtools/x.mjs', 'docs/a.md']);
  ok(r);
  assert.match(r.out, /2 lanes over 4 files/);
  assert.match(r.out, /core 2 · tools 1 · laneless 1/);
});

test('the STEWARD owns a contested path even when it is listed after the lane that also matches', () => {
  const r = run({
    lanes: [lane('docs', ['docs/**']), lane('records', ['docs/DECISIONS.md'], { steward: true })],
  }, ['docs/a.md', 'docs/DECISIONS.md']);
  ok(r);
  assert.match(r.out, /records 1 · docs 1/);
});

test('between two non-steward lanes the FIRST in file order owns the path', () => {
  const r = run({ lanes: [lane('one', ['src/**']), lane('two', ['src/**'])] }, ['src/a.cs']);
  ok(r);
  assert.match(r.out, /one 1 · two 0/);
});

test('`!` carves a path out of the globs before it, so the carved file can live elsewhere', () => {
  const r = run({
    lanes: [lane('records', ['docs/DECISIONS.md'], { steward: true })],
    laneless: [{ what: 'docs', paths: ['docs/**', '!docs/DECISIONS.md'] }],
  }, ['docs/a.md', 'docs/DECISIONS.md']);
  ok(r);
  assert.match(r.out, /records 1 · laneless 1/);
});

test('a `!` carves only from the globs BEFORE it — a later glob adds the file back', () => {
  const r = run({
    lanes: [lane('a', ['docs/x.md'])],
    laneless: [{ what: 'docs', paths: ['docs/**', '!docs/x.md', 'docs/x.md'] }],
  }, ['docs/x.md']);
  fails(r, /docs\/x\.md — owned by lane a and listed in laneless\[0\]/);
});

// ---- the refusals over the tree ------------------------------------------------------------------------

test('a file in no lane and no laneless group FAILS, named', () => {
  fails(run({ lanes: [lane('core', ['src/**'])] }, ['src/a.cs', 'stray.txt']), /in no lane[\s\S]*stray\.txt/);
});

test('a file a lane owns that is ALSO listed laneless FAILS', () => {
  fails(run({
    lanes: [lane('core', ['src/**'])],
    laneless: [{ what: 'shared', paths: ['src/Shared.props'] }],
  }, ['src/a.cs', 'src/Shared.props']), /src\/Shared\.props — owned by lane core and listed in laneless\[0\]/);
});

test('a positive glob that matches no file FAILS as stale — in a lane and in a laneless group', () => {
  fails(run({ lanes: [lane('core', ['src/**', 'scr/typo/**'])] }, ['src/a.cs']), /lane core: "scr\/typo\/\*\*" matches no file/);
  fails(run({
    lanes: [lane('core', ['src/**'])],
    laneless: [{ what: 'docs', paths: ['docs/**'] }],
  }, ['src/a.cs']), /laneless\[0\]: "docs\/\*\*" matches no file/);
});

test('a misspelt BRANCH of an alternation FAILS even when its sibling matches', () => {
  fails(run({ lanes: [lane('core', ['src/{Jobs,Gaurds}/**'])] }, ['src/Jobs/a.cs']),
    /its branch "src\/Gaurds\/\*\*"/);
});

test('an empty file list FAILS closed rather than printing a tick', () => {
  fails(run({ lanes: [lane('core', ['src/**'])] }, []), /found no files/);
});

// ---- the refusals over the map itself ------------------------------------------------------------------

test('invalid JSON FAILS, and a missing map FAILS', () => {
  fails(run('{ "lanes": [', ['a']), /could not be read as JSON/);
  const dir = makeTree({});
  try {
    const log = recorder();
    assert.equal(checkLanes(dir, log, ['a']), 1);
    assert.match(log.text(), /could not be read/);
  } finally { removeTree(dir); }
});

test('a missing, malformed or repeated id FAILS, each named', () => {
  const r = run({
    lanes: [
      { title: 'T', summary: 'S', paths: ['a/**'] },
      lane('Bad_Id', ['a/**']),
      lane('9lane', ['a/**']),
      lane('dup', ['a/**']),
      lane('dup', ['a/**']),
    ],
  }, ['a/x']);
  fails(r, /lanes\[0\]: missing id/);
  assert.match(r.out, /id "Bad_Id" is not lower-case/);
  assert.match(r.out, /id "9lane" is not lower-case/);
  assert.match(r.out, /lanes\[4\] \(dup\): id "dup" is repeated/);
});

test('a missing title or summary FAILS', () => {
  const r = run({ lanes: [{ id: 'a', paths: ['a/**'] }, { id: 'b', title: ' ', summary: 'S', paths: ['a/**'] }] }, ['a/x']);
  fails(r, /lanes\[0\] \(a\): missing title/);
  assert.match(r.out, /lanes\[0\] \(a\): missing summary/);
  assert.match(r.out, /lanes\[1\] \(b\): missing title/);
});

test('a lane with no paths FAILS — absent, empty, or only carve-outs', () => {
  const r = run({
    lanes: [lane('a', undefined), lane('b', []), lane('c', ['!x/**'])],
  }, ['x/y']);
  fails(r, /lanes\[0\] \(a\): no paths/);
  assert.match(r.out, /lanes\[1\] \(b\): no paths/);
  assert.match(r.out, /lanes\[2\] \(c\): only carve-outs/);
});

test('more than one steward FAILS, naming both', () => {
  fails(run({
    lanes: [lane('a', ['a/**'], { steward: true }), lane('b', ['b/**'], { steward: true })],
  }, ['a/x', 'b/x']), /more than one steward lane: a, b/);
});

test('a `gates` field FAILS — this repository declares none', () => {
  fails(run({ lanes: [lane('a', ['a/**'], { gates: ['test'] })] }, ['a/x']), /lanes\[0\] \(a\): has `gates`/);
});

test('a laneless group without its reason or its paths FAILS', () => {
  const r = run({ lanes: [lane('a', ['a/**'])], laneless: [{ paths: ['b/**'] }, { what: 'w' }] }, ['a/x', 'b/x']);
  fails(r, /laneless\[0\]: missing what/);
  assert.match(r.out, /laneless\[1\]: no paths/);
});

test('an unclosed brace FAILS as malformed rather than throwing', () => {
  fails(run({ lanes: [lane('a', ['a/{b,c/**'])] }, ['a/b']), /unclosed "\{"/);
});
