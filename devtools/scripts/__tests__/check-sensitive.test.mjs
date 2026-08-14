// check-sensitive — the leak guard. See devtools/scripts/check-sensitive.mjs.
//
// The highest-stakes gate in the repository: a false PASS here is a committed credential or a dev-machine
// path, and `.claude/rules/sensitive-info.md` is explicit that a committed leak is a HISTORY problem, not a
// working-tree one. So what is tested is that each pattern actually FIRES — running the guard proves nothing,
// because "no output" is what both a clean tree and a broken scanner look like.
//
// Every value below is SYNTHESIZED: obviously-fake, assembled from parts, and never a real credential or a
// real machine path. They are built by concatenation so this file itself contains no literal the guard would
// (rightly) flag — the tests exercise the patterns without seeding the thing they ban.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  builtins, checkSensitive, decodeText, loadPatterns, scanFiles, sources,
} from '../check-sensitive.mjs';
import { git, makeRepo, makeTree, recorder, removeTree, writeInto } from './_fixtures.mjs';

const DRIVE = 'C:';
const HOME_LEAK = `${DRIVE}\\Users\\nobody\\notes.txt`;          // Windows user-home shape
const DEV_LEAK = `${DRIVE}\\Development\\Invented\\App\\x.cs`;   // dev-machine project-root shape
const FAKE_TOKEN = 'lyn' + '_sk-' + 'EXAMPLE000000000000';       // never a real key; shape only
const FAKE_SIBLING = 'Nebulon';                                  // an invented name, not a real sibling

const { patterns: builtinsOnly } = loadPatterns('does-not-exist');

/** Scan in-memory content with the given patterns — no git, no disk. */
function scanText(text, patterns = builtinsOnly, name = 'f.md') {
  return scanFiles([name], () => Buffer.from(text, 'utf8'), patterns);
}

describe('check-sensitive — the built-in patterns FIRE', () => {
  it('catches a Windows user-home absolute path', () => {
    const { hits } = scanText(`the file lives at ${HOME_LEAK} on my box\n`);
    assert.equal(hits.length, 1);
    assert.equal(hits[0].why, 'Windows user-home absolute path');
    assert.equal(hits[0].line, 1);
    assert.ok(hits[0].snippet.startsWith(`${DRIVE}\\Users\\`));
  });

  it('catches a dev-machine project-root absolute path', () => {
    const { hits } = scanText(`# notes\n\nsee ${DEV_LEAK}\n`);
    assert.equal(hits.length, 1);
    assert.equal(hits[0].why, 'dev-machine project-root absolute path');
    assert.equal(hits[0].line, 3, 'the reported line is where the leak is');
  });

  it('is case-insensitive, and matches any drive letter', () => {
    const lower = scanText(`d:${'\\'}users${'\\'}someone${'\\'}f.txt\n`);
    assert.equal(lower.hits.length, 1, 'a lower-case drive/segment is the same leak');
  });

  it('does NOT fire on a neutral placeholder or an unrelated absolute path', () => {
    const clean = scanText([
      'Use a repo-relative path such as docs/DECISIONS.md.',
      'Windows system paths are not machine-identifying: ' + DRIVE + '\\Windows\\System32',
      'A placeholder is fine: <repo>/devtools/dev.mjs',
    ].join('\n'));
    assert.deepEqual(clean.hits, [], 'a guard that fires on clean prose teaches people to bypass it');
  });

  it('reports every leak in a file, not just the first', () => {
    const { hits } = scanText(`${HOME_LEAK}\nok\n${DEV_LEAK}\n`);
    assert.equal(hits.length, 2);
    assert.deepEqual(hits.map((h) => h.line), [1, 3]);
  });

  it('ships exactly the two structural built-ins (they are the tracked, publishable half)', () => {
    assert.equal(builtins.length, 2);
    assert.equal(builtinsOnly.length, 2, 'no local file → the built-ins still run');
  });
});

