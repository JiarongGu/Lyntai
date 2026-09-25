// memory-contention — what it costs to serve MANY memory seams from ONE model server.
//
// A probe on 2026-09-11 found that llama-server's ROUTER mode is a process SUPERVISOR — one child server
// per model — so `dedicated` and `router-resident` have the SAME process and memory topology and differ
// only by a reverse-proxy hop. What a router buys is one endpoint, on-demand loading, and eviction;
// residency and routing are INDEPENDENT axes, which is why `--models-max` is the only thing separating the
// two router arms.
//
// This ORCHESTRATOR owns every server process (topology, the busy-device load); the C# `--contention` sweep
// it invokes (`runBench`) only measures, against whichever backend `--verifier` names, and refuses on an
// identity mismatch. `--device quiet|busy|both` decides whether a cell also holds a busy-device load for its
// duration, and `startGpuSampler` makes "the device was quiet" an ASSERTION rather than a hope.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  HARNESS_PORTS, assertGpuIdle, buildBench, censusContamination, getJson, isFree, killTracked,
  llamaServerProcesses, neighbourReport, netstat, ownedPidClosure, ownedPids, parseListeners, postJson,
  resolveServerExe, runTracked, settle, spawnServer, startGpuCensus, startGpuSampler, startServers,
  stopServers, treeKill, vanishedNeighbours, waitUntilListening,
} from './_llama-harness.mjs';

const here = fileURLToPath(import.meta.url);

/** Ports this harness owns (`_llama-harness.mjs`' registry). `load` is the busy-device generator — a second,
 *  small llama-server, checked and torn down under the same rules as every other port here. */
export const PORTS = HARNESS_PORTS['memory-contention'];

/** The busy-device load's own model — the smallest GENERATIVE model on disk, not the smallest file overall.
 *  `ROLES.embed.file` (~333 MB) is smaller still, but the embedder cannot serve `/v1/chat/completions` at
 *  all, and a driven embedding loop would exercise the wrong shape of GPU work — short pooled-batch encodes,
 *  never the sustained token-by-token decode the seams under test (chat annotation, the judge backend)
 *  actually contend on. `gemma-3-1b-it-Q4_K_M.gguf` (~806 MB) is the smallest model that can. */
const LOAD_MODEL_FILE = 'gemma-3-1b-it-Q4_K_M.gguf';

/** Each role's weights, the flags that ENABLE it, and its context/batch SETTINGS — all three live here as
 *  DATA rather than in a call site, because `renderPreset` (the router arms) and `startArm` (the dedicated
 *  arm) must both read the SAME numbers. `dedicated` differing from a router arm by more than routing would
 *  void the whole comparison this bench exists to make.
 *
 *  Settings sizing: `embed` needs its whole sequence in ONE physical batch for pooled embeddings, and 2048
 *  is embeddinggemma's trained window. `rerank` sizes for a query+document PAIR, well under bge-m3's 8192.
 *  `chat` gets only a context size — causal prompts chunk fine at the default physical batch, unlike a
 *  pooled or cross-encoded input. Undersizing fails LOUDLY (`500 … input (N tokens) is too large`), which is
 *  what `probeExtreme` exists to catch — a four-word probe against the unsized default once certified a
 *  server that then rejected a real ~1,500-token turn. `--embedding` and `--reranking` are process-wide
 *  restrictions rather than per-request modes, so a role without its flag answers 501 while still being
 *  listed by the router — which is why the flags live here too, not at a call site. */
export const ROLES = {
  chat:   { file: 'gemma-3-4b-it-Q4_K_M.gguf',      flags: [],              settings: { 'ctx-size': 8192 } },
  embed:  { file: 'embeddinggemma-300M-Q8_0.gguf',  flags: ['embeddings'],  settings: { 'ctx-size': 2048, 'batch-size': 2048, 'ubatch-size': 2048 } },
  rerank: { file: 'bge-reranker-v2-m3-Q8_0.gguf',   flags: ['reranking'],   settings: { 'ctx-size': 4096, 'batch-size': 4096, 'ubatch-size': 4096 } },
};

export const ARMS = [
  { name: 'dedicated',       kind: 'dedicated', modelsMax: null },
  { name: 'router-resident', kind: 'router',    modelsMax: 4 },
  { name: 'router-swapping', kind: 'router',    modelsMax: 1 },
];

