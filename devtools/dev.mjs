// Lyntai devtools dispatcher (family pattern: one entry, project-specific inputs in project.config.mjs).
//   node devtools/dev.mjs build            - dotnet build the solution
//   node devtools/dev.mjs check-warnings   - FAIL if any src/ project compiles with a warning (--list for all)
//   node devtools/dev.mjs check-bundle     - FAIL if the `Lyntai` bundle's dependency closure drifted (D26)
//   node devtools/dev.mjs check-packages   - FAIL if a package is missing from any registry it needs (D27)
//   node devtools/dev.mjs check-docs       - FAIL if a doc uses vocabulary a decision retired (the PROSE)
//   node devtools/dev.mjs check-encoding   - FAIL if a tracked text file contains MOJIBAKE (mangled UTF-8)
//   node devtools/dev.mjs check-counts     - FAIL if a COUNT written in prose disagrees with the tree
//   node devtools/dev.mjs check-comments   - FAIL if a comment block outgrows what it explains
//   node devtools/dev.mjs check-api-vocabulary
//                                          - FAIL if a committed API baseline still spells a retired name
//   node devtools/dev.mjs check-samples [--list]
//                                          - FAIL if a fenced C# block in our own docs does not COMPILE
//   node devtools/dev.mjs new-package <Id> - scaffold an adapter package + register it in all nine registries
//   node devtools/dev.mjs consumer-smoke   - pack, then restore/build/run a fresh app against the PACKAGES
//   node devtools/dev.mjs test [args]      - dotnet test the test project (extra args pass through)
//   node devtools/dev.mjs test-devtools    - node --test over devtools/scripts/__tests__ (the gates' own tests)
//   node devtools/dev.mjs e2e [all|pN|pN-pM|p1,p3] [--build] [--parallel[=N]] - Playground e2e suites
//   node devtools/dev.mjs playground [args]- run the sample console app (uses LYNTAI_PROVIDER_CMD if set)
//   node devtools/dev.mjs pack             - dotnet pack the packable libraries -> publish/packages/
//   node devtools/dev.mjs doctor [--fix]   - check README ## Status version == VersionPrefix (--fix syncs it)
//                                            AND VersionPrefix == the newest v* release tag (authorship)
//   node devtools/dev.mjs check-version    - the pre-commit version-authorship guard, run by hand
//   node devtools/dev.mjs changelog [--fix] [--version X.Y.Z] [--date YYYY-MM-DD]
//                                          - check/stamp the CHANGELOG `## Unreleased` heading for a release
//   node devtools/dev.mjs release-notes --tag vX.Y.Z [--from vA.B.C]
//                                          - PREVIEW the GitHub Release body the workflow will publish
//   node devtools/dev.mjs install-hooks    - git core.hooksPath -> devtools/hooks (pre-commit guard)
//   node devtools/dev.mjs check-sensitive  - scan staged changes (--tree for all tracked files)
//   node devtools/dev.mjs decisions-index  - regenerate the index table at the top of docs/DECISIONS.md
//                                          - (--check to fail instead of writing)
import { spawn, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import config from './project.config.mjs';
import { changelogDoctor, packDoctor, versionDoctor } from './scripts/doctors.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [cmd, ...args] = process.argv.slice(2);

const run = (exe, argv, opts = {}) => {
  const r = spawnSync(exe, argv, { stdio: 'inherit', cwd: repo, shell: false, ...opts });
  process.exitCode = r.status ?? 1;
};

// The three doctors (pack / version / changelog) live in scripts/doctors.mjs — extracted 2026-08-11 so they
// could be TESTED (TASKS.md Part 62); a function inside this switch cannot be driven by anything. They keep
// their reasoning next to themselves; this file is their command line.

switch (cmd) {
  case 'build':
    run('dotnet', ['build', config.solution, '-v', 'minimal']);
    break;

  // A LIBRARY-specific gate: the published projects must compile warning-free. Not style policing —
  // it's how a shipped claim stops rotting silently. `IsAotCompatible=true` stamps IsTrimmable into the
  // assembly, telling a consumer's trimmer "safe to trim"; the only thing that catches code which breaks
  // that promise is an IL2026/IL3050 warning, and a warning nobody fails on is a warning nobody reads
  // (four of them shipped into Lyntai.Providers.Default this way). Doc-comment warnings matter for the same
  // reason: unresolved crefs ship inside the XML docs consumers read in IntelliSense.
  // Scoped to src/ — tests and samples are free to warn. Pass --list to see them all.
  //
  // Extracted to its own script 2026-08-11 so it can be TESTED (TASKS.md Part 62) — the three build flags
  // it passes are load-bearing and two of them fail green (`--no-incremental` most of all: MSBuild does not
  // re-emit a warning for a project it did not rebuild). Nothing about what it catches changed in the move.
  case 'check-warnings':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-warnings.mjs'), ...args]);
    break;

  // The `Lyntai` bundle's DEPENDENCY BUDGET (docs/DECISIONS.md D26). Membership in the one-line install is
  // not free: an untrimmed `dotnet publish` copies the whole graph and analyses nothing, so every package the
  // bundle references lands in every bundle consumer's output folder whether they call it or not. This fails
  // when the bundle's third-party closure gains an id nobody decided on — the drift that a growing package
  // graph produces silently, since adding one ProjectReference can pull a whole SDK behind it.
  // The Microsoft.Extensions.* band is auto-allowed (runtime version band; present in any DI app).
  //
  // Extracted to its own script 2026-08-11 so it can be TESTED (TASKS.md Part 62) — its allowlist-STALENESS
  // branch had never run at all, and a rotted allowance is a standing permission for an id to come back
  // without anyone deciding again. Nothing about what it catches changed in the move.
  case 'check-bundle':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-bundle.mjs'), ...args]);
    break;

  // The package INVENTORY is consistent across every registry that must know about a package (D27).
  case 'check-packages':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-packages.mjs'), ...args]);
    break;

  // Scaffold an adapter package AND register it in all nine registries — the companion to check-packages.
  // Bundle membership stays a human decision (D26); everything mechanical is done for you.
  case 'new-package':
    run('node', [path.join(repo, 'devtools', 'scripts', 'new-package.mjs'), ...args]);
    break;

  // A RELEASE gate, not a dev-loop one: packs every package to a scratch feed and restores/builds/runs a fresh
  // consumer app against it. Minutes, not seconds — deliberately out of `verify`. Run before a release or
  // after touching packaging; it is the only check that exercises what actually SHIPS.
  case 'consumer-smoke':
    run('node', [path.join(repo, 'devtools', 'scripts', 'consumer-smoke.mjs'), ...args]);
    break;

  case 'test':
    run('dotnet', ['test', config.testProject, '-v', 'minimal', ...args]);
    break;

  // The gates' OWN tests (node --test, in the runtime already — no dependency, which is the same budget
  // reasoning as D26). It runs FIRST in `verify`, before the guards it covers: every one of these scripts
  // fails PERMISSIVELY when it breaks, so a broken guard reports a clean repository, and running it proves
  // nothing. Three measured check-docs defects passed every gate for their whole lifetime that way — see
  // TASKS.md Part 60 and devtools/scripts/__tests__/. Milliseconds, so unlike consumer-smoke it is in.
  //
  // A GLOB, not the directory: Node's runner matches test files by pattern, and a bare directory argument is
  // loaded as a module instead ("Cannot find module …__tests__"). Forward slashes, resolved against repo/.
  //
  // Quiet when green and loud when red, the same shape as check-warnings: `verify` should gain one line for
  // a gate that costs milliseconds. Pass any --test-reporter (or --watch) to get the runner's own output —
  // that path streams live and is what to use while writing a test.
  case 'test-devtools': {
    const label = 'test-devtools';
    const pattern = 'devtools/scripts/__tests__/*.test.mjs';
    if (args.some((a) => a.startsWith('--test-reporter') || a === '--watch')) {
      run('node', ['--test', ...args, pattern]);
      break;
    }
    const r = spawnSync('node', ['--test', '--test-reporter=spec', ...args, pattern],
      { cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    const out = `${r.stdout ?? ''}${r.stderr ?? ''}`;
    const count = (name) => Number((out.match(new RegExp(`^\\u2139 ${name} (\\d+)`, 'm')) ?? [])[1] ?? NaN);
    const [tests, passed, failed] = [count('tests'), count('pass'), count('fail')];
    if (r.status === 0 && failed === 0 && tests > 0) {
      console.log(`${label}: ${passed}/${tests} guard-script tests pass ✓`);
      break;
    }
    console.error(out.trimEnd());
    console.error(`\n${label}: ✗ the scripts that GATE this repository are themselves failing` +
      `${Number.isFinite(failed) && failed > 0 ? ` — ${failed} test(s)` : ''}\n` +
      '  Nothing below this gate can be trusted until it is green: each of these scripts fails permissively,\n' +
      '  so a broken one reports a clean repository (TASKS.md Part 60).');
    process.exitCode = r.status || 1;
    break;
  }

  case 'playground':
    run('dotnet', ['run', '--project', config.playgroundProject, ...args]);
    break;

  case 'bench':
    // BenchmarkDotNet refuses a Debug build — always Release. Extra args pass to the switcher
    // (e.g. `node devtools/dev.mjs bench -- --filter *Router*`).
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', ...args]);
    break;

  // Replays the deterministic memory corpus against a live SQLite-backed graph engine under the four
  // {ranking x forgetting} policy arms, PAIRED BY SEED (every arm at a given seed+shape replays the
  // byte-identical corpus — DSR-default falsification plan Task 2), and prints paired mean differences with
  // 95% CIs plus the 2x2 interaction — see bench/Lyntai.Benchmarks/MemoryPolicySweep.cs. `--sweep` is
  // branched on INSIDE Program.cs before the BenchmarkSwitcher ever runs, so this is deliberately NOT `bench`
  // with a filter: BDN measures wall-clock time per operation, not recall quality. Runs a pilot (5 seeds)
  // then scales to the seed count that pilot's own variance implies, one live SQLite db per (seed, shape,
  // arm) combination, fanned out across all cores — minutes, not hours, but still slow enough, like
  // `consumer-smoke`, to stay deliberately OUT of `verify`.
  case 'memory-sweep':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--sweep', ...args]);
    break;

  // memory-spacing — a SENSITIVITY study over one DsrOptions constant, SpacingWeight, asking whether the
  // `topical` regression D49 shipped knowingly is even responsive to the knob TASKS.md Part 56 named as its
  // suspect. Same harness, same corpus, same seed pairing as `memory-sweep`; one factor instead of a 2x2, and
  // it ADOPTS nothing. That last part is what makes it legitimate where parameter FITTING is not: fitting
  // DsrOptions against this library's own review log is circular by construction (the log's grade is a
  // function of the model's own prediction, and it can only ever contain successes) — see docs/DECISIONS.md
  // D51's 2026-08-12 amendment. A sensitivity curve makes no claim about the true value, so neither
  // objection touches it. Out of `verify` for the same reason memory-sweep is: tens of minutes.
  case 'memory-spacing':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--spacing', ...args]);
    break;

  // memory-reinforcement — the isolation `memory-spacing` structurally could not do. SpacingWeight
  // multiplies the WHOLE increase, so its best arm (0) is reinforcement switched OFF, which is consistent
  // with two different diagnoses and distinguishes neither. This adds a third arm that keeps reinforcement
  // ON with the r-dependence frozen (the real Reinforce called with Age pinned to Stability, where r is
  // exactly the curve's own 0.5 anchor), so `law 3 does not transfer` and `reinforcement is net-harmful` stop
  // being the same setting. See TASKS.md Part 64 / docs/DECISIONS.md D53.
  // memory-reinforcement — isolates the r-dependence of law 3 from reinforcement MAGNITUDE, which
  // memory-spacing's SpacingWeight knob cannot separate (it multiplies the whole increase term, so its 0 arm
  // is reinforcement switched OFF rather than law 3 switched off). TASKS.md Part 64.
  case 'memory-reinforcement':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--reinforcement', ...args]);
    break;

  // memory-bounded — the study the other four converge on. Salience (clamped) and the age reset (age -> 0)
  // both help; stability growth, which multiplies on every recall, hurts badly. If the difference is that
  // the effect COMPOUNDS rather than where its signal comes from, a growth rule that cannot compound should
  // beat both the shipped rule and switching growth off. Four arms over the FORM of the rule, not its
  // constants. This is what decides whether 3.0 changes DsrOptions.ReinforceGain. TASKS.md Part 64.
  case 'memory-bounded':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--bounded', ...args]);
    break;

  // memory-salience — salience ships ON for two of its three consumers (decay resistance, store admission)
  // and has never been measured ONCE: MemoryPolicySweep's C1 control asserts its absence, and novelty needs
  // an embedder + vector store no harness supplied. Both arms here get FakeEmbedder + InMemoryVectorStore so
  // enrichment is held constant and only salience varies. It cannot settle whether novelty INVERTS on noisy
  // input — this corpus's noise is templated, so it reads as familiar rather than novel. TASKS.md Part 53.
  case 'memory-salience':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--salience', ...args]);
    break;

  // memory-language — the same corpus in Chinese. Every recall-quality figure this repository publishes was
  // measured on English, space-separated text: the friendliest tokenization the library supports, recorded
  // as a blind spot in design §5.7.0 and never measured. The two arms replay STRUCTURALLY IDENTICAL corpora
  // (same steps, ids and ground truth; only the text differs), so a gap is the language and not the
  // timeline. Adopts nothing — the language is the consumer's, not a setting. docs/DECISIONS.md D55.
  case 'memory-language':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--language', ...args]);
    break;

  // memory-annotation — cluster recall sits at the no-graph floor (miss = 1 - 1/AttributeCount) and is
  // IDENTICAL at recall limit 10 and 50, so those entries are never gathered and no ranking policy can reach
  // them. IMemoryAnnotationPolicy is the only mechanism that addresses it. The annotator here is PERFECT by
  // construction, so the numbers are the mechanism's CEILING rather than a model's accuracy — a real one can
  // only do worse, and if the ceiling is low no prompt rescues it. English + Chinese, because the argument
  // for a model here is that the judgement is language-independent.
  case 'memory-annotation':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--annotation', ...args]);
    break;

  // memory-verification — the only mechanism aimed at PollutionRate, and the only 3.0 seam that had no sweep
  // (added 2026-08-15). Every OTHER recall-quality figure this repository publishes is MODEL-FREE: the policy
  // sweep wires no verifier and no annotator, so its numbers are the lexical floor rather than what a
  // consumer who registered an LLM would see. That matters for reading them: critical-rare's ~0.805 pollution
  // sits half a point above the STRUCTURAL floor (limit - |relevant|) / limit = 0.800, because a 10-slot page
  // over a 2-target ground truth is 80% non-relevant however perfect the ranker. The only way under that
  // floor is to return FEWER things, which is what VerificationFilters does. Perfect judge = the mechanism's
  // CEILING, never a model's accuracy; three arms (off / reorder / filter) so "consulted" and "obeyed" are
  // distinguishable.
  case 'memory-verification':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--verification', ...args]);
    break;

  // memory-fan — ACT-R's fan effect (ReciprocalRankFusionOptions.DiagnosticityWeight), added 2026-08-15 and
  // shipped at 0 because nothing had measured it. Nothing in the engine consulted GraphNode.Degree, so a node
  // with fifty neighbours contributed exactly as much as one with a single edge — while subject annotation
  // exists precisely to BUILD hubs. The argument for penalising a hub is information-theoretic (a node
  // adjacent to everything discriminates nothing), not biomimetic. Four coarse arms: direction and magnitude,
  // never a search, and no default moves off zero on one run.
  case 'memory-fan':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--fan', ...args]);
    break;

  // memory-enrichment — WHY registering an embedder costs recall quality, which had never been separated
  // from THAT it does. Two write-time mechanisms could explain it (similarity linking; novelty feeding
  // salience) and one knob switched both, so a 2x2 needed `MinSimilarity` above 1 to keep the embed while
  // writing no edge, and a neutral salience policy to keep the edges while dropping novelty.
  // The ONLY sweep here that calls a REAL model, and it exits rather than substituting a double: the arm it
  // replaces used a feature-hashed bag of words in which "semantic similarity" IS word overlap, so the
  // embedder could only ever be seen paying a cost it could never be seen earning back.
  case 'memory-enrichment':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--enrichment', ...args]);
    break;

  // memory-salience-weight — whether the `many-candidates` regression salience ships with is recoverable by
  // BOUNDING its ranking voice (ReciprocalRankFusionOptions.SalienceWeight), which is the open half of
  // TASKS.md Part 65. Needs a REAL embedder, and for a sharper reason than the enrichment sweep's: without
  // one, salience declines on every write and RRF ranks by COMPETITION (D82), so a uniformly-absent signal
  // contributes the same constant at every weight — arm 0 and arm 2 are the same engine and the flat curve
  // reads as an exoneration. Every cell therefore reports salient-vs-judged writes, and the verdict refuses
  // to interpret the table unless that ratio is strictly between 0 and 1.
  case 'memory-salience-weight':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--salience-weight', ...args]);
    break;

  // memory-importance — WHAT the salience signal measures, where memory-salience-weight asked how loud it is.
  // The shipped novelty policy against a perfect importance ORACLE, salience-off as control. D89 priced
  // novelty and took its ranking voice to zero, which is not a finding about importance: novelty is monotone
  // in "unlike anything already stored", so sustained significance decays on that axis exactly as it is
  // confirmed while a one-off triviality reads as maximal. The decisive shape is `diverse-noise`, because
  // under templated noise the second entry onward reads as FAMILIAR and the failure mode is unreachable.
  // Ranking is held at the shipped SalienceWeight = 0, so this prices SURVIVAL — decay resistance and store
  // admission — which is D45's actual claim for what salience means. Needs a REAL embedder for D89's reason:
  // without one the novelty arm silently BECOMES the control and the table reads as a win.
  // The oracle is a CEILING, never an accuracy: it reads ground truth off the corpus id, so a weak result
  // kills the idea and a strong one only says a real rater is worth costing.
  case 'memory-importance':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--importance', ...args]);
    break;

  // memory-density — do a CORRECTION and a RECURRENCE separate on SalienceContext.SimilarCount? The gist
  // tier's promotion rule rests entirely on that separation, and this is the cheapest way to refute it:
  // authored fixtures, a real embedder, no corpus and no tier. A `novel` population is the control, because
  // a signal that returns a high number for everything looks excellent on recurrence alone.
  case 'memory-density':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--density', ...args]);
    break;

  // memory-support — which rule selects a recurring cluster's CURRENT regime, for the planned gist tier.
  // The default is the full sweep: the corpus grid x seeds x both pacing regimes, scored against BOTH
  // declared answers, so a rule that merely tracks recency can be refuted rather than winning by
  // construction. Its MECHANICAL arms are model-free and always run; the model arm is additive and says
  // so when it is skipped. `--screen` is the size ladder alone, on one shape, and EXITS rather than
  // substituting a scripted stand-in: an arm measuring what a model is worth cannot use a double.
  case 'memory-support':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--gist-support', ...args]);
    break;

  // memory-scale — the blind spot docs/memory.md §8 concedes outright: nothing in this subsystem had
  // exceeded a few hundred entries. MemoryRecallBenchmarks does run 1k/10k/100k and runs them against
  // SqliteMemoryStore — the KEYWORD store — so the graph engine's own write and read paths were unmeasured
  // at any size. The ONLY sweep here whose subject is COST rather than recall quality, which is why it says
  // so loudly and reports no miss or pollution at all: it has no ground truth and is not the quality
  // instrument. Runs SEQUENTIALLY where every other sweep fans out — contention cannot bias a rate and
  // biases a latency silently.
  case 'memory-scale':
    if (!config.benchProject) { console.log('no bench project configured'); break; }
    run('dotnet', ['run', '-c', 'Release', '--project', config.benchProject, '--', '--scale', ...args]);
    break;


  case 'install-hooks':
    run('git', ['config', 'core.hooksPath', 'devtools/hooks']);
    console.log('git hooks installed (core.hooksPath = devtools/hooks). Pre-commit runs check-sensitive.');
    break;

  case 'check-sensitive':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-sensitive.mjs'), ...args]);
    break;

  case 'decisions-index':
    run('node', [path.join(repo, 'devtools', 'scripts', 'decisions-index.mjs'), ...args]);
    break;

  case 'doctor': {
    // both checks always run (no short-circuit) so drift is reported in one pass. `--fix` syncs the README
    // headline; it deliberately does NOT "fix" the version — a hand-authored version is the problem, not
    // the symptom, so it is restored by hand or by letting the release workflow bump it.
    const readmeOk = packDoctor({ repo, version: config.version, fix: args.includes('--fix') });
    const versionOk = versionDoctor({ repo, version: config.version });
    process.exitCode = readmeOk && versionOk ? 0 : 1;
    break;
  }

  case 'check-version':
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-version-bump.mjs'), ...args]);
    break;

  case 'changelog': {
    // Deliberately NOT folded into `doctor`/`pack`: those run on every local pack, and rewriting a
    // history-facing document as a side effect of building packages would be a surprise. Stamping is an
    // explicit act of RELEASING — the release workflow calls this, and a manual release runs the same line.
    const valueOf = (flag) => {
      const at = args.indexOf(flag);
      return at >= 0 ? args[at + 1] : undefined;
    };
    process.exitCode = changelogDoctor({
      repo,
      fix: args.includes('--fix'),
      version: valueOf('--version') ?? config.version,
      date: valueOf('--date'),
    }) ? 0 : 1;
    break;
  }

  case 'release-notes': {
    // The same module the release workflow runs, so a preview is the real thing rather than an
    // approximation of it. Worth having as a command: the notes are the one release output a consumer
    // reads, and until 2026-08-16 nothing could look at them before they were published.
    run('node', [path.join(repo, 'devtools', 'scripts', 'release-notes.mjs'), ...args]);
    break;
  }

  case 'pack': {
    // auto-sync the README `## Status` version to VersionPrefix, then pack — the release pipeline bumps the
    // version, so pack updates the header for it (never packs a stale README, never hard-fails on a bump).
    packDoctor({ repo, version: config.version, fix: true });
    // dotnet pack each packable library → publish/packages/*.nupkg, then print id + sha256.
    const out = path.join(repo, 'publish', 'packages');
    fs.rmSync(out, { recursive: true, force: true });
    fs.mkdirSync(out, { recursive: true });
    for (const proj of config.packableProjects) {
      const r = spawnSync('dotnet', ['pack', proj, '-c', 'Release', '-o', out, '-v', 'minimal',
        `-p:Version=${config.version}`], { stdio: 'inherit', cwd: repo, shell: false });
      if (r.status !== 0) { process.exitCode = r.status ?? 1; break; }
    }
    for (const f of fs.readdirSync(out).filter((f) => f.endsWith('.nupkg'))) {
      const sha = crypto.createHash('sha256').update(fs.readFileSync(path.join(out, f))).digest('hex');
      console.log(`  ${f}\n    sha256: ${sha}`);
    }
    break;
  }

  case 'e2e': {
    const scriptsDir = path.join(repo, 'devtools', 'scripts', 'e2e');
    // Fail-closed, the same rule the check-* scanners carry: `e2e` is a VERIFY GATE, and verify propagates
    // this exit code — so a vanished directory or a wrong repo root would otherwise let the gate report
    // success having run nothing at all. Suites existing is a property of this repository, not a maybe.
    if (!fs.existsSync(scriptsDir)) {
      console.error('e2e: ✗ no suite directory at devtools/scripts/e2e/ — nothing ran, so this gate proves nothing');
      process.exitCode = 1;
      break;
    }
    const all = fs.readdirSync(scriptsDir)
      .filter((f) => /^p\d+\.mjs$/.test(f))   // suites live in scripts/e2e/ as p1.mjs, p2.mjs, …
      .map((f) => f.slice(0, -4))
      // NUMERIC order (p2 before p10) — a plain .sort() is lexicographic, which puts p3–p9 LAST.
      .sort((a, b) => Number(a.slice(1)) - Number(b.slice(1)));

    // Selector: all | pN | pN-pM (range) | p1,p3,p9 (list). Flags: --build (build first), --parallel[=N].
    const flags = args.filter((a) => a.startsWith('-'));
    const sel = args.find((a) => !a.startsWith('-')) ?? 'all';
    const doBuild = flags.includes('--build');
    const pFlag = flags.find((f) => f === '--parallel' || f.startsWith('--parallel=') || f === '-p' || f.startsWith('-p='));
    let limit = 1;
    if (pFlag) {
      const n = pFlag.includes('=') ? Number(pFlag.split('=')[1]) : NaN;
      limit = Number.isFinite(n) && n > 0 ? n : Math.max(2, Math.min(6, (os.cpus().length || 4) - 2));
    }

    const expand = (s) => {
      if (s === 'all') return all;
      if (s.includes(',')) return s.split(',').map((x) => x.trim());
      const range = s.match(/^p(\d+)-p?(\d+)$/);
      if (range) {
        const [lo, hi] = [Number(range[1]), Number(range[2])];
        return all.filter((x) => { const n = Number(x.slice(1)); return n >= lo && n <= hi; });
      }
      return [s];
    };
    const suites = expand(sel).filter((s) => all.includes(s));
    // Same rule from the other end: a selector that matches nothing ran nothing. This is the typo case —
    // `e2e p12` when the suites are p1..p3 — and printing a note while exiting 0 makes a mistyped run
    // indistinguishable from a passing one, to a person and to any script that checks the code.
    if (suites.length === 0) {
      console.error(`e2e: ✗ no suites match "${sel}" — available: ${all.join(', ') || '(none)'}`);
      process.exitCode = 1;
      break;
    }

    if (doBuild) {
      console.log('e2e: building first…');
      const b = spawnSync('node', [path.join(repo, 'devtools', 'dev.mjs'), 'build'], { stdio: 'inherit', cwd: repo });
      if (b.status !== 0) { console.error('e2e: build failed — aborting'); process.exitCode = b.status ?? 1; break; }
    }

    // A suite passes on a clean exit-0, OR if it printed its "PASS" line — a suite that logically passed
    // but then died in process teardown (the Windows libuv UV_HANDLE_CLOSING abort) has succeeded; trust
    // the marker over the crash code.
    const runOne = (suite, capture) => new Promise((resolve) => {
      const t0 = Date.now();
      const child = spawn('node', [path.join(scriptsDir, `${suite}.mjs`)], { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] });
      let out = '';
      const tee = (dest) => (d) => { out += d; if (!capture) dest.write(d); };
      child.stdout?.on('data', tee(process.stdout));
      child.stderr?.on('data', tee(process.stderr));
      child.on('close', (code, signal) => {
        const logicallyPassed = new RegExp(`\\ne2e-${suite} PASS`).test(out);
        resolve({ suite, passed: (code === 0 && !signal) || logicallyPassed, status: code, signal, out, ms: Date.now() - t0 });
      });
    });

    const results = [];
    const wallStart = Date.now();
    if (limit <= 1) {
      for (const suite of suites) results.push(await runOne(suite, false));
    } else {
      console.log(`e2e: ${suites.length} suites, up to ${limit} at once…`);
      const pending = [...suites];
      const running = [];
      const launch = (suite) => {
        const entry = { promise: runOne(suite, true).then((rec) => {
          running.splice(running.indexOf(entry), 1);
          results.push(rec);
          process.stdout.write(rec.out);
          console.log(`  ${rec.passed ? '✓' : '✗'} ${rec.suite} (${(rec.ms / 1000).toFixed(0)}s)`);
        }) };
        running.push(entry);
      };
      while (pending.length || running.length) {
        while (pending.length && running.length < limit) launch(pending.shift());
        if (running.length) await Promise.race(running.map((e) => e.promise));
      }
    }

    results.sort((a, b) => Number(a.suite.slice(1)) - Number(b.suite.slice(1)));
    const wall = ((Date.now() - wallStart) / 1000).toFixed(0);
    const failed = results.filter((r) => !r.passed);
    console.log(`\ne2e: ${results.length - failed.length}/${results.length} suites passed in ${wall}s${limit > 1 ? ` (parallel ×${limit})` : ''}`);
    for (const f of failed) console.log(`  ✗ ${f.suite} — ${f.signal ? `signal ${f.signal}` : `exit ${f.status}`}`);
    if (failed.length) process.exitCode = 1;
    break;
  }

  // The prose counterpart to check-warnings: fail when a doc uses vocabulary a decision retired.
  //
  // IN verify, unlike `decisions-index`, and the difference is the cost of being wrong. A stale index costs
  // a reader one Ctrl-F. A stale SPEC costs the next session a wrong implementation — it is read as the
  // contract, and on 2026-08-08 two specs silently stopped being true while every code gate stayed green.
  // Registry: `retiredTerms` in project.config.mjs.
  case 'check-docs': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-docs.mjs'), ...args]);
    break;
  }

  // check-docs' other twin: the same "does this document still tell the truth" question, asked of its
  // in-repo REFERENCES rather than its vocabulary. `docs/superpowers/INDEX.md` already ends its archiving
  // procedure with "repoint every inbound reference, and check nothing dangles" — and that step was skipped
  // when the ranking × forgetting measurement record was untracked under D43, leaving SIX dead references in
  // maintained state (README ×3, the design contract, DECISIONS ×2) that every gate reported clean until a
  // reader found them. Existence only, never line numbers: those rot legitimately on every refactor.
  // Registry: `staleReferenceAllowances` in project.config.mjs. Line escape: `link-ok`.
  case 'check-links': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-links.mjs'), ...args]);
    break;
  }

  // The THIRD member of that family, asking whether what a document COUNTS is still true. TASKS.md Part 73
  // measured six corrections to a counted claim inside sixty commits, plus two more that went stale during
  // the session which built this — eight incidents, zero automated catches, because check-docs structurally
  // cannot see one: a count going stale retires no vocabulary, so the sentence stays grammatical and wrong.
  // Registry: `COUNTED_CLAIMS` in the script itself (an entry is a regex plus a FUNCTION, so it is code and
  // project.config.mjs stays a data file). Line escape: `count-ok`, deliberately not `drift-ok`.
  case 'check-counts': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-counts.mjs'), ...args]);
    break;
  }

  // The prose gates ask whether a document still SAYS, POINTS AT and COUNTS what is true; this one asks
  // whether a comment is still doing a comment's JOB. Registry: `commentBlockAllowances` in
  // project.config.mjs, a RATCHET (an allowance looser than the file needs fails). Rule:
  // .claude/rules/code-commentary.md. Line escape: `comment-ok`, deliberately not `drift-ok`.
  case 'check-comments': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-comments.mjs'), ...args]);
    break;
  }

  // FAIL when a tracked text file contains mojibake — UTF-8 decoded as another codepage and written back.
  //
  // A gate rather than a rule because the rule already existed and was still broken THREE TIMES in one
  // session (2026-08-13), each time under time pressure, each time caught only because someone looked. The
  // damage is silent by construction: the file stays valid UTF-8, compiles, passes every test and ships —
  // so no other gate can see it. Same reasoning as check-warnings and check-docs, applied to bytes.
  case 'check-encoding': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-encoding.mjs'), ...args]);
    break;
  }

  // check-docs' twin, one layer down: the same "vocabulary a decision retired" question, asked of the
  // frozen PUBLIC SURFACE instead of the prose. It exists because neither neighbour can ask it —
  // check-docs deliberately excludes src/, and the API baseline records parameter names without JUDGING
  // them, so a stale name round-trips cleanly through the one gate whose whole job is noticing API
  // changes. Three of them (`ageClocks:`, `appraisers:`, `modulators:`) reached the eve of the 3.0 freeze
  // that way and a human review, not a gate, caught them (TASKS.md Part 61, docs/DECISIONS.md D47). Named
  // arguments are source-compatible surface, so after a freeze each one costs a MAJOR version — which is
  // what puts this in `verify` rather than beside `decisions-index`.
  // Registry: `retiredApiNames` in project.config.mjs (its own, NOT retiredTerms — that one is prose).
  case 'check-api-vocabulary': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-api-vocabulary.mjs'), ...args]);
    break;
  }

  // The third gate over the documentation, and the one that reads what a consumer actually COPIES:
  // every fenced ```csharp block in the maintained prose is wrapped by shape and compiled against the real
  // projects. `check-docs` gates the words around the sample and `check-api-vocabulary` gates the names
  // inside the assembly; nothing gated the sample itself, so a README block assigning a `TimeSpan` to a
  // `double` property shipped through all eight gates (2026-08-09) and the 3.0 migration guide's samples
  // are correct only because its author hand-built a probe project to compile them (2026-08-11).
  // Default-ON, with two annotations before the fence. Reach for the first: `<!-- compile-given: <decls>
  // -->` supplies the reader-side context a fragment assumes and keeps the block COMPILED — the
  // declarations are compiled with it, so a given naming a type that does not exist fails like any other
  // unresolved name, and it cannot wave a sample through. `<!-- compile-skip: <reason> -->` takes the block
  // out and is correct only where no context would help (a partial signature, a before/after pair, a menu
  // of alternatives) or where the context needed is a whole program. Both on one block is an error.
  //
  // IN `verify`, and the measurement is what decided it — the first draft of this comment argued the
  // opposite from the `consumer-smoke`/`memory-sweep` analogy ("it is a COMPILE, not a scan") before
  // anyone timed it. Measured 2026-08-11, immediately after `build`: **6.0s green**, twice, on a `verify`
  // that already spends minutes in `build` + 2309 tests + e2e. Nothing is restored that `build` has not
  // already restored (the scratch project adds no package reference — only ProjectReferences), so the
  // marginal cost is one incremental compile of ~43 tiny files. `consumer-smoke` and `memory-sweep` are
  // MINUTES and pull their own package feed; the analogy fails on the numbers.
  // The positive argument is stronger than the cost argument anyway: this is the only gate whose subject
  // can be broken by an edit to a `.md` file alone, and editing `.md` files is what a session does right
  // before it runs `verify`. Out of `verify` it would be run rarely — which is precisely how
  // `memory-sweep`'s reflection trip-wire goes unfired (`.claude/knowledge/pitfalls.md` §Refactoring).
  // `--list` prints the inventory without compiling (375ms); that is what `cli-entry.test.mjs` drives.
  case 'check-samples': {
    run('node', [path.join(repo, 'devtools', 'scripts', 'check-samples.mjs'), ...args]);
    break;
  }

  case 'verify': {
    // The single "am I done?" gate, stopping at the first failure. `steps` below IS the list — deliberately
    // not restated in prose here, and not counted here either: the previous version of this comment did both
    // and had silently drifted to "eleven checks" over a list of twelve, omitting `check-encoding` entirely
    // while the paragraph five lines down explained why that gate runs early. It also seeded the same wrong
    // count into CLAUDE.md, which then contradicted itself. The summary line printed below is derived from
    // `steps` for exactly this reason; a comment cannot be, so it says nothing a reader could check against
    // the array two lines away.
    //
    // The guard tests come FIRST, and cost milliseconds: everything after them is enforced BY the scripts
    // they cover, and those scripts fail permissively — a broken one reports a clean repository. Fail before
    // the guards run rather than trusting ten green lights that were never checked (TASKS.md Part 60).
    //
    // The documentation gates sit together, innermost last: `check-docs` asks whether the PROSE still says
    // what a decision settled, `check-links` whether its in-repo REFERENCES still resolve,
    // `check-api-vocabulary` asks the vocabulary question of the frozen public SURFACE (TASKS.md Part 61),
    // and `check-samples` COMPILES the fenced C# a consumer copies (Part 59).
    // `check-samples` must follow `build`, which is what makes it a 6s incremental compile rather than a
    // cold one.
    // `check-encoding` sits beside `check-sensitive` in spirit — both scan tracked text for damage nothing
    // else can see — but runs EARLY, because mojibake is cheapest to fix before a build has consumed the
    // file and while the edit that caused it is still the last thing that happened.
    const steps = [['test-devtools', []], ['build', []], ['check-warnings', []], ['check-packages', []],
      ['check-bundle', []], ['check-encoding', []], ['check-docs', []], ['check-links', []],
      ['check-counts', []], ['check-comments', []], ['check-api-vocabulary', []], ['check-samples', []], ['test', []], ['e2e', []],
      ['check-sensitive', ['--tree']]];
    let failed = null;
    for (const [step, extra] of steps) {
      console.log(`\n=== verify: ${step} ===`);
      const r = spawnSync('node', [path.join(repo, 'devtools', 'dev.mjs'), step, ...extra], { stdio: 'inherit', cwd: repo });
      if (r.status !== 0) { failed = step; process.exitCode = r.status ?? 1; break; }
    }
    if (failed) console.error(`\nverify: ✗ FAILED at ${failed}`);
    // Derived from `steps`, never hand-listed: the previous version was a literal and silently stopped
    // naming every gate the moment one was added — a summary that under-reports what ran is the same class
    // of quiet inaccuracy the gates themselves exist to prevent.
    else console.log(`\nverify: ✓ all ${steps.length} gates green (${steps.map(([s]) => s).join(' · ')})`);
    break;
  }

  case 'new-migration': {
    // scaffold the next FluentMigrator migration with a guaranteed-unique, monotonic yyyyMMddHHmm
    // number (reusing a number is silently skipped — the classic footgun the audit flagged).
    // The timestamp is self-describing, which a per-day NNNN sequence is not: two people adding a
    // migration on the same day without coordinating collide only if they do it in the same MINUTE,
    // and the collision is still resolved by the strictly-greater-than-max loop below.
    const raw = args[0];
    if (!raw || !/^[a-z][a-z0-9_-]*$/i.test(raw)) {
      console.error('usage: node devtools/dev.mjs new-migration <name>   (e.g. add-jobs-table)');
      process.exitCode = 1;
      break;
    }
    const dir = path.join(repo, 'src', 'Lyntai.Storage.Sqlite', 'Migrations');
    const nums = fs.readdirSync(dir).map((f) => (f.match(/^M(\d{12})_/) ?? [])[1]).filter(Boolean).map(Number);
    const max = nums.length ? Math.max(...nums) : 0;
    const d = new Date();
    const pad = (v) => String(v).padStart(2, '0');
    let num = Number(
      `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}${pad(d.getHours())}${pad(d.getMinutes())}`);
    // strictly greater than every existing number — never reuse one. Two migrations in the same minute
    // push the second past a real clock value (…1360); uniqueness and ordering are what matter here, and
    // both hold.
    while (num <= max) num++;
    const pascal = raw.split(/[-_]/).filter(Boolean).map((s) => s[0].toUpperCase() + s.slice(1)).join('');
    const cls = `M${num}_${pascal}`;
    const file = path.join(dir, `${cls}.cs`);
    if (fs.existsSync(file)) { console.error(`already exists: ${file}`); process.exitCode = 1; break; }
    fs.writeFileSync(file, [
      'using FluentMigrator;',
      '',
      'namespace Lyntai.Storage.Sqlite.Migrations;',
      '',
      "// TODO: replace Feature below with THIS migration's StorageFeature (KeyValue, Conversation, Memory,",
      '// Score, Trace, PromptVersion, Jobs, Governance, CuratedMemory) — the placeholder does not compile on',
      '// purpose. Both tags are load-bearing: the feature tag is what a SUBSET pass requests, AllTag is what',
      '// the default StorageFeature.All pass requests, and an UNTAGGED migration runs under EVERY feature',
      '// set — so a domain the app disabled would still land its table.',
      '/// <summary>TODO: what this migration does.</summary>',
      `[Migration(${num})]`,
      '[Tags(nameof(StorageFeature.Feature), StorageFeatures.AllTag)]',
      `public sealed class ${cls} : Migration`,
      '{',
      '    public override void Up()',
      '    {',
      '        // TODO. Prefix every object lyntai_. snake_case columns. Composite PK + FK inline at',
      "        // Create.Table (SQLite can't ALTER ADD CONSTRAINT). Wrap 0..1/double columns in",
      '        // CAST(x AS REAL) when you SELECT them. Searchable text? add an FTS5 trigram',
      "        // external-content mirror + AFTER INSERT/DELETE/UPDATE triggers (emit 'delete' rows on",
      '        // delete AND update) + an in-migration backfill — see M202607280003_Memory and',
      '        // .claude/knowledge/storage.md.',
      '    }',
      '',
      '    public override void Down()',
      '    {',
      '        // TODO: reverse Up.',
      '    }',
      '}',
      '',
    ].join('\n'));
    console.log(`created ${path.relative(repo, file).replaceAll('\\', '/')} — number ${num} (unique, monotonic).`);
    console.log("Next: name the [Tags] feature (the placeholder doesn't compile — an untagged migration would");
    console.log('run under every feature set), define the table, then a store + its I*Store impl.');
    console.log('See .claude/skills/add-migration.');
    break;
  }

  default:
    // DERIVED from this file's own `case` labels, never hand-listed. The literal it replaces had drifted to
    // 24 of 30 commands — every memory sweep except `memory-sweep`, plus `check-encoding` the day it was
    // added — so `dev.mjs` with no argument, which is what CLAUDE.md calls "the authoritative list",
    // silently stopped being one. Same defect and same fix as `verify`'s summary line.
    console.log(`usage: node devtools/dev.mjs <${commandNames().join('|')}>`);
    process.exitCode = cmd ? 1 : 0;
}

/**
 * Every command this file dispatches, read out of its own source.
 *
 * Self-reading rather than a maintained array because an array is a second list that can drift from the
 * switch exactly the way the usage string did. The switch IS the registry; this makes it readable.
 * `dedupe` matters because a few commands share a case body through fall-through.
 */
export function commandNames(source = null) {
  const src = source ?? fs.readFileSync(fileURLToPath(import.meta.url), 'utf8');
  return [...new Set([...src.matchAll(/^\s*case '([a-z][a-z0-9-]*)':/gm)].map((m) => m[1]))];
}
