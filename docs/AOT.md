# Trimming & Native AOT status

Lyntai targets net10.0 and sets `IsAotCompatible=true` in `src/Directory.Build.props` for every
packable project, which turns on the trim, single-file, and AOT analyzers. Per-package status:

| Package | Trim / AOT | Notes |
|---|---|---|
| `Lyntai.Core` | ✅ compatible | Pure abstractions + router + cortex + the generation platform + `Lyntai.Text`'s WordPiece tokenizer; `System.Text.Json` used with `JsonDocument`/`JsonNode`/`Utf8JsonWriter` (no reflection-based (de)serialization). DI generic registrations carry `DynamicallyAccessedMembers` annotations. The tokenizer is OWNED rather than referenced (**D122**) — which is what made it eligible for Core at all, since Core takes no third-party dependency. |
| `Lyntai.Storage.Basic` | ✅ compatible | Zero dependencies beyond Core; no reflection. The file backend reads and writes its records through `System.Text.Json`'s reader and writer, never a reflection serializer. |
| `Lyntai.Providers.Basic` | ✅ compatible | Process spawn + `HttpClient`, responses parsed with `JsonDocument`, request bodies built with `JsonNode`/`Utf8JsonWriter`. The `claude`/`codex` CLIs, OpenAI-compatible chat/embeddings/reranking, Ollama's native API, and the in-process model2vec embedder (its table read over a header and a `Buffer.BlockCopy`, so nothing reflects over types). **It carries no third-party dependency but `Microsoft.Extensions.Http`** since **D146** deleted the `Microsoft.Extensions.AI` bridge — the 656 KB row in the trimming table below is what that removal returned, without trimming, to a consumer that does not also reference `Lyntai.Tools.Mcp`, whose SDK still carries `Microsoft.Extensions.AI.Abstractions` into the bundle. |
| `Lyntai.Secrets.Dpapi` | ✅ compatible | P/Invoke to the Windows DPAPI; no reflection. |
| `Lyntai.Storage.Sqlite` | ⚠️ **opts out** | `IsAotCompatible=false; IsTrimmable=false; EnableTrimAnalyzer=true`. Dapper and FluentMigrator materialize via reflection/IL-emit, which the project-level analyzer can't see through — claiming compatibility would be dishonest. The analyzer stays on for *our* code in this package. |
| `Lyntai.Storage.Postgres` | ⚠️ **opts out** | Same as Sqlite — Npgsql + Dapper + FluentMigrator reflection. Analyzer on for our code. |
| `Lyntai.Providers.LlamaSharp` | ⚠️ **opts out** | Same stance — LLamaSharp loads the native llama.cpp backend dynamically and materializes options via reflection; a native-interop package can't honestly claim AOT/trim compatibility. Analyzer on for our code. |
| `Lyntai.Tools.Mcp` | ⚠️ **opts out** | MCP argument/result marshaling is dynamic JSON (reflection), in both directions — consuming a server's tools and hosting ours through the SDK's server transport. Analyzer on for our code. |
| `Lyntai.Generation` | ✅ compatible | The media backends: `HttpClient` + `JsonDocument`/`JsonObject`, and two subprocesses (`sd-cli`, piper) through `IProcessRunner`. Its only dependency is managed `Microsoft.Extensions.Http` (for the per-backend `Add*` shims' named clients). |
| `Lyntai.Providers.Onnx` | ⚠️ **opts out** | `IsAotCompatible=false; IsTrimmable=false; EnableTrimAnalyzer=true`. ONNX Runtime loads a native library dynamically and resolves its execution provider by name; a native-interop package cannot honestly claim trim/AOT compatibility. Analyzer on for our code. **It references the MANAGED half only** — the app adds `Microsoft.ML.OnnxRuntime` (CPU), `.DirectML` or `.Gpu`, so this row is about our assembly and the app's backend choice governs the rest. |
| `Lyntai` (the bundle) | n/a | Ships no assembly (`IncludeBuildOutput=false`) — its status is whatever its references are. |

## Keeping the ✅ rows honest

A `✅` row is not a comment: `IsAotCompatible=true` stamps `IsTrimmable` into the assembly, which tells a
consumer's trimmer that the code inside survives trimming. The only thing that catches code breaking that
promise is an **IL2026 / IL3050** warning, so:

- **`verify` fails on any warning in a published project** (`node devtools/dev.mjs check-warnings`). That gate
  exists because four reflection-serialization calls — `JsonSerializer.Serialize(new { … })` on anonymous types,
  in three generation backends — reached `Lyntai.Providers.Basic` while it advertised the opposite. The
  warnings were there the whole time; nothing failed on them.
- **Build a request body with `JsonObject` or `Utf8JsonWriter`, never an anonymous type.** The reflection
  serializer is what breaks under trimming; `Payloads/OpenAiPayload.cs` is the pattern to copy.
- A package that genuinely can't comply **opts out** (the ⚠️ rows) instead of shipping a false claim.

## Why the project-level analyzer isn't the whole story

Per Microsoft's guidance, project-level trim analysis does **not** surface dependency warnings: a
self-contained app that publishes trimmed, roots the library as a `TrimmerRootAssembly`, and pulls in
its transitive dependencies is required to see the real picture. Dapper is neither `IsTrimmable`-marked
nor `RequiresUnreferencedCode`-annotated, so its reflection-based row materialization is invisible until
you actually AOT-publish an app that uses `Lyntai.Storage.Sqlite`.

## Consuming Lyntai from an AOT / trimmed app

- Using only the packages **the table above marks `✅ compatible`** publishes clean under `PublishAot=true` /
  `PublishTrimmed=true`. Read the table, not a second list here: `check-packages` fails if a package has no row
  in it, so the table cannot silently omit one — a hand-kept enumeration in this sentence can.
- **What trimming actually buys, measured 2026-08-04** on a console app that references the `Lyntai` bundle
  but calls only `AddLyntai` and the text front door. The rows name that day's package set — before the MCP
  SDK's major (**D172**) and before `Storage.InMemory` folded into `Lyntai.Storage.Basic` (**D173**) — so read
  the SHAPE of the result, not the kilobytes:

  | | Plain `publish` | `PublishTrimmed=true` |
  |---|---|---|
  | `Lyntai.Core.dll` | 528 KB | **40 KB** |
  | `Lyntai.Providers.Basic.dll` | 158 KB | **11 KB** |
  | `Microsoft.Extensions.AI.Abstractions.dll` | 656 KB | **removed** |
  | `ModelContextProtocol.Core.dll` | 1188 KB | **removed** |
  | `Lyntai.Tools.Mcp` + `.Hosting`, `Storage.InMemory` | 90 KB | **removed** |
  | Lyntai's total footprint | 3.2 MB | **0.21 MB** |

  The removals answer "does an unused dependency ship?" — no, once trimming is on. The 93% shrink of the
  assemblies that ARE used is the payoff for honest `IsTrimmable` metadata: an assembly without it is copied
  **whole** under the default partial trim mode. A library cannot enable trimming for its consumer (it needs a
  self-contained publish, which is the app's decision) — all a library controls is whether trimming pays off
  when they do.
- Using `Lyntai.Storage.Sqlite` under trimming/AOT is **not currently supported without warnings**.
  The path to fixing it: evaluate **Dapper.AOT** (source-generated command materialization) for the async +
  `MatchNamesWithUnderscores` + FluentMigrator combination, or provide an alternative source-generated storage
  backend. Until then, either use the in-memory or file-system backend in an AOT app — the file-system one
  persists the six domains it serves — or accept the trim warnings with a documented suppression.
