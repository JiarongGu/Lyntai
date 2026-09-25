// check-counts — FAIL when a COUNT written in prose disagrees with the tree it counts.
//
// A number written by hand that nothing computes goes stale silently, and no other gate can see it: a stale
// count retires no vocabulary, so the sentence stays grammatical and wrong. What it cost and its limit (it
// covers only counts somebody REGISTERED): `docs/GATES.md` §check-counts.
//
// The registry is `COUNTED_CLAIMS` below rather than in `project.config.mjs`, because an entry is a regex
// plus a FUNCTION over the tree. Escape: `count-ok`, for a sentence quoting a historical count.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { VERIFY_STEPS } from '../commands.mjs';
import { IN_SCOPE, IS_SCANNED, SUPERSEDED_BANNER, liveLinesOnly } from './check-docs.mjs';
import { packableProjects } from './check-packages.mjs';
import { readRepoText, repoFiles, twoLineWindows, windowHits } from './_repo-files.mjs';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

/**
 * Number words this repository actually writes, so `twelve packages` is checked like `12 packages`.
 *
 * Load-bearing: most counted claims here are SPELLED, and a digits-only matcher would have caught none of
 * the eight measured incidents. Case is folded by the caller.
 */
export const NUMBER_WORDS = {
  zero: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10,
  eleven: 11, twelve: 12, thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16, seventeen: 17,
  eighteen: 18, nineteen: 19, twenty: 20, thirty: 30, forty: 40, fifty: 50,
  // `empty` is how English spells zero for a SET, and a registry that cannot express a legitimate value
  // fails on the day that value arrives. Added 2026-09-16 when the startable count reached 0 and the
  // banner naturally read "the startable set is EMPTY" — the third time this claim hit a boundary its
  // pattern could not say (plural-only broke at ONE, on 2026-09-12). The tempting fix each time is
  // ungrammatical prose written to satisfy a regex, which is the gate training the document.
  empty: 0,
};

