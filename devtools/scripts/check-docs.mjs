// check-docs — fail when a doc uses vocabulary a decision retired.
//
// The gap this closes: the code is gated from every side (check-warnings, the API-surface baselines, the
// storage contracts) and the prose is gated from none. A spec paragraph that quietly stops being true
// survives every check, and the next session reads it and implements the wrong thing.
//
// The registry is `retiredTerms` in devtools/project.config.mjs — a term, what to say instead, and why.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repo = join(dirname(here), '..', '..');

/**
 * Files whose whole job is to be a record of their own day, so retired vocabulary is CORRECT in them.
 *
 * Specs and plans used to need entries here. They moved to the gitignored `local/superpowers/` (see
 * `docs/superpowers/INDEX.md`), so they are untracked and never reach this scan at all — what remains is
 * the maintained set, every document of which has to keep being true. The rule that replaced the gate for
 * a design record is the stronger one: a conclusion that must outlive its version belongs in a maintained
 * document, which this scan does cover.
 *
 * Exported (with `SUPERSEDED_BANNER`, `IN_SCOPE` and `LIVE_PREFIX`) because `check-samples.mjs` asks the
 * SAME question — "is this maintained state, or a record of its own day?" — and must not answer it
 * differently. Two copies drift the moment a document is archived, and the drift is silent, in the
 * permissive direction, on whichever copy was forgotten.
 */
export const HISTORICAL = [
  /^CHANGELOG\.md$/,
  /^docs\/task-archive\.md$/,
];

/**
 * A file that is historical BELOW a boundary and MAINTAINED above it — scanned down to that line and no
 * further.
 *
 * `CHANGELOG.md` is the case, added 2026-08-11 (TASKS.md Part 53). The exemption above rests on records
 * being "accurate BY using the vocabulary of their day", which is true of a RELEASED section and false of
 * `## Unreleased`: that section describes behaviour that has not shipped, is still being edited, and can
 * still change under the words describing it. Measured 2026-08-09 — the `ReciprocalRankFusionPolicy` entry
 * kept asserting the pre-fix tie behaviour AND its retired justification after the code changed, while a
 * paragraph five lines below was corrected in the same pass. The gate reported 39 docs clean with it
 * present, and a human reviewer found it: exactly the failure D42 created the gate to prevent.
 *
 * The boundary is the first RELEASED heading, so everything above it — the preamble, the format note, and
 * the whole Unreleased section — is live and everything from `## 2.5.0 — …` down is the record it was.
 */
