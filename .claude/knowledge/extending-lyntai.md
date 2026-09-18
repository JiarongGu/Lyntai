---
name: extending-lyntai
applies_when: adding an LLM provider, a storage backend, a scorer, a CLI tool-hosting dialect, a generation backend, or a migration
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

**A. Is the backend OpenAI-COMPATIBLE? Then it is already supported (preferred).** OpenAI, Azure, Ollama,
OpenRouter, vLLM, llama-server, Groq, DeepSeek and most of the rest ship such an endpoint. You do *nothing*
but register: `builder.AddHttpProvider("my-id", o => { o.BaseUrl = …; o.Dialect = …; })`, and one
registration serves chat, embeddings or reranking depending on `Produces`. **Only write a native provider if
no dialect reaches it** — which, since **D146** deleted the Microsoft.Extensions.AI bridge, means a vendor
whose wire format is genuinely its own.

**A3. Reachable but its OWN wire format → a BRIDGE, which is a lambda.**
`builder.AddBridgeProvider("my-id", (req, ct) => …)` turns anything that already answers into a routed
backend — a vendor SDK, an in-house service, a `Microsoft.Extensions.AI` `IChatClient`. You write only the
mapping you need; routing, fallback, cooldown, admission and the ops layer come along, and the library takes
no dependency on whatever you wrapped (**D147**). **Return a non-Ok `ProviderVerdict` rather than throwing**,
so the router can advance to the next candidate. A bridge declares only the operations you hand it a
delegate for — omit `stream` and no router will ask it to stream.

**A2. A SPAWNED CLI → write a DIALECT, not a provider.** If the backend is a command-line agent
(`claude`, `codex`, or a sibling), do NOT re-implement the spawn/verdict/streaming rules — they are already in
`CliProviderEngine` (Core, `Lyntai.Inference.Cli`), and re-deriving them is exactly how they drifted apart before
(D21). Read `ClaudeCliDialect` and `CodexCliDialect` side by side first: they are the two worked examples, and
their differences (stdin vs. required repo-check flag, JSON vs. prose auth, `auth logout` vs. top-level
`logout`, pinning vs. no pinning) show what a dialect is for. Derive from `CliProviderDialectBase` and supply
only what is specific to that CLI:

<!-- compile-skip: a dialect sketch: its member bodies are elided for illustration -->
```csharp
public sealed class MyCliDialect : CliProviderDialectBase
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

Then a ~40-line provider that composes engine + dialect and declares which optional capabilities the
backend *actually* has (`IModelProvider` / `IProviderUpdater` / `IProviderVersionInstaller` /
`IProviderAuth`) — copy `ClaudeCliProvider`, which is nothing but forwarding members. The engine owns:
command resolution, neutral cwd, prompt delivery (stdin or trailing argument — set `PromptDelivery`),
the inactivity clock (plus an absolute backstop on the BUFFERED path only — a streamed turn is bounded by
provider inactivity and the caller's token, nothing else), `ProviderVerdictClassifier`, empty→`Failed`,
streaming order, and probe → run → re-probe maintenance.

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
- **`SupportsToolCalls` on the dialect drives ONLY the engine's ignored-tools warning.** If your dialect
  returns `true`, the composing provider must declare it in its `ProviderCapabilities`
  (`SupportsToolCalls = true`) — the provider is the capability declarer (D21) and the engine does not
  forward the dialect's answer. **It is NOT a member of `IModelProvider`**: writing
  `public bool SupportsToolCalls => true;` on your provider compiles and is read by nothing.
  Otherwise `TextRouter.SupportsToolCalls` reports false and `ToolLoop` silently takes the prompt-based
  fallback on a backend that can do native tool calls.
- **Portable installs are free if you don't fight them** — the host passes `command` (+ `environment`) to your
  builder extension (D22); pass both straight through to the engine and don't read env vars yourself.

**B. Native `IModelProvider`** for anything else (like `HttpModelProvider`). **Where it lives is a
FOOTPRINT test, not one-package-per-backend** (`docs/DECISIONS.md` D25): a dialect or native provider that
needs nothing beyond Core/BCL — or only managed `Microsoft.Extensions.Http` — is a class in
`src/Lyntai.Providers.Basic/`, where `ClaudeCliDialect`, `CodexCliDialect`, `ClaudeCliProvider`,
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
    public ProviderCapabilities Capabilities => /* Accepts / Produces / Operations */;   // NO default — the one member you must write
    public bool IsAvailable => /* cheap check; real failures surface as verdicts, not here */;
    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default);
    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default);
}
```

