// memory-decision — which SHAPE wins a decision at 3-7 options, and at what model size.
//
// This ORCHESTRATOR owns every server process; the C# `--decision` sweep it invokes only measures. Four
// servers run AT ONCE — the 4B and the 1B on their own ports, an embedder, and a cross-encoder — because
// the arms must be PAIRED trial for trial, and two sequential runs could only be paired by a digest.
//
// It deliberately does NOT refuse a busy device, where `memory-contention` does: the metric here is
// ACCURACY, every cell shares one backend, and a neighbour costs wall clock rather than validity. The
// device state is SAMPLED and REPORTED so no cost figure is mistaken for a portable one.
//
// The reranker is screened against `rerank-screen`'s published REFERENCE pair, not just for ordering: a
// community GGUF missing its head still loads and still returns scores, and an easy fixture passes one
// that ranks backwards (`.claude/knowledge/pitfalls.md`). TASKS.md Part 178.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  neighbourReport, ownedPids, resolveServerExe, runTracked,
  startGpuSampler, startServers, stopServers, vanishedNeighbours,
} from './memory-contention.mjs';
import { REFERENCE, evaluateReference, parseRerankRows } from './rerank-screen.mjs';

const here = fileURLToPath(import.meta.url);

/** Ports this harness owns. DELIBERATELY outside `memory-contention`'s 8140-8144 and `rerank-screen`'s
 *  8147, and nowhere near 8090, which a sibling tool's embedding server has held across several sessions. */
export const PORTS = { chat: 8150, small: 8151, embed: 8152, rerank: 8153 };

/** Each role's weights and serving settings, as DATA. `--parallel 4` on the two chat servers matches the
 *  bench's own in-flight ceiling; it DIVIDES the context across slots, so 8192 leaves 2,048 per slot, well
 *  clear of the ~400 tokens a 7-option prompt costs. The embedder gets no `--parallel`: pooled embedding
 *  wants its whole sequence in one physical batch, and the bench embeds sequentially anyway. */
export const ROLES = {
  chat:   { file: 'gemma-3-4b-it-Q4_K_M.gguf',     flags: [],             settings: { 'ctx-size': 8192, parallel: 4 } },
  small:  { file: 'gemma-3-1b-it-Q4_K_M.gguf',     flags: [],             settings: { 'ctx-size': 8192, parallel: 4 } },
  embed:  { file: 'embeddinggemma-300M-Q8_0.gguf', flags: ['embeddings'], settings: { 'ctx-size': 2048, 'batch-size': 2048, 'ubatch-size': 2048 } },
  rerank: { file: 'LAMAR-600m.Q5_K_M.gguf',        flags: ['reranking'],  settings: { 'ctx-size': 4096, 'batch-size': 4096, 'ubatch-size': 4096 } },
};

/** `{label, port, argv}` per role — the shape `startServers` takes. Exported so a test can assert the argv
 *  without spawning anything, which is the only way to check `-ngl` is passed EXPLICITLY: the server's
 *  default is not neutral and its log prints no offload line.
 *
 *  <b>Parameterised by PORTS 2026-09-12</b>, when `tool-affordance` needed the same four roles on four
 *  ports of its own. The role table stays here and is IMPORTED rather than copied: the two sweeps' tables
 *  are read together, so a drifted weights list would make that comparison wrong rather than noisy, and
 *  neither sweep's output would say so. */
export function specsFor(ports, modelDir) {
  return Object.entries(ROLES).map(([role, { file, flags, settings }]) => ({
    label: role,
    port: ports[role],
    argv: [
      '--model', path.join(modelDir, file), '--alias', role,
      '--port', String(ports[role]), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
      ...flags.map((f) => `--${f}`),
      ...Object.entries(settings).flatMap(([k, v]) => [`--${k}`, String(v)]),
    ],
  }));
}

export const serverSpecs = (modelDir) => specsFor(PORTS, modelDir);

/** Env the C# sweep reads. Every URL spells 127.0.0.1, never `localhost`: on Windows that resolves to ::1
 *  first and costs ~1.8 s per call against an IPv4-only listener — at several thousand calls it is the
 *  whole run. */
