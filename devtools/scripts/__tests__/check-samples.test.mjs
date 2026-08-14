// check-samples — the gate that COMPILES the fenced C# in our own documentation.
// See devtools/scripts/check-samples.mjs.
//
// Every test here drives the gate through an injected `compile` seam, so no test spawns dotnet: the
// interesting behaviour is entirely in how blocks are found, wrapped, batched and attributed, and a suite
// that needed a real build would be too slow to run in `verify`'s first step.
//
// FOUR of these are regression tests for defects measured on this gate's own first runs (2026-08-11), each
// of which made it report a clean or near-clean repository over code it had not checked. They are marked
// `defect N` and each was mutation-checked: revert that specific line of check-samples.mjs and that test
// fails.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { rmSync } from 'node:fs';
import { join } from 'node:path';

import {
  IN_SCOPE, PREAMBLE, SHAPES, annotationsAbove, attemptsFor, checkSamples, extractBlocks, guessShape,
  libraryNamespaces, projectFile, shadowsLibrary, synthesize,
} from '../check-samples.mjs';
import { git, makeRepo, recorder, removeTree } from './_fixtures.mjs';

const fence = (code) => '```csharp\n' + code + '\n```\n';

/**
 * Drive the gate over in-memory documents with a scripted compiler.
 *
 * `errorsFor(name, round, source)` returns the errors that batch should report for one generated file, so
 * a test can make the fake compiler behave like Roslyn — including its masking, which is the whole point
 * of the fixed-point loop.
 */
function run(docs, errorsFor = () => [], options = {}) {
  const log = recorder();
  const rounds = [];
  const compile = (files) => {
    const round = rounds.length;
    rounds.push([...files.keys()]);
    const byFile = new Map();
    for (const [name, source] of files) {
      const errors = errorsFor(name, round, source);
      if (errors.length) byFile.set(name, errors);
    }
    return { byFile, harness: null };
  };
  const code = checkSamples('/repo', {
    log,
    files: Object.keys(docs),
    read: (f) => docs[f],
    compile,
    ...options,
  });
  return { code, out: log.text(), rounds };
}

describe('check-samples — finding the blocks', () => {
  it('finds a fenced csharp block and reports the FENCE line', () => {
    const { out } = run({ 'README.md': 'intro\n\n' + fence('services.AddLyntai(x);') },
      (name) => [{ line: 1, col: 1, code: 'CS0103', message: 'boom' }]);
    assert.match(out, /README\.md:3 /, 'the fence is on line 3');
  });

  it('defect 1: a ````markdown block QUOTING a ```csharp block yields no sample', () => {
    // CommonMark: a closing fence must be at least as long as its opener. Matching a bare three read the
    // four-backtick opener's info string as "`markdown", failed to recognise the four-backtick CLOSER, and
    // left the scanner "inside" a block for the rest of the file — losing one real block and silently
    // skipping every line after it. Measured on docs/2026-08-04-generation-platform-plan.md.
    // TWO quoted blocks, so the CLOSER's length is load-bearing as well as the opener's: accept any
    // three-backtick line as a closer and the first quoted block's `` ``` `` ends the markdown block,
    // after which the SECOND quoted block is extracted as a real, unfixable phantom sample.
    const doc = '# Doc\n\n````markdown\nQuote two samples:\n\n```csharp\nnot a sample;\n```\n\n'
      + '```csharp\nalso not a sample;\n```\n````\n\n' + fence('services.AddLyntai(x);');
    const blocks = extractBlocks(doc, 'README.md');
    assert.equal(blocks.length, 1, 'only the block OUTSIDE the markdown quote is a sample');
    assert.match(blocks[0].body.join('\n'), /AddLyntai/);
    // …and the scanner is still live afterwards: a block after the four-backtick close is found.
    assert.equal(blocks[0].line, 15);
  });

  it('an indented fence is found and dedented', () => {
    const blocks = extractBlocks('- item:\n  ```csharp\n  services.AddLyntai(x);\n  ```\n', 'x.md');
    assert.equal(blocks.length, 1);
    assert.deepEqual(blocks[0].body, ['services.AddLyntai(x);']);
  });

  it('a non-csharp fence is not a sample', () => {
    assert.equal(extractBlocks('```bash\nnode x\n```\n', 'x.md').length, 0);
  });
});

