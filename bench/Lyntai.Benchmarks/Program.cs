using BenchmarkDotNet.Running;
using Lyntai.Benchmarks;

// `node devtools/dev.mjs memory-sweep` → --sweep, handled HERE, before the switcher ever sees the args.
// The sweep measures miss rate / pollution rate across policy arms and corpus shapes — not wall-clock time
// per operation, which is all BenchmarkSwitcher can report — so it is its own command, not a [Benchmark].
// `node devtools/dev.mjs bench` never passes --sweep, so the BenchmarkSwitcher path below is unchanged.
if (args.Contains("--sweep"))
    return await MemoryPolicySweep.RunAsync();

// `node devtools/dev.mjs memory-spacing` → --spacing, handled the same way and for the same reason: it is a
// SENSITIVITY study over one DsrOptions constant, reporting recall quality rather than wall-clock time.
if (args.Contains("--spacing"))
    return await MemorySpacingSweep.RunAsync();

// `node devtools/dev.mjs memory-reinforcement` → --reinforcement. Isolates the one question --spacing
// structurally could not answer: reinforcement itself, or only law 3's r-dependence?
if (args.Contains("--reinforcement"))
    return await MemoryReinforcementSweep.RunAsync();

// `node devtools/dev.mjs memory-bounded` → --bounded. Does limiting the compounding beat removing growth?
// The question the other studies converge on, and the one that decides a shipped default.
if (args.Contains("--bounded"))
    return await MemoryBoundedGrowthSweep.RunAsync();

// `node devtools/dev.mjs memory-salience` → --salience. Salience ships ON and has never been measured once;
// every prior study asserted its absence as a control. See MemorySalienceSweep.
if (args.Contains("--salience"))
    return await MemorySalienceSweep.RunAsync();

// `node devtools/dev.mjs memory-language` → --language. Every recall figure this repository publishes was
// measured on English, space-separated text; this measures the same corpus in Chinese. See
// MemoryLanguageSweep and docs/DECISIONS.md D55.
if (args.Contains("--language"))
    return await MemoryLanguageSweep.RunAsync();

// `node devtools/dev.mjs memory-annotation` → --annotation. Cluster recall sits at the no-graph floor and is
// unreachable by any ranking policy; subject annotation is the only mechanism that addresses it. Measures
// the MECHANISM'S CEILING with a perfect annotator, never a model's accuracy. See MemoryAnnotationSweep.
if (args.Contains("--annotation"))
    return await MemoryAnnotationSweep.RunAsync();

// A `--evidence` study lived here on 2026-08-12 and was removed with the `GraphMemoryOptions.ReinforceOn`
// seam it drove — see that option's own reverted-here note. Its FINDING survives in TASKS.md Part 64: the
// engine's reinforcement conflates an age RESET with a stability GROWTH, and those pull in opposite
// directions, so a single gate over both could not express the configuration the evidence favours.

// `node devtools/dev.mjs bench` → runs all benchmarks; pass a filter to narrow, e.g.
//   node devtools/dev.mjs bench -- --filter *Router*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;

// (Program must be a partial class BenchmarkSwitcher can reflect over from top-level statements.)
public partial class Program;
