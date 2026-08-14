// check-packages — the package INVENTORY is consistent across every registry that must know about it.
//
// Why this exists: shipping a package means registering it in nine places, and the failure modes are silent.
// Miss the ApiSurfaceTests entry and the package ships with NO public-API gate — the one protection that makes
// SemVer claims real — and nothing tells you. Miss a docs row and the docs lie about what you publish; that
// already happened (the docs pointed at `Lyntai.Generation.Http` for two releases after it was folded away, an
// install instruction that simply fails to restore).
//
// The FILESYSTEM is the source of truth: every src/* project with <IsPackable>true</IsPackable> is a package.
// Everything else must agree with it. A package that ships no assembly (IncludeBuildOutput=false — the bundle)
// is exempt from the assembly-shaped checks, since there is nothing to baseline or load.
//
// Deliberately presence-only, in both directions between the machine-readable registries. It does NOT scan prose
// for stale names: namespaces legitimately outlive the package ids they were named for (D25 forbids renaming a
// namespace when packages merge), so `Lyntai.Providers.ClaudeCli` appearing in text is correct, not drift.
//
// `inventory()` takes the repo root, so devtools/scripts/__tests__ can point it at a fixture tree and prove a
// missing registry entry is actually DETECTED — this gate's own docstring says the misses are silent, which is
// exactly the shape of thing that has to be tested rather than run (TASKS.md Part 60).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

// The two ApiSurfaceTests registries must be checked SEPARATELY: an assembly listed in Assemblies() but absent
// from Loaded throws, and one in Loaded but absent from Assemblies() is never gated at all. Searching the whole
// file for the name finds it in the other registry and passes vacuously — which this gate did on its first run,
// caught only by deliberately breaking it.
const between = (text, startRe, endToken) => {
  const m = text.match(startRe);
  if (!m) return '';
  const from = m.index + m[0].length;
  const to = text.indexOf(endToken, from);
  return to < 0 ? text.slice(from) : text.slice(from, to);
};

// A docs mention must be a TABLE ROW, not any prose occurrence — both docs describe the package set as a table,
// and every package is named in prose somewhere else (so a prose match also passes vacuously).
const hasTableRow = (doc, id) =>
  new RegExp(`^\\|.*\`${id.replace(/\./g, '\\.')}\``, 'm').test(doc);

