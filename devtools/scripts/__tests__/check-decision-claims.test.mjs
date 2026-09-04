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
  DECISION_CLAIMS, checkDecisionClaims, defaultOf, policyDomainFolders, silentAotOptOuts,
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
});

describe('wireJsonSerializerUses (D14)', () => {
  it('finds a real USE in a wire path — the RED case the claim exists for', () => {
    const r = fixture({
      'src/Lyntai.Providers.Default/OpenAiHttp.cs': 'var x = JsonSerializer.Deserialize<Reply>(body);',
    });
    assert.deepEqual(wireJsonSerializerUses(r), ['src/Lyntai.Providers.Default/OpenAiHttp.cs']);
  });

  it('IGNORES the word in a comment, which is what the first run got wrong', () => {
    // Two shipped files say "JsonDocument.Parse (not JsonSerializer)" precisely because they honour D14;
    // a text match flagged the two call sites most explicitly obeying it.
    const r = fixture({
      'src/Lyntai.Providers.ExtensionsAi/Decl.cs':
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
