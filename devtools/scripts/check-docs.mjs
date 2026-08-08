// check-docs — fail when a doc uses vocabulary a decision retired.
//
// The gap this closes: the code is gated from every side (check-warnings, the API-surface baselines, the
// storage contracts) and the prose is gated from none. A spec paragraph that quietly stops being true
// survives every check, and the next session reads it and implements the wrong thing.
//
// The registry is `retiredTerms` in devtools/project.config.mjs — a term, what to say instead, and why.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const config = (await import('../project.config.mjs')).default;

/**
 * Files whose whole job is to be a record of their own day, so retired vocabulary is CORRECT in them.
 *
 * Plans are in here and SPECS are deliberately not. `CLAUDE.md` calls both "point-in-time designs and
 * plans, not maintained state" — but only one of them gets read as the contract. A plan describes what was
 * done on a day and is finished; a spec describes how the thing WORKS and is what the next session builds
 * against, so it has to keep being true.
 */
const HISTORICAL = [
  /^CHANGELOG\.md$/,
  /^docs\/task-archive\.md$/,
  /^docs\/superpowers\/plans\//,
];

/** A file that declares itself superseded in its opening banner is a record too — same reasoning. */
const SUPERSEDED_BANNER = /^(?:.*\n){0,20}?.*\bSUPERSEDED\b/;

/** Only prose is checked; see the note on `retiredTerms` for why `src/` is deliberately excluded. */
const IN_SCOPE = (path) =>
  path === 'README.md' || path.startsWith('docs/') || path.startsWith('.claude/');

function checkDocs(repo, config, log = console.log) {
  const rules = config.retiredTerms ?? [];
  if (rules.length === 0) {
    log('check-docs: no retired terms configured — nothing to check.');
    return 0;
  }

  const tracked = execFileSync('git', ['ls-files'], { cwd: repo, encoding: 'utf8' })
    .split('\n')
    .map((f) => f.trim())
    // .html too: the published design record is a tracked page, and an untracked one drifted three times
    .filter((f) => f.endsWith('.md') || f.endsWith('.html'))
    .filter(IN_SCOPE)
    .filter((f) => !HISTORICAL.some((re) => re.test(f)));

  const hits = [];
  let skipped = 0;

  for (const file of tracked) {
    let text;
    try { text = readFileSync(join(repo, file), 'utf8'); } catch { continue; }

    if (SUPERSEDED_BANNER.test(text)) { skipped++; continue; }

    const lines = text.split(/\r?\n/);
    for (const rule of rules) {
      const re = new RegExp(rule.term, 'g');
      lines.forEach((line, i) => {
        // `drift-ok` is the honest annotation for a passage that deliberately NAMES the retired thing
        if (line.includes('drift-ok')) return;
        re.lastIndex = 0;
        if (re.test(line)) hits.push({ file, line: i + 1, rule, text: line.trim() });
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

process.exitCode = checkDocs(repo, config);
