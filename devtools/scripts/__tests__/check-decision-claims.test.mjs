// Tests for check-decision-claims.
//
// The gate's failure mode is a FALSE PASS — a predicate that quietly stops discriminating reports a clean
// repository forever, which is the whole reason `test-devtools` runs first in `verify` (TASKS.md Part 60,
// where three check-docs defects had passed every gate for their entire lifetime, all in the permissive
// direction).
//
// So these drive the pure function against SYNTHESIZED trees rather than the real one: a test that only ever
// asserts "the real repo is green" passes on a predicate that can never go red.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';

import {
  DECISION_CLAIMS, checkDecisionClaims, defaultOf, missingReleasedMigrations, policyDomainFolders,
  silentAotOptOuts,
  sqliteObjectsMissingPrefix,
  wireJsonSerializerUses,
} from '../check-decision-claims.mjs';

const repo = path.resolve(path.dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, '$1'),
  '..', '..', '..');

/** A throwaway tree, so a predicate can be driven to RED without touching the repository. */
function fixture(files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'lyntai-dc-'));
  for (const [rel, body] of Object.entries(files)) {
    const full = path.join(root, rel);
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, body);
  }
  return root;
}

describe('defaultOf', () => {
  it('reads an explicit initializer', () => {
    const r = fixture({ 'a.cs': 'private readonly double _w = 1.5;' });
    assert.equal(defaultOf(r, 'a.cs', 'w'), 1.5);
  });

  it('reports 0 for a field DECLARED WITHOUT an initializer, which is C#\'s default', () => {
    // The case that broke the first version of this gate: `_diagnosticityWeight` ships at 0 by declaring
    // nothing (D62), and a matcher requiring `= 0` read that as the field being ABSENT — a broken predicate
    // reporting as a stale decision.
    const r = fixture({ 'a.cs': 'private readonly double _w;' });
    assert.equal(defaultOf(r, 'a.cs', 'w'), 0);
  });

  it('reports NaN for an ABSENT field, so "ships at 0" and "deleted" stay distinguishable', () => {
    const r = fixture({ 'a.cs': 'private readonly double _other = 3;' });
    assert.ok(Number.isNaN(defaultOf(r, 'a.cs', 'w')));
  });
});

describe('policyDomainFolders', () => {
  it('counts only directories holding an IMemory<X>Policy seam, and never Engines', () => {
    const r = fixture({
      'src/Lyntai.Core/Memory/Salience/IMemorySaliencePolicy.cs': '',
      'src/Lyntai.Core/Memory/Ranking/IMemoryRankingPolicy.cs': '',
      'src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs': '',   // not a domain
      'src/Lyntai.Core/Memory/Storage/SomeRow.cs': '',             // no seam
    });
    assert.equal(policyDomainFolders(r), 2);
  });
});

describe('silentAotOptOuts (D7)', () => {
  const proj = (body) => ({ 'src/Lyntai.X/Lyntai.X.csproj': body });

  it('names a BARE opt-out — the RED case, and the one that quietens check-warnings', () => {
    const r = fixture(proj('<Project>\n<PropertyGroup>\n<IsAotCompatible>false</IsAotCompatible>\n'));
    assert.deepEqual(silentAotOptOuts(r), ['src/Lyntai.X/Lyntai.X.csproj:3']);
  });

  it('accepts an opt-out whose reason sits in the comment above it', () => {
    const r = fixture(proj('<Project>\n<PropertyGroup>\n'
      + '<!-- Dapper materializes via reflection: an honest opt-out. -->\n'
      + '<IsAotCompatible>false</IsAotCompatible>\n'));
    assert.deepEqual(silentAotOptOuts(r), []);
  });

  it('IGNORES a commented-out TEMPLATE, which is what the first run got wrong', () => {
    // Lyntai.Generation.csproj carries exactly this: an example inside <!-- --> showing what to write IF
    // the package ever needs to opt out. A naive scan reports a package that does not opt out as silent.
    const r = fixture(proj('<Project>\n<!--\n  If this package drags a reflection-heavy dependency it must\n'
      + '  opt out rather than inherit the claim:\n'
      + '    <IsAotCompatible>false</IsAotCompatible>\n-->\n<PropertyGroup>\n'));
    assert.deepEqual(silentAotOptOuts(r), []);
  });

  it('is silent about a project that keeps the inherited claim', () => {
    const r = fixture(proj('<Project>\n<PropertyGroup>\n<IsPackable>true</IsPackable>\n'));
    assert.deepEqual(silentAotOptOuts(r), []);
  });
});

