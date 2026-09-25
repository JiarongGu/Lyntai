import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The ONE file list every full-tree gate scans: tracked files PLUS new files that are not ignored.
 *
 * The leading underscore keeps this out of the gate roster the way `_fixtures.mjs` keeps its helper out of
 * test discovery — `devtools/scripts/` is a list of executable gates, and this is not one.
 *
 * ## Why untracked files are included
 *
 * `git ls-files` lists the INDEX. The ordinary workflow is *write → `verify` → commit*, so a file that is
 * neither committed nor `git add`ed is absent from every gate's scope — meaning `verify` scans everything
 * EXCEPT the work being verified, and prints the same green line it prints when there is genuinely nothing
 * wrong. Measured 2026-08-23: a 36-line comment block in a new bench file passed two full `verify` runs and
 * the individual gate twice, then failed the moment its file was committed, byte-identical. The first
 * hypothesis was a non-deterministic gate, which would have been far worse than the truth.
 *
 * `--others --exclude-standard` adds exactly the new SOURCE and none of the scratch: `local/`,
 * `devtools/_*`, `bin` and `obj` are ignored and stay out on their own. That is what made index-only look
 * sufficient — the two reasons pull in opposite directions and git already tells them apart.
 *
 * ## Why it is shared
 *
 * Five gates carried their own copy of this rule and the blind spot was five-fold, because a scope rule
 * diverges silently: every copy prints the same green line whatever it scanned. Same shape as the `salience`
 * coercion `pitfalls.md` records, applied to gate SCOPE.
 *
 * @param {string} repo Repository root.
 * @param {string[]} tiers Optional pathspecs to narrow to (e.g. `['src', 'tests']`); empty = whole repo.
 */
export const repoFiles = (repo, tiers = []) => {
  // -z on both calls: git C-QUOTES any path with a non-ASCII byte, the read then fails, and a
  // silently-skipped file reports exactly like a clean one. This repository is named 灵台, so a CJK-named
  // document is not hypothetical.
  const list = (extra) =>
    execFileSync('git', ['ls-files', '-z', ...extra, ...tiers], { cwd: repo, encoding: 'utf8' })
      .split('\0')
      .filter(Boolean);

  return [...new Set([...list([]), ...list(['--others', '--exclude-standard'])])];
};

/**
 * What a scanning guard reads: a file list and a reader for each file's BYTES. The full tree
 * (`repoFiles`, working-tree bytes) or, `staged`, the INDEX — what a commit is about to record, read with
 * `git show :<path>`, because a pre-commit guard that reads the working tree passes a damaged blob that was
 * repaired on disk without being re-staged, and blocks on damage the commit does not touch.
 *
 * `R` in the staged filter is load-bearing: rename detection is on by default, so `git mv` plus an edit
 * stages as status R, and an `ACM` filter returns an EMPTY list. `D` has no staged blob to scan. `-z` is
 * load-bearing for `repoFiles`' reason: a C-quoted non-ASCII name matches no blob.
 */
export function repoSources(repo, { staged = false } = {}) {
  if (!staged) return { files: repoFiles(repo), bytesOf: (f) => readFileSync(join(repo, f)) };
  const git = (args, encoding) => execFileSync('git', args, { cwd: repo, encoding, maxBuffer: 64 * 1024 * 1024 });
  return {
    files: git(['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z'], 'utf8').split('\0').filter(Boolean),
    bytesOf: (f) => git(['show', `:${f}`], 'buffer'),
  };
}

/**
 * A listed file's text, or `null` when it is gone from the working tree — a pending deletion, with nothing
 * left to certify. Any OTHER read error THROWS: a file a gate cannot read is one it cannot prove clean, and
 * skipping it while counting it in a green total is a false PASS.
 */
export function readRepoText(repo, f) {
  try {
    return readFileSync(join(repo, f), 'utf8');
  } catch (e) {
    if (e?.code === 'ENOENT') return null;
    throw new Error(`${f}: could not be read (${e?.code ?? e?.message}), so no gate can prove it clean`);
  }
}

