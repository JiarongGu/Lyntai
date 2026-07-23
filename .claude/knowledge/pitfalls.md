# Pitfalls — don't reintroduce these

Concrete traps, most surfaced by a real bug or an independent audit. Each passed the build (and usually
the tests) while being wrong. Skim before touching the relevant area.

## Environment / tooling

- **This machine's console is GBK/CP936.** Writing UTF-8 through it (PowerShell `Set-Content`/`Out-File`
  without `-Encoding utf8`, `echo >`, a shell heredoc) **double-encodes and lossily corrupts** non-ASCII
  content — it once mangled every `灵台`/`—`/`§` in `tasks.md` irreversibly. **Always write files with
  the Write/Edit tools** (they emit UTF-8 directly) or, in scripts, `fs.writeFileSync`/`-Encoding utf8`.
  Verify with an ASCII-safe check (codepoints), not by eyeballing console output (which re-mangles it).
- Sources are BOM-less UTF-8 + `<CodePage>65001</CodePage>` (in `Directory.Build.props`) — without it
  csc reads CJK string literals as ANSI mojibake on a CJK-locale machine.

## LLM / router (details in `llm-and-router.md`)

- **A timeout as a single `CancelAfter` over a whole call** — a wall clock that kills a slow-but-alive
  child (a long tool loop, a big prompt) exactly like a dead one. Both the STREAMING path and the BUFFERED
  `ProcessRunner.RunAsync` must use a per-chunk inactivity clock (re-armed on each read); the buffered path
  adds an absolute `maxDuration` backstop and reports `ProcessResult.TimeoutKind`. (Streaming shipped the
  bug in two providers; the buffered path shipped it too — both fixed.)
- **Committing a stream on an empty content chunk** — disables fallback for a zero-content first chunk.
  Gate the commit on `Text.Length > 0`.
- **Hand-rolled verdict heuristics** in a provider — they drift. Always route through
  `LlmVerdictClassifier`. And keep it conservative: a bare word like "unauthorized" or a "429" in a
  stack frame must not trip a verdict that benches a healthy host.
- **Empty provider output as `Ok`** — must be `Failed` (and a terminal `Error` chunk when streaming) so
  the router can fall over.
- The retry in `CompleteJsonAsync` must **differ** from the first attempt (feed back the bad reply + a
  corrective instruction) — re-sending the identical request to a temperature-0 model just repeats it.

## Storage (details in `storage.md`)

- **Missing FTS `'delete'` trigger row on delete/update** — silent index corruption. Three triggers,
  always.
- **A double column read without `CAST(x AS REAL)`** — the SQLite integer-affinity trap.
- **Opening a connection outside the factory** — loses per-connection `foreign_keys=ON`, so cascades
  silently stop.
- **Reusing a migration number** — silently skipped, so the migration never runs. Use
  `dev.mjs new-migration`.
- **`ORDER BY` on a non-unique column with no tiebreaker** — nondeterministic on ties.

## DI / config

- **Calling `AddLyntai` twice** — registers a second `LyntaiOptions` (shadows the first) while both
  calls' providers pile into the collections. It now throws; compose everything in one callback.
- **A singleton capturing a scoped/transient dependency** (captive dependency). Providers/stores are
  singletons; resolve per-call transients (like an `HttpClient` from `IHttpClientFactory`) inside the
  method, not the constructor.
- **A documented option/env-var that isn't wired** — the `LYNTAI_MODEL_<CONSUMER>` override and the OTel
  cost attribute were both documented but silently dropped, and no test caught it. When you add a
  documented knob, add the test that exercises the documented path.

## Testing

- Provider/e2e tests must not hit a live endpoint or spend real tokens — HTTP providers get a stubbed
  `HttpMessageHandler`; CLI providers and the Playground get `provider-stub.mjs` via
  `LYNTAI_PROVIDER_CMD`. Extend the stub's prompt-marker behavior when a test needs a new deterministic
  output.
- 221 passing tests didn't catch any of the LLM/contract bugs above — **tests passing ≠ correct.** For
  load-bearing semantics (fallback, streaming, verdicts, FTS sync) reason about the code, then add the
  test that would have caught the bug.
