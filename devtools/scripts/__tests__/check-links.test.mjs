// check-links — the dangling-reference gate. See devtools/scripts/check-links.mjs.
//
// The first test is a REGRESSION test for the measured defect that created this gate: untracking the
// ranking × forgetting measurement record under D43 left six references to `docs/2026-08-09-…md` in
// maintained state (README ×3, the design contract, DECISIONS ×2) while every gate stayed green and a
// reader found them. `docs/superpowers/INDEX.md` already ended its archiving procedure with "check nothing
// dangles"; this file is what makes that step fail loudly instead of being remembered.
//
// A guard whose failure mode is a false PASS cannot be validated by running it, which is why the negative
// cases below matter as much as the positive one (TASKS.md Part 60).
import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { PATH_PATTERN, checkLinks, trackedFiles } from '../check-links.mjs';
import { git, makeRepo, makeTree, recorder, removeTree } from './_fixtures.mjs';

const noAllowances = { staleReferenceAllowances: [] };

/**
 * Run the gate over a fixture tree with an INJECTED file list, which doubles as the on-disk set — a
 * reference dangles exactly when it names something not in that list.
 */
function run(files, config = noAllowances) {
  const dir = makeTree(files);
  const log = recorder();
  try {
    return { code: checkLinks(dir, config, log, Object.keys(files)), out: log.text() };
  } finally {
    removeTree(dir);
  }
}

describe('check-links — regression: the archived-document defect this gate exists for', () => {
  it('catches a maintained doc pointing at a document that has been moved out of docs/', () => {
    const { code, out } = run({
      'README.md': 'Full measurement: `docs/2026-08-09-memory-policy-measurement.md`.\n',
      'docs/DECISIONS.md': 'See `docs/2026-08-09-memory-policy-measurement.md` for the corpus.\n',
    });

    assert.equal(code, 1, 'a reference to a path that is not in the repository must fail');
    assert.match(out, /README\.md:1\s+->\s+docs\/2026-08-09-memory-policy-measurement\.md/);
    assert.match(out, /docs\/DECISIONS\.md:1/);
    assert.match(out, /2 reference\(s\)/);
  });

  it('accepts the same reference once it is repointed at where the document actually lives', () => {
    // `local/**` is untracked by design, so it is skipped rather than resolved — the repoint is judged
    // right because the DANGLING path is gone, not because the new one was found on disk.
    const { code, out } = run({
      'README.md': 'Full measurement: `local/superpowers/records/2026-08-09-memory-policy-measurement.md`.\n',
    });

    assert.equal(code, 0);
    assert.match(out, /every in-repo reference resolves/);
  });
});

describe('check-links — what it does NOT flag', () => {
  it('a reference that resolves', () => {
    const { code } = run({
      'README.md': 'The engine lives in `src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs`.\n',
      'src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs': '// engine\n',
    });
    assert.equal(code, 0);
  });

  it('a LINE NUMBER that has rotted — existence only, never lines', () => {
    // Deliberate scope. A `file.cs:123` reference rots on the next edit for entirely legitimate reasons,
    // and pitfalls.md §DI/config already records line numbers rotting twice and being deleted in favour of
    // names. Gating them would fail this check on every refactor, for no defect.
    const { code } = run({
      'README.md': 'See `src/a.cs:99999`.\n',
      'src/a.cs': '// one line\n',
    });
    assert.equal(code, 0, 'a rotted line number is not this gate\'s business');
  });

  it('a line carrying `link-ok` — the annotation for prose that NAMES a path as data', () => {
    const { code } = run({
      'docs/FIXES.md': 'a fixture named `docs/灵台.md` <!-- link-ok: fixture name -->\n',
    });
    assert.equal(code, 0);
  });

  it('`link-ok` is NOT `drift-ok`: one token must not silence two unrelated gates', () => {
    const { code, out } = run({
      'docs/FIXES.md': 'a fixture named `docs/灵台.md` <!-- drift-ok: retired vocabulary reason -->\n',
    });
    assert.equal(code, 1, 'check-docs\' annotation must not exempt a line from THIS gate');
    assert.match(out, /docs\/灵台\.md/);
  });

  it('a HISTORICAL record, whose paths were right on its own day', () => {
    const { code } = run({ 'docs/task-archive.md': 'moved `docs/gone.md` here\n' });
    assert.equal(code, 0);
  });

  it("but a CHANGELOG's `## Unreleased` prefix IS checked — it has not shipped and can still be edited", () => {
    const { code, out } = run({
      'CHANGELOG.md': '## Unreleased\n\n- see `docs/gone.md`\n\n## 2.5.0 — 2026-08-08\n\n- see `docs/also-gone.md`\n',
    });
    assert.equal(code, 1);
    assert.match(out, /CHANGELOG\.md:3/, 'the unreleased reference is reported');
    assert.doesNotMatch(out, /also-gone/, 'the released section is history and is not');
  });

  it('a path outside the repository (a URL, a foreign tree)', () => {
    const { code } = run({
      'README.md': 'See https://example.invalid/docs/thing.md and vendor/docs/other.md\n',
    });
    assert.equal(code, 0, 'only this repository\'s own top-level directories are anchored on');
  });
});