/** A captured token (`12`, `twelve`, `TWELVE`) as an integer, or `null` when it is not a number at all. */
export function parseCount(token) {
  if (token == null) return null;
  const t = String(token).trim().replace(/[*`_]/g, '').toLowerCase();
  if (/^\d+$/.test(t)) return Number(t);
  return Object.prototype.hasOwnProperty.call(NUMBER_WORDS, t) ? NUMBER_WORDS[t] : null;
}

// ---- the counters ------------------------------------------------------------------------------------
// Each is a function over the repo root. Part 73's caveat is the design constraint: "a counter that is
// subtly wrong is worse than none — it fails a clean tree and the fix is to edit the counter, which trains
// exactly the 'ignore this gate' reflex". So each one is pinned by a test against the tree as it stands,
// and each avoids the naive glob that would be wrong (noted per counter below).

/** Packable library projects. Reuses check-packages' own reader rather than re-deriving the rule. */
export const countPackages = (repo) => packableProjects(repo).length;

/** Gates in `verify` — the roster `verify` runs, imported rather than parsed out of the dispatcher. */
export const countVerifyGates = () => VERIFY_STEPS.length;

/**
 * FluentMigrator migrations.
 *
 * Matches the `M<12 digits>_` filename convention rather than `M*.cs`: Part 73 records a first probe that
 * globbed `Migrations/M*.cs` and got 12, because `MigrationRunnerService.cs` matches it and is not a
 * migration. The directory holds 13 files for 11 migrations, so the naive count is wrong by two.
 */
export function countMigrations(repo) {
  const dir = path.join(repo, 'src', 'Lyntai.Storage.Sqlite', 'Migrations');
  if (!fs.existsSync(dir)) return -1;
  return fs.readdirSync(dir).filter((f) => /^M\d{12}_.+\.cs$/.test(f)).length;
}

/** The memory subsystem's source root, which four counters below walk. */
const MEMORY = 'src/Lyntai.Core/Memory';

/** The `.cs` files under a directory, from the ONE file list every gate scans (`repoFiles`). */
const csUnder = (repo, dir) => repoFiles(repo, [dir]).filter((f) => f.endsWith('.cs'));
const read = (repo, f) => readRepoText(repo, f) ?? '';

/**
 * Call sites of the one memory option-domain guard (`MemoryOption.Require`, D78).
 *
 * Registered because this claim went stale IN THE COMMIT THAT MADE IT: D78 shipped saying "all 23 … across
 * four files" when the tree held 31 across five, and the same wrong pair was copied into `MemoryOption`'s own
 * remarks, two test comments and the CHANGELOG entry a consumer reads. Five places, one number, nothing
 * looking at it — the exact shape this gate exists for.
 *
 * Counts the CALL SITES rather than the guarded properties: one `Require` per property is the invariant D78
 * establishes, so if the two ever diverge the call-site count is the one that describes the code.
 */
export function countOptionGuards(repo) {
  return csUnder(repo, MEMORY).reduce((n, f) => n + (read(repo, f).match(/MemoryOption\.Require\b/g) ?? []).length, 0);
}

/**
 * BARE `catch (OperationCanceledException)` sites in `Lyntai.Core/Memory` — those WITHOUT the
 * `when (ct.IsCancellationRequested)` filter that tells a caller's cancel from the seam's own timeout.
 *
 * Registered 2026-09-09, the day its predecessor was found wrong in THREE maintained places at once
 * (`TASKS.md`, `pitfalls.md` and `docs/FIXES.md` all said "20 other sites"), which is the same shape as the
 * option-guard claim above. The count was wrong for the most ordinary reason: it was derived by subtracting
 * one fixed site from a grep total, and one of the remainder already carried the filter.
 *
 * Counts BARE rather than guarded because that is the number work MOVES: each site answered turns one bare
 * into one guarded, so a stale figure here is a backlog item that has silently already been done.
 */
export function countBareCancellationCatches(repo) {
  if (!fs.existsSync(path.join(repo, MEMORY))) return -1;
  let n = 0;
  for (const f of csUnder(repo, MEMORY)) {
    for (const line of read(repo, f).split(/\r?\n/))
      // The filter is what makes a site answered; anything else catching an OCE is still bare.
      if (/catch\s*\(\s*OperationCanceledException/.test(line)
          && !/when\s*\(\s*ct\.IsCancellationRequested\s*\)/.test(line)) n++;
  }
  return n;
}

/**
 * e2e suites — the `pN.mjs` files the runner discovers.
 *
 * Registered 2026-08-23 with the two below, after a session in which EIGHT maintained claims went stale and
 * the gates caught three. Every one they caught was registered here; every one they missed was not — which
 * is this gate's documented limit ("it only covers counts somebody REGISTERED") behaving exactly as written.
 * The response to that limit is to register more, and the cheapest wins are the claims sitting in the SAME
 * SENTENCE as one that already drifted: `CLAUDE.md`'s test line carries five numbers and only one of them
 * was gated.
 */
export function countE2eSuites(repo) {
  const dir = path.join(repo, 'devtools', 'scripts', 'e2e');
  if (!fs.existsSync(dir)) return -1;
  return fs.readdirSync(dir).filter((f) => /^p\d+\.mjs$/.test(f)).length;
}

/**
 * Arms of the corpus language axis — the members of `CorpusLanguage`.
 *
 * This is the claim that drifted most recently (the roster said four after a fifth was added), and it is
 * why the counter reads the ENUM rather than the sweep's prose: `Enum.GetValues<CorpusLanguage>()` is what
 * the sweep actually runs, so the enum is the authority.
 */
export function countLanguageArms(repo) {
  const file = path.join(repo, 'tests', 'Lyntai.Tests', 'Memory', 'Corpus', 'CorpusLexicon.cs');
  if (!fs.existsSync(file)) return -1;
  const text = fs.readFileSync(file, 'utf8');
  const at = text.indexOf('enum CorpusLanguage');
  if (at < 0) return -1;
  const body = text.slice(at);
  const inner = body.slice(body.indexOf('{') + 1, body.indexOf('}'));
  return inner.replace(/\/\/\/.*/g, '').split(',')
    .map((s) => s.trim())
    .filter((s) => /^[A-Za-z]\w*$/.test(s))
    .length;
}

/**
 * How far the decision log goes — the `n` a `D1–Dn` range claim publishes.
 *
 * Reads the HEADINGS, never the index table at the top of the file. That table is regenerated by
 * `decisions-index`, which is deliberately outside `verify`, so a counter reading it would gate one
 * hand-maintained number against another and agree with a stale table.
 *
 * Returns the MAXIMUM rather than the tally, because a range claim is a statement about the maximum. The
 * two are interchangeable only while the log is contiguous (`docs/DECISIONS.md`'s own "Contiguous
 * `D1..Dn`, no stubs"), so this counter's test asserts that contiguity as its own fact — a gap fails there
 * rather than quietly turning `D1–Dn` into a claim about a range with a hole in it.
 */
export function countDecisions(repo) {
  const file = path.join(repo, 'docs', 'DECISIONS.md');
  if (!fs.existsSync(file)) return -1;
  const ns = [...fs.readFileSync(file, 'utf8').matchAll(/^## D(\d+)\b/gm)].map((m) => Number(m[1]));
  return ns.length ? Math.max(...ns) : -1;
}

/**
 * Root-level `IMemory<X>Policy` seams that are deliberately NOT graph-memory domains, each with the reason.
 *
 * An entry that stops matching FAILS (`check-counts.test.mjs`), the same rule every other allowance in this
 * repository carries: an exemption nobody can see expire is how a blind spot becomes permanent.
 */
export const ROOT_MEMORY_POLICY_EXEMPTIONS = {
  IMemoryRemovalPolicy:
    'a BLEND concern — consulted by CompositeMemoryEngine to decide which MEMBERS a forget or prune visits '
    + '(D72) — rather than a stage of the graph engine\'s decay pipeline, which is what the seven domains '
    + 'describe. Its namespace is public and frozen (D70), so it cannot move even if that changed.',
};

/** Root-level `IMemory<X>Policy` seams with no recorded exemption — each one a domain nobody filed. */
export function unexemptedRootMemoryPolicies(repo) {
  const found = [];
  for (const f of csUnder(repo, MEMORY).filter((p) => !p.slice(MEMORY.length + 1).includes('/'))) {
    const text = read(repo, f);
    if (!/^namespace Lyntai\.Memory;/m.test(text)) continue;
    for (const m of text.matchAll(/^\s*public interface (IMemory\w+Policy)\b/gm))
      if (!Object.hasOwn(ROOT_MEMORY_POLICY_EXEMPTIONS, m[1])) found.push(m[1]);
  }
  return found;
}

/**
 * Memory POLICY domains — a `Lyntai.Memory.*` sub-namespace whose job is to hold one `IMemory<X>Policy`
 * seam plus its implementations (design §5.7 / D47).
 *
 * Derived from the seams, not from a directory listing: `.Engines` is a sub-namespace and is NOT a domain
 * (it holds the engines), so counting folders would be wrong by one in the direction that looks right.
 * The test asserts the NAMES, not just the total — the lesson from the `verify`-gate counter above.
 *
 * **It also counts an UNEXEMPTED root-level seam, which is the blind spot it used to have.** The written
 * rule derives a domain from the seam's NAME; this function derived it from a seam in a SUB-namespace, so a
 * policy declared at the root (`namespace Lyntai.Memory;`) matched neither the regex nor anyone's attention
 * — and this gate's own reason for existing is that "two public seams sat outside the documented domain
 * list on the eve of the 3.0 freeze". Counting one makes the number disagree with every document that says
 * seven, which is the loud failure; the fix is then to file it as a domain or record why it is not.
 */
export function countMemoryDomains(repo) {
  if (!fs.existsSync(path.join(repo, MEMORY))) return -1;
  const domains = new Set();
  for (const f of csUnder(repo, MEMORY)) {
    const text = read(repo, f);
    const ns = text.match(/^namespace (Lyntai\.Memory\.[A-Za-z]+)/m);
    // The seam is what makes a sub-namespace a DOMAIN — `IMemory<X>Policy` declared in it.
    if (ns && /^\s*public interface IMemory\w+Policy\b/m.test(text)) domains.add(ns[1]);
  }
  return domains.size + unexemptedRootMemoryPolicies(repo).length;
}

/**
 * Golden shapes registered in `MemoryCorpusGoldenTests.Goldens()`.
 *
 * Registered 2026-08-27, after a sixth shape (the routine class's own golden) landed and "five goldens" went
 * stale in every document quoting it. Counts the HASH LITERALS rather than the data rows themselves:
 * `Goldens()` is a `TheoryData` collection initializer, so a row is a tuple whose shape can vary (a named
 * `CorpusShape.Default with { ... }` versus a positional `new CorpusShape(...)`), but every row carries
 * exactly one 64-character hex SHA-256 — the one part of the shape that cannot be mistaken for something
 * else in the same file.
 *
 * It gates the TOTAL, which is the only quantity it can compute — and the prose was rewritten to state a
 * total rather than lean on it for a SUBSET claim. "Byte-identical when unset, proved by N goldens" is
 * provable only by the goldens captured before the language axis, so binding it to the total would have
 * forced the number further wrong on the next golden added for any unrelated axis. TWO maintained documents
 * now carry the total (CLAUDE.md and the design contract); `MemoryCorpus`'s own copy of the sentence states
 * the proof and names no number, which is what keeps this gate's `.md`-only scope honest there.
 */
export function countGoldenShapes(repo) {
  const file = path.join(repo, 'tests', 'Lyntai.Tests', 'Memory', 'Corpus', 'MemoryCorpusGoldenTests.cs');
  if (!fs.existsSync(file)) return -1;
  const text = fs.readFileSync(file, 'utf8');
  const at = text.indexOf('Goldens()');
  if (at < 0) return -1;
  const end = text.indexOf('};', at);
  if (end < 0) return -1;
  return (text.slice(at, end).match(/"[0-9a-f]{64}"/g) ?? []).length;
}

/**
 * The registry. One entry per counted claim: a pattern whose first capture group is the number, the
 * function that computes the truth, and why the claim is worth gating.
 *
 * Patterns are deliberately NARROW. A loose one matches prose that is not the claim — `packages` alone also
 * appears in "15 third-party packages in the bundle closure" — and a gate that fires on the wrong sentence
 * gets switched off. Each is required to match at least once (see the dead-entry check), so a pattern that
 * is too narrow to find its own claim fails rather than passing silently.
 */
export const COUNTED_CLAIMS = [
  {
    what: 'packable packages',
    // The trailing `;` is what separates the CLAIM from every other sentence containing the word. Measured
    // on the first run: a bare `(\w+) packages` also matched "The five packages left OUT" of the bundle
    // (a different quantity), "15 third-party packages in the bundle closure", and a design-record line
    // reading "11 packages **as of v0.30**" that self-corrects in its own next clause. None is this claim.
    pattern: /\b([\w]+)\s+packages;/gi,
    count: countPackages,
    why: 'the package set is the consuming surface; `check-packages` gates membership but nothing gated the NUMBER',
  },
  {
    what: '`verify` gates',
    // The second alternative was added 2026-09-10 after this claim went stale in the one phrasing the
    // first cannot see. `CLAUDE.md` states the number TWICE — "`verify` runs nineteen checks" in the
    // packaging paragraph, and *the "am I done?" gate, eighteen checks stopping at the first failure* in
    // the Dev loop — and only the first has a verb. So `check-backlog` was added to `verify`, one sentence
    // was updated, the gate agreed with it, and the other sat wrong beside an arrow list that had also
    // lost the new gate. The lesson is the one `check-links`' own registry already records: a rule must
    // match the CLAIM in every order it is writable, not the one sentence someone had in mind.
    pattern: /`?verify`?\s+(?:runs|has)\s+([\w]+)\s+(?:checks|gates)|gate,\s+([\w]+)\s+checks/gi,
    count: countVerifyGates,
    why: 'CLAUDE.md tells a reader what `verify` runs; a stale number there misdescribes the one command they run most',
  },
  {
    what: 'migrations',
    pattern: /applies\s+\*{0,2}([\w]+)\*{0,2}\s+migrations/gi,
    count: countMigrations,
    why: 'a fresh database applies exactly this many; the naive glob over the directory is wrong by two',
  },
  {
    what: 'memory option-domain guard sites',
    // Anchored on "at all N sites across" — the LIVE count. Deliberately not on the "replaced 31 across
    // five" clause beside it: that one is history and is true forever, and a counter pointed at a historical
    // tally fails the moment a new option is guarded, which trains the reflex this gate exists to prevent.
    pattern: /at all\s+\*{0,2}(\d+)\*{0,2}\s+sites across/gi,
    count: countOptionGuards,
    why: 'D78 shipped with this number wrong in five places at once; nothing but a person was looking at it',
  },
  {
    what: 'bare cancellation catches in Lyntai.Core/Memory',
    // Anchored on "guarded and N bare", the one form all three documents were normalised to when this was
    // registered. Deliberately NOT on the bare "21 sites" beside it: the total moves whenever any catch is
    // added or removed, including ones nobody is claiming anything about, and a counter that fails for a
    // reason the sentence is not making a claim about is the kind people learn to escape rather than fix.
    pattern: /\bguarded\s+and\s+\*{0,2}(\d+)\*{0,2}\s+bare/gi,
    count: countBareCancellationCatches,
    why: 'its predecessor shipped wrong in THREE maintained documents at once, derived by subtraction from a grep',
  },
  {
    what: 'memory policy domains',
    // Registered 2026-08-15, the day the claim was found stale in BOTH the design contract ("the five
    // domains so far") and CLAUDE.md's namespace map, while the tree held seven. The two that were missing
    // are singular and default to NONE, so nothing constructs them and no test names them — a domain that
    // is invisible to every other signal is exactly what a counted-claim gate is for.
    // Two shapes because the claim is written two ways: the design contract says "…are the seven domains
    // so far", CLAUDE.md's namespace map says "DOMAINS are SEVEN:".
    pattern: /([\w]+)\s+domains\s+so\s+far|DOMAINS are ([A-Z]+):/g,
    count: countMemoryDomains,
    why: 'two public seams sat outside the documented domain list on the eve of the 3.0 freeze',
  },
  {
    what: 'corpus language arms',
    // Bold + an em-dash introducing the roster is the claim's actual shape, and narrowing to it was
    // necessary: a bare `(\w+) arms` matched a pitfalls entry quoting a past mistake ("calling five corpus
    // arms 'the two arms'") and a CHANGELOG sentence about a measurement collapsing "the four arms to two".
    // Both are prose ABOUT arm counts rather than a statement OF one.
    pattern: /\*\*([\w]+)\s+arms\s+—/gi,
    count: countLanguageArms,
    why: 'the roster said four after a fifth arm was added; the enum is what the sweep actually runs',
  },
  {
    what: 'e2e suites',
    // Anchored on the `N/N` shape the prose uses (`e2e 3/3`) so it cannot match prose ABOUT e2e.
    pattern: /\be2e\s+(\d+)\s*\/\s*\d+/gi,
    count: countE2eSuites,
    why: 'it sits in the same sentence as the guard-test count that went stale, and nothing gated it',
  },
  {
    what: 'the decision log range',
    // Anchored on `D1` AND the range dash, never on a bare `D(\d+)`: these documents name individual
    // decisions constantly ("D30", "**D39–D41**", "D42–D44"), and only a range STARTING at D1 claims how
    // far the log goes. The dash class covers en-dash, em-dash and hyphen, written as escapes so the
    // pattern does not depend on how this file's bytes survive an editor.
    //
    // Registered 2026-08-16, the day `CLAUDE.md` was found still publishing `D1–D75` against a log that had
    // reached D76 — in the one file every session reads before it reads anything else, which is the same
    // argument this gate's own header makes for existing at all.
    pattern: /\bD1\s*[–—-]\s*D(\d+)/g,
    count: countDecisions,
    why: 'CLAUDE.md routes a reader to the decision log by RANGE, so a short range reads as "nothing landed after this"',
  },
  {
    what: 'golden corpus shapes',
    // Anchored on "pins N golden shapes" — the TOTAL, which is what the counter computes. Deliberately not
    // the older "proved by N goldens": that sentence is a claim about the pre-language-axis SUBSET, so
    // gating it against the total made the two disagree by construction. A bare `(\w+) goldens` would also
    // match prose using the word descriptively, which names no count at all.
    pattern: /pins \*{0,2}([\w]+)\*{0,2} golden shapes/gi,
    count: countGoldenShapes,
    why: 'a sixth golden shape landed 2026-08-27 and "five goldens" stayed stale in every document quoting the total',
  },
];

/**
 * `count-ok` is the escape, deliberately NOT `drift-ok`.
 *
 * One token silencing two unrelated gates is a hole nobody can see opening — the reasoning `check-links`
 * already carries for keeping `link-ok` separate. A sentence quoting a historical count ("the list said
 * seven and had eleven") is legitimate and takes this marker.
 */
const ESCAPE = 'count-ok';

export function checkCounts(repo, claims = COUNTED_CLAIMS, log = console.log, files = null) {
  if (claims.length === 0) {
    log('check-counts: no counted claims registered — nothing to check.');
    return 0;
  }

  const source = files ?? repoFiles(repo);
  const docs = source.filter((f) => f.endsWith('.md')).filter(IN_SCOPE).filter(IS_SCANNED);

  // Fail-closed, the rule every scanner here carries: a gate that scanned nothing must never print a tick.
  if (source.length === 0 || (files === null && docs.length === 0)) {
    log('check-counts: ✗ found no maintained documents to scan');
    log('  Nothing was scanned, so this gate proves nothing — check IN_SCOPE and the repo root.');
    return 1;
  }

  // Compute each truth ONCE. A counter returning -1 means it could not find what it counts, which is a
  // broken counter rather than a stale document — reported separately so the two are never confused.
  const truths = new Map();
  const broken = [];
  for (const claim of claims) {
    let n;
    try { n = claim.count(repo); } catch (e) { n = -1; }
    if (!Number.isInteger(n) || n < 0) broken.push(claim);
    truths.set(claim, n);
  }

  const hits = [];
  const seen = new Map(claims.map((c) => [c, 0]));

  for (const file of docs) {
    const text = readRepoText(repo, file);
    if (text === null || SUPERSEDED_BANNER.test(text)) continue;

    // Historical lines BLANKED (`liveLinesOnly`), so a frozen seed is never asked to agree with today's tree.
    const lines = liveLinesOnly(file, text.split(/\r?\n/));
    const windows = twoLineWindows(lines);

    for (const claim of claims) {
      if (truths.get(claim) < 0) continue;   // broken counter: reported once, not per occurrence
      for (const h of windowHits(lines, claim.pattern, { escape: ESCAPE, windows })) {
        // The FIRST non-empty group, not `m[1]`: a claim written two ways in two documents is one entry
        // with an alternation, and only one branch's group is populated per match.
        const said = parseCount(h.match.slice(1).find((g) => g != null));
        if (said === null) continue;        // "many packages" — a word, not a claim
        // An ESCAPED occurrence still counts as a match, so `count-ok` never makes an entry look dead.
        seen.set(claim, seen.get(claim) + 1);
        if (!h.escaped && said !== truths.get(claim)) {
          const text = (h.straddles ? windows[h.at] : lines[h.at]).trim();
          hits.push({ file, line: h.at + 1, claim, said, actual: truths.get(claim), text });
        }
      }
    }
  }

  // A registered claim that matches NOTHING is dead weight that cannot expire — the same rule
  // `retiredApiNames` and every allowance here carry, for the same reason: an entry nobody can see
  // rotting is one that silently stops protecting anything.
  const dead = claims.filter((c) => truths.get(c) >= 0 && seen.get(c) === 0);

  if (broken.length === 0 && hits.length === 0 && dead.length === 0) {
    const total = [...seen.values()].reduce((a, b) => a + b, 0);
    log(`check-counts: ${total} counted claim(s) across ${docs.length} doc(s) agree with the tree ✓`);
    return 0;
  }

  if (broken.length > 0) {
    log(`check-counts: ✗ ${broken.length} counter(s) could not compute anything — the GATE is broken, not the docs\n`);
    for (const c of broken) log(`  ${c.what} — its counter returned no usable number; check the path it reads`);
    log('');
  }

  if (hits.length > 0) {
    log(`check-counts: ✗ ${hits.length} counted claim(s) disagree with the tree\n`);
    for (const h of hits) {
      const excerpt = h.text.length > 96 ? h.text.slice(0, 93) + '...' : h.text;
      log(`  ${h.file}:${h.line}  says ${h.said}, tree has ${h.actual}  (${h.claim.what})`);
      log(`      ${excerpt}`);
      log(`      why gated: ${h.claim.why}`);
    }
    log('');
    log(`  Fix the NUMBER, not the counter — unless the counter is what is wrong, in which case fix it and`);
    log(`  update its test. A sentence quoting a historical count deliberately takes \`${ESCAPE}\`.`);
  }

  if (dead.length > 0) {
    log(`check-counts: ✗ ${dead.length} registered claim(s) match nothing — an entry that cannot expire\n`);
    for (const c of dead) log(`  ${c.what} — its pattern found no occurrence; the prose moved, or the pattern is too narrow`);
  }

  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = checkCounts(repo);
}