describe('check-samples — the compile-skip escapes', () => {
  it('a per-block skip takes the block out and carries its reason', () => {
    const doc = '<!-- compile-skip: pseudo-code -->\n' + fence('nonsense');
    const blocks = extractBlocks(doc, 'README.md');
    assert.equal(blocks[0].skip, 'pseudo-code');
    assert.equal(blocks[0].skipScope, 'block');
  });

  it('the annotation still counts across a blank line', () => {
    const doc = '<!-- compile-skip: pseudo-code -->\n\n' + fence('nonsense');
    assert.equal(extractBlocks(doc, 'README.md')[0].skip, 'pseudo-code');
  });

  it('an UNANNOTATED block is compiled — the default is ON', () => {
    // The inversion is the whole design: opt-in coverage is whatever someone remembered to tag.
    const blocks = extractBlocks(fence('services.AddLyntai(x);'), 'README.md');
    assert.equal(blocks[0].skip, null);
  });

  it('defect 2: a file-level marker that WRAPS still applies to every block in the file', () => {
    // `(.+?)` stops at the newline, so a wrapped reason matched nothing: the marker was there, the
    // document was scanned anyway, and the only symptom was failures in a document that had opted out.
    const doc = '<!-- compile-skip-file: this plan is a record of its own day,\n'
      + '     superseded twice over -->\n\n' + fence('a') + '\n' + fence('b');
    const blocks = extractBlocks(doc, 'docs/plan.md');
    assert.equal(blocks.length, 2);
    for (const b of blocks) {
      assert.match(b.skip, /record of its own day/);
      assert.equal(b.skipScope, 'file');
    }
  });

  it('reports the two skip SCOPES apart, so a wholesale opt-out cannot hide inside per-block ones', () => {
    const { out } = run({
      'README.md': '<!-- compile-skip: mine -->\n' + fence('a'),
      'docs/plan.md': '<!-- compile-skip-file: record -->\n' + fence('b') + '\n' + fence('c'),
      'docs/live.md': fence('services.AddLyntai(x);'),
    });
    assert.match(out, /1 block skipped with a reason/);
    assert.match(out, /2 in 1 document opted out wholesale/);
  });
});

