// memory-contention — what it costs to serve MANY memory seams from ONE model server.
//
// Read the spec's §1 before changing an arm here. A probe on 2026-09-11 found that llama-server's ROUTER
// mode is a process SUPERVISOR — one child server per model — so `dedicated` and `router-resident` have
// the SAME process and memory topology and differ only by a reverse-proxy hop. What a router buys is one
// endpoint, on-demand loading, and eviction; residency and routing are INDEPENDENT axes, which is why
// `--models-max` is the only thing separating the two router arms.
//
// This ORCHESTRATOR owns every server process (topology, the busy-device load); the C# `--contention` sweep
// it invokes (`runBench`) only measures, against whichever backend `--verifier` names, and refuses on an
// identity mismatch. `--device quiet|busy|both` decides whether a cell also holds a busy-device load for its
// duration, and `startGpuSampler` makes "the device was quiet" an ASSERTION rather than a hope.
import fs from 'node:fs';
import path from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';

const execFileAsync = promisify(execFile);
const here = fileURLToPath(import.meta.url);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Ports this harness owns. 8090 is DELIBERATELY absent: a sibling tool's embedding server holds it, and
 *  killing it by image name has already happened twice (`pitfalls.md`). `load` is the busy-device generator
 *  (Task 8) — a second, small llama-server, checked and torn down under the same rules as every other port
 *  here. */
export const PORTS = { router: 8140, chat: 8141, embed: 8142, rerank: 8143, load: 8144 };

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

/** LISTENING rows only. A bare port match also hits TIME_WAIT and reports a free port BUSY — the false
 *  ABORT that mirrors the false PROCEED of binding a port someone else owns. */
export function parseListeners(netstatText) {
  const listeners = new Map();
  for (const line of netstatText.split(/\r?\n/)) {
    const m = /^\s*TCP\s+\S+?:(\d+)\s+\S+\s+LISTENING\s+(\d+)/.exec(line);
    if (m) listeners.set(Number(m[1]), Number(m[2]));
  }
  return listeners;
}

export const isFree = (listeners, port) => !listeners.has(port);

/** Every llama-server PID that is neither ours nor a descendant of ours. Enumerating PROCESSES rather than
 *  a port list is the point: the sibling's server was invisible to a sweep of the ports we planned to use. */
export function neighbourPids(rows, ownPids) {
  const mine = new Set(ownPids);
  let grew = true;
  while (grew) {               // transitive: the router's children spawn grandchildren
    grew = false;
    for (const r of rows) if (!mine.has(r.pid) && mine.has(r.ppid)) { mine.add(r.pid); grew = true; }
  }
  return rows.filter((r) => !mine.has(r.pid)).map((r) => r.pid);
}

// ---------------------------------------------------------------------------------------------------------
// Task 8: the GPU-quiet CONTROL. Every other control on this bench makes a bad cell VISIBLE (hit-rate for
// an empty recall, subjects/write for a no-op annotation, distinct-rerank-scores for a flat reranker); the
// ENVIRONMENT had none, so a contaminated run was indistinguishable from a clean one. Before/after sampling
// is NOT enough — a stream that starts and stops INSIDE one cell is exactly the case `pitfalls.md` measured
// (generation 12x faster to 26x slower, a ~300x reversal), and bookend sampling cannot see it. Sampling
// therefore runs FOR THE DURATION of the cell, on an interval, via `startGpuSampler`/`stop()`.
// ---------------------------------------------------------------------------------------------------------

/** One `nvidia-smi --query-gpu=utilization.gpu,memory.used --format=csv,noheader` line, e.g. `"3 %, 2618
 *  MiB"` or the `nounits` form `"3, 2618"` — `parseFloat` reads the leading number off either and ignores
 *  the unit suffix, so both are accepted without a format flag dependency. `null` for anything that does
 *  not parse as two numbers, which is what a missing/errored `nvidia-smi` reports upstream. */
