// new-package — the scaffolder that registers a package in every registry. See scripts/new-package.mjs.
//
// Why this one is worth testing even though it is not a gate: its failure mode is a HALF-registered package,
// and that is not hypothetical. The first version's idempotency guard checked for the ANCHOR rather than for
// the line being added — and the anchor is by definition already there — so every registry reported "already
// present, left alone" and exactly one was really written. It was caught by scaffolding a throwaway package
// and reading the report, which is precisely the check nobody runs twice. check-packages would eventually
// catch the miss, but only on the next `verify`, after the author has moved on.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { REGISTRIES, newPackage } from '../new-package.mjs';
import { makeTree, recorder, removeTree, writeInto } from './_fixtures.mjs';

/**
 * A fixture repository holding just the seven anchors, each in a plausible neighbourhood so an insertion
 * that lands in the wrong place is visible.
 */
const FIXTURE = {
  'Lyntai.slnx': '<Solution>\n  <Folder Name="/src/">\n'
    + '    <Project Path="src/Lyntai.Core/Lyntai.Core.csproj" />\n'
    + '    <Project Path="src/Lyntai.Secrets.Dpapi/Lyntai.Secrets.Dpapi.csproj" />\n  </Folder>\n</Solution>\n',
  'devtools/project.config.mjs': "export default {\n  packableProjects: [\n    'src/Lyntai.Core',\n"
    + "    'src/Lyntai.Secrets.Dpapi',\n  ],\n};\n",
  'tests/Lyntai.Tests/Lyntai.Tests.csproj': '<Project>\n  <ItemGroup>\n'
    + '    <ProjectReference Include="..\\..\\src\\Lyntai.Secrets.Dpapi\\Lyntai.Secrets.Dpapi.csproj" />\n'
    + '  </ItemGroup>\n</Project>\n',
  'tests/Lyntai.Tests/Api/ApiSurfaceTests.cs': 'static string[] Assemblies() =>\n    [\n'
    + '        "Lyntai.Core",\n        "Lyntai.Secrets.Dpapi",\n    ];\n\n'
    + '    static Dictionary<string, Assembly> Loaded() => new()\n    {\n'
    + '        ["Lyntai.Secrets.Dpapi"] = typeof(Lyntai.Secrets.DpapiSecretProtector).Assembly,\n    };\n',
  'docs/AOT.md': '| Package | AOT | Notes |\n|---|---|---|\n'
    + '| `Lyntai.Core` | ✅ compatible | |\n| `Lyntai` (the bundle) | n/a |\n',
  'README.md': '| Package | What it gives you |\n|---|---|\n'
    + '| `Lyntai.Secrets.Dpapi` | Windows DPAPI + recovery-key envelope for the secret vault. |\n',
};

function scaffold(id, { description = null, files = FIXTURE } = {}) {
  const dir = makeTree(files);
  const log = recorder();
  const code = newPackage({ repo: dir, id, description, log, error: log });
  const read = (f) => readFileSync(join(dir, f), 'utf8');
  return { dir, code, out: log.text(), read, cleanup: () => removeTree(dir) };
}

describe('new-package — the files it writes', () => {
  it('scaffolds the csproj and the DI entry point, named by convention', (t) => {
    const s = scaffold('Lyntai.Storage.Redis', { description: 'Redis-backed stores.' });
    t.after(s.cleanup);
    assert.equal(s.code, 0);

    const csproj = s.read('src/Lyntai.Storage.Redis/Lyntai.Storage.Redis.csproj');
    assert.match(csproj, /<PackageId>Lyntai\.Storage\.Redis<\/PackageId>/);
    assert.match(csproj, /<Description>Redis-backed stores\.<\/Description>/);
    assert.match(csproj, /ProjectReference Include="\.\.\\Lyntai\.Core\\Lyntai\.Core\.csproj"/,
      'an adapter references Core and never another adapter');
    assert.match(csproj, /<InternalsVisibleTo Include="Lyntai\.Tests" \/>/);

    const entry = s.read('src/Lyntai.Storage.Redis/RedisBuilderExtensions.cs');
    assert.match(entry, /^namespace Lyntai;$/m, 'the Add*/Use* methods must land on the builder\'s namespace');
    assert.match(entry, /public static class RedisBuilderExtensions/);
  });

  it('writes a TODO description when none is given, rather than an empty element', (t) => {
    const s = scaffold('Lyntai.Storage.Redis');
    t.after(s.cleanup);
    assert.match(s.read('src/Lyntai.Storage.Redis/Lyntai.Storage.Redis.csproj'),
      /<Description>TODO: what Lyntai\.Storage\.Redis gives a consumer, and which dependency it isolates\.</);
  });
});

