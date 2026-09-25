// dev-cli — the dispatcher and the roster it dispatches over. See devtools/dev.mjs and devtools/commands.mjs.
//
// The roster is DATA, so these read it directly; before, three tools regex-parsed the dispatcher's `case`
// labels and one test compared that regex against a copy of itself.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';

import { COMMANDS, VERIFY_STEPS } from '../../commands.mjs';
import { BUILTINS } from '../../dev.mjs';
import { repoRoot } from './_fixtures.mjs';

describe('the command roster', () => {
  it('names every command once, and each runs exactly ONE way', () => {
    const names = COMMANDS.map((c) => c.name);
    assert.equal(new Set(names).size, names.length, 'a duplicated name would shadow the first');
    for (const c of COMMANDS) {
      const ways = ['script', 'bench', 'builtin'].filter((k) => c[k] !== undefined);
      assert.equal(ways.length, 1, `${c.name} declares ${ways.join(' + ') || 'no way to run'}`);
    }
  });

  it('every script a command names exists', () => {
    const missing = COMMANDS.filter((c) => c.script)
      .filter((c) => !fs.existsSync(path.join(repoRoot, 'devtools', 'scripts', `${c.script}.mjs`)));
    assert.deepEqual(missing.map((c) => c.name), []);
  });

  it('every builtin has an implementation in dev.mjs, and dev.mjs implements nothing the roster lacks', () => {
    const declared = COMMANDS.filter((c) => c.builtin).map((c) => c.name).sort();
    assert.deepEqual(Object.keys(BUILTINS).sort(), declared);
  });

  it('every bench flag is distinct — two commands must never run the same sweep', () => {
    const flags = COMMANDS.filter((c) => c.bench).map((c) => c.bench);
    assert.equal(new Set(flags).size, flags.length);
  });

  it('every verify step is a command, and the guard tests run FIRST', () => {
    const names = new Set(COMMANDS.map((c) => c.name));
    assert.deepEqual(VERIFY_STEPS.filter(([s]) => !names.has(s)), []);
    assert.equal(VERIFY_STEPS[0][0], 'test-devtools');
  });
});
