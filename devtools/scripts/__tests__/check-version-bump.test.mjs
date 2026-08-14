// check-version-bump — the version-AUTHORSHIP guard. See devtools/scripts/check-version-bump.mjs.
//
// What is at stake (docs/DECISIONS.md D19): the release workflow bumps FROM whatever `<VersionPrefix>`
// currently says, so a hand-edit silently moves the baseline and the next release publishes the version
// AFTER the intended one — the skipped version is simply gone, and nothing reports it. This guard is the
// only thing that catches the ACT; `doctor` catches the resulting state one release too late.
//
// The rules are driven with diff TEXT (exact, and every branch reachable) plus one end-to-end pass over a
// real staged repository, which is what proves the git plumbing above them actually feeds them.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { StagedDiffFailure, checkVersionBump, stagedDiff, versionBumpProblems } from '../check-version-bump.mjs';
import { git, makeRepo, removeTree, repoRoot, writeInto } from './_fixtures.mjs';

/** A realistic `git diff -U0` body, headers included — the headers must not read as changed lines. */
const diff = (file, ...body) => [
  `diff --git a/${file} b/${file}`,
  'index 1111111..2222222 100644',
  `--- a/${file}`,
  `+++ b/${file}`,
  ...body,
].join('\n');

describe('check-version-bump — <VersionPrefix>', () => {
  it('blocks a hand-edited version, naming the move', () => {
    const problems = versionBumpProblems({
      propsDiff: diff('src/Directory.Build.props',
        '@@ -4 +4 @@', '-    <VersionPrefix>2.5.0</VersionPrefix>', '+    <VersionPrefix>2.6.0</VersionPrefix>'),
    });
    assert.equal(problems.length, 1);
    assert.match(problems[0], /<VersionPrefix> 2\.5\.0 → 2\.6\.0/);
  });

  it('blocks a DELETED version line too', () => {
    const problems = versionBumpProblems({
      propsDiff: diff('src/Directory.Build.props', '@@ -4 +4 @@', '-    <VersionPrefix>2.5.0</VersionPrefix>'),
    });
    assert.equal(problems.length, 1);
    assert.match(problems[0], /→ \(removed\)/);
  });

  it('allows SEEDING the file — a new props file removes nothing', () => {
    const problems = versionBumpProblems({
      propsDiff: [
        'diff --git a/src/Directory.Build.props b/src/Directory.Build.props',
        'new file mode 100644',
        '--- /dev/null',
        '+++ b/src/Directory.Build.props',
        '@@ -0,0 +1,3 @@',
        '+<Project>',
        '+    <VersionPrefix>0.1.0</VersionPrefix>',
        '+</Project>',
      ].join('\n'),
    });
    assert.deepEqual(problems, [], 'the guard is about REWRITING an existing baseline');
  });

  it('ignores an unrelated edit to the same file', () => {
    const problems = versionBumpProblems({
      propsDiff: diff('src/Directory.Build.props',
        '@@ -9 +9 @@', '-    <Nullable>disable</Nullable>', '+    <Nullable>enable</Nullable>'),
    });
    assert.deepEqual(problems, []);
  });
});

