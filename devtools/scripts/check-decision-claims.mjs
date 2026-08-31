// check-decision-claims — FAIL when a DECISION says something about the code that is no longer true.
//
// WHY THIS IS A GATE. The 2026-08-31 audit of `docs/DECISIONS.md` against the tree found the log accurate
// about VALUES and drifting on COUNTS and CLASSIFICATIONS: every stated constant verified, while D46's own
// title said "four DOMAINS" against seven and `CLAUDE.md` claimed five required `IMemoryGraphStore` members
// against thirteen. `TASKS.md` Part 129 and the audit record carry the full reading.
//
// NO EXISTING GATE CAN SEE IT. `check-docs` gates vocabulary a decision RETIRED, `check-links` whether a
// reference RESOLVES, `check-counts` counts written in PROSE. A decision going stale retires nothing,
// dangles nothing and moves no registered count, so the sentence stays grammatical, plausible and wrong —
// and `decisions-index` renders a stale title into the index table on top.
//
// A REGISTRY, NOT A SCAN, for the reason `pitfalls.md` gives: an existence check over this repository's
// prose returned ~45 hits and zero defects, because naming something absent is frequently correct here.
// "Wrong" depends on intent, so every entry is a claim somebody deliberately settled and a hit is a defect
// by construction. THE LIMIT: it covers what is registered — a gate against recurrence, not a proof.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(import.meta.url);
const repo = path.resolve(path.dirname(here), '..', '..');

const read = (r, ...p) => fs.readFileSync(path.join(r, ...p), 'utf8');

/** The seven graph-memory policy domains: a sub-directory holding one seam (D46/D47). */
export function policyDomainFolders(r) {
  const dir = path.join(r, 'src', 'Lyntai.Core', 'Memory');
  if (!fs.existsSync(dir)) return -1;
  return fs.readdirSync(dir, { withFileTypes: true })
    .filter((e) => e.isDirectory() && e.name !== 'Engines')
    .filter((e) => fs.readdirSync(path.join(dir, e.name)).some((f) => /^IMemory\w+Policy\.cs$/.test(f)))
    .length;
}

/**
 * A `private readonly` default on a shipped options record, by field name.
 *
 * <p>Returns `0` for a field DECLARED WITHOUT AN INITIALIZER, because that is C#'s default and this
 * repository ships at least one deliberate zero that way (`_diagnosticityWeight`, D62). An earlier version
 * matched only explicit initializers and reported that field as missing — a broken predicate reading as a
 * stale decision, which is the failure this gate's own error path exists to keep separate.</p>
 *
 * <p>Returns `NaN` only when the field is ABSENT, so "shipped at 0" and "deleted" stay distinguishable.</p>
 */
export function defaultOf(r, relative, field) {
  const src = read(r, relative);
  const withValue = new RegExp(`private readonly \\w+ _${field}\\s*=\\s*(-?[\\d.]+)`).exec(src);
  if (withValue) return Number(withValue[1]);
  return new RegExp(`private readonly \\w+ _${field}\\s*;`).test(src) ? 0 : NaN;
}

/**
 * Every claim registered so far, each one VERIFIED BY HAND during the 2026-08-31 audit before being
 * registered — a predicate nobody checked is a second unverified claim, not a gate.
 *
 * `holds` returns true when the decision is still true of the tree. `detail` explains what was found, so a
 * failure names the actual value rather than only announcing disagreement.
 */