describe('sqliteObjectsMissingPrefix (D6)', () => {
  const sql = (body) => ({ 'src/Lyntai.Storage.Sqlite/Migrations/M1.cs': body });

  it('names an object a new migration forgot to prefix — the RED case', () => {
    const r = fixture(sql('Execute.Sql("CREATE TABLE orders (id INTEGER)");'));
    assert.deepEqual(sqliteObjectsMissingPrefix(r), ['orders']);
  });

  it('accepts an index that CARRIES the prefix without leading with it', () => {
    // `ix_`/`ux_` + `lyntai_` is the shipped convention; D6 says objects "carry the prefix", and what it
    // buys is non-collision with an application's own schema, which an infix satisfies.
    const r = fixture(sql(
      'CREATE UNIQUE INDEX ux_lyntai_memory_dedup ON x(y); CREATE INDEX ix_lyntai_job_claim ON a(b);'));
    assert.deepEqual(sqliteObjectsMissingPrefix(r), []);
  });

  it('reads tables, triggers and virtual tables, in any quoting style', () => {
    const r = fixture(sql('CREATE TABLE IF NOT EXISTS "lyntai_kv" (k TEXT);'
      + 'CREATE TRIGGER lyntai_memory_node_ai AFTER INSERT ON x BEGIN END;'
      + 'CREATE VIRTUAL TABLE lyntai_memory_fts USING fts5(headline);'
      + 'CREATE TRIGGER audit_log_ai AFTER INSERT ON y BEGIN END;'));
    assert.deepEqual(sqliteObjectsMissingPrefix(r), ['audit_log_ai']);
  });

  it('FAILS CLOSED when nothing parses, rather than reporting a clean tree', () => {
    // The shape check-sensitive paid for: a broken pattern must never look like a clean repository.
    const r = fixture(sql('// no SQL at all in this migration'));
    assert.equal(sqliteObjectsMissingPrefix(r).length, 1);
    assert.match(sqliteObjectsMissingPrefix(r)[0], /broken predicate/);
  });

  it('sees the VERSION TABLE, which no CREATE statement ever names — the RED case D6 states and the '
    + 'predicate could not reach', () => {
    // D6 says the prefix covers "the FluentMigrator version table included", and that table is the
    // likeliest collision of all because FluentMigrator's default name is the very generic `VersionInfo`.
    // It is declared as metadata PROPERTIES rather than DDL, so a CREATE-only scan cannot see it: probed
    // 2026-09-10, renaming it back to the default left the predicate reporting a clean tree.
    const r = fixture({
      ...sql('Execute.Sql("CREATE TABLE lyntai_kv (k TEXT)");'),
      'src/Lyntai.Storage.Sqlite/Migrations/LyntaiVersionTable.cs':
        'public string TableName => "VersionInfo";\n public string UniqueIndexName => "ux_VersionInfo";',
    });
    assert.deepEqual(sqliteObjectsMissingPrefix(r), ['VersionInfo', 'ux_VersionInfo']);
  });

  it('accepts the version table as it SHIPS, so the new assertion is not merely always-red', () => {
    const r = fixture({
      ...sql('Execute.Sql("CREATE TABLE lyntai_kv (k TEXT)");'),
      'src/Lyntai.Storage.Sqlite/Migrations/LyntaiVersionTable.cs':
        'public string TableName => "lyntai_version_info";\n'
        + ' public string UniqueIndexName => "ux_lyntai_version_info";',
    });
    assert.deepEqual(sqliteObjectsMissingPrefix(r), []);
  });

  it('ignores the version table\'s NON-NAME metadata, which carry no object name to collide', () => {
    // `ColumnName`/`DescriptionColumnName`/`AppliedOnColumnName` are COLUMNS inside an already-prefixed
    // table, and `SchemaName` ships empty. Treating them as object names would flag the shipped file.
    const r = fixture({
      ...sql('Execute.Sql("CREATE TABLE lyntai_kv (k TEXT)");'),
      'src/Lyntai.Storage.Sqlite/Migrations/LyntaiVersionTable.cs':
        'public string SchemaName => "";\n public string TableName => "lyntai_version_info";\n'
        + ' public string ColumnName => "Version";\n public string DescriptionColumnName => "Description";\n'
        + ' public string UniqueIndexName => "ux_lyntai_version_info";\n'
        + ' public string AppliedOnColumnName => "AppliedOn";',
    });
    assert.deepEqual(sqliteObjectsMissingPrefix(r), []);
  });
});

