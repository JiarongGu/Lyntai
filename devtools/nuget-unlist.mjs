#!/usr/bin/env node
// nuget-unlist.mjs — bulk-UNLIST Lyntai package versions on nuget.org.
//
// `dotnet nuget delete` is misleadingly named: on nuget.org it UNLISTS (hides from search and the version
// dropdown) and never deletes. Restore by exact version keeps working, and the operation is reversible from
// the package's Manage page. Versions are immutable regardless — an unlisted version number can never be
// re-published.
//
// Deprecation is NOT scriptable (no public API — it is web-UI only, on each package's Manage page).
// This tool covers the unlist half only.
//
//   Auth: mint the key at nuget.org -> Account -> API Keys, scope "Unlist", glob pattern `Lyntai.*`.
//         Supply it either way — `--api-key` wins over the environment:
//           $env:NUGET_API_KEY = "..."           # preferred: stays out of shell history
//           --api-key <key>                      # convenient, but the key lands in shell history
//         Never commit it. It is redacted from this tool's own output either way.
//
//   Usage:
//     node devtools/nuget-unlist.mjs                 # DRY RUN — prints exactly what would be unlisted
//     node devtools/nuget-unlist.mjs --apply         # actually unlist
//     node devtools/nuget-unlist.mjs --below 1.1.0   # change the cutoff (default 1.1.0)
//     node devtools/nuget-unlist.mjs --apply --only Lyntai.Core
//     node devtools/nuget-unlist.mjs --apply --below 2.0.1 --api-key oy2...
//
// Idempotent: queries nuget.org for what is currently LISTED and skips everything else, so a re-run after
// a partial failure only does the remainder.
//
// The roster is the packable ids read from `src/*/*.csproj` plus the hand-kept RETIRED list below — never a
// single hand-written array, which went stale once and skipped a live package without saying so.

import { execFile } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';

const run = promisify(execFile);
const SOURCE = 'https://api.nuget.org/v3/index.json';
const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

/**
 * Ids retired from the tree. Their published versions still exist on the feed, and NOTHING on disk
 * remembers them — so this is the only half of the roster maintained by hand. Add an id here whenever a
 * package is removed or folded into another.
 */
const RETIRED = [
  'Lyntai.Providers.ClaudeCli', //       folded into Lyntai.Providers.Default at 2.0.1
  'Lyntai.Providers.CodexCli', //        folded into Lyntai.Providers.Default at 2.0.1
  'Lyntai.Providers.OpenAiCompatible', // folded into Lyntai.Providers.Default at 2.0.1
  'Lyntai.Providers.ClaudeCli.Mcp', //   removed at 1.1.0
];

/**
 * The currently packable ids, READ FROM THE CSPROJS rather than listed here — because a hand-written
 * roster goes stale silently and the miss looks exactly like success. This one did: written before the
 * 2.0.1 repackaging, it omitted `Lyntai.Providers.CodexCli` (which still had 1.2.2 listed) along with
 * `Lyntai`, `Lyntai.Providers.Default` and `Lyntai.Generation`, and reported a clean run regardless.
 * Same failure family as `check-packages` (CLAUDE.md §Dev loop) — a missing registry entry means no gate.
 */
function currentPackageIds() {
  const src = join(REPO_ROOT, 'src');
  const ids = [];
  for (const entry of readdirSync(src, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    let text;
    try {
      text = readFileSync(join(src, entry.name, `${entry.name}.csproj`), 'utf8');
    } catch {
      continue; // not a project directory
    }
    const id = text.match(/<PackageId>([^<]+)<\/PackageId>/)?.[1]?.trim();
    if (id) ids.push(id); // every packable src project declares one (repo-mechanics §Package layout)
  }
  if (ids.length === 0) throw new Error('no packable ids found under src/ — run this from the repository');
  return ids;
}

const PACKAGES = [...currentPackageIds(), ...RETIRED].sort();

const args = process.argv.slice(2);
const apply = args.includes('--apply');
const cutoff = valueOf('--below') ?? '1.1.0';
const only = valueOf('--only');

function valueOf(flag) {
  const i = args.indexOf(flag);
  return i >= 0 && args[i + 1] && !args[i + 1].startsWith('--') ? args[i + 1] : null;
}

/** Numeric semver-core compare (these packages carry no prerelease/build suffixes). */
function cmp(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) - (pb[i] || 0);
  return 0;
}

/** Currently LISTED versions, from the registration index (unlisted ones carry listed:false). */
async function listedVersions(id) {
  const url = `https://api.nuget.org/v3/registration5-gz-semver2/${id.toLowerCase()}/index.json`;
  const res = await fetch(url);
  if (res.status === 404) return null; // never published
  if (!res.ok) throw new Error(`${id}: registration fetch failed (HTTP ${res.status})`);
  const doc = await res.json();
  const out = [];
  for (const page of doc.items ?? [])
    for (const item of page.items ?? []) {
      const c = item.catalogEntry;
      if (c.listed !== false) out.push(c.version);
    }
  return out.sort(cmp);
}

const key = valueOf('--api-key') ?? process.env.NUGET_API_KEY;
if (apply && !key) {
  console.error('No API key. Mint an Unlist-scoped key on nuget.org (glob `Lyntai.*`), then either');
  console.error('  $env:NUGET_API_KEY = "..."   (preferred — stays out of shell history)');
  console.error('  --api-key <key>              (convenient — the key lands in shell history)');
  process.exit(1);
}

/** Never let a key reach the console, even inside a tool error that happened to echo the arguments. */
const redact = (text) => (key ? String(text).split(key).join('***') : String(text));

console.log(`${apply ? 'UNLISTING' : 'DRY RUN — nothing will change'} · versions below ${cutoff}\n`);

let planned = 0, done = 0, failed = 0;

for (const id of PACKAGES) {
  if (only && id.toLowerCase() !== only.toLowerCase()) continue;

  let listed;
  try {
    listed = await listedVersions(id);
  } catch (err) {
    console.log(`${id}\n  ! ${err.message}\n`);
    failed++;
    continue;
  }
  if (listed === null) {
    console.log(`${id}\n  - not published, skipping\n`);
    continue;
  }

  const targets = listed.filter((v) => cmp(v, cutoff) < 0);
  const keep = listed.filter((v) => cmp(v, cutoff) >= 0);
  planned += targets.length;

  console.log(id);
  if (targets.length === 0) {
    console.log('  - nothing listed below the cutoff\n');
    continue;
  }
  console.log(`  unlist (${targets.length}): ${targets.join(', ')}`);
  console.log(`  keep   (${keep.length}): ${keep.join(', ') || '(none — package fully unlisted)'}`);

  if (!apply) {
    console.log('');
    continue;
  }

  for (const version of targets) {
    try {
      await run('dotnet', ['nuget', 'delete', id, version,
        '--source', SOURCE, '--api-key', key, '--non-interactive']);
      done++;
      process.stdout.write(`  ✓ ${version}\n`);
    } catch (err) {
      failed++;
      process.stdout.write(`  ✗ ${version} — ${redact(err.stderr || err.message).trim().split('\n')[0]}\n`);
    }
  }
  console.log('');
}

console.log(apply
  ? `Done. ${done} unlisted, ${failed} failed.`
  : `Planned: ${planned} version(s) would be unlisted. Re-run with --apply to do it.`);
if (failed) process.exitCode = 1;
