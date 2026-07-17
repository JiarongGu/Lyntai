# Trimming & Native AOT status

Lyntai targets net10.0 and sets `IsAotCompatible=true` in `src/Directory.Build.props` for every
packable project, which turns on the trim, single-file, and AOT analyzers. Per-package status:

| Package | Trim / AOT | Notes |
|---|---|---|
| `Lyntai.Core` | ✅ compatible | Pure abstractions + router + cortex; `System.Text.Json` used with `JsonDocument`/`JsonNode` (no reflection-based (de)serialization). DI generic registrations carry `DynamicallyAccessedMembers` annotations. |
| `Lyntai.Storage.InMemory` | ✅ compatible | Zero dependencies beyond Core; no reflection. |
| `Lyntai.Providers.ClaudeCli` | ✅ compatible | Process spawn + `JsonDocument` parsing. |
| `Lyntai.Providers.OpenAiCompatible` | ✅ compatible | `HttpClient` + `JsonNode`/`JsonDocument`. |
| `Lyntai.Providers.ExtensionsAi` | ✅ compatible | Thin bridge over `Microsoft.Extensions.AI.Abstractions`. |
| `Lyntai.Storage.Sqlite` | ⚠️ **opts out** | `IsAotCompatible=false; IsTrimmable=false; EnableTrimAnalyzer=true`. Dapper and FluentMigrator materialize via reflection/IL-emit, which the project-level analyzer can't see through — claiming compatibility would be dishonest. The analyzer stays on for *our* code in this package. |
| `Lyntai.Storage.Postgres` | ⚠️ **opts out** | Same as Sqlite — Npgsql + Dapper + FluentMigrator reflection. Analyzer on for our code. |
| `Lyntai.Providers.Local` | ⚠️ **opts out** | Same stance — LLamaSharp loads the native llama.cpp backend dynamically and materializes options via reflection; a native-interop package can't honestly claim AOT/trim compatibility. Analyzer on for our code. |
| `Lyntai.Tools.Mcp` | ⚠️ **opts out** | MCP argument/result marshaling is dynamic JSON (reflection). Analyzer on for our code. |
| `Lyntai.Providers.ClaudeCli.Mcp` | ⚠️ **opts out** | Hosts Kestrel + dynamic-JSON tool marshaling — a hosting package can't claim trim/AOT. Analyzer on for our code. |

## Why the project-level analyzer isn't the whole story

Per Microsoft's guidance, project-level trim analysis does **not** surface dependency warnings: a
self-contained app that publishes trimmed, roots the library as a `TrimmerRootAssembly`, and pulls in
its transitive dependencies is required to see the real picture. Dapper is neither `IsTrimmable`-marked
nor `RequiresUnreferencedCode`-annotated, so its reflection-based row materialization is invisible until
you actually AOT-publish an app that uses `Lyntai.Storage.Sqlite`.

## Consuming Lyntai from an AOT / trimmed app

- Using only the AOT-compatible packages (Core + a provider + `Lyntai.Storage.InMemory`) publishes
  clean under `PublishAot=true` / `PublishTrimmed=true`.
- Using `Lyntai.Storage.Sqlite` under trimming/AOT is **not currently supported without warnings**.
  The path to fixing it (roadmap v0.5 → v1.0): evaluate **Dapper.AOT** (source-generated command
  materialization) for the async + `MatchNamesWithUnderscores` + FluentMigrator combination, or provide
  an alternative source-generated storage backend. Until then, either use the in-memory backend in an
  AOT app or accept the trim warnings with a documented suppression.
