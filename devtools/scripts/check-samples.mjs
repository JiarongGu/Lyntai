// check-samples — fail when a fenced C# block in our own documentation does not compile.
//
// A sample that cannot compile is a false promise in the document a consumer copies from. DEFAULT-ON: an
// opt-in marker makes coverage whatever someone remembered to tag. Why, and what it cost: `docs/GATES.md`
// §check-samples.
//
// Two annotations, and reach for the first: `<!-- compile-given: <declarations> -->` supplies the context
// a fragment assumes and keeps the block COMPILED (the declarations compile too, so it cannot wave a
// sample through); `<!-- compile-skip: <reason> -->` takes the block out. Both on one block is an ERROR.
// `<!-- compile-skip-file: … -->` opts out a whole historical document.
//
// Each block is wrapped by SHAPE — declaration (namespace scope), member (a class body), statement (a
// method over the stub PREAMBLE), expression (`_ = ( … );`) — guessed, then retried until one compiles.
// Two rules keep it honest (`.claude/knowledge/pitfalls.md`): Roslyn binds NOTHING in a compilation
// carrying a syntax error, so a block passes only in a compilation where nothing errored; and a type
// declared in source outranks the same type from a reference, so a block declaring a namespace under the
// library root compiles ISOLATED.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { IN_SCOPE, IS_SCANNED, LIVE_PREFIX, SUPERSEDED_BANNER, liveLineMask } from './check-docs.mjs';
import { checkQuotedBaseline } from './_baseline.mjs';
import { readRepoText, repoFiles } from './_repo-files.mjs';

const here = fileURLToPath(import.meta.url);
const repoDefault = join(dirname(here), '..', '..');

export { IN_SCOPE };

/** Where the synthesized project lives — gitignored scratch INSIDE the repo, never OS temp. */
export const SCRATCH = join('devtools', '_samples');

/**
 * The locals a documented fragment is allowed to assume already exist, and their real types.
 *
 * ONE RULE decides membership, and it is what keeps this from growing into "whatever the last failing
 * sample needed": a name belongs here only if its type is a service THIS LIBRARY provides and the
 * surrounding prose tells the reader to inject ("inject `IToolLoop`", "then inject the front door"). A
 * reader-supplied VALUE (`apiKey`, `cwd`, `payloadJson`) or a reader-supplied TYPE (`MyCustomProvider`)
 * never belongs here — that block is genuinely un-standalone and takes an honest `compile-skip`. Widening
 * this list is how a wrapper stops gating: every name added is a name no sample has to justify again.
 */
export const PREAMBLE = [
  ['Microsoft.Extensions.DependencyInjection.IServiceCollection', 'services'],
  ['System.IServiceProvider', 'serviceProvider'],
  ['System.IServiceProvider', 'sp'],
  ['Lyntai.LyntaiBuilder', 'cfg'],
  ['Lyntai.LyntaiBuilder', 'b'],
  ['Lyntai.LyntaiBuilder', 'builder'],
  ['Lyntai.Inference.ITextClient', 'llm'],
  ['Lyntai.Inference.IModelProvider', 'provider'],
  ['Lyntai.Jobs.IJobQueue', 'queue'],
  ['Lyntai.Jobs.IJobRunner', 'runner'],
  ['Lyntai.Jobs.IJobScheduler', 'scheduler'],
  ['Lyntai.Memory.IMemoryEngine', 'memory'],
  ['Lyntai.Memory.IMemoryEngine', 'engine'],
  ['Lyntai.Agents.IToolLoop', 'toolLoop'],
  ['Lyntai.Inference.IMediaRouter', 'router'],
  ['Microsoft.Extensions.AI.IChatClient', 'chat'],
];

/** Value-typed stubs need `default`, not `null!`. */
const PREAMBLE_VALUE = [['System.Threading.CancellationToken', 'ct']];

export const SHAPES = ['declaration', 'member', 'statement', 'expression'];

/** After the guess, the order the rest are retried in — nearest shape first. */
const RETRY_ORDER = {
  declaration: ['member', 'statement', 'expression'],
  member: ['declaration', 'statement', 'expression'],
  statement: ['expression', 'member', 'declaration'],
  expression: ['statement', 'member', 'declaration'],
};

// `[\s\S]` not `.` throughout: an annotation is long enough to wrap, and a `.` that stops at the newline
// silently matched nothing — the marker was present, the document was scanned anyway, and the only symptom
// was failures in a document that had explicitly opted out.
const SKIP_BLOCK = /<!--\s*compile-skip:\s*([\s\S]+?)\s*-->/;

