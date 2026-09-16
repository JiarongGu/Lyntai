// nuget-unlist — the retirement roster. See devtools/nuget-unlist.mjs.
//
// The roster's hand-kept half holds PUBLISHED package ids, and a rename sweep in the tree must never touch
// them. Three have been rewritten anyway (2026-09-15), and the third survived a whole day because the tool
// printed `- not published, skipping` and then reported success: a wrong id and a genuinely-never-published
// id were the same unremarkable line. That is the false PASS this file exists to pin.
//
// The facts below are about the ROSTER as data, so they run without the network — which is why the tool
// guards its own execution behind an `import.meta.url` check and exports the predicate.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { RETIRED, mustBePublished } from '../../nuget-unlist.mjs';

describe('nuget-unlist RETIRED roster', () => {
  it('flags a retired id the feed has never published, because that can only be a typo', () => {
    assert.equal(mustBePublished('Lyntai.Providers.ExtensionsAi', null), true);
  });

  it('says nothing about an id that is not retired — a live package may be unpublished', () => {
    assert.equal(mustBePublished('Lyntai.Core', null), false);
  });

  it('says nothing about a retired id that IS published — the normal case', () => {
    assert.equal(mustBePublished('Lyntai.Providers.Default', ['2.0.1']), false);
  });

  // The positive control. Without it this file would pass against a roster that had lost every entry, which
  // is precisely the shape the tool's own header records going wrong twice.
  it('carries the ids the 2026-09-15 sweeps rewrote, in their PUBLISHED spelling', () => {
    for (const id of [
      'Lyntai.Providers.OpenAiCompatible', // became `…Http` (D135)
      'Lyntai.Providers.Local', //           became `…LlamaSharp` (D138)
      'Lyntai.Providers.ExtensionsAi', //    became the namespace `Lyntai.ExtensionsAi` (D145)
    ]) assert.ok(RETIRED.includes(id), `${id} is missing from RETIRED — a rename sweep is the usual cause`);
  });

  // A namespace is not a package id, and this is the exact byte the D145 sweep wrote. Naming it outright
  // means a re-introduction fails on the name rather than on a count.
  it('holds no NAMESPACE that was never a published package', () => {
    assert.ok(!RETIRED.includes('Lyntai.ExtensionsAi'));
  });
});
