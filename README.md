# Lyntai (灵台)

> 灵台 (língtái) — "the numinous platform," a classical Chinese name for the seat of the mind.

A reusable **.NET 10 library**: the shared **cortex + persistence** substrate for AI apps. Give a new
project an LLM provider abstraction with routing + fallback, pluggable storage, and an LLM-ops layer
(prompt registry, scoring, traces, task-scoped memory) — `AddLyntai(...)` and go, no rebuilding it per app.

Extracted from the good parts of four sibling projects: the storage/scoring/trace patterns of
**Gatherlight**, the provider abstraction of **Vidora**, the verdict-classification + memory of **Sonora**,
mastra's **composable domain storage**, and odysseus's **streaming-aware fallback**.

## Status

**Pre-implementation.** Design + build plan are done; the code is being written phase-by-phase.
- `docs/2026-07-17-lyntai-design.md` — the design contract (interfaces, fork decisions, semantics, scope).
- `tasks.md` — the phased implementation plan.

## Packages

| Package | What it gives you |
|---|---|
| `Lyntai.Core` | Interfaces + the fallback router + cortex (prompt/scoring/trace) + DI. No heavy deps. |
| `Lyntai.Storage.Sqlite` | SQLite implementation of every storage domain (Dapper + FluentMigrator + FTS5). |
| `Lyntai.Providers.ClaudeCli` | The authenticated `claude` CLI as a provider (no API key). |
| `Lyntai.Providers.OpenAiCompatible` | OpenAI / Ollama / OpenRouter-style endpoints over HttpClient. |
| `Lyntai.Providers.ExtensionsAi` | Bridge: any `Microsoft.Extensions.AI` `IChatClient` → a Lyntai provider. |

Each `src/*` is an independent NuGet package depending only on `Lyntai.Core` — add just what you need.

## Using it (target ergonomics)

```csharp
services.AddLyntai(cfg =>
{
    cfg.AddClaudeCliProvider();                                       // family default, no API key
    cfg.AddOpenAiCompatibleProvider("ollama", o => o.BaseUrl = "http://localhost:11434");
    cfg.UseSqliteStorage("app.db");
    cfg.DefaultCandidates("claude-cli", "ollama");                    // router fallback order
});

// later, injected:
public MyService(ILlmRouter llm, IMemoryStore memory, IScoringService scoring) { … }
```

## Dev loop

```
node devtools/dev.mjs build            # build the solution
node devtools/dev.mjs test             # xUnit tests
node devtools/dev.mjs e2e --build      # Playground full-stack smoke against the provider-stub
node devtools/dev.mjs pack             # dotnet pack → publish/packages/
node devtools/dev.mjs install-hooks    # enable the pre-commit sensitive-info guard
```

See `.claude/rules/dev-conventions.md` for the load-bearing patterns.
