// check-archive — the archive-entry length gate. See devtools/scripts/check-archive.mjs.
//
// A guard whose failure mode is a false PASS cannot be validated by running it, so every case here is driven
// RED by a synthesized tree before it is trusted. The RATCHET has two negative directions — an entry over
// its budget must fail, AND an allowance looser than the entry needs must fail — and the second is what
// keeps the debt shrinking rather than becoming a permanent permission slip.
//
// The ratchet body is shared with `check-decisions` (`_entry-length.mjs`), so what is tested HERE is what is
// genuinely this gate's own: the heading it recognises, the limit, and that `## Phase` sections are not
// mistaken for entries. Re-testing the shared ledger in both files would be the duplication the shared
// module exists to remove.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  MAX_ENTRY, RECORD, checkArchive, entriesIn, overLimitEntries,
} from '../check-archive.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** An archive entry with `n` non-blank body lines, paragraph-broken so blanks are exercised. */
const entry = (id, n, title = 'a finished task') =>
  [`## ${id} — ${title} (2026-09-04)`, '',
    ...Array.from({ length: n }, (_, i) => (i % 3 === 2 ? `body ${i}\n` : `body ${i}`)),
  ].join('\n');

const record = (...entries) => ['# Archive', '', '> banner prose', '',
  '## Phase 0 — scaffolding', '', 'phase body', '', ...entries].join('\n') + '\n';

function run(text, cfg) {
  const dir = makeTree({ [RECORD]: text });
  const log = recorder();
  try {
    return { code: checkArchive(dir, cfg, log), out: log.text() };
  } finally {
    removeTree(dir);
  }
}

describe('check-archive — finding the entries', () => {
  it('counts NON-BLANK body lines and excludes the heading itself', () => {
    const e = entriesIn(record(entry('Part 1', 4)));
    assert.equal(e.length, 1);
    assert.equal(e[0].length, 4);
  });

  it('does NOT treat a `## Phase` section as an entry, nor fold it into one', () => {
    // The eight Phase sections are the original implementation plan and sit above the first Part. If the
    // heading matched them the banner would be measured as an entry; if it silently swallowed them their
    // body would inflate whichever Part came next.
    const e = entriesIn(record(entry('Part 1', 3)));
    assert.deepEqual(e.map((x) => x.id), ['Part 1']);
    assert.equal(e[0].length, 3, 'the Phase body must not be counted into Part 1');
  });

  it('anchors the number, so `Part 12` and `Part 120` are different entries', () => {
    const e = entriesIn(record(entry('Part 12', 2), entry('Part 120', 3)));
    assert.deepEqual(e.map((x) => x.id), ['Part 12', 'Part 120']);
  });

  it('reports the worst first, which is the order a paydown works in', () => {
    const over = overLimitEntries(record(
      entry('Part 1', MAX_ENTRY + 2), entry('Part 2', MAX_ENTRY + 9), entry('Part 3', 1)));
    assert.deepEqual(over.map((e) => e.id), ['Part 2', 'Part 1']);
  });
});

describe('check-archive — the ratchet', () => {
  it('FAILS an entry over the limit with no allowance', () => {
    const { code, out } = run(record(entry('Part 7', MAX_ENTRY + 1)), {});
    assert.equal(code, 1);
    assert.match(out, /Part 7 is \d+ non-blank lines/);
  });

  it('passes an entry over the limit that records its length as a debt', () => {
    const { code } = run(record(entry('Part 7', MAX_ENTRY + 5)),
      { archiveEntryLengthAllowances: { 'Part 7': MAX_ENTRY + 5 } });
    assert.equal(code, 0);
  });

  it('FAILS an allowance that is LOOSER than the entry now needs, so debt only comes down', () => {
    const { code, out } = run(record(entry('Part 7', MAX_ENTRY + 2)),
      { archiveEntryLengthAllowances: { 'Part 7': MAX_ENTRY + 9 } });
    assert.equal(code, 1);
    assert.match(out, /LOOSER than the entry needs/);
  });

  it('FAILS an allowance at or below the limit — it is doing nothing, which is registry rot', () => {
    const { code, out } = run(record(entry('Part 7', 3)),
      { archiveEntryLengthAllowances: { 'Part 7': MAX_ENTRY } });
    assert.equal(code, 1);
    assert.match(out, /LOOSER than the entry needs|should simply be deleted/);
  });

  it('FAILS an allowance naming an entry that no longer exists', () => {
    const { code, out } = run(record(entry('Part 7', 2)),
      { archiveEntryLengthAllowances: { 'Part 999': 40 } });
    assert.equal(code, 1);
    assert.match(out, /does not exist/);
  });
});

describe('check-archive — failing closed', () => {
  it('FAILS a record that parses to no entries rather than reporting it clean', () => {
    // The shape that let check-sensitive silently skip every non-ASCII filename: a broken pattern must
    // never look like a clean file.
    const { code, out } = run('# Archive\n\nno parts here at all\n', {});
    assert.equal(code, 1);
    assert.match(out, /declares no/);
  });

  it('FAILS when the record cannot be read', () => {
    const dir = makeTree({ 'docs/other.md': 'x' });
    const log = recorder();
    try {
      assert.equal(checkArchive(dir, {}, log), 1);
      assert.match(log.text(), /could not be read/);
    } finally {
      removeTree(dir);
    }
  });
});

describe('check-archive — against the real record', () => {
  it('the shipped archive is green under the shipped ledger', async () => {
    // The gate is only meaningful if it actually runs over the real file — a ledger that had gone stale
    // would fail here rather than in someone's next commit.
    const config = (await import('../../project.config.mjs')).default;
    const log = recorder();
    assert.equal(checkArchive(repo, config, log), 0, log.text());
  });

  it('every allowance names a Part that is really in the record', async () => {
    const config = (await import('../../project.config.mjs')).default;
    const ids = new Set(entriesIn(fs.readFileSync(path.join(repo, RECORD), 'utf8')).map((e) => e.id));
    for (const id of Object.keys(config.archiveEntryLengthAllowances ?? {}))
      assert.ok(ids.has(id), `${id} is budgeted but not in ${RECORD}`);
  });
});
