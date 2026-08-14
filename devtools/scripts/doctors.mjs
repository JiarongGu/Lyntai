// doctors — the three consistency checks behind `dev.mjs doctor` and `dev.mjs changelog`.
//
// Unlike its neighbours this file has NO CLI entry point of its own: `dev.mjs` is its command line
// (`doctor [--fix]`, `changelog [--fix] [--version X.Y.Z] [--date YYYY-MM-DD]`), and `pack` calls the first
// of them directly. It was extracted from dev.mjs 2026-08-11 (TASKS.md Part 62) for one reason — a function
// living inside a `switch` in the dispatcher cannot be driven by a test, and these three write to
// README.md, judge the release version, and rewrite CHANGELOG.md headings. Nothing about what they check
// changed in the move.
//
// Every file access is behind a `read`/`write` seam so a test never touches the real README or CHANGELOG.
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { toSemver } from '../project.config.mjs';

/** The `**vX.Y.Z` at the start of the README's `## Status` headline (flagged there with an HTML comment). */
export const statusVersionOf = (readme) =>
  ((readme.split(/^## /m).find((s) => s.startsWith('Status')) ?? '').match(/\*\*v(\d+\.\d+\.\d+)/) ?? [])[1] ?? null;

/**
 * pack-doctor: keep the README `## Status` headline version in lock-step with VersionPrefix (the single
 * source in src/Directory.Build.props), so a shipped nupkg's README never advertises a stale version.
 *
 * The release pipeline BUMPS the version, so `pack` (and `doctor --fix`) SYNC the header to match — no
 * manual README edit. `doctor` with no flag just CHECKS and fails on drift.
 */
export function packDoctor({
  repo = process.cwd(),
  version,
  fix = false,
  log = console.log,
  error = console.error,
  read = null,
  write = null,
} = {}) {
  const file = join(repo, 'README.md');
  const readme = (read ?? (() => readFileSync(file, 'utf8')))();
  const found = statusVersionOf(readme);
  if (found === version) {
    log(`pack-doctor: README Status matches VersionPrefix (${version}) ✓`);
    return true;
  }
  if (fix) {
    // rewrite ONLY the first `**vX.Y.Z` inside the `## Status` section
    const at = readme.search(/^## Status/m);
    (write ?? ((t) => writeFileSync(file, t)))(
      readme.slice(0, at) + readme.slice(at).replace(/\*\*v\d+\.\d+\.\d+/, `**v${version}`));
    log(`pack-doctor: synced README "## Status" version ${found ? 'v' + found : '(none)'} → `
      + `v${version} (from VersionPrefix)`);
    return true;
  }
  error(`pack-doctor: README "## Status" version (${found ?? 'none found'}) != VersionPrefix `
    + `(${version}) — run \`node devtools/dev.mjs doctor --fix\` (or \`pack\`, which auto-syncs it).`);
  return false;
}

/** The newest `v*` tag by version order, or null when the checkout has none (a shallow CI clone, a fork). */
export const newestReleaseTag = (repo, spawn = spawnSync) => {
  const r = spawn('git', ['tag', '--list', 'v*', '--sort=-v:refname'], { cwd: repo, encoding: 'utf8', shell: false });
  return (r.stdout ?? '').split('\n').map((s) => s.trim()).filter(Boolean)[0] ?? null;
};

/**
 * version-doctor: VersionPrefix must equal the LAST RELEASED tag.
 *
 * The release workflow bumps VersionPrefix as PART of releasing, so between releases the two are equal by
 * construction — any other value means the version was authored by hand, and the next release will bump FROM
 * that hand-written baseline and publish the version after the intended one (a sibling repo lost 0.2.0
 * exactly this way: a manual 0.1.2 → 0.2.0 became a published 0.3.0). This is the STATE check, so it also
 * catches a bad merge or rebase that moved the version; check-version-bump.mjs catches the ACT at commit time.
 *
 * Deliberately NOT part of `verify` or `pack`: the release workflow writes the NEW version before running
 * both, so during a legitimate release VersionPrefix is *supposed* to be ahead of the newest tag. Silent
 * when there are no tags and when LYNTAI_RELEASE=1.
 */
export function versionDoctor({
  repo = process.cwd(),
  version,
  env = process.env,
  log = console.log,
  error = console.error,
  tag = null,
} = {}) {
  if (env.LYNTAI_RELEASE === '1') {
    log('version-doctor: skipped (LYNTAI_RELEASE=1 — release pipeline or deliberate repair)');
    return true;
  }
  const newest = (tag ?? (() => newestReleaseTag(repo)))();
  if (!newest) {
    log('version-doctor: no v* release tags to compare against — skipped ✓');
    return true;
  }
  const tagged = toSemver(newest.replace(/^v/, ''));
  if (tagged === version) {
    log(`version-doctor: VersionPrefix matches the newest release tag (${newest}) ✓`);
    return true;
  }
  error(`version-doctor: VersionPrefix (${version}) != newest release tag (${newest}) — the `
    + 'version looks HAND-EDITED.\n  Between releases they are equal by construction (the release workflow '
    + 'bumps VersionPrefix as part of releasing).\n  A moved baseline makes the next release publish the '
    + `version AFTER the intended one — restore <VersionPrefix> to ${tagged} in src/Directory.Build.props `
    + 'and let the workflow bump it.\n  Mid-release, or repairing one on purpose? LYNTAI_RELEASE=1 '
    + 'node devtools/dev.mjs doctor');
  return false;
}

/** `## Unreleased`, optionally with a title after a dash — the heading the release pipeline stamps. */
export const unreleasedHeading = /^## Unreleased[ \t]*(?:[—–-][ \t]*(.+?))?[ \t]*$/m;

/**
 * changelog-doctor: stamp the CHANGELOG's `## Unreleased` heading with the version being released, the way
 * the release pipeline already stamps VersionPrefix + the README `## Status` headline. Cutting a release is
 * otherwise the ONE place a human had to remember a manual edit — and v1.2.0 shipped with its section still
 * titled "Unreleased" because of it.
 *
 * Two heading shapes are produced, matching what the file already uses:
 *   `## Unreleased`                → `## X.Y.Z — 2026-07-30`
 *   `## Unreleased — <title>`      → `## X.Y.Z — <title> (2026-07-30)`
 * so an author who wants a titled release writes the title on the Unreleased heading in advance; nothing is
 * ever invented here. IDEMPOTENT: a heading for the version already present means the release was already
 * stamped (a pipeline re-run), and the file is left untouched.
 */
export function changelogDoctor({
  repo = process.cwd(),
  version,
  date,
  fix = false,
  log = console.log,
  warn = console.warn,
  error = console.error,
  read = null,
  write = null,
} = {}) {
  const file = join(repo, 'CHANGELOG.md');
  const changelog = (read ?? (() => readFileSync(file, 'utf8')))();
  const stamped = new RegExp(`^## ${version.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?:[ \t]|$)`, 'm');

  if (stamped.test(changelog)) {
    log(`changelog-doctor: CHANGELOG already has a "## ${version}" heading ✓`);
    return true;
  }

  const match = changelog.match(unreleasedHeading);
  if (!match) {
    // Neither a section for this version nor an Unreleased one to promote: nothing DOCUMENTS what is being
    // shipped. Report it, but never fail a release over a doc heading — the packages are the deliverable.
    warn(`changelog-doctor: no "## ${version}" and no "## Unreleased" heading in CHANGELOG.md — `
      + 'nothing to stamp (add the section by hand).');
    return true;
  }
  if (!fix) {
    error(`changelog-doctor: CHANGELOG "## Unreleased" is not stamped for ${version} — `
      + 'run `node devtools/dev.mjs changelog --fix` (the release workflow does this for you).');
    return false;
  }

  const title = match[1]?.trim();
  const on = date ?? new Date().toISOString().slice(0, 10); // UTC — the release runs on a UTC runner
  const heading = title ? `## ${version} — ${title} (${on})` : `## ${version} — ${on}`;
  (write ?? ((t) => writeFileSync(file, t)))(changelog.replace(unreleasedHeading, heading));
  log(`changelog-doctor: stamped "${match[0]}" → "${heading}"`);
  return true;
}
