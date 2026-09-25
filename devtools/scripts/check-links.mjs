// check-links — fail when a maintained doc points at an in-repo path that is not there.
//
// It checks a reference four ways — a PATH that must exist, a `TASKS.md`/archive Part that must be in the
// record it names, a `§` section that must be a heading, a `Type.Member` that must be declared — because
// there are four ways one rots. Why each exists, what was measured, and its scope: `docs/GATES.md`
// §check-links. EXISTENCE only, never line numbers, and `local/**` is skipped (untracked by design).
// Escape: `link-ok`, this gate's own. The "is this maintained state?" predicates are check-docs',
// imported rather than restated.
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { HISTORICAL, IN_SCOPE, IS_SCANNED, LIVE_PREFIX, liveLinesOnly } from './check-docs.mjs';
import { readRepoText, repoFiles, twoLineWindows, windowHits } from './_repo-files.mjs';

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
 * A reference that names one of the two task records AND a Part in it: `` `TASKS.md` Part 53 ``, link-ok: pattern SHAPES, not live citations
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
 * A citation naming a TYPE and one of its MEMBERS: `` `GenerationCapabilities.Supports` ``, or the same
 * pair inside a `<see cref="...">`.
 *
 * The FOURTH way an inbound reference rots, and the other three are all blind to it — the path resolves,
 * the record is right, the § exists, and only the member name is invented. Measured 2026-08-30: a session
 * inferred `GenerationCapabilities.CanServe` from a grep that printed the method BODY without its  link-ok
 * signature, and the name reached TWO commits before a compiler error in an unrelated test exposed it.
 * `CanServe` appears in zero `.cs` files. Nothing else could have caught it: a member name in prose is not
 * a path, not a Part and not a section, and only a `///` cref is checked by the compiler.
 */
export const MEMBER_PATTERN = /`([A-Z][A-Za-z0-9]*)\.([A-Z][A-Za-z0-9]*)`/g;

/** The same claim in XML-doc form, where only `///` crefs are compiler-checked — `//` and `*` are not. */
export const MEMBER_CREF_PATTERN = /<(?:see|seealso) cref="(?:[TPMF]:)?([A-Z][A-Za-z0-9]*)\.([A-Z][A-Za-z0-9]*)"/g;

/**
 * The C# vocabulary a member citation is checked against: every declared type name, and every identifier
 * that appears anywhere in the `.cs` tree.
 *
 * REQUIRING THE LEFT SIDE TO BE A TYPE WE DECLARE is the precision knob, and it was chosen by measurement
 * rather than caution. Without it, `Lyntai.Bundle` — a package id, not a member access — is reported as a
 * dangling member. With it, 770 citations across the docs and code tiers produce exactly ONE flag, and that
 * one is a deliberately-named rejected alternative (what `link-ok` is for).
 *
 * The cost is stated rather than hidden: a BCL type's members are never checked, because `StringComparison`
 * is not declared here. That is the same trade the path half makes by scanning `docs/` targets only.
 */
export const csharpVocabulary = (texts) => {
  const known = new Set();
  const types = new Set();
  for (const text of texts) {
    // COMMENTS ARE STRIPPED FIRST, and this is the difference between a gate and a no-op. The index answers
    // "does this member exist?", so harvesting it from prose lets a citation AUTHORIZE ITSELF: write
    // `Type.Nonexistent` in a `//` comment and `Nonexistent` joins the vocabulary, after which neither that
    // comment nor any document naming it can ever be flagged. Caught by this gate's own code-tier test,
    // which failed on exactly that self-reference.
    const code = text.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/.*$/gm, ' ');
    for (const m of code.matchAll(/\b[A-Za-z_]\w*\b/g)) known.add(m[0]);
    for (const m of code.matchAll(/\b(?:class|interface|record|struct|enum)\s+([A-Z]\w*)/g)) types.add(m[1]);
  }
  return { known, types };
};

