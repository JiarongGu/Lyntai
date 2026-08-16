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

---

## Add an LLM provider

Three paths — pick the cheapest one that reaches your backend:

**A. Bridge an existing `Microsoft.Extensions.AI` `IChatClient` (preferred).** OpenAI, Azure, Ollama,
Anthropic-API, etc. already have MEAI clients. You do *nothing* but register:
`builder.AddExtensionsAiProvider("my-id", theChatClient)`. `ExtensionsAiProvider` handles the mapping,
streaming, usage, and verdict-from-exception. **Only write a native provider if MEAI can't reach it.**

**A2. A SPAWNED CLI → write a DIALECT, not a provider.** If the backend is a command-line agent
(`claude`, `codex`, or a sibling), do NOT re-implement the spawn/verdict/streaming rules — they are already in
`CliProviderEngine` (Core, `Lyntai.Llm.Cli`), and re-deriving them is exactly how they drifted apart before
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
    public override IReadOnlyList<string> BuildCompletionArgs(LlmRequest r) => ["exec", "--json"];
    public override CliOutputEvent ParseLine(string line) => /* → Content / Result / Ignored */;

    // OPTIONAL, and only when VERIFIED against the real binary (see below):
    public override IReadOnlyList<string>? UpdateArgs => ["update"];
    public override IReadOnlyList<string>? AuthStatusArgs => ["login", "status"];
}
```

Then a ~40-line provider that composes engine + dialect and declares which optional capabilities the
backend *actually* has (`IProviderProbe` / `IProviderUpdater` / `IProviderVersionInstaller` /
`IProviderAuth`) — copy `ClaudeCliProvider`, which is nothing but forwarding members. The engine owns:
command resolution, neutral cwd, prompt delivery (stdin or trailing argument — set `PromptDelivery`),
the inactivity clock (plus an absolute backstop on the BUFFERED path only — a streamed turn is bounded by
provider inactivity and the caller's token, nothing else), `LlmVerdictClassifier`, empty→`Failed`,
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
  returns `true`, the composing `ILlmProvider` must declare `public bool SupportsToolCalls => true;` itself —
  the provider is the capability declarer (D21), and the engine does not forward the dialect's answer.
  Otherwise `LlmRouter.SupportsToolCalls` reports false and `ToolLoop` silently takes the prompt-based
  fallback on a backend that can do native tool calls.
- **Portable installs are free if you don't fight them** — the host passes `command` (+ `environment`) to your
  builder extension (D22); pass both straight through to the engine and don't read env vars yourself.

**B. Native `ILlmProvider`** for anything else (like `OpenAiCompatibleProvider`). **Where it lives is a
FOOTPRINT test, not one-package-per-backend** (`docs/DECISIONS.md` D25): a dialect or native provider that
needs nothing beyond Core/BCL — or only managed `Microsoft.Extensions.Http` — is a class in
`src/Lyntai.Providers.Default/`, where `ClaudeCliDialect`, `CodexCliDialect`, `ClaudeCliProvider`,
`CodexCliProvider` and `OpenAiCompatibleProvider` already live; namespaces stay `Lyntai.Providers.<Name>`
inside the one assembly (D25), so nothing an author writes changes. It earns its own
`src/Lyntai.Providers.<Name>/` package (ref Core only, never adapter→adapter) only when it drags a native
runtime, a platform-specific API, or a dependency a consumer might refuse — `Lyntai.Providers.Local` is the
worked example. When it does earn one, scaffold it with `node devtools/dev.mjs new-package <Lyntai.X>`: a
package must enter NINE registries, `check-packages` gates them, and the misses are silent (no
`ApiSurfaceTests` entry means no API gate at all). Never register them by hand — and remember a published
package id can never be freed (D23), so a needless one is permanent. Implement:

<!-- compile-skip: a provider signature with its constructor parameters elided (`/* options, factory */`) -->
```csharp
public sealed class MyProvider(string id, /* options, factory */, LyntaiOptions options) : ILlmProvider
{
    public string Id => id;                 // the candidate id the router selects on
    public bool IsAvailable => /* cheap check; real failures surface as verdicts, not here */;
    public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default);
    public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default);
}
```

Non-negotiables (see `llm-and-router.md` for why — the router trusts every provider to honor these):
- **Classify failures with `LlmVerdictClassifier`** — never hand-roll substring heuristics (they drift;
  three copies were consolidated into one for exactly this reason). Map transport → verdict:
  429→`RateLimited`, 401/403→`AuthFailed`, content-filter→`Refused`, too-big→`ContextWindowExceeded`,
  deadline→`Timeout`, else→`Failed`.
  **An HTTP backend classifies through the THREE-argument overload**,
  `LlmVerdictClassifier.FromHttpFailure(status, body, hasCredentials)` (see
  `OpenAiCompatibleProvider`, which passes `HasCredentials`). A 401/403 answered to a call that carried NO
  credentials is `NotConfigured`, not `AuthFailed` — and the difference is not cosmetic, because routing acts
  on it: `AuthFailed` BENCHES the provider for the cooldown window, so a backend the consumer merely listed
  without configuring would be penalised on every first attempt for a fact the platform knew before calling,
  while `NotConfigured` skips it blamelessly and lets a host offer setup (`docs/DECISIONS.md` D31). The rule is
  **not** "a key is required": an OpenAI-compatible endpoint run locally (LM Studio, vLLM, Ollama)
  legitimately needs none, so "no key" cannot mean unconfigured on its own — only "no key AND the server
  demanded one" does. A CLI/session-authenticated dialect has no `hasCredentials` fact at all and correctly
  stays on the two-argument overload. The generation domain states the same rule over its own vocabulary
  (`GenerationVerdictClassifier.FromHttpFailure`); change one and check the other.
- **Empty/no output is `Failed`, not `Ok`** — both in `CompleteAsync` and as a terminal `Error` chunk in
  `StreamAsync` (a zero-content stream must let the router fall over, not report a clean empty answer).
- **Streaming timeout is an INACTIVITY clock**, never a single `CancelAfter` over the whole stream:
  re-arm before each read, `CancelAfter(Timeout.InfiniteTimeSpan)` after it returns. A single deadline
  counts consumer dwell time and kills healthy streams. Copy the shape from
  `OpenAiCompatibleProvider.StreamAsync` / `ProcessRunner.StreamLinesAsync`.
- **Only yield `LlmChunk.Content` for non-empty text**; end with exactly one `Final` (with usage) or
  `Error`.
- Spawning a CLI? Go through `ProcessRunner` (ArgumentList only, prompt via stdin, BOM-less UTF-8,
  kill-tree). Never build a provider that shells out directly.

Builder extension (in the adapter package, extending Core's `LyntaiBuilder`):
<!-- compile-skip: an extension-method sketch with its registration arguments elided -->
```csharp
public static LyntaiBuilder AddMyProvider(this LyntaiBuilder b, string id, Action<MyOptions> cfg)
{
    // register the provider into the IEnumerable<ILlmProvider> collection; resolve deps from the container
    b.AddProvider(sp => new MyProvider(id, /* … */, sp.GetRequiredService<LyntaiOptions>()));
    return b;
}
```
Tests: drive it against a stub, never a live endpoint — an HTTP provider gets a stubbed
`HttpMessageHandler` (`Fakes/StubHttpHandler`); a CLI provider gets the `provider-stub.mjs` via
`LYNTAI_PROVIDER_CMD`. Cover each verdict, streaming order, and empty→Failed.

---

## Add a generation backend

The media seam (image / video / audio / 3d) behind one capability-aware contract. Same shape as everything
else: **the CONTRACTS are in `Lyntai.Core`** (namespaces `Lyntai.Generation`, `.Routing`, `.Jobs`, `.Tools`),
the BACKENDS live in the `Lyntai.Generation` package under `Lyntai.Generation.Providers`, and each ships a
one-line `builder.Add<Name>Provider(...)` shim over `AddGenerationProvider(sp => …)`.

**Read the SemVer scope before you rely on the carve-out.** `Lyntai.Generation` is EXPERIMENTAL as a
**PACKAGE** — the backends — because they were written from vendor docs with no key to call and because
`IGenerationStreamProvider` has no implementer. The `Lyntai.Generation` **NAMESPACE** is a different thing:
`GenerationResult`, `GenerationVerdictClassifier`, the routing policy and the rest of the contracts ship
inside mandatory `Lyntai.Core` and carry the **FULL** SemVer promise. Read CLAUDE.md's reason clause before
claiming the exemption; when in doubt apply the full promise — `docs/DECISIONS.md` D36 did exactly that for a
verdict-translation fix and treated it as major-bump material.

What a backend implements:
- **`IGenerationProvider`** — `Id`, `Capabilities` (read by the router BEFORE spending anything),
  `ProbeAsync` and inline `GenerateAsync`. Both must **FAIL SAFE**: a value with a verdict, never a throw
  (cancellation propagates). `ProbeAsync` must **never generate** to answer a setup question — the
  generate-and-discard pattern it replaces bills a render to find out whether a key works.
- **Optional capability interfaces, only if the backend really has them:** `IGenerationJobProvider`
  (submit → poll → fetch, for queued/long renders) and `IGenerationStreamProvider`. They are ADDITIONAL
  interfaces the router type-tests, not flags — which is exactly why nothing may wrap a provider in a
  decorator that implements only the base seam (see `pitfalls.md`).
- **Classify through `GenerationVerdictClassifier.FromHttpFailure(status, body, hasCredentials)`** — the same
  two-term promotion as the LLM side: a 401/403 to a call that carried no credentials is `NotConfigured`, not
  `AuthFailed`, because `AuthFailed` benches the backend for the cooldown window. The classifier DELEGATES its
  pattern corpus to `LlmVerdictClassifier` and translates; never carry a second copy of "what does a 429 look
  like".
- **A submit whose outcome is UNKNOWN is `GenerationOperation.Inconclusive`, and is never re-submitted.** A
  backend that ANSWERS "no" can be retried elsewhere for free; a backend that never answered may already hold
  a billable render, and handing the same request to the next candidate buys the same generation twice. The
  router surfaces such a submission instead of advancing, and does not count it toward the dead-host
  threshold — no answer is no evidence of ill health either.
- **MEASURE the wire format before shipping it.** Two backends here are documented-not-measured and carry an
  explicit caveat until someone runs them for real (`TASKS.md` Part 33, GEN-VERIFY). Do not add a third: a
  mapping derived from vendor docs is a guess wearing a type, and the build stays green either way.

Before writing code, read the four generation traps already recorded in `pitfalls.md` — `TimeSpan.Zero` means
"no deadline" here and "cancel instantly" on the LLM side; a cooldown keyed on the provider id benches other
tenants; decorating a provider erases `IGenerationJobProvider` so every video render silently stops routing
while every image render keeps working; and `GenerationRouter`'s `Surface` arm returns one frame shallower
than the admission permit it depends on.

---

## Add a storage backend

A storage driver genuinely does earn its own package (it drags a database driver a consumer might refuse), so:
new package `src/Lyntai.Storage.<Backend>/`, ref Core only — scaffolded with `node devtools/dev.mjs
new-package Lyntai.Storage.<Backend>`, which registers it in all NINE registries `check-packages` gates.
Never hand-roll the csproj; the misses are silent.

Implement the domain interfaces the consumer needs — they're independent, you don't have to do all of them,
and there are **twelve**, not five: the eight in `src/Lyntai.Core/Storage/` (`IKeyValueStore`,
`IConversationStore`, `IMemoryStore`, `IScoreStore`, `ITraceStore`, `IPromptVersionStore`, `IJobStore`,
`ICuratedMemoryStore`) plus `IVectorStore` (`Memory/`), `IResponseCache` (`Llm/Caching/`), `IUsageTracker`
(`Llm/Budgeting/`) and `IModelRoutingStore` (`Llm/Routing/`). Mirror `src/Lyntai.Storage.Postgres/`, the
reference backend, which implements eleven of the twelve (all but `IModelRoutingStore`). Provide
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
  so a new package writes its own equivalent (`storage.md` §Migrations, which also carries the
  schema-ownership carve-out).

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
`Lyntai.Tools.Mcp.Hosting` already owns everything neutral: the ephemeral loopback MCP server, bearer
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
  referencing the hosting package. **Never make a provider package reference `Lyntai.Tools.Mcp.Hosting`**:
  it drags the MCP SDK (`ModelContextProtocol.Core`) into every app using the plain provider, and the
  hosting package opts out of AOT for its dynamic-JSON tool marshaling — so the provider would lose
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
