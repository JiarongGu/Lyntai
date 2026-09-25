---
name: extending-lyntai
applies_when: adding an LLM provider, a storage backend, a scorer, a CLI tool-hosting connector, a generation backend, or a migration
enforces: an interface in Lyntai.Core + an implementation in an adapter (never adapter→adapter) + one LyntaiBuilder extension; a new package only when the dependency footprint earns one, scaffolded by new-package
---

# Extending Lyntai

On-demand detail for the six extension points. Read the one you're touching. The always-on rules are
in `.claude/rules/` — `dotnet-package-layout.md` for the boundaries, `repo-mechanics.md` for this repo's
bindings; the correctness invariants are in `llm-and-router.md` and `storage.md`; the traps are in
`pitfalls.md`.

Lyntai's whole value is being extended without forking. Every extension is **an interface in
`Lyntai.Core` + an implementation in an adapter package that depends only on Core** (never adapter →
adapter) + **a `LyntaiBuilder` extension method** so the consumer wires it with one line.

## A duplicate NAME: two rules, and which one you are under is not guessable

Every seam here keys its members by a string, and a duplicate makes one of the two unreachable. **Half of
them THROW and half take first-wins silently** — the same one-sentence justification ("one of the two would
be unreachable") reaching opposite conclusions, which is why it is written down rather than left to be
inferred from whichever seam you happened to read.

| seam | a duplicate | why |
|---|---|---|
| `CompositeMemoryEngine` members, `AddTextClient` names | **throws** | the name is an ADDRESS a caller uses — an entry's `MemoryRef` must name one owner, and a client name must resolve to one client |
| `IModelProvider` ids (`TextRouter`), `ITool` names, `IJobHandler` types | **first-wins, silent** | the collection is a FALLBACK LIST the router walks; it also folds case, so refusing would reject registrations that differ only in case and are already merged one step earlier |

**Do not "fix" the second row.** `TextRouter` builds its lookup with `TryAdd` over a case-insensitive
dictionary deliberately (see its own comment on why an ordinal table was worse), and the pooling and
cooldown paths key on the same id.

**What this costs you when adding a backend: give every `*Options.Id` a distinct value per registration.**
Two `AddOnnxProvider` calls left on the default id are two backends where the second is invisible to the
router — and, since **D139** makes a capability declaration the wiring, it also decides which one
`AddMemoryScoringVerification` picks unless `ScoringVerificationOptions.ProviderId` names one (**D148**).
Nothing warns; the second model simply loads, occupies memory, and is never called.

---

## Add an LLM provider

Four paths — pick the cheapest one that reaches your backend:

**A. Is the backend reachable over HTTP on a wire Lyntai already speaks? Then it is already supported
(preferred).** OpenAI, Azure, Ollama,
OpenRouter, vLLM, llama-server, Groq, DeepSeek and most of the rest ship such an endpoint. You do *nothing*
but register: `builder.AddHttpProvider("my-id", o => o.BaseUrl = …)` for anything OpenAI-shaped (or its
vendor preset, whose options overload — `AddLlamaProvider("llama", o => …)` — takes the same knobs),
`builder.AddOllamaProvider(…)` for Ollama-native (**D160**) — one
registration serves chat, embeddings or reranking depending on `Produces`. **Only write a native provider if
no shipped wire reaches it** — which, since **D146** deleted the Microsoft.Extensions.AI bridge, means a vendor
whose wire format is genuinely its own.

**A3. Reachable but its OWN wire format → a BRIDGE, which is a lambda.**
`builder.AddBridgeProvider("my-id", (req, ct) => …)` turns anything that already answers into a routed
backend — a vendor SDK, an in-house service, a `Microsoft.Extensions.AI` `IChatClient`. You write only the
mapping you need; routing, fallback, cooldown, admission and the ops layer come along, and the library takes
no dependency on whatever you wrapped (**D147**). **Return a non-Ok `ProviderVerdict` rather than throwing**,
so the router can advance to the next candidate. A bridge declares only the operations you hand it a
delegate for — omit `stream` and no router will ask it to stream.

**A2. A SPAWNED CLI → an `ICliBackend` plus a thin provider.** If the backend is a command-line agent
(`claude`, `codex`, or a sibling), do NOT re-implement the spawn/verdict/streaming rules — they are already in
`CliProviderEngine` (Core, `Lyntai.Inference.Cli`), and re-deriving them is exactly how they drifted apart before
(D21/D159). Read `ClaudeCliBackend` and `CodexCliBackend` side by side first: they are the two worked examples, and
their differences (stdin vs. required repo-check flag, JSON vs. prose auth, `auth logout` vs. top-level
`logout`, pinning vs. no pinning) show what a backend class is for. Derive from `CliBackendBase` and supply
only what is specific to that CLI:

<!-- compile-skip: a backend sketch: its member bodies are elided for illustration -->
```csharp
public sealed class MyCliBackend : CliBackendBase
{
    public override string Id => "my-cli";
    public override string DefaultCommand => "mycli";                   // resolved on PATH (shim-safe)
    public override IReadOnlyList<string> CommandEnvironmentVariables    // shared stub seam first
        => ["LYNTAI_PROVIDER_CMD", "MYCLI_CMD"];
    public override IReadOnlyList<string> BuildCompletionArgs(                // TWO parameters
        TextRequest r, IReadOnlyList<string> toolHostArgs) => ["exec", "--json"];
    public override CliOutputEvent ParseLine(string line) => /* → Content / Result / Ignored */;

    // OPTIONAL, and only when VERIFIED against the real binary (see below):
    public override IReadOnlyList<string>? UpdateArgs => ["update"];
    public override IReadOnlyList<string>? AuthStatusArgs => ["login", "status"];
}
```

Then a ~40-line provider that composes engine + backend and declares which optional capabilities the
backend *actually* has (`IModelProvider` / `IProviderUpdater` / `IProviderVersionInstaller` /
`IProviderAuth`) — copy `ClaudeCliProvider`, which is nothing but forwarding members. The engine owns:
command resolution (`CliCommand.Resolve(command, backend)` — the override, then the backend's environment
variables, then its default executable), neutral cwd, prompt delivery (stdin or trailing argument — set
`PromptDelivery`), the inactivity clock (plus an absolute backstop on the BUFFERED path only — a streamed turn
is bounded by provider inactivity and the caller's token, nothing else), the fault → verdict map (`CliFault`),
empty→`Failed`, streaming order, and probe → run → re-probe maintenance. An agent session over the same CLI
resolves its command and classifies its faults through the same two, and composes the shared turn loop
(`CliAgentLoop`, `Lyntai.Providers.Basic`) rather than a second one: a new session supplies only its argv,
its stdin and its line reader.

Rules specific to this path:
- **Never name a maintenance command you haven't verified against the real binary** (`--help` it). The base
  class claims NOTHING optional by default for this reason. A CLI that treats an unrecognized token as a
  prompt will answer it — spending tokens on every call while the build stays green (`pitfalls.md`).
- **Never forward a free-form value into argv.** `TryBuildLoginArgs`/`TryBuildInstallArgs` must REFUSE a
  mode the backend doesn't have and a flag-shaped value (`FlagShaped` on the base) — `ArgumentList` stops
  shell injection, not the backend's own option parser.
- **Map an in-band failure to `CliOutputEvent.Failure`, and ONLY the terminal one.** A CLI can report a failed
  turn in its output and still exit 0 (codex does). But error-ish lines that aren't terminal — a retry notice,
  a warning item — must stay `Ignored`, or healthy calls fail on retries they recovered from.
- **Check what your CLI assumes about its working directory.** The engine spawns from a neutral temp dir; codex
  needs `--skip-git-repo-check` because of it.
- **`SupportsToolCalls` is the BACKEND's declaration, and the provider DERIVES its capability from it.** The
  provider is the capability declarer (D21), so the shipped CLI providers build `ProviderCapabilities` through
  `CliComposition.Capabilities(backend)` and the two cannot disagree; a new CLI provider does the same. **It
  is NOT a member of `IModelProvider`**: `public bool SupportsToolCalls => true;` on your provider compiles and
  is read by nothing, `TextRouter.GetCapabilitiesAsync` then reports no tool calls, and `ToolLoop` silently
  takes the prompt-based fallback on a backend that can do native tool calls.
- **Portable installs are free if you don't fight them** — the host passes `command` (+ `environment`) to your
  builder extension (D22); pass both straight through to the engine and don't read env vars yourself. The
  extension also takes an `id` defaulting to the BACKEND's name (`claude-cli`, `codex-cli`), because the router
  keeps the first provider per id: two installs of one CLI are two registrations with two ids. Resolve the
  tool provisioner through `CliComposition.Provisioner(sp, id, defaultId)`.

**B. Native `IModelProvider`** for anything else (like `HttpModelProvider`). **Where it lives is a
FOOTPRINT test, not one-package-per-backend** (`docs/DECISIONS.md` D25): a CLI backend or native provider that
needs nothing beyond Core/BCL — or only managed `Microsoft.Extensions.Http` — is a class in
`src/Lyntai.Providers.Basic/`, where `ClaudeCliBackend`, `CodexCliBackend`, `ClaudeCliProvider`,
`CodexCliProvider` and `HttpModelProvider` already live; namespaces stay `Lyntai.Providers.<Name>`
inside the one assembly (D25), so nothing an author writes changes. It earns its own
`src/Lyntai.Providers.<Name>/` package (ref Core only, never adapter→adapter) only when it drags a native
runtime, a platform-specific API, or a dependency a consumer might refuse — `Lyntai.Providers.LlamaSharp` is the
worked example. When it does earn one, scaffold it with `node devtools/dev.mjs new-package <Lyntai.X>`: a
package must enter NINE registries, `check-packages` gates them, and the misses are silent (no
`ApiSurfaceTests` entry means no API gate at all). Never register them by hand — and remember a published
package id can never be freed (D23), so a needless one is permanent. Implement:

<!-- compile-skip: a provider signature with its constructor parameters elided (`/* options, factory */`) -->
```csharp
public sealed class MyProvider(string id, /* options, factory */, LyntaiOptions options) : IModelProvider
{
    public string Id => id;                 // the candidate id the router selects on
    public ProviderCapabilities Capabilities => /* Accepts / Produces / Operations */;   // NO default: with Id, one of the two members you must write
    public bool IsAvailable => /* cheap check; real failures surface as verdicts, not here */;
    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default);
    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default);
}
```

Non-negotiables (see `llm-and-router.md` for why — the router trusts every provider to honor these):
- **Classify failures with `ProviderVerdictClassifier`** — never hand-roll substring heuristics; a second
  copy drifts. An HTTP backend classifies through the THREE-argument
  `ProviderVerdictClassifier.FromHttpFailure(status, body, hasCredentials)` (copy `HttpModelProvider`): an
  uncredentialed 401/403 is `NotConfigured`, not `AuthFailed` (`llm-and-router.md` §Verdict taxonomy, D31). An
  in-band error text classifies through `FromErrorText(text, hasCredentials)` for the same reason.
- **Empty/no output is `Failed`, not `Ok`** — both in `CompleteAsync` and as a terminal `Error` chunk in
  `StreamAsync` (a zero-content stream must let the router fall over, not report a clean empty answer).
- **Streaming iterates `GuardedStream.ReadAll`**, whose inactivity clock is the timeout — never a single
  `CancelAfter` over the whole stream (`llm-and-router.md` §Provider streaming timeout).
- **Only yield `TextChunk.Content` for non-empty text**; end with exactly one `Final` (with usage) or
  `Error`.
- Spawning a CLI? Go through `ProcessRunner` (ArgumentList only, prompt via stdin, BOM-less UTF-8,
  kill-tree). Never build a provider that shells out directly.

Builder extension (in the adapter package, extending Core's `LyntaiBuilder`):
<!-- compile-skip: an extension-method sketch with its registration arguments elided -->
```csharp
public static LyntaiBuilder AddMyBackend(this LyntaiBuilder b, string id, Action<MyOptions> cfg)
{
    // register the provider into the IEnumerable<IModelProvider> collection; resolve deps from the container
    b.AddProvider(sp => new MyProvider(id, /* … */, sp.GetRequiredService<LyntaiOptions>()));
    return b;
}
```
Tests: drive it against a stub, never a live endpoint — an HTTP provider gets a stubbed
`HttpMessageHandler` (`Fakes/StubHttpHandler`); a CLI provider gets the `provider-stub.mjs` via
`LYNTAI_PROVIDER_CMD`. Cover each verdict, streaming order, and empty→Failed.

**C. NOT a chat backend? Then implement the seam whose SIGNATURE is yours.** A vector backend implements
`IVectorProvider` and a rerank backend `IScoreProvider` — each an `IProviderCall<TRequest, TResponse>` with
one `CallAsync` — and reports failure as a VERDICT beside an empty vector or score list, because there is no
vector and no score meaning "I could not" and a zero ranks as confidently as a real number (**D153**). The
verdict rules above apply unchanged. Declare `Produces = [ProviderKinds.Vector]` or `[ProviderKinds.Score]`,
and **the declaration is the wiring**: a seam that consumes the kind finds you, so a cross-encoder needs no
reranker-shaped registration and no policy of its own — `AddMemoryScoringVerification` already selects on
`Score` (**D139**), and `OnnxProvider` registered with `Produces = ProviderKinds.Score` is the worked example.
Register with `AddProvider` like every other backend — there is no role-named registration to choose between
(**D152**). **Pass `declares` when a FACTORY produces `Vector`**: `AddSemanticMemory` decides at composition
time, before anything is built, so an undeclared factory reads as "does not embed" and that call fails fast
naming the argument. A backend built eagerly hands over its own `Capabilities` and restates nothing.

**And a second backend in an EXISTING adapter package changes no registry** — that is the whole payoff of
the footprint test. `check-packages` gates the nine a new package must enter; a class beside one that
already isolates the same dependency enters none of them, and the only generated artifact to refresh is the
API-surface baseline (run the test, promote the emitted `.actual`, and read the diff: purely additive lines
are a minor, a changed or missing line is a break).

### A provider is the ENGINE and stays pure — the model is EF Core's (D157)

**Never fork a provider class because it produces a different kind.** The pattern to copy is EF Core's:
the core is provider-agnostic; a provider is a PACKAGE with one `Add<Backend>Provider(…)` entry point named
for the backend; provider-specific knobs live in that call's options action; and the provider supplies its
own seams behind interfaces `Lyntai.Core` never sees. EF has no `UseSqlServerForReads()`, and there is no
`Add<Kind>Provider` here for the same reason.

So when one runtime serves two kinds, what varies is an internal **head**, not a class:

<!-- compile-given: string embedDir = ""; string rerankDir = ""; -->
```csharp
cfg.AddOnnxProvider(embedDir);                                  // default: Produces = Vector
cfg.AddOnnxProvider(rerankDir, o =>                             // same class, same engine
{
    o.Id = "onnx-rerank";                                       // one session is one graph, so one id each
    o.Produces = ProviderKinds.Score;                           //             → Score
});
```

**`Produces` is the whole of the consumer surface** — the same field, doing the same job, as
`HttpModelOptions.Produces`: *the field that decides how a call is encoded, which output is read, and which
methods the provider answers*. Which internal head serves it is not consumer surface at all; the seam
lives in `Lyntai.Providers.Onnx` and is `internal`, which is the EF property — a provider package grows its
own seams without Core, or a consumer, learning they exist.

**A backend MAY serve several kinds, and that is said in data.** `ProviderCapabilities.Produces` is a LIST
for one call returning several. Whether a backend's OPTIONS take one kind or many mirrors what it can
actually do — `ComfyUiOptions`/`FalOptions` take a list because one workflow host serves image AND
video; `HttpModelOptions` and `OnnxProviderOptions` take one, because one registration is one route and one
session is one graph. Copy the side your backend is actually on.

**Where the EF analogy STOPS:** EF binds one provider per `DbContext`; Lyntai registers many and ROUTES
across them with fallback and cooldown. A provider here is a candidate, not a choice — which is exactly why
what it produces must be DATA the head states rather than a class the consumer picks between.

---

## Add a generation backend

The media seam (image / video / audio / 3d) behind one capability-aware contract. Same shape as everything
else: **the CONTRACTS are in `Lyntai.Core`** — the call shape (`MediaRequest`, `MediaResponse`,
`IMediaJobProvider`, `QueuedOperation`), `MediaRouter` and `ProviderVerdictClassifier` under `Lyntai.Inference`,
and what RUNS a generation (pipelines, jobs, tools) under `Lyntai.Generation`, `.Jobs` and `.Tools` — while
the BACKENDS live in the `Lyntai.Generation` package under `Lyntai.Generation.Providers`, and each ships a
one-line `builder.Add<Name>Provider(...)` shim over `AddProvider(sp => …)` plus `AddMediaRouting()`. Every
builder method names what it REGISTERS with the vendor as the qualifier (**D137**), so the suffix is on
both — and the qualifier names the ENGINE, never what the backend produces (**D152**, **D156**): every
shipped media preset is `AddOpenAiImageProvider`, `AddAutomatic1111Provider`, `AddComfyUiProvider`,
`AddFalProvider`, `AddLocalDiffusionProvider`, `AddPiperProvider`. There is no `Add<Domain>Provider`,
because a domain is not a kind of provider — it is a `ProviderCapabilities.Produces` value.

**Put anything you are unsure of behind an OPTION rather than a literal** (**D69**): a wrong guess is then a
consumer's configuration edit, not a breaking change. The package carries the same SemVer promise as every
other (**D70**).

What a backend implements:
- **`IModelProvider`** — `Id`, `Capabilities` (read by the router BEFORE spending anything),
  `ProbeAsync` and inline `GenerateAsync`. Both must **FAIL SAFE**: a value with a verdict, never a throw
  (cancellation propagates). `ProbeAsync` must **never generate** to answer a setup question — the
  generate-and-discard pattern it replaces bills a render to find out whether a key works.
- **Inline and STREAMING are declared in DATA, the stateful JOB protocol is still an interface** — and
  which of the two a mode is decides how it breaks. `ProviderOperation.Complete` / `.Stream` go in
  `ProviderCapabilities.Operations`, so the failure is a DECLARATION/IMPLEMENTATION mismatch the router
  reports ("advertises Stream delivery but does not implement"). `IMediaJobProvider`
  (submit → poll → fetch, for queued/long renders) is an ADDITIONAL interface the router type-tests, which
  is why nothing may wrap a provider in a decorator implementing only the base seam (see `pitfalls.md`) —
  a decorator erases the type test and every video render silently stops routing while image renders keep
  working. **The two halves fail differently: a wrong FLAG mis-routes, a lost INTERFACE mis-routes
  silently.** Before **D127** all three were interfaces; it moved two into data and kept the third,
  because submit → poll → fetch → cancel is a contract SHAPE rather than a content type.
- **Classify through `ProviderVerdictClassifier`, never a second copy of "what does a 429 look like"**:
  `FromHttpFailure(status, body, hasCredentials)` for an HTTP answer (an uncredentialed 401/403 is
  `NotConfigured` — `llm-and-router.md` §Verdict taxonomy), and `FromThrown` for an exception an INLINE call
  caught, which never reads a throw as `Refused`. A queue backend in this package shares `QueueCalls` (the
  poll and fetch deadlines, and what a failed status call reads as) and a one-source-image backend
  `SingleInitInput` (an input it cannot place is refused, never dropped); both are internal.
- **Declaring `ProviderOperation.Queued` sends a pipeline stage to your submit path — but not always.** The
  pipeline job gives a stage the door of its FIRST capable candidate, queued wherever that candidate declares
  `Queued` for the request and implements `IMediaJobProvider` (**D181**). A backend declaring both doors is
  therefore submitted when it leads the list, yet still asked INLINE in two cases: when an inline-only candidate
  precedes it, the router's inline door reaches it in turn; and when the queued door is exhausted — its own
  submission refused included — the fallback asks it inline. So declare only the doors you implement, and serve
  each one honestly. **A failed submission's verdict steers the fallback**: `Unsupported` sends the stage to the
  other door's candidates, while a surfaced `Refused` ends it.
- **A submit whose outcome is UNKNOWN is `QueuedOperation.Inconclusive`, and is never re-submitted.** A
  backend that ANSWERS "no" can be retried elsewhere for free; a backend that never answered may already hold
  a billable render, and handing the same request to the next candidate buys the same generation twice. The
  router surfaces such a submission instead of advancing, and does not count it toward the dead-host
  threshold — no answer is no evidence of ill health either. **A backend that catches its own exceptions
  applies that rule itself**, because the router's throw rules never see a caught throw: a thrown submit is
  `QueuedOperation.FromThrownSubmit(ex, sent)` (Inconclusive once the request may have left the process,
  unless the throw proves it never did); any other failed submission is `QueuedOperation.Failure(detail,
  verdict)`; and a 2xx submit answer with no operation id is Inconclusive too.
- **MEASURE the wire format before shipping it.** Of the six backends here, three are measured against a
  real engine (`sd-cli`, ComfyUI, piper); two are PORTED from a sibling app's production implementation and
  say so (`Automatic1111`, `OpenAiImage`) — someone else's evidence, which is not none and is not ours; and
  one is written from vendor documentation with no key to call it (`fal`, `TASKS.md` Part 33). One of the
  three measurements found its mapping WRONG — the engine had retired the `img2img` mode value, so every
  img2img render failed at the argv parse while the build stayed green. Do not add another unmeasured one: a
  mapping derived from vendor docs is a guess wearing a type.

- **A backend whose output is BINARY streams it through `IProcessRunner.StreamBytesAsync`** (**D165**), not
  the line-shaped sibling — 0x0A is data in a PCM frame, not a line break. Declare
  `ProviderOperation.Stream` beside `Complete`, yield `MediaChunk.Content(bytes, mediaType)` and stamp the
  media type on EVERY chunk (a raw wire carries no metadata of its own), then one `MediaChunk.Completed`.
  `PiperProvider` is the worked example, and its buffered `GenerateAsync` is literally its own stream
  collected, so the two modes cannot disagree. **The BYO consequence is real**: that member is DEFAULTED and
  its default THROWS rather than degrading, so a host running a sandboxed `IProcessRunner` must implement it
  before a byte-streaming backend can route through them.

Before writing code, read the four generation traps already recorded in `pitfalls.md` — `TimeSpan.Zero` means
"no deadline" here and "cancel instantly" on the LLM side; a cooldown keyed on the provider id benches other
tenants; decorating a provider erases `IMediaJobProvider` so every video render silently stops routing
while every image render keeps working; and `MediaRouter`'s `Surface` arm returns one frame shallower
than the admission permit it depends on.

---

## Add a storage backend

A storage backend that drags a database driver earns its own package (a consumer might refuse the driver); one
needing nothing beyond Core joins `Lyntai.Storage.Basic` instead, in a folder of its own (**D173**). A new package:
new package `src/Lyntai.Storage.<Backend>/`, ref Core only — scaffolded with `node devtools/dev.mjs
new-package Lyntai.Storage.<Backend>`, which registers it in all NINE registries `check-packages` gates.
Never hand-roll the csproj; the misses are silent.

Implement the domain interfaces the consumer needs — they're independent, and you don't have to do all of
them. A backend implements up to **twelve**: the eight in `src/Lyntai.Core/Storage/` (`IKeyValueStore`,
`IConversationStore`, `IMemoryStore`, `IScoreStore`, `ITraceStore`, `IPromptVersionStore`, `IJobStore`,
`ICuratedMemoryStore`) plus `IVectorStore` (`Memory/`), `IResponseCache` (`Inference/Caching/`), `IUsageTracker`
(`Inference/Budgeting/`) and **`IMemoryGraphStore` (`Memory/`)**. A thirteenth storage seam,
`IModelRoutingStore`, is never yours to write: Core serves it as `KeyValueModelRoutingStore` over your
`IKeyValueStore`.

**`IMemoryGraphStore` is the LARGEST contract in the storage layer, and the easiest to plan a backend without.**
It is also the only one with a per-backend migration asymmetry (`.claude/knowledge/storage.md` §Migrations)
and the only one whose contract pins an ORDER (`WriteBackAsync`, **D101**). If you are backing the memory
engine, it is most of your work; if you are not, skip it deliberately, never by omission.

**`IMemoryGraphStore` has SIXTEEN members; TWO carry a default body, and each default costs something
different.** `LinkManyAsync` (**D99**) and `WriteBackAsync` (**D101**) default to the calls the engine used
to make one at a time, so a BYO store loses no behaviour and is merely no faster. And `WriteBackAsync` carries
an ORDER as contract — the review log last, so a broken log cannot cost the touch or the edges — so an
override that reorders it is wrong however fast it is. `KnownSubjectsAsync` has NO default: a store that
answered with an empty list would silently get **no subject seeding** at all (**D88**), so it must return the
subject handles in use under a task and scope, most-used first.

Mirror `src/Lyntai.Storage.Postgres/`, the reference backend, which implements all twelve. **A backend that is
not a database mirrors `src/Lyntai.Storage.Basic/FileSystem/` instead** (**D171**) — no SQL and no migrations, but
the same contract suites, plus RESTART tests those suites cannot express, since each runs one live store. An
in-process `IMemoryGraphStore` in that package runs its internal `MemoryGraphState` rather than re-implementing
it (**D174**) — plan a change, persist it, apply it. Provide
`builder.Use<Backend>Storage(...)` that registers an `IDbConnectionFactory` (or the backend's equivalent) +
the stores + runs migrations.

Two seams the list alone doesn't reveal:
- **`IJobStore` goes through `Core/Storage/JobStoreSql.cs`** — the job state machine (transition statements,
  the `claimed_by` write fence, the claim-candidate predicate) plus the `JobRow` mapping are SHARED on
  purpose, because drift there is a correctness bug; only the locking frame is per-dialect (`storage.md`
  §Don't "dedup" the Sqlite/Postgres stores).
- **A Governance-backed `Use*` helper needs the startup guard.** `lyntai_vector`, the response cache and
  the usage ledger all ship under `StorageFeature.Governance`, so those helpers must reject a Governance-less
  subset at wiring time rather than at first use. The guard is written once, in
  `src/Shared/Relational/GovernanceGuard.cs`, and a relational package links that source (`storage.md` §Don't
  "dedup") rather than writing its own — **`docs/DECISIONS.md` D150** is why the check is EAGER, what a lazy
  one would have accepted, and the two scope rules a copy gets wrong.

Each domain you DO implement owes a `<Domain>StoreContract` fact class alongside the existing ones
(`tests/Lyntai.Tests/Storage/`, and `tests/Lyntai.Tests/Jobs/` for `JobStoreContract`) — the contract facts
run every domain against every backend and are what keeps them from drifting (`storage.md` §Don't "dedup").
That is the gate a new backend passes.

Read `storage.md` before writing SQL — the FTS trigram triggers, the `CAST(x AS REAL)` affinity trap,
per-connection `foreign_keys`, and the `lyntai_` prefix are all load-bearing and easy to get subtly
wrong. Mirror `Lyntai.Storage.Sqlite`.

The domain interfaces are shaped so a future **composite store** (route each domain to a different
backend, mastra-style) can be layered on without breaking consumers — don't add cross-domain coupling.

---

## Add a scorer

Cheapest extension. A class + one registration, no new package (built-ins live in
`Lyntai.Core/Cortex/Scorers/`; a consumer's own can live anywhere).

- **Deterministic:** implement `IScorer` directly, compute in code, return `ScoreResult` (or `null` when
  the scorer doesn't apply to this context — `ScoringService` skips nulls).
- **LLM-judge:** extend `LlmScorerBase` — it runs a one-shot judge through the front door and parses a
  clamped `{score,reason}`. Override `Model`/`Consumer` to route a cheap judge to a cheap model; you supply
  the criterion prompt.

Register into the DI collection: `builder.AddScorer<MyScorer>()`. `ScoringService` iterates
`IEnumerable<IScorer>` and isolates a throwing scorer — never add an `if/switch` over scorer ids.

**Domain dimensions** a scorer needs beyond input/output ride in `ScoreContext.Extra` (a flat
`string→string` map — the app's own key catalog, e.g. `phase`/`mode`/`changed_files`). It's deliberately
stringly-typed so Core stays domain-agnostic; **serialize non-scalar values** (a list → JSON or a
delimiter the scorer splits). Persist a preview run without writing rows via
`EvaluateAsync(ctx, persist: false)`; the store upserts on `(session, scorer)` so re-scoring replaces.

---

## Add a CLI tool-hosting connector (`IMcpCliConnector`)

For a CLI provider whose model runs its OWN agent loop and can only reach custom tools over MCP (the
`claude` CLI is the reference case). **No new package** — a class + `AddMcpToolHost(new MyConnector())`.

**Two MCP paths exist and they are not alternatives — know which one you are extending.** THIS one hosts the
app's **in-process `ITool`s** on a loopback server Lyntai stands up (`McpEndpoint`, HTTP-only, bearer token,
torn down with the `CliToolSession`). The other, `AgentSessionOptions.McpServers` / `AgentMcpServer`, points a
CLI at MCP servers the **app already runs or launches** — stdio as well as HTTP — and is rendered per backend
by `ClaudeMcpConfig` / `CodexMcpConfig` rather than by an `IMcpCliConnector` (`docs/DECISIONS.md` **D38**). They
compose: an app can do both in one turn. If you are adding a CLI, you may owe BOTH — a connector here, and a
rendering there.
`Lyntai.Tools.Mcp` already owns everything neutral: the ephemeral loopback MCP server, bearer
token, temp-file writing, teardown, and the no-tools short-circuit. You supply only the flags and the
config-file shape.

<!-- compile-skip: the config-file payload is elided (`/* JSON or TOML, from ctx.Endpoint */`) -->
```csharp
public sealed class MyCliMcpConnector : IMcpCliConnector
{
    public string ProviderId => "my-cli";        // the provisioner is registered KEYED on this

    public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext ctx, CancellationToken ct = default)
    {
        var path = ctx.WriteTempFile("mcp", /* JSON or TOML, from ctx.Endpoint */);
        return ValueTask.FromResult<IReadOnlyList<string>>(["--mcp-config", path]);
    }
}
```

Load-bearing details:
- **Write config files ONLY through `ctx.WriteTempFile`.** It applies owner-only permissions (the file
  carries the bearer token) and registers the path for deletion when the session ends. A file you write
  yourself leaks a credential into temp.
- **`IMcpCliConnector` lives in Core, deliberately** — so a *provider* package can ship its connector without
  referencing the MCP package. **Never make a provider package reference `Lyntai.Tools.Mcp`**:
  it drags the MCP SDK (`ModelContextProtocol.Core`) into every app using the plain provider, and the
  MCP package opts out of AOT for its dynamic-JSON tool marshaling — so the provider would lose
  `IsAotCompatible` too. That is the exact thing the `ICliToolProvisioner` seam exists to prevent
  (`docs/DECISIONS.md` D17).
- **Derive names from `ctx.Endpoint.ServerName`**, never hard-code `"lyntai"` — it's configurable via
  `McpToolHostOptions`, and CLIs that build permission patterns from it (`mcp__<server>__*`) break if the
  two disagree.
- **Don't add a convenience package that composes host + connector.** One existed
  (`Lyntai.Providers.ClaudeCli.Mcp`) and was deleted: a package id whose only value is saving the caller
  `new MyConnector()` isn't worth its versioning and doc footprint, and it was the tree's only
  adapter→adapter reference. The app composes the two halves itself — that's the normal DI story.

Tests need no CLI binary: hand `BuildArgsAsync` an `McpCliContext` with a recording writer and assert the
argv + file contents (`ClaudeCliMcpConnectorTests`). The host itself is covered generically by
`McpToolHostTests`.

---

## Add a migration

`node devtools/dev.mjs new-migration <name>`, then the `add-migration` skill; the traps are `storage.md`
§Migrations.
