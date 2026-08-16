// check-links — fail when a maintained doc points at an in-repo path that is not there.
//
// The gap this closes, and it is a MEASURED one rather than a hypothetical. `docs/superpowers/INDEX.md`
// § "Archiving one that is still in `docs/`" ends with "repoint every inbound reference, and check nothing
// dangles". That step was skipped when the ranking × forgetting measurement record was untracked under D43:
// SIX references in maintained state — README (×3), the design contract (×1), DECISIONS (×2) — kept naming
// `docs/2026-08-09-memory-policy-measurement.md`, a path that had stopped existing. Every gate stayed  link-ok
// green. (That path is named deliberately: it is the dead reference this gate was BUILT for.)
// Found by a reader, which is precisely the failure mode `check-docs` and `check-encoding` were each added
// to end: a rule that is written down and still violated is a missing gate, not a knowledge problem.
//
// SCOPE, stated so nobody widens it by accident:
//   - EXISTENCE only, never line numbers. A `file.cs:123` reference rots on the next edit for entirely
//     legitimate reasons, and `pitfalls.md` §DI/config already records line numbers rotting twice and being
//     deleted in favour of names. Gating them would make every refactor fail this check for no defect.
//   - `local/**` is skipped: untracked by design (`docs/superpowers/INDEX.md`), so "not on disk" says
//     nothing about whether the reference is right.
//   - The SAME "is this maintained state?" predicates as check-docs, imported rather than restated. Two
//     copies of that question drift the moment a document is archived, and silently, in the permissive
//     direction, on whichever copy was forgotten — check-samples already imports them for this reason.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { HISTORICAL, IN_SCOPE, IS_SCANNED, LIVE_PREFIX, liveLineCount } from './check-docs.mjs';

const here = fileURLToPath(import.meta.url);
const repo = join(dirname(here), '..', '..');

export { HISTORICAL, IN_SCOPE, IS_SCANNED, LIVE_PREFIX };

/**
 * A path-shaped token in prose: anchored on one of the repository's own top-level directories and closed
 * on a known source/doc extension, which keeps bare prose ("the docs/ directory") out of the result set
 * without needing a heuristic.
 *
 * THE LEADING LOOKBEHIND IS LOAD-BEARING, and this gate's own test is what found that out. A bare `\b`
 * sits happily between the `/` and the `d` of `https://example.invalid/docs/thing.md`, so every URL
 * carrying a `/docs/…md` — and every `vendor/docs/other.md` — was reported as a dangling in-repo
 * reference on the first run. `(?<![\w/.-])` requires the directory name to actually START a path.
 *
 * An optional `:NNN` suffix is CONSUMED but not checked — see the scope note above.
 *
 * THE EXTENSION ALTERNATION IS ORDERED LONGEST-FIRST, and that is a fix rather than a style. Regex
 * alternation takes the FIRST branch that matches, not the longest, so with `cs` ahead of `csproj` every
 * project path matched as far as `.cs` and stopped: `src/Lyntai.Core/Lyntai.Core.csproj` was captured as
 * `src/Lyntai.Core/Lyntai.Core.cs`, which is on no disk, and reported as a dangling reference. It failed
 * CLOSED (a false failure, never a silent pass) but named a path nobody wrote, and it was latent only
 * because no maintained doc happens to name a `.csproj` — `repo-mechanics.md` §Package layout is one `src/`
 * prefix away from tripping it. Found 2026-08-14 by running this pattern over the code tiers the gate does
 * not scan; pinned by check-links.test.mjs' "matches a LONG extension whole".
 */
export const PATH_PATTERN =
  /(?<![\w/.-])((?:src|tests|devtools|bench|samples|docs|local|\.claude)\/[A-Za-z0-9_./-￿-]+\.(?:csproj|props|slnx|html|json|yaml|mjs|sql|txt|yml|md|cs))(?::\d+)?/g;

/**
 * A reference that names one of the two task records AND a Part in it: `` `TASKS.md` Part 53 ``,
 * `docs/task-archive.md` **Part 54**, `task-archive.md` Part 60.
 *
 * The SECOND way an inbound reference rots, and no path check can see it — the path resolves and the Part
 * exists, in the OTHER file. `task-lifecycle.md`'s premise is that the two records answer different
 * questions ("what is left" versus "how was it closed"), so a reference to the wrong one sends a reader
 * somewhere the answer is not. Every archived task silently converts each surviving `TASKS.md Part N` into
 * exactly that. Measured 2026-08-14: five live ones, across CHANGELOG's Unreleased prefix and docs/FIXES.md.
 *
 * A BARE `Part 53` is deliberately not matched. Prose says it constantly without claiming which file holds
 * it, and only a reference that NAMES a record makes a checkable claim — flagging the rest is the
 * crying-wolf failure `retiredTerms`' own comments warn about. The gap between the two is bounded to 24
 * non-period characters for the same reason: it spans `` ` `` and `**`, never a sentence boundary.
 */
