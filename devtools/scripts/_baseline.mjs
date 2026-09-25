import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Check a RUN-derived number `CLAUDE.md` quotes as a baseline — `doc samples N/N`, `guard-script tests N/N`
 * — against the run that produced it. The producing gate already holds the number, where a static counter
 * would have to reimplement its filtering (`docs/GATES.md` §Which numbers a gate holds).
 *
 * Both numbers are compared, and an ABSENT claim FAILS: a check that passes when its sentence is reworded
 * away disarms silently, which is the one thing a baseline check must not do.
 *
 * @param {string} repo
 * @param {{ gate: string, label: string, passed: number, total: number }} run the gate's name, the claim's
 *   label as written before `N/N`, and this run's two numbers
 * @param {(s: string) => void} log
 * @returns {number} 0 when the claim matches, 1 otherwise
 */
export function checkQuotedBaseline(repo, { gate, label, passed, total }, log = console.log) {
  const file = join(repo, 'CLAUDE.md');
  const text = existsSync(file) ? readFileSync(file, 'utf8') : '';
  const m = new RegExp(`\\b${label}\\s+(\\d+)\\s*/\\s*(\\d+)`, 'i').exec(text);
  if (!m) {
    log(`${gate}: ✗ CLAUDE.md no longer quotes "${label} N/N" — the baseline this run checks is gone`);
    log('  Restore the sentence; move it VERBATIM when rewording, or this check is disarmed.');
    return 1;
  }
  if (Number(m[1]) === passed && Number(m[2]) === total) return 0;

  log(`${gate}: ✗ CLAUDE.md says "${label} ${m[1]}/${m[2]}" — this run produced ${passed}/${total}`);
  log('');
  log('  That line is a BASELINE a reader is told to compare against, so a stale number teaches them to');
  log('  stop comparing. Update it to the numbers above, which are what the tree actually produces.');
  return 1;
}
