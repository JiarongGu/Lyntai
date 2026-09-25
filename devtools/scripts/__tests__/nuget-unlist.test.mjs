// nuget-unlist — the retirement roster. See devtools/nuget-unlist.mjs.
//
// The roster's hand-kept half holds PUBLISHED package ids, and a rename sweep in the tree must never touch
// them. Three have been rewritten anyway (2026-09-15), and the third survived a whole day because the tool
// printed `- not published, skipping` and then reported success: a wrong id and a genuinely-never-published
// id were the same unremarkable line. That is the false PASS this file exists to pin.
//
// The facts below are about the ROSTER as data, so they run without the network — which is why the tool
// guards its own execution behind an `import.meta.url` check and exports the predicate.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { EventEmitter } from 'node:events';
import { Readable } from 'node:stream';

import { RETIRED, mustBePublished, nugetUnlist, promptHidden } from '../../nuget-unlist.mjs';

describe('nuget-unlist RETIRED roster', () => {
  it('flags a retired id the feed has never published, because that can only be a typo', () => {
    assert.equal(mustBePublished('Lyntai.Providers.ExtensionsAi', null), true);
  });

  it('says nothing about an id that is not retired — a live package may be unpublished', () => {
    assert.equal(mustBePublished('Lyntai.Core', null), false);
  });

  it('says nothing about a retired id that IS published — the normal case', () => {
    assert.equal(mustBePublished('Lyntai.Providers.Default', ['2.0.1']), false);
  });

  // The positive control. Without it this file would pass against a roster that had lost every entry, which
  // is precisely the shape the tool's own header records going wrong twice.
  it('carries the ids the 2026-09-15 sweeps rewrote, in their PUBLISHED spelling', () => {
    for (const id of [
      'Lyntai.Providers.OpenAiCompatible', // became `…Http` (D135)
      'Lyntai.Providers.Local', //           became `…LlamaSharp` (D138)
      'Lyntai.Providers.ExtensionsAi', //    became the namespace `Lyntai.ExtensionsAi` (D145)
    ]) assert.ok(RETIRED.includes(id), `${id} is missing from RETIRED — a rename sweep is the usual cause`);
  });

  // A namespace is not a package id, and this is the exact byte the D145 sweep wrote. Naming it outright
  // means a re-introduction fails on the name rather than on a count.
  it('holds no NAMESPACE that was never a published package', () => {
    assert.ok(!RETIRED.includes('Lyntai.ExtensionsAi'));
  });
});

describe('nuget-unlist — the tool, driven through its seams (no network, no key)', () => {
  const feed = (byId) => async (url) => {
    const id = Object.keys(byId).find((k) => url.includes(`/${k.toLowerCase()}/`));
    const versions = id ? byId[id] : null;
    if (versions === null) return { status: 404, ok: false };
    return { status: 200, ok: true, json: async () => ({ items: [{ items: versions.map((v) => ({ catalogEntry: { version: v } })) }] }) };
  };
  const drive = async (opts) => {
    const lines = [];
    const calls = [];
    const code = await nugetUnlist({
      log: (s) => lines.push(s), run: async (...a) => { calls.push(a); }, ids: () => ['Lyntai.Core'], ...opts,
    });
    return { code, out: lines.join('\n'), calls };
  };

  it('refuses --apply without a key, and touches nothing', async () => {
    const { code, out, calls } = await drive({ args: ['--apply'], fetch: feed({ 'Lyntai.Core': ['1.0.0'] }) });
    assert.equal(code, 1);
    assert.match(out, /No API key/);
    assert.deepEqual(calls, []);
  });

  it('a DRY RUN plans the versions below the cutoff and runs nothing', async () => {
    const { code, out, calls } = await drive({ args: [], fetch: feed({ 'Lyntai.Core': ['1.0.0', '1.2.0'] }) });
    assert.equal(code, 0);
    assert.match(out, /unlist \(1\): 1\.0\.0/);
    assert.deepEqual(calls, []);
  });

  it('FAILS the run on a RETIRED id the feed has never published', async () => {
    const { code, out } = await drive({ ids: () => ['Lyntai.Providers.Default'], fetch: feed({}) });
    assert.equal(code, 1);
    assert.match(out, /RETIRED but the feed has never published it/);
  });

  it('never prints the key, even inside a failing tool\'s echoed arguments', async () => {
    const key = 'k' + 'ey-' + 'EXAMPLE';
    const { out } = await drive({
      args: ['--apply'], env: { NUGET_API_KEY: key }, fetch: feed({ 'Lyntai.Core': ['1.0.0'] }),
      run: async () => { const e = new Error(`delete --api-key ${key} failed`); throw e; },
    });
    assert.doesNotMatch(out, new RegExp(key));
    assert.match(out, /\*\*\*/);
  });

  it('a bare --api-key PROMPTS for the key, which reaches the delete and is never printed', async () => {
    const key = 'k' + 'ey-' + 'TYPED';
    const { code, out, calls } = await drive({
      args: ['--apply', '--api-key'], prompt: async () => key, fetch: feed({ 'Lyntai.Core': ['1.0.0'] }),
    });
    assert.equal(code, 0, out);
    assert.ok(calls[0][1].includes(key), 'the typed key is what the delete carries');
    assert.doesNotMatch(out, new RegExp(key));
  });

  it('a DRY RUN never prompts, since it needs no key', async () => {
    const { code } = await drive({
      args: ['--api-key'], prompt: async () => { throw new Error('prompted on a dry run'); },
      fetch: feed({ 'Lyntai.Core': ['1.0.0'] }),
    });
    assert.equal(code, 0);
  });

  it('an EMPTY answer is no key, and touches nothing', async () => {
    const { code, out, calls } = await drive({
      args: ['--apply', '--api-key'], prompt: async () => '  ', fetch: feed({ 'Lyntai.Core': ['1.0.0'] }),
    });
    assert.equal(code, 1);
    assert.match(out, /No API key/);
    assert.deepEqual(calls, []);
  });
});

describe('nuget-unlist — promptHidden', () => {
  const sink = () => { const s = { text: '', write: (x) => { s.text += x; } }; return s; };

  it('reads the FIRST line of a pipe — a secret store or an echo', async () => {
    const input = Readable.from([Buffer.from('from-a-pipe\nignored\n')]);
    assert.equal(await promptHidden('key: ', { input, output: sink() }), 'from-a-pipe');
  });

  it('on a terminal it echoes nothing and honours backspace', async () => {
    const tty = Object.assign(new EventEmitter(), {
      isTTY: true, setRawMode() {}, resume() {}, pause() {}, setEncoding() {},
    });
    const output = sink();
    const answer = promptHidden('key: ', { input: tty, output });
    tty.emit('data', 'abc\u007fd\r');
    assert.equal(await answer, 'abd');
    assert.equal(output.text, 'key: \n', 'only the question and a newline are written — never the key');
  });
});
