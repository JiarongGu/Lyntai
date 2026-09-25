// embed-screen — does a candidate GGUF actually WORK as an embedder, before anybody spends a run on it?
//
// The EMBEDDER counterpart of `rerank-screen`, and it exists because the sizing question moved roles:
// llama.cpp PR #21729 blocks a sub-100 MB cross-encoder and does NOT reach a single-sequence embedder
// (`docs/model-tasks.md` §3). So the sub-100 MB survey is now aimed here, and a survey alone is not
// enough — a community GGUF can load and embed nonsense, or be served under the wrong pooling mode.
//
// It ASSERTS health and REPORTS sharpness, and keeping those apart is the whole design. A fixture hard
// enough to rank models fails working ones — measured here, on four healthy sub-100 MB candidates the
// 333,590,944 B control out-separates — so `HEALTH` is an easy pair with a generous threshold and
// `FIXTURE`'s margin is a number read against a `--control`, never a verdict. The reranker screen could
// assert a hard case because it had a PUBLISHED reference score; there is no such number for an embedder.
//
// The rest are traps `.claude/knowledge/pitfalls.md` records: ORDERING is not discrimination, so the
// cosine RANGE is asserted beside it; the serving configuration is measured before the model (D107), so
// `--pooling` is swept rather than assumed; and the EXTREME input is probed, never the typical one —
// which here decides ROLE FIT rather than health, since a 512-position model serves a query fine.
//
// Usage:
//   node dev.mjs embed-screen --model <path.gguf> [--model <path.gguf> …] [--pooling mean,cls]
//                             [--port N] [--ctx N] [--ngl N] [--control <path.gguf>]
//   node dev.mjs embed-screen --inspect <gguf-url> [<gguf-url> …]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  neighbourReport, ownedPids, resolveServerExe, startServers, stopServers, vanishedNeighbours,
} from './memory-contention.mjs';
import { inspectRemote } from './rerank-screen.mjs';

const here = fileURLToPath(import.meta.url);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Port this harness owns. DELIBERATELY outside `memory-contention`'s 8140-8144, `rerank-screen`'s 8147,
 *  `memory-decision`'s 8150-8153 and `tool-affordance`'s 8160-8163 — and nowhere near 8090, which a
 *  sibling tool's embedding server has held across several sessions. */
export const DEFAULT_PORT = 8167;

/** The discriminating fixture: four topics, two sentences each, chosen so that MEANING and SURFACE
 *  disagree. Within a pair the two sentences share no content word; ACROSS the first two pairs they
 *  share `Moon` and `Earth`. A model reduced to bag-of-words therefore links two different topics more
 *  tightly than it links one topic to itself, and fails — where the reranker screen's first fixture (one
 *  answer against unrelated noise) passed a model that ranked a published pair backwards. */
export const FIXTURE = {
  pairs: [
    {
      topic: 'moon-landing',
      texts: [
        'Apollo 11 carried the first humans from Earth to the Moon on 20 July 1969.',
        'Armstrong and Aldrin walked across the lunar surface that summer.',
      ],
    },
    {
      topic: 'lunar-eclipse',
      texts: [
        'A lunar eclipse occurs when the Moon passes into the shadow of the Earth.',
        'Our planet slides between the sun and its satellite, turning the disc deep red.',
      ],
    },
    {
      topic: 'baking',
      texts: [
        'Bread is baked in an oven at around 220 degrees Celsius.',
        'Let the dough rise overnight, then give the loaf a fierce heat until its crust darkens.',
      ],
    },
    {
      topic: 'berlin-population',
      texts: [
        'Berlin had a population of 3,520,031 registered inhabitants.',
        'About three and a half million people live in the German capital.',
      ],
    },
  ],

  /** The longest single input the memory benches can emit — `GraphMemoryOptions`' truncation lets an
   *  entry reach ~6,000 characters. A four-word probe certified a server that returned HTTP 500 on a
   *  real turn, so the probe is the extreme rather than the typical. */
  longProbe: () => 'The Apollo 11 mission. '
    + 'Lunar module descent telemetry and crew transcript. '.repeat(120),
};