export const PART_PATTERN =
  /`?(TASKS\.md|(?:docs\/)?task-archive\.md)`?[^.\n]{0,24}?\bPart (\d+)/g;

/**
 * Part numbers declared in a task record.
 *
 * THREE shapes, because this record uses three. `##` and `###` headings are the common ones (the archive
 * files closed sub-entries at `###`), and a closed item is often filed as a LIST ITEM instead —
 * `- [x] **Part 41 — …**`. Reading headings only made those Parts invisible, so a correct reference to one
 * was reported as "in NEITHER record": a false positive on right prose.
 *
 * That mattered more than a lone false positive normally would, because it CANCELLED a false negative.
 * The one live defect this gate was built for — the design contract naming `TASKS.md` for an archived
 * Part 40 — was both wrapped across two lines (invisible to the old line-at-a-time match) and
 * bullet-declared (invisible here). Two blind spots, opposite signs, one green gate over a real defect.
 * Fixing either alone would have surfaced it; fixing neither kept it quiet for a release.
 */
export const declaredParts = (text) => {
  const parts = new Set();
  for (const line of text.split(/\r?\n/)) {
    const heading = /^#{2,3} Part (\d+)\b/.exec(line);
    if (heading) { parts.add(Number(heading[1])); continue; }
    const item = /^\s*[-*]\s+(?:\[[ xX]\]\s+)?\*{0,2}Part (\d+)\b/.exec(line);
    if (item) parts.add(Number(item[1]));
  }
  return parts;
};

/**
 * The tracked file list, `-z` so git does not C-QUOTE a non-ASCII path — `docs/灵台.md` would otherwise  link-ok
 * arrive as an 8-escape string matching no file on disk, and this gate would both fail to scan it AND
 * report every reference to it as dangling. Same root cause as check-sensitive's and check-docs' own,
 * measured 2026-08-11 (TASKS.md Part 60).
 */
export const trackedFiles = (repo) =>
  execFileSync('git', ['ls-files', '-z'], { cwd: repo, encoding: 'utf8' }).split('\0').filter(Boolean);

/**
 * Check every maintained doc's in-repo references resolve.
 *
 * `files` is the raw candidate list (a `git ls-files` shape) and doubles as the on-disk set, so a test
 * supplies one list and gets both halves — a reference is dangling exactly when it names something the
 * tracked list does not contain.
 */
