// Lyntai devtools dispatcher (family pattern: one entry, project-specific inputs in project.config.mjs).
//   node devtools/dev.mjs build            - dotnet build the solution
//   node devtools/dev.mjs check-warnings   - FAIL if any src/ project compiles with a warning (--list for all)
//   node devtools/dev.mjs check-bundle     - FAIL if the `Lyntai` bundle's dependency closure drifted (D32)
//   node devtools/dev.mjs check-packages   - FAIL if a package is missing from any registry it needs (D33)
//   node devtools/dev.mjs new-package <Id> - scaffold an adapter package + register it in all nine registries
//   node devtools/dev.mjs consumer-smoke   - pack, then restore/build/run a fresh app against the PACKAGES
//   node devtools/dev.mjs test [args]      - dotnet test the test project (extra args pass through)
//   node devtools/dev.mjs e2e [all|pN|pN-pM|p1,p3] [--build] [--parallel[=N]] - Playground e2e suites
//   node devtools/dev.mjs playground [args]- run the sample console app (uses LYNTAI_PROVIDER_CMD if set)
//   node devtools/dev.mjs pack             - dotnet pack the packable libraries -> publish/packages/
//   node devtools/dev.mjs doctor [--fix]   - check README ## Status version == VersionPrefix (--fix syncs it)
//                                            AND VersionPrefix == the newest v* release tag (authorship)
//   node devtools/dev.mjs check-version    - the pre-commit version-authorship guard, run by hand
//   node devtools/dev.mjs changelog [--fix] [--version X.Y.Z] [--date YYYY-MM-DD]
//                                          - check/stamp the CHANGELOG `## Unreleased` heading for a release
//   node devtools/dev.mjs install-hooks    - git core.hooksPath -> devtools/hooks (pre-commit guard)
//   node devtools/dev.mjs check-sensitive  - scan staged changes (--tree for all tracked files)
import { spawn, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import config, { toSemver } from './project.config.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [cmd, ...args] = process.argv.slice(2);

const run = (exe, argv, opts = {}) => {
  const r = spawnSync(exe, argv, { stdio: 'inherit', cwd: repo, shell: false, ...opts });
  process.exitCode = r.status ?? 1;
};

// pack-doctor: keep the README `## Status` headline version in lock-step with VersionPrefix (the single
// source in src/Directory.Build.props), so a shipped nupkg's README never advertises a stale version. The
// release pipeline BUMPS the version, so `pack` (and `doctor --fix`) SYNC the header to match — no manual
// README edit. `doctor` with no flag just CHECKS and fails on drift. The value that gets updated is the
// `**vX.Y.Z` at the start of the `## Status` headline (flagged with an HTML comment in the README).
const statusVersionOf = (readme) =>
  ((readme.split(/^## /m).find((s) => s.startsWith('Status')) ?? '').match(/\*\*v(\d+\.\d+\.\d+)/) ?? [])[1] ?? null;

const packDoctor = ({ fix = false } = {}) => {
  const file = path.join(repo, 'README.md');
  const readme = fs.readFileSync(file, 'utf8');
  const found = statusVersionOf(readme);
  if (found === config.version) {
    console.log(`pack-doctor: README Status matches VersionPrefix (${config.version}) ✓`);
    return true;
  }
  if (fix) {
    // rewrite ONLY the first `**vX.Y.Z` inside the `## Status` section
    const at = readme.search(/^## Status/m);
    fs.writeFileSync(file, readme.slice(0, at) + readme.slice(at).replace(/\*\*v\d+\.\d+\.\d+/, `**v${config.version}`));
    console.log(`pack-doctor: synced README "## Status" version ${found ? 'v' + found : '(none)'} → ` +
      `v${config.version} (from VersionPrefix)`);
    return true;
  }
  console.error(`pack-doctor: README "## Status" version (${found ?? 'none found'}) != VersionPrefix ` +
    `(${config.version}) — run \`node devtools/dev.mjs doctor --fix\` (or \`pack\`, which auto-syncs it).`);
  return false;
};

// version-doctor: VersionPrefix must equal the LAST RELEASED tag. The release workflow bumps VersionPrefix
// as PART of releasing, so between releases the two are equal by construction — any other value means the
// version was authored by hand, and the next release will bump FROM that hand-written baseline and publish
// the version after the intended one (a sibling repo lost 0.2.0 exactly this way: a manual 0.1.2 → 0.2.0
// became a published 0.3.0). This is the STATE check, so it also catches a bad merge or rebase that moved
// the version; check-version-bump.mjs catches the ACT at commit time.
//
// Deliberately NOT part of `verify` or `pack`: the release workflow writes the NEW version before running
// both, so during a legitimate release VersionPrefix is *supposed* to be ahead of the newest tag. Silent
// when there are no tags (a shallow CI checkout or a fresh clone has none) and when LYNTAI_RELEASE=1.
const newestReleaseTag = () => {
  const r = spawnSync('git', ['tag', '--list', 'v*', '--sort=-v:refname'],
    { cwd: repo, encoding: 'utf8', shell: false });
  return (r.stdout ?? '').split('\n').map((s) => s.trim()).filter(Boolean)[0] ?? null;
};

const versionDoctor = () => {
  if (process.env.LYNTAI_RELEASE === '1') {
    console.log('version-doctor: skipped (LYNTAI_RELEASE=1 — release pipeline or deliberate repair)');
    return true;
  }
  const tag = newestReleaseTag();
  if (!tag) {
    console.log('version-doctor: no v* release tags to compare against — skipped ✓');
    return true;
  }
  const tagged = toSemver(tag.replace(/^v/, ''));
  if (tagged === config.version) {
    console.log(`version-doctor: VersionPrefix matches the newest release tag (${tag}) ✓`);
    return true;
  }
  console.error(`version-doctor: VersionPrefix (${config.version}) != newest release tag (${tag}) — the ` +
    'version looks HAND-EDITED.\n  Between releases they are equal by construction (the release workflow ' +
    'bumps VersionPrefix as part of releasing).\n  A moved baseline makes the next release publish the ' +
    `version AFTER the intended one — restore <VersionPrefix> to ${tagged} in src/Directory.Build.props ` +
    'and let the workflow bump it.\n  Mid-release, or repairing one on purpose? LYNTAI_RELEASE=1 ' +
    'node devtools/dev.mjs doctor');
  return false;
};

// changelog-doctor: stamp the CHANGELOG's `## Unreleased` heading with the version being released, the way
// the release pipeline already stamps VersionPrefix + the README `## Status` headline. Cutting a release is
// otherwise the ONE place a human had to remember a manual edit — and v1.2.0 shipped with its section still
// titled "Unreleased" because of it.
//
// Two heading shapes are produced, matching what the file already uses:
//   `## Unreleased`                → `## X.Y.Z — 2026-07-30`
//   `## Unreleased — <title>`      → `## X.Y.Z — <title> (2026-07-30)`
// so an author who wants a titled release writes the title on the Unreleased heading in advance; nothing is
// ever invented here. IDEMPOTENT: a heading for the version already present means the release was already
// stamped (a pipeline re-run), and the file is left untouched.
const unreleasedHeading = /^## Unreleased[ \t]*(?:[—–-][ \t]*(.+?))?[ \t]*$/m;

const changelogDoctor = ({ fix = false, version = config.version, date } = {}) => {
  const file = path.join(repo, 'CHANGELOG.md');
  const changelog = fs.readFileSync(file, 'utf8');
  const stamped = new RegExp(`^## ${version.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?:[ \t]|$)`, 'm');

  if (stamped.test(changelog)) {
    console.log(`changelog-doctor: CHANGELOG already has a "## ${version}" heading ✓`);
    return true;
  }

  const match = changelog.match(unreleasedHeading);
  if (!match) {
    // Neither a section for this version nor an Unreleased one to promote: nothing DOCUMENTS what is being
    // shipped. Report it, but never fail a release over a doc heading — the packages are the deliverable.
    console.warn(`changelog-doctor: no "## ${version}" and no "## Unreleased" heading in CHANGELOG.md — ` +
      'nothing to stamp (add the section by hand).');
    return true;
  }
  if (!fix) {
    console.error(`changelog-doctor: CHANGELOG "## Unreleased" is not stamped for ${version} — ` +
      'run `node devtools/dev.mjs changelog --fix` (the release workflow does this for you).');
    return false;
  }

  const title = match[1]?.trim();
  const on = date ?? new Date().toISOString().slice(0, 10); // UTC — the release runs on a UTC runner
  const heading = title ? `## ${version} — ${title} (${on})` : `## ${version} — ${on}`;
  fs.writeFileSync(file, changelog.replace(unreleasedHeading, heading));
  console.log(`changelog-doctor: stamped "${match[0]}" → "${heading}"`);
  return true;
};

switch (cmd) {
  case 'build':
    run('dotnet', ['build', config.solution, '-v', 'minimal']);
    break;

  // A LIBRARY-specific gate: the published projects must compile warning-free. Not style policing —
  // it's how a shipped claim stops rotting silently. `IsAotCompatible=true` stamps IsTrimmable into the
  // assembly, telling a consumer's trimmer "safe to trim"; the only thing that catches code which breaks
  // that promise is an IL2026/IL3050 warning, and a warning nobody fails on is a warning nobody reads
  // (four of them shipped into Lyntai.Providers.Default this way). Doc-comment warnings matter for the same
  // reason: unresolved crefs ship inside the XML docs consumers read in IntelliSense.
  // Scoped to src/ — tests and samples are free to warn. Pass --list to see them all.
  case 'check-warnings': {
    const label = 'check-warnings';
    // -warnaserror is deliberately NOT used: it stops the build at the first project, hiding the rest.
    const r = spawnSync('dotnet', ['build', config.solution, '-v', 'normal', '--no-incremental'],
      { cwd: repo, encoding: 'utf8' });
    const lines = [...new Set((r.stdout || '').split(/\r?\n/).filter((l) =>
      /warning [A-Z]{2,4}\d+/.test(l) && /[\\/]src[\\/]/.test(l)))];
    if (r.status !== 0) {
      console.error(`${label}: build FAILED — fix the build first`);
      process.exitCode = r.status ?? 1;
      break;
    }
    if (!lines.length) {
      console.log(`${label}: src/ compiles warning-free ✓`);
      break;
    }
    const show = args.includes('--list') ? lines : lines.slice(0, 15);
    console.error(`${label}: ✗ ${lines.length} warning(s) in src/ — a published project must compile clean`);
    for (const l of show) console.error(`  ${l.replace(repo, '.').trim()}`);
    if (show.length < lines.length) console.error(`  … ${lines.length - show.length} more (--list to see all)`);
    process.exitCode = 1;
    break;
  }

  // The `Lyntai` bundle's DEPENDENCY BUDGET (docs/DECISIONS.md D32). Membership in the one-line install is
  // not free: an untrimmed `dotnet publish` copies the whole graph and analyses nothing, so every package the
  // bundle references lands in every bundle consumer's output folder whether they call it or not. This fails
  // when the bundle's third-party closure gains an id nobody decided on — the drift that a growing package
  // graph produces silently, since adding one ProjectReference can pull a whole SDK behind it.
  // The Microsoft.Extensions.* band is auto-allowed (runtime version band; present in any DI app).
  case 'check-bundle': {
    const label = 'check-bundle';
    const cfg = config.bundle;
    if (!cfg) { console.log(`${label}: no bundle configured — skipped`); break; }

    // restore so the assets file reflects the CURRENT ProjectReferences (fast when already up to date)
    const restore = spawnSync('dotnet', ['restore', path.join(cfg.project, path.basename(cfg.project) + '.csproj'),
      '-v', 'quiet'], { cwd: repo, encoding: 'utf8' });
    if (restore.status !== 0) {
      console.error(`${label}: restore failed — cannot read the dependency closure\n${restore.stdout ?? ''}`);
      process.exitCode = restore.status ?? 1;
      break;
    }

    const assetsPath = path.join(repo, cfg.project, 'obj', 'project.assets.json');
    if (!fs.existsSync(assetsPath)) {
      console.error(`${label}: no project.assets.json at ${cfg.project}/obj — run a restore/build first`);
      process.exitCode = 1;
      break;
    }

    const libs = JSON.parse(fs.readFileSync(assetsPath, 'utf8')).libraries ?? {};
    const closure = Object.entries(libs)
      .filter(([, v]) => v.type === 'package')
      .map(([k]) => ({ id: k.split('/')[0], version: k.split('/')[1] }));
    const band = closure.filter((p) => p.id.startsWith('Microsoft.Extensions.'));
    const outside = closure.filter((p) => !p.id.startsWith('Microsoft.Extensions.'));
    const allowed = new Set(cfg.allowedThirdParty ?? []);

    const unexpected = outside.filter((p) => !allowed.has(p.id));
    const stale = [...allowed].filter((id) => !outside.some((p) => p.id === id));

    console.log(`${label}: ${closure.length} third-party packages in the bundle closure ` +
      `(${band.length} on the Microsoft.Extensions.* band, auto-allowed)`);
    for (const p of outside) console.log(`  outside the band: ${p.id} ${p.version}`);

    if (unexpected.length) {
      console.error(`\n${label}: ✗ ${unexpected.length} package(s) nobody decided on:`);
      for (const p of unexpected) console.error(`    ${p.id} ${p.version}`);
      console.error('  The bundle forces these on EVERY one-line-install consumer, used or not.\n' +
        '  Either keep the package out of the bundle (consumers reference it directly), or accept the cost:\n' +
        '  add the id to `bundle.allowedThirdParty` in devtools/project.config.mjs and record why in D32.');
      process.exitCode = 1;
      break;
    }
    if (stale.length) {
      console.error(`\n${label}: ✗ allowlist is stale — no longer in the closure: ${stale.join(', ')}\n` +
        '  Remove it from `bundle.allowedThirdParty` (and D32) so the budget keeps meaning something.');
      process.exitCode = 1;
      break;
    }
    console.log(`${label}: bundle dependency budget respected ✓`);
    break;
  }

  // The package INVENTORY is consistent across every registry that must know about a package (D33).
  case 'check-packages':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-packages.mjs'), ...args]);
    break;

  // Scaffold an adapter package AND register it in all nine registries — the companion to check-packages.
  // Bundle membership stays a human decision (D32); everything mechanical is done for you.
  case 'new-package':
    run('node', [path.join(repo, 'devtools', 'scripts', 'new-package.mjs'), ...args]);
    break;

  // A RELEASE gate, not a dev-loop one: packs every package to a scratch feed and restores/builds/runs a fresh
  // consumer app against it. Minutes, not seconds — deliberately out of `verify`. Run before a release or
  // after touching packaging; it is the only check that exercises what actually SHIPS.
  case 'consumer-smoke':
    run('node', [path.join(repo, 'devtools', 'scripts', 'consumer-smoke.mjs'), ...args]);
    break;

  case 'test':
    run('dotnet', ['test', config.testProject, '-v', 'minimal', ...args]);
    break;

  case 'playground':
    run('dotnet', ['run', '--project', config.playgroundProject, ...args]);
    break;

  case 'bench':
    // BenchmarkDotNet refuses a Debug build — always Release. Extra args pass to the switcher
    // (e.g. `node devtools/dev.mjs bench -- --filter *Router*`).
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', ...args]);
    break;

  case 'install-hooks':
    run('git', ['config', 'core.hooksPath', 'devtools/hooks']);
    console.log('git hooks installed (core.hooksPath = devtools/hooks). Pre-commit runs check-sensitive.');
    break;

  case 'check-sensitive':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-sensitive.mjs'), ...args]);
    break;

  case 'doctor': {
    // both checks always run (no short-circuit) so drift is reported in one pass. `--fix` syncs the README
    // headline; it deliberately does NOT "fix" the version — a hand-authored version is the problem, not
    // the symptom, so it is restored by hand or by letting the release workflow bump it.
    const readmeOk = packDoctor({ fix: args.includes('--fix') });
    const versionOk = versionDoctor();
    process.exitCode = readmeOk && versionOk ? 0 : 1;
    break;
  }

  case 'check-version':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-version-bump.mjs'), ...args]);
    break;

  case 'changelog': {
    // Deliberately NOT folded into `doctor`/`pack`: those run on every local pack, and rewriting a
    // history-facing document as a side effect of building packages would be a surprise. Stamping is an
    // explicit act of RELEASING — the release workflow calls this, and a manual release runs the same line.
    const valueOf = (flag) => {
      const at = args.indexOf(flag);
      return at >= 0 ? args[at + 1] : undefined;
    };
    process.exitCode = changelogDoctor({
      fix: args.includes('--fix'),
      version: valueOf('--version') ?? config.version,
      date: valueOf('--date'),
    }) ? 0 : 1;
    break;
  }

  case 'pack': {
    // auto-sync the README `## Status` version to VersionPrefix, then pack — the release pipeline bumps the
    // version, so pack updates the header for it (never packs a stale README, never hard-fails on a bump).
    packDoctor({ fix: true });
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

  case 'verify': {
    // the single "am I done?" gate: build → test → e2e → leak scan, stopping at the first failure.
    const steps = [['build', []], ['check-warnings', []], ['check-packages', []], ['check-bundle', []],
      ['test', []], ['e2e', []], ['check-sensitive', ['--tree']]];
    let failed = null;
    for (const [step, extra] of steps) {
      console.log(`\n=== verify: ${step} ===`);
      const r = spawnSync('node', [path.join(repo, 'devtools', 'dev.mjs'), step, ...extra], { stdio: 'inherit', cwd: repo });
      if (r.status !== 0) { failed = step; process.exitCode = r.status ?? 1; break; }
    }
    if (failed) console.error(`\nverify: ✗ FAILED at ${failed}`);
    else console.log('\nverify: ✓ all gates green ' +
      '(build · warnings · packages · bundle · test · e2e · check-sensitive)');
    break;
  }

  case 'new-migration': {
    // scaffold the next FluentMigrator migration with a guaranteed-unique, monotonic YYYYMMDDNNNN
    // number (reusing a number is silently skipped — the classic footgun the audit flagged).
    const raw = args[0];
    if (!raw || !/^[a-z][a-z0-9_-]*$/i.test(raw)) {
      console.error('usage: node devtools/dev.mjs new-migration <name>   (e.g. add-jobs-table)');
      process.exitCode = 1;
      break;
    }
    const dir = path.join(repo, 'src', 'Lyntai.Storage.Sqlite', 'Migrations');
    const nums = fs.readdirSync(dir).map((f) => (f.match(/^M(\d{12})_/) ?? [])[1]).filter(Boolean).map(Number);
    const max = nums.length ? Math.max(...nums) : 0;
    const d = new Date();
    const today = Number(`${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`);
    let num = today * 10000 + 1;
    while (num <= max) num++; // strictly greater than every existing number — never reuse one
    const pascal = raw.split(/[-_]/).filter(Boolean).map((s) => s[0].toUpperCase() + s.slice(1)).join('');
    const cls = `M${num}_${pascal}`;
    const file = path.join(dir, `${cls}.cs`);
    if (fs.existsSync(file)) { console.error(`already exists: ${file}`); process.exitCode = 1; break; }
    fs.writeFileSync(file, [
      'using FluentMigrator;',
      '',
      'namespace Lyntai.Storage.Sqlite.Migrations;',
      '',
      '/// <summary>TODO: what this migration does.</summary>',
      `[Migration(${num})]`,
      `public sealed class ${cls} : Migration`,
      '{',
      '    public override void Up()',
      '    {',
      '        // TODO. Prefix every object lyntai_. snake_case columns. Composite PK + FK inline at',
      "        // Create.Table (SQLite can't ALTER ADD CONSTRAINT). Wrap 0..1/double columns in",
      '        // CAST(x AS REAL) when you SELECT them. Searchable text? add an FTS5 trigram',
      "        // external-content mirror + AFTER INSERT/DELETE/UPDATE triggers (emit 'delete' rows on",
      '        // delete AND update) + an in-migration backfill — see M202607170003_Memory and',
      '        // .claude/knowledge/storage.md.',
      '    }',
      '',
      '    public override void Down()',
      '    {',
      '        // TODO: reverse Up.',
      '    }',
      '}',
      '',
    ].join('\n'));
    console.log(`created ${path.relative(repo, file).replaceAll('\\', '/')} — number ${num} (unique, monotonic).`);
    console.log('Next: define the table, then a store + its I*Store impl. See .claude/skills/add-migration.');
    break;
  }

  default:
    console.log('usage: node devtools/dev.mjs <build|check-warnings|check-bundle|check-packages|test|e2e|verify|playground|bench|pack|doctor|changelog|' +
      'new-migration|new-package|consumer-smoke|install-hooks|check-sensitive|check-version>');
    process.exitCode = cmd ? 1 : 0;
}