export const DECISION_CLAIMS = [
  {
    id: 'D54',
    claim: 'retrieval-driven stability growth is OFF by default: ReinforceGain = 0',
    holds: (r) => defaultOf(r, 'src/Lyntai.Core/Memory/Forgetting/DsrRetrievability.cs', 'reinforceGain') === 0,
    detail: (r) => `ReinforceGain = ${defaultOf(r, 'src/Lyntai.Core/Memory/Forgetting/DsrRetrievability.cs', 'reinforceGain')}`,
    why: 'D54 is the measured default a whole study rests on; a silent change would invalidate it without moving a word of prose',
  },
  {
    id: 'D89',
    claim: 'salience does not vote on ranking: SalienceWeight ships at 0',
    holds: (r) => defaultOf(r, 'src/Lyntai.Core/Memory/Ranking/ReciprocalRankFusionPolicy.cs', 'salienceWeight') === 0,
    detail: (r) => `SalienceWeight = ${defaultOf(r, 'src/Lyntai.Core/Memory/Ranking/ReciprocalRankFusionPolicy.cs', 'salienceWeight')}`,
    why: 'D89 moved this to 0 on a two-embedder measurement; it is the one salience default that ships OFF',
  },
  {
    id: 'D62',
    claim: "ACT-R's fan effect is implemented and OFF: DiagnosticityWeight ships at 0",
    // BOTH halves are asserted, and that is the point: deleting the fan effect entirely would satisfy "it is
    // off" while falsifying "it is implemented". defaultOf returns NaN for an absent field and 0 for one
    // declared without an initializer, so the two cases stay distinguishable.
    holds: (r) => defaultOf(r, 'src/Lyntai.Core/Memory/Ranking/ReciprocalRankFusionPolicy.cs', 'diagnosticityWeight') === 0,
    detail: (r) => {
      const v = defaultOf(r, 'src/Lyntai.Core/Memory/Ranking/ReciprocalRankFusionPolicy.cs', 'diagnosticityWeight');
      return Number.isNaN(v) ? 'DiagnosticityWeight is GONE — the fan effect is no longer implemented' : `DiagnosticityWeight = ${v}`;
    },
    why: 'D62 says the fan effect EXISTS and is off; both halves matter, since deleting it would also satisfy "off"',
  },
  {
    id: 'D88',
    claim: 'the subject seed is ON by default: SubjectSeedOptions.K > 0',
    // The seed-source-fusion work moved this off GraphMemoryOptions onto its own options record
    // (src/Lyntai.Core/Memory/Seeding/SubjectSeedOptions.cs), where the default lives on the BACKING FIELD
    // (`_k`), read by `defaultOf` like every other claim in this registry.
    holds: (r) => defaultOf(r, 'src/Lyntai.Core/Memory/Seeding/SubjectSeedOptions.cs', 'k') > 0,
    detail: (r) => `SubjectSeedOptions.K = ${defaultOf(r, 'src/Lyntai.Core/Memory/Seeding/SubjectSeedOptions.cs', 'k')}`,
    why: 'D88 turns this ON deliberately, unlike SemanticSeedOptions.K (AddMemorySemanticSeeds is not '
      + 'registered by default); the two are easy to conflate',
  },
  {
    id: 'D46',
    claim: "CLAUDE.md's namespace map states the live policy-domain count",
    holds: (r) => {
      const n = policyDomainFolders(r);
      return new RegExp(`DOMAINS are (SEVEN|${n})\\b`, 'i').test(read(r, 'CLAUDE.md')) && n > 0;
    },
    detail: (r) => `${policyDomainFolders(r)} domain folder(s) hold an IMemory<X>Policy seam`,
    why: "D46's own title carried a stale count for two domain additions, and decisions-index rendered it into the index table too",
  },
  {
    id: 'D47',
    claim: 'every IMemory<X>Policy seam lives in a domain folder, except the composite-level removal policy',
    holds: (r) => {
      const root = path.join(r, 'src', 'Lyntai.Core', 'Memory');
      const strays = fs.readdirSync(root).filter((f) => /^IMemory\w+Policy\.cs$/.test(f));
      return strays.length === 1 && strays[0] === 'IMemoryRemovalPolicy.cs';
    },
    detail: (r) => `at the Memory root: ${fs.readdirSync(path.join(r, 'src', 'Lyntai.Core', 'Memory')).filter((f) => /^IMemory\w+Policy\.cs$/.test(f)).join(', ') || '(none)'}`,
    why: 'the ONE documented exception — removal governs blend MEMBERS, not entries. This audit filed its placement as a violation on the strength of the name and nearly moved it, breaking the API for nothing; the predicate is what makes the exception checkable instead of arguable',
  },
];

export function checkDecisionClaims(r, claims = DECISION_CLAIMS, log = console.log) {
  if (claims.length === 0) {
    log('check-decision-claims: no claims registered — nothing to check.');
    return 0;
  }

  const broken = [];
  for (const c of claims) {
    let ok;
    try {
      ok = c.invert ? !c.holds(r) : c.holds(r);
    } catch (err) {
      // A predicate that THROWS is a broken gate, not a stale decision — say so, because "fix the decision"
      // is the wrong advice when the checker is what failed. Same stance check-counts takes.
      log(`check-decision-claims: ✗ ${c.id}'s predicate threw — the GATE is broken, not the decision`);
      log(`  ${err.message}`);
      return 1;
    }
    if (!ok) broken.push(c);
  }

  if (broken.length === 0) {
    log(`check-decision-claims: ${claims.length} decision claim(s) still true of the tree ✓`);
    return 0;
  }

  log(`check-decision-claims: ✗ ${broken.length} decision(s) no longer describe the code\n`);
  for (const c of broken) {
    log(`  ${c.id} — ${c.claim}`);
    log(`      tree says: ${c.detail(r)}`);
    log(`      why gated: ${c.why}`);
  }
  log('');
  log('  Amend the DECISION, not the code — unless the code is what is wrong. A decision record is written');
  log('  in the present tense, so an entry that stopped being true is a defect in the entry.');
  return 1;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing.
if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.exitCode = checkDecisionClaims(repo);
}
