// check-backlog — FAIL when the OPEN backlog stops being a list of open work.
//
// TWO SUBJECTS, and they are the same defect from opposite ends: prose that has stopped answering "what is
// left".
//
// LENGTH, measured 2026-09-10: the `## Active backlog` preamble had reached 478 non-blank lines carrying
// ZERO open checkboxes — five stacked HANDOVER blocks plus a running tally of what had closed — inside a
// 1308-line file holding 17 open items. The file had RECORDED deleting a 49-line tally for this exact
// reason on 2026-09-03 and then regrew a 19-line one in the same place.
//
// THE MANIFEST: the startable-set banner has advertised finished work FOUR times, always because a summary
// and the items it summarizes were maintained separately. `.claude/knowledge/pitfalls.md` refuted deriving
// that banner from the checkboxes — "Part 99 is a WATCH item" is not computable from a `- [ ]` — and
// pointed at a registry instead. This is that registry: state is AUTHORED on each item in an
// `<!-- item: … -->` marker, and the roster at the head of the file is GENERATED from it, so the two cannot
// disagree. See `docs/DECISIONS.md` D111.
//
// FOUR CHECKS, each anchored on something measured rather than on taste:
//   1. the PREAMBLE has a non-blank line budget — a RATCHET (`backlogPreambleAllowance`), no escape token;
//   2. no HANDOVER block survives anywhere — a handover describes DONE work, so its home is the archive;
//   3. every open `- [ ]` carries one well-formed marker, a blocker naming its KIND and what would clear it;
//   4. the generated manifest equals what those markers say. `--write` regenerates it.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  anchorProblems, carriedEscapes, cell, escapeComments, fixedPoint, markerPattern, parseAttributes,
} from './_markers.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = path.resolve(path.dirname(here), '..', '..');

/** The record this gate reads. */
export const RECORD = 'TASKS.md';

/** Non-blank lines the preamble may occupy before it has stopped being a banner. */
export const MAX_PREAMBLE = 40;

/**
 * The closed vocabulary of item states.
 *
 * `watch` and `decision-only` exist because a checkbox cannot express either, and both were being counted
 * as startable: a probe burned 64 lines discovering Part 99 is something to watch for recurrence, and Part
 * 128's `Model`-precedence item says in its own prose that it is "not startable as a code change".
 */
export const STATES = ['startable', 'blocked', 'watch', 'decision-only'];

/**
 * The closed vocabulary of blocker kinds, from `.claude/rules/task-lifecycle.md`.
 *
 * Each is refuted by looking somewhere DIFFERENT — the tree, the machine, a ruling, a deployment's data —
 * which is the whole reason a blocker records one. A backlog item once sat blocked on "a real embedding
 * model" while one was pulled on the machine the entire time, because the re-check read the tree.
 */
export const KINDS = ['tree', 'env', 'decision', 'data'];

/** A handover block. Kept broad — the defect is the BLOCK, whatever it is dressed as. */
const HANDOVER = /^\s*(?:_|\*)*\s*\*\*HANDOVER\b/i;

// Where the preamble ends. The BLOCKED roster is structured backlog data — which Parts cannot be started
// and on what — so it is open-backlog content and charging it to a "the banner is too long" budget would
// fire the day somebody adds a blocked Part, which is a false alarm and the fastest way to teach a reader
// to raise the allowance without looking. The subject here is the NARRATIVE above it.
const PREAMBLE_END = /^## Part \d|^Blocked, and on what:/;

const OPEN_ITEM = /^- \[ \]/;
const PART_HEADING = /^#{2,3} Part (\d+)\b/;
const TITLE = /^- \[ \]\s+\*\*(.+?)\*\*/;

// The marker cannot contain `>`, which is what keeps it from running past its own terminator. A `needs`
// that wants one is a `needs` that has stopped being a short testable phrase.
const MARKER = markerPattern('item');

/** The anchors between which the manifest is generated. Placed by hand ONCE; content is never hand-written. */
export const BLOCK_BEGIN = '<!-- open-items:begin';
export const BLOCK_END = '<!-- open-items:end -->';

/**
 * Every open item, as the FILE declares it — `{ items, unmarked, problems }`.
 *
 * State is read, never inferred. An item whose marker is missing or malformed lands in `unmarked` or
 * `problems` rather than being given a default, because a default is exactly the guess this registry exists
 * to replace.
 */