/** The INI `--models-preset` takes: section = served id, keys = long-form flags without the `--`.
 *  Format recovered from the router's own `status.preset` field rather than guessed. Emits `ROLES[role]`'s
 *  `settings` as plain `key = value` lines — the SAME keys `settingsArgs` turns into `--`-prefixed CLI args
 *  for the dedicated arm, which is what keeps the two arms comparable. */
export function renderPreset(modelDir, roles) {
  return roles.map((role) => {
    const { file, flags, settings } = ROLES[role];
    const lines = [`[${role}]`, `model = ${modelDir}/${file}`, 'n-gpu-layers = 99'];
    for (const flag of flags) lines.push(`${flag} = true`);
    for (const [key, value] of Object.entries(settings ?? {})) lines.push(`${key} = ${value}`);
    return lines.join('\n');
  }).join('\n\n') + '\n';
}

/** `ROLES[role].settings` as the `--`-prefixed CLI args `llama-server` expects, for the DEDICATED arm.
 *  Exists as its own exported function — rather than inlined at `startArm`'s call site — so a test can
 *  assert this equals what `renderPreset` emits for the same role from the same data: that equivalence is
 *  what keeps `dedicated` comparable to the router arms, so it is the thing to protect with an assertion,
 *  not just with shared data. */
export function settingsArgs(role) {
  return Object.entries(ROLES[role].settings ?? {}).flatMap(([key, value]) => [`--${key}`, String(value)]);
}

export function writePreset(path, modelDir, roles) {
  fs.writeFileSync(path, renderPreset(modelDir, roles), 'utf8');  // never shell redirection: GBK console
  return path;
}



/** Prints the GPU control's line for one cell — the `samples === 0` branch is the "say so" half of "report
 *  `null`, never silently zero": a reader must not be able to mistake an unmeasured cell for a quiet one.
 *
 *  **Utilization does not detect contamination — it SATURATES.** A cell already running real chat/embed/
 *  rerank traffic can sit at 90%+ on its own, so a neighbour arriving on top of that shows up as LATENCY,
 *  not as a visible jump here. `maxMemMiB` is additive against the known idle baseline and worth reading;
 *  `startGpuCensus`/`censusContamination` (printed separately) is what actually attributes contamination,
 *  by PID. */
function printGpuLine(device, gpu) {
  if (gpu.samples === 0) {
    console.log(`GPU (${device}): UNAVAILABLE — nvidia-smi produced zero samples (absent, or every attempt `
      + 'errored). Reporting null, not zero — a zero here would read as a quiet device.');
    return;
  }
  console.log(`GPU (${device}): max ${gpu.maxUtil.toFixed(0)}% util, mean ${gpu.meanUtil.toFixed(1)}% util, `
    + `max ${gpu.maxMemMiB.toFixed(0)} MiB used (${gpu.samples} samples) — informational; see the census `
    + 'line below for contamination');
}

/** Prints the census's verdict for one cell — CLEAN (only this run's own PIDs and the pre-run-known
 *  neighbour touched the device), CONTAMINATED (named, by PID — never an aggregate), or UNCHECKED
 *  (`nvidia-smi --query-compute-apps` never once answered). */
function printCensusLine(device, census, ownPids, knownNeighbourPids) {
  if (!census.sampledOk) {
    console.log(`GPU census (${device}): UNCHECKED — nvidia-smi --query-compute-apps never answered. `
      + 'Contamination NOT ruled out this cell.');
    return;
  }
  const contaminants = censusContamination(census.pids, ownPids, knownNeighbourPids);
  if (contaminants.length) {
    console.error(`GPU CENSUS CONTAMINATED (${device}): unexpected PID(s) using the device this cell: `
      + contaminants.join(', '));
    process.exitCode = 1;
    return;
  }
  console.log(`GPU census (${device}): clean — only this run's PID(s) [${ownPids.join(',') || 'none'}] `
    + `and the known neighbour [${knownNeighbourPids.join(',') || 'none'}] touched the device.`);
}


/** Env every arm exports. ALL SIX are set explicitly and spell 127.0.0.1: `localhost` resolves to ::1 first
 *  on Windows and costs ~1.8s per call against an IPv4-only listener, which on a LATENCY bench is not a
 *  degradation but a fabrication. The bench's own defaults spell `localhost`, so inheriting them is the bug. */
