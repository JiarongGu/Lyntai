// check-counts — FAIL when a COUNT written in prose disagrees with the tree it counts.
//
// WHY THIS IS A GATE. `TASKS.md` Part 73 measured six corrections to a counted claim inside sixty commits,
// all the same shape: a number written by hand that nothing computes. Two more went stale during the
// 2026-08-15 session that built this, both in `CLAUDE.md`'s own baseline line, and both caught by a person
// who happened to be looking. That is eight incidents and zero automated catches.
//
// `check-docs` structurally CANNOT see this. Its registry holds vocabulary a decision RETIRED, and a count
// going stale retires nothing — the sentence stays grammatical, plausible, and wrong. It is the same
// relationship `check-links` has to `check-docs`: one asks whether a document still SAYS what was settled,
// this asks whether what it COUNTS is still true.
//
// THE HONEST LIMIT, stated here rather than discovered: this only ever covers counts somebody REGISTERED.
// It is a gate against recurrence in the places that have drifted, not a proof that every number in the
// documentation is right.
//
// WHY THE REGISTRY IS CODE AND NOT `project.config.mjs`. Every other registry there (`retiredTerms`,
// `retiredApiNames`, `staleReferenceAllowances`) is pure data. An entry here is a regex plus a FUNCTION over
// the tree, so it lives beside the gate that runs it and keeps the config a data file.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { IN_SCOPE, IS_SCANNED, SUPERSEDED_BANNER, liveLineCount } from './check-docs.mjs';
import { packableProjects } from './check-packages.mjs';

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

/**
 * Gates in `verify`, read from the `steps` array that `verify`'s own summary line is derived from.
 *
 * Matches an OUTER entry — `['name', [` — rather than any quoted word in the array. The first version used
 * `\['[a-z-]+'` and was wrong TWICE in cancelling directions: the character class excludes digits so it
 * never matched `e2e`, and it DID match the inner argument array in `['check-sensitive', ['--tree']]`. Both
 * errors together produced exactly the right total, so the gate agreed with the documentation for the wrong
 * reason. Caught by this counter's own test, which compares the parsed NAMES and not just the count — the
 * literal illustration of Part 73's "a counter that is subtly wrong is worse than none".
 */
export function countVerifyGates(repo) {
  const dev = fs.readFileSync(path.join(repo, 'devtools', 'dev.mjs'), 'utf8');
  const m = dev.match(/const steps = \[([\s\S]*?)\];/);
  return m ? [...m[1].matchAll(/\['([a-z0-9-]+)',\s*\[/g)].length : -1;
}

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

/**
 * Guard-script tests.
 *
 * Counted STATICALLY from the declarations, which is exact here and was verified to be: on 2026-08-15 the
 * static count matched `node --test`'s reported total on all sixteen files individually AND in aggregate.
 * That equality is a property of how these files are written (no test is generated in a loop), so the test
 * pinning this counter compares it against a real run rather than against a hard-coded number.
 */
export function countGuardTests(repo) {
  const dir = path.join(repo, 'devtools', 'scripts', '__tests__');
  if (!fs.existsSync(dir)) return -1;
  return fs.readdirSync(dir)
    .filter((f) => f.endsWith('.test.mjs'))
    .reduce((n, f) => n + (fs.readFileSync(path.join(dir, f), 'utf8').match(/^\s*(?:it|test)\(/gm) ?? []).length, 0);
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
 * Memory POLICY domains — a `Lyntai.Memory.*` sub-namespace whose job is to hold one `IMemory<X>Policy`
 * seam plus its implementations (design §5.7 / D47).
 *
 * Derived from the seams, not from a directory listing: `.Engines` is a sub-namespace and is NOT a domain
 * (it holds the engines), so counting folders would be wrong by one in the direction that looks right.
 * The test asserts the NAMES, not just the total — the lesson from the `verify`-gate counter above.
 */
export function countMemoryDomains(repo) {
  const dir = path.join(repo, 'src', 'Lyntai.Core', 'Memory');
  if (!fs.existsSync(dir)) return -1;
  const domains = new Set();
  const walk = (d) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) { walk(p); continue; }
      if (!e.name.endsWith('.cs')) continue;
      const text = fs.readFileSync(p, 'utf8');
      const ns = text.match(/^namespace (Lyntai\.Memory\.[A-Za-z]+)/m);
      // The seam is what makes a sub-namespace a DOMAIN — `IMemory<X>Policy` declared in it.
      if (ns && /^\s*public interface IMemory\w+Policy\b/m.test(text)) domains.add(ns[1]);
    }
  };
  walk(dir);
  return domains.size;
}

/**
 * Files in a `.claude/` tier that are THIS repository's own — the ones a `daoris` sync does not own and
 * can therefore delete without anything failing (`repo-mechanics.md` opens with that exposure).
 *
 * Canonical artifacts carry `<!-- daoris: … — canonical`; local ones carry `<!-- local: never synced`. The
 * marker must be matched as a DECLARATION at line start, not by searching for the word: a local file's own
 * marker reads "not a daoris artifact", so a naive `includes('daoris')` classifies every local file as
 * canonical and returns ZERO. Measured 2026-08-15 — the first probe did exactly that and nearly produced a
 * "fix" to a claim that was already correct.
 *
 * This is Part 73's own worked example of a subtly-wrong counter: the directories hold 10 skills and 9
 * knowledge documents, while the LOCAL counts are 5 and 6.
 */
function countLocal(repo, tier, file = null) {
  const dir = path.join(repo, '.claude', tier);
  if (!fs.existsSync(dir)) return -1;
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  let n = 0;
  for (const e of entries) {
    const target = file ? path.join(dir, e.name, file) : path.join(dir, e.name);
    if (file ? !e.isDirectory() : !e.name.endsWith('.md')) continue;
    if (!fs.existsSync(target)) continue;
    if (!/^<!-- daoris:/m.test(fs.readFileSync(target, 'utf8'))) n++;
  }
  return n;
}

export const countLocalSkills = (repo) => countLocal(repo, 'skills', 'SKILL.md');

/** Local knowledge documents. `RULES_INDEX`/`TEMPLATE` live in the rules tier, so they are not counted. */
export const countLocalKnowledge = (repo) => countLocal(repo, 'knowledge');

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
    pattern: /`?verify`?\s+(?:runs|has)\s+([\w]+)\s+(?:checks|gates)/gi,
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
    what: 'guard-script tests',
    pattern: /guard-script tests\s+([\d]+)\s*\/\s*\d+/gi,
    count: countGuardTests,
    why: 'CLAUDE.md instructs the reader to COMPARE against this baseline — a stale one teaches them to stop comparing',
  },
  {
    what: 'local skills',
    pattern: /(?:the\s+)?([\w]+)\s+local skills|—\s*([\w]+)\s+LOCAL\b/g,
    count: countLocalSkills,
    why: 'a sync can delete a local skill and nothing fails — the count is the only thing that would notice',
  },
  {
    what: 'local knowledge documents',
    pattern: /(?:the\s+)?([\w]+)\s+local knowledge documents/gi,
    count: countLocalKnowledge,
    why: 'same exposure as the skills tier: unowned by the sync, so a loss is silent',
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
];

