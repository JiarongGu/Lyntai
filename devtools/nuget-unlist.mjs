#!/usr/bin/env node
// nuget-unlist — bulk-UNLIST Lyntai package versions on nuget.org (`docs/GATES.md` §nuget-unlist).
//
// `dotnet nuget delete` UNLISTS on nuget.org and never deletes: a pinned restore keeps working, the number is
// never freed, and the Manage page reverses it. Deprecation has no API, so it stays a web-UI step.
//
//   node devtools/dev.mjs nuget-unlist [--below 1.1.0] [--only <id>] [--apply] [--api-key [<key>]]
//
// A DRY RUN unless `--apply`. The key is minted Unlist-scoped on nuget.org and comes from a bare `--api-key`,
// which PROMPTS for it with typing hidden (or reads a pipe's first line); from `NUGET_API_KEY`; or from
// `--api-key <key>`, which leaves it in shell history. It is redacted from everything this prints. Idempotent:
// only what the feed still LISTS is touched, so a re-run after a partial failure does the remainder.
import { execFile } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const SOURCE = 'https://api.nuget.org/v3/index.json';
const REPO_ROOT = join(dirname(here), '..');

/**
 * Ids retired from the tree, whose published versions still sit on the feed — the one half of the roster
 * nothing on disk remembers, so add an id here whenever a package is removed or folded (D44).
 *
 * These are PUBLISHED ids, never current names: a rename sweep must not touch them. `mustBePublished` is
 * what catches one that it did.
 */
export const RETIRED = [
  'Lyntai.Providers.ClaudeCli', //        folded into Lyntai.Providers.Default at 2.0.1
  'Lyntai.Providers.CodexCli', //         folded into Lyntai.Providers.Default at 2.0.1
  'Lyntai.Providers.OpenAiCompatible', // folded into Lyntai.Providers.Default at 2.0.1
  'Lyntai.Providers.ClaudeCli.Mcp', //    removed at 1.1.0
  'Lyntai.Providers.Local', //            renamed to Lyntai.Providers.LlamaSharp (D122's naming pass)
  'Lyntai.Providers.ExtensionsAi', //     folded into Lyntai.Providers.Default (D123)
  'Lyntai.Tools.Mcp.Hosting', //          folded into Lyntai.Tools.Mcp (D142)
  'Lyntai.Providers.Default', //          renamed to Lyntai.Providers.Basic (D144)
  'Lyntai.Storage.InMemory', //          folded into Lyntai.Storage.Basic (D173)
];

/**
 * A RETIRED id that the feed has never heard of is a TYPO, not an absence — the array exists only for ids
 * that WERE published, so "never published" is the one answer it can never legitimately give. `null` means
 * the registration fetch 404'd.
 */
export function mustBePublished(id, listed, retired = RETIRED) {
  return listed === null && retired.includes(id);
}

/** The currently packable ids, READ FROM THE CSPROJS — a hand-written roster once skipped a live package. */
export function currentPackageIds(repo = REPO_ROOT) {
  const src = join(repo, 'src');
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
    if (id) ids.push(id);
  }
  if (ids.length === 0) throw new Error('no packable ids found under src/ — run this from the repository');
  return ids;
}

/** Numeric semver-core compare (these packages carry no prerelease/build suffixes). */
export function cmp(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) - (pb[i] || 0);
  return 0;
}

/** Currently LISTED versions from the registration index, or `null` when the id was never published. */
export async function listedVersions(id, fetchFn = fetch) {
  const url = `https://api.nuget.org/v3/registration5-gz-semver2/${id.toLowerCase()}/index.json`;
  const res = await fetchFn(url);
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`${id}: registration fetch failed (HTTP ${res.status})`);
  const doc = await res.json();
  const out = [];
  for (const page of doc.items ?? [])
    for (const item of page.items ?? []) if (item.catalogEntry.listed !== false) out.push(item.catalogEntry.version);
  return out.sort(cmp);
}

/**
 * Read a secret without echoing it: on a terminal in raw mode, honouring backspace; from a pipe, its first
 * line — a secret store's output, say. A terminal emulator that presents no TTY (Git Bash's mintty without
 * winpty) is read as a pipe, so its typing shows; the hint says so.
 * @returns {Promise<string>} what was entered
 */
