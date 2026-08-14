// check-warnings — the published projects compile clean. See devtools/scripts/check-warnings.mjs.
//
// What is at stake: `IsAotCompatible=true` stamps `IsTrimmable` into the assembly, so an unfailed
// IL2026/IL3050 is a FALSE trim promise shipped to a consumer's trimmer — four of them shipped into
// Lyntai.Providers.Default before this gate existed. And it has lied once already, in the other direction:
// a `-v normal` log outgrew Node's 1 MiB spawnSync default, spawnSync threw ENOBUFS, and the gate reported
// "build FAILED" for a build that SUCCEEDED (.claude/knowledge/pitfalls.md). A gate that lies is worse than
// one that misses, so both directions are pinned below.
//
// The build is a SEAM here, never actually run: this suite is `verify`'s first step and must cost
// milliseconds. What that buys is the branches a real build can hardly ever produce on demand — a failing
// compile, a truncated log, a missing `dotnet`.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { BUILD_MAX_BUFFER, checkWarnings, runBuild, warningLines } from '../check-warnings.mjs';
import { recorder } from './_fixtures.mjs';

/**
 * An MSBuild `-v normal` warning line, in the shape the real log emits — including the trailing absolute
 * path of the OWNING PROJECT, which is what makes this fixture worth building carefully: that suffix is a
 * second path on every line, and a filter written against the file path alone would match through it.
 */
const warn = (file, code, text, project = null) => {
  const dir = file.split(/[\\/]/).slice(0, -1);
  const owner = project ?? (dir.length ? `${dir.join('\\')}\\${dir[dir.length - 1]}.csproj` : 'X.csproj');
  return `  ${file} : warning ${code}: ${text} [${owner}]`;
};

function run(build, { list = false } = {}) {
  const log = recorder();
  return { code: checkWarnings({ repo: 'D:\\repo', config: { solution: 'X.slnx' }, log, error: log, list, build }), out: log.text() };
}

describe('check-warnings — which lines count as a warning', () => {
  it('needs BOTH a warning code and a src/ path', () => {
    const lines = warningLines([
      warn('D:\\repo\\src\\Lyntai.Core\\A.cs(3,5)', 'IL2026', 'trim unfriendly'),
      warn('D:\\repo\\tests\\Lyntai.Tests\\B.cs(1,1)', 'CS1591', 'missing XML comment'),
      warn('D:\\repo\\samples\\Playground\\C.cs(1,1)', 'CS0168', 'unused'),
      '  Build succeeded with 1 warning(s)',            // the word, but no code
      '  D:\\repo\\src\\Lyntai.Core\\D.cs(1,1): error CS0103: missing',   // a code, but an ERROR
    ].join('\n'));
    assert.equal(lines.length, 1);
    assert.match(lines[0], /IL2026/);
  });

  it('matches a forward-slash log too — the separator is the platform\'s, not the gate\'s', () => {
    assert.equal(warningLines('/home/ci/repo/src/Lyntai.Core/A.cs(1,1): warning CS1574: bad cref').length, 1);
  });

  it('accepts the code shapes this gate was written for — 2-4 uppercase letters then digits', () => {
    for (const code of ['CS1591', 'IL2026', 'IL3050', 'NU1701', 'MSB3277', 'CA1822'])
      assert.equal(warningLines(warn('D:\\repo\\src\\Lyntai.Core\\A.cs(1,1)', code, 'x')).length, 1,
        `${code} must be seen`);
  });

  it('…and the two families the original pattern could not see (SYSLIB…, xUnit…)', () => {
    // Was pinned here as a KNOWN LIMIT on 2026-08-11 and FIXED 2026-08-12 (TASKS.md Part 62). The old
    // `[A-Z]{2,4}\d+` could match neither a six-letter prefix nor a lowercase-led one, so .NET's own
    // obsoletion warnings and the analyzer packages using camelCase ids were invisible — a published project
    // could carry one and this gate would report `src/` clean. Measured before widening: a full
    // --no-incremental solution build scanned with the loose pattern finds the same zero warnings in `src/`
    // the strict one did, so nothing was actually hiding behind it.
    for (const code of ['SYSLIB0011', 'xUnit1013', 'xUnit2013'])
      assert.equal(warningLines(warn('D:\\repo\\src\\A.cs(1,1)', code, 'x')).length, 1,
        `${code} must be seen`);
  });

  it('is still a BOUNDED pattern, not `.+` — prose and codeless numbers stay out', () => {
    // Widening a matcher is how false positives get in, so the bounds are asserted rather than assumed: at
    // least two leading letters, at most ten, and at least one digit.
    assert.deepEqual(warningLines('  D:\\repo\\src\\A.cs(1,1): warning 42: a bare number is not a code'), []);
    assert.deepEqual(warningLines('  D:\\repo\\src\\A.cs(1,1): warning X1: one letter is not a code'), []);
    assert.deepEqual(warningLines('  D:\\repo\\src\\A.cs(1,1): warning ABCDEFGHIJKL42: too long to be a code'), []);
    assert.deepEqual(warningLines('  D:\\repo\\src\\A.cs(1,1): warning CSNODIGITS: no digits is not a code'), []);
  });

  it('deduplicates — MSBuild emits the same diagnostic once per logger pass and per target framework', () => {
    const one = warn('D:\\repo\\src\\Lyntai.Core\\A.cs(3,5)', 'IL2026', 'trim unfriendly');
    assert.equal(warningLines([one, one, one].join('\n')).length, 1,
      'one defect reported three times teaches the reader to discount the count');
  });

  it('survives an empty or absent log without throwing', () => {
    assert.deepEqual(warningLines(''), []);
    assert.deepEqual(warningLines(undefined), []);
    assert.deepEqual(warningLines(null), []);
  });
});