/**
 * The two-line window three gates share: `check-counts` and `check-docs` join a claim across a wrap this
 * way, and `check-links` joins its own Part-reference check the same way. A claim broken by this
 * repository's ~110-column wrap reads as one continuous span — provided the caller hands it PROSE; a
 * comment line's own marker is the caller's job to strip first (`check-docs`' `commentLinesOnly` does this
 * for its code tier), because this function only knows about whitespace.
 *
 * The CONTINUATION's leading indentation is stripped before the join; the first line is never touched. That
 * asymmetry is load-bearing, not a style choice: `check-counts`'s duplicate-report guard
 * (`m.index >= line.length + 1`) and `check-links`' own (`match.index > line.length`) anchor the join
 * boundary at the same place — the raw, untrimmed first line's own length — so trimming the continuation
 * leaves it exactly where each guard expects it, while trimming the first line's tail would move it and
 * mis-fire both silently. Without the trim at all, an indented continuation (a wrap into a nested or
 * bulleted block) joins as `"…proved by" + " " + "      seven"`, and a pattern anchored on single-space
 * adjacency never crosses the extra whitespace — the claim is invisible.
 *
 * A BLOCKQUOTE marker on the continuation is stripped for the same reason and was missed for the same
 * reason. Found 2026-09-12: a retired claim wrapping inside a `>` block joined as `"…never a" + " " +
 * "> generator asked to choose"`, and the `>` sat exactly where the pattern expected a space — so the one
 * place this repository puts its standing working positions was the one place the gate could not read a
 * wrapped claim. It is the same defect the indentation trim already fixed, wearing markdown's syntax
 * instead of whitespace; `check-docs`' `commentLinesOnly` is the third instance, for `//`.
 *
 * @param {string[]} lines
 * @returns {string[]} one window per input line: `lines[i]` joined to `lines[i + 1]`'s trimmed text, or
 *   `lines[i]` unchanged for the last line.
 */
export const twoLineWindows = (lines) =>
  lines.map((line, i) => (i + 1 < lines.length
    ? `${line} ${lines[i + 1].replace(/^\s+/, '').replace(/^(?:>\s*)+/, '')}`
    : line));

/**
 * Every match of `re` in a file, each reported ONCE at the line it begins on — the rule every prose gate
 * shares, so no gate can get one half of it wrong alone.
 *
 * A match is read from the line alone, or from the two-line window when it straddles the join (a match
 * that extends past the line, such as a range whose second end wrapped, is taken whole). A window match
 * lying wholly on the NEXT line is dropped: that line reports it. A match the line alone can see is
 * `escaped` only by that line's own `escape` token — even when the window extends it — and one only the
 * window can see by the token on either line. Escaped hits are returned flagged, so a caller that counts
 * matches still counts them.
 *
 * @param {string[]} lines prose, line numbers preserved (blank a line to take it out of scope)
 * @param {RegExp} re the pattern; a non-global one is made global
 * @param {{ escape?: string | null, windows?: string[] }} options `windows` defaults to `twoLineWindows`
 * @returns {{ at: number, match: RegExpMatchArray, straddles: boolean, escaped: boolean }[]} `at` is the
 *   0-based line index.
 */
export function windowHits(lines, re, { escape = null, windows = twoLineWindows(lines) } = {}) {
  const g = re.global ? re : new RegExp(re.source, `${re.flags}g`);
  const hits = [];
  for (let at = 0; at < lines.length; at++) {
    const line = lines[at];
    const byStart = new Map();
    for (const match of line.matchAll(g)) byStart.set(match.index, { match, straddles: false, alone: true });
    if (at + 1 < lines.length) {
      for (const match of windows[at].matchAll(g)) {
        if (match.index > line.length) continue;
        const alone = byStart.has(match.index);
        if (alone && match.index + match[0].length <= line.length) continue;
        byStart.set(match.index, { match, straddles: true, alone });
      }
    }
    const selfOk = escape !== null && line.includes(escape);
    const nextOk = escape !== null && (lines[at + 1] ?? '').includes(escape);
    for (const [, { match, straddles, alone }] of [...byStart].sort((a, b) => a[0] - b[0]))
      hits.push({ at, match, straddles, escaped: selfOk || (!alone && nextOk) });
  }
  return hits;
}
