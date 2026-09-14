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
    'src/Lyntai.Providers.LlamaSharp',
    'src/Lyntai.Tools.Mcp',
    'src/Lyntai.Tools.Mcp.Hosting',
    'src/Lyntai.Secrets.Dpapi',
    'src/Lyntai.Providers.Onnx',
    'src/Lyntai.Generation',
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
   * `docs/DECISIONS.md` D26 for the rule and the current justification of each entry.
   */
  bundle: {
    project: 'src/Lyntai.Bundle',
    allowedThirdParty: [
      // 1.19 MB, and it drags Microsoft.Extensions.AI.Abstractions (656 KB). In by explicit exception:
      // MCP is near-universal for the agentic consumers this library targets (D25/D26).
      'ModelContextProtocol.Core',
    ],
  },

  /**
   * RETIRED PUBLIC-API NAMES — identifiers a deliberate decision replaced, which must not reappear in the
   * frozen public surface. Enforced by `dev.mjs check-api-vocabulary` against the committed API baselines
   * (`tests/Lyntai.Tests/Api/Baselines/*.txt`), and part of `verify`.
   *
   * The gap this closes sits BETWEEN two gates that each work correctly: `check-docs` deliberately excludes
   * `src/`, so it never reads a parameter name, and the API-surface baseline RECORDS parameter names
   * without judging them — it reports THAT a name changed, never THAT a name should have. So a stale name
   * round-trips cleanly through the one gate whose entire job is noticing API changes. Measured cost
   * (TASKS.md Part 61, docs/DECISIONS.md D47): `GraphMemoryEngine(ageClocks:)`, `(appraisers:)` and
   * `ModulatedRetrievability(modulators:)` kept the three words that decision retired all the way to the
   * eve of the 3.0 freeze. A human review caught all three; no gate did. Named arguments are
   * source-compatible public surface, so after a freeze each costs a major version.
   *
   * WHY THIS IS ITS OWN REGISTRY AND NOT `retiredTerms`. That one is written for PROSE — its entries are
   * claim shapes (`available,? (?:but )?not (?:the )?default`) and qualified namespace paths, both of which
   * are meaningless against a baseline, which is identifiers and signatures only. The two registries also
   * want opposite matching: prose needs loose, sentence-spanning regexes; a baseline needs the strictest
   * possible whole-identifier equality.
   *
   * MATCHING IS WHOLE-IDENTIFIER, NEVER SUBSTRING. Each baseline line is tokenized into identifiers:
   * `names` is set membership, `pattern` is a regex anchored to the WHOLE token (an identifier SHAPE).
   * This is what lets `modulators`/`IRetentionModulator` be retired while `Lyntai.Memory.Modulation` and
   * `ModulatedRetrievability` stay live, and `IMemoryClock` be retired while `InactivityClock` and every
   * genuine `Func<DateTimeOffset> clock` parameter stay live. A substring rule would fire on all of them,
   * and a gate that cries wolf on a deliberate decision gets an exclusion added and then rots.
   *
   * Each entry: the retired identifiers, what to use instead, and why — the same teach-don't-just-refuse
   * shape `retiredTerms` uses. Add one whenever a decision renames something on the public surface.
   *
   * ESCAPE HATCH: a baseline is GENERATED, so `drift-ok` cannot live in it — the next regeneration would
   * erase it. An escape is instead an `allow: [{ signature, why }]` on the rule it silences: the EXACT
   * baseline line, so it expires by itself when that signature changes, and a `why`, so it is never a
   * silent exclusion. An allowance that matches nothing is a FAILURE — a rotting exclusion hides the next
   * occurrence of the same mistake.
   *
   * The limit, stated rather than oversold: this catches the reintroduction of an exact retired
   * identifier, not every descendant of a retired word. A method named for the verb form of a retired type
   * is not that type's name, and no rule here would flag it.
   */
  /**
   * Documents `check-links` may not hold to "every in-repo reference resolves", and WHY.
   *
   * The gate exists because untracking the ranking × forgetting measurement record under D43 left six
   * dangling references in maintained state — README, the design contract, DECISIONS — and every gate
   * stayed green while a reader found them. `docs/superpowers/INDEX.md` already ENDS its archiving
   * procedure with "check nothing dangles"; this makes that step enforceable rather than remembered.
   *
   * An allowance is per-FILE and never per-path: the case it covers is a whole document whose paths were
   * correct on the day it was written, which is the same rationale `HISTORICAL` carries in check-docs. A
   * single deliberate mention inside an otherwise-maintained document gets `drift-ok` on its line instead.
   *
   * AN ALLOWANCE THAT MATCHES NOTHING IS A FAILURE, exactly as in `retiredApiNames`: once a document's
   * last stale reference is repaired the allowance is a hole nobody can see expiring, and the next
   * genuine dangling reference in that file would go unreported forever.
   */
  // EMPTY on purpose, and the reason is worth keeping so the next dangling reference is not waved through
  // as "there used to be an allowance". The one entry here covered
  // `docs/2026-08-04-generation-platform-plan.md`, whose paths below its 2026-08-05 status banner are the
  // layout AS FIRST WRITTEN — a faithful record of how the core was built, superseded twice by D25. D125
  // moved that file into `check-docs`' HISTORICAL list for the same reason, and `check-links` reads
  // HISTORICAL from there, so the file is no longer scanned and the allowance matched nothing — which this
  // registry treats as a failure by design. The COST, stated rather than buried: that document's still-live
  // half (GEN-VERIFY/GEN6/GEN7 execute from it) is now outside both gates until it moves to local/.
  staleReferenceAllowances: [],

  retiredApiNames: [
    {
      // D136. `LlmVerdictException` is deliberately absent: it survives, being the LLM front door's own
      // exception type, and whole-identifier matching keeps it live without an allowance.
      names: [
        'LlmVerdict',
        'GenerationVerdict',
        'LlmVerdictClassifier',
        'GenerationVerdictClassifier',
        'LlmVerdictExtensions',
      ],
      use: '`Lyntai.Lifecycle.ProviderVerdict` / `ProviderVerdictClassifier` / `ProviderVerdictExtensions`',
      why: 'the media enum was the LLM one minus ContextWindowExceeded, member for member, with a 101-line '
        + 'translation between them whose missing arm reported a capability gap as a hard failure for a '
        + 'whole release (D136)',
    },
    {
      // D135. `OpenAiPayload` and the `HttpDialect.OpenAi` MEMBER are deliberately absent: they name
      // OpenAI's actual schema, which is one of four dialects and correctly called that.
      names: [
        'AddOpenAiCompatible',
        'AddOpenAiCompatibleProvider',
        'OpenAiCompatibleProvider',
        'OpenAiCompatibleOptions',
        'OpenAiCompatibleBuilderExtensions',
        'OpenAiEmbeddingsTransport',
        'OpenAiEndpoint',
        'OpenAiFlavor',
        'OpenAiHttp',
      ],
      use: '`AddHttpProvider` / `HttpModelProvider` / `HttpModelOptions` / `HttpDialect` in '
        + '`Lyntai.Providers.Http`',
      why: 'the family is not OpenAI — the Ollama dialect posts /api/chat and /api/embed, which that '
        + 'vendor documents as NOT OpenAI-compatible, so the name was false for one dialect and centred a '
        + 'vendor for all four (D135)',
    },
    {
      // D134. `AddProvider`, `AddEmbeddingProvider` and `AddGenerationProvider` are deliberately NOT here:
      // those take a FACTORY and are the generic primitives, where `Provider` is the noun rather than a
      // suffix on a backend's name. Whole-identifier equality keeps them live without an allowance.
      names: [
        'AddAutomatic1111Provider',
        'AddAzureOpenAiProvider',
        'AddClaudeCliProvider',
        'AddCodexCliProvider',
        'AddComfyUiProvider',
        'AddExtensionsAiProvider',
        'AddFalProvider',
        'AddLlamaProvider',
        'AddLlamaSharpProvider',
        'AddLocalDiffusionProvider',
        'AddModel2VecProvider',
        'AddOllamaProvider',
        'AddOnnxProvider',
        'AddHttpProviderProvider',
        'AddOpenAiImageProvider',
        'AddOpenAiProvider',
        'AddOpenRouterProvider',
      ],
      use: 'the same name without the suffix — `AddHttpProvider`, `AddOllama`, `AddOnnx`, `AddClaudeCli`',
      why: 'every one of these returns an IModelProvider, so a suffix carried by all of them distinguishes '
        + 'none of them — the objection that retired the name IProvider, one layer out (D134)',
    },
    {
      // D130. `Kinds` and `Embed` are whole-identifier tokens, so `ProviderKinds`, `GenerationKinds` and
      // every `EmbedAsync` stay live without an allowance — which is the point of the tokenizing rule.
      names: ['Kinds', 'Embed'],
      use: '`ProviderCapabilities.Accepts` / `.Produces`, and `ProviderKinds.Vector` for what an embedder '
        + 'PUTS OUT',
      why: 'a backend is accepts -> produces delivered some way; embedding was the one ProviderOperation '
        + 'member whose own doc had to explain that it produced nothing of its Kinds (D130)',
    },
    {
      names: [
        'AddHttpProviderEmbedder', 'AddHttpProviderEmbeddings',
        'OpenAiCompatibleEmbedderOptions', 'HttpEmbedder', 'EmbedderHttpClientName',
      ],
      use: '`AddHttpProvider` with `HttpModelOptions.Embeddings` set (and `Chat = null` '
        + 'for a host that serves no chat); the wire shape is the internal `HttpEmbeddingsTransport`',
      why: 'a second Add* method for the same backend IS the chat-vs-embedder split, re-entering through '
        + 'the one surface a consumer types. One host, one registration, routes as configuration (D132)',
    },
    {
      names: [
        'AddStaticEmbedder', 'StaticEmbedder', 'StaticEmbedderOptions', 'StaticBuilderExtensions',
        'AddOnnxEmbedder', 'AddLocalProvider',
      ],
      use: '`AddModel2Vec` / `Model2VecProvider`, `AddOnnx`, `AddLlamaSharp`',
      why: 'every registration returns an IModelProvider, so an *Embedder suffix sorted backends by what '
        + 'they produce — the taxonomy D130 deleted. "Static" also named a technique rather than the '
        + 'model2vec format it loads, and "Local" named nothing at all (D132)',
    },
    {
      // D125's provider unification. These were two BYTE-IDENTICAL records — `(string ProviderId, string?
      // Model = null)` — one per domain, and the generation one's own XML doc admitted it behaved "exactly
      // as on the LLM side". Whole-identifier equality is what lets `UseDefaultGenerationCandidates` (a
      // generation BUILDER method, not the type) stay live without needing an allowance.
      names: ['LlmCandidate', 'GenerationCandidate', 'GenerationCandidateSpec'],
      use: '`Lyntai.Lifecycle.ProviderCandidate` / `ProviderCandidateSpec`',
      why: 'which backend and which of its models is ONE routing rule, not one per domain — two copies of '
        + 'it drift and a single type cannot (D125)',
    },
    {
      // D125's capability half. The generation domain had the right model — capability as DATA — and the
      // LLM domain had none, so "can this backend serve this" was a data question on one side and a type
      // question on the other. Generalized rather than copied. `Deliveries` became `Operations` because the
      // list now spans Embed as well as Inline/Job/Stream, and `Inline` became `Complete` for the same
      // reason: a chat completion and an inline render are the same operation on different KINDS.
      // D128. Added and removed the SAME DAY, which is why it is recorded rather than quietly dropped: it
      // gave embedders an Id by minting a THIRD provider family, when what they needed was one enum member
      // on the capability object they already had. An embedder is an IModelProvider declaring Embed.
      names: ['IEmbeddingProvider'],
      use: '`Lyntai.Lifecycle.IModelProvider` declaring `ProviderKinds.Vector` in `Produces`',
      why: 'an embedding model is a TEXT backend like a chat model — giving it its own provider family '
        + 'splits by domain what belongs in data (D128)',
    },
    {
      // D127's collapse. FIVE seams became one: the two domain provider interfaces, the optional streaming
      // one (streaming is an OPERATION now, declared in data), and the LLM probe seam with its own probe
      // record — which had duplicated the generation one in a different field order, and was found only by
      // a compile collision. `IGenerationJobProvider` deliberately SURVIVES and is not listed: a stateful
      // submit/poll/fetch/cancel protocol is a contract shape, not a content type.
      names: [
        'ILlmProvider', 'IGenerationProvider', 'IGenerationStreamProvider',
        'IProviderProbe', 'GenerationProbeResult',
      ],
      use: '`Lyntai.Lifecycle.IModelProvider` / `ProviderProbeResult`',
      why: 'one backend seam for every domain, with what it serves declared in ProviderCapabilities rather '
        + 'than encoded as a type — an embedder and a chat model are one interface apart only in data (D127)',
    },
    {
      names: ['GenerationCapabilities', 'GenerationDelivery', 'Deliveries'],
      use: '`Lyntai.Lifecycle.ProviderCapabilities` / `ProviderOperation` / `ProviderCapabilities.Operations`',
      why: 'capability belongs to every provider seam, not to one domain — and a per-domain capability '
        + 'type forces embedding into a parallel stack (D125)',
    },
    {
      // D76's naming sweep of the surface D67-D76 added. 'Flags' on an options object reads as boolean
      // feature flags or a [Flags] enum in .NET, not as 'the argv token for each argument' - so it is a
      // name that MISLEADS, which is the only kind D66 spends a rename on. Whole-identifier, so the
      // ordinary [Flags] attribute and any genuine flag set elsewhere are untouched.
      names: ['Flags'],
      use: '`LocalDiffusionOptions.ArgvFlags`',
      why: 'it maps a MEANING to an argv token; Flags in .NET connotes booleans or a [Flags] enum (D76)',
    },
    {
      // D76. The prose half is in `retiredTerms`; this is the SURFACE half, and it is the one that mattered
      // most — `reap` had reached the frozen 3.0 API as three type names and a parameter, where prose can be
      // reworded and a shipped type name cannot. Whole-identifier equality, so `ObserveStdinAndReapAsync`
      // (the POSIX child-process sense, and correct) is untouched without needing an allowance.
      names: ['IMemoryReapPolicy', 'MemoryReapKind', 'DefaultMemoryReapPolicy', 'reapPolicy'],
      use: '`IMemoryRemovalPolicy` / `MemoryRemovalKind` / `DefaultMemoryRemovalPolicy` / `removalPolicy`',
      why: 'to reap means to HARVEST in ordinary English — the opposite of removing data — so a reader could '
        + 'take IMemoryReapPolicy for something governing recall. Caught on the eve of the freeze, which is '
        + 'the last moment it was free to change (D76)',
    },
    {
      // The three parameter names the 2026-08-11 whole-branch review caught, plus the 2.5 name the first of
      // them replaced. All four are GONE — these entries exist so nothing REINTRODUCES them, which is the
      // same reasoning `retiredTerms` records for its own zero-hit entries. `memoryClock` is included
      // because it is the one name in this group a real 2.5 consumer can still be holding (D47's closing
      // amendment: the other two were introduced and renamed entirely inside the unreleased window).
      names: ['ageClocks', 'appraisers', 'modulators', 'memoryClock'],
      use: '`agePolicies:` (was `memoryClock:`, then `ageClocks:`), `saliencePolicies:` (was `appraisers:`) '
        + 'and `retentionPolicies:` (was `modulators:`)',
      why: 'docs/DECISIONS.md D47 retired "clock", "appraiser" and "modulator" as names for these seams — '
        + 'there was never a clock, age is INTERFERENCE — and the parameters kept the retired words until '
        + 'the eve of the 3.0 freeze. `ageClocks` sat three lines from a genuine `Func<DateTimeOffset> '
        + 'clock`, so the parameter named "clocks" was the one that is not a clock',
    },
    {
      // Added with the rename itself, 2026-08-11, so this entry and the surface it guards land together.
      //
      // Worth reading before deciding this one is pedantic: `Appraise` survived the D47 sweep by an
      // implementer's JUDGEMENT, a reviewer noticed it was missing from D47's "deliberately unchanged"
      // list, and the reasoning that justified keeping it was composed while closing that review finding —
      // then repeated to another agent as settled. It became "deliberate" at the moment it was written
      // down, which is not the same as having been decided. The owner's one question ("is that by
      // measure?") is what surfaced it.
      //
      // The check that settled it was not a measurement — naming never is — but a PATTERN the codebase
      // already had: every seam method is named for what it RETURNS (`IMemoryRetrievabilityPolicy.
      // Retrievability`, `IMemoryAgePolicy.Age`, `IMemoryRetentionPolicy.StabilityFactor`,
      // `IMemoryRankingPolicy.Rank`). `Appraise` alone was named for the ACT, in the verb form of the
      // retired `ISalienceAppraiser`, while returning `MemorySignals`. Its own XML summary already read
      // "Signals for this write" — the docs had settled on the return-shaped name; only the identifier
      // lagged.
      names: ['Appraise'],
      use: '`IMemorySaliencePolicy.Signals` — the method is named for what it RETURNS, like every other '
        + 'seam method in this domain',
      why: 'renamed 2026-08-11 (owner ruling) in the last window before the 3.0 freeze, where it costs '
        + 'nothing; after the freeze it would cost a major. Note the LIMIT this entry demonstrates: a '
        + 'registry of exact identifiers would NOT have caught `Appraise` from a rule naming '
        + '`ISalienceAppraiser`, because a method named for the verb form of a retired type is not that '
        + 'type\'s name. This gate catches REINTRODUCTION, not every descendant of a retired word — that '
        + 'still needs a reader',
    },
    {
      // The ten type renames of D47 itself. `retiredTerms` fences the QUALIFIED old paths in prose; nothing
      // fenced the surface, which is where a rename actually costs a major version.
      names: ['IMemoryClock', 'IRetrievabilityPolicy', 'IRetentionModulator', 'ISalienceAppraiser',
        'PerWriteClock', 'ContentSizeClock', 'ElapsedClock', 'BurstDampenedClock',
        'SalienceModulator', 'StructuralSalienceAppraiser'],
      use: '`IMemoryAgePolicy`, `IMemoryRetrievabilityPolicy`, `IMemoryRetentionPolicy`, '
        + '`IMemorySaliencePolicy`; `PerWriteAgePolicy`, `ContentSizeAgePolicy`, `ElapsedAgePolicy`, '
        + '`BurstDampenedAgePolicy`; `SalienceRetentionPolicy`, `StructuralSaliencePolicy`',
      why: 'docs/DECISIONS.md D47 (2026-08-10) gave every memory policy seam ONE naming shape, '
        + '`IMemory<Domain>Policy`, and renamed every implementation whose own name embedded a retired '
        + 'word. Deliberately unrenamed and still LIVE: `ModulatedRetrievability` and the '
        + '`Lyntai.Memory.Modulation` namespace (the word there is Modulation, the domain, not Modulator, '
        + 'the retired suffix) — which is why this rule matches whole identifiers only',
    },
    {
      // A DELETED type, not a renamed one: there is no forward name to migrate to, so a surface carrying
      // either of these again would be a restoration nobody decided on.
      names: ['HalfLifeRetrievability', 'HalfLifeOptions'],
      use: '`DsrRetrievability` — the only shipped forgetting curve as of 3.0. A consumer who needs the '
        + 'exponential shape implements `IMemoryRetrievabilityPolicy` themselves',
      why: 'both were DELETED 2026-08-10 (fsrs-properly plan Task 1, docs/DECISIONS.md D49), with no '
        + 'restore path — a public surface reintroducing either is a mistake, not a migration',
    },
    {
      // The 3.0 collision fix. `MemoryRetentionPolicy` bounded IMemoryStore's SIZE while
      // `IMemoryRetentionPolicy` modulates a graph entry's half-life — two unrelated concepts one `I`
      // apart, in a language where `IFoo` reads as the interface of `Foo`. This gate's own header used to
      // cite that pair as the example of a live type a substring rule would wrongly flag; the pair is gone
      // because the STORAGE side moved to the eviction vocabulary it already used (`MemoryEvictionMode`,
      // `MemoryEviction.Survivors`), leaving "retention" to the graph seam alone.
      //
      // `Eviction` is NOT listed, and deliberately: it is an ordinary English word that a future member
      // could legitimately carry, and whole-identifier matching would fire on any of them. What was retired
      // is the SPECIFIC property `MemoryEvictionPolicy.Eviction`, which stuttered against its own type —  link-ok
      // and a rule that cannot express "this member on this type" should not pretend to by banning the word.
      names: ['MemoryRetentionPolicy'],
      use: '`MemoryEvictionPolicy` (and `LyntaiOptions.MemoryEviction`, `ConfigureMemory(p => p.Mode = …)`)',
      why: 'renamed for 3.0 (docs/DECISIONS.md D13) because it sat one `I` away from the unrelated '
        + '`IMemoryRetentionPolicy` graph seam. The old name reappearing would restore a collision two '
        + 'entries in this file had to carry a KNOWN HAZARD note about',
    },
    {
      // Identifier-SHAPED only, mirroring the reasoning of `retiredTerms`'s own `\w+Dto\b` prose entry: the
      // bare word has to stay sayable (the rules tier quotes what it bans), and a pattern that cannot tell
      // a leak from a prohibition just teaches people to add escapes. On the SURFACE there is no such
      // tension — every hit is a real identifier — but the shape is kept so both registries read the same.
      // The lowercase `dto` alternative is exact, not `dto\w*`: it catches the parameter named `dto`
      // (repo-mechanics.md is explicit that `dto` is not an abbreviation for `DateTimeOffset` either)
      // without claiming every identifier that happens to start with those three letters.
      pattern: '\\w*Dto\\w*|dto',
      use: '`*Row` for a materialization type, `*Request`/`*Reply`, `*Result`, `*Entry`',
      why: 'a name says what a thing IS, never which layer it crossed. `.claude/rules/repo-mechanics.md` '
        + '§Naming records that the tree contains zero `Dto` identifiers and that this is worth keeping; '
        + '`retiredTerms` already fences the prose, and this fences the surface the prose describes',
    },
    {
      // The 2026-08-15 pre-3.0 review's renames. Registered WITH the renames, so this entry and the surface
      // it guards land together — the discipline the `Appraise` entry above records paying for.
      //
      // WHAT IS AND IS NOT HERE, because the gap is the interesting part. Whole-identifier equality can
      // only ban a name that is dead EVERYWHERE, so a rename whose old spelling survives elsewhere — on a
      // different type, for a different thing — is unregisterable by construction. Measured against the
      // baselines at the time of the rename:
      //
      //   registerable (0 live occurrences): Reserve, task
      //   NOT registerable:  AuthoritativeReserve (1 — the GraphMemoryOptions SLOTS member, which keeps
      //                      its name and is correct), policy (7), Strength (6 — GraphNode/MemoryDecayState),
      //                      candidates (19 — GenerationCandidate parameters throughout)
      //
      // `AuthoritativeReserve` is the sharpest rename in the group and the one this cannot guard: it named
      // TWO quantities in the SAME namespace in different UNITS — recall SLOTS on `GraphMemoryOptions`,
      // prompt CHARACTERS on `MemoryCompositionOptions` — both reachable from one builder chain. Banning
      // the identifier would fail the build on the member that legitimately kept it. Banning `Reserve`
      // is what closes the practical hole: that was the only way to SET the characters one.
      names: [
        'EnsureEachBitIsSingleRealAndUnique',
        'IProviderInstallation',
        'SummedAgeComposition',
        'MultiplicativeRetentionComposition',
        'MaximalSalienceComposition',
        'Reserve',
        'task',
      ],
      use: '`ValidateProvenanceBits`, `IProviderProbe`, and the three composition policies with the '
        + '`Policy` suffix their own interfaces and every sibling seam carry',
      why: 'EnsureEachBitIsSingleRealAndUnique was an assertion-shaped, ungrammatical name on frozen '
        + 'surface, beside three siblings called Fits/Pack/Unpack. IProviderInstallation declares a single '
        + 'ProbeAsync and installs nothing — one word from IProviderVersionInstaller, which does — and the '
        + 'documented use is a capability type-test, so the NAME is the whole API for a reader choosing '
        + 'between them. The three *Composition implementations dropped the suffix that every other policy '
        + 'in the seven memory domains carries, so a reader scanning the surface could not tell '
        + 'MultiplicativeRetentionComposition was a policy while MultiplicativeRankingPolicy was',
    },
    {
      // Method names on the two composition seams. Separate entry because the reason is the RULE rather
      // than any one identifier: both were named for the ACT while returning something the domain already
      // has a word for — the exact convention the `Appraise` entry above settled, applied to the seams
      // created in the same window and missed by it.
      names: ['Compose'],
      allow: [
        {
          signature: 'static Compose(IEnumerable<CuratedMemory> entries, Func<String,String> header = null, '
            + 'String bullet = "- ", String taskKey = null, IEnumerable<String> scopes = null) : String',
          why: 'CuratedMemorySections.Compose is NOT a policy-seam method and the rule above does not reach '
            + 'it: composing is genuinely what it DOES — it renders catalog entries into prose sections — '
            + 'and it returns a bare String, which is not a domain noun the seam convention could name it '
            + 'for. Its sibling MemoryComposition.ComposeAsync uses the same verb for the same act. The '
            + 'convention retired is "named for the act rather than the RETURN"; here the act is the thing',
        },
      ],
      use: '`IMemoryRetentionCompositionPolicy.StabilityFactor` and '
        + '`IMemorySalienceCompositionPolicy.Signals` — named for what they RETURN',
      why: 'D48 gave each plural domain a composition policy, and two of the three were named for the act '
        + 'while the age one (Advance/Age) followed the rule. The Appraise entry above states the pattern: '
        + 'every seam method in this domain is named for what it returns. These two were created in the '
        + 'same window as that ruling and did not inherit it',
    },
    {
      // The seed-source-fusion work (2026-08-31) DELETED these three GraphMemoryOptions properties outright
      // — no rename, no forward alias — replacing pooled Relevance seeding with a plural IMemorySeedSource
      // collection, each implementation carrying its own options record. Registered with the removal, same
      // discipline as the Appraise/Compose entries above.
      names: ['SemanticSeedK', 'SubjectSeedK', 'SubjectSeedScan'],
      use: '`SemanticSeedOptions.K` (`AddMemorySemanticSeeds`, NOT registered by default) and '
        + '`SubjectSeedOptions.K` / `.Scan` (`AddMemorySubjectSeeds`, registered by default)',
      why: 'a per-source seed pipeline needs a per-source options record; one composite `GraphMemoryOptions` '
        + 'carrying all three made every seed source\'s knobs live on an unrelated type, and let a query '
        + 'against one silently narrow another (docs/DECISIONS.md D88)',
    },
  ],

  /**
   * RETIRED VOCABULARY — words a deliberate decision replaced, which must not reappear in the docs.
   *
   * The problem this closes: the CODE is gated from every side (check-warnings, the API-surface baselines,
   * the storage contracts) while the PROSE is gated from none. A spec paragraph that quietly stops being
   * true survives every check, and the next session reads it and implements the wrong thing. That happened
   * twice on 2026-08-08 and both times a human reading it was the only thing that caught it.
   *
   * Each entry is a term a decision retired, what to say instead, and why — so the failure message teaches
   * rather than merely refusing. Add one whenever a decision renames or re-dimensions something.
   *
   * Scope, and READ IT FROM `check-docs.mjs` RATHER THAN FROM HERE — `IN_SCOPE`, `HISTORICAL` and
   * `LIVE_PREFIX` are the authority, this paragraph is a summary of them:
   *   - IN: `docs/`, `.claude/`, `README.md`, `CLAUDE.md`, `TASKS.md`.
   *   - IN, but only down to its first RELEASED heading: `CHANGELOG.md`. The historical exemption rests on a
   *     record being accurate BY using the vocabulary of its day, which is true of a released section and
   *     false of `## Unreleased` — that describes behaviour that has not shipped and can still change under
   *     the words describing it. (This paragraph said `CHANGELOG.md` was out of scope entirely until
   *     2026-08-14, which stopped being true on 2026-08-11 when the live prefix was added; three of the
   *     rules below already cite pointing this gate at that prefix.)
   *   - OUT wholly: `docs/task-archive.md` (a record, by the same rationale), and any file declaring itself
   *     superseded in its status banner.
   *   - OUT: `src/`. The usual justification — "the compiler already gates their crefs" — is only PART true
   *     and worth stating precisely, because the gap is where real drift has landed: the compiler resolves
   *     `<see cref>` and nothing else, so a type or file named in a `<c>` tag or a `//` comment is checked by
   *     no one. `check-api-vocabulary` covers retired IDENTIFIERS on the frozen surface; nothing covers a
   *     retired CLAIM in an XML doc, which is how `GraphMemoryOptions.AuthoritativeReserve` shipped a
   *     paragraph describing an implementation the measurement had already rejected (2026-08-14 review).
   *
   * Escape hatch: put `drift-ok` on the line. That is the honest annotation for a passage that deliberately
   * NAMES the retired thing — an amendment explaining what changed, or a rule quoting the word it bans.
   */
  retiredTerms: [
    {
      // D136. The prose half. `LlmVerdictException` is absent for the reason on the surface rule above.
      term: '\\bLlmVerdict\\b|\\bGenerationVerdict\\b|\\bLlmVerdictClassifier\\b|\\bGenerationVerdictClassifier\\b|\\bLlmVerdictExtensions\\b',
      why: 'one verdict taxonomy serves every domain; the two enums and the translation between them are '
        + 'gone (D136)',
      use: '`ProviderVerdict` / `ProviderVerdictClassifier` / `ProviderVerdictExtensions`',
    },
    {
      // D135. The prose half. `OpenAiPayload` and `OpenAiFlavor.OpenAi`'s member name are absent for the
      // reason on the surface rule above; so is the PHRASE "OpenAI-compatible", which stays correct about
      // the three dialects that are.
      term: '\\bAddOpenAiCompatible\\b|\\bAddOpenAiCompatibleProvider\\b|\\bOpenAiCompatibleProvider\\b|\\bOpenAiCompatibleOptions\\b|\\bOpenAiCompatibleBuilderExtensions\\b|\\bOpenAiEmbeddingsTransport\\b|\\bOpenAiEndpoint\\b|\\bOpenAiFlavor\\b|\\bOpenAiHttp\\b',
      why: 'the HTTP family is not OpenAI — one of its four dialects is documented by its own vendor as '
        + 'not OpenAI-compatible (D135)',
      use: 'the `Http*` name in `Lyntai.Providers.Http`',
    },
    {
      // D134. The prose half. The generic factory primitives are absent for the reason given on the
      // surface rule above, and `AddProvider` would otherwise match inside every one of these.
      term: '\\bAddAutomatic1111Provider\\b|\\bAddAzureOpenAiProvider\\b|\\bAddClaudeCliProvider\\b|\\bAddCodexCliProvider\\b|\\bAddComfyUiProvider\\b|\\bAddExtensionsAiProvider\\b|\\bAddFalProvider\\b|\\bAddLlamaProvider\\b|\\bAddLlamaSharpProvider\\b|\\bAddLocalDiffusionProvider\\b|\\bAddModel2VecProvider\\b|\\bAddOllamaProvider\\b|\\bAddOnnxProvider\\b|\\bAddHttpProviderProvider\\b|\\bAddOpenAiImageProvider\\b|\\bAddOpenAiProvider\\b|\\bAddOpenRouterProvider\\b',
      why: 'a Provider suffix on every backend registration distinguishes none of them (D134)',
      use: 'the same name without the suffix',
    },
    {
      // D130/D132. The SURFACE half is in `retiredApiNames`; this is the prose half. A document naming any
      // of these describes a registration or a type the tree no longer has. `StaticEmbedder` is matched but
      // the WORD "static" is not — a lookup table is still correctly called static embeddings in prose,
      // which is exactly why the type had to be renamed and the technique did not.
      term: '\\bAddHttpProviderEmbedder\\b|\\bAddHttpProviderEmbeddings\\b'
        + '|\\bOpenAiCompatibleEmbedderOptions\\b|\\bHttpEmbedder\\b'
        + '|\\bAddStaticEmbedder\\b|\\bStaticEmbedderOptions\\b|\\bStaticEmbedder\\b'
        + '|\\bAddOnnxEmbedder\\b|\\bAddLocalProvider\\b',
      why: 'every registration returns an IModelProvider, so an *Embedder suffix sorted backends by what '
        + 'they produce — the taxonomy D130 deleted from the types and D132 from the surface',
      use: '`AddHttpProvider` (with `Chat`/`Embeddings` saying which routes), '
        + '`AddOnnx`, `AddModel2Vec`, `AddLlamaSharp`, `HttpEmbeddingsTransport`',
    },
    {
      // D125. The SURFACE half is in `retiredApiNames`; this is the prose half. Both names described the
      // same record in two namespaces, so a document naming either is describing a type that no longer
      // exists. Historical records (CHANGELOG below the Unreleased boundary, the task archive) are exempt
      // by file; a deliberate mention inside a maintained document takes `drift-ok` on its line.
      // D127. The SURFACE half is in `retiredApiNames`; this is the prose half. A document naming any of
      // these is describing a seam the tree no longer has. `IGenerationJobProvider` is deliberately NOT
      // matched — it survives, and a pattern that swept it up would fire on every correct mention.
      term: '\\bILlmProvider\\b|\\bIGenerationProvider\\b|\\bIGenerationStreamProvider\\b|\\bIProviderProbe\\b',
      why: 'the domain provider seams collapsed into one IModelProvider (D127), with what a backend serves '
        + 'declared in ProviderCapabilities rather than encoded as a type',
      use: '`IModelProvider`',
    },
    {
      term: '\\b(?:Llm|Generation)Candidate\\b',
      why: 'the two per-domain candidate records were unified into one (D125) — naming either sends a '
        + 'reader to a type the tree no longer has',
      use: '`ProviderCandidate`',
    },
    {
      // A SHAPE rule retired by measurement rather than a rename, which is why it needs an entry at all:
      // nothing about the code changed, so no other gate can see the claim go stale. Standing guidance said
      // the evidence pointed at "a SCORER over a bounded candidate list, never a generator asked to choose".
      // Measured at 3-7 options on 2026-09-12 that is HALF true — the winning shape INVERTS with model size,
      // and at 2.49 GB the generator asked to choose wins at every list length. The two live occurrences are
      // quotations of the retired rule and carry `drift-ok`.
      term: 'never a generator asked to choose',
      why: 'it was inferred from an endorse-a-SUBSET task at 20-80 candidates and does not survive the '
        + 'forced choice at 3-7, which is the length a decision actually uses',
      use: 'the winning SHAPE inverts with model size — a generator asked to choose WINS at 2,489,757,856 B '
        + 'and loses at 806,058,240 B, where it stops choosing and emits a constant. Say "pick the shape '
        + 'from the size", and cite `docs/memory-measurements.md` §5 (`decision-shape-single-evidence`). '
        + 'The scorer recommendation survives only for the small-model half.',
    },
    // 2026-09-10, from `docs/FIXES.md`. "Re-throw only OperationCanceledException" was the fail-open rule
    // this repository taught for its own storage seams — and it is the DEFECT, not a shorthand for it: an
    // `HttpClient` deadline arrives as `TaskCanceledException`, which IS an `OperationCanceledException`,
    // so a bare rethrow makes a fail-open seam fail CLOSED on the one failure a network-backed store
    // actually has. Fixed at 21 sites across two commits; `.claude/knowledge/storage.md` was still teaching
    // it afterwards, where a BYO backend author would have reimplemented it.
    //
    // Narrow on purpose. It bans the RULE ("rethrow only ..."), never the type name, which every correct
    // fix must still write — including the correct form, `catch (OperationCanceledException) when
    // (ct.IsCancellationRequested)`. The lookahead is what keeps the two apart.
    {
      term: '\\bre-?throw(s|ing)?\\s+only\\s+`?\\\\?OperationCanceledException'
        + '|\\bonly\\s+`?\\\\?OperationCanceledException`?\\s+(?:is|are)\\s+re-?thrown',
      use: 'that the CALLER\'s cancellation is re-thrown, tested as `ct.IsCancellationRequested` — never by '
        + 'the exception\'s TYPE. The correct catch is '
        + '`catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }`',
      why: 'a bare rethrow makes a fail-open seam fail CLOSED: an HttpClient timeout surfaces as '
        + 'TaskCanceledException, which IS an OperationCanceledException, so the type cannot tell a '
        + "caller's cancel from a component's own deadline. Cost 40 minutes of ingestion before it was "
        + 'read, then 21 sites to repair (`docs/FIXES.md`, 2026-09-09/10)',
    },
    // D76 (2026-08-16). "Reap" was the memory subsystem's umbrella verb for forget-or-prune, and it MISLEADS
    // rather than merely differing: in ordinary English to reap is to HARVEST — "reap what you sow", "reap
    // the rewards" — which is close to the opposite of removing data. A reader meeting `IMemoryReapPolicy`
    // cold could reasonably think it governs recall.
    //
    // The pattern is deliberately narrow and does NOT ban the word outright, because two live uses are
    // correct and unrelated: `ProcessRunner.ObserveStdinAndReapAsync` (the POSIX sense — reaping a child
    // process — which IS the established term for that) and one idiomatic "nothing to reap it" in
    // PairwiseComparer. What is retired is the word applied to MEMORY.
    {
      term: '\\b[Rr]eap(s|ed|ing|able)?\\b(?![^\\n]*\\b(process|child|stdin|zombie)\\b)'
        + '(?=[^\\n]*\\b(memory|entr(y|ies)|engine|blend|member|prune|forget|scope|taskKey)\\b)'
        + '|\\bIMemoryReapPolicy\\b|\\bMemoryReapKind\\b|\\bDefaultMemoryReapPolicy\\b',
      use: 'say REMOVE / REMOVAL — `IMemoryRemovalPolicy`, `MemoryRemovalKind`, "a removal", "removes"',
      why: 'to reap means to HARVEST in ordinary English, which is the opposite of removing data; D66\'s own '
        + 'rule spends a name that misleads. Retention and Deletion were both unavailable — '
        + 'IMemoryRetentionPolicy already exists (D47) and D41 makes "deletion" the thing the graph '
        + 'deliberately does NOT do (burial). The POSIX process sense is untouched (D76)',
    },
    // D70 (2026-08-16) withdrew the `Lyntai.Generation` SemVer exemption. This is registered because the
    // claim was stated in NINE maintained documents plus two code comments — a caveat that widely copied is
    // exactly the kind that grows back, and every one of those copies read as current fact. Historical
    // `CHANGELOG.md` entries and `docs/task-archive.md` are out of `check-docs`' scope already, which is
    // right: they were accurate on their own day.
    //
    // The pattern is deliberately narrow. `experimental` alone would fire on the ROADMAP's genuine uses —
    // MEAI's experimental surfaces, OTel's experimental semconv — which are other projects' labels and
    // nothing to do with this promise. What is retired is the CLAIM: this library marking its own package.
    {
      term: '(?:EXPERIMENTAL|experimental)(?:\\s+\\w+){0,3}\\s+(?:carve-out|carveout)'
        + '|(?:carve-out|carveout)(?:\\s+\\w+){0,3}\\s+(?:EXPERIMENTAL|experimental)'
        + '|Lyntai\\.Generation`?(?:\\s+\\w+){0,4}\\s+(?:is|ships)\\s+EXPERIMENTAL'
        + '|exempt from (?:that |the )?(?:SemVer )?promise',
      use: 'say every package carries the full SemVer promise (D70), and name D67/D69 for why the three '
        + 'reasons the exemption gave are closed',
      why: 'the carve-out was WITHDRAWN in 3.0, not satisfied — a document still describing Lyntai.Generation '
        + 'as exempt tells a consumer they may rely on minor-version reshaping that will not happen, and '
        + 'tells a contributor they may reshape a frozen package (D70)',
    },
    // The 2026-08-15 naming sweep (D66). Registered here as well as in `retiredApiNames`, because the two
    // gates see different tiers and the PROSE half is the one that puts a retired name back: `check-docs`
    // scans docs/ and .claude/ (and .html), `check-api-vocabulary` scans the API baselines, and neither
    // reads the other's subject. `pitfalls.md` records exactly this — "a rename that stops at the types
    // leaves the retired word alive in PARAMETER NAMES and PROSE, and the prose is what puts it back" — and
    // the first pass of this very sweep did it again: four maintained documents kept naming renamed API and
    // every gate stayed green, because nothing was registered HERE.
    {
      term: 'IProviderInstallation',
      use: '`IProviderProbe`',
      why: 'it declares one ProbeAsync and installs nothing, one word from IProviderVersionInstaller which '
        + 'does; the documented use is a capability type-test, so the name is the whole API (D66)',
    },
    {
      term: 'EnsureEachBitIsSingleRealAndUnique',
      use: '`MemoryProvenance.ValidateProvenanceBits`',
      why: 'an assertion-shaped, ungrammatical name on frozen surface, beside siblings called Fits/Pack/'
        + 'Unpack (D66)',
    },
    {
      // Whole-word so `MultiplicativeRetentionCompositionPolicy` — the LIVE name, which contains the retired
      // one — does not match. The same containment problem `retiredApiNames`' header records for
      // MemoryRetentionPolicy/IMemoryRetentionPolicy, in a registry whose matching is loose by design.
      term: '(?<!\\w)(SummedAgeComposition|MultiplicativeRetentionComposition|MaximalSalienceComposition)(?!\\w|Policy)',
      use: 'the same names with the `Policy` suffix their interfaces and every sibling seam carry',
      why: 'a reader scanning the surface could not tell MultiplicativeRetentionComposition was a policy '
        + 'implementation while MultiplicativeRankingPolicy was (D66)',
    },
    {
      // Deliberately NOT a bare `Reserve`: that word appears in ordinary prose about reserving slots, and a
      // registry that cries wolf gets an exclusion added and then rots. The CALL is what was renamed.
      term: '\\.Reserve\\(',
      use: '`.ReserveCharacters(` — the builder call now names its unit',
      why: 'AuthoritativeReserve named recall SLOTS on GraphMemoryOptions and prompt CHARACTERS on '
        + 'MemoryCompositionOptions, both reachable from one builder chain, so a bare Reserve(2) read as '
        + 'slots silently meant two characters (D66)',
    },
    {
      term: 'age_days',
      use: '`age` — a subtraction on the engine\'s position',
      why: 'decay is measured in what has HAPPENED in a memory, not in elapsed time (spec §3.-1)',
    },
    {
      term: 'last_recalled_at',
      use: '`last_recalled_position`',
      why: 'the node records where the engine\'s position stood, not a timestamp (spec §3.-1)',
    },
    // `strengthened_at` was banned here until 2026-08-12 (D52) and the ban is deliberately GONE, not escaped.
    // It was written when an edge's ONLY strengthening mark was `strengthened_position`, so a timestamp could
    // only have REPLACED it — which is the thing spec §3.-1 forbids. An edge now carries the same three
    // policy-independent primitives a node does, and `strengthened_at` sits BESIDE `strengthened_position`
    // exactly as the node's own `encoding_at` sits beside its marks, feeding `ElapsedAgePolicy`'s projection.
    // The tell that the rule had gone stale rather than caught something: `encoding_at` is the identical
    // construct, appears throughout these same documents, and was never banned — so the registry was
    // forbidding on the edge what it permits on the node. Four `drift-ok` escapes would have hidden that,
    // which is the failure mode this registry's own header warns about.
    // What §3.-1 actually protects is unchanged and still true: decay is measured against the position
    // accumulator (or a policy's own projection of the primitives), never against the wall clock.
    {
      term: 'YYYYMMDDNNNN',
      use: '`yyyyMMddHHmm`',
      why: 'migration numbering changed 2026-08-08 — the timestamp is self-describing (storage.md)',
    },
    {
      // The retired CLAIM, in the two phrasings the docs actually used, not the words "single" or
      // "contiguous" on their own — both stay sayable, and this gate's own header warns that a rule which
      // cannot tell a leak from a prohibition just teaches people to add escapes. The live passages that
      // NAME the retired claim to explain the reversal carry `drift-ok`.
      term: 'portable guarantee is (therefore )?(a )?SINGLE-token|single-token (guarantee|one)'
        + '|match(es)? the (whole )?query as (one|a) contiguous substring',
      use: 'every backend splits a query the same way (`Lyntai.Storage.SearchTerms`) and an entry carrying '
        + 'ANY term is recalled — words for a space-separated script, character trigrams for one written '
        + 'without spaces. What stays backend-specific is RANKING (bm25 on SQLite, matched-term count then '
        + 'recency elsewhere), never which entries are found',
      why: 'docs/DECISIONS.md D55 (2026-08-12). Only SQLite\'s FTS path ever split a query; every other path '
        + 'matched the whole query as one contiguous substring, so a realistic multi-word cue found the fact '
        + 'on one backend and nothing at all on the others — and a CJK query, which whitespace cannot split '
        + 'at all, got exact-phrase-or-nothing in every language that writes no spaces. This was recorded '
        + 'for a year as a by-design divergence and defended by a test that ASSERTED it. The rule that fell '
        + 'out, and the reason this entry exists: an ORDERING difference between backends is a divergence, a '
        + 'different answer to "is the fact found" is a defect',
    },
    {
      // Identifier-SHAPED only (`CustomerDto`), not the bare word: the rules tier has to quote what it
      // bans, and a pattern that cannot tell a leak from a prohibition just teaches people to add escapes.
      term: '\\w+Dto\\b',
      use: '`*Row` for a materialization type, `*Request`/`*Reply`, `*Result`, `*Entry`',
      why: 'a name says what a thing IS, never which layer it crossed; the tree holds zero Dto identifiers '
        + 'and prose seeds the name back in on the next change (repo-mechanics.md §Naming)',
    },
    {
      // Two phrasings of the same retired claim — see the "why" for the reversal. Neither hits today: the
      // claim currently only survives in src/ XML docs and CHANGELOG.md, both out of this gate's scope (see
      // the module doc above), plus tests/, which was never in scope either. This entry exists so nothing
      // NEW repeats the claim in a gated document.
      //
      // NOTE for a future editor: "salience does not reorder a seed" reads as TRUE again once you know the
      // rank half is off by default (D45, corrected 2026-08-09) — that is not a reason to remove this entry.
      // The retired claim was UNCONDITIONAL ("never reorders", full stop, no store-admission ordering
      // either), and that blanket form is still false: admission ordering is real and always on. A doc
      // reintroducing the unconditional phrasing is still wrong, just for a subtler reason than before.
      term: 'never reorders a seed|not retrieval priority',
      use: 'salience means "does not fade away" — decay resistance AND store ADMISSION priority, both on by '
        + 'default; it can ALSO lift rank in GraphMemoryEngine (bounded, logarithmic), but that is a '
        + 'stronger, separate claim and defaults OFF — a consumer opts in',
      why: 'reversed 2026-08-09 (memory-3.0 plan 2, Task 6) — docs/DECISIONS.md D45, corrected the same day '
        + 'by D45 from an initial reading that also defaulted ranking on. The store-admission half shipped '
        + 'in Task 5 and is unconditional; the engine-ranking half shipped in Task 6 but is opt-in '
        + '(Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight defaults to 0 — moved off '
        + 'GraphMemoryOptions 2026-08-09 by the memory-ranking-seam plan) — read SalienceRetentionPolicy\'s '
        + 'own summary carefully: that POLICY itself still only scales stability, D45 explains why',
    },
    {
      // Keyed on the QUALIFIED old names on purpose — see the note below on why the BARE forms of the ten
      // names this later renamed (IMemoryClock, PerWriteClock, ContentSizeClock, ElapsedClock,
      // BurstDampenedClock, IRetrievabilityPolicy, IRetentionModulator, SalienceModulator, ISalienceAppraiser,
      // StructuralSalienceAppraiser) are deliberately NOT added here too. The six names below that were never
      // touched by that later rename (`MemoryTick`, `HalfLifeOptions`, `HalfLifeRetrievability`,
      // `ModulatedRetrievability`, `SalienceContext`, `SalienceOptions`) are still exactly right bare, and
      // still appear all over the docs — a pattern on those would fire on every correct mention and teach
      // nothing. Only `Lyntai.Memory.<Type>` is now false, and it is false for all sixteen retention types at
      // once (ten doubly so, post-rename), which is why this is one entry rather than sixteen.
      //
      // 2026-08-10 (memory-policy-seams Task 1, docs/DECISIONS.md D47): the ten names above were renamed a
      // SECOND time onto one `IMemory*Policy`/`*AgePolicy` shape — IMemoryClock, PerWriteClock,
      // ContentSizeClock, ElapsedClock, BurstDampenedClock -> IMemoryAgePolicy, PerWriteAgePolicy,
      // ContentSizeAgePolicy, ElapsedAgePolicy, BurstDampenedAgePolicy; IRetrievabilityPolicy ->
      // IMemoryRetrievabilityPolicy; IRetentionModulator -> IMemoryRetentionPolicy; SalienceModulator ->
      // SalienceRetentionPolicy; ISalienceAppraiser -> IMemorySaliencePolicy; StructuralSalienceAppraiser ->
      // StructuralSaliencePolicy. This entry's own regex is untouched by that (it only ever matched the OLD
      // names under the ROOT-namespace qualification, which stays equally wrong for two reasons now instead
      // of one) — deliberately NOT extended to catch the new bare-name drift too: doing that needs a
      // `drift-ok` sweep of docs/DECISIONS.md's own historical D39/D40/D46 mentions first, which is
      // off-limits to routine task work (see TASKS.md's identical, earlier-deferred note for
      // GraphMemoryOptions's removed properties).
      term: 'Lyntai\\.Memory\\.(IMemoryClock|MemoryTick|PerWriteClock|ContentSizeClock|ElapsedClock'
        + '|BurstDampenedClock|IRetrievabilityPolicy|HalfLifeOptions|HalfLifeRetrievability'
        + '|IRetentionModulator|ModulatedRetrievability|SalienceModulator|ISalienceAppraiser'
        + '|SalienceContext|SalienceOptions|StructuralSalienceAppraiser)\\b',
      use: 'the DOMAIN namespace and the CURRENT name: `Lyntai.Memory.Interference` '
        + '(`IMemoryAgePolicy`/`MemoryTick`/`PerWriteAgePolicy`/`ContentSizeAgePolicy`/`ElapsedAgePolicy`/'
        + '`BurstDampenedAgePolicy`), `Lyntai.Memory.Forgetting` (`IMemoryRetrievabilityPolicy`, '
        + '`HalfLifeOptions`, `HalfLifeRetrievability`), `Lyntai.Memory.Modulation` '
        + '(`IMemoryRetentionPolicy`, `ModulatedRetrievability`, `SalienceRetentionPolicy`) or '
        + '`Lyntai.Memory.Salience` (`IMemorySaliencePolicy`, `SalienceContext`, `SalienceOptions`, '
        + '`StructuralSaliencePolicy`) — only `MemoryDecayState` and `MemorySignals` stayed at the root',
      why: 'the graph-memory retention types moved into per-domain sub-namespaces 2026-08-09 '
        + '(docs/DECISIONS.md D46, design §5.7): each varying rule is a domain owning its seam, its '
        + 'implementations AND its options, and a type no domain owns stays at the root. A pure rename — no '
        + 'shape changed, so a doc naming the old qualified path is describing an import that no longer '
        + 'compiles. Ten of the sixteen names were renamed AGAIN 2026-08-10 onto one naming shape '
        + '(docs/DECISIONS.md D47) — see the code comment above for why this entry\'s own match surface did '
        + 'not need to change to stay correct.',
    },
    {
      // The prose half of the 3.0 collision fix; `retiredApiNames` fences the surface.
      //
      // The lookbehind is the entire point of the pattern: `IMemoryRetentionPolicy` is LIVE and contains
      // this term as a substring, as do `MemoryRetentionComposition` and the migration
      // `M202608121100_MemoryRetentionModel`, which keeps its name because a released migration is
      // identified by NUMBER and renaming the class would say a change happened that did not. Three live
      // names one character apart from the retired one is exactly the case a bare-word pattern gets wrong.
      term: '(?<![A-Za-z_])MemoryRetention(Policy\\b|\\b)',
      use: '`MemoryEvictionPolicy` for the store bound, `LyntaiOptions.MemoryEviction` for the option, and '
        + '`IMemoryRetentionPolicy` (unchanged) for the graph engine\'s half-life modulation',
      why: 'renamed for 3.0 (docs/DECISIONS.md D13). The two were unrelated — one EVICTS entries, the other '
        + 'LENGTHENS a half-life — and sat one `I` apart, which in .NET reads as the interface of the class. '
        + 'A doc still saying `MemoryRetentionPolicy` is either naming a type that no longer exists or, '
        + 'worse, describing the graph seam with the storage type\'s semantics',
    },
    {
      // Zero current hits, same as the precedent above (`never reorders a seed`) — added so nothing
      // REINTRODUCES the claim, not because it fires today. A fix-round review (2026-08-10) found three
      // markdown hits of this exact shape (README.md twice, docs/2026-07-17-lyntai-design.md once) and they
      // were fixed by hand; this entry is what stops a fourth. Deliberately does NOT try to catch the same
      // claim in src/ XML docs (three sites: IMemoryRetrievabilityPolicy.cs x2, DsrRetrievability.cs,
      // GraphMemoryOptions.cs, all fixed by hand the same review) — see the module doc above for why `src/`
      // is out of this gate's scope; those four sites need their own eyes on the next touch.
      term: 'DsrRetrievability[^.]{0,80}available,? (?:but )?not (?:the )?default'
        // wildcard gaps, not literal spaces — this rule fences a DELETED type, so a form it cannot match is
        // worse than for a merely-demoted one: there is no forward name to migrate to. Dead since written,
        // measured 2026-08-14 alongside the two entries above.
        + '|HalfLifeRetrievability[^.\\n]{0,3}\\(the exponential curve,? the default\\)'
        + '|HalfLifeRetrievability[^.\\n]{0,3}(?:ships|is available|remains available|one line away'
        + '|remains the (?:unchanged )?default)',
      use: '`DsrRetrievability` is the ONLY shipped forgetting curve as of 3.0 '
        + '(`MemoryEngineRegistration.AddMemoryEngine`, and a bare-constructed `GraphMemoryEngine` now '
        + 'agrees); `HalfLifeRetrievability` and `HalfLifeOptions` are DELETED, with no restore path — a '
        + 'consumer who needs that shape implements `IMemoryRetrievabilityPolicy` themselves',
      why: 'docs/DECISIONS.md D49 (2026-08-10) first made DsrRetrievability the registered default, '
        + 'superseding D46\'s "not yet decided" framing on this specific question; a later decision '
        + '(2026-08-10, fsrs-properly plan Task 1) then DELETED HalfLifeRetrievability outright, closing the '
        + 'two-defaults split D49 had left open. A doc still calling DsrRetrievability "available, not '
        + 'default", or describing HalfLifeRetrievability as shipped, available, or a restorable/unchanged '
        + 'default in ANY form, is describing a behaviour and a TYPE that no longer exist.',
    },
    {
      // Companion to the entry above, for the domain-level claim rather than the default-selection one — a
      // fix-round review (2026-08-10, the same task that deleted the curve) found this exact shape in
      // docs/2026-07-17-lyntai-design.md's own §5.7 ("`.Forgetting` now ships two…") and fixed it by hand;
      // this entry is what stops it recurring, the same precedent as the two entries above it.
      // The gap after the identifier is a WILDCARD, not a literal space — measured 2026-08-14: with a literal
      // space this rule could never fire on `.Forgetting` now ships two…, the backticked form its own comment
      // below quotes as the motivating sentence, and which is the only form this repository's prose uses. It
      // had been dead since the day it was written. Same repair as the Multiplicative entry above.
      term: '\\.Forgetting[^.\\n]{0,3}(?:now )?ships two|Forgetting[^.\\n]{0,3}domain ships two (?:curves|implementations)'
        + '|two (?:shipped|forgetting) curves\\b(?!.{0,120}(?:through 2\\.5|deleted|2026-08-10))'
        // Added 2026-08-11: CHANGELOG.md's live prefix said "Both shipped curves compute…", counting a
        // DELETED curve as shipped, and no alternative above matched it — the count was spelled as a word,
        // not a numeral. Found by reading around a hit rather than by the gate, which is the reason to widen.
        // Kept to the exact FALSE claim ("both SHIPPED curves") — a first attempt at `[Bb]oth curves`
        // fired 24 times on correct prose like "Both curves already conform", which is true of the two
        // that existed then. A pattern that cannot tell a false claim from a true one only teaches
        // people to add escapes.
        + '|[Bb]oth shipped curves',
      use: '`Lyntai.Memory.Forgetting` ships ONE implementation, `DsrRetrievability` — the deleted '
        + '`HalfLifeRetrievability` is the reason this domain no longer demonstrates "implementations '
        + 'accumulate" the way `.Ranking` does',
      why: 'HalfLifeRetrievability was deleted 2026-08-10 (fsrs-properly plan Task 1, docs/DECISIONS.md) — '
        + 'a doc still counting two curves in this domain, in the present tense, is describing a package '
        + 'that no longer ships',
    },
    {
      // Companion to the two entries above it — the SAME "fix-round found this exact shape and fixed it by
      // hand" precedent, this time for the claim D49 makes about DsrRetrievability's own difficulty
      // handling. docs/DECISIONS.md is off-limits to routine task work (repo-mechanics.md), so this fence
      // exists precisely because that file's own stale sentence could not be corrected in place — see
      // TASKS.md's identical, earlier-deferred note for GraphMemoryOptions's removed properties.
      // Widened 2026-08-11, the first time this rule was pointed at `CHANGELOG.md`'s live prefix: the
      // second alternative required the word "only" before "reads", and the SAME claim appears four
      // hundred lines earlier phrased "ours reads … and never writes it back". One phrasing was caught and
      // the other was not, in the same file, about the same method — which is the argument for keying on
      // the CLAIM (`reads … never writes it back`) rather than on one sentence's adverb.
      term: 'Difficulty`? and never writes it back|\\breads\\b[^\\n]{0,90}never writes it back',
      use: '`DsrRetrievability.Reinforce` now MAINTAINS `MemoryDecayState.Difficulty` on every review '
        + '(2026-08-10, fsrs-properly plan Task 2) — it no longer merely reads the signal and discards the '
        + 'update',
      why: 'true through fsrs-properly plan Task 1 (docs/DECISIONS.md D49\'s own "ours is a PARTIAL, '
        + 'UNFITTED FSRS" section), false from Task 2 onward — a doc still saying difficulty is read-only '
        + 'is describing a limitation this library no longer has',
    },
    {
      // The RANKING half of the same reversal the two curve entries above fence. Added 2026-08-11 by the
      // whole-branch review, which found the registry had an entry for every default this window changed
      // EXCEPT this one — and the ranking default is the one a consumer feels first, because it reorders
      // every recall rather than changing how fast an entry fades.
      //
      // Deliberately keyed on the CLAIM SHAPE, never the bare type name: `MultiplicativeRankingPolicy` is
      // still shipped, still registerable in one line, and still correct to name in a doc — it merely stopped
      // being the default. A bare-name pattern would fire on every correct mention and teach nothing, the
      // same reasoning the qualified-namespace entry above records for its own six untouched names.
      // The verb list is `is|remains|stays` and deliberately NOT `as`: "restore `MultiplicativeRankingPolicy`
      // back as the default" is a correct instruction, and an `as` alternative fired on exactly that line in
      // the migration guide on this entry's first run. The claim form "ships/registers X as the default" is
      // covered by its own alternative below, which cannot match a restore sentence.
      // 2026-08-14: widened after the whole-codebase review found CLAUDE.md's NAMESPACE MAP saying
      // "`IMemoryRankingPolicy`, default `MultiplicativeRankingPolicy`" — the claim written as a NOUN PHRASE
      // (default X) rather than a sentence (X is the default), which every alternative here assumed. All five
      // required the type to precede the word "default", so the gate reported the file clean for the whole
      // window in which its most-read reference block taught the wrong default. The lesson generalises past
      // this entry and is why the others were audited in the same pass: a rule must match the CLAIM in every
      // order it is writable, not the one sentence someone had in mind.
      term: '(?:Multiplicative(?:RankingPolicy)?)[^.\\n]{0,60}\\b(?:is|remains|stays)\\b'
        + '[^.\\n]{0,40}\\b(?:the )?(?:registered |shipped |current )?default'
        + '|(?:ships|registers|registered) `?MultiplicativeRankingPolicy`? as (?:the )?default'
        + '|default(?:s)? to (?:the )?`?MultiplicativeRankingPolicy'
        // the noun-phrase order, in both directions: "default `X`" and "the ranking default is `X`"
        + '|(?:^|[^a-zA-Z])default,? `?MultiplicativeRankingPolicy'
        + '|(?:ranking )?default[^.\\n]{0,20}\\bis\\b[^.\\n]{0,20}`?MultiplicativeRankingPolicy'
        + '|ReciprocalRankFusionPolicy[^.\\n]{0,60}available,? (?:but )?not (?:the )?default'
        + '|RRF[^.\\n]{0,40}(?:is )?not (?:the )?default',
      use: '`ReciprocalRankFusionPolicy` is the REGISTERED default ranking policy as of 3.0; '
        + '`MultiplicativeRankingPolicy` stays shipped and is one line to restore '
        + '(`services.AddSingleton<IMemoryRankingPolicy>(new MultiplicativeRankingPolicy())`)',
      why: 'the owner ruled RRF the 3.0 ranking default 2026-08-11 ("lets use the best so RRF for ranking") '
        + 'on a re-measurement against the corrected arms: RRF beat MultiplicativeRankingPolicy on the '
        + 'corpus\'s `topical` class in ALL SIX shapes (+0.431 to +0.746), reproduced across two runs — see '
        + 'docs/DECISIONS.md D49\'s "ranking half is REOPENED by measurement" amendment. Note this is NOT '
        + 'the HalfLifeRetrievability case and the "use" above says so on purpose: Multiplicative is not '
        + 'unmeasured-and-wrong, it lost one measured comparison and remains the better choice on a scale '
        + 'where raw magnitude carries meaning. A doc calling it the default is wrong; a doc RECOMMENDING it '
        + 'for such a scale is not.',
    },
    {
      // The prose half of the seed-source-fusion removal above; `retiredApiNames` fences the surface. Bare
      // whole-identifier alternation is safe: none of the three is a substring of a live identifier
      // (`SemanticSeedOptions`, `SubjectSeedOptions.Scan`), and it deliberately does NOT match a
      // measurement table's arm names (`+sem`, `+sem80`), which are not these identifiers at all.
      //
      // A narrower, lookahead-excluding version of this rule shipped briefly and was WITHDRAWN the same
      // day: it excluded the term when immediately followed by a value-statement shape ("defaulted to N",
      // "was on/off at N", "= N"), to route around 7 code-tier comment lines this task was told not to
      // touch. Measured against the live pattern before reverting: `"SemanticSeedK = 30"`,
      // `"SubjectSeedK was on at 5 by default"` and `"SemanticSeedK defaulted to 5, but this is stale"` —
      // exactly the shape a stale doc reintroducing this option would take — were all silently EXCLUDED,
      // permanently, in every scanned file, not just the 7 lines it was built for. And unlike
      // `retiredApiNames`' `allow: [{signature}]`, which FAILS the moment its exact string stops matching,
      // a regex exclusion has no self-correction: nothing would ever notice it swallowing a real
      // regression. The 7 lines were fixed at the source instead — see
      // `src/Lyntai.Core/Memory/Engines/GraphMemoryEngine.cs`,
      // `src/Lyntai.Core/Memory/MemoryEngineRegistration.cs` and
      // `tests/Lyntai.Tests/Memory/GraphMemorySeedRankTests.cs` — because "do not touch src/tests/bench"
      // meant do not change LOGIC, and a comment is not logic.
      term: '\\b(?:SemanticSeedK|SubjectSeedK|SubjectSeedScan)\\b',
      use: '`SemanticSeedOptions.K` (`AddMemorySemanticSeeds`, opt-in) or `SubjectSeedOptions.K` / `.Scan` '
        + '(`AddMemorySubjectSeeds`, registered by default)',
      why: 'the three GraphMemoryOptions properties were removed and replaced by registered seed sources, '
        + 'each with its own options record, so a per-source registration can be added, overridden or left '
        + 'out without touching an unrelated option (docs/DECISIONS.md D88)',
    },
    // D103's own "why position-ranking was tried and withdrawn": an earlier ReciprocalRankFusionPolicy cut
    // ranked each seed source by its RETURN-LIST POSITION, assuming every source already returns a
    // relevance-ordered list. It does not — InMemoryMemoryGraphStore orders by recency, not relevance — so
    // position-ranking fabricated a gradient that was not there. Two test XML docs survived the fix still
    // asserting the withdrawn rule (found only by grepping IDENTIFIERS, which a PHRASING claim has none of),
    // which is what this entry is for: the RULE, not a symbol.
    //
    // BROADENED 2026-09-02 (docs/task-archive.md Part 133). The narrowing it replaces REQUIRED an article ("is THE
    // rank") purely to dodge one legitimate line — the second time on that branch a gate was shaped to miss
    // a false positive rather than annotate it, and a pattern shaped that way misses every future line of
    // that shape while nothing notices. Now the connectors are case-covered individually (the old rule spelt
    // `is`/`as`/`a`/`the` lowercase-only with no `i` flag while case-covering Position and Rank, so
    // `position IS the rank` walked straight through) and the article is OPTIONAL. The three legitimate
    // lines this newly reaches carry a `drift-ok` each, which is visible and expires.
    //
    // The Part also asked for a `ranked BY position` branch and the MEASUREMENT REFUSED IT: 15 hits, most of
    // them `ReciprocalRankFusionPolicy` describing what it legitimately DOES — reciprocal rank fusion ranks
    // by position within each source's own list, which is the algorithm and not the withdrawn rule. Its
    // false positives are therefore legitimate authorial choices, the shape `pitfalls.md` records as
    // untightenable, so the paraphrase stays UNCOVERED and is named here rather than silently dropped.
    {
      term: '(?:[Pp]osition|POSITION)(?:\\s+\\S+){0,6}?\\s+(?:[Ii][Ss]|[Aa][Ss])\\s+'
        + '(?:(?:[Aa]|[Tt][Hh][Ee])\\s+)?(?:[Rr]ank|RANK)\\b',
      use: 'the source is UNORDERED, or ranked by ITS OWN Relevance gradient — never that list position IS '
        + 'or SERVES AS a rank',
      why: 'an earlier ReciprocalRankFusionPolicy cut ranked each seed source by return-list position, which '
        + 'assumes every source already returns a relevance-ordered list; InMemoryMemoryGraphStore does not '
        + '(it orders by recency), so position-ranking fabricated a gradient — D103\'s "why position-ranking '
        + 'was tried and withdrawn"',
    },
  ],

  /**
   * DEV-LOOP COMMANDS — one line per `dev.mjs` command, the source the `## Dev loop` table in `CLAUDE.md`
   * is GENERATED from (`dev.mjs check-dev-loop [--write]`, part of `verify`).
   *
   * The NAMES are derived from `dev.mjs`'s own `case` labels and the `verify` column from its `steps`
   * array, so neither can drift; only the description is authored. An undocumented command FAILS and a key
   * naming no command FAILS, which is what keeps this from becoming the second list `dev.mjs`'s usage
   * string already drifted into (24 of 30 commands, silently).
   *
   * Keep each one SHORT — it is a table cell in the file every session reads first. What a gate is FOR, the
   * incident behind it and the numbers it holds all belong in `docs/GATES.md`.
   */
  devLoopCommands: {
    verify: 'the "am I done?" gate. Hands off the tree while it runs',
    build: 'build the solution',
    test: 'the xUnit tests',
    'test-devtools': "the guards' own tests — FIRST in `verify`",
    e2e: 'the Playground against the deterministic provider-stub',
    'check-warnings': 'a warning in `src/` — an unfailed IL2026 is a FALSE trim promise',
    'check-packages': 'a package missing from any registry; the misses are silent',
    'check-bundle': "the bundle's dependency closure — membership is a budget",
    'check-encoding': 'MOJIBAKE in tracked text — no other gate can see it',
    'check-docs': 'vocabulary a decision retired (`retiredTerms`)',
    'check-links': 'a dead path, `Part N`, `§section` or `Type.Member`',
    'check-counts': 'a COUNT in prose that disagrees with the tree',
    'check-comments': 'a comment block that outgrew what it explains',
    'check-decisions': 'a `DECISIONS.md` entry that outgrew the decision',
    'check-archive': 'an archive entry that outgrew the OUTCOME it records',
    'check-backlog': 'a backlog summarizing the archive; `--write` rebuilds its roster',
    'check-pitfalls': 'an unfiled trap or stale facet index; `--write` rebuilds it',
    'check-measurements': 'a result reading CURRENT that its own body retracts; `--write` rebuilds the index',
    'check-decision-claims': 'a DECISION that stopped describing the code it governs',
    'check-api-vocabulary': 'a retired name back on the frozen public surface',
    'check-samples': 'a fenced `csharp` block that does not COMPILE — default ON',
    'check-dev-loop': 'this table drifting from `dev.mjs`; `--write` rebuilds it',
    'check-sensitive': 'leak scan; `--tree` for everything, not just staged',
    'consumer-smoke': 'the release gate — a fresh app against the PACKAGES. Minutes',
    doctor: 'three version checks. NOT in `verify` — run before a release',
    'check-version': 'the pre-commit version-authorship guard, by hand',
    'install-hooks': 'set `core.hooksPath` — once per clone, nothing warns you',
    pack: '→ `publish/packages/`',
    changelog: 'stamp `## Unreleased` at release time — never by hand',
    'release-notes': 'render the notes for a tagged version',
    'decisions-index': 'rebuild `DECISIONS.md`\'s index after adding a `D<n>`',
    'new-package': 'scaffold an adapter package into every registry',
    'new-migration': 'scaffold the next migration with a unique number',
    playground: 'the sample console app',
    bench: 'BenchmarkDotNet router/FTS benchmarks',
    'memory-sweep': 'the {ranking × forgetting} 2×2 — miss and pollution rates',
    'memory-language': 'one factor: `CorpusLanguage`, structurally identical corpora',
    'memory-spacing': 'is `topical` responsive to `DsrOptions.SpacingWeight`?',
    'memory-reinforcement': "law 3's `r`-dependence, isolated from reinforcement MAGNITUDE",
    'memory-bounded': 'the FORM of the growth rule, not its constants — set `ReinforceGain`',
    'memory-salience': 'enrichment held constant so only salience varies',
    'memory-salience-weight': 'how LOUD salience is. Needs a real embedder or the curve is an ARTIFACT',
    'memory-importance': 'WHAT salience measures — novelty against a perfect ORACLE',
    'memory-density': 'a REFUTATION: does a CORRECTION separate from a RECURRENCE?',
    'memory-enrichment': 'why an embedder costs recall quality. Calls a REAL model',
    'memory-annotation': "subject linking with a PERFECT annotator — the CEILING, not a model",
    'memory-verification': 'the judge seam — what a model in the loop is worth',
    'memory-fan': "ACT-R's fan effect, measured and REFUSED (D62)",
    'memory-support': 'rule × θ × clock × `ConnectionBoost`; `--screen` = the model ladder',
    'memory-scale': 'COST, not quality — latency, throughput, bytes; no ground truth',
    'memory-contention': 'judge vs cross-encoder: does moving verification off the shared model pay?',
    'memory-decision': 'a FORCED CHOICE at 3-7 options: one select call, or N score calls argmax\'d?',
    'tool-affordance': 'given these tools, what do you want — through the PROMPT protocol, on a SYNTHETIC roster',
    'rerank-screen': 'does a candidate GGUF rerank AT ALL? `--inspect` needs no download',
    'embed-screen': 'does a candidate GGUF EMBED at all? Sweeps `--pooling`, reads the range not the order',
    'locomo-pair': '`memory-locomo` against a CHOSEN embedder + reader, owning both servers',
    'check-options': 'a shipped option a consumer sets with NO xml doc to explain it',
    'memory-locomo': "the FIELD's benchmark; rewards a perfect archive, so read it differentially",
    'memory-longmemeval': 'prefer a revised fact over the superseded one — **run `--haystack`**',
  },

  /**
   * TRAP FACETS — the two CLOSED vocabularies every trap in `.claude/knowledge/pitfalls.md` is filed under,
   * enforced by `dev.mjs check-pitfalls` (part of `verify`), which also GENERATES the index at the head of
   * that file from them.
   *
   * WHY FACETS AND NOT BETTER HEADINGS. Measured 2026-09-10: a cold-start probe needed the traps bearing on
   * its task, and the nine relevant ones spanned FIVE of the file's nine headings — two of them headings
   * nobody looking for that task would have opened. Retitling was refused for the reason any single
   * hierarchy fails: it puts each trap in exactly one place, and these genuinely belong in two. So the
   * facets are ORTHOGONAL to the headings, and a trap is filed by both WHERE it breaks and HOW it hides.
   *
   * BOTH ARE CLOSED, and an unknown value FAILS. An open vocabulary is a folksonomy: the fourteenth
   * synonym for "the check never ran" makes the index worse than no index, because a reader who searches
   * one of them believes they have seen them all. A value NO TRAP USES fails too — the same rule
   * `retiredApiNames` and `check-counts` carry, applied to a vocabulary: a category invented for a trap
   * that never arrived is one more wrong choice to make, protecting nothing.
   *
   * ADDING A VALUE is therefore deliberate and cheap to review: it must be a distinction a reader would
   * SEARCH by, and it must have at least one trap the day it lands.
   */
  pitfallFacets: {
    // WHERE it breaks. Deliberately the repository's own areas, not .NET's or the file's headings.
    sub: [
      'gates',        // the guard scripts (check-*), verify, dev.mjs, the dev loop
      'encoding',     // text encoding, BOM, mojibake, console codepage, line endings
      'git',          // git's own behaviour: ls-files, gitignore, the stat cache, releasing from a remote
      'build',        // building, packing, NuGet, Roslyn, the compiler, the build log
      'router',       // routing, verdict classification, streaming, fallback, timeouts, budgets
      'cli',          // spawned CLI backends: argv, exit codes, wire formats, shims, working directories
      'lifetime',     // the provider pool, cooldown, admission, decorators, disposal
      'storage',      // SQL, SQLite, FTS, migrations, connections, the store contracts
      'memory',       // the memory engine: recall, ranking, salience, seeds, forgetting, the graph
      'generation',   // the generation platform: image/video/3d, artifacts, pipelines
      'di',           // registration, options records, configuration
      'measurement',  // benchmarks, sweeps, corpora, metrics, instruments, published figures
      'docs',         // maintained prose and records: decisions, the archive, the backlog, XML docs
      'tests',        // test DESIGN — what a test does or does not actually assert
    ],
    // HOW it stays invisible. This is the half that transfers: most of these traps recur in a subsystem
    // that had never met them, so a reader who knows the SHAPE finds them and a reader who knows the area
    // does not.
    shape: [
      'fail-open',     // degrades silently to a floor; the failure is indistinguishable from agreement
      'cancellation',  // a caller's cancel confused with a component's own deadline
      'vacuous',       // a test, gate, control or assertion that cannot fail, or that examined nothing
      'scope-blind',   // the scan/filter/list excludes the very thing the check exists to catch
      'second-door',   // another path reaches the same effect and the guarantee does not cover it
      'stale-claim',   // prose, a doc, a constant, a count or a summary that stopped being true
      'silent-loss',   // data dropped, truncated, coerced or overwritten with no signal
      'wrong-subject', // the measurement or check answers a different question than the one asked
      'unmeasured',    // a mapping, format, name or value INFERRED rather than observed
      'ordering',      // precedence, ordering or nondeterminism decides the answer
      'resource',      // a leak, hang, orphan process, cache, or a process-global ceiling
    ],
  },

  /**
   * MEASUREMENT METRICS — the CLOSED vocabulary every result in `docs/memory-measurements.md` is scored
   * by, enforced by `dev.mjs check-measurements` (part of `verify`), which also GENERATES that record's
   * results index from the per-result markers.
   *
   * CLOSED for `pitfallFacets`' reason, one record over: an open vocabulary is a folksonomy, and the
   * fourth spelling of "how often the current fact beat the one it replaced" makes the index worse than
   * none, because a reader who filters on one believes they have seen them all. A slug NO RESULT USES
   * fails too — `retiredApiNames`' "an allowance that matches nothing FAILS", applied to a vocabulary.
   *
   * ADDING ONE is deliberate and cheap to review: it must be a distinction a reader would FILTER by, and
   * it must have a result the day it lands. Merging two is the commoner move — `deep@100` and `page@10`
   * arrived as separate slugs for one question (is a buried fact still reachable at depth k?) and became
   * `recovery@k` before either shipped.
   */
  measurementMetrics: [
    // RETRIEVAL — did the evidence come back at all. The bulk of the record.
    'evidence-hit@k',     // LoCoMo: share of questions whose flagged evidence is in the returned page
    'miss',               // this repository's own corpus: share of wanted entries not returned
    'miss-decomposition', // …and WHY: never a candidate, against reachable but outranked
    'recovery@k',         // is a BURIED entry still reachable at depth k (D41's invariant)
    // SUPERSESSION — LongMemEval's knowledge-update axis, where forgetting is supposed to win.
    'prefers-current',    // the current fact ranked above the one it superseded
    'current@k',          // the current fact is on the page
    'clean',              // current fact present AND the superseded one absent
    'all-evidence-recall', // temporal / multi-session: every flagged turn, not just one
    // READER-FACING — a model reads the page and answers.
    'token-f1',
    // THE MODEL-IN-THE-LOOP SEAMS — what a judge, annotator or reranker actually does.
    'endorsement-rate',   // how much of what it was shown the judge endorsed
    'screen-verdict',     // one hard case, one call: does this model answer it at all
    'regime-picks',       // which regime a gist-support rule selects
    'forced-choice-accuracy', // pick ONE of N options, exactly one of which is right — never an endorsed subset
    'false-call-rate',    // given a roster and a request NOTHING on it serves, how often a tool is invoked anyway
    // SIGNALS AND THEIR SEPARABILITY — is the number the engine computes worth anything.
    'separability-auc',
    'similar-count',
    'rrf-score-separation',
    'marker-survival',    // does a ground-truth marker survive the transform under test
    // COST — no ground truth, and never mixed with a quality column.
    'latency',
    'vram-resident',
  ],

  /**
   * DECISION-ENTRY DEBT — one entry per `docs/DECISIONS.md` decision whose NON-BLANK body still exceeds
   * `check-decisions`' own MAX_ENTRY, recorded at its CURRENT length so the number can only come down.
   *
   * WHY THIS EXISTS. Measured 2026-08-28: mean non-blank lines per entry went 11.6 (D1–D31) → 14.2
   * (D32–D63) → 38.5 (D64–D95), a 3.3x growth in what one decision costs a reader, in the file `CLAUDE.md`
   * routes every session to by RANGE. Not the entry COUNT and not data — across D64–D95 there are 40 table
   * rows and 19 lines carrying a measured figure in 1556 lines. It is prose: amendment narrative written in
   * place, and several decisions stacked under one number.
   *
   * WHY A RATCHET AND NOT A THRESHOLD, and it is the same argument `commentBlockAllowances` makes below:
   * 21 entries were already over the limit when the gate landed (251 lines above it), so a plain threshold
   * would have had to be switched off on day one. An allowance is a DEBT, not a permission — an allowance
   * looser than the entry needs FAILS, and so does one at or below the limit, which is simply rot.
   *
   * TO PAY ONE DOWN: move MEASUREMENT narrative to the design record that owns it (untracked, under
   * `local/superpowers/`) and AMENDMENT narrative to git history, keeping the decision, the alternatives,
   * and what it constrains. **This is NOT the archive's compression** — a decision's reasoning is its
   * payload, and `persist-working-state.md` says that without the reason someone reverses it later and
   * rediscovers the problem. Deleting an entry from this ledger is the goal, not an edge case.
   *
   * There is deliberately NO escape token — see `check-decisions.mjs` for why an allowance is the only way
   * out.
   */
  decisionLengthAllowances: {
    D63: 39,
    D64: 36,
    D66: 49,
    D67: 40,
    D69: 36,
    D70: 38,
    D71: 38,
    D72: 55,
    D73: 52,
    D74: 36,
    D76: 42,
    D77: 52,
    D78: 38,
    D85: 44,
    D86: 42,
    D89: 37,
    D91: 55,
    D93: 52,
    D94: 41,
  },

  /**
   * ARCHIVE-ENTRY DEBT — one entry per `docs/task-archive.md` Part still over `check-archive`'s MAX_ENTRY,
   * recorded at its CURRENT length so the number can only come down.
   *
   * WHY A RATCHET AND NOT A THRESHOLD, and why the gate exists at all. The rule
   * (`.claude/rules/task-lifecycle.md` §"An archive entry is an OUTCOME and a POINTER") was written on
   * 2026-09-02 from a measurement — mean non-blank lines per entry in thirds, 8.0 → 6.1 → 22.2 — and
   * answered with prose. It KEPT GROWING: re-measured 2026-09-04 at 7.5 → 6.7 → 23.2 over 142 entries. A
   * written-down rule that is still violated is a missing gate, the same reasoning behind `check-encoding`
   * and `check-links`.
   *
   * TO PAY ONE DOWN: strike every sentence a reader could get from the record that OWNS it — the
   * measurement record (`docs/memory-measurements.md` §5 for the memory Parts, which is where most of this debt sits),
   * `docs/DECISIONS.md` for a choice, `docs/FIXES.md` for an incident, `.claude/knowledge/pitfalls.md` for
   * a reusable trap. RELOCATE BEFORE DELETING: several of these entries are the only maintained home for a
   * trap, and cutting one without moving it first loses it. Then lower or delete the entry here.
   *
   * There is deliberately NO escape token — see `check-archive.mjs`.
   */
  archiveEntryLengthAllowances: {
    'Part 106': 22,
    'Part 108': 24,
    'Part 110': 26,
    'Part 111': 24,
    'Part 114': 25,
    'Part 115': 25,
    'Part 117': 21,
    'Part 118': 22,
    'Part 120': 22,
    'Part 121': 27,
    'Part 122': 22,
    'Part 123': 26,
    'Part 127': 26,
    'Part 130': 26,
    'Part 131': 22,
    'Part 133': 23,
    'Part 134': 22,
    'Part 135': 23,
    'Part 136': 24,
    'Part 146': 22,
    'Part 148': 24,
  },

  /**
   * COMMENT-BLOCK DEBT — one entry per file whose worst contiguous comment block still exceeds
   * `check-comments`' own MAX_BLOCK, recorded at its CURRENT worst so the number can only come down.
   *
   * WHY A RATCHET AND NOT A THRESHOLD. 49 files were already over the limit when the gate landed (1879
   * lines of block), so a plain threshold would have had to be switched off on day one. An allowance here
   * is a DEBT, not a permission: `check-comments` FAILS an allowance that is looser than the file actually
   * needs, so improving a file forces its number down and a later regression back to the old size is
   * caught. Same discipline as `retiredApiNames`' "an allowance that matches nothing FAILS", expressed as
   * a budget.
   *
   * TO PAY ONE DOWN: move the design argument to the record that owns it (`docs/DECISIONS.md` for a
   * choice, `.claude/knowledge/pitfalls.md` for a trap, the design contract for semantics), keep the RULE
   * plus a pointer in the code, then lower or delete the entry. The rule is
   * `.claude/rules/code-commentary.md`. Deleting an entry outright is the goal, not an edge case.
   */
  // One entry per FILE, holding EVERY over-limit block in it, worst first — see `check-comments.mjs`
  // `asAllowanceList` for why a single number per file was not a ratchet.
  //
  // 2026-08-16: the paydown took this from 28 blocks / 893 lines across 19 files to ONE block / 31 lines.
  // The survivor is `IMemoryGraphStore.SeedAsync`, and it is the only entry here that has ever been argued
  // irreducible rather than merely unpaid: it states SEVEN distinct guarantees a BYO store must honour, at
  // 2–11 lines each. The one paragraph that was NOT a guarantee — six lines defending the admission rule
  // against a misreading it had already stated — is what came out.
  // One entry per FILE, holding EVERY over-limit block in it, worst first — see `check-comments.mjs`
  // `asAllowanceList` for why a single number per file was not a ratchet.
  //
  // SCOPE WIDENED 2026-08-16 to `tests/`, `devtools/` and `bench/`. `src/` had been paid down to one block
  // and the other three tiers were never scanned at all — while holding LONGER blocks than anything left in
  // `src/` (worst 88 against 31). The gate's own header had argued the scope was deliberate, on the grounds
  // that a test explaining its fixture and a guard header explaining its own false PASS are doing the job
  // the rule asks. Sampling says that is HALF true: the worst block in `tests/` states a real constraint on
  // what any number measured from that corpus may claim, and also carries a dated heading plus a long
  // narration of what an earlier version did wrong. Same defect, lower density.
  //
  // So they are RATCHETED rather than paid down: nothing here has to shrink today, and nothing can grow.
  // That is exactly how `src/` started — 49 files on allowances — and it removes "unscanned" as a category.
  commentBlockAllowances: {
    "bench/Lyntai.Benchmarks/MemoryBoundedGrowthSweep.cs": [36],
    "bench/Lyntai.Benchmarks/MemoryEnrichmentSweep.cs": [32],
    "bench/Lyntai.Benchmarks/MemoryLanguageSweep.cs": [41],
    "bench/Lyntai.Benchmarks/MemoryPolicySweep.cs": [79],
    "bench/Lyntai.Benchmarks/MemoryReinforcementSweep.cs": [33],
    "bench/Lyntai.Benchmarks/MemorySalienceSweep.cs": [33],
    "bench/Lyntai.Benchmarks/MemorySpacingSweep.cs": [38],
    "bench/Lyntai.Benchmarks/MemoryVerificationSweep.cs": [27],
    // `devtools/dev.mjs`'s entry is DELETED, 2026-09-10, and the way it went is the useful part. It had
    // climbed 31 → 32 → 33 → 34 because the block was a USAGE BANNER, one line per command, so registering
    // a gate grew it by exactly one and each bump was waved through as deliberate. Adding
    // `check-measurements` made it 35 and the ratchet fired again — at which point the right question was
    // finally asked: the banner named 30 of 52 commands, the FOURTH hand-maintained copy of a list D113 had
    // already made derived twice. It is now a pointer, and the block is under the limit with no allowance.
    // **A ratchet that keeps being raised by one is measuring something that should not exist.**
    "devtools/nuget-unlist.mjs": [28],
    "devtools/scripts/check-api-vocabulary.mjs": [34],
    "devtools/scripts/check-comments.mjs": [41],
    "devtools/scripts/check-samples.mjs": [55],
    "devtools/scripts/check-version-bump.mjs": [30],
    "src/Lyntai.Core/Memory/IMemoryGraphStore.cs": [31],
    "tests/Lyntai.Tests/Memory/Corpus/MemoryCorpus.cs": [88, 35, 27],
    "tests/Lyntai.Tests/Memory/Corpus/RecallQuality.cs": [40],
    "tests/Lyntai.Tests/Memory/DsrPathologyTests.cs": [41],
    "tests/Lyntai.Tests/Memory/GraphMemoryEngineTests.cs": [30, 26],
    "tests/Lyntai.Tests/Memory/GraphMemoryRankingGoldenTests.cs": [63],
    "tests/Lyntai.Tests/Memory/GraphMemoryReviewLogTests.cs": [27],
    "tests/Lyntai.Tests/Memory/GraphMemoryWiringTests.cs": [46],
    "tests/Lyntai.Tests/Memory/LlmSemanticRecallLiveTests.cs": [31],
    "tests/Lyntai.Tests/Memory/LlmVerificationLiveTests.cs": [33, 28],
    "tests/Lyntai.Tests/Memory/MemoryAgePrimitiveIdentityTests.cs": [26],
    "tests/Lyntai.Tests/Memory/MemoryCjkRecallTests.cs": [38],
    "tests/Lyntai.Tests/Memory/MemoryClusterEdgeFormationTests.cs": [26],
    "tests/Lyntai.Tests/Memory/MemoryDefaultRecallQualityTests.cs": [76, 47],
    "tests/Lyntai.Tests/Memory/MemorySalienceInversionTests.cs": [31],
  },
};
