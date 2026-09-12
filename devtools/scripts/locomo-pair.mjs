// locomo-pair — run `memory-locomo` against a CHOSEN embedder and a CHOSEN reader, owning both servers.
//
// `memory-locomo` is the one study here with no orchestrator of its own, so a run takes whatever happens
// to be LISTENING — the port-fails-upward hazard `.claude/knowledge/pitfalls.md` records, where the
// incumbent answers and every figure is silently taken on the wrong model. This closes that, and it is
// what makes "which embedder / which reader" an ARGUMENT rather than an ambient property of the machine.
//
// It reuses `memory-contention`'s hygiene rather than restating it: check the SPECIFIC port immediately
// before binding, wait for the port, kill by PID, RE-READ (a kill's exit code is not evidence), and
// re-check the neighbour afterwards. It also reads each port's identity back from `/props`, because a
// `--model`-started `llama-server` answers to whatever name it is asked.
//
// Usage:
//   node dev.mjs locomo-pair --embed <file.gguf> --chat <file.gguf> [-- <memory-locomo args…>]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  neighbourReport, ownedPids, resolveServerExe, runTracked, startServers, stopServers,
  vanishedNeighbours,
} from './memory-contention.mjs';

const here = fileURLToPath(import.meta.url);

/** Ports this harness owns. Above every other block here — `memory-contention`'s 8140-8144,
 *  `rerank-screen`'s 8147, `memory-decision`'s 8150-8153, `tool-affordance`'s 8160-8166 and 8169,
 *  `embed-screen`'s 8167 — and nowhere near 8090, which a sibling tool's embedder has held for sessions. */
export const PORTS = { embed: 8170, chat: 8171 };

/** `{label, port, argv}` per role — the shape `startServers` takes, exported so a test can assert the
 *  argv without spawning anything.
 *
 *  <b>The embedder's batch is sized to its context on purpose.</b> A LoCoMo turn reaches ~6,000 characters
 *  and the physical batch defaults BELOW that, which surfaces as an HTTP 500 on a real turn long after a
 *  short probe passed — the "smoke test on a TYPICAL input" trap, which cost an hour of ingestion once. */
export function serverSpecs(embedFile, chatFile, modelDir) {
  return [
    {
      label: 'embed',
      port: PORTS.embed,
      argv: ['--model', path.join(modelDir, embedFile), '--alias', 'embed',
        '--port', String(PORTS.embed), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
        '--embeddings', '--ctx-size', '2048', '--batch-size', '2048', '--ubatch-size', '2048'],
    },
    {
      label: 'chat',
      port: PORTS.chat,
      argv: ['--model', path.join(modelDir, chatFile), '--alias', 'chat',
        '--port', String(PORTS.chat), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
        '--ctx-size', '8192', '--parallel', '2'],
    },
  ];
}

/** Env the bench reads. Every URL spells 127.0.0.1, never `localhost`: on Windows that resolves to ::1
 *  first and costs ~1.8 s per call against an IPv4-only listener, which at a few thousand calls is the
 *  whole run. The bench's own defaults spell `localhost`, so inheriting them is the bug. */
export const envFor = () => ({
  LYNTAI_LIVE_MODEL_URL: `http://127.0.0.1:${PORTS.embed}`, LYNTAI_LIVE_EMBED_MODEL: 'embed',
  LYNTAI_LIVE_CHAT_URL: `http://127.0.0.1:${PORTS.chat}`, LYNTAI_LIVE_CHAT_MODEL: 'chat',
});

/** `--embed` and `--chat` are this module's; everything after a bare `--` is FORWARDED verbatim to
 *  `memory-locomo`, so a flag the bench grows needs no edit here. */
export function parseArgs(argv) {
  const at = (n) => { const i = argv.indexOf(`--${n}`); return i >= 0 ? argv[i + 1] ?? null : null; };
  const dashdash = argv.indexOf('--');
  return {
    embed: at('embed'),
    chat: at('chat'),
    benchArgs: dashdash >= 0 ? argv.slice(dashdash + 1) : [],
  };
}