describe('new-package — the seven registries', () => {
  it('registers in EVERY one, and reports each (the half-registered failure this exists to prevent)', (t) => {
    const s = scaffold('Lyntai.Storage.Redis', { description: 'Redis-backed stores.' });
    t.after(s.cleanup);

    assert.match(s.read('Lyntai.slnx'), /<Project Path="src\/Lyntai\.Storage\.Redis\/Lyntai\.Storage\.Redis\.csproj" \/>/);
    assert.match(s.read('devtools/project.config.mjs'), /'src\/Lyntai\.Storage\.Redis',/);
    assert.match(s.read('tests/Lyntai.Tests/Lyntai.Tests.csproj'),
      /Include="\.\.\\\.\.\\src\\Lyntai\.Storage\.Redis\\Lyntai\.Storage\.Redis\.csproj"/);

    const api = s.read('tests/Lyntai.Tests/Api/ApiSurfaceTests.cs');
    assert.match(api, /^ {8}"Lyntai\.Storage\.Redis",$/m, 'the assembly LIST');
    assert.match(api, /\["Lyntai\.Storage\.Redis"\] = typeof\(Lyntai\.RedisBuilderExtensions\)\.Assembly,/,
      'and the anchor MAP — a package in one but not the other has no API gate at all');

    assert.match(s.read('docs/AOT.md'), /^\| `Lyntai\.Storage\.Redis` \| ✅ compatible \| TODO: confirm/m);
    assert.match(s.read('README.md'), /^\| `Lyntai\.Storage\.Redis` \| Redis-backed stores\. \|$/m);

    // Both writes into ApiSurfaceTests.cs must survive: the second `insert` re-reads what the first wrote.
    for (const r of REGISTRIES) assert.match(s.out, new RegExp(`registered: ${r.label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`));
  });

  it('inserts the AOT row BEFORE its anchor and everything else AFTER — the tables differ', (t) => {
    const s = scaffold('Lyntai.Storage.Redis');
    t.after(s.cleanup);
    const aot = s.read('docs/AOT.md').split('\n');
    assert.ok(aot.indexOf('| `Lyntai.Storage.Redis` | ✅ compatible | TODO: confirm — name the dependency this '
      + 'isolates, and opt OUT in the csproj if it uses reflection. |')
      < aot.indexOf('| `Lyntai` (the bundle) | n/a |'), 'the bundle row stays last');

    const slnx = s.read('Lyntai.slnx').split('\n');
    assert.ok(slnx.findIndex((l) => l.includes('Lyntai.Secrets.Dpapi'))
      < slnx.findIndex((l) => l.includes('Lyntai.Storage.Redis')));
  });

  it('leaves an ALREADY-registered line alone and still writes the other six', (t) => {
    // The exact defect: a guard keyed on the anchor rather than on the added line reported every registry as
    // "already present" and wrote one. Pre-seeding a single registry proves the guard reads the right thing.
    const seeded = { ...FIXTURE, 'README.md': `${FIXTURE['README.md']}| \`Lyntai.Storage.Redis\` | Already here. |\n` };
    const s = scaffold('Lyntai.Storage.Redis', { description: 'Already here.', files: seeded });
    t.after(s.cleanup);

    assert.match(s.out, /README Packages table row — already present, left alone/);
    assert.equal((s.read('README.md').match(/Lyntai\.Storage\.Redis/g) ?? []).length, 1, 'not duplicated');
    assert.match(s.out, /registered: Lyntai\.slnx/, 'and the other registries were still written');
    assert.match(s.read('devtools/project.config.mjs'), /'src\/Lyntai\.Storage\.Redis',/);
  });
});

