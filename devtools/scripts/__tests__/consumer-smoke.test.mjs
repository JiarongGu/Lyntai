import assert from 'node:assert/strict';
import { test } from 'node:test';

import { listingHasPdb, packageIdsFrom, zipEntryNames } from '../consumer-smoke-lib.mjs';

// `consumer-smoke` was the last guard with no tests, and its own backlog entry explained why: the thing
// worth testing IS the minutes-long pack/restore/build/run, and stubbing the pack would test the
// bookkeeping and none of the risk. That is true of the PROCESS and false of these two steps — pure string
// work, inside the process, whose failure mode is a green run that verified nothing. Both directions are
// asserted for each, because for a guard the dangerous direction is the false PASS.

test('packageIdsFrom derives the lowercased cache directory name', () => {
  const feed = ['Lyntai.9.9.9-smoke.nupkg', 'Lyntai.Storage.Sqlite.9.9.9-smoke.nupkg'];

  assert.deepEqual(packageIdsFrom(feed, '9.9.9-smoke'), ['lyntai', 'lyntai.storage.sqlite']);
});

test('packageIdsFrom ignores a nupkg from a DIFFERENT version rather than mangling its id', () => {
  // The regression this function exists for. Trimming a fixed LENGTH would cut the wrong number of
  // characters off the stale file and yield a garbage id — which evicts nothing, leaves the cached copy in
  // place, and makes the smoke restore and test the OLD package while reporting success.
  const feed = ['Lyntai.9.9.9-smoke.nupkg', 'Lyntai.2.5.0.nupkg'];

  const ids = packageIdsFrom(feed, '9.9.9-smoke');

  assert.deepEqual(ids, ['lyntai']);
  assert.ok(!ids.some((id) => id.includes('2.5')), 'a foreign version must not produce a mangled id');
});

test('packageIdsFrom does not treat a symbol package as a package', () => {
  // Relies on `"x.snupkg".endsWith(".nupkg")` being false — the character before `nupkg` is `s`, not `.`.
  // Pinned because it is the kind of thing a refactor to a looser match would quietly break.
  assert.deepEqual(packageIdsFrom(['Lyntai.9.9.9-smoke.snupkg'], '9.9.9-smoke'), []);
});

test('packageIdsFrom tolerates an empty feed', () => {
  assert.deepEqual(packageIdsFrom([], '9.9.9-smoke'), []);
  assert.deepEqual(packageIdsFrom(null, '9.9.9-smoke'), []);
});

test('listingHasPdb finds a real symbol entry', () => {
  const listing = 'lib/net10.0/Lyntai.Core.pdb\n_rels/.rels\n[Content_Types].xml\n';

  assert.equal(listingHasPdb(listing), true);
});

test('listingHasPdb refuses a package whose only mention of pdb is not a symbol file', () => {
  // The FALSE PASS direction, which is the one that matters: a substring match on `.pdb` would pass this
  // and wave through a symbol package carrying no symbols at all.
  const listing = 'src/pdb.cs\npdb/readme.txt\n_rels/.rels\n';

  assert.equal(listingHasPdb(listing), false);
});

test('listingHasPdb treats an empty or missing listing as no symbols', () => {
  assert.equal(listingHasPdb(''), false);
  assert.equal(listingHasPdb(null), false);
});

test('listingHasPdb ignores surrounding whitespace and case', () => {
  assert.equal(listingHasPdb('  lib/net10.0/Lyntai.Core.PDB  \n'), true);
});

/** A minimal but REAL zip (stored, no compression), built by hand so the test needs no fixture binary. */
function zip(entries) {
  const locals = [];
  const central = [];
  let offset = 0;
  for (const [name, body] of entries) {
    const n = Buffer.from(name, 'utf8');
    const d = Buffer.from(body, 'utf8');
    const lh = Buffer.alloc(30);
    lh.writeUInt32LE(0x04034b50, 0);
    lh.writeUInt16LE(n.length, 26);
    locals.push(lh, n, d);

    const ch = Buffer.alloc(46);
    ch.writeUInt32LE(0x02014b50, 0);
    ch.writeUInt32LE(d.length, 20);
    ch.writeUInt32LE(d.length, 24);
    ch.writeUInt16LE(n.length, 28);
    ch.writeUInt32LE(offset, 42);
    central.push(ch, n);
    offset += 30 + n.length + d.length;
  }
  const cd = Buffer.concat(central);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(cd.length, 12);
  eocd.writeUInt32LE(offset, 16);
  return Buffer.concat([Buffer.concat(locals), cd, eocd]);
}

test('lists every entry name', () => {
  const names = zipEntryNames(zip([['lib/net10.0/Lyntai.Core.pdb', 'x'], ['_rels/.rels', 'y']]));
  assert.deepEqual(names, ['lib/net10.0/Lyntai.Core.pdb', '_rels/.rels']);
});

test('a symbol package carrying a PDB is recognised', () => {
  assert.ok(listingHasPdb(zipEntryNames(zip([['lib/net10.0/Lyntai.Core.pdb', 'x']])).join('\n')));
});

test('one carrying NO pdb is not — the defect this step exists to catch', () => {
  assert.ok(!listingHasPdb(zipEntryNames(zip([['_rels/.rels', 'y'], ['Lyntai.nuspec', 'z']])).join('\n')));
});

test('an unreadable buffer yields NO names, so the caller can fail closed', () => {
  // The whole point of the rewrite: `tar -tf` returned a non-zero status on this machine for every
  // snupkg (GNU tar cannot read zip), the call site skipped the check on non-zero, and the step printed
  // its ✓ anyway. An empty result must be distinguishable from "checked and fine".
  assert.deepEqual(zipEntryNames(Buffer.from('not a zip at all')), []);
  assert.deepEqual(zipEntryNames(Buffer.alloc(0)), []);
  assert.deepEqual(zipEntryNames(null), []);
});
