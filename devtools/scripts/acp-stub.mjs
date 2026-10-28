#!/usr/bin/env node
// Deterministic Agent Client Protocol agent for tests — the ACP door's stand-in, at zero tokens. It speaks
// JSON-RPC 2.0 over stdio, one frame per line, as an agent (claude-agent-acp, codex-acp) does, and a SEPARATE
// script from provider-stub.mjs and codex-stub.mjs on purpose: those speak a CLI's JSONL, and a stub that faked
// three wires would stop being a faithful model of any.
//
// Every shape below is SOURCE-DERIVED unless a comment says REAL: read from the published protocol schema and
// from the shipped bundles of the two adapters (codex-acp 2.1.1's zod schemas and failure policy), never
// measured on a turn — no turn was taken against a real agent. REAL = a frame recorded from a real
// codex-acp 2.1.1 handshake (initialize and a logged-out session/new; no prompt, no sign-in).
//
// Environment:
//   LYNTAI_ACP_STUB_CAPS   comma list of what initialize states: load, resume, http (default "load,resume")
//   LYNTAI_ACP_STUB_AUTH   "out" = session/new/load/resume answer REAL's -32000 "Authentication required"
//   LYNTAI_ACP_STUB_LOG    a file every received frame is appended to, one JSON line each, so a test can read
//                          what the client SENT (cwd, mcpServers, the prompt blocks, session/cancel)
//
// A marker in the prompt text picks a behaviour, each commented where `prompt` handles it; none echoes the
// last line with usage and ends the turn.
import { appendFileSync } from 'node:fs';
import process from 'node:process';
import { createInterface } from 'node:readline';

const caps = new Set((process.env.LYNTAI_ACP_STUB_CAPS ?? 'load,resume').split(',').map((c) => c.trim()).filter(Boolean));
const logPath = process.env.LYNTAI_ACP_STUB_LOG;
const NEW_SESSION = 'acp-stub-session-1';
let SESSION = NEW_SESSION;   // the conversation the client opened: a new one, or the one it asked to load or resume

const log = (frame) => { if (logPath) appendFileSync(logPath, JSON.stringify(frame) + '\n'); };
const send = (frame) => process.stdout.write(JSON.stringify({ jsonrpc: '2.0', ...frame }) + '\n');
const update = (sessionUpdate, body = {}, sessionId = SESSION) =>
  send({ method: 'session/update', params: { sessionId, update: { sessionUpdate, ...body } } });
const text = (sessionUpdate, value) => update(sessionUpdate, { content: { type: 'text', text: value } });
const fail = (id, code, message, data) => send({ id, error: { code, message, ...(data === undefined ? {} : { data }) } });

/** Exit once stdout has DRAINED: `process.exit()` with pipe writes queued drops them. */
const exitFlushed = (code) => process.stdout.write('', () => process.exit(code));

let airFailures = false;
const pending = new Map();
let nextAgentRequest = 1;
let cancelWaiter = null;
let cancelled = false;

/** An agent-to-client request, answered by the matching response frame. */
const ask = (method, params) => new Promise((resolve) => {
  const id = `agent-${nextAgentRequest++}`;
  pending.set(id, resolve);
  send({ id, method, params });
});

const initialize = (id, params) => {
  const declared = params?.clientCapabilities?._meta?.jetbrains?.air?.capabilities;
  airFailures = Array.isArray(declared) && declared.includes('sessionFailure');
  send({
    id,
    result: {
      protocolVersion: 1,
      agentInfo: { name: 'lyntai-acp-stub', title: 'Lyntai ACP stand-in', version: '0.0.0-stub' },
      agentCapabilities: {
        loadSession: caps.has('load'),
        promptCapabilities: { embeddedContext: true, image: true },
        ...(caps.has('resume') ? { sessionCapabilities: { resume: {}, list: {} } } : {}),
        mcpCapabilities: { http: caps.has('http'), sse: false },
      },
      authMethods: [],
    },
  });
};

