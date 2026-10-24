// What the `test` step EXECUTED, read from the trx it emits rather than from `dotnet test`'s console line.
//
// With Docker down every Postgres fact SKIPS and the run still prints `Passed!`, so the whole leg went
// unexercised behind a green `verify` (`docs/GATES.md` §test). This reduces the trx to a JSON summary with
// each skip bucketed by reason, and fails when a Postgres fact skipped unless the run declares it with
// `LYNTAI_ALLOW_POSTGRES_SKIP=1`. The leading underscore keeps it out of the gate roster: `dev.mjs test`
// calls it.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

/** Every Postgres skip reason starts with this — `PostgresFixture.InitError` and each site's fallback. */
export const POSTGRES_SKIP_MARKER = 'Postgres/Docker unavailable';

/** The run's declaration that a skipped Postgres leg is expected. Only the value `1` declares it. */
export const ALLOW_POSTGRES_SKIP_ENV = 'LYNTAI_ALLOW_POSTGRES_SKIP';

/** Where `dev.mjs test` writes the trx and its summary — gitignored scratch, rewritten every run. */
export const RESULTS_DIR = 'devtools/_test-results';
export const TRX_NAME = 'lyntai.trx';
export const SUMMARY_NAME = 'summary.json';

const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };
const decode = (s) => s.replace(/&(#x[0-9a-f]+|#\d+|\w+);/gi, (m, e) => (e[0] !== '#' ? ENTITIES[e] ?? m
  : String.fromCodePoint(e[1] === 'x' || e[1] === 'X' ? parseInt(e.slice(2), 16) : Number(e.slice(1)))));
const attr = (attrs, name) => {
  const m = new RegExp(`\\b${name}="([^"]*)"`).exec(attrs);
  return m ? decode(m[1]) : undefined;
};
/** `Ns.Class.Method(args)` → `Ns.Class`; the arguments go first, because a theory's may contain a dot. */
const classOf = (testName) => {
  const bare = testName.replace(/\(.*$/s, '');
  return bare.slice(0, Math.max(0, bare.lastIndexOf('.')));
};

/**
 * Reduce a VSTest trx to counts and skip buckets. Counted from each `UnitTestResult`'s OUTCOME: VSTest's
 * `Counters` element reports `notExecuted="0"` for an xUnit skip, so a summary read from it sees none.
 * @param {string} xml the trx text
 * @returns {{ total: number, passed: number, failed: number, skipped: number, other: number,
 *   postgresSkipped: number, skips: { reason: string, count: number, postgres: boolean, classes: string[] }[] }}
 *   `skips` in descending count, ties in first-seen order
 */
export function reduceTrx(xml) {
  const summary = { total: 0, passed: 0, failed: 0, skipped: 0, other: 0, postgresSkipped: 0, skips: [] };
  const buckets = new Map();
  // An attribute value may hold `>` in principle, so the opening tag is quoted strings or non-`>` runs.
  const results = /<UnitTestResult\b((?:"[^"]*"|[^>"])*?)(?:\/>|>([\s\S]*?)<\/UnitTestResult>)/g;
  for (const [, attrs, body = ''] of xml.matchAll(results)) {
    summary.total++;
    const outcome = attr(attrs, 'outcome');
    if (outcome === 'Passed') { summary.passed++; continue; }
    if (['Failed', 'Error', 'Timeout', 'Aborted'].includes(outcome)) { summary.failed++; continue; }
    if (outcome !== 'NotExecuted') { summary.other++; continue; }

    summary.skipped++;
    const reason = decode((/<Message>([\s\S]*?)<\/Message>/.exec(body) ?? [])[1] ?? '(no reason given)').trim();
    const postgres = reason.startsWith(POSTGRES_SKIP_MARKER);
    if (postgres) summary.postgresSkipped++;
    const bucket = buckets.get(reason) ?? { reason, count: 0, postgres, classes: new Set() };
    bucket.count++;
    bucket.classes.add(classOf(attr(attrs, 'testName') ?? ''));
    buckets.set(reason, bucket);
  }
  summary.skips = [...buckets.values()].sort((a, b) => b.count - a.count)
    .map((b) => ({ ...b, classes: [...b.classes].sort() }));
  return summary;
}

/**
 * The verdict on a reduced run: 1 when it executed nothing, or when a Postgres fact skipped and the run did
 * not declare it; 0 otherwise. Always prints the trio, so the by-hand re-measure reads it off one line.
 * @param {ReturnType<typeof reduceTrx>} summary
 * @param {{ allowPostgresSkip: boolean }} declared
 * @param {(s: string) => void} log
 */
export function checkTestEvidence(summary, { allowPostgresSkip }, log = console.log) {
  const { total, passed, skipped, postgresSkipped } = summary;
  log(`test: ${passed} passed / ${total} total, ${skipped} skipped (Postgres leg: ${postgresSkipped} skipped)`);
  if (total === 0) {
    log('test: ✗ the trx records NO test results — a run that executed nothing is not evidence of anything');
    return 1;
  }
  if (!postgresSkipped) return 0;
  if (allowPostgresSkip) {
    log(`test: ${postgresSkipped} Postgres fact(s) skipped, DECLARED by ${ALLOW_POSTGRES_SKIP_ENV}=1`);
    return 0;
  }
  log(`test: ✗ ${postgresSkipped} Postgres fact(s) SKIPPED — the Postgres leg did not run, so this green`);
  log('  run says nothing about the Postgres backend. Start Docker and re-run; if the skip is intended, declare');
  log(`  it with ${ALLOW_POSTGRES_SKIP_ENV}=1 (docs/GATES.md §test).`);
  return 1;
}

/**
 * Read the trx, write the JSON summary beside it (with whether the run declared a Postgres skip), and
 * return the verdict. A missing trx FAILS: an absent record never reads as a clean one.
 * @param {{ trxPath: string, summaryPath: string, env?: Record<string, string | undefined>,
 *   log?: (s: string) => void }} opts
 * @returns {number} the exit code
 */
export function testEvidence({ trxPath, summaryPath, env = process.env, log = console.log }) {
  if (!existsSync(trxPath)) {
    log(`test: ✗ no trx at ${trxPath} — nothing records what this run executed`);
    return 1;
  }
  const allowPostgresSkip = env[ALLOW_POSTGRES_SKIP_ENV] === '1';
  const summary = reduceTrx(readFileSync(trxPath, 'utf8'));
  mkdirSync(dirname(summaryPath), { recursive: true });
  writeFileSync(summaryPath, `${JSON.stringify({ ...summary, postgresSkipDeclared: allowPostgresSkip }, null, 2)}\n`);
  return checkTestEvidence(summary, { allowPostgresSkip }, log);
}
