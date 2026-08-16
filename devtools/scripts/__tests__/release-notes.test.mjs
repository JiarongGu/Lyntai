// release-notes — the GitHub Release body generator. See devtools/scripts/release-notes.mjs.
//
// The first test is a REGRESSION test for the measured defect that moved this code out of the workflow: the
// inline categorizer keyed on `^feat(scope)?:` / `^fix(scope)?:`, so every conventional-commit BREAKING
// marker (`feat(memory)!:`, `fix(core)!:`, `refactor(api)!:`) fell through to "Other changes" — 29 commits
// across this repository's history, and 11 of 11 in the v2.5.0..3.0 range.
//
// A generator whose failure mode is plausible-but-wrong OUTPUT cannot be validated by running it, which is
// why the negative cases matter as much as the positive one: the old code ran fine and published the wrong
// document. The last test pins the rule against the REAL history rather than a fixture, because the defect
// was invisible to every fixture nobody thought to write.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { NON_USER_FACING, categorize, previousTag, renderNotes, subjectsInRange } from '../release-notes.mjs';

describe('release-notes — regression: the BREAKING marker the workflow could not see', () => {
  it('puts a bang-marked commit under Breaking, whatever its kind, and never under Other', () => {
    const { breaking, other, features, fixes } = categorize([
      'feat(memory)!: abstention on a recall',
      'fix(core)!: a wrapper that narrowed its members',
      'refactor(api)!: the 3.0 naming sweep',
    ]);

    assert.equal(breaking.length, 3, 'all three are breaking changes');
    assert.deepEqual(other, [], 'none of them may land in the bucket the defect put them in');
    assert.deepEqual(features, []);
    assert.deepEqual(fixes, []);
  });

  it('keeps the scope in a breaking line, because "which package breaks" is the first thing needed', () => {
    const { breaking } = categorize(['fix(storage)!: three backends that disagreed']);
    assert.match(breaking[0], /fix\(storage\)/);
    assert.match(breaking[0], /three backends that disagreed/);
  });
});

describe('release-notes — breaking outranks the drop list', () => {
  // The sharpest form of the defect: `refactor:` is dropped as non-user-facing, and `refactor!:` is the
  // single most important line a consumer reads. Before this, the ONLY reason a breaking refactor was not
  // silently deleted is that the drop pattern also failed to match the `!` — correctness resting on a
  // regex failing.
  it('drops a plain refactor and keeps a breaking one', () => {
    const { breaking, dropped } = categorize([
      'refactor(core): extract the shared cut',
      'refactor(api)!: rename the seam',
    ]);
    assert.equal(dropped.length, 1, 'the non-breaking refactor is not news');
    assert.equal(breaking.length, 1, 'the breaking one is');
    assert.match(breaking[0], /rename the seam/);
  });

  it('does the same for every non-user-facing kind, so no kind is a hiding place', () => {
    for (const kind of NON_USER_FACING) {
      const { breaking, dropped } = categorize([`${kind}: ordinary work`, `${kind}!: a break`]);
      assert.equal(dropped.length, 1, `${kind}: should be dropped`);
      assert.equal(breaking.length, 1, `${kind}!: must survive as breaking`);
    }
  });
});

describe('release-notes — ordinary categorization', () => {
  it('routes feat and fix, with or without a scope', () => {
    const { features, fixes } = categorize(['feat: a thing', 'feat(memory): another', 'fix: a bug', 'fix(core): another']);
    assert.deepEqual(features, ['a thing', 'another']);
    assert.deepEqual(fixes, ['a bug', 'another']);
  });

  it('drops bench and tasks — the two kinds a census of the real history found leaking into the notes', () => {
    const { dropped, other } = categorize(['bench(memory): measure the sweep', 'tasks: re-verify the blockers']);
    assert.equal(dropped.length, 2);
    assert.deepEqual(other, []);
  });

  it('sends a compound prefix to Other rather than guessing which half wins', () => {
    // `docs+guards:` claims to be two things, one of which is dropped and one of which is not. Showing it
    // is the honest answer; picking a half silently would be the wrong kind of clever.
    const { other, dropped } = categorize(['docs+guards: repair three stale claims']);
    assert.equal(other.length, 1);
    assert.deepEqual(dropped, []);
  });

  it('sends an unprefixed subject to Other, and ignores blank lines', () => {
    const { other } = categorize(['just some words', '', '   ']);
    assert.deepEqual(other, ['just some words']);
  });
});