/** What a port actually loaded, read back from the process rather than assumed from the argv. */
async function servedFile(port) {
  try {
    const res = await fetch(`http://127.0.0.1:${port}/props`, { signal: AbortSignal.timeout(10_000) });
    const props = await res.json().catch(() => null);
    const raw = props?.model_path ?? props?.default_generation_settings?.model ?? null;
    return typeof raw === 'string' && raw.length > 0 ? path.basename(raw.replace(/\\/g, '/')) : null;
  } catch { return null; }
}

async function main() {
  const { embed, chat, benchArgs } = parseArgs(process.argv.slice(2));
  const modelDir = process.env.LYNTAI_MODEL_DIR ?? process.env.LYNTAI_CONTENTION_MODEL_DIR;
  if (!modelDir || !embed || !chat) {
    console.error('locomo-pair: set LYNTAI_MODEL_DIR and pass --embed <file.gguf> --chat <file.gguf>.');
    console.error('  No default model directory, by design — a developer-machine path must never reach');
    console.error('  a tracked file. Everything after a bare `--` goes to memory-locomo unchanged.');
    process.exitCode = 2;
    return;
  }
  const missing = [embed, chat].filter((f) => !fs.existsSync(path.join(modelDir, f)));
  if (missing.length) {
    console.error(`locomo-pair: missing model file(s) in ${modelDir}: ${missing.join(', ')}`);
    process.exitCode = 2;
    return;
  }

  const repoRoot = path.resolve(path.dirname(here), '..', '..');
  const scratchDir = path.resolve(path.dirname(here), '..', '_locomo-pair');
  fs.mkdirSync(scratchDir, { recursive: true });

  const serverExe = await resolveServerExe();
  const before = await neighbourReport(ownedPids());
  console.log(`Neighbours BEFORE (${before.length}): ${JSON.stringify(before.map((r) => r.pid))}`);
  console.log(`embedder: ${embed} (${fs.statSync(path.join(modelDir, embed)).size} B)`);
  console.log(`reader  : ${chat} (${fs.statSync(path.join(modelDir, chat)).size} B)`);

  let pids = [];
  const started = Date.now();
  try {
    pids = await startServers(serverSpecs(embed, chat, modelDir), { scratchDir, serverExe });
    console.log(`servers up on ${Object.values(PORTS).join(', ')} (pids ${pids.join(', ')})`);
    for (const [role, port] of Object.entries(PORTS))
      console.log(`  ${role}: serving ${await servedFile(port) ?? 'unknown (this build exposes no /props)'}`);

    const code = await runTracked('dotnet', ['run', '-c', 'Release',
      '--project', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '--', '--locomo', ...benchArgs],
      envFor());
    if (code !== 0) process.exitCode = code;
  } finally {
    // Unconditional: a start that THREW is the likeliest moment to have leaked one, so the survivor
    // re-read must run on that path above all.
    const { survivors } = await stopServers(pids, Object.values(PORTS));
    if (survivors.length) {
      console.error(`*** PORTS STILL LISTENING: ${JSON.stringify(survivors)} — kill these by PID ***`);
      process.exitCode = 1;
    } else console.log('teardown: both owned ports free');

    const after = await neighbourReport(ownedPids());
    const lost = vanishedNeighbours(before, after);
    if (lost.length) {
      console.error(`*** NEIGHBOUR LOST: ${JSON.stringify(lost)} — restart it ***`);
      process.exitCode = 1;
    } else console.log(`neighbours after: all ${before.length} still alive`);
    console.log(`wall clock: ${((Date.now() - started) / 1000).toFixed(0)}s`);
  }
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  process.on('SIGINT', async () => {
    console.error('\nSIGINT — tearing down owned servers before exiting.');
    try { await stopServers(ownedPids(), Object.values(PORTS)); } catch { /* best effort on the way out */ }
    process.exit(130);
  });
  await main().catch((err) => { console.error(err); process.exitCode = 1; });
}
