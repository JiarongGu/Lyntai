// e2e p1 — Playground full-stack smoke against the provider stub: completion + scoring + trace +
// memory over an isolated data dir, no real tokens. Run `node devtools/dev.mjs e2e p1 --build`.
import fs from 'node:fs';
import path from 'node:path';
import { freshDataDir, makeReporter, runPlayground } from './_e2e-common.mjs';

const suite = 'p1';
const { ok, done } = makeReporter(suite);
const dataDir = freshDataDir(suite);

const { status, out } = runPlayground({ dataDir });

ok('playground exits 0', status === 0, `exit ${status}\n${out}`);
ok('completion verdict Ok', out.includes('playground: verdict=Ok'));
ok('stub reply came through', out.includes('playground: reply=stub reply:'));
ok('scores computed (incl. judge)', /playground: scores=[1-9]/.test(out));
ok('trace persisted with steps + tokens', /playground: trace steps=[1-9]\d* tokens=[1-9]/.test(out));
ok('memory row recalled', /playground: memory recall=[1-9]/.test(out));
ok('sqlite db written in the data dir', fs.existsSync(path.join(dataDir, 'lyntai.db')));
ok('final OK marker', out.includes('playground: OK'));

done();
