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

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const tree = process.argv.includes('--tree');

// Structural, non-secret patterns — a Windows home/dev-root absolute path is always a leak here (docs and
// code use repo-relative paths or neutral placeholders instead).
const builtins = [
  { re: /[A-Za-z]:\\Users\\[A-Za-z0-9._-]+/i, why: 'Windows user-home absolute path' },
  { re: /[A-Za-z]:\\Development\\/i, why: 'dev-machine project-root absolute path' },
];

// Private tokens (gitignored). Each non-comment line is a JS regex source.
const patterns = [...builtins];
const localFile = path.join(repo, 'local', 'sensitive-patterns.txt');
if (existsSync(localFile)) {
  for (const raw of readFileSync(localFile, 'utf8').split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    try { patterns.push({ re: new RegExp(line, 'i'), why: 'private ban pattern' }); }
    catch { console.error(`check-sensitive: bad regex in local/sensitive-patterns.txt: ${line}`); }
  }
} else {
  console.error('check-sensitive: local/sensitive-patterns.txt missing — running built-ins only.');
}

const git = (args) => execFileSync('git', args, { cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const gitBuf = (args) => execFileSync('git', args, { cwd: repo, maxBuffer: 64 * 1024 * 1024 });

// Decode a file's BYTES, honoring a UTF-16 BOM (or a no-BOM UTF-16LE heuristic) — a secret in a UTF-16
// file must not slip past a naive utf8 read whose embedded NULs get skipped as "binary". Returns the text,
// or null for genuine binary.
function decodeText(buf) {
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

// Files to scan + a getter for their raw bytes (staged blob vs on-disk).
let files, bytesOf;
if (tree) {
  files = git(['ls-files']).split('\n').filter(Boolean);
  bytesOf = (f) => readFileSync(path.join(repo, f));
} else {
  files = git(['diff', '--cached', '--name-only', '--diff-filter=ACM']).split('\n').filter(Boolean);
  bytesOf = (f) => gitBuf(['show', `:${f}`]);
}

const hits = [];
const unreadable = [];
for (const f of files) {
  let text;
  try { text = decodeText(bytesOf(f)); }
  catch (e) { unreadable.push(`${f}: ${e.message}`); continue; }  // don't silently pass an unscannable file
  if (text === null) continue; // genuine binary
  const lines = text.split('\n');
  for (let i = 0; i < lines.length; i++) {
    for (const { re, why } of patterns) {
      const m = lines[i].match(re);
      if (m) hits.push({ f, line: i + 1, why, snippet: m[0] });
    }
  }
}

// Fail closed: a file we couldn't read might hide a leak — block rather than pass silently.
if (unreadable.length > 0) {
  console.error('check-sensitive: could not scan these files (fail-closed):\n  ' + unreadable.join('\n  '));
  process.exit(1);
}

if (hits.length === 0) {
  if (tree) console.log('check-sensitive: clean — no dev-machine paths or private tokens in tracked files.');
  process.exit(0);
}

console.error('\n\x1b[31m✖ check-sensitive: blocked — private-data leak(s) detected:\x1b[0m');
for (const h of hits) console.error(`  ${h.f}:${h.line}  [${h.why}]  …${h.snippet}…`);
console.error('\nFix: use a repo-relative path / neutral placeholder, or move the value to local/.');
console.error('See .claude/rules/sensitive-info.md. (Override once with: git commit --no-verify)\n');
process.exit(1);
