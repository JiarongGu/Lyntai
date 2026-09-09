// check-backlog — FAIL when the OPEN backlog starts summarizing the archive.
//
// `.claude/rules/task-lifecycle.md` states the rule twice and in both directions: the backlog holds open
// work only, and it must "never let the backlog SUMMARIZE the archive". `TASKS.md` violated it anyway.
//
// MEASURED 2026-09-10, and the shape is the reason this is a gate rather than a fourth restatement of the
// rule: the `## Active backlog` preamble had reached **478 lines carrying ZERO open checkboxes** — five
// stacked `HANDOVER` blocks plus a running tally of what had closed — inside a 1308-line file holding 17
// open items. The file had even RECORDED deleting a 49-line tally for this exact reason on 2026-09-03, and
// then regrew a 19-line one in the same place. A rule that is written down, obeyed once, and violated again
// is a missing gate — the same argument behind `check-encoding`, `check-links` and `check-archive`.
//
// TWO CHECKS, both anchored on what was actually measured rather than on taste:
//   1. the PREAMBLE — the narrative above the blocked roster — has a non-blank line budget, and
//   2. no `HANDOVER` block survives in the file at all — a handover describes work that is DONE, so its
//      home is `docs/task-archive.md`, one Part per task.
//
// The preamble budget is a RATCHET, like its three siblings: `backlogPreambleAllowance` can come down and
// never up. There is no escape token, for the reason `check-decisions` carries — an allowance is a visible
// number and is the only way out.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The record this gate reads. */
export const RECORD = 'TASKS.md';

/** Non-blank lines the preamble may occupy before it has stopped being a banner. */
export const MAX_PREAMBLE = 40;

/** A handover block. Kept broad — the defect is the BLOCK, whatever it is dressed as. */
const HANDOVER = /^\s*(?:_|\*)*\s*\*\*HANDOVER\b/i;

// Where the preamble ends. The BLOCKED roster is structured backlog data — which Parts cannot be started
// and on what — so it is open-backlog content and charging it to a "the banner is too long" budget would
// fire the day somebody adds a blocked Part, which is a false alarm and the fastest way to teach a reader
// to raise the allowance without looking. The subject here is the NARRATIVE above it.
const PREAMBLE_END = /^## Part \d|^Blocked, and on what:/;

/**
 * The preamble's non-blank line count, and every handover line, as
 * `{ preamble, handovers: [{ line, text }], openItems }`.
 *
 * Counted from the `## Active backlog` heading rather than from the file's top, so the file's own purpose
 * statement and goal are not charged to the banner — those are stable and were never the thing that grew.
 */
export function readBacklog(repo) {
  const text = fs.readFileSync(path.join(repo, RECORD), 'utf8');
  const lines = text.split(/\r?\n/);

  const start = lines.findIndex((l) => /^## Active backlog/.test(l));
  const end = lines.findIndex((l, i) => i > start && PREAMBLE_END.test(l));

  const handovers = [];
  lines.forEach((l, i) => {
    if (HANDOVER.test(l)) handovers.push({ line: i + 1, text: l.trim().slice(0, 78) });
  });

  // A preamble that cannot be located is a STRUCTURE change, not a clean run — report it rather than
  // returning 0, which would read as a very short banner and pass.
  const preamble = start < 0 || end < 0
    ? -1
    : lines.slice(start + 1, end).filter((l) => l.trim().length > 0).length;

  return { preamble, handovers, openItems: lines.filter((l) => /^- \[ \]/.test(l)).length };
}

export function checkBacklog(repo, config = {}, log = console.log) {
  const allowance = Number.isInteger(config.backlogPreambleAllowance)
    ? config.backlogPreambleAllowance
    : MAX_PREAMBLE;
  const { preamble, handovers, openItems } = readBacklog(repo);

  if (preamble < 0) {
    log('check-backlog: ✗ could not locate the backlog preamble');
    log(`  Expected a '## Active backlog' heading above the first '## Part <n>' in ${RECORD}.`);
    log('  The gate proves nothing against a structure it cannot read, so this is a FAILURE.');
    return 1;
  }

  const problems = [];
  if (preamble > allowance)
    problems.push(`the preamble is ${preamble} non-blank lines, over its ${allowance}`);
  if (handovers.length > 0)
    problems.push(`${handovers.length} HANDOVER block(s) remain`);

  if (problems.length === 0) {
    const slack = allowance > MAX_PREAMBLE ? ` (allowance ${allowance}, limit ${MAX_PREAMBLE})` : '';
    log(`check-backlog: ${RECORD} preamble ${preamble} line(s) for ${openItems} open item(s), `
      + `no handover blocks ✓${slack}`);
    return 0;
  }

  log(`check-backlog: ✗ ${problems.join('; ')}`);
  for (const h of handovers) log(`  ${RECORD}:${h.line}  ${h.text}`);
  log('');
  log('  The backlog holds OPEN work only and must not summarize the archive');
  log('  (`.claude/rules/task-lifecycle.md`). A handover describes work that is DONE — move it to');
  log('  `docs/task-archive.md`, one Part per task, and leave a POINTER rather than a copy. State');
  log('  where things stand by naming the record that owns it, never by restating it here.');
  log('  If a longer preamble is genuinely earned, record it in `backlogPreambleAllowance`');
  log('  (devtools/project.config.mjs) — it can come down and never up. There is no escape token.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkBacklog(repoDefault, config);
}
