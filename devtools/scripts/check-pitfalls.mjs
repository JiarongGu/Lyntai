// check-pitfalls — FAIL when the traps record cannot be searched by anything but its headings.
//
// MEASURED 2026-09-10, by five instrumented cold-start probes reading this repository the way a fresh
// session would. One probe needed the traps bearing on its task; the nine relevant ones spanned FIVE of the
// nine headings, and two of those five are headings nobody looking for that task would have opened. The
// file is 2,200 lines and every trap in it was written because it cost something — a trap nobody can find
// is one that gets paid for twice.
//
// RETITLING THE HEADINGS IS REFUTED, and that is why this exists at all: any single hierarchy puts each
// trap in exactly one place, and these traps genuinely belong in two. So the file carries FACETS that are
// ORTHOGONAL to its headings — `sub=` (which area of the repository breaks) and `shape=` (how the wrongness
// stays invisible) — authored on each trap and indexed at the head of the file.
//
// LINE NUMBERS ONLY, deliberately. `decisions-index` was measured on the same day and barely helped LOCATING
// cost (~66 lines read against ~117); its wins were precision and a free currency scan. So an index sized
// for READING would be over-building — this one is sized for JUMPING.
//
// FIVE CHECKS: every trap carries one well-formed marker; every facet value is in a closed vocabulary
// (`pitfallFacets` in `devtools/project.config.mjs`); every vocabulary value is USED, so a category nobody
// needed cannot sit there unexpiring; the index equals what the markers say (`--write` regenerates it); and
// no trap outgrows `MAX_TRAP` lines or its `pitfallLengthAllowances` entry — the `_entry-length.mjs` ratchet,
// because a record read "before extending anything" had no bound on the length of what it asks you to read.
// State is AUTHORED and never inferred, for the reason `docs/DECISIONS.md` D111 gives.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { judgeLedger, reportLedger } from './_entry-length.mjs';
import {
  anchorProblems, cell, fixedPoint, markerPattern, parseAttributes, regenerate, scanMarkers,
} from './_markers.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The record this gate reads. */
export const RECORD = '.claude/knowledge/pitfalls.md';

/** The facets a trap carries. Both are required; each takes one or two values. */
export const FACETS = ['sub', 'shape'];

/** The most values one facet may carry. Two is "it genuinely belongs in both"; three is a shrug. */
export const MAX_VALUES = 2;

/** The non-blank lines a trap may reach. Twice the median trap (7, measured 2026-09-25 over 224 traps) —
 * the rule `check-decisions` bounds its own entries by — and the 99th percentile, so the ledger opened at
 * two entries rather than nine. */
export const MAX_TRAP = 14;

export const BLOCK_BEGIN = '<!-- facets:begin';
export const BLOCK_END = '<!-- facets:end -->';

const TRAP = /^- /;
const MARKER = markerPattern('trap');
const HEADING = /^## (.+)$/;
const LEAD = /^- \*\*(.+?)\*\*/;

/**
 * Every trap, as the file declares it — `{ traps, unmarked, problems }`.
 *
 * A top-level `- ` bullet is a trap; an indented one is a sub-point of the trap above it. A line inside a
 * fence is not prose (this file quotes captured output), and the generated block's own rows are `- `
 * bullets, so both are skipped — by `scanMarkers`, which also fails an unclosed fence and a broken marker.
 */
