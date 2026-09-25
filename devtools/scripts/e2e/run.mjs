// e2e/run — the runner behind `node devtools/dev.mjs e2e [all | pN | pN,pM] [--build]`, a `verify` gate.
//
// Suites are `pN.mjs` beside this file (a helper takes a leading `_`). Fail-closed both ways: a missing
// suite directory and a selector matching nothing each FAIL, because either would otherwise let the gate
// report success having run nothing.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const suitesDir = path.dirname(here);
const repoDefault = path.resolve(suitesDir, '..', '..', '..');

/** The suites among a directory listing, in NUMERIC order — a plain sort puts p10 before p2. */
export const discoverSuites = (files) => files
  .filter((f) => /^p\d+\.mjs$/.test(f))
  .map((f) => f.slice(0, -4))
  .sort((a, b) => Number(a.slice(1)) - Number(b.slice(1)));

/** The suites a selector names — `all`, `pN` or `pN,pM` — restricted to ones that exist. */
export function selectSuites(all, selector = 'all') {
  const wanted = selector === 'all' ? all : selector.split(',').map((s) => s.trim());
  return wanted.filter((s) => all.includes(s));
}

/**
 * Whether a finished suite passed: a clean exit, OR its own `e2e-<suite> PASS` line. A suite that logically
 * passed and then died in process teardown (the Windows libuv `UV_HANDLE_CLOSING` abort) has succeeded, so
 * the marker outranks the crash code.
 */
export const passed = (suite, { status, signal, out }) =>
  (status === 0 && !signal) || new RegExp(`\\ne2e-${suite} PASS\\b`).test(out ?? '');

/** @returns {number} the exit code */
export async function runE2e({ repo = repoDefault, args = [], log = console.log, error = console.error } = {}) {
  if (!fs.existsSync(suitesDir)) {
    error('e2e: ✗ no suite directory at devtools/scripts/e2e/ — nothing ran, so this gate proves nothing');
    return 1;
  }
  const all = discoverSuites(fs.readdirSync(suitesDir));
  const selector = args.find((a) => !a.startsWith('-')) ?? 'all';
  const suites = selectSuites(all, selector);
  if (suites.length === 0) {
    error(`e2e: ✗ no suites match "${selector}" — available: ${all.join(', ') || '(none)'}`);
    return 1;
  }
  if (args.includes('--build')) {
    log('e2e: building first…');
    const b = spawnSync('node', [path.join(repo, 'devtools', 'dev.mjs'), 'build'], { stdio: 'inherit', cwd: repo });
    if (b.status !== 0) { error('e2e: build failed — aborting'); return b.status ?? 1; }
  }

  const failed = [];
  for (const suite of suites) {
    const result = await new Promise((resolve) => {
      const child = spawn('node', [path.join(suitesDir, `${suite}.mjs`)], { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] });
      let out = '';
      child.stdout.on('data', (d) => { out += d; process.stdout.write(d); });
      child.stderr.on('data', (d) => { out += d; process.stderr.write(d); });
      child.on('close', (status, signal) => resolve({ status, signal, out }));
    });
    if (!passed(suite, result)) failed.push({ suite, ...result });
  }
  log(`\ne2e: ${suites.length - failed.length}/${suites.length} suites passed`);
  for (const f of failed) log(`  ✗ ${f.suite} — ${f.signal ? `signal ${f.signal}` : `exit ${f.status}`}`);
  return failed.length ? 1 : 0;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = await runE2e({ args: process.argv.slice(2) });
}
