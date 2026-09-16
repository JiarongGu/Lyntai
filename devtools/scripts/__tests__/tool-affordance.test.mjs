import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';

import { DEFAULT_PORT as EMBED_SCREEN_PORT } from '../embed-screen.mjs';
import { ROLES as DECISION_ROLES, PORTS as DECISION_PORTS } from '../memory-decision.mjs';
import {
  EXTRA_PORT_BASE, MAX_EXTRA_ARMS, NATIVE_PORT, NEEDED_FREE_MIB, PORTS, ROLES,
  SCORERS_ONLY_FREE_MIB, armsEnv, envFor,
  extraSpecs, hasHeadroom, nativeSpecs, parseArgs, parseGpuMemory, serverSpecs,
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
    // Not a tidiness assertion. docs/task-archive.md Part 236 compares the affordance shape against the
    // decision shape on the same models; a drifted role table would make that comparison wrong rather than
    // noisy, and nothing in either sweep's output would say so. Hence one table, imported, and this test to
    // keep it one.
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

  it('takes repeatable --embed-arm label=file and keeps BOTH halves out of the bench args', () => {
    // The flag AND its value: leaving the value behind would reach the C# sweep as a bare positional
    // and be read by nothing, which compiles and runs and silently measures the wrong configuration.
    const o = parseArgs(['--embed-arm', 'minilm=a.gguf', '--embed-arm', 'bgezh=b.gguf', '--n', '20']);
    assert.deepEqual(o.embedArms, [
      { label: 'minilm', file: 'a.gguf' }, { label: 'bgezh', file: 'b.gguf' },
    ]);
    assert.deepEqual(o.benchArgs, ['--n', '20']);
  });

  it('rejects an --embed-arm that is not label=file rather than guessing one half', () => {
    assert.throws(() => parseArgs(['--embed-arm', 'a.gguf']), /label=file/);
    assert.throws(() => parseArgs(['--embed-arm', '=a.gguf']), /label=file/);
  });
});

