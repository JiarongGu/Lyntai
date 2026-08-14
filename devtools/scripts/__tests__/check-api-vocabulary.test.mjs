// check-api-vocabulary — the SURFACE gate (retired identifiers). See devtools/scripts/check-api-vocabulary.mjs.
//
// This gate exists because a stale PARAMETER NAME passed every check the repository had: `check-docs`
// excludes `src/`, and the API baseline records parameter names without judging them. Three of them
// (`ageClocks:`, `appraisers:`, `modulators:`) reached the eve of the 3.0 freeze and a human review, not a
// gate, caught them — TASKS.md Part 61, docs/DECISIONS.md D47.
//
// Two properties carry the whole gate, and both are tested here rather than assumed:
//   · a retired identifier IS caught, in a parameter position, which is where the measured defect lived;
//   · a LIVE identifier that merely shares a root is NOT caught — because the alternative is a gate that
//     cries wolf on a deliberate decision, gets an exclusion added, and rots.
// Each load-bearing test below was mutation-checked: break that specific line of the implementation and
// that test fails.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import projectConfig from '../../project.config.mjs';
import { BASELINE_DIR, checkApiVocabulary, identifiers, scanApiVocabulary } from '../check-api-vocabulary.mjs';
import { makeTree, recorder, removeTree, repoRoot } from './_fixtures.mjs';

/** Real lines, copied from tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt — the live surface as it ships. */
const LIVE = [
  'sealed class Lyntai.Memory.GraphMemoryEngine',
  '    .ctor(String name, IMemoryGraphStore store, GraphMemoryOptions options = null,'
    + ' IMemoryRetrievabilityPolicy policy = null, IEnumerable<IMemoryAgePolicy> agePolicies = null,'
    + ' IEnumerable<IMemorySaliencePolicy> saliencePolicies = null, IMemoryRankingPolicy ranking = null,'
    + ' Func<DateTimeOffset> clock = null)',
  'sealed class Lyntai.Memory.Modulation.ModulatedRetrievability',
  '    .ctor(IMemoryRetrievabilityPolicy inner, IEnumerable<IMemoryRetentionPolicy> retentionPolicies,'
    + ' IMemoryRetentionCompositionPolicy composition = null)',
  'sealed class Lyntai.Memory.Modulation.SalienceRetentionPolicy',
  'sealed class Lyntai.Llm.Streaming.InactivityClock',
  'sealed class Lyntai.Storage.MemoryEvictionPolicy',
].join('\n') + '\n';

/** Run the gate over a fixture tree of baselines: `{ 'Lyntai.Core.txt': '…' }`. */
function run(baselines, config, options = {}) {
  const files = {};
  for (const [name, text] of Object.entries(baselines)) files[`${BASELINE_DIR}/${name}`] = text;
  const dir = makeTree(files);
  const log = recorder();
  try {
    return { code: checkApiVocabulary(dir, config, log, log, options), out: log.text(), dir };
  } finally {
    removeTree(dir);
  }
}

/** The scan alone, for asserting on structure rather than on the report. */
function scan(baselines, config) {
  const files = {};
  for (const [name, text] of Object.entries(baselines)) files[`${BASELINE_DIR}/${name}`] = text;
  const dir = makeTree(files);
  try {
    return scanApiVocabulary(dir, config);
  } finally {
    removeTree(dir);
  }
}

describe('check-api-vocabulary — the measured defect it exists to catch', () => {
  it('catches a retired name in a PARAMETER position, which is where all three real ones lived', () => {
    const stale = '    .ctor(String name, IMemoryGraphStore store, IEnumerable<IMemoryAgePolicy> ageClocks = null)\n';
    const { code, out } = run({ 'Lyntai.Core.txt': stale }, {
      retiredApiNames: [{
        names: ['ageClocks'],
        use: '`agePolicies:`',
        why: 'D47 retired "clock" — age is interference',
      }],
    });
    assert.equal(code, 1, 'a retired parameter name must fail the gate');
    assert.match(out, /Lyntai\.Core\.txt:1/);
    assert.match(out, /\[ageClocks\]/, 'the report names the identifier, not just the line');
    assert.match(out, /use `agePolicies:` instead/);
    assert.match(out, /why: D47 retired "clock"/);
    assert.match(out, /costs a major version/, 'the report says WHY a name is expensive after a freeze');
  });

  it('scans every baseline file, and reports each one by name', () => {
    const { code, out } = run({
      'Lyntai.Core.txt': 'class X\n    .ctor(Int32 modulators)\n',
      'Lyntai.Storage.Sqlite.txt': 'class Y\n    .ctor(Int32 modulators)\n',
    }, { retiredApiNames: [{ names: ['modulators'], use: '`retentionPolicies:`', why: 'D47' }] });
    assert.equal(code, 1);
    assert.match(out, /Lyntai\.Core\.txt:2/);
    assert.match(out, /Lyntai\.Storage\.Sqlite\.txt:2/);
  });
});

