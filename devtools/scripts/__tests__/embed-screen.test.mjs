import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';

import { PORTS as CONTENTION_PORTS } from '../memory-contention.mjs';
import { PORTS as DECISION_PORTS } from '../memory-decision.mjs';
import { DEFAULT_PORT as RERANK_PORT } from '../rerank-screen.mjs';
import { PORTS as AFFORDANCE_PORTS } from '../tool-affordance.mjs';
import {
  COLLAPSED_RANGE, DEFAULT_PORT, FIXTURE, HEALTH, MIN_HEALTH_GAP, cosine, evaluateFixture,
  evaluateHealth, parseArgs, parseEmbedding, serverSpec,
} from '../embed-screen.mjs';

/** Unit vectors on a 3-space, so every expected cosine is exact rather than approximate. */
const AXIS = { x: [1, 0, 0], y: [0, 1, 0], xy: [1, 1, 0] };

describe('cosine', () => {
  it('is 1 for a vector against itself and 0 for an orthogonal one', () => {
    assert.equal(cosine(AXIS.x, AXIS.x), 1);
    assert.equal(cosine(AXIS.x, AXIS.y), 0);
  });

  it('ignores MAGNITUDE, so an unnormalised server and a normalised one agree', () => {
    // llama-server does not promise L2-normalised output and the two candidate families differ.
    // A screen that compared dot products would score the same model differently per server build.
    assert.equal(cosine(AXIS.x, [5, 0, 0]), 1);
    assert.ok(Math.abs(cosine(AXIS.x, AXIS.xy) - Math.SQRT1_2) < 1e-12);
  });

  it('returns 0 rather than NaN for a zero vector — a dead embedder must not read as perfect', () => {
    // A zero vector divides to NaN, and NaN fails every comparison silently: `NaN > crossMax` is
    // false, so a model returning zeros would report "not separated" for the right reason by luck
    // and "range 0" as NaN. Zero is the honest answer and the collapse check then catches it.
    assert.equal(cosine([0, 0, 0], AXIS.x), 0);
    assert.equal(cosine([0, 0, 0], [0, 0, 0]), 0);
  });
});

describe('FIXTURE — the discriminating set, not an easy one', () => {
  it('pairs topics that share almost no WORDS, and separates topics that share many', () => {
    // This is the embedder form of the reranker REFERENCE pair. An easy fixture — one answer against
    // unrelated noise — passed a reranker that ranked a published pair backwards, because lexical
    // overlap did the separating. Here the within-pair sentences are lexically far apart and two
    // DIFFERENT pairs share vocabulary, so a model reduced to bag-of-words fails rather than coasts.
    const words = (s) => new Set(s.toLowerCase().match(/[a-z]+/g));
    const overlap = (a, b) => [...words(a)].filter((w) => words(b).has(w)).length;
    const stop = new Set(['the', 'a', 'an', 'of', 'on', 'in', 'to', 'and', 'at', 'its', 'it', 'for']);
    const content = (a, b) => [...words(a)].filter((w) => words(b).has(w) && !stop.has(w)).length;

    for (const pair of FIXTURE.pairs)
      assert.ok(content(pair.texts[0], pair.texts[1]) <= 1,
        `within-pair "${pair.topic}" shares too much vocabulary to test meaning: `
        + `${overlap(pair.texts[0], pair.texts[1])} words`);

    // And at least one CROSS-pair must share content words, or nothing traps a lexical model.
    const crossOverlaps = [];
    for (let i = 0; i < FIXTURE.pairs.length; i++)
      for (let j = i + 1; j < FIXTURE.pairs.length; j++)
        for (const a of FIXTURE.pairs[i].texts)
          for (const b of FIXTURE.pairs[j].texts) crossOverlaps.push(content(a, b));
    assert.ok(Math.max(...crossOverlaps) >= 2,
      'no cross-pair shares content words, so a bag-of-words model would pass this fixture');
  });

  it('holds at least four pairs, so `separated` is 4 assertions against 24 rather than 1 against 1', () => {
    assert.ok(FIXTURE.pairs.length >= 4);
    for (const pair of FIXTURE.pairs) assert.equal(pair.texts.length, 2);
  });

  it('names the LONGEST input the benches emit, so the extreme is probed and not the typical', () => {
    // A four-word probe certified a server that returned HTTP 500 on a real turn. `IEmbedder` is
    // called per WRITE and per RECALL, and the memory benches truncate at ~6,000 characters.
    assert.ok(FIXTURE.longProbe().length >= 6000);
  });
});

