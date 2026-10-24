// bench-compare — two runs of a memory bench, compared PAIRED.
//
// Every bench wired to `BenchResults` writes `devtools/_bench-results/<bench>-<stamp>.jsonl`, one row per
// arm × question: `bench`, `metric`, `arm`, `question`, `hit` (true / false / null = not decidable for
// this arm), optional `score` and `latencyMs`, `configuration` (a hash of the run's arguments and its
// `LYNTAI_*` environment), `run`, and `commit` when git answered. Both runs answered the same questions, so
// the comparison reads only the ones they DISAGREE on — McNemar exact, the same arithmetic as
// `BenchStats.McNemarExact`, and a test pins the two to the same published figures.
//
// It REFUSES rather than truncates: a different metric always; a different question set or configuration
// hash unless told, because each produces a believable number from two things that do not pair.
//
// Usage:
//   node devtools/dev.mjs bench-compare <a.jsonl> <b.jsonl> [--allow-question-mismatch] [--allow-config-mismatch]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);

/** `BenchStats.McNemarExact`, term for term: the two-sided exact binomial tail over the discordant pairs. */
export function mcnemarExact(onlyFirst, onlySecond) {
  const n = onlyFirst + onlySecond;
  if (n === 0) return 1;
  const k = Math.min(onlyFirst, onlySecond);
  let term = 0.5 ** n;
  let tail = term;
  for (let i = 0; i < k; i++) {
    term *= (n - i) / (i + 1);
    tail += term;
  }
  return Math.min(1, 2 * tail);
}

/** `BenchStats.Format`: a p-value said the way a reader can act on, never "p = 0.000". */
export function formatP(p) {
  if (p < 0.0001) return 'p<0.0001';
  if (p < 0.001) return 'p<0.001';
  if (p < 0.01) return `p=${p.toFixed(4)}`;
  return `p=${p.toFixed(3)}`;
}

/** One results file's rows. Throws naming `label:line` on a malformed row or a duplicate (arm, question). */
export function parseResults(text, label) {
  const rows = [];
  const seen = new Set();
  String(text).split(/\r?\n/).forEach((line, i) => {
    if (!line.trim()) return;
    const at = `${label}:${i + 1}`;
    let r;
    try { r = JSON.parse(line); } catch { throw new Error(`${at}: not JSON`); }
    for (const field of ['arm', 'question'])
      if (typeof r?.[field] !== 'string' || !r[field]) throw new Error(`${at}: no ${field}`);
    if (r.hit !== null && typeof r.hit !== 'boolean') throw new Error(`${at}: hit must be true, false or null`);
    const key = `${r.arm}\u001f${r.question}`;
    if (seen.has(key)) throw new Error(`${at}: duplicate (arm, question) ${r.arm} / ${r.question}`);
    seen.add(key);
    rows.push(r);
  });
  return rows;
}

const distinct = (rows, field) => [...new Set(rows.map((r) => r[field] ?? ''))].sort().join(',');
const byArm = (rows) => {
  const arms = new Map();
  for (const r of rows) {
    if (!arms.has(r.arm)) arms.set(r.arm, new Map());
    arms.get(r.arm).set(r.question, r.hit);
  }
  return arms;
};

/**
 * Pair run A against run B by (arm, question). `refusals` non-empty means `arms` is empty: nothing is
 * reported from a pairing the caller did not agree to. Undecided (`hit: null`) questions are left out of
 * the pairing, never scored as misses.
 */
