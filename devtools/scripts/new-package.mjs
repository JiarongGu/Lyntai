// new-package — scaffold an adapter package and register it in EVERY registry that must know about it.
//
// The companion to check-packages (docs/DECISIONS.md D33): that gate tells you which of the nine registries you
// forgot, this removes the chore so there is nothing to forget. Many small packages is the intended shape here,
// so adding the N+1th must not be a nine-step ritual.
//
// What it deliberately does NOT do: add the package to the `Lyntai` bundle. Bundle membership is a budgeted
// decision under D32 — it forces the dependency on every one-line-install consumer — so it stays a human call,
// and check-bundle will fail if a dependency sneaks in without one.
//
// Usage: node devtools/dev.mjs new-package Lyntai.Storage.Redis [--description "..."]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const argv = process.argv.slice(2);
const id = argv.find((a) => !a.startsWith('--'));
const descFlag = argv.indexOf('--description');
const description = descFlag >= 0 ? argv[descFlag + 1] : null;

const die = (msg) => { console.error(msg); process.exit(1); };

if (!id || !/^Lyntai\.[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$/.test(id))
  die('usage: node devtools/dev.mjs new-package <Lyntai.Some.Package> [--description "what it gives you"]\n' +
      '  e.g. new-package Lyntai.Storage.Redis');

const dir = path.join(repo, 'src', id);
if (fs.existsSync(dir)) die(`already exists: src/${id} — nothing to scaffold`);

const short = id.split('.').pop();                  // Redis
const entryClass = `${short}BuilderExtensions`;     // the repo convention for an adapter's DI entry point
const blurb = description ?? `TODO: what ${id} gives a consumer, and which dependency it isolates.`;

// ---- the files -----------------------------------------------------------------------------------------
fs.mkdirSync(dir, { recursive: true });

fs.writeFileSync(path.join(dir, `${id}.csproj`), `<Project Sdk="Microsoft.NET.Sdk">

  <!--
    ${id} — an ADAPTER package: it may depend on Lyntai.Core (or a domain package), never on another adapter.
    Its reason to exist is the dependency it ISOLATES (docs/DECISIONS.md D31) — state that in the Description.

    If this package drags a reflection-heavy dependency (an ORM, a serializer, a native runtime), it must OPT
    OUT of the trim/AOT claim rather than inherit it — a false IsTrimmable tells a consumer's trimmer this code
    survives trimming when it does not:
      <IsAotCompatible>false</IsAotCompatible>
      <IsTrimmable>false</IsTrimmable>
      <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    Then change its row in docs/AOT.md to match. \`verify\` fails on any warning here, which is what keeps that
    claim honest.
  -->
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <PackageId>${id}</PackageId>
    <Description>${blurb}</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\\Lyntai.Core\\Lyntai.Core.csproj" />
  </ItemGroup>

  <!-- Every package here grants this: internals are implementation detail, and their tests reach them.
       Omitting it breaks the build the moment a moved/new type has an internal member under test — which is
       exactly how this line came to be in the template. -->
  <ItemGroup>
    <InternalsVisibleTo Include="Lyntai.Tests" />
  </ItemGroup>

</Project>
`);

fs.writeFileSync(path.join(dir, `${entryClass}.cs`), `// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>DI entry point for <c>${id}</c>. A consumer composes this adapter through the builder
/// (<c>services.AddLyntai(cfg =&gt; cfg.Add${short}(…))</c>) and never constructs its types by hand.</summary>
public static class ${entryClass}
{
    // TODO: the Add*/Use* method(s) that register this adapter's implementation into the DI collection its
    // contract lives in — a variation point is a registration, never a switch (.claude/rules/dotnet-package-layout.md).
}
`);

// ---- the registries ------------------------------------------------------------------------------------
const edits = [];

// Insert `added` next to `anchor`. The idempotency guard checks for ADDED, never for the anchor: the first
// version of this checked the replacement's first line, which IS the anchor, so every registry reported
// "already present" and only one was actually written. Caught by scaffolding a throwaway package and reading
// the report — a scaffolder that silently skips its own work is worse than one that fails.
const insert = (file, anchor, added, label, where = 'after') => {
  const p = path.join(repo, file);
  const before = fs.readFileSync(p, 'utf8');
  if (before.includes(added.trim())) { edits.push(`${label} — already present, left alone`); return; }
  if (!before.includes(anchor)) die(`could not find the insertion point in ${file} — add this by hand:\n  ${added}`);
  fs.writeFileSync(p, before.replace(anchor, where === 'after' ? `${anchor}\n${added}` : `${added}\n${anchor}`));
  edits.push(label);
};

insert('Lyntai.slnx', '    <Project Path="src/Lyntai.Secrets.Dpapi/Lyntai.Secrets.Dpapi.csproj" />',
  `    <Project Path="src/${id}/${id}.csproj" />`, 'Lyntai.slnx');

insert('devtools/project.config.mjs', "    'src/Lyntai.Secrets.Dpapi',",
  `    'src/${id}',`, 'project.config.mjs → packableProjects');

insert('tests/Lyntai.Tests/Lyntai.Tests.csproj',
  '    <ProjectReference Include="..\\..\\src\\Lyntai.Secrets.Dpapi\\Lyntai.Secrets.Dpapi.csproj" />',
  `    <ProjectReference Include="..\\..\\src\\${id}\\${id}.csproj" />`,
  'Lyntai.Tests.csproj → ProjectReference');

insert('tests/Lyntai.Tests/Api/ApiSurfaceTests.cs', '        "Lyntai.Secrets.Dpapi",',
  `        "${id}",`, 'ApiSurfaceTests.Assemblies()');

insert('tests/Lyntai.Tests/Api/ApiSurfaceTests.cs',
  '        ["Lyntai.Secrets.Dpapi"] = typeof(Lyntai.Secrets.DpapiSecretProtector).Assembly,',
  `        ["${id}"] = typeof(Lyntai.${entryClass}).Assembly,`,
  'ApiSurfaceTests.Loaded (anchored on the scaffolded entry point)');

insert('docs/AOT.md', '| `Lyntai` (the bundle) | n/a |',
  `| \`${id}\` | ✅ compatible | TODO: confirm — name the dependency this isolates, and opt OUT in the csproj ` +
  `if it uses reflection. |`, 'docs/AOT.md table row', 'before');

insert('README.md', '| `Lyntai.Secrets.Dpapi` | Windows DPAPI + recovery-key envelope for the secret vault. |',
  `| \`${id}\` | ${description ?? 'TODO: what it gives you.'} |`, 'README Packages table row');

// ---- report --------------------------------------------------------------------------------------------
console.log(`new-package: scaffolded src/${id}`);
console.log(`  src/${id}/${id}.csproj`);
console.log(`  src/${id}/${entryClass}.cs   (public — also the API-gate anchor type)`);
for (const e of edits) console.log(`  registered: ${e}`);
console.log('\nnext:');
console.log(`  1. write the adapter, and fill in the Description + the docs/AOT.md and README rows (marked TODO)`);
console.log('  2. `node devtools/dev.mjs test` once — it seeds the API baseline; review it before committing');
console.log('  3. bundle membership is NOT automatic (D32): it forces the dependency on every one-line-install');
console.log('     consumer, so add it to src/Lyntai.Bundle only if it clears that budget');
console.log('  4. `node devtools/dev.mjs verify` — check-packages confirms every registry, check-warnings keeps');
console.log('     the trim claim honest');