/** Every text in fixture order — index `2p` and `2p+1` are pair `p`'s two members. */
export const fixtureTexts = (fixture = FIXTURE) => fixture.pairs.flatMap((p) => p.texts);

/** The HEALTH pair — a known-similar text and a known-unrelated one. EASY on purpose, and allowed the
 *  lexical overlap `FIXTURE` forbids, because this is the check that says whether the conversion works
 *  at all. `FIXTURE` measures how SHARP the model is; the two must not be the same assertion.
 *
 *  Separated 2026-09-12, when `FIXTURE` failed four demonstrably healthy sub-100 MB models. A screen
 *  whose only assertion is a hard one reports "broken" for "blunter than a 7x larger model", which is
 *  the retracted-reranker-row mistake pointed the other way. */
export const HEALTH = {
  anchor: 'The cat slept on the warm windowsill all afternoon.',
  similar: 'A cat dozed in the sunny window for most of the afternoon.',
  unrelated: 'Quarterly revenue rose twelve percent after the merger closed.',
};

export const healthTexts = (health = HEALTH) => [health.anchor, health.similar, health.unrelated];

/** Below this total cosine RANGE the model has no discriminative span left, whatever its ordering.
 *  Measured 2026-09-12: the five screened models span 0.2837 to 0.7647, and the control 0.6431 — so
 *  this sits well under every working model rather than between them, which is what a breakage
 *  threshold must do. Reported on every row regardless, so a wrong constant stays visible. */
export const COLLAPSED_RANGE = 0.15;

/** The minimum gap between the health pair's similar and unrelated cosines. Deliberately GENEROUS: a
 *  health check that fails a working model is not a health check, and the sharpness question is
 *  `FIXTURE`'s. Its job is to separate a live embedder from a dead one, where a dead one gives ~0. */
export const MIN_HEALTH_GAP = 0.10;

/** Magnitude-free, so an L2-normalised server and a raw one agree. Zero for a zero vector: NaN would
 *  fail every comparison silently and read as "not separated" for the wrong reason. */
