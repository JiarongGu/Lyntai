import assert from 'node:assert/strict';
import { test } from 'node:test';

import { listingHasPdb, packageIdsFrom } from '../consumer-smoke-lib.mjs';

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