describe('release-notes — previousTag', () => {
  const tags = ['v0.28.5', 'v1.0.0', 'v1.6.0', 'v1.10.0', 'v2.0.1', 'v2.5.0'];

  it('compares numerically, not as text', () => {
    assert.equal(previousTag(tags, 'v2.0.1'), 'v1.10.0', 'v1.10.0 outranks v1.6.0');
  });

  it('skips the current tag and anything newer, so a re-cut does not diff against itself', () => {
    assert.equal(previousTag(tags, 'v2.5.0'), 'v2.0.1');
    assert.equal(previousTag(tags, 'v1.0.0'), 'v0.28.5');
  });

  it('picks the newest tag below a version that does not exist yet', () => {
    assert.equal(previousTag(tags, 'v3.0.0'), 'v2.5.0');
  });

  it('returns null for a first release, and ignores unparseable tags', () => {
    assert.equal(previousTag([], 'v1.0.0'), null);
    assert.equal(previousTag(['nightly', 'latest'], 'v1.0.0'), null);
    assert.equal(previousTag(['v1.2'], 'v1.3.0'), 'v1.2', 'a two-part tag still parses');
  });
});

describe('release-notes — rendering', () => {
  const subjects = ['feat(memory)!: a break', 'feat: a feature', 'fix: a bug', 'chore: invisible'];

  it('leads with Breaking and points at the migration guide', () => {
    const md = renderNotes('v3.0.0', subjects);
    const order = ['### Breaking changes', '### New features', '### Fixes'].map((h) => md.indexOf(h));
    assert.ok(order.every((i) => i > 0), 'every section is present');
    assert.deepEqual(order, [...order].sort((a, b) => a - b), 'and Breaking comes first');
    assert.match(md, /docs\/migration-2\.5-to-3\.0\.md/);
  });

  it('omits empty sections and the migration line when nothing breaks', () => {
    const md = renderNotes('v2.5.1', ['fix: a bug']);
    assert.doesNotMatch(md, /Breaking changes/);
    assert.doesNotMatch(md, /New features/);
    assert.doesNotMatch(md, /migration-2\.5-to-3\.0/);
    assert.match(md, /### Fixes/);
  });

  it('never emits an empty body, even when every commit was dropped', () => {
    const md = renderNotes('v2.5.1', ['chore: tidy', 'docs: reword']);
    assert.match(md, /See CHANGELOG\.md for details\./);
  });
});

describe('release-notes — pinned against the real history, not a fixture', () => {
  // The defect survived because no fixture existed to catch it. This asserts the rule over the actual
  // commit log, so a subject shape this repository really uses cannot regress unnoticed. It is a PROPERTY
  // ("no breaking commit lands in Other"), never a count — a count here would fail on the next commit.
  it('routes every bang-marked commit in the repository to Breaking', () => {
    const subjects = subjectsInRange('', 'HEAD');
    const bang = subjects.filter((s) => /^[a-z+]+(\([^)]+\))?!:/.test(s));
    assert.ok(bang.length > 0, 'the history must contain breaking commits for this to prove anything');

    const { breaking, other, dropped } = categorize(subjects);
    assert.ok(breaking.length >= bang.length, `all ${bang.length} bang-marked commits are breaking`);
    for (const list of [other, dropped]) {
      const leaked = list.filter((s) => /^[a-z+]+(\([^)]+\))?!:/.test(s));
      assert.deepEqual(leaked, [], 'no breaking commit may be miscategorized or dropped');
    }
  });
});
