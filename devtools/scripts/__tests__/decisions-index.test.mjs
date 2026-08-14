// decisions-index — the generated index at the top of docs/DECISIONS.md. See scripts/decisions-index.mjs.
//
// What is at stake is small per incident and invisible: a wrong ANCHOR is a link that goes nowhere inside
// the file, which no gate can see and no reader reports — they just scroll. `verify` deliberately does not
// run this (a stale index costs one Ctrl-F), so its own tests are the only thing that ever exercises it.
//
// The script was one top-level block until 2026-08-11, which meant importing it rewrote docs/DECISIONS.md —
// untestable by construction, and the reason this file did not exist sooner.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { END, START, decisionRows, decisionsIndex, indexBlock, short, slug, withIndex } from '../decisions-index.mjs';
import { recorder, repoRoot } from './_fixtures.mjs';

const doc = (...headings) => `# Decisions\n\nSome preamble.\n\n${headings.map((h) => `${h}\n\nBody.\n`).join('\n')}`;

/** Drive the command with both file seams stubbed — the real docs/DECISIONS.md is never touched. */
function run(text, { check = false } = {}) {
  const log = recorder();
  let written = null;
  const code = decisionsIndex({ check, log, error: log, read: () => text, write: (t) => { written = t; } });
  return { code, out: log.text(), written };
}