// A DECLARATION, never a MENTION — the marker must OPEN a line (`m` + `^`), which a backticked reference
// inside a sentence never does. Found 2026-08-15: this was matched against the whole document with no such
// requirement, so CLAUDE.md, which DOCUMENTS the annotation ("`<!-- compile-skip-file: … -->` opts out a
// historical document"), opted ITSELF out of the gate. Silently, and worse than the usual silent: skippedDocs
// is populated from BLOCKS, so a document with no fences contributes nothing to the "opted out wholesale"
// count and nothing anywhere reports it. Latent only while CLAUDE.md has no csharp fences; the first one
// added would have been uncompiled and unmentioned.
//
// Identical defect and identical fix to check-docs' SUPERSEDED_BANNER, tightened 2026-08-11 for exactly
// this reason. A per-BLOCK skip needs no such anchor: it is read from the annotation lines immediately above
// one fence (annotationsAbove), so prose elsewhere in the document cannot reach it.
const SKIP_FILE = /^<!--\s*compile-skip-file:\s*([\s\S]+?)\s*-->/m;

/**
 * `<!-- compile-given: <C# declarations> -->` — the reader-side context a fragment assumes.
 *
 * STRICTLY STRONGER THAN A SKIP, and the reason is that the declarations are THEMSELVES COMPILED, in the
 * same compilation as the sample. `compile-given: MyMadeUpType x;` fails with CS0246 exactly like any other
 * unresolved name. So this cannot wave a sample through — it can only supply a context that genuinely
 * type-checks, while the sample itself still has to bind and typecheck against the real assemblies.
 *
 * It exists because the single largest category of un-compilable sample is not a wrong sample: it is a
 * two-line fragment naming something the READER supplies (`apiKey`, `cwd`, `myChatClient`,
 * `MyCustomProvider`). Twenty README blocks were in that category. The alternative of widening the shared
 * stub preamble was rejected: a preamble that silently declares arbitrary reader-side names starts
 * accepting samples that reference things this library never provides, which is the same class of
 * false-pass as the two gate bugs in the header.
 */
const GIVEN_BLOCK = /<!--\s*compile-given:\s*([\s\S]+?)\s*-->/;

/**
 * The contiguous run of HTML comments immediately above a fence — the annotations that govern that block.
 *
 * Walked rather than regex-scanned over a fixed window, so an annotation belonging to an EARLIER fence can
 * never be picked up by a later one: the walk stops at the first line that is neither blank nor part of a
 * comment. Returns `{ text, line }`, `line` being the 1-based document line the run starts on, which is
 * where an error inside a `compile-given` is reported.
 */
export function annotationsAbove(lines, fenceIndex) {
  let i = fenceIndex - 1;
  const collected = [];
  let firstLine = fenceIndex;
  for (;;) {
    while (i >= 0 && lines[i].trim() === '') i--;
    if (i < 0 || !lines[i].trimEnd().endsWith('-->')) break;
    let start = i;
    while (start >= 0 && !lines[start].includes('<!--')) start--;
    if (start < 0) break;
    collected.unshift(lines.slice(start, i + 1).join('\n'));
    firstLine = start;
    i = start - 1;
  }
  return { text: collected.join('\n'), line: firstLine + 1 };
}

/**
 * A `using` DIRECTIVE — never `using var x = …` or `using (…)`, which carry an `=` or a paren.
 *
 * The trailing-comment arm is load-bearing, not tidiness: every import in the migration guide's worked
 * before/after carries one (`using Lyntai.Memory;  // GraphMemoryOptions — STAYS`), and without it the
 * line stayed in the method body, where `using Lyntai;` parses as a using STATEMENT missing its resource.
 * That turned the guide's own showcase samples into CS1001s that had nothing to do with the sample.
 */
const USING_DIRECTIVE = /^\s*(?:global\s+)?using\s+(?:static\s+)?[A-Za-z_][\w.]*\s*;\s*(?:\/\/.*)?$/;

