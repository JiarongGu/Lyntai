// doctors — pack / version / changelog. See devtools/scripts/doctors.mjs.
//
// These three were inline in dev.mjs until 2026-08-11, which is why they had no tests: a function inside a
// `switch` cannot be driven by anything. Between them they REWRITE README.md, judge whether the version was
// hand-authored, and rewrite a CHANGELOG heading — and two of the three write files during a RELEASE, which
// is the worst moment to find out one of them is wrong.
//
// The version-doctor is the same property `check-version-bump` guards from the other side: that one catches
// the ACT at commit time, this one catches the resulting STATE. docs/DECISIONS.md D19 is what both are for.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  changelogDoctor, claudeDoctor, newestReleaseTag, packDoctor, releaseDateOf, releasedClaimOf,
  statusVersionOf, unreleasedHeading, versionDoctor,
} from '../doctors.mjs';
import { recorder } from './_fixtures.mjs';

/** Every doctor is driven with both file seams stubbed — the real README and CHANGELOG are never touched. */
const drive = (doctor, text, opts = {}) => {
  const log = recorder();
  let written = null;
  const ok = doctor({ log, error: log, warn: log, read: () => text, write: (t) => { written = t; }, ...opts });
  return { ok, out: log.text(), written };
};

const README = [
  '# Lyntai',
  '',
  'Badge line with **v0.0.1** in it, which is not the Status headline.',
  '',
  '## Status',
  '',
  '<!-- the version below is synced by `node devtools/dev.mjs doctor --fix` -->',
  '**v2.5.0** — released.',
  '',
  '## Install',
  '',
  'Another **v2.5.0** further down.',
  '',
].join('\n');

