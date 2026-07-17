---
name: add-provider
description: Use when adding a new LLM provider to Lyntai (a new backend/model source behind ILlmProvider, or bridging an existing Microsoft.Extensions.AI IChatClient). Covers the correct pattern, the verdict/streaming/timeout invariants, and stub-based tests.
---

# Add an LLM provider to Lyntai

Read `.claude/knowledge/extending-lyntai.md` (§Add an LLM provider) and `.claude/knowledge/llm-and-router.md` first.

## Decide the path
1. **Can `Microsoft.Extensions.AI` reach it?** (OpenAI/Azure/Ollama/Anthropic-API/…) → don't write a
   provider. The consumer calls `builder.AddExtensionsAiProvider("id", chatClient)`. Done.
2. Otherwise write a native `ILlmProvider` in a new `src/Lyntai.Providers.<Name>/` package (ref Core only).

## Native provider checklist
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
