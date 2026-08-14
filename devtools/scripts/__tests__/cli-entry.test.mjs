// Every guard's CLI entry point still FIRES when the script is run the way this repository runs it.
//
// The suites next door drive each guard through an exported function, which is what makes them fast and
// hermetic — but it means none of them would notice if the `if (import.meta.main ?? …)` wrapper at the
// bottom of a script stopped matching. That failure is total and silent: the process starts, scans nothing,
// prints nothing, and exits 0, so the pre-commit hook and `verify` both go green over an unscanned tree.
// This file is the only thing standing between that and a clean run.
//
// It asserts the guard SPOKE, never that this repository is clean — the exit code is deliberately not
// checked. Real drift must be reported by the gate that owns it (`verify` runs these tests FIRST, so a
// doc-drift failure surfacing here instead of at `check-docs` would blame the wrong thing).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { repoRoot } from './_fixtures.mjs';

const invoke = (script, args = [], env = {}) => {
  const r = spawnSync('node', [join(repoRoot, 'devtools', 'scripts', script), ...args],
    { cwd: repoRoot, encoding: 'utf8', env: { ...process.env, ...env }, maxBuffer: 32 * 1024 * 1024 });
  return `${r.stdout ?? ''}${r.stderr ?? ''}`;
};

/**
 * The two gates whose real work is a `dotnet` invocation, run with `dotnet` unreachable.
 *
 * Neither can be proved cheaply the way the others are: check-warnings' whole job is a `--no-incremental`
 * solution build (minutes) and check-bundle's is a restore. Every flag that would shortcut that — a
 * `--log <file>`, a build-command override, a probe env var — is also a way to make the gate PASS without
 * doing its job, and a gate with a fake-it switch is worse than one with an untested wrapper.
 *
 * Scrubbing PATH is the honest alternative: it changes nothing about the gate, costs a failed spawn, and
 * still exercises the entry point, the config load and the failure report. `process.execPath` because the
 * test's own `node` also needs finding. Windows resolves the variable case-insensitively, so every casing
 * of it goes.
 */
const invokeWithoutDotnet = (script, args = []) => {
  const env = Object.fromEntries(Object.entries(process.env).filter(([k]) => !/^path$/i.test(k)));
  const r = spawnSync(process.execPath, [join(repoRoot, 'devtools', 'scripts', script), ...args],
    { cwd: repoRoot, encoding: 'utf8', env: { ...env, PATH: '' }, maxBuffer: 32 * 1024 * 1024 });
  return `${r.stdout ?? ''}${r.stderr ?? ''}`;
};

describe('the guard CLIs run when invoked as scripts', () => {
  it('check-docs reports on the tracked docs', () => {
    assert.match(invoke('check-docs.mjs'), /^check-docs: /m);
  });

  it('check-links reports on the tracked docs', () => {
    assert.match(invoke('check-links.mjs'), /^check-links: /m);
  });

  it('check-api-vocabulary reports on the API baselines', () => {
    assert.match(invoke('check-api-vocabulary.mjs'), /^check-api-vocabulary: /m);
  });

  it('check-packages reports on the package inventory', () => {
    assert.match(invoke('check-packages.mjs'), /^check-packages: /m);
  });

  it('check-samples --list reports the documented-sample inventory', () => {
    // `--list` and not a real run: this suite is `verify`'s FIRST step and check-samples compiles, which
    // costs seconds. The inventory path still exercises the entry point, the tracked-file scan and the
    // extractor — everything except the build.
    assert.match(invoke('check-samples.mjs', ['--list']), /^check-samples: /m);
  });

  it('check-sensitive --tree reports on the tracked files', () => {
    assert.match(invoke('check-sensitive.mjs', ['--tree']), /check-sensitive: /);
  });

  it('check-bundle runs — reported through its own restore, with dotnet unreachable', () => {
    assert.match(invokeWithoutDotnet('check-bundle.mjs'), /^check-bundle: /m);
  });

  it('check-warnings runs — reported through its own build, with dotnet unreachable', () => {
    assert.match(invokeWithoutDotnet('check-warnings.mjs'), /^check-warnings: /m);
  });

  it('check-version-bump runs — proved through the one branch that always prints', () => {
    // A clean index is silent by design, which is exactly the output a non-running script produces, so the
    // escape hatch is what makes "it ran" observable at all.
    assert.match(invoke('check-version-bump.mjs', [], { LYNTAI_RELEASE: '1' }),
      /check-version-bump: skipped \(LYNTAI_RELEASE=1/);
  });

  it('decisions-index --check reports on the real record', () => {
    // `--check` and not a plain run: this suite must never WRITE to a tracked document.
    assert.match(invoke('decisions-index.mjs', ['--check']), /^decisions-index: /m);
  });

  it('new-package runs — proved through its usage refusal, which scaffolds nothing', () => {
    assert.match(invoke('new-package.mjs'), /^usage: node devtools\/dev\.mjs new-package/m);
  });
});

describe('the dispatcher commands that have no script of their own', () => {
  // The three doctors live in scripts/doctors.mjs with no CLI entry of their own — dev.mjs IS their command
  // line, so what needs pinning is the DISPATCH, not an `import.meta.main` guard. Both run read-only here:
  // `--fix` is what writes, and it is never passed.
  const dispatch = (cmd, args = []) => {
    const r = spawnSync('node', [join(repoRoot, 'devtools', 'dev.mjs'), cmd, ...args],
      { cwd: repoRoot, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
    return `${r.stdout ?? ''}${r.stderr ?? ''}`;
  };

  it('doctor runs both the README and the version doctor in one pass', () => {
    const out = dispatch('doctor');
    assert.match(out, /^pack-doctor: /m);
    assert.match(out, /^version-doctor: /m, 'both always run — drift is reported in one pass, not one at a time');
  });

  it('changelog runs the changelog doctor', () => {
    assert.match(dispatch('changelog'), /^changelog-doctor: /m);
  });
});
