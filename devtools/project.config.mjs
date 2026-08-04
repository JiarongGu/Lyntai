// project.config.mjs — the ONLY project-specific inputs for the devtools dispatcher.
//
// The dispatcher (dev.mjs) and the scripts under scripts/ are otherwise generic (pattern shared with the
// sibling projects — Gatherlight/Vidora/Sonora). To reuse this toolkit elsewhere, copy devtools/ and edit
// THIS file.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Version is sourced from src/Directory.Build.props (the single source the .NET packages also use), so the
// package versions never drift from what the built assemblies report. Padded to a full 3-part semver.
const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const rawVersion =
  (readFileSync(join(repoRoot, 'src', 'Directory.Build.props'), 'utf8')
    .match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/) || [])[1] || '0.0.0';
/** Pad a numeric version to exactly major.minor.patch (`1` → `1.0.0`, `1.0` → `1.0.0`, `1.2.3.4` → `1.2.3`). */
export const toSemver = (v) => {
  const [core, ...rest] = String(v).trim().split(/([-+])/); // keep any -pre/+build suffix
  const parts = core.split('.').filter(Boolean).slice(0, 3);
  while (parts.length < 3) parts.push('0');
  return parts.join('.') + rest.join('');
};

export default {
  name: 'Lyntai',
  /** Product version (major.minor.patch) — read from src/Directory.Build.props; the single source. */
  version: toSemver(rawVersion),
  /** Solution built/tested/packed by the dispatcher. */
  solution: 'Lyntai.slnx',
  /** xUnit test project. */
  testProject: 'tests/Lyntai.Tests',
  /** Sample console app the e2e harness boots against the provider-stub. */
  playgroundProject: 'samples/Lyntai.Playground',
  /** BenchmarkDotNet project (`bench` runs it in Release; extra args pass to the switcher). */
  benchProject: 'bench/Lyntai.Benchmarks',
  /** Packable library projects (`pack` runs `dotnet pack` over each → publish/packages/). */
  packableProjects: [
    'src/Lyntai.Core',
    'src/Lyntai.Bundle',
    'src/Lyntai.Providers.Default',
    'src/Lyntai.Storage.Sqlite',
    'src/Lyntai.Storage.InMemory',
    'src/Lyntai.Storage.Postgres',
    'src/Lyntai.Providers.ExtensionsAi',
    'src/Lyntai.Providers.Local',
    'src/Lyntai.Tools.Mcp',
    'src/Lyntai.Tools.Mcp.Hosting',
    'src/Lyntai.Secrets.Dpapi',
  ],
  /**
   * The `Lyntai` bundle's dependency budget, enforced by `dev.mjs check-bundle` (part of `verify`).
   *
   * Membership in the bundle is not free: an untrimmed `dotnet publish` copies the WHOLE dependency graph
   * and analyses nothing, so every package here lands in every bundle consumer's output folder whether they
   * call it or not. The `Microsoft.Extensions.*` band is auto-allowed — those ship on the runtime's own
   * version band and any DI app already has them. Anything OUTSIDE that band is a deliberate decision that
   * must be listed here, which is why this list is meant to stay nearly empty.
   *
   * Adding an id = accepting its full size for every consumer of the one-line install. See
   * `docs/DECISIONS.md` D32 for the rule and the current justification of each entry.
   */
  bundle: {
    project: 'src/Lyntai.Bundle',
    allowedThirdParty: [
      // 1.19 MB, and it drags Microsoft.Extensions.AI.Abstractions (656 KB). In by explicit exception:
      // MCP is near-universal for the agentic consumers this library targets (D31/D32).
      'ModelContextProtocol.Core',
    ],
  },
};
