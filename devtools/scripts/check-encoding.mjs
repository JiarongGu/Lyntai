// check-encoding — FAIL when a tracked text file contains MOJIBAKE (UTF-8 that was decoded as some other
// codepage and written back, corrupting every non-ASCII character in it) or a C0 CONTROL character (an
// escape a tool wrote as its raw byte).
//
// WHY THIS IS A GATE AND NOT A RULE. `windows-machine.md` and `repo-mechanics.md` both already say never to
// round-trip a source file through PowerShell 5's Set-Content, and the rule was still broken THREE TIMES in
// a single session on 2026-08-13 — each time under time pressure, each time caught only because a human or
// a diff happened to look. A rule that is known, written down, and still violated is not a knowledge
// problem; it is a missing gate. This repository's own position is that packaging rules are "gated, not
// remembered", and encoding deserves the same treatment.
//
// WHAT MAKES IT DANGEROUS is that it does not fail anything. Mangled CJK and em-dashes compile, pass every
// test, and ship — the file is still valid UTF-8, just wrong. The damage is silent and permanent once
// committed, exactly like a leaked path.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { repoSources } from './_repo-files.mjs';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

/// Extensions worth scanning: text this repository authors. A binary or a generated artifact is excluded
/// because a false positive in one is noise, and neither is written by the tools that cause this.
export const SCANNED = /\.(cs|md|mjs|js|json|csproj|slnx|props|targets|sql|html|yml|yaml|txt)$/i;

/// Paths where a mojibake-looking sequence is legitimate content rather than damage.
///
/// Deliberately SHORT, and shorter than it first was: writing the patterns as code points rather than
/// literals means neither this file nor its test contains the sequences it hunts for, so neither needs
/// excluding. An exclusion is a hole in a guard, so the fix was to stop needing one.
export const EXCLUDED = [];

/**
 * Sequences that only ever appear when UTF-8 was decoded as something else and re-encoded.
 *
 * Each entry is a REAL corruption observed in this repository, not a theoretical one — the GBK column is
 * what this machine's console produces, and it is what actually landed in `PostgresStorageTests.cs`,
 * `MemorySalienceInversionTests.cs` and `LlmSemanticRecallLiveTests.cs` on 2026-08-13.
 *
 * The replacement character is listed first because it is unambiguous: nothing legitimately authored here
 * contains U+FFFD, and its presence means a decode already gave up.
 */
export const MOJIBAKE = [
  // DERIVED, not remembered: each is `new TextDecoder(enc).decode(Buffer.from(ch, 'utf8'))` for the
  // character named. Written from memory they were wrong three times, which is the same class of mistake
  // this guard exists to catch — so they are listed by CODE POINT and the test re-derives them.
  { codes: [0xFFFD], why: 'U+FFFD replacement character — a decode already failed and lost the original byte' },
  { codes: [0x9225], why: 'em-dash (U+2014) read as GBK — the PowerShell 5 Set-Content signature' },
  { codes: [0x8133], why: 'multiplication sign (U+00D7) read as GBK' },
  { codes: [0x6402], why: 'section sign (U+00A7) read as GBK' },
  { codes: [0x6D93], why: 'CJK read as GBK' },
  { codes: [0x00E2, 0x20AC, 0x201D], why: 'em-dash read as CP1252 — the same damage from a different console' },
  { codes: [0x00C3, 0x2014], why: 'multiplication sign read as CP1252' },
  { codes: [0x00E4, 0x00B8, 0x00AD], why: 'CJK read as CP1252' },
].map((m) => ({ ...m, pattern: String.fromCodePoint(...m.codes) }));

/**
 * A C0 control character other than TAB, LF and CR. Authored text never holds one; a `\b` written through a
 * tool that interprets escapes lands as U+0008 and the sentence simply loses its subject — which is how
 * `pitfalls.md`'s own entry about that trap came to be missing the `\b` it was about.
 */
export const CONTROL = /[\x00-\x08\x0b\x0c\x0e-\x1f]/g;

/**
 * @param {string[] | null} files a caller-chosen list, read from disk; `null` = the gate's own source —
 *   the full tree, or with `staged` the index (the pre-commit hook's mode: what the commit will record).
 */
export function checkEncoding(repo, log = console.log, files = null, { staged = false } = {}) {
  const { files: source, bytesOf } = files === null
    ? repoSources(repo, { staged })
    : { files, bytesOf: (f) => fs.readFileSync(path.join(repo, f)) };
  const candidates = source
    .filter((f) => SCANNED.test(f))
    .filter((f) => !EXCLUDED.includes(f.replace(/\\/g, '/')));

  // Fail-closed on the tree path: an empty listing is broken whoever supplied it, and zero candidates from a
  // full tree means the text filter rejected everything. A staged run may be empty (a deletion-only commit),
  // and a caller's list may hold only binaries.
  if (!staged && (source.length === 0 || (files === null && candidates.length === 0))) {
    log('check-encoding: ✗ found no tracked text files to scan');
    log('  Nothing was scanned, so this gate proves nothing — check the repo root and the file listing.');
    return 1;
  }

  const hits = [];
  const unreadable = [];
  for (const file of candidates) {
    let text;
    try {
      text = bytesOf(file).toString('utf8');
    } catch (e) {
      // ENOENT is a pending deletion — nothing left to certify. Anything ELSE is a file this guard cannot
      // prove clean, and a silent skip there is a false PASS.
      if (e?.code === 'ENOENT') continue;
      unreadable.push(`${file}: ${e.message}`);
      continue;
    }

    const lines = text.split('\n');
    for (const { pattern, why } of MOJIBAKE) {
      for (let i = 0; i < lines.length; i++) {
        if (lines[i].includes(pattern)) hits.push({ file, line: i + 1, why });
      }
    }
    for (let i = 0; i < lines.length; i++) {
      for (const [ch] of lines[i].matchAll(CONTROL)) {
        const code = ch.charCodeAt(0).toString(16).toUpperCase().padStart(4, '0');
        hits.push({ file, line: i + 1, why: `control character U+${code} — an escape written as its raw byte` });
      }
    }
  }

  if (unreadable.length > 0) {
    log(`check-encoding: ✗ ${unreadable.length} tracked text file(s) could not be READ, so this gate cannot certify them`);
    for (const u of unreadable) log(`  ${u}`);
  }

  if (hits.length === 0) {
    if (unreadable.length > 0) return 1;
    log(`check-encoding: ${candidates.length} ${staged ? 'staged' : 'tracked'} text file(s) free of `
      + 'mojibake and control characters ✓');
    return 0;
  }

  log(`check-encoding: ✗ ${hits.length} damaged sequence(s) in tracked text`);
  for (const h of hits.slice(0, 20)) log(`  ${h.file}:${h.line} — ${h.why}`);
  if (hits.length > 20) log(`  …and ${hits.length - 20} more`);
  log('');
  log('  Mojibake is UTF-8 decoded as another codepage and written back; a control character is an escape');
  log('  (`\\b`, `\\x1b`) a tool wrote as its raw byte. Both compile, pass every test and ship, so nothing');
  log('  else will catch them.');
  log('');
  log('  Almost always: a file was round-tripped through PowerShell 5 (Get-Content/Set-Content), or content');
  log('  was echoed through a shell or console. Recover with `git checkout -- <file>` and redo the edit');
  log('  with the file-writing tools, never through the shell. See `.claude/rules/windows-machine.md`.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing. `--staged` is the
// pre-commit hook's mode.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = checkEncoding(repo, console.log, null, { staged: process.argv.includes('--staged') });
}
