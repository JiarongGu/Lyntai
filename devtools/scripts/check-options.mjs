#!/usr/bin/env node
// check-options — FAIL when a shipped option reaches a consumer with nothing to explain it.
//
// The standard is the owner's: which model to run and which option to select is the consuming
// application's job, so the library's job is to document all of them and WHY they exist. A settable
// property on a public `*Options` type is the exact surface where that obligation lands — it is what a
// consumer sets, and an undocumented one shows up in IntelliSense as a bare name with no hint of what it
// buys or what the default costs.
//
// SCOPE IS DELIBERATELY NARROW, and the narrowness is the measurement rather than timidity. The rule is
// "carries no `///` doc at all", which scored 5 defects in 5 hits on the first real run — all five genuine,
// four of them on `AgentSessionOptions`, including `Model`. The obvious wider rule ("a one-line doc says
// WHAT and not WHY") was refused: 53 options have one and most are correct, because `ApiKey` and `BaseUrl`
// do not need an essay. `pitfalls.md` records two gates this repository built, measured at a 0% defect
// rate, and withdrew; a hit here is a defect by construction, which is the bar.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { repoFiles } from './_repo-files.mjs';

/** A public `*Options` type declaration. Records and classes both, sealed or not. */
const TYPE = /^\s*public\s+(?:sealed\s+|abstract\s+)?(?:partial\s+)?(?:class|record|record\s+class)\s+(\w*Options)\b/;

/** A settable public property — `{ get; init; }` or `{ get; set; }`, the shape a consumer assigns. */
const PROP = /^\s*public\s+[\w.<>?\[\],\s]+?\s(\w+)\s*\{\s*get;\s*(?:init|set);/;

/** Only `src/` ships. A test's or a bench's options type is an instrument, not a contract. */
const SHIPS = (f) => f.startsWith('src/') && f.endsWith('.cs');

/**
 * Every settable option on a public `*Options` type, with how many `///` lines document it.
 *
 * The doc walk steps back over attributes and blank lines first, so `[JsonIgnore]` between a doc and its
 * property does not read as undocumented. It counts LINES rather than testing presence because the count is
 * what makes the "one-line" tier reportable without being enforced.
 */
export function collectOptions(repo, files) {
  const rows = [];
  for (const file of files.filter(SHIPS)) {
    const full = path.join(repo, file);
    if (!fs.existsSync(full)) continue;          // listed-then-deleted mid-refactor; not this gate's problem
    const lines = fs.readFileSync(full, 'utf8').split(/\r?\n/);
    let type = null;
    for (let i = 0; i < lines.length; i++) {
      const t = TYPE.exec(lines[i]);
      if (t) { type = t[1]; continue; }
      if (!type) continue;                        // a property before any *Options type is not ours
      const p = PROP.exec(lines[i]);
      if (!p) continue;

      let j = i - 1;
      while (j >= 0 && (/^\s*\[/.test(lines[j]) || /^\s*$/.test(lines[j]))) j--;
      let docLines = 0;
      while (j >= 0 && /^\s*\/\/\//.test(lines[j])) { docLines++; j--; }

      rows.push({ file, line: i + 1, type, prop: p[1], docLines });
    }
  }
  return rows;
}

/**
 * @param {string} repo
 * @param {{optionDocAllowances?: {type: string, prop: string, why: string}[]}} config
 * @param {(s: string) => void} log
 * @param {string[]} [files] The scan scope. Defaults to the shared repo file list; a test supplies its own
 *   so the seam stays pure — `repoFiles` shells out to git, and a gate that can only be exercised by
 *   spawning a process is the thing GATES.md's "test THAT" rule exists to prevent.
 * @returns {number} problem count; 0 = clean.
 */
export function checkOptions(repo, config, log, files = null) {
  const rows = collectOptions(repo, files ?? repoFiles(repo, ['src']));

  // FAIL CLOSED. A scan that found no options at all has not proved the tree clean — it has proved the
  // pattern stopped matching, which is what a silently-retired syntax or a broken scope list looks like.
  if (rows.length === 0) {
    log('check-options: ✗ BROKEN GATE — scanned src/ and found no settable options at all; the pattern or '
      + 'the file list stopped matching. This is not a clean tree.');
    return 1;
  }

  const allowances = config.optionDocAllowances ?? [];
  const key = (r) => `${r.type}.${r.prop}`;
  const allowed = new Map(allowances.map((a) => [`${a.type}.${a.prop}`, a]));

  const undocumented = rows.filter((r) => r.docLines === 0 && !allowed.has(key(r)));
  const used = new Set(rows.filter((r) => r.docLines === 0).map(key));

  // A DEAD ALLOWANCE MUST FAIL — an entry that cannot expire is one that silently stops covering what it
  // was written for. An allowance whose option is now documented, renamed or deleted is exactly that.
  const stale = allowances.filter((a) => !used.has(`${a.type}.${a.prop}`));

  for (const r of undocumented)
    log(`  ${r.file}:${r.line}  ${r.type}.${r.prop} — no XML doc; a consumer sets this from IntelliSense`);
  for (const a of stale)
    log(`  allowance for ${a.type}.${a.prop} no longer matches an undocumented option — remove it`);

  const problems = undocumented.length + stale.length;
  if (problems > 0) {
    log(`check-options: ✗ ${undocumented.length} undocumented option(s), ${stale.length} dead allowance(s)`);
    log('');
    log('  Say what the option IS and WHY it exists — what it buys, and what its default costs. Which');
    log('  model to run and which option to select is the consuming application\'s job; documenting the');
    log('  choice is the library\'s. A one-line doc is accepted here: this gate only catches NOTHING.');
    log('  An option that genuinely needs no prose takes an `optionDocAllowances` entry with a reason,');
    log('  and that entry FAILS the moment it stops matching.');
    return problems;
  }

  const thin = rows.filter((r) => r.docLines === 1).length;
  log(`check-options: ${rows.length} settable option(s) across ${new Set(rows.map((r) => r.type)).size} `
    + `shipped type(s) all documented ✓ (${thin} with a one-line doc — reported, not gated)`);
  return 0;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2), because the argv fallback compares resolved paths and any way
// that comparison can be wrong makes the guard silently do NOTHING and exit 0.
//
// It used two weaker forms until 2026-09-16, and it was the ONE gate with no fact in `cli-entry.test.mjs`
// — which is not a coincidence, it is why nobody looked. The basename comparison
// (`path.basename(process.argv[1]) === 'check-options.mjs'`) matched any script of that name anywhere, and
// the repo path came from `new URL(import.meta.url).pathname.replace(/^\//, '')`, which is the hand-rolled
// `file://` decode `fileURLToPath` exists to replace — it leaves a percent-escape in any path containing a
// space, in a repository named 灵台.
const here = fileURLToPath(import.meta.url);
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const repo = path.resolve(path.dirname(here), '..', '..');
  const { default: config } = await import('../project.config.mjs');
  process.exitCode = checkOptions(repo, config, (s) => console.log(s)) === 0 ? 0 : 1;
}