export function parseItems(lines) {
  const items = [];
  const unmarked = [];
  const problems = [];
  // Every `## Part n` heading, and how many open checkboxes sit under it. Counted from the RAW checkbox
  // line rather than from `items`, so a Part holding only items with broken markers is not also reported
  // as empty — that would be one defect wearing two names, and the marker report is the actionable one.
  const parts = [];
  let part = null;

  lines.forEach((raw, i) => {
    const heading = PART_HEADING.exec(raw);
    if (heading) {
      part = Number(heading[1]);
      parts.push({ number: part, line: i + 1, open: 0, title: raw.trim().slice(0, 78) });
      return;
    }
    if (!OPEN_ITEM.test(raw)) return;
    if (parts.length > 0) parts[parts.length - 1].open++;

    const line = i + 1;
    const at = (why) => problems.push({ line, why });
    const marker = MARKER.exec(raw);
    if (!marker) { unmarked.push({ line, text: raw.trim().slice(0, 78) }); return; }
    if (part === null) at('this open item sits outside any `## Part` heading, so nothing can place it');

    const title = TITLE.exec(raw);
    if (!title) at('no bolded title — an item reads `- [ ] **What this is.** …`');

    // The RESIDUE is what catches a value whose quotes were omitted — see `_markers.mjs` for why matching
    // alone cannot: the leftover holds no `=`, so it is not an unknown attribute either.
    const { attrs, residue } = parseAttributes(marker[1]);
    if (residue)
      at(`stray text \`${residue.slice(0, 40)}\` in the marker — a value containing spaces must be QUOTED `
        + '(`needs="…"`), or everything after the first word is silently dropped');

    for (const key of attrs.keys())
      if (!['state', 'kind', 'needs'].includes(key))
        at(`unknown attribute \`${key}\` — a marker carries state/kind/needs and nothing else`);

    const state = attrs.get('state');
    if (!state) at(`no \`state=\` — use one of: ${STATES.join('/')}`);
    else if (!STATES.includes(state)) at(`unknown state \`${state}\` — use one of: ${STATES.join('/')}`);

    const kinds = (attrs.get('kind') ?? '').split(',').map((k) => k.trim()).filter(Boolean);
    for (const k of kinds)
      if (!KINDS.includes(k)) at(`unknown kind \`${k}\` — use one or more of: ${KINDS.join('/')}`);

    const needs = attrs.get('needs') ?? '';
    if (state === 'startable' && (kinds.length || needs))
      at('state=startable must not carry `kind=` or `needs=` — a startable item waits on nothing, and one '
        + 'that names a blocker reads as blocked');
    if (state === 'blocked' && !kinds.length)
      at('state=blocked needs `kind=` — each kind is refuted by looking somewhere different, so a blocker '
        + 'without one cannot be re-checked (task-lifecycle.md)');
    if (state && state !== 'startable' && !needs)
      at(`state=${state} needs \`needs="…"\` — say what would clear it, concretely enough to test`);

    items.push({
      line, part, state, kinds, needs,
      title: title ? title[1].replace(/[.:]\s*$/, '') : '',
      escapes: carriedEscapes(raw),
    });
  });

  return { items, unmarked, problems, parts };
}

/**
 * Parts whose heading survives after their last open checkbox left — the accumulation this gate was
 * extended to catch (BL2, 2026-09-16).
 *
 * **It is the same defect the preamble budget already bounds, one level down.** `PREAMBLE_END` stops at the
 * first `## Part`, so everything below was unbounded: four Parts reached **295 lines and ZERO open
 * checkboxes**, every line a "CLOSED as archive Part N" note. The preamble rule had been enforced for
 * weeks while the accumulation simply moved underneath it.
 *
 * **Why an empty Part is never legitimate, stated because the item that filed this worried it might be.**
 * A Part is a GROUPING of open items; the archive is where a finished one goes. An emptied Part is not
 * merely tidy-able — it reads as a live home for its question, so other records keep citing it as one, and
 * two stale claims were reachable through exactly that. Struck-through items (`~~…~~`) are not `- [ ]` and
 * correctly do not count: a Part holding only those is empty.
 *
 * The one thing it must NOT fire on is a heading that is not a backlog Part at all — `PART_HEADING`
 * requires `Part <digits>`, so a `## Retired — …` summary heading is invisible here, which is what lets a
 * retirement leave a pointer behind without tripping the rule it just satisfied.
 */
export const emptyParts = (parts) => parts.filter((p) => p.open === 0);

/** The manifest body — a heading, a provenance line, and one row per open item, in file order. */
export function renderManifest(items) {
  const parts = new Set(items.map((i) => i.part)).size;
  const tally = STATES
    .map((s) => [s, items.filter((i) => i.state === s).length])
    .filter(([, n]) => n > 0)
    .map(([s, n]) => `${n} ${s}`)
    .join(', ');

  return [
    `## Open items — ${items.length} across ${parts} Part${parts === 1 ? '' : 's'}: ${tally}`,
    '',
    '_Generated from the per-item `<!-- item: … -->` markers by '
      + '`node devtools/dev.mjs check-backlog --write`._',
    '_Edit a marker, never this table — `verify` fails the moment the two disagree._',
    '',
    '| line | Part | item | state | waiting on |',
    '| ---: | ---: | --- | --- | --- |',
    ...items.map((i) => `| ${i.line} | ${i.part ?? '?'} | ${cell(i.title)} | `
      + `${i.state}${i.kinds.length ? ` · ${i.kinds.join('+')}` : ''} | ${cell(i.needs)}`
      + `${escapeComments(i.escapes, 'item')} |`),
  ];
}

/** The file with its manifest regenerated until it stops moving, or `null` if the anchors are missing. */
export const manifestFixedPoint = (text) => fixedPoint(
  text, (t) => renderManifest(parseItems(t.split('\n')).items), BLOCK_BEGIN, BLOCK_END,
);

