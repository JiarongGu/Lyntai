---
name: add-provider
description: Use when adding a new LLM provider to Lyntai (a new backend/model source behind ILlmProvider, or bridging an existing Microsoft.Extensions.AI IChatClient). Covers the correct pattern, the verdict/streaming/timeout invariants, and stub-based tests.
---
<!-- local: never synced; not a daoris artifact -->

# Add an LLM provider to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add an LLM provider) and `.claude/knowledge/llm-and-router.md` first.

## Decide the path
1. **Can `Microsoft.Extensions.AI` reach it?** (OpenAI/Azure/Ollama/Anthropic-API/…) → don't write a
   provider. The consumer calls `builder.AddExtensionsAiProvider("id", chatClient)`. Done.
2. **Is it a spawned CLI agent?** (`claude`, a sibling CLI) → write a **dialect**, not a provider. See the
   CLI checklist below — the spawn/verdict/streaming/maintenance invariants already live in
   `CliProviderEngine`, and a second copy of them is how they drifted before.
3. Otherwise write a native `ILlmProvider`. WHERE it lives is the footprint test below, never a default.

## Where the code lives — the footprint test (`docs/DECISIONS.md` D31)

**A package boundary must answer "which dependency does this isolate?"** A dialect or a native provider
that needs nothing beyond Core/BCL — or only managed `Microsoft.Extensions.Http` — is **a class in
`src/Lyntai.Providers.Default/`**, next to `ClaudeCliDialect`, `CodexCliDialect` and
`OpenAiCompatibleProvider`, which is where 2.0.1 merged them. Namespaces stay `Lyntai.Providers.<Name>`
inside that one assembly (D31: consolidating packages must not force a consumer to edit a `using`).

It earns its own `src/Lyntai.Providers.<Name>/` package (project-ref `Lyntai.Core` only, never
adapter→adapter) **the moment it drags a native runtime, a platform-specific API, or a dependency a
consumer might refuse** — `Lyntai.Providers.Local` (LLamaSharp + a native backend) is the worked example.
Do not create one for tidiness: a published package id can never be freed or reused (D29), so a needless
id is permanent. If it does earn a package, scaffold it — see the Baselines bullet below; never hand-roll
the csproj.

## CLI-backend checklist (the dialect path)
- [ ] A class in `src/Lyntai.Providers.Default/` (footprint test above) — a dialect adds no dependency of
      its own, so it essentially never earns a package. `ClaudeCliDialect` and `CodexCliDialect` live there.
- [ ] **MEASURE the CLI first** — `<cmd> --help`, `--version`, and `--help` on each subcommand you intend to
      drive. Record what you measured (version + date) in the dialect's XML docs. **Never name a command you
      haven't run**: a CLI that reads an unrecognized token as a PROMPT answers it, so a guessed subcommand
      spends tokens on every call while the build stays green.
- [ ] `<Name>CliDialect : CliProviderDialectBase` — `Id`, `DefaultCommand`, `CommandEnvironmentVariables`
      (put `LYNTAI_PROVIDER_CMD` first so the stub seam works), `BuildCompletionArgs`, `ParseLine`. Set
      `PromptDelivery = Argument` only if the CLI truly takes its prompt positionally.
- [ ] Optional capabilities ONLY where measured: `VersionArgs` (default `--version`), `UpdateArgs`,
      `AuthStatusArgs` + `ParseAuthStatus`, `LogoutArgs`, `TryBuildLoginArgs`, `TryBuildInstallArgs`.
      Refuse unknown free-form values (`FlagShaped`) instead of forwarding them into argv.
- [ ] `<Name>CliProvider` — forwards to `CliProviderEngine` and implements exactly the capability interfaces
      that dialect supports. Copy `ClaudeCliProvider` (pure forwarding, no logic).
- [ ] `Add<Name>CliProvider(this LyntaiBuilder)` extension; keyed `ICliToolProvisioner` lookup by provider id.
- [ ] Tests: the parsing/argv-building unit-tested through `FakeProcessRunner` (never a real binary — and
      NEVER `login`/`logout`/`install` against one, which mutate a developer's machine), plus a real-spawn
      test against `provider-stub.mjs`. Add the CLI's shapes to the stub as needed.
- [ ] Baselines: confirm Core's own baseline is untouched. A class added to `Lyntai.Providers.Default`
      needs only Core's and Default's baselines reviewed. **Only if the backend earns its own package**
      (footprint test above): scaffold with `node devtools/dev.mjs new-package Lyntai.Providers.<Name>` —
      it registers all NINE registries `check-packages` gates (`packableProjects`, the solution,
      `<Description>`, `ApiSurfaceTests.Assemblies()`, the SEPARATE `Loaded` anchor map, the baseline file,
      the test project's `ProjectReference`, the `docs/AOT.md` row, the README row). Do not hand-roll the
      csproj: the misses are silent — a package absent from `Assemblies()` has no API gate at all.
- [ ] `node devtools/dev.mjs verify` green.

## Native provider checklist (non-CLI)
- [ ] A class in `src/Lyntai.Providers.Default/` unless the backend drags a dependency a consumer might
      refuse — the footprint test above. `OpenAiCompatibleProvider` lives there (managed
      `Microsoft.Extensions.Http` only); `Lyntai.Providers.Local` earned its own package.
- [ ] `MyProvider : ILlmProvider` — `Id`, `IsAvailable`, `CompleteAsync`, `StreamAsync`.
- [ ] Failures classified via `LlmVerdictClassifier` (429→RateLimited, 401/403→AuthFailed, filter→Refused,
      too-big→ContextWindowExceeded, deadline→Timeout, else Failed). No local heuristics.
- [ ] An HTTP backend classifies through the **three-argument** `FromHttpFailure(status, body,
      hasCredentials)` — copy `OpenAiCompatibleProvider`. A 401/403 answered to a call that carried NO
      credentials is `NotConfigured`, not `AuthFailed`: AuthFailed BENCHES the provider for the cooldown
      window, so a backend the consumer merely listed without configuring is penalised on every first
      attempt (`docs/DECISIONS.md` D38). Not "a key is required" — a local OpenAI-compatible endpoint (LM
      Studio, vLLM, Ollama) legitimately needs none, so only "no key AND the server demanded one" counts.
      A CLI/session-authenticated dialect has no `hasCredentials` fact and correctly stays on the
      two-argument overload.
- [ ] Empty/no output → `Failed` (and a terminal `Error` chunk when streaming), never `Ok`.
- [ ] Streaming timeout is an **inactivity clock** (re-arm per read, `CancelAfter(InfiniteTimeSpan)` after)
      — copy `OpenAiCompatibleProvider.StreamAsync`. Yield `Content` only for non-empty text; end with one
      `Final`(usage) or `Error`.
- [ ] Spawning a CLI → go through `ProcessRunner` (never shell out directly).
- [ ] `AddMyProvider(this LyntaiBuilder, …)` extension in the adapter package; register into the
      `IEnumerable<ILlmProvider>` collection, resolve deps from the container.
- [ ] Baselines/registries: same rule as the CLI checklist above — Core untouched, and a package only if
      the footprint test says so, scaffolded with `node devtools/dev.mjs new-package`, never by hand.
- [ ] Tests against a stub only (stubbed `HttpMessageHandler`, or `provider-stub.mjs` via
      `LYNTAI_PROVIDER_CMD`) — cover each verdict, streaming order, empty→Failed. Never a live endpoint.
- [ ] `node devtools/dev.mjs verify` green.
