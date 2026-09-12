// tool-affordance — what a small model does with a roster of 3-7 tools, through the PROMPT protocol
// `ToolLoop` authors itself.
//
// This ORCHESTRATOR owns every server process; the C# `--affordance` sweep it invokes only measures. The
// SAME four roles `memory-decision` serves, on four ports of its own — the role table is IMPORTED rather
// than copied, because Part 178 reads the two tables together and a drifted weights list would make that
// comparison wrong rather than noisy.
//
// It does NOT refuse a busy device, for `memory-decision`'s reason: the metric is ACCURACY, every cell
// shares one backend, and a neighbour costs wall clock rather than validity. The device state is SAMPLED
// and REPORTED so no cost figure here is mistaken for a portable one.
//
// The reranker is screened against `rerank-screen`'s published REFERENCE pair before a run is spent: a
// community GGUF missing its head still loads and still returns scores. TASKS.md Part 178.
import { execFile } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

import {
  neighbourReport, ownedPids, resolveServerExe, runTracked,
  startGpuSampler, startServers, stopServers, vanishedNeighbours,
} from './memory-contention.mjs';
import {
  ROLES, envFor as decisionEnvFor, identityReport, screenReranker, specsFor,
} from './memory-decision.mjs';

const here = fileURLToPath(import.meta.url);
const execFileAsync = promisify(execFile);

/** Ports this harness owns. DELIBERATELY outside `memory-contention`'s 8140-8144, `rerank-screen`'s 8147
 *  and `memory-decision`'s 8150-8153, and nowhere near 8090, which a sibling tool's embedding server has
 *  held across several sessions. Its own block rather than a re-use so both sweeps can be up at once. */
export const PORTS = { chat: 8160, small: 8161, embed: 8162, rerank: 8163 };

/** Re-exported, not redeclared. The assertion that these are the same four weights `memory-decision`
 *  serves lives in this module's test, because it is the invariant that keeps the two tables comparable. */
export { ROLES };

export const serverSpecs = (modelDir) => specsFor(PORTS, modelDir);

/** Ports for EXTRA embedder arms. Its own small block above the four roles and below `embed-screen`'s
 *  8167, so the cap is structural rather than a convention someone has to remember. */
export const EXTRA_PORT_BASE = 8164;

export const MAX_EXTRA_ARMS = 3;

/** One `llama-server` per extra embedder, each served as its OWN GGUF declares — no `--pooling`. The
 *  trained mode survives conversion (the header carries it), and forcing one made `all-MiniLM-L6-v2`
 *  lose 45% of its cosine range when measured under the wrong one. */
export function extraSpecs(arms, modelDir) {
  if (arms.length > MAX_EXTRA_ARMS)
    throw new Error(`tool-affordance: at most ${MAX_EXTRA_ARMS} extra embedder arm(s), got ${arms.length}`);
  return arms.map((arm, i) => ({
    label: `embed-${arm.label}`,
    port: EXTRA_PORT_BASE + i,
    argv: [
      '--model', path.join(modelDir, arm.file), '--alias', arm.label,
      '--port', String(EXTRA_PORT_BASE + i), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
      '--embeddings', '--ctx-size', '2048', '--batch-size', '2048', '--ubatch-size', '2048',
    ],
  }));
}

/** The TOOL-CAPABLE model's port. Clear of the four roles (8160-8163), of every embedder arm
 *  (`EXTRA_PORT_BASE` + `MAX_EXTRA_ARMS` - 1 = 8166) and of `embed-screen`'s 8167 — a test asserts all
 *  three, because binding a busy port fails UPWARD and the incumbent answers every request. */
export const NATIVE_PORT = 8169;

/** The tool-capable chat server, or `[]` when none was asked for. Served like the other chat roles and
 *  NOT with `--jinja`: measured 2026-09-13, that flag is byte-irrelevant on this build for both a model
 *  whose template carries a tool section and one whose does not, so passing it would imply a dependency
 *  the probe refuted. */
export function nativeSpecs(file, modelDir) {
  if (!file) return [];
  return [{
    label: 'native',
    port: NATIVE_PORT,
    argv: [
      '--model', path.join(modelDir, file), '--alias', 'native',
      '--port', String(NATIVE_PORT), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
      '--ctx-size', '8192', '--parallel', '4',
    ],
  }];
}

/** `label=url,…` for `LYNTAI_LIVE_EMBED_ARMS`, or NULL for no arms — an absent variable and an empty
 *  one are different things to the bench, and only the first means "no extra arms were asked for". */
export const armsEnv = (arms) => (arms.length === 0
  ? null
  : arms.map((a, i) => `${a.label}=http://127.0.0.1:${EXTRA_PORT_BASE + i}`).join(','));

/** The four roles' env, plus the embedder arms and the tool-capable model. `LYNTAI_LIVE_MODEL_URL` still
 *  names the PRIMARY embedder: it builds the trials, and an arm that could move that would change which
 *  distractors a roster holds. A variable is ABSENT rather than empty when its arm was not asked for. */
