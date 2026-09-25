import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';

import { HARNESS_PORTS, RESERVED_PORTS, modelDirFromEnv } from '../_llama-harness.mjs';
import { DEFAULT_PORT as EMBED_SCREEN_PORT } from '../embed-screen.mjs';
import { PORTS as LOCOMO_PORTS } from '../locomo-pair.mjs';
import { PORTS as CONTENTION_PORTS } from '../memory-contention.mjs';
import { PORTS as DECISION_PORTS } from '../memory-decision.mjs';
import { DEFAULT_PORT as RERANK_PORT } from '../rerank-screen.mjs';
import {
  EXTRA_PORT_BASE, MAX_EXTRA_ARMS, NATIVE_PORT, PORTS as AFFORDANCE_PORTS,
} from '../tool-affordance.mjs';

describe('HARNESS_PORTS — the one port registry', () => {
  it('gives every role of every harness its own port, and none a reserved one', () => {
    // Binding a busy port fails UPWARD: the incumbent answers and every figure is taken on its model.
    const owner = new Map();
    for (const [harness, roles] of Object.entries(HARNESS_PORTS)) {
      for (const [role, port] of Object.entries(roles)) {
        const at = `${harness}.${role}`;
        assert.ok(!owner.has(port), `${at} and ${owner.get(port)} both bind ${port}`);
        owner.set(port, at);
      }
    }
    for (const port of RESERVED_PORTS) assert.ok(!owner.has(port), `${owner.get(port)} binds reserved ${port}`);
  });

  it('is the table every harness binds from, so the disjointness above covers the live ports', () => {
    assert.deepEqual(CONTENTION_PORTS, HARNESS_PORTS['memory-contention']);
    assert.deepEqual(DECISION_PORTS, HARNESS_PORTS['memory-decision']);
    assert.deepEqual(LOCOMO_PORTS, HARNESS_PORTS['locomo-pair']);
    assert.equal(RERANK_PORT, HARNESS_PORTS['rerank-screen'].screen);
    assert.equal(EMBED_SCREEN_PORT, HARNESS_PORTS['embed-screen'].screen);
    const own = HARNESS_PORTS['tool-affordance'];
    assert.deepEqual(AFFORDANCE_PORTS, { chat: own.chat, small: own.small, embed: own.embed, rerank: own.rerank });
    assert.equal(NATIVE_PORT, own.native);
  });

  it('registers one contiguous port per tool-affordance embedder arm, and no more', () => {
    // `extraSpecs` allocates EXTRA_PORT_BASE + i, so an unregistered arm port would escape the check above.
    const own = HARNESS_PORTS['tool-affordance'];
    const extras = Object.keys(own).filter((k) => /^extra\d+$/.test(k));
    assert.equal(extras.length, MAX_EXTRA_ARMS);
    for (let i = 0; i < MAX_EXTRA_ARMS; i++) assert.equal(own[`extra${i}`], EXTRA_PORT_BASE + i);
  });
});

describe('modelDirFromEnv', () => {
  it('reads LYNTAI_MODEL_DIR, then its legacy name, and has NO default', () => {
    assert.equal(modelDirFromEnv({ LYNTAI_MODEL_DIR: 'a', LYNTAI_CONTENTION_MODEL_DIR: 'b' }), 'a');
    assert.equal(modelDirFromEnv({ LYNTAI_CONTENTION_MODEL_DIR: 'b' }), 'b');
    assert.equal(modelDirFromEnv({}), null);
  });
});
