#!/usr/bin/env node
// Version-authorship guard — blocks a commit that hand-edits `<VersionPrefix>` in src/Directory.Build.props
// or un-stamps the CHANGELOG's `## Unreleased` heading. Both edits belong to the RELEASE PIPELINE, and only
// to it. Runs from the pre-commit hook (devtools/hooks/pre-commit); also runnable by hand.
//
//   node devtools/scripts/check-version-bump.mjs      # scan STAGED changes (what pre-commit does)
//   LYNTAI_RELEASE=1 git commit …                     # escape hatch (see below)
//
// WHY (this failure is not hypothetical — it fired in a sibling repo and cost a whole version number):
// release.yml's "Determine version" step reads the CURRENT version from <VersionPrefix> and bumps FROM it
// when no explicit version input is given. So a session that helpfully bumps the version by hand ("ready
// for the next release") silently moves the baseline: the next run bumps again and publishes the version
// AFTER the intended one. Over there a hand-edited 0.1.2 → 0.2.0 published 0.3.0, and 0.2.0 went from
// unreleased to skipped without anyone deciding to skip it. On a post-1.0 repo the same slip lands on a
// MAJOR. The second half of the same failure: the workflow STAMPS `## Unreleased` with the version being
// released, so a commit that stamps (or deletes) that heading by hand leaves nothing to stamp and the
// release ships with the wrong section title.
//
// `doctor`'s version check catches the resulting STATE (VersionPrefix != newest release tag); this catches
// the ACT, at the commit that introduces it. Note what is NOT the property at risk: `doctor`'s README/npm
// consistency checks all stay green through a hand-bump, because a hand-bump keeps everything consistent.
// Consistency was never the risk — AUTHORSHIP was.
//
// Escape hatch: LYNTAI_RELEASE=1 — for the release pipeline itself, and for a human deliberately repairing
// a botched release (in which case you know exactly which version you are writing and why).

import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

if (process.env.LYNTAI_RELEASE === '1') {
  console.log('check-version-bump: skipped (LYNTAI_RELEASE=1 — release pipeline or deliberate repair).');
  process.exit(0);
}

// -U0 so only the changed lines are reported; a missing/unstaged file yields an empty diff, not an error.
const stagedDiff = (file) => {
  try {
    return execFileSync('git', ['diff', '--cached', '-U0', '--', file],
      { cwd: repo, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  } catch {
    return '';
  }
};

const changedLines = (diff, sign) => diff.split('\n')
  .filter((l) => l.startsWith(sign) && !l.startsWith(`${sign}${sign}${sign}`))
  .map((l) => l.slice(1));

const problems = [];

// 1. <VersionPrefix> — a REMOVED VersionPrefix line means an existing value was rewritten. (A newly ADDED
//    file has no removal, so seeding this repo's props file was never blocked by this guard.)
const propsDiff = stagedDiff('src/Directory.Build.props');
const removedVersions = changedLines(propsDiff, '-').filter((l) => l.includes('<VersionPrefix>'));
const addedVersions = changedLines(propsDiff, '+').filter((l) => l.includes('<VersionPrefix>'));
if (removedVersions.length > 0) {
  const versionOf = (line) => (line.match(/<VersionPrefix>([^<]*)<\/VersionPrefix>/) ?? [])[1] ?? '?';
  problems.push(
    `src/Directory.Build.props: <VersionPrefix> ${removedVersions.map(versionOf).join(', ')} → ` +
    `${addedVersions.map(versionOf).join(', ') || '(removed)'}`);
}

// 2. `## Unreleased` — removing that heading IS stamping a release by hand. A commit that removes it and
//    adds it back (reordering the section) is fine.
const changelogDiff = stagedDiff('CHANGELOG.md');
const unreleased = /^## Unreleased\b/;
const removedUnreleased = changedLines(changelogDiff, '-').some((l) => unreleased.test(l));
const addedUnreleased = changedLines(changelogDiff, '+').some((l) => unreleased.test(l));
if (removedUnreleased && !addedUnreleased) {
  problems.push('CHANGELOG.md: the "## Unreleased" heading is being removed or stamped by hand');
}

// 3. …and the same act from the other direction: WRITING a version-stamped heading. Check 2 only sees a
//    stamp when `## Unreleased` was already committed; a session that adds its notes and titles them
//    `## 1.3.0` in one go removes nothing, yet leaves the pipeline nothing to stamp just the same — and if
//    the release turns out to be a different number, the log ships with a heading for a version that was
//    never released. A heading that is re-added after being removed (a whole-file rewrap) is not a stamp.
const versionHeading = /^## \d+\.\d+\.\d+/;
const removedHeadings = changedLines(changelogDiff, '-').filter((l) => versionHeading.test(l)).map((l) => l.trim());
const introducedHeadings = changedLines(changelogDiff, '+')
  .filter((l) => versionHeading.test(l))
  .filter((l) => !removedHeadings.includes(l.trim()));
if (introducedHeadings.length > 0) {
  problems.push(`CHANGELOG.md: version-stamped heading(s) written by hand — ${introducedHeadings.join(' | ').trim()}`);
}

if (problems.length === 0) process.exit(0);

console.error('\n\x1b[31m✖ check-version-bump: blocked — the release pipeline owns these edits:\x1b[0m');
for (const p of problems) console.error(`  ${p}`);
console.error(`
The version is bumped and the CHANGELOG heading stamped BY THE RELEASE WORKFLOW, which bumps from whatever
<VersionPrefix> currently says. Moving it by hand silently shifts that baseline, so the next release
publishes the version AFTER the one you intended — and the version you skipped is gone.

Instead:
  · write release notes under the "## Unreleased" heading and leave the heading alone;
  · cut the release from the Actions tab (Run workflow) — it bumps, stamps, publishes and tags;
  · repairing a botched release, or running the pipeline? LYNTAI_RELEASE=1 git commit …

See .claude/rules/task-lifecycle.md and docs/DECISIONS.md (D25). (Override once with: git commit --no-verify)
`);
process.exit(1);
