---
name: add-provider
description: Use when adding a new LLM provider to Lyntai (a new backend/model source behind IModelProvider, or bridging an existing Microsoft.Extensions.AI IChatClient). Covers the correct pattern, the verdict/streaming/timeout invariants, and stub-based tests.
---

# Add an LLM provider to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add an LLM provider) and `.claude/knowledge/llm-and-router.md` first.

## Decide the path
1. **Is it reachable over HTTP on a wire we speak?** (OpenAI/Azure/Ollama/OpenRouter/vLLM/llama-server/Groq/DeepSeek/…) → don't
   write a provider. The consumer calls `builder.AddHttpProvider("id", o => o.BaseUrl = …)`, picking a
   wire (`AddHttpProvider` for OpenAI-shaped, `AddOllamaProvider` for Ollama-native). Most vendors ship such
   an endpoint, so this is the answer far more often than not (**D146**).
2. **Is it a spawned CLI agent?** (`claude`, a sibling CLI) → an **`ICliBackend`** plus a thin provider. See the
   CLI checklist below — the spawn/verdict/streaming/maintenance invariants already live in
   `CliProviderEngine`, and a second copy of them is how they drifted before.
3. **Reachable, but its own wire format?** → a **BRIDGE**, not a provider:
   `builder.AddBridgeProvider("id", (req, ct) => …)` wraps any client that already answers — a vendor SDK,
   an in-house service, an MEAI `IChatClient` — in the few lines of mapping you actually need, and costs the
   library no dependency (**D147**). Return a non-Ok `ProviderVerdict` rather than throwing.
4. Otherwise write a native `IModelProvider`. WHERE it lives is the footprint test below, never a default.

## Where the code lives — the footprint test (`docs/DECISIONS.md` D25)

**A package boundary must answer "which dependency does this isolate?"** A CLI backend or a native provider
that needs nothing beyond Core/BCL — or only managed `Microsoft.Extensions.Http` — is **a class in
`src/Lyntai.Providers.Basic/`**, next to `ClaudeCliBackend`, `CodexCliBackend` and
`HttpModelProvider`. Namespaces stay `Lyntai.Providers.<Name>` inside that one assembly (D25:
consolidating packages must not force a consumer to edit a `using`).

It earns its own `src/Lyntai.Providers.<Name>/` package (project-ref `Lyntai.Core` only, never
adapter→adapter) **the moment it drags a native runtime, a platform-specific API, or a dependency a
consumer might refuse** — `Lyntai.Providers.LlamaSharp` (LLamaSharp + a native backend) is the worked example.
Do not create one for tidiness: a published package id can never be freed or reused (D23), so a needless
id is permanent. If it does earn a package, scaffold it — see the Baselines bullet below; never hand-roll
the csproj.

## CLI-backend checklist (the `ICliBackend` path)
- [ ] A class in `src/Lyntai.Providers.Basic/` (footprint test above) — a CLI backend adds no dependency of
      its own, so it essentially never earns a package. `ClaudeCliBackend` and `CodexCliBackend` live there.
- [ ] **MEASURE the CLI first** — `<cmd> --help`, `--version`, and `--help` on each subcommand you intend to
      drive. Record what you measured (version + date) in the backend's XML docs. **Never name a command you
      haven't run**: a CLI that reads an unrecognized token as a PROMPT answers it, so a guessed subcommand
      spends tokens on every call while the build stays green.
- [ ] `<Name>CliBackend : CliBackendBase` — `Id`, `DefaultCommand`, `CommandEnvironmentVariables`
      (put `LYNTAI_PROVIDER_CMD` first so the stub seam works), `BuildCompletionArgs`, `ParseLine`. Set
      `PromptDelivery = Argument` only if the CLI truly takes its prompt positionally.
- [ ] Optional capabilities ONLY where measured: `VersionArgs` (default `--version`), `UpdateArgs`,
      `AuthStatusArgs` + `ParseAuthStatus`, `LogoutArgs`, `TryBuildLoginArgs`, `TryBuildInstallArgs`.
      Refuse unknown free-form values (`FlagShaped`) instead of forwarding them into argv.
- [ ] `<Name>CliProvider` — forwards to `CliProviderEngine` and implements exactly the capability interfaces
      that backend supports; its `Capabilities` come from `CliComposition.Capabilities(backend)`, so the
      tool-call declaration cannot disagree with the backend's. Copy `ClaudeCliProvider` (pure forwarding).