describe('missingReleasedMigrations (D9)', () => {
  const released = { 'Lyntai.Storage.Sqlite': [202607280001, 202608121100] };
  const at = (body) => ({
    'src/Lyntai.Storage.Sqlite/Migrations/M202607280001_Kv.cs': '[Migration(202607280001)]',
    'src/Lyntai.Storage.Sqlite/Migrations/M202608121100_Memory.cs': body,
  });

  it('names a RENUMBERED released migration — the RED case no other gate can see', () => {
    // A renumber leaves the FRESH schema byte-identical, so MigrationSchemaSnapshotTests stays green, and
    // the tags are untouched, so MigrationTagConventionTests stays green. On a consumer's already-migrated
    // database the new number is absent from `lyntai_version_info`, so the migration RE-RUNS.
    const r = fixture(at('[Migration(202609101200)]'));
    assert.deepEqual(missingReleasedMigrations(r, released), ['Lyntai.Storage.Sqlite: 202608121100']);
  });

  it('names a DELETED released migration', () => {
    const r = fixture({ 'src/Lyntai.Storage.Sqlite/Migrations/M202607280001_Kv.cs': '[Migration(202607280001)]' });
    assert.deepEqual(missingReleasedMigrations(r, released), ['Lyntai.Storage.Sqlite: 202608121100']);
  });

  it('allows a NEW migration alongside every released one — it is a ratchet, not a freeze', () => {
    const r = fixture({
      ...at('[Migration(202608121100)]'),
      'src/Lyntai.Storage.Sqlite/Migrations/M202701010000_New.cs': '[Migration(202701010000)]',
    });
    assert.deepEqual(missingReleasedMigrations(r, released), []);
  });

  it('tolerates whitespace in the attribute, so a reformat is not a false RED', () => {
    const r = fixture(at('[Migration( 202608121100 )]'));
    assert.deepEqual(missingReleasedMigrations(r, released), []);
  });

  it('FAILS CLOSED when the directory is gone or nothing parses', () => {
    assert.match(missingReleasedMigrations(fixture({ 'x.txt': '' }), released)[0], /broken predicate/);
    const empty = fixture({ 'src/Lyntai.Storage.Sqlite/Migrations/README.cs': '// no attribute here' });
    assert.match(missingReleasedMigrations(empty, released)[0], /broken predicate/);
  });
});

