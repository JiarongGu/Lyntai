// rerank-screen — does a candidate GGUF actually WORK as a reranker, before anybody spends a run on it?
//
// It fills the memory verification seam (**D115**, `AddMemoryScoringVerification`), where model SIZE
// is a live question (`docs/model-tasks.md` §3). A survey alone is not enough: a community GGUF can load
// and score garbage, or load and reject the inputs the benches actually send.
//
// Every check is a trap `.claude/knowledge/pitfalls.md` already records — read it there rather than here.
// The four rules, each one line: assert ORDERING **and** DISTINCT scores (llama.cpp #16407); probe the
// EXTREME input, never the typical one; check the SPECIFIC port immediately before binding; enumerate
// neighbours as PROCESSES and re-check them after teardown.
//
// `--inspect` reads a REMOTE header over an HTTP range request — GGUF puts its metadata and tensor table
// at the front — so architecture and head presence cost no download.
//
// Usage:
//   node dev.mjs rerank-screen --model <path.gguf> [--port N] [--ctx N] [--ngl N] [--label name]
//   node dev.mjs rerank-screen --inspect <gguf-url> [<gguf-url> …]
import fs from 'node:fs';
import path from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';

import { parseListeners, isFree, neighbourPids } from './memory-contention.mjs';

const execFileAsync = promisify(execFile);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Default port. DELIBERATELY outside `memory-contention.mjs`'s 8140-8144 so the two harnesses cannot
 *  collide, and nowhere near 8090, which a sibling tool's embedding server has held across two sessions. */
export const DEFAULT_PORT = 8147;

/** The fixture: one unambiguous answer and three distractors that SHARE VOCABULARY with the query.
 *  The overlap is the point — a model reduced to mean-pooled cosine by a bad conversion still ranks
 *  "bread" last, so a fixture of one answer plus unrelated noise cannot tell a cross-encoder from a
 *  broken one. `[1]` and `[2]` are the discriminating pair. */
export const FIXTURE = {
  query: 'What year did the Apollo 11 mission land humans on the Moon?',
  documents: [
    'Apollo 11 landed the first humans on the Moon on 20 July 1969.',
    'The Apollo program was a United States human spaceflight program run by NASA.',
    'A lunar eclipse occurs when the Moon passes into the shadow of the Earth.',
    'Bread is baked in an oven at around 220 degrees Celsius.',
  ],
  expectedBest: 0,
  expectedWorst: 3,
};

/** The longest single document the memory benches can emit. `GraphMemoryOptions`' truncation lets a
 *  candidate reach ~6,000 characters; a 512-token model rejects this and passes everything shorter. */
export const longProbe = () =>
  'The Apollo 11 mission. ' + 'Lunar module descent telemetry and crew transcript. '.repeat(120);

/** A pair with a PUBLISHED reference score, and the check that `FIXTURE` was too easy to make.
 *
 *  Both documents are on-topic — only one answers — so lexical overlap cannot separate them and a model
 *  whose head has been degraded by conversion cannot coast. `FIXTURE` alone passed a GGUF that ranks these
 *  two BACKWARDS, so this is not a redundant second ordering check; it is the one that discriminates.
 *
 *  `published` is what `cross-encoder/ms-marco-MiniLM-L6-v2`'s own model card prints for these exact
 *  inputs. It belongs to THAT model, so the SPREAD is a diagnostic rather than a threshold — a healthy
 *  cross-encoder of any family separates a relevant from an irrelevant passage by units, not by
 *  hundredths, and a spread two orders of magnitude below this one means the scores are no longer logits. */
export const REFERENCE = {
  query: 'How many people live in Berlin?',
  documents: [
    'Berlin had a population of 3,520,031 registered inhabitants in an area of 891.82 square kilometers.',
    'Berlin is well known for its museums.',
  ],
  relevant: 0,
  published: [8.607138, -4.320078],
  source: 'cross-encoder/ms-marco-MiniLM-L6-v2 model card',
};

