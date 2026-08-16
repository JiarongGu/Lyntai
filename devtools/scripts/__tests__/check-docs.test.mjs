// check-docs — the prose gate (retired vocabulary). See devtools/scripts/check-docs.mjs.
//
// The first three tests are REGRESSION tests for three measured defects, all found in one sitting on
// 2026-08-11 and all of which had passed every gate for their whole lifetime because each failed in the
// PERMISSIVE direction (TASKS.md Part 60). Each was mutation-checked: revert that specific fix in
// check-docs.mjs and that test fails. They are not coverage theatre — they are the reason this file exists.
import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { HISTORICAL, IN_SCOPE, IS_SCANNED, checkDocs, liveLineCount, trackedFiles } from '../check-docs.mjs';
import { git, makeRepo, makeTree, recorder, removeTree } from './_fixtures.mjs';

/** One rule, phrased like a real registry entry: a CLAIM shape, short enough to sit inside one wrapped line. */
const claimRule = {
  term: 'available,? (?:but )?not (?:the )?default',
  use: '`X` IS the default as of 3.0',
  why: 'the default changed 2026-08-11',
};
const config = { retiredTerms: [claimRule] };

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const realConfig = (await import('../../project.config.mjs')).default;

/** Run the gate over a fixture tree with an INJECTED file list (no git fixture needed for the scan itself). */
function run(files, { rules = config } = {}) {
  const dir = makeTree(files);
  const log = recorder();
  try {
    return { code: checkDocs(dir, rules, log, Object.keys(files)), out: log.text() };
  } finally {
    removeTree(dir);
  }
}

describe('check-docs — regression: the three measured defects', () => {
  it('defect 1: catches a claim that spans a LINE WRAP (line-only matching missed every wrapped claim)', () => {
    // The exact shape that sat stale in CLAUDE.md — the file auto-loaded into every session — for the whole
    // window in which it was false, while this gate reported that file clean.
    const { code, out } = run({
      'docs/ranking.md': '# Ranking\n\nThe seam ships `ReciprocalRankFusionPolicy`, available\n'
        + 'but not the default, alongside the multiplicative one.\n',
    });
    assert.equal(code, 1, 'a claim spanning a wrap must be caught');
    assert.match(out, /docs\/ranking\.md:3/, 'reported at the FIRST line of the wrapped pair');
    assert.match(out, /1 use\(s\) of retired vocabulary/);
  });

  it('defect 1b: a wrapped claim is still caught when the wrap falls mid-word-boundary, and only ONCE', () => {
    const { code, out } = run({
      'docs/a.md': 'x\ny available\nnot default z\n',
    });
    assert.equal(code, 1);
    assert.equal((out.match(/docs\/a\.md:/g) ?? []).length, 1, 'one hit, not one per window');
  });

  it('defect 2: CLAUDE.md and TASKS.md are IN SCOPE (three startsWith calls silently omitted both)', () => {
    for (const file of ['CLAUDE.md', 'TASKS.md']) {
      const { code, out } = run({ [file]: 'The policy is available but not the default.\n' });
      assert.equal(code, 1, `${file} must be scanned — it is maintained state`);
      assert.match(out, new RegExp(`${file}:1`));
    }
    // …and the scope predicate itself, in both directions.
    for (const p of ['README.md', 'CLAUDE.md', 'TASKS.md', 'docs/x.md', '.claude/rules/y.md'])
      assert.equal(IN_SCOPE(p), true, `${p} should be in scope`);
    for (const p of ['src/Lyntai.Core/README.md', 'tests/notes.md', 'devtools/x.md', 'CONTRIBUTING.md'])
      assert.equal(IN_SCOPE(p), false, `${p} should be out of scope`);
  });

  it('defect 3: the word SUPERSEDED in ordinary PROSE no longer exempts the whole file', () => {
    // The old escape hatch matched `\bSUPERSEDED\b` anywhere in the first 21 lines, so a maintained document
    // whose intro merely said "section X below is superseded" stopped being checked entirely — silently.
    const { code, out } = run({
      'docs/live.md': '# Live design\n\nSection 4 below is SUPERSEDED by the new engine; everything else\n'
        + 'stands.\n\nThe policy is available but not the default.\n',
    });
    assert.equal(code, 1, 'a mention is not a declaration — the file stays checked');
    assert.match(out, /docs\/live\.md:6/);
    assert.doesNotMatch(out, /superseded record\(s\) skipped/);
  });
});

