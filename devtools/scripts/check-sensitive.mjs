#!/usr/bin/env node
// Sensitive-info guard — blocks committing dev-machine absolute paths or private tokens into this repo.
// Lyntai is a public-shaped library; keep dev-root paths and any private values out of tracked files.
// Runs from the pre-commit hook (devtools/hooks/pre-commit); also runnable by hand.
//
//   node devtools/scripts/check-sensitive.mjs          # scan STAGED changes (what pre-commit does)
//   node devtools/scripts/check-sensitive.mjs --tree   # scan every tracked file
//
// The tracked patterns here are STRUCTURAL only (generic path shapes) — safe to publish. Any real private
// tokens live in the gitignored local/sensitive-patterns.txt, loaded at runtime. Absent that file, the
// built-ins still run and a notice is printed. Exit 1 (blocks the commit) on any match, 0 when clean.
//
// Everything below the CLI line is exported and pure-ish (it takes its file list and its byte reader), so
// devtools/scripts/__tests__/check-sensitive.test.mjs can prove each pattern FIRES. A guard whose failure
// mode is a false PASS cannot be validated by running it — see TASKS.md Part 60.

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

// Structural, non-secret patterns — a Windows home/dev-root absolute path is always a leak here (docs and
// code use repo-relative paths or neutral placeholders instead).
export const builtins = [
  { re: /[A-Za-z]:\\Users\\[A-Za-z0-9._-]+/i, why: 'Windows user-home absolute path' },
  { re: /[A-Za-z]:\\Development\\/i, why: 'dev-machine project-root absolute path' },
];

/**
 * Built-ins plus the private tokens in the gitignored local file (each non-comment line a JS regex source).
 *
 * Returns the notices rather than printing them: the CLI reports, the function decides nothing about output.
 */
export function loadPatterns(localFile) {
  const patterns = [...builtins];
  const badLines = [];
  const localFileMissing = !existsSync(localFile);
  if (!localFileMissing) {
    for (const raw of readFileSync(localFile, 'utf8').split(/\r?\n/)) {
      const line = raw.trim();
      if (!line || line.startsWith('#')) continue;
      try { patterns.push({ re: new RegExp(line, 'i'), why: 'private ban pattern' }); }
      catch { badLines.push(line); }
    }
  }
  return { patterns, localFileMissing, badLines };
}

// Decode a file's BYTES, honoring a UTF-16 BOM (or a no-BOM UTF-16LE heuristic) — a secret in a UTF-16
// file must not slip past a naive utf8 read whose embedded NULs get skipped as "binary". Returns the text,
// or null for genuine binary.
export function decodeText(buf) {
  if (buf.length >= 2 && buf[0] === 0xff && buf[1] === 0xfe) return buf.toString('utf16le');
  if (buf.length >= 2 && buf[0] === 0xfe && buf[1] === 0xff) {
    const s = Buffer.from(buf);                              // UTF-16BE → swap to LE
    for (let i = 0; i + 1 < s.length; i += 2) { const t = s[i]; s[i] = s[i + 1]; s[i + 1] = t; }
    return s.toString('utf16le');
  }
  let odd = 0;
  const sample = Math.min(buf.length, 4096);
  for (let i = 1; i < sample; i += 2) if (buf[i] === 0) odd++;
  if (sample >= 8 && odd > sample / 8) return buf.toString('utf16le');
  const text = buf.toString('utf8');
  return text.includes('\0') ? null : text;                 // still NUL after utf8 → true binary
}

