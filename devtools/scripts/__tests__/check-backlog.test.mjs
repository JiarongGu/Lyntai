// check-backlog's own tests.
//
// The guard exists because a rule was written down, obeyed once, and violated again — so the thing that
// matters most here is that it FAILS when it should. A length gate's dangerous direction is the permissive
// one: a preamble it cannot locate, or a handover spelled slightly differently, both read as a clean run.
import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import { MAX_PREAMBLE, checkBacklog, readBacklog } from '../check-backlog.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** A backlog file with the shape the gate expects. */
function backlog({ preamble = [], handovers = [], items = 1 } = {}) {
  return ['# Backlog', '', '## Active backlog', '',
    ...preamble, ...handovers, '',
    'Blocked, and on what:',
    '- **Part 1 / X** — a key.',
    '',
    '## Part 1 — a thing',
    '',
    ...Array.from({ length: items }, (_, i) => `- [ ] item ${i + 1}`),
    ''].join('\n');
}

function run(text, config = {}) {
  const dir = makeTree({ 'TASKS.md': text });
  const log = recorder();
  try {
    return { code: checkBacklog(dir, config, log), out: log.text(), dir };
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
    const withRoster = backlog({ preamble: ['one line.'] })
      .replace('- **Part 1 / X** — a key.',
        Array.from({ length: 60 }, (_, i) => `- **Part ${i} / X** — a key.`).join('\n'));
    const { code, out } = run(withRoster);

    assert.equal(code, 0);
    assert.match(out, /preamble 1 line\(s\)/);
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
