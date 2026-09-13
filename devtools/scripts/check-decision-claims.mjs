// check-decision-claims — FAIL when a DECISION says something about the code that is no longer true.
//
// WHY THIS IS A GATE. The 2026-08-31 audit of `docs/DECISIONS.md` against the tree found the log accurate
// about VALUES and drifting on COUNTS and CLASSIFICATIONS: every stated constant verified, while D46's own
// title said "four DOMAINS" against seven and `CLAUDE.md` claimed five required `IMemoryGraphStore` members
// against thirteen. `docs/task-archive.md` Part 186 carries the full reading, including the two gates that
// were designed and REFUSED — a text predicate defeated by a one-token edit, and a counter with nothing in
// the tree to derive.
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

/**
 * Every `.cs` under the WIRE-JSON paths that names `JsonSerializer` (D14), repo-relative.
 *
 * Scoped to the paths D14 is actually about — a provider or generation backend parsing a VENDOR's reply.
 * Storage deliberately does not count: `SqliteJson`/`PostgresJson` serialize this library's OWN persisted
 * payloads, which neither drift field by field nor come off a wire. MCP hosting does not count either — it
 * hands the MCP SDK's own `JsonTypeInfo` to `JsonSerializer`, so the reflection D14 rules out is not in
 * play. Both were audited by hand on 2026-09-04 before this predicate was written; without the scope the
 * claim reads as violated by two call sites that are fine.
 *
 * COMMENTS ARE STRIPPED, and this predicate's own first run is why: two files say *"JsonDocument.Parse (not
 * JsonSerializer) so the package stays trim/AOT-clean"*, so a plain text match flagged the two call sites
 * that most explicitly HONOUR D14. It is the mirror of the trap `check-links` records — an index built from
 * prose lets prose speak for the code — and the fix is the same: read the code, not what it says about
 * itself. A USE is `JsonSerializer.`; the bare word is prose.
 */
export function wireJsonSerializerUses(r) {
  const roots = ['src/Lyntai.Core/Llm', 'src/Lyntai.Core/Generation', 'src/Lyntai.Generation',
    'src/Lyntai.Providers.Default', 'src/Lyntai.Providers.ExtensionsAi', 'src/Lyntai.Providers.LlamaSharp'];
  const strip = (s) => s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '');
  const hits = [];
  const walk = (rel) => {
    const abs = path.join(r, rel);
    if (!fs.existsSync(abs)) return;
    for (const e of fs.readdirSync(abs, { withFileTypes: true })) {
      // `bin`/`obj` hold copies of the sources' own XML docs, which would match the text and are not code.
      if (e.isDirectory()) { if (e.name !== 'bin' && e.name !== 'obj') walk(`${rel}/${e.name}`); continue; }
      if (e.name.endsWith('.cs') && /JsonSerializer\s*\./.test(strip(read(r, rel, e.name))))
        hits.push(`${rel}/${e.name}`);
    }
  };
  roots.forEach(walk);
  return hits;
}

/**
 * Every SQLite object name that does not carry `lyntai_` (D6), from DDL and from version-table metadata.
 *
 * The test is CONTAINS, not starts-with, and that is D6's own wording: indexes follow `ix_`/`ux_` +
 * `lyntai_`, so `ix_lyntai_job_claim` carries the prefix without leading with it. What D6 actually buys is
 * that a consuming application can point `UseSqliteStorage` at its own database — so the property that
 * matters is that no name Lyntai creates can collide with one the application chose.
 *
 * TWO SOURCES, because D6 names an object that no `CREATE` ever mentions. Its text scopes the prefix to
 * "the FluentMigrator version table included", and that table is declared as `IVersionTableMetaData`
 * properties. A CREATE-only scan therefore could not see the likeliest collision of all — FluentMigrator's
 * default name is the very generic `VersionInfo`. Probed 2026-09-10 before this was widened: renaming the
 * table back to that default left the predicate reporting a CLEAN tree, which is the false-pass direction
 * every gate here is built to avoid.
 */
