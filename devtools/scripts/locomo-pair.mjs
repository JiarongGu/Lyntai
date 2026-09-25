// locomo-pair — run `memory-locomo` against a CHOSEN embedder and a CHOSEN reader, owning both servers.
//
// `memory-locomo` is the one study here with no orchestrator of its own, so a run takes whatever happens
// to be LISTENING — the port-fails-upward hazard `.claude/knowledge/pitfalls.md` records, where the
// incumbent answers and every figure is silently taken on the wrong model. This closes that, and it is
// what makes "which embedder / which reader" an ARGUMENT rather than an ambient property of the machine.
//
// The server lifecycle is `_llama-harness.mjs`' `withOwnedServers`. It also reads each port's identity back
// from `/props`, because a `--model`-started `llama-server` answers to whatever name it is asked.
//
// Usage:
//   node dev.mjs locomo-pair --embed <file.gguf> --chat <file.gguf> [-- <memory-locomo args…>]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  HARNESS_PORTS, modelDirFromEnv, resolveServerExe, runTracked, tearDownOnSigint, withOwnedServers,
} from './_llama-harness.mjs';

const here = fileURLToPath(import.meta.url);

/** Ports this harness owns — `_llama-harness.mjs`' registry, which keeps every harness disjoint. */
export const PORTS = HARNESS_PORTS['locomo-pair'];

/** `{label, port, argv}` per role — the shape `startServers` takes, exported so a test can assert the
 *  argv without spawning anything.
 *
 *  <b>Only what is actually CALLED.</b> `--embed-endpoint` names an embedder already running (a static
 *  `model2vec` model has no GGUF, so no `llama-server` can serve one), and `memory-locomo --retrieval`
 *  scores a model-free metric that needs no reader at all — serving 2.5 GB of chat weights for it is the
 *  same waste `tool-affordance --scorers-only` had to shed before it could run on a busy device.
 *
 *  <b>The embedder's batch is sized to its context on purpose.</b> A LoCoMo turn reaches ~6,000 characters
 *  and the physical batch defaults BELOW that, which surfaces as an HTTP 500 on a real turn long after a
 *  short probe passed — the "smoke test on a TYPICAL input" trap, which cost an hour of ingestion once. */
export function serverSpecs(embedFile, chatFile, modelDir) {
  const specs = [];
  if (embedFile) specs.push({
    label: 'embed',
    port: PORTS.embed,
    argv: ['--model', path.join(modelDir, embedFile), '--alias', 'embed',
      '--port', String(PORTS.embed), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
      '--embeddings', '--ctx-size', '2048', '--batch-size', '2048', '--ubatch-size', '2048'],
  });
  if (chatFile) specs.push({
    label: 'chat',
    port: PORTS.chat,
    argv: ['--model', path.join(modelDir, chatFile), '--alias', 'chat',
      '--port', String(PORTS.chat), '--host', '127.0.0.1', '--no-webui', '-ngl', '99',
      '--ctx-size', '8192', '--parallel', '2'],
  });
  return specs;
}

/** Env the bench reads. Every URL spells 127.0.0.1, never `localhost`: on Windows that resolves to ::1
 *  first and costs ~1.8 s per call against an IPv4-only listener, which at a few thousand calls is the
 *  whole run. The bench's own defaults spell `localhost`, so inheriting them is the bug.
 *
 *  A role is ABSENT rather than empty when it was not asked for, so the bench refuses loudly instead of
 *  inheriting a default that points at whatever else is listening. */
export function envFor({ embedUrl = `http://127.0.0.1:${PORTS.embed}`, chat = true } = {}) {
  return {
    LYNTAI_LIVE_MODEL_URL: embedUrl, LYNTAI_LIVE_EMBED_MODEL: 'embed',
    ...(chat
      ? { LYNTAI_LIVE_CHAT_URL: `http://127.0.0.1:${PORTS.chat}`, LYNTAI_LIVE_CHAT_MODEL: 'chat' }
      : {}),
  };
}

/** `--embed`, `--embed-endpoint` and `--chat` are this module's; everything after a bare `--` is
 *  FORWARDED verbatim to `memory-locomo`, so a flag the bench grows needs no edit here. */
export function parseArgs(argv) {
  const at = (n) => { const i = argv.indexOf(`--${n}`); return i >= 0 ? argv[i + 1] ?? null : null; };
  const dashdash = argv.indexOf('--');
  const embedEndpoint = at('embed-endpoint');
  const embed = at('embed');
  if (embed && embedEndpoint)
    throw new Error('locomo-pair: pass --embed OR --embed-endpoint, not both');
  return {
    embed,
    embedEndpoint,
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
  const { embed, embedEndpoint, chat, benchArgs } = parseArgs(process.argv.slice(2));
  const modelDir = modelDirFromEnv();
  if (!modelDir || !(embed || embedEndpoint)) {
    console.error('locomo-pair: set LYNTAI_MODEL_DIR and pass --embed <file.gguf> (or');
    console.error('  --embed-endpoint <url> for an embedder this harness cannot start), plus');
    console.error('  --chat <file.gguf> for any mode that READS. `--retrieval` needs no reader.');
    console.error('  No default model directory, by design — a developer-machine path must never reach');
    console.error('  a tracked file. Everything after a bare `--` goes to memory-locomo unchanged.');
    process.exitCode = 2;
    return;
  }
  const missing = [embed, chat].filter((f) => f && !fs.existsSync(path.join(modelDir, f)));
  if (missing.length) {
    console.error(`locomo-pair: missing model file(s) in ${modelDir}: ${missing.join(', ')}`);
    process.exitCode = 2;
    return;
  }

  const repoRoot = path.resolve(path.dirname(here), '..', '..');
  const scratchDir = path.resolve(path.dirname(here), '..', '_locomo-pair');
  fs.mkdirSync(scratchDir, { recursive: true });

  const serverExe = (embed || chat) ? await resolveServerExe() : null;
  console.log(embed
    ? `embedder: ${embed} (${fs.statSync(path.join(modelDir, embed)).size} B)`
    : `embedder: ${embedEndpoint} (already running; this harness did not start it)`);
  console.log(chat ? `reader  : ${chat} (${fs.statSync(path.join(modelDir, chat)).size} B)`
    : 'reader  : NONE — a mode that does not read needs no chat server');

  const specs = serverSpecs(embed, chat, modelDir);
  const started = Date.now();
  try {
    await withOwnedServers({
      label: 'locomo-pair', specs, scratchDir, serverExe,
      run: async () => {
        for (const spec of specs)
          console.log(`  ${spec.label}: serving ${await servedFile(spec.port) ?? 'unknown'}`);

        const code = await runTracked('dotnet', ['run', '-c', 'Release',
          '--project', path.join(repoRoot, 'bench', 'Lyntai.Benchmarks'), '--', '--locomo', ...benchArgs],
          envFor({ embedUrl: embedEndpoint ?? undefined, chat: !!chat }));
        if (code !== 0) process.exitCode = code;
      },
    });
  } finally {
    console.log(`wall clock: ${((Date.now() - started) / 1000).toFixed(0)}s`);
  }
}

if (import.meta.main ?? (process.argv[1] && path.resolve(process.argv[1]) === here)) {
  tearDownOnSigint(Object.values(PORTS));
  await main().catch((err) => { console.error(err); process.exitCode = 1; });
}
