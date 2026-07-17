#!/usr/bin/env node
// Deterministic LLM provider stub for tests + e2e — speaks just enough `claude` stream-json for the
// ClaudeCli provider (Lyntai.Providers.ClaudeCli) to parse. Spawned via LYNTAI_PROVIDER_CMD (or CLAUDE_CMD),
// so no real tokens are spent and outputs are reproducible. The prompt arrives on stdin (like the real CLI).
//
// Behavior by prompt markers (extend as new tests need deterministic outputs):
//   "SLOW"         -> sleep 8s before responding (cancel / timeout testing)
//   "FORCE_ERROR"  -> emit an empty result (server maps to a Failed verdict — no content produced)
//   "SCORING TASK" -> return a canned {"score":0..1,"reason":"..."} JSON (LlmScorerBase judge path)
//   "JSON_SCHEMA"  -> return a canned JSON object (structured-output path)
//   else           -> echo a short deterministic completion derived from the prompt
import process from 'node:process';

const chunks = [];
for await (const c of process.stdin) chunks.push(c);
const prompt = Buffer.concat(chunks).toString('utf8');

const emit = (obj) => process.stdout.write(JSON.stringify(obj) + '\n');
const sessionId = `stub-${Buffer.from(prompt).length.toString(36)}`; // stable per prompt, no Date.now
const usage = { input_tokens: 1200, output_tokens: 340, cache_read_input_tokens: 800 };
const finish = (text) => emit({ type: 'result', result: text, usage, total_cost_usd: 0.012 });

emit({ type: 'system', subtype: 'init', session_id: sessionId });

if (prompt.includes('SLOW')) {
  await new Promise((r) => setTimeout(r, 8000));
}

if (prompt.includes('FORCE_ERROR')) {
  finish(''); // empty → the provider reports Failed (no output)
  process.exit(0);
}

if (prompt.includes('SCORING TASK')) {
  const verdict = JSON.stringify({ score: 0.8, reason: 'stub judge verdict' });
  emit({ type: 'assistant', message: { content: [{ type: 'text', text: verdict }] } });
  finish(verdict);
  process.exit(0);
}

if (prompt.includes('JSON_SCHEMA')) {
  const obj = JSON.stringify({ ok: true, note: 'stub structured output' });
  emit({ type: 'assistant', message: { content: [{ type: 'text', text: obj }] } });
  finish(obj);
  process.exit(0);
}

// Default: a deterministic completion. Echo the last non-empty prompt line so tests can assert round-trip.
const lastLine = prompt.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).pop() ?? '';
const text = `stub reply: ${lastLine.slice(0, 200)}`;
emit({ type: 'assistant', message: { content: [{ type: 'text', text }] } });
finish(text);
