// _llama-harness — the llama-server lifecycle every orchestrated sweep and screen shares: the port
// registry, spawn and readiness, teardown by PID with a re-read, the neighbour census, the GPU sampler, and
// `withOwnedServers`, which owns the finally/SIGINT/exit-code rules one run needs. The leading underscore
// keeps it out of the command roster. Every trap it encodes — a busy port fails UPWARD, a PID kill that
// "succeeded" can leave a listener, killing by image name takes a neighbour down — is in
// `.claude/knowledge/pitfalls.md` §Environment / tooling.
import fs from 'node:fs';
import path from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Every port a harness binds, keyed by harness, so two can be up at once and disjointness is ONE test rather
 * than a comment in every file. A port nobody here owns but something else does goes in `RESERVED_PORTS`.
 */
export const HARNESS_PORTS = {
  'memory-contention': { router: 8140, chat: 8141, embed: 8142, rerank: 8143, load: 8144 },
  'rerank-screen': { screen: 8147 },
  'memory-decision': { chat: 8150, small: 8151, embed: 8152, rerank: 8153 },
  'tool-affordance': {
    chat: 8160, small: 8161, embed: 8162, rerank: 8163, extra0: 8164, extra1: 8165, extra2: 8166, native: 8169,
  },
  'embed-screen': { screen: 8167 },
  'locomo-pair': { embed: 8170, chat: 8171 },
};

/** Held by a neighbouring tool's embedding server across many sessions — never bind it, never kill it. */
export const RESERVED_PORTS = [8090];

/** The directory holding the GGUFs: `LYNTAI_MODEL_DIR`, or its legacy name. No default, by design — a
 *  developer-machine path must never reach a tracked file. */
export const modelDirFromEnv = (env = process.env) => env.LYNTAI_MODEL_DIR ?? env.LYNTAI_CONTENTION_MODEL_DIR ?? null;

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

/** The transitive closure of `ownPids` over `rows` (`{pid, ppid}`) — walks children, then grandchildren,
 *  until nothing new joins. Shared by `neighbourPids` (everyone OUTSIDE the closure) and the GPU census's
 *  `ownPids` (everyone INSIDE it, so a router's spawned model children count as ours) — one traversal, not
 *  two that could drift apart. */
export function ownedPidClosure(rows, ownPids) {
  const mine = new Set(ownPids);
  let grew = true;
  while (grew) {               // transitive: the router's children spawn grandchildren
    grew = false;
    for (const r of rows) if (!mine.has(r.pid) && mine.has(r.ppid)) { mine.add(r.pid); grew = true; }
  }
  return mine;
}

/** Every llama-server PID that is neither ours nor a descendant of ours. Enumerating PROCESSES rather than
 *  a port list is the point: the sibling's server was invisible to a sweep of the ports we planned to use. */
export function neighbourPids(rows, ownPids) {
  const mine = ownedPidClosure(rows, ownPids);
  return rows.filter((r) => !mine.has(r.pid)).map((r) => r.pid);
}

// ---------------------------------------------------------------------------------------------------------
// The GPU-quiet CONTROL. Every other control on this bench makes a bad cell VISIBLE (hit-rate for
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
 *  successful samples rather than reading an exception as "the device is quiet". `timeout` is load-bearing:
 *  without it a hung `nvidia-smi` hangs `stop()` (and therefore the whole cell) forever. */
async function sampleGpuOnce() {
  try {
    const { stdout } = await execFileAsync('nvidia-smi',
      ['--query-gpu=utilization.gpu,memory.used', '--format=csv,noheader'], { timeout: 5_000 });
    return parseGpuSample(stdout);
  } catch {
    return null;
  }
}

/** The device is near its documented idle baseline (3-6% util, 2,618 MiB) — checked ONCE, before the first
 *  cell, to catch a game or another load that is ALREADY running before this module starts anything of its
 *  own. Deliberately loose (10% / 2.7 GiB): the point is to catch a busy neighbour, not to fail on ordinary
 *  driver jitter. Skips (rather than refuses) when `nvidia-smi` is unavailable — the same "cannot verify" is
 *  not "verified clean" posture `aggregateGpuSamples` takes for a mid-cell sample. */
const GPU_IDLE_MAX_UTIL = 10;
const GPU_IDLE_MAX_MEM_MIB = 2.7 * 1024;

