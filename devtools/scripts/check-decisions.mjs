// check-decisions — FAIL when a decision entry outgrows the decision.
//
// The rule, and the argument for it, are `docs/DECISIONS.md` D96. In short: entry length in that record
// tripled — mean non-blank lines 11.6 → 14.2 → 38.5 across the log's three thirds — and the growth is
// PROSE, not data: amendment narrative written in place, and several decisions stacked under one number.
//
// THE LIMIT IS 35 non-blank body lines. The median entry is 16 and the third quartile is 35, so an entry at
// twice the typical length is carrying something that is not the decision.
//
// A RATCHET, NOT A THRESHOLD. 21 entries were already over the limit when this landed, 251 lines above it,
// so a gate that simply failed would have to be switched off on day one — the lesson `check-comments` paid
// for with 69 blocks. Each over-limit entry records its current length in `decisionLengthAllowances`; an
// allowance LOOSER than the entry needs FAILS, and so does one at or below the limit, so the numbers can
// only ever come down.
//
// NO ESCAPE TOKEN, deliberately (D96) — an allowance is a visible ratcheted number, where an escape would
// remove the subject from measurement entirely.
//
// NOT the archive's compression: a decision's reasoning IS its payload. Paying an entry down moves
// MEASUREMENT narrative to the design record that owns it and AMENDMENT narrative to git history.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The limit an entry may reach before something other than the decision has leaked in. */
export const MAX_ENTRY = 35;

/** The record this gate reads. */
export const RECORD = 'docs/DECISIONS.md';

const HEADING = /^## (D\d+)(?![0-9])/;

/**
 * Every decision entry, as `{ id, line, length, title }`.
 *
 * `length` counts NON-BLANK body lines and excludes the heading. Blank lines are paragraph separators, so
 * counting them would measure formatting rather than prose — and would let an entry be "paid down" by
 * unwrapping its paragraphs without deleting a word. Everything above the first heading is skipped, which
 * is what keeps the generated index table out of the measurement: `dev.mjs decisions-index` rewrites it, so
 * failing an author for its size would name the wrong culprit.
 */
export function entriesIn(text) {
  const out = [];
  text.split(/\r?\n/).forEach((line, i) => {
    const m = HEADING.exec(line);
    if (m) { out.push({ id: m[1], line: i + 1, length: 0, title: line.replace(/^##\s*/, '') }); return; }
    if (out.length && line.trim()) out[out.length - 1].length++;
  });
  return out;
}

/** Every entry past the limit, worst first — the unit the ledger records. */
export const overLimitEntries = (text) =>
  entriesIn(text).filter((e) => e.length > MAX_ENTRY).sort((a, b) => b.length - a.length);

export function checkDecisions(repo, cfg, log = console.log) {
  let text;
  try { text = fs.readFileSync(path.join(repo, RECORD), 'utf8'); } catch {
    log(`check-decisions: ✗ ${RECORD} could not be read — a guard that finds nothing to scan indicts the`
      + ' listing, not the tree');
    return 1;
  }

  const entries = entriesIn(text);
  // Fail CLOSED on a record that parses to nothing: a broken pattern must never report a clean file. That is
  // the shape that let `check-sensitive` silently skip every non-ASCII filename.
  if (entries.length === 0) {
    log(`check-decisions: ✗ ${RECORD} declares no \`## D<n>\` entries — a broken parse, not a clean record`);
    return 1;
  }

  const allowances = cfg.decisionLengthAllowances ?? {};
  const byId = new Map(entries.map((e) => [e.id, e]));
  const over = [];
  const slack = [];

  for (const e of entries) {
    const budget = allowances[e.id];
    if (budget === undefined) {
      if (e.length > MAX_ENTRY) over.push({ ...e, budget: MAX_ENTRY });
    } else if (e.length > budget) {
      over.push({ ...e, budget });
    } else if (budget > e.length || budget <= MAX_ENTRY) {
      // An allowance is a DEBT, not a permission. Once the entry improves the number must come down, or the
      // next regression back to the old size passes unnoticed — and an allowance at or below the limit is
      // doing nothing at all, so it is registry rot either way.
      slack.push({ id: e.id, allowed: budget, actual: e.length });
    }
  }

  const stale = Object.keys(allowances).filter((id) => !byId.has(id));

  if (over.length === 0 && slack.length === 0 && stale.length === 0) {
    const budgeted = Object.keys(allowances).length;
    const debt = Object.values(allowances).reduce((s, n) => s + Math.max(0, n - MAX_ENTRY), 0);
    log(`check-decisions: ${entries.length} entr(ies) — none over ${MAX_ENTRY} non-blank lines ✓ `
      + `(${budgeted} entr(ies) still on a recorded allowance, ${debt} lines of debt above the limit)`);
    return 0;
  }

  if (over.length > 0) {
    log(`check-decisions: ✗ ${over.length} decision entr(ies) over their limit\n`);
    for (const o of over) log(`  ${RECORD}:${o.line}  ${o.id} is ${o.length} non-blank lines (limit ${o.budget})`);
    log('');
    log('  A decision entry states the decision, the alternatives, and what it constrains.');
    log('  Move MEASUREMENT narrative to the design record that owns it and AMENDMENT narrative to git');
    log('  history — then record what is left in `decisionLengthAllowances`. There is no escape token.');
  }

  if (slack.length > 0) {
    log(`\ncheck-decisions: ✗ ${slack.length} allowance(s) are now LOOSER than the entry needs — lower them`);
    for (const s of slack) log(`  ${s.id}: allowed ${s.allowed}, the entry is now ${s.actual}`);
    log(`  An allowance is a debt, not a permission; one at or below ${MAX_ENTRY} should simply be deleted.`);
  }

  if (stale.length > 0) {
    log(`\ncheck-decisions: ✗ ${stale.length} allowance(s) name an entry that does not exist — delete them`);
    for (const id of stale) log(`  ${id}`);
  }

  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkDecisions(repoDefault, config);
}
