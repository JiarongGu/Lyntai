// memory-contention — what it costs to serve MANY memory seams from ONE model server.
//
// Read the spec's §1 before changing an arm here. A probe on 2026-09-11 found that llama-server's ROUTER
// mode is a process SUPERVISOR — one child server per model — so `dedicated` and `router-resident` have
// the SAME process and memory topology and differ only by a reverse-proxy hop. What a router buys is one
// endpoint, on-demand loading, and eviction; residency and routing are INDEPENDENT axes, which is why
// `--models-max` is the only thing separating the two router arms.
import fs from 'node:fs';
import path from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';

const execFileAsync = promisify(execFile);
const here = fileURLToPath(import.meta.url);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Ports this harness owns. 8090 is DELIBERATELY absent: a sibling tool's embedding server holds it, and
 *  killing it by image name has already happened twice (`pitfalls.md`). */
export const PORTS = { router: 8140, chat: 8141, embed: 8142, rerank: 8143 };

/** Each role's weights and the flags that ENABLE it. `--embedding` and `--reranking` are process-wide
 *  restrictions rather than per-request modes, so a role without its flag answers 501 while still being
 *  listed by the router — which is why they live here and not at a call site. */
export const ROLES = {
  chat:   { file: 'gemma-3-4b-it-Q4_K_M.gguf',      flags: [] },
  embed:  { file: 'embeddinggemma-300M-Q8_0.gguf',  flags: ['embeddings'] },
  rerank: { file: 'bge-reranker-v2-m3-Q8_0.gguf',   flags: ['reranking'] },
};

export const ARMS = [
  { name: 'dedicated',       kind: 'dedicated', modelsMax: null },
  { name: 'router-resident', kind: 'router',    modelsMax: 4 },
  { name: 'router-swapping', kind: 'router',    modelsMax: 1 },
];

/** The INI `--models-preset` takes: section = served id, keys = long-form flags without the `--`.
 *  Format recovered from the router's own `status.preset` field rather than guessed. */
