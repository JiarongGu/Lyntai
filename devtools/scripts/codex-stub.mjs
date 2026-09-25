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

/** Exit once stdout has DRAINED: `process.exit()` with pipe writes still queued drops them, and the
 * lines it drops are the ones a test asserts on. Never resolves — the process ends in the callback. */
const exitFlushed = (code) => new Promise(() => process.stdout.write('', () => process.exit(code)));

const argv = process.argv.slice(2);
const emit = (obj) => process.stdout.write(JSON.stringify(obj) + '\n');

if (argv.includes('--version')) {
  process.stdout.write('codex-cli 0.0.0-stub\n');
  await exitFlushed(0);
}
if (argv[0] === 'update') {
  process.stdout.write('codex is already up to date (0.0.0-stub)\n');
  await exitFlushed(0);
}
if (argv[0] === 'login' && argv[1] === 'status') {
  process.stdout.write(process.env.LYNTAI_STUB_AUTH === 'in'
    ? 'Logged in using ChatGPT account stub@example.invalid\n'
    : 'Not logged in\n');
  await exitFlushed(0);
}
if (argv[0] === 'login' || argv[0] === 'logout') {
  process.stdout.write(`codex stub ${argv[0]} complete\n`);
  await exitFlushed(0);
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

// The exit code is SET rather than exited on, so every line below drains first (`exitFlushed` above).
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
} else if (prompt.includes('TOOL_TURN')) {
  // MEASURED 2026-09-19 against codex-cli 0.155.1 (D35 re-measurement, docs/task-archive.md Part 260): a
  // real authenticated turn that ran a shell command that FAILED then one that SUCCEEDED, and edited a
  // file. Verbatim item shapes — do not invent; the machine path in file_change is the only edit, to a
  // neutral one. codex emits item.started for EVERY tool item, so the reader's primary ToolCall path fires
  // and the failure signals (top-level status + exit_code) are exactly what IsFailedItem reads.
  emit({ type: 'item.completed', item: { id: 'item_1', type: 'reasoning', text: 'Ill run the command and count the lines.' } });
  // a shell step that FAILED: status "failed" AND non-zero exit_code, both top-level, in agreement
  emit({ type: 'item.started', item: { id: 'item_2', type: 'command_execution', command: 'cmd /c dir /b', aggregated_output: '', exit_code: null, status: 'in_progress' } });
  emit({ type: 'item.completed', item: { id: 'item_2', type: 'command_execution', command: 'cmd /c dir /b', aggregated_output: 'Parameter format not correct - "b".\r\n', exit_code: 1, status: 'failed' } });
  // a shell step that SUCCEEDED: status "completed" AND exit_code 0
  emit({ type: 'item.started', item: { id: 'item_3', type: 'command_execution', command: 'powershell -NoProfile -Command dir', aggregated_output: '', exit_code: null, status: 'in_progress' } });
  emit({ type: 'item.completed', item: { id: 'item_3', type: 'command_execution', command: 'powershell -NoProfile -Command dir', aggregated_output: 'seed.txt\r\n', exit_code: 0, status: 'completed' } });
  // a file edit: file_change carries no exit_code, so its failure would ride `status` alone
  emit({ type: 'item.started', item: { id: 'item_4', type: 'file_change', changes: [{ path: 'ws/hello.txt', kind: 'add' }], status: 'in_progress' } });
  emit({ type: 'item.completed', item: { id: 'item_4', type: 'file_change', changes: [{ path: 'ws/hello.txt', kind: 'add' }], status: 'completed' } });
  emit({ type: 'item.completed', item: { id: 'item_5', type: 'agent_message', text: 'It printed exactly 1 line.' } });
  emit({ type: 'turn.completed', usage: { input_tokens: 8194, cached_input_tokens: 0, cache_write_input_tokens: 0, output_tokens: 42, reasoning_output_tokens: 0 } });
} else {
  const lastLine = prompt.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).pop() ?? '';
  emit({ type: 'item.completed', item: { id: 'item_1', type: 'agent_message', text: `codex stub reply: ${lastLine.slice(0, 200)}` } });
  emit({
    type: 'turn.completed',
    usage: { input_tokens: 6489, cached_input_tokens: 12, cache_write_input_tokens: 0, output_tokens: 2, reasoning_output_tokens: 0 },
  });
}
