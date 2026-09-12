import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';

import { DEFAULT_PORT as EMBED_SCREEN_PORT } from '../embed-screen.mjs';
import { PORTS, envFor, parseArgs, serverSpecs } from '../locomo-pair.mjs';
import { PORTS as CONTENTION_PORTS } from '../memory-contention.mjs';
import { PORTS as DECISION_PORTS } from '../memory-decision.mjs';
import {
  EXTRA_PORT_BASE, MAX_EXTRA_ARMS, NATIVE_PORT, PORTS as AFFORDANCE_PORTS,
} from '../tool-affordance.mjs';

describe('serverSpecs', () => {
  const specs = serverSpecs('e.gguf', 'c.gguf', '/models');

  it('passes -ngl EXPLICITLY on both roles — the server default is not neutral and logs no offload line', () => {
    for (const spec of specs) {
      const at = spec.argv.indexOf('-ngl');
      assert.ok(at >= 0, `${spec.label} does not pass -ngl`);
      assert.equal(spec.argv[at + 1], '99');
    }
  });

  it('gives the EMBEDDER --embeddings and the reader none — the flag is PROCESS-WIDE', () => {
    const flags = Object.fromEntries(specs.map((s) => [s.label, s.argv]));
    assert.ok(flags.embed.includes('--embeddings'));
    // A chat server carrying it answers 501 to a completion, which surfaces long after startup.
    assert.ok(!flags.chat.includes('--embeddings'));
  });

  it('sizes the embedder batch to its context, so a ~6,000-character turn is not rejected at request time', () => {
    const embed = specs.find((s) => s.label === 'embed');
    for (const flag of ['--ctx-size', '--batch-size', '--ubatch-size'])
      assert.equal(embed.argv[embed.argv.indexOf(flag) + 1], '2048');
  });

  it('binds 127.0.0.1, never localhost — ::1 first costs ~1.8s per call on an IPv4-only listener', () => {
    for (const spec of specs) assert.ok(spec.argv.includes('127.0.0.1'));
    for (const url of Object.values(envFor()).filter((v) => v.startsWith('http')))
      assert.match(url, /^http:\/\/127\.0\.0\.1:\d+$/);
  });

  it('does not embed a machine path — the caller supplies the model directory', () => {
    for (const spec of specs) for (const arg of spec.argv) assert.ok(!/^[A-Za-z]:\\/.test(arg), arg);
  });

  it('claims ports no neighbouring harness owns, and never 8090', () => {
    // Binding a busy port fails UPWARD: the incumbent answers and every figure is taken on its model.
    const taken = [
      ...Object.values(CONTENTION_PORTS), 8147, ...Object.values(DECISION_PORTS),
      ...Object.values(AFFORDANCE_PORTS), NATIVE_PORT, EMBED_SCREEN_PORT, 8090,
      ...Array.from({ length: MAX_EXTRA_ARMS }, (_, i) => EXTRA_PORT_BASE + i),
    ];
    for (const port of Object.values(PORTS)) assert.ok(!taken.includes(port), `${port} collides`);
  });
});

describe('envFor', () => {
  it('names the embedder and the reader separately, so a pair is an ARGUMENT not ambient state', () => {
    // The whole point of this harness: without it `memory-locomo` takes whatever is listening.
    assert.deepEqual(Object.keys(envFor()).sort(), [
      'LYNTAI_LIVE_CHAT_MODEL', 'LYNTAI_LIVE_CHAT_URL',
      'LYNTAI_LIVE_EMBED_MODEL', 'LYNTAI_LIVE_MODEL_URL',
    ]);
    assert.equal(envFor().LYNTAI_LIVE_MODEL_URL, `http://127.0.0.1:${PORTS.embed}`);
    assert.equal(envFor().LYNTAI_LIVE_CHAT_URL, `http://127.0.0.1:${PORTS.chat}`);
  });

  it('leaves the reader variables ABSENT when no reader was asked for', () => {
    // Absent rather than empty: the bench then refuses loudly instead of inheriting its own default,
    // which spells `localhost` and would point at whatever else happened to be listening.
    const env = envFor({ chat: false });
    assert.equal(Object.hasOwn(env, 'LYNTAI_LIVE_CHAT_URL'), false);
    assert.equal(env.LYNTAI_LIVE_MODEL_URL, `http://127.0.0.1:${PORTS.embed}`);
  });

  it('points the embedder at an EXTERNAL url when one was given', () => {
    assert.equal(envFor({ embedUrl: 'http://127.0.0.1:8180' }).LYNTAI_LIVE_MODEL_URL,
      'http://127.0.0.1:8180');
  });
});

describe('only what is CALLED gets served', () => {
  it('serves no reader for a mode that does not read', () => {
    // `memory-locomo --retrieval` scores a model-free metric. Serving 2.5 GB of chat weights for it is
    // the waste `tool-affordance --scorers-only` had to shed before it could start on a busy device.
    const specs = serverSpecs('e.gguf', null, '/models');
    assert.deepEqual(specs.map((s) => s.label), ['embed']);
  });

  it('serves NOTHING when the embedder is an external endpoint and no reader was asked for', () => {
    // A static model2vec embedder has no GGUF, so this harness cannot start one at all.
    assert.deepEqual(serverSpecs(null, null, '/models'), []);
  });

  it('refuses --embed together with --embed-endpoint rather than silently preferring one', () => {
    assert.throws(() => parseArgs(['--embed', 'e.gguf', '--embed-endpoint', 'http://x']), /not both/);
  });

  it('parses an endpoint embedder and an absent reader', () => {
    const o = parseArgs(['--embed-endpoint', 'http://127.0.0.1:8180', '--', '--retrieval']);
    assert.equal(o.embedEndpoint, 'http://127.0.0.1:8180');
    assert.equal(o.embed, null);
    assert.equal(o.chat, null);
    assert.deepEqual(o.benchArgs, ['--retrieval']);
  });
});

describe('parseArgs', () => {
  it('forwards everything after a bare -- verbatim, so a new bench flag needs no edit here', () => {
    const o = parseArgs(['--embed', 'e.gguf', '--chat', 'c.gguf', '--', '--n', '60', '--arms', 'vector']);
    assert.equal(o.embed, 'e.gguf');
    assert.equal(o.chat, 'c.gguf');
    assert.deepEqual(o.benchArgs, ['--n', '60', '--arms', 'vector']);
  });

  it('reports a missing pair as null rather than guessing one', () => {
    const o = parseArgs(['--embed', 'e.gguf']);
    assert.equal(o.chat, null);
    assert.deepEqual(o.benchArgs, []);
  });
});