/**
 * Part numbers declared in a task record.
 *
 * THREE shapes, because this record uses three. `##` and `###` headings are the common ones (the archive
 * files closed sub-entries at `###`), and a closed item is often filed as a LIST ITEM instead —
 * `- [x] **Part 41 — …**`. Reading headings only made those Parts invisible, so a correct reference to one
 * was reported as "in NEITHER record": a false positive on right prose.
 *
 * That mattered more than a lone false positive normally would, because it CANCELLED a false negative.
 * The one live defect this gate was built for — the design contract naming `TASKS.md` for an archived link-ok: quotes the defect as it was written
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
 * A citation naming a SECTION of a document: `` `docs/memory.md` §7 ``, `<c>pitfalls.md</c> §Storage`,
 * `docs/d.md` §5–7 (an ILLUSTRATION of the range shape, never a file here). link-ok
 *
 * The THIRD way an inbound reference rots, and neither half above can see it — the path resolves, the
 * record is right, and the §N names a heading that is not there. Measured 2026-08-28 (docs/task-archive.md
 * Part 107): `docs/memory.md`'s `## 8. What is NOT measured` was folded into `## 7` while §9/§10 were left
 * un-renumbered, and SEVEN citations across six files kept naming a section that had stopped existing.
 *
 * ONLY the unambiguous form — the filename, an optional closing delimiter, then the §. Anything looser
 * mis-attributes: `` `pitfalls.md` (§Second doors) `` and `design §7` both sit within a few words of an
 * UNRELATED filename, and a gate that guesses the target is a gate that names the wrong file. The
 * measurement started with a 40-character window and every one of its false targets came from that.
 *
 * A RANGE is captured whole because every end of one is a separate claim. `docs/superpowers/INDEX.md`
 * cited `§7–8`: a scan for "§8" does not match it and a scan reading only the first number resolves it, so
 * that citation survived both the human pass that filed Part 107 and the first probe written to measure it.
 */