/** The packable projects under `src/` — the source of truth every registry below must agree with. */
export function packableProjects(repoRoot) {
  return fs.readdirSync(path.join(repoRoot, 'src'), { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => {
      const dir = `src/${e.name}`;
      const csproj = path.join(repoRoot, dir, `${e.name}.csproj`);
      if (!fs.existsSync(csproj)) return null;
      const xml = fs.readFileSync(csproj, 'utf8');
      if (!/<IsPackable>\s*true\s*<\/IsPackable>/i.test(xml)) return null;
      return {
        dir,
        project: e.name,
        id: (xml.match(/<PackageId>([^<]+)<\/PackageId>/) || [])[1] || e.name,
        assembly: (xml.match(/<AssemblyName>([^<]+)<\/AssemblyName>/) || [])[1] || e.name,
        shipsAssembly: !/<IncludeBuildOutput>\s*false\s*<\/IncludeBuildOutput>/i.test(xml),
        hasDescription: /<Description>[^<]+<\/Description>/.test(xml),
      };
    })
    .filter(Boolean);
}

/** Every registry cross-check, over one repo root. `projects` empty is itself the (fatal) first problem. */
export function inventory(repoRoot) {
  const read = (p) => fs.readFileSync(path.join(repoRoot, p), 'utf8');
  const problems = [];
  const fail = (what, fix) => problems.push({ what, fix });

  const projects = packableProjects(repoRoot);
  if (!projects.length) return { projects, problems, empty: true };

  // ---- the registries that must agree ----------------------------------------------------------------
  const config = read('devtools/project.config.mjs');
  const solution = read('Lyntai.slnx');
  const apiTests = read('tests/Lyntai.Tests/Api/ApiSurfaceTests.cs');
  const testCsproj = read('tests/Lyntai.Tests/Lyntai.Tests.csproj');
  const aotDoc = read('docs/AOT.md');
  const readme = read('README.md');
  const baselineDir = 'tests/Lyntai.Tests/Api/Baselines';

  const gatedList = between(apiTests, /Assemblies\(\)\s*=>/, '];');
  const loadedMap = between(apiTests, /Loaded\s*=\s*new\(\)/, '};');

  for (const p of projects) {
    // --- every packable project is packed and built ---
    if (!config.includes(`'${p.dir}'`))
      fail(`${p.id}: not in packableProjects`, `add '${p.dir}' to devtools/project.config.mjs → packableProjects`);
    if (!solution.includes(`${p.dir}/${p.project}.csproj`))
      fail(`${p.id}: not in the solution`, `add src/${p.project}/${p.project}.csproj to Lyntai.slnx`);
    if (!p.hasDescription)
      fail(`${p.id}: no <Description>`, `add one — it is the package's blurb on nuget.org`);

    if (!p.shipsAssembly) continue;   // a references-only bundle has no surface to gate or document per-assembly

    // --- the API gate: the protection that makes the SemVer claim real ---
    const quoted = `"${p.assembly}"`;
    if (!gatedList.includes(quoted))
      fail(`${p.id}: MISSING FROM THE API SURFACE GATE`,
        `add ${quoted} to ApiSurfaceTests.Assemblies() — until then this package's public API is unprotected ` +
        `and a breaking change ships silently`);
    if (!loadedMap.includes(`[${quoted}]`))
      fail(`${p.id}: no anchor type in ApiSurfaceTests.Loaded`,
        `add [${quoted}] = typeof(SomePublicType).Assembly — the gate cannot load the assembly without it`);
    if (!fs.existsSync(path.join(repoRoot, baselineDir, `${p.assembly}.txt`)))
      fail(`${p.id}: no API baseline`, `run the tests once — it seeds ${baselineDir}/${p.assembly}.txt — then review it`);
    if (!testCsproj.includes(`${p.project}.csproj`))
      fail(`${p.id}: the test project does not reference it`,
        `add a ProjectReference in tests/Lyntai.Tests/Lyntai.Tests.csproj, or the assembly never loads to be checked`);

    // --- the two docs that describe the CURRENT package set ---
    if (!hasTableRow(aotDoc, p.id))
      fail(`${p.id}: no row in the docs/AOT.md table`,
        `add its trim/AOT status — consumers read that table before trusting the claim`);
    if (!hasTableRow(readme, p.id))
      fail(`${p.id}: no row in the README Packages table`, `add a row saying what it gives you`);
  }

  // ---- reverse: registries must not name packages that no longer exist -------------------------------
  const assemblies = new Set(projects.filter((p) => p.shipsAssembly).map((p) => p.assembly));

  for (const m of config.matchAll(/'(src\/[\w.]+)'/g))
    if (!projects.some((p) => p.dir === m[1]))
      fail(`packableProjects names ${m[1]}, which is not a packable project`,
        `remove it from devtools/project.config.mjs — pack would fail on it`);

  for (const m of apiTests.matchAll(/^\s*"(Lyntai[\w.]*)",/gm))
    if (!assemblies.has(m[1]))
      fail(`ApiSurfaceTests gates "${m[1]}", which ships no assembly`, `remove it from Assemblies() and Loaded`);

  // existsSync first: a missing Baselines/ directory is a REPORTABLE state (every shipping package already
  // failed the per-package "no API baseline" check just above), not a crash. It threw an ENOENT stack trace
  // instead — fail-closed, but the trace says nothing about which package is unprotected. Found 2026-08-11
  // by the test that deletes the last baseline.
  const baselines = fs.existsSync(path.join(repoRoot, baselineDir))
    ? fs.readdirSync(path.join(repoRoot, baselineDir)) : [];
  for (const f of baselines)
    if (f.endsWith('.txt') && !assemblies.has(f.replace(/\.txt$/, '')))
      fail(`orphan baseline ${f}`, `delete ${baselineDir}/${f} — its assembly is gone, so nothing checks it`);

  return { projects, problems, empty: false };
}

/** The gate, reporting through the given sinks. Returns the process exit code. */
export function checkPackages(repoRoot = repo, log = console.log, err = console.error) {
  const { projects, problems, empty } = inventory(repoRoot);
  if (empty) {
    err('check-packages: found no packable projects under src/ — that cannot be right');
    return 1;
  }

  const shipping = projects.filter((p) => p.shipsAssembly).length;
  log(`check-packages: ${projects.length} packable projects ` +
    `(${shipping} shipping an assembly, ${projects.length - shipping} references-only)`);

  if (!problems.length) {
    log('check-packages: every package is registered everywhere it needs to be ✓');
    return 0;
  }
  err(`\ncheck-packages: ✗ ${problems.length} inventory problem(s)`);
  for (const { what, fix } of problems) err(`  • ${what}\n      → ${fix}`);
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test reads no registry.
// `import.meta.main` where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths,
// and a wrong comparison makes this gate silently do NOTHING and exit 0. Pinned by cli-entry.test.mjs.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = checkPackages(repo);
}