export function cosine(a, b) {
  let dot = 0, na = 0, nb = 0;
  const n = Math.min(a.length, b.length);
  for (let i = 0; i < n; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
  return na <= 0 || nb <= 0 ? 0 : dot / (Math.sqrt(na) * Math.sqrt(nb));
}

/** How SHARP the model is, as a number — never a verdict. `separated` means every within-pair cosine
 *  beat every cross-pair one, and `range` is the second signal the reranker screen had to learn: a
 *  model whose whole spread is hundredths orders this fixture by noise and passes every ordering check.
 *  Read `margin` against the control's; an absolute threshold on a cosine is not portable across
 *  embedding families, and nothing published gives one. */
export function evaluateFixture(vectors, fixture = FIXTURE) {
  const texts = fixtureTexts(fixture);
  if (!Array.isArray(vectors) || vectors.length !== texts.length)
    throw new Error(`evaluateFixture needs ${texts.length} vectors, got ${vectors?.length}`);

  const dimension = vectors[0]?.length ?? 0;
  const raggedDimensions = vectors.some((v) => v.length !== dimension);

  const within = [], cross = [], all = [];
  for (let i = 0; i < vectors.length; i++)
    for (let j = i + 1; j < vectors.length; j++) {
      const pairI = Math.floor(i / 2), pairJ = Math.floor(j / 2);
      const row = {
        cosine: cosine(vectors[i], vectors[j]),
        pairs: [fixture.pairs[pairI].topic, fixture.pairs[pairJ].topic],
        texts: [texts[i], texts[j]],
      };
      all.push(row);
      (pairI === pairJ ? within : cross).push(row);
    }

  const weakest = within.reduce((a, b) => (b.cosine < a.cosine ? b : a));
  const worstCross = cross.reduce((a, b) => (b.cosine > a.cosine ? b : a));
  const margin = weakest.cosine - worstCross.cosine;
  const values = all.map((r) => r.cosine);
  const range = Math.max(...values) - Math.min(...values);

  return {
    dimension, raggedDimensions, within, cross,
    withinMin: weakest.cosine, crossMax: worstCross.cosine, weakestWithin: weakest, worstCross,
    margin, separated: margin > 0,
    range, collapsed: range < COLLAPSED_RANGE,
  };
}

/** The health verdict from `[anchor, similar, unrelated]`. Both halves are required: ORDERED alone is
 *  satisfied by 0.001 of noise, which is the near-flat case this file already refuses one tier up. */
export function evaluateHealth(vectors) {
  if (!Array.isArray(vectors) || vectors.length !== 3)
    throw new Error(`evaluateHealth needs 3 vectors, got ${vectors?.length}`);
  const similar = cosine(vectors[0], vectors[1]);
  const unrelated = cosine(vectors[0], vectors[2]);
  const gap = similar - unrelated;
  const ordered = gap > 0;
  return { similar, unrelated, gap, ordered, ok: ordered && gap >= MIN_HEALTH_GAP };
}

/** A float vector from either spelling, or NULL when the shape is unusable — never a fabricated one.
 *  `/v1/embeddings` nests under `data[0].embedding`; llama-server's own `/embedding` returns an array
 *  of results whose `embedding` is an array OF arrays. A screen that knew one would report a working
 *  model as unusable. */
export function parseEmbedding(body) {
  const raw = Array.isArray(body) ? body[0]?.embedding : body?.data?.[0]?.embedding;
  const flat = Array.isArray(raw) && Array.isArray(raw[0]) ? raw[0] : raw;
  if (!Array.isArray(flat) || flat.length === 0) return null;
  return flat.every((v) => typeof v === 'number' && Number.isFinite(v)) ? flat : null;
}

/** `{label, port, argv}` — the shape `startServers` takes. `--pooling` is passed ONLY when asked, so
 *  the default row measures what the GGUF itself declares, which is a finding rather than a setting. */
export function serverSpec({ model, port, pooling = null, ctx = 2048, ngl = 99, label = 'embed' }) {
  return {
    label,
    port,
    argv: [
      '--model', model, '--alias', label, '--port', String(port), '--host', '127.0.0.1',
      '--no-webui', '-ngl', String(ngl), '--embeddings',
      // The physical batch is sized to the context: it defaults BELOW the ~1,500 tokens a 6,000
      // character input costs, and the rejection arrives at request time on a server that started clean.
      '--ctx-size', String(ctx), '--batch-size', String(ctx), '--ubatch-size', String(ctx),
      ...(pooling ? ['--pooling', pooling] : []),
    ],
  };
}

export function parseArgs(argv) {
  const all = (name) => argv.reduce((acc, a, i) =>
    (a === `--${name}` && argv[i + 1] && !argv[i + 1].startsWith('--') ? [...acc, argv[i + 1]] : acc), []);
  const at = (name) => all(name).at(-1) ?? null;

  // STOP at the next flag rather than filtering flags out: `--inspect a b --port 9` must not harvest
  // `9` as a third url. Filtering would silently accept a flag's VALUE as one.
  const inspectAt = argv.indexOf('--inspect');
  const inspect = [];
  if (inspectAt >= 0) for (const a of argv.slice(inspectAt + 1)) {
    if (a.startsWith('--')) break;
    inspect.push(a);
  }

  // `label=url` for an embedder this harness cannot START — a static model2vec/potion model has no GGUF
  // in existence, so llama-server cannot serve it and the class was unreachable by every instrument here.
  const endpoints = all('endpoint').map((spec) => {
    const eq = spec.indexOf('=');
    if (eq <= 0 || eq === spec.length - 1)
      throw new Error(`embed-screen: --endpoint wants label=url, got "${spec}"`);
    return { label: spec.slice(0, eq), url: spec.slice(eq + 1) };
  });

  const pooling = at('pooling');
  return {
    inspect,
    endpoints,
    // An endpoint cannot report its own weight file, and the size column is the point of this survey.
    // Null rather than 0: absent must read as unknown, never as a measurement.
    bytes: at('bytes') ? Number(at('bytes')) : null,
    models: all('model'),
    control: at('control'),
    // `[null]` is "whatever the GGUF declares", which is itself a measurement — see `serverSpec`.
    poolings: pooling ? pooling.split(',').map((p) => p.trim()).filter(Boolean) : [null],
    port: Number(at('port') ?? DEFAULT_PORT),
    ctx: Number(at('ctx') ?? 2048),
    ngl: Number(at('ngl') ?? 99),
    server: at('server'),
  };
}

// ── the screen ─────────────────────────────────────────────────────────────────────────────────────

const postJson = async (url, body, timeoutMs = 180_000) => {
  const res = await fetch(url, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body), signal: AbortSignal.timeout(timeoutMs),
  });
  return { status: res.status, body: await res.json().catch(() => null) };
};