describe('check-warnings — the report', () => {
  it('passes a clean build and says so', () => {
    const { code, out } = run(() => ({ status: 0, stdout: 'Build succeeded.\n' }));
    assert.equal(code, 0);
    assert.match(out, /src\/ compiles warning-free ✓/);
  });

  it('FAILS on a warning in a published project, and relativizes the repo path', () => {
    const { code, out } = run(() => ({
      status: 0,
      stdout: warn('D:\\repo\\src\\Lyntai.Core\\A.cs(3,5)', 'IL2026', 'trim unfriendly'),
    }));
    assert.equal(code, 1);
    assert.match(out, /1 warning\(s\) in src\/ — a published project must compile clean/);
    assert.match(out, /IL2026/);
    assert.match(out, /^ {2}\.[\\/]src[\\/]Lyntai\.Core[\\/]A\.cs/m, 'the location leads with a repo-relative path');
    // …but only the FIRST occurrence: `String.replace` with a string needle replaces once, and MSBuild
    // appends the owning project absolutely (`[D:\repo\src\…csproj]`), so that copy survives. Pinned as it
    // behaves rather than quietly "fixed": this is console output, not a tracked file, and the trailing
    // project path is what tells you WHICH project of a multi-targeted build warned.
    assert.match(out, /\[D:\\repo\\src\\Lyntai\.Core\\Lyntai\.Core\.csproj\]/);
  });

  it('truncates at 15 and says how to see the rest; --list shows them all', () => {
    const many = Array.from({ length: 20 }, (_, i) => warn(`D:\\repo\\src\\Lyntai.Core\\A${i}.cs(1,1)`, 'CS1591', 'x'))
      .join('\n');
    const short = run(() => ({ status: 0, stdout: many }));
    assert.equal(short.code, 1);
    assert.match(short.out, /20 warning\(s\)/);
    assert.match(short.out, /… 5 more \(--list to see all\)/);

    const full = run(() => ({ status: 0, stdout: many }), { list: true });
    assert.doesNotMatch(full.out, /more \(--list/);
    assert.match(full.out, /A19\.cs/);
  });
});

describe('check-warnings — a log it cannot trust is never a green light', () => {
  it('reports the BUILD when the build failed, not the warnings inside it', () => {
    const { code, out } = run(() => ({ status: 1, stdout: warn('D:\\repo\\src\\A.cs(1,1)', 'CS1591', 'x') }));
    assert.equal(code, 1);
    assert.match(out, /build FAILED — fix the build first/);
    assert.doesNotMatch(out, /warning\(s\) in src\//, 'a failed build\'s warning list is noise, not the problem');
  });

  it('never reports "warning-free" for a build that did not complete (the ENOBUFS shape)', () => {
    // The measured lie: spawnSync throws ENOBUFS when the log outgrows maxBuffer, `status` comes back null,
    // and stdout is truncated — so the parse finds no warnings. Whatever this prints, it must not be green.
    const { code, out } = run(() => ({ status: null, stdout: '', error: Object.assign(new Error('x'), { code: 'ENOBUFS' }) }));
    assert.notEqual(code, 0);
    assert.doesNotMatch(out, /warning-free/);
    assert.match(out, /ENOBUFS/, 'and the cause is printed, or the reader hunts a build failure that is not there');
  });

  it('reports a missing `dotnet` the same way', () => {
    const { code, out } = run(() => ({ status: null, stdout: '', error: Object.assign(new Error('x'), { code: 'ENOENT' }) }));
    assert.notEqual(code, 0);
    assert.match(out, /ENOENT/);
  });
});

describe('check-warnings — the build flags, every one of which fails GREEN if wrong', () => {
  const spy = () => {
    let seen = null;
    const spawn = (exe, argv, opts) => { seen = { exe, argv, opts }; return { status: 0, stdout: '' }; };
    runBuild('D:\\repo', 'X.slnx', spawn);
    return seen;
  };

  it('builds with `-v normal` — `minimal` does not print the per-project warning lines this parses', () => {
    const { exe, argv } = spy();
    assert.equal(exe, 'dotnet');
    assert.deepEqual(argv.slice(0, 2), ['build', 'X.slnx']);
    assert.ok(argv.includes('-v') && argv[argv.indexOf('-v') + 1] === 'normal');
  });

  it('builds `--no-incremental` — MSBuild does not re-emit warnings for a project it did not rebuild', () => {
    // Without this, the gate is green on its SECOND run over an unchanged tree no matter what is in it,
    // which is the worst shape a gate can take: it passes exactly when nobody is looking.
    assert.ok(spy().argv.includes('--no-incremental'));
  });

  it('gives spawnSync a maxBuffer far above the 1 MiB default', () => {
    // Measured: a full-solution `-v normal` log came within 6,846 bytes of the 1 MiB default (99.35%).
    assert.ok(BUILD_MAX_BUFFER >= 16 * 1024 * 1024, 'the default is 1 MiB and the log has nearly reached it');
    assert.equal(spy().opts.maxBuffer, BUILD_MAX_BUFFER);
    assert.equal(spy().opts.encoding, 'utf8', 'stdout must be text — a Buffer would match no line at all');
  });
});