describe('extra embedder arms — vary the SCORING, never the trial construction', () => {
  const arms = [{ label: 'minilm', file: 'a.gguf' }, { label: 'bgezh', file: 'b.gguf' }];

  it('gives each arm its own port, inside this harness and outside every neighbour', () => {
    const specs = extraSpecs(arms, '/models');
    assert.deepEqual(specs.map((s) => s.port), [EXTRA_PORT_BASE, EXTRA_PORT_BASE + 1]);
    const taken = [
      ...Object.values(PORTS), ...Object.values(DECISION_PORTS), EMBED_SCREEN_PORT, 8090,
    ];
    for (const spec of specs) {
      assert.ok(!taken.includes(spec.port), `${spec.port} collides with a neighbouring harness`);
      assert.ok(spec.port < 8140 || spec.port > 8153, `${spec.port} collides`);
    }
  });

  it('refuses more arms than it has ports, rather than overrunning into another harness', () => {
    // Silently allocating past the block would bind a port memory-decision or embed-screen owns, and a
    // busy port fails UPWARD: the incumbent answers and every figure is taken on the wrong model.
    const many = Array.from({ length: MAX_EXTRA_ARMS + 1 }, (_, i) => ({ label: `m${i}`, file: 'x.gguf' }));
    assert.throws(() => extraSpecs(many, '/models'), /at most/);
    assert.ok(EXTRA_PORT_BASE + MAX_EXTRA_ARMS - 1 < EMBED_SCREEN_PORT);
  });

  it('passes --embeddings and an explicit -ngl, like every other role here', () => {
    for (const spec of extraSpecs(arms, '/models')) {
      assert.ok(spec.argv.includes('--embeddings'));
      assert.equal(spec.argv[spec.argv.indexOf('-ngl') + 1], '99');
      assert.ok(spec.argv.includes('127.0.0.1'));
    }
  });

  it('does NOT pass --pooling, so each model is served as its own GGUF declares', () => {
    // Measured 2026-09-12 by `embed-screen --pooling mean,cls`: all-MiniLM-L6-v2 under the wrong mode
    // loses 45% of its cosine range. The conversions carry the trained mode, so forcing one here would
    // make the table a property of this argv rather than of the models.
    for (const spec of extraSpecs(arms, '/models')) assert.ok(!spec.argv.includes('--pooling'));
  });

  it('names every arm in LYNTAI_LIVE_EMBED_ARMS as label=url on 127.0.0.1', () => {
    assert.equal(armsEnv(arms), `minilm=http://127.0.0.1:${EXTRA_PORT_BASE},`
      + `bgezh=http://127.0.0.1:${EXTRA_PORT_BASE + 1}`);
  });

  it('carries an ALREADY-RUNNING endpoint arm beside the ones it starts', () => {
    // A model2vec/potion static embedder has no GGUF, so this harness cannot start one — but the BENCH
    // only ever wanted a URL. Endpoints append after the served arms so the port arithmetic above is
    // untouched, which is what keeps a spawned arm's port independent of how many endpoints were named.
    const eps = [{ label: 'potion8M', url: 'http://127.0.0.1:8180' }];
    assert.equal(armsEnv(arms, eps), `minilm=http://127.0.0.1:${EXTRA_PORT_BASE},`
      + `bgezh=http://127.0.0.1:${EXTRA_PORT_BASE + 1},potion8M=http://127.0.0.1:8180`);
    assert.equal(armsEnv([], eps), 'potion8M=http://127.0.0.1:8180');
  });

  it('parses --embed-endpoint label=url and keeps both halves out of the bench args', () => {
    const o = parseArgs(['--embed-endpoint', 'potion8M=http://127.0.0.1:8180', '--n', '20']);
    assert.deepEqual(o.embedEndpoints, [{ label: 'potion8M', url: 'http://127.0.0.1:8180' }]);
    assert.deepEqual(o.benchArgs, ['--n', '20']);
    assert.throws(() => parseArgs(['--embed-endpoint', 'http://x']), /label=url/);
  });

  it('does NOT count an endpoint against the spawned-arm port budget', () => {
    // The cap exists because ports run out, and an endpoint consumes none of them.
    const many = Array.from({ length: MAX_EXTRA_ARMS }, (_, i) => ({ label: `m${i}`, file: 'x.gguf' }));
    assert.equal(extraSpecs(many, '/models').length, MAX_EXTRA_ARMS);
    const eps = [{ label: 'e', url: 'http://127.0.0.1:9999' }];
    assert.ok(armsEnv(many, eps).endsWith('e=http://127.0.0.1:9999'));
  });

  it('sets NOTHING when there are no arms, so the bench sees an absent variable not an empty one', () => {
    assert.equal(armsEnv([]), null);
    assert.equal(Object.hasOwn(envFor([]), 'LYNTAI_LIVE_EMBED_ARMS'), false);
    assert.equal(envFor(arms).LYNTAI_LIVE_EMBED_ARMS, armsEnv(arms));
  });

  it('gives the TOOL-CAPABLE model a port no arm and no neighbour owns', () => {
    // Binding a busy port fails UPWARD — the incumbent answers — so a collision here would measure the
    // native transport against whichever model happened to be on that port.
    const taken = [
      ...Object.values(PORTS), ...Object.values(DECISION_PORTS), EMBED_SCREEN_PORT, 8090,
      ...Array.from({ length: MAX_EXTRA_ARMS }, (_, i) => EXTRA_PORT_BASE + i),
    ];
    assert.ok(!taken.includes(NATIVE_PORT), `${NATIVE_PORT} collides`);
    assert.ok(NATIVE_PORT < 8140 || NATIVE_PORT > 8153, `${NATIVE_PORT} collides`);
  });

  it('serves the tool-capable model WITHOUT --jinja, because the probe refuted that dependency', () => {
    // Measured 2026-09-13: --jinja is byte-irrelevant on this build for a model whose template carries a
    // tool section AND for one whose does not. Passing it would imply a dependency that does not exist.
    const [spec] = nativeSpecs('qwen.gguf', '/models');
    assert.ok(!spec.argv.includes('--jinja'));
    assert.equal(spec.argv[spec.argv.indexOf('-ngl') + 1], '99');
    assert.ok(spec.argv.includes('127.0.0.1'));
    assert.equal(spec.port, NATIVE_PORT);
  });

  it('serves NOTHING when no tool-capable model was named', () => {
    assert.deepEqual(nativeSpecs(null, '/models'), []);
    assert.equal(Object.hasOwn(envFor([], null), 'LYNTAI_LIVE_NATIVE_URL'), false);
    assert.equal(envFor([], 'q.gguf').LYNTAI_LIVE_NATIVE_URL, `http://127.0.0.1:${NATIVE_PORT}`);
  });

  it('leaves LYNTAI_LIVE_MODEL_URL pointing at the PRIMARY embedder — it builds the trials', () => {
    // The arms must not be able to move trial construction. Distractors are ordered by cosine against
    // the request, so a different constructing embedder gives a different roster and the runs stop
    // being comparable — which is the one way this measurement could quietly answer another question.
    assert.equal(envFor(arms).LYNTAI_LIVE_MODEL_URL, `http://127.0.0.1:${PORTS.embed}`);
  });
});

describe('--scorers-only must be CHEAP, or the mode defeats itself', () => {
  it('serves only the embedder and the reranker — the chat models are never called', () => {
    // Measured cost of not doing this: the mode refused to start behind the VRAM gate on a merely busy
    // GPU, because it was serving ~3.3 GB of chat weights nothing in it reads.
    const labels = serverSpecs('/models', true).map((s) => s.label).sort();
    assert.deepEqual(labels, ['embed', 'rerank']);
    // The reranker stays because it IS a scoring arm; dropping it would drop a measured column.
    assert.ok(serverSpecs('/models', true).some((s) => s.argv.includes('--reranking')));
  });

  it('still serves all four by default, so the full grid is unchanged', () => {
    assert.equal(serverSpecs('/models').length, 4);
    assert.equal(serverSpecs('/models', false).length, 4);
  });

  it('asks for LESS headroom in that mode, and both figures stay under this device', () => {
    assert.ok(SCORERS_ONLY_FREE_MIB < NEEDED_FREE_MIB);
    assert.ok(SCORERS_ONLY_FREE_MIB > 0 && NEEDED_FREE_MIB < 12282);
  });
});
