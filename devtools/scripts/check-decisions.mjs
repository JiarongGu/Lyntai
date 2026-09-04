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
//
// The ratchet itself is `_entry-length.mjs`, shared with `check-archive` — same ledger semantics over a
// different record, and the ledger is the half that would drift if it were copied.
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { entriesIn as entriesUnder, runRatchet } from './_entry-length.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The limit an entry may reach before something other than the decision has leaked in. */
export const MAX_ENTRY = 35;

/** The record this gate reads. */
export const RECORD = 'docs/DECISIONS.md';

const HEADING = /^## (D\d+)(?![0-9])/;

/** Every decision entry, as `{ id, line, length, title }`. See `_entry-length.mjs`. */
export const entriesIn = (text) => entriesUnder(text, HEADING);

/** Every entry past the limit, worst first — the unit the ledger records. */
export const overLimitEntries = (text) =>
  entriesIn(text).filter((e) => e.length > MAX_ENTRY).sort((a, b) => b.length - a.length);

export function checkDecisions(repo, cfg, log = console.log) {
  return runRatchet({
    repo,
    log,
    allowances: cfg.decisionLengthAllowances ?? {},
    name: 'check-decisions',
    record: RECORD,
    heading: HEADING,
    limit: MAX_ENTRY,
    declares: '`## D<n>`',
    advice: [
      'A decision entry states the decision, the alternatives, and what it constrains.',
      'Move MEASUREMENT narrative to the design record that owns it and AMENDMENT narrative to git',
      'history — then record what is left in `decisionLengthAllowances`. There is no escape token.',
    ],
  });
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkDecisions(repoDefault, config);
}