export function renderPreset(modelDir, roles) {
  return roles.map((role) => {
    const { file, flags } = ROLES[role];
    const lines = [`[${role}]`, `model = ${modelDir}/${file}`, 'n-gpu-layers = 99'];
    for (const flag of flags) lines.push(`${flag} = true`);
    return lines.join('\n');
  }).join('\n\n') + '\n';
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
// Task 3: server lifecycle — spawn, readiness, identity, teardown. Every PID this module kills was recorded
// by THIS module when it spawned the process; nothing here ever kills by image name (windows-machine.md).
// ---------------------------------------------------------------------------------------------------------

/** Raw `netstat -ano` text, fetched fresh on every call — the caller decides whether a moment already
 *  superseded is good enough, this function never decides that for them. */
async function netstat() {
  const { stdout } = await execFileAsync('netstat', ['-ano']);
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

/** Spawn one server with its stdout/stderr captured to its OWN log file rather than inherited — three
 *  servers sharing this process's console would interleave unreadably. Deliberately NOT `detached`: this
 *  process stays alive for the whole `startArm`..`stopArm` span and tears children down explicitly by PID,
 *  and `detached` solves a problem this call does not have (a detached child survives only a parent that
 *  outlives it — the wrong property here, and the exact trap that once reported an embedder "started"
 *  while it never came up). */
function spawnServer(serverExe, args, scratchDir, label) {
  const outFd = fs.openSync(path.join(scratchDir, `${label}.out.log`), 'a');
  const errFd = fs.openSync(path.join(scratchDir, `${label}.err.log`), 'a');
  const child = spawn(serverExe, args, { stdio: ['ignore', outFd, errFd] });
  child.on('exit', () => { try { fs.closeSync(outFd); } catch { /* already closed */ } try { fs.closeSync(errFd); } catch { /* already closed */ } });
  if (!child.pid) throw new Error(`spawn did not return a pid for ${label}`);
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
 *  trusted for anything beyond that: `stopArm` re-reads `netstat` rather than believing this succeeded. */
async function treeKill(pid) {
  try {
    await execFileAsync('taskkill', ['/F', '/PID', String(pid), '/T']);
  } catch (err) {
    const msg = String(err?.stderr ?? err?.message ?? '');
    if (!/not found/i.test(msg)) throw err;
  }
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
          '--n-gpu-layers', '99', ...flags], scratchDir, `${arm.name}-${role}`));
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
// CLI entry point — a thin wrapper, so importing this module for a test spawns nothing. Registered as a
// SIGINT handler too: a Ctrl+C during a multi-gigabyte model load must still reach every PID already
// recorded, or an interrupted smoke run leaks a server nothing will ever tear down.
// ---------------------------------------------------------------------------------------------------------

const activePids = new Set();

async function killTracked(pids) {
  for (const pid of pids) { try { await treeKill(pid); } catch { /* best effort */ } }
}

process.on('SIGINT', async () => {
  console.error(`\nSIGINT — killing ${activePids.size} tracked PID(s) before exit: ${[...activePids].join(', ')}`);
  await killTracked(activePids);
  process.exit(130);
});

/** A synthetic worst-case single memory entry: real sentences repeated to ~6,000 characters, the figure
 *  this repository's other benches already truncate LongMemEval texts to (`pitfalls.md`). Not a corpus
 *  value — Task 3 owns lifecycle plumbing only, and a later task supplies the harness's real longest text. */
function syntheticLongestInput(chars = 6_000) {
  const sentence = 'The reference model answered correctly on the first attempt near the riverbank at dusk. ';
  let s = '';
  while (s.length < chars) s += sentence;
  return s.slice(0, chars);
}

function parseArgs(argv) {
  const armsFlag = argv.indexOf('--arms');
  return {
    armNames: armsFlag >= 0 ? argv[armsFlag + 1].split(',').filter(Boolean) : null,
    smoke: argv.includes('--smoke'),
  };
}

async function reportNeighbours(label) {
  const rows = await neighbourReport([...activePids]);
  console.log(`Neighbour roster ${label} (${rows.length}):`);
  for (const r of rows) console.log(`  pid ${r.pid} port ${r.port ?? '?'} alive=${r.alive}`);
  return rows;
}

async function runArm(arm, { modelDir, scratchDir, serverExe, smoke }) {
  console.log(`\n=== arm: ${arm.name} (${arm.kind}) ===`);
  const handle = await startArm(arm, { modelDir, scratchDir, serverExe });
  for (const pid of handle.pids) activePids.add(pid);
  console.log(`started, pids: ${handle.pids.join(', ')}`);
  try {
    const identity = await verifyIdentity(handle.endpoints);
    console.log(`identity: ${identity.ok ? 'OK' : 'FAIL'} — ${identity.detail}`);
    if (!identity.ok) process.exitCode = 1;

    const extreme = await probeExtreme(handle.endpoints, syntheticLongestInput());
    console.log(`extreme:  ${extreme.ok ? 'OK' : 'FAIL'} — ${extreme.detail}`);

    if (smoke) {
      const loadState = await modelLoadState(handle.endpoints);
      console.log(`model-load-state: loaded=[${loadState.loaded.join(',')}] unloaded=[${loadState.unloaded.join(',')}]`);
    }
  } finally {
    const stopped = await stopArm(handle);
    for (const pid of handle.pids) activePids.delete(pid);
    console.log(`stopped. survivors on ${Object.values(PORTS).join('/')}: ${stopped.survivors.length}`);
    if (stopped.survivors.length) {
      console.error(`SURVIVORS: ${JSON.stringify(stopped.survivors)}`);
      process.exitCode = 1;
    }
  }
}

async function main() {
  const { armNames, smoke } = parseArgs(process.argv.slice(2));
  const modelDir = process.env.LYNTAI_CONTENTION_MODEL_DIR;
  if (!modelDir) throw new Error('LYNTAI_CONTENTION_MODEL_DIR is not set — no default, by design');

  const scratchDir = path.resolve(path.dirname(here), '..', '_contention');
  fs.mkdirSync(scratchDir, { recursive: true });

  const serverExe = await resolveServerExe();
  console.log(`llama-server resolved from PATH: ${serverExe}`);

  const targets = armNames ? ARMS.filter((a) => armNames.includes(a.name)) : ARMS;
  if (targets.length === 0) throw new Error(`no matching arm(s) in --arms ${armNames?.join(',')}`);

  const before = await reportNeighbours('BEFORE');

  for (const arm of targets) {
    await runArm(arm, { modelDir, scratchDir, serverExe, smoke });
  }

  const after = await reportNeighbours('AFTER');
  if (before.length > 0 && after.length === 0) {
    console.error('NEIGHBOUR IS DOWN — restart it before doing anything else.');
    process.exitCode = 1;
  } else if (before.some((b) => b.alive) && !after.some((a) => a.alive)) {
    console.error('NEIGHBOUR STOPPED ANSWERING — restart it before doing anything else.');
    process.exitCode = 1;
  }
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  main().catch((err) => {
    console.error(err);
    process.exitCode = 1;
  });
}