function envFor(arm) {
  const at = (p) => `http://127.0.0.1:${p}`;
  return arm.kind === 'router'
    ? { LYNTAI_LIVE_CHAT_URL: at(PORTS.router), LYNTAI_LIVE_CHAT_MODEL: 'chat',
        LYNTAI_LIVE_MODEL_URL: at(PORTS.router), LYNTAI_LIVE_EMBED_MODEL: 'embed',
        LYNTAI_LIVE_RERANK_URL: at(PORTS.router), LYNTAI_LIVE_RERANK_MODEL: 'rerank' }
    : { LYNTAI_LIVE_CHAT_URL: at(PORTS.chat), LYNTAI_LIVE_CHAT_MODEL: 'chat',
        LYNTAI_LIVE_MODEL_URL: at(PORTS.embed), LYNTAI_LIVE_EMBED_MODEL: 'embed',
        LYNTAI_LIVE_RERANK_URL: at(PORTS.rerank), LYNTAI_LIVE_RERANK_MODEL: 'rerank' };
}


export async function startArm(arm, { modelDir, scratchDir, serverExe }) {
  const specs = arm.kind === 'router'
    ? [{
        label: arm.name,
        port: PORTS.router,
        argv: ['--models-preset',
          writePreset(`${scratchDir}/presets-${arm.name}.ini`, modelDir, ['chat', 'embed', 'rerank']),
          '--port', String(PORTS.router), '--host', '127.0.0.1', '--models-max', String(arm.modelsMax)],
      }]
    : ['chat', 'embed', 'rerank'].map((role) => ({
        label: `${arm.name}-${role}`,
        port: PORTS[role],
        argv: ['--model', `${modelDir}/${ROLES[role].file}`, '--alias', role,
          '--port', String(PORTS[role]), '--host', '127.0.0.1', '--n-gpu-layers', '99',
          ...ROLES[role].flags.map((f) => `--${f}`), ...settingsArgs(role)],
      }));

  const pids = await startServers(specs, { scratchDir, serverExe });
  return { arm, pids, env: envFor(arm), endpoints: envFor(arm) };
}

export async function stopArm(handle) {
  return stopServers(handle.pids, Object.values(PORTS));
}

/** The busy-device LOAD: a second, small llama-server generating tokens continuously so the shared
 *  GPU is occupied for the whole cell — the closest same-process analog to the game-rendering neighbour
 *  `pitfalls.md` measured the 12x-faster-to-26x-slower swing against.
 *
 *  **This is a COMPUTE load, not the GRAPHICS workload that measurement used.** The DIRECTION transfers —
 *  contention should still hit generation far harder than encoding — the MAGNITUDE may not. Read every
 *  figure taken under `--device busy` with that attached, the same posture this repository takes for every
 *  borrowed number.
 *
 *  Checks and records its port exactly like every other server here, and reaches STEADY STATE — every
 *  worker's FIRST generation round has genuinely SUCCEEDED, not merely completed — before returning, rather
 *  than a fixed sleep. **A round that 500s still "completes"**: an earlier version signalled ready the
 *  moment a round returned regardless of outcome, so a load that could not serve a single request would
 *  report "steady" while applying zero real GPU work — the mixed cell would then be measured under a device
 *  labelled busy but not actually contended. The same vacuous-control shape this bench has already hit
 *  three times (`CountingAnnotation` polluted by the seed, the reranker polluted by its own probe, the
 *  mixed cell credited with free empty recalls) — verify the thing WORKS, not merely that it RAN. */