export const LIVE_PREFIX = [
  { file: /^CHANGELOG\.md$/, until: /^## \d+\.\d+\.\d+/ },
];

/** A file is READ when it is not wholly historical; a partly-historical one is read for its live prefix. */
export const IS_SCANNED = (path) =>
  !HISTORICAL.some((re) => re.test(path)) || LIVE_PREFIX.some((r) => r.file.test(path));

/**
 * How many leading lines of `file` are maintained state: `Infinity` for an ordinary document, and for a
 * partly-historical one the count before its boundary heading (all of it, when the boundary is not there
 * yet — a CHANGELOG with nothing released is entirely live).
 */
export function liveLineCount(file, lines) {
  const rule = LIVE_PREFIX.find((r) => r.file.test(file));
  if (!rule) return Infinity;
  const at = lines.findIndex((l) => rule.until.test(l));
  return at < 0 ? lines.length : at;
}

/**
 * A file that DECLARES itself superseded in its opening banner is a record too — same reasoning.
 *
 * Tightened 2026-08-11 from a bare `\bSUPERSEDED\b` anywhere in the first 21 lines. That form exempted the
 * WHOLE FILE from every rule on the strength of the word appearing in ordinary prose, and it failed in the
 * permissive direction: a maintained document whose intro happens to say "section X below is superseded"
 * silently stopped being checked, with no output saying so. `2026-08-09-memory-policy-measurement.md` came
 * within eight lines of exactly that. The word must now open an emphasized banner (`**SUPERSEDED …**` or
 * `**Status: SUPERSEDED …**`, optionally block-quoted) — a DECLARATION, not a mention.
 *
 * Zero tracked documents matched the old form when this was tightened, so nothing lost its exemption.
 */
export const SUPERSEDED_BANNER = /^(?:.*\n){0,20}?[^\S\n]*>?[^\S\n]*\*\*[^*\n]{0,40}?\bSUPERSEDED\b/;

/**
 * Only prose is checked; see the note on `retiredTerms` for why `src/` is deliberately excluded.
 *
 * The two repo-root files were added 2026-08-11, after a whole-branch review found `CLAUDE.md` describing a
 * subsystem the branch had reshaped underneath it — untouched, unflagged, and never scanned. They are the
 * highest-leverage omission this gate could have: `CLAUDE.md` is AUTO-LOADED into every session, so a stale
 * claim there is read by the next session before it reads anything else, and `TASKS.md` is the open backlog
 * a session picks work from. Both are maintained state by the same definition as `docs/` — the historical
 * twins (`CHANGELOG.md`, `docs/task-archive.md`) stay excluded below.
 */
/**
 * The CODE tiers this gate scans, added 2026-08-17 — comment lines only.
 *
 * `src/` was excluded for the gate's whole life, and the registry's own header stated the cost precisely:
 * the compiler resolves `<see cref>` and NOTHING else, so a retired claim in a `<c>` tag or a `//` comment
 * was checked by no one. `check-api-vocabulary` covers retired IDENTIFIERS on the frozen surface; nothing
 * covered a retired CLAIM. That is how `GraphMemoryOptions.AuthoritativeReserve` shipped a paragraph
 * describing an implementation the measurement had already rejected.
 *
 * Measured cost of closing it: FOUR sites. Two were real stale claims — a test asserting "both shipped
 * curves" when 3.0 deleted one of the two — and two are deliberate mentions that took `drift-ok`.
 *
 * <p>Two narrowings, both for the same reason `check-links` narrows its own code scan.</p>
 *
 * COMMENT LINES ONLY. A retired term inside a string literal is data the program uses — a SQL fragment, a
 * prompt, a test fixture — not a claim a reader believes. Non-comment lines are blanked rather than removed
 * so line numbers stay true and the soft-join window cannot bridge across code.
 *
 * `devtools/` IS EXCLUDED, and this is the one exclusion that is structural rather than a judgement: the
 * retired-term registry LIVES there, and a registry necessarily quotes every term it bans. Scanning it
 * yields 15 hits that are all the rules themselves. `check-encoding` met the identical problem and solved it
 * by construction — storing its patterns as code points so the guard never contains what it hunts — which
 * is not available for prose patterns, so the carve-out is stated instead of engineered.
 */
export const CODE_IN_SCOPE = (path) =>
  (path.endsWith('.cs') || path.endsWith('.mjs'))
  && (path.startsWith('src/') || path.startsWith('tests/') || path.startsWith('bench/'));

/** Non-comment lines blanked, so only prose is scanned and every line number stays true. */
export const commentLinesOnly = (lines) =>
  lines.map((l) => (l.trim().startsWith('//') ? l : ''));

export const IN_SCOPE = (path) =>
  path === 'README.md'
  || path === 'CLAUDE.md'
  || path === 'TASKS.md'
  // CHANGELOG.md is in scope but only for its LIVE PREFIX — see LIVE_PREFIX below, which is what keeps the
  // released sections out. Adding it here was the load-bearing half: this predicate runs FIRST, so while
  // `CHANGELOG.md` was absent from it the file was excluded before the historical filter was ever consulted,
  // and narrowing that filter changed nothing at all. Measured 2026-08-11, by probing the gate rather than
  // trusting a clean run — a silently-unscanned file reports exactly like a clean one.
  || path === 'CHANGELOG.md'
  || path.startsWith('docs/')
  || path.startsWith('.claude/');

/**
 * The tracked file list this gate scans. Its own seam so a test can supply one without a git fixture.
 *
 * `-z` (NUL-separated) is load-bearing: without it git C-QUOTES any path with a non-ASCII byte, so
 * `docs/灵台.md` arrives as `"docs/\347\201\265\345\217\260.md"`, the read below fails, and its `catch`  link-ok: a fixture name, quoted as data
 * skips the file — a doc that is never scanned and never reported as unscanned. Same root cause and same
 * fix as check-sensitive's; measured 2026-08-11 (TASKS.md Part 60).
 */
export const trackedFiles = (repo) =>
  execFileSync('git', ['ls-files', '-z'], { cwd: repo, encoding: 'utf8' }).split('\0').filter(Boolean);

/**
 * `files` is the raw candidate list (a `git ls-files` shape); it is filtered here, so a test that injects
 * one still exercises the extension, scope and historical-exclusion filters.
 */
export function checkDocs(repo, config, log = console.log, files = null) {
  const rules = config.retiredTerms ?? [];
  if (rules.length === 0) {
    log('check-docs: no retired terms configured — nothing to check.');
    return 0;
  }

  const source = files ?? trackedFiles(repo);
  const tracked = source
    // .html too: the published design record is a tracked page, and an untracked one drifted three times
    .filter((f) => f.endsWith('.md') || f.endsWith('.html'))
    .filter(IN_SCOPE)
    .filter(IS_SCANNED)
    .concat(source.filter(CODE_IN_SCOPE));

  // Fail-closed: a gate that scanned nothing must never print a tick — the rule check-api-vocabulary already
  // carries, and the one this gate was missing. `pitfalls.md` records the general shape: for any filter
  // chain, a clean run proves nothing about the stage you edited, so assert on the INTERMEDIATE.
  //
  // TWO ways a run scans nothing, and only one is always wrong. An empty SOURCE is a broken listing whoever
  // supplied it. Zero survivors of a FULL TREE means `IN_SCOPE` or the extension filter rejected everything
  // — impossible here, where README/CLAUDE/TASKS alone guarantee three — while zero from a caller-supplied
  // list is ordinary (a commit touching only `src/`), so that half is checked on the tree path alone.
  if (source.length === 0 || (files === null && tracked.length === 0)) {
    log('check-docs: ✗ found no maintained documents to scan');
    log('  Nothing was scanned, so this gate proves nothing — check IN_SCOPE and the repo root.');
    return 1;
  }

  const hits = [];
  let skipped = 0;

  for (const file of tracked) {
    let text;
    try { text = readFileSync(join(repo, file), 'utf8'); } catch { continue; }

    const isCode = CODE_IN_SCOPE(file);
    if (!isCode && SUPERSEDED_BANNER.test(text)) { skipped++; continue; }

    // A partly-historical file is scanned down to its boundary and no further, so the windows below never
    // reach across it. Line numbers are unaffected — this is a PREFIX, so index i is still line i + 1.
    const all = text.split(/\r?\n/);
    const lines = isCode ? commentLinesOnly(all) : all.slice(0, liveLineCount(file, all));

    // Each line is tested BOTH alone and soft-joined to the one after it. Line-only matching was a blind
    // spot that hid every rule in the registry from any claim spanning a wrap: these documents wrap at ~110
    // columns, so a sentence like "…`ReciprocalRankFusionPolicy`, available\nbut not the default" reads as
    // one claim and matched nothing. Found 2026-08-11 when a whole-branch review caught that exact sentence
    // in CLAUDE.md, stale, while this gate reported the file clean. A two-line window is enough by
    // construction — a wrap inserts one break, and the claims these rules describe are far shorter than a
    // line. Rules are authored against prose, so the join is a SPACE: a pattern written with `[^.\n]{0,60}`
    // still cannot run past a sentence, only past a wrap.
    const windows = lines.map((line, i) => (i + 1 < lines.length ? `${line} ${lines[i + 1]}` : line));

    for (const rule of rules) {
      const re = new RegExp(rule.term, 'g');
      lines.forEach((line, i) => {
        // `drift-ok` is the honest annotation for a passage that deliberately NAMES the retired thing.
        //
        // The two matches take DIFFERENT escapes, and conflating them was a hole (found 2026-08-15). A hit on
        // the line ALONE is excused only by that line's own annotation. A hit on the joined window spans a
        // wrap, so either line may carry it — which is the whole reason the window reads N+1. Applying the
        // N+1 escape to both meant an ordinary line inherited the exemption of whatever followed it, and two
        // unrelated adjacent paragraphs were enough to silence a genuine stale claim.
        const selfOk = line.includes('drift-ok');
        const nextOk = (lines[i + 1] ?? '').includes('drift-ok');
        re.lastIndex = 0;
        if (re.test(line)) {
          if (!selfOk) hits.push({ file, line: i + 1, rule, text: line.trim() });
          return;
        }
        if (selfOk || nextOk) return;
        re.lastIndex = 0;
        if (re.test(windows[i])) hits.push({ file, line: i + 1, rule, text: windows[i].trim() });
      });
    }
  }

  if (hits.length === 0) {
    log(`check-docs: ${tracked.length} doc(s) clean of retired vocabulary ✓`
      + (skipped ? ` (${skipped} superseded record(s) skipped)` : ''));
    return 0;
  }

  log(`check-docs: ✗ ${hits.length} use(s) of retired vocabulary — a doc that says this is out of date\n`);
  const byRule = new Map();
  for (const hit of hits) {
    if (!byRule.has(hit.rule)) byRule.set(hit.rule, []);
    byRule.get(hit.rule).push(hit);
  }
  for (const [rule, group] of byRule) {
    log(`  "${rule.term}" — say ${rule.use} instead`);
    log(`    why: ${rule.why}`);
    for (const hit of group) {
      const excerpt = hit.text.length > 96 ? hit.text.slice(0, 93) + '...' : hit.text;
      log(`    ${hit.file}:${hit.line}  ${excerpt}`);
    }
    log('');
  }
  log('  If a passage deliberately NAMES the retired thing — an amendment explaining what changed, or a');
  log('  rule quoting the word it bans — put `drift-ok` on that line.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2), because the argv fallback compares resolved paths and any way
// that comparison can be wrong makes the guard silently do NOTHING and exit 0. Pinned by cli-entry.test.mjs.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkDocs(repo, config);
}
