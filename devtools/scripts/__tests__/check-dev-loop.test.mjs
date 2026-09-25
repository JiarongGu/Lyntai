// check-dev-loop's own tests.
//
// The gate's claim is that `CLAUDE.md`'s command table cannot disagree with the command roster. Every way it
// could fail permissively has to be caught: an undocumented command renders a blank cell that reads like a
// command doing nothing, a dead registry entry silently stops covering what it was written for, and a
// hand-edited table is exactly the drift this replaces. Each of those looks like a complete table.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';

import {
  BEGIN, END, ROSTER, TARGET, checkDevLoop, commandRoster, registryProblems, renderTable,
} from '../check-dev-loop.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const DESCRIPTIONS = { verify: 'the gate', build: 'build it', bench: 'measure it' };

/** A roster of the shape `devtools/commands.mjs` exports. */
const roster = ({ names = ['verify', 'build', 'bench'], steps = ['build'] } = {}) => ({
  commands: names.map((name) => ({ name, builtin: true })),
  steps: steps.map((s) => [s, []]),
});

/** A target document carrying the anchor pair. */
const target = ({ body = [], anchors = true } = {}) =>
  ['# Doc', '', '## Dev loop', '', ...(anchors ? [`${BEGIN} -->`, '', ...body, '', END] : []), ''].join('\n');

function run(doc = target(), { descriptions = DESCRIPTIONS, r = roster(), write = false } = {}) {
  const dir = makeTree(doc === null ? {} : { [TARGET]: doc });
  const log = recorder();
  try {
    return { code: checkDevLoop(dir, { devLoopCommands: descriptions }, log, write, r), out: log.text(), dir };
  } finally { removeTree(dir); }
}

describe('check-dev-loop — the roster', () => {
  it('reads the names and the verify steps, de-duplicating', () => {
    const { names, inVerify } = commandRoster(roster({ names: ['verify', 'build', 'build'] }));
    assert.deepEqual(names, ['verify', 'build']);
    assert.deepEqual([...inVerify], ['build']);
  });

  it('renders one row per command, in roster order, ticking the verify column', () => {
    const rows = renderTable(roster(), DESCRIPTIONS);
    assert.match(rows[2], /^\| `verify` \|  \| the gate \|$/);
    assert.match(rows[3], /^\| `build` \| ✓ \| build it \|$/);
    assert.equal(rows.length, 5);
  });
});

describe('check-dev-loop — the registry has to fail in BOTH directions', () => {
  it('FAILS on a command with no entry, and names it', () => {
    const problems = registryProblems(['verify', 'build'], { verify: 'x' });
    assert.equal(problems.length, 1);
    assert.match(problems[0], /no `devLoopCommands` entry: build/);
  });

  it('FAILS on an entry naming no command, so the registry cannot rot', () => {
    const problems = registryProblems(['verify'], { verify: 'x', 'memory-gone': 'y' });
    assert.equal(problems.length, 1);
    assert.match(problems[0], /naming no command: memory-gone/);
  });

  it('treats a blank description as missing rather than as documented', () => {
    assert.match(registryProblems(['verify'], { verify: '   ' })[0], /no `devLoopCommands` entry: verify/);
  });

  it('FAILS on a `verify` step naming no command — it would run nothing', () => {
    const problems = registryProblems(['verify'], { verify: 'x' }, new Set(['check-gone']));
    assert.match(problems[0], /naming no command: check-gone/);
  });

  it('reports the registry problem through the gate itself, without touching the table', () => {
    const { code, out } = run(target(), { descriptions: { verify: 'the gate' } });
    assert.equal(code, 1);
    assert.match(out, /the command registry disagrees with the roster/);
    assert.match(out, /build, bench/);
  });
});

describe('check-dev-loop — the generated block', () => {
  it('passes when the table is what the roster renders', () => {
    const dir = makeTree({ [TARGET]: target() });
    const log = recorder();
    try {
      assert.equal(checkDevLoop(dir, { devLoopCommands: DESCRIPTIONS }, log, true, roster()), 0);
      assert.equal(checkDevLoop(dir, { devLoopCommands: DESCRIPTIONS }, log, false, roster()), 0);
      assert.match(log.text(), /3 command\(s\) documented, 1 of them in `verify`/);
    } finally { removeTree(dir); }
  });

  it('FAILS on a hand-edited table rather than accepting it', () => {
    const dir = makeTree({ [TARGET]: target() });
    try {
      checkDevLoop(dir, { devLoopCommands: DESCRIPTIONS }, recorder(), true, roster());
      const doc = path.join(dir, TARGET);
      fs.writeFileSync(doc, fs.readFileSync(doc, 'utf8').replace('build it', 'builds it, honest'), 'utf8');

      const log = recorder();
      assert.equal(checkDevLoop(dir, { devLoopCommands: DESCRIPTIONS }, log, false, roster()), 1);
      assert.match(log.text(), /command table is stale/);
    } finally { removeTree(dir); }
  });

  it('FAILS when the anchor pair is missing, as a STRUCTURE problem', () => {
    const { code, out } = run(target({ anchors: false }));
    assert.equal(code, 1);
    assert.match(out, /no `<!-- dev-loop:begin … <!-- dev-loop:end -->` block/);
  });

  it('FAILS on a duplicated begin anchor, which would splice over the text between them', () => {
    const { code, out } = run(target().replace(`${BEGIN} -->`, `${BEGIN} -->\nintro\n${BEGIN} -->`));
    assert.equal(code, 1);
    assert.match(out, /not a clean single anchor pair/);
  });

  it('FAILS CLOSED when the target is missing, rather than reporting a clean run', () => {
    const { code, out } = run(null);
    assert.equal(code, 1);
    assert.match(out, /is missing — nothing was checked/);
  });

  it('FAILS CLOSED on an empty roster', () => {
    const { code, out } = run(target(), { r: roster({ names: [], steps: [] }) });
    assert.equal(code, 1);
    assert.match(out, /the command roster is empty/);
  });
});

describe('check-dev-loop — the shipped roster', () => {
  it('every command carries a description, and `verify` is among them', async () => {
    const config = (await import('../../project.config.mjs')).default;
    const { names, inVerify } = commandRoster(ROSTER);
    assert.deepEqual(registryProblems(names, config.devLoopCommands, inVerify), []);
    assert.ok(names.includes('verify'));
  });
});