export function parseGpuSample(csvText) {
  const line = String(csvText).split(/\r?\n/).find((l) => l.trim().length > 0);
  if (!line) return null;
  const [utilRaw, memRaw] = line.split(',');
  const util = Number.parseFloat(utilRaw);
  const memMiB = Number.parseFloat(memRaw);
  return Number.isFinite(util) && Number.isFinite(memMiB) ? { util, memMiB } : null;
}

/** `{maxUtil, meanUtil, maxMemMiB, samples}` over whatever landed. **Zero samples reports NULL fields, never
 *  zero** — a cell where `nvidia-smi` is absent or every attempt errored must read as UNMEASURED, not as a
 *  quiet device, or a broken sampler would look exactly like the thing it exists to rule out. */
export function aggregateGpuSamples(samples) {
  if (samples.length === 0) return { maxUtil: null, meanUtil: null, maxMemMiB: null, samples: 0 };
  const utils = samples.map((s) => s.util);
  const mems = samples.map((s) => s.memMiB);
  return {
    maxUtil: Math.max(...utils),
    meanUtil: utils.reduce((a, b) => a + b, 0) / utils.length,
    maxMemMiB: Math.max(...mems),
    samples: samples.length,
  };
}

/** One attempt, swallowing everything (absent binary, transient error) to `null` — the caller counts
 *  successful samples rather than reading an exception as "the device is quiet". */
async function sampleGpuOnce() {
  try {
    const { stdout } = await execFileAsync('nvidia-smi',
      ['--query-gpu=utilization.gpu,memory.used', '--format=csv,noheader']);
    return parseGpuSample(stdout);
  } catch {
    return null;
  }
}

/** Starts polling `nvidia-smi` on `intervalMs`, immediately (not after the first interval, so a short cell
 *  still gets a data point) and for as long as the caller holds the handle. `stop()` ends the loop and
 *  returns `aggregateGpuSamples` of whatever landed — awaiting the in-flight poll first, so a `stop()` that
 *  races a sample never drops it. */
export function startGpuSampler({ intervalMs = 1_000 } = {}) {
  const samples = [];
  let stopped = false;
  const loop = (async () => {
    while (!stopped) {
      const sample = await sampleGpuOnce();
      if (sample) samples.push(sample);
      if (stopped) break;
      await sleep(intervalMs);
    }
  })();
  return {
    async stop() {
      stopped = true;
      await loop;
      return aggregateGpuSamples(samples);
    },
  };
}

/** Prints the GPU control's line for one cell — the `samples === 0` branch is the "say so" half of "report
 *  `null`, never silently zero": a reader must not be able to mistake an unmeasured cell for a quiet one. */
function printGpuLine(device, gpu) {
  if (gpu.samples === 0) {
    console.log(`GPU (${device}): UNAVAILABLE — nvidia-smi produced zero samples (absent, or every attempt `
      + 'errored). Reporting null, not zero — a zero here would read as a quiet device.');
    return;
  }
  console.log(`GPU (${device}): max ${gpu.maxUtil.toFixed(0)}% util, mean ${gpu.meanUtil.toFixed(1)}% util, `
    + `max ${gpu.maxMemMiB.toFixed(0)} MiB used (${gpu.samples} samples)`);
}

// ---------------------------------------------------------------------------------------------------------
// Task 3: server lifecycle — spawn, readiness, identity, teardown. Every PID this module kills was recorded
// by THIS module when it spawned the process; nothing here ever kills by image name (windows-machine.md).
// ---------------------------------------------------------------------------------------------------------

/** Raw `netstat -ano` text, fetched fresh on every call — the caller decides whether a moment already
 *  superseded is good enough, this function never decides that for them. `maxBuffer` is raised well past
 *  `execFile`'s 1 MB default: a busy machine's connection table can exceed it, and the default failure mode
 *  is a silent truncation or a thrown error, either of which reads as "no listeners" to a caller checking a
 *  specific port. */