/**
 * The preamble's non-blank line count, every handover line, and the open-item roster.
 *
 * Counted from the `## Active backlog` heading rather than from the file's top, so the file's own purpose
 * statement, goal and generated manifest are not charged to the banner — those are stable or derived, and
 * neither was ever the thing that grew.
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

  return {
    preamble, handovers, text, lines,
    openItems: lines.filter((l) => OPEN_ITEM.test(l)).length,
    ...parseItems(lines),
  };
}

export function checkBacklog(repo, config = {}, log = console.log, opts = {}) {
  const allowance = Number.isInteger(config.backlogPreambleAllowance)
    ? config.backlogPreambleAllowance
    : MAX_PREAMBLE;
  const { preamble, handovers, openItems, lines, items, unmarked, problems, parts } = readBacklog(repo);

  if (preamble < 0) {
    log('check-backlog: ✗ could not locate the backlog preamble');
    log(`  Expected a '## Active backlog' heading above the first '## Part <n>' in ${RECORD}.`);
    log('  The gate proves nothing against a structure it cannot read, so this is a FAILURE.');
    return 1;
  }

  const failures = [];
  // The anchor pair is checked BEFORE anything writes through it — `blockRange` takes the FIRST begin, so
  // a duplicate above the real one silently moves the block and a write splices over the prose between
  // them. Measured on this gate's sibling, where it deleted a document's intro paragraph at exit 0.
  failures.push(...anchorProblems(lines, BLOCK_BEGIN, BLOCK_END));
  if (preamble > allowance)
    failures.push(`the preamble is ${preamble} non-blank lines, over its ${allowance}`);
  if (handovers.length > 0) failures.push(`${handovers.length} HANDOVER block(s) remain`);
  if (unmarked.length > 0) failures.push(`${unmarked.length} open item(s) carry no \`item:\` marker`);
  if (problems.length > 0) failures.push(`${problems.length} marker problem(s)`);
  const empties = emptyParts(parts);
  if (empties.length > 0)
    failures.push(`${empties.length} \`## Part\` heading(s) hold no open checkbox`);

  // The manifest is only asked about once the markers are sound: a roster generated from a broken marker is
  // a confident wrong answer, which is worse than the missing one it replaces.
  const normalized = lines.join('\n');
  const fixed = failures.length === 0 ? manifestFixedPoint(normalized) : normalized;
  const anchorsMissing = fixed === null;
  const stale = !anchorsMissing && fixed !== normalized;

  if (anchorsMissing)
    failures.push(`the manifest anchors are missing — add \`${BLOCK_BEGIN} -->\` and \`${BLOCK_END}\``);
  else if (stale && opts.write) {
    fs.writeFileSync(path.join(repo, RECORD), fixed);
    log(`check-backlog: regenerated the open-items manifest in ${RECORD} — wrote ${items.length} row(s)`);
  } else if (stale) {
    failures.push(`the open-items manifest at the head of ${RECORD} is STALE`);
  }

  if (failures.length === 0) {
    const slack = allowance > MAX_PREAMBLE ? ` (allowance ${allowance}, limit ${MAX_PREAMBLE})` : '';
    const startable = items.filter((i) => i.state === 'startable').length;
    log(`check-backlog: ${RECORD} preamble ${preamble} line(s) for ${openItems} open item(s), `
      + `no handover blocks, manifest current (${startable} startable) ✓${slack}`);
    return 0;
  }

  log(`check-backlog: ✗ ${failures.join('; ')}\n`);
  for (const h of handovers) log(`  ${RECORD}:${h.line}  ${h.text}`);
  for (const u of unmarked) log(`  ${RECORD}:${u.line}  ${u.text}`);
  for (const p of problems) log(`  ${RECORD}:${p.line}  ${p.why}`);
  for (const p of empties) log(`  ${RECORD}:${p.line}  Part ${p.number} holds no open checkbox — ${p.title}`);
  log('');
  if (empties.length > 0) {
    log('  A Part with no open checkbox is finished work still occupying the OPEN backlog. Move it to');
    log('  `docs/task-archive.md` under a FRESH number — the two files number independently, so its own');
    log('  number is probably taken there by something unrelated — repoint every citation, and leave a');
    log('  pointer rather than a copy. A heading that is not `## Part <n>` is invisible to this rule.');
    log('');
  }
  log('  The backlog holds OPEN work only and must not summarize the archive');
  log('  (`.claude/rules/task-lifecycle.md`). A handover describes work that is DONE — move it to');
  log('  `docs/task-archive.md`, one Part per task, and leave a POINTER rather than a copy.');
  log('  Every open item carries `<!-- item: state=… -->` on its checkbox line; the roster at the head of');
  log('  the file is generated from those markers by `node devtools/dev.mjs check-backlog --write`, so a');
  log('  hand-edited table is a defect rather than an update.');
  log('  If a longer preamble is genuinely earned, record it in `backlogPreambleAllowance`');
  log('  (devtools/project.config.mjs) — it can come down and never up. There is no escape token.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkBacklog(repoDefault, config, console.log, { write: process.argv.includes('--write') });
}
