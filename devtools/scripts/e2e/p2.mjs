// e2e p2 — Playground GOVERNANCE demo (LYNTAI_DEMO=governance) full-stack smoke against the provider
// stub: response cache, usage budget, rate limit, semantic memory, and a recurring schedule — the platform
// features the default p1 flow doesn't exercise. Run `node devtools/dev.mjs e2e p2 --build`.
import { freshDataDir, makeReporter, runPlayground } from './_e2e-common.mjs';

const suite = 'p2';
const { ok, done } = makeReporter(suite);
const dataDir = freshDataDir(suite);

const { status, out } = runPlayground({ dataDir, env: { LYNTAI_DEMO: 'governance' } });

ok('playground exits 0', status === 0, `exit ${status}\n${out}`);
ok('response cache served a hit', out.includes('governance: cache hit served=True'));
ok('usage budget refused over the cap', out.includes('governance: budget refused over cap=True'));
ok('rate limiter refused', out.includes('governance: rate limited=True'));
ok('semantic memory recalled by meaning', out.includes('governance: semantic recall ranked=True'));
ok('recurring schedule enqueued a job', out.includes('governance: cron/schedule enqueued=True'));
ok('final OK marker', out.includes('governance: OK'));

done();
