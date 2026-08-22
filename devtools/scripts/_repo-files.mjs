import { execFileSync } from 'node:child_process';

/**
 * The ONE file list every full-tree gate scans: tracked files PLUS new files that are not ignored.
 *
 * The leading underscore keeps this out of the gate roster the way `_fixtures.mjs` keeps its helper out of
 * test discovery — `devtools/scripts/` is a list of executable gates, and this is not one.
 *
 * ## Why untracked files are included
 *
 * `git ls-files` lists the INDEX. The ordinary workflow is *write → `verify` → commit*, so a file that is
 * neither committed nor `git add`ed is absent from every gate's scope — meaning `verify` scans everything
 * EXCEPT the work being verified, and prints the same green line it prints when there is genuinely nothing
 * wrong. Measured 2026-08-23: a 36-line comment block in a new bench file passed two full `verify` runs and
 * the individual gate twice, then failed the moment its file was committed, byte-identical. The first
 * hypothesis was a non-deterministic gate, which would have been far worse than the truth.
 *
 * `--others --exclude-standard` adds exactly the new SOURCE and none of the scratch: `local/`,
 * `devtools/_*`, `bin` and `obj` are ignored and stay out on their own. That is what made index-only look
 * sufficient — the two reasons pull in opposite directions and git already tells them apart.
 *
 * ## Why it is shared
 *
 * Five gates carried their own copy of this rule and the blind spot was five-fold, because a scope rule
 * diverges silently: every copy prints the same green line whatever it scanned. Same shape as the `salience`
 * coercion `pitfalls.md` records, applied to gate SCOPE.
 *
 * @param {string} repo Repository root.
 * @param {string[]} tiers Optional pathspecs to narrow to (e.g. `['src', 'tests']`); empty = whole repo.
 */
export const repoFiles = (repo, tiers = []) => {
  // -z on both calls: git C-QUOTES any path with a non-ASCII byte, the read then fails, and a
  // silently-skipped file reports exactly like a clean one. This repository is named 灵台, so a CJK-named
  // document is not hypothetical.
  const list = (extra) =>
    execFileSync('git', ['ls-files', '-z', ...extra, ...tiers], { cwd: repo, encoding: 'utf8' })
      .split('\0')
      .filter(Boolean);

  return [...new Set([...list([]), ...list(['--others', '--exclude-standard'])])];
};
