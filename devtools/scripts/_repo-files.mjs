import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// The shared halves of every scanning gate: the file list, the read rule and the two-line window. The
// leading underscore keeps this out of the gate roster. Each rule here was once copied per gate, and a
// copied scope rule diverges silently — every copy prints the same green line whatever it scanned.

/**
 * The ONE file list every full-tree gate scans: tracked files PLUS new files that are not ignored.
 *
 * `git ls-files` alone lists the INDEX, so `verify` would scan everything EXCEPT the work being verified —
 * a new file passes every gate until it is committed (`.claude/knowledge/pitfalls.md` §Environment /
 * tooling). `--others --exclude-standard` adds the new SOURCE and none of the scratch: `local/`,
 * `devtools/_*`, `bin` and `obj` are ignored and stay out on their own.
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
 * Each line joined to the next, so a claim broken by this repository's ~110-column wrap reads as one span.
 * The caller hands it PROSE — a comment's own marker is the caller's to strip (`check-docs`'
 * `commentLinesOnly`), since this knows only whitespace and blockquotes.
 *
 * The CONTINUATION is trimmed of indentation and of a leading `>` blockquote marker — either one otherwise
 * sits where a pattern expects a single space, and a wrap into a nested block or a quote goes unseen. The
 * FIRST line is never touched: `windowHits` anchors the join at its raw length.
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
