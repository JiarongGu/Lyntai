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
import { repoFiles, twoLineWindows, windowHits } from './_repo-files.mjs';

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
  // Two entries were REMOVED here on 2026-09-16, and removed rather than left because an exemption whose
  // file is gone is an exemption nobody can see expiring — the rule this repository already states for
  // `staleReferenceAllowances` and `retiredApiNames`. The 2026-08-04 generation plan and the 2.5→3.0
  // migration guide are both untracked now (**D149**): a document exempted from every prose gate BECAUSE
  // it is a record of its own day is a document that has finished, and the honest place for it is
  // `local/superpowers/`, not a permanent hole in this list.
  //
  // The frozen v0.1 design record. Its §5 blocks are SEEDS kept verbatim and its amendments are DATED, so
  // "out of date" is its normal and intended state — the amendment log is what carries the present tense.
  // Added 2026-09-15 after this gate did the damage it exists to prevent: under the pressure to stay green,
  // a rename sweep rewrote the v0.1 blocks and the dated amendments alike, collapsing `IGenerationProvider`
  // and `IGenerationStreamProvider` to one name in a sentence that CONTRASTED them, and restating a 2.0.1
  // package merge under a name that package never had. A gate checking CURRENT vocabulary against a record
  // of its own day does not find drift; it manufactures it.
  //
  // NARROWED 2026-09-19 (D164): the seeds stay exempt, but the DATED AMENDMENTS are re-admitted through
  // LIVE_REGIONS below — they are the file's live half, present-tense contract claims, and the 2026-09-19
  // review found D152–D158 had left zero trace in them precisely because this whole-file hole hid them
  // from every sweep. The exemption's reason only ever covered the seeds.
  /^docs\/2026-07-17-lyntai-design\.md$/,
];

/**
 * A file that is historical BELOW a boundary and MAINTAINED above it — scanned down to that line and no
 * further.
 *
 * `CHANGELOG.md` is the case, added 2026-08-11 (docs/task-archive.md Part 53). The exemption above rests
 * on records
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

/** A file is READ when it is not wholly historical; a partly-historical one is read for its live
 * prefix (CHANGELOG) or its live regions (the design record's dated amendments, D164). */
export const IS_SCANNED = (path) =>
  !HISTORICAL.some((re) => re.test(path))
  || LIVE_PREFIX.some((r) => r.file.test(path))
  || LIVE_REGIONS.some((r) => r.file.test(path));

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
 * A file that is historical THROUGHOUT except for its INLINE dated amendments — the design record's
 * shape, where seeds and live amendments INTERLEAVE, so no prefix boundary can express the split (D164).
 * The mask keys on the syntax the document already uses, so re-scoping the exemption cost it no new
 * markup: an inline `*(YYYY-MM-DD: …)*` runs to its closing `)*` and is LIVE — the document's convention
 * makes that form the present-tense contract tier. A `> **Amendment (…)**` blockquote stays EXEMPT on a
 * measurement: the seven of them are period records (shipping summaries, superseded policy statements —
 * 157 lines carrying 49 retired-vocabulary hits that are each accurate for their day), which is the
 * cry-wolf ratio D144/D158 refuse to gate. State the CURRENT contract inline; record a period in a
 * blockquote.
 */
export const LIVE_REGIONS = [
  { file: /^docs\/2026-07-17-lyntai-design\.md$/, mask: amendmentMask },
];

/** The line mask for a seeds-plus-amendments record: true exactly on the INLINE dated amendment units. */
export function amendmentMask(lines) {
  const mask = new Array(lines.length).fill(false);
  let i = 0;
  while (i < lines.length) {
    if (/^\s*\*\(20\d\d-\d\d-\d\d/.test(lines[i])) {
      // inline dated amendment, through its closing `)*` — which may be the same line
      let j = i;
      for (; j < lines.length; j++) {
        mask[j] = true;
        if (/\)\*\s*$/.test(lines[j])) break;
      }
      i = j + 1;
      continue;
    }
    i++;
  }
  return mask;
}

/**
 * The line-level answer to "which of this file's lines are maintained state": null for an ordinary
 * document (all of it), a boolean per line otherwise — prefix-shaped files render as prefix-shaped masks,
 * region-shaped files as their amendment units. The single source every prose gate reads, for the reason
 * HISTORICAL's own note gives: answering it differently in two gates is how the permissive copy goes
 * unnoticed.
 */
export function liveLineMask(file, lines) {
  const region = LIVE_REGIONS.find((r) => r.file.test(file));
  if (region) return region.mask(lines);
  const prefix = liveLineCount(file, lines);
  if (prefix === Infinity) return null;
  return lines.map((_, i) => i < prefix);
}

/**
 * The lines a prose gate should read, with the historical ones BLANKED rather than removed — a blank
 * neutralizes content while keeping every index true, so `file:line` in a report still names the line a
 * reader will find. (The prefix rules used to slice; slicing cannot express an interleaved mask.)
 */
