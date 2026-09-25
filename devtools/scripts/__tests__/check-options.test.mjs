// check-options' own tests.
//
// The gate asserts one thing — a settable option on a shipped `*Options` type carries SOME xml doc — so
// most of these are about the ways a scanner lies: matching nothing and calling it clean, missing a
// property because an attribute sat between it and its doc, or keeping an allowance that has stopped
// applying.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { after, describe, it } from 'node:test';

import { checkOptions, collectOptions } from '../check-options.mjs';
import { makeTree, removeTree, repoRoot } from './_fixtures.mjs';

const recorder = () => {
  const lines = [];
  const log = (s) => lines.push(s);
  log.text = () => lines.join('\n');
  return log;
};

const trees = [];
after(() => trees.forEach(removeTree));

/** A throwaway fixture tree (`_fixtures.mjs`, never OS temp), removed when the file's tests end. */
function fixture(files = {}) {
  const dir = makeTree(files);
  trees.push(dir);
  return dir;
}

/** A tree with one src file, so collectOptions can be driven without the real repository. */
function tree(source, name = 'Thing.cs') {
  return { dir: fixture({ [`src/${name}`]: source }), files: [`src/${name}`] };
}

describe('collectOptions', () => {
  it('finds a settable property and counts its doc lines', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    /// <summary>How many.</summary>
    /// <para>And why.</para>
    public int Count { get; set; }
}`);
    const rows = collectOptions(dir, files);
    assert.equal(rows.length, 1);
    assert.equal(rows[0].type, 'WidgetOptions');
    assert.equal(rows[0].prop, 'Count');
    assert.equal(rows[0].docLines, 2);
  });

  it('reports an UNdocumented property as zero doc lines', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public int Count { get; set; }
}`);
    assert.equal(collectOptions(dir, files)[0].docLines, 0);
  });

  it('steps back over an ATTRIBUTE between the doc and the property', () => {
    // Without this the gate would report a documented option as undocumented — a false positive on the
    // code that is doing the right thing, which is the shape that gets a gate ignored.
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    /// <summary>How many.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Count { get; set; }
}`);
    assert.equal(collectOptions(dir, files)[0].docLines, 1);
  });

  it('sees init-only properties, which is how most options here are written', () => {
    const { dir, files } = tree(`
public sealed record WidgetOptions
{
    public int Count { get; init; }
}`);
    assert.equal(collectOptions(dir, files).length, 1);
  });

  it('ignores a get-ONLY property, which a consumer cannot set', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public int Derived { get; }
    public int Count { get; set; }
}`);
    const rows = collectOptions(dir, files);
    assert.deepEqual(rows.map((r) => r.prop), ['Count']);
  });

  it('ignores a property on a type that is not *Options', () => {
    const { dir, files } = tree(`
public sealed class WidgetRequest
{
    public int Count { get; set; }
}`);
    assert.deepEqual(collectOptions(dir, files), []);
  });

  it('ignores a NON-public property', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    internal int Hidden { get; set; }
}`);
    assert.deepEqual(collectOptions(dir, files), []);
  });

  it('scans only src/ — a test or bench options type is an instrument, not a contract', () => {
    const dir = fixture({ 'tests/T.cs': 'public sealed class FakeOptions\n{\n    public int Count { get; set; }\n}' });
    assert.deepEqual(collectOptions(dir, ['tests/T.cs']), []);
  });

  it('sees a property whose accessors span several lines — the D78-guarded shape', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public double ReinforceGain
    {
        get => _gain;
        init => _gain = Require(value);
    }
    private readonly double _gain;
}`);
    assert.deepEqual(collectOptions(dir, files).map((r) => [r.prop, r.docLines]), [['ReinforceGain', 0]]);
  });

  it('ignores a multi-line property a consumer cannot set — get-only, or a non-public setter', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public int Derived
    {
        get { return 1; }
    }
    public int Counted { get; private set; }
}`);
    assert.deepEqual(collectOptions(dir, files), []);
  });

  it('stops attributing properties when the options type CLOSES — a later class is not the options type', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    /// <summary>Documented.</summary>
    public int Count { get; set; }
}

public sealed class WidgetState
{
    public int Seen { get; set; }
}`);
    assert.deepEqual(collectOptions(dir, files).map((r) => r.prop), ['Count']);
  });

  it('does not attribute a NESTED non-options type\'s properties to the options type', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public sealed class Detail
    {
        public int Inner { get; set; }
    }
    /// <summary>Documented.</summary>
    public int Count { get; set; }
}`);
    assert.deepEqual(collectOptions(dir, files).map((r) => r.prop), ['Count']);
  });

  it('sees a POSITIONAL record parameter, documented by its <param>', () => {
    const { dir, files } = tree(`