/** What the port actually loaded, read back from the process rather than assumed from the argv: a
 *  `--model`-started `llama-server` answers to whatever name it is asked, so the requested one proves
 *  nothing. `null` on a build with no `/props` — reported as unknown, never as agreement. */
async function servedFile(baseUrl) {
  try {
    const res = await fetch(`${baseUrl}/props`, { signal: AbortSignal.timeout(10_000) });
    const props = await res.json().catch(() => null);
    const raw = props?.model_path ?? props?.default_generation_settings?.model ?? null;
    return typeof raw === 'string' && raw.length > 0 ? path.basename(raw.replace(/\\/g, '/')) : null;
  } catch { return null; }
}

/** Embed one text through `/v1/embeddings`, returning `{vector, status}`; `vector` is null on anything
 *  unusable so the caller can tell a rejection from a bad shape. */
async function embed(baseUrl, text) {
  const { status, body } = await postJson(`${baseUrl}/v1/embeddings`,
    { model: 'embed', input: text });
  return { status, vector: parseEmbedding(body), detail: JSON.stringify(body ?? {}).slice(0, 200) };
}

/** One cell: start (unless the endpoint is already up), screen, tear down, re-read.
 *
 *  <b>`endpoint` is the mode for an embedder this harness cannot START.</b> A static model2vec/potion
 *  model has no GGUF in existence, so `llama-server` cannot serve it and the whole class was unreachable
 *  by every instrument here. Given an already-running OpenAI-shaped URL the SAME fixture, the same
 *  assertions and the same control comparison apply — only the process lifecycle differs. */
