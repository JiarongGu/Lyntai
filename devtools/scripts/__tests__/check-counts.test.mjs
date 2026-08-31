// check-counts — the counted-claim gate. See devtools/scripts/check-counts.mjs.
//
// TASKS.md Part 73's caveat is what this file exists for, quoted because it IS the requirement: "a counter
// that is subtly wrong is worse than none — it fails a clean tree and the fix is to edit the counter, which
// trains exactly the 'ignore this gate' reflex". So every counter is pinned against the REAL tree, not a
// fixture, and where a runtime truth exists the counter is compared against that rather than a literal.
//
// A guard whose failure mode is a false PASS cannot be validated by running it, so the negative cases below
// matter as much as the positive ones.
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import {
  COUNTED_CLAIMS, checkCounts, countDecisions, countGoldenShapes, countGuardTests, countLanguageArms, countMemoryDomains, countMigrations, countOptionGuards, countPackages, countVerifyGates,
  parseCount,
} from '../check-counts.mjs';
import { makeTree, recorder, removeTree } from './_fixtures.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** Run the gate over a fixture tree with an INJECTED file list and a chosen claim set. */
function run(files, claims) {
  const dir = makeTree(files);
  const log = recorder();
  try {
    return { code: checkCounts(dir, claims, log, Object.keys(files)), out: log.text() };
  } finally {
    removeTree(dir);
  }
}

/** A claim whose truth is fixed, so a fact can be about the MATCHING rather than about a counter. */
const fixedClaim = (n) => [{
  what: 'widgets',
  pattern: /\b([\w]+)\s+widgets;/gi,
  count: () => n,
  why: 'a test claim',
}];

/**
 * A claim shaped like a REAL registry entry that anchors on single-space adjacency (`countGoldenShapes`'s
 * own pattern does this — `pins N golden shapes`, one literal space either side). `fixedClaim` above uses
 * `\s+`, which tolerates the extra spaces an indented continuation introduces and so cannot demonstrate the
 * defect; this one cannot cross them, which is the point.
 */
const provedByClaim = (n) => [{
  what: 'goldens proved',
  pattern: /proved by ([\w]+) goldens/gi,
  count: () => n,
  why: 'a test claim requiring single-space adjacency, the shape a real registry pattern uses',
}];

describe('check-counts — parsing a written number', () => {
  it('reads digits and spelled words alike', () => {
    // Load-bearing: most counted claims in this repository are SPELLED, so a digits-only matcher would have
    // caught none of the eight measured incidents.
    assert.equal(parseCount('12'), 12);
    assert.equal(parseCount('twelve'), 12);
    assert.equal(parseCount('TWELVE'), 12);
    assert.equal(parseCount('**thirteen**'), 13);
  });

  it('returns null for a word that is not a number, so prose is not treated as a claim', () => {
    for (const w of ['many', 'several', 'the', 'some', '', null]) assert.equal(parseCount(w), null);
  });
});