describe('check-samples — compile-given, the context a fragment assumes', () => {
  it('is picked up before the fence, wrapping across lines like the other annotations', () => {
    const doc = '<!-- compile-given: string apiKey;\n     void Save(byte[] d) { } -->\n' + fence('Save(null!);');
    const [block] = extractBlocks(doc, 'README.md');
    assert.match(block.given, /string apiKey;/);
    assert.match(block.given, /void Save\(byte\[\] d\) \{ \}/);
    assert.equal(block.skip, null, 'a given does NOT take the block out — that is the whole point');
    assert.equal(block.givenLine, 1, 'reported at the annotation, for errors inside it');
  });

  it('injects the declarations at the block\'s own scope, and says where they landed', () => {
    const { source, givenRange, bodyOffset } = synthesize(['Save(x);'], 'statement', '1', 'string apiKey;');
    const lines = source.split('\n');
    assert.equal(lines[givenRange[0]], '    string apiKey;');
    assert.ok(givenRange[1] <= bodyOffset, 'the given sits inside the header, before the body');
    // Class scope, not method scope: it is the only one of the two where a reader-side TYPE can be declared.
    const withType = synthesize(['new MyThing();'], 'statement', '1', 'sealed class MyThing { }').source;
    assert.match(withType, /\n {4}sealed class MyThing \{ \}/);
    assert.ok(withType.indexOf('sealed class MyThing') < withType.indexOf('RunAsync'),
      'declared on the class, so the method body can name it');
  });

  it('puts the given at NAMESPACE scope for a declaration block', () => {
    const { source } = synthesize(['sealed class Mine : Yours { }'], 'declaration', '1', 'class Yours { }');
    assert.ok(source.indexOf('class Yours { }') < source.indexOf('sealed class Mine'));
    assert.doesNotMatch(source, /internal abstract class Sample1/, 'no wrapper class at namespace scope');
  });

  it('THE property: an error INSIDE the given is reported, and against the annotation', () => {
    // This is what makes compile-given stronger than a skip. The declarations are compiled, so
    // `MyMadeUpType x;` fails exactly like any other unresolved name — an author cannot use this to wave a
    // sample through, only to supply a context that genuinely type-checks.
    const docs = { 'README.md': 'x\n<!-- compile-given: MyMadeUpType key; -->\n' + fence('Use(key);') };
    const compile = (files) => {
      const byFile = new Map();
      for (const [name, source] of files) {
        const at = source.split('\n').findIndex((l) => l.includes('MyMadeUpType'));
        if (at >= 0) byFile.set(name, [{ line: at + 1, col: 5, code: 'CS0246', message: "'MyMadeUpType' not found" }]);
      }
      return { byFile, harness: null };
    };
    const log = recorder();
    assert.equal(checkSamples('/repo', { log, files: Object.keys(docs), read: (f) => docs[f], compile }), 1);
    const out = log.text();
    assert.match(out, /README\.md:2 {2}error CS0246/, 'at the compile-given, not at a line of the sample');
    assert.match(out, /in its compile-given/);
  });

  it('a given block that compiles is COUNTED, so the assumption stays visible', () => {
    const { code, out } = run({ 'README.md': '<!-- compile-given: string apiKey; -->\n' + fence('Use(apiKey);') });
    assert.equal(code, 0);
    assert.match(out, /1 compiled over a compile-given context/);
  });

  it('compile-skip AND compile-given on one block is an ERROR, not a precedence question', () => {
    // They state opposite intents; resolving by precedence would leave the loser unread, and the loser
    // would be the compile-given, whose entire value is that it stays compiled.
    const doc = '<!-- compile-skip: pseudo-code -->\n<!-- compile-given: string apiKey; -->\n' + fence('Use(apiKey);');
    const [block] = extractBlocks(doc, 'README.md');
    assert.equal(block.conflict, true);

    const { code, out, rounds } = run({ 'README.md': doc });
    assert.equal(code, 1);
    assert.equal(rounds.length, 0, 'reported BEFORE anything is compiled');
    // "carries" for one, "carry" for several — the fixture here has exactly one conflicting block
    assert.match(out, /carr(?:ies|y) BOTH compile-skip and compile-given/);
    assert.match(out, /README\.md:3/, 'at the fence');
  });

  it('malformed given text FAILS the block rather than being ignored', () => {
    const docs = { 'README.md': '<!-- compile-given: this is not C# at all -->\n' + fence('Use(x);') };
    const compile = (files) => {
      const byFile = new Map();
      for (const [name, source] of files) {
        const at = source.split('\n').findIndex((l) => l.includes('not C# at all'));
        if (at >= 0) byFile.set(name, [{ line: at + 1, col: 5, code: 'CS1519', message: 'invalid token' }]);
      }
      return { byFile, harness: null };
    };
    const log = recorder();
    assert.equal(checkSamples('/repo', { log, files: Object.keys(docs), read: (f) => docs[f], compile }), 1,
      'a given that does not parse is a reported failure, never a silent skip');
    assert.match(log.text(), /in its compile-given/);
  });
});

