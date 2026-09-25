// check-tautology — fail when prose contrasts an identifier with ITSELF.
//
// A rename that unifies two seams rewrites BOTH sides of a sentence that contrasted them, and the sentence
// then says nothing. No other gate can see it: the retired name is gone, so `check-docs` is satisfied; the
// survivor resolves, so `check-links` is; prose was never on a baseline, so `check-api-vocabulary` never
// looked. What it cost, the measurement that licensed building it, and why it scans WIDER than
// `check-docs` are all in `docs/GATES.md` §check-tautology.
//
// Escape token: `tautology-ok`, this gate's own and no other's.
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { CODE_IN_SCOPE, commentLinesOnly } from './check-docs.mjs';
import { readRepoText, repoFiles, twoLineWindows, windowHits } from './_repo-files.mjs';

const here = fileURLToPath(import.meta.url);
const repo = join(dirname(here), '..', '..');

/**
 * The shapes a collapsed contrast takes. Each captures an identifier and requires the SAME one back
 * through `\1`, so nothing fires on two different names.
 *
 * Every pattern is a BACKREFERENCE rather than a literal, which is how this file avoids containing what it
 * hunts — the construction `check-encoding` uses for its own patterns, available here because a
 * self-referential regex needs no example to be readable. `devtools/` is out of scope below for the same
 * reason it is out of `check-docs`' scope, so the test's fixtures cannot reach the scan either.
 */
export const COLLAPSED = [
  // The common form: two backticked identifiers joined by a conjunction. Four joiners, because a
  // contrast is written every one of these ways in this repository and a rule must match the CLAIM in
  // every order it is writable — the lesson `check-counts`' own `verify`-gates entry records.
  /`([A-Za-z_][A-Za-z0-9_.<>]{3,})`\s+(?:and|or|vs\.?|versus)\s+`\1`/g,
  // A slash pair, which is how a two-member family is usually named in a heading or a table cell.
  /`([A-Za-z_][A-Za-z0-9_.<>]{3,})`\s*\/\s*`\1`/g,
  // A parenthesised pair — how a comment enumerates the members a registration serves.
  /\(([A-Z][A-Za-z0-9_]{4,}),\s*\1\)/g,
  // Unbackticked. Zero live hits, which is the CORRECT state rather than a dead rule: this repository
  // backticks its identifiers, so this covers the day someone does not. An initial capital and five
  // characters keep it off English's own repetitions ("over and over", "more and more"), which are
  // lowercase and short. Pinned in both directions by the test.
  /\b([A-Z][A-Za-z0-9_]{4,})\s+and\s+\1\b/g,
  // THE RENAME SHAPE, added 2026-09-17 — the one a rename sweep is most likely to collapse, and the one
  // this gate's first four patterns all missed. A rename entry is old-then-new by construction, so a sweep
  // that rewrites every occurrence of the old name rewrites BOTH sides and leaves a sentence saying a thing
  // was renamed to itself. `CHANGELOG.md`'s Breaking section is written almost entirely in this shape, which
  // is where it was found: a live entry read "`X` is renamed `X`" for two days, in the release-facing log.
  // Measured before adding: 1 true hit against 0 false, over 811 prose and code files.
  /`([A-Za-z_][A-Za-z0-9_.<>]{3,})`\s+(?:is\s+renamed|renamed\s+to|becomes|replaces|is\s+replaced\s+by)\s+`\1`/g,
  // The same claim written as an arrow, which is how a rename TABLE writes it.
  /`([A-Za-z_][A-Za-z0-9_.<>]{3,})`\s*(?:→|->|=>)\s*`\1`/g,
];

/**
 * Prose only, with line numbers preserved: a markdown file whole, a code file reduced to its comment text by
 * `check-docs`' own `commentLinesOnly` over `check-docs`' own `CODE_IN_SCOPE` — one answer to "which code
 * is prose", never a copy of it. A repeated identifier in a string literal or an expression is data the
 * program uses (`Compare(scores, scores)` is correct code).
 *
 * A `<see cref>` line is dropped rather than scanned: a cref names an OVERLOAD by its parameter types, so
 * `Math.Max(double,double)` is a SIGNATURE and matches the parenthesised rule above.
 */
export const proseOf = (file, text) => {
  const lines = text.split(/\r?\n/);
  if (file.endsWith('.md')) return lines;
  return commentLinesOnly(lines).map((l) => (l.includes('cref=') ? '' : l));
};

export const IN_SCOPE = (path) => path.endsWith('.md') || CODE_IN_SCOPE(path);

export { CODE_IN_SCOPE };

/**
 * `files` is the raw candidate list (a `git ls-files` shape), filtered here — so a test that injects one
 * still exercises the scope filter rather than bypassing it.
 */
export function checkTautology(repo, log = console.log, files = null) {
  const source = files ?? repoFiles(repo);
  const scanned = source.filter(IN_SCOPE);

  // Fail-closed, the rule `check-docs` and `check-api-vocabulary` already carry: a gate that scanned
  // nothing must never print a tick. A full-tree run has README/CLAUDE/TASKS at minimum, so zero
  // survivors there means the scope filter rejected everything; a caller-supplied list may legitimately
  // be empty (a commit touching only `devtools/`), so that half is checked on the tree path alone.
  if (source.length === 0 || (files === null && scanned.length === 0)) {
    log('check-tautology: ✗ found no prose to scan');
    log('  Nothing was scanned, so this gate proves nothing — check IN_SCOPE and the repo root.');
    return 1;
  }

  const hits = [];
  for (const file of scanned) {
    const text = readRepoText(repo, file);
    if (text === null) continue;

    const lines = proseOf(file, text);
    const windows = twoLineWindows(lines);

    // `tautology-ok` is this gate's OWN escape; `windowHits` reports each hit once, at its own line.
    for (const pattern of COLLAPSED) {
      const reported = new Set();
      for (const h of windowHits(lines, pattern, { escape: 'tautology-ok', windows })) {
        if (h.escaped || reported.has(h.at)) continue;
        reported.add(h.at);
        hits.push({ file, line: h.at + 1, text: (h.straddles ? windows[h.at] : lines[h.at]).trim() });
      }
    }
  }

  if (hits.length === 0) {
    log(`check-tautology: ${scanned.length} prose file(s) — no identifier contrasted with itself ✓`);
    return 0;
  }

  log(`check-tautology: ✗ ${hits.length} sentence(s) contrast an identifier with ITSELF\n`);
  for (const hit of hits) {
    const excerpt = hit.text.length > 110 ? `${hit.text.slice(0, 107)}...` : hit.text;
    log(`  ${hit.file}:${hit.line}  ${excerpt}`);
  }
  log('');
  log('  This is what a rename leaves behind when it replaces BOTH sides of a contrast. Find what the');
  log('  other side used to be — `retiredApiNames` / `retiredTerms` in devtools/project.config.mjs name');
  log('  the renames — and restore the distinction, or reword so the sentence no longer needs it.');
  log('  A passage that deliberately quotes the defect takes `tautology-ok` on its line.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  process.exitCode = checkTautology(repo);
}