export function envFor(arms = [], nativeFile = null) {
  const env = decisionEnvFor(PORTS);
  const value = armsEnv(arms);
  return {
    ...env,
    ...(value === null ? {} : { LYNTAI_LIVE_EMBED_ARMS: value }),
    ...(nativeFile
      ? { LYNTAI_LIVE_NATIVE_URL: `http://127.0.0.1:${NATIVE_PORT}`, LYNTAI_LIVE_NATIVE_MODEL: 'native' }
      : {}),
  };
}

/** VRAM the four servers need free, from a COMPLETED run's own sampler: it peaked at 8,670 MiB device-total
 *  against a ~2,251 MiB neighbour, so this harness's footprint is ~6,419 MiB. Rounded up for the allocator.
 *
 *  <b>Why it is a gate and not a note.</b> Launching a second run before the previous one's memory was
 *  released killed the process SILENTLY — no exception, no stack trace, output truncated mid-sentence, ports
 *  torn down, exit code 127. Nothing anywhere said VRAM. `memory-decision` serves the same four roles and
 *  has the same exposure.
 *
 *  <b>First set to 9200 and that was WRONG</b>, derived from the CRASHED run's 11,285 MiB peak minus a
 *  neighbour measured afterwards at idle — which charged the neighbour's own growth to this harness and
 *  would have refused runs that fit. A ceiling taken from a failed run measures the failure. */
export const NEEDED_FREE_MIB = 7000;

/** One `nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader` line — `"2251 MiB, 12282
 *  MiB"` or the `nounits` form. `null` for anything that does not parse as two numbers, because the caller
 *  SKIPS on null and REFUSES on false: a blank read as zero-used would claim the whole card is free. */
export function parseGpuMemory(csvText) {
  const line = String(csvText).split(/\r?\n/).find((l) => l.trim().length > 0);
  if (!line) return null;
  const [usedRaw, totalRaw] = line.split(',');
  const usedMiB = Number.parseFloat(usedRaw);
  const totalMiB = Number.parseFloat(totalRaw);
  return Number.isFinite(usedMiB) && Number.isFinite(totalMiB) ? { usedMiB, totalMiB } : null;
}

export const hasHeadroom = ({ usedMiB, totalMiB }, neededMiB) => totalMiB - usedMiB >= neededMiB;

/** Refuse BEFORE spawning anything, so an out-of-memory device produces a sentence rather than a corpse.
 *  Skips when `nvidia-smi` is unavailable — the same "cannot verify is not verified clean" posture
 *  `assertGpuIdle` takes, and for the same reason: a CPU-only machine must still be able to run this. */
async function assertGpuHeadroom() {
  let sample = null;
  try {
    const { stdout } = await execFileAsync('nvidia-smi',
      ['--query-gpu=memory.used,memory.total', '--format=csv,noheader'], { timeout: 5_000 });
    sample = parseGpuMemory(stdout);
  } catch { /* absent or errored — the skip branch below is the honest answer */ }

  if (sample === null) {
    console.log('GPU headroom: nvidia-smi unavailable — skipping (cannot verify there is room).');
    return true;
  }
  const free = sample.totalMiB - sample.usedMiB;
  console.log(`GPU headroom: ${free.toFixed(0)} MiB free of ${sample.totalMiB.toFixed(0)} `
    + `(need ${NEEDED_FREE_MIB})`);
  if (hasHeadroom(sample, NEEDED_FREE_MIB)) return true;

  console.error(`  Refusing to start: four servers need ~${NEEDED_FREE_MIB} MiB and only ${free.toFixed(0)} `
    + 'is free. Loading them anyway kills this process with no error at all — close the neighbour, or wait');
  console.error('  for a previous run\'s memory to be released (it lags the port going free).');
  return false;
}

async function buildBench(repoRoot) {
  const code = await runTracked('dotnet',
    ['build', '-c', 'Release', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '-v', 'q', '--nologo']);
  if (code !== 0) throw new Error(`dotnet build failed with exit code ${code}`);
}

/** `--difficulty`, `--n`, `--concurrency`, `--dump` and anything else are FORWARDED verbatim to the C#
 *  sweep; only `--skip-build` and `--embed-arm` are this module's. Nothing else is stripped, so a flag
 *  added to the bench needs no edit here.
 *
 *  <b>`--embed-arm label=file.gguf` takes its VALUE out too.</b> A stripped flag whose value stayed would
 *  reach the sweep as a bare positional, be read by nothing, and run a measurement nobody asked for. */
export function parseArgs(argv) {
  const embedArms = [];
  const benchArgs = [];
  let nativeModel = null;
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--skip-build') continue;
    if (argv[i] === '--native-model') {
      nativeModel = argv[++i] ?? null;
      if (!nativeModel) throw new Error('tool-affordance: --native-model wants a GGUF filename');
      continue;
    }
    if (argv[i] === '--embed-arm') {
      const spec = argv[++i] ?? '';
      const at = spec.indexOf('=');
      if (at <= 0 || at === spec.length - 1)
        throw new Error(`tool-affordance: --embed-arm wants label=file, got "${spec}"`);
      embedArms.push({ label: spec.slice(0, at), file: spec.slice(at + 1) });
      continue;
    }
    benchArgs.push(argv[i]);
  }
  return { skipBuild: argv.includes('--skip-build'), embedArms, nativeModel, benchArgs };
}

