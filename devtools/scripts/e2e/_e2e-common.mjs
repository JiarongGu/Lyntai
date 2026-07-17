// Shared e2e harness (lives with the suites in devtools/scripts/e2e/). Leading `_` → the runner
// (dev.mjs, `^p\d+\.mjs$`) never picks this up as a suite. Each suite imports what it needs; the
// boilerplate (reporter, Playground runner) lives here once.
//
// Unlike the sibling apps (which boot a long-running server), Lyntai is a library: its e2e boots the
// one-shot `Lyntai.Playground` console app against an isolated temp data folder with LYNTAI_PROVIDER_CMD
// = the provider-stub (deterministic, no real tokens), then asserts its exit code + stdout markers.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// this file is devtools/scripts/e2e/_e2e-common.mjs → three levels up is the repo root.
export const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
export const providerStubCmd = `node ${path.join(repo, 'devtools', 'scripts', 'provider-stub.mjs')}`;
export const dataDirFor = (suite) => path.join(repo, 'devtools', `_e2e-${suite}-data`);

/** Assert reporter. `ok(name, cond, extra?)` logs + counts; `done()` prints the PASS/FAIL line the runner
 *  greps for and exits with the right code. `fail()` bumps the counter for a caught fatal. */
export function makeReporter(suite) {
  let failures = 0;
  const ok = (name, cond, extra = '') => {
    console.log(`${cond ? '  ✓' : '  ✗'} ${name}${cond || !extra ? '' : ` — ${extra}`}`);
    if (!cond) failures++;
  };
  const fail = (msg) => { if (msg) console.error(msg); failures++; };
  const done = () => {
    console.log(failures === 0 ? `\ne2e-${suite} PASS` : `\ne2e-${suite} FAIL (${failures})`);
    process.exit(failures === 0 ? 0 : 1);
  };
  return { ok, fail, done };
}

/** Skip a suite gracefully (exit 0, counts as PASS) when a prerequisite is absent — keeps `e2e all` green
 *  on a fresh box / CI that hasn't provisioned something heavy. */
export function skipSuite(suite, reason) {
  console.log(`  · ${reason} — skipping ${suite} (no failures).`);
  console.log(`\ne2e-${suite} PASS (skipped)`);
  process.exit(0);
}

/** Fresh isolated data folder for a suite (removes any prior run). */
export function freshDataDir(suite) {
  const dir = dataDirFor(suite);
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });
  return dir;
}

/** Run the Playground once against an isolated data folder + the provider-stub. Returns { status, out }. */
export function runPlayground({ dataDir, env = {} }) {
  const r = spawnSync('dotnet', ['run', '--project', 'samples/Lyntai.Playground', '--no-build'], {
    cwd: repo,
    env: {
      ...process.env,
      LYNTAI_DATA: dataDir,
      LYNTAI_PROVIDER_CMD: providerStubCmd,
      ...env,
    },
    encoding: 'utf8',
  });
  return { status: r.status, out: `${r.stdout ?? ''}${r.stderr ?? ''}` };
}
