// check-packages — the package INVENTORY gate. See devtools/scripts/check-packages.mjs.
//
// Its own docstring states the case for testing it: shipping a package means registering it in nine places
// and EVERY miss is silent. A package absent from `ApiSurfaceTests.Assemblies()` has no API gate at all —
// the protection that makes the SemVer promise real — and nothing says so. So each test below removes one
// registration from an otherwise-complete fixture repository and asserts the gate NOTICES.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { checkPackages, inventory, packableProjects } from '../check-packages.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const csproj = ({ id, assembly = id, packable = true, description = true, buildOutput = true }) => `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>${packable}</IsPackable>
    <PackageId>${id}</PackageId>
    <AssemblyName>${assembly}</AssemblyName>
${buildOutput ? '' : '    <IncludeBuildOutput>false</IncludeBuildOutput>\n'}${description ? `    <Description>What ${id} gives you.</Description>\n` : ''}  </PropertyGroup>
</Project>
`;

/** A repository where `Lyntai.Core` (shipping) and `Lyntai.Bundle` (references-only) are fully registered. */
function completeTree() {
  return {
    'src/Lyntai.Core/Lyntai.Core.csproj': csproj({ id: 'Lyntai.Core' }),
    'src/Lyntai.Bundle/Lyntai.Bundle.csproj': csproj({ id: 'Lyntai.Bundle', buildOutput: false }),
    'src/Lyntai.Internal/Lyntai.Internal.csproj': csproj({ id: 'Lyntai.Internal', packable: false }),
    'devtools/project.config.mjs': "export default { packableProjects: ['src/Lyntai.Core', 'src/Lyntai.Bundle'] };\n",
    'Lyntai.slnx': '<Solution>\n  <Project Path="src/Lyntai.Core/Lyntai.Core.csproj" />\n'
      + '  <Project Path="src/Lyntai.Bundle/Lyntai.Bundle.csproj" />\n</Solution>\n',
    'tests/Lyntai.Tests/Api/ApiSurfaceTests.cs': [
      'public static string[] Assemblies() =>',
      '[',
      '    "Lyntai.Core",',
      '];',
      '',
      'private static readonly Dictionary<string, Assembly> Loaded = new()',
      '{',
      '    ["Lyntai.Core"] = typeof(LyntaiBuilder).Assembly,',
      '};',
      '',
    ].join('\n'),
    'tests/Lyntai.Tests/Lyntai.Tests.csproj':
      '<Project>\n  <ItemGroup>\n    <ProjectReference Include="../../src/Lyntai.Core/Lyntai.Core.csproj" />\n'
      + '  </ItemGroup>\n</Project>\n',
    'tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt': 'public class Lyntai.LyntaiBuilder\n',
    'docs/AOT.md': '| Package | Status |\n|---|---|\n| `Lyntai.Core` | trim-safe |\n',
    'README.md': '## Packages\n\n| Package | What it gives you |\n|---|---|\n| `Lyntai.Core` | the contracts |\n',
  };
}

/** Apply `mutate` to a complete tree, then run the inventory over it. */
function run(mutate = () => {}) {
  const files = completeTree();
  mutate(files);
  const dir = makeTree(files);
  try {
    return inventory(dir);
  } finally {
    removeTree(dir);
  }
}

const problemText = (r) => r.problems.map((p) => `${p.what} → ${p.fix}`).join('\n');

describe('check-packages — the baseline', () => {
  it('a fully registered repository is clean', () => {
    const r = run();
    assert.deepEqual(problemText(r), '', 'the fixture must be complete, or every test below proves nothing');
    assert.equal(r.projects.length, 2, 'the non-packable project is not a package');
  });

  it('reads the filesystem as the source of truth, honouring PackageId/AssemblyName/IncludeBuildOutput', () => {
    const files = completeTree();
    files['src/Lyntai.Renamed/Lyntai.Renamed.csproj'] = csproj({ id: 'Lyntai.Public', assembly: 'Lyntai.Asm' });
    const dir = makeTree(files);
    try {
      const projects = packableProjects(dir);
      const renamed = projects.find((p) => p.project === 'Lyntai.Renamed');
      assert.equal(renamed.id, 'Lyntai.Public');
      assert.equal(renamed.assembly, 'Lyntai.Asm');
      assert.equal(projects.find((p) => p.id === 'Lyntai.Bundle').shipsAssembly, false);
    } finally {
      removeTree(dir);
    }
  });
});

describe('check-packages — a package missing from one registry is DETECTED', () => {
  it('the API surface gate — the miss that leaves a package with no gate at all', () => {
    const r = run((f) => {
      f['tests/Lyntai.Tests/Api/ApiSurfaceTests.cs'] =
        f['tests/Lyntai.Tests/Api/ApiSurfaceTests.cs'].replace('    "Lyntai.Core",\n', '');
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /MISSING FROM THE API SURFACE GATE/);
  });

  it('the Loaded anchor map, checked SEPARATELY from Assemblies() (a whole-file search passes vacuously)', () => {
    const r = run((f) => {
      f['tests/Lyntai.Tests/Api/ApiSurfaceTests.cs'] = f['tests/Lyntai.Tests/Api/ApiSurfaceTests.cs']
        .replace('    ["Lyntai.Core"] = typeof(LyntaiBuilder).Assembly,\n', '');
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /no anchor type in ApiSurfaceTests\.Loaded/);
  });

  it('packableProjects — otherwise the package is never packed', () => {
    const r = run((f) => {
      f['devtools/project.config.mjs'] = "export default { packableProjects: ['src/Lyntai.Bundle'] };\n";
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /Lyntai\.Core: not in packableProjects/);
  });

  it('the solution', () => {
    const r = run((f) => {
      f['Lyntai.slnx'] = f['Lyntai.slnx'].replace(/.*Lyntai\.Core\.csproj.*\n/, '');
    });
    assert.match(problemText(r), /Lyntai\.Core: not in the solution/);
  });

  it('the API baseline file', () => {
    const r = run((f) => { delete f['tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt']; });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /no API baseline/);
  });

  it('the test project reference — without it the assembly never loads to be checked', () => {
    const r = run((f) => {
      f['tests/Lyntai.Tests/Lyntai.Tests.csproj'] = '<Project>\n  <ItemGroup>\n  </ItemGroup>\n</Project>\n';
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /the test project does not reference it/);
  });

  it('the <Description> — the package blurb on nuget.org', () => {
    const r = run((f) => {
      f['src/Lyntai.Core/Lyntai.Core.csproj'] = csproj({ id: 'Lyntai.Core', description: false });
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /no <Description>/);
  });

  it('the docs tables — and a PROSE mention does not count as a row', () => {
    const r = run((f) => {
      f['docs/AOT.md'] = '| Package | Status |\n|---|---|\n';
      f['README.md'] = 'Install `Lyntai.Core` first.\n\n| Package | What |\n|---|---|\n';
    });
    assert.equal(r.problems.length, 2);
    assert.match(problemText(r), /no row in the docs\/AOT\.md table/);
    assert.match(problemText(r), /no row in the README Packages table/);
  });

  it('a references-only package is exempt from the assembly-shaped registries, not from the others', () => {
    const clean = run();
    assert.deepEqual(clean.problems, [], 'Lyntai.Bundle has no baseline or API entry and that is correct');
    const r = run((f) => {
      f['Lyntai.slnx'] = f['Lyntai.slnx'].replace(/.*Lyntai\.Bundle\.csproj.*\n/, '');
    });
    assert.match(problemText(r), /Lyntai\.Bundle: not in the solution/);
  });
});

describe('check-packages — the reverse direction: a deleted package leaves nothing stale', () => {
  it('packableProjects naming a project that is not packable', () => {
    const r = run((f) => {
      f['devtools/project.config.mjs'] = "export default { packableProjects: ['src/Lyntai.Core', "
        + "'src/Lyntai.Bundle', 'src/Lyntai.Gone'] };\n";
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /packableProjects names src\/Lyntai\.Gone/);
  });

  it('the API gate listing an assembly nothing ships', () => {
    const r = run((f) => {
      f['tests/Lyntai.Tests/Api/ApiSurfaceTests.cs'] = f['tests/Lyntai.Tests/Api/ApiSurfaceTests.cs']
        .replace('    "Lyntai.Core",\n', '    "Lyntai.Core",\n    "Lyntai.Gone",\n');
    });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /gates "Lyntai\.Gone", which ships no assembly/);
  });

  it('an orphan baseline, which nothing checks any more', () => {
    const r = run((f) => { f['tests/Lyntai.Tests/Api/Baselines/Lyntai.Gone.txt'] = 'stale\n'; });
    assert.equal(r.problems.length, 1);
    assert.match(problemText(r), /orphan baseline Lyntai\.Gone\.txt/);
  });
});

describe('check-packages — the exit code and the report', () => {
  it('returns 1 and prints every problem with its fix', () => {
    const files = completeTree();
    delete files['tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt'];
    const dir = makeTree(files);
    const log = recorder(); const err = recorder();
    try {
      assert.equal(checkPackages(dir, log, err), 1);
      assert.match(log.text(), /2 packable projects \(1 shipping an assembly, 1 references-only\)/);
      assert.match(err.text(), /1 inventory problem\(s\)/);
      assert.match(err.text(), /→ run the tests once/);
    } finally {
      removeTree(dir);
    }
  });

  it('returns 0 on a clean inventory', () => {
    const dir = makeTree(completeTree());
    const log = recorder();
    try {
      assert.equal(checkPackages(dir, log, log), 0);
      assert.match(log.text(), /every package is registered everywhere it needs to be/);
    } finally {
      removeTree(dir);
    }
  });

  it('refuses to report success when it found no packable projects at all', () => {
    const dir = makeTree({ 'src/.keep': '' });
    const log = recorder(); const err = recorder();
    try {
      assert.equal(checkPackages(dir, log, err), 1, 'an empty scan is a broken scan, not a clean one');
      assert.match(err.text(), /found no packable projects/);
    } finally {
      removeTree(dir);
    }
  });
});

describe('check-packages — the packableProjects check must read that ARRAY, not the whole file', () => {
  it('a project named only as bundle.project is NOT registered as packable', () => {
    // The same vacuous-match defect this gate already learned for the two ApiSurfaceTests registries, in the
    // one registry that never got the treatment. `devtools/project.config.mjs` names `src/Lyntai.Bundle`
    // TWICE — once in `packableProjects` and once as `bundle.project` (D26's budget) — so a whole-file
    // `includes` finds the second and passes even with the first deleted. Measured against the real config
    // 2026-08-15: Lyntai.Bundle was the one package whose packable registration was unprotected.
    const r = run((f) => {
      f['devtools/project.config.mjs'] =
        "export default { packableProjects: ['src/Lyntai.Core'],\n"
        + "  bundle: { project: 'src/Lyntai.Bundle' } };\n";
    });
    assert.match(problemText(r), /Lyntai\.Bundle: not in packableProjects/);
  });
});
