// consumer-smoke — prove the PACKAGES work, from the outside, the way a consumer meets them.
//
// Everything else in `verify` tests the repo: project references, one solution, one build. That never exercises
// what is actually shipped — the nuspecs, the dependency groups, whether the bundle restores, whether the public
// API is reachable through a PackageReference. This packs to a scratch feed under a throwaway version, restores a
// fresh console app against it, compiles against the real surface, and runs it.
//
// It earned its place: run by hand once before 2.0.1 it found two defects nothing else could — an empty symbol
// package on the new bundle (no assembly, so no PDB, offered to nuget.org's symbol validation for nothing) and a
// backend reporting AuthFailed instead of NotConfigured when unconfigured, which the new cooldown then punished.
//
// NOT part of `verify`: it packs 11 packages and restores a project, so it is minutes, not seconds. Run it before
// a release, or after touching packaging (a csproj, a dependency, the bundle).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const work = path.join(repo, 'devtools', '_consumer-smoke');   // git-ignored scratch, per the repo convention
const version = '9.9.9-smoke';
const step = (m) => console.log(`consumer-smoke: ${m}`);
const sh = (exe, argv, cwd) => spawnSync(exe, argv, { cwd, encoding: 'utf8', shell: false });

const bail = (msg, extra) => {
  console.error(`consumer-smoke: ✗ ${msg}`);
  if (extra) console.error(extra.split('\n').filter((l) => /error|warn|Unable|not found/i.test(l)).slice(0, 12).join('\n'));
  process.exit(1);
};

fs.rmSync(work, { recursive: true, force: true });
fs.mkdirSync(path.join(work, 'app'), { recursive: true });

// ---- 1. pack every package under a throwaway version --------------------------------------------------
// A distinct version matters: packing as the CURRENT version lets NuGet serve an already-cached copy of the
// same version from a previous real release, and the smoke then silently tests the OLD package. That happened.
step(`packing ${version} to a scratch feed`);
const packed = sh('dotnet', ['pack', 'Lyntai.slnx', '-c', 'Release', `-p:Version=${version}`,
  '-o', path.join(work, 'feed'), '-v', 'quiet'], repo);
if (packed.status !== 0) bail('pack failed', packed.stdout);

const feed = fs.readdirSync(path.join(work, 'feed'));
const nupkgs = feed.filter((f) => f.endsWith('.nupkg'));
const snupkgs = feed.filter((f) => f.endsWith('.snupkg'));
step(`${nupkgs.length} packages, ${snupkgs.length} symbol packages`);

// ---- 1b. evict this version from the GLOBAL package cache ---------------------------------------------
// The throwaway version is a constant, so the first run of it populates ~/.nuget/packages/lyntai.*/9.9.9-smoke —
// and NuGet never re-extracts a version it already has. Every later run then silently restores the FIRST run's
// packages and reports success about code it never compiled against. Caught when a newly-added public method
// was "missing" from a package that demonstrably contained it. Evict by id+version rather than isolating
// NUGET_PACKAGES entirely, so third-party dependencies stay cached and the smoke stays minutes, not tens.
const locals = sh('dotnet', ['nuget', 'locals', 'global-packages', '--list'], repo);
const globalPackages = (locals.stdout ?? '').split('\n')
  .map((l) => l.replace(/^.*?global-packages:\s*/i, '').trim()).find((l) => l.length > 0);
if (!globalPackages || !fs.existsSync(globalPackages))
  bail('could not locate the NuGet global-packages folder', locals.stdout);

let evicted = 0;
for (const id of nupkgs.map((f) => f.slice(0, f.length - `.${version}.nupkg`.length).toLowerCase())) {
  const cached = path.join(globalPackages, id, version);
  if (!fs.existsSync(cached)) continue;
  fs.rmSync(cached, { recursive: true, force: true });
  evicted++;
}
step(`evicted ${evicted} stale ${version} package(s) from the global cache ✓`);

// ---- 2. every symbol package must carry a PDB ---------------------------------------------------------
// An empty .snupkg is pushed automatically alongside its .nupkg and offered to symbol validation for nothing.
for (const s of snupkgs) {
  const listing = sh('tar', ['-tf', path.join(work, 'feed', s)], repo);   // bsdtar reads zips on win/git-bash
  if (listing.status === 0 && !/\.pdb/.test(listing.stdout))
    bail(`${s} contains no PDB — set <IncludeSymbols>false</IncludeSymbols> on a package that ships no assembly`);
}
step('every symbol package carries a PDB ✓');