async function main() {
  const { skipBuild, benchArgs, embedArms, nativeModel } = parseArgs(process.argv.slice(2));
  const modelDir = process.env.LYNTAI_MODEL_DIR ?? process.env.LYNTAI_CONTENTION_MODEL_DIR;
  if (!modelDir) {
    console.error('tool-affordance: set LYNTAI_MODEL_DIR to the directory holding the GGUFs.');
    console.error('  No default, by design — a developer-machine path must never reach a tracked file.');
    console.error(`  Needs: ${Object.values(ROLES).map((r) => r.file).join(', ')}`);
    process.exitCode = 2;
    return;
  }

  const repoRoot = path.resolve(path.dirname(here), '..', '..');
  const scratchDir = path.resolve(path.dirname(here), '..', '_affordance');
  fs.mkdirSync(scratchDir, { recursive: true });

  const missing = [...Object.values(ROLES).map((r) => r.file), ...embedArms.map((a) => a.file),
    ...(nativeModel ? [nativeModel] : [])]
    .filter((f) => !fs.existsSync(path.join(modelDir, f)));
  if (missing.length) {
    console.error(`tool-affordance: missing model file(s) in ${modelDir}: ${missing.join(', ')}`);
    process.exitCode = 2;
    return;
  }

  if (!await assertGpuHeadroom()) {
    process.exitCode = 2;
    return;
  }

  const serverExe = await resolveServerExe();
  const before = await neighbourReport(ownedPids());
  console.log(`Neighbour roster BEFORE (${before.length}):`);
  for (const r of before) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);

  if (!skipBuild) await buildBench(repoRoot);

  let pids = [];
  // HOISTED out of the try, because `finally` re-reads the OWNED ports and a port it does not know about
  // is a leak nothing reports — the silent direction of the very check this teardown exists to be.
  const extras = [...extraSpecs(embedArms, modelDir), ...nativeSpecs(nativeModel, modelDir)];
  const ownedPorts = [...Object.values(PORTS), ...extras.map((s) => s.port)];
  const gpu = startGpuSampler();
  try {
    pids = await startServers([...serverSpecs(modelDir), ...extras], { scratchDir, serverExe });
    console.log(`\nServers up on ${[...Object.values(PORTS), ...extras.map((s) => s.port)].join(', ')} `
      + `(pids ${pids.join(', ')})\n`);

    console.log('IDENTITY — what each port actually loaded, read back from the server:');
    const identity = await identityReport(modelDir, PORTS);
    for (const row of identity) {
      const verdict = row.agrees === null ? 'unknown (this build exposes no /props)'
        : row.agrees ? 'agrees' : `*** MISMATCH: serving ${row.served} ***`;
      console.log(`  ${row.role.padEnd(7)} ${row.want}  ${row.bytes} B  — ${verdict}`);
    }
    // REFUSE on a mismatch. The 4B/1B contrast is one of this study's two axes, so a port serving the wrong
    // weights does not degrade the result — it inverts it, and prints one marked line among a hundred.
    if (identity.some((r) => r.agrees === false)) {
      console.error('  Refusing to run: a port is serving weights this harness did not ask for, and model');
      console.error('  SIZE is an axis under test — the table would be wrong rather than noisy.');
      process.exitCode = 1;
      return;
    }

    const screen = await screenReranker(PORTS);
    console.log(`\nRERANK SCREEN (reference pair): ${screen.ok ? 'PASS' : '*** FAIL ***'} — ${screen.detail}`);
    if (!screen.ok) {
      console.error('  Refusing to run: a cross-encoder that cannot order a pair with a published score is');
      console.error('  a broken conversion, and its arm would read as a weak model rather than a dead one.');
      process.exitCode = 1;
      return;
    }

    const code = await runTracked('dotnet', ['run', '-c', 'Release', '--no-build',
      '--project', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '--', '--affordance', ...benchArgs],
      envFor(embedArms, nativeModel));
    if (code !== 0) process.exitCode = code;
  } finally {
    const device = await gpu.stop();
    // Unconditional, not `if (pids.length)`: a start that THREW leaves `pids` empty while being the most
    // likely moment to have leaked one, so the survivor re-read must run on that path above all.
    const { survivors } = await stopServers(pids, ownedPorts);
    if (survivors.length) {
      console.error(`\n*** PORTS STILL LISTENING after teardown: ${JSON.stringify(survivors)} `
        + '(port, pid) — kill these by PID before the next run ***');
      process.exitCode = 1;
    } else {
      console.log(`\nTeardown: all ${ownedPorts.length} owned ports free`);
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