describe('new-package — the refusals', () => {
  const invalid = ['', 'Redis', 'lyntai.redis', 'Lyntai.', 'Lyntai.9Redis', 'Lyntai.Storage-Redis'];

  it('refuses an id that is not a dotted Lyntai.* name, and prints the usage', (t) => {
    for (const id of invalid) {
      const s = scaffold(id);
      t.after(s.cleanup);
      assert.equal(s.code, 1, `${JSON.stringify(id)} must be refused`);
      assert.match(s.out, /usage: node devtools\/dev\.mjs new-package/);
    }
  });

  it('refuses to overwrite an existing package directory', (t) => {
    const s = scaffold('Lyntai.Storage.Redis',
      { files: { ...FIXTURE, 'src/Lyntai.Storage.Redis/Lyntai.Storage.Redis.csproj': '<Project/>\n' } });
    t.after(s.cleanup);
    assert.equal(s.code, 1);
    assert.match(s.out, /already exists: src\/Lyntai\.Storage\.Redis — nothing to scaffold/);
    assert.equal(s.read('src/Lyntai.Storage.Redis/Lyntai.Storage.Redis.csproj'), '<Project/>\n', 'untouched');
  });

  it('refuses LOUDLY when an anchor has gone, printing the line to add by hand', (t) => {
    // The known fragility: every anchor is an existing package's row. Retiring that package breaks all of
    // them at once — which is fine BECAUSE it fails this way rather than silently skipping the registry.
    const s = scaffold('Lyntai.Storage.Redis',
      { files: { ...FIXTURE, 'docs/AOT.md': '| Package | AOT |\n|---|---|\n| `Lyntai.Core` | ✅ |\n' } });
    t.after(s.cleanup);
    assert.equal(s.code, 1);
    assert.match(s.out, /could not find the insertion point in docs\/AOT\.md — add this by hand:/);
    assert.match(s.out, /\| `Lyntai\.Storage\.Redis` \| ✅ compatible \|/);
  });

  it('KNOWN LIMIT: a missing anchor leaves the earlier registries written and the src/ dir on disk', (t) => {
    // Pinned as it BEHAVES: there is no transaction here. The refusal above is the recovery instruction, and
    // check-packages reports whatever is left half-done — but a reader should know the tree is dirty rather
    // than assume the run rolled back. Recorded 2026-08-11.
    const s = scaffold('Lyntai.Storage.Redis',
      { files: { ...FIXTURE, 'README.md': '| Package | What it gives you |\n|---|---|\n' } });
    t.after(s.cleanup);
    assert.equal(s.code, 1);
    assert.match(s.read('Lyntai.slnx'), /Lyntai\.Storage\.Redis/, 'the earlier registries are already written');
    assert.match(s.read('src/Lyntai.Storage.Redis/Lyntai.Storage.Redis.csproj'), /<PackageId>/);
  });
});

describe('new-package — an insertion must match the file it edits', () => {
  it('splices CRLF into a CRLF registry, not a lone LF', () => {
    // On Windows with `core.autocrlf=true` — this repository's setup — every tracked registry is CRLF in the
    // working copy, while the scaffolder joined with a bare `\n`. That leaves a MIXED file: valid, committed
    // clean here only because autocrlf normalises on the way in, and committed mixed by anyone whose config
    // does not. `check-encoding` cannot see it either, since mixed endings are not mojibake. Found
    // 2026-08-15, after the same splice landed by hand in this session's own edits.
    const crlf = Object.fromEntries(Object.entries(FIXTURE).map(([f, t]) => [f, t.replace(/\n/g, '\r\n')]));
    const s = scaffold('Lyntai.Storage.Redis', { files: crlf });
    try {
      assert.equal(s.code, 0, s.out);
      for (const f of Object.keys(crlf)) {
        const text = s.read(f);
        const lone = (text.match(/(?<!\r)\n/g) ?? []).length;
        assert.equal(lone, 0, `${f} gained ${lone} lone-LF line ending(s)`);
      }
    } finally { s.cleanup(); }
  });

  it('and leaves an LF registry as LF', () => {
    // The other direction: matching the file means matching it either way, never normalising to CRLF.
    const s = scaffold('Lyntai.Storage.Redis');
    try {
      assert.equal(s.code, 0, s.out);
      for (const f of Object.keys(FIXTURE))
        assert.equal((s.read(f).match(/\r\n/g) ?? []).length, 0, `${f} gained CRLF`);
    } finally { s.cleanup(); }
  });
});