export async function startBusyLoad({ modelDir, scratchDir, serverExe, concurrency = 2 }) {
  const listeners = parseListeners(await netstat());
  if (!isFree(listeners, PORTS.load)) {
    throw new Error(`load port ${PORTS.load} already LISTENING — refusing to bind`);
  }
  const modelPath = `${modelDir}/${LOAD_MODEL_FILE}`;
  if (!fs.existsSync(modelPath)) {
    // Without this, a missing model still spawns the server, still binds the port, and only fails at the
    // 180s waitUntilListening timeout — the slowest possible way to learn the file is not there.
    throw new Error(`busy-load model not found: ${modelPath}`);
  }

  const pid = spawnServer(serverExe, ['--model', modelPath, '--alias', 'load',
    '--port', String(PORTS.load), '--host', '127.0.0.1', '--n-gpu-layers', '99'], scratchDir, 'load');
  try {
    await waitUntilListening([PORTS.load]);
  } catch (err) {
    await treeKill(pid);
    throw err;
  }

  let stopped = false;
  const url = `http://127.0.0.1:${PORTS.load}/v1/chat/completions`;
  const prompt = 'Write a long, vivid, detailed description of a rainstorm moving across a city at night.';

  // A round is a SUCCESS only if the server actually returned a completion — a thrown request (timeout,
  // connection reset, a 500) or an empty body is a MISS, exactly like every other control in this file.
  async function oneRound() {
    try {
      const res = await postJson(url, { model: 'load', temperature: 0.7, max_tokens: 256,
        messages: [{ role: 'user', content: prompt }] }, { timeoutMs: 30_000 });
      return Boolean(res?.choices?.[0]?.message);
    } catch {
      return false;
    }
  }

  // STEADY STATE, not a fixed sleep, and not merely "completed": every worker's first round must have
  // SUCCEEDED before this returns. Abort (killing the pid this module just spawned) rather than proceeding
  // with an unverified load — a caller must never be handed a "steady" load that cannot actually serve.
  const firstRoundResults = await Promise.all(Array.from({ length: concurrency }, oneRound));
  if (firstRoundResults.some((ok) => !ok)) {
    await treeKill(pid);
    throw new Error('busy load: at least one worker\'s first generation round failed — refusing to call '
      + 'this load steady. Check the load server\'s log in the scratch directory.');
  }

  const loops = Array.from({ length: concurrency }, () => (async () => {
    while (!stopped) { await oneRound(); }        // best effort thereafter — one dropped round does not end the load
  })());

  return {
    pid,
    port: PORTS.load,
    async stop() {
      stopped = true;
      await Promise.all(loops);
      await treeKill(pid);
      await settle();
    },
  };
}

/** Verify IDENTITY, not plausibility. A shape check cannot separate two models that share a shape — a
 *  neighbour's embedder passed a "is it 768-dimensional?" probe while being the wrong model entirely. */
export async function verifyIdentity(endpoints) {
  const chat = await postJson(`${endpoints.LYNTAI_LIVE_CHAT_URL}/v1/chat/completions`,
    { model: endpoints.LYNTAI_LIVE_CHAT_MODEL, temperature: 0, max_tokens: 4,
      messages: [{ role: 'user', content: 'Reply with the single character: 1' }] });
  if (!chat?.choices?.[0]?.message?.content?.includes('1')) return { ok: false, detail: 'chat did not answer' };

  const embed = await postJson(`${endpoints.LYNTAI_LIVE_MODEL_URL}/v1/embeddings`,
    { model: endpoints.LYNTAI_LIVE_EMBED_MODEL, input: 'the sky is blue' });
  const dims = embed?.data?.[0]?.embedding?.length;
  if (dims !== 768) return { ok: false, detail: `embedder returned ${dims} dims, expected 768` };

  // The reranker smoke test `pitfalls.md` requires: ORDERING and DISTINCT scores. A community quant missing
  // `cls.output.weight` still loads and still returns scores — they are simply wrong, and a flat scorer
  // reads as a clean null result rather than as the broken instrument it is.
  const rr = await postJson(`${endpoints.LYNTAI_LIVE_RERANK_URL}/v1/rerank`,
    { model: endpoints.LYNTAI_LIVE_RERANK_MODEL, query: 'what colour is the sky',
      documents: ['the sky is blue', 'bread is baked'], top_n: 2 });
  const scores = rr?.results?.map((r) => r.relevance_score);
  if (!scores || scores.length !== 2) return { ok: false, detail: 'reranker did not answer' };
  const best = rr.results.reduce((a, b) => (a.relevance_score > b.relevance_score ? a : b));
  if (best.index !== 0) return { ok: false, detail: 'reranker ranked the known answer second' };
  if (scores[0] === scores[1]) return { ok: false, detail: 'reranker returned FLAT scores — missing head?' };
  return { ok: true, detail: `chat ok, embed ${dims}d, rerank ${scores.map(s => s.toFixed(2)).join('/')}` };
}

/** The LONGEST input this harness can emit, not a typical one. A four-word probe certified a server that
 *  then returned `500 input (1442 tokens) is too large` on a real turn and cost an hour of ingestion. */