describe('check-samples — reading the annotations above a fence', () => {
  it('collects the contiguous comment run, across blank lines', () => {
    const lines = ['# Doc', '<!-- compile-skip: a -->', '', '<!-- compile-given: b -->', '', '```csharp'];
    const { text, line } = annotationsAbove(lines, 5);
    assert.match(text, /compile-skip: a/);
    assert.match(text, /compile-given: b/);
    assert.equal(line, 2, 'the run starts at the first comment line');
  });

  it('stops at prose, so an EARLIER fence\'s annotation is never picked up', () => {
    // The walk is what makes this safe; a fixed-window regex scan would reach back over the paragraph.
    const lines = ['<!-- compile-given: string stolen; -->', '```csharp', 'a;', '```', '',
      'Some prose in between.', '', '```csharp'];
    const { text } = annotationsAbove(lines, 7);
    assert.equal(text, '', 'nothing above this fence but prose');
  });
});

describe('check-samples — what is deliberately NOT scanned', () => {
  it('skips the historical records, which are accurate BY being of their own day', () => {
    const { out, rounds } = run({ 'docs/task-archive.md': fence('old();') });
    assert.equal(rounds.length, 0, 'nothing was compiled');
    assert.match(out, /no documented C# samples/);
  });

  it('skips a CHANGELOG sample under a RELEASED heading, and compiles one under ## Unreleased', () => {
    // The boundary is shared with check-docs on purpose (TASKS.md Part 53): `## Unreleased` describes
    // behaviour that has not shipped, so a sample there is a promise a consumer will copy, while a sample
    // under a released heading is the record of what that release said. Answering this question differently
    // in the two gates is how the permissive copy goes unnoticed.
    const released = run({ 'CHANGELOG.md': `# Changelog\n\n## 2.5.0 — 2026-08-08\n\n${fence('old();')}` });
    assert.equal(released.rounds.length, 0, 'nothing below the boundary is compiled');

    const unreleased = run({ 'CHANGELOG.md': `# Changelog\n\n## Unreleased\n\n${fence('New();')}\n\n`
      + `## 2.5.0 — 2026-08-08\n\n${fence('old();')}` });
    assert.equal(unreleased.rounds.length, 1, 'the live prefix IS compiled');
    assert.equal(unreleased.rounds[0].length, 1, 'and only the block above the boundary');
  });

  it('skips a document declaring itself SUPERSEDED in its opening banner', () => {
    const { out } = run({ 'docs/old.md': '> **SUPERSEDED by the 3.0 design**\n\n' + fence('old();') });
    assert.match(out, /1 superseded record not scanned|no documented C# samples/);
  });

  it('scope matches check-docs — it is the same question about the same prose', () => {
    for (const p of ['README.md', 'docs/x.md', '.claude/knowledge/y.md']) assert.equal(IN_SCOPE(p), true, p);
    for (const p of ['src/Lyntai.Core/README.md', 'tests/notes.md']) assert.equal(IN_SCOPE(p), false, p);
  });
});

describe('check-samples — choosing a shape', () => {
  it('reads a type declaration as declaration and a call as statement', () => {
    assert.equal(guessShape(['public sealed class Foo : IBar']), 'declaration');
    assert.equal(guessShape(['namespace Contoso;']), 'declaration');
    assert.equal(guessShape(['[Fact]', 'public void X() { }']), 'declaration');
    assert.equal(guessShape(['services.AddLyntai(x);']), 'statement');
    assert.equal(guessShape(['var x = 1;']), 'statement');
  });

  it('looks past leading comments and using directives', () => {
    assert.equal(guessShape(['// a note', 'using Lyntai;', '', 'public sealed class Foo']), 'declaration');
    assert.equal(guessShape(['// a note', 'using Lyntai;', '', 'services.AddLyntai(x);']), 'statement');
  });

  it('every shape is attempted exactly once, guess first', () => {
    const attempts = attemptsFor(['services.AddLyntai(x);']);
    assert.equal(attempts[0], 'statement');
    assert.deepEqual([...attempts].sort(), [...SHAPES].sort());
  });
});

describe('check-samples — wrapping a block', () => {
  it('defect 3: hoists a using directive that carries a TRAILING COMMENT', () => {
    // Every import in the migration guide's worked before/after has one. Left in the method body,
    // `using Lyntai;` parses as a using STATEMENT missing its resource — so the guide's own showcase
    // samples failed on CS1001s that had nothing to do with the sample.
    const { source } = synthesize(['using Lyntai;   // the builder', 'services.AddLyntai(x);'], 'statement', '1');
    assert.match(source, /^using Lyntai;$/m, 'hoisted, with the doc-facing comment dropped');
    assert.doesNotMatch(source, /using Lyntai;\s+\/\/ the builder/);
  });

  it('does NOT mistake `using var` or `using (…)` for a directive', () => {
    for (const line of ['using var mcp = await X.CreateAsync();', 'using (var s = Open()) { }']) {
      const { source } = synthesize([line], 'statement', '1');
      assert.match(source, new RegExp(line.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')), `kept in place: ${line}`);
    }
  });

  it('blanks a hoisted using IN PLACE, so the reported doc line stays exact', () => {
    // The report's whole value is `file:line`. Removing the line instead would shift every line after it
    // by one, and an off-by-one report points at the wrong line of the right sample.
    const body = ['using Lyntai;', 'services.AddLyntai(x);'];
    const { source, bodyOffset } = synthesize(body, 'statement', '1');
    const lines = source.split('\n');
    assert.equal(lines[bodyOffset], '', 'the using line is blank, not gone');
    assert.equal(lines[bodyOffset + 1], 'services.AddLyntai(x);');
  });

  it('declares the preamble as class MEMBERS, so a sample may declare its own local of the same name', () => {
    const { source } = synthesize(['var services = Build();'], 'statement', '1');
    for (const [type, name] of PREAMBLE) assert.match(source, new RegExp(`\\n    ${type.replace(/\./g, '\\.')} ${name} =>`));
    assert.match(source, /\n {4}Microsoft\.Extensions\.DependencyInjection\.IServiceCollection services =>/,
      'a member, which a local legally shadows — a local would collide (CS0128)');
    assert.doesNotMatch(source, /internal static class/,
      'and NOT a static class, so a compile-given can be written as ordinary instance C#');
  });

  it('wraps a bare expression, with the closing paren on its own line', () => {
    // A trailing `// comment` on the last line would swallow the paren otherwise.
    const { source } = synthesize(['new GraphMemoryOptions { X = 1 }   // was: …'], 'expression', '1');
    assert.match(source, /_ = \(/);
    assert.match(source, /\n\s*\);/);
  });

  it('does not add a namespace to a block that declares its own', () => {
    const { source } = synthesize(['namespace Contoso.App;', 'class X { }'], 'declaration', '1');
    assert.equal((source.match(/^namespace /gm) ?? []).length, 1);
  });

  it('gives each block its own namespace, so two samples may declare the same type name', () => {
    const a = synthesize(['class Widget { }'], 'declaration', '0001').source;
    const b = synthesize(['class Widget { }'], 'declaration', '0002').source;
    assert.notEqual(a.match(/^namespace (.+);$/m)[1], b.match(/^namespace (.+);$/m)[1]);
  });
});

describe('check-samples — isolating a block that redefines library surface', () => {
  it('defect 4: a block declaring `namespace Lyntai.…` is compiled ALONE', () => {
    // CS0436 is a WARNING: a type declared in source outranks the same type from a referenced assembly for
    // the whole compilation. Batched, one such sample silently redefines `GenerationRequest` for every
    // other sample, which then verifies against the doc's type instead of the shipped one.
    assert.equal(shadowsLibrary(['namespace Lyntai.Generation;', 'record GenerationRequest;']), true);
    assert.equal(shadowsLibrary(['namespace Contoso.App;']), false);
    assert.equal(shadowsLibrary(['services.AddLyntai(x);']), false);

    const { rounds } = run({
      'docs/a.md': fence('services.AddLyntai(x);') + '\n' + fence('namespace Lyntai.Generation;\nclass R { }'),
    });
    assert.equal(rounds.length, 2, 'two compilations: the shared batch, then the shadowing block alone');
    assert.deepEqual(rounds[0], ['Block_0001.cs']);
    assert.deepEqual(rounds[1], ['Block_0002.cs']);
  });
});

describe('check-samples — the fixed-point loop', () => {
  it('defect 5: a batch carrying an error certifies NOTHING — the survivors are recompiled', () => {
    // THE load-bearing one. Roslyn suppresses all semantic analysis for a compilation with a syntax error,
    // so the first real run reported 132 errors, every one syntactic, zero CS0246 — and eleven blocks
    // naming types that do not exist here passed as "compiles". A verdict is only taken from a
    // compilation in which nothing at all errored.
    //
    // The fake compiler below behaves exactly that way: while block 1 is present it reports ONLY block 1's
    // syntax error; once block 1 is gone, block 2's binding error finally surfaces.
    const docs = {
      'docs/a.md': fence('!!! unparseable') + '\n' + fence('NoSuchType.Call();'),
    };
    const log = recorder();
    const compile = (fileMap) => {
      const byFile = new Map();
      if (fileMap.has('Block_0001.cs'))
        byFile.set('Block_0001.cs', [{ line: 1, col: 1, code: 'CS1002', message: '; expected' }]);
      else if (fileMap.has('Block_0002.cs'))
        byFile.set('Block_0002.cs', [{ line: 1, col: 1, code: 'CS0103', message: "'NoSuchType' does not exist" }]);
      return { byFile, harness: null };
    };
    const code = checkSamples('/repo', { log, files: Object.keys(docs), read: (f) => docs[f], compile });

    assert.equal(code, 1);
    assert.match(log.text(), /2 documented samples do not compile/,
      'the masked block must NOT be certified by the batch that never bound it');
    assert.match(log.text(), /CS0103/, "…and its real error is the one reported once it stops being masked");
  });

  it('a block that fails in the guessed shape but compiles in another is not reported', () => {
    // `public MemoryRetrievabilityProvenance Provenance => …;` guesses `declaration` and is a `member`.
    const docs = { 'docs/a.md': fence('public int Provenance => 1;') };
    const compile = (files) => {
      const byFile = new Map();
      for (const [name, source] of files) {
        if (!/internal abstract class/.test(source))          // anything but the `member` shape fails
          byFile.set(name, [{ line: 1, col: 1, code: 'CS0116', message: 'wrong scope' }]);
      }
      return { byFile, harness: null };
    };
    const log = recorder();
    const code = checkSamples('/repo', { log, files: Object.keys(docs), read: (f) => docs[f], compile });
    assert.equal(code, 0, 'the member shape compiles it, so nothing is reported');
    assert.match(log.text(), /1\/1 documented C# sample\(s\) compile/);
  });

  it('a block that compiles in NO shape is reported once, with the doc line of its error', () => {
    const docs = { 'README.md': 'x\n\n' + fence('line one;\nline two;') };
    const compile = (files) => {
      const byFile = new Map();
      for (const [name, source] of files) {
        // Point at the SECOND body line, whatever the wrapper put above it.
        const offset = source.split('\n').findIndex((l) => l === 'line one;');
        byFile.set(name, [{ line: offset + 2, col: 5, code: 'CS0103', message: "'two' does not exist" }]);
      }
      return { byFile, harness: null };
    };
    const log = recorder();
    assert.equal(checkSamples('/repo', { log, files: Object.keys(docs), read: (f) => docs[f], compile }), 1);
    const out = log.text();
    assert.match(out, /README\.md:3 {2}\(fenced sample/, 'the fence');
    assert.match(out, /README\.md:5 {2}error CS0103/, 'the offending line INSIDE the sample');
    assert.equal((out.match(/error CS0103/g) ?? []).length, 1, 'reported once, not once per shape tried');
  });

  it('passes, and says so, when every sample compiles', () => {
    const { code, out } = run({ 'README.md': fence('services.AddLyntai(x);') });
    assert.equal(code, 0);
    assert.match(out, /1\/1 documented C# sample\(s\) compile ✓/);
  });
});

describe('check-samples — telling a broken HARNESS from a broken sample', () => {
  it('exits 2 and blames itself when the build failed with nothing attributable', () => {
    const log = recorder();
    const code = checkSamples('/repo', {
      log,
      files: ['README.md'],
      read: () => fence('services.AddLyntai(x);'),
      compile: () => ({ byFile: new Map(), harness: 'error NU1101: package not found' }),
    });
    assert.equal(code, 2, 'not 1 — nothing is known about the samples');
    assert.match(log.text(), /HARNESS, not a sample/);
    assert.match(log.text(), /NU1101/);
  });
});

describe('check-samples — the scratch project', () => {
  it('references the real projects and opens the library namespaces', () => {
    const xml = projectFile('/repo', ['/repo/src/Lyntai.Core/Lyntai.Core.csproj'], ['Lyntai.Memory']);
    assert.match(xml, /<ProjectReference Include="\/repo\/src\/Lyntai\.Core\/Lyntai\.Core\.csproj" \/>/);
    assert.match(xml, /<Using Include="Lyntai\.Memory" \/>/);
    assert.match(xml, /<ManagePackageVersionsCentrally>false<\/ManagePackageVersionsCentrally>/,
      'it sits under the repo root Directory.Packages.props and must not inherit central versioning');
  });
});

describe('check-samples — deriving the library namespaces from the tree', () => {
  /**
   * Takes the test context so the fixture is REMOVED when the test ends — `_fixtures.mjs` asks every caller
   * to do that and this helper was the one place that did not, across both its call sites. Each leak is a
   * full `.git` in the gitignored scratch area nobody looks at, so it accumulated unbounded: measured
   * 2026-08-14 at 190 directories, growing by exactly 2 per `test-devtools` run, which is this helper's
   * call-site count. Harmless to correctness and invisible for the same reason — which is why it needed a
   * measurement rather than a reading.
   */
  const repoWithSources = (t) => {
    const repo = makeRepo({
      'src/Lyntai.Core/Thing.cs': 'namespace Lyntai.Alpha;\npublic sealed class Thing { }\n',
      'src/Lyntai.Storage.Sqlite/Migrations/M1_Old.cs': 'namespace Lyntai.Beta;\npublic sealed class Old { }\n',
    });
    t.after(() => removeTree(repo));
    git(repo, ['add', '-A']);
    git(repo, ['commit', '-qm', 'sources']);
    return repo;
  };

  it('collects one namespace per source file', (t) => {
    assert.deepEqual(libraryNamespaces(repoWithSources(t)), ['Lyntai.Alpha', 'Lyntai.Beta']);
  });

  /**
   * The measured crash: `git ls-files` reports what is TRACKED, so a migration deleted from the working tree
   * but not yet staged is still listed, and reading it threw an unhandled ENOENT that took down the whole
   * gate. Hit for real on 2026-08-12 squashing the six unreleased memory migrations.
   *
   * Skipping is the correct direction here and the assertion below is deliberately two-sided about it: the
   * surviving file's namespace must STILL be collected, so this pins "skip the missing one" rather than the
   * cheaper "do not throw", which a bare `return []` would also satisfy.
   */
  it('skips a tracked file already deleted from the working tree, keeping the rest', (t) => {
    const repo = repoWithSources(t);
    rmSync(join(repo, 'src/Lyntai.Storage.Sqlite/Migrations/M1_Old.cs'));

    assert.deepEqual(libraryNamespaces(repo), ['Lyntai.Alpha']);
  });
});

describe('check-samples — the --list inventory', () => {
  it('reports what it WOULD compile without compiling anything', () => {
    const { code, out, rounds } = run({ 'README.md': fence('services.AddLyntai(x);') }, () => [], { list: true });
    assert.equal(code, 0);
    assert.equal(rounds.length, 0, 'nothing was compiled');
    assert.match(out, /1 block in 1 document would compile/);
    assert.match(out, /README\.md:1 {2}statement/);
  });
});