describe('evaluateFixture — what separates a working embedder from a plausible one', () => {
  /** Vectors for the fixture's 2N texts, in fixture order, from a per-pair angle. */
  const vectorsFor = (pairAngles, jitter = 0) => pairAngles.flatMap((theta, p) => [
    [Math.cos(theta), Math.sin(theta), 0],
    [Math.cos(theta + jitter), Math.sin(theta + jitter), 0.0001 * p],
  ]);

  it('SEPARATES when every within-pair cosine beats every cross-pair one', () => {
    // Four pairs at 0, 45, 90 and 135 degrees: each pair's two members nearly coincide, and the
    // pairs are far apart. That is what a working embedder looks like on this fixture.
    const v = vectorsFor([0, Math.PI / 4, Math.PI / 2, (3 * Math.PI) / 4], 0.01);
    const r = evaluateFixture(v, FIXTURE);
    assert.equal(r.separated, true);
    assert.ok(r.margin > 0);
    assert.equal(r.collapsed, false);
  });

  it('does NOT separate when a cross-pair cosine beats a within-pair one, and NAMES the confusion', () => {
    // Pairs 0 and 1 placed 0.005 rad apart while pair 0's own members sit 0.5 rad apart: the model
    // links two different topics more tightly than it links one topic to itself. A verdict that only
    // said "false" would send the reader to the wrong half of the run.
    const v = [
      [1, 0, 0], [Math.cos(0.5), Math.sin(0.5), 0],
      [Math.cos(0.005), Math.sin(0.005), 0], [Math.cos(0.01), Math.sin(0.01), 0],
      [0, 1, 0], [Math.cos(Math.PI / 2 + 0.01), Math.sin(Math.PI / 2 + 0.01), 0],
      [0, 0, 1], [0.01, 0, 1],
    ];
    const r = evaluateFixture(v, FIXTURE);
    assert.equal(r.separated, false);
    assert.ok(r.margin < 0);
    assert.ok(r.worstCross, 'a failure must name the two texts it confused');
    assert.equal(r.worstCross.pairs[0], FIXTURE.pairs[0].topic);
    assert.equal(r.worstCross.pairs[1], FIXTURE.pairs[1].topic);
  });

  it('flags COLLAPSE even when the ordering is right — the near-flat case is the dangerous one', () => {
    // The reranker screen learned this the expensive way: distinctness is not discrimination. A model
    // whose every pairwise cosine sits at 0.999 orders the fixture correctly by noise and has no
    // discriminative range left, and `separated` alone would publish it as working.
    const v = vectorsFor([0, 0.002, 0.004, 0.006], 0.0001);
    const r = evaluateFixture(v, FIXTURE);
    assert.equal(r.separated, true, 'ordering is intact — that is the point of this case');
    assert.equal(r.collapsed, true);
    assert.ok(r.range < COLLAPSED_RANGE);
  });

  it('reads a ZERO vector as a failure rather than as a tie', () => {
    const v = Array.from({ length: FIXTURE.pairs.length * 2 }, () => [0, 0, 0]);
    const r = evaluateFixture(v, FIXTURE);
    assert.equal(r.separated, false);
    assert.equal(r.collapsed, true);
  });

  it('refuses a vector count that does not match the fixture, rather than scoring a prefix', () => {
    // A response short by one is a dropped embedding, and silently scoring the rest would report a
    // pass on a model that answered incompletely.
    assert.throws(() => evaluateFixture([[1, 0, 0]], FIXTURE), /8 vectors/);
  });

  it('reports the DIMENSION and flags a mismatch across texts', () => {
    const v = vectorsFor([0, 1, 2, 3], 0.01);
    assert.equal(evaluateFixture(v, FIXTURE).dimension, 3);
    const ragged = [...v];
    ragged[3] = [1, 0];
    assert.equal(evaluateFixture(ragged, FIXTURE).raggedDimensions, true);
  });

  it('does NOT carry a pass/fail — the hard fixture is a sharpness MEASUREMENT, not a verdict', () => {
    // Measured 2026-09-12: the 333,590,944 B control separates this fixture (+0.1634) and all four
    // sub-100 MB candidates do not (-0.06 to -0.13) while being demonstrably healthy — stable vectors,
    // full range, correct dimensions. A screen that failed them would have published "no sub-100 MB
    // embedder works", which is the over-claim in the other direction from the retracted reranker rows.
    // Health is asserted against `HEALTH`; this margin is REPORTED beside the control and nothing else.
    const v = evaluateFixture([[1, 0, 0], [0, 1, 0], [1, 0, 0], [0, 1, 0],
      [1, 0, 0], [0, 1, 0], [1, 0, 0], [0, 1, 0]], FIXTURE);
    assert.equal(Object.hasOwn(v, 'verdict'), false);
    assert.equal(Object.hasOwn(v, 'ok'), false);
    assert.equal(typeof v.margin, 'number');
  });

  it('COLLAPSED_RANGE stays an assertion, because a collapsed span is BREAKAGE rather than bluntness', () => {
    assert.ok(COLLAPSED_RANGE > 0 && COLLAPSED_RANGE < 1);
  });
});