export async function promptHidden(question, { input = process.stdin, output = process.stderr } = {}) {
  if (!input.isTTY) {
    output.write('(input is not a terminal, so typing would show — pipe the key in, or use PowerShell/cmd)\n');
    output.write(question);
    const chunks = [];
    for await (const chunk of input) {
      chunks.push(Buffer.from(chunk));
      if (Buffer.concat(chunks).includes('\n')) break;
    }
    output.write('\n');
    return Buffer.concat(chunks).toString('utf8').split(/\r?\n/)[0];
  }
  output.write(question);
  return new Promise((resolveAnswer, reject) => {
    let value = '';
    const finish = (error) => {
      input.setRawMode(false);
      input.pause();
      input.off('data', onData);
      output.write('\n');
      if (error) reject(error); else resolveAnswer(value);
    };
    const onData = (text) => {
      for (const ch of String(text)) {
        if (ch === '\r' || ch === '\n') return finish();
        if (ch === '\u0003') return finish(new Error('cancelled'));     // Ctrl+C in raw mode
        value = ch === '\u007f' || ch === '\b' ? value.slice(0, -1) : value + ch;
      }
    };
    input.setRawMode(true);
    input.setEncoding('utf8');
    input.on('data', onData);
    input.resume();
  });
}

/**
 * The tool. Every effect is a seam — `fetch` for the feed, `run` for `dotnet nuget delete`, `ids` for the
 * roster, `prompt` for a bare `--api-key` — so a test drives it without the network or the key.
 * @returns {Promise<number>} the exit code
 */
export async function nugetUnlist({
  args = [], env = {}, fetch: fetchFn = fetch, run = promisify(execFile), log = console.log,
  ids = () => [...currentPackageIds(), ...RETIRED].sort(), prompt = promptHidden,
} = {}) {
  const valueOf = (flag) => {
    const i = args.indexOf(flag);
    return i >= 0 && args[i + 1] && !args[i + 1].startsWith('--') ? args[i + 1] : null;
  };
  const apply = args.includes('--apply');
  const cutoff = valueOf('--below') ?? '1.1.0';
  const only = valueOf('--only');
  // a bare --api-key asks for the key — only when applying, since a dry run needs none
  const asks = apply && args.includes('--api-key') && valueOf('--api-key') === null;
  const key = (asks ? (await prompt('nuget.org API key (hidden): ')).trim() : null)
    || valueOf('--api-key') || env.NUGET_API_KEY;
  const redact = (text) => (key ? String(text).split(key).join('***') : String(text));

  if (apply && !key) {
    log('No API key. Mint an Unlist-scoped key on nuget.org (glob `Lyntai.*`), then either');
    log('  --api-key                    (prompts for it, typing hidden — or pipe it in)');
    log('  $env:NUGET_API_KEY = "..."   (stays out of the command line)');
    log('  --api-key <key>              (convenient — the key lands in shell history)');
    return 1;
  }
  log(`${apply ? 'UNLISTING' : 'DRY RUN — nothing will change'} · versions below ${cutoff}\n`);

  let planned = 0, done = 0, failed = 0;
  for (const id of ids()) {
    if (only && id.toLowerCase() !== only.toLowerCase()) continue;
    let listed;
    try {
      listed = await listedVersions(id, fetchFn);
    } catch (err) {
      log(`${id}\n  ! ${err.message}\n`);
      failed++;
      continue;
    }
    if (mustBePublished(id, listed)) {
      log(`${id}\n  ✗ RETIRED but the feed has never published it — this id is wrong.`);
      log('    Nothing here can unlist the real package. A rename sweep is the usual cause: these are');
      log('    PUBLISHED ids, never current names.\n');
      failed++;
      continue;
    }
    if (listed === null) { log(`${id}\n  - not published, skipping\n`); continue; }

    const targets = listed.filter((v) => cmp(v, cutoff) < 0);
    const keep = listed.filter((v) => cmp(v, cutoff) >= 0);
    planned += targets.length;
    log(id);
    if (targets.length === 0) { log('  - nothing listed below the cutoff\n'); continue; }
    log(`  unlist (${targets.length}): ${targets.join(', ')}`);
    log(`  keep   (${keep.length}): ${keep.join(', ') || '(none — package fully unlisted)'}`);
    if (!apply) { log(''); continue; }

    for (const version of targets) {
      try {
        await run('dotnet', ['nuget', 'delete', id, version, '--source', SOURCE, '--api-key', key, '--non-interactive']);
        done++;
        log(`  ✓ ${version}`);
      } catch (err) {
        failed++;
        log(`  ✗ ${version} — ${redact(err.stderr || err.message).trim().split('\n')[0]}`);
      }
    }
    log('');
  }

  log(apply
    ? `Done. ${done} unlisted, ${failed} failed.`
    : `Planned: ${planned} version(s) would be unlisted. Re-run with --apply to do it.`);
  return failed ? 1 : 0;
}

// CLI entry point — a thin wrapper, so importing this module reads no argument, no key and no network.
// The exit code is SET rather than `process.exit()`ed, which would abort a request still in flight.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  process.exitCode = await nugetUnlist({ args: process.argv.slice(2), env: process.env });
}
