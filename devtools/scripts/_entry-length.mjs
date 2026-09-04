// _entry-length — the RATCHET shared by `check-decisions` and `check-archive`.
//
// Two records grew the same way and the growth was measured the same way, so the semantics live here once:
// a limit, a per-entry allowance ledger, and three failure modes. The leading underscore keeps this out of
// the test runner's discovery, the same trick `_fixtures.mjs` and `e2e/_e2e-common.mjs` use.
//
// THE SUBTLE PART IS THE LEDGER, which is why it is shared rather than mirrored. An allowance is a DEBT and
// never a permission, so three things fail besides being over the limit: an allowance LOOSER than the entry
// now needs (or the next regression back to the old size passes unnoticed), an allowance at or below the
// limit (registry rot — it is doing nothing), and one naming an entry that no longer exists. A second
// hand-written copy of that would drift, and the drift would be silent in the permissive direction.
import fs from 'node:fs';
import path from 'node:path';

/**
 * Every entry under `heading`, as `{ id, line, length, title }`.
 *
 * `length` counts NON-BLANK body lines and excludes the heading. Blank lines are paragraph separators, so
 * counting them would measure formatting rather than prose — and would let an entry be "paid down" by
 * unwrapping its paragraphs without deleting a word. Everything above the first heading is skipped, which
 * keeps a generated index or a file banner out of the measurement: failing an author for its size would
 * name the wrong culprit.
 *
 * @param {string} text The record.
 * @param {RegExp} heading Anchored, with the id in capture group 1.
 */
export function entriesIn(text, heading) {
  const out = [];
  text.split(/\r?\n/).forEach((line, i) => {
    const m = heading.exec(line);
    if (m) { out.push({ id: m[1], line: i + 1, length: 0, title: line.replace(/^##\s*/, '') }); return; }
    if (out.length && line.trim()) out[out.length - 1].length++;
  });
  return out;
}

/**
 * Run the ratchet over one record. Returns 0 or 1 and logs its own verdict.
 *
 * @param {object} spec
 * @param {string} spec.repo Repository root.
 * @param {object} spec.allowances The ledger for this record, `{ [id]: length }`.
 * @param {(msg: string) => void} spec.log Where the verdict goes.
 * @param {string} spec.name The gate's name, for its messages.
 * @param {string} spec.record Repo-relative path.
 * @param {RegExp} spec.heading Anchored, id in capture group 1.
 * @param {number} spec.limit Non-blank body lines an entry may reach.
 * @param {string} spec.declares What a missing parse should say the record declares.
 * @param {string[]} spec.advice Printed under an over-limit list — how to pay one down.
 */
export function runRatchet({ repo, allowances, log, name, record, heading, limit, declares, advice }) {
  let text;
  try { text = fs.readFileSync(path.join(repo, record), 'utf8'); } catch {
    log(`${name}: ✗ ${record} could not be read — a guard that finds nothing to scan indicts the`
      + ' listing, not the tree');
    return 1;
  }

  const entries = entriesIn(text, heading);
  // Fail CLOSED on a record that parses to nothing: a broken pattern must never report a clean file. That is
  // the shape that let `check-sensitive` silently skip every non-ASCII filename.
  if (entries.length === 0) {
    log(`${name}: ✗ ${record} declares no ${declares} entries — a broken parse, not a clean record`);
    return 1;
  }

  const byId = new Map(entries.map((e) => [e.id, e]));
  const over = [];
  const slack = [];

  for (const e of entries) {
    const budget = allowances[e.id];
    if (budget === undefined) {
      if (e.length > limit) over.push({ ...e, budget: limit });
    } else if (e.length > budget) {
      over.push({ ...e, budget });
    } else if (budget > e.length || budget <= limit) {
      slack.push({ id: e.id, allowed: budget, actual: e.length });
    }
  }

  const stale = Object.keys(allowances).filter((id) => !byId.has(id));

  if (over.length === 0 && slack.length === 0 && stale.length === 0) {
    const budgeted = Object.keys(allowances).length;
    const debt = Object.values(allowances).reduce((s, n) => s + Math.max(0, n - limit), 0);
    log(`${name}: ${entries.length} entr(ies) — none over ${limit} non-blank lines ✓ `
      + `(${budgeted} entr(ies) still on a recorded allowance, ${debt} lines of debt above the limit)`);
    return 0;
  }

  if (over.length > 0) {
    log(`${name}: ✗ ${over.length} entr(ies) over their limit\n`);
    for (const o of over) log(`  ${record}:${o.line}  ${o.id} is ${o.length} non-blank lines (limit ${o.budget})`);
    log('');
    for (const line of advice) log(`  ${line}`);
  }

  if (slack.length > 0) {
    log(`\n${name}: ✗ ${slack.length} allowance(s) are now LOOSER than the entry needs — lower them`);
    for (const s of slack) log(`  ${s.id}: allowed ${s.allowed}, the entry is now ${s.actual}`);
    log(`  An allowance is a debt, not a permission; one at or below ${limit} should simply be deleted.`);
  }

  if (stale.length > 0) {
    log(`\n${name}: ✗ ${stale.length} allowance(s) name an entry that does not exist — delete them`);
    for (const id of stale) log(`  ${id}`);
  }

  return 1;
}
