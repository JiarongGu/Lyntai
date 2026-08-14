import assert from 'node:assert/strict';
import { test } from 'node:test';

import { MOJIBAKE, SCANNED, checkEncoding } from '../check-encoding.mjs';
import { makeTree, removeTree } from './_fixtures.mjs';

/// A fixture tree, cleaned up whatever happens. No git needed: every fact here passes the file list to
/// `checkEncoding` explicitly, so `trackedFiles` is out of scope.
function withRepo(files, body) {
  const dir = makeTree(files);
  try { return body(dir); } finally { removeTree(dir); }
}

// A guard whose failure mode is a FALSE PASS cannot be validated by running it, so both directions are
// asserted for every fact here. The patterns themselves are re-derived rather than quoted, because writing
// them from memory produced the wrong characters three times — the same mistake the guard exists to catch.

/// Mangle a string exactly the way the accident does: encode as UTF-8, decode as something else.
const mangle = (text, enc) => new TextDecoder(enc).decode(Buffer.from(text, 'utf8'));

test('the registry matches what the corruption actually produces', () => {
  // If these drift apart the guard hunts for sequences nothing generates, and passes forever.
  //
  // CONTAINS rather than EQUALS, deliberately. Decoding a 3-byte UTF-8 character as GBK consumes two bytes
  // and leaves the third dangling, so `mangle('—')` in isolation ends in U+FFFD — but in a real file that
  // third byte pairs with whatever follows and yields something else. Registering the distinctive LEADING
  // character is what makes the pattern general; registering the isolated two-character form would match
  // only text where the em-dash is the last thing in the file.
  const matches = (text) => MOJIBAKE.some((m) => text.includes(m.pattern));

  assert.ok(matches(mangle('—', 'gbk')), 'GBK em-dash must be detected');
  assert.ok(matches(mangle('×', 'gbk')), 'GBK multiplication sign must be detected');
  assert.ok(matches(mangle('§', 'gbk')), 'GBK section sign must be detected');
  assert.ok(matches(mangle('—', 'windows-1252')), 'CP1252 em-dash must be detected');
  assert.ok(matches(mangle('中', 'gbk')), 'GBK CJK must be detected');
});

test('a realistic mangled line is caught — the em-dash mid-sentence, not in isolation', () => {
  // The case the isolated-form test above cannot cover, and the one that actually happened three times.
  const line = mangle('the age reset — measured 2026-08-13 — keeps a fact alive', 'gbk');

  withRepo({ 'src/a.cs': `// ${line}\n` }, (repo) => {
    assert.equal(checkEncoding(repo, () => {}, ['src/a.cs']), 1);
  });
});

test('a mangled file FAILS, and the message names the file and the line', () => {
  withRepo({ 'src/a.cs': `// ${mangle('a — b', 'gbk')}\n` }, (repo) => {
    const lines = [];
    const code = checkEncoding(repo, (m) => lines.push(m), ['src/a.cs']);

    assert.equal(code, 1);
    assert.ok(lines.join('\n').includes('src/a.cs:1'), 'must name file and line');
  });
});

test('a clean file with legitimate non-ASCII PASSES', () => {
  // The false-positive direction, and it matters more than it looks: this repository is named 灵台, its
  // corpus is deliberately multilingual, and its prose is full of em-dashes. A guard that flagged any of
  // that would be turned off within a day.
  withRepo({
    'docs/a.md': 'An em-dash — and 中文 and 日本語 and 한국어 and ไทย.\n',
    'src/b.cs': '// clamp(1 + w × n) per §5.7\n',
  }, (repo) => {
    const lines = [];
    const code = checkEncoding(repo, (m) => lines.push(m), ['docs/a.md', 'src/b.cs']);

    assert.equal(code, 0, lines.join('\n'));
  });
});

test('the replacement character alone is enough to fail', () => {
  // U+FFFD means a decode already gave up and the original byte is GONE — unrecoverable, so it is worth
  // failing on even without a known codepage signature beside it.
  withRepo({ 'src/a.cs': `// broken ${String.fromCodePoint(0xFFFD)} here\n` }, (repo) => {
    assert.equal(checkEncoding(repo, () => {}, ['src/a.cs']), 1);
  });
});

test('an unscanned extension is ignored', () => {
  withRepo({ 'assets/blob.bin': mangle('—', 'gbk') }, (repo) => {
    assert.equal(checkEncoding(repo, () => {}, ['assets/blob.bin']), 0);
  });
});

test('SCANNED covers the file kinds this repository actually authors', () => {
  // A silent gap here is the guard scanning nothing and reporting clean — the shape of defect that made
  // check-sensitive skip every non-ASCII FILENAME until 2026-08-11.
  for (const f of ['a.cs', 'a.md', 'a.mjs', 'a.json', 'a.csproj', 'a.sql', 'a.html', 'a.yml'])
    assert.ok(SCANNED.test(f), `${f} must be scanned`);

  for (const f of ['a.png', 'a.dll', 'a.nupkg'])
    assert.ok(!SCANNED.test(f), `${f} must not be scanned`);
});

test('a missing file is skipped rather than crashing the run', () => {
  withRepo({}, (repo) => {
    assert.equal(checkEncoding(repo, () => {}, ['src/gone.cs']), 0);
  });
});

test('every registered pattern is actually detected', () => {
  // Guards against a registry entry that is unreachable — present, documented, and matching nothing.
  for (const { pattern, why } of MOJIBAKE) {
    withRepo({ 'src/a.cs': `x${pattern}y\n` }, (repo) => {
      assert.equal(checkEncoding(repo, () => {}, ['src/a.cs']), 1, `undetected: ${why}`);
    });
  }
});