export async function probeExtreme(endpoints, longestInput) {
  const embed = await postJson(`${endpoints.LYNTAI_LIVE_MODEL_URL}/v1/embeddings`,
    { model: endpoints.LYNTAI_LIVE_EMBED_MODEL, input: longestInput });
  if (!embed?.data?.[0]?.embedding) return { ok: false, detail: 'embedder rejected the longest input' };
  const chat = await postJson(`${endpoints.LYNTAI_LIVE_CHAT_URL}/v1/chat/completions`,
    { model: endpoints.LYNTAI_LIVE_CHAT_MODEL, temperature: 0, max_tokens: 8,
      messages: [{ role: 'user', content: longestInput }] });
  return chat?.choices?.[0] ? { ok: true, detail: `extreme ${longestInput.length} chars accepted` }
                            : { ok: false, detail: 'chat rejected the longest input' };
}


/** Read the router's own model-load ledger off `/v1/models`' `status.value` (the literal strings observed
 *  are `"loaded"` / `"unloaded"`). This is the ENTIRE content of the `router-swapping` arm: without a load
 *  COUNT, that arm's latency numbers cannot say whether a swap happened, which makes them unreadable.
 *
 *  A `dedicated` arm's endpoint is a plain single-model server whose `/v1/models` entries carry no `status`
 *  field at all, so every entry falls through both branches below and `{loaded: [], unloaded: []}` is the
 *  result BY CONSTRUCTION, not a special case: nothing can swap when each model owns a process that is
 *  never evicted. */
export async function modelLoadState(endpoints) {
  const body = await getJson(`${endpoints.LYNTAI_LIVE_CHAT_URL}/v1/models`);
  const entries = body?.data ?? [];
  const loaded = [];
  const unloaded = [];
  for (const entry of entries) {
    const status = entry?.status?.value;
    const id = entry?.id ?? entry?.model ?? null;
    if (status === 'loaded') loaded.push(id);
    else if (status === 'unloaded') unloaded.push(id);
  }
  return { loaded, unloaded };
}

/** A synthetic worst-case single memory entry: real sentences repeated to ~6,000 characters, the figure
 *  this repository's other benches already truncate LongMemEval texts to (`pitfalls.md`). Not a corpus
 *  value. */
function syntheticLongestInput(chars = 6_000) {
  const sentence = 'The reference model answered correctly on the first attempt near the riverbank at dusk. ';
  let s = '';
  while (s.length < chars) s += sentence;
  return s.slice(0, chars);
}

/** `--device quiet|busy|both` (default `quiet`), `--arms a,b`, `--smoke`, and everything else FORWARDED
 *  verbatim to the C# `--contention` sweep (`--verifier`, `--workers`, `--repeat`, `--writes`, `--recalls`).
 *  Only `--device` and `--arms` are stripped from `benchArgs`: topology and the device axis are THIS
 *  module's concern, and the C# bench has no flag for either. An unrecognised `--device` value falls back
 *  to `quiet` rather than refusing, matching every flag parser in the C# bench this forwards to. */
export function parseArgs(argv) {
  const armsFlag = argv.indexOf('--arms');
  const armNames = armsFlag >= 0 ? argv[armsFlag + 1].split(',').filter(Boolean) : null;

  const deviceFlag = argv.indexOf('--device');
  const deviceRaw = deviceFlag >= 0 ? argv[deviceFlag + 1] : 'quiet';
  const devices = deviceRaw === 'both' ? ['quiet', 'busy'] : deviceRaw === 'busy' ? ['busy'] : ['quiet'];

  const strip = new Set();
  if (armsFlag >= 0) { strip.add(armsFlag); strip.add(armsFlag + 1); }
  if (deviceFlag >= 0) { strip.add(deviceFlag); strip.add(deviceFlag + 1); }
  const benchArgs = argv.filter((_, i) => !strip.has(i));

  return { armNames, smoke: argv.includes('--smoke'), devices, benchArgs };
}

async function reportNeighbours(label) {
  const rows = await neighbourReport(ownedPids());
  console.log(`Neighbour roster ${label} (${rows.length}):`);
  for (const r of rows) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);
  return rows;
}


/** Spawns the C# `--contention` sweep against a running arm's servers, `--no-build` (`buildBench`, in the harness).
 *  `LYNTAI_CONTENTION_DEVICE` is a LABEL only — the bench reads it to describe the run correctly
 *  (`PrintNotSwept`), never to change what it measures; the orchestrator alone decides whether a busy load
 *  actually runs. */
