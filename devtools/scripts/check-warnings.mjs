// check-warnings — FAIL if any published (`src/`) project compiles with a warning.
//
// Not style policing: `IsAotCompatible=true` stamps `IsTrimmable` into the assembly, telling a consumer's
// trimmer "safe to trim", and the ONLY thing that catches code breaking that promise is an IL2026/IL3050
// warning — a warning nobody fails on is a warning nobody reads, and four of them shipped into
// Lyntai.Providers.Basic exactly that way. Doc-comment warnings matter for the same reason: an unresolved
// cref ships inside the XML docs consumers read in IntelliSense. Scoped to `src/` — tests and samples are
// free to warn. `--list` prints them all instead of the first 15.
//
// THE THREE BUILD FLAGS ARE LOAD-BEARING, and two of them fail in the direction that reports success:
//   · `-v normal` — `minimal` does not print the per-project warning lines this parses at all.
//   · `--no-incremental` — MSBuild does not re-emit warnings for a project it did not rebuild, so a second
//     run over an unchanged tree reports a clean `src/` no matter what is in it. A gate that passes on its
//     second run is worse than no gate.
//   · `maxBuffer` — Node's spawnSync default is 1 MiB and a full-solution `-v normal` log has come within
//     6,846 bytes of it (99.35% full). Past that, spawnSync throws ENOBUFS, `status` comes back null, and
//     the status check below reports "build FAILED" for a build that SUCCEEDED, pointing nowhere near the
//     real cause. See .claude/knowledge/pitfalls.md; `check-warnings.test.mjs` pins all three.
// `-warnaserror` is deliberately NOT used: it stops at the first project, hiding the rest.
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repoDefault = join(dirname(here), '..', '..');

/**
 * An MSBuild diagnostic line carrying a warning CODE — `warning` alone is prose, and prose is not a defect.
 *
 * Both id families are real: longer than four letters (`SYSLIB0011`) and not all upper case (analyzer ids
 * such as `xUnit1013`) — a narrower pattern reports `src/` clean over either. Still BOUNDED: two to ten
 * letters then a digit, so `warning 42` stays prose, and the literal `warning` is lower case as MSBuild
 * always emits it.
 */
export const WARNING_CODE = /warning [A-Za-z]{2,10}\d+/;

/** A compiler ERROR line, shown when the build fails — anywhere, since an error in a test fails the build too. */
export const ERROR_CODE = /: error [A-Za-z]{2,10}\d+/;

/** …and only in a PUBLISHED project. Both separators, because the log's paths are the platform's. */
export const IN_SRC = /[\\/]src[\\/]/;

/** Generous by design; see the module note. Anything at or above this is out of the ENOBUFS failure mode. */
export const BUILD_MAX_BUFFER = 64 * 1024 * 1024;

/**
 * The warning lines of a build log, deduplicated.
 *
 * MSBuild emits the same diagnostic once per logger pass and once per target framework, so an un-deduped
 * list reports one defect as three and a reader starts discounting the count.
 */
export function warningLines(stdout) {
  return [...new Set((stdout || '').split(/\r?\n/).filter((l) => WARNING_CODE.test(l) && IN_SRC.test(l)))];
}

/** The build this gate parses. `spawn` is a seam so a test can pin the flags without compiling anything. */
export const runBuild = (repo, solution, spawn = spawnSync) =>
  spawn('dotnet', ['build', solution, '-v', 'normal', '--no-incremental'],
    { cwd: repo, encoding: 'utf8', maxBuffer: BUILD_MAX_BUFFER });

/**
 * The gate. `build` is a seam so a test can drive every branch — including the two failure branches, which
 * only happen when the toolchain is unhappy and are therefore the ones least likely to be noticed wrong.
 */
export function checkWarnings({
  repo = repoDefault,
  config,
  log = console.log,
  error = console.error,
  list = false,
  build = null,
} = {}) {
  const label = 'check-warnings';
  const r = (build ?? (() => runBuild(repo, config.solution)))();
  const lines = warningLines(r.stdout);

  // A non-zero status AND a null one (spawn never completed — ENOBUFS, a missing `dotnet`) both land here.
  // Reporting the build rather than the warnings is right in both cases: the log is not trustworthy, and
  // "src/ compiles warning-free" over a log that was truncated or never produced is the false green this
  // gate exists to prevent.
  if (r.status !== 0) {
    error(`${label}: build FAILED — fix the build first`);
    if (r.error) error(`  ${r.error.code ?? r.error.message}`);
    // This is `verify`'s only build, so it shows what failed rather than only that something did.
    const errors = [...new Set((r.stdout || '').split(/\r?\n/).filter((l) => ERROR_CODE.test(l)))];
    for (const l of errors.slice(0, 20)) error(`  ${l.replace(repo, '.').trim()}`);
    if (errors.length > 20) error(`  … ${errors.length - 20} more`);
    return r.status ?? 1;
  }
  if (!lines.length) {
    log(`${label}: src/ compiles warning-free ✓`);
    return 0;
  }
  const show = list ? lines : lines.slice(0, 15);
  error(`${label}: ✗ ${lines.length} warning(s) in src/ — a published project must compile clean`);
  for (const l of show) error(`  ${l.replace(repo, '.').trim()}`);
  if (show.length < lines.length) error(`  … ${lines.length - show.length} more (--list to see all)`);
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test builds nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths, and a wrong comparison
// makes this gate silently do NOTHING and exit 0. Pinned by cli-entry.test.mjs.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkWarnings({ repo: repoDefault, config, list: process.argv.includes('--list') });
}
