import assert from 'node:assert/strict';
import { test } from 'node:test';

import { checkTautology } from '../check-tautology.mjs';
import { makeTree, removeTree } from './_fixtures.mjs';

/// A fixture tree, cleaned up whatever happens. No git needed: every fact passes the file list explicitly.
/// The guards' own tests are outside the gate's scan, so the literals below cannot reach it.
function withRepo(files, body) {
  const dir = makeTree(files);
  try { return body(dir); } finally { removeTree(dir); }
}

const run = (repo, files) => checkTautology(repo, () => {}, files);

// A guard whose failure mode is a FALSE PASS cannot be validated by running it, so every fact here is
// asserted in BOTH directions: the defect fails, and the nearest correct thing passes.
//
// Each fixture names its identifier ONCE and interpolates it on both sides. Writing the two sides by hand
// is how a test for a backreference silently stops testing anything — it would assert on two DIFFERENT
// names, which no pattern here matches, and pass against a gate that does nothing.
const NAME = 'IModelProvider';
const OTHER = 'IMediaJobProvider';

test('a backticked pair joined by "and" FAILS, and two different names PASS', () => {
  withRepo({ 'a.md': `the \`Id\` that \`${NAME}\` and \`${NAME}\` already have\n` }, (repo) => {
    assert.equal(run(repo, ['a.md']), 1, 'a name contrasted with itself must FAIL');
  });

  withRepo({ 'a.md': `the \`Id\` that \`${NAME}\` and \`${OTHER}\` already have\n` }, (repo) => {
    assert.equal(run(repo, ['a.md']), 0, 'two genuinely different names must PASS');
  });
});

test('every joiner a contrast is written with fires', () => {
  for (const joiner of ['and', 'or', 'vs', 'vs.', 'versus']) {
    withRepo({ 'a.md': `translating between \`${NAME}\` ${joiner} \`${NAME}\`\n` }, (repo) => {
      assert.equal(run(repo, ['a.md']), 1, `joiner "${joiner}" must fire`);
    });
  }
});

test('the slash pair and the parenthesised pair fire', () => {
  withRepo({ 'a.md': `\`${NAME}\` / \`${NAME}\`\n` }, (repo) => {
    assert.equal(run(repo, ['a.md']), 1, 'a slash pair must fire');
  });

  withRepo({ 'src/a.cs': `    // serves every provider seam (${NAME}, ${NAME}).\n` }, (repo) => {
    assert.equal(run(repo, ['src/a.cs']), 1, 'a parenthesised pair must fire');
  });
});

test('the UNBACKTICKED rule fires, and English\'s own repetitions do not', () => {
  withRepo({ 'a.md': `the ${NAME} and ${NAME} seams\n` }, (repo) => {
    assert.equal(run(repo, ['a.md']), 1, 'an unbackticked repeated identifier must fire');
  });

  // The false-positive direction, and the reason the rule demands an initial capital and five characters.
  const english = 'it grew over and over, and more and more of it was the same thing again and again\n';
  withRepo({ 'a.md': english }, (repo) => {
    assert.equal(run(repo, ['a.md']), 0, 'ordinary English repetition must not fire');
  });
});

test('a tautology broken across a WRAP is caught', () => {
  // This repository wraps at ~110 columns, so the half of a contrast that a sweep collapsed routinely sits
  // on the next line. A line-only matcher was `check-docs`' own blind spot and is re-made here on purpose.
  const wrapped = `gives an embedding backend the \`Id\` + \`IsAvailable\` that \`${NAME}\` and\n\`${NAME}\` already have.\n`;
  withRepo({ 'a.md': wrapped }, (repo) => {
    assert.equal(run(repo, ['a.md']), 1, 'a wrapped tautology must fire');
  });
});