export const trackedFiles = (repo) =>
  // -z for the same reason check-docs uses it: a C-quoted non-ASCII path fails to read and is silently
  // skipped, which is a document that is never scanned and never reported as unscanned.
  execFileSync('git', ['ls-files', '-z'], { cwd: repo, encoding: 'utf8' }).split('\0').filter(Boolean);

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

  const source = files ?? trackedFiles(repo);
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
    let text;
    try { text = fs.readFileSync(path.join(repo, file), 'utf8'); } catch { continue; }
    if (SUPERSEDED_BANNER.test(text)) continue;

    const all = text.split(/\r?\n/);
    const lines = all.slice(0, liveLineCount(file, all));
    // Same two-line window as check-docs, and for the same measured reason: these documents wrap at ~110
    // columns, so a claim can straddle a break and a line-only matcher would never see it.
    const windows = lines.map((l, i) => (i + 1 < lines.length ? `${l} ${lines[i + 1]}` : l));

    for (const claim of claims) {
      if (truths.get(claim) < 0) continue;   // broken counter: reported once, not per occurrence
      lines.forEach((line, i) => {
        claim.pattern.lastIndex = 0;
        const subject = claim.pattern.test(line) ? line : windows[i];
        claim.pattern.lastIndex = 0;

        // An ESCAPED occurrence still counts as a MATCH, so `count-ok` excuses the claim without making the
        // registry entry look dead. Conflating the two meant the only annotated occurrence of a claim
        // tripped the dead-entry rule instead of passing — a gate failing on correctly-annotated prose.
        const escaped = line.includes(ESCAPE)
          || (subject === windows[i] && (lines[i + 1] ?? '').includes(ESCAPE));
        for (const m of subject.matchAll(claim.pattern)) {
          // A match lying WHOLLY in the window's second half belongs to line i+1, which reports it on its
          // own pass. Without this the same claim is reported at two line numbers — once from the window
          // that straddles it and once from the line that contains it — and the second number is the
          // useful one. The window's job is only to catch a claim broken ACROSS the wrap.
          if (subject === windows[i] && m.index >= line.length + 1) continue;
          // The FIRST non-empty group, not `m[1]`: a claim written two ways in two documents is one entry
          // with an alternation, and only one branch's group is populated per match.
          const said = parseCount(m.slice(1).find((g) => g != null));
          if (said === null) continue;        // "many packages" — a word, not a claim
          seen.set(claim, seen.get(claim) + 1);
          if (!escaped && said !== truths.get(claim))
            hits.push({ file, line: i + 1, claim, said, actual: truths.get(claim), text: subject.trim() });
        }
      });
    }
  }

  // A registered claim that matches NOTHING is dead weight that cannot expire — the same rule
  // `staleReferenceAllowances` and `retiredApiNames` carry, for the same reason: an entry nobody can see
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
