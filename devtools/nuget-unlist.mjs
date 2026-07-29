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
//   Auth: set NUGET_API_KEY in your environment first. Mint it at
//         nuget.org -> Account -> API Keys, scope "Unlist", glob pattern `Lyntai.*`.
//         Never pass it on the command line (it lands in shell history) and never commit it.
//
//   Usage:
//     node devtools/nuget-unlist.mjs                 # DRY RUN — prints exactly what would be unlisted
//     node devtools/nuget-unlist.mjs --apply         # actually unlist
//     node devtools/nuget-unlist.mjs --below 1.1.0   # change the cutoff (default 1.1.0)
//     node devtools/nuget-unlist.mjs --apply --only Lyntai.Core
//
// Idempotent: queries nuget.org for what is currently LISTED and skips everything else, so a re-run after
// a partial failure only does the remainder.

import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);
const SOURCE = 'https://api.nuget.org/v3/index.json';

/** Every packable Lyntai id, including ones retired from the tree (their published versions still exist). */
const PACKAGES = [
  'Lyntai.Core',
  'Lyntai.Storage.Sqlite',
  'Lyntai.Storage.InMemory',
  'Lyntai.Storage.Postgres',
  'Lyntai.Providers.ClaudeCli',
  'Lyntai.Providers.ClaudeCli.Mcp', // retired in 1.1.0 — all versions go
  'Lyntai.Providers.OpenAiCompatible',
  'Lyntai.Providers.ExtensionsAi',
  'Lyntai.Providers.Local',
  'Lyntai.Tools.Mcp',
  'Lyntai.Tools.Mcp.Hosting',
  'Lyntai.Secrets.Dpapi',
];

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

const key = process.env.NUGET_API_KEY;
if (apply && !key) {
  console.error('NUGET_API_KEY is not set. Mint an Unlist-scoped key on nuget.org and set it in your');
  console.error('environment (PowerShell: $env:NUGET_API_KEY = "..."), then re-run with --apply.');
  process.exit(1);
}

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
      process.stdout.write(`  ✗ ${version} — ${String(err.stderr || err.message).trim().split('\n')[0]}\n`);
    }
  }
  console.log('');
}

console.log(apply
  ? `Done. ${done} unlisted, ${failed} failed.`
  : `Planned: ${planned} version(s) would be unlisted. Re-run with --apply to do it.`);
if (failed) process.exitCode = 1;
