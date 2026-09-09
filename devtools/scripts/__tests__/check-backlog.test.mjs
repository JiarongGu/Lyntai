// check-backlog's own tests.
//
// The guard exists because a rule was written down, obeyed once, and violated again — so the thing that
// matters most here is that it FAILS when it should. A length gate's dangerous direction is the permissive
// one: a preamble it cannot locate, or a handover spelled slightly differently, both read as a clean run.
//
// The manifest half carries the same burden from the other side. Its whole claim is that the roster at the
// head of the file and the items below it CANNOT disagree, and the only way that claim is worth anything is
// if a hand-edited table, a missing marker and an invented state each fail.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  KINDS, MAX_PREAMBLE, STATES, checkBacklog, manifestFixedPoint, parseItems, readBacklog,
} from '../check-backlog.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** A marker as the file writes it. `needs` is omitted when empty, exactly as a startable item omits it. */
function marker({ state = 'startable', kind = null, needs = null } = {}) {
  return `<!-- item: state=${state}${kind ? ` kind=${kind}` : ''}${needs ? ` needs="${needs}"` : ''} -->`;
}

/**
 * A backlog file with the shape the gate expects: the generated block's two anchors, a preamble, the
 * blocked roster boundary, and one Part holding marked items.
 *
 * `current: false` leaves the block EMPTY, which is the stale state a `--write` has not been run on.
 */
function backlog({
  preamble = [], handovers = [], items = [{}], roster = ['- **Part 1 / X** — a key.'],
  current = true, blockAnchors = true,
} = {}) {
  const raw = ['# Backlog', '',
    ...(blockAnchors ? ['<!-- open-items:begin -->', '<!-- open-items:end -->', ''] : []),
    '## Active backlog', '',
    ...preamble, ...handovers, '',
    'Blocked, and on what:',
    ...roster,
    '',
    '## Part 1 — a thing',
    '',
    ...items.flatMap((it, i) =>
      [`- [ ] **item ${i + 1}.** Some prose. ${marker(it)}`, '']),
  ].join('\n');
  return current && blockAnchors ? manifestFixedPoint(raw) : raw;
}

function run(text, config = {}, opts = {}) {
  const dir = makeTree({ 'TASKS.md': text });
  const log = recorder();
  try {
    return { code: checkBacklog(dir, config, log, opts), out: log.text(), dir };
  } finally { removeTree(dir); }
}