const open = (id, method, params) => {
  if (process.env.LYNTAI_ACP_STUB_AUTH === 'out') return fail(id, -32000, 'Authentication required');   // REAL
  if (method === 'session/new') return send({ id, result: { sessionId: SESSION } });
  SESSION = params.sessionId;
  if (params.sessionId === 'unknown-session') return fail(id, -32002, 'Resource not found', { reason: 'resource_not_found' });
  if (method === 'session/load') {
    // a load replays the conversation as updates BEFORE it answers; the client must not take them for the turn
    text('user_message_chunk', 'an earlier question', params.sessionId);
    text('agent_message_chunk', 'an earlier answer', params.sessionId);
  }
  return send({ id, result: {} });
};

const usage = { totalTokens: 6491, inputTokens: 6489, outputTokens: 2, cachedReadTokens: 11 };
const end = (id, stopReason = 'end_turn', extra = {}) => send({ id, result: { stopReason, usage, ...extra } });
const LIMIT_SENTENCE = 'You’ve hit your usage limit. Try again at 4:05 PM.';

const prompt = async (id, params) => {
  const said = (params.prompt ?? []).map((b) => b.text ?? '').join('\n');
  const mark = (m) => said.includes(m);

  if (mark('DIE_EARLY')) return process.exit(1);
  if (mark('AUTH_PROMPT')) return fail(id, -32000, 'Authentication required');
  if (mark('FORCE_ERROR')) return fail(id, -32603, 'Internal error: stub turn failure', { errorKind: 'server_error' });

  // claude-agent-acp's limit: a rejected _claude/rateLimit usage_update, then -32603 with data.errorKind
  if (mark('LIMIT_CLAUDE')) {
    update('usage_update', {
      used: 51200, size: 200000,
      _meta: { '_claude/rateLimit': { status: 'rejected', rateLimitType: 'five_hour', resetsAt: 1893456000 } },
    });
    return fail(id, -32603, 'Internal error: You’ve hit your limit', { errorKind: 'rate_limit' });
  }
  // codex-acp's limit: the sentence streamed as agent text, then -32603 "Internal error" with the sentence in data
  if (mark('LIMIT_CODEX')) {
    text('agent_message_chunk', LIMIT_SENTENCE);
    return fail(id, -32603, 'Internal error', { message: LIMIT_SENTENCE, codexErrorInfo: 'usageLimitExceeded' });
  }
  // end_turn with an AIR sessionFailure when the client declared it; else the sentence as text and a plain end_turn
  if (mark('TYPED_FAILURE')) {
    if (!airFailures) { text('agent_message_chunk', LIMIT_SENTENCE); return end(id); }
    const sessionFailure = { id: 'turn-1:error', revision: 1, category: 'limit', severity: 'error', title: LIMIT_SENTENCE, actions: [] };
    return end(id, 'end_turn', { _meta: { jetbrains: { air: { version: 1, sessionFailure } } } });
  }
  // a retry notice (severity warning) mid-turn, then an ordinary success
  if (mark('AIR_WARNING')) {
    const sessionFailure = { id: 'turn-1:error', revision: 1, category: 'connection', severity: 'warning', title: 'Reconnecting... 1/5', actions: [] };
    update('session_info_update', { _meta: { jetbrains: { air: { version: 1, sessionFailure } } } });
  }
  if (mark('MAX_TOKENS')) return end(id, 'max_tokens');
  if (mark('REFUSAL')) return end(id, 'refusal');

  // one chunk, then SLOW_TURN waits for session/cancel and answers cancelled; IGNORE_CANCEL never answers
  if (mark('SLOW_TURN') || mark('IGNORE_CANCEL')) {
    text('agent_message_chunk', 'working');
    if (mark('IGNORE_CANCEL')) return;   // never answers; the client's grace expires and the process is killed
    await new Promise((resolve) => { if (cancelled) resolve(); else cancelWaiter = resolve; });
    return end(id, 'cancelled');
  }
  // DIE_EARLY (above) exits on receiving the prompt, DIE_MID after a chunk, DIE_AFTER_TOOL after a tool_call
  if (mark('DIE_MID')) { text('agent_message_chunk', 'half an answ'); return setTimeout(() => process.exit(1), 50); }
  if (mark('DIE_AFTER_TOOL')) {
    update('tool_call', { toolCallId: 'call_1', title: 'Run it', kind: 'execute', status: 'pending', rawInput: { command: 'ls' } });
    return setTimeout(() => process.exit(1), 50);
  }

  // asks to edit (always, once or reject offered) and says which option the client chose
  if (mark('PERMISSION_TURN')) {
    const outcome = await ask('session/request_permission', {
      sessionId: SESSION,
      toolCall: { toolCallId: 'call_p', title: 'Edit hello.txt', kind: 'edit', status: 'pending' },
      options: [
        { optionId: 'allow-always', name: 'Always allow', kind: 'allow_always' },
        { optionId: 'allow-once', name: 'Allow', kind: 'allow_once' },
        { optionId: 'reject-once', name: 'Reject', kind: 'reject_once' },
      ],
    });
    const chosen = outcome?.result?.outcome;
    text('agent_message_chunk', `permission: ${chosen?.outcome === 'selected' ? chosen.optionId : chosen?.outcome}`);
    return end(id);
  }
  // asks for a file the client declined to offer and says the error code it got back
  if (mark('FS_TURN')) {
    const answer = await ask('fs/read_text_file', { sessionId: SESSION, path: '/project/hello.txt' });
    text('agent_message_chunk', `fs: ${answer?.error?.code}`);
    return end(id);
  }

  // a thought, a plan, a read that completes and a command that fails, then a message
  if (mark('PLAN_TURN')) {
    text('agent_thought_chunk', 'I will read the file, then run it.');
    update('plan', {
      entries: [
        { content: 'Read hello.txt', priority: 'high', status: 'completed' },
        { content: 'Run the script', priority: 'medium', status: 'in_progress' },
        { content: 'Report', priority: 'low', status: 'pending' },
      ],
    });
    update('tool_call', { toolCallId: 'call_1', title: 'Read hello.txt', kind: 'read', status: 'pending', locations: [{ path: '/project/hello.txt' }], rawInput: { path: '/project/hello.txt' } });
    update('tool_call_update', { toolCallId: 'call_1', status: 'in_progress' });
    update('tool_call_update', { toolCallId: 'call_1', status: 'completed', content: [{ type: 'content', content: { type: 'text', text: 'hello' } }] });
    update('tool_call', { toolCallId: 'call_2', title: 'Run script', kind: 'execute', status: 'in_progress', rawInput: { command: 'node run.js' } });
    update('tool_call_update', { toolCallId: 'call_2', status: 'failed', rawOutput: { exit_code: 1 } });
    text('agent_message_chunk', 'It read hello and the script failed.');
    return end(id);
  }

  const last = said.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).pop() ?? '';
  text('agent_message_chunk', `acp stub reply: ${last.slice(0, 200)}`);
  return end(id);
};

const input = createInterface({ input: process.stdin, crlfDelay: Infinity });
input.on('line', (line) => {
  if (!line.trim()) return;
  let frame;
  try { frame = JSON.parse(line); } catch { return; }
  log(frame);

  if (frame.method === undefined) {   // a response to a request this agent made
    const resolve = pending.get(frame.id);
    if (resolve) { pending.delete(frame.id); resolve(frame); }
    return;
  }
  switch (frame.method) {
    case 'initialize': return initialize(frame.id, frame.params);
    case 'session/new':
    case 'session/load':
    case 'session/resume': return open(frame.id, frame.method, frame.params ?? {});
    case 'session/prompt': void prompt(frame.id, frame.params ?? {}); return;
    case 'session/cancel': cancelled = true; if (cancelWaiter) cancelWaiter(); return;
    default: if (frame.id !== undefined) fail(frame.id, -32601, `method not found: ${frame.method}`);
  }
});
input.on('close', () => { void exitFlushed(0); });
