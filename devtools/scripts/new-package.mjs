// new-package — scaffold an adapter package and register it in EVERY registry that must know about it.
//
// The companion to check-packages (docs/DECISIONS.md D27): that gate tells you which of the nine registries you
// forgot, this removes the chore so there is nothing to forget. Many small packages is the intended shape here,
// so adding the N+1th must not be a nine-step ritual.
//
// What it deliberately does NOT do: add the package to the `Lyntai` bundle. Bundle membership is a budgeted
// decision under D26 — it forces the dependency on every one-line-install consumer — so it stays a human call,
// and check-bundle will fail if a dependency sneaks in without one.
//
// Split into a function over a repo ROOT plus a thin CLI wrapper 2026-08-11 (TASKS.md Part 62) so it can be
// tested against a fixture tree. It used to be one top-level script writing into this repository at import
// time — untestable by construction, and it is the one tool here whose failure mode is a HALF-registered
// package (see the note on `insert`). Nothing it writes changed in the move.
//
// Usage: node devtools/dev.mjs new-package Lyntai.Storage.Redis [--description "..."]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** A refusal with a message, so the caller reports and exits 1 rather than the module killing the process. */
class Bail extends Error {}

export const USAGE = 'usage: node devtools/dev.mjs new-package <Lyntai.Some.Package> [--description "what it gives you"]\n'
  + '  e.g. new-package Lyntai.Storage.Redis';

