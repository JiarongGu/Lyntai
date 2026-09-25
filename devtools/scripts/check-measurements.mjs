// check-measurements — FAIL when the measurement record cannot be read for CURRENCY.
//
// MEASURED 2026-09-10, by five instrumented cold-start probes. Two of the five answers a fresh session
// produced were not current, and the worst was "what is the best memory configuration": ~595 lines read,
// 55% of them wasted, and the answer **unverifiable by construction** — because the measurement half of
// `docs/memory.md` was 3,442 of that file's 4,222 lines and it RETRACTS INLINE. A figure published in one
// section is corrected three sections down, mid-paragraph, with nothing at the top of the file saying so.
//
// HEADING-LEVEL INDEXING IS THE FIX THAT LOOKS SUFFICIENT AND IS NOT, and that is why this exists rather
// than a table of contents. A heading-grep is blind to a mid-section retraction by construction, so the
// index is at RESULT-ROW granularity: a section carries a second marker whenever it holds a result whose
// currency differs from its heading's.
//
// `SUPERSEDED` IS NEVER AUTHORED. It is derived from another row's `supersedes=`, because two-sided
// bookkeeping is exactly where this rots — the losing half of the pair is the half nobody revisits. An
// author writes CURRENT or RETRACTED and names what a new result replaces; the index computes the rest.
//
// SIX CHECKS: every section carries a result marker or declares itself result-free; every marker is
// well-formed and complete; every `metric=` is in a closed vocabulary (`measurementMetrics` in
// `devtools/project.config.mjs`) and every vocabulary value is USED; every id is unique, every
// `supersedes=` resolves, and none of them forms a cycle; a section HEADING that announces a retraction
// while every result under it still reads CURRENT FAILS; and the index equals what the markers say.
// `--write` regenerates it. Scanning BODIES for that vocabulary was built, measured and refused — see
// `RETRACTION` for the 63-hits-zero-defects reason.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  anchorProblems, carriedEscapes, cell, escapeComments, fixedPoint, markerPattern, parseAttributes,
  regenerate, scanMarkers,
} from './_markers.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The record this gate reads. */
export const RECORD = 'docs/memory-measurements.md';

/** Every attribute a result marker may carry. `supersedes` is the only optional one. */
export const ATTRS = ['id', 'arm', 'metric', 'n', 'value', 'ships', 'status', 'supersedes'];
export const REQUIRED = ATTRS.filter((a) => a !== 'supersedes');

/** Whether the arm measured IS the library's shipped default. The first question a probe asked. */
export const SHIPS = ['yes', 'no'];

/**
 * The states an author may WRITE. `SUPERSEDED` is deliberately not among them — see the header.
 *
 * RETRACTED is the stronger claim and wins over a derived supersession: a result that was withdrawn is
 * withdrawn whether or not something replaced it.
 */
export const STATUSES = ['CURRENT', 'RETRACTED'];

/** The derived state a row lands in when a LATER row names it in `supersedes=`. */
export const DERIVED = 'SUPERSEDED';

/**
 * The vocabulary of a retraction — matched against a section's HEADING, and deliberately nowhere else.
 *
 * SCANNING BODIES WAS BUILT, MEASURED AND REFUSED, and the measurement is why this is one line rather than
 * the obvious one. Over the real record the body scan flagged **63 lines across 18 results and found ZERO
 * defects**: `stale@k` and `current@k` are METRIC NAMES here, "the superseded fact" is the PHENOMENON the
 * knowledge-update workload measures, and `correction` is a corpus class in `memory-density`. Restricting
 * it to CAPITALS did not help — the surviving hits were a table row reading `| the SUPERSEDED fact |` and a
 * heading asking *"Does a CORRECTION separate from a RECURRENCE?"*. That is the failure
 * `.claude/knowledge/pitfalls.md` records: a gate whose false positives are legitimate authorial choices
 * cannot be tightened into usefulness, only given an exclusion list nobody can see rot — so reach for a
 * REGISTRY whenever "wrong" depends on intent. The registry here is `status=` and `supersedes=`.
 *
 * A HEADING is the exception because it is a short, deliberate, authored claim rather than prose. Measured
 * on the same record: 5 of 57 flagged, 4 genuine and 1 needing an escape.
 */