async function netstat() {
  const { stdout } = await execFileAsync('netstat', ['-ano'], { maxBuffer: 16 * 1024 * 1024 });
  return stdout;
}

/** Resolve `llama-server` from PATH BY NAME — never a hardcoded absolute path in a tracked file. `where`
 *  can print more than one match; the first line is the one Windows would actually run. */
export async function resolveServerExe() {
  const finder = process.platform === 'win32' ? 'where' : 'which';
  const { stdout } = await execFileAsync(finder, ['llama-server']);
  const first = stdout.split(/\r?\n/).map((s) => s.trim()).find(Boolean);
  if (!first) throw new Error('llama-server not found on PATH');
  return first;
}

/** Every `llama-server.exe` process on the machine, as `{pid, ppid, port}` — `port` is the LISTENING port
 *  this exact PID owns, or `null` for a router CHILD, which is supervised over an internal channel rather
 *  than its own TCP listener (the router-is-a-supervisor shape this file's header comment records).
 *  Enumerating PROCESSES rather than a port list is the point: a neighbour holding a port nobody swept for
 *  is invisible to a port-only census, which is exactly how it was missed twice before. */
async function llamaServerProcesses() {
  const { stdout } = await execFileAsync('powershell', ['-NoProfile', '-NonInteractive', '-Command',
    "Get-CimInstance Win32_Process -Filter \"Name='llama-server.exe'\" | " +
    'Select-Object ProcessId,ParentProcessId | ConvertTo-Json']);
  // Windows PowerShell 5.1's ConvertTo-Json has no -AsArray: a SINGLE match prints a bare object rather
  // than a one-element array, so a naive JSON.parse().map would throw on the single-neighbour case.
  const trimmed = stdout.trim();
  const parsed = trimmed ? JSON.parse(trimmed) : [];
  const rows = Array.isArray(parsed) ? parsed : [parsed];
  const listeners = parseListeners(await netstat());
  const portByPid = new Map([...listeners].map(([port, pid]) => [pid, port]));
  return rows.map((r) => ({ pid: r.ProcessId, ppid: r.ParentProcessId, port: portByPid.get(r.ProcessId) ?? null }));
}

/** Does this port answer `/health`? A LISTENING port that never answers is as good as down to anything
 *  about to depend on it, which is why the neighbour census checks this rather than stopping at netstat. */
async function answers(port) {
  if (!port) return false;
  try {
    const res = await fetch(`http://127.0.0.1:${port}/health`, { signal: AbortSignal.timeout(3_000) });
    return res.ok;
  } catch {
    return false;
  }
}

/** Every PID this module has spawned and not yet confirmed exited, registered at SPAWN time — not after
 *  `startArm` returns — so a SIGINT during the multi-gigabyte model LOAD window (the longest and most likely
 *  window to be interrupted in) still reaches it. A bare module-level `Set` has no side effect worth gating
 *  on import (nothing is spawned, nothing is listened for); only the `process.on('SIGINT', …)` registration
 *  below is gated behind `import.meta.main`, since THAT is the thing importing this module for a test must
 *  not attach. */
const activePids = new Set();

/** pid -> ChildProcess for everything still considered ours. Windows can RECYCLE a pid the instant its
 *  owning process exits, so `treeKill` must never blindly `/T` a pid this map no longer vouches for — an
 *  early-exited child's slot could belong to a stranger by the time teardown actually runs. Cleared by the
 *  same `exit`/`error` handling that clears `activePids`. */
const spawnedChildren = new Map();

/** Spawn one server with its stdout/stderr captured to its OWN log file rather than inherited — three
 *  servers sharing this process's console would interleave unreadably. Deliberately NOT `detached`: this
 *  process stays alive for the whole `startArm`..`stopArm` span and tears children down explicitly by PID,
 *  and `detached` solves a problem this call does not have (a detached child survives only a parent that
 *  outlives it — the wrong property here, and the exact trap that once reported an embedder "started"
 *  while it never came up). Tracks the CHILD, not just its pid: an early exit must drop the pid from both
 *  registries below, and an `error` event with no listener (a bad exe, `ENOENT`) would otherwise crash this
 *  whole process asynchronously, well after this function has already returned. */
