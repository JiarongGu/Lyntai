// check-dev-loop — the `## Dev loop` command table in `CLAUDE.md`, GENERATED from `dev.mjs`.
//
// The third authored-marker/generated-index gate, sharing `_markers.mjs` with `check-backlog` (D111) and
// `check-pitfalls` (D112). Here the "markers" are `devLoopCommands` in `devtools/project.config.mjs`: the
// NAMES and the `verify` column are derived from `dev.mjs`, so only the description is authored.
//
// Why derived and not hand-listed: `dev.mjs`'s own usage string was once a literal, and it drifted to 24 of
// 30 commands — every memory sweep except one, plus a gate the day it was added — so the thing CLAUDE.md
// called "the authoritative list" silently stopped being one. A prose table in the file every session reads
// first is the same defect with a wider blast radius.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { anchorProblems, cell, fixedPoint } from './_markers.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

export const BEGIN = '<!-- dev-loop:begin';
export const END = '<!-- dev-loop:end -->';

/** The file the table lives in. Its own constant so a test can point the gate at a fixture. */
export const TARGET = 'CLAUDE.md';

/**
 * Every command `dev.mjs` dispatches, and which of them `verify` runs — both read out of its SOURCE.
 *
 * Read as text, never imported: `dev.mjs` executes its switch at module scope, so importing it to ask what
 * it contains would run a command. `check-counts` reads the same two shapes for the same reason.
 */
export function commandRoster(repo) {
  const src = fs.readFileSync(path.join(repo, 'devtools', 'dev.mjs'), 'utf8');
  const names = [...new Set([...src.matchAll(/^\s*case '([a-z][a-z0-9-]*)':/gm)].map((m) => m[1]))];
  const steps = src.match(/const steps = \[([\s\S]*?)\];/);
  const inVerify = new Set(steps ? [...steps[1].matchAll(/\['([a-z0-9-]+)',\s*\[/g)].map((m) => m[1]) : []);
  return { names, inVerify };
}

/** The table body, in `dev.mjs`'s own declaration order so the roster reads as the switch reads. */
export function renderTable(repo, commands) {
  const { names, inVerify } = commandRoster(repo);
  return [
    '| command | `verify` | what it does |',
    '| --- | :---: | --- |',
    ...names.map((n) => `| \`${n}\` | ${inVerify.has(n) ? '✓' : ''} | ${cell(commands[n] ?? '', 104)} |`),
  ];
}

/**
 * Problems with the REGISTRY itself, as message strings — empty when every command has exactly one entry.
 *
 * Both directions fail. An undocumented command would render a blank cell that reads like a command doing
 * nothing; an entry naming no command is the dead-registry-entry shape every registry here carries, and
 * without it the table quietly stops covering what it was written for.
 */
export function registryProblems(names, commands) {
  const known = new Set(names);
  const missing = names.filter((n) => !commands[n]?.trim());
  const dead = Object.keys(commands).filter((k) => !known.has(k));
  const problems = [];
  if (missing.length) problems.push(`${missing.length} command(s) with no \`devLoopCommands\` entry: ${missing.join(', ')}`);
  if (dead.length) problems.push(`${dead.length} \`devLoopCommands\` entr(ies) naming no command: ${dead.join(', ')}`);
  return problems;
}

export function checkDevLoop(repo, config, log = console.log, write = false) {
  const commands = config.devLoopCommands ?? {};
  const file = path.join(repo, TARGET);
  if (!fs.existsSync(file)) {
    log(`check-dev-loop: ✗ ${TARGET} is missing — nothing was checked, so this gate proves nothing`);
    return 1;
  }

  const { names } = commandRoster(repo);
  if (names.length === 0) {
    log('check-dev-loop: ✗ read no commands out of devtools/dev.mjs — check the `case` convention');
    return 1;
  }

  const problems = registryProblems(names, commands);
  if (problems.length) {
    log('check-dev-loop: ✗ the command registry disagrees with `dev.mjs`');
    for (const p of problems) log(`  ${p}`);
    log('');
    log('  `devLoopCommands` in devtools/project.config.mjs is the only authored half — the names and the');
    log('  `verify` column are derived. Add the missing line, or delete the entry for a command that is gone.');
    return 1;
  }

  const text = fs.readFileSync(file, 'utf8');
  const lines = text.split(/\r?\n/);
  const anchors = anchorProblems(lines, BEGIN, END);
  if (anchors.length) {
    log(`check-dev-loop: ✗ ${TARGET}'s generated block is not a clean single anchor pair`);
    for (const p of anchors) log(`  ${p}`);
    return 1;
  }

  const next = fixedPoint(text, () => renderTable(repo, commands), BEGIN, END);
  if (next === null) {
    log(`check-dev-loop: ✗ ${TARGET} has no \`${BEGIN} … ${END}\` block to generate into`);
    return 1;
  }

  if (write) {
    if (next !== text) fs.writeFileSync(file, next, 'utf8');
    log(`check-dev-loop: regenerated the command table in ${TARGET} — ${names.length} command(s)`);
  } else if (next !== text.split(/\r?\n/).join('\n')) {
    log(`check-dev-loop: ✗ ${TARGET}'s command table is stale`);
    log('');
    log('  It is GENERATED from `dev.mjs` plus `devLoopCommands`. Run `node devtools/dev.mjs');
    log('  check-dev-loop --write` rather than editing the table — a hand-edit is what this gate exists to');
    log('  catch, and it fails again on the next run.');
    return 1;
  }

  const gated = commandRoster(repo).inVerify.size;
  log(`check-dev-loop: ${names.length} command(s) documented, ${gated} of them in \`verify\` ✓`);
  return 0;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkDevLoop(repoDefault, config, console.log, process.argv.includes('--write'));
}