/** `Lyntai.Storage.Redis` — dotted PascalCase segments under the `Lyntai.` root, nothing else. */
export const VALID_ID = /^Lyntai\.[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$/;

/**
 * The seven registries this writes into, as `{ file, anchor, line(id, entryClass, description), label, where }`.
 *
 * Every anchor is an EXISTING package's row — `Lyntai.Secrets.Dpapi` for most of them — which is the one
 * fragility worth knowing about: retire that package and every anchor here dies at once, loudly (the run
 * refuses and prints the line to add by hand), never silently.
 */
export const REGISTRIES = [
  {
    file: 'Lyntai.slnx',
    anchor: '    <Project Path="src/Lyntai.Secrets.Dpapi/Lyntai.Secrets.Dpapi.csproj" />',
    line: (id) => `    <Project Path="src/${id}/${id}.csproj" />`,
    label: 'Lyntai.slnx',
  },
  {
    file: 'devtools/project.config.mjs',
    anchor: "    'src/Lyntai.Secrets.Dpapi',",
    line: (id) => `    'src/${id}',`,
    label: 'project.config.mjs → packableProjects',
  },
  {
    file: 'tests/Lyntai.Tests/Lyntai.Tests.csproj',
    anchor: '    <ProjectReference Include="..\\..\\src\\Lyntai.Secrets.Dpapi\\Lyntai.Secrets.Dpapi.csproj" />',
    line: (id) => `    <ProjectReference Include="..\\..\\src\\${id}\\${id}.csproj" />`,
    label: 'Lyntai.Tests.csproj → ProjectReference',
  },
  {
    file: 'tests/Lyntai.Tests/Api/ApiSurfaceTests.cs',
    anchor: '        "Lyntai.Secrets.Dpapi",',
    line: (id) => `        "${id}",`,
    label: 'ApiSurfaceTests.Assemblies()',
  },
  {
    file: 'tests/Lyntai.Tests/Api/ApiSurfaceTests.cs',
    anchor: '        ["Lyntai.Secrets.Dpapi"] = typeof(Lyntai.Secrets.DpapiSecretProtector).Assembly,',
    line: (id, entryClass) => `        ["${id}"] = typeof(Lyntai.${entryClass}).Assembly,`,
    label: 'ApiSurfaceTests.Loaded (anchored on the scaffolded entry point)',
  },
  {
    file: 'docs/AOT.md',
    anchor: '| `Lyntai` (the bundle) | n/a |',
    line: (id) => `| \`${id}\` | ✅ compatible | TODO: confirm — name the dependency this isolates, and opt OUT in the csproj `
      + 'if it uses reflection. |',
    label: 'docs/AOT.md table row',
    where: 'before',
  },
  {
    file: 'README.md',
    anchor: '| `Lyntai.Secrets.Dpapi` | Windows DPAPI + recovery-key envelope for the secret vault. |',
    line: (id, entryClass, description) => `| \`${id}\` | ${description ?? 'TODO: what it gives you.'} |`,
    label: 'README Packages table row',
  },
];

export const csprojFor = (id, blurb) => `<Project Sdk="Microsoft.NET.Sdk">

  <!--
    ${id} — an ADAPTER package: it may depend on Lyntai.Core (or a domain package), never on another adapter.
    Its reason to exist is the dependency it ISOLATES (docs/DECISIONS.md D25) — state that in the Description.

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
`;

export const entryClassFor = (id, short, entryClass) =>
  `// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>DI entry point for <c>${id}</c>. A consumer composes this adapter through the builder
/// (<c>services.AddLyntai(cfg =&gt; cfg.Add${short}(…))</c>) and never constructs its types by hand.</summary>
public static class ${entryClass}
{
    // TODO: the Add*/Use* method(s) that register this adapter's implementation into the DI collection its
    // contract lives in — a variation point is a registration, never a switch (.claude/rules/dotnet-package-layout.md).
}
`;

/** The scaffold. Returns a process exit code; every refusal is reported through `error` first. */
export function newPackage({ repo = repoDefault, id, description = null, log = console.log, error = console.error } = {}) {
  try {
    if (!id || !VALID_ID.test(id)) throw new Bail(USAGE);

    const dir = path.join(repo, 'src', id);
    if (fs.existsSync(dir)) throw new Bail(`already exists: src/${id} — nothing to scaffold`);

    const short = id.split('.').pop();                  // Redis
    const entryClass = `${short}BuilderExtensions`;     // the repo convention for an adapter's DI entry point
    const blurb = description ?? `TODO: what ${id} gives a consumer, and which dependency it isolates.`;

    // ---- the files -------------------------------------------------------------------------------------
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, `${id}.csproj`), csprojFor(id, blurb));
    fs.writeFileSync(path.join(dir, `${entryClass}.cs`), entryClassFor(id, short, entryClass));

    // ---- the registries --------------------------------------------------------------------------------
    const edits = [];

    // Insert `added` next to `anchor`. The idempotency guard checks for ADDED, never for the anchor: the first
    // version of this checked the replacement's first line, which IS the anchor, so every registry reported
    // "already present" and only one was actually written. Caught by scaffolding a throwaway package and reading
    // the report — a scaffolder that silently skips its own work is worse than one that fails.
    const insert = (file, anchor, added, label, where = 'after') => {
      const p = path.join(repo, file);
      const before = fs.readFileSync(p, 'utf8');
      if (before.includes(added.trim())) { edits.push(`${label} — already present, left alone`); return; }
      if (!before.includes(anchor)) throw new Bail(`could not find the insertion point in ${file} — add this by hand:\n  ${added}`);
      // Join with the line ending the FILE already uses. A bare `\n` spliced a lone LF into every registry,
      // all of which are CRLF in a Windows working copy under `core.autocrlf=true` — leaving a mixed file
      // that is committed clean here only because autocrlf normalises on the way in, and committed MIXED by
      // anyone whose config does not. No gate can see it: mixed endings are not mojibake, so check-encoding
      // is blind to them by design.
      const eol = before.includes('\r\n') ? '\r\n' : '\n';
      const line = added.replace(/\r?\n/g, eol);
      fs.writeFileSync(p, before.replace(anchor, where === 'after' ? `${anchor}${eol}${line}` : `${line}${eol}${anchor}`));
      edits.push(label);
    };

    for (const r of REGISTRIES) insert(r.file, r.anchor, r.line(id, entryClass, description), r.label, r.where);

    // ---- report ----------------------------------------------------------------------------------------
    log(`new-package: scaffolded src/${id}`);
    log(`  src/${id}/${id}.csproj`);
    log(`  src/${id}/${entryClass}.cs   (public — also the API-gate anchor type)`);
    for (const e of edits) log(`  registered: ${e}`);
    log('\nnext:');
    log('  1. write the adapter, and fill in the Description + the docs/AOT.md and README rows (marked TODO)');
    log('  2. `node devtools/dev.mjs test` once — it seeds the API baseline; review it before committing');
    log('  3. bundle membership is NOT automatic (D26): it forces the dependency on every one-line-install');
    log('     consumer, so add it to src/Lyntai.Bundle only if it clears that budget');
    log('  4. `node devtools/dev.mjs verify` — check-packages confirms every registry, check-warnings keeps');
    log('     the trim claim honest');
    return 0;
  } catch (e) {
    if (!(e instanceof Bail)) throw e;
    error(e.message);
    return 1;
  }
}

// CLI entry point — a thin wrapper, so importing this module for a test scaffolds nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const argv = process.argv.slice(2);
  const descFlag = argv.indexOf('--description');
  process.exitCode = newPackage({
    id: argv.find((a) => !a.startsWith('--')),
    description: descFlag >= 0 ? argv[descFlag + 1] : null,
  });
}
