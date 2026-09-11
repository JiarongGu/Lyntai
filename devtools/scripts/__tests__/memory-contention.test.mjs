// memory-contention's own tests.
//
// The harness measures what it costs to serve many memory seams from one model server. These tests
// verify that arm definitions (residency vs. routing, single vs. swapping) and the INI renderer
// produce the correct configuration for each role and the server's per-role constraints.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
  ARMS, PORTS, ROLES, renderPreset, settingsArgs, isFree, neighbourPids, parseListeners, vanishedNeighbours,
} from '../memory-contention.mjs';

describe('renderPreset', () => {
  it('gives the embedder and the reranker their role flags and the chat model NEITHER', () => {
    const ini = renderPreset('M', ['chat', 'embed', 'rerank']);
    // Process-wide flags: a plain --models-dir router 501s on both endpoints without these.
    // Anchor each to its own section: a bare port match `\[embed\][\s\S]*?embeddings` can scan
    // forward to the NEXT section if flags are swapped, producing a false pass. Isolate the body.
    const embed = ini.split('[embed]')[1].split('[')[0];
    assert.match(embed, /embeddings = true/);
    const rerank = ini.split('[rerank]')[1].split('[')[0];
    assert.match(rerank, /reranking = true/);
    const chat = ini.split('[chat]')[1].split('[')[0];
    assert.doesNotMatch(chat, /embeddings|reranking/,
      '--embedding RESTRICTS a server to embeddings; on the chat preset it would kill completions');
  });

  it('never emits a CRLF, because the server parses the INI line by line', () => {
    assert.doesNotMatch(renderPreset('M', ['chat']), /\r/);
  });

  it('emits each role\'s batch settings as long-form INI keys, unprefixed', () => {
    const ini = renderPreset('M', ['chat', 'embed', 'rerank']);
    const embed = ini.split('[embed]')[1].split('[')[0];
    assert.match(embed, /ctx-size = 2048/);
    assert.match(embed, /batch-size = 2048/);
    assert.match(embed, /ubatch-size = 2048/);
    const rerank = ini.split('[rerank]')[1].split('[')[0];
    assert.match(rerank, /ctx-size = 4096/);
    assert.match(rerank, /batch-size = 4096/);
    assert.match(rerank, /ubatch-size = 4096/);
    const chat = ini.split('[chat]')[1].split('[')[0];
    assert.match(chat, /ctx-size = 8192/);
    assert.doesNotMatch(chat, /batch-size|ubatch-size/,
      'causal prompts chunk fine at the default physical batch — only ctx-size is set for chat');
  });
});

describe('ROLES settings — the batch-size fix', () => {
  // Undersizing fails LOUDLY (`500 … input (N tokens) is too large`), which is what motivated these
  // specific numbers: embeddinggemma's trained window, and bge-m3's query+document pair well under 8192.
  it('sizes embed for ONE physical batch holding the whole pooled sequence', () => {
    assert.deepEqual(ROLES.embed.settings, { 'ctx-size': 2048, 'batch-size': 2048, 'ubatch-size': 2048 });
  });

  it('sizes rerank for a query+document pair', () => {
    assert.deepEqual(ROLES.rerank.settings, { 'ctx-size': 4096, 'batch-size': 4096, 'ubatch-size': 4096 });
  });

  it('gives chat only a context size, since causal prompts chunk fine at the default ubatch', () => {
    assert.deepEqual(ROLES.chat.settings, { 'ctx-size': 8192 });
  });
});

describe('settingsArgs / renderPreset — the equivalence that keeps the arms comparable', () => {
  // dedicated must NOT differ from a router arm by more than routing, or the whole comparison this bench
  // exists to make is void — so the preset INI and the dedicated-arm CLI args must agree on every value.
  it('emits the SAME batch settings the preset INI emits, for every role', () => {
    for (const role of ['chat', 'embed', 'rerank']) {
      const ini = renderPreset('M', [role]);
      const args = settingsArgs(role);
      for (const [key, value] of Object.entries(ROLES[role].settings)) {
        assert.match(ini, new RegExp(`${key} = ${value}(\\s|$)`), `INI missing ${key} for ${role}`);
        const idx = args.indexOf(`--${key}`);
        assert.ok(idx >= 0, `CLI args missing --${key} for ${role}`);
        assert.equal(args[idx + 1], String(value), `CLI value mismatch for --${key} on ${role}`);
      }
    }
  });

  it('produces `--flag value` pairs, not a single joined token', () => {
    assert.deepEqual(settingsArgs('chat'), ['--ctx-size', '8192']);
  });
});