test('`tautology-ok` silences the line it is on, and only the WINDOW hit on the line after', () => {
  const selfAnnotated = `\`${NAME}\` and \`${NAME}\` <!-- tautology-ok: quoting the defect -->\n`;
  withRepo({ 'a.md': selfAnnotated }, (repo) => {
    assert.equal(run(repo, ['a.md']), 0, 'the escape must silence its own line');
  });

  // The hole `check-docs` records from 2026-08-15: applying the N+1 escape to a SELF-line hit lets an
  // ordinary line inherit the exemption of whatever happens to follow it.
  const neighbourAnnotated = `\`${NAME}\` and \`${NAME}\`\nan unrelated line <!-- tautology-ok -->\n`;
  withRepo({ 'a.md': neighbourAnnotated }, (repo) => {
    assert.equal(run(repo, ['a.md']), 1, 'a self-line hit must NOT inherit the next line\'s escape');
  });
});

test('in code, a COMMENT is scanned and an expression is not', () => {
  withRepo({ 'src/a.cs': `    // ${NAME} and ${NAME} therefore keep their own Id.\n` }, (repo) => {
    assert.equal(run(repo, ['src/a.cs']), 1, 'a comment claim must fire');
  });

  // Correct code that repeats an argument. Both of these are in this tree and both are right.
  const code = 'var report = JudgeAgreement.Compare(scores, scores);\ngate = new Gate(new SemaphoreSlim(limit, limit));\n';
  withRepo({ 'src/a.cs': code }, (repo) => {
    assert.equal(run(repo, ['src/a.cs']), 0, 'a same-argument call is code, not a claim');
  });
});

test('a <see cref> naming an OVERLOAD by parameter type does not fire', () => {
  // `Math.Max(double,double)` is a signature. Three of these are in this tree, all correct, and they are
  // the whole reason the parenthesised rule needs the cref exclusion rather than a narrower regex.
  const cref = '    /// <para><see cref="Math.Max(double,double)"/> is not a finiteness guard.</para>\n';
  withRepo({ 'src/a.cs': cref }, (repo) => {
    assert.equal(run(repo, ['src/a.cs']), 0, 'a cref overload spec must not fire');
  });
});

test('HISTORICAL records ARE scanned — the difference from check-docs', () => {
  // `check-docs` exempts these because a record is "accurate BY using the vocabulary of its day". A
  // tautology was never anyone's vocabulary, and two of the six real defects lived exactly here. If this
  // fact ever goes green, the gate has been narrowed to the one scope that could not have found them.
  const files = {
    'CHANGELOG.md': `- an embedder carries the \`Id\` that \`${NAME}\` and \`${NAME}\` already do\n`,
    'docs/task-archive.md': `closing the split between \`${NAME}\` and \`${NAME}\`\n`,
    'docs/2026-07-17-lyntai-design.md': `three delivery modes: \`${NAME}\` and \`${NAME}\`\n`,
  };
  for (const [file, content] of Object.entries(files)) {
    withRepo({ [file]: content }, (repo) => {
      assert.equal(run(repo, [file]), 1, `${file} must be scanned by this gate`);
    });
  }
});

test('a devtools script is scanned, and the guards\' own tests are not — their fixtures ARE the defect', () => {
  const text = `// \`${NAME}\` and \`${NAME}\`\n`;
  withRepo({ 'devtools/scripts/x.mjs': text, 'devtools/scripts/__tests__/x.test.mjs': text }, (repo) => {
    assert.equal(run(repo, ['devtools/scripts/x.mjs']), 1, 'a devtools script is prose like any other code tier');
    assert.equal(run(repo, ['devtools/scripts/__tests__/x.test.mjs']), 0, 'a guard test quotes the defect by design');
  });
});

test('a run that scanned NOTHING fails rather than printing a tick', () => {
  withRepo({}, (repo) => {
    assert.equal(run(repo, []), 1, 'an empty source list is a broken listing, not a clean tree');
  });

  // A caller-supplied list that survives no filter is ordinary (a commit touching only `devtools/`), so
  // only the FULL-TREE path asserts on zero survivors.
  withRepo({ 'devtools/x.mjs': 'ok\n' }, (repo) => {
    assert.equal(run(repo, ['devtools/x.mjs']), 0, 'a filtered-to-empty caller list is not a failure');
  });
});

