// check-archive — FAIL when an archive entry outgrows the OUTCOME it records.
//
// The rule, and the measurements that argued for gating it rather than writing it down again, are
// `.claude/rules/task-lifecycle.md` §"An archive entry is an OUTCOME and a POINTER". In short: an entry says
// what the task DID, what it decided and where the detail lives, so what a paydown removes is DUPLICATION —
// unlike `check-decisions`, where the reasoning IS the payload.
//
// THE LIMIT IS 20 non-blank body lines, deliberately loose against that rule's own "roughly ten". A RATCHET
// rather than a threshold, for the reason `check-decisions` and `check-comments` both record; allowances
// live in `archiveEntryLengthAllowances` and can only come down. No escape token.
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
