// memory-contention's own tests.
//
// The harness measures what it costs to serve many memory seams from one model server. These tests
// verify that arm definitions (residency vs. routing, single vs. swapping) and the INI renderer
// produce the correct configuration for each role and the server's per-role constraints.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { ARMS, PORTS, renderPreset, isFree, neighbourPids, parseListeners } from '../memory-contention.mjs';

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