describe('check-links — allowances cannot rot', () => {
  const allowed = {
    staleReferenceAllowances: [{ file: 'docs/plan.md', why: 'its layout is superseded; paths are as-written' }],
  };

  it('an allowance suppresses that document, and only that document', () => {
    const { code, out } = run({
      'docs/plan.md': 'built into `src/Lyntai.Generation/GenerationKinds.cs`\n',
      'docs/live.md': 'built into `src/Lyntai.Generation/GenerationKinds.cs`\n',
    }, allowed);

    assert.equal(code, 1);
    assert.match(out, /docs\/live\.md:1/);
    assert.doesNotMatch(out, /docs\/plan\.md:1/);
  });

  it('an allowance that matches NOTHING is itself a failure', () => {
    // The same rule retiredApiNames' escapes carry: once a document's last stale reference is repaired the
    // allowance is a hole nobody can see expiring, and the next genuine one in that file goes unreported.
    const { code, out } = run({ 'docs/plan.md': 'nothing stale here\n' }, allowed);

    assert.equal(code, 1);
    assert.match(out, /no longer match anything/);
    assert.match(out, /docs\/plan\.md — every reference in it now resolves/);
  });
});

describe('check-links — the file list', () => {
  it('reads tracked files NUL-separated, so a non-ASCII NAME is not C-quoted', () => {
    // Without `-z`, `docs/灵台.md` arrives as `"docs/\347\201\265\345\217\260.md"` — a name matching no file
    // on disk — so the gate would both fail to scan it AND report every reference TO it as dangling. Same
    // root cause as check-sensitive's and check-docs' own (TASKS.md Part 60).
    const dir = makeRepo({ 'docs/灵台.md': '# 灵台\n', 'README.md': 'see `docs/灵台.md`\n' });
    try {
      git(dir, ['add', '-A']);
      const files = trackedFiles(dir);

      assert.ok(files.includes('docs/灵台.md'), `expected the raw path; got ${JSON.stringify(files)}`);

      const log = recorder();
      assert.equal(checkLinks(dir, noAllowances, log), 0,
        `a reference to a tracked CJK-named file must resolve; got: ${log.text()}`);
    } finally {
      removeTree(dir);
    }
  });

  it('matches a path whose own name is non-ASCII', () => {
    PATH_PATTERN.lastIndex = 0;
    assert.deepEqual([...'see `docs/灵台.md` here'.matchAll(PATH_PATTERN)].map((m) => m[1]), ['docs/灵台.md']);
  });

  // REGEX ALTERNATION IS ORDERED, and `cs` sat before `csproj`, so every project path matched as far as
  // `.cs` and stopped — `src/Lyntai.Core/Lyntai.Core.csproj` was captured as `src/Lyntai.Core/Lyntai.Core.cs`,
  // a file that does not exist, and reported as dangling. A FALSE FAILURE rather than a permissive miss, so
  // it fails closed; but the message names a path nobody wrote, which is the worst kind of gate output.
  // Latent only because no maintained doc happens to name a `.csproj` today — `repo-mechanics.md` §Package
  // layout discusses them constantly and is one `src/` prefix away from tripping it. Found 2026-08-14 by
  // pointing the pattern at the code tiers it does not scan; every long extension is asserted so a future
  // addition cannot reintroduce the ordering bug for a different one.
  it('matches a LONG extension whole, never truncating it to a shorter alternative', () => {
    for (const path of [
      'src/Lyntai.Core/Lyntai.Core.csproj',      // `cs` must not win over `csproj`
      'src/Directory.Build.props',
      'devtools/config.yaml',                    // `yml` must not be tried as a prefix of `yaml`
      'docs/design.html',
      'tests/Lyntai.Tests/Api/Baselines/x.txt',
    ]) {
      PATH_PATTERN.lastIndex = 0;
      assert.deepEqual([...`see \`${path}\` here`.matchAll(PATH_PATTERN)].map((m) => m[1]), [path],
        `${path} was not captured whole`);
    }
  });
});

