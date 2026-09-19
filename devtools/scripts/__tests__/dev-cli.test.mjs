import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { commandNames, testSummaryCounts } from '../../dev.mjs';
import { repoRoot } from './_fixtures.mjs';

// `node devtools/dev.mjs` with no argument is what CLAUDE.md calls "the authoritative list" of commands.
// It was a hand-maintained literal and had drifted to 24 of 30 — every memory sweep except `memory-sweep`,
// plus `check-encoding` on the day it was added. A list that is authoritative by DOCUMENTATION and stale by
// CONSTRUCTION is worse than no list, because a reader trusts it.

const devSource = () => fs.readFileSync(path.join(repoRoot, 'devtools', 'dev.mjs'), 'utf8');

test('every dispatched command is discoverable', () => {
  const src = devSource();
  const cases = [...new Set([...src.matchAll(/^\s*case '([a-z][a-z0-9-]*)':/gm)].map((m) => m[1]))];

  assert.deepEqual(commandNames(src), cases);
  assert.ok(cases.length > 25, `expected the full command set, found ${cases.length}`);
});

test('the commands CLAUDE.md documents actually exist', () => {
  // The other direction, and the one that catches a REMOVED command still being documented. Named
  // explicitly rather than parsed out of the prose: a regex over documentation would match its own examples
  // and counter-examples, and would go quiet the moment the wording changed.
  const documented = [
    'verify', 'build', 'test', 'test-devtools', 'e2e', 'bench', 'pack', 'doctor', 'playground',
    'check-warnings', 'check-packages', 'check-bundle', 'check-docs', 'check-encoding',
    'check-api-vocabulary', 'check-samples', 'check-sensitive', 'check-version',
    'consumer-smoke', 'install-hooks', 'decisions-index', 'changelog',
    'new-migration', 'new-package',
    'memory-sweep', 'memory-language', 'memory-spacing',
  ];
  const actual = new Set(commandNames(devSource()));

  const missing = documented.filter((d) => !actual.has(d));
  assert.deepEqual(missing, [], `documented in CLAUDE.md but not implemented: ${missing.join(', ')}`);
});

test('commandNames dedupes fall-through cases', () => {
  const fake = "case 'a':\n  case 'b':\n    break;\n  case 'a':\n";

  assert.deepEqual(commandNames(fake), ['a', 'b']);
});

test('commandNames ignores things that merely look like cases', () => {
  // A string containing `case 'x':` inside a comment or a message must not become a command. The anchor is
  // leading whitespace to start of line, which is what distinguishes a real switch arm from prose.
  const fake = "// see case 'ghost': for why\nconst s = \"case 'phantom':\";\n  case 'real':\n";

  assert.deepEqual(commandNames(fake), ['real']);
});

// `test-devtools` reads the runner's SUMMARY rather than its exit code alone, so how that summary is
// spelled is load-bearing. The counts are line-anchored on `ℹ`, and the spec reporter puts a colour escape
// BEFORE that glyph whenever FORCE_COLOR is set — which it honours through a pipe, which is exactly how the
// gate spawns it. Measured: a 871/871 run with `status 0` reported "the scripts that GATE this repository
// are themselves failing", because every count parsed as NaN and `tests > 0` is false for NaN.
test('the summary counts survive a COLOURED reporter, which is how the gate really runs it', () => {
  const plain = 'ℹ tests 871\nℹ suites 190\nℹ pass 871\nℹ fail 0\n';
  const coloured = '\u001b[34mℹ tests 871\u001b[39m\n\u001b[34mℹ pass 871\u001b[39m\n\u001b[34mℹ fail 0\u001b[39m\n';

  assert.deepEqual(testSummaryCounts(plain), { tests: 871, passed: 871, failed: 0 });
  assert.deepEqual(testSummaryCounts(coloured), { tests: 871, passed: 871, failed: 0 });
});

test('an UNREADABLE summary is distinguishable from a real failure', () => {
  // The two must not collapse: 0 failures with an unparseable summary means the gate could not READ the
  // run, and saying "tests failed" there sends the next reader hunting for a test that is perfectly green.
  const { tests, passed, failed } = testSummaryCounts('the runner crashed before it summarised anything');

  assert.ok(![tests, passed, failed].some(Number.isFinite), 'counts must be NaN, never a plausible zero');
});