describe('check-version-bump — the CHANGELOG heading', () => {
  it('blocks removing "## Unreleased" (that IS stamping a release by hand)', () => {
    const problems = versionBumpProblems({
      changelogDiff: diff('CHANGELOG.md', '@@ -3 +3 @@', '-## Unreleased', '+## 2.6.0 — 2026-08-11'),
    });
    // both halves of the same act: the heading vanished AND a stamped one appeared
    assert.equal(problems.length, 2);
    assert.match(problems.join('\n'), /"## Unreleased" heading is being removed or stamped by hand/);
    assert.match(problems.join('\n'), /version-stamped heading\(s\) written by hand — ## 2\.6\.0/);
  });

  it('blocks a stamped heading written from scratch (nothing removed to notice)', () => {
    const problems = versionBumpProblems({
      changelogDiff: diff('CHANGELOG.md', '@@ -2,0 +3,3 @@', '+## 1.3.0 — new stuff', '+', '+- a change'),
    });
    assert.equal(problems.length, 1);
    assert.match(problems[0], /## 1\.3\.0/);
  });

  it('allows a reorder: "## Unreleased" removed and added back', () => {
    const problems = versionBumpProblems({
      changelogDiff: diff('CHANGELOG.md', '@@ -3,2 +3,2 @@', '-## Unreleased', '-', '+', '+## Unreleased'),
    });
    assert.deepEqual(problems, []);
  });

  it('allows a rewrap: a version heading removed and re-added unchanged', () => {
    const problems = versionBumpProblems({
      changelogDiff: diff('CHANGELOG.md', '@@ -8 +8 @@', '-## 2.5.0 — 2026-08-05', '+## 2.5.0 — 2026-08-05'),
    });
    assert.deepEqual(problems, []);
  });

  it('allows ordinary release notes under the existing heading', () => {
    const problems = versionBumpProblems({
      changelogDiff: diff('CHANGELOG.md', '@@ -4,0 +5,2 @@', '+### Added', '+- a gate for the gates'),
    });
    assert.deepEqual(problems, []);
  });
});

describe('check-version-bump — nothing staged, and the escape hatch', () => {
  it('passes on empty diffs', () => {
    assert.deepEqual(versionBumpProblems({}), []);
  });

  it('is skipped by LYNTAI_RELEASE=1 — the pipeline, or a deliberate repair', (t) => {
    const dir = makeRepo({
      'src/Directory.Build.props': '<Project><VersionPrefix>2.5.0</VersionPrefix></Project>\n',
    });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    git(dir, ['commit', '-qm', 'seed']);
    writeInto(dir, 'src/Directory.Build.props', '<Project><VersionPrefix>9.9.9</VersionPrefix></Project>\n');
    git(dir, ['add', '.']);

    assert.equal(checkVersionBump({ repo: dir, env: {} }).problems.length, 1, 'blocked without the hatch');
    const released = checkVersionBump({ repo: dir, env: { LYNTAI_RELEASE: '1' } });
    assert.equal(released.skipped, true);
    assert.deepEqual(released.problems, []);
  });
});

describe('check-version-bump — end to end over real staged changes', () => {
  it('reads the index and blocks a hand bump plus a hand stamp in one commit', (t) => {
    const dir = makeRepo({
      'src/Directory.Build.props': '<Project>\n  <VersionPrefix>2.5.0</VersionPrefix>\n</Project>\n',
      'CHANGELOG.md': '# Changelog\n\n## Unreleased\n\n- something\n',
    });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    git(dir, ['commit', '-qm', 'seed']);

    writeInto(dir, 'src/Directory.Build.props', '<Project>\n  <VersionPrefix>2.6.0</VersionPrefix>\n</Project>\n');
    writeInto(dir, 'CHANGELOG.md', '# Changelog\n\n## 2.6.0 — 2026-08-11\n\n- something\n');
    git(dir, ['add', '.']);

    const { skipped, problems } = checkVersionBump({ repo: dir, env: {} });
    assert.equal(skipped, false);
    assert.equal(problems.length, 3, 'the version move, the lost heading, and the hand-written stamp');
    assert.match(problems.join('\n'), /2\.5\.0 → 2\.6\.0/);
  });

  it('passes a repository with NOTHING staged — the benign empty diff this guard was written for', (t) => {
    const dir = makeRepo({
      'src/Directory.Build.props': '<Project>\n  <VersionPrefix>2.5.0</VersionPrefix>\n</Project>\n',
      'CHANGELOG.md': '# Changelog\n\n## Unreleased\n\n- something\n',
    });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    git(dir, ['commit', '-qm', 'seed']);

    const { problems, gitError } = checkVersionBump({ repo: dir, env: {} });
    assert.deepEqual(problems, [], 'an empty diff is not a problem — it is the normal case');
    assert.equal(gitError, null, 'and it is NOT a git failure either');
  });

  it('passes a repository with NO COMMITS at all (git answers against the empty tree)', (t) => {
    // The other benign shape worth pinning before tightening the catch: a fresh repo has no HEAD, and a
    // guard that read "no HEAD" as "git is broken" would block the very first commit of any clone.
    const dir = makeRepo({ 'CHANGELOG.md': '# Changelog\n\n## Unreleased\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);

    const { problems, gitError } = checkVersionBump({ repo: dir, env: {} });
    assert.deepEqual(problems, []);
    assert.equal(gitError, null);
  });

  it('passes a commit that only adds notes under the untouched heading', (t) => {
    const dir = makeRepo({
      'src/Directory.Build.props': '<Project>\n  <VersionPrefix>2.5.0</VersionPrefix>\n</Project>\n',
      'CHANGELOG.md': '# Changelog\n\n## Unreleased\n\n- something\n',
    });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    git(dir, ['commit', '-qm', 'seed']);

    writeInto(dir, 'CHANGELOG.md', '# Changelog\n\n## Unreleased\n\n- something\n- and another thing\n');
    git(dir, ['add', '.']);

    assert.deepEqual(checkVersionBump({ repo: dir, env: {} }).problems, []);
  });
});

describe('check-version-bump — when GIT ITSELF fails, the guard fails CLOSED', () => {
  // The defect this pins (TASKS.md Part 62, fixed 2026-08-11): `stagedDiff` caught EVERY exception and
  // returned `''`, which the rules above read as "nothing staged" — so an unreadable index, a missing `git`
  // or a cwd with no repository anywhere above it all produced "no problems" and the guard passed. That is
  // the wrong direction for the ONE check standing between a hand-authored version and D19's lost release.
  //
  // The distinction is real and narrow: `git diff --cached -- <file>` exits 0 with empty output when the
  // file has no staged change, when the file does not exist, and even when the repository has no HEAD (the
  // two tests above pin all three). Reaching the catch at all therefore means git could not answer.

  /** Break the index the way a real one breaks — truncated/garbage bytes; git exits 128 rather than 0. */
  const corruptIndex = (dir) => writeInto(dir, '.git/index', 'not an index');

  it('reports a corrupt index as a PROBLEM, not as a clean tree', (t) => {
    const dir = makeRepo({ 'CHANGELOG.md': '# Changelog\n\n## Unreleased\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    corruptIndex(dir);

    const { skipped, problems, gitError } = checkVersionBump({ repo: dir, env: {} });
    assert.equal(skipped, false);
    assert.equal(problems.length, 1, 'a guard that cannot look must not report "nothing to see"');
    assert.ok(gitError instanceof StagedDiffFailure, 'and it must be reported AS a git failure');
    assert.match(problems[0], /git could not read the staged diff/);
    assert.match(problems[0], /src\/Directory\.Build\.props/, 'names the file it failed to read');
    assert.match(problems[0], /exit(ed)? 128/, 'and carries git\'s own exit status');
    // The remediation for a broken git is not "stop hand-editing the version" — the report must not read
    // as a version edit, or the reader is sent to fix a file that is fine.
    assert.doesNotMatch(problems[0], /VersionPrefix \S+ →/);
  });

  it('reports a MISSING git the same way (the spawn-failure branch, not the exit-code one)', () => {
    const enoent = Object.assign(new Error('spawn git ENOENT'), { code: 'ENOENT', syscall: 'spawnSync git' });
    const exec = () => { throw enoent; };
    const { problems, gitError } = checkVersionBump({ repo: '.', env: {}, exec });
    assert.equal(problems.length, 1);
    assert.ok(gitError instanceof StagedDiffFailure);
    assert.match(problems[0], /git is not on PATH|ENOENT/);
  });

  it('stops at the FIRST unreadable diff — one problem, not one per file', () => {
    const exec = () => { throw Object.assign(new Error('boom'), { status: 128, stderr: 'fatal: whatever\n' }); };
    assert.equal(checkVersionBump({ repo: '.', env: {}, exec }).problems.length, 1);
  });

  it('surfaces git\'s own stderr, so the reader can tell WHICH failure this is', () => {
    const exec = () => {
      throw Object.assign(new Error('boom'), { status: 128, stderr: 'fatal: not a git repository\nhint: x\n' });
    };
    assert.match(checkVersionBump({ repo: '.', env: {}, exec }).problems[0], /fatal: not a git repository/);
  });

  it('still yields to LYNTAI_RELEASE=1 — the hatch is checked before git is ever asked', () => {
    const exec = () => { throw new Error('git should not have been called'); };
    const { skipped, problems } = checkVersionBump({ repo: '.', env: { LYNTAI_RELEASE: '1' }, exec });
    assert.equal(skipped, true);
    assert.deepEqual(problems, []);
  });

  it('the CLI says WHICH failure it is and exits non-zero (the branch a human meets at commit time)', () => {
    // Driven with a bogus GIT_DIR, which is the cheapest real "git cannot answer here" this repo can stage
    // without breaking its own index. The assertion is on OUR line, not git's — the point is that the guard
    // blocks and says nothing about the version having been edited.
    const r = spawnSync(process.execPath, [join(repoRoot, 'devtools', 'scripts', 'check-version-bump.mjs')],
      { cwd: repoRoot, encoding: 'utf8', env: { ...process.env, GIT_DIR: 'devtools/_no-such-git-dir' } });
    const out = `${r.stdout ?? ''}${r.stderr ?? ''}`;
    assert.equal(r.status, 1, 'a guard that could not look must not exit 0');
    assert.match(out, /check-version-bump: blocked — the guard could not read the staged changes/);
    assert.match(out, /NOT a report that the version was hand-edited/);
  });

  it('stagedDiff throws StagedDiffFailure rather than returning "" — the seam itself, not just the gate', (t) => {
    const dir = makeRepo({ 'CHANGELOG.md': '# Changelog\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);
    corruptIndex(dir);

    assert.throws(() => stagedDiff(dir, 'CHANGELOG.md'), StagedDiffFailure);
    // …and the benign twin, through the same seam: an untracked, unstaged, ABSENT file is still just empty.
    const clean = makeRepo({ 'CHANGELOG.md': '# Changelog\n' });
    t.after(() => removeTree(clean));
    assert.equal(stagedDiff(clean, 'src/Directory.Build.props'), '');
  });
});