/** Below this, a score is not a logit any more. `ms-marco-MiniLM-L6-v2` converted by stock llama.cpp
 *  spreads these two by 0.015 where its own card spreads them by 12.93, and it spreads them the WRONG
 *  WAY — the conversion drops the pooler and the runtime zeroes `token_type_ids`, so a BERT cross-encoder
 *  loses the segment signal that tells the query from the document. */
export const COMPRESSED_SPREAD = 1.0;

/** `{ordered, spread, ratio, compressed}` for the reference pair. `ordered` is the assertion; the rest is
 *  reported, because the published magnitude is one model's and the ordering is every model's. */
export function evaluateReference(rows, reference = REFERENCE) {
  const byIndex = rows.slice().sort((a, b) => a.index - b.index).map((r) => r.score);
  const other = reference.relevant === 0 ? 1 : 0;
  const spread = byIndex[reference.relevant] - byIndex[other];
  const published = reference.published[0] - reference.published[1];
  return {
    ordered: spread > 0,
    spread,
    ratio: published / spread,
    compressed: Math.abs(spread) < COMPRESSED_SPREAD,
    scores: byIndex,
  };
}

/** `{index, score}[]` from a `/v1/rerank` body, or NULL when the shape is unusable — never a fabricated
 *  ordering, which would be indistinguishable from a real one in a table. Accepts both the Cohere
 *  (`relevance_score`) and the bare (`score`) spellings, as `CrossEncoderRerank.cs` does. */
export function parseRerankRows(body) {
  const results = body?.results;
  if (!Array.isArray(results) || results.length === 0) return null;
  const rows = [];
  for (const r of results) {
    if (typeof r?.index !== 'number') return null;
    const score = r.relevance_score ?? r.score;
    if (typeof score !== 'number' || !Number.isFinite(score)) return null;
    rows.push({ index: r.index, score });
  }
  return rows;
}

/** Scores in DOCUMENT order, so two responses are comparable however the server chose to sort them. */
export const byDocumentOrder = (rows) =>
  rows.slice().sort((a, b) => a.index - b.index).map((r) => r.score);

/** The two assertions #16407 calls for, plus the weaker one that catches a shuffled scorer.
 *  `distinct` rounds at 1e-6 — four orders of magnitude below the ~9e-3 drift measured between calls on a
 *  known-good model, so genuine repeat-call noise can never manufacture a distinct score. */
export function evaluateOrdering(rows, fixture = FIXTURE) {
  const best = rows.reduce((a, b) => (b.score > a.score ? b : a));
  const worst = rows.reduce((a, b) => (b.score < a.score ? b : a));
  const distinct = new Set(rows.map((r) => Math.round(r.score * 1e6))).size;
  const sorted = rows.map((r) => r.score).sort((a, b) => b - a);
  return {
    complete: rows.length === fixture.documents.length,
    distinct,
    allDistinct: distinct === rows.length,
    answerFirst: best.index === fixture.expectedBest,
    unrelatedLast: worst.index === fixture.expectedWorst,
    topTwoGap: sorted.length >= 2 ? sorted[0] - sorted[1] : NaN,
  };
}

/** Largest absolute per-document score difference between two responses to the SAME request.
 *  NaN when the two do not describe the same document set, which must not read as agreement. */
export function maxDrift(a, b) {
  if (!a || !b || a.length !== b.length || a.length === 0) return NaN;
  return Math.max(...a.map((v, i) => Math.abs(v - b[i])));
}

/** Which tensor names count as a classification head, BY ARCHITECTURE.
 *
 *  **Absence is not a defect — it is a gap in this table.** A name-only check reported `cls.output.weight`
 *  ABSENT on all three independent conversions of `jina-reranker-v1-tiny-en`, which reads as systematic
 *  breakage and is not: `jina-bert-v2` names its head `cls.weight`/`cls.bias`, and the model screens 8/8
 *  when actually served. So `headVerdict` returns `unknown` rather than `missing` for an architecture it
 *  has no entry for, and the CLI says so. */