async function screenOne({ model, pooling, opts, serverExe, scratchDir, isControl, endpoint }) {
  const manage = !endpoint;
  const label = endpoint ? endpoint.label : path.basename(model);
  // An endpoint cannot report its own weight file. `--bytes` supplies it; absent stays UNKNOWN rather
  // than 0, because a zero in the size column of a sizing survey reads as a measurement.
  const bytes = endpoint ? opts.bytes : fs.statSync(model).size;
  const baseUrl = endpoint ? endpoint.url : `http://127.0.0.1:${opts.port}`;
  const row = { label, model, pooling, bytes, isControl, endpoint: !!endpoint, checks: [], verdict: null };
  const record = (name, ok, detail) => {
    row.checks.push({ name, ok });
    console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
  };

  console.log(`\n=== embed-screen: ${label}  `
    + (endpoint ? `endpoint ${endpoint.url}` : `pooling=${pooling ?? '(as declared)'}`)
    + `${isControl ? '  [CONTROL]' : ''} ===`);
  // Both units, always: they straddle round thresholds and a sweep once mis-sorted candidates by 27%.
  console.log(bytes === null || bytes === undefined
    ? '  bytes   : UNKNOWN — pass --bytes to state it; an endpoint cannot report its own weight file'
    : `  bytes   : ${bytes}  (${(bytes / 1024 / 1024).toFixed(2)} MiB | ${(bytes / 1e6).toFixed(2)} MB)`);

  let pids = [];
  try {
    if (manage) {
      const spec = serverSpec({ model, port: opts.port, pooling, ctx: opts.ctx, ngl: opts.ngl });
      try {
        pids = await startServers([spec], { scratchDir, serverExe });
      } catch (err) {
        record('server starts', false, err.message);
        return row;
      }
      record('server starts', true);
    } else {
      // Not "assume it is up": an unreachable endpoint must FAIL here rather than surface as a model
      // that embeds nothing, which is the same conflation the vector checks below exist to prevent.
      const probe = await embed(baseUrl, 'probe').catch(() => ({ vector: null, status: 0, detail: 'unreachable' }));
      record('the endpoint answers', probe.vector !== null,
        probe.vector ? `${baseUrl} (dim ${probe.vector.length})` : `${baseUrl}: ${probe.detail}`);
      if (probe.vector === null) return row;
    }

    // IDENTITY, and the two modes can assert different amounts of it. For a GGUF the label IS the file
    // this screen told the server to load, so equality is a real check. For an ENDPOINT the label is
    // whatever the caller typed, so asserting equality tests the caller's spelling rather than the
    // server — it failed a perfectly good endpoint the first time. What is still worth asserting there
    // is that the endpoint NAMES something, so a human can see which server answered; an endpoint that
    // names nothing is reported as unverifiable rather than as agreement.
    const served = await servedFile(baseUrl);
    row.served = served;
    if (manage)
      record('the port serves the file this screen asked for', served === null || served === label,
        served === null ? 'unknown — this build exposes no /props' : `serving ${served}`);
    else
      record('the endpoint NAMES what it serves, so a wrong server is visible', served !== null,
        served === null
          ? 'this endpoint names no model — identity cannot be verified from here'
          : `serving ${served} (asked for ${label}; the label is yours, so this is REPORTED not asserted)`);

    const texts = fixtureTexts();
    const vectors = [];
    for (const text of texts) {
      const { status, vector, detail } = await embed(baseUrl, text);
      if (!vector) {
        record('POST /v1/embeddings returns a usable vector', false, `HTTP ${status}: ${detail}`);
        return row;
      }
      vectors.push(vector);
    }
    record('POST /v1/embeddings returns a usable vector', true, `${vectors.length} texts`);

    const v = evaluateFixture(vectors);
    row.evaluation = v;
    record('every vector has the SAME dimension', !v.raggedDimensions, `dim ${v.dimension}`);

    // HEALTH — the assertions. Is this a working embedder at all?
    const healthVectors = [];
    for (const text of healthTexts()) {
      const { vector } = await embed(baseUrl, text);
      if (vector) healthVectors.push(vector);
    }
    const h = healthVectors.length === 3 ? evaluateHealth(healthVectors) : null;
    row.health = h;
    record('a known-SIMILAR text outranks a known-UNRELATED one, by a real gap', h?.ok === true,
      h === null ? 'the health texts did not embed'
        : `similar ${h.similar.toFixed(4)} vs unrelated ${h.unrelated.toFixed(4)}, gap `
          + `${h.gap.toFixed(4)} (needs ${MIN_HEALTH_GAP})`);
    record('the cosine RANGE has not collapsed', !v.collapsed,
      `range ${v.range.toFixed(4)} vs ${COLLAPSED_RANGE} — ordering is not discrimination, and a model `
      + 'whose whole spread is hundredths orders a fixture by noise');

    // SHARPNESS — a number, not a verdict. Every sub-100 MB candidate screened on 2026-09-12 was
    // healthy AND failed to separate this fixture, which the control separates: that is bluntness
    // rather than breakage, and recording it as a FAIL would have published the wrong conclusion.
    console.log(`  SHARPNESS (reported, not asserted) — the hard fixture:`);
    for (const pair of v.within)
      console.log(`     within  ${pair.cosine.toFixed(4)}  ${pair.pairs[0]}`);
    console.log(`     cross   ${v.crossMax.toFixed(4)}  worst: ${v.worstCross.pairs.join(' / ')}`);
    console.log(`     margin  ${v.margin >= 0 ? '+' : ''}${v.margin.toFixed(4)}  `
      + `${v.separated ? 'separates' : 'CONFLATES'} — read against the control's, never an absolute`);

    const long = FIXTURE.longProbe();
    const extreme = await embed(baseUrl, long);
    row.longOk = extreme.vector !== null;
    // ROLE FIT, and it is a real disqualification for one role and irrelevant to another — so it is
    // reported with its consequence rather than folded into a single verdict. A 512-position model
    // cannot serve the memory engine's vectors, embedded per WRITE and per RECALL over ~6,000-character entries.
    console.log(`  ROLE FIT  : ${long.length} chars -> HTTP ${extreme.status}`
      + (extreme.vector
        ? ` — accepts the longest input the benches emit (dim ${extreme.vector.length})`
        : ` — REJECTED: ${extreme.detail.slice(0, 140)}`));
    if (!row.longOk)
      console.log('              short-input roles only (tool routing, a query); NOT the memory embedder');

    // REPEATABILITY, not determinism. The seam consumes an ORDER, so assert the vector is stable
    // enough that a near-tie cannot flip and REPORT the drift rather than asserting bitwise equality.
    const again = await embed(baseUrl, texts[0]);
    const drift = again.vector ? 1 - cosine(vectors[0], again.vector) : NaN;
    record('a repeated call returns the same vector', Number.isFinite(drift) && drift < 1e-6,
      `1 - cos = ${Number.isFinite(drift) ? drift.toExponential(2) : 'unusable'}`);
  } finally {
    // Unconditional, not `if (pids.length)`: a start that THREW is the most likely moment to have
    // leaked one, so the survivor re-read must run on that path above all. An ENDPOINT row started
    // nothing and must tear down nothing — killing a server this screen did not start is the image-name
    // mistake `.claude/knowledge/pitfalls.md` records, aimed at a port instead of a process name.
    const { survivors } = manage ? await stopServers(pids, [opts.port]) : { survivors: [] };
    if (survivors.length) {
      console.error(`  *** PORT STILL LISTENING after teardown: ${JSON.stringify(survivors)} — kill by PID ***`);
      row.leaked = true;
    }
    await sleep(500);
  }

  const failed = row.checks.filter((c) => !c.ok);
  row.verdict = failed.length === 0 ? 'HEALTHY' : 'FAIL';
  console.log(`  ${row.checks.length - failed.length}/${row.checks.length} checks passed`
    + (failed.length ? ` — FAILED: ${failed.map((f) => f.name).join(', ')}` : ''));
  return row;
}