describe('decisions-index — reading the headings', () => {
  it('parses id, title and date, and ignores everything that is not a decision heading', () => {
    const rows = decisionRows([
      '## D42 — The thing (2026-08-05)',
      '### D43 — a sub-heading, not a decision',
      '## Dx — not a number',
      '## D7 — Undated',
      'D9 — not a heading at all',
    ]);
    assert.deepEqual(rows.map((r) => [r.id, r.title, r.date]),
      [['D42', 'The thing', '2026-08-05'], ['D7', 'Undated', null]]);
    assert.equal(rows[0].n, 42, 'the numeric id is what the sort uses');
  });

  it('accepts an em dash, an en dash or a hyphen after the id', () => {
    const rows = decisionRows(['## D1 — Em', '## D2 – En', '## D3 - Hyphen']);
    assert.deepEqual(rows.map((r) => r.title), ['Em', 'En', 'Hyphen']);
  });

  it('only strips a trailing parenthetical that is really a DATE', () => {
    const [dated, kept] = decisionRows(['## D1 — A (2026-08-05)', '## D2 — B (corrected by D3)']);
    assert.deepEqual([dated.title, dated.date], ['A', '2026-08-05']);
    assert.deepEqual([kept.title, kept.date], ['B (corrected by D3)', null],
      'a parenthetical that is not a date belongs to the title');
  });

  /**
   * `D<n>` is a PERMANENT identifier — 510 references across the repository point at these numbers and
   * nothing gates them — so an overturned decision keeps its number and becomes a redirect stub. Which
   * numbers are stubs is derived, because the hand-maintained roster this replaced had drifted to naming
   * three of the fifteen entries it should have (found 2026-08-14, the audit that produced the rebuild).
   */
  it('reads a supersession off the heading, in both the current and the archived form', () => {
    const rows = decisionRows([
      '## D9 — SUPERSEDED by D14',                                  // the rebuilt stub form
      'Scope was originally fenced narrower.',
      '## D14 — Direction: a platform kit',
      'Live entry.',
      '## D55 — Scope: salience at both layers (SUPERSEDED by D56)', // the archive's parenthesised form
      'History.',
    ]);

    assert.deepEqual(rows.map((r) => r.supersededBy), ['D14', null, 'D56']);
  });

  /**
   * The other direction, and what makes the roster trustworthy rather than merely long: an entry that
   * MENTIONS a supersession in its body is still a live decision. Over-reporting would mislead exactly as
   * badly as the under-reporting this replaced.
   */
  it('does not treat a body mention as a supersession', () => {
    const rows = decisionRows([
      '## D24 — SemVer strictness is deferred',
      'D22 froze the surface. This is SUPERSEDED by nothing; D44 amends one bullet.',
      '## D44 — the amendment',
      'Amends D24.',
    ]);

    assert.deepEqual(rows.filter((r) => r.supersededBy).map((r) => r.id), [],
      'only the heading declares a stub');
  });

  /**
   * A number stops being a live decision in TWO ways, and the roster has to show both: superseded (a later
   * decision overturned it) and reclassified (it was never a decision — a work log, or a trap that belongs
   * in pitfalls). Added 2026-08-14 after an audit found six entries announcing in their own titles that
   * they were a work session.
   */
  it('counts a RECLASSIFIED entry as a stub too, not as a live decision', () => {
    const rows = decisionRows([
      '## D1 — SUPERSEDED by D3',
      '## D2 — RECLASSIFIED: a review pass, not a decision',
      '## D3 — A real choice between alternatives',
    ]);

    assert.deepEqual(rows.map((r) => r.reclassified), [false, true, false]);

    const block = indexBlock(rows);
    assert.match(block, /\*\*1 live decisions\.\*\*/, 'only D3 is live');
    assert.match(block, /\(2\)/);
    assert.match(block, /\[D1\]\(#d1--superseded-by-d3\) → D3/);
    assert.match(block, /\[D2\]\([^)]*\) \(reclassified\)/);
    assert.doesNotMatch(block.split('permanent identifier')[1], /\[D3\]/,
      'a live entry must not appear in the stub roster');
  });

  it('says so when every entry is live', () => {
    assert.match(indexBlock(decisionRows(['## D1 — A', '## D2 — B'])), /_All 2 entries are live decisions\._/);
  });
});

describe('decisions-index — the table it renders', () => {
  it('sorts NUMERICALLY, which the file\'s own order deliberately is not', () => {
    // D1–D28 run oldest-first in the file and everything after runs newest-first; the index must still let
    // an id be found by number.
    const table = indexBlock(decisionRows(['## D9 — Nine', '## D10 — Ten', '## D2 — Two']));
    assert.deepEqual(table.match(/\[D\d+\]/g), ['[D2]', '[D9]', '[D10]'],
      'a lexicographic sort would put D10 before D2');
  });

  it('builds the anchor from the WHOLE heading — id, title and date, punctuation dropped', () => {
    assert.equal(slug({ id: 'D42', title: 'The `IMemoryAgePolicy` seam', date: '2026-08-05' }),
      'd42--the-imemoryagepolicy-seam-2026-08-05');
    assert.equal(slug({ id: 'D7', title: 'No date here', date: null }), 'd7--no-date-here');
    assert.match(slug({ id: 'D8', title: '灵台 and other prose', date: null }), /^d8--灵台-and-other-prose$/,
      'a non-ASCII heading keeps its letters — \\p{L}, not [a-z]');
  });

  it('replaces each space INDIVIDUALLY, the way github-slugger does', () => {
    // Corrected 2026-08-11. This test previously pinned a `\s+` collapse "as it behaves, and see the note",
    // and the note was right to doubt it: github-slugger's source (unpkg.com/github-slugger/index.js) is
    // `.replace(regex, '')` then `.replace(/ /g, '-')` — punctuation REMOVED with its surrounding spaces
    // intact, then each space turned into its own hyphen. So `## D42 — Title` anchors as `#d42--title`,
    // the em dash gone and both its spaces surviving. The collapse produced `#d42-title`, so every
    // generated link in the index resolved to nothing — and had, for all 62 of them, since the index
    // existed. A dead in-document link is an error nowhere, which is why nothing ever reported it.
    assert.equal(slug({ id: 'D1', title: 'a  b', date: null }), 'd1--a--b',
      'two spaces must survive as two hyphens, not collapse to one');
    assert.equal(slug({ id: 'D42', title: 'Title', date: null }), 'd42--title',
      'the em dash this generator inserts is dropped and BOTH its spaces become hyphens');
  });

  it('elides a title past 100 characters rather than wrapping the table', () => {
    assert.equal(short('x'.repeat(100)).length, 100);
    assert.equal(short('x'.repeat(200)), `${'x'.repeat(97)}…`);
  });

  it('shows an em dash in the date column for an undated decision', () => {
    assert.match(indexBlock(decisionRows(['## D7 — Undated'])), /\| \[D7\]\(#d7--undated\) \| — \| Undated \|/);
  });
});

describe('decisions-index — writing the block back', () => {
  it('replaces what is between the markers and leaves the rest alone', () => {
    const before = `# Decisions\n\n${START}\n\nstale table\n\n${END}\n\n## D1 — One\n\nBody.\n`;
    const { code, written } = run(before);
    assert.equal(code, 0);
    assert.match(written, /\| \[D1\]\(#d1--one\) \|/);
    assert.doesNotMatch(written, /stale table/);
    assert.ok(written.startsWith('# Decisions\n\n'), 'the preamble survives');
    assert.ok(written.trimEnd().endsWith('Body.'), 'and so does everything after the block');
  });

  it('inserts before the FIRST decision heading on a first run, when there are no markers yet', () => {
    const { written } = run(doc('## D1 — One', '## D2 — Two'));
    assert.ok(written.indexOf(START) < written.indexOf('## D1'), 'the index goes above the first decision');
    assert.match(written, /Some preamble\./);
  });

  it('is idempotent — a second run reports "already current" and writes nothing', () => {
    const first = run(doc('## D1 — One')).written;
    const second = run(first);
    assert.equal(second.written, null, 'nothing written');
    assert.match(second.out, /already current \(1 entries\)/);
  });

  it('preserves CRLF, so a re-run does not rewrite every line of the file', () => {
    const { written } = run(doc('## D1 — One').replaceAll('\n', '\r\n'));
    assert.ok(written.includes(`${START}\r\n`), 'the block itself is CRLF');
    assert.doesNotMatch(written.split(START)[1].slice(0, 200), /[^\r]\n/, 'no bare LF is introduced');
  });
});

describe('decisions-index — the refusals, and --check', () => {
  it('refuses to write an EMPTY index rather than erasing a good one', () => {
    const { code, out, written } = run('# Decisions\n\nNothing here yet.\n');
    assert.equal(code, 1);
    assert.equal(written, null);
    assert.match(out, /refusing to write an empty index/);
  });

  it('fails when there is no insertion point at all', () => {
    // Rows exist but no `\n## D` sequence does — the document opens ON a decision heading.
    const { code, out } = run('## D1 — One');
    assert.equal(code, 1);
    assert.match(out, /could not find an insertion point/);
  });

  it('--check reports staleness and writes NOTHING', () => {
    const { code, out, written } = run(doc('## D1 — One'), { check: true });
    assert.equal(code, 1);
    assert.equal(written, null, '--check must never write — it is the gate form');
    assert.match(out, /OUT OF DATE \(1 entries\)/);
  });

  it('--check passes on a current index', () => {
    const current = run(doc('## D1 — One')).written;
    const { code, out } = run(current, { check: true });
    assert.equal(code, 0);
    assert.match(out, /up to date \(1 entries\) ✓/);
  });
});

describe('decisions-index — against the real document', () => {
  it('parses docs/DECISIONS.md itself, which no synthetic fixture is as hostile as', () => {
    // Deliberately NOT an assertion that the repository's index is current — that is `decisions-index
    // --check`'s job, and a suite that also asserted it would blame the wrong thing (see cli-entry.test.mjs).
    // The real file is used as the FIXTURE: 60+ entries, two opposite orderings, non-ASCII titles and
    // back-ticked identifiers in headings. The parser has to survive all of it.
    const text = readFileSync(join(repoRoot, 'docs', 'DECISIONS.md'), 'utf8');
    const { rows, updated } = withIndex(text);
    assert.ok(rows.length > 40, `expected the real record, got ${rows.length} rows`);
    assert.ok(updated !== null, 'the real document must have somewhere to put its index');
    assert.ok(rows.every((r) => Number.isInteger(r.n) && r.title.length > 0), 'every row is usable');
  });
});