export const HEAD_TENSORS = {
  bert: ['cls.output.weight'],
  'jina-bert-v2': ['cls.weight'],
  'xlm-roberta': ['cls.output.weight'],
  roberta: ['cls.output.weight'],
};

/** `present` | `missing` | `unknown` — and `unknown` is a statement about this table, never about the file.
 *  Only `missing` is evidence, and even then it is a REASON TO SERVE THE MODEL rather than a verdict:
 *  two conversions flagged this way turned out to fail earlier still, at load, on a missing metadata key. */
export function headVerdict(architecture, tensorNames) {
  const wanted = HEAD_TENSORS[architecture];
  if (!wanted) return { state: 'unknown', architecture, wanted: null };
  const found = wanted.filter((t) => tensorNames.includes(t));
  return { state: found.length === wanted.length ? 'present' : 'missing', architecture, wanted, found };
}

// ── GGUF header ────────────────────────────────────────────────────────────────────────────────────────
const GGUF_TYPE = { UINT32: 4, INT32: 5, BOOL: 7, STRING: 8, ARRAY: 9 };
const SCALAR_WIDTH = { 0: 1, 1: 1, 2: 2, 3: 2, 4: 4, 5: 4, 6: 4, 7: 1, 10: 8, 11: 8, 12: 8 };

class Reader {
  constructor(buf) { this.b = buf; this.o = 0; }
  need(n) { if (this.o + n > this.b.length) throw new Error('SHORT'); }
  u32() { this.need(4); const v = this.b.readUInt32LE(this.o); this.o += 4; return v; }
  u64() { this.need(8); const v = this.b.readBigUInt64LE(this.o); this.o += 8; return Number(v); }
  str() { const n = this.u64(); this.need(n); const s = this.b.subarray(this.o, this.o + n).toString('utf8'); this.o += n; return s; }
  skip(t) {
    if (t === GGUF_TYPE.STRING) { this.str(); return; }
    if (t === GGUF_TYPE.ARRAY) {
      const et = this.u32(); const n = this.u64();
      for (let i = 0; i < n; i++) this.skip(et);
      return;
    }
    const w = SCALAR_WIDTH[t];
    if (w === undefined) throw new Error(`bad scalar type ${t}`);
    this.need(w); this.o += w;
  }
}

/** Parse a GGUF header from a PREFIX of the file. Throws `SHORT` when the buffer stops mid-header, which is
 *  the caller's signal to fetch more rather than to report a malformed file — the distinction matters,
 *  because a truncated read that reported "0 tensors" would look exactly like a headless conversion. */
export function readGgufHeader(buf) {
  if (buf.subarray(0, 4).toString('ascii') !== 'GGUF')
    throw new Error(`not a GGUF (magic=${JSON.stringify(buf.subarray(0, 4).toString('ascii'))})`);
  const r = new Reader(buf);
  r.o = 4;
  const version = r.u32();
  const tensorCount = r.u64();
  const kvCount = r.u64();
  const meta = {};
  for (let i = 0; i < kvCount; i++) {
    const key = r.str();
    const type = r.u32();
    if (type === GGUF_TYPE.STRING) meta[key] = r.str();
    else if (type === GGUF_TYPE.UINT32 || type === GGUF_TYPE.INT32) { r.need(4); meta[key] = buf.readInt32LE(r.o); r.o += 4; }
    else if (type === GGUF_TYPE.BOOL) { r.need(1); meta[key] = !!buf.readUInt8(r.o); r.o += 1; }
    else r.skip(type);
  }
  const names = [];
  for (let i = 0; i < tensorCount; i++) {
    const name = r.str();
    const dims = r.u32();
    for (let d = 0; d < dims; d++) r.u64();
    r.u32(); r.u64();                       // ggml type, offset
    names.push(name);
  }
  return { version, tensorCount, kvCount, names, meta };
}