function spawnServer(serverExe, args, scratchDir, label) {
  const outFd = fs.openSync(path.join(scratchDir, `${label}.out.log`), 'a');
  const errFd = fs.openSync(path.join(scratchDir, `${label}.err.log`), 'a');
  let closed = false;
  const closeFds = () => {
    if (closed) return;
    closed = true;
    try { fs.closeSync(outFd); } catch { /* already closed */ }
    try { fs.closeSync(errFd); } catch { /* already closed */ }
  };

  let child;
  try {
    child = spawn(serverExe, args, { stdio: ['ignore', outFd, errFd] });
  } catch (err) {
    closeFds();     // spawn threw synchronously — 'exit' will never fire to release these fds
    throw err;
  }

  // Attached BEFORE the pid check below, not after: a failed spawn (bad exe, ENOENT) can leave `child.pid`
  // undefined SYNCHRONOUSLY while still queuing an async 'error' event for next tick. Checking pid first and
  // attaching listeners only in the success path would leave that queued event with no listener, which
  // crashes this whole process well after this call has already returned.
  const forget = () => {
    if (child.pid != null) { spawnedChildren.delete(child.pid); activePids.delete(child.pid); }
    closeFds();
  };
  child.once('exit', forget);
  child.once('error', (err) => {
    console.error(`spawnServer: ${label} (pid ${child.pid ?? '?'}) — ${err.message}`);
    forget();
  });

  if (!child.pid) { closeFds(); throw new Error(`spawn did not return a pid for ${label}`); }
  spawnedChildren.set(child.pid, child);
  activePids.add(child.pid);
  return child.pid;
}

/** Poll until every port is BOUND and answering `/health` — binding is necessary and not sufficient. A
 *  `spawn` that did not throw has not started anything, and a model still loading answers the TCP
 *  handshake before it answers a request, so a bind-only wait would race the model load. The timeout is
 *  generous on purpose: the chat model alone is multiple gigabytes and three servers may load together. */
async function waitUntilListening(ports, { timeoutMs = 180_000, intervalMs = 500 } = {}) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const listeners = parseListeners(await netstat());
    if (ports.every((p) => !isFree(listeners, p))) {
      const healthy = await Promise.all(ports.map((p) => answers(p)));
      if (healthy.every(Boolean)) return;
    }
    await sleep(intervalMs);
  }
  throw new Error(`timed out waiting for ports to become healthy: ${ports.join(', ')}`);
}

/** Kill one PID and its tree. "Already gone" is success, not failure — anything else is re-thrown so a
 *  caller killing several PIDs in a loop finds out which one actually failed. The exit code is still not
 *  trusted for anything beyond that: `stopArm` re-reads `netstat` rather than believing this succeeded.
 *
 *  If `spawnedChildren` no longer vouches for this pid, it already exited on its own — do NOT `/T` it
 *  regardless: Windows can hand that exact number to an unrelated process the moment ours released it, and
 *  killing "our" pid at that point would kill a stranger's tree. */
async function treeKill(pid) {
  if (!spawnedChildren.has(pid)) { activePids.delete(pid); return; }
  try {
    await execFileAsync('taskkill', ['/F', '/PID', String(pid), '/T']);
  } catch (err) {
    const msg = String(err?.stderr ?? err?.message ?? '');
    if (!/not found/i.test(msg)) throw err;
  }
  spawnedChildren.delete(pid);
  activePids.delete(pid);
}

/** A short pause after teardown, before re-reading `netstat` — a killed listener's socket does not always
 *  release instantly, and the point of re-reading at all is to see the settled state, not the instant. */
async function settle() {
  await sleep(1_500);
}

