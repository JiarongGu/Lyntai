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
  DECISION_CLAIMS, checkDecisionClaims, defaultOf, policyDomainFolders,
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