/** The summary table. Its whole job is to put a candidate's margin BESIDE the control's: an absolute
 *  threshold on a cosine margin is not portable across embedding families, and "is this like the model
 *  that scores 81.0%" is the question the sizing survey is actually asking. */
function printSummary(rows) {
  console.log('\n\nSUMMARY — a candidate is read against the CONTROL, never against an absolute\n');
  console.log(`${'model'.padEnd(38)} ${'pooling'.padEnd(9)} ${'bytes'.padStart(12)} ${'dim'.padStart(4)} `
    + `${'gap'.padStart(7)} ${'range'.padStart(7)} ${'margin'.padStart(8)} ${'long'.padStart(5)}  health`);
  console.log('-'.repeat(108));
  for (const r of rows) {
    const v = r.evaluation;
    console.log(`${(r.label + (r.isControl ? ' [control]' : '')).padEnd(38)} `
      + `${String(r.endpoint ? 'endpoint' : (r.pooling ?? 'declared')).padEnd(9)} `
      + `${String(r.bytes ?? '?').padStart(12)} `
      + `${String(v?.dimension ?? '-').padStart(4)} ${(r.health ? r.health.gap.toFixed(4) : '-').padStart(7)} `
      + `${(v ? v.range.toFixed(4) : '-').padStart(7)} `
      + `${(v ? (v.margin >= 0 ? '+' : '') + v.margin.toFixed(4) : '-').padStart(8)} `
      + `${(r.longOk ? 'ok' : 'no').padStart(5)}  ${r.verdict}`);
  }
  console.log('\n  The `health` column is the VERDICT and it is a smoke test: a live conversion, served');
  console.log('  right. gap = known-similar minus known-unrelated on an easy pair; range = the span of');
  console.log('  all 28 pairwise cosines, which catches a model that orders by noise.');
  console.log('\n  `margin` is NOT part of the verdict. It is the hard fixture — within-pair sentences');
  console.log('  sharing no content word, against two DIFFERENT topics that share vocabulary — and it');
  console.log('  measures SHARPNESS. A negative margin on a healthy model means blunter than the');
  console.log('  control, never broken. Read it against the control row; there is no portable absolute.');
  console.log('\n  `long` is ROLE FIT, not health: `no` means a 512-position model, which serves a short');
  console.log('  input fine and cannot be the memory embedder (per WRITE and per RECALL, ~6,000 chars).');
}