export const ANCHOR_PATTERN =
  /([A-Za-z0-9_./¡-￿-]*\.md)(?:<\/c>|`)?\s*§\s*(\d+(?:\.\d+)*[a-z]?(?:\s*[–—-]\s*\d+(?:\.\d+)*[a-z]?)?|[A-Z][A-Za-z-]*(?:\s+[a-z][A-Za-z-]*){0,4})/g;

/**
 * The anchors a document declares: every heading's numeric label and its text, plus BOLD BULLET LEADS.
 *
 * The bullets are not generosity. `task-lifecycle.md` §Keep the summary honest names a bolded bullet
 * rather than a heading — a reader Ctrl-Fs it and finds it at once — and it was the ONLY recurring false
 * positive the measurement produced over the whole tree. Indexing them takes the named half to zero.
 */
export const declaredAnchors = (text) => {
  const nums = new Set();
  const names = [];
  for (const line of text.split(/\r?\n/)) {
    const heading = /^#{1,6}\s+(.*)$/.exec(line);
    if (heading) {
      const label = heading[1].replace(/[*`]/g, '').trim();
      names.push(label.toLowerCase());
      const n = /^(\d+(?:\.\d+)*[a-z]?)(?:[.)\s]|$)/.exec(label);
      if (n) nums.add(n[1].toLowerCase());
      continue;
    }
    const bullet = /^\s*[-*]\s+\*\*(.+?)\*\*/.exec(line);
    if (bullet) names.push(bullet[1].replace(/[.`]/g, '').trim().toLowerCase());
  }
  return { nums, names };
};

/**
 * The end of a citation that names nothing, or `null` when it resolves.
 *
 * A NUMERIC citation resolves against a label or a deeper one it prefixes (`§5.7` covers `### 5.7.0`).
 * A NAMED one resolves against any leading WORD-PREFIX of the token, because a named citation has no
 * closing delimiter and runs straight into the sentence after it — `§Storage records three incidents`.
 * Without the prefix rule every named citation in the tree reports, which is the crying-wolf failure
 * `retiredTerms`' own comments warn about.
 */
export const unresolvedAnchor = (anchors, token) => {
  const t = token.trim().toLowerCase().replace(/[.,;:)\]]+$/, '');
  if (!t) return null;
  if (/^\d/.test(t)) {
    for (const end of t.split(/\s*[–—-]\s*/)) {
      const e = end.trim();
      if (!e) continue;
      if (anchors.nums.has(e)) continue;
      if ([...anchors.nums].some((n) => n.startsWith(e + '.'))) continue;
      return e;
    }
    return null;
  }
  const words = t.split(/\s+/);
  for (let k = words.length; k >= 1; k--) {
    const prefix = words.slice(0, k).join(' ');
    if (anchors.names.some((n) => n.startsWith(prefix))) return null;
  }
  return t;
};

/**
 * Check every maintained doc's in-repo references resolve.
 *
 * `files` is the raw candidate list (a `git ls-files` shape) and doubles as the on-disk set, so a test
 * supplies one list and gets both halves — a reference is dangling exactly when it names something the
 * tracked list does not contain.
 */
export function checkLinks(repo, config = {}, log = console.log, files = null) {
  const tracked = files ?? repoFiles(repo);
  const onDisk = new Set(tracked);

  // The MEMBER half's vocabulary, read from the same tracked listing everything else uses.
  const { known: knownIdents, types: knownTypes } = csharpVocabulary(
    tracked.filter((f) => f.endsWith('.cs')).map((f) => {
      try { return readFileSync(join(repo, f), 'utf8'); } catch { return ''; }
    }));
  const deadMembers = [];
  const scanMembers = (file, lineNo, text) => {
    if (text.includes('link-ok')) return;
    for (const re of [MEMBER_PATTERN, MEMBER_CREF_PATTERN]) {
      for (const [, type, member] of text.matchAll(re)) {
        if (!knownTypes.has(type) || knownIdents.has(member)) continue;
        deadMembers.push({ file, line: lineNo, type, member, text: text.trim() });
      }
    }
  };

  const docs = tracked
    .filter((f) => f.endsWith('.md'))
    .filter(IN_SCOPE)
    .filter(IS_SCANNED);

  // The CODE tiers: comment lines only, and `docs/` targets only for the path half — both measured, and
  // why is `docs/GATES.md` §check-links. The guard tests are scanned too: their fixtures live in string
  // literals, which no comment-line scan reads, and a comment NAMING a fixture takes `link-ok`.
  const code = tracked
    .filter((f) => /\.(cs|mjs)$/.test(f))
    .filter((f) => /^(src|tests|bench|samples|devtools)\//.test(f));

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

  // NO fail-closed guard on the code half: a repository of markdown alone legitimately has no code to
  // scan, so "zero survivors" proves nothing either way. The green line REPORTS the count instead.

  const hits = [];
  const misfiled = [];
  const deadAnchors = [];
  let anchorsChecked = 0;

  // A citation names a document, usually by BARE BASENAME (`` `pitfalls.md` §Storage ``). Resolving one to
  // exactly one tracked file is the whole admission test: a basename matching two files — or none — is a
  // guess, and PART_PATTERN's rule applies unchanged, that only a reference naming a resolvable record
  // makes a checkable claim.
  const byBasename = new Map();
  for (const f of tracked) {
    if (!f.endsWith('.md')) continue;
    const base = f.replace(/^.*\//, '');
    if (!byBasename.has(base)) byBasename.set(base, []);
    byBasename.get(base).push(f);
  }
  const resolveDoc = (name) => {
    if (name.startsWith('local/')) return null;         // untracked by design
    if (onDisk.has(name)) return name;
    const candidates = byBasename.get(name.replace(/^.*\//, '')) ?? [];
    return candidates.length === 1 ? candidates[0] : null;
  };

  // Read each cited document once. An unreadable target yields `null` and is SKIPPED rather than reported:
  // we have no basis to call its sections missing, and failing there would report the citing file for a
  // fault in the cited one.
  const anchorCache = new Map();
  const anchorsFor = (target) => {
    if (!anchorCache.has(target)) {
      let parsed = null;
      try { parsed = declaredAnchors(readFileSync(join(repo, target), 'utf8')); } catch { parsed = null; }
      anchorCache.set(target, parsed);
    }
    return anchorCache.get(target);
  };

  const checkAnchor = (file, lineNo, [, name, token], text) => {
    const target = resolveDoc(name);
    if (!target) return;
    const anchors = anchorsFor(target);
    if (!anchors) return;
    anchorsChecked++;
    const dead = unresolvedAnchor(anchors, token);
    if (dead !== null) deadAnchors.push({ file, line: lineNo, target, anchor: dead, text: text.trim() });
  };

  const checkPart = (file, lineNo, [, record, num], text) => {
    const n = Number(num);
    const claimsBacklog = record === 'TASKS.md';
    if (claimsBacklog ? openParts.has(n) : archivedParts.has(n)) return;
    const elsewhere = claimsBacklog ? archivedParts.has(n) : openParts.has(n);
    misfiled.push({
      file,
      line: lineNo,
      record,
      part: n,
      actually: elsewhere ? (claimsBacklog ? 'in the ARCHIVE' : 'still OPEN in TASKS.md') : 'in NEITHER record',
      text: text.trim(),
    });
  };

  // The Part and section halves read the two-line window (`windowHits`): a reference spans a backtick, a
  // filename and a number or `§`, so it straddles a wrap readily. `link-ok` on the next line excuses only
  // a match that straddles the join; the path and member halves read the raw line and its own token.
  const scanWindowed = (file, lines, windows, re, check) => {
    for (const h of windowHits(lines, re, { escape: 'link-ok', windows })) {
      if (!h.escaped) check(file, h.at + 1, h.match, h.straddles ? windows[h.at] : lines[h.at]);
    }
  };

  // Read once, up front: the Part half compares every reference against BOTH records, so scanning the
  // records lazily per hit would re-read them for each one. A record that is not tracked yields an empty
  // set, which makes every reference to it "nowhere" — reported, never silently passed.
  const partsIn = (f) => {
    try { return declaredParts(readFileSync(join(repo, f), 'utf8')); } catch { return new Set(); }
  };
  const openParts = partsIn('TASKS.md');
  const archivedParts = partsIn('docs/task-archive.md');

  for (const file of docs) {
    const text = readRepoText(repo, file);
    if (text === null) continue;

    // A partly-historical file is read only where it is LIVE — a prefix (CHANGELOG's unreleased half)
    // or the dated amendment regions (the design record, D164) — with historical lines BLANKED so line
    // numbers stay true: the released half of a CHANGELOG and a v0.1 seed both name paths that were right
    // on the day, which is the whole reason either is exempt at all.
    const all = text.split(/\r?\n/);
    const lines = liveLinesOnly(file, all);
    const windows = twoLineWindows(lines);

    for (const [i, line] of lines.entries()) {
      // `link-ok` is this gate's OWN token, never check-docs' `drift-ok`: a line naming a path as DATA (a
      // guard fixture's name) must not also stop being checked for retired vocabulary.
      if (line.includes('link-ok')) continue;
      for (const [, target] of line.matchAll(PATH_PATTERN)) {
        if (target.startsWith('local/')) continue;   // untracked by design
        if (onDisk.has(target)) continue;
        hits.push({ file, line: i + 1, target, text: line.trim() });
      }
      // The raw line: a citation lives inside backticks or a cref, which an author never breaks across a wrap.
      scanMembers(file, i + 1, line);
    }
    scanWindowed(file, lines, windows, PART_PATTERN, checkPart);
    scanWindowed(file, lines, windows, ANCHOR_PATTERN, checkAnchor);
  }

  // The code tiers: comment lines only (`//`, `///` and a block comment's `*`), `docs/` targets only.
  const COMMENT = /^\s*(?:\/\/|\*)/;
  for (const file of code) {
    const text = readRepoText(repo, file);
    if (text === null) continue;

    // Non-comment lines blanked, so no half reads code and a window never joins a comment to it.
    const lines = text.split(/\r?\n/).map((l) => (COMMENT.test(l) ? l : ''));
    for (const [i, line] of lines.entries()) {
      if (!line || line.includes('link-ok')) continue;
      for (const [, target] of line.matchAll(PATH_PATTERN)) {
        if (!target.startsWith('docs/')) continue;
        if (onDisk.has(target)) continue;
        hits.push({ file, line: i + 1, target, text: line.trim() });
      }
      // The section half is not narrowed to `docs/`: an anchor citation names a `.md` by construction.
      for (const match of line.matchAll(ANCHOR_PATTERN)) checkAnchor(file, i + 1, match, line);
      scanMembers(file, i + 1, line);
    }
    // The Part half wraps here as in prose, once the continuation's own comment marker is stripped.
    const windows = lines.map((l, i) => (i + 1 < lines.length
      ? `${l} ${lines[i + 1].replace(/^\s*(?:\/\/+|\*)\s*/, '')}` : l));
    scanWindowed(file, lines, windows, PART_PATTERN, checkPart);
  }

  if (hits.length === 0 && misfiled.length === 0 && deadAnchors.length === 0
    && deadMembers.length === 0) {
    // Every count is reported, so a filter that silently stopped matching one tier is visible in the
    // GREEN line rather than only in a failure that never comes.
    log(`check-links: ${docs.length} maintained doc(s) + ${code.length} code file(s) — every in-repo `
      + `reference resolves ✓ (${anchorsChecked} §-citation(s) checked)`);
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
    log('  check-docs\' annotation and must not silence this gate too. A whole record whose paths were');
    log('  right on its own day belongs in check-docs\' HISTORICAL list.');
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

  if (deadAnchors.length > 0) {
    log(`\ncheck-links: ✗ ${deadAnchors.length} citation(s) naming a SECTION that does not exist\n`);
    for (const a of deadAnchors) {
      const excerpt = a.text.length > 96 ? a.text.slice(0, 93) + '...' : a.text;
      log(`  ${a.file}:${a.line}  says ${a.target} §${a.anchor} — that document declares no such section`);
      log(`      ${excerpt}`);
    }
    log('');
    log('  A section that was RENUMBERED or FOLDED INTO another leaves every citation to it pointing at');
    log('  nothing, and no path check can see it — the file resolves, only the § does not. Repoint the');
    log('  citation at the section that now holds the text, and repoint the WORDING with it: a heading that');
    log('  used to concede something may now record it as settled. Renumbering the target instead is the');
    log('  trap — it makes existing citations resolve SILENTLY to the wrong section. If a line deliberately');
    log('  quotes a citation as it was written, put `link-ok` on it.');
  }

  if (deadMembers.length > 0) {
    log(`\ncheck-links: ✗ ${deadMembers.length} citation(s) naming a MEMBER that does not exist\n`);
    for (const m of deadMembers) {
      const excerpt = m.text.length > 96 ? m.text.slice(0, 93) + '...' : m.text;
      log(`  ${m.file}:${m.line}  says ${m.type}.${m.member} — no such identifier in any .cs file`);
      log(`      ${excerpt}`);
    }
    log('');
    log('  A member name in prose is checked by NOTHING else — it is not a path, not a Part, not a section,');
    log('  and only a `///` cref is seen by the compiler. The measured way to get one wrong is to read a');
    log('  grep that printed a method BODY and infer the name from the lines around it; read the');
    log('  DECLARATION instead. If a line deliberately names a member that never existed — a REJECTED');
    log('  alternative, which `persist-working-state.md` explicitly asks you to record — put `link-ok` on');
    log('  it, NOT `drift-ok`.');
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