describe('check-backlog', () => {
  it('passes a short preamble with no handovers, and says what it measured', () => {
    const { code, out } = run(backlog({ preamble: ['a line.', 'another.'] }));

    assert.equal(code, 0);
    assert.match(out, /preamble 2 line\(s\) for 1 open item\(s\)/);
    assert.match(out, /no handover blocks/);
  });

  it('FAILS on a preamble over its budget', () => {
    const long = Array.from({ length: MAX_PREAMBLE + 1 }, (_, i) => `narrative line ${i}.`);
    const { code, out } = run(backlog({ preamble: long }));

    assert.equal(code, 1);
    assert.match(out, new RegExp(`preamble is ${MAX_PREAMBLE + 1} non-blank lines`));
  });

  it('FAILS on a handover block wherever it sits, and names its line', () => {
    // The measured defect was FIVE of these stacked. One is enough to fail.
    const { code, out } = run(backlog({ handovers: ['**HANDOVER (2026-01-01). Everything that happened.**'] }));

    assert.equal(code, 1);
    assert.match(out, /1 HANDOVER block\(s\) remain/);
    assert.match(out, /TASKS\.md:\d+ {2}\*\*HANDOVER/);
  });

  it('sees a handover dressed in emphasis, which is how the real ones were written', () => {
    // The real blocks were `_**HANDOVER ...` and `**HANDOVER ...`; matching only the bare form would have
    // let the italic ones through, and a gate that misses the shape it was built for is worse than none.
    const { code } = run(backlog({ handovers: ['_**HANDOVER (2026-01-01), kept for the reasoning.**'] }));

    assert.equal(code, 1);
  });

  it('an allowance raises the budget and is REPORTED, so slack is never invisible', () => {
    const long = Array.from({ length: MAX_PREAMBLE + 5 }, (_, i) => `line ${i}.`);
    const { code, out } = run(backlog({ preamble: long }), { backlogPreambleAllowance: MAX_PREAMBLE + 5 });

    assert.equal(code, 0);
    assert.match(out, new RegExp(`allowance ${MAX_PREAMBLE + 5}, limit ${MAX_PREAMBLE}`));
  });

  it('FAILS CLOSED when the structure it reads is gone, rather than reporting a short banner', () => {
    // The permissive direction: no `## Active backlog` heading means `slice` would count zero lines and
    // the gate would print a tick over a file it never examined.
    const { code, out } = run('# Backlog\n\n## Part 1 — a thing\n\n- [ ] item\n');

    assert.equal(code, 1);
    assert.match(out, /could not locate the backlog preamble/);
  });

  it('does not charge the BLOCKED roster to the preamble budget', () => {
    // The roster is open-backlog data, not narrative. Counting it would fire the day a blocked Part is
    // added — a false alarm, and the fastest way to teach a reader to raise the allowance without looking.
    const { code, out } = run(backlog({
      preamble: ['one line.'],
      roster: Array.from({ length: 60 }, (_, i) => `- **Part ${i} / X** — a key.`),
    }));

    assert.equal(code, 0);
    assert.match(out, /preamble 1 line\(s\)/);
  });

  it('does not charge the GENERATED manifest to the preamble budget either', () => {
    // It sits above `## Active backlog` by construction, so this asserts the placement rather than a
    // carve-out: a manifest inside the preamble would blow the budget the day the backlog grew.
    const { code, out } = run(backlog({ preamble: ['one line.'], items: Array.from({ length: 40 }, () => ({})) }));

    assert.equal(code, 0);
    assert.match(out, /preamble 1 line\(s\) for 40 open item\(s\)/);
  });

  it('and the gate is green on this tree', () => {
    const log = recorder();
    const config = { backlogPreambleAllowance: undefined };
    assert.equal(checkBacklog(repo, config, log), 0, log.text());
  });

  it('the real backlog holds open items, so the gate is not measuring an empty file', () => {
    const { preamble, openItems, handovers } = readBacklog(repo);

    assert.ok(openItems > 0, 'TASKS.md must hold open items for this gate to mean anything');
    assert.ok(preamble >= 0, 'the preamble must be locatable on the real tree');
    assert.equal(handovers.length, 0, 'a handover belongs in docs/task-archive.md');
  });
});