export async function assertGpuIdle() {
  const sample = await sampleGpuOnce();
  if (sample === null) {
    console.log('GPU idle check: nvidia-smi unavailable — skipping (cannot verify the device is quiet).');
    return;
  }
  if (sample.util > GPU_IDLE_MAX_UTIL || sample.memMiB > GPU_IDLE_MAX_MEM_MIB) {
    throw new Error(`GPU is not idle before starting: ${sample.util}% util, ${sample.memMiB} MiB used `
      + `(expected <=${GPU_IDLE_MAX_UTIL}% util and <=${GPU_IDLE_MAX_MEM_MIB.toFixed(0)} MiB) — a game or `
      + 'another load may already be running. Refusing to start the grid.');
  }
  console.log(`GPU idle check: OK — ${sample.util}% util, ${sample.memMiB} MiB used`);
}

/** One `nvidia-smi --query-compute-apps=pid,used_gpu_memory --format=csv,noheader` line per GPU-using
 *  PROCESS, e.g. `"12345, 806 MiB"`. Unlike `parseGpuSample`'s query (always one line, the device), this
 *  query emits ZERO lines on an idle device — a genuinely quiet reading — so an empty array is not an error
 *  here the way it would be for utilization. */
export function parseGpuComputeApps(csvText) {
  const rows = [];
  for (const line of String(csvText).split(/\r?\n/).map((l) => l.trim()).filter(Boolean)) {
    const [pidRaw, memRaw] = line.split(',');
    const pid = Number.parseInt(pidRaw, 10);
    const memMiB = Number.parseFloat(memRaw);
    if (Number.isFinite(pid) && Number.isFinite(memMiB)) rows.push({ pid, memMiB });
  }
  return rows;
}

/** `null` on a failed/absent `nvidia-smi` (the caller must tell "confirmed nobody's using the device" apart
 *  from "could not ask" — the same distinction the utilization side protects). */
async function sampleGpuComputeAppsOnce() {
  try {
    const { stdout } = await execFileAsync('nvidia-smi',
      ['--query-compute-apps=pid,used_gpu_memory', '--format=csv,noheader'], { timeout: 5_000 });
    return parseGpuComputeApps(stdout);
  } catch {
    return null;
  }
}

/** Which PIDs in `censusPids` are neither OURS (`ownPids` — this cell's servers, and the busy load if one
 *  is running) nor the ALREADY-KNOWN neighbour (`knownNeighbourPids`, captured once before this run's first
 *  cell — never hardcoded, since a neighbour's PID is only ever true for one session). Reported by PID, the
 *  same shape `vanishedNeighbours` already uses, because an aggregate ("was anything extra running?") hides
 *  WHICH process it was. */
export function censusContamination(censusPids, ownPids, knownNeighbourPids) {
  const expected = new Set([...ownPids, ...knownNeighbourPids]);
  return censusPids.filter((pid) => !expected.has(pid));
}

/** Polls the compute-apps census on `intervalMs` for as long as the caller holds the handle — for the WHOLE
 *  cell, not bookend before/after, for the identical reason `startGpuSampler` does: a contaminating process
 *  that starts and stops INSIDE the cell must still be caught. `stop()` returns the UNION of every PID seen
 *  across every sample (a transient contaminant that came and went is still a contaminant) and `sampledOk`
 *  — false only when `nvidia-smi` never once answered, so the caller can tell "confirmed clean" from
 *  "never checked" apart, exactly as `aggregateGpuSamples`' `samples: 0` does for utilization. */
