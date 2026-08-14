// check-bundle — the `Lyntai` bundle's dependency BUDGET. See devtools/scripts/check-bundle.mjs.
//
// What is at stake (docs/DECISIONS.md D26): every package in the bundle's closure is forced on every
// one-line-install consumer, because an untrimmed `dotnet publish` copies the whole graph and analyses
// nothing. A false pass here silently grows what everyone downloads, and nothing downstream reports it —
// the package is simply there, and next month it is load-bearing.
//
// This gate was inline in dev.mjs until 2026-08-11 and had never been driven by anything but the real
// repository, which exercises exactly ONE of its five outcomes (the green one). The two failure branches
// and the two toolchain branches below had never run.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { bundleBudget, bundleClosure, checkBundle, restoreBundle } from '../check-bundle.mjs';
import { makeTree, recorder, removeTree, writeInto } from './_fixtures.mjs';

/** A `project.assets.json` shaped like the real one: a `libraries` map keyed `id/version`, typed. */
const assets = (entries) => JSON.stringify({
  version: 3,
  libraries: Object.fromEntries(Object.entries(entries).map(([k, type]) => [k, { type, sha512: 'x' }])),
});

const BUNDLE = 'src/Lyntai.Bundle';
const configWith = (allowedThirdParty) => ({ bundle: { project: BUNDLE, allowedThirdParty } });

/** Drive the gate over a fixture tree, with the restore stubbed out — dotnet is not what is under test. */
function run(assetsJson, allowedThirdParty = [], { restore = () => ({ status: 0 }), writeAssets = true } = {}) {
  const dir = makeTree({});
  if (writeAssets) writeInto(dir, `${BUNDLE}/obj/project.assets.json`, assetsJson);
  const log = recorder();
  try {
    return { code: checkBundle({ repo: dir, config: configWith(allowedThirdParty), log, error: log, restore }), out: log.text() };
  } finally {
    removeTree(dir);
  }
}

describe('check-bundle — the closure it reads', () => {
  it('counts PACKAGES and never this repository\'s own projects', () => {
    // `libraries` lists every project in the graph too. Counting those would report Lyntai's own packages as
    // undecided third-party dependencies — the budget failing on its own success.
    const closure = bundleClosure(JSON.parse(assets({
      'Microsoft.Extensions.AI.Abstractions/9.0.0': 'package',
      'Lyntai.Core/1.0.0': 'project',
      'ModelContextProtocol.Core/1.4.1': 'package',
    })));
    assert.deepEqual(closure.map((p) => p.id), ['Microsoft.Extensions.AI.Abstractions', 'ModelContextProtocol.Core']);
    assert.equal(closure[1].version, '1.4.1', 'the version is carried, so a report names what it found');
  });

  it('survives an assets file with no libraries at all', () => {
    assert.deepEqual(bundleClosure({}), []);
    assert.deepEqual(bundleClosure(null), []);
  });
});

describe('check-bundle — the budget', () => {
  it('auto-allows the Microsoft.Extensions.* band and only that band', () => {
    const { band, outside } = bundleBudget([
      { id: 'Microsoft.Extensions.DependencyInjection', version: '9.0.0' },
      { id: 'Microsoft.Extensions.AI', version: '9.0.0' },
      { id: 'Microsoft.Data.Sqlite', version: '9.0.0' },   // Microsoft.*, but NOT the runtime band
      { id: 'Dapper', version: '2.1.0' },
    ]);
    assert.deepEqual(band.map((p) => p.id), ['Microsoft.Extensions.DependencyInjection', 'Microsoft.Extensions.AI']);
    assert.deepEqual(outside.map((p) => p.id), ['Microsoft.Data.Sqlite', 'Dapper']);
  });

  it('reports an id nobody decided on, and says nothing about an allowed one', () => {
    const { unexpected, stale } = bundleBudget(
      [{ id: 'ModelContextProtocol.Core', version: '1.4.1' }, { id: 'Newtonsoft.Json', version: '13.0.3' }],
      ['ModelContextProtocol.Core']);
    assert.deepEqual(unexpected.map((p) => p.id), ['Newtonsoft.Json']);
    assert.deepEqual(stale, []);
  });

  it('reports a ROTTED allowance — the half that is easy to under-rate', () => {
    const { unexpected, stale } = bundleBudget([], ['ModelContextProtocol.Core']);
    assert.deepEqual(unexpected, []);
    assert.deepEqual(stale, ['ModelContextProtocol.Core'],
      'an allowance matching nothing is a standing permission for the id to come BACK undiscussed');
  });
});

