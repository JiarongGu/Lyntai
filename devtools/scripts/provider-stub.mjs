#!/usr/bin/env node
// Deterministic LLM provider stub for tests + e2e — speaks just enough `claude` stream-json for the
// ClaudeCli provider (Lyntai.Providers.ClaudeCli) to parse. Spawned via LYNTAI_PROVIDER_CMD (or CLAUDE_CMD),
// so no real tokens are spent and outputs are reproducible. The prompt arrives on stdin (like the real CLI).
//
// Behavior by prompt markers (extend as new tests need deterministic outputs):
//   "SLOW"          -> sleep 8s before responding (cancel / timeout testing)
//   "FORCE_ERROR"   -> emit an empty result (server maps to a Failed verdict — no content produced)
//   "NO_RESULT"     -> emit assistant text but exit 0 WITHOUT a terminal result line (a truncated
//                      stream that still delivered content must end Final, not Error)
//   "SCORING TASK"  -> return a canned {"score":0..1,"reason":"..."} JSON (LlmScorerBase judge path)
//   "JSON_SCHEMA"   -> return a canned JSON object (structured-output path)
//   "TOOL_DEMO"     -> drive the tool loop's prompt protocol: emit a {"tool":...} call first, then (once
//                      the tool observation is fed back into the prompt) a {"final":...} answer
//   "AGENT_SESSION" -> full multi-tool agentic transcript: init + text delta + tool_use + tool_result +
//                      result; honours --resume <id> from argv (uses that value as session_id)
//   else            -> echo a short deterministic completion derived from the prompt
//
// Maintenance argv (answered BEFORE stdin is read, like the real CLI's non-prompt paths):
//   --version       -> print a stub version line (the ClaudeCli provider's turn-free probe)
//   update|upgrade  -> print an "up to date" line and exit 0 (the self-update seam; installs nothing)
//   install [target]-> print an "installed" line and exit 0 (the PINNED-install seam; installs nothing)
//   auth status     -> print the CLI's auth-status JSON shape (the turn-free auth seam). Signed in by
//                      default; LYNTAI_STUB_AUTH=out reports a signed-out state instead.
//   auth login|logout -> print a line and exit 0 (drivable auth; the stub is stateless, so a following
//                      `auth status` still reports whatever LYNTAI_STUB_AUTH says)
import process from 'node:process';

// Maintenance flags/commands first: these take no prompt, so they must not block on stdin.
const argv0 = process.argv.slice(2);
if (argv0.includes('--version')) {
  process.stdout.write('0.0.0-stub (provider-stub)\n');
  process.exit(0);
}
if (argv0.includes('update') || argv0.includes('upgrade')) {
  process.stdout.write('provider-stub is up to date (0.0.0-stub)\n');
  process.exit(0);
}
if (argv0.includes('install')) {
  const target = argv0[argv0.indexOf('install') + 1];
  process.stdout.write(`provider-stub install ${target && !target.startsWith('--') ? target : 'stable'}: nothing to do (0.0.0-stub)\n`);
  process.exit(0);
}
if (argv0[0] === 'auth') {
  const loggedIn = process.env.LYNTAI_STUB_AUTH !== 'out';
  if (argv0[1] === 'status') {
    // the real shape of `claude auth status --json` (v2.1.220), with stub values
    process.stdout.write(JSON.stringify(loggedIn
      ? { loggedIn: true, authMethod: 'provider-stub', apiProvider: 'provider-stub', email: 'stub@example.invalid', orgId: 'stub-org', orgName: 'Stub Org', subscriptionType: 'stub' }
      : { loggedIn: false }) + '\n');
    process.exit(0);
  }
  process.stdout.write(`provider-stub ${argv0[1] === 'logout' ? 'logged out' : 'logged in'} (stateless stub)\n`);
  process.exit(0);
}

const chunks = [];
for await (const c of process.stdin) chunks.push(c);
const prompt = Buffer.concat(chunks).toString('utf8');

const emit = (obj) => process.stdout.write(JSON.stringify(obj) + '\n');
const sessionId = `stub-${Buffer.from(prompt).length.toString(36)}`; // stable per prompt, no Date.now
const usage = { input_tokens: 1200, output_tokens: 340, cache_read_input_tokens: 800 };
const finish = (text) => emit({ type: 'result', result: text, usage, total_cost_usd: 0.012 });

if (prompt.includes('AGENT_SESSION')) {
  const argv = process.argv.slice(2);
  const ri = argv.indexOf('--resume');
  const sid = ri >= 0 && argv[ri + 1] ? argv[ri + 1] : sessionId;
  emit({ type: 'system', subtype: 'init', session_id: sid, model: 'claude-stub' });
  emit({ type: 'stream_event', event: { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: 'Reading the file. ' } } });
  emit({ type: 'assistant', message: { model: 'claude-stub', content: [{ type: 'tool_use', id: 'toolu_1', name: 'Read', input: { file_path: 'README.md' } }], usage: { input_tokens: 100, output_tokens: 20, cache_read_input_tokens: 50, cache_creation_input_tokens: 10 } } });
  emit({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'toolu_1', content: 'stub file body', is_error: false }] } });
  finish('Agent session complete.');
  process.exit(0);
}

emit({ type: 'system', subtype: 'init', session_id: sessionId });

if (prompt.includes('SLOW')) {
  await new Promise((r) => setTimeout(r, 8000));
}

if (prompt.includes('FORCE_ERROR')) {
  finish(''); // empty → the provider reports Failed (no output)
  process.exit(0);
}

if (prompt.includes('NO_RESULT')) {
  emit({ type: 'assistant', message: { content: [{ type: 'text', text: 'stub reply without result line' }] } });
  process.exit(0); // no terminal result event
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

if (prompt.includes('TOOL_DEMO')) {
  // The tool loop's prompt protocol: first turn has no tool observation → ask to call the tool; the
  // second turn carries the fed-back "observed:" result → answer with a final. Stateless per call, but
  // the whole conversation (incl. the observation) is serialized into the prompt, so this stays exact.
  const reply = prompt.includes('observed:')
    ? JSON.stringify({ final: 'tool demo complete' })
    : JSON.stringify({ tool: 'echo', arguments: { text: 'lyntai' } });
  emit({ type: 'assistant', message: { content: [{ type: 'text', text: reply }] } });
  finish(reply);
  process.exit(0);
}

// Default: a deterministic completion. Echo the last non-empty prompt line so tests can assert round-trip.
const lastLine = prompt.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).pop() ?? '';
const text = `stub reply: ${lastLine.slice(0, 200)}`;
emit({ type: 'assistant', message: { content: [{ type: 'text', text }] } });
finish(text);