describe('evaluateHealth — the check the backlog item actually specified', () => {
  const vectors = (a, s, u) => [a, s, u];

  it('passes a known-similar pair ordered above a known-unrelated one, by a real gap', () => {
    const r = evaluateHealth(vectors([1, 0, 0], [0.98, 0.199, 0], [0, 1, 0]));
    assert.equal(r.ordered, true);
    assert.ok(r.gap > MIN_HEALTH_GAP);
    assert.equal(r.ok, true);
  });

  it('FAILS when the unrelated text is as close as the paraphrase — that is a broken embedder', () => {
    const r = evaluateHealth(vectors([1, 0, 0], [0, 1, 0], [0.99, 0.141, 0]));
    assert.equal(r.ordered, false);
    assert.equal(r.ok, false);
  });

  it('FAILS on a real ordering with no gap — the near-flat case, again', () => {
    // Ordered by 0.001 is ordered by noise. The health check must not pass what the range check would
    // catch, or a model with one lucky comparison reads as healthy on a screen with no control.
    const r = evaluateHealth(vectors([1, 0, 0], [0.9999, 0.0141, 0], [0.9998, 0.02, 0]));
    assert.equal(r.ordered, true);
    assert.ok(r.gap < MIN_HEALTH_GAP);
    assert.equal(r.ok, false);
  });

  it('HEALTH is EASY on purpose — the paraphrase shares vocabulary with the anchor', () => {
    // The opposite discipline from FIXTURE. This one must pass any working model, so it is allowed the
    // lexical overlap FIXTURE forbids: its job is to catch a dead conversion, not to rank quality.
    const words = (s) => new Set(s.toLowerCase().match(/[a-z]+/g));
    const shared = [...words(HEALTH.anchor)].filter((w) => words(HEALTH.similar).has(w));
    assert.ok(shared.length >= 2, `the health pair shares only ${shared.length} words — too hard`);
    const crossed = [...words(HEALTH.anchor)].filter((w) => words(HEALTH.unrelated).has(w));
    assert.ok(crossed.length <= 1, 'the unrelated text overlaps the anchor and cannot be a control');
  });

  it('MIN_HEALTH_GAP is generous, because a health check that fails a working model is not one', () => {
    assert.ok(MIN_HEALTH_GAP > 0 && MIN_HEALTH_GAP < 0.25);
  });
});

describe('parseEmbedding', () => {
  it('reads the OpenAI shape', () => {
    assert.deepEqual(parseEmbedding({ data: [{ embedding: [0.1, 0.2] }] }), [0.1, 0.2]);
  });

  it('reads llama-server\'s own /embedding shape, which nests under `embedding`', () => {
    // Two spellings answer on one server depending on the route, and a screen that knew only one
    // would report a working model as unusable.
    assert.deepEqual(parseEmbedding([{ embedding: [[0.3, 0.4]] }]), [0.3, 0.4]);
  });

  it('returns NULL for anything unusable rather than a fabricated vector', () => {
    for (const body of [null, {}, { data: [] }, { data: [{ embedding: [] }] },
      { data: [{ embedding: ['x'] }] }, { error: { message: 'nope' } }])
      assert.equal(parseEmbedding(body), null, JSON.stringify(body));
  });
});