async function fetchPrefix(url, bytes) {
  const r = await fetch(url, { headers: { Range: `bytes=0-${bytes - 1}`, 'user-agent': 'lyntai-rerank-screen' }, redirect: 'follow' });
  if (!r.ok && r.status !== 206) throw new Error(`HTTP ${r.status}`);
  return Buffer.from(await r.arrayBuffer());
}

/** Header of a REMOTE GGUF, growing the range request until the header fits rather than guessing a size. */
export async function inspectRemote(url, { start = 4 * 1024 * 1024, attempts = 6 } = {}) {
  let want = start;
  for (let i = 0; i < attempts; i++) {
    const buf = await fetchPrefix(url, want);
    try { return { ...readGgufHeader(buf), bytesRead: buf.length }; }
    catch (e) { if (e.message !== 'SHORT') throw e; want *= 3; }
  }
  throw new Error('header did not fit in the range budget');
}

// ── CLI ────────────────────────────────────────────────────────────────────────────────────────────────
export function parseArgs(argv) {
  const at = (n) => { const i = argv.indexOf(`--${n}`); return i >= 0 && argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[i + 1] : null; };
  // STOP at the next flag rather than filtering flags out: `--inspect a b --port 9` must not harvest `9`
  // as a third url. Filtering would silently accept a flag's VALUE, and an unreachable url reads as a
  // failed inspection rather than as an argument bug.
  const inspectAt = argv.indexOf('--inspect');
  const inspect = [];
  if (inspectAt >= 0) {
    for (const a of argv.slice(inspectAt + 1)) {
      if (a.startsWith('--')) break;
      inspect.push(a);
    }
  }
  return {
    inspect,
    model: at('model'),
    label: at('label'),
    port: Number(at('port') ?? DEFAULT_PORT),
    ctx: Number(at('ctx') ?? 4096),
    ngl: Number(at('ngl') ?? 99),
    server: at('server'),
  };
}

const isEntry = process.argv[1] && path.basename(process.argv[1]) === 'rerank-screen.mjs';
if (isEntry) await main(parseArgs(process.argv.slice(2)));

async function main(opts) {
  if (opts.inspect.length) return inspectMain(opts.inspect);
  if (!opts.model) {
    console.error('rerank-screen: need --model <path.gguf>, or --inspect <gguf-url> …');
    process.exitCode = 2;
    return;
  }
  return screenMain(opts);
}

async function inspectMain(urls) {
  for (const url of urls) {
    console.log(`\n===== ${url.split('/').pop()} =====`);
    try {
      const h = await inspectRemote(url);
      const arch = h.meta['general.architecture'];
      const head = headVerdict(arch, h.names);
      console.log(`  gguf v${h.version}, ${h.tensorCount} tensors (header read from ${h.bytesRead} B)`);
      console.log(`  architecture : ${arch}`);
      console.log(`  general.name : ${h.meta['general.name'] ?? '(unset)'}   <- a template field; trust the TENSOR COUNT for identity`);
      console.log(`  head         : ${head.state.toUpperCase()}` +
        (head.state === 'unknown'
          ? `  — no entry for '${arch}' in HEAD_TENSORS, so this says nothing about the file`
          : `  (looked for ${head.wanted.join(', ')})`));
      if (head.state === 'missing')
        console.log(`  NOTE         : missing is a reason to SERVE it, not a verdict — screen it with --model`);
    } catch (e) {
      console.log(`  ERROR: ${e.message}`);
      process.exitCode = 1;
    }
  }
}

