# Glossary — the names Lyntai's code and documents use

The namespace map and the vocabulary a rename must keep. Read it before naming or placing a type, or
renaming anything: a rename sweep breaks these without failing anything. The shared
`dotnet-package-layout` rule, `.claude/rules/dotnet-package-layout.md` and `.claude/rules/repo-mechanics.md`
§Naming state the naming rules; this is what they produced here.

**Namespace map (Core).** Builder + `Add*`/`Use*` extensions live in `Lyntai`.

- `Lyntai.Inference`: everything about CALLING a backend, flat (**D154**) — the provider seam, the verdict
  taxonomy, the four call shapes, the text front door and both routers (`TextRouter`, `MediaRouter`: peers
  **D153** refused to merge). Plus `.Cli` (ONE engine; a new CLI is an `ICliBackend`, **D21**/**D159**) and
  the front-door decorators `.Caching` / `.Budgeting` / `.RateLimiting` / `.Streaming`.
- `Lyntai.Generation` (+ `.Jobs` / `.Tools`): what RUNS a generation. The media backends are the separate
  `Lyntai.Generation` package, namespace `Lyntai.Generation.Providers`.
- `Lyntai.Memory`: the engines (`.Engines`), semantic memory, the vector store.
  The graph-memory DOMAINS are SEVEN: `.Interference` / `.Forgetting` / `.Modulation` / `.Salience` /
  `.Ranking` / `.Annotation` / `.Verification`, each an `IMemory*Policy` seam with its implementations and
  options, placed by OWNERSHIP.
  `.Seeding` (it produces candidates) and `IMemoryRemovalPolicy` (a blend concern, **D72**) are not domains,
  and a root-level `IMemory*Policy` without a recorded reason fails `check-counts`.
- `Lyntai.Prompts` · `Lyntai.Cortex` (+ `.Scorers`) · `Lyntai.Agents` · `Lyntai.Jobs` · `Lyntai.Guards` ·
  `Lyntai.Secrets` (shared with `Lyntai.Secrets.Dpapi`) · `Lyntai.Storage` · `Lyntai.Processes` ·
  `Lyntai.Text` · `Lyntai.Diagnostics`. Adapters are `Lyntai.Providers.<Backend>` /
  `Lyntai.Storage.<Backend>`, one segment per adapter.

**Vocabulary** — what a rename sweep breaks without failing anything:

- A CALL SHAPE is named for what it PRODUCES: `Text*`, `Vector*`, `Score*`, `Media*` (`ITextClient`,
  `TextRouter`).
- `Llm` stays wherever it means "this asks a language model": `LlmScorerBase`, `LlmMemoryVerificationPolicy`,
  `IScorer.IsLlm`, the `is_llm` column in both SQL backends and the `"llm"` score group.
- A PROVIDER is named for its BACKEND, and what it produces is DATA (`ProviderCapabilities.Produces`,
  **D152**): one `Add<Backend>Provider(…)` per backend, EF Core-style, never an `Add<Kind>Provider`. A kind
  never forks a provider class; an options field selects it (`HttpModelOptions.Produces`,
  `OnnxProviderOptions.Produces`, **D157**), and is a list only where one backend really serves several
  (`ComfyUiOptions`, `FalOptions`).
- The noun is `Vector` (what `Produces` selects on: `ProviderKinds.Vector`, `IVectorStore`) and the verb is
  `Embed` (`EmbedAsync`, `CanEmbed`).
- `Valid` names WHEN A FACT HELD IN THE WORLD (`MemoryValidity`, `MemoryValidityReading`, `ValidFrom`/`ValidTo`),
  never when the engine learned it (`CreatedAt`) and never how well it is remembered (retrievability). `Temporal` is
  the capability adjective (`ITemporalMemory`, `ITemporalMemoryGraphStore`) and "valid time" the axis. To SUPERSEDE
  is to end an entry at a successor's start because a caller named it (`MemoryWrite.Supersedes`) — it never deletes,
  buries or reinforces, and the engine never infers one.
