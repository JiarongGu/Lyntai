import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

import { repoFiles } from './_repo-files.mjs';

/**
 * `verify`'s own integrity check: did the tree CHANGE while the gates were running?
 *
 * The leading underscore keeps this out of the gate roster, the way `_repo-files.mjs` and `_fixtures.mjs`
 * stay out of theirs — `devtools/scripts/` is a list of executable gates and this is the wrapper around them.
 *
 * **The rule: a gate's verdict is only about the bytes it read.** Edit a file mid-run and every line
 * `verify` printed describes a tree that no longer exists, the green summary included — a false PASS, which
 * is the direction this repository always treats as the dangerous one. The incident and why the three
 * obvious cheaper checks do not work are in `.claude/knowledge/pitfalls.md` §Environment / tooling.
 *
 * CONTENT-hashed, never mtime: mtime moves when nothing changed (a checkout, a byte-identical rewrite), and
 * a gate that cries wolf is the one people learn to ignore. Scope is `repoFiles`, so untracked-but-unignored
 * work counts — a file created mid-run is exactly what index-only scoping misses.
 *
 * @param {string} repo Repository root.
 * @param {string[]} [files] Override the file list (tests supply their own).
 * @returns {Map<string, string>} relative path → sha256 of its bytes, or `ABSENT` if it could not be read.
 */
export const fingerprintTree = (repo, files = repoFiles(repo)) => {
  const prints = new Map();
  for (const rel of files) {
    try {
      prints.set(rel, crypto.createHash('sha256').update(fs.readFileSync(path.join(repo, rel))).digest('hex'));
    } catch {
      // Unreadable and absent are the same fact here — "not the bytes the gates read". Recording a sentinel
      // rather than skipping is what makes a file DELETED mid-run visible instead of silently equal.
      prints.set(rel, 'ABSENT');
    }
  }
  return prints;
};

/**
 * What moved between two fingerprints, as sorted, human-readable lines.
 *
 * Reports the three kinds separately because they mean different things to a reader staring at a confusing
 * result: `modified` explains a gate that disagrees with what is on disk now, `added` explains a gate that
 * scanned less than the tree holds, `removed` explains one that scanned more.
 *
 * @param {Map<string, string>} before
 * @param {Map<string, string>} after
 * @returns {string[]} empty when the tree is byte-identical.
 */
export const fingerprintDrift = (before, after) => {
  const drift = [];
  for (const [rel, hash] of after) {
    if (!before.has(rel)) drift.push(`added:    ${rel}`);
    else if (before.get(rel) !== hash) drift.push(`modified: ${rel}`);
  }
  for (const rel of before.keys()) if (!after.has(rel)) drift.push(`removed:  ${rel}`);
  return drift.sort();
};