describe('check-docs — the superseded ESCAPE, tightened to a declaration', () => {
  const stale = 'The policy is available but not the default.\n';

  it('exempts an emphasized banner, block-quoted or not', () => {
    for (const banner of [
      '**SUPERSEDED — replaced by the 3.0 design**\n\n',
      '**Status: SUPERSEDED (2026-08-01)**\n\n',
      '> **SUPERSEDED by docs/2026-08-11-x.md**\n\n',
      '# Title\n\n> **Status: SUPERSEDED**\n\n',
    ]) {
      const { code, out } = run({ 'docs/old.md': banner + stale });
      assert.equal(code, 0, `should be exempt: ${JSON.stringify(banner)}`);
      assert.match(out, /1 superseded record\(s\) skipped/);
    }
  });

  it('does not exempt a banner that appears past the opening 21 lines', () => {
    const { code } = run({ 'docs/late.md': '\n'.repeat(25) + '**SUPERSEDED**\n\n' + stale });
    assert.equal(code, 1, 'the escape is an OPENING banner, not any banner');
  });
});

describe('check-docs — drift-ok, the honest annotation', () => {
  it('silences a single-line hit', () => {
    const { code } = run({ 'docs/rule.md': 'Never write "available but not the default". <!-- drift-ok -->\n' });
    assert.equal(code, 0);
  });

  it('silences a WRAPPED hit from either line of the pair', () => {
    const first = run({ 'docs/a.md': 'The policy is available <!-- drift-ok -->\nbut not the default.\n' });
    const second = run({ 'docs/b.md': 'The policy is available\nbut not the default. <!-- drift-ok -->\n' });
    assert.equal(first.code, 0, 'annotating the first line of the pair is enough');
    assert.equal(second.code, 0, 'annotating the second line of the pair is enough');
  });
});

describe('check-docs — CHANGELOG.md is historical only BELOW its first released heading', () => {
  // TASKS.md Part 53, closed 2026-08-11. The wholesale exemption rested on records being "accurate BY using
  // the vocabulary of their day" — true of a released section, false of `## Unreleased`, which describes
  // behaviour that has not shipped and can still change under the words describing it. Measured 2026-08-09:
  // the RRF entry kept asserting the pre-fix tie behaviour AND its retired justification after the code
  // changed, the gate reported 39 docs clean, and a human found it.
  const stale = 'RRF is available but not the default.';
  const changelog = (...body) => ({ 'CHANGELOG.md': `# Changelog\n\n${body.join('\n')}\n` });

  it('IS IN SCOPE AT ALL — the half that made the first attempt at this inert', () => {
    // `IN_SCOPE` runs BEFORE the historical filter, and it never listed CHANGELOG.md. So narrowing the
    // exemption changed nothing: the file was already excluded one step earlier, and the gate reported the
    // same clean run either way. Found 2026-08-11 by probing the gate rather than trusting its green line.
    assert.equal(IN_SCOPE('CHANGELOG.md'), true);
    assert.equal(IS_SCANNED('CHANGELOG.md'), true, 'partly historical is still read');
    assert.equal(IS_SCANNED('docs/task-archive.md'), false, 'wholly historical is not');
  });

  /**
   * `docs/DECISIONS.md` is a CURRENT-STATE record as of 2026-08-14 — every entry states what is true now,
   * so unlike the task archive it has no historical half and no exemption. Pinned because it briefly had
   * one: a frozen companion file was carved out of the scan, and deleting that file without removing the
   * exemption would have left a rule matching nothing — the rot `check-api-vocabulary`'s allowances fail on.
   */
  it('gives the decision record NO historical exemption — it is maintained state', () => {
    assert.equal(IN_SCOPE('docs/DECISIONS.md'), true);
    assert.equal(IS_SCANNED('docs/DECISIONS.md'), true);
    assert.equal(run({ 'docs/DECISIONS.md': `${stale}\n` }).code, 1);

    assert.ok(!HISTORICAL.some((re) => re.test('docs/decisions-archive.md')),
      'the deleted archive must not leave an exemption behind');
  });

  it('CATCHES a stale claim under ## Unreleased', () => {
    const { code, out } = run(changelog('## Unreleased', '', `- ${stale}`, '', '## 2.5.0 — 2026-08-08', '', '- old'));
    assert.equal(code, 1);
    assert.match(out, /CHANGELOG\.md:5/, 'reported at its real line — the scan is a PREFIX, so numbers hold');
  });

  it('leaves the same claim alone below the first released heading', () => {
    const { code } = run(changelog('## Unreleased', '', '- something new', '', '## 2.5.0 — 2026-08-08', '',
      `- ${stale}`));
    assert.equal(code, 0, 'a released section is accurate BY using the vocabulary of its day');
  });

  it('never joins the last live line to the first historical one', () => {
    // The two-line window is what catches a claim spanning a wrap; it must not manufacture one across the
    // boundary, which would report a released section through a live line's coat-tails.
    const { code } = run(changelog('## Unreleased', '', '- RRF is available', '## 2.5.0 — 2026-08-08',
      'but not the default'));
    assert.equal(code, 0);
  });

  it('treats a CHANGELOG with nothing released yet as entirely live', () => {
    const { code, out } = run(changelog('## Unreleased', '', `- ${stale}`));
    assert.equal(code, 1);
    assert.match(out, /CHANGELOG\.md:5/);
  });

  it('honours drift-ok inside the live prefix', () => {
    const { code } = run(changelog('## Unreleased', '', `- ${stale} <!-- drift-ok -->`, '', '## 2.5.0 — x'));
    assert.equal(code, 0);
  });

  it('counts the live lines: everything above the first `## <major>.<minor>.<patch>` heading', () => {
    const lines = ['# Changelog', '', '## Unreleased', '', '- a thing', '', '## 2.5.0 — 2026-08-08', '', '- old'];
    assert.equal(liveLineCount('CHANGELOG.md', lines), 6, 'the boundary heading itself is not live');
    assert.equal(liveLineCount('CHANGELOG.md', ['# Changelog', '## Unreleased']), 2, 'nothing released yet');
    assert.equal(liveLineCount('docs/design.md', lines), Infinity, 'an ordinary document is live throughout');
  });
});