export function envFor(ports = PORTS) {
  const at = (p) => `http://127.0.0.1:${p}`;
  return {
    LYNTAI_LIVE_CHAT_URL: at(ports.chat), LYNTAI_LIVE_CHAT_MODEL: 'chat',
    LYNTAI_LIVE_SMALL_URL: at(ports.small), LYNTAI_LIVE_SMALL_MODEL: 'small',
    LYNTAI_LIVE_MODEL_URL: at(ports.embed), LYNTAI_LIVE_EMBED_MODEL: 'embed',
    LYNTAI_LIVE_RERANK_URL: at(ports.rerank), LYNTAI_LIVE_RERANK_MODEL: 'rerank',
  };
}

const postJson = async (url, body, timeoutMs = 120_000) => {
  const res = await fetch(url, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body), signal: AbortSignal.timeout(timeoutMs),
  });
  return res.json().catch(() => null);
};

/** What each port ACTUALLY loaded, read back from the server rather than assumed from the argv. A
 *  `--model`-started `llama-server` answers to whatever name it is asked, so the requested name proves
 *  nothing; `/props` reports the file it opened, which is a fact the process computed. Absent on a build
 *  that does not expose it — reported as `unknown`, never silently as agreement. */
export function servedFile(props) {
  const raw = props?.model_path ?? props?.default_generation_settings?.model ?? null;
  return typeof raw === 'string' && raw.length > 0 ? path.basename(raw.replace(/\\/g, '/')) : null;
}

export async function identityReport(modelDir, ports = PORTS) {
  const rows = [];
  for (const [role, { file }] of Object.entries(ROLES)) {
    let props = null;
    try {
      const res = await fetch(`http://127.0.0.1:${ports[role]}/props`, { signal: AbortSignal.timeout(10_000) });
      props = await res.json().catch(() => null);
    } catch { /* older builds have no /props; the `unknown` branch below is the honest answer */ }
    const served = servedFile(props);
    const bytes = fs.statSync(path.join(modelDir, file)).size;
    rows.push({ role, want: file, served, bytes, agrees: served === null ? null : served === file });
  }
  return rows;
}

/** The reranker check that actually discriminates. Ordering plus distinctness passed a GGUF that ranks the
 *  published pair BACKWARDS, so the pair — and the SPREAD, which says whether the scores are still logits —
 *  is the check worth running before a run is spent. */
export async function screenReranker(ports = PORTS) {
  const body = await postJson(`http://127.0.0.1:${ports.rerank}/v1/rerank`, {
    model: 'rerank', query: REFERENCE.query, documents: REFERENCE.documents, top_n: REFERENCE.documents.length,
  });
  const rows = parseRerankRows(body);
  if (!rows) return { ok: false, detail: 'no usable /v1/rerank result' };
  const ref = evaluateReference(rows, REFERENCE);
  return {
    ok: ref.ordered && !ref.compressed,
    detail: `spread ${ref.spread.toFixed(4)} vs published 12.9272 (${ref.ratio.toFixed(1)}x), `
      + `ordered=${ref.ordered}, collapsed=${ref.compressed}`,
  };
}

async function buildBench(repoRoot) {
  const code = await runTracked('dotnet',
    ['build', '-c', 'Release', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '-v', 'q', '--nologo']);
  if (code !== 0) throw new Error(`dotnet build failed with exit code ${code}`);
}

/** `--difficulty`, `--n`, `--concurrency` and anything else are FORWARDED verbatim to the C# sweep; only
 *  `--skip-build` is this module's. Nothing is stripped beyond it, so a flag added to the bench needs no
 *  edit here. */
export function parseArgs(argv) {
  return { skipBuild: argv.includes('--skip-build'), benchArgs: argv.filter((a) => a !== '--skip-build') };
}