describe('check-bundle — the gate, end to end over a fixture assets file', () => {
  it('passes a closure that is entirely band + allowlist, and shows what is outside', () => {
    const { code, out } = run(assets({
      'Microsoft.Extensions.AI/9.0.0': 'package',
      'ModelContextProtocol.Core/1.4.1': 'package',
      'Lyntai.Core/1.0.0': 'project',
    }), ['ModelContextProtocol.Core']);
    assert.equal(code, 0);
    assert.match(out, /2 third-party packages in the bundle closure \(1 on the Microsoft\.Extensions\.\* band/);
    assert.match(out, /outside the band: ModelContextProtocol\.Core 1\.4\.1/);
    assert.match(out, /bundle dependency budget respected ✓/);
  });

  it('FAILS on an undecided package, naming it and its version', () => {
    const { code, out } = run(assets({ 'Newtonsoft.Json/13.0.3': 'package' }), []);
    assert.equal(code, 1);
    assert.match(out, /1 package\(s\) nobody decided on/);
    assert.match(out, /Newtonsoft\.Json 13\.0\.3/);
    assert.match(out, /forces these on EVERY one-line-install consumer/);
    assert.match(out, /bundle\.allowedThirdParty/, 'and teaches where the decision is recorded');
  });

  it('FAILS on a stale allowlist entry — the branch that had never run', () => {
    const { code, out } = run(assets({ 'Microsoft.Extensions.AI/9.0.0': 'package' }), ['ModelContextProtocol.Core']);
    assert.equal(code, 1);
    assert.match(out, /allowlist is stale — no longer in the closure: ModelContextProtocol\.Core/);
    assert.match(out, /so the budget keeps meaning something/);
  });

  it('reports the UNDECIDED package first when both are wrong (the cost, before the tidy-up)', () => {
    const { code, out } = run(assets({ 'Newtonsoft.Json/13.0.3': 'package' }), ['ModelContextProtocol.Core']);
    assert.equal(code, 1);
    assert.match(out, /nobody decided on/);
    assert.doesNotMatch(out, /allowlist is stale/);
  });
});

describe('check-bundle — when the toolchain is unhappy', () => {
  it('FAILS when the restore fails, rather than reading a stale closure', () => {
    // The dangerous shape: `obj/project.assets.json` from an earlier restore is still on disk and still
    // parses. Passing on it would gate against a graph that no longer exists.
    const { code, out } = run(assets({ 'Newtonsoft.Json/13.0.3': 'package' }), [],
      { restore: () => ({ status: 1, stdout: 'error NU1101: package not found\n' }) });
    assert.equal(code, 1);
    assert.match(out, /restore failed — cannot read the dependency closure/);
    assert.match(out, /NU1101/, 'the restore\'s own output is surfaced');
    assert.doesNotMatch(out, /nobody decided on/, 'and the stale closure is NOT judged');
  });

  it('FAILS when there is no assets file at all', () => {
    const { code, out } = run(null, [], { writeAssets: false });
    assert.equal(code, 1);
    assert.match(out, /no project\.assets\.json at src\/Lyntai\.Bundle\/obj/);
  });

  it('skips, without failing, when no bundle is configured', () => {
    const log = recorder();
    assert.equal(checkBundle({ config: {}, log, error: log, restore: () => ({ status: 0 }) }), 0);
    assert.match(log.text(), /no bundle configured — skipped/);
  });
});

describe('check-bundle — the restore it runs', () => {
  it('restores the BUNDLE\'s own csproj, from the repo root', () => {
    // Pinned because the path is derived (`<project>/<basename>.csproj`) rather than configured: a wrong
    // derivation restores nothing and every branch above it then reads whatever is already on disk.
    let seen = null;
    restoreBundle('/repo', 'src/Lyntai.Bundle', (exe, argv, opts) => { seen = { exe, argv, opts }; return { status: 0 }; });
    assert.equal(seen.exe, 'dotnet');
    assert.equal(seen.argv[0], 'restore');
    assert.match(seen.argv[1].replaceAll('\\', '/'), /^src\/Lyntai\.Bundle\/Lyntai\.Bundle\.csproj$/);
    assert.equal(seen.opts.cwd, '/repo');
  });
});
