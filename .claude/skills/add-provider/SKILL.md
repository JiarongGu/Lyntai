---
name: add-provider
description: Use when adding a new LLM provider to Lyntai (a new backend/model source behind ILlmProvider, or bridging an existing Microsoft.Extensions.AI IChatClient). Covers the correct pattern, the verdict/streaming/timeout invariants, and stub-based tests.
---

# Add an LLM provider to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add an LLM provider) and `.claude/knowledge/llm-and-router.md` first.

## Decide the path
1. **Can `Microsoft.Extensions.AI` reach it?** (OpenAI/Azure/Ollama/Anthropic-API/…) → don't write a
   provider. The consumer calls `builder.AddExtensionsAiProvider("id", chatClient)`. Done.
2. **Is it a spawned CLI agent?** (`claude`, a sibling CLI) → write a **dialect**, not a provider. See the
   CLI checklist below — the spawn/verdict/streaming/maintenance invariants already live in
   `CliProviderEngine`, and a second copy of them is how they drifted before.
3. Otherwise write a native `ILlmProvider` in a new `src/Lyntai.Providers.<Name>/` package (ref Core only).

## CLI-backend checklist (the dialect path)
- [ ] New packable project `src/Lyntai.Providers.<Name>Cli/`, project-ref `Lyntai.Core` only.
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
- [ ] Baselines: Core is untouched; the new package gets a new baseline file (`ApiSurfaceTests.Assemblies()`
      + `project.config.mjs` `packableProjects` + the solution).
- [ ] `node devtools/dev.mjs verify` green.

## Native provider checklist (non-CLI)
- [ ] New packable project `src/Lyntai.Providers.<Name>/`, project-ref `Lyntai.Core` only.
- [ ] `MyProvider : ILlmProvider` — `Id`, `IsAvailable`, `CompleteAsync`, `StreamAsync`.
- [ ] Failures classified via `LlmVerdictClassifier` (429→RateLimited, 401/403→AuthFailed, filter→Refused,
      too-big→ContextWindowExceeded, deadline→Timeout, else Failed). No local heuristics.
- [ ] Empty/no output → `Failed` (and a terminal `Error` chunk when streaming), never `Ok`.
- [ ] Streaming timeout is an **inactivity clock** (re-arm per read, `CancelAfter(InfiniteTimeSpan)` after)
      — copy `OpenAiCompatibleProvider.StreamAsync`. Yield `Content` only for non-empty text; end with one
      `Final`(usage) or `Error`.
- [ ] Spawning a CLI → go through `ProcessRunner` (never shell out directly).
- [ ] `AddMyProvider(this LyntaiBuilder, …)` extension in the adapter package; register into the
      `IEnumerable<ILlmProvider>` collection, resolve deps from the container.
- [ ] Tests against a stub only (stubbed `HttpMessageHandler`, or `provider-stub.mjs` via
      `LYNTAI_PROVIDER_CMD`) — cover each verdict, streaming order, empty→Failed. Never a live endpoint.
- [ ] `node devtools/dev.mjs verify` green.
