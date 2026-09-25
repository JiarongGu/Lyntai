// test-devtools — the guards' own tests, and the `guard-script tests N/N` baseline CLAUDE.md quotes.
//
// It runs FIRST in `verify`: every gate after it fails permissively when broken, so a broken guard reports
// a clean repository (`docs/GATES.md` §test-devtools). Quiet when green; `--test-reporter=…` or `--watch`
// streams the runner itself, which is what to use while writing a test.
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { checkQuotedBaseline } from './_baseline.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** A GLOB, never the bare directory: Node 24 loads a directory argument as a module. Forward slashes. */
export const PATTERN = 'devtools/scripts/__tests__/*.test.mjs';

/**
 * The three counts out of a `node --test` summary — `{ tests, passed, failed }`, each NaN when absent.
 *
 * ANSI is stripped BEFORE matching: the spec reporter writes a colour escape in front of the `ℹ` the counts
 * are anchored on whenever FORCE_COLOR is set, which it honours through the pipe this gate reads. NaN rather
 * than 0 when a count is missing, so "could not read the run" never reads as "nothing failed".
 */
export function testSummaryCounts(out) {
  const plain = String(out ?? '').replace(/\u001b\[[0-9;]*m/g, '');
  const count = (name) => Number((plain.match(new RegExp(`^\\u2139 ${name} (\\d+)`, 'm')) ?? [])[1] ?? NaN);
  return { tests: count('tests'), passed: count('pass'), failed: count('fail') };
}

/**
 * The gate: run the suite, and on a green run compare its own counts against CLAUDE.md's
 * `guard-script tests N/N` — a run-derived number is checked by the run that produces it
 * (`_baseline.mjs`). `run` is a seam so a test drives every branch without spawning the suite.
 * @returns {number} the exit code
 */
export function testDevtools({
  repo = repoDefault, args = [], log = console.log, error = console.error,
  run = (argv) => spawnSync('node', argv, { cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }),
} = {}) {
  const label = 'test-devtools';
  // The reporter is NAMED: a `node --test` spawned inside a test inherits NODE_TEST_CONTEXT and would
  // otherwise switch to TAP (`# pass N`), which the summary reader cannot parse.
  const r = run(['--test', '--test-reporter=spec', ...args, PATTERN]);
  const out = `${r.stdout ?? ''}${r.stderr ?? ''}`;
  const { tests, passed, failed } = testSummaryCounts(out);
  if (r.status === 0 && failed === 0 && tests > 0) {
    log(`${label}: ${passed}/${tests} guard-script tests pass ✓`);
    return checkQuotedBaseline(repo, { gate: label, label: 'guard-script tests', passed, total: tests }, error);
  }
  error(out.trimEnd());
  // An UNREADABLE summary is its own diagnosis: the counts say nothing failed only because nothing parsed.
  const unreadable = !Number.isFinite(tests) || !Number.isFinite(failed);
  error(`\n${label}: ✗ the scripts that GATE this repository are themselves failing`
    + `${Number.isFinite(failed) && failed > 0 ? ` — ${failed} test(s)` : ''}\n`
    + (unreadable
      ? `  Its SUMMARY did not parse (exit ${r.status}), so this is not a counted test failure. Re-run\n`
        + '  `node devtools/dev.mjs test-devtools --test-reporter=spec` to see the runner directly.\n'
      : '')
    + '  Nothing below this gate can be trusted until it is green: each of these scripts fails permissively,\n'
    + '  so a broken one reports a clean repository.');
  return r.status || 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const args = process.argv.slice(2);
  if (args.some((a) => a.startsWith('--test-reporter') || a === '--watch')) {
    process.exitCode = spawnSync('node', ['--test', ...args, PATTERN], { cwd: repoDefault, stdio: 'inherit' }).status ?? 1;
  } else {
    process.exitCode = testDevtools({ args });
  }
}
