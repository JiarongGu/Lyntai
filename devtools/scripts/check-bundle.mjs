// check-bundle — the `Lyntai` bundle's DEPENDENCY BUDGET (docs/DECISIONS.md D26).
//
// Membership in the one-line install is not free: an untrimmed `dotnet publish` copies the WHOLE dependency
// graph and analyses nothing, so every package the bundle references lands in every bundle consumer's output
// folder whether they call it or not. This fails when the bundle's third-party closure gains an id nobody
// decided on — the drift a growing package graph produces silently, since adding one ProjectReference can
// pull a whole SDK behind it. The `Microsoft.Extensions.*` band is auto-allowed: those ship on the runtime's
// own version band and any DI app already has them.
//
// Extracted from dev.mjs 2026-08-11 (TASKS.md Part 62) so it can be driven by a test. Nothing about what it
// CATCHES changed in the move — the closure is read from the same `project.assets.json`, the band rule, the
// two failure branches and every message are as they were. What the extraction buys is that the
// allowlist-staleness branch, which had never run against a fixture, now runs on every `verify`.
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repoDefault = join(dirname(here), '..', '..');

/**
 * The auto-allowed band. Deliberately a prefix and not a list: these ship on the runtime's own version band
 * (any DI app already restores them), so enumerating them would be a second registry to keep in step for no
 * decision. Anything OUTSIDE the band is a deliberate cost that must be named in `bundle.allowedThirdParty`.
 */
export const ON_RUNTIME_BAND = (id) => id.startsWith('Microsoft.Extensions.');

/**
 * The third-party closure of a restored `project.assets.json`, as `{ id, version }`.
 *
 * `type === 'package'` is the load-bearing filter: the same `libraries` map also lists every PROJECT in the
 * graph (`type: 'project'`), and counting those would report this repository's own packages as an
 * undecided third-party dependency — the budget would fail on its own success.
 */
export function bundleClosure(assets) {
  return Object.entries(assets?.libraries ?? {})
    .filter(([, v]) => v?.type === 'package')
    .map(([k]) => ({ id: k.split('/')[0], version: k.split('/')[1] }));
}

/**
 * The budget itself, over a closure: what is on the band, what is outside it, what nobody decided on
 * (`unexpected`), and which allowances have rotted (`stale`).
 *
 * `stale` is the half that is easy to under-rate. An allowance that no longer matches anything is not
 * harmless tidy-up: it is a standing permission for an id to come BACK without anyone deciding again, which
 * is precisely the drift the budget exists to make visible.
 */
export function bundleBudget(closure, allowedThirdParty = []) {
  const allowed = new Set(allowedThirdParty);
  const band = closure.filter((p) => ON_RUNTIME_BAND(p.id));
  const outside = closure.filter((p) => !ON_RUNTIME_BAND(p.id));
  return {
    band,
    outside,
    unexpected: outside.filter((p) => !allowed.has(p.id)),
    stale: [...allowed].filter((id) => !outside.some((p) => p.id === id)),
  };
}

/** The restore that makes the assets file reflect the CURRENT ProjectReferences (fast when up to date). */
export const restoreBundle = (repo, project, spawn = spawnSync) =>
  spawn('dotnet', ['restore', join(project, `${basename(project)}.csproj`), '-v', 'quiet'],
    { cwd: repo, encoding: 'utf8' });

/**
 * The gate. `restore` and `readAssets` are seams so a test can drive every branch without dotnet in the
 * loop — including the two that only happen when the toolchain is unhappy, which is exactly when a gate
 * that gets them wrong is least likely to be noticed.
 */
export function checkBundle({
  repo = repoDefault,
  config,
  log = console.log,
  error = console.error,
  restore = null,
  readAssets = null,
} = {}) {
  const label = 'check-bundle';
  const cfg = config?.bundle;
  if (!cfg) { log(`${label}: no bundle configured — skipped`); return 0; }

  const restored = (restore ?? (() => restoreBundle(repo, cfg.project)))();
  if (restored.status !== 0) {
    error(`${label}: restore failed — cannot read the dependency closure\n${restored.stdout ?? ''}`);
    return restored.status ?? 1;
  }

  const assetsPath = join(repo, cfg.project, 'obj', 'project.assets.json');
  const read = readAssets ?? (() => (existsSync(assetsPath) ? readFileSync(assetsPath, 'utf8') : null));
  const raw = read();
  if (raw === null || raw === undefined) {
    error(`${label}: no project.assets.json at ${cfg.project}/obj — run a restore/build first`);
    return 1;
  }

  const { band, outside, unexpected, stale } = bundleBudget(bundleClosure(JSON.parse(raw)), cfg.allowedThirdParty);
  const closureSize = band.length + outside.length;

  log(`${label}: ${closureSize} third-party packages in the bundle closure `
    + `(${band.length} on the Microsoft.Extensions.* band, auto-allowed)`);
  for (const p of outside) log(`  outside the band: ${p.id} ${p.version}`);

  if (unexpected.length) {
    error(`\n${label}: ✗ ${unexpected.length} package(s) nobody decided on:`);
    for (const p of unexpected) error(`    ${p.id} ${p.version}`);
    error('  The bundle forces these on EVERY one-line-install consumer, used or not.\n'
      + '  Either keep the package out of the bundle (consumers reference it directly), or accept the cost:\n'
      + '  add the id to `bundle.allowedThirdParty` in devtools/project.config.mjs and record why in D26.');
    return 1;
  }
  if (stale.length) {
    error(`\n${label}: ✗ allowlist is stale — no longer in the closure: ${stale.join(', ')}\n`
      + '  Remove it from `bundle.allowedThirdParty` (and D26) so the budget keeps meaning something.');
    return 1;
  }
  log(`${label}: bundle dependency budget respected ✓`);
  return 0;
}

// CLI entry point — a thin wrapper, so importing this module for a test restores nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths, and a wrong comparison
// makes this gate silently do NOTHING and exit 0. Pinned by cli-entry.test.mjs.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkBundle({ repo: repoDefault, config });
}