describe('check-backlog — the per-item state markers', () => {
  it('FAILS on an open item carrying no marker, and names its line', () => {
    // The whole manifest rests on state being AUTHORED. An unmarked item is one the roster cannot describe,
    // and inferring a state from a checkbox is exactly what `pitfalls.md` records as untightenable.
    const text = backlog().replace(/ <!-- item: [^>]*-->/, '');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /1 open item\(s\) carry no `item:` marker/);
    assert.match(out, /TASKS\.md:\d+/);
  });

  it('FAILS on a state outside the closed vocabulary', () => {
    const { code, out } = run(backlog({ items: [{ state: 'maybe' }] }));

    assert.equal(code, 1);
    assert.match(out, /unknown state `maybe`/);
    assert.match(out, new RegExp(STATES.join('.')));
  });

  it('FAILS on a blocked item that names no KIND', () => {
    // `task-lifecycle.md`: each kind is refuted by looking somewhere different, so a blocker without one
    // cannot be re-checked. This is the half a prose roster never enforced.
    const { code, out } = run(backlog({ items: [{ state: 'blocked', needs: 'a key' }] }));

    assert.equal(code, 1);
    assert.match(out, /blocked.*needs `kind=`/);
  });

  it('FAILS on a blocked item that names no NEEDS', () => {
    const { code, out } = run(backlog({ items: [{ state: 'blocked', kind: 'env' }] }));

    assert.equal(code, 1);
    assert.match(out, /blocked.*needs `needs="…"`/);
  });

  it('FAILS on a kind outside the closed vocabulary, including one bad kind in a list', () => {
    const { code, out } = run(backlog({ items: [{ state: 'blocked', kind: 'env,vibes', needs: 'a key' }] }));

    assert.equal(code, 1);
    assert.match(out, /unknown kind `vibes`/);
    assert.match(out, new RegExp(KINDS.join('.')));
  });

  it('accepts several kinds on one blocker, because two blockers are refuted two ways', () => {
    const { code } = run(backlog({ items: [{ state: 'blocked', kind: 'decision,env', needs: 'a vendor and a key' }] }));

    assert.equal(code, 0);
  });

  it('FAILS on a STARTABLE item carrying a blocker, which is how one reads as blocked', () => {
    const { code, out } = run(backlog({ items: [{ state: 'startable', needs: 'a key' }] }));

    assert.equal(code, 1);
    assert.match(out, /startable.*must not carry/);
  });

  it('FAILS on a watch or decision-only item with nothing to wait for', () => {
    for (const state of ['watch', 'decision-only']) {
      const { code, out } = run(backlog({ items: [{ state }] }));
      assert.equal(code, 1, state);
      assert.match(out, new RegExp(`${state}.*needs \`needs="…"\``));
    }
  });

  it('FAILS on an UNQUOTED multi-word value, which is silently truncated to its first word', () => {
    // The one fail-open path an adversarial review found: `matchAll` reports what it recognised and says
    // nothing about what it skipped, so `needs=a real key` parsed as `needs="a"`, satisfied every other
    // rule, and published a one-word blocker that reads as a complete one. It is not an unknown attribute
    // either — the residue contains no `=` — so validating the parsed keys alone could never see it.
    const text = backlog({ items: [{ state: 'blocked', kind: 'env', needs: 'a key' }] })
      .replace('needs="a key"', 'needs=a real fal.ai key and a download');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /stray text/);
    assert.match(out, /must be QUOTED/);
  });

  it('and --write REFUSES to publish a table built from a broken marker', () => {
    // The half that makes the bug above dangerous rather than merely wrong: it wrote. A roster generated
    // from a marker nobody validated is a confident wrong answer, which is worse than the missing one.
    const raw = backlog({ current: false, items: [{ state: 'blocked', kind: 'env', needs: 'a key' }] })
      .replace('needs="a key"', 'needs=a real fal.ai key and a download');
    const dir = makeTree({ 'TASKS.md': raw });
    const log = recorder();
    try {
      assert.equal(checkBacklog(dir, {}, log, { write: true }), 1);
      assert.equal(fs.readFileSync(path.join(dir, 'TASKS.md'), 'utf8'), raw, 'the file must be untouched');
    } finally { removeTree(dir); }
  });

  it('a QUOTED multi-word value survives whole, so the rule above bans nothing legitimate', () => {
    const needs = 'a real fal.ai key, and a ~1.7 GB model download';
    const text = backlog({ items: [{ state: 'blocked', kind: 'env', needs }] });

    assert.equal(run(text).code, 0);
    assert.ok(text.includes(needs), 'the blocker must reach the generated row intact');
  });

  it('FAILS on an unknown attribute, so a typo cannot silently drop a field', () => {
    const text = backlog().replace('state=startable', 'state=startable klnd=env');
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /unknown attribute `klnd`/);
  });

  it('FAILS CLOSED on an open item sitting above every `## Part` heading', () => {
    const text = backlog().replace('## Active backlog',
      `- [ ] **stray.** ${marker()}\n\n## Active backlog`);
    const { code, out } = run(text);

    assert.equal(code, 1);
    assert.match(out, /outside any `## Part`/);
  });

  it('reads the Part, the line, the title and the state off the file', () => {
    const lines = backlog({ items: [{ state: 'blocked', kind: 'data', needs: 'a deployment' }] }).split('\n');
    const [item] = parseItems(lines).items;

    assert.equal(item.part, 1);
    assert.equal(item.title, 'item 1');
    assert.equal(item.state, 'blocked');
    assert.deepEqual(item.kinds, ['data']);
    assert.equal(item.needs, 'a deployment');
    assert.equal(lines[item.line - 1].startsWith('- [ ]'), true);
  });
});