async function screenMain(opts) {
  const server = opts.server ?? process.env.LYNTAI_LLAMA_SERVER;
  const label = opts.label ?? path.basename(opts.model);
  const results = [];
  const record = (name, ok, detail) => {
    results.push({ name, ok });
    console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
  };

  console.log(`\n=== rerank-screen: ${label} ===`);
  if (!server) {
    console.error('  need --server <llama-server path> or LYNTAI_LLAMA_SERVER');
    process.exitCode = 2;
    return;
  }
  if (!fs.existsSync(opts.model)) {
    console.error(`  model not found: ${opts.model}`);
    process.exitCode = 2;
    return;
  }
  const bytes = fs.statSync(opts.model).size;
  // Both units, always: they straddle round thresholds and a sweep once mis-sorted candidates by 27%.
  console.log(`  bytes   : ${bytes}  (${(bytes / 1024 / 1024).toFixed(2)} MiB | ${(bytes / 1e6).toFixed(2)} MB)`);

  const listeners = async () => parseListeners((await execFileAsync('netstat', ['-ano', '-p', 'TCP'])).stdout);
  const llamaRows = async () => {
    const { stdout } = await execFileAsync('powershell', ['-NoProfile', '-Command',
      "Get-CimInstance Win32_Process -Filter \"Name='llama-server.exe'\" | Select-Object ProcessId,ParentProcessId | ConvertTo-Json -Compress"]);
    const t = stdout.trim();
    if (!t || t === 'null') return [];
    const p = JSON.parse(t);
    return (Array.isArray(p) ? p : [p]).map((x) => ({ pid: x.ProcessId, ppid: x.ParentProcessId }));
  };

  if (!isFree(await listeners(), opts.port)) {
    console.error(`  ABORT: port ${opts.port} is LISTENING. Binding it fails UPWARD — the incumbent would ` +
      `answer every request and this screen would read clean on somebody else's model.`);
    process.exitCode = 3;
    return;
  }
  const before = neighbourPids(await llamaRows(), []);
  console.log(`  neighbours before: ${JSON.stringify(before)} (not ours — must survive teardown)`);

  // The query is a PARAMETER, never a default. It was `FIXTURE.query` for every call, so the reference
  // pair was scored against the wrong question — and the resulting FAIL on the known-good control is what
  // exposed it. A fixture's query and its documents must travel together.
  const rank = async (documents, query = FIXTURE.query) => {
    const r = await fetch(`http://127.0.0.1:${opts.port}/v1/rerank`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ model: label, query, documents, top_n: documents.length }),
    });
    const text = await r.text();
    let json = null; try { json = JSON.parse(text); } catch { /* keep raw for the report */ }
    return { status: r.status, json, text };
  };

  let child = null;
  try {
    const argv = ['--model', opts.model, '--port', String(opts.port), '--host', '127.0.0.1', '--reranking',
      '--ctx-size', String(opts.ctx), '--batch-size', String(opts.ctx), '--ubatch-size', String(opts.ctx),
      '--no-webui', '-ngl', String(opts.ngl)];
    child = spawn(server, argv, { stdio: ['ignore', 'pipe', 'pipe'] });
    let log = '', exited = null;
    child.stdout.on('data', (d) => { log += d; });
    child.stderr.on('data', (d) => { log += d; });
    child.on('exit', (c) => { exited = c; });

    let up = false;
    for (let i = 0; i < 90 && !up && exited === null; i++) {
      await sleep(1000);
      try { up = (await fetch(`http://127.0.0.1:${opts.port}/health`)).ok; } catch { /* not yet */ }
    }
    if (!up) {
      const why = exited !== null ? `process exited with code ${exited}` : 'no /health after 90s';
      record('server starts', false, why);
      const err = log.split(/\r?\n/).filter((l) => /error|failed/i.test(l)).slice(-4);
      for (const l of err) console.log(`      ${l.trim()}`);
      return;
    }
    record('server starts', true);

    const typical = await rank(FIXTURE.documents);
    if (typical.status !== 200) {
      record('POST /v1/rerank answers 200', false, `HTTP ${typical.status}: ${typical.text.slice(0, 240)}`);
      return;
    }
    record('POST /v1/rerank answers 200', true);

    const rows = parseRerankRows(typical.json);
    if (!rows) { record('usable result shape', false, JSON.stringify(typical.json).slice(0, 240)); return; }
    const v = evaluateOrdering(rows, FIXTURE);
    record('one result per document', v.complete, `${rows.length} of ${FIXTURE.documents.length}`);

    for (const r of rows.slice().sort((a, b) => b.score - a.score))
      console.log(`     ${String(r.score).padStart(22)}  [${r.index}] ${FIXTURE.documents[r.index].slice(0, 60)}`);

    record('scores are DISTINCT', v.allDistinct,
      `${v.distinct} of ${rows.length} — a flat scorer reorders nothing and reads as a clean null`);
    record('ORDERING puts the answer first', v.answerFirst);
    record('unrelated document ranks last', v.unrelatedLast);

    // THE DISCRIMINATING CHECK. Two on-topic documents with a published reference score: the fixture
    // above cannot separate a working head from a degraded one, and passed a GGUF that inverts this pair.
    const refRows = parseRerankRows((await rank(REFERENCE.documents, REFERENCE.query)).json);
    if (!refRows) record('scores the reference pair', false, 'no usable result');
    else {
      const ref = evaluateReference(refRows, REFERENCE);
      record('REFERENCE pair: the answering passage outranks the on-topic one', ref.ordered,
        `[${ref.scores.map((s) => s.toFixed(4)).join(', ')}] — published ` +
        `[${REFERENCE.published.join(', ')}] (${REFERENCE.source})`);
      record('reference scores are LOGIT-SCALED, not collapsed', !ref.compressed,
        `spread ${ref.spread.toFixed(4)} vs published 12.9272 (${ref.ratio.toFixed(1)}x) — a collapsed ` +
        `spread means a dropped pooler or zeroed token_type_ids, not a weak model`);
    }

    const long = longProbe();
    const extreme = await rank([long, FIXTURE.documents[3]]);
    record('serves the LONGEST input the benches emit', extreme.status === 200,
      `${long.length} chars -> HTTP ${extreme.status}` +
      (extreme.status === 200 ? '' : ` — ${extreme.text.slice(0, 160)}`));

    // REPEATABILITY, not determinism. Measured on a known-good control: /v1/rerank is not bitwise stable
    // within one instance (~9e-3 of drift, and the batch shape moves the fixed point). The seam consumes
    // the ORDER, so assert that and REPORT the drift.
    const again = parseRerankRows((await rank(FIXTURE.documents)).json);
    const drift = maxDrift(byDocumentOrder(rows), again ? byDocumentOrder(again) : null);
    const sameBest = again && evaluateOrdering(again, FIXTURE).answerFirst === v.answerFirst;
    record('a repeated call ranks the same document first', !!sameBest, `score drift ${drift.toExponential(2)}`);
    console.log(`  note    : top-two gap ${v.topTwoGap.toFixed(4)} vs drift ${drift.toExponential(2)} — ` +
      `this fixture cannot see a near-tie flip`);
  } finally {
    if (child?.pid) {
      try { await execFileAsync('taskkill', ['/F', '/T', '/PID', String(child.pid)]); } catch { /* gone */ }
      await sleep(1500);
    }
    console.log(`  teardown: port ${opts.port} ${(await listeners()).has(opts.port) ? '*** STILL LISTENING ***' : 'free'}`);
    const after = new Set((await llamaRows()).map((r) => r.pid));
    const lost = before.filter((p) => !after.has(p));
    console.log(lost.length
      ? `  *** NEIGHBOUR LOST: ${JSON.stringify(lost)} — restart it ***`
      : `  neighbours after : all ${before.length} still alive`);
  }

  const failed = results.filter((r) => !r.ok);
  console.log(`\n  ${results.length - failed.length}/${results.length} checks passed` +
    (failed.length ? ` — FAILED: ${failed.map((f) => f.name).join(', ')}` : ''));
  // Set the code; never process.exit() with a request possibly in flight — the abort REPLACES the code.
  if (failed.length || results.length === 0) process.exitCode = 1;
}
