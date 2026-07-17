using BenchmarkDotNet.Running;

// `node devtools/dev.mjs bench` → runs all benchmarks; pass a filter to narrow, e.g.
//   node devtools/dev.mjs bench -- --filter *Router*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// (Program must be a partial class BenchmarkSwitcher can reflect over from top-level statements.)
public partial class Program;