export function liveLinesOnly(file, lines) {
  const mask = liveLineMask(file, lines);
  return mask === null ? lines : lines.map((l, i) => (mask[i] ? l : ''));
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

/**
 * Non-comment lines blanked, so only prose is scanned and every line number stays true. A surviving
 * comment line is reduced to its PROSE content — leading indentation AND the `//`/`///` marker both
 * stripped — so `twoLineWindows`' join reads as continuous prose across two comment lines exactly as it
 * does across two paragraph lines. Left in, the marker sits between the two halves of a wrapped claim and
 * breaks the same single-space adjacency raw indentation does — the reason `twoLineWindows` itself trims.
 */
export const commentLinesOnly = (lines) =>
  lines.map((l) => (l.trim().startsWith('//') ? l.trim().replace(/^\/{2,3}\s*/, '') : ''));

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
 * fix as check-sensitive's; measured 2026-08-11 (docs/task-archive.md Part 60).
 */
export const trackedFiles = (repo) => repoFiles(repo);

/**
 * `files` is the raw candidate list (a `git ls-files` shape); it is filtered here, so a test that injects
 * one still exercises the extension, scope and historical-exclusion filters.
 */
/**
 * The PAIRING audit between the two retirement registries. The gap this closes was measured: D157's renames
 * entered `retiredApiNames` (the baseline registry) and never `retiredTerms`, so README recommended a
 * deleted registration for a day while every gate reported clean. Every `names` entry must now either be
 * matched, name for name, by some hand-written `retiredTerms` rule, or carry `proseExempt: '<why>'` — so
 * the prose half of a rename is decided AT RENAME TIME instead of discovered by a reader.
 *
 * DELIBERATELY an audit, not a derivation. Auto-deriving prose rules from the names was built first and
 * measured at 1,861 hits on this tree — decision-record narration, vocabulary that is retired on ONE seam
 * and live on others (`EmbedAsync`), and ordinary words (`dialect`, `Flags`) — which is the cry-wolf shape
 * `pitfalls.md` records as untightenable. Baseline names are CONTEXTUAL (retired FROM a surface); prose
 * matching is placeless, so only a human can write the narrow rule. This audit makes forgetting to do so
 * fail loudly, which was the whole defect.
 *
 * `pattern:` entries audit nothing: a pattern is an identifier SHAPE with no finite name list. The
 * exemption's reason is REQUIRED, and an exemption on an entry whose names are all hand-covered FAILS,
 * because a dead escape is where the next miss hides.
 */
export function auditProsePairing(retiredApiNames = [], retiredTerms = []) {
  const handRules = retiredTerms.map((r) => new RegExp(r.term));
  // A name counts as covered when some rule fires on it BARE or in CALL SHAPE — `AddEmbeddings\s*[(<]` is a
  // real rule written narrow on purpose, and demanding it also match the bare token would force it wider
  // than its author chose.
  const covered = (name) => handRules.some((re) => re.test(name) || re.test(`${name}(`));
  const defects = [];
  for (const entry of retiredApiNames) {
    if (!Array.isArray(entry.names)) continue;
    const uncovered = entry.names.filter((n) => !covered(n));
    if (entry.proseExempt !== undefined) {
      if (typeof entry.proseExempt !== 'string' || entry.proseExempt.trim() === '')
        defects.push(`a proseExempt with no reason on [${entry.names.join(', ')}] — an unexplained escape is a silent exclusion`);
      else if (uncovered.length === 0)
        defects.push(`the proseExempt on [${entry.names.join(', ')}] is doing nothing — every name is already matched by a hand-written retiredTerms rule, and a dead escape is where the next miss hides`);
      continue;
    }
    if (uncovered.length > 0)
      defects.push(`[${uncovered.join(', ')}] retired from the API surface with no retiredTerms rule matching`
        + ' — write the narrow prose rule, or record proseExempt with the reason prose cannot ban the word');
  }
  return defects;
}

export function checkDocs(repo, config, log = console.log, files = null) {
  const pairingDefects = auditProsePairing(config.retiredApiNames, config.retiredTerms ?? []);
  if (pairingDefects.length > 0) {
    log(`check-docs: ✗ ${pairingDefects.length} retiredApiNames entr(ies) unpaired with the prose registry\n`);
    for (const defect of pairingDefects) log(`  ${defect}`);
    log('\n  A rename retired on the SURFACE but not in PROSE is invisible for exactly one tier — the one');
    log('  README is in. The prose rule is hand-written because baseline names are contextual and prose is');
    log('  not; `proseExempt` records the entries where no safe prose rule exists.');
    return 1;
  }
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

    // A partly-historical file is scanned only where it is LIVE — a prefix (CHANGELOG) or the dated
    // amendment regions (the design record, D164) — with historical lines BLANKED rather than sliced, so
    // index i is still line i + 1 and a window never joins live prose to a record's.
    const all = text.split(/\r?\n/);
    const lines = isCode ? commentLinesOnly(all) : liveLinesOnly(file, all);

    // A claim is matched on its line or across the wrap into the next (`windowHits`: one report per hit,
    // at the line it begins on, and `drift-ok` on either line excuses only a hit straddling the join).
    const windows = twoLineWindows(lines);

    for (const rule of rules) {
      const reported = new Set();
      for (const h of windowHits(lines, new RegExp(rule.term, 'g'), { escape: 'drift-ok', windows })) {
        if (h.escaped || reported.has(h.at)) continue;
        reported.add(h.at);
        hits.push({ file, line: h.at + 1, rule, text: (h.straddles ? windows[h.at] : lines[h.at]).trim() });
      }
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