describe('pack-doctor — the README headline version', () => {
  it('reads the version out of the `## Status` section and nowhere else', () => {
    assert.equal(statusVersionOf(README), '2.5.0');
    assert.equal(statusVersionOf('# X\n\n**v9.9.9** everywhere but a Status section\n'), null);
  });

  it('passes when it matches VersionPrefix', () => {
    const { ok, out, written } = drive(packDoctor, README, { version: '2.5.0' });
    assert.equal(ok, true);
    assert.equal(written, null);
    assert.match(out, /README Status matches VersionPrefix \(2\.5\.0\) ✓/);
  });

  it('FAILS on drift without --fix, naming both versions, and writes nothing', () => {
    const { ok, out, written } = drive(packDoctor, README, { version: '2.6.0' });
    assert.equal(ok, false);
    assert.equal(written, null, 'a plain `doctor` is a check, never a write');
    assert.match(out, /\(2\.5\.0\) != VersionPrefix \(2\.6\.0\)/);
    assert.match(out, /doctor --fix/);
  });

  it('--fix rewrites ONLY the first `**vX.Y.Z` inside `## Status`', () => {
    // The load-bearing part: a badge above the section and a mention below it must both survive. A global
    // replace would rewrite a version that documents something else entirely.
    const { ok, written } = drive(packDoctor, README, { version: '2.6.0', fix: true });
    assert.equal(ok, true);
    assert.match(written, /Badge line with \*\*v0\.0\.1\*\*/, 'the badge above the section is untouched');
    assert.match(written, /## Status[\s\S]*\*\*v2\.6\.0\*\* — released\./);
    assert.match(written, /Another \*\*v2\.5\.0\*\* further down/, 'and so is the mention below it');
  });

  it('KNOWN LIMIT: --fix on a README with no `## Status` section reports a sync that did not happen', () => {
    // Pinned as it BEHAVES: `search` returns -1, the slice pair reassembles the file unchanged, and the log
    // claims a sync. Harmless (the file is rewritten identically) and unreachable while the README has the
    // section, but recorded rather than discovered later. Found 2026-08-11 writing this suite.
    const bare = '# Lyntai\n\nNo status section here.\n';
    const { ok, out, written } = drive(packDoctor, bare, { version: '2.6.0', fix: true });
    assert.equal(ok, true);
    assert.equal(written, bare, 'nothing actually changed');
    assert.match(out, /synced README "## Status" version \(none\) → v2\.6\.0/);
  });
});

describe('claude-doctor — the released version CLAUDE.md announces', () => {
  // Measured 2026-08-30: CLAUDE.md said "Released: v3.0.0 (2026-08-17)" while VersionPrefix, the newest tag
  // and README's `## Status` all read 3.1.0 (2026-08-23) — for a whole release. `doctor` checks those three
  // against each other and CLAUDE.md is in none of them, so the ONE copy auto-loaded into every session was
  // the one copy nothing held to the others.
  const CLAUDE = [
    '# CLAUDE.md — Lyntai',
    '',
    '## Current state',
    '',
    '**Released: v3.1.0 (2026-08-23).** Twelve packages; frozen since 1.0.',
    '',
    '**Everything before 3.0 is HISTORY** — a boundary that does NOT track the release above.',
    '',
  ].join('\n');

  const CHANGELOG = [
    '# Changelog',
    '',
    '## Unreleased',
    '',
    '## 3.1.0 — 2026-08-23',
    '',
    '## 3.0.1 — the memory seams two adopters had to work around (2026-08-21)',
    '',
  ].join('\n');

  const run = (claude, opts = {}) => {
    const log = recorder();
    const ok = claudeDoctor({
      log, error: log, read: () => claude, readChangelog: () => CHANGELOG, ...opts,
    });
    return { ok, out: log.text() };
  };

  it('reads the version and date out of the Released claim', () => {
    assert.deepEqual(releasedClaimOf(CLAUDE), { version: '3.1.0', date: '2026-08-23' });
    assert.equal(releasedClaimOf('# X\n\nno claim here\n'), null);
  });

  it('takes the release date from the CHANGELOG heading, in BOTH of its shapes', () => {
    // A titled release puts the date in parentheses at the END; a plain one puts it after the dash. Reading
    // only the first shape would silently skip the date check on every titled release.
    assert.equal(releaseDateOf(CHANGELOG, '3.1.0'), '2026-08-23');
    assert.equal(releaseDateOf(CHANGELOG, '3.0.1'), '2026-08-21');
    assert.equal(releaseDateOf(CHANGELOG, '9.9.9'), null);
  });

  it('passes when the version and the date both agree with the tree', () => {
    const { ok, out } = run(CLAUDE, { version: '3.1.0' });
    assert.equal(ok, true);
    assert.match(out, /CLAUDE\.md announces v3\.1\.0 \(2026-08-23\)/);
  });

  it('FAILS on a stale version, naming both — the measured defect', () => {
    const stale = CLAUDE.replace('v3.1.0 (2026-08-23)', 'v3.0.0 (2026-08-17)');
    const { ok, out } = run(stale, { version: '3.1.0' });
    assert.equal(ok, false);
    assert.match(out, /\(3\.0\.0\) != VersionPrefix \(3\.1\.0\)/);
  });

  it('FAILS on a right version with a WRONG date, which a version-only check would pass', () => {
    // The half a naive fix produces: bump the version, leave the date. It then LOOKS synced, which is worse
    // than the original drift because nothing invites a second look.
    const wrongDate = CLAUDE.replace('(2026-08-23)', '(2026-08-17)');
    const { ok, out } = run(wrongDate, { version: '3.1.0' });
    assert.equal(ok, false);
    assert.match(out, /date \(2026-08-17\) != CHANGELOG's 3\.1\.0 heading \(2026-08-23\)/);
  });

  it('FAILS when the claim is absent, rather than passing vacuously', () => {
    // Fail-closed, the rule every scanner here carries: a check that found nothing to check must never
    // print a tick, or deleting the sentence silently disables the gate.
    const { ok, out } = run('# CLAUDE.md\n\nno released claim at all\n', { version: '3.1.0' });
    assert.equal(ok, false);
    assert.match(out, /no "\*\*Released: vX\.Y\.Z \(YYYY-MM-DD\)\*\*" claim/);
  });

  it('checks the version but SKIPS the date mid-release, when the CHANGELOG is not yet stamped', () => {
    // The release workflow bumps VersionPrefix before stamping `## Unreleased`. Failing on the missing
    // heading would make this doctor red for a window that is entirely normal, and a gate that is red
    // during releases is a gate people learn to skip.
    const next = CLAUDE.replace('v3.1.0 (2026-08-23)', 'v3.2.0 (2026-08-30)');
    const { ok, out } = run(next, { version: '3.2.0' });
    assert.equal(ok, true);
    assert.match(out, /no "## 3\.2\.0" heading in CHANGELOG\.md yet — date not checked/);
  });
});

describe('version-doctor — VersionPrefix must equal the newest release tag', () => {
  const run = (opts) => {
    const log = recorder();
    return { ok: versionDoctor({ log, error: log, env: {}, ...opts }), out: log.text() };
  };

  it('passes when they agree', () => {
    const { ok, out } = run({ version: '2.5.0', tag: () => 'v2.5.0' });
    assert.equal(ok, true);
    assert.match(out, /matches the newest release tag \(v2\.5\.0\) ✓/);
  });

  it('FAILS on a hand-authored version, and says which value to restore', () => {
    const { ok, out } = run({ version: '2.6.0', tag: () => 'v2.5.0' });
    assert.equal(ok, false);
    assert.match(out, /VersionPrefix \(2\.6\.0\) != newest release tag \(v2\.5\.0\)/);
    assert.match(out, /restore <VersionPrefix> to 2\.5\.0/, 'the remediation is the TAG\'s value, not a bump');
    assert.match(out, /publish the version AFTER the intended one/, 'and says what it costs (D19)');
  });

  it('pads a short tag through toSemver, so v2.5 and 2.5.0 are not spurious drift', () => {
    assert.equal(run({ version: '2.5.0', tag: () => 'v2.5' }).ok, true);
  });

  it('is silent when the checkout has no tags — a shallow CI clone or a fork', () => {
    const { ok, out } = run({ version: '2.5.0', tag: () => null });
    assert.equal(ok, true);
    assert.match(out, /no v\* release tags to compare against — skipped ✓/);
  });

  it('yields to LYNTAI_RELEASE=1, where being ahead of the tag is the POINT', () => {
    const log = recorder();
    const ok = versionDoctor({ version: '9.9.9', env: { LYNTAI_RELEASE: '1' }, log, error: log, tag: () => 'v2.5.0' });
    assert.equal(ok, true);
    assert.match(log.text(), /skipped \(LYNTAI_RELEASE=1/);
  });

  it('asks git for v* tags in VERSION order and takes the newest', () => {
    // `--sort=-v:refname` is what makes v10.0.0 newer than v9.0.0; a lexicographic listing would not.
    let seen = null;
    const tag = newestReleaseTag('/repo', (exe, argv, opts) => {
      seen = { exe, argv, opts };
      return { stdout: 'v2.5.0\nv2.4.0\n' };
    });
    assert.equal(tag, 'v2.5.0');
    assert.deepEqual(seen.argv, ['tag', '--list', 'v*', '--sort=-v:refname']);
    assert.equal(seen.opts.cwd, '/repo');
    assert.equal(newestReleaseTag('/repo', () => ({ stdout: '' })), null);
  });
});

describe('changelog-doctor — stamping the Unreleased heading', () => {
  const log = '# Changelog\n\n## Unreleased\n\n### Added\n\n- a thing\n\n## 2.5.0 — 2026-08-08\n\n- old\n';

  it('stamps a plain heading with the version and date', () => {
    const { ok, written } = drive(changelogDoctor, log, { version: '2.6.0', date: '2026-08-11', fix: true });
    assert.equal(ok, true);
    assert.match(written, /^## 2\.6\.0 — 2026-08-11$/m);
    assert.doesNotMatch(written, /^## Unreleased/m);
    assert.match(written, /### Added\n\n- a thing/, 'the section body is untouched');
    assert.match(written, /^## 2\.5\.0 — 2026-08-08$/m, 'and so is every released section below it');
  });

  it('carries a TITLE the author wrote in advance, and never invents one', () => {
    const titled = '# Changelog\n\n## Unreleased — the memory release\n\n- a thing\n';
    const { written } = drive(changelogDoctor, titled, { version: '3.0.0', date: '2026-08-11', fix: true });
    assert.match(written, /^## 3\.0\.0 — the memory release \(2026-08-11\)$/m);
  });

  it('accepts an em dash, an en dash or a hyphen on the Unreleased heading', () => {
    for (const dash of ['—', '–', '-'])
      assert.equal(`## Unreleased ${dash} a title`.match(unreleasedHeading)?.[1], 'a title', `dash ${dash}`);
    assert.equal('## Unreleased'.match(unreleasedHeading)?.[1], undefined);
  });

  it('is IDEMPOTENT — a pipeline re-run finds its own heading and writes nothing', () => {
    const stamped = drive(changelogDoctor, log, { version: '2.6.0', date: '2026-08-11', fix: true }).written;
    const again = drive(changelogDoctor, stamped, { version: '2.6.0', fix: true });
    assert.equal(again.ok, true);
    assert.equal(again.written, null);
    assert.match(again.out, /already has a "## 2\.6\.0" heading ✓/);
  });

  it('matches its own version LITERALLY — the dots are escaped, not wildcards', () => {
    const odd = '# Changelog\n\n## 2X5Y0 — not a version heading\n\n## Unreleased\n\n- a thing\n';
    const { written } = drive(changelogDoctor, odd, { version: '2.5.0', date: '2026-08-11', fix: true });
    assert.match(written, /^## 2\.5\.0 — 2026-08-11$/m, 'an unescaped `.` would have matched 2X5Y0 and stopped');
  });

  it('FAILS without --fix when there is something to stamp (the check form)', () => {
    const { ok, out, written } = drive(changelogDoctor, log, { version: '2.6.0' });
    assert.equal(ok, false);
    assert.equal(written, null);
    assert.match(out, /"## Unreleased" is not stamped for 2\.6\.0/);
  });

  it('WARNS but never fails when there is no heading to stamp — the packages are the deliverable', () => {
    const { ok, out, written } = drive(changelogDoctor, '# Changelog\n\nnothing yet\n',
      { version: '2.6.0', fix: true });
    assert.equal(ok, true, 'a release must not be blocked by a documentation heading');
    assert.equal(written, null);
    assert.match(out, /no "## 2\.6\.0" and no "## Unreleased" heading/);
  });
});