describe('check-sensitive — local/sensitive-patterns.txt', () => {
  const localFile = 'local/sensitive-patterns.txt';

  it('is honoured when present: each non-comment line becomes a live ban pattern', (t) => {
    const dir = makeTree({
      [localFile]: [
        '# private tokens — one JS regex per line',
        '',
        'lyn_sk-[A-Z0-9]{6,}',
        `\\b${FAKE_SIBLING}\\b`,        // the sibling-name rule shape (sensitive-info.md)
      ].join('\n'),
    });
    t.after(() => removeTree(dir));

    const { patterns, localFileMissing, badLines } = loadPatterns(`${dir}/${localFile}`);
    assert.equal(localFileMissing, false);
    assert.deepEqual(badLines, []);
    assert.equal(patterns.length, 4, 'two built-ins + two private patterns (comment and blank skipped)');

    const token = scanText(`key = "${FAKE_TOKEN}"\n`, patterns);
    assert.equal(token.hits.length, 1, 'a private token pattern must fire');
    assert.equal(token.hits[0].why, 'private ban pattern');

    const sibling = scanText(`ported from the ${FAKE_SIBLING} app\n`, patterns);
    assert.equal(sibling.hits.length, 1, 'a private sibling-name pattern must fire');
  });

  it('reports a bad regex instead of throwing, and keeps the rest of the file', (t) => {
    const dir = makeTree({ [localFile]: 'lyn_sk-[A-Z0-9]{6,}\n[unclosed\n' });
    t.after(() => removeTree(dir));

    const { patterns, badLines } = loadPatterns(`${dir}/${localFile}`);
    assert.deepEqual(badLines, ['[unclosed']);
    assert.equal(patterns.length, 3, 'the good pattern still loads — one typo must not disarm the guard');
    assert.equal(scanText(`${FAKE_TOKEN}\n`, patterns).hits.length, 1);
  });

  it('flags its own absence, so "clean" is never mistaken for "fully armed"', () => {
    const { patterns, localFileMissing } = loadPatterns('nope/sensitive-patterns.txt');
    assert.equal(localFileMissing, true);
    assert.equal(patterns.length, 2);
  });
});

describe('check-sensitive — encodings, because a leak must not hide behind one', () => {
  const withBom = (text, be = false) => {
    const body = Buffer.from(text, 'utf16le');
    if (be) for (let i = 0; i + 1 < body.length; i += 2) { const t = body[i]; body[i] = body[i + 1]; body[i + 1] = t; }
    return Buffer.concat([Buffer.from(be ? [0xfe, 0xff] : [0xff, 0xfe]), body]);
  };

  it('decodes UTF-16LE and UTF-16BE (BOM) and still finds the leak', () => {
    for (const be of [false, true]) {
      const buf = withBom(`x\n${HOME_LEAK}\n`, be);
      const { hits } = scanFiles(['u.md'], () => buf, builtinsOnly);
      assert.equal(hits.length, 1, `${be ? 'UTF-16BE' : 'UTF-16LE'} must be decoded, not skipped as binary`);
      assert.equal(hits[0].line, 2);
    }
  });

  it('decodes BOM-less UTF-16LE by its NUL-density heuristic', () => {
    const buf = Buffer.from(`${HOME_LEAK}\n`, 'utf16le');
    assert.ok(decodeText(buf).includes('Users'), 'a no-BOM UTF-16 file is still text');
    assert.equal(scanFiles(['u.md'], () => buf, builtinsOnly).hits.length, 1);
  });

  it('skips genuine binary, and keeps reading UTF-8 with CJK in it', () => {
    // A real PNG header (magic + IHDR length). The NUL-density heuristic deliberately errs toward TEXT —
    // a misread binary yields garbage that is merely scanned, whereas a misread TEXT file would not be
    // scanned at all — so "binary" here means the utf8 decode still holds a NUL.
    const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d]);
    assert.equal(decodeText(png), null);
    const cjk = Buffer.from(`灵台 — the numinous platform\n${HOME_LEAK}\n`, 'utf8');
    assert.equal(scanFiles(['c.md'], () => cjk, builtinsOnly).hits.length, 1);
  });
});

