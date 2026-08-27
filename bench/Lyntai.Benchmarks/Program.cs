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

// `node devtools/dev.mjs memory-verification` → --verification. The only mechanism aimed at PollutionRate,
// and the only 3.0 seam with no sweep until 2026-08-15. Every other recall-quality figure this repository
// publishes is MODEL-FREE, so this is the first measurement of what putting a model in the loop is worth.
// Measures the MECHANISM'S CEILING with a perfect judge, never a model's accuracy. See MemoryVerificationSweep.
if (args.Contains("--verification"))
    return await MemoryVerificationSweep.RunAsync();

// `node devtools/dev.mjs memory-fan` → --fan. ACT-R's fan effect as a fused RRF signal. Nothing consulted
// GraphNode.Degree before 2026-08-15, so a hub spread exactly as much as a leaf; this measures whether
// penalising it helps, because a ranking default moves on a measurement and never on an argument (D49/D54).
if (args.Contains("--fan"))
    return await MemoryFanSweep.RunAsync();

// `node devtools/dev.mjs memory-enrichment` → --enrichment. WHY registering an embedder costs recall
// quality: the two WRITE-TIME mechanisms (similarity linking, novelty→salience) varied independently, which
// nothing had done. The ONLY sweep here that calls a real model rather than a deterministic double, and it
// EXITS rather than substituting one — the arm it replaces was measured through a bag-of-words fake in which
// "semantic similarity" is word overlap, which is why those numbers were withdrawn (TASKS.md Part 69).
if (args.Contains("--enrichment"))
    return await MemoryEnrichmentSweep.RunAsync();

// `node devtools/dev.mjs memory-salience-weight` → --salience-weight. Whether the `many-candidates`
// regression salience ships with is recoverable by BOUNDING its ranking voice. The second sweep here that
// needs a REAL model, and for a sharper reason than cost: without an embedder salience declines on every
// write, and RRF ranks by competition (D82), so a uniformly-absent signal contributes the same constant at
// every weight — the curve would be flat as an ARTIFACT with every control green.
// `--languages` is the SECOND run: the decisive pair (silent vs shipped) across all five CorpusLanguage
// arms, because run one measured space-separated English — the friendliest tokenization the library
// supports — and a ranking finding taken only there is a finding about the easy case.
if (args.Contains("--salience-weight"))
    return await MemorySalienceWeightSweep.RunAsync(args.Contains("--languages"));

// `node devtools/dev.mjs memory-importance` → --importance. WHAT the salience signal measures, rather than
// how loud it is: the shipped novelty policy against a perfect importance ORACLE, with salience-off as the
// control. D89 priced novelty and took its ranking voice to zero; that is not a finding about importance,
// and the two come apart precisely where it matters — novelty is monotone in "unlike anything already
// stored", so sustained significance DECAYS on that axis as it is confirmed while a one-off triviality reads
// as maximal. Ranking stays at the shipped SalienceWeight = 0, so this prices SURVIVAL (decay resistance +
// store admission), which is D45's actual claim for what salience means.
// Needs a real embedder, and for D89's reason: without one the novelty arm silently becomes the
// salience-off arm and the table reads as "importance wins" having measured nothing.
if (args.Contains("--importance"))
    return await MemoryImportanceSweep.RunAsync(args.Contains("--languages"));

// `node devtools/dev.mjs memory-density` → --density. Whether a CORRECTION and a RECURRENCE separate on
// SalienceContext.SimilarCount at all. This is the cheapest possible refutation of the gist tier: if they
// do not separate here, on authored fixtures with a real embedder, nothing downstream can work.
if (args.Contains("--density"))
    return await MemoryDensitySweep.RunAsync(args.Contains("--languages"));

// `node devtools/dev.mjs memory-support` → --gist-support. Which rule selects a recurring cluster's CURRENT
// regime: the combining form over member retrievability, and whether a small generative model beats it.
// `--screen` runs the size ladder alone on one shape, which is the arm most likely to change what the gist
// tier ships and the cheapest to kill.
if (args.Contains("--gist-support"))
    return await MemoryGistSupportSweep.RunAsync(args);

// `node devtools/dev.mjs memory-scale` → --scale. The one blind spot docs/memory.md §8 concedes outright:
// nothing in this subsystem had exceeded a few hundred entries. The ONLY sweep here whose subject is COST
// rather than recall quality — which is also why it runs sequentially where the others fan out, since
// contention biases a latency and cannot bias a rate.
// `--sizes 1000,10000` overrides the 1k/10k/100k ladder, so the harness can be exercised in seconds.
if (args.Contains("--scale"))
    return await MemoryScaleSweep.RunAsync(args);

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