describe('check-links — a reference naming the WRONG record for a Part', () => {
  // The second way an inbound reference rots, and the one no path check can see: the path resolves, the
  // Part exists — in the OTHER file. Every archived task turns each surviving "TASKS.md Part N" into a
  // pointer to the wrong record, and `task-lifecycle.md`'s whole premise is that the two records answer
  // different questions. Measured 2026-08-14: five live references across CHANGELOG's Unreleased prefix
  // and docs/FIXES.md named TASKS.md for Parts that had been archived.
  const records = {
    'TASKS.md': '## Part 70 — still open\n',
    'docs/task-archive.md': '## Part 53 — done\n### Part 62 — done as a sub-entry\n',
  };

  it('catches a maintained doc claiming the BACKLOG holds a Part the ARCHIVE holds', () => {
    const { code, out } = run({ ...records, 'README.md': 'tracked in `TASKS.md` Part 53.\n' });

    assert.equal(code, 1, 'naming the wrong record must fail');
    assert.match(out, /README\.md:1/);
    assert.match(out, /Part 53/);
    assert.match(out, /archive/i);
  });

  it('catches the reverse — claiming the ARCHIVE holds a Part that is still OPEN', () => {
    const { code, out } = run({ ...records, 'README.md': 'see `docs/task-archive.md` Part 70.\n' });

    assert.equal(code, 1, 'the reverse direction must fail too');
    assert.match(out, /still OPEN/i);
  });

  it('resolves a Part declared with ### as well as ##, so a sub-entry is not reported missing', () => {
    // The archive files some closed work as `### Part N` under a parent Part. A gate matching only `##`
    // would call every reference to one of those a dangling Part — a false positive on correct prose.
    const { code } = run({ ...records, 'README.md': 'see `docs/task-archive.md` Part 62.\n' });
    assert.equal(code, 0, 'a `### Part N` sub-entry must count as present');
  });

  it('says nothing about a Part reference that names the right record', () => {
    const { code } = run({
      ...records,
      'README.md': 'open work is `TASKS.md` Part 70; history is `docs/task-archive.md` Part 53.\n',
    });
    assert.equal(code, 0);
  });

  it('catches a Part reference that WRAPS across two lines', () => {
    // Found 2026-08-15. The Part half matched one line at a time, while check-docs and check-counts both
    // build a soft-joined two-line window and check-docs' own comment generalises the lesson — "the unit
    // you match must be the unit the claim is written in… worth carrying to any future text gate". This
    // gate was written THREE DAYS after that comment and did not carry it.
    //
    // These documents wrap at ~110 columns and a Part reference spans a backtick, a filename and a bold
    // marker, so it is among the likeliest claims to straddle a break — which is exactly what had happened
    // to the design contract's own "TASKS.md\n**Part 40**", a Part that had long since been archived.
    const { code, out } = run({
      ...records,
      'README.md': 'that rule is the open call — `TASKS.md`\n**Part 53** — and must not be revisited.\n',
    });

    assert.equal(code, 1, 'a wrapped reference names the wrong record just as loudly as an unwrapped one');
    assert.match(out, /Part 53/);
  });

  it('counts a Part declared as a LIST ITEM, not only as a heading', () => {
    // The archive files some closed work as `- [x] **Part N — …**` rather than a heading. Reading headings
    // only made those Parts invisible to the record scan, so a correct reference to one would be reported
    // as "in NEITHER record" — a false positive that in practice CANCELLED the false negative above, since
    // the one live defect happened to be both wrapped and bullet-declared.
    const { code } = run({
      'TASKS.md': '## Part 70 — still open\n',
      'docs/task-archive.md': '- [x] **Part 41 — closed as a list item.**\n',
      'README.md': 'see `docs/task-archive.md` Part 41.\n',
    });

    assert.equal(code, 0, 'a bullet-declared Part must count as present');
  });

  it('still reports a Part that is declared NOWHERE, in either shape', () => {
    // The control for the two above: widening what counts as a declaration must not make the check vacuous.
    const { code, out } = run({ ...records, 'README.md': 'see `docs/task-archive.md` Part 99.\n' });

    assert.equal(code, 1);
    assert.match(out, /NEITHER/i);
  });

  it('leaves a bare "Part N" with no record named alone', () => {
    // Prose says "Part 53" constantly without claiming which file holds it. Only a reference that NAMES a
    // record makes a checkable claim; flagging the rest would be the crying-wolf failure check-docs' own
    // comments warn about.
    const { code } = run({ ...records, 'README.md': 'the corpus work of Part 53 established this.\n' });
    assert.equal(code, 0);
  });

  it('honours `link-ok`, the same annotation the path half uses', () => {
    const { code } = run({ ...records, 'README.md': 'was `TASKS.md` Part 53 <!-- link-ok: quoting the entry as written -->\n' });
    assert.equal(code, 0);
  });
});