export function sqliteObjectsMissingPrefix(r) {
  const dir = path.join(r, 'src', 'Lyntai.Storage.Sqlite');
  if (!fs.existsSync(dir)) return ['<no SQLite package>'];
  const names = [];
  const walk = (abs) => {
    for (const e of fs.readdirSync(abs, { withFileTypes: true })) {
      const full = path.join(abs, e.name);
      if (e.isDirectory()) { if (e.name !== 'bin' && e.name !== 'obj') walk(full); continue; }
      if (!e.name.endsWith('.cs')) continue;
      const sql = fs.readFileSync(full, 'utf8');
      for (const m of sql.matchAll(
        /CREATE\s+(?:UNIQUE\s+|VIRTUAL\s+)?(?:TABLE|INDEX|TRIGGER)\s+(?:IF\s+NOT\s+EXISTS\s+)?["`[]?([A-Za-z_][A-Za-z0-9_]*)/gi))
        names.push(m[1]);
      // The VERSION TABLE is named by metadata PROPERTIES, never by DDL, so the scan above cannot reach the
      // one object D6 calls out by name. Only the two properties that name an OBJECT are read: `ColumnName`,
      // `DescriptionColumnName` and `AppliedOnColumnName` are columns INSIDE an already-prefixed table and
      // `SchemaName` ships empty, so reading those would flag the shipped file.
      for (const m of sql.matchAll(
        /\b(?:TableName|UniqueIndexName)\s*(?:=>|=)\s*"([^"]+)"/g))
        names.push(m[1]);
    }
  };
  walk(dir);
  // A parse that finds NOTHING is a broken predicate, not a clean tree — the shape check-sensitive paid for.
  if (names.length === 0) return ['<no CREATE statements found — broken predicate>'];
  return [...new Set(names.filter((n) => !n.toLowerCase().includes('lyntai_')))];
}

/**
 * The migration numbers already SHIPPED, per storage package — the ratchet D9's second half needs.
 *
 * Captured from `v3.1.0` on 2026-09-10 and identical to HEAD at that point, so this list starts as a pure
 * ratchet with nothing to pay down. It only ever GROWS: add a row when a migration ships, never remove one.
 */
export const RELEASED_MIGRATIONS = {
  'Lyntai.Storage.Sqlite': [
    202607280001, 202607280002, 202607280003, 202607280004, 202607280005, 202607280006,
    202607280007, 202607280008, 202607280009, 202608081215, 202608121100, 202608161159,
  ],
  'Lyntai.Storage.Postgres': [
    202607280001, 202607280002, 202607280003, 202607280004, 202607280005, 202607280006,
    202607280007, 202607280008, 202607280009, 202608081215, 202608121100, 202608152310, 202608161159,
  ],
};

/**
 * Every RELEASED migration number no longer present in the tree (D9), as `package: number`.
 *
 * WHAT NO OTHER GATE SEES. D9 lets a PRE-RELEASE migration be folded into the one that owns its table, and
 * that carve-out is what makes renumbering a released one thinkable. A renumber leaves the FRESH-database
 * schema byte-identical, so `MigrationSchemaSnapshotTests` stays green; the tags are untouched, so
 * `MigrationTagConventionTests` stays green. But an already-migrated database records each migration BY
 * NUMBER in `lyntai_version_info`, so on a consumer's deployed database the new number is absent and the
 * migration RE-RUNS — a hard failure on somebody else's upgrade, which D18 singles out as the one class of
 * break no disclosure repairs.
 *
 * The snapshot tests make it worse rather than better: they fail with "the schema changed", whose obvious
 * remedy (`LYNTAI_UPDATE_SCHEMA_SNAPSHOT=1`) is exactly the wrong move here.
 *
 * A RATCHET, not a freeze. A new migration is welcome; only the DISAPPEARANCE of a shipped number is a
 * defect, so this never obstructs ordinary schema work.
 */
export function missingReleasedMigrations(r, released = RELEASED_MIGRATIONS) {
  const gone = [];
  for (const [pkg, numbers] of Object.entries(released)) {
    const dir = path.join(r, 'src', pkg, 'Migrations');
    if (!fs.existsSync(dir)) { gone.push(`${pkg}: <no Migrations directory — broken predicate>`); continue; }
    const present = new Set();
    for (const f of fs.readdirSync(dir)) {
      if (!f.endsWith('.cs')) continue;
      for (const m of read(r, 'src', pkg, 'Migrations', f).matchAll(/\[Migration\(\s*(\d+)/g))
        present.add(Number(m[1]));
    }
    // A directory that parses to nothing is a broken predicate, not a clean tree.
    if (present.size === 0) { gone.push(`${pkg}: <no [Migration(n)] found — broken predicate>`); continue; }
    for (const n of numbers) if (!present.has(n)) gone.push(`${pkg}: ${n}`);
  }
  return gone;
}

/**
 * Every packable project that turns `IsAotCompatible` OFF without saying why (D7), repo-relative.
 *
 * Opting out is SANCTIONED — seven projects do, for dynamic JSON or a native backend. What D7 forbids is
 * doing it SILENTLY, and the reason that matters more than it looks: `IsAotCompatible=true` is what turns
 * the trim/AOT analyzers on, so a project that opts out stops producing IL2026/IL3050 entirely. A silent
 * opt-out therefore makes `check-warnings` QUIETER — the failure direction where a gate reports success
 * because it has been switched off, which is the shape `check-encoding` and `check-sensitive` both paid for.
 *
 * "Says why" is an XML comment in the four lines above the property; every shipped opt-out has one.
 *
 * COMMENT BODIES ARE BLANKED FIRST, and this predicate's own first run is why — the SECOND time in one
 * session that reading text without stripping comments produced a false positive (the other was D14's).
 * `Lyntai.Generation.csproj` carries a TEMPLATE inside `<!-- -->` showing what to write if the package ever
 * needs to opt out, so a naive scan reports a package that opts out as silent when it does not opt out at
 * all. Blanking preserves line numbers so the report still points at a real line.
 */
export function silentAotOptOuts(r) {
  const dir = path.join(r, 'src');
  if (!fs.existsSync(dir)) return ['<no src>'];
  // Replace each comment body with the same number of newlines, so indexes stay true to the file.
  const blank = (s) => s.replace(/<!--[\s\S]*?-->/g, (m) => m.replace(/[^\n]/g, ' '));
  const bad = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (!e.isDirectory()) continue;
    const proj = path.join(dir, e.name, `${e.name}.csproj`);
    if (!fs.existsSync(proj)) continue;
    const raw = fs.readFileSync(proj, 'utf8').split(/\r?\n/);
    const code = blank(fs.readFileSync(proj, 'utf8')).split(/\r?\n/);
    code.forEach((line, i) => {
      if (!/<IsAotCompatible>\s*false\s*<\/IsAotCompatible>/i.test(line)) return;
      // A reason may sit on the property's own line or in the comment block just above it; four lines is
      // enough for the longest shipped rationale and short enough that an unrelated comment cannot pass.
      const window = raw.slice(Math.max(0, i - 4), i + 1).join('\n');
      if (!window.includes('<!--')) bad.push(`src/${e.name}/${e.name}.csproj:${i + 1}`);
    });
  }
  return bad;
}

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
    id: 'D6',
    claim: 'every SQLite object a migration creates carries `lyntai_`, so it cannot collide with a consumer',
    holds: (r) => sqliteObjectsMissingPrefix(r).length === 0,
    detail: (r) => {
      const bad = sqliteObjectsMissingPrefix(r);
      return bad.length === 0 ? 'every created object carries `lyntai_`' : `unprefixed: ${bad.join(', ')}`;
    },
    why: 'D6 exists because `UseSqliteStorage` may target a database the APPLICATION also uses, so an '
      + 'unprefixed name is a collision in someone else\'s schema rather than a style slip — and it would '
      + 'be introduced by a new migration, which is exactly when nobody re-reads a 2026-07 decision',
  },
  {
    id: 'D7',
    claim: 'a packable project that opts OUT of the trim/AOT claim says why — it never stays silent',
    holds: (r) => silentAotOptOuts(r).length === 0,
    detail: (r) => {
      const bad = silentAotOptOuts(r);
      return bad.length === 0 ? 'every IsAotCompatible=false carries its reason' : `silent at ${bad.join(', ')}`;
    },
    why: 'opting out is sanctioned; doing it SILENTLY is not. `IsAotCompatible=true` is what enables the '
      + 'trim/AOT analyzers, so an opt-out stops IL2026/IL3050 being raised at all — a silent one makes '
      + '`check-warnings` quieter rather than louder, which is the failure direction where a gate passes '
      + 'because it was switched off',
  },
  {
    id: 'D14',
    claim: 'wire JSON is hand-walked: no reflection JsonSerializer on a provider or generation reply',
    holds: (r) => wireJsonSerializerUses(r).length === 0,
    detail: (r) => {
      const hits = wireJsonSerializerUses(r);
      return hits.length === 0 ? 'no JsonSerializer in the wire paths' : `JsonSerializer in ${hits.join(', ')}`;
    },
    why: 'D14 is a TRIM promise as much as a parsing one — reflection `System.Text.Json` is the largest '
      + 'AOT hazard a library can carry, and `IsAotCompatible` stamps `IsTrimmable` into these assemblies. '
      + 'A source-generated context would be AOT-safe and would still falsify the entry, which says such '
      + 'envelopes are "deliberately not taken" — and it would raise NO warning, so `check-warnings` '
      + 'cannot see it',
  },
  {
    id: 'D9',
    claim: 'a RELEASED migration keeps its number — folding is a pre-release move only',
    holds: (r) => missingReleasedMigrations(r).length === 0,
    detail: (r) => {
      const gone = missingReleasedMigrations(r);
      return gone.length === 0 ? 'every shipped migration number is still present' : `missing: ${gone.join(', ')}`;
    },
    why: 'the fold D9 permits before a release is what makes renumbering a SHIPPED migration thinkable, and '
      + 'nothing else here can see it: a renumber leaves the fresh schema byte-identical (the snapshot tests '
      + 'stay green) and the tags untouched (the convention test stays green), while an already-migrated '
      + 'database re-runs the migration because it records the OLD number. That lands on a consumer\'s '
      + 'upgrade, which D18 names as the one break no disclosure repairs — and the snapshot tests actively '
      + 'mislead here, since their failure message invites regenerating the golden',
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