// ---- 3. a fresh consumer app, restored from the feed --------------------------------------------------
const app = path.join(work, 'app');
fs.writeFileSync(path.join(app, 'nuget.config'), `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="smoke" value="../feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
`);
fs.writeFileSync(path.join(app, 'app.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Lyntai" Version="${version}" />
    <PackageReference Include="Lyntai.Storage.Sqlite" Version="${version}" />
    <PackageReference Include="Lyntai.Generation" Version="${version}" />
  </ItemGroup>
</Project>
`);
// Deliberately exercises the BUNDLE plus two opt-in packages, and asserts behaviour rather than just compiling:
// an unconfigured backend must report a verdict a host can act on, never throw and never invent a result.
//
// Lyntai.Generation is here because it is the package a consumer is MOST likely to meet on its own: it is not in
// the bundle (D32/D34), and it is the only one carrying a dependency of its own that the bundle never resolves —
// so a wrong dependency group in its nuspec would show up nowhere else.
fs.writeFileSync(path.join(app, 'Program.cs'), `using Lyntai;
using Lyntai.Agents;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Generation.Routing;
using Lyntai.Llm;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLyntai(cfg => cfg
    .AddOllamaProvider(defaultModel: "qwen3:4b")
    .UseSqliteStorage(Path.Combine(Path.GetTempPath(), $"lyntai-smoke-{Guid.NewGuid():N}.db"))
    // the per-backend shim, through the PACKAGE — it registers a named HttpClient, which is what needs
    // Microsoft.Extensions.Http to have travelled with Lyntai.Generation's nuspec
    .AddOpenAiImageProvider(new OpenAiImageOptions { BaseUrl = "", ApiKey = null })
    .UseDefaultGenerationCandidates("openai-images"));
using var sp = services.BuildServiceProvider();

// the front door and its decorator chain resolve through the PACKAGE graph
var client = sp.GetRequiredService<ILlmClient>();
if (client is null) throw new Exception("no ILlmClient");

// the tool contract lives in Core and is reachable from the bundle
_ = sp.GetServices<ITool>().Count();

// storage came from an opt-in package alongside the bundle
if (sp.GetRequiredService<IKeyValueStore>() is null) throw new Exception("no IKeyValueStore");

// the generation domain wires and ROUTES through the package graph, and an unconfigured backend reports a
// verdict a host can act on rather than throwing or inventing an artifact
var render = await sp.GetRequiredService<IGenerationRouter>().GenerateAsync(
    [new GenerationCandidate("openai-images")],
    new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" });
if (render.Verdict != GenerationVerdict.NotConfigured)
    throw new Exception($"unconfigured image backend reported {render.Verdict}, expected NotConfigured");

// the named factories are reachable from the package and bake the role in (D35)
if (GenerationInput.Init(new byte[] { 1 }, "image/png").Role != GenerationInputRoles.Init)
    throw new Exception("GenerationInput.Init did not carry its role");

Console.WriteLine("CONSUMER SMOKE OK");
`);

step('restoring the app against the scratch feed');
const restored = sh('dotnet', ['restore', '-v', 'quiet'], app);
if (restored.status !== 0) bail('restore failed — a nuspec or dependency group is wrong', restored.stdout);

step('building against the shipped public surface');
const built = sh('dotnet', ['build', '-c', 'Release', '-v', 'quiet', '--no-restore'], app);
if (built.status !== 0) bail('the consumer app does not compile against the packages', built.stdout);

step('running it');
const ran = sh('dotnet', ['run', '-c', 'Release', '--no-build', '--no-restore'], app);
if (ran.status !== 0 || !/CONSUMER SMOKE OK/.test(ran.stdout ?? ''))
  bail('the consumer app failed at runtime', (ran.stdout ?? '') + (ran.stderr ?? ''));

fs.rmSync(work, { recursive: true, force: true });
console.log('consumer-smoke: ✓ the packages restore, compile and run for a fresh consumer');
