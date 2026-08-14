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
  changelogDoctor, newestReleaseTag, packDoctor, statusVersionOf, unreleasedHeading, versionDoctor,
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