describe('wireJsonSerializerUses (D14)', () => {
  it('finds a real USE in a wire path — the RED case the claim exists for', () => {
    const r = fixture({
      'src/Lyntai.Providers.Default/HttpBody.cs': 'var x = JsonSerializer.Deserialize<Reply>(body);',
    });
    assert.deepEqual(wireJsonSerializerUses(r), ['src/Lyntai.Providers.Default/HttpBody.cs']);
  });

  it('IGNORES the word in a comment, which is what the first run got wrong', () => {
    // Two shipped files say "JsonDocument.Parse (not JsonSerializer)" precisely because they honour D14;
    // a text match flagged the two call sites most explicitly obeying it.
    const r = fixture({
      // Must be a path the predicate actually WALKS, or this passes by scanning nothing rather than by
      // ignoring the comment — the vacuous-filter shape. It named a package that has since been folded
      // away (D123), which would have left it green and meaningless.
      'src/Lyntai.Providers.Default/Decl.cs':
        '// JsonDocument.Parse (not JsonSerializer) so the package stays trim/AOT-clean\n'
        + '/// Uses <see cref="JsonNode"/>, not reflection-based <c>JsonSerializer</c>.\n'
        + '/* JsonSerializer.Deserialize would be wrong here */\n'
        + 'var x = JsonDocument.Parse(body);',
    });
    assert.deepEqual(wireJsonSerializerUses(r), []);
  });

  it('does NOT scan storage or MCP hosting, which are outside what D14 governs', () => {
    // SqliteJson/PostgresJson serialize this library's OWN persisted payloads; MCP hands the SDK its own
    // JsonTypeInfo. Both use JsonSerializer legitimately, so a predicate that scanned them would be red
    // against a correct tree.
    const r = fixture({
      'src/Lyntai.Storage.Sqlite/SqliteJson.cs': 'JsonSerializer.Serialize(value);',
      'src/Lyntai.Tools.Mcp.Hosting/McpToolHost.cs': 'JsonSerializer.DeserializeAsync(s, info, ct);',
    });
    assert.deepEqual(wireJsonSerializerUses(r), []);
  });

  it('skips bin/obj, which hold copies of the sources own XML docs', () => {
    const r = fixture({
      'src/Lyntai.Generation/bin/Release/Lyntai.Generation.cs': 'JsonSerializer.Deserialize<T>(s);',
    });
    assert.deepEqual(wireJsonSerializerUses(r), []);
  });
});

describe('checkDecisionClaims', () => {
  it('passes when every registered claim holds', () => {
    const lines = [];
    const ok = checkDecisionClaims(repo, [{
      id: 'DX', claim: 'always true', holds: () => true, detail: () => '', why: 'test',
    }], (m) => lines.push(m));
    assert.equal(ok, 0);
    assert.match(lines.join('\n'), /still true of the tree/);
  });

  it('FAILS and names the decision when a claim stops holding', () => {
    const lines = [];
    const code = checkDecisionClaims(repo, [{
      id: 'DX', claim: 'the sky is green', holds: () => false, detail: () => 'sky is blue', why: 'test',
    }], (m) => lines.push(m));
    assert.equal(code, 1);
    const out = lines.join('\n');
    assert.match(out, /DX/);
    assert.match(out, /sky is blue/);          // the ACTUAL value, not just "disagrees"
    assert.match(out, /Amend the DECISION/);
  });

  it('reports a THROWING predicate as a broken GATE, not as a stale decision', () => {
    // "Fix the decision" is the wrong advice when the checker is what failed — the same stance check-counts
    // takes for a counter that computes nothing.
    const lines = [];
    const code = checkDecisionClaims(repo, [{
      id: 'DX', claim: 'unreadable', holds: () => { throw new Error('ENOENT'); }, detail: () => '', why: 'test',
    }], (m) => lines.push(m));
    assert.equal(code, 1);
    assert.match(lines.join('\n'), /the GATE is broken, not the decision/);
  });

  it('says so rather than passing vacuously when nothing is registered', () => {
    const lines = [];
    assert.equal(checkDecisionClaims(repo, [], (m) => lines.push(m)), 0);
    assert.match(lines.join('\n'), /nothing to check/);
  });

  it('every registered claim holds against the REAL tree', () => {
    // Pinned last, deliberately: it is the weakest assertion here, because it passes on a predicate that can
    // never go red. The fixtures above are what prove these can.
    assert.equal(checkDecisionClaims(repo, DECISION_CLAIMS, () => {}), 0);
  });
});