describe('check-links — fail-closed on an empty scan', () => {
  it('an empty listing FAILS rather than printing a tick', () => {
    // check-api-vocabulary's rule, which this gate was missing until 2026-08-15. It shares check-docs' scope
    // predicates, so one broken predicate disarms BOTH gates at once — the divergence sharing them was meant
    // to prevent, arriving from the direction sharing cannot help with.
    const log = recorder();
    assert.equal(checkLinks('/nowhere', noAllowances, log, []), 1);
    assert.match(log.text(), /found no maintained documents/);
    assert.match(log.text(), /proves nothing/);
  });

  it('a caller-supplied list with no maintained doc in it still PASSES', () => {
    // The other direction: a commit touching only `src/` legitimately has nothing to check, so zero
    // survivors is only an indictment on the full-tree path.
    const { code, out } = run({ 'src/a.cs': '// nothing to see\n' });
    assert.equal(code, 0, out);
  });
});

describe('check-links — the CODE tiers (Part 72)', () => {
  it('a dead docs/ reference inside an XML doc comment is caught', () => {
    // The tier that SHIPS: `///` content is compiled into the .xml doc file consumers read, and the
    // compiler resolves `<see cref>` only — a path in prose is invisible to it.
    const { code, out } = run({
      'README.md': 'intro\n',
      'src/Lyntai.Core/Thing.cs': '/// <remarks>Measured in docs/2026-08-09-gone.md.</remarks>\npublic class Thing;\n',
    });
    assert.equal(code, 1, out);
    assert.match(out, /src\/Lyntai\.Core\/Thing\.cs:1/);
  });

  it('a dead docs/ reference in an ORDINARY `//` comment is caught too', () => {
    // Measured, and it reverses the scope entry's own hypothesis. Part 72 expected `//` comments to be
    // where false positives live ("a code comment is the one place a reference to something that no longer
    // exists is often CORRECT"). Replaying the pre-repair tree found the opposite: all three dead
    // references that a `///`-only rule would have missed were in `//` comments and all three were REAL,
    // while every false positive sat in a guard script naming a FIXTURE. So the line is drawn at the
    // TARGET (documents move) rather than at the comment style.
    const { code, out } = run({
      'README.md': 'intro\n',
      'tests/Lyntai.Tests/AThing.cs': 'class X {\n  // on the strength of docs/2026-08-09-gone.md\n}\n',
    });
    assert.equal(code, 1, out);
    assert.match(out, /AThing\.cs:2/);
  });

  it('a reference to a SOURCE path is NOT checked in code — only documents move', () => {
    // pitfalls.md records an existence check over prose returning ~45 hits and zero defects. That came
    // from checking every path: source files are renamed for legitimate reasons and a comment describing
    // the old shape is correct. Documents are the case this gate exists for, so only `docs/` is checked.
    const { code, out } = run({
      'README.md': 'intro\n',
      'src/Lyntai.Core/Thing.cs': '// see src/Lyntai.Core/Gone.cs for the old shape\npublic class Thing;\n',
    });
    assert.equal(code, 0, out);
  });

  it('a live docs/ reference in code passes', () => {
    const { code, out } = run({
      'README.md': 'intro\n',
      'docs/memory.md': '# memory\n',
      'src/Lyntai.Core/Thing.cs': '/// See docs/memory.md.\npublic class Thing;\n',
    });
    assert.equal(code, 0, out);
  });

  it('a NON-comment line in code is not scanned — a string literal is data, not a reference', () => {
    const { code, out } = run({
      'README.md': 'intro\n',
      'src/Lyntai.Core/Thing.cs': 'var p = "docs/2026-08-09-gone.md";\n',
    });
    assert.equal(code, 0, out);
  });

  it('the guard-script fixture tree is skipped — its paths are synthetic by design', () => {
    const { code, out } = run({
      'README.md': 'intro\n',
      'devtools/scripts/__tests__/x.test.mjs': "// makeTree({'docs/2026-08-09-gone.md': ''})\n",
    });
    assert.equal(code, 0, out);
  });

  it('`link-ok` works in code exactly as it does in prose', () => {
    const { code, out } = run({
      'README.md': 'intro\n',
      'devtools/scripts/g.mjs': '// the fixture is docs/灵台.md — never a real file link-ok\n',
    });
    assert.equal(code, 0, out);
  });
});

