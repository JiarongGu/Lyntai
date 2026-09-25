// The `dev.mjs` command roster — DATA, and free of side effects, so a gate IMPORTS it rather than reading
// the dispatcher's source with a regex. `dev.mjs` dispatches over it; `check-dev-loop` renders CLAUDE.md's
// table from it (each description is `devLoopCommands` in `project.config.mjs`); `check-counts` counts
// `VERIFY_STEPS`; the guard tests prove every script-backed step's entry point fires. What each gate is FOR
// is `docs/GATES.md`.

/**
 * Every command, in the order `node devtools/dev.mjs` lists them and CLAUDE.md's table shows them. Exactly
 * one of: `script` — `devtools/scripts/<script>.mjs`, given the command's arguments; `bench` — the bench
 * project run with that flag; `builtin` — dispatched by `dev.mjs` itself.
 */
export const COMMANDS = [
  { name: 'build', builtin: true },
  { name: 'check-warnings', script: 'check-warnings' },
  { name: 'check-bundle', script: 'check-bundle' },
  { name: 'check-packages', script: 'check-packages' },
  { name: 'new-package', script: 'new-package' },
  { name: 'consumer-smoke', script: 'consumer-smoke' },
  { name: 'test', builtin: true },
  { name: 'test-devtools', script: 'test-devtools' },
  { name: 'playground', builtin: true },
  { name: 'bench', builtin: true },
  { name: 'memory-sweep', bench: '--sweep' },
  { name: 'memory-bounded', bench: '--bounded' },
  { name: 'memory-salience', bench: '--salience' },
  { name: 'memory-longmemeval', bench: '--longmemeval' },
  { name: 'memory-locomo', bench: '--locomo' },
  { name: 'memory-language', bench: '--language' },
  { name: 'memory-annotation', bench: '--annotation' },
  { name: 'memory-annotation-drift', bench: '--annotation-drift' },
  { name: 'memory-consolidation', bench: '--consolidation' },
  { name: 'memory-affect', bench: '--affect' },
  { name: 'storage-scan', bench: '--storage-scan' },
  { name: 'memory-verification', bench: '--verification' },
  { name: 'memory-fan', bench: '--fan' },
  { name: 'memory-enrichment', bench: '--enrichment' },
  { name: 'memory-salience-weight', bench: '--salience-weight' },
  { name: 'memory-importance', bench: '--importance' },
  { name: 'memory-density', bench: '--density' },
  { name: 'memory-support', bench: '--gist-support' },
  { name: 'memory-scale', bench: '--scale' },
  { name: 'memory-contention', script: 'memory-contention' },
  { name: 'memory-decision', script: 'memory-decision' },
  { name: 'tool-affordance', script: 'tool-affordance' },
  { name: 'install-hooks', builtin: true },
  { name: 'check-sensitive', script: 'check-sensitive' },
  { name: 'decisions-index', script: 'decisions-index' },
  { name: 'rerank-screen', script: 'rerank-screen' },
  { name: 'embed-screen', script: 'embed-screen' },
  { name: 'locomo-pair', script: 'locomo-pair' },
  { name: 'doctor', builtin: true },
  { name: 'check-version', script: 'check-version-bump' },
  { name: 'changelog', builtin: true },
  { name: 'release-notes', script: 'release-notes' },
  { name: 'pack', builtin: true },
  { name: 'nuget-unlist', script: '../nuget-unlist' },
  { name: 'e2e', script: 'e2e/run' },
  { name: 'check-docs', script: 'check-docs' },
  { name: 'check-links', script: 'check-links' },
  { name: 'check-counts', script: 'check-counts' },
  { name: 'check-comments', script: 'check-comments' },
  { name: 'check-decisions', script: 'check-decisions' },
  { name: 'check-archive', script: 'check-archive' },
  { name: 'check-backlog', script: 'check-backlog' },
  { name: 'check-pitfalls', script: 'check-pitfalls' },
  { name: 'check-options', script: 'check-options' },
  { name: 'check-measurements', script: 'check-measurements' },
  { name: 'check-dev-loop', script: 'check-dev-loop' },
  { name: 'check-decision-claims', script: 'check-decision-claims' },
  { name: 'check-encoding', script: 'check-encoding' },
  { name: 'check-tautology', script: 'check-tautology' },
  { name: 'check-api-vocabulary', script: 'check-api-vocabulary' },
  { name: 'check-samples', script: 'check-samples' },
  { name: 'verify', builtin: true },
  { name: 'new-migration', script: 'new-migration' },
];

/**
 * What `verify` runs, in order, each with its arguments, stopping at the first failure. The guard tests run
 * FIRST: every gate after them fails permissively when broken. `check-warnings` is the one solution build
 * (`--no-incremental`, which it needs to see every warning); `check-samples` follows it and compiles
 * incrementally on top.
 */
export const VERIFY_STEPS = [
  ['test-devtools', []], ['check-warnings', []], ['check-packages', []], ['check-bundle', []],
  ['check-encoding', []], ['check-docs', []], ['check-links', []], ['check-counts', []],
  ['check-comments', []], ['check-decisions', []], ['check-archive', []], ['check-backlog', []],
  ['check-pitfalls', []], ['check-measurements', []], ['check-decision-claims', []], ['check-dev-loop', []],
  ['check-options', []], ['check-api-vocabulary', []], ['check-tautology', []], ['check-samples', []],
  ['test', []], ['e2e', []], ['check-sensitive', ['--tree']],
];