const DECLARATION_START =
  /^\s*(?:\[|namespace\b|public\b|internal\b|protected\b|private\b|file\b|sealed\b|abstract\b|partial\b|class\b|record\b|struct\b|interface\b|enum\b|delegate\b)/;

/**
 * Every fenced ```csharp block in one document, with the `compile-skip` annotation that governs it.
 *
 * Fences inside another fenced block (a ```markdown sample that quotes one) are NOT blocks — the scanner
 * tracks the enclosing fence, or a quoted example becomes a phantom sample nobody can fix.
 *
 * FENCE LENGTH IS LOAD-BEARING, per CommonMark: a closing fence must be at least as long as its opener, so
 * a ````markdown block can quote ```csharp blocks whole. Matching a bare three did both halves wrong here —
 * it read the four-backtick opener's info string as `` `markdown ``, then failed to recognise the
 * four-backtick CLOSER, leaving the scanner "inside" a block for the rest of the file. Measured
 * 2026-08-11 on the 2026-08-04 generation plan (`local/` since D149), where it lost one real block and silently <!-- link-ok: names where a measurement was taken, not a live path -->
 *
 * stopped scanning the remaining 140 lines.
 */
export function extractBlocks(text, file) {
  const lines = text.split(/\r?\n/);
  const fileSkip = (text.match(SKIP_FILE) ?? [])[1] ?? null;
  const blocks = [];

  let open = null;   // { indent, ticks, lang, start }
  for (let i = 0; i < lines.length; i++) {
    const fence = lines[i].match(/^(\s*)(`{3,})\s*(\S*)\s*$/);
    if (!fence) continue;
    const [, indent, ticks, lang] = fence;

    if (open) {
      // A closer is at least as long as its opener and carries no info string.
      if (ticks.length < open.ticks || lang !== '') continue;
      if (open.lang === 'csharp') {
        const body = lines.slice(open.start + 1, i).map((l) => (l.startsWith(indent) ? l.slice(indent.length) : l));
        const notes = annotationsAbove(lines, open.start);
        const ownSkip = (notes.text.match(SKIP_BLOCK) ?? [])[1] ?? null;
        const given = (notes.text.match(GIVEN_BLOCK) ?? [])[1] ?? null;
        blocks.push({
          file,
          line: open.start + 1,
          body,
          skip: fileSkip ?? ownSkip,
          skipScope: fileSkip ? 'file' : 'block',
          // A block-level skip AND a given on the same block are contradictory intents ("do not compile
          // this" / "compile this, here is its context"), so this is reported as an error rather than
          // resolved by precedence. Silently letting one win leaves the other to rot unread — and the one
          // that would rot is the `compile-given`, whose whole value is that it stays compiled.
          conflict: Boolean(ownSkip && given),
          given,
          givenLine: given ? notes.line : null,
        });
      }
      open = null;
      continue;
    }
    if (lang !== '') open = { indent, ticks: ticks.length, lang, start: i };
  }
  return blocks;
}

/**
 * Does this block declare a namespace under the library's own root?
 *
 * Such a block cannot share a compilation with any other: its declarations WIN over the referenced
 * assemblies for the whole compilation (CS0436 is a warning), so every neighbouring sample would be
 * verified against the doc's copy of a type instead of the shipped one.
 */
export const shadowsLibrary = (body, root = 'Lyntai') =>
  body.some((l) => new RegExp(`^\\s*namespace\\s+${root}\\b`).test(l));

/** The shape to try FIRST, from the first line that is neither blank, a comment, nor a using directive. */
export function guessShape(body) {
  const first = body.find((l) => l.trim() && !l.trim().startsWith('//') && !l.trim().startsWith('*')
    && !l.trim().startsWith('/*') && !USING_DIRECTIVE.test(l) && !l.trim().startsWith('#'));
  if (first === undefined) return 'statement';
  return DECLARATION_START.test(first) ? 'declaration' : 'statement';
}

export const attemptsFor = (body) => { const g = guessShape(body); return [g, ...RETRY_ORDER[g]]; };

/**
 * Re-indent `compile-given` text for the generated file.
 *
 * The annotation's FIRST line starts right after `compile-given:` and so carries no indent of its own,
 * while its continuation lines are indented to sit under it inside the HTML comment. Measuring the common
 * prefix over all lines would therefore always find zero and flatten a multi-line class declaration.
 * Purely cosmetic to the compiler — it matters when a human opens the generated file to see what failed.
 */
export function reindent(given) {
  const [first, ...rest] = given.split('\n');
  const indents = rest.filter((l) => l.trim()).map((l) => l.match(/^\s*/)[0].length);
  const strip = indents.length ? Math.min(...indents) : 0;
  return [`    ${first.trim()}`, ...rest.map((l) => (l.trim() ? `    ${l.slice(strip)}` : ''))];
}

/**
 * Wrap one block's body in `shape`, returning `{ source, bodyOffset, givenRange }`.
 *
 * `bodyOffset` is how many generated lines precede the body, so a compiler error at generated line N maps
 * to doc line `fenceLine + 1 + (N - 1 - bodyOffset)`. Hoisted `using` directives are BLANKED IN PLACE
 * rather than removed, which is what keeps that mapping exact — a report that points at the wrong line of
 * the wrong sample is not usable, and off-by-one is the easy way to get there.
 *
 * `givenRange` is the half-open span of generated lines the `compile-given` declarations occupy, so an
 * error inside THEM is reported at the annotation instead of at some line of the sample — the author needs
 * to be told which of the two they got wrong.
 *
 * The wrapper class carries INSTANCE members, not static ones, purely so `compile-given` can be written as
 * ordinary C#: `IChatClient myChatClient;` rather than `static IChatClient myChatClient;`. Nothing is ever
 * constructed, so the distinction costs nothing and the shadowing property is unchanged — a field is not
 * an enclosing local scope, so a sample declaring its own `var services = …` still shadows legally.
 */
export function synthesize(body, shape, id, given = null) {
  const usings = [];
  const kept = body.map((l) => {
    if (!USING_DIRECTIVE.test(l)) return l;
    usings.push(l.trim().replace(/\s*\/\/.*$/, ''));   // the comment is the doc's, not the compiler's
    return '';
  });
  const ns = `Lyntai.DocSamples.S${id}`;
  const declaresNamespace = kept.some((l) => /^\s*namespace\b/.test(l));
  const givenLines = given ? reindent(given) : [];

  const head = [...usings];
  if (!declaresNamespace) head.push(`namespace ${ns};`);
  let tail = [];
  let givenRange = null;

  // The given declarations go at the same SCOPE the block's own content will occupy — namespace scope for
  // a declaration block, class scope for everything else. Class scope (not method scope) is deliberate: it
  // is the only one of the two where a reader-side TYPE can be declared, and half the category needs one.
  const placeGiven = () => {
    if (givenLines.length === 0) return;
    givenRange = [head.length, head.length + givenLines.length];
    head.push(...givenLines);
  };

  if (shape === 'declaration') {
    placeGiven();
  } else if (shape === 'member') {
    head.push(`internal abstract class Sample${id}`, '{');
    placeGiven();
    tail = ['}'];
  } else if (shape === 'statement' || shape === 'expression') {
    head.push(`internal abstract class Sample${id}`, '{');
    for (const [type, name] of PREAMBLE) head.push(`    ${type} ${name} => null!;`);
    for (const [type, name] of PREAMBLE_VALUE) head.push(`    ${type} ${name} => default;`);
    placeGiven();
    head.push('    async System.Threading.Tasks.Task RunAsync()', '    {');
    if (shape === 'expression') head.push('        _ = (');
    tail = shape === 'expression' ? ['', '        );', '    }', '}'] : ['    }', '}'];
  }

  return { source: [...head, ...kept, ...tail].join('\n') + '\n', bodyOffset: head.length, givenRange };
}

/**
 * Namespaces the wrapper imports for every sample: every namespace the library itself declares, plus the
 * few third-party ones a consumer's own `Program.cs` would already have open.
 *
 * WHAT THIS DELIBERATELY DOES NOT CHECK, stated plainly because it is the gate's one real limitation: a
 * sample is verified for whether its TYPES AND MEMBERS resolve and typecheck, NOT for whether it lists its
 * own imports. Most documented fragments are two lines long and the surrounding prose owns the imports;
 * requiring each to carry four `using` lines would make the documentation worse to read in exchange for
 * catching a class of error (`CS0246`) that the reader hits instantly and can fix, while the errors that
 * actually shipped — `TimeSpan.FromDays(14)` into a `double` property, a renamed member, a property that
 * moved to another options record — are exactly the ones binding catches and a reader does not.
 * A block that DOES carry its own `using` lines is still compiled with them (the migration guide's worked
 * before/after is written that way on purpose), so nothing is lost where a document chose to be explicit.
 *
 * Derived from the tree rather than listed, so a new namespace is covered the day it is added.
 */
export function libraryNamespaces(repo) {
  const found = new Set();
  // The ONE file list (`repoFiles`): a namespace in a new, unstaged file is covered the day it is written.
  for (const f of repoFiles(repo, ['src']).filter((p) => p.endsWith('.cs'))) {
    // A pending deletion contributes no namespace; a sample that needed it then FAILS, never passes.
    const text = readRepoText(repo, f);
    if (text === null) continue;
    const m = text.match(/^namespace\s+([A-Za-z_][\w.]*)\s*[;{]/m);
    if (m) found.add(m[1]);
  }
  return [...found].sort();
}

/** Third-party namespaces a consuming app has open anyway — DI is how every sample starts. */
export const CONSUMER_NAMESPACES = [
  'Microsoft.Extensions.DependencyInjection',
  'Microsoft.Extensions.AI',
  'Microsoft.Extensions.Logging',
  'ModelContextProtocol.Client',
  'System.Text.Json',
];

/** The scratch csproj: project references to the REAL projects, never packages (consumer-smoke owns those). */
export function projectFile(repo, projects, namespaces = []) {
  const refs = projects.map((p) => `    <ProjectReference Include="${p}" />`).join('\n');
  const usings = namespaces.map((n) => `    <Using Include="${n}" />`).join('\n');
  return `<Project Sdk="Microsoft.NET.Sdk">

  <!-- GENERATED by devtools/scripts/check-samples.mjs. Scratch; gitignored (devtools/_*). -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <CodePage>65001</CodePage>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS1591;CS8321;CS0219;CS1998;CS0169;CS0414;IL2026;IL3050</NoWarn>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
${refs}
  </ItemGroup>

  <ItemGroup>
${usings}
  </ItemGroup>

</Project>
`;
}

/** Every packable project, which is what a consumer can reference — the whole documented surface. */
export const srcProjects = (repo) =>
  repoFiles(repo, ['src']).filter((p) => /^src\/[^/]+\/[^/]+\.csproj$/.test(p)).map((p) => join(repo, p));

/**
 * Compile one batch. Returns a Map of generated-file basename -> [{ line, code, message }].
 *
 * `maxBuffer` is explicit: `check-warnings` once reported "build FAILED" for a build that SUCCEEDED purely
 * because the log outgrew Node's 1 MiB default (pitfalls.md §Environment/tooling). A gate that lies is
 * worse than one that misses.
 */
export function dotnetCompile(repo, dir, files) {
  const generated = join(dir, 'Generated');
  rmSync(generated, { recursive: true, force: true });
  mkdirSync(generated, { recursive: true });
  for (const [name, source] of files) writeFileSync(join(generated, name), source, 'utf8');

  const r = spawnSync('dotnet', ['build', join(dir, 'Samples.csproj'), '-v', 'q', '--nologo'],
    { cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const out = `${r.stdout ?? ''}${r.stderr ?? ''}`;

  const byFile = new Map();
  const other = [];
  for (const line of out.split(/\r?\n/)) {
    const m = line.match(/^\s*(?:\d+>)?(.+?)\((\d+),(\d+)\):\s*error\s+([A-Z]+\d+):\s*(.*?)(?:\s*\[[^\]]*\])?\s*$/);
    if (!m) {
      if (/\berror\b/i.test(line) && line.trim()) other.push(line.trim());
      continue;
    }
    const name = m[1].replace(/\\/g, '/').split('/').pop();
    if (!files.has(name)) { other.push(line.trim()); continue; }
    if (!byFile.has(name)) byFile.set(name, []);
    // MSBuild emits each diagnostic once per logger pass, so the same error arrives twice; a report that
    // prints it twice reads as two problems.
    const errors = byFile.get(name);
    const one = { line: Number(m[2]), col: Number(m[3]), code: m[4], message: m[5] };
    if (!errors.some((e) => e.line === one.line && e.col === one.col && e.code === one.code)) errors.push(one);
  }
  // A build that failed with no error attributable to any generated file is a HARNESS failure, not a
  // sample failure — report it as such rather than announcing that zero samples are broken.
  const harness = r.status !== 0 && byFile.size === 0 ? (other.slice(0, 20).join('\n  ') || out.trim().slice(-2000)) : null;
  return { byFile, harness };
}

/**
 * Errors that mean "this block is not that SHAPE" rather than "this block is wrong" — the parser's own,
 * plus the three that fire when a construct lands at the wrong nesting level.
 */
const PARSE_ERRORS = new Set([
  'CS1001', 'CS1002', 'CS1003', 'CS1004', 'CS1022', 'CS1023', 'CS1026', 'CS1031', 'CS1041', 'CS1513',
  'CS1514', 'CS1519', 'CS1520', 'CS1525', 'CS1526', 'CS1528', 'CS1529', 'CS1585', 'CS0106', 'CS0116',
  'CS0825', 'CS0839', 'CS8124', 'CS8803', 'CS8805', 'CS9007',
]);

const plural = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`;

/**
 * The gate. `compile` is a seam so the tests can drive every branch without dotnet in the loop.
 * `files` is the raw candidate list (a `git ls-files` shape) for the same reason, and `baseline` checks a
 * green run's `compiled/total` against the `doc samples N/N` CLAUDE.md quotes (`_baseline.mjs`).
 */
export function checkSamples(repo, {
  log = console.log, files = null, compile = null, read = null, list = false,
  baseline = (passed, total) => checkQuotedBaseline(repo, { gate: 'check-samples', label: 'doc samples', passed, total }, log),
} = {}) {
  const readFile = read ?? ((f) => readRepoText(repo, f));
  const source = files ?? repoFiles(repo);
  const tracked = source
    .filter((f) => f.endsWith('.md'))
    .filter(IN_SCOPE)
    .filter(IS_SCANNED);

  const blocks = [];
  const skipped = [];
  const skippedDocs = new Set();
  const conflicts = [];
  let supersededDocs = 0;
  for (const file of tracked) {
    const text = readFile(file);
    if (text == null) continue;                    // a pending deletion; any other read error throws
    if (SUPERSEDED_BANNER.test(text)) { supersededDocs++; continue; }
    // Shared with check-docs, deliberately: a partly-historical file is live down to its boundary
    // (CHANGELOG) or inside its dated amendment regions (the design record, D164), so a fenced sample
    // under `## Unreleased` is compiled, a v0.1 seed block is left as the record it is, and a fence an
    // amendment ever grows would be compiled like any other. Answering this question differently in the
    // two gates is how the permissive copy goes unnoticed — see check-docs.mjs's note on HISTORICAL.
    const mask = liveLineMask(file, text.split(/\r?\n/));
    for (const block of extractBlocks(text, file)) {
      if (mask !== null && !mask[block.line - 1]) continue;
      if (block.conflict) { conflicts.push(block); continue; }
      if (!block.skip) { blocks.push(block); continue; }
      skipped.push(block);
      if (block.skipScope === 'file') skippedDocs.add(file);
    }
  }

  // Reported BEFORE anything is compiled: an annotation that contradicts itself is an authoring mistake,
  // and resolving it by precedence would leave the losing half unread for good.
  if (conflicts.length > 0) {
    log(`check-samples: ✗ ${plural(conflicts.length, 'block')} `
      + `${conflicts.length === 1 ? 'carries' : 'carry'} BOTH compile-skip and compile-given\n`);
    for (const block of conflicts) log(`  ${block.file}:${block.line}`);
    log('\n  They say opposite things: `compile-skip` means "do not compile this", `compile-given` means');
    log('  "compile this — here is the context it assumes". Keep whichever is true and delete the other.');
    log('  If the sample can be given a context that type-checks, `compile-given` is the stronger answer.');
    return 1;
  }

  // A live PREFIX is transient BY CONSTRUCTION: the release workflow stamps `## Unreleased` with a version
  // before it runs `verify`, so a fence living there is compiled on every ordinary run and historical the
  // instant a release starts. The census moves under this gate's own feet, the CLAUDE.md baseline it checks
  // itself against goes stale, and the pipeline fails on a tree that was green minutes earlier — measured
  // 2026-09-19 on a real release run (`docs/FIXES.md`). An amendment region (the design record, D164) is
  // permanent and unaffected, which is why this keys on LIVE_PREFIX rather than on a filename.
  const transient = blocks.filter((block) => LIVE_PREFIX.some((rule) => rule.file.test(block.file)));
  if (transient.length > 0) {
    log(`check-samples: ✗ ${plural(transient.length, 'compiled sample')} in a region the RELEASE STAMP `
      + 'makes historical\n');
    for (const block of transient) log(`  ${block.file}:${block.line}`);
    log('\n  A `## Unreleased` prefix stops being live the moment the release workflow stamps it with a');
    log('  version, so this sample compiles today and vanishes from the census mid-release. Put the');
    log('  runnable recipe in a MAINTAINED document and let the entry point at it — or, when the entry is');
    log('  quoting a shape rather than teaching one, annotate it `compile-skip: <reason>`.');
    return 1;
  }

  // `--list` is the inventory without the compile. It exists so `cli-entry.test.mjs` can prove this
  // script's entry point still FIRES without paying for a build — that suite runs first in `verify`, and a
  // gate whose entry stopped matching scans nothing, prints nothing and exits 0.
  if (list) {
    const docs = new Set(blocks.map((b) => b.file));
    log(`check-samples: ${plural(blocks.length, 'block')} in ${plural(docs.size, 'document')} would compile; `
      + `${plural(skipped.length, 'block')} skipped (--list: nothing was compiled)`);
    for (const block of blocks) log(`  ${block.file}:${block.line}  ${guessShape(block.body)}`);
    return 0;
  }

  // Fail-closed: a gate that scanned nothing must never exit 0 (check-api-vocabulary's rule).
  //
  // Blocks and skips are counted SEPARATELY because only one combination is a defect. `skipped.length > 0`
  // means the documents were read and every block opted out with a reason — legitimate. Zero of both on the
  // FULL-TREE path means no document was read at all (a broken scope predicate or repo root), and this gate
  // would otherwise announce success over an unscanned tree. From a caller-supplied list, zero of both is
  // ordinary — a commit touching only `src/` carries no samples — so that half applies to the tree path
  // alone, while an EMPTY source list indicts any caller.
  if (source.length === 0 || (files === null && blocks.length === 0 && skipped.length === 0)) {
    log('check-samples: ✗ found no documented C# samples at all — not even a skipped one');
    log('  Nothing was scanned, so this gate proves nothing — check IN_SCOPE and the repo root.');
    return 1;
  }

  if (blocks.length === 0) {
    log(`check-samples: no documented C# samples to compile (${plural(skipped.length, 'skipped')}).`);
    return 0;
  }

  const doCompile = compile ?? ((f) => dotnetCompile(repo, join(repo, SCRATCH), f));

  const items = blocks.map((block, i) => ({
    block,
    id: String(i + 1).padStart(4, '0'),
    attempts: attemptsFor(block.body),
    at: 0,
    tried: [],
  }));
  for (const item of items) item.name = `Block_${item.id}.cs`;

  /** One compilation of `group` at each item's current shape. Returns the items that errored. */
  const compileGroup = (group) => {
    const files = new Map();
    const wraps = new Map();
    for (const item of group) {
      const wrap = synthesize(item.block.body, item.attempts[item.at], item.id, item.block.given);
      files.set(item.name, wrap.source);
      wraps.set(item.name, wrap);
    }
    const { byFile, harness } = doCompile(files);
    if (harness) return { harness };
    const failing = [];
    for (const item of group) {
      const errors = byFile.get(item.name) ?? [];
      if (errors.length === 0) continue;
      const { bodyOffset, givenRange } = wraps.get(item.name);
      item.tried.push({
        shape: item.attempts[item.at],
        errors: errors.map((e) => {
          // An error inside the injected declarations is the ANNOTATION's, not the sample's. Saying so is
          // the difference between "your compile-given names a type that does not exist" and a line number
          // pointing at innocent sample code.
          const inGiven = givenRange && e.line - 1 >= givenRange[0] && e.line - 1 < givenRange[1];
          return {
            ...e,
            inGiven: Boolean(inGiven),
            docLine: inGiven ? item.block.givenLine : item.block.line + 1 + (e.line - 1 - bodyOffset),
          };
        }),
      });
      failing.push(item);
    }
    return { failing, clean: byFile.size === 0 };
  };

  const broken = new Set();
  /**
   * Iterate to a fixed point: recompile, re-shape or drop whatever errored, and accept a group only from
   * a compilation in which NOTHING errored. Anything weaker accepts unbound code — Roslyn suppresses all
   * semantic analysis for a compilation carrying a syntax error, so one unparseable block would certify
   * every other block in the batch without ever resolving a single name.
   */
  const settle = (group) => {
    let pending = group;
    // Bound: every non-final iteration advances at least one item's shape, and shapes are exhaustible.
    for (let guard = 0; pending.length > 0 && guard <= group.length * SHAPES.length + 1; guard++) {
      const round = compileGroup(pending);
      if (round.harness) return round.harness;
      if (round.clean) return null;
      const next = [];
      for (const item of pending) {
        if (!round.failing.includes(item)) { next.push(item); continue; }
        item.at++;
        if (item.at >= item.attempts.length) broken.add(item); else next.push(item);
      }
      pending = next;
    }
    return null;
  };

  // Blocks that declare a namespace under the library root get their OWN compilation each — see the
  // header: in a shared one their declarations would outrank the referenced assemblies for every sample.
  const shared = items.filter((i) => !shadowsLibrary(i.block.body));
  const alone = items.filter((i) => shadowsLibrary(i.block.body));

  for (const group of [shared, ...alone.map((i) => [i])]) {
    if (group.length === 0) continue;
    const harness = settle(group);
    if (harness) {
      log('check-samples: ✗ the sample compilation itself failed — this is the HARNESS, not a sample\n');
      log(`  ${harness}`);
      return 2;
    }
  }

  const failures = items.filter((i) => broken.has(i));   // document order, not discovery order
  const compiled = blocks.length - failures.length;

  // The two skip SCOPES are reported apart on purpose. A per-block skip is one author's judgement about
  // one sample; a file-level one takes a whole document out at a stroke and is the number that quietly
  // grows. Printing them together would let the second hide inside the first.
  const perBlock = skipped.filter((s) => s.skipScope === 'block').length;
  const perFile = skipped.length - perBlock;
  const withGiven = blocks.filter((b) => b.given).length;
  const notes = [
    // Counted and printed because a `compile-given` is a claim about the reader's context that someone has
    // to keep true — invisible, it becomes the place stale assumptions accumulate.
    withGiven ? `${withGiven} compiled over a compile-given context` : null,
    perBlock ? `${plural(perBlock, 'block')} skipped with a reason` : null,
    perFile ? `${perFile} in ${plural(skippedDocs.size, 'document')} opted out wholesale` : null,
    supersededDocs ? `${plural(supersededDocs, 'superseded record')} not scanned` : null,
  ].filter(Boolean);

  if (failures.length === 0) {
    log(`check-samples: ${compiled}/${blocks.length} documented C# sample(s) compile ✓`
      + (notes.length ? ` (${notes.join('; ')})` : ''));
    return baseline(compiled, blocks.length);
  }

  log(`check-samples: ✗ ${plural(failures.length, 'documented sample')} ${failures.length === 1 ? 'does' : 'do'}`
    + ` not compile (${compiled}/${blocks.length} do)\n`);
  for (const item of failures) {
    // Report the attempt in the shape the block actually IS. A shape that PARSED is that shape, however
    // many names failed to bind afterwards; one that did not parse produces a wall of brace noise that
    // buries the real error. So: prefer a parsed attempt, then the fewest errors, then the earliest.
    const parsed = item.tried.filter((t) => !t.errors.some((e) => PARSE_ERRORS.has(e.code)));
    const best = (parsed.length ? parsed : item.tried).reduce((a, x) => (x.errors.length < a.errors.length ? x : a));
    log(`  ${item.block.file}:${item.block.line}  (fenced sample, wrapped as ${best.shape})`);
    for (const e of best.errors.slice(0, 6)) {
      const where = e.docLine >= 1 ? `${item.block.file}:${e.docLine}` : `${item.block.file}:${item.block.line}`;
      log(`    ${where}  error ${e.code}: ${e.message}${e.inGiven ? '   ← in its compile-given' : ''}`);
    }
    if (best.errors.length > 6) log(`    … and ${best.errors.length - 6} more`);
    log('');
  }
  if (failures.some((i) => i.tried.some((t) => t.errors.some((e) => e.inGiven)))) {
    log('  An error marked `← in its compile-given` is in the ANNOTATION, not the sample: the declarations');
    log('  it supplies are compiled too, which is exactly what stops one waving a broken sample through.');
    log('');
  }
  log('  A sample that cannot compile is a false promise inside the document a consumer copies from.');
  log('  Fix it — or supply the reader-side context it assumes with');
  log('  `<!-- compile-given: <declarations> -->`, which keeps the sample COMPILED. Reach for');
  log('  `<!-- compile-skip: <honest reason> -->` only when the block cannot stand alone at all (a partial');
  log('  signature quoted for illustration, a before/after pair, a menu of alternatives), or when the');
  log('  context it would need is a whole program rather than a few declarations.');
  return 1;
}

/** Write the scratch project (idempotent). Separated so a test can assert on its contents. */
export function prepareScratch(repo) {
  const dir = join(repo, SCRATCH);
  mkdirSync(dir, { recursive: true });
  const csproj = join(dir, 'Samples.csproj');
  const wanted = projectFile(repo, srcProjects(repo), [...libraryNamespaces(repo), ...CONSUMER_NAMESPACES]);
  if (!existsSync(csproj) || readFileSync(csproj, 'utf8') !== wanted) writeFileSync(csproj, wanted, 'utf8');
  return dir;
}

// CLI entry point — a thin wrapper, so importing this module for a test runs nothing. `import.meta.main`
// where the runtime has it (Node >= 24.2); the argv fallback compares resolved paths. Pinned by
// cli-entry.test.mjs, because a wrapper that stops matching makes the guard silently do NOTHING and exit 0.
if (import.meta.main ?? (process.argv[1] && resolve(process.argv[1]) === here)) {
  const list = process.argv.includes('--list');
  if (!list) prepareScratch(repoDefault);
  process.exitCode = checkSamples(repoDefault, { list });
}
