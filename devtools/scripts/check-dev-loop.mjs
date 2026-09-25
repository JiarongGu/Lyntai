// check-dev-loop — the `## Dev loop` command table in `CLAUDE.md`, GENERATED from the command roster.
//
// The names and the `verify` column come from `devtools/commands.mjs` (imported, never parsed out of a
// source file); only each description is authored, in `devLoopCommands` in `devtools/project.config.mjs`.
// Why the table is derived, and what drifted when it was not: `docs/GATES.md` §check-dev-loop.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { COMMANDS, VERIFY_STEPS } from '../commands.mjs';
import { anchorProblems, cell, regenerate } from './_markers.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

export const BEGIN = '<!-- dev-loop:begin';
export const END = '<!-- dev-loop:end -->';

/** The file the table lives in. Its own constant so a test can point the gate at a fixture. */
export const TARGET = 'CLAUDE.md';

/** The shipped roster; a test passes its own. */
export const ROSTER = { commands: COMMANDS, steps: VERIFY_STEPS };

/** Every command name, in roster order, and the set `verify` runs. */
export function commandRoster({ commands, steps } = ROSTER) {
  return { names: [...new Set(commands.map((c) => c.name))], inVerify: new Set(steps.map(([name]) => name)) };
}

/** The table body, in roster order. */
export function renderTable(roster, descriptions) {
  const { names, inVerify } = commandRoster(roster);
  return [
    '| command | `verify` | what it does |',
    '| --- | :---: | --- |',
    ...names.map((n) => `| \`${n}\` | ${inVerify.has(n) ? '✓' : ''} | ${cell(descriptions[n] ?? '', 104)} |`),
  ];
}

/**
 * Problems with the REGISTRY itself, as message strings — empty when every command has exactly one entry.
 *
 * Both directions fail: an undocumented command renders a blank cell that reads like a command doing
 * nothing, and an entry naming no command is a dead registry entry that cannot expire. A `verify` step
 * naming no command would run nothing and fail only at run time, so it fails here.
 */
export function registryProblems(names, descriptions, inVerify = new Set()) {
  const known = new Set(names);
  const missing = names.filter((n) => !descriptions[n]?.trim());
  const dead = Object.keys(descriptions).filter((k) => !known.has(k));
  const unknownSteps = [...inVerify].filter((s) => !known.has(s));
  const problems = [];
  if (missing.length) problems.push(`${missing.length} command(s) with no \`devLoopCommands\` entry: ${missing.join(', ')}`);
  if (dead.length) problems.push(`${dead.length} \`devLoopCommands\` entr(ies) naming no command: ${dead.join(', ')}`);
  if (unknownSteps.length) problems.push(`\`verify\` runs ${unknownSteps.length} step(s) naming no command: ${unknownSteps.join(', ')}`);
  return problems;
}

export function checkDevLoop(repo, config, log = console.log, write = false, roster = ROSTER) {
  const descriptions = config.devLoopCommands ?? {};
  const file = path.join(repo, TARGET);
  if (!fs.existsSync(file)) {
    log(`check-dev-loop: ✗ ${TARGET} is missing — nothing was checked, so this gate proves nothing`);
    return 1;
  }

  const { names, inVerify } = commandRoster(roster);
  if (names.length === 0) {
    log('check-dev-loop: ✗ the command roster is empty — check devtools/commands.mjs');
    return 1;
  }

  const problems = registryProblems(names, descriptions, inVerify);
  if (problems.length) {
    log('check-dev-loop: ✗ the command registry disagrees with the roster');
    for (const p of problems) log(`  ${p}`);
    log('');
    log('  `devLoopCommands` in devtools/project.config.mjs is the only authored half — the names and the');
    log('  `verify` column come from devtools/commands.mjs. Add the missing line, or delete the entry for a');
    log('  command that is gone.');
    return 1;
  }

  const text = fs.readFileSync(file, 'utf8');
  const anchors = anchorProblems(text.split(/\r?\n/), BEGIN, END);
  if (anchors.length) {
    log(`check-dev-loop: ✗ ${TARGET}'s generated block is not a clean single anchor pair`);
    for (const p of anchors) log(`  ${p}`);
    return 1;
  }

  const outcome = regenerate(text, () => renderTable(roster, descriptions), BEGIN, END,
    write ? (next) => fs.writeFileSync(file, next, 'utf8') : null);
  if (outcome === 'missing') {
    log(`check-dev-loop: ✗ ${TARGET} has no \`${BEGIN} … ${END}\` block to generate into`);
    return 1;
  }
  if (outcome === 'stale') {
    log(`check-dev-loop: ✗ ${TARGET}'s command table is stale`);
    log('');
    log('  It is GENERATED from devtools/commands.mjs plus `devLoopCommands`. Run `node devtools/dev.mjs');
    log('  check-dev-loop --write` rather than editing the table — a hand-edit is what this gate exists to');
    log('  catch, and it fails again on the next run.');
    return 1;
  }
  if (write) log(`check-dev-loop: regenerated the command table in ${TARGET} — ${names.length} command(s)`);

  log(`check-dev-loop: ${names.length} command(s) documented, ${inVerify.size} of them in \`verify\` ✓`);
  return 0;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkDevLoop(repoDefault, config, console.log, process.argv.includes('--write'));
}