/// <summary>Tuning.</summary>
/// <param name="PollDelay">How long to wait
/// between polls.</param>
public sealed record WidgetOptions(TimeSpan? PollDelay = null, int Retries = 3)
{
    public TimeSpan Effective => PollDelay ?? TimeSpan.Zero;
}`);
    assert.deepEqual(collectOptions(dir, files).map((r) => [r.prop, r.docLines]),
      [['PollDelay', 2], ['Retries', 0]]);
  });
});

describe('checkOptions', () => {
  it('FAILS CLOSED when the scan finds nothing — an empty scan is a broken gate, not a clean tree', () => {
    // The rule GATES.md states outright, and the one this gate is most likely to break on later: a
    // pattern that stops matching prints the same tick as a clean repository unless this exists.
    const dir = fixture();
    const log = recorder();
    assert.equal(checkOptions(dir, {}, log, []), 1);
    assert.match(log.text(), /BROKEN GATE/);
    assert.doesNotMatch(log.text(), /✓/);
  });

  it('FAILS an undocumented option and names it', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public int Count { get; set; }
}`);
    const log = recorder();
    assert.equal(checkOptions(dir, {}, log, files), 1);
    assert.match(log.text(), /WidgetOptions\.Count/);
  });

  it('an ALLOWANCE excuses exactly the option it names, and nothing else', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    public int Count { get; set; }
    public int Other { get; set; }
}`);
    const log = recorder();
    const config = { optionDocAllowances: [{ type: 'WidgetOptions', prop: 'Count', why: 'self-evident' }] };

    assert.equal(checkOptions(dir, config, log, files), 1);   // Other is still undocumented
    assert.match(log.text(), /WidgetOptions\.Other/);
    assert.doesNotMatch(log.text(), /WidgetOptions\.Count —/);
  });

  it('a DEAD allowance FAILS, so an entry cannot outlive what it excused', () => {
    // GATES.md states this outright: an entry that cannot expire silently stops covering what it was
    // written for. Here the option has since been documented, so the allowance is dead.
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    /// <summary>Documented now.</summary>
    public int Count { get; set; }
}`);
    const log = recorder();
    const config = { optionDocAllowances: [{ type: 'WidgetOptions', prop: 'Count', why: 'was self-evident' }] };

    assert.equal(checkOptions(dir, config, log, files), 1);
    assert.match(log.text(), /no longer matches/);
  });

  it('passes a fully documented tree, and says how many it scanned', () => {
    const { dir, files } = tree(`
public sealed class WidgetOptions
{
    /// <summary>How many.</summary>
    public int Count { get; set; }
}`);
    const log = recorder();
    assert.equal(checkOptions(dir, {}, log, files), 0);
    assert.match(log.text(), /1 settable option\(s\)/);
  });
});

describe('the real tree', () => {
  const repo = repoRoot;

  it('is clean, and the counter actually computed something', async () => {
    const { default: config } = await import('../../project.config.mjs');
    const log = recorder();
    assert.equal(checkOptions(repo, config, log), 0, log.text());
    assert.match(log.text(), /settable option\(s\) across \d+ shipped type\(s\)/);
  });

  it('counts a substantial surface — a counter that quietly collapsed would still read clean', () => {
    // Pinning the counter against the real tree, as GATES.md requires: the first verify-gate counter here
    // returned the right total from two cancelling errors, and only comparing real values caught it.
    const rows = collectOptions(repo, [...new Set(
      fs.readdirSync(path.join(repo, 'src'), { recursive: true, encoding: 'utf8' })
        .filter((f) => f.endsWith('.cs'))
        .map((f) => `src/${f.split(path.sep).join('/')}`))]);
    assert.ok(rows.length > 100, `expected a three-figure option surface; found ${rows.length}`);
    assert.ok(new Set(rows.map((r) => r.type)).size > 15, 'expected options types across many packages');
    assert.ok(rows.some((r) => r.type === 'GraphMemoryOptions'), 'the largest options type must be seen');
  });
});