const git = (repoRoot, args) =>
  execFileSync('git', args, { cwd: repoRoot, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const gitBuf = (repoRoot, args) => execFileSync('git', args, { cwd: repoRoot, maxBuffer: 64 * 1024 * 1024 });

/**
 * Files to scan + a getter for their raw bytes (staged blob vs on-disk).
 *
 * `-z` (NUL-separated) is load-bearing, not tidiness. Without it git C-QUOTES any path containing a
 * non-ASCII byte — `docs/灵台.md` arrives as `"docs/\347\201\265\345\217\260.md"` — and that name matches no  link-ok
 * file on disk. In `--tree` mode the read then ENOENTs and lands in the "pending deletion" branch below, so
 * the file is SILENTLY SKIPPED and a leak inside it is never seen; in staged mode `git show :<quoted>` fails
 * and it is (correctly, but confusingly) fail-closed. Measured 2026-08-11 while writing this gate's tests —
 * a false PASS in the highest-stakes guard, in a repository whose own name is CJK.
 *
 * `R` in the staged filter is load-bearing for the same reason. Rename detection is ON by default, so
 * `git mv` plus an edit stages as status R — and an `ACM` filter drops it, returning an EMPTY file list, so
 * the pre-commit hook exits 0 having printed nothing. This repository's own procedures are built on `git mv`
 * (archiving a document, moving a record into `local/`), and `sensitive-info.md` is explicit that a committed
 * leak is a HISTORY problem: `--tree` would catch it only once it is already in history. Found 2026-08-14 by
 * the whole-codebase review. `D` stays excluded deliberately — a deletion has no staged blob to scan.
 */
export function sources(repoRoot, { tree = false } = {}) {
  if (tree) {
    return {
      files: git(repoRoot, ['ls-files', '-z']).split('\0').filter(Boolean),
      bytesOf: (f) => readFileSync(path.join(repoRoot, f)),
    };
  }
  return {
    files: git(repoRoot, ['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z'])
      .split('\0').filter(Boolean),
    bytesOf: (f) => gitBuf(repoRoot, ['show', `:${f}`]),
  };
}

/** The scan itself: every line of every file against every pattern. */
export function scanFiles(files, bytesOf, patterns) {
  const hits = [];
  const unreadable = [];
  const goneFromWorkingTree = [];
  for (const f of files) {
    let text;
    try { text = decodeText(bytesOf(f)); }
    catch (e) {
      // A tracked file that is GONE from the working tree is a pending deletion (a rename or refactor whose
      // removal isn't staged yet) — there is no content left to leak, so it is skipped, not fail-closed. Only
      // a file we cannot READ blocks: that could be hiding something. (Distinguishing the two matters — the
      // old behaviour blocked `verify` mid-refactor with what looked like a leak report.)
      if (e?.code === 'ENOENT') { goneFromWorkingTree.push(f); continue; }
      unreadable.push(`${f}: ${e.message}`);                          // don't silently pass an unscannable file
      continue;
    }
    if (text === null) continue; // genuine binary
    const lines = text.split('\n');
    for (let i = 0; i < lines.length; i++) {
      for (const { re, why } of patterns) {
        const m = lines[i].match(re);
        if (m) hits.push({ f, line: i + 1, why, snippet: m[0] });
      }
    }
  }
  return { hits, unreadable, goneFromWorkingTree };
}

/** The whole gate, reporting through the given sinks. Returns the process exit code. */
export function checkSensitive({ repo: repoRoot = repo, tree = false, log = console.log, err = console.error } = {}) {
  const { patterns, localFileMissing, badLines } =
    loadPatterns(path.join(repoRoot, 'local', 'sensitive-patterns.txt'));
  for (const line of badLines) err(`check-sensitive: bad regex in local/sensitive-patterns.txt: ${line}`);
  if (localFileMissing) err('check-sensitive: local/sensitive-patterns.txt missing — running built-ins only.');

  const { files, bytesOf } = sources(repoRoot, { tree });
  const { hits, unreadable, goneFromWorkingTree } = scanFiles(files, bytesOf, patterns);

  // Fail closed: a file we couldn't read might hide a leak — block rather than pass silently.
  if (unreadable.length > 0) {
    err('check-sensitive: could not scan these files (fail-closed):\n  ' + unreadable.join('\n  '));
    return 1;
  }

  // Reported, not silent: a scan that skipped files should say so, so "clean" is never mistaken for "complete".
  if (goneFromWorkingTree.length > 0) {
    log(`check-sensitive: ${goneFromWorkingTree.length} tracked file(s) deleted in the working tree ` +
      `(deletion not staged yet) — nothing to scan:\n  ${goneFromWorkingTree.join('\n  ')}`);
  }

  if (hits.length === 0) {
    if (tree) log('check-sensitive: clean — no dev-machine paths or private tokens in tracked files.');
    return 0;
  }

  err('\n\x1b[31m✖ check-sensitive: blocked — private-data leak(s) detected:\x1b[0m');
  for (const h of hits) err(`  ${h.f}:${h.line}  [${h.why}]  …${h.snippet}…`);
  err('\nFix: use a repo-relative path / neutral placeholder, or move the value to local/.');
  err('See .claude/rules/sensitive-info.md. (Override once with: git commit --no-verify)\n');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test scans nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2): the argv fallback compares resolved paths, and any way that
// comparison can be wrong makes this guard silently do NOTHING and exit 0. Pinned by cli-entry.test.mjs.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = checkSensitive({ repo, tree: process.argv.includes('--tree') });
}