test('the failure message names the file and the line', () => {
  const two = `intro line\nthe \`Id\` that \`${NAME}\` and \`${NAME}\` have\n`;
  withRepo({ 'docs/a.md': two }, (repo) => {
    const out = [];
    const code = checkTautology(repo, (m) => out.push(m), ['docs/a.md']);

    assert.equal(code, 1);
    assert.ok(out.join('\n').includes('docs/a.md:2'), 'must name file and line');
  });
});

test('a hit is reported ONCE, in BOTH orientations', () => {
  // The window reads line i joined to line i + 1, so ONE defect can be seen twice in two different ways
  // and only one of them is fixed by the early return. Both directions are asserted because the first
  // version of this gate guarded one and shipped the other: six real defects reported as ten, and the
  // fixture below — defect FIRST — passed the whole time.
  const once = (content) => withRepo({ 'a.md': content }, (repo) => {
    const out = [];
    checkTautology(repo, (m) => out.push(m), ['a.md']);
    return out.filter((l) => l.includes('a.md:')).length;
  });

  // Defect on line 1: line 1 self-hits, and its own window would hit again.
  assert.equal(once(`\`${NAME}\` and \`${NAME}\`\na following line\n`), 1, 'defect-first must report once');

  // Defect on line 2: line 1 does NOT self-hit, so it falls through to the window — which contains the
  // whole defect. This is the orientation that shipped broken, and it is the common one, because a
  // tautology that wraps sits under a heading far more often than above one.
  assert.equal(once(`## a heading line\n\`${NAME}\` and \`${NAME}\`\n`), 1, 'defect-second must report once');
});

test('a defect genuinely SPANNING the wrap is still reported', () => {
  // The guard above skips a window match lying wholly in the continuation. It must not skip one that
  // really does straddle the join, or fixing the duplicate would have silently removed the wrap coverage
  // the gate exists to have — the permissive direction, invisible in a green run.
  withRepo({ 'a.md': `the \`Id\` that \`${NAME}\` and\n\`${NAME}\` already have\n` }, (repo) => {
    const out = [];
    const code = checkTautology(repo, (m) => out.push(m), ['a.md']);

    assert.equal(code, 1, 'a straddling defect must still fire');
    assert.equal(out.filter((l) => l.includes('a.md:')).length, 1, 'and exactly once');
  });
});

// The RENAME shape. It is separated from the joiner fact above because the failure it catches is a
// different one: a joiner contrast is collapsed by a sweep that rewrote one side, a rename entry by a sweep
// that rewrote both — and a rename entry is old-then-new BY CONSTRUCTION, so it is the shape most exposed
// to a rename campaign. A live `CHANGELOG.md` entry carried it for two days past every green gate.
test('a rename that names the same identifier on both sides FAILS, and a real rename PASSES', () => {
  for (const verb of ['is renamed', 'renamed to', 'becomes', 'replaces', 'is replaced by']) {
    withRepo({ 'a.md': `- **\`${NAME}\` ${verb} \`${NAME}\`.** the package is named for what it drags\n` }, (repo) => {
      assert.equal(run(repo, ['a.md']), 1, `rename verb "${verb}" must fire`);
    });

    withRepo({ 'a.md': `- **\`${OTHER}\` ${verb} \`${NAME}\`.** the package is named for what it drags\n` }, (repo) => {
      assert.equal(run(repo, ['a.md']), 0, `a GENUINE rename via "${verb}" must pass`);
    });
  }
});

test('the arrow form a rename TABLE uses fires, and keeps two different names clean', () => {
  for (const arrow of ['→', '->', '=>']) {
    withRepo({ 'a.md': `| \`${NAME}\` ${arrow} \`${NAME}\` | the naming pass |\n` }, (repo) => {
      assert.equal(run(repo, ['a.md']), 1, `arrow "${arrow}" must fire`);
    });

    withRepo({ 'a.md': `| \`${OTHER}\` ${arrow} \`${NAME}\` | the naming pass |\n` }, (repo) => {
      assert.equal(run(repo, ['a.md']), 0, `a GENUINE rename via "${arrow}" must pass`);
    });
  }
});