async function inspectMain(urls) {
  for (const url of urls) {
    console.log(`\n===== ${url.split('/').pop()} =====`);
    try {
      const h = await inspectRemote(url);
      const arch = h.meta['general.architecture'];
      const key = (suffix) => h.meta[`${arch}.${suffix}`] ?? h.meta[suffix] ?? '(unset)';
      console.log(`  gguf v${h.version}, ${h.tensorCount} tensors (header read from ${h.bytesRead} B)`);
      console.log(`  architecture      : ${arch}`);
      console.log(`  general.name      : ${h.meta['general.name'] ?? '(unset)'}   <- a template field; `
        + 'trust the TENSOR COUNT for identity');
      console.log(`  embedding_length  : ${key('embedding_length')}`);
      console.log(`  context_length    : ${key('context_length')}   <- BERT positions stop where they `
        + 'stop; no flag moves them');
      console.log(`  pooling_type      : ${key('pooling_type')}   <- 0 none, 1 mean, 2 cls, 3 last`);
      console.log(`  token_type_count  : ${h.meta['tokenizer.ggml.token_type_count'] ?? '(unset)'}`);
      console.log('  NOTE              : a classification head is a RERANKER concern. An embedder has '
        + 'none, so `rerank-screen --inspect` reporting head MISSING says nothing here.');
    } catch (e) {
      console.log(`  ERROR: ${e.message}`);
      process.exitCode = 1;
    }
  }
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  if (opts.inspect.length) return inspectMain(opts.inspect);

  const models = [...(opts.control ? [opts.control] : []), ...opts.models];
  if (models.length === 0 && opts.endpoints.length === 0) {
    console.error('embed-screen: need --model <path.gguf>, --endpoint <label=url>, or --inspect <url> …');
    console.error('  --control <path.gguf> screens a known-good embedder first, so a candidate\'s');
    console.error('  margin is read against one rather than against an absolute threshold.');
    process.exitCode = 2;
    return;
  }
  const missing = models.filter((m) => !fs.existsSync(m));
  if (missing.length) {
    console.error(`embed-screen: model not found: ${missing.join(', ')}`);
    process.exitCode = 2;
    return;
  }

  // Only resolved when something has to be STARTED: an endpoint-only run must work on a machine with no
  // llama-server at all, which is the point of the mode — the class it reaches has no GGUF to serve.
  const serverExe = models.length === 0 ? null : (opts.server ?? await resolveServerExe());
  const scratchDir = path.resolve(path.dirname(here), '..', '_embed-screen');
  fs.mkdirSync(scratchDir, { recursive: true });

  const before = await neighbourReport(ownedPids());
  console.log(`Neighbour roster BEFORE (${before.length}):`);
  for (const r of before) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);

  const rows = [];
  try {
    for (const model of models)
      for (const pooling of opts.poolings)
        rows.push(await screenOne({
          model, pooling, opts, serverExe, scratchDir, isControl: model === opts.control,
        }));
    // Endpoints last, so a `--control` GGUF row is already on the table to read their margin against.
    for (const endpoint of opts.endpoints)
      rows.push(await screenOne({ endpoint, opts, serverExe, scratchDir, isControl: false }));
  } finally {
    printSummary(rows);
    const after = await neighbourReport(ownedPids());
    const lost = vanishedNeighbours(before, after);
    if (lost.length) {
      console.error(`\n*** NEIGHBOUR LOST: ${JSON.stringify(lost)} — restart it ***`);
      process.exitCode = 1;
    } else {
      console.log(`\nNeighbours after: all ${before.length} still alive`);
    }
  }

  // Set the code; never process.exit() with a request possibly in flight — the abort REPLACES it.
  if (rows.some((r) => r.verdict !== 'HEALTHY' || r.leaked)) process.exitCode = 1;
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.on('SIGINT', async () => {
    console.error('\nSIGINT — tearing down owned servers before exiting.');
    try { await stopServers(ownedPids(), [DEFAULT_PORT]); } catch { /* best effort on the way out */ }
    process.exit(130);
  });
  await main().catch((err) => { console.error(err); process.exitCode = 1; });
}