export function parseTraps(lines, vocab = {}) {
  const traps = [];
  const unmarked = [];
  const { visible, problems } = scanMarkers(lines, { name: 'trap', begin: BLOCK_BEGIN, end: BLOCK_END, noun: 'trap' });
  let heading = null;
  // where a trap ENDS: the next trap, marked or not, or the next heading — never one inside a fence
  const boundaries = visible.filter(({ raw }) => TRAP.test(raw) || /^#{1,6} /.test(raw)).map(({ i }) => i + 1);

  visible.forEach(({ i, raw, marker, broken }) => {
    const h = HEADING.exec(raw);
    if (h) { heading = h[1]; return; }
    if (!TRAP.test(raw) || broken) return;

    const line = i + 1;
    const at = (why) => problems.push({ line, why });
    if (!marker) { unmarked.push({ line, text: raw.trim().slice(0, 78) }); return; }

    const { attrs, residue } = parseAttributes(marker[1]);
    if (residue)
      at(`stray text \`${residue.slice(0, 40)}\` in the marker — a value containing spaces must be QUOTED, `
        + 'or everything after the first word is silently dropped');
    for (const key of attrs.keys())
      if (!FACETS.includes(key)) at(`unknown facet \`${key}\` — a trap carries ${FACETS.join('/')} only`);

    const values = {};
    for (const facet of FACETS) {
      const raw_ = attrs.get(facet);
      const vals = (raw_ ?? '').split(',').map((v) => v.trim()).filter(Boolean);
      const allowed = vocab[facet] ?? [];
      if (!vals.length) at(`no \`${facet}=\` — every trap carries both facets, or it is unfindable by one`);
      if (vals.length > MAX_VALUES)
        at(`${facet}= carries ${vals.length} values; at most ${MAX_VALUES}. A trap in every category is in none`);
      for (const v of vals)
        if (!allowed.includes(v))
          at(`unknown ${facet} \`${v}\` — the vocabulary is CLOSED: ${allowed.join('/') || '(unconfigured)'}`);
      values[facet] = vals;
    }

    const lead = LEAD.exec(raw);
    const end = boundaries.find((b) => b > line) ?? lines.length + 1;
    traps.push({
      line, heading, ...values,
      lead: lead ? lead[1] : raw.replace(TRAP, '').replace(MARKER, ''),
      // what an allowance key is matched against: the first line as prose, markup and marker gone
      title: raw.replace(TRAP, '').replace(MARKER, '').replaceAll('**', '').trim(),
      // non-blank, as `_entry-length` counts: a blank line is formatting, not prose
      length: lines.slice(i, end - 1).filter((l) => l.trim()).length,
    });
  });

  problems.sort((a, b) => a.line - b.line);
  return { traps, unmarked, problems };
}

/** The index body: one line per facet value, holding the line numbers of its traps. */
export function renderFacets(traps, vocab = {}) {
  const list = (facet) => (vocab[facet] ?? [])
    .map((v) => [v, traps.filter((t) => (t[facet] ?? []).includes(v)).map((t) => t.line)])
    .filter(([, lines]) => lines.length > 0)
    .map(([v, lines]) => `- **\`${v}\`** (${lines.length}) — ${lines.join(' · ')}`);

  return [
    `## Facets — ${traps.length} traps, indexed two ways`,
    '',
    '_Generated from the per-trap `<!-- trap: … -->` markers by '
      + '`node devtools/dev.mjs check-pitfalls --write`. Edit a marker, never this index._',
    '_Line numbers only, on purpose: this is for JUMPING, not for reading. The facets are ORTHOGONAL to the'
      + ' headings below — a probe\'s nine relevant traps once spanned five of them, two of which nobody_',
    '_looking for that task would have opened._',
    '',
    '**By AREA** — which part of the repository breaks.',
    '',
    ...list('sub'),
    '',
    '**By SHAPE** — how the wrongness stays invisible. Orthogonal to the area, and usually the more useful',
    'of the two: most of these traps recur in a subsystem that had never met them.',
    '',
    ...list('shape'),
  ];
}

/** The file with its facet index regenerated until it stops moving, or `null` if the anchors are missing. */
export const facetFixedPoint = (text, vocab) =>
  fixedPoint(text, (t) => renderFacets(parseTraps(t.split('\n'), vocab).traps, vocab), BLOCK_BEGIN, BLOCK_END);

export function checkPitfalls(repo, config = {}, log = console.log, opts = {}) {
  const vocab = config.pitfallFacets ?? {};
  let text;
  try {
    text = fs.readFileSync(path.join(repo, RECORD), 'utf8');
  } catch {
    log(`check-pitfalls: ✗ could not read ${RECORD}`);
    log('  The gate proves nothing against a file it cannot open, so this is a FAILURE.');
    return 1;
  }

  const normalized = text.split(/\r?\n/).join('\n');
  const lines = normalized.split('\n');
  const { traps, unmarked, problems } = parseTraps(lines, vocab);

  const failures = [];
  // The anchor pair is checked BEFORE anything reads or writes through it: a duplicate anchor moves the
  // block, and `--write` then splices over whatever sits between the two.
  const anchors = anchorProblems(lines, BLOCK_BEGIN, BLOCK_END);
  failures.push(...anchors);
  if (!FACETS.every((f) => (vocab[f] ?? []).length))
    failures.push(`\`pitfallFacets\` is missing a vocabulary for ${FACETS.join(' or ')}`);
  if (traps.length === 0 && unmarked.length === 0)
    failures.push('no traps found — the gate would report a tick over a file it never read');
  if (unmarked.length > 0) failures.push(`${unmarked.length} trap(s) carry no \`trap:\` marker`);
  if (problems.length > 0) failures.push(`${problems.length} marker problem(s)`);

  // A vocabulary value nobody uses is dead weight that cannot expire — the rule `retiredApiNames` and
  // `check-counts` both carry, applied to a closed vocabulary. A category invented for a trap that never
  // arrived makes the list longer to choose from and protects nothing.
  const unused = failures.length === 0
    ? FACETS.flatMap((f) => (vocab[f] ?? [])
      .filter((v) => !traps.some((t) => (t[f] ?? []).includes(v)))
      .map((v) => `${f}=${v}`))
    : [];
  if (unused.length > 0) failures.push(`${unused.length} vocabulary value(s) no trap uses`);

  const write = opts.write ? (next) => fs.writeFileSync(path.join(repo, RECORD), next) : null;
  const render = (t) => renderFacets(parseTraps(t.split('\n'), vocab).traps, vocab);
  const outcome = failures.length === 0 ? regenerate(normalized, render, BLOCK_BEGIN, BLOCK_END, write) : 'current';
  if (outcome === 'missing')
    failures.push(`the index anchors are missing — add \`${BLOCK_BEGIN} -->\` and \`${BLOCK_END}\``);
  else if (outcome === 'written')
    log(`check-pitfalls: regenerated the facet index in ${RECORD} — ${traps.length} trap(s)`);
  else if (outcome === 'stale')
    failures.push(`the facet index at the head of ${RECORD} is STALE`);
  const markersFailed = failures.length > 0;

  const { verdict, ambiguous } = lengthRatchet(traps, config.pitfallLengthAllowances ?? {});
  if (!verdict.clean || ambiguous.length > 0) failures.push(`a trap outgrew the ${MAX_TRAP}-line bound or its ledger`);

  if (failures.length === 0) {
    const used = (f) => new Set(traps.flatMap((t) => t[f] ?? [])).size;
    log(`check-pitfalls: ${traps.length} trap(s) indexed by ${used('sub')} area(s) `
      + `and ${used('shape')} shape(s), none over ${MAX_TRAP} non-blank lines ✓ `
      + `(${verdict.budgeted} on a recorded allowance, ${verdict.debt} lines of debt)`);
    return 0;
  }

  log(`check-pitfalls: ✗ ${failures.join('; ')}\n`);
  for (const u of unmarked) log(`  ${RECORD}:${u.line}  ${cell(u.text, 96)}`);
  for (const p of problems) log(`  ${RECORD}:${p.line}  ${p.why}`);
  for (const u of unused) log(`  unused vocabulary: ${u}`);
  if (markersFailed) {
    log('');
    log('  Every trap carries `<!-- trap: sub=… shape=… -->` on its first line, and the index at the head of');
    log('  the file is GENERATED from those markers by `node devtools/dev.mjs check-pitfalls --write` — so a');
    log('  hand-edited index is a defect rather than an update. The vocabularies are CLOSED and live in');
    log('  `pitfallFacets` (devtools/project.config.mjs); adding a value there is a deliberate act, and a');
    log('  value no trap uses fails so the list cannot grow categories nobody needed.');
    log('  Facets are ORTHOGONAL to the headings on purpose — a trap belongs to an AREA and to a SHAPE, and');
    log('  the headings can only ever express one hierarchy.');
  }
  if (!verdict.clean) {
    if (markersFailed) log('');
    reportLedger(verdict, {
      log, name: 'check-pitfalls', record: RECORD, limit: MAX_TRAP,
      advice: [
        'A trap states what goes wrong, why it stays invisible, and the rule that avoids it. Move the',
        'incident to docs/FIXES.md and a measurement to the record that owns it, then record what is left',
        'in `pitfallLengthAllowances`, keyed by the start of the trap\'s lead. There is no escape token.',
      ],
    });
  }
  for (const a of ambiguous)
    log(`\ncheck-pitfalls: ✗ the allowance \`${a.key}\` matches ${a.count} traps — lengthen the key until it names one`);
  return 1;
}

/**
 * The length RATCHET over the traps (`_entry-length.mjs` semantics), as `{ verdict, ambiguous }`.
 *
 * A trap has no id, so an allowance is keyed by the START of its lead — stable across the line shifts every
 * edit above it causes. A key matching no trap is stale and one matching two is ambiguous: both FAIL, since
 * either holds nothing to its number.
 */
export function lengthRatchet(traps, allowances) {
  const keys = Object.keys(allowances);
  const matches = (key) => traps.filter((t) => t.title.startsWith(key));
  const ambiguous = keys.map((key) => ({ key, count: matches(key).length })).filter((a) => a.count > 1);
  const usable = Object.fromEntries(Object.entries(allowances).filter(([key]) => !ambiguous.some((a) => a.key === key)));
  const entries = traps.map((t) => ({
    id: Object.keys(usable).find((key) => t.title.startsWith(key)) ?? t.title,
    line: t.line,
    length: t.length,
    label: cell(t.title, 60),
  }));
  return { verdict: judgeLedger(entries, usable, MAX_TRAP), ambiguous };
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkPitfalls(repoDefault, config, console.log, { write: process.argv.includes('--write') });
}