/** POST a JSON body, returning the parsed response body on BOTH success and an HTTP error status — a `500
 *  input too large` is a plausible, informative answer that every caller here reads via optional chaining,
 *  not an exception. A genuine network failure (connection refused, timeout) is NOT swallowed: it throws,
 *  because that means the server was never reachable at all, which nothing here is written to read as
 *  merely "answered no". The default timeout is generous because the FIRST long input on a fresh server
 *  pays a one-time cost — a cold Vulkan shader compile measured at 24 s on this machine. */
async function postJson(url, body, { timeoutMs = 120_000 } = {}) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(timeoutMs),
  });
  return res.json().catch(() => null);
}

/** GET and parse JSON, same non-throwing-on-HTTP-error posture as `postJson`. */
async function getJson(url, { timeoutMs = 15_000 } = {}) {
  const res = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
  return res.json().catch(() => null);
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
  const ports = arm.kind === 'router' ? [PORTS.router] : [PORTS.chat, PORTS.embed, PORTS.rerank];

  // The SPECIFIC ports, immediately before binding — never a range checked earlier. Binding a busy port
  // fails UPWARD: no error, and the incumbent answers every request.
  const listeners = parseListeners(await netstat());
  const taken = ports.filter((p) => !isFree(listeners, p));
  if (taken.length) throw new Error(`ports already LISTENING: ${taken.join(', ')} — refusing to bind`);

  const pids = [];
  try {
    if (arm.kind === 'router') {
      const ini = writePreset(`${scratchDir}/presets-${arm.name}.ini`, modelDir, ['chat', 'embed', 'rerank']);
      pids.push(spawnServer(serverExe, ['--models-preset', ini, '--port', String(PORTS.router),
        '--host', '127.0.0.1', '--models-max', String(arm.modelsMax)], scratchDir, arm.name));
    } else {
      for (const role of ['chat', 'embed', 'rerank']) {
        const flags = ROLES[role].flags.flatMap((f) => [`--${f}`]);
        pids.push(spawnServer(serverExe, ['--model', `${modelDir}/${ROLES[role].file}`,
          '--alias', role, '--port', String(PORTS[role]), '--host', '127.0.0.1',
          '--n-gpu-layers', '99', ...flags, ...settingsArgs(role)], scratchDir, `${arm.name}-${role}`));
      }
    }
    await waitUntilListening(ports);
  } catch (err) {
    // A partial start is still a start: a spawn that failed on the SECOND of three roles must not leak the
    // first. Kill whatever already exists here, immediately, before this throws past the caller that would
    // otherwise be the only thing that could ever have torn it down.
    for (const pid of pids) { try { await treeKill(pid); } catch { /* best effort during failure cleanup */ } }
    throw err;
  }
  return { arm, pids, env: envFor(arm), endpoints: envFor(arm) };
}

export async function stopArm(handle) {
  for (const pid of handle.pids) {
    try { await treeKill(pid); } catch (err) { console.error(`stopArm: PID ${pid} — ${err.message}`); }
  }               // /T: the tree is THREE levels (router→child→grandchild)
  await settle();
  // The exit code is not evidence — taskkill has reported SUCCESS for a process still listening. Re-read.
  const listeners = parseListeners(await netstat());
  const survivors = [...listeners.entries()].filter(([p]) => Object.values(PORTS).includes(p));
  return { killed: handle.pids, survivors };
}

/** The busy-device LOAD (Task 8): a second, small llama-server generating tokens continuously so the shared
 *  GPU is occupied for the whole cell — the closest same-process analog to the game-rendering neighbour
 *  `pitfalls.md` measured the 12x-faster-to-26x-slower swing against.
 *
 *  **This is a COMPUTE load, not the GRAPHICS workload that measurement used.** The DIRECTION transfers —
 *  contention should still hit generation far harder than encoding — the MAGNITUDE may not. Read every
 *  figure taken under `--device busy` with that attached, the same posture this repository takes for every
 *  borrowed number.
 *
 *  Checks and records its port exactly like every other server here, and reaches STEADY STATE (every
 *  worker has completed one full generation round) before returning, rather than a fixed sleep — so the
 *  cell's timed region never starts against a cold server. */