// `SUPERSESSION` is deliberately absent. In this record it is the NOUN FOR THE PHENOMENON the
// knowledge-update workload measures — "decay on is what adds supersession" — and never the act of
// retracting a figure, so every occurrence is a false positive. Removing the word beats escaping the line:
// an escape says "this instance is fine" where the truth is that every instance is.
export const RETRACTION =
  /\b(?:CORRECT(?:ED|ION)S?|RETRACT(?:ED|ION|ING|S)?|STALE|SUPERSED(?:ED|ES|ING)|WITHDRAWN)\b/i;

/** This gate's OWN escape token — never `drift-ok`, `link-ok` or `count-ok`, so one cannot silence two. */
export const ESCAPE = 'measure-ok';

export const BLOCK_BEGIN = '<!-- results:begin';
export const BLOCK_END = '<!-- results:end -->';

const MARKER = markerPattern('result');
const FREE = markerPattern('result-free');
const HEADING = /^(#{3,4}) (.+)$/;
const ID = /^[a-z0-9][a-z0-9-]{0,47}$/;

/**
 * Every result the file declares — `{ rows, sections, problems }`.
 *
 * The generated block (whose rows quote the values they came from), fenced code, an unclosed fence and a
 * marker too broken to match are `scanMarkers`' (`_markers.mjs`).
 *
 * A section may carry MORE THAN ONE marker, and that is the mechanism rather than a nicety: this record
 * retracts inline, so one section holds a live result and a dead one, and only a second marker lets the
 * index show them apart.
 */
export function parseResults(lines, vocab = []) {
  const rows = [];
  const sections = [];
  const { visible, problems } = scanMarkers(lines, { name: 'result', begin: BLOCK_BEGIN, end: BLOCK_END, noun: 'result' });
  let section = null;

  const at = (line, why) => problems.push({ line, why });

  visible.forEach(({ i, raw, marker, broken }) => {
    const line = i + 1;
    const h = HEADING.exec(raw);
    if (h) {
      section = { line, level: h[1].length, title: h[2].replace(MARKER, '').replace(FREE, '').trim(), rows: 0, free: null };
      sections.push(section);
    }

    const free = FREE.exec(raw);
    if (free) {
      if (!section) at(line, 'a `result-free:` marker sits outside any section heading');
      else if (section.free !== null) at(line, 'two `result-free:` markers in one section');
      else if (!free[1].trim()) at(line, '`result-free:` needs a REASON — a bare exemption cannot be reviewed');
      else section.free = free[1].trim();
    }

    if (broken || !marker) return;
    if (!section) { at(line, 'a `result:` marker sits above the first section heading'); return; }
    section.rows++;

    const { attrs, residue } = parseAttributes(marker[1]);
    if (residue)
      at(line, `stray text \`${residue.slice(0, 40)}\` in the marker — a value containing spaces must be `
        + 'QUOTED, or everything after the first word is silently dropped');
    for (const key of attrs.keys())
      if (!ATTRS.includes(key)) at(line, `unknown attribute \`${key}\` — a result carries ${ATTRS.join('/')}`);
    for (const key of REQUIRED)
      if (!(attrs.get(key) ?? '').trim())
        at(line, `no \`${key}=\` — every result names all of ${REQUIRED.join('/')}, or it cannot be read `
          + 'from the index without opening the section');

    const id = attrs.get('id') ?? '';
    if (id && !ID.test(id))
      at(line, `id \`${id}\` is not a short kebab-case slug — it is the handle another row supersedes`);

    const metric = attrs.get('metric') ?? '';
    if (metric && !vocab.includes(metric))
      at(line, `unknown metric \`${metric}\` — the vocabulary is CLOSED: ${vocab.join('/') || '(unconfigured)'}`);

    const ships = attrs.get('ships') ?? '';
    if (ships && !SHIPS.includes(ships))
      at(line, `\`ships=${ships}\` — use one of: ${SHIPS.join('/')}. It means THIS arm is the shipped default`);

    const status = attrs.get('status') ?? '';
    if (status === DERIVED)
      at(line, `\`status=${DERIVED}\` is never authored — it is DERIVED from a later row's \`supersedes=\`, `
        + 'so the two halves of the pair cannot disagree. Name this id there instead');
    else if (status && !STATUSES.includes(status))
      at(line, `unknown status \`${status}\` — use one of: ${STATUSES.join('/')}`);

    rows.push({
      line,
      section,
      id,
      metric,
      ships,
      status,
      arm: attrs.get('arm') ?? '',
      n: attrs.get('n') ?? '',
      value: attrs.get('value') ?? '',
      supersedes: (attrs.get('supersedes') ?? '').split(',').map((s) => s.trim()).filter(Boolean),
      escapes: carriedEscapes(raw),
    });
  });

  problems.sort((a, b) => a.line - b.line);
  return { rows, sections, problems };
}

/**
 * Each row's derived state and, when it was superseded, the row that did it.
 *
 * THE ORDERING RULE WAS REFUTED BY THE RECORD ITSELF. The first draft required `supersedes=` to name a row
 * EARLIER in the file — "chronological, so a replacement is always written below" — which is false here in
 * 6 of 76 cases, all the same shape: a section HEADING announces the correction and quotes the figure it
 * replaces in its own body, below. So the constraint is the one that was actually wanted, ACYCLICITY,
 * checked directly rather than bought as a side effect of a premise that does not hold.
 */
export function resolve(rows) {
  const byId = new Map();
  const problems = [];
  const state = new Map();

  for (const row of rows) {
    if (!row.id) continue;
    if (byId.has(row.id))
      problems.push({ line: row.line, why: `duplicate id \`${row.id}\` — first used at line ${byId.get(row.id).line}` });
    else byId.set(row.id, row);
  }

  const supersededBy = new Map();
  for (const row of rows) {
    for (const target of row.supersedes) {
      const found = byId.get(target);
      if (!found) {
        problems.push({ line: row.line, why: `\`supersedes=${target}\` names no result in this file` });
        continue;
      }
      if (found === row) {
        problems.push({ line: row.line, why: '`supersedes=` names its own id' });
        continue;
      }
      if (!supersededBy.has(target)) supersededBy.set(target, row);
    }
  }

  // A CYCLE makes every row in it both live and dead, and the index would render whichever the walk reached
  // first. Checked directly (grey/black DFS over `supersedes`) rather than inferred from file order.
  const colour = new Map();
  const walk = (row, trail) => {
    if (colour.get(row) === 'black') return;
    if (colour.get(row) === 'grey') {
      problems.push({
        line: row.line,
        why: `\`supersedes=\` forms a CYCLE: ${[...trail, row].map((r) => r.id).join(' → ')} — every row in `
          + 'it would be both the live figure and the dead one',
      });
      return;
    }
    colour.set(row, 'grey');
    for (const target of row.supersedes) {
      const found = byId.get(target);
      if (found && found !== row) walk(found, [...trail, row]);
    }
    colour.set(row, 'black');
  };
  for (const row of rows) walk(row, []);

  for (const row of rows)
    state.set(row, row.status === 'RETRACTED'
      ? { status: 'RETRACTED', by: null }
      : supersededBy.has(row.id)
        ? { status: DERIVED, by: supersededBy.get(row.id) }
        : { status: 'CURRENT', by: null });

  return { state, problems };
}

/**
 * Sections whose HEADING announces a retraction while every result under it still reads CURRENT.
 *
 * The defect: this record's house style is to announce a correction in the heading — *"RETRACTED — ranking
 * × the walk are NOT shown to be superadditive"*, *"the 63.5% bar above is now STALE"*, *"(n = 200,
 * superseded above)"* — and to leave the marker saying otherwise. Nothing else can see it: a superseded
 * figure retires no vocabulary, dangles no reference and moves no registered count.
 *
 * A section CLEARS the check with any row that is not derived-CURRENT, or any row naming what it
 * supersedes. Read against the DERIVED state, so a pair already recorded is reported neither twice nor at
 * all — and the ordinary escape for the honest case is that bookkeeping, which the index needs anyway.
 * `measure-ok` on the heading is for a heading using the vocabulary in its DOMAIN sense, which the real
 * record does exactly once: *"Does a CORRECTION separate from a RECURRENCE?"*.
 */
export function unmarkedRetractions(sections, rows, state, lines) {
  const hits = [];
  for (const section of sections) {
    const heading = lines[section.line - 1] ?? '';
    if (heading.includes(ESCAPE)) continue;
    const m = RETRACTION.exec(section.title);
    if (!m) continue;
    const mine = rows.filter((r) => r.section === section);
    if (mine.some((r) => state.get(r)?.status !== 'CURRENT' || r.supersedes.length > 0)) continue;
    hits.push({ line: section.line, section, word: m[0], text: section.title });
  }
  return hits;
}

/** The index body — a heading, a provenance note, and one row per result in file order. */
export function renderIndex(rows, state) {
  const tally = (s) => rows.filter((r) => state.get(r)?.status === s).length;
  const ships = rows.filter((r) => r.ships === 'yes').length;
  const shown = (row) => {
    const st = state.get(row) ?? { status: 'CURRENT', by: null };
    return st.by ? `${st.status} by L${st.by.line}` : st.status;
  };

  return [
    `## Results — ${rows.length} measured: ${tally('CURRENT')} current · ${tally(DERIVED)} superseded · `
      + `${tally('RETRACTED')} retracted`,
    '',
    '_Generated from the per-result `<!-- result: … -->` markers by',
    '`node devtools/dev.mjs check-measurements --write`. Edit a marker, never this table._',
    '_The two columns on the right are the two questions a cold-start probe could not answer from this',
    'record. `SUPERSEDED` is never written by hand — it is derived from another row\'s `supersedes=`, so the',
    'losing half of a pair cannot be the half nobody revisits. Line numbers are for JUMPING: the section',
    'itself carries the caveats, and no figure here is quotable without them._',
    '',
    `**${ships} of ${rows.length} rows measure the arm that actually SHIPS.** Every other row is a ladder`,
    'rung, a ceiling, an oracle or a baseline — reading one as a configuration recommendation is the',
    'mistake this column exists to prevent.',
    '',
    '| line | arm | metric | n | value | ships | status |',
    '| ---: | --- | --- | ---: | ---: | :---: | --- |',
    ...rows.map((r) => `| ${r.line} | ${cell(r.arm, 44)} | ${cell(r.metric, 24)} | ${cell(r.n, 18)} | `
      + `${cell(r.value, 16)} | ${r.ships === 'yes' ? '**ships**' : '—'} | ${shown(r)}`
      + `${escapeComments(r.escapes, 'result')} |`),
  ];
}

const renderFrom = (vocab) => (t) => {
  const { rows } = parseResults(t.split('\n'), vocab);
  return renderIndex(rows, resolve(rows).state);
};

/** The file with its index regenerated until it stops moving, or `null` if the anchors are missing. */
export const indexFixedPoint = (text, vocab) => fixedPoint(text, renderFrom(vocab), BLOCK_BEGIN, BLOCK_END);

export function checkMeasurements(repo, config = {}, log = console.log, opts = {}) {
  const vocab = config.measurementMetrics ?? [];
  let text;
  try {
    text = fs.readFileSync(path.join(repo, RECORD), 'utf8');
  } catch {
    log(`check-measurements: ✗ could not read ${RECORD}`);
    log('  The gate proves nothing against a file it cannot open, so this is a FAILURE.');
    return 1;
  }

  const normalized = text.split(/\r?\n/).join('\n');
  const lines = normalized.split('\n');
  const { rows, sections, problems } = parseResults(lines, vocab);
  const { state, problems: linkage } = resolve(rows);
  const all = [...problems, ...linkage].sort((a, b) => a.line - b.line);

  const failures = [];
  // The anchor pair is checked BEFORE anything writes through it: `blockRange` takes the FIRST begin, so a
  // duplicate silently moves the block and a write splices over everything between the two.
  failures.push(...anchorProblems(lines, BLOCK_BEGIN, BLOCK_END));
  if (!vocab.length) failures.push('`measurementMetrics` is empty — no metric could ever be valid');

  // Fail closed. A record with no sections is a structure change or a wrong path, never a clean run.
  if (sections.length === 0) {
    log(`check-measurements: ✗ found no result sections in ${RECORD}`);
    log('  Nothing was read, so this gate proves nothing — check the record path and its headings.');
    return 1;
  }

  const bare = sections.filter((s) => s.rows === 0 && s.free === null);
  const both = sections.filter((s) => s.rows > 0 && s.free !== null);
  if (bare.length > 0) failures.push(`${bare.length} section(s) declare neither a result nor a reason`);
  if (both.length > 0) failures.push(`${both.length} section(s) carry a result AND a result-free exemption`);
  if (all.length > 0) failures.push(`${all.length} marker problem(s)`);

  // A vocabulary value nobody uses is dead weight that cannot expire — the rule `retiredApiNames`,
  // `check-counts` and `pitfallFacets` all carry. Computed only over a sound parse: a metric that looks
  // unused because its marker is malformed would send a maintainer to delete a live entry.
  const unused = failures.length === 0 ? vocab.filter((v) => !rows.some((r) => r.metric === v)) : [];
  if (unused.length > 0) failures.push(`${unused.length} metric(s) in the vocabulary no result uses`);

  const stale = failures.length === 0 ? unmarkedRetractions(sections, rows, state, lines) : [];
  if (stale.length > 0)
    failures.push(`${stale.length} section heading(s) announce a retraction no result under them records`);

  const write = opts.write ? (next) => fs.writeFileSync(path.join(repo, RECORD), next) : null;
  const outcome = failures.length === 0 ? regenerate(normalized, renderFrom(vocab), BLOCK_BEGIN, BLOCK_END, write) : 'current';
  if (outcome === 'missing')
    failures.push(`the index anchors are missing — add \`${BLOCK_BEGIN} -->\` and \`${BLOCK_END}\``);
  else if (outcome === 'written')
    log(`check-measurements: regenerated the results index in ${RECORD} — ${rows.length} result(s)`);
  else if (outcome === 'stale')
    failures.push(`the results index at the head of ${RECORD} is STALE`);

  if (failures.length === 0) {
    const derived = rows.filter((r) => state.get(r)?.status === DERIVED).length;
    const retracted = rows.filter((r) => state.get(r)?.status === 'RETRACTED').length;
    const free = sections.filter((s) => s.free !== null).length;
    log(`check-measurements: ${rows.length} result(s) across ${sections.length} section(s) `
      + `(${free} result-free), ${rows.filter((r) => r.ships === 'yes').length} on the shipped arm, `
      + `${derived} superseded · ${retracted} retracted ✓`);
    return 0;
  }

  log(`check-measurements: ✗ ${failures.join('; ')}\n`);
  for (const s of bare) log(`  ${RECORD}:${s.line}  no result and no reason — ${cell(s.title, 78)}`);
  for (const s of both) log(`  ${RECORD}:${s.line}  a result AND a result-free reason — ${cell(s.title, 60)}`);
  for (const p of all) log(`  ${RECORD}:${p.line}  ${p.why}`);
  for (const h of stale) {
    log(`  ${RECORD}:${h.line}  this heading says "${h.word}" and every result under it reads CURRENT`);
    log(`      ${cell(h.text, 96)}`);
  }
  for (const v of unused) log(`  unused metric: ${v}`);
  log('');
  log('  Every section carries `<!-- result: id=… arm=… metric=… n=… value=… ships=… status=… -->`, or one');
  log('  `<!-- result-free: <reason> -->` when it measures nothing at all. A section that RETRACTS INLINE');
  log('  carries a SECOND marker on the retracted result — that mid-section case is the whole reason this');
  log('  index exists, and a heading-level one is blind to it by construction.');
  log(`  \`status=${DERIVED}\` is never written by hand. Name the superseded row's id in the NEW row's`);
  log('  `supersedes=` and the index derives the rest, so the two halves cannot disagree.');
  log('  A HEADING announcing a retraction is checked; a body is deliberately NOT — scanning bodies was');
  log('  measured at 63 hits and ZERO defects, because `stale@k` is a metric and supersession is the');
  log('  subject matter. Say it in `status=`/`supersedes=`, which is a registry rather than a word search.');
  log('  The index at the head of the file is GENERATED — rebuild it with');
  log('  `node devtools/dev.mjs check-measurements --write`; a hand-edited table is a defect, not an update.');
  log('  `measurementMetrics` (devtools/project.config.mjs) is CLOSED, and a value no result uses fails.');
  log(`  If a line genuinely discusses ANOTHER result's retraction, put \`${ESCAPE}\` on it — not`);
  log('  `drift-ok`/`link-ok`/`count-ok`, which are other gates\' and must not be silenced by this one.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkMeasurements(repoDefault, config, console.log, { write: process.argv.includes('--write') });
}
