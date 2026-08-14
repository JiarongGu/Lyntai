// Shared fixture helpers for the guard-script tests (`node devtools/dev.mjs test-devtools`).
//
// Two rules shape everything here:
//   · fixtures live under the gitignored `devtools/_*` scratch area, NEVER OS temp
//     (.claude/rules/no-tmp-for-repo-files.md);
//   · a guard is driven through its exported seam against a fixture ROOT, never against this repository —
//     a test that reads the real tree would pass or fail for reasons that have nothing to do with the guard.
//
// The leading underscore keeps this file out of `node --test`'s discovery patterns, the same trick
// `devtools/scripts/e2e/_e2e-common.mjs` uses.
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** Write one file into a fixture tree, creating parents. `content` may be a string or a Buffer. */
export function writeInto(dir, rel, content) {
  const file = join(dir, rel);
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, content);
}

/**
 * A throwaway tree under devtools/_guard-tests-*, populated from `{ 'rel/path': content }`.
 * Pass the returned dir to `removeTree` (from a `t.after`) when the test ends.
 */
export function makeTree(files = {}) {
  const dir = mkdtempSync(join(repoRoot, 'devtools', '_guard-tests-'));
  for (const [rel, content] of Object.entries(files)) writeInto(dir, rel, content);
  return dir;
}

export function removeTree(dir) {
  rmSync(dir, { recursive: true, force: true });
}

export function git(dir, args) {
  return execFileSync('git', args, { cwd: dir, encoding: 'utf8' });
}

/** `makeTree` + a real repository around it — for the checks that read git itself (staged diffs, ls-files). */
export function makeRepo(files = {}) {
  const dir = makeTree(files);
  git(dir, ['init', '-q']);
  git(dir, ['config', 'user.email', 'guard-tests@example.invalid']);
  git(dir, ['config', 'user.name', 'Guard Tests']);
  git(dir, ['config', 'commit.gpgsign', 'false']);
  git(dir, ['config', 'core.autocrlf', 'false']);   // keep fixture bytes exactly as written (and git quiet)
  return dir;
}

/** A log sink that records what a guard would have printed, so a test can assert on the message. */
export function recorder() {
  const lines = [];
  const sink = (...args) => lines.push(args.join(' '));
  sink.lines = lines;
  sink.text = () => lines.join('\n');
  return sink;
}