describe('check-docs — the CODE tiers, comment lines only', () => {
  it('catches a retired claim in an XML doc comment, which nothing gated before', () => {
    // The measured hole: the compiler resolves `<see cref>` and NOTHING else, so a claim in a `<c>` tag or a
    // `//` comment was checked by no gate at all. `check-api-vocabulary` covers retired IDENTIFIERS on the
    // frozen surface; a retired CLAIM had no owner.
    const { code, out } = run({ 'src/A.cs': '/// <summary>RRF is available but not the default.</summary>\nclass A;\n' });
    assert.equal(code, 1, out);
    assert.match(out, /src\/A\.cs:1/);
  });

  it('IGNORES the same words in a string literal — data the program uses, not a claim', () => {
    // The narrowing `check-links` already applies to its own code scan. A prompt, a SQL fragment or a test
    // fixture may legitimately contain any phrase; only a comment asserts something to a reader.
    const { code, out } = run({ 'src/A.cs': 'const string S = "available but not the default";\nclass A;\n' });
    assert.equal(code, 0, out);
  });

  it('blanks non-comment lines rather than dropping them, so line numbers stay true', () => {
    const text = 'class A;\n\n\n/// RRF is available but not the default\nclass B;\n';
    const { code, out } = run({ 'src/A.cs': text });
    assert.equal(code, 1);
    assert.match(out, /src\/A\.cs:4/, 'the hit must report its REAL line, not its index among comments');
  });

  it('honours drift-ok on a comment line, the same escape prose uses', () => {
    const line = '/// available but not the default — drift-ok: names the retired default deliberately\nclass A;\n';
    assert.equal(run({ 'src/A.cs': line }).code, 0);
  });

  it('does NOT scan devtools/, because the registry that defines the terms lives there', () => {
    // Structural, not a judgement call: a registry necessarily quotes every term it bans, and scanning it
    // yields only the rules themselves (15 hits, measured). `check-encoding` met the identical problem and
    // solved it by construction — patterns stored as code points — which prose patterns cannot do.
    assert.equal(run({ 'devtools/project.config.mjs': '// available but not the default\n' }).code, 0);
    assert.equal(run({ 'devtools/scripts/x.mjs': '// available but not the default\n' }).code, 0);
  });

  it('scans tests/ and bench/, not just src/ — the tiers a review would forget', () => {
    assert.equal(run({ 'tests/T.cs': '// available but not the default\nclass T;\n' }).code, 1);
    assert.equal(run({ 'bench/B.cs': '// available but not the default\nclass B;\n' }).code, 1);
  });

  it('the real tree is clean under the widened scope', () => {
    // Pinned against the tree rather than a fixture: closing this hole cost four annotations once, and a
    // regression would be a stale CLAIM shipping in a doc comment — the exact defect it was opened for.
    const log = recorder();
    assert.equal(checkDocs(repo, realConfig, log), 0, log.text());
    assert.match(log.text(), /\d{3,} doc\(s\) clean/, 'the widened scan must reach the hundreds, not 45');
  });
});