describe('check-counts — the counters, pinned against the real tree', () => {
  it('packages matches check-packages own reader', () => {
    const n = countPackages(repo);
    assert.ok(n > 0, 'must find packable projects');
    // Not a literal: the authority is the same function `check-packages` gates membership with, so this
    // stays true when a package is added and fails only if the two readers disagree.
    const out = spawnSync('node', [path.join(repo, 'devtools', 'dev.mjs'), 'check-packages'], { encoding: 'utf8' });
    const reported = Number(((out.stdout + out.stderr).match(/check-packages: (\d+) packable projects/) ?? [])[1]);
    assert.equal(n, reported, 'the counter and the gate must agree about the package count');
  });

  it('verify gates matches what verify actually runs', () => {
    const n = countVerifyGates(repo);
    assert.ok(n > 0, 'must parse the steps array');
    // verify's summary line is itself DERIVED from the steps array, so this compares the counter against
    // the same source of truth the summary uses — a parse that silently broke would show up as a mismatch.
    const dev = fs.readFileSync(path.join(repo, 'devtools', 'dev.mjs'), 'utf8');
    const names = [...(dev.match(/const steps = \[([\s\S]*?)\];/)[1]).matchAll(/\['([a-z0-9-]+)',\s*\[/g)]
      .map((m) => m[1]);
    assert.equal(n, names.length);
    // The NAMES, not just the total — the first counter got the right total from two cancelling errors
    // (it could not match `e2e`, and it counted the inner `['--tree']` argument array as a step).
    assert.ok(names.includes('e2e'), 'a step name containing a digit must be parsed');
    assert.ok(!names.includes('--tree'), 'an inner argument array must not be counted as a step');
    assert.ok(names.includes('test') && names.includes('build'), 'sanity: the parse found real step names');
  });

  it('migrations counts migrations, NOT every file in the directory', () => {
    // The exact trap Part 73 records: a first probe globbed `Migrations/M*.cs` and got 12, because
    // `MigrationRunnerService.cs` matches that glob and is not a migration.
    const dir = path.join(repo, 'src', 'Lyntai.Storage.Sqlite', 'Migrations');
    const all = fs.readdirSync(dir);
    const n = countMigrations(repo);
    assert.ok(n > 0);
    assert.ok(n < all.length, `the directory holds ${all.length} files and must not all be counted as migrations`);
    for (const f of all.filter((f) => /^M\d{12}_/.test(f))) assert.match(f, /\.cs$/);
  });

  it('the guard-test counting RULE agrees with what `node --test` reports', () => {
    // Deliberately NOT `dev.mjs test-devtools`: that runs every guard test, including this file, which
    // spawns it again — unbounded recursion, and slow enough to hide it. So the equality is proved on a
    // FIXTURE exercising the shapes these files actually use (top-level `test`, `it` nested in `describe`,
    // and a loop INSIDE one test, which must count as one), which is what the claim really rests on.
    // Laid out at the path the counter reads — it takes a REPO ROOT, not a directory of tests.
    const at = 'devtools/scripts/__tests__';
    const dir = makeTree({
      [`${at}/a.test.mjs`]: "import { test, describe, it } from 'node:test';\n"
        + "test('one', () => {});\n"
        + "describe('group', () => {\n  it('two', () => {});\n  it('three', () => {});\n});\n"
        + "test('four — a loop inside ONE test is still one', () => { for (const x of [1, 2, 3]) String(x); });\n",
      [`${at}/b.test.mjs`]: "import { test } from 'node:test';\ntest('five', () => {});\n",
    });
    try {
      // A GLOB, not the bare directory: on Node 24 `node --test <dir>` loads the directory as a module and
      // dies with "Cannot find module". Already recorded in `repo-mechanics.md` §Dev loop, and walked into
      // again while writing this — which is what the note is there to shorten.
      //
      // FORWARD slashes, even on Windows: `path.join` yields backslashes, which Node's glob does not treat
      // as separators, so the pattern matches nothing and the run reports no tests at all. Silent — it looks
      // exactly like a fixture that legitimately contains none.
      const glob = `${dir.replace(/\\/g, '/')}/${at}/*.test.mjs`;
      // NODE_TEST_CONTEXT must be cleared. A `node --test` spawned from INSIDE one inherits it, detects a
      // test context and switches from the spec reporter to TAP — so `ℹ pass 5` arrives as `# pass 5`, the
      // match yields NaN, and the assertion fails for a reason that has nothing to do with counting.
      const env = { ...process.env };
      delete env.NODE_TEST_CONTEXT;
      const out = spawnSync('node', ['--test', glob], { encoding: 'utf8', env });
      const reported = Number(((out.stdout + out.stderr).match(/[ℹ#] pass (\d+)/) ?? [])[1]);
      assert.equal(reported, 5, 'sanity: the fixture must actually run five tests');
      assert.equal(countGuardTests(dir), reported, 'the counting rule must equal the runtime total');
    } finally { removeTree(dir); }
  });

  it('and it finds a plausible number on the real tree', () => {
    const n = countGuardTests(repo);
    assert.ok(n > 200, `expected the guard suite to be substantial, got ${n}`);
  });

  it('language arms matches the enum the sweep iterates', () => {
    const n = countLanguageArms(repo);
    const text = fs.readFileSync(
      path.join(repo, 'tests', 'Lyntai.Tests', 'Memory', 'Corpus', 'CorpusLexicon.cs'), 'utf8');
    // Cross-checked against the member NAMES rather than a literal, so adding an arm moves both together.
    for (const name of ['English', 'Chinese', 'Japanese', 'Korean', 'ChineseMixed'])
      assert.ok(text.includes(name), `${name} must be an arm`);
    assert.equal(n, 5, 'five arms as of 2026-08-15 — update with the enum, and the roster prose with it');
  });

  it('golden shapes counts hash literals in Goldens(), not data rows', () => {
    // Cross-checked against the actual TheoryData rows rather than trusted as a literal: a row is a tuple
    // whose shape varies (a named `with { ... }` versus a positional `new CorpusShape(...)`), so the hash
    // literal is the one part every row carries in the same form.
    const text = fs.readFileSync(
      path.join(repo, 'tests', 'Lyntai.Tests', 'Memory', 'Corpus', 'MemoryCorpusGoldenTests.cs'), 'utf8');
    const rows = (text.match(/^\s*\{\s*"[\w-]+",/gm) ?? []).length;
    const n = countGoldenShapes(repo);
    assert.equal(n, rows, 'the hash-literal count must match the actual row count');
    assert.equal(n, 8, 'eight shapes as of 2026-08-30 (five pre-dating the language axis, one for the routine class, one for its STANDING answer arm, one for its SETTLE gap) — update with Goldens() and the "pins N golden shapes" prose with it');
  });

  it('memory domains counts SEAMS, not sub-directories', () => {
    // `.Engines` is a sub-namespace and is NOT a domain — it holds the engines, not a policy seam. Counting
    // folders would give eight, which looks plausible and is wrong. So the count is asserted alongside the
    // structural rule that produces it, the same way the verify-gate counter is pinned by its NAMES.
    assert.equal(countMemoryDomains(repo), 7);

    const memory = path.join(repo, 'src', 'Lyntai.Core', 'Memory');
    const seamOwners = new Set();
    const dirs = fs.readdirSync(memory, { withFileTypes: true }).filter((e) => e.isDirectory());
    for (const d of dirs) {
      for (const f of fs.readdirSync(path.join(memory, d.name)).filter((f) => f.endsWith('.cs'))) {
        if (/^\s*public interface IMemory\w+Policy\b/m.test(fs.readFileSync(path.join(memory, d.name, f), 'utf8')))
          seamOwners.add(d.name);
      }
    }
    assert.ok(seamOwners.has('Annotation') && seamOwners.has('Verification'),
      'the two model-in-the-loop domains must be counted — they defaulted to none and were missed for that reason');
    assert.ok(!seamOwners.has('Engines'), 'Engines holds engines, not a policy seam, and is not a domain');
  });

  it('the option-guard counter counts CALL SITES, and matches the files D78 names', () => {
    const n = countOptionGuards(repo);
    assert.ok(n > 0, 'must find MemoryOption.Require call sites');

    // Pinned against an independent walk of the tree rather than against a literal, for the reason the
    // verify-gate counter earned: a counter and a hard-coded number agree until the counter is wrong, and
    // then they agree anyway. This one re-derives from `git ls-files` instead of a manual directory walk.
    const tracked = execFileSync('git', ['ls-files', '-z', 'src/Lyntai.Core/Memory'], { cwd: repo, encoding: 'utf8' })
      .split('\0').filter((f) => f.endsWith('.cs'));
    const independent = tracked.reduce(
      (t, f) => t + (fs.readFileSync(path.join(repo, f), 'utf8').match(/MemoryOption\.Require\b/g) ?? []).length, 0);
    assert.equal(n, independent, 'the counter must agree with an independent walk of the same tree');

    // The claim is per-FILE as well as per-site, and D78's prose names eight files. A counter that only
    // checked the total would pass while the "across eight files" half rotted — which is exactly how the
    // original pair (23 / four files) went wrong: BOTH numbers were stale, and only one was ever quoted twice.
    const files = tracked.filter((f) => /MemoryOption\.Require\b/.test(fs.readFileSync(path.join(repo, f), 'utf8')));
    assert.equal(files.length, 8, `D78 says eight files; the tree has ${files.length}`);

    // The pattern must read the LIVE count and ignore the historical one sitting in the same paragraph —
    // the distinction that keeps this gate from failing every time a new option is guarded.
    const { pattern } = COUNTED_CLAIMS.find((c) => c.what === 'memory option-domain guard sites');
    const matches = (s) => [...s.matchAll(new RegExp(pattern.source, pattern.flags))].map((m) => m[1]);
    assert.deepEqual(matches('the sole guard at all 32 sites across six files'), ['32']);
    assert.deepEqual(matches('It replaced 31 hand-rolled copies across five when it landed'), []);
  });

  it('the decision log range is the log MAXIMUM, and the log has no gaps', () => {
    const n = countDecisions(repo);
    assert.ok(n > 0, 'must find decision headings');

    const ns = [...fs.readFileSync(path.join(repo, 'docs', 'DECISIONS.md'), 'utf8')
      .matchAll(/^## D(\d+)\b/gm)].map((m) => Number(m[1])).sort((a, b) => a - b);
    // The SET, not the total — the lesson from the verify-gate counter one level up. A tally and a maximum
    // agree on a contiguous log and on nothing else, so asserting contiguity is what makes `D1–Dn` a true
    // description of the log rather than a number that happens to match.
    assert.deepEqual(ns, Array.from({ length: n }, (_, i) => i + 1),
      'DECISIONS.md must stay contiguous D1..Dn — see its own header');

    // The pattern must reject a bare decision reference, or the gate would fire on every sentence naming
    // one and get switched off. `D39–D41` is a real range in CLAUDE.md and is NOT a claim about the log.
    const { pattern } = COUNTED_CLAIMS.find((c) => c.what === 'the decision log range');
    const matches = (s) => [...s.matchAll(new RegExp(pattern.source, pattern.flags))].map((m) => m[1]);
    assert.deepEqual(matches('see D30, and **D39–D41**, and D42–D44'), []);
    assert.deepEqual(matches('docs/DECISIONS.md (D1–D76 — the memory subsystem'), ['76']);
    assert.deepEqual(matches('the log runs D1-D76 today'), ['76']);
  });

  it('every registered claim has a counter that computes SOMETHING on this tree', () => {
    // A counter returning -1 means it could not find what it reads — a broken gate reporting on the docs.
    for (const claim of COUNTED_CLAIMS)
      assert.ok(claim.count(repo) >= 0, `${claim.what}: counter found nothing`);
  });
});

describe('check-counts — matching', () => {
  it('a claim that disagrees with the tree FAILS, naming both numbers', () => {
    const { code, out } = run({ 'docs/a.md': 'The library ships seven widgets; that is the set.\n' }, fixedClaim(12));
    assert.equal(code, 1);
    assert.match(out, /says 7, tree has 12/);
  });

  it('a claim that agrees PASSES', () => {
    const { code, out } = run({ 'docs/a.md': 'The library ships twelve widgets; that is the set.\n' }, fixedClaim(12));
    assert.equal(code, 0, out);
  });

  it('a claim broken across a line WRAP is still checked', () => {
    // These documents wrap at ~110 columns, so a claim can straddle a break — the same blind spot that hid
    // every check-docs rule from any wrapped claim until 2026-08-11.
    const { code, out } = run({ 'docs/a.md': 'The library ships seven\nwidgets; that is the set.\n' }, fixedClaim(12));
    assert.equal(code, 1, out);
  });

  it('a wrapped claim is reported ONCE, at the line that holds it', () => {
    // Without the window/line dedupe the same claim is reported at two line numbers, and the second is the
    // useful one. Measured on this gate's first run against the real tree.
    const { out } = run({ 'docs/a.md': 'intro line with no claim at all\nThe set is seven widgets; done.\n' }, fixedClaim(12));
    assert.equal((out.match(/says 7, tree has 12/g) ?? []).length, 1);
    assert.match(out, /docs\/a\.md:2/);
  });

  it('a STALE claim wrapped onto an INDENTED continuation is still SEEN (regression)', () => {
    // The demonstrated failure: the join kept the continuation's own leading indentation, so
    // "…proved by" + " " + "      seven goldens" carried extra spaces a single-space pattern cannot
    // cross, and the stale claim was invisible — the gate printed a clean run over it. One healthy
    // document keeps the registry entry alive (`seen > 0`) so the dead-entry rule does not mask this.
    const { code, out } = run({
      'docs/healthy.md': 'Byte-identical when unset, proved by six goldens captured before the axis existed.\n',
      'docs/stale.md': 'Elsewhere the same claim says byte-identical when unset, proved by\n'
        + '      seven goldens — an indented continuation nobody re-checked.\n',
    }, provedByClaim(6));
    assert.equal(code, 1, out);
    assert.match(out, /docs\/stale\.md/);
    assert.match(out, /says 7, tree has 6/);
  });

  it('`count-ok` excuses a sentence quoting a historical count', () => {
    const { code, out } = run(
      { 'docs/a.md': 'Back at v0.30 it shipped seven widgets; it does not now. count-ok\n' }, fixedClaim(12));
    assert.equal(code, 0, out);
  });

  it('a non-numeric word is not treated as a claim', () => {
    const { code, out } = run({ 'docs/a.md': 'The library ships many widgets; more each year.\n' }, fixedClaim(12));
    // Not a claim at all, so the entry matched nothing and the DEAD-ENTRY rule fires instead — which is the
    // honest outcome: silence here would mean an unmatched registry entry passing unnoticed.
    assert.equal(code, 1);
    assert.match(out, /match nothing/);
  });
});

describe('check-counts — the registry cannot rot', () => {
  it('a registered claim matching NOTHING fails', () => {
    // Same rule `staleReferenceAllowances` and `retiredApiNames` carry: an entry nobody can see expiring is
    // one that silently stops protecting anything. Here it also catches a pattern narrowed until it no
    // longer finds its own claim — the exact risk narrowing created on this gate's first run.
    const { code, out } = run({ 'docs/a.md': 'Nothing here resembles the claim.\n' }, fixedClaim(12));
    assert.equal(code, 1);
    assert.match(out, /match nothing/);
  });

  it('a BROKEN counter is reported as a broken gate, not as stale prose', () => {
    // The distinction matters: "fix the number" is wrong advice when the counter is what failed.
    const claims = [{ what: 'widgets', pattern: /\b([\w]+)\s+widgets;/gi, count: () => -1, why: 'x' }];
    const { code, out } = run({ 'docs/a.md': 'It ships twelve widgets; yes.\n' }, claims);
    assert.equal(code, 1);
    assert.match(out, /the GATE is broken, not the docs/);
  });

  it('a counter that THROWS is treated the same way rather than crashing the run', () => {
    const claims = [{ what: 'widgets', pattern: /\b([\w]+)\s+widgets;/gi, count: () => { throw new Error('boom'); }, why: 'x' }];
    const { code, out } = run({ 'docs/a.md': 'It ships twelve widgets; yes.\n' }, claims);
    assert.equal(code, 1);
    assert.match(out, /the GATE is broken/);
  });
});

describe('check-counts — fail-closed on an empty scan', () => {
  it('an empty listing FAILS rather than printing a tick', () => {
    const log = recorder();
    assert.equal(checkCounts('/nowhere', fixedClaim(1), log, []), 1);
    assert.match(log.text(), /found no maintained documents/);
    assert.match(log.text(), /proves nothing/);
  });

  it('an empty REGISTRY says so rather than reporting a clean tree', () => {
    const log = recorder();
    assert.equal(checkCounts(repo, [], log, ['README.md']), 0);
    assert.match(log.text(), /no counted claims registered/);
  });
});
