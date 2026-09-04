// check-archive — FAIL when an archive entry outgrows the OUTCOME it records.
//
// The rule is `.claude/rules/task-lifecycle.md` §"An archive entry is an OUTCOME and a POINTER": what the
// task DID, what it decided, and where the detail lives. That rule was written from a measurement on
// 2026-09-02 — mean non-blank lines per entry across the file in thirds, 8.0 → 6.1 → 22.2 — and answered
// with prose alone.
//
// IT KEPT GROWING. Re-measured 2026-09-04: 7.5 → 6.7 → 23.2 over 142 entries, a 3.1× spread where the rule
// had recorded 2.8×. A written-down rule that is still violated is a MISSING GATE, not a knowledge problem
// — the same reasoning that produced `check-encoding` and `check-links`.
//
// THE LIMIT IS 20 non-blank body lines, deliberately loose against the rule's own "roughly ten lines does
// that". The median entry is already 10 and the third quartile 16, so the median complies and the whole
// weight is in the tail: this catches outliers rather than re-litigating typical entries.
//
// A RATCHET, NOT A THRESHOLD, for the reason `check-decisions` and `check-comments` both record — entries
// were already over when it landed, so a plain threshold would be switched off on day one. Allowances live
// in `archiveEntryLengthAllowances` and can only come down.
//
// WHAT PAYING ONE DOWN MEANS, and it differs from `check-decisions`: a decision's reasoning IS its payload,
// while an archive entry's detail belongs to whichever record owns it. The cost being removed here is
// DUPLICATION — a measurement narrative living in both the entry and `docs/memory.md` §5 means a retraction
// has to edit two places, and the day that was measured only one of them got edited. Strike every sentence
// a reader could get from the document that owns it; what survives is the entry.
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { entriesIn as entriesUnder, runRatchet } from './_entry-length.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The limit an entry may reach before it has stopped being an outcome and become a write-up. */
export const MAX_ENTRY = 20;

/** The record this gate reads. */
export const RECORD = 'docs/task-archive.md';

// `## Part <n>`, and deliberately NOT `## Phase <n>`: the eight Phase sections are the original
// implementation plan and sit above the first Part, so they are header rather than entries. Were one ever
// written below a Part, its body would be attributed to that Part — which is why the id is anchored.
const HEADING = /^## (Part \d+)(?!\d)/;

/** Every archive entry, as `{ id, line, length, title }`. See `_entry-length.mjs`. */
export const entriesIn = (text) => entriesUnder(text, HEADING);

/** Every entry past the limit, worst first — the unit the ledger records. */
export const overLimitEntries = (text) =>
  entriesIn(text).filter((e) => e.length > MAX_ENTRY).sort((a, b) => b.length - a.length);

export function checkArchive(repo, cfg, log = console.log) {
  return runRatchet({
    repo,
    log,
    allowances: cfg.archiveEntryLengthAllowances ?? {},
    name: 'check-archive',
    record: RECORD,
    heading: HEADING,
    limit: MAX_ENTRY,
    declares: '`## Part <n>`',
    advice: [
      'An archive entry is an OUTCOME and a POINTER: what the task did, what it decided, where the',
      'detail lives. Strike every sentence a reader could get from the record that owns it — the',
      'measurement record, `docs/DECISIONS.md`, `docs/FIXES.md`, `pitfalls.md` — then record what is',
      'left in `archiveEntryLengthAllowances`. There is no escape token.',
    ],
  });
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkArchive(repoDefault, config);
}