describe('ARMS', () => {
  it('separates residency from routing, which the backlog item conflated', () => {
    const byName = Object.fromEntries(ARMS.map(a => [a.name, a]));
    assert.equal(byName['router-resident'].modelsMax, 4, 'must NOT swap at three models');
    assert.equal(byName['router-swapping'].modelsMax, 1, 'must be FORCED to evict');
    assert.equal(byName['dedicated'].kind, 'dedicated');
  });

  it('allocates no port the neighbour owns', () => {
    assert.ok(!Object.values(PORTS).includes(8090), '8090 is a sibling tool\'s embedding server');
  });
});

describe('parseListeners', () => {
  // The trap this test exists for: matching the bare port ALSO matches TIME_WAIT sockets a server you just
  // tore down leaves behind, so a free port reads BUSY. Observed 2026-09-11 on port 8137.
  const NETSTAT = [
    '  TCP    127.0.0.1:8140         0.0.0.0:0              LISTENING       4242',
    '  TCP    127.0.0.1:8141         127.0.0.1:14576        TIME_WAIT       0',
    '  TCP    127.0.0.1:14576        127.0.0.1:8141         TIME_WAIT       0',
  ].join('\n');

  it('reads only LISTENING rows, so a TIME_WAIT socket does not make a free port look busy', () => {
    const listeners = parseListeners(NETSTAT);
    assert.equal(listeners.get(8140), 4242);
    assert.equal(listeners.has(8141), false);
    assert.equal(isFree(listeners, 8141), true, 'TIME_WAIT is not an owner');
    assert.equal(isFree(listeners, 8140), false);
  });

  it('does not confuse a REMOTE port with a local one', () => {
    // 14576 appears only as a remote endpoint above; treating it as bound would abort a valid run.
    assert.equal(isFree(parseListeners(NETSTAT), 14576), true);
  });
});

describe('neighbourPids', () => {
  it('names every llama-server this harness did not start', () => {
    const rows = [{ pid: 22464, ppid: 18212 }, { pid: 4242, ppid: 999 }, { pid: 77, ppid: 4242 }];
    // 4242 is ours and 77 is its child; 22464 is the sibling's and must survive.
    assert.deepEqual(neighbourPids(rows, [4242]), [22464]);
  });
});

describe('vanishedNeighbours', () => {
  // The fix this suite exists for: an AGGREGATE check ("is the roster empty?", "is anything alive?") reports
  // clean if a DIFFERENT llama-server happens to still be up while the one that mattered is gone — the exact
  // "I only killed my own PIDs is an argument, not evidence" failure. Compare by PID instead.
  it('flags a PID that disappeared even though a DIFFERENT llama-server is still up', () => {
    const before = [{ pid: 22464, port: 8090, alive: true }];
    const after = [{ pid: 99999, port: 9999, alive: true }];   // an unrelated server, not the one we care about
    assert.deepEqual(vanishedNeighbours(before, after), [22464]);
  });

  it('flags a PID that is still LISTED but has stopped ANSWERING', () => {
    const before = [{ pid: 22464, port: 8090, alive: true }];
    const after = [{ pid: 22464, port: 8090, alive: false }];
    assert.deepEqual(vanishedNeighbours(before, after), [22464]);
  });

  it('is clean when the SAME pid is present and still alive', () => {
    const before = [{ pid: 22464, port: 8090, alive: true }];
    const after = [{ pid: 22464, port: 8090, alive: true }];
    assert.deepEqual(vanishedNeighbours(before, after), []);
  });

  it('names EACH vanished pid when there is more than one neighbour', () => {
    const before = [{ pid: 1, port: 111, alive: true }, { pid: 2, port: 222, alive: true }];
    const after = [{ pid: 1, port: 111, alive: true }];
    assert.deepEqual(vanishedNeighbours(before, after), [2]);
  });
});
