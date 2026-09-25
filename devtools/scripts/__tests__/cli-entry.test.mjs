// Every guard's CLI entry point still FIRES when the script is run the way this repository runs it.
//
// The suites next door drive each guard through an exported function, so none of them would notice if the
// `if (import.meta.main ?? …)` wrapper at the bottom of a script stopped matching. That failure is total
// and silent: the process starts, scans nothing, prints nothing and exits 0, so the pre-commit hook and
// `verify` both go green over an unscanned tree.
//
// It asserts the guard SPOKE, never that this repository is clean — the exit code is deliberately not
// checked; real drift is reported by the gate that owns it. The facts are GENERATED from the roster, so a
// gate joining `verify` gets one by construction rather than by someone remembering to add it.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { COMMANDS, VERIFY_STEPS } from '../../commands.mjs';
import { repoRoot } from './_fixtures.mjs';

const spawnNode = (argv, env) => {
  const r = spawnSync(process.execPath, argv,
    { cwd: repoRoot, encoding: 'utf8', env, maxBuffer: 32 * 1024 * 1024 });
  return `${r.stdout ?? ''}${r.stderr ?? ''}`;
};

/** The script with `dotnet` unreachable: PATH scrubbed in every casing Windows accepts. It changes nothing
 * about the gate, costs a failed spawn, and still exercises the entry point and the failure report. */
const withoutDotnet = () => ({
  ...Object.fromEntries(Object.entries(process.env).filter(([k]) => !/^path$/i.test(k))), PATH: '',
});

/**
 * How each script is proved to have RUN, when a plain invocation will not do: a gate whose real work is a
 * `dotnet` build or restore runs without `dotnet`; a read-only flag where the plain run would compile or
 * write; the one branch a silent-by-design script always prints. `test-devtools` is left out: running it
 * here would run this suite again, recursively.
 */
const HOW = {
  'check-warnings': { env: withoutDotnet },
  'check-bundle': { env: withoutDotnet },
  'check-samples': { args: ['--list'] },
  'check-sensitive': { args: ['--tree'] },
  'check-version': { env: () => ({ ...process.env, LYNTAI_RELEASE: '1' }), expect: /check-version-bump: skipped \(LYNTAI_RELEASE=1/ },
  'decisions-index': { args: ['--check'] },
  e2e: { args: ['p0'], expect: /^e2e: ✗ no suites match "p0"/m },
  'new-package': { expect: /^usage: node devtools\/dev\.mjs new-package/m },
  'new-migration': { expect: /^usage: node devtools\/dev\.mjs new-migration/m },
};

describe('every script-backed `verify` step, and the read-only tools, speak when run as scripts', () => {
  const wanted = [...VERIFY_STEPS.map(([name]) => name), 'check-version', 'decisions-index', 'new-package', 'new-migration'];
  const roster = COMMANDS.filter((c) => c.script && wanted.includes(c.name) && c.name !== 'test-devtools');

  it('the roster is not empty — a fact list generated from nothing proves nothing', () => {
    assert.ok(roster.length > 15, `expected most verify gates to be scripts, found ${roster.length}`);
  });

  for (const { name, script } of roster) {
    const how = HOW[name] ?? {};
    it(`${name} reports when invoked as a script`, () => {
      const out = spawnNode([join(repoRoot, 'devtools', 'scripts', `${script}.mjs`), ...(how.args ?? [])],
        how.env ? how.env() : process.env);
      assert.match(out, how.expect ?? new RegExp(`^${name}: `, 'm'));
    });
  }
});

describe('the dispatcher commands that have no script of their own', () => {
  // The doctors live in scripts/doctors.mjs with no CLI entry — dev.mjs IS their command line, so what
  // needs pinning is the DISPATCH. Both run read-only: `--fix` is what writes, and it is never passed.
  const dispatch = (cmd) => spawnNode([join(repoRoot, 'devtools', 'dev.mjs'), cmd], process.env);

  it('doctor runs the README and the version doctors in one pass', () => {
    const out = dispatch('doctor');
    assert.match(out, /^pack-doctor: /m);
    assert.match(out, /^version-doctor: /m, 'all run — drift is reported in one pass, not one at a time');
  });

  it('changelog runs the changelog doctor', () => {
    assert.match(dispatch('changelog'), /^changelog-doctor: /m);
  });
});