export function checkLinks(repo, config, log = console.log, files = null) {
  const tracked = files ?? trackedFiles(repo);
  const onDisk = new Set(tracked);
  const allowances = config.staleReferenceAllowances ?? [];

  const docs = tracked
    .filter((f) => f.endsWith('.md'))
    .filter(IN_SCOPE)
    .filter(IS_SCANNED);

  // The CODE tiers, added 2026-08-15 (Part 72). Narrower than the prose scan on BOTH axes, and each
  // narrowing is a measured decision rather than caution:
  //
  //   COMMENT LINES ONLY — a path in a string literal is data the program uses, not a reference a reader
  //   follows.
  //
  //   `docs/` TARGETS ONLY — `pitfalls.md` records an existence check over prose returning ~45 hits and
  //   zero defects, and that came from checking EVERY path: source files are renamed for legitimate
  //   reasons and a comment describing the old shape is correct. Documents move, and a moved document is
  //   the defect this gate was built for. `local/` stays skipped for the reason it always was — untracked
  //   by design, so "not on disk" says nothing.
  //
  // Part 72 proposed a third narrowing — `///` XML docs only — and the measurement REFUSED it. Replaying
  // the pre-repair tree: 9 genuine dead references lived in the code tiers, an XML-only rule catches 6, and
  // all 3 it misses were in ordinary `//` comments and all 3 were real. The entry's hypothesis was that
  // `//` comments would be where false positives live; every false positive was in fact a guard script
  // naming a FIXTURE, which is what `link-ok` is for. So the line is drawn at the target, not the style.
  const code = tracked
    .filter((f) => /\.(cs|mjs)$/.test(f))
    .filter((f) => /^(src|tests|bench|samples|devtools)\//.test(f))
    // The guard fixtures are synthetic paths BY DESIGN — a tree built to be scanned, never to be followed.
    .filter((f) => !f.includes('__tests__'));

  // Fail-closed: a gate that scanned nothing must never print a tick (check-api-vocabulary's rule, which
  // this gate was missing). It shares check-docs' scope predicates, so a broken one disarms BOTH at once —
  // which is exactly the divergence sharing them was meant to prevent, arriving from the other direction.
  //
  // Split the same way as its twin: an empty SOURCE is a broken listing whoever supplied it, while zero
  // survivors is only an indictment on the FULL-TREE path (a caller-supplied list may legitimately contain
  // no maintained doc at all).
  if (tracked.length === 0 || (files === null && docs.length === 0)) {
    log('check-links: ✗ found no maintained documents to scan');
    log('  Nothing was scanned, so this gate proves nothing — check IN_SCOPE and the repo root.');
    return 1;
  }

  // NO fail-closed guard on the code half, and the reason is worth stating because the other scanners all
  // have one. A fail-closed check needs a SOURCE the filtered set can be compared against, and this filter
  // has DELIBERATE exclusions (`__tests__`, non-tier directories) — so "zero survivors" cannot be told
  // apart from "legitimately nothing to scan" without duplicating the filter, which would then agree with
  // itself by construction. Two attempts proved it empirically: guarding on `code.length === 0` failed the
  // CJK-fixture test (a repository of two markdown files), and guarding on "the tree has code but none
  // survived" failed the `__tests__`-skip test (a repository whose only code is deliberately excluded).
  // Instead the green line REPORTS the count, so a filter that stopped matching shows up as `0 code
  // file(s)` on a passing run, and a test pins the real tree's count above zero.

  const allowed = new Map(allowances.map((a) => [a.file, { ...a, used: 0 }]));
  const hits = [];
  const misfiled = [];

  // Read once, up front: the Part half compares every reference against BOTH records, so scanning the
  // records lazily per hit would re-read them for each one. A record that is not tracked yields an empty
  // set, which makes every reference to it "nowhere" — reported, never silently passed.
  const partsIn = (f) => {
    try { return declaredParts(readFileSync(join(repo, f), 'utf8')); } catch { return new Set(); }
  };
  const openParts = partsIn('TASKS.md');
  const archivedParts = partsIn('docs/task-archive.md');

  for (const file of docs) {
    let text;
    try { text = readFileSync(join(repo, file), 'utf8'); } catch { continue; }

    // A partly-historical file is read down to its boundary and no further — the released half of a
    // CHANGELOG names paths that were right on the day, which is the whole reason it is exempt at all.
    const all = text.split(/\r?\n/);
    const lines = all.slice(0, liveLineCount(file, all));

    for (const [i, line] of lines.entries()) {
      // `link-ok` — its OWN annotation, deliberately not check-docs' `drift-ok`.
      //
      // The two silence unrelated gates, and sharing one token means a line annotated for a path reason
      // silently stops being checked for retired VOCABULARY too (and the reverse). That is a hole nobody
      // can see opening, on a line somebody already had a reason to annotate. The measured need is real
      // rather than theoretical: `docs/FIXES.md` and `pitfalls.md` describe the leak-scanner incident by
      // NAMING its fixtures (`docs/灵台.md`, `docs/plain.md`) — paths that never existed in this repository  link-ok
      // and never should. Those are prose about data, not links, and no pattern can tell the difference.
      if (line.includes('link-ok')) continue;
      for (const [, target] of line.matchAll(PATH_PATTERN)) {
        if (target.startsWith('local/')) continue;   // untracked by design
        if (onDisk.has(target)) continue;
        const allowance = allowed.get(file);
        if (allowance) { allowance.used++; continue; }
        hits.push({ file, line: i + 1, target, text: line.trim() });
      }

      // The Part half. A reference is wrong when the record it NAMES does not declare that Part — whether
      // the other record does (mis-filed) or neither does (gone).
      //
      // Matched over a SOFT-JOINED two-line window, not the raw line. These documents wrap at ~110 columns
      // and a Part reference spans a backtick, a filename and a bold marker, so it is among the likeliest
      // claims to straddle a break — and the one live defect this gate existed for had done exactly that:
      // the design contract's "`TASKS.md`\n**Part 40**", naming the backlog for a Part archived long ago.
      // check-docs carries the identical window and its comment generalises the rule to "any future text
      // gate"; this gate was written three days later and did not carry it. `line` alone stays the unit for
      // the PATH half above, where a target is a single token and cannot wrap mid-name.
      //
      // A match is kept only when it BEGINS in this line: one that begins in the next is seen again when
      // that line is the window's own first line, and reporting it from both would double-count every
      // reference in the file. Anchoring on the start index is exact, where deduplicating by file+part
      // would silently collapse two genuinely distinct references into one report.
      const window = i + 1 < lines.length ? `${line} ${lines[i + 1]}` : line;
      for (const match of window.matchAll(PART_PATTERN)) {
        if (match.index > line.length) continue;
        const [, record, num] = match;
        const n = Number(num);
        const claimsBacklog = record === 'TASKS.md';
        if (claimsBacklog ? openParts.has(n) : archivedParts.has(n)) continue;
        const elsewhere = claimsBacklog ? archivedParts.has(n) : openParts.has(n);
        misfiled.push({
          file,
          line: i + 1,
          record,
          part: n,
          actually: elsewhere ? (claimsBacklog ? 'in the ARCHIVE' : 'still OPEN in TASKS.md') : 'in NEITHER record',
          text: line.trim(),
        });
      }
    }
  }

  // The code tiers: comment lines only, `docs/` targets only. No Part half — a task-record reference is a
  // prose convention, and the measurement found none in code.
  const COMMENT = /^\s*(?:\/\/|\*|#)/;
  for (const file of code) {
    let text;
    try { text = readFileSync(join(repo, file), 'utf8'); } catch { continue; }

    for (const [i, line] of text.split(/\r?\n/).entries()) {
      if (!COMMENT.test(line)) continue;
      if (line.includes('link-ok')) continue;
      for (const [, target] of line.matchAll(PATH_PATTERN)) {
        if (!target.startsWith('docs/')) continue;
        if (onDisk.has(target)) continue;
        hits.push({ file, line: i + 1, target, text: line.trim() });
      }
    }
  }

  // An allowance that matches NOTHING fails, the same rule check-api-vocabulary's own escapes carry: an
  // exclusion nobody can see expiring is an exclusion that rots into a permanent hole.
  const dead = [...allowed.values()].filter((a) => a.used === 0);

  if (hits.length === 0 && dead.length === 0 && misfiled.length === 0) {
    // Both counts are reported, so a filter that silently stopped matching one tier is visible in the
    // GREEN line rather than only in a failure that never comes.
    log(`check-links: ${docs.length} maintained doc(s) + ${code.length} code file(s) — every in-repo `
      + `reference resolves ✓`
      + (allowances.length ? ` (${allowances.length} allowance(s), all still needed)` : ''));
    return 0;
  }

  if (hits.length > 0) {
    log(`check-links: ✗ ${hits.length} reference(s) to a path that is not in the repository\n`);
    for (const hit of hits) {
      const excerpt = hit.text.length > 96 ? hit.text.slice(0, 93) + '...' : hit.text;
      log(`  ${hit.file}:${hit.line}  ->  ${hit.target}`);
      log(`      ${excerpt}`);
    }
    log('');
    log('  A document that MOVED needs every inbound reference repointed — that is the last step of');
    log('  `docs/superpowers/INDEX.md` § "Archiving one that is still in `docs/`", and skipping it is what');
    log('  this gate exists to catch. If a passage deliberately names a path that is gone (a guard FIXTURE,');
    log('  a changelog entry about the move itself), put `link-ok` on that line — NOT `drift-ok`, which is');
    log('  check-docs\' annotation and must not silence this gate too. If a whole document is a record whose');
    log('  paths were right on its own day, give it an entry in `staleReferenceAllowances`');
    log('  (devtools/project.config.mjs) with the reason.');
  }

  if (misfiled.length > 0) {
    log(`\ncheck-links: ✗ ${misfiled.length} reference(s) naming the WRONG task record for a Part\n`);
    for (const m of misfiled) {
      const excerpt = m.text.length > 96 ? m.text.slice(0, 93) + '...' : m.text;
      log(`  ${m.file}:${m.line}  says ${m.record} Part ${m.part} — it is ${m.actually}`);
      log(`      ${excerpt}`);
    }
    log('');
    log('  `TASKS.md` is what is STILL TO DO; `docs/task-archive.md` is how a finished task was closed');
    log('  (.claude/rules/task-lifecycle.md). A reference to the wrong one sends a reader to the record');
    log('  that does not hold the answer, and ARCHIVING a task is what turns a right one into a wrong one —');
    log('  so repoint inbound references in the same change that moves the task. If a line deliberately');
    log('  quotes an old reference as it was written, put `link-ok` on it.');
  }

  if (dead.length > 0) {
    log(`\ncheck-links: ✗ ${dead.length} stale-reference allowance(s) no longer match anything:\n`);
    for (const a of dead) log(`  ${a.file} — every reference in it now resolves; delete this allowance.`);
  }

  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths, and any way that
// comparison can be wrong makes the guard silently do NOTHING and exit 0.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  const config = (await import('../project.config.mjs')).default;
  process.exitCode = checkLinks(repo, config);
}