describe('check-api-vocabulary — whole-identifier matching, the property that stops it crying wolf', () => {
  it('the SHIPPED registry is silent on the real live surface, then fires the moment it goes stale', () => {
    // The strongest form of the false-positive test: the registry that actually ships, against real
    // baseline lines. `Lyntai.Memory.Modulation` and `ModulatedRetrievability` are LIVE while `modulators`
    // and `IRetentionModulator` are RETIRED — same root, opposite verdicts.
    //
    // The 3.0 collision fix put a sharper pair in here, in the harder DIRECTION: `MemoryRetentionPolicy` is
    // retired while `IMemoryRetentionPolicy` is live and CONTAINS it — so a substring implementation fires
    // on the seam the rename exists to protect. The fixture carries that seam (the `ModulatedRetrievability`
    // ctor above) and the renamed storage type, so this assertion covers both sides of it.
    const clean = run({ 'Lyntai.Core.txt': LIVE }, projectConfig);
    assert.equal(clean.code, 0, `the live surface must be clean:\n${clean.out}`);

    const stale = run({ 'Lyntai.Core.txt': LIVE.replace('retentionPolicies', 'modulators') }, projectConfig);
    assert.equal(stale.code, 1, 'and the same line, spelled the retired way, must fail');
    assert.match(stale.out, /\[modulators\]/);
  });

  it('a retired name is never matched as a SUBSTRING of a live identifier', () => {
    // `InactivityClock` is a live type and `Func<DateTimeOffset> clock` a live parameter; four `*Clock`
    // policies are retired. An implementation testing `line.includes(name)` passes every other test in this
    // file and fails this one.
    const { hits } = scan({ 'Lyntai.Core.txt': LIVE }, {
      retiredApiNames: [{ names: ['Clock', 'Policy', 'Retention', 'Modulator'], use: 'x', why: 'y' }],
    });
    assert.deepEqual(hits.map((h) => `${h.line}:${h.tokens}`), [],
      'a bare root must not fire on the compound identifiers that merely contain it');
  });

  it('…while the same rule DOES fire on the whole identifier, so the test above is not vacuous', () => {
    const { hits } = scan({ 'Lyntai.Core.txt': LIVE }, {
      retiredApiNames: [{ names: ['clock'], use: 'x', why: 'y' }],
    });
    assert.equal(hits.length, 1, 'the tokenizer sees parameter names — `clock` is one');
    assert.deepEqual(hits[0].tokens, ['clock']);
  });

  it('a PATTERN is anchored to the whole token, so it cannot become an accidental substring rule', () => {
    const anchored = scan({ 'Lyntai.Core.txt': LIVE }, {
      retiredApiNames: [{ pattern: 'Rank', use: 'x', why: 'y' }],
    });
    assert.equal(anchored.hits.length, 0, '`Rank` must not fire on the live `IMemoryRankingPolicy`');

    const shaped = scan({
      'Lyntai.Core.txt': 'class Lyntai.Storage.CustomerDto\n    .ctor(MemoryRow dto)\n'
        + '    Domain : String\n    Offset : DateTimeOffset\n',
    }, { retiredApiNames: [projectConfig.retiredApiNames.find((r) => r.pattern)] });
    assert.deepEqual(shaped.hits.map((h) => h.tokens.join(',')), ['CustomerDto', 'dto'],
      'the shipped Dto rule catches both spellings and nothing else on those lines');
  });

  it('reports one hit per line even when a line carries the same retired name twice', () => {
    const { hits } = scan({ 'Lyntai.Core.txt': '    .ctor(Int32 modulators, Int32 modulators2, Int32 modulators)\n' },
      { retiredApiNames: [{ names: ['modulators'], use: 'x', why: 'y' }] });
    assert.equal(hits.length, 1);
    assert.deepEqual(hits[0].tokens, ['modulators']);
  });

  it('tokenizes a signature the way the surface is actually written', () => {
    assert.deepEqual(identifiers('    .ctor(IEnumerable<IMemoryAgePolicy> agePolicies = null)'),
      ['ctor', 'IEnumerable', 'IMemoryAgePolicy', 'agePolicies', 'null']);
    assert.deepEqual(identifiers('sealed class Lyntai.Memory.Modulation.ModulatedRetrievability'),
      ['sealed', 'class', 'Lyntai', 'Memory', 'Modulation', 'ModulatedRetrievability']);
  });
});