describe('serverSpec', () => {
  it('passes --embeddings, and -ngl EXPLICITLY because the default is not neutral', () => {
    const spec = serverSpec({ model: '/m/x.gguf', port: 8170, ngl: 99, ctx: 2048 });
    assert.ok(spec.argv.includes('--embeddings'));
    const at = spec.argv.indexOf('-ngl');
    assert.ok(at >= 0);
    assert.equal(spec.argv[at + 1], '99');
  });

  it('passes --pooling ONLY when asked, so "what the GGUF declares" stays observable', () => {
    // The serving configuration is measured before the model (D107). A screen that always forced a
    // pooling mode could never report what the file itself asks for.
    assert.ok(!serverSpec({ model: '/m/x.gguf', port: 8170 }).argv.includes('--pooling'));
    const spec = serverSpec({ model: '/m/x.gguf', port: 8170, pooling: 'cls' });
    assert.equal(spec.argv[spec.argv.indexOf('--pooling') + 1], 'cls');
  });

  it('sizes the physical batch to the context, so the longest input is not rejected by -ub', () => {
    // A four-word probe passed while a real turn returned 500: the physical batch defaults below the
    // ~1,500 tokens a 6,000-character input costs.
    const spec = serverSpec({ model: '/m/x.gguf', port: 8170, ctx: 4096 });
    for (const flag of ['--ctx-size', '--batch-size', '--ubatch-size'])
      assert.equal(spec.argv[spec.argv.indexOf(flag) + 1], '4096');
  });

  it('binds 127.0.0.1, never localhost', () => {
    assert.ok(serverSpec({ model: '/m/x.gguf', port: 8170 }).argv.includes('127.0.0.1'));
  });

  it('does not embed a machine path of its own — the caller supplies the model', () => {
    const spec = serverSpec({ model: '/m/x.gguf', port: 8170 });
    const mine = spec.argv.filter((a) => /^[A-Za-z]:\\/.test(a));
    assert.deepEqual(mine, []);
  });
});

describe('ports', () => {
  it('claims a port no neighbouring harness owns, and never 8090', () => {
    // 8140-8144 memory-contention, 8147 rerank-screen, 8150-8153 memory-decision,
    // 8160-8163 tool-affordance, 8090 a sibling tool's embedding server across several sessions.
    const taken = [
      ...Object.values(CONTENTION_PORTS), RERANK_PORT,
      ...Object.values(DECISION_PORTS), ...Object.values(AFFORDANCE_PORTS), 8090,
    ];
    assert.ok(!taken.includes(DEFAULT_PORT), `${DEFAULT_PORT} collides with a neighbouring harness`);
  });
});

describe('parseArgs', () => {
  it('takes one or more models, so a survey screens a shortlist in one teardown-checked pass', () => {
    const o = parseArgs(['--model', 'a.gguf', '--model', 'b.gguf']);
    assert.deepEqual(o.models, ['a.gguf', 'b.gguf']);
  });

  it('takes a COMMA LIST of pooling modes — a bge screened under mean measures the config', () => {
    // bge and gte train with CLS pooling and MiniLM with mean. Screening one under the other's mode
    // publishes a property of the serving layer as a property of the model, which is D107's warning.
    assert.deepEqual(parseArgs(['--model', 'a.gguf', '--pooling', 'mean,cls']).poolings, ['mean', 'cls']);
    assert.deepEqual(parseArgs(['--model', 'a.gguf']).poolings, [null],
      'the default must be "whatever the GGUF declares", which is a measurement of its own');
  });

  it('stops --inspect at the next flag rather than harvesting a flag value as a url', () => {
    const o = parseArgs(['--inspect', 'http://a', 'http://b', '--port', '9']);
    assert.deepEqual(o.inspect, ['http://a', 'http://b']);
    assert.equal(o.port, 9);
  });

  it('takes repeatable --endpoint label=url, for an embedder this harness cannot START', () => {
    // A model2vec/potion static embedder has no GGUF in existence, so llama-server cannot serve it and
    // the class was unreachable by every instrument here. An already-running OpenAI-shaped endpoint
    // is the seam that reaches it — and the same door screens Ollama or anything hosted.
    const o = parseArgs(['--endpoint', 'potion-8M=http://127.0.0.1:8180',
      '--endpoint', 'potion-2M=http://127.0.0.1:8181']);
    assert.deepEqual(o.endpoints, [
      { label: 'potion-8M', url: 'http://127.0.0.1:8180' },
      { label: 'potion-2M', url: 'http://127.0.0.1:8181' },
    ]);
  });

  it('rejects an --endpoint that is not label=url rather than guessing one half', () => {
    assert.throws(() => parseArgs(['--endpoint', 'http://127.0.0.1:8180']), /label=url/);
    assert.throws(() => parseArgs(['--endpoint', '=http://x']), /label=url/);
    assert.throws(() => parseArgs(['--endpoint', 'potion=']), /label=url/);
  });

  it('carries --bytes so an endpoint row can still state a SIZE the endpoint does not know', () => {
    // The size column is the whole point of this survey and an HTTP endpoint cannot report its own
    // weight file. Absent is reported as unknown rather than as zero, which would read as a measurement.
    assert.equal(parseArgs(['--endpoint', 'p=http://x', '--bytes', '30236760']).bytes, 30236760);
    assert.equal(parseArgs(['--endpoint', 'p=http://x']).bytes, null);
  });
});