- [ ] `Add<Name>CliProvider(this LyntaiBuilder, …, string id = <backend name>)` extension: the id defaults
      to the BACKEND's name, never a role, and a second registration (a second portable install) passes its
      own. Resolve the tool provisioner through `CliComposition.Provisioner(sp, id, defaultId)`. A builder
      method names what it REGISTERS, with the vendor qualifying it (**D137**).
- [ ] Tests: the parsing/argv-building unit-tested through `FakeProcessRunner` (never a real binary — and
      NEVER `login`/`logout`/`install` against one, which mutate a developer's machine), plus a real-spawn
      test against `provider-stub.mjs`. Add the CLI's shapes to the stub as needed.
- [ ] Baselines: confirm Core's own baseline is untouched. A class added to `Lyntai.Providers.Basic`
      needs only Core's and `Lyntai.Providers.Basic`'s baselines reviewed. **Only if the backend earns its own package**
      (footprint test above): scaffold with `node devtools/dev.mjs new-package Lyntai.Providers.<Name>` —
      it registers all NINE registries `check-packages` gates (`packableProjects`, the solution,
      `<Description>`, `ApiSurfaceTests.Assemblies()`, the SEPARATE `Loaded` anchor map, the baseline file,
      the test project's `ProjectReference`, the `docs/AOT.md` row, the README row). Do not hand-roll the
      csproj: the misses are silent — a package absent from `Assemblies()` has no API gate at all.
- [ ] `node devtools/dev.mjs verify` green.

## Native provider checklist (non-CLI)
- [ ] A class in `src/Lyntai.Providers.Basic/` unless the backend drags a dependency a consumer might
      refuse — the footprint test above. `HttpModelProvider` lives there (managed
      `Microsoft.Extensions.Http` only); `Lyntai.Providers.LlamaSharp` earned its own package.
- [ ] `MyProvider : IModelProvider` — the seam REQUIRES exactly two members, `Id` and **`Capabilities`**
      (what the router reads BEFORE spending anything; it has no default precisely because a silent
      capability serves nothing and would make the backend permanently invisible). Everything else is
      defaulted to an `Unsupported` verdict, so override only what you serve: `CompleteAsync`,
      `StreamAsync`, and `IsAvailable`/`ProbeAsync` where a real check exists — and DECLARE each in
      `Capabilities`: `Produces` lists `ProviderKinds.Text` (a configured candidate that does not is refused at
      composition, D178), and `Operations` lists `ProviderOperation.Complete` / `.Stream` for each door you
      override, since the text router never asks an undeclared door.
- [ ] Failures classified via `ProviderVerdictClassifier` — no local heuristics.
- [ ] An HTTP backend classifies through the THREE-argument `FromHttpFailure(status, body, hasCredentials)`
      (copy `HttpModelProvider`): an uncredentialed 401/403 is `NotConfigured`, not `AuthFailed`
      (`llm-and-router.md` §Verdict taxonomy, D31).
- [ ] Empty/no output → `Failed` (and a terminal `Error` chunk when streaming), never `Ok`.
- [ ] Streaming iterates `GuardedStream.ReadAll` — its inactivity clock is the timeout
      (`llm-and-router.md` §Provider streaming timeout). Yield `Content` only for non-empty text; end with
      one `Final`(usage) or `Error`.
- [ ] Spawning a CLI → go through `ProcessRunner` (never shell out directly).
- [ ] `Add<Name>Provider(this LyntaiBuilder, …)` extension in the adapter package — a builder method names
      what it REGISTERS, with the vendor qualifying it (**D137**); register into the
      `IEnumerable<IModelProvider>` collection, resolve deps from the container.
- [ ] Baselines/registries: same rule as the CLI checklist above — Core untouched, and a package only if
      the footprint test says so, scaffolded with `node devtools/dev.mjs new-package`, never by hand.
- [ ] Tests against a stub only (stubbed `HttpMessageHandler`, or `provider-stub.mjs` via
      `LYNTAI_PROVIDER_CMD`) — cover each verdict, streaming order, empty→Failed. Never a live endpoint.
- [ ] `node devtools/dev.mjs verify` green.
