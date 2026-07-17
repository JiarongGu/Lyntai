# Extending Lyntai

On-demand detail for the four extension points. Read the one you're touching. The always-on rules are
in `.claude/rules/dev-conventions.md`; the correctness invariants are in `llm-and-router.md` and
`storage.md`; the traps are in `pitfalls.md`.

Lyntai's whole value is being extended without forking. Every extension is **an interface in
`Lyntai.Core` + an implementation in an adapter package that depends only on Core** (never adapter →
adapter) + **a `LyntaiBuilder` extension method** so the consumer wires it with one line.

---

## Add an LLM provider

Two paths — pick the cheaper one:

**A. Bridge an existing `Microsoft.Extensions.AI` `IChatClient` (preferred).** OpenAI, Azure, Ollama,
Anthropic-API, etc. already have MEAI clients. You do *nothing* but register:
`builder.AddExtensionsAiProvider("my-id", theChatClient)`. `ExtensionsAiProvider` handles the mapping,
streaming, usage, and verdict-from-exception. **Only write a native provider if MEAI can't reach it.**

**B. Native `ILlmProvider`** (like `ClaudeCliProvider`, `OpenAiCompatibleProvider`). New package
`src/Lyntai.Providers.<Name>/`, ref Core only. Implement:

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

## Add a storage backend

New package `src/Lyntai.Storage.<Backend>/`, ref Core only. Implement the domain interfaces the consumer
needs (`IKeyValueStore`, `IConversationStore`, `IMemoryStore`, `IScoreStore`, `ITraceStore`) — they're
independent, you don't have to do all five. Provide `builder.Use<Backend>Storage(...)` that registers an
`IDbConnectionFactory` (or the backend's equivalent) + the stores + runs migrations.

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
  clamped `{score,reason}`. You supply the criterion prompt.

Register into the DI collection: `builder.AddScorer<MyScorer>()`. `ScoringService` iterates
`IEnumerable<IScorer>` and isolates a throwing scorer — never add an `if/switch` over scorer ids.

---

## Add a migration

`node devtools/dev.mjs new-migration <name>` scaffolds `src/Lyntai.Storage.Sqlite/Migrations/M<num>_<Name>.cs`
with a **guaranteed-unique, monotonic** `YYYYMMDDNNNN` number (reusing a number is silently skipped —
never hand-pick one). Then fill `Up()`:
- Prefix every object `lyntai_`. snake_case columns. Composite PK + FK **inline at `Create.Table`**
  (SQLite can't `ALTER ADD CONSTRAINT`).
- Searchable text → FTS5 **trigram** external-content mirror + AFTER INSERT/DELETE/UPDATE triggers
  (emit the `'delete'` command row on delete **and** update) + an in-migration backfill. Copy
  `M202607170003_Memory` exactly; the delete/update trigger is the #1 botched thing here (`storage.md`).
- The runner applies migrations under WAL + `busy_timeout` (set in `MigrationRunnerService`); it's
  idempotent, so re-running on an up-to-date db is a no-op.