export function compareResults(a, b, { allowQuestionMismatch = false, allowConfigMismatch = false } = {}) {
  const refusals = [];
  for (const field of ['bench', 'metric']) {
    const [x, y] = [distinct(a, field), distinct(b, field)];
    if (x !== y) refusals.push(`${field} differs: ${x} vs ${y}`);
  }
  const [ca, cb] = [distinct(a, 'configuration'), distinct(b, 'configuration')];
  if (ca !== cb && !allowConfigMismatch)
    refusals.push(`configuration differs: ${ca} vs ${cb} (--allow-config-mismatch to compare anyway)`);

  const [armsA, armsB] = [byArm(a), byArm(b)];
  const common = [...armsA.keys()].filter((arm) => armsB.has(arm)).sort();
  if (!allowQuestionMismatch)
    for (const arm of common) {
      const [qa, qb] = [armsA.get(arm), armsB.get(arm)];
      const onlyA = [...qa.keys()].filter((q) => !qb.has(q)).length;
      const onlyB = [...qb.keys()].filter((q) => !qa.has(q)).length;
      if (onlyA || onlyB)
        refusals.push(`questions differ for arm ${arm}: ${onlyA} only in A, ${onlyB} only in B `
          + '(--allow-question-mismatch to pair the intersection)');
    }

  const unpairedArms = {
    a: [...armsA.keys()].filter((arm) => !armsB.has(arm)).sort(),
    b: [...armsB.keys()].filter((arm) => !armsA.has(arm)).sort(),
  };
  if (refusals.length) return { refusals, arms: [], unpairedArms };

  const arms = common.map((arm) => {
    const [qa, qb] = [armsA.get(arm), armsB.get(arm)];
    let paired = 0, hitsA = 0, hitsB = 0, onlyA = 0, onlyB = 0;
    for (const [q, x] of qa) {
      const y = qb.get(q);
      if (x === null || y === null || y === undefined) continue;
      paired++;
      if (x) hitsA++;
      if (y) hitsB++;
      if (x && !y) onlyA++;
      if (y && !x) onlyB++;
    }
    return { arm, paired, hitsA, hitsB, onlyA, onlyB, p: mcnemarExact(onlyA, onlyB) };
  });
  return { refusals, arms, unpairedArms };
}

const rate = (hits, n) => (n === 0 ? '-' : `${((100 * hits) / n).toFixed(1)}%`);

/** The report as lines: refusals, or one row per paired arm. */
export function formatReport({ refusals, arms, unpairedArms }) {
  if (refusals.length) return ['bench-compare: REFUSED', ...refusals.map((r) => `  ${r}`)];
  const width = Math.max(8, ...arms.map((r) => r.arm.length));
  const lines = [`${'arm'.padEnd(width)}  paired  hit A → hit B   +A / -B   McNemar exact`];
  for (const r of arms)
    lines.push(`${r.arm.padEnd(width)}  ${String(r.paired).padStart(6)}  `
      + `${rate(r.hitsA, r.paired)} → ${rate(r.hitsB, r.paired)}  +${r.onlyA} / -${r.onlyB}  ${formatP(r.p)}`);
  if (unpairedArms.a.length) lines.push(`only in A: ${unpairedArms.a.join(', ')}`);
  if (unpairedArms.b.length) lines.push(`only in B: ${unpairedArms.b.join(', ')}`);
  lines.push("'+A' is questions only run A hit, '-B' only run B; only those disagreements carry information.");
  return lines;
}

export function parseArgs(argv) {
  return {
    files: argv.filter((a) => !a.startsWith('--')),
    allowConfigMismatch: argv.includes('--allow-config-mismatch'),
    allowQuestionMismatch: argv.includes('--allow-question-mismatch'),
  };
}

function main(argv) {
  const opts = parseArgs(argv);
  if (opts.files.length !== 2) {
    console.error('usage: node devtools/dev.mjs bench-compare <a.jsonl> <b.jsonl> '
      + '[--allow-question-mismatch] [--allow-config-mismatch]');
    process.exitCode = 1;
    return;
  }
  const [a, b] = opts.files.map((f) => parseResults(fs.readFileSync(f, 'utf8'), path.basename(f)));
  console.log(`A = ${opts.files[0]}\nB = ${opts.files[1]}`);
  const result = compareResults(a, b, opts);
  for (const line of formatReport(result)) console.log(line);
  if (result.refusals.length) process.exitCode = 1;
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  try { main(process.argv.slice(2)); } catch (err) { console.error(`bench-compare: ${err.message}`); process.exitCode = 1; }
}
