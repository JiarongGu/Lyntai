import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';

import { ROLES as DECISION_ROLES, PORTS as DECISION_PORTS } from '../memory-decision.mjs';
import {
  NEEDED_FREE_MIB, PORTS, ROLES, envFor, hasHeadroom, parseArgs, parseGpuMemory, serverSpecs,
} from '../tool-affordance.mjs';

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

  it('serves the SAME weights per role as memory-decision — the two tables are read together', () => {
    // Not a tidiness assertion. Part 178 compares the affordance shape against the decision shape on the
    // same models; a drifted role table would make that comparison wrong rather than noisy, and nothing in
    // either sweep's output would say so. Hence one table, imported, and this test to keep it one.
    assert.equal(ROLES, DECISION_ROLES);
  });

  it('claims its OWN four ports and none that another harness owns', () => {
    assert.deepEqual(specs.map((s) => s.port).sort(), [8160, 8161, 8162, 8163]);
    // 8140-8144 memory-contention, 8147 rerank-screen, 8150-8153 memory-decision, 8090 a sibling tool.
    for (const port of Object.values(PORTS)) {
      assert.ok(port < 8140 || port > 8153, `${port} collides with a neighbouring harness`);
      assert.ok(port !== 8090);
      assert.ok(!Object.values(DECISION_PORTS).includes(port));
    }
  });

  it('does not embed a machine path — the caller supplies the model directory', () => {
    for (const spec of specs) for (const arg of spec.argv) assert.ok(!/^[A-Za-z]:\\/.test(arg), arg);
  });
});

describe('envFor', () => {
  it('names all four roles, so the bench inherits no default pointing at another harness', () => {
    const env = envFor();
    assert.deepEqual(Object.keys(env).sort(), [
      'LYNTAI_LIVE_CHAT_MODEL', 'LYNTAI_LIVE_CHAT_URL',
      'LYNTAI_LIVE_EMBED_MODEL', 'LYNTAI_LIVE_MODEL_URL',
      'LYNTAI_LIVE_RERANK_MODEL', 'LYNTAI_LIVE_RERANK_URL',
      'LYNTAI_LIVE_SMALL_MODEL', 'LYNTAI_LIVE_SMALL_URL',
    ]);
    assert.equal(env.LYNTAI_LIVE_CHAT_URL, `http://127.0.0.1:${PORTS.chat}`);
    assert.equal(env.LYNTAI_LIVE_SMALL_URL, `http://127.0.0.1:${PORTS.small}`);
  });
});

describe('GPU headroom — the check that turns a silent death into a refusal', () => {
  it('reads used and total off either nvidia-smi unit form', () => {
    assert.deepEqual(parseGpuMemory('2251 MiB, 12282 MiB'), { usedMiB: 2251, totalMiB: 12282 });
    assert.deepEqual(parseGpuMemory('2251, 12282'), { usedMiB: 2251, totalMiB: 12282 });
  });

  it('reports NULL when nvidia-smi said nothing usable — "cannot verify" is not "verified clean"', () => {
    // Load-bearing: the caller SKIPS on null and REFUSES on false, so a blank that read as zero-used would
    // claim 12 GB free on a machine with no GPU at all.
    assert.equal(parseGpuMemory(''), null);
    assert.equal(parseGpuMemory('N/A'), null);
    assert.equal(parseGpuMemory('2251 MiB'), null);
  });

  it('refuses one MiB short and accepts exactly enough', () => {
    // Written against the BOUNDARY rather than against an arbitrary "busy" number, so re-deriving
    // NEEDED_FREE_MIB from a better measurement does not silently invalidate the test — which is what
    // happened when the constant was first set from a CRASHED run and had to come down by 24%.
    const totalMiB = 12282;
    assert.equal(hasHeadroom({ usedMiB: totalMiB - NEEDED_FREE_MIB, totalMiB }, NEEDED_FREE_MIB), true);
    assert.equal(hasHeadroom({ usedMiB: totalMiB - NEEDED_FREE_MIB + 1, totalMiB }, NEEDED_FREE_MIB), false);
  });

  it('needs less than this device holds, or the harness could never run at all', () => {
    assert.ok(NEEDED_FREE_MIB > 0 && NEEDED_FREE_MIB < 12282);
  });
});

describe('parseArgs', () => {
  it('keeps its own flag out of the bench args and forwards everything else verbatim', () => {
    const { skipBuild, benchArgs } = parseArgs(['--skip-build', '--n', '60', '--difficulty', 'easy']);
    assert.equal(skipBuild, true);
    assert.deepEqual(benchArgs, ['--n', '60', '--difficulty', 'easy']);
  });

  it('forwards a flag this module has never heard of, so the bench can grow one without an edit here', () => {
    assert.deepEqual(parseArgs(['--dump']).benchArgs, ['--dump']);
    assert.equal(parseArgs(['--dump']).skipBuild, false);
  });
});