describe('check-backlog — the generated manifest', () => {
  it('FAILS when the block anchors are missing, naming what to add', () => {
    const { code, out } = run(backlog({ blockAnchors: false }));

    assert.equal(code, 1);
    assert.match(out, /open-items:begin/);
    assert.match(out, /open-items:end/);
  });

  it('FAILS when the table disagrees with the markers, and names the command that fixes it', () => {
    // The defect this gate is for: a hand-edited roster. The banner has advertised finished work four
    // times, always because the summary and the item were maintained separately.
    const stale = backlog().replace(/\| *startable *\|/, '| blocked |');
    const { code, out } = run(stale);

    assert.equal(code, 1);
    assert.match(out, /manifest .* is STALE/i);
    assert.match(out, /check-backlog --write/);
  });

  it('FAILS on an EMPTY block, which is how a new anchor pair arrives', () => {
    const { code } = run(backlog({ current: false }));

    assert.equal(code, 1);
  });

  it('--write regenerates the block, and the check then passes', () => {
    const dir = makeTree({ 'TASKS.md': backlog({ current: false }) });
    const log = recorder();
    try {
      assert.equal(checkBacklog(dir, {}, log, { write: true }), 0, log.text());
      assert.match(log.text(), /wrote|regenerated/i);
      assert.equal(checkBacklog(dir, {}, recorder()), 0);
    } finally { removeTree(dir); }
  });

  it('the manifest names every open item, with its line, Part and state', () => {
    const text = backlog({
      items: [{ state: 'startable' }, { state: 'blocked', kind: 'env', needs: 'a real key' }],
    });
    const lines = text.split('\n');
    const { items } = parseItems(lines);
    const block = text.slice(text.indexOf('<!-- open-items:begin'), text.indexOf('<!-- open-items:end -->'));

    for (const item of items) {
      assert.match(block, new RegExp(`\\|\\s*${item.line}\\s*\\|\\s*${item.part}\\s*\\|`),
        `the manifest must carry a row for the item at line ${item.line}`);
      assert.ok(block.includes(item.title), `the manifest must carry the title "${item.title}"`);
    }
    assert.ok(block.includes('a real key'), 'a blocker must be readable from the manifest');
    assert.match(block, /2 (?:open items|across)|— 2\b/, 'the heading must carry the total');
  });

  it('the LINE NUMBERS survive the block being rewritten — the fixed point converges', () => {
    // Writing the table changes the line numbers of every item below it, so a one-pass generator publishes
    // numbers that were true of the file before it existed. This is the whole reason the writer iterates.
    const text = backlog({ items: Array.from({ length: 12 }, () => ({})) });
    const { items } = parseItems(text.split('\n'));

    assert.equal(manifestFixedPoint(text), text, 'a written manifest must already be at its fixed point');
    for (const item of items) {
      assert.match(text.split('\n')[item.line - 1], /^- \[ \]/,
        `line ${item.line} must be the checkbox the manifest points at`);
    }
  });

  it("carries the item's own escape annotation onto its generated row", () => {
    // Found by `check-links` on this gate's first real run, not by review: a row REPRODUCES the item's
    // title, so a path annotated on the checkbox arrives unannotated one line-number away and the sibling
    // gate fires on the generated copy. Carrying the item's own escape expires when the item's does.
    const raw = backlog({ current: false })
      .replace('Some prose.', 'Split `docs/gone.md`. <!-- link-ok: the file this item creates -->');
    const written = manifestFixedPoint(raw);
    const block = written.slice(written.indexOf('<!-- open-items:begin'), written.indexOf('<!-- open-items:end'));

    assert.match(block, /link-ok: carried from this item's own line/);
    assert.doesNotMatch(block, /count-ok|drift-ok/, 'only the escapes the item actually carries');
  });

  it('the real backlog is fully marked and its manifest is current', () => {
    // The on-tree assertion, and the one that actually protects the file: every open item marked, every
    // state in the vocabulary, and the head-of-file roster equal to what the markers say.
    const { items, unmarked } = parseItems(
      fs.readFileSync(path.join(repo, 'TASKS.md'), 'utf8').split(/\r?\n/));

    assert.equal(unmarked.length, 0, 'every open item in TASKS.md must carry an `item:` marker');
    assert.ok(items.length > 0, 'TASKS.md must hold open items');
    assert.ok(items.some((i) => i.state === 'startable'), 'some work must be startable');
  });
});