export async function startBusyLoad({ modelDir, scratchDir, serverExe, concurrency = 2 }) {
  const listeners = parseListeners(await netstat());
  if (!isFree(listeners, PORTS.load)) {
    throw new Error(`load port ${PORTS.load} already LISTENING — refusing to bind`);
  }

  const pid = spawnServer(serverExe, ['--model', `${modelDir}/${LOAD_MODEL_FILE}`, '--alias', 'load',
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
  const readySignals = [];
  const loops = [];
  for (let i = 0; i < concurrency; i++) {
    let markReady;
    readySignals.push(new Promise((resolve) => { markReady = resolve; }));
    loops.push((async () => {
      let firstRoundDone = false;
      while (!stopped) {
        try {
          await postJson(url, { model: 'load', temperature: 0.7, max_tokens: 256,
            messages: [{ role: 'user', content: prompt }] }, { timeoutMs: 30_000 });
        } catch { /* best effort: one dropped generation round does not end the load */ }
        if (!firstRoundDone) { firstRoundDone = true; markReady(); }
      }
    })());
  }
  // STEADY STATE, not a fixed sleep: every worker has completed one full generation round before this
  // returns, so the cell's timed region never starts against a cold server.
  await Promise.all(readySignals);

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

/** Called BEFORE an arm (to record the roster) and AFTER teardown (to prove it survived). "I only killed my
 *  own PIDs" is an argument, not evidence — a cleanup killed five servers strictly by PID and the sibling
 *  was down at the end of it anyway. */
export async function neighbourReport(ownPids) {
  const rows = await llamaServerProcesses();
  const pids = neighbourPids(rows, ownPids);
  const health = [];
  for (const pid of pids) {
    const row = rows.find((r) => r.pid === pid);
    health.push({ pid, port: row?.port ?? null, alive: await answers(row?.port) });
  }
  return health;
}

/** Compare two `neighbourReport` rosters BY PID, never by aggregate count or "is anything still alive" — an
 *  aggregate check reports clean if a DIFFERENT llama-server happens to still be up while the one that
 *  mattered is gone, which is the exact failure this file exists to catch ("I only killed my own PIDs" is
 *  an argument, not evidence). Every pid present `before` must still be present in `after` AND still
 *  answering; anything else is named, by pid, in the result. */
export function vanishedNeighbours(before, after) {
  const afterByPid = new Map(after.map((r) => [r.pid, r]));
  return before.filter((b) => !(afterByPid.get(b.pid)?.alive)).map((b) => b.pid);
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

// ---------------------------------------------------------------------------------------------------------
// CLI entry point — a thin wrapper, so importing this module for a test spawns nothing AND attaches no
// process-wide listener. `activePids`/`spawnedChildren` (declared beside `spawnServer`) are plain, empty
// collections at import time; only the `process.on('SIGINT', …)` registration below is gated behind
// `import.meta.main`, since a listener attached on every import would accumulate across repeated imports
// (a test file among them) and could fire `process.exit(130)` during an unrelated Ctrl+C.
// ---------------------------------------------------------------------------------------------------------

async function killTracked(pids) {
  for (const pid of pids) { try { await treeKill(pid); } catch { /* best effort */ } }
}

/** A synthetic worst-case single memory entry: real sentences repeated to ~6,000 characters, the figure
 *  this repository's other benches already truncate LongMemEval texts to (`pitfalls.md`). Not a corpus
 *  value — Task 3 owns lifecycle plumbing only, and a later task supplies the harness's real longest text. */
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
  const rows = await neighbourReport([...activePids]);
  console.log(`Neighbour roster ${label} (${rows.length}):`);
  for (const r of rows) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);
  return rows;
}

/** Spawns the C# `--contention` sweep against a running arm's servers. `stdio: 'inherit'` — this IS the
 *  measurement, so its table streams live rather than being captured and re-printed. Registered in the same
 *  PID bookkeeping `spawnServer` uses (`spawnedChildren`/`activePids`), so a SIGINT mid-cell still tears the
 *  bench process down alongside the servers it is measuring. */
function spawnBenchProcess(repoRoot, benchArgs, envOverlay) {
  const benchProject = path.join(repoRoot, 'bench', 'Lyntai.Benchmarks');
  const child = spawn('dotnet',
    ['run', '-c', 'Release', '--project', benchProject, '--', '--contention', ...benchArgs],
    { stdio: 'inherit', env: { ...process.env, ...envOverlay } });
  const forget = () => {
    if (child.pid != null) { spawnedChildren.delete(child.pid); activePids.delete(child.pid); }
  };
  child.once('exit', forget);
  child.once('error', (err) => { console.error(`spawnBenchProcess: ${err.message}`); forget(); });
  if (child.pid != null) { spawnedChildren.set(child.pid, child); activePids.add(child.pid); }
  return child;
}

function runBench(repoRoot, benchArgs, envOverlay) {
  return new Promise((resolve, reject) => {
    const child = spawnBenchProcess(repoRoot, benchArgs, envOverlay);
    child.once('exit', (code) => resolve(code ?? 1));
    child.once('error', reject);
  });
}

/** One device state's worth of measurement against an already-started arm: optionally hold a busy-device
 *  load for the WHOLE cell (steady state before, torn down and verified gone after), sample the GPU for the
 *  WHOLE cell (not bookend before/after — see the Task 8 header comment above `startGpuSampler`), and run
 *  the C# `--contention` sweep inside that window. */
async function runMeasuredCell(arm, device, handle, { modelDir, scratchDir, serverExe, benchArgs, repoRoot }) {
  console.log(`\n--- device: ${device} (arm ${arm.name}) ---`);
  let busy = null;
  if (device === 'busy') {
    busy = await startBusyLoad({ modelDir, scratchDir, serverExe });
    console.log(`busy load steady: pid ${busy.pid} on port ${busy.port}`);
  }
  try {
    const sampler = startGpuSampler();
    const exitCode = await runBench(repoRoot, benchArgs, handle.env);
    const gpu = await sampler.stop();
    printGpuLine(device, gpu);
    if (exitCode !== 0) process.exitCode = 1;
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

async function runArm(arm, { modelDir, scratchDir, serverExe, smoke, devices, benchArgs, repoRoot }) {
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
      await runMeasuredCell(arm, device, handle, { modelDir, scratchDir, serverExe, benchArgs, repoRoot });
    }
  } finally {
    // treeKill drops each pid from activePids/spawnedChildren itself as it confirms each one gone.
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

  const serverExe = await resolveServerExe();
  console.log(`llama-server resolved from PATH: ${serverExe}`);

  // Default to `dedicated` ONLY — the RE-SCOPE ruling (plan progress ledger, 2026-09-11) demoted topology to
  // a footnote once the router-is-a-supervisor finding showed `dedicated` and `router-resident` differ only
  // by a reverse-proxy hop. The measured grid is now {verifier} x {solo, mixed} x {device}, on `dedicated`;
  // `--arms` still exists for anyone who explicitly wants the topology ladder's own identity/extreme check.
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

  for (const arm of targets) {
    await runArm(arm, { modelDir, scratchDir, serverExe, smoke, devices, benchArgs, repoRoot });
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
    console.error(`\nSIGINT — killing ${activePids.size} tracked PID(s) before exit: ${[...activePids].join(', ')}`);
    await killTracked(activePids);
    process.exit(130);
  });
  main().catch((err) => {
    console.error(err);
    process.exitCode = 1;
  });
}
