// check-dev-loop's own tests.
//
// The gate's claim is that `CLAUDE.md`'s command table cannot disagree with `dev.mjs`. Every way it could
// fail permissively has to be caught: an undocumented command renders a blank cell that reads like a
// command doing nothing, a dead registry entry silently stops covering what it was written for, and a
// hand-edited table is exactly the drift this replaces. Each of those looks like a complete table.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  BEGIN, END, TARGET, checkDevLoop, commandRoster, registryProblems, renderTable,
} from '../check-dev-loop.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

const COMMANDS = { verify: 'the gate', build: 'build it', bench: 'measure it' };

/** A `dev.mjs` with the two shapes the gate reads: `case` labels, and `verify`'s `steps` array. */
const devSource = ({ cases = ['verify', 'build', 'bench'], steps = ['build'] } = {}) => [
  'switch (cmd) {',
  ...cases.map((c) => `  case '${c}': {\n    break;\n  }`),
  '}',
  `const steps = [${steps.map((s) => `['${s}', []]`).join(', ')}];`,
].join('\n');

/** A target document carrying the anchor pair, optionally with a stale or hand-edited body. */
const target = ({ body = [], anchors = true } = {}) =>
  ['# Doc', '', '## Dev loop', '', ...(anchors ? [`${BEGIN} -->`, '', ...body, '', END] : []), ''].join('\n');

function run(files, config = { devLoopCommands: COMMANDS }, write = false) {
  const dir = makeTree(files);
  const log = recorder();
  try {
    return { code: checkDevLoop(dir, config, log, write), out: log.text(), dir };
  } finally { removeTree(dir); }
}

const tree = (opts = {}, doc = {}) => ({
  'devtools/dev.mjs': devSource(opts),
  [TARGET]: target(doc),
});

describe('check-dev-loop — the roster read out of dev.mjs', () => {
  it('reads the case labels and the verify steps, de-duplicating fall-through', () => {
    const dir = makeTree({ 'devtools/dev.mjs': devSource({ cases: ['verify', 'build', 'build'] }) });
    try {
      const { names, inVerify } = commandRoster(dir);
      assert.deepEqual(names, ['verify', 'build']);
      assert.deepEqual([...inVerify], ['build']);
    } finally { removeTree(dir); }
  });

  it('matches an OUTER steps entry, never an inner argument array', () => {
    const dir = makeTree({
      'devtools/dev.mjs': "switch (cmd) {}\nconst steps = [['check-sensitive', ['--tree']], ['e2e', []]];",
    });
    try {
      assert.deepEqual([...commandRoster(dir).inVerify], ['check-sensitive', 'e2e']);
    } finally { removeTree(dir); }
  });

  it('renders one row per command, in declaration order, ticking the verify column', () => {
    const dir = makeTree({ 'devtools/dev.mjs': devSource() });
    try {
      const rows = renderTable(dir, COMMANDS);
      assert.match(rows[2], /^\| `verify` \|  \| the gate \|$/);
      assert.match(rows[3], /^\| `build` \| ✓ \| build it \|$/);
      assert.equal(rows.length, 5);
    } finally { removeTree(dir); }
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

  it('reports the registry problem through the gate itself, without touching the table', () => {
    const { code, out } = run(tree(), { devLoopCommands: { verify: 'the gate' } });
    assert.equal(code, 1);
    assert.match(out, /the command registry disagrees with `dev.mjs`/);
    assert.match(out, /build, bench/);
  });
});

describe('check-dev-loop — the generated block', () => {
  it('passes when the table is what the source renders', () => {
    const dir = makeTree(tree());
    const log = recorder();
    try {
      assert.equal(checkDevLoop(dir, { devLoopCommands: COMMANDS }, log, true), 0);
      assert.equal(checkDevLoop(dir, { devLoopCommands: COMMANDS }, log), 0);
      assert.match(log.text(), /3 command\(s\) documented, 1 of them in `verify`/);
    } finally { removeTree(dir); }
  });

  it('FAILS on a hand-edited table rather than accepting it', () => {
    const dir = makeTree(tree());
    const log = recorder();
    try {
      checkDevLoop(dir, { devLoopCommands: COMMANDS }, log, true);
      const doc = path.join(dir, TARGET);
      fs.writeFileSync(doc, fs.readFileSync(doc, 'utf8').replace('build it', 'builds it, honest'), 'utf8');

      const log2 = recorder();
      assert.equal(checkDevLoop(dir, { devLoopCommands: COMMANDS }, log2), 1);
      assert.match(log2.text(), /command table is stale/);
    } finally { removeTree(dir); }
  });

  it('FAILS when the anchor pair is missing, as a STRUCTURE problem', () => {
    const { code, out } = run(tree({}, { anchors: false }));
    assert.equal(code, 1);
    assert.match(out, /no `<!-- dev-loop:begin … <!-- dev-loop:end -->` block/);
  });

  it('FAILS on a duplicated begin anchor, which would splice over the text between them', () => {
    const doc = target().replace(`${BEGIN} -->`, `${BEGIN} -->\nintro\n${BEGIN} -->`);
    const { code, out } = run({ 'devtools/dev.mjs': devSource(), [TARGET]: doc });
    assert.equal(code, 1);
    assert.match(out, /not a clean single anchor pair/);
  });

  it('FAILS CLOSED when the target is missing, rather than reporting a clean run', () => {
    const { code, out } = run({ 'devtools/dev.mjs': devSource() });
    assert.equal(code, 1);
    assert.match(out, /is missing — nothing was checked/);
  });

  it('FAILS CLOSED when dev.mjs declares no commands at all', () => {
    const { code, out } = run({ 'devtools/dev.mjs': '// nothing here', [TARGET]: target() });
    assert.equal(code, 1);
    assert.match(out, /read no commands out of devtools\/dev\.mjs/);
  });
});

describe('check-dev-loop — against the real tree', () => {
  it('is green, and every real command carries a description', async () => {
    const config = (await import('../../project.config.mjs')).default;
    const log = recorder();

    assert.equal(checkDevLoop(repo, config, log), 0, log.text());
    assert.deepEqual(registryProblems(commandRoster(repo).names, config.devLoopCommands), []);
  });

  it('documents `verify` itself, which is the command the table exists to route to', async () => {
    const config = (await import('../../project.config.mjs')).default;
    assert.ok(commandRoster(repo).names.includes('verify'));
    assert.ok(config.devLoopCommands.verify?.trim());
  });
});