function runBench(repoRoot, benchArgs, device, envOverlay) {
  const benchProject = path.join(repoRoot, 'bench', 'Lyntai.Benchmarks');
  return runTracked('dotnet',
    ['run', '-c', 'Release', '--project', benchProject, '--no-build', '--', '--contention', ...benchArgs],
    { ...envOverlay, LYNTAI_CONTENTION_DEVICE: device });
}

/** One device state's worth of measurement against an already-started arm: optionally hold a busy-device
 *  load for the WHOLE cell (steady state verified before, torn down and verified gone after), sample the
 *  GPU AND census the GPU's process list for the WHOLE cell (not bookend before/after — see
 *  `startGpuSampler`), and run the C# `--contention` sweep inside that window. A busy
 *  load that never reaches steady state aborts THIS cell (non-zero exit, clear message) rather than
 *  measuring a device labelled busy that is not actually contended. */
async function runMeasuredCell(arm, device, handle, ownedPids, knownNeighbourPids,
  { modelDir, scratchDir, serverExe, benchArgs, repoRoot }) {
  console.log(`\n--- device: ${device} (arm ${arm.name}) ---`);
  let busy = null;
  if (device === 'busy') {
    try {
      busy = await startBusyLoad({ modelDir, scratchDir, serverExe });
    } catch (err) {
      console.error(`ABORTING this cell — busy-device load did not reach steady state: ${err.message}`);
      process.exitCode = 1;
      return;
    }
    console.log(`busy load steady: pid ${busy.pid} on port ${busy.port}`);
  }
  try {
    // A router arm's `ownedPids` is only the router process itself — its per-model children are a SEPARATE
    // PID the census would otherwise flag as contamination. Walk the same transitive closure `neighbourPids`
    // uses so a router's own children count as ours.
    // KNOWN LIMIT: one SNAPSHOT, taken before the cell. `router-swapping` evicts and reloads a model
    // mid-cell, so that child arrives after this and reads as contamination — a false ALARM, never a wrong
    // number, and `dedicated` (the default, and the only arm any published figure used) cannot hit it.
    const seedPids = busy ? [...ownedPids, busy.pid] : ownedPids;
    const rows = await llamaServerProcesses();
    const ownPids = [...ownedPidClosure(rows, seedPids)];
    const sampler = startGpuSampler();
    const census = startGpuCensus();
    try {
      const exitCode = await runBench(repoRoot, benchArgs, device, handle.env);
      if (exitCode !== 0) process.exitCode = 1;
    } finally {
      // stop() must run even if runBench rejects, or the sampler/census polling timers keep this process
      // alive forever.
      const gpu = await sampler.stop();
      const censusResult = await census.stop();
      printGpuLine(device, gpu);
      printCensusLine(device, censusResult, ownPids, knownNeighbourPids);
    }
  } finally {
    if (busy) {
      await busy.stop();
      const listeners = parseListeners(await netstat());
      if (!isFree(listeners, PORTS.load)) {
        console.error(`SURVIVOR: load port ${PORTS.load} still LISTENING after teardown`);
        process.exitCode = 1;
      } else {
        console.log('busy load generator confirmed gone');
      }
    }
  }
}

async function runArm(arm, knownNeighbourPids, { modelDir, scratchDir, serverExe, smoke, devices, benchArgs, repoRoot }) {
  console.log(`\n=== arm: ${arm.name} (${arm.kind}) ===`);
  // Registration happens INSIDE startArm → spawnServer, at spawn time — not here — so a SIGINT during the
  // model-load wait is already covered by the time this line returns.
  const handle = await startArm(arm, { modelDir, scratchDir, serverExe });
  console.log(`started, pids: ${handle.pids.join(', ')}`);
  try {
    const identity = await verifyIdentity(handle.endpoints);
    console.log(`identity: ${identity.ok ? 'OK' : 'FAIL'} — ${identity.detail}`);
    if (!identity.ok) { process.exitCode = 1; return; }

    const extreme = await probeExtreme(handle.endpoints, syntheticLongestInput());
    console.log(`extreme:  ${extreme.ok ? 'OK' : 'FAIL'} — ${extreme.detail}`);
    if (!extreme.ok) { process.exitCode = 1; return; }

    if (smoke) {
      const loadState = await modelLoadState(handle.endpoints);
      console.log(`model-load-state: loaded=[${loadState.loaded.join(',')}] unloaded=[${loadState.unloaded.join(',')}]`);
    }

    for (const device of devices) {
      await runMeasuredCell(arm, device, handle, handle.pids, knownNeighbourPids,
        { modelDir, scratchDir, serverExe, benchArgs, repoRoot });
    }
  } finally {
    const stopped = await stopArm(handle);
    console.log(`stopped. survivors on ${Object.values(PORTS).join('/')}: ${stopped.survivors.length}`);
    if (stopped.survivors.length) {
      console.error(`SURVIVORS: ${JSON.stringify(stopped.survivors)}`);
      process.exitCode = 1;
    }
  }
}

