<p align="center">
  <img src="https://raw.githubusercontent.com/JiarongGu/Lyntai/master/docs/assets/lyntai-icon.png" width="112" alt="Lyntai">
</p>

# Lyntai (灵台)

[![NuGet](https://img.shields.io/nuget/v/Lyntai.svg?label=nuget)](https://www.nuget.org/packages/Lyntai)
[![NuGet downloads](https://img.shields.io/nuget/dt/Lyntai.svg)](https://www.nuget.org/packages/Lyntai)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/JiarongGu/Lyntai/blob/master/LICENSE)

**A .NET 10 library for building LLM applications.** One `ITextClient` front door routes and fails over
across OpenAI, Azure OpenAI, OpenRouter, Ollama, llama.cpp, in-process GGUF and ONNX models and the `claude`
and `codex` CLIs. The same `AddLyntai(...)` call wires long-term agent memory, MCP tools, a prompt registry,
evals, run traces, durable jobs and image, video and audio generation, over SQLite, PostgreSQL (pgvector),
plain files or memory.

> 灵台 (língtái) — "the numinous platform," a classical Chinese name for the seat of the mind.

It is a library, not a platform: no server, no host, no UI. You compose it into your own application through
dependency injection, and the one package you cannot opt out of depends on nothing but the DI and logging
abstractions.

**Contents:** [Why Lyntai](#why-lyntai) · [Quickstart](#quickstart) · [What's in the box](#whats-in-the-box) ·
[Backends](#backends) · [Packages](#packages) · [Status](#status) · [Documentation](#documentation) ·
[For AI coding agents](#for-ai-coding-agents) · [License](#license)

## Why Lyntai

- **One front door, any backend.** Your code calls `ITextClient`; candidate order, failover, retries and
  dead-host cooldown happen behind it, under a `RoutingPolicy` you can tune per verdict. A stream fails over
  only before its first token, so a consumer never sees duplicated output.
- **Use the subscriptions you already have.** The signed-in `claude` (Claude Code) and `codex` CLIs are
  first-class backends — completions, self-driving agent sessions, and your own tools hosted for them over
  MCP — with no API key.
- **Local-first when you want it.** llama.cpp's `llama-server`, Ollama, GGUF models in process through
  LLamaSharp, ONNX embedders and rerankers in process, and a `model2vec` embedder that needs no server, GPU
  or port.
- **Memory that behaves like memory.** Named memory engines over keyword (FTS5 trigram, and not only for
  English), semantic (cosine, pgvector) and curated tiers — plus a graph memory that forgets by
  interference rather than by the clock, buries instead of deleting, and recalls by walking one-line
  headlines.
- **LLM-ops built in.** A prompt registry with runtime overrides, deterministic and LLM-judge scorers, run
  traces, OpenTelemetry GenAI telemetry, and a governed front door — response cache, usage budget, rate
  limit — that the tool loop, the scorers and memory all pass through.
- **Agents and tools.** A tool loop over any backend (native function calling, or a prompt protocol where
  there is none), MCP in both directions, two-gate chat orchestration with guards, and durable jobs with
  checkpoints, priorities, cron schedules and a dead-letter queue.
- **Storage you pick per domain.** SQLite, PostgreSQL, in-memory or human-readable files, mixed per domain;
  every object Lyntai creates is `lyntai_`-prefixed, so it can share your application's database.

## Quickstart

```bash
dotnet add package Lyntai
```

```csharp
using Lyntai;
using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLyntai(cfg => cfg
    .AddOllamaProvider(model: "llama3.2")        // a local model…
    .AddClaudeCliProvider()                      // …and the signed-in `claude` CLI behind it
    .UseDefaultCandidates("ollama", "claude-cli"));

await using var sp = services.BuildServiceProvider();
var llm = sp.GetRequiredService<ITextClient>();

var reply = await llm.CompleteAsync(new TextRequest { Messages = [TextMessage.User("Name three moons of Jupiter.")] });
Console.WriteLine(reply.Verdict.IsOk() ? reply.Text : $"{reply.Verdict}: {reply.Detail}");
```

A failure comes back as a **verdict, not an exception**: `reply.Verdict` says whether every candidate was
down, rate-limited, unconfigured or refused the request. Add `Lyntai.Storage.Sqlite` and
`cfg.UseSqliteStorage("app.db")` to persist prompts, memory, traces and jobs; the
[guide](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#install-and-compose) composes the rest.

## What's in the box

| You want | Reach for | Read |
|---|---|---|
| Call any model with failover | `ITextClient`, `UseDefaultCandidates`, `ConfigureRouting` | [routing semantics](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#the-semantics-youre-getting-design-6) |
| JSON that parses | `CompleteJsonAsync` with `TextRequest.JsonSchema` | [structured output](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#structured-output) |
| A response cache, a spend cap, a rate limit | `AddResponseCache`, `AddUsageBudget`, `AddRateLimit` | [governance](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#governance-response-cache-usage-budget-rate-limit) |
| Long-term memory for an agent | `AddMemory()`, `AddMemoryEngine(…)`, `IMemoryEngineFactory` | [memory engines](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#named-memory-engines), [graph memory](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#graph-memory--forgetting-relinking-and-a-cheap-first-load), [the memory guide](https://github.com/JiarongGu/Lyntai/blob/master/docs/memory.md) |
| Retrieval by meaning (RAG) | `AddSemanticMemory()`, `IVectorStore` on SQLite or pgvector | [semantic memory](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#semantic-memory) |
| Tool calling and MCP | `IToolLoop`, `AddTool`, `AddMcpTools`, `AddMcpToolHost` | [tool-calling](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#tool-calling-lyntaiagents) |
| Claude Code or Codex as an agent | `IAgentSession`, `AddClaudeCliAgentSession()` | [CLI-agent sessions](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#cli-agent-session-vs-itoolloop-iagentsession) |
| Prompts, scores, traces, OpenTelemetry | `IPromptRegistry`, `AddScorer<T>()`, `ITraceService`, `AddTextCallTracing()` | [observability](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#observability) |
| Background work that survives a restart | `AddJobHandler<T>()`, `IJobQueue`, `AddCronSchedule` | [durable jobs](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#durable-jobs-lyntaijobs) |
| Image, video, audio and 3D | `Lyntai.Generation`, `IMediaRouter` | [generation](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#generation-image--video--audio--3d-lyntaigeneration), [the generation guide](https://github.com/JiarongGu/Lyntai/blob/master/docs/generation.md) |
| A backend Lyntai has no provider for | `AddBridgeProvider` — wraps any SDK, or a `Microsoft.Extensions.AI` `IChatClient`, in a lambda | [bridging](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#bridging-a-backend-lyntai-has-no-provider-for-addbridgeprovider) |
| Backends your users configure at run time | `ITextRouterFactory`, `UseTextProviderRegistry()` | [user-owned configuration](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#when-your-users-own-the-backend-configuration-lyntaiinference) |
| Guards, a secret vault, vision | `AddGuard<T>()`, `AddSecretVault(key)`, `TextMessage.UserWithImage` | [the rest](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md#guards-orchestration-secrets-vision) |

## Backends

| Backend | Registration | Package |
|---|---|---|
| OpenAI | `AddOpenAiProvider(apiKey)` | `Lyntai.Providers.Basic` |
| Azure OpenAI | `AddAzureOpenAiProvider(endpoint, apiKey)` | `Lyntai.Providers.Basic` |
| OpenRouter | `AddOpenRouterProvider(apiKey)` | `Lyntai.Providers.Basic` |
| llama.cpp `llama-server` | `AddLlamaProvider()` | `Lyntai.Providers.Basic` |
| Ollama | `AddOllamaProvider()` | `Lyntai.Providers.Basic` |
| LM Studio, vLLM, any server speaking OpenAI's schema | `AddHttpProvider(id, o => o.BaseUrl = …)` | `Lyntai.Providers.Basic` |
| Claude Code (`claude` CLI) | `AddClaudeCliProvider()`, `AddClaudeCliAgentSession()` | `Lyntai.Providers.Basic` |
| OpenAI Codex (`codex` CLI) | `AddCodexCliProvider()`, `AddCodexCliAgentSession()` | `Lyntai.Providers.Basic` |
| `model2vec` embeddings, in process | `AddModel2VecProvider(dir)` | `Lyntai.Providers.Basic` |
| GGUF models, in process | `AddLlamaSharpProvider(modelPath)` | `Lyntai.Providers.LlamaSharp` |
| ONNX embedders and rerankers, in process | `AddOnnxProvider(dir)` | `Lyntai.Providers.Onnx` |
| Anything else | `AddBridgeProvider(id, (req, ct) => …)` | `Lyntai.Core` |
| OpenAI images, Automatic1111, ComfyUI, `sd-cli`, fal.ai, piper TTS | `AddOpenAiImageProvider`, `AddAutomatic1111Provider`, `AddComfyUiProvider`, `AddLocalDiffusionProvider`, `AddFalProvider`, `AddPiperProvider` | `Lyntai.Generation` |

Vendors market an endpoint that speaks OpenAI's wire as "OpenAI-compatible"; what Lyntai matches on is the
dialect a server actually speaks, so `AddHttpProvider` reaches any of them for chat, embeddings or reranking
— and Ollama's native API has a provider of its own. Every backend is "just another candidate": a hosted
model first and a local one as the offline backstop is one `UseDefaultCandidates` call.

## Packages

| Package | What it gives you |
|---|---|
| **`Lyntai`** | **The starting set (5 of 11)** — Core + the dependency-free LLM backends + both halves of MCP + **in-memory** storage + **file** storage (on once you name a root). Not the whole library: add `Lyntai.Storage.Sqlite` for a database and `Lyntai.Generation` for media. |
| `Lyntai.Core` | Every domain's contracts and engines: LLM routing/fallback, generation, cortex (prompt/scoring/trace), jobs, guards, secrets, memory, storage interfaces, tools, DI — plus `Lyntai.Text.WordPieceTokenizer`, a BERT tokenizer owned rather than depended on (**D122**), usable anywhere a token-aware step is wanted. Deps: DI + Logging abstractions only. |
| `Lyntai.Providers.Basic` | The dependency-free **LLM** backends — Core, the BCL and `Microsoft.Extensions.Http`, no native payload: authenticated `claude` and `codex` CLIs (completions and agent sessions); any OpenAI-shaped endpoint (OpenAI, Azure OpenAI, OpenRouter, llama-server) for chat, embeddings or reranking; Ollama's native API for chat and embeddings; `AddModel2VecProvider(dir)` — in-process embedding over a `model2vec` table with no server, GPU or port. Media backends are in `Lyntai.Generation`. |
| `Lyntai.Providers.LlamaSharp` | In-process local GGUF inference via LLamaSharp — add an `LLamaSharp.Backend.*` for your hardware. Named for the dependency, not the deployment: `AddLlamaSharpProvider(modelPath)`. |
| `Lyntai.Storage.Sqlite` | SQLite for every storage domain (Dapper + FluentMigrator + FTS5; ships a native SQLite binary). |
| `Lyntai.Storage.Postgres` | PostgreSQL storage (Npgsql + `pg_trgm` recall) for a server-backed deployment. |
| `Lyntai.Storage.Basic` | The dependency-free storage backends. **In memory** (`UseInMemoryStorage`) — tests, ephemeral use, or mixed per-domain; and **files** under a root you choose (`UseFileSystemStorage`) — one Markdown record per file, readable without a client — for key-value, prompts, conversations, task memory, curated memory and the memory engine's graph, with SQLite composed for the rest. |
| `Lyntai.Tools.Mcp` | MCP in BOTH directions: expose an MCP server's tools as Lyntai `ITool`s, and host your `ITool`s as an ephemeral loopback MCP server for a CLI that runs its own agent loop. (The tool *contract* is in Core; this is the wire adapter.) |
| `Lyntai.Secrets.Dpapi` | Windows DPAPI + recovery-key envelope for the secret vault. |
| `Lyntai.Providers.Onnx` | In-process **transformers** via ONNX Runtime — no server, no port. `AddOnnxProvider(dir)` embeds (pooling, normalization and the sequence limit read from the model's own files; the tokenizer is the model's WordPiece `vocab.txt` or its SentencePiece `tokenizer.json`, which is how the XLM-R multilingual family loads); the same call with `o.Produces = ProviderKinds.Score` scores `(query, document)` pairs, which is what `AddMemoryScoringVerification()` reranks recalls with. References the **managed half only**: add one native backend yourself (`Microsoft.ML.OnnxRuntime` for CPU, `.DirectML` for any DX12 GPU, `.Gpu` for CUDA), because the library does not choose your hardware. |
| `Lyntai.Generation` | The media backend set — OpenAI images, Automatic1111, ComfyUI, a local `sd-cli` subprocess, the fal.ai queue for video, and streaming piper TTS, each with an `Add*` of its own. Adds only `Microsoft.Extensions.Http` (its shims register named clients); the generation *contracts* are in Core. A separate package for FOOTPRINT, not for release cadence — it carries the full SemVer promise like every other (**D70**), and stays outside the bundle so a one-line install does not drag media backends for a feature most apps never call (D25/D26). |

Packages are split by **dependency footprint**, never by vendor or by size: backends that need nothing extra
share `Providers.Basic`; anything dragging a native runtime, a platform-specific API or a protocol stack of its
own (`ModelContextProtocol.Core`) is its own package; and `Lyntai.Core`, the one package you cannot opt out
of, carries the smallest footprint of all (`docs/DECISIONS.md` D25). A package joins **the bundle** only if it
adds no third-party dependency beyond the `Microsoft.Extensions.*` band, or is near-universal with the cost
accepted explicitly — never with a native payload or a platform-specific API (D26).

## Status

<!-- version-indicator: the **vX.Y.Z below is AUTO-SYNCED from src/Directory.Build.props <VersionPrefix> by
     `node devtools/dev.mjs pack` / `doctor --fix` (the release pipeline bumps the version, pack updates this
     headline). Don't hand-edit the version here to release — bump VersionPrefix; the header follows. -->
**v3.5.3 — a hardened, batteries-included cortex substrate, now with a media generation platform.**
Eleven packages; one public front door, and a public API frozen under SemVer 2.0 since 1.0.

**This page describes the working tree, not only the newest package**: anything that has not shipped yet is
listed under `## Unreleased` in [`CHANGELOG.md`](https://github.com/JiarongGu/Lyntai/blob/master/CHANGELOG.md),
so check there before assuming a member is in the version you installed.

> **Versioning.** SemVer 2.0 from **1.0**, gated by the `ApiSurfaceTests` baseline, and every package carries it
> (`docs/DECISIONS.md` **D70**). One relaxation, stated up front, while every consumer is first-party: a public-API
> or behaviour break may ship in a MINOR release. It is always listed under that release's **Breaking** heading
> in `CHANGELOG.md`, naming the action it needs (**D18**, **D161**). Storage and migration breaks still need a
> major.

## Documentation

- [**The guide**](https://github.com/JiarongGu/Lyntai/blob/master/docs/guide.md) — every feature, the
  semantics it guarantees, and a sample for each.
- [Long-term memory](https://github.com/JiarongGu/Lyntai/blob/master/docs/memory.md) — how a recall works,
  configuration, and measured costs.
- [Media generation](https://github.com/JiarongGu/Lyntai/blob/master/docs/generation.md) — backends, delivery,
  pipelines, durable renders, and what is and is not verified against a real service.
- [Model-backed tasks](https://github.com/JiarongGu/Lyntai/blob/master/docs/model-tasks.md) — every
  model-backed seam by the SHAPE of the question it asks; read it before sizing a model, since bounding the
  input is usually cheaper than a bigger model.
- [Deployment shapes](https://github.com/JiarongGu/Lyntai/blob/master/docs/deployment-shapes.md) — the same
  evidence, cut by the shape of the deployment.
- [AOT and trimming](https://github.com/JiarongGu/Lyntai/blob/master/docs/AOT.md) — per-package status and the
  measured publish footprint.
- [The design record](https://github.com/JiarongGu/Lyntai/blob/master/docs/2026-07-17-lyntai-design.md) and
  [the decisions behind it](https://github.com/JiarongGu/Lyntai/blob/master/docs/DECISIONS.md).
- [Changelog](https://github.com/JiarongGu/Lyntai/blob/master/CHANGELOG.md) — per-release detail, breaking
  changes called out; [roadmap](https://github.com/JiarongGu/Lyntai/blob/master/docs/ROADMAP.md) — one line
  per version.
- [A runnable sample](https://github.com/JiarongGu/Lyntai/tree/master/samples/Lyntai.Playground) — the
  Playground console app.

## For AI coding agents

Lyntai publishes [`llms.txt`](https://raw.githubusercontent.com/JiarongGu/Lyntai/master/llms.txt), an index of
these docs written for language models, and a
[`context7.json`](https://github.com/JiarongGu/Lyntai/blob/master/context7.json) telling the
[Context7](https://context7.com) documentation index what to serve. The rules a coding agent most often gets
wrong when it writes against Lyntai:

<!-- agent-rules:begin — llms.txt and context7.json's `rules` carry these same lines; check-docs holds the three equal (D197) -->
- Compose everything through `services.AddLyntai(cfg => …)` and inject what it registers; never construct a provider, router or store by hand.
- Call models through the injected `ITextClient`. `UseDefaultCandidates(…)` sets the fallback order over registered provider ids — by default "openai", "azure-openai", "openrouter", "llama", "ollama", "claude-cli", "codex-cli" and "llamasharp".
- A failed call returns a verdict, never an exception: check `reply.Verdict.IsOk()` and read `reply.Detail`. A bridge registered with `AddBridgeProvider` returns `ProviderVerdict.Failed` rather than throwing.
- The `Lyntai` package is a starting set: nothing persists until you choose storage (`UseSqliteStorage` from Lyntai.Storage.Sqlite, `UsePostgresStorage` from Lyntai.Storage.Postgres, or `UseFileSystemStorage`), and media generation needs Lyntai.Generation.
- An embedder is a registration of its own with `o.Produces = ProviderKinds.Vector`, even on a server that also hosts the chat model; `AddSemanticMemory()` then fails at startup when no embedder is registered.
- Long-term memory goes through `AddMemory()` or `AddMemoryEngine(name, …)` and `IMemoryEngineFactory`. Graph memory decays by interference and hides rather than deletes; only `PruneAsync` removes an entry.
- Add a cross-cutting layer to the front door with `AddFrontDoorDecorator(order, …)` and a class derived from `DelegatingTextClient`; registering your own `ITextClient` silently discards the cache, budget and rate limit.
- The builder extensions are in namespace `Lyntai`; `ITextClient`, `TextRequest`, `TextMessage` and `ProviderVerdict` are in `Lyntai.Inference`; the memory types are in `Lyntai.Memory`.
<!-- agent-rules:end -->

## Contributing

Issues and pull requests are welcome. The working conventions are
[`CLAUDE.md`](https://github.com/JiarongGu/Lyntai/blob/master/CLAUDE.md), and what each gate is for is
[`docs/GATES.md`](https://github.com/JiarongGu/Lyntai/blob/master/docs/GATES.md):
`node devtools/dev.mjs verify` is the "am I done?" check every change passes.

## Credits

Extracted from the good parts of four sibling projects: the storage/scoring/trace patterns of
**Gatherlight**, the provider abstraction of **Vidora**, the verdict-classification + memory of **Sonora**,
mastra's **composable domain storage**, and odysseus's **streaming-aware fallback**.

## License

[MIT](https://github.com/JiarongGu/Lyntai/blob/master/LICENSE) © Jiarong Gu — the same `MIT` SPDX expression
every NuGet package carries.