async function main() {
  const { skipBuild, benchArgs } = parseArgs(process.argv.slice(2));
  const modelDir = process.env.LYNTAI_MODEL_DIR ?? process.env.LYNTAI_CONTENTION_MODEL_DIR;
  if (!modelDir) {
    console.error('memory-decision: set LYNTAI_MODEL_DIR to the directory holding the GGUFs.');
    console.error('  No default, by design — a developer-machine path must never reach a tracked file.');
    console.error(`  Needs: ${Object.values(ROLES).map((r) => r.file).join(', ')}`);
    process.exitCode = 2;
    return;
  }

  const repoRoot = path.resolve(path.dirname(here), '..', '..');
  const scratchDir = path.resolve(path.dirname(here), '..', '_decision');
  fs.mkdirSync(scratchDir, { recursive: true });

  const missing = Object.values(ROLES).map((r) => r.file)
    .filter((f) => !fs.existsSync(path.join(modelDir, f)));
  if (missing.length) {
    console.error(`memory-decision: missing model file(s) in ${modelDir}: ${missing.join(', ')}`);
    process.exitCode = 2;
    return;
  }

  const serverExe = await resolveServerExe();
  const before = await neighbourReport(ownedPids());
  console.log(`Neighbour roster BEFORE (${before.length}):`);
  for (const r of before) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);

  if (!skipBuild) await buildBench(repoRoot);

  let pids = [];
  const gpu = startGpuSampler();
  try {
    pids = await startServers(serverSpecs(modelDir), { scratchDir, serverExe });
    console.log(`\nServers up on ${Object.values(PORTS).join(', ')} (pids ${pids.join(', ')})\n`);

    console.log('IDENTITY — what each port actually loaded, read back from the server:');
    const identity = await identityReport(modelDir);
    for (const row of identity) {
      const verdict = row.agrees === null ? 'unknown (this build exposes no /props)'
        : row.agrees ? 'agrees' : `*** MISMATCH: serving ${row.served} ***`;
      console.log(`  ${row.role.padEnd(7)} ${row.want}  ${row.bytes} B  — ${verdict}`);
    }
    // REFUSE on a mismatch. The study's headline axis is model SIZE, so a port serving the wrong weights
    // does not degrade the result — it inverts it, and prints one marked line among a hundred while doing so.
    if (identity.some((r) => r.agrees === false)) {
      console.error('  Refusing to run: a port is serving weights this harness did not ask for, and SIZE is');
      console.error('  the axis under test — the table would be wrong rather than noisy.');
      process.exitCode = 1;
      return;
    }

    const screen = await screenReranker();
    console.log(`\nRERANK SCREEN (reference pair): ${screen.ok ? 'PASS' : '*** FAIL ***'} — ${screen.detail}`);
    if (!screen.ok) {
      console.error('  Refusing to run: a cross-encoder that cannot order a pair with a published score is');
      console.error('  a broken conversion, and its arm would read as a weak model rather than a dead one.');
      process.exitCode = 1;
      return;
    }

    const code = await runTracked('dotnet', ['run', '-c', 'Release', '--no-build',
      '--project', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '--', '--decision', ...benchArgs],
      envFor());
    if (code !== 0) process.exitCode = code;
  } finally {
    const device = await gpu.stop();
    // Unconditional, not `if (pids.length)`: a start that THREW leaves `pids` empty while being the most
    // likely moment to have leaked one, so the survivor re-read must run on that path above all.
    const { survivors } = await stopServers(pids, Object.values(PORTS));
    if (survivors.length) {
      // A leak is a FAILURE, with the PID kept. Run 1 of this sweep left port 8153 listening, printed it to
      // stdout, discarded the pid and exited 0 — so a green line sat one row under an abandoned server.
      console.error(`\n*** PORTS STILL LISTENING after teardown: ${JSON.stringify(survivors)} `
        + '(port, pid) — kill these by PID before the next run ***');
      process.exitCode = 1;
    } else {
      console.log(`\nTeardown: all ${Object.values(PORTS).length} owned ports free`);
    }
    console.log(device.samples === 0
      ? 'GPU: UNAVAILABLE — nvidia-smi produced zero samples. Reporting null, not zero.'
      : `GPU during the run: max ${device.maxUtil.toFixed(0)}% util, mean ${device.meanUtil.toFixed(1)}%, `
        + `max ${device.maxMemMiB.toFixed(0)} MiB over ${device.samples} sample(s). Accuracy is the metric `
        + 'here, so this bounds the WALL CLOCK rather than the result.');

    const after = await neighbourReport(ownedPids());
    const lost = vanishedNeighbours(before, after);
    if (lost.length) {
      console.error(`*** NEIGHBOUR LOST: ${JSON.stringify(lost)} — restart it ***`);
      process.exitCode = 1;
    } else {
      console.log(`Neighbours after: all ${before.length} still alive`);
    }
  }
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  // Ctrl+C during the model-load window — up to 180 s with ~4.1 GB of weights coming up across four
  // servers — would otherwise terminate the process without unwinding `finally`, orphaning every one of
  // them on a GPU. `stopServers` is the same teardown the happy path uses.
  process.on('SIGINT', async () => {
    console.error('\nSIGINT — tearing down owned servers before exiting.');
    try { await stopServers(ownedPids(), Object.values(PORTS)); } catch { /* best effort on the way out */ }
    process.exit(130);
  });
  await main().catch((err) => { console.error(err); process.exitCode = 1; });
}