export function startGpuCensus({ intervalMs = 1_000 } = {}) {
  const seen = new Set();
  let stopped = false;
  let sampledOk = false;
  const loop = (async () => {
    while (!stopped) {
      const rows = await sampleGpuComputeAppsOnce();
      if (rows !== null) {
        sampledOk = true;
        for (const { pid } of rows) seen.add(pid);
      }
      if (stopped) break;
      await sleep(intervalMs);
    }
  })();
  return {
    async stop() {
      stopped = true;
      await loop;
      return { pids: [...seen], sampledOk };
    },
  };
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

// ---------------------------------------------------------------------------------------------------------
// Server lifecycle — spawn, readiness, teardown. Every PID this module kills was recorded
// by THIS module when it spawned the process; nothing here ever kills by image name (windows-machine.md).
// ---------------------------------------------------------------------------------------------------------

/** Raw `netstat -ano` text, fetched fresh on every call — the caller decides whether a moment already
 *  superseded is good enough, this function never decides that for them. `maxBuffer` is raised well past
 *  `execFile`'s 1 MB default: a busy machine's connection table can exceed it, and the default failure mode
 *  is a silent truncation or a thrown error, either of which reads as "no listeners" to a caller checking a
 *  specific port. */
export async function netstat() {
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
export async function llamaServerProcesses() {
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
export async function answers(port) {
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
export function spawnServer(serverExe, args, scratchDir, label) {
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
export async function waitUntilListening(ports, { timeoutMs = 180_000, intervalMs = 500 } = {}) {
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
export async function treeKill(pid) {
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
export async function settle() {
  await sleep(1_500);
}

/** POST a JSON body, returning the parsed response body on BOTH success and an HTTP error status — a `500
 *  input too large` is a plausible, informative answer that every caller here reads via optional chaining,
 *  not an exception. A genuine network failure (connection refused, timeout) is NOT swallowed: it throws,
 *  because that means the server was never reachable at all, which nothing here is written to read as
 *  merely "answered no". The default timeout is generous because the FIRST long input on a fresh server
 *  pays a one-time cost — a cold Vulkan shader compile measured at 24 s on this machine. */
export async function postJson(url, body, { timeoutMs = 120_000 } = {}) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(timeoutMs),
  });
  return res.json().catch(() => null);
}

/** GET and parse JSON, same non-throwing-on-HTTP-error posture as `postJson`. */
export async function getJson(url, { timeoutMs = 15_000 } = {}) {
  const res = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
  return res.json().catch(() => null);
}

/** Start a set of servers, or leave NOTHING behind trying. `specs` is `{label, port, argv}[]`.
 *
 *  Check the SPECIFIC ports immediately before binding (a busy port fails UPWARD and the incumbent
 *  answers), wait for `/health` rather than for a bind, and kill a PARTIAL start rather than leaking it
 *  past the throw. */
export async function startServers(specs, { scratchDir, serverExe }) {
  const ports = specs.map((s) => s.port);
  const listeners = parseListeners(await netstat());
  const taken = ports.filter((p) => !isFree(listeners, p));
  if (taken.length) throw new Error(`ports already LISTENING: ${taken.join(', ')} — refusing to bind`);

  const pids = [];
  try {
    for (const spec of specs) pids.push(spawnServer(serverExe, spec.argv, scratchDir, spec.label));
    await waitUntilListening(ports);
  } catch (err) {
    // A partial start is still a start: a spawn that failed on the SECOND of three roles must not leak the
    // first. Kill whatever already exists here, immediately, before this throws past the caller that would
    // otherwise be the only thing that could ever have torn it down.
    for (const pid of pids) { try { await treeKill(pid); } catch { /* best effort during failure cleanup */ } }
    throw err;
  }
  return pids;
}

/** Tear down by PID and then RE-READ, because the exit code is not evidence: `taskkill /F /PID` has reported
 *  SUCCESS for a process still listening seconds later. `ownedPorts` is what to re-check. */
export async function stopServers(pids, ownedPorts) {
  for (const pid of pids) {
    try { await treeKill(pid); } catch (err) { console.error(`stopServers: PID ${pid} — ${err.message}`); }
  }               // /T: the tree is THREE levels (router→child→grandchild)
  await settle();
  const listeners = parseListeners(await netstat());
  return { killed: pids, survivors: [...listeners.entries()].filter(([p]) => ownedPorts.includes(p)) };
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

export async function killTracked(pids) {
  for (const pid of pids) { try { await treeKill(pid); } catch { /* best effort */ } }
}

/** Spawns any process this module considers ITS OWN for the rest of the run's lifetime — `stdio: 'inherit'`
 *  so the child's own output streams live, and registered in the same PID bookkeeping `spawnServer` uses
 *  (`spawnedChildren`/`activePids`), so a SIGINT mid-build or mid-cell still tears it down. Used for both
 *  the one-time `dotnet build` and the per-cell `dotnet run --no-build` invocation below — a plain child
 *  process, not a server, so it is never checked against a port. */
export function spawnTracked(exe, args, envOverlay = {}) {
  const child = spawn(exe, args, { stdio: 'inherit', env: { ...process.env, ...envOverlay } });
  const forget = () => {
    if (child.pid != null) { spawnedChildren.delete(child.pid); activePids.delete(child.pid); }
  };
  child.once('exit', forget);
  child.once('error', (err) => { console.error(`spawnTracked: ${exe} — ${err.message}`); forget(); });
  if (child.pid != null) { spawnedChildren.set(child.pid, child); activePids.add(child.pid); }
  return child;
}

/** Every PID this module currently considers its own — for `neighbourReport`, which must be told what to
 *  EXCLUDE. Exported as a snapshot rather than as the live Set so an importing orchestrator cannot mutate
 *  the bookkeeping that teardown depends on. */
export const ownedPids = () => [...activePids];

export function runTracked(exe, args, envOverlay) {
  return new Promise((resolve, reject) => {
    const child = spawnTracked(exe, args, envOverlay);
    child.once('exit', (code) => resolve(code ?? 1));
    child.once('error', reject);
  });
}

/** Build the C# bench ONCE, outside any measured window — a `dotnet run` that restores and builds on its
 *  own would sit inside whichever cell ran first. The per-cell `dotnet run --no-build` relies on it. */
export async function buildBench(repoRoot) {
  console.log('\nbuilding the C# bench (dotnet build -c Release) once, OUTSIDE any measured window...');
  const code = await runTracked('dotnet',
    ['build', '-c', 'Release', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '-v', 'q', '--nologo']);
  if (code !== 0) throw new Error(`dotnet build -c Release exited ${code} — refusing to start`);
}

/**
 * One owned-server run: take the neighbour roster, start `specs` (`{label, port, argv}[]`), call `run`, and
 * in `finally` tear down by PID, re-read the ports, and re-take the roster. A port still listening or a
 * neighbour gone is a FAILURE (`process.exitCode = 1`), never a line under a green result. `gpu` samples
 * the device for the run and prints `gpuNote` beside the figures.
 */
export async function withOwnedServers({ label, specs, scratchDir, serverExe, run, gpu = false, gpuNote = '' }) {
  const ports = specs.map((s) => s.port);
  const before = await neighbourReport(ownedPids());
  console.log(`Neighbour roster BEFORE (${before.length}):`);
  for (const r of before) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);

  let pids = [];
  const sampler = gpu ? startGpuSampler() : null;
  try {
    pids = await startServers(specs, { scratchDir, serverExe });
    console.log(`\n${label}: servers up on ${ports.join(', ')} (pids ${pids.join(', ')})\n`);
    await run({ pids });
  } finally {
    const device = sampler ? await sampler.stop() : null;
    // Unconditional: a start that THREW leaves `pids` empty while being the likeliest moment to have leaked
    // one, so the survivor re-read must run on that path above all.
    const { survivors } = await stopServers(pids, ports);
    if (survivors.length) {
      console.error(`\n*** PORTS STILL LISTENING after teardown: ${JSON.stringify(survivors)} `
        + '(port, pid) — kill these by PID before the next run ***');
      process.exitCode = 1;
    } else {
      console.log(`\nTeardown: all ${ports.length} owned port(s) free`);
    }
    if (device) {
      console.log(device.samples === 0
        ? 'GPU: UNAVAILABLE — nvidia-smi produced zero samples. Reporting null, not zero.'
        : `GPU during the run: max ${device.maxUtil.toFixed(0)}% util, mean ${device.meanUtil.toFixed(1)}%, `
          + `max ${device.maxMemMiB.toFixed(0)} MiB over ${device.samples} sample(s).${gpuNote ? ` ${gpuNote}` : ''}`);
    }
    const lost = vanishedNeighbours(before, await neighbourReport(ownedPids()));
    if (lost.length) {
      console.error(`*** NEIGHBOUR LOST: ${JSON.stringify(lost)} — restart it ***`);
      process.exitCode = 1;
    } else {
      console.log(`Neighbours after: all ${before.length} still alive`);
    }
  }
}

/** Ctrl+C during a model-load window would end the process without unwinding `finally`, orphaning every
 *  server on a GPU; this tears the owned ones down first. Call it from a CLI entry only — never on import. */
export function tearDownOnSigint(ports) {
  process.on('SIGINT', async () => {
    console.error('\nSIGINT — tearing down owned servers before exiting.');
    try { await stopServers(ownedPids(), ports); } catch { /* best effort on the way out */ }
    process.exit(130);
  });
}