describe('check-docs — what is deliberately NOT scanned', () => {
  it('skips the task archive, which is accurate BY using the vocabulary of its day', () => {
    const { code } = run({ 'docs/task-archive.md': 'RRF is available but not the default.\n' });
    assert.equal(code, 0);
  });

  it('skips non-prose files, and scans .html as well as .md', () => {
    const skipped = run({ 'docs/notes.txt': 'available but not the default\n' });
    assert.equal(skipped.code, 0, '.txt is not prose this gate owns');
    const page = run({ 'docs/design.html': '<p>available but not the default</p>\n' });
    assert.equal(page.code, 1, 'the published design PAGE is tracked prose and is scanned');
  });

  it('says so, and passes, when the registry is empty', () => {
    const { code, out } = run({ 'docs/x.md': 'available but not the default\n' }, { rules: { retiredTerms: [] } });
    assert.equal(code, 0);
    assert.match(out, /no retired terms configured/);
  });
});

describe('check-docs — the report', () => {
  it('groups hits by rule and teaches the replacement', () => {
    const { code, out } = run({
      'docs/one.md': 'available but not the default\n',
      'docs/two.md': 'x\navailable but not the default\n',
    });
    assert.equal(code, 1);
    assert.match(out, /docs\/one\.md:1/);
    assert.match(out, /docs\/two\.md:2/);
    assert.match(out, /say `X` IS the default as of 3\.0 instead/);
    assert.match(out, /why: the default changed 2026-08-11/);
  });
});

describe('check-docs — the tracked-file list', () => {
  it('returns a NON-ASCII path unquoted, so a CJK-named doc is actually scanned', (t) => {
    // Without `-z`, git C-quotes such a path (`"docs/\347\201\265\345\217\260.md"`); the read then fails and
    // check-docs' own `catch` skips the file — never scanned, never reported as unscanned. Measured
    // 2026-08-11 while writing these tests; the same root cause as check-sensitive's twin.
    const dir = makeRepo({ 'docs/灵台.md': 'available but not the default\n' });
    t.after(() => removeTree(dir));
    git(dir, ['add', '.']);

    const files = trackedFiles(dir);
    assert.deepEqual(files, ['docs/灵台.md'], 'the path must arrive raw, not C-quoted');

    const log = recorder();
    assert.equal(checkDocs(dir, config, log), 1, 'and the file must actually be scanned');
    assert.match(log.text(), /docs\/灵台\.md:1/);
  });
});

describe('check-docs — fail-closed on an empty scan', () => {
  it('an empty listing FAILS rather than printing a tick', () => {
    // check-api-vocabulary's rule, which this gate was missing until 2026-08-15. A scanner that reports
    // success over zero files is the most permissive failure there is: every check above it is vacuous and
    // the output is indistinguishable from a genuinely clean tree.
    const log = recorder();
    assert.equal(checkDocs('/nowhere', config, log, []), 1);
    assert.match(log.text(), /found no maintained documents/);
    assert.match(log.text(), /proves nothing/);
  });

  it('a caller-supplied list with no maintained doc in it still PASSES', () => {
    // The other direction, and the reason the guard is not simply `tracked.length === 0`: a commit that
    // touches only `src/` legitimately has nothing for this gate to read. Only the FULL-TREE path can
    // indict IN_SCOPE, because only there is a zero impossible — README, CLAUDE.md and TASKS.md guarantee
    // three. Written after the first shape of this guard broke check-encoding's binary-only test.
    const { code, out } = run({ 'src/a.cs': '// nothing to see\n' });
    assert.equal(code, 0, out);
  });
});

describe('check-docs — `drift-ok` must not silence the line ABOVE it', () => {
  it('a drift-ok on line N+1 does not excuse a stale claim that stands alone on line N', () => {
    // The escape covers the joined window so that annotating EITHER line of a wrapped passage is enough —
    // correct, and the reason it reads line N+1 at all. But it was applied before the line-alone test too,
    // so an ordinary line inherited the exemption of whatever happened to follow it. Two unrelated
    // paragraphs is all it takes: the second legitimately names the retired thing and is annotated, and the
    // first silently stops being checked. Found 2026-08-15.
    const { code, out } = run({
      'docs/a.md': 'The ranking policy is available, not default in 3.0.\n'
        + 'The rule below bans that phrasing deliberately. drift-ok\n',
    });
    assert.equal(code, 1, out);
    assert.match(out, /docs\/a\.md:1/);
  });

  it('but a drift-ok on either line of a WRAPPED claim still excuses it', () => {
    // The behaviour that must survive the fix — this is why the window reads N+1 in the first place.
    const { code, out } = run({
      'docs/b.md': 'The ranking policy is available,\nnot default in 3.0. drift-ok\n',
    });
    assert.equal(code, 0, out);
  });
});
