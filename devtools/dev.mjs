// Lyntai devtools dispatcher (family pattern: one entry, project-specific inputs in project.config.mjs).
//   node devtools/dev.mjs build            - dotnet build the solution
//   node devtools/dev.mjs test [args]      - dotnet test the test project (extra args pass through)
//   node devtools/dev.mjs e2e [all|pN|pN-pM|p1,p3] [--build] [--parallel[=N]] - Playground e2e suites
//   node devtools/dev.mjs playground [args]- run the sample console app (uses LYNTAI_PROVIDER_CMD if set)
//   node devtools/dev.mjs pack             - dotnet pack the packable libraries -> publish/packages/
//   node devtools/dev.mjs install-hooks    - git core.hooksPath -> devtools/hooks (pre-commit guard)
//   node devtools/dev.mjs check-sensitive  - scan staged changes (--tree for all tracked files)
import { spawn, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import config from './project.config.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [cmd, ...args] = process.argv.slice(2);

const run = (exe, argv, opts = {}) => {
  const r = spawnSync(exe, argv, { stdio: 'inherit', cwd: repo, shell: false, ...opts });
  process.exitCode = r.status ?? 1;
};

switch (cmd) {
  case 'build':
    run('dotnet', ['build', config.solution, '-v', 'minimal']);
    break;

  case 'test':
    run('dotnet', ['test', config.testProject, '-v', 'minimal', ...args]);
    break;

  case 'playground':
    run('dotnet', ['run', '--project', config.playgroundProject, ...args]);
    break;

  case 'install-hooks':
    run('git', ['config', 'core.hooksPath', 'devtools/hooks']);
    console.log('git hooks installed (core.hooksPath = devtools/hooks). Pre-commit runs check-sensitive.');
    break;

  case 'check-sensitive':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-sensitive.mjs'), ...args]);
    break;

  case 'pack': {
    // dotnet pack each packable library → publish/packages/*.nupkg, then print id + sha256.
    const out = path.join(repo, 'publish', 'packages');
    fs.rmSync(out, { recursive: true, force: true });
    fs.mkdirSync(out, { recursive: true });
    for (const proj of config.packableProjects) {
      const r = spawnSync('dotnet', ['pack', proj, '-c', 'Release', '-o', out, '-v', 'minimal',
        `-p:Version=${config.version}`], { stdio: 'inherit', cwd: repo, shell: false });
      if (r.status !== 0) { process.exitCode = r.status ?? 1; break; }
    }
    for (const f of fs.readdirSync(out).filter((f) => f.endsWith('.nupkg'))) {
      const sha = crypto.createHash('sha256').update(fs.readFileSync(path.join(out, f))).digest('hex');
      console.log(`  ${f}\n    sha256: ${sha}`);
    }
    break;
  }

  case 'e2e': {
    const scriptsDir = path.join(repo, 'devtools', 'scripts', 'e2e');
    if (!fs.existsSync(scriptsDir)) { console.log('no e2e suites yet (devtools/scripts/e2e/)'); break; }
    const all = fs.readdirSync(scriptsDir)
      .filter((f) => /^p\d+\.mjs$/.test(f))   // suites live in scripts/e2e/ as p1.mjs, p2.mjs, …
      .map((f) => f.slice(0, -4))
      // NUMERIC order (p2 before p10) — a plain .sort() is lexicographic, which puts p3–p9 LAST.
      .sort((a, b) => Number(a.slice(1)) - Number(b.slice(1)));

    // Selector: all | pN | pN-pM (range) | p1,p3,p9 (list). Flags: --build (build first), --parallel[=N].
    const flags = args.filter((a) => a.startsWith('-'));
    const sel = args.find((a) => !a.startsWith('-')) ?? 'all';
    const doBuild = flags.includes('--build');
    const pFlag = flags.find((f) => f === '--parallel' || f.startsWith('--parallel=') || f === '-p' || f.startsWith('-p='));
    let limit = 1;
    if (pFlag) {
      const n = pFlag.includes('=') ? Number(pFlag.split('=')[1]) : NaN;
      limit = Number.isFinite(n) && n > 0 ? n : Math.max(2, Math.min(6, (os.cpus().length || 4) - 2));
    }

    const expand = (s) => {
      if (s === 'all') return all;
      if (s.includes(',')) return s.split(',').map((x) => x.trim());
      const range = s.match(/^p(\d+)-p?(\d+)$/);
      if (range) {
        const [lo, hi] = [Number(range[1]), Number(range[2])];
        return all.filter((x) => { const n = Number(x.slice(1)); return n >= lo && n <= hi; });
      }
      return [s];
    };
    const suites = expand(sel).filter((s) => all.includes(s));
    if (suites.length === 0) { console.log(`no e2e suites match "${sel}"`); break; }

    if (doBuild) {
      console.log('e2e: building first…');
      const b = spawnSync('node', [path.join(repo, 'devtools', 'dev.mjs'), 'build'], { stdio: 'inherit', cwd: repo });
      if (b.status !== 0) { console.error('e2e: build failed — aborting'); process.exitCode = b.status ?? 1; break; }
    }

    // A suite passes on a clean exit-0, OR if it printed its "PASS" line — a suite that logically passed
    // but then died in process teardown (the Windows libuv UV_HANDLE_CLOSING abort) has succeeded; trust
    // the marker over the crash code.
    const runOne = (suite, capture) => new Promise((resolve) => {
      const t0 = Date.now();
      const child = spawn('node', [path.join(scriptsDir, `${suite}.mjs`)], { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] });
      let out = '';
      const tee = (dest) => (d) => { out += d; if (!capture) dest.write(d); };
      child.stdout?.on('data', tee(process.stdout));
      child.stderr?.on('data', tee(process.stderr));
      child.on('close', (code, signal) => {
        const logicallyPassed = new RegExp(`\\ne2e-${suite} PASS`).test(out);
        resolve({ suite, passed: (code === 0 && !signal) || logicallyPassed, status: code, signal, out, ms: Date.now() - t0 });
      });
    });

    const results = [];
    const wallStart = Date.now();
    if (limit <= 1) {
      for (const suite of suites) results.push(await runOne(suite, false));
    } else {
      console.log(`e2e: ${suites.length} suites, up to ${limit} at once…`);
      const pending = [...suites];
      const running = [];
      const launch = (suite) => {
        const entry = { promise: runOne(suite, true).then((rec) => {
          running.splice(running.indexOf(entry), 1);
          results.push(rec);
          process.stdout.write(rec.out);
          console.log(`  ${rec.passed ? '✓' : '✗'} ${rec.suite} (${(rec.ms / 1000).toFixed(0)}s)`);
        }) };
        running.push(entry);
      };
      while (pending.length || running.length) {
        while (pending.length && running.length < limit) launch(pending.shift());
        if (running.length) await Promise.race(running.map((e) => e.promise));
      }
    }

    results.sort((a, b) => Number(a.suite.slice(1)) - Number(b.suite.slice(1)));
    const wall = ((Date.now() - wallStart) / 1000).toFixed(0);
    const failed = results.filter((r) => !r.passed);
    console.log(`\ne2e: ${results.length - failed.length}/${results.length} suites passed in ${wall}s${limit > 1 ? ` (parallel ×${limit})` : ''}`);
    for (const f of failed) console.log(`  ✗ ${f.suite} — ${f.signal ? `signal ${f.signal}` : `exit ${f.status}`}`);
    if (failed.length) process.exitCode = 1;
    break;
  }

  default:
    console.log('usage: node devtools/dev.mjs <build|test|e2e|playground|pack|install-hooks|check-sensitive>');
    process.exitCode = cmd ? 1 : 0;
}
