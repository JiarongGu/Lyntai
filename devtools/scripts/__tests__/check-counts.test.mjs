// check-counts — the counted-claim gate. See devtools/scripts/check-counts.mjs.
//
// docs/task-archive.md Part 73's caveat is what this file exists for, quoted because it IS the requirement: "a counter
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
  COUNTED_CLAIMS, ROOT_MEMORY_POLICY_EXEMPTIONS, checkCounts, countBareCancellationCatches, countDecisions, countGoldenShapes, countLanguageArms, countMemoryDomains, countMigrations, countOptionGuards, countPackages, countVerifyGates, unexemptedRootMemoryPolicies,
  parseCount,
} from '../check-counts.mjs';
import { VERIFY_STEPS } from '../../commands.mjs';
import { makeRepo, makeTree, recorder, removeTree } from './_fixtures.mjs';

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

  it('reads EMPTY as zero, because that is how English spells zero for a set', () => {
    // The startable-items claim has now hit a boundary its pattern could not express TWICE — plural-only
    // broke at ONE (2026-09-12), and the noun broke at ZERO (2026-09-16), where the banner naturally reads
    // "the startable set is EMPTY". A registry that cannot say a legitimate value fails on the day that
    // value arrives, and the tempting fix is prose bent to satisfy a regex.
    assert.equal(parseCount('EMPTY'), 0);
    assert.equal(parseCount('empty'), 0);
    assert.equal(parseCount('zero'), 0, 'the digit-word spelling must keep working too');
  });

  it('returns null for a word that is not a number, so prose is not treated as a claim', () => {
    // `empty` is a count word; these are not, and the pattern's optional noun means a sentence continuing
    // "…is going to move" captures one of them. It must be skipped rather than read as a claim of zero.
    for (const w of ['many', 'several', 'the', 'some', 'going', '', null])
      assert.equal(parseCount(w), null);
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

  it('verify gates is the roster verify runs — imported, never parsed out of the dispatcher', () => {
    assert.equal(countVerifyGates(), VERIFY_STEPS.length);
    assert.ok(VERIFY_STEPS.some(([s]) => s === 'e2e'), 'sanity: the roster holds real step names');
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

  it('language arms matches the enum the sweep iterates', () => {
    const n = countLanguageArms(repo);
    const text = fs.readFileSync(
      path.join(repo, 'tests', 'Lyntai.Tests', 'Memory', 'Corpus', 'CorpusLexicon.cs'), 'utf8');
    // Cross-checked against an INDEPENDENT read of the enum — one member per line, rather than the counter's
    // comma split — so adding an arm moves both together and never a literal here.
    const body = text.slice(text.indexOf('enum CorpusLanguage'));
    const members = body.slice(body.indexOf('{'), body.indexOf('}')).match(/^\s*[A-Z]\w*\s*,?\s*$/gm) ?? [];
    assert.ok(members.length > 1, 'sanity: the independent read found the enum members');
    assert.equal(n, members.length);
  });

  it('golden shapes counts hash literals in Goldens(), not data rows', () => {
    // Cross-checked against the actual TheoryData rows rather than trusted as a literal: a row is a tuple
    // whose shape varies (a named `with { ... }` versus a positional `new CorpusShape(...)`), so the hash
    // literal is the one part every row carries in the same form.
    const text = fs.readFileSync(
      path.join(repo, 'tests', 'Lyntai.Tests', 'Memory', 'Corpus', 'MemoryCorpusGoldenTests.cs'), 'utf8');
    const rows = (text.match(/^\s*\{\s*"[\w-]+",/gm) ?? []).length;
    const n = countGoldenShapes(repo);
    assert.ok(rows > 0, 'sanity: the row read found the goldens');
    assert.equal(n, rows, 'the hash-literal count must match the actual row count');
  });

  it('memory domains counts SEAMS, not sub-directories', () => {
    // `.Engines` is a sub-namespace and is NOT a domain — it holds the engines, not a policy seam. Counting
    // folders would give eight, which looks plausible and is wrong. So the count is asserted alongside the
    // structural rule that produces it, the same way the verify-gate counter is pinned by its NAMES.
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
    // The independent folder read agrees with the counter — never a literal a new domain would break.
    assert.equal(countMemoryDomains(repo), seamOwners.size + unexemptedRootMemoryPolicies(repo).length);
  });

  it('a ROOT-level policy seam is exempted by name or it raises the count', () => {
    // The blind spot this closed: the written rule derives a domain from the seam's NAME, the counter
    // derived it from a seam in a SUB-namespace, and they agreed on the tree by accident.
    // IMemoryRemovalPolicy is an IMemory*Policy with a shipped implementation living at the root.
    const memory = path.join(repo, 'src', 'Lyntai.Core', 'Memory');
    const rootSeams = fs.readdirSync(memory, { withFileTypes: true })
      .filter((e) => e.isFile() && e.name.endsWith('.cs'))
      .map((e) => fs.readFileSync(path.join(memory, e.name), 'utf8'))
      .filter((t) => /^namespace Lyntai\.Memory;/m.test(t))
      .flatMap((t) => [...t.matchAll(/^\s*public interface (IMemory\w+Policy)\b/gm)].map((m) => m[1]));

    assert.ok(rootSeams.includes('IMemoryRemovalPolicy'),
      'the exemption below is about a seam that must actually be at the root; if it moved, delete it');
    assert.deepEqual(unexemptedRootMemoryPolicies(repo), [],
      'a root-level IMemory*Policy with no recorded reason is a domain nobody filed — add it to a '
      + 'sub-namespace, or record why it is not a graph-memory domain in ROOT_MEMORY_POLICY_EXEMPTIONS');

    // …and the exemption list may not rot: every entry names a seam that is still there.
    for (const name of Object.keys(ROOT_MEMORY_POLICY_EXEMPTIONS))
      assert.ok(rootSeams.includes(name), `${name} is exempted but no longer declared at the memory root`);
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

  it('the bare-cancellation counter excludes the FILTERED sites, which is the whole distinction', () => {
    // Discrimination is proved on a FIXTURE, not on the real tree: every site there is guarded today, so a
    // real-tree assertion of "finds some" would have to be deleted the moment the gate's own subject was
    // fixed — which is exactly what happened to the first version of this test.
    // A git fixture: the counters read `repoFiles`, the one file list every gate scans.
    const dir = makeRepo({
      'src/Lyntai.Core/Memory/Bare.cs': 'try { } catch (OperationCanceledException) { throw; }\n',
      'src/Lyntai.Core/Memory/Nested/AlsoBare.cs':
        'catch (OperationCanceledException) { throw; }\ncatch (OperationCanceledException ex) { }\n',
      'src/Lyntai.Core/Memory/Guarded.cs':
        'catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }\n',
      'src/Lyntai.Core/Inference/Elsewhere.cs': 'catch (OperationCanceledException) { throw; }\n',
    });
    try {
      // three bare under Memory (recursively), the guarded one rejected, and Inference out of scope entirely
      assert.equal(countBareCancellationCatches(dir), 3);
    } finally { removeTree(dir); }

    // On the REAL tree the counter only has to agree with an independent walk — which holds at zero, and
    // is the check that keeps the registry entry honest as sites are added or answered.
    const tracked = execFileSync('git', ['ls-files', '-z', 'src/Lyntai.Core/Memory'], { cwd: repo, encoding: 'utf8' })
      .split('\0').filter((f) => f.endsWith('.cs'));
    const lines = tracked.flatMap((f) => fs.readFileSync(path.join(repo, f), 'utf8').split(/\r?\n/))
      .filter((l) => /catch\s*\(\s*OperationCanceledException/.test(l));
    const guarded = lines.filter((l) => /when\s*\(\s*ct\.IsCancellationRequested\s*\)/.test(l));
    assert.ok(lines.length > 0, 'sanity: the real tree must still have catches to classify');
    assert.equal(countBareCancellationCatches(repo), lines.length - guarded.length);

    const { pattern } = COUNTED_CLAIMS.find((c) => c.what === 'bare cancellation catches in Lyntai.Core/Memory');
    const matches = (s) => [...s.matchAll(new RegExp(pattern.source, pattern.flags))].map((m) => m[1]);
    assert.deepEqual(matches('holds 21 such sites, **20** guarded and **0** bare'), ['0']);
    // The total is deliberately NOT a claim: it moves when any catch is added anywhere.
    assert.deepEqual(matches('`Lyntai.Core/Memory` holds 21 of these catches'), []);
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

  it('sees the `verify` gate count in the phrasing that has NO verb (regression)', () => {
    // Measured 2026-09-10: `check-backlog` was added to `verify`, CLAUDE.md's "`verify` runs N checks"
    // sentence was updated and this gate agreed with it — while the Dev loop's *the "am I done?" gate,
    // eighteen checks* sat wrong in the same file, invisible because it names no verb.
    const claim = COUNTED_CLAIMS.find((c) => c.what === '`verify` gates');
    const line = 'the "am I done?" gate, eighteen checks stopping at the first failure:';

    claim.pattern.lastIndex = 0;
    const [match] = [...line.matchAll(claim.pattern)];
    claim.pattern.lastIndex = 0;

    assert.ok(match, 'the verbless phrasing must be matched');
    assert.equal(parseCount(match.slice(1).find((g) => g != null)), 18);
  });

  it('`count-ok` excuses a sentence quoting a historical count', () => {
    const { code, out } = run(
      { 'docs/a.md': 'Back at v0.30 it shipped seven widgets; it does not now. count-ok\n' }, fixedClaim(12));
    assert.equal(code, 0, out);
  });

  it('a frozen SEED line of the design record is never compared — only its dated amendments are live', () => {
    // check-docs' `liveLineMask` is the one answer to "which lines are live"; this gate used to scan the
    // whole design record and would have pushed an author to edit a v0.1 seed.
    const { code, out } = run({
      'docs/2026-07-17-lyntai-design.md': '# design\n\nThe v0.1 seed ships seven widgets; frozen.\n'
        + '*(2026-09-19: it ships twelve widgets; today.)*\n',
    }, fixedClaim(12));
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