async function main() {
  const { armNames, smoke, devices, benchArgs } = parseArgs(process.argv.slice(2));
  const modelDir = process.env.LYNTAI_CONTENTION_MODEL_DIR;
  if (!modelDir) throw new Error('LYNTAI_CONTENTION_MODEL_DIR is not set — no default, by design');

  const scratchDir = path.resolve(path.dirname(here), '..', '_contention');
  fs.mkdirSync(scratchDir, { recursive: true });
  const repoRoot = path.resolve(path.dirname(here), '..', '..');

  // Refuses BEFORE anything else starts if a game or another load is already running — the census below
  // only catches a contaminant that arrives DURING a cell; this catches one that was there all along.
  await assertGpuIdle();

  // Built ONCE, before any server or the busy load starts — see `buildBench`'s own doc for why a per-cell
  // `dotnet run` implicit build would otherwise sit inside the sampled window.
  await buildBench(repoRoot);

  const serverExe = await resolveServerExe();
  console.log(`llama-server resolved from PATH: ${serverExe}`);

  // Default to `dedicated` ONLY — topology is a footnote, not a measured axis: the router-is-a-supervisor
  // finding (this file's own header comment) showed `dedicated` and `router-resident` differ only by a
  // reverse-proxy hop. The measured grid is {verifier} x {solo, mixed} x {device}, on `dedicated`; `--arms`
  // still exists for anyone who explicitly wants the topology ladder's own identity/extreme check.
  const targets = armNames ? ARMS.filter((a) => armNames.includes(a.name))
                            : ARMS.filter((a) => a.name === 'dedicated');
  if (targets.length === 0) throw new Error(`no matching arm(s) in --arms ${armNames?.join(',')}`);

  if (devices.includes('busy')) {
    console.log('\nNOTE: the busy-device load below is a COMPUTE load — a second llama-server generating '
      + 'tokens — not the GRAPHICS workload (a rendering game) pitfalls.md measured the 12x-faster-to-26x'
      + '-slower swing on. The DIRECTION should transfer; the MAGNITUDE may not. Read every figure from this '
      + 'device axis with that attached.');
  }

  const before = await reportNeighbours('BEFORE');
  // Captured ONCE, before the first cell — never hardcoded to a specific PID, because a neighbour's PID is
  // only ever true for one session. This is the "known neighbour" half of the census's expected-PID set;
  // `ownPids` (this run's own servers, computed per cell in `runMeasuredCell`) is the other half.
  const knownNeighbourPids = before.map((r) => r.pid);

  for (const arm of targets) {
    await runArm(arm, knownNeighbourPids, { modelDir, scratchDir, serverExe, smoke, devices, benchArgs, repoRoot });
  }

  const after = await reportNeighbours('AFTER');
  // By PID, not by aggregate count or "is anything still alive" — killing PID 22464 while some OTHER
  // llama-server happens to be up must still be reported, not hidden behind an aggregate that stayed non-zero.
  const vanished = vanishedNeighbours(before, after);
  if (vanished.length) {
    console.error(`NEIGHBOUR PID(S) GONE OR NOT ANSWERING: ${vanished.join(', ')} — restart before doing anything else.`);
    process.exitCode = 1;
  }
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  // Gated here, not at module scope: importing this file for a test must attach no process-wide listener.
  process.on('SIGINT', async () => {
    const pids = ownedPids();
    console.error(`\nSIGINT — killing ${pids.length} tracked PID(s) before exit: ${pids.join(', ')}`);
    await killTracked(pids);
    process.exit(130);
  });
  main().catch((err) => {
    console.error(err);
    process.exitCode = 1;
  });
}