Non-negotiables (see `llm-and-router.md` for why — the router trusts every provider to honor these):
- **Classify failures with `ProviderVerdictClassifier`** — never hand-roll substring heuristics (they drift;
  three copies were consolidated into one for exactly this reason). Map transport → verdict:
  429→`RateLimited`, 401/403→`AuthFailed`, content-filter→`Refused`, too-big→`ContextWindowExceeded`,
  deadline→`Timeout`, else→`Failed`.
  **An HTTP backend classifies through the THREE-argument overload**,
  `ProviderVerdictClassifier.FromHttpFailure(status, body, hasCredentials)` (see
  `HttpModelProvider`, which passes `HasCredentials`). A 401/403 answered to a call that carried NO
  credentials is `NotConfigured`, not `AuthFailed` — and the difference is not cosmetic, because routing acts
  on it: `AuthFailed` BENCHES the provider for the cooldown window, so a backend the consumer merely listed
  without configuring would be penalised on every first attempt for a fact the platform knew before calling,
  while `NotConfigured` skips it blamelessly and lets a host offer setup (`docs/DECISIONS.md` D31). The rule is
  **not** "a key is required": an OpenAI-compatible endpoint run locally (LM Studio, vLLM, Ollama)
  legitimately needs none, so "no key" cannot mean unconfigured on its own — only "no key AND the server
  demanded one" does. A CLI/session-authenticated dialect has no `hasCredentials` fact at all and correctly
  stays on the two-argument overload. The generation domain states the same rule over its own vocabulary
  (`ProviderVerdictClassifier.FromHttpFailure`); change one and check the other.
- **Empty/no output is `Failed`, not `Ok`** — both in `CompleteAsync` and as a terminal `Error` chunk in
  `StreamAsync` (a zero-content stream must let the router fall over, not report a clean empty answer).
- **Streaming timeout is an INACTIVITY clock**, never a single `CancelAfter` over the whole stream:
  re-arm before each read, `CancelAfter(Timeout.InfiniteTimeSpan)` after it returns. A single deadline
  counts consumer dwell time and kills healthy streams. Copy the shape from
  `HttpModelProvider.StreamAsync` / `ProcessRunner.StreamLinesAsync`.
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