describe('check-api-vocabulary — the allow-list, an escape that cannot rot silently', () => {
  const rule = (allow) => ({
    retiredApiNames: [{ names: ['modulators'], use: '`retentionPolicies:`', why: 'D47', allow }],
  });
  const line = '    .ctor(IMemoryRetrievabilityPolicy inner, IEnumerable<X> modulators)';

  it('an EXACT signature is permitted', () => {
    const { code, out } = run({ 'Lyntai.Core.txt': `${line}\n` },
      rule([{ signature: line, why: 'a consumer pins this name; renaming it is scheduled for 4.0' }]));
    assert.equal(code, 0, out);
  });

  it('but the escape is narrow: another signature with the same name still fails', () => {
    const { code, out } = run({ 'Lyntai.Core.txt': `${line}\n    .ctor(Int32 modulators)\n` },
      rule([{ signature: line, why: 'scheduled for 4.0' }]));
    assert.equal(code, 1, 'an allowance permits ONE signature, never a name');
    assert.match(out, /Lyntai\.Core\.txt:2/);
    assert.doesNotMatch(out, /Lyntai\.Core\.txt:1\b/);
  });

  it('an allowance that matches nothing FAILS — a stale escape hides the next occurrence', () => {
    const { code, out } = run({ 'Lyntai.Core.txt': 'class Clean\n' },
      rule([{ signature: line, why: 'scheduled for 4.0' }]));
    assert.equal(code, 1, 'the escape outlived the surface it justified');
    assert.match(out, /allows a signature that no baseline contains/);
    assert.match(out, /delete the allowance/);
  });

  it('an allowance with no `why` FAILS — an escape without a reason is a silent exclusion', () => {
    const { code, out } = run({ 'Lyntai.Core.txt': `${line}\n` }, rule([{ signature: line }]));
    assert.equal(code, 1);
    assert.match(out, /allowance #0 has no `why`/);
  });
});

describe('check-api-vocabulary — a broken registry FAILS rather than skipping quietly', () => {
  // A rule that can never match is indistinguishable from a clean tree, which is the exact class of
  // failure this gate exists to end — so every malformed entry is reported, not dropped.
  const problems = (entry) => scan({ 'Lyntai.Core.txt': 'class X\n' }, { retiredApiNames: [entry] })
    .problems.map((p) => p.what).join('\n');

  it('rejects an entry with neither `names` nor `pattern`', () => {
    assert.match(problems({ use: 'x', why: 'y' }), /declares neither `names` nor `pattern`/);
  });

  it('rejects an entry with BOTH', () => {
    assert.match(problems({ names: ['a'], pattern: 'a', use: 'x', why: 'y' }), /declares BOTH/);
  });

  it('rejects an entry that refuses without teaching', () => {
    assert.match(problems({ names: ['a'], why: 'y' }), /has no `use`/);
    assert.match(problems({ names: ['a'], use: 'x' }), /has no `why`/);
  });

  it('rejects a pattern that is not a valid regex', () => {
    assert.match(problems({ pattern: '(unclosed', use: 'x', why: 'y' }), /is not a valid regex/);
  });
});

describe('check-api-vocabulary — fail-closed', () => {
  it('FAILS when there are no baselines to scan, rather than printing a tick over nothing', () => {
    const dir = makeTree({ 'README.md': 'nothing here\n' });
    const log = recorder();
    try {
      assert.equal(checkApiVocabulary(dir, projectConfig, log, log), 1);
      assert.match(log.text(), /found no API baselines/);
      assert.match(log.text(), /Nothing was scanned, so this gate proves nothing/);
    } finally {
      removeTree(dir);
    }
  });

  it('says so, and passes, when the registry is empty', () => {
    const { code, out } = run({ 'Lyntai.Core.txt': LIVE }, { retiredApiNames: [] });
    assert.equal(code, 0);
    assert.match(out, /no retired API names configured/);
  });
});

describe('check-api-vocabulary — the shipped registry against the REAL baselines', () => {
  it('produces ZERO hits on the surface this repository actually ships', () => {
    // The tree is clean right now, which is precisely what makes it the right proof that these rules do not
    // false-positive: every entry was checked against these files before it was seeded.
    const log = recorder();
    const code = checkApiVocabulary(repoRoot, projectConfig, log, log);
    assert.equal(code, 0, `the shipped registry must be clean of false positives:\n${log.text()}`);
  });

  it('and actually READ them — a passing scan over zero lines would prove nothing', () => {
    const { files, lines } = scanApiVocabulary(repoRoot, projectConfig);
    assert.ok(files.includes('Lyntai.Core.txt'), `expected the Core baseline among ${files.join(', ')}`);
    assert.ok(files.length >= 10, `expected every packable assembly's baseline, got ${files.length}`);
    assert.ok(lines > 1000, `expected the real surface, got ${lines} line(s)`);
  });
});
