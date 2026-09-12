import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';

import { PORTS, ROLES, envFor, parseArgs, serverSpecs, servedFile } from '../memory-decision.mjs';

describe('serverSpecs', () => {
  const specs = serverSpecs('/models');

  it('passes -ngl EXPLICITLY on every role — the server default is not neutral and it logs no offload line', () => {
    for (const spec of specs) {
      const at = spec.argv.indexOf('-ngl');
      assert.ok(at >= 0, `${spec.label} does not pass -ngl`);
      assert.equal(spec.argv[at + 1], '99');
    }
  });

  it('binds 127.0.0.1, never localhost — ::1 first costs ~1.8s per call on an IPv4-only listener', () => {
    for (const spec of specs) assert.ok(spec.argv.includes('127.0.0.1'), `${spec.label} does not bind 127.0.0.1`);
    for (const url of Object.values(envFor()).filter((v) => v.startsWith('http')))
      assert.match(url, /^http:\/\/127\.0\.0\.1:\d+$/);
  });

  it('gives the embedder and the reranker their process-wide role flag, and the chat models none', () => {
    const flags = Object.fromEntries(specs.map((s) => [s.label, s.argv.filter((a) => a.startsWith('--'))]));
    assert.ok(flags.embed.includes('--embeddings'));
    assert.ok(flags.rerank.includes('--reranking'));
    // Both are PROCESS-WIDE restrictions, so a chat server carrying one would answer 501 to the sweep.
    for (const role of ['chat', 'small']) {
      assert.ok(!flags[role].includes('--embeddings'));
      assert.ok(!flags[role].includes('--reranking'));
    }
  });

  it('claims one port per role and none that another harness owns', () => {
    assert.deepEqual(specs.map((s) => s.port).sort(), [8150, 8151, 8152, 8153]);
    // 8140-8144 is memory-contention, 8147 is rerank-screen, 8090 is a sibling tool's embedder.
    for (const port of Object.values(PORTS)) assert.ok(port < 8140 || port > 8147, `${port} collides`);
    assert.ok(!Object.values(PORTS).includes(8090));
  });

  it('serves the two instruct models the size axis compares, at different sizes', () => {
    assert.equal(ROLES.chat.file, 'gemma-3-4b-it-Q4_K_M.gguf');
    assert.equal(ROLES.small.file, 'gemma-3-1b-it-Q4_K_M.gguf');
    assert.notEqual(ROLES.chat.file, ROLES.small.file);
  });

  it('does not embed a machine path — the caller supplies the model directory', () => {
    for (const spec of specs) for (const arg of spec.argv) assert.ok(!/^[A-Za-z]:\\/.test(arg), arg);
  });
});

describe('servedFile — identity is read BACK, never assumed from the argv', () => {
  it('reads the file the server actually opened, from either spelling', () => {
    assert.equal(servedFile({ model_path: 'C:\\models\\gemma-3-4b-it-Q4_K_M.gguf' }), 'gemma-3-4b-it-Q4_K_M.gguf');
    assert.equal(servedFile({ default_generation_settings: { model: '/m/LAMAR-600m.Q5_K_M.gguf' } }),
      'LAMAR-600m.Q5_K_M.gguf');
  });

  it('reports NULL when the build exposes nothing — "could not ask" is not "agrees"', () => {
    // The distinction is load-bearing: main() refuses on `agrees === false` and proceeds on `null`, so a
    // blank that reported agreement would silence the refusal entirely.
    assert.equal(servedFile(null), null);
    assert.equal(servedFile({}), null);
    assert.equal(servedFile({ model_path: '' }), null);
  });
});

describe('parseArgs', () => {
  it('keeps its own flag out of the bench args and forwards everything else verbatim', () => {
    const { skipBuild, benchArgs } = parseArgs(['--skip-build', '--n', '200', '--difficulty', 'hard']);
    assert.equal(skipBuild, true);
    assert.deepEqual(benchArgs, ['--n', '200', '--difficulty', 'hard']);
  });

  it('forwards a flag this module has never heard of, so the bench can grow one without an edit here', () => {
    assert.deepEqual(parseArgs(['--dump']).benchArgs, ['--dump']);
    assert.equal(parseArgs(['--dump']).skipBuild, false);
  });
});