**C. NOT a chat backend? Then most of B does not apply, and what replaces it is one LIST.** Every rule above
is about a verdict, and a vector or rerank backend returns neither — `EmbedAsync` and `ScoreAsync` THROW
instead, because there is no vector and no score meaning "I could not" and a zero ranks as confidently as a
real number (`IModelProvider`'s own remarks). What you write is the method plus
`Produces = [ProviderKinds.Vector]` or `[ProviderKinds.Score]`, and **the declaration is the wiring**: a
seam that consumes the kind finds you, so a cross-encoder needs no reranker-shaped registration and no
policy of its own — `AddMemoryScoringVerification` already selects on `Score` (**D139**), and
`OnnxProvider` with an `OnnxCrossEncoderDialect` is the worked example. Register with `AddProvider` like every other backend — there is
no role-named registration to choose between (**D152**). **Pass `declares` when a FACTORY produces
`Vector`**: `AddSemanticMemory` decides at composition time, before anything is built, so an undeclared
factory reads as "does not embed" and that call fails fast naming the argument. A backend built eagerly
hands over its own `Capabilities` and restates nothing.

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

So when one runtime serves two kinds, what varies is a **dialect**, not a class:

<!-- compile-given: string embedDir = ""; string rerankDir = ""; -->
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
methods the provider answers*. Which internal dialect serves it is not consumer surface at all; the seam
lives in `Lyntai.Providers.Onnx` and is `internal`, which is the EF property — a provider package grows its
own seams without Core, or a consumer, learning they exist.

**A backend MAY serve several kinds, and that is said in data.** `ProviderCapabilities.Produces` is a LIST
for one call returning several. Whether a backend's OPTIONS take one kind or many mirrors what it can
actually do — `ComfyUiOptions`/`FalQueueOptions` take a list because one workflow host serves image AND
video; `HttpModelOptions` and `OnnxProviderOptions` take one, because one registration is one route and one
session is one graph. Copy the side your backend is actually on.

**Where the EF analogy STOPS:** EF binds one provider per `DbContext`; Lyntai registers many and ROUTES
across them with fallback and cooldown. A provider here is a candidate, not a choice — which is exactly why
what it produces must be DATA the dialect states rather than a class the consumer picks between.

---

## Add a generation backend

The media seam (image / video / audio / 3d) behind one capability-aware contract. Same shape as everything
else: **the CONTRACTS are in `Lyntai.Core`** (namespaces `Lyntai.Generation`, `.Routing`, `.Jobs`, `.Tools`),
the BACKENDS live in the `Lyntai.Generation` package under `Lyntai.Generation.Providers`, and each ships a
one-line `builder.Add<Name>Provider(...)` shim over `AddProvider(sp => …)` plus `AddMediaRouting()`. Every
builder method names what it REGISTERS with the vendor as the qualifier (**D137**), so the suffix is on
both — and the qualifier names the ENGINE, never what the backend produces (**D152**, **D156**): every
shipped media preset is `AddOpenAiImageProvider`, `AddAutomatic1111Provider`, `AddComfyUiProvider`,
`AddFalProvider`, `AddLocalDiffusionProvider`. There is no `Add<Domain>Provider`, because a domain is not a
kind of provider — it is a `ProviderCapabilities.Produces` value.

**A generation backend needs a MAJOR to reshape, like everything else.** `Lyntai.Generation` was EXEMPT as a
**PACKAGE** from 2.0.1 — the backends were written from vendor docs with no key to call, and
`IModelProvider` had no implementer — and 3.0 withdrew that once both were addressed
(`docs/DECISIONS.md` **D70**; the mappings became host options in **D69**, the stream seam was wired in
**D67**). If you are adding a backend, the practical consequence is: **put anything you are unsure of behind
an OPTION rather than a literal**, which is what makes a wrong guess a consumer's config edit instead of your
major bump. The `Lyntai.Generation` **NAMESPACE** is still a different thing from the package:
`MediaResponse`, `ProviderVerdictClassifier`, the routing policy and the rest of the contracts ship
inside mandatory `Lyntai.Core` — a distinction worth keeping straight, since `docs/DECISIONS.md` D36 did
verdict-translation fix and treated it as major-bump material.

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
- **Classify through `ProviderVerdictClassifier.FromHttpFailure(status, body, hasCredentials)`** — the same
  two-term promotion as the LLM side: a 401/403 to a call that carried no credentials is `NotConfigured`, not
  `AuthFailed`, because `AuthFailed` benches the backend for the cooldown window. The classifier DELEGATES its
  pattern corpus to `ProviderVerdictClassifier` and translates; never carry a second copy of "what does a 429 look
  like".
- **A submit whose outcome is UNKNOWN is `QueuedOperation.Inconclusive`, and is never re-submitted.** A
  backend that ANSWERS "no" can be retried elsewhere for free; a backend that never answered may already hold
  a billable render, and handing the same request to the next candidate buys the same generation twice. The
  router surfaces such a submission instead of advancing, and does not count it toward the dead-host
  threshold — no answer is no evidence of ill health either.
- **MEASURE the wire format before shipping it.** Two backends here are documented-not-measured and carry an
  explicit caveat until someone runs them for real (`TASKS.md` Part 33, GEN-VERIFY). Do not add a third: a
  mapping derived from vendor docs is a guess wearing a type, and the build stays green either way.

Before writing code, read the four generation traps already recorded in `pitfalls.md` — `TimeSpan.Zero` means
"no deadline" here and "cancel instantly" on the LLM side; a cooldown keyed on the provider id benches other
tenants; decorating a provider erases `IMediaJobProvider` so every video render silently stops routing
while every image render keeps working; and `MediaRouter`'s `Surface` arm returns one frame shallower
than the admission permit it depends on.

---

## Add a storage backend

A storage driver genuinely does earn its own package (it drags a database driver a consumer might refuse), so:
new package `src/Lyntai.Storage.<Backend>/`, ref Core only — scaffolded with `node devtools/dev.mjs
new-package Lyntai.Storage.<Backend>`, which registers it in all NINE registries `check-packages` gates.
Never hand-roll the csproj; the misses are silent.

Implement the domain interfaces the consumer needs — they're independent, you don't have to do all of them,
and there are **thirteen**, not five: the eight in `src/Lyntai.Core/Storage/` (`IKeyValueStore`,
`IConversationStore`, `IMemoryStore`, `IScoreStore`, `ITraceStore`, `IPromptVersionStore`, `IJobStore`,
`ICuratedMemoryStore`) plus `IVectorStore` (`Memory/`), `IResponseCache` (`Inference/Caching/`), `IUsageTracker`
(`Inference/Budgeting/`), `IModelRoutingStore` (`Inference/`) — and **`IMemoryGraphStore` (`Memory/`), which
this list omitted entirely until 2026-09-10**.

**Read that omission as the warning it is.** `IMemoryGraphStore` is the LARGEST thing in the storage layer
— a 643-line contract with **thirteen required members**, against 775 and 688 lines of relational
implementation — and a measured cold-start probe followed these documents and produced a backend plan
without it. It is also the only one with a per-backend migration asymmetry (`.claude/knowledge/storage.md`
§Migrations) and the only one whose contract pins an ORDER (`WriteBackAsync`, **D101**). If you are backing
the memory engine, it is most of your work; if you are not, you can skip it like any other.

**THREE of the SIXTEEN carry a default body — the thirteen above are what is left, and the difference between the three matters.**
`KnownSubjectsAsync` defaults to an empty list, so a BYO store silently gets **no subject seeding** at all
(**D88**) — nothing fails, recall is simply worse. `LinkManyAsync` (**D99**) and `WriteBackAsync` (**D101**)
default to the calls the engine used to make inline, so a BYO store loses no behaviour and is merely no
faster. And `WriteBackAsync` carries an ORDER as contract — the review log last, so a broken log cannot
cost the touch or the edges — so an override that reorders it is wrong however fast it is.

Mirror `src/Lyntai.Storage.Postgres/`, the
reference backend, which implements twelve of the thirteen (all but `IModelRoutingStore`). Provide
`builder.Use<Backend>Storage(...)` that registers an `IDbConnectionFactory` (or the backend's equivalent) +
the stores + runs migrations.

Two seams the list alone doesn't reveal:
- **`IJobStore` goes through `Core/Storage/JobStoreSql.cs`** — the job state machine (transition statements,
  the `claimed_by` write fence, the claim-candidate predicate) plus the `JobRow` mapping are SHARED on
  purpose, because drift there is a correctness bug; only the locking frame is per-dialect (`storage.md`
  §Don't "dedup" the Sqlite/Postgres stores).
- **A Governance-backed `Use*` helper needs its own startup guard.** `lyntai_vector`, the response cache and
  the usage ledger all ship under `StorageFeature.Governance`, so those helpers must reject a Governance-less
  subset at wiring time rather than at first use. The existing `RequireGovernance` is private to each backend,
  so a new package writes its own equivalent — **read `docs/DECISIONS.md` D150 first**: it is why the check
  is EAGER, what a lazy one would have accepted, and the two scope rules a copy gets wrong (`storage.md`
  §Migrations also carries the schema-ownership carve-out).

Each domain you DO implement owes a `<Domain>StoreContract` fact class alongside the existing ones
(`tests/Lyntai.Tests/Storage/`, and `tests/Lyntai.Tests/Jobs/` for `JobStoreContract`) — the contract facts
run every domain against every backend and are what keeps them from drifting (`storage.md` §Don't "dedup").
That is the gate a new backend passes.

Read `storage.md` before writing SQL — the FTS trigram triggers, the `CAST(x AS REAL)` affinity trap,
per-connection `foreign_keys`, and the `lyntai_` prefix are all load-bearing and easy to get subtly
wrong; the canonical statement of those traps is `.claude/knowledge/sql-storage.md`. Mirror
`Lyntai.Storage.Sqlite`.

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

## Add a CLI tool-hosting dialect (`IMcpCliDialect`)

For a CLI provider whose model runs its OWN agent loop and can only reach custom tools over MCP (the
`claude` CLI is the reference case). **No new package** — a class + `AddMcpToolHost(new MyDialect())`.

**Two MCP paths exist and they are not alternatives — know which one you are extending.** THIS one hosts the
app's **in-process `ITool`s** on a loopback server Lyntai stands up (`McpEndpoint`, HTTP-only, bearer token,
torn down with the `CliToolSession`). The other, `AgentSessionOptions.McpServers` / `AgentMcpServer`, points a
CLI at MCP servers the **app already runs or launches** — stdio as well as HTTP — and is rendered per backend
by `ClaudeMcpConfig` / `CodexMcpConfig` rather than by an `IMcpCliDialect` (`docs/DECISIONS.md` **D38**). They
compose: an app can do both in one turn. If you are adding a CLI, you may owe BOTH — a dialect here, and a
rendering there.
`Lyntai.Tools.Mcp` already owns everything neutral: the ephemeral loopback MCP server, bearer
token, temp-file writing, teardown, and the no-tools short-circuit. You supply only the flags and the
config-file shape.

<!-- compile-skip: the config-file payload is elided (`/* JSON or TOML, from ctx.Endpoint */`) -->
```csharp
public sealed class MyCliMcpDialect : IMcpCliDialect
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
- **`IMcpCliDialect` lives in Core, deliberately** — so a *provider* package can ship its dialect without
  referencing the MCP package. **Never make a provider package reference `Lyntai.Tools.Mcp`**:
  it drags the MCP SDK (`ModelContextProtocol.Core`) into every app using the plain provider, and the
  MCP package opts out of AOT for its dynamic-JSON tool marshaling — so the provider would lose
  `IsAotCompatible` too. That is the exact thing the `ICliToolProvisioner` seam exists to prevent
  (`docs/DECISIONS.md` D17). _The original cost was heavier — a framework reference on
  `Microsoft.AspNetCore.App` — until 2.0.1 moved the host onto `System.Net.HttpListener` (D25). The
  dependency shrank; the rule did not change._
- **Derive names from `ctx.Endpoint.ServerName`**, never hard-code `"lyntai"` — it's configurable via
  `McpToolHostOptions`, and CLIs that build permission patterns from it (`mcp__<server>__*`) break if the
  two disagree.
- **Don't add a convenience package that composes host + dialect.** One existed
  (`Lyntai.Providers.ClaudeCli.Mcp`) and was deleted: a package id whose only value is saving the caller
  `new MyDialect()` isn't worth its versioning and doc footprint, and it was the tree's only
  adapter→adapter reference. The app composes the two halves itself — that's the normal DI story.

Tests need no CLI binary: hand `BuildArgsAsync` an `McpCliContext` with a recording writer and assert the
argv + file contents (`ClaudeCliMcpDialectTests`). The host itself is covered generically by
`McpToolHostTests`.

---

## Add a migration

`node devtools/dev.mjs new-migration <name>` scaffolds `src/Lyntai.Storage.Sqlite/Migrations/M<num>_<Name>.cs`
with a **guaranteed-unique, monotonic** `yyyyMMddHHmm` number (reusing a number is silently skipped —
never hand-pick one, and never renumber one that has shipped: the number is recorded in
`lyntai_version_info`, so changing it re-runs the migration against a database that already has its tables). Then fill `Up()`:
- Tag it `[Tags(nameof(StorageFeature.<Feature>), StorageFeatures.AllTag)]` — the scaffold's placeholder
  doesn't compile until you do. Both tags are load-bearing, and an UNTAGGED migration runs under every
  feature set (so a disabled domain still lands its table).
- Prefix every object `lyntai_`. snake_case columns. Composite PK + FK **inline at `Create.Table`**
  (SQLite can't `ALTER ADD CONSTRAINT`).
- Searchable text → FTS5 **trigram** external-content mirror + AFTER INSERT/DELETE/UPDATE triggers
  (emit the `'delete'` command row on delete **and** update) + an in-migration backfill. Copy
  `M202607280003_Memory` exactly; the delete/update trigger is the #1 botched thing here (`storage.md`).
- The runner applies migrations under WAL + `busy_timeout` (set in `MigrationRunnerService`); it's
  idempotent, so re-running on an up-to-date db is a no-op.
