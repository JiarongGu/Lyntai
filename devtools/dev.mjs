// Lyntai devtools dispatcher: one entry point over the roster in `devtools/commands.mjs`, with the project's
// inputs in `project.config.mjs`. `node devtools/dev.mjs` with no argument lists every command; CLAUDE.md
// §Dev loop is the same list with a description each; `docs/GATES.md` says what each gate is FOR. A
// script-backed command documents its own arguments at the top of its script, a sweep in its bench class.
import { spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { COMMANDS, VERIFY_STEPS } from './commands.mjs';
import config from './project.config.mjs';
import { changelogDoctor, claudeDoctor, packDoctor, versionDoctor } from './scripts/doctors.mjs';
import { fingerprintDrift, fingerprintTree } from './scripts/_tree-fingerprint.mjs';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..');

const run = (exe, argv, opts = {}) => {
  const r = spawnSync(exe, argv, { stdio: 'inherit', cwd: repo, shell: false, ...opts });
  process.exitCode = r.status ?? 1;
};

/** BenchmarkDotNet refuses a Debug build, so the bench always runs Release. */
const bench = (argv) => {
  if (!config.benchProject) { console.log('no bench project configured'); return; }
  run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', ...argv]);
};

const valueOf = (args, flag) => {
  const at = args.indexOf(flag);
  return at >= 0 ? args[at + 1] : undefined;
};

/** The commands `dev.mjs` runs itself, keyed by name. Every `builtin` in the roster has exactly one. */
export const BUILTINS = {
  build: () => run('dotnet', ['build', config.solution, '-v', 'minimal']),
  test: (args) => run('dotnet', ['test', config.testProject, '-v', 'minimal', ...args]),
  playground: (args) => run('dotnet', ['run', '--project', config.playgroundProject, ...args]),
  bench: (args) => bench(args),
  'install-hooks': () => {
    run('git', ['config', 'core.hooksPath', 'devtools/hooks']);
    console.log('git hooks installed (core.hooksPath = devtools/hooks). Pre-commit runs check-sensitive, '
      + 'check-version-bump and check-encoding --staged.');
  },
  doctor: (args) => {
    // All three always run, so drift is reported in one pass. `--fix` syncs the README headline and never
    // the version: a hand-authored version is the defect, restored by hand or by the release workflow.
    const readmeOk = packDoctor({ repo, version: config.version, fix: args.includes('--fix') });
    const claudeOk = claudeDoctor({ repo, version: config.version });
    const versionOk = versionDoctor({ repo, version: config.version });
    process.exitCode = readmeOk && claudeOk && versionOk ? 0 : 1;
  },
  changelog: (args) => {
    // An explicit act of RELEASING, never a side effect of `doctor` or `pack`.
    process.exitCode = changelogDoctor({
      repo, fix: args.includes('--fix'), version: valueOf(args, '--version') ?? config.version, date: valueOf(args, '--date'),
    }) ? 0 : 1;
  },
  pack: () => {
    // The release pipeline bumps the version, so pack syncs the README headline first — and stops if it
    // cannot, rather than packing a README that advertises no version.
    if (!packDoctor({ repo, version: config.version, fix: true })) { process.exitCode = 1; return; }
    const out = path.join(repo, 'publish', 'packages');
    fs.rmSync(out, { recursive: true, force: true });
    fs.mkdirSync(out, { recursive: true });
    for (const proj of config.packableProjects) {
      const r = spawnSync('dotnet', ['pack', proj, '-c', 'Release', '-o', out, '-v', 'minimal',
        `-p:Version=${config.version}`], { stdio: 'inherit', cwd: repo, shell: false });
      if (r.status !== 0) { process.exitCode = r.status ?? 1; return; }
    }
    for (const f of fs.readdirSync(out).filter((f) => f.endsWith('.nupkg'))) {
      const sha = crypto.createHash('sha256').update(fs.readFileSync(path.join(out, f))).digest('hex');
      console.log(`  ${f}\n    sha256: ${sha}`);
    }
  },
  verify: () => {
    // Fingerprinted before and after: a file edited while the gates run makes the whole report describe a
    // tree that no longer exists, so the green line is suppressed and the run fails (`_tree-fingerprint.mjs`).
    const treeBefore = fingerprintTree(repo);
    let failed = null;
    for (const [step, extra] of VERIFY_STEPS) {
      console.log(`\n=== verify: ${step} ===`);
      const r = spawnSync('node', [here, step, ...extra], { stdio: 'inherit', cwd: repo });
      if (r.status !== 0) { failed = step; process.exitCode = r.status ?? 1; break; }
    }
    const drift = fingerprintDrift(treeBefore, fingerprintTree(repo));
    if (failed) console.error(`\nverify: ✗ FAILED at ${failed}`);
    else if (!drift.length) {
      console.log(`\nverify: ✓ all ${VERIFY_STEPS.length} gates green (${VERIFY_STEPS.map(([s]) => s).join(' · ')})`);
    }
    if (drift.length) {
      console.error('\nverify: ✗ THE TREE CHANGED WHILE VERIFY WAS RUNNING — this result describes neither '
        + 'the tree it started on nor the one on disk now. Re-run it on a tree that holds still.');
      for (const line of drift) console.error(`  ${line}`);
      process.exitCode ||= 1;
    }
  },
};

/** Run one command from the roster; an unknown or absent one prints the list. */
export function dispatch(cmd, args) {
  const entry = COMMANDS.find((c) => c.name === cmd);
  if (!entry) {
    console.log(`usage: node devtools/dev.mjs <${COMMANDS.map((c) => c.name).join('|')}>`);
    process.exitCode = cmd ? 1 : 0;
    return;
  }
  if (entry.script) run('node', [path.join(repo, 'devtools', 'scripts', `${entry.script}.mjs`), ...args]);
  else if (entry.bench) bench([entry.bench, ...args]);
  else BUILTINS[entry.name](args);
}

// CLI entry point — a thin wrapper, so importing this module for a test runs no command.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const [cmd, ...args] = process.argv.slice(2);
  dispatch(cmd, args);
}
