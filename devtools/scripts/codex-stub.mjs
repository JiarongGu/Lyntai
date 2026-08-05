#!/usr/bin/env node
// Deterministic `codex` CLI stub for tests — speaks just enough of codex-cli's surface for the CodexCli
// provider (Lyntai.Providers.CodexCli) to parse. A SEPARATE stub from provider-stub.mjs on purpose: that one
// speaks claude's stream-json, this one speaks codex's JSONL, and a stub that faked both would stop being a
// faithful model of either. Spawned via LYNTAI_PROVIDER_CMD / CODEX_CMD, so no real tokens are spent.
//
// The event shapes below are copied from a REAL codex-cli 0.146.0 run (a successful turn via the --oss local
// path, and a failed one) — keep them that way. If you need a new shape, measure it, don't invent it.
//
// Maintenance argv (answered BEFORE stdin is read, like the real CLI's non-prompt paths):
//   --version        -> "codex-cli 0.0.0-stub"
//   update           -> an "up to date" line, exit 0 (installs nothing)
//   login status     -> "Not logged in" (LYNTAI_STUB_AUTH=in reports a signed-in line instead)
//   login | logout   -> a line, exit 0 (the stub is stateless)
//
// Prompt-marker behavior (the prompt arrives on stdin, as `codex exec … -` does):
//   "FORCE_ERROR"    -> emit turn.failed (an in-band failure at exit 0 — the codex-shaped failure path)
//   "AUTH_ERROR"     -> emit turn.failed with a 401 message (must classify as AuthFailed)
//   "AUTH_ERROR_EXIT"-> the measured EXPIRED-LOGIN pair: a bare `error` line (string message) + turn.failed
//                       (object message), stderr chatter, and a NON-ZERO exit
//   "NOISY"          -> emit non-terminal noise (a bare `error` line + an `error` ITEM) and then succeed
//   else             -> echo a deterministic agent_message + turn.completed with usage
import process from 'node:process';

const argv = process.argv.slice(2);
const emit = (obj) => process.stdout.write(JSON.stringify(obj) + '\n');

if (argv.includes('--version')) {
  process.stdout.write('codex-cli 0.0.0-stub\n');
  process.exit(0);
}
if (argv[0] === 'update') {
  process.stdout.write('codex is already up to date (0.0.0-stub)\n');
  process.exit(0);
}
if (argv[0] === 'login' && argv[1] === 'status') {
  process.stdout.write(process.env.LYNTAI_STUB_AUTH === 'in'
    ? 'Logged in using ChatGPT account stub@example.invalid\n'
    : 'Not logged in\n');
  process.exit(0);
}
if (argv[0] === 'login' || argv[0] === 'logout') {
  process.stdout.write(`codex stub ${argv[0]} complete\n`);
  process.exit(0);
}

const chunks = [];
for await (const c of process.stdin) chunks.push(c);
const prompt = Buffer.concat(chunks).toString('utf8');

// stable per prompt, no Date.now (deterministic across runs)
emit({ type: 'thread.started', thread_id: `stub-${Buffer.from(prompt).length.toString(36)}` });

if (prompt.includes('NOISY')) {
  // both of these appeared in a REAL run that went on to SUCCEED — neither may fail the call
  emit({ type: 'error', message: 'Reconnecting... 2/5 (transient stub notice)' });
  emit({ type: 'item.completed', item: { id: 'item_0', type: 'error', message: 'Model metadata not found; using fallback' } });
}

emit({ type: 'turn.started' });

// The exit code is SET, never `process.exit()`d: exiting while stdout writes are still queued would drop
// the very lines a test is asserting on (stdout is a pipe here, so its writes are async).
// AUTH_ERROR_EXIT is checked BEFORE AUTH_ERROR — the specific marker contains the general one.
if (prompt.includes('AUTH_ERROR_EXIT')) {
  // MEASURED 2026-08-05 against an account whose login had EXPIRED: one turn prints both error-ish events,
  // which do NOT share a shape (`error` carries a string `message`, `turn.failed` an OBJECT nesting one),
  // and then the process exits NON-ZERO with codex's ordinary startup chatter on stderr. The 401 appears
  // only in the in-band message, so a reader that classifies the exit/stderr instead loses it.
  emit({ type: 'error', message: 'Reconnecting... 2/5 (unexpected status 401 Unauthorized)' });
  emit({ type: 'turn.failed', error: { message: 'unexpected status 401 Unauthorized: expired login' } });
  process.stderr.write('Reading prompt from stdin...\n');
  process.exitCode = 1;
} else if (prompt.includes('AUTH_ERROR')) {
  // measured on 0.146.0: an in-band failure at exit 0 — the other half of the pair above
  emit({ type: 'turn.failed', error: { message: 'unexpected status 401 Unauthorized: Missing bearer or basic authentication in header' } });
} else if (prompt.includes('FORCE_ERROR')) {
  emit({ type: 'turn.failed', error: { message: 'stub turn failure' } });
} else {
  const lastLine = prompt.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).pop() ?? '';
  emit({ type: 'item.completed', item: { id: 'item_1', type: 'agent_message', text: `codex stub reply: ${lastLine.slice(0, 200)}` } });
  emit({
    type: 'turn.completed',
    usage: { input_tokens: 6489, cached_input_tokens: 12, cache_write_input_tokens: 0, output_tokens: 2, reasoning_output_tokens: 0 },
  });
}
