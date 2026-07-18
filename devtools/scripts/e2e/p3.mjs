// e2e p3 — Playground AGENT-SESSION demo (LYNTAI_DEMO=agent-session) full-stack smoke against the
// provider stub: proves the self-driving CLI-agent-session primitive end-to-end (spawn + gate flags +
// streamed events + resume) without spending real tokens. Run `node devtools/dev.mjs e2e p3 --build`.
import { freshDataDir, makeReporter, runPlayground } from './_e2e-common.mjs';

const suite = 'p3';
const { ok, done } = makeReporter(suite);
const dataDir = freshDataDir(suite);

const { status, out } = runPlayground({ dataDir, env: { LYNTAI_DEMO: 'agent-session' } });

ok('playground exits 0', status === 0, `exit ${status}\n${out}`);
ok('session 1 streamed tool call', /agent: tool=Read file=README\.md/.test(out));
ok('session 1 read-only completed Ok', /agent: session1 id=\S+ events=[1-9]\d* tools=[1-9] verdict=Ok/.test(out));
ok('session 2 resumed and completed Ok', /agent: session2 id=\S+ events=[1-9]\d* resumed=True verdict=Ok/.test(out));
ok('final OK marker', out.includes('agent: OK'));

done();