describe('check-links — the code tier is actually covered on the real tree', () => {
  it('scans a substantial number of code files, and says so on a PASSING run', async () => {
    // This stands in for the fail-closed guard the code half deliberately does not have (see the note in
    // check-links.mjs). The filter has intentional exclusions, so "zero survivors" cannot be distinguished
    // from "nothing to scan" inside the gate — but it can be pinned from outside, against the real tree.
    // The REAL config, not `noAllowances`: the tree has one legitimate allowance (a superseded plan that
    // names its original file layout on purpose), and running without it reports 29 hits that the shipped
    // gate does not.
    const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
    const config = (await import('../../project.config.mjs')).default;
    const log = recorder();
    checkLinks(repo, config, log);

    // The COUNT, not the exit code. Asserting exit 0 would make this test fail whenever the tree happens to
    // hold a dangling reference — which is `verify`'s job to report, from check-links itself, with the
    // offending path named. Measured 2026-08-15: archiving three Parts left one stale reference in
    // `docs/memory.md`, and the over-assertion turned that into a red GUARD-SCRIPT suite, pointing the
    // reader at the harness instead of at the document. This test's claim is only that the tier is scanned.
    const scanned = Number((log.text().match(/\+ (\d+) code file\(s\)/) ?? [])[1]);
    assert.ok(scanned > 100, `expected the code tier to be covered; the run reported ${scanned}`);
  });
});