describe('check-sensitive — how a file that cannot be read is classified', () => {
  const throwing = (code) => () => { const e = new Error('boom'); e.code = code; throw e; };

  it('treats ENOENT as a pending deletion — skipped and REPORTED, never a phantom leak', () => {
    const r = scanFiles(['gone.md'], throwing('ENOENT'), builtinsOnly);
    assert.deepEqual(r.hits, []);
    assert.deepEqual(r.unreadable, []);
    assert.deepEqual(r.goneFromWorkingTree, ['gone.md']);
  });

  it('fails CLOSED on any other read error — an unscannable file could be hiding one', () => {
    const r = scanFiles(['locked.md'], throwing('EACCES'), builtinsOnly);
    assert.equal(r.unreadable.length, 1);
    assert.match(r.unreadable[0], /^locked\.md: /);
  });
});

describe('check-sensitive — end to end over a real repository', () => {
  it('a clean tree passes, and says so', (t) => {
    const dir = makeRepo({ 'README.md': 'Use repo-relative paths.\n', 'docs/x.md': 'nothing to see\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);

    const log = recorder(); const err = recorder();
    assert.equal(checkSensitive({ repo: dir, tree: true, log, err }), 0);
    assert.match(log.text(), /clean — no dev-machine paths or private tokens/);
  });

  it('--tree blocks on a leak in a tracked file, naming file and line', (t) => {
    const dir = makeRepo({ 'docs/notes.md': `intro\nbuilt at ${DEV_LEAK}\n` });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);

    const log = recorder(); const err = recorder();
    assert.equal(checkSensitive({ repo: dir, tree: true, log, err }), 1);
    assert.match(err.text(), /docs\/notes\.md:2/);
    assert.match(err.text(), /dev-machine project-root absolute path/);
  });

  it('scans the STAGED blob, not the working tree — the blob is what gets committed', (t) => {
    const dir = makeRepo({ 'docs/notes.md': 'clean\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    git(dir, ['commit', '-qm', 'seed']);

    writeInto(dir, 'docs/notes.md', `leaking ${HOME_LEAK}\n`);
    git(dir, ['add', 'docs/notes.md']);
    writeInto(dir, 'docs/notes.md', 'cleaned up again\n');   // working tree now innocent, index is not

    const log = recorder(); const err = recorder();
    assert.equal(checkSensitive({ repo: dir, tree: false, log, err }), 1,
      'the guard must read the index; a tidy working tree is not a clean commit');
    assert.match(err.text(), /docs\/notes\.md:1/);
  });

  it('sees a leak in a file whose NAME is non-ASCII (git C-quotes such paths without -z)', (t) => {
    // Measured 2026-08-11 while writing these tests. Without `-z`, `git ls-files` emits
    // `"docs/\347\201\265\345\217\260.md"`, the on-disk read ENOENTs, and the ENOENT branch classifies it as
    // a PENDING DELETION — so the file is silently skipped and its leak ships. A false PASS in the guard
    // whose false passes are unrecoverable, in a repository whose own name is CJK.
    const dir = makeRepo({ 'docs/灵台.md': `see ${HOME_LEAK}\n` });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);

    assert.deepEqual(sources(dir, { tree: true }).files, ['docs/灵台.md'], 'the path must arrive raw');

    const log = recorder(); const err = recorder();
    assert.equal(checkSensitive({ repo: dir, tree: true, log, err }), 1);
    assert.match(err.text(), /docs\/灵台\.md:1/);
    assert.doesNotMatch(log.text(), /deleted in the working tree/, 'and it is not mistaken for a deletion');
  });

  it('does not block on a tracked file deleted from the working tree — it reports the skip', (t) => {
    const dir = makeRepo({ 'docs/a.md': 'fine\n', 'docs/b.md': 'fine\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    git(dir, ['commit', '-qm', 'seed']);
    removeTree(`${dir}/docs/b.md`);   // deletion not staged — mid-refactor

    const log = recorder(); const err = recorder();
    assert.equal(checkSensitive({ repo: dir, tree: true, log, err }), 0);
    assert.match(log.text(), /1 tracked file\(s\) deleted in the working tree/);
  });
});
